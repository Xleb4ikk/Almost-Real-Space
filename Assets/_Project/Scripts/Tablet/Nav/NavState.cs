using System;

namespace Galilego.Tablet
{
    public enum NavPanel { Frames = 0, Time = 1, Plan = 2 }

    /// <summary>Slide animation of a collapsible panel: Open drives Progress 0 (collapsed strip) … 1 (fully open).</summary>
    public sealed class PanelAnim
    {
        public bool Open;
        public float Progress;

        /// <summary>Progress eased (smoothstep) for layout interpolation.</summary>
        public float Eased { get { return Progress * Progress * (3f - 2f * Progress); } }

        public void Tick(float dt, float secondsToFlip)
        {
            float step = secondsToFlip > 0f ? dt / secondsToFlip : 1f;
            Progress = Open ? Math.Min(1f, Progress + step) : Math.Max(0f, Progress - step);
        }
    }

    /// <summary>
    /// Everything the navigation tablet remembers between frames, with no Unity and no rendering:
    /// reference frame / map centre / orbit visibility / line lengths / maneuver plan / panel states.
    /// Bodies are 0…BodyCount−1; the ship is <see cref="ShipId"/> (never a reference frame, only a centre).
    /// </summary>
    public sealed class NavState
    {
        public const double MinForecastS = 60d;
        public const double MaxForecastS = 3.156e9d;   // ≈ 100 years
        public const double MaxHistoryS = 3.156e9d;
        public const double MinHistoryS = 60d;         // below this the history is switched off (0)
        public const double SlideSeconds = 0.3d;

        /// <summary>In-world calendar start. The game itself only counts seconds from its epoch.</summary>
        public DateTime WorldEpochUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

        public string[] BodyNames { get; private set; } = new string[0];
        public bool[] OrbitVisible { get; private set; } = new bool[1];

        public int FrameIndex;
        public int CenterIndex;
        public double ForecastS = 3d * 86400d;
        public double HistoryS = 86400d;

        public readonly RateSlider ForecastSlider = new RateSlider();
        public readonly RateSlider HistorySlider = new RateSlider();
        public readonly ManeuverPlan Plan = new ManeuverPlan();
        public readonly PanelAnim[] Panels = { new PanelAnim(), new PanelAnim { Open = true, Progress = 1f }, new PanelAnim { Open = true, Progress = 1f } };

        /// <summary>Bumped when line lengths change: the trajectory must be recomputed.</summary>
        public int CalcVersion { get; private set; }

        /// <summary>Bumped when only the view changes (frame, centre, visibility): lines are re-projected, not re-integrated.</summary>
        public int ViewVersion { get; private set; }

        public int BodyCount { get { return BodyNames.Length; } }
        public int ShipId { get { return BodyNames.Length; } }

        /// <summary>Re-reads the body list; keeps the selection when the indices are still valid.</summary>
        public void SetBodies(string[] names)
        {
            BodyNames = names ?? new string[0];
            bool[] old = OrbitVisible;
            OrbitVisible = new bool[BodyNames.Length + 1];
            for (int i = 0; i < OrbitVisible.Length; i++) { OrbitVisible[i] = i < old.Length ? old[i] : true; }
            if (FrameIndex >= BodyCount) { FrameIndex = 0; }
            if (CenterIndex > ShipId) { CenterIndex = 0; }
            ViewVersion++;
        }

        /// <summary>Only bodies can be a reference frame; returns false for the ship or an invalid index.</summary>
        public bool SetFrame(int i)
        {
            if (i < 0 || i >= BodyCount) { return false; }
            FrameIndex = i;
            ViewVersion++;
            return true;
        }

        public bool SetCenter(int i)
        {
            if (i < 0 || i > ShipId) { return false; }
            CenterIndex = i;
            ViewVersion++;
            return true;
        }

        public void ToggleOrbit(int i)
        {
            if (i < 0 || i >= OrbitVisible.Length) { return; }
            OrbitVisible[i] = !OrbitVisible[i];
            ViewVersion++;
        }

        public void SetForecast(double seconds)
        {
            ForecastS = Math.Max(MinForecastS, Math.Min(MaxForecastS, seconds));
            CalcVersion++;
        }

        /// <summary>0 switches the history off.</summary>
        public void SetHistory(double seconds)
        {
            HistoryS = seconds < MinHistoryS ? 0d : Math.Min(MaxHistoryS, seconds);
            CalcVersion++;
        }

        public void SetPanel(NavPanel p, bool open)
        {
            Panels[(int)p].Open = open;
        }

        public void TogglePanel(NavPanel p)
        {
            Panels[(int)p].Open = !Panels[(int)p].Open;
        }

        /// <summary>Once per frame: slider relaxation, length changes from the sliders, panel animations.</summary>
        public void Tick(float dt)
        {
            double cf = ForecastSlider.Tick(dt);
            if (cf != 0d)
            {
                double v = RateSlider.AdvanceLog(ForecastS, cf, 1.8d, dt, MinForecastS, MaxForecastS, false);
                if (v != ForecastS) { ForecastS = v; CalcVersion++; }
            }

            double ch = HistorySlider.Tick(dt);
            if (ch != 0d)
            {
                double v = RateSlider.AdvanceLog(HistoryS, ch, 1.8d, dt, MinHistoryS, MaxHistoryS, true);
                if (v != HistoryS) { HistoryS = v; CalcVersion++; }
            }

            for (int i = 0; i < Panels.Length; i++) { Panels[i].Tick(dt, (float)SlideSeconds); }
        }
    }
}
