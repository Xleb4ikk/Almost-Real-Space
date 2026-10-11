using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Рисунки деталей для инвентаря: маленькие «фотографии» рисуются кодом
    /// (фронтальный вид бака, кольцо отделителя сверху, сопло RS-25), поэтому
    /// не тянут ассеты и выглядят одинаково в любой теме. Текстуры кэшируются:
    /// статическая ссылка обязательна — иначе сборщик мусора съест нативную
    /// текстуру, а GUIStyle оставит висячий указатель (см. CheatGuiSkin).
    /// </summary>
    public static class PartIcons
    {
        private const int Size = 96;

        private static Texture2D engine;
        private static Texture2D tank;
        private static Texture2D separator;

        /// <summary>Рисунок детали (создаётся при первом обращении).</summary>
        public static Texture2D Get(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.Engine:
                    if (engine == null)
                    {
                        engine = DrawEngine();
                    }

                    return engine;
                case PartKind.Separator:
                    if (separator == null)
                    {
                        separator = DrawSeparator();
                    }

                    return separator;
                default:
                    if (tank == null)
                    {
                        tank = DrawTank();
                    }

                    return tank;
            }
        }

        // ─── Бак: цилиндр, вид спереди ───────────────────────────────────────

        private static Texture2D DrawTank()
        {
            var pixels = NewCanvas(new Color(0f, 0f, 0f, 0f));
            int left = 26;
            int right = 70;
            int top = 12;
            int bottom = 84;
            int centerX = (left + right) / 2;
            float halfWidth = (right - left) * 0.5f;

            // Обечайка: горизонтальный градиент металла (цилиндрическая тень).
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    float t = (x - centerX) / halfWidth;              // -1..1
                    float shade = 0.55f + (0.45f * Mathf.Cos(t * Mathf.PI * 0.5f));
                    var color = new Color(0.45f * shade, 0.52f * shade, 0.60f * shade);
                    Set(pixels, x, y, color);
                }
            }

            // Стыковые кольца (горизонтальные полосы) — металл читается как бак.
            for (int band = 1; band <= 3; band++)
            {
                int y = top + ((bottom - top) * band) / 4;
                for (int x = left; x <= right; x++)
                {
                    Set(pixels, x, y, new Color(0.30f, 0.35f, 0.41f));
                }
            }

            // Крышки: эллипсы сверху и снизу.
            DrawEllipse(pixels, centerX, top, halfWidth, 5f, new Color(0.35f, 0.40f, 0.47f));
            DrawEllipse(pixels, centerX, top - 1, halfWidth * 0.82f, 3.4f, new Color(0.50f, 0.56f, 0.63f));
            DrawEllipse(pixels, centerX, bottom, halfWidth, 5f, new Color(0.22f, 0.26f, 0.31f));
            Outline(pixels, left, top, right, bottom, new Color(0.14f, 0.17f, 0.21f));
            return Finish(pixels);
        }

        // ─── Отделитель: кольцо сверху ───────────────────────────────────────

        private static Texture2D DrawSeparator()
        {
            var pixels = NewCanvas(new Color(0f, 0f, 0f, 0f));
            int cx = Size / 2;
            int cy = Size / 2;
            float outer = 36f;
            float inner = 26f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    if (r > outer || r < inner)
                    {
                        continue;
                    }

                    float shade = 0.75f + (0.25f * (dy / outer)); // верх чуть светлее
                    Set(pixels, x, y, new Color(0.13f * shade, 0.14f * shade, 0.16f * shade));
                }
            }

            // Болты накладок по кольцу.
            for (int i = 0; i < 12; i++)
            {
                float a = (2f * Mathf.PI * i) / 12f;
                int bx = cx + Mathf.RoundToInt(Mathf.Cos(a) * 31f);
                int by = cy + Mathf.RoundToInt(Mathf.Sin(a) * 31f);
                Set(pixels, bx, by, new Color(0.55f, 0.58f, 0.64f));
                Set(pixels, bx + 1, by, new Color(0.38f, 0.41f, 0.46f));
            }

            // Кромки кольца.
            for (int angle = 0; angle < 360; angle++)
            {
                float a = angle * Mathf.Deg2Rad;
                int ox = cx + Mathf.RoundToInt(Mathf.Cos(a) * outer);
                int oy = cy + Mathf.RoundToInt(Mathf.Sin(a) * outer);
                int ix = cx + Mathf.RoundToInt(Mathf.Cos(a) * inner);
                int iy = cy + Mathf.RoundToInt(Mathf.Sin(a) * inner);
                Set(pixels, ox, oy, new Color(0.68f, 0.72f, 0.78f));
                Set(pixels, ix, iy, new Color(0.30f, 0.33f, 0.38f));
            }

            return Finish(pixels);
        }

        // ─── Двигатель: сопло RS-25, вид спереди ─────────────────────────────

        private static Texture2D DrawEngine()
        {
            var pixels = NewCanvas(new Color(0f, 0f, 0f, 0f));
            int top = 10;
            int bottom = 86;
            int centerX = Size / 2;

            // Колокол: ширина растёт от камеры (верх) к срезу (низ).
            for (int y = top; y <= bottom; y++)
            {
                float v = (y - top) / (float)(bottom - top);          // 0..1
                int halfWidth = Mathf.RoundToInt(Mathf.Lerp(9f, 30f, Mathf.Pow(v, 0.72f)));
                for (int x = centerX - halfWidth; x <= centerX + halfWidth; x++)
                {
                    float t = (x - centerX) / (float)halfWidth;
                    float shade = 0.62f + (0.38f * Mathf.Cos(t * Mathf.PI * 0.5f));
                    var color = new Color(0.78f * shade, 0.76f * shade, 0.74f * shade);
                    Set(pixels, x, y, color);
                }
            }

            // Турбонасосный блок и камера сверху.
            for (int y = 2; y < top + 6; y++)
            {
                for (int x = centerX - 8; x <= centerX + 8; x++)
                {
                    Set(pixels, x, y, new Color(0.55f, 0.57f, 0.60f));
                }
            }

            // Красный поясок RS-25 у среза и тёмные рёбра.
            for (int x = centerX - 27; x <= centerX + 27; x++)
            {
                Set(pixels, x, bottom - 10, new Color(0.75f, 0.16f, 0.12f));
                Set(pixels, x, bottom - 9, new Color(0.60f, 0.12f, 0.10f));
            }

            for (int rib = 0; rib < 3; rib++)
            {
                float v = 0.35f + (rib * 0.2f);
                int y = top + Mathf.RoundToInt((bottom - top) * v);
                int halfWidth = Mathf.RoundToInt(Mathf.Lerp(9f, 30f, Mathf.Pow(v, 0.72f)));
                for (int x = centerX - halfWidth; x <= centerX + halfWidth; x++)
                {
                    if (((x + Size) % 6) == 0)
                    {
                        Set(pixels, x, y, new Color(0.42f, 0.40f, 0.38f));
                    }
                }
            }

            // Тёмный срез сопла.
            for (int x = centerX - 28; x <= centerX + 28; x++)
            {
                Set(pixels, x, bottom + 1, new Color(0.16f, 0.15f, 0.14f));
            }

            return Finish(pixels);
        }

        // ─── Примитивы ───────────────────────────────────────────────────────

        private static Color[] NewCanvas(Color fill)
        {
            var pixels = new Color[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = fill;
            }

            return pixels;
        }

        private static void Set(Color[] pixels, int x, int y, Color color)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size)
            {
                return;
            }

            int index = ((Size - 1 - y) * Size) + x;
            pixels[index] = color;
        }

        private static void DrawEllipse(Color[] pixels, int cx, int cy, float rx, float ry, Color color)
        {
            for (int y = Mathf.FloorToInt(cy - ry); y <= Mathf.CeilToInt(cy + ry); y++)
            {
                for (int x = Mathf.FloorToInt(cx - rx); x <= Mathf.CeilToInt(cx + rx); x++)
                {
                    float nx = (x - cx) / rx;
                    float ny = (y - cy) / ry;
                    if ((nx * nx) + (ny * ny) <= 1f)
                    {
                        Set(pixels, x, y, color);
                    }
                }
            }
        }

        private static void Outline(Color[] pixels, int left, int top, int right, int bottom, Color color)
        {
            for (int x = left; x <= right; x++)
            {
                Set(pixels, x, top, color);
                Set(pixels, x, bottom, color);
            }

            for (int y = top; y <= bottom; y++)
            {
                Set(pixels, left, y, color);
                Set(pixels, right, y, color);
            }
        }

        private static Texture2D Finish(Color[] pixels)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
