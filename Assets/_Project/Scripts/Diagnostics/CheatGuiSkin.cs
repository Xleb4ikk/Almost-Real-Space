using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Cheat menu skin: panels, buttons, toggles and sliders are drawn with textures
    /// generated in code (SDF rounded rectangle, 9-slice). Custom styles instead of
    /// GUI.skin keep the look identical in any theme/build, pull no assets, and avoid
    /// the editor default theme's near-transparent buttons in a dark scene.
    /// GUI.Window draws its background with exactly the style passed as the third
    /// parameter, so a label style gives a backgroundless window.
    /// </summary>
    internal static class CheatGuiSkin
    {
        // Texture size and border are sized to the button height (30 px).
        private const int TextureSize = 24;
        private const int TextureBorder = 6;
        private const float CornerRadius = 6f;
        private const float EdgeWidth = 1.5f;
        private const int ButtonHeight = 30;

        private static readonly Color PanelFill = new Color(0.043f, 0.055f, 0.075f, 0.96f);
        private static readonly Color PanelEdge = new Color(0.29f, 0.38f, 0.51f, 1f);
        private static readonly Color ButtonFill = new Color(0.16f, 0.20f, 0.27f, 1f);
        private static readonly Color ButtonFillHover = new Color(0.24f, 0.31f, 0.42f, 1f);
        private static readonly Color ButtonFillActive = new Color(0.11f, 0.14f, 0.19f, 1f);
        private static readonly Color ButtonEdge = new Color(0.33f, 0.42f, 0.56f, 1f);
        private static readonly Color ToggleOnFill = new Color(0.10f, 0.42f, 0.29f, 1f);
        private static readonly Color ToggleOnEdge = new Color(0.29f, 0.78f, 0.55f, 1f);
        private static readonly Color TrackFill = new Color(0.09f, 0.11f, 0.15f, 1f);
        private static readonly Color TextColor = new Color(0.87f, 0.90f, 0.94f, 1f);
        private static readonly Color TextMuted = new Color(0.63f, 0.69f, 0.78f, 1f);
        private static readonly Color TextAccent = new Color(0.60f, 0.78f, 1f, 1f);
        private static readonly Color TextOn = new Color(0.75f, 1f, 0.88f, 1f);

        private static bool built;

        // Textures live in static fields: a runtime-created Texture2D survives only
        // while a managed reference exists, but GUIStyle holds a native pointer.
        // Styles without such references make backgrounds flicker after GC, so the
        // references are mandatory.
        private static Texture2D panelTexture;
        private static Texture2D buttonTexture;
        private static Texture2D buttonHoverTexture;
        private static Texture2D buttonActiveTexture;
        private static Texture2D toggleOffTexture;
        private static Texture2D toggleOnTexture;
        private static Texture2D trackTexture;
        private static Texture2D thumbTexture;

        public static GUIStyle Panel { get; private set; }

        /// <summary>Window title style: carries the panel background and draws the title.</summary>
        public static GUIStyle WindowTitle { get; private set; }

        public static GUIStyle Label { get; private set; }

        public static GUIStyle LabelMuted { get; private set; }

        public static GUIStyle Header { get; private set; }

        public static GUIStyle Readout { get; private set; }

        public static GUIStyle Button { get; private set; }

        public static GUIStyle Toggle { get; private set; }

        public static GUIStyle SliderTrack { get; private set; }

        public static GUIStyle SliderThumb { get; private set; }

        /// <summary>
        /// Builds the styles once. The built flag is set LAST: domain reload is
        /// disabled for this project (Enter Play Mode Options = Disable Domain
        /// Reload), so statics survive Play sessions. If the build threw halfway,
        /// styles with null backgrounds would stick for good; setting the flag last
        /// lets a failed call self-heal on the next frame.
        /// </summary>
        public static void Build()
        {
            if (built)
            {
                return;
            }

            panelTexture = RoundedRect(PanelFill, PanelEdge);
            buttonTexture = RoundedRect(ButtonFill, ButtonEdge);
            buttonHoverTexture = RoundedRect(ButtonFillHover, ButtonEdge);
            buttonActiveTexture = RoundedRect(ButtonFillActive, ButtonEdge);
            toggleOffTexture = RoundedRect(ButtonFill, ButtonEdge);
            toggleOnTexture = RoundedRect(ToggleOnFill, ToggleOnEdge);
            trackTexture = RoundedRect(TrackFill, new Color(0.20f, 0.26f, 0.35f, 1f));
            thumbTexture = Circle(new Color(0.53f, 0.66f, 0.82f, 1f), new Color(0.72f, 0.83f, 0.95f, 1f));

            Panel = Filled(GUI.skin.box, panelTexture, TextAnchor.UpperLeft, TextColor, 15, FontStyle.Normal);
            Panel.padding = new RectOffset(14, 14, 10, 10);
            Panel.wordWrap = false;

            // Window title: GUI.Window draws it with the window style; with MiddleLeft
            // the text centers over the window height and overlaps the content, so
            // UpperLeft pins it to the top edge.
            WindowTitle = Filled(GUI.skin.window, panelTexture, TextAnchor.UpperLeft, TextAccent, 16, FontStyle.Bold);
            WindowTitle.padding = new RectOffset(14, 14, 8, 8);

            Label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false
            };
            Label.normal.textColor = TextColor;

            LabelMuted = new GUIStyle(Label);
            LabelMuted.normal.textColor = TextMuted;

            Header = new GUIStyle(Label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            Header.normal.textColor = TextAccent;

            Readout = Filled(GUI.skin.box, panelTexture, TextAnchor.UpperLeft, TextColor, 15, FontStyle.Normal);
            Readout.padding = new RectOffset(14, 14, 10, 10);
            Readout.wordWrap = false;

            Button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                fixedHeight = ButtonHeight
            };
            Button.normal.background = buttonTexture;
            Button.normal.textColor = TextColor;
            Button.hover.background = buttonHoverTexture;
            Button.hover.textColor = Color.white;
            Button.active.background = buttonActiveTexture;
            Button.active.textColor = TextAccent;
            Button.onNormal.background = buttonTexture;
            Button.onNormal.textColor = TextColor;
            Button.onHover.background = buttonHoverTexture;
            Button.onHover.textColor = Color.white;
            Button.onActive.background = buttonActiveTexture;
            Button.onActive.textColor = TextAccent;
            Button.border = new RectOffset(TextureBorder, TextureBorder, TextureBorder, TextureBorder);
            Button.padding = new RectOffset(14, 14, 4, 4);

            Toggle = new GUIStyle(Button)
            {
                alignment = TextAnchor.MiddleLeft
            };
            Toggle.normal.background = toggleOffTexture;
            Toggle.onNormal.background = toggleOnTexture;
            Toggle.onNormal.textColor = TextOn;
            Toggle.onHover.background = toggleOnTexture;
            Toggle.onHover.textColor = TextOn;
            Toggle.onActive.background = toggleOnTexture;
            Toggle.onActive.textColor = TextOn;

            SliderTrack = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 10,
                stretchHeight = true
            };
            SliderTrack.normal.background = trackTexture;
            SliderTrack.border = new RectOffset(TextureBorder, TextureBorder, TextureBorder, TextureBorder);

            SliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 20,
                fixedHeight = 20
            };
            SliderThumb.normal.background = thumbTexture;

            built = true;
        }

        private static GUIStyle Filled(GUIStyle from, Texture2D background, TextAnchor anchor, Color text, int fontSize, FontStyle fontStyle)
        {
            GUIStyle style = new GUIStyle(from)
            {
                alignment = anchor,
                fontSize = fontSize,
                fontStyle = fontStyle
            };
            style.normal.background = background;
            style.normal.textColor = text;
            style.hover.background = background;
            style.hover.textColor = text;
            style.active.background = background;
            style.active.textColor = text;
            style.onNormal.background = background;
            style.onNormal.textColor = text;
            style.onHover.background = background;
            style.onHover.textColor = text;
            style.onActive.background = background;
            style.onActive.textColor = text;
            style.border = new RectOffset(TextureBorder, TextureBorder, TextureBorder, TextureBorder);
            return style;
        }

        /// <summary>
        /// Rounded rectangle with an edge, ready for 9-slice. The SDF sign is
        /// antialiased over 1 px — otherwise the edge looks stair-stepped.
        /// </summary>
        private static Texture2D RoundedRect(Color fill, Color edge)
        {
            const int size = TextureSize;
            float half = size * 0.5f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    pixels[(y * size) + x] = Shade(RoundedBox(dx, dy, half, half, CornerRadius), fill, edge);
                }
            }

            return Finish(pixels, size);
        }

        private static Texture2D Circle(Color fill, Color edge)
        {
            const int size = TextureSize;
            float half = size * 0.5f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy)) - (half - 1.5f);
                    pixels[(y * size) + x] = Shade(d, fill, edge);
                }
            }

            return Finish(pixels, size);
        }

        /// <summary>
        /// Pixel color from the signed distance: transparent outside, edge color on
        /// the border, fill inside.
        /// </summary>
        private static Color Shade(float distance, Color fill, Color edge)
        {
            float coverage = Mathf.Clamp01(0.5f - distance);
            float inner = Mathf.Clamp01(0.5f - (distance + EdgeWidth));
            Color color = Color.Lerp(edge, fill, inner);
            color.a *= coverage;
            return color;
        }

        private static float RoundedBox(float px, float py, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(px) - (halfW - radius);
            float qy = Mathf.Abs(py) - (halfH - radius);
            float ax = Mathf.Max(qx, 0f);
            float ay = Mathf.Max(qy, 0f);
            return Mathf.Sqrt((ax * ax) + (ay * ay)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static Texture2D Finish(Color[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
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
