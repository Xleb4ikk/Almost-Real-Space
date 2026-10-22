using System.Collections.Generic;
using Galilego.Building;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Shared player-support mechanics over PhysX colliders in render space:
    /// Floor — downward rays from the foot point (5 samples across the footprint),
    /// Ceiling — 5 samples up the head footprint, Sweep — a horizontal CapsuleCast
    /// with sliding along walls. Subclasses decide which colliders count as support
    /// (buildings, ship parts) and how to issue stable sourceIds.
    /// </summary>
    public abstract class MeshSupportBase : IPlayerSupport, IOverlapSupport
    {
        /// <summary>Walk diagnostics: enabled by the Debug Overlay toggle on PlayerController.</summary>
        public static bool DebugEnabled;

        /// <summary>Last collider that blocked a sweep (for the diagnostics overlay).</summary>
        public static string DebugBlocker = "-";

        protected SiteFrame frame;
        protected Vector3 renderOrigin;
        protected Vector3 renderUp;
        protected Vector3 renderEast;
        protected Vector3 renderNorth;
        protected bool ready;

        /// <summary>Warn about non-convex colliders once per collider.</summary>
        private readonly HashSet<Collider> warnedNonConvex = new HashSet<Collider>();

        /// <summary>Collider roles for walking: 1 — obstacle only (PlayerObstacleOnly), 2 — ignored (PlayerIgnore).</summary>
        private readonly Dictionary<Collider, int> roles = new Dictionary<Collider, int>();

        private int RoleOf(Collider collider)
        {
            if (!roles.TryGetValue(collider, out int role))
            {
                if (roles.Count > 512)
                {
                    roles.Clear();
                }

                role = (collider.GetComponent<PlayerObstacleOnly>() != null ? 1 : 0)
                    | (collider.GetComponent<PlayerIgnore>() != null ? 2 : 0);
                roles[collider] = role;
            }

            return role;
        }

        /// <summary>Furniture: a wall for the player, but not a floor or a step.</summary>
        private bool IsObstacleOnly(Collider collider)
        {
            return collider != null && (RoleOf(collider) & 1) != 0;
        }

        /// <summary>
        /// The collider counts for walking: belongs to the support and is not marked
        /// PlayerIgnore (monitors, seat interaction box: needed by aim rays, not the body).
        /// </summary>
        private bool IsPlayerCollider(Collider collider)
        {
            return collider != null && IsSupportCollider(collider) && (RoleOf(collider) & 2) == 0;
        }

        private readonly Collider[] overlapBuffer = new Collider[64];

        /// <summary>
        /// The capsule (feet at feet, radius, height) overlaps a support collider. The
        /// capsule is lifted 6 cm: standing on the floor, touching the floor is not
        /// "does not fit".
        /// </summary>
        public bool Overlaps(Vector3d feet, double radius, double height)
        {
            if (!ready)
            {
                return false;
            }

            const float Lift = 0.06f;
            float r = (float)radius;
            Vector3 p1 = ToRenderPoint(feet) + (renderUp * (Lift + r));
            Vector3 p2 = p1 + (renderUp * Mathf.Max(0.01f, (float)height - Lift - (2f * r)));
            int count = Physics.OverlapCapsuleNonAlloc(p1, p2, r, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (IsPlayerCollider(overlapBuffer[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Collider belongs to this source's support.</summary>
        protected abstract bool IsSupportCollider(Collider collider);

        /// <summary>Stable id of a support collider (sourceId).</summary>
        protected abstract int SourceIdFor(Collider collider);

        public virtual void SetFrame(SiteFrame siteFrame)
        {
            // Colliders move in LateUpdate with the floating origin, and
            // Physics.autoSyncTransforms is off: without synchronization the support
            // queries (rays/capsules in Update) would see last frame's poses — an
            // error equal to the player's displacement since the last sync.
            Physics.SyncTransforms();
            frame = siteFrame;
            renderOrigin = ToRender(Vector3d.Zero);
            renderUp = Direction(new Vector3d(0d, 0d, 1d));
            renderEast = Direction(new Vector3d(1d, 0d, 0d));
            renderNorth = Direction(new Vector3d(0d, 1d, 0d));
            ready = true;
        }

        /// <summary>
        /// Cabin mode: the controller's local axes are those of a vessel part — east = forward,
        /// north = right, up = up, origin = the part's position. The queries then run against the
        /// colliders in the very frame they move in, with no site frame or floating-origin
        /// conversion in between.
        /// </summary>
        public virtual void SetPlatformFrame(Transform root)
        {
            Physics.SyncTransforms();
            frame = null;
            renderOrigin = root.position;
            renderEast = root.forward.normalized;
            renderNorth = root.right.normalized;
            renderUp = root.up.normalized;
            ready = true;
        }

        /// <summary>Render-space point of the controller's local coordinates in the current frame.</summary>
        public Vector3 RenderPointOf(Vector3d local)
        {
            return ToRenderPoint(local);
        }

        /// <summary>Render-space direction of a local vector in the current frame.</summary>
        public Vector3 RenderDirectionOf(Vector3d local)
        {
            return (renderEast * (float)local.X) + (renderNorth * (float)local.Y) + (renderUp * (float)local.Z);
        }

        protected Vector3 ToRender(Vector3d local)
        {
            frame.ToWorld(local, Vector3d.Zero, out Vector3d world, out _);
            return FloatingOrigin.ToRender(world);
        }

        protected Vector3 Direction(Vector3d localDirection)
        {
            return (ToRender(localDirection) - ToRender(Vector3d.Zero)).normalized;
        }

        protected Vector3 ToRenderPoint(Vector3d local)
        {
            return renderOrigin
                + (renderEast * (float)local.X)
                + (renderNorth * (float)local.Y)
                + (renderUp * (float)local.Z);
        }

        /// <summary>
        /// The capsule overlaps a collider — push-out direction (wall normal) for the
        /// sweep. The CapsuleCast normal is unreliable when starting in penetration
        /// (usually -moveDir), so for such hits we take the normal from
        /// Physics.ComputePenetration. Touching the floor/ceiling gives a vertical
        /// push-out — the projection onto the plane degenerates and the method returns
        /// false (not a wall; horizontal is handled by the support/step logic).
        /// </summary>
        protected bool TryPushOut(Collider collider, Vector3 feet, float radius, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (collider == null)
            {
                return false;
            }

            if (collider is MeshCollider mc && !mc.convex && warnedNonConvex.Add(mc))
            {
                Debug.LogWarning("[Support] невыпуклый MeshCollider на '" + collider.name
                    + "' — ComputePenetration не работает", collider);
            }

            CapsuleCollider probe = SiteBoxRegistry.Probe(radius);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, renderUp);
            bool overlap = Physics.ComputePenetration(
                probe, feet, rotation,
                collider, collider.transform.position, collider.transform.rotation,
                out direction, out _);
            direction = Vector3.ProjectOnPlane(direction, renderUp);
            if (!overlap || direction.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            direction.Normalize();
            return true;
        }

        public bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d normal, out int sourceId)
        {
            height = 0d;
            normal = new Vector3d(0d, 0d, 1d);
            sourceId = -1;
            if (!ready || !(maxDrop > 0d))
            {
                return false;
            }

            // Support under the foot: one center ray plus four at the foot edge (by the
            // capsule radius). A single ray slips through narrow gaps between colliders
            // and edges. We take the HIGHEST support in the footprint: the feet land on it.
            const double Foot = PlayerSurfaceController.CapsuleRadius;
            bool found = false;
            double best = double.NegativeInfinity;
            for (int k = 0; k < 5; k++)
            {
                Vector3d sample = from;
                switch (k)
                {
                    case 1: sample = from + new Vector3d(Foot, 0d, 0d); break;
                    case 2: sample = from + new Vector3d(-Foot, 0d, 0d); break;
                    case 3: sample = from + new Vector3d(0d, Foot, 0d); break;
                    case 4: sample = from + new Vector3d(0d, -Foot, 0d); break;
                }

                // The ray starts 5 cm above the foot point: with micro-penetration the
                // foot is inside a convex collider and a ray from the start point finds
                // no surface — "no support", and the body falls through the floor. The
                // length is increased by the same lift, so support depth is unchanged.
                const float Lift = 0.05f;
                Vector3 origin = ToRenderPoint(sample) + (renderUp * Lift);
                RaycastHit[] hits = Physics.RaycastAll(origin, -renderUp, (float)maxDrop + Lift, ~0, QueryTriggerInteraction.Ignore);
                if (!TryPickFloor(hits, out double h, out Vector3d n, out int id))
                {
                    continue;
                }

                if (!found || h > best)
                {
                    best = h;
                    height = h;
                    normal = n;
                    sourceId = id;
                    found = true;
                }
            }

            return found;
        }

        private bool TryPickFloor(RaycastHit[] hits, out double height, out Vector3d normal, out int sourceId)
        {
            height = 0d;
            normal = new Vector3d(0d, 0d, 1d);
            sourceId = -1;
            bool found = false;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsPlayerCollider(hit.collider) || IsObstacleOnly(hit.collider) || hit.distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = hit.distance;
                height = LocalHeight(hit.point);
                normal = LocalDirection(hit.normal);
                sourceId = SourceIdFor(hit.collider);
                found = true;
            }

            return found;
        }

        public bool Ceiling(Vector3d head, double maxRise, out double height)
        {
            height = 0d;
            if (!ready || !(maxRise > 0d))
            {
                return false;
            }

            // Ceiling is searched along the head footprint (center + four 0.25 m
            // offsets): a single ray slips through gaps between colliders and edges.
            // We take the LOWEST ceiling in the footprint.
            bool found = false;
            float best = float.PositiveInfinity;
            for (int k = 0; k < 5; k++)
            {
                Vector3d sample = head;
                switch (k)
                {
                    case 1: sample = head + new Vector3d(0.25d, 0d, 0d); break;
                    case 2: sample = head + new Vector3d(-0.25d, 0d, 0d); break;
                    case 3: sample = head + new Vector3d(0d, 0.25d, 0d); break;
                    case 4: sample = head + new Vector3d(0d, -0.25d, 0d); break;
                }

                RaycastHit[] hits = Physics.RaycastAll(ToRenderPoint(sample), renderUp, (float)maxRise, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits.Length; i++)
                {
                    if (!IsPlayerCollider(hits[i].collider) || hits[i].distance >= best)
                    {
                        continue;
                    }

                    best = hits[i].distance;
                    height = LocalHeight(hits[i].point);
                    found = true;
                }
            }

            return found;
        }

        public bool Sweep(Vector3d from, Vector3d to, double radius, double height, out Vector3d normal, out double fraction)
        {
            normal = new Vector3d(0d, 0d, 1d);
            fraction = 1d;
            if (!ready)
            {
                return false;
            }

            // Capsule: the bottom of the lower hemisphere is exactly the feet (from),
            // the top is from + height. CapsuleCast adds the radius BEYOND the segment
            // ends, so the segment starts one radius above the feet (otherwise the
            // hemisphere goes 0.45 m under the floor and the sweep wrongly catches the
            // ceiling/floor under the feet).
            Vector3 p1 = ToRenderPoint(from) + (renderUp * (float)radius);
            Vector3 p2 = p1 + (renderUp * Mathf.Max(0.01f, (float)(height - (2d * radius))));
            Vector3 delta = ToRenderPoint(to) - ToRenderPoint(from);
            float distance = delta.magnitude;
            if (distance < 1e-6f)
            {
                return false;
            }

            Vector3 moveDir = delta / distance;
            RaycastHit[] hits = Physics.CapsuleCastAll(p1, p2, (float)radius, moveDir, distance, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            float bestFraction = 1f;
            Vector3 bestNormal = renderUp;

            // Wall gap: stop not at the contact point (otherwise the next substep
            // starts already inside PhysX's contact tolerance) but Skin before it.
            const float Skin = 0.015f;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsPlayerCollider(hit.collider) || hit.distance < 0f)
                {
                    continue;
                }

                Vector3 wallN = hit.normal;
                float f;
                if (hit.distance <= 0f)
                {
                    // Start in penetration: the CapsuleCast normal is unreliable — take
                    // the push-out direction. Moving away from the wall is free, entering
                    // deeper is blocked (see the direction check below). Touching the floor
                    // or ceiling is not a wall (vertical push-out).
                    if (!TryPushOut(hit.collider, p1 - (renderUp * (float)radius), (float)radius, out wallN))
                    {
                        continue;
                    }

                    f = 0f;
                }
                else
                {
                    // Touching floor/slope/ceiling (normal almost vertical) does not block
                    // horizontal movement: the support/step logic handles it. For
                    // furniture/monitors (PlayerObstacleOnly) only a nearly horizontal face
                    // counts as floor: 30–45° bevels and chamfers (|n·up| ~ 0.7–0.9) block
                    // as a wall.
                    float verticalLimit = IsObstacleOnly(hit.collider) ? 0.95f : 0.7f;
                    if (Mathf.Abs(Vector3.Dot(hit.normal, renderUp)) > verticalLimit)
                    {
                        continue;
                    }

                    f = Mathf.Max(0f, (hit.distance - Skin) / distance);
                }

                // Only movement INTO the surface blocks: moving away from the wall and
                // along the contact are free, entering deeper is blocked. The push-out
                // normal is reliable even when starting in penetration.
                if (Vector3.Dot(moveDir, wallN) > -1e-4f)
                {
                    continue;
                }

                if (f < bestFraction)
                {
                    bestFraction = f;
                    bestNormal = wallN;
                    found = true;

                    if (DebugEnabled)
                    {
                        DebugBlocker = hit.collider.name + " | родитель: " + hit.collider.transform.parent?.name
                            + " | obstacle=" + IsObstacleOnly(hit.collider)
                            + " | |n·up|=" + Mathf.Abs(Vector3.Dot(wallN, renderUp)).ToString("F2")
                            + " | f=" + f.ToString("F2");
                    }
                }
            }

            if (found)
            {
                fraction = bestFraction;
                normal = LocalDirection(bestNormal);
            }

            return found;
        }

        private double LocalHeight(Vector3 point)
        {
            return Vector3.Dot(point - renderOrigin, renderUp);
        }

        private Vector3d LocalDirection(Vector3 renderDirection)
        {
            return new Vector3d(
                Vector3.Dot(renderDirection, renderEast),
                Vector3.Dot(renderDirection, renderNorth),
                Vector3.Dot(renderDirection, renderUp));
        }
    }
}
