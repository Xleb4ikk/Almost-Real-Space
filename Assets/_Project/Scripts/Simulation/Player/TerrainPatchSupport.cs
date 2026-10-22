using Galilego.Simulation.ContactZone;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Support — the contact zone's terrain patch collider, queried in render space. Used only
    /// in cabin mode (the player lives in a vessel's frame and cannot ask the analytic terrain,
    /// which speaks the site frame): a player stepping out of a landed capsule finds the ground
    /// here. Floor only; the composite does not sweep or ceiling-test the terrain slot.
    /// </summary>
    public sealed class TerrainPatchSupport : MeshSupportBase
    {
        /// <summary>Support id of the patch (below the building ids, so it never counts as a vessel).</summary>
        public const int SourceId = 50;

        protected override bool IsSupportCollider(Collider collider)
        {
            return collider != null && collider.GetComponentInParent<TerrainPatch>() != null;
        }

        protected override int SourceIdFor(Collider collider)
        {
            return SourceId;
        }
    }
}
