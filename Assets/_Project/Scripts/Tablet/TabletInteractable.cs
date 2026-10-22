using Galilego.Universe;
using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// "Wall tablet": the player aims at it (ray ≤ 4 m, the GameObject needs a
    /// collider) and presses E to expand it. While it is open, player input is
    /// blocked (PlayerController.InputBlocked) and the camera does not grab the cursor.
    /// </summary>
    public sealed class TabletInteractable : Interactable
    {
        public ShipTablet Tablet;

        private PlayerController user;
        private bool cursorClaimed;

        public override string Prompt
        {
            get { return "Открыть планшет (E)"; }
        }

        public override void Interact(PlayerController player)
        {
            if (Tablet == null || Tablet.Expanded)
            {
                return;
            }

            user = player;
            if (user != null)
            {
                user.InputBlocked = true;
            }

            // The expanded tablet is clicked with the mouse: CLAIM the cursor for it
            // (the camera yields it), release on close — the camera re-captures by
            // itself. Counted claims cannot get stuck released.
            FirstPersonCamera.SetCursorClaim(ref cursorClaimed, true, "TabletInteractable");
            Tablet.Closed += OnClosed;
            Tablet.Open();
        }

        private void OnClosed()
        {
            Tablet.Closed -= OnClosed;
            if (user != null)
            {
                user.InputBlocked = false;
            }

            FirstPersonCamera.SetCursorClaim(ref cursorClaimed, false, "TabletInteractable");
            user = null;
        }

        private void OnDisable()
        {
            // A disabled/destroyed interactable must not keep the cursor claimed
            // (the tablet may be destroyed while expanded, with no Closed event).
            if (Tablet != null)
            {
                Tablet.Closed -= OnClosed;
            }

            if (user != null)
            {
                user.InputBlocked = false;
            }

            FirstPersonCamera.SetCursorClaim(ref cursorClaimed, false, "TabletInteractable");
            user = null;
        }
    }
}
