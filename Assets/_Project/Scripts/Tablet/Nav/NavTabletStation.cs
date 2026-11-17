using System;
using System.Collections.Generic;
using Galilego.Building;
using Galilego.Core;
using Galilego.Events;
using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEngine;
using UnityEngine.UIElements;

namespace Galilego.Tablet
{
    /// <summary>
    /// Second (navigation) tablet in the Orion cabin. The main ShipTablet uses one
    /// operator monitor (Monitor_Operator_2); this station uses the other operator
    /// monitor (Monitor_Operator_1), which is already in the capsule model to its
    /// right. The monitor shows a live map built from the simulation (baked
    /// ephemerides + N-body ship trajectory); aim + E zooms the camera to it and
    /// opens the live UI Toolkit navigation interface full-screen with mouse input.
    /// Outline, prompt and E/Esc match the main tablet.
    /// </summary>
    [DefaultExecutionOrder(-55)]
    public sealed class NavTabletStation : MonoBehaviour
    {
        [Tooltip("Планшет-источник (ShipTablet капсулы).")]
        public ShipTablet SourceTablet;

        [Tooltip("Запас по краям кадра при приближении.")]
        [Min(1f)]
        public float FitMargin = 1.02f;

        [Tooltip("Секунд приближения до открытия интерфейса.")]
        [Min(0f)]
        public float OverlayDelay = 0.35f;

        private NavTabletView view;
        private InteractiveScreen screen;
        private UIDocument document;
        private SimulationRunner runner;
        private PlayerController player;
        private Renderer screenRenderer;
        private Material screenMaterial;
        private Material screenTemplate;
        private string[] bodies;
        private int bodyCountSeen = -1;
        private double lastTime = double.NaN;
        private float focusStartedAt;
        private bool focused;
        private bool overlay;
        private bool initialized;

        // Live map: the model is rebuilt from the simulation, the renderers only draw it.
        private NavMapModel mapModel;
        private NavMapRenderer overlayMap;
        private NavMapRenderer previewMap;
        private readonly List<NavShipSample> shipHistory = new List<NavShipSample>();
        private readonly NavMapInfo mapInfo = new NavMapInfo();
        private double lastHistorySampleTime = double.NaN;
        private double nextMapBuild;
        private int lastCalcVersion = -1;
        private int lastViewVersion = -1;
        private int lastPlanVersion = -1;

        /// <summary>
        /// The physical monitor is always live: a low-rate model rebuild keeps its
        /// craft/body markers moving even while the player is not at the tablet.
        /// </summary>
        private const double PreviewMapIntervalSeconds = 0.5d;
        private bool mapErrorLogged;
        private bool framePinned;
        private int lastTextureVersion = -1;
        private float nextInteractiveMapRedraw;
        private float interactiveUntil;
        private bool mapDirty;
        private Vector2 lastHoverLocal;

        public InteractiveScreen Screen { get { return screen; } }
        public NavTabletView View { get { return view; } }

        /// <summary>Attach a station to a ShipTablet (called by the spawner and safe to call twice).</summary>
        public void Initialize(ShipTablet tablet)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            SourceTablet = tablet;
            if (tablet == null || tablet.ScreenRenderer == null
                || tablet.GetComponentInParent<VesselPartCollider>() == null)
            {
                enabled = false;
                return;
            }

            runner = FindAnyObjectByType<SimulationRunner>();
            Renderer alternate = FindAlternateScreen(tablet);
            if (alternate == null)
            {
                Debug.LogWarning("[NavTablet] в капсуле не найден второй экран для навигационного планшета");
                enabled = false;
                return;
            }

            BuildOn(alternate);
        }

