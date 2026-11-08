using System;

namespace P1bStudy.Framework
{
    /// <summary>RHS used by the IAS15 core; the core itself never counts, the caller wraps it.</summary>
    internal delegate void Ias15Rhs(double t, double[] y, double[] dy);

    /// <summary>
    /// IAS15 (Rein &amp; Spiegel 2015), Gauss-Radau 15th order, ported from the standalone
    /// AdvancedIntegrators prototype. State layout is [q(dim), v(dim), extras(nx)], the same
    /// as StudyProblem. Handles velocity-dependent forces and mass flow.
    ///
    /// The core is shared by the adaptive IAS15 stepper and by the Encke / Gauss-Jackson
    /// start-up, which need to integrate a short interval without the sweep's step control.
    /// </summary>
    internal sealed class Ias15Core
    {
        private static readonly double[] H = { 0.0, 0.0562625605369221464656521910318, 0.180240691736892364987579942780, 0.352624717113169637373907769648, 0.547153626330555383001448554766, 0.734210177215410531523210605558, 0.885320946839095768090359771030, 0.977520613561287501891174488626 };
        private static readonly double[] RR = { 0.0562625605369221464656522, 0.1802406917368923649875799, 0.1239781311999702185219278, 0.3526247171131696373739078, 0.2963621565762474909082556, 0.1723840253762772723863278, 0.5471536263305553830014486, 0.4908910657936332365357964, 0.3669129345936630180138686, 0.1945289092173857456275408, 0.7342101772154105315232106, 0.6779476166784883850575584, 0.5539694854785181665356307, 0.3815854601022408941493028, 0.1870565508848551485217621, 0.8853209468390957680903598, 0.8290583863021736216247076, 0.7050802551022034031027798, 0.5326962297259261307164520, 0.3381673205085403850889112, 0.1511107696236852365671492, 0.9775206135612875018911745, 0.9212580530243653554255223, 0.7972799218243951369035945, 0.6248958964481178645172667, 0.4303669872307321188897259, 0.2433104363458769703679639, 0.0921996667221917338008147 };
        private static readonly double[] C = { -0.0562625605369221464656522, 0.0101408028300636299864818, -0.2365032522738145114532321, -0.0035758977292516175949345, 0.0935376952594620658957485, -0.5891279693869841488271399, 0.0019565654099472210769006, -0.0547553868890686864408084, 0.4158812000823068616886219, -1.1362815957175395318285885, -0.0014365302363708915424460, 0.0421585277212687077072973, -0.3600995965020568122897665, 1.2501507118406910258505441, -1.8704917729329500633517991, 0.0012717903090268677492943, -0.0387603579159067703699046, 0.3609622434528459832253398, -1.4668842084004269643701553, 2.9061362593084293014237913, -2.7558127197720458314421588 };
        private static readonly double[] D = { 0.0562625605369221464656522, 0.0031654757181708292499905, 0.2365032522738145114532321, 0.0001780977692217433881125, 0.0457929855060279188954539, 0.5891279693869841488271399, 0.0000100202365223291272096, 0.0084318571535257015445000, 0.2535340690545692665214616, 1.1362815957175395318285885, 0.0000005637641639318207610, 0.0015297840025004658189490, 0.0978342365324440053653648, 0.8752546646840910912297246, 1.8704917729329500633517991, 0.0000000317188154017613665, 0.0002762930909826476593130, 0.0360285539837364596003871, 0.5767330002770787313544596, 2.2485887607691597933926895, 2.7558127197720458314421588 };
        private const double Safety = 0.25d;
        private const double ControllerExponent = 7d;

        public double Epsilon = 1e-9d;
        public double Dt;
        public long Steps;
        public long Rejected;

        private readonly int dim;
        private readonly int nx;
        private readonly int nc;
        private readonly double[][] b;
        private readonly double[][] e;
        private readonly double[][] br;
        private readonly double[][] er;
        private readonly double[][] g;
        private readonly double[][] csb;
        private readonly double[] csx;
        private readonly double[] csu;
        private readonly double[] x0;
        private readonly double[] u0;
        private readonly double[] a0;
        private readonly double[] at;
        private readonly double[] st;
        private readonly double[] ds;
        private readonly double[] yInter;
        private double dtLastDone;

        public Ias15Core(int dim, int nx)
        {
            if (dim < 1 || nx < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dim));
            }

