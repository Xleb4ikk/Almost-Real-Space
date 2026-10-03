using System;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Spacecraft;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Допуски burn-режима — ПАРАМЕТР вызова, а не встроенное предположение.
    /// Химический прожиг (тяга в единицы g) упирается в ошибку массы и
    /// направления — massTol жёсткий; низкотяговый (ионный, тяга меньше
    /// гравитации) — в ошибку позиции/скорости на длинной дуге. Неверный выбор
    /// здесь — решение не той проблемы, поэтому пресет передаётся явно.
    /// </summary>
    public readonly struct BurnTolerancePreset
    {
        public readonly double PositionAbsoluteTolerance;
        public readonly double VelocityAbsoluteTolerance;
        public readonly double MassAbsoluteTolerance;
        public readonly double RelativeTolerance;

        public BurnTolerancePreset(double positionAbsoluteTolerance, double velocityAbsoluteTolerance, double massAbsoluteTolerance, double relativeTolerance)
        {
            PositionAbsoluteTolerance = positionAbsoluteTolerance;
            VelocityAbsoluteTolerance = velocityAbsoluteTolerance;
            MassAbsoluteTolerance = massAbsoluteTolerance;
            RelativeTolerance = relativeTolerance;
        }

        /// <summary>Химический двигатель: доминирует масса/направление.</summary>
        public static BurnTolerancePreset Chemical => new BurnTolerancePreset(1e-3d, 1e-6d, 1e-7d, 1e-11d);

        /// <summary>Низкая тяга: доминирует позиция/скорость на длинной дуге.</summary>
        public static BurnTolerancePreset LowThrust => new BurnTolerancePreset(1e-2d, 1e-5d, 1e-6d, 1e-10d);
    }

    /// <summary>
    /// Итог связки coast+burn: где остановились, на чём (событие или отсечка),
    /// доработала ли тяга до плановой отсечки.
    /// </summary>
    public readonly struct ManeuverResult
    {
        public readonly double EndTimeSeconds;
        public readonly SpacecraftIntegrationState EndState;
        public readonly EventOccurrence? StoppingEvent;
        public readonly bool CompletedBurn;

        public ManeuverResult(double endTimeSeconds, SpacecraftIntegrationState endState, EventOccurrence? stoppingEvent, bool completedBurn)
        {
            EndTimeSeconds = endTimeSeconds;
            EndState = endState;
            StoppingEvent = stoppingEvent;
            CompletedBurn = completedBurn;
        }
    }

    /// <summary>
    /// Связка coast→burn поверх готовых драйверов, не новый движок: добор до
    /// зажигания — LongWarpDriver (баллистика, throttle там ноль и вход
    /// разрешён формально), сам прожиг — EventDrivenPropagator чанками,
    /// обрезанными WarpController по control-tick (тяга активна — граница тика
    /// никогда не пересекается, доказано T2). Пресет допусков применяется на
    /// время прожига и восстанавливается в finally: архитектура не знает,
    /// «химия» там или ионник.
    ///
    /// Плановая отсечка — расписанием ThrottleAt (непрерывно в ноль), сброс
    /// ступени — StageSeparation (разрыв массы/импульс отделения, дискретно,
    /// внеплановая выработка — PropellantDepletionDetector в burn-пропагаторе.
    /// </summary>
    public sealed class ManeuverEvaluator
    {
        private readonly SpacecraftPhysics physics;
        private readonly EventDrivenPropagator coastPropagator;
        private readonly LongWarpDriver coastDriver;
        private readonly EventDrivenPropagator burnPropagator;
        private readonly WarpController warp;

        public ManeuverEvaluator(
            SpacecraftPhysics physics,
            EventDrivenPropagator coastPropagator,
            LongWarpDriver coastDriver,
            EventDrivenPropagator burnPropagator,
            WarpController warp)
        {
            this.physics = physics ?? throw new ArgumentNullException(nameof(physics));
            this.coastPropagator = coastPropagator ?? throw new ArgumentNullException(nameof(coastPropagator));
            this.coastDriver = coastDriver ?? throw new ArgumentNullException(nameof(coastDriver));
            this.burnPropagator = burnPropagator ?? throw new ArgumentNullException(nameof(burnPropagator));
            this.warp = warp ?? throw new ArgumentNullException(nameof(warp));
        }

        public ManeuverResult CoastThenBurn(
            Ship ship,
            double startTimeSeconds,
            double igniteTimeSeconds,
            double cutTimeSeconds,
            BurnTolerancePreset preset,
            double frameSimSeconds,
            Func<double, double> throttleAt = null)
        {
            if (ship == null)
            {
                throw new ArgumentNullException(nameof(ship));
            }

            if (cutTimeSeconds <= igniteTimeSeconds || igniteTimeSeconds < startTimeSeconds)
            {
                throw new ArgumentOutOfRangeException("Окно прожига обязано лежать впереди старта.");
            }

            // frameSimSeconds ≤ 0 даёт frameEnd = t: внутренний цикл не
            // выполняется, timeAcc не растёт — вечный цикл (аудит S1/2.3). Громко.
            if (!(frameSimSeconds > 0d) || !double.IsFinite(frameSimSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(frameSimSeconds),
                    "frameSimSeconds обязан быть конечным и положительным, получено " + frameSimSeconds + ".");
            }

            // Газ: расписание 0..1 по времени. null = заглушка до системы
            // ступеней/ввода — полный газ на весь прожиг (прежнее поведение,
            // раньше 1.0 был захардкожен молча).
            Func<double, double> throttle = throttleAt ?? (_ => 1d);

            LongWarpResult coast = coastDriver.AdvanceToTarget(ship, startTimeSeconds, igniteTimeSeconds);
            if (coast.StoppingEvent.HasValue)
            {
                return new ManeuverResult(coast.ReachedTimeSeconds, Capture(ship), coast.StoppingEvent, false);
            }

            double savedPosTol = physics.PositionAbsoluteTolerance;
            double savedVelTol = physics.VelocityAbsoluteTolerance;
            double savedMassTol = physics.MassAbsoluteTolerance;
            double savedRtol = physics.RelativeTolerance;
            physics.PositionAbsoluteTolerance = preset.PositionAbsoluteTolerance;
            physics.VelocityAbsoluteTolerance = preset.VelocityAbsoluteTolerance;
            physics.MassAbsoluteTolerance = preset.MassAbsoluteTolerance;
            physics.RelativeTolerance = preset.RelativeTolerance;
            try
            {
                warp.ResetTicks(igniteTimeSeconds);
                var timeAcc = new KahanAccumulator(igniteTimeSeconds);
                while (timeAcc.Sum < cutTimeSeconds - 1e-12d)
                {
                    double t = timeAcc.Sum;
                    double frameEnd = Math.Min(t + frameSimSeconds, cutTimeSeconds);
                    while (t < frameEnd - 1e-12d)
                    {
                        double maxChunk = warp.GetMaxChunkSeconds(t, burnPropagator.MaxChunkSeconds, true);
                        double chunk = Math.Min(maxChunk, frameEnd - t);
                        if (chunk <= 0d)
                        {
                            // Ноль от warp-гейта — легитимный отказ продвижения
                            // (запрет), а не «подшагнём сами»: громко (аудит S1/F13).
                            throw new InvalidOperationException(
                                "Warp-контроллер запретил продвижение (maxChunk=" + maxChunk +
                                ") на t=" + t.ToString("R") + " — продлевать окно симуляции нельзя без диагностики.");
                        }

                        double throttleNow = throttle(t);
                        // NaN-throttle молча проходит кламп (Max/Min с NaN = NaN)
                        // и уходит в пропагатор (аудит S1/2.3).
                        if (!double.IsFinite(throttleNow))
                        {
                            throw new InvalidOperationException(
                                "Расписание газа вернуло не-конечное значение на t=" + t.ToString("R") + ".");
                        }

                        warp.SampleControl(t, Math.Max(0d, Math.Min(1d, throttleNow)));
                        EventOccurrence? hit = burnPropagator.Propagate(ship, t, chunk);
                        if (hit.HasValue)
                        {
                            return new ManeuverResult(hit.Value.TimeSeconds, Capture(ship), hit, false);
                        }

                        timeAcc.Add(chunk);
                        t = timeAcc.Sum;
                    }
                }

                return new ManeuverResult(cutTimeSeconds, Capture(ship), null, true);
            }
            finally
            {
                physics.PositionAbsoluteTolerance = savedPosTol;
                physics.VelocityAbsoluteTolerance = savedVelTol;
                physics.MassAbsoluteTolerance = savedMassTol;
                physics.RelativeTolerance = savedRtol;
            }
        }

        /// <summary>
        /// Сброс ступени с сохранением импульса. Старый ApplyStaging
        /// (абсолютный кик separationDv кораблю, второго тела нет) удалён:
        /// вызывающих было ноль, семантика была неверна. Здесь внутренний
        /// импульс разводит ДВА тела: v1 = v0 + (m2/M)·Δv_rel остатку,
        /// v2 = v0 − (m1/M)·Δv_rel отделяемой ступени; m1·v1 + m2·v2 = M·v0
        /// тождественно по построению коэффициентов (проверено T33a).
        /// Направление Δv_rel — готовым вектором (вариант А: вызывающий берёт
        /// из AttitudeState, Events ориентацией не владеет — та же развязка,
        /// что ControlInputSnapshot). Ступень спавнится в DebrisPool тем же
        /// методом, что обломки (второго пути создания тел нет); живёт дольше
        /// дефолтного мусора (отдельный физический объект, не ошмёток взрыва).
        /// Громкое исключение при уходе массы ниже минимума (как TimedEvent).
        /// Драйвер пропагатора не трогаем: спавн делает вызывающий слой после
        /// возврата Timed-события, у драйвера пула нет и не должно быть.
        /// </summary>
        public static int StageSeparation(
            Ship ship,
            double stageMassKg,
            Vector3d relativeVelocity,
            DebrisPool pool,
            double timeSeconds,
            double minimumMassKg,
            double lifetimeSeconds = 1e7d)
        {
            if (ship == null)
            {
                throw new ArgumentNullException(nameof(ship));
            }

            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            if (stageMassKg <= 0d || stageMassKg >= ship.Mass)
            {
                throw new ArgumentOutOfRangeException(nameof(stageMassKg), "Масса ступени обязана быть в (0, M).");
            }

            double totalMass = ship.Mass;
            double remainingMass = totalMass - stageMassKg;
            if (remainingMass < minimumMassKg)
            {
                throw new InvalidOperationException(
                    "Staging: масса после сброса " + remainingMass.ToString("F3") +
                    " кг ниже минимума " + minimumMassKg.ToString("F3") + " кг.");
            }

            Vector3d initialVelocity = ship.Velocity;
            ship.Mass = remainingMass;
            ship.Velocity = initialVelocity + (relativeVelocity * (stageMassKg / totalMass));
            Vector3d stageVelocity = initialVelocity - (relativeVelocity * (remainingMass / totalMass));
            return pool.Spawn(ship.Position, stageVelocity, stageMassKg, timeSeconds, lifetimeSeconds);
        }

        private static SpacecraftIntegrationState Capture(Ship ship)
        {
            return new SpacecraftIntegrationState
            {
                Position = ship.Position,
                Velocity = ship.Velocity,
                Mass = ship.Mass
            };
        }
    }
}
