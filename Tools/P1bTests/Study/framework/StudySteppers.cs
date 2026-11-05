using System;

namespace P1bStudy.Framework
{
    /// <summary>Classical RK4, 4 RHS per step, no cache.</summary>
    internal sealed class Rk4Stepper : IFixedStepper
    {
        private readonly StudyContext ctx;
        private readonly int n;
        private readonly double[] k1;
        private readonly double[] k2;
        private readonly double[] k3;
        private readonly double[] k4;
        private readonly double[] tmp;

        public Rk4Stepper(StudyProblem p, StudyContext ctx)
        {
            this.ctx = ctx;
            n = p.Dim;
            k1 = new double[n];
            k2 = new double[n];
            k3 = new double[n];
            k4 = new double[n];
            tmp = new double[n];
        }

        public void Step(double t, double h, double[] y)
        {
            ctx.Rhs(t, y, k1);
            for (int i = 0; i < n; i++)
            {
                tmp[i] = y[i] + (0.5d * h * k1[i]);
            }

            ctx.Rhs(t + (0.5d * h), tmp, k2);
            for (int i = 0; i < n; i++)
            {
                tmp[i] = y[i] + (0.5d * h * k2[i]);
            }

            ctx.Rhs(t + (0.5d * h), tmp, k3);
            for (int i = 0; i < n; i++)
            {
                tmp[i] = y[i] + (h * k3[i]);
            }

            ctx.Rhs(t + h, tmp, k4);
            for (int i = 0; i < n; i++)
            {
                y[i] += h / 6d * (k1[i] + (2d * k2[i]) + (2d * k3[i]) + k4[i]);
            }
        }

        public void Flush(double[] y)
        {
        }
    }

    /// <summary>
    /// Explicit RK with FSAL for any RkTableau. Cost per attempted step:
    /// Stages-1 RHS, plus the FSAL evaluation that is done only if the error estimate needs it
    /// (DOPRI5) or after acceptance (DOP853, whose estimator does not use the FSAL stage).
    /// So a rejected DOP853 step costs 11 RHS, an accepted one 12, and a rejection never
    /// forces k1 to be recomputed. StudySelfTest pins these counts.
    /// </summary>
    internal sealed class ExplicitRkStepper : IAdaptiveStepper
    {
        private readonly RkTableau tab;
        private readonly StudyProblem p;
        private readonly StudyContext ctx;
        private readonly double rtol;
        private readonly double atol;
        private readonly bool computeError;
        private readonly bool errNeedsFsal;
        private readonly int dim;
        private readonly double[][] k;
        private readonly double[] ytmp;
        private readonly int[][] blocks;
        private bool fsalPending;

        public ExplicitRkStepper(RkTableau tab, StudyProblem p, StudyConfig cfg, StudyContext ctx, bool computeError)
        {
            this.tab = tab;
            this.p = p;
            this.ctx = ctx;
            this.computeError = computeError;
            rtol = cfg.Rtol;
            atol = cfg.Atol;
            dim = p.Dim;
            blocks = p.GetBlocks();
            k = new double[tab.Stages + 1][];
            for (int i = 0; i < k.Length; i++)
            {
                k[i] = new double[dim];
            }

            ytmp = new double[dim];
            errNeedsFsal = tab.E5[tab.Stages] != 0d
                || (tab.E3 != null && tab.E3[tab.Stages] != 0d);
        }

        public int ErrorOrderExponent
        {
            get { return tab.ErrorExponent; }
        }

        public void Begin(double t, double[] y)
        {
            ctx.Rhs(t, y, k[0]);
            fsalPending = false;
        }

        public double TryStep(double t, double h, double[] y, double[] yNew)
        {
            int s = tab.Stages;
            for (int i = 1; i < s; i++)
            {
                double[] ai = tab.A[i];
                for (int c = 0; c < dim; c++)
                {
                    double sum = 0d;
                    for (int j = 0; j < i; j++)
                    {
                        sum += ai[j] * k[j][c];
                    }

                    ytmp[c] = y[c] + (h * sum);
                }

                ctx.Rhs(t + (tab.C[i] * h), ytmp, k[i]);
            }

            for (int c = 0; c < dim; c++)
            {
                double sum = 0d;
                for (int j = 0; j < s; j++)
                {
                    sum += tab.B[j] * k[j][c];
                }

                yNew[c] = y[c] + (h * sum);
            }

            fsalPending = true;
            if (!computeError)
            {
                return 0d;
            }

            if (errNeedsFsal)
            {
                ctx.Rhs(t + h, yNew, k[s]);
                fsalPending = false;
            }

            return ErrorNorm(h, y, yNew);
        }

