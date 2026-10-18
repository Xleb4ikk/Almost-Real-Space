using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// One source's contribution to the ship's ODE right-hand side. The
    /// SpecificAcceleration/Force split keeps units clear: gravity does not
    /// depend on ship mass and returns a ready acceleration (m/s²), while
    /// thrust/drag depend on mass and return a force (N) — division by mass
    /// happens once, in one place (SpacecraftPhysics), not scattered across
    /// every source.
    /// Torque (N·m, body frame) is summed alongside force in
    /// SpacecraftPhysics.EvaluateDerivative and exposed via the TotalTorqueBody
    /// snapshot (rotation control with step seeding); it is not integrated into
    /// the rotation derivative itself — rotation is handled by the AttitudePhysics
    /// stepper (see its doc). RCS uses the full Evaluate (force+torque).
    /// </summary>
    public readonly struct DynamicsContribution
    {
        public readonly Vector3d SpecificAcceleration; // m/s²
        public readonly Vector3d Force; // N
        public readonly double MassFlow; // kg/s, positive value = consumption
        public readonly Vector3d Torque; // N·m, body frame

        public DynamicsContribution(Vector3d specificAcceleration, Vector3d force, double massFlow, Vector3d torque)
        {
            SpecificAcceleration = specificAcceleration;
            Force = force;
            MassFlow = massFlow;
            Torque = torque;
        }

        public static DynamicsContribution FromSpecificAcceleration(Vector3d specificAcceleration) =>
            new DynamicsContribution(specificAcceleration, Vector3d.Zero, 0d, Vector3d.Zero);

        public static DynamicsContribution operator +(DynamicsContribution a, DynamicsContribution b) =>
            new DynamicsContribution(
                a.SpecificAcceleration + b.SpecificAcceleration,
                a.Force + b.Force,
                a.MassFlow + b.MassFlow,
                a.Torque + b.Torque);
    }
}
