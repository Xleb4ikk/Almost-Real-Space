using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    /// <summary>
    /// Absolute accuracy ceilings, independent of ToleranceTable calibration.
    /// Each ceiling is a hardcoded requirement, deliberately tighter than the
    /// per-row regression gate at the same tolerance.
    /// </summary>
    [Trait("Tier", "Fast")]
    public sealed class AbsoluteAccuracyCeilingTests
    {
        [Theory]
        // tol=1e-9
        [InlineData("Dop853", "kepler-circular", 1e-9d, 3e-5d, 3e-8d)]
        [InlineData("Dop853", "kepler-e05", 1e-9d, 2.5e-4d, 3.5e-7d)]
        [InlineData("Dop853", "kepler-e09", 1e-9d, 2.5e-3d, 4e-5d)]
        [InlineData("Dop853", "kepler-e099", 1e-9d, 7e-2d, 3e-2d)]
        [InlineData("Dop853", "kepler-near-parabolic", 1e-9d, 4e-5d, 6e-7d)]
        [InlineData("Dopri5", "kepler-circular", 1e-9d, 5e-2d, 5e-5d)]
        [InlineData("Dopri5", "kepler-e05", 1e-9d, 6e-1d, 9e-4d)]
        [InlineData("Dopri5", "kepler-e09", 1e-9d, 6.5d, 1e-1d)]
        [InlineData("Dopri5", "kepler-e099", 1e-9d, 1.8e2d, 8e1d)]
        [InlineData("Dopri5", "kepler-near-parabolic", 1e-9d, 1e-1d, 1.5e-3d)]
        // tol=1e-6, including the previously unguarded 636 km row
        [InlineData("Dop853", "kepler-circular", 1e-6d, 3e-1d, 3e-4d)]
        [InlineData("Dop853", "kepler-e05", 1e-6d, 7e-1d, 9e-4d)]
        [InlineData("Dop853", "kepler-e09", 1e-6d, 5.5d, 8e-2d)]
        [InlineData("Dop853", "kepler-e099", 1e-6d, 1.7e2d, 7.5e1d)]
        [InlineData("Dop853", "kepler-near-parabolic", 1e-6d, 1e-1d, 1.3e-3d)]
        [InlineData("Dopri5", "kepler-circular", 1e-6d, 3e2d, 3.5e-1d)]
        [InlineData("Dopri5", "kepler-e05", 1e-6d, 9.5e2d, 1.4d)]
        [InlineData("Dopri5", "kepler-e09", 1e-6d, 9.5e3d, 1.45e2d)]
        [InlineData("Dopri5", "kepler-e099", 1e-6d, 2.4e5d, 7e4d)]
        [InlineData("Dopri5", "kepler-near-parabolic", 1e-6d, 1.5e2d, 2.1d)]
        public void PositionAndVelocityStayUnderAbsoluteCeilings(
            string integratorName, string scenarioId, double tolerance,
            double positionCeiling, double velocityCeiling)
        {
            IntegratorKind integrator = integratorName == "Dop853"
                ? IntegratorKind.Dop853
                : IntegratorKind.Dopri5;
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);

            Asserts.True(run.MaxPositionError <= positionCeiling,
                scenarioId + "/" + integratorName + " at tol=" + tolerance.ToString("R") +
                ": position " + run.MaxPositionError.ToString("E3") +
                " m > absolute ceiling " + positionCeiling.ToString("E3") + " m.");
            Asserts.True(run.MaxVelocityError <= velocityCeiling,
                scenarioId + "/" + integratorName + " at tol=" + tolerance.ToString("R") +
                ": velocity " + run.MaxVelocityError.ToString("E3") +
                " m/s > absolute ceiling " + velocityCeiling.ToString("E3") + " m/s.");
        }
    }
}
