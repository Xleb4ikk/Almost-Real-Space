using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Cloud profile asset. Like AtmosphereProfileAsset — it just stores a
    /// named CloudProfile preset so bodies can reuse it.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Cloud Profile", fileName = "CloudProfile")]
    public sealed class CloudProfileAsset : ScriptableObject
    {
        public CloudProfile Profile = new CloudProfile();
    }
}
