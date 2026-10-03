using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Процедурные камни для декора местности: низкополигональные фасетные меши
    /// (икосаэдр + детерминированное смещение, приплюснутое основание) и
    /// процедурная тайловая текстура породы. Внешние модели не нужны.
    /// </summary>
    public static class RockModelSetup
    {
        private const string DecorModelsFolder = "Assets/_Project/Models/Decor";
        private const string DecorTexturesFolder = "Assets/_Project/Textures/Decor";

        public static Mesh[] CreateOrLoadRocks()
        {
            var rocks = new Mesh[3];
            for (int i = 0; i < rocks.Length; i++)
            {
                rocks[i] = CreateOrLoadRock(i);
            }

            return rocks;
        }

        /// <summary>Тайловая шумовая текстура породы (серые тона + тёмные трещины).</summary>
        public static Texture2D CreateOrLoadRockNoise()
        {
            string path = DecorTexturesFolder + "/rock_noise.png";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            const int size = 512;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float n = 0f;
                    float amplitude = 0.5f;
                    float frequency = 4f;
                    for (int o = 0; o < 5; o++)
                    {
                        n += amplitude * Mathf.PerlinNoise((u * frequency) + 13.7f, (v * frequency) + 7.3f);
                        amplitude *= 0.5f;
                        frequency *= 2.13f;
                    }

                    float shade = Mathf.Lerp(0.34f, 0.74f, n);
                    float cracks = Mathf.PerlinNoise((u * 18f) + 41.1f, (v * 18f) + 23.9f);
                    if (cracks < 0.32f)
                    {
                        shade *= 0.55f;
                    }

                    pixels[(y * size) + x] = new Color(shade, shade, shade * 1.02f, 1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Debug.Log("[RockModelSetup] создана " + path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Mesh CreateOrLoadRock(int variant)
        {
            string path = DecorModelsFolder + "/Rock" + (variant + 1) + ".asset";
            // Пересобираем каждый раз при сборке контента: старые битые ассеты
            // не должны переиспользоваться.
            AssetDatabase.DeleteAsset(path);

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            BuildIcosahedron(vertices, triangles);
            Subdivide(vertices, triangles);
            Subdivide(vertices, triangles);

            float seed = (variant + 1) * 17.31f;
            var displaced = new List<Vector3>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 d = vertices[i];
                float n = (0.26f * Mathf.Sin((3.1f * d.x) + (1.7f * d.y) + (0.9f * d.z) + seed))
                    + (0.19f * Mathf.Sin((5.3f * d.y) - (2.1f * d.z) + (1.3f * d.x) + (seed * 2.3f)))
                    + (0.12f * Mathf.Sin((8.7f * d.z) + (4.3f * d.x) + (seed * 3.7f)));
                displaced.Add(d * (1f + n));
            }

            // Фасетная нарезка: вершины дублируются по треугольникам —
            // RecalculateNormals даёт плоские грани (низкополигональный вид).
            var flatVertices = new List<Vector3>(triangles.Count);
            var flatUvs = new List<Vector2>(triangles.Count);
            for (int i = 0; i < triangles.Count; i++)
            {
                Vector3 p = displaced[triangles[i]];
                flatVertices.Add(p);
                Vector3 n = p.normalized;
                flatUvs.Add(new Vector2(
                    (Mathf.Atan2(n.z, n.x) / (2f * Mathf.PI)) + 0.5f,
                    (Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) / Mathf.PI) + 0.5f));
            }

            // Нормализация: габарит ~1 м, основание приплюснуто и на y=0.
            float maxExtent = 0.0001f;
            for (int i = 0; i < flatVertices.Count; i++)
            {
                Vector3 p = flatVertices[i];
                maxExtent = Mathf.Max(maxExtent, Mathf.Max(Mathf.Abs(p.x), Mathf.Max(Mathf.Abs(p.z), p.y)));
            }

            float scale = 1f / maxExtent;
            float minY = float.MaxValue;
            for (int i = 0; i < flatVertices.Count; i++)
            {
                Vector3 p = flatVertices[i] * scale;
                minY = Mathf.Min(minY, p.y);
                flatVertices[i] = p;
            }

            float height = 0f;
            for (int i = 0; i < flatVertices.Count; i++)
            {
                flatVertices[i] = new Vector3(flatVertices[i].x, flatVertices[i].y - minY, flatVertices[i].z);
                height = Mathf.Max(height, flatVertices[i].y);
            }

            float flattenBelow = height * 0.22f;
            for (int i = 0; i < flatVertices.Count; i++)
            {
                Vector3 p = flatVertices[i];
                if (p.y < flattenBelow)
                {
                    p.y = flattenBelow + ((p.y - flattenBelow) * 0.35f);
                }

                flatVertices[i] = p;
            }

            var mesh = new Mesh { name = "Rock" + (variant + 1) };
            mesh.SetVertices(flatVertices);
            mesh.SetUVs(0, flatUvs);
            // Фасетные вершины не индексируются: индексы треугольников —
            // последовательные (0,1,2,...), а не исходного икосаэдра.
            var flatTriangles = new List<int>(flatVertices.Count);
            for (int i = 0; i < flatVertices.Count; i++)
            {
                flatTriangles.Add(i);
            }

            mesh.SetTriangles(flatTriangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void BuildIcosahedron(List<Vector3> vertices, List<int> triangles)
        {
            float phi = (1f + Mathf.Sqrt(5f)) * 0.5f;
            vertices.Add(new Vector3(-1f, phi, 0f).normalized);
            vertices.Add(new Vector3(1f, phi, 0f).normalized);
            vertices.Add(new Vector3(-1f, -phi, 0f).normalized);
            vertices.Add(new Vector3(1f, -phi, 0f).normalized);
            vertices.Add(new Vector3(0f, -1f, phi).normalized);
            vertices.Add(new Vector3(0f, 1f, phi).normalized);
            vertices.Add(new Vector3(0f, -1f, -phi).normalized);
            vertices.Add(new Vector3(0f, 1f, -phi).normalized);
            vertices.Add(new Vector3(phi, 0f, -1f).normalized);
            vertices.Add(new Vector3(phi, 0f, 1f).normalized);
            vertices.Add(new Vector3(-phi, 0f, -1f).normalized);
            vertices.Add(new Vector3(-phi, 0f, 1f).normalized);
            triangles.AddRange(new[]
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            });
        }

        private static void Subdivide(List<Vector3> vertices, List<int> triangles)
        {
            var cache = new Dictionary<long, int>();
            var next = new List<int>(triangles.Count * 4);
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                int ab = Midpoint(vertices, cache, a, b);
                int bc = Midpoint(vertices, cache, b, c);
                int ca = Midpoint(vertices, cache, c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            triangles.Clear();
            triangles.AddRange(next);
        }

        private static int Midpoint(List<Vector3> vertices, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int index))
            {
                return index;
            }

            vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
            index = vertices.Count - 1;
            cache[key] = index;
            return index;
        }
    }
}
