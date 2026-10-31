using System;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    /// <summary>
    /// Physical accuracy ceilings, derived from the orbit's own periapsis.
    /// These are requirements, not calibrated regression gates: a 212 km error
    /// on an orbit with a 100 km periapsis is rejected no matter how stable the
    /// measurement is. Step-endpoint and dense-interpolation errors are gated
    /// separately so a good integrator cannot hide a bad interpolant (or vice
    /// versa).
    /// </summary>
    [Trait("Tier", "Fast")]
    public sealed class AbsoluteAccuracyCeilingTests
    {
        [Theory]
        [InlineData("Dop853", "kepler-circular", 1e-6d)]
        [InlineData("Dop853", "kepler-e05", 1e-6d)]
        [InlineData("Dop853", "kepler-e09", 1e-6d)]
        [InlineData("Dop853", "kepler-e09", 1e-9d)]
        [InlineData("Dop853", "kepler-e099", 1e-6d)]
        [InlineData("Dop853", "kepler-e099", 1e-9d)]
        [InlineData("Dop853", "kepler-near-parabolic", 1e-6d)]
        [InlineData("Dop853", "kepler-near-parabolic", 1e-9d)]
        [InlineData("Dop853", "kepler-hyperbolic", 1e-6d)]
        [InlineData("Dopri5", "kepler-circular", 1e-6d)]
        [InlineData("Dopri5", "kepler-e05", 1e-6d)]
        [InlineData("Dopri5", "kepler-e09", 1e-9d)]
        [InlineData("Dopri5", "kepler-e099", 1e-9d)]
        [InlineData("Dopri5", "kepler-near-parabolic", 1e-9d)]
        [InlineData("Dopri5", "restricted-3body", 1e-6d)]
        [InlineData("Dopri5", "restricted-flyby", 1e-6d)]
        public void PositionVelocityAndOrbitStayWithinPeriapsisScale(
            string integratorName, string scenarioId, double tolerance)
        {
            IntegratorKind integrator = integratorName == "Dop853"
                ? IntegratorKind.Dop853
                : IntegratorKind.Dopri5;
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);
            string row = scenarioId + "/" + integratorName + " at tol=" + tolerance.ToString("R");

            Asserts.True(run.MaxStepPositionError <= scenario.StepPositionLimitMeters,
                row + ": step position " + run.MaxStepPositionError.ToString("E3") +
                " m > " + (scenario.PhysicalStepPositionFraction * 100d).ToString("G3") +
                "% of periapsis (" + scenario.PeriapsisMeters.ToString("E3") + " m).");
            Asserts.True(run.MaxDensePositionError <= scenario.DensePositionLimitMeters,
                row + ": dense position " + run.MaxDensePositionError.ToString("E3") +
                " m > " + (scenario.PhysicalDensePositionFraction * 100d).ToString("G3") +
                "% of periapsis (" + scenario.PeriapsisMeters.ToString("E3") + " m).");
            if (scenario.PeriapsisSpeedMetersPerSecond > 0d)
            {
                Asserts.True(run.MaxStepVelocityError <= scenario.StepVelocityLimitMetersPerSecond,
                    row + ": step velocity " + run.MaxStepVelocityError.ToString("E3") +
                    " m/s > 1% of periapsis speed.");
                Asserts.True(run.MaxDenseVelocityError <= scenario.DenseVelocityLimitMetersPerSecond,
                    row + ": dense velocity " + run.MaxDenseVelocityError.ToString("E3") +
                    " m/s > 2% of periapsis speed.");
            }

            run.MatchesPhysicalAccuracy();
        }

        [Theory]
        [InlineData("kepler-e09")]
        [InlineData("kepler-e099")]
        [InlineData("kepler-near-parabolic")]
        public void CoarseDopri5HighEccentricityRowsViolateThePhysicalCeiling(string scenarioId)
        {
            // The row is deliberately absent from the certified matrix. Prove
            // the exclusion is justified by actually running it: it must exceed
            // the same physical requirement that the certified rows meet.
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            double[] tolerances = scenario.TolerancesFor(IntegratorKind.Dopri5);
            Asserts.True(Array.IndexOf(tolerances, 1e-6d) < 0,
                scenarioId + " still certifies DOPRI5 at tol=1e-6.");

            ScenarioExecution run = ScenarioExecution.Execute(
                scenario, IntegratorKind.Dopri5, 1e-6d, 1);
            bool violates =
                run.MaxStepPositionError > scenario.StepPositionLimitMeters ||
                run.MaxDensePositionError > scenario.DensePositionLimitMeters;
            Asserts.True(violates,
                scenarioId + " / Dopri5 at tol=1e-6 now meets the physical ceiling " +
                "(step " + run.MaxStepPositionError.ToString("E3") +
                " m, dense " + run.MaxDensePositionError.ToString("E3") +
                " m; limits " + scenario.StepPositionLimitMeters.ToString("E3") +
                " / " + scenario.DensePositionLimitMeters.ToString("E3") +
                " m); re-enable the row in the certified matrix.");
        }
    }
}
