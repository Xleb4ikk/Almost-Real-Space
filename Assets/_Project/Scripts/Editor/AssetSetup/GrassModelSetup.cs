using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Сборка ближнего LOD травы: процедурный низкополигональный клинок
    /// (складчатая лента) + специализированный шейдер Galilego/GrassBlade.
    ///
    /// Почему больше не FBX. Замер показал: у прежнего клинка 26 треугольников
    /// на инстанс, зеркальных пар треугольников НЕТ (все 26 односторонние) и
    /// одна нормаль на все 46 вершин. То есть это складчатая плоскость, а 26
    /// треугольников — просто избыточная разбивка. Стоимость травы при этом не
    /// в геометрии, а в заполнении (ковёр под игроком + Cull Off), поэтому
    /// геометрию ужать можно почти бесплатно: 3 складки ? 1 квад = 6
    /// треугольников с тем же силуэтом.
    ///
    /// Cull Back на этом меше НЕЛЬЗЯ: односторонний винг + Cull Off = клинок
    /// виден с обеих сторон, отсечение задних граней holes бы половину
    /// клинков на половине ракурсов.
    /// </summary>
    public static class GrassModelSetup
    {
        private const string DecorModelsFolder = "Assets/_Project/Models/Decor";
        private const string MaterialsFolder = "Assets/_Project/Materials";
        private const string DecorMaterialsFolder = MaterialsFolder + "/Decor";
        private const string MaterialPath = DecorMaterialsFolder + "/GrassBlade.mat";
        private const string FixedMeshPath = DecorModelsFolder + "/GrassBladeFixed.asset";
        private const string FarMeshPath = DecorModelsFolder + "/GrassBladeFar.asset";

        /// <summary>Текстура земли для автотинта травы (средний цвет пикселей).</summary>
        private const string GroundTexturePath = "Assets/_Project/Textures/Terrain/terrain_mid_clean.jpg";

        private const float TargetMinHeightMeters = 1.0f;
        private const float TargetMaxHeightMeters = 2.08f;

        // Геометрия клинка. Ширина в основании совпадает с прежним мешем
        // (0.34 м), глубина складок — 0.12 м, высота 1.58 м: MinScale/MaxScale
        // в слое считаются от высоты меша, поэтому размеры те же и слой
        // продолжает сажать клинки 1.0…2.08 м.
        private const float BladeBaseWidth = 0.34f;
        private const float BladeTipWidth = 0.09f;
        private const float BladeHeight = 1.58f;
        private const float BladeFoldDepth = 0.12f;
        private const int BladePanels = 3;

        /// <summary>
        /// Слой травы с ближним 3D-клинком и дешёвым дальним LOD (один квад).
        /// Дальний квад рендерер разворачивает к камере (GroundDecorMatrixJob,
        /// Billboard = !near), поэтому одного квада достаточно: 2 треугольника
        /// вместо 26 на всю дальнюю половину ковра.
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

            // Одиночный клинок на инстанс: остров собирается из МНОГИХ
            // отдельных травинок по сетке, а не из кустиков-«точек» —
            // внутри острова получается плотный ровный ковёр.
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

            // Дальний LOD красим тем же тинтом, что ближний: иначе на
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
                // Дальний LOD — ОДИН квад (2 треугольника) вместо клинка:
                // дальше NearDistance разворачивается к камере, поэтому
                // подробности сгибов всё равно не читаются, а геометрия идёт
                // в два пасса. Переход near>far не должен быть виден — квад
                // повторяет силуэт клинка (та же ширина в основании и высота).
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
                // Песок — только абсолютный пляж (BeachHeightMeters профиля
                // террейна), нормированной нижней границы у травы нет: 0.03 при
                // амплитуде 9144 м — это 274 м высоты, и такая граница оставляла
                // всю прибрежную равнину (зелёную по рельефу) без травы.
                MinNormalizedHeight = 0d,
                // Верх зелени палитры: выше t=0.45 рельеф красится скалой —
                // там только камни. Нижняя граница wet — начало Grass в палитре.
                MaxNormalizedHeight = GroundDecorLayer.RockBottomNormalizedHeight,
                MaxAltitudeMeters = GroundDecorSetup.GrassMaxAltitudeMeters,
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
                // [ГРАФИКА] ДАЛЬНОСТЬ И ЗАКОН ПОКРЫТИЯ.
                //
                // Дальность поднята со 120 до 200 м, но это само по себе
                // ничего не даёт: плотность обязана падать с расстоянием, иначе
                // инстансов больше, чем пикселей. Раньше стояла экспонента
                // exp(-(d-20)/25): на 100 м от неё оставалось ~4%, на 200 м — уже
                // ничего, и за горизонтом трава просто исчезала (жалоба «лысый
                // круг»). Экспонента логична для независимых объектов, но не
                // для сплошного ковра.
                //
                // Здесь работает закон покрытия: плотность ∝ (R/d)², а размер
                // дальнего квада ∝ d (GroundDecorMatrixJob, DistanceCore +
                // BillboardFarScale). Тогда заполнение экрана n·g² постоянно
                // вплоть до MaxDistanceMeters, а суммарное число инстансов
                // растёт как ln(R) вместо R² — то есть вдвое дальше по радиусу
                // стоит заметно меньше инстансов, чем постоянная плотность.
                // Power = 2 — точное поддержание покрытия. Больше 2 — реже
                // (дешевле, дальняя зона бледнее), меньше 2 — дороже.
                NearDistanceMeters = 35f,
                MaxDistanceMeters = 200f,
                // FarDensity = 0: на дальней границе травы быть не должно,
                // иначе последние 200 м держат лишний слой инстансов.
                FarDensity = 0d,
                // DensityFalloffMeters при Power > 0 не участвует в профиле
                // плотности, но остаётся ради R/keepRadius в сборке.
                DensityFalloffMeters = 25f,
                DensityCoreMeters = 20f,
                DensityFalloffPower = 2f,
                // [ПУСТОТА ПОД НОГАМИ] Порог пересборки = core − lookAhead.
                // При core 20 и lookAhead 25 это упрётся в пол
                // DecorMinRebuildMeters = 6 м: полная плотность гарантирована
                // на 20 − 6 = 14 м впереди, дальше лёгкое разрежение и рост
                // после пересборки остаются. Это осознанный размен: вариант с
                // ядром 30 дал бы 24 м вперёд, но это (30/20)² ≈ 2.25× инстансов.
                // Если после маршрута рождений у камеры много — поднимать ядро.
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
            // Подмешивание к цвету земли. Границы по DensityFalloffPower: рост
            // начинается с края ядра (20 м), плотность падает с него же, и
            // подмешивание должно накрыть ровно ту зону, где трава стала
            // редкой. 55…185 м при MaxDistanceMeters = 200.
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

        /// <summary>
        /// Ближний клинок: складчатая лента из BladePanels квадов, сужающаяся
        /// к вершине. Все нормали — вверх: у клинка одна нормаль на весь меш
        /// (так и было у FBX-версии), и после поворота инстанса +Y смотрит
        /// вдоль нормали поверхности, поэтому свет ложится ровно. Тот же
        /// «односторонний винг + Cull Off», поэтому Cull Back включать нельзя.
        /// Vertex colors белые: alpha = маска ветра (гнётся всё), RGB = 1.
        /// </summary>
        private static Mesh BuildBladeMesh()
        {
            var vertices = new Vector3[(BladePanels + 1) * 2];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[BladePanels * 6];

            // Раскладка по X: панели идут слева направо, складка задаётся Z.
            // Углы складки симметричны, поэтому Z положительный/отрицательный
            // чередуется и клинок в сечении — «зигзаг», а не плоская лента.
            float panelWidth = BladeBaseWidth / BladePanels;
            float tipPanelWidth = BladeTipWidth / BladePanels;
            for (int i = 0; i <= BladePanels; i++)
            {
                float t = (float)i / BladePanels;
                // Складка гаснет к вершине (10% в остр��е): иначе верхний край
                // идёт зигзагом по Z и клинок читается как рваный осколок.
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
        /// Дальний LOD — ОДИН квад (2 треугольника) в габаритах клинка.
        /// GroundDecorMatrixJob разворачивает его к камере (Billboard = !near),
        /// поэтому подробности сгибов на дальней дистанции не читаются, а
        /// платить за них приходилось на всей площади ковра: 26 треугольников
        /// на инстанс против 2. Ширина в основании и высота совпадают с
        /// клинком, поэтому переход near>far не виден.
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