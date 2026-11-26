using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Galilego.Planning;

namespace Galilego.Tablet
{
    /// <summary>
    /// Maneuver planner as a slide-in drawer on the map's right edge, using the
    /// same menu language as the reference-frame panel. The drawer keeps the plan
    /// column visible, so committed maneuvers can be inspected while planning.
    /// The view only mirrors <see cref="PlannerController"/> state and sends it
    /// commands; all physics runs in the controller.
    /// </summary>
    public sealed class PlannerView : VisualElement
    {
        private readonly PlannerController ctl;
        private readonly Func<ManeuverPlan> plan;
        private readonly Func<double> shipDvAvailable;
        private readonly Action<int, int> setViewFrame;
        private readonly Func<int[]> getViewFrame;
        private readonly Action planChanged;

        private Button bTransfer, bFlyby, bOrbitCur, bOrbitSet, bCapC, bCapE, bCapF, bSearch, bDraft, bCommit, bRemove;
        private Label lFrom, lStatus, lWindowsTitle, lSelected, lFlags, lPlanState, lPlanNote, lHeatEmpty, lOrbitHint;
        private Label lDvTotal, lDvPatched, lDvMiss;
        private TextField fPark, fArr, fEcc, fSpan, fFlight;
        private DropdownField ddTo, ddVia;
        private VisualElement rowVia, rowPark, rowOrbit, rowArr, rowEcc, heatBox, impulseList;
        private Image heat;
        private Texture2D tex;
        private ScrollView content, windowList;
        private int version = -1, drawnWindows = -1, drawnSelected = -2, drawnHeatSelected = -2, drawnWindowsVersion = -1;
        private PorkchopGrid drawnGrid;
        private Scroller styledContentScroller, styledWindowScroller;
        private int[] savedFrame, appliedFrame;
        private bool open;
        private int choiceFrom = -2, choiceCount = -1, choiceTo = -1, choiceVia = -1;
        private bool choicesDirty = true;
        private readonly List<int> toBodies = new List<int>();
        private readonly List<int> viaBodies = new List<int>();
        private readonly List<string> toNames = new List<string>();
        private readonly List<string> viaNames = new List<string>();

        /// <summary>The drawer was opened (NavTabletView closes the frames menu).</summary>
        public event Action Opened;

        /// <summary>The drawer was closed (frame restored, draft calculation stopped).</summary>
        public event Action Closed;

        public PlannerView(PlannerController controller, Func<ManeuverPlan> planSource, Func<double> shipDv,
            Action<int, int> setFrame, Func<int[]> getFrame, Action planChanged = null)
        {
            ctl = controller;
            plan = planSource;
            shipDvAvailable = shipDv;
            setViewFrame = setFrame;
            getViewFrame = getFrame;
            this.planChanged = planChanged;
            name = "planner";
            AddToClassList("planner-drawer");
            AddToClassList("collapsed");
            Build();
        }

        public bool IsOpen { get { return open; } }

        public void Open()
        {
            if (open) { return; }
            savedFrame = getViewFrame != null ? getViewFrame() : null;
            appliedFrame = null;
            open = true;
            RemoveFromClassList("collapsed");
            choicesDirty = true;
            SyncFields();
            if (ctl.World != null && ctl.Settings.To < 0) { ctl.Settings.To = FirstTarget(); }
            version = -1;
            Refresh();
            if (Opened != null) { Opened(); }
        }

        public void Close()
        {
            if (!open) { return; }
            ctl.Cancel();
            open = false;
            AddToClassList("collapsed");
            // Restore the map frame only if the player did not change it while
            // the planner was open.
            if (savedFrame != null && savedFrame.Length >= 2 && appliedFrame != null && appliedFrame.Length >= 2 && getViewFrame != null)
            {
                int[] now = getViewFrame();
                if (now != null && now.Length >= 2 && now[0] == appliedFrame[0] && now[1] == appliedFrame[1])
                {
                    setViewFrame(savedFrame[0], savedFrame[1]);
                }
            }

            if (Closed != null) { Closed(); }
        }

        /// <summary>Runs a slice of the n-body correction and repaints the drawer when it changed.</summary>
        public void Tick()
        {
            if (!open) { return; }
            ctl.Tick(6d);
            // The ScrollView builds its scroller after the first layout; restyle it
            // whenever it exists so the theme's white thumb never appears.
            StyleScrollerOnce(content, ref styledContentScroller);
            StyleScrollerOnce(windowList, ref styledWindowScroller);
            if (ctl.Version != version || ctl.Busy) { Refresh(); }
        }

        // ------------------------------------------------------------------------------------------------ layout

        private void Build()
        {
            var body = new VisualElement();
            body.AddToClassList("planner-body");
            Add(body);

            content = new ScrollView(ScrollViewMode.Vertical);
            content.AddToClassList("planner-scroll");
            content.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(content);
            BuildFooter(body);

            var head = new VisualElement();
            head.AddToClassList("planner-head");
            bTransfer = Segmented("ПЕРЕЛЁТ", () => SetMode(PlannerMode.Transfer));
            bTransfer.tooltip = "Прямой перелёт к телу того же родителя";
            bFlyby = Segmented("ГРАВ. МАНЁВР", () => SetMode(PlannerMode.Flyby));
            bFlyby.tooltip = "Перелёт с гравитационным манёвром у промежуточного тела";
            head.Add(bTransfer);
            head.Add(bFlyby);
            var title = new Label("ПЛАНИРОВЩИК МАНЁВРОВ");
            title.AddToClassList("planner-title");
            head.Add(title);
            Button close = Btn("✕", Close);
            close.AddToClassList("planner-close");
            head.Add(close);
            content.Add(head);

            lFrom = new Label("");
            lFrom.AddToClassList("planner-from");
            content.Add(lFrom);

            lStatus = new Label("");
            lStatus.AddToClassList("planner-status");
            content.Add(lStatus);

            var columns = new VisualElement();
            columns.AddToClassList("planner-columns");
            var left = new VisualElement();
            left.AddToClassList("planner-col-left");
            var right = new VisualElement();
            right.AddToClassList("planner-col-right");
            columns.Add(left);
            columns.Add(right);
            content.Add(columns);

            BuildRouteCard(left);
            BuildOrbitCard(left);
            BuildArrivalCard(left);
            BuildSearchCard(left);
            BuildWindowsCard(right);
            BuildPlanCard(right);
        }

