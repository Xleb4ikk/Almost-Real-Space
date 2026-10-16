using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Держатель runtime-мешей выпуклых коллайдеров капсулы (OrionCollisionBuilder):
    /// при уничтожении детали освобождает их, иначе меши копились бы в памяти.
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
