using System;

namespace P1bStudy.Framework
{
    /// <summary>
    /// Coefficients copied from the standalone AdvancedIntegrators prototype
    /// (Blanes-Moan SRKN14A, order 6).
    /// </summary>
    internal static class AdvancedCoefficients
    {
        public static readonly double[] SrknA14A = { 0.0378593198406116, 0.102635633102435, -0.0258678882665587, 0.314241403071447, -0.130144459517415, 0.106417700369543, -0.00879424312851058, 0.2073050690568954, -0.00879424312851058, 0.106417700369543, -0.130144459517415, 0.314241403071447, -0.0258678882665587, 0.102635633102435, 0.0378593198406116 };
        public static readonly double[] SrknB14A = { 0.0, 0.09171915262446165, 0.183983170005006, -0.05653436583288827, 0.004914688774712854, 0.143761127168358, 0.328567693746804, -0.19641146648645422, -0.19641146648645422, 0.328567693746804, 0.143761127168358, 0.004914688774712854, -0.05653436583288827, 0.183983170005006, 0.09171915262446165 };
    }

    /// <summary>
    /// Adaptive RKN 12(10) (Dormand, El-Mikkawy, Prince 1987, "RKN12(10)17M") for y'' = a(t, y).
    /// Velocity-independent forces only; extras (mass) must have a zero derivative.
    /// </summary>
    internal sealed class Rkn1210Stepper : IAdaptiveStepper
    {
        private static readonly double[] C = { 0.0, 0.02, 0.04, 0.1, 0.13333333333333333, 0.16, 0.05, 0.2, 0.25, 0.3333333333333333, 0.5, 0.5555555555555556, 0.75, 0.8571428571428571, 0.9452162222720143, 1.0, 1.0 };
        private static readonly double[] B = { 0.012127868517185414, 0.0, 0.0, 0.0, 0.0, 0.0, 0.08629746251568875, 0.2525469581187147, -0.1974186799326823, 0.2031869190789726, -0.020775808077714918, 0.10967804874502014, 0.038065132526466504, 0.01163406880432423, 0.0046580297040248785, 0.0, 0.0 };
        private static readonly double[] BP = { 0.012127868517185414, 0.0, 0.0, 0.0, 0.0, 0.0, 0.09083943422704079, 0.3156836976483934, -0.2632249065769097, 0.3047803786184589, -0.041551616155429835, 0.2467756096762953, 0.15226053010586602, 0.08143848163026961, 0.08502571193890811, -0.009155189630077963, 0.025 };
        private static readonly double[] BT = { -0.004880833389821577, 0.0, 0.0, 0.0, 0.0, 0.0, 0.014038126584857338, -0.11947921920803832, 0.20440246507662121, -0.1322681492223791, 0.11053069299761689, -0.07975385787102851, 0.011224330486437457, -0.004671596801593694, 0.0008580413473282841, 0.0, 0.0 };
        private static readonly double[] BPT = { -0.004880833389821577, 0.0, 0.0, 0.0, 0.0, 0.0, 0.014776975352481408, -0.1493490240100479, 0.2725366201021616, -0.19840222383356862, 0.22106138599523378, -0.17944618020981415, 0.04489732194574983, -0.03270117761115586, 0.015662325288859434, -0.029155189630077964, 0.025 };
        private static readonly double[][] A = new double[][]
        {
            new double[] { },
            new double[] { 0.0002 },
            new double[] { 0.0002666666666666667, 0.0005333333333333334 },
            new double[] { 0.002916666666666667, -0.004166666666666667, 0.00625 },
            new double[] { 0.0016460905349794238, 0.0, 0.0054869684499314125, 0.0017558299039780521 },
            new double[] { 0.0019456, 0.0, 0.007151746031746032, 0.0029127111111111113, 0.0007899428571428571 },
            new double[] { 0.00056640625, 0.0, 0.0008809730489417989, -0.0004369212962962963, 0.00033900669642857143, -9.946469907407407e-05 },
            new double[] { 0.0030833333333333333, 0.0, 0.0, 0.0017777777777777779, 0.0027, 0.0015782828282828283, 0.01086060606060606 },
            new double[] { 0.00365183937480113, 0.0, 0.003965171714072343, 0.0031972582629306284, 0.008221467306855435, -0.0013130926959572379, 0.009771586968064868, 0.003755769069232834 },
            new double[] { 0.003707241068718501, 0.0, 0.005082045854555286, 0.001174708002175412, -0.021147629915126993, 0.06010463698107881, 0.02010573476850619, -0.02835075012293358, 0.014879568918581932 },
            new double[] { 0.03512537656073344, 0.0, -0.008615749195138479, -0.005791448051007917, 1.945554823782616, -3.4351238674565137, -0.10930701107475221, 2.3496383118995166, -0.7560094086870229, 0.10952897222156927 },
            new double[] { 0.020527792537482496, 0.0, -0.00728644676448018, -0.0021153556079618403, 0.9275807968723522, -1.6522824844257367, -0.02107956300568657, 1.2065364326207872, -0.4137144770010661, 0.09079873982809654, 0.005355552600533985 },
            new double[] { -0.14324078875545515, 0.0, 0.012528703773091817, 0.006826019163969827, -4.799555395574387, 5.6986250439519415, 0.7553430369523645, -0.12755487858281084, -1.9605926051117384, 0.9185609056635262, -0.23880085505284432, 0.15911081357234216 },
            new double[] { 0.8045019205520489, 0.0, -0.016658527067011247, -0.021415834042629735, 16.827235928962466, -11.172835357176098, -3.377159297226324, -15.243326655360846, 17.179835738215417, -5.437719239823995, 1.3878671618364655, -0.5925827732652812, 0.029603873171297354 },
            new double[] { -0.913296766697358, 0.0, 0.0024112725757805178, 0.01765812269386174, -14.851649779720384, 2.1589708670045757, 3.99791558311788, 28.434151800232232, -25.2593643549416, 7.733878542362238, -1.8913028948478674, 1.0014845070224718, 0.004641199599109052, 0.011218755022148957 },
            new double[] { -0.27519629720559396, 0.0, 0.03661188877915492, 0.009789519688231562, -12.293062345886211, 14.207226453937903, 1.5866476906789537, 2.4577735327595946, -8.935193694403273, 4.373672731613407, -1.8347181765449492, 1.1592085289061491, -0.017290253165383924, 0.019325977904460768, 0.005204442937554993 },
            new double[] { 1.3076391847404059, 0.0, 0.017364109189745843, -0.018544456454265796, 14.811522032867726, 9.38317630848247, -5.2284261999445425, -48.95128052584765, 38.297096034337926, -10.58738133697598, 2.4332304376226275, -1.0453406042575444, 0.0717732095086726, 0.0021622109708082783, 0.007009595759602514, 0.0 },
        };

