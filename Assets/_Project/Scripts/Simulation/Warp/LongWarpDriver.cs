using System;
using System.Diagnostics;
using Galilego.Core;
using Galilego.Spacecraft;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Result of a long jump: reached the target or stopped on an event.
    /// For smooth rendering — AdvanceToTargetWithSegments: a chain of dense output
    /// for the traversed path, sampled with no Step at all (see DenseSegment).
    /// </summary>
    public readonly struct LongWarpResult
    {
        public readonly double ReachedTimeSeconds;
        public readonly EventOccurrence? StoppingEvent;
        public readonly bool ReachedTarget;

        /// <summary>
        /// true — кадровый бюджет кончился, недоигранное переносится на следующий кадр.
        /// Часы уже стоят на честно прожитом времени, поэтому показ не врёт.
        /// </summary>
        public readonly bool BudgetExhausted;

        public LongWarpResult(double reachedTimeSeconds, EventOccurrence? stoppingEvent, bool reachedTarget,
            bool budgetExhausted = false)
        {
            ReachedTimeSeconds = reachedTimeSeconds;
            StoppingEvent = stoppingEvent;
            ReachedTarget = reachedTarget;
            BudgetExhausted = budgetExhausted;
        }
    }

    /// <summary>
    /// Long warp — not a new engine but another way of calling the existing
    /// EventDrivenPropagator straight to a large target mark. On smooth stretches
    /// the adaptive step grows itself, and the layer stops advancing at the first
    /// event. The entry condition is formal: no IDynamicsSource other than gravity
    /// contributes non-zero right now (checked by actual contribution, not by
    /// player mode). Cancelling on thrust touch is silent, BEFORE the next RHS
    /// call, so no call ever sees a warp-scale step together with an active
    /// non-gravity source.
    ///
    /// Кадровый бюджет: цель режется на срезы (не больше MaxChunkSeconds — столько же,
    /// сколько максимальный чанк пропагатора), между срезами проверяется Stopwatch.
    /// Один срез вдали от тел — один чанк 600 с (накладных расходов нет), у планеты
    /// детектор дробит его на подшаги, и срез автоматически мельчает, чтобы бюджет
    /// соблюдался. Без бюджета ×1 000 000 в LEO — это ~5300 подшагов на кадр, то есть
    /// рывок; с бюджетом темп ниже, но честный и без потери точности событий.
    /// </summary>
    public sealed class LongWarpDriver
    {
        /// <summary>Максимальный срез цели — равен максимальному чанку пропагатора.</summary>
        public const double MaxSliceSeconds = OrbitIntegrator.DefaultMaxStepSize;

        /// <summary>Минимальный срез: мельче не имеет смысла, зерно событий уже 3 с.</summary>
        public const double MinSliceSeconds = 1d;

        /// <summary>
        /// Бюджет одного кадра на активный корабль, мс. 6 мс — потолок, после которого
        /// кадр начинает проседать; недоигранное переносится.
        /// </summary>
        public double FrameBudgetMilliseconds = 6d;

        private readonly SpacecraftPhysics physics;
        private readonly EventDrivenPropagator propagator;

        /// <summary>
        /// Checked on every RHS through a wrapper: if the cancel flag is set
        /// (the player touched thrust), further computation is forbidden.
        /// The check is cheap.
        /// </summary>
        public bool CancelRequested;

        public LongWarpDriver(SpacecraftPhysics physics, EventDrivenPropagator propagator)
        {
            this.physics = physics ?? throw new ArgumentNullException(nameof(physics));
            this.propagator = propagator ?? throw new ArgumentNullException(nameof(propagator));
        }

        /// <summary>
        /// Formal entry check: all sources except GravitySource and
        /// CachedGravitySource contribute zero at the current point.
        /// </summary>
        public bool CanEnterLongWarp(SpacecraftIntegrationState state, double timeSeconds)
        {
            for (int i = 0; i < physics.Sources.Count; i++)
            {
                IDynamicsSource source = physics.Sources[i];
                if (source is GravitySource || source is CachedGravitySource)
                {
                    continue;
                }

                DynamicsContribution c = source.Evaluate(state.Position, state.Velocity, state.Mass, timeSeconds);
                if (c.Force.SqrMagnitude > 0d || c.SpecificAcceleration.SqrMagnitude > 0d || c.MassFlow != 0d)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Runs to targetTimeSeconds or to the first event. Silent reset: if
        /// CancelRequested is set before/during the computation, we stop at the
        /// current point without throwing; the ship stays valid at the boundary
        /// of the already-accepted chunks.
        /// </summary>
        public LongWarpResult AdvanceToTarget(Ship ship, double currentTimeSeconds, double targetTimeSeconds)
        {
            return AdvanceToTargetWithSegments(ship, currentTimeSeconds, targetTimeSeconds, null);
        }

        /// <summary>
        /// The same jump plus a chain of dense segments for the traversed path
        /// (only up to the event/target) for render sampling without Step.
        /// Останавливается на событии, на цели или по кадровому бюджету.
        /// </summary>
        public LongWarpResult AdvanceToTargetWithSegments(Ship ship, double currentTimeSeconds, double targetTimeSeconds,
            System.Collections.Generic.List<DenseSegment> render)
        {
            if (ship == null)
            {
                throw new ArgumentNullException(nameof(ship));
            }

            if (targetTimeSeconds <= currentTimeSeconds)
            {
                return new LongWarpResult(currentTimeSeconds, null, true);
            }

            bool budgeted = FrameBudgetMilliseconds > 0d;
            Stopwatch watch = budgeted ? Stopwatch.StartNew() : null;
            double sliceSeconds = MaxSliceSeconds;
            double time = currentTimeSeconds;

            while (time < targetTimeSeconds)
            {
                if (CancelRequested)
                {
                    return new LongWarpResult(time, null, false);
                }

                double remaining = targetTimeSeconds - time;
                double slice = remaining < sliceSeconds ? remaining : sliceSeconds;

                double sliceStartedMs = budgeted ? watch.Elapsed.TotalMilliseconds : 0d;
                EventOccurrence? hit = propagator.PropagateWithSegments(ship, time, slice, render);
                if (hit.HasValue)
                {
                    return new LongWarpResult(hit.Value.TimeSeconds, hit, false);
                }

                time += slice;

                if (!budgeted)
                {
                    continue;
                }

                double spentMs = watch.Elapsed.TotalMilliseconds - sliceStartedMs;
                if (watch.Elapsed.TotalMilliseconds >= FrameBudgetMilliseconds)
                {
                    return new LongWarpResult(time, null, false, true);
                }

                // Срез дороже половины бюджета — мельчим, чтобы следующий кадр не получил
                // один длинный срез в самом дорогом месте (низкая орбита, тесное сближение).
                if (spentMs > FrameBudgetMilliseconds * 0.5d)
                {
                    sliceSeconds = Math.Max(MinSliceSeconds, sliceSeconds * 0.25d);
                }
                else if (spentMs < FrameBudgetMilliseconds * 0.125d)
                {
                    sliceSeconds = Math.Min(MaxSliceSeconds, sliceSeconds * 2d);
                }
            }

            return new LongWarpResult(targetTimeSeconds, null, true);
        }

        /// <summary>Silent warp cancel (thrust touch): the next RHS will see it.</summary>
        public void RequestSilentCancel()
        {
            CancelRequested = true;
        }

        public void ClearCancel()
        {
            CancelRequested = false;
        }
    }
}
