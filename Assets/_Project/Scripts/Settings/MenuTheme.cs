using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Look of the pause/settings menu. Everything is generated in code (no assets):
    /// anti-aliased rounded rectangles for 9-slice boxes, a soft shadow, a disc.
    /// Textures are WHITE — colour comes from GUI.color at draw time, so one
    /// texture serves every state (normal / hover / active). Radii and font sizes are
    /// generated for the current UI scale, so text and corners stay crisp at any
    /// resolution instead of being stretched by a GUI matrix.
    /// The font is an OS font (Bahnschrift → Segoe UI → Arial), all of them have Cyrillic.
    /// </summary>
    internal static class MenuTheme
    {
        // ─── Palette ─────────────────────────────────────────────────────────
        public static readonly Color Accent = new Color(0.30f, 0.66f, 1.00f, 1f);
        public static readonly Color AccentDark = new Color(0.16f, 0.40f, 0.78f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.95f, 0.98f, 1f);
        public static readonly Color Muted = new Color(0.60f, 0.65f, 0.74f, 1f);
        public static readonly Color Subtle = new Color(0.40f, 0.45f, 0.54f, 1f);
        public static readonly Color Danger = new Color(1.00f, 0.43f, 0.43f, 1f);
        public static readonly Color Good = new Color(0.38f, 0.86f, 0.62f, 1f);
        public static readonly Color Panel = new Color(0.050f, 0.060f, 0.085f, 0.97f);
        public static readonly Color Card = new Color(0.085f, 0.100f, 0.135f, 1f);
        public static readonly Color Well = new Color(0.030f, 0.036f, 0.054f, 1f);
        public static readonly Color Hair = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color HairStrong = new Color(1f, 1f, 1f, 0.14f);

        public static float Scale { get; private set; } = -1f;

        public static Texture2D Disc { get; private set; }
        public static GUIStyle Glow { get; private set; }

        private static Font font;
        private static readonly Dictionary<int, GUIStyle> Fills = new Dictionary<int, GUIStyle>();
        private static readonly Dictionary<int, GUIStyle> Rings = new Dictionary<int, GUIStyle>();
        private static readonly Dictionary<long, GUIStyle> Texts = new Dictionary<long, GUIStyle>();
        private static readonly List<Texture2D> Keep = new List<Texture2D>();

        /// <summary>Call every OnGUI. Rebuilds everything when the UI scale changes or Unity destroyed a texture.</summary>
        public static void Ensure(float scale)
        {
            if (Disc != null && Mathf.Abs(scale - Scale) < 0.005f)
            {
                return;
            }

            Scale = scale;
            Fills.Clear();
            Rings.Clear();
            Texts.Clear();
            Keep.Clear();

            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial" }, 16);
            }

            Disc = MakeDisc(96);
            Glow = BoxStyle(MakeGlow(64, 18f, 18f), 31);
        }

        // ─── Styles ──────────────────────────────────────────────────────────

        /// <summary>Filled rounded box. radius is in virtual px; tint with GUI.color.</summary>
        public static GUIStyle Fill(int radius)
        {
            if (!Fills.TryGetValue(radius, out GUIStyle style))
            {
                int px = Mathf.Max(1, Mathf.RoundToInt(radius * Scale));
                style = BoxStyle(MakeRound(px, false, 1f), px + 1);
                Fills[radius] = style;
            }

            return style;
        }

        /// <summary>1 px outline of a rounded box.</summary>
        public static GUIStyle Ring(int radius)
        {
            if (!Rings.TryGetValue(radius, out GUIStyle style))
            {
                int px = Mathf.Max(1, Mathf.RoundToInt(radius * Scale));
                style = BoxStyle(MakeRound(px, true, Mathf.Max(1f, Mathf.Round(Scale))), px + 1);
                Rings[radius] = style;
            }

            return style;
        }

        /// <summary>Text style, white (tint with GUI.color). size is in virtual px.</summary>
        public static GUIStyle TextStyle(int size, bool bold, TextAnchor anchor, bool wrap = false)
        {
            long key = ((long)size << 16) | ((long)(bold ? 1 : 0) << 8) | ((long)(int)anchor << 1) | (wrap ? 1L : 0L);
            if (!Texts.TryGetValue(key, out GUIStyle style))
            {
                style = new GUIStyle
                {
                    font = font,
                    fontSize = Mathf.Max(8, Mathf.RoundToInt(size * Scale)),
                    fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                    alignment = anchor,
                    wordWrap = wrap,
                    richText = false,
                    clipping = TextClipping.Overflow,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0)
                };
                style.normal.textColor = Color.white;
                Texts[key] = style;
            }

            return style;
        }

        private static GUIStyle BoxStyle(Texture2D texture, int border)
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

        // ─── Textures ────────────────────────────────────────────────────────

        private static Texture2D Make(int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            Keep.Add(texture);
            return texture;
        }

        private static float BoxDistance(float px, float py, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(px) - (halfW - radius);
            float qy = Mathf.Abs(py) - (halfH - radius);
            float ox = Mathf.Max(qx, 0f);
            float oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt((ox * ox) + (oy * oy)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>
        /// White rounded square, side 2r+2 (so a 9-slice with border r+1 stretches its
        /// 1px middle). A rect drawn with this style must be at least (2r+2) px each way.
        /// </summary>
        private static Texture2D MakeRound(int radius, bool ring, float ringWidth)
        {
            int size = (radius * 2) + 2;
            float half = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = BoxDistance(x + 0.5f - half, y + 0.5f - half, half - 0.5f, half - 0.5f, radius);
                    float a = Mathf.Clamp01(0.5f - d);
                    if (ring)
                    {
                        a = Mathf.Max(0f, a - Mathf.Clamp01(0.5f - (d + ringWidth)));
                    }

                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            return Make(size, size, pixels);
        }

        private static Texture2D MakeGlow(int size, float inset, float falloff)
        {
            float half = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = BoxDistance(x + 0.5f - half, y + 0.5f - half, half - inset, half - inset, 16f);
                    float t = Mathf.Clamp01(1f - (Mathf.Max(0f, d) / falloff));
                    float a = t * t * 0.62f;
                    pixels[(y * size) + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            }

            return Make(size, size, pixels);
        }

        private static Texture2D MakeDisc(int size)
        {
            float half = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float a = Mathf.Clamp01(half - 0.5f - Mathf.Sqrt((dx * dx) + (dy * dy)) + 0.5f);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            return Make(size, size, pixels);
        }
    }
}
