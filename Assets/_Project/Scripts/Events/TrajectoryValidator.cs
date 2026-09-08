using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Итог валидации импульсной дуги полным n-body. Valid ⟺ дошёл до tArr
    /// без убивающих событий И промах в допуске И минимум клиренса не отрицан
    /// событием. События собираются все по пути (вход/выход/SOI — мимоходом,
    /// touchdown/depletion — останов с invalid).
    /// </summary>
    public readonly struct ValidationResult
    {
        public readonly bool Valid;
        public readonly bool ReachedTarget;
        public readonly double ArrivalPositionErrorMeters;
        public readonly double ArrivalVelocityErrorMetersPerSecond;
        public readonly double MinClearanceMeters;
        public readonly OrbitingBody MinClearanceBody;
        public readonly IReadOnlyList<EventOccurrence> Events;
        public readonly double EndTimeSeconds;

        public ValidationResult(
            bool valid, bool reachedTarget,
            double arrivalPositionErrorMeters, double arrivalVelocityErrorMetersPerSecond,
            double minClearanceMeters, OrbitingBody minClearanceBody,
            IReadOnlyList<EventOccurrence> events, double endTimeSeconds)
        {
            Valid = valid;
            ReachedTarget = reachedTarget;
            ArrivalPositionErrorMeters = arrivalPositionErrorMeters;
            ArrivalVelocityErrorMetersPerSecond = arrivalVelocityErrorMetersPerSecond;
            MinClearanceMeters = minClearanceMeters;
            MinClearanceBody = minClearanceBody;
            Events = events;
            EndTimeSeconds = endTimeSeconds;
        }
    }

    /// <summary>
    /// Итог валидации finite-burn: сухой прогон оценщика на клоне против
    /// НЕЗАВИСИМОГО импульсного идеала (не против живого прогона тем же
    /// оценщиком — то доказывало бы лишь детерминизм). CompletedBurn=false
    /// (событие оборвало дугу) — всегда invalid, даже при малом промахе.
    /// </summary>
    public readonly struct BurnValidationResult
    {
        public readonly bool Valid;
        public readonly bool CompletedBurn;
        public readonly double PositionErrorMeters;
        public readonly double VelocityErrorMetersPerSecond;
        public readonly double FuelUsedKg;
        public readonly double FuelErrorKg;
        public readonly EventOccurrence? StoppingEvent;

        public BurnValidationResult(
            bool valid, bool completedBurn,
            double positionErrorMeters, double velocityErrorMetersPerSecond,
            double fuelUsedKg, double fuelErrorKg, EventOccurrence? stoppingEvent)
        {
            Valid = valid;
            CompletedBurn = completedBurn;
            PositionErrorMeters = positionErrorMeters;
            VelocityErrorMetersPerSecond = velocityErrorMetersPerSecond;
            FuelUsedKg = fuelUsedKg;
            FuelErrorKg = fuelErrorKg;
            StoppingEvent = stoppingEvent;
        }
    }

    /// <summary>
    /// P1c: финальная защита перед исполнением манёвра. Porkchop — грубый
    /// фильтр (16 сэмплов, концы исключены); принятый кандидат проверяется
    /// целиком тем же движком, что полетит: полный n-body прогон на шаге
    /// интегратора + штатный детект событий. Закрывает пробелы сэмплинга
    /// ДЛЯ БЕЗОПАСНОСТИ ИСПОЛНЕНИЯ (ложноотрицательные — отвергнутые валидные
    /// окна — валидатор не видит по построению; совершенствование фильтра —
    /// отдельная задача).
    ///
    /// Дисциплина «без общего бага»: Ламберт НЕ перерешивается внутри — v1
    /// приходит параметром от планировщика (иначе проверяли бы согласие
    /// планировщика с собой, а общий баг обеих сторон спрятался бы).
    /// Реальный корабль не мутирует никогда: все прогоны — на клонах
    /// (planning-time new допустим, это не тиковый путь).
    /// </summary>
    public static class TrajectoryValidator
    {
        public static ValidationResult ValidateImpulsive(
            StarSystem system,
            SpacecraftPhysics flightPhysics,
            EventDrivenPropagator flightPropagator,
            SpacecraftIntegrationState departState,
            double departTimeSeconds,
            Vector3d departureVelocityWorld,
            OrbitingBody targetBody,
            double arriveTimeSeconds,
            double arrivalToleranceMeters)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));
            if (flightPhysics == null || flightPropagator == null)
                throw new ArgumentNullException("Боевая физика и драйвер обязаны быть заданы.");
            if (targetBody == null)
                throw new ArgumentNullException(nameof(targetBody));
            if (arriveTimeSeconds <= departTimeSeconds)
                throw new ArgumentOutOfRangeException(nameof(arriveTimeSeconds), "Прилёт обязан быть позже вылета.");
            if (arrivalToleranceMeters < 0d)
                throw new ArgumentOutOfRangeException(nameof(arrivalToleranceMeters), "Допуск неотрицателен.");

            var clone = new Ship(departState.Position, departureVelocityWorld, departState.Mass);
            var events = new List<EventOccurrence>();
            double minClearance = double.PositiveInfinity;
            OrbitingBody minBody = null;
            double time = departTimeSeconds;
            bool killed = false;

            while (time < arriveTimeSeconds - 1e-12d)
            {
                var track = new List<DenseSegment>();
                EventOccurrence? hit = flightPropagator.PropagateWithSegments(clone, time, arriveTimeSeconds - time, track);
                ScanClearance(system, track, ref minClearance, ref minBody);
                if (!hit.HasValue)
                {
                    time = arriveTimeSeconds;
                    break;
                }

                events.Add(hit.Value);
                time = hit.Value.TimeSeconds;
                if (hit.Value.Kind == EventKind.Touchdown || hit.Value.Kind == EventKind.PropellantDepleted)
                {
                    killed = true;
                    break;
                }
            }

            bool reached = !killed && time >= arriveTimeSeconds - 1e-9d;
            targetBody.EvaluateWorldState(arriveTimeSeconds, out Vector3d targetPosition, out Vector3d targetVelocity);
            double arrivalError = reached ? (clone.Position - targetPosition).Magnitude : double.PositiveInfinity;
            double arrivalVelError = reached ? (clone.Velocity - targetVelocity).Magnitude : double.PositiveInfinity;
            bool valid = reached && !killed && arrivalError <= arrivalToleranceMeters;
            return new ValidationResult(valid, reached, arrivalError, arrivalVelError, minClearance, minBody, events, time);
        }

        public static BurnValidationResult ValidateFiniteBurn(
            ManeuverEvaluator evaluator,
            SpacecraftIntegrationState startState,
            double startTimeSeconds,
            double igniteTimeSeconds,
            double cutTimeSeconds,
            BurnTolerancePreset preset,
            double frameSimSeconds,
            Vector3d expectedEndPosition,
            Vector3d expectedEndVelocity,
            double positionToleranceMeters,
            double velocityToleranceMetersPerSecond,
            double plannedFuelKg,
            double fuelToleranceKg)
        {
            if (evaluator == null)
                throw new ArgumentNullException(nameof(evaluator));

            var clone = new Ship(startState.Position, startState.Velocity, startState.Mass);
            ManeuverResult run = evaluator.CoastThenBurn(
                clone, startTimeSeconds, igniteTimeSeconds, cutTimeSeconds, preset, frameSimSeconds);
            double fuelUsed = startState.Mass - run.EndState.Mass;
            double fuelError = fuelUsed - plannedFuelKg;
            double posError = (run.EndState.Position - expectedEndPosition).Magnitude;
            double velError = (run.EndState.Velocity - expectedEndVelocity).Magnitude;
            bool valid = run.CompletedBurn && !run.StoppingEvent.HasValue
                && posError <= positionToleranceMeters
                && velError <= velocityToleranceMetersPerSecond
                && Math.Abs(fuelError) <= fuelToleranceKg;
            return new BurnValidationResult(valid, run.CompletedBurn, posError, velError, fuelUsed, fuelError, run.StoppingEvent);
        }

        private static void ScanClearance(StarSystem system, List<DenseSegment> track, ref double minClearance, ref OrbitingBody minBody)
        {
            for (int s = 0; s < track.Count; s++)
            {
                DenseSegment seg = track[s];
                ScanPoint(system, seg.Y0.Position, seg.T0, ref minClearance, ref minBody);
                if (s == track.Count - 1)
                {
                    ScanPoint(system, seg.Y1.Position, seg.T1, ref minClearance, ref minBody);
                }
            }
        }

        private static void ScanPoint(StarSystem system, Vector3d position, double time, ref double minClearance, ref OrbitingBody minBody)
        {
            IReadOnlyList<OrbitingBody> bodies = system.AllBodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                OrbitingBody body = bodies[i];
                body.EvaluateWorldState(time, out Vector3d bodyPosition, out _);
                double clearance = (position - bodyPosition).Magnitude - body.Radius;
                if (clearance < minClearance)
                {
                    minClearance = clearance;
                    minBody = body;
                }
            }
        }
    }
}
