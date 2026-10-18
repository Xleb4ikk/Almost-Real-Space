using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Torque source for AttitudePhysics. Separate from IDynamicsSource on purpose:
    /// a force needs no torque and rotation needs no force; IDynamicsSource has no
    /// attitude channel (Evaluate without attitude), so RCS force goes only through
    /// EvaluateFull below. TODO: extend IDynamicsSource with an attitude channel
    /// when aerodynamic torque is needed.
    /// </summary>
    public interface ITorqueSource
    {
        Vector3d EvaluateTorque(QuaternionD attitude, Vector3d angularVelocityBody, double timeSeconds);
    }

    /// <summary>
    /// A single RCS thruster: application point and direction in the body frame;
    /// the game loop (or a test) toggles Active. A class, not a struct: Active
    /// mutates; no per-tick allocations (pooled at start).
    /// </summary>
    public sealed class RcsThruster
    {
        public Vector3d Position;
        public Vector3d Direction;
        public double ThrustNewtons;
        public bool Active;

        public RcsThruster(Vector3d position, Vector3d direction, double thrustNewtons)
        {
            // A zero/degenerate direction would silently contribute nothing (Normalized
            // of zero is zero). Reject loudly here, at configuration time, rather than
            // on the hot Evaluate path.
            if (!(direction.SqrMagnitude > 0d) || !direction.IsFinite)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(direction), "Направление дюза обязано быть конечным ненулевым вектором, получено " + direction + ".");
            }

            Position = position;
            Direction = direction;
            ThrustNewtons = thrustNewtons;
            Active = false;
        }
    }

    /// <summary>
    /// RCS block: sum of torques of active thrusters τ = Σ r×F (body frame).
    /// Full channel via IDynamicsSource: set AttitudeProvider and add the block
    /// to SpacecraftPhysics.Sources — force (world frame) goes into the orbital
    /// RHS, torque (body frame) into the SpacecraftPhysics.TotalTorqueBody
    /// snapshot, which rotation reads. So force+torque come from one source.
    /// </summary>
    public sealed class RcsBlock : ITorqueSource, IDynamicsSource
    {
        public readonly List<RcsThruster> Thrusters = new List<RcsThruster>();

        /// <summary>
        /// Attitude body→world for the full channel. null → Evaluate fails loudly:
        /// a silent zero RCS force is a silent loss of control.
        /// </summary>
        public Func<QuaternionD> AttitudeProvider;

        public Vector3d EvaluateTorque(QuaternionD attitude, Vector3d angularVelocityBody, double timeSeconds)
        {
            Vector3d total = Vector3d.Zero;
            for (int i = 0; i < Thrusters.Count; i++)
            {
                RcsThruster thruster = Thrusters[i];
                if (thruster == null || !thruster.Active)
                {
                    continue;
                }

                total += Vector3d.Cross(thruster.Position, thruster.Direction.Normalized * thruster.ThrustNewtons);
            }

            return total;
        }

        public DynamicsContribution EvaluateFull(QuaternionD attitude, Vector3d angularVelocityBody, double timeSeconds)
        {
            Vector3d bodyForce = Vector3d.Zero;
            for (int i = 0; i < Thrusters.Count; i++)
            {
                RcsThruster thruster = Thrusters[i];
                if (thruster == null || !thruster.Active)
                {
                    continue;
                }

                bodyForce += thruster.Direction.Normalized * thruster.ThrustNewtons;
            }

            return new DynamicsContribution(
                Vector3d.Zero,
                attitude.Normalized.Rotate(bodyForce),
                0d,
                EvaluateTorque(attitude, angularVelocityBody, timeSeconds));
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            if (AttitudeProvider == null)
            {
                throw new InvalidOperationException(
                    "RcsBlock в орбитальном пути без AttitudeProvider: сила и момент остались бы нулями молча. Задайте провайдер ориентации (снимок из AttitudePhysics) или уберите блок из Sources.");
            }

            // Angular velocity is not used in the torque channel (RCS τ is independent of ω).
            return EvaluateFull(AttitudeProvider(), Vector3d.Zero, timeSeconds);
        }
    }
}
