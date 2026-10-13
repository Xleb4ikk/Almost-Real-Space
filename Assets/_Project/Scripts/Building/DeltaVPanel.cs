using System;
using System.Collections.Generic;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Панель ΔV справа: столбик ступеней почти во всю высоту экрана (нижняя
    /// ступень — внизу, как в ракете; высота плашки пропорциональна её ΔV),
    /// под столбиком — шапка с общей ΔV, как основание ракеты. Наведёшь курсор
    /// на ступень — из неё выезжает влево карточка: двигатели, топливо, время
    /// работы, тяга, TWR, Isp, массы и предупреждения.
    ///
    /// Рисуется из BuilderUI.OnGUI теми же стилями BuilderSkin. Расчёт —
    /// DeltaV.Compute; пересчитывается только при изменении сборки.
    /// Наведение работает при свободном курсоре (удержание Ctrl, окно
    /// инвентаря или Esc); подсказка — в панели подсказок (H).
    /// </summary>
    public sealed class DeltaVPanel
    {
        // Геометрия (виртуальные пиксели при высоте экрана 1080).
        private const float Margin = 18f;
        private const float TabWidth = 92f;
        private const float RailPad = 9f;
        private const float StageMinHeight = 52f;
        private const float StageGap = 8f;
        private const float HeaderWidth = 224f;
        private const float HeaderHeight = 98f;
        private const float HeaderGap = 10f;
        private const float CardWidth = 300f;
        private const float CardPad = 16f;
        private const float RowHeight = 23f;
        private const float SlideSpeed = 7f;

        private static readonly Color[] Palette =
        {
            new Color(0.36f, 0.64f, 0.96f, 1f),
            new Color(0.30f, 0.80f, 0.74f, 1f),
            new Color(0.96f, 0.74f, 0.32f, 1f),
            new Color(0.74f, 0.58f, 0.96f, 1f),
            new Color(0.94f, 0.52f, 0.58f, 1f),
            new Color(0.58f, 0.82f, 0.42f, 1f),
        };

        private static readonly Color NoEngineColor = new Color(0.55f, 0.57f, 0.60f, 1f);
        private static readonly Color WarnColor = new Color(0.98f, 0.76f, 0.36f, 1f);

        private static Texture2D fillTexture;
        private static GUIStyle fillStyle;
        private static GUIStyle warnStyle;

        private DvResult result;
        private int signature;
        private bool hasSignature;
        private float[] slide = new float[0];
        private int hovered = -1;

        // Прямоугольники прошлой отрисовки: для перехвата кликов (Captures).
        private readonly List<Rect> hitRects = new List<Rect>();

        private float width;
        private float height;
        private float alpha = 1f;

        /// <summary>Результат последнего расчёта (null — ещё не считали).</summary>
        public DvResult Result { get { return result; } }

        /// <summary>
        /// Курсор над панелью (плашка ступени или выехавшая карточка): BuilderUI
        /// гасит клики/колесо, чтобы они не проваливались в окно инвентаря под
        /// карточкой.
        /// </summary>
        public bool Captures(Vector2 mouse)
        {
            for (int i = 0; i < hitRects.Count; i++)
            {
                if (hitRects[i].Contains(mouse))
                {
                    return true;
                }
            }

            return false;
        }

        public void Draw(SpacecraftBuilder builder, float uiWidth, float uiHeight, float uiAlpha)
        {
            hitRects.Clear();
            if (builder == null)
            {
                return;
            }

            width = uiWidth;
            height = uiHeight;
            alpha = uiAlpha;
            EnsureStyles();
            Refresh(builder);
            if (result == null || result.IsEmpty)
            {
                hovered = -1;
                return;
            }

            int n = result.Stages.Count;
            if (slide.Length != n)
            {
                slide = new float[n];
                hovered = -1;
            }

            bool interactive = Cursor.lockState != CursorLockMode.Locked;
            Vector2 mouse = Event.current.mousePosition;
            bool repaint = Event.current.type == EventType.Repaint;

            // ─── Раскладка ───────────────────────────────────────────────────
            // Столбик ступеней — почти во всю высоту экрана, шапка — внизу
            // (основание «ракеты»). Края держим в пределах экрана.
            float headerY = Mathf.Max(Margin, height - Margin - HeaderHeight);
            float railH = Mathf.Max(1f, headerY - HeaderGap - Margin);

            var header = new Rect(width - Margin - HeaderWidth, headerY, HeaderWidth, HeaderHeight);
            var rail = new Rect(width - Margin - TabWidth - (RailPad * 2f), Margin,
                TabWidth + (RailPad * 2f), railH);
            hitRects.Add(header);

            DrawHeader(header);
            Plate(rail);

            // Высоты плашек: минимум + доля ΔV от оставшегося места. Не влезло
            // (крошечное окно) — ужимаем плашки и зазоры пропорционально.
            var heights = new float[n];
            double totalDv = 0d;
            for (int i = 0; i < n; i++)
            {
                totalDv += result.Stages[i].DeltaVMs;
            }

            float usable = Mathf.Max(1f, railH - (RailPad * 2f));
            float minTotal = (n * StageMinHeight) + ((n - 1) * StageGap);
            float fit = Mathf.Min(1f, usable / minTotal);
            float gap = StageGap * fit;
            float extra = Mathf.Max(0f, usable - minTotal);
            for (int i = 0; i < n; i++)
            {
                double share = totalDv > 0d ? result.Stages[i].DeltaVMs / totalDv : 1d / n;
                heights[i] = (StageMinHeight * fit) + (extra * (float)share);
            }

            // Нижняя ступень (индекс 0) — снизу, над шапкой.
            var tabs = new Rect[n];
            float cursorY = rail.yMax - RailPad;
            for (int i = 0; i < n; i++)
            {
                cursorY -= heights[i];
                tabs[i] = new Rect(rail.x + RailPad, cursorY, TabWidth, heights[i]);
                cursorY -= gap;
            }

            // ─── Наведение и анимация выезда ─────────────────────────────────
            int newHover = -1;
            if (interactive)
            {
                for (int i = 0; i < n && newHover < 0; i++)
                {
                    if (tabs[i].Contains(mouse))
                    {
                        newHover = i;
                    }
                    else if (slide[i] > 0.05f && CardRect(tabs[i], i, slide[i]).Contains(mouse))
                    {
                        newHover = i;
                    }
                }
            }

            hovered = newHover;
            if (repaint)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                for (int i = 0; i < n; i++)
                {
                    slide[i] = Mathf.MoveTowards(slide[i], i == hovered ? 1f : 0f, dt * SlideSpeed);
                }
            }

            // ─── Плашки ступеней ─────────────────────────────────────────────
            for (int i = 0; i < n; i++)
            {
                DrawTab(tabs[i], result.Stages[i], i == hovered, slide[i]);
                hitRects.Add(tabs[i]);

                // Отметка отделителя между ступенями.
                if (i < n - 1)
                {
                    var mark = new Rect(tabs[i].x + 14f, tabs[i].y - (gap * 0.5f) - 1f, TabWidth - 28f, 2f);
                    Solid(mark, new Color(1f, 1f, 1f, 0.16f));
                }
            }

            // Выехавшие карточки — поверх всего.
            for (int i = 0; i < n; i++)
            {
                if (slide[i] > 0.01f)
                {
                    Rect card = CardRect(tabs[i], i, slide[i]);
                    DrawCard(card, result.Stages[i], slide[i], StageColor(result.Stages[i], i));
                    hitRects.Add(card);
                }
            }
        }

        // ─── Расчёт по изменению сборки ──────────────────────────────────────

        private void Refresh(SpacecraftBuilder builder)
        {
            IReadOnlyList<PlacedPart> parts = builder.Parts;
            DvEnvironment env = EnvironmentFor(builder);

            int h = 17;
            unchecked
            {
                h = (h * 31) + (parts != null ? parts.Count : 0);
                h = (h * 31) + env.SurfaceGravity.GetHashCode();
                if (parts != null)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        PlacedPart p = parts[i];
                        h = (h * 31) + (int)p.Kind;
                        h = (h * 31) + p.Metrics.MassKg.GetHashCode();
                        h = (h * 31) + p.Pivot.y.GetHashCode();
                        h = (h * 31) + p.ParentIndex;
                        h = (h * 31) + (p.EngineSpec != null ? p.EngineSpec.Id.GetHashCode() : 0);
                    }
                }
            }

            if (hasSignature && h == signature)
            {
                return;
            }

            signature = h;
            hasSignature = true;
            result = DeltaV.Compute(parts, env);
        }

        /// <summary>
        /// Условия расчёта. Сейчас — вакуум у доминирующего тела (g на его
        /// поверхности нужен только для TWR). Выбор планеты и высоты —
        /// сюда же, в DvEnvironment.
        /// </summary>
        private static DvEnvironment EnvironmentFor(SpacecraftBuilder builder)
        {
            OrbitingBody body = builder.Runner != null ? builder.Runner.DominantBody : null;
            if (body == null || body.Radius <= 0d)
            {
                return new DvEnvironment(null, DvEnvironment.G0);
            }

            double g = body.StandardGravitationalParameter / (body.Radius * body.Radius);
            return new DvEnvironment(body.Name, g);
        }

        // ─── Шапка ───────────────────────────────────────────────────────────

        private void DrawHeader(Rect rect)
        {
            Plate(rect);
            DvEnvironment env = result.Environment;

            Label(new Rect(rect.x + 16f, rect.y + 9f, rect.width - 32f, 16f),
                "ΔV  ·  " + env.Label.ToUpperInvariant(), BuilderSkin.Heading, Color.white);

            Label(new Rect(rect.x + 16f, rect.y + 25f, rect.width - 32f, 34f),
                Group(result.TotalDeltaVMs) + " м/с", BuilderSkin.TitleText, Color.white);

            string sub = result.Stages.Count + " " + StageWord(result.Stages.Count)
                + "  ·  " + FormatMass(result.TotalMassKg);
            Label(new Rect(rect.x + 16f, rect.y + 59f, rect.width - 32f, 16f),
                sub, BuilderSkin.Muted, Color.white);

            if (result.DetachedParts > 0)
            {
                Label(new Rect(rect.x + 16f, rect.y + 76f, rect.width - 32f, 16f),
                    result.DetachedParts + " дет. не соединены — не в расчёте", BuilderSkin.Tiny, WarnColor);
            }
            else
            {
                Label(new Rect(rect.x + 16f, rect.y + 76f, rect.width - 32f, 16f),
                    env.BodyName + "  ·  g " + env.SurfaceGravity.ToString("0.00") + " м/с²",
                    BuilderSkin.Tiny, Color.white);
            }
        }

        // ─── Плашка ступени ──────────────────────────────────────────────────

        private void DrawTab(Rect tab, DvStage stage, bool hover, float slideT)
        {
            Color color = StageColor(stage, stage.Index);

            Tint(Color.white);
            GUI.Box(Expand(tab, 8f), GUIContent.none, BuilderSkin.Glow);
            GUI.Box(tab, GUIContent.none, hover ? BuilderSkin.StepHover : BuilderSkin.Step);

            float glow = Mathf.Lerp(0.20f, 0.46f, Mathf.SmoothStep(0f, 1f, slideT));
            Tint(new Color(color.r, color.g, color.b, glow));
            GUI.Box(Inset(tab, 3f), GUIContent.none, fillStyle);

            // Цветная полоска у правого края — «рельса» ступени.
            Solid(new Rect(tab.xMax - 7f, tab.y + 8f, 3.5f, tab.height - 16f), color);

            // Номер ступени слева.
            var num = new Rect(tab.x + 6f, tab.y, 26f, tab.height);
            Label(num, stage.Index + 1 + string.Empty, BuilderSkin.NameTextCenter, Color.white);

            // ΔV справа от номера.
            var value = new Rect(tab.x + 30f, tab.y + (tab.height * 0.5f) - 17f, tab.width - 42f, 18f);
            var unit = new Rect(tab.x + 30f, tab.y + (tab.height * 0.5f) + 1f, tab.width - 42f, 14f);
            Label(value, stage.HasEngine ? Group(stage.DeltaVMs) : "—", BuilderSkin.BodyRight, Color.white);
            Label(unit, "м/с", BuilderSkin.TinyRight, Color.white);
            Tint(Color.white);
        }

        // ─── Выезжающая карточка ─────────────────────────────────────────────

        private Rect CardRect(Rect tab, int stageIndex, float slideT)
        {
            float e = Mathf.SmoothStep(0f, 1f, slideT);
            float h = CardHeight(result.Stages[stageIndex]);
            float w = CardWidth * e;
            float y = Mathf.Clamp(tab.center.y - (h * 0.5f), 66f, Mathf.Max(66f, height - h - 14f));
            return new Rect(tab.x - w - 6f, y, w, h);
        }

        private static float CardHeight(DvStage stage)
        {
            float h = CardPad + 44f + 8f + (RowHeight * 8f) + CardPad;
            for (int i = 0; i < stage.Warnings.Count; i++)
            {
                h += WarningHeight(stage.Warnings[i]) + 4f;
            }

            if (stage.Warnings.Count > 0)
            {
                h += 8f;
            }

            return h;
        }

        private static float WarningHeight(string text)
        {
            float w = CardWidth - (CardPad * 2f) - 16f;
            return Mathf.Max(16f, warnStyle.CalcHeight(new GUIContent(text), w));
        }

        private void DrawCard(Rect card, DvStage stage, float slideT, Color color)
        {
            float e = Mathf.SmoothStep(0f, 1f, slideT);
            Tint(Color.white);
            GUI.Box(Expand(card, 16f), GUIContent.none, BuilderSkin.Glow);
            GUI.Box(card, GUIContent.none, BuilderSkin.Window);

            // Содержимое «приклеено» к правому краю и обрезается группой —
            // так оно выезжает вместе с карточкой, а не растягивается.
            GUI.BeginGroup(card);
            float ox = card.width - CardWidth;
            float x = ox + CardPad;
            float w = CardWidth - (CardPad * 2f);
            float y = CardPad;

            // Заголовок.
            var dot = new Rect(x, y + 6f, 12f, 12f);
            Tint(new Color(color.r, color.g, color.b, e));
            GUI.DrawTexture(dot, BuilderSkin.Dot);
            Tint(new Color(1f, 1f, 1f, e));
            GUI.Label(new Rect(x + 20f, y, w - 20f, 24f), "Ступень " + (stage.Index + 1), BuilderSkin.NameText);
            string tag = stage.Index == 0 ? "стартовая" : string.Empty;
            if (tag.Length > 0)
            {
                GUI.Label(new Rect(x, y, w, 24f), tag, BuilderSkin.TinyRight);
            }

            string engines = stage.HasEngine ? stage.EngineLabel : "без двигателя";
            GUI.Label(new Rect(x + 20f, y + 22f, w - 20f, 20f), engines, BuilderSkin.Muted);
            y += 44f + 4f;

            Solid(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.10f * e));
            y += 4f;

            bool burns = stage.HasEngine && stage.DeltaVMs > 0d;
            string none = "—";

            Row(x, ref y, w, e, "ΔV", burns ? Group(stage.DeltaVMs) + " м/с" : none, color);
            Row(x, ref y, w, e, "Время работы", burns ? FormatTime(stage.BurnTimeS) : none, Color.white);
            Row(x, ref y, w, e, "Тяга", burns ? Group(stage.ThrustN / 1000d) + " кН" : none, Color.white);

            Color twrColor = Color.white;
            if (burns && stage.Index == 0 && stage.TwrStart < 1d)
            {
                twrColor = WarnColor;       // с земли такая ступень не взлетит
            }

            Row(x, ref y, w, e, "TWR",
                burns ? stage.TwrStart.ToString("0.00") + "  →  " + stage.TwrEnd.ToString("0.00") : none, twrColor);
            Row(x, ref y, w, e, "Уд. импульс", burns ? stage.IspS.ToString("0.#") + " с" : none, Color.white);

            string fuel = stage.PropellantLabel.Length > 0
                ? stage.PropellantLabel + "  ·  " + FormatMass(stage.PropellantMassKg)
                : none;
            Row(x, ref y, w, e, "Топливо", fuel, Color.white);
            Row(x, ref y, w, e, "Сухая масса", FormatMass(stage.DryMassKg), Color.white);
            Row(x, ref y, w, e, "Масса на старте", FormatMass(stage.StartMassKg), Color.white);

            if (stage.Warnings.Count > 0)
            {
                y += 4f;
                Solid(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.10f * e));
                y += 8f;
                for (int i = 0; i < stage.Warnings.Count; i++)
                {
                    float wh = WarningHeight(stage.Warnings[i]);
                    Tint(new Color(WarnColor.r, WarnColor.g, WarnColor.b, e));
                    GUI.DrawTexture(new Rect(x + 1f, y + 4f, 8f, 8f), BuilderSkin.Dot);
                    GUI.contentColor = WarnColor;
                    Tint(new Color(1f, 1f, 1f, e));
                    GUI.Label(new Rect(x + 16f, y, w - 16f, wh), stage.Warnings[i], warnStyle);
                    GUI.contentColor = Color.white;
                    y += wh + 4f;
                }
            }

            GUI.EndGroup();
            Tint(Color.white);
        }

        private void Row(float x, ref float y, float w, float e, string label, string value, Color valueColor)
        {
            Tint(new Color(1f, 1f, 1f, e));
            GUI.Label(new Rect(x, y, w * 0.45f, RowHeight), label, BuilderSkin.Muted);
            GUI.contentColor = valueColor;
            GUI.Label(new Rect(x + (w * 0.40f), y, w * 0.60f, RowHeight), value, BuilderSkin.BodyRight);
            GUI.contentColor = Color.white;
            y += RowHeight;
        }

        // ─── Рисование и форматы ─────────────────────────────────────────────

        private static Color StageColor(DvStage stage, int index)
        {
            return stage.HasEngine ? Palette[index % Palette.Length] : NoEngineColor;
        }

        private void Plate(Rect rect)
        {
            Tint(BuilderSkin.Shadow);
            GUI.Box(Expand(rect, 18f), GUIContent.none, BuilderSkin.Glow);
            Tint(Color.white);
            GUI.Box(rect, GUIContent.none, BuilderSkin.Plate);
        }

        private void Label(Rect rect, string text, GUIStyle style, Color tint)
        {
            GUI.contentColor = tint;
            Tint(Color.white);
            GUI.Label(rect, text, style);
            GUI.contentColor = Color.white;
        }

        private void Solid(Rect rect, Color color)
        {
            Tint(color);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            Tint(Color.white);
        }

        private void Tint(Color color)
        {
            GUI.color = new Color(color.r, color.g, color.b, color.a * alpha);
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + (amount * 2f), rect.height + (amount * 2f));
        }

        private static Rect Inset(Rect rect, float amount)
        {
            return Expand(rect, -amount);
        }

        /// <summary>Число с разрядами через пробел: 12345.6 → «12 346».</summary>
        private static string Group(double value)
        {
            long v = (long)Math.Round(value);
            string s = Math.Abs(v).ToString();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && (s.Length - i) % 3 == 0)
                {
                    sb.Append(' ');
                }

                sb.Append(s[i]);
            }

            return (v < 0 ? "−" : string.Empty) + sb;
        }

        private static string FormatMass(double kg)
        {
            return kg >= 1000d ? (kg / 1000d).ToString("0.0") + " т" : kg.ToString("0") + " кг";
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 10d)
            {
                return seconds.ToString("0.0") + " с";
            }

            long s = (long)Math.Round(seconds);
            if (s < 60)
            {
                return s + " с";
            }

            if (s < 3600)
            {
                return (s / 60) + " мин " + (s % 60).ToString("00") + " с";
            }

            return (s / 3600) + " ч " + ((s % 3600) / 60).ToString("00") + " мин";
        }

        private static string StageWord(int n)
        {
            int m10 = n % 10;
            int m100 = n % 100;
            if (m10 == 1 && m100 != 11)
            {
                return "ступень";
            }

            return m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "ступени" : "ступеней";
        }

        // ─── Стили ───────────────────────────────────────────────────────────

        private static void EnsureStyles()
        {
            if (fillStyle == null || fillTexture == null)
            {
                fillTexture = RoundedWhite(32, 9f);
                fillStyle = new GUIStyle
                {
                    border = new RectOffset(11, 11, 11, 11),
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0)
                };
                fillStyle.normal.background = fillTexture;
            }

            if (warnStyle == null)
            {
                warnStyle = new GUIStyle(BuilderSkin.Tiny)
                {
                    fontSize = 12,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                    clipping = TextClipping.Overflow
                };
                warnStyle.normal.textColor = Color.white;
            }
        }

        private static Texture2D RoundedWhite(int size, float radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    float dy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    float outside = Mathf.Sqrt((Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f)) + (Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f)));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                    float a = Mathf.Clamp01(0.5f - d);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
