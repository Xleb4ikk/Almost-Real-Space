using System;
using System.Collections.Generic;

namespace Galilego.Core
{
    public static class PhysicsSolver
    {
        public const double GravitationalConstant = 6.67430e-11d;

        public static double MassToStandardGravitationalParameter(double mass)
        {
            return GravitationalConstant * mass;
        }

        public static double StandardGravitationalParameterToMass(double standardGravitationalParameter)
        {
            return standardGravitationalParameter / GravitationalConstant;
        }

        public static Vector3d CalculateAcceleration(Vector3d currentPos, Vector3d anchorPos, double anchorMass)
        {
            return CalculateAccelerationFromStandardGravitationalParameter(
                currentPos,
                anchorPos,
                MassToStandardGravitationalParameter(anchorMass));
        }

        /// <summary>
        /// Чисто численный пол против деления на ноль в пробных RK-подстадиях.
        /// Без физического смысла: событие касания обязано остановить корабль
        /// раньше, этот пол — последняя страховка от Inf/NaN, а не модель.
        /// </summary>
        public const double NumericalFloorSqrDistance = 1e-6d;

        /// <summary>
        /// Геометрический предикат «внутри тела»: sqrDistance &lt; radius².
        /// Отдельно от численного пола выше — не подменяет ускорение нулём,
        /// а заводит AltitudeCrossingDetector (Falling/Touchdown).
        /// </summary>
        public static bool IsInsideBodySqr(double sqrDistance, double bodyRadius)
        {
            if (bodyRadius <= 0d)
            {
                return false;
            }

            return sqrDistance < bodyRadius * bodyRadius;
        }

        /// <summary>
        /// Вычисляет гравитационное ускорение от одного тела.
        ///
        /// NOTE: Математика должна быть идентична AccelerationEvaluator.BodyGravity
        /// (double3 версия для Burst, отсутствует в этом репо — паритет автоматически
        /// не проверяется, см. T34). Формула, которую обязаны разделять обе стороны:
        /// - NumericalFloorSqrDistance = 1e-6 м² (только защита от r=0)
        /// - a = (bodyPos - shipPos) * (μ / r³)
        /// </summary>
        public static Vector3d CalculateAccelerationFromStandardGravitationalParameter(
            Vector3d currentPos,
            Vector3d anchorPos,
            double standardGravitationalParameter)
        {
            if (standardGravitationalParameter == 0d)
            {
                return Vector3d.Zero;
            }

            Vector3d offset = anchorPos - currentPos;
            double sqrDistance = offset.SqrMagnitude;

            if (sqrDistance < NumericalFloorSqrDistance)
            {
                return Vector3d.Zero;
            }

            double inverseDistance = 1d / Math.Sqrt(sqrDistance);
            double inverseDistanceCubed = inverseDistance / sqrDistance;
            double accelerationScale = standardGravitationalParameter * inverseDistanceCubed;

            return offset * accelerationScale;
        }

        /// <summary>
        /// Вариант для планировщика/превью (Lambert/porkchop), где корабля нет
        /// и событию неоткуда сработать. false = точка внутри тела (по radius),
        /// кандидат трассы помечается invalid, а не тихо зануляется.
        /// Конечность гарантируется: ниже численного пола тоже false.
        /// </summary>
        public static bool TryCalculateAccelerationFromStandardGravitationalParameter(
            Vector3d currentPos,
            Vector3d anchorPos,
            double standardGravitationalParameter,
            double bodyRadius,
            out Vector3d acceleration)
        {
            Vector3d offset = anchorPos - currentPos;
            double sqrDistance = offset.SqrMagnitude;

            if (sqrDistance < NumericalFloorSqrDistance || IsInsideBodySqr(sqrDistance, bodyRadius))
            {
                acceleration = Vector3d.Zero;
                return false;
            }

            if (standardGravitationalParameter == 0d)
            {
                acceleration = Vector3d.Zero;
                return true;
            }

            double inverseDistance = 1d / Math.Sqrt(sqrDistance);
            double inverseDistanceCubed = inverseDistance / sqrDistance;
            acceleration = offset * (standardGravitationalParameter * inverseDistanceCubed);
            return acceleration.IsFinite;
        }

        [Obsolete("Legacy B9 cluster: gravity now flows through StarSystem.EvaluateShipAcceleration into SpacecraftPhysics. Chain: StepShip -> OrbitIntegrator -> RK4/CelestialBody.")]
#pragma warning disable 0618 // Legacy B9 cluster: internal delegation within obsolete surface.
        public static Vector3d CalculateAcceleration(Vector3d currentPos, List<CelestialBody> anchors)
        {
            if (anchors == null)
            {
                throw new ArgumentNullException(nameof(anchors));
            }

            Vector3d totalAcceleration = Vector3d.Zero;

            for (int i = 0; i < anchors.Count; i++)
            {
                CelestialBody anchor = anchors[i];

                if (anchor == null || anchor.Mass == 0d)
                {
                    continue;
                }

                totalAcceleration += CalculateAccelerationFromStandardGravitationalParameter(
                    currentPos,
                    anchor.Position,
                    anchor.StandardGravitationalParameter);
            }

            return totalAcceleration;
        }
#pragma warning restore 0618

