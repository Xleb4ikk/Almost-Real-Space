using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// ИНТЕРФЕЙС СТРОИТЕЛЬСТВА (IMGUI поверх BuilderSkin, светлая серая тема).
    ///
    /// Хотбар: 9 слотов снизу по центру на светлой подложке. Предметы
    /// перетаскиваются сюда из инвентаря (ЛКМ, с порогом сдвига — простой клик
    /// только выбирает деталь). Двойной клик по карточке — в свободный слот.
    /// ПКМ по слоту — МИНИ-МЕНЮ слота (в том же стиле, что инвентарь): быстрые
    /// размеры детали прямо у панели, а если размер менять нельзя (двигатель) —
    /// краткие характеристики. Убрать деталь из панели — вытащить её из слота
    /// и отпустить мимо панели. Выбор слота — колесо или цифры 1..9; выбранный
    /// слот приподнят, обведён ярким квадратом со свечением, над панелью —
    /// плашка с названием и размерами.
    ///
    /// РАЗМЕРЫ ХРАНЯТСЯ В СЛОТЕ (PartPreset): у каждого слота своя заготовка
    /// детали — форма бака и его радиусы/длина. В панели удобно заранее
    /// подготовить баки разных диаметров (например, цилиндр 2.5 м и конус
    /// 5→2.5 м) и ставить их по очереди, не перенастраивая размеры заново.
    /// Иконка слота — отрисовка ИМЕННО его заготовки (форма и пропорции).
    ///
    /// ПРАВКИ РАЗМЕРОВ идут только в ЯВНО выбранный слот (клик по слоту) либо
    /// в ЧЕРНОВИК вида (клик по карточке — «заготовка на следующую постановку»):
    /// перетаскивание копирует заготовку в слот и не меняет соседние.
    ///
    /// Инвентарь (E, только в стройке): окно ~70% экрана — слева 3D-превью
    /// выбранной детали (крутится, размеры учитываются) и характеристики,
    /// справа сетка карточек. Размеры меняются кнопками-стрелками: большие
    /// «двойные» — на 1 м, одиночные — на 0.1 м (удержание повторяет шаг).
    /// Размеры сразу меняют призрак и поставленную деталь, а также превью.
    /// Столбец характеристик ПРОКРУЧИВАЕТСЯ (колесо/бегунок): у конусного
    /// бака строк больше, чем влезает, а окно ради этого не раздувается.
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

        /// <summary>Минимальная высота окна инвентаря.</summary>
        private const float WindowMinHeight = 540f;

        /// <summary>Шаг прокрутки колесом в столбце характеристик, px на щелчок.</summary>
        private const float StatsScrollStep = 42f;

        // Шаги кнопок-стрелок, м.
        private const float StepBig = 1f;
        private const float StepSmall = 0.1f;

        /// <summary>
        /// Слот панели: своя заготовка детали и иконка ИМЕННО этой заготовки
        /// (форма/пропорции). Иконка перерисовывается, когда размеры изменились.
        /// </summary>
        private sealed class HotSlot
        {
            public bool Occupied;
            public PartPreset Preset;
            public Texture2D Icon;
            public bool IconOwned;      // своя отрисовка — уничтожается при замене
            public bool IconDirty;      // размеры изменились: перерисовать
        }

        private readonly HotSlot[] hotbar = CreateHotbar();

        /// <summary>
        /// Черновики видов («заготовка на следующую постановку»): их правит окно,
        /// когда выбранный слот — не этот вид. Правка черновика не трогает
        /// уже поставленные в панель детали.
        /// </summary>
        private readonly PartPreset[] drafts = new PartPreset[Catalog.Length];

        /// <summary>
        /// Слот, который правит окно (выбран в панели). null — правится черновик:
        /// клик по карточке в инвентаре переключает на черновик, клик по слоту —
        /// на слот.
        /// </summary>
        private HotSlot editSlot;

        private readonly BuilderPreview preview = new BuilderPreview();
        private int hotbarIndex = -1;
        private bool open;
        private float openAnim;
        private int statsIndex = -1;

        private PartPreset? dragPreset;
        private Texture2D dragIcon;
        private int dragFromHotbar = -1;
        private Vector2 dragOrigin;
        private bool dragMoved;

        private int holdId = -1;
        private float holdNextTime;

        // Прокрутка столбца характеристик (конусному баку не хватает высоты).
        private float statsScroll;
        private bool statsScrollDrag;
        private float statsScrollDragOrigin;
        private float statsScrollDragStart;

        /// <summary>Курсор над столбцом характеристик (ставит OnGUI): колесо листает его, а не слоты.</summary>
        private bool statsHovered;

        // Мини-меню слота (ПКМ по слоту): быстрые размеры у панели.
        private const float SlotMenuWidth = 320f;
        private const int SlotMenuIdBase = 100;      // не пересекается с id окна (10..43)
        private int slotMenuIndex = -1;

        /// <summary>
        /// Активны ли элементы управления ТОЙ панели, что рисуется сейчас:
        /// окно инвентаря инертно, пока открыто мини-меню слота (и наоборот).
        /// </summary>
        private bool controlsActive;

        private bool cursorWasLocked = true;
        private float uiWidth = 1920f;
        private float uiHeight = 1080f;
        private float uiAlpha = 1f;

        /// <summary>Панель подсказок по клавишам скрыта (H или крестик на панели).</summary>
        private static bool hintsHidden;

        private static HotSlot[] CreateHotbar()
        {
            var slots = new HotSlot[9];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new HotSlot();
            }

            return slots;
        }

        private void Start()
        {
            // Иконки видов — 3D-рендеры под углом (тот же студийный свет, что
            // у превью): без этого в слотах и карточках были плоские проекции.
            if (Builder != null)
            {
                preview.PrepareIcons(Builder.Engine, Builder.TankSideMaterial,
                    Builder.TankCapMaterial, Builder.SeparatorMaterial);

                for (int i = 0; i < Catalog.Length; i++)
                {
                    drafts[i] = Builder.DefaultPreset(Catalog[i]);
                }
            }

            // Стартовая палитра в панели: строить можно сразу, но перетаскивание
            // из инвентаря работает как обычно (слоты можно менять местами).
            if (Builder != null)
            {
                AssignPreset(0, Builder.DefaultPreset(PartKind.Engine));
                AssignPreset(1, Builder.DefaultPreset(PartKind.FuelTank));
                AssignPreset(2, Builder.DefaultPreset(PartKind.Separator));
            }

            hotbarIndex = 0;
            ApplySelection();
            editSlot = hotbar[0].Occupied ? hotbar[0] : null;
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

            // Колесо над столбцом характеристик листает его (OnGUI), а не слоты:
            // иначе прокрутка размеров заодно переключала бы выбранный слот.
            float scroll = PlayerInput.MouseScroll();
            if (Mathf.Abs(scroll) > 0.05f && !statsHovered)
            {
                int step = scroll > 0f ? -1 : 1;
                SetHotbarIndex(((hotbarIndex + step) % hotbar.Length + hotbar.Length) % hotbar.Length);
            }
        }

        private void SetHotbarIndex(int index)
        {
            hotbarIndex = index;
            ApplySelection();
            // Выбрали слот — окно правит ЕГО; иначе правится черновик вида.
            editSlot = index >= 0 && hotbar[index].Occupied ? hotbar[index] : null;
        }

        private void ApplySelection()
        {
            HotSlot slot = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
            bool has = slot != null && slot.Occupied;
            Builder.HasSelection = has;
            if (has)
            {
                Builder.SelectedPreset = slot.Preset;
            }
        }

        private void SetOpen(bool value)
        {
            open = value;
            holdId = -1;
            if (!value)
            {
                // Окно и мини-меню слота живут вместе: закрылось окно — закрыт и
                // он (курсор и ввод возвращает окно).
                slotMenuIndex = -1;
            }

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
                    HotSlot current = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
                    statsIndex = current != null && current.Occupied
                        ? System.Array.IndexOf(Catalog, current.Preset.Kind)
                        : 0;
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

            ResetDrag();
            statsHovered = false;
            statsScrollDrag = false;
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
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (hotbar[i].IconOwned && hotbar[i].Icon != null)
                {
                    Destroy(hotbar[i].Icon);
                }
            }
        }

        // ─── 3D-предпросмотр ─────────────────────────────────────────────────

        /// <summary>
        /// Раз в кадр: показать в превью выбранную деталь с текущими размерами
        /// и дорисовать «поздние» иконки слотов. Визуал пересобирается при смене
        /// детали или размера, камера кадрирует по габаритам, кадр рендерится
        /// не чаще 30 Гц (внутри Ensure).
        /// </summary>
        private void TickPreview()
        {
            if (Builder == null)
            {
                return;
            }

            // Иконки слотов с изменёнными размерами: не больше одной отрисовки
            // за кадр (размеры правятся удержанием стрелок — рендер не копится).
            // Работает и при ЗАКРЫТОМ окне: слот могли получить перетаскиванием,
            // а окно сразу закрыть — иконка всё равно должна стать точной.
            for (int i = 0; i < hotbar.Length; i++)
            {
                HotSlot slot = hotbar[i];
                if (slot.Occupied && slot.IconDirty && CaptureSlotIcon(slot))
                {
                    break;
                }
            }

            if (openAnim <= 0.001f)
            {
                return;
            }

            PartPreset preset = StatsPreset();
            preview.Ensure(preset.Kind, Builder.MetricsFor(preset), Builder.Engine,
                Builder.TankSideMaterial, Builder.TankCapMaterial, Builder.SeparatorMaterial);
        }

        /// <summary>Перерисовать иконку слота под его текущие размеры.</summary>
        private bool CaptureSlotIcon(HotSlot slot)
        {
            Texture2D texture = preview.CaptureIcon(slot.Preset.Kind, Builder.MetricsFor(slot.Preset),
                Builder.Engine, Builder.TankSideMaterial, Builder.TankCapMaterial, Builder.SeparatorMaterial);
            if (texture != null)
            {
                SetIcon(slot, texture, owned: true);
            }

            // Рендер не удался (нет рига/геометрии) — не зацикливаемся, остаётся
            // прежняя картинка (дефолтная отрисовка вида).
            slot.IconDirty = false;
            return true;
        }

        /// <summary>
        /// Заготовка, которую показывает и правит окно: заготовка явно
        /// выбранного слота этого вида, иначе — черновик вида (заготовка
        /// на следующую постановку).
        /// </summary>
        private PartPreset StatsPreset()
        {
            int card = StatsCardIndex();
            HotSlot owner = EditSlotFor(Catalog[card]);
            return owner != null ? owner.Preset : drafts[card];
        }

        /// <summary>Индекс карточки характеристик (всегда валидный).</summary>
        private int StatsCardIndex()
        {
            if (statsIndex >= 0 && statsIndex < Catalog.Length)
            {
                return statsIndex;
            }

            if (hotbarIndex >= 0 && hotbar[hotbarIndex].Occupied)
            {
                int found = System.Array.IndexOf(Catalog, hotbar[hotbarIndex].Preset.Kind);
                if (found >= 0)
                {
                    return found;
                }
            }

            return 0;
        }

        /// <summary>
        /// Слот, который правит окно: ТОЛЬКО явно выбранный слот этого вида.
        /// Иначе — null (правится черновик): правка не должна менять соседние
        /// слоты, которые игрок не выбирал.
        /// </summary>
        private HotSlot EditSlotFor(PartKind kind)
        {
            return editSlot != null && editSlot.Occupied && editSlot.Preset.Kind == kind ? editSlot : null;
        }

        // ─── Слоты ───────────────────────────────────────────────────────────

        /// <summary>Идёт перетаскивание детали (мышь занята).</summary>
        private bool Dragging => dragPreset.HasValue;

        /// <summary>Интерфейс активен: открыто окно инвентаря или мини-меню слота.</summary>
        private bool Interactive => open || slotMenuIndex >= 0;

        /// <summary>
        /// Положить заготовку в слот. Иконка сразу — дефолтная отрисовка вида,
        /// точная (под размеры заготовки) дорисуется в TickPreview.
        /// </summary>
        private void AssignPreset(int index, PartPreset preset)
        {
            HotSlot slot = hotbar[index];
            SetIcon(slot, PartIcons.Get(preset.Kind), owned: false);
            slot.Preset = preset;
            slot.Occupied = true;
            slot.IconDirty = true;
        }

        /// <summary>Очистить слот (ПКМ по слоту / бросили мимо панели).</summary>
        private void ClearSlot(int index)
        {
            HotSlot slot = hotbar[index];
            if (editSlot == slot)
            {
                editSlot = null;                      // окно переходит на черновик
            }

            SetIcon(slot, null, owned: false);
            slot.Preset = default;
            slot.Occupied = false;
            slot.IconDirty = false;
        }

        /// <summary>Поменять два слота местами (перенос = обмен).</summary>
        private void SwapSlots(int a, int b)
        {
            HotSlot first = hotbar[a];
            hotbar[a] = hotbar[b];
            hotbar[b] = first;
        }

        private static void SetIcon(HotSlot slot, Texture2D texture, bool owned)
        {
            if (slot.IconOwned && slot.Icon != null && slot.Icon != texture)
            {
                Destroy(slot.Icon);
            }

            slot.Icon = texture;
            slot.IconOwned = owned;
        }

        /// <summary>Записать правку заготовки: в слот-владелец или в черновик вида.</summary>
        private void ApplyPresetEdit(HotSlot owner, int card, PartPreset preset)
        {
            if (owner != null)
            {
                owner.Preset = preset;
                owner.IconDirty = true;
                if (hotbarIndex >= 0 && hotbar[hotbarIndex] == owner)
                {
                    Builder.SelectedPreset = preset;   // призрак и постановка — сразу
                }
            }
            else
            {
                drafts[card] = preset;
            }
        }

        private static bool SamePreset(PartPreset a, PartPreset b)
        {
            return a.Kind == b.Kind
                && a.Form == b.Form
                && Mathf.Abs(a.Radius - b.Radius) < 1e-4f
                && Mathf.Abs(a.TopRadius - b.TopRadius) < 1e-4f
                && Mathf.Abs(a.Height - b.Height) < 1e-4f;
        }

        // ─── Мини-меню слота (ПКМ) ───────────────────────────────────────────

        /// <summary>ПКМ по слоту: открыть мини-меню размеров (или закрыть его).</summary>
        private void ToggleSlotMenu(int index)
        {
            if (slotMenuIndex == index)
            {
                slotMenuIndex = -1;
                holdId = -1;
                return;
            }

            slotMenuIndex = hotbar[index].Occupied ? index : -1;
            holdId = -1;
        }

        /// <summary>
        /// Мини-меню слота: быстрые размеры детали прямо у панели (те же
        /// редакторы, что в инвентаре), а у двигателя — краткие характеристики.
        /// Живёт, пока открыто окно инвентаря: курсор и ввод общие, а элементы
        /// окна под меню становятся инертными (controlsActive), поэтому клики
        /// достаются меню.
        /// </summary>
        private void DrawSlotMenu()
        {
            if (slotMenuIndex < 0 || !open)
            {
                return;
            }

            HotSlot slot = hotbar[slotMenuIndex];
            if (!slot.Occupied)
            {
                slotMenuIndex = -1;
                return;
            }

            int card = System.Array.IndexOf(Catalog, slot.Preset.Kind);
            if (card < 0)
            {
                slotMenuIndex = -1;
                return;
            }

            PartKind kind = slot.Preset.Kind;
            bool tank = kind == PartKind.FuelTank;
            bool hasSize = kind != PartKind.Engine;
            int editors = tank ? (slot.Preset.Form == TankForm.Cone ? 3 : 2) : 1;
            float bodyH = hasSize
                ? (tank ? ShapeRowHeight : 0f) + (editors * EditorHeight)
                : 3f * 26f;
            float menuH = 46f + bodyH + 16f;

            Rect slotRect = HotbarSlotRect(slotMenuIndex);
            float x = Mathf.Clamp(slotRect.center.x - (SlotMenuWidth * 0.5f), 12f, uiWidth - SlotMenuWidth - 12f);
            float y = Mathf.Max(12f, PlateRect().y - menuH - 50f);
            var menu = new Rect(x, y, SlotMenuWidth, menuH);

            // Клик ЛКМ мимо меню закрывает его (кнопки меню событие не съедают).
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !menu.Contains(e.mousePosition))
            {
                slotMenuIndex = -1;
                holdId = -1;
                return;
            }

            SetColor(BuilderSkin.Shadow);
            GUI.Box(Expand(menu, 16f), GUIContent.none, BuilderSkin.Glow);
            SetColor(Color.white);
            GUI.Box(menu, GUIContent.none, BuilderSkin.Window);

            controlsActive = true;                    // меню в фокусе: окно инертно
            float px = menu.x + 16f;
            float pw = menu.width - 32f;
            float py = menu.y + 12f;

            GUI.Label(new Rect(px, py, pw, 22f), PartCatalog.DisplayName(kind), BuilderSkin.NameText);
            py += 26f;
            DrawSolid(new Rect(px, py, pw, 1f), BuilderSkin.Line);
            py += 8f;

            PartPreset preset = slot.Preset;
            PartPreset before = preset;
            if (tank)
            {
                DrawShapeRow(px, ref py, pw, ref preset, slot, card);
                bool cone = preset.Form == TankForm.Cone;
                DrawEditor(px, ref py, pw, "Длина бака", ref preset.Height,
                    PartCatalog.TankMinHeight, PartCatalog.TankMaxHeight, SlotMenuIdBase, "F1");
                DrawEditor(px, ref py, pw, cone ? "Нижний радиус" : "Радиус бака", ref preset.Radius,
                    PartCatalog.TankMinRadius, PartCatalog.TankMaxRadius, SlotMenuIdBase + 10, "F1");
                if (cone)
                {
                    DrawEditor(px, ref py, pw, "Верхний радиус", ref preset.TopRadius,
                        PartCatalog.TankMinRadius, PartCatalog.TankMaxRadius, SlotMenuIdBase + 20, "F1");
                }
            }
            else if (hasSize)
            {
                DrawEditor(px, ref py, pw, "Радиус отделителя", ref preset.Radius,
                    PartCatalog.SeparatorMinRadius, PartCatalog.SeparatorMaxRadius, SlotMenuIdBase, "F1");
            }
            else
            {
                PartMetrics m = Builder.MetricsFor(preset);
                AddRow(px, ref py, pw, "Масса", FormatMass(m.MassKg));
                AddRow(px, ref py, pw, "Тяга (вак.)", "2279 кН");
                AddRow(px, ref py, pw, "Удельный импульс", "452 с");
            }

            if (!SamePreset(before, preset))
            {
                ApplyPresetEdit(slot, card, preset);
            }

            controlsActive = open;
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

                DrawSlotMenu();
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
                bool hover = open && !Dragging && rect.Contains(mouse);
                HotSlot slot = hotbar[i];

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

                if (slot.Occupied)
                {
                    float inset = selected ? 6f : 8f;
                    var icon = new Rect(rect.x + inset, rect.y + inset, rect.width - (inset * 2f), rect.height - (inset * 2f));
                    bool lifted = dragMoved && dragFromHotbar == i;
                    SetColor(new Color(1f, 1f, 1f, lifted ? 0.3f : 1f));
                    Texture2D picture = slot.Icon != null ? slot.Icon : PartIcons.Get(slot.Preset.Kind);
                    GUI.DrawTexture(icon, picture, ScaleMode.ScaleToFit);
                    SetColor(Color.white);
                }

                GUI.Label(new Rect(rect.x + 6f, rect.y + 3f, 20f, 14f), (i + 1).ToString(),
                    selected ? BuilderSkin.Body : BuilderSkin.Tiny);

                if (open && current.type == EventType.MouseDown && rect.Contains(mouse) && !Dragging)
                {
                    if (current.button == 1)
                    {
                        // ПКМ по слоту — мини-меню размеров (повторный ПКМ закрывает).
                        ToggleSlotMenu(i);
                    }
                    else if (current.button == 0)
                    {
                        SetHotbarIndex(i);
                        if (slot.Occupied)
                        {
                            statsIndex = System.Array.IndexOf(Catalog, slot.Preset.Kind);
                            dragPreset = slot.Preset;
                            dragIcon = slot.Icon;
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
            HotSlot slot = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
            if (slot == null || !slot.Occupied)
            {
                return;
            }

            PartPreset preset = slot.Preset;
            PartMetrics m = Builder.MetricsFor(preset);
            string dims = string.Empty;
            if (preset.Kind == PartKind.FuelTank)
            {
                dims = "   <color=#9AA0A8>L " + m.Height.ToString("F1") + " м  ·  R " + m.Radius.ToString("F1") + " м</color>";
                if (IsCone(m))
                {
                    dims = "   <color=#9AA0A8>L " + m.Height.ToString("F1") + " м  ·  Rниз "
                        + m.Radius.ToString("F1") + " м  ·  Rверх " + m.TopRadius.ToString("F1") + " м</color>";
                }
            }
            else if (preset.Kind == PartKind.Separator)
            {
                dims = "   <color=#9AA0A8>R " + m.Radius.ToString("F1") + " м</color>";
            }

            string name = PartCatalog.DisplayName(preset.Kind);
            if (preset.Kind == PartKind.FuelTank && IsCone(m))
            {
                name += " · конус";
            }

            string text = "<b>" + name + "</b>" + dims;
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
                new[] { "ПКМ", "слот" },
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
                "Размеры слота",
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

            // Пока открыто мини-меню слота, элементы окна инертны: клики под
            // меню должны доставаться меню, а не карточкам/редакторам окна.
            controlsActive = open && slotMenuIndex < 0;

            // Окно не выходит за экран (зажимается по вьюпорту) и не раздувается
            // под содержимое: чего не влезло — листается прокруткой столбца
            // характеристик.
            float winW = Mathf.Min(uiWidth - 16f, Mathf.Max(760f, uiWidth * WindowFraction));
            float winH = Mathf.Min(uiHeight - 24f, Mathf.Max(WindowMinHeight, uiHeight * WindowFraction));
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
            // Узкий экран: характеристики ужимаются, но сетке остаётся место.
            statsW = Mathf.Min(statsW, win.width - 160f);
            var stats = new Rect(win.x + 18f, bodyY, statsW, bodyH);
            var grid = new Rect(stats.xMax + 14f, bodyY, win.xMax - 18f - (stats.xMax + 14f), bodyH);

            GUI.Box(stats, GUIContent.none, BuilderSkin.PanelInner);
            GUI.Box(grid, GUIContent.none, BuilderSkin.PanelInner);
            DrawStats(stats);
            DrawGrid(grid);

            uiAlpha = 1f;
            GUI.color = Color.white;
        }

        /// <summary>
        /// Панель характеристик с ПРОКРУТКОЙ: у конусного бака строк и редакторов
        /// больше, чем влезает в окно, — окно не раздуваем, контент листается
        /// колесом или бегунком у правого края.
        /// </summary>
        private void DrawStats(Rect r)
        {
            statsHovered = r.Contains(Event.current.mousePosition);
            StatsLayout layout = ComputeStatsLayout(r.width, r.height);
            float maxScroll = Mathf.Max(0f, layout.ContentHeight - r.height);
            if (maxScroll <= 0f)
            {
                statsScroll = 0f;
            }

            statsScroll = Mathf.Clamp(statsScroll, 0f, maxScroll);
            HandleStatsScroll(r, maxScroll);

            GUI.BeginGroup(r);
            DrawStatsContent(r.width, layout, statsScroll);
            GUI.EndGroup();

            if (maxScroll > 0f)
            {
                DrawStatsScrollbar(r, layout.ContentHeight, maxScroll);
            }
        }

        /// <summary>Раскладка столбца характеристик: что рисуем и сколько это займёт.</summary>
        private struct StatsLayout
        {
            public int Card;
            public PartKind Kind;
            public HotSlot Owner;
            public PartPreset Preset;
            public PartMetrics Metrics;
            public bool Tank;
            public int Rows;
            public int Editors;
            public float PreviewH;
            public float ContentHeight;
        }

        private StatsLayout ComputeStatsLayout(float width, float viewHeight)
        {
            int card = StatsCardIndex();
            PartKind kind = Catalog[card];
            HotSlot owner = EditSlotFor(kind);
            PartPreset preset = owner != null ? owner.Preset : drafts[card];
            PartMetrics m = Builder.MetricsFor(preset);

            bool tank = kind == PartKind.FuelTank;
            int rows = kind == PartKind.Engine ? 4 : 3;
            int editors = kind == PartKind.Engine ? 0 : (tank ? (IsCone(m) ? 3 : 2) : 1);

            // Всё, кроме кадра: шапка, подписи, строки, форма и редакторы.
            // Редактор размера занимает подпись + ряд кнопок (EditorHeight) —
            // без подписи высота контента занижалась и низ не долистывался.
            float rest = 16f + 28f + 14f + 26f + 24f + 8f + (rows * 26f)
                + (editors > 0 ? 10f + 26f + (tank ? ShapeRowHeight : 0f) + (editors * EditorHeight) : 0f)
                + 16f;
            float w = width - 40f;
            // Кадр превью — ОДИНАКОВЫЙ для всех деталей и не зависит от числа
            // строк: у бака строк и редакторов больше, и подгонка кадра делала
            // его «иконку» заметно мельче, чем у двигателя/отделителя. Если
            // контент не влез — столбец листается прокруткой.
            float previewH = Mathf.Clamp(w * 0.75f, 120f, 260f);

            return new StatsLayout
            {
                Card = card,
                Kind = kind,
                Owner = owner,
                Preset = preset,
                Metrics = m,
                Tank = tank,
                Rows = rows,
                Editors = editors,
                PreviewH = previewH,
                ContentHeight = rest + previewH,
            };
        }

        // Контент рисуется в ЛОКАЛЬНЫХ координатах группы панели; scroll —
        // сколько пикселей контента уже уехало вверх.
        private void DrawStatsContent(float width, StatsLayout layout, float scroll)
        {
            float x = 20f;
            float w = width - 40f;
            float y = 16f - scroll;

            DrawHeading(new Rect(x, y, w, 16f), "ХАРАКТЕРИСТИКИ");
            y += 28f;

            PartKind kind = layout.Kind;
            PartMetrics m = layout.Metrics;
            float previewW = layout.PreviewH / 0.75f;
            var viewport = new Rect((width - previewW) * 0.5f, y, previewW, layout.PreviewH);

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
                    double volume = ProceduralFuelTank.Volume(m.Radius, m.TopRadius, m.Height);
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

            PartPreset preset = layout.Preset;
            if (kind == PartKind.FuelTank)
            {
                // Правки копятся в заготовке и пишутся в слот-владелец/черновик
                // одним разом — иначе каждый шаг стрелки дёргал бы иконку.
                PartPreset before = preset;
                DrawShapeRow(x, ref y, w, ref preset, layout.Owner, layout.Card);
                bool cone = preset.Form == TankForm.Cone;
                DrawEditor(x, ref y, w, "Длина бака", ref preset.Height,
                    PartCatalog.TankMinHeight, PartCatalog.TankMaxHeight, 10, "F1");
                DrawEditor(x, ref y, w, cone ? "Нижний радиус" : "Радиус бака", ref preset.Radius,
                    PartCatalog.TankMinRadius, PartCatalog.TankMaxRadius, 20, "F1");
                if (cone)
                {
                    DrawEditor(x, ref y, w, "Верхний радиус", ref preset.TopRadius,
                        PartCatalog.TankMinRadius, PartCatalog.TankMaxRadius, 30, "F1");
                }

                if (!SamePreset(before, preset))
                {
                    ApplyPresetEdit(layout.Owner, layout.Card, preset);
                }
            }
            else
            {
                PartPreset before = preset;
                DrawEditor(x, ref y, w, "Радиус отделителя", ref preset.Radius,
                    PartCatalog.SeparatorMinRadius, PartCatalog.SeparatorMaxRadius, 40, "F1");
                if (!SamePreset(before, preset))
                {
                    ApplyPresetEdit(layout.Owner, layout.Card, preset);
                }
            }
        }

        /// <summary>Колесо над панелью характеристик листает столбец.</summary>
        private void HandleStatsScroll(Rect r, float maxScroll)
        {
            if (maxScroll <= 0f)
            {
                return;
            }

            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition))
            {
                // Знак: колесо «от себя» (delta.y > 0) листает вниз, к нижним
                // строкам — как в окнах Unity.
                statsScroll = Mathf.Clamp(statsScroll + (e.delta.y * StatsScrollStep), 0f, maxScroll);
                e.Use();
            }
        }

        // Бегунок у правого края панели: полоска в поле панели и перетаскивание.
        private void DrawStatsScrollbar(Rect r, float contentHeight, float maxScroll)
        {
            var track = new Rect(r.xMax - 11f, r.y + 6f, 5f, r.height - 12f);
            DrawSolid(track, new Color(1f, 1f, 1f, 0.06f));

            float thumbH = Mathf.Max(30f, track.height * (r.height / contentHeight));
            float t = maxScroll > 0f ? statsScroll / maxScroll : 0f;
            var thumb = new Rect(track.x - 1f, Mathf.Lerp(track.y, track.yMax - thumbH, t), track.width + 2f, thumbH);

            Event e = Event.current;
            bool hover = controlsActive && thumb.Contains(e.mousePosition);
            if (controlsActive && e.type == EventType.MouseDown && e.button == 0 && hover && !Dragging)
            {
                statsScrollDrag = true;
                statsScrollDragOrigin = e.mousePosition.y;
                statsScrollDragStart = statsScroll;
                e.Use();
            }

            if (statsScrollDrag && e.type == EventType.MouseDrag)
            {
                float span = track.height - thumbH;
                if (span > 1f)
                {
                    float dy = e.mousePosition.y - statsScrollDragOrigin;
                    statsScroll = Mathf.Clamp(statsScrollDragStart + ((dy / span) * maxScroll), 0f, maxScroll);
                }

                e.Use();
            }

            if (e.type == EventType.MouseUp)
            {
                statsScrollDrag = false;
            }

            DrawSolid(thumb, hover || statsScrollDrag ? BuilderSkin.Accent : new Color(1f, 1f, 1f, 0.28f));
        }

        /// <summary>Высота строки выбора формы бака (для расчёта кадра превью).</summary>
        private const float ShapeRowHeight = 66f;

        /// <summary>Высота редактора размера: подпись (22) + ряд кнопок (62).</summary>
        private const float EditorHeight = 84f;

        // Строка «Форма бака»: две кнопки-сегмента — цилиндр и конус-переходник.
        // Выбор тут же меняет призрак, поставленную деталь и превью.
        private void DrawShapeRow(float x, ref float y, float w, ref PartPreset preset, HotSlot owner, int card)
        {
            GUI.Label(new Rect(x, y, w * 0.6f, 18f), "Форма бака", BuilderSkin.Body);
            GUI.Label(new Rect(x + (w * 0.4f), y, w * 0.6f, 18f), "цилиндр / конус", BuilderSkin.TinyRight);
            y += 22f;

            const float gap = 4f;
            float buttonWidth = (w - gap) * 0.5f;
            var cylinder = new Rect(x, y, buttonWidth, 34f);
            var cone = new Rect(x + buttonWidth + gap, y, buttonWidth, 34f);

            if (ShapeButton(cylinder, "Цилиндр", preset.Form != TankForm.Cone))
            {
                SetTankForm(ref preset, owner, card, TankForm.Cylinder);
            }

            if (ShapeButton(cone, "Конус-переходник", preset.Form == TankForm.Cone))
            {
                SetTankForm(ref preset, owner, card, TankForm.Cone);
            }

            y += 44f;
        }

        private bool ShapeButton(Rect rect, string text, bool selected)
        {
            Event e = Event.current;
            bool hover = controlsActive && !Dragging && rect.Contains(e.mousePosition);
            GUI.Box(rect, GUIContent.none, selected ? BuilderSkin.StepActive : hover ? BuilderSkin.StepHover : BuilderSkin.Step);
            GUI.Label(rect, text, BuilderSkin.ButtonText);

            if (controlsActive && e.type == EventType.MouseDown && e.button == 0 && !Dragging && rect.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Переключить форму заготовки. При первом переходе в конус, если верхний
        /// радиус ещё равен нижнему (бак выглядел бы цилиндром), задаём ступень
        /// вдвое — заготовка переходника, которую игрок дальше правит сам.
        /// Ступень округляется до 0.1 м, чтобы дальше шаги давали «круглые» числа.
        /// </summary>
        private void SetTankForm(ref PartPreset preset, HotSlot owner, int card, TankForm form)
        {
            if (preset.Form == form)
            {
                return;
            }

            preset.Form = form;
            if (form == TankForm.Cone
                && Mathf.Abs(preset.TopRadius - preset.Radius) < 0.001f)
            {
                float half = Mathf.Round(preset.Radius * 5f) / 10f;
                preset.TopRadius = Mathf.Clamp(half, PartCatalog.TankMinRadius, PartCatalog.TankMaxRadius);
            }

            ApplyPresetEdit(owner, card, preset);
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
            // Шаг по сетке 0.1 м: значение всегда «круглое» (0.5, а не 0.65) —
            // иначе размер под соседний бак не подобрать. Заодно округление
            // убирает накопление float-ошибки (0.1 + 0.1 + ...).
            float stepped = Mathf.Round((value + delta) * 10f) / 10f;
            return Mathf.Clamp(stepped, min, max);
        }

        // Кнопка со стрелкой: срабатывает по нажатию, при удержании повторяется.
        private bool StepButton(Rect rect, Texture2D icon, bool enabled, int id, float repeat)
        {
            Event e = Event.current;
            bool inside = rect.Contains(e.mousePosition);
            bool pressed = enabled && holdId == id;
            bool hover = enabled && controlsActive && inside && !Dragging;

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

            if (!enabled || !controlsActive)
            {
                return false;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && inside && !Dragging)
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

            if (open && e.type == EventType.MouseDown && e.button == 0 && inside && !Dragging)
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
                bool hover = controlsActive && !Dragging && card.Contains(e.mousePosition);

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

                // Карточка показывает картинку заготовки: если вид уже в панели —
                // иконку слота (она под его форму/размеры), иначе дефолтную.
                Texture2D picture = SlotIconFor(kind);
                if (picture == null)
                {
                    picture = PartIcons.Get(kind);
                }

                GUI.DrawTexture(new Rect(iconBox.center.x - (iconSize * 0.5f), iconBox.center.y - (iconSize * 0.5f), iconSize, iconSize),
                    picture, ScaleMode.ScaleToFit);

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

                if (controlsActive && e.type == EventType.MouseDown && e.button == 0 && !Dragging && card.Contains(e.mousePosition))
                {
                    statsIndex = i;

                    // Карточка — черновик «на следующую постановку»: правка
                    // размеров дальше не трогает слоты, которые игрок не выбирал.
                    // Стартовая точка черновика — как выглядит выбранный слот.
                    if (editSlot != null && editSlot.Occupied && editSlot.Preset.Kind == kind)
                    {
                        drafts[i] = editSlot.Preset;
                    }

                    editSlot = null;

                    if (e.clickCount >= 2)
                    {
                        AssignToHotbar(kind);        // двойной клик — в свободный слот
                    }
                    else
                    {
                        dragPreset = StatsPreset();  // тащится то, что показывает окно
                        dragIcon = picture;
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
            GUI.Box(rect, GUIContent.none, (controlsActive && inside) ? hover : normal);
            GUI.Label(rect, text, BuilderSkin.DangerButtonText);

            if (controlsActive && e.type == EventType.MouseDown && e.button == 0 && inside && !Dragging)
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
                if (hotbar[i].Occupied && hotbar[i].Preset.Kind == kind)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Иконка первого слота с этим видом (иначе null — общая дефолтная).</summary>
        private Texture2D SlotIconFor(PartKind kind)
        {
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (hotbar[i].Occupied && hotbar[i].Preset.Kind == kind && hotbar[i].Icon != null)
                {
                    return hotbar[i].Icon;
                }
            }

            return null;
        }

        /// <summary>
        /// Двойной клик по карточке: копия показанной заготовки в свободный слот.
        /// Копия — чтобы из одного вида легко наделать слотов с разными размерами.
        /// </summary>
        private void AssignToHotbar(PartKind kind)
        {
            int target = -1;
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (!hotbar[i].Occupied)
                {
                    target = i;
                    break;
                }
            }

            if (target < 0)
            {
                int existing = FindInHotbar(kind);
                SetHotbarIndex(existing >= 0 ? existing : (hotbarIndex >= 0 ? hotbarIndex : 0));
                return;
            }

            AssignPreset(target, StatsPreset());
            SetHotbarIndex(target);
        }

        // ─── Перетаскивание ──────────────────────────────────────────────────

        private void HandleDragRelease()
        {
            Event current = Event.current;
            if (Dragging && !dragMoved && (current.mousePosition - dragOrigin).sqrMagnitude > DragThreshold * DragThreshold)
            {
                dragMoved = true;                    // сдвинули — это уже перетаскивание, не клик
            }

            if (current.type != EventType.MouseUp || !Dragging)
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
                PartPreset dragged = dragPreset.Value;
                bool dropped = false;
                for (int i = 0; i < hotbar.Length; i++)
                {
                    if (!HotbarSlotRect(i).Contains(current.mousePosition))
                    {
                        continue;
                    }

                    if (dragFromHotbar >= 0 && dragFromHotbar != i)
                    {
                        SwapSlots(dragFromHotbar, i);    // перенос = обмен местами
                    }
                    else
                    {
                        AssignPreset(i, dragged);        // из инвентаря — копия заготовки
                    }

                    SetHotbarIndex(i);
                    dropped = true;
                    break;
                }

                if (!dropped && dragFromHotbar >= 0)
                {
                    ClearSlot(dragFromHotbar);           // бросили мимо — убрать из панели
                    ApplySelection();
                }
            }

            ResetDrag();
            current.Use();
        }

        private void ResetDrag()
        {
            dragPreset = null;
            dragIcon = null;
            dragFromHotbar = -1;
            dragMoved = false;
        }

        private void DrawDragIcon()
        {
            if (!Dragging || !open || !dragMoved)
            {
                return;
            }

            Vector2 mouse = Event.current.mousePosition;
            var rect = new Rect(mouse.x - 30f, mouse.y - 30f, 60f, 60f);
            SetColor(new Color(0.30f, 0.55f, 0.85f, 0.50f));
            GUI.Box(Expand(rect, 20f), GUIContent.none, BuilderSkin.Glow);
            SetColor(new Color(1f, 1f, 1f, 0.95f));
            Texture2D picture = dragIcon != null ? dragIcon : PartIcons.Get(dragPreset.Value.Kind);
            GUI.DrawTexture(rect, picture, ScaleMode.ScaleToFit);
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

        /// <summary>Конусный ли бак: верхний радиус задан и отличается от нижнего.</summary>
        private static bool IsCone(PartMetrics metrics)
        {
            return metrics.TopRadius > 0.01f
                && Mathf.Abs(metrics.TopRadius - metrics.Radius) > 1e-3f;
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
