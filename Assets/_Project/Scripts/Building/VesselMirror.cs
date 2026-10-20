using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Mirrors the vessel visuals onto the contact zone bodies. Runs AFTER the
    /// contact zone has moved the frame root (ContactZoneHost, order −50) and
    /// BEFORE the camera (FirstPersonCamera, −40): the seat anchor the player
    /// view reads must be the current one, not last frame's. A separate
    /// component is required because SpacecraftBuilder runs at order −110 (its
    /// Update must precede the runner step), and the bodies must be read only
    /// after the zone has moved the frame root.
    /// </summary>
    [DefaultExecutionOrder(-48)]
    public sealed class VesselMirror : MonoBehaviour
    {
        [Tooltip("Режим строительства: у него берутся детали и тела прокси.")]
        public SpacecraftBuilder Builder;

        private void LateUpdate()
        {
            if (Builder != null)
            {
                Builder.MirrorVisualsToBodies();
            }
        }
    }
}
