using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// Навбол, нарисованный попиксельно: сетка небо/земля, горизонт, стороны
    /// света и шесть маркеров скорости. Шар виден «со стороны носа»: центр —
    /// куда смотрит корабль. Маркеры за шаром (задняя полусфера) бледные, но
    /// тоже нажимаются — клик по маркеру = навести корабль (SasMode).
    /// </summary>
    public sealed class NavballRenderer
    {
        public const int Radius = 54;
        private const int GridStepDeg = 15;
        private const int HitRadius = 7;

        private static readonly string[][] Icons =
        {
            // Прогрейд: круг, точка, три «уса»
            new[] { "....#....", "...###...", "..#...#..", ".#.....#.", "##..#..##", ".#.....#.", "..#...#..", "...###..." },
            // Ретроград: круг, крест, три «уса»
            new[] { "....#....", "...###...", "..#...#..", ".#.#.#.#.", "##..#..##", ".#.#.#.#.", "..#...#..", "...###..." },
            // Нормаль: треугольник вверх
            new[] { ".........", ".........", "....#....", "...#.#...", "..#...#..", ".#.....#.", "#########" },
            // Антинормаль: треугольник вниз
            new[] { ".........", ".........", "#########", ".#.....#.", "..#...#..", "...#.#...", "....#...." },
            // Радиально внутрь: ромб с крестом
            new[] { "....#....", "...#.#...", "..#...#..", ".#.#.#.#.", "#...#...#", ".#.#.#.#.", "..#...#..", "...#.#...", "....#...." },
            // Радиально наружу: ромб с точкой
            new[] { "....#....", "...#.#...", "..#...#..", ".#.....#.", "#...#...#", ".#.....#.", "..#...#..", "...#.#...", "....#...." },
        };

        public static readonly string[] MarkerNames =
        {
            "ПО СКОРОСТИ", "ПРОТИВ СКОРОСТИ", "НОРМАЛЬ", "АНТИНОРМАЛЬ", "РАДИАЛЬНО ВНУТРЬ", "РАДИАЛЬНО НАРУЖУ",
        };

        // Кэш прошлой отрисовки (для попадания курсором).
        private readonly int[] markerX = new int[6];
        private readonly int[] markerY = new int[6];
        private readonly float[] markerZ = new float[6];
        private readonly bool[] markerShown = new bool[6];

        private const int Size = (Radius * 2) + 1;
        private readonly int[] elevCell = new int[Size * Size];
        private readonly int[] azCell = new int[Size * Size];
        private readonly float[] depth = new float[Size * Size];
        private readonly byte[] flags = new byte[Size * Size]; // 1 — внутри шара, 2 — небо
        private readonly float[] elevDeg = new float[Size * Size];

        /// <summary>Индекс маркера под точкой (px, py) или -1. Передний маркер важнее заднего.</summary>
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

        /// <summary>Курс / тангаж / крен корабля (градусы) из ориентации относительно горизонта.</summary>
        public static void Euler(Quaternion q, out float heading, out float pitch, out float roll)
        {
            Vector3 f = q * Vector3.forward;
            Vector3 r = q * Vector3.right;
            Vector3 u = q * Vector3.up;
            pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
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

            // Проход 1: для каждого пикселя шара — ячейки сетки, небо/земля, глубина.
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

            // Проход 2: раскраска.
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
            DrawCardinals(c, cx, cy, q);
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

            // Внешнее пунктирное кольцо — «лимб» прибора.
            float ring = Radius + 4f;
            for (int deg = 0; deg < 360; deg += 2)
            {
                float a = deg * Mathf.Deg2Rad;
                int px = cx + Mathf.RoundToInt(Mathf.Cos(a) * ring);
                int py = cy + Mathf.RoundToInt(Mathf.Sin(a) * ring);
                c.Px(px, py, (deg % 30) == 0 ? TabletTheme.Dim : TabletTheme.Line);
            }
        }

        private static readonly Vector3[] CardinalDirs =
        {
            new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(-1f, 0f, 0f),
        };

        private static readonly string[] CardinalNames = { "\u0421", "\u0412", "\u042E", "\u0417" };

        private static void DrawCardinals(PixelCanvas c, int cx, int cy, Quaternion q)
        {
            Quaternion inv = Quaternion.Inverse(q);
            for (int i = 0; i < 4; i++)
            {
                Vector3 s = inv * CardinalDirs[i];
                if (s.z < 0.35f)
                {
                    continue;
                }

                int x = cx + Mathf.RoundToInt(s.x * Radius);
                int y = cy - Mathf.RoundToInt(s.y * Radius);
                c.Fill(x - 4, y - 5, 9, 11, TabletTheme.Sky[0]);
                c.TextCenter(x, y - 3, CardinalNames[i], TabletTheme.Bright);
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

            // Сначала задние (бледные), потом передние — передние поверх.
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
                    // тёмная подложка, чтобы значок читался на сетке
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

        /// <summary>Неподвижная метка корабля в центре: «крылья» и точка.</summary>
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
