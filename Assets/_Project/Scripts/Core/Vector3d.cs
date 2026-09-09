using System;
using UnityEngine;

namespace Galilego.Core
{
[Serializable]
    public struct Vector3d
    {
        public static readonly Vector3d Zero = new Vector3d(0d, 0d, 0d);

        public double X;
        public double Y;
        public double Z;

        public bool IsFinite => IsFiniteComponent(X) && IsFiniteComponent(Y) && IsFiniteComponent(Z);
        public double SqrMagnitude => (X * X) + (Y * Y) + (Z * Z);
        public double Magnitude => Math.Sqrt(SqrMagnitude);
        /// <summary>
        /// Единичный вектор. Контракт отличается от operator/ сознательно:
        /// нулевой вектор даёт Zero (не исключение). Вызывающие в горячем пути
        /// полагаются именно на это — менять на throw нельзя без аудита.
        /// NaN обязан распространяться (как в QuaternionD): обнуление молча
        /// маскировало бы порчу состояния (аудит S1).
        /// </summary>
        public Vector3d Normalized
        {
            get
            {
                double magnitude = Magnitude;
                if (magnitude > 0d)
                {
                    return this / magnitude;
                }

                if (double.IsNaN(magnitude))
                {
                    return new Vector3d(double.NaN, double.NaN, double.NaN);
                }

                return Zero;
            }
        }

        public Vector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3d operator +(Vector3d left, Vector3d right)
        {
            return new Vector3d(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static Vector3d operator -(Vector3d left, Vector3d right)
        {
            return new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static Vector3d operator -(Vector3d value)
        {
            return new Vector3d(-value.X, -value.Y, -value.Z);
        }

        public static Vector3d operator *(Vector3d vector, double scalar)
        {
            return new Vector3d(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);
        }

        public static Vector3d operator *(double scalar, Vector3d vector)
        {
            return vector * scalar;
        }

        public static Vector3d operator /(Vector3d vector, double scalar)
        {
            if (scalar == 0d)
            {
                throw new DivideByZeroException("Vector3d cannot be divided by zero.");
            }

            return new Vector3d(vector.X / scalar, vector.Y / scalar, vector.Z / scalar);
        }

        public static double Dot(Vector3d left, Vector3d right)
        {
            return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
        }

        public static Vector3d Cross(Vector3d left, Vector3d right)
        {
            return new Vector3d(
                (left.Y * right.Z) - (left.Z * right.Y),
                (left.Z * right.X) - (left.X * right.Z),
                (left.X * right.Y) - (left.Y * right.X));
        }

        public static Vector3d Lerp(Vector3d a, Vector3d b, double t)
        {
            t = Math.Max(0d, Math.Min(1d, t));
            return new Vector3d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }

        /// <summary>
        /// ГОЛОБЫЙ каст без перестановки осей — перепутанный кадр. Мост astro→sim
        /// ровно один: AstroFrame.ToSimulation (sim = (x, z, −y)). Метод оставлен
        /// [Obsolete(..., true)] — любой вызов ломает компиляцию, а не молча
        /// отдаёт движку Y-up-независимый вектор.
        /// </summary>
        [Obsolete("Перепутанный кадр: голый каст без перестановки осей. Используйте AstroFrame.ToSimulation (sim = (x, z, −y)).", true)]
        public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);

        public override string ToString()
{
            return $"({X}, {Y}, {Z})";
        }

        private static bool IsFiniteComponent(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
