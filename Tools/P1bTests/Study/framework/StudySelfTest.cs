using System;
using System.Collections.Generic;
using System.Globalization;

namespace P1bStudy.Framework
{
    internal sealed class StudyCheck
    {
        public string Name;
        public bool Passed;
        public string Detail;
    }

    /// <summary>
    /// Engine-independent tests of the framework itself, on a Kepler orbit (mu = 1, e = 0.2, a = 1)
    /// whose exact solution is known. Run these first: if one fails, no sweep result can be trusted.
    /// What they pin: tableau consistency, observed order of every fixed-step method, the exact
    /// RHS-call formula of every method (so cost-accounting bugs cannot creep back in), the
    /// Kepler propagator, the budget guard, stop/breakpoint merging, the advanced ports
    /// (RKN12(10), IAS15, SRKN14A, WH-BM14A, GJ8, Encke-IAS15) and a full sweep smoke run.
    /// </summary>
    internal static class StudySelfTest
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const double Ecc = 0.2d;
        private const double Period = 2d * Math.PI;

        public static List<StudyCheck> RunAll()
        {
            var checks = new List<StudyCheck>();
            TableauChecks(checks);
            KeplerPropagatorChecks(checks);
            StopChecks(checks);
            BudgetCheck(checks);
            StudyProblem p = MakeKeplerProblem(8, 2d);
            FixedOrderChecks(checks, p);
            CallCountChecks(checks, p);
            AdaptiveChecks(checks, p);
            AdaptiveScaleChecks(checks);
            AbortReasonChecks(checks);
            RoundoffFloorChecks(checks);
            CapLabelChecks(checks);
            AdvancedIntegratorChecks(checks);
            SweepSmoke(checks);
            PlateauSweepChecks(checks);
            return checks;
        }

        // ---------------------------------------------------------------------------
        // Problem: Kepler orbit with an extra constant "mass" component (Dim = 7)
        // ---------------------------------------------------------------------------

        private static void Analytic(double e, double t, double[] y)
        {
            double m = t;
            double ea = m;
            for (int i = 0; i < 80; i++)
            {
                double d = (ea - (e * Math.Sin(ea)) - m) / (1d - (e * Math.Cos(ea)));
                ea -= d;
                if (Math.Abs(d) < 1e-16d)
                {
                    break;
                }
            }

            double r = 1d - (e * Math.Cos(ea));
            double sq = Math.Sqrt(1d - (e * e));
            y[0] = Math.Cos(ea) - e;
            y[1] = sq * Math.Sin(ea);
            y[2] = 0d;
            y[3] = -Math.Sin(ea) / r;
            y[4] = sq * Math.Cos(ea) / r;
            y[5] = 0d;
            y[6] = 1d;
        }

        public static StudyProblem MakeKeplerProblem(int checkpoints, double periods)
        {
            return MakeKeplerProblemEcc(Ecc, checkpoints, periods);
        }

        private static StudyProblem MakeKeplerProblemEcc(double e, int checkpoints, double periods)
        {
            var p = new StudyProblem();
            p.Label = "selftest Kepler e=" + e.ToString("G3", Inv);
            p.Dim = 7;
            p.PosDim = 3;
            p.ConservativeSecondOrder = true;
            p.CentralMu = 1d;
            p.T0 = 0d;
            p.Y0 = new double[7];
            Analytic(e, 0d, p.Y0);
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                double r2 = (y[0] * y[0]) + (y[1] * y[1]) + (y[2] * y[2]);
                double r3 = r2 * Math.Sqrt(r2);
                dy[0] = y[3];
                dy[1] = y[4];
                dy[2] = y[5];
                dy[3] = -y[0] / r3;
                dy[4] = -y[1] / r3;
                dy[5] = -y[2] / r3;
                dy[6] = 0d;
            };
            p.CheckpointTimes = new double[checkpoints];
            p.CheckpointRef = new double[checkpoints][];
            for (int k = 0; k < checkpoints; k++)
            {
                double t = periods * Period * (k + 1) / checkpoints;
                p.CheckpointTimes[k] = t;
                p.CheckpointRef[k] = new double[7];
                Analytic(e, t, p.CheckpointRef[k]);
            }

