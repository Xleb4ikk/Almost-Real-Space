using Galilego.Events;
using Galilego.Simulation.ContactZone;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Player input (B3): warp ladder and throttle. Writes ONLY to SimulationRunner
    /// (Warp.SetWarpFactor / RawThrottle) — physics runs in its Update.
    /// Keys: 1..7 are the ladder rungs [1, 5, 10, 100, 500, 1000, 3000],
    /// LeftShift throttle up, LeftControl down, P pause.
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

            // Warp rungs: keys 1..7 snap down in SetWarpFactor.
            // Warp is forbidden in the contact zone: zone time runs on real dt
            // (VesselStep.AdvanceTime), and raised warp would jerk the ship on exit.
            // In build mode the digits are taken by the hotbar.
            if (!ContactZoneHost.Active && !Runner.BuildMode)
            {
                for (int rung = 0; rung < WarpController.WarpRungs.Length; rung++)
                {
                    if (PlayerInput.Down((GameAction)((int)GameAction.Warp1 + rung)))
                    {
                        Runner.Warp.SetWarpFactor(WarpController.WarpRungs[rung]);
                    }
                }
            }

            double throttle = Runner.RawThrottle;
            if (PlayerInput.Held(GameAction.ThrottleUp))
            {
                throttle = System.Math.Min(1d, throttle + ThrottleStep * Time.deltaTime * 2d);
            }

            if (PlayerInput.Held(GameAction.ThrottleDown))
            {
                throttle = System.Math.Max(0d, throttle - ThrottleStep * Time.deltaTime * 2d);
            }

            Runner.RawThrottle = throttle;

            if (PlayerInput.Down(GameAction.Pause))
            {
                Runner.Paused = !Runner.Paused;
            }
        }
    }
}
