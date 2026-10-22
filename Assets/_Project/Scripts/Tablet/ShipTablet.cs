using System;
using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// Ship control tablet. Attach to the tablet GameObject (or nearby); it renders
    /// a 512×320 screen into a texture that is assigned to the screen mesh
    /// (ScreenRenderer). While the player is not interacting it only shows data
    /// (up to CompactRefreshHz times per second, no input handling). Open() expands
    /// the interface to fullscreen at integer pixel scale, releases the cursor and
    /// enables the buttons; Esc / E collapses it.
    ///
    /// Screen material: HDRP/Unlit preferred (the color map goes into _UnlitColorMap);
    /// _BaseColorMap / _BaseMap / _MainTex / _EmissiveColorMap are also picked up.
    /// </summary>
    public sealed class ShipTablet : MonoBehaviour
    {
        [Tooltip("Мост к симуляции (наследник TabletLink). Пусто + AutoDemo — вешается демо.")]
        public TabletLink Link;

        [Tooltip("Если Link не задан — повесить DemoTabletLink с вымышленными данными.")]
        public bool AutoDemo = true;

        [Tooltip("Меш экрана планшета. Его материал получит текстуру планшета.")]
        public Renderer ScreenRenderer;

        [Tooltip("Обновлений экрана в секунду, пока планшет свёрнут.")]
        [Range(1f, 60f)]
        public float CompactRefreshHz = 15f;

        [Tooltip("С какого расстояния лучом можно жать кнопки прямо на экране (м).")]
        public float WorldTouchMeters = 3f;

        [Tooltip("Яркость экрана на мешe (для HDRP/Unlit — множитель цвета).")]
        [Range(0.2f, 4f)]
        public float ScreenBrightness = 1f;

        private static readonly string[] TextureProperties =
        {
            "_UnlitColorMap", "_BaseColorMap", "_BaseMap", "_MainTex", "_EmissiveColorMap",
        };

        private TabletScreen screen;
        private TabletTelemetry telemetry;
        private Texture2D texture;
        private Material screenMaterial;
        private float nextRefresh;
        private bool prevCursorVisible;
        private CursorLockMode prevCursorLock;

        /// <summary>Screen mesh collider (world pointer: ray → UV → panel pixel).</summary>
        private Collider screenCollider;

        /// <summary>This frame the ray hits the screen (button presses directly on the monitor).</summary>
        private bool worldPointerHit;

        private Galilego.Universe.SimulationRunner runner;
        private bool runnerSearched;
        private readonly RaycastHit[] worldHits = new RaycastHit[8];

        /// <summary>Probe tilt (degrees): the crosshair aims "at the monitor", not at a pixel.</summary>
        private const float PointerProbeDegrees = 1.5f;

        /// <summary>Last reported reason the world pointer is off (logged once per change).</summary>
        private string pointerNote;

        /// <summary>
        /// Frame in which the tablet was opened: the open key (E) is still queued in
        /// the IMGUI events during that same frame, and without this guard OnGUI would
        /// immediately close the tablet with the same press.
        /// </summary>
        private int openFrame = -1;

        /// <summary>Whether the tablet is expanded to fullscreen.</summary>
        public bool Expanded { get; private set; }

        /// <summary>Aim with the mouse (cursor is free) instead of the screen center. Set by InteractiveScreen.</summary>
        public bool MouseAim { get; set; }

        /// <summary>The pointer is currently over the screen (for hiding the system cursor).</summary>
        public bool PointerOnScreen { get { return worldPointerHit; } }

        private static int expandedCount;

        /// <summary>
        /// Whether any tablet is expanded: other interfaces (the BuilderUI inventory
        /// window) use this flag to not grab E while a tablet is open.
        /// </summary>
        public static bool AnyExpanded { get { return expandedCount > 0; } }

        /// <summary>Raised on collapse (the player has regained control).</summary>
        public event Action Closed;

        /// <summary>Screen texture (can be assigned to any other material/RawImage).</summary>
        public Texture2D Texture { get { return texture; } }

        private void Awake()
        {
            screen = new TabletScreen();
            telemetry = new TabletTelemetry();
            texture = new Texture2D(TabletScreen.W, TabletScreen.H, TextureFormat.RGBA32, false)
            {
                name = "TabletScreen",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            BindMaterial();
            InteractiveScreen.EnsureFor(this);
        }

        private void OnDestroy()
        {
            if (Expanded)
            {
                Expanded = false;
                expandedCount = Mathf.Max(0, expandedCount - 1);
                RestoreCursor();
            }

            if (texture != null)
            {
                Destroy(texture);
            }

            if (screenMaterial != null)
            {
                Destroy(screenMaterial);
            }
        }

        private void BindMaterial()
        {
            if (ScreenRenderer == null)
            {
                return;
            }

            screenCollider = ScreenRenderer.GetComponent<Collider>();
            if (screenCollider == null)
            {
                screenCollider = ScreenRenderer.GetComponentInChildren<Collider>();
            }

            screenMaterial = ScreenRenderer.material; // own instance
            for (int i = 0; i < TextureProperties.Length; i++)
            {
                if (screenMaterial.HasProperty(TextureProperties[i]))
                {
                    screenMaterial.SetTexture(TextureProperties[i], texture);
                }
            }

            Color tint = new Color(ScreenBrightness, ScreenBrightness, ScreenBrightness, 1f);
            if (screenMaterial.HasProperty("_UnlitColor"))
            {
                screenMaterial.SetColor("_UnlitColor", tint);
            }

            if (screenMaterial.HasProperty("_EmissiveColor"))
            {
                // HDRP/Unlit without the _EMISSIVE_COLOR_MAP keyword lights the
                // whole screen with _EmissiveColor (UnlitData.hlsl) — white washes the image out.
                screenMaterial.SetColor("_EmissiveColor", Color.black);
            }
        }

        private void Update()
        {
            if (Link == null && AutoDemo)
            {
                Link = gameObject.AddComponent<DemoTabletLink>();
            }

            if (Link == null)
            {
                return;
            }

            // Collapsed — CompactRefreshHz; expanded or under the crosshair (world
            // click on the monitor) — 30 Hz: a click waits no longer than 33 ms.
            if (!Expanded && Time.unscaledTime < nextRefresh)
            {
                return;
            }

            bool fast = Expanded || worldPointerHit;
            nextRefresh = Time.unscaledTime + (1f / (fast ? 30f : Mathf.Max(1f, CompactRefreshHz)));
            Link.Fill(telemetry);
            // Under the crosshair the panel is "interactive" too: hover and click work.
            screen.Render(telemetry, Link, Expanded || worldPointerHit);
            texture.SetPixels32(screen.Canvas.Pixels);
            texture.Apply(false);
        }

        /// <summary>
        /// World pointer AFTER the camera (FirstPersonCamera runs at order −40): the
        /// ray must see THIS frame's poses. In Update the camera is still at last
        /// frame's position — while the vessel moves, the screen is tens of metres
        /// away from that stale origin and the ray never reaches it (the monitor
        /// "stops reacting to clicks" in flight, while it works on the pad).
        /// </summary>
        private void LateUpdate()
        {
            UpdateWorldPointer();
        }

        /// <summary>
        /// World pointer: cast the camera ray (crosshair) against the screen mesh; the
        /// hit point is converted from UV to panel pixels. LMB and drag press buttons
        /// directly on the monitor, and the cursor arrow is drawn on the screen itself.
        /// Active while the tablet is collapsed, the cursor is locked and no building is
        /// in progress (there LMB/RMB place parts).
        /// </summary>
        private void UpdateWorldPointer()
        {
            worldPointerHit = false;

            // A UI (menu, expanded tablet, screen focus) owns the mouse: the world
            // pointer yields to it. The screen focus sets MouseAim and still aims.
            bool uiOwnsCursor = Galilego.Universe.FirstPersonCamera.CursorClaimed;
            if (Expanded || ScreenRenderer == null || screenCollider == null
                || (uiOwnsCursor && !MouseAim))
            {
                NotePointerBlocked(Expanded
                    ? "планшет развёрнут (кнопки жмутся в окне)"
                    : (ScreenRenderer == null || screenCollider == null
                        ? "нет меша/коллайдера экрана"
                        : "курсор занят интерфейсом"));
                screen.SetWorldPointer(false, 0, 0);
                return;
            }

            if (runner == null && !runnerSearched)
            {
                runnerSearched = true;
                runner = FindAnyObjectByType<Galilego.Universe.SimulationRunner>();
            }

            if (runner != null && runner.BuildMode)
            {
                NotePointerBlocked("режим стройки (в стройке прицел по экрану выключен)");
                screen.SetWorldPointer(false, 0, 0);
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                NotePointerBlocked("нет камеры");
                return;
            }

            // The panel draws its own arrow only while the system cursor is captured
            // (the crosshair mode); with a free cursor the OS pointer is the pointer.
            screen.DrawArrow = Cursor.lockState == CursorLockMode.Locked;

            // Captured cursor: the crosshair ray. Free cursor (no UI owns it — the
            // editor just gave the mouse back, or the game view was re-focused):
            // aim with the visible mouse instead of going dead until the cursor is
            // captured again — otherwise the monitor "stops reacting to clicks".
            Ray ray = MouseAim || Cursor.lockState != CursorLockMode.Locked
                ? cam.ScreenPointToRay(Galilego.Universe.PlayerInput.MousePosition())
                : new Ray(cam.transform.position, cam.transform.forward);

            // A small cross of PLAIN rays around the aim (the crosshair aims "at the
            // monitor", not at an exact pixel). Plain raycasts, not a SphereCast: only
            // they give the panel pixel through RaycastHit.textureCoord — a sphere
            // cast reports the contact point and the buttons stop registering.
            int best = -1;
            Vector3 direction = ray.direction;
            for (int probe = 0; probe < 5 && best < 0; probe++)
            {
                Vector3 probeDirection = direction;
                if (probe == 1) probeDirection = Quaternion.AngleAxis(PointerProbeDegrees, cam.transform.up) * direction;
                else if (probe == 2) probeDirection = Quaternion.AngleAxis(-PointerProbeDegrees, cam.transform.up) * direction;
                else if (probe == 3) probeDirection = Quaternion.AngleAxis(PointerProbeDegrees, cam.transform.right) * direction;
                else if (probe == 4) probeDirection = Quaternion.AngleAxis(-PointerProbeDegrees, cam.transform.right) * direction;

                int count = Physics.RaycastNonAlloc(new Ray(ray.origin, probeDirection), worldHits,
                    Mathf.Max(WorldTouchMeters, 1f), ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    // Only the screen itself matters (the mesh collider with UV): the
                    // bezel and the outer monitor housing do not count.
                    if (worldHits[i].collider == screenCollider
                        && (best < 0 || worldHits[i].distance < worldHits[best].distance))
                    {
                        best = i;
                    }
                }
            }

            if (best < 0)
            {
                float distance = cam != null && screenCollider != null
                    ? Vector3.Distance(cam.transform.position, screenCollider.bounds.center)
                    : -1f;
                NotePointerBlocked("луч мимо экрана (до экрана " + distance.ToString("F2")
                    + " м, луч " + WorldTouchMeters.ToString("F2") + " м)");
                screen.SetWorldPointer(false, 0, 0);
                return;
            }

            Vector2 uv = worldHits[best].textureCoord;
            int px = Mathf.Clamp((int)(uv.x * TabletScreen.W), 0, TabletScreen.W - 1);
            int py = Mathf.Clamp((int)((1f - uv.y) * TabletScreen.H), 0, TabletScreen.H - 1);
            screen.SetWorldPointer(true, px, py);
            worldPointerHit = true;
            NotePointerBlocked(null);

            if (Galilego.Universe.PlayerInput.MouseLeftDown())
            {
                Debug.Log("[Click] экран: пиксель " + px + "," + py
                    + " (mouseAim=" + MouseAim + " expanded=" + Expanded + ")");
                screen.Click();
            }

            if (Galilego.Universe.PlayerInput.MouseLeftHeld())
            {
                screen.Drag();
            }
        }

        /// <summary>
        /// The world pointer went on/off — log the reason once per change (the "monitor
        /// does not react" bug is diagnosed by this line alone).
        /// </summary>
        private void NotePointerBlocked(string note)
        {
            if (note == pointerNote)
            {
                return;
            }

            pointerNote = note;
            Debug.Log("[Pointer] " + (note ?? "экран под прицелом: кнопки жмутся")
                + "  (expanded=" + Expanded + "  cursor=" + Cursor.lockState
                + "  mouseAim=" + MouseAim
                + "  заявок=" + Galilego.Universe.FirstPersonCamera.CursorClaimed
                + "  пиксель=" + (screen != null ? screen.PointerX + "," + screen.PointerY : "-")
                + "  годен=" + (screen != null ? screen.PointerValid.ToString() : "-") + ")");
        }

        /// <summary>Expand the tablet to fullscreen (buttons become active).</summary>
        public void Open()
        {
            if (Expanded)
            {
                return;
            }

            Expanded = true;
            expandedCount++;
            openFrame = Time.frameCount;
            prevCursorVisible = Cursor.visible;
            prevCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("[Tablet] развёрнут: курсор=" + Cursor.lockState
                + " заявок=" + Galilego.Universe.FirstPersonCamera.CursorClaimed);
        }

        /// <summary>Collapse the tablet back onto the ship screen.</summary>
        public void Close()
        {
            if (!Expanded)
            {
                return;
            }

            Expanded = false;
            expandedCount = Mathf.Max(0, expandedCount - 1);
            screen.SetPointer(false, 0, 0);
            RestoreCursor();
            Debug.Log("[Tablet] свёрнут: курсор=" + Cursor.lockState
                + " заявок=" + Galilego.Universe.FirstPersonCamera.CursorClaimed);
            Action handler = Closed;
            if (handler != null)
            {
                handler();
            }
        }

        private void RestoreCursor()
        {
            Cursor.lockState = prevCursorLock;
            Cursor.visible = prevCursorVisible;
        }

        private void OnGUI()
        {
            if (!Expanded || texture == null)
            {
                return;
            }

            Event e = Event.current;
            int scale = Mathf.Max(1, Mathf.Min(Screen.width / TabletScreen.W, Screen.height / TabletScreen.H));
            float rw = TabletScreen.W * scale;
            float rh = TabletScreen.H * scale;
            var rect = new Rect(Mathf.Floor((Screen.width - rw) * 0.5f), Mathf.Floor((Screen.height - rh) * 0.5f), rw, rh);

            if (e.type == EventType.Repaint)
            {
                Color old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.82f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                GUI.color = old;
                return;
            }

            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.E))
            {
                // E in the opening frame is the same press TabletInteractable
                // already handled: it must not close the tablet, otherwise the
                // tablet "won't open". Esc always closes.
                if (e.keyCode == KeyCode.E && Time.frameCount == openFrame)
                {
                    e.Use();
                    return;
                }

                e.Use();
                Close();
                return;
            }

            Vector2 m = e.mousePosition;
            bool inside = rect.Contains(m);
            int lx = Mathf.FloorToInt((m.x - rect.x) / scale);
            int ly = Mathf.FloorToInt((m.y - rect.y) / scale);
            screen.SetPointer(inside, lx, ly);

            if (e.type == EventType.MouseDown && e.button == 0 && inside)
            {
                screen.Click();
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && inside)
            {
                screen.Drag();
                e.Use();
            }
        }
    }
}
