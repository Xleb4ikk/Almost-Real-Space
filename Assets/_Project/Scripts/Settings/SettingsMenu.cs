using System.Collections.Generic;
using Galilego.Building;
using Galilego.Tablet;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Settings menu on Esc. IMGUI, drawn with BuilderSkin so it matches the builder UI.
    /// Tabs: Graphics, View, Controls (key bindings), Audio. While open the player input
    /// is blocked (PlayerInput returns "nothing pressed" and PlayerController.InputBlocked
    /// is set), the cursor is released and the simulation is paused (optional).
    /// Esc is handed to a screen / tablet / builder window / cheat menu if one is open;
    /// otherwise it opens this menu. Runs before every other script (order -200) so it
    /// sees the state as it was when Esc was pressed.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class SettingsMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private const float RowH = 44f;
        private const float CtrlW = 360f;
        private const float BtnH = 34f;

        private static readonly string[] TabNames = { "Графика", "Вид", "Управление", "Звук" };
        private static readonly string[] TabIds = { "graphics", "view", "controls", "audio" };
        private static readonly string[] ScreenModes = { "Полный экран", "Окно без рамки", "В окне" };
        private static readonly string[] OffOn = { "Выкл", "Вкл" };
        private static readonly string[] AaNames = { "Выкл", "FXAA", "SMAA", "TAA" };
        private static readonly string[] TextureNames = { "Высокое", "Среднее", "Низкое" };
        private static readonly string[] AnisoNames = { "Выкл", "Обычная", "Принудительная" };
        private static readonly string[] BlurNames = { "Выкл", "Низкое", "Среднее", "Высокое" };
        private static readonly int[] FrameCaps = { 0, 30, 60, 75, 90, 120, 144, 165, 240 };
        private static readonly string[] FrameCapNames = { "Без лимита", "30", "60", "75", "90", "120", "144", "165", "240" };

        private SimulationRunner runner;
        private PlayerController controller;
        private FirstPersonCamera view;

        private bool lockBefore;
        private bool cursorWasLocked;
        private bool pausedByMenu;

        private int tab;
        private readonly Vector2[] scrolls = new Vector2[4];
        private readonly float[] contentHeights = new float[4];

        // Key capture
        private bool capturing;
        private GameAction captureAction;
        private int captureSlot;

        // Staged display settings
        private List<Vector2Int> resolutions = new List<Vector2Int>();
        private string[] resolutionLabels = new string[0];
        private int stageRes;
        private int stageMode;

        private string toast;
        private float toastUntil;
        private float quitArmedUntil;

        // Drawing state
        private float y;
        private float rowW;
        private bool mouseInView = true;
        private bool modal;

        private GUISkin skin;
        private Texture2D styleGuard;
        private GUIStyle sliderStyle;
        private GUIStyle thumbStyle;
        private GUIStyle dangerKeyText;

        // ─── Open / close ────────────────────────────────────────────────────

        private void Update()
        {
            if (IsOpen)
            {
                UpdateOpen();
                return;
            }

            if (PlayerInput.RawEscapeDown() && CanOpen())
            {
                PlayerInput.EscapeConsumedFrame = Time.frameCount;
                Open();
            }
        }

        private void UpdateOpen()
        {
            if (capturing)
            {
                // IMGUI does not always deliver a lone modifier press; catch those here.
                KeyCode[] modifiers =
                {
                    KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl,
                    KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt
                };
                foreach (KeyCode k in modifiers)
                {
                    if (KeyBindings.RawDown(k))
                    {
                        Assign(k);
                        break;
                    }
                }

                return;
            }

            if (PlayerInput.RawEscapeDown())
            {
                PlayerInput.EscapeConsumedFrame = Time.frameCount;
                SettingsApplier applier = SettingsApplier.Instance;
                if (applier != null && applier.RevertPending)
                {
                    applier.RevertDisplay();
                    SyncStagedDisplay();
                    return;
                }

                Close();
            }
        }

        private bool CanOpen()
        {
            ResolveRefs();
            if (ShipTablet.AnyExpanded || BuilderUI.AnyOpen || CheatMenu.AnyOpen)
            {
                return false;
            }

            return controller == null || (!controller.InScreenFocus && !controller.InputBlocked);
        }

        private void ResolveRefs()
        {
            if (runner == null)
            {
                runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (controller == null)
            {
                controller = FindAnyObjectByType<PlayerController>();
            }

            if (view == null)
            {
                view = FindAnyObjectByType<FirstPersonCamera>();
            }
        }

        private void Open()
        {
            ResolveRefs();
            IsOpen = true;
            capturing = false;

            if (controller != null)
            {
                controller.InputBlocked = true;
            }

            cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            if (view != null)
            {
                lockBefore = view.LockCursor;
                view.LockCursor = false;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (runner != null && GameSettings.Data.pauseOnMenu && !runner.Paused)
            {
                runner.Paused = true;
                pausedByMenu = true;
            }

            resolutions = SettingsApplier.GetResolutions();
            resolutionLabels = new string[resolutions.Count];
            for (int i = 0; i < resolutions.Count; i++)
            {
                resolutionLabels[i] = resolutions[i].x + " × " + resolutions[i].y;
            }

            SyncStagedDisplay();
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            capturing = false;

            if (controller != null)
            {
                controller.InputBlocked = false;
            }

            if (view != null)
            {
                view.LockCursor = lockBefore;
            }

            if (cursorWasLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (pausedByMenu && runner != null)
            {
                runner.Paused = false;
            }

            pausedByMenu = false;
            GameSettings.Save();
        }

        private void OnDisable()
        {
            Close();
        }

        private void SyncStagedDisplay()
        {
            SettingsData d = GameSettings.Data;
            stageMode = Mathf.Clamp(d.screenMode, 0, 2);
            stageRes = 0;
            int best = int.MaxValue;
            for (int i = 0; i < resolutions.Count; i++)
            {
                int diff = Mathf.Abs(resolutions[i].x - d.resWidth) + Mathf.Abs(resolutions[i].y - d.resHeight);
                if (diff < best)
                {
                    best = diff;
                    stageRes = i;
                }
            }
        }

        // ─── Styles ──────────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            BuilderSkin.Build();
            if (skin != null && styleGuard == BuilderSkin.Dot)
            {
                return;
            }

            styleGuard = BuilderSkin.Dot;
            skin = Instantiate(GUI.skin);

            GUIStyle vbar = skin.verticalScrollbar;
            vbar.normal.background = BuilderSkin.Track.normal.background;
            vbar.border = BuilderSkin.Track.border;
            vbar.fixedWidth = 12f;
            GUIStyle vthumb = skin.verticalScrollbarThumb;
            vthumb.normal.background = BuilderSkin.Step.normal.background;
            vthumb.hover.background = BuilderSkin.StepHover.normal.background;
            vthumb.active.background = BuilderSkin.StepActive.normal.background;
            vthumb.border = BuilderSkin.Step.border;
            vthumb.fixedWidth = 12f;
            vthumb.overflow = new RectOffset(0, 0, 0, 0);
            GUIStyle hbar = skin.horizontalScrollbar;
            hbar.fixedHeight = 0f;
            hbar.normal.background = null;

            sliderStyle = new GUIStyle(skin.horizontalSlider)
            {
                fixedHeight = 22f,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            sliderStyle.normal.background = null;
            sliderStyle.hover.background = null;
            sliderStyle.active.background = null;

            thumbStyle = new GUIStyle(skin.horizontalSliderThumb)
            {
                fixedWidth = 22f,
                fixedHeight = 22f,
                border = BuilderSkin.StepActive.border,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0)
            };
            thumbStyle.normal.background = BuilderSkin.StepActive.normal.background;
            thumbStyle.hover.background = BuilderSkin.StepActive.normal.background;
            thumbStyle.active.background = BuilderSkin.StepActive.normal.background;
            thumbStyle.onNormal.background = BuilderSkin.StepActive.normal.background;
            thumbStyle.onHover.background = BuilderSkin.StepActive.normal.background;
            thumbStyle.onActive.background = BuilderSkin.StepActive.normal.background;

            dangerKeyText = new GUIStyle(BuilderSkin.KeyText);
            dangerKeyText.normal.textColor = BuilderSkin.DangerText;
        }

        // ─── Frame ───────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUISkin previousSkin = GUI.skin;
            Matrix4x4 previousMatrix = GUI.matrix;
            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.skin = skin;
            GUI.depth = -10000;

            float scale = Mathf.Clamp(Screen.height / 1080f, 0.5f, 2f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float vw = Screen.width / scale;
            float vh = Screen.height / scale;

            SettingsApplier applier = SettingsApplier.Instance;
            modal = applier != null && applier.RevertPending;
            mouseInView = true;

            if (capturing)
            {
                HandleCapture(Event.current);
            }

            // Dim the game behind the window.
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0f, 0f, vw, vh), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float width = Mathf.Min(1100f, vw - 60f);
            float height = Mathf.Min(720f, vh - 60f);
            var win = new Rect((vw - width) * 0.5f, (vh - height) * 0.5f, width, height);

            GUI.color = BuilderSkin.Shadow;
            GUI.Box(new Rect(win.x - 24f, win.y - 24f, win.width + 48f, win.height + 48f), GUIContent.none, BuilderSkin.Glow);
            GUI.color = Color.white;
            GUI.Box(win, GUIContent.none, BuilderSkin.Window);

            DrawHeader(win);
            DrawSidebar(win);
            DrawContent(win);
            DrawToast(win);
            if (modal)
            {
                DrawRevertBanner(win, applier);
            }

            GUI.color = previousColor;
            GUI.depth = previousDepth;
            GUI.matrix = previousMatrix;
            GUI.skin = previousSkin;
        }

        private void DrawHeader(Rect win)
        {
            GUI.Label(new Rect(win.x + 28f, win.y + 16f, 500f, 36f), "НАСТРОЙКИ", BuilderSkin.TitleText);
            GUI.Label(new Rect(win.x + 28f, win.y + 44f, 500f, 20f), "Esc — закрыть меню", BuilderSkin.Tiny);

            var close = new Rect(win.xMax - 58f, win.y + 18f, 38f, 38f);
            bool hover = Hover(close);
            GUI.Box(close, GUIContent.none, hover ? BuilderSkin.StepHover : BuilderSkin.Step);
            GUI.DrawTexture(new Rect(close.center.x - 9f, close.center.y - 9f, 18f, 18f), BuilderSkin.Cross);
            if (ClickOn(close) == 1)
            {
                Close();
            }
        }

        private void DrawSidebar(Rect win)
        {
            float x = win.x + 22f;
            float top = win.y + 80f;
            for (int i = 0; i < TabNames.Length; i++)
            {
                var r = new Rect(x, top + (i * 60f), 200f, 52f);
                bool hover = Hover(r);
                GUIStyle bg = i == tab ? BuilderSkin.CardSelected : (hover ? BuilderSkin.CardHover : BuilderSkin.Card);
                GUI.Box(r, GUIContent.none, bg);
                GUI.Label(new Rect(r.x + 18f, r.y, r.width - 24f, r.height), TabNames[i], BuilderSkin.NameText);
                if (ClickOn(r) == 1)
                {
                    tab = i;
                    capturing = false;
                }
            }

            float bottom = win.yMax - 22f;
            var quit = new Rect(x, bottom - BtnH, 200f, BtnH);
            bool armed = Time.unscaledTime < quitArmedUntil;
            if (Button(quit, armed ? "Нажмите ещё раз" : "Выйти из игры", ButtonKind.Danger))
            {
                if (armed)
                {
                    QuitGame();
                }
                else
                {
                    quitArmedUntil = Time.unscaledTime + 3f;
                }
            }

            var reset = new Rect(x, quit.y - BtnH - 10f, 200f, BtnH);
            if (Button(reset, "Сбросить вкладку", ButtonKind.Normal))
            {
                GameSettings.ResetCategory(TabIds[tab]);
                if (tab == 0)
                {
                    SettingsApplier.Instance?.ChangePreset(GameSettings.Data.quality);
                }

                SyncStagedDisplay();
                Toast("Вкладка «" + TabNames[tab] + "» сброшена");
            }

            var resume = new Rect(x, reset.y - BtnH - 10f, 200f, BtnH);
            if (Button(resume, "Продолжить", ButtonKind.Accent))
            {
                Close();
            }
        }

        private static void QuitGame()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void DrawContent(Rect win)
        {
            var panel = new Rect(win.x + 246f, win.y + 80f, win.width - 270f, win.height - 102f);
            GUI.Box(panel, GUIContent.none, BuilderSkin.Viewport);

            var viewRect = new Rect(panel.x + 6f, panel.y + 8f, panel.width - 12f, panel.height - 16f);
            mouseInView = !modal && viewRect.Contains(Event.current.mousePosition);

            float innerW = viewRect.width - 28f;
            var content = new Rect(0f, 0f, innerW, Mathf.Max(viewRect.height, contentHeights[tab]));
            scrolls[tab] = GUI.BeginScrollView(viewRect, scrolls[tab], content, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            rowW = innerW - 12f;
            y = 4f;
            GUI.enabled = !modal;
            switch (tab)
            {
                case 0: DrawGraphics(); break;
                case 1: DrawView(); break;
                case 2: DrawControls(); break;
                default: DrawAudio(); break;
            }

            GUI.enabled = true;
            contentHeights[tab] = y + 12f;
            GUI.EndScrollView();
            mouseInView = true;
        }

        // ─── Tabs ────────────────────────────────────────────────────────────

        private void DrawGraphics()
        {
            SettingsData d = GameSettings.Data;
            bool changed = false;
            SettingsApplier applier = SettingsApplier.Instance;

            Section("Дисплей");
            Choice("Режим экрана", ScreenModes, ref stageMode, true);
            Choice("Разрешение", resolutionLabels, ref stageRes, stageMode != 1);
            bool displayDirty = stageMode != d.screenMode
                || (stageMode != 1 && stageRes < resolutions.Count
                    && (resolutions[stageRes].x != d.resWidth || resolutions[stageRes].y != d.resHeight));
            if (displayDirty && applier != null)
            {
                var apply = new Rect(rowW - CtrlW, y + 3f, CtrlW, BtnH);
                if (Button(apply, "Применить параметры экрана", ButtonKind.Accent))
                {
                    Vector2Int r = stageMode == 1
                        ? new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height)
                        : resolutions[Mathf.Clamp(stageRes, 0, resolutions.Count - 1)];
                    applier.ApplyDisplay(r.x, r.y, stageMode);
                }

                y += RowH;
            }

            int vsync = d.vsync;
            if (Choice("Вертикальная синхронизация", OffOn, ref vsync, true))
            {
                d.vsync = vsync;
                changed = true;
            }

            int capIndex = NearestIndex(FrameCaps, d.frameCap);
            if (Choice(d.vsync > 0 ? "Лимит FPS (при V-Sync не действует)" : "Лимит FPS", FrameCapNames, ref capIndex, d.vsync == 0))
            {
                d.frameCap = FrameCaps[capIndex];
                changed = true;
            }

            Section("Качество");
            string[] presets = QualitySettings.names;
            int preset = Mathf.Clamp(d.quality, 0, presets.Length - 1);
            if (Choice("Пресет качества", presets, ref preset, true) && applier != null)
            {
                applier.ChangePreset(preset);
            }

            int aa = Mathf.Clamp(d.antiAliasing, 0, 3);
            if (Choice("Сглаживание", AaNames, ref aa, true))
            {
                d.antiAliasing = aa;
                changed = true;
            }

            bool dynRes = SettingsApplier.DynamicResolutionAvailable;
            float scale = d.renderScale;
            if (Slider("Масштаб рендера", ref scale, 50f, 100f, 5f, Mathf.RoundToInt(scale) + " %", dynRes))
            {
                d.renderScale = Mathf.RoundToInt(scale);
                changed = true;
            }

            if (!dynRes)
            {
                Note("Масштаб рендера недоступен: в HDRP-ассете выключено Dynamic Resolution. "
                    + "Включить: меню Unity → Galilego → Настройки → Включить динамическое разрешение.");
            }

            int tex = Mathf.Clamp(d.textureQuality, 0, 2);
            if (Choice("Качество текстур", TextureNames, ref tex, true))
            {
                d.textureQuality = tex;
                changed = true;
            }

            int aniso = Mathf.Clamp(d.anisotropic, 0, 2);
            if (Choice("Анизотропная фильтрация", AnisoNames, ref aniso, true))
            {
                d.anisotropic = aniso;
                changed = true;
            }

            float lod = d.lodBias;
            if (Slider("Дальность детализации объектов", ref lod, 0.5f, 3f, 0.1f, "×" + lod.ToString("0.0"), true))
            {
                d.lodBias = lod;
                changed = true;
            }

            float shadow = d.shadowDistance;
            if (Slider("Дальность теней", ref shadow, 0f, 2000f, 50f,
                shadow < 1f ? "Авто" : Mathf.RoundToInt(shadow) + " м", true))
            {
                d.shadowDistance = shadow;
                changed = true;
            }

            Section("Эффекты");
            int blur = d.motionBlur;
            if (Choice("Размытие в движении", BlurNames, ref blur, true))
            {
                d.motionBlur = blur;
                changed = true;
            }

            changed |= Toggle("Свечение (Bloom)", ref d.bloom, true);
            changed |= Toggle("Плёночное зерно", ref d.filmGrain, true);
            changed |= Toggle("Хроматическая аберрация", ref d.chromaticAberration, true);

            if (changed)
            {
                GameSettings.NotifyChanged();
            }
        }

        private void DrawView()
        {
            SettingsData d = GameSettings.Data;
            bool changed = false;

            Section("Камера");
            float fov = d.fov > 0f ? d.fov : 60f;
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.7778f;
            float horizontal = 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
            if (Slider("Поле зрения (по вертикали)", ref fov, 40f, 110f, 1f,
                Mathf.RoundToInt(fov) + "° (≈" + Mathf.RoundToInt(horizontal) + "° по гор.)", true))
            {
                d.fov = fov;
                changed = true;
            }

            float sens = d.mouseSensitivity;
            if (Slider("Чувствительность мыши", ref sens, 0.1f, 3f, 0.05f, "×" + sens.ToString("0.00"), true))
            {
                d.mouseSensitivity = sens;
                changed = true;
            }

            changed |= Toggle("Инвертировать вертикаль", ref d.invertY, true);
            changed |= Toggle("Приближение к экранам (клавиша «Приблизиться»)", ref d.lean, true);

            Section("Прицел");
            changed |= Toggle("Показывать прицел", ref d.crosshair, true);
            float cs = d.crosshairScale;
            if (Slider("Размер прицела", ref cs, 0.5f, 2.5f, 0.1f, "×" + cs.ToString("0.0"), d.crosshair))
            {
                d.crosshairScale = cs;
                changed = true;
            }

            Section("Меню");
            changed |= Toggle("Ставить симуляцию на паузу, пока открыто меню", ref d.pauseOnMenu, true);

            if (changed)
            {
                GameSettings.NotifyChanged();
            }
        }

        private void DrawAudio()
        {
            SettingsData d = GameSettings.Data;
            bool changed = false;

            Section("Громкость");
            changed |= VolumeSlider("Общая", ref d.volMaster);
            changed |= VolumeSlider("Музыка", ref d.volMusic);
            changed |= VolumeSlider("Эффекты", ref d.volEffects);
            changed |= VolumeSlider("Окружение", ref d.volAmbient);
            changed |= VolumeSlider("Интерфейс", ref d.volInterface);

            Section("Прочее");
            changed |= Toggle("Выключать звук, когда окно не в фокусе", ref d.muteUnfocused, true);
            Note("Звуков в игре пока нет: ползунки уже работают и сохраняются. Любой AudioSource подхватит их, "
                + "если добавить ему компонент Game Audio Source и выбрать канал.");

            if (changed)
            {
                GameSettings.NotifyChanged();
            }
        }

        private bool VolumeSlider(string label, ref float value)
        {
            float percent = value * 100f;
            if (Slider(label, ref percent, 0f, 100f, 1f, Mathf.RoundToInt(percent) + " %", true))
            {
                value = percent / 100f;
                return true;
            }

            return false;
        }

        private void DrawControls()
        {
            y += 4f;
            GUI.Label(new Rect(0f, y, rowW - 160f, 40f),
                "ЛКМ по клавише — назначить · ПКМ — очистить · Esc — отмена. У каждого действия две клавиши.",
                WrappedMuted());
            if (Button(new Rect(rowW - 150f, y + 3f, 150f, BtnH), "Сбросить все", ButtonKind.Normal))
            {
                KeyBindings.ResetAll();
                Toast("Все клавиши сброшены");
            }

            y += 46f;
            for (int g = 0; g < ActionTable.GroupNames.Length; g++)
            {
                Section(ActionTable.GroupNames[g]);
                foreach (ActionInfo info in ActionTable.All)
                {
                    if ((int)info.Group == g)
                    {
                        KeyRow(info);
                    }
                }
            }
        }

        private static GUIStyle wrappedMuted;

        private static GUIStyle WrappedMuted()
        {
            if (wrappedMuted == null || wrappedMuted.normal.textColor != BuilderSkin.TextMuted)
            {
                wrappedMuted = new GUIStyle(BuilderSkin.Muted) { wordWrap = true, fontSize = 13 };
            }

            return wrappedMuted;
        }

        // ─── Key rows ────────────────────────────────────────────────────────

        private void KeyRow(ActionInfo info)
        {
            float resetW = 64f;
            float slotW = 132f;
            float resetX = rowW - resetW;
            float slot1X = resetX - 8f - slotW;
            float slot0X = slot1X - 8f - slotW;

            GUI.Label(new Rect(0f, y, slot0X - 12f, RowH), info.Label, BuilderSkin.Body);
            for (int slot = 0; slot < KeyBindings.Slots; slot++)
            {
                var r = new Rect(slot == 0 ? slot0X : slot1X, y + ((RowH - BtnH) * 0.5f), slotW, BtnH);
                KeyButton(info, slot, r);
            }

            bool isDefault = KeyBindings.IsDefault(info.Action);
            var resetRect = new Rect(resetX, y + ((RowH - BtnH) * 0.5f), resetW, BtnH);
            if (Button(resetRect, "Сброс", isDefault ? ButtonKind.Disabled : ButtonKind.Normal, !isDefault))
            {
                KeyBindings.ResetAction(info.Action);
            }

            y += RowH;
        }

        private void KeyButton(ActionInfo info, int slot, Rect r)
        {
            KeyCode key = KeyBindings.Get(info.Action, slot);
            bool active = capturing && captureAction == info.Action && captureSlot == slot;
            bool hover = Hover(r);
            GUIStyle bg = active ? BuilderSkin.StepActive : (hover ? BuilderSkin.StepHover : BuilderSkin.Key);
            GUI.Box(r, GUIContent.none, bg);

            string text;
            GUIStyle textStyle = BuilderSkin.KeyText;
            if (active)
            {
                text = "Нажмите клавишу…";
            }
            else
            {
                text = KeyBindings.Display(key);
                if (key != KeyCode.None && KeyBindings.FindConflict(info.Action, key, out _))
                {
                    textStyle = dangerKeyText;
                    text = "! " + text;
                }
            }

            GUI.Label(r, text, textStyle);

            int click = ClickOn(r);
            if (click == 1)
            {
                capturing = true;
                captureAction = info.Action;
                captureSlot = slot;
            }
            else if (click == 2)
            {
                KeyBindings.Set(info.Action, slot, KeyCode.None);
                capturing = false;
            }
        }

        private void HandleCapture(Event e)
        {
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    capturing = false;
                }
                else
                {
                    Assign(e.keyCode);
                }

                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button >= 1 && e.button <= 4)
            {
                Assign(KeyCode.Mouse0 + e.button);
                e.Use();
            }
        }

        private void Assign(KeyCode key)
        {
            GameAction action = captureAction;
            int slot = captureSlot;
            capturing = false;

            // The same key twice on one action makes no sense: clear the other slot.
            int other = 1 - slot;
            if (KeyBindings.Get(action, other) == key)
            {
                KeyBindings.Set(action, other, KeyCode.None);
            }

            if (KeyBindings.StealKey(action, key, out GameAction from))
            {
                Toast("«" + KeyBindings.Display(key) + "» перенесена с действия «" + ActionTable.Get(from).Label + "»");
            }

            KeyBindings.Set(action, slot, key);
        }

        // ─── Controls ────────────────────────────────────────────────────────

        private enum ButtonKind { Normal, Accent, Danger, Disabled }

        private bool Hover(Rect r)
        {
            return !modal && mouseInView && r.Contains(Event.current.mousePosition);
        }

        /// <summary>1 for a left click, 2 for a right click inside the rect (the event is consumed).</summary>
        private int ClickOn(Rect r)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && !modal && mouseInView && r.Contains(e.mousePosition)
                && (e.button == 0 || e.button == 1))
            {
                e.Use();
                return e.button + 1;
            }

            return 0;
        }

        private bool Button(Rect r, string text, ButtonKind kind, bool enabled = true)
        {
            bool hover = enabled && Hover(r);
            GUIStyle bg;
            GUIStyle label = BuilderSkin.ButtonText;
            Color previous = GUI.color;
            switch (kind)
            {
                case ButtonKind.Accent:
                    bg = BuilderSkin.StepActive;
                    if (hover)
                    {
                        GUI.color = new Color(1.15f, 1.15f, 1.15f, 1f);
                    }

                    break;
                case ButtonKind.Danger:
                    bg = hover ? BuilderSkin.DangerHover : BuilderSkin.Danger;
                    label = BuilderSkin.DangerButtonText;
                    break;
                case ButtonKind.Disabled:
                    bg = BuilderSkin.StepDisabled;
                    label = BuilderSkin.MutedCenter;
                    break;
                default:
                    bg = hover ? BuilderSkin.StepHover : BuilderSkin.Step;
                    break;
            }

            if (!enabled && kind != ButtonKind.Disabled)
            {
                bg = BuilderSkin.StepDisabled;
                label = BuilderSkin.MutedCenter;
            }

            GUI.Box(r, GUIContent.none, bg);
            GUI.color = previous;
            GUI.Label(r, text, label);
            return enabled && ClickOn(r) == 1;
        }

        private void Section(string title)
        {
            y += 12f;
            GUI.Label(new Rect(0f, y, rowW, 22f), title.ToUpperInvariant(), BuilderSkin.Heading);
            y += 24f;
            Color previous = GUI.color;
            GUI.color = BuilderSkin.Line;
            GUI.DrawTexture(new Rect(0f, y, rowW, 1f), Texture2D.whiteTexture);
            GUI.color = previous;
            y += 6f;
        }

        private void Note(string text)
        {
            GUIStyle style = WrappedMuted();
            float h = Mathf.Max(22f, style.CalcHeight(new GUIContent(text), rowW));
            GUI.Label(new Rect(0f, y, rowW, h), text, style);
            y += h + 6f;
        }

        private void RowLabel(string text, bool enabled)
        {
            GUI.Label(new Rect(0f, y, rowW - CtrlW - 20f, RowH), text, enabled ? BuilderSkin.Body : BuilderSkin.Muted);
        }

        /// <summary>Chevron selector. Returns true when the index changed.</summary>
        private bool Choice(string label, string[] options, ref int index, bool enabled)
        {
            RowLabel(label, enabled);
            int count = options != null ? options.Length : 0;
            float top = y + ((RowH - BtnH) * 0.5f);
            float x = rowW - CtrlW;
            var left = new Rect(x, top, BtnH, BtnH);
            var right = new Rect(x + CtrlW - BtnH, top, BtnH, BtnH);
            var value = new Rect(x + BtnH + 6f, top, CtrlW - ((BtnH + 6f) * 2f), BtnH);
            bool changed = false;

            GUI.Box(value, GUIContent.none, BuilderSkin.ValueBox);
            string text = count > 0 ? options[Mathf.Clamp(index, 0, count - 1)] : "—";
            GUI.Label(value, text, enabled ? BuilderSkin.ButtonText : BuilderSkin.MutedCenter);

            bool dec = Arrow(left, BuilderSkin.ChevronLeft, enabled && count > 1);
            bool inc = Arrow(right, BuilderSkin.ChevronRight, enabled && count > 1);
            if (enabled && count > 1 && ClickOn(value) == 1)
            {
                inc = true;
            }

            if (dec)
            {
                index = (index - 1 + count) % count;
                changed = true;
            }
            else if (inc)
            {
                index = (index + 1) % count;
                changed = true;
            }

            y += RowH;
            return changed;
        }

        private bool Arrow(Rect r, Texture2D icon, bool enabled)
        {
            bool hover = enabled && Hover(r);
            GUIStyle bg = !enabled ? BuilderSkin.StepDisabled : (hover ? BuilderSkin.StepHover : BuilderSkin.Step);
            GUI.Box(r, GUIContent.none, bg);
            Color previous = GUI.color;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(r.center.x - 8f, r.center.y - 8f, 16f, 16f), icon);
            GUI.color = previous;
            return enabled && ClickOn(r) == 1;
        }

        /// <summary>Horizontal slider with a value readout. Returns true when the value changed.</summary>
        private bool Slider(string label, ref float value, float min, float max, float step, string valueText, bool enabled)
        {
            RowLabel(label, enabled);
            float x = rowW - CtrlW;
            float readoutW = 118f;
            var track = new Rect(x, y + ((RowH - 22f) * 0.5f), CtrlW - readoutW - 10f, 22f);

            GUI.Box(new Rect(track.x, track.y + 6f, track.width, 10f), GUIContent.none, BuilderSkin.Track);
            float t = Mathf.InverseLerp(min, max, value);
            float fillW = 11f + (t * (track.width - 22f));
            Color previous = GUI.color;
            GUI.color = enabled ? BuilderSkin.Accent : new Color(0.45f, 0.45f, 0.47f, 1f);
            GUI.DrawTexture(new Rect(track.x + 3f, track.y + 8f, Mathf.Max(0f, fillW - 3f), 6f), Texture2D.whiteTexture);
            GUI.color = previous;

            bool wasEnabled = GUI.enabled;
            GUI.enabled = enabled && !modal;
            float next = GUI.HorizontalSlider(track, value, min, max, sliderStyle, thumbStyle);
            GUI.enabled = wasEnabled;

            if (step > 0f)
            {
                next = Mathf.Clamp(Mathf.Round((next - min) / step) * step + min, min, max);
            }

            GUI.Label(new Rect(x + track.width + 10f, y, readoutW, RowH), valueText,
                enabled ? BuilderSkin.BodyRight : BuilderSkin.MutedRight);

            y += RowH;
            if (!Mathf.Approximately(next, value))
            {
                value = next;
                return true;
            }

            return false;
        }

        /// <summary>Switch. Returns true when toggled.</summary>
        private bool Toggle(string label, ref bool value, bool enabled)
        {
            RowLabel(label, enabled);
            float x = rowW - CtrlW;
            var area = new Rect(x, y, CtrlW, RowH);
            var sw = new Rect(x + CtrlW - 62f, y + ((RowH - 28f) * 0.5f), 62f, 28f);

            bool hover = enabled && Hover(area);
            GUI.Label(new Rect(x, y, CtrlW - 76f, RowH), value ? "Вкл" : "Выкл", enabled ? BuilderSkin.BodyRight : BuilderSkin.MutedRight);

            Color previous = GUI.color;
            GUI.Box(sw, GUIContent.none, value && enabled ? BuilderSkin.StepActive : BuilderSkin.Track);
            if (hover)
            {
                GUI.color = new Color(1.12f, 1.12f, 1.12f, 1f);
            }

            float knobX = value ? sw.xMax - 26f : sw.x + 4f;
            GUI.Box(new Rect(knobX, sw.y + 4f, 22f, 20f), GUIContent.none, hover ? BuilderSkin.StepHover : BuilderSkin.Step);
            GUI.color = previous;

            y += RowH;
            if (enabled && ClickOn(area) == 1)
            {
                value = !value;
                return true;
            }

            return false;
        }

        private static int NearestIndex(int[] values, int target)
        {
            int best = 0;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < values.Length; i++)
            {
                int diff = Mathf.Abs(values[i] - target);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = i;
                }
            }

            return best;
        }

        // ─── Toast / revert banner ───────────────────────────────────────────

        private void Toast(string text)
        {
            toast = text;
            toastUntil = Time.unscaledTime + 3.5f;
        }

        private void DrawToast(Rect win)
        {
            if (string.IsNullOrEmpty(toast) || Time.unscaledTime > toastUntil)
            {
                return;
            }

            var r = new Rect(win.center.x - 280f, win.yMax - 64f, 560f, 40f);
            GUI.Box(r, GUIContent.none, BuilderSkin.Pill);
            GUI.Label(r, toast, BuilderSkin.PillText);
        }

        private void DrawRevertBanner(Rect win, SettingsApplier applier)
        {
            var dim = new Rect(win.x, win.y, win.width, win.height);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(dim, Texture2D.whiteTexture);
            GUI.color = Color.white;

            var box = new Rect(win.center.x - 270f, win.center.y - 80f, 540f, 160f);
            GUI.Box(box, GUIContent.none, BuilderSkin.Plate);
            GUI.Label(new Rect(box.x, box.y + 16f, box.width, 30f), "Оставить новые параметры экрана?", BuilderSkin.NameTextCenter);
            GUI.Label(new Rect(box.x, box.y + 50f, box.width, 24f),
                "Если ничего не нажать, через " + Mathf.CeilToInt(applier.RevertSecondsLeft) + " с вернутся прежние.",
                BuilderSkin.MutedCenter);

            bool wasModal = modal;
            modal = false;
            mouseInView = true;
            if (Button(new Rect(box.x + 40f, box.yMax - 56f, 210f, BtnH + 4f), "Оставить", ButtonKind.Accent))
            {
                applier.ConfirmDisplay();
                SyncStagedDisplay();
            }

            if (Button(new Rect(box.xMax - 250f, box.yMax - 56f, 210f, BtnH + 4f), "Вернуть", ButtonKind.Normal))
            {
                applier.RevertDisplay();
                SyncStagedDisplay();
            }

            modal = wasModal;
        }
    }
}
