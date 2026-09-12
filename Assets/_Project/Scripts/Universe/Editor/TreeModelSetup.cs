using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Сборка слоя деревьев для декора местности: меши из Models/Tree
    /// (tree1..tree3), материал Galilego/GroundDecorSolid и объединение частей
    /// модели в один меш с маской ветра по сабмешам. Используется GroundDecorSetup.
    /// </summary>
    public static class TreeModelSetup
    {
        private const string TreesFolder = "Assets/_Project/Models/Tree";
        private const string DecorModelsFolder = "Assets/_Project/Models/Decor";
        private const string MaterialsFolder = "Assets/_Project/Materials";
        private const string DecorMaterialsFolder = MaterialsFolder + "/Decor";
        private const string MaterialPath = DecorMaterialsFolder + "/TreeSolid.mat";
        private const float TargetTreeHeightMeters = 15f;

        /// <summary>
        /// Слой деревьев: меши из Models/Tree, цвет-материал, осевой офсет по
        /// bbox (пивот FBX может быть в центре — поднимаем, чтобы ствол стоял
        /// на земле). Используется и GroundDecorSetup.
        /// </summary>
        internal static GroundDecorLayer BuildTreesLayer()
        {
            Material material = CreateOrLoadMaterial();
            Mesh[] meshes = LoadTreeMeshes();
            if (material == null || meshes.Length == 0)
            {
                return null;
            }

            float maxHeight = 0f;
            for (int i = 0; i < meshes.Length; i++)
            {
                maxHeight = Mathf.Max(maxHeight, meshes[i].bounds.size.y);
            }

            // Меши уже нормализованы (низ на y=0), осевой офсет не нужен.
            float baseScale = maxHeight > 0.0001f ? TargetTreeHeightMeters / maxHeight : 1f;
            const float groundOffset = 0f;

            return new GroundDecorLayer
            {
                Name = "Trees",
                Enabled = true,
                CastShadows = true,
                NearMeshes = meshes,
                FarBillboardMesh = null,
                NearMaterial = material,
                FarMaterial = null,
                SpacingMeters = 12d,
                MaxInstancesPerChunk = 600,
                MaxCellsPerAxis = 128,
                Density = 1.0d,
                DistributionFrequency = 150d,
                DistributionOctaves = 4,
                DistributionSeedOffset = 3,
                ClusterThreshold = 0d,
                MinAltitudeMeters = 5d,
                // Верхняя граница леса — общая с травой (см. TreeLineMaxAltitudeMeters).
                MaxAltitudeMeters = GroundDecorSetup.TreeLineMaxAltitudeMeters,
                MaxSlopeTan = 0.9d,
                AvoidWater = true,
                WetMin = 0.1d,
                WetMax = 1d,
                MinScale = baseScale * 0.85f,
                MaxScale = baseScale * 1.5f,
                SteepPower = 3d,
                GroundOffsetMeters = groundOffset,
                Collides = true,
                CollisionRadiusMeters = 1.2d,
                CollisionHeightMeters = 16d,
                // [ГРАФИКА] дальность деревьев (near/far) и плотность.
                NearDistanceMeters = 150f,
                MaxDistanceMeters = 3000f
            };
        }

        private static Material CreateOrLoadMaterial()
        {
            Shader shader = Shader.Find("Galilego/GroundDecorSolid");
            if (shader == null)
            {
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", new Color(0.30f, 0.44f, 0.20f, 1f));
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_WindStrength", 0.05f);
            material.SetFloat("_WindSpeed", 1.1f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh[] LoadTreeMeshes()
        {
            var result = new List<Mesh>();
            string[] guids = AssetDatabase.FindAssets("tree t:Model");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.StartsWith(TreesFolder, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Mesh mesh = LoadCombinedMesh(path);
                if (mesh != null)
                {
                    result.Add(mesh);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result.ToArray();
        }

        private static Mesh LoadBiggestMesh(string modelPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            Mesh best = null;
            Mesh bestNonHelper = null;
            for (int i = 0; i < assets.Length; i++)
            {
                if (!(assets[i] is Mesh mesh))
                {
                    continue;
                }

                if (best == null || mesh.vertexCount > best.vertexCount)
                {
                    best = mesh;
                }

                if (!IsHelperMesh(mesh.name) && (bestNonHelper == null || mesh.vertexCount > bestNonHelper.vertexCount))
                {
                    bestNonHelper = mesh;
                }
            }

            return bestNonHelper != null ? bestNonHelper : best;
        }

        /// <summary>Служебные меши моделей (коллизионные боксы LODBox и т.п.) в рендер не берём.</summary>
        private static bool IsHelperMesh(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            return lower.Contains("lodbox") || lower.Contains("collider") || lower.Contains("collision")
                || lower.Contains("collisionbox") || lower.Contains("bounds");
        }

        /// <summary>
        /// Собрать ВСЕ части модели (ствол + листва) в один меш: FBX хранит их
        /// отдельными объектами, а раньше брался только самый крупный — деревья
        /// получались «палками» без кроны. Комбайн — через инстанс префаба,
        /// чтобы учесть локальные трансформы частей.
        /// </summary>
        private static Mesh LoadCombinedMesh(string modelPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null)
            {
                return LoadBiggestMesh(modelPath);
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                return LoadBiggestMesh(modelPath);
            }

            try
            {
                // true — части могут быть выключены в FBX (варианты/крона),
                // без includeInactive они теряются (tree3 без листвы).
                MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
                var combines = new List<CombineInstance>();
                var tempMeshes = new List<Mesh>();
                var partTransforms = new List<Transform>();
                int sourceVertexCount = 0;
                for (int i = 0; i < filters.Length; i++)
                {
                    Mesh mesh = filters[i].sharedMesh;
                    if (mesh == null || IsHelperMesh(mesh.name))
                    {
                        continue;
                    }

                    // Копия с vertex color = маска ветра: 1 у листвы, 0 у ствола.
                    // Маску ставим ПО САБМЕШАМ: у моделей с одной меш-оболочкой
                    // ствол и крона — разные материалы (Bark/Leaf), и если брать
                    // только имя меша, крона помечается как ствол и не гнётся.
                    Mesh temp = Object.Instantiate(mesh);
                    MeshRenderer partRenderer = filters[i].GetComponent<MeshRenderer>();
                    Material[] partMaterials = partRenderer != null ? partRenderer.sharedMaterials : null;
                    var colors = new Color[temp.vertexCount];
                    // По умолчанию всё — ствол (не гнётся). Листвой помечаем
                    // ТОЛЬКО вершины leaf-сабмешей/материалов: у tree3 ствол и
                    // крона в одном меше, и маска по имени меша гнула всё.
                    for (int v = 0; v < colors.Length; v++)
                    {
                        colors[v] = Color.black;
                    }

                    bool hasMaterials = partMaterials != null && partMaterials.Length > 0;
                    if (hasMaterials)
                    {
                        int submeshes = Mathf.Max(1, temp.subMeshCount);
                        for (int s = 0; s < submeshes; s++)
                        {
                            string materialName = s < partMaterials.Length && partMaterials[s] != null
                                ? partMaterials[s].name
                                : string.Empty;
                            if (!IsLeafPart(materialName))
                            {
                                continue;
                            }

                            try
                            {
                                int[] triangles = temp.GetTriangles(s);
                                for (int t = 0; t < triangles.Length; t++)
                                {
                                    int vertex = triangles[t];
                                    if (vertex >= 0 && vertex < colors.Length)
                                    {
                                        colors[vertex] = Color.white;
                                    }
                                }
                            }
                            catch (System.Exception)
                            {
                                // Нечитаемый сабмеш — остаётся стволом.
                            }
                        }
                    }

                    if (!hasMaterials && IsLeafPart(mesh.name))
                    {
                        // Материалов нет — классифицируем по имени меша.
                        for (int v = 0; v < colors.Length; v++)
                        {
                            colors[v] = Color.white;
                        }
                    }

                    try
                    {
                        temp.colors = colors;
                    }
                    catch (System.Exception)
                    {
                        // Меш только для чтения — маска ветра не критична.
                    }

                    tempMeshes.Add(temp);
                    partTransforms.Add(filters[i].transform);
                    sourceVertexCount += mesh.vertexCount;

                    combines.Add(new CombineInstance
                    {
                        mesh = temp,
                        transform = instance.transform.worldToLocalMatrix * filters[i].transform.localToWorldMatrix
                    });
                }

                try
                {
                    if (combines.Count == 0)
                    {
                        return LoadBiggestMesh(modelPath);
                    }

                    // Одна деталь (tree3): CombineMeshes терял сабмеш кроны —
                    // сохраняем клон как есть; маска ветра уже в vertex colors.
                    if (tempMeshes.Count == 1)
                    {
                        Mesh single = tempMeshes[0];
                        tempMeshes.Clear();
                        single.name = prefab.name + "_Combined";

                        Matrix4x4 partMatrix =
                            instance.transform.worldToLocalMatrix * partTransforms[0].localToWorldMatrix;
                        if (!partMatrix.isIdentity)
                        {
                            try
                            {
                                Vector3[] singleVertices = single.vertices;
                                for (int v = 0; v < singleVertices.Length; v++)
                                {
                                    singleVertices[v] = partMatrix.MultiplyPoint3x4(singleVertices[v]);
                                }

                                single.vertices = singleVertices;

                                Vector3[] singleNormals = single.normals;
                                for (int v = 0; v < singleNormals.Length; v++)
                                {
                                    singleNormals[v] = partMatrix.MultiplyVector(singleNormals[v]).normalized;
                                }

                                single.normals = singleNormals;
                                single.RecalculateBounds();
                            }
                            catch (System.Exception)
                            {
                                // Нечитаемый меш — оставляем локальные координаты.
                            }
                        }

                        NormalizeMeshToGround(single);
                        string singlePath = DecorModelsFolder + "/" + prefab.name + "_Combined.asset";
                        AssetDatabase.DeleteAsset(singlePath);
                        AssetDatabase.CreateAsset(single, singlePath);
                        return single;
                    }

                    var combined = new Mesh { name = prefab.name + "_Combined" };
                    // mergeSubMeshes = false: с true CombineMeshes у модели
                    // tree3 терял половину вершин (крона-сабмеш пропадала).
                    combined.CombineMeshes(combines.ToArray(), false, true);
                    // Нормали после комбайна могут быть кривыми (части с разными
                    // матрицами) — чёрные грани. Пересчитываем.
                    combined.RecalculateNormals();
                    combined.RecalculateBounds();

                    // Пивот может быть не в основании (например, крона-икосфера
                    // с центром в себе) — сдвигаем геометрию так, чтобы низ был
                    // на y=0: все модели стоят на земле без общего офсета.
                    NormalizeMeshToGround(combined);

                    if (combined.vertexCount < sourceVertexCount)
                    {
                        // CombineMeshes потерял часть геометрии (tree3: крона-
                        // сабмеш). Берём исходный меш целиком: без vertex-маски
                        // (ветер гнёт дерево как раньше), но крона на месте.
                        Debug.LogWarning("[TreeMesh] " + prefab.name + ": потеря вершин при комбайне — исходный меш.");
                        Object.DestroyImmediate(combined);
                        return LoadBiggestMesh(modelPath);
                    }

                    string meshPath = DecorModelsFolder + "/" + prefab.name + "_Combined.asset";
                    AssetDatabase.DeleteAsset(meshPath);
                    AssetDatabase.CreateAsset(combined, meshPath);
                    return combined;
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning("[TreeModelSetup] не удалось собрать " + modelPath + ": " + exception.Message);
                    return LoadBiggestMesh(modelPath);
                }
                finally
                {
                    for (int i = 0; i < tempMeshes.Count; i++)
                    {
                        Object.DestroyImmediate(tempMeshes[i]);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Сдвинуть геометрию так, чтобы низ меша был на y=0.</summary>
        private static void NormalizeMeshToGround(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            float minY = mesh.bounds.min.y;
            if (Mathf.Abs(minY) <= 1e-4f)
            {
                return;
            }

            for (int v = 0; v < vertices.Length; v++)
            {
                vertices[v].y -= minY;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        /// <summary>Листва (гнётся ветром) или ствол/кора (не гнётся).</summary>
        private static bool IsLeafPart(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower.Contains("leaf") || lower.Contains("leaves") || lower.Contains("ico")
                || lower.Contains("canopy") || lower.Contains("crown") || lower.Contains("sphere")
                || lower.Contains("top") || lower.Contains("branch");
        }
    }
}
