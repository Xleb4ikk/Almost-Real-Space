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
    /// Kepler propagator, the budget guard, stop/breakpoint merging and a full sweep smoke run.
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
            var p = new StudyProblem();
            p.Label = "selftest Kepler e=0.2";
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
                Analytic(Ecc, t, p.CheckpointRef[k]);
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
                if (cell.Status != "OK" && cell.Status != "EDGE" && cell.Status != "NA")
                {
                    missing.Add(key + "=" + cell.Status);
                }
            }

            Add(list, "sweep smoke: every supported method reaches the loose target",
                missing.Count == 0, missing.Count == 0 ? "all reached" : string.Join("; ", missing));

            bool unresolved;
            string win = StudyReport.Winner(res, o.Targets(p)[1], o, out unresolved);
            Add(list, "sweep smoke: a winner is named at the tight target", win != "-", win);

            // the reported cost must be work that was actually spent
            var rows = new List<StudyRow>();
            rows.Add(new StudyRow { Scenario = "t", Integrator = "a", Status = "ok", Finite = true, Err = 1e-2d, Cost = 100d, Ms = 1d });
            rows.Add(new StudyRow { Scenario = "t", Integrator = "b", Status = "ok", Finite = true, Err = 1e-4d, Cost = 400d, Ms = 4d });
            StudyCell mid = StudyReport.CostAtTarget(rows, 1e-3d);
            StudyCell below = StudyReport.CostAtTarget(rows, 1e-6d);
            StudyCell above = StudyReport.CostAtTarget(rows, 1d);
            bool actual = mid.Status == "OK" && Math.Abs(mid.Cost - 400d) < 1e-9d
                && below.Status == "NR" && above.Status == "EDGE" && Math.Abs(above.Cost - 100d) < 1e-9d;
            Add(list, "cost is the cheapest executed point at or below target, never extrapolated", actual,
                "mid=" + mid.Status + " " + mid.Cost.ToString("F3", Inv) + ", below=" + below.Status
                + ", above=" + above.Status + " " + above.Cost.ToString("F3", Inv));

            // flat work-precision curves must be reported as ties, not as a ranking
            var tieRes = new StudyScenarioResult();
            tieRes.Problem = MakeKeplerProblem(1, 1d);
            tieRes.Problem.Floor = 0d;
            tieRes.Problem.Certified = true;
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Integrator = "A", Status = "ok", Finite = true, Err = 1e-2d, Cost = 100d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Integrator = "B", Status = "ok", Finite = true, Err = 1e-2d, Cost = 300d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Integrator = "B", Status = "ok", Finite = true, Err = 1e-4d, Cost = 400d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Integrator = "C", Status = "ok", Finite = true, Err = 1e-2d, Cost = 320d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Integrator = "C", Status = "ok", Finite = true, Err = 1e-4d, Cost = 430d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Integrator = "D", Status = "ok", Finite = true, Err = 1e-2d, Cost = 350d });
            tieRes.Rows.Add(new StudyRow { Scenario = "t", Integrator = "D", Status = "ok", Finite = true, Err = 1e-4d, Cost = 800d });
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

            Add(list, "winner marks a 10% cost tie", tieWin.Contains("B (400)") && tieWin.Contains("~ C (430)")
                && !tieWin.Contains("D (800)"), tieWin + " | " + tieDetail);
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
