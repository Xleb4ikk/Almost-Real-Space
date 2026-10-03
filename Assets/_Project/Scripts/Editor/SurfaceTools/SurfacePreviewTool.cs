using System;
using Galilego.Core;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Превью поверхности в сцене: живёт под SurfaceFrame и показывает рельеф
    /// вокруг точки спавна прямо в Scene view, без Play-режима. Нужно, чтобы
    /// видеть планету и ставить базу: в сцене её нет, потому что рельеф
    /// генерится в рантайме (PlanetSurfaceRenderer.Start), а координаты тела
    /// (2.6e10 м) не влезают в float32 сцены.
    ///
    /// Объект превью служебный: HideFlags.DontSave, в сцену не пишется и в
    /// билд не попадает. Считается тем же шумом, что и игровой, поэтому
    /// «зелёное пятно под ногами» в сцене и под ногами в игре — одна земля.
    ///
    /// Размер патча ФИКСИРОВАН и не зависит от камеры SceneView — намеренно.
    /// С поверхности должен быть виден горизонт, а камера автора может стоять
    /// где угодно (в 35 км, под землёй, в ортографике) и тянуть за собой
    /// масштаб; на этом уже ловилось «превью то есть, то нет».
    /// </summary>
    [InitializeOnLoad]
    public static class SurfacePreviewTool
    {
        public const string PreviewName = "__SurfacePreview";

        /// <summary>Число колец радиальной сетки. 13 → диапазон 2^12 = 4096:1 по радиусу.</summary>
        private const int Rings = 13;

        /// <summary>Видимый радиус, на котором превью перестраивается перед кадрированием, м.</summary>
        private const double FocusVisibleRadius = 100d;

        /// <summary>
        /// Радиус превью в сцене, м. Патх на 5 км одновременно несёт кольца от
        /// 1.2 м у фрейма до 5 км на горизонте — клипмап это позволяет, а
        /// автору нужен и масштаб базы, и горизонт.
        /// </summary>
        public static double PreviewRadiusMeters = 5000d;

        private static double lastBuildTime;
        private static double lastDistance = -1d;
        private static long lastSignature;
        private static Mesh previewMesh;
        private static Material previewMaterial;
        private static SurfaceFrame boundFrame;

        /// <summary>Рисовать превью вообще (тумблер в инспекторе фрейма).</summary>
        public static bool Enabled = true;

        // Где игрок стоял в последнем проходе по Play. Кнопка «взять точку у
        // игрока» обязана работать и после выхода из Play: остановиться,
        // посмотреть превью и поставить базу — нормальный порядок, а внутри
        // Play сцену не проверишь. В EditorPrefs, а не в статике: выход из Play
        // делает перезагрузку домена и обнуляет статические поля.
        private const string PlayerLatKey = "Galilego.SurfaceFrame.LastPlayerLatitude";
        private const string PlayerLonKey = "Galilego.SurfaceFrame.LastPlayerLongitude";
        private const string PlayerBodyKey = "Galilego.SurfaceFrame.LastPlayerBody";

        /// <summary>Есть ли запомненная точка игрока.</summary>
        public static bool HasRememberedPlayer => EditorPrefs.HasKey(PlayerLatKey);

        public static double RememberedLatitude => EditorPrefs.GetFloat(PlayerLatKey, 0f);

        public static double RememberedLongitude => EditorPrefs.GetFloat(PlayerLonKey, 0f);

        public static string RememberedBody => EditorPrefs.GetString(PlayerBodyKey, string.Empty);

        private static void RememberPlayerPosition(string bodyName, double lat, double lon)
        {
            EditorPrefs.SetFloat(PlayerLatKey, (float)lat);
            EditorPrefs.SetFloat(PlayerLonKey, (float)lon);
            EditorPrefs.SetString(PlayerBodyKey, bodyName ?? string.Empty);
        }

        /// <summary>
        /// Пишет в EditorPrefs, где стоит игрок. Вызывается из
        /// EditorApplication.update (он работает и в Play) раз в 0.5 с: чаще
        /// незачем, а каждый кадр — это запись на диск каждый кадр.
        /// </summary>
        private static void RecordPlayerPosition()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - lastRecordTime < 0.5d)
            {
                return;
            }

            lastRecordTime = now;
            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null || runner.SystemState == null || runner.DominantBody == null)
            {
                return;
            }

            var body = runner.DominantBody;
            body.SurfaceLatLonAt(runner.PlayerPosition, runner.TimeSeconds, out double lat, out double lon);
            RememberPlayerPosition(body.Name, lat, lon);
        }

        private static double lastRecordTime = -1d;

        static SurfacePreviewTool()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // Кэш собранной системы инвалидируем по ЛЮБОМУ изменению иерархии.
            // Подпись (хеш полей блюпринта) ловит правку значений, но не ловит
            // момент, когда кэш впервые собран: если первая сборка успела
            // произойти, пока часть тел ещё не подъехала под StarSystemAuthoring,
            // дальше подпись совпадает и система остаётся неполной навсегда
            // (наблюдалось: в кэше остался один Sol, Terra не находилась).
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private static void OnHierarchyChanged()
        {
            SurfaceSceneSystem.Invalidate();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // В Play превью не нужно: там настоящий рельеф рисует
            // PlanetSurfaceRenderer, а служебный GO в сцене — лишний.
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                DestroyPreview();
            }
        }

        /// <summary>
        /// При перезагрузке домена НИЧЕГО не уничтожаем: статика обнулится сама,
        /// а служебный GO помечен HideFlags.DontSave и Unity уберёт его сам.
        /// Раньше здесь стоял DestroyPreview, и он убивал объект, пока инспектор
        /// на него смотрел — инспектор падал в OnEnable с MissingReference на
        /// каждом скриптовом пересчёте.
        /// </summary>
        private static void Cleanup()
        {
            lastSignature = 0L;
            lastDistance = -1d;
            boundFrame = null;
        }

        private static void Tick()
        {
            RecordPlayerPosition();

            if (Application.isPlaying || !Enabled)
            {
                if (previewMesh != null)
                {
                    DestroyPreview();
                }

                return;
            }

            if (EditorApplication.timeSinceStartup - lastBuildTime < MinRebuildIntervalSeconds)
            {
                return;
            }

            SurfaceFrame frame = ResolveFrame();
            if (frame == null || !frame.IsUsable)
            {
                if (previewMesh != null)
                {
                    DestroyPreview();
                }

                boundFrame = null;
                lastSignature = 0L;
                lastDistance = -1d;
                return;
            }

            frame.Refresh();
            HeightfieldTerrain terrain = frame.Terrain;
            double radius = PreviewRadiusMeters;

            long signature = Signature(frame, terrain);
            bool resized = Math.Abs(radius - lastDistance) > 1e-6d;
            if (signature == lastSignature && !resized && boundFrame == frame && previewMesh != null)
            {
                return;
            }

            Rebuild(frame, terrain, radius);
            lastBuildTime = EditorApplication.timeSinceStartup;
            lastDistance = radius;
            lastSignature = signature;
            boundFrame = frame;
        }

        private const double MinRebuildIntervalSeconds = 0.2d;

        /// <summary>Мокрая кромка воды — значения по умолчанию, как в сцене.</summary>
        private const float ShoreWetMetersDefault = 3f;

        private static readonly Vector4 ShoreWetTintDefault = new Vector4(0.42f, 0.52f, 0.56f, 1f);

        private static void Rebuild(SurfaceFrame frame, HeightfieldTerrain terrain, double radius)
        {
            var grid = SurfacePreviewBuilder.GridForVisibleRadius(radius, Rings);
            Mesh mesh;
            try
            {
                mesh = SurfacePreviewBuilder.Build(
                    frame.BodyState, terrain,
                    KeplerMath.DegreesToRadians(frame.LatitudeDegrees),
                    KeplerMath.DegreesToRadians(frame.LongitudeDegrees),
                    frame.AnchorBodyFixed, frame.AltitudeMeters,
                    grid, true);
            }
            catch (Exception e)
            {
                Debug.LogError("[SurfacePreview] Сборка превью провалилась: " + e);
                return;
            }

            if (mesh == null)
            {
                return;
            }

            GameObject host = EnsureHost(frame);
            var filter = host.GetComponent<MeshFilter>();
            var renderer = host.GetComponent<MeshRenderer>();

            if (previewMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(previewMesh);
            }

            previewMesh = mesh;
            filter.sharedMesh = mesh;

            // Превью рисует УПРОЩЁННЫМ шейдером, не игровым Galileo/PlanetSurface.
            // Причина практическая: чтобы игровой шейдер выглядел правильно, его
            // глобалы ставит SkyEnvironment, а тот тянет интеграл атмосферы; вызванный
            // из EditorApplication.update он съедал главный поток редактора, и Unity
            // переставала отвечать (дважды, оба раза до отката). Для постановки базы
            // игровой вид не нужен — нужна форма рельефа и snap объектов на неё
            // (SurfaceGrounded). За палитрой и текстурами превью идёт в
            // PlanetSurfaceRenderer.ApplyTerrainGlobals — это общий код, не копия.
            PlanetSurfaceRenderer.ApplyTerrainGlobals(terrain, ShoreWetMetersDefault, ShoreWetTintDefault);

            if (previewMaterial == null)
            {
                previewMaterial = SurfacePreviewBuilder.CreateFallbackMaterial();
                if (previewMaterial == null)
                {
                    Debug.LogError("[SurfacePreview] Шейдер Galilego/SurfacePreview не найден — превью без материала.");
                    return;
                }
            }

            ApplyGridSettings(previewMaterial, radius);
            previewMaterial.SetFloat("_PreviewSeaLevel", terrain != null ? (float)terrain.SeaLevelMeters : -1000000000f);
            renderer.sharedMaterial = previewMaterial;
            renderer.enabled = true;
        }

        /// <summary>
        /// Шаг сетки под масштаб: нечётная степень десяти, чтобы шаг менялся
        /// плавно при каждом зуме, а не прыгал 10 → 100 → 1000 м. Плюс тонкая
        /// подсетка внутри каждой крупной клетки.
        /// </summary>
        public static void ApplyGridSettings(Material material, double visibleRadius)
        {
            if (material == null)
            {
                return;
            }

            double km = Math.Max(0.005d, Math.Min(visibleRadius, 2000d) / 1000d);
            int power = (int)Math.Floor(Math.Log(km, 10d));
            double mantissa = Math.Pow(10d, Math.Log(km, 10d) - power);
            double[] steps = { 1d, 2d, 5d };
            double chosen = steps[0];
            foreach (double s in steps)
            {
                if (mantissa >= s)
                {
                    chosen = s;
                }
            }

            material.SetFloat("_PreviewGridSpacing", (float)(chosen * Math.Pow(10d, power)));
            material.SetFloat("_PreviewGridMinor", 0.2f);
            material.SetFloat("_PreviewGridStrength", 0.55f);
            material.SetFloat("_PreviewGridFade", (float)Math.Max(1.5d, visibleRadius * 2.5d / 1000d));
        }

        private static GameObject EnsureHost(SurfaceFrame frame)
        {
            Transform existing = frame.transform.Find(PreviewName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var go = new GameObject(PreviewName);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(frame.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// Показать превью в Scene view: сначала узкое (чтобы F не улетел за
        /// горизонт на 5 км), потом кадрируем. Выделение НЕ трогаем — служебный
        /// GO помечен DontSave и не переживает перезагрузку домена, оставленное
        /// выделенным инспектор падает в OnEnable с MissingReference.
        /// </summary>
        public static bool FocusSceneView(SurfaceFrame frame)
        {
            if (frame == null || !frame.IsUsable)
            {
                Debug.LogWarning("Surface frame: фрейм не найден или тело не разрешилось.");
                return false;
            }

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                Debug.LogWarning("Surface frame: нет открытой Scene View.");
                return false;
            }

            double saved = PreviewRadiusMeters;
            PreviewRadiusMeters = FocusVisibleRadius;
            frame.Refresh();
            Rebuild(frame, frame.Terrain, FocusVisibleRadius);
            lastDistance = FocusVisibleRadius;
            lastSignature = Signature(frame, frame.Terrain);
            boundFrame = frame;
            lastBuildTime = EditorApplication.timeSinceStartup;
            PreviewRadiusMeters = saved;

            view.orthographic = false;
            view.rotation = Quaternion.Euler(30f, 0f, 0f);
            view.Frame(new Bounds(frame.transform.position, Vector3.one * 250f), false);
            view.Repaint();
            return true;
        }

        /// <summary>
        /// Подпись всего, что меняет картинку превью. Профиль рельефа целиком не
        /// хешируем (45 полей): форму определяют seed, амплитуда, частота, октавы,
        /// маски и warp, а на остальное есть кнопка перестроения.
        /// </summary>
        private static long Signature(SurfaceFrame frame, HeightfieldTerrain terrain)
        {
            unchecked
            {
                long h = 17L;
                h = (h * 31L) + (long)Math.Round(frame.LatitudeDegrees * 1e7d);
                h = (h * 31L) + (long)Math.Round(frame.LongitudeDegrees * 1e7d);
                h = (h * 31L) + (long)Math.Round(frame.AltitudeMeters * 1e4d);
                h = (h * 31L) + (long)Math.Round(frame.BodyRadius * 1e3d);
                h = (h * 31L) + frame.BodyName.GetHashCode();
                if (terrain == null)
                {
                    return h;
                }

                h = (h * 31L) + terrain.Seed;
                h = (h * 31L) + (long)Math.Round(terrain.AmplitudeMeters * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.BaseFrequency * 1e4d);
                h = (h * 31L) + terrain.Octaves;
                h = (h * 31L) + (long)Math.Round(terrain.SeaLevelMeters * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.Lacunarity * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.Gain * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.RidgedMix * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.ContinentFrequency * 1e4d);
                h = (h * 31L) + terrain.ContinentOctaves;
                h = (h * 31L) + (long)Math.Round(terrain.ContinentThreshold * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.ContinentDepth * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.PlainMix * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.WarpStrength * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.DetailMix * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.ColorNoiseStrength * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.ColorDetailStrength * 1e4d);
                return h;
            }
        }

        /// <summary>Фрейм, к которому привязано превью: выделенный, иначе первый в сцене.</summary>
        public static SurfaceFrame ResolveFrame()
        {
            if (Selection.activeGameObject != null)
            {
                var selected = Selection.activeGameObject.GetComponentInParent<SurfaceFrame>();
                if (selected != null)
                {
                    return selected;
                }
            }

            SurfaceFrame[] all = UnityEngine.Object.FindObjectsByType<SurfaceFrame>(FindObjectsInactive.Include);
            return all.Length > 0 ? all[0] : null;
        }

        public static void DestroyPreview()
        {
            if (previewMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(previewMesh);
                previewMesh = null;
            }

            if (previewMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(previewMaterial);
                previewMaterial = null;
            }

            foreach (SurfaceFrame frame in UnityEngine.Object.FindObjectsByType<SurfaceFrame>(FindObjectsInactive.Include))
            {
                Transform child = frame.transform.Find(PreviewName);
                if (child == null)
                {
                    continue;
                }

                // Если превью выделено, инспектор держит на него ссылку, а
                // DestroyImmediate обнулит объект — и следующий OnEnable
                // инспектора упадёт MissingReference. Снимаем выделение заранее.
                if (Selection.activeGameObject == child.gameObject)
                {
                    Selection.activeGameObject = frame.gameObject;
                }

                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            lastSignature = 0L;
            lastDistance = -1d;
            boundFrame = null;
        }

        /// <summary>Пометить превью как устаревшее (после правки профиля рельефа).</summary>
        public static void Invalidate()
        {
            lastSignature = 0L;
            lastDistance = -1d;
        }
    }
}
