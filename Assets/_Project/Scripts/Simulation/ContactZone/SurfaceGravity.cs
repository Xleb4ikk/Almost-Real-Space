using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// The ONE definition of "weight" on a body's surface, shared by the contact zone (PhysX frame gravity)
    /// and the walking player. Effective acceleration of a site frame, in its axes (east, north, up):
    /// total gravity at the origin (ALL bodies as point masses + the root-frame indirect term; no J2, see below)
    /// − acceleration of the frame origin (the planet's own acceleration) − centrifugal term of the planet's spin.
    ///
    /// Why all bodies: the planet's acceleration (finite difference of its velocity) contains the star's pull
    /// (~6e-3 m/s² at 1 AU). Subtracting it from the planet's gravity alone (as before) left a spurious uniform
    /// "away from the star" acceleration of that size; with the star's own pull included only the true tidal
    /// term (~5e-7 m/s²) remains. Coriolis is not modeled (order ω·v ≈ 1e-3 m/s² on landing).
    /// </summary>
    public static class SurfaceGravity
    {
        /// <summary>Half-width of the central difference used for the origin's acceleration (s).</summary>
        private const double DifferenceHalfWidthSeconds = 0.5d;

        public static Vector3d EffectiveLocal(StarSystem system, OrbitingBody body, SiteFrame f, double t)
        {
            Vector3d origin = f.Origin;
            body.EvaluateWorldState(t, out Vector3d bodyPosition, out _);
            body.EvaluateWorldState(t - DifferenceHalfWidthSeconds, out _, out Vector3d velocityBefore);
            body.EvaluateWorldState(t + DifferenceHalfWidthSeconds, out _, out Vector3d velocityAfter);
            Vector3d frameAcceleration = (velocityAfter - velocityBefore) * (1d / (2d * DifferenceHalfWidthSeconds));

            // Point masses (no J2): the ground is a sphere, and J2 over a sphere flips the equator/pole sign of weight.
            Vector3d gravity = system.EvaluateShipAccelerationCached(origin, t, includeOblateness: false);

            Vector3d w = f.AngularVelocity;
            Vector3d centrifugal = Vector3d.Cross(w, Vector3d.Cross(w, origin - bodyPosition));

            Vector3d effective = gravity - frameAcceleration - centrifugal;
            return new Vector3d(Vector3d.Dot(effective, f.East), Vector3d.Dot(effective, f.North), Vector3d.Dot(effective, f.Up));
        }
    }
}
