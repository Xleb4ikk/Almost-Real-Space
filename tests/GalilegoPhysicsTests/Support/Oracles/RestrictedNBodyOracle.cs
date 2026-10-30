using System;

namespace GalilegoPhysicsTests.Support.Oracles
{
    internal readonly struct RestrictedBody
    {
        public readonly double Mu;
        public readonly double Radius;
        public readonly double Phase;
        public readonly double AngularSpeed;

        public RestrictedBody(double mu, double radius, double phase, double angularSpeed)
        {
            Mu = mu;
            Radius = radius;
            Phase = phase;
            AngularSpeed = angularSpeed;
        }

        public void PositionAt(double time, out double x, out double y)
        {
            double angle = (AngularSpeed * time) + Phase;
            x = Radius * Math.Cos(angle);
            y = Radius * Math.Sin(angle);
        }
    }

    internal static class RestrictedNBodyOracle
    {
        public static RestrictedBody[] CreateBodies(double centralMu)
        {
            return new[]
            {
                new RestrictedBody(3.986004418e14d, 1.0e10d, 0.0d, Math.Sqrt(centralMu / 1.0e30d)),
                new RestrictedBody(1.267127641e17d, 2.0e10d, 1.2d, Math.Sqrt(centralMu / 8.0e30d))
            };
        }

        public static OracleState Propagate(
            Double3 r0,
            Double3 v0,
            double centralMu,
            RestrictedBody[] bodies,
            double durationSeconds)
        {
            // Independent fixed-step RK4 at 0.5 s is the oracle. The acceleration
            // below is written here, not taken from Core.
            return Integrate(r0, v0, centralMu, bodies, durationSeconds, 0.5d);
        }

        private static OracleState Integrate(
            Double3 r0,
            Double3 v0,
            double centralMu,
            RestrictedBody[] bodies,
            double durationSeconds,
            double step)
        {
            Double3 r = r0;
            Double3 v = v0;
            int steps = (int)Math.Ceiling(durationSeconds / step);
            double h = durationSeconds / steps;
            double t = 0d;
            for (int i = 0; i < steps; i++)
            {
                Double3 k1r = v;
                Double3 k1v = Acceleration(r, t, centralMu, bodies);
                Double3 k2r = v + (k1v * (0.5d * h));
                Double3 k2v = Acceleration(r + (k1r * (0.5d * h)), t + (0.5d * h), centralMu, bodies);
                Double3 k3r = v + (k2v * (0.5d * h));
                Double3 k3v = Acceleration(r + (k2r * (0.5d * h)), t + (0.5d * h), centralMu, bodies);
                Double3 k4r = v + (k3v * h);
                Double3 k4v = Acceleration(r + (k3r * h), t + h, centralMu, bodies);
                r += (k1r + (k2r * 2d) + (k3r * 2d) + k4r) * (h / 6d);
                v += (k1v + (k2v * 2d) + (k3v * 2d) + k4v) * (h / 6d);
                t += h;
            }

            return new OracleState(r, v);
        }

        private static Double3 Acceleration(Double3 r, double time, double centralMu, RestrictedBody[] bodies)
        {
            double centralR2 = r.SqrMagnitude;
            Double3 acceleration = r * (-centralMu / (centralR2 * Math.Sqrt(centralR2)));
            foreach (RestrictedBody body in bodies)
            {
                body.PositionAt(time, out double bx, out double by);
                double dx = bx - r.X;
                double dy = by - r.Y;
                double dz = -r.Z;
                double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                double scale = body.Mu / (r2 * Math.Sqrt(r2));
                acceleration += new Double3(dx * scale, dy * scale, dz * scale);
            }

            return acceleration;
        }
    }
}
