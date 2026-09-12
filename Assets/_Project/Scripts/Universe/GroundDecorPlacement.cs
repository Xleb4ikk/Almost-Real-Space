using System;
using Galilego.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Galilego.Universe
{
    /// <summary>
    /// Плоские параметры размещения декора для Burst-джобы: только blittable,
    /// без ссылок. Собираются из GroundDecorLayer + HeightfieldTerrain
    /// (форма рельефа и уровень моря — те же, что у физики/рендера).
    /// </summary>
    public struct GroundDecorPlacementParams
    {
        /// <summary>Амплитуда рельефа (м) — из HeightfieldTerrain.</summary>
        public double AmplitudeMeters;

        /// <summary>Уровень моря (м) — из HeightfieldTerrain.</summary>
        public double SeaLevelMeters;

        /// <summary>Радиус тела (м): перевод углового шага склона в метры.</summary>
        public double RadiusMeters;

        /// <summary>База конечных разностей для склона (м).</summary>
        public double SampleStepMeters;

        public double MinAltitudeMeters;
        public double MaxAltitudeMeters;
        public double MaxSlopeTan;
        public bool AvoidWater;
        public double WetMin;
        public double WetMax;
        public double Density;
        public double DistributionFrequency;
        public int DistributionOctaves;
        public int DistributionSeedOffset;
        public double ClusterThreshold;
        public double MinScale;
        public double MaxScale;
        public double SteepPower;

        /// <summary>Осевой офсет (м) вдоль радиали: пивот модели не в основании.</summary>
        public double GroundOffsetMeters;

        /// <summary>Погружение в землю: доля масштаба инстанса.</summary>
        public double GroundSinkFactor;

        /// <summary>Сколько мешей ближнего LOD у слоя (для случайного выбора варианта).</summary>
        public int NearMeshCount;

        /// <summary>Центр чанка (тел-fixed, м): позиции инстансов локальны ему (float-точность).</summary>
        public double ChunkCenterX;
        public double ChunkCenterY;
        public double ChunkCenterZ;

        public static GroundDecorPlacementParams FromLayer(
            GroundDecorLayer layer, HeightfieldTerrain terrain, double radiusMeters, Vector3d chunkCenter)
        {
            return new GroundDecorPlacementParams
            {
                AmplitudeMeters = terrain.AmplitudeMeters,
                SeaLevelMeters = terrain.SeaLevelMeters,
                RadiusMeters = radiusMeters,
                SampleStepMeters = 1d,
                NearMeshCount = layer.NearMeshes != null ? layer.NearMeshes.Length : 0,
                MinAltitudeMeters = layer.MinAltitudeMeters,
                MaxAltitudeMeters = layer.MaxAltitudeMeters,
                MaxSlopeTan = layer.MaxSlopeTan,
                AvoidWater = layer.AvoidWater,
                WetMin = layer.WetMin,
                WetMax = layer.WetMax,
                Density = layer.Density,
                DistributionFrequency = layer.DistributionFrequency,
                DistributionOctaves = layer.DistributionOctaves,
                DistributionSeedOffset = layer.DistributionSeedOffset,
                ClusterThreshold = layer.ClusterThreshold,
                MinScale = layer.MinScale,
                MaxScale = layer.MaxScale,
                SteepPower = layer.SteepPower,
                GroundOffsetMeters = layer.GroundOffsetMeters,
                GroundSinkFactor = layer.GroundSinkFactor,
                ChunkCenterX = chunkCenter.X,
                ChunkCenterY = chunkCenter.Y,
                ChunkCenterZ = chunkCenter.Z
            };
        }
    }

    /// <summary>Один инстанс декора: позиция/нормаль — sim-кадр (Y-up) локально чанку.</summary>
    public struct GroundDecorInstance
    {
        public float3 Position;
        public float3 Normal;
        public float Scale;
        public float Yaw;

        /// <summary>Индекс варианта меша ближнего LOD (для разнообразия видов).</summary>
        public int MeshIndex;
    }

    /// <summary>
    /// Детерминированное размещение: чистая функция направления. Высоту/склон
    /// считает ТА ЖЕ TerrainNoise, что физика и рендер рельефа (паритет по
    /// построению); биом — та же wet-маска SampleColorNoise, что красит
    /// поверхность. Никакого runtime-RNG: зерно + хеш направления.
    /// </summary>
    public static class GroundDecorDistribution
    {
        /// <summary>Хеш в [0,1) — джиттер ячейки/масштаб/поворот (детерминирован).</summary>
        public static double Hash01(int a, int b, int c, int d, int salt)
        {
            unchecked
            {
                int h = (a * 374761393) + (b * 668265263) + (c * 1274126177) + (d * 2147483647) + (salt * 7919);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216d;
            }
        }

        /// <summary>
        /// Попытка посадить инстанс. random.x — доля отбора, random.y — масштаб,
        /// random.z — поворот, meshPick — выбор варианта меша [0,1). Фильтры:
        /// вода, высота, склон, wet-биом, кластерный шум; плотность дополнительно
        /// гасится склоном (SteepPower).
        /// </summary>
        public static bool TryEvaluate(
            GroundDecorPlacementParams p, TerrainNoiseParams terrain, double3 direction, double3 random, double meshPick,
            out GroundDecorInstance instance)
        {
            instance = default;

            double rawHeight = TerrainNoise.SampleHeight(terrain, direction) * p.AmplitudeMeters;
            if (p.AvoidWater && rawHeight <= p.SeaLevelMeters + (p.AmplitudeMeters * 0.001d))
            {
                return false;
            }

            double aboveSea = rawHeight - p.SeaLevelMeters;
            if (aboveSea < p.MinAltitudeMeters || aboveSea > p.MaxAltitudeMeters)
            {
                return false;
            }

            double slope = SlopeTan(terrain, p, direction, out float3 surfaceNormal);
            if (slope > p.MaxSlopeTan)
            {
                return false;
            }

            double cluster = Clamp01((TerrainNoise.SampleDecorNoise(
                terrain, p.DistributionSeedOffset, p.DistributionFrequency, p.DistributionOctaves, direction) + 1d) * 0.5d);
            if (cluster < p.ClusterThreshold)
            {
                return false;
            }

            double wet = Clamp01(0.5d + (Clamp(TerrainNoise.SampleColorNoise(terrain, direction), -1d, 1d) * 1.6d));
            if (wet < p.WetMin || wet > p.WetMax)
            {
                return false;
            }

            double clusterWeight = (cluster - p.ClusterThreshold) / Math.Max(1e-9d, 1d - p.ClusterThreshold);
            double slopeWeight = 1d - (slope / Math.Max(1e-9d, p.MaxSlopeTan));
            slopeWeight = Clamp01(slopeWeight);
            if (p.SteepPower > 0d)
            {
                slopeWeight = Math.Pow(slopeWeight, p.SteepPower);
            }

            double accept = p.Density * clusterWeight * slopeWeight;
            if (random.x >= accept)
            {
                return false;
            }

            double radius = p.MinScale + (random.y * Math.Max(0d, p.MaxScale - p.MinScale));
            double offset = p.GroundOffsetMeters - (radius * p.GroundSinkFactor);
            double3 astro = (direction * (p.RadiusMeters + rawHeight))
                + (new double3(surfaceNormal.x, -surfaceNormal.z, surfaceNormal.y) * offset);
            double3 rel = astro - new double3(p.ChunkCenterX, p.ChunkCenterY, p.ChunkCenterZ);

            // sim = (x, z, −y) — тот же мост, что AstroFrame (без ссылки на UnityEngine).
            instance.Position = new float3((float)rel.x, (float)rel.z, (float)-rel.y);
            instance.Normal = surfaceNormal;
            instance.Scale = (float)radius;
            instance.Yaw = (float)(random.z * 6.28318530717958647692d);
            instance.MeshIndex = p.NearMeshCount > 1
                ? Math.Min(p.NearMeshCount - 1, (int)(meshPick * p.NearMeshCount))
                : 0;
            return true;
        }

        /// <summary>
        /// Тангенс склона по центральным разностям высоты в касательной плоскости
        /// + нормаль поверхности из тех же трёх точек (тел-fixed формула
        /// p = dir·(R+h), кросс разностей) — карточки/деревья ставятся по склону,
        /// а не по радиали (иначе на склонах трава «лежит»).
        /// </summary>
        public static double SlopeTan(
            TerrainNoiseParams terrain, GroundDecorPlacementParams p, double3 direction, out float3 surfaceNormal)
        {
            double epsilon = Math.Max(1e-7d, p.SampleStepMeters / Math.Max(1d, p.RadiusMeters));
            double3 axis = math.abs(direction.y) < 0.9d ? new double3(0d, 1d, 0d) : new double3(1d, 0d, 0d);
            double3 tangent = math.normalize(math.cross(axis, direction));
            double3 bitangent = math.cross(direction, tangent);
            double3 dir1 = math.normalize(direction + (tangent * epsilon));
            double3 dir2 = math.normalize(direction + (bitangent * epsilon));

            double h0 = TerrainNoise.SampleHeight(terrain, direction);
            double h1 = TerrainNoise.SampleHeight(terrain, dir1);
            double h2 = TerrainNoise.SampleHeight(terrain, dir2);
            double dh1 = (h1 - h0) * p.AmplitudeMeters;
            double dh2 = (h2 - h0) * p.AmplitudeMeters;

            double3 p0 = direction * (p.RadiusMeters + (h0 * p.AmplitudeMeters));
            double3 p1 = dir1 * (p.RadiusMeters + (h1 * p.AmplitudeMeters));
            double3 p2 = dir2 * (p.RadiusMeters + (h2 * p.AmplitudeMeters));
            double3 normal = math.normalize(math.cross(p1 - p0, p2 - p0));
            if (math.dot(normal, direction) < 0d)
            {
                normal = -normal;
            }

            surfaceNormal = new float3((float)normal.x, (float)normal.z, (float)-normal.y);
            return Math.Sqrt((dh1 * dh1) + (dh2 * dh2)) / p.SampleStepMeters;
        }

        private static double Clamp(double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        private static double Clamp01(double v)
        {
            return v < 0d ? 0d : (v > 1d ? 1d : v);
        }
    }

    /// <summary>
    /// Burst-обход кандидатов чанка: направления готовит main thread (джиттер
    /// ячеек), здесь — фильтры и выход инстансов. Компактификация — на main
    /// thread по Accepted (параллельная запись в очередь не нужна: кандидатов
    /// на чанк ограниченное число).
    /// </summary>
    [BurstCompile]
    public struct GroundDecorCandidateJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<double3> Directions;

        [ReadOnly]
        public NativeArray<double3> Randoms;

        /// <summary>Выбор варианта меша [0,1) на кандидата (детерминированный хеш).</summary>
        [ReadOnly]
        public NativeArray<double> MeshPicks;

        public TerrainNoiseParams Terrain;

        public GroundDecorPlacementParams Placement;

        public NativeArray<int> Accepted;

        public NativeArray<GroundDecorInstance> Instances;

        public void Execute(int index)
        {
            if (GroundDecorDistribution.TryEvaluate(
                Placement, Terrain, Directions[index], Randoms[index], MeshPicks[index], out GroundDecorInstance instance))
            {
                Accepted[index] = 1;
                Instances[index] = instance;
            }
        }
    }
}
