using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Ассет-профиль облаков. Как AtmosphereProfileAsset — только хранит
    /// именованный пресет CloudProfile, чтобы тела могли его переиспользовать.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Cloud Profile", fileName = "CloudProfile")]
    public sealed class CloudProfileAsset : ScriptableObject
    {
        public CloudProfile Profile = new CloudProfile();
    }
}