        private readonly StudyProblem p;
        private readonly StudyContext ctx;
        private readonly double rtol;
        private readonly double atol;
        private readonly int kdim;
        private readonly int dim;
        private readonly int[][] blocks;
        private readonly double[][] k;
        private readonly double[] ds;
        private readonly double[] st;
        private readonly double[] qn;
        private readonly double[] vn;
        private readonly double[] eq;
        private readonly double[] ev;

        public Rkn1210Stepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            this.p = p;
            this.ctx = ctx;
            rtol = cfg.Rtol;
            atol = cfg.Atol;
            kdim = p.PosDim;
            dim = p.Dim;
            blocks = p.GetBlocks();
            k = new double[17][];
            for (int i = 0; i < k.Length; i++)
            {
                k[i] = new double[kdim];
            }

            ds = new double[dim];
            st = new double[dim];
            qn = new double[kdim];
            vn = new double[kdim];
            eq = new double[kdim];
            ev = new double[kdim];
        }

        public int ErrorOrderExponent
        {
            get { return 11; }
        }

        private void Eval(double t, double[] state, double[] acceleration)
        {
            ctx.Rhs(t, state, ds);
            for (int j = 0; j < kdim; j++)
            {
                acceleration[j] = ds[kdim + j];
            }

            for (int j = 2 * kdim; j < dim; j++)
            {
                if (ds[j] != 0d)
                {
                    throw new InvalidOperationException(
                        "RKN12(10): the extra state (mass) has a non-zero derivative; use IAS15 or DOP853.");
                }
            }
        }