            p.Floor = 1e-13d;
            p.Certified = true;
            p.CharTime = Period;
            p.ScaleLength = 1d;
            return p;
        }

        private static void Add(List<StudyCheck> list, string name, bool ok, string detail)
        {
            var c = new StudyCheck();
            c.Name = name;
            c.Passed = ok;
            c.Detail = detail;
            list.Add(c);
        }

        private static string E(double v)
        {
            return v.ToString("E2", Inv);
        }

        private static StudyRun RunDirect(IStudyIntegrator integ, StudyProblem p, double control, double maxStep, out StudyContext ctx)
        {
            StudyConfig cfg = integ.MakeConfig(p, control, maxStep);
            ctx = new StudyContext(p, long.MaxValue / 4L);
            return integ.Run(p, cfg, ctx);
        }

        private static double MaxErr(StudyProblem p, StudyRun run)
        {
            double worst = 0d;
            for (int k = 0; k < p.CheckpointTimes.Length; k++)
            {
                if (run.Samples[k] == null)
                {
                    return double.NaN;
                }

                double e = p.Error(run.Samples[k], p.CheckpointRef[k]);
                if (double.IsNaN(e))
                {
                    return double.NaN;
                }

                worst = Math.Max(worst, e);
            }

            return worst;
        }

        // ---------------------------------------------------------------------------
        // Checks
        // ---------------------------------------------------------------------------

        private static void TableauChecks(List<StudyCheck> list)
        {
            foreach (RkTableau t in new[] { RkTableau.Dopri5, RkTableau.Dop853 })
            {
                double rowErr = 0d;
                for (int i = 0; i < t.Stages; i++)
                {
                    double s = 0d;
                    for (int j = 0; j < t.A[i].Length; j++)
                    {
                        s += t.A[i][j];
                    }

                    rowErr = Math.Max(rowErr, Math.Abs(s - t.C[i]));
                }

                double sb = 0d;
                foreach (double b in t.B)
                {
                    sb += b;
                }

                double se5 = 0d;
                foreach (double e5 in t.E5)
                {
                    se5 += e5;
                }

                double se3 = 0d;
                if (t.E3 != null)
                {
                    foreach (double e3 in t.E3)
                    {
                        se3 += e3;
                    }
                }

                double worst = Math.Max(Math.Max(rowErr, Math.Abs(sb - 1d)), Math.Max(Math.Abs(se5), Math.Abs(se3)));
                Add(list, t.Name + " tableau consistent", worst < 1e-14d,
                    "max(|sum A_i - c_i|, |sum b - 1|, |sum E|) = " + E(worst));

                // order conditions sum b_j c_j^k = 1/(k+1) up to the method's order
                int maxK = t.Name == "DOP853" ? 7 : 4;
                double orderErr = 0d;
                for (int k = 0; k <= maxK; k++)
                {
                    double s = 0d;
                    for (int j = 0; j < t.Stages; j++)
                    {
                        s += t.B[j] * Math.Pow(t.C[j], k);
                    }

                    orderErr = Math.Max(orderErr, Math.Abs(s - (1d / (k + 1))));
                }

                Add(list, t.Name + " quadrature order conditions", orderErr < 1e-13d,
                    "max |sum b c^k - 1/(k+1)| for k<=" + maxK.ToString(Inv) + " = " + E(orderErr));
            }
        }

        private static double Energy(double[] y)
        {
            double r = Math.Sqrt((y[0] * y[0]) + (y[1] * y[1]) + (y[2] * y[2]));
            return (0.5d * ((y[3] * y[3]) + (y[4] * y[4]) + (y[5] * y[5]))) - (1d / r);
        }

        private static void KeplerPropagatorChecks(List<StudyCheck> list)
        {
            // against the analytic solution
            double[] y = new double[7];
            Analytic(Ecc, 0d, y);
            KeplerUniversal.Propagate(1d, y, 0, 3, Period / 3d);
            double[] exact = new double[7];
            Analytic(Ecc, Period / 3d, exact);
            double worst = 0d;
            for (int i = 0; i < 6; i++)
            {
                worst = Math.Max(worst, Math.Abs(y[i] - exact[i]));
            }

            Add(list, "Kepler propagator vs analytic (e=0.2, dt=P/3)", worst < 1e-12d, "max component error " + E(worst));

            // hyperbola: reversibility and energy
            double[] h = new double[] { 1d, 0d, 0d, 0d, 1.6d, 0.2d, 1d };
            double[] h0 = (double[])h.Clone();
            double e0 = Energy(h0);
            KeplerUniversal.Propagate(1d, h, 0, 3, 2d);
            double e1 = Energy(h);
            KeplerUniversal.Propagate(1d, h, 0, 3, -2d);
            double back = 0d;
            for (int i = 0; i < 6; i++)
            {
                back = Math.Max(back, Math.Abs(h[i] - h0[i]));
            }

            Add(list, "Kepler propagator hyperbola reversible", back < 1e-11d, "return error " + E(back));
            Add(list, "Kepler propagator hyperbola energy", Math.Abs(e1 - e0) < 1e-12d * Math.Max(1d, Math.Abs(e0)),
                "|dE| = " + E(Math.Abs(e1 - e0)));

            // round-off must not accumulate with the number of sub-steps (the old element-based drift did)
            double[] c = new double[] { 1d, 0d, 0d, 0d, 1d, 0d, 1d };
            const int n = 100000;
            double hstep = Period / 1000d;
            for (int i = 0; i < n; i++)
            {
                KeplerUniversal.Propagate(1d, c, 0, 3, hstep);
            }

            double ang = n * hstep;
            double drift = Math.Sqrt(Math.Pow(c[0] - Math.Cos(ang), 2d) + Math.Pow(c[1] - Math.Sin(ang), 2d));
            Add(list, "Kepler propagator 1e5 sub-steps, no round-off growth", drift < 1e-9d,
                "position error after " + n.ToString(Inv) + " sub-steps: " + E(drift));

            // GTO-like eccentricity: the ship sweep's large steps live here. After 100
            // full periods the exact state is the initial state again.
            const double eHigh = 0.8d;
            double rp = 1d - eHigh;
            double vp = Math.Sqrt((1d + eHigh) / (1d - eHigh));
            double[] g = { rp, 0d, 0d, 0d, vp, 0d, 1d };
            double[] g0 = (double[])g.Clone();
            double energy0 = Energy(g);
            const int nHigh = 1000;
            for (int i = 0; i < nHigh; i++)
            {
                KeplerUniversal.Propagate(1d, g, 0, 3, Period / 10d);
            }

            double driftHigh = 0d;
            for (int i = 0; i < 6; i++)
            {
                driftHigh = Math.Max(driftHigh, Math.Abs(g[i] - g0[i]));
            }

            double energyDrift = Math.Abs(Energy(g) - energy0);
            // The along-track (phase) error accumulates ~1e-11 per periapsis crossing:
            // the universal-variable update loses a few ulp where the orbit is fastest.
            // Energy, the quantity a symplectic split relies on, stays at round-off.
            Add(list, "Kepler propagator e=0.8, 100 periods: energy stable, phase drift bounded",
                energyDrift < 1e-12d && driftHigh < 1e-8d,
                "position/velocity drift " + E(driftHigh) + ", |dE| " + E(energyDrift));
        }

        private static void StopChecks(List<StudyCheck> list)
        {
            StudyProblem p = MakeKeplerProblem(4, 1d);
            p.Breakpoints = new[] { 1.0d, p.CheckpointTimes[1], -5d, p.EndTime + 1d };
            StudyStops s = StudyStops.Build(p);
            bool sorted = true;
            int recorded = 0;
            for (int i = 0; i < s.Times.Length; i++)
            {
                if (i > 0 && !(s.Times[i] > s.Times[i - 1]))
                {
                    sorted = false;
                }

                if (s.CheckpointIndex[i] >= 0)
                {
                    recorded++;
                }
            }

            Add(list, "stops merge checkpoints and breakpoints", sorted && recorded == 4 && s.Times.Length == 5,
                "stops=" + s.Times.Length.ToString(Inv) + " recorded=" + recorded.ToString(Inv));
        }

        private static void BudgetCheck(List<StudyCheck> list)
        {
            StudyProblem p = MakeKeplerProblem(2, 1d);
            var ctx = new StudyContext(p, 5L);
            bool thrown = false;
            double[] dy = new double[7];
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    ctx.Rhs(0d, p.Y0, dy);
                }
            }
            catch (StudyBudgetExceeded)
            {
                thrown = true;
            }

            Add(list, "RHS budget guard", thrown && ctx.Calls == 6L, "calls=" + ctx.Calls.ToString(Inv));
        }

        private static double Median(List<double> v)
        {
            v.Sort();
            return v[v.Count / 2];
        }

        private static void FixedOrderChecks(List<StudyCheck> list, StudyProblem p)
        {
            var methods = new List<IStudyIntegrator>
            {
                new Rk4Integrator(), new Dopri5FixedIntegrator(), new PefrlIntegrator(),
                new Yoshida6Integrator(false), new Yoshida6Integrator(true), new LeapfrogIntegrator()
            };
            int[] ns = { 32, 64, 128, 256 };
            foreach (IStudyIntegrator m in methods)
            {
                var errs = new double[ns.Length];
                for (int i = 0; i < ns.Length; i++)
                {
                    StudyContext ctx;
                    StudyRun run = RunDirect(m, p, Period / ns[i], double.PositiveInfinity, out ctx);
                    errs[i] = MaxErr(p, run);
                }

                var slopes = new List<double>();
                for (int i = 0; i + 1 < ns.Length; i++)
                {
                    if (errs[i] > 1e-12d && errs[i + 1] > 1e-12d && errs[i] < 0.1d && errs[i + 1] < errs[i])
                    {
                        slopes.Add(Math.Log(errs[i] / errs[i + 1]) / Math.Log(2d));
                    }
                }

                double expected = m.ExpectedOrder;
                bool ok = slopes.Count >= 2;
                double med = ok ? Median(slopes) : double.NaN;
                ok = ok && med > expected - 0.7d && med < expected + 1.0d;
                Add(list, m.Name + " observed order", ok,
                    "expected " + expected.ToString("F0", Inv) + ", median " + med.ToString("F2", Inv)
                    + " from " + slopes.Count.ToString(Inv) + " pairs; errors " + E(errs[0]) + " .. " + E(errs[ns.Length - 1]));
            }

            // Kepler split is exact for a pure Kepler problem, at any step
            StudyContext c2;
            StudyRun r2 = RunDirect(new KeplerSplitIntegrator(), p, Period / 4d, double.PositiveInfinity, out c2);
            double e2 = MaxErr(p, r2);
            Add(list, "WH-Y6 exact on pure Kepler (h = P/4)", e2 < 1e-9d, "error " + E(e2));

            // The GTO ship case runs WH-Y6 at h up to ~Tau (a large fraction of the
            // periapsis timescale). A pure e=0.8 Kepler orbit must still be exact there;
            // otherwise the universal-variable drift, not the perturbation order, would
            // be responsible for the non-monotonic work-precision curve.
            StudyProblem pec = MakeKeplerProblemEcc(0.8d, 8, 2d);
            StudyContext c3;
            StudyRun r3 = RunDirect(new KeplerSplitIntegrator(), pec, Period / 2d, double.PositiveInfinity, out c3);
            double e3 = MaxErr(pec, r3);
            Add(list, "WH-Y6 exact on pure Kepler e=0.8 (h = P/2)", e3 < 1e-8d, "error " + E(e3));
        }

        private static void CallCountChecks(List<StudyCheck> list, StudyProblem p)
        {
            double h = Period / 64d;
            StudyContext ctx;
            StudyRun run;

            run = RunDirect(new Rk4Integrator(), p, h, double.PositiveInfinity, out ctx);
            Add(list, "RK4 calls = 4*steps", ctx.Calls == 4L * run.Steps, "calls=" + ctx.Calls.ToString(Inv) + " steps=" + run.Steps.ToString(Inv));

            run = RunDirect(new Dopri5FixedIntegrator(), p, h, double.PositiveInfinity, out ctx);
            Add(list, "DOPRI5-fixed calls = 1 + 6*steps", ctx.Calls == 1L + (6L * run.Steps), "calls=" + ctx.Calls.ToString(Inv) + " steps=" + run.Steps.ToString(Inv));

            run = RunDirect(new PefrlIntegrator(), p, h, double.PositiveInfinity, out ctx);
            Add(list, "PEFRL calls = 4*steps", ctx.Calls == 4L * run.Steps, "calls=" + ctx.Calls.ToString(Inv) + " steps=" + run.Steps.ToString(Inv));

            run = RunDirect(new Yoshida6Integrator(false), p, h, double.PositiveInfinity, out ctx);
            Add(list, "Y6 calls = 1 + 7*steps", ctx.Calls == 1L + (7L * run.Steps), "calls=" + ctx.Calls.ToString(Inv) + " steps=" + run.Steps.ToString(Inv));

            run = RunDirect(new Yoshida6Integrator(true), p, h, double.PositiveInfinity, out ctx);
            Add(list, "Y6C calls = 1 + 7*steps", ctx.Calls == 1L + (7L * run.Steps), "calls=" + ctx.Calls.ToString(Inv) + " steps=" + run.Steps.ToString(Inv));

            run = RunDirect(new KeplerSplitIntegrator(), p, h, double.PositiveInfinity, out ctx);
            Add(list, "WH-Y6 calls = 1 + 7*steps, aux = 7*steps",
                ctx.Calls == 1L + (7L * run.Steps) && ctx.AuxOps == 7L * run.Steps,
                "calls=" + ctx.Calls.ToString(Inv) + " aux=" + ctx.AuxOps.ToString(Inv) + " steps=" + run.Steps.ToString(Inv));

            run = RunDirect(new LeapfrogIntegrator(), p, h, double.PositiveInfinity, out ctx);
            Add(list, "Leapfrog calls = 1 + steps", ctx.Calls == 1L + run.Steps, "calls=" + ctx.Calls.ToString(Inv) + " steps=" + run.Steps.ToString(Inv));

            run = RunDirect(new Dopri5AdaptiveIntegrator(), p, 1e-8d, double.PositiveInfinity, out ctx);
            long attempts5 = run.Steps + run.Rejected;
            Add(list, "DOPRI5-adaptive calls = 1 + 6*(accepted+rejected)", ctx.Calls == 1L + (6L * attempts5),
                "calls=" + ctx.Calls.ToString(Inv) + " accepted=" + run.Steps.ToString(Inv) + " rejected=" + run.Rejected.ToString(Inv));

            run = RunDirect(new Dop853AdaptiveIntegrator(), p, 1e-8d, double.PositiveInfinity, out ctx);
            long attempts8 = run.Steps + run.Rejected;
            Add(list, "DOP853-adaptive calls = 1 + 11*(accepted+rejected) + accepted",
                ctx.Calls == 1L + (11L * attempts8) + run.Steps,
                "calls=" + ctx.Calls.ToString(Inv) + " accepted=" + run.Steps.ToString(Inv) + " rejected=" + run.Rejected.ToString(Inv));
        }

        private static void AdaptiveChecks(List<StudyCheck> list, StudyProblem p)
        {
            foreach (IStudyIntegrator m in new IStudyIntegrator[] { new Dopri5AdaptiveIntegrator(), new Dop853AdaptiveIntegrator() })
            {
                double[] tol = { 1e-6d, 1e-8d, 1e-10d };
                var errs = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    StudyContext ctx;
                    StudyRun run = RunDirect(m, p, tol[i], double.PositiveInfinity, out ctx);
                    errs[i] = MaxErr(p, run);
                }

                bool ok = errs[1] < errs[0] && errs[2] < errs[1] && errs[2] < 1e-5d;
                Add(list, m.Name + " error decreases with rtol", ok,
                    "errors " + E(errs[0]) + ", " + E(errs[1]) + ", " + E(errs[2]));
            }

            // the step cap must bind and be reported
            StudyContext c;
            StudyRun capped = RunDirect(new Dop853AdaptiveIntegrator(), p, 1e-4d, 0.05d, out c);
            Add(list, "adaptive step cap is honoured and counted", capped.CapLimited > 0L,
                "cap-limited steps " + capped.CapLimited.ToString(Inv) + " of " + capped.Steps.ToString(Inv));
        }

        /// <summary>
        /// A body whose barycentric position is a large constant offset plus a small dynamical
        /// motion must be controlled at the dynamical scale, not at the offset. This is the
        /// planet-sweep bug in miniature: without AdaptiveScales the controller lets the
        /// oscillation fall far below its own error tolerance and the answer is much worse.
        /// </summary>
        private static void AdaptiveScaleChecks(List<StudyCheck> list)
        {
            const double offsetPos = 1e12d;
            const double offsetVel = 1e5d;
            const double amplitude = 1e3d;
            StudyProblem p = MakeOffsetOscillator(offsetPos, offsetVel, amplitude, 8, 10d);

            StudyContext c1;
            StudyRun raw = RunDirect(new Dopri5AdaptiveIntegrator(), p, 1e-6d, double.PositiveInfinity, out c1);
            double errRaw = MaxErr(p, raw);

            p.AdaptiveScales = new[] { amplitude, amplitude };
            StudyContext c2;
            StudyRun scaled = RunDirect(new Dopri5AdaptiveIntegrator(), p, 1e-6d, double.PositiveInfinity, out c2);
            double errScaled = MaxErr(p, scaled);

            bool ok = scaled.Steps > raw.Steps && errScaled < errRaw;
            Add(list, "adaptive scale uses the local dynamical amplitude, not the offset", ok,
                "raw: steps=" + raw.Steps.ToString(Inv) + " err=" + E(errRaw)
                + "; scaled: steps=" + scaled.Steps.ToString(Inv) + " err=" + E(errScaled));
        }

        private static StudyProblem MakeOffsetOscillator(
            double offsetPos, double offsetVel, double amplitude, int checkpoints, double periods)
        {
            var p = new StudyProblem();
            p.Label = "selftest offset oscillator";
            p.Dim = 6;
            p.PosDim = 3;
            p.T0 = 0d;
            p.Y0 = new[] { offsetPos, 0d, 0d, offsetVel + amplitude, 0d, 0d };
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                dy[0] = y[3] - offsetVel;
                dy[1] = y[4];
                dy[2] = y[5];
                dy[3] = -(y[0] - offsetPos);
                dy[4] = -y[1];
                dy[5] = -y[2];
            };
            p.CheckpointTimes = new double[checkpoints];
            p.CheckpointRef = new double[checkpoints][];
            for (int k = 0; k < checkpoints; k++)
            {
                double t = periods * Period * (k + 1) / checkpoints;
                p.CheckpointTimes[k] = t;
                p.CheckpointRef[k] = new double[6];
                p.CheckpointRef[k][0] = offsetPos + (amplitude * Math.Sin(t));
                p.CheckpointRef[k][3] = offsetVel + (amplitude * Math.Cos(t));
            }

            p.Floor = 0d;
            p.Certified = true;
            p.CharTime = Period;
            p.ScaleLength = amplitude;
            return p;
        }

        /// <summary>
        /// An aborted adaptive run must say why (step floor vs. a diverged physical state),
        /// and that reason must reach the sweep row; "no finite samples" alone is not enough.
        /// </summary>
        private static void AbortReasonChecks(List<StudyCheck> list)
        {
            StudyProblem p = MakeKeplerProblem(1, 1d);
            StudyContext ctx;
            StudyRun floorRun = RunDirect(new NeverConvergesIntegrator(), p, 1e-8d, double.PositiveInfinity, out ctx);
            Add(list, "adaptive step-floor abort carries a reason",
                floorRun.Aborted && floorRun.Abort.IndexOf("step floor", StringComparison.Ordinal) >= 0,
                "aborted=" + floorRun.Aborted.ToString() + " reason=" + floorRun.Abort);

            StudyProblem boxed = MakeKeplerProblem(4, 1d);
            boxed.DomainViolation = delegate (double[] y) { return "left the box"; };
            StudyContext ctx2;
            StudyRun boxRun = RunDirect(new Rk4Integrator(), boxed, boxed.Duration / 8d, double.PositiveInfinity, out ctx2);
            Add(list, "fixed-step domain violation aborts with the physical reason",
                boxRun.Aborted && boxRun.Abort == "left the box",
                "aborted=" + boxRun.Aborted.ToString() + " reason=" + boxRun.Abort);

            var o = new StudyOptions();
            o.RelTargets = new double[0];
            o.AbsTargets = new[] { 1d };
            o.MaxCostRhs = 100000L;
            o.TimingReps = 1;
            StudyScenarioResult res = StudySweepRunner.Run(
                boxed, new List<IStudyIntegrator> { new Rk4Integrator() }, o, null);
            bool noted = false;
            foreach (StudyRow r in res.Rows)
            {
                if (r.Note != null && r.Note.IndexOf("left the box", StringComparison.Ordinal) >= 0)
                {
                    noted = true;
                }
            }

            Add(list, "sweep row reports the abort reason instead of generic non-finite", noted,
                noted ? "reason reached the row" : "no row note contains the reason");
        }

        private static void RoundoffFloorChecks(List<StudyCheck> list)
        {
            var p = new StudyProblem();
            p.Label = "selftest roundoff";
            p.Dim = 3;
            p.PosDim = 0;
            p.T0 = 0d;
            p.Y0 = new double[3];
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                dy[0] = 0d;
                dy[1] = 0d;
                dy[2] = 0d;
            };
            p.CheckpointTimes = new[] { 1d };
            p.CheckpointRef = new[] { new double[3] };
            p.Floor = 0d;
            p.Certified = true;
            p.ScaleLength = 1e12d;
            p.CharTime = 1d;

            var res = new StudyScenarioResult();
            res.Problem = p;
            res.Rows.Add(new StudyRow { Scenario = "t", Integrator = "Naive", Status = "ok", Finite = true, Err = 0.5d, Cost = 100d, Steps = 1000000L });
            res.Rows.Add(new StudyRow { Scenario = "t", Integrator = "Precise", Status = "ok", Finite = true, Err = 0.5d, Cost = 200d, Steps = 100L });

            var o = new StudyOptions();
            o.RelTargets = new double[0];
            o.AbsTargets = new[] { 1d, 10d };
            StudyTarget one = o.Targets(p)[0];
            StudyTarget ten = o.Targets(p)[1];
            StudyCell naiveOne = StudyReport.CellFor(res, "Naive", one, o);
            StudyCell preciseOne = StudyReport.CellFor(res, "Precise", one, o);
            StudyCell naiveTen = StudyReport.CellFor(res, "Naive", ten, o);
            bool unused;
            string win = StudyReport.Winner(res, one, o, out unused);
            double floor = StudyReport.RoundoffFloor(p, res.Rows[0]);
            bool ok = naiveOne.Status == "FLOOR"
                && naiveOne.Note.IndexOf("round-off", StringComparison.OrdinalIgnoreCase) >= 0
                && (preciseOne.Status == "OK" || preciseOne.Status == "EDGE")
                && naiveTen.Status == "FLOOR"
                && win.IndexOf("Precise", StringComparison.Ordinal) >= 0
                && win.IndexOf("Naive", StringComparison.Ordinal) < 0
                && Math.Abs(floor - (16d * 2.220446049250313e-16 * 1e12 * 1000d)) < 1e-6d;
            Add(list, "targets/errors within a run's round-off floor are marked and never win", ok,
                "floor=" + E(floor) + ", naive@1=" + naiveOne.Status + ", precise@1=" + preciseOne.Status
                + ", naive@10=" + naiveTen.Status + ", winner=" + win
                + ", text='" + StudyReport.CellText(naiveOne) + "'");
        }

        private static void CapLabelChecks(List<StudyCheck> list)
        {
            var p = new StudyProblem();
            p.Label = "selftest cap label";
            p.Dim = 3;
            p.PosDim = 0;
            p.T0 = 0d;
            p.Y0 = new double[3];
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                dy[0] = 0d;
                dy[1] = 0d;
                dy[2] = 0d;
            };
            p.CheckpointTimes = new[] { 1d };
            p.CheckpointRef = new[] { new double[3] };
            p.Floor = 0d;
            p.Certified = true;
            p.ScaleLength = 1d;

            var o = new StudyOptions();
            o.RelTargets = new double[0];
            o.AbsTargets = new[] { 1d };
            StudyTarget target = o.Targets(p)[0];

            var same = new StudyScenarioResult();
            same.Problem = p;
            same.Rows.Add(new StudyRow { Scenario = "t", Integrator = "A", Status = "ok", Finite = true, Err = 0.5d, Cost = 1000d });
            same.Rows.Add(new StudyRow { Scenario = "t", Integrator = "A", Variant = "cap=600 s", Status = "ok", Finite = true, Err = 0.5d, Cost = 1000d });
            same.Rows.Add(new StudyRow { Scenario = "t", Integrator = "B", Status = "ok", Finite = true, Err = 0.5d, Cost = 2000d });
            bool unused;
            string sameWin = StudyReport.Winner(same, target, o, out unused);

            var needed = new StudyScenarioResult();
            needed.Problem = p;
            needed.Rows.Add(new StudyRow { Scenario = "t", Integrator = "A", Status = "ok", Finite = true, Err = 1e9d, Cost = 100d });
            needed.Rows.Add(new StudyRow { Scenario = "t", Integrator = "A", Variant = "cap=600 s", Status = "ok", Finite = true, Err = 0.5d, Cost = 1000d });
            needed.Rows.Add(new StudyRow { Scenario = "t", Integrator = "B", Status = "ok", Finite = true, Err = 0.5d, Cost = 5000d });
            string neededWin = StudyReport.Winner(needed, target, o, out unused);

            bool ok = sameWin.IndexOf("[cap=", StringComparison.Ordinal) < 0
                && sameWin.IndexOf("~ A", StringComparison.Ordinal) < 0
                && neededWin.IndexOf("A [cap=600 s]", StringComparison.Ordinal) >= 0;
            Add(list, "a cap that does not decide the result is not named as a separate method", ok,
                "redundant: " + sameWin + "; load-bearing: " + neededWin);
        }

        // ---------------------------------------------------------------------------
        // Advanced methods ported from the standalone AdvancedIntegrators prototype
        // ---------------------------------------------------------------------------

        private static void AdvancedIntegratorChecks(List<StudyCheck> list)
        {
            StudyProblem p = MakeKeplerProblem(8, 2d);
            StudyContext ctx;

            StudyRun rkn = RunDirect(new Rkn1210Integrator(), p, 1e-11d, double.PositiveInfinity, out ctx);
            double rknErr = MaxErr(p, rkn);
            long rknAttempts = rkn.Steps + rkn.Rejected;
            Add(list, "RKN12(10) error at rtol 1e-11", rknErr < 1e-8d,
                "err=" + E(rknErr) + " steps=" + rkn.Steps.ToString(Inv) + " rej=" + rkn.Rejected.ToString(Inv));
            Add(list, "RKN12(10) calls = 1 + 16*(accepted+rejected) + accepted",
                ctx.Calls == 1L + (16L * rknAttempts) + rkn.Steps,
                "calls=" + ctx.Calls.ToString(Inv) + " accepted=" + rkn.Steps.ToString(Inv)
                + " rejected=" + rkn.Rejected.ToString(Inv));

            StudyRun ias = RunDirect(new Ias15Integrator(), p, 1e-10d, double.PositiveInfinity, out ctx);
            double iasErr = MaxErr(p, ias);
            Add(list, "IAS15 error at rtol 1e-10", iasErr < 1e-7d,
                "err=" + E(iasErr) + " steps=" + ias.Steps.ToString(Inv) + " rej=" + ias.Rejected.ToString(Inv));
            Add(list, "IAS15 calls are 1 + 7*iterations per attempt",
                ctx.Calls >= 8L * (ias.Steps + ias.Rejected)
                && ctx.Calls <= 85L * (ias.Steps + ias.Rejected),
                "calls=" + ctx.Calls.ToString(Inv) + " accepted=" + ias.Steps.ToString(Inv)
                + " rejected=" + ias.Rejected.ToString(Inv));

            StudyRun iasLoose = RunDirect(new Ias15Integrator(), p, 1e-6d, double.PositiveInfinity, out ctx);
            double iasLooseErr = MaxErr(p, iasLoose);
            Add(list, "IAS15 error decreases with rtol (1e-6 -> 1e-10)",
                iasLooseErr > iasErr && iasErr < 1e-9d,
                "err(1e-6)=" + E(iasLooseErr) + " err(1e-10)=" + E(iasErr)
                + " steps=" + iasLoose.Steps.ToString(Inv) + " -> " + ias.Steps.ToString(Inv));

            StudyRun srkn = RunDirect(new Srkn14AIntegrator(), p, Period / 64d, double.PositiveInfinity, out ctx);
            double srknErr = MaxErr(p, srkn);
            Add(list, "SRKN14A error at h = P/64", srknErr < 1e-7d, "err=" + E(srknErr));
            Add(list, "SRKN14A calls = 14*steps", ctx.Calls == 14L * srkn.Steps,
                "calls=" + ctx.Calls.ToString(Inv) + " steps=" + srkn.Steps.ToString(Inv));

            StudyRun bm = RunDirect(new KeplerSplit14AIntegrator(), p, Period / 4d, double.PositiveInfinity, out ctx);
            double bmErr = MaxErr(p, bm);
            Add(list, "WH-BM14A exact on pure Kepler (h = P/4)", bmErr < 1e-9d, "err=" + E(bmErr));
            Add(list, "WH-BM14A calls = 14*steps, aux = 15*steps",
                ctx.Calls == 14L * bm.Steps && ctx.AuxOps == 15L * bm.Steps,
                "calls=" + ctx.Calls.ToString(Inv) + " aux=" + ctx.AuxOps.ToString(Inv)
                + " steps=" + bm.Steps.ToString(Inv));

            double[] srknA = AdvancedCoefficients.SrknA14A;
            double[] srknB = AdvancedCoefficients.SrknB14A;
            double sumA = 0d;
            double sumB = 0d;
            bool symmetricA = true;
            for (int i = 0; i < srknA.Length; i++)
            {
                sumA += srknA[i];
                sumB += srknB[i];
                if (i < srknA.Length / 2
                    && Math.Abs(srknA[i] - srknA[srknA.Length - 1 - i]) > 1e-15d)
                {
                    symmetricA = false;
                }
            }

            Add(list, "SRKN14A/WH-BM14A weights: sum A = sum B = 1, A symmetric",
                Math.Abs(sumA - 1d) < 1e-14d && Math.Abs(sumB - 1d) < 1e-14d && symmetricA,
                "sumA=" + sumA.ToString("R", Inv) + " sumB=" + sumB.ToString("R", Inv)
                + " symmetric=" + symmetricA.ToString());

            StudyRun gj = RunDirect(new GaussJacksonIntegrator(), p, Period / 128d, double.PositiveInfinity, out ctx);
            double gjErr = MaxErr(p, gj);
            Add(list, "GJ8 error at h = P/128", gjErr < 1e-4d,
                "err=" + E(gjErr) + " steps=" + gj.Steps.ToString(Inv) + " calls=" + ctx.Calls.ToString(Inv));

            StudyProblem uneven = MakeUnevenKeplerProblem();
            StudyRun gjUneven = RunDirect(new GaussJacksonIntegrator(), uneven, Period / 64d, double.PositiveInfinity, out ctx);
            double gjUnevenErr = MaxErr(uneven, gjUneven);
            Add(list, "GJ8 survives a change of h at checkpoint boundaries",
                !gjUneven.Aborted && gjUnevenErr < 1e-4d,
                "err=" + E(gjUnevenErr) + " steps=" + gjUneven.Steps.ToString(Inv)
                + " calls=" + ctx.Calls.ToString(Inv) + " aborted=" + gjUneven.Aborted.ToString());

            StudyRun encke = RunDirect(new EnckeIntegrator(), p, Period / 4d, double.PositiveInfinity, out ctx);
            double enckeErr = MaxErr(p, encke);
            Add(list, "Encke-IAS15 error at H = P/4", enckeErr < 1e-8d,
                "err=" + E(enckeErr) + " calls=" + ctx.Calls.ToString(Inv) + " aux=" + ctx.AuxOps.ToString(Inv)
                + " aborted=" + encke.Aborted.ToString() + " " + encke.Abort);
            Add(list, "Encke-IAS15 prices one Kepler solve per RHS",
                ctx.AuxOps == ctx.Calls,
                "calls=" + ctx.Calls.ToString(Inv) + " aux=" + ctx.AuxOps.ToString(Inv));

            StudyProblem perturbed = MakePerturbedKeplerProblem();
            StudyRun reference = RunDirect(new Dop853AdaptiveIntegrator(), perturbed, 1e-13d,
                double.PositiveInfinity, out StudyContext refCtx);
            for (int k = 0; k < perturbed.CheckpointTimes.Length; k++)
            {
                if (reference.Samples[k] != null)
                {
                    perturbed.CheckpointRef[k] = (double[])reference.Samples[k].Clone();
                }
            }

            StudyRun enckePerturbed = RunDirect(new EnckeIntegrator(), perturbed, Period / 4d,
                double.PositiveInfinity, out ctx);
            double enckePerturbedErr = MaxErr(perturbed, enckePerturbed);
            Add(list, "Encke-IAS15 perturbed branch (DOP853 reference)",
                !enckePerturbed.Aborted && enckePerturbedErr < 1e-6d && ctx.Calls > 200L,
                "err=" + E(enckePerturbedErr) + " calls=" + ctx.Calls.ToString(Inv)
                + " aux=" + ctx.AuxOps.ToString(Inv) + " ref_calls=" + refCtx.Calls.ToString(Inv)
                + " aborted=" + enckePerturbed.Aborted.ToString());

            StudyProblem drag = MakeDragKeplerProblem();
            StudyRun dragReference = RunDirect(new Dop853AdaptiveIntegrator(), drag, 1e-13d,
                double.PositiveInfinity, out StudyContext dragRefCtx);
            for (int k = 0; k < drag.CheckpointTimes.Length; k++)
            {
                if (dragReference.Samples[k] != null)
                {
                    drag.CheckpointRef[k] = (double[])dragReference.Samples[k].Clone();
                }
            }

            StudyRun enckeDrag = RunDirect(new EnckeIntegrator(), drag, Period / 4d,
                double.PositiveInfinity, out ctx);
            double enckeDragErr = MaxErr(drag, enckeDrag);
            Add(list, "Encke-IAS15 velocity-dependent drag branch (DOP853 reference)",
                !enckeDrag.Aborted && enckeDragErr < 1e-6d && ctx.Calls > 200L,
                "err=" + E(enckeDragErr) + " calls=" + ctx.Calls.ToString(Inv)
                + " aux=" + ctx.AuxOps.ToString(Inv) + " ref_calls=" + dragRefCtx.Calls.ToString(Inv)
                + " aborted=" + enckeDrag.Aborted.ToString());

            ObservedOrder(list, new Srkn14AIntegrator(), p, 6d, new[] { 8, 16, 32, 64 });
            ObservedOrder(list, new GaussJacksonIntegrator(), p, 10d, new[] { 64, 128, 256, 512 });
            Rkn1210FixedOrder(list);

            StudyProblem harmonic = MakeHarmonicProblem();
            StudyRun harmonicRun = RunDirect(new GaussJacksonIntegrator(), harmonic, Period / 32d,
                double.PositiveInfinity, out ctx);
            double harmonicErr = MaxErr(harmonic, harmonicRun);
            Add(list, "GJ8 harmonic accuracy (non-Kepler)", !harmonicRun.Aborted && harmonicErr < 1e-8d,
                "err=" + E(harmonicErr) + " steps=" + harmonicRun.Steps.ToString(Inv)
                + " aborted=" + harmonicRun.Aborted.ToString());

            StudyProblem whPerturbed = MakePerturbedKeplerProblem(1e-4d, 1);
            StudyRun whReference = RunDirect(new Dop853AdaptiveIntegrator(), whPerturbed, 1e-13d,
                double.PositiveInfinity, out StudyContext whRefCtx);
            for (int k = 0; k < whPerturbed.CheckpointTimes.Length; k++)
            {
                if (whReference.Samples[k] != null)
                {
                    whPerturbed.CheckpointRef[k] = (double[])whReference.Samples[k].Clone();
                }
            }

            ObservedOrder(list, new Srkn14AIntegrator(), whPerturbed, 6d, new[] { 16, 32, 64, 128 }, true);
            StudyRun whRun = RunDirect(new KeplerSplit14AIntegrator(), whPerturbed, Period / 16d,
                double.PositiveInfinity, out ctx);
            double whRunErr = MaxErr(whPerturbed, whRun);
            Add(list, "WH-BM14A perturbed accuracy (DOP853 reference)", !whRun.Aborted && whRunErr < 1e-8d,
                "err=" + E(whRunErr) + " steps=" + whRun.Steps.ToString(Inv)
                + " aborted=" + whRun.Aborted.ToString());
            WhOrderCheck(list);

            string why;
            StudyProblem offset = MakeOffsetOscillator(1e12d, 1e5d, 1e3d, 1, 1d);
            Add(list, "RKN12(10) rejects a velocity-dependent force",
                !new Rkn1210Integrator().Supports(offset, out why), why);
            Add(list, "SRKN14A rejects a velocity-dependent force",
                !new Srkn14AIntegrator().Supports(offset, out why), why);
            Add(list, "GJ8 rejects a velocity-dependent force",
                !new GaussJacksonIntegrator().Supports(offset, out why), why);

            StudyProblem noCentre = MakeKeplerProblem(1, 1d);
            noCentre.CentralMu = 0d;
            Add(list, "WH-BM14A needs a central body",
                !new KeplerSplit14AIntegrator().Supports(noCentre, out why), why);
            Add(list, "Encke-IAS15 needs a central body",
                !new EnckeIntegrator().Supports(noCentre, out why), why);

            StudyProblem firstOrder = new StudyProblem();
            firstOrder.Dim = 3;
            firstOrder.PosDim = 0;
            Add(list, "IAS15 rejects a first-order problem",
                !new Ias15Integrator().Supports(firstOrder, out why), why);

            var edgeRes = new StudyScenarioResult();
            edgeRes.Problem = MakeKeplerProblem(1, 1d);
            edgeRes.Problem.Floor = 0d;
            edgeRes.Problem.Certified = true;
            var edgeOpt = new StudyOptions();
            edgeOpt.RelTargets = new double[0];
            edgeOpt.AbsTargets = new[] { 1e-3d };
            StudyTarget edgeTarget = edgeOpt.Targets(edgeRes.Problem)[0];
            edgeRes.Rows.Add(new StudyRow
            {
                Scenario = "t", Integrator = "PlainEdge", Status = "ok", Finite = true,
                Err = 1e-6d, Cost = 100d, Ms = 1d, Control = 1d, Steps = 10L, CapLimited = 0L
            });
            bool unresolvedEdge;
            string plainEdge = StudyReport.Winner(edgeRes, edgeTarget, edgeOpt, out unresolvedEdge);
            Add(list, "non-cap EDGE winner is flagged unresolved",
                unresolvedEdge && plainEdge.Contains("EDGE"), plainEdge);

            edgeRes.Rows.Clear();
            edgeRes.Rows.Add(new StudyRow
            {
                Scenario = "t", Integrator = "CapEdge", Status = "ok", Finite = true,
                Err = 1e-6d, Cost = 100d, Ms = 1d, Control = 1d, Steps = 10L, CapLimited = 5L
            });
            bool unresolvedCap;
            string capEdge = StudyReport.Winner(edgeRes, edgeTarget, edgeOpt, out unresolvedCap);
            Add(list, "cap-limited EDGE winner is resolved",
                !unresolvedCap && capEdge.Contains("EDGE"), capEdge);

            edgeRes.Rows.Clear();
            edgeRes.Rows.Add(new StudyRow
            {
                Scenario = "t", Integrator = "Bracketed", Status = "ok", Finite = true,
                Err = 1e-2d, Cost = 900d, Ms = 9d, Control = 1d, Steps = 10L
            });
            edgeRes.Rows.Add(new StudyRow
            {
                Scenario = "t", Integrator = "Bracketed", Status = "ok", Finite = true,
                Err = 1e-4d, Cost = 1100d, Ms = 11d, Control = 0.5d, Steps = 20L
            });
            edgeRes.Rows.Add(new StudyRow
            {
                Scenario = "t", Integrator = "CloseEdge", Status = "ok", Finite = true,
                Err = 1e-6d, Cost = 1200d, Ms = 12d, Control = 1d, Steps = 10L
            });
            bool unresolvedClose;
            string closeEdge = StudyReport.Winner(edgeRes, edgeTarget, edgeOpt, out unresolvedClose);
            Add(list, "close non-cap EDGE competitor keeps the winner provisional",
                unresolvedClose && !closeEdge.Contains("EDGE"), closeEdge);
        }

        private static StudyProblem MakeUnevenKeplerProblem()
        {
            StudyProblem p = MakeKeplerProblem(1, 2d);
            double[] times = { 0.3d * Period, 0.55d * Period, 0.9d * Period, 1.3d * Period, 2d * Period };
            p.CheckpointTimes = times;
            p.CheckpointRef = new double[times.Length][];
            for (int k = 0; k < times.Length; k++)
            {
                var reference = new double[7];
                Analytic(Ecc, times[k], reference);
                p.CheckpointRef[k] = reference;
            }

            return p;
        }

        private static StudyProblem MakePerturbedKeplerProblem(double perturbation = 1e-6d, int checkpoints = 8)
        {
            var p = new StudyProblem();
            p.Label = "selftest perturbed Kepler";
            p.Dim = 7;
            p.PosDim = 3;
            p.ConservativeSecondOrder = true;
            p.CentralMu = 1d;
            p.T0 = 0d;
            p.Y0 = new double[7];
            Analytic(Ecc, 0d, p.Y0);
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                double r2 = (y[0] * y[0]) + (y[1] * y[1]) + (y[2] * y[2]);
                double r3 = r2 * Math.Sqrt(r2);
                dy[0] = y[3];
                dy[1] = y[4];
                dy[2] = y[5];
                dy[3] = (-y[0] / r3) + perturbation;
                dy[4] = -y[1] / r3;
                dy[5] = -y[2] / r3;
                dy[6] = 0d;
            };
            p.CheckpointTimes = new double[checkpoints];
            p.CheckpointRef = new double[checkpoints][];
            for (int k = 0; k < checkpoints; k++)
            {
                p.CheckpointTimes[k] = 2d * Period * (k + 1) / checkpoints;
                p.CheckpointRef[k] = new double[7];
            }

            p.Floor = 1e-12d;
            p.Certified = true;
            p.CharTime = Period;
            p.ScaleLength = 1d;
            return p;
        }

        private static StudyProblem MakeDragKeplerProblem()
        {
            const int checkpoints = 8;
            var p = new StudyProblem();
            p.Label = "selftest drag Kepler";
            p.Dim = 7;
            p.PosDim = 3;
            p.ConservativeSecondOrder = false;
            p.CentralMu = 1d;
            p.T0 = 0d;
            p.Y0 = new double[7];
            Analytic(Ecc, 0d, p.Y0);
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                double r2 = (y[0] * y[0]) + (y[1] * y[1]) + (y[2] * y[2]);
                double r3 = r2 * Math.Sqrt(r2);
                dy[0] = y[3];
                dy[1] = y[4];
                dy[2] = y[5];
                dy[3] = -y[0] / r3 - (0.01d * y[3]);
                dy[4] = -y[1] / r3 - (0.01d * y[4]);
                dy[5] = -y[2] / r3 - (0.01d * y[5]);
                dy[6] = 0d;
            };
            p.CheckpointTimes = new double[checkpoints];
            p.CheckpointRef = new double[checkpoints][];
            for (int k = 0; k < checkpoints; k++)
            {
                p.CheckpointTimes[k] = 2d * Period * (k + 1) / checkpoints;
                p.CheckpointRef[k] = new double[7];
            }

            p.Floor = 1e-12d;
            p.Certified = true;
            p.CharTime = Period;
            p.ScaleLength = 1d;
            return p;
        }

        private static StudyProblem MakeHarmonicProblem()
        {
            var p = new StudyProblem();
            p.Label = "selftest harmonic 3D";
            p.Dim = 6;
            p.PosDim = 3;
            p.ConservativeSecondOrder = true;
            p.CentralMu = 0d;
            p.T0 = 0d;
            p.Y0 = new[] { 1d, 0d, 0d, 0d, 1d, 0d };
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                dy[0] = y[3];
                dy[1] = y[4];
                dy[2] = y[5];
                dy[3] = -y[0];
                dy[4] = -y[1];
                dy[5] = -y[2];
            };
            p.CheckpointTimes = new[] { Period };
            p.CheckpointRef = new[]
            {
                new[]
                {
                    Math.Cos(Period), Math.Sin(Period), 0d,
                    -Math.Sin(Period), Math.Cos(Period), 0d
                }
            };
            p.Floor = 1e-13d;
            p.Certified = true;
            p.CharTime = Period;
            p.ScaleLength = 1d;
            return p;
        }

        private static void Rkn1210FixedOrder(List<StudyCheck> list)
        {
            StudyProblem p = MakeKeplerProblem(1, 2d);
            int[] ns = { 4, 8, 16 };
            var errors = new double[ns.Length];
            for (int i = 0; i < ns.Length; i++)
            {
                double h = Period / ns[i];
                var cfg = new StudyConfig();
                cfg.Rtol = 1e-13d;
                cfg.Atol = 0d;
                cfg.Label = "fixed h";
                var ctx = new StudyContext(p, long.MaxValue / 4L);
                var stepper = new Rkn1210Stepper(p, cfg, ctx);
                double[] y = (double[])p.Y0.Clone();
                double[] yNew = new double[p.Dim];
                double t = p.T0;
                stepper.Begin(t, y);
                int steps = (int)Math.Round((p.EndTime - p.T0) / h);
                for (int s = 0; s < steps; s++)
                {
                    stepper.TryStep(t, h, y, yNew);
                    stepper.Accept(t + h, yNew);
                    Array.Copy(yNew, y, y.Length);
                    t += h;
                }

                errors[i] = p.Error(y, p.CheckpointRef[0]);
            }

            var slopes = new List<double>();
            for (int i = 0; i + 1 < ns.Length; i++)
            {
                if (errors[i] > 1e-14d && errors[i + 1] > 1e-14d
                    && errors[i] < 0.1d && errors[i + 1] < errors[i])
                {
                    slopes.Add(Math.Log(errors[i] / errors[i + 1]) / Math.Log(2d));
                }
            }

            bool decreasing = true;
            for (int i = 0; i + 1 < errors.Length; i++)
            {
                decreasing &= errors[i + 1] < errors[i];
            }

            // The leading error term cancels on this symmetric orbit, so a pairwise
            // slope jumps between 10.5 and 14.5. The slope over the whole tested range
            // is the stable estimate of the method's order.
            double overall = Math.Log(errors[0] / errors[errors.Length - 1])
                / Math.Log((double)ns[ns.Length - 1] / ns[0]);
            bool ok = decreasing && slopes.Count >= 1 && overall > 11.0d && overall < 13.5d;
            string allErrors = "";
            for (int i = 0; i < errors.Length; i++)
            {
                allErrors += (i == 0 ? "" : ", ") + ns[i].ToString(Inv) + ":" + E(errors[i]);
            }

            string pairSlopes = "";
            for (int i = 0; i < slopes.Count; i++)
            {
                pairSlopes += (i == 0 ? "" : ", ") + slopes[i].ToString("F2", Inv);
            }

            Add(list, "RKN12(10) observed fixed-step order", ok,
                "expected 12, overall " + overall.ToString("F2", Inv)
                + " (pairwise " + pairSlopes + "); errors " + allErrors);
        }

        private static void WhOrderCheck(List<StudyCheck> list)
        {
            StudyProblem p = MakePerturbedKeplerProblem(1e-2d, 1);
            int[] ns = { 8, 16, 32, 64, 128 };
            var states = new double[ns.Length][];
            for (int i = 0; i < ns.Length; i++)
            {
                StudyContext ctx;
                StudyRun run = RunDirect(new KeplerSplit14AIntegrator(), p, Period / ns[i],
                    double.PositiveInfinity, out ctx);
                states[i] = run.Final;
            }

            // Self-convergence: difference between h and h/2 is independent of the
            // reference and its floor. Points below 1e-11 are dominated by round-off.
            var pairNs = new List<int>();
            var pairErrors = new List<double>();
            string detail = "";
            for (int i = 0; i + 1 < ns.Length; i++)
            {
                double difference = p.Error(states[i], states[i + 1]);
                detail += (i == 0 ? "" : ", ") + ns[i].ToString(Inv) + "/" +
                    ns[i + 1].ToString(Inv) + ":" + E(difference);
                if (difference > 1e-12d && difference < 0.1d)
                {
                    pairNs.Add(ns[i]);
                    pairErrors.Add(difference);
                }
            }

            double overall = LeastSquaresOrder(pairNs.ToArray(), pairErrors.ToArray());
            bool ok = pairErrors.Count >= 2 && !double.IsNaN(overall)
                && overall > 5.0d && overall < 7.0d;
            Add(list, "WH-BM14A self-converged order", ok,
                "expected 6, overall " + overall.ToString("F2", Inv) + "; " + detail);
        }

        private static double LeastSquaresOrder(int[] ns, double[] errors)
        {
            double sumX = 0d;
            double sumY = 0d;
            int count = 0;
            for (int i = 0; i < errors.Length; i++)
            {
                if (errors[i] > 1e-14d && errors[i] < 0.1d)
                {
                    sumX += Math.Log(ns[i]);
                    sumY += Math.Log(errors[i]);
                    count++;
                }
            }

            if (count < 3)
            {
                return double.NaN;
            }

            double meanX = sumX / count;
            double meanY = sumY / count;
            double covariance = 0d;
            double variance = 0d;
            for (int i = 0; i < errors.Length; i++)
            {
                if (errors[i] > 1e-14d && errors[i] < 0.1d)
                {
                    double dx = Math.Log(ns[i]) - meanX;
                    covariance += dx * (Math.Log(errors[i]) - meanY);
                    variance += dx * dx;
                }
            }

            return variance > 0d ? -covariance / variance : double.NaN;
        }

        private static void ObservedOrder(
            List<StudyCheck> list, IStudyIntegrator integ, StudyProblem p, double expected, int[] ns,
            bool useOverall = false)
        {
            if (ns.Length < 2)
            {
                return;
            }

            var errors = new double[ns.Length];
            for (int i = 0; i < ns.Length; i++)
            {
                StudyContext ctx;
                StudyRun run = RunDirect(integ, p, Period / ns[i], double.PositiveInfinity, out ctx);
                errors[i] = MaxErr(p, run);
            }

            var slopes = new List<double>();
            for (int i = 0; i + 1 < ns.Length; i++)
            {
                if (errors[i] > 1e-12d && errors[i + 1] > 1e-12d
                    && errors[i] < 0.1d && errors[i + 1] < errors[i])
                {
                    slopes.Add(Math.Log(errors[i] / errors[i + 1]) / Math.Log(2d));
                }
            }

            bool decreasing = true;
            for (int i = 0; i + 1 < errors.Length; i++)
            {
                decreasing &= errors[i + 1] < errors[i];
            }

            double overallSlope = decreasing ? LeastSquaresOrder(ns, errors) : double.NaN;

            bool ok;
            double reportedOrder;
            string orderName;
            if (useOverall)
            {
                ok = !double.IsNaN(overallSlope)
                    && overallSlope > expected - 1.0d && overallSlope < expected + 1.0d;
                reportedOrder = overallSlope;
                orderName = "overall";
            }
            else
            {
                ok = slopes.Count >= 2;
                reportedOrder = ok ? Median(slopes) : double.NaN;
                ok = ok && reportedOrder > expected - 0.7d && reportedOrder < expected + 1.0d;
                orderName = "median";
            }

            string allErrors = "";
            for (int i = 0; i < errors.Length; i++)
            {
                allErrors += (i == 0 ? "" : ", ") + ns[i].ToString(Inv) + ":" + E(errors[i]);
            }

            string pairSlopes = "";
            for (int i = 0; i < slopes.Count; i++)
            {
                pairSlopes += (i == 0 ? "" : ", ") + slopes[i].ToString("F2", Inv);
            }

            Add(list, integ.Name + " observed order", ok,
                "expected " + expected.ToString("F0", Inv) + ", " + orderName + " "
                + reportedOrder.ToString("F2", Inv)
                + " (overall " + overallSlope.ToString("F2", Inv)
                + ", pairs " + pairSlopes + "); errors " + allErrors);
        }

        /// <summary>Always fails its error test; used to exercise the step-floor abort.</summary>
        private sealed class NeverConvergesIntegrator : AdaptiveIntegrator
        {
            public override string Name { get { return "NeverConverges"; } }

            public override string Family { get { return "Test"; } }

            public override bool Supports(StudyProblem p, out string reason)
            {
                reason = "";
                return true;
            }

            protected override IAdaptiveStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
            {
                return new NeverStepper();
            }

            private sealed class NeverStepper : IAdaptiveStepper
            {
                public int ErrorOrderExponent { get { return 5; } }

                public void Begin(double t, double[] y)
                {
                }

                public double TryStep(double t, double h, double[] y, double[] yNew)
                {
                    return 2d;
                }

                public void Accept(double tNew, double[] yNew)
                {
                }
            }
        }

        private static void SweepSmoke(List<StudyCheck> list)
        {
            StudyProblem p = MakeKeplerProblem(8, 2d);
            var o = new StudyOptions();
            o.RelTargets = new[] { 1e-5d, 1e-8d };
            o.MaxCostRhs = 600000L;
            o.AdaptiveMaxSteps = new[] { double.PositiveInfinity, 0.2d };
            StudyScenarioResult res = StudySweepRunner.Run(p, StudyRegistry.All(), o, null);
            int errors = 0;
            foreach (StudyRow r in res.Rows)
            {
                if (r.Status == "error")
                {
                    errors++;
                }
            }

            Add(list, "sweep smoke: no failed runs", errors == 0, "failed rows: " + errors.ToString(Inv));

            StudyTarget loose = o.Targets(p)[0];
            var missing = new List<string>();
            foreach (string key in StudyReport.Keys(res.Rows))
            {
                // Reachability is a property of the method, not of its production-cap
                // re-run: with cap = 0.2 s on this orbit the best error is ~8e-5,
                // above the loose target, so the capped variant is legitimately
                // censored ("not reached"/CAP-BOUND) while the method itself reaches it.
                List<StudyRow> variantRows = StudyReport.RowsOf(res.Rows, key);
                if (variantRows.Count > 0 && variantRows[0].Variant.Length > 0)
                {
                    continue;
                }

                StudyCell cell = StudyReport.CellFor(res, key, loose, o);
                // FLOOR also reached the loose target numerically; it only means the
                // error is below the run's accumulation noise, so the report refuses
                // to rank that point. Reachability is still satisfied.
                if (cell.Status != "OK" && cell.Status != "EDGE"
                    && cell.Status != "NA" && cell.Status != "FLOOR")
                {
                    missing.Add(key + "=" + cell.Status);
                }
            }

            Add(list, "sweep smoke: every supported method reaches the loose target",
                missing.Count == 0, missing.Count == 0 ? "all reached" : string.Join("; ", missing));

            bool unresolved;
            string win = StudyReport.Winner(res, o.Targets(p)[1], o, out unresolved);
            string winDetail = win;
            foreach (string key in StudyReport.Keys(res.Rows))
            {
                StudyCell c = StudyReport.CellFor(res, key, o.Targets(p)[1], o);
                winDetail += " | " + key + ":" + c.Status + "/" + c.Cost.ToString("F0", Inv);
            }

            Add(list, "sweep smoke: a winner is named at the tight target", win != "-", winDetail);

            // the reported cost must be work that was actually spent
            var rows = new List<StudyRow>();
            rows.Add(new StudyRow { Scenario = "t", Integrator = "a", Status = "ok", Finite = true, Err = 1e-2d, Cost = 100d, Ms = 1d });
            rows.Add(new StudyRow { Scenario = "t", Integrator = "b", Status = "ok", Finite = true, Err = 1e-4d, Cost = 400d, Ms = 4d });
            StudyCell mid = StudyReport.CostAtTarget(rows, 1e-3d, p);
            StudyCell below = StudyReport.CostAtTarget(rows, 1e-6d, p);
            StudyCell above = StudyReport.CostAtTarget(rows, 1d, p);
            bool actual = mid.Status == "OK" && Math.Abs(mid.Cost - 400d) < 1e-9d
                && !mid.Interpolated
                && below.Status == "NR" && above.Status == "EDGE" && Math.Abs(above.Cost - 100d) < 1e-9d;
            Add(list, "wide 4x bracket falls back to the first passing point", actual,
                "mid=" + mid.Status + " " + mid.Cost.ToString("F3", Inv) + ", below=" + below.Status
                + ", above=" + above.Status + " " + above.Cost.ToString("F3", Inv));

            var closeRows = new List<StudyRow>
            {
                new StudyRow { Scenario = "t", Integrator = "a", Kind = StudyKind.Adaptive, Status = "ok", Finite = true, Err = 1e-2d, Cost = 100d, Ms = 1d, Control = 1d },
                new StudyRow { Scenario = "t", Integrator = "b", Kind = StudyKind.Adaptive, Status = "ok", Finite = true, Err = 1e-4d, Cost = 140d, Ms = 1.4d, Control = 0.5d }
            };
            StudyCell closeMid = StudyReport.CostAtTarget(closeRows, 1e-3d, p);
            double closeExpected = Math.Sqrt(100d * 140d);
            bool closeActual = closeMid.Status == "OK" && closeMid.Interpolated
                && Math.Abs(closeMid.Cost - closeExpected) < 1e-9d;
            Add(list, "1.4x bracket is log-log interpolated", closeActual,
                "cost=" + closeMid.Cost.ToString("F3", Inv) + " expected=" + closeExpected.ToString("F3", Inv)
                + " interpolated=" + closeMid.Interpolated.ToString());

            var fixedRows = new List<StudyRow>
            {
                new StudyRow
                {
                    Scenario = "t", Integrator = "a", Kind = StudyKind.Fixed, Status = "ok",
                    Finite = true, Err = 1e-2d, Cost = 100d, Ms = 1d, Control = 1d,
                    Steps = 10L, ExpectedOrder = 6d
                },
                new StudyRow
                {
                    Scenario = "t", Integrator = "b", Kind = StudyKind.Fixed, Status = "ok",
                    Finite = true, Err = 1e-4d, Cost = 140d, Ms = 1.4d, Control = 0.6d,
                    Steps = 20L, ExpectedOrder = 6d
                }
            };
            StudyCell fixedMid = StudyReport.CostAtTarget(fixedRows, 1e-3d, p);
            Add(list, "fixed-step local order uses the step-count ratio",
                fixedMid.Interpolated,
                "interpolated=" + fixedMid.Interpolated.ToString() + " cost="
                + fixedMid.Cost.ToString("F3", Inv));

            // flat work-precision curves must be reported as ties, not as a ranking
            var tieRes = new StudyScenarioResult();
            tieRes.Problem = MakeKeplerProblem(1, 1d);
            tieRes.Problem.Floor = 0d;
            tieRes.Problem.Certified = true;
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Kind = StudyKind.Adaptive, Integrator = "A", Status = "ok", Finite = true, Err = 1e-2d, Cost = 100d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Kind = StudyKind.Adaptive, Integrator = "B", Status = "ok", Finite = true, Err = 1e-2d, Cost = 300d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Kind = StudyKind.Adaptive, Integrator = "B", Status = "ok", Finite = true, Err = 1e-4d, Cost = 400d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Kind = StudyKind.Adaptive, Integrator = "C", Status = "ok", Finite = true, Err = 1e-2d, Cost = 320d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Kind = StudyKind.Adaptive, Integrator = "C", Status = "ok", Finite = true, Err = 1e-4d, Cost = 430d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Kind = StudyKind.Adaptive, Integrator = "D", Status = "ok", Finite = true, Err = 1e-2d, Cost = 350d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Kind = StudyKind.Adaptive, Integrator = "D", Status = "ok", Finite = true, Err = 1e-4d, Cost = 800d });
            var tieOpt = new StudyOptions();
            tieOpt.RelTargets = new double[0];
            tieOpt.AbsTargets = new[] { 1e-3d };
            bool unused;
            StudyTarget tieTarget = tieOpt.Targets(tieRes.Problem)[0];
            string tieWin = StudyReport.Winner(tieRes, tieTarget, tieOpt, out unused);
            string tieDetail = "";
            foreach (string key in StudyReport.Keys(tieRes.Rows))
            {
                StudyCell c = StudyReport.CellFor(tieRes, key, tieTarget, tieOpt);
                tieDetail += key + ":" + c.Status + " " + c.Cost.ToString("F0", Inv) + "; ";
            }

            Add(list, "winner marks a 10% cost tie", tieWin.Contains("B (") && tieWin.Contains("~ C (")
                && !tieWin.Contains("D ("), tieWin + " | " + tieDetail);
        }

        /// <summary>
        /// Regression guard for the plateau break. Both an adaptive method (rtol sweep)
        /// and a fixed-step method (h sweep) may sit at a large error for several points
        /// and only then enter their convergent branch. The sweep must not stop at that
        /// plateau: the adaptive planet sweep and the fixed WH-Y6 ship sweep both did.
        /// </summary>
        private static void PlateauSweepChecks(List<StudyCheck> list)
        {
            CheckPlateauSweep(list, StudyKind.Adaptive);
            CheckPlateauSweep(list, StudyKind.Fixed);
        }

        private static void CheckPlateauSweep(List<StudyCheck> list, StudyKind kind)
        {
            var p = new StudyProblem();
            p.Label = "selftest plateau";
            p.Dim = 3;
            p.PosDim = 0;
            p.T0 = 0d;
            p.Y0 = new double[3];
            p.Rhs = delegate (double t, double[] y, double[] dy)
            {
                dy[0] = 0d;
                dy[1] = 0d;
                dy[2] = 0d;
            };
            p.CheckpointTimes = new[] { 1d };
            p.CheckpointRef = new[] { new double[3] };
            p.Floor = 0d;
            p.Certified = true;
            p.CharTime = 1d;
            // Large enough that the old rules (error < 0.01*ScaleLength) count the
            // 1e8 plateau as a terminal stall instead of a still-converging regime.
            p.ScaleLength = 1e12d;

            var o = new StudyOptions();
            o.RelTargets = new double[0];
            o.AbsTargets = new[] { 1e2d, 1e3d };
            o.MaxCostRhs = 1000000L;
            o.AdaptiveMaxSteps = new[] { double.PositiveInfinity };
            o.TimingReps = 1;

            var integ = new PlateauIntegrator(kind);
            StudyScenarioResult res = StudySweepRunner.Run(p, new List<IStudyIntegrator> { integ }, o, null);
            var target = new StudyTarget();
            target.Value = 1e2d;
            target.Label = "1e2 m";
            StudyCell cell = StudyReport.CellFor(res, integ.Name, target, o);
            bool reached = cell.Status == "OK" || cell.Status == "EDGE";
            int points = 0;
            double finest = double.PositiveInfinity;
            foreach (StudyRow r in res.Rows)
            {
                if (r.Status == "ok" && r.Finite)
                {
                    points++;
                    finest = Math.Min(finest, r.Control);
                }
            }

            string label = kind == StudyKind.Adaptive ? "adaptive" : "fixed";
            Add(list, label + " sweep continues past a plateau above the targets",
                reached && finest <= 1e-8d,
                "cell=" + cell.Status + " " + cell.Cost.ToString("F0", Inv)
                + ", points=" + points.ToString(Inv) + ", finest control=" + E(finest));
        }

        /// <summary>
        /// Deliberate test method: error 1e8 for control >= 1e-6 (a plateau far above
        /// both targets), then 1e3, 1e2, 10 as control goes to 1e-7, 1e-8, 1e-9.
        /// </summary>
        private sealed class PlateauIntegrator : IStudyIntegrator
        {
            private readonly StudyKind kind;

            public PlateauIntegrator(StudyKind kind)
            {
                this.kind = kind;
            }

            public string Name
            {
                get { return "PlateauTest"; }
            }

            public string Family
            {
                get { return "Test"; }
            }

            public StudyKind Kind
            {
                get { return kind; }
            }

            public double ExpectedOrder
            {
                get { return double.NaN; }
            }

            public bool Supports(StudyProblem p, out string reason)
            {
                reason = "";
                return true;
            }

            public void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
            {
                coarse = 1e-3d;
                fine = 1e-9d;
                ratio = 10d;
            }

            public StudyConfig MakeConfig(StudyProblem p, double control, double maxStep)
            {
                var cfg = new StudyConfig();
                cfg.Control = control;
                if (kind == StudyKind.Fixed)
                {
                    cfg.H = control;
                    cfg.Label = "h=" + control.ToString("G3", Inv);
                }
                else
                {
                    cfg.Rtol = control;
                    cfg.MaxStep = maxStep;
                    cfg.Label = "rtol=" + control.ToString("G3", Inv);
                }

                return cfg;
            }

            public StudyRun Run(StudyProblem p, StudyConfig cfg, StudyContext ctx)
            {
                double err;
                if (cfg.Control >= 1e-6d)
                {
                    err = 1e8d;
                }
                else if (cfg.Control >= 1e-7d)
                {
                    err = 1e3d;
                }
                else if (cfg.Control >= 1e-8d)
                {
                    err = 1e2d;
                }
                else
                {
                    err = 10d;
                }

                // Vary the counted work so the sweep's "cost stopped changing" guard
                // does not mistake this fake for a cap-bound adaptive run.
                int calls = 1 + (int)Math.Round(-Math.Log10(cfg.Control));
                var dy = new double[p.Dim];
                for (int i = 0; i < calls; i++)
                {
                    ctx.Rhs(p.T0, p.Y0, dy);
                }

                var state = new double[p.Dim];
                state[0] = err;
                var run = new StudyRun();
                run.Samples = new double[p.CheckpointTimes.Length][];
                for (int k = 0; k < run.Samples.Length; k++)
                {
                    run.Samples[k] = (double[])state.Clone();
                }

                run.Final = state;
                run.Steps = 1L;
                return run;
            }
        }
    }
}
