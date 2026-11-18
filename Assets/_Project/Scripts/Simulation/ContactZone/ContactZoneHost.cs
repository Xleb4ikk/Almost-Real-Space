// Contact zone (vertical slice).
// Role: owns the contact zone. Outside the zone it does nothing and does not touch ship state.
// Enter/exit only through ContactZoneGate (pure logic, tests Test300-305).
// Game code changes by a single line: SimulationRunner.Update skips the step
// when ContactZoneHost.Active == true (see SLICE_PLAN.md, "Connection point").
// Inside the zone the gate and the double world work in WORLD coordinates: the proxy's
// local COM is converted through the current site frame (frame.At(t)).
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Events;
using Galilego.Universe;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Simulation.ContactZone
{
    [DefaultExecutionOrder(-50)]
    public sealed class ContactZoneHost : MonoBehaviour
    {
        public SimulationRunner Runner;
        public ContactZoneProxy Proxy;
        public TerrainPatch Patch;

        [Tooltip("Отладка: логировать входы и выходы.")]
        public bool Log = true;

        [Tooltip("Включена ли зона. Включением/выключением управляет режим строительства " +
            "(физика аппарата по R); в тестовой сцене стоит включённой всегда.")]
        public bool Enabled = true;

        public static bool Active { get; private set; }

        private readonly ContactZoneGate gate = new ContactZoneGate();
        private SiteFrame frame;

        /// <summary>The site frame the zone works in (the zone root's axes). Diagnostics only.</summary>
        public SiteFrame ZoneFrame => frame;

        /// <summary>The site frame at the given simulation time (the zone root's axes).</summary>
        public SiteFrame ZoneFrameAt(double timeSeconds)
        {
            return frame != null ? frame.At(timeSeconds) : null;
        }

        /// <summary>From the center of mass to the ship point along local Up (m). In game
        /// Ship.Position is the ship's bottom (touch point), not the COM: without this
        /// offset the ship jumps to COM height on zone enter and exit.</summary>
        private double anchorUp;

        [Tooltip("Доля HalfSize: ближе к центру патч не трогаем, дальше — пересобираем вокруг корабля.")]
        public float PatchRecenterFraction = 0.4f;

        [Tooltip("Не чаще, с: пересборка = кукинг меша и потеря contact manifold на кадр.")]
        public float PatchRebuildIntervalSeconds = 0.5f;

        private double lastPatchRebuildTime = double.NegativeInfinity;
        private Vector2 patchCenterLocal;

        /// <summary>
        /// Forced exit from the zone without writing ship state back (a writing Exit
        /// would leave the ship where PhysX had it — wrong when rolling back to build
        /// mode, which restores its own state). The proxy bodies are destroyed, so no
        /// debris from broken joints is left behind.
        /// </summary>
        public void ForceExit()
        {
            if (!Active)
            {
                gate.Reset();
                return;
            }

            Proxy.Clear();
            Patch.Clear();
            Active = false;
            gate.Reset();
            if (Log) Debug.Log("[ContactZone] принудительный выход");
        }

        private void LateUpdate()
        {
            if (Runner == null || Runner.Ship == null || Runner.DominantBody == null)
            {
                return;
            }

            // A disabled zone does not touch the ship at all (build mode: the vessel
            // is static, the physics step in SimulationRunner is skipped by mode).
            if (!Enabled)
            {
                if (Active)
                {
                    ForceExit();
                }

                return;
            }

            // Empty assembly: there would be no bodies, and SimulationRunner would
            // skip the step on Active — the ship would hang without physics. The zone
            // does not enter in that case.
            if (Runner.Parts == null || Runner.Parts.Length == 0)
            {
                if (Active)
                {
                    ForceExit();
                }

                return;
            }

            OrbitingBody body = Runner.DominantBody;
            ITerrainModel terrain = body.Terrain;
            if (terrain == null)
            {
                return;
            }

            double t = Runner.TimeSeconds;
            Ship ship = Runner.Ship;

            if (!Active)
            {
                if (gate.Update(body, terrain, ship.Position, ship.Velocity, t))
                {
                    Enter(body, terrain, ship, t);
                }

                return;
            }

            // Inside the zone the gate and the double world need WORLD coordinates of
            // the SHIP POINT: subtract anchorUp from the COM along local Up, then
            // convert through the current site frame (frame.At(t)), not the entry anchor.
            Proxy.GetCenterOfMassState(frame, t, out Vector3d com, out Vector3d comVelocity);
            Vector3d anchor = new Vector3d(com.X, com.Y, com.Z - anchorUp);
            SiteFrame current = frame.At(t);
            current.ToWorld(anchor, comVelocity, out Vector3d worldPosition, out Vector3d worldVelocity);
            if (!gate.Update(body, terrain, worldPosition, worldVelocity, t))
            {
                Exit(ship, t);
                return;
            }

            // Zone pose: the proxy and patch live in the site frame axes — every frame
            // the zone root is pinned to the frame (the planet rotates and the floating
            // origin follows the player; without this the plate contact would drift apart).
            ApplyPose(current, t);

            // Effective gravity of the site frame (in its local axes) — see SLICE_PLAN.md.
            Proxy.SetFrameGravity(EffectiveLocalGravity(body, current, t));

            // The patch follows the ship; build around the CURRENT frame, not the entry
            // anchor. Measure from the last patch center: the mesh is rebuilt around the
            // ship while the root stays at the frame origin, so a root-relative trigger
            // stays true forever (measured: 35+ rebuilds instead of ≈4).
            Vector3 shipLocal = transform.InverseTransformPoint(FloatingOrigin.ToRender(worldPosition));
            double patchDx = shipLocal.x - patchCenterLocal.x;
            double patchDz = shipLocal.z - patchCenterLocal.y;
            if (ContactZoneGate.ShouldRecenterPatch(patchDx, patchDz, Patch.HalfSize,
                    PatchRecenterFraction, t, lastPatchRebuildTime, PatchRebuildIntervalSeconds))
            {
                Patch.Build(body, terrain, current, t, new Vector3(shipLocal.x, 0f, shipLocal.z));
                patchCenterLocal = new Vector2(shipLocal.x, shipLocal.z);
                lastPatchRebuildTime = t;
                if (Log)
                {
                    Debug.Log("[CZ] патч пересобран: x=" + shipLocal.x.ToString("F1")
                        + " z=" + shipLocal.z.ToString("F1"));
                }
            }

            // The zone drives ship state: written back to the double world every frame.
            ship.Position = worldPosition;
            ship.Velocity = worldVelocity;
        }

        /// <summary>Zone root pose = site frame pose in render space.</summary>
        private void ApplyPose(SiteFrame frameAtTime, double t)
        {
            Vector3 position = FloatingOrigin.ToRender(frameAtTime.Origin);
            Quaternion rotation = SurfaceFrame.RenderRotation(
                Runner.DominantBody, frameAtTime.LatitudeDegrees, frameAtTime.LongitudeDegrees, t);
            transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();
        }

        private void Enter(OrbitingBody body, ITerrainModel terrain, Ship ship, double t)
        {
            frame = SiteFrame.Anchor(body, terrain, ship.Position, t);
            Patch.Build(body, terrain, frame, t);
            ApplyPose(frame, t);
            frame.ToLocal(ship.Position, ship.Velocity, out Vector3d lp, out Vector3d lv);
            // Zone local axes are (north, up, east); the entry patch is centered on the anchor.
            patchCenterLocal = new Vector2((float)lp.Y, (float)lp.X);
            bool centerOfMass = Runner.Flight != null && ship.PositionIsCom;
            Proxy.Build(Runner.Parts, frame, lp, lv, centerOfMass);
            if (centerOfMass)
            {
                // Re-entry from flight: the assembly puts its COM at the point and turns like the ship.
                Galilego.Flight.FlightRuntime.HandoffIn(Runner, Proxy, frame);
            }

            // Frame gravity is set immediately, not on the next frame: with world
            // gravity (0,−9.81,0) and the root rotated by site axes the bodies would
            // get almost sideways acceleration and fly off the pad.
            Proxy.SetFrameGravity(EffectiveLocalGravity(body, frame, t));

            // COM-to-ship-point offset: fixed at entry and used for the whole zone
            // session (both when writing state and on exit).
            Proxy.GetCenterOfMassState(frame, t, out Vector3d com, out _);
            anchorUp = com.Z - lp.Z;

            // Phase 0 measurement (temporary): the numbers from COLLISION_FIX_PLAN §4.1.
            if (Log)
            {
                Rigidbody b0 = Proxy.BodyAt(0);
                // The zone lives in render space: the expected velocity is the site-relative
                // local velocity rotated by the zone root, WITHOUT the frame origin velocity.
                // Site axes (east, north, up) → Unity local axes (north, up, east).
                Vector3 expected = transform.TransformDirection(
                    new Vector3((float)lv.Y, (float)lv.Z, (float)lv.X));
                Vector3 bodyVelocity = b0 != null ? b0.linearVelocity : Vector3.zero;
                double dv = b0 != null ? (bodyVelocity - expected).magnitude : -1d;
                Debug.Log("[CZ-IN] lat=" + frame.LatitudeDegrees.ToString("F5")
                    + " lon=" + frame.LongitudeDegrees.ToString("F5")
                    + " root.rot=" + transform.rotation.eulerAngles
                    + " gravity=" + Physics.gravity
                    + " iters_global=" + Physics.defaultSolverIterations
                    + " iters_body0=" + (b0 != null ? b0.solverIterations : -1)
                    + " com_zone=" + com
                    + " com_flight=" + (Runner.Flight != null ? Runner.Flight.Com : Vector3d.Zero)
                    + " v_body0=" + bodyVelocity
                    + " v_expected=" + expected
                    + " dv=" + dv.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                    + " com_physx0=" + (b0 != null ? b0.worldCenterOfMass : Vector3.zero)
                    + " maxAngular=" + (b0 != null ? b0.maxAngularVelocity : -1f)
                    + " maxLinear=" + (b0 != null ? b0.maxLinearVelocity : -1f));
            }

            // Warp is forbidden in the zone: time runs on real dt (AdvanceTime), not
            // the warp factor; raised warp would jerk the ship on exit.
            Runner.Warp.SetWarpFactor(WarpController.MinWarpFactor);

            Active = true;
            if (Log) Debug.Log("[ContactZone] вход: " + frame.LatitudeDegrees + ", " + frame.LongitudeDegrees);
        }

        private void Exit(Ship ship, double t)
        {
            Proxy.GetCenterOfMassState(frame, t, out Vector3d com, out Vector3d comVelocity);
            // 6-DOF flight model: outside the zone the ship's Position is the CENTER OF MASS
            // (not the bottom), and the orientation and angular velocity of the PhysX bodies go with it.
            bool flight = Runner.Flight != null;
            Vector3d anchor = flight ? com : new Vector3d(com.X, com.Y, com.Z - anchorUp);
            frame.At(t).ToWorld(anchor, comVelocity, out Vector3d wp, out Vector3d wv);
            ship.Position = wp;
            ship.Velocity = wv;
            if (flight)
            {
                Galilego.Flight.FlightRuntime.HandoffOut(Runner, Proxy, frame.At(t));
            }

            Proxy.Clear();
            Patch.Clear();
            Active = false;
            if (Log) Debug.Log("[ContactZone] выход");
        }

        // Effective acceleration of the site frame (gravity of ALL bodies at the point − origin acceleration −
        // centrifugal), in site axes (east, north, up); the Unity conversion is in ContactZoneProxy.
        // One shared definition with the walking player: see SurfaceGravity.
        private Vector3d EffectiveLocalGravity(OrbitingBody body, SiteFrame f, double t)
        {
            return SurfaceGravity.EffectiveLocal(Runner.SystemState, body, f, t);
        }
    }
}
