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

        private static bool Same(DecorExclusionData x, DecorExclusionData y)
        {
            return x.Center.Equals(y.Center) && x.AxisX.Equals(y.AxisX) && x.AxisZ.Equals(y.AxisZ)
                && x.HalfX == y.HalfX && x.HalfZ == y.HalfZ
                && x.SoftHalfX == y.SoftHalfX && x.SoftHalfZ == y.SoftHalfZ;
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
