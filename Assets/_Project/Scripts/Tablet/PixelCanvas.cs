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

        /// <summary>Bitmap from rows ('#' is a pixel), top-left corner at (x, y).</summary>
        public void Bitmap(int x, int y, string[] rows, Color32 c)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i] == '#')
                    {
                        Px(x + i, y + r, c);
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
