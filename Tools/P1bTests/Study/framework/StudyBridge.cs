using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
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

    /// <summary>Filtered checks write CSVs only; a partial mode/regime summary would be misleading.</summary>
    private static bool StudyBridgeSkipSummaries()
    {
        return string.Equals(StudyBridgeEnv("P1B_SKIP_SUMMARIES"), "1", StringComparison.Ordinal);
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
            if (frag.Length > 0 && frag[0] == '=')
            {
                if (string.Equals(label, frag.Substring(1), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (frag.Length > 0 && label.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0)
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
        p.Mode = c.Mode;
        p.ModeTags = c.ModeTags;
        p.AltitudeKm = c.AltitudeKm;
        p.SpeedMps = c.SpeedMps;
        p.Eccentricity = c.Eccentricity;
        p.HasBreakpoints = c.HasBreakpoints;

        // Splitting methods are valid only for a velocity- and mass-independent force.
        p.ConservativeSecondOrder = c.Drag == null && !c.Thrust;
        p.CentralMu = c.Sys.AllBodies[c.Central].ResolveStandardGravitationalParameter();
        OrbitingBody centralBody = c.Sys.AllBodies[c.Central];
        if (p.ConservativeSecondOrder && p.CentralMu > 0d && p.PosDim == 3
            && centralBody.J2 == 0d
            && c.Label.IndexOf("J2", StringComparison.OrdinalIgnoreCase) < 0)
        {
            double mu = p.CentralMu;
            p.EnergyFn = delegate (double[] s)
            {
                double r2 = (s[0] * s[0]) + (s[1] * s[1]) + (s[2] * s[2]);
                double v2 = (s[3] * s[3]) + (s[4] * s[4]) + (s[5] * s[5]);
                return (0.5d * v2) - (mu / Math.Sqrt(r2));
            };
            p.AngularMomentumFn = delegate (double[] s)
            {
                double lx = (s[1] * s[5]) - (s[2] * s[4]);
                double ly = (s[2] * s[3]) - (s[0] * s[5]);
                double lz = (s[0] * s[4]) - (s[1] * s[3]);
                return Math.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
            };
        }
        else if (p.ConservativeSecondOrder && p.CentralMu > 0d && p.PosDim == 3
            && centralBody.J2 != 0d)
        {
            // J2 potential: U = -mu/r * (1 - J2 (R/r)^2 (3/2 (z/r)^2 - 1/2)).
            double mu = p.CentralMu;
            double j2 = centralBody.J2;
            double radius = centralBody.Radius;
            p.EnergyFn = delegate (double[] s)
            {
                double r2 = (s[0] * s[0]) + (s[1] * s[1]) + (s[2] * s[2]);
                double r = Math.Sqrt(r2);
                double v2 = (s[3] * s[3]) + (s[4] * s[4]) + (s[5] * s[5]);
                double c2 = (s[2] * s[2]) / r2;
                double factor = 1d - (j2 * radius * radius / r2 * ((1.5d * c2) - 0.5d));
                return (0.5d * v2) - (mu / r * factor);
            };
        }

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
        p.ReferenceNote = refs.LocalKind;
        p.ReferenceMethod = "fixed DOP853, local frame";
        p.ReferenceFamily = "Runge-Kutta";
        p.ReferenceResolution = refs.LocalUnratioedResolutionUsed
            ? "unratioed_exemption_used"
            : (refs.LocalValid && refs.LocalAsymptotic ? "ratio_ok" : "not_certified");
        p.CharTime = c.Tau;
        // Cases that do not set a scale explicitly still need a length for the
        // relative-error column and the diagnostics. The initial orbital radius is
        // the natural choice; a bare 1 m default made err_rel meaningless.
        p.ScaleLength = c.ScaleLength > 1d ? c.ScaleLength : Math.Max(1d, c.P0.Magnitude);
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
        // One absolute target set for every ship scenario: 100 m / 10 m / 1 m.
        // Absolute metres are what the mission needs, they are identical for every
        // scenario (so the summary table's columns mean the same thing everywhere),
        // and they never depend on a per-case ScaleLength. A target below the
        // certified reference floor is still skipped by the report, per scenario.
        options.RelTargets = new double[0];
        options.AbsTargets = new[] { 100d, 10d, 1d };
        // The Kepler weight is resolved once per scenario and pinned; see StudyCalibration.
        // Run adaptive methods once with their own step choice and once with the production cap:
        // a binding cap makes the cost independent of rtol.
        options.AdaptiveMaxSteps = new[] { double.PositiveInfinity, 600d };
        if (StudyBridgeEnv("P1B_NO_REFINE") == "1")
        {
            options.RefineTies = false;
        }

        var results = new List<StudyScenarioResult>();
        var endResults = new List<StudyScenarioResult>();
        foreach (StudyShipMatrixCase c in cases)
        {
            if (!StudyBridgeScenarioSelected(c.Label, scenarioFilter))
            {
                continue;
            }

            StudyShipMatrixSetTargets(c);
            StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(c);
            StudyProblem problem = StudyBridgeShipProblem(c, refs);
            StudyBridgeAttachSecondReference(problem);
            Console.WriteLine();
            Console.WriteLine("sweeping: " + c.Label);
            StudyScenarioResult r = StudySweepRunner.Run(problem, integrators, options, Console.Out);
            results.Add(r);
            StudyReport.Print(Console.Out, r, options);
            StudyScenarioResult rEnd = StudySweepRunner.RunEndOnly(problem, integrators, options, null);
            endResults.Add(rEnd);
            Console.WriteLine("  end-only: " + rEnd.Rows.Count.ToString(CultureInfo.InvariantCulture) + " points");
        }

        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        StudyReport.WriteCsv(Path.Combine(dir, "Test552_FrameworkShipSweep.csv"), results);
        StudyReport.WriteCsv(Path.Combine(dir, "Test552_FrameworkShipSweep_endonly.csv"), endResults);
        StudyReport.WriteCellsCsv(Path.Combine(dir, "Test552_FrameworkShipSweep_cells.csv"),
            results, endResults, options);
        if (!StudyBridgeSkipSummaries())
        {
            StudyReport.WriteMarkdown(Path.Combine(dir, "Test552_FrameworkShipSweep.md"), results, options);
            StudyModeSummary.Write(Path.Combine(dir, "Test552_ModeSummary.md"), "Test552", results, options);
        }

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

        // A sweep that ran but ranked nothing is not a successful comparison.
        // Every certified scenario must (a) have a physical length scale and (b)
        // have at least one target above the reference floor margin and at least one
        // integrator cell that actually reaches it. The uncertified flyby is exempt
        // from the ranking check; its report line already says "NOT certified".
        int badScale = 0;
        int emptyCertified = 0;
        foreach (StudyScenarioResult r in results)
        {
            if (!(r.Problem.ScaleLength > 1d) || !double.IsFinite(r.Problem.ScaleLength))
            {
                badScale++;
                Console.WriteLine("BAD SCALE " + r.Problem.Label + ": "
                    + r.Problem.ScaleLength.ToString("E3", CultureInfo.InvariantCulture) + " m");
            }

            if (!r.Problem.Certified)
            {
                continue;
            }

            double floor = r.Problem.Floor;
            int targetsAboveFloor = 0;
            foreach (StudyTarget t in options.Targets(r.Problem))
            {
                if (t.Value >= options.FloorMargin * floor)
                {
                    targetsAboveFloor++;
                }
            }

            bool ranked = false;
            foreach (string key in StudyReport.Keys(r.Rows))
            {
                foreach (StudyTarget t in options.Targets(r.Problem))
                {
                    StudyCell cell = StudyReport.CellFor(r, key, t, options);
                    if (cell.Status == "OK" || cell.Status == "EDGE")
                    {
                        ranked = true;
                        break;
                    }
                }

                if (ranked)
                {
                    break;
                }
            }

            if (targetsAboveFloor == 0 || !ranked)
            {
                emptyCertified++;
                Console.WriteLine("EMPTY " + r.Problem.Label + ": targets above floor "
                    + targetsAboveFloor.ToString(CultureInfo.InvariantCulture)
                    + ", ranked=" + ranked + ", floor="
                    + floor.ToString("E3", CultureInfo.InvariantCulture) + " m");
            }
        }

        Check(results.Count > 0, "framework ship sweep ran scenarios",
            results.Count.ToString(CultureInfo.InvariantCulture) + " scenario(s); filter=" + (scenarioFilter ?? "none"));
        Check(failedRuns == 0, "framework ship sweep: no failed runs",
            failedRuns.ToString(CultureInfo.InvariantCulture) + " failed run(s)");
        Check(badScale == 0, "framework ship sweep: every scenario has a physical scale length",
            badScale.ToString(CultureInfo.InvariantCulture) + " bad scale(s)");
        Check(emptyCertified == 0, "framework ship sweep: every certified scenario ranks at least one cell",
            emptyCertified.ToString(CultureInfo.InvariantCulture) + " empty certified scenario(s)");
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

    /// <summary>
    /// Build a second reference of a different family (IAS15, Gauss-Radau) and use
    /// max|ref_A - ref_B| over the checkpoints as an additional floor. The grading
    /// reference A is NOT replaced, so candidate errors stay comparable.
    /// Budget: 20M RHS and 300 s wall clock per scenario; a failure is recorded,
    /// not hidden.
    /// </summary>
    private static void StudyBridgeAttachSecondReference(StudyProblem p)
    {
        if (!p.Certified || double.IsNaN(p.Floor))
        {
            p.ReferenceIndependent = false;
            p.ReferenceResolution = "not_certified";
            return;
        }

        var integ = new Ias15Integrator();
        string why;
        if (!integ.Supports(p, out why))
        {
            p.ReferenceIndependent = false;
            p.ReferenceSecondMethod = "IAS15 unsupported: " + why;
            return;
        }

        try
        {
            StudyConfig cfg = integ.MakeConfig(p, 1e-13d, double.PositiveInfinity);
            var ctx = new StudyContext(p, 20000000L, 300000d);
            StudyRun run = integ.Run(p, cfg, ctx);
            if (run.Aborted || run.Samples == null || run.Samples.Length != p.CheckpointTimes.Length)
            {
                p.ReferenceIndependent = false;
                p.ReferenceSecondMethod = "IAS15 did not complete"
                    + (string.IsNullOrEmpty(run.Abort) ? "" : ": " + run.Abort);
                return;
            }

            double cross = 0d;
            for (int k = 0; k < p.CheckpointTimes.Length; k++)
            {
                if (run.Samples[k] == null)
                {
                    p.ReferenceIndependent = false;
                    p.ReferenceSecondMethod = "IAS15 left checkpoint " + k + " unrecorded";
                    return;
                }

                double d = p.Error(run.Samples[k], p.CheckpointRef[k]);
                if (!double.IsFinite(d))
                {
                    p.ReferenceIndependent = false;
                    p.ReferenceSecondMethod = "IAS15 cross-difference is not finite at checkpoint " + k;
                    return;
                }

                cross = Math.Max(cross, d);
            }

            p.ReferenceIndependent = true;
            p.ReferenceCrossDiffM = cross;
            p.ReferenceSecondMethod = "IAS15 rtol 1e-13";
            p.Floor = Math.Max(p.Floor, cross);
            p.Certified = double.IsFinite(p.Floor);
        }
        catch (Exception ex)
        {
            p.ReferenceIndependent = false;
            p.ReferenceSecondMethod = "IAS15 failed: " + ex.GetType().Name + ": " + ex.Message;
        }
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
        if (label.IndexOf("Tight binary", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            p.Mode = "n-body-close";
            p.ModeTags = new[] { "tight-binary", "n-body", "no-central", "close-pair", "long" };
        }
        else if (label.IndexOf("Heliocentric", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            p.Mode = "heliocentric";
            p.ModeTags = new[] { "sun", "jupiter", "probe", "heliocentric", "central", "long" };
        }
        else if (label.IndexOf("comet", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            p.Mode = "n-body-eccentric";
            p.ModeTags = new[] { "sun", "comet", "jupiter", "eccentric", "n-body", "no-central", "long" };
        }
        else
        {
            p.Mode = "n-body";
            p.ModeTags = new[] { "real-solar", "n-body", "no-central", "long", "planets" };
        }

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
        p.ReferenceNote = "independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference";
        p.ReferenceMethod = "dd-Taylor 43200 s / 21600 s";
        p.ReferenceFamily = "Taylor";
        p.ReferenceIndependent = true;
        p.ReferenceResolution = p.Certified ? "ratio_ok" : "not_certified";
        p.ReferenceCrossDiffM = floor;
        p.ReferenceSecondMethod = "dd-Taylor (other step)";

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
        StudyBridgeAttachAdaptiveScales(p, nb);
        return p;
    }

    /// <summary>
    /// The planet state is barycentric, but the error metric is the worst relative pair
    /// separation. Scaling the adaptive error norm by a barycentric radius would let
    /// rtol = 1e-3 lose ~1e8 m per step on the Moon's 3.8e8 m orbit (and the controller
    /// then accepts an ejected Moon as a finite state). Use each body's distance to its
    /// nearest neighbour, and that pair's circular speed for the velocity block, so rtol
    /// means a fraction of the local dynamical scale.
    /// </summary>
    private static void StudyBridgeAttachAdaptiveScales(StudyProblem p, StudyNBody nb)
    {
        int n = nb.Count;
        int[][] blocks = p.GetBlocks();
        var scales = new double[blocks.Length];
        double minimumPair = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            int nearest = -1;
            double nearestDistance = double.PositiveInfinity;
            for (int j = 0; j < n; j++)
            {
                if (j == i)
                {
                    continue;
                }

                double dx = nb.P0[3 * i] - nb.P0[3 * j];
                double dy = nb.P0[(3 * i) + 1] - nb.P0[(3 * j) + 1];
                double dz = nb.P0[(3 * i) + 2] - nb.P0[(3 * j) + 2];
                double d = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearest = j;
                }
            }

            if (nearest >= 0 && nearestDistance > 0d && double.IsFinite(nearestDistance))
            {
                scales[i] = nearestDistance;
                minimumPair = Math.Min(minimumPair, nearestDistance);
                if (n + i < scales.Length)
                {
                    scales[n + i] = Math.Sqrt((nb.Mu[i] + nb.Mu[nearest]) / nearestDistance);
                }
            }
        }

        p.AdaptiveScales = scales;
        p.DomainViolation = delegate (double[] y)
        {
            double minPair = double.PositiveInfinity;
            for (int i = 1; i < n; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    double dx = y[3 * i] - y[3 * j];
                    double dy = y[(3 * i) + 1] - y[(3 * j) + 1];
                    double dz = y[(3 * i) + 2] - y[(3 * j) + 2];
                    double d = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                    if (d < minPair)
                    {
                        minPair = d;
                    }
                }
            }

            if (minPair >= 1e-4d * minimumPair)
            {
                return null;
            }

            return "a pair distance collapsed to "
                + minPair.ToString("E2", CultureInfo.InvariantCulture)
                + " m (initial minimum "
                + minimumPair.ToString("E2", CultureInfo.InvariantCulture)
                + " m): the trajectory left the physical domain";
        };
    }

    private static int Test553_IntegratorFrameworkPlanetSweep()
    {
        Console.WriteLine("--- Integrator framework: planet-orbit work-precision sweep ---");
        List<IStudyIntegrator> integrators = StudyRegistry.Select(StudyBridgeEnv("P1B_INTEGRATORS"));
        var options = new StudyOptions();
        options.RelTargets = new double[0];
        options.AbsTargets = new[] { 100d, 10d, 1d };       // metres, worst pair error over the horizon
        options.AdaptiveMaxSteps = new[] { double.PositiveInfinity, 86400d };
        if (StudyBridgeEnv("P1B_NO_REFINE") == "1")
        {
            options.RefineTies = false;
        }

        string scenarioFilter = StudyBridgeEnv("P1B_PLANET_SCENARIOS");
        string yearsEnv = StudyBridgeEnv("P1B_PLANET_YEARS");
        var results = new List<StudyScenarioResult>();
        var endResults = new List<StudyScenarioResult>();

        void RunPlanet(string label, StarSystem sys, double years)
        {
            if (!StudyBridgeScenarioSelected(label, scenarioFilter))
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("sweeping: " + label);
            long savedBudget = options.MaxCostRhs;
            if (years >= 100d)
            {
                // The 100-year rows were censored at 2 M before they could bracket
                // 100 m; give this horizon more room. The override exists because
                // the fixed-step rows are hopeless here and only burn the budget.
                string budgetEnv = StudyBridgeEnv("P1B_PLANET_BUDGET");
                options.MaxCostRhs = budgetEnv != null
                    ? long.Parse(budgetEnv, CultureInfo.InvariantCulture)
                    : 5000000L;
            }

            try
            {
                StudyScenarioResult r = StudyBridgeRunPlanetScenario(
                    label, sys, years, 30d * 86400d, integrators, options, endResults);
                results.Add(r);
                StudyReport.Print(Console.Out, r, options);
            }
            finally
            {
                options.MaxCostRhs = savedBudget;
            }
        }

        void RunHeliocentric(string label, double years)
        {
            if (!StudyBridgeScenarioSelected(label, scenarioFilter))
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("sweeping: " + label);
            StudyScenarioResult r = StudyBridgeRunHeliocentricScenario(
                label, years, 30d * 86400d, integrators, options, endResults);
            results.Add(r);
            StudyReport.Print(Console.Out, r, options);
        }

        if (yearsEnv != null)
        {
            // Backward-compatible shortcut: one real-solar scenario at the requested horizon.
            double years = double.Parse(yearsEnv, CultureInfo.InvariantCulture);
            RunPlanet("Real solar 7-body, " + years.ToString("G", CultureInfo.InvariantCulture) + " y",
                StudyRealSolar7(), years);
        }
        else
        {
            RunPlanet("Real solar 7-body, 10 y", StudyRealSolar7(), 10d);
            RunPlanet("Real solar 7-body, 100 y", StudyRealSolar7(), 100d);
            RunPlanet("Custom 6-body, 10 y", StudyCustomSystem(), 10d);
            RunPlanet("Tight binary, 10 y", StudyTightBinarySystem(), 10d);
            RunPlanet("Sun + eccentric comet + Jupiter, 10 y", StudyEccentricSystem(), 10d);
            RunHeliocentric("Heliocentric Sun-Jupiter probe, 10 y", 10d);
        }

        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        StudyReport.WriteCsv(Path.Combine(dir, "Test553_FrameworkPlanetSweep.csv"), results);
        StudyReport.WriteCsv(Path.Combine(dir, "Test553_FrameworkPlanetSweep_endonly.csv"), endResults);
        StudyReport.WriteCellsCsv(Path.Combine(dir, "Test553_FrameworkPlanetSweep_cells.csv"),
            results, endResults, options);
        if (!StudyBridgeSkipSummaries())
        {
            StudyReport.WriteMarkdown(Path.Combine(dir, "Test553_FrameworkPlanetSweep.md"), results, options);
            StudyModeSummary.Write(Path.Combine(dir, "Test553_ModeSummary.md"), "Test553", results, options);
        }

        int failedRuns = 0;
        foreach (StudyScenarioResult res in results)
        {
            foreach (StudyRow row in res.Rows)
            {
                if (row.Status == "error")
                {
                    failedRuns++;
                    Console.WriteLine("FAILED RUN " + res.Problem.Label + " / " + row.Key + " / "
                        + row.Config + ": " + row.Note);
                }
            }
        }

        Check(results.Count > 0, "framework planet sweep ran scenarios",
            results.Count.ToString(CultureInfo.InvariantCulture) + " scenario(s); filter=" + (scenarioFilter ?? "none"));
        Check(failedRuns == 0, "framework planet sweep: no failed runs",
            failedRuns.ToString(CultureInfo.InvariantCulture) + " failed run(s)");

        // A green planet sweep must actually compare adaptive methods: on every
        // scenario at least one adaptive method must reach a target above the
        // reference floor. Not every method has to: on a hard 100-year or
        // tight-pair problem one adaptive controller may legitimately collapse
        // while another still converges. The per-method outcome is printed in
        // the detail, so a failure is not silently ranked against nothing.
        foreach (StudyScenarioResult res in results)
        {
            bool anyReached = false;
            string details = "";
            foreach (IStudyIntegrator integ in integrators)
            {
                if (integ.Kind != StudyKind.Adaptive)
                {
                    continue;
                }

                string supportReason;
                if (!integ.Supports(res.Problem, out supportReason))
                {
                    continue;
                }

                bool reached = false;
                double bestErr = double.PositiveInfinity;
                double bestControl = double.NaN;
                foreach (string key in StudyReport.Keys(res.Rows))
                {
                    if (key != integ.Name && !key.StartsWith(integ.Name + " [", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (StudyRow row in res.Rows)
                    {
                        if (row.Key == key && row.Status == "ok" && row.Finite && row.Err < bestErr)
                        {
                            bestErr = row.Err;
                            bestControl = row.Control;
                        }
                    }

                    foreach (StudyTarget t in options.Targets(res.Problem))
                    {
                        StudyCell cell = StudyReport.CellFor(res, key, t, options);
                        if (cell.Status == "OK" || cell.Status == "EDGE")
                        {
                            reached = true;
                        }
                    }
                }

                anyReached |= reached;
                details += (details.Length == 0 ? "" : "; ") + integ.Name + "="
                    + (reached ? "reached" : "not reached")
                    + (double.IsFinite(bestErr)
                        ? " (best " + bestErr.ToString("E3", CultureInfo.InvariantCulture) + " m at "
                          + bestControl.ToString("G3", CultureInfo.InvariantCulture) + ")"
                        : " (no finite run)");
            }

            Check(anyReached, "framework planet sweep: an adaptive method reaches a target: "
                + res.Problem.Label, details.Length == 0 ? "no adaptive method supported" : details);
        }

        Console.WriteLine("report: " + Path.Combine(dir, "Test553_FrameworkPlanetSweep.md"));
        return 0;
    }

    private static StudyScenarioResult StudyBridgeRunPlanetScenario(
        string label, StarSystem sys, double years, double sampleEvery,
        List<IStudyIntegrator> integrators, StudyOptions options,
        List<StudyScenarioResult> endResults)
    {
        StudyNBody nb = StudyMakeNBody(sys);
        StudyRefResult reference = null;
        StudyRefResult alternative = null;
        // The two independent dd-Taylor references dominate the runtime; run them together.
        Parallel.Invoke(
            () => reference = StudyPlanetIndependentReference(nb, years, sampleEvery),
            () => alternative = StudyPlanetIndependentReferenceAlt(nb, years, sampleEvery));
        StudyProblem problem = StudyBridgePlanetProblem(label, nb, years, reference, alternative);
        StudyScenarioResult r = StudySweepRunner.Run(problem, integrators, options, Console.Out);
        if (endResults != null)
        {
            endResults.Add(StudySweepRunner.RunEndOnly(problem, integrators, options, null));
        }

        return r;
    }

    private static double[] StudyBridgeRelativeState(double[] p, double[] v, int body, int centre)
    {
        var state = new double[6];
        for (int i = 0; i < 3; i++)
        {
            state[i] = p[(3 * body) + i] - p[(3 * centre) + i];
            state[3 + i] = v[(3 * body) + i] - v[(3 * centre) + i];
        }

        return state;
    }

    /// <summary>
    /// Probe state relative to the Sun, with Jupiter's exact two-body rail as the
    /// only perturbation. This is the heliocentric form the production local step
    /// uses, and the one form in the planet sweep where WH-Y6 is valid.
    /// </summary>
    private static StudyProblem StudyBridgeHeliocentricProblem(
        string label, StarSystem sys, StudyNBody nb, int sunIndex, int jupiterIndex, int probeIndex,
        StudyRefResult reference, StudyRefResult alternative)
    {
        double muSun = nb.Mu[sunIndex];
        double muJupiter = nb.Mu[jupiterIndex];
        OrbitingBody sun = sys.AllBodies[sunIndex];
        OrbitingBody jupiter = sys.AllBodies[jupiterIndex];
        double[] y0 = StudyBridgeRelativeState(nb.P0, nb.V0, probeIndex, sunIndex);

        var p = new StudyProblem();
        p.Label = label;
        p.Mode = "heliocentric";
        p.ModeTags = new[] { "sun", "jupiter", "probe", "heliocentric", "central", "long" };
        p.Dim = 6;
        p.PosDim = 3;
        p.T0 = 0d;
        p.Y0 = y0;
        p.ConservativeSecondOrder = true;
        p.CentralMu = muSun;
        p.Rhs = delegate (double t, double[] y, double[] dy)
        {
            sys.EvaluateBodyState(jupiter, t, out Vector3d jp, out _);
            sys.EvaluateBodyState(sun, t, out Vector3d sp, out _);
            Vector3d rj = jp - sp;
            var r = new Vector3d(y[0], y[1], y[2]);
            double r2 = (r.X * r.X) + (r.Y * r.Y) + (r.Z * r.Z);
            double r3 = r2 * Math.Sqrt(r2);
            Vector3d d = rj - r;
            double d2 = (d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z);
            double d3 = d2 * Math.Sqrt(d2);
            double rj2 = (rj.X * rj.X) + (rj.Y * rj.Y) + (rj.Z * rj.Z);
            double rj3 = rj2 * Math.Sqrt(rj2);
            Vector3d a = (r * (-muSun / r3)) + (d * (muJupiter / d3)) + (rj * (-muJupiter / rj3));
            dy[0] = y[3];
            dy[1] = y[4];
            dy[2] = y[5];
            dy[3] = a.X;
            dy[4] = a.Y;
            dy[5] = a.Z;
        };

        var times = new List<double>();
        var states = new List<double[]>();
        var altStates = new List<double[]>();
        bool haveAlt = alternative != null && alternative.Samples != null
            && reference.Samples != null && alternative.Samples.Count == reference.Samples.Count;
        if (reference.Samples != null)
        {
            for (int k = 0; k < reference.Samples.Count; k++)
            {
                StudySample s = reference.Samples[k];
                times.Add(s.T);
                states.Add(StudyBridgeRelativeState(s.P, s.V, probeIndex, sunIndex));
                if (haveAlt)
                {
                    StudySample a = alternative.Samples[k];
                    altStates.Add(StudyBridgeRelativeState(a.P, a.V, probeIndex, sunIndex));
                }
            }
        }

        if (times.Count == 0)
        {
            times.Add(reference.Samples != null && reference.Samples.Count > 0
                ? reference.Samples[reference.Samples.Count - 1].T : 0d);
            states.Add(StudyBridgeRelativeState(reference.P, reference.V, probeIndex, sunIndex));
            if (haveAlt)
            {
                altStates.Add(StudyBridgeRelativeState(alternative.P, alternative.V, probeIndex, sunIndex));
            }
        }

        p.CheckpointTimes = times.ToArray();
        p.CheckpointRef = states.ToArray();

        double floor = 0d;
        double scale = 1d;
        for (int k = 0; k < states.Count; k++)
        {
            for (int i = 0; i < 3; i++)
            {
                scale = Math.Max(scale, Math.Abs(states[k][i]));
            }

            if (haveAlt && k < altStates.Count)
            {
                floor = Math.Max(floor, p.Error(states[k], altStates[k]));
            }
        }

        p.Floor = Math.Max(floor, ToleranceReference.FloatingPointFloor * scale);
        p.Certified = true;
        p.ScaleLength = scale;
        p.ReferenceMethod = "dd-Taylor 43200 s / 21600 s";
        p.ReferenceFamily = "Taylor";
        p.ReferenceIndependent = true;
        p.ReferenceResolution = p.Certified ? "ratio_ok" : "not_certified";
        p.ReferenceCrossDiffM = floor;
        p.ReferenceSecondMethod = "dd-Taylor (other step)";

        double r0 = Math.Sqrt((y0[0] * y0[0]) + (y0[1] * y0[1]) + (y0[2] * y0[2]));
        double v2 = (y0[3] * y0[3]) + (y0[4] * y0[4]) + (y0[5] * y0[5]);
        double a0 = 1d / Math.Max(1e-30d, (2d / Math.Max(r0, 1d)) - (v2 / muSun));
        p.CharTime = 2d * Math.PI * Math.Sqrt(a0 * a0 * a0 / muSun);
        return p;
    }

    private static StudyScenarioResult StudyBridgeRunHeliocentricScenario(
        string label, double years, double sampleEvery,
        List<IStudyIntegrator> integrators, StudyOptions options,
        List<StudyScenarioResult> endResults)
    {
        StarSystem sys = StudyHeliocentricSystem();
        StudyNBody nb = StudyMakeNBody(sys);
        int sun = 0;
        int jupiter = 1;
        int probe = 2;
        StudyRefResult reference = null;
        StudyRefResult alternative = null;
        Parallel.Invoke(
            () => reference = StudyPlanetIndependentReference(nb, years, sampleEvery),
            () => alternative = StudyPlanetIndependentReferenceAlt(nb, years, sampleEvery));
        StudyProblem problem = StudyBridgeHeliocentricProblem(
            label, sys, nb, sun, jupiter, probe, reference, alternative);
        StudyScenarioResult r = StudySweepRunner.Run(problem, integrators, options, Console.Out);
        if (endResults != null)
        {
            endResults.Add(StudySweepRunner.RunEndOnly(problem, integrators, options, null));
        }

        return r;
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
