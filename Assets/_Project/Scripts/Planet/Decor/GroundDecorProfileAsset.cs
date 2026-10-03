using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Ассет-пресет декора местности: список слоёв (трава/камни/деревья) с
    /// мешами, материалами и правилами распределения. Один пресет — на все
    /// похожие тела; сид рельефа остаётся per-body.
    /// </summary>
    [CreateAssetMenu(menuName = "Galilego/Ground Decor Profile", fileName = "GroundDecorProfile")]
    public sealed class GroundDecorProfileAsset : ScriptableObject
    {
        public GroundDecorProfile Profile = new GroundDecorProfile();
    }
}
