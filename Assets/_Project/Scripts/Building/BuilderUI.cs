using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// ИНТЕРФЕЙС СТРОИТЕЛЬСТВА (IMGUI, как чит-меню и подсказки проекта).
    ///
    /// Хотбар: 9 слотов снизу по центру, всегда виден в стройке. Предметы
    /// перетаскиваются сюда из окна инвентаря (ЛКМ). Выбор слота — колесо или
    /// цифры 1..9; выбранный слот обведён ярким квадратом; выбранный предмет
    /// сразу показывает перед игроком призрак (ставит SpacecraftBuilder).
    ///
    /// Инвентарь (E, только в стройке): окно с карточками деталей (рисунок +
    /// название), слева от него — характеристики выбранной: масса, объём и т.п.
    /// плюс поля размеров (длина/радиус бака, радиус отделителя) — размеры
    /// сразу меняют призрак и записываются в поставленную деталь.
    ///
    /// Пока окно открыто, курсор отпускается, ввод игроку блокируется
    /// (PlayerController.InputBlocked), камера не крутится — как в чит-меню.
    /// Масштаб интерфейса под DPI/4K: GUI.matrix растягивается по высоте экрана.
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

        private readonly PartKind?[] hotbar = new PartKind?[9];
        private int hotbarIndex = -1;
        private bool open;
        private int statsIndex = -1;
        private int dragKind = -1;
        private int dragFromHotbar = -1;
        private bool cursorWasLocked = true;

        private const float SlotSize = 54f;
        private const float SlotGap = 6f;
        private const float InvWidth = 300f;
        private const float InvHeight = 286f;
        private const float StatsWidth = 236f;

        private void Start()
        {
            // Стартовая палитра в панели: строить можно сразу, но перетаскивание
            // из инвентаря работает как обычно (слоты можно менять местами).
            hotbar[0] = PartKind.Engine;
            hotbar[1] = PartKind.FuelTank;
            hotbar[2] = PartKind.Separator;
            hotbarIndex = 0;
            ApplySelection();
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

                return;
            }

            if (PlayerInput.Down(GameKey.E))
            {
                SetOpen(!open);
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
            if (Camera != null)
            {
                Camera.LockCursor = cursorWasLocked;
            }

            Cursor.lockState = cursorWasLocked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !cursorWasLocked;
        }

        private void OnDisable()
        {
            if (open)
            {
                SetOpen(false);
            }
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
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            CheatGuiSkin.Build();

            if (buildActive)
            {
                DrawHotbar(width, height);
                DrawHints(width, height);
                DrawRHold(width, height);
                if (open)
                {
                    DrawStats(width, height);
                    DrawInventory(width, height);
                }

                DrawDragIcon();
                HandleDragRelease();
            }
            else
            {
                GUI.Label(new Rect(12f, height - 60f, 480f, 24f),
                    "Физика включена. Удерживайте R (1 с), чтобы вернуться к стройке.",
                    CheatGuiSkin.Label);
            }

            GUI.matrix = previous;
        }

        private Rect HotbarSlotRect(int index, float width, float height)
        {
            float total = (SlotSize * hotbar.Length) + (SlotGap * (hotbar.Length - 1));
            float x = ((width - total) * 0.5f) + (index * (SlotSize + SlotGap));
            float y = height - SlotSize - 14f;
            return new Rect(x, y, SlotSize, SlotSize);
        }

        private void DrawHotbar(float width, float height)
        {
            for (int i = 0; i < hotbar.Length; i++)
            {
                Rect rect = HotbarSlotRect(i, width, height);
                bool selected = i == hotbarIndex;

                GUI.color = selected ? new Color(0.60f, 0.78f, 1f, 1f) : new Color(1f, 1f, 1f, 0.9f);
                GUI.Box(rect, GUIContent.none, CheatGuiSkin.Panel);
                GUI.color = Color.white;

                if (selected)
                {
                    // «Яркий квадрат» вокруг выбранного слота (как в майне).
                    var glow = new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f);
                    DrawRectOutline(glow, 2f, new Color(0.55f, 0.85f, 1f, 1f));
                }

                PartKind? kind = hotbar[i];
                if (kind.HasValue)
                {
                    var icon = new Rect(rect.x + 7f, rect.y + 7f, rect.width - 14f, rect.height - 14f);
                    GUI.DrawTexture(icon, PartIcons.Get(kind.Value), ScaleMode.ScaleToFit);
                }

                GUI.Label(new Rect(rect.x + 4f, rect.y + 1f, 20f, 16f),
                    (i + 1).ToString(), CheatGuiSkin.LabelMuted);

                // Клик по слоту: выбрать (и при наличии предмета — потянуть).
                if (open)
                {
                    Event current = Event.current;
                    if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
                    {
                        SetHotbarIndex(i);
                        if (kind.HasValue)
                        {
                            statsIndex = System.Array.IndexOf(Catalog, kind.Value);
                            dragKind = (int)kind.Value;
                            dragFromHotbar = i;
                        }

                        current.Use();
                    }
                }
            }
        }

        private void DrawHints(float width, float height)
        {
            GUI.Label(new Rect(12f, height - 122f, 620f, 22f),
                "E — инвентарь    ЛКМ (в окне) — перетащить в панель    ПКМ — поставить    Ctrl+Z — убрать последнюю", CheatGuiSkin.Label);

            PartKind? kind = hotbarIndex >= 0 ? hotbar[hotbarIndex] : null;
            if (kind.HasValue)
            {
                GUI.Label(new Rect(12f, height - 100f, 620f, 22f),
                    "Выбрано: " + PartCatalog.DisplayName(kind.Value) + "    Колёсико — сменить слот", CheatGuiSkin.LabelMuted);
            }
        }

        private void DrawRHold(float width, float height)
        {
            float progress = Builder.RHoldProgress;
            if (progress <= 0.001f)
            {
                return;
            }

            var bar = new Rect((width - 220f) * 0.5f, height - SlotSize - 52f, 220f, 10f);
            GUI.Box(bar, GUIContent.none, CheatGuiSkin.SliderTrack);
            var fill = new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * progress, bar.height - 4f);
            DrawSolid(fill, new Color(0.55f, 0.85f, 1f, 0.95f));
            GUI.Label(new Rect(bar.x, bar.y - 22f, bar.width, 20f), "Включение физики…", CheatGuiSkin.Label);
        }

        private void DrawInventory(float width, float height)
        {
            float x = ((width - InvWidth) * 0.5f) + 110f;
            float y = ((height - InvHeight) * 0.5f) - 30f;
            var panel = new Rect(x, y, InvWidth, InvHeight);
            GUI.Box(panel, GUIContent.none, CheatGuiSkin.Panel);
            GUI.Label(new Rect(x + 14f, y + 8f, InvWidth - 28f, 24f), "Инвентарь деталей", CheatGuiSkin.Header);

            for (int i = 0; i < Catalog.Length; i++)
            {
                var card = new Rect(x + 14f, y + 40f + (i * 64f), InvWidth - 28f, 58f);
                bool selected = statsIndex == i;
                var old = GUI.color;
                GUI.color = selected ? new Color(0.45f, 0.62f, 0.85f, 1f) : Color.white;
                GUI.Box(card, GUIContent.none, CheatGuiSkin.Button);
                GUI.color = old;

                var icon = new Rect(card.x + 4f, card.y + 3f, 52f, 52f);
                GUI.DrawTexture(icon, PartIcons.Get(Catalog[i]), ScaleMode.ScaleToFit);
                GUI.Label(new Rect(card.x + 62f, card.y + 8f, card.width - 68f, 22f),
                    PartCatalog.DisplayName(Catalog[i]), CheatGuiSkin.Label);
                GUI.Label(new Rect(card.x + 62f, card.y + 30f, card.width - 68f, 20f),
                    "ЛКМ — перетащить в панель", CheatGuiSkin.LabelMuted);

                HandleCardEvents(card, i, Event.current);
            }

            var clear = new Rect(x + 14f, y + InvHeight - 40f, 150f, 28f);
            if (GUI.Button(clear, "Очистить всё (Ctrl+Z)", CheatGuiSkin.Button))
            {
                Builder.ClearAll();
            }

            GUI.Label(new Rect(x + 176f, y + InvHeight - 38f, InvWidth - 190f, 20f), "E — закрыть", CheatGuiSkin.LabelMuted);
        }

        private void DrawStats(float width, float height)
        {
            float x = ((width - InvWidth) * 0.5f) + 110f - StatsWidth - 10f;
            float y = ((height - InvHeight) * 0.5f) - 30f;
            var panel = new Rect(x, y, StatsWidth, InvHeight);
            GUI.Box(panel, GUIContent.none, CheatGuiSkin.Panel);
            GUI.Label(new Rect(x + 14f, y + 8f, StatsWidth - 28f, 24f), "Характеристики", CheatGuiSkin.Header);

            if (statsIndex < 0)
            {
                GUI.Label(new Rect(x + 14f, y + 44f, StatsWidth - 28f, 60f),
                    "Выберите деталь слева — здесь появятся её характеристики и размеры.",
                    CheatGuiSkin.LabelMuted);
                return;
            }

            PartKind kind = Catalog[statsIndex];
            PartMetrics metrics = Builder.MetricsFor(kind);
            float line = y + 44f;
            GUI.Label(new Rect(x + 14f, line, StatsWidth - 28f, 22f), PartCatalog.DisplayName(kind), CheatGuiSkin.Label);
            line += 26f;

            AddStatLine(x + 14f, ref line, StatsWidth - 28f, "Масса", FormatMass(metrics.MassKg));
            if (kind == PartKind.FuelTank)
            {
                double volume = System.Math.PI * metrics.Radius * metrics.Radius * metrics.Height;
                AddStatLine(x + 14f, ref line, StatsWidth - 28f, "Объём", volume.ToString("F1") + " м³");
            }

            AddStatLine(x + 14f, ref line, StatsWidth - 28f, "Прочность стыка",
                (metrics.JointStrengthNewtons / 1e6).ToString("F1") + " МН");

            switch (kind)
            {
                case PartKind.Engine:
                    AddStatLine(x + 14f, ref line, StatsWidth - 28f, "Тяга (вак.)", "2279 кН");
                    AddStatLine(x + 14f, ref line, StatsWidth - 28f, "Удельный импульс", "452 с");
                    break;

                case PartKind.FuelTank:
                    GUI.Label(new Rect(x + 14f, line, StatsWidth - 28f, 20f), "Длина бака: " + Builder.TankHeight.ToString("F1") + " м", CheatGuiSkin.Label);
                    Builder.TankHeight = GUI.HorizontalSlider(
                        new Rect(x + 14f, line + 22f, StatsWidth - 28f, 12f), Builder.TankHeight,
                        PartCatalog.TankMinHeight, PartCatalog.TankMaxHeight, CheatGuiSkin.SliderTrack, CheatGuiSkin.SliderThumb);
                    line += 44f;
                    GUI.Label(new Rect(x + 14f, line, StatsWidth - 28f, 20f), "Радиус бака: " + Builder.TankRadius.ToString("F2") + " м", CheatGuiSkin.Label);
                    Builder.TankRadius = GUI.HorizontalSlider(
                        new Rect(x + 14f, line + 22f, StatsWidth - 28f, 12f), Builder.TankRadius,
                        PartCatalog.TankMinRadius, PartCatalog.TankMaxRadius, CheatGuiSkin.SliderTrack, CheatGuiSkin.SliderThumb);
                    line += 44f;
                    break;

                case PartKind.Separator:
                    GUI.Label(new Rect(x + 14f, line, StatsWidth - 28f, 20f), "Радиус отделителя: " + Builder.SeparatorRadius.ToString("F2") + " м", CheatGuiSkin.Label);
                    Builder.SeparatorRadius = GUI.HorizontalSlider(
                        new Rect(x + 14f, line + 22f, StatsWidth - 28f, 12f), Builder.SeparatorRadius,
                        PartCatalog.SeparatorMinRadius, PartCatalog.SeparatorMaxRadius, CheatGuiSkin.SliderTrack, CheatGuiSkin.SliderThumb);
                    line += 44f;
                    AddStatLine(x + 14f, ref line, StatsWidth - 28f, "Толщина", "40 мм");
                    break;
            }
        }

        private static void AddStatLine(float x, ref float line, float width, string left, string right)
        {
            GUI.Label(new Rect(x, line, width * 0.6f, 20f), left, CheatGuiSkin.LabelMuted);
            var rightRect = new Rect(x + (width * 0.55f), line, width * 0.45f, 20f);
            GUI.Label(rightRect, right, CheatGuiSkin.Label);
            line += 22f;
        }

        private static string FormatMass(double kg)
        {
            return kg >= 1000d ? (kg / 1000d).ToString("F1") + " т" : kg.ToString("F0") + " кг";
        }

        // ─── Перетаскивание ──────────────────────────────────────────────────

        private void HandleCardEvents(Rect card, int index, Event current)
        {
            if (current.type != EventType.MouseDown || !card.Contains(current.mousePosition))
            {
                return;
            }

            statsIndex = index;
            dragKind = (int)Catalog[index];
            dragFromHotbar = -1;
            current.Use();
        }

        private void HandleDragRelease()
        {
            Event current = Event.current;
            if (current.type != EventType.MouseUp)
            {
                return;
            }

            if (dragKind < 0)
            {
                return;
            }

            if (!open)
            {
                dragKind = -1;
                dragFromHotbar = -1;
                return;
            }

            float width = Screen.width / Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            float height = Screen.height / Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
            var kind = (PartKind)dragKind;

            bool dropped = false;
            for (int i = 0; i < hotbar.Length; i++)
            {
                if (!HotbarSlotRect(i, width, height).Contains(current.mousePosition))
                {
                    continue;
                }

                hotbar[i] = kind;
                if (dragFromHotbar >= 0 && dragFromHotbar != i)
                {
                    hotbar[dragFromHotbar] = null;       // перенос местами
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

            dragKind = -1;
            dragFromHotbar = -1;
            current.Use();
        }

        private void DrawDragIcon()
        {
            if (dragKind < 0 || !open)
            {
                return;
            }

            Vector2 mouse = Event.current.mousePosition;
            var rect = new Rect(mouse.x - 26f, mouse.y - 26f, 52f, 52f);
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(rect, PartIcons.Get((PartKind)dragKind), ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        // ─── Мелкая графика ──────────────────────────────────────────────────

        private static void DrawSolid(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void DrawRectOutline(Rect rect, float thickness, Color color)
        {
            DrawSolid(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolid(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolid(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolid(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
