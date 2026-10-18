using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Terrain decor asset preset: a list of layers (grass/rocks/trees) with
    /// meshes, materials and distribution rules. One preset for all similar
    /// bodies; the terrain seed stays per-body.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Ground Decor Profile", fileName = "GroundDecorProfile")]
    public sealed class GroundDecorProfileAsset : ScriptableObject
    {
        public GroundDecorProfile Profile = new GroundDecorProfile();
    }
}
