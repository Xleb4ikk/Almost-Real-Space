using System;
using System.Collections.Generic;
using Galilego.Core;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Appendix C, C5': the ported WH-BM14A stepper. In pure central gravity the
    /// Kepler-split composition is exact, so the test can compare against the
    /// universal-Kepler drift itself. This is the core gate before the game policy
    /// is allowed to select the method.
    /// </summary>
    [Trait("Tier", "Fast")]
    public sealed class WhBm14aTests
    {
        private const double EarthMu = 3.986004418e14d;
        private const double EarthRadius = 6.371e6d;
        private readonly ITestOutputHelper output;

        public WhBm14aTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void PureKeplerEllipse_IsExactToRoundoff()
        {
            SpacecraftIntegrationState start = StateAtPeriapsis(6.771e6d, 4.2e7d);
            SpacecraftIntegrationState expected = start;
            KeplerUniversalDrift.Propagate(EarthMu, ref expected, 600d);

            SpacecraftOrbitIntegrator.DerivativeFunction central =
                delegate (SpacecraftIntegrationState s, double t)
                {
                    double r2 = s.Position.SqrMagnitude;
                    double r = Math.Sqrt(r2);
                    return new SpacecraftIntegrationState
                    {
                        Position = s.Velocity,
                        Velocity = s.Position * (-EarthMu / (r2 * r)),
                        Mass = 0d
                    };
                };

            SpacecraftIntegrationResult actual = WhBm14aIntegrator.StepForward(
                start, 0d, 600d, central, EarthMu);
            double dp = (actual.State.Position - expected.Position).Magnitude;
            double dv = (actual.State.Velocity - expected.Velocity).Magnitude;
            output.WriteLine("ellipse dp=" + dp.ToString("E3") + " m dv=" + dv.ToString("E3") + " m/s");
            Assert.True(dp < 1e-8d, "position error " + dp.ToString("E3") + " m");
            Assert.True(dv < 1e-11d, "velocity error " + dv.ToString("E3") + " m/s");
        }

        [Fact]
        public void PureKeplerOneOrbit_ReturnsToStart()
        {
            SpacecraftIntegrationState start = StateAtPeriapsis(6.771e6d, 6.771e6d);
            double period = 2d * Math.PI * Math.Sqrt(
                Math.Pow(6.771e6d, 3d) / EarthMu);
            const int steps = 100;
            double h = period / steps;
            SpacecraftIntegrationState state = start;
            for (int i = 0; i < steps; i++)
            {
                state = WhBm14aIntegrator.StepForward(state, i * h, h, Central(EarthMu), EarthMu).State;
            }

            double dp = (state.Position - start.Position).Magnitude;
            double dv = (state.Velocity - start.Velocity).Magnitude;
            output.WriteLine("orbit dp=" + dp.ToString("E3") + " m dv=" + dv.ToString("E3") + " m/s");
            Assert.True(dp < 1e-6d, "position error " + dp.ToString("E3") + " m");
            Assert.True(dv < 1e-9d, "velocity error " + dv.ToString("E3") + " m/s");
        }

        [Fact]
        public void DenseSegmentEndpointsAreBitExact()
        {
            SpacecraftIntegrationState start = StateAtPeriapsis(6.771e6d, 4.2e7d);
            var segments = new List<DenseSegment>();
            SpacecraftIntegrationResult result = WhBm14aIntegrator.StepForward(
                start, 0d, 600d, Central(EarthMu), EarthMu, segments);

            Assert.Single(segments);
            DenseSegment segment = segments[0];
            Assert.Equal(start.Position.X, segment.Evaluate(0d).Position.X);
            Assert.Equal(start.Position.Y, segment.Evaluate(0d).Position.Y);
            Assert.Equal(start.Position.Z, segment.Evaluate(0d).Position.Z);
            Assert.Equal(result.State.Position.X, segment.Evaluate(600d).Position.X);
            Assert.Equal(result.State.Position.Y, segment.Evaluate(600d).Position.Y);
            Assert.Equal(result.State.Position.Z, segment.Evaluate(600d).Position.Z);
        }

        [Fact]
        public void MassFlow_IsRejected()
        {
            SpacecraftIntegrationState start = StateAtPeriapsis(6.771e6d, 4.2e7d);
            SpacecraftOrbitIntegrator.DerivativeFunction flowing =
                delegate (SpacecraftIntegrationState s, double t)
                {
                    return new SpacecraftIntegrationState
                    {
                        Position = s.Velocity,
                        Velocity = Vector3d.Zero,
                        Mass = -1e-3d
                    };
                };

            Assert.Throws<InvalidOperationException>(delegate
            {
                WhBm14aIntegrator.StepForward(start, 0d, 600d, flowing, EarthMu);
            });
        }

        private static SpacecraftOrbitIntegrator.DerivativeFunction Central(double mu)
        {
            return delegate (SpacecraftIntegrationState s, double t)
            {
                double r2 = s.Position.SqrMagnitude;
                double r = Math.Sqrt(r2);
                return new SpacecraftIntegrationState
                {
                    Position = s.Velocity,
                    Velocity = s.Position * (-mu / (r2 * r)),
                    Mass = 0d
                };
            };
        }

        private static SpacecraftIntegrationState StateAtPeriapsis(double rp, double ra)
        {
            double a = 0.5d * (rp + ra);
            double vp = Math.Sqrt(EarthMu * ((2d / rp) - (1d / a)));
            return new SpacecraftIntegrationState
            {
                Position = new Vector3d(rp, 0d, 0d),
                Velocity = new Vector3d(0d, vp, 0d),
                Mass = 1000d
            };
        }
    }
}
