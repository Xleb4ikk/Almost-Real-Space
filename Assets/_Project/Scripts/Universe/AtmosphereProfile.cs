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
    /// Визуал (PlanetAtmosphereView + Galilego/PlanetAtmosphere) читает те же
    /// высоту/плотность и добавляет цвет/оптику — один профиль в инспекторе на
    /// оба слоя. Всё визуальное за тумблером VisualEnabled и настраиваемо.
    /// </summary>
    [System.Serializable]
    public sealed class AtmosphereProfile
    {
        [Header("Физика (высотная модель плотности)")]
        public double TopAltitudeMeters;
        public double SeaLevelPressurePascals;
        public double SeaLevelDensityKgPerCubicMeter;
        public double ScaleHeightMeters;

        [Header("Визуал (raymarch-атмосфера)")]
        [Tooltip("Рисовать атмосферу. Выключено — физика работает, визуала нет.")]
        public bool VisualEnabled;

        [Tooltip("Цвет рэлеевского рассеяния (голубой день).")]
        public Color RayleighColor = new Color(0.30f, 0.55f, 1f, 1f);

        [Tooltip("Цвет Ми-рассеяния (тёплая дымка/закат у горизонта).")]
        public Color MieColor = new Color(1f, 0.85f, 0.62f, 1f);

        [Tooltip("Общая яркость/оптическая плотность.")]
        public float Intensity = 1f;

        [Tooltip("Анизотропия Ми (−1..1): 0.76 — выраженный солнечный ореол.")]
        public float MieAnisotropy = 0.76f;

        [Tooltip("Множитель высотного профиля плотности (1 = как в физике).")]
        public float DensityFalloff = 1f;

        [Tooltip("Число шагов raymarch (больше — плавнее и дороже).")]
        public int StepCount = 32;

        [Tooltip("Многократное рассеяние: голубое небо и в зените (single-scatter даёт почти чёрный зенит).")]
        public float MultiScatter = 0.35f;

        [Tooltip("Считать ли планету преградой для луча (закаты/тень).")]
        public bool PlanetOcclusion = true;
    }
}
