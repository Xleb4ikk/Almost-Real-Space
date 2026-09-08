using System;

namespace Galilego.Core
{
    /// <summary>
    /// Кватернион двойной точности (x, y, z, w), без зависимости от Unity —
    /// тестовый стенд компилирует его напрямую. Конвенция зафиксирована здесь,
    /// а не подразумевается: q отображает body-frame в world-frame
    /// (v_world = q · v_body · q⁻¹), угловая скорость — в body-frame, тогда
    /// q̇ = ½·q⊗ω_quat. Одноосевые тесты (R1a/R1b) переворот НЕ ловят:
    /// вращения вокруг одной оси коммутируют (подалгебра ≅ ℂ), а E/|L|
    /// от конвенции не зависят вовсе — доказано красным прогоном.
    /// Дискриминатор — сохранение мировой L в кувырке (R1c ΔL_world).
    /// </summary>
    public readonly struct QuaternionD
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;
        public readonly double W;

        public QuaternionD(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static QuaternionD Identity => new QuaternionD(0d, 0d, 0d, 1d);

        public double NormSquared => ((X * X) + (Y * Y)) + ((Z * Z) + (W * W));

        public QuaternionD Normalized
        {
            get
            {
                double norm = Math.Sqrt(NormSquared);
                if (norm <= 0d)
                {
                    return Identity;
                }

                return this / norm;
            }
        }

        public QuaternionD Conjugated => new QuaternionD(-X, -Y, -Z, W);

        public static QuaternionD operator +(QuaternionD a, QuaternionD b) =>
            new QuaternionD(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);

        public static QuaternionD operator -(QuaternionD a, QuaternionD b) =>
            new QuaternionD(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);

        public static QuaternionD operator *(QuaternionD q, double s) =>
            new QuaternionD(q.X * s, q.Y * s, q.Z * s, q.W * s);

        public static QuaternionD operator *(double s, QuaternionD q) => q * s;

        public static QuaternionD operator /(QuaternionD q, double s) =>
            new QuaternionD(q.X / s, q.Y / s, q.Z / s, q.W / s);

        public static QuaternionD operator *(QuaternionD a, QuaternionD b) =>
            new QuaternionD(
                ((a.W * b.X) + (a.X * b.W)) + ((a.Y * b.Z) - (a.Z * b.Y)),
                ((a.W * b.Y) - (a.X * b.Z)) + ((a.Y * b.W) + (a.Z * b.X)),
                ((a.W * b.Z) + (a.X * b.Y)) - ((a.Y * b.X) - (a.Z * b.W)),
                ((a.W * b.W) - (a.X * b.X)) - ((a.Y * b.Y) + (a.Z * b.Z)));

        /// <summary>Поворот вектора из body-frame в world-frame: q·v·q⁻¹.</summary>
        public Vector3d Rotate(Vector3d body)
        {
            QuaternionD v = new QuaternionD(body.X, body.Y, body.Z, 0d);
            QuaternionD turned = this * v * Conjugated;
            return new Vector3d(turned.X, turned.Y, turned.Z);
        }

        public static QuaternionD FromAxisAngle(Vector3d axis, double angleRadians)
        {
            Vector3d unit = axis.Normalized;
            double half = 0.5d * angleRadians;
            double s = Math.Sin(half);
            return new QuaternionD(unit.X * s, unit.Y * s, unit.Z * s, Math.Cos(half));
        }

        /// <summary>
        /// Производная ориентации при body-frame угловой скорости:
        /// q̇ = ½·q⊗(ωx, ωy, ωz, 0). Пара к конвенции Rotate выше.
        /// </summary>
        public static QuaternionD Derivative(QuaternionD attitude, Vector3d angularVelocityBody)
        {
            return attitude * new QuaternionD(angularVelocityBody.X, angularVelocityBody.Y, angularVelocityBody.Z, 0d) * 0.5d;
        }
    }
}
