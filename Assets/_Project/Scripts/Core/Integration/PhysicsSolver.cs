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
        /// Purely numerical floor against division by zero in trial RK substages.
        /// No physical meaning: the touch-down event must stop the ship earlier;
        /// this floor is the last safeguard against Inf/NaN, not a model.
        /// </summary>
        public const double NumericalFloorSqrDistance = 1e-6d;

        /// <summary>
        /// J2 (oblateness) is applied only closer than this many reference radii: the term falls as
        /// 1/r⁴, so beyond ~50 R it is below 1e-6 of the point-mass pull for Earth-like J2 and is not worth the cost.
        /// </summary>
        public const double J2CutoffRadii = 50d;

        /// <summary>
        /// Acceleration from the second zonal harmonic of a body (equatorial bulge), in vector form
        /// (no basis needed): a = -(3/2)·J2·μ·R²/r⁵ · [ (1 − 5·zc²/r²)·ρ + 2·zc·ẑ ],
        /// ρ = shipPos − bodyPos, zc = ρ·ẑ, ẑ = unit spin axis, R = equatorial reference radius.
        /// Added ON TOP of the point-mass term; returns zero beyond J2CutoffRadii·R.
        /// </summary>
        public static Vector3d CalculateJ2Acceleration(
            Vector3d relativePosition, Vector3d spinAxisUnit, double standardGravitationalParameter, double j2, double referenceRadius)
        {
            if (j2 == 0d || referenceRadius <= 0d || standardGravitationalParameter == 0d)
            {
                return Vector3d.Zero;
            }

            double r2 = relativePosition.SqrMagnitude;
            double cutoff = J2CutoffRadii * referenceRadius;
            if (r2 < NumericalFloorSqrDistance || r2 > cutoff * cutoff)
            {
                return Vector3d.Zero;
            }

            double r = Math.Sqrt(r2);
            double zc = Vector3d.Dot(relativePosition, spinAxisUnit);
            double k = -1.5d * j2 * standardGravitationalParameter * referenceRadius * referenceRadius / (r2 * r2 * r);
            return (relativePosition * ((1d - (5d * zc * zc / r2)) * k)) + (spinAxisUnit * (2d * zc * k));
        }

        /// <summary>
        /// Geometric "inside body" predicate: sqrDistance &lt; radius².
        /// Separate from the numerical floor above — it does not replace acceleration
        /// with zero, it arms AltitudeCrossingDetector (Falling/Touchdown).
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
        /// Computes gravitational acceleration from a single body.
        ///
        /// NOTE: The math must match AccelerationEvaluator.BodyGravity (Burst double3
        /// version, not present in this repo — parity is not verified automatically,
        /// see T34). The formula both sides must share:
        /// - NumericalFloorSqrDistance = 1e-6 m² (only guards r=0)
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
        /// Variant for the planner/preview (Lambert/porkchop), where there is no ship
        /// and no event can fire. false = point inside the body (by radius); the
        /// trajectory candidate is marked invalid instead of silently zeroed.
        /// Finiteness is guaranteed: below the numerical floor it is also false.
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
