using System;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Kepler
{
    [Trait("Tier", "Fast")]
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

            var logTolerance = new double[tolerances.Length];
            var logError = new double[tolerances.Length];
            double previousError = double.PositiveInfinity;
            int previousSteps = 0;
            for (int i = 0; i < tolerances.Length; i++)
            {
                double tolerance = tolerances[i];
                ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);
                output.WriteLine(integratorName + " tol=" + tolerance.ToString("E0") +
                                 ": error=" + run.MaxPositionError.ToString("E2") +
                                 ", steps=" + run.Steps + ".");
                Asserts.True(run.MaxPositionError <= previousError * 1.2d,
                    integratorName + ": error did not decrease at tol=" + tolerance.ToString("E0") +
                    " (" + run.MaxPositionError.ToString("E2") + " vs " + previousError.ToString("E2") + ").");
                Asserts.True(run.Steps > previousSteps,
                    integratorName + ": step count did not increase at tol=" + tolerance.ToString("E0") + ".");
                previousError = run.MaxPositionError;
                previousSteps = run.Steps;
                logTolerance[i] = Math.Log(tolerance);
                logError[i] = Math.Log(Math.Max(run.MaxPositionError, 1e-300d));
            }

            double meanX = 0d;
            double meanY = 0d;
            for (int i = 0; i < tolerances.Length; i++)
            {
                meanX += logTolerance[i];
                meanY += logError[i];
            }

            meanX /= tolerances.Length;
            meanY /= tolerances.Length;
            double covariance = 0d;
            double variance = 0d;
            for (int i = 0; i < tolerances.Length; i++)
            {
                covariance += (logTolerance[i] - meanX) * (logError[i] - meanY);
                variance += (logTolerance[i] - meanX) * (logTolerance[i] - meanX);
            }

            double slope = covariance / variance;
            output.WriteLine(integratorName + ": log(error)/log(tolerance) slope = " + slope.ToString("F3") + ".");
            Asserts.True(slope >= 1.0d,
                integratorName + ": error scales too weakly with tolerance (slope " + slope.ToString("F3") + ").");
            Asserts.True(Math.Exp(logError[logError.Length - 1]) < Math.Exp(logError[0]) * 0.01d,
                integratorName + ": tighter tolerances did not improve the error by 100x (" +
                Math.Exp(logError[0]).ToString("E2") + " -> " +
                Math.Exp(logError[logError.Length - 1]).ToString("E2") + ").");
        }

        [Fact]
        public void ConvergesWithHelperSeesATighterRun()
        {
            ScenarioExecution run = Run.Scenario("kepler-e05").With(Integrator.Dopri5, 1e-8d).Execute();
            run.ConvergesWith(1e-10d);
        }
    }
}
