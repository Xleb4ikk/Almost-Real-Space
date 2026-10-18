using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Stage 0: jitter grid of candidates and UV pre-pass, entirely in Burst.
    /// The jitter formula (separate hashes over a/b and (u,v)) and the face
    /// direction define the decor placement.
    /// </summary>
    [BurstCompile]
    public struct GroundDecorPrepJob : IJobParallelFor
    {
        public int Face;
        public int Depth;
        public int Ix;
        public int Iy;
        public int Cells;
        public int LayerIndex;
        public double U0;
        public double V0;
        public double SizeUv;

        [WriteOnly] public NativeArray<double3> Dirs;
        [WriteOnly] public NativeArray<float2> Uvs;
        [WriteOnly] public NativeArray<double3> Randoms;
        [WriteOnly] public NativeArray<double> MeshPicks;
        [WriteOnly] public NativeArray<double> BuryRandoms;
        [WriteOnly] public NativeArray<double> LeanRandoms;

        public void Execute(int index)
        {
            int a = index / Cells;
            int b = index % Cells;
            double uJitter = GroundDecorDistribution.Hash01(Face, Depth, Ix, (a * 97) + Iy + (b * 131), 11);
            double vJitter = GroundDecorDistribution.Hash01(Face, Depth, Iy, (b * 89) + Ix + (a * 137), 12);
            double u = U0 + (((a + uJitter) / Cells) * SizeUv);
            double v = V0 + (((b + vJitter) / Cells) * SizeUv);
            Dirs[index] = GroundDecorDistribution.CubeFaceDirection(Face, u, v);
            Uvs[index] = new float2((float)u, (float)v);
            Randoms[index] = new double3(
                GroundDecorDistribution.Hash01(Face, Depth, Ix + Iy, (index * 31) + LayerIndex, 21),
                GroundDecorDistribution.Hash01(Face, Depth, Iy, (index * 37) + LayerIndex, 22),
                GroundDecorDistribution.Hash01(Face, Depth, Ix, (index * 41) + LayerIndex, 23));
            MeshPicks[index] = GroundDecorDistribution.Hash01(Face, Depth, Ix, (index * 43) + LayerIndex, 24);
            BuryRandoms[index] = GroundDecorDistribution.Hash01(Face, Depth, Ix, (index * 47) + LayerIndex, 25);
            LeanRandoms[index] = GroundDecorDistribution.Hash01(Face, Depth, Ix, (index * 53) + LayerIndex, 26);
        }
    }

    /// <summary>
    /// Burst stage of grass assembly (layers with PerInstanceDensity): candidate
    /// weight with falloff and the EXACT blade count per cell (stochastic rounding
    /// of the expected value).
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
        /// <summary>Layer pool radius (m) — no blades beyond it.</summary>
        public float PoolRadius;
        /// <summary>Fade band before PoolRadius (m): softens the cut edge of the pool.</summary>
        public float RadiusFadeMeters;

        /// <summary>Blade count for the cell (0..SubPerCell): density expectation
        /// with falloff, stochastically rounded — the chunk total is exact.</summary>
        [WriteOnly] public NativeArray<int> Counts;

        /// <summary>Tangential distance of the candidate from the selection centre.</summary>
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

            // Density per sub-tuft: biome×slope (DensityWeight) × layer profile
            // (flat core + falloff). Then — how many of the SubPerCell
            // sub-tufts are planted: 1-3 blades per cell at the far edge,
            // the whole grid up close.
            double density = Placement.Density * instance.DensityWeight
                * GroundDecorDistribution.Falloff(Placement, distance);
            if (RadiusFadeMeters > 0f)
            {
                // Soft edge at the pool radius: density fades to zero at PoolRadius,
                // so even an under-filled budget does not draw a line on the ground.
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
    /// Write-slot allocation: cells are walked in BUCKETS BY DISTANCE from the
    /// selection centre (nearest first), not in grid-index order, so cells that
    /// fit in the budget form a round cut instead of straight stripes.
    /// The tail of the budget (FadeShare) is diluted: the last slots get fewer
    /// blades, so the cut boundary is a smooth ring.
    /// Runs single-threaded: Count ~ tens of thousands, fractions of a ms in Burst.
    /// </summary>
    [BurstCompile]
    public struct GroundDecorAllocateJob : IJob
    {
        [ReadOnly] public NativeArray<int> Counts;
        [ReadOnly] public NativeArray<float> Distances;

        public int Count;
        public int Take;
        /// <summary>Bucket width by distance (m): resolution of the cut radius.</summary>
        public float BucketWidth;
        public int BucketCount;
        /// <summary>Budget share for the soft fade at the cut boundary (0..0.9).</summary>
        public float FadeShare;
        public int Salt;

        /// <summary>Scratch cell order by buckets: the job both writes and reads
        /// it (not [WriteOnly] — the safety system would throw on read).</summary>
        public NativeArray<int> CellOrder;
        [WriteOnly] public NativeArray<int> Offsets;
        [WriteOnly] public NativeArray<int> WriteCounts;
        /// <summary>[0] — blades written, [1] — cut radius (m).</summary>
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

            // Cell order by buckets (prefix sums) — nearest-to-farthest walk
            // without sorting.
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

                    // Soft edge: the last budget slots thin out blades
                    // stochastically — the pool boundary is not a hard line.
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
    /// Grass sub-tuft expansion: exactly the blades reserved by the allocator
    /// (WriteCounts) are written, with no probabilistic selection — that is already
    /// accounted for in the cell count. Positions within the cell are taken in a
    /// "thinned" order (stride 13, coprime with 36) offset by the cell hash: a thinned
    /// cell scatters over the whole area instead of sticking to a corner.
    /// Writes go to private non-overlapping slot ranges, so there are no races.
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
        /// <summary>[0] — selection radius, [1] — boundary band share, [2] — width.</summary>
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

        /// <summary>Terrain noise — for re-checking sub-tufts (water/sand/mountains).</summary>
        public TerrainNoiseParams Terrain;

        /// <summary>Layer filters — for sub-tuft re-checks and the snap guard.</summary>
        public GroundDecorPlacementParams Placement;

        public int SubPerCell;
        /// <summary>Stride of the thinned sub-tuft order (coprime with the sub-grid cell count).</summary>
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
        /// <summary>Blade birth time base (Time.time): each gets its own hash
        /// offset — they sprout one by one, not in a band.</summary>
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
                    // Single blade — at the cell's jitter centre.
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

                // Culling — WITHOUT continue: slots are pre-assigned by the allocator and
                // Stored comes from a pool holding foreign data. A skipped write would
                // leave another chunk's positions in the slot (grass in the sky), so a
                // culled sub-tuft is written degenerate (Scale 0 → 1e-4 point).
                bool culled = false;
                if (SubPerCell > 1)
                {
                    // The sub-tuft moved away from the cell centre checked by
                    // TryEvaluate: re-check the hard filters (water/sand/mountains/biome)
                    // at its own direction. Single instances are not scattered here —
                    // they don't need the check.
                    double3 subDirection = GroundDecorDistribution.CubeFaceDirection(Face, finalU, finalV);
                    if (!GroundDecorDistribution.IsSurfaceAllowed(Placement, Terrain, subDirection))
                    {
                        culled = true;
                    }
                }

                Vector3 meshPoint = MeshPoint(finalU, finalV);
                float3 meshPoint3 = new float3(meshPoint.x, meshPoint.y, meshPoint.z);

                // Snap guard: the coarse chunk mesh deviates from the analytic
                // height by metres — the point itself must be above water.
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
                    // Birth: time + random offset by (cell, sub-tuft).
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
    /// Carries over from the OLD pool the blades that remain in the new selection
    /// (same cell, same first sub-tufts): their data and BirthTime are preserved.
    /// When the pool is rebuilt, already-grown blades do not sprout again —
    /// only those added by the gradient stay new (with a fresh BirthTime).
    /// Thus local density never drops in a batch, and sprouting stays
    /// one-by-one and smooth.
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
