using Galilego.Universe;
using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// «Планшет на стене»: игрок наводится (луч ≤ 4 м, нужен коллайдер на GO) и
    /// жмёт E — планшет разворачивается. Пока он открыт, ввод игрока заблокирован
    /// (PlayerController.InputBlocked), камера не перехватывает курсор.
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
