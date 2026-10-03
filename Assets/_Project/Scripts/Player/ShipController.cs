using Galilego.Events;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Ввод игрока (B3): варп-лестница и газ. Пишет ТОЛЬКО в SimulationRunner
    /// (Warp.SetWarpFactor / RawThrottle) — физика исполняется в его Update.
    /// Клавиши: 1..7 — ступени лестницы [1, 5, 10, 100, 500, 1000, 3000],
    /// LeftShift — газ вверх, LeftControl — вниз, P — пауза.
    /// </summary>
    public sealed class ShipController : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Шаг газа за нажатие.")]
        [Range(0.01f, 1f)]
        public double ThrottleStep = 0.1d;

        private void Update()
        {
            if (Runner == null || Runner.Warp == null)
            {
                return;
            }

            // Ступени варпа: клавиши 1..7 снапятся вниз в SetWarpFactor.
            for (int rung = 0; rung < WarpController.WarpRungs.Length; rung++)
            {
                if (PlayerInput.Down((GameKey)((int)GameKey.Digit1 + rung)))
                {
                    Runner.Warp.SetWarpFactor(WarpController.WarpRungs[rung]);
                }
            }

            double throttle = Runner.RawThrottle;
            if (PlayerInput.Held(GameKey.LeftShift))
            {
                throttle = System.Math.Min(1d, throttle + ThrottleStep * Time.deltaTime * 2d);
            }

            if (PlayerInput.Held(GameKey.LeftControl))
            {
                throttle = System.Math.Max(0d, throttle - ThrottleStep * Time.deltaTime * 2d);
            }

            Runner.RawThrottle = throttle;

            if (PlayerInput.Down(GameKey.P))
            {
                Runner.Paused = !Runner.Paused;
            }
        }
    }
}
