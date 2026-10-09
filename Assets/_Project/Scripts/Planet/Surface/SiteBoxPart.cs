using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Метка коллайдера-части постройки: холдер создаётся ДЕТЬЮ исходной части
    /// (его поза всегда равна позе части — не зависит от порядка Refresh места),
    /// а сам SiteBox лежит не в предках холдера, а рядом (на объекте «Collider»).
    /// Поэтому источник опоры (SiteBoxSupport) находит владельца по этой ссылке.
    /// </summary>
    public sealed class SiteBoxPart : MonoBehaviour
    {
        [Tooltip("Владелец-постройка; задаётся SiteBox при сборке коллайдеров.")]
        public SiteBox Owner;
    }
}
