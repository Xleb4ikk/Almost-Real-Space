using System;
using Galilego.Core;

namespace GalilegoPhysicsTests.Support.Oracles
{
    internal readonly struct Double3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Double3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Double3 Zero = new Double3(0d, 0d, 0d);

        public double SqrMagnitude => (X * X) + (Y * Y) + (Z * Z);
        public double Magnitude => Math.Sqrt(SqrMagnitude);

        public static Double3 operator +(Double3 a, Double3 b) => new Double3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Double3 operator -(Double3 a, Double3 b) => new Double3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Double3 operator -(Double3 a) => new Double3(-a.X, -a.Y, -a.Z);
        public static Double3 operator *(Double3 a, double s) => new Double3(a.X * s, a.Y * s, a.Z * s);
        public static Double3 operator *(double s, Double3 a) => a * s;
        public static Double3 operator /(Double3 a, double s) => new Double3(a.X / s, a.Y / s, a.Z / s);

        public static double Dot(Double3 a, Double3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        public static Double3 Cross(Double3 a, Double3 b) => new Double3(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

        public Double3 Normalized => Magnitude > 0d ? this / Magnitude : Zero;

        public Vector3d ToVector3d() => new Vector3d(X, Y, Z);

        public override string ToString() => "(" + X.ToString("R") + ", " + Y.ToString("R") + ", " + Z.ToString("R") + ")";
    }

    internal readonly struct OracleState
    {
        public readonly Double3 Position;
        public readonly Double3 Velocity;
        public readonly double Mass;

        public OracleState(Double3 position, Double3 velocity, double mass)
        {
            Position = position;
            Velocity = velocity;
            Mass = mass;
        }

        public OracleState(Double3 position, Double3 velocity)
            : this(position, velocity, 0d)
        {
        }

        public SpacecraftIntegrationState ToState(double mass)
        {
            return new SpacecraftIntegrationState
            {
                Position = Position.ToVector3d(),
                Velocity = Velocity.ToVector3d(),
                Mass = mass
            };
        }
    }
}
