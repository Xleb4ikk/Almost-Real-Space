using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{    // Double-double arithmetic for a reference that is not limited by double rounding.
    internal readonly struct StudyDd
    {
        public readonly double Hi;
        public readonly double Lo;

        public StudyDd(double hi, double lo)
        {
            Hi = hi;
            Lo = lo;
        }

        public static readonly StudyDd Zero = new StudyDd(0d, 0d);
        public static StudyDd From(double x) => new StudyDd(x, 0d);

        public static StudyDd Add(StudyDd a, StudyDd b)
        {
            double s = a.Hi + b.Hi;
            double v = s - a.Hi;
            double e = (a.Hi - (s - v)) + (b.Hi - v) + a.Lo + b.Lo;
            double r = s + e;
            return new StudyDd(r, e - (r - s));
        }

        public static StudyDd Sub(StudyDd a, StudyDd b) =>
            Add(a, new StudyDd(-b.Hi, -b.Lo));

        public static StudyDd Mul(StudyDd a, StudyDd b)
        {
            double p = a.Hi * b.Hi;
            double e = Math.FusedMultiplyAdd(a.Hi, b.Hi, -p);
            e += (a.Hi * b.Lo) + (a.Lo * b.Hi);
            double r = p + e;
            return new StudyDd(r, e - (r - p));
        }

        public static StudyDd Div(StudyDd a, StudyDd b)
        {
            double q1 = a.Hi / b.Hi;
            StudyDd r = Sub(a, Mul(b, From(q1)));
            double q2 = r.Hi / b.Hi;
            StudyDd q = Add(From(q1), From(q2));
            r = Sub(a, Mul(b, q));
            double q3 = r.Hi / b.Hi;
            return Add(q, From(q3));
        }

        public static StudyDd Sqrt(StudyDd a)
        {
            if (a.Hi == 0d && a.Lo == 0d)
            {
                return Zero;
            }

            if (!(a.Hi > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(a), "StudyDd.Sqrt requires a non-negative argument.");
            }

            double x = Math.Sqrt(a.Hi);
            StudyDd r = Sub(a, Mul(From(x), From(x)));
            double c = r.Hi / (2d * x);
            return Add(From(x), From(c));
        }

        public static StudyDd operator +(StudyDd a, StudyDd b) => Add(a, b);
        public static StudyDd operator -(StudyDd a, StudyDd b) => Sub(a, b);
        public static StudyDd operator *(StudyDd a, StudyDd b) => Mul(a, b);
        public static StudyDd operator /(StudyDd a, StudyDd b) => Div(a, b);
        public double ToDouble => Hi + Lo;
    }

    internal readonly struct StudyDd3
    {
        public readonly StudyDd X;
        public readonly StudyDd Y;
        public readonly StudyDd Z;

        public StudyDd3(StudyDd x, StudyDd y, StudyDd z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly StudyDd3 Zero = new StudyDd3(StudyDd.Zero, StudyDd.Zero, StudyDd.Zero);
        public static StudyDd3 From(Vector3d v) =>
            new StudyDd3(StudyDd.From(v.X), StudyDd.From(v.Y), StudyDd.From(v.Z));
        public Vector3d ToVector3d() => new Vector3d(X.ToDouble, Y.ToDouble, Z.ToDouble);

        public StudyDd SqrMagnitude => StudyDd.Add(StudyDd.Add(StudyDd.Mul(X, X), StudyDd.Mul(Y, Y)), StudyDd.Mul(Z, Z));
        public StudyDd Magnitude => StudyDd.Sqrt(SqrMagnitude);

        public static StudyDd3 operator +(StudyDd3 a, StudyDd3 b) =>
            new StudyDd3(StudyDd.Add(a.X, b.X), StudyDd.Add(a.Y, b.Y), StudyDd.Add(a.Z, b.Z));
        public static StudyDd3 operator -(StudyDd3 a, StudyDd3 b) =>
            new StudyDd3(StudyDd.Sub(a.X, b.X), StudyDd.Sub(a.Y, b.Y), StudyDd.Sub(a.Z, b.Z));
        public static StudyDd3 operator *(StudyDd3 a, StudyDd s) =>
            new StudyDd3(StudyDd.Mul(a.X, s), StudyDd.Mul(a.Y, s), StudyDd.Mul(a.Z, s));
        public static StudyDd3 operator *(StudyDd3 a, double s) => a * StudyDd.From(s);
    }

    private static StudyDd3 StudyForceDd(StarSystem sys, StudyDd3 p, double t)
    {
        var bodies = sys.AllBodies;
        int n = bodies.Count;
        sys.EvaluateBodyState(bodies[0], t, out Vector3d rootPos, out _);
        StudyDd3 total = StudyDd3.Zero;
        for (int i = 0; i < n; i++)
        {
            OrbitingBody body = bodies[i];
            double mu = body.ResolveStandardGravitationalParameter();
            if (mu == 0d)
            {
                continue;
            }

            sys.EvaluateBodyState(body, t, out Vector3d bp, out _);
            StudyDd3 d = StudyDd3.From(bp) - p;
            StudyDd r2 = d.SqrMagnitude;
            StudyDd inv = StudyDd.From(1d) / (r2 * StudyDd.Sqrt(r2));
            total += d * (StudyDd.From(mu) * inv);
        }

        // Root-fixed frame: subtract the root's acceleration from its direct
        // children (subtree mu), not the field value at the root (which is 0).
        Vector3d rootAccel = Vector3d.Zero;
        for (int i = 1; i < n; i++)
        {
            OrbitingBody body = bodies[i];
            if (!ReferenceEquals(body.Parent, bodies[0]))
            {
                continue;
            }

            sys.EvaluateBodyState(body, t, out Vector3d bp, out _);
            rootAccel += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                rootPos, bp, body.ResolveSubtreeStandardGravitationalParameter());
        }

        return total - StudyDd3.From(rootAccel);
    }
}


