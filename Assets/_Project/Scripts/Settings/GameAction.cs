using System;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Every rebindable game action. To add a new key: add a member here, one
    /// line to <see cref="ActionTable"/>, and read it via PlayerInput.Held/Down(GameAction.X).
    /// Runs of consecutive members (Engine1..8, Warp1..7, Hotbar1..9) are indexed
    /// as (GameAction)((int)GameAction.Engine1 + i) — keep them contiguous.
    /// </summary>
    internal enum GameAction
    {
        // On foot
        MoveForward, MoveBack, MoveLeft, MoveRight, Jump, Sprint, Descend, Interact, Lean,

        // Ship flight (seated)
        PitchUp, PitchDown, YawRight, YawLeft, RollRight, RollLeft,
        TransRight, TransLeft, TransUp, TransDown, TransForward, TransBack,
        ThrottleUp, ThrottleDown, ThrottleFull, ThrottleCut, Sas, Rcs, Stage,
        Engine1, Engine2, Engine3, Engine4, Engine5, Engine6, Engine7, Engine8,
        EnginesAllOn, EnginesAllOff, LesAbort, LesJettison,

        // Time
        Pause, Warp1, Warp2, Warp3, Warp4, Warp5, Warp6, Warp7,

        // Building
        BuildPlace, BuildDelete, BuildPeek, BuildUndo, BuildPhysics, BuildInventory, BuildHints,
        Hotbar1, Hotbar2, Hotbar3, Hotbar4, Hotbar5, Hotbar6, Hotbar7, Hotbar8, Hotbar9,
    }

    internal enum ActionGroup { Foot, Flight, Engines, Time, Build }

    /// <summary>Where an action is live. Two actions on one key only conflict if their contexts overlap.</summary>
    [Flags]
    internal enum ActionContext { None = 0, Foot = 1, Ship = 2, Build = 4 }

    internal sealed class ActionInfo
    {
        public GameAction Action;
        public string Label;
        public ActionGroup Group;
        public ActionContext Context;
        public KeyCode Default;
        public KeyCode DefaultAlt;
    }

    internal static class ActionTable
    {
        public static readonly string[] GroupNames =
        {
            "Движение и взаимодействие", "Управление кораблём", "Двигатели", "Время", "Стройка"
        };

        public static readonly ActionInfo[] All;
        private static readonly ActionInfo[] ByAction;

        public static int Count { get { return ByAction.Length; } }

        public static ActionInfo Get(GameAction action) { return ByAction[(int)action]; }

        static ActionTable()
        {
            var list = new System.Collections.Generic.List<ActionInfo>();
            const ActionContext F = ActionContext.Foot, S = ActionContext.Ship, B = ActionContext.Build;
            const ActionGroup gF = ActionGroup.Foot, gS = ActionGroup.Flight, gE = ActionGroup.Engines,
                gT = ActionGroup.Time, gB = ActionGroup.Build;

            void Add(GameAction a, string label, ActionGroup g, ActionContext c, KeyCode key, KeyCode alt = KeyCode.None)
            {
                list.Add(new ActionInfo { Action = a, Label = label, Group = g, Context = c, Default = key, DefaultAlt = alt });
            }

            Add(GameAction.MoveForward, "Вперёд", gF, F, KeyCode.W);
            Add(GameAction.MoveBack, "Назад", gF, F, KeyCode.S);
            Add(GameAction.MoveLeft, "Влево", gF, F, KeyCode.A);
            Add(GameAction.MoveRight, "Вправо", gF, F, KeyCode.D);
            Add(GameAction.Jump, "Прыжок / вверх (джетпак, плавание)", gF, F, KeyCode.Space);
            Add(GameAction.Sprint, "Бег / вниз при парении", gF, F, KeyCode.LeftShift);
            Add(GameAction.Descend, "Вниз (джетпак, плавание)", gF, F, KeyCode.LeftControl);
            Add(GameAction.Interact, "Использовать / сесть / встать", gF, F | S, KeyCode.E);
            Add(GameAction.Lean, "Приблизиться к экрану (удерживать)", gF, F | S, KeyCode.Mouse1);

            Add(GameAction.PitchUp, "Тангаж +", gS, S, KeyCode.W);
            Add(GameAction.PitchDown, "Тангаж −", gS, S, KeyCode.S);
            Add(GameAction.YawRight, "Рыскание +", gS, S, KeyCode.D);
            Add(GameAction.YawLeft, "Рыскание −", gS, S, KeyCode.A);
            Add(GameAction.RollRight, "Крен +", gS, S, KeyCode.E);
            Add(GameAction.RollLeft, "Крен −", gS, S, KeyCode.Q);
            Add(GameAction.TransRight, "РСУ: вправо", gS, S, KeyCode.L);
            Add(GameAction.TransLeft, "РСУ: влево", gS, S, KeyCode.J);
            Add(GameAction.TransUp, "РСУ: вверх", gS, S, KeyCode.I);
            Add(GameAction.TransDown, "РСУ: вниз", gS, S, KeyCode.K);
            Add(GameAction.TransForward, "РСУ: вперёд", gS, S, KeyCode.O);
            Add(GameAction.TransBack, "РСУ: назад", gS, S, KeyCode.U);
            Add(GameAction.ThrottleUp, "Газ: прибавить (удерживать)", gS, S, KeyCode.LeftShift);
            Add(GameAction.ThrottleDown, "Газ: убавить (удерживать)", gS, S, KeyCode.LeftControl);
            Add(GameAction.ThrottleFull, "Газ: максимум", gS, S, KeyCode.Z);
            Add(GameAction.ThrottleCut, "Газ: ноль", gS, S, KeyCode.X);
            Add(GameAction.Sas, "Стабилизация (SAS)", gS, S, KeyCode.T);
            Add(GameAction.Rcs, "РСУ вкл/выкл", gS, S, KeyCode.G);
            Add(GameAction.Stage, "Ступень (зажигание / разделение)", gS, S, KeyCode.Space);

            for (int i = 0; i < 8; i++)
            {
                Add((GameAction)((int)GameAction.Engine1 + i), "Двигатель " + (i + 1) + " вкл/выкл", gE, S, KeyCode.F1 + i);
            }

            Add(GameAction.EnginesAllOn, "Все двигатели: включить", gE, S, KeyCode.F9);
            Add(GameAction.EnginesAllOff, "Все двигатели: выключить", gE, S, KeyCode.F10);
            Add(GameAction.LesAbort, "САС: аварийный увод", gE, S, KeyCode.Backspace);
            Add(GameAction.LesJettison, "САС: отстрел башни", gE, S, KeyCode.Delete);

            Add(GameAction.Pause, "Пауза симуляции", gT, F | S | B, KeyCode.P);
            int[] warp = { 1, 5, 10, 100, 500, 1000, 3000 };
            for (int i = 0; i < 7; i++)
            {
                Add((GameAction)((int)GameAction.Warp1 + i), "Ускорение времени ×" + warp[i], gT, F | S, KeyCode.Alpha1 + i);
            }

            Add(GameAction.BuildPlace, "Установить деталь (с «Удалить» — удалить)", gB, B, KeyCode.Mouse1);
            Add(GameAction.BuildDelete, "Модификатор удаления (удерживать)", gB, B, KeyCode.LeftAlt, KeyCode.RightAlt);
            Add(GameAction.BuildPeek, "Освободить курсор (удерживать)", gB, B, KeyCode.LeftControl);
            Add(GameAction.BuildUndo, "Отменить последнюю деталь (с «Освободить курсор»)", gB, B, KeyCode.Z);
            Add(GameAction.BuildPhysics, "Физика / стройка (удерживать)", gB, B, KeyCode.R);
            Add(GameAction.BuildInventory, "Инвентарь деталей", gB, B, KeyCode.I);
            Add(GameAction.BuildHints, "Показать/скрыть подсказки", gB, B, KeyCode.H);
            for (int i = 0; i < 9; i++)
            {
                Add((GameAction)((int)GameAction.Hotbar1 + i), "Слот панели " + (i + 1), gB, B, KeyCode.Alpha1 + i);
            }

            ByAction = new ActionInfo[list.Count];
            foreach (ActionInfo info in list)
            {
                ByAction[(int)info.Action] = info;
            }

            All = list.ToArray();
        }
    }
}
