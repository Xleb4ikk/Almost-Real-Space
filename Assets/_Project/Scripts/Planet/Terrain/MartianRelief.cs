using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Parameters of the "Mars-like" relief generator. Plain blittable data: it lives in
    /// TerrainProfile (serialized in the asset), is copied into HeightfieldTerrain and from
    /// there into TerrainNoiseParams, i.e. it is read by the Burst tile job and by the CPU
    /// physics through the SAME function (MartianRelief.SampleHeight).
    ///
    /// Enabled = 0 (the default of every existing asset) means this module is not touched at
    /// all: TerrainNoise.SampleHeight returns the legacy shape bit-for-bit.
    ///
    /// All sizes are given in "real Mars" units (km on a planet of R = 3389.5 km). The planet
    /// can have any radius: lengths are unit-sphere angles, heights are divided by
    /// HeightUnitMeters, and the profile's AmplitudeMeters = HeightUnitMeters * (R / 3389.5 km)
    /// scales the whole relief proportionally (see MarsLikePlanetSetup).
    ///
    /// Structs cannot have field initializers in C# 9, so defaults are zero = "feature off";
    /// ready-made values come from MartianReliefParams.CreateMarsLike().
    /// </summary>
    [System.Serializable]
    public struct MartianReliefParams
    {
        [Tooltip("1 = использовать марсианский рельеф вместо обычной формы. 0 = модуль выключен (legacy бит-в-бит).")]
        public int Enabled;

        [Tooltip("Сколько метров РЕАЛЬНОГО Марса соответствует 1.0 нормированной высоты. AmplitudeMeters профиля = это значение * (R / 3389.5 км).")]
        public double HeightUnitMeters;

        [Header("Дихотомия: низкие северные равнины / древнее южное нагорье")]
        public double DichotomyAxisLatDeg;
        public double DichotomyAxisLonDeg;
        [Tooltip("Порог по косинусу угла к оси низины: 0 = половина шара, 0.15 = ~42% шара.")]
        public double DichotomyThreshold;
        public double DichotomyWidth;
        public double DichotomyBoundaryWarp;
        public double DichotomyDepthKm;
        public double HighlandBaseKm;

        [Header("Общая фактура")]
        public double SwellAmplitudeKm;
        public double SwellFrequency;
        public double RoughAmplitudeKm;
        public double RoughFrequency;
        public int RoughOctaves;
        public double RoughLowlandFactor;
        public double MassifAmplitudeKm;
        public double MassifFrequency;
        public double WrinkleAmplitudeKm;
        public double HummockAmplitudeKm;
        public double HummockFrequency;
        public int HummockOctaves;
        public double WarpStrength;
        public double WarpFrequency;

        [Header("Кратеры (хэш-сетка, степенной закон размеров)")]
        [Tooltip("Множитель плотности. 1 = как на Марсе (~1.6e5 кратеров > 1 км).")]
        public double CraterDensity;
        [Tooltip("Сколько октав диаметра считать (каждая октава = 27 ячеек). Больше = мельче кратеры и дороже.")]
        public int CraterClasses;
        public double CraterLargestDiameterKm;
        [Tooltip("Доля плотности кратеров на низменностях (они моложе).")]
        public double CraterLowlandFactor;
        public double CraterDepthScale;
        [Tooltip("0..1: насколько кратеры разрушены и засыпаны в среднем (древность).")]
        public double CraterDegradation;

        [Header("Ударные бассейны (Эллада / Аргир / Исида)")]
        public int BasinCount;
        public double BasinRadiusKm;
        public double BasinDepthKm;

        [Header("Вулканизм (Фарсида / Элизий / одиночные щиты)")]
        [Tooltip("0 = нет, 1 = Фарсида-подобная провинция, 2 = ещё и Элизий-подобная.")]
        public int VolcanicProvinces;
        public double RiseHeightKm;
        public double RiseRadiusDeg;
        public double ShieldHeightKm;
        public double ShieldRadiusKm;
        public double GrandShieldHeightKm;
        public double GrandShieldRadiusKm;
        public double CalderaDepthKm;
        public int ScatteredVolcanoes;

        [Header("Каньоны, русла, грабены")]
        public double CanyonLengthDeg;
        public double CanyonWidthKm;
        public double CanyonDepthKm;
        public int OutflowChannels;
        public double ChannelWidthKm;
        public double ChannelDepthKm;
        public int Fossae;

        [Header("Слоистые породы (страты)")]
        [Tooltip("Толщина крупной пачки, км.")]
        public double StrataMajorKm;
        [Tooltip("Толщина тонкого слоя, км.")]
        public double StrataMinorKm;
        [Tooltip("0..1: сила терраcирования в «слоистых» районах.")]
        public double StrataMix;
        [Tooltip("Дополнительная сила на обнажениях: стенки каньонов, кратеров, бассейнов.")]
        public double StrataWallBoost;
        public double StrataRegionFrequency;
        public double StrataRegionThreshold;
        [Tooltip("Наклон слоёв в толщинах крупной пачки (слои не горизонтальны).")]
        public double StrataTilt;
        [Tooltip("0..1: насколько слои красятся в разные тона (через цветовую маску).")]
        public double StrataColorStrength;

        [Header("Полярные шапки (слоистые отложения со спиральными желобами)")]
        public double PolarCapLatitudeDeg;
        public double PolarCapFadeDeg;
        public double PolarCapEdgeJitterDeg;
        public double PolarCapHeightKm;
        public int PolarSpiralArms;
        public double PolarTroughDepthKm;
        public double PolarLayerKm;

        /// <summary>Whole-struct comparison by value (the chunk cache must see live tuning).</summary>
        public bool SameAs(in MartianReliefParams o)
        {
            return Enabled == o.Enabled
                && HeightUnitMeters == o.HeightUnitMeters
                && DichotomyAxisLatDeg == o.DichotomyAxisLatDeg
                && DichotomyAxisLonDeg == o.DichotomyAxisLonDeg
                && DichotomyThreshold == o.DichotomyThreshold
                && DichotomyWidth == o.DichotomyWidth
                && DichotomyBoundaryWarp == o.DichotomyBoundaryWarp
                && DichotomyDepthKm == o.DichotomyDepthKm
                && HighlandBaseKm == o.HighlandBaseKm
                && SwellAmplitudeKm == o.SwellAmplitudeKm
                && SwellFrequency == o.SwellFrequency
                && RoughAmplitudeKm == o.RoughAmplitudeKm
                && RoughFrequency == o.RoughFrequency
                && RoughOctaves == o.RoughOctaves
                && RoughLowlandFactor == o.RoughLowlandFactor
                && MassifAmplitudeKm == o.MassifAmplitudeKm
                && MassifFrequency == o.MassifFrequency
                && WrinkleAmplitudeKm == o.WrinkleAmplitudeKm
                && HummockAmplitudeKm == o.HummockAmplitudeKm
                && HummockFrequency == o.HummockFrequency
                && HummockOctaves == o.HummockOctaves
                && WarpStrength == o.WarpStrength
                && WarpFrequency == o.WarpFrequency
                && CraterDensity == o.CraterDensity
                && CraterClasses == o.CraterClasses
                && CraterLargestDiameterKm == o.CraterLargestDiameterKm
                && CraterLowlandFactor == o.CraterLowlandFactor
                && CraterDepthScale == o.CraterDepthScale
                && CraterDegradation == o.CraterDegradation
                && BasinCount == o.BasinCount
                && BasinRadiusKm == o.BasinRadiusKm
                && BasinDepthKm == o.BasinDepthKm
                && VolcanicProvinces == o.VolcanicProvinces
                && RiseHeightKm == o.RiseHeightKm
                && RiseRadiusDeg == o.RiseRadiusDeg
                && ShieldHeightKm == o.ShieldHeightKm
                && ShieldRadiusKm == o.ShieldRadiusKm
                && GrandShieldHeightKm == o.GrandShieldHeightKm
                && GrandShieldRadiusKm == o.GrandShieldRadiusKm
                && CalderaDepthKm == o.CalderaDepthKm
                && ScatteredVolcanoes == o.ScatteredVolcanoes
                && CanyonLengthDeg == o.CanyonLengthDeg
                && CanyonWidthKm == o.CanyonWidthKm
                && CanyonDepthKm == o.CanyonDepthKm
                && OutflowChannels == o.OutflowChannels
                && ChannelWidthKm == o.ChannelWidthKm
                && ChannelDepthKm == o.ChannelDepthKm
                && Fossae == o.Fossae
                && StrataMajorKm == o.StrataMajorKm
                && StrataMinorKm == o.StrataMinorKm
                && StrataMix == o.StrataMix
                && StrataWallBoost == o.StrataWallBoost
                && StrataRegionFrequency == o.StrataRegionFrequency
                && StrataRegionThreshold == o.StrataRegionThreshold
                && StrataTilt == o.StrataTilt
                && StrataColorStrength == o.StrataColorStrength
                && PolarCapLatitudeDeg == o.PolarCapLatitudeDeg
                && PolarCapFadeDeg == o.PolarCapFadeDeg
                && PolarCapEdgeJitterDeg == o.PolarCapEdgeJitterDeg
                && PolarCapHeightKm == o.PolarCapHeightKm
                && PolarSpiralArms == o.PolarSpiralArms
                && PolarTroughDepthKm == o.PolarTroughDepthKm
                && PolarLayerKm == o.PolarLayerKm;
        }

        // CODEGEN-BEGIN
        /// <summary>
        /// "Mars analogue" preset. Not a copy of the real planet: the same KINDS of structures
        /// (dichotomy, Tharsis-like rise with a chain of shields and a giant shield, a long
        /// canyon system, outflow channels, ancient cratered highlands, impact basins, layered
        /// outcrops, polar layered deposits) with seed-driven placement.
        /// </summary>
        public static MartianReliefParams CreateMarsLike()
        {
            MartianReliefParams m = new MartianReliefParams();
            m.Enabled = 1;
            m.HeightUnitMeters = 60000.0;

            m.DichotomyAxisLatDeg = 52.0;
            m.DichotomyAxisLonDeg = 200.0;
            m.DichotomyThreshold = 0.12;
            m.DichotomyWidth = 0.10;
            m.DichotomyBoundaryWarp = 0.22;
            m.DichotomyDepthKm = 5.0;
            m.HighlandBaseKm = 1.6;

            m.SwellAmplitudeKm = 0.9;
            m.SwellFrequency = 1.5;
            m.RoughAmplitudeKm = 0.55;
            m.RoughFrequency = 10.0;
            m.RoughOctaves = 9;
            m.RoughLowlandFactor = 0.22;
            m.MassifAmplitudeKm = 1.6;
            m.MassifFrequency = 6.0;
            m.WrinkleAmplitudeKm = 0.16;
            m.HummockAmplitudeKm = 0.05;
            m.HummockFrequency = 600.0;
            m.HummockOctaves = 5;
            m.WarpStrength = 0.07;
            m.WarpFrequency = 3.0;

            m.CraterDensity = 1.0;
            m.CraterClasses = 8;
            m.CraterLargestDiameterKm = 420.0;
            m.CraterLowlandFactor = 0.30;
            m.CraterDepthScale = 1.0;
            m.CraterDegradation = 0.65;

            m.BasinCount = 3;
            m.BasinRadiusKm = 1000.0;
            m.BasinDepthKm = 6.0;

            m.VolcanicProvinces = 2;
            m.RiseHeightKm = 6.5;
            m.RiseRadiusDeg = 24.0;
            m.ShieldHeightKm = 11.0;
            m.ShieldRadiusKm = 210.0;
            m.GrandShieldHeightKm = 19.0;
            m.GrandShieldRadiusKm = 310.0;
            m.CalderaDepthKm = 3.0;
            m.ScatteredVolcanoes = 9;

            m.CanyonLengthDeg = 62.0;
            m.CanyonWidthKm = 95.0;
            m.CanyonDepthKm = 7.5;
            m.OutflowChannels = 3;
            m.ChannelWidthKm = 28.0;
            m.ChannelDepthKm = 1.3;
            m.Fossae = 7;

            m.StrataMajorKm = 0.8;
            m.StrataMinorKm = 0.11;
            m.StrataMix = 0.30;
            m.StrataWallBoost = 1.0;
            m.StrataRegionFrequency = 2.2;
            m.StrataRegionThreshold = 0.05;
            m.StrataTilt = 0.9;
            m.StrataColorStrength = 0.85;

            m.PolarCapLatitudeDeg = 81.5;
            m.PolarCapFadeDeg = 1.2;
            m.PolarCapEdgeJitterDeg = 2.2;
            m.PolarCapHeightKm = 2.6;
            m.PolarSpiralArms = 6;
            m.PolarTroughDepthKm = 0.65;
            m.PolarLayerKm = 0.10;
            return m;
        }

        /// <summary>
        /// A different but still Mars-like planet: nudges the global character (cratering,
        /// dichotomy depth, canyon scale, number of volcanoes, bed thickness) by the variant
        /// seed. Positions of every feature are driven by TerrainSeed at run time anyway.
        /// </summary>
        public static MartianReliefParams CreateVariant(int variantSeed)
        {
            MartianReliefParams m = CreateMarsLike();
            m.CraterDensity = 0.6 + 1.1 * Rand01(variantSeed, 1);
            m.CraterDegradation = 0.45 + 0.4 * Rand01(variantSeed, 2);
            m.DichotomyDepthKm = 3.8 + 2.6 * Rand01(variantSeed, 3);
            m.DichotomyThreshold = 0.0 + 0.3 * Rand01(variantSeed, 4);
            m.RiseHeightKm = 4.5 + 4.0 * Rand01(variantSeed, 5);
            m.GrandShieldHeightKm = 14.0 + 9.0 * Rand01(variantSeed, 6);
            m.CanyonLengthDeg = 40.0 + 45.0 * Rand01(variantSeed, 7);
            m.CanyonDepthKm = 5.5 + 5.0 * Rand01(variantSeed, 8);
            m.OutflowChannels = 2 + (int)(4.0 * Rand01(variantSeed, 9));
            m.ScatteredVolcanoes = 4 + (int)(10.0 * Rand01(variantSeed, 10));
            m.VolcanicProvinces = Rand01(variantSeed, 11) < 0.25 ? 1 : 2;
            m.StrataMajorKm = 0.55 + 0.7 * Rand01(variantSeed, 12);
            m.StrataMix = 0.2 + 0.25 * Rand01(variantSeed, 13);
            m.BasinCount = 2 + (int)(3.0 * Rand01(variantSeed, 14));
            m.PolarCapHeightKm = 1.8 + 1.8 * Rand01(variantSeed, 15);
            m.DichotomyAxisLatDeg = 35.0 + 40.0 * Rand01(variantSeed, 16);
            m.DichotomyAxisLonDeg = 360.0 * Rand01(variantSeed, 17);
            return m;
        }

        private static double Rand01(int seed, int salt)
        {
            uint h = (uint)seed * 2654435761u + (uint)salt * 40503u + 12345u;
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return (h >> 8) * (1.0 / 16777216.0);
        }
        // CODEGEN-END
    }

    /// <summary>
    /// Procedural Mars-like heightfield. Pure statics on doubles/double3/uint: no managed
    /// references, no allocations, no static mutable state, so the same code runs inside the
    /// Burst tile job and on the main thread (physics, editor preview) with identical results.
    ///
    /// Everything is a pure function of (params, seed, direction): the relief has no stored
    /// data and no tiles, so it has unlimited resolution; the whole layering of ages is
    /// built from these steps, applied oldest to youngest:
    ///   1. dichotomy + swells + rolling ancient terrain + massifs          (Noachian crust)
    ///   2. impact basins and a power-law field of craters (in 8 octaves)   (late bombardment)
    ///   3. Tharsis-like rise, shield volcanoes, calderas, lava burial       (Hesperian volcanism)
    ///   4. canyons, outflow channels, radial grabens                        (tectonic/fluvial)
    ///   5. strata: terraced "beds" exposed on every wall and in layered terrains
    ///   6. polar layered deposits with spiral troughs                       (Amazonian)
    ///   7. micro hummocks (wind-sculpted ground)
    /// </summary>
    public static class MartianRelief
    {
        private const double MarsRadiusKm = 3389.5;
        private const double Pi = 3.14159265358979323846;
        private const double TwoPi = 6.28318530717958647692;
        private const double DegToRad = 0.017453292519943295;

        // Salts of independent random streams.
        private const int SaltWarp = 100;
        private const int SaltDich = 120;
        private const int SaltSwell = 130;
        private const int SaltRough = 140;
        private const int SaltMassif = 150;
        private const int SaltWrinkle = 160;
        private const int SaltHummock = 170;
        private const int SaltStrata = 180;
        private const int SaltTilt = 190;
        private const int SaltRise = 200;
        private const int SaltTrough = 300;
        private const int SaltCrater = 1000;

        // ------------------------------------------------------------------ basic helpers

        private static double Smooth01(double t)
        {
            t = math.clamp(t, 0.0, 1.0);
            return t * t * (3.0 - (2.0 * t));
        }

        /// <summary>Smoothstep between e0 and e1; works for reversed edges (e1 &lt; e0) too.</summary>
        private static double Smooth(double e0, double e1, double x)
        {
            return Smooth01((x - e0) / (e1 - e0));
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + ((b - a) * t);
        }

        private static uint Mix(uint a)
        {
            a ^= a >> 16;
            a *= 0x7feb352du;
            a ^= a >> 15;
            a *= 0x846ca68bu;
            a ^= a >> 16;
            return a;
        }

        private static uint Hash3(int x, int y, int z, int salt)
        {
            uint h = ((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ ((uint)z * 83492791u) ^ ((uint)salt * 2654435761u);
            return Mix(h + 0x9e3779b9u);
        }

        private static double U01(uint h)
        {
            return (h >> 8) * (1.0 / 16777216.0);
        }

        private static double Rnd(int seed, int a, int b)
        {
            return U01(Hash3(a, b, 7, seed));
        }

        private static double Fade(double t)
        {
            return t * t * t * ((t * ((t * 6.0) - 15.0)) + 10.0);
        }

        private static double Grad(uint h, double x, double y, double z)
        {
            switch (h & 15u)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x + z;
                case 5: return -x + z;
                case 6: return x - z;
                case 7: return -x - z;
                case 8: return y + z;
                case 9: return -y + z;
                case 10: return y - z;
                case 11: return -y - z;
                case 12: return x + y;
                case 13: return -y + z;
                case 14: return -x + y;
                default: return -y - z;
            }
        }

        /// <summary>3D gradient (Perlin) noise, ~[-1, 1]. The salt selects an independent stream.</summary>
        private static double Noise3(double x, double y, double z, int salt)
        {
            double fx = math.floor(x);
            double fy = math.floor(y);
            double fz = math.floor(z);
            int xi = (int)fx;
            int yi = (int)fy;
            int zi = (int)fz;
            double u = x - fx;
            double v = y - fy;
            double w = z - fz;
            double fu = Fade(u);
            double fv = Fade(v);
            double fw = Fade(w);

            double n000 = Grad(Hash3(xi, yi, zi, salt), u, v, w);
            double n100 = Grad(Hash3(xi + 1, yi, zi, salt), u - 1.0, v, w);
            double n010 = Grad(Hash3(xi, yi + 1, zi, salt), u, v - 1.0, w);
            double n110 = Grad(Hash3(xi + 1, yi + 1, zi, salt), u - 1.0, v - 1.0, w);
            double n001 = Grad(Hash3(xi, yi, zi + 1, salt), u, v, w - 1.0);
            double n101 = Grad(Hash3(xi + 1, yi, zi + 1, salt), u - 1.0, v, w - 1.0);
            double n011 = Grad(Hash3(xi, yi + 1, zi + 1, salt), u, v - 1.0, w - 1.0);
            double n111 = Grad(Hash3(xi + 1, yi + 1, zi + 1, salt), u - 1.0, v - 1.0, w - 1.0);

            double x00 = n000 + ((n100 - n000) * fu);
            double x10 = n010 + ((n110 - n010) * fu);
            double x01 = n001 + ((n101 - n001) * fu);
            double x11 = n011 + ((n111 - n011) * fu);
            double y0 = x00 + ((x10 - x00) * fv);
            double y1 = x01 + ((x11 - x01) * fv);
            return (y0 + ((y1 - y0) * fw)) * 0.9;
        }

        /// <summary>fBm on the sphere (~[-0.8, 0.8], normalized by the amplitude sum).</summary>
        private static double Fbm(double3 p, double freq, int octaves, int salt)
        {
            octaves = math.min(octaves, 12);
            double sum = 0.0;
            double amp = 0.5;
            double norm = 0.0;
            double f = freq;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Noise3((p.x * f) + 17.31, (p.y * f) - 9.77, (p.z * f) + 4.13, salt + (i * 13));
                norm += amp;
                amp *= 0.5;
                f *= 2.03;
            }

            return norm > 0.0 ? sum / norm : 0.0;
        }

        /// <summary>Ridged multifractal, ~[0, 1]: ridges branch, valleys stay flat.</summary>
        private static double Ridged(double3 p, double freq, int octaves, int salt)
        {
            octaves = math.min(octaves, 12);
            double sum = 0.0;
            double amp = 0.5;
            double norm = 0.0;
            double f = freq;
            double wgt = 1.0;
            for (int i = 0; i < octaves; i++)
            {
                double n = 1.0 - math.abs(Noise3((p.x * f) + 3.71, (p.y * f) + 11.9, (p.z * f) - 6.2, salt + (i * 17)));
                n *= n;
                double s = n * wgt;
                sum += s * amp;
                norm += amp;
                wgt = math.clamp(s * 2.0, 0.0, 1.0);
                amp *= 0.5;
                f *= 2.0;
            }

            return norm > 0.0 ? sum / norm : 0.0;
        }

        private static double3 LatLonDir(double latDeg, double lonDeg)
        {
            double la = latDeg * DegToRad;
            double lo = lonDeg * DegToRad;
            double c = math.cos(la);
            return new double3(c * math.cos(lo), c * math.sin(lo), math.sin(la));
        }

        private static double3 RandDir(int seed, int a, int b)
        {
            double z = (2.0 * Rnd(seed, a, b)) - 1.0;
            double ang = TwoPi * Rnd(seed, a, b + 1);
            double r = math.sqrt(math.max(0.0, 1.0 - (z * z)));
            return new double3(r * math.cos(ang), r * math.sin(ang), z);
        }

        /// <summary>North unit tangent at a (towards +Z).</summary>
        private static double3 NorthAt(double3 a)
        {
            double3 east = math.cross(new double3(0.0, 0.0, 1.0), a);
            double el = math.length(east);
            if (el < 1e-6)
            {
                east = new double3(0.0, 1.0, 0.0);
            }
            else
            {
                east = east / el;
            }

            return math.cross(a, east);
        }

        private static double3 EastAt(double3 a)
        {
            double3 east = math.cross(new double3(0.0, 0.0, 1.0), a);
            double el = math.length(east);
            if (el < 1e-6)
            {
                return new double3(0.0, 1.0, 0.0);
            }

            return east / el;
        }

        /// <summary>Unit heading at a for the given azimuth (0 = north, pi/2 = east).</summary>
        private static double3 Heading(double3 a, double azimuth)
        {
            return (math.cos(azimuth) * NorthAt(a)) + (math.sin(azimuth) * EastAt(a));
        }

        /// <summary>Point reached from a after moving ang radians along the heading h (h perpendicular to a).</summary>
        private static double3 MoveAlong(double3 a, double3 h, double ang)
        {
            return math.normalize((math.cos(ang) * a) + (math.sin(ang) * h));
        }

        /// <summary>Angle between two unit vectors (stable near 0 and pi).</summary>
        private static double Angle(double3 a, double3 b)
        {
            return math.atan2(math.length(math.cross(a, b)), math.dot(a, b));
        }

        // ------------------------------------------------------------------ strata

        /// <summary>
        /// Terraced "bed" height: flat benches separated by cliffs. Every bed has its own
        /// hardness (hash): a hard bed is a tall cliff, a soft one a gentle slope. Monotonic
        /// and continuous: the beds only redistribute height inside one thickness T.
        /// </summary>
        private static double Terrace(double h, double thick, int salt)
        {
            double ph = h / thick;
            double fl = math.floor(ph);
            double fr = ph - fl;
            double hard = U01(Hash3((int)fl, salt, 3, 91));
            double cf = 0.16 + (0.5 * hard);
            double s = Smooth01((fr - (1.0 - cf)) / cf);
            return (fl + s) * thick;
        }

        private static double Terrace2(double h, double major, double minor)
        {
            double a = Terrace(h, major, 1);
            return Terrace(a, minor, 2);
        }

        // ------------------------------------------------------------------ features

        /// <summary>Lowland weight 0..1 from the analytic dichotomy (no noise: cheap for crater centers).</summary>
        private static double LowlandCheap(in MartianReliefParams m, double3 axis, double3 d)
        {
            double wid = math.max(1e-4, m.DichotomyWidth);
            return Smooth01((math.dot(d, axis) - (m.DichotomyThreshold - wid)) / (2.0 * wid));
        }

        /// <summary>Axis of the low hemisphere with a seed-driven offset (every planet is tilted differently).</summary>
        private static double3 DichotomyAxis(in MartianReliefParams m, int seed)
        {
            double lat = m.DichotomyAxisLatDeg + (18.0 * (Rnd(seed, 5001, 1) - 0.5));
            double lon = m.DichotomyAxisLonDeg + (360.0 * (Rnd(seed, 5001, 2) - 0.5));
            return LatLonDir(lat, lon);
        }

        /// <summary>Province center: 0 = Tharsis-like, 1 = Elysium-like.</summary>
        private static double3 ProvinceCenter(int seed, int province)
        {
            double lat0 = Lerp(-14.0, 20.0, Rnd(seed, 6001, 1));
            double lon0 = 360.0 * Rnd(seed, 6001, 2);
            if (province == 0)
            {
                return LatLonDir(lat0, lon0);
            }

            double lat1 = Lerp(6.0, 32.0, Rnd(seed, 6002, 1));
            double lon1 = lon0 + Lerp(115.0, 205.0, Rnd(seed, 6002, 2));
            return LatLonDir(lat1, lon1);
        }

        /// <summary>
        /// One shield volcano. x = r/R; height above the surroundings; optional basal scarp
        /// (a cliff around the base, as at Olympus Mons) and a summit caldera. Writes cover
        /// (0..1) = how thoroughly the lava buries what lies beneath.
        /// </summary>
        private static double Shield(double theta, double radiusKm, double heightKm, double scarpKm, double calderaFrac, double calderaKm, out double cover)
        {
            double thetaR = radiusKm / MarsRadiusKm;
            double x = theta / thetaR;
            cover = Smooth(1.3, 0.85, x);
            if (x >= 1.0)
            {
                return 0.0;
            }

            double flank = math.pow(math.max(0.0, 1.0 - (x * x)), 1.15);
            double h;
            if (scarpKm > 0.0)
            {
                double edge = 1.0 - Smooth(0.955, 1.0, x);
                h = (scarpKm * edge) + ((heightKm - scarpKm) * flank);
            }
            else
            {
                h = heightKm * flank;
            }

            if (calderaFrac > 0.0)
            {
                double cx = x / calderaFrac;
                if (cx < 1.4)
                {
                    h -= calderaKm * (1.0 - Smooth(0.55, 1.0, cx));
                    h += 0.14 * calderaKm * math.exp(-((cx - 1.05) * (cx - 1.05)) / 0.05);
                }
            }

            return h;
        }

        /// <summary>
        /// All volcanic structures. Returns the height in km; cover = lava burial 0..1 (craters
        /// beneath it fade), exposure of layered flanks is not needed (lava flows are massive).
        /// </summary>
        private static double Volcanism(in MartianReliefParams m, int seed, double3 q, out double cover)
        {
            cover = 0.0;
            double h = 0.0;
            if (m.VolcanicProvinces <= 0 && m.ScatteredVolcanoes <= 0)
            {
                return 0.0;
            }

            if (m.VolcanicProvinces >= 1)
            {
                double3 c0 = ProvinceCenter(seed, 0);
                double th0 = Angle(q, c0);
                double riseR = m.RiseRadiusDeg * DegToRad;
                if (th0 < riseR * 1.4)
                {
                    double ox = 1.0 + (0.14 * Fbm(q, 2.2, 3, SaltRise + (seed * 7919)));
                    double xr = th0 / (riseR * ox);
                    if (xr < 1.0)
                    {
                        h += m.RiseHeightKm * math.pow(0.5 + (0.5 * math.cos(Pi * xr)), 1.3);
                    }

                    cover = math.max(cover, 0.9 * Smooth(1.0, 0.35, xr));
                }

                double chainAz = Pi * Rnd(seed, 6010, 1);
                double3 hc = Heading(c0, chainAz);
                double spacing = 0.19 + (0.05 * Rnd(seed, 6010, 2));
                for (int i = 0; i < 3; i++)
                {
                    double off = (i - 1) * spacing;
                    double3 ci = MoveAlong(c0, hc, off);
                    double th = Angle(q, ci);
                    double rk = m.ShieldRadiusKm * (0.85 + (0.3 * Rnd(seed, 6011, i)));
                    if (th < rk / MarsRadiusKm * 1.35)
                    {
                        double hk = m.ShieldHeightKm * (0.8 + (0.4 * Rnd(seed, 6012, i)));
                        double cv;
                        h += Shield(th, rk, hk, 0.0, 0.17, m.CalderaDepthKm * 0.55, out cv);
                        cover = math.max(cover, cv);
                    }
                }

                // The giant shield with the basal scarp, outside the rise (Olympus-like).
                double ga = chainAz + (Pi * (0.45 + (0.2 * Rnd(seed, 6013, 1))));
                double3 hg = Heading(c0, ga);
                double3 cg = MoveAlong(c0, hg, riseR * (1.05 + (0.15 * Rnd(seed, 6013, 2))));
                double thg = Angle(q, cg);
                double rg = m.GrandShieldRadiusKm;
                if (thg < rg / MarsRadiusKm * 1.35)
                {
                    double cv;
                    h += Shield(thg, rg, m.GrandShieldHeightKm, m.GrandShieldHeightKm * 0.22, 0.13, m.CalderaDepthKm, out cv);
                    cover = math.max(cover, cv);
                }

                // A very broad, low volcano (Alba-like).
                double aa = chainAz - (Pi * (0.35 + (0.2 * Rnd(seed, 6014, 1))));
                double3 ha = Heading(c0, aa);
                double3 ca = MoveAlong(c0, ha, riseR * (0.55 + (0.2 * Rnd(seed, 6014, 2))));
                double tha = Angle(q, ca);
                double ra = m.GrandShieldRadiusKm * 1.9;
                if (tha < ra / MarsRadiusKm * 1.35)
                {
                    double cv;
                    h += Shield(tha, ra, m.GrandShieldHeightKm * 0.28, 0.0, 0.07, m.CalderaDepthKm * 0.4, out cv);
                    cover = math.max(cover, cv);
                }
            }

            if (m.VolcanicProvinces >= 2)
            {
                double3 c1 = ProvinceCenter(seed, 1);
                double th1 = Angle(q, c1);
                double riseR1 = m.RiseRadiusDeg * DegToRad * 0.52;
                if (th1 < riseR1 * 1.4)
                {
                    double ox1 = 1.0 + (0.14 * Fbm(q, 3.1, 3, SaltRise + 5 + (seed * 7919)));
                    double xr1 = th1 / (riseR1 * ox1);
                    if (xr1 < 1.0)
                    {
                        h += m.RiseHeightKm * 0.42 * math.pow(0.5 + (0.5 * math.cos(Pi * xr1)), 1.3);
                    }

                    cover = math.max(cover, 0.85 * Smooth(1.0, 0.35, xr1));
                }

                double az1 = Pi * Rnd(seed, 6020, 1);
                double3 h1 = Heading(c1, az1);
                for (int i = 0; i < 3; i++)
                {
                    double off = (i - 1) * 0.16;
                    double3 ci = MoveAlong(c1, h1, off);
                    double th = Angle(q, ci);
                    double rk = m.ShieldRadiusKm * (i == 1 ? 0.95 : 0.55);
                    if (th < rk / MarsRadiusKm * 1.35)
                    {
                        double hk = m.ShieldHeightKm * (i == 1 ? 0.55 : 0.32);
                        double cv;
                        h += Shield(th, rk, hk, 0.0, 0.16, m.CalderaDepthKm * 0.4, out cv);
                        cover = math.max(cover, cv);
                    }
                }
            }

            int scattered = math.min(m.ScatteredVolcanoes, 24);
            for (int i = 0; i < scattered; i++)
            {
                double3 ci = RandDir(seed, 6100 + i, 1);
                double th = Angle(q, ci);
                bool patera = Rnd(seed, 6100 + i, 4) < 0.4;
                double rk = Lerp(45.0, 130.0, Rnd(seed, 6100 + i, 5));
                if (patera)
                {
                    rk = Lerp(110.0, 260.0, Rnd(seed, 6100 + i, 5));
                }

                if (th < rk / MarsRadiusKm * 1.35)
                {
                    double hk = patera ? Lerp(0.8, 1.7, Rnd(seed, 6100 + i, 6)) : Lerp(1.2, 3.8, Rnd(seed, 6100 + i, 6));
                    double cf = patera ? 0.30 : 0.14;
                    double cd = patera ? hk * 1.1 : hk * 0.45;
                    double cv;
                    h += Shield(th, rk, hk, 0.0, cf, cd, out cv);
                    cover = math.max(cover, cv * 0.8);
                }
            }

            return h;
        }

        /// <summary>
        /// Impact basins (Hellas / Argyre / Isidis-like): a flat sediment-filled floor, steep
        /// terraced walls, a raised rim and a wide ejecta apron. Writes exposure (walls) and
        /// floor (0..1: smooth interior, craters are scarce there).
        /// </summary>
        private static double Basins(in MartianReliefParams m, int seed, double3 q, double3 axis, out double expo, out double floorW)
        {
            expo = 0.0;
            floorW = 0.0;
            double h = 0.0;
            int n = math.min(m.BasinCount, 6);
            for (int i = 0; i < n; i++)
            {
                double3 rd = RandDir(seed, 7000 + i, 1);
                double3 c;
                if (i < 2)
                {
                    // The two biggest lie in the old highlands (Hellas, Argyre).
                    double t = -(0.30 + (0.6 * Rnd(seed, 7000 + i, 4)));
                    double3 perp = rd - (math.dot(rd, axis) * axis);
                    double pl = math.length(perp);
                    perp = pl < 1e-6 ? NorthAt(axis) : perp / pl;
                    c = math.normalize((perp * math.sqrt(1.0 - (t * t))) + (axis * t));
                }
                else if (i == 2)
                {
                    // Isidis-like: on the dichotomy boundary.
                    double t = -0.1 + (0.35 * Rnd(seed, 7000 + i, 4));
                    double3 perp = rd - (math.dot(rd, axis) * axis);
                    double pl = math.length(perp);
                    perp = pl < 1e-6 ? NorthAt(axis) : perp / pl;
                    c = math.normalize((perp * math.sqrt(1.0 - (t * t))) + (axis * t));
                }
                else
                {
                    c = rd;
                }

                double rk = m.BasinRadiusKm * (i == 0 ? 1.0 : (i == 1 ? 0.78 : (i == 2 ? 0.6 : 0.42)));
                double depth = m.BasinDepthKm * (i == 0 ? 1.0 : (i == 1 ? 0.8 : (i == 2 ? 0.62 : 0.5)));
                double thetaR = rk / MarsRadiusKm;
                double th = Angle(q, c);
                double x = th / thetaR;
                if (x > 2.6)
                {
                    continue;
                }

                double rim = 0.32 * depth;
                if (x < 1.0)
                {
                    double wall = math.pow(Smooth(0.34, 1.0, x), 0.8);
                    h += -depth * (1.0 - wall);
                    h += rim * Smooth(0.82, 1.0, x);
                    expo = math.max(expo, Smooth(0.38, 0.9, x));
                    floorW = math.max(floorW, 1.0 - Smooth(0.45, 0.85, x));
                }
                else
                {
                    double ej = rim * math.pow(x, -3.0) * (1.0 - Smooth(1.6, 2.6, x));
                    h += ej;
                }
            }

            return h;
        }

        /// <summary>
        /// Crater field: hash grid with 27 cells per octave class of diameter, power-law size
        /// distribution N(&gt;D) ~ D^-1.7, depth/diameter by Pike's law, flat floors and central
        /// peaks for complex craters, raised rims and ejecta; each crater has its own age
        /// (degradation: infilled and eroded vs. fresh).
        /// </summary>
        private static double Craters(in MartianReliefParams m, int seed, double3 q, double3 axis, double burial, double quiet, out double expo)
        {
            expo = 0.0;
            double total = 0.0;
            int classes = math.min(m.CraterClasses, 10);
            double alpha = 1.7;
            for (int c = 0; c < classes; c++)
            {
                double dmax = m.CraterLargestDiameterKm / math.pow(2.0, c);
                double dmin = dmax * 0.5;
                double rmaxU = 0.5 * dmax / MarsRadiusKm;
                double cell = 4.5 * rmaxU;
                double inv = 1.0 / cell;
                double nGlobal = m.CraterDensity * 0.69 * 1.6e5 * math.pow(dmin, -alpha);
                double pc = math.min(1.0, nGlobal * cell * cell / (4.0 * Pi));
                int ix = (int)math.floor(q.x * inv);
                int iy = (int)math.floor(q.y * inv);
                int iz = (int)math.floor(q.z * inv);
                int salt = SaltCrater + (c * 31) + (seed * 7);
                double ratio = math.pow(dmin / dmax, alpha);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int cx = ix + dx;
                            int cy = iy + dy;
                            int cz = iz + dz;
                            uint hh = Hash3(cx, cy, cz, salt);
                            if (U01(hh) > pc)
                            {
                                continue;
                            }

                            double px = (cx + U01(Mix(hh + 1u))) * cell;
                            double py = (cy + U01(Mix(hh + 2u))) * cell;
                            double pz = (cz + U01(Mix(hh + 3u))) * cell;
                            double plen = math.sqrt((px * px) + (py * py) + (pz * pz));
                            if (math.abs(plen - 1.0) > 0.5 * cell)
                            {
                                continue;
                            }

                            double3 dk = new double3(px / plen, py / plen, pz / plen);
                            double u = U01(Mix(hh + 4u));
                            double rU = (dmin * 0.5 / MarsRadiusKm) * math.pow(1.0 - (u * (1.0 - ratio)), -1.0 / alpha);
                            double dx3 = q.x - dk.x;
                            double dy3 = q.y - dk.y;
                            double dz3 = q.z - dk.z;
                            double r = math.sqrt((dx3 * dx3) + (dy3 * dy3) + (dz3 * dz3));
                            if (r > 2.2 * rU)
                            {
                                continue;
                            }

                            // Younger lowlands, buried craters and the smooth basin floors are sparse.
                            double fac = 1.0 - ((1.0 - m.CraterLowlandFactor) * LowlandCheap(m, axis, dk));
                            if (U01(Mix(hh + 5u)) > fac)
                            {
                                continue;
                            }

                            double x = r / rU;
                            double dKm = 2.0 * rU * MarsRadiusKm;
                            double age = U01(Mix(hh + 6u));
                            double deg = math.min(1.0, m.CraterDegradation * (0.25 + (1.5 * age)));
                            double fill = 0.88 * deg;
                            double depth = (dKm < 7.0 ? 0.2 * dKm : 1.4 * math.pow(dKm / 7.0, 0.27)) * m.CraterDepthScale;
                            double rimH = 0.036 * math.pow(dKm, 0.7) * (1.0 - (0.8 * deg)) * m.CraterDepthScale;
                            double cplx = Smooth(7.0, 40.0, dKm);
                            double fade = (1.0 - burial) * (1.0 - (0.85 * quiet));
                            double dh;
                            if (x < 1.0)
                            {
                                double e = 2.0 + (1.3 * cplx);
                                double bowl = -depth * (1.0 - fill) * (1.0 - math.pow(x, e));
                                double peak = 0.0;
                                if (dKm > 20.0 && dKm < 190.0)
                                {
                                    peak = 0.3 * depth * (1.0 - fill) * Smooth(20.0, 45.0, dKm) * math.exp(-(x / 0.14) * (x / 0.14));
                                }
                                else if (dKm >= 190.0)
                                {
                                    double rr = (x - 0.45) / 0.08;
                                    peak = 0.22 * depth * (1.0 - fill) * math.exp(-rr * rr);
                                }

                                dh = bowl + peak + (rimH * Smooth(0.78, 1.0, x));
                                expo = math.max(expo, Smooth(0.5, 0.97, x) * fade * (0.4 + (0.6 * (1.0 - fill))));
                            }
                            else
                            {
                                dh = rimH * math.pow(x, -3.0) * (1.0 - Smooth(1.5, 2.2, x));
                            }

                            total += dh * fade;
                        }
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// A trough along a great-circle arc: a canyon, an outflow channel or a graben.
        /// a = start point, b = unit heading at a. Returns depth in km (&gt;= 0) and the
        /// exposure of its walls.
        /// </summary>
        private static double Trough(double3 q, double3 a, double3 b, double lenRad, double halfWidthRad, double depthKm, double wiggleRad, double wigFreq, double flare, int salt, out double expo)
        {
            expo = 0.0;
            if (lenRad < 1e-6)
            {
                return 0.0;
            }

            double3 nrm = math.cross(a, b);
            double cr = math.dot(q, nrm);
            if (math.abs(cr) > (halfWidthRad * 2.7) + wiggleRad + 0.01)
            {
                return 0.0;
            }

            double along = math.atan2(math.dot(q, b), math.dot(q, a));
            if (along < -0.05 || along > lenRad + 0.05)
            {
                return 0.0;
            }

            double t = along / lenRad;
            double wn = 0.5 + (0.5 * Noise3((t * 4.0) + 3.7, 1.3, 2.9, salt));
            double width = halfWidthRad * (1.0 + (flare * ((wn * 1.6) - 0.5)));
            double off = wiggleRad * Noise3((t * wigFreq) + 5.1, 2.7, 0.9, salt + 1);
            double taper = Smooth(0.0, 0.07, t) * Smooth(0.0, 0.16, 1.0 - t);
            if (taper <= 0.0)
            {
                return 0.0;
            }

            double u = math.abs(cr - off) / math.max(1e-6, width);
            double wallNoise = Noise3(q.x * 220.0, q.y * 220.0, q.z * 220.0, salt + 2);
            u += 0.17 * wallNoise * Smooth(0.35, 0.9, u);
            double f = 1.0 - Smooth(0.74, 1.0, u);
            if (f <= 0.0)
            {
                return 0.0;
            }

            double mound = Smooth(0.12, 0.6, Fbm(q, 14.0, 3, salt + 3));
            double inner = 1.0 - Smooth(0.5, 0.78, u);
            double depth = depthKm * (1.0 - (0.62 * mound * inner * Smooth(0.0, 0.2, t) * Smooth(0.0, 0.25, 1.0 - t)));
            expo = math.max(Smooth(0.28, 0.78, u) * (1.0 - Smooth(0.95, 1.2, u)), 0.55 * mound * inner);
            expo *= taper;
            return depth * f * taper;
        }

        private static double Troughs(in MartianReliefParams m, int seed, double3 q, out double expo)
        {
            expo = 0.0;
            double total = 0.0;
            double e;
            double3 c0 = ProvinceCenter(seed, 0);

            // Main canyon system: starts on the flank of the rise and runs radially away from it.
            double az = TwoPi * Rnd(seed, 8000, 1);
            double3 hc = Heading(c0, az);
            double riseR = m.RiseRadiusDeg * DegToRad;
            double dStart = riseR * 0.78;
            double3 a = MoveAlong(c0, hc, dStart);
            double3 b = math.normalize((-math.sin(dStart) * c0) + (math.cos(dStart) * hc));
            double len = m.CanyonLengthDeg * DegToRad;
            double halfW = m.CanyonWidthKm / MarsRadiusKm;
            total += Trough(q, a, b, len, halfW, m.CanyonDepthKm, halfW * 0.8, 7.0, 1.0, SaltTrough + (seed * 7919), out e);
            expo = math.max(expo, e);

            // Outflow channels: sinuous, narrow, start near the far end of the canyon.
            int nch = math.min(m.OutflowChannels, 8);
            for (int i = 0; i < nch; i++)
            {
                double phi = len * (0.72 + (0.28 * Rnd(seed, 8100 + i, 1)));
                double3 p = (math.cos(phi) * a) + (math.sin(phi) * b);
                double3 hd = (-math.sin(phi) * a) + (math.cos(phi) * b);
                double dev = (Rnd(seed, 8100 + i, 2) - 0.5) * 1.3;
                double3 hp = math.normalize((math.cos(dev) * hd) + (math.sin(dev) * math.cross(p, hd)));
                double cl = 0.35 + (0.55 * Rnd(seed, 8100 + i, 3));
                double cw = (m.ChannelWidthKm / MarsRadiusKm) * (0.7 + (0.7 * Rnd(seed, 8100 + i, 4)));
                total += Trough(q, p, hp, cl, cw, m.ChannelDepthKm, 0.03, 8.0, 0.9, SaltTrough + 10 + (i * 5) + (seed * 7919), out e);
                expo = math.max(expo, e * 0.8);
            }

            // Radial grabens around the rise.
            int nf = math.min(m.Fossae, 12);
            for (int i = 0; i < nf; i++)
            {
                double fa = TwoPi * ((i + (0.6 * Rnd(seed, 8200 + i, 1))) / (nf > 0 ? nf : 1));
                double d0 = riseR * (0.25 + (0.7 * Rnd(seed, 8200 + i, 2)));
                double3 hf = Heading(c0, fa);
                double3 pf = MoveAlong(c0, hf, d0);
                double3 bf = math.normalize((-math.sin(d0) * c0) + (math.cos(d0) * hf));
                double fl = 0.12 + (0.38 * Rnd(seed, 8200 + i, 3));
                total += Trough(q, pf, bf, fl, 0.0028 + (0.0022 * Rnd(seed, 8200 + i, 4)), 0.55, 0.0, 1.0, 0.3, SaltTrough + 100 + (i * 3) + (seed * 7919), out e);
                expo = math.max(expo, e * 0.7);
            }

            return total;
        }

        // ------------------------------------------------------------------ polar caps

        /// <summary>Polar cap weight 0..1 (0 outside, 1 inside); the edge is jittered. Used by both height and colour.</summary>
        public static double PolarCapWeight(in MartianReliefParams m, int seed, double3 d)
        {
            if (!(m.PolarCapLatitudeDeg > 0.0) || !(m.PolarCapFadeDeg > 0.0))
            {
                return 0.0;
            }

            double latRad = math.min(m.PolarCapLatitudeDeg, 89.9) * DegToRad;
            double edgeSin = math.sin(latRad);
            double dSinPerDeg = math.cos(latRad) * DegToRad;
            double fadeSin = math.max(1e-9, m.PolarCapFadeDeg * dSinPerDeg);
            double ampSin = m.PolarCapEdgeJitterDeg * dSinPerDeg;
            double z = math.abs(d.z);
            if (z <= edgeSin - ampSin - (0.5 * fadeSin))
            {
                return 0.0;
            }

            if (ampSin > 0.0)
            {
                edgeSin += ampSin * Fbm(d, 5.0, 3, SaltStrata + 40 + (d.z < 0.0 ? 7 : 0) + (seed * 7919));
            }

            return Smooth01(((z - edgeSin) / fadeSin) + 0.5);
        }

        /// <summary>Layered deposit dome with spiral troughs; returns height in km (before layering).</summary>
        private static double PolarDeposit(in MartianReliefParams m, int seed, double3 d, double w, out double troughKm)
        {
            troughKm = 0.0;
            if (w <= 0.0)
            {
                return 0.0;
            }

            double capRad = (90.0 - m.PolarCapLatitudeDeg) * DegToRad;
            double colat = math.acos(math.clamp(math.abs(d.z), 0.0, 1.0));
            double t = math.min(1.0, colat / math.max(1e-4, capRad));
            double hemi = d.z >= 0.0 ? 1.0 : 0.78;
            double dome = m.PolarCapHeightKm * hemi * (0.42 + (0.58 * (1.0 - (t * t))));
            double az = math.atan2(d.y, d.x) * (d.z >= 0.0 ? 1.0 : -1.0);
            double arms = math.max(1, m.PolarSpiralArms);
            double psi = arms * (az + (2.2 * math.log(math.max(t, 1e-3))));
            double tr = math.pow(0.5 + (0.5 * math.cos(psi)), 6.0);
            tr *= Smooth(0.07, 0.2, t) * (1.0 - Smooth(0.88, 1.0, t));
            troughKm = m.PolarTroughDepthKm * tr * w;
            return dome * w;
        }

        // ------------------------------------------------------------------ public entry points

        private static double Evaluate(in MartianReliefParams m, int seed, double3 dir, double capOverride, out double strataW, out double strataTone)
        {
            strataW = 0.0;
            strataTone = 0.0;
            double k = 1000.0 / math.max(1.0, m.HeightUnitMeters);

            double3 q = dir;
            if (m.WarpStrength > 0.0)
            {
                double wx = Fbm(dir, m.WarpFrequency, 3, SaltWarp + 1 + (seed * 7919));
                double wy = Fbm(dir, m.WarpFrequency, 3, SaltWarp + 2 + (seed * 7919));
                double wz = Fbm(dir, m.WarpFrequency, 3, SaltWarp + 3 + (seed * 7919));
                q = math.normalize(dir + (m.WarpStrength * new double3(wx, wy, wz)));
            }

            // 1. Crust: dichotomy, broad swells, rolling terrain, massifs.
            double3 axis = DichotomyAxis(m, seed);
            double edge = math.dot(q, axis) + (m.DichotomyBoundaryWarp * Fbm(q, 1.7, 4, SaltDich + (seed * 7919)));
            double wid = math.max(1e-4, m.DichotomyWidth);
            double low = Smooth01((edge - (m.DichotomyThreshold - wid)) / (2.0 * wid));
            double h = m.HighlandBaseKm - (m.DichotomyDepthKm * low);
            h += m.SwellAmplitudeKm * Fbm(q, m.SwellFrequency, 3, SaltSwell + (seed * 7919));

            double ruggedMod = 0.45 + (0.9 * Smooth(-0.35, 0.45, Fbm(q, 2.6, 2, SaltRough + 77 + (seed * 7919))));
            double rough = Fbm(q, m.RoughFrequency, m.RoughOctaves, SaltRough + (seed * 7919));
            h += m.RoughAmplitudeKm * ruggedMod * Lerp(1.0, m.RoughLowlandFactor, low) * rough;

            double massifMask = Smooth(0.0, 0.5, Fbm(q, 3.3, 3, SaltMassif + 50 + (seed * 7919))) * (1.0 - low);
            if (massifMask > 0.0 && m.MassifAmplitudeKm > 0.0)
            {
                h += m.MassifAmplitudeKm * massifMask * (Ridged(q, m.MassifFrequency, 6, SaltMassif + (seed * 7919)) - 0.35);
            }

            if (m.WrinkleAmplitudeKm > 0.0)
            {
                double wr = Ridged(q, 16.0, 3, SaltWrinkle + (seed * 7919));
                h += m.WrinkleAmplitudeKm * low * (wr - 0.4);
            }

            // 3 (computed first: its lava cover fades the craters beneath).
            double cover;
            double volc = Volcanism(m, seed, q, out cover);

            // 2. Bombardment: basins, then the crater field.
            double basinExpo;
            double basinFloor;
            double basin = Basins(m, seed, q, axis, out basinExpo, out basinFloor);
            h += basin;

            double craterExpo = 0.0;
            if (m.CraterClasses > 0 && m.CraterDensity > 0.0 && m.CraterLargestDiameterKm > 0.0)
            {
                h += Craters(m, seed, q, axis, cover, basinFloor, out craterExpo);
            }

            h += volc;

            // 4. Canyons, channels, grabens.
            double troughExpo;
            double trough = Troughs(m, seed, q, out troughExpo);
            h -= trough;

            // 5. Strata: beds exposed in layered terrains and on every wall. Massive lava
            // (shield flanks) is not layered, but canyon/crater walls cut through it are.
            if (m.StrataMix > 0.0 || m.StrataWallBoost > 0.0)
            {
                double region = Smooth(m.StrataRegionThreshold - 0.18, m.StrataRegionThreshold + 0.18, Fbm(q, m.StrataRegionFrequency, 3, SaltStrata + (seed * 7919)));
                double expo = math.max(craterExpo, math.max(basinExpo, troughExpo));
                double wS = math.clamp((m.StrataMix * region * (1.0 - cover)) + (m.StrataWallBoost * expo), 0.0, 1.0);
                if (wS > 0.0)
                {
                    double major = math.max(0.02, m.StrataMajorKm);
                    double minor = math.max(0.01, m.StrataMinorKm);
                    double tilt = m.StrataTilt * major * Fbm(q, 3.5, 2, SaltTilt + (seed * 7919));
                    double terraced = Terrace2(h + tilt, major, minor) - tilt;
                    h = Lerp(h, terraced, wS);

                    // Tone of the bed this point belongs to (bed index is unchanged by terracing).
                    double hh = h + tilt;
                    double bedMajor = (U01(Hash3((int)math.floor(hh / major), 11, 5, 17 + seed)) * 2.0) - 1.0;
                    double bedMinor = (U01(Hash3((int)math.floor(hh / minor), 12, 5, 29 + seed)) * 2.0) - 1.0;
                    strataTone = (0.7 * bedMajor) + (0.3 * bedMinor);
                    strataW = wS;
                }
            }

            // 6. Polar layered deposits.
            double cap = capOverride >= 0.0 ? capOverride : PolarCapWeight(m, seed, dir);
            if (cap > 0.0)
            {
                double troughKm;
                double dome = PolarDeposit(m, seed, dir, cap, out troughKm);
                double layered = dome - troughKm;
                double layerT = math.max(0.01, m.PolarLayerKm);
                layered = Lerp(layered, Terrace(layered, layerT, 5), 0.8 * cap);
                h += layered;
            }

            // 7. Wind-sculpted micro relief.
            if (m.HummockAmplitudeKm > 0.0 && m.HummockFrequency > 0.0)
            {
                double hm = Fbm(q, m.HummockFrequency, m.HummockOctaves, SaltHummock + (seed * 7919));
                h += m.HummockAmplitudeKm * hm * Lerp(1.0, 0.6, low);
            }

            return h * k;
        }

        /// <summary>
        /// capWeight &gt;= 0 replaces the internal polar cap weight (the game passes the weight
        /// of the profile's polar ice, which also whitens the albedo, so the deposit and the
        /// white cap coincide exactly); a negative value = compute it internally.
        /// </summary>
        public static double SampleHeight(in MartianReliefParams m, int seed, double3 dir, double capWeight)
        {
            double w;
            double t;
            return Evaluate(m, seed, dir, capWeight, out w, out t);
        }

        /// <summary>
        /// Height of the surface in NORMALIZED units (1.0 = HeightUnitMeters); the caller
        /// multiplies it by AmplitudeMeters. Polar cap weight is computed internally.
        /// </summary>
        public static double SampleHeight(in MartianReliefParams m, int seed, double3 dir)
        {
            return SampleHeight(m, seed, dir, -1.0);
        }

        /// <summary>
        /// Height (normalized, like SampleHeight) plus the colour mask for the existing palette
        /// pipeline in ~[-1, 1]: baseMask (the low-frequency albedo provinces) is mixed with a
        /// tone per exposed bed ONLY where the strata are actually exposed (walls, layered
        /// terrains), so that the layers show as bands of different colour and the rest of the
        /// planet keeps its smooth albedo variation.
        /// </summary>
        public static double SampleHeightAndMask(in MartianReliefParams m, int seed, double3 dir, double capWeight, double baseMask, out double mask)
        {
            double w;
            double tone;
            double h = Evaluate(m, seed, dir, capWeight, out w, out tone);
            double s = math.clamp(m.StrataColorStrength, 0.0, 1.0) * math.sqrt(w);
            mask = math.clamp((baseMask * (1.0 - (0.5 * s))) + (tone * s), -1.0, 1.0);
            return h;
        }
    }
}
