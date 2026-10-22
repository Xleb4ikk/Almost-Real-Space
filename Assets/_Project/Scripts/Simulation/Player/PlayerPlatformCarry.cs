using System;
using System.Collections.Generic;
using Galilego.Core;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// The player aboard a vessel ("cabin mode"). The player's state is kept IN THE VESSEL
    /// PART'S FRAME: Position/Velocity are the controller's local coordinates (x = east,
    /// y = north, z = up) whose axes are the part's own (east = forward, north = right,
    /// up = up, see SetPlatformFrame). The colliders the controller queries are the same
    /// transforms, so whatever the vessel does — falls, brakes, tilts, jumps to a new pose
    /// when the contact zone is left — the player moves with it exactly, with no conversion
    /// through the double world, the floating origin or the site frame (those bridges
    /// disagree by metres inside a moving cabin). The double-world position and velocity
    /// are only DERIVED from this state for everything else (anchor, orbit, UI).
    ///
    /// Zone mode additionally measures the platform's own acceleration (zone-root
    /// coordinates are body-fixed, so planet motion drops out), which the runner turns
    /// into the felt weight: gravity minus the vessel's acceleration.
    /// </summary>
    public sealed class PlayerPlatformCarry
    {
        private const double AccelerationFilterSeconds = 0.1d;
        private const float BoundsMarginMeters = 0.3f;

        private Transform platform;
        private readonly Dictionary<Transform, Collider[]> colliderCache = new Dictionary<Transform, Collider[]>();

        // Platform kinematics in zone-root axes (Unity: x north, y up, z east).
        private Transform kinematicsPlatform;
        private bool kinematicsValid;
        private Vector3 lastOriginZone;
        private double lastKinematicsTime;
        private Vector3 velocityZone;
        private Vector3 accelerationZone;

        /// <summary>Controller coordinates in the platform's frame (x east, y north, z up).</summary>
        public Vector3d Position;

        /// <summary>Velocity relative to the platform, same axes.</summary>
        public Vector3d Velocity;

        public bool Active
        {
            get { return platform != null; }
        }

        public Transform Platform
        {
            get { return platform; }
        }

        /// <summary>Platform velocity in zone-root axes (m/s), zone mode only.</summary>
        public Vector3 PlatformVelocityZone
        {
            get { return velocityZone; }
        }

        /// <summary>Platform acceleration in zone-root axes (m/s²), filtered, zone mode only.</summary>
        public Vector3 PlatformAccelerationZone
        {
            get { return accelerationZone; }
        }

        /// <summary>Unit axes of the frame in render space: east = forward, north = right, up = up.</summary>
        public static void Axes(Transform root, out Vector3 east, out Vector3 north, out Vector3 up)
        {
            east = root.forward.normalized;
            north = root.right.normalized;
            up = root.up.normalized;
        }

        public Vector3 RenderPoint()
        {
            Axes(platform, out Vector3 e, out Vector3 n, out Vector3 u);
            return platform.position + (e * (float)Position.X) + (n * (float)Position.Y) + (u * (float)Position.Z);
        }

        /// <summary>Render-space direction of a vector given in the platform's controller axes.</summary>
        public Vector3 RenderDirection(Vector3d local)
        {
            Axes(platform, out Vector3 e, out Vector3 n, out Vector3 u);
            return (e * (float)local.X) + (n * (float)local.Y) + (u * (float)local.Z);
        }

        /// <summary>Controller axes components of a render-space direction.</summary>
        public static Vector3d LocalDirection(Transform root, Vector3 renderDirection)
        {
            Axes(root, out Vector3 e, out Vector3 n, out Vector3 u);
            return new Vector3d(Vector3.Dot(renderDirection, e), Vector3.Dot(renderDirection, n), Vector3.Dot(renderDirection, u));
        }

        public static Vector3d LocalPoint(Transform root, Vector3 renderPoint)
        {
            return LocalDirection(root, renderPoint - root.position);
        }

        public bool TryGetRenderPoint(out Vector3 renderPoint)
        {
            if (platform == null)
            {
                renderPoint = Vector3.zero;
                return false;
            }

            renderPoint = RenderPoint();
            return true;
        }

        public bool TryGetUp(out Vector3 up)
        {
            if (platform == null)
            {
                up = Vector3.up;
                return false;
            }

            up = platform.up.normalized;
            return true;
        }

        /// <summary>Start carrying: the player is at renderPoint now, moving at relativeVelocity (platform axes).</summary>
        public void Attach(Transform root, Vector3 renderPoint, Vector3d relativeVelocity)
        {
            platform = root;
            Position = LocalPoint(root, renderPoint);
            Velocity = relativeVelocity;
            kinematicsValid = false;
        }

        /// <summary>Switch to another part of the same vessel without moving the player.</summary>
        public void Reframe(Transform next)
        {
            if (next == null || next == platform)
            {
                return;
            }

            Vector3 point = RenderPoint();
            Vector3 velocity = RenderDirection(Velocity);
            platform = next;
            Position = LocalPoint(next, point);
            Velocity = LocalDirection(next, velocity);
        }

        public void Reset()
        {
            platform = null;
            Position = Vector3d.Zero;
            Velocity = Vector3d.Zero;
            kinematicsValid = false;
            kinematicsPlatform = null;
            velocityZone = Vector3.zero;
            accelerationZone = Vector3.zero;
            colliderCache.Clear();
        }

        public void ResetKinematics()
        {
            kinematicsValid = false;
            velocityZone = Vector3.zero;
            accelerationZone = Vector3.zero;
        }

        /// <summary>
        /// Zone mode, once per frame: platform velocity and acceleration from its origin's motion
        /// in the (body-fixed) zone-root space. time — the time the colliders' poses belong to.
        /// </summary>
        public void UpdateZoneKinematics(Transform zone, double time)
        {
            if (platform == null || zone == null)
            {
                return;
            }

            Vector3 originZone = zone.InverseTransformPoint(platform.position);
            double dt = time - lastKinematicsTime;
            if (kinematicsValid && kinematicsPlatform == platform && dt > 1e-4d && dt < 0.5d)
            {
                Vector3 v = (originZone - lastOriginZone) / (float)dt;
                Vector3 a = (v - velocityZone) / (float)dt;
                float k = (float)Math.Min(1d, dt / AccelerationFilterSeconds);
                accelerationZone = Vector3.Lerp(accelerationZone, a, k);
                velocityZone = v;
            }
            else if (!(kinematicsValid && kinematicsPlatform == platform))
            {
                velocityZone = Vector3.zero;
                accelerationZone = Vector3.zero;
            }

            lastOriginZone = originZone;
            lastKinematicsTime = time;
            kinematicsPlatform = platform;
            kinematicsValid = true;
        }

        /// <summary>The render point is inside the platform's collider bounds (airborne inside the cabin).</summary>
        public bool InsideBounds(Vector3 point)
        {
            if (platform == null)
            {
                return false;
            }

            if (!colliderCache.TryGetValue(platform, out Collider[] colliders) || colliders == null || colliders.Length == 0)
            {
                if (colliderCache.Count > 32)
                {
                    colliderCache.Clear();
                }

                colliders = platform.GetComponentsInChildren<Collider>(false);
                colliderCache[platform] = colliders;
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
    }
}