        /// <summary>
        /// The other operator monitor. The main tablet is Monitor_Operator_2; the
        /// navigation one is Monitor_Operator_1 (the screen to its right when the
        /// player faces the operator desk). Falls back to any other *_Screen mesh.
        /// </summary>
        private static Renderer FindAlternateScreen(ShipTablet tablet)
        {
            Renderer source = tablet.ScreenRenderer;
            Renderer fallback = null;
            Renderer[] renderers = tablet.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || r == source || !r.name.EndsWith("_Screen", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (r.name.IndexOf("Monitor_Operator", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return r;
                }

                if (fallback == null)
                {
                    fallback = r;
                }
            }

            return fallback;
        }

        private void BuildOn(Renderer screenRenderer)
        {
            this.screenRenderer = screenRenderer;
            PanelSettings panel = Resources.Load<PanelSettings>("NavTablet/NavTabletPanelSettings");
            VisualTreeAsset tree = Resources.Load<VisualTreeAsset>("NavTablet/NavTablet");
            VisualTreeAsset card = Resources.Load<VisualTreeAsset>("NavTablet/ManeuverCard");
            if (panel == null || tree == null)
            {
                Debug.LogError("[NavTablet] не найдены Resources/NavTablet/NavTabletPanelSettings.asset или NavTablet.uxml");
                enabled = false;
                return;
            }

            // The right monitor carries the same live map the overlay shows: the
            // renderer below is redrawn from SimulationRunner (baked ephemerides +
            // N-body ship trajectory), not from a static preview image.
            overlayMap = new NavMapRenderer(1024, 640, false);
            previewMap = new NavMapRenderer(1024, 576, true);
            previewMap.Draw(null, null, new NavMapInfo { Connected = false, Status = "НЕТ СВЯЗИ С СИМУЛЯЦИЕЙ" });
            overlayMap.Draw(null, null, new NavMapInfo { Connected = false, Status = "НЕТ СВЯЗИ С СИМУЛЯЦИЕЙ" });
            screenTemplate = screenRenderer.sharedMaterial;
            BindScreenMaterial();
            AssignPreviewTexture();

            GameObject uiObject = new GameObject("NavTabletUI");
            uiObject.SetActive(false);
            uiObject.transform.SetParent(transform, false);

            document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = tree;
            document.sortingOrder = 10;

            view = uiObject.AddComponent<NavTabletView>();
            view.Configure(document, card);
            view.FrameChanged += _ =>
            {
                framePinned = true;
                overlayMap.ResetView();
                RebuildMap(true);
            };
            view.CenterChanged += _ => RebuildMap(true);
            view.OrbitToggled += _ => RebuildMap(true);
            view.MapDragged += OnMapDragged;
            view.MapZoomed += OnMapZoomed;
            view.MapHovered += OnMapHovered;
            view.MapPointerLeft += () => { if (view != null) { view.HideTip(); } };
            view.MapReset += () =>
            {
                overlayMap.ResetView();
                interactiveUntil = Time.unscaledTime + 0.2f;
                mapDirty = true;
                RebuildMap(true);
            };
            view.ManeuverDue += _ => RebuildMap(true);

            Transform monitor = screenRenderer.transform.parent;
            monitor.gameObject.AddComponent<NavTabletScreenMarker>();
            screen = monitor.gameObject.AddComponent<InteractiveScreen>();
            screen.AutoResolve = false;
            screen.Tablet = null;
            screen.ScreenRenderer = screenRenderer;
            screen.RequireSeated = true;
            screen.FitMargin = FitMargin;
            screen.HighlightRoot = monitor;

            uiObject.SetActive(true);
            overlay = false;
            document.rootVisualElement.style.display = DisplayStyle.None;
            Debug.Log("[NavTablet] навигационный планшет подключён ко второму экрану: " + screenRenderer.name);
        }

        private void Update()
        {
            if (view == null || !view.Bound)
            {
                return;
            }

            if (runner == null)
            {
                runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
            }

            bool nowFocused = player != null && player.FocusedScreen == screen;
            if (nowFocused && !focused)
            {
                focusStartedAt = Time.unscaledTime;
            }

            focused = nowFocused;
            if (!focused)
            {
                SetOverlay(false);
                focusStartedAt = 0f;
            }
            else if (!overlay && Time.unscaledTime - focusStartedAt >= OverlayDelay)
            {
                SetOverlay(true);
            }

            if (runner == null)
            {
                return;
            }

            FeedBodies();
            AutoFrame();

            view.MissionSeconds = runner.TimeSeconds;
            view.UniverseSeconds = runner.TimeSeconds;

            double mass = runner.Ship != null && runner.Ship.Mass > 0d ? runner.Ship.Mass : 0d;
            double thrust = 0d;
            double flow = 0d;
            if (runner.Propulsion != null)
            {
                thrust = runner.Propulsion.ThrustN;
                flow = runner.Propulsion.MassFlowKgS;
            }

            if (thrust <= 0d && runner.MainThrustNewtons > 0d)
            {
                thrust = runner.MainThrustNewtons;
            }

            view.ShipMassKg = mass;
            view.ThrustN = thrust;
            view.IspS = thrust > 0d && flow > 1e-9d ? thrust / (flow * 9.80665d) : 0d;

            double now = runner.TimeSeconds;
            double simDt = double.IsNaN(lastTime) ? 0d : Math.Max(0d, now - lastTime);
            lastTime = now;

            SampleHistory(now);
            NavBodyTextureCache.Advance(1024);
            if (NavBodyTextureCache.Version != lastTextureVersion)
            {
                lastTextureVersion = NavBodyTextureCache.Version;
                RebuildMap(true);
            }

            TickMapAnimation();
            RebuildMapIfNeeded(false);
            TickPreviewAnimation();
            TickInteractiveMap();
            view.Tick(Time.unscaledDeltaTime, simDt);
        }

        /// <summary>
        /// The physical monitor uses the same camera hand-over as the overlay. If the
        /// player left while the turntable was still easing, keep easing the monitor
        /// to that target; the markers themselves come from the low-rate rebuild.
        /// </summary>
        private void TickPreviewAnimation()
        {
            if (overlay || previewMap == null || view == null || !view.Bound)
            {
                return;
            }

            if (previewMap.Tick(Time.unscaledDeltaTime))
            {
                previewMap.Interactive = true;
                mapDirty = true;
            }
            else if (previewMap.Interactive)
            {
                // The camera has settled: draw one clean globe instead of keeping
                // the coarse interactive raster forever.
                previewMap.Interactive = false;
                mapDirty = true;
            }
        }

        /// <summary>
        /// Eases the map camera toward the drag target. While it moves, the map is
        /// redrawn at the interactive rate; when it settles, TickInteractiveMap
        /// renders one clean frame. Without this the view snapped on every drag delta.
        /// </summary>
        private void TickMapAnimation()
        {
            if (!overlay || overlayMap == null || view == null || !view.Bound)
            {
                return;
            }

            if (overlayMap.Tick(Time.unscaledDeltaTime))
            {
                overlayMap.Interactive = true;
                interactiveUntil = Time.unscaledTime + 0.15f;
                mapDirty = true;
            }
        }

        /// <summary>
        /// Dragging the map used to redraw the whole software globe on every pointer
        /// event (a 100+ ms main-thread raster per event). Input now only records the
        /// new view; the image follows at ~10 Hz with a coarse globe, and one clean
        /// frame is rendered when the gesture ends.
        /// </summary>
        private void TickInteractiveMap()
        {
            if (view == null || !view.Bound)
            {
                return;
            }

            if (!overlay)
            {
                // The physical monitor is visible while the player is away: keep the
                // camera easing and redraw the existing model. The simulation clock
                // still rebuilds the model at the low-rate tick, so markers move too.
                if (previewMap == null || mapModel == null || !mapDirty)
                {
                    return;
                }

                if (Time.unscaledTime < nextInteractiveMapRedraw)
                {
                    return;
                }

                nextInteractiveMapRedraw = Time.unscaledTime + (previewMap.Interactive ? 0.05f : 0f);
                mapDirty = false;
                previewMap.Draw(mapModel, view.State, mapInfo);
                AssignPreviewTexture();
                return;
            }

            if (overlayMap == null)
            {
                return;
            }

            bool interacting = Time.unscaledTime < interactiveUntil;
            if (overlayMap.Interactive != interacting)
            {
                overlayMap.Interactive = interacting;
                mapDirty = true;
            }

            if (!mapDirty || Time.unscaledTime < nextInteractiveMapRedraw)
            {
                return;
            }

            nextInteractiveMapRedraw = Time.unscaledTime + (interacting ? 0.033f : 0f);
            mapDirty = false;
            RedrawOverlayMap();
        }

        /// <summary>
        /// Actual ship position trail, sampled from the simulation on every frame the
        /// clock has moved (up to the history length). The map's history line is this
        /// buffer, never an analytical reconstruction.
        /// </summary>
        private void SampleHistory(double now)
        {
            if (view == null || runner == null || runner.Ship == null)
            {
                return;
            }

            double historySeconds = view.State.HistoryS;
            if (historySeconds <= 0d)
            {
                shipHistory.Clear();
                lastHistorySampleTime = now;
                return;
            }

            if (double.IsNaN(lastHistorySampleTime))
            {
                lastHistorySampleTime = now;
            }

            double wanted = Math.Max(0.25d, historySeconds / 240d);
            double elapsed = now - lastHistorySampleTime;
            if (elapsed < 0d)
            {
                // The clock was reset (teleport/new flight): the old trail is not the
                // same trajectory any more.
                shipHistory.Clear();
                lastHistorySampleTime = now;
                return;
            }

            if (elapsed < wanted && shipHistory.Count > 0)
            {
                // A short suborbital arc can cross the whole visible sky before the
                // window-scaled cadence gives it a second sample. Add a point when
                // the bearing around the dominant body changes noticeably, so the
                // trail is a curve and not one chord. Slower flights keep the time
                // cadence (and the buffer is decimated, never truncated).
                NavShipSample previous = shipHistory[shipHistory.Count - 1];
                if (!BearingChanged(previous.Position, runner.Ship.Position, now, 0.25d))
                {
                    return;
                }
            }

            shipHistory.Add(new NavShipSample(now, runner.Ship.Position));
            lastHistorySampleTime = now;
            TrimHistory(historySeconds, now);
        }

        /// <summary>True when the direction from the dominant body turned by at least <paramref name="minDegrees"/>.</summary>
        private bool BearingChanged(Vector3d previous, Vector3d current, double time, double minDegrees)
        {
            OrbitingBody body = runner.Ship != null ? runner.Ship.LocalBody : null;
            if (body == null && runner.SystemState != null)
            {
                body = runner.SystemState.FindDominantBody(current, time);
            }

            if (body == null)
            {
                return false;
            }

            body.EvaluateWorldState(time, out Vector3d center, out _);
            Vector3d a = previous - center;
            Vector3d b = current - center;
            double lengthA = a.Magnitude;
            double lengthB = b.Magnitude;
            if (!(lengthA > 1d) || !(lengthB > 1d))
            {
                return false;
            }

            double cosine = Math.Max(-1d, Math.Min(1d, Vector3d.Dot(a, b) / (lengthA * lengthB)));
            return Math.Acos(cosine) * (180d / Math.PI) >= minDegrees;
        }

        /// <summary>
        /// Keeps the whole slider span while preserving detail near the craft: once the
        /// buffer exceeds twice the draw cap, the newest half is kept intact and the
        /// older half is thinned to fill the other half. Truncating the front instead
        /// would silently shorten a fast flight's trail to the last few minutes.
        /// </summary>
        private void TrimHistory(double historySeconds, double now)
        {
            double cutoff = now - historySeconds;
            int drop = 0;
            while (drop < shipHistory.Count && shipHistory[drop].TimeSeconds < cutoff)
            {
                drop++;
            }

            if (drop > 0)
            {
                shipHistory.RemoveRange(0, drop);
            }

            int max = NavMapModel.MaxHistoryPoints;
            if (shipHistory.Count <= 2 * max)
            {
                return;
            }

            int keepRecent = max / 2;
            int older = shipHistory.Count - keepRecent;
            int targetOlder = max - keepRecent;
            int stride = Math.Max(2, (older + targetOlder - 1) / targetOlder);
            var compact = new List<NavShipSample>((targetOlder + keepRecent) + 1);
            for (int i = 0; i < older; i += stride)
            {
                compact.Add(shipHistory[i]);
            }

            for (int i = older; i < shipHistory.Count; i++)
            {
                compact.Add(shipHistory[i]);
            }

            shipHistory.Clear();
            shipHistory.AddRange(compact);
        }

        /// <summary>
        /// Rebuild the map when the geometry actually changes: a slider/frame/center/
        /// visibility edit, or a low-rate clock tick. Forecast integration is the
        /// expensive part, so it never runs every frame.
        /// </summary>
        private void RebuildMapIfNeeded(bool force)
        {
            if (view == null || !view.Bound || runner == null || runner.SystemState == null)
            {
                return;
            }

            bool forced = force;
            int calc = view.State.CalcVersion;
            int viewVersion = view.State.ViewVersion;
            int planVersion = view.State.Plan.Version;
            if (calc != lastCalcVersion || viewVersion != lastViewVersion || planVersion != lastPlanVersion)
            {
                forced = true;
            }

            double interval = overlay
                ? (mapModel != null && mapModel.ViewMode == NavViewMode.Surface ? 0.6d : 0.35d)
                : PreviewMapIntervalSeconds;
            if (!forced && Time.unscaledTime < nextMapBuild)
            {
                return;
            }

            nextMapBuild = Time.unscaledTime + interval;
            lastCalcVersion = calc;
            lastViewVersion = viewVersion;
            lastPlanVersion = planVersion;
            RebuildMap();
        }

        private void RebuildMap(bool force = true)
        {
            if (view == null || !view.Bound || runner == null || runner.SystemState == null)
            {
                return;
            }

            if (overlayMap == null)
            {
                overlayMap = new NavMapRenderer(1024, 640, false);
            }

            if (previewMap == null)
            {
                previewMap = new NavMapRenderer(1024, 576, true);
            }

            if (mapModel == null)
            {
                mapModel = new NavMapModel();
            }

            bool allowForecast = !ContactZoneHost.Active
                && !runner.BuildMode
                && runner.Regime == VesselRegime.Flying
                && runner.Ship != null;

            // A standing craft is carried by the site frame. Its past/future is the
            // planet's orbit + rotation, computed from the ephemerides, so it has a
            // real (non-ballistic) history/forecast even though it cannot coast.
            bool parked = runner.Ship != null
                && (ContactZoneHost.Active || runner.BuildMode || runner.Regime == VesselRegime.Landed);

            try
            {
                mapModel.Rebuild(
                    runner.SystemState,
                    runner.Ship,
                    runner.TimeSeconds,
                    view.State.FrameIndex,
                    view.State.CenterIndex,
                    view.State.OrbitVisible,
                    view.State.ForecastS,
                    view.State.HistoryS,
                    shipHistory,
                    allowForecast,
                    parked,
                    view.State.Plan,
                    view.IspS);

                // Ballistic coast vs. a maneuver in progress: the map tints the ship and its apsis marks by it.
                mapModel.ShipThrusting = runner.Ship != null
                    && runner.RawThrottle > 0.001d
                    && ((runner.Propulsion != null && runner.Propulsion.ThrustN > 0d) || runner.MainThrustNewtons > 0d);
            }
            catch (Exception e)
            {
                // The tablet is a display: a corrupt ephemeris or a bad system asset
                // must not take down the simulation loop. Keep the last good geometry
                // and state the problem on the panel.
                if (!mapErrorLogged)
                {
                    mapErrorLogged = true;
                    Debug.LogWarning("[NavTablet] карта не построена: " + e);
                }

                mapInfo.Connected = false;
                mapInfo.Status = "ОШИБКА КАРТЫ: " + e.GetType().Name;
                view.ForecastAvailable = false;
                if (overlay)
                {
                    overlayMap.Draw(mapModel, view.State, mapInfo);
                    view.SetMapTexture(overlayMap.Texture);
                }
                else
                {
                    previewMap.Draw(mapModel, view.State, mapInfo);
                    AssignPreviewTexture();
                }

                return;
            }

            FillMapInfo();
            // A body forecast (the centred body's own ephemeris span) uses the
            // forecast slider even when the ship has no free-flight forecast.
            view.ForecastAvailable = allowForecast
                || (mapModel != null && (mapModel.ForecastValid || mapModel.HasBodyForecast));
            if (overlay)
            {
                overlayMap.Draw(mapModel, view.State, mapInfo);
                view.SetMapTexture(overlayMap.Texture);
            }
            else
            {
                previewMap.Draw(mapModel, view.State, mapInfo);
                AssignPreviewTexture();
            }

            if (force)
            {
                nextMapBuild = Time.unscaledTime + (overlay
                    ? (mapModel.ViewMode == NavViewMode.Surface ? 0.6d : 0.35d)
                    : PreviewMapIntervalSeconds);
            }
        }

        private void FillMapInfo()
        {
            mapInfo.Connected = runner != null && runner.SystemState != null && runner.Ship != null;
            mapInfo.PlanDeltaVMs = view != null ? view.State.Plan.TotalDv : 0d;
            mapInfo.ManeuverCount = view != null ? view.State.Plan.Count : 0;
            mapInfo.MassKg = runner != null && runner.Ship != null ? runner.Ship.Mass : 0d;
            mapInfo.SpeedMs = mapModel != null ? mapModel.FrameRelativeShipVelocity.Magnitude : 0d;
            mapInfo.HasShip = mapModel != null && mapModel.HasShip;
            mapInfo.AltitudeMeters = mapModel != null ? mapModel.ShipHeightAboveTerrain : 0d;
            mapInfo.VerticalSpeedMps = mapModel != null ? mapModel.ShipVerticalSpeedMps : 0d;
            mapInfo.Status = mapModel != null ? mapModel.Note : string.Empty;
        }

        private void OnMapDragged(Vector2 delta)
        {
            if (overlayMap == null || view == null)
            {
                return;
            }

            // Centre-locked map: a drag is always a turntable rotation around the
            // selected body. There is no pan that could move it off screen centre.
            overlayMap.RotateByPixels(ToMapPixels(delta));
            overlayMap.Interactive = true;
            interactiveUntil = Time.unscaledTime + 0.2f;
            mapDirty = true;
        }

        private void OnMapZoomed(Vector2 localPosition, float wheelDelta)
        {
            if (overlayMap == null)
            {
                return;
            }

            overlayMap.ZoomAt(ToMapPixels(localPosition), wheelDelta);
            overlayMap.Interactive = true;
            interactiveUntil = Time.unscaledTime + 0.2f;
            mapDirty = true;
        }

        private void OnMapHovered(Vector2 localPosition)
        {
            if (overlayMap == null || view == null)
            {
                return;
            }

            lastHoverLocal = localPosition;
            if (overlayMap.TryPick(ToMapPixels(localPosition), out string text))
            {
                view.ShowTip(text, lastHoverLocal);
            }
            else
            {
                view.HideTip();
            }
        }

        private void RedrawOverlayMap()
        {
            if (overlayMap == null || mapModel == null || view == null || !view.Bound)
            {
                return;
            }

            overlayMap.Draw(mapModel, view.State, mapInfo);
            view.SetMapTexture(overlayMap.Texture);
        }

        /// <summary>UI pixels → overlay texture pixels (the texture is stretched to the map view).</summary>
        private Vector2 ToMapPixels(Vector2 uiPixels)
        {
            if (overlayMap == null || view == null)
            {
                return uiPixels;
            }

            Vector2 size = view.MapSize;
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0.5f || size.y <= 0.5f)
            {
                return uiPixels;
            }

            return new Vector2(
                uiPixels.x * overlayMap.Width / size.x,
                uiPixels.y * overlayMap.Height / size.y);
        }

