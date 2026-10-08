using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Spacecraft;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Universe
{
    /// <summary>
    /// Шаг корабля за кадр: полёт (орбитальная цепочка с событиями), посадка
    /// (стеснённая динамика поверхности), реакции на события. Вынесен из
    /// SimulationRunner, чтобы игра осталась тонкой MonoBehaviour-обёрткой,
    /// а шаг можно было прогонять без Unity (тестовый стенд, трейсы посадки).
    ///
    /// Владение состоянием: корабль, его режим (Flying/Landed/Destroyed),
    /// доминантное тело и время с Кахен-аккумулятором живут здесь. Игровой
    /// цикл (SimulationRunner) читает их и отвечает только за ввод, игрока,
    /// рендер-якорь и обновление обломков.
    ///
    /// Логика перенесена из SimulationRunner без изменения поведения; трейсы
    /// S1–S5 до и после выноса совпадают побайтно (шаг 0.4 ТЗ v2).
    /// </summary>
    public sealed class VesselStep
    {
        public readonly StarSystem SystemState;
        public readonly WarpController Warp;
        public readonly EventDrivenPropagator Propagator;
        public readonly LongWarpDriver Driver;
        public readonly ThrustSource MainThrust;
        public readonly IBreakupModel BreakupModel;
        public readonly DebrisPool Debris;
        public readonly PartDefinition[] Parts;
        public readonly double SpawnEpsilonMeters;
        public readonly double DebrisLifetimeSeconds;

        /// <summary>Корабль. Игровой цикл ставит его при спавне и больше не подменяет.</summary>
        public Ship Ship;

        /// <summary>Режим корабля: летит, стоит на поверхности или разрушен.</summary>
        public VesselRegime Regime;

        /// <summary>Тело, относительно которого сейчас живёт корабль (floating origin, SOI).</summary>
        public OrbitingBody DominantBody;

        /// <summary>Симулированное время шага (с).</summary>
        public double TimeSeconds { get; private set; }

        /// <summary>Сырой газ 0..1 от ввода; читается в шаге при активной тяге.</summary>
        public double RawThrottle;

        /// <summary>Наблюдаемость: вызывается перед обработкой события, состояние — момент удара.</summary>
        public Action<EventOccurrence> OccurrenceHandled;

        // Не readonly: KahanAccumulator — структура, мутирующие методы на
        // readonly-поле работали бы с копией.
        private KahanAccumulator time = new KahanAccumulator(0d);
        private static readonly SphericalTerrain surfaceTerrain = new SphericalTerrain();

        /// <summary>Верхняя граница подшага на поверхности (с), согласована с зерном control-tick.</summary>
        private const double SurfaceMaxStepSeconds = 0.5d;

        public VesselStep(
            StarSystem systemState,
            WarpController warp,
            EventDrivenPropagator propagator,
            LongWarpDriver driver,
            ThrustSource mainThrust,
            IBreakupModel breakupModel,
            DebrisPool debris,
            PartDefinition[] parts,
            double spawnEpsilonMeters,
            double debrisLifetimeSeconds)
        {
            SystemState = systemState ?? throw new ArgumentNullException(nameof(systemState));
            Warp = warp ?? throw new ArgumentNullException(nameof(warp));
            Propagator = propagator ?? throw new ArgumentNullException(nameof(propagator));
            Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            MainThrust = mainThrust;
            BreakupModel = breakupModel ?? throw new ArgumentNullException(nameof(breakupModel));
            Debris = debris ?? throw new ArgumentNullException(nameof(debris));
            Parts = parts;
            SpawnEpsilonMeters = spawnEpsilonMeters;
            DebrisLifetimeSeconds = debrisLifetimeSeconds;
            TimeSeconds = 0d;
            Regime = VesselRegime.Flying;
        }

        /// <summary>
        /// Сбросить Кахен-аккумулятор на текущее время: после внешних
        /// перемещений корабля (телепорт) накопленная компенсация не относится
        /// к новой траектории.
        /// </summary>
        public void ResetTimeAccumulator()
        {
            time.Reset(TimeSeconds);
        }

        /// <summary>
        /// Полёт за кадр: орбитальная цепочка чанками по границе control-tick,
        /// событие обрабатывается здесь же. playerInShip=false (игрок вне
        /// корабля) запрещает дальний варп и ограничивает физический ×4 —
        /// иначе игрока не догнать подшагами.
        /// </summary>
        public void StepFlying(float realDt, bool playerInShip)
        {
            WarpFrameDecision decision = EphemerisRuntime.ComputeFrame(
                SystemState, Warp, Ship.Position, TimeSeconds, realDt);
            double playerCap = playerInShip ? double.PositiveInfinity : 4d;
            double factor = Math.Min(decision.EffectiveFactor, playerCap);
            double frameEnd = TimeSeconds + (realDt * factor);

            if (decision.UseLongWarp && playerCap == double.PositiveInfinity)
            {
                // Дальний варп: баллистика целиком, тяга запрещена по построению.
                Warp.PrepareForLongWarp(TimeSeconds);
                Driver.CancelRequested = false;
                LongWarpResult result = Driver.AdvanceToTarget(Ship, TimeSeconds, frameEnd);
                TimeSeconds = result.ReachedTimeSeconds;
                time.Reset(TimeSeconds);
                return;
            }

            double throttle = Warp.EffectiveThrottle(RawThrottle, factor);
            bool thrustActive = MainThrust != null && throttle > 0d;
            while (TimeSeconds < frameEnd - 1e-12d)
            {
                double chunk = Warp.GetMaxChunkSeconds(TimeSeconds, OrbitIntegrator.DefaultMaxStepSize, thrustActive);
                chunk = Math.Min(chunk, frameEnd - TimeSeconds);
                if (chunk <= 0d)
                {
                    chunk = Math.Min(OrbitIntegrator.DefaultMinStepSize, frameEnd - TimeSeconds);
                }

                Warp.SampleControl(TimeSeconds, throttle);
                EventOccurrence? occurrence = Propagator.Propagate(Ship, TimeSeconds, chunk);
                if (occurrence.HasValue)
                {
                    TimeSeconds = occurrence.Value.TimeSeconds;
                    time.Reset(TimeSeconds);
                    HandleOccurrence(occurrence.Value);
                    if (Regime != VesselRegime.Flying)
                    {
                        return;
                    }
                }
                else
                {
                    time.Add(chunk);
                    TimeSeconds = time.Sum;
                }
            }
        }

        /// <summary>
        /// Посадка за кадр: стеснённая динамика поверхности чанками. Дальний
        /// варп запрещён, физический кап ×3 (как в атмосфере). Тяга выше
        /// прижатия отрывает корабль (TryLiftoff) — возврат в Flying здесь же.
        /// </summary>
        public void StepLanded(float realDt, bool playerInShip)
        {
            WarpFrameDecision decision = EphemerisRuntime.ComputeFrame(
                SystemState, Warp, Ship.Position, TimeSeconds, realDt);
            double playerCap = playerInShip ? double.PositiveInfinity : 4d;
            double factor = Math.Min(Math.Min(decision.EffectiveFactor, WarpController.AtmosphereMaxWarpFactor), playerCap);
            double frameEnd = TimeSeconds + (realDt * factor);
            double throttle = Warp.EffectiveThrottle(RawThrottle, factor);
            bool thrustActive = MainThrust != null && throttle > 0d;

            while (TimeSeconds < frameEnd - 1e-12d)
            {
                if (thrustActive)
                {
                    Vector3d gravity = SystemState.EvaluateShipAcceleration(Ship.Position, TimeSeconds);
                    DynamicsContribution thrust = MainThrust.Evaluate(Ship.Position, Ship.Velocity, Ship.Mass, TimeSeconds);
                    if (EventReactions.TryLiftoff(Ship, DominantBody, thrust.Force / Ship.Mass, gravity, TimeSeconds, out _)
                        == VesselRegime.Flying)
                    {
                        Regime = VesselRegime.Flying;
                        Warp.ResetTicks(TimeSeconds);
                        time.Reset(TimeSeconds);
                        return;
                    }
                }

                double chunk = Warp.GetMaxChunkSeconds(TimeSeconds, SurfaceMaxStepSeconds, thrustActive);
                chunk = Math.Min(chunk, frameEnd - TimeSeconds);
                if (chunk <= 0d)
                {
                    chunk = Math.Min(SurfaceMaxStepSeconds, frameEnd - TimeSeconds);
                }

                Warp.SampleControl(TimeSeconds, throttle);
                SurfaceMotionResult result = SurfaceMotion.Step(
                    Ship.Position, Ship.Velocity, DominantBody, surfaceTerrain,
                    (position, t) => SystemState.EvaluateShipAcceleration(position, t),
                    TimeSeconds, chunk);
                Ship.Position = result.Position;
                Ship.Velocity = result.Velocity;
                time.Add(chunk);
                TimeSeconds = time.Sum;
                if (result.Regime == VesselRegime.Flying)
                {
                    Regime = VesselRegime.Flying;
                    Warp.ResetTicks(TimeSeconds);
                    return;
                }
            }
        }

        private void HandleOccurrence(EventOccurrence occurrence)
        {
            // Хук наблюдаемости — до реакций: стенд видит состояние удара,
            // а не результат проекции/разрушения.
            OccurrenceHandled?.Invoke(occurrence);
            switch (occurrence.Kind)
            {
                case EventKind.Touchdown:
                    TouchdownOutcome outcome = EventReactions.ApplyTouchdown(
                        Ship, occurrence, occurrence.Body, BreakupModel, SpawnEpsilonMeters,
                        NormalizeAssembly(Ship.Mass));
                    if (outcome.IsLanding)
                    {
                        Regime = VesselRegime.Landed;
                        DominantBody = occurrence.Body;
                        Warp.ResetTicks(occurrence.TimeSeconds);
                        LandedState landed = outcome.Landed;
                        Debug.Log("Посадка на \"" + occurrence.Body.Name + "\" ("
                            + landed.LatitudeDegrees.ToString("F2") + "°, "
                            + landed.LongitudeDegrees.ToString("F2") + "°), v_t = "
                            + landed.TangentialSpeed.ToString("F1") + " м/с");
                    }
                    else
                    {
                        ApplyBreakup(occurrence, outcome.Specs);
                    }

                    break;
                case EventKind.PropellantDepleted:
                    EventReactions.ApplyDepletion(Ship, occurrence);
                    break;
                case EventKind.EphemerisEnd:
                    Debug.LogWarning("Конец испечённого мира — варп ограничен кеплеровыми рельсами.");
                    break;
            }
        }

        /// <summary>
        /// Сборка деталей, нормированная к фактической массе корабля (массы
        /// деталей задают ПРОПОРЦИИ: топливо выгорает, суммарная масса дрейфует).
        /// null, если деталей не задано — whole-ship путь breakup-модели.
        /// </summary>
        private List<Part> NormalizeAssembly(double shipMass)
        {
            if (Parts == null || Parts.Length == 0)
            {
                return null;
            }

            double definedMass = 0d;
            for (int i = 0; i < Parts.Length; i++)
            {
                definedMass += Parts[i].MassKg;
            }

            if (definedMass <= 0d)
            {
                return null;
            }

            double scale = shipMass / definedMass;
            var assembly = new List<Part>(Parts.Length);
            for (int i = 0; i < Parts.Length; i++)
            {
                assembly.Add(new Part(
                    Parts[i].MassKg * scale,
                    new Vector3d(Parts[i].Offset.x, Parts[i].Offset.y, Parts[i].Offset.z),
                    Parts[i].JointStrengthNewtons));
            }

            return assembly;
        }

        /// <summary>
        /// Жёсткий удар: отломанные детали → обломки, выжившая сборка остаётся
        /// кораблём (масса = разность, посадка продолжится SurfaceMotion).
        /// Пустой список спеков = стыки держат → посадка с повреждениями.
        /// Ни одной целой детали → Destroyed.
        /// </summary>
        private void ApplyBreakup(EventOccurrence occurrence, System.Collections.Generic.IReadOnlyList<PartSeparationSpec> specs)
        {
            double brokenMass = 0d;
            for (int i = 0; i < specs.Count; i++)
            {
                Debris.Spawn(specs[i].Position, specs[i].Velocity, specs[i].Mass,
                    occurrence.TimeSeconds, DebrisLifetimeSeconds);
                brokenMass += specs[i].Mass;
            }

            double survivorMass = Ship.Mass - brokenMass;
            DominantBody = occurrence.Body;
            if (survivorMass > 1e-9d)
            {
                Ship.Mass = survivorMass;
                Regime = VesselRegime.Landed;
                Warp.ResetTicks(occurrence.TimeSeconds);
                Debug.Log("Удар о \"" + occurrence.Body.Name + "\": потеряно "
                    + brokenMass.ToString("F0") + " кг, осталось "
                    + specs.Count + " отделившихся деталей; сборка села.");
            }
            else
            {
                Regime = VesselRegime.Destroyed;
                Warp.SetWarpFactor(WarpController.MinWarpFactor);
                Debug.LogWarning("Удар о \"" + occurrence.Body.Name + "\": сборка разрушена полностью ("
                    + specs.Count + " обломков).");
            }
        }
    }
}
