using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// One physics pose sync per frame, after every LateUpdate writer (BodyView −100,
    /// SurfaceSite −90, ContactZoneHost −50, ShipView −49, VesselMirror −48) and before
    /// the readers (camera −40) and before the next Update's player queries.
    /// The project runs with Physics.autoSyncTransforms = false.
    /// </summary>
    [DefaultExecutionOrder(-41)]
    public sealed class PoseSync : MonoBehaviour
    {
        private void LateUpdate()
        {
            Physics.SyncTransforms();
        }
    }
}
