using System;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>Гипербола в СК тела в периапсисе: ось x̂ (к периапсису), направление движения ŷ, нормаль плоскости ĥ = x̂×ŷ.</summary>
    public struct HyperbolaAtPeriapsis
    {
        public bool Valid;
        public Vector3d AxisX, MotionY, Normal;
        public double Rp, Vp, Eccentricity;
        public Vector3d PeriapsisPosition { get { return AxisX * Rp; } }
        public Vector3d PeriapsisVelocity { get { return MotionY * Vp; } }
    }

    /// <summary>Состояние в периапсисе входной гиперболы захвата и импульс захвата.</summary>
    public static class CaptureGeometry
    {
        /// <summary>
        /// Входная гипербола с вектором избытка vInf (скорость на бесконечности в СК тела) и периапсисом rp.
        /// Плоскость: содержит vInf и перпендикулярную vInf часть hPrefer (орбита захвата прямая относительно hPrefer).
        /// Из u = cosφ·x̂ + sinφ·ŷ, cosφ = 1/e (F-FUN: асимптота), x̂ = R(−φ)u вокруг ĥ.
        /// </summary>
        public static bool TryIncoming(double mu, Vector3d vInf, double rp, Vector3d hPrefer, out HyperbolaAtPeriapsis h)
        {
            h = default;
            double v = vInf.Magnitude;
            if (!(v > 1e-6d) || !(rp > 0d) || !(mu > 0d)) { return false; }
            Vector3d u = vInf / v;
            Vector3d hh = hPrefer - (u * Vector3d.Dot(hPrefer, u));
            if (!(hh.Magnitude > 1e-9d * Math.Max(1d, hPrefer.Magnitude)))
            {
                // hPrefer ∥ u: любая перпендикулярная
                Vector3d t = Math.Abs(u.X) < 0.9d ? new Vector3d(1d, 0d, 0d) : new Vector3d(0d, 1d, 0d);
                hh = Vector3d.Cross(u, t);
            }

            hh = hh.Normalized;
            double e = 1d + (rp * v * v / mu);
            double cosPhi = 1d / e, sinPhi = Math.Sqrt(1d - (cosPhi * cosPhi));
            Vector3d x = ((u * cosPhi) - (Vector3d.Cross(hh, u) * sinPhi)).Normalized;
            Vector3d y = Vector3d.Cross(hh, x).Normalized;
            h = new HyperbolaAtPeriapsis
            {
                Valid = true, AxisX = x, MotionY = y, Normal = hh, Rp = rp, Eccentricity = e,
                Vp = Math.Sqrt((v * v) + (2d * mu / rp)),
            };
            return true;
        }

        /// <summary>Точка входной гиперболы за secondsBefore до периапсиса (СК тела), двухтельное обращение времени.</summary>
        public static bool TryPositionBefore(double mu, in HyperbolaAtPeriapsis h, double secondsBefore, out Vector3d position)
        {
            position = Vector3d.Zero;
            if (!UniversalKepler.TryPropagate(mu, h.PeriapsisPosition, h.PeriapsisVelocity, -secondsBefore, out position, out _)) { return false; }
            return true;
        }

        /// <summary>Импульс захвата в периапсисе (касательный, против движения) на круг или эллипс; вектор в осях тела.</summary>
        public static Vector3d CaptureImpulse(double mu, in HyperbolaAtPeriapsis h, CaptureKind kind, double eccentricity)
        {
            double vTarget = kind == CaptureKind.Elliptic
                ? Math.Sqrt(mu * (1d + eccentricity) / h.Rp)
                : Math.Sqrt(mu / h.Rp);
            return h.MotionY * (vTarget - h.Vp);
        }
    }

    /// <summary>Подготовка орбиты: переход Хомана между круговыми орбитами одной плоскости (две касательные импульса).</summary>
    public struct PrepPlan
    {
        public bool Valid;
        public double T1, T2;
        public Vector3d Dv1, Dv2;                 // в осях тела
        public Vector3d RAfter, VAfter;           // круговая орбита после второго импульса (состояние при T2)
        public double TotalDv { get { return Dv1.Magnitude + Dv2.Magnitude; } }
    }

    public static class OrbitPrep
    {
        /// <summary>F-MAN-01: Δv1 = √(μ/r1)(√(2r2/(r1+r2))−1), Δv2 = √(μ/r2)(1−√(2r1/(r1+r2))); для почти круговой исходной (e&lt;maxEcc).</summary>
        public static bool TryHohmann(double mu, double t1, Vector3d r0, Vector3d v0, double rNew, double maxEcc, out PrepPlan plan)
        {
            plan = default;
            double r1 = r0.Magnitude;
            Vector3d h = Vector3d.Cross(r0, v0);
            if (!(r1 > 0d) || !(h.Magnitude > 0d) || !(rNew > 0d)) { return false; }
            Vector3d eVec = ((r0 * (v0.SqrMagnitude - (mu / r1))) - (v0 * Vector3d.Dot(r0, v0))) / mu;
            if (eVec.Magnitude > maxEcc || v0.SqrMagnitude >= 2d * mu / r1) { return false; }
            Vector3d tHat = Vector3d.Cross(h.Normalized, r0 / r1);
            double vTransfer = Math.Sqrt(mu * ((2d / r1) - (2d / (r1 + rNew))));
            Vector3d vAfter1 = tHat * vTransfer;
            Vector3d dv1 = vAfter1 - v0;
            double aT = 0.5d * (r1 + rNew);
            double half = Math.PI * Math.Sqrt(aT * aT * aT / mu);
            if (!UniversalKepler.TryPropagate(mu, r0, vAfter1, half, out Vector3d r2, out Vector3d v2)) { return false; }
            Vector3d vCirc = Vector3d.Cross(h.Normalized, r2 / r2.Magnitude) * Math.Sqrt(mu / r2.Magnitude);
            plan = new PrepPlan { Valid = true, T1 = t1, T2 = t1 + half, Dv1 = dv1, Dv2 = vCirc - v2, RAfter = r2, VAfter = vCirc };
            return true;
        }
    }
}
