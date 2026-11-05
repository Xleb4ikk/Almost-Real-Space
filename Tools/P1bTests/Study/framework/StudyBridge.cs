using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

// THE ONLY FILE THAT KNOWS THE ENGINE. It is a partial of P1bTests so it can reach the existing
// private helpers (StudyShipMatrix*, StudyPlanet*). Everything in Study/framework/*.cs besides
// this file is engine-independent and can be unit-tested alone (Test551).
//
// Needs ONE refactor in SpacecraftOrbitMatrix.cs (a pure move, see AGENT_PROMPT.md step 3):
//     private static List<StudyShipMatrixCase> StudyShipMatrixBuildCases(
//         StarSystem gravity, StarSystem j2OnlySystem, StarSystem dragOnlySystem)
internal static partial class P1bTests
{
    private static string StudyBridgeEnv(string name)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>P1B_SCENARIOS="LEO;Sun 1 AU" runs only scenarios whose label contains one of the fragments.</summary>
    private static bool StudyBridgeScenarioSelected(string label, string filter)
    {
        if (filter == null)
        {
            return true;
        }

        foreach (string part in filter.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string frag = part.Trim();
            if (frag.Length > 0 && label.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------------------------
    // Spacecraft
    // ------------------------------------------------------------------------------------

    private static StudyProblem StudyBridgeShipProblem(StudyShipMatrixCase c, StudyShipMatrixRefs refs)
    {
        var acc = new StudyShipMatrixAcc(c);
        var p = new StudyProblem();
        p.Label = c.Label;
        p.Dim = 7;
        p.PosDim = 3;
        p.T0 = StudyShipT0;
        p.Y0 = new[] { c.P0.X, c.P0.Y, c.P0.Z, c.V0.X, c.V0.Y, c.V0.Z, c.Mass0 };
        p.Rhs = delegate (double t, double[] y, double[] dy) { acc.EvaluateArray(y, t, dy); };

        // Splitting methods are valid only for a velocity- and mass-independent force.
        p.ConservativeSecondOrder = c.Drag == null && !c.Thrust;
        p.CentralMu = c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter();

        double[] times = refs.CheckpointTimes;
        p.CheckpointTimes = (double[])times.Clone();
        p.CheckpointRef = new double[times.Length][];
        for (int k = 0; k < times.Length; k++)
        {
            p.CheckpointRef[k] = new[]
            {
                refs.CheckpointP[k].X, refs.CheckpointP[k].Y, refs.CheckpointP[k].Z,
                refs.CheckpointV[k].X, refs.CheckpointV[k].Y, refs.CheckpointV[k].Z,
                c.Mass0
            };
        }

        if (c.Thrust)
        {
            // The force jumps (or has a corner) at burn start/end and at the throttle ramps:
            // every integrator must land exactly on these times.
            double ramp = StudyShipMatrixRampSeconds(c);
            double start = c.ThrustStart;
            double end = c.ThrustStart + c.ThrustSeconds;
            p.Breakpoints = new[] { start, start + ramp, end - ramp, end };
        }

        // Block layout [pos(3), vel(3), mass(1)] is derived from PosDim = 3, Dim = 7.
        p.Floor = refs.ReferenceFloor;           // NaN when the reference is not certified
        p.Certified = double.IsFinite(refs.ReferenceFloor);
        p.CharTime = c.Tau;
        p.ScaleLength = c.ScaleLength;
        // The baked ephemeris is only defined from t = 0, and the Y6-family sub-steps
        // reach t - 0.158*h: a step of months (the generic 0.25*CharTime on a Sun orbit)
        // would ask for a negative time and throw EphemerisRangeException. Test549's own
        // grid never goes coarser than 4 days; use the same engine-safe ceiling.
        p.MaxControl = 4d * 86400d;
        return p;
    }

    private static int Test552_IntegratorFrameworkShipSweep()
    {
        Console.WriteLine("--- Integrator framework: spacecraft work-precision sweep ---");
        StarSystem gravity = StudyShipMatrixSystem(false, false);
        StarSystem j2OnlySystem = StudyShipMatrixSystem(true, false);
        StarSystem dragOnlySystem = StudyShipMatrixSystem(false, true);
        List<StudyShipMatrixCase> cases = StudyShipMatrixBuildCases(gravity, j2OnlySystem, dragOnlySystem);

        string scenarioFilter = StudyBridgeEnv("P1B_SCENARIOS");
        List<IStudyIntegrator> integrators = StudyRegistry.Select(StudyBridgeEnv("P1B_INTEGRATORS"));

        var options = new StudyOptions();
        options.RelTargets = new[] { 1e-6d, 1e-8d, 1e-10d };
        // Run adaptive methods once with their own step choice and once with the production cap:
        // a binding cap makes the cost independent of rtol.
        options.AdaptiveMaxSteps = new[] { double.PositiveInfinity, 600d };
        // Costs are in study-RHS calls. Set to 3 to price the production-local path (three gravity sums).
        string mult = StudyBridgeEnv("P1B_RHS_MULTIPLIER");
        if (mult != null)
        {
            options.RhsCostMultiplier = double.Parse(mult, CultureInfo.InvariantCulture);
        }

        var results = new List<StudyScenarioResult>();
        foreach (StudyShipMatrixCase c in cases)
        {
            if (!StudyBridgeScenarioSelected(c.Label, scenarioFilter))
            {
                continue;
            }

            StudyShipMatrixSetTargets(c);
            StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(c);
            StudyProblem problem = StudyBridgeShipProblem(c, refs);
            Console.WriteLine();
            Console.WriteLine("sweeping: " + c.Label);
            StudyScenarioResult r = StudySweepRunner.Run(problem, integrators, options, Console.Out);
            results.Add(r);
            StudyReport.Print(Console.Out, r, options);
        }

        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        StudyReport.WriteCsv(Path.Combine(dir, "Test552_FrameworkShipSweep.csv"), results);
        StudyReport.WriteMarkdown(Path.Combine(dir, "Test552_FrameworkShipSweep.md"), results, options);

        // Structural checks only. Accuracy rankings are DATA, not pass/fail: a regression lock
        // would just freeze today's numbers (see the review of Test549's baselines).
        int failedRuns = 0;
        foreach (StudyScenarioResult r in results)
        {
            foreach (StudyRow row in r.Rows)
            {
                if (row.Status == "error")
                {
                    failedRuns++;
                    Console.WriteLine("FAILED RUN " + row.Scenario + " / " + row.Key + " / " + row.Config + ": " + row.Note);
                }
            }
        }

        Check(results.Count > 0, "framework ship sweep ran scenarios",
            results.Count.ToString(CultureInfo.InvariantCulture) + " scenario(s); filter=" + (scenarioFilter ?? "none"));
        Check(failedRuns == 0, "framework ship sweep: no failed runs",
            failedRuns.ToString(CultureInfo.InvariantCulture) + " failed run(s)");
        Console.WriteLine("report: " + Path.Combine(dir, "Test552_FrameworkShipSweep.md"));
        return 0;
    }

    // ------------------------------------------------------------------------------------
    // Planets
    // ------------------------------------------------------------------------------------

    private static double[] StudyBridgeConcat(double[] a, double[] b)
    {
        var r = new double[a.Length + b.Length];
        Array.Copy(a, 0, r, 0, a.Length);
        Array.Copy(b, 0, r, a.Length, b.Length);
        return r;
    }

    private static StudyProblem StudyBridgePlanetProblem(
        string label, StudyNBody nb, double years,
        StudyRefResult reference, StudyRefResult alternative)
    {
        int n = nb.Count;
        int len = 3 * n;
        var pBuf = new double[len];
        var aBuf = new double[len];
        double[] mu = nb.Mu;
        var p = new StudyProblem();
        p.Label = label;
        p.Dim = 2 * len;
        p.PosDim = len;
        p.T0 = 0d;
        p.Y0 = StudyBridgeConcat(nb.P0, nb.V0);
        p.Rhs = delegate (double t, double[] y, double[] dy)
        {
            Array.Copy(y, 0, pBuf, 0, len);
            StudyAcc(pBuf, aBuf, mu);
            for (int i = 0; i < len; i++)
            {
                dy[i] = y[len + i];
                dy[len + i] = aBuf[i];
            }
        };
        p.ConservativeSecondOrder = true;      // pure mutual gravity
        p.CentralMu = 0d;                      // no single dominant centre: Kepler-split methods report n/a

        // Error metric: worst relative-vector error over ALL body pairs. It covers Moon-about-Earth
        // as well as the outer planets without choosing a parent body per planet.
        p.ErrorFn = delegate (double[] s, double[] r)
        {
            double worst = 0d;
            for (int i = 1; i < n; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    worst = Math.Max(worst, StudyRel(s, i, j, r));
                }
            }

            return worst;
        };

        double end = years * StudyYear;
        var times = new List<double>();
        var refStates = new List<double[]>();
        var altStates = new List<double[]>();
        bool haveAlt = alternative != null && alternative.Samples != null
            && reference.Samples != null && alternative.Samples.Count == reference.Samples.Count;
        if (reference.Samples != null)
        {
            for (int i = 0; i < reference.Samples.Count; i++)
            {
                StudySample s = reference.Samples[i];
                times.Add(s.T);
                refStates.Add(StudyBridgeConcat(s.P, s.V));
                if (haveAlt)
                {
                    StudySample a = alternative.Samples[i];
                    altStates.Add(StudyBridgeConcat(a.P, a.V));
                }
            }
        }

        if (times.Count == 0 || times[times.Count - 1] < end - 1e-6d)
        {
            times.Add(end);
            refStates.Add(StudyBridgeConcat(reference.P, reference.V));
            if (haveAlt)
            {
                altStates.Add(StudyBridgeConcat(alternative.P, alternative.V));
            }
        }

        p.CheckpointTimes = times.ToArray();
        p.CheckpointRef = refStates.ToArray();

        double floor = double.NaN;
        if (haveAlt && altStates.Count == refStates.Count)
        {
            floor = 0d;
            for (int k = 0; k < refStates.Count; k++)
            {
                floor = Math.Max(floor, p.ErrorFn(refStates[k], altStates[k]));
            }

            double scale = 1d;
            for (int i = 0; i < reference.P.Length; i++)
            {
                scale = Math.Max(scale, Math.Abs(reference.P[i]));
            }

            floor = Math.Max(floor, ToleranceReference.FloatingPointFloor * scale);
        }

        p.Floor = floor;
        p.Certified = double.IsFinite(floor);

        // scales
        double outer = 0d;
        for (int i = 1; i < n; i++)
        {
            double dx = nb.P0[3 * i] - nb.P0[0];
            double dy = nb.P0[(3 * i) + 1] - nb.P0[1];
            double dz = nb.P0[(3 * i) + 2] - nb.P0[2];
            outer = Math.Max(outer, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
        }

        p.ScaleLength = Math.Max(outer, 1d);
        double fastest = double.PositiveInfinity;
        for (int i = 1; i < n; i++)
        {
            for (int j = 0; j < i; j++)
            {
                double dx = nb.P0[3 * i] - nb.P0[3 * j];
                double dy = nb.P0[(3 * i) + 1] - nb.P0[(3 * j) + 1];
                double dz = nb.P0[(3 * i) + 2] - nb.P0[(3 * j) + 2];
                double d = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                double per = 2d * Math.PI * Math.Sqrt(d * d * d / (nb.Mu[i] + nb.Mu[j]));
                fastest = Math.Min(fastest, per);
            }
        }

        p.CharTime = fastest;
        return p;
    }

    private static int Test553_IntegratorFrameworkPlanetSweep()
    {
        Console.WriteLine("--- Integrator framework: planet-orbit work-precision sweep ---");
        double years = 10d;
        string yearsEnv = StudyBridgeEnv("P1B_PLANET_YEARS");
        if (yearsEnv != null)
        {
            years = double.Parse(yearsEnv, CultureInfo.InvariantCulture);
        }

        List<IStudyIntegrator> integrators = StudyRegistry.Select(StudyBridgeEnv("P1B_INTEGRATORS"));
        var options = new StudyOptions();
        options.RelTargets = new double[0];
        options.AbsTargets = new[] { 100d, 10d, 1d };       // metres, worst pair error over the horizon
        options.AdaptiveMaxSteps = new[] { double.PositiveInfinity, 86400d };

        var results = new List<StudyScenarioResult>();
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        double sampleEvery = 30d * 86400d;
        StudyRefResult reference = StudyPlanetIndependentReference(nb, years, sampleEvery);
        StudyRefResult alternative = StudyPlanetIndependentReferenceAlt(nb, years, sampleEvery);
        StudyProblem problem = StudyBridgePlanetProblem(
            "Real solar 7-body, " + years.ToString("G", CultureInfo.InvariantCulture) + " y", nb, years, reference, alternative);
        StudyScenarioResult r = StudySweepRunner.Run(problem, integrators, options, Console.Out);
        results.Add(r);
        StudyReport.Print(Console.Out, r, options);

        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        StudyReport.WriteCsv(Path.Combine(dir, "Test553_FrameworkPlanetSweep.csv"), results);
        StudyReport.WriteMarkdown(Path.Combine(dir, "Test553_FrameworkPlanetSweep.md"), results, options);

        int failedRuns = 0;
        foreach (StudyRow row in r.Rows)
        {
            if (row.Status == "error")
            {
                failedRuns++;
                Console.WriteLine("FAILED RUN " + row.Key + " / " + row.Config + ": " + row.Note);
            }
        }

        Check(failedRuns == 0, "framework planet sweep: no failed runs",
            failedRuns.ToString(CultureInfo.InvariantCulture) + " failed run(s)");
        Console.WriteLine("report: " + Path.Combine(dir, "Test553_FrameworkPlanetSweep.md"));
        return 0;
    }

    // ------------------------------------------------------------------------------------
    // Framework self-test (engine independent; run this first)
    // ------------------------------------------------------------------------------------

    private static int Test551_IntegratorFrameworkSelfTest()
    {
        Console.WriteLine("--- Integrator framework self-test (Kepler orbit, exact solution) ---");
        foreach (StudyCheck sc in StudySelfTest.RunAll())
        {
            Check(sc.Passed, "framework: " + sc.Name, sc.Detail);
        }

        return 0;
    }
}
