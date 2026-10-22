using System;
using System.Collections.Generic;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Pure construction math (no MonoBehaviour — testable without Play):
    /// pad polygon, cylinder intersection, snap search by nodes near the camera
    /// ray, draft-to-PartDefinition[] conversion for physics.
    /// All coordinates are SITE axes: x — north, y — zenith, z — east.
    /// Radial parts are never rotated: their cylinders always stand along Y, so
    /// intersection is a circle in plan × an interval in height.
    /// </summary>
    public static class BuildMath
    {
        /// <summary>Node contact is not an intersection: clearance along the normal, m.</summary>
        public const float ContactSkin = 0.02f;

        /// <summary>Maximum ray distance for snapping and placement, m.</summary>
        public const float SnapReach = 25f;

        /// <summary>Distance of the fallback point when the ray hits nothing, m.</summary>
        public const float AirFallbackDistance = 6f;

        // ─── Pad polygon ────────────────────────────────────────────────

        /// <summary>Point inside a closed polygon (x = north, y = east).</summary>
        public static bool InsidePolygon(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if (((a.y > point.y) != (b.y > point.y))
                    && (point.x < ((b.x - a.x) * (point.y - a.y) / (b.y - a.y)) + a.x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// The part's WHOLE footprint inside the polygon: center and base circle
        /// (otherwise half the tank would hang off the pad edge).
        /// </summary>
        public static bool FootprintInside(IReadOnlyList<Vector2> polygon, Vector2 center, float radius)
        {
            if (!InsidePolygon(polygon, center))
            {
                return false;
            }

            const int samples = 8;
            for (int i = 0; i < samples; i++)
            {
                float angle = (2f * Mathf.PI * i) / samples;
                var p = new Vector2(
                    center.x + (radius * Mathf.Cos(angle)),
                    center.y + (radius * Mathf.Sin(angle)));
                if (!InsidePolygon(polygon, p))
                {
                    return false;
                }
            }

            return true;
        }

        // ─── Intersections ─────────────────────────────────────────────────────

        /// <summary>
        /// Whether two bodies of revolution (cylinders/truncated cones) overlap:
        /// intervals along Y and circles in plan. A part's radius changes linearly
        /// between bottom and top, so the sum of radii is also linear and reaches
        /// its maximum over the overlap interval at one of its ends: check both.
        /// Node contact (intervals touching) is not an intersection.
        /// </summary>
        public static bool CylindersOverlap(
            Vector3 centerA, PartMetrics a,
            Vector3 centerB, PartMetrics b)
        {
            float lo = Mathf.Max(centerA.y + a.Bottom, centerB.y + b.Bottom);
            float hi = Mathf.Min(centerA.y + a.Top, centerB.y + b.Top);
            if (hi - lo <= ContactSkin)
            {
                return false;
            }

            float sumAtLo = RadiusAtHeight(centerA, a, lo) + RadiusAtHeight(centerB, b, lo);
            float sumAtHi = RadiusAtHeight(centerA, a, hi) + RadiusAtHeight(centerB, b, hi);
            float dx = centerA.x - centerB.x;
            float dz = centerA.z - centerB.z;
            float maxSum = Mathf.Max(sumAtLo, sumAtHi) - ContactSkin;
            return ((dx * dx) + (dz * dz)) < (maxSum * maxSum);
        }

        /// <summary>Part radius at height y (site axes): linear between the ends.</summary>
        public static float RadiusAtHeight(Vector3 center, PartMetrics m, float y)
        {
            float span = m.Top - m.Bottom;
            if (span <= 1e-4f)
            {
                return m.Radius;
            }

            float t = Mathf.Clamp01((y - (center.y + m.Bottom)) / span);
            return Mathf.Lerp(m.Radius, m.TopRadius, t);
        }

        /// <summary>Whether the probe part overlaps the already placed ones.</summary>
        public static bool OverlapsAny(
            IReadOnlyList<PlacedPart> parts, Vector3 pivot, PartMetrics probe, int ignoreIndex)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (i == ignoreIndex)
                {
                    continue;
                }

                // Radial attachments (RCS blocks) sit on a host's side and are not
                // stack bodies: they must not block a tank placed above/below.
                if (parts[i].Radial)
                {
                    continue;
                }

                if (CylindersOverlap(pivot, probe, parts[i].Pivot, parts[i].Metrics))
                {
                    return true;
                }
            }

            return false;
        }

        // ─── Snapping ────────────────────────────────────────────────────────────

        /// <summary>Where a snap was found: the partner part and which of its nodes is taken.</summary>
        public readonly struct SnapHit
        {
            public readonly int PartIndex;

            /// <summary>true — the ghost's bottom sits on the partner's TOP.</summary>
            public readonly bool OnTop;

            /// <summary>Desired ghost pivot (site axes).</summary>
            public readonly Vector3 Pivot;

            public SnapHit(int partIndex, bool onTop, Vector3 pivot)
            {
                PartIndex = partIndex;
                OnTop = onTop;
                Pivot = pivot;
            }
        }

        /// <summary>
        /// A free node under the aiming RETICLE the ghost can stick to: a
        /// candidate is a node inside the reticle cone (perpendicular distance
        /// ≤ along × coneTangent, range ≤ SnapReach); the node closest BY ANGLE
        /// to the reticle center wins, distance only breaks near-ties:
        /// "reticle aimed at a node" means the node under the center, not any
        /// node the edge touched. Candidates: ghost bottom onto the partner's
        /// top and ghost top under the partner's bottom.
        /// </summary>
        public static bool TryFindSnap(
            IReadOnlyList<PlacedPart> parts, PartMetrics ghost,
            Vector3 rayOrigin, Vector3 rayDirection, float coneTangent,
            out SnapHit hit)
        {
            hit = default;
            bool found = false;
            float bestOffset = float.MaxValue;
            float bestT = float.MaxValue;

            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];

                if (!p.TopOccupied)
                {
                    Vector3 node = p.Pivot + new Vector3(0f, p.Metrics.Top, 0f);
                    if (RayHitsNode(rayOrigin, rayDirection, node, coneTangent, out float t, out float offset)
                        && IsCloserToCenter(offset, t, found, bestOffset, bestT))
                    {
                        bestOffset = offset;
                        bestT = t;
                        hit = new SnapHit(i, true, node - new Vector3(0f, ghost.Bottom, 0f));
                        found = true;
                    }
                }

                if (!p.BottomOccupied)
                {
                    Vector3 node = p.Pivot + new Vector3(0f, p.Metrics.Bottom, 0f);
                    if (RayHitsNode(rayOrigin, rayDirection, node, coneTangent, out float t, out float offset)
                        && IsCloserToCenter(offset, t, found, bestOffset, bestT))
                    {
                        bestOffset = offset;
                        bestT = t;
                        hit = new SnapHit(i, false, node - new Vector3(0f, ghost.Top, 0f));
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Which of the two caught nodes is closer to the reticle center: a
        /// smaller angular offset beats distance; on near-equal offset — the closer along the ray.
        /// </summary>
        private static bool IsCloserToCenter(
            float offset, float t, bool found, float bestOffset, float bestT)
        {
            return !found
                || offset < bestOffset - 1e-6f
                || (offset < bestOffset + 1e-6f && t < bestT);
        }

        /// <summary>
        /// Node inside the aiming cone: the perpendicular distance to the ray is
        /// no more than along × coneTangent — the same condition as "node pixel
        /// inside the reticle" on screen (perspective projection preserves the
        /// aspect ratio); offset is the node's angular offset from the reticle
        /// center (lateral/along). Nodes closer than 0.2 m are not caught: a
        /// point behind the camera / at point blank gives garbage hits.
        /// </summary>
        private static bool RayHitsNode(
            Vector3 rayOrigin, Vector3 rayDirection, Vector3 node,
            float coneTangent, out float t, out float offset)
        {
            t = 0f;
            offset = 0f;
            Vector3 d = node - rayOrigin;
            float along = Vector3.Dot(d, rayDirection);
            if (along < 0.2f || along > SnapReach)
            {
                return false;
            }

            Vector3 lateral = d - (rayDirection * along);
            float lateralLength = lateral.magnitude;
            if (lateralLength > along * coneTangent)
            {
                return false;
            }

            t = along;
            offset = lateralLength / along;
            return true;
        }

        /// <summary>
        /// Second snap pass: the ghost already stands at a free point (floating
        /// over or next to the stack) — if its bottom passed right over a free
        /// part TOP (or its top under the part's bottom) and the centers nearly
        /// match in XZ, it snaps edge-to-edge. This guarantees "no need to catch
        /// millimeters": a tank placed with a gap over an engine still lands on it.
        /// </summary>
        public static bool TryFindProximitySnap(
            IReadOnlyList<PlacedPart> parts, PartMetrics ghost, Vector3 pivot, out SnapHit hit)
        {
            hit = default;
            bool found = false;
            float bestGap = float.MaxValue;
            float ghostBottom = pivot.y + ghost.Bottom;
            float ghostTop = pivot.y + ghost.Top;

            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                float dx = pivot.x - p.Pivot.x;
                float dz = pivot.z - p.Pivot.z;
                float lateral = (dx * dx) + (dz * dz);

                // XZ tolerance follows the CONTACTING ends (a cone has different
                // radii): ghost bottom to partner top and ghost top to its bottom.
                if (!p.TopOccupied)
                {
                    float reach = Mathf.Max(0.05f, Mathf.Min(ghost.Radius, p.Metrics.TopRadius) * 0.85f);
                    float node = p.Pivot.y + p.Metrics.Top;
                    float gap = ghostBottom - node;
                    if (lateral <= reach * reach
                        && gap >= -0.25f && gap <= 1.5f && Mathf.Abs(gap) < Mathf.Abs(bestGap))
                    {
                        bestGap = gap;
                        hit = new SnapHit(i, true, new Vector3(p.Pivot.x, node - ghost.Bottom, p.Pivot.z));
                        found = true;
                    }
                }

                if (!p.BottomOccupied)
                {
                    float reach = Mathf.Max(0.05f, Mathf.Min(ghost.TopRadius, p.Metrics.Radius) * 0.85f);
                    float node = p.Pivot.y + p.Metrics.Bottom;
                    float gap = node - ghostTop;
                    if (lateral <= reach * reach
                        && gap >= -0.25f && gap <= 1.5f && Mathf.Abs(gap) < Mathf.Abs(bestGap))
                    {
                        bestGap = gap;
                        hit = new SnapHit(i, false, new Vector3(p.Pivot.x, node - ghost.Top, p.Pivot.z));
                        found = true;
                    }
                }
            }

            return found;
        }

        // ─── Ray hitting a part (aim-based removal) ──────────────────

        /// <summary>
        /// The center ray is tested first: if it hits a part, the nearest one
        /// exactly at the reticle center is chosen. This keeps the ring probes
        /// from pulling the choice to a neighboring part (e.g. an engine under a
        /// tank), even if several peripheral rays graze its edge. Only when the
        /// center ray misses every part do we choose by majority vote of the
        /// ring probes. votes is a caller buffer (indexed by parts; length ≥
        /// parts.Count): no per-frame allocations. Pure math — visuals need no
        /// colliders, and they don't pollute the placement ray.
        /// </summary>
        public static bool TryRaycastParts(
            IReadOnlyList<PlacedPart> parts, Vector3 rayOrigin,
            IReadOnlyList<Vector3> rayDirections, int[] votes,
            float maxDistance, out int index, out float distance)
        {
            index = -1;
            distance = maxDistance;
            for (int i = 0; i < parts.Count; i++)
            {
                votes[i] = 0;
            }

            // The center ray has priority over the ring periphery. Otherwise,
            // aiming at the tank's lower edge could let several ring rays hit
            // the engine below and win the vote while the reticle looks at the tank.
            if (rayDirections.Count > 0)
            {
                int centerNearest = -1;
                float centerDistance = maxDistance;
                for (int i = 0; i < parts.Count; i++)
                {
                    PlacedPart p = parts[i];
                    Vector3 baseCenter = p.Pivot + new Vector3(0f, p.Metrics.Bottom, 0f);
                    if (RayFrustum(rayOrigin, rayDirections[0], baseCenter,
                            p.Metrics.Radius, p.Metrics.TopRadius, p.Metrics.Height, out float t)
                        && t < centerDistance)
                    {
                        centerDistance = t;
                        centerNearest = i;
                    }
                }

                if (centerNearest >= 0)
                {
                    index = centerNearest;
                    distance = centerDistance;
                    votes[index] = 1;
                    return true;
                }
            }

            // Center ray empty — allow choosing by the reticle ring.
            for (int s = 1; s < rayDirections.Count; s++)
            {
                int nearest = -1;
                float nearestT = maxDistance;
                for (int i = 0; i < parts.Count; i++)
                {
                    PlacedPart p = parts[i];
                    Vector3 baseCenter = p.Pivot + new Vector3(0f, p.Metrics.Bottom, 0f);
                    if (RayFrustum(rayOrigin, rayDirections[s], baseCenter,
                            p.Metrics.Radius, p.Metrics.TopRadius, p.Metrics.Height, out float t)
                        && t < nearestT)
                    {
                        nearestT = t;
                        nearest = i;
                    }
                }

                if (nearest >= 0)
                {
                    votes[nearest]++;
                }
            }

            int bestVotes = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                int v = votes[i];
                if (v == 0)
                {
                    continue;
                }

                bool better;
                if (index < 0 || v > bestVotes)
                {
                    better = true;
                }
                else if (v < bestVotes)
                {
                    better = false;
                }
                else
                {
                    better = MinHitDistance(parts, rayOrigin, rayDirections, i, maxDistance)
                        < MinHitDistance(parts, rayOrigin, rayDirections, index, maxDistance);
                }

                if (better)
                {
                    index = i;
                    bestVotes = v;
                }
            }

            if (index >= 0)
            {
                distance = MinHitDistance(parts, rayOrigin, rayDirections, index, maxDistance);
            }

            return index >= 0;
        }

        /// <summary>Minimum part hit distance over the probes (or maxDistance).</summary>
        private static float MinHitDistance(
            IReadOnlyList<PlacedPart> parts, Vector3 rayOrigin,
            IReadOnlyList<Vector3> rayDirections, int partIndex, float maxDistance)
        {
            PlacedPart p = parts[partIndex];
            Vector3 baseCenter = p.Pivot + new Vector3(0f, p.Metrics.Bottom, 0f);
            float best = maxDistance;
            for (int s = 0; s < rayDirections.Count; s++)
            {
                if (RayFrustum(rayOrigin, rayDirections[s], baseCenter,
                        p.Metrics.Radius, p.Metrics.TopRadius, p.Metrics.Height, out float t)
                    && t < best)
                {
                    best = t;
                }
            }

            return best;
        }

        /// <summary>
        /// Ray intersection with a vertical body of revolution: side (truncated
        /// cone; equal radii — cylinder) + flat end caps. The quadratic equation
        /// comes from substituting the generatrix r(y) = r0 + k·y into x² + z² = r(y)².
        /// </summary>
        public static bool RayFrustum(
            Vector3 rayOrigin, Vector3 rayDirection,
            Vector3 baseCenter, float bottomRadius, float topRadius, float height,
            out float distance)
        {
            distance = 0f;
            float safeHeight = Mathf.Max(height, 1e-6f);
            float k = (topRadius - bottomRadius) / safeHeight;
            float ox = rayOrigin.x - baseCenter.x;
            float oz = rayOrigin.z - baseCenter.z;
            float oy = rayOrigin.y - baseCenter.y;
            float m = bottomRadius + (k * oy);          // radius at the ray's start height
            float dx = rayDirection.x;
            float dy = rayDirection.y;
            float dz = rayDirection.z;

            float a = (dx * dx) + (dz * dz) - (k * k * dy * dy);
            float b = 2f * (((ox * dx) + (oz * dz)) - (m * k * dy));
            float c = (ox * ox) + (oz * oz) - (m * m);

            float best = -1f;
            const float eps = 1e-8f;

            // Side: quadratic equation (degenerates to linear when the ray is
            // parallel to the cone's generatrix).
            if (Mathf.Abs(a) > eps)
            {
                float disc = (b * b) - (4f * a * c);
                if (disc >= 0f)
                {
                    float sq = Mathf.Sqrt(disc);
                    float t0 = (-b - sq) / (2f * a);
                    float t1 = (-b + sq) / (2f * a);
                    for (int i = 0; i < 2; i++)
                    {
                        float t = i == 0 ? t0 : t1;
                        if (t < 0f)
                        {
                            continue;
                        }

                        float y = oy + (dy * t);
                        if (y >= 0f && y <= height && (best < 0f || t < best))
                        {
                            best = t;
                        }
                    }
                }
            }
            else if (Mathf.Abs(b) > eps)
            {
                float t = -c / b;
                float y = oy + (dy * t);
                if (t >= 0f && y >= 0f && y <= height)
                {
                    best = t;
                }
            }

            // End caps: needed to look at the part from above/below.
            if (Mathf.Abs(dy) > 1e-6f)
            {
                for (int cap = 0; cap < 2; cap++)
                {
                    float capY = baseCenter.y + (cap == 0 ? 0f : height);
                    float t = (capY - rayOrigin.y) / dy;
                    if (t < 0f)
                    {
                        continue;
                    }

                    float x = rayOrigin.x + (dx * t) - baseCenter.x;
                    float z = rayOrigin.z + (dz * t) - baseCenter.z;
                    float radius = cap == 0 ? bottomRadius : topRadius;
                    if ((x * x) + (z * z) <= radius * radius && (best < 0f || t < best))
                    {
                        best = t;
                    }
                }
            }

            if (best < 0f)
            {
                return false;
            }

            distance = best;
            return true;
        }

        // ─── Assembly for physics ───────────────────────────────────────────────

        /// <summary>Lowest point of the assembly (bottom of the lowest part), site axes.</summary>
        public static Vector3 VesselBottom(IReadOnlyList<PlacedPart> parts)
        {
            Vector3 best = Vector3.zero;
            bool has = false;
            for (int i = 0; i < parts.Count; i++)
            {
                Vector3 pivot = parts[i].Pivot;
                float bottom = pivot.y + parts[i].Metrics.Bottom;
                if (!has || bottom < best.y)
                {
                    best = new Vector3(pivot.x, bottom, pivot.z);
                    has = true;
                }
            }

            return best;
        }

        /// <summary>Total assembly mass, kg.</summary>
        public static double TotalMass(IReadOnlyList<PlacedPart> parts)
        {
            double mass = 0d;
            for (int i = 0; i < parts.Count; i++)
            {
                mass += parts[i].Metrics.MassKg;
            }

            return mass;
        }

        /// <summary>
        /// Dry assembly mass for ThrustSource: only the propellant that burns at
        /// the engine's O/F ratio (BipropellantMix) is subtracted from the total.
        /// Propellant burns in pairs (oxidizer + fuel), so an excess of one
        /// component without the other is dead weight: it stays in the mass after
        /// burnout and must not be subtracted. If tanks are filled unevenly, the
        /// excess is not subtracted either.
        /// </summary>
        public static double EffectiveDryMass(IReadOnlyList<PlacedPart> parts, BipropellantMix mix)
        {
            double total = 0d;
            double oxidizer = 0d;
            double fuel = 0d;
            for (int i = 0; i < parts.Count; i++)
            {
                PartMetrics m = parts[i].Metrics;
                total += m.MassKg;
                if (m.Load == null)
                {
                    continue;
                }

                for (int c = 0; c < m.Load.Count; c++)
                {
                    Propellant p = m.Load.Component(c);
                    double kg = m.Load.ComponentMassKg(c, m.VolumeM3, 1d);
                    if (p == mix.Oxidizer)
                    {
                        oxidizer += kg;
                    }
                    else if (p == mix.Fuel)
                    {
                        fuel += kg;
                    }
                }
            }

            return total - mix.BurnableMassKg(oxidizer, fuel);
        }

        /// <summary>
        /// Draft → PartDefinition[] for VesselStep/contact zone: Offset is the
        /// part center from the assembly BOTTOM (vessel space: y is site up),
        /// ParentIndex from snapping. Shape is a body of revolution: a cylinder,
        /// or a truncated cone for the adapter tank (Dimensions.z — top radius).
        /// Radial parts (RCS blocks) are boxes with the part rotation; their body
        /// is centred on PhysicsCenterLocal, not on the hull contact pivot. Offset is
        /// the BODY centre: the part pivot plus the metrics centre (the pivot is the
        /// part bottom for tanks, engines and the capsule) and PhysicsCenterLocal.
        /// </summary>
        public static PartDefinition[] ToPartDefinitions(IReadOnlyList<PlacedPart> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return null;
            }

            Vector3 bottom = VesselBottom(parts);
            var defs = new PartDefinition[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                bool box = p.Kind == PartKind.RcsBlock;
                Vector3 center = p.Pivot + p.Rotation
                    * (new Vector3(0f, p.Metrics.Center, 0f) + p.PhysicsCenterLocal);
                float centerY = center.y - bottom.y;
                float bottomY = p.Pivot.y + p.Metrics.Bottom - bottom.y;
                bool frustum = !box && p.Metrics.TopRadius > 0.01f
                    && Mathf.Abs(p.Metrics.TopRadius - p.Metrics.Radius) > 1e-3f;
                defs[i] = new PartDefinition
                {
                    Name = PartCatalog.DisplayName(p.Kind, p.EngineSpec),
                    MassKg = p.Metrics.MassKg,
                    JointStrengthNewtons = p.Metrics.JointStrengthNewtons,
                    BreakTorqueNm = p.Metrics.BreakTorqueNm,
                    Offset = new Vector3(center.x - bottom.x, centerY, center.z - bottom.z),
                    Shape = box ? PartShape.Box : (frustum ? PartShape.Frustum : PartShape.Cylinder),
                    Dimensions = box
                        ? RcsBlockPlacement.BodySize
                        : (frustum
                            ? new Vector3(p.Metrics.Radius, p.Metrics.Height, p.Metrics.TopRadius)
                            : new Vector3(p.Metrics.Radius, p.Metrics.Height, 0f)),
                    Orientation = new Vector4(
                        p.Rotation.x, p.Rotation.y, p.Rotation.z, p.Rotation.w),
                    ParentIndex = p.ParentIndex,
                    JointAnchor = new Vector3(0f, bottomY - centerY, 0f),
                    EngineOffset = new Vector3(0f, bottomY - centerY, 0f),
                    PlayerPassthrough = p.Kind == PartKind.Capsule,
                };
            }

            return defs;
        }
    }
}
