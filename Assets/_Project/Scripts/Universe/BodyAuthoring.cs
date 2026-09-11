using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Компонент небесного тела. Вешается на GameObject; иерархия Transform'ов
    /// сцены = орбитальная иерархия системы (родитель в иерархии — орбитальный
    /// родитель). Корень дерева (звезда) — GameObject без родителя среди авторингов.
    /// Поля читаются в SystemBlueprint (чистые данные) — вся логика сборки
    /// системы живёт там и тестируется на стенде (T65), здесь только сериализация.
    /// </summary>
    public sealed class BodyAuthoring : MonoBehaviour
    {
        [Header("Физика (СИ)")]
        public double StandardGravitationalParameter = 1d;
        public double Radius = 1d;
        public double CrashToleranceMps = 5d;

        [Header("Трение поверхности (см. SurfaceMotion)")]
        public double SurfaceStaticFrictionMu = 0.7d;
        public double SurfaceKineticFrictionMu = 0.5d;

        [Header("Кеплеровы элементы (относительно родителя; для звезды не используются)")]
        public double SemiMajorAxis = 1e11;
        public double Eccentricity;
        public double InclinationDegrees;
        public double LongitudeOfAscendingNodeDegrees;
        public double ArgumentOfPeriapsisDegrees;
        public double MeanAnomalyAtEpochDegrees;
        public double EpochTimeSeconds;

        [Header("Вращение вокруг оси")]
        [Tooltip("Период вращения, с. Игнорируется при включённом приливном захвате.")]
        public double RotationPeriodSeconds;

        [Tooltip("Приливный захват 1:1: период = орбитальному (по приведённой массе родитель+поддерево), к родителю всегда одна сторона (долгота 0 в эпоху). Ручные период и нулевой меридиан игнорируются.")]
        public bool TidallyLocked;

        public double PrimeMeridianOffsetDegrees;
        public Vector3d NorthPoleDirection = new Vector3d(0d, 0d, 1d);

        [Header("Атмосфера (null = нет)")]
        public AtmosphereProfile Atmosphere;

        [Header("Процедурный рельеф (HeightfieldTerrain)")]
        [Tooltip("Включить рельеф (иначе гладкая сфера).")]
        public bool TerrainEnabled;

        [Tooltip("Зерно шума: одинаковый seed = одинаковый рельеф.")]
        public int TerrainSeed;

        [Tooltip("Амплитуда рельефа над/под средним радиусом (м).")]
        public double TerrainAmplitudeMeters = 1000d;

        [Tooltip("Базовая частота (циклов на единичный вектор; больше = мельче особенности).")]
        public double TerrainBaseFrequency = 3d;

        [Tooltip("Число октав (каждая ×2 частота, ×0.5 амплитуда).")]
        public int TerrainOctaves = 5;

        [Tooltip("Уровень моря над средним радиусом (м). Ниже −1e30 = моря нет.")]
        public double TerrainSeaLevelMeters = -1e30d;

        [Header("Продвинутый heightfield (фаза 1): нули = legacy-fBm")]
        [Tooltip("Лакунарность fBm (множитель частоты на октаву). 2 = legacy.")]
        public double TerrainLacunarity = 2d;

        [Tooltip("Затухание амплитуды на октаву. 0.5 = legacy.")]
        public double TerrainGain = 0.5d;

        [Tooltip("Частота континентальной маски (океан/суша). 0 = выключена.")]
        public double TerrainContinentFrequency = 0d;

        [Tooltip("Число октав маски континентов.")]
        public int TerrainContinentOctaves = 3;

        [Tooltip("Порог маски: выше — суша, ниже — океан.")]
        public double TerrainContinentThreshold = 0d;

        [Tooltip("Полуширина smoothstep-перехода маски.")]
        public double TerrainContinentSharpness = 0.25d;

        [Tooltip("Глубина океанических впадин в долях амплитуды.")]
        public double TerrainContinentDepth = 0.75d;

        [Tooltip("Доля ridged-шума 0..1 (горные хребты, только на континентах). 0 = выключен.")]
        public double TerrainRidgedMix = 0d;

        [Tooltip("Сила равнин 0..1: в зонах маски рельеф стягивается к низкому плато. 0 = выключено.")]
        public double TerrainPlainMix = 0d;

        [Tooltip("Частота низкочастотной маски равнин. 0 = выключена.")]
        public double TerrainPlainFrequency = 0d;

        [Tooltip("Число октав маски равнин.")]
        public int TerrainPlainOctaves = 2;

        [Tooltip("Порог маски равнин: выше — равнина.")]
        public double TerrainPlainThreshold = 0d;

        [Tooltip("Полуширина smoothstep-перехода маски равнин.")]
        public double TerrainPlainSharpness = 0.3d;

        [Tooltip("Нормализованная высота плато равнин (доля амплитуды).")]
        public double TerrainPlainElevation = 0.1d;

        [Tooltip("Мелкомасштабная деталь как доля амплитуды (0 = выключена).")]
        public double TerrainDetailMix = 0d;

        [Tooltip("Базовая частота детали (циклов на единичный вектор). 0 = выключена.")]
        public double TerrainDetailFrequency = 0d;

        [Tooltip("Число октав детали.")]
        public int TerrainDetailOctaves = 5;

        [Tooltip("Сила domain-warp в единицах направления (типично 0.05..0.3). 0 = выключен.")]
        public double TerrainWarpStrength = 0d;

        [Tooltip("Базовая частота warp-шума (независимый поток сида).")]
        public double TerrainWarpFrequency = 1d;

        [Tooltip("Число октав warp-шума.")]
        public int TerrainWarpOctaves = 2;

        [Tooltip("Сдвиг warp-потока (целый).")]
        public int TerrainWarpSeedOffset = 0;

        [Header("Цвет рельефа (фаза 2): нули = legacy-палитра")]
        [Tooltip("Порог rock-override в tan склона (круче — скала на любой высоте). 0 = выключен.")]
        public double TerrainColorRockSlopeTan = 0d;

        [Tooltip("Полуширина бленда в скалу (единицы tan).")]
        public double TerrainColorRockSlopeWidth = 0.1d;

        [Tooltip("Макс. склон для снега в tan (круче — скала). 0 = выключен (снег по высоте).")]
        public double TerrainColorSnowSlopeTan = 0d;

        [Tooltip("Частота шума цветовой маски (разбивка полос). 0 = выключена.")]
        public double TerrainColorNoiseFrequency = 0d;

        [Tooltip("Число октав шума маски.")]
        public int TerrainColorNoiseOctaves = 3;

        [Tooltip("Сила маски (сдвиг t перед bands). 0 = выключена.")]
        public double TerrainColorNoiseStrength = 0d;

        [Tooltip("Сдвиг потока маски (целый).")]
        public int TerrainColorNoiseSeedOffset = 0;

        [Tooltip("Частота мелкомасштабной цветовой детали (моттлинг земли). 0 = выключена.")]
        public double TerrainColorDetailFrequency = 0d;

        [Tooltip("Число октав цветовой детали.")]
        public int TerrainColorDetailOctaves = 3;

        [Tooltip("Сила моттлинга 0..1 (0 = выключена).")]
        public double TerrainColorDetailStrength = 0d;

        [Tooltip("Сдвиг потока цветовой детали (целый).")]
        public int TerrainColorDetailSeedOffset = 0;

        public BodyBlueprint ToBlueprint(int parentIndex)
        {
            return new BodyBlueprint
            {
                Name = gameObject.name,
                StandardGravitationalParameter = StandardGravitationalParameter,
                Radius = Radius,
                CrashToleranceMps = CrashToleranceMps,
                SurfaceStaticFrictionMu = SurfaceStaticFrictionMu,
                SurfaceKineticFrictionMu = SurfaceKineticFrictionMu,
                SemiMajorAxis = SemiMajorAxis,
                Eccentricity = Eccentricity,
                InclinationDegrees = InclinationDegrees,
                LongitudeOfAscendingNodeDegrees = LongitudeOfAscendingNodeDegrees,
                ArgumentOfPeriapsisDegrees = ArgumentOfPeriapsisDegrees,
                MeanAnomalyAtEpochDegrees = MeanAnomalyAtEpochDegrees,
                EpochTimeSeconds = EpochTimeSeconds,
                RotationPeriodSeconds = RotationPeriodSeconds,
                TidallyLocked = TidallyLocked,
                PrimeMeridianOffsetDegrees = PrimeMeridianOffsetDegrees,
                NorthPoleX = NorthPoleDirection.X,
                NorthPoleY = NorthPoleDirection.Y,
                NorthPoleZ = NorthPoleDirection.Z,
                Atmosphere = Atmosphere,
                TerrainEnabled = TerrainEnabled,
                TerrainSeed = TerrainSeed,
                TerrainAmplitudeMeters = TerrainAmplitudeMeters,
                TerrainBaseFrequency = TerrainBaseFrequency,
                TerrainOctaves = TerrainOctaves,
                TerrainSeaLevelMeters = TerrainSeaLevelMeters,
                TerrainLacunarity = TerrainLacunarity,
                TerrainGain = TerrainGain,
                TerrainContinentFrequency = TerrainContinentFrequency,
                TerrainContinentOctaves = TerrainContinentOctaves,
                TerrainContinentThreshold = TerrainContinentThreshold,
                TerrainContinentSharpness = TerrainContinentSharpness,
                TerrainContinentDepth = TerrainContinentDepth,
                TerrainRidgedMix = TerrainRidgedMix,
                TerrainPlainMix = TerrainPlainMix,
                TerrainPlainFrequency = TerrainPlainFrequency,
                TerrainPlainOctaves = TerrainPlainOctaves,
                TerrainPlainThreshold = TerrainPlainThreshold,
                TerrainPlainSharpness = TerrainPlainSharpness,
                TerrainPlainElevation = TerrainPlainElevation,
                TerrainDetailMix = TerrainDetailMix,
                TerrainDetailFrequency = TerrainDetailFrequency,
                TerrainDetailOctaves = TerrainDetailOctaves,
                TerrainWarpStrength = TerrainWarpStrength,
                TerrainWarpFrequency = TerrainWarpFrequency,
                TerrainWarpOctaves = TerrainWarpOctaves,
                TerrainWarpSeedOffset = TerrainWarpSeedOffset,
                TerrainColorRockSlopeTan = TerrainColorRockSlopeTan,
                TerrainColorRockSlopeWidth = TerrainColorRockSlopeWidth,
                TerrainColorSnowSlopeTan = TerrainColorSnowSlopeTan,
                TerrainColorNoiseFrequency = TerrainColorNoiseFrequency,
                TerrainColorNoiseOctaves = TerrainColorNoiseOctaves,
                TerrainColorNoiseStrength = TerrainColorNoiseStrength,
                TerrainColorNoiseSeedOffset = TerrainColorNoiseSeedOffset,
                TerrainColorDetailFrequency = TerrainColorDetailFrequency,
                TerrainColorDetailOctaves = TerrainColorDetailOctaves,
                TerrainColorDetailStrength = TerrainColorDetailStrength,
                TerrainColorDetailSeedOffset = TerrainColorDetailSeedOffset,
                ParentIndex = parentIndex
            };
        }

        /// <summary>
        /// Перенести авторские параметры рельефа в живой HeightfieldTerrain
        /// (live-тюнинг в Play-режиме). Единственное место копирования — не
        /// дублируется по вызывающим.
        /// </summary>
        public void ApplyToTerrain(HeightfieldTerrain terrain)
        {
            if (terrain == null)
            {
                return;
            }

            terrain.Seed = TerrainSeed;
            terrain.AmplitudeMeters = TerrainAmplitudeMeters;
            terrain.BaseFrequency = TerrainBaseFrequency;
            terrain.Octaves = TerrainOctaves;
            terrain.SeaLevelMeters = TerrainSeaLevelMeters;
            terrain.Lacunarity = TerrainLacunarity;
            terrain.Gain = TerrainGain;
            terrain.ContinentFrequency = TerrainContinentFrequency;
            terrain.ContinentOctaves = TerrainContinentOctaves;
            terrain.ContinentThreshold = TerrainContinentThreshold;
            terrain.ContinentSharpness = TerrainContinentSharpness;
            terrain.ContinentDepth = TerrainContinentDepth;
            terrain.RidgedMix = TerrainRidgedMix;
            terrain.PlainMix = TerrainPlainMix;
            terrain.PlainFrequency = TerrainPlainFrequency;
            terrain.PlainOctaves = TerrainPlainOctaves;
            terrain.PlainThreshold = TerrainPlainThreshold;
            terrain.PlainSharpness = TerrainPlainSharpness;
            terrain.PlainElevation = TerrainPlainElevation;
            terrain.DetailMix = TerrainDetailMix;
            terrain.DetailFrequency = TerrainDetailFrequency;
            terrain.DetailOctaves = TerrainDetailOctaves;
            terrain.WarpStrength = TerrainWarpStrength;
            terrain.WarpFrequency = TerrainWarpFrequency;
            terrain.WarpOctaves = TerrainWarpOctaves;
            terrain.WarpSeedOffset = TerrainWarpSeedOffset;
            terrain.ColorRockSlopeTan = TerrainColorRockSlopeTan;
            terrain.ColorRockSlopeWidth = TerrainColorRockSlopeWidth;
            terrain.ColorSnowSlopeTan = TerrainColorSnowSlopeTan;
            terrain.ColorNoiseFrequency = TerrainColorNoiseFrequency;
            terrain.ColorNoiseOctaves = TerrainColorNoiseOctaves;
            terrain.ColorNoiseStrength = TerrainColorNoiseStrength;
            terrain.ColorNoiseSeedOffset = TerrainColorNoiseSeedOffset;
            terrain.ColorDetailFrequency = TerrainColorDetailFrequency;
            terrain.ColorDetailOctaves = TerrainColorDetailOctaves;
            terrain.ColorDetailStrength = TerrainColorDetailStrength;
            terrain.ColorDetailSeedOffset = TerrainColorDetailSeedOffset;
        }

        /// <summary>
        /// Пресет «планета как Земля»: невысокий рельеф относительно радиуса,
        /// континенты + редкие хребты + равнины (низкочастотная маска) + лёгкий
        /// warp, slope-цвет (скалы/снег) и noise-маска. Амплитуда — доля радиуса
        /// (≈0.8%): при 1143 км это ~9 км, а не 50 км, как в дефолте сцены.
        /// </summary>
        [ContextMenu("Earth-like terrain preset")]
        public void ApplyEarthLikeTerrainPreset()
        {
            TerrainEnabled = true;
            TerrainAmplitudeMeters = System.Math.Max(200d, Radius * 0.008d);
            TerrainBaseFrequency = 20d;
            TerrainOctaves = 10;
            TerrainSeaLevelMeters = 0d;
            TerrainLacunarity = 2d;
            TerrainGain = 0.5d;
            TerrainContinentFrequency = 4d;
            TerrainContinentOctaves = 3;
            TerrainContinentThreshold = -0.1d;
            TerrainContinentSharpness = 0.3d;
            TerrainContinentDepth = 0.9d;
            TerrainRidgedMix = 0.7d;
            TerrainPlainMix = 0.85d;
            TerrainPlainFrequency = 1.8d;
            TerrainPlainOctaves = 2;
            TerrainPlainThreshold = 0.05d;
            TerrainPlainSharpness = 0.25d;
            TerrainPlainElevation = 0.1d;
            TerrainDetailMix = 0d;
            TerrainDetailFrequency = 0d;
            TerrainDetailOctaves = 5;
            TerrainWarpStrength = 0.1d;
            TerrainWarpFrequency = 2d;
            TerrainWarpOctaves = 2;
            TerrainColorRockSlopeTan = 0.6d;
            TerrainColorRockSlopeWidth = 0.15d;
            TerrainColorSnowSlopeTan = 0.5d;
            TerrainColorNoiseFrequency = 25d;
            TerrainColorNoiseOctaves = 4;
            TerrainColorNoiseStrength = 0.09d;
            TerrainColorDetailFrequency = 1500d;
            TerrainColorDetailOctaves = 3;
            TerrainColorDetailStrength = 0.35d;
        }
    }
}
