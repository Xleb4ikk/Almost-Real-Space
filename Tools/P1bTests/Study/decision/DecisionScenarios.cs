using System;
using System.Collections.Generic;
using System.Globalization;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

// Test560 scenario definitions. Every scenario is a StudyShipMatrixCase, so the
// certified fixed-step DOP853 reference (CertifiedReference.cs) and the production
// force model are the same objects the existing stand already trusts.
internal static partial class P1bTests
{
    private sealed class DecisionTarget
    {
        public string Label;
        public double Value;
        public string Kind;   // position | velocity
    }

    private sealed class DecisionScenario
    {
        public string Label;
        public string Regime;
        public StudyShipMatrixCase Case;
        public StudyShipMatrixRefs Refs;
        public StudyProblem Problem;
        public double ChunkSeconds;
        public int ChunkCount;
        public DecisionTarget[] Targets;
        public double FloorM;
        public double FloorV;
        public double EventTargetSeconds = double.NaN;
        public string EventKind = "";
        public double EventLevelM;
        public string Note = "";
        public double KeplerWeight = 1d;
        public double KeplerCv;
        public string KeplerSource = "fixed";
    }

    /// <summary>One baked/analytic system per force model, shared by all scenarios that use it.</summary>
    private sealed class DecisionSystems
    {
        public StarSystem Surface;
        public StarSystem Leo;
        public StarSystem High;
        public StarSystem Cislunar;
        public StarSystem Helio;

        public static DecisionSystems Build()
        {
            var s = new DecisionSystems();
            // The bake horizon is 0.25/1.2 years in StudyShipMatrixSystem; every
            // Earth-centred scenario here is at most 30 days, so those are safe.
            // Heliocentric/far scenarios are 1/10 years and use the analytic rails.
            s.Surface = StudyShipMatrixSystem(false, true, true);
            s.Leo = StudyShipMatrixSystem(true, true, true);
            s.High = StudyShipMatrixSystem(true, false, true);
            s.Cislunar = StudyShipMatrixSystem(false, false, true);
            s.Helio = StudyShipMatrixSystem(false, false, false);
            return s;
        }
    }

    private static List<DecisionScenario> DecisionBuildScenarios()
    {
        DecisionSystems systems = DecisionSystems.Build();
        var result = new List<DecisionScenario>();

        // ---- Surface: 0-1 km, 0-300 m/s, gravity + drag + thrust + variable mass ----
        DecisionScenariosAdd(result, DecisionSurfaceAscent(systems.Surface));
        DecisionScenariosAdd(result, DecisionSurfaceDescent(systems.Surface));

        // ---- LEO: 200/300 km, J2 + drag + a 60 s burn ----
        DecisionScenariosAdd(result, DecisionLeo(systems.Leo, "LEO 200 km, 1 d", 200d, 1d * 86400d, 60d, 10d));
        DecisionScenariosAdd(result, DecisionLeo(systems.Leo, "LEO 300 km, 30 d", 300d, 30d * 86400d, 600d, 100d));

        // ---- High orbit: 35 786 km and 200 000 km, 30 d, gravity + J2 ----
        DecisionScenariosAdd(result, DecisionHighOrbit(systems.High, "High orbit 35 786 km (GEO), 30 d", 35786d, 100d));
        DecisionScenariosAdd(result, DecisionHighOrbit(systems.High, "High orbit 200 000 km, 30 d", 200000d, 100d));

        // ---- Cislunar: Moon SOI entry, perilune, exit, 10 d ----
        DecisionScenariosAdd(result, DecisionCislunar(systems.Cislunar));

        // ---- Heliocentric: 0.3 AU 1 y and 5 AU 10 y, pure gravity ----
        DecisionScenariosAdd(result, DecisionHeliocentric(systems.Helio, "Heliocentric 0.3 AU e 0.5, 1 y", 0.3d, 0.5d, 1d, 7200d, 10000d));
        DecisionScenariosAdd(result, DecisionHeliocentric(systems.Helio, "Heliocentric 5 AU e 0.3, 10 y", 5d, 0.3d, 10d, 36000d, 100000d));

        // ---- Far flight: 30 AU, 10 y, pure gravity ----
        DecisionScenariosAdd(result, DecisionFar(systems.Helio));

        return result;
    }

    private static void DecisionScenariosAdd(List<DecisionScenario> list, DecisionScenario s)
    {
        if (s != null)
        {
            list.Add(s);
        }
    }

