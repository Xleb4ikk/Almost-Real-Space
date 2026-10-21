using System;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
using Galilego.Tablet;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Flight
{
    /// <summary>
    /// Flight runtime: player input → model commands; inside the CONTACT ZONE (PhysX) it applies
    /// engine/nozzle forces at their real points (AddForceAtPosition), updates body masses as
    /// propellant burns and cuts joints on staging; outside the zone force/torque go through
    /// VesselStep (n-body, 6-DOF). The two modes are joined by HandoffOut/HandoffIn: attitude and
    /// angular velocity move across without a seam.
    ///
    /// Keys (in the ship): throttle — LeftShift/LeftCtrl, Z — 100 %, X — 0 %;
    /// W/S — pitch, A/D — yaw, Q/E — roll; I/K, J/L, U/O — RCS translation; T — attitude
    /// hold (SAS), G — RCS on/off; F1…F8 — engine #n on/off, F9/F10 — all on/off;
    /// Space — stage (ignition/separation).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class FlightRuntime : MonoBehaviour
    {
        public static FlightRuntime Instance { get; private set; }

        public SimulationRunner Runner;
        public ContactZoneHost Zone;
        public bool ShowHud = true;

        private VesselFlightModel model;

        public static FlightRuntime Begin(SimulationRunner runner, ContactZoneHost zone, VesselFlightModel model)
        {
            if (Instance == null)
            {
                var go = new GameObject("FlightRuntime");
                Instance = go.AddComponent<FlightRuntime>();
            }

            Instance.Runner = runner;
            Instance.Zone = zone;
            Instance.model = model;
            return Instance;
        }

        public static void End()
        {
            if (Instance == null) return;
            Instance.model = null;
        }

        // ─── Input ────────────────────────────────────────────────────────────

        private void Update()
        {
            if (model == null || Runner == null || Runner.Paused) return;
            ReadInput();
        }

        private void ReadInput()
        {
            FlightControls c = model.Controls;
            bool seated = Runner.PlayerMode == PlayerMode.InShip && !ShipTablet.AnyExpanded;
            if (!seated)
            {
                c.Pitch = c.Yaw = c.Roll = 0d; c.Translate = Vector3d.Zero;
                return;
            }

            c.Pitch = Axis(GameAction.PitchUp, GameAction.PitchDown);
            c.Yaw = Axis(GameAction.YawRight, GameAction.YawLeft);
            c.Roll = Axis(GameAction.RollRight, GameAction.RollLeft);
            c.Translate = new Vector3d(Axis(GameAction.TransRight, GameAction.TransLeft),
                Axis(GameAction.TransUp, GameAction.TransDown), Axis(GameAction.TransForward, GameAction.TransBack));

            if (PlayerInput.Down(GameAction.ThrottleFull)) Runner.RawThrottle = 1d;
            if (PlayerInput.Down(GameAction.ThrottleCut)) Runner.RawThrottle = 0d;
            if (PlayerInput.Down(GameAction.Sas)) { c.SasOn = !c.SasOn; model.HoldCurrentAttitude(); }
            if (PlayerInput.Down(GameAction.Rcs)) c.RcsOn = !c.RcsOn;
            for (int i = 0; i < 8; i++)
                if (PlayerInput.Down((GameAction)((int)GameAction.Engine1 + i))) model.ToggleEngine(i);
            if (PlayerInput.Down(GameAction.EnginesAllOn)) model.SetAllEngines(true);
            if (PlayerInput.Down(GameAction.EnginesAllOff)) model.SetAllEngines(false);
        }

        /// <summary>Staging separation (called by SpacecraftBuilder after Propulsion.Stage()).</summary>
        public void OnStaged()
        {
            if (model == null) return;
            model.HoldCurrentAttitude();
            if (ContactZoneHost.Active)
            {
                Runner.Ship.Mass = Runner.Propulsion.AttachedTotalKg() + Runner.Propulsion.DetachedMassKg;
                Zone.Proxy.SeverForStaging(model.BodyKeepMask(), Vector3.down, 1.5f);
            }
            else
            {
                // Free flight: the remaining COM shifts — Position (which is the COM) moves with it.
                Vector3d oldCom = model.Com;
                model.RefreshMassProps();
                Runner.Ship.Position += Runner.Ship.Attitude.Rotate(model.Com - oldCom);
                Runner.SetVesselParts(model.RemainingDefs());
            }
        }

        // ─── Contact zone physics (PhysX) ────────────────────────────────────

        private void FixedUpdate()
        {
            if (model == null || Runner == null || Runner.Paused || !ContactZoneHost.Active || Zone == null) return;
            ContactZoneProxy proxy = Zone.Proxy;
            int rootBody = proxy.RootBodyIndex();
            if (rootBody < 0) return;

            float dt = Time.fixedDeltaTime;
            Rigidbody root = proxy.BodyAt(rootBody);
            Transform zt = proxy.transform;
            Quaternion qz = Quaternion.Inverse(zt.rotation) * root.rotation;      // ship → zone axes
            Vector3 wBody = Quaternion.Inverse(root.rotation) * root.angularVelocity;
            double pressure = Runner.AmbientPressurePa(Runner.Ship.Position, Runner.TimeSeconds);
            model.Prepare(dt, new QuaternionD(qz.x, qz.y, qz.z, qz.w), new Vector3d(wBody.x, wBody.y, wBody.z),
                Runner.RawThrottle, pressure, true);

            // Forces at real points: application point and axis are in the host-part frame.
            foreach (Thruster t in model.Thrusters)
            {
                if (t.Level <= 0d) continue;
                if (model.IsGone(t.PartIndex)) continue;
                Rigidbody rb = proxy.BodyAt(model.BodyOfPart(t.PartIndex));
                if (rb == null) continue;
                PartDefinition d = model.Defs[t.PartIndex];
                Vector3 local = new Vector3(
                    (float)t.Position.X - d.Offset.x, (float)t.Position.Y - d.Offset.y, (float)t.Position.Z - d.Offset.z);
                Vector3d dir = t.CurrentDirection();
                Vector3 worldDir = rb.transform.TransformDirection(new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z));
                rb.AddForceAtPosition(worldDir * (float)t.ThrustAt(pressure), rb.transform.TransformPoint(local), ForceMode.Force);
            }

            // Flow and mass: stage pools and hardware → body masses (PhysX recomputes COM/inertia).
            double main = model.MainBurnKg(dt);
            model.Finish(dt);
            if (Runner.Propulsion != null)
            {
                Runner.Propulsion.CommitBurn(main);
                Runner.Ship.Mass = Runner.Propulsion.AttachedTotalKg() + Runner.Propulsion.DetachedMassKg;
            }

            for (int b = 0; b < proxy.BodyCount; b++)
            {
                int part = model.PartOfBody[b];
                if (model.IsGone(part)) continue;
                Rigidbody rb = proxy.BodyAt(b);
                if (rb != null) rb.mass = Mathf.Max(1f, (float)model.PartMassKg(part));
            }
        }

        // ─── Zone ⇄ n-body handoff ───────────────────────────────────────────

        /// <summary>Leaving the zone: ship attitude and angular velocity from the PhysX bodies (before Proxy.Clear).</summary>
        public static void HandoffOut(SimulationRunner runner, ContactZoneProxy proxy, SiteFrame frame)
        {
            int rootBody = proxy.RootBodyIndex();
            Rigidbody body = rootBody >= 0 ? proxy.BodyAt(rootBody) : null;
            if (body == null) return;

            Transform pt = proxy.transform;
            Quaternion qLocal = Quaternion.Inverse(pt.rotation) * body.rotation;
            Vector3 wLocal = pt.InverseTransformDirection(body.angularVelocity);
            Vector3d bx = ToAstro(frame, qLocal * Vector3.right);
            Vector3d by = ToAstro(frame, qLocal * Vector3.up);
            Vector3d bz = ToAstro(frame, qLocal * Vector3.forward);
            QuaternionD att = AttitudeMath.FromAxes(bx, by, bz);
            Vector3d wAstro = ToAstro(frame, wLocal) + frame.AngularVelocity;   // + planet rotation
            Spacecraft.Spacecraft ship = runner.Ship;
            ship.Attitude = att;
            ship.AngularVelocity = att.Conjugated.Rotate(wAstro);
            ship.PositionIsCom = true;
            if (runner.Flight != null)
            {
                runner.Flight.HoldCurrentAttitude();
                runner.SetVesselParts(runner.Flight.RemainingDefs());
            }
        }

        /// <summary>Entering the zone from flight: the assembly (already built by Proxy.Build around the COM) is oriented and spun up.</summary>
        public static void HandoffIn(SimulationRunner runner, ContactZoneProxy proxy, SiteFrame frame)
        {
            Spacecraft.Spacecraft ship = runner.Ship;
            QuaternionD att = ship.Attitude.Normalized;
            Vector3 fwd = ToLocal(frame, att.Rotate(new Vector3d(0d, 0d, 1d)));
            Vector3 upv = ToLocal(frame, att.Rotate(new Vector3d(0d, 1d, 0d)));
            Vector3d wAstro = att.Rotate(ship.AngularVelocity) - frame.AngularVelocity;
            proxy.ApplyRigidState(Quaternion.LookRotation(fwd, upv), ToLocal(frame, wAstro));
            if (runner.Flight != null) runner.Flight.HoldCurrentAttitude();
        }

        // Zone axes (Unity): x — north, y — up, z — east.
        private static Vector3d ToAstro(SiteFrame f, Vector3 v)
        {
            return (f.North * v.x) + (f.Up * v.y) + (f.East * v.z);
        }

        private static Vector3 ToLocal(SiteFrame f, Vector3d a)
        {
            return new Vector3((float)Vector3d.Dot(a, f.North), (float)Vector3d.Dot(a, f.Up), (float)Vector3d.Dot(a, f.East));
        }

        // ─── Diagnostics ─────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!ShowHud || model == null || Runner == null) return;
            StagePropulsion p = Runner.Propulsion;
            string s = string.Format("Газ {0:F0}%   Масса {1:F0} кг   ступень {2}   SAS {3}  РСУ {4}\n",
                Runner.RawThrottle * 100d, Runner.Ship.Mass, p != null ? p.ActiveStageIndex + 1 : 0,
                model.Controls.SasOn ? "вкл" : "выкл", model.Controls.RcsOn ? "вкл" : "выкл");
            int k = 0;
            foreach (Thruster t in model.Thrusters)
            {
                if (t.Kind != ThrusterKind.Main) continue;
                s += string.Format("F{0} {1}: {2} {3:F0}%  подвес {4:F1}°/{5:F1}°\n", k + 1, t.Name,
                    t.Enabled ? "вкл" : "выкл", t.Level * 100d, t.GimbalX * 57.2958d, t.GimbalZ * 57.2958d);
                k++;
            }

            GUI.Label(new Rect(12, 12, 520, 260), s);
        }

        // ─── Keys ─────────────────────────────────────────────────────────

        private static double Axis(GameAction plus, GameAction minus)
        {
            return (PlayerInput.Held(plus) ? 1d : 0d) - (PlayerInput.Held(minus) ? 1d : 0d);
        }
    }
}
