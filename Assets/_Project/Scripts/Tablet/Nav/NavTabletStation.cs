using System;
using Galilego.Universe;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Tablet
{
    /// <summary>
    /// Second (navigation) tablet in the Orion cabin. The main ShipTablet uses one
    /// operator monitor (Monitor_Operator_2); this station puts a UI Toolkit
    /// world-space panel on the other operator monitor (Monitor_Operator_1), which
    /// is already in the capsule model to its right. Interaction is the same as the
    /// main tablet: aim -> outline + prompt, E -> the camera flies close, mouse
    /// clicks on the UI, E / Esc -> back away.
    /// </summary>
    [DefaultExecutionOrder(-55)]
    public sealed class NavTabletStation : MonoBehaviour
    {
        [Tooltip("Планшет-источник (ShipTablet капсулы).")]
        public ShipTablet SourceTablet;

        [Tooltip("Запас по краям кадра при приближении.")]
        [Min(1f)]
        public float FitMargin = 1.12f;

        /// <summary>Must match m_PixelsPerUnit in NavTabletPanelSettings.asset.</summary>
        private const float UiPixelsPerUnit = 2625.641f;

        private const float PanelWidth = 1024f;
        private const float PanelHeight = 576f;

        private NavTabletView view;
        private InteractiveScreen screen;
        private SimulationRunner runner;
        private PlayerController player;
        private Material blackMaterial;
        private Volume exposureVolume;
        private VolumeProfile exposureProfile;
        private string[] bodies;
        private int bodyCountSeen = -1;
        private double lastTime = double.NaN;
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
            if (tablet == null || tablet.ScreenRenderer == null)
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

            Transform screenTransform = screenRenderer.transform;
            MeshFilter filter = screenRenderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Vector3 meshSize = mesh != null ? mesh.bounds.size : screenRenderer.localBounds.size;
            Vector3 meshCenter = mesh != null ? mesh.bounds.center : screenRenderer.localBounds.center;

            float worldWidth = screenTransform.TransformVector(new Vector3(meshSize.x, 0f, 0f)).magnitude;
            float worldHeight = screenTransform.TransformVector(new Vector3(0f, meshSize.y, 0f)).magnitude;
            if (worldWidth < 1e-4f || worldHeight < 1e-4f)
            {
                worldWidth = Mathf.Max(0.01f, screenRenderer.bounds.size.x);
                worldHeight = Mathf.Max(0.01f, screenRenderer.bounds.size.y);
            }

            Vector3 lossy = screenTransform.lossyScale;
            float scaleX = Mathf.Abs(lossy.x) > 1e-6f
                ? worldWidth * UiPixelsPerUnit / (PanelWidth * Mathf.Abs(lossy.x))
                : 1f;
            float scaleY = Mathf.Abs(lossy.y) > 1e-6f
                ? worldHeight * UiPixelsPerUnit / (PanelHeight * Mathf.Abs(lossy.y))
                : 1f;

            Transform monitor = screenTransform.parent;

            // The UI Toolkit panel does not paint its transparent background in
            // world space. Black out the monitor body/screen behind it so the
            // green phosphor design is readable instead of showing the white shell.
            Shader blackShader = Shader.Find("HDRP/Unlit");
            if (blackShader != null)
            {
                blackMaterial = new Material(blackShader) { name = "NavTabletBlack" };
                blackMaterial.SetColor("_UnlitColor", Color.black);
                blackMaterial.SetTexture("_UnlitColorMap", Texture2D.blackTexture);
                blackMaterial.SetColor("_EmissiveColor", Color.black);
            }

            Renderer[] monitorRenderers = monitor.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < monitorRenderers.Length; i++)
            {
                Renderer r = monitorRenderers[i];
                if (r == null)
                {
                    continue;
                }

                if (blackMaterial != null)
                {
                    r.material = blackMaterial;
                }
                else
                {
                    Material mat = r.material;
                    if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", Color.black);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.black);
                    if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", Color.black);
                    if (mat.HasProperty("_UnlitColorMap")) mat.SetTexture("_UnlitColorMap", Texture2D.blackTexture);
                    if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", Texture2D.blackTexture);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Texture2D.blackTexture);
                }
            }

            GameObject uiObject = new GameObject("NavTabletUI");
            uiObject.SetActive(false);
            uiObject.transform.SetParent(screenTransform, false);
            // The screen mesh's visible face is local +Z. The UI panel faces -Z by
            // default, so turn it around; keep it a fraction of a millimetre above
            // the mesh (in the model's local units).
            uiObject.transform.localPosition = new Vector3(meshCenter.x, meshCenter.y, meshCenter.z + 0.00002f);
            uiObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            uiObject.transform.localScale = new Vector3(scaleX, scaleY, 1f);

            UIDocument document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = tree;
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = new Vector2(PanelWidth, PanelHeight);
            document.pivotReferenceSize = PivotReferenceSize.Layout;
            document.pivot = Pivot.Center;
            document.sortingOrder = 10;

            view = uiObject.AddComponent<NavTabletView>();
            view.Configure(document, card);

            monitor.gameObject.AddComponent<NavTabletScreenMarker>();
            screen = monitor.gameObject.AddComponent<InteractiveScreen>();
            screen.AutoResolve = false;
            screen.Tablet = null;
            screen.ScreenRenderer = screenRenderer;
            screen.RequireSeated = true;
            screen.FitMargin = FitMargin;
            screen.HighlightRoot = monitor;

            uiObject.SetActive(true);

            var volumeObject = new GameObject("NavTabletExposure");
            volumeObject.transform.SetParent(transform, false);
            exposureVolume = volumeObject.AddComponent<Volume>();
            exposureVolume.isGlobal = true;
            exposureVolume.priority = 100f;
            exposureProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            Exposure exposure = exposureProfile.Add<Exposure>();
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(12f);
            exposureVolume.profile = exposureProfile;
            exposureVolume.enabled = false;

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

            bool aimed = player != null && (player.Focused == screen || player.FocusedScreen == screen);
            if (exposureVolume != null && exposureVolume.enabled != aimed)
            {
                exposureVolume.enabled = aimed;
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
            if (blackMaterial != null)
            {
                Destroy(blackMaterial);
            }

            if (exposureProfile != null)
            {
                Destroy(exposureProfile);
            }
        }
    }

    /// <summary>Marks the navigation monitor (the main tablet does not have it).</summary>
    public sealed class NavTabletScreenMarker : MonoBehaviour
    {
    }

    /// <summary>
    /// Creates one NavTabletStation per ShipTablet (capsules are instantiated at
    /// runtime, so this cannot be authored in the scene) and gates UI Toolkit
    /// world-space input to the moment a navigation tablet is focused.
    /// </summary>
    internal sealed class NavTabletSpawner : MonoBehaviour
    {
        private PanelInputConfiguration input;
        private PlayerController player;
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
            if (input == null)
            {
                EnsureInput();
            }

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.5f;
                Scan();
            }

            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
            }

            bool navFocused = player != null
                && player.FocusedScreen != null
                && player.FocusedScreen.GetComponent<NavTabletScreenMarker>() != null;
            if (input != null && input.processWorldSpaceInput != navFocused)
            {
                input.processWorldSpaceInput = navFocused;
            }
        }

        private void Scan()
        {
            ShipTablet[] tablets = FindObjectsByType<ShipTablet>(FindObjectsInactive.Exclude);
            for (int i = 0; i < tablets.Length; i++)
            {
                ShipTablet tablet = tablets[i];
                if (tablet == null || tablet.ScreenRenderer == null || tablet.GetComponent<NavTabletStation>() != null)
                {
                    continue;
                }

                NavTabletStation station = tablet.gameObject.AddComponent<NavTabletStation>();
                station.Initialize(tablet);
            }
        }

        private void EnsureInput()
        {
            input = FindAnyObjectByType<PanelInputConfiguration>();
            if (input == null)
            {
                var go = new GameObject("NavTabletInput");
                go.transform.SetParent(transform, false);
                input = go.AddComponent<PanelInputConfiguration>();
            }

            input.processWorldSpaceInput = false;
            input.interactionLayers = ~0;
            input.maxInteractionDistance = 4f;
            input.defaultEventCameraIsMainCamera = true;
            input.autoCreatePanelComponents = true;
        }
    }
}


