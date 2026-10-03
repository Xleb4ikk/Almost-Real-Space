using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Рендер игрока (floating origin, как ShipView). GO игрока — цель камеры
    /// (FirstPersonCamera.Target): в EVA/OnSurface — позиция из double-мира
    /// относительно доминантного тела; в корабле — точка корабля + локальный
    /// оффсет (ходьба) или якорь кресла (сидя). Вызов — LateUpdate ПОСЛЕ
    /// ShipView (ShipTransform должен быть свежим).
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
            if (Controller != null && Controller.Seated && Controller.SeatedSeat.Anchor != null)
            {
                transform.position = Controller.SeatedSeat.Anchor.position;
                return;
            }

            if (Controller != null && Controller.ShipTransform != null)
            {
                transform.position = Controller.ShipTransform.position + Controller.ShipLocalOffset;
                return;
            }

            // Фолбэк: точка корабля через floating origin.
            PlaceFree(Runner.Ship.Position);
        }

        private void PlaceFree()
        {
            PlaceFree(Runner.PlayerPosition);
        }

        private void PlaceFree(Vector3d astroPosition)
        {
            // Floating origin: и игрок, и тела используют один якорь (FloatingOrigin.Anchor).
            transform.position = FloatingOrigin.ToRender(astroPosition);
        }
    }
}
