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
    /// Surface capture and measurements. A separate Editor script rather than an
    /// eval call: in Play the main thread is busy building chunks, and any
    /// one-line eval hits the pipeline's 5-second limit. A coroutine waits frame
    /// by frame, so that per-operation limit does not apply.
    ///
    /// All menu items work the same way: wait for the chunk queue to drain, take
    /// a frame, write a PNG. The queue is empty when SurfacePerf.QueuedBuilds == 0
    /// and InFlightBuilds == 0.
    /// </summary>
    public static class SurfaceCaptureTool
    {
        private const string OutDir = "Assets/_Project/Screenshots/dampmodes";
        private const string LogPath = "Logs/surface-capture.log";
        private const double QueueTimeoutSeconds = 60d;

        /// <summary>
        /// Capture positions. Fixed in code so that mode comparisons use identical
        /// frames. Altitudes: orbit, 30 km, 2 km, 200 m.
        /// </summary>
        private static readonly double[] AltitudesMeters = { 400000d, 30000d, 2000d, 200d };

        private static readonly StringBuilder Log = new StringBuilder();

        // ---------------------------------------------------------------- refs

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

            // The project enters Play with runInBackground = 0: losing editor
            // focus stops the game loop, which would leave the chunk queue stuck
            // and produce empty PNGs, so background running is forced for the
            // capture and restored on exit from Play.
            _savedRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            if (!Application.runInBackground)
            {
                // Did not take effect: fall back to the project setting, which
                // is also restored on exit from Play.
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
        /// Resets the play-mode settings forced by Start if the editor was
        /// reloaded before they could be restored on exit from Play.
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
                // The original value is gone after the editor reload, so the flag
                // is just reset and a warning logged for manual checking.
                EditorSettings.enterPlayModeOptionsEnabled = false;
                Debug.LogWarning("[SurfaceCapture] аварийно сброшены enterPlayModeOptions: DisableDomainReload был включён инструментом и снят при перезагрузке редактора. Проверьте Project Settings > Editor > Enter Play Mode Settings.");
                SessionState.EraseBool("SurfaceCapture.DomainReloadDisabled");
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                // Re-subscribe: Unity may recreate the delegates even with
                // domain reload disabled.
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
                    // Restore the EXACT saved value, not EnterPlayModeOptions.None:
                    // zeroing would silently drop other flags such as
                    // DisableSceneReload and change Play behavior editor-wide.
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

        // ------------------------------------------------------- water skirt

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

            // The coast search spans frames: GetHeightMeters is expensive in the
            // editor, and scanning the whole grid in one frame blows the frame
            // budget, so the grid is walked in portions.
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

                    // The wait is inlined rather than a nested coroutine: the
                    // driver steps only the outer coroutine, so a nested
                    // yield return IEnumerator would be skipped and the shot
                    // taken on an unloaded planet.
                    float drained = 0f;
                    bool ok = false;
                    int finalized = 0;
                    while (true)
                    {
                        // ChunksFinalized is a per-frame counter. Without requiring
                        // finalized > 0 the check passes trivially right after a
                        // teleport, when the queue is still empty.
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

                    // Re-place right before the shot: the planet has rotated while
                    // the queue was draining, so the player left the target point;
                    // place again using the current time.
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

        // -------------------------------------------------- damp modes

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

            // HeightfieldTerrain is copied from the profile at initialization, so
            // both the asset profile AND the runtime terrain model must be changed.
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

                        // See RunWaterSkirt: the wait is inlined, a nested coroutine
                        // would not be stepped.
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

        // ------------------------------------------------------------- flight

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
                    // Teleport: this is where the peak queue lands.
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

                    // ChunksFinalized/ChunksBuilt are per-frame counters reset by
                    // SurfacePerf.BeginFrame, so accumulate them manually.
                    // QueuedBuilds/InFlightBuilds are gauges, read as-is.
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

        // ------------------------------------------------- underwater ceiling

        /// <summary>
        /// Shoots the water ceiling from below: Snell window, ripples, thickness.
        ///
        /// Depth is reached in a closed loop on EyeDepthMeters, not by guessing
        /// EyeHeightMeters: PlaceAt puts the player at max(0, terrain height) plus
        /// altitude, while the camera sits 2 m above the target and
        /// UnderwaterEffect measures depth at the camera.
        ///
        /// Time of day is chosen by longitude (local solar time), not by warping time.
        ///
        /// Planet readiness is gated on cache coverage rather than ChunksFinalized
        /// (that counter only grows in the async branch): CachedChunks &gt; 0, empty
        /// queue and in-flight counters, DesiredInKeep == VisibleChunks, held for
        /// several frames.
        ///
        /// While waiting, the real counters and Application.isFocused are logged
        /// once per second.
        /// </summary>
        private static IEnumerator RunUnderwaterCeiling(bool fullMatrix)
        {
            // The runner wait is inlined: Pump does exactly one MoveNext per
            // tick, so a nested coroutine (yield return WaitForRunner()) would
            // never run.
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

            // Terra's clouds are off in the scene (body.Clouds == null), and the
            // clouds are what make the Snell window refraction readable: without
            // them the window shows only a gradient. A profile is enabled for the
            // capture and restored afterwards, because it is body state, not a
            // tool setting.
            //
            // CloudProfile is a plain class, not a ScriptableObject, so it cannot
            // be loaded via AssetDatabase: construct one with project defaults and
            // raise the coverage.
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
                // --- ocean point with the needed sun elevation --------------------
                // Search a latitude/longitude GRID, not a single latitude: the
                // planet is not all ocean, and a longitude-only scan stalls where
                // there is no ocean. Rejected samples are counted and logged so
                // that a "NOT FOUND" states its reason.
                bool found = false;
                double spotLat = 0d, spotLon = 0d, spotSun = 0d, spotFloor = 0d;
                Vector3 spotUp = Vector3.up, spotNorth = Vector3.forward, spotSunDir = Vector3.up;

                int sampled = 0, tooShallow = 0, noFrame = 0, wrongSun = 0;
                double deepest = 0d;
                double sunMin = 999d, sunMax = -999d;

                double requiredFloor = -Math.Max(deepestDepth, 5d);
                for (int latIndex = 1; latIndex < LatitudeSteps - 1 && !found; latIndex++)
                {
                    // Latitudes ±75°, not the full range to the poles: near a pole
                    // the "north" tangent is degenerate (TryLocalFrame rejects it)
                    // and a composition under the pole is meaningless. The minus
                    // sign matters: the sweep must run from -75° towards +75°.
                    double latRad = -SearchLatitudeRangeRad
                        + ((2.0 * SearchLatitudeRangeRad * latIndex) / (LatitudeSteps - 1));

                    for (int i = 0; i < LongitudeSteps && !found; i++)
                    {
                        double lonRad = -Math.PI + ((2.0 * Math.PI * i) / LongitudeSteps);

                        // DEPTH comes from GetRawHeightMeters, not GetHeightMeters:
                        // the latter clamps the ocean to sea level (TerrainModel.cs),
                        // so every ocean point would read as exactly 0 m.
                        double raw = terrain.GetRawHeightMeters(body, latRad, lonRad);
                        sampled++;
                        if (raw < deepest)
                        {
                            deepest = raw;
                        }

                        // Need ocean deeper than the deepest shot depth: otherwise
                        // the camera sits on the seabed and the shot is about the
                        // floor, not the ceiling.
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
                        // Depth via a closed loop. Iterations are inlined because
                        // Pump steps only the outer coroutine, so a nested
                        // yield return IEnumerator would silently be skipped.
                        double altitudeOffset = -depth;
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            double returned = PlaceAt(runner, body, terrain, spotLat, spotLon, altitudeOffset);
                            yield return null;
                            yield return null;

                            // Placement diagnostics: logs the player radius above
                            // sea, the camera radius (what UnderwaterEffect measures)
                            // and the sea level from the terrain model.
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

                            // depth = -offset + EyeHeight, so a larger offset means a
                            // shallower camera: a positive error (too deep) requires
                            // increasing the offset.
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
        /// What is above the camera in the water plane: how many water meshes are
        /// nearby, whether they are enabled, and where their bounds are.
        /// Distinguishes "no mesh nearby" (not built / disabled / culled) from
        /// "mesh exists but is elsewhere".
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

                // Nearest mesh bound point to the camera, in astronomical
                // coordinates: at radius 6.4e6 the difference is lost in
                // float32 subtraction.
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

        /// <summary>Sun elevation window for a capture: name plus range in degrees.</summary>
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
        /// Three states for before/after comparison.
        ///
        /// Day requires the sun above 55°: the Snell window half-angle is 48.6°,
        /// so at 25° it lies outside the window and a straight-up shot shows
        /// empty sky. Night requires the sun below −10°: below that StarVisibility
        /// is not raised yet and there are no stars in the frame.
        /// </summary>
        private static readonly SolarWindow[] SolarWindows =
        {
            new SolarWindow { Name = "day", MinElevDeg = 55d, MaxElevDeg = 90d },
            new SolarWindow { Name = "sunset", MinElevDeg = -3d, MaxElevDeg = 6d },
            new SolarWindow { Name = "night", MinElevDeg = -90d, MaxElevDeg = -10d },
        };

        /// <summary>Camera depths, m. Full matrix is 1/3/10 m.</summary>
        private static readonly double[] UnderwaterDepthsMeters = { 1d, 3d, 10d };

        /// <summary>
        /// Look elevation angles, degrees above the local horizon: 90° is straight
        /// up (centre of the Snell window), 60° is 30° to the surface, 0° is the horizon.
        /// </summary>
        private static readonly double[] UnderwaterElevationsDeg = { 90d, 60d, 0d };

        private const int LatitudeSteps = 13;
        private const int LongitudeSteps = 60;
        private const double SearchLatitudeRangeRad = 75d * Math.PI / 180d;
        private const double DepthToleranceMeters = 0.2d;
        private const int StableFramesForShot = 4;

        /// <summary>
        /// Planet readiness as a step-wise check, not a coroutine: the tool's
        /// driver does exactly one MoveNext per editor tick, so a nested
        /// coroutine (yield return IEnumerator) would be skipped entirely.
        ///
        /// Measured by cache coverage, not ChunksFinalized: that counter only
        /// grows in the async branch, while the first frame builds every desired
        /// node synchronously (firstFrameGuard), so on a loaded planet it is
        /// structurally 0.
        ///
        /// Several consecutive frames are required, otherwise the transient frame
        /// between teleport and first build would pass.
        /// </summary>
        private struct PlanetWait
        {
            private float elapsed;
            private int stable;
            private float nextLog;

            public float Elapsed => elapsed;

            /// <summary>One frame of waiting. true means a shot may be taken.</summary>
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
        /// Look direction from azimuth and elevation. The azimuth points at the
        /// SUN when it is above the horizon: that is what makes the sunset window
        /// show the sun and its halo rather than an empty gradient, so the shot
        /// proves the window distorts the source. At night the azimuth is north,
        /// otherwise it would be degenerate.
        ///
        /// The sun direction comes from outside (TryLocalFrame) so the azimuth is
        /// explicit and the shot is repeatable.
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
        /// Local frame of a surface point in world (simulation) vectors: local up,
        /// north tangent and the direction to the sun. The sun elevation is computed
        /// here from the star direction and local up.
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

        // ------------------------------------------------------------ utils


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
        /// Enables noclip via the cheat menu and freezes the intent.
        ///
        /// Noclip is required, not a convenience: the scene runs player-only with
        /// no ship (SimulationRunner.Ship), so TeleportTo and SpawnPlayerOnSurface
        /// are unusable. Without noclip gravity accelerates the player away from
        /// the target and the chunk queue would never drain.
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
        /// Places the PLAYER at a lat/lon point at the given altitude above sea.
        ///
        /// The CURRENT simulation time is used, not 0: the planet rotates, so at
        /// t=0 the player would not land where the simulation expects.
        ///
        /// Position is set by reflection on the PlayerPosition/PlayerVelocity
        /// auto-property backing fields: the setters are private and every public
        /// transfer path requires a ship, which the scene has none of. This
        /// reflection is a deliberate tool-only compromise.
        ///
        /// Returns the actual player altitude above the terrain so the caller can
        /// discard a frame if the player ended up underground.
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

            // Actual altitude above the terrain at the same point as the camera.
            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            Vector3d up = (world - bodyPos).Normalized;
            double actual = (world - bodyPos).Magnitude - body.Radius;
            return actual;
        }

        /// <summary>Altitude tolerance: target and actual must match or the frame is discarded.</summary>
        private const double AltitudeToleranceMeters = 0.75;


        /// <summary>
        /// Waits up to 30 s for a SimulationRunner to appear in the scene.
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
                // Log unavailable: do not fail the capture over the file.
            }
        }
    }
}
#endif