    private static DecisionScenario DecisionFinish(
        StudyShipMatrixCase c, string regime, double chunkSeconds,
        DecisionTarget[] targets, string eventKind, double eventLevelM,
        double eventTargetSeconds, string note)
    {
        string filter = DecisionEnv("P1B560_REGIMES");
        if (!string.IsNullOrWhiteSpace(filter)
            && !DecisionSelected(c.Label, filter) && !DecisionSelected(regime, filter))
        {
            return null;
        }

        c.WorldChunkSeconds = chunkSeconds;
        int chunks = (int)Math.Round(c.Seconds / chunkSeconds, MidpointRounding.AwayFromZero);
        if (chunks < 1)
        {
            chunks = 1;
        }

        c.CheckpointTimesOverride = DecisionChunkAlignedTimes(c, chunkSeconds, chunks);
        StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(c);
        StudyProblem problem = StudyBridgeShipProblem(c, refs);

        double floorM = refs.ReferenceFloor;
        double floorV = refs.LocalVelocityFloor;
        if (!double.IsFinite(floorV) && double.IsFinite(floorM))
        {
            // The unratioed-resolution branch certifies position only and leaves
            // VelocityError at zero. A conservative velocity bound is the certified
            // position floor divided by the reference step (m / s).
            double h = c.CertifiedReferenceStepSeconds > 0d
                ? c.CertifiedReferenceStepSeconds
                : Math.Max(0.1d, c.Seconds / 1000d);
            floorV = Math.Max(1e-9d, floorM / h);
        }
        Console.WriteLine("  reference " + c.Label + ": floor="
            + (double.IsFinite(floorM) ? floorM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
            + " m, velocity floor="
            + (double.IsFinite(floorV) ? floorV.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
            + " m/s, " + (refs.LocalKind ?? "no note"));

        if (string.Equals(DecisionEnv("P1B560_DIAG"), "1", StringComparison.Ordinal))
        {
            for (int i = 0; i < refs.CheckpointTimes.Length; i++)
            {
                Console.WriteLine("    ref ck " + i.ToString(CultureInfo.InvariantCulture)
                    + " t=" + refs.CheckpointTimes[i].ToString("G6", CultureInfo.InvariantCulture)
                    + " |r|=" + refs.CheckpointP[i].Magnitude.ToString("E3", CultureInfo.InvariantCulture)
                    + " |v|=" + refs.CheckpointV[i].Magnitude.ToString("E3", CultureInfo.InvariantCulture));
            }
        }

        return new DecisionScenario
        {
            Label = c.Label,
            Regime = regime,
            Case = c,
            Refs = refs,
            Problem = problem,
            ChunkSeconds = chunkSeconds,
            ChunkCount = chunks,
            Targets = targets,
            FloorM = floorM,
            FloorV = floorV,
            EventKind = eventKind ?? "",
            EventLevelM = eventLevelM,
            EventTargetSeconds = eventTargetSeconds,
            Note = note ?? ""
        };
    }

    /// <summary>
    /// Checkpoints that are exact multiples of the chunk. This is the whole point
    /// of the scenario: candidate and reference meet at a chunk boundary, so no
    /// artificial extra stop is inserted just to sample the trajectory.
    /// </summary>
    private static double[] DecisionChunkAlignedTimes(
        StudyShipMatrixCase c, double chunkSeconds, int chunks)
    {
        int count = 16;
        while (count > 1 && (chunks % count) != 0)
        {
            count--;
        }

        int stride = Math.Max(1, chunks / Math.Max(1, count));
        var times = new List<double>();
        double t0 = StudyShipT0;
        for (int i = 1; i <= count; i++)
        {
            times.Add(t0 + (stride * chunkSeconds * i));
        }

        // The burn edges are force breakpoints. Include them when they are also
        // chunk boundaries, so the max-error metric sees the burn itself, not only
        // the coast after it.
        if (c.Thrust)
        {
            foreach (double edge in StudyShipRefBreakpoints(c))
            {
                if (edge <= t0 + 1e-9d || edge >= t0 + c.Seconds - 1e-9d)
                {
                    continue;
                }

                double units = (edge - t0) / chunkSeconds;
                if (Math.Abs(units - Math.Round(units)) <= 1e-9d)
                {
                    times.Add(edge);
                }
            }
        }

        times.Sort();
        var unique = new List<double>();
        foreach (double t in times)
        {
            if (unique.Count == 0 || Math.Abs(t - unique[unique.Count - 1]) > 1e-6d)
            {
                unique.Add(t);
            }
        }

        if (unique.Count == 0 || Math.Abs(unique[unique.Count - 1] - (t0 + c.Seconds)) > 1e-6d)
        {
            unique.Add(t0 + c.Seconds);
        }

        return unique.ToArray();
    }

    private static DecisionScenario DecisionSurfaceAscent(StarSystem sys)
    {
        OrbitingBody earth = sys.AllBodies[1];
        const double altitude = 500d;
        const double seconds = 60d;
        double atmosphereSpeed = earth.SpinAngularSpeed * (earth.Radius + altitude);
        var c = new StudyShipMatrixCase
        {
            Label = "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22",
            Sys = sys,
            Central = 1,
            P0 = new Vector3d(earth.Radius + altitude, 0d, 0d),
            V0 = new Vector3d(30d, atmosphereSpeed + 100d, 0d),
            Mass0 = 1000d,
            Seconds = seconds,
            Thrust = true,
            ThrustDirection = new Vector3d(1d, 0d, 0d),
            ThrustStart = StudyShipT0 + 1d,
            ThrustSeconds = seconds - 2d,
            ThrustNewtons = 12000d,
            ThrustIsp = 300d,
            ThrustRampSeconds = 2d,
            Tau = seconds / 16d,
            StepScale = 0.25d,
            ScaleLength = earth.Radius,
            Drag = new DragSource(earth) { ReferenceAreaM2 = 1d, DragCoefficient = 1d },
            CertifiedReferenceStepSeconds = 0.4d,
            AccuracyTarget = 0.1d,
            AllowUnratioedResolution = true,
            HasBreakpoints = true,
            Mode = "surface-ascent",
            ModeTags = new[] { "earth", "surface", "atmosphere", "drag", "thrust", "variable-mass", "low-speed" },
            AltitudeKm = altitude / 1000d,
            SpeedMps = 104.4d,
            Eccentricity = double.NaN
        };

        return DecisionFinish(c, "surface", 0.5d, new[]
        {
            new DecisionTarget { Label = "0.1 m", Value = 0.1d, Kind = "position" },
            new DecisionTarget { Label = "0.1 m/s", Value = 0.1d, Kind = "velocity" }
        }, "", 0d, double.NaN, "thrust + drag + mass flow, chunk 0.5 s");
    }

    private static DecisionScenario DecisionSurfaceDescent(StarSystem sys)
    {
        OrbitingBody earth = sys.AllBodies[1];
        const double altitude = 2000d;
        const double seconds = 30d;
        double atmosphereSpeed = earth.SpinAngularSpeed * (earth.Radius + altitude);
        var c = new StudyShipMatrixCase
        {
            Label = "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal",
            Sys = sys,
            Central = 1,
            P0 = new Vector3d(earth.Radius + altitude, 0d, 0d),
            V0 = new Vector3d(-50d, atmosphereSpeed + 100d, 0d),
            Mass0 = 1000d,
            Seconds = seconds,
            Drag = new DragSource(earth) { ReferenceAreaM2 = 1d, DragCoefficient = 1d },
            Tau = seconds / 16d,
            StepScale = 0.25d,
            ScaleLength = earth.Radius,
            CertifiedReferenceStepSeconds = 0.2d,
            AccuracyTarget = 0.1d,
            AllowUnratioedResolution = true,
            HasBreakpoints = false,
            Mode = "surface-descent",
            ModeTags = new[] { "earth", "surface", "atmosphere", "drag", "touchdown", "low-speed" },
            AltitudeKm = altitude / 1000d,
            SpeedMps = 111.8d,
            Eccentricity = double.NaN
        };

        return DecisionFinish(c, "surface", 0.05d, new[]
        {
            new DecisionTarget { Label = "0.1 m", Value = 0.1d, Kind = "position" },
            new DecisionTarget { Label = "0.1 m/s", Value = 0.1d, Kind = "velocity" }
        }, "touchdown", 0d, 0.05d, "drag + gravity; touchdown event; chunk 0.05 s");
    }

    private static DecisionScenario DecisionLeo(
        StarSystem sys, string label, double altitudeKm, double seconds, double chunkSeconds, double targetM)
    {
        OrbitingBody earth = sys.AllBodies[1];
        double r = earth.Radius + (altitudeKm * 1000d);
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            label, sys, 1, r, r, 0d, 0d, 0d, seconds, true, false, 0.25d);
        c.Thrust = true;
        c.ThrustStart = StudyShipT0 + 3600d;
        c.ThrustSeconds = 60d;
        c.ThrustNewtons = 2000d;
        c.ThrustIsp = 300d;
        c.ThrustRampSeconds = 10d;
        // A fixed inertial direction is only meaningful if it is prograde at the
        // burn, not at t0: the orbit has rotated by n*(start-t0) by then.
        double n = Math.Sqrt(earth.ResolveStandardGravitationalParameter() / (r * r * r));
        double theta = n * (c.ThrustStart - StudyShipT0);
        Vector3d h = Vector3d.Cross(c.P0, c.V0).Normalized;
        c.ThrustDirection = (c.V0.Normalized * Math.Cos(theta)
            + Vector3d.Cross(h, c.V0.Normalized) * Math.Sin(theta)).Normalized;
        c.HasBreakpoints = true;
        c.CertifiedReferenceStepSeconds = altitudeKm <= 220d ? 120d : 300d;
        c.AccuracyTarget = targetM;
        c.AllowUnratioedResolution = true;
        c.Mode = "leo";
        c.ModeTags = new[] { "earth", "leo", "j2", "drag", "thrust", "variable-mass" };
        c.AltitudeKm = altitudeKm;
        c.SpeedMps = Math.Sqrt(earth.ResolveStandardGravitationalParameter() / r);
        c.Eccentricity = 0d;

        if (string.Equals(DecisionEnv("P1B560_DIAG"), "1", StringComparison.Ordinal))
        {
            var acc = new StudyShipMatrixAcc(c);
            SpacecraftIntegrationState d = acc.Evaluate(
                new SpacecraftIntegrationState { Position = c.P0, Velocity = c.V0, Mass = c.Mass0 },
                StudyShipT0);
            var burnState = new SpacecraftIntegrationState { Position = c.P0, Velocity = c.V0, Mass = c.Mass0 };
            SpacecraftIntegrationState db = acc.Evaluate(burnState, c.ThrustStart + 1d);
            Console.WriteLine("    diag LEO " + label + ": r0=" + c.P0.Magnitude.ToString("E3", CultureInfo.InvariantCulture)
                + " alt=" + (c.P0.Magnitude - earth.Radius).ToString("E3", CultureInfo.InvariantCulture)
                + " atm top=" + (earth.Atmosphere != null ? earth.Atmosphere.TopAltitudeMeters.ToString("E3", CultureInfo.InvariantCulture) : "none")
                + " scale=" + (earth.Atmosphere != null ? earth.Atmosphere.ScaleHeightMeters.ToString("E3", CultureInfo.InvariantCulture) : "none")
                + " dragArea=" + (c.Drag != null ? c.Drag.ReferenceAreaM2.ToString("E3", CultureInfo.InvariantCulture) : "none")
                + " a=" + d.Velocity.Magnitude.ToString("E3", CultureInfo.InvariantCulture)
                + " a_burn=(" + db.Velocity.X.ToString("E3", CultureInfo.InvariantCulture)
                + "," + db.Velocity.Y.ToString("E3", CultureInfo.InvariantCulture)
                + "," + db.Velocity.Z.ToString("E3", CultureInfo.InvariantCulture) + ")");
        }

        return DecisionFinish(c, "leo", chunkSeconds, new[]
        {
            new DecisionTarget { Label = targetM.ToString("G", CultureInfo.InvariantCulture) + " m", Value = targetM, Kind = "position" }
        }, "", 0d, double.NaN, "J2 + drag + 600 s burn, chunk " + chunkSeconds.ToString("G", CultureInfo.InvariantCulture) + " s");
    }

    private static DecisionScenario DecisionHighOrbit(
        StarSystem sys, string label, double altitudeKm, double targetM)
    {
        OrbitingBody earth = sys.AllBodies[1];
        double r = earth.Radius + (altitudeKm * 1000d);
        double seconds = 30d * 86400d;
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            label, sys, 1, r, r, 0d, 0d, 0d, seconds, true, false, 0.25d);
        c.CertifiedReferenceStepSeconds = 600d;
        c.AccuracyTarget = targetM;
        c.AllowUnratioedResolution = true;
        c.Mode = "high-orbit";
        c.ModeTags = new[] { "earth", "high-orbit", "j2", "third-body" };
        c.AltitudeKm = altitudeKm;
        c.SpeedMps = Math.Sqrt(earth.ResolveStandardGravitationalParameter() / r);
        c.Eccentricity = 0d;

        return DecisionFinish(c, "high-orbit", 600d, new[]
        {
            new DecisionTarget { Label = targetM.ToString("G", CultureInfo.InvariantCulture) + " m", Value = targetM, Kind = "position" }
        }, "", 0d, double.NaN, "gravity + J2, chunk 600 s");
    }