        /// <summary>
        /// The builder paints placed parts with ghost/delete materials and restores
        /// them with <see cref="PartCatalog.PartVisual.RestoreNatural"/>, which puts the
        /// shared material back and drops the instance this station had textured. The
        /// physical monitor then shows its static texture (or nothing) until a rebind.
        /// Detect the swap and take a fresh instance instead of writing to an orphan.
        /// </summary>
        private void BindScreenMaterial()
        {
            if (screenRenderer == null)
            {
                return;
            }

            Material current = screenRenderer.sharedMaterial;
            if (current == null)
            {
                screenMaterial = null;
                return;
            }

            // A ghost/delete paint (HDRP/Lit) must not become the monitor surface;
            // put the original screen shader back and take an instance of that.
            if (screenTemplate != null && current.shader != screenTemplate.shader)
            {
                screenRenderer.sharedMaterial = screenTemplate;
                current = screenTemplate;
            }

            if (!ReferenceEquals(current, screenMaterial))
            {
                screenMaterial = screenRenderer.material; // own instance of the current material
            }

            if (screenMaterial == null)
            {
                return;
            }

            if (screenMaterial.HasProperty("_UnlitColor"))
            {
                screenMaterial.SetColor("_UnlitColor", Color.white);
            }

            if (screenMaterial.HasProperty("_BaseColor"))
            {
                screenMaterial.SetColor("_BaseColor", Color.white);
            }

            if (screenMaterial.HasProperty("_EmissiveColor"))
            {
                screenMaterial.SetColor("_EmissiveColor", Color.black);
            }
        }

