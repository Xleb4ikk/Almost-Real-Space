using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Процедурные кактусы для декора местности: низкополигональные «сагуаро»
    /// (ствол + 0..2 руки) из трубок с ручным расчётом нормалей. Текстура —
    /// бесшовная кожа кактуса (cactus.png), UV: вокруг ствола × длина.
    /// </summary>
    public static class CactusModelSetup
    {
        private const string DecorModelsFolder = "Assets/_Project/Models/Decor";
        private const int Segments = 10;

        public static Mesh[] CreateOrLoadCacti()
        {
            return new[] { BuildCactus(0), BuildCactus(1), BuildCactus(2) };
        }

        private static Mesh BuildCactus(int variant)
        {
            string path = DecorModelsFolder + "/Cactus" + (variant + 1) + ".asset";
            AssetDatabase.DeleteAsset(path);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            const float height = 3f;
            AppendTube(vertices, normals, uvs, triangles, VerticalSpine(height), 0.36f, 0.3f);

            if (variant >= 1)
            {
                AppendArm(vertices, normals, uvs, triangles, height * 0.42f, 1f, 0f, 1.1f);
            }

            if (variant >= 2)
            {
                AppendArm(vertices, normals, uvs, triangles, height * 0.58f, -1f, 0f, 1.3f);
                AppendArm(vertices, normals, uvs, triangles, height * 0.3f, 0f, 1f, 0.9f);
            }

            // Нормализация: высота ~1, основание на y=0.
            float maxExtent = 0.0001f;
            for (int i = 0; i < vertices.Count; i++)
            {
                maxExtent = Mathf.Max(maxExtent, Mathf.Max(Mathf.Abs(vertices[i].x), Mathf.Max(Mathf.Abs(vertices[i].z), vertices[i].y)));
            }

            float minY = float.MaxValue;
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] *= 1f / maxExtent;
                minY = Mathf.Min(minY, vertices[i].y);
            }

            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = new Vector3(vertices[i].x, vertices[i].y - minY, vertices[i].z);
                uvs[i] = new Vector2(uvs[i].x, uvs[i].y / maxExtent);
            }

            var mesh = new Mesh { name = "Cactus" + (variant + 1) };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static List<Vector3> VerticalSpine(float height)
        {
            var spine = new List<Vector3>();
            const int steps = 6;
            for (int i = 0; i <= steps; i++)
            {
                spine.Add(new Vector3(0f, height * i / steps, 0f));
            }

            return spine;
        }

        private static void AppendArm(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
            float baseY, float sideX, float sideZ, float length)
        {
            const int steps = 8;
            var spine = new List<Vector3>();
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float outAmount = Mathf.Sin(t * Mathf.PI * 0.5f) * length;
                float upAmount = (1f - Mathf.Cos(t * Mathf.PI * 0.5f)) * length * 0.9f;
                spine.Add(new Vector3(sideX * outAmount, baseY + upAmount, sideZ * outAmount));
            }

            AppendTube(vertices, normals, uvs, triangles, spine, 0.24f, 0.2f);
        }

        private static void AppendTube(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
            List<Vector3> spine, float radiusStart, float radiusEnd)
        {
            int steps = spine.Count;
            int ringStart = vertices.Count;
            var lengths = new float[steps];
            for (int i = 1; i < steps; i++)
            {
                lengths[i] = lengths[i - 1] + (spine[i] - spine[i - 1]).magnitude;
            }

            for (int i = 0; i < steps; i++)
            {
                Vector3 tangent = i < steps - 1
                    ? (spine[i + 1] - spine[i]).normalized
                    : (spine[i] - spine[i - 1]).normalized;
                Vector3 reference = Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.forward;
                Vector3 right = Vector3.Cross(reference, tangent).normalized;
                Vector3 up = Vector3.Cross(tangent, right);
                float radius = Mathf.Lerp(radiusStart, radiusEnd, i / (float)(steps - 1));
                for (int j = 0; j <= Segments; j++)
                {
                    float angle = j / (float)Segments * Mathf.PI * 2f;
                    Vector3 dir = (right * Mathf.Cos(angle)) + (up * Mathf.Sin(angle));
                    vertices.Add(spine[i] + (dir * radius));
                    normals.Add(dir);
                    uvs.Add(new Vector2(j / (float)Segments, lengths[i]));
                }
            }

            for (int i = 0; i < steps - 1; i++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int a = ringStart + (i * (Segments + 1)) + j;
                    int b = a + 1;
                    int c = a + (Segments + 1);
                    int d = c + 1;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            // Верхний купол (полюс по касательной) и нижняя заглушка.
            Vector3 topTangent = (spine[steps - 1] - spine[steps - 2]).normalized;
            int topRing = ringStart + ((steps - 1) * (Segments + 1));
            int topPole = vertices.Count;
            vertices.Add(spine[steps - 1] + (topTangent * (radiusEnd * 0.8f)));
            normals.Add(topTangent);
            uvs.Add(new Vector2(0.5f, lengths[steps - 1] + (radiusEnd * 0.8f)));
            for (int j = 0; j < Segments; j++)
            {
                triangles.Add(topRing + j);
                triangles.Add(topPole);
                triangles.Add(topRing + j + 1);
            }

            Vector3 bottomTangent = (spine[0] - spine[1]).normalized;
            int bottomPole = vertices.Count;
            vertices.Add(spine[0] + (bottomTangent * (radiusStart * 0.3f)));
            normals.Add(bottomTangent);
            uvs.Add(new Vector2(0.5f, -radiusStart * 0.3f));
            for (int j = 0; j < Segments; j++)
            {
                triangles.Add(ringStart + j + 1);
                triangles.Add(bottomPole);
                triangles.Add(ringStart + j);
            }
        }
    }
}
