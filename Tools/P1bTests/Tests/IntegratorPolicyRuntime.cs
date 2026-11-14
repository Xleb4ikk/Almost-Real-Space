using System;
using System.Diagnostics;
using System.Reflection;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Flight;
using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

// Test561: the per-chunk integrator policy is wired into VesselStep.
// Pure-boundary cases live in tests/GalilegoSimTests/IntegratorPolicyTests.cs;
// here the real VesselStep path is exercised at live chunks.
internal static partial class P1bTests
{
    private static int Test561_IntegratorPolicy()
    {
        Console.WriteLine("--- Test561: per-chunk integrator policy (VesselStep) ---");

        StarSystem sys = TestSystem();
        OrbitingBody star = sys.Root;
        OrbitingBody planet = sys.AllBodies[1];

        // 1) Sun-dominated coast, x1 chunk 1/60 s: DOPRI5.
        (VesselStep sunStep, TestThrustSource sunThrust) = BuildPolicyRig(sys);
        sunStep.EnableRegimeIntegratorPolicy = true;
        sunStep.EnableSunCoastPolicy = true;
        star.EvaluateWorldState(0d, out Vector3d starP, out Vector3d starV);
        sunStep.DominantBody = star;
        sunStep.Ship = new Ship(
            starP + new Vector3d(0d, 1.5e11d, 0d),
            starV + new Vector3d(29_780d, 0d, 0d),
            1000d);
        sunStep.StepFlying(1f / 60f, true);
        Check(sunStep.LastSelectedIntegrator == SpacecraftIntegrator.Dopri5,
            "T561 Sun coast x1 -> DOPRI5", sunStep.LastSelectedIntegrator.ToString());

        // 2) Same coast at a 30 s live-warp chunk: still DOPRI5.
        sunStep.StepFlying(30f, true);
        Check(sunStep.LastSelectedIntegrator == SpacecraftIntegrator.Dopri5,
            "T561 Sun coast 30 s -> DOPRI5", sunStep.LastSelectedIntegrator.ToString());

        // 3) Earth-dominated coast: the same 30 s chunk must stay on DOP853.
        (VesselStep earthStep, _) = BuildPolicyRig(sys);
        earthStep.EnableRegimeIntegratorPolicy = true;
        planet.EvaluateWorldState(0d, out Vector3d planetP, out Vector3d planetV);
        Vector3d radial = new Vector3d(0d, 1d, 0d);
        Vector3d east = new Vector3d(0d, 0d, 1d);
        double orbitRadius = planet.Radius + 200_000d;
        double circular = Math.Sqrt(planet.StandardGravitationalParameter / orbitRadius);
        earthStep.DominantBody = planet;
        earthStep.Ship = new Ship(
            planetP + radial * orbitRadius,
            planetV + east * circular,
            1000d);
        earthStep.StepFlying(30f, true);
        Check(earthStep.LastSelectedIntegrator == SpacecraftIntegrator.Dop853,
            "T561 Earth coast 30 s -> DOP853", earthStep.LastSelectedIntegrator.ToString());

        // 3b) Earth/surface policy-on must not add a CanEnterLongWarp source scan.
        (VesselStep earthOn, CountingSource earthOnCalls) = BuildCoastRig(sys, true);
        (VesselStep earthOff, CountingSource earthOffCalls) = BuildCoastRig(sys, false);
        ConfigureEarthCoast(earthOn, planet);
        ConfigureEarthCoast(earthOff, planet);
        earthOn.StepFlying(1f / 60f, true);
        earthOff.StepFlying(1f / 60f, true);
        Check(earthOnCalls.Calls == earthOffCalls.Calls,
            "T561 Earth policy on adds no source evaluations",
            earthOnCalls.Calls.ToString() + " == " + earthOffCalls.Calls.ToString());

        // 3c) Sun-dominant but near another body (5 AU/cislunar-like): DOP853.
        (VesselStep guardStep, _) = BuildPolicyRig(sys);
        guardStep.EnableRegimeIntegratorPolicy = true;
        guardStep.EnableSunCoastPolicy = true;
        planet.EvaluateWorldState(0d, out Vector3d guardBodyP, out Vector3d guardBodyV);
        Vector3d guardRadial = new Vector3d(1d, 0d, 0d);
        guardStep.DominantBody = star;
        guardStep.Ship = new Ship(
            guardBodyP + guardRadial * (5d * planet.SphereOfInfluenceRadius),
            guardBodyV + new Vector3d(0d, 0d, 100d),
            1000d);
        guardStep.StepFlying(1f / 60f, true);
        Check(guardStep.LastSelectedIntegrator == SpacecraftIntegrator.Dop853,
            "T561 near-planet Sun coast -> DOP853", guardStep.LastSelectedIntegrator.ToString());

        // 4) Active thrust forbids the Sun-coast branch even at a short chunk.
        sunThrust.ThrustNewtons = 1000d;
        sunThrust.ActiveAt = _ => true;
        sunStep.RawThrottle = 1d;
        sunStep.StepFlying(1f / 60f, true);
        Check(sunStep.LastSelectedIntegrator == SpacecraftIntegrator.Dop853,
            "T561 Sun coast + thrust -> DOP853", sunStep.LastSelectedIntegrator.ToString());

        // 5) Policy disabled: no selection, historical DOP853.
        (VesselStep offStep, _) = BuildPolicyRig(sys);
        offStep.EnableRegimeIntegratorPolicy = false;
        offStep.EnableSunCoastPolicy = false;
        offStep.DominantBody = star;
        Vector3d offStartP = starP + new Vector3d(0d, 1.5e11d, 0d);
        Vector3d offStartV = starV + new Vector3d(29_780d, 0d, 0d);
        offStep.Ship = new Ship(offStartP, offStartV, 1000d);
        const float oneFrame = 1f / 60f;
        offStep.StepFlying(oneFrame, true);
        Check(offStep.LastSelectedIntegrator == SpacecraftIntegrator.Dop853,
            "T561 policy off -> DOP853", offStep.LastSelectedIntegrator.ToString());

        // 5b) Policy off is the same one-chunk state as a direct production Step.
        // Use the same float->double frame value on both paths: StepFlying takes a
        // float realDt, so 1d/60d would not be the same dt.
        var directPhysics = new SpacecraftPhysics { ToleranceFrameSystem = sys };
        directPhysics.Sources.Add(new CachedGravitySource(sys));
        var directShip = new Ship(offStartP, offStartV, 1000d);
        directPhysics.Step(directShip, 0d, (double)oneFrame);
        bool sameState = offStep.Ship.Position.X == directShip.Position.X
            && offStep.Ship.Position.Y == directShip.Position.Y
            && offStep.Ship.Position.Z == directShip.Position.Z
            && offStep.Ship.Velocity.X == directShip.Velocity.X
            && offStep.Ship.Velocity.Y == directShip.Velocity.Y
            && offStep.Ship.Velocity.Z == directShip.Velocity.Z
            && offStep.Ship.Mass == directShip.Mass;
        string stateDetail = "dp=" + (offStep.Ship.Position - directShip.Position).Magnitude.ToString("E3")
            + " dv=" + (offStep.Ship.Velocity - directShip.Velocity).Magnitude.ToString("E3")
            + " dm=" + Math.Abs(offStep.Ship.Mass - directShip.Mass).ToString("E3");
        Check(sameState, "T561 policy off full-state == direct Step", stateDetail);

        // 6) Surface branch through the real VesselStep + Flight model.
        (VesselStep surfaceOn, CountingSource surfaceOnCalls) = BuildSurfaceRig(sys, planet, true);
        (VesselStep surfaceOff, CountingSource surfaceOffCalls) = BuildSurfaceRig(sys, planet, false);
        surfaceOn.StepFlying(1f, true);
        surfaceOff.StepFlying(1f, true);
        Check(surfaceOn.LastSelectedIntegrator == SpacecraftIntegrator.Dopri5,
            "T561 surface Flight x1 -> DOPRI5", surfaceOn.LastSelectedIntegrator.ToString());
        Check(surfaceOnCalls.Calls < surfaceOffCalls.Calls,
            "T561 surface Flight cost < DOP853",
            surfaceOnCalls.Calls.ToString() + " < " + surfaceOffCalls.Calls.ToString());
        Check((surfaceOn.Ship.Position - surfaceOff.Ship.Position).Magnitude < 1e-3d,
            "T561 surface Flight state agrees with DOP853",
            (surfaceOn.Ship.Position - surfaceOff.Ship.Position).Magnitude.ToString("E3") + " m");

        // 7) Ported WH-BM14A (Appendix C): narrow 600 s long-warp coast, flag on.
        (VesselStep whStep, _) = BuildPolicyRig(sys);
        whStep.EnableRegimeIntegratorPolicy = false;
        whStep.EnableSunCoastPolicy = false;
        whStep.EnablePortedIntegratorPolicy = true;
        ConfigureEarthCoast(whStep, planet);
        whStep.StepFlying(600f, true);
        Check(whStep.LastSelectedIntegrator == SpacecraftIntegrator.WhBm14a,
            "T561 ported 600 s LEO coast -> WH-BM14A",
            whStep.LastSelectedIntegrator.ToString());
        Check(whStep.Ship.Position.IsFinite && whStep.Ship.Velocity.IsFinite,
            "T561 ported WH-BM14A state is finite",
            whStep.Ship.Position.ToString());

        // 7b) Live 1/60 chunk: the ported branch must stay on DOP853.
        (VesselStep whLive, _) = BuildPolicyRig(sys);
        whLive.EnablePortedIntegratorPolicy = true;
        ConfigureEarthCoast(whLive, planet);
        whLive.StepFlying(1f / 60f, true);
        Check(whLive.LastSelectedIntegrator == SpacecraftIntegrator.Dop853,
            "T561 ported live 1/60 -> DOP853", whLive.LastSelectedIntegrator.ToString());

        // 7c) Eccentric orbit: not the ported regime.
        (VesselStep whEcc, _) = BuildPolicyRig(sys);
        whEcc.EnablePortedIntegratorPolicy = true;
        planet.EvaluateWorldState(0d, out Vector3d eccP, out Vector3d eccV);
        Vector3d eccRadial = new Vector3d(0d, 1d, 0d);
        Vector3d eccEast = new Vector3d(0d, 0d, 1d);
        double eccRadius = planet.Radius + 400_000d;
        double eccSpeed = Math.Sqrt(planet.StandardGravitationalParameter * 1.2d / eccRadius);
        whEcc.DominantBody = planet;
        whEcc.Ship = new Ship(eccP + eccRadial * eccRadius, eccV + eccEast * eccSpeed, 1000d);
        whEcc.StepFlying(600f, true);
        Check(whEcc.LastSelectedIntegrator == SpacecraftIntegrator.Dop853,
            "T561 ported eccentric 600 s -> DOP853", whEcc.LastSelectedIntegrator.ToString());

        // 7d) Non-zero thrust: CanCoast false, the ported branch must not be selected.
        (VesselStep whThrust, TestThrustSource whThrustSource) = BuildPolicyRig(sys);
        whThrust.EnablePortedIntegratorPolicy = true;
        whThrustSource.ThrustNewtons = 100d;
        whThrustSource.ActiveAt = _ => true;
        whThrust.RawThrottle = 1d;
        ConfigureEarthCoast(whThrust, planet);
        whThrust.StepFlying(600f, true);
        Check(whThrust.LastSelectedIntegrator != SpacecraftIntegrator.WhBm14a,
            "T561 ported with thrust -> not WH-BM14A", whThrust.LastSelectedIntegrator.ToString());

        // 8) D2/T2: with every flag off, the VesselStep path must be bitwise the
        // direct SpacecraftPhysics.Step at the live/600 s/flight chunks.
        Test561PolicyOffParity(sys, planet);

        // 9) D2/T5: crossing and transition events through VesselStep under WH-BM14A.
        Test561WhCrossingAndTransition(sys, planet);

        // 10) D2/T6: policy overhead (allocations, call time, 600 s wall vs Prod-DOP853).
        Test561PolicyOverhead(sys, planet);

        Console.WriteLine("done: " + (failures == 0 ? "ALL PASS" : failures + " FAILURE(S)"));
        return 0;
    }

