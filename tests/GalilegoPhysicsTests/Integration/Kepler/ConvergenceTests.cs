using System;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Kepler
{
    public sealed class ConvergenceTests
    {
        private readonly ITestOutputHelper output;

        public ConvergenceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        public void TighteningToleranceReducesErrorAndIncreasesSteps(string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-e05");
            IntegratorKind integrator = integratorName == "Dop853" ? IntegratorKind.Dop853 : IntegratorKind.Dopri5;
            double[] tolerances = { 1e-6d, 1e-7d, 1e-8d, 1e-9d, 1e-10d };

            double previousError = double.PositiveInfinity;
            int previousSteps = 0;
            double firstError = 0d;
            double lastError = 0d;
            foreach (double tolerance in tolerances)
            {
                ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);
                output.WriteLine(integratorName + " tol=" + tolerance.ToString("E0") +
                                 ": error=" + run.MaxPositionError.ToString("E2") +
                                 ", steps=" + run.Steps + ".");
                Asserts.True(run.MaxPositionError <= previousError * 1.2d,
                    integratorName + ": error did not decrease at tol=" + tolerance.ToString("E0") +
                    " (" + run.MaxPositionError.ToString("E2") + " vs " + previousError.ToString("E2") + ").");
                Asserts.True(run.Steps >= previousSteps,
                    integratorName + ": step count decreased at tol=" + tolerance.ToString("E0") + ".");
                previousError = run.MaxPositionError;
                previousSteps = run.Steps;
                if (double.IsInfinity(firstError) || firstError == 0d)
                {
                    firstError = run.MaxPositionError;
                }

                lastError = run.MaxPositionError;
            }

            Asserts.True(lastError < firstError * 0.1d,
                integratorName + ": tighter tolerances did not improve the error by 10x (" +
                firstError.ToString("E2") + " -> " + lastError.ToString("E2") + ").");
        }

        [Fact]
        public void ConvergesWithHelperSeesATighterRun()
        {
            ScenarioExecution run = Run.Scenario("kepler-e05").With(Integrator.Dopri5, 1e-8d).Execute();
            run.ConvergesWith(1e-10d);
        }
    }
}
