using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// Pixel-drawn navball: sky/ground grid, horizon, compass and pitch numbers,
    /// and six velocity markers. The sphere is seen "from the nose": the center is
    /// where the ship points. Markers behind the sphere (rear hemisphere) are pale but
    /// still clickable — clicking a marker points the ship at it (SasMode).
    /// </summary>
    public sealed class NavballRenderer
    {
        public const int Radius = 54;
        private const int GridStepDeg = 15;
        private const int HitRadius = 7;

        // Numbers printed on the ball. A label is fixed to a (heading, elevation)
        // pair and therefore turns with the graticule, like a real instrument ball.
        private const int CompassStepDeg = 30;
        private const int CompassTickStepDeg = 10;
        private const float CompassLabelElevationDeg = 6.5f;
        private const float LabelMinDepth = 0.45f;
        private static readonly int[] PitchMeridiansDeg = { 0, 90, 180, 270 };
        private static readonly int[] PitchLabelsDeg = { -60, -45, -30, -15, 15, 30, 45, 60 };
        private static readonly string[] PitchLabelNames = { "60", "45", "30", "15", "15", "30", "45", "60" };
        private static readonly string[] CompassNames =
        {
            "000", "030", "060", "090", "120", "150", "180", "210", "240", "270", "300", "330",
        };

        // 3×5 digits for the ball. The tablet's 5×7 font is too heavy on a
        // 108-pixel sphere; the small engraved digits read as instrument print.
        private static readonly string[][] BallDigits =
        {
            new[] { "###", "#.#", "#.#", "#.#", "###" },
            new[] { "..#", ".##", "..#", "..#", "###" },
            new[] { "###", "..#", "###", "#..", "###" },
            new[] { "###", "..#", "###", "..#", "###" },
            new[] { "#.#", "#.#", "###", "..#", "..#" },
            new[] { "###", "#..", "###", "..#", "###" },
            new[] { "###", "#..", "###", "#.#", "###" },
            new[] { "###", "..#", "..#", "..#", "..#" },
            new[] { "###", "#.#", "###", "#.#", "###" },
            new[] { "###", "#.#", "###", "..#", "###" },
        };

        private static readonly string[][] Icons =
        {
            // Prograde: circle, dot, three "whiskers"
            new[] { "....#....", "...###...", "..#...#..", ".#.....#.", "##..#..##", ".#.....#.", "..#...#..", "...###..." },
            // Retrograde: circle, cross, three "whiskers"
            new[] { "....#....", "...###...", "..#...#..", ".#.#.#.#.", "##..#..##", ".#.#.#.#.", "..#...#..", "...###..." },
            // Normal: triangle up
            new[] { ".........", ".........", "....#....", "...#.#...", "..#...#..", ".#.....#.", "#########" },
            // Antinormal: triangle down
            new[] { ".........", ".........", "#########", ".#.....#.", "..#...#..", "...#.#...", "....#...." },
            // Radial in: diamond with a cross
            new[] { "....#....", "...#.#...", "..#...#..", ".#.#.#.#.", "#...#...#", ".#.#.#.#.", "..#...#..", "...#.#...", "....#...." },
            // Radial out: diamond with a dot
            new[] { "....#....", "...#.#...", "..#...#..", ".#.....#.", "#...#...#", ".#.....#.", "..#...#..", "...#.#...", "....#...." },
        };

        public static readonly string[] MarkerNames =
        {
            "ПО СКОРОСТИ", "ПРОТИВ СКОРОСТИ", "НОРМАЛЬ", "АНТИНОРМАЛЬ", "РАДИАЛЬНО ВНУТРЬ", "РАДИАЛЬНО НАРУЖУ",
        };

        // Cache of the last draw (for cursor hit-testing).
        private readonly int[] markerX = new int[6];
        private readonly int[] markerY = new int[6];
        private readonly float[] markerZ = new float[6];
        private readonly bool[] markerShown = new bool[6];

        private const int Size = (Radius * 2) + 1;
        private readonly int[] elevCell = new int[Size * Size];
        private readonly int[] azCell = new int[Size * Size];
        private readonly float[] depth = new float[Size * Size];
        private readonly byte[] flags = new byte[Size * Size]; // 1 — inside the sphere, 2 — sky
        private readonly float[] elevDeg = new float[Size * Size];

        // Last drawn label positions (dedupe around the pole; at most 12 + 32).
        private readonly int[] labelX = new int[48];
        private readonly int[] labelY = new int[48];
        private int labelCount;

        /// <summary>Marker index under point (px, py) or -1. A front marker beats a rear one.</summary>
        public int HitTest(int px, int py)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < 6; i++)
            {
                if (!markerShown[i])
                {
                    continue;
                }

                int dx = px - markerX[i];
                int dy = py - markerY[i];
                int d2 = (dx * dx) + (dy * dy);
                if (d2 > HitRadius * HitRadius)
                {
                    continue;
                }

                float score = d2 - (markerZ[i] * 20f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Ship heading / pitch / roll (degrees) from orientation relative to the horizon.</summary>
        public static void Euler(Quaternion q, out float heading, out float pitch, out float roll)
        {
            Vector3 f = q * Vector3.forward;
            Vector3 r = q * Vector3.right;
            Vector3 u = q * Vector3.up;
            pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;

            // The chart frame is (east, up, north), so the compass heading runs
            // toward +X: 0 = north, 90 = east. With the nose near vertical the
            // heading is undefined; the ball's up axis (the craft roll reference)
            // gives a stable reading instead of amplifying float noise.
            Vector3 flat = new Vector3(f.x, 0f, f.z);
            if (flat.sqrMagnitude < 1e-4f)
            {
                flat = new Vector3(u.x, 0f, u.z);
            }

            heading = flat.sqrMagnitude > 1e-8f
                ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg
                : 0f;
            if (heading < 0f)
            {
                heading += 360f;
            }

            Vector3 hr = Vector3.Cross(Vector3.up, f);
            if (hr.sqrMagnitude < 1e-6f)
            {
                roll = 0f;
                return;
            }

            hr.Normalize();
            Vector3 hu = Vector3.Cross(f, hr);
            roll = Mathf.Atan2(-Vector3.Dot(r, hu), Vector3.Dot(u, hu)) * Mathf.Rad2Deg;
        }

        public void Draw(PixelCanvas c, int cx, int cy, TabletTelemetry t, int hoverMarker, int activeMarker)
        {
            Quaternion q = t.Attitude;
            Vector3 bx = q * Vector3.right;
            Vector3 by = q * Vector3.up;
            Vector3 bz = q * Vector3.forward;

            // Pass 1: for each sphere pixel — grid cells, sky/ground, depth.
            for (int j = 0; j < Size; j++)
            {
                for (int i = 0; i < Size; i++)
                {
                    int idx = (j * Size) + i;
                    float sx = (i - Radius) / (float)Radius;
                    float sy = -(j - Radius) / (float)Radius;
                    float d2 = (sx * sx) + (sy * sy);
                    if (d2 > 1f)
                    {
                        flags[idx] = 0;
                        continue;
                    }

                    float sz = Mathf.Sqrt(1f - d2);
                    float fx = (bx.x * sx) + (by.x * sy) + (bz.x * sz);
                    float fy = (bx.y * sx) + (by.y * sy) + (bz.y * sz);
                    float fz = (bx.z * sx) + (by.z * sy) + (bz.z * sz);
                    float elev = Mathf.Asin(Mathf.Clamp(fy, -1f, 1f)) * Mathf.Rad2Deg;
                    float az = Mathf.Atan2(fx, fz) * Mathf.Rad2Deg;
                    if (az < 0f)
                    {
                        az += 360f;
                    }

                    elevDeg[idx] = elev;
                    elevCell[idx] = Mathf.FloorToInt((elev + 90f) / GridStepDeg);
                    azCell[idx] = Mathf.FloorToInt(az / 30f) % 12;
                    depth[idx] = sz;
                    flags[idx] = (byte)(fy >= 0f ? 3 : 1);
                }
            }

            // Pass 2: coloring.
            for (int j = 0; j < Size; j++)
            {
                for (int i = 0; i < Size; i++)
                {
                    int idx = (j * Size) + i;
                    if (flags[idx] == 0)
                    {
                        continue;
                    }

                    bool sky = (flags[idx] & 2) != 0;
                    float z = depth[idx];
                    int level = Mathf.Clamp(Mathf.FloorToInt((z * 2.6f) + Bayer(i, j)), 0, 2);
                    Color32 col = sky ? TabletTheme.Sky[level] : TabletTheme.Ground[level];

                    int left = EdgeNeighbor(i - 1, j);
                    int up = EdgeNeighbor(i, j - 1);
                    int right = EdgeNeighbor(i + 1, j);
                    int down = EdgeNeighbor(i, j + 1);

                    bool horizon = (left >= 0 && SkyFlag(left) != sky)
                        || (up >= 0 && SkyFlag(up) != sky)
                        || (right >= 0 && SkyFlag(right) != sky)
                        || (down >= 0 && SkyFlag(down) != sky);
                    bool gridElev = (left >= 0 && elevCell[left] != elevCell[idx])
                        || (up >= 0 && elevCell[up] != elevCell[idx]);
                    bool gridAz = Mathf.Abs(elevDeg[idx]) < 72f
                        && ((left >= 0 && azCell[left] != azCell[idx]) || (up >= 0 && azCell[up] != azCell[idx]));

                    if (horizon)
                    {
                        col = TabletTheme.Horizon;
                    }
                    else if (gridElev || gridAz)
                    {
                        col = sky ? TabletTheme.GridSky : TabletTheme.GridGround;
                        if (level == 0)
                        {
                            col = sky ? TabletTheme.Sky[2] : TabletTheme.Ground[1];
                        }
                    }

                    c.Px(cx + i - Radius, cy + j - Radius, col);
                }
            }

            DrawRim(c, cx, cy);
            DrawCompass(c, cx, cy, q);
            DrawPitchLabels(c, cx, cy, q);
            DrawMarkers(c, cx, cy, t, hoverMarker, activeMarker);
            DrawBoresight(c, cx, cy);
        }

        private int EdgeNeighbor(int i, int j)
        {
            if (i < 0 || j < 0 || i >= Size || j >= Size)
            {
                return -1;
            }

            int idx = (j * Size) + i;
            return flags[idx] == 0 ? -1 : idx;
        }

        private bool SkyFlag(int idx)
        {
            return (flags[idx] & 2) != 0;
        }

        private static readonly float[] BayerMatrix =
        {
            0f, 8f, 2f, 10f, 12f, 4f, 14f, 6f, 3f, 11f, 1f, 9f, 15f, 7f, 13f, 5f,
        };

        private static float Bayer(int x, int y)
        {
            return (BayerMatrix[((y & 3) * 4) + (x & 3)] / 16f) - 0.5f;
        }

        private static void DrawRim(PixelCanvas c, int cx, int cy)
        {
            float rOuter = Radius + 0.5f;
            float rInner = Radius - 0.7f;
            for (int y = -Radius - 1; y <= Radius + 1; y++)
            {
                for (int x = -Radius - 1; x <= Radius + 1; x++)
                {
                    float d = Mathf.Sqrt((x * x) + (y * y));
                    if (d >= rInner && d <= rOuter)
                    {
                        c.Px(cx + x, cy + y, TabletTheme.Main);
                    }
                }
            }

            // Outer dashed ring — the instrument limb.
            float ring = Radius + 4f;
            for (int deg = 0; deg < 360; deg += 2)
            {
                float a = deg * Mathf.Deg2Rad;
                int px = cx + Mathf.RoundToInt(Mathf.Cos(a) * ring);
                int py = cy + Mathf.RoundToInt(Mathf.Sin(a) * ring);
                c.Px(px, py, (deg % 30) == 0 ? TabletTheme.Dim : TabletTheme.Line);
            }
        }

        /// <summary>
        /// Unit direction in the horizon chart for a compass heading and an
        /// elevation, degrees. 0 = north (+Z), 90 = east (+X), +90 = zenith (+Y).
        /// </summary>
        private static Vector3 ChartDirection(float headingDeg, float elevationDeg)
        {
            float h = headingDeg * Mathf.Deg2Rad;
            float e = elevationDeg * Mathf.Deg2Rad;
            float ce = Mathf.Cos(e);
            return new Vector3(Mathf.Sin(h) * ce, Mathf.Sin(e), Mathf.Cos(h) * ce);
        }

        /// <summary>
        /// Compass numbers every 30° along the horizon, on the sky side. Minor
        /// 10° ticks make the horizon read as a compass rose; the bearing is
        /// constant along a meridian, so the value is right away from the line.
        /// </summary>
        private void DrawCompass(PixelCanvas c, int cx, int cy, Quaternion q)
        {
            Quaternion inv = Quaternion.Inverse(q);
            labelCount = 0;
            DrawHorizonTicks(c, cx, cy, inv);
            for (int heading = 0; heading < 360; heading += CompassStepDeg)
            {
                Vector3 s = inv * ChartDirection(heading, CompassLabelElevationDeg);
                TryLabel(c, cx, cy, s, CompassNames[heading / CompassStepDeg], false, TabletTheme.Bright);
            }
        }

        /// <summary>Short degree ticks crossing the horizon every 10°.</summary>
        private static void DrawHorizonTicks(PixelCanvas c, int cx, int cy, Quaternion inv)
        {
            for (int heading = 0; heading < 360; heading += CompassTickStepDeg)
            {
                float reach = (heading % CompassStepDeg) == 0 ? 2.2f : 1.1f;
                Vector3 a = inv * ChartDirection(heading, reach);
                Vector3 b = inv * ChartDirection(heading, -reach);
                if (a.z < 0.25f || b.z < 0.25f)
                {
                    continue;
                }

                int x0 = cx + Mathf.RoundToInt(a.x * Radius);
                int y0 = cy - Mathf.RoundToInt(a.y * Radius);
                int x1 = cx + Mathf.RoundToInt(b.x * Radius);
                int y1 = cy - Mathf.RoundToInt(b.y * Radius);
                if (OutsideDisc(x0, y0, cx, cy) || OutsideDisc(x1, y1, cx, cy))
                {
                    continue;
                }

                c.Line(x0, y0, x1, y1,
                    (heading % CompassStepDeg) == 0 ? TabletTheme.LineHi : TabletTheme.Line);
            }
        }

        private static bool OutsideDisc(int x, int y, int cx, int cy)
        {
            int dx = x - cx;
            int dy = y - cy;
            int r = Radius - 1;
            return (dx * dx) + (dy * dy) > r * r;
        }

        /// <summary>
        /// Pitch ladder numbers every 15° on four meridians. The number is the
        /// elevation of that ring, so the ring labelled 30 crosses the centre
        /// exactly when the pitch readout says +30°. Unsigned: the horizon already
        /// shows which side of the ball the ring is on.
        /// </summary>
        private void DrawPitchLabels(PixelCanvas c, int cx, int cy, Quaternion q)
        {
            Quaternion inv = Quaternion.Inverse(q);
            for (int m = 0; m < PitchMeridiansDeg.Length; m++)
            {
                for (int k = 0; k < PitchLabelsDeg.Length; k++)
                {
                    int elev = PitchLabelsDeg[k];
                    Vector3 s = inv * ChartDirection(PitchMeridiansDeg[m], elev);
                    TryLabel(c, cx, cy, s, PitchLabelNames[k], true, TabletTheme.Main);
                }
            }
        }

        /// <summary>
        /// Draws one upright label at the projected point. Rejects rear-hemisphere
        /// points, labels that would spill over the limb, the one under the
        /// crosshair and duplicates (the meridians converge at the pole).
        /// </summary>
        private bool TryLabel(PixelCanvas c, int cx, int cy, Vector3 s, string text, bool avoidCenter, Color32 ink)
        {
            if (s.z < LabelMinDepth)
            {
                return false;
            }

            int w = BallTextWidth(text);
            int x = cx + Mathf.RoundToInt(s.x * Radius);
            int y = cy - Mathf.RoundToInt(s.y * Radius);
            if (Mathf.Abs(x - cx) + (w / 2) + 2 > Radius - 2
                || Mathf.Abs(y - cy) + 4 > Radius - 2)
            {
                return false;
            }

            if (avoidCenter && Mathf.Abs(x - cx) <= 12 && Mathf.Abs(y - cy) <= 8)
            {
                return false;
            }

            for (int i = 0; i < labelCount; i++)
            {
                if (Mathf.Abs(x - labelX[i]) < 11 && Mathf.Abs(y - labelY[i]) < 7)
                {
                    return false;
                }
            }

            if (labelCount >= labelX.Length)
            {
                return false;
            }

            labelX[labelCount] = x;
            labelY[labelCount] = y;
            labelCount++;
            DrawBallText(c, x - (w / 2), y - 2, text, ink, TabletTheme.Bg);
            return true;
        }

        private static int BallTextWidth(string s)
        {
            return (s.Length * 4) - 1;
        }

        /// <summary>3×5 digits with a one-pixel drop shadow (engraved instrument print).</summary>
        private static void DrawBallText(PixelCanvas c, int x, int y, string s, Color32 ink, Color32 shadow)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                Color32 col = pass == 0 ? shadow : ink;
                int ox = x + (pass == 0 ? 1 : 0);
                int oy = y + (pass == 0 ? 1 : 0);
                for (int i = 0; i < s.Length; i++)
                {
                    int digit = s[i] - '0';
                    if (digit < 0 || digit > 9)
                    {
                        continue;
                    }

                    string[] glyph = BallDigits[digit];
                    for (int r = 0; r < 5; r++)
                    {
                        string row = glyph[r];
                        for (int k = 0; k < 3; k++)
                        {
                            if (row[k] == '#')
                            {
                                c.Px(ox + (i * 4) + k, oy + r, col);
                            }
                        }
                    }
                }
            }
        }

        private void DrawMarkers(PixelCanvas c, int cx, int cy, TabletTelemetry t, int hover, int active)
        {
            Quaternion inv = Quaternion.Inverse(t.Attitude);
            for (int i = 0; i < 6; i++)
            {
                markerShown[i] = false;
                if (!t.MarkersValid)
                {
                    continue;
                }

                Vector3 s = inv * t.MarkerDir((NavMarker)i);
                int x = cx + Mathf.RoundToInt(s.x * Radius);
                int y = cy - Mathf.RoundToInt(s.y * Radius);
                markerX[i] = x;
                markerY[i] = y;
                markerZ[i] = s.z;
                markerShown[i] = true;
            }

            // Rear (pale) first, then front — front drawn on top.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (!markerShown[i] || (markerZ[i] >= 0f) != (pass == 1))
                    {
                        continue;
                    }

                    bool front = pass == 1;
                    bool isActive = i == active;
                    bool isHover = i == hover;
                    Color32 col = front ? TabletTheme.Main : TabletTheme.LineHi;
                    if (isActive || isHover)
                    {
                        col = TabletTheme.Bright;
                    }

                    string[] icon = Icons[i];
                    int w = icon[0].Length;
                    int ox = markerX[i] - (w / 2);
                    int oy = markerY[i] - 4;
                    // dark backing so the icon reads on the grid
                    for (int r = 0; r < icon.Length; r++)
                    {
                        for (int k = 0; k < icon[r].Length; k++)
                        {
                            if (icon[r][k] == '#')
                            {
                                for (int ddy = -1; ddy <= 1; ddy++)
                                {
                                    for (int ddx = -1; ddx <= 1; ddx++)
                                    {
                                        c.Px(ox + k + ddx, oy + r + ddy, TabletTheme.Bg);
                                    }
                                }
                            }
                        }
                    }

                    c.Bitmap(ox, oy, icon, col);
                    if (isActive || isHover)
                    {
                        DrawBrackets(c, markerX[i], markerY[i], isActive ? TabletTheme.Bright : TabletTheme.Main);
                    }
                }
            }
        }

        private static void DrawBrackets(PixelCanvas c, int x, int y, Color32 col)
        {
            const int h = 8;
            const int l = 3;
            c.HLine(x - h, y - h, l, col);
            c.VLine(x - h, y - h, l, col);
            c.HLine(x + h - l + 1, y - h, l, col);
            c.VLine(x + h, y - h, l, col);
            c.HLine(x - h, y + h, l, col);
            c.VLine(x - h, y + h - l + 1, l, col);
            c.HLine(x + h - l + 1, y + h, l, col);
            c.VLine(x + h, y + h - l + 1, l, col);
        }

        /// <summary>Fixed ship marker at the center: "wings" and a dot.</summary>
        private static void DrawBoresight(PixelCanvas c, int cx, int cy)
        {
            Color32 dark = TabletTheme.Bg;
            Color32 col = TabletTheme.Bright;
            for (int pass = 0; pass < 2; pass++)
            {
                Color32 k = pass == 0 ? dark : col;
                int o = pass == 0 ? 1 : 0;
                c.Fill(cx - 15 - o, cy - o, 10 + (2 * o), 1 + (2 * o), k);
                c.Fill(cx + 6 - o, cy - o, 10 + (2 * o), 1 + (2 * o), k);
                c.Fill(cx - 15 - o, cy - o, 1 + (2 * o), 4 + (2 * o), k);
                c.Fill(cx + 15 - o, cy - o, 1 + (2 * o), 4 + (2 * o), k);
                c.Fill(cx - 2 - o, cy - 2 - o, 5 + (2 * o), 5 + (2 * o), k);
            }

            c.Fill(cx - 1, cy - 1, 3, 3, TabletTheme.PanelBg);
            c.Px(cx, cy, col);
        }
    }
}
