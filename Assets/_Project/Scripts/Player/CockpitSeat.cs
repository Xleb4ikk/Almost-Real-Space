using Galilego.Tablet;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Кресло пилота/оператора: E — сесть (игрок фиксируется у якоря, ходьба
    /// замирает; если у станции есть монитор-планшет, он открывается — курсор
    /// свободен, кнопки жмутся мышью). E/Esc на открытом мониторе — закрыть
    /// его и встать; E по креслу и E в кабине сидя — встать.
    /// </summary>
    public sealed class CockpitSeat : Interactable
    {
        [Tooltip("Пустой дочерний GO — позиция ГЛАЗ сидящего. Местный поворот " +
            "якоря держите нулевым: местная +Z — это «вверх» кресла (ноги вниз).")]
        public Transform Anchor;

        [Tooltip("Монитор станции (планшет). Пусто — посадка без открытия панели.")]
        public ShipTablet Tablet;

        [Tooltip("Высота глаз сидящего над точкой опоры (м): ноги — под якорем.")]
        public float EyeHeightMeters = 0.85f;

        [Tooltip("Куда встать: смещение от ног под якорем, м, в местных осях кресла " +
            "(+X — от консоли в проход).")]
        public Vector3 StandOffsetMeters = new Vector3(1.3f, 0f, 0f);

        private PlayerController user;
        private FirstPersonCamera cam;
        private bool camWasLocked;

        public override string Prompt => "Сесть в кресло (E)";

        public override void Interact(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            if (player.Seated)
            {
                player.StandUp();
                return;
            }

            if (!player.EnterSeat(this))
            {
                return;
            }

            // Сессия монитора станции: как у TabletInteractable — ввод игрока
            // заблокирован, курсор свободен (мышь жмёт кнопки панели).
            if (Tablet != null && !Tablet.Expanded)
            {
                user = player;
                user.InputBlocked = true;

                cam = Object.FindAnyObjectByType<FirstPersonCamera>();
                if (cam != null)
                {
                    camWasLocked = cam.LockCursor;
                    cam.LockCursor = false;
                }

                Tablet.Closed += OnTabletClosed;
                Tablet.Open();
            }
        }

        private void OnTabletClosed()
        {
            Tablet.Closed -= OnTabletClosed;
            if (user != null)
            {
                user.InputBlocked = false;
                user.StandUp();
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
