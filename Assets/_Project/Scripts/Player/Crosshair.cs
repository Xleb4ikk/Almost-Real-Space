using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Crosshair: a small ring at the center of the screen — just a circle, no fill.
    /// Game rays (E interaction, monitor buttons, delete in the builder) go along
    /// Camera.main.transform.forward, i.e. exactly at the center point, and the in-game
    /// cursor is hidden — without the ring that point is invisible.
    ///
    /// Drawn with IMGUI (OnGUI): the project has no Canvas/UI Toolkit, the whole HUD is
    /// OnGUI, and the ring texture is generated in code (like the cheat-menu and
    /// builder skins) so it pulls no assets. Visibility is by CURSOR LOCK: every menu
    /// (cheat menu, tablet, Ctrl-peek in the builder) and Esc release the cursor, so the
    /// ring hides itself without a per-screen list of conditions.
    ///
    /// Radius is not just visual: AimCone uses exactly this area as the hit zone
    /// (snap/delete in the builder, E interaction). Shrink the ring and the zone shrinks.
    /// </summary>
    public sealed class Crosshair : MonoBehaviour
    {
        /// <summary>Default ring radius (screen px): also used by AimCone when the
        /// camera has no component but the zone is still needed.</summary>
        public const float DefaultRadiusPx = 5f;

        [Tooltip("Радиус кольца (px экрана) — он же угол зоны взаимодействия.")]
        public float Radius = DefaultRadiusPx;

        [Tooltip("Толщина кольца (px экрана).")]
        public float Thickness = 0.75f;

        [Tooltip("Цвет кольца; альфа — прозрачность.")]
        public Color RingColor = new Color(0.92f, 0.95f, 1f, 0.85f);

        private Texture2D ringTexture;
        private int builtSize;
        private float builtRadius;
        private float builtThickness;

        /// <summary>
        /// The ring must exist even if the component was not placed on the camera in
        /// the scene (it was silently missing there): add it to the player camera.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            FirstPersonCamera playerCamera = FindAnyObjectByType<FirstPersonCamera>();
            if (playerCamera != null && playerCamera.GetComponent<Crosshair>() == null)
            {
                playerCamera.gameObject.AddComponent<Crosshair>();
            }
        }

        private void OnGUI()
        {
            // The ring is only needed while aiming: with the cursor released
            // (Esc, cheat menu, tablet, builder peek) the mouse does not drive the
            // view and the screen center means nothing.
            if (Cursor.lockState != CursorLockMode.Locked || !Application.isFocused)
            {
                return;
            }

            EnsureTexture();
            float left = Mathf.Floor((Screen.width - builtSize) * 0.5f);
            float top = Mathf.Floor((Screen.height - builtSize) * 0.5f);
            Color previous = GUI.color;
            GUI.color = RingColor;
            GUI.DrawTexture(new Rect(left, top, builtSize, builtSize), ringTexture);
            GUI.color = previous;
        }

        /// <summary>
        /// The texture matches the current on-screen ring size (drawn 1:1, without
        /// scaling: otherwise bilinear filtering blurs the ring). Rebuilt when the
        /// inspector changes the radius/thickness (including live in Play); color does
        /// not trigger a rebuild — it is carried by GUI.color.
        /// </summary>
        private void EnsureTexture()
        {
            int size = Mathf.CeilToInt((Radius + Thickness) * 2f) + 2;
            if (ringTexture != null && size == builtSize
                && Mathf.Approximately(Radius, builtRadius)
                && Mathf.Approximately(Thickness, builtThickness))
            {
                return;
            }

            if (ringTexture != null)
            {
                Destroy(ringTexture);
            }

            builtSize = size;
            builtRadius = Radius;
            builtThickness = Thickness;
            ringTexture = BuildRing(size, Radius, Thickness);
        }

        /// <summary>
        /// The ring as a signed distance to the circle: |d − R| − thickness/2.
        /// The fractional part gives 1 px antialiasing — without it the edge is
        /// stepped (the same trick as in the cheat-menu skin).
        /// </summary>
        private static Texture2D BuildRing(int size, float radius, float thickness)
        {
            float center = size * 0.5f;
            float halfThickness = Mathf.Max(0.5f, thickness * 0.5f);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float distance = Mathf.Abs(Mathf.Sqrt((dx * dx) + (dy * dy)) - radius)
                        - halfThickness;
                    pixels[(y * size) + x] = new Color32(255, 255, 255,
                        (byte)(Mathf.Clamp01(0.5f - distance) * 255f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            if (ringTexture != null)
            {
                Destroy(ringTexture);
            }
        }
    }
}
