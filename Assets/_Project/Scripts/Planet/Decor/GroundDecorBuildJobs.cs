using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Burst-стадия сборки травы (слои с PerInstanceDensity): вес кандидата
    /// с затуханием и ТОЧНОЕ число травинок клетки (стохастическое округление
    /// ожидания). Раньше здесь резервировались слоты «вслепую» по SubPerCell —
    /// ближний круг съедал бюджет, а записанные впустую слоты оставались
    /// дырками в пуле и в отрисовке.
    /// </summary>
    [BurstCompile]
    public struct GroundDecorWeightJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> Accepted;
        [ReadOnly] public NativeArray<GroundDecorInstance> Instances;
        [ReadOnly] public NativeArray<float2> Uvs;
        [ReadOnly] public NativeArray<Vector3> MeshVertices;

        public int CoreN;
        public double U0;
        public double V0;
        public double StepUv;
        public float3 CameraLocal;
        public GroundDecorPlacementParams Placement;
        public int SubPerCell;
        public int Face;
        public int Ix;
        public int Iy;
        /// <summary>Радиус пула слоя (м) — дальше травинок нет.</summary>
        public float PoolRadius;
        /// <summary>Полоса плавного затухания перед PoolRadius (м): без неё
        /// край пула обрывался бы ровной линией на границе видимости.</summary>
        public float RadiusFadeMeters;

        /// <summary>Число травинок клетки (0..SubPerCell): ожидание плотности
        /// с затуханием, округлённое стохастически — сумма по чанку точная.</summary>
        [WriteOnly] public NativeArray<int> Counts;

        /// <summary>Тангенциальная дистанция кандидата от центра выборки.</summary>
        [WriteOnly] public NativeArray<float> Distances;

        public void Execute(int index)
        {
            Counts[index] = 0;
            Distances[index] = float.MaxValue;
            if (Accepted[index] == 0)
            {
                return;
            }

            GroundDecorInstance instance = Instances[index];
            float3 normal = instance.Normal;
            float length = math.length(normal);
            normal = length > 1e-6f ? normal / length : new float3(0f, 1f, 0f);

            Vector3 meshPoint = MeshPoint(index);
            float3 toCamera = new float3(meshPoint.x, meshPoint.y, meshPoint.z) - CameraLocal;
            float along = math.dot(toCamera, normal);
            float3 tangential = toCamera - (normal * along);
            float distance = math.length(tangential);
            Distances[index] = distance;

            // Плотность на подтуфт: биом×склон (DensityWeight) × профиль слоя
            // (плоское ядро + затухание). Дальше — сколько из SubPerCell
            // подтуфтов реально сажаем: на дальней границе это 1-3 травинки на
            // клетку, вблизи — вся сетка.
            double density = Placement.Density * instance.DensityWeight
                * GroundDecorDistribution.Falloff(Placement, distance);
            if (RadiusFadeMeters > 0f)
            {
                // Мягкий край у радиуса пула: плотность гаснет к PoolRadius,
                // поэтому даже «недобранный» бюджет не рисует линию по земле.
                float t = (PoolRadius - distance) / RadiusFadeMeters;
                if (t < 1f)
                {
                    density *= math.max(0f, t);
                }
            }

            double wanted = math.max(0d, density) * SubPerCell;
            int count = (int)math.floor(wanted);
            if (count > SubPerCell)
            {
                count = SubPerCell;
            }

            double fraction = wanted - math.floor(wanted);
            if (fraction > 0d && count < SubPerCell
                && GroundDecorDistribution.Hash01(index + Ix, Face, Iy, 0, 43) < fraction)
            {
                count++;
            }

            Counts[index] = count;
        }

        private Vector3 MeshPoint(int index)
        {
            float2 uv = Uvs[index];
            double gridU = (uv.x - U0) / StepUv;
            double gridV = (uv.y - V0) / StepUv;
            gridU = math.max(0d, math.min(gridU, CoreN - 1d));
            gridV = math.max(0d, math.min(gridV, CoreN - 1d));
            int uIndex = math.min((int)math.floor(gridU), CoreN - 2);
            int vIndex = math.min((int)math.floor(gridV), CoreN - 2);
            float fu = (float)(gridU - uIndex);
            float fv = (float)(gridV - vIndex);

            Vector3 p00 = MeshVertices[(uIndex * CoreN) + vIndex];
            Vector3 p10 = MeshVertices[((uIndex + 1) * CoreN) + vIndex];
            Vector3 p01 = MeshVertices[(uIndex * CoreN) + vIndex + 1];
            Vector3 p11 = MeshVertices[((uIndex + 1) * CoreN) + vIndex + 1];

            if (fu + fv <= 1f)
            {
                return p00 + (fu * (p10 - p00)) + (fv * (p01 - p00));
            }

            return p11 + ((1f - fu) * (p01 - p11)) + ((1f - fv) * (p10 - p11));
        }
    }

    /// <summary>
    /// Раздача слотов записи: клетки обходятся БАКЕТАМИ ПО ДИСТАНЦИИ от центра
    /// выборки (ближние первыми), а не в порядке индекса сетки. Прежний
    /// ряд-мажорный обход при исчерпании бюджета оставлял выжившие клетки
    /// прямыми полосами по сетке — это и было видно как «трава спавнится
    /// линиями». Хвост бюджета (FadeShare) размывается: последние слоты
    /// получают меньше травинок, поэтому граница обрезки — плавное кольцо.
    /// Работает одним потоком: Count ~ десятков тысяч, в Burst это доли мс.
    /// </summary>
    [BurstCompile]
    public struct GroundDecorAllocateJob : IJob
    {
        [ReadOnly] public NativeArray<int> Counts;
        [ReadOnly] public NativeArray<float> Distances;

        public int Count;
        public int Take;
        /// <summary>Ширина бакета по дистанции (м): разрешение радиуса обрезки.</summary>
        public float BucketWidth;
        public int BucketCount;
        /// <summary>Доля бюджета на плавное затухание у границы обрезки (0..0.9).</summary>
        public float FadeShare;
        public int Salt;

        /// <summary>Скретч порядка клеток по бакетам: джоба и пишет, и читает его
        /// (не [WriteOnly] — иначе safety-система бросает на чтении).</summary>
        public NativeArray<int> CellOrder;
        [WriteOnly] public NativeArray<int> Offsets;
        [WriteOnly] public NativeArray<int> WriteCounts;
        /// <summary>[0] — записано травинок, [1] — радиус обрезки (м).</summary>
        [WriteOnly] public NativeArray<float> WrittenTotal;

        public void Execute()
        {
            WrittenTotal[0] = 0f;
            WrittenTotal[1] = 0f;

            int buckets = math.max(1, BucketCount);
            float width = math.max(1e-3f, BucketWidth);
            var cursor = new NativeArray<int>(buckets + 1, Allocator.Temp);
            for (int b = 0; b <= buckets; b++)
            {
                cursor[b] = 0;
            }

            int total = 0;
            for (int i = 0; i < Count; i++)
            {
                Offsets[i] = int.MaxValue;
                WriteCounts[i] = 0;
                int c = Counts[i];
                if (c > 0)
                {
                    cursor[BucketOf(Distances[i], width, buckets)]++;
                    total += c;
                }
            }

            if (total <= 0 || Take <= 0)
            {
                cursor.Dispose();
                return;
            }

            // Порядок клеток по бакетам (префиксные суммы) — обход от ближних
            // к дальним без сортировки.
            int running = 0;
            for (int b = 0; b < buckets; b++)
            {
                int n = cursor[b];
                cursor[b] = running;
                running += n;
            }

            cursor[buckets] = running;

            for (int i = 0; i < Count; i++)
            {
                if (Counts[i] <= 0)
                {
                    continue;
                }

                int b = BucketOf(Distances[i], width, buckets);
                CellOrder[cursor[b]++] = i;
            }

            int fadeSlots = math.max(1, (int)(Take * math.clamp(FadeShare, 0f, 0.9f)));
            int fadeStart = math.max(0, Take - fadeSlots);
            int written = 0;
            float cutRadius = 0f;
            int prevEnd = 0;
            for (int b = 0; b < buckets && written < Take; b++)
            {
                int end = cursor[b];
                for (int k = prevEnd; k < end && written < Take; k++)
                {
                    int i = CellOrder[k];
                    int c = Counts[i];
                    if (c <= 0)
                    {
                        continue;
                    }

                    // Плавный край: у последних слотов бюджета травинки редеют
                    // стохастически — граница пула перестаёт быть линией.
                    if (written > fadeStart)
                    {
                        float t = (Take - written) / (float)math.max(1, Take - fadeStart);
                        if (t < 1f)
                        {
                            double faded = c * (double)t;
                            int keep = (int)math.floor(faded);
                            if (keep < c && (faded - keep) > 0d
                                && GroundDecorDistribution.Hash01(i, Salt, 0, 0, 44) < (faded - keep))
                            {
                                keep++;
                            }

                            c = math.max(0, math.min(c, keep));
                        }
                    }

                    c = math.min(c, Take - written);
                    if (c <= 0)
                    {
                        continue;
                    }

                    Offsets[i] = written;
                    WriteCounts[i] = c;
                    written += c;
                    cutRadius = Distances[i];
                }

                prevEnd = end;
            }

            cursor.Dispose();
            WrittenTotal[0] = written;
            WrittenTotal[1] = cutRadius;
        }

        private static int BucketOf(float distance, float width, int buckets)
        {
            int b = (int)(math.max(0f, distance) / width);
            return math.min(b, buckets - 1);
        }
    }

    /// <summary>
    /// Разворот подтуфтов травы: пишутся ровно те травинки, что зарезервированы
    /// аллокатором (WriteCounts), без вероятностного отбора — он уже учтён в
    /// счёте клетки. Позиции внутри клетки берутся в «разреженном» порядке
    /// (шаг 13, взаимно прост с 36) со сдвигом по хешу клетки: прореженная
    /// клетка рассыпается по всей площади, а не липнет к углу.
    /// Запись — в свои непересекающиеся диапазоны слотов, гонок нет.
    /// </summary>
    [BurstCompile]
    public struct GroundDecorExpandJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> Accepted;
        [ReadOnly] public NativeArray<GroundDecorInstance> Instances;
        [ReadOnly] public NativeArray<float2> Uvs;
        [ReadOnly] public NativeArray<Vector3> MeshVertices;
        [ReadOnly] public NativeArray<int> WriteCounts;
        [ReadOnly] public NativeArray<int> WriteOffsets;
        /// <summary>[0] — радиус выборки, [1] — доля граничной полосы, [2] — ширина.</summary>
        [ReadOnly] public NativeArray<float> Selection;

        public int CoreN;
        public int Face;
        public int Ix;
        public int Iy;
        public int Cells;
        public double SizeUv;
        public double U0;
        public double V0;
        public double StepUv;
        public float3 CameraLocal;

        /// <summary>Шум рельефа — для перепроверки подтуфтов (вода/песок/горы).</summary>
        public TerrainNoiseParams Terrain;

        /// <summary>Фильтры слоя — для перепроверки подтуфтов и стража снэпа.</summary>
        public GroundDecorPlacementParams Placement;

        public int SubPerCell;
        /// <summary>Шаг разреженного порядка подтуфтов (взаимно прост с числом клеток сетки).</summary>
        public int SpreadStride;
        public double MinScale;
        public double MaxScale;
        public double WindJitterDegrees;
        public double WindLeanMinDegrees;
        public double WindLeanMaxDegrees;
        public double MinSink;
        public double MaxSink;
        public double GroundOffsetMeters;
        public int MeshCount;
        public int Take;
        /// <summary>Время появления травинок (Time.time): у каждой свой сдвиг
        /// по хешу — прорастают поштучно, а не полосой.</summary>
        public float BuildTime;
        public float StaggerSeconds;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<GroundDecorInstance> Stored;

        public void Execute(int index)
        {
            if (Accepted[index] == 0)
            {
                return;
            }

            int baseSlot = WriteOffsets[index];
            if (baseSlot == int.MaxValue)
            {
                return;
            }

            int subs = WriteCounts[index];
            if (subs <= 0)
            {
                return;
            }

            GroundDecorInstance baseInstance = Instances[index];
            float3 normal = baseInstance.Normal;
            float normalLength = math.length(normal);
            normal = normalLength > 1e-6f ? normal / normalLength : new float3(0f, 1f, 0f);

            double cellSizeUv = SizeUv / Cells;
            int cellA = index / Cells;
            int cellB = index % Cells;
            double cellU0 = U0 + (cellA * cellSizeUv);
            double cellV0 = V0 + (cellB * cellSizeUv);

            int subGridN = (int)math.ceil(math.sqrt((double)math.max(1, SubPerCell)));
            int subCount = math.max(1, subGridN * subGridN);
            int rotation = (int)(GroundDecorDistribution.Hash01(index + Ix, Face, Iy, 0, 45) * subCount);
            rotation = math.clamp(rotation, 0, subCount - 1);

            for (int s = 0; s < subs; s++)
            {
                GroundDecorInstance sub = baseInstance;
                double finalU;
                double finalV;
                int subSlot = subCount > 1
                    ? (rotation + (s * math.max(1, SpreadStride))) % subCount
                    : 0;
                if (subs == 1 || subCount <= 1)
                {
                    // Одиночная травинка — на джиттер-центре клетки (как раньше).
                    finalU = Uvs[index].x;
                    finalV = Uvs[index].y;
                }
                else
                {
                    int subRow = subSlot / subGridN;
                    int subCol = subSlot % subGridN;
                    double jU = GroundDecorDistribution.Hash01(subSlot, Face, index + Ix + 397, Iy, 36);
                    double jV = GroundDecorDistribution.Hash01(subSlot, Face, index + Ix, Iy + 1399, 37);
                    finalU = cellU0 + (((subCol + jU) / subGridN) * cellSizeUv);
                    finalV = cellV0 + (((subRow + jV) / subGridN) * cellSizeUv);

                    double scaleHash = GroundDecorDistribution.Hash01(subSlot, Face, index + Ix + 997, Iy, 33);
                    sub.Scale = (float)(MinScale + (scaleHash * math.max(0d, MaxScale - MinScale)));

                    double sinkHash = GroundDecorDistribution.Hash01(subSlot, Face, index + Ix + 1397, Iy + 293, 38);
                    sub.SinkFactor = (float)(MinSink + (sinkHash * math.max(0d, MaxSink - MinSink)));

                    double meshHash = GroundDecorDistribution.Hash01(subSlot, Face, index + Ix + 1497, Iy + 593, 39);
                    sub.MeshIndex = MeshCount > 1
                        ? math.min(MeshCount - 1, (int)(meshHash * MeshCount))
                        : 0;

                    double yawHash = GroundDecorDistribution.Hash01(subSlot, Face, index + Ix, Iy + 1999, 34);
                    double jitter = (yawHash - 0.5d) * 2d * WindJitterDegrees * 0.017453292519943295d;
                    sub.Yaw = (float)(baseInstance.Yaw + jitter);

                    double leanHash = GroundDecorDistribution.Hash01(subSlot, Face, index + Ix + 1997, Iy + 1993, 35);
                    sub.LeanDegrees = (float)(WindLeanMinDegrees
                        + (leanHash * math.max(0d, WindLeanMaxDegrees - WindLeanMinDegrees)));
                }

                // Отсев — БЕЗ continue: слоты заранее расписаны аллокатором, а
                // Stored — из пула с чужими данными. Пропущенная запись оставила
                // бы в слоте позиции чужого чанка (трава в небе), поэтому
                // отсеянный подтуфт пишется вырожденным (Scale 0 → точка 1e-4).
                bool culled = false;
                if (SubPerCell > 1)
                {
                    // Подтуфт ушёл от проверенного TryEvaluate центра клетки:
                    // перепроверяем жёсткие фильтры (вода/песок/горы/биом) по
                    // его собственному направлению. Без этого на грубом LOD
                    // край клетки заливал травой воду и пляж. Одиночные
                    // инстансы здесь не разбрасываются — им проверка не нужна.
                    double3 subDirection = GroundDecorDistribution.CubeFaceDirection(Face, finalU, finalV);
                    if (!GroundDecorDistribution.IsSurfaceAllowed(Placement, Terrain, subDirection))
                    {
                        culled = true;
                    }
                }

                Vector3 meshPoint = MeshPoint(finalU, finalV);
                float3 meshPoint3 = new float3(meshPoint.x, meshPoint.y, meshPoint.z);

                // Страж снэпа: грубый меш чанка отклоняется от аналитической
                // высоты на метры — точка обязана сама быть над водой.
                if (!culled && !GroundDecorDistribution.IsMeshPointAboveWater(Placement, meshPoint3))
                {
                    culled = true;
                }

                float3 toCamera = meshPoint3 - CameraLocal;
                float along = math.dot(toCamera, normal);
                float3 tangential = toCamera - (normal * along);
                float distance = math.length(tangential);
                if (!culled && distance > Selection[0] + Selection[2])
                {
                    culled = true;
                }

                if (culled)
                {
                    sub.Scale = 0f;
                    sub.BirthTime = 0f;
                    sub.Position = meshPoint3;
                }
                else
                {
                    float layerOffset = (float)(GroundOffsetMeters - (sub.Scale * sub.SinkFactor));
                    sub.Position = meshPoint3 + (normal * layerOffset);
                    // Появление: время + случайный сдвиг по (клетка, подтуфт).
                    sub.BirthTime = StaggerSeconds > 0f
                        ? BuildTime + ((float)GroundDecorDistribution.Hash01(
                            subSlot, Face, index + Ix + 1997, Iy, 42) * StaggerSeconds)
                        : 0f;
                }

                int slot = baseSlot + s;
                if (slot < Take)
                {
                    Stored[slot] = sub;
                }
            }
        }

        private Vector3 MeshPoint(double finalU, double finalV)
        {
            double gridU = (finalU - U0) / StepUv;
            double gridV = (finalV - V0) / StepUv;
            gridU = math.max(0d, math.min(gridU, CoreN - 1d));
            gridV = math.max(0d, math.min(gridV, CoreN - 1d));
            int uIndex = math.min((int)math.floor(gridU), CoreN - 2);
            int vIndex = math.min((int)math.floor(gridV), CoreN - 2);
            float fu = (float)(gridU - uIndex);
            float fv = (float)(gridV - vIndex);

            Vector3 p00 = MeshVertices[(uIndex * CoreN) + vIndex];
            Vector3 p10 = MeshVertices[((uIndex + 1) * CoreN) + vIndex];
            Vector3 p01 = MeshVertices[(uIndex * CoreN) + vIndex + 1];
            Vector3 p11 = MeshVertices[((uIndex + 1) * CoreN) + vIndex + 1];

            if (fu + fv <= 1f)
            {
                return p00 + (fu * (p10 - p00)) + (fv * (p01 - p00));
            }

            return p11 + ((1f - fu) * (p01 - p11)) + ((1f - fv) * (p10 - p11));
        }
    }

    /// <summary>
    /// Переносит из СТАРОГО пула травинки, оставшиеся в новой выборке (та же
    /// клетка, те же первые подтуфты): их данные и BirthTime сохраняются.
    /// При пересборке пула уже выросшие травинки не «прорастают» заново —
    /// новыми (со свежим BirthTime) остаются только добавленные градиентом.
    /// Так локальная плотность никогда не падает пачкой, а появление идёт
    /// поштучно и плавно.
    /// </summary>
    [BurstCompile]
    public struct GroundDecorMergeKeptJob : IJob
    {
        [ReadOnly] public NativeArray<GroundDecorInstance> PrevInstances;
        [ReadOnly] public NativeArray<int> PrevOffsets;
        [ReadOnly] public NativeArray<int> PrevCounts;
        [ReadOnly] public NativeArray<int> NewOffsets;
        [ReadOnly] public NativeArray<int> NewCounts;

        public int CellCount;
        public int Take;

        [NativeDisableParallelForRestriction] public NativeArray<GroundDecorInstance> Stored;

        public void Execute()
        {
            int cells = math.min(CellCount, math.min(PrevOffsets.Length, NewOffsets.Length));
            for (int i = 0; i < cells; i++)
            {
                int prevCount = PrevCounts[i];
                int newCount = NewCounts[i];
                if (prevCount <= 0 || newCount <= 0)
                {
                    continue;
                }

                int prevOffset = PrevOffsets[i];
                int newOffset = NewOffsets[i];
                if (prevOffset == int.MaxValue || newOffset == int.MaxValue)
                {
                    continue;
                }

                int keep = math.min(prevCount, newCount);
                for (int s = 0; s < keep; s++)
                {
                    int from = prevOffset + s;
                    int to = newOffset + s;
                    if (from >= PrevInstances.Length || to >= Take || to >= Stored.Length)
                    {
                        break;
                    }

                    Stored[to] = PrevInstances[from];
                }
            }
        }
    }
}
