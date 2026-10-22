using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Ship render (B3). The ship lives in the core's double world; the Unity position
    /// is relative to the floating origin. Must run AFTER BodyView.LateUpdate in the
    /// same frame (script order).
    ///
    /// Order −49: AFTER the contact zone (−50), which writes Ship.Position from the
    /// PhysX bodies, and BEFORE the mirror (−48), which puts the part visuals into the
    /// world poses. The part visuals are children of this GameObject, so a stale ship
    /// position here drags every part (and the seat anchor) by one frame of orbital
    /// motion — hundreds of metres at 5 km/s. The zone writes the position in its
    /// LateUpdate, so an earlier order reads the PREVIOUS frame's value.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-49)]
    public sealed class ShipView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        private void LateUpdate()
        {
            if (Runner == null || Runner.Ship == null)
            {
                return;
            }

            // Free flight: the ship point is the COM, the root is the bottom pivot and
            // the attitude turns the whole craft. Zone/build: the root is the ship
            // point, the pose is owned by the mirror / the builder.
            if (Runner.TryGetFlightVesselRootPose(out Vector3 position, out Quaternion rotation))
            {
                transform.SetPositionAndRotation(position, rotation);
                return;
            }

            // Floating origin: single anchor (FloatingOrigin.Anchor).
            transform.position = FloatingOrigin.ToRender(Runner.Ship.Position);
        }
    }
}
