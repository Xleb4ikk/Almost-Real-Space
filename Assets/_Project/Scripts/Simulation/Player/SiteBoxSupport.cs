using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Опора — мешевые коллайдеры построек (SiteBox) в render-пространстве.
    /// В отличие от старого PushOutMesh, вертикаль НЕ обнуляется: крыши и
    /// верхние грани — полноценная опора. Общая механика (Floor/Ceiling/Sweep)
    /// в MeshSupportBase; здесь — только опознание построек и их sourceId.
    /// </summary>
    public sealed class SiteBoxSupport : MeshSupportBase
    {
        /// <summary>База идентификаторов опор-построек в композите.</summary>
        public const int SourceIdBase = 100;

        // Стабильные идентификаторы построек (sourceId для переноса/адресации).
        private readonly System.Collections.Generic.Dictionary<SiteBox, int> siteIds
            = new System.Collections.Generic.Dictionary<SiteBox, int>();
        private int nextSiteId = SourceIdBase;

        /// <summary>Владелец коллайдера: SiteBox в предках (авторские коллайдеры) или
        /// SiteBoxPart.Owner (холдеры — дети частей, SiteBox лежит рядом).</summary>
        public static SiteBox ResolveSiteBox(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            SiteBox box = collider.GetComponentInParent<SiteBox>();
            if (box != null)
            {
                return box;
            }

            SiteBoxPart part = collider.GetComponentInParent<SiteBoxPart>();
            return part != null ? part.Owner : null;
        }

        protected override bool IsSupportCollider(Collider collider)
        {
            return ResolveSiteBox(collider) != null;
        }

        protected override int SourceIdFor(Collider collider)
        {
            SiteBox box = ResolveSiteBox(collider);
            if (box == null)
            {
                return -1;
            }

            if (!siteIds.TryGetValue(box, out int id))
            {
                id = nextSiteId++;
                siteIds.Add(box, id);
            }

            return id;
        }

        public override void SetFrame(SiteFrame siteFrame)
        {
            // Страховка от порядка жизненного цикла: OnEnable постройки мог не
            // собрать коллайдеры (наблюдалось «мигание» между сессиями Play).
            SiteBoxRegistry.EnsureAllBuilt();
            base.SetFrame(siteFrame);
        }
    }
}
