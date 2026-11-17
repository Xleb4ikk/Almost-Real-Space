using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Gas giant profile asset. Like AtmosphereProfileAsset/CloudProfileAsset,
    /// the asset only stores a named preset; the view and shader read a plain
    /// GasGiantProfile.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Gas Giant Profile", fileName = "GasGiantProfile")]
    public sealed class GasGiantProfileAsset : ScriptableObject
    {
        public GasGiantProfile Profile = GasGiantProfile.CreateAeolus();
    }
}
