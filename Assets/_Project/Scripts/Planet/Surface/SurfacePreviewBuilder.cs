using System;
using System.Collections.Generic;
using Galilego.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Editor terrain preview: a clipmap patch around the frame point, using the
    /// same noise and the same Burst job as the runtime (TerrainTileJob +
    /// TerrainNoise + HeightfieldTerrain). There is deliberately no "simplified"
    /// height here: if the preview computes a different function, the author
    /// places the base in the wrong spot.
    ///
    /// Geometry is a radial grid (rings) in the ENU tangent plane: ring i has a
    /// geometrically progressing radius and a segment count that doubles every
    /// SegmentsGrowEvery rings. One grid covers both centimetres underfoot and
    /// tens of kilometres to the horizon while the triangle count grows linearly.
    /// The caller sets the radii (usually the SceneView camera distance): this is
    /// the "variable size, zoom".
    ///
    /// Vertices are local to the FRAME ANCHOR POINT (x = north, y = zenith,
    /// z = east), not to the planet centre: coordinates are in kilometres, so
    /// float keeps centimetres. null Terrain = smooth sphere, zero heights.
    /// </summary>
    public static class SurfacePreviewBuilder
    {
        /// <summary>Radial grid: how many rings, segments and of what radius.</summary>
        public struct GridSettings
        {
            /// <summary>Number of rings (radial LOD levels).</summary>
            public int Rings;

            /// <summary>Segments in the first ring; doubles every SegmentsGrowEvery rings after that.</summary>
            public int BaseSegments;

            /// <summary>How many rings between segment count doublings.</summary>
            public int SegmentsGrowEvery;

            /// <summary>Radius of the innermost ring, m.</summary>
            public double InnerRadiusMeters;

            /// <summary>Radius of the outermost ring, m.</summary>
            public double OuterRadiusMeters;
        }

        /// <summary>
        /// Grid for a camera window. visibleRadiusMeters is half the visible frame
        /// height (for the SceneView, its size). Rings follow a strict power-of-two
        /// progression (otherwise a ring would not contain the previous one and the
        /// LOD falls apart), so the total available scale is 2^(Rings-1), and
        /// Inner/Outer are fitted to the camera: outside is 4x the visible radius
        /// (margin beyond the horizon), inside is 2^(Rings-1) times finer.
        ///
        /// The vertex/triangle count does NOT depend on zoom: only radii change.
        /// That is what makes zoom cheap — a rebuild costs like one frame, not like
        /// rebuilding the whole planet.
        /// </summary>
        public static GridSettings GridForVisibleRadius(double visibleRadiusMeters, int rings)
        {
            int ringCount = Math.Max(3, rings);
            double span = Math.Pow(2d, ringCount - 1);
            double outer = Math.Max(1e-3d, visibleRadiusMeters) * 1.25d;
            double inner = outer / span;
            if (inner < 1e-3d)
            {
                // Too close: don't let the inner ring shrink to microns — nothing
                // is visible there anyway, and vertex precision drops.
                inner = 1e-3d;
                outer = inner * span;
            }

            return new GridSettings
            {
                Rings = ringCount,
                BaseSegments = 64,
                SegmentsGrowEvery = 2,
                InnerRadiusMeters = inner,
                OuterRadiusMeters = outer
            };
        }

        /// <summary>Grid topology: the start index of each ring and its segment count.</summary>
        private struct Layout
        {
            public int[] Starts;
            public int[] Segments;
            public int VertexCount;

            public int Start(int ring) => Starts[ring];

            public int Seg(int ring) => Segments[ring];
        }

        private static Layout MakeLayout(in GridSettings grid)
        {
            int rings = Math.Max(1, grid.Rings);
            var layout = new Layout
            {
                Starts = new int[rings],
                Segments = new int[rings],
                VertexCount = 1
            };

            for (int ring = 0; ring < rings; ring++)
            {
                layout.Starts[ring] = layout.VertexCount;
                layout.Segments[ring] = SegmentsForRing(grid, ring);
                layout.VertexCount += layout.Segments[ring];
            }

            return layout;
        }

        /// <summary>Vertex count: the centre plus the sum of segments across rings.</summary>
        public static int VertexCount(in GridSettings grid) => MakeLayout(grid).VertexCount;

        /// <summary>Segments in ring i (0 = innermost).</summary>
        public static int SegmentsForRing(in GridSettings grid, int ringIndex)
        {
            int growth = Math.Max(1, grid.SegmentsGrowEvery);
            int doublings = Math.Max(0, ringIndex) / growth;
            return Math.Max(3, grid.BaseSegments) << Math.Min(doublings, 12);
        }

        private static double RingRadius(in GridSettings grid, int ringIndex)
        {
            int last = Math.Max(1, grid.Rings - 1);
            double inner = Math.Max(1e-6d, grid.InnerRadiusMeters);
            double outer = Math.Max(inner, grid.OuterRadiusMeters);
            double t = Math.Max(0, Math.Min(last, ringIndex)) / (double)last;
            return inner * Math.Pow(outer / inner, t);
        }

        /// <summary>
        /// Build the preview mesh. anchorBodyFixed is the body-fixed position of the
        /// FRAME ANCHOR POINT (SurfaceFrame.AnchorBodyFixed), lat/lon are its
        /// coordinates, frameAltitudeMeters is its altitude above the radius (needed
        /// to place the sea level correctly in local axes). The mesh sits locally
        /// relative to the frame.
        /// </summary>
        public static Mesh Build(
            OrbitingBody body, HeightfieldTerrain terrain,
            double latitudeRadians, double longitudeRadians,
            Vector3d anchorBodyFixed, double frameAltitudeMeters,
            in GridSettings grid, bool tintUnderwater)
        {
            if (body == null)
            {
                return null;
            }

            Layout layout = MakeLayout(grid);
            int count = layout.VertexCount;

            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var colors = new Color[count];
            var extra = new Vector4[count];
            var directionsLocal = new Vector3[count];
            var heightsMeters = new double[count];
            var masksCpu = new float[count];
            var detailsCpu = new float[count];
            var rawDirs = new double3[count];
            var directions = new NativeArray<double3>(count, Allocator.TempJob);

            Vector3d up = SurfaceFrameMath.Up(latitudeRadians, longitudeRadians);
            Vector3d north = SurfaceFrameMath.North(latitudeRadians, longitudeRadians);
            Vector3d east = SurfaceFrameMath.East(latitudeRadians, longitudeRadians);
            double radius = body.Radius;

            directions[0] = new double3(up.X, up.Y, up.Z);
            for (int ring = 0; ring < grid.Rings; ring++)
            {
                double r = RingRadius(grid, ring);
                int segments = layout.Seg(ring);
                int start = layout.Start(ring);
                for (int s = 0; s < segments; s++)
                {
                    // Angle is measured from north: cos → north, sin → east.
                    double a = (s / (double)segments) * Math.PI * 2d;
                    double n = Math.Cos(a) * r;
                    double e = Math.Sin(a) * r;
                    Vector3d dir = SurfaceFrameMath.DirectionOffset(
                        latitudeRadians, longitudeRadians, radius, e, n);
                    directions[start + s] = new double3(dir.X, dir.Y, dir.Z);
                }
            }

            // The raw direction is needed both for the position and for uv1 of the
            // game shader (it looks at the body-fixed direction and builds texture
            // coordinates from it). Convert to local frame axes right away, while
            // the NativeArray is still alive.
            for (int v = 0; v < count; v++)
            {
                rawDirs[v] = directions[v];
                Vector3d local = SurfaceFrameMath.ToLocal(
                    latitudeRadians, longitudeRadians,
                    new Vector3d(rawDirs[v].x, rawDirs[v].y, rawDirs[v].z));
                directionsLocal[v] = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
            }

            double amplitude = terrain != null ? Math.Max(1d, terrain.AmplitudeMeters) : 1d;
            double seaLevel = terrain != null ? terrain.SeaLevelMeters : -1e30d;
            bool hasSea = terrain != null && seaLevel > -1e29d;

            if (terrain != null)
            {
                var heights = new NativeArray<double>(count, Allocator.TempJob);
                var masks = new NativeArray<float>(count, Allocator.TempJob);
                var details = new NativeArray<float>(count, Allocator.TempJob);
                var job = new TerrainTileJob
                {
                    Params = TerrainNoiseParams.FromTerrain(terrain),
                    Directions = directions,
                    Heights = heights,
                    ColorMasks = masks,
                    ColorDetails = details
                };
                job.Schedule(count, 64, new JobHandle()).Complete();

                for (int v = 0; v < count; v++)
                {
                    double h = heights[v] * amplitude;
                    heightsMeters[v] = h;
                    masksCpu[v] = masks[v];
                    detailsCpu[v] = details[v];
                    double3 d = rawDirs[v];
                    positions[v] = ToFrameLocal(
                        new Vector3d(d.x, d.y, d.z) * (radius + h), anchorBodyFixed, north, up, east);
                    colors[v] = Shade(terrain, h, seaLevel, amplitude, masks[v], details[v],
                        latitudeRadians, tintUnderwater && hasSea);
                }

                heights.Dispose();
                masks.Dispose();
                details.Dispose();
            }
            else
            {
                for (int v = 0; v < count; v++)
                {
                    double3 d = rawDirs[v];
                    positions[v] = ToFrameLocal(
                        new Vector3d(d.x, d.y, d.z) * radius, anchorBodyFixed, north, up, east);
                    colors[v] = TerrainPalette.Grass;
                }
            }

            directions.Dispose();

            // Sea level in local frame axes: the sea-level anchor has radius
            // (R + seaLevel), so the zenith projection difference is exactly
            // seaLevel − frameAltitude.
            double seaLocalY = seaLevel + TerrainPalette.SeaEpsilon(amplitude) - frameAltitudeMeters;
            if (hasSea && tintUnderwater)
            {
                FlattenBelowSea(positions, normals, seaLocalY);
            }

            RecomputeNormals(positions, normals, layout, grid);

            // Vertex layout is EXACTLY what the game's Galileo/PlanetSurface
            // expects (its struct Attributes): uv2 = (raw height, slope cosine to
            // zenith, CPU colour mask, CPU detail). The game shader uses all of
            // them: textures, beach and water are computed from these channels.
            for (int v = 0; v < count; v++)
            {
                extra[v] = new Vector4(
                    (float)heightsMeters[v],
                    normals[v].y,
                    masksCpu[v],
                    detailsCpu[v]);
            }

            int[] triangles = BuildTriangles(layout, out _);

            var mesh = new Mesh
            {
                name = "SurfacePreview",
                indexFormat = count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.SetUVs(1, new List<Vector4>(extra));
            mesh.SetUVs(2, new List<Vector3>(directionsLocal));
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 ToFrameLocal(Vector3d point, Vector3d anchor, Vector3d north, Vector3d up, Vector3d east)
        {
            Vector3d d = point - anchor;
            return new Vector3(
                (float)Vector3d.Dot(d, north),
                (float)Vector3d.Dot(d, up),
                (float)Vector3d.Dot(d, east));
        }

        /// <summary>
        /// Fallback simplified material for when the game terrain shader is
        /// unavailable. Flat vertex colour + a scale grid: it doesn't look like
        /// the game, but the preview stays usable instead of black.
        /// </summary>
        public static Material CreateFallbackMaterial()
        {
            Shader shader = Shader.Find("Galilego/SurfacePreview");
            if (shader == null)
            {
                return null;
            }

            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private static Color Shade(
            HeightfieldTerrain terrain, double rawHeight, double seaLevel, double amplitude,
            float mask, float detail, double latitudeRadians, bool tintUnderwater)
        {
            Color c = TerrainPalette.HeightColorEx(
                rawHeight, seaLevel, amplitude, 0d, mask,
                terrain.ColorRockSlopeTan, terrain.ColorRockSlopeWidth,
                terrain.ColorSnowSlopeTan, terrain.ColorNoiseStrength,
                detail, terrain.ColorDetailStrength, latitudeRadians,
                terrain.ColorRockHeightMin, terrain.BeachHeightMeters);

            // Underwater ground is tinted blue, otherwise the ocean is
            // indistinguishable from land. The threshold is the same SeaEpsilon
            // used by the palette and the shader.
            return tintUnderwater && rawHeight < seaLevel + TerrainPalette.SeaEpsilon(amplitude)
                ? Color.Lerp(c, TerrainPalette.Sea, 0.55f)
                : c;
        }

        private static void FlattenBelowSea(Vector3[] positions, Vector3[] normals, double seaLocalY)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].y < seaLocalY)
                {
                    positions[i].y = (float)seaLocalY;
                    normals[i] = Vector3.up;
                }
            }
        }

        /// <summary>
        /// Normals by central differences over the built positions: cheap, and the
        /// mesh is its own reference (no second noise sample needed). Neighbours are
        /// taken from the same topology as the triangles, so indices never diverge.
        /// </summary>
        private static void RecomputeNormals(
            Vector3[] positions, Vector3[] normals, in Layout layout, in GridSettings grid)
        {
            for (int v = 0; v < positions.Length; v++)
            {
                normals[v] = Vector3.up;
            }

            for (int ring = 0; ring < grid.Rings; ring++)
            {
                int segments = layout.Seg(ring);
                int start = layout.Start(ring);
                for (int s = 0; s < segments; s++)
                {
                    int v = start + s;
                    int prev = start + ((s - 1 + segments) % segments);
                    int next = start + ((s + 1) % segments);
                    int outward = ring + 1 < grid.Rings
                        ? layout.Start(ring + 1) + Nearest(layout.Seg(ring + 1), segments, s)
                        : v;
                    int inward = ring == 0
                        ? 0
                        : layout.Start(ring - 1) + Nearest(layout.Seg(ring - 1), segments, s);

                    Vector3 radial = positions[outward] - positions[inward];
                    Vector3 tangential = positions[next] - positions[prev];
                    Vector3 n = Vector3.Cross(tangential, radial);
                    float len = n.magnitude;
                    Vector3 normal = len > 1e-9f ? (n / len) : Vector3.up;
                    normals[v] = normal.y < 0f ? -normal : normal;
                }
            }
        }

        /// <summary>Segment index in a ring of countSeg, nearest by fraction to s/segments.</summary>
        private static int Nearest(int countSeg, int segments, int s)
        {
            int index = (int)((long)s * countSeg / segments);
            return index >= countSeg ? countSeg - 1 : index;
        }

        private static int[] BuildTriangles(in Layout layout, out int triangleCount)
        {
            int first = layout.Seg(0);
            int rings = layout.Segments.Length;

            // Upper bound: two triangles per step. Degenerate ones are actually
            // skipped, so the array is shrunk at the end.
            int capacity = first;
            for (int ring = 1; ring < rings; ring++)
            {
                capacity += 2 * Math.Max(layout.Seg(ring - 1), layout.Seg(ring));
            }

            var triangles = new int[capacity * 3];
            int t = 0;

            // Centre fan into the first ring.
            for (int s = 0; s < first; s++)
            {
                triangles[t++] = 0;
                triangles[t++] = layout.Start(0) + ((s + 1) % first);
                triangles[t++] = layout.Start(0) + s;
            }

            // Quads between neighbouring rings. Counter-clockwise winding (viewed
            // from above, +Y): cross(v1−v0, v2−v0) faces the viewer, so the face is
            // front-facing — otherwise the preview is invisible with Cull Back.
            for (int ring = 1; ring < rings; ring++)
            {
                int innerSeg = layout.Seg(ring - 1);
                int outerSeg = layout.Seg(ring);
                int innerStart = layout.Start(ring - 1);
                int outerStart = layout.Start(ring);
                int steps = Math.Max(innerSeg, outerSeg);

                for (int s = 0; s < steps; s++)
                {
                    // (s + 1) modulo steps: otherwise on the last step the index runs
                    // into the start of the NEXT ring (1..Seg(ring) out of range) and
                    // Unity silently drops the whole mesh.
                    int s1 = (s + 1) % steps;
                    int a0 = innerStart + ((int)((long)s * innerSeg / steps));
                    int a1 = innerStart + ((int)((long)s1 * innerSeg / steps));
                    int b0 = outerStart + ((int)((long)s * outerSeg / steps));
                    int b1 = outerStart + ((int)((long)s1 * outerSeg / steps));

                    if (a0 == a1)
                    {
                        // Inner edge collapsed (twice as many segments outside): the
                        // quad degenerated into a single triangle.
                        triangles[t++] = a0;
                        triangles[t++] = b1;
                        triangles[t++] = b0;
                    }
                    else if (b0 == b1)
                    {
                        triangles[t++] = a0;
                        triangles[t++] = a1;
                        triangles[t++] = b1;
                    }
                    else
                    {
                        triangles[t++] = a0;
                        triangles[t++] = b1;
                        triangles[t++] = b0;

                        triangles[t++] = a0;
                        triangles[t++] = a1;
                        triangles[t++] = b1;
                    }
                }
            }

            if (t != triangles.Length)
            {
                Array.Resize(ref triangles, t);
            }

            triangleCount = t / 3;
            return triangles;
        }
    }
}
