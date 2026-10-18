using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Near grass LOD assembly: a procedural low-poly blade (folded strip) plus
    /// the dedicated Galilego/GrassBlade shader.
    ///
    /// Cull Back is FORBIDDEN on this mesh: a single-sided wing with Cull Off is
    /// visible from both sides, while back-face culling would cut half the blades
    /// at half the view angles.
    /// </summary>
    public static class GrassModelSetup
    {
        private const string DecorModelsFolder = "Assets/_Project/Models/Decor";
        private const string MaterialsFolder = "Assets/_Project/Materials";
        private const string DecorMaterialsFolder = MaterialsFolder + "/Decor";
        private const string MaterialPath = DecorMaterialsFolder + "/GrassBlade.mat";
        private const string FixedMeshPath = DecorModelsFolder + "/GrassBladeFixed.asset";
        private const string FarMeshPath = DecorModelsFolder + "/GrassBladeFar.asset";

        /// <summary>Ground texture for grass auto-tint (average pixel colour).</summary>
        private const string GroundTexturePath = "Assets/_Project/Textures/Terrain/terrain_mid_clean.jpg";

        private const float TargetMinHeightMeters = 1.0f;
        private const float TargetMaxHeightMeters = 2.08f;

        // Blade geometry: base width 0.34 m, fold depth 0.12 m, height 1.58 m.
        // The layer's MinScale/MaxScale are relative to mesh height, so the layer
        // plants blades 1.0...2.08 m tall.
        private const float BladeBaseWidth = 0.34f;
        private const float BladeTipWidth = 0.09f;
        private const float BladeHeight = 1.58f;
        private const float BladeFoldDepth = 0.12f;
        private const int BladePanels = 3;

        /// <summary>
        /// Grass layer with a near 3D blade and a cheap far LOD (single quad).
        /// The renderer turns the far quad towards the camera (GroundDecorMatrixJob,
        /// Billboard = !near), so one quad suffices for the whole far half.
        /// </summary>
        internal static GroundDecorLayer BuildGrassLayer(Mesh farBillboard, Material farMaterial)
        {
            Material material = CreateOrLoadMaterial();
            Mesh blade = BuildBladeMesh();
            Mesh bladeFar = BuildFarMesh();
            if (material == null || blade == null || bladeFar == null)
            {
                return null;
            }

            // Single blade per instance: an island is assembled from MANY
            // individual blades on a grid, giving a dense even carpet.
            Bounds bounds = blade.bounds;
            float meshHeight = Mathf.Max(1e-4f, bounds.size.y);
            Debug.Log("[GrassModelSetup] GrassBladeFixed bounds.size=" + bounds.size
                + " tris=" + blade.triangles.Length / 3
                + " (высота меша как есть: " + meshHeight + " м)");

            float groundOffset = 0f;
            if (Mathf.Abs(bounds.min.y) > 1e-4f)
            {
                groundOffset = -bounds.min.y * (TargetMinHeightMeters / meshHeight);
                Debug.LogWarning("[GrassModelSetup] низ меша не на y=0 — GroundOffsetMeters=" + groundOffset);
            }

            // Tint the far LOD the same as the near one: otherwise grass pops
            // from dark to bright green at NearDistance.
            Color tint = material.GetColor("_BaseColor");
            if (farMaterial != null)
            {
                farMaterial.SetColor("_BaseColor", tint);
                EditorUtility.SetDirty(farMaterial);
            }

            return new GroundDecorLayer
            {
                Name = "Grass",
                Enabled = true,
                CastShadows = false,
                ShadowCastDistanceMeters = 0f,
                NearMeshes = new[] { blade },
                NearMaterial = material,
                // Far LOD is ONE quad (2 triangles) instead of a blade: beyond
                // NearDistance it faces the camera, so fold detail is unreadable
                // anyway. The near>far transition must be invisible: the quad
                // repeats the blade silhouette (same base width and height).
                FarBillboardMesh = bladeFar,
                FarMaterial = material,
                SpacingMeters = 0.8d,
                MaxInstancesPerChunk = 26000,
                Density = 4.0d,
                DistributionFrequency = 20000d,
                ClusterPatchMeters = 120d,
                DistributionOctaves = 4,
                DistributionSeedOffset = 1,
                ClusterThreshold = 0.36d,
                ClusterFade = 0.12d,
                MinAltitudeMeters = 2d,
                // Sand is only the absolute beach (terrain profile
                // BeachHeightMeters); grass has no normalized lower bound.
                MinNormalizedHeight = 0d,
                // Top of the palette's green: above t=0.45 the terrain is rock,
                // so only stones there. WetMin is where Grass starts in the palette.
                MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
                MaxAltitudeMeters = GroundDecorSetup.GrassMaxAltitudeMeters,
                MaxSlopeTan = 2.8d,
                AvoidWater = true,
                // Blade carpet on any land at green heights, including dry
                // steppe; sand, rock, snow, beach, ice and water still cut it
                // off. Otherwise dry hills with a green photo are bald.
                WetMin = 0d,
                WetMax = 1d,
                WetFade = 0.12d,
                MinScale = TargetMinHeightMeters / meshHeight,
                MaxScale = TargetMaxHeightMeters / meshHeight,
                SteepPower = 0.4d,
                GroundOffsetMeters = groundOffset,
                WindZoneFrequency = 2.5d,
                WindZoneOctaves = 3,
                WindZoneSeedOffset = 20,
                WindLeanMinDegrees = 10d,
                WindLeanMaxDegrees = 20d,
                WindJitterDegrees = 12d,
                MinGroundSinkFactor = 0d,
                MaxGroundSinkFactor = 0.5d,
                // [GRAPHICS] DISTANCE AND COVERAGE LAW.
                //
                // Coverage law: density ∝ (R/d)² while the far quad size ∝ d
                // (GroundDecorMatrixJob, DistanceCore + BillboardFarScale). The
                // screen fill n·g² stays constant up to MaxDistanceMeters, and the
                // total instance count grows like ln(R) instead of R² — going twice
                // as far costs markedly fewer instances than constant density.
                // Power = 2 maintains coverage exactly. Above 2 is sparser
                // (cheaper, fainter far zone), below 2 is more expensive.
                NearDistanceMeters = 35f,
                MaxDistanceMeters = 200f,
                // FarDensity = 0: no grass at the far boundary, otherwise the
                // last 200 m would hold an extra layer of instances.
                FarDensity = 0d,
                // With Power > 0, DensityFalloffMeters is unused by the density
                // profile but still feeds R/keepRadius at build time.
                DensityFalloffMeters = 25f,
                DensityCoreMeters = 20f,
                DensityFalloffPower = 2f,
                // [BARESPOTS] Rebuild threshold = core − lookAhead, floored at
                // DecorMinRebuildMeters = 6 m: with core 20 full density is
                // guaranteed 14 m ahead. Raise the core if births near the camera
                // stay high.
                DecorLookAheadMeters = 25f,
                DecorMinRebuildMeters = 6f,
                PerInstanceDensity = true,
                SpawnMarginMeters = 100f,
                SubInstancesPerCell = 400,
                MaxCellsPerAxis = 192
            };
        }

        private static Material CreateOrLoadMaterial()
        {
            Shader shader = Shader.Find("Galilego/GrassBlade");
            if (shader == null)
            {
                Debug.LogError("[GrassModelSetup] нет шейдера Galilego/GrassBlade — трава не будет создана.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", SampleGroundTint(GroundTexturePath));
            material.SetFloat("_TwoSided", 1f);
            material.SetFloat("_ShadowMul", 1f);
            // Blend towards the ground colour. Density starts dropping at the
            // core edge, so the blend covers exactly the zone where grass got
            // sparse: 55...185 m with MaxDistanceMeters = 200.
            material.SetFloat("_FarFadeStart", 55f);
            material.SetFloat("_FarFadeEnd", 185f);
            material.SetFloat("_FarFadeStrength", 1f);
            material.SetFloat("_WindStrength", 0.15f);
            material.SetFloat("_WindSpeed", 1.5f);
            material.SetFloat("_Translucency", 0.35f);
            material.SetColor("_GroundTint", new Color(0.7f, 0.7f, 0.7f, 1f));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            Debug.Log("[GrassModelSetup] material " + MaterialPath + " _BaseColor=" + material.GetColor("_BaseColor")
                + " shader=" + shader.name + " enableInstancing=" + material.enableInstancing);
            return material;
        }

        private static Color SampleGroundTint(string texturePath)
        {
            Color fallback = new Color(0.146f, 0.370f, 0.201f, 1f);
            Color avg = fallback;
            try
            {
                // Decode the file on the CPU (Texture2D.LoadImage): the
                // RenderTexture/Graphics.Blit path does not work in -nographics.
                string fullPath = System.IO.Path.GetFullPath(texturePath);
                if (!System.IO.File.Exists(fullPath))
                {
                    Debug.LogWarning("[GrassModelSetup] текстура земли не найдена: " + texturePath
                        + " — используем запасной зелёный.");
                    return fallback;
                }

                byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
                Texture2D readable = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (readable.LoadImage(bytes))
                {
                    Color[] px = readable.GetPixels();
                    Color sum = Color.black;
                    for (int i = 0; i < px.Length; i++)
                    {
                        sum += px[i];
                    }

                    if (px.Length > 0)
                    {
                        avg = new Color(sum.r / px.Length, sum.g / px.Length, sum.b / px.Length, 1f);
                    }
                }

                Object.DestroyImmediate(readable);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GrassModelSetup] не удалось прочитать " + texturePath + ": " + e.Message);
            }

            Debug.Log("[GrassModelSetup] средний цвет " + texturePath + " = " + avg);
            return avg;
        }

        /// <summary>
        /// Near blade: a folded strip of BladePanels quads narrowing towards the
        /// tip. All normals point up (one normal for the whole mesh), so after
        /// instance rotation +Y follows the surface normal and lighting is even.
        /// Single-sided wing + Cull Off, so Cull Back must stay off.
        /// Vertex colors are white: alpha = wind mask (all of it bends), RGB = 1.
        /// </summary>
        private static Mesh BuildBladeMesh()
        {
            var vertices = new Vector3[(BladePanels + 1) * 2];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[BladePanels * 6];

            // Layout along X: panels run left to right, folds are set in Z. Fold
            // corners are symmetric, so Z alternates sign and the blade
            // cross-section is a zigzag rather than a flat strip.
            float panelWidth = BladeBaseWidth / BladePanels;
            float tipPanelWidth = BladeTipWidth / BladePanels;
            for (int i = 0; i <= BladePanels; i++)
            {
                float t = (float)i / BladePanels;
                // The fold fades towards the tip (10% at the tip): otherwise the
                // top edge zigzags in Z and the blade reads as a tattered shard.
                float fold = Mathf.Lerp(1f, 0.1f, t);
                float halfZ = ((i & 1) == 0 ? 1f : -1f) * (BladeFoldDepth * 0.5f * fold);
                float x = -BladeBaseWidth * 0.5f + panelWidth * i;
                float tipX = -BladeTipWidth * 0.5f + tipPanelWidth * i;

                int v = i * 2;
                vertices[v + 0] = new Vector3(x, 0f, halfZ);
                vertices[v + 1] = new Vector3(tipX, BladeHeight, halfZ);
                for (int k = 0; k < 2; k++)
                {
                    normals[v + k] = Vector3.up;
                    colors[v + k] = Color.white;
                    uvs[v + k] = new Vector2(t, k);
                }
            }

            for (int i = 0; i < BladePanels; i++)
            {
                int v = i * 2;
                int t = i * 6;
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "GrassBladeFixed" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            AssetDatabase.DeleteAsset(FixedMeshPath);
            AssetDatabase.CreateAsset(mesh, FixedMeshPath);
            Debug.Log("[GrassModelSetup] клинок: " + FixedMeshPath
                + " tris=" + triangles.Length / 3 + " verts=" + vertices.Length
                + " bounds=" + mesh.bounds.size);
            return mesh;
        }

        /// <summary>
        /// Far LOD: ONE quad (2 triangles) in the blade's dimensions.
        /// GroundDecorMatrixJob turns it towards the camera (Billboard = !near),
        /// so fold detail is unreadable at distance; base width and height match
        /// the blade, so the near>far transition is invisible.
        /// </summary>
        private static Mesh BuildFarMesh()
        {
            float halfWidth = BladeBaseWidth * 0.5f;
            var vertices = new[]
            {
                new Vector3(-halfWidth, 0f, 0f),
                new Vector3(halfWidth, 0f, 0f),
                new Vector3(-halfWidth, BladeHeight, 0f),
                new Vector3(halfWidth, BladeHeight, 0f)
            };
            var normals = new Vector3[4];
            var colors = new Color[4];
            var uvs = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };

            for (int i = 0; i < 4; i++)
            {
                normals[i] = Vector3.up;
                colors[i] = Color.white;
            }

            var mesh = new Mesh { name = "GrassBladeFar" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.uv = uvs;
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();

            AssetDatabase.DeleteAsset(FarMeshPath);
            AssetDatabase.CreateAsset(mesh, FarMeshPath);
            Debug.Log("[GrassModelSetup] дальний LOD: " + FarMeshPath
                + " tris=2 bounds=" + mesh.bounds.size);
            return mesh;
        }
    }
}