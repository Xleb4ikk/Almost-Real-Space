using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Player rendering (floating origin, like ShipView). The player GO is the
    /// camera target (FirstPersonCamera.Target): in EVA/OnSurface a position from
    /// the double world relative to the dominant body; in the ship the ship point
    /// plus a local offset (walking); seated: the FEET at the drawn seat anchor
    /// (render world). Called from LateUpdate AFTER ShipView (ShipTransform must be fresh).
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-80)]
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
            PlaceFree(Runner.PlayerPosition);
        }

        private void PlaceFree(Vector3d astroPosition)
        {
            // Floating origin: both the player and the bodies use one anchor (FloatingOrigin.Anchor).
            transform.position = FloatingOrigin.ToRender(astroPosition);
        }
    }
}
