using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Ship
{
    [Trait("Tier", "Fast")]
    public sealed class SpacecraftIntegratorTests
    {
        private readonly ITestOutputHelper output;

        public SpacecraftIntegratorTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void ConstantThrustMatchesTsiolkovsky(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("thrust-tsiolkovsky");
            IntegratorKind integrator = Parse(integratorName);
            ToleranceProfile table = ToleranceTable.For(integrator, scenario.Class, 1e-9d);

            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-9d, 1);
            run.MatchesOracle(table.OraclePositionMeters, table.OracleVelocityMetersPerSecond)
               .MatchesMass(table.OracleMassRelative);

            output.WriteLine(integratorName + ": steps=" + run.Steps +
                             ", pos err=" + run.MaxPositionError.ToString("E2") +
                             ", mass err=" + run.MaxMassError.ToString("E2") + ".");
        }

        [Fact]
        public void TightMassToleranceReallyDrivesTheStepSize()
        {
            int looseCalls = RunOscillatingMassFlow(1e3d, out double looseMass);
            int tightCalls = RunOscillatingMassFlow(1e-9d, out double tightMass);
            double expectedMass = 1000d - (0.1d * 3600d);
            double tightRelativeError = Math.Abs(tightMass - expectedMass) / expectedMass;

            output.WriteLine("RHS calls: loose=" + looseCalls + ", tight=" + tightCalls +
                             "; mass loose=" + looseMass.ToString("R") +
                             ", tight=" + tightMass.ToString("R") +
                             ", analytic=" + expectedMass.ToString("R") + ".");

            Asserts.True(tightCalls > 3 * looseCalls,
                "MassAbsoluteTolerance does not subdivide steps: " + tightCalls + " vs " + looseCalls + ".");
            Asserts.True(tightRelativeError < 1e-6,
                "tight mass run did not converge to m0 - k*T: " + tightRelativeError.ToString("E3") + ".");
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void StepToTimeEqualsStepForward(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-e05");
            IntegratorKind integrator = Parse(integratorName);
            SpacecraftIntegrationState initial = scenario.InitialState;
            long calls = 0L;
            SpacecraftOrbitIntegrator.DerivativeFunction derivative = (state, time) =>
            {
                calls++;
                return scenario.Derivative(state, time);
            };

            SpacecraftIntegrationState forward = StepForward(integrator, initial, 0d, scenario.DurationSeconds, derivative, 1e-10d);
            SpacecraftIntegrationState toTime = StepToTime(integrator, initial, 0d, scenario.DurationSeconds, derivative, 1e-10d);
            Asserts.True(forward.Position.X == toTime.Position.X &&
                         forward.Position.Y == toTime.Position.Y &&
                         forward.Position.Z == toTime.Position.Z &&
                         forward.Velocity.X == toTime.Velocity.X &&
                         forward.Velocity.Y == toTime.Velocity.Y &&
                         forward.Velocity.Z == toTime.Velocity.Z,
                integratorName + ": StepToTime and StepForward differ.");
            Asserts.True(calls > 0, "RHS was never called.");
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void DenseCollectorDoesNotChangeTheFinalState(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-e05");
            IntegratorKind integrator = Parse(integratorName);
            var withSegments = new System.Collections.Generic.List<DenseSegment>();
            SpacecraftOrbitIntegrator.DerivativeFunction derivative = (state, time) => scenario.Derivative(state, time);
            SpacecraftIntegrationState without = StepForward(integrator, scenario.InitialState, 0d, scenario.DurationSeconds,
                derivative, 1e-10d, null);
            SpacecraftIntegrationState with = StepForward(integrator, scenario.InitialState, 0d, scenario.DurationSeconds,
                derivative, 1e-10d, withSegments);

            Asserts.True(without.Position.X == with.Position.X &&
                         without.Position.Y == with.Position.Y &&
                         without.Position.Z == with.Position.Z &&
                         without.Velocity.X == with.Velocity.X &&
                         without.Velocity.Y == with.Velocity.Y &&
                         without.Velocity.Z == with.Velocity.Z &&
                         without.Mass == with.Mass,
                integratorName + ": dense collector changed the accepted state.");
            Asserts.True(withSegments.Count > 0, "dense collector produced no segments.");
        }

        [Fact]
        public void ToleranceReferenceUsesTheLocalScale()
        {
            const double offset = 1.0e11d;
            const double radius = 7.0e6d;
            const double mu = 3.986004418e14d;
            var bodyPosition = new Vector3d(offset, 0d, 0d);
            var initial = new SpacecraftIntegrationState
            {
                Position = bodyPosition + new Vector3d(radius, 0d, 0d),
                Velocity = new Vector3d(0d, Math.Sqrt(mu / radius), 0d),
                Mass = 1000d
            };
            SpacecraftOrbitIntegrator.DerivativeFunction derivative = (state, time) => new SpacecraftIntegrationState
            {
                Position = state.Velocity,
                Velocity = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    state.Position, bodyPosition, mu),
                Mass = 0d
            };

            double period = 2d * Math.PI * Math.Sqrt(radius * radius * radius / mu);
            var plainSegments = new System.Collections.Generic.List<DenseSegment>();
            var referenceSegments = new System.Collections.Generic.List<DenseSegment>();
            var reference = new ToleranceReference(bodyPosition, Vector3d.Zero, 0d);

            SpacecraftIntegrationState plain = SpacecraftOrbitIntegrator.StepForward(
                initial, 0d, period, derivative, 1e-9d, 1e-9d, 1e-9d, 1e-9d, plainSegments, null).State;
            SpacecraftIntegrationState withReference = SpacecraftOrbitIntegrator.StepForward(
                initial, 0d, period, derivative, 1e-9d, 1e-9d, 1e-9d, 1e-9d, referenceSegments, reference).State;

            double plainError = (plain.Position - initial.Position).Magnitude;
            double referenceError = (withReference.Position - initial.Position).Magnitude;
            output.WriteLine("steps plain=" + plainSegments.Count + " (" + plainError.ToString("E2") +
                             " m), reference=" + referenceSegments.Count + " (" + referenceError.ToString("E2") + " m).");

            Asserts.True(referenceSegments.Count > plainSegments.Count,
                "ToleranceReference did not tighten the local step scale.");
            Asserts.True(referenceError < plainError,
                "ToleranceReference did not improve the local error.");

            Asserts.VectorNear(reference.PositionAt(10d), bodyPosition, 0d, "PositionAt");
            Asserts.VectorNear(reference.PositionAt(-5d), bodyPosition, 0d, "PositionAt negative");
            Asserts.Near(ToleranceReference.MaxSpanSeconds, 7200d, 0d, "MaxSpanSeconds");
        }

        [Fact]
        public void NonPositiveMassThrowsLoudly()
        {
            var initial = new SpacecraftIntegrationState
            {
                Position = Vector3d.Zero,
                Velocity = Vector3d.Zero,
                Mass = 10d
            };
            SpacecraftOrbitIntegrator.DerivativeFunction derivative = (state, time) => new SpacecraftIntegrationState
            {
                Position = state.Velocity,
                Velocity = Vector3d.Zero,
                Mass = -2d
            };

            Asserts.Throws<InvalidOperationException>(
                () => SpacecraftOrbitIntegrator.StepForward(initial, 0d, 10d, derivative, 1e-6d, 1e-6d, 1e-6d, 1e-9d),
                "mass exhaustion");
        }

        private static int RunOscillatingMassFlow(double massTolerance, out double finalMass)
        {
            var initial = new SpacecraftIntegrationState
            {
                Position = Vector3d.Zero,
                Velocity = Vector3d.Zero,
                Mass = 1000d
            };
            int calls = 0;
            SpacecraftOrbitIntegrator.DerivativeFunction derivative = (state, time) =>
            {
                calls++;
                double flow = 0.1d * (1d + (0.9d * Math.Sin(2d * Math.PI * time / 50d)));
                return new SpacecraftIntegrationState
                {
                    Position = state.Velocity,
                    Velocity = Vector3d.Zero,
                    Mass = -flow
                };
            };

            SpacecraftIntegrationState result = SpacecraftOrbitIntegrator.StepForward(
                initial, 0d, 3600d, derivative, 1e-2d, 1e-5d, massTolerance, 1e-10d).State;
            finalMass = result.Mass;
            return calls;
        }

        private static SpacecraftIntegrationState StepForward(
            IntegratorKind integrator,
            SpacecraftIntegrationState initial,
            double currentTime,
            double dt,
            SpacecraftOrbitIntegrator.DerivativeFunction derivative,
            double tolerance,
            System.Collections.Generic.IList<DenseSegment> segments = null)
        {
            if (integrator == IntegratorKind.Dopri5)
            {
                return SpacecraftOrbitIntegrator.StepForward(
                    initial, currentTime, dt, derivative, tolerance, tolerance, tolerance, tolerance, segments, null).State;
            }

            return SpacecraftOrbitIntegrator.StepForwardDop853(
                initial, currentTime, dt, derivative, tolerance, tolerance, tolerance, tolerance, segments, null).State;
        }

        private static SpacecraftIntegrationState StepToTime(
            IntegratorKind integrator,
            SpacecraftIntegrationState initial,
            double currentTime,
            double targetTime,
            SpacecraftOrbitIntegrator.DerivativeFunction derivative,
            double tolerance)
        {
            if (integrator == IntegratorKind.Dopri5)
            {
                return SpacecraftOrbitIntegrator.StepToTime(
                    initial, currentTime, targetTime, derivative, tolerance, tolerance, tolerance, tolerance).State;
            }

            return SpacecraftOrbitIntegrator.StepToTimeDop853(
                initial, currentTime, targetTime, derivative, tolerance, tolerance, tolerance, tolerance).State;
        }

        private static IntegratorKind Parse(string name)
        {
            return name == "Dop853" ? IntegratorKind.Dop853 : IntegratorKind.Dopri5;
        }
    }
}
