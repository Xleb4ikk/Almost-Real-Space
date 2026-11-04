using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    /// <summary>
    /// The Earth-radius matrix cannot see the heliocentric regime: at 1 AU the
    /// 600 s step cap, not the error controller, chooses the step. This is a
    /// fixed-step comparison at the cap, so it gates absolute accuracy and cost
    /// rather than claiming that the higher-order method must win; the measured
    /// result on the current controller is that DOPRI5 is slightly cheaper and
    /// no less accurate here.
    /// </summary>
    public sealed class SunRegimeTests
    {
        private const double Tolerance = 1e-9d;
        private const double StepErrorLimitMeters = 1e-2d;
        private const double CostRatioLimit = 3d;

        [Fact]
        [Trait("Tier", "Fast")]
        public void SunOneAuStaysAccurateAtTheStepCap()
        {
            Scenario scenario = ScenarioCatalog.SunOneAu;
            ScenarioExecution dop853 = ScenarioExecution.Execute(
                scenario, IntegratorKind.Dop853, Tolerance, 1);
            ScenarioExecution dopri5 = ScenarioExecution.Execute(
                scenario, IntegratorKind.Dopri5, Tolerance, 1);
            string measured =
                "DOP853 step " + dop853.MaxStepPositionError.ToString("E3") + " m / " +
                dop853.RhsCalls + " calls; DOPRI5 step " +
                dopri5.MaxStepPositionError.ToString("E3") + " m / " +
                dopri5.RhsCalls + " calls.";

            Asserts.True(dop853.MaxStepPositionError > 0d && dopri5.MaxStepPositionError > 0d,
                "Sun 1 AU: a zero step error cannot rank the integrators. " + measured);
            Asserts.True(dop853.MaxStepPositionError <= StepErrorLimitMeters,
                "Sun 1 AU: DOP853 step-endpoint error must stay under 1 cm. " + measured);
            Asserts.True(dopri5.MaxStepPositionError <= StepErrorLimitMeters,
                "Sun 1 AU: DOPRI5 step-endpoint error must stay under 1 cm. " + measured);
            Asserts.True(dop853.RhsCalls <= CostRatioLimit * dopri5.RhsCalls,
                "Sun 1 AU: DOP853 must stay within 3x the DOPRI5 cost. " + measured);
        }
    }
}