    private static (VesselStep Step, TestThrustSource Thrust) BuildPolicyRig(StarSystem sys)
    {
        var warp = new WarpController();
        var physics = new SpacecraftPhysics { ToleranceFrameSystem = sys };
        physics.Sources.Add(new CachedGravitySource(sys));
        var thrust = new TestThrustSource
        {
            ThrustNewtons = 0d,
            MassFlowKgS = 0d,
            Direction = new Vector3d(1d, 0d, 0d),
            ActiveAt = _ => false
        };
        physics.Sources.Add(thrust);
        var propagator = new EventDrivenPropagator(physics)
        {
            HardHorizonSeconds = sys.BakedEndSeconds()
        };
        var driver = new LongWarpDriver(physics, propagator);
        var step = new VesselStep(
            sys, warp, propagator, driver, null,
            new JointedBreakup(), new DebrisPool(), new PartDefinition[0], 1d, 600d);
        step.Thrust = thrust;
        return (step, thrust);
    }

    private static (VesselStep Step, CountingSource Calls) BuildSurfaceRig(
        StarSystem sys, OrbitingBody planet, bool policy)
    {
        var warp = new WarpController();
        var physics = new SpacecraftPhysics { ToleranceFrameSystem = sys };
        var counting = new CountingSource(new CachedGravitySource(sys));
        physics.Sources.Add(counting);

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

        var propagator = new EventDrivenPropagator(physics)
        {
            HardHorizonSeconds = sys.BakedEndSeconds()
        };
        var driver = new LongWarpDriver(physics, propagator);
        var step = new VesselStep(
            sys, warp, propagator, driver, null,
            new JointedBreakup(), new DebrisPool(), new PartDefinition[0], 1d, 600d);
        step.Flight = flight;
        step.FlightSource = flightSource;
        step.EnableRegimeIntegratorPolicy = policy;
        step.EnableSunCoastPolicy = false;
        step.DominantBody = planet;

        planet.EvaluateWorldState(0d, out Vector3d bodyPosition, out Vector3d bodyVelocity);
        Vector3d radial = new Vector3d(1d, 0d, 0d);
        step.Ship = new Ship(
            bodyPosition + radial * (planet.Radius + 5_000d),
            bodyVelocity + new Vector3d(0d, 0d, 100d),
            1000d);
        return (step, counting);
    }

