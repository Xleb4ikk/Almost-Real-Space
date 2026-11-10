using System;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Direct DOPRI5 event-path check. The production policy keeps descent
    /// below 2 km on DOP853, but the event test must still prove that DOPRI5
    /// finds the same touchdown root before the guard can be removed or the
    /// regime expanded.
    /// </summary>
    public sealed class TouchdownEventIntegratorTests
    {
        [Fact]
        public void Dopri5TouchdownEventMatchesDop853()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            system.Root.Radius = 6.371e6d;

            EventOccurrence dop853 = PropagateEvent(system, SpacecraftIntegrator.Dop853);
            EventOccurrence dopri5 = PropagateEvent(system, SpacecraftIntegrator.Dopri5);

            Assert.True(dop853.TimeSeconds > 0d, "DOP853 touchdown time is not positive");
            Assert.True(dopri5.TimeSeconds > 0d, "DOPRI5 touchdown time is not positive");
            Assert.True(Math.Abs(dopri5.TimeSeconds - dop853.TimeSeconds) < 1e-3d,
                "DOPRI5 event time " + dopri5.TimeSeconds.ToString("R") +
                " s differs from DOP853 " + dop853.TimeSeconds.ToString("R") + " s by more than 1 ms");
        }

        [Fact]
        public void Dopri5SoiEntryEventMatchesDop853()
        {
            StarSystem system = CreateStarPlanetSystem();
            OrbitingBody star = system.Root;
            OrbitingBody planet = star.Children[0];

            EventOccurrence dop853 = PropagateSoi(system, SpacecraftIntegrator.Dop853);
            EventOccurrence dopri5 = PropagateSoi(system, SpacecraftIntegrator.Dopri5);

            Assert.True(dop853.TimeSeconds > 0d, "DOP853 SOI time is not positive");
            Assert.True(dopri5.TimeSeconds > 0d, "DOPRI5 SOI time is not positive");
            Assert.True(Math.Abs(dopri5.TimeSeconds - dop853.TimeSeconds) < 1e-3d,
                "DOPRI5 SOI time " + dopri5.TimeSeconds.ToString("R") +
                " s differs from DOP853 " + dop853.TimeSeconds.ToString("R") + " s by more than 1 ms");
        }

        private static EventOccurrence PropagateSoi(
            StarSystem system, SpacecraftIntegrator integrator)
        {
            var physics = new SpacecraftPhysics
            {
                ToleranceFrameSystem = system,
                Integrator = integrator
            };
            physics.Sources.Add(new GravitySource(system));

            var propagator = new EventDrivenPropagator(physics)
            {
                MaxChunkSeconds = 600d,
                DetectionSubdivisions = 20,
                UseDenseRefinement = true
            };
            propagator.TransitionDetectors.Add(new SoiChangeDetector(system));

            OrbitingBody planet = system.Root.Children[0];
            planet.EvaluateWorldState(0d, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d radial = new Vector3d(1d, 0d, 0d);
            var ship = new Spacecraft(
                bodyPosition + radial * (planet.SphereOfInfluenceRadius * 1.1d),
                bodyVelocity - radial * 1000d,
                TestScene.ShipMass);
            EventOccurrence? hit = propagator.Propagate(ship, 0d, 200_000d);
            Assert.True(hit.HasValue, "SOI event was not found");
            return hit.Value;
        }

        private static StarSystem CreateStarPlanetSystem()
        {
            var star = new OrbitingBody
            {
                Name = "Star",
                StandardGravitationalParameter = 1.327e20d
            };
            star.SyncMassFromGravitationalParameter();
            var planet = new OrbitingBody
            {
                Name = "Planet",
                StandardGravitationalParameter = TestScene.Mu,
                Radius = 6.371e6d,
                SemiMajorAxis = 1.5e11d,
                Eccentricity = 0d,
                Parent = star,
                EpochTimeSeconds = 0d
            };
            planet.SyncMassFromGravitationalParameter();
            star.Children.Add(planet);
            planet.UpdateInfluenceRadii();
            return new StarSystem(star);
        }

        private static EventOccurrence PropagateEvent(
            StarSystem system, SpacecraftIntegrator integrator)
        {
            var physics = new SpacecraftPhysics
            {
                ToleranceFrameSystem = system,
                Integrator = integrator
            };
            physics.Sources.Add(new GravitySource(system));

            var propagator = new EventDrivenPropagator(physics)
            {
                MaxChunkSeconds = 600d,
                DetectionSubdivisions = 20,
                UseDenseRefinement = true
            };
            propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(system.Root));

            var ship = new Spacecraft(
                new Vector3d(system.Root.Radius + 10_000d, 0d, 0d),
                new Vector3d(-100d, 0d, 0d),
                TestScene.ShipMass);
            EventOccurrence? hit = propagator.Propagate(ship, 0d, 1000d);
            Assert.True(hit.HasValue, "touchdown event was not found");
            return hit.Value;
        }
    }
}
