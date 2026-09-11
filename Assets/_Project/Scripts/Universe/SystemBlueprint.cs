using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Описание одного тела системы: чистые данные без UnityEngine-типов
    /// (кроме Vector3d), сериализуемые и в Unity-инспекторе, и в тестах.
    /// Иерархия задается ParentIndex (-1 = корень-звезда, ровно один).
    /// </summary>
    [Serializable]
    public sealed class BodyBlueprint
    {
        public string Name = "Body";
        public double StandardGravitationalParameter = 1d;
        public double Radius = 1d;
        public double CrashToleranceMps = 5d;

        /// <summary>Трение поверхности (Кулон, см. SurfaceMotion): стик пока |a_t| ≤ μ_s·|a_n|.</summary>
        public double SurfaceStaticFrictionMu = 0.7d;

        public double SurfaceKineticFrictionMu = 0.5d;

        public double SemiMajorAxis = 1d;
        public double Eccentricity;
        public double InclinationDegrees;
        public double LongitudeOfAscendingNodeDegrees;
        public double ArgumentOfPeriapsisDegrees;
        public double MeanAnomalyAtEpochDegrees;
        public double EpochTimeSeconds;

        public double RotationPeriodSeconds;
        public double PrimeMeridianOffsetDegrees;
        public double NorthPoleX;
        public double NorthPoleY = 1d;
        public double NorthPoleZ;

        /// <summary>Приливный захват 1:1 (см. OrbitingBody.ApplyTidalLock): ручной период/оффсет игнорируются.</summary>
        public bool TidallyLocked;

        public AtmosphereProfile Atmosphere;

        /// <summary>Рельеф: null — гладкая сфера. Параметры — см. HeightfieldTerrain.</summary>
        public int TerrainSeed;
        public bool TerrainEnabled;
        public double TerrainAmplitudeMeters = 1000d;
        public double TerrainBaseFrequency = 3d;
        public int TerrainOctaves = 5;
        public double TerrainSeaLevelMeters = double.NegativeInfinity;
        public double TerrainLacunarity = 2d;
        public double TerrainGain = 0.5d;
        public double TerrainContinentFrequency = 0d;
        public int TerrainContinentOctaves = 3;
        public double TerrainContinentThreshold = 0d;
        public double TerrainContinentSharpness = 0.25d;
        public double TerrainContinentDepth = 0.75d;
        public double TerrainRidgedMix = 0d;
        public double TerrainPlainMix = 0d;
        public double TerrainPlainFrequency = 0d;
        public int TerrainPlainOctaves = 2;
        public double TerrainPlainThreshold = 0d;
        public double TerrainPlainSharpness = 0.3d;
        public double TerrainPlainElevation = 0.1d;
        public double TerrainDetailMix = 0d;
        public double TerrainDetailFrequency = 0d;
        public int TerrainDetailOctaves = 5;
        public double TerrainWarpStrength = 0d;
        public double TerrainWarpFrequency = 1d;
        public int TerrainWarpOctaves = 2;
        public int TerrainWarpSeedOffset = 0;
        public double TerrainColorRockSlopeTan = 0d;
        public double TerrainColorRockSlopeWidth = 0.1d;
        public double TerrainColorSnowSlopeTan = 0d;
        public double TerrainColorNoiseFrequency = 0d;
        public int TerrainColorNoiseOctaves = 3;
        public double TerrainColorNoiseStrength = 0d;
        public int TerrainColorNoiseSeedOffset = 0;
        public double TerrainColorDetailFrequency = 0d;
        public int TerrainColorDetailOctaves = 3;
        public double TerrainColorDetailStrength = 0d;
        public int TerrainColorDetailSeedOffset = 0;

        /// <summary>Индекс родителя в списке; -1 — корень дерева (звезда).</summary>
        public int ParentIndex = -1;
    }

    /// <summary>
    /// Чертёж системы: сборка StarSystem из плоских данных. Отделена от
    /// MonoBehaviour-оберток (BodyAuthoring) ровно для того, чтобы логика
    /// сборки была тестируемой на чистом стенде (T65) и не тащила Unity API
    /// в горячий цикл. На Build() срабатывает громкая Hill-валидация (P2) —
    /// нестабильный контент падает при создании системы, до пекаря не доходит.
    /// </summary>
    [Serializable]
    public sealed class SystemBlueprint
    {
        public List<BodyBlueprint> Bodies = new List<BodyBlueprint>();

        public StarSystem Build()
        {
            if (Bodies == null || Bodies.Count == 0)
            {
                throw new InvalidOperationException("Чертёж системы пуст: добавьте хотя бы звезду.");
            }

            int rootCount = 0;
            int rootIndex = -1;
            for (int i = 0; i < Bodies.Count; i++)
            {
                if (Bodies[i].ParentIndex < 0)
                {
                    rootCount++;
                    rootIndex = i;
                }
            }

            if (rootCount != 1)
            {
                throw new InvalidOperationException(
                    "Чертёж обязан содержать ровно одну звезду (тело без родителя); найдено " + rootCount + ".");
            }

            var created = new List<OrbitingBody>(Bodies.Count);
            for (int i = 0; i < Bodies.Count; i++)
            {
                BodyBlueprint bp = Bodies[i];
                var body = new OrbitingBody
                {
                    Name = string.IsNullOrEmpty(bp.Name) ? ("Body" + i) : bp.Name,
                    StandardGravitationalParameter = bp.StandardGravitationalParameter,
                    Radius = bp.Radius,
                    CrashToleranceMps = bp.CrashToleranceMps,
                    SurfaceStaticFrictionMu = bp.SurfaceStaticFrictionMu,
                    SurfaceKineticFrictionMu = bp.SurfaceKineticFrictionMu,
                    SemiMajorAxis = bp.SemiMajorAxis,
                    Eccentricity = bp.Eccentricity,
                    InclinationDegrees = bp.InclinationDegrees,
                    LongitudeOfAscendingNodeDegrees = bp.LongitudeOfAscendingNodeDegrees,
                    ArgumentOfPeriapsisDegrees = bp.ArgumentOfPeriapsisDegrees,
                    MeanAnomalyAtEpochDegrees = bp.MeanAnomalyAtEpochDegrees,
                    EpochTimeSeconds = bp.EpochTimeSeconds,
                    RotationPeriodSeconds = bp.RotationPeriodSeconds,
                    PrimeMeridianOffsetDegrees = bp.PrimeMeridianOffsetDegrees,
                    NorthPoleDirection = new Vector3d(bp.NorthPoleX, bp.NorthPoleY, bp.NorthPoleZ),
                    Atmosphere = bp.Atmosphere
                };
                if (bp.TerrainEnabled)
                {
                    body.Terrain = new HeightfieldTerrain
                    {
                        Seed = bp.TerrainSeed,
                        AmplitudeMeters = bp.TerrainAmplitudeMeters,
                        BaseFrequency = bp.TerrainBaseFrequency,
                        Octaves = bp.TerrainOctaves,
                        SeaLevelMeters = bp.TerrainSeaLevelMeters,
                        Lacunarity = bp.TerrainLacunarity,
                        Gain = bp.TerrainGain,
                        ContinentFrequency = bp.TerrainContinentFrequency,
                        ContinentOctaves = bp.TerrainContinentOctaves,
                        ContinentThreshold = bp.TerrainContinentThreshold,
                        ContinentSharpness = bp.TerrainContinentSharpness,
                        ContinentDepth = bp.TerrainContinentDepth,
                        RidgedMix = bp.TerrainRidgedMix,
                        PlainMix = bp.TerrainPlainMix,
                        PlainFrequency = bp.TerrainPlainFrequency,
                        PlainOctaves = bp.TerrainPlainOctaves,
                        PlainThreshold = bp.TerrainPlainThreshold,
                        PlainSharpness = bp.TerrainPlainSharpness,
                        PlainElevation = bp.TerrainPlainElevation,
                        DetailMix = bp.TerrainDetailMix,
                        DetailFrequency = bp.TerrainDetailFrequency,
                        DetailOctaves = bp.TerrainDetailOctaves,
                        WarpStrength = bp.TerrainWarpStrength,
                        WarpFrequency = bp.TerrainWarpFrequency,
                        WarpOctaves = bp.TerrainWarpOctaves,
                        WarpSeedOffset = bp.TerrainWarpSeedOffset,
                        ColorRockSlopeTan = bp.TerrainColorRockSlopeTan,
                        ColorRockSlopeWidth = bp.TerrainColorRockSlopeWidth,
                        ColorSnowSlopeTan = bp.TerrainColorSnowSlopeTan,
                        ColorNoiseFrequency = bp.TerrainColorNoiseFrequency,
                        ColorNoiseOctaves = bp.TerrainColorNoiseOctaves,
                        ColorNoiseStrength = bp.TerrainColorNoiseStrength,
                        ColorNoiseSeedOffset = bp.TerrainColorNoiseSeedOffset,
                        ColorDetailFrequency = bp.TerrainColorDetailFrequency,
                        ColorDetailOctaves = bp.TerrainColorDetailOctaves,
                        ColorDetailStrength = bp.TerrainColorDetailStrength,
                        ColorDetailSeedOffset = bp.TerrainColorDetailSeedOffset
                    };
                }

                created.Add(body);
            }

            for (int i = 0; i < Bodies.Count; i++)
            {
                int p = Bodies[i].ParentIndex;
                if (p < 0)
                {
                    continue;
                }

                if (p >= Bodies.Count || p == i)
                {
                    throw new InvalidOperationException(
                        "Тело " + Bodies[i].Name + ": некорректный ParentIndex " + p + ".");
                }

                created[i].Parent = created[p];
                created[p].Children.Add(created[i]);
            }

            // Приливный захват — ПОСЛЕ связки иерархии: μ_local требует дерева
            // (μ_родителя + μ_своего поддерева). Pure-функция данных элементов —
            // пересборка даёт бит-в-бит тот же результат.
            for (int i = 0; i < Bodies.Count; i++)
            {
                if (Bodies[i].TidallyLocked)
                {
                    created[i].ApplyTidalLock();
                }
            }

            return new StarSystem(created[rootIndex]);
        }
    }
}
