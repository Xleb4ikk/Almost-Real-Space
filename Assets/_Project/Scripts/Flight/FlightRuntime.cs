using System;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
using Galilego.Tablet;
using Galilego.Universe;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Galilego.Flight
{
    /// <summary>
    /// Flight runtime: player input → model commands; inside the CONTACT ZONE (PhysX) it applies
    /// engine/nozzle forces at their real points (AddForceAtPosition), updates body masses as
    /// propellant burns and cuts joints on staging/abort; outside the zone force/torque go through
    /// VesselStep (n-body, 6-DOF). The two modes are joined by HandoffOut/HandoffIn: attitude and
    /// angular velocity move across without a seam.
    ///
    /// Keys (in the ship): throttle — LeftShift/LeftCtrl, Z — 100 %, X — 0 %;
    /// W/S — pitch, A/D — yaw, Q/E — roll; I/K, J/L, U/O — RCS translation; T — attitude
    /// hold (SAS), G — RCS on/off; F1…F8 — engine #n on/off, F9/F10 — all on/off;
    /// Space — stage (ignition/separation); Backspace — LES ABORT; Delete — LES jettison.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class FlightRuntime : MonoBehaviour
    {
        public static FlightRuntime Instance { get; private set; }

        public SimulationRunner Runner;
        public ContactZoneHost Zone;
        public bool ShowHud = true;

        private VesselFlightModel model;
        private GameObject lesVisual;

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
            Instance.BuildLesVisual();
            return Instance;
        }

        public static void End()
        {
            if (Instance == null) return;
            if (Instance.lesVisual != null) Destroy(Instance.lesVisual);
            Instance.model = null;
        }

        // ─── Input ────────────────────────────────────────────────────────────

        private void Update()
        {
            if (model == null || Runner == null || Runner.Paused) return;
            ReadInput();
            ExecuteRequests();
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

            c.Pitch = Axis(FKey.W, FKey.S);
            c.Yaw = Axis(FKey.D, FKey.A);
            c.Roll = Axis(FKey.E, FKey.Q);
            c.Translate = new Vector3d(Axis(FKey.L, FKey.J), Axis(FKey.I, FKey.K), Axis(FKey.O, FKey.U));

            if (Down(FKey.Z)) Runner.RawThrottle = 1d;
            if (Down(FKey.X)) Runner.RawThrottle = 0d;
            if (Down(FKey.T)) { c.SasOn = !c.SasOn; model.HoldCurrentAttitude(); }
            if (Down(FKey.G)) c.RcsOn = !c.RcsOn;
            for (int i = 0; i < 8; i++)
                if (Down((FKey)((int)FKey.F1 + i))) model.ToggleEngine(i);
            if (Down(FKey.F9)) model.SetAllEngines(true);
            if (Down(FKey.F10)) model.SetAllEngines(false);
            if (Down(FKey.Backspace) && model.TriggerAbort()) Debug.Log("[САС] АВАРИЙНЫЙ УВОД");
            if (Down(FKey.Delete) && model.RequestJettison()) Debug.Log("[САС] отстрел башни");
        }

        // ─── Separation / LES ─────────────────────────────────────────────────

        private void ExecuteRequests()
        {
            if (model.SplitPending)
            {
                model.ConsumeSplit();
                RemoveAndSplit(model.AbortKeep, false);
                Debug.Log("[САС] капсула отделена от ракеты");
            }

            if (model.JettisonPending && model.LesPart >= 0)
            {
                var keep = new bool[model.Defs.Length];
                for (int i = 0; i < keep.Length; i++) keep[i] = i != model.LesPart;
                model.ConsumeJettison();
                RemoveAndSplit(keep, true);
                Debug.Log("[САС] башня отстрелена");
            }
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

        private void RemoveAndSplit(bool[] keep, bool jettison)
        {
            Spacecraft.Spacecraft ship = Runner.Ship;
            model.RefreshMassProps();
            Vector3d oldCom = model.Com;
            QuaternionD q = ship.Attitude;
            double kickImpulse = 0d;
            if (jettison)
                foreach (var t in model.Thrusters)
                    if (t.Kind == ThrusterKind.JettisonMotor) kickImpulse += t.OwnPropKg * t.IspVacS * Thruster.G0;

            double removed = model.RemoveParts(keep, out Vector3d removedCom);
            if (Runner.Propulsion != null)
                ship.Mass = Runner.Propulsion.AttachedTotalKg() + Runner.Propulsion.DetachedMassKg;
            else
                ship.Mass -= removed;

            if (ContactZoneHost.Active)
            {
                // Zone: joints are severed, PhysX does the rest (abort/jettison via real nozzle forces).
                Zone.Proxy.SeverForStaging(model.BodyKeepMask(), Vector3.up, 0f);
                return;
            }

            // Free flight: what separated becomes debris at the same velocity; the remaining COM shifts.
            Vector3d up = q.Rotate(new Vector3d(0d, 1d, 0d));
            Vector3d debrisVelocity = ship.Velocity;
            if (jettison && removed > 0d)
            {
                // Jettison motor: impulse J separates the tower (+Y) and the capsule (−Y) by conservation of momentum.
                debrisVelocity += up * (kickImpulse / removed);
                ship.Velocity -= up * (kickImpulse / Math.Max(1d, ship.Mass));
                foreach (var t in model.Thrusters)
                    if (t.Kind == ThrusterKind.JettisonMotor) { t.OwnPropKg = 0d; t.Ignited = false; t.Level = 0d; }
            }

            if (Runner.Debris != null && removed > 0d)
                Runner.Debris.Spawn(ship.Position + q.Rotate(removedCom - oldCom), debrisVelocity, removed,
                    Runner.TimeSeconds, Runner.DebrisLifetimeSeconds);
            model.RefreshMassProps();
            ship.Position += q.Rotate(model.Com - oldCom);
            Runner.SetVesselParts(model.RemainingDefs());
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
                bool gone = model.IsGone(t.PartIndex);
                if (gone && t.Kind != ThrusterKind.JettisonMotor) continue;
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

        // ─── LES visual (placeholder: no model yet) ──────────────────────────

        private void BuildLesVisual()
        {
            if (lesVisual != null) Destroy(lesVisual);
            lesVisual = null;
            if (model == null || model.LesPart < 0) return;
            PartDefinition d = model.Defs[model.LesPart];
            lesVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lesVisual.name = "LES_placeholder";
            Destroy(lesVisual.GetComponent<Collider>());
            lesVisual.transform.localScale = new Vector3(2f * d.Dimensions.x, 0.5f * d.Dimensions.y, 2f * d.Dimensions.x);
        }

        private void LateUpdate()
        {
            if (model == null || lesVisual == null) return;
            Rigidbody rb = Zone != null && ContactZoneHost.Active ? Zone.Proxy.BodyAt(model.BodyOfPart(model.LesPart)) : null;
            lesVisual.SetActive(rb != null);
            if (rb != null) lesVisual.transform.SetPositionAndRotation(rb.position, rb.rotation);
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

            if (model.LesPart >= 0 || model.Aborting)
                s += "САС: " + (model.Aborting ? "УВОД" : model.LesArmed ? "готова (Backspace — увод)" : "отстрелена") + "\n";
            GUI.Label(new Rect(12, 12, 520, 260), s);
        }

        // ─── Keys ─────────────────────────────────────────────────────────

        private enum FKey { W, A, S, D, Q, E, I, J, K, L, U, O, T, G, Z, X, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, Backspace, Delete }

        private static double Axis(FKey plus, FKey minus)
        {
            return (Held(plus) ? 1d : 0d) - (Held(minus) ? 1d : 0d);
        }

#if ENABLE_INPUT_SYSTEM
        private static Key Map(FKey k)
        {
            switch (k)
            {
                case FKey.W: return Key.W; case FKey.A: return Key.A; case FKey.S: return Key.S; case FKey.D: return Key.D;
                case FKey.Q: return Key.Q; case FKey.E: return Key.E; case FKey.I: return Key.I; case FKey.J: return Key.J;
                case FKey.K: return Key.K; case FKey.L: return Key.L; case FKey.U: return Key.U; case FKey.O: return Key.O;
                case FKey.T: return Key.T; case FKey.G: return Key.G; case FKey.Z: return Key.Z; case FKey.X: return Key.X;
                case FKey.F1: return Key.F1; case FKey.F2: return Key.F2; case FKey.F3: return Key.F3; case FKey.F4: return Key.F4;
                case FKey.F5: return Key.F5; case FKey.F6: return Key.F6; case FKey.F7: return Key.F7; case FKey.F8: return Key.F8;
                case FKey.F9: return Key.F9; case FKey.F10: return Key.F10;
                case FKey.Backspace: return Key.Backspace; default: return Key.Delete;
            }
        }

        private static bool Held(FKey k) { Keyboard kb = Keyboard.current; return kb != null && kb[Map(k)].isPressed; }
        private static bool Down(FKey k) { Keyboard kb = Keyboard.current; return kb != null && kb[Map(k)].wasPressedThisFrame; }
#else
        private static KeyCode Map(FKey k)
        {
            switch (k)
            {
                case FKey.Backspace: return KeyCode.Backspace;
                case FKey.Delete: return KeyCode.Delete;
                case FKey.F9: return KeyCode.F9;
                case FKey.F10: return KeyCode.F10;
                default:
                    if (k >= FKey.F1 && k <= FKey.F8) return KeyCode.F1 + (k - FKey.F1);
                    return (KeyCode)Enum.Parse(typeof(KeyCode), k.ToString());
            }
        }

        private static bool Held(FKey k) { return Input.GetKey(Map(k)); }
        private static bool Down(FKey k) { return Input.GetKeyDown(Map(k)); }
#endif
    }
}
