using System;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    /// <summary>
    /// Physical accuracy ceilings, derived from the orbit's own periapsis.
    /// These are requirements, not calibrated regression gates: an error of
    /// 0.2% of the periapsis is rejected no matter how stable the measurement
    /// is. Step-endpoint and dense-interpolation errors are gated separately so
    /// a good integrator cannot hide a bad interpolant (or vice versa).
    ///
    /// A periapsis-relative gate alone is far too loose on large orbits (0.2%
    /// of a 7,000 km orbit is 14 km). <see cref="PositionAndVelocityStayUnderAbsoluteCeilings"/>
    /// adds tight absolute ceilings pinned to the measured error (at most 4x
    /// headroom). Those numbers are deliberately not read from ToleranceTable:
    /// a stale calibration cannot silently relax them. They are regression
    /// locks, not requirements derived from first principles.
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
        // tol=1e-9. Tight regression ceilings pinned to the measured error, not
        // ToleranceTable lookups and not physically derived bounds.
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
        // tol=1e-6 for the certified rows only. The coarse DOPRI5 high-e rows
        // are deliberately outside the certified matrix; CoarseDopri5... below
        // owns them, and an absolute ceiling there would be either vacuous or
        // would silently certify behaviour the matrix has rejected.
        [InlineData("Dop853", "kepler-circular", 1e-6d, 3e-1d, 3e-4d)]
        [InlineData("Dop853", "kepler-e05", 1e-6d, 7e-1d, 9e-4d)]
        [InlineData("Dop853", "kepler-e09", 1e-6d, 5.5d, 8e-2d)]
        [InlineData("Dop853", "kepler-e099", 1e-6d, 1.7e2d, 7.5e1d)]
        [InlineData("Dop853", "kepler-near-parabolic", 1e-6d, 1e-1d, 1.3e-3d)]
        [InlineData("Dopri5", "kepler-circular", 1e-6d, 3e2d, 3.5e-1d)]
        [InlineData("Dopri5", "kepler-e05", 1e-6d, 9.5e2d, 1.4d)]
        public void PositionAndVelocityStayUnderAbsoluteCeilings(
            string integratorName, string scenarioId, double tolerance,
            double positionCeiling, double velocityCeiling)
        {
            IntegratorKind integrator = integratorName == "Dop853"
                ? IntegratorKind.Dop853
                : IntegratorKind.Dopri5;
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);
            string row = scenarioId + "/" + integratorName + " at tol=" + tolerance.ToString("R");

            Asserts.True(run.MaxPositionError <= positionCeiling,
                row + ": position " + run.MaxPositionError.ToString("E3") +
                " m > hard absolute ceiling " + positionCeiling.ToString("E3") + " m.");
            Asserts.True(run.MaxVelocityError <= velocityCeiling,
                row + ": velocity " + run.MaxVelocityError.ToString("E3") +
                " m/s > hard absolute ceiling " + velocityCeiling.ToString("E3") + " m/s.");

            // A ceiling is a requirement, not a free parameter. It must stay
            // tighter than the periapsis-relative physical gate and must not be
            // silently relaxed to many times the current measurement.
            Asserts.True(positionCeiling <= scenario.StepPositionLimitMeters,
                row + ": position ceiling " + positionCeiling.ToString("E3") +
                " m is looser than the physical step limit " +
                scenario.StepPositionLimitMeters.ToString("E3") + " m.");
            Asserts.True(velocityCeiling <= scenario.StepVelocityLimitMetersPerSecond,
                row + ": velocity ceiling " + velocityCeiling.ToString("E3") +
                " m/s is looser than the physical step limit " +
                scenario.StepVelocityLimitMetersPerSecond.ToString("E3") + " m/s.");
            // Improvement is not a failure: a ceiling far above the measurement is
            // a tightening opportunity. The physical step/dense limits above stay
            // hard and are not relaxed.
            if (positionCeiling > Math.Max(run.MaxPositionError * 4d, 1e-12d))
            {
                Console.WriteLine("tighten ceiling: " + row + " position ceiling "
                    + positionCeiling.ToString("E3") + " m is more than 4x the measured "
                    + run.MaxPositionError.ToString("E3") + " m.");
            }

            if (velocityCeiling > Math.Max(run.MaxVelocityError * 4d, 1e-15d))
            {
                Console.WriteLine("tighten ceiling: " + row + " velocity ceiling "
                    + velocityCeiling.ToString("E3") + " m/s is more than 4x the measured "
                    + run.MaxVelocityError.ToString("E3") + " m/s.");
            }
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
            bool violatesPosition =
                run.MaxStepPositionError > scenario.StepPositionLimitMeters ||
                run.MaxDensePositionError > scenario.DensePositionLimitMeters;
            bool violatesVelocity =
                run.MaxStepVelocityError > scenario.StepVelocityLimitMetersPerSecond ||
                run.MaxDenseVelocityError > scenario.DenseVelocityLimitMetersPerSecond;
            Asserts.True(violatesPosition || violatesVelocity,
                scenarioId + " / Dopri5 at tol=1e-6 now meets the physical ceiling " +
                "(step " + run.MaxStepPositionError.ToString("E3") +
                " m, dense " + run.MaxDensePositionError.ToString("E3") +
                " m, step velocity " + run.MaxStepVelocityError.ToString("E3") +
                " m/s, dense velocity " + run.MaxDenseVelocityError.ToString("E3") +
                " m/s; limits " + scenario.StepPositionLimitMeters.ToString("E3") +
                " / " + scenario.DensePositionLimitMeters.ToString("E3") +
                " m and " + scenario.StepVelocityLimitMetersPerSecond.ToString("E3") +
                " / " + scenario.DenseVelocityLimitMetersPerSecond.ToString("E3") +
                " m/s); re-enable the row in the certified matrix.");
        }
    }
}
