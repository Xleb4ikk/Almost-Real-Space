using System.Collections.Generic;
using System.IO;
using Galilego.Core;
using Unity.Mathematics;
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
        /// Верхняя граница леса/травы: конец зелёной текстуры рельефа
        /// (MidHighBlendEnd = 3500 м у Terra). Общая для деревьев,
        /// травы, сухой травы и ромашек. Камни игнорируют границу и спавнятся
        /// до максимальной высоты.
        /// </summary>
        internal const double TreeLineMaxAltitudeMeters = 3500d;

        private const string DecorModelsFolder = "Assets/_Project/Models/Decor";
        private const string DecorMaterialsFolder = "Assets/_Project/Materials/Decor";
        private const string DecorTexturesFolder = "Assets/_Project/Textures/Decor";

        [MenuItem("Tools/Galilego/Create ground decor content (grass + trees)")]
        public static void Run()
        {
            EnsureFolder(DecorModelsFolder);
            EnsureFolder(DecorMaterialsFolder);
            ReimportTextures();

            // Куст травы (RGB+alpha) — текстура дальнего биллборда травы.
            Texture2D grass = LoadTexture("grass_clump");
            Texture2D daisy = LoadTexture("daisy");
            if (grass == null)
            {
                Debug.LogWarning("[GroundDecorSetup] нет " + DecorTexturesFolder + "/grass_clump.png — пропуск.");
                return;
            }

            Mesh billboard = CreateOrLoadBillboardMesh();

            Material grassFar = CreateOrLoadMaterial("GrassBillboard", "Galilego/GroundDecorBillboard", grass, 0.35f, 0.35f);

            // ===== [ГРАФИКА] Дистанции и плотность декора =====
            // NearDistanceMeters / MaxDistanceMeters слоёв ниже — кандидаты в
            // будущие настройки графики (пресеты «минимально…максимально»):
            // трава, ромашки, камни, деревья. Поиск по тегу [ГРАФИКА].
            var layers = new List<GroundDecorLayer>();
            GroundDecorLayer grassLayer = GrassModelSetup.BuildGrassLayer(billboard, grassFar);
            if (grassLayer != null)
            {
                layers.Add(grassLayer);
            }

            if (daisy != null)
            {
                // Ромашки — «пятачок» на земле: одиночный квад плашмя (крест из
                // трёх квадов после поворота давал веер наклонных плоскостей).
                layers.Add(new GroundDecorLayer
                {
                    Name = "Daisy",
                    Enabled = true,
                    // Ромашки тоже не кастуют: лежат на земле, теней не видно.
                    CastShadows = false,
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
                    // Ромашки — луговые цветы: только зелёная земля палитры.
                    MinNormalizedHeight = 0.032d,
                    MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
                    MaxAltitudeMeters = TreeLineMaxAltitudeMeters,
                    MaxSlopeTan = 0.8d,
                    AvoidWater = true,
                    WetMin = GroundDecorLayer.GreenWetMin,
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
                    // выше границы леса) и без нормированных границ палитры:
                    // в горах и на пляже только камни. 8100 покрывает высшую
                    // точку Terra.
                    MaxAltitudeMeters = 8100d,
                    MaxSlopeTan = 3d,
                    AvoidWater = true,
                    WetMin = 0d,
                    WetMax = 1d,
                    // Камни лежат и на полярной шапке: широтный фейд не применяем.
                    IgnoreLatitude = true,
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
                    // Кактусы — сухой биом, но не пляж и не скалы.
                    MinNormalizedHeight = 0.032d,
                    MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
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

        private const string TerrainProfilePath = "Assets/_Project/Profiles/Terrain/EarthLike.asset";

        /// <summary>
        /// Диагностика покрытия травой: гоняет ту же TryEvaluate по сфере
        /// (фибоначчиева выборка) и печатает долю принятых клеток от суши и от
        /// зелёного биома. Нужна для калибровки Density/ClusterThreshold.
        /// </summary>
        [MenuItem("Tools/Galilego/Diagnose grass coverage")]
        public static void DiagnoseGrassCoverage()
        {
            const int samples = 200000;

            GroundDecorProfileAsset decor = AssetDatabase.LoadAssetAtPath<GroundDecorProfileAsset>(ProfilePath);
            TerrainProfileAsset terrainAsset = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(TerrainProfilePath);
            if (decor == null || decor.Profile == null || decor.Profile.Layers.Count == 0 || terrainAsset == null)
            {
                Debug.LogWarning("[GrassDiag] нет " + ProfilePath + " или " + TerrainProfilePath);
                return;
            }

            GroundDecorLayer grass = decor.Profile.Layers[0];
            int seed = 24334543;
            double radius = 1143000d;
            TerrainProfile terrainProfile = terrainAsset.Profile;
            BodyAuthoring[] bodies = Object.FindObjectsByType<BodyAuthoring>();
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].TerrainPreset == null || bodies[i].TerrainPreset.Profile == null)
                {
                    continue;
                }

                seed = bodies[i].TerrainSeed;
                radius = bodies[i].Radius;
                terrainProfile = bodies[i].TerrainPreset.Profile;
                break;
            }

            HeightfieldTerrain terrain = new HeightfieldTerrain();
            terrain.ApplyProfile(terrainProfile, seed);
            TerrainNoiseParams noise = TerrainNoiseParams.FromTerrain(terrain);
            GroundDecorPlacementParams p = GroundDecorPlacementParams.FromLayer(grass, terrain, radius, new Vector3d(0d, 0d, 0d));
            p.WindAzimuthRad = 0d;

            long land = 0;
            long green = 0;
            long accepted = 0;
            long acceptedGreen = 0;
            double golden = System.Math.PI * (3d - System.Math.Sqrt(5d));
            for (int i = 0; i < samples; i++)
            {
                double z = 1d - (2d * ((i + 0.5d) / samples));
                double r = System.Math.Sqrt(System.Math.Max(0d, 1d - (z * z)));
                double phi = i * golden;
                double3 dir = new double3(System.Math.Cos(phi) * r, System.Math.Sin(phi) * r, z);

                double rawHeight = TerrainNoise.SampleHeight(noise, dir) * p.AmplitudeMeters;
                if (rawHeight <= p.SeaLevelMeters + (p.AmplitudeMeters * 0.001d))
                {
                    continue;
                }

                land++;
                double mask = System.Math.Max(-1d, System.Math.Min(1d, TerrainNoise.SampleColorNoise(noise, dir)));
                double wet = System.Math.Max(0d, System.Math.Min(1d, 0.5d + (mask * 1.6d)));
                double tMasked = ((rawHeight - p.SeaLevelMeters) / System.Math.Max(1d, p.AmplitudeMeters))
                    + (mask * p.ColorNoiseStrength);
                // Та же полярная шапка, что в декоре: сплошной лёд — не зелень.
                double zc = dir.z < -1d ? -1d : (dir.z > 1d ? 1d : dir.z);
                double lat01 = System.Math.Abs(System.Math.Asin(zc)) / (System.Math.PI * 0.5d);
                double iceT = (lat01 - 0.70d) / 0.16d;
                double ice = iceT <= 0d ? 0d : (iceT >= 1d ? 1d : iceT * iceT * (3d - (2d * iceT)));
                bool isGreen = wet >= 0.38d && tMasked >= 0.03d && tMasked < 0.45d
                    && (rawHeight - terrain.SeaLevelMeters) >= terrain.BeachHeightMeters
                    && ice < 1d;
                if (isGreen)
                {
                    green++;
                }

                double3 random = new double3(
                    GroundDecorDistribution.Hash01(i, 1, 0, 0, 21),
                    GroundDecorDistribution.Hash01(i, 2, 0, 0, 22),
                    GroundDecorDistribution.Hash01(i, 3, 0, 0, 23));
                bool ok = GroundDecorDistribution.TryEvaluate(
                    p, noise, dir, random,
                    GroundDecorDistribution.Hash01(i, 4, 0, 0, 24),
                    GroundDecorDistribution.Hash01(i, 5, 0, 0, 25),
                    GroundDecorDistribution.Hash01(i, 6, 0, 0, 26),
                    out _);
                if (ok)
                {
                    accepted++;
                    if (isGreen)
                    {
                        acceptedGreen++;
                    }
                }
            }

            Debug.Log(string.Format(
                "[GrassDiag] samples={0} land={1} green={2} accepted={3} acceptedGreen={4} | coverage land={5:P1} green={6:P1} | layer spacing={7} density={8} cluster={9}+fade{10} wet={11}+fade{12} norm=[{13},{14}]",
                samples, land, green, accepted, acceptedGreen,
                land > 0 ? (double)accepted / land : 0d,
                green > 0 ? (double)acceptedGreen / green : 0d,
                grass.SpacingMeters, grass.Density, grass.ClusterThreshold, grass.ClusterFade,
                grass.WetMin, grass.WetFade, grass.MinNormalizedHeight, grass.MaxNormalizedHeight));
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
        /// Маска ветра в vertex color: alpha 1 = гнётся (трава/листва), 0 = нет.
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
        private const string VersionMarker = "Temp/decor-content-v55.done";

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
