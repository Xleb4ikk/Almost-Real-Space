using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Procedural stage separator: a solid ribbed disc with no central hole, 40 mm
    /// thick, close to a real launch-vehicle separator — an outer bolted flange with
    /// a pyrotechnic ring, two intermediate frames, 12 radial stringers and a raised
    /// hub. Axis is local Y, origin at mid-height. Radial features scale with the
    /// player-set radius, thickness is constant. UVs are metric (texture tile =
    /// segment-seam spacing, ~250 mm), so seams keep their scale at any radius.
    /// Edges are hard: wall and face vertices are separate.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralSeparator : MonoBehaviour
    {
        [Header("Геометрия (параметры игрока)")]
        [Tooltip("Наружный радиус отделителя, м.")]
        [Min(0.1f)] public float Radius = 1.5f;

        [Tooltip("Сегментов по окружности. Больше — глаже силуэт.")]
        [Range(16, 160)] public int Segments = 64;

        [Header("Материал")]
        [Tooltip("Анодированный чёрный металл с рисунком сегментных швов.")]
        public Material Material;

        [Tooltip("Метров поверхности на тайл текстуры: задаёт шаг сегментных швов (~250 мм).")]
        [Min(0.05f)] public float TextureMetersPerTile = 0.25f;

        /// <summary>Separator thickness (height along Y), constant 40 mm.</summary>
        public const float Thickness = 0.04f;

        // ─── Layout ─────────────────────────────────────────────────────────
        // Radial fractions of Radius (taken from the redesign: 3 m disc, 40 mm).
        private const float HubFraction = 0.1467f;
        private const float Ring3InnerFraction = 0.5333f;
        private const float Ring3OuterFraction = 0.5667f;
        private const float Ring2InnerFraction = 0.7333f;
        private const float Ring2OuterFraction = 0.7733f;
        private const float PyroInnerFraction = 0.8567f;
        private const float PyroOuterFraction = 0.8700f;
        private const float FrameInnerFraction = 0.9067f;
        private const float RibInnerWidthFraction = 0.0213f;
        private const float RibOuterWidthFraction = 0.0333f;
        private const float BoltRadialFraction = 0.9533f;
        private const float BoltHeadFraction = 0.0090f;
        private const int RibCount = 12;

        // Heights as fractions of the thickness; y0 = -0.5 T, y1 = +0.5 T.
        private const float MembraneTop = -0.25f;
        private const float RibTop = 0.15f;
        private const float Ring3Top = 0.10f;
        private const float Ring2Top = 0.20f;
        private const float HubTop = 0.30f;
        private const float FrameTop = 0.35f;

        private Mesh mesh;

        /// <summary>Number of flange bolts for a radius (one every ~330 mm of circumference).</summary>
        public static int BoltCountFor(float radius)
        {
            float circumference = 2f * Mathf.PI * Mathf.Max(0.01f, radius) * BoltRadialFraction;
            return Mathf.Clamp(Mathf.RoundToInt(circumference / 0.33f), 8, 48);
        }

        /// <summary>
        /// Steel volume of the separator, m³ — the analytic volume of the same
        /// features the mesh is built from (membrane, frame, pyro ring, two frames,
        /// hub, stringers), minus the overlaps where the frame and the stringers
        /// pass through the membrane and the frames. Mass = volume × steel density.
        /// </summary>
        public static double MetalVolume(float radius)
        {
            double r = Mathf.Max(0.01f, radius);
            double t = Thickness;
            double r2 = r * r;

            double volume = System.Math.PI * r2 * 0.25 * t;                                  // membrane
            volume += System.Math.PI * (r2 - Sq(r * FrameInnerFraction)) * 0.85 * t;          // outer frame
            volume += System.Math.PI * (Sq(r * PyroOuterFraction) - Sq(r * PyroInnerFraction)) * 0.65 * t;
            volume += System.Math.PI * (Sq(r * Ring2OuterFraction) - Sq(r * Ring2InnerFraction)) * 0.45 * t;
            volume += System.Math.PI * (Sq(r * Ring3OuterFraction) - Sq(r * Ring3InnerFraction)) * 0.35 * t;
            volume += System.Math.PI * Sq(r * HubFraction) * 0.55 * t;
            volume += RibCount * (r * FrameInnerFraction - r * HubFraction)
                      * (r * (RibInnerWidthFraction + RibOuterWidthFraction) * 0.5)
                      * 0.40 * t;

            // Overlaps (the mesh is a union of solids that interpenetrate).
            volume -= System.Math.PI * (r2 - Sq(r * FrameInnerFraction)) * 0.25 * t;          // frame in membrane
            volume -= RibBandOverlap(r, Ring3InnerFraction, Ring3OuterFraction);
            volume -= RibBandOverlap(r, Ring2InnerFraction, Ring2OuterFraction);
            volume -= RibBandOverlap(r, PyroInnerFraction, PyroOuterFraction);
            return volume;
        }

        /// <summary>Volume where the radial stringers pass through one ring band.</summary>
        private static double RibBandOverlap(double radius, float innerFraction, float outerFraction)
        {
            double r0 = radius * HubFraction;
            double r1 = radius * FrameInnerFraction;
            double mid = radius * 0.5 * (innerFraction + outerFraction);
            double k = Mathf.Clamp01((float)((mid - r0) / (r1 - r0)));
            double width = radius * (RibInnerWidthFraction + ((RibOuterWidthFraction - RibInnerWidthFraction) * k));
            return RibCount * (radius * (outerFraction - innerFraction)) * width * 0.40 * Thickness;
        }

        /// <summary>Separator mesh: solid disc, one submesh.</summary>
        public static Mesh BuildSeparator(float radius, float thickness, int segments, float metersPerTile)
        {
            var result = new Mesh();
            FillSeparator(result, radius, thickness, segments, metersPerTile);
            return result;
        }

        /// <summary>
        /// Fill an EXISTING mesh. The mesh is reused, not recreated: AddComponent
        /// immediately calls OnEnable (build with default sizes), then the UI sets the
        /// radius and requests Rebuild — destroying the mesh under the GPU Resident
        /// Drawer (HDRP) gives "invalid Batch/Mesh ID".
        /// </summary>
        public static void FillSeparator(Mesh target, float radius, float thickness, int segments, float metersPerTile)
        {
            radius = Mathf.Max(0.01f, radius);
            thickness = Mathf.Max(0.005f, thickness);
            segments = Mathf.Max(8, segments);

            float t = thickness;
            float y0 = -0.5f * t;
            float y1 = 0.5f * t;
            var builder = new Builder(segments, metersPerTile);

            // Shell: solid membrane, no central hole.
            builder.Disc(radius, y0, y0 + (0.25f * t));
            // Outer bolted frame, full thickness.
            builder.Ring(radius * FrameInnerFraction, radius, y0, y0 + (0.85f * t));
            // Pyrotechnic (cut) ring just inside the frame.
            builder.Ring(radius * PyroInnerFraction, radius * PyroOuterFraction, y0 + (0.25f * t), y0 + (0.90f * t));
            // Two intermediate frames.
            builder.Ring(radius * Ring2InnerFraction, radius * Ring2OuterFraction, y0 + (0.25f * t), y0 + (0.70f * t));
            builder.Ring(radius * Ring3InnerFraction, radius * Ring3OuterFraction, y0 + (0.25f * t), y0 + (0.60f * t));
            // Raised hub.
            builder.Disc(radius * HubFraction, y0 + (0.25f * t), y0 + (0.80f * t));

            // Radial stringers.
            for (int i = 0; i < RibCount; i++)
            {
                float angle = 2f * Mathf.PI * i / RibCount;
                builder.Rib(angle, radius * HubFraction, radius * FrameInnerFraction,
                    radius * RibInnerWidthFraction, radius * RibOuterWidthFraction,
                    y0 + (0.25f * t), y0 + (0.65f * t));
            }

            // Flange bolts (hex heads).
            int bolts = BoltCountFor(radius);
            float head = Mathf.Clamp(radius * BoltHeadFraction, 0.004f, 0.02f);
            for (int i = 0; i < bolts; i++)
            {
                float angle = 2f * Mathf.PI * i / bolts;
                builder.Bolt(
                    Mathf.Cos(angle) * radius * BoltRadialFraction,
                    Mathf.Sin(angle) * radius * BoltRadialFraction,
                    head, y0 + (0.85f * t), y1);
            }

            target.Clear();
            target.name = "Separator_" + radius.ToString("F2");
            target.SetVertices(builder.Vertices);
            target.SetNormals(builder.Normals);
            target.SetUVs(0, builder.Uvs);
            target.SetTriangles(builder.Triangles, 0);
            target.RecalculateBounds();
        }

        private static double Sq(double value)
        {
            return value * value;
        }

        // ─── Mesh construction ──────────────────────────────────────────────

        private sealed class Builder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            private readonly int segments;
            private readonly float tile;

            public Builder(int segments, float metersPerTile)
            {
                this.segments = segments;
                tile = Mathf.Max(0.05f, metersPerTile);
            }

            public int Add(Vector3 position, Vector3 normal, Vector2 uv)
            {
                Vertices.Add(position);
                Normals.Add(normal);
                Uvs.Add(uv);
                return Vertices.Count - 1;
            }

            private Vector2 CubeUv(Vector3 position, Vector3 normal)
            {
                float ax = Mathf.Abs(normal.x);
                float ay = Mathf.Abs(normal.y);
                float az = Mathf.Abs(normal.z);
                if (ay >= ax && ay >= az)
                {
                    return new Vector2(position.x / tile, position.z / tile);
                }

                if (ax >= az)
                {
                    return new Vector2(position.z / tile, position.y / tile);
                }

                return new Vector2(position.x / tile, position.y / tile);
            }

            private void Tri(int a, int b, int c)
            {
                Triangles.Add(a);
                Triangles.Add(b);
                Triangles.Add(c);
            }

            private Vector2 Planar(float x, float z)
            {
                return new Vector2(x / tile, z / tile);
            }

            /// <summary>Ring of wall vertices (smooth radial normal).</summary>
            public int[] WallLoop(float radius, float y, bool outside, float v)
            {
                var loop = new int[segments];
                int tiles = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.PI * radius / tile));
                for (int i = 0; i < segments; i++)
                {
                    float angle = 2f * Mathf.PI * i / segments;
                    float cs = Mathf.Cos(angle);
                    float sn = Mathf.Sin(angle);
                    var normal = new Vector3(cs, 0f, sn) * (outside ? 1f : -1f);
                    loop[i] = Add(new Vector3(radius * cs, y, radius * sn), normal,
                        new Vector2((i / (float)segments) * tiles, v));
                }

                return loop;
            }

            /// <summary>Ring of flat-face vertices (planar metric UV, hard normal).</summary>
            public int[] FlatLoop(float radius, float y, bool up)
            {
                var loop = new int[segments];
                Vector3 normal = up ? Vector3.up : Vector3.down;
                for (int i = 0; i < segments; i++)
                {
                    float angle = 2f * Mathf.PI * i / segments;
                    float x = radius * Mathf.Cos(angle);
                    float z = radius * Mathf.Sin(angle);
                    loop[i] = Add(new Vector3(x, y, z), normal, Planar(x, z));
                }

                return loop;
            }

            public void Wall(int[] bottom, int[] top, bool outside)
            {
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    if (outside)
                    {
                        Tri(bottom[i], top[i], top[j]);
                        Tri(bottom[i], top[j], bottom[j]);
                    }
                    else
                    {
                        Tri(bottom[i], top[j], top[i]);
                        Tri(bottom[i], bottom[j], top[j]);
                    }
                }
            }

            public void Annulus(int[] outer, int[] inner, bool up)
            {
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    if (up)
                    {
                        Tri(outer[i], inner[i], inner[j]);
                        Tri(outer[i], inner[j], outer[j]);
                    }
                    else
                    {
                        Tri(outer[i], inner[j], inner[i]);
                        Tri(outer[i], outer[j], inner[j]);
                    }
                }
            }

            public void Cap(int[] loop, float y, bool up)
            {
                int center = Add(new Vector3(0f, y, 0f), up ? Vector3.up : Vector3.down, Planar(0f, 0f));
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    if (up)
                    {
                        Tri(center, loop[j], loop[i]);
                    }
                    else
                    {
                        Tri(center, loop[i], loop[j]);
                    }
                }
            }

            /// <summary>Solid disc: two wall loops plus fan caps.</summary>
            public void Disc(float radius, float y0, float y1)
            {
                var wb = WallLoop(radius, y0, true, y0 / tile);
                var wt = WallLoop(radius, y1, true, y1 / tile);
                Wall(wb, wt, true);

                var bb = FlatLoop(radius, y0, false);
                var tt = FlatLoop(radius, y1, true);
                Cap(bb, y0, false);
                Cap(tt, y1, true);
            }

            /// <summary>Annular frame: outer and inner barrels plus two flat faces.</summary>
            public void Ring(float rIn, float rOut, float y0, float y1)
            {
                var ob = WallLoop(rOut, y0, true, y0 / tile);
                var ot = WallLoop(rOut, y1, true, y1 / tile);
                var ib = WallLoop(rIn, y0, false, y0 / tile);
                var it = WallLoop(rIn, y1, false, y1 / tile);
                Wall(ob, ot, true);
                Wall(ib, it, false);

                var to = FlatLoop(rOut, y1, true);
                var ti = FlatLoop(rIn, y1, true);
                Annulus(to, ti, true);

                var bo = FlatLoop(rOut, y0, false);
                var bi = FlatLoop(rIn, y0, false);
                Annulus(bo, bi, false);
            }

            /// <summary>Tapered radial stringer (box with flat faces).</summary>
            public void Rib(float angle, float r0, float r1, float w0, float w1, float y0, float y1)
            {
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var perp = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                Vector3 a0 = (dir * r0) - (perp * (w0 * 0.5f));
                Vector3 b0 = (dir * r0) + (perp * (w0 * 0.5f));
                Vector3 a1 = (dir * r1) - (perp * (w1 * 0.5f));
                Vector3 b1 = (dir * r1) + (perp * (w1 * 0.5f));

                Quad(Lift(a0, y1), Lift(b0, y1), Lift(b1, y1), Lift(a1, y1), Vector3.up);
                Quad(Lift(a0, y0), Lift(a1, y0), Lift(b1, y0), Lift(b0, y0), Vector3.down);
                Quad(Lift(a0, y0), Lift(a0, y1), Lift(a1, y1), Lift(a1, y0), -perp);
                Quad(Lift(b0, y0), Lift(b1, y0), Lift(b1, y1), Lift(b0, y1), perp);
                Quad(Lift(a0, y0), Lift(b0, y0), Lift(b0, y1), Lift(a0, y1), -dir);
                Quad(Lift(a1, y0), Lift(a1, y1), Lift(b1, y1), Lift(b1, y0), dir);
            }

            private static Vector3 Lift(Vector3 point, float y)
            {
                point.y = y;
                return point;
            }

            /// <summary>Hex bolt head: flat side faces plus two caps.</summary>
            public void Bolt(float cx, float cz, float radius, float y0, float y1)
            {
                const int sides = 6;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = 2f * Mathf.PI * i / sides;
                    float a1 = 2f * Mathf.PI * (i + 1) / sides;
                    var p0 = new Vector3(cx + (radius * Mathf.Cos(a0)), 0f, cz + (radius * Mathf.Sin(a0)));
                    var p1 = new Vector3(cx + (radius * Mathf.Cos(a1)), 0f, cz + (radius * Mathf.Sin(a1)));
                    float mid = (a0 + a1) * 0.5f;
                    var normal = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                    Quad(Lift(p0, y0), Lift(p1, y0), Lift(p1, y1), Lift(p0, y1), normal);
                }

                int top = Add(new Vector3(cx, y1, cz), Vector3.up, Planar(cx, cz));
                int bottom = Add(new Vector3(cx, y0, cz), Vector3.down, Planar(cx, cz));
                int[] topLoop = new int[sides];
                int[] bottomLoop = new int[sides];
                for (int i = 0; i < sides; i++)
                {
                    float angle = 2f * Mathf.PI * i / sides;
                    float x = cx + (radius * Mathf.Cos(angle));
                    float z = cz + (radius * Mathf.Sin(angle));
                    topLoop[i] = Add(new Vector3(x, y1, z), Vector3.up, Planar(x, z));
                    bottomLoop[i] = Add(new Vector3(x, y0, z), Vector3.down, Planar(x, z));
                }

                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    Tri(top, topLoop[j], topLoop[i]);
                    Tri(bottom, bottomLoop[i], bottomLoop[j]);
                }
            }

            /// <summary>Planar quad with a single flat normal (winding fixed by the normal).</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
                {
                    Vector3 swap = b;
                    b = d;
                    d = swap;
                }

                int ia = Add(a, normal, CubeUv(a, normal));
                int ib = Add(b, normal, CubeUv(b, normal));
                int ic = Add(c, normal, CubeUv(c, normal));
                int id = Add(d, normal, CubeUv(d, normal));
                Tri(ia, ib, ic);
                Tri(ia, ic, id);
            }
        }

        // ─── Component ──────────────────────────────────────────────────────

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

