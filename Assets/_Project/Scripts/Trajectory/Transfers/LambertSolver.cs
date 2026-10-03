using System;

namespace Galilego.Core
{
    /// <summary>
    /// Итог задачи Ламберта: скорости на концах дуги (инерциальные векторы).
    /// Single-rev только: многовитковые решения не строятся (исключение).
    /// </summary>
    public readonly struct LambertSolution
    {
        public readonly Vector3d DepartureVelocity;
        public readonly Vector3d ArrivalVelocity;
        public readonly bool LongWay;

        public LambertSolution(Vector3d departureVelocity, Vector3d arrivalVelocity, bool longWay)
        {
            DepartureVelocity = departureVelocity;
            ArrivalVelocity = arrivalVelocity;
            LongWay = longWay;
        }
    }

    /// <summary>
    /// Задача Ламберта (r1 → r2 за dt) методом Bate–Mueller–White с бисекцией по
    /// универсальной переменной z вместо Ньютона: tof(z) монотонна на single-rev
    /// семействе, бисекция неубиваема (200 итераций, допуск 1e-13), производные
    /// не нужны. Цена — микросекунды на клетку porkchop.
    ///
        /// Вырожденный случай 180° (g = A√(y/μ) = 0, деление на ноль) решается НЕ
        /// шевелением геометрии, а точной конструкцией: при Δν=π дуга лежит в
        /// плоскости с нормалью n=normalize(r1×ref) (ref=+Z, фолбэк +Y), семейство
        /// коник параметризуется эксцентриситетом при постоянном p=2r1r2/(r1+r2) —
        /// см. SolveHalfPlane (BMWS-машина тут теряет поправку 2e·sinE1 и не годится).
        /// Окрестность |sinΔν|&lt;1e-7 идёт тем же
        /// путём (погрешность O(угол) — для планирования приемлемо, для точного
        /// 180° — ноль).
        /// Та же точка (cosΔν≈1) — неоднозначность (любое число витков) → исключение.
    /// </summary>
    public static class LambertSolver
    {
        private const double HalfPlaneSineTolerance = 1e-7d;

        private const double SamePointTolerance = 1e-12d;

        private const int MaxBisectionIterations = 200;

        private const double BisectionRelativeTolerance = 1e-13d;

        public static LambertSolution Solve(Vector3d relativePosition1, Vector3d relativePosition2, double dtSeconds, double mu)
        {
            return Solve(relativePosition1, relativePosition2, dtSeconds, mu, false);
        }

        public static LambertSolution Solve(Vector3d relativePosition1, Vector3d relativePosition2, double dtSeconds, double mu, bool longWay)
        {
            if (dtSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Время перелёта обязано быть положительным.");
            }

            if (mu <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(mu), "Гравитационный параметр обязан быть положительным.");
            }

            double r1 = relativePosition1.Magnitude;
            double r2 = relativePosition2.Magnitude;
            if (r1 <= 0d || r2 <= 0d)
            {
                throw new ArgumentException("Радиус-векторы обязаны быть ненулевыми.");
            }

            double cosDelta = Vector3d.Dot(relativePosition1, relativePosition2) / (r1 * r2);
            if (cosDelta > 1d)
            {
                cosDelta = 1d;
            }
            else if (cosDelta < -1d)
            {
                cosDelta = -1d;
            }

            if (cosDelta > 1d - SamePointTolerance)
            {
                throw new ArgumentException("Совпадающие точки: бесконечно много решений (витки).");
            }

            double sinDelta = Math.Sqrt(Math.Max(0d, 1d - (cosDelta * cosDelta)));
            if (longWay)
            {
                sinDelta = -sinDelta;
            }

            if (cosDelta < 0d && Math.Abs(sinDelta) < HalfPlaneSineTolerance)
            {
                return SolveHalfPlane(relativePosition1, relativePosition2, r1, r2, mu, longWay, dtSeconds);
            }

