using System;
using System.Collections.Generic;
using GalilegoPhysicsTests.Support.Scenarios;

namespace GalilegoPhysicsTests.Support
{
    internal sealed class ToleranceProfile
    {
        public double OraclePositionMeters;
        public double OracleVelocityMetersPerSecond;
        public double OracleMassRelative;
        public double EnergyRelative;
        public double AngularMomentumRelative;
        public double LaplaceRungeLenzAbsolute;
        public double ReversibilityPositionMeters;
        public double ReversibilityVelocityMetersPerSecond;
        public double ChunkingPositionMeters;
        public double ChunkingVelocityMetersPerSecond;
        public double DenseInterpolationMeters;

        public ToleranceProfile Scale(double factor)
        {
            return new ToleranceProfile
            {
                OraclePositionMeters = OraclePositionMeters * factor,
                OracleVelocityMetersPerSecond = OracleVelocityMetersPerSecond * factor,
                OracleMassRelative = OracleMassRelative * factor,
                EnergyRelative = EnergyRelative * factor,
                AngularMomentumRelative = AngularMomentumRelative * factor,
                LaplaceRungeLenzAbsolute = LaplaceRungeLenzAbsolute * factor,
                ReversibilityPositionMeters = ReversibilityPositionMeters * factor,
                ReversibilityVelocityMetersPerSecond = ReversibilityVelocityMetersPerSecond * factor,
                ChunkingPositionMeters = ChunkingPositionMeters * factor,
                ChunkingVelocityMetersPerSecond = ChunkingVelocityMetersPerSecond * factor,
                DenseInterpolationMeters = DenseInterpolationMeters * factor
            };
        }
    }

