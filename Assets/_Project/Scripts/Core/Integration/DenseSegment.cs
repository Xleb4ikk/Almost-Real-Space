using System;
using System.Collections.Generic;

namespace Galilego.Core
{
    /// <summary>
    /// Dense output coefficients of one accepted step.
    ///
    /// DOPRI5 (Shampine form): C1=y1-y0, C2=h*k1-C1, C3=C1-C2-h*k7,
    /// C4=h*(D*k). Computed from the already available stages (k7 is the FSAL
    /// estimate f(t0+h,y1)); 0 extra RHS evaluations per segment.
    ///
    /// DOP853 (Hairer/Wanner contd8 form): C1..C7 are F0..F6; the constructor
    /// marks the segment as DOP853 and the evaluator uses the mixed
    /// theta/(1-theta) polynomial of the 8th-order dense output.
    /// </summary>
    public readonly struct ShampineCoeffs
    {
        public readonly SpacecraftIntegrationState C1;
        public readonly SpacecraftIntegrationState C2;
        public readonly SpacecraftIntegrationState C3;
        public readonly SpacecraftIntegrationState C4;
        public readonly SpacecraftIntegrationState C5;
        public readonly SpacecraftIntegrationState C6;
        public readonly SpacecraftIntegrationState C7;
        public readonly bool IsDop853;

        public ShampineCoeffs(
            SpacecraftIntegrationState c1, SpacecraftIntegrationState c2,
            SpacecraftIntegrationState c3, SpacecraftIntegrationState c4)
        {
            C1 = c1;
            C2 = c2;
            C3 = c3;
            C4 = c4;
            C5 = default;
            C6 = default;
            C7 = default;
            IsDop853 = false;
        }

        public ShampineCoeffs(
            SpacecraftIntegrationState c1, SpacecraftIntegrationState c2,
            SpacecraftIntegrationState c3, SpacecraftIntegrationState c4,
            SpacecraftIntegrationState c5, SpacecraftIntegrationState c6,
            SpacecraftIntegrationState c7)
        {
            C1 = c1;
            C2 = c2;
            C3 = c3;
            C4 = c4;
            C5 = c5;
            C6 = c6;
            C7 = c7;
            IsDop853 = true;
        }
    }

