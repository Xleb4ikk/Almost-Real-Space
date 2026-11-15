using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Galilego.Core;
using Galilego.Universe;
using P1bStudy.Framework;
using Rkn12BakeStepper = Galilego.Universe.Rkn1210Stepper;

internal static partial class P1bTests
{
    // ────────────────────────────────────────────────────────────────────────
    // R3 synthetic certification (TASK_RKN12 §R3/§8.2/§8.3).
    // Results: Study/rkn12/results/synthetic/*.csv (local artifacts).
    // ────────────────────────────────────────────────────────────────────────

    private const double Rkn12R3YearSeconds = 365d * 86400d;
    private const double Rkn12R3GateMeters = 1.0d;

    private static void Rkn12CertificationSynthetic()
    {
        var rows = new List<string>();
        var invariantRows = new List<string>();

        Rkn12CertTwoBody("Outdoors-2body", BaselineOutdoorsSystem(), rows, invariantRows);
        Rkn12CertTwoBody("ContactZone-2body", BaselineContactZoneSystem(), rows, invariantRows);
        Rkn12CertTightBinary(rows, invariantRows);
        Rkn12CertFourBody(rows, invariantRows);
        Rkn12CertP3(rows, invariantRows);

        string dir = Path.Combine("Study", "rkn12", "results", "synthetic");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "synthetic.csv"), new[]
        {
            "system,horizon_years,body,reference,samples,max_node_m,rms_node_m,max_between_m,gate_m,verdict,note"
        }, new System.Text.UTF8Encoding(false));
        File.AppendAllLines(Path.Combine(dir, "synthetic.csv"), rows, new System.Text.UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(dir, "invariants.csv"), new[]
        {
            "system,horizon_years,method,samples,max_abs_dE_over_E,dp_over_P,dcom_over_COM,steps,note"
        }, new System.Text.UTF8Encoding(false));
        File.AppendAllLines(Path.Combine(dir, "invariants.csv"), invariantRows, new System.Text.UTF8Encoding(false));
        Console.WriteLine("R3 synthetic artifacts: " + Path.GetFullPath(dir));
    }

    private static BaselineRun Rkn12CertBake(
        StarSystem sys, double years, BakeIntegrator integrator, string label)
    {
        BakeConfig config = BaselineConfig();
        config.Integrator = integrator;
        string dir = Path.Combine("Study", "rkn12", "results", "synthetic", "bake", label);
        return BaselineBakeOnce(sys, years, config, dir);
    }

    // ------------------------------------------------------------------------
    // 2-body (analytic Kepler rail, exact)
    // ------------------------------------------------------------------------

    private static void Rkn12CertTwoBody(
        string label, StarSystem sys, List<string> rows, List<string> invariantRows)
    {
        foreach (double years in new[] { 1.2d, 1024d })
        {
            string runLabel = label + "_" + Fmt(years) + "y";
            BaselineRun run = Rkn12CertBake(sys, years, BakeIntegrator.Rkn1210Adaptive, runLabel);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  R3 {0} {1} y: {2:F2} s", label, Fmt(years), run.WallSeconds));

            foreach (var kv in run.Ephemerides)
            {
                OrbitingBody body = kv.Key;
                if (body.Parent == null)
                {
                    continue;
                }

                BakedEphemeris mem = kv.Value.LoadRangeToMemory(0d, kv.Value.EndSeconds, 1000000);
                double maxNode;
                double rmsNode;
                double maxBetween;
                int samples;
                Rkn12ErrorAgainstRail(body, mem, out maxNode, out rmsNode, out maxBetween, out samples);

                bool pass = maxNode <= Rkn12R3GateMeters;
                Check(pass, "R3 2-body " + label + " " + Fmt(years) + " y: node error <= 1 m",
                    maxNode.ToString("E3", CultureInfo.InvariantCulture) + " m");
                rows.Add(BaselineCsvRow(
                    label, Fmt(years), body.Name, "analytic-kepler-rail", samples,
                    maxNode.ToString("R", CultureInfo.InvariantCulture),
                    rmsNode.ToString("R", CultureInfo.InvariantCulture),
                    maxBetween.ToString("R", CultureInfo.InvariantCulture),
                    Rkn12R3GateMeters.ToString("R", CultureInfo.InvariantCulture),
                    pass ? "PASS" : "FAIL", "delivered BG3; includes quantization"));
            }

            Rkn12EnergyInvariant(label, run, invariantRows);
            Rkn12DirectInvariants(label, run.System, years, invariantRows);
        }
    }

    private static void Rkn12ErrorAgainstRail(
        OrbitingBody body, BakedEphemeris eph,
        out double maxNode, out double rmsNode, out double maxBetween, out int samples)
    {
        maxNode = 0d;
        rmsNode = 0d;
        maxBetween = 0d;
        samples = 0;
        double[] nodes = BaselineNodeTimes(eph, 4096);
        double sumSq = 0d;
        foreach (double t in nodes)
        {
            double tt = Math.Min(t, eph.EndSeconds);
            if (!eph.TryEvaluate(tt, out Vector3d p, out _))
            {
                continue;
            }

            body.EvaluateKeplerOffset(tt, out Vector3d r, out _);
            double dp = (p - r).Magnitude;
            maxNode = Math.Max(maxNode, dp);
            sumSq += dp * dp;
            samples++;
        }

        rmsNode = Math.Sqrt(sumSq / Math.Max(1, samples));

        double span = eph.EndSeconds;
        for (int k = 0; k < 128; k++)
        {
            double t = span * (k + 0.5d) / 128d;
            if (!eph.TryEvaluate(t, out Vector3d p, out _))
            {
                continue;
            }

            body.EvaluateKeplerOffset(t, out Vector3d r, out _);
            maxBetween = Math.Max(maxBetween, (p - r).Magnitude);
        }
    }

    // ------------------------------------------------------------------------
    // Tight binary (P4): 2-body analytic, may fail 1 m — record, do not force
    // ------------------------------------------------------------------------

    private static void Rkn12CertTightBinary(List<string> rows, List<string> invariantRows)
    {
        const double years = 10d;
        StarSystem sys = StudyTightBinarySystem();
        BaselineRun run = Rkn12CertBake(sys, years, BakeIntegrator.Rkn1210Adaptive, "P4_tight_10y");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  R3 P4 tight binary 10 y: {0:F2} s", run.WallSeconds));

        foreach (var kv in run.Ephemerides)
        {
            OrbitingBody body = kv.Key;
            if (body.Parent == null)
            {
                continue;
            }

            BakedEphemeris mem = kv.Value.LoadRangeToMemory(0d, kv.Value.EndSeconds, 1000000);
            Rkn12ErrorAgainstRail(body, mem, out double maxNode, out double rmsNode, out double maxBetween, out int samples);
            bool pass = maxNode <= Rkn12R3GateMeters;
            Check(double.IsFinite(maxNode), "R3 P4 tight binary: error finite",
                maxNode.ToString("E3", CultureInfo.InvariantCulture));
            rows.Add(BaselineCsvRow(
                "P4-tight", Fmt(years), body.Name, "analytic-kepler-rail", samples,
                maxNode.ToString("R", CultureInfo.InvariantCulture),
                rmsNode.ToString("R", CultureInfo.InvariantCulture),
                maxBetween.ToString("R", CultureInfo.InvariantCulture),
                Rkn12R3GateMeters.ToString("R", CultureInfo.InvariantCulture),
                pass ? "PASS" : "FAIL (documented limit)", "P4 1 m may fail; not forced"));
        }

        Rkn12EnergyInvariant("P4-tight", run, invariantRows);
        Rkn12DirectInvariants("P4-tight", sys, years, invariantRows);
    }

    // ------------------------------------------------------------------------
    // 4-body preset: 10 and 100 years, dd-Taylor endpoint, Moon separately
    // ------------------------------------------------------------------------

    private static void Rkn12CertFourBody(List<string> rows, List<string> invariantRows)
    {
        foreach (double years in new[] { 10d, 100d })
        {
            StarSystem sys = ExampleSystemPresets.CreateExampleSystem();
            BaselineRun run = Rkn12CertBake(sys, years, BakeIntegrator.Rkn1210Adaptive,
                "Example4_" + Fmt(years) + "y");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  R3 Example4 {0} y: {1:F2} s", Fmt(years), run.WallSeconds));

            Rkn12DdEndpointErrors("Example4", run, years, rows);
            Rkn12EnergyInvariant("Example4", run, invariantRows);
            Rkn12DirectInvariants("Example4", sys, years, invariantRows);
        }
    }

    private static void Rkn12DdEndpointErrors(
        string label, BaselineRun run, double years, List<string> rows)
    {
        BaselineInitialState(run.System, out double[] p0, out double[] v0, out double[] mu);
        var input = new TaylorNBodyInput
        {
            Names = new string[run.System.AllBodies.Count],
            Mu = mu,
            P0 = p0,
            V0 = v0
        };
        for (int i = 0; i < input.Names.Length; i++)
        {
            input.Names[i] = run.System.AllBodies[i].Name;
        }

        double totalSeconds = years * Rkn12R3YearSeconds;
        Console.WriteLine("  R3 dd-Taylor endpoint " + label + " " + Fmt(years) + " y ...");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        DdNBodyResult dd = RunTaylorNBodyDdOptSeconds(
            input, totalSeconds, 32, 1e-30d, 1e-30d, 86400d, 1e-6d);
        watch.Stop();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  R3 dd-Taylor done: {0} steps, {1:F1} s", dd.Steps, watch.Elapsed.TotalSeconds));

        for (int i = 0; i < run.System.AllBodies.Count; i++)
        {
            OrbitingBody body = run.System.AllBodies[i];
            if (body.Parent == null)
            {
                continue;
            }

            int parent = BaselineIndexOf(run.System.AllBodies, body.Parent);
            run.Ephemerides.TryGetValue(body, out BakedEphemeris eph);
            if (eph == null)
            {
                continue;
            }

            double t = Math.Min(totalSeconds, eph.EndSeconds);
            eph.TryEvaluate(t, out Vector3d p, out Vector3d v);
            double dx = p.X - (dd.P[3 * i] - dd.P[3 * parent]);
            double dy = p.Y - (dd.P[(3 * i) + 1] - dd.P[(3 * parent) + 1]);
            double dz = p.Z - (dd.P[(3 * i) + 2] - dd.P[(3 * parent) + 2]);
            double dp = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            bool isMoon = body.Parent.Parent != null;
            bool pass = dp <= Rkn12R3GateMeters;
            bool gate = years >= 100d - 1e-9d;
            string gateName = isMoon ? "R3 4-body Moon <= 1 m" : "R3 4-body body <= 1 m";
            if (gate)
            {
                Check(pass, gateName + " " + label + " " + Fmt(years) + " y",
                    body.Name + ": " + dp.ToString("E3", CultureInfo.InvariantCulture) + " m");
            }

            rows.Add(BaselineCsvRow(
                label, Fmt(years), body.Name, "dd-taylor-K32-tol1e-30-endpoint", 1,
                dp.ToString("R", CultureInfo.InvariantCulture), "",
                "", Rkn12R3GateMeters.ToString("R", CultureInfo.InvariantCulture),
                gate ? (pass ? "PASS" : "FAIL") : "INFO (gate at 100 y)",
                isMoon ? "Moon separately; dd steps=" + dd.Steps : "dd steps=" + dd.Steps));
        }
    }

    // ------------------------------------------------------------------------
    // P3 Custom 6-body: 10 years, qualitative vs tighter DOP853
    // ------------------------------------------------------------------------

    private static void Rkn12CertP3(List<string> rows, List<string> invariantRows)
    {
        const double years = 10d;
        StarSystem rknSys = StudyCustomSystem();
        BaselineRun rkn = Rkn12CertBake(rknSys, years, BakeIntegrator.Rkn1210Adaptive, "P3_10y_rkn12");
        StarSystem dopSys = StudyCustomSystem();
        BaselineRun dop = Rkn12CertBake(dopSys, years, BakeIntegrator.Dop853Fixed, "P3_10y_dop853");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  R3 P3 10 y: RKN12 {0:F2} s, DOP853 {1:F2} s",
            rkn.WallSeconds, dop.WallSeconds));

        // A true tighter probe (60 s / phase 0.002) is used only as consistency.
        BaselineRun tighter = Rkn12CertBakeTight(StudyCustomSystem(), years, "P3_10y_tight60");

        double rknErr = Rkn12ConsistencyMax(rkn, tighter);
        double dopErr = Rkn12ConsistencyMax(dop, tighter);
        bool pass = rknErr <= dopErr * 1.1d;
        Check(pass, "R3 P3: RKN12 not worse than DOP853 (consistency, 1.1x)",
            "RKN12=" + rknErr.ToString("E3", CultureInfo.InvariantCulture) +
            " DOP853=" + dopErr.ToString("E3", CultureInfo.InvariantCulture));

        rows.Add(BaselineCsvRow(
            "P3-custom6", Fmt(years), "all", "tighter-DOP853-consistency", 512,
            rknErr.ToString("R", CultureInfo.InvariantCulture), "", "",
            "", pass ? "PASS (qualitative)" : "FAIL (qualitative)",
            "RKN12 " + rknErr.ToString("E3", CultureInfo.InvariantCulture) +
            " vs DOP853 " + dopErr.ToString("E3", CultureInfo.InvariantCulture) +
            "; tighter is consistency only, not independent"));

        Rkn12EnergyInvariant("P3-custom6", rkn, invariantRows);
        Rkn12DirectInvariants("P3-custom6", rknSys, years, invariantRows);
    }

    private static BaselineRun Rkn12CertBakeTight(StarSystem sys, double years, string label)
    {
        BakeConfig config = BaselineTightConfig();
        string dir = Path.Combine("Study", "rkn12", "results", "synthetic", "bake", label);
        return BaselineBakeOnce(sys, years, config, dir);
    }

    private static double Rkn12ConsistencyMax(BaselineRun run, BaselineRun reference)
    {
        double max = 0d;
        foreach (var kv in run.Ephemerides)
        {
            OrbitingBody body = kv.Key;
            if (body.Parent == null)
            {
                continue;
            }

            BakedEphemeris mem = kv.Value.LoadRangeToMemory(0d, kv.Value.EndSeconds, 1000000);
            BakedEphemeris refEph = BaselineFindByName(reference.Ephemerides, body.Name);
            BakedEphemeris refMem = refEph != null
                ? refEph.LoadRangeToMemory(0d, refEph.EndSeconds, 1000000)
                : null;
            if (mem == null || refMem == null)
            {
                continue;
            }

            foreach (double t in BaselineNodeTimes(mem, 512))
            {
                double tt = Math.Min(t, Math.Min(mem.EndSeconds, refMem.EndSeconds));
                if (!mem.TryEvaluate(tt, out Vector3d p, out _) || !refMem.TryEvaluate(tt, out Vector3d r, out _))
                {
                    continue;
                }

                max = Math.Max(max, (p - r).Magnitude);
            }
        }

        return max;
    }

    // ------------------------------------------------------------------------
    // R3 diagnostic: is the 1024-year drift our implementation or the method?
    // Runs the reference StudyAdvancedSteppers stepper on the SAME absolute
    // 2-body state and fixed step, then the new stepper, and compares both with
    // the analytic rail. No gate; diagnostic only.
    // ------------------------------------------------------------------------

    private static int Rkn12RoundoffProbe()
    {
        StarSystem sys = BaselineOutdoorsSystem();
        BaselineInitialState(sys, out double[] p, out double[] v, out double[] mu);
        double years = 1024d;
        double end = years * Rkn12R3YearSeconds;
        long steps = 642000L;
        double h = end / steps;

        // Reference stepper: 12-component [p(2 bodies), v(2 bodies)] state.
        var problem = new StudyProblem
        {
            Dim = 12,
            PosDim = 6,
            T0 = 0d,
            Y0 = new double[12]
        };
        Array.Copy(p, 0, problem.Y0, 0, 6);
        Array.Copy(v, 0, problem.Y0, 6, 6);
        problem.Rhs = delegate (double t, double[] y, double[] dy)
        {
            for (int i = 0; i < 6; i++)
            {
                dy[i] = y[6 + i];
                dy[6 + i] = 0d;
            }

            for (int i = 0; i < 2; i++)
            {
                for (int j = i + 1; j < 2; j++)
                {
                    double dx = y[3 * j] - y[3 * i];
                    double dyv = y[(3 * j) + 1] - y[(3 * i) + 1];
                    double dz = y[(3 * j) + 2] - y[(3 * i) + 2];
                    double r2 = (dx * dx) + (dyv * dyv) + (dz * dz);
                    double s = 1d / (r2 * Math.Sqrt(r2));
                    double fi = s * mu[j];
                    double fj = s * mu[i];
                    dy[6 + (3 * i)] += fi * dx;
                    dy[6 + (3 * i) + 1] += fi * dyv;
                    dy[6 + (3 * i) + 2] += fi * dz;
                    dy[6 + (3 * j)] -= fj * dx;
                    dy[6 + (3 * j) + 1] -= fj * dyv;
                    dy[6 + (3 * j) + 2] -= fj * dz;
                }
            }
        };

        var config = new StudyConfig { Rtol = 1e-12d, Atol = 0d };
        var context = new StudyContext(problem, long.MaxValue / 4L);
        var reference = new P1bStudy.Framework.Rkn1210Stepper(problem, config, context);
        var refNew = new double[12];
        reference.Begin(0d, problem.Y0);
        double tr = 0d;
        for (long s = 0; s < steps; s++)
        {
            reference.TryStep(tr, h, problem.Y0, refNew);
            reference.Accept(tr + h, refNew);
            Array.Copy(refNew, problem.Y0, 12);
            tr += h;
        }

        var stepper = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-12, 1e-3, 1e-6, Rkn12GravityMu(mu));
        var pn = new double[6];
        var vn = new double[6];
        stepper.Begin(0d, p, v);
        double t = 0d;
        for (long s = 0; s < steps; s++)
        {
            stepper.TryStep(t, h, p, v, pn, vn);
            stepper.Accept(t + h, pn, vn);
            Array.Copy(pn, p, 6);
            Array.Copy(vn, v, 6);
            t += h;
        }

        OrbitingBody body = sys.AllBodies[1];
        body.EvaluateKeplerOffset(t, out Vector3d railP, out _);
        double tExact = (double)steps * h;
        body.EvaluateKeplerOffset(tExact, out Vector3d railExact, out _);
        var refRel = new Vector3d(
            problem.Y0[3] - problem.Y0[0],
            problem.Y0[4] - problem.Y0[1],
            problem.Y0[5] - problem.Y0[2]);
        var ourRel = new Vector3d(p[3] - p[0], p[4] - p[1], p[5] - p[2]);
        double refErr = (refRel - railP).Magnitude;
        double ourErr = (ourRel - railP).Magnitude;
        double cross = (refRel - ourRel).Magnitude;
        double refErrExact = (refRel - railExact).Magnitude;
        double ourErrExact = (ourRel - railExact).Magnitude;
        double dt = t - tExact;
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "R3 roundoff probe 1024 y, h={0:F0} s, steps={1}: reference vs rail={2:E3} m, ours vs rail={3:E3} m, reference vs ours={4:E3} m",
            h, steps, refErr, ourErr, cross));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "R3 time-base probe: tAccum={0:R}, tExact={1:R}, dt={2:E6} s; ref vs rail@tExact={3:E3} m, ours vs rail@tExact={4:E3} m",
            t, tExact, dt, refErrExact, ourErrExact));
        Check(double.IsFinite(refErr) && double.IsFinite(ourErr),
            "R3 roundoff probe finite", "");

        // Convergence at the EXACT physical time: the state is advanced by h, so
        // its physical time is steps*h; the accumulated label t drifts. Compare
        // both to separate truncation/round-off from time-base bookkeeping.
        Console.WriteLine("R3 convergence at exact time (2-body 1024 y):");
        foreach (double factor in new[] { 1.0d, 0.5d, 0.25d })
        {
            double hh = end / Math.Round(end / (h * factor));
            long n = (long)Math.Round(end / hh);
            double[] pp = new double[6];
            double[] vv = new double[6];
            Array.Copy(p, 0, pp, 0, 6);
            Array.Copy(v, 0, vv, 0, 6);
            // Rebuild the initial state: p/v above are the final ones; use the
            // exact RunBake initial state again.
            BaselineInitialState(sys, out double[] p0c, out double[] v0c, out _);
            Array.Copy(p0c, 0, pp, 0, 6);
            Array.Copy(v0c, 0, vv, 0, 6);
            var st = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-12, 1e-3, 1e-6, Rkn12GravityMu(mu));
            var ppn = new double[6];
            var vvn = new double[6];
            st.Begin(0d, pp, vv);
            double tc = 0d;
            for (long s = 0; s < n; s++)
            {
                st.TryStep(tc, hh, pp, vv, ppn, vvn);
                st.Accept(tc + hh, ppn, vvn);
                Array.Copy(ppn, pp, 6);
                Array.Copy(vvn, vv, 6);
                tc += hh;
            }

            double tEnd = (double)n * hh;
            var rel = new Vector3d(pp[3] - pp[0], pp[4] - pp[1], pp[5] - pp[2]);
            body.EvaluateKeplerOffset(tc, out Vector3d railAccum, out _);
            body.EvaluateKeplerOffset(tEnd, out Vector3d railExact2, out _);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  h={0:E3} s, steps={1}: dt={2:E3} s, err@tAccum={3:E3} m, err@tExact={4:E3} m",
                hh, n, tc - tEnd, (rel - railAccum).Magnitude, (rel - railExact2).Magnitude));
        }

        return 0;
    }

    // ------------------------------------------------------------------------
    // Energy / direct momentum-COM
    // ------------------------------------------------------------------------

    private static void Rkn12EnergyInvariant(string label, BaselineRun run, List<string> rows)
    {
        int n = run.System.AllBodies.Count;
        var mu = new double[n];
        for (int i = 0; i < n; i++)
        {
            mu[i] = run.System.AllBodies[i].ResolveStandardGravitationalParameter();
        }

        const int samples = 257;
        double e0 = 0d;
        double maxRel = 0d;
        for (int k = 0; k < samples; k++)
        {
            double t = run.Years * Rkn12R3YearSeconds * k / (samples - 1.0d);
            double e = BaselineEnergy(run, mu, t, out bool ok);
            if (!ok)
            {
                continue;
            }

            if (k == 0)
            {
                e0 = e;
            }

            if (e0 != 0d)
            {
                maxRel = Math.Max(maxRel, Math.Abs((e - e0) / e0));
            }
        }

        rows.Add(BaselineCsvRow(
            label, Fmt(run.Years), "rkn12-delivered", samples,
            maxRel.ToString("R", CultureInfo.InvariantCulture),
            "", "", "", "",
            "E_total from delivered relative states; gate <= max(2xDOP853,1e-7) at 100y"));
    }

    private static void Rkn12DirectInvariants(
        string label, StarSystem sys, double years, List<string> rows)
    {
        BaselineInitialState(sys, out double[] p, out double[] v, out double[] mu);
        int n = sys.AllBodies.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++)
        {
            parent[i] = sys.AllBodies[i].Parent == null
                ? -1
                : BaselineIndexOf(sys.AllBodies, sys.AllBodies[i].Parent);
        }

        double phaseMax = Rkn12PhaseStepMax(sys);
        var stepper = new Rkn12BakeStepper(n, parent, 1e-12, 1e-3, 1e-6, Rkn12GravityMu(mu));
        var pn = new double[3 * n];
        var vn = new double[3 * n];
        stepper.Begin(0d, p, v);

        double p0x = 0d, p0y = 0d, p0z = 0d, c0x = 0d, c0y = 0d, c0z = 0d;
        double pScale = 0d, cScale = 0d;
        double totalMu = 0d;
        for (int i = 0; i < n; i++)
        {
            totalMu += mu[i];
            p0x += mu[i] * v[3 * i];
            p0y += mu[i] * v[(3 * i) + 1];
            p0z += mu[i] * v[(3 * i) + 2];
            c0x += mu[i] * p[3 * i];
            c0y += mu[i] * p[(3 * i) + 1];
            c0z += mu[i] * p[(3 * i) + 2];
            pScale += mu[i] * Magnitude(v, 3 * i);
            cScale += mu[i] * Magnitude(p, 3 * i);
        }

        cScale /= totalMu;
        double e0 = Rkn12SyntheticEnergy(mu, p, v);
        double t = 0d;
        double h = phaseMax;
        double end = years * Rkn12R3YearSeconds;
        long steps = 0L;
        while (t < end)
        {
            double hTry = Math.Min(h, end - t);
            double err = stepper.TryStep(t, hTry, p, v, pn, vn);
            if (err <= 1d)
            {
                stepper.Accept(t + hTry, pn, vn);
                Array.Copy(pn, p, p.Length);
                Array.Copy(vn, v, v.Length);
                t += hTry;
                steps++;
                double factor = Math.Min(5d, Math.Max(0.2d,
                    0.9d * Math.Pow(Math.Max(err, 1e-10d), -1d / 11d)));
                h = Math.Min(hTry * factor, phaseMax);
            }
            else
            {
                h = hTry * Math.Min(0.9d, Math.Max(0.1d, 0.9d * Math.Pow(err, -1d / 11d)));
                if (h < 1e-3d)
                {
                    throw new InvalidOperationException("R3 direct invariants: step floor reached.");
                }
            }
        }

        double px = 0d, py = 0d, pz = 0d, cx = 0d, cy = 0d, cz = 0d;
        for (int i = 0; i < n; i++)
        {
            px += mu[i] * v[3 * i];
            py += mu[i] * v[(3 * i) + 1];
            pz += mu[i] * v[(3 * i) + 2];
            cx += mu[i] * p[3 * i];
            cy += mu[i] * p[(3 * i) + 1];
            cz += mu[i] * p[(3 * i) + 2];
        }

        double dp = Math.Sqrt(
            ((px - p0x) * (px - p0x)) + ((py - p0y) * (py - p0y)) + ((pz - p0z) * (pz - p0z)))
            / Math.Max(pScale, 1e-300d);
        double dcom = Math.Sqrt(
            ((cx - c0x) * (cx - c0x)) + ((cy - c0y) * (cy - c0y)) + ((cz - c0z) * (cz - c0z)))
            / Math.Max(cScale * totalMu, 1e-300d);
        double e1 = Rkn12SyntheticEnergy(mu, p, v);
        double de = Math.Abs((e1 - e0) / e0);

        bool pass = dp <= 1e-11d && dcom <= 1e-11d;
        Check(pass, "R3 invariants " + label + " " + Fmt(years) + " y: P/COM <= 1e-11",
            "|dP|/P=" + dp.ToString("E3", CultureInfo.InvariantCulture) +
            ", |dCOM|/COM=" + dcom.ToString("E3", CultureInfo.InvariantCulture));
        rows.Add(BaselineCsvRow(
            label, Fmt(years), "rkn12-direct", 1,
            de.ToString("R", CultureInfo.InvariantCulture),
            dp.ToString("R", CultureInfo.InvariantCulture),
            dcom.ToString("R", CultureInfo.InvariantCulture),
            steps.ToString(CultureInfo.InvariantCulture), "",
            "direct stepper from the exact RunBake initial state"));
    }

    private static double Rkn12PhaseStepMax(StarSystem sys)
    {
        double best = double.PositiveInfinity;
        for (int i = 0; i < sys.AllBodies.Count; i++)
        {
            OrbitingBody body = sys.AllBodies[i];
            if (body.Parent == null || !(body.SemiMajorAxis > 0d))
            {
                continue;
            }

            double muLocal = body.Parent.ResolveStandardGravitationalParameter()
                + body.ResolveSubtreeStandardGravitationalParameter();
            if (!(muLocal > 0d))
            {
                continue;
            }

            double period = 2d * Math.PI
                * Math.Sqrt(body.SemiMajorAxis * body.SemiMajorAxis * body.SemiMajorAxis / muLocal);
            if (period > 0d)
            {
                best = Math.Min(best, period * 0.02d / (2d * Math.PI));
            }
        }

        return double.IsPositiveInfinity(best) ? 900d : best;
    }

    private static Rkn1210Acceleration Rkn12GravityMu(double[] mu)
    {
        int n = mu.Length;
        return delegate (double t, double[] p, double[] a)
        {
            for (int i = 0; i < 3 * n; i++)
            {
                a[i] = 0d;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dx = p[3 * j] - p[3 * i];
                    double dy = p[(3 * j) + 1] - p[(3 * i) + 1];
                    double dz = p[(3 * j) + 2] - p[(3 * i) + 2];
                    double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                    double s = 1d / (r2 * Math.Sqrt(r2));
                    double fi = s * mu[j];
                    double fj = s * mu[i];
                    a[3 * i] += fi * dx;
                    a[(3 * i) + 1] += fi * dy;
                    a[(3 * i) + 2] += fi * dz;
                    a[3 * j] -= fj * dx;
                    a[(3 * j) + 1] -= fj * dy;
                    a[(3 * j) + 2] -= fj * dz;
                }
            }
        };
    }

    private static double Rkn12SyntheticEnergy(double[] mu, double[] p, double[] v)
    {
        int n = mu.Length;
        double kinetic = 0d;
        for (int i = 0; i < n; i++)
        {
            double vx = v[3 * i];
            double vy = v[(3 * i) + 1];
            double vz = v[(3 * i) + 2];
            kinetic += 0.5d * mu[i] * ((vx * vx) + (vy * vy) + (vz * vz));
        }

        double potential = 0d;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = p[3 * i] - p[3 * j];
                double dy = p[(3 * i) + 1] - p[(3 * j) + 1];
                double dz = p[(3 * i) + 2] - p[(3 * j) + 2];
                potential -= mu[i] * mu[j] / Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }
        }

        return kinetic + potential;
    }
}
