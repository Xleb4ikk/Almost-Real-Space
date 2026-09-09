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
    public sealed class ShipView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        private void LateUpdate()
        {
            if (Runner == null || Runner.Ship == null || Runner.DominantBody == null)
            {
                return;
            }

            if (Runner.SystemView == null || !Runner.SystemView.TryGetBodyTransform(Runner.DominantBody.Name, out Transform bodyTransform))
            {
                return;
            }

            Runner.SystemState.EvaluateBodyState(Runner.DominantBody, Runner.TimeSeconds, out Vector3d bodyP, out _);
            Vector3d delta = Runner.Ship.Position - bodyP;
            Vector3 simDelta = AstroFrame.ToSimulation(delta);
            transform.position = bodyTransform.position + simDelta;
        }
    }
}
