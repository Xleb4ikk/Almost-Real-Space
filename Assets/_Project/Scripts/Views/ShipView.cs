using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Ship render (B3). The ship lives in the core's double world; the Unity position
    /// is relative to the floating origin. Must run AFTER BodyView.LateUpdate in the
    /// same frame (script order).
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-90)]
    public sealed class ShipView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        private void LateUpdate()
        {
            if (Runner == null || Runner.Ship == null)
            {
                return;
            }

            // Floating origin: single anchor (FloatingOrigin.Anchor).
            transform.position = FloatingOrigin.ToRender(Runner.Ship.Position);
        }
    }
}
