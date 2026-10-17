using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;

namespace Galilego.Flight
{
    /// <summary>
    /// Единая 6-DOF модель полёта аппарата (без Unity). Хранит реальные двигатели/сопла с
    /// точками приложения, считает ЦМ и тензор инерции по ТЕКУЩИМ массам деталей (топливо
    /// выгорает пропорционально по ступени), суммарные силу и момент (относительно ЦМ),
    /// расходы, автопилот (удержание ориентации: подвес двигателей + РСУ) и САС.
    /// Один кадр: Prepare (команды→уровни тяги, кэш силы/момента) → интегратор/PhysX →
    /// Finish (списание аппаратного топлива). Силы/момент кэшируются на чанк — то же
    /// «зерно control-tick», что у газа в остальном проекте (физика ×3 совпадает с ×1).
    /// </summary>
    public sealed class VesselFlightModel
    {
        public const double Eps = 1e-4d;

        public readonly PartDefinition[] Defs;
        public readonly List<Thruster> Thrusters = new List<Thruster>();
        public readonly FlightControls Controls = new FlightControls();
        public StagePropulsion Propulsion;

        // ─ параметры автопилота ─
        public double MaxManualRateRadS = 0.35d;
        public double ManualAlphaRadS2 = 0.3d;
        public double HoldOmega = 0.8d;      // собственная частота удержания, рад/с
        public double RcsDeadbandRadS2 = 0.004d;
        public double HoldCaptureRadS = 0.01d;   // ниже этой скорости вращения фиксируется цель удержания
        public bool EnforceMinThrottle = true;

        // ─ данные деталей ─
        private readonly double[] dry, propStart, hwDry;
        private readonly int[] stageOf;
        private readonly bool[] gone;
        private readonly List<Part> scratch = new List<Part>();

        // ─ САС ─
        public bool[] AbortKeep;              // детали, остающиеся с капсулой (капсула, всё выше, САС)
        public int LesPart = -1;              // индекс детали-башни САС (−1 — САС нет)
        public bool LesArmed { get; private set; }
        public bool Aborting { get; private set; }
        public bool EnginesCut { get; private set; }
        public bool SplitPending { get; private set; }
        public bool JettisonPending { get; private set; }

        // ─ кэш чанка ─
        public double MassKg { get; private set; }
        public Vector3d Com { get; private set; }
        public Matrix3x3 Inertia { get; private set; } = Matrix3x3.Identity;
        public Vector3d ForceBody { get; private set; }
        public Vector3d TorqueBody { get; private set; }
        public double FlowTotal { get; private set; }
        public double FlowAux { get; private set; }
        public double PressurePa { get; private set; }

        // ─ автопилот ─
        private QuaternionD holdTarget = QuaternionD.Identity;
        private bool holdValid;

        public VesselFlightModel(PartDefinition[] defs, double[] dryKg, double[] propStartKg, int[] stages, double[] hardwareDryKg)
        {
            Defs = defs ?? throw new ArgumentNullException(nameof(defs));
            dry = dryKg; propStart = propStartKg; stageOf = stages; hwDry = hardwareDryKg;
            gone = new bool[defs.Length];
            LesArmed = false;
            PartOfBody = new int[defs.Length];
            for (int i = 0; i < defs.Length; i++) PartOfBody[i] = i;
        }

        /// <summary>Тело прокси b ↔ деталь модели PartOfBody[b] (меняется при пересборке остатка).</summary>
        public int[] PartOfBody;

        public int BodyOfPart(int part)
        {
            for (int b = 0; b < PartOfBody.Length; b++) if (PartOfBody[b] == part) return b;
            return -1;
        }

        /// <summary>Маска тел прокси, остающихся с кораблём (для SeverForStaging).</summary>
        public bool[] BodyKeepMask()
        {
            var keep = new bool[PartOfBody.Length];
            for (int b = 0; b < keep.Length; b++) keep[b] = !IsGone(PartOfBody[b]);
            return keep;
        }

        /// <summary>
        /// Описание остатка для зоны контакта/посадки: массы ТЕКУЩИЕ (с учётом выгоревшего топлива),
        /// индексы родителей пересчитаны; PartOfBody обновляется под новую нумерацию тел.
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
                    EngineOffset = d.EngineOffset,
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

        public void ArmLes(int part) { LesPart = part; LesArmed = part >= 0; }

        // ─── Массы ───────────────────────────────────────────────────────────

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

        /// <summary>Масса аппаратуры (не входит в ядра ступеней): в Ship.Mass есть, в ядрах нет.</summary>
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

        // ─── Управление ──────────────────────────────────────────────────────

