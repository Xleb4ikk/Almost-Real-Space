using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// Software framebuffer for the tablet: pixels are drawn straight into a
    /// Color32 array (Y is down, rows are stored bottom-up in the array — as
    /// Texture2D expects, so the array uploads without reordering).
    /// No fonts, assets or UI packages: pixel art comes out exact.
    /// </summary>
    public sealed class PixelCanvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new Color32[width * height];
        }

        public void Clear(Color32 c)
        {
            for (int i = 0; i < Pixels.Length; i++)
            {
                Pixels[i] = c;
            }
        }

        public void Px(int x, int y, Color32 c)
        {
            if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            {
                Pixels[((Height - 1 - y) * Width) + x] = c;
            }
        }

        public Color32 Get(int x, int y)
        {
            if ((uint)x < (uint)Width && (uint)y < (uint)Height)
            {
                return Pixels[((Height - 1 - y) * Width) + x];
            }

            return new Color32(0, 0, 0, 255);
        }

        /// <summary>Blend one pixel over what is already there (soft fills, glows, shadows).</summary>
        public void PxBlend(int x, int y, Color32 c, float alpha)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || !(alpha > 0f))
            {
                return;
            }

            int i = ((Height - 1 - y) * Width) + x;
            Color32 d = Pixels[i];
            float a = alpha > 1f ? 1f : alpha;
            Pixels[i] = new Color32(
                (byte)(d.r + ((c.r - d.r) * a) + 0.5f),
                (byte)(d.g + ((c.g - d.g) * a) + 0.5f),
                (byte)(d.b + ((c.b - d.b) * a) + 0.5f),
                (byte)Mathf.Max(d.a, c.a));
        }

        /// <summary>Alpha-blended rectangle.</summary>
        public void FillBlend(int x, int y, int w, int h, Color32 c, float alpha)
        {
            for (int yy = y; yy < y + h; yy++)
            {
                for (int xx = x; xx < x + w; xx++)
                {
                    PxBlend(xx, yy, c, alpha);
                }
            }
        }

        public void Fill(int x, int y, int w, int h, Color32 c)
        {
            int x0 = Mathf.Max(0, x);
            int y0 = Mathf.Max(0, y);
            int x1 = Mathf.Min(Width, x + w);
            int y1 = Mathf.Min(Height, y + h);
            for (int yy = y0; yy < y1; yy++)
            {
                int row = (Height - 1 - yy) * Width;
                for (int xx = x0; xx < x1; xx++)
                {
                    Pixels[row + xx] = c;
                }
            }
        }

        /// <summary>Checkerboard fill (pseudo halftone).</summary>
        public void FillChecker(int x, int y, int w, int h, Color32 a, Color32 b)
        {
            for (int yy = y; yy < y + h; yy++)
            {
                for (int xx = x; xx < x + w; xx++)
                {
                    Px(xx, yy, ((xx + yy) & 1) == 0 ? a : b);
                }
            }
        }

        public void HLine(int x, int y, int w, Color32 c)
        {
            Fill(x, y, w, 1, c);
        }

        public void VLine(int x, int y, int h, Color32 c)
        {
            Fill(x, y, 1, h, c);
        }

        public void Frame(int x, int y, int w, int h, Color32 c)
        {
            HLine(x, y, w, c);
            HLine(x, y + h - 1, w, c);
            VLine(x, y, h, c);
            VLine(x + w - 1, y, h, c);
        }

        public void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = -Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Px(x0, y0, c);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        /// <summary>
        /// Anti-aliased thick segment. Coverage comes from the exact distance from
        /// the pixel centre to the segment (a capsule), so the edge is a soft ramp
        /// instead of a Bresenham staircase. Drawing a polyline as a chain of short
        /// pieces works: the capsules tessellate, so a dense chain reads as one
        /// stroke with a single brightness and no beads at the vertices.
        /// </summary>
        public void StrokeAA(Vector2 a, Vector2 b, Color32 color, float width, float alpha = 1f)
        {
            if (!(width > 0f) || !(alpha > 0f))
            {
                return;
            }

            float radius = width * 0.5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius - 1f));
            int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius + 1f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius - 1f));
            int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius + 1f));
            if (x0 > x1 || y0 > y1)
            {
                return;
            }

            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float lengthSquared = (dx * dx) + (dy * dy);
            float inverseLengthSquared = lengthSquared > 1e-8f ? 1f / lengthSquared : 0f;
            float edge = radius + 0.5f;

            for (int y = y0; y <= y1; y++)
            {
                float py = y + 0.5f;
                int row = (Height - 1 - y) * Width;
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f;
                    float t = inverseLengthSquared > 0f
                        ? (((px - a.x) * dx) + ((py - a.y) * dy)) * inverseLengthSquared
                        : 0f;
                    t = t < 0f ? 0f : (t > 1f ? 1f : t);
                    float qx = (a.x + (dx * t)) - px;
                    float qy = (a.y + (dy * t)) - py;
                    float coverage = edge - Mathf.Sqrt((qx * qx) + (qy * qy));
                    if (coverage <= 0f)
                    {
                        continue;
                    }

                    float amount = (coverage > 1f ? 1f : coverage) * alpha;
                    Color32 destination = Pixels[row + x];
                    Pixels[row + x] = new Color32(
                        (byte)(destination.r + ((color.r - destination.r) * amount) + 0.5f),
                        (byte)(destination.g + ((color.g - destination.g) * amount) + 0.5f),
                        (byte)(destination.b + ((color.b - destination.b) * amount) + 0.5f),
                        255);
                }
            }
        }

        /// <summary>Bitmap from rows ('#' is a pixel), top-left corner at (x, y). Scale makes chunky pixels.</summary>
        public void Bitmap(int x, int y, string[] rows, Color32 c, int scale = 1)
        {
            int s = Mathf.Max(1, scale);
            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i] != '#')
                    {
                        continue;
                    }

                    if (s == 1)
                    {
                        Px(x + i, y + r, c);
                    }
                    else
                    {
                        Fill(x + (i * s), y + (r * s), s, s, c);
                    }
                }
            }
        }

        // ── Text ───────────────────────────────────────────────────────

        public static int TextWidth(string s, int scale = 1)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }

            int w = 0;
            for (int i = 0; i < s.Length; i++)
            {
                w += (PixelFont.Get(s[i]).Width + 1) * scale;
            }

            return w - scale;
        }

        /// <summary>Draws a string, (x, y) is the top of the left edge. Returns the right edge.</summary>
        public int Text(int x, int y, string s, Color32 c, int scale = 1)
        {
            if (string.IsNullOrEmpty(s))
            {
                return x;
            }

            int cx = x;
            for (int i = 0; i < s.Length; i++)
            {
                PixelFont.Glyph g = PixelFont.Get(s[i]);
                for (int bit = 0; bit < 64; bit++)
                {
                    if ((g.Bits & (1UL << bit)) == 0UL)
                    {
                        continue;
                    }

                    int gx = bit & 7;
                    int gy = bit >> 3;
                    if (scale == 1)
                    {
                        Px(cx + gx, y + gy, c);
                    }
                    else
                    {
                        Fill(cx + (gx * scale), y + (gy * scale), scale, scale, c);
                    }
                }

                cx += (g.Width + 1) * scale;
            }

            return cx - scale;
        }

        /// <summary>
        /// Text with a 1 px dark outline. Labels that sit on the globe need this:
        /// a filled black plate around every name looks heavy, an outline does not.
        /// </summary>
        public int TextHalo(int x, int y, string s, Color32 c, Color32 halo, int scale = 1)
        {
            if (string.IsNullOrEmpty(s))
            {
                return x;
            }

            int step = Mathf.Max(1, scale);
            Text(x - step, y, s, halo, scale);
            Text(x + step, y, s, halo, scale);
            Text(x, y - step, s, halo, scale);
            Text(x, y + step, s, halo, scale);
            Text(x - step, y - step, s, halo, scale);
            Text(x + step, y - step, s, halo, scale);
            Text(x - step, y + step, s, halo, scale);
            Text(x + step, y + step, s, halo, scale);
            return Text(x, y, s, c, scale);
        }

        public static Color32 Mix(Color32 a, Color32 b, float t)
        {
            float k = t < 0f ? 0f : (t > 1f ? 1f : t);
            return new Color32(
                (byte)(a.r + ((b.r - a.r) * k) + 0.5f),
                (byte)(a.g + ((b.g - a.g) * k) + 0.5f),
                (byte)(a.b + ((b.b - a.b) * k) + 0.5f),
                255);
        }

        public void TextRight(int xRight, int y, string s, Color32 c, int scale = 1)
        {
            Text(xRight - TextWidth(s, scale), y, s, c, scale);
        }

        public void TextCenter(int xCenter, int y, string s, Color32 c, int scale = 1)
        {
            Text(xCenter - (TextWidth(s, scale) / 2), y, s, c, scale);
        }
    }
}
