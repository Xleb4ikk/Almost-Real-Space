using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;

namespace Galilego.Flight
{
    /// <summary>
    /// Single 6-DOF flight model of a vessel (no Unity). Holds the real engines/nozzles with their
    /// application points, computes COM and inertia tensor from the CURRENT part masses (propellant
    /// burns proportionally per stage), total force and torque (about the COM),
    /// flows, the autopilot (attitude hold: engine gimbals + RCS).
    /// One step: Prepare (commands→throttle levels, force/torque cache) → integrator/PhysX →
    /// Finish (hardware propellant bookkeeping). Force/torque are cached per chunk — the same
    /// control-tick granularity as throttle elsewhere in the project (×3 physics matches ×1).
    /// </summary>
    public sealed class VesselFlightModel
    {
        public const double Eps = 1e-4d;

        public readonly PartDefinition[] Defs;
        public readonly List<Thruster> Thrusters = new List<Thruster>();
        public readonly FlightControls Controls = new FlightControls();
        public StagePropulsion Propulsion;

        // ─ autopilot parameters ─
        public double MaxManualRateRadS = 0.35d;
        public double ManualAlphaRadS2 = 0.3d;
        public double HoldOmega = 0.8d;      // hold natural frequency, rad/s
        public double RcsDeadbandRadS2 = 0.004d;
        public double HoldCaptureRadS = 0.01d;   // below this rotation rate the hold target is captured
        public bool EnforceMinThrottle = true;

        // ─ part data ─
        private readonly double[] dry, propStart, hwDry;
        private readonly int[] stageOf;
        private readonly bool[] gone;
        private readonly List<Part> scratch = new List<Part>();

        // ─ chunk cache ─
        public double MassKg { get; private set; }
        public Vector3d Com { get; private set; }
        public Matrix3x3 Inertia { get; private set; } = Matrix3x3.Identity;
        public Vector3d ForceBody { get; private set; }
        public Vector3d TorqueBody { get; private set; }
        public double FlowTotal { get; private set; }
        public double FlowAux { get; private set; }
        public double PressurePa { get; private set; }

        // ─ autopilot ─
        private QuaternionD holdTarget = QuaternionD.Identity;
        private bool holdValid;

        public VesselFlightModel(PartDefinition[] defs, double[] dryKg, double[] propStartKg, int[] stages, double[] hardwareDryKg)
        {
            Defs = defs ?? throw new ArgumentNullException(nameof(defs));
            dry = dryKg; propStart = propStartKg; stageOf = stages; hwDry = hardwareDryKg;
            gone = new bool[defs.Length];
            PartOfBody = new int[defs.Length];
            for (int i = 0; i < defs.Length; i++) PartOfBody[i] = i;
        }

        /// <summary>Proxy body b ↔ model part PartOfBody[b] (changes when the remainder is rebuilt).</summary>
        public int[] PartOfBody;

        public int BodyOfPart(int part)
        {
            for (int b = 0; b < PartOfBody.Length; b++) if (PartOfBody[b] == part) return b;
            return -1;
        }

        /// <summary>Mask of proxy bodies that stay with the ship (for SeverForStaging).</summary>
        public bool[] BodyKeepMask()
        {
            var keep = new bool[PartOfBody.Length];
            for (int b = 0; b < keep.Length; b++) keep[b] = !IsGone(PartOfBody[b]);
            return keep;
        }

        /// <summary>
        /// Remainder description for the contact zone/landing: masses are CURRENT (burnt propellant
        /// taken into account), parent indices remapped; PartOfBody is updated to the new body numbering.
        /// </summary>
        public PartDefinition[] RemainingDefs()
        {
            var map = new int[Defs.Length];
            var list = new List<PartDefinition>();
            var parts = new List<int>();
            for (int i = 0; i < Defs.Length; i++)
            {
                if (IsGone(i)) { map[i] = -1; continue; }
                map[i] = list.Count; parts.Add(i);
                PartDefinition d = Defs[i];
                list.Add(new PartDefinition
                {
                    Name = d.Name, MassKg = PartMassKg(i), JointStrengthNewtons = d.JointStrengthNewtons,
                    BreakTorqueNm = d.BreakTorqueNm, Offset = d.Offset, Shape = d.Shape, Dimensions = d.Dimensions,
                    Orientation = d.Orientation, ParentIndex = d.ParentIndex, JointAnchor = d.JointAnchor,
                    EngineOffset = d.EngineOffset, PlayerPassthrough = d.PlayerPassthrough,
                });
            }

            for (int n = 0; n < list.Count; n++)
            {
                int p = list[n].ParentIndex;
                list[n].ParentIndex = p >= 0 && p < map.Length ? map[p] : -1;
            }

            PartOfBody = parts.ToArray();
            return list.ToArray();
        }

