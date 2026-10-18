using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Builds the tree layer for ground decor: meshes from Models/Tree
    /// (tree1..tree3), the Galilego/GroundDecorSolid material and merging model
    /// parts into one mesh with a per-submesh wind mask. Used by GroundDecorSetup.
    /// </summary>
    public static class TreeModelSetup
    {
        private const string TreesFolder = "Assets/_Project/Models/Tree";
        private const string DecorModelsFolder = "Assets/_Project/Models/Decor";
        private const string MaterialsFolder = "Assets/_Project/Materials";
        private const string DecorMaterialsFolder = MaterialsFolder + "/Decor";
        private const string MaterialPath = DecorMaterialsFolder + "/TreeSolid.mat";
        private const float TargetTreeHeightMeters = 15f;

        /// <summary>Bark: Blender material Bark colour; alpha 0 — wind does not bend the trunk.</summary>
        private static readonly Color TrunkVertexColor = new Color(0.55f, 0.18f, 0.03f, 0f);

        /// <summary>Foliage: the stylised green; alpha 1 — bends in the wind.</summary>
        private static readonly Color LeafVertexColor = new Color(0.01034f, 0.06838f, 0.01848f, 1f);

        /// <summary>
        /// Trees layer: meshes from Models/Tree, colour material, bbox axial
        /// offset (the FBX pivot may be centred — raise it so the trunk stands
        /// on the ground). Also used by GroundDecorSetup.
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

            // Meshes are already normalised (bottom at y=0); no axial offset needed.
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
                MaxInstancesPerChunk = 800,
                MaxCellsPerAxis = 128,
                // Density rather than MaxInstancesPerChunk: the per-chunk limit only cuts
                // distant chunks (~1300 candidates near), so density would differ between them.
                Density = 0.25d,
                DistributionFrequency = 150d,
                DistributionOctaves = 4,
                DistributionSeedOffset = 3,
                ClusterThreshold = 0d,
                MinAltitudeMeters = 5d,
                // Sand is only the absolute beach (terrain profile BeachHeightMeters,
                // ~18 m); greenery begins above it. No normalised lower bound.
                MinNormalizedHeight = 0d,
                MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
                MaxAltitudeMeters = GroundDecorSetup.TreeMaxAltitudeMeters,
                MaxSlopeTan = 0.9d,
                AvoidWater = true,
                // Trees avoid only extreme desert; dry steppe is forested. WetFade gives a
                // soft edge: a hard threshold draws a visible "forest ends here" line.
                WetMin = 0.2d,
                WetMax = 1d,
                WetFade = 0.2d,
                MinScale = baseScale * 0.85f,
                MaxScale = baseScale * 1.5f,
                // Slope attenuation is soft (^1.5).
                SteepPower = 1.5d,
                GroundOffsetMeters = groundOffset,
                Collides = true,
                CollisionRadiusMeters = 1.2d,
                CollisionHeightMeters = 16d,
                // [GRAPHICS] Tree draw distance. Past ProxyFromMeters a cheap proxy is
                // used (see FarProxy). The real limit is also the horizon.
                NearDistanceMeters = 150f,
                MaxDistanceMeters = 11200f,
                FarDensity = 0.35d,
                FarProxy = true,
                ProxyFromMeters = 400f,
                ProxyScaleBoost = 2.5f,
                ProxyBoostStartMeters = 1500f,
                HorizonObjectHeightMeters = 200f
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
            // Trunk/leaf colour is baked into vertex colors (see LoadCombinedMesh), so the
            // material stays white. _VertexColorTint tints trees only; other decor is unchanged.
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_VertexColorTint", 1f);
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

        /// <summary>Model helper meshes (LODBox collision boxes etc.) are not rendered.</summary>
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
        /// Combines ALL parts of the model (trunk + foliage) into one mesh: the FBX
        /// stores them as separate objects. Uses a prefab instance so local part
        /// transforms are taken into account.
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
                // true — parts may be disabled in the FBX (variants/canopy);
                // without includeInactive they are lost.
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

                    // Vertex color: RGB — part colour (trunk/foliage), alpha — wind
                    // mask. The GroundDecorSolid shader tints albedo by RGB and bends
                    // by alpha.
                    Mesh temp = Object.Instantiate(mesh);
                    MeshRenderer partRenderer = filters[i].GetComponent<MeshRenderer>();
                    Material[] partMaterials = partRenderer != null ? partRenderer.sharedMaterials : null;
                    var colors = new Color[temp.vertexCount];
                    // Default is trunk (not bent). Only vertices of leaf submeshes
                    // and materials are marked as foliage.
                    for (int v = 0; v < colors.Length; v++)
                    {
                        colors[v] = TrunkVertexColor;
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
                                        colors[vertex] = LeafVertexColor;
                                    }
                                }
                            }
                            catch (System.Exception)
                            {
                                // Unreadable submesh — stays trunk.
                            }
                        }
                    }

                    if (!hasMaterials && IsLeafPart(mesh.name))
                    {
                        // No materials — classify by mesh name.
                        for (int v = 0; v < colors.Length; v++)
                        {
                            colors[v] = LeafVertexColor;
                        }
                    }

                    try
                    {
                        temp.colors = colors;
                    }
                    catch (System.Exception)
                    {
                        // Read-only mesh — the wind mask is not critical.
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

                    // Single part: keep the clone as is; the wind mask is already
                    // in vertex colors.
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
                                // Unreadable mesh — keep local coordinates.
                            }
                        }

                        NormalizeMeshToGround(single);
                        string singlePath = DecorModelsFolder + "/" + prefab.name + "_Combined.asset";
                        AssetDatabase.DeleteAsset(singlePath);
                        AssetDatabase.CreateAsset(single, singlePath);
                        return single;
                    }

                    var combined = new Mesh { name = prefab.name + "_Combined" };
                    // mergeSubMeshes = false keeps the submeshes separate.
                    combined.CombineMeshes(combines.ToArray(), false, true);
                    // Normals after combining can be wrong (parts with different
                    // matrices) — recalculate.
                    combined.RecalculateNormals();
                    combined.RecalculateBounds();

                    // The pivot may not be at the base (e.g. a canopy icosphere centred
                    // on itself) — shift the geometry so the bottom is at y=0: every
                    // model then stands on the ground without a shared offset.
                    NormalizeMeshToGround(combined);

                    if (combined.vertexCount < sourceVertexCount)
                    {
                        // CombineMeshes dropped geometry — fall back to the source mesh:
                        // no vertex mask, but the canopy is intact.
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

        /// <summary>Shift the geometry so the mesh bottom is at y=0.</summary>
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

        /// <summary>Foliage (bends in the wind) or trunk/bark (does not bend).</summary>
        private static bool IsLeafPart(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower.Contains("leaf") || lower.Contains("leaves") || lower.Contains("ico")
                || lower.Contains("canopy") || lower.Contains("crown") || lower.Contains("sphere")
                || lower.Contains("top") || lower.Contains("branch");
        }
    }
}