        private void BuildRouteCard(VisualElement host)
        {
            VisualElement card = Card("МАРШРУТ");
            var toRow = Row();
            toRow.Add(FieldLabel("КУДА"));
            ddTo = Dropdown();
            ddTo.tooltip = "Целевое тело (в v1 — тот же родитель, что у тела старта)";
            ddTo.RegisterValueChangedCallback(e =>
            {
                int i = toNames.IndexOf(e.newValue);
                if (i < 0) { return; }
                int body = toBodies[i];
                if (body == ctl.Settings.To) { return; }
                ctl.Settings.To = body;
                if (ctl.Settings.Via == body) { ctl.Settings.Via = -1; }
                ctl.Clear("Цель изменена: найдите окна заново.");
                Touch();
            });
            toRow.Add(ddTo);
            card.Add(toRow);

            rowVia = Row();
            rowVia.Add(FieldLabel("ЧЕРЕЗ"));
            ddVia = Dropdown();
            ddVia.tooltip = "Тело, у которого выполняется гравитационный манёвр";
            ddVia.RegisterValueChangedCallback(e =>
            {
                int i = viaNames.IndexOf(e.newValue);
                if (i < 0) { return; }
                int body = viaBodies[i];
                if (body == ctl.Settings.Via) { return; }
                ctl.Settings.Via = body;
                ctl.Clear("Тело гравитационного манёвра изменено: найдите окна заново.");
                Touch();
            });
            rowVia.Add(ddVia);
            card.Add(rowVia);
            host.Add(card);
        }

        private void BuildOrbitCard(VisualElement host)
        {
            VisualElement card = Card("ОРБИТА ОТЛЁТА");
            rowOrbit = Row();
            bOrbitCur = Segmented("ТЕКУЩАЯ", () => SetOrbit(true));
            bOrbitCur.tooltip = "Отлёт с текущей орбиты аппарата";
            bOrbitSet = Segmented("ЗАДАТЬ", () => SetOrbit(false));
            bOrbitSet.tooltip = "Сначала перевести аппарат на круговую орбиту заданной высоты";
            rowOrbit.Add(bOrbitCur);
            rowOrbit.Add(bOrbitSet);
            card.Add(rowOrbit);

            lOrbitHint = new Label("ГРАВ. МАНЁВР V1: СТАРТ ТОЛЬКО С ТЕКУЩЕЙ ОРБИТЫ");
            lOrbitHint.AddToClassList("planner-caption");
            lOrbitHint.style.display = DisplayStyle.None;
            card.Add(lOrbitHint);

            rowPark = Row();
            rowPark.Add(FieldLabel("ВЫСОТА, КМ"));
            fPark = Field(ctl.Settings.ParkAltitudeKm, v => ctl.Settings.ParkAltitudeKm = v);
            rowPark.Add(fPark);
            card.Add(rowPark);
            host.Add(card);
        }

        private void BuildArrivalCard(VisualElement host)
        {
            VisualElement card = Card("ПРИБЫТИЕ");
            var row = Row();
            bCapC = Segmented("КРУГ", () => SetCapture(CaptureKind.Circular));
            bCapC.tooltip = "Выход на круговую орбиту у цели";
            bCapE = Segmented("ЭЛЛИПС", () => SetCapture(CaptureKind.Elliptic));
            bCapE.tooltip = "Выход на эллиптическую орбиту у цели";
            bCapF = Segmented("ПРОЛЁТ", () => SetCapture(CaptureKind.None));
            bCapF.tooltip = "Без захвата: пролёт мимо цели";
            row.Add(bCapC);
            row.Add(bCapE);
            row.Add(bCapF);
            card.Add(row);

            rowArr = Row();
            rowArr.Add(FieldLabel("ВЫСОТА, КМ"));
            fArr = Field(ctl.Settings.ArrivalAltitudeKm, v => ctl.Settings.ArrivalAltitudeKm = v);
            rowArr.Add(fArr);
            card.Add(rowArr);

            rowEcc = Row();
            rowEcc.Add(FieldLabel("ЭКСЦЕНТР."));
            fEcc = Field(ctl.Settings.CaptureEccentricity, v => ctl.Settings.CaptureEccentricity = Math.Max(0d, Math.Min(0.99d, v)));
            rowEcc.Add(fEcc);
            card.Add(rowEcc);
            host.Add(card);
        }