    /// <summary>
    /// The single place where assertion tolerances live. Base values are measured
    /// at requested integrator tolerance 1e-9 on the full fast matrix, then given
    /// 10x headroom; see TestResults/calibration.md. Scale(tol) applies the
    /// requested-tolerance ratio (measured errors generally grow with tolerance,
    /// so the scaled table stays conservative for loose and tight runs).
    /// </summary>
    internal static class ToleranceTable
    {
        private static readonly Dictionary<(IntegratorKind, ScenarioClass), ToleranceProfile> Base =
            new Dictionary<(IntegratorKind, ScenarioClass), ToleranceProfile>
            {
                [(IntegratorKind.Dopri5, ScenarioClass.KeplerCircular)] =
                    Profile(7.60e-1, 8.20e-4, 7.550e-9, 1.132e-8, 2.460e-8, 3.894, 3.894, 3.894),
                [(IntegratorKind.Dopri5, ScenarioClass.KeplerEccentric)] =
                    Profile(4.509, 6.249e-3, 6.708e-9, 2.061e-8, 4.511e-8, 45.09, 45.09, 45.09),
                [(IntegratorKind.Dopri5, ScenarioClass.KeplerHighEccentric)] =
                    Profile(1.628e3, 7.286e2, 3.834e-9, 3.762e-8, 5.216e-8, 1.628e4, 1.628e4, 1.628e4),
                [(IntegratorKind.Dopri5, ScenarioClass.KeplerHyperbolic)] =
                    Profile(1.298e-1, 1.516e-4, 1.163e-8, 3.236e-8, 7.475e-8, 1.298, 1.298, 1.298),
                [(IntegratorKind.Dopri5, ScenarioClass.KeplerNearParabolic)] =
                    Profile(8.598e-1, 1.164e-2, 3.195e-9, 3.316e-8, 2.154e-8, 8.598, 8.598, 8.598),
                [(IntegratorKind.Dopri5, ScenarioClass.RestrictedNBody)] =
                    Profile(1.068e-3, 5.124e-9, 7.381e-5, 1.107e-4, 2.274e-4, 1.068e-2, 1.068e-2, 1.068e-2),
                [(IntegratorKind.Dopri5, ScenarioClass.Thrust)] =
                    Profile(1.905e-6, 7.723e-9, 0d, 0d, 0d, 1.905e-5, 1.905e-5, 1.905e-5, 2.526e-15),
                [(IntegratorKind.Dopri5, ScenarioClass.Drift)] =
                    Profile(1.118e-7, 1e-9, 0d, 0d, 0d, 1.118e-6, 1.118e-6, 1.118e-6),

                [(IntegratorKind.Dop853, ScenarioClass.KeplerCircular)] =
                    Profile(5.838e1, 6.293e-2, 1.112e-5, 1.668e-5, 2.502e-5, 5.838e2, 5.838e2, 5.838e2),
                [(IntegratorKind.Dop853, ScenarioClass.KeplerEccentric)] =
                    Profile(1.479e2, 3.137e-1, 2.623e-5, 5.528e-5, 9.024e-5, 1.479e3, 1.479e3, 1.479e3),
                [(IntegratorKind.Dop853, ScenarioClass.KeplerHighEccentric)] =
                    Profile(7.982e1, 2.635e-1, 7.906e-7, 3.538e-5, 2.787e-5, 7.982e2, 7.982e2, 7.982e2),
                [(IntegratorKind.Dop853, ScenarioClass.KeplerHyperbolic)] =
                    Profile(4.743e1, 1.377e-1, 8.763e-6, 1.727e-5, 4.925e-5, 4.743e1, 4.743e1, 4.743e1),
                [(IntegratorKind.Dop853, ScenarioClass.KeplerNearParabolic)] =
                    Profile(3.170, 4.271e-1, 2.851e-7, 1.024e-5, 2.555e-6, 3.170, 3.170, 3.170),
                [(IntegratorKind.Dop853, ScenarioClass.RestrictedNBody)] =
                    Profile(7.838e-2, 4.904e-7, 7.381e-5, 1.107e-4, 2.274e-4, 7.838e-1, 7.838e-1, 7.838e-1),
                [(IntegratorKind.Dop853, ScenarioClass.Thrust)] =
                    Profile(3.514e-2, 1.082e-4, 0d, 0d, 0d, 3.514e-1, 3.514e-1, 3.514e-1, 3.790e-15),
                [(IntegratorKind.Dop853, ScenarioClass.Drift)] =
                    Profile(1e-8, 1e-10, 0d, 0d, 0d, 1e-7, 1e-7, 1e-7),

                [(IntegratorKind.Dopri5, ScenarioClass.KeplerCircularLong)] =
                    Profile(7.996e3, 8.619, 8.111e-7, 1.217e-6, 2.345e-8, 7.996e4, 7.996e4, 7.996e4),
                [(IntegratorKind.Dopri5, ScenarioClass.KeplerEccentricLong)] =
                    Profile(4.292e3, 6.084, 1.528e-7, 3.959e-7, 9.979e-7, 4.292e4, 4.292e4, 4.292e4),
                [(IntegratorKind.Dopri5, ScenarioClass.KeplerHighEccentricLong)] =
                    Profile(5.86e2, 2.828e1, 2.185e-8, 7.929e-8, 1.498e-7, 2.093e3, 2.093e3, 2.093e3),
                [(IntegratorKind.Dopri5, ScenarioClass.KeplerHyperbolicLong)] =
                    Profile(4.345, 1.064e-4, 5.104e-9, 3.356e-8, 5.266e-8, 4.345e1, 4.345e1, 4.345e1),

                [(IntegratorKind.Dop853, ScenarioClass.KeplerCircularLong)] =
                    Profile(1.262e2, 1.361e-1, 2.405e-5, 3.607e-5, 5.411e-5, 1.262e3, 1.262e3, 1.262e3),
                [(IntegratorKind.Dop853, ScenarioClass.KeplerEccentricLong)] =
                    Profile(3.002e2, 5.180e-1, 4.320e-5, 1.091e-4, 1.487e-4, 3.002e3, 3.002e3, 3.002e3),
                [(IntegratorKind.Dop853, ScenarioClass.KeplerHighEccentricLong)] =
                    Profile(1.374e2, 6.807e-1, 2.925e-5, 6.653e-5, 1.131e-4, 1.374e3, 1.374e3, 1.374e3),
                [(IntegratorKind.Dop853, ScenarioClass.KeplerHyperbolicLong)] =
                    Profile(1.379e2, 3.831e-2, 1.103e-6, 8.961e-6, 1.338e-5, 1.379e3, 1.379e3, 1.379e3),

                [(IntegratorKind.Dop853, ScenarioClass.GoldenCircular10y)] =
                    Profile(3.869e4, 4.171e1, 1d, 1d, 1d, 3.869e5, 3.869e5, 3.869e5),
                [(IntegratorKind.Dop853, ScenarioClass.GoldenEccentric1y)] =
                    Profile(3.890e2, 5.638e-1, 1d, 1d, 1d, 3.890e3, 3.890e3, 3.890e3)
            };

        private const double HeadroomDivisor = 3d;

        public static ToleranceProfile For(IntegratorKind integrator, ScenarioClass scenarioClass, double tolerance)
        {
            if (!Base.TryGetValue((integrator, scenarioClass), out ToleranceProfile profile))
            {
                throw new ArgumentOutOfRangeException(nameof(scenarioClass));
            }

            double factor = tolerance / 1e-9d;
            // Below 1e-2 of the reference the observed error stops scaling
            // linearly (step-control floor and round-off); do not scale the
            // already measured 10x headroom below that.
            if (factor < 1e-2d)
            {
                factor = 1e-2d;
            }

            return profile.Scale(factor / HeadroomDivisor);
        }

        private static ToleranceProfile Profile(
            double oraclePosition,
            double oracleVelocity,
            double energy,
            double angular,
            double lrl,
            double reversibilityPosition,
            double chunkingPosition,
            double dense,
            double mass = 0d)
        {
            return new ToleranceProfile
            {
                OraclePositionMeters = oraclePosition,
                OracleVelocityMetersPerSecond = oracleVelocity,
                OracleMassRelative = mass,
                EnergyRelative = energy,
                AngularMomentumRelative = angular,
                LaplaceRungeLenzAbsolute = lrl,
                ReversibilityPositionMeters = reversibilityPosition,
                ReversibilityVelocityMetersPerSecond = reversibilityPosition * 1e-3,
                ChunkingPositionMeters = chunkingPosition,
                ChunkingVelocityMetersPerSecond = chunkingPosition * 1e-4,
                DenseInterpolationMeters = dense
            };
        }
    }
}


