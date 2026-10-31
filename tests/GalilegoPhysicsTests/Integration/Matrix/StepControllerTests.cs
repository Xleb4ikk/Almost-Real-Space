using System;
using System.Collections.Generic;
using System.Linq;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    public sealed class StepControllerTests
    {
        [Theory]
        [Trait("Tier", "Fast")]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void TighterToleranceIncreasesTheStepCountOnFastScenarios(string integratorName)
        {
            AssertStepCountsIncrease(Parse(integratorName), ScenarioCatalog.Fast());
        }

        [Theory]
        [Trait("Tier", "Slow")]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void TighterToleranceIncreasesTheStepCountOnSlowScenarios(string integratorName)
        {
            AssertStepCountsIncrease(Parse(integratorName), ScenarioCatalog.Slow());
        }

        private static void AssertStepCountsIncrease(IntegratorKind integrator, IEnumerable<Scenario> scenarios)
        {
            foreach (Scenario scenario in scenarios)
            {
                double[] tolerances = scenario.Tolerances.OrderByDescending(value => value).ToArray();
                Asserts.True(tolerances.Length >= 2,
                    scenario.Id + " needs at least two tolerances to exercise the step controller.");

                int previous = -1;
                var steps = new List<int>();
                foreach (double tolerance in tolerances)
                {
                    ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);
                    steps.Add(run.Steps);
                    Asserts.True(run.Steps >= previous,
                        "step count decreased for " + scenario.Id + " / " +
                        scenario.IntegratorLabel(integrator) + " at tol=" + tolerance.ToString("R") +
                        ": " + string.Join(" -> ", steps));
                    previous = run.Steps;
                }

                Asserts.True(steps[steps.Count - 1] > steps[0],
                    "step controller is not active for " + scenario.Id + " / " +
                    scenario.IntegratorLabel(integrator) + ": steps " +
                    string.Join(" -> ", steps) + ".");
            }
        }

        private static IntegratorKind Parse(string integratorName)
        {
            return integratorName == "Dop853" ? IntegratorKind.Dop853 : IntegratorKind.Dopri5;
        }
    }
}
