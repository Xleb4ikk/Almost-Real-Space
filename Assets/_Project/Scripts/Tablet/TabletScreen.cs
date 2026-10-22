using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// Tablet screen 512×320: grouped layout (speed, altitude, pressure, attitude,
    /// orbit, ΔV) and buttons. "Immediate" mode: every Render redraws everything and
    /// processes a click right away if there was one.
    /// interactive = false — the ship tablet only shows data; hover and clicks are ignored.
    /// </summary>
    public sealed class TabletScreen
    {
        public const int W = 512;
        public const int H = 320;

        public readonly PixelCanvas Canvas = new PixelCanvas(W, H);

        // Local display settings (the game does not need them).
        public AltitudeMode AltMode = AltitudeMode.SeaLevel;
        private int pinnedStage = -1;
        private bool notesOpen;

        private readonly NavballRenderer navball = new NavballRenderer();
        private bool pointerValid;
        private int px;
        private int py;
        private bool clickPending;
        private bool dragPending;

        /// <summary>The pointer came from a world ray on the monitor — the panel draws a cursor arrow.</summary>
        private bool worldPointer;

        /// <summary>
        /// Draw the panel's own cursor arrow. On while the crosshair aims (the system
        /// cursor is captured and invisible then); off while the system cursor is free
        /// (screen focus) — otherwise there would be two pointers.
        /// </summary>
        public bool DrawArrow = true;

        private PixelCanvas c;
        private bool interactive;
        private ITabletCommands cmd;

        /// <summary>Cursor coordinates in tablet screen pixels (valid = false when off-screen).</summary>
        public void SetPointer(bool valid, int x, int y)
        {
            pointerValid = valid;
            px = x;
            py = y;
        }

        /// <summary>Current pointer pixel (diagnostics).</summary>
        public int PointerX { get { return px; } }

        /// <summary>Current pointer pixel (diagnostics).</summary>
        public int PointerY { get { return py; } }

        /// <summary>Pointer validity (diagnostics).</summary>
        public bool PointerValid { get { return pointerValid; } }

        /// <summary>Left button press; processed on the next Render.</summary>
        public void Click()
        {
            clickPending = true;
        }

        /// <summary>Drag with the button held (throttle slider); processed on the next Render.</summary>
        public void Drag()
        {
            dragPending = true;
        }

        /// <summary>
        /// World-ray pointer (the player aims at the monitor from the world): same panel
        /// pixels, but the panel draws the cursor arrow itself — with it you press buttons.
        /// </summary>
        public void SetWorldPointer(bool valid, int x, int y)
        {
            pointerValid = valid;
            px = x;
            py = y;
            worldPointer = valid;
        }

        public void Render(TabletTelemetry t, ITabletCommands commands, bool isInteractive)
        {
            c = Canvas;
            cmd = commands;
            interactive = isInteractive;
            if (!interactive)
            {
                if (clickPending)
                {
                    Debug.Log("[Click] сброшен: панель не интерактивна (pointerValid=" + pointerValid
                        + " px=" + px + " py=" + py + ")");
                }

                clickPending = false;
                dragPending = false;
            }

            c.Clear(TabletTheme.Bg);
            DrawSpeed(t, 4, 4, 164, 100);
            DrawAltitude(t, 4, 108, 164, 44);
            DrawPressure(t, 4, 156, 164, 34);
            DrawNav(t, 172, 4, 168, 186);
            DrawOrbit(t, 4, 194, 164, 122);
            DrawControl(t, 172, 194, 168, 122);
            DrawDeltaV(t, 344, 4, 164, 312);
            if (interactive && worldPointer && DrawArrow)
            {
                DrawPointer(px, py);
            }

            worldPointer = false;
            clickPending = false;
            dragPending = false;
        }

        /// <summary>The panel's cursor arrow (world click): it is drawn wherever you aim.</summary>
        private static readonly string[] PointerShape =
        {
            "#....",
            "##...",
            "###..",
            "####.",
            "#####",
            "####.",
            "###..",
        };

        private void DrawPointer(int x, int y)
        {
            // Dark outline — the arrow is visible against both digits and bright buttons.
            c.Bitmap(x - 1, y, PointerShape, TabletTheme.Bg);
            c.Bitmap(x + 1, y, PointerShape, TabletTheme.Bg);
            c.Bitmap(x, y - 1, PointerShape, TabletTheme.Bg);
            c.Bitmap(x, y + 1, PointerShape, TabletTheme.Bg);
            c.Bitmap(x, y, PointerShape, TabletTheme.Bright);
        }

        // ── Layout primitives ────────────────────────────────────────────

        private bool Over(int x, int y, int w, int h)
        {
            return interactive && pointerValid && px >= x && py >= y && px < x + w && py < y + h;
        }

        private bool Clicked(int x, int y, int w, int h)
        {
            bool hit = clickPending && Over(x, y, w, h);
            if (hit)
            {
                Debug.Log("[Click] кнопка " + x + "," + y + " " + w + "x" + h
                    + " (px=" + px + " py=" + py + ")");
            }

            return hit;
        }

        /// <summary>Panel with beveled corners and a title in a gap in the frame.</summary>
        private void Panel(int x, int y, int w, int h, string title)
        {
            c.Fill(x + 1, y + 1, w - 2, h - 2, TabletTheme.PanelBg);
            Color32 l = TabletTheme.Line;
            c.HLine(x + 3, y, w - 6, l);
            c.HLine(x + 3, y + h - 1, w - 6, l);
            c.VLine(x, y + 3, h - 6, l);
            c.VLine(x + w - 1, y + 3, h - 6, l);
            c.Px(x + 1, y + 2, l);
            c.Px(x + 2, y + 1, l);
            c.Px(x + w - 2, y + 2, l);
            c.Px(x + w - 3, y + 1, l);
            c.Px(x + 1, y + h - 3, l);
            c.Px(x + 2, y + h - 2, l);
            c.Px(x + w - 2, y + h - 3, l);
            c.Px(x + w - 3, y + h - 2, l);
            if (!string.IsNullOrEmpty(title))
            {
                int tw = PixelCanvas.TextWidth(title);
                c.Fill(x + 6, y - 1, tw + 6, 3, TabletTheme.Bg);
                c.Text(x + 9, y - 3, title, TabletTheme.Main);
            }
        }

        private void Row(int x, int y, int w, string label, string value, Color32 valueColor)
        {
            c.Text(x, y, label, TabletTheme.Dim);
            c.TextRight(x + w, y, value, valueColor);
        }

        private void Row(int x, int y, int w, string label, string value)
        {
            Row(x, y, w, label, value, TabletTheme.Main);
        }

        private void Separator(int x, int y, int w)
        {
            for (int i = 0; i < w; i += 2)
            {
                c.Px(x + i, y, TabletTheme.Line);
            }
        }

        /// <summary>Framed button; returns true on the frame of the click.</summary>
        private bool Button(int x, int y, int w, int h, string label, bool on, bool enabled)
        {
            bool hover = enabled && Over(x, y, w, h);
            Color32 bg = on ? TabletTheme.ButtonOn : (hover ? TabletTheme.ButtonHover : TabletTheme.PanelBg);
            Color32 border = on ? TabletTheme.Main : (hover ? TabletTheme.LineHi : TabletTheme.Line);
            Color32 text = !enabled ? TabletTheme.Line : (on || hover ? TabletTheme.Bright : TabletTheme.Main);
            c.Fill(x, y, w, h, bg);
            c.Frame(x, y, w, h, border);
            c.TextCenter(x + (w / 2), y + ((h - 7) / 2), label, text);
            return enabled && Clicked(x, y, w, h);
        }

        // ── Speed ────────────────────────────────────────────────────────

        private void DrawSpeed(TabletTelemetry t, int x, int y, int w, int h)
        {
            Panel(x, y, w, h, "СКОРОСТЬ");
            int ix = x + 7;
            int iw = w - 14;

            // Reference body and frame.
            string body = t.ReferenceBodies != null && t.ReferenceBodies.Length > 0
                ? t.ReferenceBodies[Mathf.Clamp(t.ReferenceBodyIndex, 0, t.ReferenceBodies.Length - 1)]
                : "—";
            int bodies = t.ReferenceBodies == null ? 0 : t.ReferenceBodies.Length;
            bool multi = bodies > 1;
            if (Button(ix, y + 8, 88, 14, multi ? body + " \u25B8" : body, false, multi))
            {
                cmd.SetReferenceBody((t.ReferenceBodyIndex + 1) % bodies);
            }

            bool surface = t.Frame == SpeedFrame.Surface;
            if (Button(ix + 92, y + 8, iw - 92, 14, surface ? "ПОВЕРХ." : "ОРБИТА", true, true))
            {
                cmd.SetSpeedFrame(surface ? SpeedFrame.Orbit : SpeedFrame.Surface);
            }

            // Main number.
            string unit = "М/С";
            int unitW = PixelCanvas.TextWidth(unit);
            c.TextRight(ix + iw, y + 36, unit, TabletTheme.Dim);
            c.TextRight(ix + iw - unitW - 4, y + 30, TabletFormat.Speed(t.SpeedMs), TabletTheme.Bright, 2);

            string arrow = t.VerticalSpeedMs >= 0d ? "\u25B2" : "\u25BC";
            string vert = TabletFormat.Num(t.VerticalSpeedMs, 1) + " М/С";
            c.Text(ix, y + 52, "ВЕРТ.", TabletTheme.Dim);
            c.TextRight(ix + iw, y + 52, vert, TabletTheme.Main);
            c.TextRight(ix + iw - PixelCanvas.TextWidth(vert) - 4, y + 52, arrow, TabletTheme.Bright);

            Separator(ix, y + 64, iw);
            Row(ix, y + 69, iw, "УСКОР.", TabletFormat.Num(t.AccelerationMs2, 2) + " М/С\u00B2");
            Row(ix, y + 79, iw, "ПЕРЕГР.", TabletFormat.Num(t.GForce, 2) + " G");
            Row(ix, y + 89, iw, "ГРАВ.", TabletFormat.Num(t.GravityG, 3) + " G");
        }

        // ── Altitude ─────────────────────────────────────────────────────

        private void DrawAltitude(TabletTelemetry t, int x, int y, int w, int h)
        {
            bool sea = AltMode == AltitudeMode.SeaLevel;
            bool hover = Over(x + 2, y + 2, w - 4, h - 4);
            Panel(x, y, w, h, "ВЫСОТА");
            if (hover)
            {
                c.Frame(x + 2, y + 2, w - 4, h - 4, TabletTheme.LineHi);
            }

            if (Clicked(x + 2, y + 2, w - 4, h - 4))
            {
                AltMode = sea ? AltitudeMode.Surface : AltitudeMode.SeaLevel;
                sea = !sea;
            }

            int ix = x + 8;
            int iw = w - 16;
            c.Text(ix, y + 9, sea ? "ОТ УРОВНЯ МОРЯ" : "ОТ ПОВЕРХНОСТИ", TabletTheme.Dim);
            c.TextRight(ix + iw, y + 9, "\u2022", sea ? TabletTheme.Main : TabletTheme.Line);
            c.TextRight(ix + iw - 8, y + 9, "\u2022", sea ? TabletTheme.Line : TabletTheme.Main);
            string v = TabletFormat.Dist(sea ? t.AltitudeSeaLevelM : t.AltitudeSurfaceM);
            c.TextRight(ix + iw, y + 22, v, TabletTheme.Bright, 2);
        }

        // ── Pressure ─────────────────────────────────────────────────────

        private void DrawPressure(TabletTelemetry t, int x, int y, int w, int h)
        {
            Panel(x, y, w, h, "ДАВЛЕНИЕ");
            int ix = x + 8;
            int iw = w - 16;
            Row(ix, y + 9, iw, "СНАРУЖИ", TabletFormat.Pressure(t.PressurePa), TabletTheme.Bright);
            Row(ix, y + 20, iw, "В АТМ", TabletFormat.Atm(t.PressurePa));
        }

        // ── Attitude (navball) ───────────────────────────────────────────

        private static int ActiveMarker(TabletTelemetry t)
        {
            if (!t.SasEnabled)
            {
                return -1;
            }

            switch (t.SasMode)
            {
                case SasMode.Prograde: return 0;
                case SasMode.Retrograde: return 1;
                case SasMode.Normal: return 2;
                case SasMode.Antinormal: return 3;
                case SasMode.RadialIn: return 4;
                case SasMode.RadialOut: return 5;
                default: return -1;
            }
        }

        private static SasMode ModeOf(int marker)
        {
            return (SasMode)(marker + 1); // Prograde=1 … RadialOut=6
        }

        private void DrawNav(TabletTelemetry t, int x, int y, int w, int h)
        {
            Panel(x, y, w, h, "ОРИЕНТАЦИЯ");
            int cx = x + (w / 2);
            int cy = y + 12 + NavballRenderer.Radius + 5;

            int hover = interactive && pointerValid ? navball.HitTest(px, py) : -1;
            int active = ActiveMarker(t);
            if (t.ControlsAvailable && hover >= 0 && clickPending)
            {
                if (hover == active)
                {
                    cmd.SetSasMode(SasMode.Stability);
                }
                else
                {
                    cmd.SetSasMode(ModeOf(hover));
                    if (!t.SasEnabled)
                    {
                        cmd.SetSasEnabled(true);
                    }
                }
            }

            navball.Draw(c, cx, cy, t, hover, active);

            // Caption: what is under the cursor / what SAS is holding.
            string caption;
            Color32 captionColor = TabletTheme.Dim;
            if (hover >= 0)
            {
                caption = NavballRenderer.MarkerNames[hover];
                captionColor = TabletTheme.Bright;
            }
            else if (t.SasEnabled)
            {
                caption = "САС: " + SasName(t.SasMode);
                captionColor = TabletTheme.Main;
            }
            else
            {
                caption = "САС ВЫКЛЮЧЕНА";
            }

            c.TextCenter(cx, y + 12 + (2 * NavballRenderer.Radius) + 15, caption, captionColor);

            float heading;
            float pitch;
            float roll;
            NavballRenderer.Euler(t.Attitude, out heading, out pitch, out roll);
            string att = "КУРС " + Mathf.RoundToInt(heading).ToString("000")
                + " ТАНГ " + TabletFormat.Signed(Mathf.RoundToInt(pitch), 0)
                + " КРЕН " + TabletFormat.Signed(Mathf.RoundToInt(roll), 0);
            c.TextCenter(cx, y + 12 + (2 * NavballRenderer.Radius) + 26, att, TabletTheme.Main);

            // SAS / RCS.
            int by = y + h - 24;
            if (ToggleButton(x + 7, by, 74, 17, "САС", t.SasEnabled, t.ControlsAvailable))
            {
                bool on = !t.SasEnabled;
                if (on)
                {
                    // The monitor's button always means attitude hold, not a marker mode.
                    cmd.SetSasMode(SasMode.Stability);
                }

                cmd.SetSasEnabled(on);
            }

            if (ToggleButton(x + w - 81, by, 74, 17, "РСУ", t.RcsEnabled, t.ControlsAvailable))
            {
                cmd.SetRcsEnabled(!t.RcsEnabled);
            }
        }

        private bool ToggleButton(int x, int y, int w, int h, string label, bool on, bool enabled = true,
            string onText = null, string offText = null)
        {
            bool hover = enabled && Over(x, y, w, h);
            Color32 bg = on ? TabletTheme.ButtonOn : (hover ? TabletTheme.ButtonHover : TabletTheme.PanelBg);
            Color32 border = !enabled ? TabletTheme.Line : (on ? TabletTheme.Main : (hover ? TabletTheme.LineHi : TabletTheme.Line));
            c.Fill(x, y, w, h, bg);
            c.Frame(x, y, w, h, border);
            int ly = y + ((h - 7) / 2);
            c.Fill(x + 5, y + ((h - 5) / 2), 5, 5, !enabled ? TabletTheme.Line : (on ? TabletTheme.Bright : TabletTheme.Line));
            c.Text(x + 14, ly, label, !enabled ? TabletTheme.Line : (on || hover ? TabletTheme.Bright : TabletTheme.Main));
            c.TextRight(x + w - 5, ly, on ? (onText ?? "ВКЛ") : (offText ?? "ВЫКЛ"),
                !enabled ? TabletTheme.Line : (on ? TabletTheme.Main : TabletTheme.Dim));
            return enabled && Clicked(x, y, w, h);
        }

        private static string SasName(SasMode m)
        {
            switch (m)
            {
                case SasMode.Prograde: return "ПО СКОРОСТИ";
                case SasMode.Retrograde: return "ПРОТИВ СКОРОСТИ";
                case SasMode.Normal: return "НОРМАЛЬ";
                case SasMode.Antinormal: return "АНТИНОРМАЛЬ";
                case SasMode.RadialIn: return "РАДИАЛЬНО ВНУТРЬ";
                case SasMode.RadialOut: return "РАДИАЛЬНО НАРУЖУ";
                default: return "СТАБИЛИЗАЦИЯ";
            }
        }

        // ── Orbit (narrow left column) ───────────────────────────────────

        private void DrawOrbit(TabletTelemetry t, int x, int y, int w, int h)
        {
            Panel(x, y, w, h, "ОРБИТА");
            TabletOrbit o = t.Orbit;
            int ix = x + 8;
            int iw = w - 16;
            bool ap = o.Valid && o.HasApoapsis;

            DrawApsisRow(ix, y + 8, iw, "АПОЦЕНТР", ap, o.ApAltitudeM);
            Row(ix, y + 25, iw, "ДО АП", ap ? TabletFormat.Time(o.ApTimeS) : TabletFormat.Dash);
            Row(ix, y + 35, iw, "СКОРОСТЬ", ap ? TabletFormat.Num(o.ApSpeedMs, 1) + " М/С" : TabletFormat.Dash);
            Separator(ix, y + 46, iw);

            DrawApsisRow(ix, y + 50, iw, "ПЕРИЦЕНТР", o.Valid, o.PeAltitudeM);
            Row(ix, y + 67, iw, "ДО ПР", o.Valid ? TabletFormat.Time(o.PeTimeS) : TabletFormat.Dash);
            Row(ix, y + 77, iw, "СКОРОСТЬ", o.Valid ? TabletFormat.Num(o.PeSpeedMs, 1) + " М/С" : TabletFormat.Dash);
            Separator(ix, y + 88, iw);

            Row(ix, y + 92, iw, "ПЕРИОД", ap ? TabletFormat.Time(o.PeriodS) : TabletFormat.Dash);
            Row(ix, y + 102, iw, "ЭКСЦЕНТРИСИТЕТ", o.Valid ? TabletFormat.Num(o.Eccentricity, 4) : TabletFormat.Dash);
            Row(ix, y + 112, iw, "НАКЛОНЕНИЕ", o.Valid ? TabletFormat.Num(o.InclinationDeg, 2) + "\u00B0" : TabletFormat.Dash);
        }

        /// <summary>Apsis row: label and a large value on one line (like "speed/m/s").</summary>
        private void DrawApsisRow(int x, int y, int w, string caption, bool valid, double alt)
        {
            c.Text(x, y + 4, caption, TabletTheme.Dim);
            c.TextRight(x + w, y, valid ? TabletFormat.Dist(alt) : TabletFormat.Dash, TabletTheme.Bright, 2);
        }

        // ── Control (below attitude) ─────────────────────────────────────

        private void DrawControl(TabletTelemetry t, int x, int y, int w, int h)
        {
            Panel(x, y, w, h, "УПРАВЛЕНИЕ");
            int ix = x + 8;
            int iw = w - 16;

            DrawThrottle(t, ix, y + 9, iw, t.ControlsAvailable);

            Separator(ix, y + 60, iw);

            if (ToggleButton(ix, y + 64, iw, 17, "ДВИГАТЕЛЬ", t.EngineOn, t.EngineAvailable && t.ControlsAvailable))
            {
                cmd.SetEnginesEnabled(!t.EngineOn);
            }

            string stage = t.StageIgnited ? "ОТДЕЛИТЬ СТУПЕНЬ" : "ЗАЖЕЧЬ СТУПЕНЬ";
            if (Button(ix, y + 83, iw, 17, stage, false, t.CanStage && t.ControlsAvailable))
            {
                cmd.RequestStage();
            }

            if (ToggleButton(ix, y + 102, iw, 17, "ЛЮК", t.HatchOpen, t.HatchAvailable,
                    "ОТКРЫТ", "ЗАКРЫТ"))
            {
                cmd.SetHatchOpen(!t.HatchOpen);
            }
        }

        /// <summary>
        /// Throttle 0-100 %: large value and a bar gauge. A click or drag on the bar sets
        /// the throttle right at the point under the cursor; quarter ticks are for aiming.
        /// </summary>
        private void DrawThrottle(TabletTelemetry t, int x, int y, int w, bool enabled)
        {
            float frac = enabled ? Mathf.Clamp01((float)t.Throttle01) : 0f;

            c.Text(x, y + 4, "ТЯГА", TabletTheme.Dim);
            int unitW = PixelCanvas.TextWidth("%");
            c.TextRight(x + w, y + 4, "%", TabletTheme.Dim);
            c.TextRight(x + w - unitW - 4, y, TabletFormat.Num(frac * 100f, 0), TabletTheme.Bright, 2);

            int by = y + 21;
            int bh = 16;
            bool hover = enabled && Over(x, by, w, bh);

            c.Fill(x, by, w, bh, TabletTheme.Bg);
            for (int i = 1; i < 4; i++)
            {
                c.VLine(x + 1 + (((w - 2) * i) / 4), by + 1, bh - 2, TabletTheme.Line);
            }

            int fill = Mathf.RoundToInt(frac * (w - 2));
            if (fill > 0)
            {
                c.Fill(x + 1, by + 1, fill, bh - 2, TabletTheme.ButtonOn);
                c.HLine(x + 1, by + 1, fill, TabletTheme.LineHi);
            }

            if (hover && pointerValid)
            {
                c.VLine(Mathf.Clamp(px, x + 1, x + w - 2), by + 1, bh - 2, TabletTheme.LineHi);
            }

            c.Frame(x, by, w, bh, hover ? TabletTheme.LineHi : TabletTheme.Line);
            c.VLine(Mathf.Min(x + 1 + fill, x + w - 2), by + 1, bh - 2, TabletTheme.Bright);

            int sy = by + bh + 3;
            c.Text(x, sy, "0", TabletTheme.Line);
            c.TextCenter(x + (w / 2), sy, "50", TabletTheme.Line);
            c.TextRight(x + w, sy, "100", TabletTheme.Line);

            if (enabled && (Clicked(x, by, w, bh) || (dragPending && hover)))
            {
                cmd.SetThrottle(Mathf.Clamp01((px - (x + 1)) / (float)(w - 2)));
            }
        }

        // ── ΔV ───────────────────────────────────────────────────────────

        private void DrawDeltaV(TabletTelemetry t, int x, int y, int w, int h)
        {
            Panel(x, y, w, h, "\u0394V \u00B7 " + (t.DvEnvLabel ?? string.Empty));
            int ix = x + 8;
            int iw = w - 16;

            string unit = "М/С";
            c.TextRight(ix + iw, y + 22, unit, TabletTheme.Dim);
            c.TextRight(ix + iw - PixelCanvas.TextWidth(unit) - 4, y + 16, TabletFormat.Num(t.TotalDeltaVMs, 0), TabletTheme.Bright, 2);
            c.Text(ix, y + 9, t.InFlight ? "ОСТАЛОСЬ" : "ВСЕГО", TabletTheme.Dim);

            Row(ix, y + 36, iw, "ПЛАН", TabletFormat.Num(t.PlanDeltaVMs, 0) + " М/С");
            Row(ix, y + 46, iw, "ВРЕМЯ РАБОТЫ", TabletFormat.Time(t.TotalBurnTimeS));
            Row(ix, y + 56, iw, "МАССА", TabletFormat.Mass(t.CurrentMassKg));
            Row(ix, y + 66, iw, "TWR", TabletFormat.Num(t.Twr, 2));
            Row(ix, y + 76, iw, "ТЯГА", TabletFormat.Num(t.ThrustN / 1000d, 1) + " КН");
            Separator(ix, y + 88, iw);

            int zoneTop = y + 94;
            int noteCount = t.Notes == null ? 0 : t.Notes.Length;
            int zoneBottom = y + h - 8 - (noteCount > 0 ? 14 : 0);

            if (noteCount > 0)
            {
                int ny = y + h - 20;
                bool hover = Over(ix, ny, iw, 13);
                c.Fill(ix, ny, iw, 13, hover ? TabletTheme.ButtonHover : TabletTheme.PanelBg);
                c.Frame(ix, ny, iw, 13, TabletTheme.WarnDim);
                c.TextCenter(ix + (iw / 2), ny + 3, (notesOpen ? "СКРЫТЬ ПРИМЕЧАНИЯ" : "ПРИМЕЧАНИЙ: " + noteCount), TabletTheme.Warn);
                if (Clicked(ix, ny, iw, 13))
                {
                    notesOpen = !notesOpen;
                }
            }
            else
            {
                notesOpen = false;
            }

            if (notesOpen)
            {
                DrawNotes(t, ix, zoneTop, iw, zoneBottom - zoneTop);
                return;
            }

            DrawStages(t, ix, zoneTop, iw, zoneBottom - zoneTop);
        }

        private void DrawNotes(TabletTelemetry t, int x, int y, int w, int h)
        {
            int ly = y;
            int maxChars = w / 6;
            for (int i = 0; i < t.Notes.Length && ly + 8 <= y + h; i++)
            {
                string[] lines = Wrap(t.Notes[i], maxChars - 2);
                for (int k = 0; k < lines.Length && ly + 8 <= y + h; k++)
                {
                    c.Text(x + (k == 0 ? 0 : 6), ly, lines[k], TabletTheme.Warn);
                    ly += 9;
                }

                ly += 4;
            }
        }

        private void DrawStages(TabletTelemetry t, int x, int y, int w, int h)
        {
            TabletStage[] stages = t.Stages;
            int n = stages == null ? 0 : stages.Length;
            if (n == 0)
            {
                c.Text(x, y + 2, "НЕТ СТУПЕНЕЙ", TabletTheme.Dim);
                return;
            }

            const int railW = 38;
            int railX = x + w - railW;
            const int gap = 4;
            const int minH = 26;

            double total = 0d;
            for (int i = 0; i < n; i++)
            {
                total += System.Math.Max(0d, stages[i].PlanDeltaVMs);
            }

            int usable = h - ((n - 1) * gap);
            int extra = Mathf.Max(0, usable - (n * minH));
            var tops = new int[n];
            var heights = new int[n];
            int cursor = y + h;
            for (int i = 0; i < n; i++)
            {
                double share = total > 0d ? System.Math.Max(0d, stages[i].PlanDeltaVMs) / total : 1d / n;
                heights[i] = Mathf.Max(18, Mathf.Min(minH, usable / n) + Mathf.RoundToInt((float)(extra * share)));
                cursor -= heights[i];
                tops[i] = cursor;
                cursor -= gap;
            }

            int hovered = -1;
            for (int i = 0; i < n; i++)
            {
                if (Over(railX, tops[i], railW, heights[i]))
                {
                    hovered = i;
                }

                if (Clicked(railX, tops[i], railW, heights[i]))
                {
                    pinnedStage = pinnedStage == i ? -1 : i;
                }
            }

            int focus = hovered >= 0 ? hovered : (pinnedStage >= 0 && pinnedStage < n ? pinnedStage : ActiveStage(stages));

            for (int i = 0; i < n; i++)
            {
                DrawStageBar(stages[i], railX, tops[i], railW, heights[i], i == focus, i == hovered, t.InFlight);
            }

            if (focus >= 0)
            {
                // Link "bar → card".
                int midY = tops[focus] + (heights[focus] / 2);
                for (int lx = railX - 4; lx < railX; lx += 2)
                {
                    c.Px(lx, midY, TabletTheme.Main);
                }

                DrawStageCard(stages[focus], x, y, w - railW - 6, h, t.InFlight);
            }
        }

        private static int ActiveStage(TabletStage[] stages)
        {
            for (int i = 0; i < stages.Length; i++)
            {
                if (stages[i].IsActive)
                {
                    return i;
                }
            }

            for (int i = 0; i < stages.Length; i++)
            {
                if (!stages[i].IsSpent && !stages[i].IsJettisoned)
                {
                    return i;
                }
            }

            return stages.Length - 1;
        }

        private void DrawStageBar(TabletStage s, int x, int y, int w, int h, bool focus, bool hovered, bool flight)
        {
            bool dead = s.IsSpent || s.IsJettisoned;
            Color32 border = focus ? TabletTheme.Bright : (s.IsActive ? TabletTheme.Main : (dead ? TabletTheme.Line : TabletTheme.Dim));
            c.Fill(x, y, w, h, TabletTheme.PanelBg);

            double plan = System.Math.Max(1d, s.PlanDeltaVMs);
            double frac = flight ? Mathf.Clamp01((float)(s.DeltaVMs / plan)) : 1f;
            int fill = Mathf.RoundToInt((float)(frac * (h - 2)));
            if (fill > 0)
            {
                Color32 fa = dead ? TabletTheme.Line : (s.HasEngine ? TabletTheme.ButtonOn : TabletTheme.PanelBg);
                Color32 fb = dead ? TabletTheme.PanelBg : (s.HasEngine ? TabletTheme.Line : TabletTheme.PanelBg);
                c.FillChecker(x + 1, y + h - 1 - fill, w - 2, fill, fa, fb);
            }

            c.Frame(x, y, w, h, border);
            if (s.IsActive)
            {
                c.Frame(x + 1, y + 1, w - 2, h - 2, TabletTheme.Dim);
            }

            Color32 tc = dead ? TabletTheme.Dim : (focus ? TabletTheme.Bright : TabletTheme.Main);
            c.Fill(x + 3, y + 3, PixelCanvas.TextWidth((s.Index + 1).ToString()) + 2, 9, TabletTheme.PanelBg);
            c.Text(x + 4, y + 4, (s.Index + 1).ToString(), tc);
            if (h >= 26)
            {
                string dv = TabletFormat.Num(flight ? s.DeltaVMs : s.PlanDeltaVMs, 0);
                c.Fill(x + 3, y + h - 12, PixelCanvas.TextWidth(dv) + 2, 9, TabletTheme.PanelBg);
                c.Text(x + 4, y + h - 11, dv, tc);
            }
        }

        private void DrawStageCard(TabletStage s, int x, int y, int w, int h, bool flight)
        {
            string state = s.IsJettisoned ? "СБРОШЕНА" : (s.IsSpent ? "ВЫРАБОТАНА" : (s.IsActive ? "АКТИВНА" : "ОЖИДАЕТ"));
            Color32 stateColor = s.IsActive ? TabletTheme.Bright : TabletTheme.Dim;
            c.Text(x, y, "СТУПЕНЬ " + (s.Index + 1), TabletTheme.Bright);
            c.Text(x, y + 10, state, stateColor);

            int ry = y + 24;
            const int step = 10;
            bool burns = s.HasEngine;
            string none = TabletFormat.Dash;

            c.Text(x, ry, TabletFormat.Clip(burns ? s.EngineLabel : "БЕЗ ДВИГАТЕЛЯ", w), burns ? TabletTheme.Main : TabletTheme.Dim);
            ry += step + 2;

            Row(x, ry, w, flight ? "ΔV ОСТ." : "ΔV", burns ? TabletFormat.Num(flight ? s.DeltaVMs : s.PlanDeltaVMs, 0) + " М/С" : none);
            ry += step;
            Row(x, ry, w, flight ? "РАБОТАТЬ" : "ВРЕМЯ", burns ? TabletFormat.Time(flight ? s.BurnTimeS : s.PlanBurnTimeS) : none);
            ry += step;
            Row(x, ry, w, "ТЯГА", burns ? TabletFormat.Num(s.ThrustN / 1000d, 1) + " КН" : none);
            ry += step;
            Row(x, ry, w, "TWR", burns ? TabletFormat.Num(s.TwrStart, 2) + ">" + TabletFormat.Num(s.TwrEnd, 2) : none);
            ry += step;
            Row(x, ry, w, "УД.ИМП.", burns ? TabletFormat.Num(s.IspS, 1) + " С" : none);
            ry += step + 2;

            c.Text(x, ry, TabletFormat.Clip(string.IsNullOrEmpty(s.PropellantLabel) ? "ТОПЛИВО" : s.PropellantLabel, w), TabletTheme.Dim);
            ry += step;
            double fuel = flight ? s.PropellantRemainingKg : s.PropellantMassKg;
            Row(x, ry, w, "ТОПЛИВО", TabletFormat.Mass(fuel));
            ry += step;
            Row(x, ry, w, "СУХАЯ", TabletFormat.Mass(s.DryMassKg));
            ry += step;
            Row(x, ry, w, "МАССА", TabletFormat.Mass(s.StartMassKg));
            ry += step;
            Row(x, ry, w, "ПОСЛЕ", TabletFormat.Mass(s.EndMassKg));
            ry += step + 2;

            if (s.Warnings != null)
            {
                for (int i = 0; i < s.Warnings.Length && ry + 8 <= y + h; i++)
                {
                    string[] lines = Wrap(s.Warnings[i], w / 6);
                    for (int k = 0; k < lines.Length && ry + 8 <= y + h; k++)
                    {
                        c.Text(x, ry, lines[k], TabletTheme.Warn);
                        ry += 9;
                    }
                }
            }
        }

        /// <summary>Word wrap (the font renders the text in uppercase).</summary>
        public static string[] Wrap(string text, int maxChars)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return lines.ToArray();
            }

            string[] words = text.Split(' ');
            string line = string.Empty;
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (line.Length == 0)
                {
                    line = w;
                }
                else if (line.Length + 1 + w.Length <= maxChars)
                {
                    line += " " + w;
                }
                else
                {
                    lines.Add(line);
                    line = w;
                }
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }
    }
}
