using System.Collections.Generic;
using Galilego.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Galilego.Universe
{
    /// <summary>
    /// Планетарный рендер: cube-sphere с quadtree LOD. Заменяет старый тайловый
    /// BodyTerrainView и примитив-сферу.
    ///
    /// Геометрия: 6 граней куба → единичные направления (CubeSphere) →
    /// высота/маска из ЕДИНОЙ TerrainNoise (тот же Burst-job, что физика) →
    /// тело-fixed позиции (Radius + h). Чанки — дети общего корня, который
    /// наследует трансформ тела (BodyView), но имеет компенсированный скейл,
    /// поэтому вершины живут в метрах от центра.
    ///
    /// LOD: узел делится, пока дистанция камеры < размера узла × SplitFactor
    /// (и depth < MaxDepth). Готовые меши кэшируются по id узла (рельеф
    /// статичен в тел-fixed кадре), видимый набор — по куллингу задней
    /// полусферы. Юбки по краям скрывают трещины между разными LOD.
    /// Океан — внутри чанков: затопленные вершины несут water-флаг (color.a),
    /// шейдер рисует им воду (fresnel + блик). Вызов — LateUpdate после BodyView.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-70)]
    public sealed class PlanetSurfaceRenderer : MonoBehaviour
    {
        // ===== [ГРАФИКА] Качество и дальность рельефа =====
        // TileResolution / MaxDepth / SplitFactor / BuildsPerFrame / MaxNodes /
        // MaxCachedChunks — кандидаты в будущие пресеты настроек графики (от
        // минимального до максимального качества).
        // Дальность видимости задаётся камерой (FirstPersonCamera.UpdateClipPlanes,
        // физический горизонт с запасом на вершины) — здесь рельеф строится на
        // всю видимую полусферу без высотного порога.
        // Сглаживание кадра (SMAA/TAA) задаётся на Main Camera в сцене
        // OutdoorsScene: HDAdditionalCameraData (antialiasing / SMAAQuality / TAA*).
        // Поиск по тегу: [ГРАФИКА].

        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Вершин на сторону узла (65 ≈ 64 квада).")]
        [Range(9, 257)]
        public int TileResolution = 65;

        [Tooltip("Максимальная глубина quadtree (0 = 6 граней целиком).")]
        [Range(0, 16)]
        public int MaxDepth = 12;

        [Tooltip("Сплит: узел делится, если дистанция до центра < размера узла × фактор.")]
        [Range(1f, 12f)]
        public float SplitFactor = 3.5f;

        [Tooltip("Гистерезис слияния: уже поделённый узел сливается обратно только при дистанции > размера × SplitFactor × это. Между порогами держится прошлый уровень (против LOD-флапа на скорости).")]
        [Range(1f, 3f)]
        public float MergeHysteresis = 1.3f;

        [Tooltip("Глубина юбки как доля размера узла.")]
        [Range(0f, 0.2f)]
        public float SkirtFactor = 0.03f;

        [Tooltip("Сколько чанков строить за кадр (первый кадр — без лимита).")]
        [Range(1, 64)]
        public int BuildsPerFrame = 16;

        [Tooltip("Ограничитель активных узлов (защита от лавины).")]
        [Min(64)]
        public int MaxNodes = 6000;

        [Tooltip("Максимум закэшированных мешей (лишние вытесняются).")]
        [Min(64)]
        public int MaxCachedChunks = 2048;

        private struct Node
        {
            public int Face;
            public int Depth;
            public int Ix;
            public int Iy;
        }

        private sealed class Chunk
        {
            public GameObject Go;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public bool Visible;
            public int Depth;
            public Vector3d CenterAstro;
            public float BoundsRadius;
            public readonly List<DecorLayerRuntime> Decor = new List<DecorLayerRuntime>();
        }

        /// <summary>Готовые инстансы одного слоя декора в чанке (локально чанку).</summary>
        private sealed class DecorLayerRuntime
        {
            public GroundDecorLayer Profile;
            public NativeArray<GroundDecorInstance> Instances;
            public Matrix4x4[] Matrices;
        }

        private OrbitingBody body;
        private HeightfieldTerrain terrain;
        private TerrainNoiseParams noiseParams;
        private Transform surfaceRoot;
        private Material materialCache;
        private BodyAuthoring bodyAuthoring;
        private GroundDecorProfile decorProfile;
        private Matrix4x4[] decorBatchScratch;
        private Matrix4x4[] decorPerMeshScratch;

        private readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
        private readonly List<Node> desired = new List<Node>();
        private readonly HashSet<long> keep = new HashSet<long>();
        private readonly HashSet<long> splitNodes = new HashSet<long>();
        private readonly HashSet<long> splitNext = new HashSet<long>();
        private readonly List<long> toEvict = new List<long>();
        private int firstFrameGuard = -1;
        private int diagnosticChunksLogged;
        private Vector3 bodyRenderPosition;
        private Quaternion currentBodyRotation = Quaternion.identity;
        private Vector3d currentBodyPosition;
        private QuaternionD currentBodyOrientation = QuaternionD.Identity;

        private void Start()
        {
            if (Runner == null || Runner.SystemState == null)
            {
                enabled = false;
                return;
            }

            foreach (OrbitingBody candidate in Runner.SystemState.AllBodies)
            {
                if (candidate.Name == gameObject.name)
                {
                    body = candidate;
                    break;
                }
            }

            terrain = body?.Terrain as HeightfieldTerrain;
            if (body == null || terrain == null)
            {
                // Гладкая сфера или тело не найдено — старый визуал не трогаем.
                enabled = false;
                return;
            }

            noiseParams = TerrainNoiseParams.FromTerrain(terrain);
            // Примитив-сфера тела — только заглушка на время, пока не был
            // cube-sphere. Её радиус равен среднему (уровню моря): с включёнными
            // чанками она копланарна океану и мерцает, поэтому гасим навсегда.
            // Непрозрачность планеты на любой дистанции теперь даёт сам рельеф
            // (cube-sphere рендерится и в космосе, без высотного порога).
            MeshRenderer sphereRenderer = GetComponent<MeshRenderer>();
            if (sphereRenderer != null)
            {
                sphereRenderer.enabled = false;
            }


            if (Runner.SystemView == null)
            {
                Runner.SystemView = new BodyTransformRegistry();
            }

            // Identity-корень для чанков: чанки НЕ под трансформом тела (иначе
            // их координаты ~радиус планеты и float-дрожь). Позицию/поворот
            // каждого чанка ставим каждый кадр через FloatingOrigin.
            surfaceRoot = new GameObject("PlanetSurfaceChunks").transform;
            surfaceRoot.position = Vector3.zero;
            surfaceRoot.rotation = Quaternion.identity;
            surfaceRoot.localScale = Vector3.one;

            bodyAuthoring = GetComponent<BodyAuthoring>();
            decorProfile = bodyAuthoring != null && bodyAuthoring.GroundDecorPreset != null
                ? bodyAuthoring.GroundDecorPreset.Profile
                : null;
            // Чистое состояние на каждый Play: с Enter Play Mode Options (без
            // Reload Scene/Domain) приватные поля компонента могут пережить
            // прошлый запуск, а их чанки уже уничтожены.
            ClearChunkCache();
        }

        private void OnDestroy()
        {
            if (surfaceRoot != null)
            {
                Destroy(surfaceRoot.gameObject);
            }

            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                DisposeDecor(kv.Value);
            }

            if (materialCache != null)
            {
                Destroy(materialCache);
            }
        }

        /// <summary>Сбросить кэш мешей (после live-смены параметров рельефа).</summary>
        [ContextMenu("Clear chunk cache")]
        public void ClearChunkCache()
        {
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                DisposeDecor(kv.Value);
                if (kv.Value.Go != null)
                {
                    Destroy(kv.Value.Go);
                }
            }

            chunks.Clear();
            splitNodes.Clear();
        }

        private static void DisposeDecor(Chunk chunk)
        {
            for (int i = 0; i < chunk.Decor.Count; i++)
            {
                if (chunk.Decor[i].Instances.IsCreated)
                {
                    chunk.Decor[i].Instances.Dispose();
                }
            }

            chunk.Decor.Clear();
        }

        /// <summary>
        /// Live-тюнинг в Play: перенести параметры рельефа из соседнего
        /// BodyAuthoring в живой HeightfieldTerrain, обновить снимок шума и
        /// перестроить все чанки. Вне Play меняет только сериализованные поля.
        /// </summary>
        [ContextMenu("Apply authoring params + rebuild")]
        public void ApplyAuthoringAndRebuild()
        {
            BodyAuthoring authoring = GetComponent<BodyAuthoring>();
            if (authoring == null || terrain == null)
            {
                if (Application.isPlaying)
                {
                    Debug.LogWarning("[PlanetSurfaceRenderer] нет BodyAuthoring/HeightfieldTerrain — нечего применять.");
                }

                return;
            }

            authoring.ApplyToTerrain(terrain);
            noiseParams = TerrainNoiseParams.FromTerrain(terrain);
            if (materialCache != null)
            {
                ApplyTerrainUniforms(materialCache);
            }

            ClearChunkCache();
            firstFrameGuard = -1;
        }

        /// <summary>
        /// Live-тюнинг: если поля рельефа в BodyAuthoring изменили в Play,
        /// переносим их в живой terrain и перестраиваем чанки автоматически —
        /// иначе правки в Inspector не влияют на уже построенную геометрию.
        /// </summary>
        private void Update()
        {
            if (body == null || terrain == null)
            {
                return;
            }

            if (bodyAuthoring == null)
            {
                return;
            }

            bodyAuthoring.ApplyToTerrain(terrain);

            GroundDecorProfile currentDecor = bodyAuthoring.GroundDecorPreset != null
                ? bodyAuthoring.GroundDecorPreset.Profile
                : null;
            if (!ReferenceEquals(currentDecor, decorProfile))
            {
                decorProfile = currentDecor;
                ClearChunkCache();
                firstFrameGuard = -1;
            }

            TerrainNoiseParams current = TerrainNoiseParams.FromTerrain(terrain);
            if (!ParamsEqual(current, noiseParams))
            {
                noiseParams = current;
                if (materialCache != null)
                {
                    ApplyTerrainUniforms(materialCache);
                }

                ClearChunkCache();
                firstFrameGuard = -1;
            }
            else if (materialCache != null && !ColorStateEquals(CaptureColorState(), colorStateCache))
            {
                // Правки ТОЛЬКО цвета (сила/частота моттлинга, пороги скалы/снега)
                // не меняют геометрию — перестраивать чанки не нужно, достаточно
                // перелить униформы в материал.
                ApplyTerrainUniforms(materialCache);
            }
        }

        /// <summary>Снимок цветовых униформ (без геометрии) для live-тюнинга.</summary>
        private struct TerrainColorState
        {
            public double RockSlopeTan;
            public double RockSlopeWidth;
            public double RockHeightMin;
            public double SnowSlopeTan;
            public double NoiseFrequency;
            public int NoiseOctaves;
            public double NoiseStrength;
            public double DetailFrequency;
            public int DetailOctaves;
            public double DetailStrength;
            public TerrainPaletteData Palette;
        }

        private TerrainColorState colorStateCache;

        private TerrainColorState CaptureColorState()
        {
            return new TerrainColorState
            {
                RockSlopeTan = terrain.ColorRockSlopeTan,
                RockSlopeWidth = terrain.ColorRockSlopeWidth,
                RockHeightMin = terrain.ColorRockHeightMin,
                SnowSlopeTan = terrain.ColorSnowSlopeTan,
                NoiseFrequency = terrain.ColorNoiseFrequency,
                NoiseOctaves = terrain.ColorNoiseOctaves,
                NoiseStrength = terrain.ColorNoiseStrength,
                DetailFrequency = terrain.ColorDetailFrequency,
                DetailOctaves = terrain.ColorDetailOctaves,
                DetailStrength = terrain.ColorDetailStrength,
                Palette = terrain.Palette
            };
        }

        private static bool ColorStateEquals(TerrainColorState a, TerrainColorState b)
        {
            return a.RockSlopeTan == b.RockSlopeTan
                && a.RockSlopeWidth == b.RockSlopeWidth
                && a.RockHeightMin == b.RockHeightMin
                && a.SnowSlopeTan == b.SnowSlopeTan
                && a.NoiseFrequency == b.NoiseFrequency
                && a.NoiseOctaves == b.NoiseOctaves
                && a.NoiseStrength == b.NoiseStrength
                && a.DetailFrequency == b.DetailFrequency
                && a.DetailOctaves == b.DetailOctaves
                && a.DetailStrength == b.DetailStrength
                && ((a.Palette == null && b.Palette == null)
                    || (a.Palette != null && a.Palette.Matches(b.Palette)));
        }

        private static bool ParamsEqual(TerrainNoiseParams a, TerrainNoiseParams b)
        {
            return a.Seed == b.Seed
                && a.BaseFrequency == b.BaseFrequency
                && a.Octaves == b.Octaves
                && a.Lacunarity == b.Lacunarity
                && a.Gain == b.Gain
                && a.ContinentFrequency == b.ContinentFrequency
                && a.ContinentOctaves == b.ContinentOctaves
                && a.ContinentThreshold == b.ContinentThreshold
                && a.ContinentSharpness == b.ContinentSharpness
                && a.ContinentDepth == b.ContinentDepth
                && a.RidgedMix == b.RidgedMix
                && a.PlainMix == b.PlainMix
                && a.PlainFrequency == b.PlainFrequency
                && a.PlainOctaves == b.PlainOctaves
                && a.PlainThreshold == b.PlainThreshold
                && a.PlainSharpness == b.PlainSharpness
                && a.PlainElevation == b.PlainElevation
                && a.DetailMix == b.DetailMix
                && a.DetailFrequency == b.DetailFrequency
                && a.DetailOctaves == b.DetailOctaves
                && a.WarpStrength == b.WarpStrength
                && a.WarpFrequency == b.WarpFrequency
                && a.WarpOctaves == b.WarpOctaves
                && a.WarpSeedOffset == b.WarpSeedOffset
                && a.ColorNoiseFrequency == b.ColorNoiseFrequency
                && a.ColorNoiseOctaves == b.ColorNoiseOctaves
                && a.ColorNoiseSeedOffset == b.ColorNoiseSeedOffset
                && a.ColorDetailFrequency == b.ColorDetailFrequency
                && a.ColorDetailOctaves == b.ColorDetailOctaves
                && a.ColorDetailSeedOffset == b.ColorDetailSeedOffset
                && a.ComputeMask == b.ComputeMask
                && a.ComputeDetail == b.ComputeDetail;
        }

        private void LateUpdate()
        {
            if (body == null || terrain == null || Runner.Ship == null)
            {
                return;
            }
            if (Runner.DominantBody != body)
            {
                SetAllInvisible();
                return;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPosition, out _);

            // [ГРАФИКА] Сглаживание (SMAA/TAA) живёт на этом же Main Camera:
            // HDAdditionalCameraData в сцене (antialiasing / SMAAQuality / TAA*).
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 cameraPosition = camera.transform.position;
            Shader.SetGlobalVector("_PlanetCameraPos", cameraPosition);

            // Траверс ЛОД — каждый кадр. Раньше был гистерезис по рендер-позиции
            // камеры, но под floating origin она ~0 и при движении игрока по телу
            // почти не меняется → набор узлов «замирал» на спавне и дальние чанки
            // устаревали/кособочились. Траверс дёшев (меши кэшированы).
            bodyRenderPosition = FloatingOrigin.ToRender(bodyPosition);
            currentBodyPosition = bodyPosition;
            currentBodyOrientation = body.GetVisualOrientation(Runner.TimeSeconds);
            currentBodyRotation = FloatingOrigin.RenderRotation(currentBodyOrientation);

            {
                // Тел-fixed направления для current-кадра.
                desired.Clear();
                splitNext.Clear();
                for (int face = 0; face < CubeSphere.FaceCount; face++)
                {
                    Traverse(face, 0, 0, 0, cameraPosition);
                }

                // Зафиксировать подразбиение кадра — база гистерезиса в Traverse
                // на следующем кадре, чтобы узел не дробился/сливался каждый кадр.
                splitNodes.Clear();
                splitNodes.UnionWith(splitNext);

                // Сначала СТРОИМ/ПОКАЗЫВАЕМ желаемые и только потом считаем keep
                // по фактическому состоянию кадра. Иначе на кадре появления ребёнка
                // keep ещё включает родителя (ребёнок был кэш-промахом) — грубый и
                // подробный чанки видны вместе: z-fight/«хлопок» на каждом переходе.
                int budget = firstFrameGuard < 0 ? int.MaxValue : BuildsPerFrame;
                firstFrameGuard = 0;
                int built = 0;
                for (int i = 0; i < desired.Count; i++)
                {
                    long id = NodeId(desired[i]);
                    if (chunks.TryGetValue(id, out Chunk cached))
                    {
                        if (!cached.Visible)
                        {
                            cached.Go.SetActive(true);
                            cached.Visible = true;
                        }

                        continue;
                    }

                    if (built >= budget)
                    {
                        continue;
                    }

                    BuildChunk(desired[i]);
                    built++;
                }

                // keep = построенные желаемые листья + предки тех желаемых, что не
                // успели построить в этом кадре («затычка» от дыр на смене LOD).
                keep.Clear();
                for (int i = 0; i < desired.Count; i++)
                {
                    if (chunks.ContainsKey(NodeId(desired[i])))
                    {
                        keep.Add(NodeId(desired[i]));
                    }
                }

                for (int i = 0; i < desired.Count; i++)
                {
                    Node node = desired[i];
                    if (chunks.ContainsKey(NodeId(node)))
                    {
                        continue;
                    }

                    int face = node.Face;
                    int depth = node.Depth;
                    int ix = node.Ix;
                    int iy = node.Iy;
                    while (depth > 0)
                    {
                        depth--;
                        ix >>= 1;
                        iy >>= 1;
                        keep.Add(NodeId(face, depth, ix, iy));
                    }
                }

                // Прячем всё видимое, чего нет в keep.
                foreach (KeyValuePair<long, Chunk> kv in chunks)
                {
                    if (kv.Value.Visible && !keep.Contains(kv.Key))
                    {
                        kv.Value.Go.SetActive(false);
                        kv.Value.Visible = false;
                    }
                }

                EvictIfNeeded();
            }

            // Трансформы видимых чанков — каждый кадр (floating origin + спин).
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                if (kv.Value.Visible)
                {
                    kv.Value.Go.transform.position = NodeRenderPosition(kv.Value.CenterAstro);
                    kv.Value.Go.transform.rotation = currentBodyRotation;
                }
            }

            // Декор после расстановки трансформов чанков: матрицы инстансов
            // берут мировой трансформ чанка текущего кадра.
            Shader.SetGlobalFloat("_GroundDecorTime", Time.time);
            UpdateDecorCollision();
            DrawDecor(cameraPosition);
        }

        /// <summary>
        /// Обновить цилиндры-коллайдеры стволов (слои с Collides) для физики
        /// игрока: инстансы только из чанков в ~60 м от игрока, позиции — в
        /// инерциальный astro-кадр (как PlayerPosition).
        /// </summary>
        private void UpdateDecorCollision()
        {
            if (decorProfile == null || decorProfile.Layers == null || body == null || Runner == null)
            {
                return;
            }

            GroundDecorCollisionRegistry.Begin(body.Name);
            bool anyCollides = false;
            for (int i = 0; i < decorProfile.Layers.Count; i++)
            {
                GroundDecorLayer layer = decorProfile.Layers[i];
                if (layer != null && layer.Enabled && layer.Collides)
                {
                    anyCollides = true;
                    break;
                }
            }

            if (!anyCollides)
            {
                return;
            }

            const float collisionRange = 60f;
            Vector3 playerRender = FloatingOrigin.ToRender(Runner.PlayerPosition);
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (chunk.Decor.Count == 0)
                {
                    continue;
                }

                float chunkDistance = Mathf.Max(
                    0f, Vector3.Distance(playerRender, chunk.Go.transform.position) - chunk.BoundsRadius);
                // Коллайдеры нужны независимо от видимости: во время LOD-перехода
                // ближний чанк может быть скрыт, но стволы физически на месте.
                if (chunkDistance > collisionRange)
                {
                    continue;
                }

                Matrix4x4 chunkMatrix = chunk.Go.transform.localToWorldMatrix;
                for (int i = 0; i < chunk.Decor.Count; i++)
                {
                    DecorLayerRuntime runtime = chunk.Decor[i];
                    GroundDecorLayer layer = runtime.Profile;
                    if (layer == null || !layer.Collides || runtime.Instances.Length == 0)
                    {
                        continue;
                    }

                    int count = runtime.Instances.Length;
                    for (int k = 0; k < count; k++)
                    {
                        GroundDecorInstance instance = runtime.Instances[k];
                        Vector3 localPosition = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);
                        if ((chunkMatrix.MultiplyPoint3x4(localPosition) - playerRender).sqrMagnitude
                            > collisionRange * collisionRange)
                        {
                            continue;
                        }

                        Vector3 localNormal = new Vector3(instance.Normal.x, instance.Normal.y, instance.Normal.z);
                        if (localNormal.sqrMagnitude < 1e-6f)
                        {
                            localNormal = Vector3.up;
                        }

                        Vector3d relAstro = AstroFrame.ToAstro(localPosition);
                        Vector3d bodyFixed = chunk.CenterAstro + relAstro;
                        Vector3d inertial = currentBodyPosition + currentBodyOrientation.Rotate(bodyFixed);
                        Vector3d upAstro = AstroFrame.ToAstro(localNormal).Normalized;
                        Vector3d upInertial = currentBodyOrientation.Rotate(upAstro);
                        GroundDecorCollisionRegistry.Add(
                            inertial, upInertial, layer.CollisionRadiusMeters, layer.CollisionHeightMeters);
                    }
                }
            }
        }

        private void Traverse(int face, int depth, int ix, int iy, Vector3 cameraPosition)
        {
            Vector3d dir = CubeSphere.NodeCenterDirection(face, depth, ix, iy);
            Vector3 worldCenter = NodeRenderPosition(dir * body.Radius);

            Vector3 bodyToCam = cameraPosition - bodyRenderPosition;
            Vector3 bodyToNode = worldCenter - bodyRenderPosition;
            if (bodyToNode.sqrMagnitude > 1e-6f && bodyToCam.sqrMagnitude > 1e-6f)
            {
                // Задняя полусфера не строится.
                if (Vector3.Dot(bodyToNode.normalized, bodyToCam.normalized) < -0.15f)
                {
                    return;
                }
            }

            double nodeSize = body.Radius * 1.5707963267948966d / (1 << depth);
            float distance = Vector3.Distance(cameraPosition, worldCenter);
            // Дистанция до БЛИЖАЙШЕЙ точки узла, а не до центра: у крупного
            // узла центр может быть далеко за порогом, хотя его край виден
            // вплотную. С центром такой узел не дробился, а при повороте камеры
            // край «внезапно» требовал детализации — LOD-скачок/мигание.
            // Полудиагональ патча ≈ nodeSize·√2/2.
            float closestDistance = Mathf.Max(0f, distance - (float)(nodeSize * 0.70710678d));

            bool split;
            if (depth < MaxDepth && desired.Count + 4 < MaxNodes)
            {
                // Гистерезис: делим на splitDistance, а сливаем только когда ушли
                // заметно дальше (×MergeHysteresis). Между порогами держим прошлый
                // уровень — иначе узел «дробится/сливается» каждый кадр (LOD-флап,
                // сильнее заметный на скорости).
                double splitDistance = nodeSize * SplitFactor;
                split = splitNodes.Contains(NodeId(face, depth, ix, iy))
                    ? closestDistance < splitDistance * MergeHysteresis
                    : closestDistance < splitDistance;
            }
            else
            {
                split = false;
            }

            if (split)
            {
                splitNext.Add(NodeId(face, depth, ix, iy));
                Traverse(face, depth + 1, (ix * 2) + 0, (iy * 2) + 0, cameraPosition);
                Traverse(face, depth + 1, (ix * 2) + 1, (iy * 2) + 0, cameraPosition);
                Traverse(face, depth + 1, (ix * 2) + 0, (iy * 2) + 1, cameraPosition);
                Traverse(face, depth + 1, (ix * 2) + 1, (iy * 2) + 1, cameraPosition);
                return;
            }

            desired.Add(new Node { Face = face, Depth = depth, Ix = ix, Iy = iy });
        }

        /// <summary>
        /// Рендер-позиция точки, заданной тел-fixed вектором от центра тела.
        /// Считаем В DOUBLE (bodyPos + orientation·local − anchor), потом float:
        /// иначе сумма двух больших float-чисел (~радиус) теряет точность и
        /// чанк/камера дрожат на десятки сантиметров.
        /// </summary>
        private Vector3 NodeRenderPosition(Vector3d centerBodyFixed)
        {
            Vector3d absolute = currentBodyPosition + currentBodyOrientation.Rotate(centerBodyFixed);
            return FloatingOrigin.ToRender(absolute);
        }

        private void BuildChunk(Node node)
        {
            int res = System.Math.Max(2, TileResolution);
            int n = res + 1;
            int grid = n + 2;
            int coreCount = n * n;
            int perEdge = n;
            int totalVerts = coreCount + (4 * perEdge);
            int totalTris = (((n - 1) * (n - 1)) + (4 * (n - 1))) * 6;

            double sizeUv = 1d / (1 << node.Depth);
            double u0 = node.Ix * sizeUv;
            double v0 = node.Iy * sizeUv;
            double stepUv = sizeUv / (n - 1);

            // Центр узла (тел-fixed, double): вершины меша храним ЛОКАЛЬНО
            // относительно него. Иначе float на радиусе планеты (~1.1e6 м) даёт
            // ulp ~0.06 м → камера/рельеф дрожат; огромные вершины ломают и
            // camera-relative рендер (трансформ тела в якоре ~0).
            Vector3d centerAstro = CubeSphere.NodeCenterDirection(node.Face, node.Depth, node.Ix, node.Iy) * body.Radius;
            Vector3 centerUnity = AstroFrame.ToSimulation(centerAstro);

            double amplitude = System.Math.Max(1d, terrain.AmplitudeMeters);
            double seaLevel = terrain.SeaLevelMeters;
            double seaEps = amplitude * 0.001d;

            // Тайлы с оверлапом в 1 вершину — нормали из центральных разностей.
            var dirs = new NativeArray<double3>(grid * grid, Allocator.TempJob);
            var jobHeights = new NativeArray<double>(grid * grid, Allocator.TempJob);
            var jobMasks = new NativeArray<float>(grid * grid, Allocator.TempJob);
            var jobDetails = new NativeArray<float>(grid * grid, Allocator.TempJob);

            for (int gi = 0; gi < grid; gi++)
            {
                double u = u0 + ((gi - 1) * stepUv);
                for (int gj = 0; gj < grid; gj++)
                {
                    double v = v0 + ((gj - 1) * stepUv);
                    Vector3d dir = CubeSphere.Direction(node.Face, u, v);
                    dirs[(gi * grid) + gj] = new double3(dir.X, dir.Y, dir.Z);
                }
            }

            // Цвет маски/детали теперь считается на пиксель в шейдере — в
            // tile-job'е эти потоки не нужны. Правку делаем в ЛОКАЛЬНОЙ копии,
            // чтобы не сломать ParamsEqual по кэшированному noiseParams.
            TerrainNoiseParams jobParams = noiseParams;
            jobParams.ComputeMask = false;
            jobParams.ComputeDetail = false;

            TerrainTileJob tileJob = new TerrainTileJob
            {
                Params = jobParams,
                Directions = dirs,
                Heights = jobHeights,
                ColorMasks = jobMasks,
                ColorDetails = jobDetails
            };
            tileJob.Schedule(grid * grid, 64, new JobHandle()).Complete();

            var positions = new Vector3[grid * grid];
            for (int gi = 0; gi < grid; gi++)
            {
                for (int gj = 0; gj < grid; gj++)
                {
                    int vi = (gi * grid) + gj;
                    double3 d = dirs[vi];
                    double rawHeight = jobHeights[vi] * amplitude;
                    double height = rawHeight < seaLevel ? seaLevel : rawHeight;
                    Vector3d absAstro = new Vector3d(d.x, d.y, d.z) * (body.Radius + height);
                    positions[vi] = AstroFrame.ToSimulation(absAstro - centerAstro);
                }
            }

            // dirs/jobHeights ещё нужны для per-pixel атрибутов ниже.
            jobMasks.Dispose();
            jobDetails.Dispose();

            var vertices = new Vector3[totalVerts];
            var normals = new Vector3[totalVerts];
            // Альбедо считается НА ПИКСЕЛЬ во фрагментном шейдере (per-pixel
            // процедурный цвет): на грубом LOD вершинный цвет давал блочные
            // границы пятен. Сюда кладём только то, что нельзя вывести из
            // геометрии: тел-fixed направление, сырую высоту (до клампа морем)
            // и косинус уклона (rock/snow-полосы по склону).
            var surfaceDirs = new Vector3[totalVerts];
            var surfaceExtra = new Vector2[totalVerts];

            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < n; col++)
                {
                    int halo = ((row + 1) * grid) + (col + 1);
                    int index = (row * n) + col;
                    Vector3 position = positions[halo];
                    vertices[index] = position;

                    Vector3 dCol = positions[halo + 1] - positions[halo - 1];
                    Vector3 dRow = positions[halo + grid] - positions[halo - grid];
                    Vector3 normal = Vector3.Cross(dCol, dRow);
                    float normalLength = normal.magnitude;
                    Vector3 radial = (position + centerUnity).normalized;
                    normals[index] = normalLength > 1e-10f ? normal / normalLength : radial;
                    if (Vector3.Dot(normals[index], radial) < 0f)
                    {
                        normals[index] = -normals[index];
                    }

                    double3 d = dirs[halo];
                    surfaceDirs[index] = new Vector3((float)d.x, (float)d.y, (float)d.z);
                    double rawHeight = jobHeights[halo] * amplitude;
                    surfaceExtra[index] = new Vector2(
                        (float)rawHeight, Vector3.Dot(normals[index], radial));
                }
            }

            dirs.Dispose();
            jobHeights.Dispose();

            if (diagnosticChunksLogged < 8)
            {
                diagnosticChunksLogged++;
                int landCore = 0;
                double minH = double.MaxValue;
                double maxH = double.MinValue;
                for (int k = 0; k < coreCount; k++)
                {
                    double raw = surfaceExtra[k].x;
                    if (raw > seaLevel + seaEps)
                    {
                        landCore++;
                    }

                    minH = System.Math.Min(minH, raw);
                    maxH = System.Math.Max(maxH, raw);
                }

                Debug.Log(string.Format(
                    "[PlanetSurface] чанк f{0} d{1}: суша {2:P0} верш., h=[{3:F0}..{4:F0}] м",
                    node.Face, node.Depth, (double)landCore / coreCount, minH, maxH));
            }

            // Юбки: дублируем рёбра, утопленные к центру планеты — перекрывают
            // трещины между соседними LOD-уровнями.
            float skirtDepth = (float)(body.Radius * 1.5707963267948966d / (1 << node.Depth) * SkirtFactor);
            for (int edge = 0; edge < 4; edge++)
            {
                int baseIndex = coreCount + (edge * perEdge);
                for (int k = 0; k < perEdge; k++)
                {
                    int coreIndex;
                    switch (edge)
                    {
                        case 0: coreIndex = k; break;                        // row 0
                        case 1: coreIndex = (res * n) + k; break;            // row res
                        case 2: coreIndex = k * n; break;                    // col 0
                        default: coreIndex = (k * n) + res; break;           // col res
                    }

                    Vector3 p = vertices[coreIndex];
                    Vector3 inward = (p + centerUnity).normalized;
                    vertices[baseIndex + k] = p - (inward * skirtDepth);
                    normals[baseIndex + k] = normals[coreIndex];
                    surfaceDirs[baseIndex + k] = surfaceDirs[coreIndex];
                    surfaceExtra[baseIndex + k] = surfaceExtra[coreIndex];
                }
            }

            var triangles = new int[totalTris];
            int t = 0;
            for (int row = 0; row < n - 1; row++)
            {
                for (int col = 0; col < n - 1; col++)
                {
                    int a = (row * n) + col;
                    int b = a + 1;
                    int c = a + n;
                    int d = c + 1;
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            for (int edge = 0; edge < 4; edge++)
            {
                int baseIndex = coreCount + (edge * perEdge);
                for (int k = 0; k < n - 1; k++)
                {
                    int a;
                    int b;
                    switch (edge)
                    {
                        case 0: a = k; b = k + 1; break;
                        case 1: a = (res * n) + k; b = a + 1; break;
                        case 2: a = k * n; b = a + n; break;
                        default: a = (k * n) + res; b = a + n; break;
                    }

                    int c = baseIndex + k;
                    int d = baseIndex + k + 1;
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            long id = NodeId(node);
            Chunk chunk = new Chunk
            {
                Go = new GameObject("PlanetChunk_" + node.Face + "_" + node.Depth + "_" + node.Ix + "_" + node.Iy),
                Mesh = new Mesh()
            };
            chunk.Mesh.MarkDynamic();
            chunk.Go.transform.SetParent(surfaceRoot, false);
            chunk.Go.transform.localScale = Vector3.one;
            chunk.CenterAstro = centerAstro;
            chunk.Depth = node.Depth;
            chunk.BoundsRadius = (float)(body.Radius * 1.5707963267948966d / (1 << node.Depth) * 0.70710678d);
            chunk.Go.transform.position = NodeRenderPosition(centerAstro);
            chunk.Go.transform.rotation = currentBodyRotation;
            MeshFilter filter = chunk.Go.AddComponent<MeshFilter>();
            chunk.Renderer = chunk.Go.AddComponent<MeshRenderer>();
            chunk.Renderer.sharedMaterial = GetMaterial();
            filter.sharedMesh = chunk.Mesh;

            chunk.Mesh.Clear();
            chunk.Mesh.vertices = vertices;
            chunk.Mesh.normals = normals;
            chunk.Mesh.SetUVs(1, new List<Vector3>(surfaceDirs));
            chunk.Mesh.SetUVs(2, new List<Vector2>(surfaceExtra));
            chunk.Mesh.triangles = triangles;
            chunk.Mesh.RecalculateBounds();
            // Запас к границам (полразмера узла): страховка от ложного
            // фрустум-куллинга чанка на краю кадра — иначе при повороте камеры
            // далёкие чанки мигают («появляется/пропадает»). Цена — чуть меньше
            // отсекается за кадром, точность видимости не страдает.
            Bounds paddedBounds = chunk.Mesh.bounds;
            paddedBounds.Expand(chunk.BoundsRadius);
            chunk.Mesh.bounds = paddedBounds;

            chunk.Visible = true;
            chunks[id] = chunk;
            BuildChunkDecor(node, chunk, centerAstro);
        }

        /// <summary>
        /// Построить инстансы декора для чанка: кандидаты — джиттер-сетка в UV
        /// чанка, фильтры/высоты — Burst-джоба на той же TerrainNoise, что
        /// физика. Результат кэшируется в чанке (тел-fixed рельеф статичен) и
        /// живёт до вытеснения чанка.
        /// </summary>
        private void BuildChunkDecor(Node node, Chunk chunk, Vector3d centerAstro)
        {
            if (decorProfile == null || decorProfile.Layers == null || decorProfile.Layers.Count == 0)
            {
                return;
            }

            double sizeUv = 1d / (1 << node.Depth);
            double u0 = node.Ix * sizeUv;
            double v0 = node.Iy * sizeUv;
            double chunkArc = body.Radius * 1.5707963267948966d * sizeUv;

            for (int layerIndex = 0; layerIndex < decorProfile.Layers.Count; layerIndex++)
            {
                GroundDecorLayer layer = decorProfile.Layers[layerIndex];
                if (layer == null || !layer.Enabled || layer.NearMeshes == null || layer.NearMeshes.Length == 0
                    || layer.MaxInstancesPerChunk <= 0)
                {
                    continue;
                }

                double spacing = System.Math.Max(0.25d, layer.SpacingMeters);
                int cells = (int)System.Math.Ceiling(chunkArc / spacing);
                cells = System.Math.Max(1, System.Math.Min(System.Math.Max(1, layer.MaxCellsPerAxis), cells));
                int count = cells * cells;

                var dirs = new NativeArray<double3>(count, Allocator.TempJob);
                var randoms = new NativeArray<double3>(count, Allocator.TempJob);
                var meshPicks = new NativeArray<double>(count, Allocator.TempJob);
                var accepted = new NativeArray<int>(count, Allocator.TempJob);
                var instances = new NativeArray<GroundDecorInstance>(count, Allocator.TempJob);

                for (int a = 0; a < cells; a++)
                {
                    double uJitter = GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Ix, (a * 97) + node.Iy, 11);
                    for (int b = 0; b < cells; b++)
                    {
                        int index = (a * cells) + b;
                        double vJitter = GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Iy, (b * 89) + node.Ix, 12);
                        double u = u0 + (((a + uJitter) / cells) * sizeUv);
                        double v = v0 + (((b + vJitter) / cells) * sizeUv);
                        Vector3d direction = CubeSphere.Direction(node.Face, u, v);
                        dirs[index] = new double3(direction.X, direction.Y, direction.Z);
                        randoms[index] = new double3(
                            GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Ix + node.Iy, (index * 31) + layerIndex, 21),
                            GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Iy, (index * 37) + layerIndex, 22),
                            GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Ix, (index * 41) + layerIndex, 23));
                        meshPicks[index] = GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Ix, (index * 43) + layerIndex, 24);
                    }
                }

                GroundDecorPlacementParams placement = GroundDecorPlacementParams.FromLayer(layer, terrain, body.Radius, centerAstro);
                var job = new GroundDecorCandidateJob
                {
                    Directions = dirs,
                    Randoms = randoms,
                    MeshPicks = meshPicks,
                    Terrain = noiseParams,
                    Placement = placement,
                    Accepted = accepted,
                    Instances = instances
                };
                job.Schedule(count, 64, new JobHandle()).Complete();

                int acceptedCount = 0;
                for (int i = 0; i < count; i++)
                {
                    if (accepted[i] != 0)
                    {
                        acceptedCount++;
                    }
                }

                dirs.Dispose();
                randoms.Dispose();
                meshPicks.Dispose();

                int take = System.Math.Min(acceptedCount, layer.MaxInstancesPerChunk);
                if (take > 0)
                {
                    var persisted = new NativeArray<GroundDecorInstance>(take, Allocator.Persistent);
                    int write = 0;
                    for (int i = 0; i < count && write < take; i++)
                    {
                        if (accepted[i] != 0)
                        {
                            persisted[write++] = instances[i];
                        }
                    }

                    chunk.Decor.Add(new DecorLayerRuntime
                    {
                        Profile = layer,
                        Instances = persisted,
                        Matrices = new Matrix4x4[write]
                    });
                }

                accepted.Dispose();
                instances.Dispose();
            }
        }

        /// <summary>
        /// Нарисовать декор видимых чанков: near — solid-меш, дальше —
        /// биллборд до MaxDistance. Матрицы — мировой трансформ чанка ×
        /// локальный TRS инстанса (пересчёт каждый кадр: floating origin/спин).
        /// </summary>
        private void DrawDecor(Vector3 cameraPosition)
        {
            if (decorProfile == null)
            {
                return;
            }

            // Дистанция декора — тангенциальная (по касательной к поверхности):
            // высота полёта НЕ выключает весь декор разом. Иначе на наборе
            // высоты ~MaxDistance всё исчезало одной границей (деревья ~3 км).
            Vector3 cameraUp = cameraPosition - bodyRenderPosition;
            if (cameraUp.sqrMagnitude < 1e-6f)
            {
                cameraUp = Vector3.up;
            }
            else
            {
                cameraUp.Normalize();
            }

            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (!chunk.Visible || chunk.Decor.Count == 0)
                {
                    continue;
                }

                Matrix4x4 chunkMatrix = chunk.Go.transform.localToWorldMatrix;
                Vector3 toChunk = chunk.Go.transform.position - cameraPosition;
                Vector3 tangential = toChunk - (Vector3.Dot(toChunk, cameraUp) * cameraUp);
                float distance = Mathf.Max(0f, tangential.magnitude - chunk.BoundsRadius);
                for (int i = 0; i < chunk.Decor.Count; i++)
                {
                    DecorLayerRuntime runtime = chunk.Decor[i];
                    GroundDecorLayer layer = runtime.Profile;
                    if (distance > layer.MaxDistanceMeters || runtime.Instances.Length == 0)
                    {
                        continue;
                    }

                    // Слой без billboard-меша (деревья) всегда идёт 3D-путём:                    // иначе его рисовало бы камеро-ориентированным и он крутился
                    // бы за игроком.
                    bool near = distance <= layer.NearDistanceMeters || layer.FarBillboardMesh == null;
                    Material material = near ? layer.NearMaterial : layer.FarMaterial;
                    if (material == null)
                    {
                        material = layer.NearMaterial;
                    }

                    if (material == null)
                    {
                        continue;
                    }

                    int count = runtime.Instances.Length;
                    if (near)
                    {
                        for (int k = 0; k < count; k++)
                        {
                            GroundDecorInstance instance = runtime.Instances[k];
                            Vector3 position = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);

                            // Ориентация по НОРМАЛИ поверхности: локальный +Y меша
                            // (верх карточки/дерева) смотрит вдоль радиали, yaw —
                            // поворот вокруг неё. Раньше был yaw вокруг мировой Y:
                            // на широтах карточки ложились плашмя и тонули в земле.
                            Vector3 up = new Vector3(instance.Normal.x, instance.Normal.y, instance.Normal.z);
                            if (up.sqrMagnitude < 1e-6f)
                            {
                                up = Vector3.up;
                            }
                            else
                            {
                                up.Normalize();
                            }

                            Vector3 reference = Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right;
                            Vector3 tangent = Vector3.Cross(reference, up).normalized;
                            Quaternion rotation;
                            if (layer.FlatOnGround)
                            {
                                // Плашмя, без случайного поворота: «пятачок» лежит
                                // ровно (локальный Z меша = нормаль поверхности).
                                rotation = Quaternion.LookRotation(up, tangent);
                            }
                            else
                            {
                                Vector3 forward = Vector3.Cross(up, tangent);
                                Quaternion align = Quaternion.LookRotation(forward, up);
                                rotation = Quaternion.AngleAxis(instance.Yaw * 57.29578f, up) * align;
                            }

                            Vector3 scale = new Vector3(instance.Scale, instance.Scale, instance.Scale);
                            runtime.Matrices[k] = chunkMatrix * Matrix4x4.TRS(position, rotation, scale);
                        }

                        if (layer.NearMeshes.Length > 1)
                        {
                            EnsureDecorScratch(count);
                            for (int m = 0; m < layer.NearMeshes.Length; m++)
                            {
                                Mesh variant = layer.NearMeshes[m];
                                if (variant == null)
                                {
                                    continue;
                                }

                                int written = 0;
                                for (int k = 0; k < count; k++)
                                {
                                    if (runtime.Instances[k].MeshIndex == m)
                                    {
                                        decorPerMeshScratch[written++] = runtime.Matrices[k];
                                    }
                                }

                                if (written > 0)
                                {
                                    DrawDecorBatches(variant, material, decorPerMeshScratch, written);
                                }
                            }
                        }
                        else if (layer.NearMeshes[0] != null)
                        {
                            DrawDecorBatches(layer.NearMeshes[0], material, runtime.Matrices, count);
                        }
                    }
                    else
                    {
                        Mesh billboard = layer.FarBillboardMesh != null ? layer.FarBillboardMesh : layer.NearMeshes[0];
                        if (billboard == null)
                        {
                            continue;
                        }

                        // Биллборд в МИРОВЫХ координатах: локальный +Y инстанса =
                        // нормаль поверхности, +Z = взгляд камеры в касательной
                        // плоскости. Вся ориентация здесь, шейдер — passthrough.
                        for (int k = 0; k < count; k++)
                        {
                            GroundDecorInstance instance = runtime.Instances[k];
                            Vector3 localPosition = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);
                            Vector3 localUp = new Vector3(instance.Normal.x, instance.Normal.y, instance.Normal.z);
                            if (localUp.sqrMagnitude < 1e-6f)
                            {
                                localUp = Vector3.up;
                            }
                            else
                            {
                                localUp.Normalize();
                            }

                            Vector3 worldPosition = chunkMatrix.MultiplyPoint3x4(localPosition);
                            Vector3 worldUp = chunkMatrix.MultiplyVector(localUp).normalized;

                            Vector3 facing = Vector3.ProjectOnPlane(cameraPosition - worldPosition, worldUp);
                            if (facing.sqrMagnitude < 1e-6f)
                            {
                                facing = Vector3.ProjectOnPlane(Vector3.forward, worldUp);
                                if (facing.sqrMagnitude < 1e-6f)
                                {
                                    facing = Vector3.ProjectOnPlane(Vector3.right, worldUp);
                                }
                            }

                            float size = instance.Scale;
                            Quaternion rotation = Quaternion.LookRotation(facing.normalized, worldUp);
                            // Квад центрирован по высоте: поднимаем, чтобы основание
                            // стояло на земле.
                            worldPosition += worldUp * (0.5f * size);
                            runtime.Matrices[k] = Matrix4x4.TRS(
                                worldPosition, rotation, new Vector3(size, size, size));
                        }

                        DrawDecorBatches(billboard, material, runtime.Matrices, count);
                    }
                }
            }
        }

        private void EnsureDecorScratch(int size)
        {
            if (decorPerMeshScratch == null || decorPerMeshScratch.Length < size)
            {
                decorPerMeshScratch = new Matrix4x4[size];
            }
        }

        private void DrawDecorBatches(Mesh mesh, Material material, Matrix4x4[] matrices, int count)
        {
            const int batchSize = 1023;
            if (!material.enableInstancing)
            {
                // DrawMeshInstanced требует включённого инстансинга у материала.
                material.enableInstancing = true;
            }

            int start = 0;
            while (start < count)
            {
                int n = System.Math.Min(batchSize, count - start);
                Matrix4x4[] batch = matrices;
                if (start > 0)
                {
                    if (decorBatchScratch == null || decorBatchScratch.Length < batchSize)
                    {
                        decorBatchScratch = new Matrix4x4[batchSize];
                    }

                    System.Array.Copy(matrices, start, decorBatchScratch, 0, n);
                    batch = decorBatchScratch;
                }

                // Все сабмеши: у FBX ствол и листва могут быть одним мешем
                // с двумя материалами, иначе дерево рисуется без кроны.
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Graphics.DrawMeshInstanced(
                        mesh, sub, material, batch, n, null, ShadowCastingMode.Off, false, gameObject.layer);
                }

                start += n;
            }
        }

        private void EvictIfNeeded()
        {
            if (chunks.Count <= MaxCachedChunks)
            {
                return;
            }

            toEvict.Clear();
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                if (!keep.Contains(kv.Key))
                {
                    toEvict.Add(kv.Key);
                }
            }

            for (int i = 0; i < toEvict.Count && chunks.Count > MaxCachedChunks; i++)
            {
                long id = toEvict[i];
                Chunk chunk = chunks[id];
                DisposeDecor(chunk);
                if (chunk.Go != null)
                {
                    Destroy(chunk.Go);
                }

                chunks.Remove(id);
            }
        }

        private void SetAllInvisible()
        {
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                if (kv.Value.Visible)
                {
                    kv.Value.Go.SetActive(false);
                    kv.Value.Visible = false;
                }
            }
        }

        private Material GetMaterial()
        {
            if (materialCache == null)
            {
                Shader shader = Shader.Find("Galilego/PlanetSurface");
                materialCache = new Material(shader);
                ApplyTerrainUniforms(materialCache);
            }

            return materialCache;
        }

        /// <summary>
        /// Параметры per-pixel палитры/шума в материал из единственного источника
        /// (HeightfieldTerrain + TerrainPalette). Цвета передаём как float4 через
        /// SetVector: они уже линейные, повторная sRGB-конверсия не нужна.
        /// </summary>
        private void ApplyTerrainUniforms(Material m)
        {
            TerrainPaletteData palette = terrain.Palette ?? new TerrainPaletteData();
            m.SetFloat("_TerrainAmplitude", (float)System.Math.Max(1d, terrain.AmplitudeMeters));
            m.SetFloat("_TerrainSeaLevel", (float)System.Math.Max(terrain.SeaLevelMeters, -1e30d));
            m.SetFloat("_TerrainSeed", terrain.Seed);
            m.SetFloat("_TerrainGain", (float)TerrainNoise.EffectiveGain(terrain.Gain));
            m.SetFloat("_TerrainLacunarity", (float)TerrainNoise.EffectiveLacunarity(terrain.Lacunarity));
            m.SetFloat("_TerrainRockSlopeTan", (float)terrain.ColorRockSlopeTan);
            m.SetFloat("_TerrainRockSlopeWidth", (float)terrain.ColorRockSlopeWidth);
            m.SetFloat("_TerrainRockHeightMin", (float)terrain.ColorRockHeightMin);
            m.SetFloat("_TerrainSnowSlopeTan", (float)terrain.ColorSnowSlopeTan);
            m.SetFloat("_ColorNoiseFrequency", (float)terrain.ColorNoiseFrequency);
            m.SetFloat("_ColorNoiseOctaves", terrain.ColorNoiseOctaves);
            m.SetFloat("_ColorNoiseStrength", (float)terrain.ColorNoiseStrength);
            m.SetFloat("_ColorDetailFrequency", (float)terrain.ColorDetailFrequency);
            m.SetFloat("_ColorDetailOctaves", terrain.ColorDetailOctaves);
            m.SetFloat("_ColorDetailStrength", (float)terrain.ColorDetailStrength);

            m.SetVector("_ColSand", ToVec(palette.SandLinear));
            m.SetVector("_ColDesert", ToVec(palette.DesertLinear));
            m.SetVector("_ColDryGrass", ToVec(palette.DryGrassLinear));
            m.SetVector("_ColGrass", ToVec(palette.GrassLinear));
            m.SetVector("_ColForest", ToVec(palette.ForestLinear));
            m.SetVector("_ColTundra", ToVec(palette.TundraLinear));
            m.SetVector("_ColRock", ToVec(palette.RockLinear));
            m.SetVector("_ColSnow", ToVec(palette.SnowLinear));
            m.SetVector("_ColSea", ToVec(palette.SeaLinear));
            m.SetVector("_ColSoil", ToVec(palette.SoilLinear));
            m.SetVector("_ColLush", ToVec(palette.LushLinear));

            // Текстуры рельефа: трипланарный блендинг по высоте
            // и склону. Все четыре альбедо обязательны, иначе — процедурная палитра.
            Texture2D texLow = terrain.TextureLow;
            Texture2D texMid = terrain.TextureMid;
            Texture2D texHigh = terrain.TextureHigh;
            Texture2D texSteep = terrain.TextureSteep;
            bool useTextures = texLow != null && texMid != null && texHigh != null && texSteep != null;
            m.SetFloat("_TerrainUseTextures", useTextures ? 1f : 0f);
            if (useTextures)
            {
                m.SetTexture("_TexLow", texLow);
                m.SetTexture("_TexMid", texMid);
                m.SetTexture("_TexHigh", texHigh);
                m.SetTexture("_TexSteep", texSteep);
                m.SetTexture("_TexOcclusion", terrain.TextureOcclusion != null ? terrain.TextureOcclusion : Texture2D.whiteTexture);
                m.SetFloat("_TerrainTextureScale", (float)terrain.TextureScale);
                m.SetFloat("_BodyRadius", (float)body.Radius);
                m.SetFloat("_LowMidBlendStart", (float)terrain.LowMidBlendStart);
                m.SetFloat("_LowMidBlendEnd", (float)terrain.LowMidBlendEnd);
                m.SetFloat("_MidHighBlendStart", (float)terrain.MidHighBlendStart);
                m.SetFloat("_MidHighBlendEnd", (float)terrain.MidHighBlendEnd);
                m.SetFloat("_SteepBlendStart", (float)terrain.SteepBlendStart);
                m.SetFloat("_SteepBlendEnd", (float)terrain.SteepBlendEnd);
            }

            colorStateCache = CaptureColorState();
        }

        private static Vector4 ToVec(Color c)
        {
            return new Vector4(c.r, c.g, c.b, c.a);
        }

        private static long NodeId(int face, int depth, int ix, int iy)
        {
            return (long)face
                | ((long)depth << 3)
                | ((long)(ix & 0xFFFFF) << 8)
                | ((long)(iy & 0xFFFFF) << 28);
        }

        private static long NodeId(Node node)
        {
            return NodeId(node.Face, node.Depth, node.Ix, node.Iy);
        }
    }
}
