using System;
using System.Collections.Generic;
using System.Linq;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    [Trait("Tier", "Fast")]
    public sealed class StepControllerTests
    {
        private readonly ITestOutputHelper output;

        public StepControllerTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void TighterToleranceDoesNotReduceTheStepCountExceptForExplicitBounds(string integratorName)
        {
            IntegratorKind integrator = integratorName == "Dop853"
                ? IntegratorKind.Dop853
                : IntegratorKind.Dopri5;
            var excluded = new List<string>();
            foreach (Scenario scenario in ScenarioCatalog.All)
            {
                if (scenario.ControllerBound)
                {
                    excluded.Add(scenario.Id + " / " + scenario.IntegratorLabel(integrator) +
                                 ": " + scenario.ControllerBoundReason);
                    continue;
                }

                double[] tolerances = scenario.Tolerances.OrderByDescending(value => value).ToArray();
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

                Asserts.True(steps.Count >= 2 && steps[steps.Count - 1] > steps[0],
                    "step controller is not active for " + scenario.Id + " / " +
                    scenario.IntegratorLabel(integrator) + ": steps " +
                    string.Join(" -> ", steps) + "; mark it ControllerBound with a reason.");
            }

            foreach (string line in excluded)
            {
                output.WriteLine("ControllerBound: " + line);
            }
        }
    }
}
