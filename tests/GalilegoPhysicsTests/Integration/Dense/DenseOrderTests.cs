using System;
using System.Collections.Generic;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Oracles;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Dense
{
    [Trait("Tier", "Fast")]
    public sealed class DenseOrderTests
    {
        private readonly ITestOutputHelper output;

        public DenseOrderTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Dopri5DenseInterpolationIsFifthOrder()
        {
            double circular = MeasureSlope(IntegratorKind.Dopri5, "kepler-circular",
                new[] { 600d, 300d, 150d, 75d, 37.5d, 18.75d });
            double eccentric = MeasureSlope(IntegratorKind.Dopri5, "kepler-e05",
                new[] { 300d, 150d, 75d, 37.5d, 18.75d });
            output.WriteLine("DOPRI5 dense log-log slope: circular=" + circular.ToString("F3") +
                             ", e05=" + eccentric.ToString("F3") + ".");
            Asserts.True(circular >= 4.7d, "DOPRI5 circular dense order is " + circular.ToString("F3") + " < 4.7");
            Asserts.True(eccentric >= 4.7d, "DOPRI5 e05 dense order is " + eccentric.ToString("F3") + " < 4.7");
        }

        [Fact]
        public void Dop853DenseInterpolationIsEighthOrder()
        {
            // The circular probe stops at h=75 s: below that the double-precision
            // round-off floor dominates before the h^8 term does.
            double circular = MeasureSlope(IntegratorKind.Dop853, "kepler-circular",
                new[] { 600d, 300d, 150d, 75d });
            double eccentric = MeasureSlope(IntegratorKind.Dop853, "kepler-e05",
                new[] { 300d, 150d, 75d, 37.5d, 18.75d });
            output.WriteLine("DOP853 dense log-log slope: circular=" + circular.ToString("F3") +
                             ", e05=" + eccentric.ToString("F3") + " (Hairer/Wanner contd8).");
            Asserts.True(circular >= 7.5d,
                "DOP853 circular dense order is " + circular.ToString("F3") + " < 7.5");
            Asserts.True(eccentric >= 7.5d,
                "DOP853 e05 dense order is " + eccentric.ToString("F3") + " < 7.5");
        }

        private double MeasureSlope(IntegratorKind integrator, string scenarioId, double[] stepSizes)
        {
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            var logH = new List<double>();
            var logError = new List<double>();
            foreach (double h in stepSizes)
            {
                var segments = new List<DenseSegment>();
                SpacecraftOrbitIntegrator.DerivativeFunction derivative =
                    (state, time) => scenario.Derivative(state, time);
                SpacecraftIntegrationState final = integrator == IntegratorKind.Dopri5
                    ? SpacecraftOrbitIntegrator.StepForward(
                        scenario.InitialState, 0d, h, derivative,
                        1e6d, 1e6d, 1e6d, 1e-12d, segments).State
                    : SpacecraftOrbitIntegrator.StepForwardDop853(
                        scenario.InitialState, 0d, h, derivative,
                        1e6d, 1e6d, 1e6d, 1e-12d, segments).State;

                Asserts.True(segments.Count == 1,
                    "dense-order probe expected one accepted step at h=" + h.ToString("R") +
                    ", got " + segments.Count + "; loosen the probe tolerance.");
                Asserts.True(segments[0].T1 - segments[0].T0 == h || Math.Abs(segments[0].T1 - segments[0].T0 - h) < 1e-12,
                    "segment length does not match h.");

                double worst = 0d;
                foreach (double theta in new[] { 0.25d, 0.5d, 0.75d })
                {
                    double time = h * theta;
                    SpacecraftIntegrationState dense = segments[0].Evaluate(time);
                    OracleState oracle = scenario.Oracle(time);
                    worst = Math.Max(worst, (dense.Position - oracle.Position.ToVector3d()).Magnitude);
                }

                output.WriteLine(integrator + " " + scenarioId + " h=" + h.ToString("R") +
                                 " dense error=" + worst.ToString("E3"));
                logH.Add(Math.Log(h));
                logError.Add(Math.Log(Math.Max(worst, 1e-300d)));
            }

            double meanX = 0d;
            double meanY = 0d;
            for (int i = 0; i < logH.Count; i++)
            {
                meanX += logH[i];
                meanY += logError[i];
            }

            meanX /= logH.Count;
            meanY /= logError.Count;
            double covariance = 0d;
            double variance = 0d;
            for (int i = 0; i < logH.Count; i++)
            {
                covariance += (logH[i] - meanX) * (logError[i] - meanY);
                variance += (logH[i] - meanX) * (logH[i] - meanX);
            }

            return covariance / variance;
        }
    }
}

