using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// Режим судна. Переходы (явный автомат, владелец — игровой цикл):
    /// Flying + Touchdown(soft) → Landed (EventReactions, проекция+v_t);
    /// Flying + Touchdown(hard) → Destroyed (+PartSeparationSpecs);
    /// Landed + потеря контакта → Flying (здесь, Liftoff);
    /// Landed + LiftoffHook (тяга/Δv) → Flying (задел, триггер позже).
    /// Драйвер событий в режимах не участвует: он только детектит.
    /// </summary>
    public enum VesselRegime
    {
        Flying,
        Landed,
        Destroyed
    }

    /// <summary>
    /// Итог шага по поверхности: спроецированные мировые состояние, режим
    /// (Landed или Flying при отрыве), флаг покоя.
    /// </summary>
    public readonly struct SurfaceMotionResult
    {
        public readonly Vector3d Position;
        public readonly Vector3d Velocity;
        public readonly VesselRegime Regime;
        public readonly bool IsStuck;

        public SurfaceMotionResult(Vector3d position, Vector3d velocity, VesselRegime regime, bool isStuck)
        {
            Position = position;
            Velocity = velocity;
            Regime = regime;
            IsStuck = isStuck;
        }
    }

    /// <summary>
    /// Движение севшего судна: стеснённая динамика на поверхности.
    /// Состояние — декартово (мировые позиция/скорость), углы НЕ интегрируются:
    /// полюсов нет как класса, lat/lon получаются только для отчётов через
    /// SurfaceLatLonAt. Схема первого порядка по dt (игровые шаги ≤0.5с):
    /// точна для постоянных полей, для переменных — O(dt²) локально.
    ///
    /// Свободное ускорение в системе поверхности (согласованно: v_t строго
    /// относительно поверхности, иначе член Кориолиса неверен):
    ///   a_free = g − ω×(ω×r) − 2ω×v_t.
    /// Центробежный член −ω×(ω×r) направлен НАРУЖУ от оси: именно он
    /// позволяет отрыв при быстром спине (ω²R &gt; |g|). Знак пойман багом:
    /// с +ω×(ω×r) контакт держался всегда, отрыв не наступал никогда.
    /// Контакт держится ⟺ a_free·n &lt; 0 (прижатие). Детерминированная ветка
    /// границы: a_free·n &gt; ContactEpsilon → отрыв (Liftoff без изменения
    /// состояния, дальше летит игровой интегратор); |зона| ≤ eps → контакт
    /// держится (фликера stick/slip от fp нет). Проверка контакта — только
    /// в начале шага: внутришаговый переход разрешается следующим шагом
    /// (задержка ≤ dt, задокументирована, не баг).
    /// Стик: |a_t| ≤ μ_s·|a_n| → совместное вращение точным поворотом Родригеса.
    /// Слип: торможение μ_k·|a_n| против движения; t_stop = |v_t|/(μ_k·|a_n|);
    /// если t_stop &lt; dt — силы и статик-условие пересчитываются В МОМЕНТ
    /// ОСТАНОВКИ (один доп. вызов гравитации и состояния тела), не в начале.
    /// FreeAcceleration здесь БЕЗ тяги: тяга добавляется только на пути
    /// LiftoffHook (EventReactions.TryLiftoff, тот же критерий a·n &gt; eps) —
    /// стеснённая динамика шага считает прижатие от гравитации/спина.
    /// </summary>
    public static class SurfaceMotion
    {
        public const double ContactEpsilon = 1e-9d;

        /// <summary>
        /// Порог «тангенциальная скорость нулевая» для выбора стика. Должен
        /// стоять НАД fp-шумом вычитания абсолютных скоростей: relativeVelocity
        /// = velocity − surfaceVelocity с операндами ~3e4 м/с (орбитальная
        /// скорость планеты) даёт шум до ~1e-10 м/с (ulp(3e4)≈3.6e-12 на
        /// операцию). Старые 1e-12 были недостижимы — детерминизм стика держался
        /// только на ветке статического трения, сам порог не срабатывал никогда.
        /// </summary>
        public const double SlipSpeedEpsilon = 1e-9d;

        public delegate Vector3d GravityProvider(Vector3d position, double timeSeconds);

        public static SurfaceMotionResult Step(
            Vector3d position, Vector3d velocity,
            OrbitingBody body, ITerrainModel terrain,
            GravityProvider gravity, double timeSeconds, double dt)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));
            if (gravity == null)
                throw new ArgumentNullException(nameof(gravity));
            if (dt <= 0d)
                throw new ArgumentOutOfRangeException(nameof(dt), "Шаг SurfaceMotion обязан быть положительным.");

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d axis = body.SpinAxis;
            double omega = body.SpinAngularSpeed;
            Vector3d omegaVector = axis * omega;

            Vector3d contact = ProjectToSurface(body, terrain, bodyPosition, position, timeSeconds);
            Vector3d normal = terrain.GetOutwardNormal(body, contact - bodyPosition).Normalized;
            Vector3d surfaceVelocity = bodyVelocity + Vector3d.Cross(omegaVector, contact - bodyPosition);
            Vector3d relativeVelocity = velocity - surfaceVelocity;
            Vector3d tangentialVelocity = relativeVelocity - (normal * Vector3d.Dot(relativeVelocity, normal));

            Vector3d freeAcceleration = FreeAcceleration(gravity(contact, timeSeconds), omegaVector, contact - bodyPosition, tangentialVelocity);
            double normalAccel = Vector3d.Dot(freeAcceleration, normal);
            if (normalAccel > ContactEpsilon)
            {
                return new SurfaceMotionResult(position, velocity, VesselRegime.Flying, false);
            }

            double pressMagnitude = normalAccel < 0d ? -normalAccel : 0d;
            Vector3d tangentialAccel = freeAcceleration - (normal * normalAccel);
            double tangentialSpeed = tangentialVelocity.Magnitude;

            if (tangentialSpeed <= SlipSpeedEpsilon)
            {
                if (tangentialAccel.Magnitude <= body.SurfaceStaticFrictionMu * pressMagnitude)
                {
                    return CoRotate(contact, body, terrain, timeSeconds, timeSeconds + dt, omega, axis);
                }
            }

            Vector3d slideDirection;
            if (tangentialSpeed > SlipSpeedEpsilon)
            {
                slideDirection = tangentialVelocity / tangentialSpeed;
            }
            else if (tangentialAccel.Magnitude > 0d)
            {
                slideDirection = tangentialAccel.Normalized;
            }
            else
            {
                return CoRotate(contact, body, terrain, timeSeconds, timeSeconds + dt, omega, axis);
            }

            // Кинетика вдоль трека: s=|vT|, drive — проекция движущего aT на
            // направление движения (при старте с места — на aT), decel=μk|an|.
            // Скорость вдоль трека: ds/dt = drive − decel. Предполагается
            // μk ≤ μs (иначе статик и кинетика противоречат — ветки ниже всё
            // равно детерминированы, но физика конфигурации бессмысленна).
            Vector3d trackDir;
            if (tangentialSpeed > SlipSpeedEpsilon)
            {
                trackDir = tangentialVelocity / tangentialSpeed;
            }
            else if (tangentialAccel.Magnitude > 0d)
            {
                trackDir = tangentialAccel.Normalized;
            }
            else
            {
                return CoRotate(contact, body, terrain, timeSeconds, timeSeconds + dt, omega, axis);
            }

            double drive = Vector3d.Dot(tangentialAccel, trackDir);
            double kineticDecel = body.SurfaceKineticFrictionMu * pressMagnitude;
            double netRate = drive - kineticDecel;
            double stopTime = netRate < 0d && tangentialSpeed > SlipSpeedEpsilon
                ? tangentialSpeed / -netRate
                : double.PositiveInfinity;
            if (stopTime >= dt)
            {
                Vector3d vEnd = trackDir * (tangentialSpeed + (netRate * dt));
                return Slide(contact, tangentialVelocity, vEnd,
                    body, terrain, timeSeconds, timeSeconds + dt, omega, axis);
            }

            double stopMoment = timeSeconds + stopTime;
            Vector3d vAtStop = trackDir * (tangentialSpeed + (netRate * stopTime));
            SurfaceMotionResult atStop = Slide(contact, tangentialVelocity, vAtStop,
                body, terrain, timeSeconds, stopMoment, omega, axis);
            if (atStop.Regime != VesselRegime.Landed)
            {
                return atStop;
            }

            // Силы в момент остановки — заново, не из начала шага: гравитация
            // и тело берутся в stopMoment/stop-точке (нюанс 3).
            body.EvaluateWorldState(stopMoment, out Vector3d stopBodyP, out Vector3d stopBodyV);
            Vector3d stopNormal = terrain.GetOutwardNormal(body, atStop.Position - stopBodyP).Normalized;
            Vector3d stopSurfV = stopBodyV + Vector3d.Cross(omegaVector, atStop.Position - stopBodyP);
            Vector3d stopRel = atStop.Velocity - stopSurfV;
            Vector3d stopTangent = stopRel - (stopNormal * Vector3d.Dot(stopRel, stopNormal));
            Vector3d stopFree = FreeAcceleration(gravity(atStop.Position, stopMoment), omegaVector, atStop.Position - stopBodyP, stopTangent);
            double stopNormalAccel = Vector3d.Dot(stopFree, stopNormal);
            if (stopNormalAccel > ContactEpsilon)
            {
                return new SurfaceMotionResult(atStop.Position, atStop.Velocity, VesselRegime.Flying, false);
            }

            double stopPress = stopNormalAccel < 0d ? -stopNormalAccel : 0d;
            Vector3d stopTangentialAccel = stopFree - (stopNormal * stopNormalAccel);
            if (stopTangentialAccel.Magnitude <= body.SurfaceStaticFrictionMu * stopPress)
            {
                return CoRotate(atStop.Position, body, terrain, stopMoment, timeSeconds + dt, omega, axis);
            }

            double remaining = timeSeconds + dt - stopMoment;
            double netRate2 = stopTangentialAccel.Magnitude - (body.SurfaceKineticFrictionMu * stopPress);
            if (netRate2 <= 0d)
            {
                return CoRotate(atStop.Position, body, terrain, stopMoment, timeSeconds + dt, omega, axis);
            }

            Vector3d dir2 = stopTangentialAccel.Normalized;
            return Slide(atStop.Position, stopTangent, dir2 * (netRate2 * remaining),
                body, terrain, stopMoment, timeSeconds + dt, omega, axis);
        }

        private static Vector3d FreeAcceleration(Vector3d gravity, Vector3d omegaVector, Vector3d relativePosition, Vector3d tangentialVelocity)
        {
            return gravity
                - Vector3d.Cross(omegaVector, Vector3d.Cross(omegaVector, relativePosition))
                - (Vector3d.Cross(omegaVector, tangentialVelocity) * 2d);
        }

        private static Vector3d ProjectToSurface(OrbitingBody body, ITerrainModel terrain, Vector3d bodyPosition, Vector3d position, double timeSeconds)
        {
            Vector3d contact = position;
            for (int i = 0; i < 2; i++)
            {
                Vector3d relative = contact - bodyPosition;
                Vector3d direction = relative.Normalized;
                body.SurfaceLatLonAt(contact, timeSeconds, out double latDeg, out double lonDeg);
                double targetRadius = body.Radius + terrain.GetHeightMeters(body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d));
                contact = bodyPosition + (direction * targetRadius);
            }

            return contact;
        }

        /// <summary>
        /// Совместное вращение покоящейся точки с телом: точный поворот Родригеса
        /// оффсета на ω·Δt вокруг оси + перенос за орбитальным движением тела.
        /// При ω·Δt == 0 возвращает вход бит-в-бит (стоянка без дрейфа).
        /// </summary>
        private static SurfaceMotionResult CoRotate(Vector3d contact, OrbitingBody body, ITerrainModel terrain, double fromTime, double toTime, double omega, Vector3d axis)
        {
            double angle = omega * (toTime - fromTime);
            body.EvaluateWorldState(fromTime, out Vector3d fromBodyP, out _);
            body.EvaluateWorldState(toTime, out Vector3d toBodyP, out Vector3d toBodyV);
            Vector3d offset = contact - fromBodyP;
            Vector3d rotated = angle == 0d ? offset : RotateAboutAxis(offset, axis, angle);
            Vector3d endPosition = ProjectToSurface(body, terrain, toBodyP, toBodyP + rotated, toTime);
            Vector3d endVelocity = toBodyV + Vector3d.Cross(axis * omega, endPosition - toBodyP);
            return new SurfaceMotionResult(endPosition, endVelocity, VesselRegime.Landed, true);
        }

        /// <summary>
        /// Скольжение за интервал: смещение средней касательной скоростью
        /// (трапеция — точна при постоянном торможении), транспорт поворотом
        /// тела, проекция на контакт, перенос касательной скорости с чисткой
        /// нормальной компоненты в новой точке.
        /// </summary>
        private static SurfaceMotionResult Slide(Vector3d contact, Vector3d tangentStart, Vector3d tangentEnd, OrbitingBody body, ITerrainModel terrain, double fromTime, double toTime, double omega, Vector3d axis)
        {
            double dt = toTime - fromTime;
            body.EvaluateWorldState(fromTime, out Vector3d fromBodyP, out _);
            body.EvaluateWorldState(toTime, out Vector3d toBodyP, out Vector3d toBodyV);
            Vector3d average = (tangentStart + tangentEnd) * 0.5d;
            Vector3d transported = RotateAboutAxis((contact - fromBodyP) + (average * dt), axis, omega * dt);
            Vector3d endPosition = ProjectToSurface(body, terrain, toBodyP, toBodyP + transported, toTime);
            Vector3d endNormal = terrain.GetOutwardNormal(body, endPosition - toBodyP).Normalized;
            Vector3d rotatedEnd = RotateAboutAxis(tangentEnd, axis, omega * dt);
            Vector3d endTangent = rotatedEnd - (endNormal * Vector3d.Dot(rotatedEnd, endNormal));
            Vector3d endVelocity = toBodyV + Vector3d.Cross(axis * omega, endPosition - toBodyP) + endTangent;
            return new SurfaceMotionResult(endPosition, endVelocity, VesselRegime.Landed, false);
        }

        private static Vector3d RotateAboutAxis(Vector3d v, Vector3d axis, double angle)
        {
            if (angle == 0d)
            {
                return v;
            }

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            Vector3d axisCross = Vector3d.Cross(axis, v);
            return (v * cos) + (axisCross * sin) + (axis * (Vector3d.Dot(axis, v) * (1d - cos)));
        }
    }
}