    private static (VesselStep Step, CountingSource Calls) BuildCoastRig(
        StarSystem sys, bool policy)
    {
        var warp = new WarpController();
        var physics = new SpacecraftPhysics { ToleranceFrameSystem = sys };
        var counting = new CountingSource(new CachedGravitySource(sys));
        physics.Sources.Add(counting);
        var propagator = new EventDrivenPropagator(physics)
        {
            HardHorizonSeconds = sys.BakedEndSeconds()
        };
        var driver = new LongWarpDriver(physics, propagator);
        var step = new VesselStep(
            sys, warp, propagator, driver, null,
            new JointedBreakup(), new DebrisPool(), new PartDefinition[0], 1d, 600d);
        step.EnableRegimeIntegratorPolicy = policy;
        step.EnableSunCoastPolicy = false;
        return (step, counting);
    }

    private static void ConfigureEarthCoast(VesselStep step, OrbitingBody planet)
    {
        planet.EvaluateWorldState(0d, out Vector3d planetP, out Vector3d planetV);
        Vector3d radial = new Vector3d(0d, 1d, 0d);
        Vector3d east = new Vector3d(0d, 0d, 1d);
        double orbitRadius = planet.Radius + 200_000d;
        double circular = Math.Sqrt(planet.StandardGravitationalParameter / orbitRadius);
        step.DominantBody = planet;
        step.Ship = new Ship(
            planetP + radial * orbitRadius,
            planetV + east * circular,
            1000d);
    }

