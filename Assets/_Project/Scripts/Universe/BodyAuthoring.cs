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
                ParentIndex = parentIndex
            };
        }
    }
}
