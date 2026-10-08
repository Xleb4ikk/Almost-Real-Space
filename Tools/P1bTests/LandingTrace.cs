using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

// Трейсы посадки (шаг 0.6 ТЗ v2). Каждый сценарий прогоняется дважды,
// результаты сравниваются побайтно. Режим --trace-legacy идёт через слепок
// логики SimulationRunner до выноса (шаг 0.4), режим --trace — через вынесенный
// шаг. Совпадение двух наборов — доказательство, что вынос не изменил поведение.
internal static partial class P1bTests
{
    private const double TraceStepSeconds = 0.5d;
    private const double TraceShipMassKg = 5000d;
    private const double TraceThrustN = 100000d;
    private const double TraceIspSeconds = 320d;
    private const double TraceDryMassKg = 1000d;
    private const double TraceSpawnEpsilonMeters = 1d;
    private const double TraceDebrisLifetimeSeconds = 600d;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed class TraceRig
    {
        public Ship Ship;
        public Func<double> GetTime;
        public Func<VesselRegime> GetRegime;
        public Action<float, bool> StepFlying;
        public Action<float, bool> StepLanded;
        public Action<Ship> SetShip;
        public Action SetLanded;
        public Action<Action<EventOccurrence>> SetOccurrenceHook;
        public Func<int> DebrisCount;
    }

    private readonly struct TraceScenario
    {
        public readonly string Name;
        public readonly double SpawnAltitudeMeters;
        public readonly double RadialSpeedMps;
        public readonly double EastSpeedMps;
        public readonly bool StartLanded;
        public readonly double MaxSeconds;

        public TraceScenario(string name, double spawnAltitudeMeters, double radialSpeedMps, double eastSpeedMps, bool startLanded, double maxSeconds)
        {
            Name = name;
            SpawnAltitudeMeters = spawnAltitudeMeters;
            RadialSpeedMps = radialSpeedMps;
            EastSpeedMps = eastSpeedMps;
            StartLanded = startLanded;
            MaxSeconds = maxSeconds;
        }
    }

    /// <summary>Возвращает число расхождений повторных прогонов (0 — успех).</summary>
    public static int RunLandingTraces(string outDir, bool legacy)
    {
        if (!legacy)
        {
            Console.WriteLine("--trace: вынесенный шаг ещё не создан (шаг 0.4 ТЗ v2) — используйте --trace-legacy");
            return 2;
        }

        var scenarios = new[]
        {
            new TraceScenario("S1_drop2km", 2000d, 0d, 0d, false, 900d),
            new TraceScenario("S2_descent2", 100d, -2d, 0d, false, 300d),
            new TraceScenario("S3_impact50", 25d, -50d, 0d, false, 120d),
            new TraceScenario("S4_entry", 120000d, -300d, 1500d, false, 3600d),
            new TraceScenario("S5_stand", 0d, 0d, 0d, true, 3600d),
        };

        string only = Environment.GetEnvironmentVariable("P1B_TRACE_ONLY");
        if (!string.IsNullOrEmpty(only))
        {
            scenarios = Array.FindAll(scenarios, s => s.Name.StartsWith(only, StringComparison.Ordinal));
        }

        Directory.CreateDirectory(outDir);
        string repeatDir = Path.Combine(
            Path.GetTempPath(), "opencode", "trace-repeat", Path.GetFileName(Path.GetFullPath(outDir)));
        if (Directory.Exists(repeatDir))
        {
            Directory.Delete(repeatDir, true);
        }

        var summary = new StringBuilder();
        summary.AppendLine("Landing traces, шаг 0.6");
        summary.AppendLine("Источник шага: " + (legacy ? "LegacyVesselStep (слепок SimulationRunner до 0.4)" : "VesselStep (после 0.4)"));
        summary.AppendLine("Мир: Terra mu=1.28e13 R=1.143e6 rotation=86400 s pole=+Z;");
        summary.AppendLine("     terrain=EarthLike_Perlin seed=24334543; atmosphere: Top=100 km rho0=1.225 H=8500 (drag Cd=1 A=10 у Terra, как в SimulationRunner.Awake);");
        summary.AppendLine("     корабль 5000 kg, тяга в стенде не включена, старт lat=0 lon=0.");
        summary.AppendLine("Колонки: t,alt,v_radial,v_tangential,regime,lat,lon,event; шаг 0.5 s (2 Гц), событие — отдельной строкой в точный момент.");
        summary.AppendLine("Скорости — относительно со-вращающейся поверхности (кадр SurfaceMotion): стоянка даёт 0, падение — вертикальную скорость.");
        summary.AppendLine();

        int mismatches = 0;
        foreach (TraceScenario scenario in scenarios)
        {
            string file = scenario.Name + ".csv";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("trace " + scenario.Name + ": run 1...");
            RunScenarioOnce(outDir, file, scenario, legacy, summary);
            Console.WriteLine("trace " + scenario.Name + ": run 1 done in " + watch.ElapsedMilliseconds + " ms; run 2...");
            string hashA = HashFile(Path.Combine(outDir, file));
            RunScenarioOnce(repeatDir, file, scenario, legacy, summary);
            Console.WriteLine("trace " + scenario.Name + ": run 2 done in " + watch.ElapsedMilliseconds + " ms");
            string hashB = HashFile(Path.Combine(repeatDir, file));
            bool identical = hashA == hashB;
            if (!identical)
            {
                mismatches++;
            }

            summary.AppendLine(string.Format(Inv,
                "repeat {0}: {1} == {2} -> {3}", scenario.Name, hashA, hashB, identical ? "IDENTICAL" : "MISMATCH"));
            summary.AppendLine();
        }

        File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.ToString(), new UTF8Encoding(false));
        if (Directory.Exists(repeatDir))
        {
            Directory.Delete(repeatDir, true);
        }

