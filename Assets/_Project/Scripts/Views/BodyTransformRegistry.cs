using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Реестр соответствий тело→трансформ для floating-origin рендера:
    /// BodyView регистрирует себя при старте, ShipView читает позицию
    /// доминантного тела без перебора иерархии каждый кадр. Plain-объект
    /// (не MonoBehaviour), живёт в SimulationRunner.SystemView.
    /// </summary>
    public sealed class BodyTransformRegistry
    {
        private readonly Dictionary<string, Transform> transforms = new Dictionary<string, Transform>();

        public void Register(string bodyName, Transform transform)
        {
            transforms[bodyName] = transform;
        }

        public bool TryGetBodyTransform(string bodyName, out Transform transform)
        {
            return transforms.TryGetValue(bodyName, out transform);
        }
    }
}