        private void BuildSearchCard(VisualElement host)
        {
            VisualElement card = Card("ПОИСК");
            var spanRow = Row();
            spanRow.Add(FieldLabel("ИСКАТЬ, СУТ"));
            fSpan = Field(0d, v => ctl.Settings.SearchSpanDays = Math.Max(0d, v));
            spanRow.Add(fSpan);
            card.Add(spanRow);

            var flightRow = Row();
            flightRow.Add(FieldLabel("ПОЛЁТ ≤, СУТ"));
            fFlight = Field(0d, v => ctl.Settings.MaxFlightDays = Math.Max(0d, v));
            flightRow.Add(fFlight);
            card.Add(flightRow);

            var hint = new Label("0 = ПОДБОР АВТОМАТИЧЕСКИ");
            hint.AddToClassList("planner-caption");
            card.Add(hint);

            bSearch = Btn("НАЙТИ ОКНА", SearchNow);
            bSearch.tooltip = "Перебрать даты отлёта и времена полёта (около секунды)";
            bSearch.AddToClassList("planner-primary");
            bSearch.AddToClassList("planner-search");
            card.Add(bSearch);
            host.Add(card);
        }

        private void BuildWindowsCard(VisualElement host)
        {
            VisualElement card = Card("ОКНА");
            lWindowsTitle = new Label("");
            lWindowsTitle.AddToClassList("planner-caption");
            card.Add(lWindowsTitle);

            heatBox = new VisualElement();
            heatBox.AddToClassList("planner-heat-box");
            heat = new Image { scaleMode = ScaleMode.StretchToFill };
            heat.AddToClassList("planner-heat");
            heat.tooltip = "Клик — выбрать ближайшее окно";
            heat.RegisterCallback<PointerDownEvent>(OnHeatClick);
            heatBox.Add(heat);
            lHeatEmpty = new Label("НАЖМИТЕ «НАЙТИ ОКНА»");
            lHeatEmpty.AddToClassList("planner-heat-empty");
            lHeatEmpty.pickingMode = PickingMode.Ignore;
            heatBox.Add(lHeatEmpty);
            card.Add(heatBox);

            var axes = new VisualElement();
            axes.AddToClassList("planner-heat-axes");
            var axisT = new Label("T ОТЛЁТА →");
            axisT.AddToClassList("planner-axis");
            var axisF = new Label("↑ T ПОЛЁТА");
            axisF.AddToClassList("planner-axis");
            axes.Add(axisT);
            axes.Add(axisF);
            card.Add(axes);

            windowList = new ScrollView(ScrollViewMode.Vertical);
            windowList.AddToClassList("planner-windows");
            windowList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            card.Add(windowList);

            lSelected = new Label("");
            lSelected.AddToClassList("planner-summary");
            card.Add(lSelected);

            lFlags = new Label("");
            lFlags.AddToClassList("planner-flags");
            card.Add(lFlags);
            host.Add(card);
        }

        private void BuildPlanCard(VisualElement host)
        {
            VisualElement card = Card("ПЛАН МАНЁВРОВ");
            var tiles = new VisualElement();
            tiles.AddToClassList("planner-tiles");
            lDvTotal = Tile(tiles, "ИТОГО ΔV");
            lDvPatched = Tile(tiles, "ПАТЧ-КОНИКИ");
            lDvMiss = Tile(tiles, "ПРОМАХ");
            lPlanState = Tile(tiles, "СОСТОЯНИЕ");
            card.Add(tiles);

            var cap = new Label("ИМПУЛЬСЫ");
            cap.AddToClassList("planner-caption");
            card.Add(cap);
            impulseList = new VisualElement();
            impulseList.AddToClassList("planner-impulses");
            card.Add(impulseList);

            lPlanNote = new Label("");
            lPlanNote.AddToClassList("planner-note");
            card.Add(lPlanNote);
            host.Add(card);
        }

        private void BuildFooter(VisualElement body)
        {
            var footer = new VisualElement();
            footer.AddToClassList("planner-footer");
            bDraft = Btn("ПОСТРОИТЬ ПЛАН", DraftAction);
            bDraft.AddToClassList("planner-primary");
            bDraft.tooltip = "Уточнить выбранное окно в n-телесной физике";
            bCommit = Btn("В ПЛАН", CommitAction);
            bCommit.AddToClassList("planner-primary");
            bCommit.tooltip = "Добавить импульсы планировщика в план манёвров";
            bRemove = Btn("УБРАТЬ", RemoveAction);
            bRemove.tooltip = "Убрать манёвры планировщика из плана";
            footer.Add(bDraft);
            footer.Add(bCommit);
            footer.Add(bRemove);
            body.Add(footer);
        }

        // ------------------------------------------------------------------------------------------------ commands

        private void Touch() { version = -1; Refresh(); }