            this.dim = dim;
            this.nx = nx;
            nc = dim + nx;
            b = New7(nc);
            e = New7(nc);
            br = New7(nc);
            er = New7(nc);
            g = New7(nc);
            csb = New7(nc);
            csx = new double[dim];
            csu = new double[nc];
            x0 = new double[dim];
            u0 = new double[nc];
            a0 = new double[nc];
            at = new double[nc];
            st = new double[dim + nc];
            ds = new double[dim + nc];
            yInter = new double[dim + nc];
        }

        private static double[][] New7(int width)
        {
            var result = new double[7][];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new double[width];
            }

            return result;
        }

        public int StateLength
        {
            get { return dim + nc; }
        }

        public void Reset()
        {
            for (int p = 0; p < 7; p++)
            {
                for (int k = 0; k < nc; k++)
                {
                    b[p][k] = 0d;
                    e[p][k] = 0d;
                    br[p][k] = 0d;
                    er[p][k] = 0d;
                }
            }

            for (int k = 0; k < dim; k++)
            {
                csx[k] = 0d;
            }

            for (int k = 0; k < nc; k++)
            {
                csu[k] = 0d;
            }

            dtLastDone = 0d;
            Dt = 0d;
            Steps = 0L;
            Rejected = 0L;
        }

        public void Begin(double[] state)
        {
            if (state.Length != StateLength)
            {
                throw new ArgumentException("IAS15: state length mismatch");
            }

            Reset();
            for (int k = 0; k < dim; k++)
            {
                x0[k] = state[k];
            }

            for (int k = 0; k < nc; k++)
            {
                u0[k] = state[dim + k];
            }
        }

        private void Eval(Ias15Rhs rhs, double t)
        {
            rhs(t, st, ds);
            for (int k = 0; k < nc; k++)
            {
                at[k] = ds[dim + k];
            }
        }

        private static void AddCompensated(ref double value, ref double compensation, double increment)
        {
            double y = increment - compensation;
            double t = value + y;
            compensation = (t - value) - y;
            value = t;
        }

        private void Predict(double ratio, double[][] targetE, double[][] targetB)
        {
            if (ratio > 20d)
            {
                for (int p = 0; p < 7; p++)
                {
                    for (int k = 0; k < nc; k++)
                    {
                        targetE[p][k] = 0d;
                        targetB[p][k] = 0d;
                    }
                }

                return;
            }

            double q1 = ratio;
            double q2 = q1 * q1;
            double q3 = q1 * q2;
            double q4 = q2 * q2;
            double q5 = q2 * q3;
            double q6 = q3 * q3;
            double q7 = q3 * q4;
            for (int k = 0; k < nc; k++)
            {
                double b0 = targetB[0][k];
                double b1 = targetB[1][k];
                double b2 = targetB[2][k];
                double b3 = targetB[3][k];
                double b4 = targetB[4][k];
                double b5 = targetB[5][k];
                double b6 = targetB[6][k];
                double be0 = b0 - targetE[0][k];
                double be1 = b1 - targetE[1][k];
                double be2 = b2 - targetE[2][k];
                double be3 = b3 - targetE[3][k];
                double be4 = b4 - targetE[4][k];
                double be5 = b5 - targetE[5][k];
                double be6 = b6 - targetE[6][k];
                targetE[0][k] = q1 * ((b6 * 7d) + (b5 * 6d) + (b4 * 5d) + (b3 * 4d) + (b2 * 3d) + (b1 * 2d) + b0);
                targetE[1][k] = q2 * ((b6 * 21d) + (b5 * 15d) + (b4 * 10d) + (b3 * 6d) + (b2 * 3d) + b1);
                targetE[2][k] = q3 * ((b6 * 35d) + (b5 * 20d) + (b4 * 10d) + (b3 * 4d) + b2);
                targetE[3][k] = q4 * ((b6 * 35d) + (b5 * 15d) + (b4 * 5d) + b3);
                targetE[4][k] = q5 * ((b6 * 21d) + (b5 * 6d) + b4);
                targetE[5][k] = q6 * ((b6 * 7d) + b5);
                targetE[6][k] = q7 * b6;
                targetB[0][k] = targetE[0][k] + be0;
                targetB[1][k] = targetE[1][k] + be1;
                targetB[2][k] = targetE[2][k] + be2;
                targetB[3][k] = targetE[3][k] + be3;
                targetB[4][k] = targetE[4][k] + be4;
                targetB[5][k] = targetE[5][k] + be5;
                targetB[6][k] = targetE[6][k] + be6;
            }
        }

        /// <summary>
        /// One trial step. Returns the scaled error norm: &lt;= 1 means accepted and the internal
        /// state has already advanced; &gt; 1 means rejected and the internal state is unchanged.
        /// The returned value is chosen so the shared adaptive driver picks exactly the dtNew
        /// estimated by IAS15 (fac = 0.9 * err^(-1/7) = dtNew / dt).
        /// </summary>
        public double TryStep(Ias15Rhs rhs, double t, double dt, out double dtNew, double[] yOut)
        {
            if (yOut == null || yOut.Length != StateLength)
            {
                throw new ArgumentException("IAS15: output state length mismatch");
            }

            for (int k = 0; k < dim; k++)
            {
                st[k] = x0[k];
            }

            for (int k = 0; k < nc; k++)
            {
                st[dim + k] = u0[k];
            }

            Eval(rhs, t);
            for (int k = 0; k < nc; k++)
            {
                a0[k] = at[k];
            }

            for (int p = 0; p < 7; p++)
            {
                for (int k = 0; k < nc; k++)
                {
                    csb[p][k] = 0d;
                }
            }

            for (int k = 0; k < nc; k++)
            {
                double b0 = b[0][k];
                double b1 = b[1][k];
                double b2 = b[2][k];
                double b3 = b[3][k];
                double b4 = b[4][k];
                double b5 = b[5][k];
                double b6 = b[6][k];
                g[0][k] = (b6 * D[15]) + (b5 * D[10]) + (b4 * D[6]) + (b3 * D[3]) + (b2 * D[1]) + (b1 * D[0]) + b0;
                g[1][k] = (b6 * D[16]) + (b5 * D[11]) + (b4 * D[7]) + (b3 * D[4]) + (b2 * D[2]) + b1;
                g[2][k] = (b6 * D[17]) + (b5 * D[12]) + (b4 * D[8]) + (b3 * D[5]) + b2;
                g[3][k] = (b6 * D[18]) + (b5 * D[13]) + (b4 * D[9]) + b3;
                g[4][k] = (b6 * D[19]) + (b5 * D[14]) + b4;
                g[5][k] = (b6 * D[20]) + b5;
                g[6][k] = b6;
            }

            double pcErr = 1e300d;
            double pcErrLast = 2d;
            int iter = 0;
            while (true)
            {
                if (pcErr < 1e-16d)
                {
                    break;
                }

                if (iter > 2 && pcErrLast <= pcErr)
                {
                    break;
                }

                if (iter >= 12)
                {
                    break;
                }

                pcErrLast = pcErr;
                pcErr = 0d;
                iter++;
                for (int n = 1; n < 8; n++)
                {
                    double hn = H[n];
                    double tn = t + (dt * hn);
                    for (int k = 0; k < dim; k++)
                    {
                        double xk = -csx[k] + ((((((((b[6][k] * 7d * hn / 9d + b[5][k]) * 3d * hn / 4d + b[4][k]) * 5d * hn / 7d + b[3][k]) * 2d * hn / 3d + b[2][k]) * 3d * hn / 5d + b[1][k]) * hn / 2d + b[0][k]) * hn / 3d + a0[k]) * dt * hn / 2d + u0[k]) * dt * hn;
                        st[k] = xk + x0[k];
                    }

                    for (int k = 0; k < nc; k++)
                    {
                        double vk = -csu[k] + (((((((b[6][k] * 7d * hn / 8d + b[5][k]) * 6d * hn / 7d + b[4][k]) * 5d * hn / 6d + b[3][k]) * 4d * hn / 5d + b[2][k]) * 3d * hn / 4d + b[1][k]) * 2d * hn / 3d + b[0][k]) * hn / 2d + a0[k]) * dt * hn;
                        st[dim + k] = vk + u0[k];
                    }

                    Eval(rhs, tn);
                    int m = n - 1;
                    int rb = (n - 1) * n / 2;
                    int cb = (n - 1) * (n - 2) / 2;
                    double maxak = 0d;
                    double maxb6 = 0d;
                    for (int k = 0; k < nc; k++)
                    {
                        double gk = (at[k] - a0[k]) / RR[rb];
                        for (int j = 0; j < n - 1; j++)
                        {
                            gk = (gk - g[j][k]) / RR[rb + 1 + j];
                        }

                        double tmp = gk - g[m][k];
                        g[m][k] = gk;
                        for (int p = 0; p < m; p++)
                        {
                            AddCompensated(ref b[p][k], ref csb[p][k], tmp * C[cb + p]);
                        }

                        AddCompensated(ref b[m][k], ref csb[m][k], tmp);
                        if (n == 7 && k < dim)
                        {
                            double ak = Math.Abs(at[k]);
                            if (ak > maxak && !double.IsInfinity(ak))
                            {
                                maxak = ak;
                            }

                            double bb = Math.Abs(tmp);
                            if (bb > maxb6 && !double.IsInfinity(bb))
                            {
                                maxb6 = bb;
                            }
                        }
                    }

                    if (n == 7)
                    {
                        pcErr = maxak > 0d ? maxb6 / maxak : 0d;
                    }
                }
            }

            double dtDone = dt;
            double a0i = 0d;
            double y2 = 0d;
            double y3 = 0d;
            double y4 = 0d;
            for (int k = 0; k < dim; k++)
            {
                a0i += a0[k] * a0[k];
                double tmp = a0[k] + b[0][k] + b[1][k] + b[2][k] + b[3][k] + b[4][k] + b[5][k] + b[6][k];
                y2 += tmp * tmp;
                tmp = b[0][k] + (2d * b[1][k]) + (3d * b[2][k]) + (4d * b[3][k]) + (5d * b[4][k]) + (6d * b[5][k]) + (7d * b[6][k]);
                y3 += tmp * tmp;
                tmp = (2d * b[1][k]) + (6d * b[2][k]) + (12d * b[3][k]) + (20d * b[4][k]) + (30d * b[5][k]) + (42d * b[6][k]);
                y4 += tmp * tmp;
            }

            double ts2 = 0d;
            bool ok2 = a0i > 0d && !double.IsInfinity(a0i) && !double.IsNaN(a0i);
            if (ok2)
            {
                ts2 = 2d * y2 / (y3 + Math.Sqrt(y4 * y2));
            }

            double epsilon = Math.Max(Epsilon, 1e-16d);
            if (ok2 && ts2 > 0d && !double.IsInfinity(ts2) && !double.IsNaN(ts2))
            {
                dtNew = Math.Sqrt(ts2) * dtDone * Math.Pow(epsilon * 5040d, 1d / 7d);
            }
            else
            {
                dtNew = dtDone / Safety;
            }

            double ratio = Math.Abs(dtNew / dt);
            if (!(ratio > 0d) || double.IsNaN(ratio) || double.IsInfinity(ratio))
            {
                ratio = 0.2d;
            }

            if (ratio > 4d)
            {
                ratio = 4d;
            }

            if (ratio < 0.2d)
            {
                ratio = 0.2d;
            }

            // IAS15 itself accepts any shrink down to Safety (0.25). The shared driver only
            // accepts err <= 1, i.e. hNew >= 0.9 h. In that band accept the current step
            // (as IAS15 does) but let the next step shrink by the driver's 0.9; rejecting
            // repeatedly would collapse the step geometrically on nearly unperturbed arcs.
            bool accepted = ratio >= Safety;
            double nextRatio = accepted && ratio < 0.9d ? 0.9d : ratio;
            dtNew = Math.Sign(dt) * nextRatio * Math.Abs(dt);
            double error = Math.Pow(0.9d / nextRatio, ControllerExponent);
            if (accepted)
            {
                for (int k = 0; k < nc; k++)
                {
                    if (k < dim)
                    {
                        double d2 = dtDone * dtDone;
                        AddCompensated(ref x0[k], ref csx[k], b[6][k] / 72d * d2);
                        AddCompensated(ref x0[k], ref csx[k], b[5][k] / 56d * d2);
                        AddCompensated(ref x0[k], ref csx[k], b[4][k] / 42d * d2);
                        AddCompensated(ref x0[k], ref csx[k], b[3][k] / 30d * d2);
                        AddCompensated(ref x0[k], ref csx[k], b[2][k] / 20d * d2);
                        AddCompensated(ref x0[k], ref csx[k], b[1][k] / 12d * d2);
                        AddCompensated(ref x0[k], ref csx[k], b[0][k] / 6d * d2);
                        AddCompensated(ref x0[k], ref csx[k], a0[k] / 2d * d2);
                        AddCompensated(ref x0[k], ref csx[k], u0[k] * dtDone);
                    }

                    AddCompensated(ref u0[k], ref csu[k], b[6][k] / 8d * dtDone);
                    AddCompensated(ref u0[k], ref csu[k], b[5][k] / 7d * dtDone);
                    AddCompensated(ref u0[k], ref csu[k], b[4][k] / 6d * dtDone);
                    AddCompensated(ref u0[k], ref csu[k], b[3][k] / 5d * dtDone);
                    AddCompensated(ref u0[k], ref csu[k], b[2][k] / 4d * dtDone);
                    AddCompensated(ref u0[k], ref csu[k], b[1][k] / 3d * dtDone);
                    AddCompensated(ref u0[k], ref csu[k], b[0][k] / 2d * dtDone);
                    AddCompensated(ref u0[k], ref csu[k], a0[k] * dtDone);
                }

                for (int p = 0; p < 7; p++)
                {
                    for (int k = 0; k < nc; k++)
                    {
                        er[p][k] = e[p][k];
                        br[p][k] = b[p][k];
                    }
                }

                Predict(nextRatio, e, b);
                dtLastDone = dtDone;
                Steps++;
                for (int k = 0; k < dim; k++)
                {
                    yOut[k] = x0[k];
                }

                for (int k = 0; k < nc; k++)
                {
                    yOut[dim + k] = u0[k];
                }

                return error;
            }

            Rejected++;
            if (dtLastDone != 0d)
            {
                Predict(dtNew / dtLastDone, er, br);
            }
            else
            {
                for (int p = 0; p < 7; p++)
                {
                    for (int k = 0; k < nc; k++)
                    {
                        b[p][k] = 0d;
                        e[p][k] = 0d;
                    }
                }
            }

            return error;
        }

        /// <summary>
        /// Integrates [t0, t1] with the method's own step control. Used for Encke rectification
        /// intervals and for Gauss-Jackson start-up, where the sweep does not drive the steps.
        /// With reset = false the core continues from its current state (the caller must pass
        /// the same state the core already holds).
        /// </summary>
        public void IntegrateFixed(Ias15Rhs rhs, double[] state, double t0, double t1, bool reset)
        {
            if (state.Length != StateLength)
            {
                throw new ArgumentException("IAS15: state length mismatch");
            }

            if (reset)
            {
                Begin(state);
            }

            double dir = t1 >= t0 ? 1d : -1d;
            double t = t0;
            if (Dt == 0d)
            {
                for (int k = 0; k < dim; k++)
                {
                    st[k] = x0[k];
                }

                for (int k = 0; k < nc; k++)
                {
                    st[dim + k] = u0[k];
                }

                Eval(rhs, t);
                double aa = 0d;
                double vv = 0d;
                for (int k = 0; k < dim; k++)
                {
                    aa += at[k] * at[k];
                    vv += u0[k] * u0[k];
                }

                double d = aa > 0d && vv > 0d
                    ? 0.01d * Math.Sqrt(vv / aa)
                    : Math.Abs(t1 - t0) / 1000d;
                Dt = dir * Math.Min(d, Math.Abs(t1 - t0));
            }
            else
            {
                Dt = Math.Abs(Dt) * dir;
            }

            double minStep = 1e-14d * Math.Max(1d, Math.Abs(t1 - t0));
            int guard = 0;
            while ((t1 - t) * dir > 0d)
            {
                if (++guard > 100000000)
                {
                    throw new InvalidOperationException("IAS15: too many steps");
                }

                double dtTry = Dt;
                bool clipped = Math.Abs(dtTry) >= Math.Abs(t1 - t);
                if (clipped)
                {
                    dtTry = t1 - t;
                }

                double dtNext;
                double error = TryStep(rhs, t, dtTry, out dtNext, yInter);
                if (error <= 1d)
                {
                    t = clipped ? t1 : t + dtTry;
                    Dt = dtNext;
                }
                else
                {
                    Dt = dtNext;
                    if (!clipped && Math.Abs(Dt) < minStep && Math.Abs(t1 - t) > minStep)
                    {
                        throw new InvalidOperationException(
                            "IAS15: step floor reached at t=" + t.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }

            for (int k = 0; k < dim; k++)
            {
                state[k] = x0[k];
            }

            for (int k = 0; k < nc; k++)
            {
                state[dim + k] = u0[k];
            }
        }
    }

    /// <summary>Adaptive IAS15 driven by the common sweep controller.</summary>
    internal sealed class Ias15Stepper : IAdaptiveStepper
    {
        private readonly Ias15Core core;
        private readonly StudyContext ctx;
        private readonly double[] yNew;

        public Ias15Stepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            this.ctx = ctx;
            core = new Ias15Core(p.PosDim, p.Dim - (2 * p.PosDim));
            core.Epsilon = Math.Max(cfg.Rtol, 1e-16d);
            yNew = new double[p.Dim];
        }

        public int ErrorOrderExponent
        {
            get { return 7; }
        }

        public void Begin(double t, double[] y)
        {
            core.Begin(y);
        }

        public double TryStep(double t, double h, double[] y, double[] yNewOut)
        {
            double dtNew;
            double error = core.TryStep(ctx.Rhs, t, h, out dtNew, yNew);
            if (error <= 1d)
            {
                Array.Copy(yNew, yNewOut, yNewOut.Length);
            }

            return error;
        }

        public void Accept(double tNew, double[] yNewState)
        {
        }
    }
}
