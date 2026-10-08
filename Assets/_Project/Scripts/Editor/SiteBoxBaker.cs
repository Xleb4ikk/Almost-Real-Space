using System;
using System.Collections.Generic;
using Galilego.Universe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Запекает поверхность мешей в непересекающиеся боксы в локальных осях каждой части.
    /// </summary>
    public static class SiteBoxBaker
    {
        private const long MaxGridCells = 4000000;
        private const int MaxBoxesPerPart = 200000;
        private const int MaxTotalBoxesWarning = 4000;

        public static int Bake(SiteBox site)
        {
            if (site == null)
            {
                throw new ArgumentNullException(nameof(site));
            }

            Transform root = site.MeshRoot != null ? site.MeshRoot : site.transform.parent;
            if (root == null)
            {
                throw new InvalidOperationException("Не задан корень модели и у SiteBox нет родителя.");
            }

            float voxelSize = Mathf.Max(site.BakeVoxelSizeMeters, 0.1f);
            List<MeshFilter> filters = EnsureReadable(root, SiteBox.CollectPartFilters(root));
            var bakedParts = new List<SiteBox.BakedPartData>();
            var seen = new HashSet<MeshFilter>();
            int totalBoxes = 0;
            int hullParts = 0;
            int boxParts = 0;

            for (int i = 0; i < filters.Count; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || !seen.Add(filter) || site.IsSkipped(filter.name))
                {
                    continue;
                }

                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0 || mesh.triangles.Length < 3)
                {
                    continue;
                }

                // Выпуклая часть → один точный convex hull из собственного меша.
                // ValidateHull ловит случай, когда PhysX при сварке отсёк
                // крайние вершины (лимит на облако точек) — тогда остаются боксы.
                if (IsConvex(mesh))
                {
                    if (ValidateHull(mesh, out string reason))
                    {
                        bakedParts.Add(new SiteBox.BakedPartData
                        {
                            Source = filter,
                            UseConvexMesh = true
                        });
                        hullParts++;
                        continue;
                    }

                    Debug.LogWarning("[SiteBox] '" + filter.name +
                        "': меш выпуклый, но hull не прошёл проверку контейнмента (" + reason +
                        ") — коллизия осталась по боксам.", filter);
                }

                List<SiteBox.BakedBoxData> boxes = Decompose(mesh, filter.transform, voxelSize);
                if (boxes.Count == 0)
                {
                    continue;
                }

                bakedParts.Add(new SiteBox.BakedPartData
                {
                    Source = filter,
                    Boxes = boxes
                });
                boxParts++;
                totalBoxes += boxes.Count;
            }

            if (bakedParts.Count == 0)
            {
                throw new InvalidOperationException("Не найдено мешей с треугольниками для запекания.");
            }

            if (totalBoxes > MaxTotalBoxesWarning)
            {
                Debug.LogWarning("[SiteBox] Запечено " + totalBoxes + " боксов (лимит внимания " +
                    MaxTotalBoxesWarning + "). Увеличьте Bake Voxel Size Meters или разбейте вогнутые части на выпуклые вручную.",
                    site);
            }

            Undo.RecordObject(site, "Запечь коллайдеры SiteBox");
            site.BakedParts = bakedParts;
            site.BakeVoxelSizeMeters = voxelSize;
            FitBroadPhaseBounds(site, bakedParts);
            EditorUtility.SetDirty(site);
            PrefabUtility.RecordPrefabInstancePropertyModifications(site);
            if (site.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(site.gameObject.scene);
            }

            int totalColliders = hullParts + totalBoxes;
            Debug.Log("[SiteBox] Запечено: " + hullParts + " convex hull'ов (точно), " +
                boxParts + " частей боксами (" + totalBoxes + " боксов, шаг " +
                voxelSize.ToString("0.##") + " м) = " + totalColliders + " коллайдеров: " + site.name, site);
            return totalColliders;
        }

        public static void Clear(SiteBox site)
        {
            if (site == null)
            {
                return;
            }

            Undo.RecordObject(site, "Очистить запекание SiteBox");
            site.BakedParts = new List<SiteBox.BakedPartData>();
            EditorUtility.SetDirty(site);
            PrefabUtility.RecordPrefabInstancePropertyModifications(site);
            if (site.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(site.gameObject.scene);
            }
        }

        /// <summary>
        /// Включает Read/Write у моделей, участвующих в запекании: сварка hull'а
        /// в рантайме (BuildParts при старте сцены) читает данные меша скриптом.
        /// После переимпорта список частей пересобирается — ссылки на меши могли обновиться.
        /// </summary>
        private static List<MeshFilter> EnsureReadable(Transform root, List<MeshFilter> filters)
        {
            var dirty = new List<ModelImporter>();
            var seenPaths = new HashSet<string>();
            for (int i = 0; i < filters.Count; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (string.IsNullOrEmpty(path) || !seenPaths.Add(path))
                {
                    continue;
                }

                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    dirty.Add(importer);
                }
            }

            if (dirty.Count == 0)
            {
                return filters;
            }

            foreach (ModelImporter importer in dirty)
            {
                Debug.Log("[SiteBox] Включён Read/Write в импорте '" + importer.assetPath +
                    "' — без него hull не сварится в билде (меш нечитаем рантаймом).");
                importer.SaveAndReimport();
            }

            return SiteBox.CollectPartFilters(root);
        }

        /// <summary>Меш строго выпуклый: все вершины по одну сторону плоскости каждой грани.</summary>
        private static bool IsConvex(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            const float eps = 1e-3f;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f)
                {
                    continue;
                }

                float minD = float.MaxValue;
                float maxD = float.MinValue;
                for (int k = 0; k < vertices.Length; k++)
                {
                    float d = Vector3.Dot(normal, vertices[k] - a);
                    if (d < minD)
                    {
                        minD = d;
                    }

                    if (d > maxD)
                    {
                        maxD = d;
                    }
                }

                if (minD < -eps && maxD > eps)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Сваривает временный convex MeshCollider и проверяет, что PhysX не отсёк
        /// крайние вершины при построении hull (лимит на облако точек): сфера
        /// радиуса 0.1% габарита в каждой вершине меша должна пересекать hull.
        /// </summary>
        private static bool ValidateHull(Mesh mesh, out string reason)
        {
            reason = null;
            var colliderGo = new GameObject("__SiteBoxHullTest") { hideFlags = HideFlags.HideAndDontSave };
            var probeGo = new GameObject("__SiteBoxHullProbe") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                MeshCollider hull = colliderGo.AddComponent<MeshCollider>();
                hull.convex = true;
                hull.sharedMesh = mesh;
                if (hull.sharedMesh == null)
                {
                    reason = "hull не сварился";
                    return false;
                }

                SphereCollider probe = probeGo.AddComponent<SphereCollider>();
                Bounds bounds = mesh.bounds;
                float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float radius = Mathf.Max(1e-4f, maxDim * 0.001f);
                probe.radius = radius;

                Vector3[] vertices = mesh.vertices;
                int fails = 0;
                Vector3 firstFail = Vector3.zero;
                for (int i = 0; i < vertices.Length; i++)
                {
                    // Вершина на поверхности hull'а: dist ≈ radius; внутри — больше;
                    // снаружи — меньше (или пересечения нет вовсе).
                    //
                    // Допуск: PhysX при сварке упрощает hull и срезает углы на
                    // миллиметры-сантиметры (проверено: база площадки — до 2.9 см
                    // при радиусе 4.6 см). Это не влияет на игру (радиус капсулы
                    // ~30 см), поэтому провалом считаем только dist < 0.1*radius
                    // (срез больше 90% радиуса) или полное отсутствие пересечения —
                    // так ловится реальная потеря PhysX региона вершин.
                    bool hit = Physics.ComputePenetration(
                        probe, vertices[i], Quaternion.identity,
                        hull, Vector3.zero, Quaternion.identity,
                        out _, out float dist);
                    if (!hit || dist < radius * 0.1f)
                    {
                        if (fails == 0)
                        {
                            firstFail = vertices[i];
                        }

                        fails++;
                    }
                }

                if (fails > 0)
                {
                    reason = fails + " из " + vertices.Length + " вершин вне hull (первая " + firstFail.ToString("F3") + ")";
                    return false;
                }

                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(colliderGo);
                UnityEngine.Object.DestroyImmediate(probeGo);
            }
        }

        private static List<SiteBox.BakedBoxData> Decompose(Mesh mesh, Transform source, float requestedVoxelSize)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices.Length == 0 || triangles.Length < 3)
            {
                return new List<SiteBox.BakedBoxData>();
            }

            Matrix4x4 matrix = source.localToWorldMatrix;
            Vector3 worldAxisScale = new Vector3(
                Mathf.Max(matrix.GetColumn(0).magnitude, 0.0001f),
                Mathf.Max(matrix.GetColumn(1).magnitude, 0.0001f),
                Mathf.Max(matrix.GetColumn(2).magnitude, 0.0001f));

            Bounds bounds = mesh.bounds;
            Vector3 cellSize = new Vector3(
                requestedVoxelSize / worldAxisScale.x,
                requestedVoxelSize / worldAxisScale.y,
                requestedVoxelSize / worldAxisScale.z);
            Vector3Int dimensions = GetDimensions(bounds, cellSize);
            float resolutionScale = 1f;
            while (CellCount(dimensions) > MaxGridCells)
            {
                resolutionScale *= 1.15f;
                float voxelSize = requestedVoxelSize * resolutionScale;
                cellSize = new Vector3(
                    voxelSize / worldAxisScale.x,
                    voxelSize / worldAxisScale.y,
                    voxelSize / worldAxisScale.z);
                dimensions = GetDimensions(bounds, cellSize);
            }

            Vector3 origin = bounds.min - cellSize;
            int nx = dimensions.x;
            int ny = dimensions.y;
            int nz = dimensions.z;
            int cellCount = checked(nx * ny * nz);
            var occupied = new bool[cellCount];

            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                int ia = triangles[triangle];
                int ib = triangles[triangle + 1];
                int ic = triangles[triangle + 2];
                if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                {
                    continue;
                }

                Vector3 a = vertices[ia];
                Vector3 b = vertices[ib];
                Vector3 c = vertices[ic];
                Vector3 triMin = Vector3.Min(a, Vector3.Min(b, c));
                Vector3 triMax = Vector3.Max(a, Vector3.Max(b, c));
                int minX = ClampCell(Mathf.FloorToInt((triMin.x - origin.x) / cellSize.x), nx);
                int minY = ClampCell(Mathf.FloorToInt((triMin.y - origin.y) / cellSize.y), ny);
                int minZ = ClampCell(Mathf.FloorToInt((triMin.z - origin.z) / cellSize.z), nz);
                int maxX = ClampCell(Mathf.FloorToInt((triMax.x - origin.x) / cellSize.x), nx);
                int maxY = ClampCell(Mathf.FloorToInt((triMax.y - origin.y) / cellSize.y), ny);
                int maxZ = ClampCell(Mathf.FloorToInt((triMax.z - origin.z) / cellSize.z), nz);

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            Vector3 center = origin + Vector3.Scale(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), cellSize);
                            if (TriangleIntersectsBox(a, b, c, center, cellSize * 0.5f))
                            {
                                occupied[Index(x, y, z, nx, ny)] = true;
                            }
                        }
                    }
                }
            }

            return MergeOccupiedCells(occupied, origin, cellSize, nx, ny, nz);
        }

        private static List<SiteBox.BakedBoxData> MergeOccupiedCells(
            bool[] occupied, Vector3 origin, Vector3 cellSize, int nx, int ny, int nz)
        {
            var used = new bool[occupied.Length];
            var boxes = new List<SiteBox.BakedBoxData>();

            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        int start = Index(x, y, z, nx, ny);
                        if (!occupied[start] || used[start])
                        {
                            continue;
                        }

                        int width = 1;
                        while (x + width < nx && IsFree(occupied, used, x + width, y, z, nx, ny))
                        {
                            width++;
                        }

                        int height = 1;
                        while (y + height < ny && RegionIsFree(occupied, used, x, y + height, z, width, 1, nx, ny))
                        {
                            height++;
                        }

                        int depth = 1;
                        while (z + depth < nz && RegionIsFree(occupied, used, x, y, z + depth, width, height, nx, ny))
                        {
                            depth++;
                        }

                        for (int dz = 0; dz < depth; dz++)
                        {
                            for (int dy = 0; dy < height; dy++)
                            {
                                for (int dx = 0; dx < width; dx++)
                                {
                                    used[Index(x + dx, y + dy, z + dz, nx, ny)] = true;
                                }
                            }
                        }

                        boxes.Add(new SiteBox.BakedBoxData
                        {
                            Center = origin + Vector3.Scale(
                                new Vector3(x + width * 0.5f, y + height * 0.5f, z + depth * 0.5f), cellSize),
                            Size = Vector3.Scale(new Vector3(width, height, depth), cellSize)
                        });

                        if (boxes.Count > MaxBoxesPerPart)
                        {
                            throw new InvalidOperationException(
                                "Получилось больше " + MaxBoxesPerPart + " боксов в одном меше. Увеличьте размер ячейки запекания.");
                        }
                    }
                }
            }

            return boxes;
        }

        private static bool RegionIsFree(
            bool[] occupied, bool[] used, int x, int y, int z, int width, int height, int nx, int ny)
        {
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    if (!IsFree(occupied, used, x + dx, y + dy, z, nx, ny))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsFree(bool[] occupied, bool[] used, int x, int y, int z, int nx, int ny)
        {
            int index = Index(x, y, z, nx, ny);
            return occupied[index] && !used[index];
        }

        private static bool TriangleIntersectsBox(Vector3 a, Vector3 b, Vector3 c, Vector3 center, Vector3 half)
        {
            a -= center;
            b -= center;
            c -= center;

            float boundsEpsilon = 1e-5f * Mathf.Max(1f, Mathf.Max(half.x, Mathf.Max(half.y, half.z)));
            half += Vector3.one * boundsEpsilon;
            if (Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > half.x ||
                Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < -half.x ||
                Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > half.y ||
                Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < -half.y ||
                Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > half.z ||
                Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < -half.z)
            {
                return false;
            }

            Vector3 edge0 = b - a;
            Vector3 edge1 = c - b;
            Vector3 edge2 = a - c;
            Vector3 xAxis = Vector3.right;
            Vector3 yAxis = Vector3.up;
            Vector3 zAxis = Vector3.forward;
            if (Separates(edge0, xAxis, a, b, c, half) || Separates(edge0, yAxis, a, b, c, half) ||
                Separates(edge0, zAxis, a, b, c, half) || Separates(edge1, xAxis, a, b, c, half) ||
                Separates(edge1, yAxis, a, b, c, half) || Separates(edge1, zAxis, a, b, c, half) ||
                Separates(edge2, xAxis, a, b, c, half) || Separates(edge2, yAxis, a, b, c, half) ||
                Separates(edge2, zAxis, a, b, c, half))
            {
                return false;
            }

            Vector3 normal = Vector3.Cross(edge0, c - a);
            return !SeparatesOnAxis(normal, a, b, c, half);
        }

        private static bool Separates(Vector3 edge, Vector3 axis, Vector3 a, Vector3 b, Vector3 c, Vector3 half)
        {
            return SeparatesOnAxis(Vector3.Cross(edge, axis), a, b, c, half);
        }

        private static bool SeparatesOnAxis(Vector3 axis, Vector3 a, Vector3 b, Vector3 c, Vector3 half)
        {
            float axisLengthSq = axis.sqrMagnitude;
            if (axisLengthSq < 1e-12f)
            {
                return false;
            }

            float pa = Vector3.Dot(a, axis);
            float pb = Vector3.Dot(b, axis);
            float pc = Vector3.Dot(c, axis);
            float min = Mathf.Min(pa, Mathf.Min(pb, pc));
            float max = Mathf.Max(pa, Mathf.Max(pb, pc));
            float radius = (half.x * Mathf.Abs(axis.x)) + (half.y * Mathf.Abs(axis.y)) + (half.z * Mathf.Abs(axis.z));
            float epsilon = 1e-6f * Mathf.Sqrt(axisLengthSq);
            return min > radius + epsilon || max < -radius - epsilon;
        }

        private static void FitBroadPhaseBounds(SiteBox site, List<SiteBox.BakedPartData> bakedParts)
        {
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool hasBounds = false;

            for (int i = 0; i < bakedParts.Count; i++)
            {
                SiteBox.BakedPartData part = bakedParts[i];
                if (part.Source == null)
                {
                    continue;
                }

                if (part.Boxes != null && part.Boxes.Count > 0)
                {
                    for (int j = 0; j < part.Boxes.Count; j++)
                    {
                        SiteBox.BakedBoxData box = part.Boxes[j];
                        Vector3 half = box.Size * 0.5f;
                        for (int corner = 0; corner < 8; corner++)
                        {
                            Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                                (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                            Vector3 world = part.Source.transform.TransformPoint(box.Center + Vector3.Scale(half, sign));
                            Vector3 local = site.transform.InverseTransformPoint(world);
                            min = Vector3.Min(min, local);
                            max = Vector3.Max(max, local);
                            hasBounds = true;
                        }
                    }
                }
                else if (part.UseConvexMesh && part.Source.sharedMesh != null)
                {
                    // Hull части = её собственный меш, габарит — mesh.bounds.
                    Bounds meshBounds = part.Source.sharedMesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 sign = new Vector3((corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                        Vector3 world = part.Source.transform.TransformPoint(
                            meshBounds.center + Vector3.Scale(meshBounds.extents, sign));
                        Vector3 local = site.transform.InverseTransformPoint(world);
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);
                        hasBounds = true;
                    }
                }
            }

            if (hasBounds)
            {
                site.Center = (min + max) * 0.5f;
                site.Size = max - min;
            }
        }

        private static Vector3Int GetDimensions(Bounds bounds, Vector3 cellSize)
        {
            return new Vector3Int(
                Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cellSize.x) + 2),
                Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / cellSize.y) + 2),
                Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cellSize.z) + 2));
        }

        private static long CellCount(Vector3Int dimensions)
        {
            return (long)dimensions.x * dimensions.y * dimensions.z;
        }

        private static int ClampCell(int value, int dimension)
        {
            return Mathf.Clamp(value, 0, dimension - 1);
        }

        private static int Index(int x, int y, int z, int nx, int ny)
        {
            return (z * ny + y) * nx + x;
        }
    }
}
