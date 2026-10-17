using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Прицел: маленькое кольцо в центре экрана — только окружность, без
    /// заливки. Игровые лучи (E-интеракция, кнопки монитора, удаление в
    /// стройке) идут по Camera.main.transform.forward, то есть ровно в
    /// точку центра, а курсор в игре скрыт — без кольца эту точку не видно.
    ///
    /// Рисуется IMGUI (OnGUI): Canvas/UI Toolkit в проекте не заведены, весь
    /// HUD — OnGUI, а текстура кольца генерируется кодом (как скины чит-меню
    /// и стройки) и не тянет ассетов. Видимость — по ЗАХВАТУ курсора: все
    /// меню (чит-меню, планшет, Ctrl-подсмотр в стройке) и Esc отпускают
    /// курсор, так что кольцо гаснет само, без списка условий на каждый экран.
    ///
    /// Radius — не только вид: ровно эту площадь считает зоной попадания
    /// AimCone (снап/удаление в стройке, E-интеракция). Уменьшил кольцо —
    /// сузилась и зона.
    /// </summary>
    public sealed class Crosshair : MonoBehaviour
    {
        /// <summary>Радиус кольца по умолчанию (px экрана): им же пользуется
        /// AimCone, если на камере компонента нет, а зона всё же нужна.</summary>
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

        private void OnGUI()
        {
            // Кольцо нужно только при прицеливании: с отпущенным курсором
            // (Esc, чит-меню, планшет, подсмотр в стройке) мышь не управляет
            // взглядом, и центр экрана ничего не значит.
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
        /// Текстура — под текущий размер кольца на экране (рисуем 1:1, без
        /// масштабирования: иначе кольцо размывает билинейная фильтрация).
        /// Пересобирается, когда инспектор поменял радиус/толщину (в т.ч. на
        /// лету в Play); цвет пересборку не вызывает — его несёт GUI.color.
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
        /// Кольцо как знаковое расстояние до окружности: |d − R| − толщина/2.
        /// Дробная часть даёт сглаживание в 1 px — без него кромка
        /// «ступенчатая» (тот же приём, что в скине чит-меню).
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
