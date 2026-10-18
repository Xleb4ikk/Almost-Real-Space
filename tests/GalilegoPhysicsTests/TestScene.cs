using Galilego.Core;
using Galilego.Universe;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Shared test scene: a single central body with a known μ at the origin,
    /// and a ship on a circular orbit of radius a in the XY plane.
    /// </summary>
    internal static class TestScene
    {
        public const double Mu = 3.986004418e14;
        public const double OrbitRadius = 7.0e6;
        public const double ShipMass = 1000.0;

        public static StarSystem CreateSingleBodySystem(double mu = Mu)
        {
            var star = new OrbitingBody
            {
                Name = "TestBody",
                StandardGravitationalParameter = mu
            };
            return new StarSystem(star);
        }

        public static void CircularOrbit(out Vector3d position, out Vector3d velocity, double radius = OrbitRadius, double mu = Mu)
        {
            position = new Vector3d(radius, 0d, 0d);
            double v = System.Math.Sqrt(mu / radius);
            velocity = new Vector3d(0d, v, 0d);
        }

        public static double CircularPeriod(double radius = OrbitRadius, double mu = Mu)
        {
            return 2d * System.Math.PI * System.Math.Sqrt(radius * radius * radius / mu);
        }

        /// <summary>
        /// Elliptical orbit: start at periapsis (rp, 0, 0) with velocity upward.
        /// </summary>
        public static void EllipticalOrbit(double periapsis, double apoapsis, double mu,
            out Vector3d position, out Vector3d velocity)
        {
            double a = 0.5d * (periapsis + apoapsis);
            double e = (apoapsis - periapsis) / (apoapsis + periapsis);
            position = new Vector3d(periapsis, 0d, 0d);
            double vp = System.Math.Sqrt(mu * (1d + e) / (a * (1d - e)));
            velocity = new Vector3d(0d, vp, 0d);
        }

        public static double EllipticalPeriod(double periapsis, double apoapsis, double mu)
        {
            double a = 0.5d * (periapsis + apoapsis);
            return 2d * System.Math.PI * System.Math.Sqrt(a * a * a / mu);
        }

        public static double RelativeError(Vector3d a, Vector3d b)
        {
            double denom = System.Math.Max(a.Magnitude, b.Magnitude);
            if (denom == 0d)
                return (a - b).Magnitude;
            return (a - b).Magnitude / denom;
        }
    }
}
