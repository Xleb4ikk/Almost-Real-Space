using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Ship state. Unlike Core.CelestialBody, the ship mass changes as fuel is burned,
    /// so this is a separate lightweight class with mutable state.
    /// </summary>
    public sealed class Spacecraft
    {
        public Vector3d Position;
        public Vector3d Velocity;
        public double Mass;

        /// <summary>body→world orientation and angular velocity in ship axes (6-DOF flight).</summary>
        public QuaternionD Attitude = QuaternionD.Identity;
        public Vector3d AngularVelocity;

        /// <summary>true — Position is the center of mass (after leaving the contact zone), false — the bottom of the craft.</summary>
        public bool PositionIsCom;

        public Spacecraft(Vector3d position, Vector3d velocity, double mass)
        {
            Position = position;
            Velocity = velocity;
            Mass = mass;
        }
    }
}
