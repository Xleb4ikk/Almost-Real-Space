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

        /// <summary>Мин. нормированная высота палитры (песчаная полоса пляжа).</summary>
        public double MinNormalizedHeight;

        /// <summary>Сила цветовой маски рельефа (та же, что в шейдере палитры).</summary>
        public double ColorNoiseStrength;

        public double MaxSlopeTan;
        public bool AvoidWater;
        public double WetMin;
        public double WetMax;
        public double WetFade;
        public double Density;
        public double DistributionFrequency;
        public int DistributionOctaves;
        public int DistributionSeedOffset;
        public double ClusterThreshold;
        public double ClusterFade;
        public double MinScale;
        public double MaxScale;
        public double SteepPower;

        /// <summary>Осевой офсет (м) вдоль радиали: пивот модели не в основании.</summary>
        public double GroundOffsetMeters;

        /// <summary>Погружение в землю: доля масштаба инстанса.</summary>
        public double GroundSinkFactor;

        /// <summary>Затухание плотности по дистанции до КАМЕРЫ (внутри чанка):
        /// включено рендерером. Позволяет держать острова плотными вблизи и
        /// редко — вдали, а не размазывать весь чанк лимитом инстансов.</summary>
        public bool UseCameraFalloff;

        /// <summary>Камера в локальных координатах чанка (sim, м).</summary>
        public double CameraLocalX;
        public double CameraLocalY;
        public double CameraLocalZ;

        /// <summary>Дистанция (м), с которой начинается прореживание.</summary>
        public double FalloffNearMeters;

        /// <summary>Дистанция (м) полного прореживания (FarDensity).</summary>
        public double FalloffFarMeters;

        /// <summary>Плотность на дальней границе (0..1).</summary>
        public double FarDensity;

        /// <summary>Масштаб экспоненциального затухания (м); 0 = линейное.</summary>
        public double FalloffScaleMeters;

        /// <summary>Радиус плоского ядра (м): до него плотность полная.</summary>
        public double DensityCoreMeters;

        /// <summary>Считать плотность НА ИНСТАНС (подтуфту), а не на клетку:
        /// клетка лишь отбирает кандидатов, а затухание/плотность/рандом
        /// применяются в рендерере к каждому подтуфту — иначе на крупной
        /// клетке появлялся резкий «блок» травы вместо плавного края острова.</summary>
        public bool PerInstanceDensity;

        /// <summary>Направление ветра зоны (рад): ставится вызывающим кодом после FromLayer.</summary>
        public double WindAzimuthRad;
        public double WindJitterRad;
        public double WindLeanMinDegrees;
        public double WindLeanMaxDegrees;
        public double ResolvedMinSink;
        public double ResolvedMaxSink;

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
                MinNormalizedHeight = layer.MinNormalizedHeight,
                ColorNoiseStrength = terrain.ColorNoiseStrength,
                MaxSlopeTan = layer.MaxSlopeTan,
                AvoidWater = layer.AvoidWater,
                WetMin = layer.WetMin,
                WetMax = layer.WetMax,
                WetFade = layer.WetFade,
                Density = layer.Density,
                // Пятно задано в метрах — частоту считаем от радиуса тела:
                // один период шума ≈ R/frequency метров по поверхности.
                DistributionFrequency = layer.ClusterPatchMeters > 0d
                    ? radiusMeters / layer.ClusterPatchMeters
                    : layer.DistributionFrequency,
                DistributionOctaves = layer.DistributionOctaves,
                DistributionSeedOffset = layer.DistributionSeedOffset,
                ClusterThreshold = layer.ClusterThreshold,
                ClusterFade = layer.ClusterFade,
                MinScale = layer.MinScale,
                MaxScale = layer.MaxScale,
                SteepPower = layer.SteepPower,
                GroundOffsetMeters = layer.GroundOffsetMeters,
                GroundSinkFactor = layer.GroundSinkFactor,
                FalloffNearMeters = layer.NearDistanceMeters,
                FalloffFarMeters = Math.Max(layer.NearDistanceMeters + 1d, layer.MaxDistanceMeters),
                FarDensity = Math.Max(0d, Math.Min(1d, layer.FarDensity)),
                FalloffScaleMeters = layer.DensityFalloffMeters,
                DensityCoreMeters = Math.Max(0d, layer.DensityCoreMeters),
                WindJitterRad = layer.WindJitterDegrees * 0.017453292519943295d,
                WindLeanMinDegrees = layer.WindLeanMinDegrees,
                WindLeanMaxDegrees = layer.WindLeanMaxDegrees,
                ResolvedMinSink = layer.MinGroundSinkFactor >= 0d ? layer.MinGroundSinkFactor : layer.GroundSinkFactor,
                ResolvedMaxSink = layer.MaxGroundSinkFactor >= 0d ? layer.MaxGroundSinkFactor : layer.GroundSinkFactor,
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
        public float SinkFactor;
        public float LeanDegrees;

        /// <summary>Вес поверхности (кластер×биом×склон) 0..1 — для режима
        /// PerInstanceDensity: рендерер разыгрывает приём на каждый подтуфт.</summary>
        public float DensityWeight;

        /// <summary>Индекс варианта меша ближнего LOD (для разнообразия видов).</summary>
        public int MeshIndex;

        /// <summary>Когда травинка появилась (Time.time + разброс). Матричная
        /// джоба растит её масштабом за DecorBladeFadeSeconds: новое появляется
        /// поштучно и плавно, а не полосой. При пересборке пула старые травинки
        /// переносятся со своим BirthTime — уже выросшие не «прорастают» заново.</summary>
        public float BirthTime;
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
            double buryRandom, double leanRandom,
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

            double colorNoise = Clamp(TerrainNoise.SampleColorNoise(terrain, direction), -1d, 1d);

            // Та же песчаная граница, что у палитры рельефа: t = h/amp + mask·strength.
            // Трава не должна расти на пляжной полосе (её красит Sand).
            if (p.MinNormalizedHeight > 0d)
            {
                double normalized = (aboveSea / Math.Max(1d, p.AmplitudeMeters))
                    + (colorNoise * p.ColorNoiseStrength);
                if (normalized < p.MinNormalizedHeight)
                {
                    return false;
                }
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

            double wet = Clamp01(0.5d + (colorNoise * 1.6d));
            if (wet < p.WetMin || wet > p.WetMax)
            {
                return false;
            }

            // Мягкие края: у островов (ClusterFade) и у биома (WetFade).
            // 0 = жёсткий порог, как было раньше.
            double clusterWeight;
            if (p.ClusterFade > 1e-9d)
            {
                clusterWeight = Smoothstep01((cluster - p.ClusterThreshold) / p.ClusterFade);
                if (clusterWeight <= 0d)
                {
                    return false;
                }
            }
            else
            {
                clusterWeight = (cluster - p.ClusterThreshold) / Math.Max(1e-9d, 1d - p.ClusterThreshold);
            }

            double wetWeight = 1d;
            if (p.WetFade > 1e-9d)
            {
                // Край рампы — только у границ ВНУТРИ диапазона: wet выше 1 и
                // ниже 0 не бывает, а fade у WetMax=1 занулял бы самый влажный
                // биом (там wet=1 ровно) — трава пропадала в лесах.
                if (p.WetMin > 0d)
                {
                    wetWeight *= Smoothstep01((wet - p.WetMin) / p.WetFade);
                }

                if (p.WetMax < 1d)
                {
                    wetWeight *= Smoothstep01((p.WetMax - wet) / p.WetFade);
                }

                if (wetWeight <= 0d)
                {
                    return false;
                }
            }

            double slopeWeight = 1d - (slope / Math.Max(1e-9d, p.MaxSlopeTan));
            slopeWeight = Clamp01(slopeWeight);
            if (p.SteepPower > 0d)
            {
                slopeWeight = Math.Pow(slopeWeight, p.SteepPower);
            }

            double radius = p.MinScale + (random.y * Math.Max(0d, p.MaxScale - p.MinScale));
            double sinkFactor = p.ResolvedMinSink + (buryRandom * Math.Max(0d, p.ResolvedMaxSink - p.ResolvedMinSink));
            double offset = p.GroundOffsetMeters - (radius * sinkFactor);
            double3 astro = (direction * (p.RadiusMeters + rawHeight))
                + (new double3(surfaceNormal.x, -surfaceNormal.z, surfaceNormal.y) * offset);
            double3 rel = astro - new double3(p.ChunkCenterX, p.ChunkCenterY, p.ChunkCenterZ);

            // sim = (x, z, −y) — тот же мост, что AstroFrame (без ссылки на UnityEngine).
            float3 simPosition = new float3((float)rel.x, (float)rel.z, (float)-rel.y);

            double accept = p.Density * clusterWeight * wetWeight * slopeWeight;
            if (!p.PerInstanceDensity)
            {
                // Затухание по ТАНГЕНЦИАЛЬНОЙ дистанции до камеры ВНУТРИ чанка:
                // высота полёта не гасит траву под собой, а пешие переходы
                // по-прежнему плавно редеют к границе. Без этого крупный чанк
                // размазывался лимитом инстансов в равномерно редкие точки.
                if (p.UseCameraFalloff && p.FarDensity < 1d)
                {
                    double ddx = simPosition.x - p.CameraLocalX;
                    double ddy = simPosition.y - p.CameraLocalY;
                    double ddz = simPosition.z - p.CameraLocalZ;
                    double along = (ddx * surfaceNormal.x) + (ddy * surfaceNormal.y) + (ddz * surfaceNormal.z);
                    double tx = ddx - (surfaceNormal.x * along);
                    double ty = ddy - (surfaceNormal.y * along);
                    double tz = ddz - (surfaceNormal.z * along);
                    double distance = Math.Sqrt((tx * tx) + (ty * ty) + (tz * tz));
                    accept *= Falloff(p, distance);
                }

                if (random.x >= accept)
                {
                    return false;
                }
            }

            instance.Position = simPosition;

            // Режим PerInstanceDensity: форма острова — клеточный вес, а
            // затухание по дистанции до камеры считает РЕНДЕРЕР (там обе точки
            // гарантированно в одной системе координат — меш-фрейме чанка).
            instance.DensityWeight = (float)(clusterWeight * wetWeight * slopeWeight);
            instance.Normal = surfaceNormal;
            instance.Scale = (float)radius;
            instance.SinkFactor = (float)sinkFactor;
            double jitter = (random.z - 0.5d) * 2d * p.WindJitterRad;
            instance.Yaw = (float)(p.WindAzimuthRad + jitter);
            instance.LeanDegrees = (float)(p.WindLeanMinDegrees + (leanRandom * Math.Max(0d, p.WindLeanMaxDegrees - p.WindLeanMinDegrees)));
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

        private static double Smoothstep01(double t)
        {
            t = Clamp01(t);
            return t * t * (3d - (2d * t));
        }

        /// <summary>Затухание плотности по дистанции (общее для клетки и подтуфт).
        /// До плоского ядра (DensityCoreMeters) плотность полная — край острова
        /// не «дышит» при пересборках и у игрока не редеет.</summary>
        public static double Falloff(GroundDecorPlacementParams p, double distance)
        {
            if (p.FarDensity >= 1d && p.DensityCoreMeters <= 0d)
            {
                return 1d;
            }

            double d = Math.Max(0d, distance) - Math.Max(0d, p.DensityCoreMeters);
            if (d <= 0d)
            {
                return 1d;
            }

            if (p.FarDensity >= 1d)
            {
                return 1d;
            }

            if (p.FalloffScaleMeters > 0d)
            {
                return p.FarDensity
                    + ((1d - p.FarDensity) * Math.Exp(-d / p.FalloffScaleMeters));
            }

            double span = Math.Max(1d, p.FalloffFarMeters - p.FalloffNearMeters);
            double t = Clamp01(d / span);
            return 1d + ((p.FarDensity - 1d) * t);
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

        [ReadOnly]
        public NativeArray<double> BuryRandoms;

        [ReadOnly]
        public NativeArray<double> LeanRandoms;

        public TerrainNoiseParams Terrain;

        public GroundDecorPlacementParams Placement;

        public NativeArray<int> Accepted;

        public NativeArray<GroundDecorInstance> Instances;

        public void Execute(int index)
        {
            if (GroundDecorDistribution.TryEvaluate(
                Placement, Terrain, Directions[index], Randoms[index], MeshPicks[index], BuryRandoms[index], LeanRandoms[index], out GroundDecorInstance instance))
            {
                Accepted[index] = 1;
                Instances[index] = instance;
            }
        }
    }
}
