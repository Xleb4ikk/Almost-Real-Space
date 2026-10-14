using System;
using System.Collections.Generic;
using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Панель ΔV справа: столбик ступеней почти во всю высоту экрана (нижняя
    /// ступень — внизу, как в ракете), под столбиком (в полёте — после
    /// примечаний) шапка с общей ΔV, как основание ракеты. Наведёшь курсор на
    /// ступень — из неё выезжает влево карточка: двигатели, топливо, время
    /// работы, тяга, TWR, Isp, массы и предупреждения.
    ///
    /// Два режима:
    ///  • стройка — план: все баки полные, высота плашки ∝ ΔV ступени;
    ///  • полёт — остаток по текущей массе корабля (DeltaV.ComputeInFlight):
    ///    высота плашек по плановой ΔV, заливка снизу — доля оставшейся;
    ///    выработанные ступени притушены, активная подсвечена.
    ///
    /// Рисуется из BuilderUI.OnGUI теми же стилями BuilderSkin. Пересчёт — только
    /// при изменении сборки или массы корабля (с шагом 10 г).
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
        private const float HeaderHeight = 106f;
        private const float NotesWidth = 308f;
        private const float BlockGap = 10f;
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

        private DvResult plan;
        private DvResult live;
        private bool flight;
        private int signature;
        private bool hasSignature;
        private float[] slide = new float[0];
        private int hovered = -1;

        // Прямоугольники прошлой отрисовки: для перехвата кликов (Captures).
        private readonly List<Rect> hitRects = new List<Rect>();

        private float width;
        private float height;
        private float alpha = 1f;

        /// <summary>Результат плана (все баки полные); null — ещё не считали.</summary>
        public DvResult Plan { get { return plan; } }

        /// <summary>Остаток в полёте; null — полёта нет.</summary>
        public DvResult Live { get { return live; } }

        private DvResult Shown { get { return flight && live != null ? live : plan; } }

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

        /// <summary>
        /// Пересчитать план/остаток БЕЗ отрисовки. Нужно, когда панель не
        /// рисуется, а её данные читает кто-то другой (планшет): иначе Live/Plan
        /// замерли бы на кадре последней отрисовки.
        /// </summary>
        public void Tick(SpacecraftBuilder builder)
        {
            if (builder != null)
            {
                Refresh(builder);
            }
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
            DvResult result = Shown;
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
            // Столбик ступеней — почти во всю высоту экрана, под ним (в полёте)
            // примечания, шапка — внизу, как основание «ракеты». Края держим
            // в пределах экрана.
            float notesH = 0f;
            if (flight && result.Notes.Count > 0)
            {
                notesH = 22f;
                for (int i = 0; i < result.Notes.Count; i++)
                {
                    notesH += WarningHeight(result.Notes[i], NotesWidth - 32f - 16f) + 4f;
                }
            }

            float headerY = Mathf.Max(Margin, height - Margin - HeaderHeight);
            float railBottom = headerY - BlockGap - (notesH > 0f ? notesH + BlockGap : 0f);
            float railH = Mathf.Max(1f, railBottom - Margin);

            var header = new Rect(width - Margin - HeaderWidth, headerY, HeaderWidth, HeaderHeight);
            var notesRect = new Rect(width - Margin - NotesWidth, headerY - notesH - BlockGap, NotesWidth, notesH);
            var rail = new Rect(width - Margin - TabWidth - (RailPad * 2f), Margin,
                TabWidth + (RailPad * 2f), railH);
            hitRects.Add(header);

            DrawHeader(header, result);
            if (notesH > 0f)
            {
                DrawNotes(notesRect, result);
                hitRects.Add(notesRect);
            }

            Plate(rail);

            // Высоты плашек — по ПЛАНОВОЙ ΔV: в полёте они не прыгают, а убывает
            // заливка. Не влезло (крошечное окно) — ужимаем плашки и зазоры.
            var heights = new float[n];
            double totalPlan = 0d;
            for (int i = 0; i < n; i++)
            {
                totalPlan += result.Stages[i].PlanDeltaVMs;
            }

            float usable = Mathf.Max(1f, railH - (RailPad * 2f));
            float minTotal = (n * StageMinHeight) + ((n - 1) * StageGap);
            float fit = Mathf.Min(1f, usable / minTotal);
            float gap = StageGap * fit;
            float extra = Mathf.Max(0f, usable - minTotal);
            for (int i = 0; i < n; i++)
            {
                double share = totalPlan > 0d ? result.Stages[i].PlanDeltaVMs / totalPlan : 1d / n;
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
                    else if (slide[i] > 0.05f && CardRect(tabs[i], result.Stages[i], slide[i]).Contains(mouse))
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
                    DvStage stage = result.Stages[i];
                    Rect card = CardRect(tabs[i], stage, slide[i]);
                    DrawCard(card, stage, slide[i], StageColor(stage));
                    hitRects.Add(card);
                }
            }
        }

        // ─── Расчёт по изменению сборки / массы ──────────────────────────────

        private void Refresh(SpacecraftBuilder builder)
        {
            IReadOnlyList<PlacedPart> parts = builder.Parts;
            DvEnvironment env = EnvironmentFor(builder);
            SimulationRunner runner = builder.Runner;
            bool inFlight = builder.PhysicsOn && runner != null && runner.Ship != null;
            double mass = inFlight ? runner.Ship.Mass : 0d;
            StagePropulsion propulsion = inFlight ? runner.Propulsion : null;

            int h = 17;
            unchecked
            {
                h = (h * 31) + (parts != null ? parts.Count : 0);
                h = (h * 31) + env.SurfaceGravity.GetHashCode();
                h = (h * 31) + (inFlight ? 1 : 0);
                if (inFlight)
                {
                    // Масса — с шагом 10 г (legacy-путь); состояние модели —
                    // по её ревизии (списание, зажигание, сброс ступеней).
                    h = (h * 31) + ((long)Math.Round(mass * 100d)).GetHashCode();
                    h = (h * 31) + runner.MainThrustNewtons.GetHashCode();
                    h = (h * 31) + runner.MainIspSeconds.GetHashCode();
                    h = (h * 31) + runner.DryMassKg.GetHashCode();
                    if (propulsion != null)
                    {
                        h = (h * 31) + propulsion.Revision;
                    }
                }

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
            flight = inFlight;
            plan = DeltaV.Compute(parts, env);
            live = null;
            if (inFlight)
            {
                if (propulsion != null)
                {
                    // Остаток — от СОСТОЯНИЯ модели физики (остатки по пулам и
                    // сброшенные ступени), а не от массы: после сброса масса
                    // падает на сухую ступени, и массовая эвристика приняла бы
                    // её за сгоревшее топливо.
                    var pools = new List<StagePoolState>();
                    propulsion.AppendState(pools);
                    live = DeltaV.ComputeFromState(parts, env, pools, propulsion.JettisonedFlags());
                    AddPhysicsNotes(live, plan, runner, propulsion);
                }
                else
                {
                    live = DeltaV.ComputeInFlight(parts, env, mass);
                    AddPhysicsNotes(live, plan, runner, null);
                }
            }
        }

        /// <summary>
        /// Сверка с тем, что реально считает физика полёта. Со ступенчатой
        /// моделью (propulsion != null) физика жжёт те же пулы, что и панель —
        /// расхождений по тяге/сухой массе нет, примечаний не нужно. Без модели
        /// (legacy-сцены) остаётся сверка по инспектору SimulationRunner.
        /// </summary>
        private static void AddPhysicsNotes(
            DvResult liveResult, DvResult planResult, SimulationRunner runner, StagePropulsion propulsion)
        {
            if (propulsion == null)
            {
                if (runner.MainThrustNewtons <= 0d)
                {
                    liveResult.Notes.Add("В физике полёта тяга = 0 (SimulationRunner.MainThrustNewtons): "
                        + "корабль не разгоняется, ΔV — только по сборке");
                }
                else if (liveResult.ActiveStageIndex >= 0)
                {
                    DvStage a = liveResult.Stages[liveResult.ActiveStageIndex];
                    bool thrustOff = a.ThrustN > 0d
                        && Math.Abs(runner.MainThrustNewtons - a.ThrustN) > a.ThrustN * 0.01d;
                    bool ispOff = a.IspS > 0d
                        && Math.Abs(runner.MainIspSeconds - a.IspS) > a.IspS * 0.005d;
                    if (thrustOff || ispOff)
                    {
                        liveResult.Notes.Add("Физика полёта берёт тягу " + Group(runner.MainThrustNewtons / 1000d)
                            + " кН и Isp " + runner.MainIspSeconds.ToString("0.#")
                            + " с из SimulationRunner, а не из двигателей ступени ("
                            + Group(a.ThrustN / 1000d) + " кН, " + a.IspS.ToString("0.#")
                            + " с) — реальный разгон будет другим");
                    }
                }

                double planBurn = 0d;
                for (int i = 0; i < planResult.Stages.Count; i++)
                {
                    planBurn += planResult.Stages[i].BurnedMassKg;
                }

                double simBurn = planResult.TotalMassKg - runner.DryMassKg;
                if (Math.Abs(simBurn - planBurn) > Math.Max(1d, planBurn * 0.005d))
                {
                    liveResult.Notes.Add("Физика остановит двигатель при массе " + DeltaV.FormatKg(runner.DryMassKg)
                        + ": по ней сгорит " + DeltaV.FormatKg(Math.Max(0d, simBurn))
                        + " топлива, по сборке — " + DeltaV.FormatKg(planBurn));
                }
            }

            liveResult.Notes.Add("Отделитель в физике пока не сбрасывает ступень: масса выработанных ступеней "
                + "остаётся на корабле");
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

        // ─── Шапка и примечания ──────────────────────────────────────────────

        private void DrawHeader(Rect rect, DvResult result)
        {
            Plate(rect);
            DvEnvironment env = result.Environment;
            float x = rect.x + 16f;
            float w = rect.width - 32f;

            Label(new Rect(x, rect.y + 9f, w, 16f),
                flight ? "ΔV ОСТАЛОСЬ  ·  " + env.Label.ToUpperInvariant() : "ΔV  ·  " + env.Label.ToUpperInvariant(),
                BuilderSkin.Heading, Color.white);

            Label(new Rect(x, rect.y + 25f, w, 34f),
                Group(result.TotalDeltaVMs) + " м/с", BuilderSkin.TitleText, Color.white);

            float textY = rect.y + 61f;
            if (flight)
            {
                double planTotal = plan != null ? plan.TotalDeltaVMs : 0d;
                float f = planTotal > 0d ? Mathf.Clamp01((float)(result.TotalDeltaVMs / planTotal)) : 0f;
                Solid(new Rect(x, rect.y + 59f, w, 4f), new Color(1f, 1f, 1f, 0.12f));
                Solid(new Rect(x, rect.y + 59f, w * f, 4f), Palette[0]);
                textY = rect.y + 67f;

                Label(new Rect(x, textY, w, 16f),
                    "из " + Group(planTotal) + "  ·  " + FormatMass(result.CurrentMassKg),
                    BuilderSkin.Muted, Color.white);
            }
            else
            {
                Label(new Rect(x, textY, w, 16f),
                    result.Stages.Count + " " + StageWord(result.Stages.Count) + "  ·  " + FormatMass(result.TotalMassKg),
                    BuilderSkin.Muted, Color.white);
            }

            Rect last = new Rect(x, textY + 17f, w, 16f);
            if (result.DetachedParts > 0)
            {
                Label(last, result.DetachedParts + " дет. не соединены — не в расчёте", BuilderSkin.Tiny, WarnColor);
            }
            else
            {
                Label(last, env.BodyName + "  ·  g " + env.SurfaceGravity.ToString("0.00") + " м/с²",
                    BuilderSkin.Tiny, Color.white);
            }
        }

        private void DrawNotes(Rect rect, DvResult result)
        {
            Plate(rect);
            float x = rect.x + 16f;
            float w = rect.width - 32f;
            float y = rect.y + 12f;
            for (int i = 0; i < result.Notes.Count; i++)
            {
                float wh = WarningHeight(result.Notes[i], w - 16f);
                Tint(WarnColor);
                GUI.DrawTexture(new Rect(x + 1f, y + 4f, 8f, 8f), BuilderSkin.Dot);
                GUI.contentColor = WarnColor;
                Tint(Color.white);
                GUI.Label(new Rect(x + 16f, y, w - 16f, wh), result.Notes[i], warnStyle);
                GUI.contentColor = Color.white;
                y += wh + 4f;
            }
        }

        // ─── Плашка ступени ──────────────────────────────────────────────────

        private void DrawTab(Rect tab, DvStage stage, bool hover, float slideT)
        {
            Color color = StageColor(stage);
            float dim = flight && stage.IsSpent ? 0.45f : 1f;

            Tint(Color.white);
            GUI.Box(Expand(tab, 8f), GUIContent.none, BuilderSkin.Glow);
            if (flight && stage.IsActive)
            {
                Tint(new Color(color.r, color.g, color.b, 0.55f));
                GUI.Box(Expand(tab, 7f), GUIContent.none, BuilderSkin.Glow);
            }

            Tint(new Color(1f, 1f, 1f, dim));
            GUI.Box(tab, GUIContent.none, hover ? BuilderSkin.StepHover : BuilderSkin.Step);

            float glow = Mathf.Lerp(flight ? 0.12f : 0.20f, 0.46f, Mathf.SmoothStep(0f, 1f, slideT));
            Tint(new Color(color.r, color.g, color.b, glow * dim));
            GUI.Box(Inset(tab, 3f), GUIContent.none, fillStyle);

            // Полёт: заливка снизу — доля оставшейся ΔV ступени.
            if (flight)
            {
                float f = stage.PlanDeltaVMs > 0d ? Mathf.Clamp01((float)(stage.DeltaVMs / stage.PlanDeltaVMs)) : 0f;
                Rect inner = Inset(tab, 3f);
                float fh = inner.height * f;
                if (fh > 0.5f)
                {
                    var fillRect = new Rect(inner.x, inner.yMax - fh, inner.width, fh);
                    float fa = hover ? 0.58f : 0.46f;
                    if (fh >= 22f)
                    {
                        Tint(new Color(color.r, color.g, color.b, fa * dim));
                        GUI.Box(fillRect, GUIContent.none, fillStyle);
                    }
                    else
                    {
                        Solid(Inset(fillRect, 2f), new Color(color.r, color.g, color.b, fa * dim));
                    }
                }
            }

            // Цветная полоска у правого края — «рельса» ступени.
            Solid(new Rect(tab.xMax - 7f, tab.y + 8f, 3.5f, tab.height - 16f),
                new Color(color.r, color.g, color.b, dim));

            // Номер ступени слева.
            var num = new Rect(tab.x + 6f, tab.y, 26f, tab.height);
            Label(num, stage.Index + 1 + string.Empty, BuilderSkin.NameTextCenter, new Color(1f, 1f, 1f, dim));

            if (flight && stage.IsActive)
            {
                Tint(color);
                GUI.DrawTexture(new Rect(tab.x + 9f, tab.y + 7f, 7f, 7f), BuilderSkin.Dot);
            }

            // ΔV справа от номера.
            var value = new Rect(tab.x + 30f, tab.y + (tab.height * 0.5f) - 17f, tab.width - 42f, 18f);
            var unit = new Rect(tab.x + 30f, tab.y + (tab.height * 0.5f) + 1f, tab.width - 42f, 14f);
            string shown = stage.IsJettisoned
                ? "—"
                : stage.HasEngine && (stage.DeltaVMs > 0d || stage.PlanDeltaVMs > 0d)
                    ? Group(stage.DeltaVMs)
                    : "—";
            string unitText = stage.IsJettisoned ? "сброс" : flight && stage.IsSpent ? "выраб." : "м/с";
            Label(value, shown, BuilderSkin.BodyRight, new Color(1f, 1f, 1f, dim));
            Label(unit, unitText, BuilderSkin.TinyRight, new Color(1f, 1f, 1f, dim));
            Tint(Color.white);
        }

        // ─── Выезжающая карточка ─────────────────────────────────────────────

        private Rect CardRect(Rect tab, DvStage stage, float slideT)
        {
            float e = Mathf.SmoothStep(0f, 1f, slideT);
            float h = CardHeight(stage);
            float w = CardWidth * e;
            float y = Mathf.Clamp(tab.center.y - (h * 0.5f), 66f, Mathf.Max(66f, height - h - 14f));
            return new Rect(tab.x - w - 6f, y, w, h);
        }

        private static float CardHeight(DvStage stage)
        {
            float h = CardPad + 44f + 8f + (RowHeight * 8f) + CardPad;
            for (int i = 0; i < stage.Warnings.Count; i++)
            {
                h += WarningHeight(stage.Warnings[i], CardWidth - (CardPad * 2f) - 16f) + 4f;
            }

            if (stage.Warnings.Count > 0)
            {
                h += 8f;
            }

            return h;
        }

        private static float WarningHeight(string text, float width)
        {
            return Mathf.Max(16f, warnStyle.CalcHeight(new GUIContent(text), width));
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

            string tag = string.Empty;
            if (stage.IsJettisoned)
            {
                tag = "сброшена";
            }
            else if (flight)
            {
                tag = stage.IsSpent ? "выработана" : (stage.IsActive ? "активная" : "ожидает");
            }
            else if (stage.Index == 0)
            {
                tag = "стартовая";
            }

            if (tag.Length > 0)
            {
                GUI.contentColor = flight && stage.IsActive ? color : Color.white;
                GUI.Label(new Rect(x, y, w, 24f), tag, BuilderSkin.TinyRight);
                GUI.contentColor = Color.white;
            }

            string engines = stage.HasEngine ? stage.EngineLabel : "без двигателя";
            GUI.Label(new Rect(x + 20f, y + 22f, w - 20f, 20f), engines, BuilderSkin.Muted);
            y += 44f + 4f;

            Solid(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.10f * e));
            y += 4f;

            bool burns = stage.HasEngine && stage.DeltaVMs > 0d;
            string none = "—";

            string dvText = none;
            if (flight && stage.HasEngine && stage.PlanDeltaVMs > 0d)
            {
                dvText = Group(stage.DeltaVMs) + " из " + Group(stage.PlanDeltaVMs) + " м/с";
            }
            else if (burns)
            {
                dvText = Group(stage.DeltaVMs) + " м/с";
            }

            Row(x, ref y, w, e, flight ? "ΔV осталось" : "ΔV", dvText, color);
            Row(x, ref y, w, e, flight ? "Осталось работать" : "Время работы",
                burns ? FormatTime(stage.BurnTimeS) : none, Color.white);
            Row(x, ref y, w, e, "Тяга", burns ? Group(stage.ThrustN / 1000d) + " кН" : none, Color.white);

            Color twrColor = Color.white;
            if (burns && stage.Index == 0 && stage.TwrStart < 1d)
            {
                twrColor = WarnColor;       // с земли такая ступень не взлетит
            }

            Row(x, ref y, w, e, "TWR",
                burns ? stage.TwrStart.ToString("0.00") + "  →  " + stage.TwrEnd.ToString("0.00") : none, twrColor);
            Row(x, ref y, w, e, "Уд. импульс", burns ? stage.IspS.ToString("0.#") + " с" : none, Color.white);

            string fuel = none;
            if (stage.PropellantLabel.Length > 0)
            {
                bool partial = flight && Math.Abs(stage.PropellantRemainingKg - stage.PropellantMassKg) > 1d;
                fuel = stage.PropellantLabel + "  ·  "
                    + (partial
                        ? FormatMassNoUnit(stage.PropellantRemainingKg) + " / " + FormatMass(stage.PropellantMassKg)
                        : FormatMass(stage.PropellantMassKg));
            }

            Row(x, ref y, w, e, "Топливо", fuel, Color.white);
            Row(x, ref y, w, e, "Сухая масса", FormatMass(stage.DryMassKg), Color.white);

            string massLabel = "Масса на старте";
            string massText = FormatMass(stage.StartMassKg);
            if (flight)
            {
                massLabel = stage.IsActive ? "Масса сейчас" : "Масса на старте";
                massText = stage.IsSpent ? none : massText;
            }

            Row(x, ref y, w, e, massLabel, massText, Color.white);

            if (stage.Warnings.Count > 0)
            {
                y += 4f;
                Solid(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.10f * e));
                y += 8f;
                for (int i = 0; i < stage.Warnings.Count; i++)
                {
                    float wh = WarningHeight(stage.Warnings[i], w - 16f);
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

        private static Color StageColor(DvStage stage)
        {
            return stage.HasEngine ? Palette[stage.Index % Palette.Length] : NoEngineColor;
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
            return DeltaV.FormatKg(kg);
        }

        /// <summary>Масса без единицы — для «остаток / всего т».</summary>
        private static string FormatMassNoUnit(double kg)
        {
            return kg >= 1000d ? (kg / 1000d).ToString("0.0") : kg.ToString("0") + " кг";
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