        public void Accept(double tNew, double[] yNew)
        {
            int s = tab.Stages;
            if (fsalPending)
            {
                ctx.Rhs(tNew, yNew, k[s]);
                fsalPending = false;
            }

            double[] swap = k[0];
            k[0] = k[s];
            k[s] = swap;
        }

        private double ErrorNorm(double h, double[] y, double[] yNew)
        {
            int s = tab.Stages;
            int last = errNeedsFsal ? s : s - 1;
            int nb = blocks.Length;
            double sum5 = 0d;
            double sum3 = 0d;
            for (int b = 0; b < nb; b++)
            {
                int start = blocks[b][0];
                int len = blocks[b][1];
                double ny = 0d;
                double nyn = 0d;
                double e5 = 0d;
                double e3 = 0d;
                for (int c = start; c < start + len; c++)
                {
                    double a5 = 0d;
                    double a3 = 0d;
                    for (int j = 0; j <= last; j++)
                    {
                        double kj = k[j][c];
                        a5 += tab.E5[j] * kj;
                        if (tab.E3 != null)
                        {
                            a3 += tab.E3[j] * kj;
                        }
                    }

                    e5 += a5 * a5;
                    e3 += a3 * a3;
                    ny += y[c] * y[c];
                    nyn += yNew[c] * yNew[c];
                }

                double ab = atol + (p.AdaptiveAtol != null ? p.AdaptiveAtol[b] : 0d);
                double sc = ab + (rtol * Math.Max(Math.Sqrt(ny), Math.Sqrt(nyn)));
                if (sc < 1e-300d)
                {
                    sc = 1e-300d;
                }

                sum5 += e5 / (sc * sc);
                sum3 += e3 / (sc * sc);
            }

            if (double.IsNaN(sum5) || double.IsNaN(sum3))
            {
                return double.NaN;
            }

            if (tab.E3 == null)
            {
                // classical estimator: RMS over blocks
                return Math.Abs(h) * Math.Sqrt(sum5 / nb);
            }

            // Hairer's DOP853 estimator: err5^2 / sqrt(err5^2 + 0.01 err3^2), scaled by h.
            double deno = sum5 + (0.01d * sum3);
            if (!(deno > 0d))
            {
                return 0d;
            }

            return Math.Abs(h) * sum5 / Math.Sqrt(deno * nb);
        }
    }

    /// <summary>Fixed-step use of an RK tableau (no error control, FSAL still saves one RHS per step).</summary>
    internal sealed class FixedRkStepper : IFixedStepper
    {
        private readonly ExplicitRkStepper inner;
        private readonly double[] ynew;
        private bool began;

        public FixedRkStepper(RkTableau tab, StudyProblem p, StudyContext ctx)
        {
            var cfg = new StudyConfig();
            inner = new ExplicitRkStepper(tab, p, cfg, ctx, false);
            ynew = new double[p.Dim];
        }

        public void Step(double t, double h, double[] y)
        {
            if (!began)
            {
                inner.Begin(t, y);
                began = true;
            }

            inner.TryStep(t, h, y, ynew);
            inner.Accept(t + h, ynew);
            Array.Copy(ynew, y, ynew.Length);
        }

        public void Flush(double[] y)
        {
        }
    }

    internal static class StudyCoefficients
    {
        // Yoshida 6th order, solution A: w1..w3 as published, w0 closes the sum to 1.
        public static readonly double[] Y6 = BuildY6();

        public static readonly double[] Y6Cumulative = Cumulative(Y6);

        private static double[] BuildY6()
        {
            const double w1 = 0.78451361047755726382d;
            const double w2 = 0.23557321335935813368d;
            const double w3 = -1.17767998417887100695d;
            double w0 = 1d - (2d * (w1 + w2 + w3));
            return new[] { w1, w2, w3, w0, w3, w2, w1 };
        }

