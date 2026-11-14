using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Galilego.Debris;
using Galilego.Flight;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

// Test565: Appendix D, stage D4. Measures the two branches the owner kept in scope
// (R1 = WH-BM14A port; R3 = Sun-coast, report only) through the real VesselStep
// path and writes results/dynamic.csv. DYNAMIC_REPORT.md is generated from that CSV.
// No production-code change: the flags stay false by default; the test sets them on
// its own rigs only.
internal static partial class P1bTests
{
    private const double DynEarthMu = 3.986004418e14d;
    private const double DynEarthRadius = 6.371e6d;
    private const double DynSunMu = 1.32712440018e20d;
    private const double DynMoonMu = 4.9028695e12d;
    private const double DynAu = 1.495978707e11d;
    private const double DynChunk = 600d;
    private const double DynDay = 86400d;
    private const double DynErrorTargetM = 1d;
    private const double DynEventTargetS = 1e-3d;
    private const double DynWallRatioTarget = 0.5d;

    private static int Test565_DynamicPolicy()
    {
        Console.WriteLine("--- Test565: Appendix D, dynamic policy through VesselStep (D4) ---");
        string folder = DynResultsFolder();
        Directory.CreateDirectory(folder);
        string csvPath = Path.Combine(folder, "dynamic.csv");

        var rows = new List<DynRow>();
        DynRunR1(rows);
        DynRunR1OutOfRegime(rows);
        DynRunR3(rows);
        DynRunD5(rows);

        DynWriteCsv(csvPath, rows);
        string reportPath = Path.Combine(FindHarnessRoot(), "Study",
            "dynamic_policy", "DYNAMIC_REPORT.md");
        DynWriteReport(csvPath, reportPath);
        Console.WriteLine("dynamic.csv: " + csvPath);
        Console.WriteLine("report: " + reportPath);

        int failed = 0;
        foreach (DynRow row in rows)
        {
            if (row.Verdict == "FAIL")
            {
                failed++;
            }
        }

        // A FAIL row is a gate result, not a harness error: D4 records it and the
        // owner decides (TASK_D §7 D4/D7). The test itself succeeded.
        Console.WriteLine("rows: " + rows.Count + ", FAIL rows (gate result): " + failed);
        return 0;
    }

