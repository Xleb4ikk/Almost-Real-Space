using System;
using System.Collections.Generic;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Dense
{
    [Trait("Tier", "Fast")]
    public sealed class DenseReferenceTests
    {
        private readonly ITestOutputHelper output;

        public DenseReferenceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Dopri5DensePolynomialMatchesHairerRationalReference()
        {
            Scenario scenario = ScenarioCatalog.Get("kepler-circular");
            const double h = 600d;
            var segments = new List<DenseSegment>();
            SpacecraftOrbitIntegrator.DerivativeFunction derivative =
                (state, time) => scenario.Derivative(state, time);
            SpacecraftOrbitIntegrator.StepForward(
                scenario.InitialState, 0d, h, derivative,
                1e6d, 1e6d, 1e6d, 1e-12d, segments);
            Asserts.True(segments.Count == 1, "expected one accepted step, got " + segments.Count);
            DenseSegment segment = segments[0];

            SpacecraftIntegrationState y0 = segment.Y0;
            SpacecraftIntegrationState y1 = segment.Y1;
            SpacecraftIntegrationState k1 = derivative(y0, 0d);
            SpacecraftIntegrationState s2 = y0 + k1 * (h * DOPRI5Coefficients.a21);
            SpacecraftIntegrationState k2 = derivative(s2, h * DOPRI5Coefficients.c2);
            SpacecraftIntegrationState s3 = y0 + k1 * (h * DOPRI5Coefficients.a31) + k2 * (h * DOPRI5Coefficients.a32);
            SpacecraftIntegrationState k3 = derivative(s3, h * DOPRI5Coefficients.c3);
            SpacecraftIntegrationState s4 = y0 + k1 * (h * DOPRI5Coefficients.a41) + k2 * (h * DOPRI5Coefficients.a42) + k3 * (h * DOPRI5Coefficients.a43);
            SpacecraftIntegrationState k4 = derivative(s4, h * DOPRI5Coefficients.c4);
            SpacecraftIntegrationState s5 = y0 + k1 * (h * DOPRI5Coefficients.a51) + k2 * (h * DOPRI5Coefficients.a52) + k3 * (h * DOPRI5Coefficients.a53) + k4 * (h * DOPRI5Coefficients.a54);
            SpacecraftIntegrationState k5 = derivative(s5, h * DOPRI5Coefficients.c5);
            SpacecraftIntegrationState s6 = y0 + k1 * (h * DOPRI5Coefficients.a61) + k2 * (h * DOPRI5Coefficients.a62) + k3 * (h * DOPRI5Coefficients.a63) + k4 * (h * DOPRI5Coefficients.a64) + k5 * (h * DOPRI5Coefficients.a65);
            SpacecraftIntegrationState k6 = derivative(s6, h * DOPRI5Coefficients.c6);
            SpacecraftIntegrationState s7 = y0 + k1 * (h * DOPRI5Coefficients.a71) + k3 * (h * DOPRI5Coefficients.a73) + k4 * (h * DOPRI5Coefficients.a74) + k5 * (h * DOPRI5Coefficients.a75) + k6 * (h * DOPRI5Coefficients.a76);
            SpacecraftIntegrationState k7 = derivative(s7, h * DOPRI5Coefficients.c7);

            SpacecraftIntegrationState c1 = y1 - y0;
            SpacecraftIntegrationState c2 = k1 * h - c1;
            SpacecraftIntegrationState c3 = c1 - c2 - k7 * h;
            SpacecraftIntegrationState c4 = ((k1 * D1) + (k3 * D3) + (k4 * D4) +
                                             (k5 * D5) + (k6 * D6) + (k7 * D7)) * h;

            double worst = 0d;
            foreach (double theta in new[] { 0.1d, 0.25d, 0.5d, 0.75d, 0.9d })
            {
                double t = segment.T0 + (theta * h);
                SpacecraftIntegrationState expected = Poly(y0, c1, c2, c3, c4, theta);
                SpacecraftIntegrationState actual = segment.Evaluate(t);
                worst = Math.Max(worst, (actual.Position - expected.Position).Magnitude /
                                        Math.Max(1d, expected.Position.Magnitude));
            }

            output.WriteLine("DOPRI5 dense reference max relative position mismatch = " + worst.ToString("E3"));
            Asserts.True(worst < 1e-13, "DOPRI5 dense polynomial mismatch " + worst.ToString("E3"));
        }

        private static readonly double D1 = new ExactRational(-12715105075L, 11282082432L).ToDouble();
        private static readonly double D3 = new ExactRational(87487479700L, 32700410799L).ToDouble();
        private static readonly double D4 = new ExactRational(-10690763975L, 1880347072L).ToDouble();
        private static readonly double D5 = new ExactRational(701980252875L, 199316789632L).ToDouble();
        private static readonly double D6 = new ExactRational(-1453857185L, 822651844L).ToDouble();
        private static readonly double D7 = new ExactRational(69997945L, 29380423L).ToDouble();

        private static SpacecraftIntegrationState Poly(
            SpacecraftIntegrationState c0,
            SpacecraftIntegrationState c1,
            SpacecraftIntegrationState c2,
            SpacecraftIntegrationState c3,
            SpacecraftIntegrationState c4,
            double theta)
        {
            double t1 = 1d - theta;
            return c0 + (c1 + (c2 + (c3 + c4 * t1) * theta) * t1) * theta;
        }
    }
}
