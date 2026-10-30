using System;
using GalilegoPhysicsTests.Support;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Kepler
{
    /// <summary>
    /// Core has no multi-body state (SpacecraftOrbitIntegrator integrates one
    /// spacecraft state). The restricted three-body matrix scenario is the
    /// production path; this test exercises the independent Taylor N-body oracle
    /// on the classical Pythagorean three-body problem so the oracle itself is
    /// pinned to the known conservation laws. It never calls Core.
    /// </summary>
    public sealed class PythagoreanOracleTests
    {
        private readonly ITestOutputHelper output;

        public PythagoreanOracleTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SlowFact]
        [Trait("Tier", "Slow")]
        public void TaylorOracleConservesThePythagoreanInvariants()
        {
            var input = new TaylorNBodyInput
            {
                Names = new[] { "m3", "m4", "m5" },
                Mu = new[] { 3d, 4d, 5d },
                P0 = new[] { 1d, 3d, 0d, -2d, -1d, 0d, 1d, -1d, 0d },
                V0 = new double[9]
            };

            TaylorNBodyResult result = TaylorNBody.RunSeconds(
                input, 70d, order: 20, positionTolerance: 1e-12, velocityTolerance: 1e-12,
                maxStep: 0.01, minStep: 1e-14, sampleEvery: 0d);

            double energy0 = Energy(input.P0, input.V0, input.Mu);
            double energy1 = Energy(result.P, result.V, input.Mu);
            double relativeEnergyDrift = Math.Abs(energy1 - energy0) / Math.Max(1d, Math.Abs(energy0));
            double momentumDrift = MomentumDrift(input.V0, result.V, input.Mu);
            double angularMomentumDrift = AngularMomentumDrift(input.P0, input.V0, result.P, result.V, input.Mu);

            output.WriteLine("Pythagorean: steps=" + result.Steps +
                             ", energy drift=" + relativeEnergyDrift.ToString("E3") +
                             ", momentum drift=" + momentumDrift.ToString("E3") +
                             ", angular drift=" + angularMomentumDrift.ToString("E3") + ".");

            Asserts.True(result.P.Length == 9 && result.V.Length == 9, "Taylor result shape");
            Asserts.True(relativeEnergyDrift < 1e-7, "Pythagorean energy drift " + relativeEnergyDrift.ToString("E3"));
            Asserts.True(momentumDrift < 1e-9, "Pythagorean momentum drift " + momentumDrift.ToString("E3"));
            Asserts.True(angularMomentumDrift < 1e-9, "Pythagorean angular momentum drift " + angularMomentumDrift.ToString("E3"));
        }

        private static double Energy(double[] p, double[] v, double[] mu)
        {
            int n = mu.Length;
            double kinetic = 0d;
            for (int i = 0; i < n; i++)
            {
                kinetic += 0.5d * mu[i] * ((v[3 * i] * v[3 * i]) + (v[(3 * i) + 1] * v[(3 * i) + 1]) + (v[(3 * i) + 2] * v[(3 * i) + 2]));
            }

            double potential = 0d;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dx = p[3 * i] - p[3 * j];
                    double dy = p[(3 * i) + 1] - p[(3 * j) + 1];
                    double dz = p[(3 * i) + 2] - p[(3 * j) + 2];
                    potential += mu[i] * mu[j] / Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                }
            }

            return kinetic - potential;
        }

        private static double MomentumDrift(double[] v0, double[] v1, double[] mu)
        {
            double worst = 0d;
            for (int axis = 0; axis < 3; axis++)
            {
                double momentum0 = 0d;
                double momentum1 = 0d;
                for (int i = 0; i < mu.Length; i++)
                {
                    momentum0 += mu[i] * v0[(3 * i) + axis];
                    momentum1 += mu[i] * v1[(3 * i) + axis];
                }

                worst = Math.Max(worst, Math.Abs(momentum1 - momentum0) / Math.Max(1d, Math.Abs(momentum0)));
            }

            return worst;
        }

        private static double AngularMomentumDrift(double[] p0, double[] v0, double[] p1, double[] v1, double[] mu)
        {
            double[] l0 = AngularMomentum(p0, v0, mu);
            double[] l1 = AngularMomentum(p1, v1, mu);
            double scale = Math.Max(1d, Math.Sqrt((l0[0] * l0[0]) + (l0[1] * l0[1]) + (l0[2] * l0[2])));
            double dx = l1[0] - l0[0];
            double dy = l1[1] - l0[1];
            double dz = l1[2] - l0[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / scale;
        }

        private static double[] AngularMomentum(double[] p, double[] v, double[] mu)
        {
            var result = new double[3];
            for (int i = 0; i < mu.Length; i++)
            {
                double x = p[3 * i];
                double y = p[(3 * i) + 1];
                double z = p[(3 * i) + 2];
                double vx = v[3 * i];
                double vy = v[(3 * i) + 1];
                double vz = v[(3 * i) + 2];
                result[0] += mu[i] * ((y * vz) - (z * vy));
                result[1] += mu[i] * ((z * vx) - (x * vz));
                result[2] += mu[i] * ((x * vy) - (y * vx));
            }

            return result;
        }
    }
}