        public void Begin(double t, double[] y)
        {
            Eval(t, y, k[0]);
        }

        public double TryStep(double t, double h, double[] y, double[] yNew)
        {
            Array.Copy(y, st, dim);
            for (int i = 1; i < 17; i++)
            {
                double ci = C[i];
                double[] ai = A[i];
                for (int j = 0; j < kdim; j++)
                {
                    double sum = 0d;
                    for (int m = 0; m < i; m++)
                    {
                        sum += ai[m] * k[m][j];
                    }

                    st[j] = y[j] + (h * ((ci * y[kdim + j]) + (h * sum)));
                }

                Eval(t + (h * ci), st, k[i]);
            }

            for (int j = 0; j < kdim; j++)
            {
                double sb = 0d;
                double sbp = 0d;
                double sbt = 0d;
                double sbpt = 0d;
                for (int i = 0; i < 17; i++)
                {
                    double kij = k[i][j];
                    sb += B[i] * kij;
                    sbp += BP[i] * kij;
                    sbt += BT[i] * kij;
                    sbpt += BPT[i] * kij;
                }

                qn[j] = y[j] + (h * y[kdim + j]) + (h * h * sb);
                vn[j] = y[kdim + j] + (h * sbp);
                eq[j] = h * h * sbt;
                ev[j] = h * sbpt;
            }

            for (int j = 0; j < kdim; j++)
            {
                yNew[j] = qn[j];
                yNew[kdim + j] = vn[j];
            }

            for (int j = 2 * kdim; j < dim; j++)
            {
                yNew[j] = y[j];
            }

            return ErrorNorm(y);
        }

        private double ErrorNorm(double[] y)
        {
            double sum = 0d;
            int count = 0;
            for (int b = 0; b < blocks.Length; b++)
            {
                int start = blocks[b][0];
                int len = blocks[b][1];
                if (start >= 2 * kdim)
                {
                    continue;
                }

                double blockScale = p.AdaptiveScales != null && b < p.AdaptiveScales.Length
                    ? p.AdaptiveScales[b]
                    : 0d;
                double blockAtol = p.AdaptiveAtol != null && b < p.AdaptiveAtol.Length
                    ? p.AdaptiveAtol[b]
                    : 0d;
                for (int c = start; c < start + len && c < 2 * kdim; c++)
                {
                    bool position = c < kdim;
                    int j = position ? c : c - kdim;
                    double reference = blockScale > 0d
                        ? blockScale
                        : Math.Max(Math.Abs(y[c]), Math.Abs(position ? qn[j] : vn[j]));
                    double scale = atol + blockAtol + (rtol * reference);
                    if (scale < 1e-300d)
                    {
                        scale = 1e-300d;
                    }

                    double componentError = position ? eq[j] : ev[j];
                    sum += (componentError / scale) * (componentError / scale);
                    count++;
                }
            }

            return count == 0 ? 0d : Math.Sqrt(sum / count);
        }

