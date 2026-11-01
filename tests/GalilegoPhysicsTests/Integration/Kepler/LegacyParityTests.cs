using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Kepler
{
    /// <summary>
    /// Port of the old RegressionTests intent: the legacy OrbitIntegrator and the
    /// full-state SpacecraftOrbitIntegrator must agree on a pure-gravity arc.
    /// </summary>
    [Trait("Tier", "Fast")]
    public sealed class LegacyParityTests
    {
        private readonly ITestOutputHelper output;

        public LegacyParityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void LegacyAndSpacecraftIntegratorsAgreeOnPureGravity()
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-circular");
            Vector3d legacyPosition = scenario.InitialState.Position;
            Vector3d legacyVelocity = scenario.InitialState.Velocity;
            SpacecraftIntegrationState spacecraft = scenario.InitialState;
            double time = 0d;
            const double chunk = 60d;
            const int chunks = 60;
            double worstPosition = 0d;
            double worstVelocity = 0d;

#pragma warning disable 0618
            for (int i = 0; i < chunks; i++)
            {
                IntegrationResult legacy = OrbitIntegrator.StepForward(
                    legacyPosition, legacyVelocity, time, chunk,
                    (position, t) => scenario.Derivative(
                        new SpacecraftIntegrationState { Position = position, Velocity = Vector3d.Zero, Mass = 1d }, t).Velocity,
                    absoluteTolerance: 1e-9d,
                    relativeTolerance: 1e-10d);
                legacyPosition = legacy.Position;
                legacyVelocity = legacy.Velocity;

                spacecraft = SpacecraftOrbitIntegrator.StepForward(
                    spacecraft, time, chunk,
                    (state, t) => scenario.Derivative(state, t),
                    1e-9d, 1e-9d, 1e-9d, 1e-10d).State;
                time += chunk;

                worstPosition = Math.Max(worstPosition, (legacyPosition - spacecraft.Position).Magnitude);
                worstVelocity = Math.Max(worstVelocity, (legacyVelocity - spacecraft.Velocity).Magnitude);
            }
#pragma warning restore 0618

            output.WriteLine("Legacy parity: max |dP|=" + worstPosition.ToString("E3") +
                             " m, max |dV|=" + worstVelocity.ToString("E3") + " m/s.");
            Asserts.True(worstPosition < 1e-6, "legacy position parity " + worstPosition.ToString("E3"));
            Asserts.True(worstVelocity < 1e-9, "legacy velocity parity " + worstVelocity.ToString("E3"));
        }
    }
}
