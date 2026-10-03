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
        internal static readonly Color Grass = Srgb(0.102f, 0.290f, 0.145f);
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

        /// <summary>
        /// Допуск уровня моря в АБСОЛЮТНЫХ метрах — тот же, что в
        /// PlanetSurface.shader (SeaEpsilon). Раньше здесь стояло
        /// amplitude*0.001: при амплитуде 9144 м это 9.14 м, и полоса СУШИ в
        /// девять метров над водой считалась водой — CPU-палитра красила
        /// прибрежную равнину океаном, а шейдер (у него допуск уже был
        /// абсолютным) — сушей. Теперь обе стороны решают одинаково.
        /// </summary>
        internal static double SeaEpsilon(double amplitude)
        {
            return System.Math.Max(0.05d, amplitude * 1e-6d);
        }

        internal static Color HeightColorEx(
            double height, double seaLevel, double amplitude,
            double slopeTan, double colorMask,
            double rockSlopeTan, double rockWidth,
            double snowSlopeTan, double maskStrength,
            double colorDetail = 0d, double detailStrength = 0d,
            double latitudeRadians = 0d, double rockHeightMin = 0d,
            double beachHeightMeters = 0d)
        {
            if (rockSlopeTan <= 0d && snowSlopeTan <= 0d && maskStrength == 0d
                && detailStrength == 0d && beachHeightMeters <= 0d)
            {
                return HeightColorLegacy(height, seaLevel, amplitude);
            }

            bool maskOn = maskStrength != 0d;
            Color c = BaseColor(height, seaLevel, amplitude, colorMask, maskOn, maskStrength, latitudeRadians, beachHeightMeters);
            bool isSea = height <= seaLevel + SeaEpsilon(amplitude);
            // Пляж — абсолютными метрами: моттлинг и скала его не трогают
            // (паритет шейдеру), даже если маска подняла t выше песчаной полосы.
            // Границы берём те же, что у BaseColor (SandWeight): жёсткий порог
            // здесь включал бы пятна почвы/зелени ровно на 18 м, и на ровном
            // берегу моттлинг сам рисовал бы ту же линию, ради которой он снят.
            double beachWeight = SandWeight(height - seaLevel, beachHeightMeters);
            bool isBeach = !isSea && beachWeight > 0d;

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
            if (!isSea && !isBeach && detailStrength != 0d)
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
            if (!isSea && !isBeach && rockSlopeTan > 0d && slopeTan >= rockSlopeTan
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
            return BaseColor(height, seaLevel, amplitude, 0d, false, 0d, 0d, 0d);
        }

        private static bool IsSnowBand(double height, double seaLevel, double amplitude)
        {
            double t = (height - seaLevel) / System.Math.Max(1d, amplitude);
            return t >= 0.55d;
        }

        private static Color BaseColor(
            double height, double seaLevel, double amplitude,
            double mask, bool maskOn, double maskStrength, double latitudeRadians,
            double beachHeightMeters)
        {
            if (height <= seaLevel + SeaEpsilon(amplitude))
            {
                return Sea;
            }

            // Пляж — абсолютными метрами над морем, маской не стирается:
            // иначе на «плюсовых» берегах зелень начинается от уреза воды.
            // Не ступенька, а затухание: полная полоса песка только до 0.4·beach,
            // к 1.6·beach от песка не остаётся ничего, и ровно на beach — половина.
            // Ступенчатый край на сфере проецировался в прямую линию через весь
            // кадр (серая полоса у горизонта), а на планете высоты это просто
            // граница константного цвета. Зеркалит smoothstep в PlanetSurface.
            double sandW = SandWeight(height - seaLevel, beachHeightMeters);
            if (sandW >= 1d)
            {
                return Sand;
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

            // Биом по влажности: сухо → пустыня → сухая трава → луг → лес.
            // Ниже НИКАКОЙ нормированной «песчаной» полосы: песок — это только
            // абсолютный пляж (beachHeightMeters) выше по коду. Полоса t<0.03 при
            // амплитуде 9144 м — это 274 м высоты, и она красила песком ВСЮ
            // прибрежную равнину (а Grass/Tree ещё и отсекали её по MinNormalizedHeight,
            // т.е. зелёная по рельефу земля оставалась без травы).
            //
            // Кривая wet01 — общая с декором (GroundDecorDistribution.BiomeWetness,
            // зеркало в PlanetSurface.shader). Мягкая, tanh вместо clamp: маска
            // SampleColorNoise на сфере широкая (p10=−0.33, p90=+0.32), и старая
            // clamp(0.5+1.6·mask) САТУРИРОВАЛА 21% планеты в ровные 0/1: четверть
            // суши получала wet ровно 0, по краям была ровная «пустыня/лес», а
            // деревья (WetMin=0.2) не росли нигде на площади в десятки километров.
            double wet = maskOn ? GroundDecorDistribution.BiomeWetness(mask) : 0.5d;
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

            // Песок — поверх биома, а не вместо ветки: на затухании полосы
            // зелёный биом должен проступать сквозь песок, а не исчезать.
            if (sandW > 0d)
            {
                c = Color.Lerp(c, Sand, (float)sandW);
            }

            return c;
        }

        /// <summary>
        /// Вес песка по высоте над морем: 1 до 0.4·beach, 0 с 1.6·beach,
        /// ровно 0.5 на самой границе пляжа. Нулевой beach — пляжа нет.
        /// Единственный источник чисел: шейдер и декор берут тот же beach.
        /// </summary>
        internal static double SandWeight(double aboveSea, double beachHeightMeters)
        {
            if (beachHeightMeters <= 0d)
            {
                return 0d;
            }

            return 1d - Smoothstep01((aboveSea - (0.4d * beachHeightMeters)) / (1.2d * beachHeightMeters));
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
