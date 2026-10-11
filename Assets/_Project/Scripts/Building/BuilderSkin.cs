using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Скин интерфейса строительства. Всё рисуется кодом, без ассетов:
    /// скруглённые прямоугольники по SDF (9-slice), мягкая тень/свечение,
    /// шевроны-стрелки и крестик. Не зависит от CheatGuiSkin, поэтому чит-меню
    /// и стройка оформляются независимо.
    ///
    /// Текстуры лежат в статических полях (иначе сборщик мусора съест нативные
    /// объекты, а GUIStyle останется с висячим указателем), а Build() заново
    /// собирает всё, если Unity уничтожил текстуру (выключенный domain reload).
    /// </summary>
    internal static class BuilderSkin
    {
        // ─── Палитра ─────────────────────────────────────────────────────────
        public static readonly Color Accent = new Color(0.45f, 0.80f, 1f, 1f);
        public static readonly Color AccentDim = new Color(0.45f, 0.80f, 1f, 0.35f);
        public static readonly Color TextMain = new Color(0.90f, 0.93f, 0.97f, 1f);
        public static readonly Color TextMuted = new Color(0.55f, 0.63f, 0.75f, 1f);
        public static readonly Color Line = new Color(0.55f, 0.68f, 0.86f, 0.14f);
        public static readonly Color Good = new Color(0.38f, 0.92f, 0.62f, 1f);

        // ─── Стили рамок (GUI.Box) ───────────────────────────────────────────
        public static GUIStyle Window { get; private set; }
        public static GUIStyle PanelInner { get; private set; }
        public static GUIStyle Plate { get; private set; }
        public static GUIStyle Slot { get; private set; }
        public static GUIStyle SlotHover { get; private set; }
        public static GUIStyle SlotSelected { get; private set; }
        public static GUIStyle Card { get; private set; }
        public static GUIStyle CardHover { get; private set; }
        public static GUIStyle CardSelected { get; private set; }
        public static GUIStyle Step { get; private set; }
        public static GUIStyle StepHover { get; private set; }
        public static GUIStyle StepActive { get; private set; }
        public static GUIStyle StepDisabled { get; private set; }
        public static GUIStyle ValueBox { get; private set; }
        public static GUIStyle Key { get; private set; }
        public static GUIStyle Pill { get; private set; }
        public static GUIStyle Danger { get; private set; }
        public static GUIStyle DangerHover { get; private set; }
        public static GUIStyle Track { get; private set; }
        public static GUIStyle Glow { get; private set; }

        // ─── Текстовые стили ─────────────────────────────────────────────────
        public static GUIStyle TitleText { get; private set; }
        public static GUIStyle NameText { get; private set; }
        public static GUIStyle Heading { get; private set; }
        public static GUIStyle Body { get; private set; }
        public static GUIStyle BodyRight { get; private set; }
        public static GUIStyle Muted { get; private set; }
        public static GUIStyle MutedRight { get; private set; }
        public static GUIStyle Tiny { get; private set; }
        public static GUIStyle TinyRight { get; private set; }
        public static GUIStyle TinyCenter { get; private set; }
        public static GUIStyle CardName { get; private set; }
        public static GUIStyle CardKind { get; private set; }
        public static GUIStyle ValueText { get; private set; }
        public static GUIStyle KeyText { get; private set; }
        public static GUIStyle PillText { get; private set; }
        public static GUIStyle ButtonText { get; private set; }

        // ─── Значки ──────────────────────────────────────────────────────────
        public static Texture2D ChevronLeft { get; private set; }
        public static Texture2D ChevronRight { get; private set; }
        public static Texture2D DoubleLeft { get; private set; }
        public static Texture2D DoubleRight { get; private set; }
        public static Texture2D Cross { get; private set; }
        public static Texture2D Dot { get; private set; }

        private static bool built;
        private static Texture2D guard;     // по ней узнаём, что текстуры уничтожены

        // Держим ссылки на все текстуры, чтобы их не собрал GC.
        private static readonly System.Collections.Generic.List<Texture2D> Keep =
            new System.Collections.Generic.List<Texture2D>();

        public static void Build()
        {
            if (built && guard != null)
            {
                return;
            }

            built = false;
            Keep.Clear();

            // Окна и панели.
            Window = Box(Rounded(48, 16f, C(0.043f, 0.056f, 0.082f, 0.97f), C(0.21f, 0.28f, 0.40f, 1f), 1.5f), 20);
            PanelInner = Box(Rounded(32, 10f, C(0.072f, 0.091f, 0.129f, 1f), C(0.13f, 0.18f, 0.26f, 1f), 1.2f), 12);
            Plate = Box(Rounded(40, 14f, C(0.03f, 0.04f, 0.06f, 0.80f), C(0.17f, 0.23f, 0.33f, 0.95f), 1.2f), 16);
            Pill = Box(Rounded(40, 14f, C(0.03f, 0.04f, 0.06f, 0.90f), C(0.25f, 0.34f, 0.48f, 0.95f), 1.2f), 16);

            // Слоты хотбара и карточки.
            Slot = Box(Rounded(32, 9f, C(0.040f, 0.052f, 0.075f, 1f), C(0.16f, 0.21f, 0.30f, 1f), 1.2f), 11);
            SlotHover = Box(Rounded(32, 9f, C(0.07f, 0.095f, 0.14f, 1f), C(0.30f, 0.42f, 0.60f, 1f), 1.5f), 11);
            SlotSelected = Box(Rounded(32, 9f, C(0.07f, 0.11f, 0.17f, 1f), C(0.45f, 0.80f, 1f, 1f), 2.6f), 11);
            Card = Box(Rounded(32, 10f, C(0.098f, 0.122f, 0.172f, 1f), C(0.17f, 0.22f, 0.31f, 1f), 1.2f), 12);
            CardHover = Box(Rounded(32, 10f, C(0.125f, 0.157f, 0.218f, 1f), C(0.28f, 0.38f, 0.54f, 1f), 1.4f), 12);
            CardSelected = Box(Rounded(32, 10f, C(0.105f, 0.165f, 0.245f, 1f), C(0.45f, 0.80f, 1f, 1f), 2.2f), 12);

            // Кнопки-стрелки.
            Step = Box(Rounded(32, 9f, C(0.15f, 0.19f, 0.27f, 1f), C(0.26f, 0.34f, 0.47f, 1f), 1.2f), 11);
            StepHover = Box(Rounded(32, 9f, C(0.22f, 0.30f, 0.43f, 1f), C(0.40f, 0.55f, 0.75f, 1f), 1.4f), 11);
            StepActive = Box(Rounded(32, 9f, C(0.28f, 0.52f, 0.82f, 1f), C(0.55f, 0.80f, 1f, 1f), 1.4f), 11);
            StepDisabled = Box(Rounded(32, 9f, C(0.075f, 0.095f, 0.135f, 1f), C(0.12f, 0.15f, 0.21f, 1f), 1.2f), 11);
            ValueBox = Box(Rounded(32, 9f, C(0.036f, 0.048f, 0.070f, 1f), C(0.17f, 0.24f, 0.35f, 1f), 1.2f), 11);
            Key = Box(Rounded(24, 6f, C(0.13f, 0.16f, 0.22f, 1f), C(0.30f, 0.38f, 0.50f, 1f), 1.2f), 8);
            Danger = Box(Rounded(32, 9f, C(0.24f, 0.09f, 0.10f, 1f), C(0.50f, 0.20f, 0.22f, 1f), 1.2f), 11);
            DangerHover = Box(Rounded(32, 9f, C(0.38f, 0.12f, 0.13f, 1f), C(0.82f, 0.31f, 0.33f, 1f), 1.4f), 11);
            Track = Box(Rounded(16, 5f, C(0.05f, 0.07f, 0.10f, 1f), C(0.13f, 0.18f, 0.26f, 1f), 1f), 6);
            Glow = Box(MakeGlow(64, 20f, 12f), 30);

            // Текст.
            TitleText = Text(20, TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            NameText = Text(17, TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            Heading = Text(12, Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            Body = Text(14, TextMain, TextAnchor.MiddleLeft, FontStyle.Normal);
            BodyRight = Text(14, TextMain, TextAnchor.MiddleRight, FontStyle.Bold);
            Muted = Text(14, TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal);
            MutedRight = Text(14, TextMuted, TextAnchor.MiddleRight, FontStyle.Normal);
            Tiny = Text(11, TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal);
            TinyRight = Text(11, TextMuted, TextAnchor.MiddleRight, FontStyle.Normal);
            TinyCenter = Text(11, TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal);
            CardName = Text(15, TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            CardKind = Text(12, TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal);
            ValueText = Text(18, TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            KeyText = Text(12, new Color(0.86f, 0.91f, 0.97f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            PillText = Text(14, TextMain, TextAnchor.MiddleCenter, FontStyle.Normal);
            PillText.richText = true;
            ButtonText = Text(14, TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Значки.
            ChevronLeft = Chevron(false, false);
            ChevronRight = Chevron(true, false);
            DoubleLeft = Chevron(false, true);
            DoubleRight = Chevron(true, true);
            Cross = MakeCross();
            Dot = MakeDot();

            guard = Dot;
            built = true;
        }

        // ─── Фабрики стилей ──────────────────────────────────────────────────

        private static Color C(float r, float g, float b, float a)
        {
            return new Color(r, g, b, a);
        }

        private static GUIStyle Box(Texture2D texture, int border)
        {
            var style = new GUIStyle
            {
                border = new RectOffset(border, border, border, border),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            style.normal.background = texture;
            style.hover.background = texture;
            style.active.background = texture;
            style.onNormal.background = texture;
            style.onHover.background = texture;
            style.onActive.background = texture;
            return style;
        }

        private static GUIStyle Text(int size, Color color, TextAnchor anchor, FontStyle fontStyle)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = anchor,
                wordWrap = false,
                richText = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            return style;
        }

        // ─── Текстуры ────────────────────────────────────────────────────────

        private static Texture2D Make(int size, Color[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels(pixels);
            texture.Apply();
            Keep.Add(texture);
            return texture;
        }

        /// <summary>Скруглённый прямоугольник с кромкой, готовый к 9-slice.</summary>
        private static Texture2D Rounded(int size, float radius, Color fill, Color edge, float edgeWidth)
        {
            float half = size * 0.5f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = RoundedBox(dx, dy, half, half, radius);
                    float coverage = Mathf.Clamp01(0.5f - d);
                    float inner = Mathf.Clamp01(0.5f - (d + edgeWidth));
                    Color color = Color.Lerp(edge, fill, inner);
                    color.a *= coverage;
                    pixels[(y * size) + x] = color;
                }
            }

            return Make(size, pixels);
        }

        /// <summary>
        /// Мягкая тень/свечение: внутри «коробки» альфа 1, снаружи затухает
        /// экспонентой до нуля у края текстуры. Рисуется на rect, раздутом на
        /// inset, тонируется через GUI.color.
        /// </summary>
        private static Texture2D MakeGlow(int size, float inset, float falloff)
        {
            float half = size * 0.5f;
            float boxHalf = half - inset;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = RoundedBox(dx, dy, boxHalf, boxHalf, 12f);
                    float a = d <= 0f ? 1f : Mathf.Exp(-d / falloff * 1.6f);
                    a = Mathf.Clamp01((a - 0.015f) / 0.985f);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, a);
                }
            }

            return Make(size, pixels);
        }

        private static float RoundedBox(float px, float py, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(px) - (halfW - radius);
            float qy = Mathf.Abs(py) - (halfH - radius);
            float ax = Mathf.Max(qx, 0f);
            float ay = Mathf.Max(qy, 0f);
            return Mathf.Sqrt((ax * ax) + (ay * ay)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax;
            float aby = by - ay;
            float t = Mathf.Clamp01((((px - ax) * abx) + ((py - ay) * aby)) / ((abx * abx) + (aby * aby)));
            float cx = ax + (abx * t);
            float cy = ay + (aby * t);
            float dx = px - cx;
            float dy = py - cy;
            return Mathf.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>Одиночная или двойная стрелка-шеврон (белая, тонируется).</summary>
        private static Texture2D Chevron(bool right, bool dbl)
        {
            const int size = 32;
            float thick = 3.4f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float best = 99f;
                    if (dbl)
                    {
                        best = Mathf.Min(best, ChevronDistance(px, py, 7f, right));
                        best = Mathf.Min(best, ChevronDistance(px, py, 16f, right));
                    }
                    else
                    {
                        best = ChevronDistance(px, py, 11f, right);
                    }

                    float a = Mathf.Clamp01((thick * 0.5f) - best + 0.5f);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, a);
                }
            }

            return Make(size, pixels);
        }

        // Шеврон с вершиной справа/слева; tail — x «хвоста» (для зеркала считается от края).
        private static float ChevronDistance(float px, float py, float tail, bool right)
        {
            float span = 9.5f;
            float x0 = right ? tail : 32f - tail;
            float x1 = right ? tail + span : 32f - tail - span;
            float d1 = SegmentDistance(px, py, x0, 16f - 8.5f, x1, 16f);
            float d2 = SegmentDistance(px, py, x0, 16f + 8.5f, x1, 16f);
            return Mathf.Min(d1, d2);
        }

        private static Texture2D MakeCross()
        {
            const int size = 32;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float d1 = SegmentDistance(px, py, 9f, 9f, 23f, 23f);
                    float d2 = SegmentDistance(px, py, 9f, 23f, 23f, 9f);
                    float a = Mathf.Clamp01((3.2f * 0.5f) - Mathf.Min(d1, d2) + 0.5f);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, a);
                }
            }

            return Make(size, pixels);
        }

        private static Texture2D MakeDot()
        {
            const int size = 16;
            float half = size * 0.5f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy)) - (half - 1f);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
                }
            }

            return Make(size, pixels);
        }
    }
}
