using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Player rendering (floating origin, like ShipView). The player GO is the
    /// camera target (FirstPersonCamera.Target): in EVA a position from the double
    /// world; on the surface a position from the double world, or the current pose
    /// of the vessel part carrying the player (walking on a ship); in the ship the
    /// ship point plus a local offset; seated: the FEET at the drawn seat anchor
    /// (render world). Called from LateUpdate AFTER ShipView (ShipTransform must be
    /// fresh) and AFTER VesselMirror (−48) in flight: the seat anchor and the
    /// carried point live on the part visuals, so they must already be this frame's
    /// pose — otherwise the camera rides a frame-old anchor.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-42)]
    public sealed class PlayerView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("PlayerController сцены (ходьба внутри/кресло).")]
        public PlayerController Controller;

        private void LateUpdate()
        {
            if (Runner == null || Runner.SystemState == null)
            {
                return;
            }

            // Seated (in ANY mode): the player point is placed at the drawn seat
            // anchor. The camera must sit where the seat is drawn, not at a separate
            // double-world point, which in the contact zone moves with physics. The
            // anchor's render position is consistent with the ship by construction.
            if (Controller != null && Controller.Seated && Controller.SeatedSeat != null
                && Controller.SeatedSeat.Anchor != null)
            {
                CockpitSeat seat = Controller.SeatedSeat;
                Vector3 seatUp = seat.Anchor.TransformDirection(Vector3.forward);

                // The anchor is the seated player's EYES; the player point (as when
                // walking) is the feet: the camera brings the eye height up to the
                // anchor (FirstPersonCamera).
                transform.position = seat.Anchor.position - (seatUp * seat.EyeHeightMeters);

                // The seat's "up" for the camera is refreshed every frame: with a
                // ship tilt a static snapshot would push the eye offset sideways.
                Runner.PlayerSeatedUpRender = seatUp;
                return;
            }

            switch (Runner.PlayerMode)
            {
                case PlayerMode.InShip:
                    PlaceInShip();
                    break;
                default:
                    PlaceFree();
                    break;
            }
        }

        private void PlaceInShip()
        {
            if (Controller != null && Controller.ShipTransform != null)
            {
                transform.position = Controller.ShipTransform.position + Controller.ShipLocalOffset;
                return;
            }

            // Fallback: the ship point through the floating origin.
            PlaceFree(Runner.Ship.Position);
        }

        private void PlaceFree()
        {
            // Carried by a vessel part: ride the pose the cabin is DRAWN at, not the
            // simulation point (that one is a physics frame behind the mirrored visuals
            // and made the view shake around a falling vessel).
            if (Runner.TryGetPlayerRenderPoint(out Vector3 carried))
            {
                transform.position = carried;
                return;
            }

            PlaceFree(Runner.PlayerPosition);
        }

        private void PlaceFree(Vector3d astroPosition)
        {
            // Floating origin: both the player and the bodies use one anchor (FloatingOrigin.Anchor).
            transform.position = FloatingOrigin.ToRender(astroPosition);
        }
    }
}
