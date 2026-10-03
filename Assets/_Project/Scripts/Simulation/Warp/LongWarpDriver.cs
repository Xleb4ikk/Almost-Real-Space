using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Результат дальнего прыжка: дошли до цели или оборвались на событии.
    /// Для плавного рендера — AdvanceToTargetWithSegments: цепочка dense output
    /// пройденного пути, сэмплируется без единого Step (см. DenseSegment).
    /// </summary>
    public readonly struct LongWarpResult
    {
        public readonly double ReachedTimeSeconds;
        public readonly EventOccurrence? StoppingEvent;
        public readonly bool ReachedTarget;

        public LongWarpResult(double reachedTimeSeconds, EventOccurrence? stoppingEvent, bool reachedTarget)
        {
            ReachedTimeSeconds = reachedTimeSeconds;
            StoppingEvent = stoppingEvent;
            ReachedTarget = reachedTarget;
        }
    }

    /// <summary>
    /// Дальний варп — не новый движок, а другой режим вызова готового
    /// EventDrivenPropagator сразу с большой целевой отметкой. На гладком
    /// участке адаптивный шаг сам растёт (тесты B/D из P0), слой P1a сам
    /// обрывает продвижение на первом событии. Условие входа формальное: ни
    /// один IDynamicsSource, кроме гравитации, не даёт ненулевого вклада
    /// прямо сейчас (проверяется по факту вклада, не по режиму игрока).
    /// Сброс при касании тяги — тихий, ДО следующего RHS-вызова: ни один вызов
    /// не видит одновременно шаг варп-масштаба и активный негравитационный
    /// источник.
    /// </summary>
    public sealed class LongWarpDriver
    {
        private readonly SpacecraftPhysics physics;
        private readonly EventDrivenPropagator propagator;

        /// <summary>
        /// Вызывается каждый RHS через обёртку: если взведён флаг отмены
        /// (игрок тронул тягу), дальнейший расчёт запрещён. Проверка дешёвая.
        /// </summary>
        public bool CancelRequested;

        public LongWarpDriver(SpacecraftPhysics physics, EventDrivenPropagator propagator)
        {
            this.physics = physics ?? throw new ArgumentNullException(nameof(physics));
            this.propagator = propagator ?? throw new ArgumentNullException(nameof(propagator));
        }

        /// <summary>
        /// Формальная проверка входа: все источники, кроме GravitySource и
        /// CachedGravitySource, дают нулевой вклад в текущей точке.
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
        /// Прогоняет до targetTimeSeconds или до первого события. Тихий сброс:
        /// если CancelRequested взведён до/во время расчёта — останавливаемся
        /// на текущей точке без исключения, корабль остаётся в валидном
        /// состоянии на границе уже принятых чанков.
        /// </summary>
        public LongWarpResult AdvanceToTarget(Ship ship, double currentTimeSeconds, double targetTimeSeconds)
        {
            return AdvanceToTargetWithSegments(ship, currentTimeSeconds, targetTimeSeconds, null);
        }

        /// <summary>
        /// Тот же прыжок, плюс цепочка dense-сегментов пройденного пути
        /// (только до события/цели) для сэмплирования рендера без Step.
        /// </summary>
        public LongWarpResult AdvanceToTargetWithSegments(Ship ship, double currentTimeSeconds, double targetTimeSeconds, System.Collections.Generic.List<DenseSegment> render)
        {
            if (ship == null)
            {
                throw new ArgumentNullException(nameof(ship));
            }

            if (targetTimeSeconds <= currentTimeSeconds)
            {
                return new LongWarpResult(currentTimeSeconds, null, true);
            }

            double time = currentTimeSeconds;
            while (time < targetTimeSeconds)
            {
                if (CancelRequested)
                {
                    return new LongWarpResult(time, null, false);
                }

                double remaining = targetTimeSeconds - time;
                EventOccurrence? hit = propagator.PropagateWithSegments(ship, time, remaining, render);
                if (hit.HasValue)
                {
                    return new LongWarpResult(hit.Value.TimeSeconds, hit, false);
                }

                time = targetTimeSeconds;
            }

            return new LongWarpResult(targetTimeSeconds, null, true);
        }

        /// <summary>Тихий сброс варпа (касание тяги): следующий RHS его увидит.</summary>
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
