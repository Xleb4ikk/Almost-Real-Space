using System;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Authoring data of the lunar-type relief generator (see LunarRelief). Lives inside
    /// TerrainProfile.Lunar. Disabled by default: with Enabled = false the profile behaves
    /// exactly as before. All lengths are metres.
    /// </summary>
    [Serializable]
    public sealed class LunarReliefSettings
    {
        [Header("Общее")]
        [Tooltip("Включить генератор лунного рельефа (заменяет fBm/континенты этого профиля).")]
        public bool Enabled;

        [Tooltip("Радиус тела (м). Должен совпадать с BodyAuthoring.Radius: от него зависят размеры кратеров.")]
        public double RadiusMeters = 1737400d;

        [Tooltip("Поправка среднего уровня (м). Кратеры в сумме слегка смещают среднюю высоту; creator-окно подбирает это число автоматически.")]
        public double MeanOffsetMeters = 300d;

        [Header("Глобальная фигура")]
        [Tooltip("Амплитуда крупных пологих вздутий (м), длина волны порядка радиуса/частота.")]
        public double GlobalReliefMeters = 1800d;

        [Tooltip("Частота крупных вздутий (циклов на радиус).")]
        public double GlobalFrequency = 1.4d;

        [Tooltip("Разница уровней видимого/обратного полушария (м): видимая сторона ниже, как у Луны (~2 км между центром фигуры и массы).")]
        public double NearFarOffsetMeters = 1000d;

        [Tooltip("Ось «видимого» полушария (в системе тела). Вектор нормализуется.")]
        public double NearX = 1d;
        public double NearY;
        public double NearZ;

        [Tooltip("Амплитуда фрактальной шероховатости нагорий (м) на масштабе сотен км.")]
        public double RoughnessMeters = 700d;

        [Tooltip("Базовая частота шероховатости (циклов на радиус).")]
        public double RoughnessFrequency = 6d;

        [Tooltip("Число октав шероховатости (каждая вдвое мельче).")]
        [UnityEngine.Range(1, 14)]
        public int RoughnessOctaves = 8;

        [Tooltip("Показатель Хёрста: чем больше, тем глаже мелкие масштабы (Луна ≈ 0.8).")]
        public double RoughnessHurst = 0.8d;

        [Header("Кратеры")]
        [Tooltip("Плотность кратеров на опорном масштабе 20 км (0..1 = вероятность на ячейку сетки). 0.3 ≈ насыщенные нагорья Луны.")]
        public double CraterDensity = 0.30d;

        [Tooltip("Кумулятивный наклон распределения по размерам N(>D) ~ D^-b. Луна ≈ 2.0–2.3; больше — относительно больше мелких кратеров.")]
        public double ProductionSlope = 2.1d;

        [Tooltip("Множитель плотности для кратеров/бассейнов крупнее ~150 км: у Луны их заметно меньше, чем даёт экстраполяция мелких.")]
        public double BasinDensityScale = 0.55d;

        [Tooltip("Максимальный диаметр обычных кратеров (м). 1.4e6 даёт бассейны вроде Имбриума.")]
        public double MaxCraterDiameterMeters = 1400000d;

        [Tooltip("Минимальный диаметр кратера (м). Каждое уменьшение вдвое = ещё один уровень = дороже. Меш на макс. LOD имеет шаг ~10 м, поэтому < 20 м видны только в физике.")]
        public double MinCraterDiameterMeters = 24d;

        [Tooltip("Диаметр перехода простой→сложный кратер (м). Луна ≈ 18 км; обратно пропорционален силе тяжести (Меркурий ≈ 11 км, Марс ≈ 7 км).")]
        public double ComplexTransitionMeters = 18000d;

        [Tooltip("Показатель свежести: больше — больше сильно разрушенных старых кратеров, меньше свежих резких.")]
        public double FreshnessExponent = 2.5d;

        [Tooltip("Доля «возраста» (0..1), старше которой кратер считается дозатопным. Затопленное лавой рельеф скрывает такие кратеры.")]
        public double MareAge = 0.6d;

        [Tooltip("Плотность редких свежих кратеров с лучевыми системами (0 = лучей нет).")]
        public double RayDensity = 0.12d;

        [Tooltip("Неравномерность кратеров: размах гармоник контура, высоты вала и скульптуры дна. " +
                 "1 = физически правдоподобно (свежие простые кратеры почти круглые, старые — рваные), " +
                 "0 = идеальные круглые чаши, 2 = карикатурно мятые.")]
        public double CraterIrregularity = 1d;

        [Tooltip("Разброс валов: 0 = у всех кратеров вал ровно по формуле, " +
                 "1 = у одних вал высокий и широкий, у других почти нет, у части вал разорван с одной стороны, " +
                 "2 = подчёркнуто (для настройки «на глаз»).")]
        public double CraterRimVariety = 1d;

        [Tooltip("Сила скоплений вторичных кратеров 0..1. Реальный молодой кратер выбрасывает сотни мелких " +
                 "кратеров-сателлитов кучей, а не посыпает ими весь шар равномерно. 0 = равномерная плотность.")]
        public double SecondaryClusters = 0.55d;

        [Tooltip("Частота поля скоплений, циклов на радиус. Меньше — крупные поля шириной в сотни км.")]
        public double SecondaryClusterFrequency = 7.5d;

        [Header("Гигантский бассейн (аналог Южный полюс–Эйткен)")]
        [Tooltip("Диаметр (м). 0 = нет.")]
        public double GiantBasinDiameterMeters = 2400000d;

        [Tooltip("Направление центра бассейна (тело-фиксированная система).")]
        public double GiantX = -0.8d;
        public double GiantY = 0.25d;
        public double GiantZ = -0.55d;

        [Tooltip("Множитель глубины гигантского бассейна (1 = по эмпирической формуле).")]
        public double GiantDepthScale = 0.8d;

        [Header("Моря (лавовые равнины)")]
        [Tooltip("Доля поверхности, где ВОЗМОЖНЫ моря (0 = морей нет). Заливается лишь то, что ниже уровня лавы, поэтому итоговая площадь морей меньше: 0.45 даёт ~17% как у Луны.")]
        public double MareCoverage = 0.45d;

        [Tooltip("Частота маски морей (циклов на радиус): ниже — крупные океаны вроде Океана Бурь.")]
        public double MareFrequency = 1.5d;

        [Tooltip("Сдвиг морей к видимой стороне (0..1.5). Луна: моря почти все на видимой стороне.")]
        public double MareNearsideBias = 0.9d;

        [Tooltip("Уровень лавы относительно среднего радиуса (м). Всё, что ниже в зоне морей, затапливается.")]
        public double MareLevelMeters = -500d;

        [Tooltip("Разброс уровня лавы между морями (м).")]
        public double MareLevelVariationMeters = 450d;

        [Tooltip("Размах слабых вздутий поверхности моря (м).")]
        public double MareUndulationMeters = 35d;

        [Tooltip("Сглаживание берега лавы (м).")]
        public double MareShoreSmoothMeters = 40d;

        [Tooltip("Высота морщинистых хребтов (м). 0 = нет.")]
        public double WrinkleRidgeMeters = 110d;

        [Tooltip("Частота хребтов (циклов на радиус): расстояние между ними ≈ 2πR/частота.")]
        public double WrinkleRidgeFrequency = 26d;

        [Tooltip("Глубина извилистых борозд (м). 0 = нет.")]
        public double RilleDepthMeters = 140d;

        [Tooltip("Частота борозд (циклов на радиус).")]
        public double RilleFrequency = 70d;

        [Tooltip("Доля зон, где есть борозды (0..0.5).")]
        public double RilleAmount = 0.25d;

        [Tooltip("Высота лавовых куполов (м). 0 = нет.")]
        public double DomeHeightMeters = 140d;

        [Header("Меркурианские уступы")]
        [Tooltip("Высота линейных уступов-надвигов (м). 0 = нет.")]
        public double ScarpHeightMeters;
        public double ScarpFrequency = 14d;
        public double ScarpAmount = 0.25d;

        [Header("Реголит (мелкая шероховатость)")]
        [Tooltip("Амплитуда (м) на масштабе ~1 км — база фрактала: 9 м и 6 октав дают наклон ~10° на всех масштабах от 30 м до 1 км. 2–3 м даёт стеклянную поверхность.")]
        public double RegolithMeters = 9d;
        [Tooltip("Базовая частота (циклов на радиус): длина волны ≈ R / частота. Заводской пресет ставит R / 1000.")]
        public double RegolithFrequency = 1700d;
        [UnityEngine.Range(1, 8)]
        public int RegolithOctaves = 6;

        [Header("Мегареголит (между валами кратеров)")]
        [Tooltip("Амплитуда (м) полосы 0.5–10 км. Заполняет разрыв между крупными вздутиями (сотни км) и реголитом (~1 км): " +
                 "без неё каждый кратер лежит на гладком листе. На морях гаснет (лава, а не щебень).")]
        public double MegaregolithMeters = 14d;

        [Tooltip("Базовая частота мегареголита (циклов на радиус): длина волны ≈ R / частота. Заводской пресет ставит R / 7000.")]
        public double MegaregolithFrequency = 59d;

        [UnityEngine.Range(1, 8)]
        public int MegaregolithOctaves = 4;

        [Header("Альбедо (канал цветовой детали)")]
        [Tooltip("Насколько темнее моря (0..1). Мера лерпа в палитровый цвет Soil.")]
        public double MareDarkness = 0.85d;

        [Tooltip("Вариации яркости нагорий (0..1).")]
        public double HighlandVariation = 0.18d;

        [Tooltip("Яркость свежих выбросов и лучей (0..1). Лерп в палитровый цвет Lush.")]
        public double FreshEjectaBrightness = 1d;

        public LunarReliefSettings Clone()
        {
            return (LunarReliefSettings)MemberwiseClone();
        }

        /// <summary>Convert to the blittable Burst representation.</summary>
        public LunarParams ToParams(int seed)
        {
            double R = Math.Max(1000d, RadiusMeters);
            double faceWidth = Math.PI * 0.5d * R;
            int first = (int)Math.Max(1d, Math.Round(faceWidth / Math.Max(1000d, MaxCraterDiameterMeters)));
            double s0 = faceWidth / first;
            int levels = (int)Math.Max(1d, Math.Floor(Math.Log(s0 / Math.Max(1d, MinCraterDiameterMeters), 2d)) + 1d);
            levels = Math.Min(levels, 24);

            double nl = Math.Sqrt((NearX * NearX) + (NearY * NearY) + (NearZ * NearZ));
            if (nl < 1e-9d) { nl = 1d; }

            // The mare mask is gradient fBm (3 octaves, std ~0.17) plus bias * cos(angle) (var bias^2/3).
            double sigma = Math.Sqrt((0.17d * 0.17d) + (MareNearsideBias * MareNearsideBias / 3d));
            double cov = Math.Max(0d, Math.Min(0.99d, MareCoverage));
            double thr = cov <= 0.0005d ? 10d : (sigma * Probit(1d - cov));

            var p = new LunarParams
            {
                Enabled = Enabled ? 1 : 0,
                Seed = seed,
                Radius = R,
                MeanOffset = MeanOffsetMeters,
                GlobalRelief = GlobalReliefMeters,
                GlobalFrequency = GlobalFrequency,
                NearFarOffset = NearFarOffsetMeters,
                NearX = NearX / nl,
                NearY = NearY / nl,
                NearZ = NearZ / nl,
                RoughAmp = RoughnessMeters,
                RoughBaseFreq = RoughnessFrequency,
                RoughGain = Math.Pow(2d, -Math.Max(0.1d, Math.Min(1.5d, RoughnessHurst))),
                RoughOctaves = Math.Max(1, Math.Min(14, RoughnessOctaves)),
                CraterDensity = Math.Max(0d, CraterDensity),
                ProductionSlope = ProductionSlope,
                BasinDensityScale = BasinDensityScale,
                ComplexTransition = Math.Max(100d, ComplexTransitionMeters),
                FreshnessExp = Math.Max(0.2d, FreshnessExponent),
                MareAge = MareAge,
                FirstCells = first,
                LevelCount = levels,
                RayDensity = Math.Max(0d, RayDensity),
                CraterIrregularity = Math.Max(0d, Math.Min(2d, CraterIrregularity)),
                RimVariety = Math.Max(0d, Math.Min(2d, CraterRimVariety)),
                SecondaryClusters = Math.Max(0d, Math.Min(1d, SecondaryClusters)),
                SecondaryFreq = Math.Max(0d, SecondaryClusterFrequency),
                GiantD = Math.Max(0d, GiantBasinDiameterMeters),
                GiantX = GiantX,
                GiantY = GiantY,
                GiantZ = GiantZ,
                GiantDepthScale = GiantDepthScale,
                MareThreshold = thr,
                MareSharp = 0.07d,
                MareFreq = MareFrequency,
                MareNearBias = MareNearsideBias,
                MareLevel = MareLevelMeters,
                MareLevelVar = MareLevelVariationMeters,
                MareLevelFreq = 3.5d,
                MareSmooth = Math.Max(0d, MareShoreSmoothMeters),
                MareUndul = MareUndulationMeters,
                MareDrop = 9000d,
                WrinkleH = WrinkleRidgeMeters,
                WrinkleFreq = WrinkleRidgeFrequency,
                RilleDepth = RilleDepthMeters,
                RilleFreq = RilleFrequency,
                RilleAmount = RilleAmount,
                DomeH = DomeHeightMeters,
                ScarpH = ScarpHeightMeters,
                ScarpFreq = ScarpFrequency,
                ScarpAmount = ScarpAmount,
                RegolithAmp = RegolithMeters,
                RegolithBaseFreq = RegolithFrequency,
                RegolithOctaves = Math.Max(1, Math.Min(8, RegolithOctaves)),
                MegaAmp = Math.Max(0d, MegaregolithMeters),
                MegaFreq = Math.Max(0d, MegaregolithFrequency),
                MegaOctaves = Math.Max(1, Math.Min(8, MegaregolithOctaves)),
                MareDark = MareDarkness,
                HighlandVar = HighlandVariation,
                FreshBright = FreshEjectaBrightness
            };
            p.ContentHash = Hash(p);
            return p;
        }

        /// <summary>Inverse normal CDF (Abramowitz–Stegun 26.2.23).</summary>
        private static double Probit(double pr)
        {
            pr = Math.Max(1e-6d, Math.Min(1d - 1e-6d, pr));
            double sign = 1d;
            if (pr < 0.5d) { sign = -1d; pr = 1d - pr; }
            double t = Math.Sqrt(-2d * Math.Log(1d - pr));
            double x = t - (((0.010328d * t) + 0.802853d) * t + 2.515517d)
                / ((((0.001308d * t) + 0.189269d) * t + 1.432788d) * t + 1d);
            return sign * x;
        }

        private static long Hash(LunarParams p)
        {
            unchecked
            {
                long h = 1469598103934665603L;
                foreach (System.Reflection.FieldInfo fi in typeof(LunarParams).GetFields())
                {
                    object v = fi.GetValue(p);
                    long bits;
                    if (v is double) { bits = BitConverter.DoubleToInt64Bits((double)v); }
                    else if (v is int) { bits = (int)v; }
                    else { continue; }
                    h = (h ^ bits) * 1099511628211L;
                }

                return h;
            }
        }
    }
}