        public void Accept(double tNew, double[] yNew)
        {
            Eval(tNew, yNew, k[0]);
        }
    }

    /// <summary>Fixed-step Blanes-Moan SRKN14A (order 6, ABA composition).</summary>
    internal sealed class Srkn14AStepper : IFixedStepper
    {
        private readonly StudyContext ctx;
        private readonly int kdim;
        private readonly double[] ds;

        public Srkn14AStepper(StudyProblem p, StudyContext ctx)
        {
            this.ctx = ctx;
            kdim = p.PosDim;
            ds = new double[p.Dim];
        }

        public void Step(double t, double h, double[] y)
        {
            double[] a = AdvancedCoefficients.SrknA14A;
            double[] b = AdvancedCoefficients.SrknB14A;
            for (int m = 0; m < a.Length; m++)
            {
                if (b[m] != 0d)
                {
                    ctx.Rhs(t, y, ds);
                    double kh = b[m] * h;
                    for (int j = 0; j < kdim; j++)
                    {
                        y[kdim + j] += kh * ds[kdim + j];
                    }
                }

                if (a[m] != 0d)
                {
                    double dh = a[m] * h;
                    for (int j = 0; j < kdim; j++)
                    {
                        y[j] += dh * y[kdim + j];
                    }

                    t += dh;
                }
            }
        }

        public void Flush(double[] y)
        {
        }
    }

    /// <summary>
    /// Blanes-Moan 14A composition on the Kepler split: kick by the perturbation
    /// (total acceleration minus the point-mass term), then exact Kepler drift.
    /// </summary>
    internal sealed class KeplerSplit14AStepper : IFixedStepper
    {
        private readonly StudyContext ctx;
        private readonly double mu;
        private readonly int kdim;
        private readonly double[] ds;
        private readonly double[] perturbation;
        private bool failed;

        public KeplerSplit14AStepper(StudyProblem p, StudyContext ctx)
        {
            this.ctx = ctx;
            mu = p.CentralMu;
            kdim = p.PosDim;
            ds = new double[p.Dim];
            perturbation = new double[kdim];
        }

        public void Step(double t, double h, double[] y)
        {
            if (failed)
            {
                return;
            }

            double[] a = AdvancedCoefficients.SrknA14A;
            double[] b = AdvancedCoefficients.SrknB14A;
            for (int m = 0; m < a.Length; m++)
            {
                if (b[m] != 0d)
                {
                    ctx.Rhs(t, y, ds);
                    double r2 = (y[0] * y[0]) + (y[1] * y[1]) + (y[2] * y[2]);
                    double kk = -mu / (r2 * Math.Sqrt(r2));
                    double kh = b[m] * h;
                    for (int j = 0; j < kdim; j++)
                    {
                        perturbation[j] = ds[kdim + j] - (kk * y[j]);
                        y[kdim + j] += kh * perturbation[j];
                    }
                }

                if (a[m] != 0d)
                {
                    double dh = a[m] * h;
                    try
                    {
                        KeplerUniversal.Propagate(mu, y, 0, kdim, dh);
                    }
                    catch (InvalidOperationException ex)
                    {
                        ctx.Fail("Kepler drift failed at t="
                            + t.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                            + " s, h=" + dh.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                            + " s: " + ex.Message);
                        for (int j = 0; j < y.Length; j++)
                        {
                            y[j] = double.NaN;
                        }

                        failed = true;
                        return;
                    }

                    ctx.CountAux(1L);
                    t += dh;
                }
            }
        }

        public void Flush(double[] y)
        {
        }
    }

    /// <summary>
    /// Gauss-Jackson (summed Stormer-Cowell) predictor-corrector of order q (8 by default).
    /// Coefficients are generated from Delta(x) = x/(-ln(1-x)), Gamma(x) = Delta(x)^2.
    /// Start-up values on the first q intervals come from IAS15; after that the fixed step
    /// must stay constant, otherwise the history is rebuilt.
    /// </summary>
    internal sealed class GaussJacksonStepper : IFixedStepper
    {
        private const int CorrectorPasses = 2;

        private readonly StudyContext ctx;
        private readonly int q;
        private readonly int kdim;
        private readonly int n;
        private readonly double[] delta;
        private readonly double[] gamma;
        private readonly double[] rho;
        private readonly double[] tau;
        private readonly double[] corrPos;
        private readonly double[] corrVel;
        private readonly Ias15Core startup;
        private readonly double[][] history;
        private readonly double[] ds;
        private readonly double[] full;
        private readonly double[] yNew;
        private readonly double[] vNew;
        private readonly double[] fNew;
        private readonly double[][] d;
        private readonly double[] s1;
        private readonly double[] s2;
        private readonly double[] dn;
        private int historyCount;
        private double historyStep;
        private bool startupFresh = true;
        private bool initialized;
        private bool failed;

        public GaussJacksonStepper(StudyProblem p, StudyContext ctx, int order)
        {
            this.ctx = ctx;
            q = order;
            kdim = p.PosDim;
            n = p.Dim;
            int len = q + 4;
            double[] l = new double[len];
            for (int j = 0; j < len; j++)
            {
                l[j] = 1d / (j + 1d);
            }

            delta = new double[len];
            delta[0] = 1d;
            for (int k = 1; k < len; k++)
            {
                double sum = 0d;
                for (int j = 1; j <= k; j++)
                {
                    sum += l[j] * delta[k - j];
                }

                delta[k] = -sum;
            }

            gamma = new double[len];
            for (int k = 0; k < len; k++)
            {
                double sum = 0d;
                for (int j = 0; j <= k; j++)
                {
                    sum += delta[j] * delta[k - j];
                }

                gamma[k] = sum;
            }

            corrPos = new double[q + 1];
            corrVel = new double[q + 1];
            for (int j = 0; j <= q; j++)
            {
                corrPos[j] = gamma[j + 2];
                corrVel[j] = j == 0 ? 1d + delta[1] : delta[j + 1];
            }

            rho = new double[q + 1];
            tau = new double[q + 1];
            double cumulativePos = 0d;
            double cumulativeVel = 0d;
            for (int j = 0; j <= q; j++)
            {
                cumulativePos += corrPos[j];
                cumulativeVel += corrVel[j];
                rho[j] = cumulativePos;
                tau[j] = cumulativeVel;
            }

            startup = new Ias15Core(kdim, n - (2 * kdim));
            startup.Epsilon = 1e-10d;
            history = new double[q + 1][];
            for (int i = 0; i < history.Length; i++)
            {
                history[i] = new double[kdim];
            }

            ds = new double[n];
            full = new double[n];
            yNew = new double[kdim];
            vNew = new double[kdim];
            fNew = new double[kdim];
            d = new double[q + 1][];
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = new double[kdim];
            }

            s1 = new double[kdim];
            s2 = new double[kdim];
            dn = new double[q + 1];
        }

        private void Acc(double t, double[] state)
        {
            ctx.Rhs(t, state, ds);
            for (int j = 0; j < kdim; j++)
            {
                fNew[j] = ds[kdim + j];
            }

            for (int j = 2 * kdim; j < n; j++)
            {
                if (ds[j] != 0d)
                {
                    throw new InvalidOperationException(
                        "Gauss-Jackson: the extra state (mass) has a non-zero derivative; use IAS15 or DOP853.");
                }
            }
        }

        private void PushHistory(double[] acceleration)
        {
            if (historyCount < history.Length)
            {
                Array.Copy(acceleration, history[historyCount], kdim);
                historyCount++;
                return;
            }

            for (int i = 1; i < history.Length; i++)
            {
                double[] swap = history[i - 1];
                history[i - 1] = history[i];
                history[i] = swap;
            }

            Array.Copy(acceleration, history[history.Length - 1], kdim);
        }

        private void ClearHistory()
        {
            historyCount = 0;
            initialized = false;
            startupFresh = true;
        }

        private void InitDifferences(double h, double[] y)
        {
            var column = new double[q + 1];
            for (int c = 0; c < kdim; c++)
            {
                for (int kk = 0; kk <= q; kk++)
                {
                    column[kk] = history[kk][c];
                }

                for (int j = 0; j <= q; j++)
                {
                    d[j][c] = column[q];
                    for (int kk = q; kk > j; kk--)
                    {
                        column[kk] = column[kk] - column[kk - 1];
                    }
                }
            }

            for (int c = 0; c < kdim; c++)
            {
                double velocitySum = 0d;
                for (int k2 = 1; k2 <= q + 1; k2++)
                {
                    velocitySum += delta[k2] * d[k2 - 1][c];
                }

                s1[c] = (y[kdim + c] / h) - velocitySum;
                double positionSum = 0d;
                for (int k2 = 2; k2 <= q + 2; k2++)
                {
                    positionSum += gamma[k2] * d[k2 - 2][c];
                }

                s2[c] = (y[c] / (h * h)) + s1[c] - positionSum;
            }

            initialized = true;
        }

        private void GjStep(double t, double h, double[] y)
        {
            double tn = t + h;
            for (int c = 0; c < kdim; c++)
            {
                double positionSum = 0d;
                double velocitySum = 0d;
                for (int j = 0; j <= q; j++)
                {
                    positionSum += rho[j] * d[j][c];
                    velocitySum += tau[j] * d[j][c];
                }

                yNew[c] = h * h * (s2[c] + positionSum);
                vNew[c] = h * (s1[c] + velocitySum);
            }

            for (int pass = 0; pass < CorrectorPasses; pass++)
            {
                Array.Copy(y, full, n);
                for (int c = 0; c < kdim; c++)
                {
                    full[c] = yNew[c];
                    full[kdim + c] = vNew[c];
                }

                Acc(tn, full);
                for (int c = 0; c < kdim; c++)
                {
                    dn[0] = fNew[c];
                    for (int j = 1; j <= q; j++)
                    {
                        dn[j] = dn[j - 1] - d[j - 1][c];
                    }

                    double positionSum = 0d;
                    double velocitySum = 0d;
                    for (int j = 0; j <= q; j++)
                    {
                        positionSum += corrPos[j] * dn[j];
                        velocitySum += corrVel[j] * dn[j];
                    }

                    yNew[c] = h * h * (s2[c] + positionSum);
                    vNew[c] = h * (s1[c] + velocitySum);
                }
            }

            for (int c = 0; c < kdim; c++)
            {
                dn[0] = fNew[c];
                for (int j = 1; j <= q; j++)
                {
                    dn[j] = dn[j - 1] - d[j - 1][c];
                }

                for (int j = 0; j <= q; j++)
                {
                    d[j][c] = dn[j];
                }

                s1[c] += fNew[c];
                s2[c] += s1[c];
                y[c] = yNew[c];
                y[kdim + c] = vNew[c];
            }

            PushHistory(fNew);
        }

        public void Step(double t, double h, double[] y)
        {
            if (failed)
            {
                return;
            }

            if (historyCount > 0 && Math.Abs(h - historyStep) > 1e-9d * Math.Max(1d, Math.Abs(h)))
            {
                ClearHistory();
            }

            if (historyCount == 0)
            {
                historyStep = h;
                Acc(t, y);
                PushHistory(fNew);
            }

            if (initialized)
            {
                GjStep(t, h, y);
                return;
            }

            if (historyCount >= q + 1)
            {
                InitDifferences(h, y);
                GjStep(t, h, y);
                return;
            }

            try
            {
                startup.IntegrateFixed(ctx.Rhs, y, t, t + h, startupFresh);
                startupFresh = false;
            }
            catch (InvalidOperationException ex)
            {
                ctx.Fail("Gauss-Jackson start-up failed at t="
                    + t.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                    + " s: " + ex.Message);
                for (int i = 0; i < y.Length; i++)
                {
                    y[i] = double.NaN;
                }

                failed = true;
                return;
            }

            Acc(t + h, y);
            PushHistory(fNew);
        }

        public void Flush(double[] y)
        {
        }
    }

    /// <summary>
    /// Encke rectification: the point-mass motion is exact (universal Kepler), the deviation
    /// is integrated by IAS15. Works with velocity-dependent forces and mass flow; needs a
    /// single central body at the origin (PosDim == 3).
    /// </summary>
    internal sealed class EnckeStepper : IFixedStepper
    {
        private readonly StudyContext ctx;
        private readonly double mu;
        private readonly int n;
        private readonly Ias15Core inner;
        private readonly double[] refState = new double[6];
        private readonly double[] kep = new double[6];
        private readonly double[] full;
        private readonly double[] ds;
        private readonly double[] deviation;
        private readonly bool conservative;
        private bool failed;

        public EnckeStepper(StudyProblem p, StudyContext ctx)
        {
            this.ctx = ctx;
            mu = p.CentralMu;
            n = p.Dim;
            conservative = p.ConservativeSecondOrder;
            inner = new Ias15Core(3, n - 6);
            inner.Epsilon = 1e-9d;
            full = new double[n];
            ds = new double[n];
            deviation = new double[n];
        }

        public void Step(double t, double h, double[] y)
        {
            if (failed)
            {
                return;
            }

            for (int j = 0; j < 6; j++)
            {
                refState[j] = y[j];
            }

            Array.Clear(deviation, 0, n);
            for (int j = 6; j < n; j++)
            {
                deviation[j] = y[j];
            }

            if (conservative && PerturbationNegligible(t, y, ds))
            {
                Finish(h, y, false);
                return;
            }

            double tStart = t;
            Ias15Rhs rhs = delegate (double tau, double[] yy, double[] dy)
            {
                for (int j = 0; j < 6; j++)
                {
                    kep[j] = refState[j];
                }

                KeplerUniversal.Propagate(mu, kep, 0, 3, tau - tStart);
                ctx.CountAux(1L);
                for (int j = 0; j < 3; j++)
                {
                    full[j] = kep[j] + yy[j];
                    full[3 + j] = kep[3 + j] + yy[3 + j];
                }

                for (int j = 6; j < n; j++)
                {
                    full[j] = yy[j];
                }

                ctx.Rhs(tau, full, ds);
                double rx = full[0];
                double ry = full[1];
                double rz = full[2];
                double r2 = (rx * rx) + (ry * ry) + (rz * rz);
                double r1 = Math.Sqrt(r2);
                double kr3 = -mu / (r2 * r1);
                double dx = yy[0];
                double dyv = yy[1];
                double dz = yy[2];
                double qv = ((dx * (dx - (2d * rx))) + (dyv * (dyv - (2d * ry))) + (dz * (dz - (2d * rz)))) / r2;
                double f = -qv * ((3d + (3d * qv) + (qv * qv)) / (1d + Math.Pow(1d + qv, 1.5d)));
                double rk2 = (kep[0] * kep[0]) + (kep[1] * kep[1]) + (kep[2] * kep[2]);
                double mrk3 = -mu / (rk2 * Math.Sqrt(rk2));
                dy[0] = yy[3];
                dy[1] = yy[4];
                dy[2] = yy[5];
                dy[3] = (ds[3] - (kr3 * rx)) + (mrk3 * (dx - (f * rx)));
                dy[4] = (ds[4] - (kr3 * ry)) + (mrk3 * (dyv - (f * ry)));
                dy[5] = (ds[5] - (kr3 * rz)) + (mrk3 * (dz - (f * rz)));
                for (int j = 6; j < n; j++)
                {
                    dy[j] = ds[j];
                }
            };

            try
            {
                inner.IntegrateFixed(rhs, deviation, t, t + h, true);
                Finish(h, y, true);
            }
            catch (InvalidOperationException ex)
            {
                // A pure Kepler arc leaves only round-off in the perturbation. The
                // inner IAS15 then sees a noise-level force and can grind to its step
                // floor; the physically correct deviation is zero, so keep the exact
                // Kepler flow instead of failing the run.
                if (conservative && DeviationNegligible())
                {
                    Finish(h, y, false);
                    return;
                }

                ctx.Fail("Encke inner IAS15 failed at t="
                    + t.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                    + " s: " + ex.Message);
                for (int j = 0; j < y.Length; j++)
                {
                    y[j] = double.NaN;
                }

                failed = true;
            }
        }

        /// <summary>True when the full force equals the central Kepler term to round-off.</summary>
        private bool PerturbationNegligible(double t, double[] y, double[] total)
        {
            ctx.Rhs(t, y, total);
            double ax = total[3];
            double ay = total[4];
            double az = total[5];
            double r2 = (y[0] * y[0]) + (y[1] * y[1]) + (y[2] * y[2]);
            double k = -mu / (r2 * Math.Sqrt(r2));
            double px = ax - (k * y[0]);
            double py = ay - (k * y[1]);
            double pz = az - (k * y[2]);
            double total2 = (ax * ax) + (ay * ay) + (az * az);
            double perturbation2 = (px * px) + (py * py) + (pz * pz);
            return total2 == 0d || perturbation2 <= 1e-24d * total2;
        }

        private bool DeviationNegligible()
        {
            double scale = 1d;
            for (int j = 0; j < 6; j++)
            {
                scale = Math.Max(scale, Math.Abs(refState[j]));
            }

            double deviationMax = 0d;
            for (int j = 0; j < 6; j++)
            {
                deviationMax = Math.Max(deviationMax, Math.Abs(deviation[j]));
            }

            return deviationMax <= 1e-9d * scale;
        }

        private void Finish(double h, double[] y, bool useDeviation)
        {
            for (int j = 0; j < 6; j++)
            {
                kep[j] = refState[j];
            }

            KeplerUniversal.Propagate(mu, kep, 0, 3, h);
            ctx.CountAux(1L);
            for (int j = 0; j < 6; j++)
            {
                y[j] = kep[j] + (useDeviation ? deviation[j] : 0d);
            }

            if (useDeviation)
            {
                for (int j = 6; j < n; j++)
                {
                    y[j] = deviation[j];
                }
            }
        }

        public void Flush(double[] y)
        {
        }
    }
}
