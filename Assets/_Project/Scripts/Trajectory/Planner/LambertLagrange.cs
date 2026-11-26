using System;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>
    /// Задача Ламберта (один виток) в безразмерной форме Ланкастера–Бланшара/Иццо с временем полёта по уравнению
    /// Лагранжа (F-MAN-09 справочника навыка астронома): T(x) = a^{3/2}[(α−sin α) − (β−sin β)]/2, a = 1/(1−x²),
    /// α = 2 acos x, β = 2 asin(λ√(1−x²)) (эллипс), гипербола — через acosh/asinh. T(x) монотонно убывает на (−1,∞),
    /// корень ищется бисекцией (без производных, не расходится). Скорости — из радиальной/тангенциальной частей.
    ///
    /// Зачем не LambertSolver (BMWS): у него погрешность растёт как 1/ε² при подходе к дуге 180° (λ² = 1−c/s —
    /// вычитание почти равных чисел), а 180° — это геометрия Хомана. Здесь λ² для D&lt;0 считается устойчиво:
    /// λ² = 2|r1×r2|² / ((P−D)(r1+r2+c)²), P=|r1||r2|, D=r1·r2.
    /// Плоскость при почти точных 180° (|r1×r2| &lt; 1e-9·P) задаётся предпочтительной нормалью hPrefer.
    /// </summary>
    public static class LambertLagrange
    {
        private const double CoplanarSine = 1e-9d;

        public static bool TrySolve(double mu, Vector3d r1, Vector3d r2, double dt, Vector3d hPrefer,
            out Vector3d v1, out Vector3d v2)
        {
            v1 = v2 = Vector3d.Zero;
            double n1 = r1.Magnitude, n2 = r2.Magnitude;
            if (!(n1 > 0d) || !(n2 > 0d) || !(dt > 0d) || !(mu > 0d) || !r1.IsFinite || !r2.IsFinite)
            {
                return false;
            }

            Vector3d cross = Vector3d.Cross(r1, r2);
            double crm = cross.Magnitude;
            double p = n1 * n2;
            double d = Vector3d.Dot(r1, r2);
            double c = (r2 - r1).Magnitude;
            double sumC = n1 + n2 + c;
            double s = 0.5d * sumC;
            double lam2 = d < 0d ? 2d * crm * crm / ((p - d) * sumC * sumC) : 1d - (c / s);
            lam2 = Math.Min(1d, Math.Max(0d, lam2));
            double lam = Math.Sqrt(lam2);

            Vector3d hMotion;
            bool longWay;
            if (crm > CoplanarSine * p)
            {
                Vector3d ih = cross / crm;
                longWay = Vector3d.Dot(ih, hPrefer) < 0d;
                hMotion = longWay ? -ih : ih;
            }
            else if (d < 0d)
            {
                Vector3d x = r1 / n1;
                Vector3d hp = hPrefer - (x * Vector3d.Dot(hPrefer, x));
                if (!(hp.Magnitude > 1e-12d * Math.Max(1d, hPrefer.Magnitude)))
                {
                    return false;
                }

                hMotion = hp.Normalized;
                longWay = false;
            }
            else
            {
                return false; // совпадающие точки: бесконечно много решений
            }

            double lamS = longWay ? -lam : lam;
            double tNd = Math.Sqrt(2d * mu / (s * s * s)) * dt;
            if (!TrySolveX(lamS, tNd, out double xr))
            {
                return false;
            }

            double y = Math.Sqrt(Math.Max(0d, 1d - (lam2 * (1d - (xr * xr)))));
            double gamma = Math.Sqrt(mu * s / 2d);
            double rho = (n1 - n2) / c;
            double sigma = Math.Sqrt(Math.Max(0d, 1d - (rho * rho)));
            double vr1 = gamma * ((lamS * y - xr) - (rho * ((lamS * y) + xr))) / n1;
            double vr2 = -gamma * ((lamS * y - xr) + (rho * ((lamS * y) + xr))) / n2;
            double vt = gamma * sigma * (y + (lamS * xr));
            Vector3d u1 = r1 / n1, u2 = r2 / n2;
            v1 = (u1 * vr1) + (Vector3d.Cross(hMotion, u1) * (vt / n1));
            v2 = (u2 * vr2) + (Vector3d.Cross(hMotion, u2) * (vt / n2));
            return v1.IsFinite && v2.IsFinite;
        }

        /// <summary>Безразмерное время T(x, λ) (уравнение Лагранжа, F-MAN-09). x ∈ (−1, ∞).</summary>
        internal static double Tof(double x, double lam)
        {
            double om = 1d - (x * x);
            if (Math.Abs(x - 1d) < 1e-9d)
            {
                return (2d / 3d) * (1d - (lam * lam * lam)); // парабола (Эйлер)
            }

            double a = 1d / om;
            if (a > 0d)
            {
                double alpha = 2d * Math.Acos(x);
                double beta = 2d * Math.Asin(Math.Min(1d, Math.Max(-1d, lam * Math.Sqrt(om))));
                return a * Math.Sqrt(a) * (Dif(alpha) - Dif(beta)) / 2d;
            }
            else
            {
                double alpha = 2d * Acosh(x);
                double beta = 2d * Asinh(lam * Math.Sqrt(-om));
                return -a * Math.Sqrt(-a) * (DifH(beta) - DifH(alpha)) / 2d;
            }
        }

        private static bool TrySolveX(double lam, double target, out double x)
        {
            x = 0d;
            if (!(target > 0d) || !double.IsFinite(target))
            {
                return false;
            }

            double lo, hi;
            if (target > Tof(0d, lam))
            {
                lo = -1d + 1e-15d;
                hi = 0d;
                if (Tof(lo, lam) < target) { return false; }
            }
            else
            {
                lo = 0d;
                hi = 1d;
                int guard = 0;
                while (Tof(hi, lam) > target)
                {
                    lo = hi;
                    hi *= 2d;
                    if (++guard > 80) { return false; }
                }
            }

            // Tof убывает по x: Tof(lo) ≥ target ≥ Tof(hi)
            for (int i = 0; i < 300; i++)
            {
                x = 0.5d * (lo + hi);
                double f = Tof(x, lam) - target;
                if (f > 0d) { lo = x; } else { hi = x; }
                if (hi - lo <= 2e-16d * Math.Max(1d, Math.Abs(x))) { break; }
            }

            x = 0.5d * (lo + hi);
            return double.IsFinite(x);
        }

        /// <summary>t − sin t без потери разрядов при малых t.</summary>
        private static double Dif(double t)
        {
            if (Math.Abs(t) > 0.5d) { return t - Math.Sin(t); }
            double t2 = t * t, term = t * t2 / 6d, sum = term;
            for (int k = 1; k < 10; k++) { term *= -t2 / ((2d * k + 2d) * (2d * k + 3d)); sum += term; }
            return sum;
        }

        /// <summary>t − sinh t без потери разрядов при малых t.</summary>
        private static double DifH(double t)
        {
            if (Math.Abs(t) > 0.5d) { return t - Math.Sinh(t); }
            double t2 = t * t, term = -t * t2 / 6d, sum = term;
            for (int k = 1; k < 10; k++) { term *= t2 / ((2d * k + 2d) * (2d * k + 3d)); sum += term; }
            return sum;
        }

        private static double Acosh(double x) { return Math.Log(x + Math.Sqrt((x * x) - 1d)); }
        private static double Asinh(double x) { return Math.Log(x + Math.Sqrt((x * x) + 1d)); }
    }
}
