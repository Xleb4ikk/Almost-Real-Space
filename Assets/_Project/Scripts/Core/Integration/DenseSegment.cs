using System;
using System.Collections.Generic;

namespace Galilego.Core
{
    /// <summary>
    /// Shampine dense output coefficients of one accepted step:
    /// C1=y1-y0, C2=h·k1-C1, C3=C1-C2-h·k7, C4=h·Σ(D·k). Computed inside
    /// DoPri5Step from the already available stages (k7 — FSAL estimate f(t0+h,y1)):
    /// 0 extra RHS evaluations per segment.
    /// </summary>
    public readonly struct ShampineCoeffs
    {
        public readonly SpacecraftIntegrationState C1;
        public readonly SpacecraftIntegrationState C2;
        public readonly SpacecraftIntegrationState C3;
        public readonly SpacecraftIntegrationState C4;

        public ShampineCoeffs(
            SpacecraftIntegrationState c1, SpacecraftIntegrationState c2,
            SpacecraftIntegrationState c3, SpacecraftIntegrationState c4)
        {
            C1 = c1;
            C2 = c2;
            C3 = c3;
            C4 = c4;
        }
    }

    /// <summary>
    /// Dense output of one accepted inner DOPRI5 step per Shampine (1986):
    /// y(t0+θh) = C0 + θ·(C1 + (1-θ)·(C2 + θ·(C3 + (1-θ)·C4))).
    /// The segment is per-step: 4th-order Shampine with small constants and
    /// 0 extra RHS evaluations.
    ///
    /// Ends are bit-exact (θ≤0 → Y0, θ≥1 → Y1 from storage, not the formula).
    /// Forward only: the drivers run forward, h is positive.
    /// </summary>
    public readonly struct DenseSegment
    {
        public readonly double T0;
        public readonly double T1;
        public readonly SpacecraftIntegrationState Y0;
        public readonly SpacecraftIntegrationState Y1;

        private readonly double h;
        private readonly SpacecraftIntegrationState c1;
        private readonly SpacecraftIntegrationState c2;
        private readonly SpacecraftIntegrationState c3;
        private readonly SpacecraftIntegrationState c4;

        public DenseSegment(
            double t0, double t1, double h,
            SpacecraftIntegrationState y0, SpacecraftIntegrationState y1,
            ShampineCoeffs coeffs)
        {
            T0 = t0;
            T1 = t1;
            this.h = h;
            Y0 = y0;
            Y1 = y1;
            c1 = coeffs.C1;
            c2 = coeffs.C2;
            c3 = coeffs.C3;
            c4 = coeffs.C4;
        }

        public SpacecraftIntegrationState Evaluate(double t)
        {
            if (t <= T0)
            {
                return Y0;
            }

            if (t >= T1)
            {
                return Y1;
            }

            double theta = (t - T0) / h;
            double theta1 = 1d - theta;
            return new SpacecraftIntegrationState
            {
                Position = Poly(Y0.Position, c1.Position, c2.Position, c3.Position, c4.Position, theta, theta1),
                Velocity = Poly(Y0.Velocity, c1.Velocity, c2.Velocity, c3.Velocity, c4.Velocity, theta, theta1),
                Mass = Poly(Y0.Mass, c1.Mass, c2.Mass, c3.Mass, c4.Mass, theta, theta1)
            };
        }

        private static Vector3d Poly(Vector3d c0, Vector3d c1, Vector3d c2, Vector3d c3, Vector3d c4, double theta, double theta1)
        {
            return c0 + (c1 + (c2 + (c3 + c4 * theta1) * theta) * theta1) * theta;
        }

        private static double Poly(double c0, double c1, double c2, double c3, double c4, double theta, double theta1)
        {
            return c0 + (c1 + (c2 + (c3 + c4 * theta1) * theta) * theta1) * theta;
        }

        /// <summary>
        /// Track state at time t. The track from the collector is ordered;
        /// micro-gaps between chunks (≤MinStepSize, StepForward remainder) are
        /// tolerated: a point in a gap is evaluated by the neighbouring segment
        /// (error ≤ v·1e-6). An empty track throws instead of silently returning nothing.
        /// </summary>
        public static SpacecraftIntegrationState EvaluateTrack(IReadOnlyList<DenseSegment> segments, double t)
        {
            if (segments == null || segments.Count == 0)
            {
                throw new InvalidOperationException("Трек сегментов пуст — интерполировать нечего.");
            }

            int lo = 0;
            int hi = segments.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (segments[mid].T0 <= t)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            DenseSegment seg = segments[lo];
            if (t > seg.T1 && lo + 1 < segments.Count && t >= segments[lo + 1].T0)
            {
                seg = segments[lo + 1];
            }

            return seg.Evaluate(t);
        }
    }
}
