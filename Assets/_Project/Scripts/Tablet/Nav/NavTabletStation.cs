using System;
using Galilego.Building;
using Galilego.Universe;
using UnityEngine;
using UnityEngine.UIElements;

namespace Galilego.Tablet
{
    /// <summary>
    /// Second (navigation) tablet in the Orion cabin. The main ShipTablet uses one
    /// operator monitor (Monitor_Operator_2); this station uses the other operator
    /// monitor (Monitor_Operator_1), which is already in the capsule model to its
    /// right. The monitor shows a navigation preview; aim + E zooms the camera to it
    /// and opens the live UI Toolkit navigation interface full-screen with mouse
    /// input. Outline, prompt and E/Esc match the main tablet.
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
        private Material screenMaterial;
        private string[] bodies;
        private int bodyCountSeen = -1;
        private double lastTime = double.NaN;
        private float focusStartedAt;
        private bool focused;
        private bool overlay;
        private bool initialized;

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
            PanelSettings panel = Resources.Load<PanelSettings>("NavTablet/NavTabletPanelSettings");
            VisualTreeAsset tree = Resources.Load<VisualTreeAsset>("NavTablet/NavTablet");
            VisualTreeAsset card = Resources.Load<VisualTreeAsset>("NavTablet/ManeuverCard");
            if (panel == null || tree == null)
            {
                Debug.LogError("[NavTablet] не найдены Resources/NavTablet/NavTabletPanelSettings.asset или NavTablet.uxml");
                enabled = false;
                return;
            }

            // The right monitor shows the navigation preview image (a texture on the
            // screen material), so it is never a black hole. The live interface opens
            // as a screen-space overlay on E.
            Texture2D preview = Resources.Load<Texture2D>("NavTablet/NavTabletPreview");
            screenMaterial = screenRenderer.material; // own instance
            if (preview != null)
            {
                string[] textureProperties =
                {
                    "_UnlitColorMap", "_BaseColorMap", "_BaseMap", "_MainTex", "_EmissiveColorMap",
                };
                for (int i = 0; i < textureProperties.Length; i++)
                {
                    if (screenMaterial.HasProperty(textureProperties[i]))
                    {
                        screenMaterial.SetTexture(textureProperties[i], preview);
                    }
                }
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

            GameObject uiObject = new GameObject("NavTabletUI");
            uiObject.SetActive(false);
            uiObject.transform.SetParent(transform, false);

            document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = tree;
            document.sortingOrder = 10;

            view = uiObject.AddComponent<NavTabletView>();
            view.Configure(document, card);

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

            view.MissionSeconds = runner.TimeSeconds;
            view.UniverseSeconds = runner.TimeSeconds;

            double mass = runner.Ship != null && runner.Ship.Mass > 0d ? runner.Ship.Mass : 14000d;
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
            view.ThrustN = thrust > 0d ? thrust : 40000d;
            view.IspS = thrust > 0d && flow > 1e-9d ? thrust / (flow * 9.80665d) : 330d;

            double now = runner.TimeSeconds;
            double simDt = double.IsNaN(lastTime) ? 0d : Math.Max(0d, now - lastTime);
            lastTime = now;
            view.Tick(Time.unscaledDeltaTime, simDt);
        }

        private void SetOverlay(bool on)
        {
            if (overlay == on || document == null)
            {
                return;
            }

            overlay = on;
            NavOverlayState.Open = on;
            document.rootVisualElement.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
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
            for (int i = 0; i < all.Count; i++)
            {
                bodies[i] = (all[i].Name ?? "?").ToUpperInvariant();
            }

            bodyCountSeen = all.Count;
            view.SetBodies(bodies);
        }

        private void OnDestroy()
        {
            NavOverlayState.Open = false;

            if (screenMaterial != null)
            {
                Destroy(screenMaterial);
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

