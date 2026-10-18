using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Pure math for converting Keplerian elements to position/velocity. An independent
    /// reusable utility with no dependency on a particular system.
    /// </summary>
    public static class KeplerMath
    {
        /// <summary>
        /// Solves Kepler's equation M = E - e·sin(E) by Newton's method (8 iterations)
        /// with a residual check and a bisection fallback. The bracket [M-e, M+e] is valid
        /// for any M: f grows strictly for e&lt;1 (f'=1-e·cosE ≥ 1-e &gt; 0), so
        /// f(M-e) ≤ 0 ≤ f(M+e) always. For e∉[0,1) only Newton runs.
        /// </summary>
        public static double SolveEccentricAnomaly(double meanAnomaly, double eccentricity)
        {
            double estimate = eccentricity < 0.8d ? meanAnomaly : Math.PI;

            for (int i = 0; i < 8; i++)
            {
                double function = estimate - (eccentricity * Math.Sin(estimate)) - meanAnomaly;
                double derivative = 1d - (eccentricity * Math.Cos(estimate));
                estimate -= function / derivative;
            }

            if (eccentricity >= 0d && eccentricity < 1d)
            {
                double residual = estimate - (eccentricity * Math.Sin(estimate)) - meanAnomaly;
                if (Math.Abs(residual) > 1e-12d)
                {
                    estimate = BisectKepler(meanAnomaly, eccentricity);
                }
            }

            return estimate;
        }

        private static double BisectKepler(double meanAnomaly, double eccentricity)
        {
            double lo = meanAnomaly - eccentricity;
            double hi = meanAnomaly + eccentricity;
            for (int i = 0; i < 100; i++)
            {
                double mid = 0.5d * (lo + hi);
                double f = mid - (eccentricity * Math.Sin(mid)) - meanAnomaly;
                if (f > 0d)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            return 0.5d * (lo + hi);
        }

        /// <summary>
        /// Rotates from the orbit's perifocal plane into the inertial frame via
        /// R3(-Ω)·R1(-i)·R3(-ω). The result is in the astrodynamics frame (Z = north of
        /// the reference plane). Conversion to Unity coordinates (Y-up) is done separately
        /// at the render layer — that concern is deliberately not mixed into physics here.
        /// </summary>
        public static Vector3d RotateOrbitalToWorld(Vector3d vector, double ascendingNode, double inclination, double periapsis)
        {
            double cosOmega = Math.Cos(ascendingNode);
            double sinOmega = Math.Sin(ascendingNode);
            double cosI = Math.Cos(inclination);
            double sinI = Math.Sin(inclination);
            double cosW = Math.Cos(periapsis);
            double sinW = Math.Sin(periapsis);

            double x =
                ((cosOmega * cosW) - (sinOmega * sinW * cosI)) * vector.X +
                ((-cosOmega * sinW) - (sinOmega * cosW * cosI)) * vector.Y;

            double y =
                ((sinOmega * cosW) + (cosOmega * sinW * cosI)) * vector.X +
                ((-sinOmega * sinW) + (cosOmega * cosW * cosI)) * vector.Y;

            double z =
                (sinW * sinI * vector.X) +
                (cosW * sinI * vector.Y);

            return new Vector3d(x, y, z);
        }

        public static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

        public static double NormalizeAngle(double angle)
        {
            double twoPi = Math.PI * 2d;
            angle %= twoPi;
            return angle < 0d ? angle + twoPi : angle;
        }

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