    // ---------------------------------------------------------------------------------
    // D2 additions (T2, T5, T6). No production code is changed: these checks only
    // exercise the already-built policy through VesselStep.
    // ---------------------------------------------------------------------------------

    private static void Test561PolicyOffParity(StarSystem sys, OrbitingBody planet)
    {
        CheckPolicyOffMatchesDirect(sys, planet, 1d / 60d, false, "live 1/60 s", true);
        CheckPolicyOffMatchesDirect(sys, planet, 30d, false, "live 30 s", true);
        CheckPolicyOffMatchesDirect(sys, planet, 600d, false, "long-warp 600 s", false);
        CheckPolicyOffMatchesDirect(sys, planet, 0.05d, true, "flight 0.05 s", false);
        CheckLongWarpDeterministic(sys, planet);
    }

    /// <summary>
    /// T2: policy off must reproduce the direct Step path bit for bit. The
    /// propagator integrates a call in chunks of MaxChunkSeconds/DetectionSubdivisions
    /// (VesselStep 600 s / 20 = 30 s), so the direct reference repeats the same
    /// subdivision instead of one big step; anything else would compare different
    /// chunking, not the policy.
    /// </summary>
    private static void CheckPolicyOffMatchesDirect(
        StarSystem sys, OrbitingBody planet, double dt, bool flight, string label, bool requireBitwise)
    {
        VesselStep step;
        if (flight)
        {
            (step, _) = BuildSurfaceRig(sys, planet, false);
        }
        else
        {
            (step, _) = BuildPolicyRig(sys);
            ConfigureEarthCoast(step, planet);
            step.EnableRegimeIntegratorPolicy = false;
            step.EnableSunCoastPolicy = false;
            step.EnablePortedIntegratorPolicy = false;
        }

        var directShip = new Ship(step.Ship.Position, step.Ship.Velocity, step.Ship.Mass);
        var physics = new SpacecraftPhysics { ToleranceFrameSystem = sys };
        physics.Sources.Add(new CachedGravitySource(sys));

        float realDt = (float)dt;
        step.StepFlying(realDt, true);
        double elapsed = step.TimeSeconds;
        double grain = step.Propagator.MaxChunkSeconds / step.Propagator.DetectionSubdivisions;
        double sub = Math.Min(grain, elapsed);
        double time = 0d;
        while (time < elapsed - 1e-12d)
        {
            double subDt = Math.Min(sub, elapsed - time);
            physics.Step(directShip, time, subDt);
            time += subDt;
        }

        double dp = (step.Ship.Position - directShip.Position).Magnitude;
        double dv = (step.Ship.Velocity - directShip.Velocity).Magnitude;
        double dm = Math.Abs(step.Ship.Mass - directShip.Mass);
        bool same = step.Ship.Position.X == directShip.Position.X
            && step.Ship.Position.Y == directShip.Position.Y
            && step.Ship.Position.Z == directShip.Position.Z
            && step.Ship.Velocity.X == directShip.Velocity.X
            && step.Ship.Velocity.Y == directShip.Velocity.Y
            && step.Ship.Velocity.Z == directShip.Velocity.Z
            && step.Ship.Mass == directShip.Mass;
        string detail = "dp=" + dp.ToString("E3") + " dv=" + dv.ToString("E3")
            + " dm=" + dm.ToString("E3") + " elapsed=" + elapsed.ToString("R");
        if (requireBitwise)
        {
            Check(same, "T561 policy off bitwise == direct Step, " + label, detail);
        }
        else
        {
            // TASK_D.md D2: a difference is a measurement, not a red test. The
            // number goes to PROGRESS.md; D2 changes no production code.
            Measured("T561 policy off vs direct Step, " + label, detail);
        }
    }

