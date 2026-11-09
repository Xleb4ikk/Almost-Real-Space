using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private const double MoonMu = 4.9028e12d;
    private const double MoonRadius = 1.737e6d;

    /// <summary>
    /// Pure-gravity regime suite. No thrust, no atmosphere, no landing burn.
    /// Every scenario has a real perturbation (Earth third body, Jupiter, Sun tide,
    /// or a small-body tide), so a pure-Kepler solver cannot win trivially.
    /// </summary>
    private static int Test559_PureGravityRegimes()
    {
        Console.WriteLine("--- Integrator framework: pure-gravity regimes ---");
        var problems = new List<StudyProblem>();
        StarSystem solar = StudyShipMatrixSystem(false, false);

        // Moon-centred low orbits, Earth third body.
        foreach (double altitude in new[] { 1d, 10d, 100d, 1000d })
        {
            problems.Add(MoonOrbitProblem(solar, altitude, 1));
        }

        // Long lunar orbits at 100 km: enough phase error for 1 m to resolve.
        foreach (int orbits in new[] { 10, 100, 1000 })
        {
            problems.Add(MoonOrbitProblem(solar, 100d, orbits));
        }

        // Moon ballistic hops, small to near-circular.
        foreach (double speed in new[] { 50d, 200d, 500d, 1000d, 1200d, 1400d, 1500d, 1600d })
        {
            problems.Add(MoonHopProblem(solar, speed));
        }

        // Moon flybys: v_inf x periapsis altitude.
        foreach (double vinf in new[] { 50d, 200d, 500d, 1000d, 3000d, 10000d })
        {
            foreach (double altitude in new[] { 1d, 10d, 100d, 1000d })
            {
                problems.Add(MoonFlybyProblem(solar, vinf, altitude));
            }
        }

        // Heliocentric orbits with Jupiter perturbation.
        foreach (double au in new[] { 0.3d, 1d, 5d, 30d })
        {
            foreach (double e in new[] { 0d, 0.5d, 0.9d, 0.99d })
            {
                problems.Add(HeliocentricProblem(solar, au, e));
            }
        }

        // Far flight in physical and normalized coordinates.
        StudyShipMatrixCase far = FarFlightCase(solar);
        StudyShipMatrixSetTargets(far);
        StudyShipMatrixRefs farRefs = StudyShipMatrixBuildReferences(far);
        StudyProblem farProblem = StudyBridgeShipProblem(far, farRefs);
        StudyBridgeAttachSecondReference(farProblem);
        problems.Add(farProblem);
        StudyProblem farNormalized = NormalizedFromCase(far, farRefs);
        StudyBridgeAttachSecondReference(farNormalized);
        problems.Add(farNormalized);

        // Planet flybys: periapsis in planet radii, low/high v_inf, solar tide.
        foreach (double factor in new[] { 1.1d, 3d, 10d })
        {
            foreach (double vinf in new[] { 1000d, 10000d })
            {
                problems.Add(PlanetFlybyProblem(solar, factor, vinf));
            }
        }

        // Small-body flybys: small mu, v_inf 10-100 m/s, Sun tide.
        foreach (double vinf in new[] { 10d, 50d, 100d })
        {
            problems.Add(SmallBodyFlybyProblem(vinf));
        }

        if (StudyBridgeEnv("P1B_SEAM_PROBE") == "1")
        {
            string probeDir = StudyShipMatrixResultsDir();
            Directory.CreateDirectory(probeDir);
            string probeCsv = Path.Combine(probeDir, "Test559_SeamProbe.csv");
            StudyProblem moon = problems.Find(x => x.Label == "Moon orbit 100 km x1000 orbits");
            StudyProblem flyby = problems.Find(x => x.Label == "Moon flyby vinf 1000 alt 100");
            if (moon != null)
            {
                StudySeamProbe.Run(Console.Out, probeCsv, moon.Label, solar, moon,
                    1e-11d, 0.001d, 1570.0337936401368d);
            }

            if (flyby != null)
            {
                StudySeamProbe.Run(Console.Out, probeCsv, flyby.Label, solar, flyby,
                    1e-11d, 0.0001d, 2292.3905988282368d);
            }

            Console.WriteLine("seam probe: " + probeCsv);
            return 0;
        }

        var options = new StudyOptions();
        options.RelTargets = new double[0];
        options.AbsTargets = new[] { 100d, 10d, 1d, 0.1d, 0.01d, 0.001d };
        options.AdaptiveMaxSteps = new[] { double.PositiveInfinity, 600d };
        // Extend the coarse end downward: a target that is already met at rtol=10
        // or at one fixed step is floor-limited, not an equivalent method.
        options.AdaptiveControlMax = 10d;
        options.MaxCostRhs = 2000000L;
        options.MaxPoints = 50;
        if (StudyBridgeEnv("P1B_NO_REFINE") == "1")
        {
            options.RefineTies = false;
        }

        string maxPointsEnv = StudyBridgeEnv("P1B_MAX_POINTS");
        if (maxPointsEnv != null)
        {
            options.MaxPoints = int.Parse(maxPointsEnv, CultureInfo.InvariantCulture);
        }
        string ratioEnv = StudyBridgeEnv("P1B_GRID_RATIO");
        if (ratioEnv != null)
        {
            options.GridRatio = double.Parse(ratioEnv, CultureInfo.InvariantCulture);
        }

        string suffix = StudyBridgeEnv("P1B_OUTPUT_SUFFIX") ?? "";

        string filter = StudyBridgeEnv("P1B_SCENARIOS");
        List<IStudyIntegrator> integrators = StudyRegistry.Select(StudyBridgeEnv("P1B_INTEGRATORS"));
        var results = new List<StudyScenarioResult>();
        var endResults = new List<StudyScenarioResult>();
        foreach (StudyProblem p in problems)
        {
            if (!StudyBridgeScenarioSelected(p.Label, filter))
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine("sweeping: " + p.Label);
            StudyScenarioResult r = StudySweepRunner.Run(p, integrators, options, Console.Out);
            results.Add(r);
            StudyReport.Print(Console.Out, r, options);
            StudyScenarioResult rEnd = StudySweepRunner.RunEndOnly(p, integrators, options, null);
            endResults.Add(rEnd);
            Console.WriteLine("  end-only: " + rEnd.Rows.Count.ToString(CultureInfo.InvariantCulture) + " points");
        }

        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        string csvPath = Path.Combine(dir, "Test559_PureGravity" + suffix + ".csv");
        StudyReport.WriteCsv(csvPath, results);
        StudyReport.WriteCsv(Path.Combine(dir, "Test559_PureGravity" + suffix + "_endonly.csv"), endResults);
        StudyReport.WriteCellsCsv(Path.Combine(dir, "Test559_PureGravity" + suffix + "_cells.csv"),
            results, endResults, options);
        if (!StudyBridgeSkipSummaries())
        {
            StudyReport.WriteMarkdown(Path.Combine(dir, "Test559_PureGravity" + suffix + ".md"), results, options);
            string tieCsv = Path.Combine(dir, "Test559_PureGravity_tie115.csv");
            string[] driftCsv =
            {
                Path.Combine(dir, "Test552_FrameworkShipSweep.csv"),
                Path.Combine(dir, "Test558_J2AndSun.csv"),
                csvPath
            };
            StudyRegimeAnalysis.WriteAll(
                csvPath, dir, "Test559" + suffix, options,
                suffix.Length == 0 ? tieCsv : null, driftCsv);
        }

        int failed = 0;
        int uncertified = 0;
        foreach (StudyScenarioResult r in results)
        {
            foreach (StudyRow row in r.Rows)
            {
                if (row.Status == "error")
                {
                    failed++;
                    Console.WriteLine("FAILED RUN " + row.Scenario + " / " + row.Key + ": " + row.Note);
                }
            }

            if (!r.Problem.Certified)
            {
                uncertified++;
                Console.WriteLine("UNCERTIFIED " + r.Problem.Label + ": " + r.Problem.ReferenceNote);
            }
        }

        Check(results.Count > 0, "pure-gravity suite ran",
            results.Count.ToString(CultureInfo.InvariantCulture));
        Check(failed == 0, "pure-gravity suite: no failed runs",
            failed.ToString(CultureInfo.InvariantCulture));
        Check(uncertified == 0, "pure-gravity suite: every scenario certified",
            uncertified.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("report: " + Path.Combine(dir, "Test559_PureGravity" + suffix + ".md"));
        return 0;
    }

    private static StudyProblem MoonOrbitProblem(StarSystem sys, double altitudeKm, int orbits)
    {
        double rp = MoonRadius + altitudeKm * 1000d;
        double period = 2d * Math.PI * Math.Sqrt(rp * rp * rp / MoonMu);
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            "Moon orbit " + altitudeKm.ToString("G", CultureInfo.InvariantCulture) + " km"
                + (orbits > 1 ? " x" + orbits.ToString(CultureInfo.InvariantCulture) + " orbits" : ""),
            sys, 2, rp, rp, 0d, 0d, 0d, period * orbits, true, false, 0.25d);
        c.ScaleLength = rp;
        c.Mode = "moon-orbit";
        c.ModeTags = new[]
        {
            "moon", "orbit", "third-body-earth",
            "distance-radii=" + (rp / MoonRadius).ToString("G4", CultureInfo.InvariantCulture),
            "orbits=" + orbits.ToString(CultureInfo.InvariantCulture)
        };
        c.AltitudeKm = altitudeKm;
        c.SpeedMps = Math.Sqrt(MoonMu / rp);
        c.Eccentricity = 0d;
        c.AllowUnratioedResolution = true;
        return BuildPureProblem(c);
    }

    private static StudyProblem MoonHopProblem(StarSystem sys, double up)
    {
        double r = MoonRadius + 1000d;
        double g = 1.62d;
        double hMax = up * up / (2d * g);
        double dg = 1d - (MoonRadius / (MoonRadius + hMax)) * (MoonRadius / (MoonRadius + hMax));
        var tags = new List<string>
        {
            "moon", "ballistic-hop", "third-body-earth",
            "speed=" + up.ToString("G", CultureInfo.InvariantCulture),
            "delta-g=" + dg.ToString("G3", CultureInfo.InvariantCulture)
        };
        if (dg < 0.01d)
        {
            tags.Add("near-uniform-field");
        }

        var c = new StudyShipMatrixCase
        {
            Label = "Moon hop " + up.ToString("G", CultureInfo.InvariantCulture) + " m/s",
            Sys = sys,
            Central = 2,
            P0 = new Vector3d(r, 0d, 0d),
            V0 = new Vector3d(0d, up, 1d),
            Mass0 = 1000d,
            Seconds = Math.Min(2d * up / 1.62d, 3600d),
            Tau = Math.Min(2d * up / 1.62d / 4d, 900d),
            StepScale = 0.25d,
            ScaleLength = r,
            Perturbed = true,
            AllowTaylor = false,
            Mode = "moon-hop",
            ModeTags = tags.ToArray(),
            AltitudeKm = 1d,
            SpeedMps = up,
            Eccentricity = double.NaN,
            AllowUnratioedResolution = true
        };
        return BuildPureProblem(c);
    }

    private static StudyProblem MoonFlybyProblem(StarSystem sys, double vinf, double altitudeKm)
    {
        double rp = MoonRadius + altitudeKm * 1000d;
        double vper = Math.Sqrt(vinf * vinf + 2d * MoonMu / rp);
        double tau = 2d * Math.PI * rp / vper;
        var c = new StudyShipMatrixCase
        {
            Label = "Moon flyby vinf " + vinf.ToString("G", CultureInfo.InvariantCulture)
                + " alt " + altitudeKm.ToString("G", CultureInfo.InvariantCulture),
            Sys = sys,
            Central = 2,
            P0 = new Vector3d(rp, 0d, 0d),
            V0 = new Vector3d(0d, vper, 0d),
            Mass0 = 1000d,
            Seconds = Math.Min(5d * 86400d, 20d * tau),
            Tau = tau,
            StepScale = 0.25d,
            ScaleLength = rp,
            Perturbed = true,
            AllowTaylor = false,
            Mode = "moon-flyby",
            ModeTags = new[]
            {
                "moon", "flyby", "third-body-earth",
                "vinf=" + vinf.ToString("G", CultureInfo.InvariantCulture),
                "periapsis-km=" + altitudeKm.ToString("G", CultureInfo.InvariantCulture)
            },
            AltitudeKm = altitudeKm,
            SpeedMps = vper,
            Eccentricity = double.NaN,
            AllowUnratioedResolution = true
        };
        return BuildPureProblem(c);
    }

    private static StudyProblem HeliocentricProblem(StarSystem sys, double au, double e)
    {
        double mu = sys.AllBodies[0].ResolveStandardGravitationalParameter();
        double a = au * StudyAU;
        double rp = a * (1d - e);
        double ra = a * (1d + e);
        double period = 2d * Math.PI * Math.Sqrt(a * a * a / mu);
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            "Heliocentric " + au.ToString("G", CultureInfo.InvariantCulture) + " AU e "
                + e.ToString("G", CultureInfo.InvariantCulture),
            sys, 0, rp, ra, 0d, 0d, 0d, Math.Min(period, 365.25d * 86400d),
            true, false, 0.25d);
        c.ScaleLength = ra;
        c.CertifiedReferenceStepSeconds = au <= 0.3d ? 300d : (au <= 1d ? 900d : (au <= 5d ? 3600d : 86400d));
        c.Mode = "heliocentric-orbit";
        c.ModeTags = new[]
        {
            "sun", "heliocentric", "jupiter-perturbation",
            "a-au=" + au.ToString("G", CultureInfo.InvariantCulture),
            "e=" + e.ToString("G", CultureInfo.InvariantCulture)
        };
        c.AltitudeKm = double.NaN;
        c.SpeedMps = Math.Sqrt(mu * ((2d / rp) - (1d / a)));
        c.Eccentricity = e;
        c.AllowUnratioedResolution = true;
        return BuildPureProblem(c);
    }

    private static StudyShipMatrixCase FarFlightCase(StarSystem sys)
    {
        double mu = sys.AllBodies[0].ResolveStandardGravitationalParameter();
        double a = 30d * StudyAU;
        double e = 0.5d;
        double rp = a * (1d - e);
        double ra = a * (1d + e);
        double period = 2d * Math.PI * Math.Sqrt(a * a * a / mu);
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            "Far flight 30 AU e 0.5 (physical)",
            sys, 0, rp, ra, 0d, 0d, 0d, Math.Min(period, 365.25d * 86400d),
            true, false, 0.25d);
        c.ScaleLength = ra;
        c.CertifiedReferenceStepSeconds = 86400d;
        c.Mode = "far-flight";
        c.ModeTags = new[] { "sun", "far", "weak-acceleration", "jupiter-perturbation", "physical" };
        c.Eccentricity = e;
        c.AllowUnratioedResolution = true;
        return c;
    }

    private static StudyProblem NormalizedFromCase(StudyShipMatrixCase c, StudyShipMatrixRefs refs)
    {
        const double L = StudyAU;
        const double T = 86400d;
        var p = new StudyProblem
        {
            Label = c.Label + " (normalized)",
            Mode = "far-normalized",
            ModeTags = new[] { "sun", "far", "weak-acceleration", "normalized", "jupiter-perturbation" },
            Dim = 6,
            PosDim = 3,
            T0 = 0d,
            Y0 = new[]
            {
                c.P0.X / L, c.P0.Y / L, c.P0.Z / L,
                c.V0.X * T / L, c.V0.Y * T / L, c.V0.Z * T / L
            },
            ConservativeSecondOrder = true,
            CentralMu = c.Sys.AllBodies[0].ResolveStandardGravitationalParameter() * T * T / (L * L * L),
            ScaleLength = 1d,
            CharTime = (refs.CheckpointTimes[refs.CheckpointTimes.Length - 1] - StudyShipT0) / T,
            Floor = refs.ReferenceFloor,
            Certified = double.IsFinite(refs.ReferenceFloor),
            MaxControl = 1d,
            ReferenceNote = refs.LocalKind + "; normalized state, error converted to meters",
            ReferenceMethod = "fixed DOP853, local frame (normalized)",
            ReferenceFamily = "Runge-Kutta",
            ReferenceIndependent = false,
            ReferenceResolution = refs.LocalUnratioedResolutionUsed
                ? "unratioed_exemption_used"
                : (refs.LocalValid && refs.LocalAsymptotic ? "ratio_ok" : "not_certified"),
            ReferenceCrossDiffM = double.NaN,
            ReferenceSecondMethod = "",
            SpeedMps = c.V0.Magnitude,
            Eccentricity = c.Eccentricity
        };
        p.Rhs = delegate (double t, double[] y, double[] dy)
        {
            var rel = new Vector3d(y[0] * L, y[1] * L, y[2] * L);
            double physicalTime = StudyShipT0 + t * T;
            Vector3d acceleration = StudyShipMatrixLocalAcceleration(c, rel, physicalTime);
            dy[0] = y[3];
            dy[1] = y[4];
            dy[2] = y[5];
            dy[3] = acceleration.X * T * T / L;
            dy[4] = acceleration.Y * T * T / L;
            dy[5] = acceleration.Z * T * T / L;
        };
        p.ErrorFn = delegate (double[] s, double[] r)
        {
            double dx = s[0] - r[0];
            double dy = s[1] - r[1];
            double dz = s[2] - r[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) * L;
        };
        p.CheckpointTimes = new double[refs.CheckpointTimes.Length];
        p.CheckpointRef = new double[refs.CheckpointTimes.Length][];
        for (int i = 0; i < refs.CheckpointTimes.Length; i++)
        {
            p.CheckpointTimes[i] = (refs.CheckpointTimes[i] - StudyShipT0) / T;
            p.CheckpointRef[i] = new[]
            {
                refs.CheckpointP[i].X / L, refs.CheckpointP[i].Y / L, refs.CheckpointP[i].Z / L,
                refs.CheckpointV[i].X * T / L, refs.CheckpointV[i].Y * T / L, refs.CheckpointV[i].Z * T / L
            };
        }

        return p;
    }

    private static StudyProblem PlanetFlybyProblem(StarSystem sys, double factor, double vinf)
    {
        OrbitingBody earth = sys.AllBodies[1];
        double mu = earth.ResolveStandardGravitationalParameter();
        double rp = earth.Radius * factor;
        double vper = Math.Sqrt(vinf * vinf + 2d * mu / rp);
        double tau = 2d * Math.PI * rp / vper;
        var c = new StudyShipMatrixCase
        {
            Label = "Earth flyby " + factor.ToString("G", CultureInfo.InvariantCulture)
                + " R vinf " + vinf.ToString("G", CultureInfo.InvariantCulture),
            Sys = sys,
            Central = 1,
            P0 = new Vector3d(rp, 0d, 0d),
            V0 = new Vector3d(0d, vper, 0d),
            Mass0 = 1000d,
            Seconds = Math.Min(30d * 86400d, 20d * tau),
            Tau = tau,
            StepScale = 0.25d,
            ScaleLength = rp,
            Perturbed = true,
            AllowTaylor = false,
            Mode = "planet-flyby",
            ModeTags = new[]
            {
                "earth", "flyby", "solar-tide",
                "periapsis-radii=" + factor.ToString("G", CultureInfo.InvariantCulture),
                "vinf=" + vinf.ToString("G", CultureInfo.InvariantCulture)
            },
            AltitudeKm = (factor - 1d) * earth.Radius / 1000d,
            SpeedMps = vper,
            Eccentricity = double.NaN,
            AllowUnratioedResolution = true
        };
        return BuildPureProblem(c);
    }

    private static StudyProblem SmallBodyFlybyProblem(double vinf)
    {
        var bp = new SystemBlueprint();
        bp.Bodies.Add(StudyBody("Sun", -1, 1.32712440018e20d, 6.96e8d, 0d, 0d, 0d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Small", 0, 1.0e9d, 1000d, 1.496e11d, 0d, 0d, 0d, 0d, 90d));
        StarSystem sys = bp.Build();
        double mu = 1.0e9d;
        double rp = 2000d;
        double vper = Math.Sqrt(vinf * vinf + 2d * mu / rp);
        double tau = 2d * Math.PI * rp / vper;
        var c = new StudyShipMatrixCase
        {
            Label = "Small-body flyby vinf " + vinf.ToString("G", CultureInfo.InvariantCulture),
            Sys = sys,
            Central = 1,
            P0 = new Vector3d(rp, 0d, 0d),
            V0 = new Vector3d(0d, vper, 0d),
            Mass0 = 1000d,
            Seconds = Math.Min(30d * 86400d, 20d * tau),
            Tau = tau,
            StepScale = 0.25d,
            ScaleLength = rp,
            Perturbed = true,
            AllowTaylor = false,
            Mode = "small-body-flyby",
            ModeTags = new[]
            {
                "small-body", "flyby", "solar-tide",
                "vinf=" + vinf.ToString("G", CultureInfo.InvariantCulture)
            },
            AltitudeKm = 1d,
            SpeedMps = vper,
            Eccentricity = double.NaN,
            AllowUnratioedResolution = true
        };
        return BuildPureProblem(c);
    }

    private static StudyProblem BuildPureProblem(StudyShipMatrixCase c)
    {
        StudyShipMatrixSetTargets(c);
        StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(c);
        StudyProblem p = StudyBridgeShipProblem(c, refs);
        if (NeedsIndependentReference(c.Label))
        {
            AttachIndependentIas15Reference(p);
        }
        else
        {
            StudyBridgeAttachSecondReference(p);
        }

        return p;
    }

    /// <summary>
    /// The fixed DOP853 reference is the same broad family as DOPRI5/DOP853/RKN12.
    /// For the cells where those methods compete, grade against an independent
    /// Gauss-Radau solution: IAS15 at two tolerances. The difference between the two
    /// runs is the reference floor; the finer run supplies the checkpoint states.
    /// </summary>
    private static void AttachIndependentIas15Reference(StudyProblem p)
    {
        var integ = new Ias15Integrator();
        string why;
        if (!integ.Supports(p, out why))
        {
            return;
        }

        StudyRun coarse = RunIas15Reference(p, integ, 1e-12d);
        StudyRun fine = RunIas15Reference(p, integ, 1e-13d);
        if (coarse == null || fine == null || coarse.Samples == null || fine.Samples == null)
        {
            Console.WriteLine("INFO independent IAS15 reference for " + p.Label
                + ": one of the runs did not complete; keeping fixed DOP853 reference");
            return;
        }

        double diff = 0d;
        bool complete = true;
        for (int k = 0; k < p.CheckpointTimes.Length; k++)
        {
            if (fine.Samples[k] == null || coarse.Samples[k] == null)
            {
                complete = false;
                break;
            }

            diff = Math.Max(diff, p.Error(fine.Samples[k], coarse.Samples[k]));
        }

        if (!complete || !double.IsFinite(diff))
        {
            Console.WriteLine("INFO independent IAS15 reference for " + p.Label
                + ": checkpoint samples incomplete; keeping fixed DOP853 reference");
            return;
        }

        double crossFixed = 0d;
        for (int k = 0; k < p.CheckpointTimes.Length; k++)
        {
            crossFixed = Math.Max(crossFixed, p.Error(fine.Samples[k], p.CheckpointRef[k]));
        }

        double roundoff = 16d * 2.220446049250313e-16
            * Math.Max(p.ScaleLength, 1d) * Math.Sqrt(Math.Max(fine.Steps, 1L));
        double previous = double.IsNaN(p.Floor) ? 0d : p.Floor;
        double floor = Math.Max(previous, Math.Max(Math.Max(diff, crossFixed), roundoff));
        p.CheckpointRef = new double[p.CheckpointTimes.Length][];
        for (int k = 0; k < p.CheckpointTimes.Length; k++)
        {
            p.CheckpointRef[k] = (double[])fine.Samples[k].Clone();
        }

        p.Floor = floor;
        p.Certified = true;
        p.ReferenceFamily = "Gauss-Radau";
        p.ReferenceMethod = "IAS15 rtol 1e-13";
        p.ReferenceIndependent = true;
        p.ReferenceResolution = "ratio_ok";
        p.ReferenceCrossDiffM = crossFixed;
        p.ReferenceSecondMethod = "fixed DOP853, local frame";
        p.ReferenceNote = "independent IAS15 rtol 1e-13 vs 1e-12: max checkpoint difference "
            + diff.ToString("E3", CultureInfo.InvariantCulture)
            + " m; floor " + floor.ToString("E3", CultureInfo.InvariantCulture)
            + " m (includes fixed-DOP853 floor " + previous.ToString("E3", CultureInfo.InvariantCulture) + " m)";
        Console.WriteLine("INFO independent IAS15 reference for " + p.Label + ": " + p.ReferenceNote);
    }

    private static StudyRun RunIas15Reference(StudyProblem p, IStudyIntegrator integ, double rtol)
    {
        try
        {
            StudyConfig cfg = integ.MakeConfig(p, rtol, double.PositiveInfinity);
            var ctx = new StudyContext(p, 20000000L);
            StudyRun run = integ.Run(p, cfg, ctx);
            return run.Aborted ? null : run;
        }
        catch (Exception ex)
        {
            Console.WriteLine("INFO IAS15 reference failed for " + p.Label + " at rtol "
                + rtol.ToString("E1", CultureInfo.InvariantCulture) + ": " + ex.Message);
            return null;
        }
    }

    private static bool NeedsIndependentReference(string label)
    {
        switch (label)
        {
            case "Moon orbit 100 km":
            case "Moon flyby vinf 200 alt 100":
            case "Moon flyby vinf 1000 alt 100":
            case "Moon flyby vinf 10000 alt 100":
            case "Heliocentric 1 AU e 0.9":
            case "Heliocentric 5 AU e 0":
            case "Heliocentric 5 AU e 0.5":
            case "Heliocentric 30 AU e 0":
            case "Heliocentric 30 AU e 0.5":
                return true;
            default:
                return false;
        }
    }

    private static void WriteMoonFlybyMap(
        string dir, List<StudyScenarioResult> results, StudyOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Moon flyby map: v_inf x periapsis altitude");
        sb.AppendLine();
        sb.AppendLine("Cell is the winner at 1 m; `~` = tie, `EDGE` = unresolved upper bound, `—` = no winner. See `Test559_PureGravity.csv` for 0.1/0.01 m.");
        sb.AppendLine();
        WriteFlybyMatrix(sb, results, options, 1d, "1 m");
        sb.AppendLine();
        sb.AppendLine("## 0.01 m");
        sb.AppendLine();
        WriteFlybyMatrix(sb, results, options, 0.01d, "0.01 m");
        File.WriteAllText(Path.Combine(dir, "Test559_MoonFlybyMap.md"), sb.ToString());
    }

    private static void WriteFlybyMatrix(
        StringBuilder sb, List<StudyScenarioResult> results, StudyOptions options,
        double targetMeters, string title)
    {
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        var vinfs = new List<double>();
        var alts = new List<double>();
        foreach (StudyScenarioResult res in results)
        {
            if (res.Problem.Mode != "moon-flyby")
            {
                continue;
            }

            double vinf = TagValue(res.Problem.ModeTags, "vinf=");
            double alt = TagValue(res.Problem.ModeTags, "periapsis-km=");
            if (!double.IsFinite(vinf) || !double.IsFinite(alt))
            {
                continue;
            }

            if (!vinfs.Contains(vinf)) vinfs.Add(vinf);
            if (!alts.Contains(alt)) alts.Add(alt);
            StudyTarget target = null;
            foreach (StudyTarget t in options.Targets(res.Problem))
            {
                if (Math.Abs(t.Value - targetMeters) <= 1e-9d * Math.Max(1d, targetMeters))
                {
                    target = t;
                    break;
                }
            }

            if (target == null)
            {
                continue;
            }

            bool unresolved;
            string winner = StudyReport.Winner(res, target, options, out unresolved);
            string method = FirstMethod(winner);
            if (winner.IndexOf("EDGE", StringComparison.Ordinal) >= 0) method += " EDGE";
            if (winner.IndexOf('~') >= 0) method += " ~";
            if (unresolved) method += " ?";
            cells[Key(vinf, alt)] = method;
        }

        vinfs.Sort();
        alts.Sort();
        sb.Append("| v_inf \\ alt km |");
        foreach (double alt in alts) sb.Append(' ').Append(alt.ToString("G", CultureInfo.InvariantCulture)).Append(" |");
        sb.AppendLine();
        sb.Append("|---|");
        foreach (double alt in alts) sb.Append("---|");
        sb.AppendLine();
        foreach (double vinf in vinfs)
        {
            sb.Append("| ").Append(vinf.ToString("G", CultureInfo.InvariantCulture)).Append(" |");
            foreach (double alt in alts)
            {
                string cell;
                cells.TryGetValue(Key(vinf, alt), out cell);
                sb.Append(' ').Append(cell ?? "—").Append(" |");
            }

            sb.AppendLine();
        }
    }

    private static void WriteRegimeTransitions(
        string dir, List<StudyScenarioResult> results, StudyOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Regime transitions");
        sb.AppendLine();
        sb.AppendLine("Generated from Test559 at 1 m. A change is listed only when the winner method changes; ties/EDGE are marked in the source cells.");
        sb.AppendLine();
        foreach (string mode in new[] { "moon-orbit", "moon-flyby", "heliocentric-orbit", "planet-flyby", "small-body-flyby" })
        {
            sb.AppendLine("## " + mode);
            sb.AppendLine();
            sb.AppendLine("| axis | from | to | winner change |");
            sb.AppendLine("|---|---|---|---|");
            var rows = new List<StudyScenarioResult>();
            foreach (StudyScenarioResult r in results)
            {
                if (r.Problem.Mode == mode) rows.Add(r);
            }

            if (mode == "moon-flyby")
            {
                TransitionsByTag(sb, rows, options, "vinf=", "vinf");
                TransitionsByTag(sb, rows, options, "periapsis-km=", "periapsis-km");
            }
            else if (mode == "heliocentric-orbit")
            {
                TransitionsByTag(sb, rows, options, "e=", "eccentricity");
                TransitionsByTag(sb, rows, options, "a-au=", "a-AU");
            }
            else if (mode == "planet-flyby")
            {
                TransitionsByTag(sb, rows, options, "periapsis-radii=", "periapsis-radii");
                TransitionsByTag(sb, rows, options, "vinf=", "vinf");
            }
            else if (mode == "small-body-flyby")
            {
                TransitionsByTag(sb, rows, options, "vinf=", "vinf");
            }
            else
            {
                TransitionsByTag(sb, rows, options, "distance-radii=", "distance-radii");
            }

            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(dir, "Test559_Transitions.md"), sb.ToString());
    }

    private static void TransitionsByTag(
        StringBuilder sb, List<StudyScenarioResult> rows, StudyOptions options,
        string tagPrefix, string axis)
    {
        var byValue = new SortedDictionary<double, string>(Comparer<double>.Default);
        foreach (StudyScenarioResult r in rows)
        {
            double v = TagValue(r.Problem.ModeTags, tagPrefix);
            if (!double.IsFinite(v)) continue;
            StudyTarget target = null;
            foreach (StudyTarget t in options.Targets(r.Problem))
            {
                if (Math.Abs(t.Value - 1d) < 1e-9d) { target = t; break; }
            }

            if (target == null) continue;
            bool unresolved;
            string winner = StudyReport.Winner(r, target, options, out unresolved);
            byValue[v] = FirstMethod(winner) + (winner.IndexOf('~') >= 0 ? " ~" : "")
                + (winner.IndexOf("EDGE", StringComparison.Ordinal) >= 0 ? " EDGE" : "")
                + (unresolved ? " ?" : "");
        }

        double previousValue = double.NaN;
        string previous = null;
        foreach (KeyValuePair<double, string> pair in byValue)
        {
            if (previous != null && pair.Value != previous)
            {
                sb.Append('|').Append(axis).Append('|')
                    .Append(previousValue.ToString("G", CultureInfo.InvariantCulture)).Append('|')
                    .Append(pair.Key.ToString("G", CultureInfo.InvariantCulture)).Append('|')
                    .Append(previous).Append(" -> ").Append(pair.Value).AppendLine("|");
            }

            previousValue = pair.Key;
            previous = pair.Value;
        }
    }

    private static double TagValue(string[] tags, string prefix)
    {
        if (tags == null) return double.NaN;
        foreach (string tag in tags)
        {
            if (tag.StartsWith(prefix, StringComparison.Ordinal))
            {
                double v;
                if (double.TryParse(tag.Substring(prefix.Length), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out v))
                {
                    return v;
                }
            }
        }

        return double.NaN;
    }

    private static string FirstMethod(string winner)
    {
        if (string.IsNullOrEmpty(winner) || winner == "-") return "—";
        int i = winner.IndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? winner.Substring(0, i) : winner;
    }

    private static string Key(double vinf, double alt)
    {
        return vinf.ToString("R", CultureInfo.InvariantCulture) + "/"
            + alt.ToString("R", CultureInfo.InvariantCulture);
    }
}
