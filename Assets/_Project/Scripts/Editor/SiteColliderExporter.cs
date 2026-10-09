using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Galilego.Universe;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Экспорт коллайдеров запечённого места в JSON для стенда (фаза 6):
    /// выпуклые части — облака вершин, вогнутые — воксельные боксы, всё в локальном
    /// кадре корня префаба (Y — зенит). Стенд строит по этому файлу SiteColliderSet.
    /// </summary>
    public static class SiteColliderExporter
    {
        private const string OutputPath = "Tools/P1bTests/Data/site_1_colliders.json";

        [MenuItem("Galilego/Test Vessel/Export Site Colliders (JSON)")]
        public static void ExportMenu()
        {
            Debug.Log(Export());
        }

        public static string Export()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Sites" });
            if (guids.Length == 0)
            {
                return "Экспорт: префаб места не найден в Assets/_Project/Sites";
            }

            string prefabPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return "Экспорт: префаб не загрузился: " + prefabPath;
            }

            var culture = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(1 << 18);
            sb.Append("{\n");
            sb.Append("  \"name\": \"").Append(prefab.name).Append("\",\n");
            sb.Append("  \"hulls\": [\n");

            bool firstHull = true;
            foreach (SiteBox box in prefab.GetComponentsInChildren<SiteBox>(true))
            {
                foreach (SiteBox.BakedPartData part in box.BakedParts)
                {
                    if (part == null || part.Source == null || !part.UseConvexMesh)
                    {
                        continue;
                    }

                    Mesh mesh = part.Source.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    Vector3[] vertices = mesh.vertices;
                    if (vertices.Length == 0)
                    {
                        continue;
                    }

                    if (!firstHull)
                    {
                        sb.Append(",\n");
                    }

                    firstHull = false;
                    sb.Append("    { \"name\": \"").Append(part.Source.name).Append("\", \"points\": [");
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 local = prefab.transform.InverseTransformPoint(part.Source.transform.TransformPoint(vertices[i]));
                        if (i > 0)
                        {
                            sb.Append(", ");
                        }

                        sb.Append('[')
                            .Append(local.x.ToString("G17", culture)).Append(", ")
                            .Append(local.y.ToString("G17", culture)).Append(", ")
                            .Append(local.z.ToString("G17", culture)).Append(']');
                    }

                    sb.Append("] }");
                }
            }

            sb.Append("\n  ],\n");
            sb.Append("  \"boxes\": [\n");

            bool firstBox = true;
            foreach (SiteBox box in prefab.GetComponentsInChildren<SiteBox>(true))
            {
                foreach (SiteBox.BakedPartData part in box.BakedParts)
                {
                    if (part == null || part.Source == null || part.UseConvexMesh || part.Boxes == null)
                    {
                        continue;
                    }

                    Vector3 scale = part.Source.transform.lossyScale;
                    Vector3 absScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    Quaternion rotationInRoot = Quaternion.Inverse(prefab.transform.rotation) * part.Source.transform.rotation;
                    for (int i = 0; i < part.Boxes.Count; i++)
                    {
                        SiteBox.BakedBoxData data = part.Boxes[i];
                        if (data.Size.x <= 0f || data.Size.y <= 0f || data.Size.z <= 0f)
                        {
                            continue;
                        }

                        Vector3 center = prefab.transform.InverseTransformPoint(part.Source.transform.TransformPoint(data.Center));
                        Vector3 half = new Vector3(
                            0.5f * data.Size.x * absScale.x,
                            0.5f * data.Size.y * absScale.y,
                            0.5f * data.Size.z * absScale.z);

                        if (!firstBox)
                        {
                            sb.Append(",\n");
                        }

                        firstBox = false;
                        sb.Append("    { \"name\": \"").Append(part.Source.name).Append("\", \"center\": [")
                            .Append(center.x.ToString("G17", culture)).Append(", ")
                            .Append(center.y.ToString("G17", culture)).Append(", ")
                            .Append(center.z.ToString("G17", culture)).Append("], \"half\": [")
                            .Append(half.x.ToString("G17", culture)).Append(", ")
                            .Append(half.y.ToString("G17", culture)).Append(", ")
                            .Append(half.z.ToString("G17", culture)).Append("], \"rotation\": [")
                            .Append(rotationInRoot.x.ToString("G17", culture)).Append(", ")
                            .Append(rotationInRoot.y.ToString("G17", culture)).Append(", ")
                            .Append(rotationInRoot.z.ToString("G17", culture)).Append(", ")
                            .Append(rotationInRoot.w.ToString("G17", culture)).Append("] }");
                    }
                }
            }

            sb.Append("\n  ]\n}\n");

            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputPath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(false));
            return "Экспорт: " + fullPath + " (" + sb.Length + " символов)";
        }
    }
}
