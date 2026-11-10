using System;
using System.Collections.Generic;
using System.Globalization;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

// Test560b scenarios. One physical case can be cloned into several explicit
// configurations (production max step, regime max step, chunk sensitivity);
// the reference/problem are shared, the chunk/max-step are not.
internal static partial class P1bTests
{
    private sealed class DecisionBTarget
    {
        public string Label;
        public double Value;
        public string Kind; // position | velocity
    }

    private sealed class DecisionBScenario
    {
        public string Label;
        public string BaseLabel;
        public string Regime;
        public string Config = "production";
        public StudyShipMatrixCase Case;
        public StudyShipMatrixRefs Refs;
        public StudyProblem Problem;
        public double MaxStep;
        public Func<double, double> ChunkAt;
        public double ChunkMax;
        public string ChunkDescription = "";
        public bool Sensitivity;
        public DecisionBTarget[] Targets = new DecisionBTarget[0];
        public double FloorM = double.NaN;
        public double FloorV = double.NaN;
        public double EventTargetSeconds = double.NaN;
        public string EventKind = "";
        public double EventLevelM;
        public double KeplerWeight = 1d;
        public double KeplerCv;
        public string KeplerSource = "fixed";
        public OrbitingBody DominantBodyOverride;
        public string Note = "";

        public DecisionBScenario CloneForConfig(
            string config, double maxStep, Func<double, double> chunkAt,
            double chunkMax, string chunkDescription, bool sensitivity)
        {
            return new DecisionBScenario
            {
                Label = BaseLabel + " [" + config + "]",
                BaseLabel = BaseLabel,
                Regime = Regime,
                Config = config,
                Case = Case,
                Refs = Refs,
                Problem = Problem,
                MaxStep = maxStep,
                ChunkAt = chunkAt,
                ChunkMax = chunkMax,
                ChunkDescription = chunkDescription,
                Sensitivity = sensitivity,
                Targets = Targets,
                FloorM = FloorM,
                FloorV = FloorV,
                EventTargetSeconds = EventTargetSeconds,
                EventKind = EventKind,
                EventLevelM = EventLevelM,
                KeplerWeight = KeplerWeight,
                KeplerCv = KeplerCv,
                KeplerSource = KeplerSource,
                DominantBodyOverride = DominantBodyOverride,
                Note = Note
            };
        }

        public DecisionBScenario CloneForChunk(string suffix, double chunk, bool sensitivity)
        {
            var clone = CloneForConfig(
                "chunk-sweep " + chunk.ToString("G6", CultureInfo.InvariantCulture),
                600d, t => chunk, chunk,
                "fixed chunk " + chunk.ToString("G6", CultureInfo.InvariantCulture) + " s",
                sensitivity);
            clone.Label = BaseLabel + " [" + suffix + "]";
            clone.Config = "chunk-sweep";
            return clone;
        }
    }

    private sealed class DecisionBSystems
    {
        public StarSystem Surface;
        public StarSystem Leo;
        public StarSystem High;
        public StarSystem Cislunar;
        public StarSystem Helio;

        public static DecisionBSystems Build()
        {
            var s = new DecisionBSystems();
            s.Surface = StudyShipMatrixSystem(false, true, true);
            s.Leo = StudyShipMatrixSystem(true, true, true);
            s.High = StudyShipMatrixSystem(true, false, true);
            s.Cislunar = StudyShipMatrixSystem(false, false, true);
            s.Helio = StudyShipMatrixSystem(false, false, false);
            return s;
        }
    }

