using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Galilego.Tablet
{
    /// <summary>
    /// UI Toolkit view of the navigation tablet. Binds <see cref="NavState"/> to NavTablet.uxml / NavTablet.uss:
    /// panels with slide animation, reference-frame table, rate sliders, time fields, maneuver cards, clock.
    /// It does not draw the map: a map camera renders into a RenderTexture that is passed to <see cref="SetMapTexture"/>,
    /// and the map controller listens to the Map* events (drag, zoom, hover).
    /// </summary>
    public sealed class NavTabletView : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private VisualTreeAsset maneuverCard;

        public NavState State { get; } = new NavState();

        /// <summary>The view is bound to a live UIDocument and can be ticked.</summary>
        public bool Bound { get { return bound; } }

        /// <summary>Seconds since mission start (shown as T+). Feed it from the simulation clock.</summary>
        public double MissionSeconds;

        /// <summary>Universe seconds from the game epoch (shown as the in-world date via NavState.WorldEpochUtc).</summary>
        public double UniverseSeconds;

        /// <summary>For burn times (rocket equation). Replace with per-stage data from DvTabletAdapter.</summary>
        public double ShipMassKg = 14000d;
        public double ThrustN = 40000d;
        public double IspS = 330d;

        public event Action<int> FrameChanged;
        public event Action<int> CenterChanged;
        public event Action<int> OrbitToggled;
        public event Action<Maneuver> ManeuverDue;
        public event Action<Vector2> MapDragged;         // pixel delta; always a turntable rotation
        public event Action<Vector2, float> MapZoomed;   // cursor position (unused for the pivot), wheel delta
        public event Action<Vector2> MapHovered;         // cursor position in map (for picking planets / markers)
        public event Action MapPointerLeft;
        public event Action PlannerClosed;
        public event Action MapReset;                    // double-click: back to the default view

        private sealed class Card
        {
            public Maneuver M;
            public VisualElement Root;
            public Label Title, Dv, Burn;
            public TextField FieldT;
            public readonly TextField[] FieldDv = new TextField[3];
            public RateSliderElement SlT;
            public readonly RateSliderElement[] SlDv = new RateSliderElement[3];

            // Строка манёвра планировщика (создаётся кодом, в карточке UXML её нет): подпись, СК манёвра, «вернуть решение».
            public VisualElement PlannerRow;
            public Label PlannerInfo;
            public DropdownField PlannerFrame;
            public Button PlannerRestore;
        }

        private readonly List<Card> cards = new List<Card>();
        private readonly VisualElement[] panelRoots = new VisualElement[3];
        private readonly Label[] panelLabels = new Label[3];
        private static readonly string[] PanelTitles = { "СИСТЕМЫ ОТСЧЁТА", "ВРЕМЯ ЛИНИЙ", "ПЛАН ПОЛЁТА" };

        private VisualElement root, mapView, mapRoot, devRoot, framesTable, clock;
        private Label tip, mission, worldDate, framesDesc, sDv, sBurn, sNext;
        private ScrollView planList;
        private TextField fForecast, fHistory;
        private RateSliderElement slForecast, slHistory;
        private DropdownField mFrame;
        private int maneuverFrame;
        private PlannerController plannerController;
        private PlannerView planner;
        private Func<double> plannerShipDv;
        private VisualElement plannerTab;
        private bool bound;
        private float refreshTimer;
        private Texture lastMapTexture;
        private Label lForecast;
        private bool forecastAvailable = true;

        /// <summary>
        /// False when there is nothing to predict (no ship / no support point): the
        /// slider must say so instead of silently changing a value that draws nothing.
        /// A landed craft still has a forecast — its site path over the ephemerides.
        /// </summary>
        public bool ForecastAvailable
        {
            get { return forecastAvailable; }
            set
            {
                if (forecastAvailable == value)
                {
                    return;
                }

                forecastAvailable = value;
                ApplyForecastAvailability();
            }
        }

        /// <summary>Create the view on an inactive object, then activate it (UIDocument builds the tree on enable).</summary>
        public void Configure(UIDocument doc, VisualTreeAsset card)
        {
            document = doc;
            maneuverCard = card;
        }

        private void OnEnable()
        {
            TryBind();
        }

        private void Start()
        {
            TryBind();
        }

        private void Update()
        {
            if (!bound)
            {
                TryBind();
            }

            if (planner != null) { planner.Tick(); }
        }

        // ------------------------------------------------------------------ planner window

        /// <summary>
        /// Подключает планировщик (вызывает станция после создания мира: нужны StarSystem и корабль). Окно строится кодом
        /// при ближайшей возможности; кнопка «ПЛАНИРОВЩИК» добавляется над списком манёвров.
        /// </summary>
        public void AttachPlanner(PlannerController controller, Func<double> shipDvAvailable)
        {
            plannerController = controller;
            plannerShipDv = shipDvAvailable;
            EnsurePlanner();
        }

        private void EnsurePlanner()
        {
            if (!bound || plannerController == null || planner != null) { return; }
            planner = new PlannerView(plannerController, () => State.Plan, plannerShipDv, SetViewFrame,
                () => new[] { State.FrameIndex, State.CenterIndex }, RebuildCards);
            // Both menus live on the map; opening one closes the other so the
            // overlay never covers the panel the player is reading.
            planner.Opened += () =>
            {
                if (State.Panels[(int)NavPanel.Frames].Open)
                {
                    State.Panels[(int)NavPanel.Frames].Open = false;
                    ApplyPanel(NavPanel.Frames);
                }
            };
            planner.Closed += () => { if (PlannerClosed != null) { PlannerClosed(); } };
            VisualElement host = devRoot != null ? devRoot : root;
            host.Add(planner);

            plannerTab = new VisualElement { name = "planner-tab" };
            plannerTab.AddToClassList("planner-rail-tab");
            var plannerLabel = new Label("ПЛАНИРОВЩИК");
            plannerLabel.AddToClassList("planner-rail-label");
            plannerTab.Add(plannerLabel);
            plannerTab.tooltip = "Планировщик манёвров: открыть/закрыть";
            plannerTab.AddManipulator(new Clickable(TogglePlanner));
            VisualElement tabHost = mapRoot != null ? mapRoot : root;
            tabHost.Add(plannerTab);
            VisualElement framesPanel = panelRoots[(int)NavPanel.Frames];
            if (framesPanel != null) { framesPanel.RegisterCallback<GeometryChangedEvent>(_ => PositionPlannerTab()); }
            if (mapRoot != null) { mapRoot.RegisterCallback<GeometryChangedEvent>(_ => PositionPlannerTab()); }
            PositionPlannerTab();
        }

        /// <summary>
        /// The planner tab is the second entry in the left menu rail: it sits
        /// directly under the reference-frame panel, whatever its height is.
        /// </summary>
        private void PositionPlannerTab()
        {
            if (plannerTab == null || mapRoot == null) { return; }
            VisualElement framesPanel = panelRoots[(int)NavPanel.Frames];
            float top = framesPanel != null ? framesPanel.layout.yMax + 6f : 300f;
            float maxTop = Mathf.Max(44f, mapRoot.layout.height - 94f);
            plannerTab.style.top = Mathf.Min(top, maxTop);
        }

        private void TogglePlanner()
        {
            if (planner == null) { return; }
            if (planner.IsOpen) { planner.Close(); } else { planner.Open(); }
        }

        /// <summary>
        /// Система отсчёта и центр карты — тем же путём, что и клик «ИСК»/«⌖» в таблице систем отсчёта (события
        /// FrameChanged/CenterChanged доходят до карты). Планировщик переключает вид на родителя общего уровня и
        /// возвращает прежний, если игрок не менял его сам.
        /// </summary>
        public void SetViewFrame(int frameId, int centerId)
        {
            FrameChangeFromPlanner = true;
            try
            {
                State.SetFrame(frameId);
                if (FrameChanged != null) { FrameChanged(frameId); }
                State.SetCenter(centerId);
                if (CenterChanged != null) { CenterChanged(centerId); }
            }
            finally { FrameChangeFromPlanner = false; }

            RebuildFrames();
        }

        /// <summary>true на время события FrameChanged, вызванного планировщиком (станция не закрепляет такую СК за игроком).</summary>
        public bool FrameChangeFromPlanner { get; private set; }

        /// <summary>Окно планировщика открыто: автовыбор системы отсчёта карты не должен перебивать его вид.</summary>
        public bool PlannerOpen { get { return planner != null && planner.IsOpen; } }

        private void TryBind()
        {
            if (bound || document == null)
            {
                return;
            }

            VisualElement tree = document.rootVisualElement;
            if (tree == null || tree.childCount == 0)
            {
                return;
            }

            root = tree;
            mapView = root.Q("map-view");
            mapRoot = root.Q("map");
            devRoot = root.Q("dev");

            tip = root.Q<Label>("tip");
            mission = root.Q<Label>("mission");
            worldDate = root.Q<Label>("world-date");
            framesDesc = root.Q<Label>("frames-desc");
            framesTable = root.Q("frames-table");
            sDv = root.Q<Label>("s-dv");
            sBurn = root.Q<Label>("s-burn");
            sNext = root.Q<Label>("s-next");
            planList = root.Q<ScrollView>("plan-list");

            BindPanel(NavPanel.Frames, "frames", "frames-tab", "frames-tab-l");
            BindPanel(NavPanel.Time, "time", "time-tab", "time-tab-l");
            BindPanel(NavPanel.Plan, "plan", "plan-tab", "plan-tab-l");

            slForecast = root.Q<RateSliderElement>("sl-forecast");
            slHistory = root.Q<RateSliderElement>("sl-history");
            slForecast.Model = State.ForecastSlider;
            slHistory.Model = State.HistorySlider;
            fForecast = root.Q<TextField>("f-forecast");
            fHistory = root.Q<TextField>("f-history");
            lForecast = root.Q<Label>("l-forecast");
            fForecast.RegisterValueChangedCallback(e => { double t; if (TabletTime.TryParse(e.newValue, out t)) { State.SetForecast(t); } });
            fHistory.RegisterValueChangedCallback(e => { double t; if (TabletTime.TryParse(e.newValue, out t)) { State.SetHistory(t); } });

            mFrame = root.Q<DropdownField>("m-frame");
            mFrame.RegisterValueChangedCallback(e => { maneuverFrame = Mathf.Max(0, mFrame.index); State.Plan.SetFrameBody(maneuverFrame); });

            root.Q<Button>("add").clicked += () =>
            {
                State.Plan.Add(maneuverFrame, Math.Max(State.ForecastS / 10d, 600d));
                RebuildCards();
            };

            mapView.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.clickCount >= 2)
                {
                    HideTip();
                    if (MapReset != null) { MapReset(); }
                    e.StopPropagation();
                    return;
                }

                mapView.CapturePointer(e.pointerId);
            });
            mapView.RegisterCallback<PointerUpEvent>(e => mapView.ReleasePointer(e.pointerId));
            mapView.RegisterCallback<PointerLeaveEvent>(e => { HideTip(); if (MapPointerLeft != null) { MapPointerLeft(); } });
            mapView.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (mapView.HasPointerCapture(e.pointerId))
                {
                    HideTip();
                    // Both buttons rotate around the selected centre; the centre
                    // body is locked to the screen middle, so there is no pan.
                    if (MapDragged != null) { MapDragged(e.deltaPosition); }
                }
                else if (MapHovered != null)
                {
                    MapHovered(e.localPosition);
                }
            });
            mapView.RegisterCallback<WheelEvent>(e =>
            {
                if (MapZoomed != null) { MapZoomed(e.localMousePosition, e.delta.y); }
                e.StopPropagation();
            });

            for (int i = 0; i < 3; i++) { ApplyPanel((NavPanel)i); }
            bound = true;
            SetBodies(State.BodyNames);
            RebuildCards();
            ApplyForecastAvailability();
            EnsurePlanner();
        }

        private void ApplyForecastAvailability()
        {
            if (!bound)
            {
                return;
            }

            if (lForecast != null)
            {
                lForecast.text = forecastAvailable ? "ДЛИНА ПРОГНОЗА" : "ПРОГНОЗ ВЫКЛ";
                lForecast.EnableInClassList("row-l--tan", !forecastAvailable);
            }

            float opacity = forecastAvailable ? 1f : 0.45f;
            if (slForecast != null)
            {
                slForecast.style.opacity = opacity;
                slForecast.tooltip = forecastAvailable
                    ? null
                    : "Прогноз недоступен: нет аппарата или опорной точки";
            }

            if (fForecast != null)
            {
                fForecast.style.opacity = opacity;
                fForecast.tooltip = forecastAvailable
                    ? null
                    : "Прогноз недоступен: нет аппарата или опорной точки";
            }
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Body list from the simulation (names as shown on screen). Call once after the universe is loaded.</summary>
        public void SetBodies(string[] names, bool[] orbitAvailable = null)
        {
            State.SetBodies(names, orbitAvailable);
            if (!bound || mFrame == null)
            {
                return;
            }

            var choices = new List<string>(State.BodyNames);
            mFrame.choices = choices;
            maneuverFrame = Mathf.Clamp(maneuverFrame, 0, Mathf.Max(0, choices.Count - 1));
            if (choices.Count > 0) { mFrame.SetValueWithoutNotify(choices[maneuverFrame]); }
            RebuildFrames();
        }

        /// <summary>Map camera output.</summary>
        public void SetMapTexture(Texture texture)
        {
            if (mapView == null || ReferenceEquals(texture, lastMapTexture))
            {
                return;
            }

            lastMapTexture = texture;
            if (texture is RenderTexture rt)
            {
                mapView.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            }
            else if (texture is Texture2D t2d)
            {
                mapView.style.backgroundImage = new StyleBackground(t2d);
            }
        }

        /// <summary>Current map view size in UI pixels (the texture is stretched to it).</summary>
        public Vector2 MapSize
        {
            get
            {
                if (mapView == null)
                {
                    return Vector2.zero;
                }

                return new Vector2(mapView.resolvedStyle.width, mapView.resolvedStyle.height);
            }
        }

        /// <summary>
        /// Device pixels per UI Toolkit pixel: the panel covers the screen but its
        /// layout size is in UI units, so the ratio is the real scale the map
        /// texture is displayed at. A 1024 px texture stretched onto a smaller panel
        /// is resampled by the GPU (and point filtering drops whole rows), which is
        /// what made the orbit lines look chunky; rendering at this size keeps the
        /// upload 1:1 and the anti-aliased lines smooth.
        /// </summary>
        public float PanelScale
        {
            get
            {
                if (root == null)
                {
                    return 1f;
                }

                float width = root.resolvedStyle.width;
                if (!(width > 1f))
                {
                    return 1f;
                }

                float scale = Screen.width / width;
                return float.IsFinite(scale) && scale >= 0.25f && scale <= 8f ? scale : 1f;
            }
        }

        public void ShowTip(string richText, Vector2 mapPos)
        {
            tip.text = richText;
            float maxX = Mathf.Max(0f, mapView.resolvedStyle.width - 180f);
            tip.style.left = Mathf.Min(mapPos.x + 12f, maxX);
            tip.style.top = mapPos.y + 12f;
            tip.AddToClassList("tip--on");
        }

        public void HideTip()
        {
            tip.RemoveFromClassList("tip--on");
        }

        /// <summary>Per-frame update. simDt is simulated time (warp-aware), realDt drives animation and sliders.</summary>
        public void Tick(float realDt, double simDt)
        {
            if (!bound)
            {
                return;
            }

            State.Tick(realDt);

            List<Maneuver> due = State.Plan.AdvanceTime(simDt);
            if (due.Count > 0)
            {
                foreach (Maneuver m in due) { if (ManeuverDue != null) { ManeuverDue(m); } }
                RebuildCards();
            }

            TickCardSliders(realDt);
            State.Plan.ComputeBurns(ShipMassKg, ThrustN, IspS);

            // Text is layout work in UI Toolkit: 10 Hz is indistinguishable from
            // every frame for a clock/readout panel, and leaves the frame budget
            // to the sim and the planet.
            refreshTimer += realDt;
            if (refreshTimer >= 0.1f)
            {
                refreshTimer = 0f;
                Refresh();
            }
        }

        // ------------------------------------------------------------------ panels

        private void BindPanel(NavPanel id, string panelName, string tabName, string labelName)
        {
            int i = (int)id;
            panelRoots[i] = root.Q(panelName);
            panelLabels[i] = root.Q<Label>(labelName);
            root.Q(tabName).AddManipulator(new Clickable(() =>
            {
                State.TogglePanel(id);
                ApplyPanel(id);
                if (id == NavPanel.Frames && State.Panels[(int)NavPanel.Frames].Open && planner != null)
                {
                    planner.Close();
                }
            }));
        }

        private void ApplyPanel(NavPanel id)
        {
            int i = (int)id;
            bool open = State.Panels[i].Open;
            panelRoots[i].EnableInClassList("collapsed", !open);
            panelLabels[i].text = (open ? "▾ " : "▸ ") + PanelTitles[i];
        }

        // ------------------------------------------------------------------ reference frames table

        private void RebuildFrames()
        {
            if (framesTable == null) { return; }
            framesTable.Clear();
            if (State.BodyCount > 0)
            {
                framesDesc.text = "ИНЕРЦИАЛЬНАЯ СК: " + State.BodyNames[State.FrameIndex] + "\nЦЕНТР ТЕЛА НЕПОДВИЖЕН\nСИСТЕМА НЕ ВРАЩАЕТСЯ";
            }

            for (int id = 0; id <= State.ShipId; id++)
            {
                framesTable.Add(MakeFrameRow(id));
            }
        }

        private VisualElement MakeFrameRow(int id)
        {
            bool ship = id == State.ShipId;
            string name = ship ? "АППАРАТ" : State.BodyNames[id];

            var row = new VisualElement();
            row.AddToClassList("tr");

            var nm = new VisualElement();
            nm.AddToClassList("tr-name");
            bool canToggle = id < State.OrbitAvailable.Length && State.OrbitAvailable[id];
            nm.tooltip = !canToggle
                ? "У этого тела нет орбиты вокруг родителя"
                : (ship ? "Показать/скрыть траекторию аппарата" : "Показать/скрыть след (историю и прогноз)");
            var dot = new VisualElement();
            dot.AddToClassList("dot");
            dot.EnableInClassList("dot--on", canToggle && State.OrbitVisible[id]);
            dot.EnableInClassList("dot--off", !canToggle || !State.OrbitVisible[id]);
            if (!canToggle)
            {
                dot.style.opacity = 0.35f;
            }

            nm.Add(dot);
            nm.Add(new Label(name));
            if (canToggle)
            {
                nm.RegisterCallback<ClickEvent>(e =>
                {
                    State.ToggleOrbit(id);
                    if (OrbitToggled != null) { OrbitToggled(id); }
                    RebuildFrames();
                    e.StopPropagation();
                });
            }
            row.Add(nm);

            var center = new Button(() =>
            {
                State.SetCenter(id);
                if (CenterChanged != null) { CenterChanged(id); }
                RebuildFrames();
            }) { text = "⌖" };
            center.AddToClassList("btn");
            center.AddToClassList("tr-b");
            center.EnableInClassList("btn--on", State.CenterIndex == id);
            row.Add(center);

            if (!ship)
            {
                var isk = new Button(() =>
                {
                    State.SetFrame(id);
                    if (FrameChanged != null) { FrameChanged(id); }
                    RebuildFrames();
                }) { text = "ИСК" };
                isk.AddToClassList("btn");
                isk.AddToClassList("tr-b");
                isk.AddToClassList("tr-isk");
                isk.EnableInClassList("btn--on", State.FrameIndex == id);
                row.Add(isk);
            }
            else
            {
                var spacer = new VisualElement();
                spacer.AddToClassList("tr-b");
                spacer.AddToClassList("tr-isk");
                row.Add(spacer);   // the ship is never a reference frame
            }

            return row;
        }

        // ------------------------------------------------------------------ maneuver cards

        private void RebuildCards()
        {
            if (planList == null || maneuverCard == null) { return; }
            planList.Clear();
            cards.Clear();
            ManeuverPlan plan = State.Plan;
            for (int i = 0; i < plan.Count; i++)
            {
                cards.Add(MakeCard(plan[i]));
                planList.Add(cards[i].Root);
            }
        }

        private Card MakeCard(Maneuver m)
        {
            var c = new Card { M = m, Root = maneuverCard.Instantiate() };
            VisualElement r = c.Root;
            c.Title = r.Q<Label>("title");
            c.Dv = r.Q<Label>("dv");
            c.Burn = r.Q<Label>("burn");
            c.FieldT = r.Q<TextField>("f-t");
            c.SlT = r.Q<RateSliderElement>("sl-t");

            r.Q<Button>("up").clicked += () => { if (State.Plan.MoveUp(State.Plan.Items.IndexOf(m))) { RebuildCards(); } };
            r.Q<Button>("down").clicked += () => { if (State.Plan.MoveDown(State.Plan.Items.IndexOf(m))) { RebuildCards(); } };
            r.Q<Button>("del").clicked += () => { if (State.Plan.Remove(State.Plan.Items.IndexOf(m))) { RebuildCards(); } };

            r.Q<Button>("z-t").clicked += () => State.Plan.ResetTime(State.Plan.Items.IndexOf(m));
            c.FieldT.RegisterValueChangedCallback(e =>
            {
                double t;
                if (TabletTime.TryParse(e.newValue, out t)) { State.Plan.SetTime(State.Plan.Items.IndexOf(m), t); }
            });

            for (int a = 0; a < 3; a++)
            {
                int axis = a;
                c.FieldDv[a] = r.Q<TextField>("f-" + a);
                c.SlDv[a] = r.Q<RateSliderElement>("sl-" + a);
                r.Q<Button>("z-" + a).clicked += () => State.Plan.ResetDv(State.Plan.Items.IndexOf(m), axis);
                c.FieldDv[a].RegisterValueChangedCallback(e =>
                {
                    double v;
                    if (double.TryParse(e.newValue.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    {
                        State.Plan.SetDv(State.Plan.Items.IndexOf(m), axis, v);
                    }
                });
            }

            if (m.PlannerId != 0) { BuildPlannerRow(c); }
            return c;
        }

        /// <summary>
        /// Манёвр решения планировщика: подпись роли, ВЫПАДАЮЩИЙ СПИСОК СК этого манёвра и «ВЕРНУТЬ РЕШЕНИЕ». Смена СК здесь
        /// пересчитывает T/N/B так, что физический импульс остаётся прежним (общий селектор СК такие манёвры не трогает).
        /// Правка времени и T/N/B обычными полями работает как у любого манёвра; отклонение от решения помечается в заголовке.
        /// </summary>
        private void BuildPlannerRow(Card c)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            c.PlannerInfo = new Label();
            c.PlannerInfo.style.flexGrow = 1;
            row.Add(c.PlannerInfo);

            c.PlannerFrame = new DropdownField(new List<string>(State.BodyNames), Mathf.Clamp(c.M.FrameBody, 0, Mathf.Max(0, State.BodyNames.Length - 1)));
            c.PlannerFrame.tooltip = "СК манёвра (импульс сохраняется)";
            c.PlannerFrame.RegisterValueChangedCallback(e =>
            {
                int idx = State.Plan.Items.IndexOf(c.M);
                int body = c.PlannerFrame.index;
                var bodies = plannerController != null ? plannerController.World as IBodyStateSource : null;
                if (idx < 0 || State.Plan.ReframeManeuver(idx, body, bodies)) { return; }
                c.PlannerFrame.SetValueWithoutNotify(State.BodyNames[Mathf.Clamp(c.M.FrameBody, 0, State.BodyNames.Length - 1)]);   // no node state yet (forecast not built) — roll back
            });
            row.Add(c.PlannerFrame);

            c.PlannerRestore = new Button(() => State.Plan.RestorePlanned(State.Plan.Items.IndexOf(c.M))) { text = "ВЕРНУТЬ" };
            c.PlannerRestore.AddToClassList("btn");
            c.PlannerRestore.tooltip = "Вернуть время и импульс решения планировщика";
            row.Add(c.PlannerRestore);

            c.PlannerRow = row;
            c.Root.Add(row);
        }

        private static string RoleTitle(ManeuverRole role)
        {
            switch (role)
            {
                case ManeuverRole.PrepOrbit: return "ПОДГОТОВКА ОРБИТЫ";
                case ManeuverRole.Departure: return "ОТЛЁТ";
                case ManeuverRole.FlybyBurn: return "ПЕРИАПСИС ГРАВ. МАНЁВРА";
                case ManeuverRole.Capture: return "ЗАХВАТ";
                case ManeuverRole.Correction: return "КОРРЕКЦИЯ";
                default: return "МАНЁВР";
            }
        }

        private void TickCardSliders(float dt)
        {
            ManeuverPlan plan = State.Plan;
            foreach (Card c in cards)
            {
                int idx = plan.Items.IndexOf(c.M);
                if (idx < 0) { continue; }

                double ct = c.SlT.Model.Tick(dt);
                if (ct != 0d)
                {
                    double v = RateSlider.AdvanceLinear(c.M.TimeToStartS, ct, Math.Max(State.ForecastS * 0.25d, 600d), dt, ManeuverPlan.MinStartS, 1e12);
                    plan.SetTime(idx, v);
                }

                c.SlT.Sync();
                for (int a = 0; a < 3; a++)
                {
                    double cd = c.SlDv[a].Model.Tick(dt);
                    if (cd != 0d)
                    {
                        plan.SetDv(idx, a, RateSlider.AdvanceLinear(c.M.GetDv(a), cd, Math.Max(160d, Math.Abs(c.M.GetDv(a)) * 0.25d), dt, -ManeuverPlan.MaxDvPerAxis, ManeuverPlan.MaxDvPerAxis));
                    }

                    c.SlDv[a].Sync();
                }
            }

            slForecast.Sync();
            slHistory.Sync();
        }

        // ------------------------------------------------------------------ text refresh

        private void Refresh()
        {
            mission.text = TabletTime.Mission(MissionSeconds);
            worldDate.text = TabletTime.WorldDate(UniverseSeconds, State.WorldEpochUtc);

            SetField(fForecast, TabletTime.Field(State.ForecastS));
            SetField(fHistory, TabletTime.Field(State.HistoryS));

            ManeuverPlan plan = State.Plan;
            int conflict = plan.ConflictIndex;
            for (int i = 0; i < cards.Count; i++)
            {
                Card c = cards[i];
                c.Root.EnableInClassList("mc--bad", i == conflict);
                bool fromPlanner = c.M.PlannerId != 0;
                c.Title.text = (fromPlanner ? RoleTitle(c.M.Role) : "МАНЁВР") + " " + (i + 1).ToString(CultureInfo.InvariantCulture);
                if (fromPlanner)
                {
                    bool edited = plan.IsEdited(c.M);
                    c.PlannerInfo.text = edited ? "ИЗМЕНЁН ВРУЧНУЮ" : "РЕШЕНИЕ ПЛАНИРОВЩИКА";
                    c.PlannerInfo.style.color = edited ? (Color)TabletTheme.Warn : (Color)TabletTheme.Dim;
                    c.PlannerRestore.SetEnabled(edited);
                    if (c.PlannerFrame.index != c.M.FrameBody && c.M.FrameBody >= 0 && c.M.FrameBody < State.BodyNames.Length)
                    {
                        c.PlannerFrame.SetValueWithoutNotify(State.BodyNames[c.M.FrameBody]);
                    }
                }

                SetField(c.FieldT, TabletTime.Field(c.M.TimeToStartS));
                for (int a = 0; a < 3; a++) { SetField(c.FieldDv[a], c.M.GetDv(a).ToString("F2", CultureInfo.InvariantCulture)); }
                c.Dv.text = c.M.DvMagnitude.ToString("F2", CultureInfo.InvariantCulture) + " М/С";
                c.Burn.text = c.M.BurnTimeS.ToString("F1", CultureInfo.InvariantCulture) + " С";
            }

            sDv.text = plan.TotalDv.ToString("F2", CultureInfo.InvariantCulture) + " М/С";
            sBurn.text = plan.TotalBurnS.ToString("F1", CultureInfo.InvariantCulture) + " С";
            sNext.text = plan.Count > 0 ? TabletTime.Countdown(plan.NextStartS) : TabletFormat.Dash;
        }

        private static void SetField(TextField f, string text)
        {
            if (!IsFocused(f) && f.value != text) { f.SetValueWithoutNotify(text); }
        }

        private static bool IsFocused(VisualElement f)
        {
            var fc = f.panel != null ? f.panel.focusController : null;
            var fe = fc != null ? fc.focusedElement as VisualElement : null;
            return fe != null && (fe == f || f.Contains(fe));
        }
    }
}
