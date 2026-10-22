using System;
using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Cheat menu (Alt+F10) and the debug coordinate HUD.
    ///
    /// The window is IMGUI (OnGUI), as already used in PlayerController: the project
    /// has no separate Canvas/UXML, and a debug menu should not pull assets. While
    /// the window is open, player input goes to the menu
    /// (PlayerController.InputBlocked) and the cursor is released — buttons cannot
    /// be clicked under a captured camera.
    ///
    /// Noclip: speed slider from 0.1 to 1e9 m/s (log scale); the high end is needed
    /// to cross a planet or leave its vicinity in a single frame. Direction is full
    /// 3D camera-relative (WASD + Space/Ctrl), read by PlayerController; speed is
    /// owned here (SimulationRunner.NoclipActive is the single switch — the window
    /// only toggles it).
    ///
    /// Coordinates: lat/lon of the body under the player (dominant by SOI), height
    /// above sea level (distance to center minus Radius+SeaLevel) and height above
    /// terrain. Printed as decimal degrees with 6 digits and as DMS.
    /// </summary>
    public sealed class CheatMenu : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены. Пусто — найдётся сам при старте.")]
        public SimulationRunner Runner;

        [Tooltip("PlayerController сцены. Пусто — найдётся сам при старте.")]
        public PlayerController Controller;

        [Tooltip("Камера от первого лица: её захват курсора отдаётся меню. Пусто — найдётся сама.")]
        public FirstPersonCamera View;

        [Tooltip("Бенчмарк-полёт для замера fps на скорости. Пусто — найдётся сам.")]
        public FlightBenchmark Benchmark;

        [Tooltip("Показывать окно сразу при старте (для отладки и скриншотов).")]
        public bool OpenOnStart;

        [Tooltip("Требовать Alt на горячей клавише открытия/закрытия.")]
        public bool RequireAlt = true;

        [Header("Диагностика")]
        [Tooltip("Показывать счётчики рендера поверхности (чанки, GC, меши, декор) в HUD. " +
                 "Сама строка собирается только пока флаг включён — в выключенном виде " +
                 "дополнительной managed-аллокации в кадре нет.")]
        public bool ShowPerformance = true;

        [Header("Ноуклип")]
        [Tooltip("Минимальная скорость ноуклипа (м/с) — левый край слайдера.")]
        public double MinNoclipSpeed = 0.1d;

        [Tooltip("Максимальная скорость ноуклипа (м/с) — правый край слайдера.")]
        public double MaxNoclipSpeed = 1e9d;

        [Tooltip("Скорость ноуклипа, м/с (в окне показывается и правится слайдером).")]
        public double NoclipSpeed = 50d;

        [Header("Ноуклип под управлением бенчмарка")]
        [Tooltip("Пока true, меню не трогает ни включение ноуклипа, ни его скорость: " +
                 "ими управляет FlightBenchmark. Иначе Update меню каждый кадр сверяет " +
                 "своё намерение с Runner.NoclipActive и выключает ноуклип, который " +
                 "только что включил бенчмарк, а ползунок в окне затирает " +
                 "выставленную им скорость.")]
        public bool NoclipExternallyDriven;

        private const int WindowId = 0xC4EA7;

        /// <summary>Window size: fits all content without scrolling, with some slack.</summary>
        private const float WindowWidth = 520f;
        private const float WindowHeight = 420f;

        private const float HudWidth = 540f;
        private const float HudHeight = 108f;
        private const float HudMargin = 12f;
        private const float PerfHudWidth = 540f;
        private const float PerfHudHeight = 84f;
        private const float PerfHudY = HudMargin + HudHeight + 4f;

        private bool isOpen;

        /// <summary>The cheat window is open (the settings menu does not open over it).</summary>
        public static bool AnyOpen { get; private set; }

        private bool noclipRequested;
        private bool showCoordinates;
        private double noclipSpeedLog;
        private Rect windowRect = new Rect(WindowWidth + 48f, 24f, WindowWidth, WindowHeight);
        private string coordinatesText = string.Empty;
        private string statusText = string.Empty;
        private bool cursorClaimed;
        private double nextNoclipLogTime = -1e9d;
        private Vector3d lastLoggedPosition;
        private bool hasLoggedPosition;

        private void Start()
        {
            ResolveReferences();
            SetOpen(OpenOnStart);
            noclipRequested = Runner != null && Runner.NoclipActive;
            noclipSpeedLog = ToLogSlider(NoclipSpeed);
        }

        private void OnDisable()
        {
            // A disabled/destroyed menu must not keep the cursor claimed.
            FirstPersonCamera.SetCursorClaim(ref cursorClaimed, false, "CheatMenu");
        }

        private void Update()
        {
            ResolveReferences();
            if (Runner == null)
            {
                return;
            }

            if (PlayerInput.CheatMenuDown(RequireAlt))
            {
                SetOpen(!isOpen);
            }

            if (!NoclipExternallyDriven && noclipRequested != Runner.NoclipActive)
            {
                Runner.SetNoclip(noclipRequested, CameraExitDirection());
            }

            LogNoclipTick();
        }

        /// <summary>
        /// Noclip diagnostics: once per 2 s of sim time, while noclip is on, logs the
        /// intent direction (aggregate of held keys), speed and actual displacement.
        /// Speed is FRAME-RELATIVE: inertial speed while running on the surface equals
        /// the planet's orbital velocity (~30 km/s on Terra) and would read as flying
        /// to space at 30 km/s while standing still. Format matches LogSurfacePosition
        /// in SimulationRunner.
        /// </summary>
        private void LogNoclipTick()
        {
            if (!Runner.NoclipActive)
            {
                hasLoggedPosition = false;
                return;
            }

            if (Runner.TimeSeconds < nextNoclipLogTime)
            {
                return;
            }

            nextNoclipLogTime = Runner.TimeSeconds + 2d;
            Vector3d delta = hasLoggedPosition ? Runner.PlayerPosition - lastLoggedPosition : Vector3d.Zero;
            lastLoggedPosition = Runner.PlayerPosition;
            hasLoggedPosition = true;
            Camera camera = Camera.main;
            Debug.Log(string.Format(
                "[Noclip] t={0:F0} сдвиг={1:F0} м скорость={2:F1} (отн. кадра) интентНапр={3} интентСкорость={4:F1} camFwd={5} режим={6}",
                Runner.TimeSeconds,
                delta.Magnitude,
                Runner.PlayerFrameRelativeSpeed(Runner.TimeSeconds),
                Runner.PlayerIntent.NoclipDirection,
                Runner.PlayerIntent.NoclipSpeed,
                camera != null ? camera.transform.forward.ToString("F2") : "нет камеры",
                Runner.PlayerMode));
        }

        private void OnGUI()
        {
            if (Runner == null)
            {
                return;
            }

            CheatGuiSkin.Build();
            UpdateReadout();
            if (showCoordinates)
            {
                Rect hud = new Rect(HudMargin, HudMargin, HudWidth, HudHeight);
                GUI.Label(hud, coordinatesText + "\n" + statusText, CheatGuiSkin.Readout);
            }

            if (ShowPerformance)
            {
                // Surface render counters as the second block under the coordinates.
                // SurfacePerf builds its line itself, only while the flag is on.
                GUI.Label(new Rect(HudMargin, PerfHudY, PerfHudWidth, PerfHudHeight), SurfacePerf.HudLine(), CheatGuiSkin.Readout);
            }

            if (isOpen)
            {
                // The title is drawn manually (Layout with an empty title): the built-in
                // one renders above the edge and overlaps the panel. Window size is
                // fixed; only the position is taken from the drawn rect.
                Rect drawn = GUILayout.Window(WindowId, windowRect, DrawWindow, string.Empty, CheatGuiSkin.WindowTitle);
                windowRect = new Rect(drawn.x, drawn.y, WindowWidth, WindowHeight);
            }
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label(RequireAlt ? "ЧИТЫ   (Alt+F10)" : "ЧИТЫ   (F10)", CheatGuiSkin.Header);
            if (NoclipExternallyDriven)
            {
                GUILayout.Label("  Ноуклип ведёт бенчмарк (см. блок ниже)", CheatGuiSkin.LabelMuted);
                GUILayout.Label("Скорость полёта: " + FormatSpeed(NoclipSpeed) + " м/с", CheatGuiSkin.LabelMuted);
            }
            else
            {
                noclipRequested = GUILayout.Toggle(noclipRequested, "  Ноуклип включён", CheatGuiSkin.Toggle);
                GUILayout.Label("Скорость полёта", CheatGuiSkin.LabelMuted);
                GUILayout.Label(FormatSpeed(NoclipSpeed) + " м/с", CheatGuiSkin.Label);
                noclipSpeedLog = GUILayout.HorizontalSlider(
                    (float)noclipSpeedLog,
                    (float)LogMin,
                    (float)LogMax,
                    CheatGuiSkin.SliderTrack,
                    CheatGuiSkin.SliderThumb);
                NoclipSpeed = FromLogSlider(noclipSpeedLog);
            }

            GUILayout.Space(12f);
            GUILayout.Label("КООРДИНАТЫ", CheatGuiSkin.Header);
            showCoordinates = GUILayout.Toggle(showCoordinates, "  Показывать координаты", CheatGuiSkin.Toggle);
            GUILayout.Label(coordinatesText, CheatGuiSkin.Readout);
            GUILayout.Label(statusText, CheatGuiSkin.LabelMuted);

            GUILayout.Space(12f);
            GUILayout.Label("ДИАГНОСТИКА", CheatGuiSkin.Header);
            ShowPerformance = GUILayout.Toggle(ShowPerformance, "  Показывать счётчики поверхности", CheatGuiSkin.Toggle);
            GUILayout.Label(SurfacePerf.HudLine(), CheatGuiSkin.Readout);

            GUILayout.Space(12f);
            GUILayout.Label("БЕНЧМАРК-ПОЛЁТ", CheatGuiSkin.Header);
            if (Benchmark == null)
            {
                GUILayout.Label("  Компонент FlightBenchmark в сцене не найден.", CheatGuiSkin.LabelMuted);
            }
            else if (Benchmark.Running)
            {
                if (GUILayout.Button("  Остановить бенчмарк", CheatGuiSkin.Button))
                {
                    Benchmark.StopBenchmark();
                }

                GUILayout.Label(Benchmark.StatusLine, CheatGuiSkin.Readout);
            }
            else
            {
                if (GUILayout.Button("  Запустить бенчмарк (200/500/1000/2000 м/с, 30 с)", CheatGuiSkin.Button))
                {
                    Benchmark.Menu = this;
                    Benchmark.StartBenchmark();
                }

                GUILayout.Label(
                    "Ноуклип включится сам и вернётся в исходное состояние. " +
                    "CSV: <persistentDataPath>/benchmark.csv",
                    CheatGuiSkin.LabelMuted);
            }

            GUI.DragWindow(new Rect(0f, 0f, WindowWidth, 30f));
        }

        /// <summary>
        /// Enables/disables noclip externally (benchmark). The usual path is the toggle
        /// in the window; this one applies the same switch without a UI click and
        /// without the menu's Update rolling it back.
        /// </summary>
        public void RequestNoclip(bool active)
        {
            noclipRequested = active;
            if (Runner != null)
            {
                Runner.SetNoclip(active, CameraExitDirection());
            }
        }

        private void SetOpen(bool open)
        {
            isOpen = open;
            AnyOpen = open;
            if (Controller != null)
            {
                Controller.InputBlocked = open;
            }

            // With a captured cursor the buttons cannot be clicked, and the window
            // blocks the view: while open the menu CLAIMS the cursor (the camera stops
            // capturing it) and on close gives it back — the camera re-captures it by
            // itself. Claims are counted, so a window opened over another one cannot
            // leave the cursor released (see FirstPersonCamera.SetCursorClaim).
            if (open)
            {
                FirstPersonCamera.SetCursorClaim(ref cursorClaimed, true, "CheatMenu");
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            FirstPersonCamera.SetCursorClaim(ref cursorClaimed, false, "CheatMenu");
        }

        private void ResolveReferences()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (Controller == null)
            {
                Controller = FindAnyObjectByType<PlayerController>();
            }

            if (View == null)
            {
                View = FindAnyObjectByType<FirstPersonCamera>();
            }

            if (Benchmark == null)
            {
                Benchmark = FindAnyObjectByType<FlightBenchmark>();
            }
        }

        /// <summary>Where to drop the player out of the ship when noclip turns on — along the camera's forward.</summary>
        private Vector3d CameraExitDirection()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return Vector3d.Zero;
            }

            return AstroFrame.ToAstro(camera.transform.forward);
        }

        private double LogMin => Math.Log10(Math.Max(MinNoclipSpeed, 1e-9d));

        private double LogMax => Math.Log10(Math.Max(MaxNoclipSpeed, MinNoclipSpeed * 10d));

        private double ToLogSlider(double speed)
        {
            // Lower bound is 1e-9, not MinNoclipSpeed: a zero in the inspector would
            // make log10 return −inf and the slider would push NaN into the speed.
            return Math.Log10(Math.Max(1e-9d, Math.Min(MaxNoclipSpeed, speed)));
        }

        private double FromLogSlider(double log)
        {
            return Math.Pow(10d, Math.Min(LogMax, Math.Max(LogMin, log)));
        }

        /// <summary>
        /// Player coordinates: the body underneath (dominant by SOI), lat/lon, height
        /// above sea level and terrain, speed and mode. Computed every frame — the
        /// terrain sample is cheap and the numbers should stay live.
        /// </summary>
        private void UpdateReadout()
        {
            OrbitingBody body = Runner.DominantBody;
            if (body == null)
            {
                coordinatesText = "Нет данных: тело не найдено";
                statusText = string.Empty;
                return;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPosition, out _);
            body.SurfaceLatLonAt(Runner.PlayerPosition, Runner.TimeSeconds, out double latDeg, out double lonDeg);
            double distance = (Runner.PlayerPosition - bodyPosition).Magnitude;
            double seaLevel = WaterQuery.TryGetSeaLevel(body, out double sea) ? sea : 0d;
            double terrainHeight = body.Terrain != null
                ? body.Terrain.GetHeightMeters(body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d))
                : 0d;

            coordinatesText = string.Format(
                "{0}: lat {1:F6}°  lon {2:F6}°\n{3} {4} {5}\nнад морем {6}   над рельефом {7}",
                body.Name,
                latDeg,
                lonDeg,
                ToDms(latDeg, "N", "S"),
                ToDms(lonDeg, "E", "W"),
                string.Empty,
                FormatMeters(distance - body.Radius - seaLevel),
                FormatMeters(distance - body.Radius - terrainHeight));
            statusText = string.Format(
                "скорость {0} (отн. кадра)   режим {1}{2}",
                FormatSpeed(Runner.PlayerFrameRelativeSpeed(Runner.TimeSeconds)),
                Runner.PlayerMode,
                Runner.NoclipActive ? " + НОУКЛИП" : string.Empty);
        }

        /// <summary>Heights in m/km; "e9 m" beyond 1e9 (otherwise the line is 20 chars).</summary>
        private static string FormatMeters(double meters)
        {
            double magnitude = Math.Abs(meters);
            if (magnitude >= 1e9d)
            {
                return (meters / 1e9d).ToString("F2") + "e9 м";
            }

            if (magnitude >= 1e4d)
            {
                return (meters / 1e3d).ToString("F1") + " км";
            }

            return meters.ToString("F2") + " м";
        }

        private static string FormatSpeed(double speed)
        {
            double magnitude = Math.Abs(speed);
            if (magnitude >= 1e9d)
            {
                return (speed / 1e9d).ToString("F2") + "e9 м/с";
            }

            if (magnitude >= 1e4d)
            {
                return (speed / 1e3d).ToString("F1") + " км/с";
            }

            return speed.ToString("F1") + " м/с";
        }

        /// <summary>Degrees to DMS: 27°50′25.8″S reads better at a glance than decimal.</summary>
        private static string ToDms(double degrees, string positiveHemisphere, string negativeHemisphere)
        {
            double absolute = Math.Abs(degrees);
            int wholeDegrees = (int)absolute;
            double minutesTotal = (absolute - wholeDegrees) * 60d;
            int minutes = (int)minutesTotal;
            double seconds = (minutesTotal - minutes) * 60d;
            return string.Format(
                "{0}°{1:00}′{2:00.0}″{3}",
                wholeDegrees,
                minutes,
                seconds,
                degrees < 0d ? negativeHemisphere : positiveHemisphere);
        }
    }
}