    /// <summary>
    /// Dense output of one accepted inner DOPRI5/DOP853 step.
    ///
    /// DOPRI5 uses the 4th-order Shampine segment:
    /// y(t0+theta*h) = Y0 + theta*(C1 + phi*(C2 + theta*(C3 + phi*C4))),
    /// built with 0 extra RHS evaluations.
    ///
    /// DOP853 uses the 8th-order Hairer/Wanner contd8 segment:
    /// y = Y0 + theta*(F0 + phi*(F1 + theta*(F2 + phi*(F3 + theta*(F4 + phi*(F5 + theta*F6)))))),
    /// built from the 12 method stages, the FSAL stage and three extended stages
    /// (3 extra RHS evaluations per accepted step when a collector is supplied).
    ///
    /// Ends are bit-exact (theta=0 returns Y0, theta=1 returns Y1 from storage,
    /// not from the formula). Forward only: the drivers run forward, h is positive.
    /// </summary>
    public readonly struct DenseSegment
    {
        public readonly double T0;
        public readonly double T1;
        public readonly SpacecraftIntegrationState Y0;
        public readonly SpacecraftIntegrationState Y1;

        // Non-null for a segment integrated in the dominant body's local frame.
        // Y0/Y1 stay ABSOLUTE (the renderer and validators read them directly);
        // the polynomial stays LOCAL and Evaluate adds the body state at the query
        // time. This lets the event/warp path use the local frame without an
        // absolute polynomial.
        public readonly IBodyStateProvider ReferenceBody;
        private readonly SpacecraftIntegrationState localY0;

        private readonly double h;
        private readonly ShampineCoeffs coeffs;

        public DenseSegment(
            double t0, double t1, double h,
            SpacecraftIntegrationState y0, SpacecraftIntegrationState y1,
            ShampineCoeffs coeffs)
            : this(t0, t1, h, y0, y1, coeffs, null, y0)
        {
        }

        private DenseSegment(
            double t0, double t1, double h,
            SpacecraftIntegrationState y0, SpacecraftIntegrationState y1,
            ShampineCoeffs coeffs, IBodyStateProvider referenceBody,
            SpacecraftIntegrationState localY0)
        {
            T0 = t0;
            T1 = t1;
            this.h = h;
            Y0 = y0;
            Y1 = y1;
            ReferenceBody = referenceBody;
            this.localY0 = localY0;
            this.coeffs = coeffs;
        }

        /// <summary>
        /// Convert a local-frame segment to the public absolute representation
        /// without losing dense interpolation: endpoints absolute, polynomial local.
        /// </summary>
        public DenseSegment AsAbsolute(IBodyStateProvider referenceBody)
        {
            if (ReferenceBody != null)
            {
                return this;
            }

            referenceBody.EvaluateWorldState(T0, out Vector3d p0, out Vector3d v0);
            referenceBody.EvaluateWorldState(T1, out Vector3d p1, out Vector3d v1);
            var absolute0 = new SpacecraftIntegrationState
            {
                Position = Y0.Position + p0,
                Velocity = Y0.Velocity + v0,
                Mass = Y0.Mass
            };
            var absolute1 = new SpacecraftIntegrationState
            {
                Position = Y1.Position + p1,
                Velocity = Y1.Velocity + v1,
                Mass = Y1.Mass
            };
            return new DenseSegment(
                T0, T1, h, absolute0, absolute1,
                coeffs, referenceBody, Y0);
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
            SpacecraftIntegrationState origin = ReferenceBody != null ? localY0 : Y0;
            SpacecraftIntegrationState local;
            if (coeffs.IsDop853)
            {
                local = new SpacecraftIntegrationState
                {
                    Position = Poly853(
                        origin.Position,
                        coeffs.C1.Position, coeffs.C2.Position, coeffs.C3.Position, coeffs.C4.Position,
                        coeffs.C5.Position, coeffs.C6.Position, coeffs.C7.Position, theta, theta1),
                    Velocity = Poly853(
                        origin.Velocity,
                        coeffs.C1.Velocity, coeffs.C2.Velocity, coeffs.C3.Velocity, coeffs.C4.Velocity,
                        coeffs.C5.Velocity, coeffs.C6.Velocity, coeffs.C7.Velocity, theta, theta1),
                    Mass = Poly853(
                        origin.Mass,
                        coeffs.C1.Mass, coeffs.C2.Mass, coeffs.C3.Mass, coeffs.C4.Mass,
                        coeffs.C5.Mass, coeffs.C6.Mass, coeffs.C7.Mass, theta, theta1)
                };
            }
            else
            {
                local = new SpacecraftIntegrationState
                {
                    Position = Poly(origin.Position, coeffs.C1.Position, coeffs.C2.Position, coeffs.C3.Position, coeffs.C4.Position, theta, theta1),
                    Velocity = Poly(origin.Velocity, coeffs.C1.Velocity, coeffs.C2.Velocity, coeffs.C3.Velocity, coeffs.C4.Velocity, theta, theta1),
                    Mass = Poly(origin.Mass, coeffs.C1.Mass, coeffs.C2.Mass, coeffs.C3.Mass, coeffs.C4.Mass, theta, theta1)
                };
            }
            if (ReferenceBody == null)
            {
                return local;
            }

            ReferenceBody.EvaluateWorldState(t, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            return new SpacecraftIntegrationState
            {
                Position = local.Position + bodyPosition,
                Velocity = local.Velocity + bodyVelocity,
                Mass = local.Mass
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

        private static Vector3d Poly853(
            Vector3d c0,
            Vector3d f0, Vector3d f1, Vector3d f2, Vector3d f3, Vector3d f4, Vector3d f5, Vector3d f6,
            double theta, double theta1)
        {
            return c0 + theta * (f0 + theta1 * (f1 + theta * (f2 + theta1 * (f3 + theta * (f4 + theta1 * (f5 + theta * f6))))));
        }

        private static double Poly853(
            double c0,
            double f0, double f1, double f2, double f3, double f4, double f5, double f6,
            double theta, double theta1)
        {
            return c0 + theta * (f0 + theta1 * (f1 + theta * (f2 + theta1 * (f3 + theta * (f4 + theta1 * (f5 + theta * f6))))));
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