        private void AssignPreviewTexture()
        {
            if (previewMap == null)
            {
                return;
            }

            BindScreenMaterial();
            if (screenMaterial == null)
            {
                return;
            }

            string[] textureProperties =
            {
                "_UnlitColorMap", "_BaseColorMap", "_BaseMap", "_MainTex", "_EmissiveColorMap",
            };
            for (int i = 0; i < textureProperties.Length; i++)
            {
                if (screenMaterial.HasProperty(textureProperties[i]))
                {
                    screenMaterial.SetTexture(textureProperties[i], previewMap.Texture);
                }
            }
        }

        private void SetOverlay(bool on)
        {
            if (overlay == on || document == null)
            {
                return;
            }

            // The monitor and the overlay are two renderers with different layouts.
            // Hand the camera over so entering and leaving the tablet does not snap
            // the map back to the other renderer's last remembered view.
            NavMapRenderer target = on ? overlayMap : previewMap;
            NavMapRenderer source = on ? previewMap : overlayMap;
            if (target != null && source != null)
            {
                target.CopyViewFrom(source);
                // The hand-over replaces the camera; the first frame of the newly
                // visible map should be the clean, full-quality one.
                target.Interactive = false;
            }

            overlay = on;
            NavOverlayState.Open = on;
            document.rootVisualElement.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            // Opening/closing swaps which texture is visible: redraw the one that
            // just became active immediately, not on the next low-rate tick.
            RebuildMap(true);
        }

