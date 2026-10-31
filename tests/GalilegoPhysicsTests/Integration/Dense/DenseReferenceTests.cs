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

        [Fact]
        public void Dop853DensePolynomialMatchesTheExtendedTableau()
        {
            // Tableau pin: recompute F0..F6 from the published 16-stage tableau.
            // Independent accuracy evidence comes from DenseOrderTests and the
            // analytic-oracle dense interpolation/midpoint tests.
            Scenario scenario = ScenarioCatalog.Get("kepler-circular");
            const double h = 600d;
            var segments = new List<DenseSegment>();
            SpacecraftOrbitIntegrator.DerivativeFunction derivative =
                (state, time) => scenario.Derivative(state, time);
            SpacecraftOrbitIntegrator.StepForwardDop853(
                scenario.InitialState, 0d, h, derivative,
                1e6d, 1e6d, 1e6d, 1e-12d, segments);
            Asserts.True(segments.Count == 1, "expected one accepted step, got " + segments.Count);
            DenseSegment segment = segments[0];

            SpacecraftIntegrationState y0 = segment.Y0;
            SpacecraftIntegrationState y1 = segment.Y1;
            var k = new SpacecraftIntegrationState[16];
            k[0] = derivative(y0, 0d);
            for (int s = 1; s < 12; s++)
            {
                SpacecraftIntegrationState stage = y0;
                double[] row = DOP853Coefficients.A[s];
                for (int j = 0; j < s; j++)
                {
                    if (row[j] != 0d)
                    {
                        stage += k[j] * (h * row[j]);
                    }
                }

                k[s] = derivative(stage, h * DOP853Coefficients.C[s]);
            }

            k[12] = derivative(y1, h);
            for (int s = 13; s < 16; s++)
            {
                SpacecraftIntegrationState stage = y0;
                double[] row = ExtraA[s - 13];
                for (int j = 0; j < s; j++)
                {
                    if (row[j] != 0d)
                    {
                        stage += k[j] * (h * row[j]);
                    }
                }

                k[s] = derivative(stage, h * ExtraC[s - 13]);
            }

            SpacecraftIntegrationState delta = y1 - y0;
            SpacecraftIntegrationState f0 = delta;
            SpacecraftIntegrationState f1 = (k[0] * h) - delta;
            SpacecraftIntegrationState f2 = (delta * 2d) - ((k[12] + k[0]) * h);
            SpacecraftIntegrationState f3 = SpacecraftIntegrationState.Zero;
            SpacecraftIntegrationState f4 = SpacecraftIntegrationState.Zero;
            SpacecraftIntegrationState f5 = SpacecraftIntegrationState.Zero;
            SpacecraftIntegrationState f6 = SpacecraftIntegrationState.Zero;
            for (int s = 0; s < 16; s++)
            {
                f3 += k[s] * (h * DenseD[0][s]);
                f4 += k[s] * (h * DenseD[1][s]);
                f5 += k[s] * (h * DenseD[2][s]);
                f6 += k[s] * (h * DenseD[3][s]);
            }

            double worst = 0d;
            foreach (double theta in new[] { 0.1d, 0.25d, 0.5d, 0.75d, 0.9d })
            {
                double phi = 1d - theta;
                SpacecraftIntegrationState expected = y0 + Poly853(f0, f1, f2, f3, f4, f5, f6, theta, phi);
                SpacecraftIntegrationState actual = segment.Evaluate(theta * h);
                worst = Math.Max(worst, (actual.Position - expected.Position).Magnitude /
                                        Math.Max(1d, expected.Position.Magnitude));
            }

            output.WriteLine("DOP853 dense reference max relative position mismatch = " + worst.ToString("E3"));
            Asserts.True(worst < 1e-13, "DOP853 dense polynomial mismatch " + worst.ToString("E3"));
        }

        private static SpacecraftIntegrationState Poly853(
            SpacecraftIntegrationState f0,
            SpacecraftIntegrationState f1,
            SpacecraftIntegrationState f2,
            SpacecraftIntegrationState f3,
            SpacecraftIntegrationState f4,
            SpacecraftIntegrationState f5,
            SpacecraftIntegrationState f6,
            double theta,
            double phi)
        {
            return (f0 + (phi * (f1 + (theta * (f2 + (phi * (f3 +
                    (theta * (f4 + (phi * (f5 + (theta * f6)))))))))))) * theta;
        }

        // Extended stages and dense-output rows are transcribed from Hairer/Wanner
        // "contd8" (dop853.f) instead of reading DOP853Coefficients, so a typo in
        // the production table is visible here.
        private static readonly double[][] ExtraA =
        {
            new[]
            {
                5.61675022830479523392909219681e-2, 0, 0, 0, 0, 0,
                2.53500210216624811088794765333e-1, -2.46239037470802489917441475441e-1,
                -1.24191423263816360469010140626e-1, 1.5329179827876569731206322685e-1,
                8.20105229563468988491666602057e-3, 7.56789766054569976138603589584e-3,
                -8.298e-3, 0, 0, 0
            },
            new[]
            {
                3.18346481635021405060768473261e-2, 0, 0, 0, 0,
                2.83009096723667755288322961402e-2, 5.35419883074385676223797384372e-2,
                -5.49237485713909884646569340306e-2, 0, 0, -1.08347328697249322858509316994e-4,
                3.82571090835658412954920192323e-4, -3.40465008687404560802977114492e-4,
                1.41312443674632500278074618366e-1, 0, 0
            },
            new[]
            {
                -4.28896301583791923408573538692e-1, 0, 0, 0, 0,
                -4.69762141536116384314449447206, 7.68342119606259904184240953878,
                4.06898981839711007970213554331, 3.56727187455281109270669543021e-1, 0, 0, 0,
                -1.39902416515901462129418009734e-3, 2.9475147891527723389556272149,
                -9.15095847217987001081870187138, 0
            }
        };

        private static readonly double[] ExtraC =
        {
            1.0e-1, 2.0e-1, 7.77777777777777777777777777778e-1
        };

        private static readonly double[][] DenseD =
        {
            new[]
            {
                -8.4289382761090128651353491142, 0, 0, 0, 0,
                5.6671495351937776962531783590e-1, -3.0689499459498916912797304727,
                2.3846676565120698287728149680, 2.1170345824450282767155149946,
                -8.7139158377797299206789907490e-1, 2.2404374302607882758541771650,
                6.3157877876946881815570249290e-1, -8.8990336451333310820698117400e-2,
                1.8148505520854727256656404962e1, -9.1946323924783554000451984436,
                -4.4360363875948939664310572000
            },
            new[]
            {
                1.0427508642579134603413151009e1, 0, 0, 0, 0,
                2.4228349177525818288430175319e2, 1.6520045171727028198505394887e2,
                -3.7454675472269020279518312152e2, -2.2113666853125306036270938578e1,
                7.7334326684722638389603898808, -3.0674084731089398182061213626e1,
                -9.3321305264302278729567221706, 1.5697238121770843886131091075e1,
                -3.1139403219565177677282850411e1, -9.3529243588444783865713862664,
                3.5816841486394083752465898540e1
            },
            new[]
            {
                1.9985053242002433820987653617e1, 0, 0, 0, 0,
                -3.8703730874935176555105901742e2, -1.8917813819516756882830838328e2,
                5.2780815920542364900561016686e2, -1.1573902539959630126141871134e1,
                6.8812326946963000169666922661, -1.0006050966910838403183860980,
                7.7771377980534432092869265740e-1, -2.7782057523535084065932004339,
                -6.0196695231264120758267380846e1, 8.4320405506677161018159903784e1,
                1.1992291136182789328035130030e1
            },
            new[]
            {
                -2.5693933462703749003312586129e1, 0, 0, 0, 0,
                -1.5418974869023643374053993627e2, -2.3152937917604549567536039109e2,
                3.5763911791061412378285349910e2, 9.3405324183624310003907691704e1,
                -3.7458323136451633156875139351e1, 1.0409964950896230045147246184e2,
                2.9840293426660503123344363579e1, -4.3533456590011143754432175058e1,
                9.6324553959188282948394950600e1, -3.9177261675615439165231486172e1,
                -1.4972683625798562581422125276e2
            }
        };

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
