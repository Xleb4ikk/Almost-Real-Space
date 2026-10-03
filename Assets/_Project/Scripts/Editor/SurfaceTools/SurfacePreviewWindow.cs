using System;
using Galilego.Core;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Окно просмотра поверхности планеты со СВОЕЙ камерой и СВОИМ мешем.
    ///
    /// Почему не Scene view: камера пользователя может быть в 35 км от фрейма
    /// (в их случае size = 17 км), и её позицию нельзя вычислить из кода —
    /// Unity пересчитывает её только на репейнте окна. Превью, живущее в сцене,
    /// от этого зависит целиком и потому то видно, то нет. Здесь и камера, и
    /// меш наши: окно всегда показывает рельеф вокруг фрейма, мышь орбитит и
    /// зумит, как в нормальном 3D-редакторе.
    ///
    /// Меш строится тем же кодом, что и игровой (TerrainNoise + TerrainTileJob),
    /// и того же радиуса, что видит окно, поэтому картинка в окне и в Play
    /// совпадает, а масштаб не зависит от состояния сцены.
    ///
    /// Окно умеет всё, что нужно для постановки задачи: показать рельеф, задать
    /// широту/долготу, взять точку у игрока (в Play) или у точки спавна,
    /// показать высоту и есть ли под ногами вода.
    /// </summary>
    public sealed class SurfacePreviewWindow : EditorWindow
    {
        private const int TextureWidth = 1024;
        private const int TextureHeight = 640;
        private const int Rings = 13;

        private const double MinDistance = 0.4d;
        private const double MaxDistance = 150000d;

        private SurfaceFrame frame;
        private double bodyRadius = 1d;
        private HeightfieldTerrain terrain;

        private GameObject meshGo;
        private MeshFilter filter;
        private MeshRenderer meshRenderer;
        private Material material;
        private Mesh mesh;

        private RenderTexture target;
        private Camera cam;
        private GameObject camGo;

        private double yawDegrees = 35d;
        private double pitchDegrees = 34d;
        private double distanceMeters = 120d;
        private Vector3 panOffset = Vector3.zero;

        private Vector2 lastMouse;
        private bool dragging;
        private int mouseButton;

        private string status = string.Empty;
        private MessageType statusType = MessageType.Info;
        private long lastSignature = long.MinValue;
        private double lastBuiltDistance = -1d;

        private Texture2D cachedFrame;
        private bool frameDirty = true;
        private bool hasRendered;
        private Vector3 lastRenderedCameraPos;
        private double lastRenderedDistance = -1d;
        private double lastRenderedYaw = -1d;
        private double lastRenderedPitch = -1d;
        private float lastRenderedAspect = -1f;


        [MenuItem("Tools/Galilego/Surface authoring/Planet surface window", false, 0)]
        private static void Open()
        {
            var window = GetWindow<SurfacePreviewWindow>("Поверхность");
            window.minSize = new Vector2(600f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Поверхность");
            target = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "SurfacePreviewRT",
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();

            camGo = new GameObject("__surface_window_cam") { hideFlags = HideFlags.HideAndDontSave };
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.11f, 1f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 800000f;
            cam.enabled = false;
            cam.targetTexture = target;

            meshGo = new GameObject("__surface_window_mesh") { hideFlags = HideFlags.HideAndDontSave };
            filter = meshGo.AddComponent<MeshFilter>();
            meshRenderer = meshGo.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            ResolveFrame();
            Rebuild(true);
        }

        private void OnDisable()
        {
            if (cam != null)
            {
                cam.targetTexture = null;
            }

            if (mesh != null)
            {
                DestroyImmediate(mesh);
            }

            if (cachedFrame != null)
            {
                DestroyImmediate(cachedFrame);
                cachedFrame = null;
            }

            if (material != null)
            {
                // Материал превью принадлежит SurfaceGameLook и переиспользуется
                // сценой: убивать его здесь нельзя.
                material = null;
            }


            if (camGo != null)
            {
                DestroyImmediate(camGo);
            }

            if (meshGo != null)
            {
                DestroyImmediate(meshGo);
            }

            if (target != null)
            {
                target.Release();
                DestroyImmediate(target);
            }
        }

        private void ResolveFrame()
        {
            frame = SurfacePreviewTool.ResolveFrame();
            if (frame == null)
            {
                // Фрейма нет — создаём сами. Иначе окно упирается в пустоту:
                // выход из Play откатывает несохранённую сцену, фрейм исчезает,
                // и пользователь оказывается перед окном без карты.
                frame = CreateFrameInternal("Фрейм создан автоматически: точка спавна из раннера. Задай lat/lon или нажми «Взять точку игрока».");
                return;
            }

            if (!frame.IsUsable)
            {
                SetStatus(
                    "Фрейм не разрешил тело: проверь поле Body/BodyName и наличие StarSystemAuthoring в сцене.",
                    MessageType.Error);
                return;
            }

            bodyRadius = frame.BodyRadius;
            terrain = frame.Terrain;
            meshGo.transform.position = frame.transform.position;
        }

        private void SetStatus(string message, MessageType type)
        {
            status = message;
            statusType = type;
        }

        private void Update()
        {
            if (frame == null || !frame.IsUsable)
            {
                ResolveFrame();
            }
            else
            {
                frame.Refresh();
                meshGo.transform.position = frame.transform.position;
            }

            // Жёсткий троттл. Update окна зовётся на КАЖДЫЙ репейнт, а
            // пересборка — это 12 тысяч сэмплов шума плюс интеграл
            // атмосферы для света. Без ограничения окно съедало главный поток
            // редактора на 100% и Unity переставал отвечать (воспроизведено).
            if (EditorApplication.timeSinceStartup - lastRebuildTime < MinRebuildIntervalSeconds)
            {
                return;
            }

            lastRebuildTime = EditorApplication.timeSinceStartup;
            Rebuild(false);
            Repaint();
        }

        private const double MinRebuildIntervalSeconds = 0.2d;
        private static double lastRebuildTime = -1d;


        private long Signature()
        {
            unchecked
            {
                long h = 17L;
                h = (h * 31L) + (long)Math.Round(frame.LatitudeDegrees * 1e6d);
                h = (h * 31L) + (long)Math.Round(frame.LongitudeDegrees * 1e6d);
                h = (h * 31L) + (long)Math.Round(frame.AltitudeMeters * 1e3d);
                h = (h * 31L) + frame.BodyName.GetHashCode();
                if (terrain != null)
                {
                    h = (h * 31L) + terrain.Seed;
                    h = (h * 31L) + (long)Math.Round(terrain.AmplitudeMeters * 1e3d);
                    h = (h * 31L) + (long)Math.Round(terrain.BaseFrequency * 1e3d);
                    h = (h * 31L) + terrain.Octaves;
                    h = (h * 31L) + (long)Math.Round(terrain.SeaLevelMeters * 1e3d);
                    h = (h * 31L) + (long)Math.Round(terrain.Lacunarity * 1e3d);
                    h = (h * 31L) + (long)Math.Round(terrain.Gain * 1e3d);
                    h = (h * 31L) + (long)Math.Round(terrain.RidgedMix * 1e3d);
                    h = (h * 31L) + (long)Math.Round(terrain.ContinentFrequency * 1e3d);
                    h = (h * 31L) + (long)Math.Round(terrain.WarpStrength * 1e3d);
                }

                return h;
            }
        }

        private void Rebuild(bool force)
        {
            if (frame == null || !frame.IsUsable)
            {
                return;
            }

            // Превью рисует упрощённым шейдером (см. SurfacePreviewTool.Rebuild):
            // палитра и текстуры подтягиваются общим кодом, а игровой
            // Galileo/PlanetSurface в редакторе не используется — его глобалы
            // ставит SkyEnvironment, а тот тянет интеграл атмосферы и подвешивает
            // редактор. Форма рельефа от этого не меняется, а для постановки
            // базы нужна именно она.

            // Видимый радиус в окне: половина высоты кадра на расстоянии
            // камеры. Меш строим вчетверо шире — край за кадром, иначе на
            // горизонте видно обрыв диска.
            double visible = VisibleRadius();
            long signature = Signature();
            bool zoomed = lastBuiltDistance > 0d
                && (visible > lastBuiltDistance * 1.3d || visible < lastBuiltDistance / 1.3d);
            if (!force && !zoomed && signature == lastSignature && mesh != null)
            {
                return;
            }

            lastSignature = signature;
            lastBuiltDistance = visible;

            var grid = SurfacePreviewBuilder.GridForVisibleRadius(visible, Rings);
            Mesh built = SurfacePreviewBuilder.Build(
                frame.BodyState, terrain,
                KeplerMath.DegreesToRadians(frame.LatitudeDegrees),
                KeplerMath.DegreesToRadians(frame.LongitudeDegrees),
                frame.AnchorBodyFixed, frame.AltitudeMeters,
                grid, true);
            if (built == null)
            {
                return;
            }

            if (mesh != null)
            {
                DestroyImmediate(mesh);
            }

            mesh = built;
            filter.sharedMesh = mesh;

            material = SurfacePreviewBuilder.CreateFallbackMaterial();
            if (material == null)
            {
                SetStatus("Шейдер Galilego/SurfacePreview не найден — превью будет пустым.", MessageType.Error);
                return;
            }

            PlanetSurfaceRenderer.ApplyTerrainGlobals(terrain, 3f, new Vector4(0.42f, 0.52f, 0.56f, 1f));
            material.SetFloat("_PreviewSeaLevel", terrain != null ? (float)terrain.SeaLevelMeters : -1000000000f);
            SurfacePreviewTool.ApplyGridSettings(material, visible);
            meshRenderer.sharedMaterial = material;
            meshRenderer.enabled = true;
            frameDirty = true;
        }



        private double VisibleRadius()
        {
            double halfHeight = distanceMeters * Math.Tan(cam.fieldOfView * 0.5d * Mathf.Deg2Rad);
            return Math.Max(2d, Math.Min(halfHeight * 2d, 200000d));
        }

        private void OnGUI()
        {
            DrawToolbar();

            Rect view = GUILayoutUtility.GetRect(0f, Mathf.Max(240f, position.height - 186f), GUILayout.ExpandWidth(true));
            DrawView(view);

            GUILayout.Space(6f);
            DrawLocation();
            DrawActions();
            DrawReadout();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Перестроить", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    Rebuild(true);
                }

                if (GUILayout.Button("Сверху", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    pitchDegrees = 89d;
                    yawDegrees = 0d;
                }

                if (GUILayout.Button("Сбоку", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    pitchDegrees = 5d;
                }

                if (GUILayout.Button("Сбросить камеру", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                {
                    yawDegrees = 35d;
                    pitchDegrees = 34d;
                    distanceMeters = 120d;
                    panOffset = Vector3.zero;
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label("ЛКМ — орбита, колесо — зум, СКМ — сдвиг", EditorStyles.miniLabel);
            }
        }

        private void DrawView(Rect view)
        {
            if (frame == null || !frame.IsUsable || mesh == null || material == null)
            {
                GUI.DrawTexture(view, Texture2D.blackTexture, ScaleMode.StretchToFill, false);
                GUI.Label(view, "Превью не построено", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            HandleInput(view);

            // Кадр кэшируется и перерисовывается ТОЛЬКО когда что-то изменилось.
            // Рендер в RenderTexture + ReadPixels на каждом репейнте — это
            // CPU-чтение кадра обратно в managed-память, и на каждом репейнте
            // оно уводило редактор в 100% CPU (воспроизведено: Unity перестала
            // отвечать на 30+ секунд).
            Vector3 camPos = camGo.transform.position;
            float aspect = view.width / Mathf.Max(1f, view.height);
            bool cameraMoved = hasRendered
                && ((lastRenderedCameraPos - camPos).sqrMagnitude > 1e-6f
                    || System.Math.Abs(lastRenderedDistance - distanceMeters) > 1e-6d
                    || System.Math.Abs(lastRenderedYaw - yawDegrees) > 1e-6d
                    || System.Math.Abs(lastRenderedPitch - pitchDegrees) > 1e-6d
                    || System.Math.Abs(lastRenderedAspect - aspect) > 0.01d);

            if (frameDirty || cameraMoved || cachedFrame == null)
            {
                RenderFrame(view);
                frameDirty = false;
                hasRendered = true;
                lastRenderedCameraPos = camPos;
                lastRenderedDistance = distanceMeters;
                lastRenderedYaw = yawDegrees;
                lastRenderedPitch = pitchDegrees;
                lastRenderedAspect = aspect;
            }

            if (cachedFrame != null)
            {
                GUI.DrawTexture(view, cachedFrame, ScaleMode.StretchToFill, false);
            }
        }

        private void RenderFrame(Rect view)
        {
            PlaceCamera(view);
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = target;
            if (cachedFrame == null)
            {
                cachedFrame = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGB24, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            cachedFrame.ReadPixels(new Rect(0, 0, TextureWidth, TextureHeight), 0, 0);
            cachedFrame.Apply();
            RenderTexture.active = prev;
        }


        private void PlaceCamera(Rect view)
        {
            // FOV по вертикали, а окно обычно шире, чем выше: если оставить
            // fieldOfView по горизонтали, земля сплющивается по краям.
            float aspect = Mathf.Max(0.2f, view.width / Mathf.Max(1f, view.height));
            if (Mathf.Abs(cam.aspect - aspect) > 0.001f)
            {
                cam.aspect = aspect;
            }

            float pitch = Mathf.Clamp((float)pitchDegrees, 1f, 89f);
            Vector3 dir = Quaternion.Euler(pitch, (float)yawDegrees, 0f) * Vector3.back;
            Vector3 pivot = meshGo.transform.position + panOffset;
            camGo.transform.position = pivot + (dir * (float)distanceMeters);
            camGo.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
        }

        private void HandleInput(Rect view)
        {
            Event e = Event.current;
            if (!view.Contains(e.mousePosition))
            {
                dragging = false;
                return;
            }

            if (e.type == EventType.MouseDown)
            {
                dragging = true;
                mouseButton = e.button;
                lastMouse = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && dragging)
            {
                Vector2 delta = e.mousePosition - lastMouse;
                lastMouse = e.mousePosition;
                if (mouseButton == 0)
                {
                    yawDegrees += delta.x * 0.4d;
                    pitchDegrees = Mathf.Clamp((float)(pitchDegrees - delta.y * 0.3d), 1f, 89f);
                }
                else if (mouseButton == 2)
                {
                    Vector3 right = camGo.transform.right;
                    Vector3 up = camGo.transform.up;
                    float panScale = (float)(distanceMeters * 0.0012d);
                    panOffset -= (right * (delta.x * panScale)) + (up * (delta.y * panScale));
                    ClampPan();
                }

                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                dragging = false;
            }
            else if (e.type == EventType.ScrollWheel)
            {
                float factor = Mathf.Clamp(1f - e.delta.y * 0.12f, 0.5f, 2f);
                distanceMeters = Mathf.Clamp((float)(distanceMeters * factor), (float)MinDistance, (float)MaxDistance);
                ClampPan();
                e.Use();
            }
        }

        /// <summary>
        /// Сдвиг не должен увести камеру за край диска: меш круглый, за краем
        /// пустота, и «вернуть» камеру в землю больше нечем.
        /// </summary>
        private void ClampPan()
        {
            float limit = (float)(distanceMeters * 1.2d);
            panOffset = new Vector3(
                Mathf.Clamp(panOffset.x, -limit, limit),
                0f,
                Mathf.Clamp(panOffset.z, -limit, limit));
        }

        private void DrawLocation()
        {
            if (frame == null)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                double lat = EditorGUILayout.DoubleField("Широта", frame.LatitudeDegrees, GUILayout.Width(240f));
                double lon = EditorGUILayout.DoubleField("Долгота", NormalizeLongitude(frame.LongitudeDegrees), GUILayout.Width(240f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(frame, "Surface frame: lat/lon");
                    frame.LatitudeDegrees = Mathf.Clamp((float)lat, -90f, 90f);
                    frame.LongitudeDegrees = lon;
                    frame.SnapToTerrain = true;
                    EditorUtility.SetDirty(frame);
                    frame.Refresh();
                    bodyRadius = frame.BodyRadius;
                    terrain = frame.Terrain;
                    Rebuild(true);
                }
            }
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Application.isPlaying ? "Взять точку у игрока" : "Взять точку игрока (Play)", GUILayout.Height(26f)))
                {
                    ApplyPlayerPosition();
                }

                if (GUILayout.Button("Взять точку спавна", GUILayout.Height(26f)))
                {
                    ApplySpawnPosition();
                }

                if (GUILayout.Button("Создать фрейм", GUILayout.Height(26f)))
                {
                    CreateFrame();
                }

                using (new EditorGUI.DisabledScope(frame == null))
                {
                    if (GUILayout.Button("Показать в Scene view", GUILayout.Height(26f)))
                    {
                        SurfacePreviewTool.FocusSceneView(frame);
                    }
                }
            }
        }

        private void CreateFrame()
        {
            CreateFrameInternal(null);
        }

        private SurfaceFrame CreateFrameInternal(string message)
        {
            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            var go = new GameObject("SurfaceFrame");
            Undo.RegisterCreatedObjectUndo(go, "Create surface frame");
            var created = Undo.AddComponent<SurfaceFrame>(go);
            created.Runner = runner;
            created.SnapToTerrain = true;
            if (runner != null)
            {
                created.BodyName = string.IsNullOrEmpty(runner.SpawnBodyName) ? "Terra" : runner.SpawnBodyName;
                created.LatitudeDegrees = runner.SpawnLatitudeDegrees;
                created.LongitudeDegrees = runner.SpawnLongitudeDegrees;
            }

            created.Refresh();
            frame = created;
            bodyRadius = created.BodyRadius;
            terrain = created.Terrain;
            meshGo.transform.position = created.transform.position;
            Selection.activeGameObject = go;
            Rebuild(true);
            SetStatus(
                message ?? "Фрейм создан в точке спавна. Задай lat/lon или нажми «Взять точку игрока».",
                MessageType.Info);
            return created;
        }

        private void ApplyPlayerPosition()
        {
            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null || runner.SystemState == null || runner.DominantBody == null)
            {
                SetStatus("Нет SimulationRunner в сцене.", MessageType.Error);
                return;
            }

            if (!Application.isPlaying)
            {
                // Точка из последнего прохода в Play. Правка на то, что игрок
                // стоял под водой/вне зоны фрейма, делается вручную в полях
                // широты и долготы выше — с ними кнопка не спорит.
                if (!SurfacePreviewTool.HasRememberedPlayer)
                {
                    SetStatus("Запомненной точки нет: нажми Play, дойди до нужного места, потом Stop и вернись сюда.", MessageType.Warning);
                    return;
                }

                SetFrameTo(SurfacePreviewTool.RememberedBody, SurfacePreviewTool.RememberedLatitude, SurfacePreviewTool.RememberedLongitude);
                return;
            }

            var body = runner.DominantBody;
            body.SurfaceLatLonAt(runner.PlayerPosition, runner.TimeSeconds, out double lat, out double lon);
            SetFrameTo(body.Name, lat, lon);
        }

        private void ApplySpawnPosition()
        {
            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null)
            {
                SetStatus("Нет SimulationRunner в сцене.", MessageType.Error);
                return;
            }

            string bodyName = string.IsNullOrEmpty(runner.SpawnBodyName) ? frame.BodyName : runner.SpawnBodyName;
            SetFrameTo(bodyName, runner.SpawnLatitudeDegrees, runner.SpawnLongitudeDegrees);
        }

        private void SetFrameTo(string bodyName, double lat, double lon)
        {
            Undo.RecordObject(frame, "Surface frame: новая точка");
            frame.Body = null;
            frame.BodyName = bodyName;
            frame.LatitudeDegrees = Mathf.Clamp((float)lat, -90f, 90f);
            frame.LongitudeDegrees = lon;
            frame.SnapToTerrain = true;
            frame.HeightOffsetMeters = 0d;
            EditorUtility.SetDirty(frame);
            frame.Refresh();

            bodyRadius = frame.BodyRadius;
            terrain = frame.Terrain;
            panOffset = Vector3.zero;
            distanceMeters = 120d;
            Rebuild(true);

            SetStatus(
                string.Format("{0}: lat {1:F3}°, lon {2:F3}°, рельеф {3:F1} м — {4}",
                    bodyName,
                    frame.LatitudeDegrees,
                    NormalizeLongitude(frame.LongitudeDegrees),
                    frame.GroundHeightMeters,
                    IsWater(frame.BodyState, frame.LatitudeDegrees, frame.LongitudeDegrees) ? "вода" : "суша"),
                MessageType.Info);
        }

        private static bool IsWater(OrbitingBody body, double latDeg, double lonDeg)
        {
            return body != null && WaterQuery.IsWaterAt(body, latDeg, lonDeg);
        }

        private void DrawReadout()
        {
            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.HelpBox(status, statusType);
            }

            if (frame == null || !frame.IsUsable)
            {
                return;
            }

            double ground = frame.GroundHeightMeters;
            double sea = frame.BodyState.Terrain != null ? frame.BodyState.Terrain.GetSeaLevelMeters() : double.NegativeInfinity;
            EditorGUILayout.LabelField(
                "Тело", frame.BodyState.Name + ", R = " + frame.BodyState.Radius.ToString("F0") + " м");
            EditorGUILayout.LabelField(
                "Высота рельефа",
                ground.ToString("F2") + " м" + (sea > -1e29d ? "   (море " + sea.ToString("F0") + " м)" : string.Empty));
            EditorGUILayout.LabelField(
                "Под точкой", IsWater(frame.BodyState, frame.LatitudeDegrees, frame.LongitudeDegrees) ? "вода" : "суша");
            if (mesh != null)
            {
                EditorGUILayout.LabelField(
                    "Меш", mesh.vertexCount + " вершин, " + (mesh.triangles.Length / 3) + " треугольников, радиус "
                    + mesh.bounds.extents.x.ToString("F0") + " м   |   камера " + distanceMeters.ToString("F0") + " м");
            }
        }

        /// <summary>Долгота в [-180, 180]: lat/lon из SurfaceLatLonAt приходят в [0, 360).</summary>
        private static double NormalizeLongitude(double lon)
        {
            double d = lon % 360d;
            if (d > 180d)
            {
                d -= 360d;
            }
            else if (d < -180d)
            {
                d += 360d;
            }

            return d;
        }
    }
}
