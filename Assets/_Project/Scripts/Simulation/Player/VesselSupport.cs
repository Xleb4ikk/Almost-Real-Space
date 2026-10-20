using Galilego.Building;
using Galilego.Core;
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
            return ResolvePart(collider) != null;
        }

        protected override int SourceIdFor(Collider collider)
        {
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
    }
}
