using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    /// <summary>
    /// Absolute accuracy ceilings, independent of ToleranceTable calibration.
    /// They are deliberately looser than the measured errors but far tighter
    /// than the regression gates at high eccentricity.
    /// </summary>
    [Trait("Tier", "Fast")]
    public sealed class AbsoluteAccuracyCeilingTests
    {
        [Theory]
        [InlineData("Dop853", "kepler-circular", 1e-3d, 1e-5d)]
        [InlineData("Dop853", "kepler-e05", 1e-2d, 1e-3d)]
        [InlineData("Dop853", "kepler-e09", 1e-1d, 1e-3d)]
        [InlineData("Dop853", "kepler-e099", 1.0d, 0.1d)]
        [InlineData("Dop853", "kepler-near-parabolic", 1e-1d, 1e-3d)]
        [InlineData("Dopri5", "kepler-circular", 1.0d, 1e-2d)]
        [InlineData("Dopri5", "kepler-e05", 10.0d, 0.1d)]
        [InlineData("Dopri5", "kepler-e09", 100.0d, 10.0d)]
        [InlineData("Dopri5", "kepler-e099", 500.0d, 200.0d)]
        [InlineData("Dopri5", "kepler-near-parabolic", 1.0d, 0.01d)]
        public void PositionAndVelocityStayUnderAbsoluteCeilings(
            string integratorName, string scenarioId, double positionCeiling, double velocityCeiling)
        {
            IntegratorKind integrator = integratorName == "Dop853"
                ? IntegratorKind.Dop853
                : IntegratorKind.Dopri5;
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-9d, 1);

            Asserts.True(run.MaxPositionError <= positionCeiling,
                scenarioId + "/" + integratorName + ": position " +
                run.MaxPositionError.ToString("E3") + " m > absolute ceiling " +
                positionCeiling.ToString("E3") + " m.");
            Asserts.True(run.MaxVelocityError <= velocityCeiling,
                scenarioId + "/" + integratorName + ": velocity " +
                run.MaxVelocityError.ToString("E3") + " m/s > absolute ceiling " +
                velocityCeiling.ToString("E3") + " m/s.");
        }
    }
}