        private static double[] Cumulative(double[] w)
        {
            var c = new double[w.Length + 1];
            for (int i = 0; i < w.Length; i++)
            {
                c[i + 1] = c[i] + w[i];
            }

            c[w.Length] = 1d;
            return c;
        }
    }

    /// <summary>
    /// Omelyan PEFRL (4th order). 4 RHS per step; the closing drift is not followed by a
    /// force evaluation. Velocity-independent forces only.
    /// </summary>
    internal sealed class PefrlStepper : IFixedStepper
    {
        private const double Xi = 0.1786178958448091d;
        private const double Lam = -0.2123418310626054d;
        private const double Chi = -0.06626458266981849d;
        private readonly StudyContext ctx;
        private readonly int kdim;
        private readonly double[] ds;

        public PefrlStepper(StudyProblem p, StudyContext ctx)
        {
            this.ctx = ctx;
            kdim = p.PosDim;
            ds = new double[p.Dim];
        }

        private void Drift(double[] y, double dt)
        {
            for (int i = 0; i < kdim; i++)
            {
                y[i] += dt * y[kdim + i];
            }
        }

        private void Kick(double t, double[] y, double dt)
        {
            ctx.Rhs(t, y, ds);
            for (int i = 0; i < kdim; i++)
            {
                y[kdim + i] += dt * ds[kdim + i];
            }
        }

        public void Step(double t, double h, double[] y)
        {
            Drift(y, Xi * h);
            Kick(t + (Xi * h), y, (1d - (2d * Lam)) * h / 2d);
            Drift(y, Chi * h);
            Kick(t + ((Xi + Chi) * h), y, Lam * h);
            Drift(y, (1d - (2d * (Chi + Xi))) * h);
            Kick(t + ((1d - Chi - Xi) * h), y, Lam * h);
            Drift(y, Chi * h);
            Kick(t + ((1d - Xi) * h), y, (1d - (2d * Lam)) * h / 2d);
            Drift(y, Xi * h);
        }

        public void Flush(double[] y)
        {
        }
    }

    /// <summary>
    /// Yoshida 6th order as 7 kick-drift-kick sub-steps; the closing force of one sub-step is
    /// the opening force of the next, so a step costs 7 RHS (+1 once at the start).
    /// compensated = Neumaier summation of the position/velocity updates (Y6C).
    /// </summary>
    internal sealed class Yoshida6Stepper : IFixedStepper
    {
        private readonly StudyContext ctx;
        private readonly int kdim;
        private readonly double[] ds;
        private readonly double[] acc;
        private readonly double[] comp;
        private readonly double[] eval;
        private bool have;

        public Yoshida6Stepper(StudyProblem p, StudyContext ctx, bool compensated)
        {
            this.ctx = ctx;
            kdim = p.PosDim;
            ds = new double[p.Dim];
            acc = new double[kdim];
            if (compensated)
            {
                comp = new double[p.Dim];
                eval = new double[p.Dim];
            }
        }

        private void Add(double[] y, int i, double inc)
        {
            if (comp == null)
            {
                y[i] += inc;
                return;
            }

            double s = y[i];
            double t = s + inc;
            if (Math.Abs(s) >= Math.Abs(inc))
            {
                comp[i] += (s - t) + inc;
            }
            else
            {
                comp[i] += (inc - t) + s;
            }

            y[i] = t;
        }

        private void EvalAcc(double t, double[] y)
        {
            if (comp == null)
            {
                ctx.Rhs(t, y, ds);
            }
            else
            {
                for (int i = 0; i < y.Length; i++)
                {
                    eval[i] = y[i] + comp[i];
                }

                ctx.Rhs(t, eval, ds);
            }

            for (int i = 0; i < kdim; i++)
            {
                acc[i] = ds[kdim + i];
            }
        }

        public void Step(double t, double h, double[] y)
        {
            if (!have)
            {
                EvalAcc(t, y);
                have = true;
            }

            double[] w = StudyCoefficients.Y6;
            double[] cw = StudyCoefficients.Y6Cumulative;
            for (int k = 0; k < w.Length; k++)
            {
                double dt = w[k] * h;
                for (int i = 0; i < kdim; i++)
                {
                    Add(y, kdim + i, 0.5d * dt * acc[i]);
                }

                for (int i = 0; i < kdim; i++)
                {
                    double v = y[kdim + i] + (comp == null ? 0d : comp[kdim + i]);
                    Add(y, i, dt * v);
                }

                EvalAcc(t + (cw[k + 1] * h), y);
                for (int i = 0; i < kdim; i++)
                {
                    Add(y, kdim + i, 0.5d * dt * acc[i]);
                }
            }
        }

