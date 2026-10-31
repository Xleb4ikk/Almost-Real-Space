using System;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Oracles;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    [Trait("Tier", "Fast")]
    public sealed class RestrictedBodyContributionTests
    {
        private readonly ITestOutputHelper output;

        public RestrictedBodyContributionTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void RealRestricted3BodyPerturberMeasurablyBendsTheTrajectory()
        {
            Scenario scenario = ScenarioCatalog.Get("restricted-3body");
            Double3 r0 = new Double3(
                scenario.InitialState.Position.X,
                scenario.InitialState.Position.Y,
                scenario.InitialState.Position.Z);
            Double3 v0 = new Double3(
                scenario.InitialState.Velocity.X,
                scenario.InitialState.Velocity.Y,
                scenario.InitialState.Velocity.Z);
            double centralMu = scenario.RestrictedCentralMu;
            RestrictedBody[] bodies = scenario.RestrictedBodies;

            OracleState all = RestrictedNBodyOracle.Propagate(
                r0, v0, centralMu, bodies, scenario.DurationSeconds, 0.5d);
            OracleState one = RestrictedNBodyOracle.Propagate(
                r0, v0, centralMu, System.Array.Empty<RestrictedBody>(), scenario.DurationSeconds, 0.5d);
            double contribution = (all.Position - one.Position).Magnitude;
            double gate = 10d * ToleranceTable.For(
                IntegratorKind.Dop853, "restricted-3body", 1e-9d).OraclePositionMeters;

            output.WriteLine("restricted-3body: periapsis=" +
                             scenario.PeriapsisMeters.ToString("E3") + " m, periapsis speed=" +
                             scenario.PeriapsisSpeedMetersPerSecond.ToString("E3") +
                             " m/s; perturber contribution=" + contribution.ToString("E3") +
                             " m; 10x oracle tolerance=" + gate.ToString("E3") + " m.");
            Asserts.True(contribution > gate,
                "restricted-3body is too weak: perturber contribution " + contribution.ToString("E3") +
                " m <= 10x oracle tolerance " + gate.ToString("E3") + " m.");
        }

        [Fact]
        public void FlybyBodyDominatesTheCentralFieldNearApproach()
        {
            Scenario scenario = ScenarioCatalog.Get("restricted-flyby");
            Double3 r0 = new Double3(
                scenario.InitialState.Position.X,
                scenario.InitialState.Position.Y,
                scenario.InitialState.Position.Z);
            Double3 v0 = new Double3(
                scenario.InitialState.Velocity.X,
                scenario.InitialState.Velocity.Y,
                scenario.InitialState.Velocity.Z);
            double centralMu = scenario.RestrictedCentralMu;
            RestrictedBody[] bodies = scenario.RestrictedBodies;

            OracleState withBody = RestrictedNBodyOracle.Propagate(
                r0, v0, centralMu, bodies, scenario.DurationSeconds, 0.1d);
            OracleState withoutBody = RestrictedNBodyOracle.Propagate(
                r0, v0, centralMu, Array.Empty<RestrictedBody>(), scenario.DurationSeconds, 0.1d);
            double contribution = (withBody.Position - withoutBody.Position).Magnitude;
            double gate = 10d * ToleranceTable.For(
                IntegratorKind.Dop853, "restricted-flyby", 1e-9d).OraclePositionMeters;

            output.WriteLine("restricted-flyby: periapsis=" +
                             scenario.PeriapsisMeters.ToString("E3") + " m, periapsis speed=" +
                             scenario.PeriapsisSpeedMetersPerSecond.ToString("E3") +
                             " m/s; body contribution=" + contribution.ToString("E3") +
                             " m; 10x oracle tolerance=" + gate.ToString("E3") + " m.");
            Asserts.True(contribution > gate,
                "restricted-flyby is too weak: body contribution " + contribution.ToString("E3") +
                " m <= 10x oracle tolerance " + gate.ToString("E3") + " m.");
        }
    }
}
