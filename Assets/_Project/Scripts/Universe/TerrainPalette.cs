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
        // Цвета заданы в привычном sRGB (как в редакторе), а проект рендерит в
        // Linear: без конверсии альбедо попадает в шейдер как линейное и на
        // выходе гамма-осветляется/обесцвечивается (бледная поверхность).
        internal static float ToLinear(float c)
        {
            return c <= 0.04045f
                ? c / 12.92f
                : (float)System.Math.Pow((c + 0.055f) / 1.055f, 2.4d);
        }

        /// <summary>sRGB→Linear для палитры из TerrainProfile.Palette (T92).</summary>
        internal static Color ToLinear(Color c)
        {
            return new Color(ToLinear(c.r), ToLinear(c.g), ToLinear(c.b), 1f);
        }

        private static Color Srgb(float r, float g, float b)
        {
            return new Color(ToLinear(r), ToLinear(g), ToLinear(b), 1f);
        }

        internal static readonly Color Sand = Srgb(0.80f, 0.73f, 0.55f);
        internal static readonly Color Desert = Srgb(0.74f, 0.60f, 0.36f);
        internal static readonly Color DryGrass = Srgb(0.50f, 0.52f, 0.27f);
        internal static readonly Color Grass = Srgb(0.28f, 0.46f, 0.17f);
        internal static readonly Color Forest = Srgb(0.12f, 0.28f, 0.10f);
        internal static readonly Color Tundra = Srgb(0.46f, 0.44f, 0.37f);
        internal static readonly Color Rock = Srgb(0.45f, 0.41f, 0.36f);
        internal static readonly Color Snow = Srgb(0.96f, 0.97f, 0.99f);
        internal static readonly Color Sea = Srgb(0.05f, 0.20f, 0.42f);
        // Моттлинг земли: пятна почвы (в тёплый коричневый) и сочной зелени.
        internal static readonly Color Soil = Srgb(0.50f, 0.40f, 0.26f);
        internal static readonly Color Lush = Srgb(0.13f, 0.40f, 0.12f);

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
            double snowSlopeTan, double maskStrength,
            double colorDetail = 0d, double detailStrength = 0d,
            double latitudeRadians = 0d, double rockHeightMin = 0d)
        {
            if (rockSlopeTan <= 0d && snowSlopeTan <= 0d && maskStrength == 0d
                && detailStrength == 0d)
            {
                return HeightColorLegacy(height, seaLevel, amplitude);
            }

            bool maskOn = maskStrength != 0d;
            Color c = BaseColor(height, seaLevel, amplitude, colorMask, maskOn, maskStrength, latitudeRadians);
            bool isSea = height <= seaLevel + (amplitude * 0.001d);

            // Нормированная высота t как в шейдере (маска сдвигает пороги):
            // используется и для отсечения пляжа от моттлинга, и для порога
            // rock-override по высоте (камни только на крупных горах).
            double tMasked = (height - seaLevel) / System.Math.Max(1d, amplitude);
            if (maskOn)
            {
                tMasked += colorMask * maskStrength;
            }

            if (tMasked < 0d)
            {
                tMasked = 0d;
            }

            // Мелкомасштабное разнообразие земли: пятна почвы (в тёплый коричневый)
            // и более сочной/тёмной зелени. Только на земле выше пляжной зоны —
            // на песке пятен быть не должно.
            if (!isSea && tMasked >= 0.03d && detailStrength != 0d)
            {
                double d = Clamp(colorDetail, -1d, 1d);
                if (d > 0d)
                {
                    c = Color.Lerp(c, Soil, (float)(d * detailStrength));
                }
                else
                {
                    c = Color.Lerp(c, Lush, (float)((-d) * detailStrength * 0.6d));
                }
            }

            bool snowBand = !isSea && IsSnowBand(height, seaLevel, amplitude);

            // Скала — только на крутых И достаточно высоких склонах (крупные
            // горы). Порог по высоте отсекает пляж, дюны и низменности.
            if (!isSea && rockSlopeTan > 0d && slopeTan >= rockSlopeTan
                && (rockHeightMin <= 0d || tMasked >= rockHeightMin))
            {
                double w = (slopeTan - rockSlopeTan) / System.Math.Max(1e-9d, rockWidth);
                w = System.Math.Min(1d, w);
                w = w * w * (3d - (2d * w));
                c = w >= 1d ? Rock : Color.Lerp(c, Rock, (float)w);
            }

            if (snowBand && snowSlopeTan > 0d && slopeTan > snowSlopeTan)
            {
                c = Rock;
            }

            return c;
        }

        internal static Color HeightColorLegacy(double height, double seaLevel, double amplitude)
        {
            return BaseColor(height, seaLevel, amplitude, 0d, false, 0d, 0d);
        }

        private static bool IsSnowBand(double height, double seaLevel, double amplitude)
        {
            double t = (height - seaLevel) / System.Math.Max(1d, amplitude);
            return t >= 0.55d;
        }

        private static Color BaseColor(
            double height, double seaLevel, double amplitude,
            double mask, bool maskOn, double maskStrength, double latitudeRadians)
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

            if (t < 0.03d)
            {
                return Sand;
            }

            // Биом по влажности: сухо → пустыня → сухая трава → луг → лес.
            // Маска усиливается (×1.6): её сырое значение жмётся к нулю fBm'ом,
            // без усиления весь континент получается одним тоном.
            double wet = maskOn ? Clamp01(0.5d + (Clamp(mask, -1d, 1d) * 1.6d)) : 0.5d;
            Color lowland = BiomeColor(wet);
            Color c;
            if (t < 0.45d)
            {
                c = lowland;
            }
            else if (t < 0.70d)
            {
                c = Color.Lerp(lowland, Rock, (float)((t - 0.45d) * (1d / 0.25d)));
            }
            else
            {
                c = Color.Lerp(Rock, Snow, (float)((t - 0.70d) * (1d / 0.30d)));
            }

            // Полярные широты: тундра у границы леса, ледяная шапка у полюсов.
            double lat01 = Clamp01(System.Math.Abs(latitudeRadians) / (System.Math.PI * 0.5d));
            double tundra = Smoothstep01((lat01 - 0.52d) / 0.22d);
            double ice = Smoothstep01((lat01 - 0.70d) / 0.16d);
            if (tundra > 0d)
            {
                c = Color.Lerp(c, Tundra, (float)(tundra * 0.85d));
            }

            if (ice > 0d)
            {
                c = Color.Lerp(c, Snow, (float)ice);
            }

            return c;
        }

        /// <summary>Цвет низменности по нормированной влажности 0..1.</summary>
        private static Color BiomeColor(double wet)
        {
            if (wet < 0.18d)
            {
                return Desert;
            }

            if (wet < 0.38d)
            {
                return Color.Lerp(Desert, DryGrass, (float)((wet - 0.18d) / 0.20d));
            }

            if (wet < 0.58d)
            {
                return Color.Lerp(DryGrass, Grass, (float)((wet - 0.38d) / 0.20d));
            }

            if (wet < 0.80d)
            {
                return Color.Lerp(Grass, Forest, (float)((wet - 0.58d) / 0.22d));
            }

            return Forest;
        }

        private static double Smoothstep01(double t)
        {
            if (t <= 0d)
            {
                return 0d;
            }

            if (t >= 1d)
            {
                return 1d;
            }

            return t * t * (3d - (2d * t));
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
