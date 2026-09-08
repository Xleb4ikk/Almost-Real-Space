namespace Galilego.Universe
{
    /// <summary>
    /// Данные атмосферы планеты для экспоненциальной модели плотности
    /// (как в KSP и в реальной барометрической формуле):
    ///
    ///     ρ(h) = SeaLevelDensityKgPerCubicMeter * exp(-h / ScaleHeightMeters)
    ///
    /// обнуляется при h >= TopAltitudeMeters. Потребляется DragSource
    /// (сопротивление), детекторами входа/выхода (TopAltitudeMeters как граница
    /// режима) и высотной интерполяцией Isp (ScaleHeightMeters). Heating/lift и
    /// ветер отсутствуют осознанно (см. V13) — это данные без этих моделей.
    /// Поле Atmosphere на OrbitingBody может быть null (тело без атмосферы).
    /// </summary>
    [System.Serializable]
    public sealed class AtmosphereProfile
    {
        public double TopAltitudeMeters;
        public double SeaLevelPressurePascals;
        public double SeaLevelDensityKgPerCubicMeter;
        public double ScaleHeightMeters;
    }
}