        // ─── Masses ──────────────────────────────────────────────────────────

        public bool IsGone(int i)
        {
            if (i < 0 || i >= gone.Length) return true;
            if (gone[i]) return true;
            return stageOf[i] >= 0 && Propulsion != null && Propulsion.IsJettisoned(stageOf[i]);
        }

        private double Fill(int stage)
        {
            if (Propulsion == null || stage < 0 || stage >= Propulsion.StageCount) return 1d;
            StageCore c = Propulsion.Cores[stage];
            double start = c.StartPropellantKg;
            return start > 0d ? Math.Min(1d, c.PropellantKg / start) : 1d;
        }

        public double PartMassKg(int i)
        {
            double m = dry[i] + hwDry[i] + (propStart[i] * Fill(stageOf[i]));
            for (int k = 0; k < Thrusters.Count; k++)
            {
                Thruster t = Thrusters[k];
                if (t.PartIndex == i && t.Kind != ThrusterKind.Main) m += t.OwnPropKg;
            }
            return m;
        }

        /// <summary>Hardware mass (not part of the stage cores): in Ship.Mass but not in the cores.</summary>
        public double HardwareMassKg()
        {
            double s = 0d;
            for (int i = 0; i < Defs.Length; i++)
            {
                if (IsGone(i)) continue;
                double hw = hwDry[i];
                for (int k = 0; k < Thrusters.Count; k++)
                    if (Thrusters[k].PartIndex == i && Thrusters[k].Kind != ThrusterKind.Main) hw += Thrusters[k].OwnPropKg;
                s += hw;
            }
            return s;
        }

        public void RefreshMassProps()
        {
            scratch.Clear();
            for (int i = 0; i < Defs.Length; i++)
            {
                if (IsGone(i)) continue;
                double m = PartMassKg(i);
                if (!(m > 0d)) continue;
                PartDefinition d = Defs[i];
                var shape = d.Shape;
                var dims = new Vector3d(d.Dimensions.x, d.Dimensions.y, d.Dimensions.z);
                if (shape != PartShape.Point && !(dims.X > 0d && dims.Y > 0d)) shape = PartShape.Point;
                if (shape == PartShape.Frustum && !(dims.Z > 0d)) shape = PartShape.Cylinder;
                scratch.Add(new Part(m, new Vector3d(d.Offset.x, d.Offset.y, d.Offset.z), d.JointStrengthNewtons,
                    shape, dims, QuaternionD.Identity));
            }

            if (scratch.Count == 0) { MassKg = 0d; return; }
            AssemblyStats s = AssemblyStats.Compute(scratch);
            MassKg = s.TotalMassKg; Com = s.CenterOfMass; Inertia = s.Inertia;
        }

        // ─── Control ─────────────────────────────────────────────────────────

        public bool IsOperating(Thruster t, double pressurePa)
        {
            if (IsGone(t.PartIndex) || !t.Enabled || Propulsion == null) return false;
            if (t.Stage != Propulsion.ActiveStageIndex) return false;
            if (pressurePa > t.MaxOperatingPressurePa) return false;
            StageCore core = Propulsion.ActiveCore;
            if (core == null) return false;
            for (int g = 0; g < core.Engines.Count; g++)
                if (Equals(core.Engines[g].Key, t.EngineKey)) return core.IsFed(core.Engines[g]);
            return false;
        }

        public int MainEngineCount { get { int n = 0; foreach (var t in Thrusters) if (t.Kind == ThrusterKind.Main) n++; return n; } }

        public Thruster MainEngine(int ordinal)
        {
            int n = 0;
            foreach (var t in Thrusters) if (t.Kind == ThrusterKind.Main && n++ == ordinal) return t;
            return null;
        }

        public void ToggleEngine(int ordinal)
        {
            Thruster t = MainEngine(ordinal);
            if (t != null) t.Enabled = !t.Enabled;
        }

        public void SetAllEngines(bool on)
        {
            foreach (var t in Thrusters) if (t.Kind == ThrusterKind.Main) t.Enabled = on;
        }

