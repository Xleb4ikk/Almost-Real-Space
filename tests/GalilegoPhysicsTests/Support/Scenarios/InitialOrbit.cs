using System;
using GalilegoPhysicsTests.Support.Oracles;

namespace GalilegoPhysicsTests.Support.Scenarios
{
    /// <summary>
    /// Builds a two-body state from classical elements. Independent of
    /// OrbitalElements on purpose: the round-trip test needs an outside source
    /// of truth for the initial state.
    /// </summary>
    internal static class InitialOrbit
    {
        public static void StateFromElements(
            double mu,
            double semiMajorAxis,
            double eccentricity,
            double inclinationDegrees,
            double raanDegrees,
            double argumentOfPeriapsisDegrees,
            double trueAnomalyDegrees,
            out Double3 position,
            out Double3 velocity)
        {
            double inc = inclinationDegrees * Math.PI / 180d;
            double raan = raanDegrees * Math.PI / 180d;
            double argp = argumentOfPeriapsisDegrees * Math.PI / 180d;
            double nu = trueAnomalyDegrees * Math.PI / 180d;

            double p = semiMajorAxis * (1d - (eccentricity * eccentricity));
            if (!(p > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(eccentricity), "p = a(1-e²) must be positive.");
            }

            double radius = p / (1d + (eccentricity * Math.Cos(nu)));
            double speedScale = Math.Sqrt(mu / p);

            Double3 perifocalPosition = new Double3(radius * Math.Cos(nu), radius * Math.Sin(nu), 0d);
            Double3 perifocalVelocity = new Double3(
                -speedScale * Math.Sin(nu),
                speedScale * (eccentricity + Math.Cos(nu)),
                0d);

            position = RotateZ(RotateX(RotateZ(perifocalPosition, argp), inc), raan);
            velocity = RotateZ(RotateX(RotateZ(perifocalVelocity, argp), inc), raan);
        }

        public static double Period(double mu, double semiMajorAxis)
        {
            return 2d * Math.PI * Math.Sqrt(semiMajorAxis * semiMajorAxis * semiMajorAxis / mu);
        }

        private static Double3 RotateX(Double3 v, double angle)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return new Double3(v.X, (v.Y * c) - (v.Z * s), (v.Y * s) + (v.Z * c));
        }

        private static Double3 RotateZ(Double3 v, double angle)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return new Double3((v.X * c) - (v.Y * s), (v.X * s) + (v.Y * c), v.Z);
        }
    }
}
