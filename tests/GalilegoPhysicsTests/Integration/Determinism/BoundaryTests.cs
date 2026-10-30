using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Determinism
{
    [Trait("Tier", "Fast")]
    public sealed class BoundaryTests
    {
        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void ZeroIntervalReturnsTheInitialStateExactly(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-circular");
            IntegratorKind integrator = Parse(integratorName);
            SpacecraftIntegrationState result = Step(integrator, scenario.InitialState, 0d, 0d, scenario, 1e-9d);
            Asserts.True(result.Position.X == scenario.InitialState.Position.X &&
                         result.Position.Y == scenario.InitialState.Position.Y &&
                         result.Position.Z == scenario.InitialState.Position.Z &&
                         result.Velocity.X == scenario.InitialState.Velocity.X &&
                         result.Velocity.Y == scenario.InitialState.Velocity.Y &&
                         result.Velocity.Z == scenario.InitialState.Velocity.Z &&
                         result.Mass == scenario.InitialState.Mass,
                integratorName + ": zero interval changed the state.");
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void NegativeDirectionThrows(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-circular");
            IntegratorKind integrator = Parse(integratorName);
            Asserts.Throws<ArgumentOutOfRangeException>(
                () => Step(integrator, scenario.InitialState, 0d, -60d, scenario, 1e-9d),
                integratorName + ": negative targetDt");
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void NonFiniteInitialStateThrows(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-circular");
            IntegratorKind integrator = Parse(integratorName);
            SpacecraftIntegrationState nan = scenario.InitialState;
            nan.Position = new Vector3d(double.NaN, 0d, 0d);
            Asserts.Throws<InvalidOperationException>(
                () => Step(integrator, nan, 0d, 60d, scenario, 1e-9d),
                integratorName + ": NaN initial state");

            SpacecraftIntegrationState infinite = scenario.InitialState;
            infinite.Velocity = new Vector3d(0d, double.PositiveInfinity, 0d);
            Asserts.Throws<InvalidOperationException>(
                () => Step(integrator, infinite, 0d, 60d, scenario, 1e-9d),
                integratorName + ": Inf initial state");
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void VeryLooseAndVeryTightTolerancesStayFinite(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-circular");
            IntegratorKind integrator = Parse(integratorName);

            SpacecraftIntegrationState loose = Step(integrator, scenario.InitialState, 0d,
                scenario.DurationSeconds, scenario, 1e2d);
            Asserts.Finite(loose.Position, integratorName + " loose position");
            Asserts.Finite(loose.Velocity, integratorName + " loose velocity");

            SpacecraftIntegrationState tight = Step(integrator, scenario.InitialState, 0d,
                scenario.DurationSeconds, scenario, 1e-13d);
            Asserts.Finite(tight.Position, integratorName + " tight position");
            Asserts.Finite(tight.Velocity, integratorName + " tight velocity");
        }

        [Fact]
        public void LegacyOrbitIntegratorIsForwardOnlyAndZeroIsIdentity()
        {
#pragma warning disable 0618
            IntegrationResult zero = OrbitIntegrator.StepForward(
                new Vector3d(1e7, 0d, 0d), new Vector3d(0d, 6000d, 0d), 0d, 0d,
                (position, time) => Vector3d.Zero);
            Asserts.VectorNear(zero.Position, new Vector3d(1e7, 0d, 0d), 0d, "zero interval position");
            Asserts.VectorNear(zero.Velocity, new Vector3d(0d, 6000d, 0d), 0d, "zero interval velocity");

            Asserts.Throws<ArgumentOutOfRangeException>(
                () => OrbitIntegrator.StepForward(
                    Vector3d.Zero, Vector3d.Zero, 0d, -1d,
                    (position, time) => Vector3d.Zero),
                "negative targetDt");
#pragma warning restore 0618
        }

        private static SpacecraftIntegrationState Step(
            IntegratorKind integrator,
            SpacecraftIntegrationState initial,
            double time,
            double dt,
            Scenario scenario,
            double tolerance)
        {
            SpacecraftOrbitIntegrator.DerivativeFunction derivative = (state, t) => scenario.Derivative(state, t);
            if (integrator == IntegratorKind.Dopri5)
            {
                return SpacecraftOrbitIntegrator.StepForward(
                    initial, time, dt, derivative, tolerance, tolerance, tolerance, tolerance).State;
            }

            return SpacecraftOrbitIntegrator.StepForwardDop853(
                initial, time, dt, derivative, tolerance, tolerance, tolerance, tolerance).State;
        }

        private static IntegratorKind Parse(string name)
        {
            return name == "Dop853" ? IntegratorKind.Dop853 : IntegratorKind.Dopri5;
        }
    }
}
