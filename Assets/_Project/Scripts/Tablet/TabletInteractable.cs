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
        private FirstPersonCamera cam;
        private bool camWasLocked;

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

            cam = Object.FindAnyObjectByType<FirstPersonCamera>();
            if (cam != null)
            {
                camWasLocked = cam.LockCursor;
                cam.LockCursor = false;
            }

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

            if (cam != null)
            {
                cam.LockCursor = camWasLocked;
            }

            user = null;
            cam = null;
        }
    }
}