        /// <summary>Restores the text fields from settings when the drawer opens.</summary>
        private void SyncFields()
        {
            fPark.SetValueWithoutNotify(ctl.Settings.ParkAltitudeKm.ToString("0.###", CultureInfo.InvariantCulture));
            fArr.SetValueWithoutNotify(ctl.Settings.ArrivalAltitudeKm.ToString("0.###", CultureInfo.InvariantCulture));
            fEcc.SetValueWithoutNotify(ctl.Settings.CaptureEccentricity.ToString("0.###", CultureInfo.InvariantCulture));
            fSpan.SetValueWithoutNotify(ctl.Settings.SearchSpanDays.ToString("0.###", CultureInfo.InvariantCulture));
            fFlight.SetValueWithoutNotify(ctl.Settings.MaxFlightDays.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private void SetMode(PlannerMode mode)
        {
            if (ctl.Settings.Mode == mode) { return; }
            ctl.Settings.Mode = mode;
            // The flyby mode always captures at the final target (the "ПРОЛЁТ" button is hidden there).
            // A stale None from transfer mode must not silently turn the destination into a flyby.
            if (mode == PlannerMode.Flyby && ctl.Settings.Capture == CaptureKind.None)
            {
                ctl.Settings.Capture = CaptureKind.Circular;
            }

            ctl.Clear("Режим изменён: найдите окна заново.");
            choicesDirty = true;
            drawnWindows = -1;
            drawnSelected = -2;
            drawnHeatSelected = -2;
            drawnGrid = null;
            Touch();
        }

        private void SetOrbit(bool current)
        {
            if (ctl.Settings.Mode == PlannerMode.Flyby) { return; }
            if (ctl.Settings.UseCurrentOrbit == current) { return; }
            ctl.Settings.UseCurrentOrbit = current;
            ctl.Clear(current
                ? "Отлёт с текущей орбиты: найдите окна заново."
                : "Орбита отлёта изменена: найдите окна заново.");
            Touch();
        }

        private void SetCapture(CaptureKind capture)
        {
            ctl.Settings.Capture = capture;
            Touch();
        }

        private void SearchNow()
        {
            if (ctl.Busy) { return; }
            bSearch.SetEnabled(false);
            lStatus.text = "ПОИСК ОКОН…";
            lStatus.style.display = DisplayStyle.Flex;
            // One frame of repaint before the synchronous Lambert grid, so the
            // button visibly changes state instead of the tablet freezing silently.
            schedule.Execute(() =>
            {
                if (!open) { return; }
                try
                {
                    ctl.Search();
                }
                catch (Exception e)
                {
                    ctl.Status = "Ошибка поиска: " + e.Message;
                }

                bSearch.SetEnabled(true);
                Touch();
            }).ExecuteLater(30);
        }

        private void DraftAction()
        {
            if (ctl.Busy) { ctl.Cancel(); Touch(); return; }
            ctl.BeginDraft();
            Touch();
        }

        private void CommitAction()
        {
            if (plan == null) { return; }
            ManeuverPlan p = plan();
            if (p == null) { return; }
            ctl.Commit(p);
            Touch();
            RebuildCards();
        }

        private void RemoveAction()
        {
            if (plan == null) { return; }
            ManeuverPlan p = plan();
            if (p == null) { return; }
            ctl.Remove(p);
            Touch();
            RebuildCards();
        }

        private void RebuildCards()
        {
            if (planChanged != null) { planChanged(); }
        }

        private int FirstTarget()
        {
            if (ctl.World == null) { return -1; }
            int from = ctl.World.ShipBody();
            if (from < 0) { return -1; }
            int parent = ctl.World.ParentOf(from);
            for (int i = 0; i < ctl.World.BodyCount; i++)
            {
                if (i != from && ctl.World.ParentOf(i) == parent) { return i; }
            }

            return -1;
        }

        // ------------------------------------------------------------------------------------------------ display

        private void Refresh()
        {
            version = ctl.Version;
            PlannerSettings st = ctl.Settings;
            bool flyby = st.Mode == PlannerMode.Flyby;
            if (flyby) { st.UseCurrentOrbit = true; }
            On(bTransfer, !flyby);
            On(bFlyby, flyby);
            On(bOrbitCur, st.UseCurrentOrbit);
            On(bOrbitSet, !st.UseCurrentOrbit);
            On(bCapC, st.Capture == CaptureKind.Circular);
            On(bCapE, st.Capture == CaptureKind.Elliptic);
            On(bCapF, st.Capture == CaptureKind.None);
            bCapF.style.display = flyby ? DisplayStyle.None : DisplayStyle.Flex;
            rowVia.style.display = flyby ? DisplayStyle.Flex : DisplayStyle.None;
            rowOrbit.style.display = flyby ? DisplayStyle.None : DisplayStyle.Flex;
            lOrbitHint.style.display = flyby ? DisplayStyle.Flex : DisplayStyle.None;
            rowPark.style.display = st.UseCurrentOrbit ? DisplayStyle.None : DisplayStyle.Flex;
            rowEcc.style.display = st.Capture == CaptureKind.Elliptic ? DisplayStyle.Flex : DisplayStyle.None;
            heatBox.style.display = flyby ? DisplayStyle.None : DisplayStyle.Flex;

            EnsureChoices();
            int from = ctl.World != null ? ctl.World.ShipBody() : -1;
            lFrom.text = "ОТКУДА: " + (from >= 0 && ctl.World != null ? ctl.World.BodyName(from) : "—")
                + (st.UseCurrentOrbit
                    ? "  ·  ТЕКУЩАЯ ОРБИТА"
                    : "  ·  ОРБИТА " + st.ParkAltitudeKm.ToString("F0", CultureInfo.InvariantCulture) + " КМ");
            lStatus.text = ctl.Status + (ctl.Busy ? "  ·  " + ctl.Shots.ToString(CultureInfo.InvariantCulture) : "");
            lStatus.style.display = string.IsNullOrEmpty(lStatus.text) ? DisplayStyle.None : DisplayStyle.Flex;
            lWindowsTitle.text = flyby
                ? "МАРШРУТЫ С ГРАВ. МАНЁВРОМ"
                : "ЦВЕТ — ΔV  ·  ◆ — НАЙДЕННЫЕ ОКНА";

            bSearch.SetEnabled(!ctl.Busy);
            bDraft.text = ctl.Busy ? "ОТМЕНИТЬ РАСЧЁТ" : "ПОСТРОИТЬ ПЛАН";
            bDraft.SetEnabled(ctl.Busy || ctl.Selected >= 0);
            bDraft.EnableInClassList("planner-danger", ctl.Busy);
            bCommit.SetEnabled(ctl.CanCommit);
            bRemove.SetEnabled(HasPlannerManeuvers());

            StyleScrollerOnce(content, ref styledContentScroller);
            StyleScrollerOnce(windowList, ref styledWindowScroller);
            RebuildWindows();
            DrawHeat();
            ShowSelected();
        }

        private void EnsureChoices()
        {
            int from = ctl.World != null ? ctl.World.ShipBody() : -1;
            int count = ctl.World != null ? ctl.World.BodyCount : 0;
            if (!choicesDirty && from == choiceFrom && count == choiceCount && ctl.Settings.To == choiceTo && ctl.Settings.Via == choiceVia)
            {
                return;
            }

            choicesDirty = false;
            choiceFrom = from;
            choiceCount = count;
            toBodies.Clear();
            toNames.Clear();
            viaBodies.Clear();
            viaNames.Clear();

            int parent = from >= 0 && ctl.World != null ? ctl.World.ParentOf(from) : -1;
            for (int i = 0; i < count; i++)
            {
                if (i == from) { continue; }
                if (parent >= 0 && ctl.World.ParentOf(i) != parent) { continue; }
                toBodies.Add(i);
                toNames.Add(ctl.World.BodyName(i));
            }

            if (!toBodies.Contains(ctl.Settings.To))
            {
                ctl.Settings.To = toBodies.Count > 0 ? toBodies[0] : -1;
            }

            for (int i = 0; i < toBodies.Count; i++)
            {
                if (toBodies[i] == ctl.Settings.To) { continue; }
                viaBodies.Add(toBodies[i]);
                viaNames.Add(toNames[i]);
            }

            if (!viaBodies.Contains(ctl.Settings.Via))
            {
                ctl.Settings.Via = viaBodies.Count > 0 ? viaBodies[0] : -1;
            }

            choiceTo = ctl.Settings.To;
            choiceVia = ctl.Settings.Via;
            ddTo.choices = new List<string>(toNames);
            ddTo.SetEnabled(toNames.Count > 0);
            ddTo.SetValueWithoutNotify(choiceTo >= 0 && ctl.World != null ? ctl.World.BodyName(choiceTo) : "—");
            ddVia.choices = new List<string>(viaNames);
            ddVia.SetEnabled(viaNames.Count > 0);
            ddVia.SetValueWithoutNotify(choiceVia >= 0 && ctl.World != null ? ctl.World.BodyName(choiceVia) : "—");
        }

        private void StyleScrollerOnce(ScrollView view, ref Scroller styled)
        {
            Scroller current = view != null ? view.verticalScroller : null;
            if (current == null || ReferenceEquals(current, styled)) { return; }
            styled = current;
            StyleScroller(view);
        }

        /// <summary>
        /// The runtime theme paints the scroll scroller as a wide white strip with
        /// white arrows. The tablet is a phosphor console, so the scroller is
        /// narrowed and recoloured once it exists (its track/thumb are theme
        /// internals, styled here rather than by a fragile USS selector).
        /// </summary>
        private static void StyleScroller(ScrollView view)
        {
            if (view == null) { return; }
            Scroller scroller = view.verticalScroller;
            if (scroller == null) { return; }
            scroller.style.width = 9f;
            scroller.style.minWidth = 9f;
            scroller.style.maxWidth = 9f;
            scroller.style.backgroundColor = (Color)new Color32(4, 17, 9, 255);
            scroller.style.backgroundImage = default(StyleBackground);
            scroller.style.borderTopWidth = 0f;
            scroller.style.borderBottomWidth = 0f;
            scroller.style.borderLeftWidth = 0f;
            scroller.style.borderRightWidth = 0f;
            Button low = scroller.Q<Button>(null, "unity-scroller__low-button");
            Button high = scroller.Q<Button>(null, "unity-scroller__high-button");
            if (low != null) { low.style.display = DisplayStyle.None; }
            if (high != null) { high.style.display = DisplayStyle.None; }
            Slider slider = scroller.Q<Slider>();
            if (slider == null) { return; }
            slider.style.backgroundColor = (Color)new Color32(4, 17, 9, 255);
            slider.style.backgroundImage = default(StyleBackground);
            slider.style.width = 9f;
            slider.style.minWidth = 9f;
            slider.style.maxWidth = 9f;
            slider.style.marginLeft = 0f;
            slider.style.marginRight = 0f;
            slider.style.borderTopWidth = 0f;
            slider.style.borderBottomWidth = 0f;
            slider.style.borderLeftWidth = 0f;
            slider.style.borderRightWidth = 0f;
            VisualElement track = slider.Q(null, "unity-base-slider__input");
            if (track != null)
            {
                track.style.backgroundColor = (Color)new Color32(8, 30, 18, 255);
                track.style.backgroundImage = default(StyleBackground);
                track.style.width = 9f;
                track.style.minWidth = 9f;
                track.style.maxWidth = 9f;
                track.style.marginLeft = 0f;
                track.style.marginRight = 0f;
                track.style.paddingLeft = 0f;
                track.style.paddingRight = 0f;
                track.style.borderTopWidth = 0f;
                track.style.borderBottomWidth = 0f;
                track.style.borderLeftWidth = 0f;
                track.style.borderRightWidth = 0f;
            }

            VisualElement thumb = slider.Q(null, "unity-base-slider__drag-container");
            if (thumb != null)
            {
                thumb.style.backgroundColor = (Color)new Color32(76, 224, 124, 255);
                thumb.style.backgroundImage = default(StyleBackground);
                thumb.style.width = 9f;
                thumb.style.borderTopLeftRadius = 4f;
                thumb.style.borderTopRightRadius = 4f;
                thumb.style.borderBottomLeftRadius = 4f;
                thumb.style.borderBottomRightRadius = 4f;
            }

            VisualElement tracker = slider.Q(null, "unity-base-slider__tracker");
            if (tracker != null)
            {
                tracker.style.backgroundColor = (Color)new Color32(8, 30, 18, 255);
                tracker.style.backgroundImage = default(StyleBackground);
                tracker.style.width = 9f;
                tracker.style.borderTopWidth = 0f;
                tracker.style.borderBottomWidth = 0f;
                tracker.style.borderLeftWidth = 0f;
                tracker.style.borderRightWidth = 0f;
            }

            VisualElement dragger = slider.Q(null, "unity-base-slider__dragger");
            if (dragger != null)
            {
                dragger.style.backgroundColor = (Color)new Color32(76, 224, 124, 255);
                dragger.style.backgroundImage = default(StyleBackground);
                dragger.style.width = 9f;
                dragger.style.minWidth = 9f;
                dragger.style.maxWidth = 9f;
                dragger.style.borderTopLeftRadius = 4f;
                dragger.style.borderTopRightRadius = 4f;
                dragger.style.borderBottomLeftRadius = 4f;
                dragger.style.borderBottomRightRadius = 4f;
            }
        }

        private void RebuildWindows()
        {
            int count = ctl.WindowCount;
            bool flyby = ctl.Settings.Mode == PlannerMode.Flyby;
            int token = ctl.WindowsVersion;
            if (drawnWindows == count && drawnSelected == ctl.Selected && drawnWindowsVersion == token) { return; }
            drawnWindows = count;
            drawnSelected = ctl.Selected;
            drawnWindowsVersion = token;
            windowList.Clear();
            if (count == 0)
            {
                var empty = new Label(flyby ? "МАРШРУТОВ НЕТ" : "ОКОН НЕТ");
                empty.AddToClassList("planner-empty");
                windowList.Add(empty);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int index = i;
                Button b = Btn(WindowText(i, flyby), () =>
                {
                    ctl.Select(index);
                    Touch();
                });
                b.AddToClassList("planner-window");
                b.EnableInClassList("btn--on", i == ctl.Selected);
                windowList.Add(b);
            }
        }

        private string WindowText(int i, bool flyby)
        {
            IPlannerWorld w = ctl.World;
            if (w == null) { return (i + 1).ToString(CultureInfo.InvariantCulture); }
            double now = w.UniverseNow;
            if (flyby)
            {
                FlybySolution s = ctl.FlybyWindows[i];
                return (i + 1).ToString(CultureInfo.InvariantCulture)
                    + "  ·  ОТЛЁТ " + Days(s.T0 - now)
                    + "  ·  ПРИЛЁТ " + Days(s.T2 - now)
                    + "\nΔV " + Dv(s.Total) + (s.Feasible ? "" : "  ·  НЕДОБОР ПОВОРОТА");
            }

            TransferSolution t = ctl.Windows[i];
            return (i + 1).ToString(CultureInfo.InvariantCulture)
                + "  ·  ЧЕРЕЗ " + Days(t.T0 - now)
                + "  ·  ПОЛЁТ " + Days(t.Tof)
                + "  ·  " + Dv(t.Total);
        }

        private void DrawHeat()
        {
            PorkchopGrid g = ctl.Grid;
            if (g == null || ctl.Settings.Mode != PlannerMode.Transfer)
            {
                heat.image = null;
                lHeatEmpty.style.display = DisplayStyle.Flex;
                lHeatEmpty.text = "НЕТ ДАННЫХ — НАЖМИТЕ «НАЙТИ ОКНА»";
                drawnGrid = null;
                return;
            }

            lHeatEmpty.style.display = DisplayStyle.None;
            if (ReferenceEquals(g, drawnGrid) && drawnHeatSelected == ctl.Selected) { return; }
            drawnGrid = g;
            drawnHeatSelected = ctl.Selected;

            if (tex == null || tex.width != g.NDep || tex.height != g.NTof)
            {
                if (tex != null)
                {
                    heat.image = null;
                    UnityEngine.Object.Destroy(tex);
                }

                tex = new Texture2D(g.NDep, g.NTof, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            float best = float.MaxValue;
            foreach (float v in g.Total)
            {
                if (!float.IsNaN(v) && v < best) { best = v; }
            }

            if (!float.IsFinite(best)) { best = 0f; }
            float cap = best * 1.8f + 1000f;
            double budget = shipDvAvailable != null ? shipDvAvailable() : double.NaN;
            var pixels = new Color32[g.NDep * g.NTof];
            for (int i = 0; i < g.NDep; i++)
            {
                for (int k = 0; k < g.NTof; k++)
                {
                    float v = g.Total[(i * g.NTof) + k];
                    Color32 c;
                    if (float.IsNaN(v))
                    {
                        c = TabletTheme.Bg;
                    }
                    else
                    {
                        float t = Mathf.Clamp01((v - best) / Mathf.Max(1f, cap - best));
                        c = Color32.Lerp(TabletTheme.Main, TabletTheme.Line, Mathf.Pow(t, 0.6f));
                        if (!double.IsNaN(budget) && v > budget && ((i + k) & 1) == 0) { c = TabletTheme.WarnDim; }
                    }

                    pixels[(k * g.NDep) + i] = c;
                }
            }

            for (int w = 0; w < ctl.Windows.Count; w++)
            {
                TransferSolution s = ctl.Windows[w];
                int i = (int)Math.Round((s.T0 - g.T0Start) / g.StepS);
                int k = (int)Math.Round((s.Tof / g.StepS) - g.KMin);
                Color32 mark = w == ctl.Selected ? TabletTheme.Bright : TabletTheme.Warn;
                for (int d = -2; d <= 2; d++)
                {
                    Put(pixels, g, i + d, k, mark);
                    Put(pixels, g, i, k + d, mark);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            heat.image = tex;
        }

        private static void Put(Color32[] pixels, PorkchopGrid g, int i, int k, Color32 c)
        {
            if (i >= 0 && i < g.NDep && k >= 0 && k < g.NTof) { pixels[(k * g.NDep) + i] = c; }
        }

        private void OnHeatClick(PointerDownEvent e)
        {
            PorkchopGrid g = ctl.Grid;
            if (g == null || ctl.Settings.Mode != PlannerMode.Transfer || ctl.Windows.Count == 0) { return; }
            Rect r = heat.contentRect;
            if (r.width < 1f || r.height < 1f) { return; }
            double gi = e.localPosition.x / r.width * g.NDep;
            double gk = (1d - (e.localPosition.y / r.height)) * g.NTof;
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < ctl.Windows.Count; i++)
            {
                TransferSolution w = ctl.Windows[i];
                double wi = (w.T0 - g.T0Start) / g.StepS;
                double wk = (w.Tof / g.StepS) - g.KMin;
                double d = ((wi - gi) * (wi - gi)) + ((wk - gk) * (wk - gk));
                if (d < bestD) { bestD = d; best = i; }
            }

            if (best >= 0)
            {
                ctl.Select(best);
                Touch();
            }
        }

        private void ShowSelected()
        {
            lSelected.text = "";
            lFlags.text = "";
            lFlags.style.display = DisplayStyle.None;
            lDvTotal.text = "—";
            lDvPatched.text = "—";
            lDvMiss.text = "—";
            lPlanState.text = "—";
            lPlanState.EnableInClassList("planner-warn", false);
            impulseList.Clear();
            lPlanNote.text = "";
            if (ctl.Selected < 0 || ctl.Selected >= ctl.WindowCount || ctl.World == null) { return; }

            var sb = new StringBuilder();
            double now = ctl.World.UniverseNow;
            var flags = new StringBuilder();
            bool flyby = ctl.Settings.Mode == PlannerMode.Flyby;
            if (flyby)
            {
                FlybySolution s = ctl.FlybyWindows[ctl.Selected];
                double alt = s.Flyby.PeriapsisRadius - ctl.World.Radius(ctl.Settings.Via);
                sb.AppendLine("ОТЛЁТ ЧЕРЕЗ " + Days(s.T0 - now) + "  ·  ПРОЛЁТ " + Days(s.T1 - now) + "  ·  ПРИЛЁТ " + Days(s.T2 - now));
                sb.AppendLine("ПРОЛЁТ: ВЫСОТА " + Dist(alt) + "  ·  ПОВОРОТ " + (s.Flyby.TurnNeeded * 180d / Math.PI).ToString("F1", CultureInfo.InvariantCulture)
                    + "° (МАКС. " + (s.Flyby.TurnMax * 180d / Math.PI).ToString("F1", CultureInfo.InvariantCulture) + "°)");
                sb.AppendLine(s.Trailing ? "ПРОХОД ПОЗАДИ ПЛАНЕТЫ: РАЗГОН" : "ПРОХОД ВПЕРЕДИ ПЛАНЕТЫ: ТОРМОЖЕНИЕ");
                sb.AppendLine("v∞ ВХОД/ВЫХОД " + (s.VInfIn.Magnitude / 1000d).ToString("F2", CultureInfo.InvariantCulture)
                    + " / " + (s.VInfOut.Magnitude / 1000d).ToString("F2", CultureInfo.InvariantCulture)
                    + " КМ/С  ·  " + (s.Flyby.PoweredDv > 1e-3d ? "Δv∞ (С ИМПУЛЬСОМ) " : "Δv∞ (ДАРОМ) ")
                    + Dv(s.FreeVelocityChange));
                sb.Append("ΔV: ОТЛЁТ " + Dv(s.DepDv) + "  ·  У ГИГАНТА " + Dv(s.Flyby.PoweredDv) + "  ·  ЗАХВАТ " + Dv(s.ArrDv) + "  ·  ИТОГО " + Dv(s.Total));
                if (!s.Feasible)
                {
                    flags.AppendLine("ПЛАНЕТА НЕ ПОВЕРНЁТ ТАК СИЛЬНО: НЕДОБОР " + Dv(s.Flyby.DeficitDv) + " — ВЫБЕРИТЕ ДРУГОЕ ОКНО.");
                }
            }
            else
            {
                TransferSolution s = ctl.Windows[ctl.Selected];
                sb.AppendLine("ОТЛЁТ ЧЕРЕЗ " + Days(s.T0 - now) + "  ·  ПОЛЁТ " + Days(s.Tof));
                sb.AppendLine("ПРИЛЁТ ЧЕРЕЗ " + Days(s.T1 - now));
                sb.AppendLine("v∞ " + (s.VInfDep.Magnitude / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " КМ/С");
                sb.Append("ΔV: ОТЛЁТ " + Dv(s.DepDv) + "  ·  ЗАХВАТ " + Dv(s.ArrDv) + "  ·  ИТОГО " + Dv(s.Total));
            }

            lSelected.text = sb.ToString();
            PlanDraft d = ctl.Draft;
            if (d == null)
            {
                lPlanNote.text = "ПЛАН ЕЩЁ НЕ ПОСТРОЕН.";
            }
            else
            {
                lDvTotal.text = Dv(d.TotalDv);
                lDvPatched.text = Dv(d.PatchedDv);
                lDvMiss.text = Dist(d.MissBeforeM) + " → " + Dist(d.MissAfterM);
                lPlanState.text = d.Refined ? (d.Preliminary ? "ОРИЕНТИРОВОЧНО" : "N-ТЕЛА") : "ПАТЧ-КОНИКИ";
                lPlanState.EnableInClassList("planner-warn", !d.Refined || d.Preliminary);
                if (d.PlanePenaltyDv > 5d)
                {
                    flags.AppendLine("ОРБИТА НЕ СОГЛАСОВАНА С ОКНОМ: +" + Dv(d.PlanePenaltyDv) + " К ИДЕАЛУ (ПЛОСКОСТЬ/ФАЗА).");
                }

                if (d.Preliminary)
                {
                    flags.AppendLine("ОРИЕНТИРОВОЧНО: ОКНО ДАЛЕКО — ПЕРЕСЧИТАЙТЕ ЗА 3 СУТ ДО ОТЛЁТА.");
                }

                double available = shipDvAvailable != null ? shipDvAvailable() : double.NaN;
                if (!double.IsNaN(available) && d.TotalDv > available)
                {
                    flags.AppendLine("НЕ ХВАТАЕТ ТОПЛИВА: НУЖНО " + Dv(d.TotalDv) + ", ЕСТЬ " + Dv(available) + ".");
                }

                lPlanNote.text = d.Note;
                foreach (PlannedImpulse impulse in d.Impulses)
                {
                    var row = new VisualElement();
                    row.AddToClassList("planner-impulse");
                    var role = new Label(impulse.Label);
                    role.AddToClassList("planner-impulse-role");
                    row.Add(role);
                    var meta = new Label("ЧЕРЕЗ " + Days(impulse.Time - now)
                        + "  ·  " + Dv(impulse.DvWorld.Magnitude)
                        + "  ·  СК " + (impulse.FrameBody >= 0 && impulse.FrameBody < ctl.World.BodyCount ? ctl.World.BodyName(impulse.FrameBody) : "—"));
                    meta.AddToClassList("planner-impulse-meta");
                    row.Add(meta);
                    impulseList.Add(row);
                }
            }

            lFlags.text = flags.ToString();
            lFlags.style.display = flags.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool HasPlannerManeuvers()
        {
            if (plan == null || ctl == null) { return false; }
            ManeuverPlan p = plan();
            if (p == null) { return false; }
            for (int i = 0; i < p.Count; i++)
            {
                if (p[i].PlannerId == ctl.PlannerId) { return true; }
            }

            return false;
        }

        // ------------------------------------------------------------------------------------------------ small helpers

        private static string Dv(double ms)
        {
            return double.IsFinite(ms) ? ms.ToString("F1", CultureInfo.InvariantCulture) + " М/С" : "—";
        }

        private static string Days(double s)
        {
            return double.IsFinite(s) ? (s / 86400d).ToString("F1", CultureInfo.InvariantCulture) + " СУТ" : "—";
        }

        private static string Dist(double m)
        {
            if (!double.IsFinite(m)) { return "—"; }
            double a = Math.Abs(m);
            if (a >= 1e9d) { return (m / 1e9d).ToString("F2", CultureInfo.InvariantCulture) + " МЛН КМ"; }
            if (a >= 1e4d) { return (m / 1e3d).ToString("F0", CultureInfo.InvariantCulture) + " КМ"; }
            return m.ToString("F0", CultureInfo.InvariantCulture) + " М";
        }

        private static void On(Button b, bool on)
        {
            b.EnableInClassList("btn--on", on);
        }

        private static VisualElement Card(string title)
        {
            var card = new VisualElement();
            card.AddToClassList("planner-card");
            var label = new Label(title);
            label.AddToClassList("planner-card-title");
            card.Add(label);
            return card;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("planner-row");
            return row;
        }

        private static Label FieldLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("planner-label");
            return label;
        }

        private static Label Tile(VisualElement host, string caption)
        {
            var tile = new VisualElement();
            tile.AddToClassList("planner-tile");
            var cap = new Label(caption);
            cap.AddToClassList("planner-tile-cap");
            tile.Add(cap);
            var value = new Label("—");
            value.AddToClassList("planner-tile-val");
            tile.Add(value);
            host.Add(tile);
            return value;
        }

        private static Button Btn(string text, Action action)
        {
            var b = new Button(action) { text = text };
            b.AddToClassList("btn");
            return b;
        }

        private static Button Segmented(string text, Action action)
        {
            Button b = Btn(text, action);
            b.AddToClassList("planner-seg");
            return b;
        }

        private static DropdownField Dropdown()
        {
            var dd = new DropdownField();
            dd.AddToClassList("dd");
            dd.AddToClassList("planner-dd");
            return dd;
        }

        private TextField Field(double initial, Action<double> set)
        {
            var field = new TextField { value = initial.ToString("0.###", CultureInfo.InvariantCulture) };
            field.AddToClassList("tf");
            field.AddToClassList("planner-field");
            field.RegisterValueChangedCallback(e =>
            {
                if (double.TryParse(e.newValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                    && double.IsFinite(v))
                {
                    set(v);
                }
            });
            return field;
        }
    }
}
