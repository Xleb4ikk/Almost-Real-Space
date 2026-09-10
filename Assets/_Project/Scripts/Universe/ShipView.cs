using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Рендер корабля (B3). Корабль живёт в double-мире ядра; Unity-позиция —
    /// ОТНОСИТЕЛЬНО доминантного тела (floating origin): world-позиция корабля
    /// = world-позиция BodyView доминантного тела + мостнутая дельта. Важно
    /// вызывать ПОСЛЕ BodyView.LateUpdate того же кадра (порядок скриптов).
    /// Поиск трансформа доминантного тела — через Runner.SystemView (кэш
    /// соответствий тело→трансформ, заполняется BodyView при старте), без
    /// перебора иерархии каждый кадр.
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

            // Floating origin: единый якорь (FloatingOrigin.Anchor).
            transform.position = FloatingOrigin.ToRender(Runner.Ship.Position);
        }
    }
}
