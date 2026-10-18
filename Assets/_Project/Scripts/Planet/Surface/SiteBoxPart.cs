using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Marker for a construction part collider: the holder is created as a CHILD
    /// of the source part (its pose always equals the part pose, independent of
    /// site Refresh order), while the SiteBox itself sits beside the holder (on
    /// the "Collider" object), not in its ancestry. The player support source
    /// (SiteBoxSupport) finds the owner through this reference.
    /// </summary>
    public sealed class SiteBoxPart : MonoBehaviour
    {
        [Tooltip("Владелец-постройка; задаётся SiteBox при сборке коллайдеров.")]
        public SiteBox Owner;
    }
}
