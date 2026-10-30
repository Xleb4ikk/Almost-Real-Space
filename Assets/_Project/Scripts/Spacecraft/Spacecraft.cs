using Galilego.Core;
using Galilego.Universe;

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

        // Local-frame carry-over between Step() calls. Absolute Position/Velocity are
        // the public contract; the local copy removes the per-chunk absolute->local
        // conversion that otherwise loses ulp(1.5e11) = 3e-5 m on every chunk.
        // Invalidated whenever the absolute state is changed outside the integrator
        // (Position/Velocity writes, event commits, frame switches).
        internal Vector3d LocalPosition;
        internal Vector3d LocalVelocity;
        internal Vector3d LocalAbsolutePosition;
        internal Vector3d LocalAbsoluteVelocity;
        internal OrbitingBody LocalBody;
        internal double LocalTimeSeconds;
        internal bool HasLocalState;

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
