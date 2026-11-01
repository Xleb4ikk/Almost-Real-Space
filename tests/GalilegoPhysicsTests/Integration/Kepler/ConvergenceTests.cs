using System;
using System.Collections.Generic;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Oracles;
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

        [Theory]
        [InlineData("Dopri5", 5.5d)]
        [InlineData("Dop853", 8.0d)]
        public void OneFixedStepShowsTheMethodOrder(string integratorName, double minimumOrder)
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-circular");
            IntegratorKind integrator = integratorName == "Dop853"
                ? IntegratorKind.Dop853
                : IntegratorKind.Dopri5;
            SpacecraftOrbitIntegrator.DerivativeFunction derivative =
                (state, time) => scenario.Derivative(state, time);
            double[] stepSizes = integrator == IntegratorKind.Dopri5
                ? new[] { 600d, 300d, 150d, 75d, 37.5d }
                : new[] { 600d, 300d, 150d, 75d };
            var logH = new double[stepSizes.Length];
            var logError = new double[stepSizes.Length];

            for (int i = 0; i < stepSizes.Length; i++)
            {
                double h = stepSizes[i];
                // A huge tolerance deliberately disables step rejection so this
                // measures one full step of the published method. That is the
                // local truncation order p+1, not the global order; the global
                // order is pinned by the matrix, golden and absolute-accuracy
                // gates. DenseOrderTests covers interpolation order separately.
                var segments = new List<DenseSegment>();
                SpacecraftIntegrationState final = integrator == IntegratorKind.Dopri5
                    ? SpacecraftOrbitIntegrator.StepForward(
                        scenario.InitialState, 0d, h, derivative,
                        1e6d, 1e6d, 1e6d, 1e6d, segments).State
                    : SpacecraftOrbitIntegrator.StepForwardDop853(
                        scenario.InitialState, 0d, h, derivative,
                        1e6d, 1e6d, 1e6d, 1e6d, segments).State;
                Asserts.True(segments.Count == 1,
                    integratorName + " h=" + h.ToString("R") + ": expected exactly one step, got " +
                    segments.Count + ".");
                OracleState oracle = scenario.Oracle(h);
                double error = (final.Position - oracle.Position.ToVector3d()).Magnitude;
                Asserts.True(error > 0d && double.IsFinite(error),
                    integratorName + " h=" + h.ToString("R") + ": endpoint error is not finite.");
                logH[i] = Math.Log(h);
                logError[i] = Math.Log(error);
                output.WriteLine(integratorName + " h=" + h.ToString("R") +
                                 ": endpoint error=" + error.ToString("E3") + " m.");
            }

            double meanX = 0d;
            double meanY = 0d;
            for (int i = 0; i < logH.Length; i++)
            {
                meanX += logH[i];
                meanY += logError[i];
            }

            meanX /= logH.Length;
            meanY /= logH.Length;
            double covariance = 0d;
            double variance = 0d;
            for (int i = 0; i < logH.Length; i++)
            {
                covariance += (logH[i] - meanX) * (logError[i] - meanY);
                variance += (logH[i] - meanX) * (logH[i] - meanX);
            }

            double slope = covariance / variance;
            output.WriteLine(integratorName + ": one-step local order slope = " + slope.ToString("F3") + ".");
            Asserts.True(slope >= minimumOrder,
                integratorName + ": local order " + slope.ToString("F3") +
                " < " + minimumOrder.ToString("F1") + ".");
        }

        [Fact]
        public void ConvergesWithHelperSeesATighterRun()
        {
            ScenarioExecution run = Run.Scenario("kepler-e05").With(Integrator.Dopri5, 1e-8d).Execute();
            run.ConvergesWith(1e-10d);
        }
    }
}
