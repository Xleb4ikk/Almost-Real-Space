using System;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Universe
{
    // ЧЕК-ЛИСТ ИНТЕГРАЦИИ В UNITY (все файлы из 1game копируются целиком,
    // исключения ниже; тестовый стенд P1bTests их НЕ включает):
    //   + SimulationRunner.cs (этот файл), BodyView.cs, ShipController.cs,
    //     ShipView.cs, BodyAuthoring.cs, StarSystemAuthoring.cs,
    //     EphemerisRuntime.cs, Editor/StarSystemAuthoringEditor.cs.
    //   − P1bTests/UnityStubs.cs и P1bTests/Program.cs НЕ копировать
    //     (дубликаты UnityEngine-типов).
    //   − Всё из Core/, Events/, Spacecraft/, Debris/, Universe/ (кроме
    //     перечисленных выше Unity-файлов они и так входят в стенд).
    //   − Пакеты: com.unity.mathematics + com.unity.collections (Jobs-путь
    //     OrbitalElements.CalculateBatch; вызов Unity.Mathematics.math.max уже
    //     с строчным math).
    //   − Сцена: GameObject-иерархия тел с BodyAuthoring (имя GO = имя тела),
    //     на корне StarSystemAuthoring; на каждом теле BodyView; в сцене
    //     SimulationRunner (System = корень), ShipController, ShipView
    //     (произвольный GameObject корабля).
    //   − Порядок исполнения: SimulationRunner.Update → BodyView.LateUpdate →
    //     ShipView.LateUpdate (Script Execution Order или дефолт Update/LateUpdate
    //     уже даёт нужный порядок между Update и LateUpdate).

    /// <summary>
    /// Владелец времени и физического цикла (B1). Единственный MonoBehaviour,
    /// который двигает симуляцию; BodyView/ShipView читают готовые состояния.
    ///
    /// Контракт кадра (исполнение контракта EphemerisRuntime.ComputeFrame):
    /// 1. decision = EphemerisRuntime.ComputeFrame(sys, warp, ship, t, realDt);
    /// 2. useLongWarp → warp.PrepareForLongWarp(t) + LongWarpDriver.AdvanceToTarget
    ///    (баллистика, тяга запрещена целиком);
    /// 3. иначе — физическая цепочка с тем же RHS, что на ×1: чанки по границе
    ///    control-tick, SampleControl на каждом чанке, Propagate.
    /// HardHorizonSeconds выставляется из BakedEndSeconds() при старте — варп не
    /// улетит за конец испечённого мира (EphemerisEnd-событие сработает честно).
    /// Время физической цепочки накапливается Кахеном (годы варпа, тысячи кадров);
    /// после дальнего варпа аккумулятор сбрасывается на время результата.
    /// Вызовы — только с главного потока.
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [Header("Система")]
        [Tooltip("StarSystemAuthoring на корневом GameObject иерархии тел.")]
        public StarSystemAuthoring System;

        [Tooltip("Пауза симуляции (время не идёт, рендер остаётся).")]
        public bool Paused;

        [Header("Корабль")]
        [Tooltip("Начальная высота над поверхностью тела спавна (м).")]
        public double SpawnAltitudeMeters = 200000d;

        [Tooltip("Начальная горизонтальная скорость (м/с): 0 — сидим, ~7800 — орбита.")]
        public double SpawnHorizontalSpeedMps = 7800d;

        [Tooltip("Начальная масса корабля, кг.")]
        public double SpawnMassKg = 5000d;

        [Tooltip("Максимальная тяга главного двигателя, Н (0 — корабль без тяги).")]
        public double MainThrustNewtons;

        [Tooltip("Удельный импульс, с.")]
        public double MainIspSeconds = 320d;

        [Tooltip("Сухая масса (без топлива), кг.")]
        public double DryMassKg = 1000d;

        [Tooltip("Тело спавна (имя из иерархии); пусто — самое глубокое тело под корнем.")]
        public string SpawnBodyName;

        /// <summary>Симулированное время системы (с от эпохи эфемерид).</summary>
        public double TimeSeconds { get; private set; }

        public StarSystem SystemState { get; private set; }

        public WarpController Warp { get; private set; }

        public Ship Ship { get; private set; }

        /// <summary>Сырой газ 0..1 от ввода (ShipController); EffectiveThrottle капает выше ×3.</summary>
        public double RawThrottle { get; set; }

        /// <summary>Реестр трансформов тел (заполняют BodyView); ShipView читает для floating origin.</summary>
        public BodyTransformRegistry SystemView { get; set; }

        /// <summary>Тело, относительно которого сейчас живёт корабль (floating origin, SOI).</summary>
        public OrbitingBody DominantBody { get; private set; }

        private SpacecraftPhysics physics;
        private EventDrivenPropagator propagator;
        private LongWarpDriver driver;
        private ThrustSource mainThrust;
        private KahanAccumulator time;

        private void Awake()
        {
            if (System == null)
            {
                enabled = false;
                Debug.LogWarning("SimulationRunner: не назначен StarSystemAuthoring — симуляция отключена.");
                return;
            }

            SystemState = System.BuildBlueprint().Build();
            bool baked = EphemerisRuntime.AttachFromStreamingAssets(SystemState);

            Warp = new WarpController();
            physics = new SpacecraftPhysics();
            physics.Sources.Add(new CachedGravitySource(SystemState));
            if (MainThrustNewtons > 0d)
            {
                mainThrust = new ThrustSource(MainThrustNewtons, DryMassKg, new ConstantIsp(MainIspSeconds));
                mainThrust.ThrottleAt = t => Warp.CurrentControlSnapshot.Throttle;
                physics.Sources.Add(mainThrust);
            }

            propagator = new EventDrivenPropagator(physics);
            // Контракт горизонта: дальний варп не летит за конец испечённого мира
            // (в wiring из ComputeFrame продублирован явно — без него
            // EphemerisEnd-событие не сработало бы).
            propagator.HardHorizonSeconds = SystemState.BakedEndSeconds();
            driver = new LongWarpDriver(physics, propagator);

            SpawnShip();
            time = new KahanAccumulator(0d);
            TimeSeconds = 0d;
            if (!baked)
            {
                Debug.LogWarning("SimulationRunner: эфемериды не прицеплены — тела на кеплеровых рельсах (штатно, точность ниже).");
            }
        }

        private void SpawnShip()
        {
            DominantBody = FindSpawnBody();
            DominantBody.EvaluateWorldState(0d, out Vector3d bodyP, out Vector3d bodyV);
            Vector3d radial = bodyP - SystemState.Root.RootPosition;
            if (radial.SqrMagnitude <= 0d)
            {
                radial = new Vector3d(0d, 0d, 1d);
            }

            radial = radial.Normalized;
            Vector3d horizontal = Vector3d.Cross(new Vector3d(0d, 0d, 1d), radial);
            if (horizontal.SqrMagnitude < 1e-30d)
            {
                horizontal = new Vector3d(1d, 0d, 0d);
            }

            horizontal = horizontal.Normalized;
            Ship = new Ship(
                bodyP + (radial * (DominantBody.Radius + SpawnAltitudeMeters)),
                bodyV + (horizontal * SpawnHorizontalSpeedMps),
                SpawnMassKg);
        }

        private OrbitingBody FindSpawnBody()
        {
            if (!string.IsNullOrEmpty(SpawnBodyName))
            {
                foreach (OrbitingBody body in SystemState.AllBodies)
                {
                    if (body.Name == SpawnBodyName)
                    {
                        return body;
                    }
                }

                throw new InvalidOperationException("SimulationRunner: тело спавна \"" + SpawnBodyName + "\" не найдено в системе.");
            }

            // Самое глубокое тело под корнем (первая планета по иерархии).
            OrbitingBody deepest = SystemState.Root;
            while (deepest.Children.Count > 0)
            {
                deepest = deepest.Children[0];
            }

            return deepest;
        }

        private void Update()
        {
            if (Paused || Ship == null)
            {
                return;
            }

            float realDt = Time.deltaTime;
            if (!(realDt > 0f))
            {
                return;
            }

            WarpFrameDecision decision = EphemerisRuntime.ComputeFrame(
                SystemState, Warp, Ship.Position, TimeSeconds, realDt);
            double frameEnd = TimeSeconds + decision.SimSeconds;

            if (decision.UseLongWarp)
            {
                // Дальний варп: баллистика целиком, тяга запрещена по построению.
                Warp.PrepareForLongWarp(TimeSeconds);
                driver.CancelRequested = false;
                LongWarpResult result = driver.AdvanceToTarget(Ship, TimeSeconds, frameEnd);
                TimeSeconds = result.ReachedTimeSeconds;
                time.Reset(TimeSeconds);
            }
            else
            {
                double throttle = Warp.EffectiveThrottle(RawThrottle, decision.EffectiveFactor);
                bool thrustActive = mainThrust != null && throttle > 0d;
                while (TimeSeconds < frameEnd - 1e-12d)
                {
                    double chunk = Warp.GetMaxChunkSeconds(TimeSeconds, OrbitIntegrator.DefaultMaxStepSize, thrustActive);
                    chunk = Math.Min(chunk, frameEnd - TimeSeconds);
                    if (chunk <= 0d)
                    {
                        chunk = Math.Min(OrbitIntegrator.DefaultMinStepSize, frameEnd - TimeSeconds);
                    }

                    Warp.SampleControl(TimeSeconds, throttle);
                    propagator.Propagate(Ship, TimeSeconds, chunk);
                    time.Add(chunk);
                    TimeSeconds = time.Sum;
                }
            }

            DominantBody = SystemState.FindDominantBody(Ship.Position, TimeSeconds);
        }
    }
}