        /// <summary>Match the active stage core's engine groups to the engines actually running.</summary>
        private void SyncStageGroups(double pressurePa)
        {
            StageCore core = Propulsion != null ? Propulsion.ActiveCore : null;
            if (core == null) return;
            for (int g = 0; g < core.Engines.Count; g++)
            {
                int n = 0;
                foreach (var t in Thrusters)
                {
                    if (t.Kind != ThrusterKind.Main || t.Stage != Propulsion.ActiveStageIndex) continue;
                    if (!Equals(t.EngineKey, core.Engines[g].Key)) continue;
                    if (!IsGone(t.PartIndex) && t.Enabled && pressurePa <= t.MaxOperatingPressurePa) n++;
                }
                core.Engines[g].Count = n;
            }
        }

        /// <summary>
        /// Chunk preparation. attitude — body→world (or →site frame in the zone), omega — in ship axes.
        /// allowed=false (warp above the thrust threshold) — everything is muted.
        /// </summary>
        public void Prepare(double dt, QuaternionD attitude, Vector3d omegaBody, double throttle, double pressurePa, bool allowed)
        {
            PressurePa = pressurePa;
            RefreshMassProps();
            if (!allowed) { ZeroLevels(); Evaluate(); return; }

            SyncStageGroups(pressurePa);
            throttle = Math.Min(1d, Math.Max(0d, throttle));

            // 1) Main engines: throttle → target level → ramp to it.
            foreach (var t in Thrusters)
            {
                if (t.Kind != ThrusterKind.Main) continue;
                double target = 0d;
                if (IsOperating(t, pressurePa) && throttle > 0d)
                    target = EnforceMinThrottle ? Math.Max(throttle, t.MinThrottle) : throttle;
                if (target <= 0d && !IsOperating(t, pressurePa)) t.Level = 0d;
                else t.Level = Approach(t.Level, target, t.ThrottleRatePerS * dt);
            }

            // 2) Autopilot: required torque → gimbals + RCS.
            Vector3d tauReq = RequiredTorque(attitude, omegaBody);
            Vector3d tauRes = AllocateGimbal(tauReq, dt);
            AllocateRcs(tauRes, omegaBody);

            Evaluate();
        }

        private static double Approach(double v, double target, double step)
        {
            if (v < target) return Math.Min(target, v + step);
            return Math.Max(target, v - step);
        }

        private void ZeroLevels()
        {
            foreach (var t in Thrusters) t.Level = 0d;
        }

        // ─── Autopilot ───────────────────────────────────────────────────────

        private Vector3d RequiredTorque(QuaternionD q, Vector3d omega)
        {
            var c = Controls;
            var manual = new Vector3d(c.Pitch, c.Roll, c.Yaw); // X, Y (rocket axis), Z
            bool manualActive = manual.SqrMagnitude > 1e-4d;
            double kd = 2d * HoldOmega, kp = HoldOmega * HoldOmega;

            Vector3d alpha;
            if (manualActive)
            {
                holdValid = false;
                if (c.SasOn)
                    alpha = ((manual * MaxManualRateRadS) - omega) * kd;            // rate command
                else
                    alpha = manual * ManualAlphaRadS2;                               // direct torque
            }
            else if (c.SasOn)
            {
                // The target is captured only once rotation is damped out; before that — ω damping.
                if (!holdValid && omega.Magnitude < HoldCaptureRadS) { holdTarget = q; holdValid = true; }
                alpha = holdValid ? (ErrorVector(q, holdTarget) * kp) - (omega * kd) : -(omega * kd);
            }
            else return Vector3d.Zero;

            return Inertia * alpha;
        }

        /// <summary>Rotation vector (in ship axes) from the current attitude to the target.</summary>
        public static Vector3d ErrorVector(QuaternionD q, QuaternionD target)
        {
            QuaternionD e = (q.Conjugated * target).Normalized;
            double s = e.W < 0d ? -1d : 1d;
            var v = new Vector3d(e.X * s, e.Y * s, e.Z * s);
            double vm = v.Magnitude;
            if (vm < 1e-12d) return Vector3d.Zero;
            double angle = 2d * Math.Atan2(vm, e.W * s);
            return v * (angle / vm);
        }

        public void HoldCurrentAttitude() { holdValid = false; }

