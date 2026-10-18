using System;
using System.Collections.Generic;
using Galilego.Core;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// List of SITES on the planet: create, open, edit, teleport to.
    /// A site is a PREFAB whose root carries SurfaceSite; only one is instanced
    /// in the scene at a time (at the origin, so all its objects keep normal
    /// coordinate precision in Scene view). Switching a site swaps the instance.
    /// </summary>
    public sealed class SurfaceSitesWindow : EditorWindow
    {
        private const string SitesFolder = "Assets/_Project/Sites";
        private const string PrefabSuffix = "Site.prefab";

        private Vector2 scroll;
        private string filter = string.Empty;
        private string status = string.Empty;
        private MessageType statusType = MessageType.Info;

        private GameObject activeInstance;
        private SurfaceSite activeSite;

        /// <summary>
        /// Whether to create a flat pad under the site: it edits the terrain
        /// ITSELF, so rendering and physics both pick it up.
        /// </summary>
        private bool createPad = true;

        [MenuItem("Tools/Galilego/Surface authoring/Sites", false, 5)]
        private static void Open()
        {
            var window = GetWindow<SurfaceSitesWindow>("Места");
            window.minSize = new Vector2(420f, 380f);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureFolder();
            RefreshActive();
        }

        private void OnDisable()
        {
            // The site instance is a working copy and must not be left dirty
            // in the scene.
            if (activeInstance != null && !Application.isPlaying)
            {
                DestroyImmediate(activeInstance);
            }

            activeInstance = null;
            activeSite = null;
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(SitesFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Sites");
                // A freshly created folder is visible to AssetDatabase only
                // after Refresh.
                AssetDatabase.Refresh();
            }
        }

        private void RefreshActive()
        {
            if (activeInstance != null)
            {
                activeSite = activeInstance.GetComponent<SurfaceSite>();
                return;
            }

            activeSite = UnityEngine.Object.FindAnyObjectByType<SurfaceSite>();
        }

        private void OnGUI()
        {
            DrawTabs();
            if (tab == 0)
            {
                DrawHeader();
                DrawActiveSite();
                DrawActions();
                DrawList();
            }
            else
            {
                DrawPads();
            }

            DrawStatus();
        }

        private int tab;

        private void DrawTabs()
        {
            tab = GUILayout.Toolbar(tab, new[] { "Места", "Площадки рельефа" });
        }

        /// <summary>
        /// Pads live on the body (BodyAuthoring.TerrainPads), not in the site
        /// prefab: the terrain edit applies regardless of an open base prefab.
        /// </summary>
        private void DrawPads()
        {
            EditorGUILayout.LabelField("Площадки рельефа (аналог PQS-модов KSP)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Площадка выравнивает рельеф в точке: в ядре ровно, дальше гладкий фартук. " +
                "Правка идёт в единственную функцию формы рельефа, поэтому сразу меняет и картинку, " +
                "и физику — расхождение невозможно.\n\n" +
                "Высота площадки запоминается из ЕСТЕСТВЕННОГО рельефа в её точке. Не выбрасывай " +
                "её на середину холма: площадка срежет склон и оставит яму вокруг.",
                MessageType.Info);

            var mods = ActiveMods();
            if (mods == null)
            {
                EditorGUILayout.HelpBox(
                    "На Terra нет компонента TerrainMods. Добавь его на GameObject тела " +
                    "(там же, где BodyAuthoring) — это включает площадки.",
                    MessageType.Warning);
                if (GUILayout.Button("Добавить TerrainMods на Terra", GUILayout.Height(22f)))
                {
                    AddModsToActiveBody();
                    mods = ActiveMods();
                }
            }

            if (mods == null)
            {
                return;
            }

            var body = mods.GetComponent<BodyAuthoring>();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                mods.NewPadInnerRadiusMeters = EditorGUILayout.DoubleField(
                    new GUIContent("Новое: ядро, м", "Внутри этого радиуса высота ровно целевой."),
                    mods.NewPadInnerRadiusMeters, GUILayout.Width(150f));
                mods.NewPadOuterRadiusMeters = EditorGUILayout.DoubleField(
                    new GUIContent("Новое: фартук, м", "На этом радиусе влияние сходит на нет."),
                    mods.NewPadOuterRadiusMeters, GUILayout.Width(150f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(mods, "Surface pad radii");
                    EditorUtility.SetDirty(mods);
                }

                using (new EditorGUI.DisabledScope(!Application.isPlaying && activeSite == null))
                {
                    if (GUILayout.Button("Площадка в точке места", GUILayout.Width(160f), GUILayout.Height(22f)))
                    {
                        if (AddPadAtActiveSite(mods))
                        {
                            status = "Площадка создана по месту.";
                            statusType = MessageType.Info;
                        }
                    }
                }
            }

            EditorGUILayout.Space(4f);
            List<TerrainModifier> pads = body != null ? body.TerrainPads : null;
            if (pads == null || pads.Count == 0)
            {
                EditorGUILayout.LabelField("Площадок нет.", EditorStyles.miniLabel);
                return;
            }

            padsScroll = EditorGUILayout.BeginScrollView(padsScroll, GUILayout.MinHeight(140f));
            for (int i = 0; i < pads.Count; i++)
            {
                DrawPadRow(pads, i);
            }

            EditorGUILayout.EndScrollView();
        }

        private Vector2 padsScroll;

        private void DrawPadRow(List<TerrainModifier> pads, int index)
        {
            TerrainModifier pad = pads[index];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    double lat = EditorGUILayout.DoubleField("Широта", pad.LatitudeDegrees, GUILayout.Width(150f));
                    double lon = EditorGUILayout.DoubleField("Долгота", pad.LongitudeDegrees, GUILayout.Width(150f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(ActiveMods().GetComponent<BodyAuthoring>(), "Surface pad move");
                        pad.LatitudeDegrees = lat;
                        pad.LongitudeDegrees = lon;
                        pads[index] = pad;
                        CommitPadChange();
                    }

                    if (GUILayout.Button("Удалить", GUILayout.Width(70f)))
                    {
                        var mods = ActiveMods();
                        if (EditorUtility.DisplayDialog("Удалить площадку", "Убрать площадку с планеты?", "Удалить", "Отмена"))
                        {
                            Undo.RecordObject(mods.GetComponent<BodyAuthoring>(), "Surface pad remove");
                            pads.RemoveAt(index);
                            CommitPadChange();
                        }

                        return;
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    double inner = EditorGUILayout.DoubleField("Ядро, м", pad.InnerRadiusMeters, GUILayout.Width(150f));
                    double outer = EditorGUILayout.DoubleField("Фартук, м", pad.OuterRadiusMeters, GUILayout.Width(150f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(ActiveMods().GetComponent<BodyAuthoring>(), "Surface pad radius");
                        pad.InnerRadiusMeters = inner;
                        pad.OuterRadiusMeters = Math.Max(outer, inner);
                        pads[index] = pad;
                        CommitPadChange();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    double target = EditorGUILayout.DoubleField(
                        new GUIContent("Высота, м", "Запоминается из естественного рельефа в точке."),
                        pad.TargetHeightMeters, GUILayout.Width(150f));
                    bool sea = EditorGUILayout.Toggle(
                        new GUIContent("Ниже моря", "Перебить кламп уровня моря: нужно для раскопок."),
                        pad.OverridesSeaLevel, GUILayout.Width(110f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(ActiveMods().GetComponent<BodyAuthoring>(), "Surface pad height");
                        pad.TargetHeightMeters = target;
                        pad.OverridesSeaLevel = sea;
                        pads[index] = pad;
                        CommitPadChange();
                    }

                    if (GUILayout.Button("Перезапомнить высоту", GUILayout.Width(150f)))
                    {
                        Undo.RecordObject(ActiveMods().GetComponent<BodyAuthoring>(), "Surface pad recapture");
                        ActiveMods().CaptureTargetHeight(index, pad.LatitudeDegrees, pad.LongitudeDegrees);
                        CommitPadChange();
                    }
                }

                // Warn if the pad has drifted from the terrain (e.g. after a seed change).
                double drift = PadHeightDrift(index);
                if (Math.Abs(drift) > 1d)
                {
                    EditorGUILayout.HelpBox(
                        string.Format("Высота площадки разошлась с естественным рельефом на {0:F1} м — нажми «Перезапомнить высоту».", drift),
                        MessageType.Warning);
                }
            }
        }

        private double PadHeightDrift(int index)
        {
            var mods = ActiveMods();
            var body = mods != null ? mods.GetComponent<BodyAuthoring>() : null;
            if (body == null || body.TerrainPads == null || index >= body.TerrainPads.Count)
            {
                return 0d;
            }

            if (!SurfaceSceneSystem.TryResolve(body.gameObject.name, out OrbitingBody live, out _))
            {
                return 0d;
            }

            var terrain = live.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                return 0d;
            }

            TerrainModifier pad = body.TerrainPads[index];
            double natural = terrain.GetRawHeightMeters(
                live, KeplerMath.DegreesToRadians(pad.LatitudeDegrees), KeplerMath.DegreesToRadians(pad.LongitudeDegrees));
            return pad.TargetHeightMeters - natural;
        }

        private static void CommitPadChange()
        {
            SurfaceSceneSystem.Invalidate();
            var body = ActiveBodyForScene();
            if (body != null)
            {
                EditorUtility.SetDirty(body);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(body.gameObject.scene);
            }
        }

        private static BodyAuthoring ActiveBodyForScene()
        {
            List<BodyAuthoring> all = SurfaceSceneSystem.AllAuthorings();
            return all.Count > 0 ? all[0] : null;
        }
        private TerrainMods ActiveMods()
        {
            BodyAuthoring body = ActiveBody();
            return body != null ? body.GetComponent<TerrainMods>() : null;
        }

        private BodyAuthoring ActiveBody()
        {
            if (activeSite != null)
            {
                foreach (BodyAuthoring a in SurfaceSceneSystem.AllAuthorings())
                {
                    if (a.gameObject.name == activeSite.BodyName)
                    {
                        return a;
                    }
                }
            }

            return ActiveBodyForScene();
        }

        private bool AddPadAtActiveSite(TerrainMods mods)
        {
            if (activeSite == null)
            {
                status = "Сначала открой или создай место — площадка ставится в его точке.";
                statusType = MessageType.Warning;
                return false;
            }

            int index = mods.AddPad(activeSite.LatitudeDegrees, activeSite.LongitudeDegrees);
            if (index < 0)
            {
                status = "Не удалось прочитать высоту рельефа в точке места.";
                statusType = MessageType.Error;
                return false;
            }

            Undo.RecordObject(mods.GetComponent<BodyAuthoring>(), "Surface pad add");
            CommitPadChange();
            return true;
        }

        private void AddModsToActiveBody()
        {
            BodyAuthoring body = ActiveBodyForScene();
            if (body == null)
            {
                status = "В сцене нет тел с BodyAuthoring.";
                statusType = MessageType.Error;
                return;
            }

            Undo.AddComponent<TerrainMods>(body.gameObject);
            status = "TerrainMods добавлен на " + body.gameObject.name + ".";
            statusType = MessageType.Info;
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Места на планете", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Место = префаб с компонентом SurfaceSite. Его lat/lon говорит, ГДЕ оно, " +
                "а дети — это база: обычные объекты и префабы, собранные в Scene view как обычно.\n\n" +
                "Одновременно редактируется одно место: оно инстанцируется у нуля сцены, " +
                "поэтому координаты метровые и точность нормальная. На планете с полуосью " +
                "2.6e10 м float32 даёт шаг около 2 км — мировые координаты для авторства " +
                "не годятся, поэтому и нужен ноль.",
                MessageType.Info);

            filter = EditorGUILayout.TextField("Поиск", filter);
        }

        private void DrawActiveSite()
        {
            if (activeSite == null)
            {
                EditorGUILayout.HelpBox("Место не выбрано. Создай новое или открой существующее из списка.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Правка: " + activeSite.SiteName, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.TextField("Имя", activeSite.SiteName);
                string purpose = EditorGUILayout.TextField("Зачем", activeSite.Purpose);
                double lat = EditorGUILayout.DoubleField("Широта", activeSite.LatitudeDegrees);
                double lon = EditorGUILayout.DoubleField("Долгота", activeSite.LongitudeDegrees);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(activeSite, "Surface site: правка");
                    activeSite.SiteName = name;
                    activeSite.Purpose = purpose;
                    activeSite.LatitudeDegrees = Mathf.Clamp((float)lat, -90f, 90f);
                    activeSite.LongitudeDegrees = lon;
                    EditorUtility.SetDirty(activeSite);
                    activeSite.Refresh();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                bool snap = EditorGUILayout.Toggle("По рельефу", activeSite.SnapToTerrain, GUILayout.Width(120f));
                double offset = EditorGUILayout.DoubleField("Смещение, м", activeSite.HeightOffsetMeters, GUILayout.Width(160f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(activeSite, "Surface site: высота");
                    activeSite.SnapToTerrain = snap;
                    activeSite.HeightOffsetMeters = offset;
                    EditorUtility.SetDirty(activeSite);
                    activeSite.Refresh();
                }
            }

            string ground = string.Format(
                "Рельеф {0:F2} м, под точкой {1}.{2}",
                activeSite.GroundHeightMeters,
                activeSite.IsWater ? "вода" : "суша",
                activeSite.BodyState != null ? "  (" + activeSite.BodyState.Name + ", R " + activeSite.BodyState.Radius.ToString("F0") + " м)" : "  (тело не найдено)");
            EditorGUILayout.LabelField(ground, EditorStyles.miniLabel);
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                createPad = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Ровная площадка под местом",
                        "Правка самого рельефа: в ядре ровно, дальше гладкий фартук. " +
                        "Без неё база на склоне получается непроходимой."),
                    createPad);

                if (GUILayout.Button("Новое место здесь (в нуле сцены)", GUILayout.Height(24f)))
                {
                    CreateSite("Место " + (CountSites() + 1));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(activeSite == null))
                    {
                        if (GUILayout.Button("Сохранить в префаб", GUILayout.Height(24f)))
                        {
                            SaveActiveToPrefab();
                        }

                        using (new EditorGUI.DisabledScope(!Application.isPlaying))
                        {
                            if (GUILayout.Button("Телепорт игрока сюда", GUILayout.Height(24f)))
                            {
                                TeleportPlayer(activeSite);
                            }
                        }
                    }

                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    {
                        if (GUILayout.Button("Новое место у игрока", GUILayout.Height(24f)))
                        {
                            CreateSiteAtPlayer();
                        }
                    }
                }
            }
        }

        private void DrawList()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Сохранённые места", EditorStyles.boldLabel);

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SitesFolder });
            Array.Sort(guids, (a, b) => string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(120f));
            int shown = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                var site = prefab.GetComponent<SurfaceSite>();
                if (site == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filter)
                    && site.SiteName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                shown++;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        site.SiteName + "\n" + site.LatitudeDegrees.ToString("F3") + "°, "
                        + site.LongitudeDegrees.ToString("F3") + "°  " + CountChildren(prefab) + " об.",
                        GUILayout.Height(34f));

                    if (GUILayout.Button("Открыть", GUILayout.Width(70f), GUILayout.Height(24f)))
                    {
                        OpenSite(prefab);
                    }

                    if (Application.isPlaying && GUILayout.Button("TP", GUILayout.Width(34f), GUILayout.Height(24f)))
                    {
                        TeleportPlayer(site);
                    }

                    if (GUILayout.Button("×", GUILayout.Width(28f), GUILayout.Height(24f)))
                    {
                        if (EditorUtility.DisplayDialog(
                                "Удалить место",
                                "Удалить префаб места \"" + site.SiteName + "\"? Содержимое пропадёт.",
                                "Удалить", "Отмена"))
                        {
                            AssetDatabase.DeleteAsset(path);
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            if (shown == 0)
            {
                EditorGUILayout.LabelField("Пока пусто.", EditorStyles.miniLabel);
            }
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.HelpBox(status, statusType);
            }
        }

        private static int CountChildren(GameObject prefab)
        {
            return prefab.GetComponentsInChildren<Transform>(true).Length - 1;
        }

        private int CountSites()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { SitesFolder }).Length;
        }

        private void CreateSite(string siteName)
        {
            var go = new GameObject(siteName);
            var site = go.AddComponent<SurfaceSite>();
            site.SiteName = siteName;
            site.BodyName = ResolveBodyName();

            // In the editor there is no player "here", so use the coordinates of
            // the active authoring frame instead of 0°, 0° (mid-Atlantic).
            var frame = UnityEngine.Object.FindAnyObjectByType<SurfaceFrame>();
            if (frame != null && frame.IsUsable)
            {
                site.LatitudeDegrees = frame.LatitudeDegrees;
                site.LongitudeDegrees = frame.LongitudeDegrees;
            }

            site.SnapToTerrain = true;
            site.Refresh();

            string padNote = createPad ? EnsurePad(site.BodyName, site.LatitudeDegrees, site.LongitudeDegrees) : null;

            SetActiveInstance(go);
            status = padNote != null
                ? "Место создано в точке фрейма, площадка выровнена. Собери базу из префабов, потом «Сохранить в префаб»."
                : "Место создано в точке фрейма. Собери базу из префабов, потом «Сохранить в префаб».";
            if (padNote != null)
            {
                status += "  (" + padNote + ")";
                statusType = MessageType.Warning;
            }
            else
            {
                statusType = MessageType.Info;
            }
        }

        /// <summary>
        /// Create (or move) a flat pad at the site point. Height is captured from
        /// the NATURAL terrain, not the current height: sampling the smoothed
        /// value would lock a new pad onto an old one and accumulate error.
        /// Returns null on success or a problem text for the caller to show.
        /// </summary>
        private string EnsurePad(string bodyName, double latitude, double longitude)
        {
            var mods = ResolveMods(bodyName);
            if (mods == null)
            {
                return "Площадка не создана: не найдено тело «" + bodyName + "» или TerrainMods.";
            }

            int index = mods.AddPad(latitude, longitude);
            if (index < 0)
            {
                return "Площадка не создана: не удалось прочитать высоту рельефа в точке.";
            }

            MarkBodyDirty(mods);
            SurfaceSceneSystem.Invalidate();
            return null;
        }

        private TerrainMods ResolveMods(string bodyName)
        {
            List<BodyAuthoring> all = SurfaceSceneSystem.AllAuthorings();
            foreach (BodyAuthoring a in all)
            {
                if (a.gameObject.name == bodyName)
                {
                    return a.GetComponent<TerrainMods>();
                }
            }

            return null;
        }

        private static void MarkBodyDirty(TerrainMods mods)
        {
            var body = mods.GetComponent<BodyAuthoring>();
            if (body == null)
            {
                return;
            }

            Undo.RecordObject(body, "Surface pad");
            EditorUtility.SetDirty(body);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(body.gameObject.scene);
        }

        private void CreateSiteAtPlayer()
        {
            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null || runner.SystemState == null || runner.DominantBody == null)
            {
                status = "Нет SimulationRunner или он не инициализирован.";
                statusType = MessageType.Error;
                return;
            }

            var body = runner.DominantBody;
            body.SurfaceLatLonAt(runner.PlayerPosition, runner.TimeSeconds, out double lat, out double lon);

            var go = new GameObject("Место " + (CountSites() + 1));
            var site = go.AddComponent<SurfaceSite>();
            site.SiteName = go.name;
            site.BodyName = body.Name;
            site.LatitudeDegrees = lat;
            site.LongitudeDegrees = lon;
            site.SnapToTerrain = true;
            site.Runner = runner;

            string padNote = createPad ? EnsurePad(body.Name, lat, lon) : null;
            site.Refresh();

            SetActiveInstance(go);
            status = string.Format(
                "Место создано там, где стоит игрок: {0} lat {1:F3}°, lon {2:F3}°.{3}",
                body.Name, lat, lon, padNote != null ? "  Площадка: " + padNote : "");
            statusType = padNote != null ? MessageType.Warning : MessageType.Info;
        }

        private string ResolveBodyName()
        {
            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner != null && !string.IsNullOrEmpty(runner.SpawnBodyName))
            {
                return runner.SpawnBodyName;
            }

            List<BodyAuthoring> all = SurfaceSceneSystem.AllAuthorings();
            foreach (BodyAuthoring a in all)
            {
                if (a.TerrainPreset != null)
                {
                    return a.gameObject.name;
                }
            }

            return all.Count > 0 ? all[0].gameObject.name : "Terra";
        }

        private void SetActiveInstance(GameObject go)
        {
            if (activeInstance != null && activeInstance != go)
            {
                if (!Application.isPlaying)
                {
                    DestroyImmediate(activeInstance);
                }
            }

            activeInstance = go;
            activeSite = go.GetComponent<SurfaceSite>();
            Selection.activeGameObject = go;
        }

        private void OpenSite(GameObject prefab)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                status = "Не удалось создать инстанс префаба.";
                statusType = MessageType.Error;
                return;
            }

            instance.name = prefab.name;
            SetActiveInstance(instance);
            SurfaceSceneSystem.Invalidate();
            activeSite.Refresh();
            status = "Открыто место \"" + activeSite.SiteName + "\". Оно в нуле сцены, Scene view показывает его собранным.";
            statusType = MessageType.Info;
        }

        private void SaveActiveToPrefab()
        {
            EnsureFolder();
            string safe = MakeSafeFileName(activeSite.SiteName);
            string path = AssetDatabase.GenerateUniqueAssetPath(SitesFolder + "/" + safe + PrefabSuffix);

            var root = activeSite.transform;
            GameObject source = new GameObject(activeSite.SiteName);
            try
            {
                // Save through a temporary clone: saving the scene instance
                // itself would remove it from the hierarchy.
                var clone = (GameObject)UnityEngine.Object.Instantiate(root.gameObject);
                clone.name = activeSite.SiteName;
                var cloneSite = clone.GetComponent<SurfaceSite>();
                cloneSite.SnapToTerrain = true;
                PrefabUtility.SaveAsPrefabAsset(clone, path);
                UnityEngine.Object.DestroyImmediate(clone);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            status = "Сохранено: " + path;
            statusType = MessageType.Info;
        }

        private void TeleportPlayer(SurfaceSite site)
        {
            if (site == null)
            {
                return;
            }

            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null || runner.SystemState == null)
            {
                status = "Телепорт только в Play: раннер не инициализирован.";
                statusType = MessageType.Error;
                return;
            }

            if (string.IsNullOrEmpty(site.BodyName))
            {
                site.BodyName = ResolveBodyName();
            }

            OrbitingBody body = null;
            foreach (OrbitingBody candidate in runner.SystemState.AllBodies)
            {
                if (candidate.Name == site.BodyName)
                {
                    body = candidate;
                    break;
                }
            }

            if (body == null)
            {
                status = "Тело \"" + site.BodyName + "\" не найдено.";
                statusType = MessageType.Error;
                return;
            }

            body.GetSurfaceState(
                site.LatitudeDegrees, site.LongitudeDegrees, site.AltitudeMeters, runner.TimeSeconds,
                out Vector3d surfacePos, out Vector3d surfaceVel);
            runner.TeleportTo(surfacePos, surfaceVel);
            status = "Игрок телепортирован на место \"" + site.SiteName + "\".";
            statusType = MessageType.Info;
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Site";
            }

            var sb = new System.Text.StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ';
                sb.Append(ok ? c : '_');
            }

            return sb.ToString().Trim();
        }
    }
}
