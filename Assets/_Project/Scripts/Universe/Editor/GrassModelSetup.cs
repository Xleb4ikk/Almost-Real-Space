using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Сборка ближнего LOD травы: 3D-меш травинки из Models/Decor/GrassBlade.fbx
    /// (единственный меш "GrassBlade") + материал Galilego/GroundDecorSolid.
    /// Дальний LOD (биллборд) пробрасывается снаружи — остаётся прежним
    /// экономичным. Используется GroundDecorSetup.
    /// Учтено по замеру геометрии: vertex colors в FBX все белые (тинт ими
    /// не дать — красим материалом); winding перевёрнут (разворачиваем);
    /// лента растёт плашмя вдоль -Z (поднимаем поворотом около X).
    /// NearMeshes — ОДИНОЧНЫЙ клинок: остров травы собирается из многих
    /// отдельных травинок по сетке (без кустиков-«точек»).
    /// </summary>
    public static class GrassModelSetup
    {
        private const string GrassModelPath = "Assets/_Project/Models/Decor/GrassBlade.fbx";
        private const string DecorModelsFolder = "Assets/_Project/Models/Decor";
        private const string MaterialsFolder = "Assets/_Project/Materials";
        private const string DecorMaterialsFolder = MaterialsFolder + "/Decor";
        private const string MaterialPath = DecorMaterialsFolder + "/GrassBladeSolid.mat";
        private const string FixedMeshPath = DecorModelsFolder + "/GrassBladeFixed.asset";

        /// <summary>Текстура земли для автотинта травы (средний цвет пикселей).</summary>
        private const string GroundTexturePath = "Assets/_Project/Textures/Terrain/terrain_mid_clean.jpg";

        private const float TargetMinHeightMeters = 1.0f;
        private const float TargetMaxHeightMeters = 2.08f;

        /// <summary>
        /// Слой травы с ближним 3D-мешем. Дальний LOD — ТОТ ЖЕ клинок и ТОТ ЖЕ
        /// материал: одинаковые полигоны на всех дистанциях, силуэт при переходе
        /// near→far не меняется, ковёр сливается. Текстурный квад (clump) с
        /// тёмным краем альфы убран — он давал «чёрную обводку». Биллборд-разворот
        /// дальнего LOD делает рендерер (GroundDecorMatrixJob).
        /// </summary>
        internal static GroundDecorLayer BuildGrassLayer(Mesh farBillboard, Material farMaterial)
        {
            Material material = CreateOrLoadMaterial();
            Mesh source = LoadBladeMesh();
            if (material == null || source == null)
            {
                return null;
            }

            Mesh blade = BuildBladeMesh(source);
            if (blade == null)
            {
                return null;
            }

            // Одиночный клинок на инстанс: остров собирается из МНОГИХ
            // отдельных травинок по сетке, а не из кустиков-«точек» —
            // внутри острова получается плотный ровный ковёр.
            Bounds bounds = blade.bounds;
            float meshHeight = Mathf.Max(1e-4f, bounds.size.y);
            Debug.Log("[GrassModelSetup] GrassBladeFixed bounds.size=" + bounds.size
                + " min.y=" + bounds.min.y + " (высота меша как есть: " + meshHeight + " м)");

            float groundOffset = 0f;
            if (Mathf.Abs(bounds.min.y) > 1e-4f)
            {
                groundOffset = -bounds.min.y * (TargetMinHeightMeters / meshHeight);
                Debug.LogWarning("[GrassModelSetup] низ меша не на y=0 — GroundOffsetMeters=" + groundOffset);
            }

            // Дальний биллборд красим тем же тинтом, что ближний: иначе на
            // NearDistance трава «попает» из тёмной в ярко-зелёную.
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
                // Дальний LOD = тот же клинок: одинаковые полигоны everywhere,
                // переход near→far — только разворот к камере и рост размера.
                FarBillboardMesh = blade,
                FarMaterial = material,
                SpacingMeters = 0.45d,
                MaxInstancesPerChunk = 160000,
                Density = 1.0d,
                DistributionFrequency = 20000d,
                ClusterPatchMeters = 120d,
                DistributionOctaves = 4,
                DistributionSeedOffset = 1,
                ClusterThreshold = 0.36d,
                ClusterFade = 0.12d,
                MinAltitudeMeters = 2d,
                MinNormalizedHeight = 0.032d,
                // Верх зелени палитры: выше t=0.45 рельеф красится скалой —
                // там только камни. Нижняя граница wet — начало Grass в палитре.
                MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
                MaxAltitudeMeters = GroundDecorSetup.TreeLineMaxAltitudeMeters,
                MaxSlopeTan = 2.8d,
                AvoidWater = true,
                // Ковёр лезвий — на любой земле зелёных высот, включая сухую
                // степь: песок (t), скалы, снег, пляж, лёд и вода по-прежнему
                // отсекают. Иначе сухие холмы с зелёным фото лысые.
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
                NearDistanceMeters = 100f,
                MaxDistanceMeters = 300f,
                FarDensity = 0.05d,
                DensityFalloffMeters = 45f,
                DensityCoreMeters = 30f,
                PerInstanceDensity = true,
                SpawnMarginMeters = 150f,
                SubInstancesPerCell = 400,
                MaxCellsPerAxis = 192
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
            material.SetColor("_BaseColor", SampleGroundTint(GroundTexturePath));
            material.SetTexture("_BaseColorMap", Texture2D.whiteTexture);
            material.SetFloat("_VertexColorTint", 0f);
            material.SetFloat("_Cutoff", 0f);
            // Двусторонний свет: плоский клинок сбоку — иначе половина травинок
            // уходит в чёрное и ковёр рябит «обводкой».
            material.SetFloat("_TwoSided", 1f);
            material.SetFloat("_WindStrength", 0.15f);
            material.SetFloat("_WindSpeed", 1.5f);
            material.enableInstancing = true;
            // Индирект-путь в HDRP даёт битые трансформы — держим обычный
            // инстансинг с per-frame матрицами (проверено визуально).
            material.DisableKeyword("_DECOR_INDIRECT_MATRICES");
            EditorUtility.SetDirty(material);
            Debug.Log("[GrassModelSetup] material _BaseColor=" + material.GetColor("_BaseColor")
                + " shader=" + (shader != null ? shader.name : "NULL")
                + " enableInstancing=" + material.enableInstancing);
            return material;
        }

        private static Color SampleGroundTint(string texturePath)
        {
            Color fallback = new Color(0.146f, 0.370f, 0.201f, 1f);
            Color avg = fallback;
            try
            {
                // Декодируем файл на CPU (Texture2D.LoadImage): путь через
                // RenderTexture/Graphics.Blit в -nographics не работает и
                // возвращал серый 0.8 — трава становилась белой.
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

        private static Mesh LoadBladeMesh()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(GrassModelPath);
            Mesh named = null;
            Mesh best = null;
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

                if (mesh.name == "GrassBlade")
                {
                    named = mesh;
                }
            }

            Mesh blade = named != null ? named : best;
            if (blade == null)
            {
                Debug.LogWarning("[GrassModelSetup] нет меша в " + GrassModelPath);
                return null;
            }

            if (blade.colors == null || blade.colors.Length != blade.vertexCount)
            {
                Debug.LogWarning("[GrassModelSetup] у GrassBlade нет vertex colors — "
                    + "проверь Import Settings модели (импорт Vertex Colors включён?). "
                    + "Тинт всё равно идёт материалом, не вертексами.");
            }

            return blade;
        }

        /// <summary>
        /// Рендер-копия меша: исходный FBX-меш read-only, с перевёрнутым
        /// winding (грани смотрели вниз — замерили кроссом треугольников)
        /// и растущий плашмя вдоль -Z. Клонируем, разворачиваем треугольники,
        /// поднимаем ленту поворотом около X почти вертикально. Сохраняем
        /// ассетом (блендерный FBX не трогаем).
        /// </summary>
        private static Mesh BuildBladeMesh(Mesh source)
        {
            Mesh flat;
            try
            {
                flat = Object.Instantiate(source);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[GrassModelSetup] не удалось клонировать GrassBlade: " + exception.Message);
                return null;
            }

            try
            {
                int subMeshes = Mathf.Max(1, flat.subMeshCount);
                for (int s = 0; s < subMeshes; s++)
                {
                    int[] triangles = flat.GetTriangles(s);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        int temp = triangles[t + 1];
                        triangles[t + 1] = triangles[t + 2];
                        triangles[t + 2] = temp;
                    }

                    flat.SetTriangles(triangles, s);
                }

                flat.RecalculateNormals();
                flat.RecalculateBounds();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[GrassModelSetup] не удалось развернуть GrassBlade: " + exception.Message);
                Object.DestroyImmediate(flat);
                return null;
            }

            Quaternion upright = Quaternion.Euler(70f, 0f, 0f);
            Vector3[] verts = flat.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = upright * verts[i];
            }

            flat.vertices = verts;
            flat.RecalculateNormals();
            flat.RecalculateBounds();

            // Нормали — ВСЕ вдоль локального +Y (рост клинка). Меш изогнут, и
            // «честные» нормали давали у каждого сегмента свой тон — на ковре
            // это читалось тёмными «полигонами». После поворота инстанса +Y
            // смотрит вдоль нормали поверхности, поэтому свет ложится ровно.
            var flatNormals = new Vector3[flat.vertexCount];
            for (int i = 0; i < flatNormals.Length; i++)
            {
                flatNormals[i] = Vector3.up;
            }

            flat.normals = flatNormals;

            Mesh result = flat;
            AssetDatabase.DeleteAsset(FixedMeshPath);
            AssetDatabase.CreateAsset(result, FixedMeshPath);
            Debug.Log("[GrassModelSetup] одиночный клинок: " + FixedMeshPath
                + " bounds=" + result.bounds.size);
            return result;
        }

    }
}
