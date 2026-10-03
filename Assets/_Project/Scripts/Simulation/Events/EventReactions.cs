using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Состояние посадки: позиция спроецирована на поверхность, нормальная
    /// скорость съедена, тангенциальная сохранена. NormalSpeed/TangentialSpeed —
    /// замер в миг касания (до съедения), для проверки перехода T20, не итога.
    /// </summary>
    public readonly struct LandedState
    {
        public readonly double LatitudeDegrees;
        public readonly double LongitudeDegrees;
        public readonly Vector3d Position;
        public readonly Vector3d Velocity;
        public readonly double NormalSpeed;
        public readonly double TangentialSpeed;

        public LandedState(double latitudeDegrees, double longitudeDegrees, Vector3d position, Vector3d velocity, double normalSpeed, double tangentialSpeed)
        {
            LatitudeDegrees = latitudeDegrees;
            LongitudeDegrees = longitudeDegrees;
            Position = position;
            Velocity = velocity;
            NormalSpeed = normalSpeed;
            TangentialSpeed = tangentialSpeed;
        }
    }

    /// <summary>Итог touchdown: либо посадка, либо спеки разрушения (не спавн).</summary>
    public readonly struct TouchdownOutcome
    {
        public readonly bool IsLanding;
        public readonly LandedState Landed;
        public readonly IReadOnlyList<PartSeparationSpec> Specs;

        public TouchdownOutcome(LandedState landed)
        {
            IsLanding = true;
            Landed = landed;
            Specs = null;
        }

        public TouchdownOutcome(IReadOnlyList<PartSeparationSpec> specs)
        {
            IsLanding = false;
            Landed = default;
            Specs = specs;
        }
    }

    public readonly struct AtmosphereResult
    {
        public readonly bool Inside;

        public AtmosphereResult(bool inside)
        {
            Inside = inside;
        }
    }

    /// <summary>
    /// Новое доминантное тело берётся из события как есть: драйвер уже положил
    /// туда ключ перехода (key1). Реакция НЕ пересчитывает SOI — запрет
    /// дублирования физической логики в реакционном слое.
    /// </summary>
    public readonly struct FocusResult
    {
        public readonly OrbitingBody NewBody;

        public FocusResult(OrbitingBody newBody)
        {
            NewBody = newBody;
        }
    }

    /// <summary>
    /// Реакции на события. Драйвер остаётся detect-only (P1a-инвариант):
    /// сюда приходит уже зафиксированное EventOccurrence, назад в propagator
    /// ничего не возвращается. Propagator не импортируется принципиально:
    /// всё нужное (вид, тело, детектор-источник, состояние) уже лежит в событии.
    /// Режим судна (Flying/Landed/Destroyed) хранит игровой цикл; Spacecraft
    /// здесь мутирует только проекцией посадки и клампом массы.
    /// </summary>
    public static class EventReactions
    {
        /// <summary>
        /// Касание. Критерий — НОРМАЛЬНАЯ составляющая удара v_n = v_impact·n:
        /// большая тангенциальная при малой нормальной — жёсткая посадка со
        /// скольжением (уходит в SurfaceMotion с начальным v_t), а не ложно
        /// мягкая. Граница детерминирована: |v_n| ≤ допуск → посадка.
        /// Нормаль здесь радиальная; ITerrainNormal обобщит её в A4.4.
        /// </summary>
        public static TouchdownOutcome ApplyTouchdown(Ship ship, EventOccurrence occurrence, OrbitingBody body, IBreakupModel breakup, double spawnEpsilonMeters, System.Collections.Generic.IReadOnlyList<Part> parts = null)
        {
            if (ship == null)
                throw new ArgumentNullException(nameof(ship));
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (breakup == null)
                throw new ArgumentNullException(nameof(breakup));
            if (occurrence.Kind != EventKind.Touchdown)
                throw new ArgumentException("ApplyTouchdown принимает только Touchdown.", nameof(occurrence));

            double t = occurrence.TimeSeconds;
            body.SurfaceLatLonAt(ship.Position, t, out double latDeg, out double lonDeg);
            // Хук мягкого приводнения (этап «корабль садится на воду»):
            // посадка ПОКА идёт по клампнутой высоте (плоскость моря) без
            // изменения поведения. Будущее правило: splash.IsWater и глубина
            // больше посадочной осадки и |v_n| в допуске приводнения →
            // посадка вместо разрушения (вода смягчает удар).
            WaterQuery.SplashdownInfo splash = WaterQuery.GetSplashdownInfo(body, latDeg, lonDeg);
            double groundAltitude = GroundAltitude(body, latDeg, lonDeg);
            body.GetSurfaceState(latDeg, lonDeg, groundAltitude, t, out Vector3d surfacePosition, out Vector3d surfaceVelocity);
            body.EvaluateWorldState(t, out Vector3d bodyPosition, out _);

            Vector3d normal = body.Terrain != null
                ? body.Terrain.GetOutwardNormal(body, ship.Position - bodyPosition, t).Normalized
                : (ship.Position - bodyPosition).Normalized;
            Vector3d impactVelocity = ship.Velocity - surfaceVelocity;
            double normalSpeed = Vector3d.Dot(impactVelocity, normal);
            Vector3d tangentialVelocity = impactVelocity - (normal * normalSpeed);
            double tangentialSpeed = tangentialVelocity.Magnitude;

            if (Math.Abs(normalSpeed) <= body.CrashToleranceMps)
            {
                LandedState landed = LandShip(ship, latDeg, lonDeg, surfacePosition, surfaceVelocity, tangentialVelocity, normalSpeed, tangentialSpeed);
                return new TouchdownOutcome(landed);
            }

            var input = new BreakupInput(
                ship.Mass, impactVelocity, surfaceVelocity,
                surfacePosition, normal, spawnEpsilonMeters, parts);
            return new TouchdownOutcome(breakup.PlanBreakup(input));
        }

        /// <summary>
        /// Переход Landed → Flying (LiftoffHook из автомата режимов). Критерий
        /// отрыва синхронизирован с SurfaceMotion: свободное ускорение в
        /// системе поверхности a = тяга + гравитация − ω×(ω×r) − 2ω×v_t;
        /// контакт держится ⟺ a·n ≤ ContactEpsilon. Центробежный член обязателен:
        /// при быстром спине (ω²R &gt; |g|) контакт не держится даже с нулевой
        /// тягой (T52/T70 — оба пути отрыва дают один вердикт). ω = 0 → формула
        /// вырождается в старый критерий (тяга+гравитация)·n &gt; 0.
        /// При отрыве корабль получает скорость поверхности + сохранённую
        /// тангенциальную (тот же путь, что LandShip): со-вращающийся корабль
        /// не меняет скорость (нет двойного учёта), а поставленный с v=0 на
        /// вращающуюся планету корректно увлекается её вращением.
        /// Иначе — остаётся: проекция на поверхность + съеденная нормальная
        /// скорость (общий путь LandShip, не вторая реализация).
        /// Детерминированная граница: |a·n| ≤ eps → контакт держится.
        /// Нормаль здесь радиальная; ITerrainNormal обобщит её в A4.4.
        /// Контракт параметров: thrustAccelWorld и gravityAccelWorld — ускорения
        /// В ТОЧКЕ КОРАБЛЯ на timeSeconds (gravityAccelWorld =
        /// starSystem.EvaluateShipAcceleration(ship.Position, timeSeconds));
        /// гравитация в чужой точке/времени рассинхронит критерий.
        /// </summary>
        public static VesselRegime TryLiftoff(Ship ship, OrbitingBody body, Vector3d thrustAccelWorld, Vector3d gravityAccelWorld, double timeSeconds, out LandedState landedState)
        {
            if (ship == null)
                throw new ArgumentNullException(nameof(ship));
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            body.SurfaceLatLonAt(ship.Position, timeSeconds, out double latDeg, out double lonDeg);
            body.GetSurfaceState(latDeg, lonDeg, GroundAltitude(body, latDeg, lonDeg), timeSeconds, out Vector3d surfacePosition, out Vector3d surfaceVelocity);
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);

            Vector3d normal = body.Terrain != null
                ? body.Terrain.GetOutwardNormal(body, ship.Position - bodyPosition, timeSeconds).Normalized
                : (ship.Position - bodyPosition).Normalized;
            Vector3d impactVelocity = ship.Velocity - surfaceVelocity;
            double normalSpeed = Vector3d.Dot(impactVelocity, normal);
            Vector3d tangentialVelocity = impactVelocity - (normal * normalSpeed);

            Vector3d omegaVector = body.SpinAxis * body.SpinAngularSpeed;
            Vector3d freeAcceleration = thrustAccelWorld + gravityAccelWorld
                - Vector3d.Cross(omegaVector, Vector3d.Cross(omegaVector, ship.Position - bodyPosition))
                - (Vector3d.Cross(omegaVector, tangentialVelocity) * 2d);
            if (Vector3d.Dot(freeAcceleration, normal) > SurfaceMotion.ContactEpsilon)
            {
                ship.Velocity = surfaceVelocity + tangentialVelocity;
                landedState = default;
                return VesselRegime.Flying;
            }

            landedState = LandShip(ship, latDeg, lonDeg, surfacePosition, surfaceVelocity, tangentialVelocity, normalSpeed, tangentialVelocity.Magnitude);
            return VesselRegime.Landed;
        }

        private static LandedState LandShip(Ship ship, double latDeg, double lonDeg, Vector3d surfacePosition, Vector3d surfaceVelocity, Vector3d tangentialVelocity, double normalSpeed, double tangentialSpeed)
        {
            ship.Position = surfacePosition;
            ship.Velocity = surfaceVelocity + tangentialVelocity;
            return new LandedState(latDeg, lonDeg, surfacePosition, ship.Velocity, normalSpeed, tangentialSpeed);
        }

        /// <summary>Высота поверхности в точке (lat/lon, градусы) по модели тела; сфера при null.</summary>
        private static double GroundAltitude(OrbitingBody body, double latDeg, double lonDeg)
        {
            return body.Terrain?.GetHeightMeters(body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d)) ?? 0d;
        }

        /// <summary>
        /// Выработка топлива: mass = max(mass, dry) — именно кламп с точным
        /// равенством mass == dry после реакции, а не «не меньше dry».
        /// Сухая масса берётся из детектора-источника события.
        /// </summary>
        public static double ApplyDepletion(Ship ship, EventOccurrence occurrence)
        {
            if (ship == null)
                throw new ArgumentNullException(nameof(ship));
            if (occurrence.Kind != EventKind.PropellantDepleted)
                throw new ArgumentException("ApplyDepletion принимает только PropellantDepleted.", nameof(occurrence));

            var detector = occurrence.SourceDetector as PropellantDepletionDetector;
            if (detector == null)
            {
                throw new InvalidOperationException("У события PropellantDepleted нет детектора-источника.");
            }

            ship.Mass = Math.Max(ship.Mass, detector.DryMassKg);
            return ship.Mass;
        }

        public static AtmosphereResult ApplyAtmosphere(EventOccurrence occurrence)
        {
            if (occurrence.Kind != EventKind.AtmosphereEntry && occurrence.Kind != EventKind.AtmosphereExit)
            {
                throw new ArgumentException("ApplyAtmosphere принимает только вход/выход.", nameof(occurrence));
            }

            return new AtmosphereResult(occurrence.Kind == EventKind.AtmosphereEntry);
        }

        public readonly struct EphemerisEndResult
        {
            public readonly double TimeSeconds;

            public EphemerisEndResult(double timeSeconds)
            {
                TimeSeconds = timeSeconds;
            }
        }

        public static EphemerisEndResult ApplyEphemerisEnd(EventOccurrence occurrence)
        {
            if (occurrence.Kind != EventKind.EphemerisEnd)
            {
                throw new ArgumentException("ApplyEphemerisEnd принимает только EphemerisEnd.", nameof(occurrence));
            }

            return new EphemerisEndResult(occurrence.TimeSeconds);
        }

        public static FocusResult ApplySoi(EventOccurrence occurrence)
        {
            if (occurrence.Kind != EventKind.SoiChange)
            {
                throw new ArgumentException("ApplySoi принимает только SoiChange.", nameof(occurrence));
            }

            if (occurrence.Body == null)
            {
                throw new InvalidOperationException("У события SoiChange нет нового тела.");
            }

            return new FocusResult(occurrence.Body);
        }
    }
}