        Console.WriteLine(mismatches == 0
            ? "LANDING TRACES: ALL REPEATS IDENTICAL"
            : "LANDING TRACES: " + mismatches + " REPEAT MISMATCHES");
        return mismatches;
    }

    private static void RunScenarioOnce(string dir, string file, TraceScenario scenario, bool legacy, StringBuilder summary)
    {
        bool dbg = Environment.GetEnvironmentVariable("P1B_TRACE_DEBUG") == "1";
        Directory.CreateDirectory(dir);
        StarSystem sys = TraceWorld(out OrbitingBody terra);
        if (dbg)
        {
            Console.WriteLine("  [dbg] world built; h(0,0)=" + terra.Terrain.GetHeightMeters(terra, 0d, 0d).ToString("F3", Inv));
        }

        TraceRig rig = BuildTraceRig(sys, terra, legacy);
        if (dbg)
        {
            Console.WriteLine("  [dbg] rig built");
        }

        var sb = new StringBuilder();
        sb.AppendLine("t,alt,v_radial,v_tangential,regime,lat,lon,event");

        bool sawTouchdown = false;
        double impactTime = double.NaN;
        double impactVn = double.NaN;
        double impactVt = double.NaN;
        double startAltitude = double.NaN;
        rig.SetOccurrenceHook(occurrence =>
        {
            // Хук вызывается ДО реакции: состояние — момент события.
            AppendTraceRow(sb, terra, rig.Ship, occurrence.TimeSeconds, rig.GetRegime(), occurrence.Kind.ToString());
            if (occurrence.Kind == EventKind.Touchdown && !sawTouchdown)
            {
                sawTouchdown = true;
                impactTime = occurrence.TimeSeconds;
                SampleState(terra, rig.Ship, occurrence.TimeSeconds, out _, out impactVn, out impactVt, out _, out _);
            }
        });

        SpawnTraceShip(rig, terra, scenario);
        SampleState(terra, rig.Ship, rig.GetTime(), out startAltitude, out _, out _, out _, out _);
        if (dbg)
        {
            Console.WriteLine("  [dbg] spawned, alt=" + startAltitude.ToString("F3", Inv) + " t=" + rig.GetTime().ToString("F3", Inv));
        }

        AppendTraceRow(sb, terra, rig.Ship, rig.GetTime(), rig.GetRegime(), string.Empty);

        int guard = (int)(scenario.MaxSeconds / TraceStepSeconds) + 100;
        for (int i = 0; i < guard; i++)
        {
            if (dbg && i % 200 == 0)
            {
                Console.WriteLine("  [dbg] step " + i + " t=" + rig.GetTime().ToString("F3", Inv) + " regime=" + rig.GetRegime());
            }

            if (rig.GetTime() >= scenario.MaxSeconds - 1e-9d)
            {
                break;
            }

            if (scenario.StartLanded)
            {
                rig.StepLanded((float)TraceStepSeconds, true);
            }
            else
            {
                rig.StepFlying((float)TraceStepSeconds, true);
            }

            AppendTraceRow(sb, terra, rig.Ship, rig.GetTime(), rig.GetRegime(), string.Empty);
            if (rig.GetRegime() == VesselRegime.Destroyed || (!scenario.StartLanded && rig.GetRegime() != VesselRegime.Flying))
            {
                break;
            }
        }

        File.WriteAllText(Path.Combine(dir, file), sb.ToString(), new UTF8Encoding(false));

        summary.AppendLine(string.Format(Inv,
            "{0}: start_alt={1:F3} m; touchdown_t={2}; v_n={3}; v_t={4}; regime_after={5}; debris={6}; final_t={7:F3}; final_alt={8:F3}",
            scenario.Name,
            startAltitude,
            double.IsNaN(impactTime) ? "нет" : impactTime.ToString("F3", Inv),
            double.IsNaN(impactVn) ? "n/a" : impactVn.ToString("F3", Inv),
            double.IsNaN(impactVt) ? "n/a" : impactVt.ToString("F3", Inv),
            rig.GetRegime(),
            rig.DebrisCount(),
            rig.GetTime(),
            SampleAlt(terra, rig.Ship, rig.GetTime())));
    }

    private static StarSystem TraceWorld(out OrbitingBody terra)
    {
        StarSystem sys = ExampleSystemPresets.CreateExampleSystem();
        terra = sys.AllBodies[1];
        terra.Name = "Terra";
        terra.StandardGravitationalParameter = 1.28e13d;
        terra.Radius = 1143000d;
        terra.RotationPeriodSeconds = 86400d;
        terra.NorthPoleDirection = new Vector3d(0d, 0d, 1d);
        terra.PrimeMeridianOffsetDegrees = 0d;
        terra.Terrain = HeightfieldTerrain.FromProfile(TerraProfile(), 24334543);
        terra.Atmosphere = new AtmosphereProfile
        {
            TopAltitudeMeters = 100000d,
            SeaLevelDensityKgPerCubicMeter = 1.225d,
            ScaleHeightMeters = 8500d
        };
        terra.SyncMassFromGravitationalParameter();
        return sys;
    }

    private static TerrainProfile TerraProfile()
    {
        // Снимок профиля EarthLike_Perlin (Assets/_Project/Profiles/Terrain) —
        // физически значимые поля; цвета/текстуры на контакт не влияют.
        return new TerrainProfile
        {
            AmplitudeMeters = 13662d,
            BaseFrequency = 20d,
            Octaves = 10,
            SeaLevelMeters = 0d,
            Lacunarity = 2d,
            Gain = 0.5d,
            RidgedMix = 0.7d,
            RidgedMode = 1,
            RidgedSharpness = 2d,
            RidgedWeightGain = 2d,
            RidgedGamma = 1d,
            SlopeDamp = 0d,
            SlopeDampMode = 0,
            NoiseStyle = 1,
            MaskNoiseStyle = -1,
            ContinentFrequency = 1.3d,
            ContinentOctaves = 5,
            ContinentThreshold = 0.14d,
            ContinentSharpness = 0.08d,
            ContinentDepth = 1.2d,
            ContinentGain = 0.55d,
            ContinentWarpStrength = 0.3d,
            ContinentWarpFrequency = 1.5d,
            ContinentWarpOctaves = 3,
            ContinentRidgeMix = 0.12d,
            ContinentRidgeFrequency = 2.5d,
            ContinentRidgeOctaves = 2,
            ContinentLatitudeBias = 0.08d,
            OceanFloorDepth = 0.42d,
            OceanShelfDepth = 0.02d,
            InteriorFloor = 0.035d,
            OrogenyFrequency = 1.1d,
            OrogenyOctaves = 3,
            OrogenyThreshold = -0.4d,
            OrogenySharpness = 0.3d,
            OrogenyFloor = 0.15d,
            OrogenyGain = 0.85d,
            PlainMix = 0.85d,
            PlainFrequency = 2d,
            PlainOctaves = 2,
            PlainThreshold = 0d,
            PlainSharpness = 0.25d,
            PlainElevation = 0.1d,
            TailKnee = 0.13d,
            TailThreshold = 0.5656d,
            DepthKnee = 0d,
            DepthThreshold = 0d,
            BeachHeightMeters = 18d,
            BeachShelfAltitudeMeters = 12d,
            BeachShelfWidth = 0.03d,
            BeachShelfWidthMaxScale = 14d,
            BeachShelfWidthNoiseFrequency = 150d,
            BeachShelfWidthNoiseOctaves = 4,
            BeachShelfSeawardWidth = 0d,
            DetailMix = 0d,
            DetailFrequency = 0d,
            DetailOctaves = 5,
            WarpStrength = 0.1d,
            WarpFrequency = 2d,
            WarpOctaves = 2,
            WarpSeedOffset = 0
        };
    }

    private static TraceRig BuildTraceRig(StarSystem sys, OrbitingBody terra, bool legacy)
    {
        var warp = new WarpController();
        var physics = new SpacecraftPhysics();
        physics.Sources.Add(new CachedGravitySource(sys));
        if (terra.Atmosphere != null)
        {
            physics.Sources.Add(new DragSource(terra));
        }

        var thrust = new ThrustSource(TraceThrustN, TraceDryMassKg, new ConstantIsp(TraceIspSeconds));
        thrust.ThrottleAt = t => warp.CurrentControlSnapshot.Throttle;
        physics.Sources.Add(thrust);

        var propagator = new EventDrivenPropagator(physics);
        foreach (OrbitingBody body in sys.AllBodies)
        {
            propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(body));
            if (body.Atmosphere != null)
            {
                propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(body));
                propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(body));
            }
        }

        propagator.HardHorizonSeconds = sys.BakedEndSeconds();
        var driver = new LongWarpDriver(physics, propagator);
        var breakup = new JointedBreakup();
        var debris = new DebrisPool();

        if (!legacy)
        {
            throw new NotSupportedException("VesselStep появится на шаге 0.4; трейс-режим --trace включится после выноса.");
        }

        var step = new LegacyVesselStep(
            sys, warp, propagator, driver, thrust, breakup, debris,
            new PartDefinition[0], TraceSpawnEpsilonMeters, TraceDebrisLifetimeSeconds);

        TraceRig rig = null;
        rig = new TraceRig
        {
            GetTime = () => step.TimeSeconds,
            GetRegime = () => step.Regime,
            StepFlying = (dt, inShip) => step.StepFlying(dt, inShip),
            StepLanded = (dt, inShip) => step.StepLanded(dt, inShip),
            SetShip = ship =>
            {
                step.DominantBody = terra;
                step.Ship = ship;
                rig.Ship = ship;
            },
            SetLanded = () =>
            {
                step.Regime = VesselRegime.Landed;
                step.DominantBody = terra;
            },
            SetOccurrenceHook = hook => step.OccurrenceHandled = hook,
            DebrisCount = () => debris.ActiveCount
        };

        return rig;
    }

    private static void SpawnTraceShip(TraceRig rig, OrbitingBody terra, TraceScenario scenario)
    {
        terra.EvaluateWorldState(0d, out Vector3d bodyPosition, out _);
        double height = terra.Terrain.GetHeightMeters(terra, 0d, 0d);
        terra.GetSurfaceState(0d, 0d, height, 0d, out Vector3d surfacePosition, out Vector3d surfaceVelocity);
        Vector3d radial = (surfacePosition - bodyPosition).Normalized;
        Vector3d east = Vector3d.Cross(new Vector3d(0d, 0d, 1d), radial);
        if (east.SqrMagnitude < 1e-12d)
        {
            east = new Vector3d(1d, 0d, 0d);
        }

        east = east.Normalized;

        Vector3d position;
        Vector3d velocity;
        if (scenario.StartLanded)
        {
            position = surfacePosition;
            velocity = surfaceVelocity;
        }
        else
        {
            position = surfacePosition + (radial * scenario.SpawnAltitudeMeters);
            velocity = surfaceVelocity + (radial * scenario.RadialSpeedMps) + (east * scenario.EastSpeedMps);
        }

        rig.SetShip(new Ship(position, velocity, TraceShipMassKg));
        if (scenario.StartLanded)
        {
            rig.SetLanded();
        }
    }

    private static void AppendTraceRow(StringBuilder sb, OrbitingBody body, Ship ship, double t, VesselRegime regime, string eventName)
    {
        SampleState(body, ship, t, out double altitude, out double radialSpeed, out double tangentialSpeed, out double latDeg, out double lonDeg);
        sb.Append(string.Format(Inv,
            "{0:F3},{1:F6},{2:F6},{3:F6},{4},{5:F7},{6:F7},{7}",
            t, altitude, radialSpeed, tangentialSpeed, regime, latDeg, lonDeg, eventName));
        sb.Append('\n');
    }

    private static void SampleState(
        OrbitingBody body, Ship ship, double t,
        out double altitude, out double radialSpeed, out double tangentialSpeed, out double latDeg, out double lonDeg)
    {
        body.EvaluateWorldState(t, out Vector3d bodyPosition, out Vector3d bodyVelocity);
        Vector3d relative = ship.Position - bodyPosition;
        double distance = relative.Magnitude;
        body.SurfaceLatLonAt(ship.Position, t, out latDeg, out lonDeg);
        double height = body.Terrain.GetHeightMeters(body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d));
        altitude = distance - (body.Radius + height);
        Vector3d radial = distance > 1e-9d ? relative / distance : new Vector3d(0d, 0d, 1d);
        // Скорости — в кадре со-вращающейся поверхности (как SurfaceMotion):
        // стоящий аппарат даёт ноль, падение — честную вертикальную скорость.
        Vector3d surfaceVelocity = bodyVelocity + Vector3d.Cross(body.SpinAxis * body.SpinAngularSpeed, relative);
        Vector3d relativeVelocity = ship.Velocity - surfaceVelocity;
        radialSpeed = Vector3d.Dot(relativeVelocity, radial);
        tangentialSpeed = (relativeVelocity - (radial * radialSpeed)).Magnitude;
    }

    private static double SampleAlt(OrbitingBody body, Ship ship, double t)
    {
        SampleState(body, ship, t, out double altitude, out _, out _, out _, out _);
        return altitude;
    }

    private static string HashFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
