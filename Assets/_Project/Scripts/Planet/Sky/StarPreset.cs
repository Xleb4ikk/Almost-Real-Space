using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Input of the physically based star render. The only required value is the
    /// mass; luminosity, radius and effective temperature are derived from it by
    /// <see cref="StarPhysics"/>. The three overrides exist for artistic experiments
    /// and deliberately leave the mass–luminosity relation (a warning is logged).
    ///
    /// A different star is one different asset: no renderer code changes.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Stars/Star Preset", fileName = "StarPreset")]
    public sealed class StarPreset : ScriptableObject
    {
        [Header("Физика (единственный обязательный вход)")]
        [Tooltip("Масса главной последовательности в массах Солнца для звёздной модели (L, R, Teff, цвет). " +
                 "0.2 = красный карлик, 1 = Солнце, 10 = голубая звезда. Гравитация системы задаётся отдельно " +
                 "в BodyAuthoring.StandardGravitationalParameter.")]
        public double MassSolar = 0.2d;

        [Header("Необязательные переопределения (0 = вывести из массы)")]
        [Tooltip("Радиус в R☉. 0 = R(M). Задание переопределения уводит звезду с зависимости масса–светимость.")]
        public double RadiusSolarOverride = 0d;

        [Tooltip("Светимость в L☉. 0 = L(M). Задание переопределения уводит звезду с зависимости масса–светимость.")]
        public double LuminositySolarOverride = 0d;

        [Tooltip("Эффективная температура, K. 0 = Teff(L,R). Задание переопределения уводит звезду с зависимости масса–светимость.")]
        public double EffectiveTemperatureKOverride = 0d;

        [Header("Художественные множители (по умолчанию 1)")]
        [Tooltip("Активность 0…1: покрытие пятнами, факелы, яркость короны.")]
        public double Activity01 = 1d;

        [Tooltip("Множитель контраста грануляции (физическая база 0.07 уже внутри).")]
        public double GranulationContrastMultiplier = 1d;

        [Tooltip("Множитель интенсивности bloom (HDRP ограничивает intensity сверху 1).")]
        public double BloomGain = 1d;

        [Tooltip("Множитель амплитуды блика (Moffat).")]
        public double GlareGain = 1d;

        [Tooltip("Множитель яркости короны.")]
        public double CoronaGain = 1d;

        [Tooltip("Смещение экспозиции, EV (положительное — ярче).")]
        public double ExposureBias = 0d;

        [Tooltip("Адаптация глаза 0…1: 0 = чистый цвет чёрного тела, 1 = белый. 0.25 — как в задании.")]
        public double Adaptation01 = 0.25d;

        [Header("Рендер прокси-сферы и короны")]
        [Tooltip("Дистанция прокси-сферы, м. 0 = 0.25·far clip (безопасно для HDRP depth).")]
        public double RenderDistanceMeters = 0d;

        [Tooltip("Угловой размер Moffat-ядра блика θ0, градусы.")]
        public double GlareTheta0Degrees = 0.15d;

        [Tooltip("Радиус квада блика, градусы. За ним остаток яркости забирает bloom.")]
        public double GlareExtentDegrees = 8d;

        [Tooltip("Радиус короны в радиусах звезды.")]
        public double CoronaExtentRadii = 12d;

        [Tooltip("Период медленного дрейфа грануляции, с игрового времени.")]
        public double GranulationPeriodSeconds = 1800d;

        [Tooltip("Зерно грануляции/пятен: одинаковый seed = одинаковая поверхность.")]
        public int SurfaceSeed = 1;

        [Tooltip("Слабые 4-лучевые стриксы (дифракция). По умолчанию выключены.")]
        public bool DiffractionRays;

        [Tooltip("Применять выведенный радиус к телу звезды в StarSystem (для SOI/окклюзии). " +
                 "Масса/гравитация системы не меняются.")]
        public bool ApplyDerivedRadiusToStarBody = true;

        /// <summary>Convert the asset into the pure-C# definition consumed by StarPhysics.</summary>
        public StarDefinition ToDefinition()
        {
            return new StarDefinition(
                MassSolar,
                RadiusSolarOverride,
                LuminositySolarOverride,
                EffectiveTemperatureKOverride,
                Activity01,
                GranulationContrastMultiplier,
                Adaptation01);
        }

        /// <summary>Derive the complete immutable state (safe to call from the editor).</summary>
        public StarState Derive()
        {
            return StarPhysics.Derive(ToDefinition());
        }

        private void OnValidate()
        {
            if (MassSolar <= 0d)
            {
                MassSolar = 0.01d;
            }

            if (RadiusSolarOverride < 0d)
            {
                RadiusSolarOverride = 0d;
            }

            if (LuminositySolarOverride < 0d)
            {
                LuminositySolarOverride = 0d;
            }

            if (EffectiveTemperatureKOverride < 0d)
            {
                EffectiveTemperatureKOverride = 0d;
            }

            Activity01 = StarPhysics.Clamp(Activity01, 0d, 1d);
            Adaptation01 = StarPhysics.Clamp(Adaptation01, 0d, 1d);
            GranulationContrastMultiplier = System.Math.Max(0d, GranulationContrastMultiplier);

            if (RadiusSolarOverride > 0d || LuminositySolarOverride > 0d || EffectiveTemperatureKOverride > 0d)
            {
                Debug.LogWarning(
                    "[StarPreset] " + name + ": задано переопределение радиуса/светимости/температуры — " +
                    "звезда вне зависимости масса–светимость.", this);
            }
        }
    }
}
