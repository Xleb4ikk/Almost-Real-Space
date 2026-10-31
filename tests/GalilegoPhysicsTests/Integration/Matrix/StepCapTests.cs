using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    [Trait("Tier", "Fast")]
    public sealed class StepCapTests
    {
        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void ExactLinearDriftUsesTheMaximumStep(string integratorName)
        {
            IntegratorKind integrator = integratorName == "Dop853"
                ? IntegratorKind.Dop853
                : IntegratorKind.Dopri5;
            Scenario scenario = ScenarioCatalog.LinearDrift;
            const double documentedMaxStep = 600d;
            Asserts.Near(OrbitIntegrator.DefaultMaxStepSize, documentedMaxStep, 0d,
                "DefaultMaxStepSize must stay at the documented 600 s cap");
            int expectedSteps = (int)Math.Ceiling(scenario.DurationSeconds / documentedMaxStep);

            foreach (double tolerance in scenario.Tolerances)
            {
                ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);
                Asserts.True(run.Steps == expectedSteps,
                    "exact drift / " + scenario.IntegratorLabel(integrator) +
                    " at tol=" + tolerance.ToString("R") + " took " + run.Steps +
                    " steps, expected the " + expectedSteps + " step cap.");
                foreach (DenseSegment segment in run.Segments)
                {
                    Asserts.Near(segment.T1 - segment.T0, documentedMaxStep, 1e-9d,
                        "exact drift / " + scenario.IntegratorLabel(integrator) +
                        " at tol=" + tolerance.ToString("R") + ": segment is not the 600 s cap");
                }

                Asserts.True(run.MaxPositionError < 1e-6d,
                    "exact drift / " + scenario.IntegratorLabel(integrator) +
                    " at tol=" + tolerance.ToString("R") + " has position error " +
                    run.MaxPositionError.ToString("E3") + " m.");
            }
        }
    }
}
