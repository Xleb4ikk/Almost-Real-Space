using Galilego.Building;
using Galilego.Tablet;
using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Pause menu (Esc) with a separate settings screen.
    ///   Pause:    Continue / Settings / Quit — buttons in the centre.
    ///   Settings: tabs Graphics, View, Controls (key bindings), Audio.
    /// IMGUI, drawn with MenuTheme (generated rounded textures, OS font, no assets, no
    /// GUI matrix — sizes are multiplied by the UI scale so text stays crisp).
    /// While open the player input is blocked (PlayerInput returns "nothing pressed",
    /// PlayerController.InputBlocked is set), the cursor is released and the simulation
    /// is paused (optional). Esc: leaves a screen / tablet / builder window / cheat menu
    /// first if one is open; here it goes Settings → Pause → closed. Runs before every
    /// other script (order -200) so it sees the state as it was when Esc was pressed.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class SettingsMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        // The project enters Play without a domain reload: a menu left open in the
        // previous session must not keep PlayerInput blocked in the next one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsOpen = false;
        }

        private enum MenuScreen { Pause, Settings }

        private enum BtnKind { Normal, Primary, Danger, Ghost }

        private const float CtrlW = 360f;
        private const float RowPad = 22f;

        private static readonly string[] TabNames = { "Графика", "Вид", "Управление", "Звук" };
        private static readonly string[] TabIds = { "graphics", "view", "controls", "audio" };
        private static readonly string[] ScreenModes = { "Полный экран", "Окно без рамки", "В окне" };
        private static readonly string[] OffOn = { "Выкл", "Вкл" };
        private static readonly string[] AaNames = { "Выкл", "FXAA", "SMAA", "TAA" };
        private static readonly string[] TextureNames = { "Высокое", "Среднее", "Низкое" };
        private static readonly string[] AnisoNames = { "Выкл", "Обычная", "Принудит." };
        private static readonly string[] BlurNames = { "Выкл", "Низкое", "Среднее", "Высокое" };
        private static readonly string[] GrassNames = { "Отключена", "Низкое", "Среднее", "Высокое", "Максимальное" };
        private static readonly int[] FrameCaps = { 0, 30, 60, 75, 90, 120, 144, 165, 240 };
        private static readonly string[] FrameCapNames = { "Без лимита", "30", "60", "75", "90", "120", "144", "165", "240" };

        private SimulationRunner runner;
        private PlayerController controller;
        private FirstPersonCamera view;

        private bool lockBefore;
        private bool cursorWasLocked;
        private bool pausedByMenu;

        private MenuScreen screen;
        private int tab;
        private readonly float[] scrolls = new float[4];
        private readonly float[] contentHeights = new float[4];
        private readonly float[,] cardHeights = new float[4, 16];

        // Key capture
        private bool capturing;
        private GameAction captureAction;
        private int captureSlot;

        // Staged display settings
        private List<Vector2Int> resolutions = new List<Vector2Int>();
        private string[] resolutionLabels = new string[0];
        private int stageRes;
        private int stageMode;

        private string toast;
        private float toastUntil;
        private bool confirmQuit;

        // Animation
        private float openT;
        private float screenT;
        private float tabT = 1f;
        private float tabUnderline = -1f;
        private float fade;
        private float localFade = 1f;
        private readonly float[] hov = new float[1024];
        private readonly float[] anim2 = new float[1024];
        private readonly bool[] anim2Init = new bool[1024];

        // Drawing state
        private float ui = 1f;
        private float y;
        private float rowW;
        private int hid;
        private bool measuring;
        private bool modal;
        private bool mouseInView = true;
        private bool firstRow;
        private int cardIndex;
        private float cardTop;
        private int activeSlider = -1;
        private bool scrollDrag;
        private float scrollDragOffset;

        // ─── Open / close ────────────────────────────────────────────────────

        private void Update()
        {
            if (IsOpen)
            {
                UpdateOpen();
                return;
            }

            if (PlayerInput.RawEscapeDown() && CanOpen())
            {
                PlayerInput.EscapeConsumedFrame = Time.frameCount;
                Open();
            }
        }

        private void UpdateOpen()
        {
            if (capturing)
            {
                // IMGUI does not always deliver a lone modifier press; catch those here.
                KeyCode[] modifiers =
                {
                    KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl,
                    KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt
                };
                foreach (KeyCode k in modifiers)
                {
                    if (KeyBindings.RawDown(k))
                    {
                        Assign(k);
                        break;
                    }
                }

                return;
            }

            if (!PlayerInput.RawEscapeDown())
            {
                return;
            }

            PlayerInput.EscapeConsumedFrame = Time.frameCount;
            SettingsApplier applier = SettingsApplier.Instance;
            if (applier != null && applier.RevertPending)
            {
                applier.RevertDisplay();
                SyncStagedDisplay();
                return;
            }

            if (confirmQuit)
            {
                confirmQuit = false;
                return;
            }

            if (screen == MenuScreen.Settings)
            {
                GoTo(MenuScreen.Pause);
                return;
            }

            Close();
        }

        private void GoTo(MenuScreen next)
        {
            if (screen == MenuScreen.Settings && next == MenuScreen.Pause)
            {
                GameSettings.Save();
            }

            screen = next;
            screenT = 0f;
            capturing = false;
            activeSlider = -1;
            scrollDrag = false;
        }

        private bool CanOpen()
        {
            ResolveRefs();
            if (ShipTablet.AnyExpanded || BuilderUI.AnyOpen || CheatMenu.AnyOpen)
            {
                return false;
            }

            return controller == null || (!controller.InScreenFocus && !controller.InputBlocked);
        }

        private void ResolveRefs()
        {
            if (runner == null)
            {
                runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (controller == null)
            {
                controller = FindAnyObjectByType<PlayerController>();
            }

            if (view == null)
            {
                view = FindAnyObjectByType<FirstPersonCamera>();
            }
        }

        private void Open()
        {
            ResolveRefs();
            IsOpen = true;
            capturing = false;
            confirmQuit = false;
            screen = MenuScreen.Pause;
            openT = 0f;
            screenT = 0f;
            fade = 0f;

            if (controller != null)
            {
                controller.InputBlocked = true;
            }

            cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            if (view != null)
            {
                lockBefore = view.LockCursor;
                view.LockCursor = false;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (runner != null && GameSettings.Data.pauseOnMenu && !runner.Paused)
            {
                runner.Paused = true;
                pausedByMenu = true;
            }

            resolutions = SettingsApplier.GetResolutions();
            resolutionLabels = new string[resolutions.Count];
            for (int i = 0; i < resolutions.Count; i++)
            {
                resolutionLabels[i] = resolutions[i].x + " × " + resolutions[i].y;
            }

            SyncStagedDisplay();
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            capturing = false;
            activeSlider = -1;

            if (controller != null)
            {
                controller.InputBlocked = false;
            }

            if (view != null)
            {
                view.LockCursor = lockBefore;
            }

            if (cursorWasLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (pausedByMenu && runner != null)
            {
                runner.Paused = false;
            }

            pausedByMenu = false;
            GameSettings.Save();
        }

        private void OnDisable()
        {
            Close();
        }

        private void SyncStagedDisplay()
        {
            SettingsData d = GameSettings.Data;
            stageMode = Mathf.Clamp(d.screenMode, 0, 2);
            stageRes = 0;
            int best = int.MaxValue;
            for (int i = 0; i < resolutions.Count; i++)
            {
                int diff = Mathf.Abs(resolutions[i].x - d.resWidth) + Mathf.Abs(resolutions[i].y - d.resHeight);
                if (diff < best)
                {
                    best = diff;
                    stageRes = i;
                }
            }
        }

        private static void QuitGame()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─── Frame ───────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            Event e = Event.current;
            ui = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
            MenuTheme.Ensure(ui);

            if (e.type == EventType.Repaint)
            {
                float dt = Time.unscaledDeltaTime;
                openT += dt;
                screenT += dt;
                tabT = Mathf.Min(1f, tabT + (dt * 7f));
                fade = Mathf.Clamp01(openT / 0.16f);
            }

            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            GUI.depth = -10000;

            float vw = Screen.width / ui;
            float vh = Screen.height / ui;
            SettingsApplier applier = SettingsApplier.Instance;
            modal = confirmQuit || (applier != null && applier.RevertPending);
            mouseInView = true;
            measuring = false;
            hid = 0;
            localFade = 1f;

            if (capturing)
            {
                HandleCapture(e);
            }

            // Backdrop: dim.
            if (e.type == EventType.Repaint)
            {
                var full = new Rect(0f, 0f, Screen.width, Screen.height);
                GUI.color = new Color(0.01f, 0.015f, 0.035f, 0.62f * fade);
                GUI.DrawTexture(full, Texture2D.whiteTexture);
            }

            if (screen == MenuScreen.Pause)
            {
                DrawPause(vw, vh);
            }
            else
            {
                DrawSettings(vw, vh);
            }

            DrawToast(vw, vh);
            if (modal)
            {
                DrawModal(vw, vh, applier);
            }

            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        // ─── Primitives ──────────────────────────────────────────────────────

        private Rect P(Rect r)
        {
            return new Rect(Mathf.Round(r.x * ui), Mathf.Round(r.y * ui), Mathf.Round(r.width * ui), Mathf.Round(r.height * ui));
        }

        private void SetCol(Color c, float extra = 1f)
        {
            GUI.color = new Color(c.r, c.g, c.b, c.a * fade * localFade * extra);
        }

        private void Box(Rect r, int radius, Color c, bool ring = false)
        {
            if (measuring)
            {
                return;
            }

            // A 9-slice texture needs the rect to be at least (2r+2) px each way.
            radius = Mathf.Min(radius, Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(r.width, r.height) - 3f) * 0.5f)));
            SetCol(c);
            GUI.Box(P(r), GUIContent.none, ring ? MenuTheme.Ring(radius) : MenuTheme.Fill(radius));
        }

        private void Txt(Rect r, string text, int size, bool bold, TextAnchor anchor, Color c, bool wrap = false)
        {
            if (measuring)
            {
                return;
            }

            SetCol(c);
            GUI.Label(P(r), text, MenuTheme.TextStyle(size, bold, anchor, wrap));
        }

        private void Rule(Rect r, Color c)
        {
            if (measuring)
            {
                return;
            }

            SetCol(c);
            GUI.DrawTexture(P(r), Texture2D.whiteTexture);
        }

        private void Dot(Vector2 center, float radius, Color c)
        {
            if (measuring)
            {
                return;
            }

            SetCol(c);
            GUI.DrawTexture(P(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f)), MenuTheme.Disc);
        }

        private static Color Lerp(Color a, Color b, float t)
        {
            return Color.Lerp(a, b, t);
        }

        private static Color Alpha(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, c.a * a);
        }

        // ─── Input helpers ───────────────────────────────────────────────────

        private Vector2 MouseV
        {
            get { return Event.current.mousePosition / ui; }
        }

        private bool Hover(Rect r)
        {
            return !modal && mouseInView && r.Contains(MouseV);
        }

        /// <summary>1 for a left click, 2 for a right click inside the rect (the event is consumed).</summary>
        private int Click(Rect r)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && !modal && mouseInView && r.Contains(MouseV)
                && (e.button == 0 || e.button == 1))
            {
                e.Use();
                return e.button + 1;
            }

            return 0;
        }

        private float Smooth(float current, float target, float speed)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return current;
            }

            return Mathf.MoveTowards(current, target, Time.unscaledDeltaTime * speed);
        }

        private float HoverAnim(Rect r, bool enabled = true)
        {
            if (measuring)
            {
                return 0f;
            }

            int id = Mathf.Min(hid++, hov.Length - 1);
            hov[id] = Smooth(hov[id], enabled && Hover(r) ? 1f : 0f, 9f);
            return hov[id];
        }

        // ─── Buttons ─────────────────────────────────────────────────────────

        private bool Button(Rect r, string text, BtnKind kind, int size = 16, bool enabled = true)
        {
            if (measuring)
            {
                return false;
            }

            float h = HoverAnim(r, enabled);
            Color fill;
            Color ring;
            Color label;
            switch (kind)
            {
                case BtnKind.Primary:
                    fill = Lerp(MenuTheme.AccentDark, MenuTheme.Accent, h);
                    ring = Alpha(Color.white, 0.10f + (0.15f * h));
                    label = Color.white;
                    break;
                case BtnKind.Danger:
                    fill = Lerp(MenuTheme.Card, new Color(0.26f, 0.10f, 0.12f, 1f), h);
                    ring = Lerp(Alpha(MenuTheme.Danger, 0.35f), MenuTheme.Danger, h);
                    label = MenuTheme.Danger;
                    break;
                case BtnKind.Ghost:
                    fill = Alpha(Color.white, 0.07f * h);
                    ring = Lerp(MenuTheme.Hair, MenuTheme.Accent, h);
                    label = Lerp(MenuTheme.Muted, MenuTheme.Text, h);
                    break;
                default:
                    fill = Lerp(MenuTheme.Card, new Color(0.135f, 0.160f, 0.215f, 1f), h);
                    ring = Lerp(MenuTheme.HairStrong, MenuTheme.Accent, h);
                    label = MenuTheme.Text;
                    break;
            }

            if (!enabled)
            {
                fill = Alpha(MenuTheme.Card, 0.6f);
                ring = MenuTheme.Hair;
                label = MenuTheme.Subtle;
            }

            int radius = Mathf.RoundToInt(r.height * 0.28f);
            Box(r, radius, fill);
            Box(r, radius, ring, true);
            Txt(r, text, size, kind != BtnKind.Ghost, TextAnchor.MiddleCenter, label);
            return enabled && Click(r) == 1;
        }

        // ─── Pause screen ────────────────────────────────────────────────────

        private void DrawPause(float vw, float vh)
        {
            float cx = vw * 0.5f;
            const float bw = 400f;
            const float bh = 60f;
            const float gap = 14f;
            string[] labels = { "Продолжить", "Настройки", "Выйти из игры" };
            BtnKind[] kinds = { BtnKind.Primary, BtnKind.Normal, BtnKind.Danger };

            float headerH = 150f;
            float total = headerH + (labels.Length * bh) + ((labels.Length - 1) * gap);
            float top = Mathf.Max(24f, (vh - total) * 0.5f);

            localFade = Mathf.Clamp01(screenT / 0.18f);
            Txt(new Rect(cx - 300f, top, 600f, 22f), Application.productName.ToUpperInvariant(), 14, true, TextAnchor.MiddleCenter, MenuTheme.Muted);
            Txt(new Rect(cx - 300f, top + 24f, 600f, 70f), "ПАУЗА", 56, true, TextAnchor.MiddleCenter, MenuTheme.Text);
            Rule(new Rect(cx - 28f, top + 104f, 56f, 3f), MenuTheme.Accent);

            float by = top + headerH;
            for (int i = 0; i < labels.Length; i++)
            {
                // Staggered fade-in.
                localFade = Mathf.Clamp01((screenT - (i * 0.05f)) / 0.2f);
                var r = new Rect(cx - (bw * 0.5f), by + (i * (bh + gap)), bw, bh);
                bool hovered = Hover(r);
                if (Button(r, labels[i], kinds[i], 21))
                {
                    switch (i)
                    {
                        case 0: Close(); return;
                        case 1: tab = Mathf.Clamp(tab, 0, 3); GoTo(MenuScreen.Settings); return;
                        default: confirmQuit = true; break;
                    }
                }

                if (hovered && kinds[i] != BtnKind.Primary)
                {
                    Txt(new Rect(r.xMax - 46f, r.y, 30f, r.height), "›", 28, true, TextAnchor.MiddleCenter,
                        kinds[i] == BtnKind.Danger ? MenuTheme.Danger : MenuTheme.Accent);
                }
            }

            localFade = Mathf.Clamp01((screenT - 0.2f) / 0.3f);
            Txt(new Rect(0f, vh - 56f, vw, 22f), "Esc — продолжить", 14, false, TextAnchor.MiddleCenter, MenuTheme.Subtle);
            Txt(new Rect(vw - 220f, vh - 44f, 196f, 20f), "v" + Application.version, 12, false, TextAnchor.MiddleRight, MenuTheme.Subtle);
            localFade = 1f;
        }

        // ─── Settings screen ─────────────────────────────────────────────────

        private void DrawSettings(float vw, float vh)
        {
            float width = Mathf.Min(1040f, vw - 80f);
            float height = Mathf.Min(780f, vh - 70f);
            var panel = new Rect((vw - width) * 0.5f, (vh - height) * 0.5f, width, height);

            localFade = Mathf.Clamp01(screenT / 0.16f);

            if (Event.current.type == EventType.Repaint)
            {
                SetCol(Color.white);
                GUI.Box(P(new Rect(panel.x - 18f, panel.y - 18f, panel.width + 36f, panel.height + 36f)),
                    GUIContent.none, MenuTheme.Glow);
            }

            Box(panel, 22, MenuTheme.Panel);
            Box(panel, 22, MenuTheme.HairStrong, true);

            // Header: back · title · reset tab
            if (Button(new Rect(panel.x + 24f, panel.y + 22f, 120f, 40f), "‹  Назад", BtnKind.Ghost, 16))
            {
                GoTo(MenuScreen.Pause);
                return;
            }

            Txt(new Rect(panel.x + 160f, panel.y + 18f, panel.width - 320f, 48f), "НАСТРОЙКИ", 28, true, TextAnchor.MiddleCenter, MenuTheme.Text);
            if (Button(new Rect(panel.xMax - 204f, panel.y + 22f, 180f, 40f), "Сбросить вкладку", BtnKind.Ghost, 15))
            {
                GameSettings.ResetCategory(TabIds[tab]);
                if (tab == 0)
                {
                    SettingsApplier.Instance?.ChangePreset(GameSettings.Data.quality);
                }
                else if (tab == 1)
                {
                    ApplyMenuPause();
                }

                SyncStagedDisplay();
                Toast("Вкладка «" + TabNames[tab] + "» сброшена");
            }

            // Tabs
            DrawTabs(panel);

            // Content
            var viewRect = new Rect(panel.x + 28f, panel.y + 150f, panel.width - 56f, panel.height - 150f - 26f);
            float contentW = viewRect.width - 20f;

            float maxScroll = 0f;
            {
                // Measure pass: heights of the cards and of the whole page.
                measuring = true;
                BeginLayout(contentW);
                DrawTabContent();
                contentHeights[tab] = y;
                measuring = false;
                maxScroll = Mathf.Max(0f, contentHeights[tab] - viewRect.height + 6f);
            }

            Event e = Event.current;
            bool inView = !modal && viewRect.Contains(MouseV);
            if (e.type == EventType.ScrollWheel && inView)
            {
                scrolls[tab] += e.delta.y * 44f;
                e.Use();
            }

            scrolls[tab] = Mathf.Clamp(scrolls[tab], 0f, maxScroll);

            localFade = Mathf.Clamp01(screenT / 0.16f) * Mathf.Lerp(0.35f, 1f, tabT);
            mouseInView = inView;
            GUI.BeginGroup(P(viewRect));
            BeginLayout(contentW);
            y = -scrolls[tab];
            DrawTabContent();
            GUI.EndGroup();
            mouseInView = true;
            localFade = Mathf.Clamp01(screenT / 0.16f);

            DrawScrollbar(viewRect, maxScroll);
        }

        private void DrawTabs(Rect panel)
        {
            const float tabW = 168f;
            float total = tabW * TabNames.Length;
            float x0 = panel.x + ((panel.width - total) * 0.5f);
            float ty = panel.y + 88f;

            float targetX = x0 + (tab * tabW);
            if (tabUnderline < 0f)
            {
                tabUnderline = targetX;
            }

            if (Event.current.type == EventType.Repaint)
            {
                tabUnderline = Mathf.Lerp(tabUnderline, targetX, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 16f));
            }

            Rule(new Rect(panel.x + 28f, ty + 46f, panel.width - 56f, 1f), MenuTheme.Hair);

            for (int i = 0; i < TabNames.Length; i++)
            {
                var r = new Rect(x0 + (i * tabW), ty, tabW, 46f);
                float h = HoverAnim(r);
                Color c = i == tab ? MenuTheme.Text : Lerp(MenuTheme.Muted, MenuTheme.Text, h);
                Txt(r, TabNames[i], 18, i == tab, TextAnchor.MiddleCenter, c);
                if (Click(r) == 1 && i != tab)
                {
                    tab = i;
                    tabT = 0f;
                    capturing = false;
                    activeSlider = -1;
                }
            }

            Rule(new Rect(tabUnderline + 26f, ty + 44f, tabW - 52f, 3f), MenuTheme.Accent);
        }

        private void DrawScrollbar(Rect viewRect, float maxScroll)
        {
            if (maxScroll <= 0f)
            {
                return;
            }

            float trackH = viewRect.height;
            float thumbH = Mathf.Max(40f, trackH * trackH / (trackH + maxScroll));
            float thumbY = viewRect.y + ((trackH - thumbH) * (scrolls[tab] / maxScroll));
            var thumb = new Rect(viewRect.xMax - 8f, thumbY, 6f, thumbH);
            var hit = new Rect(viewRect.xMax - 16f, thumbY, 18f, thumbH);

            Event e = Event.current;
            bool hot = scrollDrag || Hover(hit);
            Box(new Rect(viewRect.xMax - 8f, viewRect.y, 6f, trackH), 3, MenuTheme.Hair);
            Box(thumb, 3, hot ? MenuTheme.Accent : MenuTheme.HairStrong);

            if (e.type == EventType.MouseDown && e.button == 0 && !modal && hit.Contains(MouseV))
            {
                scrollDrag = true;
                scrollDragOffset = MouseV.y - thumbY;
                e.Use();
            }

            if (scrollDrag && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            {
                float newY = Mathf.Clamp(MouseV.y - scrollDragOffset, viewRect.y, viewRect.y + trackH - thumbH);
                scrolls[tab] = ((newY - viewRect.y) / Mathf.Max(1f, trackH - thumbH)) * maxScroll;
                e.Use();
            }

            if (e.rawType == EventType.MouseUp)
            {
                scrollDrag = false;
            }
        }

        private void BeginLayout(float width)
        {
            rowW = width;
            y = 0f;
            cardIndex = 0;
            firstRow = true;
        }

        private void DrawTabContent()
        {
            switch (tab)
            {
                case 0: DrawGraphics(); break;
                case 1: DrawView(); break;
                case 2: DrawControls(); break;
                default: DrawAudio(); break;
            }
        }

        // ─── Cards and rows ──────────────────────────────────────────────────

        private void BeginCard(string title)
        {
            if (cardIndex > 0)
            {
                y += 18f;
            }

            cardTop = y;
            int idx = Mathf.Min(cardIndex, 15);
            float h = cardHeights[tab, idx];
            if (!measuring && h > 0f)
            {
                var r = new Rect(0f, cardTop, rowW, h);
                Box(r, 14, MenuTheme.Card);
                Box(r, 14, MenuTheme.Hair, true);
            }

            Txt(new Rect(RowPad, y + 16f, rowW - (RowPad * 2f), 20f), title.ToUpperInvariant(), 12, true, TextAnchor.MiddleLeft, MenuTheme.Accent);
            y += 46f;
            firstRow = true;
        }

        private void EndCard()
        {
            y += 10f;
            cardHeights[tab, Mathf.Min(cardIndex, 15)] = y - cardTop;
            cardIndex++;
        }

        /// <summary>Starts a row and returns the control rect (right column, 36 high).</summary>
        private Rect Row(string label, string desc, bool enabled, float height = 0f)
        {
            float h = height > 0f ? height : (desc != null ? 64f : 54f);
            if (!firstRow)
            {
                Rule(new Rect(RowPad, y, rowW - (RowPad * 2f), 1f), MenuTheme.Hair);
            }

            firstRow = false;
            float labelW = rowW - (RowPad * 2f) - CtrlW - 24f;
            Color main = enabled ? MenuTheme.Text : MenuTheme.Subtle;
            if (desc != null)
            {
                Txt(new Rect(RowPad, y + 10f, labelW, 24f), label, 16, false, TextAnchor.MiddleLeft, main);
                Txt(new Rect(RowPad, y + 34f, labelW, 20f), desc, 13, false, TextAnchor.MiddleLeft,
                    enabled ? MenuTheme.Muted : MenuTheme.Subtle);
            }
            else
            {
                Txt(new Rect(RowPad, y, labelW, h), label, 16, false, TextAnchor.MiddleLeft, main);
            }

            var control = new Rect(rowW - RowPad - CtrlW, y + ((h - 36f) * 0.5f), CtrlW, 36f);
            y += h;
            return control;
        }

        private void NoteRow(string text)
        {
            if (!firstRow)
            {
                Rule(new Rect(RowPad, y, rowW - (RowPad * 2f), 1f), MenuTheme.Hair);
            }

            firstRow = false;
            GUIStyle style = MenuTheme.TextStyle(13, false, TextAnchor.UpperLeft, true);
            float w = rowW - (RowPad * 2f);
            float h = Mathf.Max(20f, style.CalcHeight(new GUIContent(text), w * ui) / ui);
            Txt(new Rect(RowPad, y + 12f, w, h), text, 13, false, TextAnchor.UpperLeft, MenuTheme.Muted, true);
            y += h + 24f;
        }

        // ─── Controls ────────────────────────────────────────────────────────

        private static int MaxLength(string[] options)
        {
            int max = 0;
            foreach (string s in options)
            {
                max = Mathf.Max(max, s.Length);
            }

            return max;
        }

        /// <summary>Segmented control (few short options) or a stepper (many / long). True when changed.</summary>
        private bool Choice(string label, string desc, string[] options, ref int index, bool enabled = true)
        {
            Rect c = Row(label, desc, enabled);
            int count = options != null ? options.Length : 0;
            if (measuring || count == 0)
            {
                return false;
            }

            bool segmented = count <= 4 && MaxLength(options) <= 12;
            return segmented ? Segmented(c, options, ref index, enabled) : Stepper(c, options, ref index, enabled);
        }

        private bool Segmented(Rect c, string[] options, ref int index, bool enabled)
        {
            int id = Mathf.Min(hid++, anim2.Length - 1);
            int count = options.Length;
            index = Mathf.Clamp(index, 0, count - 1);
            if (!anim2Init[id])
            {
                anim2Init[id] = true;
                anim2[id] = index;
            }

            if (Event.current.type == EventType.Repaint)
            {
                anim2[id] = Mathf.Lerp(anim2[id], index, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            }

            Box(c, 12, MenuTheme.Well);
            Box(c, 12, MenuTheme.Hair, true);

            float segW = (c.width - 6f) / count;
            var highlight = new Rect(c.x + 3f + (anim2[id] * segW), c.y + 3f, segW, c.height - 6f);
            Box(highlight, 9, enabled ? MenuTheme.Accent : MenuTheme.Subtle);

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                var r = new Rect(c.x + 3f + (i * segW), c.y, segW, c.height);
                bool selected = i == index;
                Color tc = selected ? Color.white : (enabled && Hover(r) ? MenuTheme.Text : MenuTheme.Muted);
                if (!enabled)
                {
                    tc = MenuTheme.Subtle;
                }

                Txt(r, options[i], 14, selected, TextAnchor.MiddleCenter, tc);
                if (enabled && i != index && Click(r) == 1)
                {
                    index = i;
                    changed = true;
                }
            }

            return changed;
        }

        private bool Stepper(Rect c, string[] options, ref int index, bool enabled)
        {
            int count = options.Length;
            index = Mathf.Clamp(index, 0, count - 1);
            Box(c, 12, MenuTheme.Well);
            Box(c, 12, MenuTheme.Hair, true);

            var left = new Rect(c.x, c.y, 40f, c.height);
            var right = new Rect(c.xMax - 40f, c.y, 40f, c.height);
            var mid = new Rect(c.x + 40f, c.y, c.width - 80f, c.height);
            float hl = HoverAnim(left, enabled && count > 1);
            float hr = HoverAnim(right, enabled && count > 1);

            Color idle = enabled ? MenuTheme.Muted : MenuTheme.Subtle;
            Txt(left, "‹", 26, true, TextAnchor.MiddleCenter, Lerp(idle, MenuTheme.Accent, hl));
            Txt(right, "›", 26, true, TextAnchor.MiddleCenter, Lerp(idle, MenuTheme.Accent, hr));
            Txt(mid, options[index], 15, true, TextAnchor.MiddleCenter, enabled ? MenuTheme.Text : MenuTheme.Subtle);

            if (!enabled || count < 2)
            {
                return false;
            }

            if (Click(left) == 1)
            {
                index = (index - 1 + count) % count;
                return true;
            }

            if (Click(right) == 1 || Click(mid) == 1)
            {
                index = (index + 1) % count;
                return true;
            }

            return false;
        }

        private bool SliderRow(string label, string desc, ref float value, float min, float max, float step,
            string valueText, bool enabled = true)
        {
            Rect c = Row(label, desc, enabled);
            if (measuring)
            {
                return false;
            }

            int id = hid++;
            const float readoutW = 92f;
            const float pad = 11f;
            var track = new Rect(c.x, c.y + 13f, c.width - readoutW - 10f, 10f);
            float span = Mathf.Max(1f, track.width - (pad * 2f));
            float t = Mathf.InverseLerp(min, max, value);
            Color accent = enabled ? MenuTheme.Accent : MenuTheme.Subtle;

            var hit = new Rect(track.x - 6f, c.y, track.width + 12f, c.height);
            bool hot = enabled && (activeSlider == id || Hover(hit));
            float h = Smooth(hov[Mathf.Min(id, hov.Length - 1)], hot ? 1f : 0f, 10f);
            hov[Mathf.Min(id, hov.Length - 1)] = h;

            // Input
            Event e = Event.current;
            bool changed = false;
            if (enabled && e.type == EventType.MouseDown && e.button == 0 && Hover(hit))
            {
                activeSlider = id;
            }

            if (enabled && activeSlider == id && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                float nt = Mathf.Clamp01((MouseV.x - (track.x + pad)) / span);
                float next = Mathf.Lerp(min, max, nt);
                if (step > 0f)
                {
                    next = Mathf.Clamp((Mathf.Round((next - min) / step) * step) + min, min, max);
                }

                if (!Mathf.Approximately(next, value))
                {
                    value = next;
                    changed = true;
                }

                e.Use();
            }

            if (e.rawType == EventType.MouseUp && activeSlider == id)
            {
                activeSlider = -1;
            }

            // Draw
            t = Mathf.InverseLerp(min, max, value);
            float thumbX = track.x + pad + (t * span);
            Box(track, 4, MenuTheme.Well);
            Box(track, 4, MenuTheme.Hair, true);
            Box(new Rect(track.x, track.y, Mathf.Max(8f, thumbX - track.x), track.height), 4, accent);

            float cy = track.y + (track.height * 0.5f);
            Dot(new Vector2(thumbX, cy), 11f + (1.5f * h), Alpha(accent, 0.28f * h));
            Dot(new Vector2(thumbX, cy), 9f + (1f * h), accent);
            Dot(new Vector2(thumbX, cy), 5.5f, Color.white);

            Txt(new Rect(c.xMax - readoutW, c.y, readoutW, c.height), valueText, 15, true, TextAnchor.MiddleRight,
                enabled ? MenuTheme.Text : MenuTheme.Subtle);
            return changed;
        }

        private bool ToggleRow(string label, string desc, ref bool value, bool enabled = true)
        {
            Rect c = Row(label, desc, enabled);
            if (measuring)
            {
                return false;
            }

            int id = Mathf.Min(hid++, anim2.Length - 1);
            if (!anim2Init[id])
            {
                anim2Init[id] = true;
                anim2[id] = value ? 1f : 0f;
            }

            if (Event.current.type == EventType.Repaint)
            {
                anim2[id] = Mathf.MoveTowards(anim2[id], value ? 1f : 0f, Time.unscaledDeltaTime * 9f);
            }

            float p = anim2[id];
            var sw = new Rect(c.xMax - 58f, c.y + 2f, 58f, 32f);
            bool hover = enabled && Hover(c);

            Txt(new Rect(c.x, c.y, c.width - 74f, c.height), value ? "Вкл" : "Выкл", 14, false, TextAnchor.MiddleRight,
                enabled ? (value ? MenuTheme.Text : MenuTheme.Muted) : MenuTheme.Subtle);

            Color off = new Color(0.17f, 0.19f, 0.25f, 1f);
            Box(sw, 15, Lerp(off, enabled ? MenuTheme.Accent : MenuTheme.Subtle, p));
            Box(sw, 15, hover ? MenuTheme.HairStrong : MenuTheme.Hair, true);
            float kx = sw.x + 16f + (p * (sw.width - 32f));
            Dot(new Vector2(kx, sw.y + 16f), 11.5f + (hover ? 0.8f : 0f), enabled ? Color.white : MenuTheme.Muted);

            if (enabled && Click(c) == 1)
            {
                value = !value;
                return true;
            }

            return false;
        }

        // ─── Graphics ────────────────────────────────────────────────────────

        private void DrawGraphics()
        {
            SettingsData d = GameSettings.Data;
            bool changed = false;
            SettingsApplier applier = SettingsApplier.Instance;

            BeginCard("Дисплей");
            Choice("Режим экрана", null, ScreenModes, ref stageMode);
            Choice("Разрешение", stageMode == 1 ? "В режиме без рамки используется разрешение рабочего стола" : null,
                resolutionLabels, ref stageRes, stageMode != 1);
            bool displayDirty = stageMode != d.screenMode
                || (stageMode != 1 && stageRes < resolutions.Count
                    && (resolutions[stageRes].x != d.resWidth || resolutions[stageRes].y != d.resHeight));
            if (displayDirty && applier != null)
            {
                Rect a = Row("Новые параметры экрана ещё не применены", null, true);
                if (Button(a, "Применить", BtnKind.Primary, 15))
                {
                    Vector2Int r = stageMode == 1
                        ? new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height)
                        : resolutions[Mathf.Clamp(stageRes, 0, resolutions.Count - 1)];
                    applier.ApplyDisplay(r.x, r.y, stageMode);
                }
            }

            int vsync = d.vsync;
            if (Choice("Вертикальная синхронизация", "Убирает разрывы кадра, ограничивает FPS частотой монитора", OffOn, ref vsync))
            {
                d.vsync = vsync;
                changed = true;
            }

            int capIndex = NearestIndex(FrameCaps, d.frameCap);
            if (Choice("Лимит FPS", d.vsync > 0 ? "Не действует при включённой синхронизации" : "Ограничивает частоту кадров",
                FrameCapNames, ref capIndex, d.vsync == 0))
            {
                d.frameCap = FrameCaps[capIndex];
                changed = true;
            }

            EndCard();

            BeginCard("Качество");
            string[] presets = QualitySettings.names;
            int preset = Mathf.Clamp(d.quality, 0, Mathf.Max(0, presets.Length - 1));
            if (Choice("Пресет качества", "Набор настроек HDRP; параметры ниже действуют поверх него", presets, ref preset)
                && applier != null)
            {
                applier.ChangePreset(preset);
            }

            int aa = Mathf.Clamp(d.antiAliasing, 0, 3);
            if (Choice("Сглаживание", "SMAA — чётко, TAA — мягче, но может смазывать", AaNames, ref aa))
            {
                d.antiAliasing = aa;
                changed = true;
            }

            bool dynRes = SettingsApplier.DynamicResolutionAvailable;
            float scale = d.renderScale;
            if (SliderRow("Масштаб рендера", dynRes ? "Ниже 100 % быстрее, но картинка мягче" : "Недоступно: в HDRP-ассете выключено Dynamic Resolution",
                ref scale, 50f, 100f, 5f, Mathf.RoundToInt(scale) + " %", dynRes))
            {
                d.renderScale = Mathf.RoundToInt(scale);
                changed = true;
            }

            float terrain = d.terrainDetail;
            if (SliderRow("Детализация рельефа", "Как далеко рельеф рисуется подробной сеткой. Сильно влияет на FPS",
                ref terrain, 0.5f, 2f, 0.1f, "×" + terrain.ToString("0.0"), true))
            {
                d.terrainDetail = terrain;
                changed = true;
            }

            int grass = Mathf.Clamp(d.grassQuality, 0, 4);
            if (Choice("Качество травы", "Плотность и дальность травы. «Отключена» убирает её полностью",
                GrassNames, ref grass))
            {
                d.grassQuality = grass;
                changed = true;
            }

            int tex = Mathf.Clamp(d.textureQuality, 0, 2);
            if (Choice("Качество текстур", "Среднее и низкое экономят видеопамять", TextureNames, ref tex))
            {
                d.textureQuality = tex;
                changed = true;
            }

            int aniso = Mathf.Clamp(d.anisotropic, 0, 2);
            if (Choice("Анизотропная фильтрация", "Чёткость текстур под острым углом, например земли вдаль", AnisoNames, ref aniso))
            {
                d.anisotropic = aniso;
                changed = true;
            }

            float lod = d.lodBias;
            if (SliderRow("Дальность детализации объектов", "Когда модели переключаются на упрощённые (LOD)",
                ref lod, 0.5f, 3f, 0.1f, "×" + lod.ToString("0.0"), true))
            {
                d.lodBias = lod;
                changed = true;
            }

            float shadow = d.shadowDistance;
            if (SliderRow("Дальность теней", "«Авто» — как настроено в сцене",
                ref shadow, 0f, 2000f, 50f, shadow < 1f ? "Авто" : Mathf.RoundToInt(shadow) + " м", true))
            {
                d.shadowDistance = shadow;
                changed = true;
            }

            EndCard();

            BeginCard("Эффекты");
            int blur = d.motionBlur;
            if (Choice("Размытие в движении", null, BlurNames, ref blur))
            {
                d.motionBlur = blur;
                changed = true;
            }

            changed |= ToggleRow("Свечение (Bloom)", "Ореол вокруг ярких объектов", ref d.bloom);
            changed |= ToggleRow("Плёночное зерно", null, ref d.filmGrain);
            changed |= ToggleRow("Хроматическая аберрация", "Цветная окантовка по краям кадра", ref d.chromaticAberration);
            EndCard();

            if (changed)
            {
                GameSettings.NotifyChanged();
            }
        }

        // ─── View ────────────────────────────────────────────────────────────

        private void DrawView()
        {
            SettingsData d = GameSettings.Data;
            bool changed = false;

            BeginCard("Камера");
            float fov = d.fov > 0f ? d.fov : (view != null ? view.DefaultFieldOfView : 60f);
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.7778f;
            float horizontal = 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
            if (SliderRow("Поле зрения", "По горизонтали ≈ " + Mathf.RoundToInt(horizontal) + "° на этом экране",
                ref fov, 40f, 110f, 1f, Mathf.RoundToInt(fov) + "°", true))
            {
                d.fov = fov;
                changed = true;
            }

            float sens = d.mouseSensitivity;
            if (SliderRow("Чувствительность мыши", null, ref sens, 0.1f, 3f, 0.05f, "×" + sens.ToString("0.00"), true))
            {
                d.mouseSensitivity = sens;
                changed = true;
            }

            changed |= ToggleRow("Инвертировать вертикаль", "Мышь вверх — взгляд вниз", ref d.invertY);
            changed |= ToggleRow("Приближение к экранам", "Удержание клавиши «Приблизиться»", ref d.lean);
            EndCard();

            BeginCard("Прицел");
            changed |= ToggleRow("Показывать прицел", null, ref d.crosshair);
            float cs = d.crosshairScale;
            if (SliderRow("Размер прицела", null, ref cs, 0.5f, 2.5f, 0.1f, "×" + cs.ToString("0.0"), d.crosshair))
            {
                d.crosshairScale = cs;
                changed = true;
            }

            EndCard();

            BeginCard("Меню");
            bool pauseBefore = d.pauseOnMenu;
            changed |= ToggleRow("Пауза симуляции", "Останавливать время, пока открыто меню", ref d.pauseOnMenu);
            EndCard();

            if (pauseBefore != d.pauseOnMenu)
            {
                ApplyMenuPause();
            }

            if (changed)
            {
                GameSettings.NotifyChanged();
            }
        }

        /// <summary>The "pause while the menu is open" toggle works immediately, not next time.</summary>
        private void ApplyMenuPause()
        {
            if (runner == null)
            {
                return;
            }

            if (GameSettings.Data.pauseOnMenu)
            {
                if (!runner.Paused)
                {
                    runner.Paused = true;
                    pausedByMenu = true;
                }
            }
            else if (pausedByMenu)
            {
                runner.Paused = false;
                pausedByMenu = false;
            }
        }

        // ─── Audio ───────────────────────────────────────────────────────────

        private void DrawAudio()
        {
            SettingsData d = GameSettings.Data;
            bool changed = false;

            BeginCard("Громкость");
            changed |= VolumeRow("Общая", ref d.volMaster);
            changed |= VolumeRow("Музыка", ref d.volMusic);
            changed |= VolumeRow("Эффекты", ref d.volEffects);
            changed |= VolumeRow("Окружение", ref d.volAmbient);
            changed |= VolumeRow("Интерфейс", ref d.volInterface);
            EndCard();

            BeginCard("Прочее");
            changed |= ToggleRow("Без звука в фоне", "Выключать звук, когда окно игры не в фокусе", ref d.muteUnfocused);
            NoteRow("Звуков в игре пока нет, но ползунки уже работают и сохраняются. "
                + "Чтобы подключить звук, добавь на AudioSource компонент Game Audio Source и выбери канал.");
            EndCard();

            if (changed)
            {
                GameSettings.NotifyChanged();
            }
        }

        private bool VolumeRow(string label, ref float value)
        {
            float percent = value * 100f;
            if (SliderRow(label, null, ref percent, 0f, 100f, 1f, Mathf.RoundToInt(percent) + " %", true))
            {
                value = percent / 100f;
                return true;
            }

            return false;
        }

        // ─── Controls tab ────────────────────────────────────────────────────

        private void DrawControls()
        {
            Txt(new Rect(4f, y, rowW - 200f, 40f),
                "ЛКМ по клавише — назначить, ПКМ — очистить, Esc — отмена. У каждого действия две клавиши.",
                14, false, TextAnchor.MiddleLeft, MenuTheme.Muted, true);
            if (Button(new Rect(rowW - 170f, y + 2f, 170f, 38f), "Сбросить все", BtnKind.Normal, 15))
            {
                KeyBindings.ResetAll();
                Toast("Все клавиши сброшены");
            }

            y += 56f;

            for (int g = 0; g < ActionTable.GroupNames.Length; g++)
            {
                BeginCard(ActionTable.GroupNames[g]);
                foreach (ActionInfo info in ActionTable.All)
                {
                    if ((int)info.Group == g)
                    {
                        KeyRow(info);
                    }
                }

                EndCard();
            }
        }

        private void KeyRow(ActionInfo info)
        {
            const float resetW = 60f;
            const float slotW = 128f;
            Rect row = Row(info.Label, null, true, 52f);
            if (measuring)
            {
                return;
            }

            float rowY = row.y;
            float resetX = rowW - RowPad - resetW;
            float slot1X = resetX - 8f - slotW;
            float slot0X = slot1X - 8f - slotW;

            for (int slot = 0; slot < KeyBindings.Slots; slot++)
            {
                KeyCap(info, slot, new Rect(slot == 0 ? slot0X : slot1X, rowY, slotW, 36f));
            }

            bool isDefault = KeyBindings.IsDefault(info.Action);
            var resetRect = new Rect(resetX, rowY, resetW, 36f);
            float h = HoverAnim(resetRect, !isDefault);
            Txt(resetRect, "Сброс", 13, false, TextAnchor.MiddleCenter,
                isDefault ? MenuTheme.Subtle : Lerp(MenuTheme.Muted, MenuTheme.Accent, h));
            if (!isDefault && Click(resetRect) == 1)
            {
                KeyBindings.ResetAction(info.Action);
            }
        }

        private void KeyCap(ActionInfo info, int slot, Rect r)
        {
            KeyCode key = KeyBindings.Get(info.Action, slot);
            bool active = capturing && captureAction == info.Action && captureSlot == slot;
            float h = HoverAnim(r);

            bool conflict = !active && key != KeyCode.None && KeyBindings.FindConflict(info.Action, key, out _);
            string text;
            Color textColor = MenuTheme.Text;
            Color fill = Lerp(MenuTheme.Well, new Color(0.07f, 0.085f, 0.12f, 1f), h);
            Color ring = Lerp(MenuTheme.Hair, MenuTheme.Accent, h);

            if (active)
            {
                float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 6f));
                text = "Нажмите клавишу…";
                fill = Lerp(new Color(0.10f, 0.20f, 0.36f, 1f), new Color(0.13f, 0.27f, 0.48f, 1f), pulse);
                ring = MenuTheme.Accent;
                textColor = Color.white;
            }
            else
            {
                text = KeyBindings.Display(key);
                if (key == KeyCode.None)
                {
                    textColor = MenuTheme.Subtle;
                }

                if (conflict)
                {
                    ring = Lerp(Alpha(MenuTheme.Danger, 0.75f), MenuTheme.Danger, h);
                    textColor = MenuTheme.Danger;
                    text = "! " + text;
                }
            }

            Box(r, 10, fill);
            Box(r, 10, ring, true);
            Txt(r, text, active ? 13 : 14, true, TextAnchor.MiddleCenter, textColor);

            int click = Click(r);
            if (click == 1)
            {
                capturing = true;
                captureAction = info.Action;
                captureSlot = slot;
            }
            else if (click == 2)
            {
                KeyBindings.Set(info.Action, slot, KeyCode.None);
                capturing = false;
            }
        }

        private void HandleCapture(Event e)
        {
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    capturing = false;
                }
                else
                {
                    Assign(e.keyCode);
                }

                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button >= 1 && e.button <= 4)
            {
                Assign(KeyCode.Mouse0 + e.button);
                e.Use();
            }
        }

        private void Assign(KeyCode key)
        {
            GameAction action = captureAction;
            int slot = captureSlot;
            capturing = false;

            // The same key twice on one action makes no sense: clear the other slot.
            int other = 1 - slot;
            if (KeyBindings.Get(action, other) == key)
            {
                KeyBindings.Set(action, other, KeyCode.None);
            }

            if (KeyBindings.StealKey(action, key, out GameAction from))
            {
                Toast("«" + KeyBindings.Display(key) + "» перенесена с действия «" + ActionTable.Get(from).Label + "»");
            }

            KeyBindings.Set(action, slot, key);
        }

        private static int NearestIndex(int[] values, int target)
        {
            int best = 0;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < values.Length; i++)
            {
                int diff = Mathf.Abs(values[i] - target);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = i;
                }
            }

            return best;
        }

        // ─── Toast / dialogs ─────────────────────────────────────────────────

        private void Toast(string text)
        {
            toast = text;
            toastUntil = Time.unscaledTime + 3.5f;
        }

        private void DrawToast(float vw, float vh)
        {
            if (string.IsNullOrEmpty(toast) || Time.unscaledTime > toastUntil)
            {
                return;
            }

            float remaining = toastUntil - Time.unscaledTime;
            localFade = Mathf.Clamp01(remaining / 0.4f);
            GUIStyle style = MenuTheme.TextStyle(15, true, TextAnchor.MiddleCenter);
            float w = Mathf.Clamp((style.CalcSize(new GUIContent(toast)).x / ui) + 56f, 240f, vw - 80f);
            var r = new Rect((vw - w) * 0.5f, vh - 96f, w, 46f);
            Box(r, 14, new Color(0.10f, 0.12f, 0.17f, 0.98f));
            Box(r, 14, MenuTheme.HairStrong, true);
            Txt(r, toast, 15, true, TextAnchor.MiddleCenter, MenuTheme.Text);
            localFade = 1f;
        }

        private void DrawModal(float vw, float vh, SettingsApplier applier)
        {
            // Keep the dialog itself interactive while everything under it is not.
            bool wasModal = modal;
            modal = false;
            mouseInView = true;
            localFade = 1f;

            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f * fade);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            }

            var box = new Rect((vw - 560f) * 0.5f, (vh - 230f) * 0.5f, 560f, 230f);
            if (Event.current.type == EventType.Repaint)
            {
                SetCol(Color.white);
                GUI.Box(P(new Rect(box.x - 18f, box.y - 18f, box.width + 36f, box.height + 36f)), GUIContent.none, MenuTheme.Glow);
            }

            Box(box, 20, new Color(0.065f, 0.078f, 0.108f, 1f));
            Box(box, 20, MenuTheme.HairStrong, true);

            if (confirmQuit)
            {
                Txt(new Rect(box.x + 32f, box.y + 28f, box.width - 64f, 36f), "Выйти из игры?", 26, true, TextAnchor.MiddleLeft, MenuTheme.Text);
                Txt(new Rect(box.x + 32f, box.y + 72f, box.width - 64f, 50f), "Игра будет закрыта.", 16, false,
                    TextAnchor.UpperLeft, MenuTheme.Muted, true);
                if (Button(new Rect(box.x + 32f, box.yMax - 78f, 240f, 50f), "Отмена", BtnKind.Normal, 17))
                {
                    confirmQuit = false;
                }

                if (Button(new Rect(box.xMax - 272f, box.yMax - 78f, 240f, 50f), "Выйти", BtnKind.Danger, 17))
                {
                    QuitGame();
                }
            }
            else if (applier != null && applier.RevertPending)
            {
                Txt(new Rect(box.x + 32f, box.y + 28f, box.width - 64f, 36f), "Оставить новые параметры экрана?", 24, true,
                    TextAnchor.MiddleLeft, MenuTheme.Text);
                Txt(new Rect(box.x + 32f, box.y + 72f, box.width - 64f, 50f),
                    "Если ничего не нажать, через " + Mathf.CeilToInt(applier.RevertSecondsLeft) + " с вернутся прежние.",
                    16, false, TextAnchor.UpperLeft, MenuTheme.Muted, true);
                if (Button(new Rect(box.x + 32f, box.yMax - 78f, 240f, 50f), "Вернуть", BtnKind.Normal, 17))
                {
                    applier.RevertDisplay();
                    SyncStagedDisplay();
                }

                if (Button(new Rect(box.xMax - 272f, box.yMax - 78f, 240f, 50f), "Оставить", BtnKind.Primary, 17))
                {
                    applier.ConfirmDisplay();
                    SyncStagedDisplay();
                }
            }

            modal = wasModal;
        }
    }
}
