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
    /// Автомат режимов (SurfaceMotion.VesselRegime):
    ///   Flying  — орбитальная цепочка: чанки по control-tick, SampleControl,
    ///             Propagate; вернувшееся событие обрабатывается здесь.
    ///   Landed  — стеснённая динамика SurfaceMotion.Step по чанкам; при тяге
    ///             EventReactions.TryLiftoff возвращает в Flying.
    ///   Destroyed — симуляция корабля стоит (обломки — этап R2/A4).
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
    /// На земле время тоже Кахеном: после событий с точным корнем аккумулятор
    /// сбрасывается на время события.
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

        [Tooltip("Спавнить корабль на солнечной стороне (со стороны звезды) вместо противоположной.")]
        public bool SpawnFacingSun;

        [Tooltip("Стартовая фаза по орбите (градусы). 0 — солнце в зените (при SpawnFacingSun), 90 — солнце на горизонте, 180 — за планетой. Фаза вращает точку старта вдоль плоскости орбиты.")]
        public double SpawnPhaseDegrees;

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

        [Tooltip("Спавнить игрока пешком на поверхности (OnSurface), корабль — рядом на земле. Иначе старт в корабле на орбите.")]
        public bool SpawnOnSurface;

        [Tooltip("Отступ спавна обломков над поверхностью при разрушении (м).")]
        public double SpawnEpsilonMeters = 1d;

        [Header("Сборка (для разрушения по стыкам)")]
        [Tooltip("Детали сборки. Пусто — корабль цельный (whole-ship разрушение). Массы нормируются к SpawnMassKg.")]
        public PartDefinition[] Parts = new PartDefinition[0];

        [Header("Обломки")]
        [Tooltip("Время жизни обломка (с).")]
        public double DebrisLifetimeSeconds = 600d;

        [Tooltip("Коэффициент отскока обломков от поверхности (0 — исчезают при входе в тело).")]
        public double DebrisSurfaceRestitution = 0.2d;

        /// <summary>
        /// Сериализуемое описание детали. Offset — в системе корабля, м
        /// (локальная геометрия: например, танк выше, двигатель ниже).
        /// </summary>
        [Serializable]
        public class PartDefinition
        {
            public string Name = "Деталь";
            public double MassKg = 1000d;
            public double JointStrengthNewtons = 200000d;
            public Vector3 Offset;
        }

        /// <summary>Симулированное время системы (с от эпохи эфемерид).</summary>
        public double TimeSeconds { get; private set; }

        public StarSystem SystemState { get; private set; }

        public WarpController Warp { get; private set; }

        public Ship Ship { get; private set; }

        /// <summary>Режим корабля: летит, стоит на поверхности или разрушен.</summary>
        public VesselRegime Regime { get; private set; }

        /// <summary>Режим игрока: в корабле / EVA / на поверхности.</summary>
        public PlayerMode PlayerMode { get; private set; }

        /// <summary>Позиция игрока в double-мире (астро-кадр, инерциальный).</summary>
        public Vector3d PlayerPosition { get; private set; }

        /// <summary>Скорость игрока в double-мире.</summary>
        public Vector3d PlayerVelocity { get; private set; }

        /// <summary>Намерение игрока на кадр (заполняет PlayerController).</summary>
        public PlayerIntent PlayerIntent;

        /// <summary>Сырой газ 0..1 от ввода (ShipController); EffectiveThrottle капает выше ×3.</summary>
        public double RawThrottle { get; set; }

        /// <summary>Реестр трансформов тел (заполняют BodyView); ShipView читает для floating origin.</summary>
        public BodyTransformRegistry SystemView { get; set; }

        /// <summary>Тело, относительно которого сейчас живёт корабль (floating origin, SOI).</summary>
        public OrbitingBody DominantBody { get; private set; }

        /// <summary>Пул обломков (слоты переиспользуются); DebrisView читает для рендера.</summary>
        public DebrisPool Debris { get; private set; }

        private SpacecraftPhysics physics;
        private EventDrivenPropagator propagator;
        private LongWarpDriver driver;
        private ThrustSource mainThrust;
        private KahanAccumulator time;
        private IBreakupModel breakupModel;
        private DebrisUpdater debrisUpdater;
        private bool playerAirborne;

        /// <summary>Скорость прыжка игрока (м/с, вертикально от поверхности).</summary>
        private const double PlayerJumpSpeed = 2.5d;

        /// <summary>Максимальный подшаг интегрирования игрока (с).</summary>
        private const double PlayerMaxStepSeconds = 0.5d;

        /// <summary>Верхняя граница подшага на поверхности (с), согласована с зерном control-tick.</summary>
        private const double SurfaceMaxStepSeconds = 0.5d;

        private static readonly SphericalTerrain surfaceTerrain = new SphericalTerrain();

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
            foreach (OrbitingBody body in SystemState.AllBodies)
            {
                propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(body));
                if (body.Atmosphere != null)
                {
                    propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(body));
                    propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(body));
                    physics.Sources.Add(new DragSource(body));
                }
            }

            breakupModel = new JointedBreakup();
            Debris = new DebrisPool();
            debrisUpdater = new DebrisUpdater(SystemState)
            {
                SurfaceRestitution = DebrisSurfaceRestitution
            };
            // Контракт горизонта: дальний варп не летит за конец испечённого мира
            // (в wiring из ComputeFrame продублирован явно — без него
            // EphemerisEnd-событие не сработало бы).
            propagator.HardHorizonSeconds = SystemState.BakedEndSeconds();
            driver = new LongWarpDriver(physics, propagator);

            SpawnShip();
            time = new KahanAccumulator(0d);
            TimeSeconds = 0d;
            Regime = VesselRegime.Flying;
            PlayerMode = PlayerMode.InShip;
            PlayerPosition = Ship.Position;
            PlayerVelocity = Ship.Velocity;
            if (SpawnOnSurface)
            {
                SpawnPlayerOnSurface();
            }

            FloatingOrigin.Anchor = PlayerPosition;

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
            if (SpawnFacingSun)
            {
                radial = -radial;
            }

            Vector3d horizontal = Vector3d.Cross(new Vector3d(0d, 0d, 1d), radial);
            if (horizontal.SqrMagnitude < 1e-30d)
            {
                horizontal = new Vector3d(1d, 0d, 0d);
            }

            horizontal = horizontal.Normalized;

            // Фаза: поворот точки старта вдоль плоскости орбиты. 0° — точка
            // «под солнцем» (или против него), 90° — четверть витка (солнце на
            // горизонте), 180° — противоположная сторона планеты.
            double phase = SpawnPhaseDegrees * (Math.PI / 180d);
            Vector3d positionDirection = radial;
            Vector3d velocityDirection = horizontal;
            if (Math.Abs(phase) > 1e-12d)
            {
                Vector3d normal = Vector3d.Cross(radial, horizontal);
                Vector3d towardPhase = Vector3d.Cross(normal, radial);
                positionDirection = (radial * Math.Cos(phase)) + (towardPhase * Math.Sin(phase));
                velocityDirection = Vector3d.Cross(normal, positionDirection);
            }

            Ship = new Ship(
                bodyP + (positionDirection * (DominantBody.Radius + SpawnAltitudeMeters)),
                bodyV + (velocityDirection * SpawnHorizontalSpeedMps),
                SpawnMassKg);
        }

        /// <summary>
        /// Спавн пешком: корабль ставится на рельеф в точке спавна, игрок — в
        /// нескольких метрах по касательной (чтобы камера не оказалась внутри
        /// корабля, но дистанция входа E была в пределах EnterDistance).
        /// </summary>
        private void SpawnPlayerOnSurface()
        {
            OrbitingBody body = DominantBody;
            body.EvaluateWorldState(0d, out Vector3d bodyP, out _);
            body.SurfaceLatLonAt(Ship.Position, 0d, out double latDeg, out double lonDeg);
            double ground = GroundHeight(body, latDeg, lonDeg);
            body.GetSurfaceState(latDeg, lonDeg, ground, 0d, out Vector3d surfacePos, out Vector3d surfaceVel);

            Ship.Position = surfacePos;
            Ship.Velocity = surfaceVel;
            Regime = VesselRegime.Landed;

            Vector3d up = (surfacePos - bodyP).Normalized;
            Vector3d east = Vector3d.Cross(new Vector3d(0d, 0d, 1d), up);
            if (east.SqrMagnitude < 1e-12d)
            {
                east = new Vector3d(1d, 0d, 0d);
            }

            east = east.Normalized;
            body.SurfaceLatLonAt(surfacePos + (east * 4d), 0d, out double playLat, out double playLon);
            double playGround = GroundHeight(body, playLat, playLon);
            body.GetSurfaceState(playLat, playLon, playGround, 0d, out Vector3d playPos, out Vector3d playVel);

            PlayerPosition = playPos;
            PlayerVelocity = playVel;
            PlayerMode = PlayerMode.OnSurface;
            playerAirborne = false;
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
            if (Paused || Ship == null || Regime == VesselRegime.Destroyed)
            {
                return;
            }

            float realDt = Time.deltaTime;
            if (!(realDt > 0f))
            {
                return;
            }

            double frameStartTime = TimeSeconds;
            if (Regime == VesselRegime.Landed)
            {
                StepLanded(realDt);
            }
            else
            {
                StepFlying(realDt);
            }

            if (Debris != null)
            {
                debrisUpdater.AdvanceAll(Debris, TimeSeconds, Ship.Position);
            }

            StepPlayer(frameStartTime);
            DominantBody = SystemState.FindDominantBody(Ship.Position, TimeSeconds);

            // Якорь рендера — позиция игрока (floating origin): вьюхи в LateUpdate
            // читают свежий якорь, поэтому ставим в самом конце шага физики.
            FloatingOrigin.Anchor = PlayerPosition;
        }

        // ─── Игрок ───────────────────────────────────────────────────────────

        /// <summary>
        /// Выход из корабля (только из InShip). Игрок ставится в 3 м вдоль
        /// exitDirection с малым толчком; если корабль на поверхности — первый
        /// же шаг посадит игрока на землю (OnSurface).
        /// </summary>
        public bool TryExitShip(Vector3d exitDirection, double pushSpeed)
        {
            if (PlayerMode != PlayerMode.InShip || Ship == null)
            {
                return false;
            }

            Vector3d direction = exitDirection.Normalized;
            PlayerPosition = Ship.Position + (direction * 3d);
            PlayerVelocity = Ship.Velocity + (direction * pushSpeed);
            PlayerMode = PlayerMode.EVA;
            playerAirborne = false;
            return true;
        }

        /// <summary>Вход в корабль: EVA/OnSurface и дистанция до корабля ≤ EnterDistance.</summary>
        public bool TryEnterShip(double enterDistanceMeters)
        {
            if (PlayerMode == PlayerMode.InShip || Ship == null)
            {
                return false;
            }

            if ((PlayerPosition - Ship.Position).Magnitude > enterDistanceMeters)
            {
                return false;
            }

            PlayerMode = PlayerMode.InShip;
            PlayerPosition = Ship.Position;
            PlayerVelocity = Ship.Velocity;
            playerAirborne = false;
            return true;
        }

        /// <summary>
        /// Шаг игрока за кадр. Пока игрок вне корабля, физическое время капится
        /// (см. StepFlying/StepLanded: варп-фактор ≤ 4), поэтому подшаг ≤ 0.5 с
        /// всегда успевает за кадр; дальний варп — только когда игрок в корабле.
        /// </summary>
        private void StepPlayer(double fromTime)
        {
            double t = fromTime;
            while (t < TimeSeconds - 1e-12)
            {
                double dt = Math.Min(PlayerMaxStepSeconds, TimeSeconds - t);
                switch (PlayerMode)
                {
                    case PlayerMode.InShip:
                        PlayerPosition = Ship.Position;
                        PlayerVelocity = Ship.Velocity;
                        break;
                    case PlayerMode.EVA:
                        StepPlayerEva(t, dt);
                        break;
                    case PlayerMode.OnSurface:
                        StepPlayerSurface(t, dt);
                        break;
                }

                t += dt;
            }
        }

        private void StepPlayerEva(double t, double dt)
        {
            Vector3d gravity = SystemState.EvaluateShipAcceleration(PlayerPosition, t);
            PlayerVelocity += (gravity + PlayerIntent.JetpackAccel) * dt;
            PlayerPosition += PlayerVelocity * dt;

            OrbitingBody body = DominantBody;
            if (body == null)
            {
                return;
            }

            // Позиция уже в t+dt — контакт проверяем и сажаем в t+dt, не в t.
            double tEnd = t + dt;
            body.EvaluateWorldState(tEnd, out Vector3d bodyPos, out _);
            body.SurfaceLatLonAt(PlayerPosition, tEnd, out double latDeg, out double lonDeg);
            double terrainRadius = body.Radius + GroundHeight(body, latDeg, lonDeg);
            if ((PlayerPosition - bodyPos).Magnitude <= terrainRadius)
            {
                LandPlayer(body, tEnd);
            }
        }

        /// <summary>
        /// Касание поверхности игроком: позиция проецируется на поверхность,
        /// нормальная скорость съедается, тангенциальная сохраняется. Жёсткость
        /// удара логируется (травмы/смерть — будущий этап). Спавн-эпсилон не
        /// нужен — игрок становится ровно на поверхность.
        /// </summary>
        private void LandPlayer(OrbitingBody body, double t)
        {
            body.SurfaceLatLonAt(PlayerPosition, t, out double latDeg, out double lonDeg);
            body.GetSurfaceState(latDeg, lonDeg, GroundHeight(body, latDeg, lonDeg), t, out Vector3d surfacePos, out Vector3d surfaceVel);
            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            Vector3d normal = body.Terrain != null
                ? body.Terrain.GetOutwardNormal(body, PlayerPosition - bodyPos, t).Normalized
                : (PlayerPosition - bodyPos).Normalized;
            Vector3d relativeVelocity = PlayerVelocity - surfaceVel;
            double normalSpeed = Vector3d.Dot(relativeVelocity, normal);
            Vector3d tangential = relativeVelocity - (normal * normalSpeed);
            PlayerPosition = surfacePos;
            PlayerVelocity = surfaceVel + tangential;
            PlayerMode = PlayerMode.OnSurface;
            playerAirborne = false;
            if (normalSpeed < -10d)
            {
                Debug.Log("Жёсткое приземление игрока: " + (-normalSpeed).ToString("F1") + " м/с (травмы — будущий этап)");
            }
        }

        private void StepPlayerSurface(double t, double dt)
        {
            OrbitingBody body = DominantBody;
            if (body == null)
            {
                return;
            }

            body.EvaluateWorldState(t, out Vector3d bodyPos, out Vector3d bodyVel);

            if (playerAirborne)
            {
                // Баллистика прыжка: свободное падение до контакта.
                Vector3d gravity = SystemState.EvaluateShipAcceleration(PlayerPosition, t);
                PlayerVelocity += gravity * dt;
                PlayerPosition += PlayerVelocity * dt;
                // Позиция уже в t+dt — контакт проверяем и сажаем в t+dt, не в t.
                double airEnd = t + dt;
                body.SurfaceLatLonAt(PlayerPosition, airEnd, out double airLat, out double airLon);
                body.EvaluateWorldState(airEnd, out Vector3d airBodyPos, out _);
                double groundRadius = body.Radius + GroundHeight(body, airLat, airLon);
                if ((PlayerPosition - airBodyPos).Magnitude <= groundRadius)
                {
                    LandPlayer(body, airEnd);
                }

                return;
            }

            // Ходьба: контакт с поверхностью, скорость = поверхность + намерение.
            body.SurfaceLatLonAt(PlayerPosition, t, out double latDeg, out double lonDeg);
            double ground = GroundHeight(body, latDeg, lonDeg);
            body.GetSurfaceState(latDeg, lonDeg, ground, t, out Vector3d contact, out Vector3d surfaceVel);
            body.EvaluateWorldState(t, out Vector3d bodyPos2, out _);
            Vector3d normal = body.Terrain != null
                ? body.Terrain.GetOutwardNormal(body, contact - bodyPos2, t).Normalized
                : (contact - bodyPos2).Normalized;

            Vector3d walk = PlayerIntent.WalkDirection * PlayerIntent.WalkSpeed;
            Vector3d tangential = walk - (normal * Vector3d.Dot(walk, normal));
            PlayerVelocity = surfaceVel + tangential;
            PlayerPosition += PlayerVelocity * dt;

            // Проекция обратно на поверхность НА КОНЕЦ подшага: и lat/lon, и
            // центр тела берём в t+dt. Иначе позиция (уже в t+dt) проецируется
            // на сферу времени t — остаётся тангенциальная ошибка bodyVel·dt
            // (орбитальная скорость тела ~5 км/с), игрока тянет к точке, где
            // орбитальная скорость радиальна, и он там осциллирует: сильная
            // дрожь на месте, адская на бегу. Тот же принцип, что CoRotate в
            // SurfaceMotion (перенос в toTime + проекция в toBodyP).
            double tEnd = t + dt;
            body.SurfaceLatLonAt(PlayerPosition, tEnd, out double newLat, out double newLon);
            double newGround = GroundHeight(body, newLat, newLon);
            body.EvaluateWorldState(tEnd, out Vector3d bodyPos3, out _);
            Vector3d radial = PlayerPosition - bodyPos3;
            PlayerPosition = bodyPos3 + (radial.Normalized * (body.Radius + newGround));

            if (PlayerIntent.Jump)
            {
                playerAirborne = true;
                PlayerVelocity += normal * PlayerJumpSpeed;
            }
        }

        /// <summary>Высота рельефа тела в точке (lat/lon, градусы); сфера при null.</summary>
        private static double GroundHeight(OrbitingBody body, double latDeg, double lonDeg)
        {
            return body.Terrain?.GetHeightMeters(body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d)) ?? 0d;
        }

        private void StepFlying(float realDt)
        {
            WarpFrameDecision decision = EphemerisRuntime.ComputeFrame(
                SystemState, Warp, Ship.Position, TimeSeconds, realDt);
            // Игрок вне корабля (EVA/на поверхности) — дальний варп запрещён,
            // физический кап ×4: иначе игрока не догнать подшагами и он
            // «потеряется» в полёте за один кадр.
            double playerCap = PlayerMode == PlayerMode.InShip ? double.PositiveInfinity : 4d;
            double factor = Math.Min(decision.EffectiveFactor, playerCap);
            double frameEnd = TimeSeconds + (realDt * factor);

            if (decision.UseLongWarp && playerCap == double.PositiveInfinity)
            {
                // Дальний варп: баллистика целиком, тяга запрещена по построению.
                Warp.PrepareForLongWarp(TimeSeconds);
                driver.CancelRequested = false;
                LongWarpResult result = driver.AdvanceToTarget(Ship, TimeSeconds, frameEnd);
                TimeSeconds = result.ReachedTimeSeconds;
                time.Reset(TimeSeconds);
                return;
            }

            double throttle = Warp.EffectiveThrottle(RawThrottle, factor);
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
                EventOccurrence? occurrence = propagator.Propagate(Ship, TimeSeconds, chunk);
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

        private void StepLanded(float realDt)
        {
            WarpFrameDecision decision = EphemerisRuntime.ComputeFrame(
                SystemState, Warp, Ship.Position, TimeSeconds, realDt);
            // На поверхности дальний варп запрещён: кап физического ×3,
            // как в атмосфере (SurfaceMotion точен на любом dt, зерно — control-tick).
            // Игрок вне корабля добавляет свой кап ×4 (см. StepFlying).
            double playerCap = PlayerMode == PlayerMode.InShip ? double.PositiveInfinity : 4d;
            double factor = Math.Min(Math.Min(decision.EffectiveFactor, WarpController.AtmosphereMaxWarpFactor), playerCap);
            double frameEnd = TimeSeconds + (realDt * factor);
            double throttle = Warp.EffectiveThrottle(RawThrottle, factor);
            bool thrustActive = mainThrust != null && throttle > 0d;

            while (TimeSeconds < frameEnd - 1e-12d)
            {
                if (thrustActive)
                {
                    Vector3d gravity = SystemState.EvaluateShipAcceleration(Ship.Position, TimeSeconds);
                    DynamicsContribution thrust = mainThrust.Evaluate(Ship.Position, Ship.Velocity, Ship.Mass, TimeSeconds);
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
            switch (occurrence.Kind)
            {
                case EventKind.Touchdown:
                    TouchdownOutcome outcome = EventReactions.ApplyTouchdown(
                        Ship, occurrence, occurrence.Body, breakupModel, SpawnEpsilonMeters,
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
