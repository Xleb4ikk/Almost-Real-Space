using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// ЕДИНЫЙ источник физических коэффициентов атмосферы для CPU и GPU.
    ///
    /// До этого коэффициенты рассеяния жили в двух несогласованных местах:
    /// SkyPhysics.ExtinctionPerMeter (эмпирический вектор (0.34,1,3.05) с
    /// подгонкой масштаба) и художественные RayleighColor/MieColor в шейдере.
    /// Из-за рассинхрона цвет диска Солнца (SkyEnvironment → SunDisc) и цвет
    /// неба (купол) считались по разным моделям — отсюда, в частности, шов на
    /// закате. Теперь всё, что читает β, берёт их здесь.
    ///
    /// Значения — реальные коэффициенты рассеяния Рэлея/Ми и поглощения озона
    /// (ссылка: Hillaire, "A Scalable and Production Ready Sky and Atmosphere
    /// Rendering Technique", EGSR 2020; те же числа использует UE SkyAtmosphere
    /// и HDRP Physically Based Sky).
    ///
    /// Высотные профили: экспонента с отдельными масштабными высотами для
    /// Рэлея и Ми (раньше была одна H и HM = H/4 — не физично), озон —
    /// треугольный слой около 25 км.
    ///
    /// Плюс здесь же CPU-генерация двух LUT, которые шейдер только сэмплирует
    /// (никакого вторичного марша к Солнцу в шейдере):
    ///   * Transmittance LUT — прозрачность точки к Солнцу от (высота, cos θ);
    ///   * Multi-Scattering LUT — приближение бесконечного многократного
    ///     рассеяния по Hillaire (уравнения 9-10), без него горизонт
    ///     выжигается в белый, а зенит остаётся тусклым.
    ///
    /// Модуль чистый (только double + Vector3d) — тестируется на стенде.
    /// </summary>
    public static class AtmosphereOptics
    {
        // Рэлеевское рассеяние на уровне моря (1/м), каналы R~680 / G~550 / B~440 нм.
        public static readonly Vector3d EarthRayleighScattering = new Vector3d(5.802e-6, 13.558e-6, 33.1e-6);

        // Ми-рассеяние на уровне моря (1/м), практически не зависит от длины волны.
        public const double EarthMieScattering = 3.996e-6;

        // Поглощение озоном (1/м) на пике слоя.
        public static readonly Vector3d EarthOzoneAbsorption = new Vector3d(0.650e-6, 1.881e-6, 0.085e-6);

        private static readonly Vector3d One = new Vector3d(1d, 1d, 1d);

        public const double EarthSeaLevelDensityKgPerCubicMeter = 1.225;
        public const double EarthRayleighScaleHeightMeters = 8000d;
        public const double EarthMieScaleHeightMeters = 1200d;
        public const double EarthOzoneCenterAltitudeMeters = 25000d;
        public const double EarthOzoneHalfWidthMeters = 15000d;
        public const double EarthGroundAlbedo = 0.1d;

        /// <summary>Изотропная фаза (1/4π): к многократному рассеянию анизотропия теряется.</summary>
        public const double IsotropicPhase = 1d / (4d * Math.PI);

        /// <summary>Набор коэффициентов и высотных параметров одного тела.</summary>
        public struct Coefficients
        {
            public Vector3d RayleighScattering;   // 1/м на уровне моря
            public Vector3d MieScattering;         // 1/м на уровне моря
            public Vector3d OzoneAbsorption;       // 1/м на пике слоя
            public double RayleighScaleHeight;     // м
            public double MieScaleHeight;          // м
            public double OzoneCenterAltitude;     // м
            public double OzoneHalfWidth;          // м
            public double MieAnisotropy;           // -1..1
            public double GroundAlbedo;            // 0..1
            public bool OzoneEnabled;
        }

        /// <summary>
        /// Собирает коэффициенты тела из физических полей профиля. Плотность
        /// у поверхности линейно масштабирует все β (разреженная атмосфера —
        /// меньше рассеяния), высоты берутся земные, если в профиле 0.
        /// </summary>
        public static Coefficients FromProfile(
            double seaLevelDensityKgPerCubicMeter,
            double rayleighScaleHeightMeters,
            double mieScaleHeightMeters,
            double mieAnisotropy,
            bool ozoneEnabled,
            double aerosolScale = 1d)
        {
            double densityScale = seaLevelDensityKgPerCubicMeter > 0d
                ? seaLevelDensityKgPerCubicMeter / EarthSeaLevelDensityKgPerCubicMeter
                : 0d;
            // Turbidity: количество аэрозоля у поверхности (не высотная форма — её задаёт Hm).
            double mieScale = densityScale * (aerosolScale > 0d ? aerosolScale : 0d);

            Coefficients c;
            c.RayleighScattering = EarthRayleighScattering * densityScale;
            c.MieScattering = new Vector3d(EarthMieScattering * mieScale, EarthMieScattering * mieScale, EarthMieScattering * mieScale);
            c.OzoneAbsorption = EarthOzoneAbsorption * densityScale;
            c.RayleighScaleHeight = rayleighScaleHeightMeters > 0d ? rayleighScaleHeightMeters : EarthRayleighScaleHeightMeters;
            c.MieScaleHeight = mieScaleHeightMeters > 0d ? mieScaleHeightMeters : EarthMieScaleHeightMeters;
            c.OzoneCenterAltitude = EarthOzoneCenterAltitudeMeters;
            c.OzoneHalfWidth = EarthOzoneHalfWidthMeters;
            c.MieAnisotropy = Clamp(mieAnisotropy, -0.9d, 0.9d);
            c.GroundAlbedo = EarthGroundAlbedo;
            c.OzoneEnabled = ozoneEnabled;
            return c;
        }

        /// <summary>Треугольный высотный профиль озонового слоя (как в HDRP PBS), 0..1.</summary>
        public static double OzoneDensity(in Coefficients c, double height)
        {
            if (!c.OzoneEnabled || c.OzoneHalfWidth <= 0d)
            {
                return 0d;
            }

            return Saturate(1d - Math.Abs((height - c.OzoneCenterAltitude) / c.OzoneHalfWidth));
        }

        /// <summary>Полная экстинкция (рассеяние Рэлея/Ми + поглощение озоном), 1/м.</summary>
        public static Vector3d Extinction(in Coefficients c, double height)
        {
            double dR = Math.Exp(-height / c.RayleighScaleHeight);
            double dM = Math.Exp(-height / c.MieScaleHeight);
            double dO = OzoneDensity(c, height);

            return (c.RayleighScattering * dR) + (c.MieScattering * dM) + (c.OzoneAbsorption * dO);
        }

        /// <summary>Рассеяние (без поглощения) — источник многократного рассеяния, 1/м.</summary>
        public static Vector3d Scattering(in Coefficients c, double height)
        {
            double dR = Math.Exp(-height / c.RayleighScaleHeight);
            double dM = Math.Exp(-height / c.MieScaleHeight);

            return (c.RayleighScattering * dR) + (c.MieScattering * dM);
        }

        /// <summary>
        /// Пересечение луча (точка на радиусе r, косинус зенита cosChi) со сферой
        /// радиуса radius. Численно устойчивая форма через разность квадратов:
        /// t = r·(−cosChi ± √d), d = (radius/r)² − (1 − cosChi²).
        /// </summary>
        public static bool IntersectSphere(double radius, double r, double cosChi, out double t0, out double t1)
        {
            double ratio = radius / r;
            double d = (ratio * ratio) - Saturate(1d - (cosChi * cosChi));
            if (d < 0d)
            {
                t0 = 0d;
                t1 = 0d;
                return false;
            }

            double s = Math.Sqrt(d);
            t0 = r * (-cosChi - s);
            t1 = r * (-cosChi + s);
            return true;
        }

        /// <summary>Косинус видимого горизонта для наблюдателя на радиусе r.</summary>
        public static double CosineOfHorizon(double planetRadius, double r)
        {
            double sinHor = planetRadius / r;
            return -Math.Sqrt(Saturate(1d - (sinHor * sinHor)));
        }

        /// <summary>
        /// Полоса сглаживания горизонта по косинусу зенита (~±0.46°): у Солнца
        /// конечный угловой радиус (~0.26°) плюс рефракция у горизонта (~0.5°),
        /// поэтому прямой свет гаснет не ступенькой, а за полградуса захода.
        /// Глубоко ниже горизонта — ровно 0.
        /// </summary>
        public const double HorizonSoftening = 0.008d;

        /// <summary>
        /// Прозрачность к Солнцу для точки на радиусе r и косинусе зенита cosTheta.
        /// Ниже геометрического горизонта луч упирается в планету → 0, но с
        /// мягкой полосой HorizonSoftening (конечный диск + рефракция), иначе
        /// заход даёт видимый поп диска/света за один кадр. Численно
        /// интегрируем экспоненциальную плотность (согласовано с GPU-моделью).
        /// </summary>
        public static Vector3d TransmittanceToSpace(
            in Coefficients c, double planetRadius, double atmosphereRadius, double r, double cosTheta, int steps = 8)
        {
            double cosHor = CosineOfHorizon(planetRadius, Math.Max(r, planetRadius));
            double horizonFade = Saturate((cosTheta - (cosHor - HorizonSoftening)) / (2d * HorizonSoftening));
            horizonFade = horizonFade * horizonFade * (3d - (2d * horizonFade));
            if (horizonFade <= 0d)
            {
                return Vector3d.Zero;
            }

            if (!IntersectSphere(atmosphereRadius, r, cosTheta, out _, out double t1))
            {
                return One;
            }

            double tExit = Math.Max(t1, 0d);
            if (tExit <= 0d)
            {
                return One;
            }

            double ds = tExit / steps;
            Vector3d tau = Vector3d.Zero;
            for (int i = 0; i < steps; i++)
            {
                double t = (i + 0.5d) * ds;
                double pr = Math.Sqrt((r * r) + (2d * r * cosTheta * t) + (t * t));
                double height = Math.Max(0d, pr - planetRadius);
                tau += Extinction(c, height) * ds;
            }

            return Exp(-tau) * horizonFade;
        }

        /// <summary>
        /// Перевёрнутый для GPU индекс высоты (линейный). Согласовано с шейдером:
        /// uv = (cosTheta·0.5 + 0.5, height / atmosphereDepth).
        /// </summary>
        public static double UvCosTheta(double cosTheta) => Saturate(cosTheta * 0.5d + 0.5d);

        public static double UvHeight(double height, double atmosphereDepth) =>
            atmosphereDepth > 0d ? Saturate(height / atmosphereDepth) : 0d;

        /// <summary>Плоская LUT (RGB каналы), row-major: y * Width + x.</summary>
        public struct Lut
        {
            public int Width;
            public int Height;
            public Vector3d[] Pixels;
        }

        /// <summary>
        /// Transmittance LUT: по X — cos θ (низ солнца → верх), по Y — высота.
        /// Ниже горизонта хранится 0 (Солнце закрыто планетой).
        /// </summary>
        public static Lut BuildTransmittanceLut(
            in Coefficients c, double planetRadius, double atmosphereRadius, int size = 64)
        {
            size = Math.Max(2, size);
            double depth = Math.Max(1d, atmosphereRadius - planetRadius);

            var lut = new Lut { Width = size, Height = size, Pixels = new Vector3d[size * size] };
            for (int y = 0; y < size; y++)
            {
                double height = depth * (y + 0.5d) / size;
                double r = planetRadius + height;
                for (int x = 0; x < size; x++)
                {
                    double cosTheta = ((x + 0.5d) / size) * 2d - 1d;
                    lut.Pixels[(y * size) + x] = TransmittanceToSpace(c, planetRadius, atmosphereRadius, r, cosTheta);
                }
            }

            return lut;
        }

        /// <summary>
        /// Multi-Scattering LUT — приближение бесконечного многократного рассеяния
        /// (Hillaire, EGSR 2020, ур. 9-10): усредняем по сфере однократное
        /// рассеяние и свет от планеты, копим «пропуск» многократного, затем
        /// F_ms = 1/(1 − radianceMS), MS = radiance · F_ms.
        /// Карта: uv = (cos θ·0.5 + 0.5, height / atmosphereDepth).
        /// </summary>
        public static Lut BuildMultiScatteringLut(
            in Coefficients c, double planetRadius, double atmosphereRadius, int size = 32, int sampleCount = 64)
        {
            size = Math.Max(2, size);
            sampleCount = Math.Max(4, sampleCount);
            double depth = Math.Max(1d, atmosphereRadius - planetRadius);

            var lut = new Lut { Width = size, Height = size, Pixels = new Vector3d[size * size] };
            double dS = 1d / sampleCount;

            for (int y = 0; y < size; y++)
            {
                double height = depth * (y + 0.5d) / size;
                double r = planetRadius + height;

                for (int x = 0; x < size; x++)
                {
                    double cosChi = ((x + 0.5d) / size) * 2d - 1d;
                    double sinChi = Math.Sqrt(Saturate(1d - (cosChi * cosChi)));
                    var origin = new Vector3d(0d, r, 0d);
                    var sunDir = new Vector3d(0d, cosChi, sinChi);

                    Vector3d radiance = Vector3d.Zero;
                    Vector3d radianceMs = Vector3d.Zero;

                    for (int s = 0; s < sampleCount; s++)
                    {
                        Vector3d viewDir = UniformSphere(s, sampleCount);
                        IntegrateSkyRay(c, planetRadius, atmosphereRadius, origin, viewDir, sunDir,
                            out Vector3d skyColor, out Vector3d multiScattering);
                        radiance += skyColor;
                        radianceMs += multiScattering;
                    }

                    radiance *= dS;
                    radianceMs *= dS;

                    Vector3d fMs = new Vector3d(
                        1d / Math.Max(1e-3d, 1d - radianceMs.X),
                        1d / Math.Max(1e-3d, 1d - radianceMs.Y),
                        1d / Math.Max(1e-3d, 1d - radianceMs.Z));

                    lut.Pixels[(y * size) + x] = new Vector3d(
                        radiance.X * fMs.X, radiance.Y * fMs.Y, radiance.Z * fMs.Z);
                }
            }

            return lut;
        }

        /// <summary>
        /// Интегрирует вдоль луча от точки origin (в планетоцентрическом кадре)
        /// до выхода из атмосферы или до поверхности. Возвращает цвет неба и
        /// накопленное многократное рассеяние (для LUT).
        /// </summary>
        private static void IntegrateSkyRay(
            in Coefficients c, double planetRadius, double atmosphereRadius,
            Vector3d origin, Vector3d viewDir, Vector3d sunDir,
            out Vector3d skyColor, out Vector3d multiScattering)
        {
            double r = origin.Magnitude;
            double cosView = viewDir.Y; // origin = (0, r, 0), N = +Y
            double cosHorizon = CosineOfHorizon(planetRadius, r);
            bool hitsGround = cosView < cosHorizon;

            double tExit;
            if (hitsGround)
            {
                if (!IntersectSphere(planetRadius, r, cosView, out double g0, out _))
                {
                    skyColor = Vector3d.Zero;
                    multiScattering = Vector3d.Zero;
                    return;
                }

                tExit = Math.Max(g0, 0d);
            }
            else
            {
                if (!IntersectSphere(atmosphereRadius, r, cosView, out _, out double a1) || a1 <= 0d)
                {
                    skyColor = Vector3d.Zero;
                    multiScattering = Vector3d.Zero;
                    return;
                }

                tExit = a1;
            }

            if (tExit <= 0d)
            {
                skyColor = Vector3d.Zero;
                multiScattering = Vector3d.Zero;
                return;
            }

            const int steps = 16;
            skyColor = Vector3d.Zero;
            multiScattering = Vector3d.Zero;
            Vector3d transmittance = One;

            for (int i = 0; i < steps; i++)
            {
                double t0 = (i / (double)steps);
                double t1 = ((i + 1d) / steps);
                double s0 = t0 * t0 * tExit;
                double s1 = t1 * t1 * tExit;
                double t = (s0 + s1) * 0.5d;
                double dt = s1 - s0;

                Vector3d p = origin + (viewDir * t);
                double pr = Math.Max(p.Magnitude, planetRadius);
                double h = pr - planetRadius;
                Vector3d n = p / pr;

                Vector3d sigmaE = Extinction(c, h);
                Vector3d scattering = Scattering(c, h);
                Vector3d transSeg = Exp(-sigmaE * dt);

                multiScattering += IntegrateOverSegment(scattering, transSeg, transmittance, sigmaE);

                double cosSun = Vector3d.Dot(n, sunDir);
                Vector3d sunTransmittance = TransmittanceToSpace(c, planetRadius, atmosphereRadius, pr, cosSun);
                Vector3d source = VecMul(sunTransmittance, scattering * IsotropicPhase);
                skyColor += IntegrateOverSegment(source, transSeg, transmittance, sigmaE);

                transmittance = VecMul(transmittance, transSeg);
            }

            if (hitsGround)
            {
                Vector3d p = origin + (viewDir * tExit);
                double pr = Math.Max(p.Magnitude, planetRadius);
                Vector3d n = p / pr;
                double cosSun = Vector3d.Dot(n, sunDir);
                double cosHorizonGround = CosineOfHorizon(planetRadius, pr);
                Vector3d sunIntensity = cosSun >= cosHorizonGround
                    ? TransmittanceToSpace(c, planetRadius, atmosphereRadius, pr, cosSun)
                    : Vector3d.Zero;

                // Планета принимается серой: albedo/π · max(0, N·L) · T_sun.
                double grdf = c.GroundAlbedo / Math.PI;
                double nl = Saturate(cosSun);
                Vector3d planet = new Vector3d(
                    grdf * nl * sunIntensity.X,
                    grdf * nl * sunIntensity.Y,
                    grdf * nl * sunIntensity.Z);
                skyColor += VecMul(planet, transmittance);
            }
        }

        /// <summary>
        /// Аналитический интеграл однородного сегмента (Frostbite/Hillaire):
        /// transmittance · (S − S·transSeg) / sigmaE.
        /// </summary>
        private static Vector3d IntegrateOverSegment(Vector3d source, Vector3d transSeg, Vector3d transmittance, Vector3d sigmaE)
        {
            double sx = (source.X - (source.X * transSeg.X)) / Math.Max(1e-12d, sigmaE.X);
            double sy = (source.Y - (source.Y * transSeg.Y)) / Math.Max(1e-12d, sigmaE.Y);
            double sz = (source.Z - (source.Z * transSeg.Z)) / Math.Max(1e-12d, sigmaE.Z);
            return new Vector3d(
                transmittance.X * sx, transmittance.Y * sy, transmittance.Z * sz);
        }

        /// <summary>Детерминированная равномерная сфера (спираль Фибоначчи).</summary>
        private static Vector3d UniformSphere(int index, int count)
        {
            const double goldenAngle = Math.PI * (3d - 2.2360679774997896964d);
            double y = 1d - (2d * (index + 0.5d) / count);
            double radius = Math.Sqrt(Saturate(1d - (y * y)));
            double phi = goldenAngle * index;
            return new Vector3d(Math.Cos(phi) * radius, y, Math.Sin(phi) * radius);
        }

        /// <summary>
        /// Оценка яркости неба для ambient-засветки поверхности тем же
        /// single-scatter интегратором, что строит Multi-Scattering LUT.
        /// Возвращает «доминирующий оттенок × среднюю энергию»: величина — это
        /// косинус-взвешенное среднее radiance по верхней полусфере (как
        /// irradiance/π), а оттенок — средний цвет ярких областей неба БЕЗ
        /// косинусного веса. Иначе тёплая полоса горизонта на закате тонула бы
        /// и в синеве зенита, и в собственном косинусном подавлении (у горизонта
        /// вес ≈ 0), хотя именно эта полоса освещает землю. Поэтому цвет
        /// согласован с GPU-небом: днём — синий, на закате — тёплый,
        /// ночью ≈ 0 (верхние слои уже не освещены).
        ///
        /// Кадр — локальный: наблюдатель в (0, r, 0), зенит +Y, солнце в
        /// плоскости YZ (cosSunZenith = косинус зенитного угла солнца).
        /// Дёшево: directions × 16 шагов марша (по умолчанию 12 направлений).
        /// Выше верха атмосферы — ровно 0 (рассеивать нечему).
        /// </summary>
        public static Vector3d SkyAmbientRadiance(
            in Coefficients c, double planetRadius, double atmosphereRadius,
            double observerRadius, double cosSunZenith, int directions = 12)
        {
            if (directions <= 0 || observerRadius >= atmosphereRadius)
            {
                return Vector3d.Zero;
            }

            double r = Math.Max(observerRadius, planetRadius);
            double sinSun = Math.Sqrt(Saturate(1d - (cosSunZenith * cosSunZenith)));
            var origin = new Vector3d(0d, r, 0d);
            var sunDir = new Vector3d(0d, cosSunZenith, sinSun);

            Vector3d energySum = Vector3d.Zero;
            Vector3d hueSum = Vector3d.Zero;
            double energyWeight = 0d;
            double hueWeight = 0d;
            for (int i = 0; i < directions; i++)
            {
                Vector3d dir = HemisphereFibonacci(i, directions);
                IntegrateSkyRay(c, planetRadius, atmosphereRadius, origin, dir, sunDir,
                    out Vector3d skyColor, out _);
                double w = dir.Y;
                double lum = Luminance(skyColor);
                energySum += skyColor * w;
                energyWeight += w;
                // Оттенок — по яркости без косинуса: иначе горизонт (вес ≈ 0)
                // никогда не пробьётся, и закат останется синим.
                hueSum += skyColor * lum;
                hueWeight += lum;
            }

            if (energyWeight <= 0d || hueWeight <= 0d)
            {
                return Vector3d.Zero;
            }

            double meanLuminance = Luminance(energySum / energyWeight);
            Vector3d dominantHue = hueSum / hueWeight;
            double hueLuminance = Luminance(dominantHue);
            if (meanLuminance <= 0d || hueLuminance <= 0d)
            {
                return Vector3d.Zero;
            }

            return dominantHue * (meanLuminance / hueLuminance);
        }

        private static double Luminance(Vector3d v) =>
            (0.2126d * v.X) + (0.7152d * v.Y) + (0.0722d * v.Z);

        /// <summary>Детерминированная равномерная верхняя полусфера (спираль Фибоначчи), y ∈ (0, 1].</summary>
        private static Vector3d HemisphereFibonacci(int index, int count)
        {
            const double goldenAngle = Math.PI * (3d - 2.2360679774997896964d);
            double y = 1d - ((index + 0.5d) / count);
            double radius = Math.Sqrt(Saturate(1d - (y * y)));
            double phi = goldenAngle * index;
            return new Vector3d(Math.Cos(phi) * radius, y, Math.Sin(phi) * radius);
        }

        public static Vector3d Exp(Vector3d v) =>
            new Vector3d(Math.Exp(v.X), Math.Exp(v.Y), Math.Exp(v.Z));

        public static Vector3d VecMul(Vector3d a, Vector3d b) =>
            new Vector3d(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

        public static double Saturate(double v) => v < 0d ? 0d : (v > 1d ? 1d : v);

        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    }
}
