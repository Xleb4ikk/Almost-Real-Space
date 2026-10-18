using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Body-to-transform registry for floating-origin rendering: BodyView registers
    /// itself at start, ShipView reads the dominant body's transform without walking
    /// the hierarchy each frame. Plain object (not MonoBehaviour), lives in
    /// SimulationRunner.SystemView.
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