    private static DecisionScenario DecisionCislunar(StarSystem sys)
    {
        OrbitingBody moon = sys.AllBodies[2];
        double muMoon = moon.ResolveStandardGravitationalParameter();
        double rp = moon.Radius + 100000d;
        double vinf = 1000d;
        double vp = Math.Sqrt((vinf * vinf) + (2d * muMoon / rp));
        double[] perilune = { rp, 0d, 0d, 0d, vp, 0d };
        double rStart = 100000000d; // 100 000 km, outside the Moon's SOI
        double lo = 0d;
        double hi = 2d * 86400d;
        for (int i = 0; i < 80; i++)
        {
            double mid = 0.5d * (lo + hi);
            var trial = (double[])perilune.Clone();
            KeplerUniversal.Propagate(muMoon, trial, 0, 3, -mid);
            double r = Math.Sqrt((trial[0] * trial[0]) + (trial[1] * trial[1]) + (trial[2] * trial[2]));
            if (r < rStart)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var start = (double[])perilune.Clone();
        KeplerUniversal.Propagate(muMoon, start, 0, 3, -0.5d * (lo + hi));

        double tau = 2d * Math.PI * rp / vp;
        var c = new StudyShipMatrixCase
        {
            Label = "Cislunar Moon flyby, SOI entry-exit, 10 d",
            Sys = sys,
            Central = 2,
            P0 = new Vector3d(start[0], start[1], start[2]),
            V0 = new Vector3d(start[3], start[4], start[5]),
            Mass0 = 1000d,
            Seconds = 10d * 86400d,
            Perturbed = true,
            AllowTaylor = false,
            Tau = tau,
            StepScale = 0.25d,
            ScaleLength = rp,
            CertifiedReferenceStepSeconds = 150d,
            AccuracyTarget = 100d,
            AllowUnratioedResolution = true,
            Mode = "cislunar",
            ModeTags = new[] { "earth", "moon", "cislunar", "soi", "flyby", "third-body" },
            AltitudeKm = 100d,
            SpeedMps = vp,
            Eccentricity = double.NaN
        };

        return DecisionFinish(c, "cislunar", 600d, new[]
        {
            new DecisionTarget { Label = "100 m", Value = 100d, Kind = "position" }
        }, "soi-out", 66000000d, 60d, "Moon SOI entry/perilune/exit, chunk 600 s");
    }

    private static DecisionScenario DecisionHeliocentric(
        StarSystem sys, string label, double au, double e, double years, double referenceStep, double targetM)
    {
        double mu = sys.AllBodies[0].ResolveStandardGravitationalParameter();
        double a = au * StudyAU;
        double rp = a * (1d - e);
        double ra = a * (1d + e);
        double seconds = years * 365d * 86400d;
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            label, sys, 0, rp, ra, 0d, 0d, 0d, seconds, true, false, 0.25d);
        c.CertifiedReferenceStepSeconds = referenceStep;
        c.AccuracyTarget = targetM;
        c.AllowUnratioedResolution = true;
        c.Mode = "heliocentric";
        c.ModeTags = new[] { "sun", "heliocentric", "pure-gravity", "planets" };
        c.AltitudeKm = double.NaN;
        c.SpeedMps = Math.Sqrt(mu * ((2d / rp) - (1d / a)));
        c.Eccentricity = e;

        return DecisionFinish(c, "heliocentric", 600d, new[]
        {
            new DecisionTarget { Label = (targetM / 1000d).ToString("G", CultureInfo.InvariantCulture) + " km", Value = targetM, Kind = "position" }
        }, "", 0d, double.NaN, "pure gravity, chunk 600 s");
    }

    private static DecisionScenario DecisionFar(StarSystem sys)
    {
        double mu = sys.AllBodies[0].ResolveStandardGravitationalParameter();
        const double au = 30d;
        const double e = 0.5d;
        double a = au * StudyAU;
        double rp = a * (1d - e);
        double ra = a * (1d + e);
        double seconds = 10d * 365d * 86400d;
        StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
            "Far flight 30 AU e 0.5, 10 y", sys, 0, rp, ra, 0d, 0d, 0d, seconds, true, false, 0.25d);
        c.CertifiedReferenceStepSeconds = 86400d;
        c.AccuracyTarget = 100000d;
        c.AllowUnratioedResolution = true;
        c.Mode = "far-flight";
        c.ModeTags = new[] { "sun", "far", "pure-gravity", "weak-acceleration" };
        c.AltitudeKm = double.NaN;
        c.SpeedMps = Math.Sqrt(mu * ((2d / rp) - (1d / a)));
        c.Eccentricity = e;

        return DecisionFinish(c, "far-flight", 86400d, new[]
        {
            new DecisionTarget { Label = "100 km", Value = 100000d, Kind = "position" }
        }, "", 0d, double.NaN, "pure gravity, chunk 86 400 s");
    }
}