        /// <summary>Least-squares gimbal angle fit for the required torque; returns the residual for RCS.</summary>
        private Vector3d AllocateGimbal(Vector3d tauReq, double dt)
        {
            Vector3d sx = Vector3d.Zero, sz = Vector3d.Zero, tau0 = Vector3d.Zero;
            double lim = double.MaxValue; bool any = false;
            var ax = new Vector3d(1d, 0d, 0d); var az = new Vector3d(0d, 0d, 1d);
            foreach (var t in Thrusters)
            {
                if (t.Kind != ThrusterKind.Main || t.GimbalMaxRad <= 0d || t.Level <= 0d) continue;
                double f = t.Level * t.ThrustVacN * t.IspAt(PressurePa) / t.IspVacS;
                Vector3d r = t.Position - Com, d = t.Direction.Normalized;
                tau0 += Vector3d.Cross(r, d * f);
                sx += Vector3d.Cross(r, Vector3d.Cross(ax, d) * f);
                sz += Vector3d.Cross(r, Vector3d.Cross(az, d) * f);
                lim = Math.Min(lim, t.GimbalMaxRad); any = true;
            }

            double thx = 0d, thz = 0d;
            if (any)
            {
                double a11 = Vector3d.Dot(sx, sx), a12 = Vector3d.Dot(sx, sz), a22 = Vector3d.Dot(sz, sz);
                // τ0 + Sθ = τ_req: engine torque at zero gimbal (offset, COM drift) is known — subtract it.
                Vector3d need = tauReq - tau0;
                double b1 = Vector3d.Dot(sx, need), b2 = Vector3d.Dot(sz, need);
                double det = (a11 * a22) - (a12 * a12);
                if (det > 1e-12d * Math.Max(1d, a11 * a22))
                {
                    thx = ((b1 * a22) - (b2 * a12)) / det;
                    thz = ((a11 * b2) - (a12 * b1)) / det;
                }
                else if (a11 > 1e-9d) thx = b1 / a11;
                thx = Math.Max(-lim, Math.Min(lim, thx));
                thz = Math.Max(-lim, Math.Min(lim, thz));
            }

            foreach (var t in Thrusters)
            {
                if (t.Kind != ThrusterKind.Main || t.GimbalMaxRad <= 0d) continue;
                double step = t.GimbalRateRadS * dt;
                t.GimbalX = Approach(t.GimbalX, any && t.Level > 0d ? thx : 0d, step);
                t.GimbalZ = Approach(t.GimbalZ, any && t.Level > 0d ? thz : 0d, step);
            }

            // The residual uses the ACTUAL gimbal angles (rate-limited).
            double gx = 0d, gz = 0d; int n = 0;
            foreach (var t in Thrusters)
                if (t.Kind == ThrusterKind.Main && t.GimbalMaxRad > 0d && t.Level > 0d) { gx += t.GimbalX; gz += t.GimbalZ; n++; }
            if (n > 0) { gx /= n; gz /= n; }
            return tauReq - tau0 - (sx * gx) - (sz * gz);
        }

        /// <summary>
        /// RCS: greedy nozzle-set selection minimizing the residual (torque + unwanted force).
        /// A single nozzle of the ring produces both force and torque; pure roll or pitch comes only
        /// from a PAIR — the set is found by minimizing |τ_res|² + (λ·|F_res|)². This also yields the
        /// deadband: a nozzle is not fired if it would overshoot the target.
        /// </summary>
        private void AllocateRcs(Vector3d tauDesired, Vector3d omega)
        {
            var c = Controls;
            foreach (var t in Thrusters) if (t.Kind == ThrusterKind.Rcs) t.Level = 0d;
            if (!c.RcsOn) return;

            bool rot = (Inertia.Inverse() * tauDesired).Magnitude > RcsDeadbandRadS2;
            bool trans = c.Translate.SqrMagnitude > 1e-4d;
            if (!rot && !trans) return;

            double maxF = 0d;
            foreach (var t in Thrusters) if (t.Kind == ThrusterKind.Rcs) maxF = Math.Max(maxF, t.ThrustVacN);
            Vector3d rt = rot ? tauDesired : Vector3d.Zero;
            Vector3d rf = trans ? c.Translate * maxF : Vector3d.Zero;
            const double Lambda = 3d;
            double cost = rt.SqrMagnitude + (Lambda * Lambda * rf.SqrMagnitude);
            int n = Thrusters.Count;
            for (int guard = 0; guard < n; guard++)
            {
                // Candidate — a single nozzle OR a pair (a pair gives pure torque with the force cancelled).
                int bi = -1, bj = -1; double bestCost = cost; Vector3d bt = rt, bf = rf;
                for (int i = 0; i < n; i++)
                {
                    Thruster a = Thrusters[i];
                    if (!RcsFree(a)) continue;
                    Vector3d fa = a.Direction.Normalized * a.ThrustVacN;
                    Vector3d ta = Vector3d.Cross(a.Position - Com, fa);
                    for (int j = i; j < n; j++)
                    {
                        Thruster b = Thrusters[j];
                        if (j != i && !RcsFree(b)) continue;
                        Vector3d nt = rt - ta, nf = rf - fa;
                        if (j != i)
                        {
                            Vector3d fb = b.Direction.Normalized * b.ThrustVacN;
                            nt -= Vector3d.Cross(b.Position - Com, fb); nf -= fb;
                        }

                        double nc = nt.SqrMagnitude + (Lambda * Lambda * nf.SqrMagnitude);
                        if (nc < bestCost - 1e-9d * (1d + cost)) { bestCost = nc; bi = i; bj = j; bt = nt; bf = nf; }
                    }
                }

                if (bi < 0) break;
                Thrusters[bi].Level = 1d; Thrusters[bj].Level = 1d;
                cost = bestCost; rt = bt; rf = bf;
            }
        }