        public bool IsOperating(Thruster t, double pressurePa)
        {
            if (IsGone(t.PartIndex) || !t.Enabled || EnginesCut || Propulsion == null) return false;
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

        /// <summary>Поставить группы ядра активной ступени в соответствие реально работающим двигателям.</summary>
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
                    if (!IsGone(t.PartIndex) && t.Enabled && !EnginesCut && pressurePa <= t.MaxOperatingPressurePa) n++;
                }
                core.Engines[g].Count = n;
            }
        }

        /// <summary>
        /// Подготовка чанка. attitude — body→мир (или →рамка места в зоне), omega — в осях корабля.
        /// allowed=false (варп выше порога тяги) — всё глушится.
        /// </summary>
        public void Prepare(double dt, QuaternionD attitude, Vector3d omegaBody, double throttle, double pressurePa, bool allowed)
        {
            PressurePa = pressurePa;
            RefreshMassProps();
            if (!allowed) { ZeroLevels(); Evaluate(); return; }

            SyncStageGroups(pressurePa);
            throttle = Math.Min(1d, Math.Max(0d, throttle));

            // 1) Маршевые: газ → целевой уровень → выход на режим.
            foreach (var t in Thrusters)
            {
                if (t.Kind == ThrusterKind.Main)
                {
                    double target = 0d;
                    if (IsOperating(t, pressurePa) && throttle > 0d)
                        target = EnforceMinThrottle ? Math.Max(throttle, t.MinThrottle) : throttle;
                    if (target <= 0d && !IsOperating(t, pressurePa)) t.Level = 0d;
                    else t.Level = Approach(t.Level, target, t.ThrottleRatePerS * dt);
                }
                else if (t.Kind != ThrusterKind.Rcs)
                {
                    t.Level = t.Ignited && t.OwnPropKg > Eps ? 1d : 0d;
                    t.Ignited = t.Ignited && t.OwnPropKg > Eps;
                }
            }

            // 2) Автопилот: требуемый момент → подвес + РСУ.
            Vector3d tauReq = RequiredTorque(attitude, omegaBody);
            Vector3d tauRes = AllocateGimbal(tauReq, dt);
            AllocateRcs(tauRes, omegaBody);

            // 3) САС: после выгорания увода — отстрел башни.
            if (Aborting && LesArmed && !JettisonPending) CheckAbortProgress();
            Evaluate();
        }

        private static double Approach(double v, double target, double step)
        {
            if (v < target) return Math.Min(target, v + step);
            return Math.Max(target, v - step);
        }

        private void ZeroLevels()
        {
            foreach (var t in Thrusters) if (t.Kind != ThrusterKind.JettisonMotor) t.Level = 0d;
        }

        // ─── Автопилот ───────────────────────────────────────────────────────

        private Vector3d RequiredTorque(QuaternionD q, Vector3d omega)
        {
            var c = Controls;
            var manual = new Vector3d(c.Pitch, c.Roll, c.Yaw); // X, Y(ось ракеты), Z
            bool manualActive = manual.SqrMagnitude > 1e-4d;
            double kd = 2d * HoldOmega, kp = HoldOmega * HoldOmega;

            Vector3d alpha;
            if (manualActive)
            {
                holdValid = false;
                if (c.SasOn)
                    alpha = ((manual * MaxManualRateRadS) - omega) * kd;            // командование скоростью
                else
                    alpha = manual * ManualAlphaRadS2;                               // прямой момент
            }
            else if (c.SasOn)
            {
                // Цель фиксируется только когда вращение погашено; до этого — демпфирование ω.
                if (!holdValid && omega.Magnitude < HoldCaptureRadS) { holdTarget = q; holdValid = true; }
                alpha = holdValid ? (ErrorVector(q, holdTarget) * kp) - (omega * kd) : -(omega * kd);
            }
            else return Vector3d.Zero;

            return Inertia * alpha;
        }

        /// <summary>Вектор поворота (в осях корабля) от текущей ориентации к целевой.</summary>
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

        /// <summary>Подбор углов подвеса (МНК) под требуемый момент; возвращает остаток для РСУ.</summary>
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
                // τ0 + Sθ = τ_req: момент двигателей при нулевом подвесе (смещение, дрейф ЦМ) — известен, вычитаем.
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

            // Остаток считаем по УГЛАМ, которые подвес реально имеет (с ограничением скорости).
            double gx = 0d, gz = 0d; int n = 0;
            foreach (var t in Thrusters)
                if (t.Kind == ThrusterKind.Main && t.GimbalMaxRad > 0d && t.Level > 0d) { gx += t.GimbalX; gz += t.GimbalZ; n++; }
            if (n > 0) { gx /= n; gz /= n; }
            return tauReq - tau0 - (sx * gx) - (sz * gz);
        }

        /// <summary>
        /// РСУ: жадный подбор набора сопел, минимизирующий остаток (момент + нежелательная сила).
        /// Одиночное сопло кольца даёт и силу, и момент; чистый крен или тангаж получается только
        /// ПАРОЙ — набор находится минимизацией |τ_ост|² + (λ·|F_ост|)². Заодно получается
        /// «мёртвая зона»: сопло не включается, если перелетит цель.
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
                // Кандидат — одно сопло ИЛИ пара (пара даёт чистый момент с погашенной силой).
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

        // ─── Силы ────────────────────────────────────────────────────────────

        /// <summary>Суммарные сила и момент относительно ЦМ, расходы. Силы — в осях корабля.</summary>
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

        /// <summary>Максимальный подшаг, пока не кончится топливо аппаратуры (РСУ/САС).</summary>
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

        /// <summary>Заглушить всё (дальний варп): уровни в ноль, кэш сил пересчитан.</summary>
        public void Silence()
        {
            ZeroLevels();
            foreach (var t in Thrusters) if (t.Kind == ThrusterKind.JettisonMotor) t.Level = 0d;
            Evaluate();
        }

        public bool AnyThrust { get { return FlowTotal > 0d || ForceBody.SqrMagnitude > 0d; } }

        /// <summary>
        /// Конец чанка: списать топливо аппаратуры (расход постоянен на чанке). Возвращает массу,
        /// ушедшую из Ship.Mass по аппаратуре (она в DetachedMassKg); маршевую мерит вызывающий.
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

        /// <summary>Расход маршевых за dt (для зоны PhysX, где интегратора нет), кг.</summary>
        public double MainBurnKg(double dt)
        {
            double flow = 0d;
            foreach (var t in Thrusters)
                if (t.Kind == ThrusterKind.Main && t.Level > 0d && !IsGone(t.PartIndex)) flow += t.MassFlow;
            return flow * dt;
        }

        // ─── САС ─────────────────────────────────────────────────────────────

        /// <summary>Аварийный увод: отсечка двигателей, запрос отделения капсулы, зажигание увода.</summary>
        public bool TriggerAbort()
        {
            if (!LesArmed || Aborting) return false;
            Aborting = true; EnginesCut = true; SplitPending = true;
            foreach (var t in Thrusters) if (t.Kind == ThrusterKind.AbortMotor && t.OwnPropKg > Eps) t.Ignited = true;
            return true;
        }

        /// <summary>Штатный отстрел САС (без аварии).</summary>
        public bool RequestJettison()
        {
            if (!LesArmed || JettisonPending) return false;
            JettisonPending = true;
            foreach (var t in Thrusters) if (t.Kind == ThrusterKind.JettisonMotor && t.OwnPropKg > Eps) t.Ignited = true;
            return true;
        }

        private void CheckAbortProgress()
        {
            foreach (var t in Thrusters)
                if (t.Kind == ThrusterKind.AbortMotor && t.OwnPropKg > Eps && t.Ignited) return;
            RequestJettison();
        }

        public void ConsumeSplit() { SplitPending = false; }
        public void ConsumeJettison() { JettisonPending = false; LesArmed = false; }

        /// <summary>
        /// Убрать детали (keep[i]=false) из аппарата: возвращает их массу и ЦМ (система корабля).
        /// Если задеты детали ступеней — ступенчатая модель замораживается (всё остальное — масса
        /// вне ядер), иначе убранная масса просто списывается из «вне сборки».
        /// </summary>
        public double RemoveParts(bool[] keep, out Vector3d removedCom)
        {
            double removed = 0d; Vector3d w = Vector3d.Zero; bool touchedStage = false;
            RefreshMassProps();
            for (int i = 0; i < Defs.Length; i++)
            {
                if (IsGone(i) || (i < keep.Length && keep[i])) continue;
                double m = PartMassKg(i);
                removed += m; w += new Vector3d(Defs[i].Offset.x, Defs[i].Offset.y, Defs[i].Offset.z) * m;
                if (stageOf[i] >= 0) touchedStage = true;
                gone[i] = true;
            }

            removedCom = removed > 0d ? w / removed : Vector3d.Zero;
            if (Propulsion == null) return removed;
            if (touchedStage)
            {
                for (int i = 0; i < Defs.Length; i++)
                {
                    if (IsGone(i) || stageOf[i] < 0) continue;
                    hwDry[i] += dry[i] + (propStart[i] * Fill(stageOf[i]));
                    dry[i] = 0d; propStart[i] = 0d; stageOf[i] = -1;
                }

                double remain = 0d;
                for (int i = 0; i < Defs.Length; i++) if (!IsGone(i)) remain += PartMassKg(i);
                Propulsion.DetachAllStages(remain);
            }
            else Propulsion.ReleaseDetached(removed);
            RefreshMassProps();
            return removed;
        }
    }
}
