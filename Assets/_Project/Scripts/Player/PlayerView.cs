using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Рендер игрока (floating origin, как ShipView). GO игрока — цель камеры
    /// (FirstPersonCamera.Target): в EVA/OnSurface — позиция из double-мира
    /// относительно доминантного тела; в корабле — точка корабля + локальный
    /// оффсет (ходьба); сидя — НОГИ у нарисованного якоря кресла (render-мир).
    /// Вызов — LateUpdate ПОСЛЕ ShipView (ShipTransform должен быть свежим).
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

            // Сидя (в ЛЮБОМ режиме): точку игрока ставим у нарисованного якоря
            // кресла. Камера обязана стоять там же, где нарисовано кресло, а не
            // в отдельной точке double-мира: в зоне контакта та едет за физикой
            // (дрожь, лаг сглаживания в десятки метров) — игрок «улетал» вбок
            // и трясся. Render-позиция якоря согласована с кораблём по построению.
            if (Controller != null && Controller.Seated && Controller.SeatedSeat != null
                && Controller.SeatedSeat.Anchor != null)
            {
                CockpitSeat seat = Controller.SeatedSeat;
                Vector3 seatUp = seat.Anchor.TransformDirection(Vector3.forward);

                // Якорь — ГЛАЗА сидящего; точка игрока (как и при ходьбе) — ноги:
                // высоту глаз до якоря доводит камера (FirstPersonCamera).
                transform.position = seat.Anchor.position - (seatUp * seat.EyeHeightMeters);

                // «Вверх» кресла для камеры обновляем каждый кадр: при наклоне
                // корабля статичный снимок уводил смещение глаз в сторону.
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
