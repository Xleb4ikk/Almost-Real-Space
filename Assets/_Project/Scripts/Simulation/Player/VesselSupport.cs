using Galilego.Building;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Support — vessel parts: capsule colliders of the build-time visuals and
    /// contact-zone proxy body colliders in flight (both tagged VesselPartCollider).
    /// Floor/Ceiling/Sweep mechanics are in MeshSupportBase; here only part
    /// recognition and their sourceId.
    /// </summary>
    public sealed class VesselSupport : MeshSupportBase
    {
        /// <summary>Base id for vessel parts in the composite.</summary>
        public const int SourceIdBase = 1000;

        private readonly System.Collections.Generic.Dictionary<VesselPartCollider, int> partIds
            = new System.Collections.Generic.Dictionary<VesselPartCollider, int>();
        private int nextPartId = SourceIdBase;

        // sourceId → part root (the platform the player can be carried by).
        private readonly System.Collections.Generic.Dictionary<int, Transform> platforms
            = new System.Collections.Generic.Dictionary<int, Transform>();

        /// <summary>Part root (VesselPartCollider owner) a collider belongs to; null — not a vessel.</summary>
        public static Transform PlatformOf(Collider collider)
        {
            VesselPartCollider part = ResolvePart(collider);
            return part != null ? part.transform : null;
        }

        /// <summary>Part root by the support id (the one Floor returned); false — unknown or destroyed.</summary>
        public bool TryGetPlatform(int sourceId, out Transform platform)
        {
            return platforms.TryGetValue(sourceId, out platform) && platform != null;
        }

        public static VesselPartCollider ResolvePart(Collider collider)
        {
            return collider != null ? collider.GetComponentInParent<VesselPartCollider>() : null;
        }

        protected override bool IsSupportCollider(Collider collider)
        {
            VesselPartCollider part = ResolvePart(collider);
            return part != null && !(part.VisualCovered && ContactZoneHost.Active);
        }

        protected override int SourceIdFor(Collider collider)
        {
            if (partIds.Count > 256)
            {
                PruneDead();
            }

            VesselPartCollider part = ResolvePart(collider);
            if (part == null)
            {
                return -1;
            }

            if (!partIds.TryGetValue(part, out int id))
            {
                id = nextPartId++;
                partIds.Add(part, id);
                platforms[id] = part.transform;
            }

            return id;
        }

        /// <summary>
        /// Drop only destroyed parts: a full Clear would strip ids from live build-mode
        /// colliders and break the player standing on the capsule for one frame.
        /// </summary>
        private void PruneDead()
        {
            var dead = new System.Collections.Generic.List<VesselPartCollider>();
            foreach (System.Collections.Generic.KeyValuePair<VesselPartCollider, int> kv in partIds)
            {
                if (kv.Key == null)
                {
                    dead.Add(kv.Key);
                }
            }

            for (int i = 0; i < dead.Count; i++)
            {
                if (partIds.TryGetValue(dead[i], out int id))
                {
                    platforms.Remove(id);
                }

                partIds.Remove(dead[i]);
            }
        }
    }
}
