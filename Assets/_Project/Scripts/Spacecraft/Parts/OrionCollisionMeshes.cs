using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Holder for the capsule's runtime convex-collider meshes (OrionCollisionBuilder):
    /// releases them when the part is destroyed, otherwise the meshes would accumulate in memory.
    /// </summary>
    public sealed class OrionCollisionMeshes : MonoBehaviour
    {
        public readonly List<Mesh> Meshes = new List<Mesh>();

        private void OnDestroy()
        {
            for (int i = 0; i < Meshes.Count; i++)
            {
                if (Meshes[i] != null)
                {
                    Destroy(Meshes[i]);
                }
            }

            Meshes.Clear();
        }
    }
}
