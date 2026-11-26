using System;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>
    /// Двухтельное движение в универсальных переменных (любая коника: эллипс, парабола, гипербола, круг).
    /// Формулы — справочник навыка астронома: F-KEP-09 (функции Штумпфа), универсальное уравнение Кеплера;
    /// независимо проверено против 60-значного mpmath-оракула (tests). Чистая математика: без Unity, без аллокаций.
    /// </summary>
    public static class UniversalKepler
    {
        private const double TwoPi = 2d * Math.PI;

        /// <summary>
        /// Функции Штумпфа C(z), S(z). Ряд при |z|&lt;1 (14 членов, остаток ~1e-30) — замкнутая форма там теряла бы
        /// порядки из-за вычитания 1−cos√z, √z−sin√z.
        /// </summary>
        public static void Stumpff(double z, out double c, out double s)
        {
            if (Math.Abs(z) < 1d)
            {
                // C = Σ (−z)^k/(2k+2)!, S = Σ (−z)^k/(2k+3)!
                double termC = 0.5d;
                double termS = 1d / 6d;
                c = termC;
                s = termS;
                for (int k = 1; k <= 14; k++)
                {
                    termC *= -z / ((2d * k + 1d) * (2d * k + 2d));
                    termS *= -z / ((2d * k + 2d) * (2d * k + 3d));
                    c += termC;
                    s += termS;
                }

                return;
            }

            if (z > 0d)
            {
                double root = Math.Sqrt(z);
                c = (1d - Math.Cos(root)) / z;
                s = (root - Math.Sin(root)) / (root * z);
            }
            else
            {
                double root = Math.Sqrt(-z);
                c = (Math.Cosh(root) - 1d) / -z;
                s = (Math.Sinh(root) - root) / (root * -z);
            }
        }

        /// <summary>
        /// (r0,v0) → (r,v) через dt секунд. false — вырожденное состояние или нет сходимости.
        /// Отрицательное dt — по симметрии обращения времени.
        /// </summary>
        public static bool TryPropagate(double mu, Vector3d r0, Vector3d v0, double dt, out Vector3d r, out Vector3d v)
        {
            r = r0;
            v = v0;
            if (!(mu > 0d) || !r0.IsFinite || !v0.IsFinite || !double.IsFinite(dt))
            {
                return false;
            }

            if (dt == 0d)
            {
                return true;
            }

            if (dt < 0d)
            {
                bool ok = TryPropagate(mu, r0, -v0, -dt, out r, out v);
                v = -v;
                return ok;
            }

            double sq = Math.Sqrt(mu);
            double r0n = r0.Magnitude;
            if (!(r0n > 0d))
            {
                return false;
            }

            double vr0 = Vector3d.Dot(r0, v0) / r0n;
            double alpha = (2d / r0n) - (v0.SqrMagnitude / mu); // 1/a
            if (alpha > 1e-15d / r0n)
            {
                double period = TwoPi / (sq * Math.Pow(alpha, 1.5d));
                dt = Math.IEEERemainder(dt, period);
                if (dt < 0d)
                {
                    dt += period; // вперёд по орбите, без смены направления времени
                }

                if (dt == 0d)
                {
                    return true;
                }
            }

            double Residual(double chi, out double rr)
            {
                double z = alpha * chi * chi;
                Stumpff(z, out double cc, out double ss);
                rr = (r0n * vr0 / sq * chi * (1d - (z * ss))) + ((1d - (alpha * r0n)) * chi * chi * cc) + r0n;
                return (r0n * vr0 / sq * chi * chi * cc) + ((1d - (alpha * r0n)) * chi * chi * chi * ss) + (r0n * chi) - (sq * dt);
            }

            double lo = 0d;
            double hi = Math.Max(sq * dt / r0n, 1e-12d);
            int guard = 0;
            while (Residual(hi, out _) < 0d)
            {
                lo = hi;
                hi *= 2d;
                if (++guard > 2000 || double.IsInfinity(hi))
                {
                    return false;
                }
            }

            double x = 0.5d * (lo + hi);
            for (int i = 0; i < 200; i++)
            {
                double f = Residual(x, out double rad);
                if (f > 0d)
                {
                    hi = x;
                }
                else
                {
                    lo = x;
                }

                double next = x - (f / rad);
                if (!(next > lo && next < hi))
                {
                    next = 0.5d * (lo + hi);
                }

                bool done = Math.Abs(next - x) <= 1e-14d * Math.Max(1d, Math.Abs(x));
                x = next;
                if (done)
                {
                    break;
                }
            }

            double z2 = alpha * x * x;
            Stumpff(z2, out double c2, out double s2);
            double fLag = 1d - (x * x * c2 / r0n);
            double gLag = dt - (x * x * x * s2 / sq);
            r = (r0 * fLag) + (v0 * gLag);
            double rn = r.Magnitude;
            double fDot = sq / (rn * r0n) * x * ((z2 * s2) - 1d);
            double gDot = 1d - (x * x * c2 / rn);
            v = (r0 * fDot) + (v0 * gDot);
            return r.IsFinite && v.IsFinite;
        }
    }
}