        private void FeedBodies()
        {
            var all = runner.SystemState != null ? runner.SystemState.AllBodies : null;
            if (all == null || all.Count == 0)
            {
                return;
            }

            if (all.Count == bodyCountSeen)
            {
                return;
            }

            bodies = new string[all.Count];
            var orbitAvailable = new bool[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                bodies[i] = (all[i].Name ?? "?").ToUpperInvariant();
                orbitAvailable[i] = all[i].Parent != null;
            }

            bodyCountSeen = all.Count;
            view.SetBodies(bodies, orbitAvailable);
        }

        /// <summary>
        /// By default the map's inertial frame follows the body the ship is actually
        /// orbiting (the dominant body, chosen by the simulation). As soon as the player
        /// presses "ИСК" in the reference-frame table, the choice is pinned and is not
        /// overridden on an SOI change.
        /// </summary>
        private void AutoFrame()
        {
            if (framePinned || view == null || !view.Bound || runner == null || runner.SystemState == null)
            {
                return;
            }

            OrbitingBody dominant = runner.DominantBody;
            if (dominant == null)
            {
                return;
            }

            var all = runner.SystemState.AllBodies;
            int index = -1;
            for (int i = 0; i < all.Count; i++)
            {
                if (ReferenceEquals(all[i], dominant))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            if (view.State.FrameIndex == index)
            {
                return;
            }

            view.State.SetFrame(index);
            if (!view.State.CenterChosen)
            {
                // Planet view by default: the whole body is the focus, the craft
                // marker sits on/above it. A manual "ЦЕНТР" click pins another focus.
                view.State.SetCenterDefault(index);
            }

            if (bodies != null)
            {
                // Rebuild the table's highlight with the new frame/centre.
                view.SetBodies(bodies);
            }

            if (overlayMap != null)
            {
                overlayMap.ResetView();
            }
        }

        private void OnDestroy()
        {
            NavOverlayState.Open = false;

            if (screenMaterial != null)
            {
                Destroy(screenMaterial);
            }

            if (overlayMap != null)
            {
                overlayMap.Dispose();
                overlayMap = null;
            }

            if (previewMap != null)
            {
                previewMap.Dispose();
                previewMap = null;
            }
        }
    }