            double aParameter = sinDelta * Math.Sqrt((r1 * r2) / (1d - cosDelta));
            double z = BisectZ(r1, r2, aParameter, dtSeconds, mu);
            Stumpff.CS(z, out double c, out double s);
            double y = r1 + r2 + (aParameter * ((z * s) - 1d) / Math.Sqrt(c));
            double x = Math.Sqrt(y / c);
            double f = 1d - (y / r1);
            double g = aParameter * Math.Sqrt(y / mu);
            double gDot = 1d - (y / r2);
            Vector3d v1 = (relativePosition2 - (relativePosition1 * f)) / g;
            Vector3d v2 = ((relativePosition2 * gDot) - relativePosition1) / g;
            return new LambertSolution(v1, v2, longWay);
        }

        /// <summary>
        /// Вырожденный случай 180° (A = sinΔν·√(r1r2/(1−cosΔν)) → 0).
        /// BMWS-машина тут НЕ годится: её tof-член x³·s/√μ = a^1.5(ΔE−sinΔE)/√μ
        /// теряет поправку 2e·sinE1 (она жила в A√y, обнулившемся вместе с A),
        /// и даёт время дуги от периапсиса, а не между антиподами (поймано
        /// round-trip'ом T53, расхождение 1.4·r2). Правильная параметризация
        /// семейства: при Δν=π фокальный параметр p = 2r1r2/(r1+r2) ПОСТОЯНЕН
        /// на всём семействе коник, меняется только e ∈ [|κ|, 1),
        /// κ = (r2−r1)/(r1+r2) = e·cosν1 (ν1 = acos(κ/e) ∈ [0,π] — первый
        /// конец дуги). tof(e) = a^1.5·(π + 2e·sinE1)/√μ монотонна по e:
        /// минимум — полупериод min-energy эллипса (e=|κ|, концы в апсидах,
        /// a=(r1+r2)/2), ∞ при e→1. Гипербола/парабола антиподальные точки
        /// из фокуса не соединяют — dt &lt; tof_min: решений НЕТ, громко.
        /// Скорости — точные компоненты: v_r = (μ/h)·e·sinν = (μ/h)·√(e²−κ²)
        /// (в ν1 — наружу, в ν1+π — внутрь), v_t = h/r, h = √(μp). Чисто
        /// тангенциальные концы ТОЛЬКО при e=|κ|.
        /// longWay — та же коника против хода: v_long = −v_short точно.
        /// Плоскость при Δν=π двумя точками не определяется — фиксируем
        /// reference-вектором (ref=+Z, фолбэк +Y).
        /// </summary>
        private static LambertSolution SolveHalfPlane(Vector3d r1Vec, Vector3d r2Vec, double r1, double r2, double mu, bool longWay, double dtSeconds)
        {
            const double eCap = 1d - 1e-9d; // аналог капа z=39: e→1 даёт tof→∞, но численный кап нужен

            double kappa = (r2 - r1) / (r1 + r2); // = e·cosν1, |κ| ≤ 1
            double absKappa = Math.Abs(kappa);
            double p = (2d * r1 * r2) / (r1 + r2); // фокальный параметр, константа семейства
            double aMin = 0.5d * (r1 + r2);
            double tofMin = Math.PI * Math.Sqrt((aMin * aMin * aMin) / mu);
            if (dtSeconds < tofMin * (1d - 1e-12d))
            {
                throw new ArgumentOutOfRangeException(nameof(dtSeconds),
                    "Для дуги 180° минимальное время перелёта — полупериод min-energy эллипса (" +
                    tofMin.ToString("F0") + " с); запрошено " + dtSeconds.ToString("F0") + " с — решений нет.");
            }

            double FamilyTimeOfFlight(double e)
            {
                // cosE = (e + cosν)/(1 + e·cosν). ВАЖНО: антиподальность по ν НЕ
                // переходит в E2 = E1 + π (это верно только при e = 0) — E1 и E2
                // для ν1 и ν1+π считаются НЕЗАВИСИМО, иначе tof занижается и
                // round-trip промахивается мимо r2 (поймано T53: дуга приходила
                // в 174.3° вместо 180°).
                double cosNu1 = kappa / e; // ∈ (−1, 1) для e ∈ (|κ|, 1)
                // cosE = (e + cosν)/(1 + e·cosν); в семействе e·cosν1 = κ ровно,
                // поэтому знаменатели — константы (1±κ).
                double cosE1 = (e + cosNu1) / (1d + kappa);
                double cosE2 = (e - cosNu1) / (1d - kappa); // cosν2 = −cosν1
                double e1 = Math.Acos(Math.Max(-1d, Math.Min(1d, cosE1))); // ν1 ∈ [0, π] ⇒ E1 ∈ [0, π]
                double e2 = (2d * Math.PI) - Math.Acos(Math.Max(-1d, Math.Min(1d, cosE2))); // ν2 ∈ [π, 2π] ⇒ E2 ∈ [π, 2π]
                double sinE1 = Math.Sin(e1);
                double sinE2 = -Math.Sqrt(Math.Max(0d, 1d - (cosE2 * cosE2))); // E2 ∈ [π, 2π]
                double deltaM = (e2 - e1) + (e * (sinE1 - sinE2));
                double a = p / (1d - (e * e));
                return (a * Math.Sqrt(a) * deltaM) / Math.Sqrt(mu);
            }

            double tofCap = FamilyTimeOfFlight(eCap);
            if (dtSeconds > tofCap)
            {
                throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Время перелёта слишком велико для single-rev семейства этой геометрии.");
            }

            // dt ≈ tofMin — ровно min-energy: концы в апсидах, e = |κ| точно,
            // радиальная компонента строго нулевая. Без снапа бисекция даёт
            // e = |κ|+δ, а √(e²−κ²) = √(2κδ) превращает δ~1e-12 в конечную
            // радиальную скорость — v1 перестаёт быть перпендикулярным r1
            // (поймано T25: гоман 180°, ⊥=241 вместо <1e-6).
            if (dtSeconds <= tofMin * (1d + 1e-12d))
            {
                return HalfPlaneVelocity(r1Vec, r2Vec, r1, r2, mu, longWay, absKappa, p, kappa);
            }

            // Бисекция по e ∈ [|κ|, eCap]: tof(e) строго монотонна.
            double lo = absKappa;
            double hi = eCap;
            for (int i = 0; i < MaxBisectionIterations; i++)
            {
                double mid = 0.5d * (lo + hi);
                if (mid <= lo || mid >= hi)
                {
                    break; // предел точности double
                }

                double err = (FamilyTimeOfFlight(mid) - dtSeconds) / dtSeconds;
                if (Math.Abs(err) < BisectionRelativeTolerance)
                {
                    break;
                }

                if (err < 0d)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return HalfPlaneVelocity(r1Vec, r2Vec, r1, r2, mu, longWay, 0.5d * (lo + hi), p, kappa);
        }

        /// <summary>Скорости на концах 180°-дуги по готовому эксцентриситету.</summary>
        private static LambertSolution HalfPlaneVelocity(
            Vector3d r1Vec, Vector3d r2Vec, double r1, double r2, double mu, bool longWay, double ecc, double p, double kappa)
        {
            double semiMajor = p / (1d - (ecc * ecc));
            double h = Math.Sqrt(mu * p);
            double radialSpeed = (mu / h) * Math.Sqrt(Math.Max(0d, (ecc * ecc) - (kappa * kappa))); // (μ/h)·e·sinν1, ≥0
            double tangentialSpeed1 = h / r1;
            double tangentialSpeed2 = h / r2;

            Vector3d reference = Math.Abs(r1Vec.Z / r1) < 0.9d
                ? new Vector3d(0d, 0d, 1d)
                : new Vector3d(0d, 1d, 0d);
            Vector3d normal = Vector3d.Cross(r1Vec, reference).Normalized;
            Vector3d rHat1 = r1Vec / r1;
            Vector3d rHat2 = r2Vec / r2; // ≈ −rHat1 по геометрии ветки
            Vector3d tangent1 = Vector3d.Cross(normal, rHat1); // направление роста ν (прогрейд вокруг n)
            Vector3d tangent2 = Vector3d.Cross(normal, rHat2);

            // Короткая дуга: ν растёт от ν1 к ν1+π ⇒ v_r(ν1) > 0, v_r(ν1+π) < 0.
            Vector3d v1 = (rHat1 * radialSpeed) + (tangent1 * tangentialSpeed1);
            Vector3d v2 = (rHat2 * (-radialSpeed)) + (tangent2 * tangentialSpeed2);
            if (longWay)
            {
                v1 = -v1;
                v2 = -v2;
            }

            return new LambertSolution(v1, v2, longWay);
        }

        private static double BisectZ(double r1, double r2, double aParameter, double dtSeconds, double mu)
        {
            double tofZero = TimeOfFlight(0d, r1, r2, aParameter, mu);
            double lo;
            double hi;
            if (dtSeconds > tofZero)
            {
                lo = 0d;
                hi = 1d;
                while (TimeOfFlight(hi, r1, r2, aParameter, mu) < dtSeconds)
                {
                    hi *= 2d;
                    if (hi > 39d)
                    {
                        hi = 39d;
                        // Кап z=39 — граница single-rev семейства (c→0 при z→4π²).
                        // Если даже на капе tof меньше запрошенного — решения нет:
                        // громко, а не молчаливым съездом к капу с чужим ответом
                        // (поймано T48). Porkchop ловит ArgumentException → invalid.
                        if (TimeOfFlight(hi, r1, r2, aParameter, mu) < dtSeconds)
                        {
                            throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Время перелёта слишком велико для single-rev семейства этой геометрии.");
                        }

                        break;
                    }
                }
            }
            else if (dtSeconds < tofZero)
            {
                hi = 0d;
                lo = -1d;
                while (TimeOfFlight(lo, r1, r2, aParameter, mu) > dtSeconds)
                {
                    lo *= 2d;
                    if (lo < -1e4d)
                    {
                        throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Время перелёта слишком мало для этой геометрии.");
                    }
                }
            }
            else
            {
                return 0d;
            }

            double z = 0.5d * (lo + hi);
            for (int i = 0; i < MaxBisectionIterations; i++)
            {
                z = 0.5d * (lo + hi);
                double err = (TimeOfFlight(z, r1, r2, aParameter, mu) - dtSeconds) / dtSeconds;
                if (Math.Abs(err) < BisectionRelativeTolerance)
                {
                    break;
                }

                if (err > 0d)
                {
                    hi = z;
                }
                else
                {
                    lo = z;
                }
            }

            return z;
        }

        private static double TimeOfFlight(double z, double r1, double r2, double aParameter, double mu)
        {
            Stumpff.CS(z, out double c, out double s);
            if (c <= 0d)
            {
                return double.PositiveInfinity;
            }

            double y = r1 + r2 + (aParameter * ((z * s) - 1d) / Math.Sqrt(c));
            if (y <= 0d)
            {
                return double.PositiveInfinity;
            }

            double x = Math.Sqrt(y / c);
            return ((x * x * x * s) + (aParameter * Math.Sqrt(y))) / Math.Sqrt(mu);
        }
    }

    /// <summary>
    /// Функции Штумпфа C(z), S(z): тригонометрия (z&gt;0), гиперболика (z&lt;0),
    /// ряд (|z|≤1e-6). Внутренние, наружу не торчат.
    /// </summary>
    internal static class Stumpff
    {
        public static void CS(double z, out double c, out double s)
        {
            if (z > 1e-6d)
            {
                double root = Math.Sqrt(z);
                c = (1d - Math.Cos(root)) / z;
                s = (root - Math.Sin(root)) / (root * z);
            }
            else if (z < -1e-6d)
            {
                double root = Math.Sqrt(-z);
                c = (Math.Cosh(root) - 1d) / -z;
                s = (Math.Sinh(root) - root) / (root * -z);
            }
            else
            {
                double z2 = z * z;
                c = 0.5d - (z / 24d) + (z2 / 720d) - ((z2 * z) / 40320d);
                s = (1d / 6d) - (z / 120d) + (z2 / 5040d) - ((z2 * z) / 362880d);
            }
        }
    }
}