    private static void CheckLongWarpDeterministic(StarSystem sys, OrbitingBody planet)
    {
        (VesselStep first, _) = BuildPolicyRig(sys);
        first.EnablePortedIntegratorPolicy = false;
        ConfigureEarthCoast(first, planet);
        first.StepFlying(600f, true);

        (VesselStep second, _) = BuildPolicyRig(sys);
        second.EnablePortedIntegratorPolicy = false;
        ConfigureEarthCoast(second, planet);
        second.StepFlying(600f, true);

        bool same = first.Ship.Position.X == second.Ship.Position.X
            && first.Ship.Position.Y == second.Ship.Position.Y
            && first.Ship.Position.Z == second.Ship.Position.Z
            && first.Ship.Velocity.X == second.Ship.Velocity.X
            && first.Ship.Velocity.Y == second.Ship.Velocity.Y
            && first.Ship.Velocity.Z == second.Ship.Velocity.Z;
        Check(same, "T561 long-warp 600 s repeated runs bitwise equal",
            "dp=" + (first.Ship.Position - second.Ship.Position).Magnitude.ToString("E3"));
    }

    private static void Measured(string name, string detail)
    {
        Console.WriteLine("MEASURED " + name + " | " + detail);
    }

    private static void Test561WhCrossingAndTransition(StarSystem sys, OrbitingBody planet)
    {
        string trace;
        double whCross = RunPolicyEvent(sys, planet, true, true, out SpacecraftIntegrator crossIntegrator, out trace);
        double dopCross = RunPolicyEvent(sys, planet, false, true, out _, out string dopTrace);
        Check(crossIntegrator == SpacecraftIntegrator.WhBm14a,
            "T561 WH crossing event uses WH-BM14A", crossIntegrator + " [" + trace + "]");
        Check(Math.Abs(whCross - dopCross) <= 1e-3d,
            "T561 WH crossing event time vs DOP853 <= 1 ms",
            "WH=" + whCross.ToString("R") + " s, DOP853=" + dopCross.ToString("R")
            + " s, d=" + Math.Abs(whCross - dopCross).ToString("E3") + " s");

        double whTransition = RunPolicyEvent(sys, planet, true, false, out SpacecraftIntegrator transitionIntegrator, out trace);
        double dopTransition = RunPolicyEvent(sys, planet, false, false, out _, out string transitionDopTrace);
        Check(transitionIntegrator == SpacecraftIntegrator.WhBm14a,
            "T561 WH transition event uses WH-BM14A", transitionIntegrator + " [" + trace + "]");
        Check(Math.Abs(whTransition - dopTransition) <= 1e-3d,
            "T561 WH transition event time vs DOP853 <= 1 ms",
            "WH=" + whTransition.ToString("R") + " s, DOP853=" + dopTransition.ToString("R")
            + " s, d=" + Math.Abs(whTransition - dopTransition).ToString("E3") + " s [" + transitionDopTrace + "]");
    }

