using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Сборка декора местности: генерирует меши-карточки (крест из квадов для
    /// ближнего LOD, квад-биллборд для дальнего), материалы на шейдерах
    /// Galilego/GroundDecor*, профиль EarthDecor со слоями (трава, сухая трава,
    /// ромашки + деревья из Models/Tree) и назначает его на Terra.
    /// Запускается один раз автоматически (по маркеру) или из меню.
    /// </summary>
    public static class GroundDecorSetup
    {
        internal const string ProfilePath = "Assets/_Project/Profiles/Decor/EarthDecor.asset";

        /// <summary>
        /// Верхняя граница леса/травы: 300 м ниже конца зелёной текстуры
        /// рельефа (MidHighBlendEnd = 3500 м у Terra). Общая для деревьев,
        /// травы, сухой травы и ромашек. Камни игнорируют границу и спавнятся
        /// до максимальной высоты.
        /// </summary>
        internal const double TreeLineMaxAltitudeMeters = 3200d;

        private const string DecorModelsFolder = "Assets/_Project/Models/Decor";
        private const string DecorMaterialsFolder = "Assets/_Project/Materials/Decor";
        private const string DecorTexturesFolder = "Assets/_Project/Textures/Decor";

        [MenuItem("Tools/Galilego/Create ground decor content (grass + trees)")]
        public static void Run()
        {
            EnsureFolder(DecorModelsFolder);
            EnsureFolder(DecorMaterialsFolder);
            ReimportTextures();

            // Куст травы (RGB+alpha) — общая текстура для обычной и сухой травы,
            // различаются тинтом материала.
            Texture2D grass = LoadTexture("grass_clump");
            Texture2D dryGrass = LoadTexture("grass_clump");
            Texture2D daisy = LoadTexture("daisy");
            if (grass == null)
            {
                Debug.LogWarning("[GroundDecorSetup] нет " + DecorTexturesFolder + "/grass_clump.png — пропуск.");
                return;
            }

            Mesh card = CreateOrLoadCardMesh();
            Mesh billboard = CreateOrLoadBillboardMesh();

            Material grassSolid = CreateOrLoadMaterial("GrassSolid", "Galilego/GroundDecorSolid", grass, 0.35f, 0.35f);
            Material grassFar = CreateOrLoadMaterial("GrassBillboard", "Galilego/GroundDecorBillboard", grass, 0.35f, 0.35f);
            Color grassTint = new Color(0.45f, 0.80f, 0.35f, 1f);
            Tint(grassSolid, grassTint);
            Tint(grassFar, grassTint);

            // ===== [ГРАФИКА] Дистанции и плотность декора =====
            // NearDistanceMeters / MaxDistanceMeters слоёв ниже — кандидаты в
            // будущие настройки графики (пресеты «минимально…максимально»):
            // трава, сухая трава, ромашки, камни, деревья. Поиск по тегу [ГРАФИКА].
            var layers = new List<GroundDecorLayer>();
            layers.Add(new GroundDecorLayer
            {
                Name = "Grass",
                Enabled = true,
                NearMeshes = new[] { card },
                NearMaterial = grassSolid,
                FarBillboardMesh = billboard,
                FarMaterial = grassFar,
                SpacingMeters = 1.4d,
                MaxInstancesPerChunk = 1500,
                Density = 0.9d,
                DistributionFrequency = 2500d,
                DistributionOctaves = 4,
                DistributionSeedOffset = 1,
                ClusterThreshold = 0d,
                MinAltitudeMeters = 2d,
                MaxAltitudeMeters = TreeLineMaxAltitudeMeters,
                MaxSlopeTan = 1.2d,
                AvoidWater = true,
                WetMin = 0d,
                WetMax = 1d,
                MinScale = 1.0d,
                MaxScale = 1.9d,
                SteepPower = 3d,
                // [ГРАФИКА] дальность травы (near/far).
                NearDistanceMeters = 90f,
                MaxDistanceMeters = 700f
            });

            if (dryGrass != null)
            {
                // [ГРАФИКА] дальность сухой травы (near/far).
                layers.Add(CreateLayer(
                    "DryGrass", card, billboard,
                    CreateOrLoadMaterial("DryGrassSolid", "Galilego/GroundDecorSolid", dryGrass, 0.4f, 0.25f),
                    CreateOrLoadMaterial("DryGrassBillboard", "Galilego/GroundDecorBillboard", dryGrass, 0.4f, 0.25f),
                    spacing: 2.0d, frequency: 2200d, threshold: 0.2d, seedOffset: 2,
                    wetMin: 0d, wetMax: 0.35d, minScale: 0.7d, maxScale: 1.2d,
                    nearDistance: 60f, maxDistance: 600f, maxAltitude: TreeLineMaxAltitudeMeters));
            }

            if (daisy != null)
            {
                // Ромашки — «пятачок» на земле: одиночный квад плашмя (крест из
                // трёх квадов после поворота давал веер наклонных плоскостей).
                layers.Add(new GroundDecorLayer
                {
                    Name = "Daisy",
                    Enabled = true,
                    NearMeshes = new[] { billboard },
                    NearMaterial = CreateOrLoadMaterial("DaisySolid", "Galilego/GroundDecorSolid", daisy, 0.4f, 0f),
                    FarBillboardMesh = null,
                    FarMaterial = null,
                    SpacingMeters = 10d,
                    MaxInstancesPerChunk = 600,
                    Density = 0.6d,
                    DistributionFrequency = 1500d,
                    DistributionOctaves = 4,
                    DistributionSeedOffset = 5,
                    ClusterThreshold = 0.5d,
                    MinAltitudeMeters = 2d,
                    MaxAltitudeMeters = TreeLineMaxAltitudeMeters,
                    MaxSlopeTan = 0.8d,
                    AvoidWater = true,
                    WetMin = 0.35d,
                    WetMax = 1d,
                    MinScale = 0.8d,
                    MaxScale = 1.2d,
                    SteepPower = 4d,
                    GroundOffsetMeters = 0d,
                    FlatOnGround = true,
                    // [ГРАФИКА] дальность ромашек (near/far).
                    NearDistanceMeters = 45f,
                    MaxDistanceMeters = 400f
                });
            }

            // Камни: процедурные меши + процедурная текстура породы. Крупные —
            // с коллизией, стоят на земле.
            Texture2D rockNoise = RockModelSetup.CreateOrLoadRockNoise();
            Mesh[] rocks = RockModelSetup.CreateOrLoadRocks();
            if (rockNoise != null && rocks != null && rocks.Length > 0)
            {
                Material rockMaterial = CreateOrLoadMaterial(
                    "RockSolid", "Galilego/GroundDecorSolid", rockNoise, 0f, 0f);
                Tint(rockMaterial, new Color(0.62f, 0.62f, 0.64f, 1f));
                layers.Add(new GroundDecorLayer
                {
                    Name = "Rocks",
                    Enabled = true,
                    NearMeshes = rocks,
                    NearMaterial = rockMaterial,
                    FarBillboardMesh = null,
                    FarMaterial = null,
                    SpacingMeters = 60d,
                    MaxInstancesPerChunk = 200,
                    Density = 0.27d,
                    DistributionFrequency = 350d,
                    DistributionOctaves = 3,
                    DistributionSeedOffset = 6,
                    ClusterThreshold = 0.05d,
                    MinAltitudeMeters = 2d,
                    // Камни — единственный слой до максимальной высоты (пики,
                    // выше границы леса). 8100 покрывает высшую точку Terra.
                    MaxAltitudeMeters = 8100d,
                    MaxSlopeTan = 3d,
                    AvoidWater = true,
                    WetMin = 0d,
                    WetMax = 1d,
                    MinScale = 1.5d,
                    MaxScale = 4.5d,
                    SteepPower = 1.2d,
                    GroundSinkFactor = 0.65d,
                    Collides = true,
                    CollisionRadiusMeters = 1.8d,
                    CollisionHeightMeters = 6d,
                    // [ГРАФИКА] дальность камней (near/far) и плотность.
                    NearDistanceMeters = 120f,
                    MaxDistanceMeters = 1200f
                });
            }

            // Кактусы: процедурные меши + бесшовная кожа кактуса; сухие биомы.
            Texture2D cactus = LoadTexture("cactus");
            Mesh[] cacti = CactusModelSetup.CreateOrLoadCacti();
            if (cactus != null && cacti != null && cacti.Length > 0)
            {
                Material cactusMaterial = CreateOrLoadMaterial(
                    "CactusSolid", "Galilego/GroundDecorSolid", cactus, 0f, 0f);
                Tint(cactusMaterial, Color.white);
                layers.Add(new GroundDecorLayer
                {
                    Name = "Cactus",
                    Enabled = true,
                    NearMeshes = cacti,
                    NearMaterial = cactusMaterial,
                    FarBillboardMesh = null,
                    FarMaterial = null,
                    SpacingMeters = 90d,
                    MaxInstancesPerChunk = 80,
                    Density = 0.6d,
                    DistributionFrequency = 800d,
                    DistributionOctaves = 3,
                    DistributionSeedOffset = 9,
                    ClusterThreshold = 0.25d,
                    MinAltitudeMeters = 2d,
                    MaxAltitudeMeters = 4000d,
                    MaxSlopeTan = 0.8d,
                    AvoidWater = true,
                    WetMin = 0d,
                    WetMax = 0.32d,
                    MinScale = 2d,
                    MaxScale = 5d,
                    SteepPower = 4d,
                    Collides = true,
                    CollisionRadiusMeters = 0.8d,
                    CollisionHeightMeters = 4d,
                    // [ГРАФИКА] дальность кактусов (near/far) и плотность.
                    NearDistanceMeters = 120f,
                    MaxDistanceMeters = 1200f
                });
            }

            GroundDecorLayer trees = TreeModelSetup.BuildTreesLayer();
            if (trees != null)
            {
                layers.Add(trees);
            }

            GroundDecorProfileAsset asset = CreateOrLoadProfile();
            if (asset.Profile == null)
            {
                asset.Profile = new GroundDecorProfile();
            }

            asset.Profile.Layers = layers;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            AssignToTerra(asset);
            Debug.Log("[GroundDecorSetup] готово: " + ProfilePath + ", слоёв=" + layers.Count);
        }

        private static GroundDecorLayer CreateLayer(
            string name, Mesh card, Mesh billboard, Material nearMaterial, Material farMaterial,
            double spacing, double frequency, double threshold, int seedOffset,
            double wetMin, double wetMax, double minScale, double maxScale,
            float nearDistance, float maxDistance, double maxAltitude = 6000d)
        {
            return new GroundDecorLayer
            {
                Name = name,
                Enabled = true,
                NearMeshes = new[] { card },
                NearMaterial = nearMaterial,
                FarBillboardMesh = billboard,
                FarMaterial = farMaterial,
                SpacingMeters = spacing,
                MaxInstancesPerChunk = 600,
                Density = 0.6d,
                DistributionFrequency = frequency,
                DistributionOctaves = 4,
                DistributionSeedOffset = seedOffset,
                ClusterThreshold = threshold,
                MinAltitudeMeters = 2d,
                MaxAltitudeMeters = maxAltitude,
                MaxSlopeTan = 1.0d,
                AvoidWater = true,
                WetMin = wetMin,
                WetMax = wetMax,
                MinScale = minScale,
                MaxScale = maxScale,
                SteepPower = 4d,
                NearDistanceMeters = nearDistance,
                MaxDistanceMeters = maxDistance
            };
        }

        private static void ReimportTextures()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { DecorTexturesFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guids[i]), ImportAssetOptions.ForceUpdate);
            }
        }

        private static Texture2D LoadTexture(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(DecorTexturesFolder + "/" + name + ".png");
        }

        private static void Tint(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
        }

        private static Material CreateOrLoadMaterial(
            string name, string shaderName, Texture2D texture, float cutoff, float windStrength)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                return null;
            }

            string path = DecorMaterialsFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_BaseColorMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Cutoff", cutoff);
            material.SetFloat("_WindStrength", windStrength);
            material.SetFloat("_WindSpeed", 1.5f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh CreateOrLoadCardMesh()
        {
            string path = DecorModelsFolder + "/GrassCard.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EnsureWindMask(existing);
                return existing;
            }

            // Крест из трёх квадов (0/60/120°) с основанием в y=0 — классическая
            // карточка травы для инстансинга. Нормали вверх: мягкий свет сверху.
            const int quadCount = 3;
            var vertices = new Vector3[quadCount * 4];
            var normals = new Vector3[quadCount * 4];
            var uvs = new Vector2[quadCount * 4];
            var triangles = new int[quadCount * 6];
            for (int q = 0; q < quadCount; q++)
            {
                float angle = q * 60f * Mathf.Deg2Rad;
                Vector3 right = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.5f;
                int v = q * 4;
                vertices[v + 0] = -right;
                vertices[v + 1] = right;
                vertices[v + 2] = -right + Vector3.up;
                vertices[v + 3] = right + Vector3.up;
                for (int k = 0; k < 4; k++)
                {
                    normals[v + k] = Vector3.up;
                }

                uvs[v + 0] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(0f, 1f);
                uvs[v + 3] = new Vector2(1f, 1f);
                int t = q * 6;
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "GrassCard" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            EnsureWindMask(mesh);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>
        /// Маска ветра в vertex color: 1 = гнётся (трава/листва), 0 = нет.
        /// Нужна шейдеру GroundDecorSolid, чтобы стволы деревьев не качались.
        /// </summary>
        private static void EnsureWindMask(Mesh mesh)
        {
            if (mesh.colors != null && mesh.colors.Length == mesh.vertexCount)
            {
                return;
            }

            var colors = new Color[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Color.white;
            }

            mesh.colors = colors;
            EditorUtility.SetDirty(mesh);
        }

        private static Mesh CreateOrLoadBillboardMesh()
        {
            string path = DecorModelsFolder + "/BillboardQuad.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                return existing;
            }

            // Квад центрирован по XY (шейдер GroundDecorBillboard сам разворачивает
            // его к камере и поднимает на 0.5): x,y ∈ [-0.5, 0.5], uv 0..1.
            var mesh = new Mesh { name = "BillboardQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static GroundDecorProfileAsset CreateOrLoadProfile()
        {
            GroundDecorProfileAsset asset = AssetDatabase.LoadAssetAtPath<GroundDecorProfileAsset>(ProfilePath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<GroundDecorProfileAsset>();
                AssetDatabase.CreateAsset(asset, ProfilePath);
            }

            return asset;
        }

        private static void AssignToTerra(GroundDecorProfileAsset asset)
        {
            bool changed = false;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    BodyAuthoring[] bodies = roots[r].GetComponentsInChildren<BodyAuthoring>(true);
                    for (int b = 0; b < bodies.Length; b++)
                    {
                        if (bodies[b].TerrainPreset == null || bodies[b].gameObject.name != "Terra")
                        {
                            continue;
                        }

                        bodies[b].GroundDecorPreset = asset;
                        EditorUtility.SetDirty(bodies[b]);
                        changed = true;
                    }
                }

                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            if (changed)
            {
                EditorSceneManager.SaveOpenScenes();
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    /// <summary>
    /// Первичный/версионный автозапуск сборки декора: по маркеру версии.
    /// Дальше не трогает — правки пользователя не затираются.
    /// </summary>
    [InitializeOnLoad]
    internal static class GroundDecorSetupAuto
    {
        private const string VersionMarker = "Temp/decor-content-v41.done";

        static GroundDecorSetupAuto()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
            {
                // Не пропускаем сборку навсегда: ждём выхода из Play.
                EditorApplication.delayCall += Run;
                return;
            }

            if (File.Exists(VersionMarker))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/Decor/grass_clump.png") == null)
            {
                EditorApplication.delayCall += Run;
                return;
            }

            try
            {
                GroundDecorSetup.Run();
                File.WriteAllText(VersionMarker, System.DateTime.Now.ToString("O"));
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[GroundDecorSetup] ошибка: " + exception);
            }
        }
    }
}
