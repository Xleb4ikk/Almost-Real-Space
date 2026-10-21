using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Vessel part collider marker: visuals of placed parts (in build mode) and
    /// contact zone proxy bodies (in flight). The player support (VesselSupport)
    /// uses it to treat the collider as part of the ship and not let the player
    /// through. Visual colliders are disabled in flight (otherwise the PhysX proxy
    /// bodies would collide with themselves); proxy body colliders take their place.
    /// </summary>
    public sealed class VesselPartCollider : MonoBehaviour
    {
        /// <summary>
        /// Set on a part's VISUAL colliders in flight when a proxy body represents the part
        /// while the contact zone is active: the player support then ignores the visual
        /// copy (two coincident colliders would double the push-out). Outside the zone the
        /// visual colliders are the only ones and count. Never affects PhysX contacts.
        /// </summary>
        public bool VisualCovered;
    }
}
