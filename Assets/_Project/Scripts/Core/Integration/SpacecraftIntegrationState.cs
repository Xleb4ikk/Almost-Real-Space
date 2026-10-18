using System;

namespace Galilego.Core
{
    /// <summary>
    /// Full spacecraft state for integration: position, velocity, mass.
    /// Mass is part of the state so that fuel consumption (dMass/dt = -mass flow)
    /// evolves synchronously with position/velocity in every RK substage; otherwise
    /// acceleration (force/mass) would use a stale mass and the method's formal
    /// order of accuracy would be lost. The operators below are exactly what DOPRI5
    /// needs for linear stage combinations.
    /// </summary>
    [Serializable]
    public struct SpacecraftIntegrationState
    {
        public Vector3d Position;
        public Vector3d Velocity;
        public double Mass;

        /// <summary>All components are finite (NaN/Inf in the state means integration corruption).</summary>
        public bool IsFinite =>
            Position.IsFinite && Velocity.IsFinite && !double.IsNaN(Mass) && !double.IsInfinity(Mass);

        public static readonly SpacecraftIntegrationState Zero =
            new SpacecraftIntegrationState { Position = Vector3d.Zero, Velocity = Vector3d.Zero, Mass = 0d };

        public static SpacecraftIntegrationState operator +(SpacecraftIntegrationState a, SpacecraftIntegrationState b) =>
            new SpacecraftIntegrationState
            {
                Position = a.Position + b.Position,
                Velocity = a.Velocity + b.Velocity,
                Mass = a.Mass + b.Mass
            };

        public static SpacecraftIntegrationState operator -(SpacecraftIntegrationState a, SpacecraftIntegrationState b) =>
            new SpacecraftIntegrationState
            {
                Position = a.Position - b.Position,
                Velocity = a.Velocity - b.Velocity,
                Mass = a.Mass - b.Mass
            };

        public static SpacecraftIntegrationState operator *(SpacecraftIntegrationState s, double scalar) =>
            new SpacecraftIntegrationState { Position = s.Position * scalar, Velocity = s.Velocity * scalar, Mass = s.Mass * scalar };

        public static SpacecraftIntegrationState operator *(double scalar, SpacecraftIntegrationState s) => s * scalar;
    }
}
