using System.Collections.Generic;
using Galilego.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

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

        [Tooltip("Высота над рельефом, выше которой чанки рельефа не рендерятся (м). На этой дистанции вместо них включается базовая сфера (см. BaseSphereColor).")]
        public double MaxAltitudeMeters = 5e6d;

        [Tooltip("Диффузный цвет базовой сферы планеты на большой дистанции. Нужна, чтобы тело было непрозрачным и честно закрывало звёзды/солнце по глубине.")]
        public Color BaseSphereColor = new Color(0.16f, 0.24f, 0.32f, 1f);

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
            public Vector3d CenterAstro;
        }

        private OrbitingBody body;
        private HeightfieldTerrain terrain;
        private TerrainNoiseParams noiseParams;
        private Transform surfaceRoot;
        private Material materialCache;
        private MeshRenderer baseSphereRenderer;
        private Material baseSphereMaterial;

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
            // Базовая сфера — не списываем навсегда: на малой высоте её роль
            // играет cube-sphere (сфера гасится), а на большой дистанции, где
            // чанки отключаются, сфера включается как непрозрачное тело, которое
            // пишет depth и закрывает звёзды/солнце. Иначе на удалении от тела
            // геометрии нет вообще и небо просвечивает планету.
            baseSphereRenderer = GetComponent<MeshRenderer>();
            if (baseSphereRenderer != null && baseSphereRenderer.sharedMaterial != null)
            {
                baseSphereMaterial = new Material(baseSphereRenderer.sharedMaterial);
                baseSphereMaterial.color = BaseSphereColor;
                baseSphereMaterial.SetColor("_BaseColor", BaseSphereColor);
                baseSphereMaterial.SetColor("_UnlitColor", BaseSphereColor);
                baseSphereRenderer.sharedMaterial = baseSphereMaterial;
                baseSphereRenderer.enabled = false;
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
        }

        private void OnDestroy()
        {
            if (surfaceRoot != null)
            {
                Destroy(surfaceRoot.gameObject);
            }

            if (materialCache != null)
            {
                Destroy(materialCache);
            }

            if (baseSphereMaterial != null)
            {
                Destroy(baseSphereMaterial);
            }
        }

        /// <summary>Сбросить кэш мешей (после live-смены параметров рельефа).</summary>
        [ContextMenu("Clear chunk cache")]
        public void ClearChunkCache()
        {
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                if (kv.Value.Go != null)
                {
                    Destroy(kv.Value.Go);
                }
            }

            chunks.Clear();
            splitNodes.Clear();
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

        /// <summary>Пресет «как Земля» (низкий рельеф, континенты, хребты) + перестройка.</summary>
        [ContextMenu("Earth-like preset + rebuild")]
        public void ApplyEarthLikePresetAndRebuild()
        {
            BodyAuthoring authoring = GetComponent<BodyAuthoring>();
            if (authoring == null)
            {
                Debug.LogWarning("[PlanetSurfaceRenderer] нет BodyAuthoring.");
                return;
            }

            authoring.ApplyEarthLikeTerrainPreset();
            ApplyAuthoringAndRebuild();
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

            BodyAuthoring authoring = GetComponent<BodyAuthoring>();
            if (authoring == null)
            {
                return;
            }

            authoring.ApplyToTerrain(terrain);
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
                DetailStrength = terrain.ColorDetailStrength
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
                && a.DetailStrength == b.DetailStrength;
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
                SetBaseSphereVisible(false);
                return;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPosition, out _);
            double altitude = (Runner.Ship.Position - bodyPosition).Magnitude - body.Radius;
            if (altitude > MaxAltitudeMeters)
            {
                // Чанки рельефа на такой дистанции отключаются — включаем
                // базовую сферу, иначе у тела нет никакой геометрии и звёзды
                // рисуются «сквозь планету».
                SetAllInvisible();
                SetBaseSphereVisible(true);
                return;
            }

            // Малая высота: рельеф играет роль поверхности, сферу гасим.
            SetBaseSphereVisible(false);

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

            bool split;
            if (depth < MaxDepth && desired.Count + 4 < MaxNodes)
            {
                // Гистерезис: делим на splitDistance, а сливаем только когда ушли
                // заметно дальше (×MergeHysteresis). Между порогами держим прошлый
                // уровень — иначе узел «дробится/сливается» каждый кадр (LOD-флап,
                // сильнее заметный на скорости).
                double splitDistance = nodeSize * SplitFactor;
                split = splitNodes.Contains(NodeId(face, depth, ix, iy))
                    ? distance < splitDistance * MergeHysteresis
                    : distance < splitDistance;
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

            chunk.Visible = true;
            chunks[id] = chunk;
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

        private void SetBaseSphereVisible(bool visible)
        {
            if (baseSphereRenderer != null && baseSphereRenderer.enabled != visible)
            {
                baseSphereRenderer.enabled = visible;
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

            m.SetVector("_ColSand", ToVec(TerrainPalette.Sand));
            m.SetVector("_ColDesert", ToVec(TerrainPalette.Desert));
            m.SetVector("_ColDryGrass", ToVec(TerrainPalette.DryGrass));
            m.SetVector("_ColGrass", ToVec(TerrainPalette.Grass));
            m.SetVector("_ColForest", ToVec(TerrainPalette.Forest));
            m.SetVector("_ColTundra", ToVec(TerrainPalette.Tundra));
            m.SetVector("_ColRock", ToVec(TerrainPalette.Rock));
            m.SetVector("_ColSnow", ToVec(TerrainPalette.Snow));
            m.SetVector("_ColSea", ToVec(TerrainPalette.Sea));
            m.SetVector("_ColSoil", ToVec(TerrainPalette.Soil));
            m.SetVector("_ColLush", ToVec(TerrainPalette.Lush));

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