    /// <summary>
    /// T5 probe. Crossing = a real AltitudeCrossingDetector shell inside the Ported
    /// band. Transition = the ITransitionDetector path with a time-keyed test probe:
    /// a real SoiChangeDetector cannot fire inside a 200-2000 km planetocentric coast
    /// (the SOI boundary is far outside the Ported band), so the probe checks the
    /// transition/root-refinement path under WH without inventing physics.
    /// </summary>
    private static double RunPolicyEvent(
        StarSystem sys, OrbitingBody planet, bool useWh, bool crossing,
        out SpacecraftIntegrator selected, out string trace)
    {
        (VesselStep step, _) = BuildPolicyRig(sys);
        step.EnableRegimeIntegratorPolicy = false;
        step.EnableSunCoastPolicy = false;
        step.EnablePortedIntegratorPolicy = useWh;

        planet.EvaluateWorldState(0d, out Vector3d bodyP, out Vector3d bodyV);
        // The event must fall inside the FIRST 600 s call: only then is the long-warp
        // policy still in force (after it, the warp factor drops and the live 0.5 s
        // chunk correctly selects DOP853). Crossing: start at apogee (400 km + 2ae)
        // and fall through a 500 km shell (~90 s). Transition: probe at 300 s.
        double periapsisAltitude = 400_000d;
        double eccentricity = crossing ? 0.01d : 0d;
        double semiMajorAxis = planet.Radius + periapsisAltitude;
        double apoapsisRadius = planet.Radius + periapsisAltitude + 2d * eccentricity * semiMajorAxis;
        double radius = crossing ? apoapsisRadius : semiMajorAxis;
        double speed = crossing
            ? Math.Sqrt(planet.StandardGravitationalParameter * (1d - eccentricity) / radius)
            : Math.Sqrt(planet.StandardGravitationalParameter / radius);
        step.DominantBody = planet;
        step.Ship = new Ship(
            bodyP + new Vector3d(0d, radius, 0d),
            bodyV + new Vector3d(0d, 0d, speed),
            1000d);

        if (crossing)
        {
            step.Propagator.CrossingDetectors.Add(new AltitudeCrossingDetector(
                planet, 534_000d, EventDirection.Falling,
                EventPriorities.Planned, "T561TestCrossing", EventKind.AtmosphereEntry));
        }
        else
        {
            step.Propagator.TransitionDetectors.Add(new T561TimeTransitionDetector(300d));
        }

        double eventTime = double.NaN;
        SpacecraftIntegrator integratorAtEvent = SpacecraftIntegrator.Dop853;
        step.OccurrenceHandled = occurrence =>
        {
            if (double.IsNaN(eventTime))
            {
                eventTime = occurrence.TimeSeconds;
                // The event is produced by the Propagate call of the current chunk:
                // LastSelectedIntegrator at this instant is the integrator that
                // integrated the event. After it, VesselStep re-selects for the
                // remaining part of the frame (a shorter chunk -> DOP853), so the
                // post-call value is not the event's integrator.
                integratorAtEvent = step.LastSelectedIntegrator;
            }
        };

        var traceBuilder = new System.Text.StringBuilder();
        for (int i = 0; i < 20 && double.IsNaN(eventTime); i++)
        {
            step.StepFlying(600f, true);
            traceBuilder.Append(step.TimeSeconds.ToString("F1"))
                .Append(':').Append(step.LastSelectedIntegrator).Append(' ');
        }

        trace = traceBuilder.ToString().Trim();
        selected = integratorAtEvent;
        return eventTime;
    }

