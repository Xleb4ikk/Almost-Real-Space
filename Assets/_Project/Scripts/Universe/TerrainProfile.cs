using System;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Палитра поверхности (11 цветов) в привычном sRGB — как настраивается в
    /// инспекторе. Переводом в Linear занимается TerrainPalette.ToLinear:
    /// единственный путь sRGB→Linear, поэтому цвета из ассета и старые
    /// константы палитры не расходятся (доказано T92).
    /// </summary>
    [Serializable]
    public sealed class TerrainPaletteData
    {
        public Color Sand = new Color(0.80f, 0.73f, 0.55f, 1f);
        public Color Desert = new Color(0.74f, 0.60f, 0.36f, 1f);
        public Color DryGrass = new Color(0.50f, 0.52f, 0.27f, 1f);
        public Color Grass = new Color(0.28f, 0.46f, 0.17f, 1f);
        public Color Forest = new Color(0.12f, 0.28f, 0.10f, 1f);
        public Color Tundra = new Color(0.46f, 0.44f, 0.37f, 1f);
        public Color Rock = new Color(0.45f, 0.41f, 0.36f, 1f);
        public Color Snow = new Color(0.96f, 0.97f, 0.99f, 1f);
        public Color Sea = new Color(0.05f, 0.20f, 0.42f, 1f);
        public Color Soil = new Color(0.50f, 0.40f, 0.26f, 1f);
        public Color Lush = new Color(0.13f, 0.40f, 0.12f, 1f);

        public Color SandLinear => TerrainPalette.ToLinear(Sand);
        public Color DesertLinear => TerrainPalette.ToLinear(Desert);
        public Color DryGrassLinear => TerrainPalette.ToLinear(DryGrass);
        public Color GrassLinear => TerrainPalette.ToLinear(Grass);
        public Color ForestLinear => TerrainPalette.ToLinear(Forest);
        public Color TundraLinear => TerrainPalette.ToLinear(Tundra);
        public Color RockLinear => TerrainPalette.ToLinear(Rock);
        public Color SnowLinear => TerrainPalette.ToLinear(Snow);
        public Color SeaLinear => TerrainPalette.ToLinear(Sea);
        public Color SoilLinear => TerrainPalette.ToLinear(Soil);
        public Color LushLinear => TerrainPalette.ToLinear(Lush);

        public TerrainPaletteData Clone()
        {
            // Color — структура, MemberwiseClone копирует значения полностью.
            return (TerrainPaletteData)MemberwiseClone();
        }

        /// <summary>Совпадают ли палитры по значениям (для live-тюнинга рендера).</summary>
        public bool Matches(TerrainPaletteData other)
        {
            return other != null
                && Sand == other.Sand
                && Desert == other.Desert
                && DryGrass == other.DryGrass
                && Grass == other.Grass
                && Forest == other.Forest
                && Tundra == other.Tundra
                && Rock == other.Rock
                && Snow == other.Snow
                && Sea == other.Sea
                && Soil == other.Soil
                && Lush == other.Lush;
        }
    }

    /// <summary>
    /// Профиль процедурного рельефа: форма (fBm/континенты/хребты/равнины/
    /// деталь/warp), цветовые пороги и палитра. Чистые данные без
    /// UnityEngine-поведения — живут в ассете (TerrainProfileAsset), копируются
    /// в HeightfieldTerrain при сборке системы (T91) и сериализуются в тестах.
    /// Зерно шума остаётся на теле (BodyAuthoring.TerrainSeed): один профиль-
    /// пресет можно вешать на разные тела с разными сидами.
    /// Нулевые/дефолтные значения параметров дают бит-в-бит legacy-fBm —
    /// контракт HeightfieldTerrain не меняется.
    /// </summary>
    [Serializable]
    public sealed class TerrainProfile
    {
        [Header("Форма")]
        [Tooltip("Амплитуда рельефа над/под средним радиусом (м).")]
        public double AmplitudeMeters = 1000d;

        [Tooltip("Базовая частота (циклов на единичный вектор).")]
        public double BaseFrequency = 3d;

        [Tooltip("Число октав.")]
        public int Octaves = 5;

        [Tooltip("Уровень моря над средним радиусом (м). Ниже −1e30 = моря нет.")]
        public double SeaLevelMeters = -1e30d;

        [Tooltip("Лакунарность fBm: 2 = legacy.")]
        public double Lacunarity = 2d;

        [Tooltip("Затухание амплитуды на октаву: 0.5 = legacy.")]
        public double Gain = 0.5d;

        [Tooltip("Доля ridged-шума 0..1 (горные хребты, только на континентах). 0 = выключен.")]
        public double RidgedMix = 0d;

        [Header("Континенты (океан/суша)")]
        [Tooltip("Частота континентальной маски. 0 = выключена.")]
        public double ContinentFrequency = 0d;

        [Tooltip("Число октав маски континентов.")]
        public int ContinentOctaves = 3;

        [Tooltip("Порог маски: выше — суша, ниже — океан.")]
        public double ContinentThreshold = 0d;

        [Tooltip("Полуширина smoothstep-перехода маски.")]
        public double ContinentSharpness = 0.25d;

        [Tooltip("Глубина океанических впадин в долях амплитуды.")]
        public double ContinentDepth = 0.75d;

        [Header("Равнины")]
        [Tooltip("Сила равнин 0..1: в зонах маски рельеф стягивается к низкому плато. 0 = выключено.")]
        public double PlainMix = 0d;

        [Tooltip("Частота низкочастотной маски равнин. 0 = выключена.")]
        public double PlainFrequency = 0d;

        [Tooltip("Число октав маски равнин.")]
        public int PlainOctaves = 2;

        [Tooltip("Порог маски равнин: выше — равнина.")]
        public double PlainThreshold = 0d;

        [Tooltip("Полуширина smoothstep-перехода маски равнин.")]
        public double PlainSharpness = 0.3d;

        [Tooltip("Нормализованная высота плато равнин (доля амплитуды).")]
        public double PlainElevation = 0.1d;

        [Header("Деталь и domain warp")]
        [Tooltip("Мелкомасштабная деталь как доля амплитуды (0 = выключена).")]
        public double DetailMix = 0d;

        [Tooltip("Базовая частота детали (циклов на единичный вектор). 0 = выключена.")]
        public double DetailFrequency = 0d;

        [Tooltip("Число октав детали.")]
        public int DetailOctaves = 5;

        [Tooltip("Сила domain-warp в единицах направления (типично 0.05..0.3). 0 = выключен.")]
        public double WarpStrength = 0d;

        [Tooltip("Базовая частота warp-шума (независимый поток сида).")]
        public double WarpFrequency = 1d;

        [Tooltip("Число октав warp-шума.")]
        public int WarpOctaves = 2;

        [Tooltip("Сдвиг warp-потока (целый).")]
        public int WarpSeedOffset = 0;

        [Header("Цвет рельефа (скалы, снег, биомы)")]
        [Tooltip("Порог rock-override в tan склона (круче — скала на любой высоте). 0 = выключен.")]
        public double ColorRockSlopeTan = 0d;

        [Tooltip("Полуширина бленда в скалу (единицы tan).")]
        public double ColorRockSlopeWidth = 0.1d;

        [Tooltip("Мин. нормированная высота для скалы: камни только на крупных горах. 0 = без порога.")]
        public double ColorRockHeightMin = 0d;

        [Tooltip("Макс. склон для снега в tan (круче — скала). 0 = выключен.")]
        public double ColorSnowSlopeTan = 0d;

        [Tooltip("Частота шума цветовой маски (разбивка полос). 0 = выключена.")]
        public double ColorNoiseFrequency = 0d;

        [Tooltip("Число октав шума маски.")]
        public int ColorNoiseOctaves = 3;

        [Tooltip("Сила маски (сдвиг t перед bands). 0 = выключена.")]
        public double ColorNoiseStrength = 0d;

        [Tooltip("Сдвиг потока маски (целый).")]
        public int ColorNoiseSeedOffset = 0;

        [Tooltip("Частота мелкомасштабной цветовой детали (моттлинг земли). 0 = выключена.")]
        public double ColorDetailFrequency = 0d;

        [Tooltip("Число октав цветовой детали.")]
        public int ColorDetailOctaves = 3;

        [Tooltip("Сила моттлинга 0..1 (0 = выключена).")]
        public double ColorDetailStrength = 0d;

        [Tooltip("Сдвиг потока цветовой детали (целый).")]
        public int ColorDetailSeedOffset = 0;

        [Header("Палитра")]
        public TerrainPaletteData Palette = new TerrainPaletteData();

        [Header("Текстуры рельефа (null = только процедурная палитра)")]
        [Tooltip("Альбедо низкогорья (обычно до ~60 м над морем).")]
        public Texture2D TextureLow;

        [Tooltip("Альбедо среднего пояса.")]
        public Texture2D TextureMid;

        [Tooltip("Альбедо высокогорья.")]
        public Texture2D TextureHigh;

        [Tooltip("Альбедо крутых склонов (скалы).")]
        public Texture2D TextureSteep;

        [Tooltip("Карта затенения (мягкий AO рельефа).")]
        public Texture2D TextureOcclusion;

        [Tooltip("Повторов текстуры на метр (0.04 = тайл 25 м).")]
        public double TextureScale = 0.04d;

        public double LowMidBlendStart = 30d;
        public double LowMidBlendEnd = 60d;
        public double MidHighBlendStart = 2500d;
        public double MidHighBlendEnd = 3500d;
        public double SteepBlendStart = 0.7d;
        public double SteepBlendEnd = 1.4d;

        public TerrainProfile Clone()
        {
            TerrainProfile copy = (TerrainProfile)MemberwiseClone();
            copy.Palette = Palette != null ? Palette.Clone() : new TerrainPaletteData();
            return copy;
        }

        /// <summary>
        /// Пресет «планета как Земля»: невысокий рельеф (≈0.8% радиуса),
        /// континенты + редкие хребты + равнины + лёгкий warp, slope-цвет и
        /// noise-маска. Раньше жил в BodyAuthoring.ApplyEarthLikeTerrainPreset —
        /// теперь это ассет EarthLike, на который ссылается тело.
        /// </summary>
        public static TerrainProfile CreateEarthLike(double radius)
        {
            return new TerrainProfile
            {
                AmplitudeMeters = System.Math.Max(200d, radius * 0.008d),
                BaseFrequency = 20d,
                Octaves = 10,
                SeaLevelMeters = 0d,
                Lacunarity = 2d,
                Gain = 0.5d,
                ContinentFrequency = 4d,
                ContinentOctaves = 3,
                ContinentThreshold = -0.1d,
                ContinentSharpness = 0.3d,
                ContinentDepth = 0.9d,
                RidgedMix = 0.7d,
                PlainMix = 0.85d,
                PlainFrequency = 1.8d,
                PlainOctaves = 2,
                PlainThreshold = 0.05d,
                PlainSharpness = 0.25d,
                PlainElevation = 0.1d,
                DetailMix = 0d,
                DetailFrequency = 0d,
                DetailOctaves = 5,
                WarpStrength = 0.1d,
                WarpFrequency = 2d,
                WarpOctaves = 2,
                ColorRockSlopeTan = 0.6d,
                ColorRockSlopeWidth = 0.15d,
                ColorRockHeightMin = 0.4d,
                ColorSnowSlopeTan = 0.5d,
                ColorNoiseFrequency = 25d,
                ColorNoiseOctaves = 4,
                ColorNoiseStrength = 0.09d,
                ColorDetailFrequency = 400d,
                ColorDetailOctaves = 3,
                ColorDetailStrength = 0.15d
            };
        }
    }
}
