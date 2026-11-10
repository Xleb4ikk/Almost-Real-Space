using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Live-chunk checks for the v1 policy: the 600 s decision matrix is a
    /// stress test, while the game runs 1/60-30 s chunks. These tests compare
    /// the policy against pure DOP853 at the chunks the game actually uses.
    /// </summary>
    public sealed class LiveChunkPolicyTests
    {
        [Theory]
        [InlineData(1d / 60d, 10d)]
        [InlineData(30d, 86_400d)]
        public void SunCoastPolicyIsCheaperAndMatchesDop853(double chunk, double duration)
        {
            StarSystem system = CreateStarPlanetSystem();
            OrbitingBody star = system.Root;
            star.EvaluateWorldState(0d, out Vector3d sp, out Vector3d sv);
            double radius = 1.5e11d;
            double circular = Math.Sqrt(star.StandardGravitationalParameter / radius);
            Vector3d position = sp + new Vector3d(0d, radius, 0d);
            Vector3d velocity = sv + new Vector3d(0d, 0d, circular);

            Run baseline = RunCoast(system, position, velocity, false, chunk, duration);
            Run policy = RunCoast(system, position, velocity, true, chunk, duration);

            Assert.True(policy.SelectedDopri5, "policy did not select DOPRI5 on a Sun coast");
            Assert.True((policy.Position - baseline.Position).Magnitude < 1e-2d,
                "policy vs DOP853 final position difference " +
                (policy.Position - baseline.Position).Magnitude.ToString("E3") + " m");
            Assert.True(policy.Calls < baseline.Calls,
                "policy cost " + policy.Calls + " >= DOP853 cost " + baseline.Calls);
        }

        [Fact]
        public void SurfaceControlGrainPolicyIsCheaperAndMatchesDop853()
        {
            var planet = new OrbitingBody
            {
                Name = "Planet",
                StandardGravitationalParameter = TestScene.Mu,
                Radius = 6.371e6d
            };
            planet.SyncMassFromGravitationalParameter();
            var system = new StarSystem(planet);

            Vector3d position = new Vector3d(planet.Radius + 500d, 0d, 0d);
            Vector3d velocity = new Vector3d(0d, 10d, 0d);
            var thrust = new ConstantAccelerationSource(new Vector3d(20d, 0d, 0d));

            Run baseline = RunSurface(system, position, velocity, thrust, false, 0.05d, 10d);
            Run policy = RunSurface(system, position, velocity, thrust, true, 0.05d, 10d);

            Assert.True(policy.SelectedDopri5, "policy did not select DOPRI5 on the surface grain");
            Assert.True((policy.Position - baseline.Position).Magnitude < 1e-6d,
                "policy vs DOP853 final position difference " +
                (policy.Position - baseline.Position).Magnitude.ToString("E3") + " m");
            Assert.True(policy.Calls < baseline.Calls,
                "policy cost " + policy.Calls + " >= DOP853 cost " + baseline.Calls);
        }

        [Fact]
        public void SwitchingDopri5Dop853AtChunkBoundariesDoesNotAddAStateJump()
        {
            StarSystem system = CreateStarPlanetSystem();
            OrbitingBody star = system.Root;
            star.EvaluateWorldState(0d, out Vector3d sp, out Vector3d sv);
            double radius = 1.5e11d;
            double circular = Math.Sqrt(star.StandardGravitationalParameter / radius);
            Vector3d position = sp + new Vector3d(0d, radius, 0d);
            Vector3d velocity = sv + new Vector3d(0d, 0d, circular);

            const double chunk = 0.05d;
            const int total = 200;   // 10 s
            const int switchToDopri5 = 60;
            const int switchBackToDop853 = 120;

            var physics = new SpacecraftPhysics
            {
                ToleranceFrameSystem = system,
                RelativeTolerance = 1e-11d
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);

            double time = 0d;
            for (int i = 0; i < total; i++)
            {
                physics.Integrator = i >= switchToDopri5 && i < switchBackToDop853
                    ? SpacecraftIntegrator.Dopri5
                    : SpacecraftIntegrator.Dop853;
                physics.Step(ship, time, chunk);
                time += chunk;
                Assert.True(ship.HasLocalState, "local frame lost at chunk " + i);
                Assert.Equal(0d, (ship.LocalAbsolutePosition - ship.Position).Magnitude);
            }

            Run baseline = RunCoast(system, position, velocity, false, chunk, total * chunk);
            Assert.True((ship.Position - baseline.Position).Magnitude < 1e-2d,
                "seam vs pure DOP853 final position difference " +
                (ship.Position - baseline.Position).Magnitude.ToString("E3") + " m");
        }

        private static Run RunCoast(
            StarSystem system, Vector3d position, Vector3d velocity,
            bool usePolicy, double chunk, double duration)
        {
            var physics = new SpacecraftPhysics
            {
                ToleranceFrameSystem = system,
                RelativeTolerance = 1e-11d
            };
            var counting = new CountingSource(new GravitySource(system));
            physics.Sources.Add(counting);
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);

            bool selected = false;
            double time = 0d;
            while (time < duration - 1e-12d)
            {
                double dt = Math.Min(chunk, duration - time);
                if (usePolicy)
                {
                    OrbitingBody body = system.FindDominantBody(ship.Position, time);
                    system.EvaluateBodyState(body, time, out Vector3d bp, out Vector3d bv);
                    Vector3d relative = ship.Position - bp;
                    double altitude = relative.Magnitude - body.Radius;
                    double vertical = relative.Magnitude > 0d
                        ? Vector3d.Dot(ship.Velocity - bv, relative.Normalized)
                        : 0d;
                    var context = new IntegratorContext(
                        dt, false, ReferenceEquals(body, system.Root), true, true, altitude, vertical);
                    SpacecraftIntegrator selectedIntegrator = IntegratorPolicy.Select(context);
                    selected |= selectedIntegrator == SpacecraftIntegrator.Dopri5;
                    physics.Integrator = selectedIntegrator;
                }
                else
                {
                    physics.Integrator = SpacecraftIntegrator.Dop853;
                }

                physics.Step(ship, time, dt);
                time += dt;
            }

            return new Run(ship.Position, ship.Velocity, counting.Calls, selected);
        }

        private static Run RunSurface(
            StarSystem system, Vector3d position, Vector3d velocity,
            ConstantAccelerationSource thrust, bool usePolicy, double chunk, double duration)
        {
            var physics = new SpacecraftPhysics
            {
                ToleranceFrameSystem = system,
                RelativeTolerance = 1e-11d
            };
            var counting = new CountingSource(new GravitySource(system));
            physics.Sources.Add(counting);
            physics.Sources.Add(thrust);
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);

            bool selected = false;
            double time = 0d;
            while (time < duration - 1e-12d)
            {
                double dt = Math.Min(chunk, duration - time);
                if (usePolicy)
                {
                    OrbitingBody body = system.FindDominantBody(ship.Position, time);
                    system.EvaluateBodyState(body, time, out Vector3d bp, out Vector3d bv);
                    Vector3d relative = ship.Position - bp;
                    double altitude = relative.Magnitude - body.Radius;
                    double vertical = relative.Magnitude > 0d
                        ? Vector3d.Dot(ship.Velocity - bv, relative.Normalized)
                        : 0d;
                    var context = new IntegratorContext(
                        dt, true, ReferenceEquals(body, system.Root), false, false, altitude, vertical);
                    SpacecraftIntegrator selectedIntegrator = IntegratorPolicy.Select(context);
                    selected |= selectedIntegrator == SpacecraftIntegrator.Dopri5;
                    physics.Integrator = selectedIntegrator;
                }
                else
                {
                    physics.Integrator = SpacecraftIntegrator.Dop853;
                }

                physics.Step(ship, time, dt);
                time += dt;
            }

            return new Run(ship.Position, ship.Velocity, counting.Calls, selected);
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
            return new StarSystem(star);
        }

        private readonly struct Run
        {
            public readonly Vector3d Position;
            public readonly Vector3d Velocity;
            public readonly long Calls;
            public readonly bool SelectedDopri5;

            public Run(Vector3d position, Vector3d velocity, long calls, bool selectedDopri5)
            {
                Position = position;
                Velocity = velocity;
                Calls = calls;
                SelectedDopri5 = selectedDopri5;
            }
        }

        private sealed class CountingSource : IDynamicsSource
        {
            private readonly IDynamicsSource inner;

            public CountingSource(IDynamicsSource inner)
            {
                this.inner = inner;
            }

            public long Calls { get; private set; }

            public DynamicsContribution Evaluate(
                Vector3d position, Vector3d velocity, double mass, double timeSeconds)
            {
                Calls++;
                return inner.Evaluate(position, velocity, mass, timeSeconds);
            }
        }

        private sealed class ConstantAccelerationSource : IDynamicsSource
        {
            private readonly Vector3d acceleration;

            public ConstantAccelerationSource(Vector3d acceleration)
            {
                this.acceleration = acceleration;
            }

            public DynamicsContribution Evaluate(
                Vector3d position, Vector3d velocity, double mass, double timeSeconds)
            {
                return DynamicsContribution.FromSpecificAcceleration(acceleration);
            }
        }
    }
}
