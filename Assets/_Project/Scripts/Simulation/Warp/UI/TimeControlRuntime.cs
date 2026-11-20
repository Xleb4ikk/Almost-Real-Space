using System;
using Galilego.Building;
using Galilego.Events;
using Galilego.Simulation;
using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Связка панели времени с симуляцией. Владеет моделью панели и таймлайном точек,
    /// снимает снимки по РЕАЛЬНЫМ минутам, применяет откат и переключает режимы варпа.
    ///
    /// Разделение шкал:
    ///  • RealSeconds — реальные секунды полёта (Kahan-аккумулятор, не t += dt): по ним
    ///    сетка точек (одна точка в минуту);
    ///  • FlightSeconds = Runner.TimeSeconds − launchTime: модельное время, которое
    ///    рисуется на точке и в главном таймере.
    ///
    /// Откат — это ВОССТАНОВЛЕНИЕ СНИМКА, а не обратная интеграция: тела и эфемериды —
    /// чистые функции времени, поэтому достаточно вернуть часы и состояние корабля,
    /// игрока, топлива, SAS и обломков.
    ///
    /// Запрос из IMGUI только ставится в очередь: мир меняется в начале Update раннера,
    /// до шага физики, иначе тот же кадр затёр бы восстановленное.
    /// </summary>
    public sealed class TimeControlRuntime
    {
        public static TimeControlRuntime Current { get; private set; }

        /// <summary>Снимок одной точки (мир целиком).</summary>
        private sealed class CheckpointState
        {
            public SimulationRunner.WorldSnapshot World;
        }

        /// <summary>Точка, на которую просили откатиться; −1 — запроса нет.</summary>
        private const int NoRequest = -1;

        private static readonly double[] FullFactors = TimeControlPanel.FullFactors;

        private readonly SimulationRunner runner;
        private readonly FlightCheckpointTimeline<CheckpointState> timeline =
            new FlightCheckpointTimeline<CheckpointState>();

        private double launchTimeSeconds;
        private double lastSimTime;
        private bool started;
        private bool pointsDirty;
        private int pendingRewind = NoRequest;
        private float noticeUntil;

        internal TimeControlModel Model { get; }

        public TimeControlRuntime(SimulationRunner runner)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            Model = new TimeControlModel();
            Model.RewindRequested += RequestRewind;
            Model.PhysicalFactorChosen += ChoosePhysical;
            Model.FullFactorChosen += ChooseFull;
            Current = this;
        }

        /// <summary>Отписка при уничтожении раннера (смена сцены).</summary>
        public void Dispose()
        {
            Model.RewindRequested -= RequestRewind;
            Model.PhysicalFactorChosen -= ChoosePhysical;
            Model.FullFactorChosen -= ChooseFull;
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }

        // ─── Кадр ────────────────────────────────────────────────────────────

        /// <summary>
        /// Тик после шага физики (LateUpdate раннера): часы, точки, показания модели.
        /// Вызывается и когда корабль разрушен или игра на паузе — откат должен работать.
        /// </summary>
        public void Tick(float realDt)
        {
            if (runner == null)
            {
                return;
            }

            double simTime = runner.TimeSeconds;
            bool advanced = simTime != lastSimTime;
            lastSimTime = simTime;

            if (!started)
            {
                TryAutoStart();
            }
            else if (advanced && realDt > 0f)
            {
                // Часы идут только когда модельное время реально изменилось: пауза,
                // билд-режим и разрушенный корабль сами по себе время не двигают.
                int due;
                if (timeline.Advance(realDt, out due))
                {
                    while (timeline.NextCaptureIndex <= due)
                    {
                        CapturePoint(timeline.NextCaptureIndex, FlightPointKind.Flight);
                    }
                }
            }

            SyncModel();

            if (noticeUntil > 0f && Time.unscaledTime >= noticeUntil)
            {
                noticeUntil = 0f;
                Model.Notice = string.Empty;
            }
        }

        private void TryAutoStart()
        {
            if (runner.Ship == null || runner.BuildMode || ContactZoneHost.Active)
            {
                return;
            }

            if (runner.Regime == VesselRegime.Destroyed)
            {
                return;
            }

            // Сцену стартовали не через билдер (чит-спавн/тестовая сцена): точка 0 —
            // текущий кадр, обычный снимок.
            BeginRecording(runner.TimeSeconds, FlightPointKind.Flight);
        }

        // ─── Запись ──────────────────────────────────────────────────────────

        /// <summary>
        /// Новый полёт: таймлайн обнуляется, точка 0 снимается в момент включения физики.
        /// Вызывается из SpacecraftBuilder.EnterFlightAt (и из авто-старта).
        /// </summary>
        public void ResetFlightTimeline()
        {
            BeginRecording(runner.TimeSeconds, FlightPointKind.Launch);
        }

        /// <summary>Отметка «это повторный вход после отката на точку 0»: шкалу не чистим.</summary>
        public void MarkReenteringFromRewind()
        {
            started = true;
            pointsDirty = true;
            SetNotice("перемотка на старт", 2.5f);
        }

        private void BeginRecording(double launchTime, FlightPointKind kind)
        {
            timeline.ResetForLaunch();
            launchTimeSeconds = launchTime;
            lastSimTime = runner.TimeSeconds;
            started = true;
            pointsDirty = true;
            CapturePoint(0, kind);
        }

        /// <summary>
        /// Снять точку. В контактной зоне (PhysX) снимок не хранится: откат туда
        /// невосстановим, точка остаётся серой, чтобы индексы не съезжали.
        /// </summary>
        private void CapturePoint(int index, FlightPointKind kind)
        {
            double flightSeconds = runner.TimeSeconds - launchTimeSeconds;
            bool blocked = index != 0 && ContactZoneHost.Active;
            if (blocked)
            {
                timeline.Capture(index, timeline.RealSeconds, flightSeconds, FlightPointKind.Blocked, null);
                pointsDirty = true;
                return;
            }

            var state = new CheckpointState { World = runner.CaptureWorldSnapshot() };
            timeline.Capture(index, timeline.RealSeconds, flightSeconds, kind, state);
            pointsDirty = true;
        }

        // ─── Откат ───────────────────────────────────────────────────────────

        /// <summary>Из IMGUI: только очередь, мир не трогаем. Один запрос на кадр.</summary>
        private void RequestRewind(int index)
        {
            pendingRewind = index;
        }

        /// <summary>
        /// Применить запрос. Вызывается в начале Update раннера, ДО шага физики.
        /// </summary>
        public void ApplyPendingRewind()
        {
            if (pendingRewind == NoRequest)
            {
                return;
            }

            int index = pendingRewind;
            pendingRewind = NoRequest;

            if (!started)
            {
                SetNotice("полёт ещё не начался");
                return;
            }

            if (ContactZoneHost.Active)
            {
                SetNotice("перемотка недоступна на площадке");
                return;
            }

            if (runner.BuildMode)
            {
                SetNotice("перемотка недоступна в режиме стройки");
                return;
            }

            FlightCheckpoint<CheckpointState> point = timeline.PointAt(index);
            if (point == null)
            {
                SetNotice("точка недоступна");
                return;
            }

            if (index == 0 && point.Kind == FlightPointKind.Launch)
            {
                RewindToLaunch(point);
                return;
            }

            if (point.State == null || point.Kind == FlightPointKind.Blocked)
            {
                SetNotice("точка недоступна");
                return;
            }

            if (timeline.Rewind(index) == null)
            {
                SetNotice("точка недоступна");
                return;
            }

            runner.RestoreWorldSnapshot(point.State.World);
            ForcePhysicalOne();
            SetNotice("перемотано на " + index + " мин", 2.5f);
        }

        /// <summary>
        /// Точка 0 восстанавливается через билдер: PhysX-зона создаётся заново, а состояние
        /// аппарата детерминировано чертежом. Если билдера нет (чит-спавн) — обычный снимок.
        /// </summary>
        private void RewindToLaunch(FlightCheckpoint<CheckpointState> point)
        {
            SpacecraftBuilder builder = UnityEngine.Object.FindAnyObjectByType<SpacecraftBuilder>();
            if (builder != null && builder.CanRewindToLaunch)
            {
                timeline.Rewind(0);
                ForcePhysicalOne();
                if (builder.RewindToLaunch())
                {
                    MarkReenteringFromRewind();
                    return;
                }
            }

            if (point.State == null)
            {
                SetNotice("точка недоступна");
                return;
            }

            timeline.Rewind(0);
            runner.RestoreWorldSnapshot(point.State.World);
            ForcePhysicalOne();
            SetNotice("перемотано на старт", 2.5f);
        }

        /// <summary>
        /// После любой перемотки — строго физический ×1. Варп из снимка НЕ восстанавливается:
        /// иначе откат возвращал бы игрока в прошлое вместе с его прошлым ускорением времени.
        /// </summary>
        private void ForcePhysicalOne()
        {
            if (runner.Warp != null)
            {
                runner.Warp.ResetToPhysicalOne();
                runner.Warp.ResetTicks(runner.TimeSeconds);
            }

            runner.ResetLongWarpState();
        }

        // ─── Варп ────────────────────────────────────────────────────────────

        private void ChoosePhysical(int factor)
        {
            if (runner.Warp == null)
            {
                return;
            }

            runner.Warp.SetPhysicalWarp(factor);
        }

        private void ChooseFull(int index)
        {
            if (runner.Warp == null)
            {
                return;
            }

            if (index < 0 || index >= FullFactors.Length)
            {
                return;
            }

            runner.Warp.SetFullWarp(FullFactors[index]);
        }

        // ─── Модель ──────────────────────────────────────────────────────────

        /// <summary>
        /// Диагностика (Play Mode-тесты и чит-меню): поставить откат в очередь. Мир
        /// меняется в ближайшем ApplyPendingRewind — как и настоящий запрос из панели.
        /// </summary>
        public void QueueRewind(int index)
        {
            RequestRewind(index);
        }

        public int DebugPointCount { get { return timeline.PointCount; } }

        public int DebugNextCaptureIndex { get { return timeline.NextCaptureIndex; } }

        public double DebugRealSeconds { get { return timeline.RealSeconds; } }

        public double DebugPointRealSeconds(int index)
        {
            FlightCheckpoint<CheckpointState> point = timeline.PointAt(index);
            return point != null ? point.RealSeconds : double.NaN;
        }

        public double DebugPointFlightSeconds(int index)
        {
            FlightCheckpoint<CheckpointState> point = timeline.PointAt(index);
            return point != null ? point.FlightSeconds : double.NaN;
        }

        public FlightPointKind DebugPointKind(int index)
        {
            FlightCheckpoint<CheckpointState> point = timeline.PointAt(index);
            return point != null ? point.Kind : FlightPointKind.Blocked;
        }

        public bool DebugPointAvailable(int index)
        {
            return timeline.IsAvailable(index);
        }

        public int DebugStoredStates { get { return timeline.StoredStateCount; } }

        /// <summary>Время включения физики (FlightSeconds = TimeSeconds − launchTime).</summary>
        public double DebugLaunchTimeSeconds { get { return launchTimeSeconds; } }

        public bool DebugStarted { get { return started; } }

        /// <summary>Текст сообщения панели (для отчётов/тестов).</summary>
        public string DebugNotice { get { return Model.Notice; } }

        private void SyncModel()
        {
            if (!started)
            {
                Model.CurrentRealMinutes = 0d;
                Model.CurrentFlightSeconds = 0d;
                return;
            }

            Model.CurrentRealMinutes = timeline.PlayheadMinutes;
            Model.CurrentFlightSeconds = runner.TimeSeconds - launchTimeSeconds;

            if (pointsDirty)
            {
                pointsDirty = false;
                var list = Model.PointFlightSeconds;
                var available = Model.PointAvailable;
                while (list.Count < timeline.PointCount)
                {
                    list.Add(0d);
                    available.Add(false);
                }

                while (list.Count > timeline.PointCount)
                {
                    list.RemoveAt(list.Count - 1);
                    available.RemoveAt(available.Count - 1);
                }

                for (int i = 0; i < timeline.PointCount; i++)
                {
                    list[i] = timeline.PointAt(i).FlightSeconds;
                    available[i] = timeline.IsAvailable(i);
                }
            }

            WarpController warp = runner.Warp;
            if (warp == null)
            {
                return;
            }

            Model.ActiveKind = warp.Mode == WarpMode.Full ? WarpKind.Full : WarpKind.Physical;
            Model.PhysicalFactor = warp.Mode == WarpMode.Physical ? (int)warp.Factor : warp.LastPhysicalFactor;

            Model.FullIndex = -1;
            if (warp.Mode == WarpMode.Full)
            {
                for (int i = 0; i < FullFactors.Length; i++)
                {
                    if (Math.Abs(FullFactors[i] - warp.Factor) < 1e-9d)
                    {
                        Model.FullIndex = i;
                        break;
                    }
                }
            }

            double applied = runner.EffectiveWarpFactor;
            Model.AppliedFactor = applied;
            Model.AppliedFactorClamped = applied < warp.Factor - 1e-9d;
        }

        private void SetNotice(string text)
        {
            SetNotice(text, 3f);
        }

        private void SetNotice(string text, float seconds)
        {
            Model.Notice = text;
            noticeUntil = Time.unscaledTime + seconds;
        }
    }
}
