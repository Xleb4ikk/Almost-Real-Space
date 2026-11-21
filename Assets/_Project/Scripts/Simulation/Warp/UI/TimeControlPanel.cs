using Galilego.Building;
using Galilego.Tablet;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Time panel (T): rewind timeline + time acceleration. The panel draws a
    /// <see cref="TimeControlModel"/> and reports what the player did through its events;
    /// the rewind / warp / physics logic lives in <see cref="TimeControlRuntime"/>.
    ///
    ///   ┌ physics slot │ full warp ×10 … ×1M ┐   ← tab that sticks out of the window (top-left)
    ///   ├─────────────────────────────────────┴──────────────┐
    ///   │  ПЕРЕМОТКА                                  [T]    │
    ///   │                 ВРЕМЯ ПОЛЁТА  (main clock)         │
    ///   │   o──●──●──●──●──●──●──○──○──○  (timeline)         │
    ///   │                 ПЕРЕМОТАТЬ НА  (selected point)    │
    ///   │                                  [ ◀◀ Перемотать ] │
    ///   └────────────────────────────────────────────────────┘
    ///
    /// Same stack as the other menus: IMGUI + MenuTheme (generated rounded textures, OS font,
    /// no assets, no GUI matrix — all sizes are virtual px multiplied by the UI scale).
    /// While open: player input is blocked, the cursor is released. The simulation keeps running.
    /// Runs after SettingsMenu (-200) so Esc/T are seen in a consistent order.
    ///
    /// Mouse: click / drag on the timeline selects a point (snaps to points and to "now");
    /// right click on the physics slot opens its flyout. Keys: ←/→ select, Enter rewind,
    /// T / Esc close.
    /// </summary>
    [DefaultExecutionOrder(-190)]
    public sealed class TimeControlPanel : MonoBehaviour
    {
        /// <summary>Key that opens/closes the panel. NOTE: T is also the default key of SAS.</summary>
        private const KeyCode OpenKey = KeyCode.T;

        public static bool IsOpen { get; private set; }

        /// <summary>
        /// False on the frame the panel closed with its own key, so that T does not also
        /// reach gameplay (T is the default SAS key) — the panel hides before flight code
        /// polls the input in the same frame.
        /// </summary>
        private static int closedFrame = -1;

        /// <summary>Gameplay input must stay blocked: the panel is open or it just closed.</summary>
        internal static bool BlocksInput
        {
            get { return IsOpen || closedFrame == Time.frameCount; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsOpen = false;
            closedFrame = -1;
        }

        /// <summary>Physical warp values (x1 is the default of the slot, the rest live in the flyout).</summary>
        internal static readonly int[] PhysicalFactors = { 1, 2, 3, 4, 6 };

        /// <summary>Full warp rungs, in the order of the buttons.</summary>
        internal static readonly double[] FullFactors = { 10d, 50d, 100d, 500d, 1000d, 10000d, 100000d, 1000000d };

        private static readonly string[] FullLabels = { "×10", "×50", "×100", "×500", "×1K", "×10K", "×100K", "×1M" };

        // ─── Geometry (virtual px, window-local, origin = window top-left) ───
        private const float W = 940f;
        private const float H = 244f;
        private const float BottomMargin = 44f;
        private const int WinR = 18;

        private const float TabW = 596f;
        private const float TabH = 64f;       // part of the tab that sticks out above the window
        private const float TabOver = 22f;    // part of the tab hidden inside the window (> WinR)
        private const int TabR = 16;
        private const float TabPad = 16f;
        private const float CapY = -TabH + 9f;
        private const float BtnY = -TabH + 24f;
        private const float BtnH = 32f;
        private const float SlotW = 56f;
        private const float WarpBtnW = 58f;
        private const float WarpGap = 4f;

        private const float TrackX0 = 70f;
        private const float TrackX1 = W - 70f;
        private const float TrackY = 130f;

        private const float RewindW = 212f;
        private const float RewindH = 42f;

        // ─── Palette (extends MenuTheme) ─────────────────────────────────────
        private static readonly Color PhysCol = new Color(0.34f, 0.85f, 0.70f, 1f);
        private static readonly Color WarpCol = new Color(1.00f, 0.72f, 0.30f, 1f);
        private static readonly Color OnPhys = new Color(0.02f, 0.14f, 0.11f, 1f);
        private static readonly Color OnWarp = new Color(0.16f, 0.09f, 0.00f, 1f);
        private static readonly Color OnAccent = new Color(0.015f, 0.065f, 0.12f, 1f);
        private static readonly Color Hole = new Color(0.040f, 0.050f, 0.075f, 1f);
        private static readonly Color FlyoutBg = new Color(0.045f, 0.075f, 0.075f, 0.98f);

        // Control ids for hover animation
        private const int IdSlot = 0;
        private const int IdWarp0 = 1;        // 1..8
        private const int IdFly0 = 9;         // 9..12
        private const int IdRewind = 13;
        private const int IdCount = 16;

        // ─── State ───────────────────────────────────────────────────────────
        /// <summary>Наведение на слот, с: через это время вылет открывается сам.</summary>
        private const float SlotHoverOpenSeconds = 0.18f;

        // Open animation: a damped spring, not a tween — the panel is pulled up from below the
        // bottom edge of the screen and lands with a small overshoot. The whole travel is over
        // in ~0.08 s, it bobs ~10 px above its place at ~0.12 s and is still by ~0.2 s. Being a
        // spring, re-opening mid-close just reverses the motion instead of jumping.
        private const float SlideOmega = 40f;       // rad/s, higher = snappier
        private const float SlideZeta = 0.75f;      // < 1 → ~3 % overshoot (≈ 10 px) on landing

        private TimeControlModel model;
        private PlayerController controller;
        private bool cursorClaimed;

        private float slotHover;
        private bool slotPressed;

        private float slide;                  // 0 = parked below the screen, 1 = resting place
        private float slideVel;
        private float flyoutT;
        private bool flyoutOpen;
        private int selected = -1;            // point index, -1 = "now"
        private bool dragging;
        private float trackMax = -1f;
        private float selMinAnim = -1f;
        private readonly float[] hov = new float[IdCount];

        // Frame drawing state
        private float ui = 1f;
        private float fade = 1f;
        private float ox;
        private float oy;
        private Vector2 mp;
        private bool live;
        private Rect slotRect;
        private Rect flyoutRect;
        private readonly List<int> flyoutItems = new List<int>(4);

        // Generated textures (white, tinted with GUI.color)
        private static Texture2D triLeft, triRight, triUp, triDown, gradient;

        /// <summary>Bind a real model. Without it (and with useDemoData) a built-in demo is shown.</summary>
        internal TimeControlModel Model
        {
            get { return model; }
            set { model = value; }
        }

        /// <summary>
        /// Живая привязка к симуляции: модель принадлежит TimeControlRuntime (раннер сцены).
        /// Перечитывается каждый кадр — смена сцены подменяет рантайм, панель остаётся.
        /// Живой модели нет — панель показывает пустой таймлайн «полёт не начался».
        /// </summary>
        private void UpdateModelBinding()
        {
            TimeControlRuntime runtime = TimeControlRuntime.Current;
            if (runtime != null)
            {
                model = runtime.Model;
                return;
            }

            if (model == null)
            {
                model = new TimeControlModel();
            }
        }

        // ─── Open / close ────────────────────────────────────────────────────

        private void Update()
        {
            if (IsOpen)
            {
                if (PlayerInput.RawEscapeDown())
                {
                    PlayerInput.EscapeConsumedFrame = Time.frameCount;
                    if (flyoutOpen)
                    {
                        flyoutOpen = false;
                    }
                    else
                    {
                        Close();
                    }

                    return;
                }

                if (KeyBindings.RawDown(OpenKey))
                {
                    Close();
                }

                return;
            }

            if (KeyBindings.RawDown(OpenKey) && CanOpen())
            {
                Open();
            }
        }

        private bool CanOpen()
        {
            if (SettingsMenu.IsOpen || ShipTablet.AnyExpanded || BuilderUI.AnyOpen || CheatMenu.AnyOpen)
            {
                return false;
            }

            if (controller == null)
            {
                controller = FindAnyObjectByType<PlayerController>();
            }

            return controller == null || (!controller.InScreenFocus && !controller.InputBlocked);
        }

        private void Open()
        {
            EnsureModel();
            if (model == null)
            {
                return; // no data source bound and no demo: nothing to show
            }

            if (controller == null)
            {
                controller = FindAnyObjectByType<PlayerController>();
            }

            IsOpen = true;
            selected = -1;
            dragging = false;
            flyoutOpen = false;
            flyoutT = 0f;
            trackMax = -1f;
            selMinAnim = -1f;

            if (controller != null)
            {
                controller.InputBlocked = true;
            }

            FirstPersonCamera.SetCursorClaim(ref cursorClaimed, true, "TimeControlPanel");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            dragging = false;
            flyoutOpen = false;
            closedFrame = Time.frameCount;

            if (controller != null)
            {
                controller.InputBlocked = false;
            }

            FirstPersonCamera.SetCursorClaim(ref cursorClaimed, false, "TimeControlPanel");
        }

        private void OnDisable()
        {
            Close();
        }

        private void EnsureModel()
        {
            UpdateModelBinding();
        }

        // ─── Frame ───────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!IsOpen && slide <= 0.0005f)
            {
                return;
            }

            UpdateModelBinding();
            if (model == null)
            {
                return;
            }

            Event e = Event.current;
            ui = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
            MenuTheme.Ensure(ui);
            EnsureTextures();
            live = IsOpen;

            float dt = Time.unscaledDeltaTime;
            if (e.type == EventType.Repaint)
            {
                AdvanceSlide(dt);
                flyoutT = Mathf.MoveTowards(flyoutT, flyoutOpen ? 1f : 0f, dt / 0.18f);
            }

            fade = Mathf.Clamp01(slide * 2.4f);

            float vw = Screen.width / ui;
            float vh = Screen.height / ui;
            ox = Mathf.Round((vw - W) * 0.5f);

            // Resting place on screen vs. parked completely below the bottom edge (the tab
            // sticks out above the window, hence + TabH), so opening really rises from off-screen.
            float restY = (vh - H) - BottomMargin;
            float parkedY = vh + TabH + 6f;
            oy = Mathf.Round(Mathf.LerpUnclamped(parkedY, restY, slide));
            mp = e.mousePosition / ui;

            Color previousColor = GUI.color;
            int previousDepth = GUI.depth;
            GUI.depth = -9000;

            slotRect = L(TabPad, BtnY, SlotW, BtnH);
            ComputeFlyout();

            if (live && e.type == EventType.MouseDown && flyoutOpen && !FlyoutBoxRect().Contains(mp) && !slotRect.Contains(mp))
            {
                flyoutOpen = false;
            }

            if (live && flyoutOpen && e.type == EventType.MouseDown)
            {
                HandleFlyoutClick();
            }

            DrawChrome();
            DrawTab();
            DrawWindow(e, dt);
            DrawFlyout();

            if (live && e.type == EventType.KeyDown)
            {
                HandleKey(e);
            }

            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        /// <summary>
        /// Drives <see cref="slide"/> (0 = below the screen, 1 = resting place) with a damped
        /// spring: fast pull-in, small overshoot, then still. The underdamped solution is
        /// evaluated in closed form, so it looks identical at any frame rate and a long frame
        /// cannot make it overshoot the wrong way.
        /// </summary>
        private void AdvanceSlide(float dt)
        {
            float target = IsOpen ? 1f : 0f;

            float wd = SlideOmega * Mathf.Sqrt(1f - (SlideZeta * SlideZeta));
            float a = SlideZeta * SlideOmega;
            float e0 = slide - target;
            float b = (slideVel + (a * e0)) / wd;
            float decay = Mathf.Exp(-a * dt);
            float cos = Mathf.Cos(wd * dt);
            float sin = Mathf.Sin(wd * dt);

            slide = target + (decay * ((e0 * cos) + (b * sin)));
            slideVel = decay * ((((b * wd) - (a * e0)) * cos) - (((e0 * wd) + (a * b)) * sin));

            if (slide < 0f)
            {
                slide = 0f;
                slideVel = 0f;
            }

            // Snap the last invisible pixel: a spring's tail would otherwise crawl for a while.
            if (Mathf.Abs(target - slide) < 0.003f && Mathf.Abs(slideVel) < 0.15f)
            {
                slide = target;
                slideVel = 0f;
            }
        }

        // ─── Chrome: shadow, outline, fills ──────────────────────────────────

        private void DrawChrome()
        {
            Rect win = L(0f, 0f, W, H);
            Rect tab = L(0f, -TabH, TabW, TabH + TabOver);

            SetCol(Color.white);
            GUI.Box(P(new Rect(win.x - 18f, win.y - 18f, win.width + 36f, win.height + 36f)), GUIContent.none, MenuTheme.Glow);
            GUI.Box(P(new Rect(tab.x - 18f, tab.y - 18f, tab.width + 36f, TabH + 36f)), GUIContent.none, MenuTheme.Glow);

            // One outline for the union of tab + window: draw both outlines (slightly larger
            // fills) first, then both panel fills on top — the seam between them disappears.
            Box(Inflate(tab, 1f), TabR + 1, MenuTheme.HairStrong);
            Box(Inflate(win, 1f), WinR + 1, MenuTheme.HairStrong);
            Box(tab, TabR, MenuTheme.Panel);
            Box(win, WinR, MenuTheme.Panel);

            // The window's top-left corner is square: the tab continues the left edge
            // (fill patch + a straight outline strip so the border has no notch).
            Flat(L(0f, 0f, WinR + 2f, WinR + 2f), MenuTheme.Panel);
            Flat(L(-1f, -TabH + TabR, 1f, H + TabH - TabR - WinR), MenuTheme.HairStrong);
        }

        // ─── Tab: time acceleration ──────────────────────────────────────────

        private void DrawTab()
        {
            bool physActive = model.ActiveKind == WarpKind.Physical;
            float warpX = TabPad + SlotW + 22f;

            Txt(L(TabPad, CapY, 80f, 12f), "ФИЗИКА", 10, true, TextAnchor.MiddleLeft, MenuTheme.Subtle);
            Txt(L(warpX, CapY, 200f, 12f), "УСКОРЕНИЕ ВРЕМЕНИ", 10, true, TextAnchor.MiddleLeft, MenuTheme.Subtle);

            // Readout of the active (or hovered) factor, full number.
            string readout;
            Color readoutCol;
            int hoverWarp = HoveredWarpButton();
            if (hoverWarp >= 0)
            {
                readout = FormatFactor(FullFactors[hoverWarp]);
                readoutCol = WarpCol;
            }
            else if (physActive)
            {
                readout = FormatFactor(model.PhysicalFactor) + " · физика";
                readoutCol = PhysCol;
            }
            else
            {
                readout = FormatFactor(FullFactors[Mathf.Clamp(model.FullIndex, 0, FullFactors.Length - 1)]);
                readoutCol = WarpCol;
            }

            if (model.AppliedFactorClamped)
            {
                // Кап (площадка/посадка/атмосфера/запрет долгого варпа) показан явно:
                // молчаливый срез запрещён.
                readout += " · кап " + FormatFactor(model.AppliedFactor);
                if (model.ClampReason.Length > 0)
                {
                    readout += " (" + model.ClampReason + ")";
                }

                readoutCol = Color.Lerp(readoutCol, MenuTheme.Muted, 0.35f);
            }

            Txt(L(TabW - TabPad - 220f, CapY, 220f, 12f), readout, 10, true, TextAnchor.MiddleRight, readoutCol);

            // Physics slot. Left click = use it, right click = flyout with the other values.
            float h = Hov(IdSlot, slotRect);
            if (physActive)
            {
                Box(Inflate(slotRect, 4f), 12, WithA(PhysCol, 0.16f + (0.06f * h)));
                Box(slotRect, 9, Color.Lerp(PhysCol, Color.white, 0.12f * h));
            }
            else
            {
                Box(slotRect, 9, Color.Lerp(MenuTheme.Card, new Color(0.11f, 0.17f, 0.17f, 1f), h));
                Box(slotRect, 9, WithA(PhysCol, 0.40f + (0.30f * h)), true);
            }

            Txt(slotRect, "×" + model.PhysicalFactor, 14, true, TextAnchor.MiddleCenter, physActive ? OnPhys : PhysCol);
            Tri(triUp, new Rect(slotRect.xMax - 12f, slotRect.y + 5f, 7f, 4.5f), physActive ? WithA(OnPhys, 0.7f) : WithA(PhysCol, 0.8f));

            if (live)
            {
                // Наведение на слот выдвигает меню физического ускорения (без клика).
                if (slotRect.Contains(mp))
                {
                    slotHover += Time.unscaledDeltaTime;
                    if (slotHover >= SlotHoverOpenSeconds)
                    {
                        flyoutOpen = true;
                    }
                }
                else
                {
                    slotHover = 0f;
                }
            }

            // ЛКМ: нажатие открывает вылет, протяжка вверх выбирает, отпускание применяет.
            if (Clicked(slotRect, 0))
            {
                slotPressed = true;
                flyoutOpen = true;
            }
            else if (Clicked(slotRect, 1))
            {
                flyoutOpen = !flyoutOpen;
            }

            if (live && slotPressed && (Event.current.type == EventType.MouseUp || Event.current.rawType == EventType.MouseUp))
            {
                slotPressed = false;
                ApplySlotRelease();
            }

            Flat(L(warpX - 11f, BtnY + 3f, 1f, BtnH - 6f), MenuTheme.HairStrong);

            // Full warp
            for (int i = 0; i < FullFactors.Length; i++)
            {
                Rect r = WarpButtonRect(i);
                bool active = model.ActiveKind == WarpKind.Full && model.FullIndex == i;
                float hv = Hov(IdWarp0 + i, r);

                if (active)
                {
                    Box(Inflate(r, 4f), 12, WithA(WarpCol, 0.16f + (0.06f * hv)));
                    Box(r, 9, Color.Lerp(WarpCol, Color.white, 0.12f * hv));
                }
                else
                {
                    Box(r, 9, Color.Lerp(MenuTheme.Card, new Color(0.17f, 0.15f, 0.11f, 1f), hv));
                }

                Color idle = Color.Lerp(MenuTheme.Muted, WarpCol, hv);
                Txt(r, FullLabels[i], 13, true, TextAnchor.MiddleCenter, active ? OnWarp : idle);

                if (Clicked(r, 0))
                {
                    model.RaiseFull(i);
                }
            }
        }

        private Rect WarpButtonRect(int i)
        {
            return L(TabPad + SlotW + 22f + (i * (WarpBtnW + WarpGap)), BtnY, WarpBtnW, BtnH);
        }

        /// <summary>
        /// Отпускание мыши после протяжки от слота: над пунктом вылета — применить его,
        /// над слотом — оставить вылет открытым, иначе просто закрыть вылет.
        /// </summary>
        private void ApplySlotRelease()
        {
            Event e = Event.current;

            for (int i = 0; i < flyoutItems.Count; i++)
            {
                if (FlyoutItemRect(i).Contains(mp))
                {
                    flyoutOpen = false;
                    model.RaisePhysical(flyoutItems[i]);
                    e.Use();
                    return;
                }
            }

            if (slotRect.Contains(mp))
            {
                // An ordinary click on the slot is how the list is opened: keep it open.
                // Applying the factor that is already active would be a no-op and closing
                // the list on mouse-up made the values unreachable for a player who clicks
                // instead of hovering or dragging.
                return;
            }

            flyoutOpen = false;
        }

        private int HoveredWarpButton()
        {
            if (!live)
            {
                return -1;
            }

            for (int i = 0; i < FullFactors.Length; i++)
            {
                if (WarpButtonRect(i).Contains(mp))
                {
                    return i;
                }
            }

            return -1;
        }

        // ─── Flyout (physical warp values) ───────────────────────────────────

        private void ComputeFlyout()
        {
            flyoutItems.Clear();
            for (int i = 0; i < PhysicalFactors.Length; i++)
            {
                // The value shown in the slot is not repeated in the flyout.
                if (PhysicalFactors[i] != model.PhysicalFactor)
                {
                    flyoutItems.Add(PhysicalFactors[i]);
                }
            }

            const float itemH = 30f;
            const float itemGap = 4f;
            const float pad = 8f;
            float n = flyoutItems.Count;
            float fh = (pad * 2f) + (n * itemH) + (Mathf.Max(0f, n - 1f) * itemGap);
            float fw = SlotW + 16f;
            flyoutRect = L(TabPad - 8f, -TabH - 8f - fh, fw, fh);
        }

        private Rect FlyoutItemRect(int i)
        {
            const float itemH = 30f;
            const float itemGap = 4f;
            const float pad = 8f;
            return new Rect(flyoutRect.x + pad, flyoutRect.y + pad + (i * (itemH + itemGap)) + FlyoutLift(), SlotW, itemH);
        }

        /// <summary>
        /// How far the opening flyout still hangs below its resting place. The drawing and
        /// the hit tests must use the same value: a click on an item that is still riding up
        /// has to land on the item the player actually sees.
        /// </summary>
        private float FlyoutLift()
        {
            float ease = 1f - Mathf.Pow(1f - flyoutT, 3f);
            return (1f - ease) * 14f;
        }

        /// <summary>The flyout box as drawn this frame (resting rect shifted by the lift).</summary>
        private Rect FlyoutBoxRect()
        {
            Rect box = flyoutRect;
            box.y += FlyoutLift();
            return box;
        }

        private void HandleFlyoutClick()
        {
            Event e = Event.current;
            if (e.button != 0)
            {
                return;
            }

            for (int i = 0; i < flyoutItems.Count; i++)
            {
                if (FlyoutItemRect(i).Contains(mp))
                {
                    e.Use();
                    flyoutOpen = false;
                    model.RaisePhysical(flyoutItems[i]);
                    return;
                }
            }
        }

        private void DrawFlyout()
        {
            if (flyoutT <= 0.001f)
            {
                return;
            }

            float ease = 1f - Mathf.Pow(1f - flyoutT, 3f);
            float previousFade = fade;
            fade *= ease;

            Rect box = FlyoutBoxRect();

            SetCol(Color.white);
            GUI.Box(P(new Rect(box.x - 18f, box.y - 18f, box.width + 36f, box.height + 36f)), GUIContent.none, MenuTheme.Glow);
            Box(Inflate(box, 1f), 13, WithA(PhysCol, 0.35f));
            Box(box, 12, FlyoutBg);

            for (int i = 0; i < flyoutItems.Count; i++)
            {
                Rect r = FlyoutItemRect(i);

                // staggered: the item nearest to the slot (bottom) arrives first
                float stagger = Mathf.Clamp01((flyoutT * 1.7f) - ((flyoutItems.Count - 1 - i) * 0.18f));
                float prev = fade;
                fade = previousFade * stagger;

                float h = Hov(IdFly0 + i, r);
                Box(r, 8, Color.Lerp(MenuTheme.Card, WithA(PhysCol, 0.22f), h));
                Txt(r, "×" + flyoutItems[i], 13, true, TextAnchor.MiddleCenter, Color.Lerp(PhysCol, Color.white, 0.25f * h));
                fade = prev;
            }

            fade = previousFade;
        }

        // ─── Window: clock, timeline, rewind ─────────────────────────────────

        private void DrawWindow(Event e, float dt)
        {
            Txt(L(26f, 16f, 200f, 12f), "ПЕРЕМОТКА", 10, true, TextAnchor.MiddleLeft, MenuTheme.Subtle);

            // close hint
            string key = KeyBindings.Display(OpenKey);
            Txt(L(W - 26f - 56f, 12f, 56f, 20f), "закрыть", 11, false, TextAnchor.MiddleRight, MenuTheme.Subtle);
            Rect keycap = L(W - 26f - 22f - 62f, 12f, 22f, 20f);
            Box(keycap, 6, MenuTheme.Card);
            Box(keycap, 6, MenuTheme.HairStrong, true);
            Txt(keycap, key, 11, true, TextAnchor.MiddleCenter, MenuTheme.Muted);

            // ── track geometry ──
            int count = model.PointCount;
            float cur = (float)model.CurrentRealMinutes;
            float targetMax = Mathf.Max(1f, Mathf.Max(cur, count - 1));
            if (trackMax < 0f)
            {
                trackMax = targetMax;
            }

            int sel = EffectiveSelection();
            float selMin = sel >= 0 ? sel : cur;
            if (selMinAnim < 0f)
            {
                selMinAnim = selMin;
            }

            if (e.type == EventType.Repaint)
            {
                trackMax = Mathf.Lerp(trackMax, targetMax, 1f - Mathf.Exp(-8f * dt));
                selMinAnim = Mathf.Lerp(selMinAnim, selMin, 1f - Mathf.Exp(-18f * dt));
            }

            float trackLen = TrackX1 - TrackX0;
            float spacing = trackLen / trackMax;
            float playX = XOf(cur);

            HandleTrackInput(e, count, cur);
            sel = EffectiveSelection();

            // ── main clock ──
            Txt(L((W * 0.5f) - 100f, 30f, 200f, 12f), "ВРЕМЯ ПОЛЁТА", 10, true, TextAnchor.MiddleCenter, MenuTheme.Subtle);
            string nowPrefix;
            string nowClock = FormatClock(model.CurrentFlightSeconds, out nowPrefix);
            float nowW = Clock(nowClock, W * 0.5f, 44f, 40, MenuTheme.Text);
            if (nowPrefix.Length > 0)
            {
                Txt(L((W * 0.5f) - (nowW * 0.5f) - 132f, 56f, 120f, 26f), nowPrefix, 15, true, TextAnchor.MiddleRight, MenuTheme.Muted);
            }

            // ── track ──
            Rect groove = L(TrackX0 - 8f, TrackY - 3f, trackLen + 16f, 6f);
            Box(groove, 3, MenuTheme.Well);
            Box(groove, 3, MenuTheme.Hair, true);

            float lastX = count > 0 ? XOf(count - 1) : playX;
            if (lastX > playX + 0.5f)
            {
                Box(L(playX, TrackY - 1.5f, lastX - playX, 3f), 2, WithA(MenuTheme.Muted, 0.28f)); // stale future
            }

            SetCol(MenuTheme.Accent);
            GUI.DrawTexture(P(L(TrackX0 - 8f, TrackY - 2f, playX - (TrackX0 - 8f), 4f)), gradient);

            float selX = XOf(selMinAnim);
            if (sel >= 0)
            {
                float a = Mathf.Min(selX, playX);
                float b = Mathf.Max(selX, playX);
                Box(L(a, TrackY - 2f, b - a, 4f), 2, WithA(MenuTheme.Text, 0.55f));
            }

            // points + minute labels
            float pointR = spacing < 16f ? Mathf.Max(2.5f, spacing * 0.3f) : 5f;
            int stride = Mathf.Max(1, Mathf.CeilToInt(26f / Mathf.Max(1f, spacing)));
            bool wordStart = spacing * stride >= 44f;
            // Сжатие шкалы: при тысячах точек рисуем каждую N-ю, иначе каша из точек.
            int drawStride = Mathf.Max(1, Mathf.CeilToInt(3f / Mathf.Max(1f, spacing)));
            int hoverK = live ? NearestTarget(mp.x, count, cur, 14f) : -2;
            for (int k = 0; k < count; k++)
            {
                if (drawStride > 1 && (k % drawStride) != 0 && k != sel && k != 0 && k != count - 1)
                {
                    continue;
                }

                float x = XOf(k);
                bool available = model.IsPointAvailable(k);
                bool future = k > cur + 0.0001f;
                float r = k == 0 ? pointR * 1.3f : pointR;
                if (k == hoverK)
                {
                    r += 1.2f;
                }

                if (!available)
                {
                    // Точка снята в контактной зоне/освобождена лимитом: откат запрещён.
                    Dot(x, TrackY, r, WithA(MenuTheme.Muted, 0.5f));
                    Dot(x, TrackY, Mathf.Max(1f, r - 2f), Hole);
                }
                else if (future)
                {
                    Dot(x, TrackY, r, MenuTheme.Muted);
                    Dot(x, TrackY, r - 2f, Hole);
                }
                else
                {
                    Dot(x, TrackY, r, MenuTheme.Accent);
                    Dot(x, TrackY, 2f, WithA(Hole, 0.6f));
                }

                bool isSel = k == sel;
                if (k % stride == 0 || isSel)
                {
                    string label = k == 0 ? (wordStart ? "СТАРТ" : "0") : k.ToString(CultureInfo.InvariantCulture);
                    Txt(L(x - 24f, TrackY + 16f, 48f, 12f), label, 10, isSel || k == 0, TextAnchor.MiddleCenter,
                        isSel ? MenuTheme.Text : MenuTheme.Subtle);
                }
            }

            // playhead
            Dot(playX, TrackY, 14f, WithA(MenuTheme.Accent, 0.10f));
            Dot(playX, TrackY, 12f, WithA(MenuTheme.Accent, 0.22f));
            Dot(playX, TrackY, 9f, MenuTheme.Accent);
            Dot(playX, TrackY, 3.5f, OnAccent);
            Txt(L(playX - 30f, TrackY - 36f, 60f, 12f), "СЕЙЧАС", 9, true, TextAnchor.MiddleCenter, MenuTheme.Accent);

            // selection handle
            if (sel >= 0)
            {
                Dot(selX, TrackY, 15f, WithA(MenuTheme.Text, 0.10f));
                Rect ring = L(selX - 11f, TrackY - 11f, 22f, 22f);
                Box(ring, 11, MenuTheme.Text, true);
                Box(Inflate(ring, -1f), 10, MenuTheme.Text, true);
                Tri(triDown, L(selX - 4f, TrackY - 22f, 8f, 6f), MenuTheme.Text);
            }

            // ── selected point readout ──
            double selSeconds = sel >= 0 ? model.PointFlightSeconds[sel] : model.CurrentFlightSeconds;
            string selCaption = sel < 0 ? "ВРЕМЯ ТОЧКИ" : (sel > cur ? "ВПЕРЁД НА" : "ПЕРЕМОТАТЬ НА");
            Txt(L((W * 0.5f) - 100f, 172f, 200f, 12f), selCaption, 10, true, TextAnchor.MiddleCenter, MenuTheme.Subtle);
            string selPrefix;
            string selClock = FormatClock(selSeconds, out selPrefix);
            float selW = Clock(selClock, W * 0.5f, 186f, 26, sel >= 0 ? MenuTheme.Accent : MenuTheme.Subtle);
            if (selPrefix.Length > 0)
            {
                Txt(L((W * 0.5f) - (selW * 0.5f) - 122f, 191f, 110f, 22f), selPrefix, 13, true, TextAnchor.MiddleRight, MenuTheme.Muted);
            }

            if (sel >= 0)
            {
                double delta = selSeconds - model.CurrentFlightSeconds;
                string chip = (delta < 0d ? "−" : "+") + FormatShort(delta);
                float chipW = TextW(chip, 11, true) + 18f;
                Rect chipRect = L((W * 0.5f) + (selW * 0.5f) + 14f, 192f, chipW, 22f);
                Box(chipRect, 11, MenuTheme.Card);
                Box(chipRect, 11, MenuTheme.HairStrong, true);
                Txt(chipRect, chip, 11, true, TextAnchor.MiddleCenter, MenuTheme.Muted);
            }

            // ── legend ──
            Dot(33f, H - 35f, 5f, MenuTheme.Accent);
            Txt(L(44f, H - 43f, 70f, 16f), "записано", 11, false, TextAnchor.MiddleLeft, MenuTheme.Subtle);
            Dot(117f, H - 35f, 5f, MenuTheme.Muted);
            Dot(117f, H - 35f, 3f, Hole);
            Txt(L(128f, H - 43f, 240f, 16f), "будет перезаписано", 11, false, TextAnchor.MiddleLeft, MenuTheme.Subtle);

            string footer = model.Notice.Length > 0
                ? model.Notice
                : (count == 0
                    ? "полёт не начался"
                    : (drawStride > 1 ? "точка каждые " + drawStride + " мин" : "1 точка = 1 минута полёта"));
            Txt(L(28f, H - 26f, 360f, 16f), footer, 11, false, TextAnchor.MiddleLeft,
                model.Notice.Length > 0 ? MenuTheme.Accent : WithA(MenuTheme.Subtle, 0.7f));

            // ── rewind button ──
            DrawRewind(sel, cur);
        }

        private void DrawRewind(int sel, float cur)
        {
            Rect r = L(W - 26f - RewindW, H - 24f - RewindH, RewindW, RewindH);
            bool can = sel >= 0 && model.IsPointAvailable(sel);
            float h = Hov(IdRewind, r, can);

            if (can)
            {
                Box(Inflate(r, 5f), 15, WithA(MenuTheme.Accent, 0.14f + (0.08f * h)));
                Box(r, 12, Color.Lerp(MenuTheme.Accent, Color.white, 0.14f * h));
            }
            else
            {
                Box(r, 12, MenuTheme.Card);
                Box(r, 12, MenuTheme.Hair, true);
            }

            Color ink = can ? OnAccent : MenuTheme.Subtle;
            bool forward = sel > cur;
            Texture2D tri = forward ? triRight : triLeft;
            float iy = r.y + (RewindH * 0.5f) - 5f;
            Tri(tri, new Rect(r.x + 20f, iy, 7f, 10f), ink);
            Tri(tri, new Rect(r.x + 28f, iy, 7f, 10f), ink);
            Txt(new Rect(r.x + 36f, r.y, r.width - 44f, r.height), "Перемотать время", 14, true, TextAnchor.MiddleCenter, ink);

            if (can && Clicked(r, 0))
            {
                ConfirmRewind();
            }
        }

        // ─── Selection ───────────────────────────────────────────────────────

        private int EffectiveSelection()
        {
            if (selected < 0 || selected >= model.PointCount)
            {
                return -1;
            }

            // Selecting the point the playhead sits on means "nothing to rewind to".
            return Mathf.Abs(selected - (float)model.CurrentRealMinutes) < 0.001f ? -1 : selected;
        }

        private float XOf(float minutes)
        {
            float t = trackMax > 0f ? Mathf.Clamp(minutes / trackMax, 0f, 1.02f) : 0f;
            return TrackX0 + (t * (TrackX1 - TrackX0));
        }

        /// <summary>Nearest point index to a virtual x; -1 = "now"; -2 = nothing within maxDist.</summary>
        private int NearestTarget(float x, int count, float cur, float maxDist)
        {
            if (mp.y < TrackY + oy - 30f || mp.y > TrackY + oy + 26f)
            {
                return -2;
            }

            float lx = x - ox;
            int best = -2;
            float bestD = maxDist;

            float dNow = Mathf.Abs(lx - XOf(cur));
            if (dNow <= bestD)
            {
                best = -1;
                bestD = dNow;
            }

            for (int k = 0; k < count; k++)
            {
                float d = Mathf.Abs(lx - XOf(k));
                if (d < bestD - 0.01f)
                {
                    best = k;
                    bestD = d;
                }
            }

            return best;
        }

        private void HandleTrackInput(Event e, int count, float cur)
        {
            if (!live)
            {
                return;
            }

            Rect area = L(TrackX0 - 16f, TrackY - 30f, (TrackX1 - TrackX0) + 32f, 58f);
            if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(mp))
            {
                dragging = true;
                e.Use();
                PickAt(count, cur);
            }
            else if (dragging && e.type == EventType.MouseDrag)
            {
                e.Use();
                PickAt(count, cur);
            }
            else if (dragging && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                dragging = false;
            }
        }

        private void PickAt(int count, float cur)
        {
            // While dragging, always snap to the closest target regardless of the vertical position.
            float lx = mp.x - ox;
            int best = -1;
            float bestD = Mathf.Abs(lx - XOf(cur));
            for (int k = 0; k < count; k++)
            {
                float d = Mathf.Abs(lx - XOf(k));
                if (d < bestD - 0.01f)
                {
                    best = k;
                    bestD = d;
                }
            }

            selected = best;
        }

        private void HandleKey(Event e)
        {
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                    StepSelection(-1);
                    e.Use();
                    break;
                case KeyCode.RightArrow:
                    StepSelection(1);
                    e.Use();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (EffectiveSelection() >= 0)
                    {
                        ConfirmRewind();
                    }

                    e.Use();
                    break;
            }
        }

        /// <summary>Steps through all points plus "now", ordered along the track.</summary>
        private void StepSelection(int dir)
        {
            int count = model.PointCount;
            float cur = (float)model.CurrentRealMinutes;
            var order = new List<int>(count + 1);
            bool nowAdded = false;
            for (int k = 0; k < count; k++)
            {
                if (!nowAdded && k >= cur - 0.001f)
                {
                    order.Add(-1);
                    nowAdded = true;
                    if (Mathf.Abs(k - cur) < 0.001f)
                    {
                        continue; // the point under the playhead IS "now"
                    }
                }

                order.Add(k);
            }

            if (!nowAdded)
            {
                order.Add(-1);
            }

            int at = order.IndexOf(EffectiveSelection());
            if (at < 0)
            {
                at = order.IndexOf(-1);
            }

            at = Mathf.Clamp(at + dir, 0, order.Count - 1);
            selected = order[at];
        }

        private void ConfirmRewind()
        {
            int sel = EffectiveSelection();
            if (sel < 0)
            {
                return;
            }

            model.RaiseRewind(sel);
            selected = -1;
        }

        // ─── Time formatting ─────────────────────────────────────────────────

        private static void Split(double seconds, out long years, out long days, out int h, out int m, out int s)
        {
            long t = (double.IsNaN(seconds) || seconds <= 0d) ? 0L : (long)System.Math.Floor(seconds);
            years = t / 31536000L;
            t -= years * 31536000L;
            days = t / 86400L;
            t -= days * 86400L;
            h = (int)(t / 3600L);
            t -= h * 3600L;
            m = (int)(t / 60L);
            s = (int)(t - (m * 60L));
        }

        /// <summary>"HH:MM:SS" for the big digits; years/days go to the small prefix ("2 г 134 д").</summary>
        private static string FormatClock(double seconds, out string prefix)
        {
            long y, d;
            int h, m, s;
            Split(seconds, out y, out d, out h, out m, out s);
            prefix = y > 0 ? y + " г " + d + " д" : (d > 0 ? d + " д" : string.Empty);
            return h.ToString("00", CultureInfo.InvariantCulture) + ":" +
                   m.ToString("00", CultureInfo.InvariantCulture) + ":" +
                   s.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Two largest units: "8 мин 37 с", "20 ч 30 мин", "2 д 3 ч", "1 г 20 д".</summary>
        private static string FormatShort(double seconds)
        {
            long y, d;
            int h, m, s;
            Split(System.Math.Abs(seconds), out y, out d, out h, out m, out s);
            if (y > 0)
            {
                return y + " г " + d + " д";
            }

            if (d > 0)
            {
                return d + " д " + h + " ч";
            }

            if (h > 0)
            {
                return h + " ч " + m + " мин";
            }

            if (m > 0)
            {
                return m + " мин " + s + " с";
            }

            return s + " с";
        }

        /// <summary>"×1 000 000" — thousands separated by a normal space.</summary>
        private static string FormatFactor(double factor)
        {
            return "×" + ((long)factor).ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
        }

        private static readonly string[] DigitStrings = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", ":" };

        /// <summary>Clock with fixed-width cells so the digits never jitter. Returns total width.</summary>
        private float Clock(string text, float cx, float y, int size, Color c)
        {
            float cell = size * 0.60f;
            float colon = size * 0.32f;
            float total = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                total += text[i] == ':' ? colon : cell;
            }

            float x = cx - (total * 0.5f);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                float w = ch == ':' ? colon : cell;
                string s = ch == ':' ? DigitStrings[10] : DigitStrings[Mathf.Clamp(ch - '0', 0, 9)];
                Txt(L(x, y, w, size * 1.2f), s, size, true, TextAnchor.MiddleCenter, c);
                x += w;
            }

            return total;
        }

        // ─── Primitives ──────────────────────────────────────────────────────

        /// <summary>Window-local → virtual screen rect.</summary>
        private Rect L(float x, float y, float w, float h)
        {
            return new Rect(ox + x, oy + y, w, h);
        }

        private static Rect Inflate(Rect r, float by)
        {
            return new Rect(r.x - by, r.y - by, r.width + (by * 2f), r.height + (by * 2f));
        }

        private static Color WithA(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, a);
        }

        private Rect P(Rect r)
        {
            return new Rect(Mathf.Round(r.x * ui), Mathf.Round(r.y * ui), Mathf.Round(r.width * ui), Mathf.Round(r.height * ui));
        }

        private void SetCol(Color c)
        {
            GUI.color = new Color(c.r, c.g, c.b, c.a * fade);
        }

        private void Box(Rect r, int radius, Color c, bool ring = false)
        {
            // A 9-slice texture needs the rect to be at least (2r+2) px each way.
            radius = Mathf.Min(radius, Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(r.width, r.height) - 3f) * 0.5f)));
            SetCol(c);
            GUI.Box(P(r), GUIContent.none, ring ? MenuTheme.Ring(radius) : MenuTheme.Fill(radius));
        }

        private void Flat(Rect r, Color c)
        {
            SetCol(c);
            GUI.DrawTexture(P(r), Texture2D.whiteTexture);
        }

        private void Dot(float cx, float cy, float radius, Color c)
        {
            SetCol(c);
            GUI.DrawTexture(P(new Rect(ox + cx - radius, oy + cy - radius, radius * 2f, radius * 2f)), MenuTheme.Disc);
        }

        private void Tri(Texture2D tex, Rect r, Color c)
        {
            SetCol(c);
            GUI.DrawTexture(P(r), tex);
        }

        private void Txt(Rect r, string text, int size, bool bold, TextAnchor anchor, Color c)
        {
            SetCol(c);
            GUI.Label(P(r), text, MenuTheme.TextStyle(size, bold, anchor));
        }

        private float TextW(string text, int size, bool bold)
        {
            return MenuTheme.TextStyle(size, bold, TextAnchor.MiddleCenter).CalcSize(new GUIContent(text)).x / ui;
        }

        private float Hov(int id, Rect r, bool enabled = true)
        {
            if (Event.current.type == EventType.Repaint)
            {
                bool over = live && enabled && r.Contains(mp) && !(flyoutOpen && flyoutRect.Contains(mp));
                hov[id] = Mathf.MoveTowards(hov[id], over ? 1f : 0f, Time.unscaledDeltaTime * 9f);
            }

            return hov[id];
        }

        private bool Clicked(Rect r, int button)
        {
            Event e = Event.current;
            if (!live || e.type != EventType.MouseDown || e.button != button || !r.Contains(mp))
            {
                return false;
            }

            e.Use();
            return true;
        }

        // ─── Generated textures ──────────────────────────────────────────────

        private static void EnsureTextures()
        {
            if (triLeft != null && triRight != null && triUp != null && triDown != null && gradient != null)
            {
                return;
            }

            triLeft = MakeTriangle(0);
            triRight = MakeTriangle(1);
            triUp = MakeTriangle(2);
            triDown = MakeTriangle(3);
            gradient = MakeGradient(64);
        }

        /// <summary>White anti-aliased triangle. dir: 0 points left, 1 right, 2 up, 3 down.</summary>
        private static Texture2D MakeTriangle(int dir)
        {
            const int N = 32;
            const int S = 4;
            var pixels = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < S; sy++)
                    {
                        for (int sx = 0; sx < S; sx++)
                        {
                            float u = (x + ((sx + 0.5f) / S)) / N;
                            float v = (y + ((sy + 0.5f) / S)) / N;
                            bool inside;
                            switch (dir)
                            {
                                case 0: inside = Mathf.Abs(v - 0.5f) <= 0.5f * u; break;
                                case 1: inside = Mathf.Abs(v - 0.5f) <= 0.5f * (1f - u); break;
                                case 2: inside = Mathf.Abs(u - 0.5f) <= 0.5f * (1f - v); break;
                                default: inside = Mathf.Abs(u - 0.5f) <= 0.5f * v; break;
                            }

                            if (inside)
                            {
                                hit++;
                            }
                        }
                    }

                    pixels[(y * N) + x] = new Color32(255, 255, 255, (byte)(255f * hit / (S * S)));
                }
            }

            return MakeTexture(N, N, pixels);
        }

        /// <summary>White horizontal ramp, alpha 0.30 → 1 (the filled part of the timeline fades in from the start).</summary>
        private static Texture2D MakeGradient(int width)
        {
            var pixels = new Color32[width];
            for (int x = 0; x < width; x++)
            {
                float a = Mathf.Lerp(0.30f, 1f, x / (float)(width - 1));
                pixels[x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            return MakeTexture(width, 1, pixels);
        }

        private static Texture2D MakeTexture(int w, int h, Color32[] pixels)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            t.SetPixels32(pixels);
            t.Apply();
            return t;
        }
    }
}
