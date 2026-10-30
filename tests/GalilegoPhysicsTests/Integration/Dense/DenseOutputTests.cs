using System;
using System.Collections.Generic;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Oracles;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Dense
{
    public sealed class DenseOutputTests
    {
        private readonly ITestOutputHelper output;

        public DenseOutputTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void SegmentEndpointsAreBitExact(string integratorName)
        {
            ScenarioExecution run = Execute("kepler-e05", integratorName);
            Asserts.True(run.Segments.Count > 0, "no dense segments");
            foreach (DenseSegment segment in run.Segments)
            {
                AssertStateEqual(segment.Evaluate(segment.T0), segment.Y0, "T0");
                AssertStateEqual(segment.Evaluate(segment.T1), segment.Y1, "T1");
                AssertStateEqual(segment.Evaluate(segment.T0 - 1d), segment.Y0, "T0-1");
                AssertStateEqual(segment.Evaluate(segment.T1 + 1d), segment.Y1, "T1+1");
            }
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void AdjacentSegmentsJoinExactly(string integratorName)
        {
            ScenarioExecution run = Execute("kepler-e05", integratorName);
            for (int i = 1; i < run.Segments.Count; i++)
            {
                DenseSegment previous = run.Segments[i - 1];
                DenseSegment next = run.Segments[i];
                Asserts.Near(previous.T1, next.T0, 0d, "segment boundary time");
                AssertStateEqual(previous.Y1, next.Y0, "join state");
                AssertStateEqual(DenseSegment.EvaluateTrack(run.Segments, next.T0), next.Y0, "track at join");
            }
        }

        [Theory]
        [InlineData("kepler-circular", "Dopri5")]
        [InlineData("kepler-circular", "Dop853")]
        [InlineData("kepler-e05", "Dopri5")]
        [InlineData("kepler-e05", "Dop853")]
        [InlineData("kepler-e09", "Dopri5")]
        [InlineData("kepler-e09", "Dop853")]
        public void InterpolationMatchesTheOracleInsideSegments(string scenarioId, string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            IntegratorKind integrator = Parse(integratorName);
            ScenarioExecution run = Execute(scenarioId, integratorName);
            ToleranceProfile table = ToleranceTable.For(integrator, scenario.Class, 1e-9d);

            double worst = 0d;
            foreach (DenseSegment segment in run.Segments)
            {
                for (int i = 1; i <= 3; i++)
                {
                    double theta = i / 4d;
                    double time = segment.T0 + ((segment.T1 - segment.T0) * theta);
                    SpacecraftIntegrationState dense = segment.Evaluate(time);
                    OracleState oracle = scenario.Oracle(time);
                    double error = (dense.Position - oracle.Position.ToVector3d()).Magnitude;
                    worst = Math.Max(worst, error);
                }
            }

            output.WriteLine(scenarioId + "/" + integratorName + ": dense interpolation error = " +
                             worst.ToString("E3") + " m (tolerance " + table.DenseInterpolationMeters.ToString("E3") + ").");
            Asserts.True(worst <= table.DenseInterpolationMeters,
                "dense interpolation error " + worst.ToString("E3") + " m exceeds " + table.DenseInterpolationMeters.ToString("E3"));
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void DenseMidpointMatchesATightReferenceStep(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-e05");
            IntegratorKind integrator = Parse(integratorName);
            ScenarioExecution run = Execute("kepler-e05", integratorName);
            SpacecraftOrbitIntegrator.DerivativeFunction derivative = (state, time) => scenario.Derivative(state, time);

            double worst = 0d;
            int checkedSegments = 0;
            foreach (DenseSegment segment in run.Segments)
            {
                if (checkedSegments++ >= 10)
                {
                    break;
                }

                double half = 0.5d * (segment.T1 - segment.T0);
                SpacecraftIntegrationState reference = integrator == IntegratorKind.Dopri5
                    ? SpacecraftOrbitIntegrator.StepForward(
                        segment.Y0, segment.T0, half, derivative, 1e-14d, 1e-14d, 1e-14d, 1e-14d).State
                    : SpacecraftOrbitIntegrator.StepForwardDop853(
                        segment.Y0, segment.T0, half, derivative, 1e-14d, 1e-14d, 1e-14d, 1e-14d).State;
                SpacecraftIntegrationState dense = segment.Evaluate(segment.T0 + half);
                worst = Math.Max(worst, (dense.Position - reference.Position).Magnitude);
            }

            output.WriteLine(integratorName + ": dense midpoint vs tight reference = " + worst.ToString("E3") + " m.");
            // Measured baselines on this scenario: 1.8e-3 m (DOPRI5) and 5.4 m
            // (DOP853's cubic segment). The gates below are measured ×10.
            double tolerance = integrator == IntegratorKind.Dopri5 ? 1.8e-2d : 5.4e1d;
            Asserts.True(worst < tolerance, "dense midpoint mismatch " + worst.ToString("E3") + " m.");
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void DenseEndpointDerivativeMatchesTheRhs(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-e05");
            ScenarioExecution run = Execute("kepler-e05", integratorName);
            double worstPositionDerivative = 0d;
            double worstVelocityDerivative = 0d;
            int checkedSegments = 0;
            foreach (DenseSegment segment in run.Segments)
            {
                if (checkedSegments++ >= 20)
                {
                    break;
                }

                double h = segment.T1 - segment.T0;
                double delta = h * 1e-4d;
                SpacecraftIntegrationState start = segment.Evaluate(segment.T0);
                Vector3d startDerivative = (segment.Evaluate(segment.T0 + delta).Position * 4d -
                                           segment.Evaluate(segment.T0 + (2d * delta)).Position -
                                           (start.Position * 3d)) * (1d / (2d * delta));
                Vector3d startVelocityDerivative = (segment.Evaluate(segment.T0 + delta).Velocity * 4d -
                                                    segment.Evaluate(segment.T0 + (2d * delta)).Velocity -
                                                    (start.Velocity * 3d)) * (1d / (2d * delta));
                SpacecraftIntegrationState rhs = scenario.Derivative(start, segment.T0);
                worstPositionDerivative = Math.Max(worstPositionDerivative,
                    (startDerivative - rhs.Position).Magnitude / Math.Max(1d, rhs.Position.Magnitude));
                worstVelocityDerivative = Math.Max(worstVelocityDerivative,
                    (startVelocityDerivative - rhs.Velocity).Magnitude / Math.Max(1d, rhs.Velocity.Magnitude));

                SpacecraftIntegrationState end = segment.Evaluate(segment.T1);
                Vector3d endDerivative = ((end.Position * 3d) -
                                          (segment.Evaluate(segment.T1 - delta).Position * 4d) +
                                          segment.Evaluate(segment.T1 - (2d * delta)).Position) * (1d / (2d * delta));
                Vector3d endVelocityDerivative = ((end.Velocity * 3d) -
                                                  (segment.Evaluate(segment.T1 - delta).Velocity * 4d) +
                                                  segment.Evaluate(segment.T1 - (2d * delta)).Velocity) * (1d / (2d * delta));
                SpacecraftIntegrationState endRhs = scenario.Derivative(end, segment.T1);
                worstPositionDerivative = Math.Max(worstPositionDerivative,
                    (endDerivative - endRhs.Position).Magnitude / Math.Max(1d, endRhs.Position.Magnitude));
                worstVelocityDerivative = Math.Max(worstVelocityDerivative,
                    (endVelocityDerivative - endRhs.Velocity).Magnitude / Math.Max(1d, endRhs.Velocity.Magnitude));
            }

            output.WriteLine(integratorName + ": dense endpoint derivative error: position=" +
                             worstPositionDerivative.ToString("E3") + ", velocity=" +
                             worstVelocityDerivative.ToString("E3") + ".");
            Asserts.True(worstPositionDerivative < 1e-6,
                "dense position derivative mismatch " + worstPositionDerivative.ToString("E3"));
            Asserts.True(worstVelocityDerivative < 1e-6,
                "dense velocity derivative mismatch " + worstVelocityDerivative.ToString("E3"));
        }

        [Fact]
        public void EmptyTrackThrowsInsteadOfReturningNothing()
        {
            Asserts.Throws<InvalidOperationException>(
                () => DenseSegment.EvaluateTrack(Array.Empty<DenseSegment>(), 0d),
                "empty segment track");
        }

        [Fact]
        public void AsAbsoluteAddsTheMovingBodyStateAtTheQueryTime()
        {
            ScenarioExecution run = Execute("drift-linear", "Dop853");
            var body = new LinearBody(new Vector3d(1e9, -2e9, 3e8), new Vector3d(30d, -20d, 10d));

            double worst = 0d;
            foreach (DenseSegment local in run.Segments)
            {
                DenseSegment absolute = local.AsAbsolute(body);
                for (int i = 0; i <= 4; i++)
                {
                    double theta = i / 4d;
                    double time = local.T0 + ((local.T1 - local.T0) * theta);
                    SpacecraftIntegrationState localState = local.Evaluate(time);
                    SpacecraftIntegrationState absoluteState = absolute.Evaluate(time);
                    body.EvaluateWorldState(time, out Vector3d bodyPosition, out Vector3d bodyVelocity);
                    Vector3d expectedPosition = localState.Position + bodyPosition;
                    Vector3d expectedVelocity = localState.Velocity + bodyVelocity;
                    worst = Math.Max(worst, (absoluteState.Position - expectedPosition).Magnitude);
                    worst = Math.Max(worst, (absoluteState.Velocity - expectedVelocity).Magnitude);
                }
            }

            output.WriteLine("AsAbsolute max mismatch: " + worst.ToString("E3") + ".");
            Asserts.True(worst < 1e-9, "AsAbsolute mismatch " + worst.ToString("E3"));
        }

        private ScenarioExecution Execute(string scenarioId, string integratorName)
        {
            return Run.Scenario(scenarioId)
                      .With(integratorName == "Dop853" ? Integrator.Dop853 : Integrator.Dopri5, 1e-9d)
                      .Execute();
        }

        private static IntegratorKind Parse(string name)
        {
            return name == "Dop853" ? IntegratorKind.Dop853 : IntegratorKind.Dopri5;
        }

        private static void AssertStateEqual(SpacecraftIntegrationState actual, SpacecraftIntegrationState expected, string what)
        {
            Asserts.True(actual.Position.X == expected.Position.X &&
                         actual.Position.Y == expected.Position.Y &&
                         actual.Position.Z == expected.Position.Z,
                what + ": position is not bit-exact (" + actual.Position + " vs " + expected.Position + ").");
            Asserts.True(actual.Velocity.X == expected.Velocity.X &&
                         actual.Velocity.Y == expected.Velocity.Y &&
                         actual.Velocity.Z == expected.Velocity.Z,
                what + ": velocity is not bit-exact (" + actual.Velocity + " vs " + expected.Velocity + ").");
            Asserts.True(actual.Mass == expected.Mass, what + ": mass is not bit-exact.");
        }

        private sealed class LinearBody : IBodyStateProvider
        {
            private readonly Vector3d position0;
            private readonly Vector3d velocity0;

            public LinearBody(Vector3d position0, Vector3d velocity0)
            {
                this.position0 = position0;
                this.velocity0 = velocity0;
            }

            public void EvaluateWorldState(double timeSeconds, out Vector3d position, out Vector3d velocity)
            {
                position = position0 + (velocity0 * timeSeconds);
                velocity = velocity0;
            }
        }
    }
}