        [Obsolete("Legacy B9 cluster: use SpacecraftPhysics (SpacecraftOrbitIntegrator). Chain: StepShip -> OrbitIntegrator -> RK4/CelestialBody.")]
#pragma warning disable 0618 // Legacy B9 cluster: internal delegation within obsolete surface.
        public static IntegrationResult IntegrateRK4(CelestialBody currentBody, List<CelestialBody> anchors, double dt)
        {
            if (currentBody == null)
            {
                throw new ArgumentNullException(nameof(currentBody));
            }

            if (anchors == null)
            {
                throw new ArgumentNullException(nameof(anchors));
            }

            Vector3d position = currentBody.Position;
            Vector3d velocity = currentBody.Velocity;
            double halfDt = dt * 0.5d;
            double sixthDt = dt / 6d;

            Vector3d k1Position = velocity;
            Vector3d k1Velocity = CalculateAcceleration(position, anchors);

            Vector3d k2Position = velocity + (k1Velocity * halfDt);
            Vector3d k2Velocity = CalculateAcceleration(position + (k1Position * halfDt), anchors);

            Vector3d k3Position = velocity + (k2Velocity * halfDt);
            Vector3d k3Velocity = CalculateAcceleration(position + (k2Position * halfDt), anchors);

            Vector3d k4Position = velocity + (k3Velocity * dt);
            Vector3d k4Velocity = CalculateAcceleration(position + (k3Position * dt), anchors);

            Vector3d newPosition = position + ((k1Position + (2d * k2Position) + (2d * k3Position) + k4Position) * sixthDt);
            Vector3d newVelocity = velocity + ((k1Velocity + (2d * k2Velocity) + (2d * k3Velocity) + k4Velocity) * sixthDt);

            return new IntegrationResult(newPosition, newVelocity);
        }
#pragma warning restore 0618

        [Obsolete("Legacy B9 cluster: use SpacecraftPhysics (SpacecraftOrbitIntegrator). Chain: StepShip -> OrbitIntegrator -> RK4/CelestialBody.")]
#pragma warning disable 0618 // Legacy B9 cluster: internal delegation within obsolete surface.
        public static IntegrationResult RK4(CelestialBody currentBody, List<CelestialBody> anchors, double dt)
        {
            return IntegrateRK4(currentBody, anchors, dt);
        }
#pragma warning restore 0618

        [Obsolete("Legacy B9 cluster: use SpacecraftPhysics (SpacecraftOrbitIntegrator). Chain: StepShip -> OrbitIntegrator -> RK4/CelestialBody.")]
#pragma warning disable 0618 // Legacy B9 cluster: internal delegation within obsolete surface.
        public static IntegrationResult RK4(
            CelestialBody currentBody,
            double currentTimeSeconds,
            double dt,
            Func<Vector3d, double, Vector3d> accelerationProvider)
        {
            if (currentBody == null)
            {
                throw new ArgumentNullException(nameof(currentBody));
            }

            return RK4(currentBody.Position, currentBody.Velocity, currentTimeSeconds, dt, accelerationProvider);
        }
#pragma warning restore 0618

        [Obsolete("Legacy B9 cluster: use SpacecraftPhysics (SpacecraftOrbitIntegrator). Chain: StepShip -> OrbitIntegrator -> RK4/CelestialBody.")]
        public static IntegrationResult RK4(
            Vector3d position,
            Vector3d velocity,
            double currentTimeSeconds,
            double dt,
            Func<Vector3d, double, Vector3d> accelerationProvider)
        {
            if (accelerationProvider == null)
            {
                throw new ArgumentNullException(nameof(accelerationProvider));
            }

            double halfDt = dt * 0.5d;
            double sixthDt = dt / 6d;

            Vector3d k1Position = velocity;
            Vector3d k1Velocity = accelerationProvider(position, currentTimeSeconds);

            Vector3d k2Position = velocity + (k1Velocity * halfDt);
            Vector3d k2Velocity = accelerationProvider(position + (k1Position * halfDt), currentTimeSeconds + halfDt);

            Vector3d k3Position = velocity + (k2Velocity * halfDt);
            Vector3d k3Velocity = accelerationProvider(position + (k2Position * halfDt), currentTimeSeconds + halfDt);

            Vector3d k4Position = velocity + (k3Velocity * dt);
            Vector3d k4Velocity = accelerationProvider(position + (k3Position * dt), currentTimeSeconds + dt);

            Vector3d newPosition = position + ((k1Position + (2d * k2Position) + (2d * k3Position) + k4Position) * sixthDt);
            Vector3d newVelocity = velocity + ((k1Velocity + (2d * k2Velocity) + (2d * k3Velocity) + k4Velocity) * sixthDt);

            return new IntegrationResult(newPosition, newVelocity);
        }
    }
}
