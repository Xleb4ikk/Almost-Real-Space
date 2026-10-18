using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Mirrors the vessel visuals onto the contact zone bodies — at the VERY END
    /// of the frame (order 900, after ContactZoneHost at −50). A separate
    /// component is required because SpacecraftBuilder runs at order −110 (its
    /// Update must precede the runner step), and the bodies must be read only
    /// after the zone has moved the frame root.
    /// </summary>
    [DefaultExecutionOrder(900)]
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
