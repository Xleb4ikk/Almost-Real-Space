#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Galilego.Core;
using Galilego.Universe;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Съёмка и замеры поверхности. Отдельный Editor-скрипт, а не eval, потому
    /// что в Play главный поток насыщен сборкой чанков и любой однострочный
    /// eval упирается в лимит 5 с на стороне пайплайна. Корутина ждёт очередь
    /// по кадрам, поэтому ограничения на время одной операции не касается.
    ///
    /// Порядок работы всех трёх пунктов меню одинаков: дождаться опустошения
    /// очереди чанков, снять кадр, записать PNG. Очередь пуста, когда
    /// SurfacePerf.QueuedBuilds == 0 и InFlightBuilds == 0.
    /// </summary>
    public static class SurfaceCaptureTool
    {
        private const string OutDir = "Assets/Screenshots/dampmodes";
        private const string LogPath = "Logs/surface-capture.log";
        private const double QueueTimeoutSeconds = 60d;

        /// <summary>
        /// Позиции съёмки. Фиксированы в коде, как и требовалось: сравнение
        /// режимов имеет смысл только на одинаковых кадрах.
        /// Высоты: орбита, 30 км, 2 км, 200 м.
        /// </summary>
        private static readonly double[] AltitudesMeters = { 400000d, 30000d, 2000d, 200d };

        private static readonly StringBuilder Log = new StringBuilder();

        // ---------------------------------------------------------------- ссылки

        [MenuItem("Galilego/Capture/1. Water Skirt (0.03 / 0.3 / 1.0)")]
        public static void CaptureWaterSkirt() => Start(RunWaterSkirt);

        [MenuItem("Galilego/Capture/2. Damp Modes (Off/Accum/Gradient)")]
        public static void CaptureDampModes() => Start(RunDampModes);

        [MenuItem("Galilego/Measure/3. Flight Benchmark")]
        public static void FlightBenchmark() => Start(RunFlightBenchmark);

        [MenuItem("Galilego/Capture/4. Underwater Ceiling (full: 1/3/10 m x 3 angles x 3 times)")]
        public static void CaptureUnderwaterCeilingFull() => Start(() => RunUnderwaterCeiling(true));

        [MenuItem("Galilego/Capture/4b. Underwater Ceiling (quick: 1 and 3 m)")]
        public static void CaptureUnderwaterCeilingQuick() => Start(() => RunUnderwaterCeiling(false));


        private static void Start(Func<IEnumerator> routine)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SurfaceCapture] уже в Play — выйдите сначала.");
                return;
            }

            Log.Clear();
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? ".");
            _savedReloadSetting = EditorSettings.enterPlayModeOptionsEnabled;
            _savedPlayModeOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            SessionState.SetBool("SurfaceCapture.DomainReloadDisabled", true);

            // Проект входит в Play с runInBackground = 0
            // (ProjectSettings.asset:90). При потере фокуса редактора игровой
            // цикл ОСТАНАВЛИВАЕТСЯ: не тикают LateUpdate, не качаются джобы
            // сборки чанков, не перерисовывается HUD. Для съёмки это значит
            // «зависшую» очередь и пустые PNG, причём выглядит оно как поломка
            // конвейера, а не как потеря фокуса. Поэтому на время съёмки
            // требуем работу в фоне и по возвращении из Play возвращаем
            // былое значение.
            _savedRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            if (!Application.runInBackground)
            {
                // Не подействовало — падаем на настройку проекта, её тоже
                // восстановим при выходе из Play.
                _savedPlayerRunInBackground = PlayerSettings.runInBackground;
                PlayerSettings.runInBackground = true;
            }

            SessionState.SetBool("SurfaceCapture.RunInBackgroundForced", !Application.runInBackground);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Pump;
            _routine = routine();
            _phase = "вход в Play";
            _stop = false;
            LogLine("старт " + _phase + ", runInBackground=" + Application.runInBackground
                + " focused=" + EditorApplication.isFocused);
            EditorApplication.isPlaying = true;
        }

        private static IEnumerator _routine;
        private static string _phase;
        private static bool _stop;
        private static bool _savedReloadSetting;
        private static EnterPlayModeOptions _savedPlayModeOptions;
        private static bool _savedRunInBackground;
        private static bool _savedPlayerRunInBackground;

        private static void RestoreRunInBackground()
        {
            Application.runInBackground = _savedRunInBackground;
            if (SessionState.GetBool("SurfaceCapture.RunInBackgroundForced", false))
            {
                PlayerSettings.runInBackground = _savedPlayerRunInBackground;
                SessionState.EraseBool("SurfaceCapture.RunInBackgroundForced");
            }
        }


        /// <summary>
        /// Вход в Play по умолчанию перезагружает домен, и тогда все static'ы
        /// этого класса обнуляются: корутина теряется молча, ничего не
        /// снимается, лог остаётся пустым. Поэтому на время съёмки доменная
        /// перезагрузка выключается, а исходная настройка возвращается при
        /// выходе из Play и, страховочно, при следующей загрузке редактора.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RestoreReloadSettingOnEditorLoad()
        {
            if (SessionState.GetBool("SurfaceCapture.RunInBackgroundForced", false))
            {
                EditorSettings.enterPlayModeOptionsEnabled = false;
                Debug.LogWarning("[SurfaceCapture] аварийно восстановлен runInBackground проекта: инструмент включал его на время съёмки и не успел вернуть. Проверьте Project Settings > Player > Run In Background.");
                PlayerSettings.runInBackground = false;
                SessionState.EraseBool("SurfaceCapture.RunInBackgroundForced");
            }

            if (SessionState.GetBool("SurfaceCapture.DomainReloadDisabled", false))
            {
                // Значение не восстанавливаем: после перезагрузки редактора его уже
                // негде взять, поэтому сбрасываем флаг и пишем в лог, чтобы автор
                // проверил настройку сам.
                EditorSettings.enterPlayModeOptionsEnabled = false;
                Debug.LogWarning("[SurfaceCapture] аварийно сброшены enterPlayModeOptions: DisableDomainReload был включён инструментом и снят при перезагрузке редактора. Проверьте Project Settings > Editor > Enter Play Mode Settings.");
                SessionState.EraseBool("SurfaceCapture.DomainReloadDisabled");
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                // Переподключаем обработчики: даже с выключенной доменной
                // перезагрузкой Unity может пересоздать делегаты.
                EditorApplication.update -= Pump;
                EditorApplication.update += Pump;
                if (_routine != null && !_routine.MoveNext())
                {
                    EditorApplication.isPlaying = false;
                }
            }

            if (change == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
                EditorApplication.update -= Pump;
                EditorSettings.enterPlayModeOptionsEnabled = _savedReloadSetting;
                if (_savedReloadSetting)
                {
                    // Восстанавливаем ТОЧНОЕ значение, а не EnterPlayModeOptions.None:
                    // в проекте стояло 3 (DisableDomainReload | DisableSceneReload),
                    // и обнуление молча убирало DisableSceneReload, меняя поведение
                    // Play-режима во всём редакторе.
                    EditorSettings.enterPlayModeOptions = _savedPlayModeOptions;
                }

                SessionState.EraseBool("SurfaceCapture.DomainReloadDisabled");
                RestoreRunInBackground();
                LogLine("вышли из Play");
                FlushLog();
                _routine = null;
                _stop = true;
                Debug.Log("[SurfaceCapture] готово, лог: " + LogPath);
            }
        }

        private static void Pump()
        {
            if (_stop || _routine == null)
            {
                return;
            }

            if (!_routine.MoveNext())
            {
                EditorApplication.isPlaying = false;
            }
        }

        // ------------------------------------------------------- водная юбка

        private static IEnumerator RunWaterSkirt()
        {
            yield return WaitForRunner();
            var runner = Runner();
            if (runner == null)
            {
                yield break;
            }

            var body = FindBody(runner, "Terra");
            if (body == null)
            {
                LogLine("NO body Terra");
                yield break;
            }

            var terrain = body.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                LogLine("NO HeightfieldTerrain");
                yield break;
            }

            if (!EnableNoclip(runner))
            {
                LogLine("FATAL: noclip not available, capture impossible");
                yield break;
            }

            LogLine("профиль: noiseStyle=" + terrain.NoiseStyle
                + " ridgedMode=" + terrain.RidgedMode
                + " tailKnee=" + terrain.TailKnee
                + " depthKnee=" + terrain.DepthKnee
                + " amp=" + terrain.AmplitudeMeters);

            // Ищем берег, но не одним кадром: GetHeightMeters в Editor дорогой,
            // и весь поиск в один кадр упирается в кадровый бюджет. Поэтому
            // сетка обходится порциями.
            double lat = 0d, lon = 0d;
            bool found = false;
            for (int i = 0; i < 90 && !found; i++)
            {
                double la = (-78.0 + (156.0 * i / 90.0)) * Math.PI / 180d;
                for (int j = 0; j < 180 && !found; j++)
                {
                    double lo = (-180.0 + (360.0 * j / 180.0)) * Math.PI / 180d;
                    double h = terrain.GetHeightMeters(body, la, lo);
                    if (Math.Abs(h) > 600d)
                    {
                        continue;
                    }

                    int land = 0, sea = 0;
                    for (int a = -1; a <= 1; a++)
                    {
                        for (int b = -1; b <= 1; b++)
                        {
                            if (a == 0 && b == 0)
                            {
                                continue;
                            }

                            double na = la + (a * 0.6 * Math.PI / 180d);
                            double nb = lo + (b * 0.6 * Math.PI / 180d);
                            if (terrain.GetHeightMeters(body, na, nb) > 0d)
                            {
                                land++;
                            }
                            else
                            {
                                sea++;
                            }
                        }
                    }

                    if (land >= 2 && sea >= 2)
                    {
                        lat = la;
                        lon = lo;
                        found = true;
                    }
                }

                yield return null;
            }

            if (!found)
            {
                LogLine("coast not found");
                yield break;
            }

            LogLine(string.Format(
                CultureInfo.InvariantCulture,
                "берег: lat={0:F3} lon={1:F3}", lat * 180.0 / Math.PI, lon * 180.0 / Math.PI));

            var renderer = FindRenderer();
            if (renderer == null)
            {
                LogLine("NO PlanetSurfaceRenderer");
                yield break;
            }

            foreach (float factor in new float[] { 0.03f, 0.3f, 1f })
            {
                renderer.WaterSkirtFactor = factor;
                LogLine("=== WaterSkirtFactor = " + factor.ToString("F2", CultureInfo.InvariantCulture)
                    + " | SkirtFactor = " + renderer.SkirtFactor.ToString("F2", CultureInfo.InvariantCulture));

                foreach (double alt in AltitudesMeters)
                {
                    double actual = PlaceAt(runner, body, terrain, lat, lon, alt);
                    yield return null;

                    // Ожидание развёрнуто здесь, а не вынесено в отдельную
                    // корутину: yield return IEnumerator НЕ ждёт её в
                    // драйвере, который шагает только по внешней корутине, -
                    // внешняя ускакивает вперёд и снимок делается на
                    // недогруженной планете.
                    float drained = 0f;
                    bool ok = false;
                    int finalized = 0;
                    while (true)
                    {
                        // ChunksFinalized - счётчик ЗА КАДР. Без требования
                        // finalized > 0 проверка проходит тривиально: сразу
                        // после телепорта очередь ещё пуста.
                        finalized += SurfacePerf.ChunksFinalized;
                        if (finalized > 0 && SurfacePerf.QueuedBuilds == 0 && SurfacePerf.InFlightBuilds == 0)
                        {
                            ok = true;
                            break;
                        }

                        if (drained > (float)QueueTimeoutSeconds)
                        {
                            LogLine(string.Format(
                                CultureInfo.InvariantCulture,
                                "  TIMEOUT {0:F0}s: queued={1} inFlight={2} finalized={3} - shot on unloaded planet",
                                QueueTimeoutSeconds, SurfacePerf.QueuedBuilds,
                                SurfacePerf.InFlightBuilds, finalized));
                            ok = false;
                            break;
                        }

                        drained += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    // Повторная установка перед самым снимком: пока ждали
                    // очередь, планета повернулась, и игрок уехал из-под
                    // нужной точки. Ставим по текущему времени ещё раз.
                    double finalAlt = PlaceAt(runner, body, terrain, lat, lon, alt);
                    yield return null;

                    bool altOk = Math.Abs(finalAlt - (Math.Max(0d, terrain.GetHeightMeters(body, lat, lon)) + alt))
                        <= AltitudeToleranceMeters;
                    if (!altOk)
                    {
                        LogLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "  REJECT alt={0:F0}: player at {1:F0} m above sea, wanted {2:F0} - shot discarded",
                            alt, finalAlt, alt));
                        continue;
                    }

                    string name = string.Format(
                        CultureInfo.InvariantCulture,
                        "waterskirt_{0:F2}_alt{1:F0}",
                        factor,
                        alt);
                    string path = OutDir + "/" + name + ".png";
                    ScreenCapture.CaptureScreenshot(path);
                    LogLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  alt={0,8:F0} м  drained={1} in {2:F1} s  playerAt={3:F0} м -> {4}",
                        alt, ok, drained, finalAlt, path));
                    yield return null;
                    yield return null;
                    yield return null;
                }
            }

            LogLine("water skirt capture done");
        }

        // -------------------------------------------------- режимы домена

        private static IEnumerator RunDampModes()
        {
            yield return WaitForRunner();
            var runner = Runner();
            var body = runner == null ? null : FindBody(runner, "Terra");
            var terrain = body == null ? null : body.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                LogLine("NO profile");
                yield break;
            }

            if (!EnableNoclip(runner))
            {
                LogLine("FATAL: noclip not available, capture impossible");
                yield break;
            }

            // Три режима, но менять поле у уже построенного terrain-модели
            // недостаточно: HeightfieldTerrain копируется из профиля при
            // инициализации, поэтому меняем профиль в ассете И модель в рантайме.
            var asset = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(
                "Assets/_Project/Profiles/Terrain/EarthLike_Perlin.asset");
            double savedDamp = terrain.SlopeDamp;
            int savedMode = terrain.SlopeDampMode;

            for (int mode = 0; mode <= 2; mode++)
            {
                terrain.SlopeDamp = 0.6d;
                terrain.SlopeDampMode = mode;
                if (asset != null)
                {
                    asset.Profile.SlopeDamp = 0.6d;
                    asset.Profile.SlopeDampMode = mode;
                    EditorUtility.SetDirty(asset);
                }

                LogLine("=== SlopeDampMode = " + mode + " (" + ModeName(mode) + ")");

                for (int seed = 0; seed < 3; seed++)
                {
                    for (int ai = 0; ai < AltitudesMeters.Length; ai++)
                    {
                        double alt = AltitudesMeters[ai];
                        PlaceAt(runner, body, terrain, 12.0 * seed - 12.0, 30.0 * seed, alt);
                        yield return null;

                        // См. пояснение в RunWaterSkirt: ожидание развёрнуто
                        // инлайн, yield return вложенной корутины не ждёт её.
                        float drained = 0f;
                        bool ok = false;
                        int finalized = 0;
                        while (true)
                        {
                            finalized += SurfacePerf.ChunksFinalized;
                            if (finalized > 0 && SurfacePerf.QueuedBuilds == 0 && SurfacePerf.InFlightBuilds == 0)
                            {
                                ok = true;
                                break;
                            }

                            if (drained > (float)QueueTimeoutSeconds)
                            {
                                LogLine(string.Format(
                                    CultureInfo.InvariantCulture,
                                    "  TIMEOUT {0:F0}s: queued={1} inFlight={2} finalized={3} - shot on unloaded planet",
                                    QueueTimeoutSeconds, SurfacePerf.QueuedBuilds,
                                    SurfacePerf.InFlightBuilds, finalized));
                                ok = false;
                                break;
                            }

                            drained += Time.unscaledDeltaTime;
                            yield return null;
                        }

                        string path = string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}_{1}_{2}",
                            ModeName(mode),
                            seed,
                            alt);
                        path = OutDir + "/" + path + ".png";
                        ScreenCapture.CaptureScreenshot(path);
                        LogLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "  mode={0} seed={1} alt={2,8:F0} м очередь={3} {4:F1} с -> {5}",
                            ModeName(mode), seed, alt, ok, drained, path));
                        yield return null;
                        yield return null;
                        yield return null;
                    }
                }
            }

            terrain.SlopeDamp = savedDamp;
            terrain.SlopeDampMode = savedMode;
            if (asset != null)
            {
                asset.Profile.SlopeDamp = savedDamp;
                asset.Profile.SlopeDampMode = savedMode;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            LogLine("damp modes capture done");
        }

        private static string ModeName(int m)
        {
            return m == 0 ? "Off" : (m == 1 ? "Accum" : "Gradient");
        }

        // ------------------------------------------------------------- пролёт

        private static IEnumerator RunFlightBenchmark()
        {
            yield return WaitForRunner();
            var runner = Runner();
            var body = runner == null ? null : FindBody(runner, "Terra");
            if (body == null)
            {
                LogLine("NO body");
                yield break;
            }

            if (!EnableNoclip(runner))
            {
                LogLine("FATAL: noclip not available, benchmark impossible");
                yield break;
            }

            var terrain = body.Terrain as HeightfieldTerrain;
            var asset = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(
                "Assets/_Project/Profiles/Terrain/EarthLike_Perlin.asset");
            var earthLike = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(
                "Assets/_Project/Profiles/Terrain/EarthLike.asset");

            foreach (string which in new string[] { "EarthLike", "EarthLike_Perlin" })
            {
                var src = which == "EarthLike" ? earthLike : asset;
                if (src == null)
                {
                    LogLine("NO profile " + which);
                    continue;
                }

                if (terrain != null)
                {
                    CopyProfile(src.Profile, terrain);
                }

                LogLine("=== профиль " + which + ": noiseStyle=" + (terrain == null ? -1 : terrain.NoiseStyle)
                    + " ridgedMode=" + (terrain == null ? -1 : terrain.RidgedMode)
                    + " amp=" + (terrain == null ? -1 : terrain.AmplitudeMeters));

                for (int run = 0; run < 3; run++)
                {
                    // Телепорт: сюда и приходит вся пиковая очередь.
                    double lat = -12.0 + (7.0 * run);
                    double lon = 20.0 + (23.0 * run);
                    PlaceAt(runner, body, terrain, lat, lon, 200d);

                    int peakQueued = 0, peakDesired = 0, peakActive = 0;
                    int framesBusyFinalize = 0;
                    int frames = 0;
                    int totalFinalized = 0;
                    int totalBuilt = 0;
                    int totalErrors = 0;
                    int totalDropped = 0;
                    double drainSeconds = 0d;
                    bool drained = false;
                    float since = 0f;

                    // ChunksFinalized/ChunksBuilt - счётчики ЗА КАДР, их сбрасывает
                    // SurfacePerf.BeginFrame, поэтому накапливаем вручную.
                    // QueuedBuilds/InFlightBuilds - датчики, их смотрим как есть.
                    while (frames < 3600)
                    {
                        frames++;
                        totalFinalized += SurfacePerf.ChunksFinalized;
                        totalBuilt += SurfacePerf.ChunksBuilt;
                        totalErrors += SurfacePerf.FinalizeErrors;
                        totalDropped += SurfacePerf.QueueDropped;
                        since += Time.unscaledDeltaTime;
                        if (SurfacePerf.QueuedBuilds > peakQueued)
                        {
                            peakQueued = SurfacePerf.QueuedBuilds;
                        }

                        if (SurfacePerf.DesiredNodes > peakDesired)
                        {
                            peakDesired = SurfacePerf.DesiredNodes;
                        }

                        if (SurfacePerf.ActiveChunks > peakActive)
                        {
                            peakActive = SurfacePerf.ActiveChunks;
                        }

                        if (SurfacePerf.ChunksFinalized > 0)
                        {
                            framesBusyFinalize++;
                        }

                        if (!drained && SurfacePerf.QueuedBuilds == 0 && SurfacePerf.InFlightBuilds == 0)
                        {
                            drainSeconds = since;
                            drained = true;
                            break;
                        }

                        if (since > QueueTimeoutSeconds)
                        {
                            drainSeconds = since;
                            break;
                        }

                        yield return null;
                    }

                    LogLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  прогон {0}: пик очереди={1} DesiredNodes={2} ActiveChunks={3} "
                        + "кадров с финализацией={4} до опустошения={5:F1} с (ok={6}) "
                        + "финализировано={7} построено={8} ошибок={9} отброшено={10}",
                        run, peakQueued, peakDesired, peakActive, framesBusyFinalize,
                        drainSeconds, drained, totalFinalized, totalBuilt,
                        totalErrors, totalDropped));
                }
            }

            LogLine("flight benchmark done");
        }

        private static void CopyProfile(TerrainProfile from, HeightfieldTerrain to)
        {
            to.NoiseStyle = from.NoiseStyle;
            to.MaskNoiseStyle = from.MaskNoiseStyle;
            to.AmplitudeMeters = from.AmplitudeMeters;
            to.RidgedMix = from.RidgedMix;
            to.RidgedMode = from.RidgedMode;
            to.RidgedGamma = from.RidgedGamma;
            to.ContinentThreshold = from.ContinentThreshold;
            to.ContinentDepth = from.ContinentDepth;
            to.TailKnee = from.TailKnee;
            to.TailThreshold = from.TailThreshold;
            to.DepthKnee = from.DepthKnee;
            to.DepthThreshold = from.DepthThreshold;
        }

        // ------------------------------------------------- подводный потолок

        /// <summary>
        /// Съёмка потолка воды снизу: окно Снелла, рябь, зеркало толщи.
        ///
        /// Отличия от RunWaterSkirt, и каждая — на устранённой ошибке:
        ///
        ///  1. ГЛУБИНА. PlaceAt берёт Math.Max(0, высота рельефа) + altitude,
        ///     и при altitude &lt; 0 это точка НИЖЕ уровня моря, то есть под
        ///     водой — это уже умелось. Но камера стоит на
        ///     Target.position + eyeOffset (2 м вверх), а UnderwaterEffect меряет
        ///     глубину по камере: «-3 м» для игрока даёт -1 м для камеры. Отсюда
        ///     замкнутая петля по EyeDepthMeters вместо угадывания EyeHeightMeters.
        ///
        ///  2. ВРЕМЯ СУТОК задаётся выбором точки, а не перемоткой времени:
        ///     время симвремена приватно и живёт в эфемеридах, а долгота
        ///     однозначно задаёт местное солнце. Ищем океан по долготе, пока
        ///     высота солнца не попала в нужное окно.
        ///
        ///  3. ГОТОВНОСТЬ ПЛАНЕТЫ. Прежний гейт требовал ChunksFinalized &gt; 0,
        ///     а он растёт ТОЛЬКО в async-ветке, тогда как первый кадр строит
        ///     все desired узлы СИНХРОННО (firstFrameGuard,
        ///     PlanetSurfaceRenderer.cs:1527). На полностью загруженной планете
        ///     ChunksFinalized структурно 0 — и инструмент 60 с ждал кадр на
        ///     «незагруженной планете», которой не было (см. surface-capture.log).
        ///     Теперь гейт по покрытию кэша: CachedChunks &gt; 0, очередь и полёт
        ///     пусты, DesiredInKeep == VisibleChunks, и так несколько кадров
        ///     подряд.
        ///
        ///  4. ДИАГНОСТИКА ЗАВИСАНИЯ. Каждую секунду пишем реальные счётчики и
        ///     Application.isFocused. Прошлый прогон упёрся в runInBackground=0:
        ///     при потере фокуса редактора игровой цикл останавливается, и ВСЕ
        ///     счётчики замирают на последних значениях — «очередь 305, в полёте
        ///     6, ничего не финализируется» выглядит как зависание, хотя никто не
        ///     завис. isFocused в логе отличает это от настоящего зависания.
        /// </summary>
        private static IEnumerator RunUnderwaterCeiling(bool fullMatrix)
        {
            // Ожидание раннера — ВПЛЕТЕНО, а не yield return WaitForRunner():
            // Pump делает ровно один MoveNext за тик, поэтому вложенная
            // корутина не выполняется НИ РАЗУ, и первый же тик после входа в
            // Play видел пустую сцену (Runner() == null), корутина завершалась,
            // и инструмент выходил из Play через секунду после старта.
            float runnerWait = 0f;
            while (runnerWait < 30f && Runner() == null)
            {
                runnerWait += Time.unscaledDeltaTime;
                yield return null;
            }

            var runner = Runner();
            if (runner == null)
            {
                yield break;
            }

            var body = FindBody(runner, "Terra");
            if (body == null)
            {
                LogLine("NO body Terra");
                yield break;
            }

            var terrain = body.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                LogLine("NO HeightfieldTerrain");
                yield break;
            }

            if (!EnableNoclip(runner))
            {
                LogLine("FATAL: noclip not available, capture impossible");
                yield break;
            }

            var effect = UnityEngine.Object.FindAnyObjectByType<UnderwaterEffect>();
            var viewFog = UnityEngine.Object.FindAnyObjectByType<UnderwaterViewFog>();
            var renderer = FindRenderer();

            // Облака у Terra в сцене выключены (body.Clouds == null, поэтому
            // PlanetCloudsView каждый кадр ставит _CldCloudActive = 0), и окно
            // Снелла осталось бы без облаков — а именно они делают преломление
            // читаемым: без них в окне только градиент. Включаем профиль на
            // время съёмки и возвращаем былое значение, потому что это
            // состояние тела, а не настройка инструмента.
            //
            // CloudProfile — обычный класс, а не ScriptableObject, поэтому
            // AssetDatabase.LoadAssetAtPath его не грузит (первая версия
            // инструмента так и не скомпилировалась на этом). Берём
            // конструктор с проектными умолчаниями и поднимаем покрытие.
            CloudProfile savedClouds = body.Clouds;
            var cloudProfile = new CloudProfile();
            cloudProfile.Coverage = 0.6f;
            body.Clouds = cloudProfile;
            LogLine("облака: включаю профиль на время съёмки, покрытие="
                + cloudProfile.Coverage.ToString("F2", CultureInfo.InvariantCulture)
                + ", слой " + cloudProfile.BottomAltitudeMeters.ToString("F0", CultureInfo.InvariantCulture)
                + ".." + cloudProfile.TopAltitudeMeters.ToString("F0", CultureInfo.InvariantCulture) + " м");

            LogLine("UnderwaterEffect=" + (effect != null) + " UnderwaterViewFog=" + (viewFog != null));
            if (viewFog != null)
            {
                LogLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "туман: sigma=({0:F3},{1:F3},{2:F3}) 1/м, цвет={3} ambient={4:F2} debugMode={5}",
                    viewFog.SigmaPerMeter.x, viewFog.SigmaPerMeter.y, viewFog.SigmaPerMeter.z,
                    viewFog.WaterColor, viewFog.Ambient, viewFog.DebugMode));
                viewFog.DebugMode = 0;
            }

            if (renderer == null)
            {
                LogLine("NO PlanetSurfaceRenderer");
                yield break;
            }

            double[] depths = fullMatrix ? UnderwaterDepthsMeters : new double[] { 1d, 3d };
            double deepestDepth = 0d;
            for (int i = 0; i < depths.Length; i++)
            {
                deepestDepth = Math.Max(deepestDepth, depths[i]);
            }
            LogLine(string.Format(
                CultureInfo.InvariantCulture,
                "=== подводный потолок: матрица {0} глубины × {1} углов × {2} времени суток",
                depths.Length, UnderwaterElevationsDeg.Length, SolarWindows.Length));
            LogLine(string.Format(
                CultureInfo.InvariantCulture,
                "параметры: async={0} jobsInFlight={1} finalizeBudget={2:F2} мс buildsPerFrame={3} focused={4}",
                renderer.AsyncChunkBuild, renderer.MaxChunkJobsInFlight,
                renderer.ChunkFinalizeBudgetMs, renderer.BuildsPerFrame, Application.isFocused));

            FirstPersonCamera.DebugLookOverride = true;

            foreach (SolarWindow window in SolarWindows)
            {
                // --- точка океана с нужной высотой солнца --------------------
                // Ищем по СЕТКЕ широт и долгот, а не по одной широте: рельеф
                // Земли не океан на всех параллелях, и поиск только по долготе
                // намертво застревал там, где океана нет (первая версия
                // инструмента так и не нашла точку ни для одного из трёх
                // состояний). Всё, что отсеивается, считаем и пишем в лог —
                // иначе «НЕ НАЙДЕН» ничего не говорит о причине.
                bool found = false;
                double spotLat = 0d, spotLon = 0d, spotSun = 0d, spotFloor = 0d;
                Vector3 spotUp = Vector3.up, spotNorth = Vector3.forward, spotSunDir = Vector3.up;

                int sampled = 0, tooShallow = 0, noFrame = 0, wrongSun = 0;
                double deepest = 0d;
                double sunMin = 999d, sunMax = -999d;

                double requiredFloor = -Math.Max(deepestDepth, 5d);
                for (int latIndex = 1; latIndex < LatitudeSteps - 1 && !found; latIndex++)
                {
                    // Широты ±75°, а не полный разброс до полюсов: у полюса
                    // касательная «на север» вырождена (TryLocalFrame её
                    // отбрасывает), да и композиция под полюсом бессмысленна.
                    // Старт со знаком МИНУС: разброс должен идти от -75° в
                    // сторону +75°. Со знаком плюс первая же проба давала
                    // +75+12.5 = 87.5°, а в сумме до 150°, то есть точку
                    // за пределами планеты — GetRawHeightMeters там не океан,
                    // и кадры «1 м» выходили пустыми.
                    double latRad = -SearchLatitudeRangeRad
                        + ((2.0 * SearchLatitudeRangeRad * latIndex) / (LatitudeSteps - 1));

                    for (int i = 0; i < LongitudeSteps && !found; i++)
                    {
                        double lonRad = -Math.PI + ((2.0 * Math.PI * i) / LongitudeSteps);

                        // ГЛУБИНА — из GetRawHeightMeters, а не GetHeightMeters:
                        // последняя зажимает океан до уровня моря
                        // (TerrainModel.cs:485-488, «иначе раскопка ниже уровня
                        // моря молча наполнялась бы водой»), то есть по ней
                        // весь океан имеет высоту ровно 0 м и «глубокого океана»
                        // не существует. Первая версия поиска так и не нашла
                        // точку ни для одного состояния.
                        double raw = terrain.GetRawHeightMeters(body, latRad, lonRad);
                        sampled++;
                        if (raw < deepest)
                        {
                            deepest = raw;
                        }

                        // Нужен океан глубже самой глубокой точки съёмки плюс
                        // запас: иначе камера окажется на дне и кадр будет про
                        // дно, а не про потолок.
                        if (raw > requiredFloor)
                        {
                            tooShallow++;
                            if ((sampled & 31) == 31)
                            {
                                yield return null;
                            }

                            continue;
                        }

                        if (!TryLocalFrame(runner, body, latRad, lonRad, 0d,
                                out Vector3 up, out Vector3 north, out Vector3 sunDir, out double sunElevDeg))
                        {
                            noFrame++;
                            if ((sampled & 31) == 31)
                            {
                                yield return null;
                            }

                            continue;
                        }

                        if (sunElevDeg < sunMin)
                        {
                            sunMin = sunElevDeg;
                        }

                        if (sunElevDeg > sunMax)
                        {
                            sunMax = sunElevDeg;
                        }

                        if (sunElevDeg < window.MinElevDeg || sunElevDeg > window.MaxElevDeg)
                        {
                            wrongSun++;
                            if ((sampled & 31) == 31)
                            {
                                yield return null;
                            }

                            continue;
                        }

                        spotLat = latRad;
                        spotLon = lonRad;
                        spotSun = sunElevDeg;
                        spotFloor = raw;
                        spotUp = up;
                        spotNorth = north;
                        spotSunDir = sunDir;
                        found = true;
                    }
                }

                LogLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: точек {1}, мелко {2}, без рамки {3}, солнце не в окне {4}; "
                    + "дно до {5:F0} м (нужно < {6:F0}), солнце {7:F1}..{8:F1}°",
                    window.Name, sampled, tooShallow, noFrame, wrongSun,
                    deepest, requiredFloor, sunMin, sunMax));

                if (!found)
                {
                    LogLine(window.Name + ": океан с солнцем " + window.Describe() + " НЕ НАЙДЕН");
                    continue;
                }

                LogLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: lat={1:F3} lon={2:F3} солнце={3:F1}° дно={4:F0} м",
                    window.Name, spotLat * 180.0 / Math.PI, spotLon * 180.0 / Math.PI, spotSun, spotFloor));

                foreach (double depth in depths)
                {
                    foreach (double elevDeg in UnderwaterElevationsDeg)
                    {
                        // Глубина замкнутой петлёй. Итерации ВПЛЕТЕНЫ в корутину:
                        // yield return IEnumerator здесь не ждёт (Pump двигает
                        // только внешнюю корутину по одному MoveNext), поэтому
                        // отдельная корутина на постановку молча проскочила бы.
                        double altitudeOffset = -depth;
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            double returned = PlaceAt(runner, body, terrain, spotLat, spotLon, altitudeOffset);
                            yield return null;
                            yield return null;

                            // Диагностика постановки: без неё «глубина не
                            // сходится» выглядит одинаково при трёх разных
                            // причинах — PlaceAt пишет не в то поле, камера
                            // следует не за игроком, или глубину меряет не тот
                            // код. Печатаем радиус игрока над морем, радиус
                            // камеры (её и считает UnderwaterEffect) и уровень
                            // моря из модели рельефа.
                            body.EvaluateWorldState(runner.TimeSeconds, out Vector3d bodyPosDbg, out _);
                            double playerRadius = (runner.PlayerPosition - bodyPosDbg).Magnitude - body.Radius;
                            var cam = Camera.main;
                            double camRadius = cam != null
                                ? (FloatingOrigin.Anchor + AstroFrame.ToAstro(cam.transform.position) - bodyPosDbg).Magnitude - body.Radius
                                : double.NaN;
                            LogLine(string.Format(
                                CultureInfo.InvariantCulture,
                                "    попытка {0}: цель {1:F1} м, offset={2:F1} -> PlaceAt вернул {3:F1}; "
                                + "уровень моря={4:F1}, игрок={5:F1}, камера={6:F1} (радиусы над R), "
                                + "глубина по эффекту={7:F2}, под водой={8}, режим={9}",
                                attempt, depth, altitudeOffset, returned, terrain.GetSeaLevelMeters(),
                                playerRadius, camRadius,
                                effect != null && effect.IsUnderwater ? effect.EyeDepthMeters : 0d,
                                effect != null && effect.IsUnderwater, runner.PlayerMode));

                            if (effect == null || !effect.IsUnderwater)
                            {
                                altitudeOffset -= 1d;
                                continue;
                            }

                            double depthError = effect.EyeDepthMeters - depth;
                            if (Math.Abs(depthError) <= DepthToleranceMeters)
                            {
                                break;
                            }

                            // ПРАВКА ЗНАКА: глубина = -(offset) + EyeHeight, то
                            // есть чем глубже offset, тем мельче камера. Поэтому
                            // «слишком глубоко» (ошибка > 0) требует offset В
                            // БОЛЬШУЮ сторону. Со знаком минус камера на каждой
                            // итерации ныряла всё глубже: 5 → 7 → 11 → 19 → 35 м
                            // вместо 3.
                            altitudeOffset += depthError;
                        }

                        Vector3 look = AimDirection(spotUp, spotNorth, spotSunDir, elevDeg, out string azimuthNote);
                        FirstPersonCamera.DebugLookDirection = look;

                        var wait = new PlanetWait();
                        bool timedOut = false;
                        while (!wait.Step(out timedOut))
                        {
                            yield return null;
                        }

                        double measured = effect != null && effect.IsUnderwater ? effect.EyeDepthMeters : 0d;
                        LogWaterNearCamera(runner, body, depth);
                        string name = string.Format(
                            CultureInfo.InvariantCulture,
                            "underwater_{0}_depth{1:F0}m_elev{2:F0}",
                            window.Name, depth, elevDeg);
                        string path = OutDir + "/" + name + ".png";
                        ScreenCapture.CaptureScreenshot(path);

                        LogLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "  {0}  камера на {1:F2} м  ждали {2:F1} с{3}  азимут {4} -> {5}",
                            name, measured, wait.Elapsed, timedOut ? " ТАЙМАУТ" : "", azimuthNote, path));
                        yield return null;
                        yield return null;
                        yield return null;
                    }
                }
            }

            FirstPersonCamera.DebugLookOverride = false;
            body.Clouds = savedClouds;
            LogLine("подводная съёмка закончена, облака возвращены: "
                + (savedClouds == null ? "выключены" : "профиль тела"));
        }

        /// <summary>
        /// Что видно над камерой в плане ВОДЫ: сколько водных мешей рядом,
        /// включены ли они и где их границы.
        ///
        /// Нужно потому, что на 1 м глубины кадр «строго вверх» выходил
        /// ПУСТЫМ (ни потолка, ни неба — ровный цвет вуали), и гипотез было
        /// две: меша рядом нет (не построен / выключен / отсечён) или он есть,
        /// но находится не там. Этот замер отличает их за один кадр.
        /// </summary>
        private static void LogWaterNearCamera(SimulationRunner runner, OrbitingBody body, double targetDepth)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                LogLine("    вода: нет Main Camera");
                return;
            }

            var camAstro = FloatingOrigin.Anchor + AstroFrame.ToAstro(camera.transform.position);
            int total = 0, enabled = 0, near = 0;
            double closest = double.MaxValue;
            double closestRadiusAboveSea = double.NaN;
            float seaLevel = (float)body.Terrain.GetSeaLevelMeters();

            MeshRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach (MeshRenderer meshRenderer in renderers)
            {
                Material material = meshRenderer.sharedMaterial;
                if (material == null || material.shader == null
                    || material.shader.name != "Galilego/WaterSurface")
                {
                    continue;
                }

                total++;
                if (!meshRenderer.enabled)
                {
                    continue;
                }

                enabled++;

                // Ближайшая точка границы меша к камере — в астрономических
                // координатах, иначе на радиусе 6.4e6 разница теряется в
                // float32 вычитании.
                Vector3d center = FloatingOrigin.Anchor + AstroFrame.ToAstro(meshRenderer.bounds.center);
                double distance = (center - camAstro).Magnitude;
                double radiusSpan = meshRenderer.bounds.extents.magnitude;
                if (distance - radiusSpan < closest)
                {
                    closest = distance - radiusSpan;
                    closestRadiusAboveSea = (center - FloatingOrigin.Anchor).Magnitude;
                }

                if (distance - radiusSpan < 60d)
                {
                    near++;
                }
            }

            LogLine(string.Format(
                CultureInfo.InvariantCulture,
                "    вода над камерой (цель {0:F1} м): мешей {1}, включено {2}, ближе 60 м {3}, "
                + "ближайшая граница {4:F1} м, радиус её центра над R {5:F0} м (уровень моря {6:F0})",
                targetDepth, total, enabled, near,
                closest == double.MaxValue ? -1d : closest,
                closestRadiusAboveSea - (double)seaLevel, seaLevel));
        }

        /// <summary>Окно высоты солнца для съёмки: имя + диапазон в градусах.</summary>
        private struct SolarWindow
        {
            public string Name;
            public double MinElevDeg;
            public double MaxElevDeg;

            public string Describe()
            {
                return "(" + MinElevDeg.ToString("F0", CultureInfo.InvariantCulture) + ".."
                    + MaxElevDeg.ToString("F0", CultureInfo.InvariantCulture) + "°)";
            }
        }

        /// <summary>
        /// Три состояния для сравнения «до/после».
        ///
        /// День — солнце ВЫШЕ 55°, а не выше 25°: окно Снелла имеет
        /// полуугол 48.6°, и при солнце на 25° оно лежит вне окна, то есть
        /// кадр «строго вверх» показывает пустое небо и ничего не проверяет.
        /// Солнце ночью ниже −10°: ниже этого StarVisibility ещё не поднят и
        /// звёзд в кадре нет.
        /// </summary>
        private static readonly SolarWindow[] SolarWindows =
        {
            new SolarWindow { Name = "day", MinElevDeg = 55d, MaxElevDeg = 90d },
            new SolarWindow { Name = "sunset", MinElevDeg = -3d, MaxElevDeg = 6d },
            new SolarWindow { Name = "night", MinElevDeg = -90d, MaxElevDeg = -10d },
        };

        /// <summary>Глубины камеры, м. Полная матрица — 1/3/10 м.</summary>
        private static readonly double[] UnderwaterDepthsMeters = { 1d, 3d, 10d };

        /// <summary>
        /// Углы возвышения взгляда, градусы над местным горизонтом: 90° — строго
        /// вверх (центр окна Снелла), 60° — «30° к поверхности», 0° — горизонт.
        /// </summary>
        private static readonly double[] UnderwaterElevationsDeg = { 90d, 60d, 0d };

        private const int LatitudeSteps = 13;
        private const int LongitudeSteps = 60;
        private const double SearchLatitudeRangeRad = 75d * Math.PI / 180d;
        private const double DepthToleranceMeters = 0.2d;
        private const int StableFramesForShot = 4;

        /// <summary>
        /// Готовность планеты к снимку — пошаговая проверка, а не корутина:
        /// движок инструмента делает ровно один MoveNext за тик редактора,
        /// поэтому вложенная корутина (yield return IEnumerator) там просто
        /// проскочила бы, не выполнившись.
        ///
        /// Считаем по покрытию кэша, а не по ChunksFinalized: тот растёт
        /// ТОЛЬКО в async-ветке, а первый кадр строит все desired узлы
        /// СИНХРОННО (firstFrameGuard, PlanetSurfaceRenderer.cs:1527), поэтому на
        /// готовой планете ChunksFinalized структурно 0. Прежний гейт на
        /// нём ждал 60 с и объявлял «незагруженную планету» на полностью
        /// загруженной (см. surface-capture.log, все кадры TIMEOUT).
        ///
        /// Требуем несколько кадров подряд, иначе ловим переходный кадр между
        /// телепортом и первой постройкой.
        /// </summary>
        private struct PlanetWait
        {
            private float elapsed;
            private int stable;
            private float nextLog;

            public float Elapsed => elapsed;

            /// <summary>Один кадр ожидания. true — снимать можно.</summary>
            public bool Step(out bool timedOut)
            {
                timedOut = false;

                bool ready = SurfacePerf.CachedChunks > 0
                    && SurfacePerf.DesiredNodes > 0
                    && SurfacePerf.QueuedBuilds == 0
                    && SurfacePerf.InFlightBuilds == 0
                    && SurfacePerf.DesiredInKeep == SurfacePerf.VisibleChunks
                    && SurfacePerf.DesiredInKeep > 0;

                stable = ready ? stable + 1 : 0;

                if (elapsed >= nextLog)
                {
                    nextLog = elapsed + 1f;
                    LogLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  ждём планету {0:F1} с: desired={1} cached={2} queued={3} inFlight={4} "
                        + "visible={5} keep={6} финализировано={7} ошибок={8} отброшено={9} focused={10}",
                        elapsed, SurfacePerf.DesiredNodes, SurfacePerf.CachedChunks,
                        SurfacePerf.QueuedBuilds, SurfacePerf.InFlightBuilds,
                        SurfacePerf.VisibleChunks, SurfacePerf.DesiredInKeep,
                        SurfacePerf.ChunksFinalized, SurfacePerf.FinalizeErrors,
                        SurfacePerf.QueueDropped, Application.isFocused));
                }

                if (stable >= StableFramesForShot)
                {
                    return true;
                }

                if (elapsed > (float)QueueTimeoutSeconds)
                {
                    LogLine("  TIMEOUT: снимок на неготовой планете, реальные счётчики выше");
                    timedOut = true;
                    return true;
                }

                elapsed += Time.unscaledDeltaTime;
                return false;
            }
        }

        /// <summary>
        /// Направление взгляда по азимуту и углу возвышения. Азимут берём на
        /// СОЛНЦЕ, когда оно над горизонтом: только так закатное окно состоит из
        /// солнца и ореола, а не из пустого градиента, и только так кадр
        /// показывает, что окно искажает именно источник. Ночью солнца нет —
        /// берём север, иначе направление было бы вырожденным.
        ///
        /// Направление на солнце приходит извне (TryLocalFrame), а не
        /// восстанавливается из высоты солнца: той для «есть ли солнце в кадре»
        /// хватает, а азимут должен быть задан явно, иначе кадр неповторим.
        /// </summary>
        private static Vector3 AimDirection(Vector3 up, Vector3 north, Vector3 sunDir,
            double elevationDeg, out string note)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(sunDir, up);
            string what;
            if (horizontal.sqrMagnitude > 1e-6f && Vector3.Dot(sunDir, up) > -0.05f)
            {
                horizontal.Normalize();
                what = "на солнце";
            }
            else
            {
                horizontal = north.normalized;
                what = "на север (солнце под горизонтом)";
            }

            double e = elevationDeg * Math.PI / 180d;
            Vector3 dir = ((horizontal * (float)Math.Cos(e)) + (up * (float)Math.Sin(e))).normalized;
            note = what;
            return dir;
        }

        /// <summary>
        /// Локальная рамка точки на поверхности в мировых (simulation) векторах:
        /// локальная вверх, касательная на север и направление на солнце. Высота
        /// солнца считается здесь же — из направления на звезду и локальной вверх.
        /// </summary>
        private static bool TryLocalFrame(SimulationRunner runner, OrbitingBody body,
            double latRad, double lonRad, double altitudeMeters,
            out Vector3 simUp, out Vector3 simNorth, out Vector3 simSunDir, out double sunElevationDeg)
        {
            simUp = Vector3.up;
            simNorth = Vector3.forward;
            simSunDir = Vector3.up;
            sunElevationDeg = 0d;

            if (runner.SystemState == null || runner.SystemState.Root == null)
            {
                return false;
            }

            double t = runner.TimeSeconds;
            double latDeg = latRad * 180.0 / Math.PI;
            double lonDeg = lonRad * 180.0 / Math.PI;

            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            body.GetSurfaceState(latDeg, lonDeg, Math.Max(0d, altitudeMeters), t, out Vector3d world, out _);
            Vector3d up = world - bodyPos;
            if (up.Magnitude < 1e-6)
            {
                return false;
            }

            up = up.Normalized;

            Vector3d pole = body.GetVisualOrientation(t).Rotate(body.NorthPoleDirection);
            Vector3d north = pole - (up * Vector3d.Dot(pole, up));
            if (north.Magnitude < 1e-9)
            {
                return false;
            }

            runner.SystemState.Root.EvaluateWorldState(t, out Vector3d starPos, out _);
            Vector3d toStar = starPos - world;
            double starDist = toStar.Magnitude;
            if (starDist <= 0d)
            {
                return false;
            }

            double sinEl = Vector3d.Dot(toStar / starDist, up);
            sinEl = Math.Max(-1d, Math.Min(1d, sinEl));
            sunElevationDeg = Math.Asin(sinEl) * 180.0 / Math.PI;

            simUp = AstroFrame.ToSimulation(up);
            simNorth = AstroFrame.ToSimulation(north.Normalized);
            simSunDir = AstroFrame.ToSimulation((toStar / starDist));
            return true;
        }

        // ------------------------------------------------------------ утилиты


        private static SimulationRunner Runner()
        {
            return UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
        }

        private static OrbitingBody FindBody(SimulationRunner runner, string name)
        {
            if (runner.SystemState == null)
            {
                return null;
            }

            foreach (OrbitingBody b in runner.SystemState.AllBodies)
            {
                if (b.Name == name)
                {
                    return b;
                }
            }

            return null;
        }

        private static PlanetSurfaceRenderer FindRenderer()
        {
            return UnityEngine.Object.FindAnyObjectByType<PlanetSurfaceRenderer>();
        }

        /// <summary>
        /// Включает ноклип через чит-меню и замораживает намерение.
        ///
        /// Ноклип здесь обязателен, а не удобством: сцена работает в режиме
        /// только-игрока, корабля (SimulationRunner.Ship) нет, поэтому
        /// TeleportTo и SpawnPlayerOnSurface непригодны - обе требуют Ship.
        /// Без ноклипа игрок сразу разгоняется гравитацией (замер показал
        /// 5304 м/с) и улетает с цели, а очередь чанков не опустошается
        /// никогда, потому что камера всё время в движении.
        /// </summary>
        private static bool EnableNoclip(SimulationRunner runner)
        {
            if (!runner.NoclipActive)
            {
                var cheats = Resources.FindObjectsOfTypeAll<CheatMenu>();
                if (cheats.Length > 0)
                {
                    cheats[0].RequestNoclip(true);
                }
                else
                {
                    runner.SetNoclip(true, new Vector3d(0d, 0d, 1d));
                }
            }

            runner.PlayerIntent.NoclipDirection = Vector3d.Zero;
            runner.PlayerIntent.NoclipSpeed = 0d;
            LogLine("noclip=" + runner.NoclipActive + " mode=" + runner.PlayerMode
                + " ship=" + (runner.Ship != null));
            return runner.NoclipActive;
        }

        private static System.Reflection.FieldInfo _posField;
        private static System.Reflection.FieldInfo _velField;

        /// <summary>
        /// Ставит ИГРОКА в точку lat/lon на заданной высоте над морем.
        ///
        /// Время берётся ТЕКУЩЕЕ симуляционное, а не 0: планета поворачивается,
        /// и при t=0 игрок оказывался не там, где считает симуляция, - из-за
        /// этого часть кадров уезжала под рельеф (высота 0 м, чёрный низ).
        ///
        /// Позиция ставится рефлексией по backing-полям автосвойств
        /// PlayerPosition/PlayerVelocity: сеттеры приватные, а все публичные
        /// способы переноса завязаны на Ship, которого в сцене нет. Рефлексия -
        /// осознанный компромисс ИНСТРУМЕНТА, в продуктовый код не попадает.
        ///
        /// Возвращает реальную высоту игрока над рельефом, чтобы вызывающий
        /// мог отбросить кадр, если игрок ушёл под землю.
        /// </summary>
        private static double PlaceAt(SimulationRunner runner, OrbitingBody body, HeightfieldTerrain terrain,
            double latRad, double lonRad, double altitudeMeters)
        {
            if (_posField == null || _velField == null)
            {
                foreach (System.Reflection.FieldInfo f in typeof(SimulationRunner).GetFields(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                {
                    if (f.Name.Contains("PlayerPosition"))
                    {
                        _posField = f;
                    }
                    else if (f.Name.Contains("PlayerVelocity"))
                    {
                        _velField = f;
                    }
                }
            }

            if (_posField == null || _velField == null)
            {
                LogLine("FATAL: backing fields PlayerPosition/PlayerVelocity not found - tool aborts");
                throw new InvalidOperationException("PlayerPosition backing field not found");
            }

            double t = runner.TimeSeconds;
            double latDeg = latRad * 180.0 / Math.PI;
            double lonDeg = lonRad * 180.0 / Math.PI;
            double h = terrain == null ? 0d : terrain.GetHeightMeters(body, latRad, lonRad);
            double target = Math.Max(0d, h) + altitudeMeters;
            body.GetSurfaceState(latDeg, lonDeg, target, t, out Vector3d world, out Vector3d vel);
            _posField.SetValue(runner, world);
            _velField.SetValue(runner, vel);
            FloatingOrigin.Anchor = world;

            // Реальная высота над рельефом в той же точке, что и камера.
            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            Vector3d up = (world - bodyPos).Normalized;
            double actual = (world - bodyPos).Magnitude - body.Radius;
            return actual;
        }

        /// <summary>Допуск высоты: цель и факт должны совпадать, иначе кадр брак.</summary>
        private const double AltitudeToleranceMeters = 0.75;


        /// <summary>
        /// Ждёт опустошения очереди чанков. Возвращает true, если очередь
        /// действительно опустела, и время в секундах — иначе false и 60 с.
        /// Таймаут пишется в лог, а не молча пропускается: иначе снимок
        /// делается на недогруженной планете и выглядит как ошибка шума.
        /// </summary>
        private static IEnumerator WaitForRunner()
        {
            float since = 0f;
            while (since < 30f && Runner() == null)
            {
                since += Time.unscaledDeltaTime;
                yield return null;
            }

            yield break;
        }

        private static void LogLine(string s)
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "[{0:HH:mm:ss}] {1}",
                DateTime.Now, s);
            Log.AppendLine(line);
            Debug.Log("[SurfaceCapture] " + s);
            FlushLog();
        }

        private static void FlushLog()
        {
            try
            {
                File.WriteAllText(LogPath, Log.ToString());
            }
            catch (IOException)
            {
                // лог недоступен — не роняем съёмку из-за файла
            }
        }
    }
}
#endif
