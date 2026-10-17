using System;
using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// Планшет управления кораблём. Вешается на GO планшета (или рядом) и
    /// рисует экран 512×320 в текстуру, которая кладётся на меш экрана
    /// (ScreenRenderer). Пока игрок не взаимодействует — просто показывает
    /// данные (до CompactRefreshHz кадров/с, без реакции на ввод). Open() —
    /// интерфейс разворачивается на весь экран с целым масштабом пикселей, курсор
    /// освобождается, работают кнопки; Esc / E — свернуть.
    ///
    /// Материал экрана: лучше HDRP/Unlit (карта цвета кладётся в _UnlitColorMap);
    /// подхватываются также _BaseColorMap / _BaseMap / _MainTex / _EmissiveColorMap.
    /// </summary>
    public sealed class ShipTablet : MonoBehaviour
    {
        [Tooltip("Мост к симуляции (наследник TabletLink). Пусто + AutoDemo — вешается демо.")]
        public TabletLink Link;

        [Tooltip("Если Link не задан — повесить DemoTabletLink с вымышленными данными.")]
        public bool AutoDemo = true;

        [Tooltip("Меш экрана планшета. Его материал получит текстуру планшета.")]
        public Renderer ScreenRenderer;

        [Tooltip("Обновлений экрана в секунду, пока планшет свёрнут.")]
        [Range(1f, 60f)]
        public float CompactRefreshHz = 15f;

        [Tooltip("С какого расстояния лучом можно жать кнопки прямо на экране (м).")]
        public float WorldTouchMeters = 1.6f;

        [Tooltip("Яркость экрана на мешe (для HDRP/Unlit — множитель цвета).")]
        [Range(0.2f, 4f)]
        public float ScreenBrightness = 1f;

        private static readonly string[] TextureProperties =
        {
            "_UnlitColorMap", "_BaseColorMap", "_BaseMap", "_MainTex", "_EmissiveColorMap",
        };

        private TabletScreen screen;
        private TabletTelemetry telemetry;
        private Texture2D texture;
        private Material screenMaterial;
        private float nextRefresh;
        private bool prevCursorVisible;
        private CursorLockMode prevCursorLock;

        /// <summary>Коллайдер меша экрана (мировой указатель: луч → UV → пиксель панели).</summary>
        private Collider screenCollider;

        /// <summary>В этом кадре луч смотрит на экран (жатие кнопок прямо на мониторе).</summary>
        private bool worldPointerHit;

        private Galilego.Universe.SimulationRunner runner;
        private bool runnerSearched;
        private readonly RaycastHit[] worldHits = new RaycastHit[8];

        /// <summary>
        /// Кадр, в котором планшет открыли: клавиша открытия (E) в этом же кадре
        /// ещё лежит в очереди IMGUI-событий, и без этой защиты OnGUI тем же
        /// нажатием планшет мгновенно закрывал бы.
        /// </summary>
        private int openFrame = -1;

        /// <summary>Развёрнут ли планшет на весь экран.</summary>
        public bool Expanded { get; private set; }

        private static int expandedCount;

        /// <summary>
        /// Развёрнут ли хоть один планшет: другие интерфейсы (окно инвентаря
        /// BuilderUI) по этому флагу не перехватывают E, пока планшет открыт.
        /// </summary>
        public static bool AnyExpanded { get { return expandedCount > 0; } }

        /// <summary>Вызывается при сворачивании (игрок вернул управление).</summary>
        public event Action Closed;

        /// <summary>Текстура экрана (можно повесить на любой другой материал/RawImage).</summary>
        public Texture2D Texture { get { return texture; } }

        private void Awake()
        {
            screen = new TabletScreen();
            telemetry = new TabletTelemetry();
            texture = new Texture2D(TabletScreen.W, TabletScreen.H, TextureFormat.RGBA32, false)
            {
                name = "TabletScreen",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            BindMaterial();
        }

        private void OnDestroy()
        {
            if (Expanded)
            {
                Expanded = false;
                expandedCount = Mathf.Max(0, expandedCount - 1);
                RestoreCursor();
            }

            if (texture != null)
            {
                Destroy(texture);
            }

            if (screenMaterial != null)
            {
                Destroy(screenMaterial);
            }
        }

        private void BindMaterial()
        {
            if (ScreenRenderer == null)
            {
                return;
            }

            screenCollider = ScreenRenderer.GetComponent<Collider>();
            if (screenCollider == null)
            {
                screenCollider = ScreenRenderer.GetComponentInChildren<Collider>();
            }

            screenMaterial = ScreenRenderer.material; // собственный экземпляр
            for (int i = 0; i < TextureProperties.Length; i++)
            {
                if (screenMaterial.HasProperty(TextureProperties[i]))
                {
                    screenMaterial.SetTexture(TextureProperties[i], texture);
                }
            }

            Color tint = new Color(ScreenBrightness, ScreenBrightness, ScreenBrightness, 1f);
            if (screenMaterial.HasProperty("_UnlitColor"))
            {
                screenMaterial.SetColor("_UnlitColor", tint);
            }

            if (screenMaterial.HasProperty("_EmissiveColor"))
            {
                // HDRP/Unlit без ключа _EMISSIVE_COLOR_MAP светит _EmissiveColor
                // ровно по всему экрану (UnlitData.hlsl) — белый вымывает картинку.
                screenMaterial.SetColor("_EmissiveColor", Color.black);
            }
        }

        private void Update()
        {
            if (Link == null && AutoDemo)
            {
                Link = gameObject.AddComponent<DemoTabletLink>();
            }

            if (Link == null)
            {
                return;
            }

            UpdateWorldPointer();

            // Свёрнут — CompactRefreshHz; развёрнут или под прицелом (мировой
            // клик по монитору) — 30 Гц: клик ждёт кадра не дольше 33 мс.
            if (!Expanded && Time.unscaledTime < nextRefresh)
            {
                return;
            }

            bool fast = Expanded || worldPointerHit;
            nextRefresh = Time.unscaledTime + (1f / (fast ? 30f : Mathf.Max(1f, CompactRefreshHz)));
            Link.Fill(telemetry);
            // Под прицелом панель тоже «интерактивна»: работают наведение и клик.
            screen.Render(telemetry, Link, Expanded || worldPointerHit);
            texture.SetPixels32(screen.Canvas.Pixels);
            texture.Apply(false);
        }

        /// <summary>
        /// Мировой указатель: луч камеры (прицел) в меш экрана — точка попадания
        /// по UV переводится в пиксели панели; ЛКМ и протяжка жмут кнопки прямо
        /// на мониторе, стрелка-курсор рисуется на самом экране. Работает, когда
        /// планшет не развёрнут, курсор захвачен и не идёт стройка (там ЛКМ/ПКМ
        /// заняты постановкой деталей).
        /// </summary>
        private void UpdateWorldPointer()
        {
            worldPointerHit = false;
            if (Expanded || ScreenRenderer == null || screenCollider == null
                || Cursor.lockState != CursorLockMode.Locked)
            {
                screen.SetWorldPointer(false, 0, 0);
                return;
            }

            if (runner == null && !runnerSearched)
            {
                runnerSearched = true;
                runner = FindAnyObjectByType<Galilego.Universe.SimulationRunner>();
            }

            if (runner != null && runner.BuildMode)
            {
                screen.SetWorldPointer(false, 0, 0);
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            int count = Physics.RaycastNonAlloc(ray, worldHits, WorldTouchMeters, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                // Нас интересует только сам экран (меш-коллайдер с UV): рамка и
                // корпус монитора снаружи в счёт не идут.
                if (worldHits[i].collider == screenCollider
                    && (best < 0 || worldHits[i].distance < worldHits[best].distance))
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                screen.SetWorldPointer(false, 0, 0);
                return;
            }

            Vector2 uv = worldHits[best].textureCoord;
            int px = Mathf.Clamp((int)(uv.x * TabletScreen.W), 0, TabletScreen.W - 1);
            int py = Mathf.Clamp((int)((1f - uv.y) * TabletScreen.H), 0, TabletScreen.H - 1);
            screen.SetWorldPointer(true, px, py);
            worldPointerHit = true;

            if (Galilego.Universe.PlayerInput.MouseLeftDown())
            {
                screen.Click();
            }

            if (Galilego.Universe.PlayerInput.MouseLeftHeld())
            {
                screen.Drag();
            }
        }

        /// <summary>Развернуть планшет на весь экран (кнопки начинают работать).</summary>
        public void Open()
        {
            if (Expanded)
            {
                return;
            }

            Expanded = true;
            expandedCount++;
            openFrame = Time.frameCount;
            prevCursorVisible = Cursor.visible;
            prevCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Свернуть планшет обратно на корабельный экран.</summary>
        public void Close()
        {
            if (!Expanded)
            {
                return;
            }

            Expanded = false;
            expandedCount = Mathf.Max(0, expandedCount - 1);
            screen.SetPointer(false, 0, 0);
            RestoreCursor();
            Action handler = Closed;
            if (handler != null)
            {
                handler();
            }
        }

        private void RestoreCursor()
        {
            Cursor.lockState = prevCursorLock;
            Cursor.visible = prevCursorVisible;
        }

        private void OnGUI()
        {
            if (!Expanded || texture == null)
            {
                return;
            }

            Event e = Event.current;
            int scale = Mathf.Max(1, Mathf.Min(Screen.width / TabletScreen.W, Screen.height / TabletScreen.H));
            float rw = TabletScreen.W * scale;
            float rh = TabletScreen.H * scale;
            var rect = new Rect(Mathf.Floor((Screen.width - rw) * 0.5f), Mathf.Floor((Screen.height - rh) * 0.5f), rw, rh);

            if (e.type == EventType.Repaint)
            {
                Color old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.82f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                GUI.color = old;
                return;
            }

            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.E))
            {
                // E в кадре открытия — то же нажатие, что уже обработал
                // TabletInteractable: закрывать им нельзя, иначе планшет
                // «не открывается». Esc закрывает всегда.
                if (e.keyCode == KeyCode.E && Time.frameCount == openFrame)
                {
                    e.Use();
                    return;
                }

                e.Use();
                Close();
                return;
            }

            Vector2 m = e.mousePosition;
            bool inside = rect.Contains(m);
            int lx = Mathf.FloorToInt((m.x - rect.x) / scale);
            int ly = Mathf.FloorToInt((m.y - rect.y) / scale);
            screen.SetPointer(inside, lx, ly);

            if (e.type == EventType.MouseDown && e.button == 0 && inside)
            {
                screen.Click();
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && inside)
            {
                screen.Drag();
                e.Use();
            }
        }
    }
}
