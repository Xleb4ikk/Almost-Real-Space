using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Galilego.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Galilego.Universe
{
    /// <summary>
    /// Relief vertex in a format that can be uploaded to a Mesh with a single
    /// SetVertexBufferData. Field order = VertexAttribute order in the layout:
    /// Position, Normal, TexCoord1 (f32x3), TexCoord2 (f32x4).
    ///
    /// One interleaved buffer instead of mesh.vertices / normals / SetUVs(1) /
    /// SetUVs(2). Vertices are computed in the job directly in the required
    /// layout, so the main thread does a single memcpy.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TerrainVertex
    {
        public float3 Position;
        public float3 Normal;
        public float3 SurfaceDir;
        public float4 Extra;
    }

    /// <summary>Water surface vertex: position, normal, depth in UV2.
    /// A separate struct because water has no TexCoord1 and a different layout —
    /// a buffer shared with the relief would have shifted it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WaterVertex
    {
        public float3 Position;
        public float3 Normal;
        public float4 Extra;
    }

    /// <summary>Quadtree node identifier for a build request. A separate public
    /// struct because the renderer's nested Node is private, while the job and pool
    /// live in another file. The duplication is trivial, but they can't be linked
    /// directly without exposing Node.</summary>
    public struct ChunkNodePlan
    {
        public int Face;
        public int Depth;
        public int Ix;
        public int Iy;
    }

    /// <summary>
    /// Request for an asynchronous chunk build. Array ownership belongs to the pool:
    /// they are Persistent, reused and returned in Finalize or on cancel. A leak of
    /// even one such array per cancelled request shows up only in Unity's native
    /// memory report, so cancellation is done by a single function called from everywhere.
    /// </summary>
    public sealed class ChunkBuildRequest
    {
        public ChunkNodePlan Plan;
        public int Res;
        public int N;
        public int Grid;
        public int CoreCount;
        public int TotalVerts;
        public int IndexCount;
        public float Priority;
        public int ParamsVersion;
        public JobHandle Handle;

        public NativeArray<double3> Dirs;
        public NativeArray<double> Heights;
        public NativeArray<float> Masks;
        public NativeArray<float> Details;
        public NativeArray<float3> Positions;
        public NativeArray<TerrainVertex> Vertices;
        public NativeArray<WaterVertex> WaterVertices;
        /// <summary>1 = the chunk has water, 0 = no. One element: the write from the
        /// job must be visible on the main thread.</summary>
        public NativeArray<int> WaterFlags;
        public NativeArray<float3> BoundsMinMax;

        /// <summary>Node centre in body-fixed double. Needed at finalize: the mesh is
        /// built relative to the chunk centre, and the transform is set after upload.</summary>
        public Vector3d CenterAstro;

        /// <summary>Relief skirt depth — used to expand the mesh bounds.</summary>
        public double SkirtDepth;

        public bool HasWater => WaterFlags.Length > 0 && WaterFlags[0] != 0;
    }

    /// <summary>
    /// Burst-compatible copy of CubeSphere.Direction.
    ///
    /// The duplication is intentional and is the only place where "just call the
    /// existing method" is impossible: Vector3d.Normalized lives in Core and is not
    /// Burst-accessible. The formula is ported one-to-one — same axis permutation,
    /// same division by Math.Sqrt(x²+y²+z²), same operation order — otherwise
    /// heights would drift by the last bit and seams between chunks would split.
    /// </summary>
    public static class CubeSphereBurst
    {
        public static double3 Direction(int face, double u, double v)
        {
            double a = (u * 2d) - 1d;
            double b = (v * 2d) - 1d;
            double x;
            double y;
            double z;
            switch (face)
            {
                case 0: x = 1d; y = b; z = -a; break;
                case 1: x = -1d; y = b; z = a; break;
                case 2: x = a; y = 1d; z = -b; break;
                case 3: x = a; y = -1d; z = b; break;
                case 4: x = a; y = b; z = 1d; break;
                default: x = -a; y = b; z = -1d; break;
            }

            double length = math.sqrt((x * x) + (y * y) + (z * z));
            if (length > 0d)
            {
                return new double3(x / length, y / length, z / length);
            }

            return double3.zero;
        }
    }

    /// <summary>Chain step 1: cube-sphere directions for the whole grid with halo.</summary>
    [BurstCompile]
    public struct FillDirectionsJob : IJobParallelFor
    {
        public int Face;
        public int Grid;
        public double U0;
        public double V0;
        public double StepUv;

        [WriteOnly]
        public NativeArray<double3> Dirs;

        public void Execute(int index)
        {
            int gi = index / Grid;
            int gj = index - (gi * Grid);
            double u = U0 + ((gi - 1) * StepUv);
            double v = V0 + ((gj - 1) * StepUv);
            Dirs[index] = CubeSphereBurst.Direction(Face, u, v);
        }
    }

    /// <summary>
    /// Chain step 3: assembles relief and water vertices from directions, heights,
    /// masks and details. Normals by central differences, flip relative to the
    /// radial, skirts downward, bounds.
    ///
    /// All computation is in float3/float4 in the same operation order as the
    /// managed version, so results match: Vector3.Cross = math.cross,
    /// Vector3.magnitude = math.length, division by length is division, not
    /// multiplication by the reciprocal.
    /// </summary>
    [BurstCompile]
    public struct AssembleChunkMeshJob : IJob
    {
        [ReadOnly] public NativeArray<double3> Dirs;
        [ReadOnly] public NativeArray<double> Heights;
        [ReadOnly] public NativeArray<float> Masks;
        [ReadOnly] public NativeArray<float> Details;

        public int Res;
        public int N;
        public int Grid;
        public int CoreCount;
        public int TotalVerts;
        public double Amplitude;
        public double Radius;
        public double SeaLevel;
        public double SkirtDepth;
        public double WaterSkirtDepth;
        public double3 CenterAstro;

        /// <summary>Intermediate grid positions with halo. Without [WriteOnly]:
        /// the job writes them in the first pass and reads them in the second (normals
        /// by central differences).</summary>
        public NativeArray<float3> Positions;

        /// <summary>Finished relief vertices. Also written then read: the core pass
        /// fills them, and the skirt pass takes edge vertices from them. A WriteOnly-marked
        /// array cannot be read, and Burst fails the job with InvalidOperationException
        /// instead of silently corrupting data.</summary>
        public NativeArray<TerrainVertex> Vertices;

        /// <summary>Water vertices. Read twice: the "is there water" scan and the
        /// skirt pass. Hence no [WriteOnly], for the same reason as the other two.</summary>
        public NativeArray<WaterVertex> WaterVertices;
        [WriteOnly] public NativeArray<int> WaterFlags;
        public NativeArray<float3> BoundsMinMax;

        /// <summary>Normalization exactly like Vector3.normalized: with the threshold
        /// kEpsilon = 1e-5 and by division, not multiplication by the inverse length.
        /// math.normalizesafe also divides but has no threshold, and in practice this is
        /// the only place where the last bit could drift.</summary>
        private static float3 NormalizeLikeUnity(float3 v)
        {
            float mag = math.length(v);
            return mag > 1e-5f ? v / mag : float3.zero;
        }

        public void Execute()
        {
            int gridCount = Grid * Grid;

            // Positions in double, then in float. Order matters: double at the planet
            // radius (~1.14e6) gives ulp ~0.1 m, and any reordering of operations shifts
            // the vertex by centimetres.
            for (int gi = 0; gi < Grid; gi++)
            {
                for (int gj = 0; gj < Grid; gj++)
                {
                    int vi = (gi * Grid) + gj;
                    double3 d = Dirs[vi];
                    double rawHeight = Heights[vi] * Amplitude;
                    double3 absAstro = new double3(d.x, d.y, d.z) * (Radius + rawHeight);
                    double3 rel = absAstro - CenterAstro;
                    Positions[vi] = new float3((float)rel.x, (float)rel.z, (float)-rel.y);
                }
            }

            float3 centerUnity = new float3(
                (float)CenterAstro.x, (float)CenterAstro.z, (float)-CenterAstro.y);

            bool hasWater = SeaLevel > -1e29d;

            for (int row = 0; row < N; row++)
            {
                for (int col = 0; col < N; col++)
                {
                    int halo = ((row + 1) * Grid) + (col + 1);
                    int index = (row * N) + col;
                    float3 position = Positions[halo];

                    float3 dCol = Positions[halo + 1] - Positions[halo - 1];
                    float3 dRow = Positions[halo + Grid] - Positions[halo - Grid];
                    float3 normal = math.cross(dCol, dRow);
                    float normalLength = math.length(normal);
                    float3 radial = NormalizeLikeUnity(position + centerUnity);
                    float3 unit = normalLength > 1e-10f ? normal / normalLength : radial;
                    if (math.dot(unit, radial) < 0f)
                    {
                        unit = -unit;
                    }

                    double3 d = Dirs[halo];
                    double rawHeight = Heights[halo] * Amplitude;

                    Vertices[index] = new TerrainVertex
                    {
                        Position = position,
                        Normal = unit,
                        SurfaceDir = new float3((float)d.x, (float)d.y, (float)d.z),
                        Extra = new float4(
                            (float)rawHeight, math.dot(unit, radial), Masks[halo], Details[halo])
                    };

                    if (hasWater)
                    {
                        double3 absWater = new double3(d.x, d.y, d.z) * (Radius + SeaLevel);
                        double3 wrel = absWater - CenterAstro;
                        float3 wpos = new float3((float)wrel.x, (float)wrel.z, (float)-wrel.y);
                        WaterVertices[index] = new WaterVertex
                        {
                            Position = wpos,
                            Normal = NormalizeLikeUnity(wpos + centerUnity),
                            Extra = new float4((float)math.max(0d, SeaLevel - rawHeight), 0f, 0f, 0f)
                        };
                    }
                }
            }

            // The chunk is entirely above the sea — no water is built.
            if (hasWater)
            {
                hasWater = false;
                for (int k = 0; k < CoreCount; k++)
                {
                    if (WaterVertices[k].Extra.x > 0f)
                    {
                        hasWater = true;
                        break;
                    }
                }
            }

            WaterFlags[0] = hasWater ? 1 : 0;

            // Relief skirts: duplicate the edges, sink them toward the planet centre.
            for (int edge = 0; edge < 4; edge++)
            {
                int baseIndex = CoreCount + (edge * N);
                for (int k = 0; k < N; k++)
                {
                    int coreIndex = EdgeCoreIndex(Res, N, edge, k);
                    TerrainVertex core = Vertices[coreIndex];
                    float3 inward = NormalizeLikeUnity(core.Position + centerUnity);
                    Vertices[baseIndex + k] = new TerrainVertex
                    {
                        Position = core.Position - (inward * (float)SkirtDepth),
                        Normal = core.Normal,
                        SurfaceDir = core.SurfaceDir,
                        Extra = core.Extra
                    };

                    if (hasWater)
                    {
                        WaterVertex wcore = WaterVertices[coreIndex];
                        float3 winward = NormalizeLikeUnity(wcore.Position + centerUnity);
                        WaterVertices[baseIndex + k] = new WaterVertex
                        {
                            Position = wcore.Position - (winward * (float)WaterSkirtDepth),
                            Normal = wcore.Normal,
                            Extra = wcore.Extra
                        };
                    }
                }
            }

            BoundsMinMax[0] = float3.zero;
            BoundsMinMax[1] = float3.zero;
            if (hasWater)
            {
                AccumulateBounds(WaterVertices, TotalVerts);
            }
            else
            {
                AccumulateBounds(Vertices, TotalVerts);
            }
        }

        private void AccumulateBounds(NativeArray<TerrainVertex> source, int count)
        {
            float3 min = source[0].Position;
            float3 max = source[0].Position;
            for (int i = 1; i < count; i++)
            {
                float3 p = source[i].Position;
                min = math.min(min, p);
                max = math.max(max, p);
            }

            BoundsMinMax[0] = min;
            BoundsMinMax[1] = max;
        }

        private void AccumulateBounds(NativeArray<WaterVertex> source, int count)
        {
            float3 min = source[0].Position;
            float3 max = source[0].Position;
            for (int i = 1; i < count; i++)
            {
                float3 p = source[i].Position;
                min = math.min(min, p);
                max = math.max(max, p);
            }

            BoundsMinMax[0] = min;
            BoundsMinMax[1] = max;
        }

        /// <summary>Index of the core vertex on a skirt edge. The edge order (row0,
        /// rowRes, col0, colRes) must match the order in the indices, otherwise the
        /// skirt glues to the wrong side.</summary>
        private static int EdgeCoreIndex(int res, int n, int edge, int k)
        {
            switch (edge)
            {
                case 0: return k;
                case 1: return (res * n) + k;
                case 2: return k * n;
                default: return (k * n) + res;
            }
        }
    }

    /// <summary>
    /// Pool of Persistent arrays and shared index buffers.
    ///
    /// Chunk topology depends only on res, not position, so indices are built ONCE
    /// per res and copied into the mesh. Chunk vertex count is always below 65535,
    /// so ushort suffices.
    /// </summary>
    public static class SurfaceChunkPool
    {
        private static readonly Dictionary<int, NativeArray<ushort>> sharedIndices =
            new Dictionary<int, NativeArray<ushort>>();

        private static readonly List<ChunkBuildRequest> idle = new List<ChunkBuildRequest>();

        /// <summary>Indices for res: core grid plus four skirts, 6 indices per quad.
        /// Cached; must not be called from a job.</summary>
        public static NativeArray<ushort> GetSharedIndices(int res)
        {
            if (sharedIndices.TryGetValue(res, out NativeArray<ushort> cached) && cached.IsCreated)
            {
                return cached;
            }

            int n = res + 1;
            int coreCount = n * n;

            // 6 indices PER QUAD, not per triangle: each quad is written as two
            // triangles (a,c,b + b,c,d), i.e. exactly 6.
            int quadCount = ((n - 1) * (n - 1)) + (4 * (n - 1));
            int indexCount = quadCount * 6;

            // Bounds are checked BEFORE writing, not after. Checking at the end is
            // useless: by then the buffer is already overflowed and NativeArray throws
            // before control reaches it. The cost is one comparison, but a topology error
            // can no longer slip silently into VRAM.
            int expectedCore = (n - 1) * (n - 1) * 6;
            int expectedSkirts = 4 * (n - 1) * 6;
            if (indexCount != expectedCore + expectedSkirts || indexCount <= 0)
            {
                throw new InvalidOperationException(
                    "SurfaceChunkPool: topology mismatch for res=" + res
                    + " indexCount=" + indexCount
                    + " core=" + expectedCore + " skirts=" + expectedSkirts);
            }

            var indices = new NativeArray<ushort>(indexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            int t = 0;
            for (int row = 0; row < n - 1; row++)
            {
                for (int col = 0; col < n - 1; col++)
                {
                    int a = (row * n) + col;
                    int b = a + 1;
                    int c = a + n;
                    int d = c + 1;
                    indices[t++] = (ushort)a;
                    indices[t++] = (ushort)c;
                    indices[t++] = (ushort)b;
                    indices[t++] = (ushort)b;
                    indices[t++] = (ushort)c;
                    indices[t++] = (ushort)d;
                }
            }

            for (int edge = 0; edge < 4; edge++)
            {
                int baseIndex = coreCount + (edge * n);
                for (int k = 0; k < n - 1; k++)
                {
                    int a;
                    int b;
                    switch (edge)
                    {
                        case 0: a = k; b = k + 1; break;
                        case 1: a = (res * n) + k; b = a + 1; break;
                        case 2: a = k * n; b = a + n; break;
                        default: a = (k * n) + res; b = a + n; break;
                    }

                    int c = baseIndex + k;
                    int d = baseIndex + k + 1;
                    indices[t++] = (ushort)a;
                    indices[t++] = (ushort)c;
                    indices[t++] = (ushort)b;
                    indices[t++] = (ushort)b;
                    indices[t++] = (ushort)c;
                    indices[t++] = (ushort)d;
                }
            }

            if (t != indexCount)
            {
                // Should not be reachable: the arithmetic was checked BEFORE writing.
                // This only verifies the loops did not diverge from it — the buffer is
                // intact at this point, so releasing it is safe.
                indices.Dispose();
                throw new InvalidOperationException(
                    "SurfaceChunkPool: wrote " + t + " indices, expected " + indexCount
                    + " for res=" + res);
            }

            sharedIndices[res] = indices;
            return indices;
        }

        /// <summary>Indices in the chunk mesh at this res. 6 indices per quad: two
        /// triangles, a,c,b + b,c,d.</summary>
        public static int IndexCountFor(int res)
        {
            int n = res + 1;
            return (((n - 1) * (n - 1)) + (4 * (n - 1))) * 6;
        }

        public static ChunkBuildRequest Rent()
        {
            while (idle.Count > 0)
            {
                int last = idle.Count - 1;
                ChunkBuildRequest req = idle[last];
                idle.RemoveAt(last);
                if (!req.Dirs.IsCreated)
                {
                    continue;
                }

                return req;
            }

            return new ChunkBuildRequest();
        }

        /// <summary>Prepares the request arrays for a specific chunk size. Reallocation
        /// only when the pool is empty or demand grew: in practice they are almost always
        /// reused, because the same res values alternate in flight.</summary>
        public static void Prepare(ChunkBuildRequest req, int res, int n, int grid, int totalVerts)
        {
            int gridCount = grid * grid;
            req.Res = res;
            req.N = n;
            req.Grid = grid;
            req.CoreCount = n * n;
            req.TotalVerts = totalVerts;
            req.IndexCount = IndexCountFor(res);

            EnsureDouble3(ref req.Dirs, gridCount);
            EnsureDouble(ref req.Heights, gridCount);
            EnsureFloat(ref req.Masks, gridCount);
            EnsureFloat(ref req.Details, gridCount);
            EnsureFloat3(ref req.Positions, gridCount);
            EnsureTerrain(ref req.Vertices, totalVerts);
            EnsureWater(ref req.WaterVertices, totalVerts);
            EnsureInt(ref req.WaterFlags, 1);
            EnsureFloat3(ref req.BoundsMinMax, 2);
        }

        public static void Return(ChunkBuildRequest req)
        {
            if (req == null)
            {
                return;
            }

            idle.Add(req);
        }

        /// <summary>Full reset on teardown: Persistent arrays must not survive between
        /// play sessions, otherwise Unity reports a native memory leak on exit.</summary>
        public static void ClearAll()
        {
            for (int i = 0; i < idle.Count; i++)
            {
                DisposeRequest(idle[i]);
            }

            idle.Clear();

            foreach (KeyValuePair<int, NativeArray<ushort>> kv in sharedIndices)
            {
                if (kv.Value.IsCreated)
                {
                    kv.Value.Dispose();
                }
            }

            sharedIndices.Clear();
        }

        public static void DisposeRequest(ChunkBuildRequest req)
        {
            if (req == null)
            {
                return;
            }

            DisposeIfCreated(ref req.Dirs);
            DisposeIfCreated(ref req.Heights);
            DisposeIfCreated(ref req.Masks);
            DisposeIfCreated(ref req.Details);
            DisposeIfCreated(ref req.Positions);
            DisposeIfCreated(ref req.Vertices);
            DisposeIfCreated(ref req.WaterVertices);
            DisposeIfCreated(ref req.WaterFlags);
            DisposeIfCreated(ref req.BoundsMinMax);
        }

        private static void DisposeIfCreated<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
                array = default;
            }
        }

        private static void EnsureDouble3(ref NativeArray<double3> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<double3>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureDouble(ref NativeArray<double> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<double>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureFloat(ref NativeArray<float> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<float>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureInt(ref NativeArray<int> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<int>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureFloat3(ref NativeArray<float3> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<float3>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureTerrain(ref NativeArray<TerrainVertex> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<TerrainVertex>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureWater(ref NativeArray<WaterVertex> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<WaterVertex>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }
    }
}
