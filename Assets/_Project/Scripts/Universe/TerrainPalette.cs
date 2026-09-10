using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Палитра поверхности: высотные зоны (пляж → низины → скалы → снег) плюс
    /// биомы по «влажности» из noise-маски (пустыня/степь/лес). Порядок слоёв:
    /// (0) маска сдвигает t ДО слоёв и задаёт биом; (1) база по высоте;
    /// (2) rock-override по склону; (3) снег только на пологих. Все фичи
    /// выключены (rock/snow/mask = 0) → нейтральная база (= HeightColorLegacy),
    /// что доказано T83.
    /// </summary>
    internal static class TerrainPalette
    {
        private static readonly Color Sand = new Color(0.72f, 0.68f, 0.50f);
        private static readonly Color Desert = new Color(0.64f, 0.54f, 0.34f);
        private static readonly Color Grass = new Color(0.26f, 0.46f, 0.21f);
        private static readonly Color Forest = new Color(0.13f, 0.31f, 0.13f);
        private static readonly Color Rock = new Color(0.40f, 0.36f, 0.32f);
        private static readonly Color Snow = new Color(0.92f, 0.92f, 0.95f);
        private static readonly Color Sea = new Color(0.12f, 0.30f, 0.58f);

        /// <summary>
        /// Тангенс угла склона по косинусу между нормалью и радиалью.
        /// Безразмерен (rise/run) — пороги не едут от масштаба тайла/LOD.
        /// cos ≤ 1e-6 (нависание/вырождение) — считаем склон вертикальным.
        /// </summary>
        internal static double SlopeTan(double cosA)
        {
            if (!(cosA > 1e-6d))
            {
                return 1e6d;
            }

            double sinA = System.Math.Sqrt(System.Math.Max(0d, 1d - (cosA * cosA)));
            return sinA / cosA;
        }

        internal static Color HeightColorEx(
            double height, double seaLevel, double amplitude,
            double slopeTan, double colorMask,
            double rockSlopeTan, double rockWidth,
            double snowSlopeTan, double maskStrength)
        {
            if (rockSlopeTan <= 0d && snowSlopeTan <= 0d && maskStrength == 0d)
            {
                return HeightColorLegacy(height, seaLevel, amplitude);
            }

            bool maskOn = maskStrength != 0d;
            Color c = BaseColor(height, seaLevel, amplitude, colorMask, maskOn, maskStrength);
            bool isSea = height <= seaLevel + (amplitude * 0.001d);
            bool snowBand = !isSea && IsSnowBand(height, seaLevel, amplitude);

            if (!isSea && rockSlopeTan > 0d && slopeTan >= rockSlopeTan)
            {
                double w = (slopeTan - rockSlopeTan) / System.Math.Max(1e-9d, rockWidth);
                w = System.Math.Min(1d, w);
                w = w * w * (3d - (2d * w));
                c = Color.Lerp(c, Rock, (float)w);
            }

            if (snowBand && snowSlopeTan > 0d && slopeTan > snowSlopeTan)
            {
                c = Rock;
            }

            return c;
        }

        internal static Color HeightColorLegacy(double height, double seaLevel, double amplitude)
        {
            return BaseColor(height, seaLevel, amplitude, 0d, false, 0d);
        }

        private static bool IsSnowBand(double height, double seaLevel, double amplitude)
        {
            double t = (height - seaLevel) / System.Math.Max(1d, amplitude);
            return t >= 0.62d;
        }

        private static Color BaseColor(double height, double seaLevel, double amplitude, double mask, bool maskOn, double maskStrength)
        {
            if (height <= seaLevel + (amplitude * 0.001d))
            {
                return Sea;
            }

            double t = (height - seaLevel) / System.Math.Max(1d, amplitude);
            if (maskOn)
            {
                t += mask * maskStrength;
            }

            if (t < 0d)
            {
                t = 0d;
            }

            if (t < 0.05d)
            {
                return Sand;
            }

            // Биом по влажности: маска −1 (сухо) → пустыня, +1 (влажно) → лес.
            double wet = maskOn ? Clamp01(0.5d + (0.5d * Clamp(mask, -1d, 1d))) : 0.5d;
            Color lowland;
            if (wet < 0.5d)
            {
                lowland = Color.Lerp(Desert, Grass, (float)(wet * 2d));
            }
            else
            {
                lowland = Color.Lerp(Grass, Forest, (float)((wet - 0.5d) * 2d));
            }

            if (t < 0.4d)
            {
                return lowland;
            }

            if (t < 0.62d)
            {
                return Color.Lerp(lowland, Rock, (float)((t - 0.4d) * (1d / 0.22d)));
            }

            return Color.Lerp(Rock, Snow, (float)((t - 0.62d) * (1d / 0.38d)));
        }

        private static double Clamp(double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        private static double Clamp01(double v)
        {
            return v < 0d ? 0d : (v > 1d ? 1d : v);
        }
    }
}
