using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Данные объёмных облаков планеты. Слой плотности между двумя радиусами
    /// (planetRadius + BottomAltitude .. planetRadius + TopAltitude), заполняемый
    /// 3D-шумом (Perlin-Worley форма + Worley-детализация), с coverage/тип из
    /// низкочастотного 3D weather-шума.
    ///
    /// ВАЖНО про параметризацию: все домены шума (shape/detail/weather/curl)
    /// сэмплируются в МИРОВЫХ декартовых координатах относительно центра
    /// планеты (см. PlanetClouds.shader), а НЕ в lat-long/UV. У сферы нет
    /// естественной беcшовной 2D-развёртки — любой lat-long или cube-UV даёт
    /// анизотропию у полюсов/швов и на большом радиусе (~1000+ км) превращается
    /// в вытянутые полосы вдоль параллелей. Декартова 3D-тайловая текстура на
    /// такой развёртке не нуждается: гладкая и бесшовная в любой точке сферы.
    ///
    /// Как и AtmosphereProfile — один профиль на физику+визуал, разница в том,
    /// что у облаков собственной физики (плотность воздуха и т.п.) не завязано,
    /// профиль целиком визуальный.
    /// </summary>
    [System.Serializable]
    public sealed class CloudProfile
    {
        [Header("Геометрия (высота слоя над уровнем моря)")]
        [Tooltip("Нижняя граница облачного слоя, м над уровнем моря.")]
        public double BottomAltitudeMeters = 7000d;

        [Tooltip("Верхняя граница облачного слоя, м над уровнем моря.")]
        public double TopAltitudeMeters = 8500d;

        [Header("Визуал")]
        [Tooltip("Рисовать облака. Выключено — компонент неактивен.")]
        public bool VisualEnabled;

         [Tooltip("Debug-режим вместо финального кадра.")]
         public CloudDebugMode DebugMode = CloudDebugMode.Final;

         public CloudNoiseStyle NoiseStyle = CloudNoiseStyle.EarthClusters;

         [Header("Форма/покрытие")]
        [Tooltip("Целевая доля неба под облаками (0 = чисто, 1 = сплошная облачность). Множитель поверх weather-шума.")]
        [Range(0f, 1f)]
        public float Coverage = 0.45f;

        [Header("Масштаб и распределение")]
        [Min(50f)]
        public float SmallCloudCellMeters = 3500f;

        [Min(100f)]
        public float MediumCloudCellMeters = 18000f;

        [Min(500f)]
        public float LargeCloudCellMeters = 100000f;

        [Range(0f, 1f)]
        public float SizeVariation = 0.8f;

        [Min(50000f)]
        public float ClearZoneCellMeters = 250000f;

        [Range(0f, 0.1f)]
        public float ClearZoneFraction = 0.01f;

        [Range(0.01f, 0.5f)]
        public float ClearZoneSoftness = 0.08f;

        [Min(0f)]
        public float ShapeWarpMeters = 1800f;

        [Tooltip("Размер одного облака (диаметр типичной кучевой 'шапки'), метры. Задаёт частоту shape-текстуры: 1/ShapeCellMeters.")]
        [Min(50f)]
        public float ShapeCellMeters = 1400f;

        [Tooltip("Размер деталей эрозии краёв ('дым'), метры. Обычно в 8–16 раз мельче ShapeCellMeters.")]
        [Min(5f)]
        public float DetailCellMeters = 140f;

        [Tooltip("Размер погодных фронтов (крупные зоны облачности/просветов), метры. Обычно в 10–30 раз крупнее ShapeCellMeters.")]
        [Min(500f)]
        public float WeatherCellMeters = 200000f;

        [Tooltip("Сила эрозии краёв детальным шумом (0 = гладкие блобы, 1 = сильно рваные/дымные края).")]
        [Range(0f, 1f)]
        public float DetailErosion = 0.55f;

        [Tooltip("Смягчение низа слоя (рваный/дымный, как в ТЗ 'дым, сквозь который летит игрок'). 0..~0.3 доли высоты слоя.")]
        [Range(0.01f, 0.4f)]
        public float BottomFeather = 0.15f;

        [Tooltip("Смягчение верха слоя (истончение к вершине). 0..~0.6 доли высоты слоя от вершины вниз.")]
        [Range(0.05f, 0.9f)]
        public float TopFeather = 0.45f;

        [Header("Ветер (сдвиг domain шума во времени, м/с в мировых координатах)")]
        public Vector3 WindVelocityMps = new Vector3(6f, 0f, 2f);

        [Header("Оптика")]
        [Tooltip("Сила облачной тени на поверхности и декоре.")]
        [Range(0f, 1f)]
        public float CloudShadowStrength = 0.65f;

        [Tooltip("Базовый коэффициент экстинкции, 1/м (плотный кучевой ~0.05–0.15). Больше — плотнее и темнее в глубине.")]
        [Min(0f)]
        public float Extinction = 0.08f;

        [Tooltip("Альбедо однократного рассеяния (доля экстинкции, уходящая в рассеяние, а не в поглощение). Облака почти не поглощают: держать близко к 1.")]
        [Range(0f, 1f)]
        public float ScatterAlbedo = 0.98f;

        [Tooltip("Анизотропия Henyey-Greenstein (0..0.95): выраженный ореол вокруг солнца при высоких значениях.")]
        [Range(0f, 0.95f)]
        public float PhaseAnisotropy = 0.65f;

        [Tooltip("Сила эффекта пудры (тёмные края, обращённые к солнцу, в глубине облака).")]
        [Range(0f, 2f)]
        public float PowderStrength = 0.8f;

        [Tooltip("Мягкое многократное рассеяние, заполняющее тени внутри облака.")]
        [Range(0f, 1f)]
        public float MultipleScattering = 0.65f;

        [Tooltip("Общий множитель яркости.")]
        [Min(0f)]
        public float Intensity = 1f;

        [Tooltip("Тинт ambient-подсветки снизу/по бокам (небо/земля), множитель поверх SkyAmbient.")]
        public Color AmbientTint = new Color(0.7f, 0.78f, 0.92f, 1f);

        [Header("Raymarch (перф)")]
        [Tooltip("Число основных шагов марша сквозь оболочку.")]
        [Range(8, 128)]
        public int StepCount = 64;

        [Tooltip("Число шагов вторичного марша к солнцу (самозатенение).")]
        [Range(2, 8)]
        public int LightSteps = 5;

        [Tooltip("Порог прозрачности для раннего выхода из марша (0.01 = останов при 99% непрозрачности).")]
        [Range(0.001f, 0.1f)]
        public float EarlyExitTransmittance = 0.01f;

        [Header("Шум (разрешение текстур; больше — качественнее и дольше печётся)")]
        [Tooltip("Разрешение 3D shape-текстуры (Perlin-Worley база + Worley-эрозия в GBA). 64 — хороший баланс.")]
        public int ShapeTextureResolution = 64;

        [Tooltip("Разрешение 3D detail-текстуры (мелкая Worley-эрозия краёв).")]
        public int DetailTextureResolution = 32;

        [Tooltip("Разрешение 3D weather-текстуры (крупные фронты покрытия).")]
        public int WeatherTextureResolution = 24;

        [Tooltip("Зерно генератора шума: одинаковый seed = одинаковые облака (детерминизм между запусками).")]
        public int NoiseSeed = 1337;

        /// <summary>Копия профиля: runtime не должен мутировать данные ассета.</summary>
        public CloudProfile Clone()
        {
            return (CloudProfile)MemberwiseClone();
        }
    }

    public enum CloudDebugMode
    {
        Final = 0,
        Density = 1,
        Coverage = 2,
        Transmittance = 3,
        StepCount = 4,
        Lod = 5,
        ClearMask = 6,
        CloudSize = 7,
        WeatherLod = 8,
    }

    public enum CloudNoiseStyle
    {
        LegacyBands = 0,
        EarthClusters = 1,
    }
}
