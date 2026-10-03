using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Цвет абсолютно чёрного тела (фотосферы звезды) — чистая математика, без
    /// Unity API, покрывается тестом на стенде (T80).
    /// Спектр Планка → функции отклика глаза CIE 1931 (аналитический фит
    /// Wyman et al. 2013: много-лобовые гауссианы, без таблиц) → XYZ →
    /// линейный sRGB (HDRP работает в linear — конверсия честная).
    /// Результат нормируется по максимальному каналу в [0,1]; множитель
    /// яркости — задача вызывающего (HDR-цвет >1 кормит bloom).
    /// Физическая проверка (T80): 1500 K — красный доминирует, 5772 K —
    /// тёплый белый (R≥G≥B, звёзды такой температуры БЕЛЫЕ; жёлтой их делает
    /// рэлеевское рассеяние в атмосфере), 10000 K — синий доминирует.
    /// </summary>
    public static class StarColorUtil
    {
        /// <summary>hc/k в нм·К — для Планка в длинах волн нанометрах.</summary>
        private const double C2 = 1.4387768775039337e7;

        /// <summary>
        /// Линейный sRGB-цвет черного тела при temperatureKelvin, нормированный
        /// по максимальному каналу. Компоненты ≥ 0, max = 1.
        /// </summary>
        public static Vector3d FromTemperature(double temperatureKelvin)
        {
            if (!(temperatureKelvin > 0d) || double.IsInfinity(temperatureKelvin))
            {
                throw new ArgumentOutOfRangeException(nameof(temperatureKelvin), "Температура обязана быть положительной и конечной.");
            }

            double x = 0d;
            double y = 0d;
            double z = 0d;
            for (int lambda = 380; lambda <= 780; lambda += 5)
            {
                double planck = PlanckSpectralRadiance(lambda, temperatureKelvin);
                x += CieX(lambda) * planck;
                y += CieY(lambda) * planck;
                z += CieZ(lambda) * planck;
            }

            // XYZ → линейный sRGB (стандартная матрица sRGB D65).
            double r = (3.2406255d * x) + (-1.537208d * y) + (-0.4986286d * z);
            double g = (-0.9689307d * x) + (1.8757561d * y) + (0.0415175d * z);
            double b = (0.0557101d * x) + (-0.2040261d * y) + (1.0569959d * z);

            r = Math.Max(0d, r);
            g = Math.Max(0d, g);
            b = Math.Max(0d, b);
            double max = Math.Max(r, Math.Max(g, b));
            if (max <= 0d)
            {
                return new Vector3d(1d, 1d, 1d);
            }

            return new Vector3d(r / max, g / max, b / max);
        }

        /// <summary>Спектральная яркость Планка (произвольный общий множитель — он уходит в нормировку).</summary>
        private static double PlanckSpectralRadiance(double lambdaNm, double temperatureKelvin)
        {
            double lambda5 = lambdaNm * lambdaNm * lambdaNm * lambdaNm * lambdaNm;
            return 1d / (lambda5 * (Math.Exp(C2 / (lambdaNm * temperatureKelvin)) - 1d));
        }

        /// <summary>Много-лобовый гауссиан (σ одно до μ, другое после — фит СИЕ-кривых).</summary>
        private static double PiecewiseGaussian(double lambda, double mu, double sigma1, double sigma2)
        {
            double t = lambda - mu;
            double sigma = t < 0d ? sigma1 : sigma2;
            return Math.Exp(-(t * t) / (2d * sigma * sigma));
        }

        private static double CieX(double lambda)
        {
            return (1.056d * PiecewiseGaussian(lambda, 599.8d, 37.9d, 31.0d))
                + (0.362d * PiecewiseGaussian(lambda, 442.0d, 16.0d, 26.7d))
                - (0.065d * PiecewiseGaussian(lambda, 501.1d, 20.4d, 26.2d));
        }

        private static double CieY(double lambda)
        {
            return (0.821d * PiecewiseGaussian(lambda, 568.8d, 46.9d, 40.5d))
                + (0.286d * PiecewiseGaussian(lambda, 530.9d, 16.3d, 31.1d));
        }

        private static double CieZ(double lambda)
        {
            return (1.217d * PiecewiseGaussian(lambda, 437.0d, 11.8d, 36.0d))
                + (0.681d * PiecewiseGaussian(lambda, 459.0d, 26.0d, 13.8d));
        }
    }
}