    /// <summary>Marks the navigation monitor (the main tablet does not have it).</summary>
    public sealed class NavTabletScreenMarker : MonoBehaviour
    {
    }

    /// <summary>
    /// Creates one NavTabletStation per ShipTablet. Capsules are instantiated at
    /// runtime (build mode and flight), so this cannot be authored in the scene.
    /// </summary>
    internal sealed class NavTabletSpawner : MonoBehaviour
    {
        private float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<NavTabletSpawner>() != null)
            {
                return;
            }

            var go = new GameObject("NavTabletSpawner");
            DontDestroyOnLoad(go);
            go.AddComponent<NavTabletSpawner>();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextScan)
            {
                return;
            }

            nextScan = Time.unscaledTime + 0.5f;
            ShipTablet[] tablets = FindObjectsByType<ShipTablet>(FindObjectsInactive.Exclude);
            for (int i = 0; i < tablets.Length; i++)
            {
                ShipTablet tablet = tablets[i];
                if (tablet == null || tablet.ScreenRenderer == null
                    || tablet.GetComponentInParent<VesselPartCollider>() == null
                    || tablet.GetComponent<NavTabletStation>() != null)
                {
                    continue;
                }

                NavTabletStation station = tablet.gameObject.AddComponent<NavTabletStation>();
                station.Initialize(tablet);
            }
        }
    }
}

