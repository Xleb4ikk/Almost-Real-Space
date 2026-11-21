using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Galilego.Universe
{
    // Pure core of the decor zone table: no Unity renderer, Time or scene.
    // Included by both the test stand (Tools/P1bTests) and the game. The assembly
    // and deferred-release implementation is in DecorExclusion.cs (same partial class).
    /// <summary>
    /// Zone where decor (trees, grass, rocks) does not grow: a rotated rectangle
    /// on the surface. Blittable only — read from Burst jobs.
    /// Centre and axes are unit body-fixed vectors, half-sizes are angular (m / R).
    /// </summary>
    public struct DecorExclusionData
    {
        public double3 Center;
        public double3 AxisX;
        public double3 AxisZ;
        public double HalfX;
        public double HalfZ;

        /// <summary>Smaller rectangle (soft margin, grass): HalfX/HalfZ plus a narrow road zone.</summary>
        public double SoftHalfX;
        public double SoftHalfZ;
    }

    public static partial class DecorExclusionTable
    {
        public static readonly NativeArray<DecorExclusionData> Empty =
            new NativeArray<DecorExclusionData>(0, Allocator.Persistent);

        // ---------- query (Burst) ----------

        public static bool IsExcluded(NativeArray<DecorExclusionData> zones, double3 direction)
        {
            if (!zones.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                DecorExclusionData z = zones[i];
                if (math.dot(direction, z.Center) < 0.5d)
                {
                    continue;
                }

                double3 d = direction - z.Center;
                if (math.abs(math.dot(d, z.AxisX)) <= z.HalfX && math.abs(math.dot(d, z.AxisZ)) <= z.HalfZ)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Query against the small rectangle — grass at the road edge (0.3 m margin).</summary>
        public static bool IsExcludedSoft(NativeArray<DecorExclusionData> zones, double3 direction)
        {
            if (!zones.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                DecorExclusionData z = zones[i];
                if (math.dot(direction, z.Center) < 0.5d)
                {
                    continue;
                }

                double3 d = direction - z.Center;
                if (math.abs(math.dot(d, z.AxisX)) <= z.SoftHalfX && math.abs(math.dot(d, z.AxisZ)) <= z.SoftHalfZ)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TableEqual(NativeArray<DecorExclusionData> a, NativeArray<DecorExclusionData> b)
        {
            int na = a.IsCreated ? a.Length : 0;
            int nb = b.IsCreated ? b.Length : 0;
            if (na != nb)
            {
                return false;
            }

            for (int i = 0; i < na; i++)
            {
                if (!Same(a[i], b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Tolerance of the table comparison, in direction-space (radians of arc) and in
        /// normalized half-sizes (m/R). 1e-6 is ~1 m on a 1000 km body.
        ///
        /// An EXACT comparison is not usable here. The zone centre is quantized to 0.25 m
        /// (see Q in DecorExclusion.cs) from a site pose that is recomputed every frame
        /// while the planet rotates: when the raw value sits on a rounding boundary, the
        /// last float bit flips the result by one whole 0.25 m step. With exact equality
        /// one zone flipped about once per half-second scan, so ParamsEqual in
        /// PlanetSurfaceRenderer went false, the chunk cache was cleared and the whole
        /// terrain (≈900 chunks) was rebuilt synchronously — a 2 s freeze every few
        /// seconds, seen at the pole and anywhere else while standing still.
        /// A metre-scale change is far below what decor exclusion needs to notice.
        /// </summary>
        private const double TableTolerance = 1e-6d;

        private static bool Same(DecorExclusionData x, DecorExclusionData y)
        {
            return Near(x.Center, y.Center) && Near(x.AxisX, y.AxisX) && Near(x.AxisZ, y.AxisZ)
                && Math.Abs(x.HalfX - y.HalfX) <= TableTolerance
                && Math.Abs(x.HalfZ - y.HalfZ) <= TableTolerance
                && Math.Abs(x.SoftHalfX - y.SoftHalfX) <= TableTolerance
                && Math.Abs(x.SoftHalfZ - y.SoftHalfZ) <= TableTolerance;
        }

        private static bool Near(double3 a, double3 b)
        {
            return math.lengthsq(a - b) <= TableTolerance * TableTolerance;
        }

        static partial void AfterRelease();   // implemented in DecorExclusion.cs (game), empty in the test stand

        public static void Release(ref NativeArray<DecorExclusionData> table)
        {
            if (table.IsCreated && table.Length > 0)
            {
                table.Dispose();
            }

            table = Empty;
            AfterRelease();
        }
    }
}
