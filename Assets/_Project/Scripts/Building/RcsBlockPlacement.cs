using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Geometry of an RCS block mounted radially on a tank hull, and the ring
    /// symmetry of the inventory item. The block model (Models/Parts/RCS) has its
    /// origin at the hull contact point, +Z outward and +Y up; its round base is
    /// 0.4 m in radius and its face (with the bolts) is 0.09 m above the origin.
    ///
    /// The block is sunk by the hull curvature under the base plus the base face,
    /// so the round base disappears in the tank of any radius while the nozzles
    /// stay outside. The sink is capped: on a 1 m tank the model does not drown.
    /// </summary>
    public static class RcsBlockPlacement
    {
        /// <summary>Ring sizes the player cycles with X (up) and Z (down).</summary>
        public static readonly int[] Counts = { 1, 2, 3, 4, 6, 8 };

        public const int DefaultCount = 4;

        // Base ring on the hull: its face and the bolt heads are hidden by sinking.
        public const float BaseRadius = 0.4f;
        public const float BaseFace = 0.09f;
        public const float ExtraSink = 0.003f;
        public const float MaxSink = 0.18f;

        /// <summary>Vertical half-extent of the block (side nozzles), m: keeps the ring off the caps.</summary>
        public const float HalfHeight = 0.56f;

        /// <summary>Body centre along the outward axis from the hull contact point, m.</summary>
        public const float BodyCenterOutward = 0.208f;

        /// <summary>Physics body (box) size in the part frame: X tangential, Y up, Z outward.</summary>
        public static readonly Vector3 BodySize = new Vector3(1.12f, 1.12f, 0.724f);

        /// <summary>One nozzle: 450 N, 290 s (the same placeholders as FlightSystemsConfig).</summary>
        public const float NozzleThrustN = 450f;
        public const float NozzleIspSeconds = 290f;
        public const float NozzlePropellantKg = 12f;

        /// <summary>
        /// Five nozzle mouths in the part frame (+Z outward, +Y up, ±X tangential):
        /// the central one at the hub top and four on the arms. Order: +X, −X, +Y, −Y, +Z.
        /// </summary>
        public static readonly Vector3[] NozzleMouths =
        {
            new Vector3(0.56f, 0f, 0.20f),
            new Vector3(-0.56f, 0f, 0.20f),
            new Vector3(0f, 0.56f, 0.20f),
            new Vector3(0f, -0.56f, 0.20f),
            new Vector3(0f, 0f, 0.56f),
        };

        /// <summary>Exhaust directions of the five nozzles in the part frame (outward along the bells).</summary>
        public static readonly Vector3[] NozzleExhausts =
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down,
            Vector3.forward,
        };

        /// <summary>Nearest allowed ring size (any input, e.g. an old preset with 0).</summary>
        public static int ClampCount(int count)
        {
            if (count <= 0)
            {
                return DefaultCount;
            }

            int best = Counts[0];
            int bestDelta = int.MaxValue;
            for (int i = 0; i < Counts.Length; i++)
            {
                int delta = Mathf.Abs(Counts[i] - count);
                if (delta < bestDelta)
                {
                    best = Counts[i];
                    bestDelta = delta;
                }
            }

            return best;
        }

        /// <summary>Next/previous ring size in the cycle (direction +1 or −1).</summary>
        public static int Step(int count, int direction)
        {
            int current = ClampCount(count);
            int index = 0;
            for (int i = 0; i < Counts.Length; i++)
            {
                if (Counts[i] == current)
                {
                    index = i;
                    break;
                }
            }

            index = ((index + (direction > 0 ? 1 : -1)) % Counts.Length + Counts.Length) % Counts.Length;
            return Counts[index];
        }

        /// <summary>Tank generatrix slope (radius change per metre of height).</summary>
        public static float Slope(PartMetrics tank)
        {
            return (tank.TopRadius - tank.Radius) / Mathf.Max(0.01f, tank.Height);
        }

        /// <summary>Tank radius at a height above its bottom, m.</summary>
        public static float RadiusAt(PartMetrics tank, float height)
        {
            return tank.Radius + (Slope(tank) * height);
        }

        /// <summary>Ring height clamped so the block stays between the barrel ends.</summary>
        public static float ClampHeight(PartMetrics tank, float height)
        {
            if (tank.Height <= 2f * HalfHeight)
            {
                return tank.Height * 0.5f;
            }

            return Mathf.Clamp(height, HalfHeight, tank.Height - HalfHeight);
        }

        /// <summary>
        /// Sink depth for a hull of the given radius: the base face plus the sagitta
        /// at the base rim along the local normal. For a cone the circumferential
        /// curvature radius is larger than the horizontal radius (the normal is tilted).
        /// </summary>
        public static float Sink(float radiusAtRing, float slope)
        {
            float cosTilt = 1f / Mathf.Sqrt(1f + (slope * slope));
            float curvatureRadius = radiusAtRing / Mathf.Max(0.05f, cosTilt);

            // Base wider than the curvature radius: no rim contact at all — sink to the cap.
            float sagitta = BaseRadius;
            if (curvatureRadius > BaseRadius)
            {
                sagitta = curvatureRadius - Mathf.Sqrt((curvatureRadius * curvatureRadius) - (BaseRadius * BaseRadius));
            }

            return Mathf.Clamp(BaseFace + sagitta + ExtraSink, 0f, MaxSink);
        }

        /// <summary>
        /// One block's mount pose in SITE axes: pivot is the hull contact point sunk
        /// into the tank, rotation has +Z along the hull normal and +Y along the hull.
        /// </summary>
        public static void Pose(
            PartMetrics tank, Vector3 tankPivot, float height, float angle,
            out Vector3 pivot, out Quaternion rotation)
        {
            float slope = Slope(tank);
            float radiusAtRing = RadiusAt(tank, height);
            float sink = Sink(radiusAtRing, slope);

            var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 normal = new Vector3(radial.x, -slope, radial.z).normalized;
            Vector3 up = (Vector3.up - (Vector3.Dot(Vector3.up, normal) * normal)).normalized;
            Vector3 hull = tankPivot + (radial * radiusAtRing) + (Vector3.up * (tank.Bottom + height));

            pivot = hull - (normal * sink);
            rotation = Quaternion.LookRotation(normal, up);
        }
    }
}
