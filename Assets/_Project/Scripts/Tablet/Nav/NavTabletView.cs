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
        public event Action<Vector2, bool> MapDragged;   // pixel delta, rotate (right button or Shift)
        public event Action<Vector2, float> MapZoomed;   // cursor position in map, wheel delta
        public event Action<Vector2> MapHovered;         // cursor position in map (for picking planets / markers)
        public event Action MapPointerLeft;

        private sealed class Card
        {
            public Maneuver M;
            public VisualElement Root;
            public Label Title, Dv, Burn;
            public TextField FieldT;
            public readonly TextField[] FieldDv = new TextField[3];
            public RateSliderElement SlT;
            public readonly RateSliderElement[] SlDv = new RateSliderElement[3];
        }

        private readonly List<Card> cards = new List<Card>();
        private readonly VisualElement[] panelRoots = new VisualElement[3];
        private readonly Label[] panelLabels = new Label[3];
        private static readonly string[] PanelTitles = { "СИСТЕМЫ ОТСЧЁТА", "ВРЕМЯ ЛИНИЙ", "ПЛАН ПОЛЁТА" };

        private VisualElement root, mapView, framesTable, clock;
        private Label tip, mission, worldDate, framesDesc, sDv, sBurn, sNext;
        private ScrollView planList;
        private TextField fForecast, fHistory;
        private RateSliderElement slForecast, slHistory;
        private DropdownField mFrame;
        private int maneuverFrame;
        private bool bound;

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
        }

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
            Texture2D mapBackground = Resources.Load<Texture2D>("NavTablet/NavMapBackground");
            if (mapView != null && mapBackground != null)
            {
                mapView.style.backgroundImage = new StyleBackground(mapBackground);
            }

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
            fForecast.RegisterValueChangedCallback(e => { double t; if (TabletTime.TryParse(e.newValue, out t)) { State.SetForecast(t); } });
            fHistory.RegisterValueChangedCallback(e => { double t; if (TabletTime.TryParse(e.newValue, out t)) { State.SetHistory(t); } });

            mFrame = root.Q<DropdownField>("m-frame");
            mFrame.RegisterValueChangedCallback(e => { maneuverFrame = Mathf.Max(0, mFrame.index); State.Plan.SetFrameBody(maneuverFrame); });

            root.Q<Button>("add").clicked += () =>
            {
                State.Plan.Add(maneuverFrame, Math.Max(State.ForecastS / 10d, 600d));
                RebuildCards();
            };

            mapView.RegisterCallback<PointerDownEvent>(e => mapView.CapturePointer(e.pointerId));
            mapView.RegisterCallback<PointerUpEvent>(e => mapView.ReleasePointer(e.pointerId));
            mapView.RegisterCallback<PointerLeaveEvent>(e => { HideTip(); if (MapPointerLeft != null) { MapPointerLeft(); } });
            mapView.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (mapView.HasPointerCapture(e.pointerId))
                {
                    HideTip();
                    if (MapDragged != null) { MapDragged(e.deltaPosition, (e.pressedButtons & 2) != 0 || e.shiftKey); }
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
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Body list from the simulation (names as shown on screen). Call once after the universe is loaded.</summary>
        public void SetBodies(string[] names)
        {
            State.SetBodies(names);
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
        public void SetMapTexture(RenderTexture rt)
        {
            mapView.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
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
            Refresh();
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
            var dot = new VisualElement();
            dot.AddToClassList("dot");
            dot.EnableInClassList("dot--on", State.OrbitVisible[id]);
            dot.RegisterCallback<ClickEvent>(e =>
            {
                State.ToggleOrbit(id);
                if (OrbitToggled != null) { OrbitToggled(id); }
                RebuildFrames();
            });
            nm.Add(dot);
            nm.Add(new Label(name));
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

            return c;
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
                        plan.SetDv(idx, a, RateSlider.AdvanceLinear(c.M.GetDv(a), cd, 160d, dt, -ManeuverPlan.MaxDvPerAxis, ManeuverPlan.MaxDvPerAxis));
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
                c.Title.text = "МАНЁВР " + (i + 1).ToString(CultureInfo.InvariantCulture);
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
