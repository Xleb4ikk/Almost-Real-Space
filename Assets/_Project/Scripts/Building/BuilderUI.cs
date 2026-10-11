using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// ИНТЕРФЕЙС СТРОИТЕЛЬСТВА (IMGUI поверх BuilderSkin, светлая серая тема).
    ///
    /// Хотбар: 9 слотов снизу по центру на светлой подложке. Предметы
    /// перетаскиваются сюда из инвентаря (ЛКМ, с порогом сдвига — простой клик
    /// только выбирает деталь). Двойной клик по карточке — в свободный слот,
    /// ПКМ по слоту — очистить. Выбор слота — колесо или цифры 1..9; выбранный
    /// слот приподнят, обведён ярким квадратом со свечением, над панелью —
    /// плашка с названием и размерами.
    ///
    /// Инвентарь (E, только в стройке): окно ~70% экрана — слева 3D-превью
    /// выбранной детали (крутится, размеры учитываются) и характеристики,
    /// справа сетка карточек. Размеры меняются кнопками-стрелками: большие
    /// «двойные» — на 1 м, одиночные — на 0.1 м (удержание повторяет шаг).
    /// Размеры сразу меняют призрак и поставленную деталь, а также превью.
    ///
    /// Пока окно открыто, курсор отпускается и ввод игроку блокируется
    /// (PlayerController.InputBlocked) — как в чит-меню. Масштаб под DPI/4K:
    /// GUI.matrix растягивается по высоте экрана.
    /// </summary>
    [DefaultExecutionOrder(-105)]
    public sealed class BuilderUI : MonoBehaviour
    {
        [Tooltip("Режим строительства сцены.")]
        public SpacecraftBuilder Builder;

        [Tooltip("Камера от первого лица (её захват курсора отдаётся окну).")]
        public FirstPersonCamera Camera;

        [Tooltip("Контроллер игрока (блокировка ввода, пока окно открыто).")]
        public PlayerController Controller;

        private static readonly PartKind[] Catalog =
        {
            PartKind.Engine, PartKind.FuelTank, PartKind.Separator
        };

        // Геометрия (в «виртуальных» пикселях при высоте экрана 1080).
        private const float SlotSize = 58f;
        private const float SlotGap = 8f;
        private const float PlatePad = 10f;
        private const float DragThreshold = 5f;

        /// <summary>Окно инвентаря занимает ~70% экрана (по ширине и высоте).</summary>
        private const float WindowFraction = 0.70f;

        // Шаги кнопок-стрелок, м.
        private const float StepBig = 1f;
        private const float StepSmall = 0.1f;

        private readonly PartKind?[] hotbar = new PartKind?[9];
        private readonly BuilderPreview preview = new BuilderPreview();
        private int hotbarIndex = -1;
        private bool open;
        private float openAnim;
        private int statsIndex = -1;

        private int dragKind = -1;
        private int dragFromHotbar = -1;
        private Vector2 dragOrigin;
        private bool dragMoved;

        private int holdId = -1;
        private float holdNextTime;

        private bool cursorWasLocked = true;
        private float uiWidth = 1920f;
        private float uiHeight = 1080f;
        private float uiAlpha = 1f;

        /// <summary>Панель подсказок по клавишам скрыта (H или крестик на панели).</summary>
        private static bool hintsHidden;

        private void Start()
        {
            // Стартовая палитра в панели: строить можно сразу, но перетаскивание
            // из инвентаря работает как обычно (слоты можно менять местами).
            hotbar[0] = PartKind.Engine;
            hotbar[1] = PartKind.FuelTank;
            hotbar[2] = PartKind.Separator;
            hotbarIndex = 0;
            ApplySelection();

            // Иконки деталей — 3D-рендеры под углом (тот же студийный свет, что
            // у превью): без этого в слотах и карточках были плоские проекции.
            if (Builder != null)
            {
                preview.PrepareIcons(Builder.Engine, Builder.TankSideMaterial,
                    Builder.TankCapMaterial, Builder.SeparatorMaterial);
            }
        }

        private void Update()
        {
            if (Builder == null)
            {
                return;
            }

            SimulationRunner runner = Builder.Runner;
            if (runner == null)
            {
                return;
            }

            bool buildActive = runner.BuildMode && !Builder.PhysicsOn;
            if (!buildActive)
            {
                if (open)
                {
                    SetOpen(false);
                }

                openAnim = 0f;
                return;
            }

            openAnim = Mathf.MoveTowards(openAnim, open ? 1f : 0f, Time.unscaledDeltaTime * 9f);
            TickPreview();

            if (PlayerInput.Down(GameKey.E))
            {
                SetOpen(!open);
            }

            // H — скрыть/вернуть подсказки по клавишам (панель слева внизу).
            if (PlayerInput.Down(GameKey.H))
            {
                hintsHidden = !hintsHidden;
            }

            // Колесо/цифры работают и при открытом окне (удобно наводить порядок).
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (PlayerInput.Down((GameKey)((int)GameKey.Digit1 + i)))
                {
                    SetHotbarIndex(i);
                }
            }

            float scroll = PlayerInput.MouseScroll();
            if (Mathf.Abs(scroll) > 0.05f)
            {
                int step = scroll > 0f ? -1 : 1;
                SetHotbarIndex(((hotbarIndex + step) % hotbar.Length + hotbar.Length) % hotbar.Length);
            }
        }

        private void SetHotbarIndex(int index)
        {
            hotbarIndex = index;
            ApplySelection();
        }

        private void ApplySelection()
        {
            bool has = hotbarIndex >= 0 && hotbar[hotbarIndex].HasValue;
            Builder.HasSelection = has;
            if (has)
            {
                Builder.SelectedKind = hotbar[hotbarIndex].Value;
            }
        }

        private void SetOpen(bool value)
        {
            open = value;
            holdId = -1;
            if (Builder != null)
            {
                Builder.UiOpen = value;
            }

            if (Controller != null)
            {
                Controller.InputBlocked = value;
            }

            if (value)
            {
                // Чтобы панель характеристик не была пустой: показываем выбранную деталь.
                if (statsIndex < 0)
                {
                    PartKind? current = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
                    statsIndex = current.HasValue ? System.Array.IndexOf(Catalog, current.Value) : 0;
                }

                cursorWasLocked = Camera == null || Camera.LockCursor;
                if (Camera != null)
                {
                    Camera.LockCursor = false;
                }

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            dragKind = -1;
            dragFromHotbar = -1;
            dragMoved = false;
            if (Camera != null)
            {
                Camera.LockCursor = cursorWasLocked;
            }

            Cursor.lockState = cursorWasLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !cursorWasLocked;
        }

        private void OnDisable()
        {
            preview.Release();
            if (open)
            {
                SetOpen(false);
            }
        }

        private void OnDestroy()
        {
            preview.Release();
        }

        // ─── 3D-предпросмотр ─────────────────────────────────────────────────

        /// <summary>
        /// Раз в кадр: показать в превью выбранную деталь с текущими размерами.
        /// Визуал пересобирается при смене детали или размера, камера кадрирует
        /// по габаритам, кадр рендерится не чаще 30 Гц (внутри Ensure).
        /// </summary>
        private void TickPreview()
        {
            if (openAnim <= 0.001f || Builder == null)
            {
                return;
            }

            int index = statsIndex;
            if (index < 0 || index >= Catalog.Length)
            {
                PartKind? current = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
                index = current.HasValue ? System.Array.IndexOf(Catalog, current.Value) : 0;
            }

            PartKind kind = Catalog[index];
            preview.Ensure(kind, Builder.MetricsFor(kind), Builder.Engine,
                Builder.TankSideMaterial, Builder.TankCapMaterial, Builder.SeparatorMaterial);
        }

        // ─── Отрисовка ───────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (Builder == null)
            {
                return;
            }

            SimulationRunner runner = Builder.Runner;
            if (runner == null)
            {
                return;
            }

            bool buildActive = runner.BuildMode && !Builder.PhysicsOn;
            bool returning = !runner.BuildMode && Builder.PhysicsOn;
            if (!buildActive && !returning)
            {
                return;
            }

            float scale = Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            uiWidth = Screen.width / scale;
            uiHeight = Screen.height / scale;
            uiAlpha = 1f;
            BuilderSkin.Build();

            if (Event.current.type == EventType.MouseUp)
            {
                holdId = -1;                         // отпустили кнопку-стрелку
            }

            if (buildActive)
            {
                DrawSummary();
                DrawHints();
                DrawHotbar();
                DrawRHold("Включение физики…");
                if (openAnim > 0.001f)
                {
                    DrawWindow();
                }

                DrawDragIcon();
                HandleDragRelease();
            }
            else
            {
                DrawRHold("Возврат к стройке…");
                DrawPhysicsBanner();
            }

            GUI.color = Color.white;
            GUI.matrix = previous;
        }

        // ─── Хотбар ──────────────────────────────────────────────────────────

        private Rect PlateRect()
        {
            float total = (SlotSize * hotbar.Length) + (SlotGap * (hotbar.Length - 1));
            float w = total + (PlatePad * 2f);
            float h = SlotSize + (PlatePad * 2f);
            return new Rect((uiWidth - w) * 0.5f, uiHeight - h - 16f, w, h);
        }

        private Rect HotbarSlotRect(int index)
        {
            Rect plate = PlateRect();
            return new Rect(plate.x + PlatePad + (index * (SlotSize + SlotGap)), plate.y + PlatePad, SlotSize, SlotSize);
        }

        private void DrawHotbar()
        {
            Rect plate = PlateRect();
            Event current = Event.current;
            Vector2 mouse = current.mousePosition;

            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(plate, 22f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(plate, GUIContent.none, BuilderSkin.Plate);

            DrawNamePlate(plate);

            for (int i = 0; i < hotbar.Length; i++)
            {
                Rect rect = HotbarSlotRect(i);
                bool selected = i == hotbarIndex;
                bool dropTarget = open && dragMoved && rect.Contains(mouse);
                bool hover = open && dragKind < 0 && rect.Contains(mouse);
                PartKind? kind = hotbar[i];

                if (selected)
                {
                    rect.y -= 5f;                    // выбранный слот приподнят
                    SetColor(new Color(0.30f, 0.55f, 0.85f, 0.55f));
                    GUI.Box(Expand(rect, 20f), GUIContent.none, BuilderSkin.Glow);
                    SetColor(Color.white);
                }

                GUIStyle style = selected ? BuilderSkin.SlotSelected
                    : (dropTarget || hover) ? BuilderSkin.SlotHover : BuilderSkin.Slot;
                GUI.Box(rect, GUIContent.none, style);

                if (dropTarget)
                {
                    DrawOutline(Expand(rect, 1f), 2f, BuilderSkin.Accent);
                }

                if (kind.HasValue)
                {
                    float inset = selected ? 6f : 8f;
                    var icon = new Rect(rect.x + inset, rect.y + inset, rect.width - (inset * 2f), rect.height - (inset * 2f));
                    bool lifted = dragMoved && dragFromHotbar == i;
                    SetColor(new Color(1f, 1f, 1f, lifted ? 0.3f : 1f));
                    GUI.DrawTexture(icon, PartIcons.Get(kind.Value), ScaleMode.ScaleToFit);
                    SetColor(Color.white);
                }

                GUI.Label(new Rect(rect.x + 6f, rect.y + 3f, 20f, 14f), (i + 1).ToString(),
                    selected ? BuilderSkin.Body : BuilderSkin.Tiny);

                if (open && current.type == EventType.MouseDown && rect.Contains(mouse) && dragKind < 0)
                {
                    if (current.button == 1)
                    {
                        hotbar[i] = null;            // ПКМ по слоту — очистить
                        ApplySelection();
                    }
                    else if (current.button == 0)
                    {
                        SetHotbarIndex(i);
                        if (kind.HasValue)
                        {
                            statsIndex = System.Array.IndexOf(Catalog, kind.Value);
                            dragKind = (int)kind.Value;
                            dragFromHotbar = i;
                            dragOrigin = mouse;
                            dragMoved = false;
                        }
                    }

                    current.Use();
                }
            }
        }

        // Плашка над хотбаром: название выбранной детали и её размеры.
        private void DrawNamePlate(Rect plate)
        {
            PartKind? kind = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
            if (!kind.HasValue)
            {
                return;
            }

            PartMetrics m = Builder.MetricsFor(kind.Value);
            string dims = string.Empty;
            if (kind.Value == PartKind.FuelTank)
            {
                dims = "   <color=#9AA0A8>L " + m.Height.ToString("F1") + " м  ·  R " + m.Radius.ToString("F2") + " м</color>";
            }
            else if (kind.Value == PartKind.Separator)
            {
                dims = "   <color=#9AA0A8>R " + m.Radius.ToString("F2") + " м</color>";
            }

            string text = "<b>" + PartCatalog.DisplayName(kind.Value) + "</b>" + dims;
            Vector2 size = BuilderSkin.PillText.CalcSize(new GUIContent(text));
            float w = size.x + 32f;
            var rect = new Rect(plate.center.x - (w * 0.5f), plate.y - 42f, w, 30f);
            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 10f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(rect, GUIContent.none, BuilderSkin.Pill);
            GUI.Label(rect, text, BuilderSkin.PillText);
        }

        // ─── Подсказки и сводка ──────────────────────────────────────────────

        private void DrawHints()
        {
            if (hintsHidden)
            {
                DrawHintsRestore();
                return;
            }

            string[][] keys =
            {
                new[] { "E" },
                new[] { "ПКМ" },
                new[] { "Alt", "ПКМ" },
                new[] { "Ctrl", "Z" },
                new[] { "Колесо" },
                new[] { "R" },
                new[] { "H" }
            };
            string[] labels =
            {
                "Инвентарь",
                "Поставить деталь",
                "Удалить по прицелу",
                "Убрать последнюю",
                "Выбор слота",
                "Физика (удерж. 1 с)",
                "Скрыть подсказки"
            };

            const float rowH = 26f;
            const float col = 92f;
            float h = (rowH * labels.Length) + 20f;
            var panel = new Rect(16f, uiHeight - h - 16f, 268f, h);
            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(panel, 14f), GUIContent.none, BuilderSkin.Glow);
            SetColor(new Color(1f, 1f, 1f, 0.98f));
            GUI.Box(panel, GUIContent.none, BuilderSkin.Plate);
            SetColor(Color.white);

            // Крестик в углу панели — скрыть подсказки (клик работает при
            // свободном курсоре: окно инвентаря открыто или курсор отпущен).
            var hide = new Rect(panel.xMax - 30f, panel.y + 5f, 22f, 22f);
            if (HintButton(hide, BuilderSkin.Cross, 12f))
            {
                hintsHidden = true;
            }

            for (int i = 0; i < labels.Length; i++)
            {
                float y = panel.y + 10f + (i * rowH);
                float x = panel.x + 14f;
                for (int k = 0; k < keys[i].Length; k++)
                {
                    float kw = Mathf.Max(26f, BuilderSkin.KeyText.CalcSize(new GUIContent(keys[i][k])).x + 14f);
                    var keyRect = new Rect(x, y + 2f, kw, 22f);
                    GUI.Box(keyRect, GUIContent.none, BuilderSkin.Key);
                    GUI.Label(keyRect, keys[i][k], BuilderSkin.KeyText);
                    x += kw + 4f;
                }

                GUI.Label(new Rect(panel.x + 14f + col, y, panel.width - col - 34f, rowH), labels[i], BuilderSkin.Muted);
            }
        }

        /// <summary>Свёрнутая плашка «Подсказки» на месте скрытой панели.</summary>
        private void DrawHintsRestore()
        {
            const string text = "Подсказки (H)";
            Vector2 size = BuilderSkin.PillText.CalcSize(new GUIContent(text));
            float w = size.x + 52f;
            var rect = new Rect(16f, uiHeight - 46f, w, 30f);

            bool interactive = Cursor.lockState != CursorLockMode.Locked;
            bool hover = interactive && rect.Contains(Event.current.mousePosition);

            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 10f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(rect, GUIContent.none, hover ? BuilderSkin.SlotHover : BuilderSkin.Pill);

            var mark = new Rect(rect.x + 8f, rect.y + 5f, 20f, 20f);
            GUI.Box(mark, GUIContent.none, BuilderSkin.Key);
            GUI.Label(mark, "?", BuilderSkin.KeyText);
            GUI.Label(new Rect(rect.x + 34f, rect.y, rect.width - 40f, rect.height), text, BuilderSkin.PillText);

            Event e = Event.current;
            if (interactive && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                hintsHidden = false;
                e.Use();
            }
        }

        /// <summary>Маленькая кнопка-иконка подсказок (крестик): клик при свободном курсоре.</summary>
        private bool HintButton(Rect rect, Texture2D icon, float iconSize)
        {
            Event e = Event.current;
            bool interactive = Cursor.lockState != CursorLockMode.Locked;
            bool hover = interactive && rect.Contains(e.mousePosition);

            GUI.Box(rect, GUIContent.none, hover ? BuilderSkin.StepHover : BuilderSkin.Step);
            SetColor(new Color(0.85f, 0.87f, 0.89f, 0.95f));
            GUI.DrawTexture(new Rect(rect.center.x - (iconSize * 0.5f), rect.center.y - (iconSize * 0.5f), iconSize, iconSize), icon);
            SetColor(Color.white);

            if (interactive && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }

            return false;
        }

        // Верхняя плашка: сколько деталей, масса и высота стека.
        private void DrawSummary()
        {
            var parts = Builder.Parts;
            if (parts == null || parts.Count == 0)
            {
                return;
            }

            double mass = 0d;
            float low = float.MaxValue;
            float high = float.MinValue;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart part = parts[i];
                mass += part.Metrics.MassKg;
                low = Mathf.Min(low, part.Pivot.y + part.Metrics.Bottom);
                high = Mathf.Max(high, part.Pivot.y + part.Metrics.Top);
            }

            string text = "<b>" + parts.Count + "</b> дет.   ·   <b>" + FormatMass(mass) + "</b>   ·   высота <b>"
                + Mathf.Max(0f, high - low).ToString("F1") + " м</b>";
            Vector2 size = BuilderSkin.PillText.CalcSize(new GUIContent(text));
            float w = size.x + 40f;
            var rect = new Rect((uiWidth - w) * 0.5f, 18f, w, 34f);
            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 10f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(rect, GUIContent.none, BuilderSkin.Pill);
            GUI.Label(rect, text, BuilderSkin.PillText);
        }

        private void DrawRHold(string caption)
        {
            float progress = Builder.RHoldProgress;
            if (progress <= 0.001f)
            {
                return;
            }

            Rect plate = PlateRect();
            var bar = new Rect(plate.center.x - 130f, plate.y - 96f, 260f, 14f);
            GUI.Label(new Rect(bar.x, bar.y - 24f, bar.width, 20f), caption, BuilderSkin.NameTextCenter);
            GUI.Box(bar, GUIContent.none, BuilderSkin.Track);

            // Заливка: полная плашка, обрезанная группой до нужной ширины.
            float fillW = Mathf.Max(14f, bar.width * Mathf.Clamp01(progress));
            GUI.BeginGroup(new Rect(bar.x, bar.y, fillW, bar.height));
            SetColor(BuilderSkin.Accent);
            GUI.Box(new Rect(0f, 0f, bar.width, bar.height), GUIContent.none, BuilderSkin.StepActive);
            SetColor(Color.white);
            GUI.EndGroup();
        }

        private void DrawPhysicsBanner()
        {
            const string text = "Физика включена   ·   удерживайте <b>R</b> (1 с), чтобы вернуться к стройке";
            Vector2 size = BuilderSkin.PillText.CalcSize(new GUIContent(text));
            float w = size.x + 64f;
            var rect = new Rect((uiWidth - w) * 0.5f, uiHeight - 62f, w, 38f);
            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 12f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(rect, GUIContent.none, BuilderSkin.Pill);

            float pulse = 0.55f + (0.45f * Mathf.Sin(Time.unscaledTime * 4f));
            SetColor(new Color(BuilderSkin.Good.r, BuilderSkin.Good.g, BuilderSkin.Good.b, pulse));
            GUI.DrawTexture(new Rect(rect.x + 18f, rect.y + 14f, 10f, 10f), BuilderSkin.Dot);
            SetColor(Color.white);
            GUI.Label(new Rect(rect.x + 24f, rect.y, rect.width - 24f, rect.height), text, BuilderSkin.PillText);
        }

        // ─── Окно инвентаря ──────────────────────────────────────────────────

        private void DrawWindow()
        {
            float eased = 1f - ((1f - openAnim) * (1f - openAnim));
            uiAlpha = eased;

            float winW = Mathf.Max(760f, uiWidth * WindowFraction);
            float winH = Mathf.Max(540f, uiHeight * WindowFraction);
            float slide = (1f - eased) * 18f;
            var win = new Rect((uiWidth - winW) * 0.5f, ((uiHeight - winH) * 0.5f) - 24f + slide, winW, winH);

            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(win, 26f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(win, GUIContent.none, BuilderSkin.Window);

            // Шапка.
            GUI.Label(new Rect(win.x + 26f, win.y + 14f, 500f, 30f), "Инвентарь деталей", BuilderSkin.TitleText);
            GUI.Label(new Rect(win.x + 26f, win.y + 40f, 500f, 18f), "Режим строительства", BuilderSkin.Tiny);

            var close = new Rect(win.xMax - 56f, win.y + 16f, 36f, 36f);
            if (DrawIconButton(close, BuilderSkin.Cross, 22f))
            {
                SetOpen(false);
            }

            var keyRect = new Rect(close.x - 38f, win.y + 23f, 28f, 22f);
            GUI.Box(keyRect, GUIContent.none, BuilderSkin.Key);
            GUI.Label(keyRect, "E", BuilderSkin.KeyText);
            GUI.Label(new Rect(keyRect.x - 78f, win.y + 21f, 72f, 26f), "закрыть", BuilderSkin.MutedRight);

            DrawSolid(new Rect(win.x + 18f, win.y + 68f, win.width - 36f, 1f), BuilderSkin.Line);

            float bodyY = win.y + 80f;
            float bodyH = win.height - 80f - 18f;
            float statsW = Mathf.Clamp(win.width * 0.30f, 350f, 520f);
            var stats = new Rect(win.x + 18f, bodyY, statsW, bodyH);
            var grid = new Rect(stats.xMax + 14f, bodyY, win.xMax - 18f - (stats.xMax + 14f), bodyH);

            GUI.Box(stats, GUIContent.none, BuilderSkin.PanelInner);
            GUI.Box(grid, GUIContent.none, BuilderSkin.PanelInner);
            DrawStats(stats);
            DrawGrid(grid);

            uiAlpha = 1f;
            GUI.color = Color.white;
        }

        private void DrawStats(Rect r)
        {
            float x = r.x + 20f;
            float w = r.width - 40f;
            float y = r.y + 16f;

            DrawHeading(new Rect(x, y, w, 16f), "ХАРАКТЕРИСТИКИ");
            y += 28f;

            if (statsIndex < 0)
            {
                GUI.Label(new Rect(x, y, w, 20f), "Выберите деталь справа.", BuilderSkin.Muted);
                return;
            }

            PartKind kind = Catalog[statsIndex];
            PartMetrics m = Builder.MetricsFor(kind);

            // 3D-окно предпросмотра: кадр 4:3, высота — что осталось от панели
            // после строк и редакторов размеров (минимум 110 px).
            int rows = kind == PartKind.Engine ? 4 : 3;
            int editors = kind == PartKind.Engine ? 0 : (kind == PartKind.FuelTank ? 2 : 1);
            float rest = 44f + 14f + 50f + 8f + (rows * 26f)
                + (editors > 0 ? 10f + 26f + (editors * 62f) : 0f) + 16f;
            float budget = Mathf.Max(96f, r.height - rest - 16f);
            float previewH = Mathf.Min(w * 0.75f, budget, 320f);
            float previewW = previewH / 0.75f;
            var viewport = new Rect(r.center.x - (previewW * 0.5f), y, previewW, previewH);

            SetColor(new Color(0f, 0f, 0f, 0.45f));
            GUI.Box(Expand(viewport, 9f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(viewport, GUIContent.none, BuilderSkin.Viewport);

            Texture frame = preview.Texture;
            if (frame != null)
            {
                var inner = new Rect(viewport.x + 2f, viewport.y + 2f, viewport.width - 4f, viewport.height - 4f);
                GUI.DrawTexture(inner, frame, ScaleMode.StretchToFill);
                GUI.DrawTexture(inner, BuilderSkin.Vignette, ScaleMode.StretchToFill);
            }

            y = viewport.yMax + 14f;

            GUI.Label(new Rect(x, y, w, 24f), PartCatalog.DisplayName(kind), BuilderSkin.NameTextCenter);
            y += 26f;
            GUI.Label(new Rect(x, y, w, 18f), KindLabel(kind), BuilderSkin.MutedCenter);
            y += 24f;

            DrawSolid(new Rect(x, y, w, 1f), BuilderSkin.Line);
            y += 8f;

            AddRow(x, ref y, w, "Масса", FormatMass(m.MassKg));
            switch (kind)
            {
                case PartKind.Engine:
                    AddRow(x, ref y, w, "Тяга (вак.)", "2279 кН");
                    AddRow(x, ref y, w, "Удельный импульс", "452 с");
                    break;
                case PartKind.FuelTank:
                    double volume = System.Math.PI * m.Radius * m.Radius * m.Height;
                    AddRow(x, ref y, w, "Объём", volume.ToString("F1") + " м³");
                    break;
                case PartKind.Separator:
                    AddRow(x, ref y, w, "Толщина", "40 мм");
                    break;
            }

            AddRow(x, ref y, w, "Прочность стыка", (m.JointStrengthNewtons / 1e6).ToString("F1") + " МН");

            if (kind == PartKind.Engine)
            {
                return;
            }

            y += 10f;
            DrawHeading(new Rect(x, y, w, 16f), "РАЗМЕРЫ");
            y += 26f;

            if (kind == PartKind.FuelTank)
            {
                DrawEditor(x, ref y, w, "Длина бака", ref Builder.TankHeight,
                    PartCatalog.TankMinHeight, PartCatalog.TankMaxHeight, 10, "F1");
                DrawEditor(x, ref y, w, "Радиус бака", ref Builder.TankRadius,
                    PartCatalog.TankMinRadius, PartCatalog.TankMaxRadius, 20, "F2");
            }
            else
            {
                DrawEditor(x, ref y, w, "Радиус отделителя", ref Builder.SeparatorRadius,
                    PartCatalog.SeparatorMinRadius, PartCatalog.SeparatorMaxRadius, 30, "F2");
            }
        }

        private void DrawHeading(Rect rect, string text)
        {
            DrawSolid(new Rect(rect.x, rect.y + 2f, 3f, rect.height - 4f), BuilderSkin.Accent);
            GUI.Label(new Rect(rect.x + 10f, rect.y, rect.width - 10f, rect.height), text, BuilderSkin.Heading);
        }

        private static void AddRow(float x, ref float y, float w, string left, string right)
        {
            GUI.Label(new Rect(x, y, w * 0.55f, 24f), left, BuilderSkin.Muted);
            GUI.Label(new Rect(x + (w * 0.45f), y, w * 0.55f, 24f), right, BuilderSkin.BodyRight);
            y += 26f;
        }

        // Редактор размера: [««][‹]  значение  [›][»»]. Большие стрелки — 1 м,
        // малые — 0.1 м, удержание повторяет шаг.
        private void DrawEditor(float x, ref float y, float w, string label, ref float value,
            float min, float max, int idBase, string format)
        {
            GUI.Label(new Rect(x, y, w * 0.6f, 18f), label, BuilderSkin.Body);
            GUI.Label(new Rect(x + (w * 0.4f), y, w * 0.6f, 18f),
                min.ToString("0.##") + " – " + max.ToString("0.##") + " м", BuilderSkin.TinyRight);
            y += 22f;

            const float btn = 36f;
            const float gap = 4f;
            float cluster = (btn * 2f) + gap;
            float valueW = w - (cluster * 2f) - 16f;

            var bigDown = new Rect(x, y, btn, 34f);
            var smallDown = new Rect(x + btn + gap, y, btn, 34f);
            var box = new Rect(x + cluster + 8f, y, valueW, 34f);
            var smallUp = new Rect(box.xMax + 8f, y, btn, 34f);
            var bigUp = new Rect(smallUp.xMax + gap, y, btn, 34f);

            const float eps = 0.0001f;
            bool canDown = value > min + eps;
            bool canUp = value < max - eps;

            if (StepButton(bigDown, BuilderSkin.DoubleLeft, canDown, idBase, 0.14f))
            {
                value = Nudge(value, -StepBig, min, max);
            }

            if (StepButton(smallDown, BuilderSkin.ChevronLeft, canDown, idBase + 1, 0.05f))
            {
                value = Nudge(value, -StepSmall, min, max);
            }

            if (StepButton(smallUp, BuilderSkin.ChevronRight, canUp, idBase + 2, 0.05f))
            {
                value = Nudge(value, StepSmall, min, max);
            }

            if (StepButton(bigUp, BuilderSkin.DoubleRight, canUp, idBase + 3, 0.14f))
            {
                value = Nudge(value, StepBig, min, max);
            }

            // Значение + полоска положения в диапазоне.
            GUI.Box(box, GUIContent.none, BuilderSkin.ValueBox);
            GUI.Label(new Rect(box.x, box.y - 2f, box.width, box.height - 4f),
                value.ToString(format) + " м", BuilderSkin.ValueText);
            float t = Mathf.InverseLerp(min, max, value);
            var track = new Rect(box.x + 10f, box.yMax - 7f, box.width - 20f, 3f);
            DrawSolid(track, new Color(1f, 1f, 1f, 0.08f));
            DrawSolid(new Rect(track.x, track.y, Mathf.Max(3f, track.width * t), track.height), BuilderSkin.Accent);

            // Подписи шага под кнопками.
            GUI.Label(new Rect(bigDown.x, y + 36f, btn, 14f), "1 м", BuilderSkin.TinyCenter);
            GUI.Label(new Rect(smallDown.x, y + 36f, btn, 14f), "0.1 м", BuilderSkin.TinyCenter);
            GUI.Label(new Rect(smallUp.x, y + 36f, btn, 14f), "0.1 м", BuilderSkin.TinyCenter);
            GUI.Label(new Rect(bigUp.x, y + 36f, btn, 14f), "1 м", BuilderSkin.TinyCenter);

            y += 62f;
        }

        private static float Nudge(float value, float delta, float min, float max)
        {
            // Округление до сотых убирает накопление float-ошибки (0.1 + 0.1 + ...).
            return Mathf.Clamp(Mathf.Round((value + delta) * 100f) / 100f, min, max);
        }

        // Кнопка со стрелкой: срабатывает по нажатию, при удержании повторяется.
        private bool StepButton(Rect rect, Texture2D icon, bool enabled, int id, float repeat)
        {
            Event e = Event.current;
            bool inside = rect.Contains(e.mousePosition);
            bool pressed = enabled && holdId == id;
            bool hover = enabled && open && inside && dragKind < 0;

            GUIStyle style = !enabled ? BuilderSkin.StepDisabled
                : pressed ? BuilderSkin.StepActive
                : hover ? BuilderSkin.StepHover : BuilderSkin.Step;
            GUI.Box(rect, GUIContent.none, style);

            Color tint = !enabled ? new Color(0.55f, 0.56f, 0.58f, 0.30f)
                : pressed ? new Color(1f, 1f, 1f, 1f)
                : new Color(0.82f, 0.84f, 0.86f, 0.95f);
            SetColor(tint);
            GUI.DrawTexture(new Rect(rect.center.x - 13f, rect.center.y - 13f, 26f, 26f), icon);
            SetColor(Color.white);

            if (!enabled || !open)
            {
                return false;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && inside && dragKind < 0)
            {
                holdId = id;
                holdNextTime = Time.unscaledTime + 0.45f;
                e.Use();
                return true;
            }

            if (holdId == id && e.type == EventType.Repaint && Time.unscaledTime >= holdNextTime)
            {
                holdNextTime = Time.unscaledTime + repeat;
                return true;
            }

            return false;
        }

        private bool DrawIconButton(Rect rect, Texture2D icon, float iconSize)
        {
            Event e = Event.current;
            bool inside = rect.Contains(e.mousePosition);
            GUI.Box(rect, GUIContent.none, (open && inside) ? BuilderSkin.StepHover : BuilderSkin.Step);
            SetColor(new Color(0.85f, 0.87f, 0.89f, 0.95f));
            GUI.DrawTexture(new Rect(rect.center.x - (iconSize * 0.5f), rect.center.y - (iconSize * 0.5f), iconSize, iconSize), icon);
            SetColor(Color.white);

            if (open && e.type == EventType.MouseDown && e.button == 0 && inside && dragKind < 0)
            {
                e.Use();
                return true;
            }

            return false;
        }

        // ─── Сетка деталей ───────────────────────────────────────────────────

        private void DrawGrid(Rect r)
        {
            float x = r.x + 20f;
            float w = r.width - 40f;
            float y = r.y + 16f;

            DrawHeading(new Rect(x, y, w, 16f), "ДЕТАЛИ");
            GUI.Label(new Rect(x, y, w, 16f), "Поставлено: " + Builder.Parts.Count, BuilderSkin.TinyRight);
            y += 30f;

            const int columns = 3;
            const float gap = 14f;
            float cardW = (w - (gap * (columns - 1))) / columns;
            float cardH = Mathf.Clamp(r.height - 30f - 96f, 200f, 380f);
            Event e = Event.current;

            for (int i = 0; i < Catalog.Length; i++)
            {
                var card = new Rect(x + ((i % columns) * (cardW + gap)), y + ((i / columns) * (cardH + gap)), cardW, cardH);
                PartKind kind = Catalog[i];
                bool selected = statsIndex == i;
                bool hover = open && dragKind < 0 && card.Contains(e.mousePosition);

                if (selected)
                {
                    SetColor(new Color(0.30f, 0.55f, 0.85f, 0.45f));
                    GUI.Box(Expand(card, 16f), GUIContent.none, BuilderSkin.Glow);
                    SetColor(Color.white);
                }

                GUI.Box(card, GUIContent.none, selected ? BuilderSkin.CardSelected : hover ? BuilderSkin.CardHover : BuilderSkin.Card);

                var iconBox = new Rect(card.x + 10f, card.y + 10f, card.width - 20f, card.height - 78f);
                GUI.Box(iconBox, GUIContent.none, BuilderSkin.Slot);
                float iconSize = Mathf.Min(iconBox.width, iconBox.height) * 0.88f;
                GUI.DrawTexture(new Rect(iconBox.center.x - (iconSize * 0.5f), iconBox.center.y - (iconSize * 0.5f), iconSize, iconSize),
                    PartIcons.Get(kind), ScaleMode.ScaleToFit);

                // Бейдж «в панели»: номер слота.
                int slot = FindInHotbar(kind);
                if (slot >= 0)
                {
                    var badge = new Rect(iconBox.xMax - 28f, iconBox.y + 8f, 20f, 20f);
                    GUI.Box(badge, GUIContent.none, BuilderSkin.Key);
                    GUI.Label(badge, (slot + 1).ToString(), BuilderSkin.KeyText);
                }

                GUI.Label(new Rect(card.x + 6f, card.yMax - 62f, card.width - 12f, 24f), PartCatalog.DisplayName(kind), BuilderSkin.CardName);
                GUI.Label(new Rect(card.x + 6f, card.yMax - 36f, card.width - 12f, 20f), KindLabel(kind), BuilderSkin.CardKind);

                if (open && e.type == EventType.MouseDown && e.button == 0 && dragKind < 0 && card.Contains(e.mousePosition))
                {
                    statsIndex = i;
                    if (e.clickCount >= 2)
                    {
                        AssignToHotbar(kind);        // двойной клик — в свободный слот
                    }
                    else
                    {
                        dragKind = (int)kind;
                        dragFromHotbar = -1;
                        dragOrigin = e.mousePosition;
                        dragMoved = false;
                    }

                    e.Use();
                }
            }

            // Низ панели: подсказка и «Очистить».
            GUI.Label(new Rect(x, r.yMax - 76f, w, 20f), "Перетащите деталь в нижнюю панель.", BuilderSkin.Muted);
            GUI.Label(new Rect(x, r.yMax - 56f, w, 20f), "Двойной клик — в свободный слот.", BuilderSkin.Tiny);

            var clear = new Rect(r.xMax - 20f - 186f, r.yMax - 16f - 36f, 186f, 36f);
            if (DrawTextButton(clear, "Очистить площадку", BuilderSkin.Danger, BuilderSkin.DangerHover))
            {
                Builder.ClearAll();
            }
        }

        private bool DrawTextButton(Rect rect, string text, GUIStyle normal, GUIStyle hover)
        {
            Event e = Event.current;
            bool inside = rect.Contains(e.mousePosition);
            GUI.Box(rect, GUIContent.none, (open && inside) ? hover : normal);
            GUI.Label(rect, text, BuilderSkin.DangerButtonText);

            if (open && e.type == EventType.MouseDown && e.button == 0 && inside && dragKind < 0)
            {
                e.Use();
                return true;
            }

            return false;
        }

        private int FindInHotbar(PartKind kind)
        {
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (hotbar[i].HasValue && hotbar[i].Value == kind)
                {
                    return i;
                }
            }

            return -1;
        }

        private void AssignToHotbar(PartKind kind)
        {
            int existing = FindInHotbar(kind);
            if (existing >= 0)
            {
                SetHotbarIndex(existing);
                return;
            }

            int target = -1;
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (!hotbar[i].HasValue)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                target = hotbarIndex >= 0 ? hotbarIndex : 0;
            }

            hotbar[target] = kind;
            SetHotbarIndex(target);
        }

        // ─── Перетаскивание ──────────────────────────────────────────────────

        private void HandleDragRelease()
        {
            Event current = Event.current;
            if (dragKind >= 0 && !dragMoved && (current.mousePosition - dragOrigin).sqrMagnitude > DragThreshold * DragThreshold)
            {
                dragMoved = true;                    // сдвинули — это уже перетаскивание, не клик
            }

            if (current.type != EventType.MouseUp || dragKind < 0)
            {
                return;
            }

            if (!open)
            {
                ResetDrag();
                return;
            }

            if (dragMoved)
            {
                var kind = (PartKind)dragKind;
                bool dropped = false;
                for (int i = 0; i < hotbar.Length; i++)
                {
                    if (!HotbarSlotRect(i).Contains(current.mousePosition))
                    {
                        continue;
                    }

                    PartKind? previous = hotbar[i];
                    hotbar[i] = kind;
                    if (dragFromHotbar >= 0 && dragFromHotbar != i)
                    {
                        hotbar[dragFromHotbar] = previous;   // перенос = обмен местами
                    }

                    SetHotbarIndex(i);
                    dropped = true;
                    break;
                }

                if (!dropped && dragFromHotbar >= 0)
                {
                    hotbar[dragFromHotbar] = null;           // бросили мимо — убрать из панели
                    ApplySelection();
                }
            }

            ResetDrag();
            current.Use();
        }

        private void ResetDrag()
        {
            dragKind = -1;
            dragFromHotbar = -1;
            dragMoved = false;
        }

        private void DrawDragIcon()
        {
            if (dragKind < 0 || !open || !dragMoved)
            {
                return;
            }

            Vector2 mouse = Event.current.mousePosition;
            var rect = new Rect(mouse.x - 30f, mouse.y - 30f, 60f, 60f);
            SetColor(new Color(0.30f, 0.55f, 0.85f, 0.50f));
            GUI.Box(Expand(rect, 20f), GUIContent.none, BuilderSkin.Glow);
            SetColor(new Color(1f, 1f, 1f, 0.95f));
            GUI.DrawTexture(rect, PartIcons.Get((PartKind)dragKind), ScaleMode.ScaleToFit);
            SetColor(Color.white);
        }

        // ─── Мелочи ──────────────────────────────────────────────────────────

        private static string KindLabel(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.Engine: return "Двигатель";
                case PartKind.FuelTank: return "Ёмкость · настраиваемая";
                default: return "Разделение ступеней";
            }
        }

        private static string FormatMass(double kg)
        {
            return kg >= 1000d ? (kg / 1000d).ToString("F1") + " т" : kg.ToString("F0") + " кг";
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + (amount * 2f), rect.height + (amount * 2f));
        }

        // Единая точка установки цвета: учитывает прозрачность анимации окна.
        private void SetColor(Color color)
        {
            GUI.color = new Color(color.r, color.g, color.b, color.a * uiAlpha);
        }

        private void DrawSolid(Rect rect, Color color)
        {
            SetColor(color);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            SetColor(Color.white);
        }

        private void DrawOutline(Rect rect, float thickness, Color color)
        {
            DrawSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