    private static void Test561PolicyOverhead(StarSystem sys, OrbitingBody planet)
    {
        MethodInfo apply = typeof(VesselStep).GetMethod(
            "ApplyIntegratorPolicy", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(double), typeof(bool) }, null);
        if (apply == null)
        {
            Check(false, "T561 policy overhead", "ApplyIntegratorPolicy not found");
            return;
        }

        (VesselStep policyStep, _) = BuildPolicyRig(sys);
        policyStep.EnableRegimeIntegratorPolicy = true;
        policyStep.EnableSunCoastPolicy = false;
        policyStep.EnablePortedIntegratorPolicy = true;
        ConfigureEarthCoast(policyStep, planet);

        var applyDelegate = (Action<double, bool>)apply.CreateDelegate(
            typeof(Action<double, bool>), policyStep);

        for (int i = 0; i < 200; i++)
        {
            applyDelegate(600d, false);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            applyDelegate(600d, false);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0L, "T561 ApplyIntegratorPolicy allocations == 0",
            allocated + " B / 1000 calls");

        const int timingCalls = 1000;
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < timingCalls; i++)
        {
            applyDelegate(600d, false);
        }

        timer.Stop();
        double microseconds = timer.Elapsed.TotalMilliseconds * 1000d / timingCalls;
        Console.WriteLine("T561 ApplyIntegratorPolicy: " + microseconds.ToString("F3")
            + " us/call (single sequential pass, " + timingCalls + " calls)");

        // §6 wall row: policy + 600 s WH chunk vs the same chunk on DOP853.
        // The game's propagator subdivides a 600 s warp into
        // MaxChunkSeconds/DetectionSubdivisions = 30 s chunks (SystemForGrain is
        // not set by SimulationRunner), so this is a VesselStep-path measurement,
        // not the stand's single-600 s-step figure.
        double whMs = MedianChunkWall(sys, planet, true);
        double dopMs = MedianChunkWall(sys, planet, false);
        double ratio = whMs / dopMs;
        Console.WriteLine("T561 600 s chunk wall (VesselStep, 30 s grain): WH="
            + whMs.ToString("F4") + " ms, DOP853=" + dopMs.ToString("F4")
            + " ms, ratio=" + ratio.ToString("F3"));

        long allocOn = AllocatedForOneChunk(sys, planet, true);
        long allocOff = AllocatedForOneChunk(sys, planet, false);
        Check(allocOn <= allocOff, "T561 policy on allocations <= policy off",
            "on=" + allocOn + " B, off=" + allocOff + " B");
    }

    private static double MedianChunkWall(StarSystem sys, OrbitingBody planet, bool useWh)
    {
        const int samples = 5;
        (VesselStep step, _) = BuildPolicyRig(sys);
        step.EnableRegimeIntegratorPolicy = false;
        step.EnablePortedIntegratorPolicy = useWh;
        ConfigureEarthCoast(step, planet);
        step.StepFlying(600f, true); // warm-up: lazy caches, JIT, first-step effects
        var values = new double[samples];
        for (int rep = 0; rep < samples; rep++)
        {
            var timer = Stopwatch.StartNew();
            step.StepFlying(600f, true);
            timer.Stop();
            values[rep] = timer.Elapsed.TotalMilliseconds;
        }

        Array.Sort(values);
        return values[samples / 2];
    }

    private static long AllocatedForOneChunk(StarSystem sys, OrbitingBody planet, bool useWh)
    {
        (VesselStep step, _) = BuildPolicyRig(sys);
        step.EnableRegimeIntegratorPolicy = false;
        step.EnablePortedIntegratorPolicy = useWh;
        ConfigureEarthCoast(step, planet);
        step.StepFlying(600f, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        step.StepFlying(600f, true);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class T561TimeTransitionDetector : ITransitionDetector
    {
        private readonly double switchTimeSeconds;

        public T561TimeTransitionDetector(double switchTimeSeconds)
        {
            this.switchTimeSeconds = switchTimeSeconds;
        }

        public int Priority => EventPriorities.SoiChange;

        public string Name => "T561TimeTransition";

        public EventKind Kind => EventKind.SoiChange;

        public object GetKey(SpacecraftIntegrationState state, double timeSeconds)
        {
            return timeSeconds >= switchTimeSeconds ? "after" : "before";
        }
    }
}