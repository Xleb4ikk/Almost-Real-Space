using System;
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
}
