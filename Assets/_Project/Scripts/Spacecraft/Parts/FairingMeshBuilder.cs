using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Mesh generation for one fairing petal. All petals of a fairing share ONE mesh built in the
    /// "petal frame": the origin is the hinge point on the base ring at the petal's middle angle,
    /// +X points outward (radially), +Y is up, +Z is tangential. A point of the profile ring
    /// (radius r, height y) at angle φ from the petal middle sits at (r·cosφ − r0, y, r·sinφ),
    /// where r0 is the base ring radius. The petal object is then placed at the hinge and turned
    /// about Y; opening is a tilt about the local −Z axis (top goes outward).
    /// The wall has a real thickness: outer skin, inner skin, two seam walls and two rims.
    /// Scratch lists are static (main thread only): rebuilding the ghost every frame allocates nothing.
    /// </summary>
    public static class FairingMeshBuilder
    {
        private static readonly List<Vector3> Vertices = new List<Vector3>(4096);
        private static readonly List<Vector3> Normals = new List<Vector3>(4096);
        private static readonly List<Vector2> Uvs = new List<Vector2>(4096);
        private static readonly List<int> Triangles = new List<int>(8192);

        /// <summary>Petal arc span (without the seam gap), radians.</summary>
        public static float PetalSpan(int petals)
        {
            return ((2f * Mathf.PI) / Mathf.Max(1, petals)) - (FairingShell.SeamGapDegrees * Mathf.Deg2Rad);
        }

        /// <summary>Columns across the arc: ~7.5° per column.</summary>
        public static int ArcColumns(float spanRadians)
        {
            return Mathf.Clamp(Mathf.CeilToInt(spanRadians * Mathf.Rad2Deg / 7.5f), 2, 14);
        }

        /// <summary>Convex collider slices per petal (≤ 60° each, so a hull never fills the interior).</summary>
        public static int CollisionSlices(int petals)
        {
            float petalDegrees = 360f / Mathf.Max(1, petals);
            return Mathf.Max(1, Mathf.CeilToInt((petalDegrees / 60f) - 1e-3f));
        }

        // ─── Visual petal ─────────────────────────────────────────────────

        /// <summary>Fill an EXISTING mesh (never recreate: HDRP GPU resident drawer dislikes it).</summary>
        public static void FillPetal(Mesh target, FairingShell shell, float metersPerTile)
        {
            Vertices.Clear();
            Normals.Clear();
            Uvs.Clear();
            Triangles.Clear();

            IReadOnlyList<FairingRing> rings = shell.Rings;
            if (rings.Count >= 2)
            {
                float span = PetalSpan(shell.Petals);
                float half = span * 0.5f;
                int cols = ArcColumns(span);
                float tile = Mathf.Max(0.1f, metersPerTile);
                float t = FairingShell.ShellThicknessM;
                float r0 = rings[0].Radius;
                float slantStart = 0f;

                // Smooth normals: each profile ring takes the mean of the two segments that meet
                // there, so the shell shades as one surface instead of showing a hard band at every
                // joint (the plate, the seams and the rims stay hard-edged).
                var ringNormals = new Vector2[rings.Count];
                for (int i = 0; i < rings.Count; i++)
                {
                    Vector2 sum = Vector2.zero;
                    if (i > 0)
                    {
                        sum += SegmentNormal(rings[i - 1], rings[i]);
                    }

                    if (i + 1 < rings.Count)
                    {
                        sum += SegmentNormal(rings[i], rings[i + 1]);
                    }

                    ringNormals[i] = sum.sqrMagnitude > 1e-8f ? sum.normalized : new Vector2(1f, 0f);
                }

                for (int s = 0; s + 1 < rings.Count; s++)
                {
                    FairingRing a = rings[s];
                    FairingRing b = rings[s + 1];
                    float dr = b.Radius - a.Radius;
                    float dy = b.Y - a.Y;
                    float len = Mathf.Sqrt((dr * dr) + (dy * dy));
                    if (len < 1e-4f)
                    {
                        continue;
                    }

                    // Outward normal in the (radial, up) plane.
                    float nr = dy / len;
                    float ny = -dr / len;

                    var innerA = new FairingRing(Mathf.Max(0f, a.Radius - (t * nr)), a.Y - (t * ny));
                    var innerB = new FairingRing(Mathf.Max(0f, b.Radius - (t * nr)), b.Y - (t * ny));

                    AddSkin(a, b, ringNormals[s], ringNormals[s + 1], -half, half, cols, r0, tile, slantStart, len, false);
                    AddSkin(innerA, innerB, ringNormals[s], ringNormals[s + 1], -half, half, cols, r0, tile, slantStart, len, true);

                    // Seam walls at both edges of the petal.
                    AddSeamWall(a, b, innerA, innerB, half, r0, tile);
                    AddSeamWall(a, b, innerA, innerB, -half, r0, tile);

                    // Bottom rim (first segment) and top rim (last segment).
                    if (s == 0)
                    {
                        AddRim(a, innerA, -dr / len, -dy / len, half, cols, r0, tile);
                    }

                    if (s + 2 == rings.Count)
                    {
                        AddRim(b, innerB, dr / len, dy / len, half, cols, r0, tile);
                    }

                    slantStart += len;
                }
            }

            target.Clear();
            if (Vertices.Count > 65000)
            {
                target.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            target.SetVertices(Vertices);
            target.SetNormals(Normals);
            target.SetUVs(0, Uvs);
            target.SetTriangles(Triangles, 0);
            target.RecalculateBounds();
            target.name = "FairingPetal";
        }

        /// <summary>Outward normal of one cone segment in the (radial, up) plane.</summary>
        private static Vector2 SegmentNormal(FairingRing a, FairingRing b)
        {
            float dr = b.Radius - a.Radius;
            float dy = b.Y - a.Y;
            float len = Mathf.Sqrt((dr * dr) + (dy * dy));
            return len < 1e-4f ? Vector2.zero : new Vector2(dy / len, -dr / len);
        }

        /// <summary>
        /// One conical skin strip between two rings. Normals are shared along the segment (smooth
        /// radial normals) and come per ring, so the profile shades continuously across joints; the
        /// winding is fixed by the outward normal, so it is correct whatever the handedness.
        /// </summary>
        private static void AddSkin(
            FairingRing a, FairingRing b, Vector2 normalA, Vector2 normalB, float phiFrom, float phiTo,
            int cols, float r0, float tile, float slantStart, float slantLength, bool inner)
        {
            int baseIndex = Vertices.Count;
            float sign = inner ? -1f : 1f;
            for (int c = 0; c <= cols; c++)
            {
                float phi = Mathf.Lerp(phiFrom, phiTo, c / (float)cols);
                float cs = Mathf.Cos(phi);
                float sn = Mathf.Sin(phi);
                var normalLow = new Vector3(cs * normalA.x, normalA.y, sn * normalA.x) * sign;
                var normalHigh = new Vector3(cs * normalB.x, normalB.y, sn * normalB.x) * sign;
                Add(new Vector3((a.Radius * cs) - r0, a.Y, a.Radius * sn), normalLow,
                    new Vector2(phi * a.Radius / tile, slantStart / tile));
                Add(new Vector3((b.Radius * cs) - r0, b.Y, b.Radius * sn), normalHigh,
                    new Vector2(phi * b.Radius / tile, (slantStart + slantLength) / tile));
            }

            for (int c = 0; c < cols; c++)
            {
                int a0 = baseIndex + (c * 2);
                int b0 = a0 + 1;
                int a1 = a0 + 2;
                int b1 = a0 + 3;
                Vector3 meanNormal = Normals[a0] + Normals[a1];
                TriPair(a0, b0, b1, a1, meanNormal);
            }
        }

        /// <summary>Quad (a0, b0, b1, a1) as two triangles, wound to face <paramref name="normal"/>.</summary>
        private static void TriPair(int a0, int b0, int b1, int a1, Vector3 normal)
        {
            // Both triangles' areas are summed: at the nose point one of them is degenerate.
            Vector3 c1 = Vector3.Cross(Vertices[b0] - Vertices[a0], Vertices[b1] - Vertices[a0]);
            Vector3 c2 = Vector3.Cross(Vertices[b1] - Vertices[a0], Vertices[a1] - Vertices[a0]);
            bool flip = Vector3.Dot(c1 + c2, normal) < 0f;
            if (!flip)
            {
                Triangles.Add(a0); Triangles.Add(b0); Triangles.Add(b1);
                Triangles.Add(a0); Triangles.Add(b1); Triangles.Add(a1);
            }
            else
            {
                Triangles.Add(a0); Triangles.Add(b1); Triangles.Add(b0);
                Triangles.Add(a0); Triangles.Add(a1); Triangles.Add(b1);
            }
        }

        private static void AddSeamWall(
            FairingRing a, FairingRing b, FairingRing innerA, FairingRing innerB,
            float phi, float r0, float tile)
        {
            float cs = Mathf.Cos(phi);
            float sn = Mathf.Sin(phi);
            Vector3 normal = phi > 0f ? new Vector3(-sn, 0f, cs) : new Vector3(sn, 0f, -cs);
            Quad(Point(a, cs, sn, r0), Point(b, cs, sn, r0), Point(innerB, cs, sn, r0), Point(innerA, cs, sn, r0),
                normal, tile);
        }

        /// <summary>Rim strip between the outer and inner ring along the whole arc.</summary>
        private static void AddRim(
            FairingRing outer, FairingRing inner, float dirR, float dirY,
            float half, int cols, float r0, float tile)
        {
            for (int c = 0; c < cols; c++)
            {
                float p0 = Mathf.Lerp(-half, half, c / (float)cols);
                float p1 = Mathf.Lerp(-half, half, (c + 1) / (float)cols);
                float mid = (p0 + p1) * 0.5f;
                var normal = new Vector3(Mathf.Cos(mid) * dirR, dirY, Mathf.Sin(mid) * dirR);
                Quad(
                    Point(outer, Mathf.Cos(p0), Mathf.Sin(p0), r0), Point(outer, Mathf.Cos(p1), Mathf.Sin(p1), r0),
                    Point(inner, Mathf.Cos(p1), Mathf.Sin(p1), r0), Point(inner, Mathf.Cos(p0), Mathf.Sin(p0), r0),
                    normal, tile);
            }
        }

        private static Vector3 Point(FairingRing ring, float cs, float sn, float r0)
        {
            return new Vector3((ring.Radius * cs) - r0, ring.Y, ring.Radius * sn);
        }

        private static void Add(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Vertices.Add(position);
            Normals.Add(normal);
            Uvs.Add(uv);
        }

        private static Vector2 CubeUv(Vector3 p, Vector3 n, float tile)
        {
            float ax = Mathf.Abs(n.x);
            float ay = Mathf.Abs(n.y);
            float az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az)
            {
                return new Vector2(p.x / tile, p.z / tile);
            }

            return ax >= az ? new Vector2(p.z / tile, p.y / tile) : new Vector2(p.x / tile, p.y / tile);
        }

        /// <summary>Flat quad with its own vertices and a hard normal; winding fixed by the normal.</summary>
        private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float tile)
        {
            if (normal.sqrMagnitude < 1e-10f)
            {
                return;
            }

            normal.Normalize();
            if (Vector3.Dot(Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a), normal) < 0f)
            {
                Vector3 swap = b;
                b = d;
                d = swap;
            }

            int i = Vertices.Count;
            Add(a, normal, CubeUv(a, normal, tile));
            Add(b, normal, CubeUv(b, normal, tile));
            Add(c, normal, CubeUv(c, normal, tile));
            Add(d, normal, CubeUv(d, normal, tile));
            Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2);
            Triangles.Add(i); Triangles.Add(i + 2); Triangles.Add(i + 3);
        }

        // ─── Collision slice ─────────────────────────────────────────────────

        /// <summary>
        /// Point cloud of one convex slice of a petal (outer + inner skin at three columns). The
        /// collider is convex (MeshCollider.convex); the triangles are only a valid dummy fan.
        /// </summary>
        public static void FillCollisionSlice(Mesh target, FairingShell shell, int slice, int slices)
        {
            Vertices.Clear();
            Normals.Clear();
            Uvs.Clear();
            Triangles.Clear();

            float span = PetalSpan(shell.Petals);
            float sliceSpan = span / Mathf.Max(1, slices);
            float phi0 = (-span * 0.5f) + (sliceSpan * slice);
            float r0 = shell.BaseRadius;
            float t = FairingShell.ShellThicknessM;

            for (int k = 0; k < shell.Rings.Count; k++)
            {
                FairingRing ring = shell.Rings[k];
                for (int c = 0; c < 3; c++)
                {
                    float phi = phi0 + (sliceSpan * 0.5f * c);
                    float cs = Mathf.Cos(phi);
                    float sn = Mathf.Sin(phi);
                    Vertices.Add(new Vector3((ring.Radius * cs) - r0, ring.Y, ring.Radius * sn));
                    float inner = Mathf.Max(0f, ring.Radius - (t * 2f));
                    Vertices.Add(new Vector3((inner * cs) - r0, ring.Y, inner * sn));
                }
            }

            for (int i = 1; i + 1 < Vertices.Count; i++)
            {
                Triangles.Add(0);
                Triangles.Add(i);
                Triangles.Add(i + 1);
            }

            target.Clear();
            target.SetVertices(Vertices);
            target.SetTriangles(Triangles, 0);
            target.RecalculateBounds();
            target.name = "FairingCollision";
        }
    }
}
