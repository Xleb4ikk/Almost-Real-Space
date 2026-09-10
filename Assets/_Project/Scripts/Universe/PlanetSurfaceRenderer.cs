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
        public int TileResolution = 65;

        [Tooltip("Максимальная глубина quadtree (0 = 6 граней целиком).")]
        public int MaxDepth = 10;

        [Tooltip("Сплит: узел делится, если дистанция до центра < размера узла × фактор.")]
        public float SplitFactor = 4f;

        [Tooltip("Гистерезис слияния: уже поделённый узел сливается обратно только при дистанции > размера × SplitFactor × это. Между порогами держится прошлый уровень (против LOD-флапа на скорости).")]
        public float MergeHysteresis = 1.3f;

        [Tooltip("Глубина юбки как доля размера узла.")]
        public float SkirtFactor = 0.03f;

        [Tooltip("Сколько чанков строить за кадр (первый кадр — без лимита).")]
        public int BuildsPerFrame = 12;

        [Tooltip("Ограничитель активных узлов (защита от лавины).")]
        public int MaxNodes = 4000;

        [Tooltip("Высота над рельефом, выше которой планета не рендерится (м).")]
        public double MaxAltitudeMeters = 5e6d;

        [Tooltip("Максимум закэшированных мешей (лишние вытесняются).")]
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

            // Скрываем примитив-сферу: её роль теперь играет cube-sphere.
            MeshRenderer baseSphere = GetComponent<MeshRenderer>();
            if (baseSphere != null)
            {
                baseSphere.enabled = false;
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
            double altitude = (Runner.Ship.Position - bodyPosition).Magnitude - body.Radius;
            if (altitude > MaxAltitudeMeters)
            {
                SetAllInvisible();
                return;
            }

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

            TerrainTileJob tileJob = new TerrainTileJob
            {
                Params = noiseParams,
                Directions = dirs,
                Heights = jobHeights,
                ColorMasks = jobMasks
            };
            tileJob.Schedule(grid * grid, 64, new JobHandle()).Complete();

            var positions = new Vector3[grid * grid];
            var heights = new double[grid * grid];
            var masks = new float[grid * grid];
            for (int gi = 0; gi < grid; gi++)
            {
                for (int gj = 0; gj < grid; gj++)
                {
                    int vi = (gi * grid) + gj;
                    double3 d = dirs[vi];
                    double height = jobHeights[vi] * amplitude;
                    if (height < seaLevel)
                    {
                        height = seaLevel;
                    }

                    heights[vi] = height;
                    masks[vi] = jobMasks[vi];
                    Vector3d absAstro = new Vector3d(d.x, d.y, d.z) * (body.Radius + height);
                    positions[vi] = AstroFrame.ToSimulation(absAstro - centerAstro);
                }
            }

            dirs.Dispose();
            jobHeights.Dispose();
            jobMasks.Dispose();

            var vertices = new Vector3[totalVerts];
            var normals = new Vector3[totalVerts];
            var colors = new Color[totalVerts];

            bool maskOn = terrain.ColorNoiseFrequency > 0d && terrain.ColorNoiseStrength != 0d;

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

                    double cosA = Vector3.Dot(normals[index], radial);
                    double slopeTan = TerrainPalette.SlopeTan(cosA);
                    double mask = maskOn ? masks[halo] : 0d;

                    Color color = TerrainPalette.HeightColorEx(
                        heights[halo], seaLevel, amplitude, slopeTan, mask,
                        terrain.ColorRockSlopeTan, terrain.ColorRockSlopeWidth,
                        terrain.ColorSnowSlopeTan, terrain.ColorNoiseStrength);

                    bool isWater = heights[halo] <= seaLevel + seaEps;
                    colors[index] = new Color(color.r, color.g, color.b, isWater ? 1f : 0f);
                }
            }

            if (diagnosticChunksLogged < 8)
            {
                diagnosticChunksLogged++;
                int landCore = 0;
                double minH = double.MaxValue;
                double maxH = double.MinValue;
                for (int k = 0; k < coreCount; k++)
                {
                    if (colors[k].a < 0.5f)
                    {
                        landCore++;
                    }

                    minH = System.Math.Min(minH, heights[((k / n) + 1) * grid + ((k % n) + 1)]);
                    maxH = System.Math.Max(maxH, heights[((k / n) + 1) * grid + ((k % n) + 1)]);
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
                    colors[baseIndex + k] = colors[coreIndex];
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
            chunk.Mesh.colors = colors;
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

        private Material GetMaterial()
        {
            if (materialCache == null)
            {
                Shader shader = Shader.Find("Galilego/PlanetSurface");
                materialCache = new Material(shader);
            }

            return materialCache;
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
