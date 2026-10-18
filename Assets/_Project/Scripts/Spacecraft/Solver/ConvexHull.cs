using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Spacecraft.Solver
{
    /// <summary>
    /// Convex hull of a point cloud (quickhull, double). Faces are triangles with
    /// outward normals and planes; a point query returns the depth and outward
    /// normal of the nearest face (part against site contacts).
    /// Duplicates are collapsed; degenerate clouds (planar/linear) are replaced
    /// by an AABB hull (slab) so contact is not lost.
    /// </summary>
    public sealed class ConvexHull
    {
        /// <summary>Hull vertices (deduplicated).</summary>
        public readonly Vector3d[] Points;

        public readonly int[] FaceA;
        public readonly int[] FaceB;
        public readonly int[] FaceC;

        /// <summary>Outward unit face normals.</summary>
        public readonly Vector3d[] Normals;

        /// <summary>dot(normal, vertex0) for each face.</summary>
        public readonly double[] Offsets;

        public readonly Vector3d Min;
        public readonly Vector3d Max;

        /// <summary>true — the cloud was degenerate, hull built from the AABB.</summary>
        public readonly bool Degenerate;

        private ConvexHull(
            Vector3d[] points,
            List<int[]> faces,
            Vector3d min,
            Vector3d max,
            bool degenerate)
        {
            Points = points;
            Min = min;
            Max = max;
            Degenerate = degenerate;
            FaceA = new int[faces.Count];
            FaceB = new int[faces.Count];
            FaceC = new int[faces.Count];
            Normals = new Vector3d[faces.Count];
            Offsets = new double[faces.Count];
            for (int i = 0; i < faces.Count; i++)
            {
                int[] f = faces[i];
                FaceA[i] = f[0];
                FaceB[i] = f[1];
                FaceC[i] = f[2];
                Vector3d n = Vector3d.Cross(points[f[1]] - points[f[0]], points[f[2]] - points[f[0]]);
                double len = n.Magnitude;
                n = len > 1e-30d ? n / len : new Vector3d(0d, 1d, 0d);
                Normals[i] = n;
                Offsets[i] = Vector3d.Dot(n, points[f[0]]);
            }
        }

        /// <summary>
        /// Builds the hull. toleranceScale is the relative tolerance (to the AABB size).
        /// </summary>
        public static ConvexHull Build(IReadOnlyList<Vector3d> raw, double toleranceScale = 1e-9d)
        {
            if (raw == null)
            {
                throw new ArgumentNullException(nameof(raw));
            }

            if (raw.Count == 0)
            {
                throw new ArgumentException("Оболочка без точек невозможна.", nameof(raw));
            }

            Vector3d min = raw[0];
            Vector3d max = raw[0];
            for (int i = 1; i < raw.Count; i++)
            {
                Vector3d p = raw[i];
                min = new Vector3d(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vector3d(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }

            double extent = (max - min).Magnitude;
            double eps = Math.Max(1e-12d, extent * toleranceScale);

            Vector3d[] points = Dedupe(raw, eps);

            if (points.Length < 4)
            {
                return BoxFallback(min, max);
            }

            // Initial simplex: line → triangle → tetrahedron.
            FindExtremePair(points, out int ia, out int ib);
            Vector3d a = points[ia];
            Vector3d b = points[ib];
            Vector3d ab = b - a;
            double abLen = ab.Magnitude;
            if (abLen <= eps)
            {
                return BoxFallback(min, max);
            }

            Vector3d abUnit = ab / abLen;
            int ic = -1;
            double bestLine = eps;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3d d = points[i] - a;
                double dist = (d - (abUnit * Vector3d.Dot(d, abUnit))).Magnitude;
                if (dist > bestLine)
                {
                    bestLine = dist;
                    ic = i;
                }
            }

            if (ic < 0)
            {
                return BoxFallback(min, max);
            }

            Vector3d c = points[ic];
            Vector3d abc = Vector3d.Cross(ab, c - a);
            double abcLen = abc.Magnitude;
            if (abcLen <= eps * eps)
            {
                return BoxFallback(min, max);
            }

            Vector3d abcUnit = abc / abcLen;
            int id = -1;
            double bestPlane = eps;
            for (int i = 0; i < points.Length; i++)
            {
                double dist = Math.Abs(Vector3d.Dot(abcUnit, points[i] - a));
                if (dist > bestPlane)
                {
                    bestPlane = dist;
                    id = i;
                }
            }

            if (id < 0)
            {
                return BoxFallback(min, max);
            }

            Vector3d interior = (a + b + c + points[id]) * 0.25d;

            // Four tetrahedron faces, oriented outward.
            var faces = new List<int[]>(64);
            var outside = new List<int>(64);
            AddFace(faces, outside, points, interior, eps, ia, ib, ic);
            AddFace(faces, outside, points, interior, eps, ia, ic, id);
            AddFace(faces, outside, points, interior, eps, ia, id, ib);
            AddFace(faces, outside, points, interior, eps, ib, id, ic);

            // Distribute points to the faces they see.
            var assignments = new List<int>[faces.Count];
            for (int i = 0; i < faces.Count; i++)
            {
                assignments[i] = new List<int>();
            }

            for (int i = 0; i < points.Length; i++)
            {
                if (i == ia || i == ib || i == ic || i == id)
                {
                    continue;
                }

                int bestFace = -1;
                double bestDist = eps;
                for (int f = 0; f < faces.Count; f++)
                {
                    double s = FaceDistance(points, faces[f], points[i]);
                    if (s > bestDist)
                    {
                        bestDist = s;
                        bestFace = f;
                    }
                }

                if (bestFace >= 0)
                {
                    assignments[bestFace].Add(i);
                }
            }

            // Main loop: an outside point expands the hull.
            var edgeCount = new Dictionary<long, int>(256);
            var horizon = new List<long>(64);
            int guard = 0;
            while (true)
            {
                int workFace = -1;
                for (int f = 0; f < faces.Count; f++)
                {
                    if (assignments[f] != null && assignments[f].Count > 0)
                    {
                        workFace = f;
                        break;
                    }
                }

                if (workFace < 0 || ++guard > 100000)
                {
                    break;
                }

                List<int> outsideSet = assignments[workFace];
                int far = outsideSet[0];
                double farDist = FaceDistance(points, faces[workFace], points[far]);
                for (int i = 1; i < outsideSet.Count; i++)
                {
                    double d = FaceDistance(points, faces[workFace], points[outsideSet[i]]);
                    if (d > farDist)
                    {
                        farDist = d;
                        far = outsideSet[i];
                    }
                }

                Vector3d farPoint = points[far];

                // Visible faces: farPoint strictly above the plane.
                var visible = new List<int>(16);
                for (int f = 0; f < faces.Count; f++)
                {
                    if (FaceDistance(points, faces[f], farPoint) > eps)
                    {
                        visible.Add(f);
                    }
                }

                // Horizon: edges of visible faces bordering invisible ones.
                edgeCount.Clear();
                for (int v = 0; v < visible.Count; v++)
                {
                    int[] f = faces[visible[v]];
                    CountEdge(edgeCount, f[0], f[1]);
                    CountEdge(edgeCount, f[1], f[2]);
                    CountEdge(edgeCount, f[2], f[0]);
                }

                horizon.Clear();
                foreach (KeyValuePair<long, int> pair in edgeCount)
                {
                    if (pair.Value == 1)
                    {
                        horizon.Add(pair.Key);
                    }
                }

                // Points of removed faces are reassigned after the new ones are built.
                var orphaned = new List<int>(64);
                for (int v = 0; v < visible.Count; v++)
                {
                    int f = visible[v];
                    if (assignments[f] != null)
                    {
                        orphaned.AddRange(assignments[f]);
                        assignments[f] = null;
                    }
                }

                // Remove visible faces (replaced with null — compaction does not matter).
                var removed = new bool[faces.Count];
                for (int v = 0; v < visible.Count; v++)
                {
                    removed[visible[v]] = true;
                }

                var compactFaces = new List<int[]>(faces.Count + horizon.Count);
                var compactAssignments = new List<List<int>>(faces.Count + horizon.Count);
                for (int f = 0; f < faces.Count; f++)
                {
                    if (!removed[f])
                    {
                        compactFaces.Add(faces[f]);
                        compactAssignments.Add(assignments[f] ?? new List<int>());
                    }
                }

                // New faces from the horizon to farPoint.
                for (int h = 0; h < horizon.Count; h++)
                {
                    long edge = horizon[h];
                    int e0 = (int)(edge >> 32);
                    int e1 = (int)(edge & 0xFFFFFFFFL);
                    var face = new List<int>(3) { e0, e1, far };
                    // Outward orientation: normal points away from the interior point.
                    Vector3d n = Vector3d.Cross(points[e1] - points[e0], farPoint - points[e0]);
                    if (Vector3d.Dot(n, interior - points[e0]) > 0d)
                    {
                        int tmp = face[0];
                        face[0] = face[1];
                        face[1] = tmp;
                    }

                    compactFaces.Add(new[] { face[0], face[1], face[2] });
                    compactAssignments.Add(new List<int>());
                }

                faces = compactFaces;
                assignments = compactAssignments.ToArray();

                // Reassign orphaned points to new/remaining faces.
                for (int i = 0; i < orphaned.Count; i++)
                {
                    int p = orphaned[i];
                    if (p == far)
                    {
                        continue;
                    }

                    int bestFace = -1;
                    double bestDist = eps;
                    for (int f = 0; f < faces.Count; f++)
                    {
                        double s = FaceDistance(points, faces[f], points[p]);
                        if (s > bestDist)
                        {
                            bestDist = s;
                            bestFace = f;
                        }
                    }

                    if (bestFace >= 0)
                    {
                        assignments[bestFace].Add(p);
                    }
                }
            }

            return new ConvexHull(points, faces, min, max, false);
        }

        /// <summary>
        /// Point inside the hull: depth &gt; 0 and the outward normal of the nearest face.
        /// (Minimum over face planes; exact for boxes/typical site hulls.)
        /// </summary>
        public bool QueryPoint(Vector3d point, out double depth, out Vector3d normal)
        {
            double maxSigned = double.NegativeInfinity;
            int face = -1;
            for (int i = 0; i < Normals.Length; i++)
            {
                double s = Vector3d.Dot(Normals[i], point) - Offsets[i];
                if (s > maxSigned)
                {
                    maxSigned = s;
                    face = i;
                }
            }

            if (face < 0 || maxSigned >= 0d)
            {
                depth = 0d;
                normal = Vector3d.Zero;
                return false;
            }

            depth = -maxSigned;
            normal = Normals[face];
            return true;
        }

        private static void AddFace(
            List<int[]> faces,
            List<int> outside,
            Vector3d[] points,
            Vector3d interior,
            double eps,
            int a,
            int b,
            int c)
        {
            Vector3d n = Vector3d.Cross(points[b] - points[a], points[c] - points[a]);
            if (Vector3d.Dot(n, interior - points[a]) > 0d)
            {
                int tmp = b;
                b = c;
                c = tmp;
            }

            faces.Add(new[] { a, b, c });
        }

        private static double FaceDistance(Vector3d[] points, int[] face, Vector3d p)
        {
            Vector3d n = Vector3d.Cross(points[face[1]] - points[face[0]], points[face[2]] - points[face[0]]);
            double len = n.Magnitude;
            if (len <= 1e-30d)
            {
                return double.NegativeInfinity;
            }

            n = n / len;
            return Vector3d.Dot(n, p - points[face[0]]);
        }

        private static void CountEdge(Dictionary<long, int> counts, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        private static void FindExtremePair(Vector3d[] points, out int a, out int b)
        {
            int minX = 0, maxX = 0, minY = 0, maxY = 0, minZ = 0, maxZ = 0;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i].X < points[minX].X) { minX = i; }
                if (points[i].X > points[maxX].X) { maxX = i; }
                if (points[i].Y < points[minY].Y) { minY = i; }
                if (points[i].Y > points[maxY].Y) { maxY = i; }
                if (points[i].Z < points[minZ].Z) { minZ = i; }
                if (points[i].Z > points[maxZ].Z) { maxZ = i; }
            }

            int[] candidates = { minX, maxX, minY, maxY, minZ, maxZ };
            a = 0;
            b = 1;
            double best = -1d;
            for (int i = 0; i < candidates.Length; i++)
            {
                for (int j = i + 1; j < candidates.Length; j++)
                {
                    double d = (points[candidates[j]] - points[candidates[i]]).SqrMagnitude;
                    if (d > best)
                    {
                        best = d;
                        a = candidates[i];
                        b = candidates[j];
                    }
                }
            }
        }

        private static Vector3d[] Dedupe(IReadOnlyList<Vector3d> raw, double eps)
        {
            double epsSq = eps * eps;
            var unique = new List<Vector3d>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                Vector3d p = raw[i];
                bool duplicate = false;
                for (int j = 0; j < unique.Count; j++)
                {
                    if ((unique[j] - p).SqrMagnitude <= epsSq)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    unique.Add(p);
                }
            }

            return unique.ToArray();
        }

        private static ConvexHull BoxFallback(Vector3d min, Vector3d max)
        {
            double pad = Math.Max(1e-6d, (max - min).Magnitude * 1e-9d);
            var p = new Vector3d[8];
            for (int i = 0; i < 8; i++)
            {
                p[i] = new Vector3d(
                    (i & 1) == 0 ? min.X : max.X,
                    (i & 2) == 0 ? min.Y : max.Y,
                    (i & 4) == 0 ? min.Z : max.Z);
            }

            var faces = new List<int[]>
            {
                new[] { 0, 2, 3 }, new[] { 0, 3, 1 },   // -Z, +Z (the constructor fixes the normal order)
                new[] { 0, 4, 6 }, new[] { 0, 6, 2 },   // -Y, +Y
                new[] { 0, 1, 5 }, new[] { 0, 5, 4 },   // -X, +X
                new[] { 1, 3, 7 }, new[] { 1, 7, 5 },
                new[] { 2, 6, 7 }, new[] { 2, 7, 3 },
                new[] { 4, 5, 7 }, new[] { 4, 7, 6 },
            };

            Vector3d interior = (min + max) * 0.5d;
            var oriented = new List<int[]>(faces.Count);
            foreach (int[] f in faces)
            {
                int a = f[0], b = f[1], c = f[2];
                Vector3d n = Vector3d.Cross(p[b] - p[a], p[c] - p[a]);
                if (Vector3d.Dot(n, interior - p[a]) > 0d)
                {
                    int tmp = b;
                    b = c;
                    c = tmp;
                }

                oriented.Add(new[] { a, b, c });
            }

            return new ConvexHull(p, oriented, min - new Vector3d(pad, pad, pad), max + new Vector3d(pad, pad, pad), true);
        }
    }
}
