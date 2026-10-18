using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Support — building mesh colliders (SiteBox) in render space. Unlike the old
    /// PushOutMesh, the vertical is NOT zeroed: roofs and top faces are full support.
    /// Shared mechanics (Floor/Ceiling/Sweep) are in MeshSupportBase; here only
    /// building recognition and their sourceId.
    /// </summary>
    public sealed class SiteBoxSupport : MeshSupportBase
    {
        /// <summary>Base id for building supports in the composite.</summary>
        public const int SourceIdBase = 100;

        // Stable building ids (sourceId for transfer/addressing).
        private readonly System.Collections.Generic.Dictionary<SiteBox, int> siteIds
            = new System.Collections.Generic.Dictionary<SiteBox, int>();
        private int nextSiteId = SourceIdBase;

        /// <summary>Collider owner: a SiteBox in the ancestors (authored colliders) or
        /// SiteBoxPart.Owner (holders are children of parts, the SiteBox sits nearby).</summary>
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
            // Lifecycle-order safeguard: a building's OnEnable may not have built its
            // colliders yet.
            SiteBoxRegistry.EnsureAllBuilt();
            base.SetFrame(siteFrame);
        }
    }
}
