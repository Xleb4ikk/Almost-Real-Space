using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// D2/T4: StepDiagnostics.Fallbacks must stay at zero on every scenario inside
    /// the Ported regime (local frame) and become non-zero only outside it — the
    /// absolute path without a tolerance frame is the documented fallback.
    /// </summary>
    public sealed class DynamicPolicyFallbackTests
    {
        private readonly ITestOutputHelper output;

        public DynamicPolicyFallbackTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData(0d, false)]
        [InlineData(90d, false)]
        [InlineData(98d, true)]
        [InlineData(0d, true)]
        public void InRegimeLocalFrameHasNoFallbacks(double inclinationDegrees, bool j2)
        {
            StarSystem system = CreateEarth(j2);
            OrbitingBody earth = system.Root;
            double radius = earth.Radius + 400_000d;
            double speed = Math.Sqrt(TestScene.Mu / radius);
            double inclination = inclinationDegrees * Math.PI / 180d;
            var position = new Vector3d(radius, 0d, 0d);
            var velocity = new Vector3d(
                0d, speed * Math.Cos(inclination), speed * Math.Sin(inclination));

            var physics = new SpacecraftPhysics
            {
                Integrator = SpacecraftIntegrator.WhBm14a,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, 1000d);
            for (int i = 0; i < 144; i++)
            {
                physics.Step(ship, i * 600d, 600d);
            }

            output.WriteLine("T4 in-regime i=" + inclinationDegrees + " j2=" + j2
                + ": fallbacks=" + physics.StepDiagnostics.Fallbacks);
            Assert.Equal(0L, physics.StepDiagnostics.Fallbacks);
        }

        [Fact]
        public void AbsolutePathOutsideTheRegimeFallsBack()
        {
            StarSystem system = CreateEarth(false);
            OrbitingBody earth = system.Root;
            double radius = earth.Radius + 400_000d;
            double speed = Math.Sqrt(TestScene.Mu / radius);

            var physics = new SpacecraftPhysics
            {
                Integrator = SpacecraftIntegrator.WhBm14a
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(new Vector3d(radius, 0d, 0d), new Vector3d(0d, speed, 0d), 1000d);
            physics.Step(ship, 0d, 600d);

            output.WriteLine("T4 absolute path: fallbacks=" + physics.StepDiagnostics.Fallbacks);
            Assert.True(physics.StepDiagnostics.Fallbacks > 0L,
                "the absolute path must report a fallback");
        }

        [Fact]
        public void OutOfRegimeSelectionNeverReachesWh()
        {
            var context = new IntegratorContext(
                600d, false, false, false, true, 400_000d, 0d,
                TestScene.Mu, 0.2d, true);
            SpacecraftIntegrator selected = IntegratorPolicy.Select(context);
            Assert.Equal(SpacecraftIntegrator.Dop853, selected);

            StarSystem system = CreateEarth(false);
            OrbitingBody earth = system.Root;
            double periapsis = earth.Radius + 400_000d;
            double apoapsis = earth.Radius + 40_000_000d;
            TestScene.EllipticalOrbit(periapsis, apoapsis, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);
            var physics = new SpacecraftPhysics
            {
                Integrator = selected,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, 1000d);
            for (int i = 0; i < 24; i++)
            {
                physics.Step(ship, i * 600d, 600d);
            }

            output.WriteLine("T4 out-of-regime selection " + selected
                + ": fallbacks=" + physics.StepDiagnostics.Fallbacks);
            Assert.Equal(0L, physics.StepDiagnostics.Fallbacks);
        }

        private static StarSystem CreateEarth(bool j2)
        {
            var earth = new OrbitingBody
            {
                Name = "Earth",
                StandardGravitationalParameter = TestScene.Mu,
                Radius = 6.371e6d
            };
            if (j2)
            {
                earth.J2 = 1.08262668e-3d;
                earth.J2ReferenceRadius = earth.Radius;
            }

            return new StarSystem(earth);
        }
    }
}
