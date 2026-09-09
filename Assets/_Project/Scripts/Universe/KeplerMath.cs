using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Чистая математика перевода кеплеровых элементов в положение/скорость.
    /// Вынесено из старого UniverseManager (там это были private/static методы
    /// god-класса) — сама математика не менялась ни на йоту, просто теперь это
    /// независимая переиспользуемая утилита без привязки к "Юпитеру".
    /// </summary>
    public static class KeplerMath
    {
        /// <summary>
        /// Решает уравнение Кеплера M = E - e·sin(E) методом Ньютона (8 итераций)
        /// с проверкой невязки и фолбэком на бисекцию. Брекет [M-e, M+e] валиден
        /// при любом M: f строго растёт при e&lt;1 (f'=1-e·cosE ≥ 1-e &gt; 0), так что
        /// f(M-e) ≤ 0 ≤ f(M+e) всегда. При e∉[0,1) — только Ньютон, как раньше
        /// (невалидный вход, поведение не меняем).
        /// </summary>
        public static double SolveEccentricAnomaly(double meanAnomaly, double eccentricity)
        {
            double estimate = eccentricity < 0.8d ? meanAnomaly : Math.PI;

            for (int i = 0; i < 8; i++)
            {
                double function = estimate - (eccentricity * Math.Sin(estimate)) - meanAnomaly;
                double derivative = 1d - (eccentricity * Math.Cos(estimate));
                estimate -= function / derivative;
            }

            if (eccentricity >= 0d && eccentricity < 1d)
            {
                double residual = estimate - (eccentricity * Math.Sin(estimate)) - meanAnomaly;
                if (Math.Abs(residual) > 1e-12d)
                {
                    estimate = BisectKepler(meanAnomaly, eccentricity);
                }
            }

            return estimate;
        }

        private static double BisectKepler(double meanAnomaly, double eccentricity)
        {
            double lo = meanAnomaly - eccentricity;
            double hi = meanAnomaly + eccentricity;
            for (int i = 0; i < 100; i++)
            {
                double mid = 0.5d * (lo + hi);
                double f = mid - (eccentricity * Math.Sin(mid)) - meanAnomaly;
                if (f > 0d)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            return 0.5d * (lo + hi);
        }

        /// <summary>
        /// Поворот из перифокальной плоскости орбиты в инерциальную систему координат
        /// через R3(-Ω)·R1(-i)·R3(-ω). Результат — в астродинамической системе (Z = север
        /// опорной плоскости). Перевод в систему координат Unity (Y-up/Z-up) делается
        /// отдельно, на уровне рендера — здесь эта забота сознательно не смешивается
        /// с физикой (в отличие от старого EvaluateMoonState, где было и то, и другое сразу).
        /// </summary>
        public static Vector3d RotateOrbitalToWorld(Vector3d vector, double ascendingNode, double inclination, double periapsis)
        {
            double cosOmega = Math.Cos(ascendingNode);
            double sinOmega = Math.Sin(ascendingNode);
            double cosI = Math.Cos(inclination);
            double sinI = Math.Sin(inclination);
            double cosW = Math.Cos(periapsis);
            double sinW = Math.Sin(periapsis);

            double x =
                ((cosOmega * cosW) - (sinOmega * sinW * cosI)) * vector.X +
                ((-cosOmega * sinW) - (sinOmega * cosW * cosI)) * vector.Y;

            double y =
                ((sinOmega * cosW) + (cosOmega * sinW * cosI)) * vector.X +
                ((-sinOmega * sinW) + (cosOmega * cosW * cosI)) * vector.Y;

            double z =
                (sinW * sinI * vector.X) +
                (cosW * sinI * vector.Y);

            return new Vector3d(x, y, z);
        }

        public static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

        public static double NormalizeAngle(double angle)
        {
            double twoPi = Math.PI * 2d;
            angle %= twoPi;
            return angle < 0d ? angle + twoPi : angle;
        }

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
