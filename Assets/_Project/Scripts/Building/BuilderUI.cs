using System.Collections.Generic;
using Galilego.Spacecraft;
using Galilego.Tablet;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// BUILDING INTERFACE (IMGUI over BuilderSkin, light gray theme).
    ///
    /// Hotbar: 9 bottom-center slots filled by dragging from the inventory
    /// (a plain click only selects); right-click opens a slot mini-menu with
    /// quick size editors or engine stats. Sizes are stored per slot
    /// (PartPreset); edits go to the explicitly selected slot or to the card
    /// draft. The inventory window (I, build mode only) shows the 3D preview
    /// and stats column on the left and a scrolling card grid on the right.
    /// While the window is open the cursor is released and player input is
    /// blocked (PlayerController.InputBlocked), like in the cheat menu; the UI
    /// scales to DPI/4K via GUI.matrix by screen height.
    /// </summary>
    [DefaultExecutionOrder(-105)]
    public sealed class BuilderUI : MonoBehaviour
    {
        [Tooltip("Режим строительства сцены.")]
        public SpacecraftBuilder Builder;

        [Tooltip("Камера от первого лица (её захват курсора отдаётся окну).")]
        public FirstPersonCamera Camera;

        [Tooltip("Контроллер игрока (блокировка ввода, пока окно открыто).")]
        public PlayerController Controller;

        /// <summary>
        /// ΔV panel (plan/remaining in flight). External consumers of the same
        /// calculation (the tablet) read it: panel.Live ?? panel.Plan — a single
        /// source, nothing is recomputed.
        /// </summary>
        public DeltaVPanel DeltaV { get { return deltaVPanel; } }

        /// <summary>
        /// Catalog entry: part kind and (for an engine) its Id from EngineSpecs.
        /// Each engine gets its OWN card in the grid (engines go together, in
        /// registry order). A new engine in the registry adds its card itself.
        /// </summary>
        private readonly struct CatalogEntry
        {
            public readonly PartKind Kind;
            public readonly string EngineId;

            public CatalogEntry(PartKind kind, string engineId)
            {
                Kind = kind;
                EngineId = engineId;
            }
        }

        private static CatalogEntry[] BuildCatalog()
        {
            var entries = new System.Collections.Generic.List<CatalogEntry>();
            IReadOnlyList<EngineSpec> engines = EngineSpecs.All;
            for (int i = 0; i < engines.Count; i++)
            {
                entries.Add(new CatalogEntry(PartKind.Engine, engines[i].Id));
            }

            entries.Add(new CatalogEntry(PartKind.FuelTank, null));
            entries.Add(new CatalogEntry(PartKind.Separator, null));
            entries.Add(new CatalogEntry(PartKind.Capsule, null));
            entries.Add(new CatalogEntry(PartKind.RcsBlock, null));
            return entries.ToArray();
        }

        private static readonly CatalogEntry[] Catalog = BuildCatalog();

        // Geometry (in "virtual" pixels at a screen height of 1080).
        private const float SlotSize = 64f;
        private const float SlotGap = 10f;

        /// <summary>Row under the hotbar slots: the key number (1..9) is drawn there.</summary>
        private const float SlotNumberRow = 26f;
        private const float DragThreshold = 5f;

        /// <summary>The inventory window: up to ~78% of the screen height.</summary>
        private const float WindowHeightFraction = 0.78f;

        /// <summary>Free space left of the window.</summary>
        private const float WindowLeftMargin = 24f;

        /// <summary>
        /// Free space right of the window: the ΔV panel rail (tabs) lives there
        /// and would cover the search/preview column otherwise.
        /// </summary>
        private const float WindowRightReserve = 146f;

        /// <summary>Top of the window: below the parts/mass summary pill.</summary>
        private const float WindowTop = 64f;

        /// <summary>
        /// Screen height kept free under the window: close row, ΔV header block
        /// (bottom right) and the hotbar with its name plate.
        /// </summary>
        private const float WindowBottomReserve = 192f;

        /// <summary>Minimum inventory window height (small game views).</summary>
        private const float WindowMinHeight = 420f;

        private const float TabHeight = 44f;
        private const float StatPillHeight = 34f;
        private const float StatPillGap = 6f;
        private const int GridColumns = 8;

        /// <summary>Wheel scroll step in the stats column, px per notch.</summary>
        private const float StatsScrollStep = 42f;

        // Arrow button steps, m.
        private const float StepBig = 1f;
        private const float StepSmall = 0.1f;

        /// <summary>
        /// Hotbar slot: its own part preset and an icon of THAT preset (shape/
        /// proportions). The icon is re-rendered when the sizes change.
        /// </summary>
        private sealed class HotSlot
        {
            public bool Occupied;
            public PartPreset Preset;
            public Texture2D Icon;
            public bool IconOwned;      // own render — destroyed on replace
            public bool IconDirty;      // sizes changed: re-render
        }

        private readonly HotSlot[] hotbar = CreateHotbar();

        /// <summary>
        /// Kind drafts ("preset for the next placement"): the window edits them
        /// when the selected slot is not of this kind. Editing a draft does not
        /// touch parts already placed in the hotbar.
        /// </summary>
        private readonly PartPreset[] drafts = new PartPreset[Catalog.Length];

        /// <summary>
        /// The slot the window edits (selected in the hotbar). null — a draft is
        /// edited: clicking a card in the inventory switches to the draft,
        /// clicking a slot to the slot.
        /// </summary>
        private HotSlot editSlot;

        private readonly BuilderPreview preview = new BuilderPreview();
        private readonly DeltaVPanel deltaVPanel = new DeltaVPanel();
        private int hotbarIndex = -1;
        private bool open;
        private float openAnim;
        private int statsIndex = -1;

        private PartPreset? dragPreset;
        private Texture2D dragIcon;
        private int dragFromHotbar = -1;
        private Vector2 dragOrigin;
        private bool dragMoved;

        private int holdId = -1;
        private float holdNextTime;

        // Stats column scroll (a cone tank does not fit the height).
        private float statsScroll;
        private bool statsScrollDrag;
        private float statsScrollDragOrigin;
        private float statsScrollDragStart;

        /// <summary>Cursor over the stats column (set by OnGUI): the wheel scrolls it, not the slots.</summary>
        private bool statsHovered;

        // Part grid scroll: 8 columns, the rest is scrolled with the wheel.
        private const float GridScrollStep = 70f;
        private float gridScroll;
        private bool gridHovered;

        // Inventory tabs and search (reference-style window).
        private static readonly string[] TabNames = { "ВСЕ", "ДВИГАТЕЛИ", "ТОПЛИВО", "ПРОЧЕЕ" };
        private int tabIndex;
        private string search = string.Empty;
        private const string SearchControl = "BuilderSearch";

        /// <summary>The search field has keyboard focus: gameplay hotkeys (I, H, 1..9, X, Z) are muted.</summary>
        private bool searchFocused;
        private bool releaseSearchFocus;

        private readonly List<int> visibleCards = new List<int>();
        private readonly int[] placedCounts = new int[Catalog.Length];
        private string cellTooltip;

        // Slot mini-menu (RMB on a slot): quick sizes at the panel.
        private const float SlotMenuWidth = 320f;
        private const int SlotMenuIdBase = 100;      // does not overlap window ids (10..43)
        private int slotMenuIndex = -1;

        /// <summary>
        /// Whether the controls of the panel drawn right now are active: the
        /// inventory window is inert while the slot mini-menu is open (and vice versa).
        /// </summary>
        private bool controlsActive;

        private bool cursorClaimed;
        private float uiWidth = 1920f;
        private float uiHeight = 1080f;
        private float uiAlpha = 1f;

        /// <summary>Key hints panel is hidden (H or the cross on the panel).</summary>
        private static bool hintsHidden;

        private static HotSlot[] CreateHotbar()
        {
            var slots = new HotSlot[9];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new HotSlot();
            }

            return slots;
        }

        private void Start()
        {
            // Kind icons are angled 3D renders (same studio light as the preview).
            if (Builder != null)
            {
                preview.PrepareIcons(Builder.Engines, Builder.TankSideMaterial,
                    Builder.TankCapMaterial, Builder.SeparatorMaterial);

                for (int i = 0; i < Catalog.Length; i++)
                {
                    drafts[i] = DraftFor(Catalog[i]);
                }
            }

            // Starting hotbar palette: you can build right away, and dragging
            // from the inventory works as usual (slots can be swapped).
            if (Builder != null)
            {
                AssignPreset(0, DraftFor(Catalog[0]));
                AssignPreset(1, Builder.DefaultPreset(PartKind.FuelTank));
                AssignPreset(2, Builder.DefaultPreset(PartKind.Separator));
                AssignPreset(3, Builder.DefaultPreset(PartKind.Capsule));
                AssignPreset(4, Builder.DefaultPreset(PartKind.RcsBlock));
            }

            hotbarIndex = 0;
            ApplySelection();
            editSlot = hotbar[0].Occupied ? hotbar[0] : null;
        }

        private void Update()
        {
            if (Builder == null)
            {
                return;
            }

            SimulationRunner runner = Builder.Runner;
            if (runner == null)
            {
                return;
            }

            bool buildActive = runner.BuildMode && !Builder.PhysicsOn;
            if (!buildActive)
            {
                if (open)
                {
                    SetOpen(false);
                }

                openAnim = 0f;
                return;
            }

            // Ctrl — "peek": the camera releases the cursor and placement/
            // deletion are muted while held (as with the window open). An
            // expanded tablet uses the same flag: clicks on its buttons must not
            // place/delete parts behind it.
            Builder.UiOpen = open || PlayerInput.Held(GameAction.BuildPeek) || ShipTablet.AnyExpanded;

            // The station tablet took over the screen (sat in a chair) — close
            // the inventory: two fullscreen windows on top of each other are not needed.
            if (ShipTablet.AnyExpanded && open)
            {
                SetOpen(false);
            }

            openAnim = Mathf.MoveTowards(openAnim, open ? 1f : 0f, Time.unscaledDeltaTime * 9f);
            TickPreview();

            // Inventory is on I (E is taken by world interactions: chairs, tablet).
            // An expanded tablet also blocks I: two fullscreen interfaces on top
            // of each other are a mess, and there is one cursor.
            if (PlayerInput.Down(GameAction.BuildInventory) && !ShipTablet.AnyExpanded && !searchFocused)
            {
                SetOpen(!open);
            }

            if (open && PlayerInput.EscapeDown())
            {
                // First Esc leaves the search field, the next one closes the window.
                if (searchFocused)
                {
                    releaseSearchFocus = true;
                    searchFocused = false;
                }
                else
                {
                    SetOpen(false);
                }
            }

            // Typing in the search field must not trigger hotkeys.
            if (searchFocused)
            {
                return;
            }

            // H — hide/restore key hints (panel at the bottom left).
            if (PlayerInput.Down(GameAction.BuildHints))
            {
                hintsHidden = !hintsHidden;
            }

            // Wheel/digits work with the window open too (handy for tidying up).
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (PlayerInput.Down((GameAction)((int)GameAction.Hotbar1 + i)))
                {
                    SetHotbarIndex(i);
                }
            }

            // RCS ring size: X adds a block to the ring, Z removes one
            // (1/2/3/4/6/8). Ctrl is peek/undo — leave those chords alone.
            if (!PlayerInput.Held(GameAction.BuildPeek) && Builder.SelectedPreset.Kind == PartKind.RcsBlock)
            {
                if (PlayerInput.RawKeyDown(KeyCode.X))
                {
                    StepRcsCount(1);
                }

                if (PlayerInput.RawKeyDown(KeyCode.Z))
                {
                    StepRcsCount(-1);
                }
            }

            // The wheel over the stats column scrolls it (OnGUI), not the slots:
            // otherwise scrolling sizes would also switch the selected slot.
            // Same over the card grid — its scroll does not touch the slots.
            float scroll = PlayerInput.MouseScroll();
            if (Mathf.Abs(scroll) > 0.05f && !statsHovered && !gridHovered)
            {
                int step = scroll > 0f ? -1 : 1;
                SetHotbarIndex(((hotbarIndex + step) % hotbar.Length + hotbar.Length) % hotbar.Length);
            }
        }

        private void SetHotbarIndex(int index)
        {
            hotbarIndex = index;
            ApplySelection();
            // A slot was selected — the window edits IT; otherwise the kind draft.
            editSlot = index >= 0 && hotbar[index].Occupied ? hotbar[index] : null;
        }

        private void ApplySelection()
        {
            HotSlot slot = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
            bool has = slot != null && slot.Occupied;
            Builder.HasSelection = has;
            if (has)
            {
                Builder.SelectedPreset = slot.Preset;
            }
        }

        /// <summary>Change the RCS ring size of the selected slot (X grows, Z shrinks).</summary>
        private void StepRcsCount(int direction)
        {
            HotSlot slot = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
            if (slot == null || !slot.Occupied || slot.Preset.Kind != PartKind.RcsBlock)
            {
                return;
            }

            PartPreset preset = slot.Preset;
            int next = RcsBlockPlacement.Step(preset.Count, direction);
            if (next == RcsBlockPlacement.ClampCount(preset.Count))
            {
                return;
            }

            preset.Count = next;
            ApplyPresetEdit(slot, CardIndexFor(preset), preset);
            slot.IconDirty = true;
        }

        /// <summary>The inventory window is open (Esc closes it instead of opening the settings menu).</summary>
        public static bool AnyOpen { get; private set; }

        private void SetOpen(bool value)
        {
            open = value;
            AnyOpen = value;
            holdId = -1;
            if (!value)
            {
                // The window and the slot mini-menu live together: window closed
                // means the menu is closed (the window restores cursor and input).
                slotMenuIndex = -1;
            }

            if (Builder != null)
            {
                Builder.UiOpen = value;
            }

            if (Controller != null)
            {
                Controller.InputBlocked = value;
            }

            if (value)
            {
                // So the stats panel is not empty: show the selected part.
                if (statsIndex < 0)
                {
                    HotSlot current = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
                    statsIndex = current != null && current.Occupied
                        ? Mathf.Max(0, CardIndexFor(current.Preset))
                        : 0;
                }

                // The window needs a free cursor: CLAIM it (the camera stops capturing
                // and stops reading the mouse), release on close — the camera
                // re-captures by itself. Counted claims cannot get stuck released.
                FirstPersonCamera.SetCursorClaim(ref cursorClaimed, true, "BuilderUI");
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            ResetDrag();
            statsHovered = false;
            statsScrollDrag = false;
            searchFocused = false;
            releaseSearchFocus = true;
            FirstPersonCamera.SetCursorClaim(ref cursorClaimed, false, "BuilderUI");
        }

        private void OnDisable()
        {
            preview.Release();
            if (open)
            {
                SetOpen(false);
            }
        }

        private void OnDestroy()
        {
            preview.Release();
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (hotbar[i].IconOwned && hotbar[i].Icon != null)
                {
                    Destroy(hotbar[i].Icon);
                }
            }
        }

        // ─── 3D preview ─────────────────────────────────────────────────

        /// <summary>
        /// Once per frame: show the selected part with its current sizes in the
        /// preview and finish "late" slot icons. The visual is rebuilt when the
        /// part or size changes, the camera frames by dimensions, and the frame
        /// is rendered at most 30 Hz (inside Ensure).
        /// </summary>
        private void TickPreview()
        {
            if (Builder == null)
            {
                return;
            }

            // Slot icons with changed sizes: at most one render per frame (sizes
            // are edited by holding arrows, so renders don't pile up). Works with
            // the window CLOSED too: a slot can be filled by dragging and the
            // window closed right away — the icon must still become exact.
            for (int i = 0; i < hotbar.Length; i++)
            {
                HotSlot slot = hotbar[i];
                if (slot.Occupied && slot.IconDirty && CaptureSlotIcon(slot))
                {
                    break;
                }
            }

            if (openAnim <= 0.001f)
            {
                return;
            }

            PartPreset preset = StatsPreset();
            preview.Ensure(preset.Kind, Builder.MetricsFor(preset), Builder.EngineFor(preset),
                Builder.TankSideMaterial, Builder.TankCapMaterial, Builder.SeparatorMaterial);
        }

        /// <summary>Re-render the slot icon for its current sizes.</summary>
        private bool CaptureSlotIcon(HotSlot slot)
        {
            Texture2D texture = preview.CaptureIcon(slot.Preset.Kind, Builder.MetricsFor(slot.Preset),
                Builder.EngineFor(slot.Preset), Builder.TankSideMaterial, Builder.TankCapMaterial, Builder.SeparatorMaterial);
            if (texture != null)
            {
                SetIcon(slot, texture, owned: true);
            }

            // Render failed (no rig/geometry) — don't loop: the previous picture
            // stays (the default kind render).
            slot.IconDirty = false;
            return true;
        }

        /// <summary>
        /// The preset shown and edited by the window: the preset of the card's
        /// explicitly selected slot, otherwise the card's draft (preset for the
        /// next placement).
        /// </summary>
        private PartPreset StatsPreset()
        {
            int card = StatsCardIndex();
            HotSlot owner = EditSlotFor(Catalog[card]);
            return owner != null ? owner.Preset : drafts[card];
        }

        /// <summary>Stats card index (always valid).</summary>
        private int StatsCardIndex()
        {
            if (statsIndex >= 0 && statsIndex < Catalog.Length)
            {
                return statsIndex;
            }

            if (hotbarIndex >= 0 && hotbar[hotbarIndex].Occupied)
            {
                int found = CardIndexFor(hotbar[hotbarIndex].Preset);
                if (found >= 0)
                {
                    return found;
                }
            }

            return 0;
        }

        /// <summary>
        /// The slot the window edits: ONLY the explicitly selected slot of this
        /// card (same model for an engine). Otherwise null (a draft is edited):
        /// an edit must not touch neighbouring slots the player did not select.
        /// </summary>
        private HotSlot EditSlotFor(CatalogEntry entry)
        {
            return editSlot != null && editSlot.Occupied && MatchesEntry(entry, editSlot.Preset)
                ? editSlot
                : null;
        }

        /// <summary>Card matching the preset (kind + engine), or −1.</summary>
        private static int CardIndexFor(PartPreset preset)
        {
            for (int i = 0; i < Catalog.Length; i++)
            {
                if (MatchesEntry(Catalog[i], preset))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Whether the card matches the preset: the kind matches and, for an
        /// engine, the model too (an empty preset EngineId means the default engine).
        /// </summary>
        private static bool MatchesEntry(CatalogEntry entry, PartPreset preset)
        {
            if (entry.Kind != preset.Kind)
            {
                return false;
            }

            if (entry.Kind != PartKind.Engine)
            {
                return true;
            }

            string presetEngine = string.IsNullOrEmpty(preset.EngineId)
                ? EngineSpecs.Default.Id
                : preset.EngineId;
            return entry.EngineId == presetEngine;
        }

        /// <summary>Card draft: the starting preset of its kind and engine.</summary>
        private PartPreset DraftFor(CatalogEntry entry)
        {
            PartPreset preset = Builder.DefaultPreset(entry.Kind);
            if (entry.Kind == PartKind.Engine)
            {
                preset.EngineId = entry.EngineId;
            }

            return preset;
        }

        // ─── Slots ───────────────────────────────────────────────────────────

        /// <summary>A part drag is in progress (the mouse is busy).</summary>
        private bool Dragging => dragPreset.HasValue;

        /// <summary>Interface is active: the inventory window or the slot mini-menu is open.</summary>
        private bool Interactive => open || slotMenuIndex >= 0;

        /// <summary>
        /// Put a preset into a slot. The icon is the default kind render at
        /// first; the exact one (for the preset's sizes) is drawn in TickPreview.
        /// </summary>
        private void AssignPreset(int index, PartPreset preset)
        {
            HotSlot slot = hotbar[index];
            SetIcon(slot, PartIcons.Get(preset.Kind, preset.EngineId), owned: false);
            slot.Preset = preset;
            slot.Occupied = true;
            slot.IconDirty = true;
        }

        /// <summary>Clear the slot (RMB on the slot / dropped outside the panel).</summary>
        private void ClearSlot(int index)
        {
            HotSlot slot = hotbar[index];
            if (editSlot == slot)
            {
                editSlot = null;                      // window switches to the draft
            }

            SetIcon(slot, null, owned: false);
            slot.Preset = default;
            slot.Occupied = false;
            slot.IconDirty = false;
        }

        /// <summary>Swap two slots (moving = swapping).</summary>
        private void SwapSlots(int a, int b)
        {
            HotSlot first = hotbar[a];
            hotbar[a] = hotbar[b];
            hotbar[b] = first;
        }

        private static void SetIcon(HotSlot slot, Texture2D texture, bool owned)
        {
            if (slot.IconOwned && slot.Icon != null && slot.Icon != texture)
            {
                Destroy(slot.Icon);
            }

            slot.Icon = texture;
            slot.IconOwned = owned;
        }

        /// <summary>Write a preset edit: to the owning slot or to the kind draft.</summary>
        private void ApplyPresetEdit(HotSlot owner, int card, PartPreset preset)
        {
            if (owner != null)
            {
                owner.Preset = preset;
                owner.IconDirty = true;
                if (hotbarIndex >= 0 && hotbar[hotbarIndex] == owner)
                {
                    Builder.SelectedPreset = preset;   // ghost and placement update immediately
                }
            }
            else
            {
                drafts[card] = preset;
            }
        }

        private static bool SamePreset(PartPreset a, PartPreset b)
        {
            return a.Kind == b.Kind
                && a.Form == b.Form
                && a.Load == b.Load
                && a.EngineId == b.EngineId
                && a.Count == b.Count
                && Mathf.Abs(a.Radius - b.Radius) < 1e-4f
                && Mathf.Abs(a.TopRadius - b.TopRadius) < 1e-4f
                && Mathf.Abs(a.Height - b.Height) < 1e-4f;
        }

        // ─── Slot mini-menu (RMB) ───────────────────────────────────────────

        /// <summary>RMB on a slot: open the size mini-menu (or close it).</summary>
        private void ToggleSlotMenu(int index)
        {
            if (slotMenuIndex == index)
            {
                slotMenuIndex = -1;
                holdId = -1;
                return;
            }

            slotMenuIndex = hotbar[index].Occupied ? index : -1;
            holdId = -1;
        }

        /// <summary>
        /// Slot mini-menu: quick part sizes right at the panel (the same editors
        /// as in the inventory), or brief stats for an engine. It lives while
        /// the inventory window is open: cursor and input are shared, and window
        /// controls under the menu become inert (controlsActive), so clicks go
        /// to the menu.
        /// </summary>
        private void DrawSlotMenu()
        {
            if (slotMenuIndex < 0 || !open)
            {
                return;
            }

            HotSlot slot = hotbar[slotMenuIndex];
            if (!slot.Occupied)
            {
                slotMenuIndex = -1;
                return;
            }

            int card = CardIndexFor(slot.Preset);
            if (card < 0)
            {
                slotMenuIndex = -1;
                return;
            }

            PartKind kind = slot.Preset.Kind;
            bool tank = kind == PartKind.FuelTank;
            bool rcs = kind == PartKind.RcsBlock;
            bool hasSize = kind != PartKind.Engine && !rcs;
            int editors = tank ? (slot.Preset.Form == TankForm.Cone ? 3 : 2) : 1;
            float bodyH = hasSize || rcs
                ? (tank ? ShapeRowHeight + LoadRowHeight(PropellantLoads.All.Count) : 0f) + (editors * EditorHeight)
                : 3f * 26f;
            float menuH = 46f + bodyH + 16f;

            Rect slotRect = HotbarSlotRect(slotMenuIndex);
            float x = Mathf.Clamp(slotRect.center.x - (SlotMenuWidth * 0.5f), 12f, uiWidth - SlotMenuWidth - 12f);
            float y = Mathf.Max(12f, PlateRect().y - menuH - 50f);
            var menu = new Rect(x, y, SlotMenuWidth, menuH);

            // An LMB click outside the menu closes it (menu buttons don't consume the event).
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !menu.Contains(e.mousePosition))
            {
                slotMenuIndex = -1;
                holdId = -1;
                return;
            }

            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(menu, 16f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(menu, GUIContent.none, BuilderSkin.Window);

            controlsActive = true;                    // menu focused: window inert
            float px = menu.x + 16f;
            float pw = menu.width - 32f;
            float py = menu.y + 12f;

            GUI.Label(new Rect(px, py, pw, 22f),
                PartCatalog.DisplayName(kind, EngineSpecFor(slot.Preset)), BuilderSkin.NameText);
            py += 26f;
            DrawSolid(new Rect(px, py, pw, 1f), BuilderSkin.Line);
            py += 8f;

            PartPreset preset = slot.Preset;
            PartPreset before = preset;
            if (tank)
            {
                DrawShapeRow(px, ref py, pw, ref preset, slot, card);
                DrawLoadRow(px, ref py, pw, ref preset, slot, card);
                bool cone = preset.Form == TankForm.Cone;
                DrawEditor(px, ref py, pw, "Длина бака", ref preset.Height,
                    PartCatalog.TankMinHeight, PartCatalog.TankMaxHeight, SlotMenuIdBase, "F1");
                // Edits are in DIAMETER (clearer for the player and matches the
                // capsule's "Ø" label); the preset stores the radius.
                float diameter = preset.Radius * 2f;
                DrawEditor(px, ref py, pw, cone ? "Диаметр низа" : "Диаметр бака", ref diameter,
                    PartCatalog.TankMinRadius * 2f, PartCatalog.TankMaxRadius * 2f, SlotMenuIdBase + 10, "F1");
                preset.Radius = diameter * 0.5f;
                if (cone)
                {
                    float topDiameter = preset.TopRadius * 2f;
                    DrawEditor(px, ref py, pw, "Диаметр верха", ref topDiameter,
                        PartCatalog.TankMinRadius * 2f, PartCatalog.TankMaxRadius * 2f, SlotMenuIdBase + 20, "F1");
                    preset.TopRadius = topDiameter * 0.5f;
                }
            }
            else if (rcs)
            {
                int count = RcsBlockPlacement.ClampCount(preset.Count);
                DrawCountEditor(px, ref py, pw, "Блоков по кольцу", ref count, SlotMenuIdBase + 40);
                preset.Count = count;
            }
            else if (hasSize)
            {
                float diameter = preset.Radius * 2f;
                DrawEditor(px, ref py, pw, "Диаметр отделителя", ref diameter,
                    PartCatalog.SeparatorMinRadius * 2f, PartCatalog.SeparatorMaxRadius * 2f, SlotMenuIdBase, "F1");
                preset.Radius = diameter * 0.5f;
            }
            else
            {
                PartMetrics m = Builder.MetricsFor(preset);
                EngineSpec spec = EngineSpecFor(preset);
                AddRow(px, ref py, pw, "Масса", FormatMass(m.MassKg));
                AddRow(px, ref py, pw, "Тяга (вак.)", FormatThrust(spec));
                AddRow(px, ref py, pw, "Удельный импульс", FormatIsp(spec));
            }

            if (!SamePreset(before, preset))
            {
                ApplyPresetEdit(slot, card, preset);
            }

            controlsActive = open;
        }

        // ─── Drawing ───────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (Builder == null)
            {
                return;
            }

            SimulationRunner runner = Builder.Runner;
            if (runner == null)
            {
                return;
            }

            bool buildActive = runner.BuildMode && !Builder.PhysicsOn;
            bool returning = !runner.BuildMode && Builder.PhysicsOn;
            if (!buildActive && !returning)
            {
                return;
            }

            // In flight the ΔV panel lives on the ship tablet only: keep its numbers
            // ticking for the tablet, but do not draw it on the screen.
            if (!buildActive)
            {
                deltaVPanel.Hide();
            }

            // A fullscreen expanded tablet draws on top and owns the screen: the
            // builder UI is not drawn. The ΔV calculation must stay alive though —
            // the tablet reads the same Live/Plan (panel.Tick without drawing).
            if (ShipTablet.AnyExpanded)
            {
                deltaVPanel.Tick(Builder);
                return;
            }

            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            uiWidth = Screen.width / scale;
            uiHeight = Screen.height / scale;
            uiAlpha = 1f;
            BuilderSkin.Build();

            if (Event.current.type == EventType.MouseUp)
            {
                holdId = -1;                         // arrow button released
            }

            EventType et = Event.current.type;
            if ((et == EventType.MouseDown || et == EventType.ScrollWheel)
                && deltaVPanel.Captures(Event.current.mousePosition))
            {
                Event.current.Use();
            }

            if (buildActive)
            {
                DrawSummary();
                if (openAnim <= 0.001f)
                {
                    DrawHints();                     // the inventory has its own hint line
                }

                DrawHotbar();
                DrawRHold("Включение физики…");
                if (openAnim > 0.001f)
                {
                    DrawWindow();
                }

                deltaVPanel.Draw(Builder, uiWidth, uiHeight, uiAlpha);
                DrawSlotMenu();
                DrawDragIcon();
                HandleDragRelease();
            }
            else
            {
                DrawRHold("Возврат к стройке…");
                DrawPhysicsBanner();
                deltaVPanel.Tick(Builder);
            }

            GUI.color = Color.white;
            GUI.matrix = previous;
        }

        // ─── Hotbar ──────────────────────────────────────────────────────────

        /// <summary>Bounds of the hotbar row (slots + key numbers under them). No plate is drawn.</summary>
        private Rect PlateRect()
        {
            float total = (SlotSize * hotbar.Length) + (SlotGap * (hotbar.Length - 1));
            float h = SlotSize + SlotNumberRow;
            return new Rect((uiWidth - total) * 0.5f, uiHeight - h - 14f, total, h);
        }

        private Rect HotbarSlotRect(int index)
        {
            Rect plate = PlateRect();
            return new Rect(plate.x + (index * (SlotSize + SlotGap)), plate.y, SlotSize, SlotSize);
        }

        private void DrawHotbar()
        {
            Rect plate = PlateRect();
            Event current = Event.current;
            Vector2 mouse = current.mousePosition;

            DrawNamePlate(plate);

            for (int i = 0; i < hotbar.Length; i++)
            {
                Rect rect = HotbarSlotRect(i);
                bool selected = i == hotbarIndex;
                bool dropTarget = open && dragMoved && rect.Contains(mouse);
                bool hover = open && !Dragging && rect.Contains(mouse);
                HotSlot slot = hotbar[i];

                GUIStyle style = selected ? BuilderSkin.SlotSelected
                    : (dropTarget || hover) ? BuilderSkin.SlotHover : BuilderSkin.Slot;
                GUI.Box(rect, GUIContent.none, style);

                if (dropTarget)
                {
                    DrawOutline(Expand(rect, 1f), 2f, BuilderSkin.Accent);
                }

                if (slot.Occupied)
                {
                    const float inset = 7f;
                    var icon = new Rect(rect.x + inset, rect.y + inset, rect.width - (inset * 2f), rect.height - (inset * 2f));
                    bool lifted = dragMoved && dragFromHotbar == i;
                    SetColor(new Color(1f, 1f, 1f, lifted ? 0.3f : 1f));
                    Texture2D picture = slot.Icon != null ? slot.Icon : PartIcons.Get(slot.Preset.Kind, slot.Preset.EngineId);
                    GUI.DrawTexture(icon, picture, ScaleMode.ScaleToFit);
                    SetColor(Color.white);
                }

                // Key number UNDER the slot (bold white with an outline, readable on any scene).
                DrawOutlinedLabel(new Rect(rect.x, rect.yMax + 3f, rect.width, SlotNumberRow - 3f),
                    (i + 1).ToString(), BuilderSkin.SlotNumber);

                if (open && current.type == EventType.MouseDown && rect.Contains(mouse) && !Dragging)
                {
                    if (current.button == 1)
                    {
                        // RMB on a slot — the size mini-menu (a second RMB closes it).
                        ToggleSlotMenu(i);
                    }
                    else if (current.button == 0)
                    {
                        SetHotbarIndex(i);
                        if (slot.Occupied)
                        {
                            statsIndex = Mathf.Max(0, CardIndexFor(slot.Preset));
                            dragPreset = slot.Preset;
                            dragIcon = slot.Icon;
                            dragFromHotbar = i;
                            dragOrigin = mouse;
                            dragMoved = false;
                        }
                    }

                    current.Use();
                }
            }
        }

        // Plate above the hotbar: name and sizes of the selected part.
        private void DrawNamePlate(Rect plate)
        {
            HotSlot slot = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
            if (slot == null || !slot.Occupied)
            {
                return;
            }

            PartPreset preset = slot.Preset;
            PartMetrics m = Builder.MetricsFor(preset);
            string dims = string.Empty;
            if (preset.Kind == PartKind.FuelTank)
            {
                // Diameters (Ø) everywhere — the capsule label is Ø too.
                dims = "   <color=#9AA0A8>L " + m.Height.ToString("F1") + " м  ·  Ø " + (m.Radius * 2f).ToString("F1") + " м</color>";
                if (IsCone(m))
                {
                    dims = "   <color=#9AA0A8>L " + m.Height.ToString("F1") + " м  ·  Øниз "
                        + (m.Radius * 2f).ToString("F1") + " м  ·  Øверх " + (m.TopRadius * 2f).ToString("F1") + " м</color>";
                }
            }
            else if (preset.Kind == PartKind.Separator)
            {
                dims = "   <color=#9AA0A8>Ø " + (m.Radius * 2f).ToString("F1") + " м</color>";
            }
            else if (preset.Kind == PartKind.Capsule)
            {
                dims = "   <color=#9AA0A8>Ø " + (m.Radius * 2f).ToString("F1") + " м  ·  H "
                    + m.Height.ToString("F1") + " м</color>";
            }

            string name = PartCatalog.DisplayName(preset.Kind, EngineSpecFor(preset));
            if (preset.Kind == PartKind.FuelTank && IsCone(m))
            {
                name += " · конус";
            }

            if (preset.Kind == PartKind.FuelTank)
            {
                name += " · " + (preset.Load ?? PropellantLoads.Default).Id;
            }

            string text = "<b>" + name + "</b>" + dims;
            Vector2 size = BuilderSkin.PillText.CalcSize(new GUIContent(text));
            float w = size.x + 32f;
            var rect = new Rect(plate.center.x - (w * 0.5f), plate.y - 42f, w, 30f);
            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 10f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(rect, GUIContent.none, BuilderSkin.Pill);
            GUI.Label(rect, text, BuilderSkin.PillText);
        }

        // ─── Hints and summary ──────────────────────────────────────────────

        private void DrawHints()
        {
            if (hintsHidden)
            {
                DrawHintsRestore();
                return;
            }

            string[][] keys =
            {
                new[] { "I" },
                new[] { "ПКМ" },
                new[] { "ПКМ", "слот" },
                new[] { "Alt", "ПКМ" },
                new[] { "Ctrl", "Z" },
                new[] { "Колесо" },
                new[] { "R" },
                new[] { "Ctrl" },
                new[] { "X", "Z" },
                new[] { "H" }
            };
            string[] labels =
            {
                "Инвентарь",
                "Поставить деталь",
                "Размеры слота",
                "Удалить по прицелу",
                "Убрать последнюю",
                "Выбор слота",
                "Физика (удерж. 1 с)",
                "Курсор (удерж.)",
                "РСУ: блоков в кольце",
                "Скрыть подсказки"
            };

            const float rowH = 26f;
            const float col = 92f;
            float h = (rowH * labels.Length) + 20f;
            var panel = new Rect(16f, uiHeight - h - 16f, 268f, h);
            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(panel, 14f), GUIContent.none, BuilderSkin.Glow);
            SetColor(new Color(1f, 1f, 1f, 0.98f));
            GUI.Box(panel, GUIContent.none, BuilderSkin.Plate);
            SetColor(Color.white);

            // Cross in the panel corner — hide the hints (the click works with a
            // free cursor: inventory window open or cursor released).
            var hide = new Rect(panel.xMax - 30f, panel.y + 5f, 22f, 22f);
            if (HintButton(hide, BuilderSkin.Cross, 12f))
            {
                hintsHidden = true;
            }

            for (int i = 0; i < labels.Length; i++)
            {
                float y = panel.y + 10f + (i * rowH);
                float x = panel.x + 14f;
                for (int k = 0; k < keys[i].Length; k++)
                {
                    float kw = Mathf.Max(26f, BuilderSkin.KeyText.CalcSize(new GUIContent(keys[i][k])).x + 14f);
                    var keyRect = new Rect(x, y + 2f, kw, 22f);
                    GUI.Box(keyRect, GUIContent.none, BuilderSkin.Key);
                    GUI.Label(keyRect, keys[i][k], BuilderSkin.KeyText);
                    x += kw + 4f;
                }

                GUI.Label(new Rect(panel.x + 14f + col, y, panel.width - col - 34f, rowH), labels[i], BuilderSkin.Muted);
            }
        }

        /// <summary>Collapsed "Hints" plate in place of the hidden panel.</summary>
        private void DrawHintsRestore()
        {
            const string text = "Подсказки (H)";
            Vector2 size = BuilderSkin.PillText.CalcSize(new GUIContent(text));
            float w = size.x + 52f;
            var rect = new Rect(16f, uiHeight - 46f, w, 30f);

            bool interactive = Cursor.lockState != CursorLockMode.Locked;
            bool hover = interactive && rect.Contains(Event.current.mousePosition);

            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 10f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(rect, GUIContent.none, hover ? BuilderSkin.SlotHover : BuilderSkin.Pill);

            var mark = new Rect(rect.x + 8f, rect.y + 5f, 20f, 20f);
            GUI.Box(mark, GUIContent.none, BuilderSkin.Key);
            GUI.Label(mark, "?", BuilderSkin.KeyText);
            GUI.Label(new Rect(rect.x + 34f, rect.y, rect.width - 40f, rect.height), text, BuilderSkin.PillText);

            Event e = Event.current;
            if (interactive && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                hintsHidden = false;
                e.Use();
            }
        }

        /// <summary>Small hints icon button (cross): clicks with a free cursor.</summary>
        private bool HintButton(Rect rect, Texture2D icon, float iconSize)
        {
            Event e = Event.current;
            bool interactive = Cursor.lockState != CursorLockMode.Locked;
            bool hover = interactive && rect.Contains(e.mousePosition);

            GUI.Box(rect, GUIContent.none, hover ? BuilderSkin.StepHover : BuilderSkin.Step);
            SetColor(new Color(0.85f, 0.87f, 0.89f, 0.95f));
            GUI.DrawTexture(new Rect(rect.center.x - (iconSize * 0.5f), rect.center.y - (iconSize * 0.5f), iconSize, iconSize), icon);
            SetColor(Color.white);

            if (interactive && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }

            return false;
        }

        // Top plate: part count, mass and stack height.
        private void DrawSummary()
        {
            var parts = Builder.Parts;
            if (parts == null || parts.Count == 0)
            {
                return;
            }

            double mass = 0d;
            float low = float.MaxValue;
            float high = float.MinValue;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart part = parts[i];
                mass += part.Metrics.MassKg;
                low = Mathf.Min(low, part.Pivot.y + part.Metrics.Bottom);
                high = Mathf.Max(high, part.Pivot.y + part.Metrics.Top);
            }

            string text = "<b>" + parts.Count + "</b> дет.   ·   <b>" + FormatMass(mass) + "</b>   ·   высота <b>"
                + Mathf.Max(0f, high - low).ToString("F1") + " м</b>";
            Vector2 size = BuilderSkin.PillText.CalcSize(new GUIContent(text));
            float w = size.x + 40f;
            var rect = new Rect((uiWidth - w) * 0.5f, 18f, w, 34f);
            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 10f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(rect, GUIContent.none, BuilderSkin.Pill);
            GUI.Label(rect, text, BuilderSkin.PillText);
        }

        private void DrawRHold(string caption)
        {
            float progress = Builder.RHoldProgress;
            if (progress <= 0.001f)
            {
                return;
            }

            Rect plate = PlateRect();
            var bar = new Rect(plate.center.x - 130f, plate.y - 96f, 260f, 14f);
            GUI.Label(new Rect(bar.x, bar.y - 24f, bar.width, 20f), caption, BuilderSkin.NameTextCenter);
            GUI.Box(bar, GUIContent.none, BuilderSkin.Track);

            // Fill: full plate clipped by a group to the required width.
            float fillW = Mathf.Max(14f, bar.width * Mathf.Clamp01(progress));
            GUI.BeginGroup(new Rect(bar.x, bar.y, fillW, bar.height));
            SetColor(BuilderSkin.Accent);
            GUI.Box(new Rect(0f, 0f, bar.width, bar.height), GUIContent.none, BuilderSkin.StepActive);
            SetColor(Color.white);
            GUI.EndGroup();
        }

        private void DrawPhysicsBanner()
        {
            const string text = "Физика включена   ·   удерживайте <b>R</b> (1 с), чтобы вернуться к стройке";
            Vector2 size = BuilderSkin.PillText.CalcSize(new GUIContent(text));
            float w = size.x + 64f;
            var rect = new Rect((uiWidth - w) * 0.5f, uiHeight - 62f, w, 38f);
            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 12f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(rect, GUIContent.none, BuilderSkin.Pill);

            float pulse = 0.55f + (0.45f * Mathf.Sin(Time.unscaledTime * 4f));
            SetColor(new Color(BuilderSkin.Good.r, BuilderSkin.Good.g, BuilderSkin.Good.b, pulse));
            GUI.DrawTexture(new Rect(rect.x + 18f, rect.y + 14f, 10f, 10f), BuilderSkin.Dot);
            SetColor(Color.white);
            GUI.Label(new Rect(rect.x + 24f, rect.y, rect.width - 24f, rect.height), text, BuilderSkin.PillText);
        }

        // ─── Inventory window ──────────────────────────────────────────────────

        private void DrawWindow()
        {
            float eased = 1f - ((1f - openAnim) * (1f - openAnim));
            uiAlpha = eased;

            // While the slot mini-menu is open, the window controls are inert:
            // clicks under the menu must reach the menu, not the window cards/editors.
            controlsActive = open && slotMenuIndex < 0;

            // The window never leaves the screen (clamped to the viewport) and
            // never grows to fit the content: overflow scrolls in the right column.
            float winW = Mathf.Max(640f, uiWidth - WindowLeftMargin - WindowRightReserve);
            float winTop = Mathf.Max(WindowTop, uiHeight * 0.065f);
            float winH = Mathf.Max(WindowMinHeight,
                Mathf.Min(uiHeight * WindowHeightFraction, uiHeight - WindowBottomReserve - winTop));
            float slide = (1f - eased) * 18f;
            var win = new Rect(WindowLeftMargin, winTop + slide, winW, winH);

            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(win, 26f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(win, GUIContent.none, BuilderSkin.Window);

            const float pad = 14f;
            float rightW = Mathf.Clamp(win.width * 0.26f, 380f, 520f);
            rightW = Mathf.Min(rightW, win.width - 360f);
            float rightX = win.xMax - pad - rightW;

            // Top row: tabs on the left, search over the right column.
            var tabsRow = new Rect(win.x + pad, win.y + pad, rightX - pad - (win.x + pad), TabHeight);
            var searchRect = new Rect(rightX, win.y + pad, rightW, TabHeight);
            DrawTabs(tabsRow);
            DrawSearch(searchRect);

            float bodyY = win.y + pad + TabHeight + 10f;
            float bodyH = win.yMax - pad - bodyY;
            var grid = new Rect(win.x + pad, bodyY, rightX - pad - (win.x + pad), bodyH);
            var stats = new Rect(rightX, bodyY, rightW, bodyH);

            DrawGrid(grid);
            DrawStats(stats);
            DrawFooter(win);
            DrawCellTooltip();

            uiAlpha = 1f;
            GUI.color = Color.white;
        }

        // ─── Tabs, search, footer ────────────────────────────────────────

        /// <summary>Which tab a part kind lives in (0 = ВСЕ is handled by the caller).</summary>
        private static bool InTab(int tab, PartKind kind)
        {
            switch (tab)
            {
                case 1: return kind == PartKind.Engine || kind == PartKind.RcsBlock;
                case 2: return kind == PartKind.FuelTank;
                case 3: return kind == PartKind.Separator || kind == PartKind.Capsule;
                default: return true;
            }
        }

        private bool MatchesSearch(CatalogEntry entry)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            EngineSpec spec = EngineSpecFor(entry);
            string hay = (PartCatalog.DisplayName(entry.Kind, spec) + " " + KindLabel(entry.Kind, spec)).ToLowerInvariant();
            return hay.Contains(search.Trim().ToLowerInvariant());
        }

        private void DrawTabs(Rect row)
        {
            Event e = Event.current;
            float x = row.x;
            for (int i = 0; i < TabNames.Length; i++)
            {
                float w = Mathf.Max(72f, BuilderSkin.TabText.CalcSize(new GUIContent(TabNames[i])).x + 40f);
                var rect = new Rect(x, row.y, w, row.height);
                bool active = i == tabIndex;
                bool hover = controlsActive && !Dragging && rect.Contains(e.mousePosition);

                GUI.Box(rect, GUIContent.none, hover && !active ? BuilderSkin.TabHover : BuilderSkin.Tab);
                DrawSolid(new Rect(rect.x + 2f, rect.yMax - 3f, rect.width - 4f, 3f), BuilderSkin.TabLine);
                GUI.Label(rect, TabNames[i], active ? BuilderSkin.TabTextActive : BuilderSkin.TabText);

                if (controlsActive && !Dragging && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
                {
                    tabIndex = i;
                    gridScroll = 0f;
                    e.Use();
                }

                x += w + 6f;
            }
        }

        private void DrawSearch(Rect rect)
        {
            if (releaseSearchFocus)
            {
                GUI.FocusControl(null);
                releaseSearchFocus = false;
            }

            Event e = Event.current;
            GUI.Box(rect, GUIContent.none, BuilderSkin.SearchBox);

            // A click anywhere outside the field takes the keyboard focus away.
            if (e.type == EventType.MouseDown && !rect.Contains(e.mousePosition))
            {
                GUI.FocusControl(null);
            }

            GUI.SetNextControlName(SearchControl);
            search = GUI.TextField(rect, search, 40, BuilderSkin.SearchField);
            searchFocused = open && GUI.GetNameOfFocusedControl() == SearchControl;

            if (string.IsNullOrEmpty(search) && !searchFocused)
            {
                GUI.Label(new Rect(rect.x + 14f, rect.y, rect.width - 60f, rect.height), "Поиск ...", BuilderSkin.SearchPlaceholder);
            }

            SetColor(new Color(0.72f, 0.74f, 0.77f, 1f));
            GUI.DrawTexture(new Rect(rect.xMax - 36f, rect.center.y - 11f, 22f, 22f), BuilderSkin.Search);
            SetColor(Color.white);
        }

        /// <summary>
        /// Under the window: hint and part counter on the left, "Clear" and
        /// "Close" on the right (the close tab hangs under the window corner).
        /// </summary>
        private void DrawFooter(Rect win)
        {
            var closeRect = new Rect(win.xMax - 160f, win.yMax + 4f, 160f, 40f);
            var clearRect = new Rect(closeRect.x - 12f - 190f, closeRect.y, 190f, 40f);

            DrawOutlinedLabel(new Rect(win.x + 6f, win.yMax + 6f, clearRect.x - win.x - 18f, 36f),
                "Перетащите деталь в нижнюю панель · двойной клик — в свободный слот      Поставлено: "
                + Builder.Parts.Count, BuilderSkin.Muted);

            if (DrawTextButton(clearRect, "Очистить площадку", BuilderSkin.Danger, BuilderSkin.DangerHover, BuilderSkin.DangerButtonText))
            {
                Builder.ClearAll();
            }

            if (DrawTextButton(closeRect, "ЗАКРЫТЬ", BuilderSkin.Tab, BuilderSkin.TabHover, BuilderSkin.TabText))
            {
                SetOpen(false);
            }
        }

        /// <summary>Hover name of a grid cell (cells show only the picture).</summary>
        private void DrawCellTooltip()
        {
            if (string.IsNullOrEmpty(cellTooltip) || !controlsActive || Dragging)
            {
                return;
            }

            Vector2 mouse = Event.current.mousePosition;
            Vector2 size = BuilderSkin.PillText.CalcSize(new GUIContent(cellTooltip));
            float w = size.x + 28f;
            var rect = new Rect(Mathf.Min(mouse.x + 16f, uiWidth - w - 8f), mouse.y + 20f, w, 28f);
            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 8f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(rect, GUIContent.none, BuilderSkin.Pill);
            GUI.Label(rect, cellTooltip, BuilderSkin.PillText);
        }

        /// <summary>
        /// Stats panel with SCROLLING: a cone tank has more rows and editors than
        /// fit the window, so the window does not grow — the content is scrolled
        /// with the wheel or the thumb at the right edge.
        /// </summary>
        private void DrawStats(Rect r)
        {
            statsHovered = r.Contains(Event.current.mousePosition);
            StatsLayout layout = ComputeStatsLayout(r.width, r.height);
            float maxScroll = Mathf.Max(0f, layout.ContentHeight - r.height);
            if (maxScroll <= 0f)
            {
                statsScroll = 0f;
            }

            statsScroll = Mathf.Clamp(statsScroll, 0f, maxScroll);
            HandleStatsScroll(r, maxScroll);

            GUI.BeginGroup(r);
            DrawStatsContent(r.width, layout, statsScroll);
            GUI.EndGroup();

            if (maxScroll > 0f)
            {
                DrawStatsScrollbar(r, layout.ContentHeight, maxScroll);
            }
        }

        /// <summary>Stats column layout: what we draw and how much space it takes.</summary>
        private struct StatsLayout
        {
            public int Card;
            public PartKind Kind;
            public HotSlot Owner;
            public PartPreset Preset;
            public PartMetrics Metrics;
            public bool Tank;
            public int Rows;
            public int Editors;
            public float PreviewH;
            public string Description;
            public float DescHeight;
            public float ContentHeight;
        }

        private StatsLayout ComputeStatsLayout(float width, float viewHeight)
        {
            int card = StatsCardIndex();
            CatalogEntry entry = Catalog[card];
            PartKind kind = entry.Kind;
            HotSlot owner = EditSlotFor(entry);
            PartPreset preset = owner != null ? owner.Preset : drafts[card];
            PartMetrics m = Builder.MetricsFor(preset);

            bool tank = kind == PartKind.FuelTank;
            bool engine = kind == PartKind.Engine;
            bool capsule = kind == PartKind.Capsule;
            bool rcs = kind == PartKind.RcsBlock;

            // Stats rows: an engine has mass, propellant, thrust, Isp, flow,
            // expansion ratio, joint strength. A capsule has mass, diameter,
            // height and joint strength; it has no sizes.
            int rows = engine ? 7 : (kind == PartKind.FuelTank ? 7 : (capsule ? 4 : (rcs ? 4 : 3)));
            int editors = engine || capsule ? 0 : (tank ? (IsCone(m) ? 3 : 2) : 1);

            // Editors (shape, load, sizes): heading + label/button rows. A size
            // editor takes a label + a button row (EditorHeight). A tank adds
            // two rows: tank shape and load.
            float extras = editors > 0
                ? 26f + (tank ? ShapeRowHeight + LoadRowHeight(PropellantLoads.All.Count) : 0f) + (editors * EditorHeight)
                : 0f;

            // The column is a stack of rounded blocks: preview (with the name
            // inside), stat pills, description, then the editors.
            float w = width - StatsGutter;
            float previewH = Mathf.Clamp(w * 0.80f, 220f, 340f);
            string description = KindDescription(kind, EngineSpecFor(preset));
            float descH = BuilderSkin.DescText.CalcHeight(new GUIContent(description), w - 28f) + 22f;
            float pills = rows * (StatPillHeight + StatPillGap);

            return new StatsLayout
            {
                Card = card,
                Kind = kind,
                Owner = owner,
                Preset = preset,
                Metrics = m,
                Tank = tank,
                Rows = rows,
                Editors = editors,
                PreviewH = previewH,
                Description = description,
                DescHeight = descH,
                ContentHeight = previewH + 8f + pills + 4f + descH + 10f + extras + 16f,
            };
        }

        /// <summary>Right gutter of the stats column (the scrollbar lives there).</summary>
        private const float StatsGutter = 14f;

        // Content is drawn in the panel group's LOCAL coordinates; scroll is how
        // many content pixels have moved up.
        private void DrawStatsContent(float width, StatsLayout layout, float scroll)
        {
            float x = 0f;
            float w = width - StatsGutter;
            float y = -scroll;

            PartKind kind = layout.Kind;
            PartMetrics m = layout.Metrics;
            const float nameH = 42f;

            // Preview: the 3D frame with the part name at the bottom, inside one rounded block.
            var viewport = new Rect(x, y, w, layout.PreviewH);
            GUI.Box(viewport, GUIContent.none, BuilderSkin.Viewport);

            Texture frame = preview.Texture;
            if (frame != null)
            {
                var inner = new Rect(viewport.x + 3f, viewport.y + 3f, viewport.width - 6f, viewport.height - nameH - 3f);
                GUI.DrawTexture(inner, frame, ScaleMode.ScaleToFit);
                GUI.DrawTexture(inner, BuilderSkin.Vignette, ScaleMode.StretchToFill);
            }

            GUI.Label(new Rect(viewport.x, viewport.yMax - nameH, viewport.width, nameH),
                PartCatalog.DisplayName(kind, EngineSpecFor(layout.Preset)).ToUpperInvariant(), BuilderSkin.PreviewName);

            y = viewport.yMax + 8f;

            AddStat(x, ref y, w, "Масса", FormatMass(m.MassKg));
            switch (kind)
            {
                case PartKind.Engine:
                {
                    EngineSpec spec = EngineSpecFor(layout.Preset);
                    BipropellantMix mix = spec != null ? spec.Mix : PropellantPairs.Default.Mix;
                    AddStat(x, ref y, w, "Топливо", mix.Oxidizer.Id + " + " + mix.Fuel.Id);
                    AddStat(x, ref y, w, "Тяга (вак.)", FormatThrust(spec));
                    AddStat(x, ref y, w, "Удельный импульс", FormatIsp(spec));
                    AddStat(x, ref y, w, "Расход",
                        mix.MassFlowKgS.ToString("0.#") + " кг/с · O/F "
                        + mix.OxidizerToFuelMassRatio.ToString("0.##"));
                    AddStat(x, ref y, w, "Степень расширения",
                        spec != null ? spec.ExpansionRatio.ToString("0.#") + ":1" : "—");
                    break;
                }
                case PartKind.FuelTank:
                    AddStat(x, ref y, w, "Топливо", m.Load != null ? m.Load.DisplayName : "—");
                    AddStat(x, ref y, w, "Объём",
                        m.VolumeM3.ToString("F1") + " м³ (" + (m.VolumeM3 * 1000d).ToString("F0") + " л)");
                    double tankUsable = UsableVolumeM3(m);
                    AddStat(x, ref y, w, "Залито",
                        tankUsable.ToString("F1") + " м³ (" + (tankUsable * 1000d).ToString("F0") + " л)");
                    AddStat(x, ref y, w, "Топливо в баке", FormatMass(m.PropellantMassKg));
                    AddStat(x, ref y, w, "Сухая масса", FormatMass(m.DryMassKg));
                    break;
                case PartKind.Separator:
                    AddStat(x, ref y, w, "Толщина", "40 мм");
                    break;
                case PartKind.Capsule:
                    AddStat(x, ref y, w, "Диаметр", (m.Radius * 2f).ToString("F1") + " м");
                    AddStat(x, ref y, w, "Высота", m.Height.ToString("F1") + " м");
                    break;
                case PartKind.RcsBlock:
                {
                    int ring = RcsBlockPlacement.ClampCount(layout.Preset.Count);
                    AddStat(x, ref y, w, "Блоков в кольце", ring + " шт.");
                    AddStat(x, ref y, w, "Сопел", (ring * 5) + " × 450 Н");
                    break;
                }
            }

            AddStat(x, ref y, w, "Прочность стыка", (m.JointStrengthNewtons / 1e6).ToString("F1") + " МН");

            // Description block (text per part kind, see KindDescription).
            y += 4f;
            var descBox = new Rect(x, y, w, layout.DescHeight);
            GUI.Box(descBox, GUIContent.none, BuilderSkin.DescBox);
            GUI.Label(new Rect(descBox.x + 14f, descBox.y + 11f, descBox.width - 28f, descBox.height - 16f),
                layout.Description, BuilderSkin.DescText);
            y = descBox.yMax + 10f;

            if (kind == PartKind.Engine || kind == PartKind.Capsule)
            {
                // An engine has no sizes: the model is chosen by the inventory
                // card (each engine has its own card). Capsule sizes are fixed
                // by the Orion model.
                return;
            }

            DrawHeading(new Rect(x, y, w, 16f), "РАЗМЕРЫ");
            y += 26f;

            PartPreset preset = layout.Preset;
            if (kind == PartKind.FuelTank)
            {
                // Edits accumulate in the preset and are written to the owning
                // slot/draft in one go — otherwise every arrow step would dirty the icon.
                PartPreset before = preset;
                DrawShapeRow(x, ref y, w, ref preset, layout.Owner, layout.Card);
                DrawLoadRow(x, ref y, w, ref preset, layout.Owner, layout.Card);
                bool cone = preset.Form == TankForm.Cone;
                DrawEditor(x, ref y, w, "Длина бака", ref preset.Height,
                    PartCatalog.TankMinHeight, PartCatalog.TankMaxHeight, 10, "F1");
                // Edits are in DIAMETER (the preset stores the radius).
                float diameter = preset.Radius * 2f;
                DrawEditor(x, ref y, w, cone ? "Диаметр низа" : "Диаметр бака", ref diameter,
                    PartCatalog.TankMinRadius * 2f, PartCatalog.TankMaxRadius * 2f, 20, "F1");
                preset.Radius = diameter * 0.5f;
                if (cone)
                {
                    float topDiameter = preset.TopRadius * 2f;
                    DrawEditor(x, ref y, w, "Диаметр верха", ref topDiameter,
                        PartCatalog.TankMinRadius * 2f, PartCatalog.TankMaxRadius * 2f, 30, "F1");
                    preset.TopRadius = topDiameter * 0.5f;
                }

                if (!SamePreset(before, preset))
                {
                    ApplyPresetEdit(layout.Owner, layout.Card, preset);
                }
            }
            else if (kind == PartKind.RcsBlock)
            {
                PartPreset before = preset;
                int count = RcsBlockPlacement.ClampCount(preset.Count);
                DrawCountEditor(x, ref y, w, "Блоков по кольцу", ref count, 40);
                preset.Count = count;
                if (!SamePreset(before, preset))
                {
                    ApplyPresetEdit(layout.Owner, layout.Card, preset);
                }
            }
            else
            {
                PartPreset before = preset;
                float diameter = preset.Radius * 2f;
                DrawEditor(x, ref y, w, "Диаметр отделителя", ref diameter,
                    PartCatalog.SeparatorMinRadius * 2f, PartCatalog.SeparatorMaxRadius * 2f, 40, "F1");
                preset.Radius = diameter * 0.5f;
                if (!SamePreset(before, preset))
                {
                    ApplyPresetEdit(layout.Owner, layout.Card, preset);
                }
            }
        }

        /// <summary>The wheel over the stats panel scrolls the column.</summary>
        private void HandleStatsScroll(Rect r, float maxScroll)
        {
            if (maxScroll <= 0f)
            {
                return;
            }

            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition))
            {
                // Sign: wheel away from the viewer (delta.y > 0) scrolls down,
                // towards the lower rows — as in Unity windows.
                statsScroll = Mathf.Clamp(statsScroll + (e.delta.y * StatsScrollStep), 0f, maxScroll);
                e.Use();
            }
        }

        /// <summary>The wheel over the card grid scrolls it (more parts than 6×3).</summary>
        private void HandleGridScroll(Rect r, float maxScroll)
        {
            if (maxScroll <= 0f)
            {
                return;
            }

            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition))
            {
                gridScroll = Mathf.Clamp(gridScroll + (e.delta.y * GridScrollStep), 0f, maxScroll);
                e.Use();
            }
        }

        // Thumb at the panel's right edge: a strip in the panel field plus dragging.
        private void DrawStatsScrollbar(Rect r, float contentHeight, float maxScroll)
        {
            var track = new Rect(r.xMax - 11f, r.y + 6f, 5f, r.height - 12f);
            DrawSolid(track, new Color(1f, 1f, 1f, 0.06f));

            float thumbH = Mathf.Max(30f, track.height * (r.height / contentHeight));
            float t = maxScroll > 0f ? statsScroll / maxScroll : 0f;
            var thumb = new Rect(track.x - 1f, Mathf.Lerp(track.y, track.yMax - thumbH, t), track.width + 2f, thumbH);

            Event e = Event.current;
            bool hover = controlsActive && thumb.Contains(e.mousePosition);
            if (controlsActive && e.type == EventType.MouseDown && e.button == 0 && hover && !Dragging)
            {
                statsScrollDrag = true;
                statsScrollDragOrigin = e.mousePosition.y;
                statsScrollDragStart = statsScroll;
                e.Use();
            }

            if (statsScrollDrag && e.type == EventType.MouseDrag)
            {
                float span = track.height - thumbH;
                if (span > 1f)
                {
                    float dy = e.mousePosition.y - statsScrollDragOrigin;
                    statsScroll = Mathf.Clamp(statsScrollDragStart + ((dy / span) * maxScroll), 0f, maxScroll);
                }

                e.Use();
            }

            if (e.type == EventType.MouseUp)
            {
                statsScrollDrag = false;
            }

            DrawSolid(thumb, hover || statsScrollDrag ? BuilderSkin.Accent : new Color(1f, 1f, 1f, 0.28f));
        }

        /// <summary>Height of the tank shape row (for the preview frame calculation).</summary>
        private const float ShapeRowHeight = 66f;

        /// <summary>
        /// Load row height: composition and mass labels (36) + one button per
        /// registry load (34) with a 4 gap between them.
        /// </summary>
        private static float LoadRowHeight(int loads)
        {
            return 46f + (loads * 34f) + ((loads - 1) * 4f);
        }

        /// <summary>Size editor height: label (22) + button row (62).</summary>
        private const float EditorHeight = 84f;

        // "Tank shape" row: two segment buttons — cylinder and cone adapter.
        // The choice immediately updates the ghost, the placed part and the preview.
        private void DrawShapeRow(float x, ref float y, float w, ref PartPreset preset, HotSlot owner, int card)
        {
            GUI.Label(new Rect(x, y, w * 0.6f, 18f), "Форма бака", BuilderSkin.Body);
            GUI.Label(new Rect(x + (w * 0.4f), y, w * 0.6f, 18f), "цилиндр / конус", BuilderSkin.TinyRight);
            y += 22f;

            const float gap = 4f;
            float buttonWidth = (w - gap) * 0.5f;
            var cylinder = new Rect(x, y, buttonWidth, 34f);
            var cone = new Rect(x + buttonWidth + gap, y, buttonWidth, 34f);

            if (ShapeButton(cylinder, "Цилиндр", preset.Form != TankForm.Cone))
            {
                SetTankForm(ref preset, owner, card, TankForm.Cylinder);
            }

            if (ShapeButton(cone, "Конус-переходник", preset.Form == TankForm.Cone))
            {
                SetTankForm(ref preset, owner, card, TankForm.Cone);
            }

            y += 44f;
        }

        /// <summary>
        /// "Load" row: buttons per the PropellantLoads registry (a new load
        /// appears in the UI automatically). The choice immediately updates the
        /// ghost, the placed part and the preview — tank mass is derived from the load.
        /// </summary>
        private void DrawLoadRow(float x, ref float y, float w, ref PartPreset preset, HotSlot owner, int card)
        {
            GUI.Label(new Rect(x, y, w * 0.4f, 18f), "Наполнение", BuilderSkin.Body);
            GUI.Label(new Rect(x + (w * 0.3f), y, w * 0.7f, 18f),
                LoadComposition(preset.Load ?? PropellantLoads.Default), BuilderSkin.TinyRight);

            // Full load: percentage (above, by volume) and the mass of each
            // component at the current tank sizes, in kg.
            PartMetrics m = Builder.MetricsFor(preset);
            GUI.Label(new Rect(x, y + 18f, w, 14f),
                LoadComponentMasses(m, preset.Load ?? PropellantLoads.Default), BuilderSkin.TinyRight);
            y += 36f;

            // One button per registry load, stacked full width: the long pair
            // names get clipped when put in a row.
            IReadOnlyList<PropellantLoad> all = PropellantLoads.All;
            PropellantLoad current = preset.Load ?? PropellantLoads.Default;
            for (int i = 0; i < all.Count; i++)
            {
                var rect = new Rect(x, y + (i * 38f), w, 34f);
                if (ShapeButton(rect, all[i].DisplayName, all[i] == current) && all[i] != current)
                {
                    preset.Load = all[i];
                    ApplyPresetEdit(owner, card, preset);
                }
            }

            y += (all.Count * 34f) + ((all.Count - 1) * 4f);
        }

        /// <summary>Load composition by volume fraction: "LOX 27% · LH2 73% by volume".</summary>
        private static string LoadComposition(PropellantLoad load)
        {
            string text = string.Empty;
            for (int i = 0; i < load.Count; i++)
            {
                if (i > 0)
                {
                    text += " · ";
                }

                text += load.Component(i).Id + " " + (load.VolumeFraction(i) * 100d).ToString("F0") + "%";
            }

            return text + " по объёму";
        }

        /// <summary>Component masses at full load: "LOX 30610 kg · LH2 5076 kg".</summary>
        private static string LoadComponentMasses(PartMetrics metrics, PropellantLoad load)
        {
            if (load == null || metrics.VolumeM3 <= 0d)
            {
                return string.Empty;
            }

            string text = string.Empty;
            for (int i = 0; i < load.Count; i++)
            {
                if (i > 0)
                {
                    text += " · ";
                }

                text += load.Component(i).Id + " "
                    + load.ComponentMassKg(i, metrics.VolumeM3, 1d).ToString("F0") + " кг";
            }

            return text;
        }

        /// <summary>Fillable volume (no ullage), m³ — litres match the fuelling.</summary>
        private static double UsableVolumeM3(PartMetrics metrics)
        {
            if (metrics.Load == null || metrics.VolumeM3 <= 0d)
            {
                return 0d;
            }

            double usable = 0d;
            for (int i = 0; i < metrics.Load.Count; i++)
            {
                usable += metrics.Load.UsableVolumeM3(i, metrics.VolumeM3);
            }

            return usable;
        }

        private bool ShapeButton(Rect rect, string text, bool selected)
        {
            Event e = Event.current;
            bool hover = controlsActive && !Dragging && rect.Contains(e.mousePosition);
            GUI.Box(rect, GUIContent.none, selected ? BuilderSkin.StepActive : hover ? BuilderSkin.StepHover : BuilderSkin.Step);
            GUI.Label(rect, text, BuilderSkin.ButtonText);

            if (controlsActive && e.type == EventType.MouseDown && e.button == 0 && !Dragging && rect.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Switch the preset's shape. On the first switch to a cone, if the top
        /// radius still equals the bottom one (the tank would look like a
        /// cylinder), the step is set to half — an adapter preset the player then
        /// edits themselves; the step is rounded to 0.1 m so steps give "round" numbers.
        /// </summary>
        private void SetTankForm(ref PartPreset preset, HotSlot owner, int card, TankForm form)
        {
            if (preset.Form == form)
            {
                return;
            }

            preset.Form = form;
            if (form == TankForm.Cone
                && Mathf.Abs(preset.TopRadius - preset.Radius) < 0.001f)
            {
                float half = Mathf.Round(preset.Radius * 5f) / 10f;
                preset.TopRadius = Mathf.Clamp(half, PartCatalog.TankMinRadius, PartCatalog.TankMaxRadius);
            }

            ApplyPresetEdit(owner, card, preset);
        }

        private void DrawHeading(Rect rect, string text)
        {
            DrawSolid(new Rect(rect.x, rect.y + 2f, 3f, rect.height - 4f), BuilderSkin.Accent);
            GUI.Label(new Rect(rect.x + 10f, rect.y, rect.width - 10f, rect.height), text, BuilderSkin.Heading);
        }

        /// <summary>Stat as a rounded pill: "Масса: 25 кг" (reference-style).</summary>
        private static void AddStat(float x, ref float y, float w, string left, string right)
        {
            var rect = new Rect(x, y, w, StatPillHeight);
            GUI.Box(rect, GUIContent.none, BuilderSkin.Stat);
            GUI.Label(new Rect(rect.x + 14f, rect.y, rect.width - 22f, rect.height), left + ": " + right, BuilderSkin.StatText);
            y += StatPillHeight + StatPillGap;
        }

        /// <summary>
        /// Description under the stats (bold last line = the main gameplay note).
        /// Plain text per kind — edit the wording here.
        /// </summary>
        private static string KindDescription(PartKind kind, EngineSpec engine)
        {
            switch (kind)
            {
                case PartKind.Engine:
                    return "Жидкостный ракетный двигатель. Тяга и удельный импульс заданы моделью; у каждой модели своя карточка в каталоге."
                        + (engine != null && engine.VacuumOnly ? " Рассчитан на работу в вакууме." : string.Empty)
                        + "\n<b>Размеры двигателя не настраиваются.</b>";
                case PartKind.FuelTank:
                    return "Топливный бак: цилиндр или конус-переходник между ступенями разного диаметра. Длина, диаметр и состав топлива настраиваются."
                        + "\n<b>Масса бака зависит от наполнения.</b>";
                case PartKind.Separator:
                    return "Отделитель ступеней. Самое слабое место стыка: при разделении рвётся первым."
                        + "\n<b>Диаметр отделителя настраивается.</b>";
                case PartKind.Capsule:
                    return "Экипажный модуль Orion. Габариты и масса определяются моделью."
                        + "\n<b>Размеры фиксированные.</b>";
                default:
                    return "Блок системы ориентации (РСУ) с пятью соплами. Ставится радиально по кольцу вокруг бака."
                        + "\n<b>Блоков в кольце: клавиши X / Z.</b>";
            }
        }

        private static void AddRow(float x, ref float y, float w, string left, string right)
        {
            GUI.Label(new Rect(x, y, w * 0.55f, 24f), left, BuilderSkin.Muted);
            GUI.Label(new Rect(x + (w * 0.45f), y, w * 0.55f, 24f), right, BuilderSkin.BodyRight);
            y += 26f;
        }

        // Size editor: [««][‹]  value  [›][»»]. Big arrows — 1 m, small ones —
        // 0.1 m; holding repeats the step.
        private void DrawEditor(float x, ref float y, float w, string label, ref float value,
            float min, float max, int idBase, string format)
        {
            GUI.Label(new Rect(x, y, w * 0.6f, 18f), label, BuilderSkin.Body);
            GUI.Label(new Rect(x + (w * 0.4f), y, w * 0.6f, 18f),
                min.ToString("0.##") + " – " + max.ToString("0.##") + " м", BuilderSkin.TinyRight);
            y += 22f;

            const float btn = 36f;
            const float gap = 4f;
            float cluster = (btn * 2f) + gap;
            float valueW = w - (cluster * 2f) - 16f;

            var bigDown = new Rect(x, y, btn, 34f);
            var smallDown = new Rect(x + btn + gap, y, btn, 34f);
            var box = new Rect(x + cluster + 8f, y, valueW, 34f);
            var smallUp = new Rect(box.xMax + 8f, y, btn, 34f);
            var bigUp = new Rect(smallUp.xMax + gap, y, btn, 34f);

            const float eps = 0.0001f;
            bool canDown = value > min + eps;
            bool canUp = value < max - eps;

            if (StepButton(bigDown, BuilderSkin.DoubleLeft, canDown, idBase, 0.14f))
            {
                value = Nudge(value, -StepBig, min, max);
            }

            if (StepButton(smallDown, BuilderSkin.ChevronLeft, canDown, idBase + 1, 0.05f))
            {
                value = Nudge(value, -StepSmall, min, max);
            }

            if (StepButton(smallUp, BuilderSkin.ChevronRight, canUp, idBase + 2, 0.05f))
            {
                value = Nudge(value, StepSmall, min, max);
            }

            if (StepButton(bigUp, BuilderSkin.DoubleRight, canUp, idBase + 3, 0.14f))
            {
                value = Nudge(value, StepBig, min, max);
            }

            // Value + position bar within the range.
            GUI.Box(box, GUIContent.none, BuilderSkin.ValueBox);
            GUI.Label(new Rect(box.x, box.y - 2f, box.width, box.height - 4f),
                value.ToString(format) + " м", BuilderSkin.ValueText);
            float t = Mathf.InverseLerp(min, max, value);
            var track = new Rect(box.x + 10f, box.yMax - 7f, box.width - 20f, 3f);
            DrawSolid(track, new Color(1f, 1f, 1f, 0.08f));
            DrawSolid(new Rect(track.x, track.y, Mathf.Max(3f, track.width * t), track.height), BuilderSkin.Accent);

            // Step labels under the buttons.
            GUI.Label(new Rect(bigDown.x, y + 36f, btn, 14f), "1 м", BuilderSkin.TinyCenter);
            GUI.Label(new Rect(smallDown.x, y + 36f, btn, 14f), "0.1 м", BuilderSkin.TinyCenter);
            GUI.Label(new Rect(smallUp.x, y + 36f, btn, 14f), "0.1 м", BuilderSkin.TinyCenter);
            GUI.Label(new Rect(bigUp.x, y + 36f, btn, 14f), "1 м", BuilderSkin.TinyCenter);

            y += 62f;
        }

        /// <summary>
        /// Ring count editor (RCS): [&lt;] N шт. [&gt;]. Values cycle through the
        /// symmetry presets 1/2/3/4/6/8; X/Z do the same in the world.
        /// </summary>
        private void DrawCountEditor(float x, ref float y, float w, string label, ref int count, int idBase)
        {
            GUI.Label(new Rect(x, y, w * 0.6f, 18f), label, BuilderSkin.Body);
            GUI.Label(new Rect(x + (w * 0.4f), y, w * 0.6f, 18f), "X / Z", BuilderSkin.TinyRight);
            y += 22f;

            const float btn = 36f;
            float valueW = w - (btn * 2f) - 16f;
            var down = new Rect(x, y, btn, 34f);
            var box = new Rect(x + btn + 8f, y, valueW, 34f);
            var up = new Rect(box.xMax + 8f, y, btn, 34f);

            if (StepButton(down, BuilderSkin.ChevronLeft, true, idBase, 0.25f))
            {
                count = RcsBlockPlacement.Step(count, -1);
            }

            if (StepButton(up, BuilderSkin.ChevronRight, true, idBase + 1, 0.25f))
            {
                count = RcsBlockPlacement.Step(count, 1);
            }

            GUI.Box(box, GUIContent.none, BuilderSkin.ValueBox);
            GUI.Label(new Rect(box.x, box.y - 2f, box.width, box.height - 4f),
                count + " шт.", BuilderSkin.ValueText);
            GUI.Label(new Rect(x, y + 36f, w, 14f), "1 / 2 / 3 / 4 / 6 / 8 по окружности",
                BuilderSkin.TinyRight);

            y += 62f;
        }

        private static float Nudge(float value, float delta, float min, float max)
        {
            // 0.1 m grid step: the value is always "round" (0.5, not 0.65), so
            // the size can be matched to a neighbouring tank. Rounding also
            // removes accumulated float error (0.1 + 0.1 + ...).
            float stepped = Mathf.Round((value + delta) * 10f) / 10f;
            return Mathf.Clamp(stepped, min, max);
        }

        // Arrow button: fires on press, repeats while held.
        private bool StepButton(Rect rect, Texture2D icon, bool enabled, int id, float repeat)
        {
            Event e = Event.current;
            bool inside = rect.Contains(e.mousePosition);
            bool pressed = enabled && holdId == id;
            bool hover = enabled && controlsActive && inside && !Dragging;

            GUIStyle style = !enabled ? BuilderSkin.StepDisabled
                : pressed ? BuilderSkin.StepActive
                : hover ? BuilderSkin.StepHover : BuilderSkin.Step;
            GUI.Box(rect, GUIContent.none, style);

            Color tint = !enabled ? new Color(0.55f, 0.56f, 0.58f, 0.30f)
                : pressed ? new Color(1f, 1f, 1f, 1f)
                : new Color(0.82f, 0.84f, 0.86f, 0.95f);
            SetColor(tint);
            GUI.DrawTexture(new Rect(rect.center.x - 13f, rect.center.y - 13f, 26f, 26f), icon);
            SetColor(Color.white);

            if (!enabled || !controlsActive)
            {
                return false;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && inside && !Dragging)
            {
                holdId = id;
                holdNextTime = Time.unscaledTime + 0.45f;
                e.Use();
                return true;
            }

            if (holdId == id && e.type == EventType.Repaint && Time.unscaledTime >= holdNextTime)
            {
                holdNextTime = Time.unscaledTime + repeat;
                return true;
            }

            return false;
        }

        private bool DrawIconButton(Rect rect, Texture2D icon, float iconSize)
        {
            Event e = Event.current;
            bool inside = rect.Contains(e.mousePosition);
            GUI.Box(rect, GUIContent.none, (open && inside) ? BuilderSkin.StepHover : BuilderSkin.Step);
            SetColor(new Color(0.85f, 0.87f, 0.89f, 0.95f));
            GUI.DrawTexture(new Rect(rect.center.x - (iconSize * 0.5f), rect.center.y - (iconSize * 0.5f), iconSize, iconSize), icon);
            SetColor(Color.white);

            if (open && e.type == EventType.MouseDown && e.button == 0 && inside && !Dragging)
            {
                e.Use();
                return true;
            }

            return false;
        }

        // ─── Part grid ───────────────────────────────────────────────────

        /// <summary>How many parts of each catalog card are placed on the pad.</summary>
        private void RefreshPlacedCounts()
        {
            System.Array.Clear(placedCounts, 0, placedCounts.Length);
            IReadOnlyList<PlacedPart> parts = Builder.Parts;
            if (parts == null)
            {
                return;
            }

            for (int p = 0; p < parts.Count; p++)
            {
                PlacedPart part = parts[p];
                for (int c = 0; c < Catalog.Length; c++)
                {
                    if (part.Kind != Catalog[c].Kind)
                    {
                        continue;
                    }

                    if (part.Kind == PartKind.Engine
                        && (part.EngineSpec == null || part.EngineSpec.Id != Catalog[c].EngineId))
                    {
                        continue;
                    }

                    placedCounts[c]++;
                    break;
                }
            }
        }

        private void DrawGrid(Rect r)
        {
            GUI.Box(r, GUIContent.none, BuilderSkin.PanelInner);
            cellTooltip = null;
            RefreshPlacedCounts();

            // Cards of the current tab that match the search.
            visibleCards.Clear();
            for (int i = 0; i < Catalog.Length; i++)
            {
                if ((tabIndex == 0 || InTab(tabIndex, Catalog[i].Kind)) && MatchesSearch(Catalog[i]))
                {
                    visibleCards.Add(i);
                }
            }

            const float inset = 8f;
            const float gap = 4f;
            const int columns = GridColumns;
            var view = new Rect(r.x + inset, r.y + inset, r.width - (inset * 2f) - 8f, r.height - (inset * 2f));
            float cell = (view.width - (gap * (columns - 1))) / columns;

            // Empty cells fill the visible area, like the reference: the grid is never "short".
            int rowsFilled = Mathf.CeilToInt(visibleCards.Count / (float)columns);
            int rowsVisible = Mathf.CeilToInt((view.height + gap) / (cell + gap));
            int rows = Mathf.Max(1, Mathf.Max(rowsFilled, rowsVisible));
            float contentH = (rows * (cell + gap)) - gap;
            float maxScroll = Mathf.Max(0f, contentH - view.height);
            gridScroll = Mathf.Clamp(gridScroll, 0f, maxScroll);
            gridHovered = r.Contains(Event.current.mousePosition);
            HandleGridScroll(r, maxScroll);

            // The mouse is in global coordinates (before the group): cell hits
            // are tested in those, while drawing is local (the group translates).
            Vector2 mouseTop = Event.current.mousePosition;
            bool mouseInView = view.Contains(mouseTop);
            GUI.BeginGroup(view);
            Event e = Event.current;

            for (int n = 0; n < rows * columns; n++)
            {
                var card = new Rect(
                    (n % columns) * (cell + gap),
                    ((n / columns) * (cell + gap)) - gridScroll,
                    cell, cell);
                if (card.yMax < 0f || card.y > view.height)
                {
                    continue;                            // whole row off-screen
                }

                if (n >= visibleCards.Count)
                {
                    SetColor(new Color(1f, 1f, 1f, 0.55f));
                    GUI.Box(card, GUIContent.none, BuilderSkin.Cell);   // empty cell
                    SetColor(Color.white);
                    continue;
                }

                int i = visibleCards[n];
                var cardTop = new Rect(view.x + card.x, view.y + card.y, cell, cell);
                CatalogEntry entry = Catalog[i];
                PartKind kind = entry.Kind;
                EngineSpec spec = EngineSpecFor(entry);
                bool selected = statsIndex == i;
                bool hover = controlsActive && !Dragging && mouseInView && cardTop.Contains(mouseTop);

                GUI.Box(card, GUIContent.none, selected ? BuilderSkin.CellSelected : hover ? BuilderSkin.CellHover : BuilderSkin.Cell);

                if (hover)
                {
                    cellTooltip = PartCatalog.DisplayName(kind, spec);
                }

                // The cell shows the preset's picture: if this card is already in
                // the hotbar — the slot icon (matched to shape/sizes), otherwise
                // the default kind render (per-model for an engine).
                Texture2D picture = SlotIconFor(entry);
                if (picture == null)
                {
                    picture = PartIcons.Get(kind, entry.EngineId);
                }

                float iconSize = cell * 0.80f;
                GUI.DrawTexture(new Rect(card.center.x - (iconSize * 0.5f), card.center.y - (iconSize * 0.5f), iconSize, iconSize),
                    picture, ScaleMode.ScaleToFit);

                // "In hotbar" badge: slot number (top-left).
                int slot = FindInHotbar(entry);
                if (slot >= 0)
                {
                    var badge = new Rect(card.x + 8f, card.y + 8f, 24f, 22f);
                    GUI.Box(badge, GUIContent.none, BuilderSkin.Key);
                    GUI.Label(badge, (slot + 1).ToString(), BuilderSkin.KeyText);
                }

                // Bottom-right: how many of this part are placed (where the reference shows stock).
                if (placedCounts[i] > 0)
                {
                    DrawOutlinedLabel(new Rect(card.x, card.y, card.width - 8f, card.height - 4f),
                        placedCounts[i].ToString(), BuilderSkin.CountText);
                }

                if (controlsActive && e.type == EventType.MouseDown && e.button == 0 && !Dragging
                    && mouseInView && cardTop.Contains(mouseTop))
                {
                    statsIndex = i;

                    // The card is a "next placement" draft: further size edits do
                    // not touch slots the player did not select. The draft starts
                    // as the selected slot looks.
                    if (editSlot != null && editSlot.Occupied && MatchesEntry(entry, editSlot.Preset))
                    {
                        drafts[i] = editSlot.Preset;
                    }

                    editSlot = null;

                    if (e.clickCount >= 2)
                    {
                        AssignToHotbar(entry);       // double click — into a free slot
                    }
                    else
                    {
                        dragPreset = StatsPreset();  // what the window shows is dragged
                        dragIcon = picture;
                        dragFromHotbar = -1;
                        dragOrigin = mouseTop;
                        dragMoved = false;
                    }

                    e.Use();
                }
            }

            GUI.EndGroup();

            // Thin scrollbar at the right edge (wheel scrolls; this one only shows the position).
            if (maxScroll > 0f)
            {
                var track = new Rect(r.xMax - 12f, r.y + inset, 4f, r.height - (inset * 2f));
                DrawSolid(track, new Color(1f, 1f, 1f, 0.06f));
                float thumbH = Mathf.Max(36f, track.height * (view.height / contentH));
                float t = gridScroll / maxScroll;
                DrawSolid(new Rect(track.x, Mathf.Lerp(track.y, track.yMax - thumbH, t), track.width, thumbH),
                    new Color(1f, 1f, 1f, 0.30f));
            }
        }

        private bool DrawTextButton(Rect rect, string text, GUIStyle normal, GUIStyle hover, GUIStyle textStyle)
        {
            Event e = Event.current;
            bool inside = rect.Contains(e.mousePosition);
            GUI.Box(rect, GUIContent.none, (controlsActive && inside) ? hover : normal);
            GUI.Label(rect, text, textStyle);

            if (controlsActive && e.type == EventType.MouseDown && e.button == 0 && inside && !Dragging)
            {
                e.Use();
                return true;
            }

            return false;
        }

        private int FindInHotbar(CatalogEntry entry)
        {
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (hotbar[i].Occupied && MatchesEntry(entry, hotbar[i].Preset))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Icon of this card's first slot (otherwise null — use the kind default).</summary>
        private Texture2D SlotIconFor(CatalogEntry entry)
        {
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (hotbar[i].Occupied && MatchesEntry(entry, hotbar[i].Preset) && hotbar[i].Icon != null)
                {
                    return hotbar[i].Icon;
                }
            }

            return null;
        }

        /// <summary>
        /// Double click on a card: a copy of the shown preset into a free slot.
        /// A copy, so one card can easily produce slots with different sizes (an
        /// engine has no sizes — a copy of the same model).
        /// </summary>
        private void AssignToHotbar(CatalogEntry entry)
        {
            int target = -1;
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (!hotbar[i].Occupied)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                int existing = FindInHotbar(entry);
                SetHotbarIndex(existing >= 0 ? existing : (hotbarIndex >= 0 ? hotbarIndex : 0));
                return;
            }

            AssignPreset(target, StatsPreset());
            SetHotbarIndex(target);
        }

        // ─── Drag and drop ──────────────────────────────────────────────────

        private void HandleDragRelease()
        {
            Event current = Event.current;
            if (Dragging && !dragMoved && (current.mousePosition - dragOrigin).sqrMagnitude > DragThreshold * DragThreshold)
            {
                dragMoved = true;                    // moved — this is already a drag, not a click
            }

            if (current.type != EventType.MouseUp || !Dragging)
            {
                return;
            }

            if (!open)
            {
                ResetDrag();
                return;
            }

            if (dragMoved)
            {
                PartPreset dragged = dragPreset.Value;
                bool dropped = false;
                for (int i = 0; i < hotbar.Length; i++)
                {
                    if (!HotbarSlotRect(i).Contains(current.mousePosition))
                    {
                        continue;
                    }

                    if (dragFromHotbar >= 0 && dragFromHotbar != i)
                    {
                        SwapSlots(dragFromHotbar, i);    // moving = swapping
                    }
                    else
                    {
                        AssignPreset(i, dragged);        // from the inventory — a copy of the preset
                    }

                    SetHotbarIndex(i);
                    dropped = true;
                    break;
                }

                if (!dropped && dragFromHotbar >= 0)
                {
                    ClearSlot(dragFromHotbar);           // dropped outside — remove from the panel
                    ApplySelection();
                }
            }

            ResetDrag();
            current.Use();
        }

        private void ResetDrag()
        {
            dragPreset = null;
            dragIcon = null;
            dragFromHotbar = -1;
            dragMoved = false;
        }

        private void DrawDragIcon()
        {
            if (!Dragging || !open || !dragMoved)
            {
                return;
            }

            Vector2 mouse = Event.current.mousePosition;
            var rect = new Rect(mouse.x - 30f, mouse.y - 30f, 60f, 60f);
            SetColor(new Color(0.30f, 0.55f, 0.85f, 0.50f));
            GUI.Box(Expand(rect, 20f), GUIContent.none, BuilderSkin.Glow);
            SetColor(new Color(1f, 1f, 1f, 0.95f));
            Texture2D picture = dragIcon != null ? dragIcon : PartIcons.Get(dragPreset.Value.Kind, dragPreset.Value.EngineId);
            GUI.DrawTexture(rect, picture, ScaleMode.ScaleToFit);
            SetColor(Color.white);
        }

        // ─── Misc ──────────────────────────────────────────────────────────

        /// <summary>
        /// Engine spec of the preset (by EngineId from the EngineSpecs registry;
        /// an empty Id is the default engine). The stats do not depend on whether
        /// the model is loaded.
        /// </summary>
        private static EngineSpec EngineSpecFor(PartPreset preset)
        {
            return EngineSpecs.Get(preset.EngineId);
        }

        /// <summary>Engine spec of the card (null for non-engines).</summary>
        private static EngineSpec EngineSpecFor(CatalogEntry entry)
        {
            return entry.Kind == PartKind.Engine ? EngineSpecs.Get(entry.EngineId) : null;
        }

        private static string FormatThrust(EngineSpec spec)
        {
            return spec == null ? "—" : (spec.ThrustVacuumNewtons / 1000d).ToString("0.#") + " кН";
        }

        private static string FormatIsp(EngineSpec spec)
        {
            return spec == null ? "—" : spec.IspVacuumSeconds.ToString("0.#") + " с";
        }

        private static string KindLabel(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.Engine: return "Двигатель";
                case PartKind.FuelTank: return "Ёмкость · настраиваемая";
                case PartKind.Capsule: return "Модуль экипажа · фиксированный";
                case PartKind.RcsBlock: return "Блок РСУ · радиальный";
                default: return "Разделение ступеней";
            }
        }

        /// <summary>
        /// Kind label with the engine spec: a vacuum-only upper stage engine gets
        /// an extra tag.
        /// </summary>
        private static string KindLabel(PartKind kind, EngineSpec engine)
        {
            string label = KindLabel(kind);
            return engine != null && engine.VacuumOnly ? label + " · вакуумный" : label;
        }

        /// <summary>Whether the tank is conical: top radius set and different from the bottom.</summary>
        private static bool IsCone(PartMetrics metrics)
        {
            return metrics.TopRadius > 0.01f
                && Mathf.Abs(metrics.TopRadius - metrics.Radius) > 1e-3f;
        }

        private static string FormatMass(double kg)
        {
            return kg >= 1000d ? (kg / 1000d).ToString("F1") + " т" : kg.ToString("F0") + " кг";
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + (amount * 2f), rect.height + (amount * 2f));
        }

        // Single point for setting colour: accounts for the window animation transparency.
        private void SetColor(Color color)
        {
            GUI.color = new Color(color.r, color.g, color.b, color.a * uiAlpha);
        }

        private void DrawSolid(Rect rect, Color color)
        {
            SetColor(color);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            SetColor(Color.white);
        }

        /// <summary>Label with a dark outline: readable over the 3D scene without a plate.</summary>
        private void DrawOutlinedLabel(Rect rect, string text, GUIStyle style)
        {
            SetColor(new Color(0f, 0f, 0f, 0.85f));
            GUI.Label(new Rect(rect.x - 1.5f, rect.y, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x, rect.y - 1.5f, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x, rect.y + 1.5f, rect.width, rect.height), text, style);
            SetColor(Color.white);
            GUI.Label(rect, text, style);
        }

        private void DrawOutline(Rect rect, float thickness, Color color)
        {
            DrawSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}