    // ----------------------------------------------------------------------------------
    // R1: WH-BM14A through VesselStep, C7.5 comparison against the certified default path
    // (DOP853 with the policy off). The stand's certified reference is a different model
    // (study local frame); the game path's certified default is DOP853, so that is the
    // reference here and the absolute gate is the §6 1 m target.
    // ----------------------------------------------------------------------------------
    private static void DynRunR1(List<DynRow> rows)
    {
        var scenarios = new (string Id, double InclinationDegrees, bool J2)[]
        {
            ("S01", 0d, false),
            ("S02", 90d, false),
            ("S03", 98d, true),
            ("S09", 0d, true)
        };
        double[] altitudes = { 200_000d, 400_000d, 2_000_000d };

        foreach ((string id, double inclination, bool j2) in scenarios)
        {
            foreach (double altitude in altitudes)
            {
                StarSystem system = DynCreateSunEarthMoon(j2);
                OrbitingBody earth = system.AllBodies[1];

                DynRig wh = DynBuildRig(system, ported: true);
                DynRig dop = DynBuildRig(system, ported: false);
                DynPlaceShip(wh.Step, earth, altitude, inclination, 0d);
                DynPlaceShip(dop.Step, earth, altitude, inclination, 0d);

                double maxError = 0d;
                bool firstIsWh = false;
                long whCalls = 0L;
                long chunks = (long)(DynDay / DynChunk);
                for (long i = 0; i < chunks; i++)
                {
                    wh.Step.StepFlying((float)DynChunk, true);
                    dop.Step.StepFlying((float)DynChunk, true);
                    if (i == 0)
                    {
                        firstIsWh = wh.Step.LastSelectedIntegrator == SpacecraftIntegrator.WhBm14a;
                    }

                    if (wh.Step.LastSelectedIntegrator == SpacecraftIntegrator.WhBm14a)
                    {
                        whCalls++;
                    }

                    double dp = (wh.Step.Ship.Position - dop.Step.Ship.Position).Magnitude;
                    if (dp > maxError)
                    {
                        maxError = dp;
                    }
                }

                string tag = id + " " + FormatAltitude(altitude);
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "selected_first_is_wh",
                    firstIsWh ? 1d : 0d, "1/0", 1d, firstIsWh ? "PASS" : "FAIL",
                    "selection of the first 600 s chunk"));
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "wh_chunk_share",
                    (double)whCalls / chunks, "fraction", double.NaN, "INFO",
                    "calls whose policy selection was WH"));
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "error_max_m", maxError, "m",
                    DynErrorTargetM, maxError <= DynErrorTargetM ? "PASS" : "FAIL",
                    "max abs(r_WH - r_DOP853) over 1 day"));
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "fallbacks",
                    wh.Physics.StepDiagnostics.Fallbacks, "count", double.NaN, "INFO",
                    "guard fallbacks (D3)"));
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "wh_state_finite",
                    wh.Step.Ship.Position.IsFinite && wh.Step.Ship.Velocity.IsFinite ? 1d : 0d, "1/0",
                    1d, wh.Step.Ship.Position.IsFinite && wh.Step.Ship.Velocity.IsFinite ? "PASS" : "FAIL", ""));

                // Determinism: two independent identically-built rigs, one 600 s call.
                DynRig a = DynBuildRig(system, ported: true);
                DynRig b = DynBuildRig(system, ported: true);
                DynPlaceShip(a.Step, earth, altitude, inclination, 0d);
                DynPlaceShip(b.Step, earth, altitude, inclination, 0d);
                a.Step.StepFlying((float)DynChunk, true);
                b.Step.StepFlying((float)DynChunk, true);
                bool same = a.Step.Ship.Position.X == b.Step.Ship.Position.X
                    && a.Step.Ship.Position.Y == b.Step.Ship.Position.Y
                    && a.Step.Ship.Position.Z == b.Step.Ship.Position.Z
                    && a.Step.Ship.Velocity.X == b.Step.Ship.Velocity.X
                    && a.Step.Ship.Velocity.Y == b.Step.Ship.Velocity.Y
                    && a.Step.Ship.Velocity.Z == b.Step.Ship.Velocity.Z;
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "determinism_bitwise", same ? 1d : 0d, "1/0",
                    1d, same ? "PASS" : "FAIL", ""));

                // Allocations: one warm 600 s chunk, WH vs DOP853.
                long allocWh = DynAllocOneChunk(system, earth, altitude, inclination, true);
                long allocDop = DynAllocOneChunk(system, earth, altitude, inclination, false);
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "alloc_bytes_wh", allocWh, "B",
                    allocDop, allocWh <= allocDop ? "PASS" : "FAIL", "one 600 s chunk"));
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "alloc_bytes_dop853", allocDop, "B",
                    double.NaN, "INFO", "one 600 s chunk"));

                // §6 wall row, option A: the game's VesselStep path (30 s event grain).
                double wallWh = DynMedianChunkWall(system, earth, altitude, inclination, true);
                double wallDop = DynMedianChunkWall(system, earth, altitude, inclination, false);
                double ratio = wallWh / wallDop;
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "wall_ms_wh", wallWh, "ms",
                    double.NaN, "INFO", "median of 5; one 600 s call"));
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "wall_ms_dop853", wallDop, "ms",
                    double.NaN, "INFO", "median of 5; one 600 s call"));
                rows.Add(DynRow.Number("R1", tag, altitude, 0d, "wall_ratio", ratio, "x",
                    DynWallRatioTarget, ratio <= DynWallRatioTarget ? "PASS" : "FAIL",
                    "policy + WH <= 0.5 x DOP853 (option A)"));
            }
        }

        // Events: crossing and timed, WH vs DOP853 (S01, 400 km).
        DynRig whEvent = DynBuildRig(DynCreateSunEarthMoon(false), ported: true);
        DynRig dopEvent = DynBuildRig(DynCreateSunEarthMoon(false), ported: false);
        OrbitingBody earthEventWh = whEvent.Step.SystemState.AllBodies[1];
        OrbitingBody earthEventDop = dopEvent.Step.SystemState.AllBodies[1];
        double crossWh = DynRunEvent(whEvent, earthEventWh, crossing: true);
        double crossDop = DynRunEvent(dopEvent, earthEventDop, crossing: true);
        double crossDt = Math.Abs(crossWh - crossDop);
        rows.Add(DynRow.Number("R1", "S01 event", 400_000d, 0d, "event_crossing_dt_s", crossDt, "s",
            DynEventTargetS, crossDt <= DynEventTargetS ? "PASS" : "FAIL",
            "altitude crossing 420 km; WH vs DOP853"));

        DynRig whTimed = DynBuildRig(DynCreateSunEarthMoon(false), ported: true);
        DynRig dopTimed = DynBuildRig(DynCreateSunEarthMoon(false), ported: false);
        double timedWh = DynRunEvent(whTimed, whTimed.Step.SystemState.AllBodies[1], crossing: false);
        double timedDop = DynRunEvent(dopTimed, dopTimed.Step.SystemState.AllBodies[1], crossing: false);
        double timedDt = Math.Abs(timedWh - timedDop);
        rows.Add(DynRow.Number("R1", "S01 event", 400_000d, 0d, "event_timed_dt_s", timedDt, "s",
            DynEventTargetS, timedDt <= DynEventTargetS ? "PASS" : "FAIL",
            "timed event 1234.5 s; WH vs DOP853"));
    }

    // ----------------------------------------------------------------------------------
    // R1 outside the regime: DOP853 and bitwise equal to the flag-off path.
    // ----------------------------------------------------------------------------------
    private static void DynRunR1OutOfRegime(List<DynRow> rows)
    {
        // Eccentric orbit (e=0.2) at 400 km perigee.
        DynOutCase(rows, "e=0.2", 400_000d, 0.2d, 600f, thrust: false);
        // Altitude outside the band.
        DynOutCase(rows, "alt 3000 km", 3_000_000d, 0d, 600f, thrust: false);
        // Chunk shorter than 600 s.
        DynOutCase(rows, "chunk 300 s", 400_000d, 0d, 300f, thrust: false);
        // Active thrust: CanCoast false.
        DynOutCase(rows, "thrust", 400_000d, 0d, 600f, thrust: true);

        // Sun-dominant body: the ported branch never applies.
        StarSystem sun = DynCreateSun();
        DynRig on = DynBuildRig(sun, ported: true);
        DynRig off = DynBuildRig(sun, ported: false);
        DynPlaceHeliocentric(on.Step, 1d);
        DynPlaceHeliocentric(off.Step, 1d);
        on.Step.StepFlying((float)DynChunk, true);
        off.Step.StepFlying((float)DynChunk, true);
        bool same = DynSameState(on.Step.Ship, off.Step.Ship);
        rows.Add(DynRow.Number("R1_out", "sun-dominant", double.NaN, 0d, "selected_dop853",
            on.Step.LastSelectedIntegrator == SpacecraftIntegrator.Dop853 ? 1d : 0d, "1/0", 1d,
            on.Step.LastSelectedIntegrator == SpacecraftIntegrator.Dop853 ? "PASS" : "FAIL", ""));
        rows.Add(DynRow.Number("R1_out", "sun-dominant", double.NaN, 0d, "bitwise_vs_off", same ? 1d : 0d,
            "1/0", 1d, same ? "PASS" : "FAIL", ""));
    }

    private static void DynOutCase(
        List<DynRow> rows, string tag, double altitude, double eccentricity, float chunk, bool thrust)
    {
        StarSystem system = DynCreateSunEarthMoon(false);
        OrbitingBody earth = system.AllBodies[1];
        DynRig on = DynBuildRig(system, ported: true, thrust: thrust);
        DynRig off = DynBuildRig(system, ported: false, thrust: thrust);
        DynPlaceShip(on.Step, earth, altitude, 0d, eccentricity);
        DynPlaceShip(off.Step, earth, altitude, 0d, eccentricity);
        if (thrust)
        {
            on.Step.RawThrottle = 1d;
            off.Step.RawThrottle = 1d;
        }

        on.Step.StepFlying(chunk, true);
        off.Step.StepFlying(chunk, true);
        bool selectedDop = on.Step.LastSelectedIntegrator == SpacecraftIntegrator.Dop853;
        bool same = DynSameState(on.Step.Ship, off.Step.Ship);
        rows.Add(DynRow.Number("R1_out", tag, altitude, eccentricity, "selected_dop853",
            selectedDop ? 1d : 0d, "1/0", 1d, selectedDop ? "PASS" : "FAIL", ""));
        rows.Add(DynRow.Number("R1_out", tag, altitude, eccentricity, "bitwise_vs_off", same ? 1d : 0d,
            "1/0", 1d, same ? "PASS" : "FAIL", ""));
    }

    // ----------------------------------------------------------------------------------
    // R3: Sun-coast report only. The flag stays false in the game (Q3 = NO).
    // ----------------------------------------------------------------------------------
    private static void DynRunR3(List<DynRow> rows)
    {
        foreach (double chunk in new[] { 30d, 600d })
        {
            StarSystem sun = DynCreateSun();
            DynRig on = DynBuildRig(sun, ported: false, sunCoast: true);
            DynRig off = DynBuildRig(sun, ported: false, sunCoast: false);
            DynPlaceHeliocentric(on.Step, 1d);
            DynPlaceHeliocentric(off.Step, 1d);

            long calls = (long)(DynDay / chunk);
            double maxError = 0d;
            for (long i = 0; i < calls; i++)
            {
                on.Step.StepFlying((float)chunk, true);
                off.Step.StepFlying((float)chunk, true);
                double dp = (on.Step.Ship.Position - off.Step.Ship.Position).Magnitude;
                if (dp > maxError)
                {
                    maxError = dp;
                }
            }

            string tag = "1 AU " + chunk.ToString("F0", CultureInfo.InvariantCulture) + " s";
            rows.Add(DynRow.Number("R3", tag, double.NaN, 0d, "selected_dopri5",
                on.Step.LastSelectedIntegrator == SpacecraftIntegrator.Dopri5 ? 1d : 0d, "1/0", 1d,
                on.Step.LastSelectedIntegrator == SpacecraftIntegrator.Dopri5 ? "PASS" : "FAIL", ""));
            rows.Add(DynRow.Number("R3", tag, double.NaN, 0d, "error_max_m", maxError, "m",
                DynErrorTargetM, maxError <= DynErrorTargetM ? "PASS" : "FAIL", "1 day vs DOP853"));
            rows.Add(DynRow.Number("R3", tag, double.NaN, 0d, "wall_ms",
                DynMedianSunWall(sunCoast: true, chunk), "ms", double.NaN, "INFO", "median of 5"));
            rows.Add(DynRow.Number("R3", tag, double.NaN, 0d, "wall_ms_dop853",
                DynMedianSunWall(sunCoast: false, chunk), "ms", double.NaN, "INFO", "median of 5"));
            rows.Add(DynRow.Number("R3", tag, double.NaN, 0d, "rhs_eq_est",
                DynRhsEstimate(sunCoast: true, chunk), "calls", double.NaN, "INFO",
                "attempts x stages (VesselStep has no RHS counter)"));

            double timedOn = DynRunHelioTimed(sunCoast: true, chunk);
            double timedOff = DynRunHelioTimed(sunCoast: false, chunk);
            double timedDt = Math.Abs(timedOn - timedOff);
            rows.Add(DynRow.Number("R3", tag, double.NaN, 0d, "event_timed_dt_s", timedDt, "s",
                DynEventTargetS, timedDt <= DynEventTargetS ? "PASS" : "FAIL",
                "timed event 3600 s; DOPRI5 vs DOP853"));
        }

        // 5 AU and cislunar guards.
        StarSystem far = DynCreateSun();
        DynRig farOn = DynBuildRig(far, ported: false, sunCoast: true);
        DynPlaceHeliocentric(farOn.Step, 5d);
        farOn.Step.StepFlying((float)DynChunk, true);
        rows.Add(DynRow.Number("R3", "5 AU", double.NaN, 0d, "selected_dopri5",
            farOn.Step.LastSelectedIntegrator == SpacecraftIntegrator.Dopri5 ? 1d : 0d, "1/0", 0d,
            farOn.Step.LastSelectedIntegrator == SpacecraftIntegrator.Dopri5 ? "FAIL" : "PASS",
            "TASK_D expects 5 AU to stay on DOP853"));

        StarSystem cislunar = DynCreateSunEarthMoon(false);
        OrbitingBody earth = cislunar.AllBodies[1];
        DynRig near = DynBuildRig(cislunar, ported: false, sunCoast: true);
        earth.EvaluateWorldState(0d, out Vector3d earthPosition, out Vector3d earthVelocity);
        double soi = earth.SphereOfInfluenceRadius;
        Vector3d radial = new Vector3d(1d, 0d, 0d);
        near.Step.Ship = new Ship(
            earthPosition + radial * (5d * soi),
            earthVelocity + new Vector3d(0d, 0d, 100d),
            1000d);
        near.Step.StepFlying((float)DynChunk, true);
        rows.Add(DynRow.Number("R3", "cislunar-like", double.NaN, 0d, "selected_dop853",
            near.Step.LastSelectedIntegrator == SpacecraftIntegrator.Dop853 ? 1d : 0d, "1/0", 1d,
            near.Step.LastSelectedIntegrator == SpacecraftIntegrator.Dop853 ? "PASS" : "FAIL",
            "near another body keeps DOP853"));
    }

    // ----------------------------------------------------------------------------------
    // D5: one scripted phase sequence through VesselStep. Owner decision A after D4:
    // every policy flag is off, so every phase must stay on DOP853 / Default. The
    // sequence still exercises flight chunks, live chunk, long warp, thrust and
    // touchdown, and the two runs must be bitwise identical.
    // ----------------------------------------------------------------------------------
    private sealed class DynD5Phase
    {
        public string Name;
        public SpacecraftIntegrator Integrator;
        public IntegratorSelectionReason Reason;
        public double AltitudeM;
    }

    private sealed class DynD5Result
    {
        public readonly List<DynD5Phase> Phases = new List<DynD5Phase>();
        public double TouchdownSeconds = double.NaN;
        public bool Landed;
        public Ship FinalShip;
    }

    private static void DynRunD5(List<DynRow> rows)
    {
        DynD5Result first = DynRunD5Once();
        DynD5Result second = DynRunD5Once();

        foreach (DynD5Phase phase in first.Phases)
        {
            bool ok = phase.Integrator == SpacecraftIntegrator.Dop853
                && phase.Reason == IntegratorSelectionReason.Default;
            rows.Add(DynRow.Number("D5", phase.Name, phase.AltitudeM, 0d, "integrator",
                (double)(int)phase.Integrator, "enum", 0d, ok ? "PASS" : "FAIL",
                "reason=" + phase.Reason));
            rows.Add(DynRow.Number("D5", phase.Name, phase.AltitudeM, 0d, "reason",
                (double)(int)phase.Reason, "enum", 0d,
                phase.Reason == IntegratorSelectionReason.Default ? "PASS" : "FAIL", ""));
        }

        rows.Add(DynRow.Number("D5", "touchdown", double.NaN, 0d, "event_touchdown_s",
            first.TouchdownSeconds, "s", double.NaN,
            double.IsFinite(first.TouchdownSeconds) ? "PASS" : "FAIL", ""));
        rows.Add(DynRow.Number("D5", "touchdown", double.NaN, 0d, "regime_landed",
            first.Landed ? 1d : 0d, "1/0", 1d, first.Landed ? "PASS" : "FAIL", ""));

        bool same = first.FinalShip != null && second.FinalShip != null
            && DynSameState(first.FinalShip, second.FinalShip);
        rows.Add(DynRow.Number("D5", "sequence", double.NaN, 0d, "determinism_bitwise",
            same ? 1d : 0d, "1/0", 1d, same ? "PASS" : "FAIL", "two identical scripted runs"));
    }

    private static DynD5Result DynRunD5Once()
    {
        StarSystem system = DynCreateSunEarthMoon(false);
        OrbitingBody earth = system.AllBodies[1];

        var physics = new SpacecraftPhysics
        {
            Integrator = SpacecraftIntegrator.Dop853,
            ToleranceFrameSystem = system
        };
        physics.Sources.Add(new CachedGravitySource(system));
        var flight = new VesselFlightModel(
            new[]
            {
                new PartDefinition
                {
                    Name = "Test part",
                    MassKg = 1000d,
                    Shape = PartShape.Cylinder,
                    Dimensions = new Vector3(2f, 4f, 2f),
                    ParentIndex = -1
                }
            },
            new[] { 1000d },
            new[] { 0d },
            new[] { -1 },
            new[] { 0d });
        var flightSource = new FlightThrustSource { Model = flight };
        physics.Sources.Add(flightSource);
        var thrust = new TestThrustSource
        {
            ThrustNewtons = 100d,
            MassFlowKgS = 0d,
            Direction = new Vector3d(1d, 0d, 0d),
            ActiveAt = _ => true
        };
        physics.Sources.Add(thrust);

        var propagator = new EventDrivenPropagator(physics)
        {
            HardHorizonSeconds = system.BakedEndSeconds()
        };
        propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(earth));
        var driver = new LongWarpDriver(physics, propagator);
        var step = new VesselStep(
            system, new WarpController(), propagator, driver, null,
            new JointedBreakup(), new DebrisPool(), new PartDefinition[0], 1d, 600d);
        step.Flight = flight;
        step.FlightSource = flightSource;
        step.Thrust = thrust;
        step.EnableRegimeIntegratorPolicy = false;
        step.EnableSunCoastPolicy = false;
        step.EnablePortedIntegratorPolicy = false;

        var result = new DynD5Result();
        step.OccurrenceHandled = occurrence =>
        {
            if (occurrence.Kind == EventKind.Touchdown && double.IsNaN(result.TouchdownSeconds))
            {
                result.TouchdownSeconds = occurrence.TimeSeconds;
            }
        };

        // 1) surface, flight model, 0.05 s chunk.
        DynD5Place(step, earth, 1d, 2d, 0d);
        DynD5Step(step, earth, "surface liftoff 0.05 s", 0.05f, 1, result);

        // 2) ascent, flight model, 0.05 s chunk.
        DynD5Place(step, earth, 5_000d, 100d, 0d);
        DynD5Step(step, earth, "ascent 5 km 0.05 s", 0.05f, 1, result);

        // 3) circular LEO, live chunk 1/60 s.
        DynD5Place(step, earth, 400_000d, 0d, DynCircularSpeed(earth, 400_000d));
        DynD5Step(step, earth, "LEO live 1/60 s", 1f / 60f, 1, result);

        // 4) long warp 600 s.
        DynD5Step(step, earth, "LEO long-warp 600 s", (float)DynChunk, 1, result);

        // 5) thrust maneuver: the policy must keep DOP853 (flags are off; the source
        //    scan is not even reached, which is exactly the disabled-path check).
        step.RawThrottle = 1d;
        DynD5Step(step, earth, "thrust maneuver 600 s", (float)DynChunk, 1, result);
        step.RawThrottle = 0d;

        // 6) long warp again.
        DynD5Step(step, earth, "LEO long-warp again 600 s", (float)DynChunk, 1, result);

        // 7) descent to touchdown, flight model, 0.05 s chunks.
        DynD5Place(step, earth, 1d, 0d, 0d);
        for (int i = 0; i < 200 && step.Regime == VesselRegime.Flying; i++)
        {
            step.StepFlying(0.05f, true);
        }

        DynD5Step(step, earth, "touchdown 0.05 s", 0f, 0, result);
        result.Landed = step.Regime == VesselRegime.Landed;
        result.FinalShip = step.Ship;
        return result;
    }

    private static void DynD5Step(
        VesselStep step, OrbitingBody earth, string name, float chunk, int calls, DynD5Result result)
    {
        for (int i = 0; i < calls; i++)
        {
            step.StepFlying(chunk, true);
        }

        earth.EvaluateWorldState(step.TimeSeconds, out Vector3d bodyPosition, out _);
        double altitude = (step.Ship.Position - bodyPosition).Magnitude - earth.Radius;
        result.Phases.Add(new DynD5Phase
        {
            Name = name,
            Integrator = step.LastSelectedIntegrator,
            Reason = step.LastSelectionReason,
            AltitudeM = altitude
        });
    }

    private static void DynD5Place(
        VesselStep step, OrbitingBody earth, double altitude, double verticalSpeed, double horizontalSpeed)
    {
        earth.EvaluateWorldState(step.TimeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
        Vector3d radial = new Vector3d(0d, 1d, 0d);
        Vector3d east = new Vector3d(0d, 0d, 1d);
        step.DominantBody = earth;
        step.Ship = new Ship(
            bodyPosition + radial * (earth.Radius + altitude),
            bodyVelocity + radial * verticalSpeed + east * horizontalSpeed,
            1000d);
    }

    private static double DynCircularSpeed(OrbitingBody body, double altitude)
    {
        return Math.Sqrt(body.StandardGravitationalParameter / (body.Radius + altitude));
    }

    // ----------------------------------------------------------------------------------
    // Rig, orbits, measurements
    // ----------------------------------------------------------------------------------
    private sealed class DynRig
    {
        public VesselStep Step;
        public SpacecraftPhysics Physics;
    }

    private static DynRig DynBuildRig(
        StarSystem system, bool ported, bool sunCoast = false, bool thrust = false)
    {
        var physics = new SpacecraftPhysics { ToleranceFrameSystem = system };
        physics.Sources.Add(new CachedGravitySource(system));
        TestThrustSource thrustSource = null;
        if (thrust)
        {
            thrustSource = new TestThrustSource
            {
                ThrustNewtons = 100d,
                MassFlowKgS = 0d,
                Direction = new Vector3d(1d, 0d, 0d),
                ActiveAt = _ => true
            };
            physics.Sources.Add(thrustSource);
        }

        var warp = new WarpController();
        var propagator = new EventDrivenPropagator(physics)
        {
            HardHorizonSeconds = system.BakedEndSeconds()
        };
        var driver = new LongWarpDriver(physics, propagator);
        var step = new VesselStep(
            system, warp, propagator, driver, null,
            new JointedBreakup(), new DebrisPool(), new PartDefinition[0], 1d, 600d);
        step.Thrust = thrustSource;
        step.EnableRegimeIntegratorPolicy = sunCoast;
        step.EnableSunCoastPolicy = sunCoast;
        step.EnablePortedIntegratorPolicy = ported;
        return new DynRig { Step = step, Physics = physics };
    }

    private static void DynPlaceShip(
        VesselStep step, OrbitingBody body, double altitude, double inclinationDegrees, double e)
    {
        body.EvaluateWorldState(0d, out Vector3d bodyPosition, out Vector3d bodyVelocity);
        double radius = body.Radius + altitude;
        double speed = Math.Sqrt(body.StandardGravitationalParameter * (1d + e) / radius);
        double inclination = inclinationDegrees * Math.PI / 180d;
        step.DominantBody = body;
        step.Ship = new Ship(
            bodyPosition + new Vector3d(radius, 0d, 0d),
            bodyVelocity + new Vector3d(
                0d, speed * Math.Cos(inclination), speed * Math.Sin(inclination)),
            1000d);
    }

    private static void DynPlaceHeliocentric(VesselStep step, double au)
    {
        double radius = au * DynAu;
        double speed = Math.Sqrt(DynSunMu / radius);
        step.DominantBody = step.SystemState.Root;
        step.Ship = new Ship(
            new Vector3d(radius, 0d, 0d), new Vector3d(0d, speed, 0d), 1000d);
    }

    private static double DynRunEvent(DynRig rig, OrbitingBody earth, bool crossing)
    {
        DynPlaceShip(rig.Step, earth, 400_000d, 0d, crossing ? 0.01d : 0d);
        if (crossing)
        {
            rig.Step.Propagator.CrossingDetectors.Add(new AltitudeCrossingDetector(
                earth, 420_000d, EventDirection.Rising,
                EventPriorities.Planned, "Test565Crossing", EventKind.AtmosphereEntry));
        }
        else
        {
            rig.Step.Propagator.TimedEvents.Add(new TimedEvent
            {
                TimeSeconds = 1234.5d,
                DeltaMass = 0d,
                MinimumMassKg = 0d,
                DeltaVelocity = Vector3d.Zero
            });
        }

        double eventTime = double.NaN;
        rig.Step.OccurrenceHandled = occurrence =>
        {
            if (double.IsNaN(eventTime))
            {
                eventTime = occurrence.TimeSeconds;
            }
        };
        for (int i = 0; i < 20 && double.IsNaN(eventTime); i++)
        {
            rig.Step.StepFlying((float)DynChunk, true);
        }

        return eventTime;
    }

    private static long DynAllocOneChunk(
        StarSystem system, OrbitingBody earth, double altitude, double inclination, bool wh)
    {
        DynRig rig = DynBuildRig(system, ported: wh);
        DynPlaceShip(rig.Step, earth, altitude, inclination, 0d);
        rig.Step.StepFlying((float)DynChunk, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        rig.Step.StepFlying((float)DynChunk, true);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static double DynMedianChunkWall(
        StarSystem system, OrbitingBody earth, double altitude, double inclination, bool wh)
    {
        DynRig rig = DynBuildRig(system, ported: wh);
        DynPlaceShip(rig.Step, earth, altitude, inclination, 0d);
        rig.Step.StepFlying((float)DynChunk, true);
        var values = new double[5];
        for (int i = 0; i < values.Length; i++)
        {
            long before = System.Diagnostics.Stopwatch.GetTimestamp();
            rig.Step.StepFlying((float)DynChunk, true);
            long after = System.Diagnostics.Stopwatch.GetTimestamp();
            values[i] = (after - before) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        }

        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static double DynMedianSunWall(bool sunCoast, double chunk)
    {
        StarSystem sun = DynCreateSun();
        DynRig rig = DynBuildRig(sun, ported: false, sunCoast: sunCoast);
        DynPlaceHeliocentric(rig.Step, 1d);
        rig.Step.StepFlying((float)chunk, true);
        var values = new double[5];
        for (int i = 0; i < values.Length; i++)
        {
            long before = System.Diagnostics.Stopwatch.GetTimestamp();
            rig.Step.StepFlying((float)chunk, true);
            long after = System.Diagnostics.Stopwatch.GetTimestamp();
            values[i] = (after - before) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        }

        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static double DynRunHelioTimed(bool sunCoast, double chunk)
    {
        StarSystem sun = DynCreateSun();
        DynRig rig = DynBuildRig(sun, ported: false, sunCoast: sunCoast);
        DynPlaceHeliocentric(rig.Step, 1d);
        rig.Step.Propagator.TimedEvents.Add(new TimedEvent
        {
            TimeSeconds = 3600d,
            DeltaMass = 0d,
            MinimumMassKg = 0d,
            DeltaVelocity = Vector3d.Zero
        });
        double eventTime = double.NaN;
        rig.Step.OccurrenceHandled = occurrence =>
        {
            if (double.IsNaN(eventTime))
            {
                eventTime = occurrence.TimeSeconds;
            }
        };
        long calls = (long)Math.Ceiling(7200d / chunk);
        for (long i = 0; i < calls && double.IsNaN(eventTime); i++)
        {
            rig.Step.StepFlying((float)chunk, true);
        }

        return eventTime;
    }

    private static long DynRhsEstimate(bool sunCoast, double chunk)
    {
        StarSystem sun = DynCreateSun();
        DynRig rig = DynBuildRig(sun, ported: false, sunCoast: sunCoast);
        DynPlaceHeliocentric(rig.Step, 1d);
        long calls = (long)(DynDay / chunk);
        long attempts = 0L;
        for (long i = 0; i < calls; i++)
        {
            rig.Physics.StepDiagnostics.Reset();
            rig.Step.StepFlying((float)chunk, true);
            attempts += rig.Physics.StepDiagnostics.Attempts;
        }

        return attempts * 6L; // DOPRI5: 6 RHS per attempt (FSAL)
    }

    private static bool DynSameState(Ship a, Ship b)
    {
        return a.Position.X == b.Position.X && a.Position.Y == b.Position.Y && a.Position.Z == b.Position.Z
            && a.Velocity.X == b.Velocity.X && a.Velocity.Y == b.Velocity.Y && a.Velocity.Z == b.Velocity.Z
            && a.Mass == b.Mass;
    }

    private static StarSystem DynCreateSun()
    {
        var sun = new OrbitingBody
        {
            Name = "Sun",
            StandardGravitationalParameter = DynSunMu,
            Radius = 6.957e8d
        };
        return new StarSystem(sun);
    }

    private static StarSystem DynCreateSunEarthMoon(bool earthJ2)
    {
        var sun = new OrbitingBody
        {
            Name = "Sun",
            StandardGravitationalParameter = DynSunMu,
            Radius = 6.957e8d
        };
        var earth = new OrbitingBody
        {
            Name = "Earth",
            StandardGravitationalParameter = DynEarthMu,
            Radius = DynEarthRadius,
            SemiMajorAxis = DynAu,
            Eccentricity = 0d,
            InclinationDegrees = 0d,
            Parent = sun,
            EpochTimeSeconds = 0d
        };
        if (earthJ2)
        {
            earth.J2 = 1.08262668e-3d;
            earth.J2ReferenceRadius = DynEarthRadius;
        }

        var moon = new OrbitingBody
        {
            Name = "Moon",
            StandardGravitationalParameter = DynMoonMu,
            Radius = 1.7374e6d,
            SemiMajorAxis = 3.844e8d,
            Eccentricity = 0.0549d,
            InclinationDegrees = 5.145d,
            Parent = earth,
            EpochTimeSeconds = 0d
        };
        sun.Children.Add(earth);
        earth.Children.Add(moon);
        sun.SyncMassFromGravitationalParameter();
        earth.SyncMassFromGravitationalParameter();
        moon.SyncMassFromGravitationalParameter();
        return new StarSystem(sun);
    }

    private static string FormatAltitude(double altitude)
    {
        return (altitude / 1000d).ToString("F0", CultureInfo.InvariantCulture) + " km";
    }

    // ----------------------------------------------------------------------------------
    // CSV + report (report is generated by reading the CSV, not from the rows in memory)
    // ----------------------------------------------------------------------------------
    private sealed class DynRow
    {
        public string Branch;
        public string Scenario;
        public string Metric;
        public double AltitudeM = double.NaN;
        public double Eccentricity = double.NaN;
        public double Value = double.NaN;
        public string Unit;
        public double Reference = double.NaN;
        public string Verdict;
        public string Note;

        public static DynRow Number(
            string branch, string scenario, double altitude, double eccentricity, string metric,
            double value, string unit, double reference, string verdict, string note)
        {
            return new DynRow
            {
                Branch = branch,
                Scenario = scenario,
                AltitudeM = altitude,
                Eccentricity = eccentricity,
                Metric = metric,
                Value = value,
                Unit = unit,
                Reference = reference,
                Verdict = verdict,
                Note = note
            };
        }
    }

    private static string DynResultsFolder()
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string hash = TryRunGit("rev-parse --short HEAD").Trim();
        if (hash.Length == 0)
        {
            hash = "nohash";
        }

        return Path.Combine(FindHarnessRoot(), "Study", "dynamic_policy",
            "results", stamp + "_" + hash);
    }

    private static void DynWriteCsv(string path, List<DynRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("branch,scenario,altitude_m,eccentricity,metric,value,unit,reference,verdict,note");
        foreach (DynRow r in rows)
        {
            sb.Append(r.Branch).Append(',')
                .Append(DynField(r.Scenario)).Append(',')
                .Append(DynNum(r.AltitudeM)).Append(',')
                .Append(DynNum(r.Eccentricity)).Append(',')
                .Append(r.Metric).Append(',')
                .Append(DynNum(r.Value)).Append(',')
                .Append(r.Unit).Append(',')
                .Append(DynNum(r.Reference)).Append(',')
                .Append(r.Verdict).Append(',')
                .Append(DynField(r.Note)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void DynWriteReport(string csvPath, string reportPath)
    {
        List<string[]> rows = DynReadCsv(csvPath);
        var sb = new StringBuilder();
        sb.AppendLine("# DYNAMIC_REPORT: режим → интегратор → доказательство (D4)");
        sb.AppendLine();
        sb.AppendLine("Generated from `" + Path.GetFileName(csvPath) + "`; no hand edits.");
        sb.AppendLine("Branch R1 = WH-BM14A through VesselStep (flags on in the test rig only),");
        sb.AppendLine("R1_out = must stay DOP853, R3 = Sun-coast report only (flag false in the game).");
        sb.AppendLine();
        sb.AppendLine("Reference for R1 error: the certified default path (DOP853, flags off) on the");
        sb.AppendLine("same VesselStep scenario; gate is the §6 absolute target 1 m. The stand's");
        sb.AppendLine("certified reference is a different study-frame model and is not used here.");
        sb.AppendLine();
        sb.AppendLine("| branch | scenario | metric | value | unit | reference | verdict | note |");
        sb.AppendLine("|---|---|---|---:|---|---:|---|---|");
        foreach (string[] r in rows)
        {
            sb.Append("| ").Append(r[0]).Append(" | ").Append(r[1]).Append(" | ").Append(r[4])
                .Append(" | ").Append(r[5]).Append(" | ").Append(r[6]).Append(" | ").Append(r[7])
                .Append(" | ").Append(r[8]).Append(" | ").Append(r[9]).AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## Gate summary");
        sb.AppendLine();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string[] r in rows)
        {
            string verdict = r.Length > 8 ? r[8] : "";
            counts.TryGetValue(verdict, out int n);
            counts[verdict] = n + 1;
        }

        foreach (var pair in counts)
        {
            sb.AppendLine("* " + pair.Key + ": " + pair.Value);
        }

        File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static List<string[]> DynReadCsv(string path)
    {
        var list = new List<string[]>();
        string[] lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            list.Add(lines[i].Split(','));
        }

        return list;
    }

    private static string DynNum(double value)
    {
        return double.IsNaN(value)
            ? ""
            : value.ToString("E16", CultureInfo.InvariantCulture);
    }

    private static string DynField(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.IndexOf(',') >= 0 || text.IndexOf('"') >= 0
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }
}