        public void Flush(double[] y)
        {
            if (comp == null)
            {
                return;
            }

            for (int i = 0; i < y.Length; i++)
            {
                y[i] += comp[i];
                comp[i] = 0d;
            }
        }
    }

    /// <summary>
    /// Wisdom-Holman-style Kepler split with Yoshida-6 weights: half kick of the perturbation
    /// (total acceleration minus the central Kepler term), exact Kepler drift
    /// (KeplerUniversal), half kick. 7 RHS + 7 Kepler solves per step. Needs a central body at the
    /// origin of x[0..2] and a velocity-independent force. Works for unbound orbits too.
    /// Note: the perturbation is a difference of two nearly equal accelerations, so its
    /// round-off is ~eps*|a_total| per kick; supply the perturbation directly from the
    /// problem if that ever becomes the limit.
    /// </summary>
    internal sealed class KeplerSplitStepper : IFixedStepper
    {
        private readonly StudyContext ctx;
        private readonly double mu;
        private readonly double[] ds;
        private readonly double[] pert = new double[3];
        private bool have;

        public KeplerSplitStepper(StudyProblem p, StudyContext ctx)
        {
            this.ctx = ctx;
            mu = p.CentralMu;
            ds = new double[p.Dim];
        }

        private void EvalPert(double t, double[] y)
        {
            ctx.Rhs(t, y, ds);
            double r2 = (y[0] * y[0]) + (y[1] * y[1]) + (y[2] * y[2]);
            double r3 = r2 * Math.Sqrt(r2);
            for (int i = 0; i < 3; i++)
            {
                pert[i] = ds[3 + i] + (mu * y[i] / r3);
            }
        }

        public void Step(double t, double h, double[] y)
        {
            if (!have)
            {
                EvalPert(t, y);
                have = true;
            }

            double[] w = StudyCoefficients.Y6;
            double[] cw = StudyCoefficients.Y6Cumulative;
            for (int k = 0; k < w.Length; k++)
            {
                double dt = w[k] * h;
                for (int i = 0; i < 3; i++)
                {
                    y[3 + i] += 0.5d * dt * pert[i];
                }

                KeplerUniversal.Propagate(mu, y, 0, 3, dt);
                ctx.CountAux(1);
                EvalPert(t + (cw[k + 1] * h), y);
                for (int i = 0; i < 3; i++)
                {
                    y[3 + i] += 0.5d * dt * pert[i];
                }
            }
        }

        public void Flush(double[] y)
        {
        }
    }

    /// <summary>Velocity Verlet (2nd order), 1 RHS per step (+1 at the start). Used as the "how to add a method" example.</summary>
    internal sealed class LeapfrogStepper : IFixedStepper
    {
        private readonly StudyContext ctx;
        private readonly int kdim;
        private readonly double[] ds;
        private readonly double[] acc;
        private bool have;

        public LeapfrogStepper(StudyProblem p, StudyContext ctx)
        {
            this.ctx = ctx;
            kdim = p.PosDim;
            ds = new double[p.Dim];
            acc = new double[kdim];
        }

        private void EvalAcc(double t, double[] y)
        {
            ctx.Rhs(t, y, ds);
            for (int i = 0; i < kdim; i++)
            {
                acc[i] = ds[kdim + i];
            }
        }

        public void Step(double t, double h, double[] y)
        {
            if (!have)
            {
                EvalAcc(t, y);
                have = true;
            }

            for (int i = 0; i < kdim; i++)
            {
                y[kdim + i] += 0.5d * h * acc[i];
            }

            for (int i = 0; i < kdim; i++)
            {
                y[i] += h * y[kdim + i];
            }

            EvalAcc(t + h, y);
            for (int i = 0; i < kdim; i++)
            {
                y[kdim + i] += 0.5d * h * acc[i];
            }
        }

        public void Flush(double[] y)
        {
        }
    }
}
