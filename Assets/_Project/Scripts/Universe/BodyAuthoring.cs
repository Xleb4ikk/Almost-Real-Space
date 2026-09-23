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

        [Header("Профили-ассеты (пресеты)")]
        [Tooltip("Пресет рельефа (форма/цвет/палитра). null = гладкая сфера. Зерно шума — TerrainSeed (per-body).")]
        public TerrainProfileAsset TerrainPreset;

        [Tooltip("Пресет атмосферы (физика+визуал). null = атмосферы нет.")]
        public AtmosphereProfileAsset AtmospherePreset;

        [Tooltip("Пресет облаков (см. PlanetCloudsView). null = облаков нет.")]
        public CloudProfileAsset CloudsPreset;

        [Tooltip("Пресет декора местности (трава/камни/деревья). null = декора нет.")]
        public GroundDecorProfileAsset GroundDecorPreset;

        [Tooltip("Зерно шума рельефа: одинаковый seed = одинаковый рельеф.")]
        public int TerrainSeed;

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
                Atmosphere = ResolveAtmosphere(),
                Clouds = ResolveClouds(),
                Terrain = ResolveTerrain(),
                TerrainSeed = TerrainSeed,
                ParentIndex = parentIndex
            };
        }

        /// <summary>Runtime-профиль рельефа из ассета-пресета; null — гладкая сфера.</summary>
        private TerrainProfile ResolveTerrain()
        {
            return TerrainPreset != null && TerrainPreset.Profile != null
                ? TerrainPreset.Profile.Clone()
                : null;
        }

        /// <summary>Runtime-профиль атмосферы из ассета-пресета; null — атмосферы нет.</summary>
        private AtmosphereProfile ResolveAtmosphere()
        {
            return AtmospherePreset != null && AtmospherePreset.Profile != null
                ? AtmospherePreset.Profile.Clone()
                : null;
        }

        private CloudProfile ResolveClouds()
        {
            return CloudsPreset != null && CloudsPreset.Profile != null
                ? CloudsPreset.Profile.Clone()
                : null;
        }

        /// <summary>
        /// Перенести параметры пресета рельефа в живой HeightfieldTerrain
        /// (live-тюнинг в Play-режиме). Единственное место копирования — не
        /// дублируется по вызывающим.
        /// </summary>
        public void ApplyToTerrain(HeightfieldTerrain terrain)
        {
            if (terrain == null || TerrainPreset == null || TerrainPreset.Profile == null)
            {
                return;
            }

            terrain.ApplyProfile(TerrainPreset.Profile, TerrainSeed);
        }
    }
}

