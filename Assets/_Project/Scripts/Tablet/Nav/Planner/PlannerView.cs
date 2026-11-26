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
    /// Окно «ПЛАНИРОВЩИК» на планшете навигации: плавающая панель поверх карты, строится кодом (в UXML планшета
    /// отдельной панели нет). Две вкладки — ПЕРЕЛЁТ и ГРАВ. МАНЁВР. Окно ничего не считает само: оно показывает состояние
    /// <see cref="PlannerController"/> и отдаёт ему команды. Карта под окном остаётся живой.
    /// </summary>
    public sealed class PlannerView : VisualElement
    {
        private readonly PlannerController ctl;
        private readonly Func<ManeuverPlan> plan;
        private readonly Func<double> shipDvAvailable;
        private readonly Action<int, int> setViewFrame;
        private readonly Func<int[]> getViewFrame;
        private readonly Action planChanged;

        private Button bTransfer, bFlyby, bOrbitCur, bOrbitSet, bCapC, bCapE, bCapF, bTo, bVia, bSearch, bDraft, bCommit, bRemove;
        private Label lFrom, lStatus, lWindowsTitle, lSelected, lPlan, lFlags;
        private TextField fPark, fArr, fEcc, fSpan, fFlight;
        private VisualElement windowList, viaRow, parkRow, eccRow, heatBox;
        private Image heat;
        private Texture2D tex;
        private int version = -1, drawnWindows = -1, drawnSelected = -2;
        private int[] savedFrame, appliedFrame;

        public PlannerView(PlannerController controller, Func<ManeuverPlan> planSource, Func<double> shipDv,
            Action<int, int> setFrame, Func<int[]> getFrame, Action planChanged = null)
        {
            ctl = controller; plan = planSource; shipDvAvailable = shipDv; setViewFrame = setFrame; getViewFrame = getFrame;
            this.planChanged = planChanged;
            name = "planner";
            style.position = Position.Absolute;
            style.left = Length.Percent(3); style.top = Length.Percent(4); style.width = Length.Percent(94); style.height = Length.Percent(92);
            style.backgroundColor = (Color)TabletTheme.PanelBg;
            Border(this, 2, TabletTheme.LineHi);
            style.paddingLeft = 8; style.paddingRight = 8; style.paddingTop = 6; style.paddingBottom = 6;
            style.display = DisplayStyle.None;
            Build();
        }

        private bool open;
        public bool IsOpen { get { return open; } }

        public void Open()
        {
            savedFrame = getViewFrame != null ? getViewFrame() : null;
            appliedFrame = null;
            open = true;
            style.display = DisplayStyle.Flex;
            if (ctl.World != null) { ctl.Settings.To = ctl.Settings.To >= 0 ? ctl.Settings.To : FirstTarget(); }
            version = -1;
            Refresh();
        }

        public void Close()
        {
            ctl.Cancel();
            open = false;
            style.display = DisplayStyle.None;
            // Вернуть систему отсчёта карты, если игрок не менял её сам после нашего переключения.
            if (savedFrame != null && appliedFrame != null && getViewFrame != null)
            {
                int[] now = getViewFrame();
                if (now[0] == appliedFrame[0] && now[1] == appliedFrame[1]) { setViewFrame(savedFrame[0], savedFrame[1]); }
            }
        }

        /// <summary>Каждый кадр: выполнить кусок расчёта и обновить показ, если состояние изменилось.</summary>
        public void Tick()
        {
            if (!IsOpen) { return; }
            ctl.Tick(6d);
            if (ctl.Version != version || ctl.Busy) { Refresh(); }
        }

        // ------------------------------------------------------------------------------------------------ построение

        private void Build()
        {
            var head = Row();
            bTransfer = Btn("ПЕРЕЛЁТ", () => SetMode(PlannerMode.Transfer));
            bFlyby = Btn("ГРАВ. МАНЁВР", () => SetMode(PlannerMode.Flyby));
            head.Add(bTransfer); head.Add(bFlyby);
            var title = Text("ПЛАНИРОВЩИК МАНЁВРОВ", TabletTheme.Bright); title.style.flexGrow = 1; title.style.unityTextAlign = TextAnchor.MiddleCenter;
            head.Add(title);
            head.Add(Btn("✕", Close));
            Add(head);

            var body = Row(); body.style.flexGrow = 1; body.style.marginTop = 6;
            Add(body);

            // ---- левая колонка: настройки
            var left = Column(30);
            lFrom = Text("", TabletTheme.Main); left.Add(lFrom);
            var toRow = Row(); toRow.Add(Text("КУДА", TabletTheme.Dim)); bTo = Btn("", () => CycleBody(false)); bTo.style.flexGrow = 1; toRow.Add(bTo); left.Add(toRow);
            viaRow = Row(); viaRow.Add(Text("ЧЕРЕЗ", TabletTheme.Dim)); bVia = Btn("", () => CycleBody(true)); bVia.style.flexGrow = 1; viaRow.Add(bVia); left.Add(viaRow);

            left.Add(Text("ОРБИТА ОТЛЁТА", TabletTheme.Dim));
            var o = Row(); bOrbitCur = Btn("ТЕКУЩАЯ", () => { ctl.Settings.UseCurrentOrbit = true; Touch(); }); bOrbitSet = Btn("ЗАДАТЬ", () => { ctl.Settings.UseCurrentOrbit = false; Touch(); });
            o.Add(bOrbitCur); o.Add(bOrbitSet); left.Add(o);
            parkRow = Row(); parkRow.Add(Text("ВЫСОТА, КМ", TabletTheme.Dim)); fPark = Field(ctl.Settings.ParkAltitudeKm, v => ctl.Settings.ParkAltitudeKm = v); parkRow.Add(fPark); left.Add(parkRow);

            left.Add(Text("ПРИБЫТИЕ", TabletTheme.Dim));
            var c = Row(); bCapC = Btn("КРУГ", () => SetCapture(CaptureKind.Circular)); bCapE = Btn("ЭЛЛИПС", () => SetCapture(CaptureKind.Elliptic)); bCapF = Btn("ПРОЛЁТ", () => SetCapture(CaptureKind.None));
            c.Add(bCapC); c.Add(bCapE); c.Add(bCapF); left.Add(c);
            var a = Row(); a.Add(Text("ВЫСОТА, КМ", TabletTheme.Dim)); fArr = Field(ctl.Settings.ArrivalAltitudeKm, v => ctl.Settings.ArrivalAltitudeKm = v); a.Add(fArr); left.Add(a);
            eccRow = Row(); eccRow.Add(Text("ЭКСЦЕНТР.", TabletTheme.Dim)); fEcc = Field(ctl.Settings.CaptureEccentricity, v => ctl.Settings.CaptureEccentricity = Math.Max(0d, Math.Min(0.99d, v))); eccRow.Add(fEcc); left.Add(eccRow);

            left.Add(Text("ОГРАНИЧЕНИЯ (0 = АВТО)", TabletTheme.Dim));
            var s = Row(); s.Add(Text("ИСКАТЬ, СУТ", TabletTheme.Dim)); fSpan = Field(0d, v => ctl.Settings.SearchSpanDays = Math.Max(0d, v)); s.Add(fSpan); left.Add(s);
            var f = Row(); f.Add(Text("ПОЛЁТ ≤, СУТ", TabletTheme.Dim)); fFlight = Field(0d, v => ctl.Settings.MaxFlightDays = Math.Max(0d, v)); f.Add(fFlight); left.Add(f);

            bSearch = Btn("НАЙТИ ОКНА", () => { ctl.Search(); SyncMapFrame(); Touch(); }); bSearch.style.marginTop = 8; left.Add(bSearch);
            lStatus = Text("", TabletTheme.Warn); lStatus.style.whiteSpace = WhiteSpace.Normal; left.Add(lStatus);
            body.Add(left);

            // ---- середина: карта окон и список
            var mid = Column(36);
            lWindowsTitle = Text("ОКНА ПЕРЕЛЁТА", TabletTheme.Dim); mid.Add(lWindowsTitle);
            heatBox = new VisualElement(); heatBox.style.height = Length.Percent(48); heatBox.style.marginBottom = 4; Border(heatBox, 1, TabletTheme.Line);
            heat = new Image { scaleMode = ScaleMode.StretchToFill }; heat.style.flexGrow = 1;
            heat.RegisterCallback<PointerDownEvent>(OnHeatClick);
            heatBox.Add(heat); mid.Add(heatBox);
            var sv = new ScrollView(); sv.style.flexGrow = 1; windowList = sv; mid.Add(sv);
            body.Add(mid);

            // ---- правая колонка: решение
            var right = Column(34);
            lSelected = Text("", TabletTheme.Main); lSelected.style.whiteSpace = WhiteSpace.Normal; right.Add(lSelected);
            lFlags = Text("", TabletTheme.Warn); lFlags.style.whiteSpace = WhiteSpace.Normal; right.Add(lFlags);
            lPlan = Text("", TabletTheme.Main); lPlan.style.whiteSpace = WhiteSpace.Normal; lPlan.style.flexGrow = 1; right.Add(lPlan);
            bDraft = Btn("ПОСТРОИТЬ ПЛАН (n-тела)", () => { ctl.BeginDraft(); Touch(); }); right.Add(bDraft);
            bCommit = Btn("В ПЛАН МАНЁВРОВ", () => { ctl.Commit(plan()); Touch(); RebuildCards(); }); right.Add(bCommit);
            bRemove = Btn("УБРАТЬ ИЗ ПЛАНА", () => { ctl.Remove(plan()); Touch(); RebuildCards(); }); right.Add(bRemove);
            body.Add(right);
        }

        /// <summary>The maneuver cards are created by NavTabletView; ask it to rebuild
        /// them after the plan's contents changed.</summary>
        private void RebuildCards()
        {
            if (planChanged != null) { planChanged(); }
        }

        // ------------------------------------------------------------------------------------------------ команды

        private void Touch() { version = -1; Refresh(); }

        private void SetMode(PlannerMode m)
        {
            if (ctl.Settings.Mode == m) { return; }
            ctl.Cancel(); ctl.Settings.Mode = m; ctl.Windows.Clear(); ctl.FlybyWindows.Clear(); ctl.Selected = -1;
            ctl.Settings.Capture = m == PlannerMode.Flyby && ctl.Settings.Capture == CaptureKind.None ? CaptureKind.Circular : ctl.Settings.Capture;
            if (m == PlannerMode.Flyby && ctl.Settings.Via < 0) { ctl.Settings.Via = FirstTarget(); }
            drawnWindows = -1; Touch();
        }

        private void SetCapture(CaptureKind k) { ctl.Settings.Capture = k; Touch(); }

        private int FirstTarget()
        {
            int from = ctl.World.ShipBody(), p = from >= 0 ? ctl.World.ParentOf(from) : -1;
            for (int i = 0; i < ctl.World.BodyCount; i++) { if (i != from && ctl.World.ParentOf(i) == p && p >= 0) { return i; } }
            return -1;
        }

        /// <summary>Листает тела, обращающиеся вокруг того же родителя, что и тело старта (v1).</summary>
        private void CycleBody(bool via)
        {
            int from = ctl.World.ShipBody(), p = from >= 0 ? ctl.World.ParentOf(from) : -1;
            if (p < 0) { return; }
            int cur = via ? ctl.Settings.Via : ctl.Settings.To, n = ctl.World.BodyCount;
            for (int step = 1; step <= n; step++)
            {
                int i = (((cur < 0 ? -1 : cur) + step) % n + n) % n;
                if (i == from || ctl.World.ParentOf(i) != p) { continue; }
                if (via ? i == ctl.Settings.To : i == ctl.Settings.Via && ctl.Settings.Mode == PlannerMode.Flyby) { continue; }
                if (via) { ctl.Settings.Via = i; } else { ctl.Settings.To = i; }
                break;
            }

            Touch();
        }

        private void SyncMapFrame()
        {
            // Окно перелёта лучше смотреть в СК родителя (гелиоцентрической): планета-старт и цель на одной карте.
            int parent = ctl.CommonParent;
            if (parent < 0 || ctl.WindowCount == 0 || setViewFrame == null) { return; }
            setViewFrame(parent, parent);
            appliedFrame = getViewFrame != null ? getViewFrame() : null;
        }

        private void OnHeatClick(PointerDownEvent e)
        {
            PorkchopGrid g = ctl.Grid;
            if (g == null || ctl.Settings.Mode != PlannerMode.Transfer || ctl.Windows.Count == 0) { return; }
            Rect r = heat.contentRect;
            if (r.width < 1f || r.height < 1f) { return; }
            double gi = e.localPosition.x / r.width * g.NDep, gk = (1d - (e.localPosition.y / r.height)) * g.NTof;
            int best = -1; double bestD = double.MaxValue;
            for (int i = 0; i < ctl.Windows.Count; i++)
            {
                TransferSolution w = ctl.Windows[i];
                double wi = (w.T0 - g.T0Start) / g.StepS, wk = (w.Tof / g.StepS) - g.KMin;
                double d = ((wi - gi) * (wi - gi)) + ((wk - gk) * (wk - gk));
                if (d < bestD) { bestD = d; best = i; }
            }

            if (best >= 0) { ctl.Select(best); Touch(); }
        }

        // ------------------------------------------------------------------------------------------------ показ

        private void Refresh()
        {
            version = ctl.Version;
            PlannerSettings st = ctl.Settings;
            bool flyby = st.Mode == PlannerMode.Flyby;
            On(bTransfer, !flyby); On(bFlyby, flyby);
            On(bOrbitCur, st.UseCurrentOrbit); On(bOrbitSet, !st.UseCurrentOrbit);
            On(bCapC, st.Capture == CaptureKind.Circular); On(bCapE, st.Capture == CaptureKind.Elliptic); On(bCapF, st.Capture == CaptureKind.None);
            bCapF.style.display = flyby ? DisplayStyle.None : DisplayStyle.Flex;
            viaRow.style.display = flyby ? DisplayStyle.Flex : DisplayStyle.None;
            parkRow.style.display = st.UseCurrentOrbit ? DisplayStyle.None : DisplayStyle.Flex;
            bOrbitSet.SetEnabled(!flyby); if (flyby) { st.UseCurrentOrbit = true; }
            eccRow.style.display = st.Capture == CaptureKind.Elliptic ? DisplayStyle.Flex : DisplayStyle.None;
            heatBox.style.display = flyby ? DisplayStyle.None : DisplayStyle.Flex;

            IPlannerWorld w = ctl.World;
            int from = w != null ? w.ShipBody() : -1;
            lFrom.text = "ОТКУДА: " + (from >= 0 ? w.BodyName(from) : "—") + (st.UseCurrentOrbit ? " (ТЕКУЩАЯ ОРБИТА)" : " (ОРБИТА " + st.ParkAltitudeKm.ToString("F0", CultureInfo.InvariantCulture) + " КМ)");
            bTo.text = st.To >= 0 && w != null ? w.BodyName(st.To) : "—";
            bVia.text = st.Via >= 0 && w != null ? w.BodyName(st.Via) : "—";
            lStatus.text = ctl.Status + (ctl.Busy ? " [" + ctl.Shots + "]" : "");
            lWindowsTitle.text = flyby ? "МАРШРУТЫ С ГРАВ. МАНЁВРОМ" : "ОКНА ПЕРЕЛЁТА (ЦВЕТ — ΔV; ◆ — ОКНА)";

            RebuildWindowList();
            DrawHeat();
            ShowSelected();

            bDraft.SetEnabled(!ctl.Busy && ctl.Selected >= 0);
            bCommit.SetEnabled(ctl.CanCommit);
            bRemove.SetEnabled(plan != null && plan() != null && HasPlannerManeuvers());
            bDraft.text = ctl.Busy ? "РАСЧЁТ… (ОТМЕНА)" : "ПОСТРОИТЬ ПЛАН (n-тела)";
        }

        private bool HasPlannerManeuvers()
        {
            ManeuverPlan p = plan();
            for (int i = 0; i < p.Count; i++) { if (p[i].PlannerId == ctl.PlannerId) { return true; } }
            return false;
        }

        private void RebuildWindowList()
        {
            if (drawnWindows == ctl.WindowCount && drawnSelected == ctl.Selected) { return; }
            drawnWindows = ctl.WindowCount; drawnSelected = ctl.Selected;
            windowList.Clear();
            bool flyby = ctl.Settings.Mode == PlannerMode.Flyby;
            for (int i = 0; i < ctl.WindowCount; i++)
            {
                int idx = i;
                string t;
                if (flyby)
                {
                    FlybySolution s = ctl.FlybyWindows[i];
                    t = (i + 1) + ". ОТЛЁТ ЧЕРЕЗ " + Days(s.T0 - ctl.World.UniverseNow) + " · ПРИЛЁТ ЧЕРЕЗ " + Days(s.T2 - ctl.World.UniverseNow) + " · ΔV " + Dv(s.Total) + (s.Feasible ? "" : " · НЕДОБОР ПОВОРОТА");
                }
                else
                {
                    TransferSolution s = ctl.Windows[i];
                    t = (i + 1) + ". ОТЛЁТ ЧЕРЕЗ " + Days(s.T0 - ctl.World.UniverseNow) + " · ПОЛЁТ " + Days(s.Tof) + " · ΔV " + Dv(s.Total);
                }

                Button b = Btn(t, () => { ctl.Select(idx); Touch(); });
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                On(b, i == ctl.Selected);
                windowList.Add(b);
            }
        }

        private void DrawHeat()
        {
            PorkchopGrid g = ctl.Grid;
            if (g == null || ctl.Settings.Mode != PlannerMode.Transfer) { heat.image = null; return; }
            if (tex == null || tex.width != g.NDep || tex.height != g.NTof)
            {
                tex = new Texture2D(g.NDep, g.NTof, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }

            float best = float.MaxValue;
            foreach (float v in g.Total) { if (!float.IsNaN(v) && v < best) { best = v; } }
            float cap = best * 1.8f + 1000f;
            double budget = shipDvAvailable != null ? shipDvAvailable() : double.NaN;
            var px = new Color32[g.NDep * g.NTof];
            for (int i = 0; i < g.NDep; i++)
            {
                for (int k = 0; k < g.NTof; k++)
                {
                    float v = g.Total[(i * g.NTof) + k];
                    Color32 c;
                    if (float.IsNaN(v)) { c = TabletTheme.Bg; }
                    else
                    {
                        float tt = Mathf.Clamp01((v - best) / (cap - best));
                        c = Color32.Lerp(TabletTheme.Main, TabletTheme.Line, Mathf.Pow(tt, 0.6f));
                        if (!double.IsNaN(budget) && v > budget && ((i + k) & 1) == 0) { c = TabletTheme.WarnDim; }   // штриховка: не хватит ΔV
                    }

                    px[(k * g.NDep) + i] = c;
                }
            }

            for (int w = 0; w < ctl.Windows.Count; w++)
            {
                TransferSolution s = ctl.Windows[w];
                int i = (int)Math.Round((s.T0 - g.T0Start) / g.StepS), k = (int)Math.Round((s.Tof / g.StepS) - g.KMin);
                Color32 mark = w == ctl.Selected ? TabletTheme.Bright : TabletTheme.Warn;
                for (int d = -2; d <= 2; d++)
                {
                    Put(px, g, i + d, k, mark); Put(px, g, i, k + d, mark);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false);
            heat.image = tex;
        }

        private static void Put(Color32[] px, PorkchopGrid g, int i, int k, Color32 c)
        {
            if (i >= 0 && i < g.NDep && k >= 0 && k < g.NTof) { px[(k * g.NDep) + i] = c; }
        }

        private void ShowSelected()
        {
            lFlags.text = ""; lSelected.text = ""; lPlan.text = "";
            if (ctl.Selected < 0) { return; }
            var sb = new StringBuilder();
            IPlannerWorld w = ctl.World;
            double now = w.UniverseNow;
            if (ctl.Settings.Mode == PlannerMode.Transfer)
            {
                TransferSolution s = ctl.Windows[ctl.Selected];
                sb.AppendLine("ОТЛЁТ  ЧЕРЕЗ " + Days(s.T0 - now));
                sb.AppendLine("ПРИЛЁТ ЧЕРЕЗ " + Days(s.T1 - now) + " (ПОЛЁТ " + Days(s.Tof) + ")");
                sb.AppendLine("СКОРОСТЬ ОТНОСИТЕЛЬНО ПЛАНЕТЫ (v∞): " + (s.VInfDep.Magnitude / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " КМ/С");
                sb.AppendLine("ΔV ОТЛЁТ " + Dv(s.DepDv) + " · ЗАХВАТ " + Dv(s.ArrDv));
                sb.Append("ИТОГО (ИДЕАЛ) " + Dv(s.Total));
            }
            else
            {
                FlybySolution s = ctl.FlybyWindows[ctl.Selected];
                double alt = s.Flyby.PeriapsisRadius - w.Radius(ctl.Settings.Via);
                sb.AppendLine("ОТЛЁТ ЧЕРЕЗ " + Days(s.T0 - now) + " · ПРОЛЁТ ЧЕРЕЗ " + Days(s.T1 - now) + " · ПРИЛЁТ ЧЕРЕЗ " + Days(s.T2 - now));
                sb.AppendLine("ПРОЛЁТ: ВЫСОТА " + Dist(alt) + " · ПОВОРОТ " + (s.Flyby.TurnNeeded * 180d / Math.PI).ToString("F1", CultureInfo.InvariantCulture) + "° (МАКС. " + (s.Flyby.TurnMax * 180d / Math.PI).ToString("F1", CultureInfo.InvariantCulture) + "°)");
                sb.AppendLine(s.Trailing ? "ПРОХОД ПОЗАДИ ПЛАНЕТЫ: РАЗГОН" : "ПРОХОД ВПЕРЕДИ ПЛАНЕТЫ: ТОРМОЖЕНИЕ");
                sb.AppendLine("v∞ ВХОД/ВЫХОД " + (s.VInfIn.Magnitude / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " / " + (s.VInfOut.Magnitude / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " КМ/С · ДАРОМ " + Dv(s.FreeVelocityChange));
                sb.AppendLine("ΔV ОТЛЁТ " + Dv(s.DepDv) + " · У ГИГАНТА " + Dv(s.Flyby.PoweredDv) + " · ЗАХВАТ " + Dv(s.ArrDv));
                sb.Append("ИТОГО (ИДЕАЛ) " + Dv(s.Total));
                if (!s.Feasible) { lFlags.text = "ПЛАНЕТА НЕ ПОВЕРНЁТ ТАК СИЛЬНО: НЕДОБОР " + Dv(s.Flyby.DeficitDv) + ". ВЫБЕРИТЕ ДРУГОЕ ОКНО."; }
            }

            lSelected.text = sb.ToString();
            PlanDraft d = ctl.Draft;
            if (d == null) { return; }
            var p = new StringBuilder();
            if (d.Valid)
            {
                p.AppendLine("ПЛАН: " + Dv(d.TotalDv) + " (ПАТЧ-КОНИКИ " + Dv(d.PatchedDv) + ")");
                if (d.Refined) { p.AppendLine("n-ТЕЛА: ПРОМАХ " + Dist(d.MissBeforeM) + " → " + Dist(d.MissAfterM)); }
                foreach (PlannedImpulse i in d.Impulses)
                {
                    p.AppendLine(i.Label + " · ЧЕРЕЗ " + Days(i.Time - now) + " · " + Dv(i.DvWorld.Magnitude) + " · СК " + (i.FrameBody >= 0 ? w.BodyName(i.FrameBody) : "—"));
                }

                if (d.PlanePenaltyDv > 5d) { lFlags.text += (lFlags.text.Length > 0 ? "\n" : "") + "ОРБИТА НЕ СОГЛАСОВАНА С ОКНОМ: +" + Dv(d.PlanePenaltyDv) + " К ИДЕАЛУ (ПЛОСКОСТЬ/ФАЗА)."; }
                if (d.Preliminary) { lFlags.text += (lFlags.text.Length > 0 ? "\n" : "") + "ОРИЕНТИРОВОЧНО: ОКНО ДАЛЕКО — ПЕРЕСЧИТАЙТЕ ЗА 3 СУТ ДО ОТЛЁТА."; }
                double avail = shipDvAvailable != null ? shipDvAvailable() : double.NaN;
                if (!double.IsNaN(avail) && d.TotalDv > avail) { lFlags.text += (lFlags.text.Length > 0 ? "\n" : "") + "НЕ ХВАТАЕТ ТОПЛИВА: НУЖНО " + Dv(d.TotalDv) + ", ЕСТЬ " + Dv(avail) + "."; }
            }

            lPlan.text = p.ToString();
        }

        // ------------------------------------------------------------------------------------------------ мелочи

        private static string Dv(double ms) { return ms.ToString("F1", CultureInfo.InvariantCulture) + " М/С"; }
        private static string Days(double s) { return (s / 86400d).ToString("F1", CultureInfo.InvariantCulture) + " СУТ"; }
        private static string Dist(double m)
        {
            double a = Math.Abs(m);
            if (a >= 1e9d) { return (m / 1e9d).ToString("F2", CultureInfo.InvariantCulture) + " МЛН КМ"; }
            if (a >= 1e4d) { return (m / 1e3d).ToString("F0", CultureInfo.InvariantCulture) + " КМ"; }
            return m.ToString("F0", CultureInfo.InvariantCulture) + " М";
        }
        private static void On(Button b, bool on) { b.EnableInClassList("btn--on", on); }

        private static void Border(VisualElement e, float w, Color32 c)
        {
            e.style.borderTopWidth = w; e.style.borderBottomWidth = w; e.style.borderLeftWidth = w; e.style.borderRightWidth = w;
            e.style.borderTopColor = (Color)c; e.style.borderBottomColor = (Color)c; e.style.borderLeftColor = (Color)c; e.style.borderRightColor = (Color)c;
        }

        private static VisualElement Row() { var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; r.style.alignItems = Align.Center; return r; }

        private static VisualElement Column(float widthPercent)
        {
            var c = new VisualElement();
            c.style.width = Length.Percent(widthPercent); c.style.flexShrink = 0; c.style.paddingLeft = 4; c.style.paddingRight = 4;
            return c;
        }

        private static Label Text(string t, Color32 color) { var l = new Label(t); l.style.color = (Color)color; return l; }

        private static Button Btn(string t, Action a)
        {
            var b = new Button(a) { text = t };
            b.AddToClassList("btn");
            return b;
        }

        private TextField Field(double initial, Action<double> set)
        {
            var f = new TextField { value = initial.ToString("0.###", CultureInfo.InvariantCulture) };
            f.style.flexGrow = 1;
            f.RegisterValueChangedCallback(e =>
            {
                double v;
                if (double.TryParse(e.newValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) { set(v); }
            });
            return f;
        }
    }
}
