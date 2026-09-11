using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Данные атмосферы планеты. Физика (DragSource/детекторы/Isp) читает только
    /// высотную модель:
    ///
    ///     ρ(h) = SeaLevelDensityKgPerCubicMeter * exp(-h / ScaleHeightMeters)
    ///
    /// обнуляется при h >= TopAltitudeMeters.
    ///
    /// Коэффициенты рассеяния НЕ задаются здесь художественно: они выводятся из
    /// SeaLevelDensityKgPerCubicMeter и земных констант в AtmosphereOptics —
    /// едином источнике для CPU (SkyPhysics/SkyEnvironment) и GPU (шейдер).
    /// RayleighColor/MieColor ниже — только множители поверх физики (тинты),
    /// 1 = без изменений.
    ///
    /// Визуал (PlanetAtmosphereView + Galilego/PlanetAtmosphere) читает те же
    /// высоту/плотность и добавляет оптику — один профиль в инспекторе на оба
    /// слоя. Всё визуальное за тумблером VisualEnabled и настраиваемо.
    /// </summary>
    [System.Serializable]
    public sealed class AtmosphereProfile
    {
        [Header("Физика (высотная модель плотности)")]
        public double TopAltitudeMeters;
        public double SeaLevelPressurePascals;
        public double SeaLevelDensityKgPerCubicMeter;
        public double ScaleHeightMeters;

        [Tooltip("Считать ли озон (тонкий слой ~25 км): синий зенит и чистый закат. Отключено — проще, но небо бледнее.")]
        public bool OzoneEnabled = true;

        [Header("Визуал (raymarch-атмосфера)")]
        [Tooltip("Рисовать атмосферу. Выключено — физика работает, визуала нет.")]
        public bool VisualEnabled;

        [Header("Визуал: художественные тинты (множители поверх физики)")]
        [Tooltip("МНОЖИТЕЛЬ поверх физического рассеяния Рэлея (не цвет неба!). 1=без изменений. Синева исходит из физики, тут только правка оттенка.")]
        public Color RayleighColor = new Color(1f, 1f, 1f, 1f);

        [Tooltip("МНОЖИТЕЛЬ поверх физического рассеяния Ми (дымка/гало). 1=без изменений; цвет аэрозоля по умолчанию нейтральный.")]
        public Color MieColor = new Color(1f, 1f, 1f, 1f);

        [Tooltip("Общий множитель яркости неба. Мал — тускло, велик — пересвет; 1 = физическая калибровка.")]
        public float Intensity = 1f;

        [Tooltip("Анизотропия Ми (−1..1): 0.76 — выраженный солнечный ореол.")]
        public float MieAnisotropy = 0.8f;

        [Tooltip("Turbidity: множитель плотности аэрозоля (мутность горизонта). 1 = физическая. Яркость — это Intensity; форма профиля — ScaleHeight. Крутить «слишком туманно» нужно этим.")]
        public float AerosolScale = 1f;

        [Tooltip("Число шагов raymarch (больше — плавнее и дороже).")]
        public int StepCount = 48;

        [Tooltip("Считать ли планету преградой для луча, когда глубины сцены нет (пиксель неба): закаты/тень. При наличии глубины она приоритетнее.")]
        public bool PlanetOcclusion = true;

        [Tooltip("Цвет фолбэк-земли там, где рельеф не дорисован за горизонтом: сфера закрашивается затенённой землёй с дымкой вместо тёмной дыры.")]
        public Color GroundColor = new Color(0.16f, 0.18f, 0.14f, 1f);

        [Tooltip("Ширина плавного стыка небо/земля у горизонта (в косинусе зенита луча). Больше — мягче и шире; 0.01 ≈ 0.6°.")]
        public float HorizonFade = 0.01f;

        /// <summary>Единый набор коэффициентов для CPU и GPU (см. AtmosphereOptics).</summary>
        public AtmosphereOptics.Coefficients ToOptics()
        {
            return AtmosphereOptics.FromProfile(
                SeaLevelDensityKgPerCubicMeter,
                ScaleHeightMeters,
                ScaleHeightMeters > 0d ? System.Math.Max(1d, ScaleHeightMeters * 0.15d) : 0d,
                MieAnisotropy,
                OzoneEnabled,
                AerosolScale);
        }
    }
}