    private static List<DecisionBScenario> DecisionBBuildScenarios()
    {
        DecisionBSystems systems = DecisionBSystems.Build();
        var result = new List<DecisionBScenario>();

        DecisionBScenario surfaceAscent = DecisionBSurfaceAscent(systems.Surface);
        DecisionBAdd(result, surfaceAscent);
        if (surfaceAscent != null)
        {
            result.Add(surfaceAscent.CloneForChunk("surface ascent chunk 0.5 s sensitivity", 0.5d, true));
        }

        DecisionBScenario surfaceDescent = DecisionBSurfaceDescent(systems.Surface);
        DecisionBAdd(result, surfaceDescent);
        if (surfaceDescent != null)
        {
            result.Add(surfaceDescent.CloneForChunk("surface descent chunk 0.5 s sensitivity", 0.5d, true));
        }

        // Surface hover and level flight: same 0.05 s production chunk and drag +
        // thrust + variable mass as ascent/descent, but the thrust vector is chosen
        // so the initial acceleration balances gravity (hover) or gravity + drag
        // (level). Thrust follows m(t) so T/m does not drift as propellant burns.
        DecisionBAdd(result, DecisionBSurfaceHover(systems.Surface));
        DecisionBAdd(result, DecisionBSurfaceLevel(systems.Surface, 500d));
        DecisionBAdd(result, DecisionBSurfaceLevel(systems.Surface, 1000d));
        DecisionBAdd(result, DecisionBLeo(systems.Leo, "LEO 200 km, 1 d", 200d, 1d * 86400d, 10d));
        DecisionBAdd(result, DecisionBLeo(systems.Leo, "LEO 300 km, 30 d", 300d, 30d * 86400d, 100d));
        DecisionBAdd(result, DecisionBHighOrbit(systems.High, "High orbit 35 786 km (GEO), 30 d", 35786d, 100d));
        DecisionBAdd(result, DecisionBHighOrbit(systems.High, "High orbit 200 000 km, 30 d", 200000d, 100d));
        DecisionBAdd(result, DecisionBCislunar(systems.Cislunar));

        DecisionBScenario h03 = DecisionBHeliocentric(
            systems.Helio, "Heliocentric 0.3 AU e 0.5, 1 y", 0.3d, 0.5d, 1d, 7200d, 10000d);
        DecisionBAdd(result, h03);
        if (h03 != null)
        {
            result.Add(h03.CloneForConfig(
                "regime86400", 86400d, t => 86400d, 86400d,
                "86 400 s (regime sensitivity, not production)", true));
        }

        DecisionBScenario h5 = DecisionBHeliocentric(
            systems.Helio, "Heliocentric 5 AU e 0.3, 10 y", 5d, 0.3d, 10d, 36000d, 100000d);
        DecisionBAdd(result, h5);
        if (h5 != null)
        {
            // The production auto dominant-body path is the known 199 000 km
            // outlier. Keep it visible for isolation, but do not use it as the
            // method-selection scenario. The forced-Sun clone is the valid
            // production/stand comparison; the game would need the same fix.
            h5.Config = "auto-bug";
            h5.Sensitivity = true;
            h5.Note = "production auto dominant-body path; see HeliocentricIsolation";

            DecisionBScenario forced = h5.CloneForConfig(
                "production", 600d, t => 600d, 600d,
                "600 s (forced Sun; production auto path is buggy)", false);
            forced.Label = h5.BaseLabel + " [forced Sun]";
            forced.DominantBodyOverride = h5.Case.Sys.AllBodies[0];
            forced.Note = "forced Sun; production auto path is the isolation finding";
            result.Add(forced);

            DecisionBScenario forcedRegime = forced.CloneForConfig(
                "regime86400", 86400d, t => 86400d, 86400d,
                "86 400 s (regime sensitivity, forced Sun)", true);
            forcedRegime.Label = forcedRegime.Label + " [forced Sun]";
            result.Add(forcedRegime);
        }

        DecisionBScenario far = DecisionBFar(systems.Helio);
        DecisionBAdd(result, far);
        if (far != null)
        {
            result.Add(far.CloneForConfig(
                "regime86400", 86400d, t => 86400d, 86400d,
                "86 400 s (regime max step, not the current production 600 s)", false));
        }

        return result;
    }

    private static void DecisionBAdd(List<DecisionBScenario> list, DecisionBScenario s)
    {
        if (s != null)
        {
            list.Add(s);
        }
    }