#if UNITY_EDITOR
            // The mesh cannot be destroyed directly inside OnValidate, so the rebuild is
            // deferred to the end of the frame.
            UnityEditor.EditorApplication.delayCall -= RebuildDelayed;
            UnityEditor.EditorApplication.delayCall += RebuildDelayed;
#else
            Rebuild();
#endif
        }

#if UNITY_EDITOR
        private void RebuildDelayed()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildDelayed;
            if (this != null && isActiveAndEnabled)
            {
                Rebuild();
            }
        }
#endif

        /// <summary>
        /// Rebuild the mesh for the current parameters. Call it after changing Radius
        /// from the UI (VAB) so as not to wait for OnValidate. The mesh is reused
        /// (FillSeparator): destroying a mesh under the GPU Resident Drawer (HDRP)
        /// gives "invalid Batch/Mesh ID", since AddComponent builds before the UI sets
        /// the radius.
        /// </summary>
        public void Rebuild()
        {
            if (Radius <= 0.01f)
            {
                return;
            }

            MeshFilter filter = GetComponent<MeshFilter>();
            if (mesh == null)
            {
                mesh = new Mesh { hideFlags = HideFlags.DontSave };
            }

            FillSeparator(mesh, Radius, Thickness, Segments, TextureMetersPerTile);
            filter.sharedMesh = mesh;

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
        }
    }
}