        private bool RcsFree(Thruster t)
        {
            return t.Kind == ThrusterKind.Rcs && t.Level <= 0d && !IsGone(t.PartIndex) && t.OwnPropKg > Eps;
        }

        // ─── Forces ──────────────────────────────────────────────────────────

        /// <summary>Total force and torque about the COM, plus flows. Forces are in ship axes.</summary>
        public void Evaluate()
        {
            Vector3d f = Vector3d.Zero, tq = Vector3d.Zero;
            double flow = 0d, aux = 0d;
            foreach (var t in Thrusters)
            {
                if (t.Level <= 0d || IsGone(t.PartIndex)) continue;
                Vector3d fi = t.CurrentDirection() * t.ThrustAt(PressurePa);
                f += fi;
                tq += Vector3d.Cross(t.Position - Com, fi);
                flow += t.MassFlow;
                if (t.Kind != ThrusterKind.Main) aux += t.MassFlow;
            }

            ForceBody = f; TorqueBody = tq; FlowTotal = flow; FlowAux = aux;
        }

        /// <summary>Maximum substep before the hardware propellant (RCS) runs out.</summary>
        public double MaxChunkSeconds()
        {
            double best = double.PositiveInfinity;
            foreach (var t in Thrusters)
            {
                if (t.Kind == ThrusterKind.Main || t.Level <= 0d || IsGone(t.PartIndex)) continue;
                double flow = t.MassFlow;
                if (flow > 0d) best = Math.Min(best, Math.Max(0.002d, t.OwnPropKg / flow));
            }
            return best;
        }

        /// <summary>Mute everything (far warp): levels to zero, force cache recomputed.</summary>
        public void Silence()
        {
            ZeroLevels();
            Evaluate();
        }

        public bool AnyThrust { get { return FlowTotal > 0d || ForceBody.SqrMagnitude > 0d; } }

        /// <summary>
        /// End of chunk: burn the hardware propellant (flow is constant over the chunk). Returns the
        /// mass that left Ship.Mass as hardware (it sits in DetachedMassKg); main burn is measured by the caller.
        /// </summary>
        public double Finish(double dt)
        {
            double aux = 0d;
            foreach (var t in Thrusters)
            {
                if (t.Kind == ThrusterKind.Main || t.Level <= 0d) continue;
                double burn = Math.Min(t.OwnPropKg, t.MassFlow * dt);
                t.OwnPropKg -= burn;
                if (!IsGone(t.PartIndex)) aux += burn;
                if (t.OwnPropKg <= Eps) t.OwnPropKg = Math.Max(0d, t.OwnPropKg);
            }

            if (aux > 0d && Propulsion != null) Propulsion.ReleaseDetached(aux);
            return aux;
        }

        /// <summary>Main-engine flow over dt (for the PhysX zone, which has no integrator), kg.</summary>
        public double MainBurnKg(double dt)
        {
            double flow = 0d;
            foreach (var t in Thrusters)
                if (t.Kind == ThrusterKind.Main && t.Level > 0d && !IsGone(t.PartIndex)) flow += t.MassFlow;
            return flow * dt;
        }
    }
}
