using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Building UI skin — LIGHT GRAY theme. Everything is drawn in code, without
    /// assets: rounded rectangles via SDF (9-slice), soft shadow, chevron arrows,
    /// cross and preview-window vignette. Independent of CheatGuiSkin, so the
    /// cheat menu and building are styled separately.
    ///
    /// Textures are kept in static fields (otherwise the GC would eat the native
    /// objects and GUIStyle would hold a dangling pointer), and Build() rebuilds
    /// everything if Unity destroyed a texture (disabled domain reload).
    /// </summary>
    internal static class BuilderSkin
    {
        // ─── Palette (dark charcoal, like the reference inventory) ─
        public static readonly Color Accent = new Color(0.30f, 0.55f, 0.85f, 1f);
        public static readonly Color AccentDim = new Color(0.30f, 0.55f, 0.85f, 0.30f);
        public static readonly Color TextMain = new Color(0.86f, 0.87f, 0.88f, 1f);
        public static readonly Color TextMuted = new Color(0.60f, 0.61f, 0.63f, 1f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color Good = new Color(0.35f, 0.78f, 0.50f, 1f);
        public static readonly Color DangerText = new Color(0.92f, 0.62f, 0.62f, 1f);

        /// <summary>Active tab text (orange, like the reference inventory).</summary>
        public static readonly Color Highlight = new Color(1.00f, 0.56f, 0.12f, 1f);

        /// <summary>Thin line under every tab.</summary>
        public static readonly Color TabLine = new Color(0.27f, 0.62f, 0.80f, 1f);

        /// <summary>Shadow around windows and panels (tints Glow).</summary>
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.45f);

        // ─── Frame styles (GUI.Box) ───────────────────────────────────────────
        public static GUIStyle Window { get; private set; }
        public static GUIStyle PanelInner { get; private set; }
        public static GUIStyle Plate { get; private set; }
        public static GUIStyle Viewport { get; private set; }
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

        // Reference-style inventory: flat grid cells, tabs, search, stat pills.
        public static GUIStyle Cell { get; private set; }
        public static GUIStyle CellHover { get; private set; }
        public static GUIStyle CellSelected { get; private set; }
        public static GUIStyle Tab { get; private set; }
        public static GUIStyle TabHover { get; private set; }
        public static GUIStyle SearchBox { get; private set; }
        public static GUIStyle SearchField { get; private set; }
        public static GUIStyle Stat { get; private set; }
        public static GUIStyle DescBox { get; private set; }

        // ─── Text styles ─────────────────────────────────────────────────
        public static GUIStyle TitleText { get; private set; }
        public static GUIStyle NameText { get; private set; }
        public static GUIStyle NameTextCenter { get; private set; }
        public static GUIStyle Heading { get; private set; }
        public static GUIStyle Body { get; private set; }
        public static GUIStyle BodyRight { get; private set; }
        public static GUIStyle Muted { get; private set; }
        public static GUIStyle MutedRight { get; private set; }
        public static GUIStyle MutedCenter { get; private set; }
        public static GUIStyle Tiny { get; private set; }
        public static GUIStyle TinyRight { get; private set; }
        public static GUIStyle TinyCenter { get; private set; }
        public static GUIStyle CardName { get; private set; }
        public static GUIStyle CardKind { get; private set; }
        public static GUIStyle ValueText { get; private set; }
        public static GUIStyle KeyText { get; private set; }
        public static GUIStyle PillText { get; private set; }
        public static GUIStyle ButtonText { get; private set; }
        public static GUIStyle DangerButtonText { get; private set; }
        public static GUIStyle TabText { get; private set; }
        public static GUIStyle TabTextActive { get; private set; }
        public static GUIStyle StatText { get; private set; }
        public static GUIStyle DescText { get; private set; }
        public static GUIStyle PreviewName { get; private set; }
        public static GUIStyle SlotNumber { get; private set; }
        public static GUIStyle CountText { get; private set; }
        public static GUIStyle SearchPlaceholder { get; private set; }

        // ─── Icons ──────────────────────────────────────────────────────────
        public static Texture2D ChevronLeft { get; private set; }
        public static Texture2D ChevronRight { get; private set; }
        public static Texture2D DoubleLeft { get; private set; }
        public static Texture2D DoubleRight { get; private set; }
        public static Texture2D Cross { get; private set; }
        public static Texture2D Dot { get; private set; }
        public static Texture2D Vignette { get; private set; }
        public static Texture2D Search { get; private set; }

        private static bool built;
        private static Texture2D guard;     // it tells us the textures were destroyed

        // Keep references to all textures so the GC doesn't collect them.
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

            // Windows and panels: dark charcoal, flat, like the reference inventory.
            Window = Box(Rounded(48, 16f, C(0.105f, 0.112f, 0.120f, 0.95f), C(0.060f, 0.064f, 0.070f, 1f), 1.2f), 20);
            PanelInner = Box(Rounded(32, 10f, C(0.125f, 0.132f, 0.140f, 1f), C(0.090f, 0.096f, 0.102f, 1f), 1.0f), 12);
            Plate = Box(Rounded(40, 14f, C(0.120f, 0.128f, 0.136f, 0.95f), C(0.070f, 0.075f, 0.080f, 1f), 1.2f), 16);
            Pill = Box(Rounded(40, 14f, C(0.150f, 0.158f, 0.168f, 0.96f), C(0.085f, 0.090f, 0.096f, 1f), 1.2f), 16);
            Viewport = Box(Rounded(48, 14f, C(0.168f, 0.176f, 0.186f, 1f), C(0.110f, 0.116f, 0.122f, 1f), 1.2f), 16);

            // Hotbar slots: separate dark squares; the selected one gets a white outline.
            Slot = Box(Rounded(32, 9f, C(0.165f, 0.172f, 0.180f, 0.94f), C(0.100f, 0.105f, 0.110f, 1f), 1.2f), 11);
            SlotHover = Box(Rounded(32, 9f, C(0.215f, 0.222f, 0.232f, 0.96f), C(0.470f, 0.470f, 0.490f, 1f), 1.6f), 11);
            SlotSelected = Box(Rounded(32, 9f, C(0.195f, 0.202f, 0.212f, 0.96f), C(0.96f, 0.96f, 0.96f, 1f), 2.6f), 11);
            Card = Box(Rounded(32, 10f, C(0.165f, 0.172f, 0.180f, 1f), C(0.105f, 0.110f, 0.115f, 1f), 1.2f), 12);
            CardHover = Box(Rounded(32, 10f, C(0.205f, 0.212f, 0.222f, 1f), C(0.470f, 0.470f, 0.490f, 1f), 1.6f), 12);
            CardSelected = Box(Rounded(32, 10f, C(0.330f, 0.340f, 0.355f, 1f), C(0.640f, 0.650f, 0.670f, 1f), 2.2f), 12);

            // Inventory grid cells (flat, slightly rounded) and tabs.
            Cell = Box(Rounded(24, 5f, C(0.168f, 0.176f, 0.186f, 1f), C(0.168f, 0.176f, 0.186f, 1f), 0.5f), 8);
            CellHover = Box(Rounded(24, 5f, C(0.215f, 0.224f, 0.236f, 1f), C(0.330f, 0.340f, 0.355f, 1f), 1.2f), 8);
            CellSelected = Box(Rounded(24, 5f, C(0.335f, 0.345f, 0.360f, 1f), C(0.640f, 0.650f, 0.670f, 1f), 2.2f), 8);
            Tab = Box(Rounded(24, 5f, C(0.140f, 0.150f, 0.160f, 1f), C(0.140f, 0.150f, 0.160f, 1f), 0.5f), 8);
            TabHover = Box(Rounded(24, 5f, C(0.190f, 0.200f, 0.212f, 1f), C(0.190f, 0.200f, 0.212f, 1f), 0.5f), 8);
            SearchBox = Box(Rounded(32, 11f, C(0.085f, 0.090f, 0.096f, 1f), C(0.175f, 0.184f, 0.195f, 1f), 1.2f), 12);
            Stat = Box(Rounded(32, 11f, C(0.175f, 0.183f, 0.194f, 1f), C(0.175f, 0.183f, 0.194f, 1f), 0.5f), 12);
            DescBox = Box(Rounded(32, 12f, C(0.150f, 0.158f, 0.168f, 1f), C(0.150f, 0.158f, 0.168f, 1f), 0.5f), 14);

            // Arrow buttons.
            Step = Box(Rounded(32, 9f, C(0.215f, 0.224f, 0.236f, 1f), C(0.135f, 0.142f, 0.150f, 1f), 1.2f), 11);
            StepHover = Box(Rounded(32, 9f, C(0.280f, 0.290f, 0.305f, 1f), C(0.480f, 0.480f, 0.500f, 1f), 1.6f), 11);
            StepActive = Box(Rounded(32, 9f, C(0.240f, 0.400f, 0.600f, 1f), C(0.40f, 0.65f, 0.92f, 1f), 1.6f), 11);
            StepDisabled = Box(Rounded(32, 9f, C(0.160f, 0.167f, 0.175f, 1f), C(0.130f, 0.136f, 0.143f, 1f), 1.2f), 11);
            ValueBox = Box(Rounded(32, 9f, C(0.085f, 0.090f, 0.096f, 1f), C(0.060f, 0.064f, 0.068f, 1f), 1.2f), 11);
            Key = Box(Rounded(24, 6f, C(0.195f, 0.203f, 0.214f, 1f), C(0.120f, 0.126f, 0.133f, 1f), 1.2f), 8);
            Danger = Box(Rounded(32, 9f, C(0.380f, 0.200f, 0.200f, 1f), C(0.540f, 0.290f, 0.290f, 1f), 1.2f), 11);
            DangerHover = Box(Rounded(32, 9f, C(0.500f, 0.265f, 0.265f, 1f), C(0.740f, 0.360f, 0.360f, 1f), 1.5f), 11);
            Track = Box(Rounded(16, 5f, C(0.100f, 0.105f, 0.110f, 1f), C(0.070f, 0.074f, 0.078f, 1f), 1f), 6);
            Glow = Box(MakeGlow(64, 20f, 12f), 30);

            // Text.
            TitleText = Text(20, TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            NameText = Text(17, TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            NameTextCenter = Text(17, TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            Heading = Text(12, Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            Body = Text(14, TextMain, TextAnchor.MiddleLeft, FontStyle.Normal);
            BodyRight = Text(14, TextMain, TextAnchor.MiddleRight, FontStyle.Bold);
            Muted = Text(14, TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal);
            MutedRight = Text(14, TextMuted, TextAnchor.MiddleRight, FontStyle.Normal);
            MutedCenter = Text(14, TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal);
            Tiny = Text(11, TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal);
            TinyRight = Text(11, TextMuted, TextAnchor.MiddleRight, FontStyle.Normal);
            TinyCenter = Text(11, TextMuted, TextAnchor.MiddleCenter, FontStyle.Normal);
            CardName = Text(15, TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            // Subtitle under the name — WHITE and word-wrapped ("Engine · vacuum"
            // does not fit a narrow card on one line).
            CardKind = Text(12, TextMain, TextAnchor.MiddleCenter, FontStyle.Normal);
            CardKind.wordWrap = true;
            ValueText = Text(18, TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            KeyText = Text(12, new Color(0.82f, 0.84f, 0.86f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
            PillText = Text(14, TextMain, TextAnchor.MiddleCenter, FontStyle.Normal);
            PillText.richText = true;
            ButtonText = Text(14, TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            DangerButtonText = Text(14, DangerText, TextAnchor.MiddleCenter, FontStyle.Bold);
            TabText = Text(17, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            TabTextActive = Text(17, Highlight, TextAnchor.MiddleCenter, FontStyle.Normal);
            StatText = Text(16, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            StatText.clipping = TextClipping.Clip;
            StatText.richText = true;
            DescText = Text(15, new Color(0.90f, 0.91f, 0.92f, 1f), TextAnchor.UpperLeft, FontStyle.Normal);
            DescText.wordWrap = true;
            DescText.richText = true;
            PreviewName = Text(18, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            SlotNumber = Text(19, Color.white, TextAnchor.UpperCenter, FontStyle.Bold);
            CountText = Text(24, Color.white, TextAnchor.LowerRight, FontStyle.Bold);
            SearchPlaceholder = Text(17, TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic);

            // Text field: transparent background, the SearchBox frame is drawn under it.
            SearchField = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 40, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip
            };
            SearchField.normal.background = null;
            SearchField.hover.background = null;
            SearchField.active.background = null;
            SearchField.focused.background = null;
            SearchField.normal.textColor = Color.white;
            SearchField.hover.textColor = Color.white;
            SearchField.active.textColor = Color.white;
            SearchField.focused.textColor = Color.white;

            // Icons.
            ChevronLeft = Chevron(false, false);
            ChevronRight = Chevron(true, false);
            DoubleLeft = Chevron(false, true);
            DoubleRight = Chevron(true, true);
            Cross = MakeCross();
            Dot = MakeDot();
            Vignette = MakeVignette();
            Search = MakeSearch();

            guard = Dot;
            built = true;
        }

        // ─── Style factories ──────────────────────────────────────────────────

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

        // ─── Textures ────────────────────────────────────────────────────────

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

        /// <summary>Rounded rectangle with an edge, ready for 9-slice.</summary>
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
        /// Soft shadow/glow: alpha 1 inside the "box", fading exponentially to
        /// zero at the texture edge. Drawn on a rect inflated by inset, tinted
        /// via GUI.color.
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

        /// <summary>Vignette for the preview window: soft darkening towards the edges.</summary>
        private static Texture2D MakeVignette()
        {
            const int size = 128;
            float half = size * 0.5f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy));
                    float a = Mathf.Clamp01((d - 0.62f) / 0.38f);
                    a = a * a * 0.22f;
                    pixels[(y * size) + x] = new Color(0.06f, 0.06f, 0.07f, a);
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

        /// <summary>Single or double chevron arrow (white, gets tinted).</summary>
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

        // Chevron with its apex right/left; tail — x of the "tail" (mirrored: measured from the edge).
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

        /// <summary>Magnifier icon (white, gets tinted).</summary>
        private static Texture2D MakeSearch()
        {
            const int size = 32;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float dx = px - 13f;
                    float dy = py - 19f;
                    float ring = Mathf.Abs(Mathf.Sqrt((dx * dx) + (dy * dy)) - 8f);
                    float handle = SegmentDistance(px, py, 19.5f, 12.5f, 26f, 6f);
                    float d = Mathf.Min(ring, handle);
                    float a = Mathf.Clamp01((3.0f * 0.5f) - d + 0.5f);
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
