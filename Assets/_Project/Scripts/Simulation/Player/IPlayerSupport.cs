using Galilego.Core;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Source of support geometry for the player, in LOCAL meters of the site's
    /// tangent frame (x = east, y = north, z = up; see SiteFrame). Implementations:
    /// analytic terrain (regression), building mesh colliders (Unity), test platform
    /// stub (test rig).
    /// </summary>
    public interface IPlayerSupport
    {
        /// <summary>
        /// The highest support below from within maxDrop. height — support height
        /// along the local vertical, normal — outward surface normal, sourceId —
        /// support id (for transfer by a moving platform).
        /// </summary>
        bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d normal, out int sourceId);

        /// <summary>Ceiling above head within maxRise (height — the ceiling's underside).</summary>
        bool Ceiling(Vector3d head, double maxRise, out double height);

        /// <summary>
        /// Horizontal capsule block (radius radius, height height from from): the
        /// path fraction to first contact and the wall normal. Height is a parameter,
        /// not a constant: a crouched player (smaller capsule) passes under low
        /// openings — the Orion capsule hatch.
        /// </summary>
        bool Sweep(Vector3d from, Vector3d to, double radius, double height, out Vector3d normal, out double fraction);
    }

    /// <summary>
    /// Optional extension: a moving support (platform, ship part). The player is
    /// carried with it at its horizontal velocity.
    /// </summary>
    public interface IMovingSupport
    {
        bool TryGetSupportVelocity(int sourceId, out Vector3d velocity);
    }

    /// <summary>
    /// Optional extension: "the capsule centered at feet, radius radius and height
    /// height overlaps the support". Needed so the player stands up from a crouch
    /// ONLY where the whole capsule fits (a sloped cabin wall is invisible to a
    /// sideways ray).
    /// </summary>
    public interface IOverlapSupport
    {
        bool Overlaps(Vector3d feet, double radius, double height);
    }
}
