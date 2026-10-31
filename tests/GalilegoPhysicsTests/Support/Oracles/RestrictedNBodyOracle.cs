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
            double durationSeconds,
            double stepSeconds = 0.5d)
        {
            // Independent fixed-step RK4. The close-flyby scenario uses a smaller
            // step; the generic 1 h particle uses 0.5 s.
            return Integrate(r0, v0, centralMu, bodies, durationSeconds, stepSeconds);
        }

        public static RestrictedNBodyTrajectory Build(
            Double3 r0,
            Double3 v0,
            double centralMu,
            RestrictedBody[] bodies,
            double durationSeconds,
            double stepSeconds)
        {
            int steps = (int)Math.Ceiling(durationSeconds / stepSeconds);
            double h = durationSeconds / steps;
            var positions = new Double3[steps + 1];
            var velocities = new Double3[steps + 1];
            Double3 r = r0;
            Double3 v = v0;
            positions[0] = r;
            velocities[0] = v;
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
                positions[i + 1] = r;
                velocities[i + 1] = v;
            }

            return new RestrictedNBodyTrajectory(0d, durationSeconds, h, positions, velocities);
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

    internal sealed class RestrictedNBodyTrajectory
    {
        private readonly double t0;
        private readonly double t1;
        private readonly double h;
        private readonly Double3[] positions;
        private readonly Double3[] velocities;

        public RestrictedNBodyTrajectory(
            double t0,
            double t1,
            double h,
            Double3[] positions,
            Double3[] velocities)
        {
            this.t0 = t0;
            this.t1 = t1;
            this.h = h;
            this.positions = positions;
            this.velocities = velocities;
        }

        public OracleState Evaluate(double time)
        {
            if (time <= t0)
            {
                return new OracleState(positions[0], velocities[0]);
            }

            if (time >= t1)
            {
                int last = positions.Length - 1;
                return new OracleState(positions[last], velocities[last]);
            }

            int index = (int)((time - t0) / h);
            if (index < 0)
            {
                index = 0;
            }

            if (index > positions.Length - 2)
            {
                index = positions.Length - 2;
            }

            double s = (time - (t0 + (index * h))) / h;
            double s2 = s * s;
            double s3 = s2 * s;
            double h00 = (2d * s3) - (3d * s2) + 1d;
            double h10 = s3 - (2d * s2) + s;
            double h01 = (-2d * s3) + (3d * s2);
            double h11 = s3 - s2;
            Double3 p = (positions[index] * h00) + (velocities[index] * (h * h10)) +
                        (positions[index + 1] * h01) + (velocities[index + 1] * (h * h11));
            double dh00 = (6d * s2) - (6d * s);
            double dh10 = (3d * s2) - (4d * s) + 1d;
            double dh01 = (-6d * s2) + (6d * s);
            double dh11 = (3d * s2) - (2d * s);
            Double3 v = (positions[index] * (dh00 / h)) + (velocities[index] * dh10) +
                        (positions[index + 1] * (dh01 / h)) + (velocities[index + 1] * dh11);
            return new OracleState(p, v);
        }
    }
}
