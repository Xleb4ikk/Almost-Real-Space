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
        public void RepeatedRestrictedBodiesMeasurablyPerturbTheTrajectory()
        {
            const double centralMu = 1.32712440018e20d;
            Double3 r0 = new Double3(1.5e10d, 0d, 0d);
            Double3 v0 = new Double3(0d, Math.Sqrt(centralMu / 1.5e10d), 0d);
            const double duration = 3600d;
            RestrictedBody[] bodies = RestrictedNBodyOracle.CreateBodies(centralMu);

            OracleState all = RestrictedNBodyOracle.Propagate(r0, v0, centralMu, bodies, duration, 0.5d);
            OracleState one = RestrictedNBodyOracle.Propagate(r0, v0, centralMu, new[] { bodies[0] }, duration, 0.5d);
            double contribution = (all.Position - one.Position).Magnitude;
            double gate = 10d * ToleranceTable.For(
                IntegratorKind.Dop853, ScenarioClass.RestrictedNBody, 1e-9d).OraclePositionMeters;

            output.WriteLine("restricted-3body: perturber contribution=" +
                             contribution.ToString("E3") + " m; 10x oracle tolerance=" +
                             gate.ToString("E3") + " m.");
            Asserts.True(contribution > gate,
                "restricted-3body is too weak: perturber contribution " + contribution.ToString("E3") +
                " m <= 10x oracle tolerance " + gate.ToString("E3") + " m.");
        }

        [Fact]
        public void FlybyBodyDominatesTheCentralFieldNearApproach()
        {
            const double centralMu = 1.32712440018e20d;
            const double bodyR = 1.0e10d;
            var body = new RestrictedBody(3.986004418e14d, bodyR, 0d, 0d);
            Double3 r0 = new Double3(bodyR + 3.0e7d, 0d, 0d);
            Double3 v0 = new Double3(0d, 3000d, 0d);
            const double duration = 15000d;

            OracleState withBody = RestrictedNBodyOracle.Propagate(r0, v0, centralMu, new[] { body }, duration, 0.1d);
            OracleState withoutBody = RestrictedNBodyOracle.Propagate(r0, v0, centralMu, Array.Empty<RestrictedBody>(), duration, 0.1d);
            double contribution = (withBody.Position - withoutBody.Position).Magnitude;
            double gate = 10d * ToleranceTable.For(
                IntegratorKind.Dop853, ScenarioClass.RestrictedFlyby, 1e-9d).OraclePositionMeters;

            output.WriteLine("restricted-flyby: body contribution=" +
                             contribution.ToString("E3") + " m; 10x oracle tolerance=" +
                             gate.ToString("E3") + " m.");
            Asserts.True(contribution > gate,
                "restricted-flyby is too weak: body contribution " + contribution.ToString("E3") +
                " m <= 10x oracle tolerance " + gate.ToString("E3") + " m.");
        }
    }
}