    private static DecisionBScenario DecisionBFinish(
        StudyShipMatrixCase c, string regime, string config, double maxStep,
        Func<double, double> chunkAt, double chunkMax, string chunkDescription,
        DecisionBTarget[] targets, string eventKind, double eventLevelM,
        double eventTargetSeconds, string note, bool sensitivity = false)
    {
        string filter = DecisionBEnv("P1B560B_REGIMES");
        if (!string.IsNullOrWhiteSpace(filter)
            && !DecisionBSelected(c.Label, filter) && !DecisionBSelected(regime, filter))
        {
            return null;
        }

        c.WorldChunkSeconds = chunkMax;
        c.CheckpointTimesOverride = DecisionBCheckpointTimes(c);
        StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(c);
        StudyProblem problem = StudyBridgeShipProblem(c, refs);

        double floorM = refs.ReferenceFloor;
        double floorV = refs.LocalVelocityFloor;
        if (!double.IsFinite(floorV) && double.IsFinite(floorM))
        {
            double h = c.CertifiedReferenceStepSeconds > 0d
                ? c.CertifiedReferenceStepSeconds
                : Math.Max(0.1d, c.Seconds / 1000d);
            floorV = Math.Max(1e-9d, floorM / h);
        }

        var s = new DecisionBScenario
        {
            Label = c.Label,
            BaseLabel = c.Label,
            Regime = regime,
            Config = config,
            Case = c,
            Refs = refs,
            Problem = problem,
            MaxStep = maxStep,
            ChunkAt = chunkAt,
            ChunkMax = chunkMax,
            ChunkDescription = chunkDescription,
            Sensitivity = sensitivity,
            Targets = targets,
            FloorM = floorM,
            FloorV = floorV,
            EventKind = eventKind ?? "",
            EventLevelM = eventLevelM,
            EventTargetSeconds = eventTargetSeconds,
            Note = note ?? ""
        };

        Console.WriteLine("  reference " + c.Label + ": floor="
            + (double.IsFinite(floorM) ? floorM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
            + " m, velocity floor="
            + (double.IsFinite(floorV) ? floorV.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
            + " m/s, " + (refs.LocalKind ?? "no note"));

        return s;
    }

    private static double[] DecisionBCheckpointTimes(StudyShipMatrixCase c)
    {
        double t0 = StudyShipT0;
        double end = t0 + c.Seconds;
        int count = 16;
        var times = new List<double>();
        for (int i = 1; i <= count; i++)
        {
            times.Add(t0 + (c.Seconds * i / count));
        }

        if (c.Thrust)
        {
            double start = c.ThrustStart;
            double finish = start + c.ThrustSeconds;
            foreach (double edge in new[] { start, start + (0.5d * c.ThrustSeconds), finish })
            {
                if (edge > t0 + 1e-6d && edge < end - 1e-6d)
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

        if (unique.Count == 0 || Math.Abs(unique[unique.Count - 1] - end) > 1e-6d)
        {
            unique.Add(end);
        }

        return unique.ToArray();
    }

    private static double DecisionBLeoChunk(StudyShipMatrixCase c, double t)
    {
        if (c.Thrust)
        {
            double start = c.ThrustStart;
            double end = start + c.ThrustSeconds;
            if (t >= start - 1e-9d && t < end - 1e-9d)
            {
                return 0.05d;
            }
        }

        return 600d;
    }

    private static DecisionBScenario DecisionBSurfaceAscent(StarSystem sys)
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

        var baseScenario = DecisionBFinish(
            c, "surface", "production", 600d, t => 0.05d, 0.05d,
            "0.05 s flight (production)", new[]
            {
                new DecisionBTarget { Label = "0.1 m", Value = 0.1d, Kind = "position" },
                new DecisionBTarget { Label = "0.1 m/s", Value = 0.1d, Kind = "velocity" }
            }, "", 0d, double.NaN, "thrust + drag + mass flow", false);
        if (baseScenario != null)
        {
            // Separate sensitivity row: not production, used only by the chunk sweep.
            baseScenario.Note = (baseScenario.Note + "; production chunk 0.05 s").Trim();
        }

        return baseScenario;
    }

    private static DecisionBScenario DecisionBSurfaceDescent(StarSystem sys)
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

        var baseScenario = DecisionBFinish(
            c, "surface", "production", 600d, t => 0.05d, 0.05d,
            "0.05 s flight (production)", new[]
            {
                new DecisionBTarget { Label = "0.1 m", Value = 0.1d, Kind = "position" },
                new DecisionBTarget { Label = "0.1 m/s", Value = 0.1d, Kind = "velocity" }
            }, "touchdown", 0d, 0.05d,
            "drag + gravity; touchdown event; post-contact continuation is not a contact model", false);
        if (baseScenario != null)
        {
            baseScenario.Note = (baseScenario.Note + "; production chunk 0.05 s").Trim();
        }

        return baseScenario;
    }

    private static double DecisionBSurfaceDensity(double altitudeM)
    {
        return 1.225d * Math.Exp(-altitudeM / 8500d);
    }

    private static DecisionBScenario DecisionBSurfaceHover(StarSystem sys)
    {
        OrbitingBody earth = sys.AllBodies[1];
        const double altitude = 500d;
        const double seconds = 60d;
        double r = earth.Radius + altitude;
        double mu = earth.ResolveStandardGravitationalParameter();
        double g = mu / (r * r);
        double atmosphereSpeed = earth.SpinAngularSpeed * r;
        var c = new StudyShipMatrixCase
        {
            Label = "Surface hover 0.5 km, 0 m/s relative",
            Sys = sys,
            Central = 1,
            P0 = new Vector3d(r, 0d, 0d),
            // Zero velocity relative to the rotating atmosphere: no drag, only
            // gravity and the radial thrust that balances it.
            V0 = new Vector3d(0d, atmosphereSpeed, 0d),
            Mass0 = 1000d,
            Seconds = seconds,
            Thrust = true,
            ThrustDirection = new Vector3d(1d, 0d, 0d),
            ThrustStart = StudyShipT0,
            ThrustSeconds = seconds,
            ThrustNewtons = 1000d * g,
            ThrustIsp = 300d,
            ThrustRampSeconds = 0d,
            ThrustScalesWithMass = true,
            Tau = seconds / 16d,
            StepScale = 0.25d,
            ScaleLength = earth.Radius,
            Drag = new DragSource(earth) { ReferenceAreaM2 = 1d, DragCoefficient = 1d },
            CertifiedReferenceStepSeconds = 0.4d,
            AccuracyTarget = 0.1d,
            AllowUnratioedResolution = true,
            HasBreakpoints = true,
            Mode = "surface-hover",
            ModeTags = new[] { "earth", "surface", "atmosphere", "drag", "thrust", "variable-mass", "hover", "low-speed" },
            AltitudeKm = altitude / 1000d,
            SpeedMps = 0d,
            Eccentricity = double.NaN
        };

        return DecisionBFinish(
            c, "surface", "production", 600d, t => 0.05d, 0.05d,
            "0.05 s flight (production)", new[]
            {
                new DecisionBTarget { Label = "0.1 m", Value = 0.1d, Kind = "position" },
                new DecisionBTarget { Label = "0.1 m/s", Value = 0.1d, Kind = "velocity" }
            }, "", 0d, double.NaN,
            "hover: radial thrust follows m(t); zero relative wind; production chunk 0.05 s", false);
    }

    private static DecisionBScenario DecisionBSurfaceLevel(StarSystem sys, double altitudeM)
    {
        OrbitingBody earth = sys.AllBodies[1];
        const double seconds = 60d;
        const double speed = 100d;
        double r = earth.Radius + altitudeM;
        double mu = earth.ResolveStandardGravitationalParameter();
        double g = mu / (r * r);
        double atmosphereSpeed = earth.SpinAngularSpeed * r;
        double density = DecisionBSurfaceDensity(altitudeM);
        double dragN = 0.5d * density * 1d * speed * speed;
        double verticalN = 1000d * g;
        // Thrust scales with m(t), so the vertical component stays m(t)*g. The
        // horizontal component is sized for the mean mass over the 60 s window so
        // the horizontal speed does not drift with propellant burn.
        double guessN = Math.Sqrt((dragN * dragN) + (verticalN * verticalN));
        double massFlow = guessN / (300d * 9.80665d);
        double meanFraction = Math.Max(0.5d, 1d - (0.5d * massFlow * seconds / 1000d));
        double horizontalN = dragN / meanFraction;
        double thrustN = Math.Sqrt((horizontalN * horizontalN) + (verticalN * verticalN));
        var c = new StudyShipMatrixCase
        {
            Label = "Surface level flight " + (altitudeM / 1000d).ToString("G6", CultureInfo.InvariantCulture)
                + " km, 100 m/s horizontal",
            Sys = sys,
            Central = 1,
            P0 = new Vector3d(r, 0d, 0d),
            V0 = new Vector3d(0d, atmosphereSpeed + speed, 0d),
            Mass0 = 1000d,
            Seconds = seconds,
            Thrust = true,
            // +x is radial (up), +y is horizontal along the atmosphere-fixed velocity.
            ThrustDirection = new Vector3d(verticalN / thrustN, horizontalN / thrustN, 0d),
            ThrustStart = StudyShipT0,
            ThrustSeconds = seconds,
            ThrustNewtons = thrustN,
            ThrustIsp = 300d,
            ThrustRampSeconds = 0d,
            ThrustScalesWithMass = true,
            Tau = seconds / 16d,
            StepScale = 0.25d,
            ScaleLength = earth.Radius,
            Drag = new DragSource(earth) { ReferenceAreaM2 = 1d, DragCoefficient = 1d },
            CertifiedReferenceStepSeconds = 0.4d,
            AccuracyTarget = 0.1d,
            AllowUnratioedResolution = true,
            HasBreakpoints = true,
            Mode = "surface-level",
            ModeTags = new[] { "earth", "surface", "atmosphere", "drag", "thrust", "variable-mass", "level-flight", "low-speed" },
            AltitudeKm = altitudeM / 1000d,
            SpeedMps = speed,
            Eccentricity = double.NaN
        };

        return DecisionBFinish(
            c, "surface", "production", 600d, t => 0.05d, 0.05d,
            "0.05 s flight (production)", new[]
            {
                new DecisionBTarget { Label = "0.1 m", Value = 0.1d, Kind = "position" },
                new DecisionBTarget { Label = "0.1 m/s", Value = 0.1d, Kind = "velocity" }
            }, "", 0d, double.NaN,
            "initial horizontal flight; no lift/altitude-hold model; drag + thrust + variable mass; production chunk 0.05 s", false);
    }

    private static DecisionBScenario DecisionBLeo(
        StarSystem sys, string label, double altitudeKm, double seconds, double targetM)
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

        return DecisionBFinish(
            c, "leo", "production", 600d, t => DecisionBLeoChunk(c, t), 600d,
            "600 s coast / 0.05 s burn (production)", new[]
            {
                new DecisionBTarget
                {
                    Label = targetM.ToString("G", CultureInfo.InvariantCulture) + " m",
                    Value = targetM, Kind = "position"
                }
            }, "", 0d, double.NaN, "J2 + drag + 60 s burn", false);
    }

    private static DecisionBScenario DecisionBHighOrbit(
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

        return DecisionBFinish(
            c, "high-orbit", "production", 600d, t => 600d, 600d,
            "600 s (production)", new[]
            {
                new DecisionBTarget
                {
                    Label = targetM.ToString("G", CultureInfo.InvariantCulture) + " m",
                    Value = targetM, Kind = "position"
                }
            }, "", 0d, double.NaN, "gravity + J2", false);
    }

    private static DecisionBScenario DecisionBCislunar(StarSystem sys)
    {
        OrbitingBody moon = sys.AllBodies[2];
        double muMoon = moon.ResolveStandardGravitationalParameter();
        double rp = moon.Radius + 100000d;
        double vinf = 1000d;
        double vp = Math.Sqrt((vinf * vinf) + (2d * muMoon / rp));
        double[] perilune = { rp, 0d, 0d, 0d, vp, 0d };
        double rStart = 100000000d;
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

        return DecisionBFinish(
            c, "cislunar", "production", 600d, t => 600d, 600d,
            "600 s (production)", new[]
            {
                new DecisionBTarget { Label = "100 m", Value = 100d, Kind = "position" }
            }, "soi-out", 66000000d, 60d,
            "Moon SOI entry/perilune/exit", false);
    }

    private static DecisionBScenario DecisionBHeliocentric(
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

        return DecisionBFinish(
            c, "heliocentric", "production", 600d, t => 600d, 600d,
            "600 s (production)", new[]
            {
                new DecisionBTarget
                {
                    Label = (targetM / 1000d).ToString("G", CultureInfo.InvariantCulture) + " km",
                    Value = targetM, Kind = "position"
                }
            }, "", 0d, double.NaN, "pure gravity", false);
    }

    private static DecisionBScenario DecisionBFar(StarSystem sys)
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

        return DecisionBFinish(
            c, "far-flight", "production", 600d, t => 86400d, 86400d,
            "86 400 s outer chunk / 600 s production max step", new[]
            {
                new DecisionBTarget { Label = "100 km", Value = 100000d, Kind = "position" }
            }, "", 0d, double.NaN, "pure gravity; production max step 600 s", false);
    }

    private static string DecisionBEnv(string name)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static bool DecisionBSelected(string value, string filter)
    {
        foreach (string part in filter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string frag = part.Trim();
            if (frag.Length > 0 && value.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
