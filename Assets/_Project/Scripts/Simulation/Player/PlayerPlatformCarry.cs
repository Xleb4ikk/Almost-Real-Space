using System.Collections.Generic;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Rigid carry of the player by a moving vessel (contact zone, PhysX bodies).
    ///
    /// The walking controller is kinematic and samples colliders once per frame. A vessel
    /// that falls or brakes moves metres between two samples, so its colliders "teleport"
    /// through the player (the roof passes over the head, the floor drops away). The carry
    /// keeps the player's pose in the PLATFORM's local space: Capture after the step, TryApply
    /// before the next one. The platform can therefore never move through the player, at any
    /// speed or deceleration. The controller then only sees small motion relative to the
    /// vessel (walking, jumping), where sweeps and rays are reliable.
    ///
    /// All maths is in zone-root space (body-fixed, pinned to the site frame), so the orbital
    /// motion and spin of the planet drop out.
    ///
    /// Attach: standing on a vessel part (ground source id), or airborne inside one (a probe
    /// up and a probe down hit the same part). Release: standing on anything else, or leaving
    /// the part's bounds while airborne. Orientation: the player's position follows the
    /// vessel's rotation, the capsule itself stays upright.
    /// </summary>
    public sealed class PlayerPlatformCarry
    {
        private const float EnclosedProbeMeters = 3f;
        private const float EnclosedProbeHeight = 0.7f;
        private const float BoundsMarginMeters = 0.1f;

        private Transform platform;
        private Vector3 localPoint;
        private Vector3 lastZonePoint;
        private double poseTime;

        private readonly Dictionary<Transform, Collider[]> colliderCache = new Dictionary<Transform, Collider[]>();

        /// <summary>The player is currently carried by a vessel part.</summary>
        public bool Active
        {
            get { return platform != null; }
        }

        /// <summary>Velocity of the carried point relative to the site (astro axes, m/s).</summary>
        public Vector3d PlatformVelocity { get; private set; }

        /// <summary>Player velocity relative to the vessel (astro axes, m/s).</summary>
        public Vector3d RelativeVelocity;

        public void Reset()
        {
            platform = null;
            PlatformVelocity = Vector3d.Zero;
            RelativeVelocity = Vector3d.Zero;
            colliderCache.Clear();
        }

        /// <summary>Vector in astro axes → components along the frame's east/north/up.</summary>
        public static Vector3d ToAxes(SiteFrame frame, Vector3d v)
        {
            return new Vector3d(Vector3d.Dot(v, frame.East), Vector3d.Dot(v, frame.North), Vector3d.Dot(v, frame.Up));
        }

        /// <summary>Components along the frame's east/north/up → vector in astro axes.</summary>
        public static Vector3d FromAxes(SiteFrame frame, Vector3d v)
        {
            return (frame.East * v.X) + (frame.North * v.Y) + (frame.Up * v.Z);
        }

        // Site (east, north, up) ↔ zone-root Unity axes (north, up, east), see ContactZoneProxy.
        private static Vector3 ToZone(SiteFrame frame, Vector3d world)
        {
            frame.ToLocal(world, Vector3d.Zero, out Vector3d p, out _);
            return new Vector3((float)p.Y, (float)p.Z, (float)p.X);
        }

        private static Vector3d FromZone(SiteFrame frame, Vector3 z)
        {
            frame.ToWorld(new Vector3d(z.z, z.x, z.y), Vector3d.Zero, out Vector3d world, out _);
            return world;
        }

        /// <summary>
        /// Start of the frame (the colliders are already at their new poses): the carried
        /// point's new position. Also measures the platform velocity over the elapsed time.
        /// </summary>
        public bool TryApply(Transform zone, SiteFrame zoneFrame, double time, out Vector3d worldPosition)
        {
            worldPosition = Vector3d.Zero;
            if (platform == null)
            {
                return false;
            }

            Vector3 zonePoint = zone.InverseTransformPoint(platform.TransformPoint(localPoint));
            double dt = time - poseTime;
            if (dt > 1e-6d)
            {
                Vector3 d = zonePoint - lastZonePoint;
                PlatformVelocity = ((zoneFrame.East * d.z) + (zoneFrame.North * d.x) + (zoneFrame.Up * d.y)) * (1d / dt);
            }

            worldPosition = FromZone(zoneFrame, zonePoint);
            lastZonePoint = zonePoint;
            poseTime = time;
            return true;
        }

        /// <summary>
        /// End of the frame (the colliders are still at the poses the step used): choose the
        /// platform and store the player's pose in its local space. poseTime — the time the
        /// colliders' poses belong to (the start of the frame).
        /// </summary>
        public void Capture(Transform zone, SiteFrame zoneFrame, double poseTime, Vector3d playerWorld,
            Vector3d playerVelocity, bool grounded, int groundSourceId, VesselSupport vessel)
        {
            Vector3 zonePoint = ToZone(zoneFrame, playerWorld);
            Vector3 renderPoint = zone.TransformPoint(zonePoint);

            Transform next = null;
            if (grounded)
            {
                if (groundSourceId >= VesselSupport.SourceIdBase)
                {
                    vessel.TryGetPlatform(groundSourceId, out next);
                }
            }
            else if (platform != null && InsideBounds(platform, renderPoint))
            {
                next = platform;
            }
            else
            {
                next = EnclosingPlatform(zone, renderPoint);
            }

            if (next == null)
            {
                platform = null;
                return;
            }

            if (platform == null)
            {
                PlatformVelocity = Vector3d.Zero;
            }

            platform = next;
            localPoint = platform.InverseTransformPoint(renderPoint);
            lastZonePoint = zonePoint;
            this.poseTime = poseTime;

            zoneFrame.ToLocal(playerWorld, playerVelocity, out _, out Vector3d relativeToSite);
            RelativeVelocity = FromAxes(zoneFrame, relativeToSite) - PlatformVelocity;
        }

        private bool InsideBounds(Transform root, Vector3 point)
        {
            if (!colliderCache.TryGetValue(root, out Collider[] colliders) || colliders == null || colliders.Length == 0)
            {
                if (colliderCache.Count > 32)
                {
                    colliderCache.Clear();
                }

                colliders = root.GetComponentsInChildren<Collider>(false);
                colliderCache[root] = colliders;
            }

            bool any = false;
            Bounds bounds = default(Bounds);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null || !colliders[i].enabled)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = colliders[i].bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(colliders[i].bounds);
                }
            }

            if (!any)
            {
                return false;
            }

            bounds.Expand(BoundsMarginMeters * 2f);
            return bounds.Contains(point);
        }

        /// <summary>The player is airborne inside a vessel part: a probe up and a probe down hit the same part.</summary>
        private static Transform EnclosingPlatform(Transform zone, Vector3 feet)
        {
            Vector3 up = zone.up;
            Vector3 origin = feet + (up * EnclosedProbeHeight);
            if (!Physics.Raycast(origin, up, out RaycastHit above, EnclosedProbeMeters, ~0, QueryTriggerInteraction.Ignore)
                || !Physics.Raycast(origin, -up, out RaycastHit below, EnclosedProbeMeters, ~0, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            Transform a = VesselSupport.PlatformOf(above.collider);
            return a != null && a == VesselSupport.PlatformOf(below.collider) ? a : null;
        }
    }
}
