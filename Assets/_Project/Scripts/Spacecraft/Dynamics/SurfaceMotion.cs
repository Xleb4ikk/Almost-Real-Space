using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// Vessel regime. Transitions (explicit state machine, owned by the game loop):
    /// Flying + Touchdown(soft) → Landed (EventReactions, projection+v_t);
    /// Flying + Touchdown(hard) → Destroyed (+PartSeparationSpecs);
    /// Landed + loss of contact → Flying (here, Liftoff);
    /// Landed + LiftoffHook (thrust/Δv) → Flying (hook, trigger later).
    /// The event driver does not take part in regimes: it only detects.
    /// </summary>
    public enum VesselRegime
    {
        Flying,
        Landed,
        Destroyed
    }

    /// <summary>
    /// Result of a surface step: projected world state, regime (Landed, or Flying
    /// on liftoff), at-rest flag.
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
    /// Motion of a landed vessel: constrained dynamics on the surface.
    /// State is Cartesian (world position/velocity); angles are NOT integrated:
    /// poles do not exist as a class; lat/lon are obtained only for reports via
    /// SurfaceLatLonAt. First-order scheme in dt (game steps ≤0.5 s): exact for
    /// constant fields, O(dt²) locally for varying ones.
    ///
    /// Free acceleration in the surface frame (consistent: v_t is strictly
    /// surface-relative, otherwise the Coriolis term is wrong):
    ///   a_free = g − ω×(ω×r) − 2ω×v_t.
    /// The centrifugal term −ω×(ω×r) points OUTWARD from the axis: it is what
    /// allows liftoff under fast spin (ω²R &gt; |g|).
    /// Contact holds ⟺ a_free·n &lt; 0 (pressed in). Deterministic boundary branch:
    /// a_free·n &gt; ContactEpsilon → liftoff (Liftoff without changing state; the
    /// game integrator continues from there); |band| ≤ eps → contact holds (no
    /// stick/slip flicker from fp). Contact is checked only at the start of the
    /// step: an intra-step transition is resolved by the next step (delay ≤ dt).
    /// Stick: |a_t| ≤ μ_s·|a_n| → co-rotation by an exact Rodrigues rotation.
    /// Slip: deceleration μ_k·|a_n| opposing motion; t_stop = |v_t|/(μ_k·|a_n|);
    /// if t_stop &lt; dt — forces and the static condition are recomputed AT THE
    /// STOP MOMENT (one extra gravity and body-state call), not at the start.
    /// FreeAcceleration here is WITHOUT thrust: thrust is added only on the
    /// LiftoffHook path (EventReactions.TryLiftoff, same a·n &gt; eps criterion) —
    /// the step's constrained dynamics computes press-in from gravity/spin.
    /// </summary>
    public static class SurfaceMotion
    {
        public const double ContactEpsilon = 1e-9d;

        /// <summary>
        /// "Tangential speed is zero" threshold for choosing stick. Must sit ABOVE the
        /// fp noise of subtracting absolute velocities: relativeVelocity =
        /// velocity − surfaceVelocity with operands ~3e4 m/s (the planet's orbital
        /// speed) gives noise up to ~1e-10 m/s (ulp(3e4)≈3.6e-12 per operation).
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
            Vector3d normal = terrain.GetOutwardNormal(body, contact - bodyPosition, timeSeconds).Normalized;
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

            // Kinetic behaviour along the track: s=|vT|, drive is the projection of
            // the driving aT onto the motion direction (onto aT when starting from
            // rest), decel=μk|an|. Speed along the track: ds/dt = drive − decel.
            // Assumes μk ≤ μs (otherwise static and kinetic friction contradict —
            // the branches below stay deterministic, but the configuration physics
            // is meaningless).
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

            // Forces at the moment of stopping are recomputed, not taken from the
            // start of the step: gravity and the body are sampled at stopMoment.
            body.EvaluateWorldState(stopMoment, out Vector3d stopBodyP, out Vector3d stopBodyV);
            Vector3d stopNormal = terrain.GetOutwardNormal(body, atStop.Position - stopBodyP, stopMoment).Normalized;
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
        /// Co-rotation of a resting point with the body: exact Rodrigues rotation of
        /// the offset by ω·Δt about the axis, plus translation by the body's orbital
        /// motion. At ω·Δt == 0 returns the input bit-for-bit (rest without drift).
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
        /// Sliding over an interval: displacement by the average tangential velocity
        /// (trapezoid — exact under constant deceleration), transport by the body's
        /// rotation, projection onto the contact, and transfer of tangential velocity
        /// with the normal component removed at the new point.
        /// </summary>
        private static SurfaceMotionResult Slide(Vector3d contact, Vector3d tangentStart, Vector3d tangentEnd, OrbitingBody body, ITerrainModel terrain, double fromTime, double toTime, double omega, Vector3d axis)
        {
            double dt = toTime - fromTime;
            body.EvaluateWorldState(fromTime, out Vector3d fromBodyP, out _);
            body.EvaluateWorldState(toTime, out Vector3d toBodyP, out Vector3d toBodyV);
            Vector3d average = (tangentStart + tangentEnd) * 0.5d;
            Vector3d transported = RotateAboutAxis((contact - fromBodyP) + (average * dt), axis, omega * dt);
            Vector3d endPosition = ProjectToSurface(body, terrain, toBodyP, toBodyP + transported, toTime);
            Vector3d endNormal = terrain.GetOutwardNormal(body, endPosition - toBodyP, toTime).Normalized;
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
