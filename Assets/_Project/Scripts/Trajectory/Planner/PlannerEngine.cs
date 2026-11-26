using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Planning
{
    public enum ImpulseRole { PrepOrbit = 1, Departure = 2, Correction = 3, FlybyBurn = 4, Capture = 5 }

    public struct PlannedImpulse
    {
        public ImpulseRole Role;
        public double Time;                 // мировое время, с
        public int FrameBody;               // тело, чьи оси определяют T/N/B
        public Vector3d DvWorld;
        public Vector3d RelPos, RelVel;     // состояние корабля относительно FrameBody в момент импульса
        public Vector3d WorldPos, WorldVel;
        public string Label;
    }

    public sealed class PlanDraft
    {
        public bool Valid;
        public string Note = "";
        public readonly List<PlannedImpulse> Impulses = new List<PlannedImpulse>();
        public double IdealDv;              // из сетки: круговая парковочная, патч-коники
        public double PatchedDv;            // с настоящей орбиты корабля, патч-коники
        public double RefinedDv;            // после n-body коррекции (сумма модулей импульсов)
        public double MissBeforeM, MissAfterM;
        /// <summary>Радиус периапсиса пролёта у гиганта в этом черновике (с учётом настоящей орбиты отлёта), м; 0 — нет пролёта.</summary>
        public double FlybyPeriapsisRadius;
        public double DepartTime, ArriveTime;
        public bool Refined;
        /// <summary>Окно далеко: фаза корабля на орбите по двухтельной модели не предсказуема — пересчитайте за ≤ ShortHorizonS до отлёта.</summary>
        public bool Preliminary;
        /// <summary>
        /// Сколько стоит несогласованность плоскости/фазы орбиты корабля относительно идеала.
        /// Подготовка орбиты (PrepOrbit) — это запрошенный игроком манёвр, а не рассогласование.
        /// </summary>
        public double PlanePenaltyDv
        {
            get
            {
                double prep = 0d;
                foreach (PlannedImpulse i in Impulses)
                {
                    if (i.Role == ImpulseRole.PrepOrbit) { prep += i.DvWorld.Magnitude; }
                }

                return Math.Max(0d, PatchedDv - IdealDv - prep);
            }
        }
        public double TotalDv { get { double s = 0d; foreach (PlannedImpulse i in Impulses) { s += i.DvWorld.Magnitude; } return s; } }
    }

    public sealed class TransferRequest
    {
        public int From, To;
        public bool UseCurrentOrbit = true;
        public double ParkAltitudeM;                 // если !UseCurrentOrbit
        public CaptureKind Capture = CaptureKind.Circular;
        public double ArrivalAltitudeM = 400e3, CaptureEccentricity;
        public double SearchStartS, SearchSpanS;     // 0 ⇒ авто
        public double TofMinS, TofMaxS;              // 0 ⇒ авто
        public double LeadTimeS = 300d;              // минимум до первого импульса
        public double ShipDvBudget = double.PositiveInfinity;
        public double MaxPrepEccentricity = 0.02d;
        /// <summary>Горизонт, внутри которого состояние корабля берётся из n-body прогноза (точно); дальше — ориентировочно.</summary>
        public double ShortHorizonS = 3d * 86400d;
    }

    /// <summary>Оркестратор: мир → треки → окна → черновик плана → n-body уточнение. Чистый C#, без Unity.</summary>
    public static class PlannerEngine
    {
        public static bool TryCommonParent(IPlannerWorld w, int from, int to, out int parent, out string why)
        {
            parent = -1; why = "";
            if (from == to) { why = "Старт и цель совпадают."; return false; }
            int pf = w.ParentOf(from), pt = w.ParentOf(to);
            if (pf < 0 || pf != pt) { why = "v1: старт и цель должны обращаться вокруг одного тела (планета↔планета, спутник↔спутник)."; return false; }
            parent = pf;
            return true;
        }

        public static bool TryBuildSpec(IPlannerWorld w, TransferRequest rq, double now, double shipRadius, out TransferSpec spec, out string why)
        {
            spec = null;
            if (!TryCommonParent(w, rq.From, rq.To, out int parent, out why)) { return false; }
            var from = new BodyTrack(w, rq.From, parent);
            if (!from.TryEvaluate(now, out Vector3d r, out Vector3d v)) { why = "Нет эфемерид тела старта."; return false; }
            Vector3d n = Vector3d.Cross(r, v).Normalized;
            double rParkFrom = rq.UseCurrentOrbit ? shipRadius : w.Radius(rq.From) + rq.ParkAltitudeM;
            spec = new TransferSpec
            {
                MuCentral = w.Mu(parent), CentralRadius = w.Radius(parent), MinCentralRadius = 1.05d * w.Radius(parent),
                From = from, To = new BodyTrack(w, rq.To, parent), Normal = n, Prograde = true,
                MuFrom = w.Mu(rq.From), ParkRadiusFrom = rParkFrom,
                MuTo = w.Mu(rq.To), ParkRadiusTo = w.Radius(rq.To) + rq.ArrivalAltitudeM, Capture = rq.Capture, CaptureEccentricity = rq.CaptureEccentricity,
            };
            why = "";
            return true;
        }

        /// <summary>Окно поиска по умолчанию: полтора синодических периода, полёт 0.35…2.2 хоманова.</summary>
        public static void DefaultWindow(IPlannerWorld w, TransferRequest rq, double now, out double start, out double span, out double tofMin, out double tofMax)
        {
            TryCommonParent(w, rq.From, rq.To, out int parent, out _);
            var a = new BodyTrack(w, rq.From, parent); var b = new BodyTrack(w, rq.To, parent);
            a.TryEvaluate(now, out Vector3d ra, out Vector3d va); b.TryEvaluate(now, out Vector3d rb, out Vector3d vb);
            double mu = w.Mu(parent);
            double aA = 1d / ((2d / ra.Magnitude) - (va.SqrMagnitude / mu)), aB = 1d / ((2d / rb.Magnitude) - (vb.SqrMagnitude / mu));
            double tA = 2d * Math.PI * Math.Sqrt(Math.Abs(aA * aA * aA) / mu), tB = 2d * Math.PI * Math.Sqrt(Math.Abs(aB * aB * aB) / mu);
            double syn = Math.Abs(tA - tB) > 1e-3d * Math.Min(tA, tB) ? ConicMath.SynodicPeriod(tA, tB) : 2d * Math.Max(tA, tB);
            double aT = 0.5d * (aA + aB), tH = Math.PI * Math.Sqrt(aT * aT * aT / mu);
            start = rq.SearchStartS > 0d ? rq.SearchStartS : now + rq.LeadTimeS;
            span = rq.SearchSpanS > 0d ? rq.SearchSpanS : 1.5d * syn;
            tofMin = rq.TofMinS > 0d ? rq.TofMinS : 0.35d * tH;
            tofMax = rq.TofMaxS > 0d ? rq.TofMaxS : 2.2d * tH;
        }

        /// <summary>Сетка «поркчоп» + локальные минимумы, уточнённые симплексом. Окна по возрастанию ΔV.</summary>
        public static List<TransferSolution> FindWindows(TransferSpec spec, double start, double span, double tofMin, double tofMax,
            int nDep, out PorkchopGrid grid)
        {
            double step = span / Math.Max(1, nDep - 1);
            int kMin = Math.Max(1, (int)Math.Ceiling(tofMin / step)), kMax = Math.Max(kMin + 1, (int)Math.Floor(tofMax / step));
            grid = TransferScanner.Scan(spec, start, step, nDep, kMin, kMax);
            var res = new List<TransferSolution>();
            foreach (GridMinimum m in TransferScanner.LocalMinima(grid, 6, Math.Max(3, nDep / 12)))
            {
                double t0 = grid.DepartTime(m.I), t1 = t0 + grid.Tof(m.K);
                if (TransferScanner.Refine(spec, t0, t1, step, start, out TransferSolution s)) { res.Add(s); }
            }

            // Одинаковая цена (в пределах 0.1 %) — раньше лучше: повторяющиеся окна не вытесняют ближайшее.
            // Сортировка в два уровня, а не компаратор с допуском: компаратор «допуск + T0 vs Total» нетранзитивен,
            // и List.Sort на нём может бросить InvalidOperationException. Группы считаются от первого элемента.
            res.Sort((x, y) => x.Total.CompareTo(y.Total));
            for (int i = 0; i < res.Count; )
            {
                int j = i + 1;
                double baseTotal = res[i].Total;
                while (j < res.Count && res[j].Total <= (baseTotal * 1.001d) + 1e-9d) { j++; }
                if (j - i > 1)
                {
                    res.Sort(i, j - i, Comparer<TransferSolution>.Create((a, b) => a.T0.CompareTo(b.T0)));
                }

                i = j;
            }

            return res;
        }

        // ------------------------------------------------------------------------------------------------ черновик плана

        /// <summary>
        /// Перелёт между телами: подготовка орбиты (если задана другая высота) → отлёт с орбиты корабля → TCM1 → захват.
        /// Патч-коники дают импульсы; с propagator — n-body уточнение (отлёт min-norm + TCM1 по лестнице целей).
        /// </summary>
        public static PlanDraft MakeTransferDraft(IPlannerWorld w, TransferRequest rq, TransferSpec spec, TransferSolution win, IPlannerPropagator prop)
        {
            var d = new PlanDraft();
            ShootingCorrector.Drive(MakeTransferDraftCo(w, rq, spec, win, prop, d));
            return d;
        }

        /// <summary>Корутина черновика: n-body «выстрелы» отдаются наружу по одному (в игре — по одному за кадр). Результат — в d.</summary>
        public static IEnumerable<ShootingCorrector.Shot> MakeTransferDraftCo(IPlannerWorld w, TransferRequest rq, TransferSpec spec, TransferSolution win, IPlannerPropagator prop, PlanDraft d)
        {
            d.IdealDv = win.Total; d.DepartTime = win.T0; d.ArriveTime = win.T1;
            double now = w.UniverseNow;
            if (!w.TryShipState(out Vector3d sp, out Vector3d sv)) { d.Note = "Нет состояния корабля."; yield break; }
            if (w.ShipBody() != rq.From) { d.Note = "Корабль не в сфере действия тела старта."; yield break; }
            w.TryBodyState(rq.From, now, out Vector3d bp, out Vector3d bv);
            Vector3d rRel = sp - bp, vRel = sv - bv;
            double muA = w.Mu(rq.From), minPer = w.Radius(rq.From) + w.SafeAltitude(rq.From);

            // 1) подготовка орбиты
            double shipEpoch = now; Vector3d r0 = rRel, v0 = vRel;
            if (!rq.UseCurrentOrbit)
            {
                double rNew = w.Radius(rq.From) + rq.ParkAltitudeM;
                if (Math.Abs(rNew - rRel.Magnitude) > 0.002d * rNew)
                {
                    if (!OrbitPrep.TryHohmann(muA, now + rq.LeadTimeS, PropagateRel(muA, rRel, vRel, rq.LeadTimeS, out Vector3d vAtLead), vAtLead, rNew, rq.MaxPrepEccentricity, out PrepPlan prep))
                    { d.Note = "Орбита не круговая (e > " + rq.MaxPrepEccentricity + "): сначала выровняйте её или оставьте «текущая орбита»."; yield break; }
                    if (prep.T2 + 60d > win.T0) { d.Note = "Подготовка орбиты не успевает до этого окна."; yield break; }
                    AddImpulse(d, w, ImpulseRole.PrepOrbit, prep.T1, rq.From, prep.Dv1, "ПОДГОТОВКА 1", muA, rRel, vRel, now);
                    UniversalKepler.TryPropagate(muA, rRel, vRel, prep.T1 - now, out Vector3d rp1, out Vector3d vp1);
                    AddImpulse(d, w, ImpulseRole.PrepOrbit, prep.T2, rq.From, prep.Dv2, "ПОДГОТОВКА 2", muA, prep.RAfter, prep.VAfter - prep.Dv2, 0d, forceState: true);
                    shipEpoch = prep.T2; r0 = prep.RAfter; v0 = prep.VAfter;
                }
            }

            // 2) отлёт с орбиты корабля
            // Состояние корабля до импульса: n-body прогноз, если окно близко и он есть; иначе Кеплер (+ пометка «ориентировочно»).
            // Для точного трека фазу ищем в пределах 30 сут (покрывает GEO/лунные орбиты), для Кеплера — только 3 сут:
            // дальше двухтельная фаза всё равно помечена Preliminary и должна быть пересчитана перед отлётом.
            bool exact = prop != null && rq.UseCurrentOrbit && win.T0 - now <= rq.ShortHorizonS;
            double period = 2d * Math.PI * Math.Sqrt(Math.Pow(1d / Math.Abs((2d / r0.Magnitude) - (v0.SqrMagnitude / muA)), 3d) / muA);
            double phaseSearch = exact ? 30d * 86400d : 3d * 86400d;
            if (!double.IsFinite(period) || period > phaseSearch) { period = phaseSearch; }
            double from = Math.Max(Math.Max(now + rq.LeadTimeS, shipEpoch + 60d), win.T0 - period), to = Math.Max(from + period, win.T0 + period);
            IShipTrack ship = exact ? (IShipTrack)new NBodyShipTrack(w, prop, rq.From, now, sp, sv) : new KeplerShipTrack(muA, shipEpoch, r0, v0);
            d.Preliminary = !exact && win.T0 - now > rq.ShortHorizonS;
            if (!ShipDeparture.Find(spec, muA, minPer, ship, win.T1, from, to, 600, out DeparturePlan dep)) { d.Note = "Не удалось построить отлёт с текущей орбиты."; yield break; }
            d.PatchedDv = dep.Burn.Dv + dep.ArrivalDv + PrepDv(d);
            d.DepartTime = dep.BurnTime;
            Vector3d dvDep = dep.Burn.DvWorld;
            AddImpulse(d, w, ImpulseRole.Departure, dep.BurnTime, rq.From, dvDep, "ОТЛЁТ", muA, dep.RShip, dep.VShip, 0d, forceState: true);

            // 3) геометрия захвата
            Vector3d hPref = spec.Normal;
            if (!CaptureGeometry.TryIncoming(spec.MuTo, dep.Transfer.VInfArr, spec.ParkRadiusTo, hPref, out HyperbolaAtPeriapsis aim)) { d.Note = "Нет геометрии захвата."; yield break; }
            double tPeri = dep.Transfer.T1, tTcm = dep.BurnTime + (0.4d * (tPeri - dep.BurnTime));

            // 4) n-body уточнение: отлёт (min-norm) + TCM1 (лестница целей)
            Vector3d dvTcm = Vector3d.Zero;
            if (prop != null)
            {
                w.TryBodyState(rq.From, dep.BurnTime, out Vector3d bpD, out Vector3d bvD);
                var model = new PlanShootingModel(w, prop, rq.To, dep.BurnTime, bpD + dep.RShip, bvD + dep.VShip);
                double dayS = 86400d;
                CaptureGeometry.TryPositionBefore(spec.MuTo, aim, 2d * dayS, out Vector3d posFar);
                CaptureGeometry.TryPositionBefore(spec.MuTo, aim, 0.25d * dayS, out Vector3d posMid);
                var s0 = new ShootingCorrector.Shot { Model = model, Dv = dvDep, Time = tPeri };
                yield return s0;
                d.MissBeforeM = s0.Ok ? (s0.RelPos - aim.PeriapsisPosition).Magnitude : double.NaN;
                var resA = new ShootingCorrector.Result();
                foreach (ShootingCorrector.Shot sh in ShootingCorrector.SolveCo(model, tPeri - (2d * dayS), dvDep, posFar, 2e7d, 8, 0.05d, resA)) { yield return sh; }
                Vector3d dvDepFixed = resA.Dv;
                var tcmModel = new PlanShootingModel(w, prop, rq.To, dep.BurnTime, bpD + dep.RShip, bvD + dep.VShip);
                tcmModel.Fixed.Add((dep.BurnTime, dvDepFixed)); tcmModel.VariableTime = tTcm;
                var ladder = new[]
                {
                    new ShootingCorrector.Aim { Time = tPeri - (2d * dayS), Position = posFar, ToleranceM = 1e5d },
                    new ShootingCorrector.Aim { Time = tPeri - (0.25d * dayS), Position = posMid, ToleranceM = 1e3d },
                    new ShootingCorrector.Aim { Time = tPeri, Position = aim.PeriapsisPosition, ToleranceM = 100d },
                };
                var resB = new ShootingCorrector.Result();
                foreach (ShootingCorrector.Shot sh in ShootingCorrector.SolveLadderCo(tcmModel, Vector3d.Zero, ladder, 12, 0.01d, resB)) { yield return sh; }
                bool ok = resB.Ok; double miss = resB.Miss; dvTcm = resB.Dv;
                d.MissAfterM = miss; d.Refined = ok;
                if (ok)
                {
                    dvDep = dvDepFixed;
                    SetLastDeparture(d, dvDep);
                }
                else { dvTcm = Vector3d.Zero; d.Note = "n-body уточнение не сошлось (промах " + (miss / 1e3).ToString("F0") + " км); импульсы — патч-коники."; }
            }

            // 5) TCM1 (всегда в плане — резерв/уточнение) и захват. Состояния узлов: n-body, если оно сошлось (T/N/B должны
            //    строиться по ИСТИННОМУ состоянию корабля, как делает прогноз), иначе патч-коники.
            int parent = w.ParentOf(rq.From);
            Vector3d rTcm, vTcm, wpTcm, wvTcm;
            Vector3d capRel, capVel, capWp, capWv;
            if (d.Refined)
            {
                w.TryBodyState(rq.From, dep.BurnTime, out Vector3d bpD2, out Vector3d bvD2);
                prop.TryPropagate(dep.BurnTime, bpD2 + dep.RShip, bvD2 + dep.VShip + dvDep, tTcm, out wpTcm, out wvTcm);
                w.TryBodyState(parent, tTcm, out Vector3d ppar2, out Vector3d vpar2);
                rTcm = wpTcm - ppar2; vTcm = wvTcm - vpar2;
                prop.TryPropagate(tTcm, wpTcm, wvTcm + dvTcm, tPeri, out capWp, out capWv);
                w.TryBodyState(rq.To, tPeri, out Vector3d bpT2, out Vector3d bvT2);
                capRel = capWp - bpT2; capVel = capWv - bvT2;
            }
            else
            {
                UniversalKepler.TryPropagate(spec.MuCentral, dep.Transfer.RFrom, dep.Transfer.VTransferDep, tTcm - dep.Transfer.T0, out rTcm, out vTcm);
                w.TryBodyState(parent, tTcm, out Vector3d ppar, out Vector3d vpar);
                wpTcm = ppar + rTcm; wvTcm = vpar + vTcm;
                w.TryBodyState(rq.To, tPeri, out Vector3d bpT, out Vector3d bvT);
                capRel = aim.PeriapsisPosition; capVel = aim.PeriapsisVelocity; capWp = bpT + capRel; capWv = bvT + capVel;
            }

            AddImpulse(d, w, ImpulseRole.Correction, tTcm, parent, dvTcm, "КОРРЕКЦИЯ 1", spec.MuCentral, rTcm, vTcm, 0d, forceState: true, worldPos: wpTcm, worldVel: wvTcm);
            // Захват: касательно к скорости в периапсисе до скорости орбиты (круг: √(μ/r), эллипс: √(μ(1+e)/r)).
            // CaptureKind.None — это пролёт мимо цели без торможения: цель по-прежнему нужна для прицеливания
            // (aim выше), но импульса захвата в плане быть не должно, иначе цена окна и план расходятся.
            if (rq.Capture != CaptureKind.None)
            {
                double rCap = capRel.Magnitude;
                double vTarget = rq.Capture == CaptureKind.Elliptic ? Math.Sqrt(spec.MuTo * (1d + rq.CaptureEccentricity) / rCap) : Math.Sqrt(spec.MuTo / rCap);
                Vector3d capDv = CaptureBurn(capRel, capVel, vTarget);
                AddImpulse(d, w, ImpulseRole.Capture, tPeri, rq.To, capDv, "ЗАХВАТ", spec.MuTo, capRel, capVel, 0d, forceState: true, worldPos: capWp, worldVel: capWv);
            }
            d.RefinedDv = d.TotalDv;
            d.Valid = true;
            yield break;
        }

        private static double PrepDv(PlanDraft d)
        {
            double s = 0d;
            foreach (PlannedImpulse i in d.Impulses)
            {
                if (i.Role == ImpulseRole.PrepOrbit) { s += i.DvWorld.Magnitude; }
            }

            return s;
        }

        /// <summary>
        /// Импульс захвата в периапсисе: после него скорость строго перпендикулярна радиусу
        /// (круг или эллипс с периапсисом r). Прежняя форма capVel.normalized·(vTarget−|capVel|)
        /// оставляла радиальную составляющую, и после n-body прицеливания орбита получала e ≈ v_r/v_c.
        /// </summary>
        internal static Vector3d CaptureBurn(Vector3d relPos, Vector3d relVel, double vTarget)
        {
            double rn = relPos.Magnitude;
            Vector3d radialHat = rn > 0d ? relPos / rn : Vector3d.Zero;
            Vector3d tangential = relVel - (radialHat * Vector3d.Dot(relVel, radialHat));
            Vector3d tangentHat = tangential.Magnitude > 1e-9d * Math.Max(1d, relVel.Magnitude)
                ? tangential.Normalized
                : relVel.Normalized;
            return (tangentHat * vTarget) - relVel;
        }

        private static void SetLastDeparture(PlanDraft d, Vector3d dv)
        {
            for (int i = d.Impulses.Count - 1; i >= 0; i--)
            {
                if (d.Impulses[i].Role != ImpulseRole.Departure) { continue; }
                PlannedImpulse p = d.Impulses[i]; p.DvWorld = dv; d.Impulses[i] = p; return;
            }
        }

        private static Vector3d PropagateRel(double mu, Vector3d r, Vector3d v, double dt, out Vector3d vOut)
        {
            UniversalKepler.TryPropagate(mu, r, v, dt, out Vector3d r2, out vOut);
            return r2;
        }

        private static void AddImpulse(PlanDraft d, IPlannerWorld w, ImpulseRole role, double t, int frameBody, Vector3d dv, string label,
            double mu, Vector3d relPos, Vector3d relVel, double now, bool forceState = false, Vector3d? worldPos = null, Vector3d? worldVel = null)
        {
            Vector3d rp = relPos, vp = relVel;
            if (!forceState) { UniversalKepler.TryPropagate(mu, relPos, relVel, t - now, out rp, out vp); }
            Vector3d wp, wv;
            if (worldPos.HasValue) { wp = worldPos.Value; wv = worldVel.Value; }
            else { w.TryBodyState(frameBody, t, out Vector3d bp, out Vector3d bv); wp = bp + rp; wv = bv + vp; }
            d.Impulses.Add(new PlannedImpulse { Role = role, Time = t, FrameBody = frameBody, DvWorld = dv, RelPos = rp, RelVel = vp, WorldPos = wp, WorldVel = wv, Label = label });
        }
    }

    public sealed class FlybyRequest
    {
        public int From, Via, To;
        public CaptureKind Capture = CaptureKind.Circular;
        public double ArrivalAltitudeM = 400e3, CaptureEccentricity;
        public double SearchStartS, SearchSpanS;
        public double LeadTimeS = 300d;
        public double ShortHorizonS = 3d * 86400d;
    }

    public static class FlybyPlanner
    {
        /// <summary>
        /// Допустимый недобор поворота у гиганта, м/с. Оптимум пролёта лежит ровно на границе
        /// rp = rpMin, поэтому дуга с настоящей орбиты корабля почти всегда просит чуть больше
        /// поворота; такой недобор (единицы м/с) поглощается коррекцией 2, а не отменяет план.
        /// </summary>
        public const double FlybyDeficitToleranceM = 2d;

        public static bool TryBuildSpec(IPlannerWorld w, FlybyRequest rq, double now, double shipRadius, out FlybySpec spec, out string why)
        {
            spec = null;
            if (!PlannerEngine.TryCommonParent(w, rq.From, rq.Via, out int parent, out why)) { return false; }
            if (w.ParentOf(rq.To) != parent || rq.To == rq.Via || rq.To == rq.From) { why = "v1: старт, гигант и цель должны обращаться вокруг одного тела."; return false; }
            var from = new BodyTrack(w, rq.From, parent);
            if (!from.TryEvaluate(now, out Vector3d r, out Vector3d v)) { why = "Нет эфемерид тела старта."; return false; }
            spec = new FlybySpec
            {
                MuCentral = w.Mu(parent), MinCentralRadius = 1.05d * w.Radius(parent),
                From = from, Via = new BodyTrack(w, rq.Via, parent), To = new BodyTrack(w, rq.To, parent),
                Normal = Vector3d.Cross(r, v).Normalized, Prograde = true,
                MuFrom = w.Mu(rq.From), ParkRadiusFrom = shipRadius,
                MuVia = w.Mu(rq.Via), ViaMinPeriapsis = w.Radius(rq.Via) + w.SafeAltitude(rq.Via), ViaSoiRadius = w.SoiRadius(rq.Via),
                MuTo = w.Mu(rq.To), ParkRadiusTo = w.Radius(rq.To) + rq.ArrivalAltitudeM, Capture = rq.Capture, CaptureEccentricity = rq.CaptureEccentricity,
            };
            return true;
        }

        /// <summary>Окна по умолчанию: старт — синодический период пары (старт, гигант), участки 0.35…2.0 хоманова каждый.</summary>
        public static void DefaultWindows(IPlannerWorld w, FlybyRequest rq, double now,
            out double t0Lo, out double t0Hi, out double t1Lo, out double t1Hi, out double t2Lo, out double t2Hi)
        {
            PlannerEngine.TryCommonParent(w, rq.From, rq.Via, out int parent, out _);
            double mu = w.Mu(parent);
            double Hohmann(int a, int b, out double period)
            {
                new BodyTrack(w, a, parent).TryEvaluate(now, out Vector3d ra, out Vector3d va);
                new BodyTrack(w, b, parent).TryEvaluate(now, out Vector3d rb, out Vector3d vb);
                double aa = 1d / ((2d / ra.Magnitude) - (va.SqrMagnitude / mu)), ab = 1d / ((2d / rb.Magnitude) - (vb.SqrMagnitude / mu));
                double ta = 2d * Math.PI * Math.Sqrt(aa * aa * aa / mu), tb = 2d * Math.PI * Math.Sqrt(ab * ab * ab / mu);
                period = Math.Abs(ta - tb) > 1e-3d * Math.Min(ta, tb) ? ConicMath.SynodicPeriod(ta, tb) : 2d * Math.Max(ta, tb);
                double at = 0.5d * (aa + ab);
                return Math.PI * Math.Sqrt(at * at * at / mu);
            }

            double h1 = Hohmann(rq.From, rq.Via, out double syn1), h2 = Hohmann(rq.Via, rq.To, out _);
            t0Lo = rq.SearchStartS > 0d ? rq.SearchStartS : now + rq.LeadTimeS;
            t0Hi = t0Lo + (rq.SearchSpanS > 0d ? rq.SearchSpanS : 1.1d * syn1);
            t1Lo = t0Lo + (0.35d * h1); t1Hi = t0Hi + (2.0d * h1);
            t2Lo = t1Lo + (0.35d * h2); t2Hi = t1Hi + (2.0d * h2);
        }

        public static List<FlybySolution> FindWindows(FlybySpec spec, double t0Lo, double t0Hi, double t1Lo, double t1Hi, double t2Lo, double t2Hi)
        {
            return FlybySearch.Search(spec, t0Lo, t0Hi, 48, t1Lo, t1Hi, 64, t2Lo, t2Hi, 64, 10);
        }

        /// <summary>
        /// Отлёт → TCM1 (40 % участка 1) → [манёвр в периапсисе у гиганта] → TCM2 (40 % участка 2) → захват.
        /// n-body: (отлёт min-norm + TCM1) целятся в периапсис у гиганта в t1; затем TCM2 — в периапсис у цели в t2.
        /// </summary>
        public static PlanDraft MakeDraft(IPlannerWorld w, FlybyRequest rq, FlybySpec spec, FlybySolution win, IPlannerPropagator prop)
        {
            var d = new PlanDraft();
            ShootingCorrector.Drive(MakeDraftCo(w, rq, spec, win, prop, d));
            return d;
        }

        public static IEnumerable<ShootingCorrector.Shot> MakeDraftCo(IPlannerWorld w, FlybyRequest rq, FlybySpec spec, FlybySolution win, IPlannerPropagator prop, PlanDraft d)
        {
            d.IdealDv = win.Total; d.DepartTime = win.T0; d.ArriveTime = win.T2;
            double now = w.UniverseNow;
            if (!win.Feasible && win.Flyby.DeficitDv > FlybyDeficitToleranceM)
            {
                d.Note = "Планета не может повернуть вектор настолько (недобор поворота " + win.Flyby.DeficitDv.ToString("F0") + " м/с). Выберите другое окно.";
                yield break;
            }
            if (!w.TryShipState(out Vector3d sp, out Vector3d sv)) { d.Note = "Нет состояния корабля."; yield break; }
            if (w.ShipBody() != rq.From) { d.Note = "Корабль не в сфере действия тела старта."; yield break; }
            w.TryBodyState(rq.From, now, out Vector3d bp, out Vector3d bv);
            Vector3d rRel = sp - bp, vRel = sv - bv;
            double muA = w.Mu(rq.From), minPer = w.Radius(rq.From) + w.SafeAltitude(rq.From);
            int parent = w.ParentOf(rq.From);

            // 1) отлёт к гиганту с орбиты корабля (участок 1)
            var leg1 = new TransferSpec
            {
                MuCentral = spec.MuCentral, CentralRadius = 0d, MinCentralRadius = spec.MinCentralRadius, From = spec.From, To = spec.Via, Normal = spec.Normal, Prograde = true,
                MuFrom = spec.MuFrom, ParkRadiusFrom = spec.ParkRadiusFrom, MuTo = spec.MuVia, ParkRadiusTo = spec.ViaMinPeriapsis, Capture = CaptureKind.None,
            };
            bool exact = prop != null && win.T0 - now <= rq.ShortHorizonS;
            double period = 2d * Math.PI * Math.Sqrt(Math.Pow(1d / Math.Abs((2d / rRel.Magnitude) - (vRel.SqrMagnitude / muA)), 3d) / muA);
            double phaseSearch = exact ? 30d * 86400d : 3d * 86400d;
            if (!double.IsFinite(period) || period > phaseSearch) { period = phaseSearch; }
            IShipTrack ship = exact ? (IShipTrack)new NBodyShipTrack(w, prop, rq.From, now, sp, sv) : new KeplerShipTrack(muA, now, rRel, vRel);
            d.Preliminary = !exact && win.T0 - now > rq.ShortHorizonS;
            double from = Math.Max(now + rq.LeadTimeS, win.T0 - period);
            if (!ShipDeparture.Find(leg1, muA, minPer, ship, win.T1, from, Math.Max(from + period, win.T0 + period), 600, out DeparturePlan dep)) { d.Note = "Не удалось построить отлёт с текущей орбиты."; yield break; }
            d.DepartTime = dep.BurnTime;

            // 2) пролёт: v∞ на входе от дуги этого отлёта, на выходе — из решения
            spec.Via.TryEvaluate(win.T1, out _, out Vector3d vG);
            Vector3d vInfIn = dep.Transfer.VTransferArr - vG, vInfOut = win.VInfOut;
            FlybyResult fl = FlybyGeometry.Evaluate(spec.MuVia, spec.ViaMinPeriapsis, spec.ViaSoiRadius, vInfIn, vInfOut);
            // Оптимум гравманёвра всегда лежит на стенке rp = rpMin (больше поворота — дешевле),
            // поэтому дуга от НАСТОЯЩЕЙ орбиты корабля может попросить на тысячные доли градуса
            // больше, чем даёт стенка. Такой недобор стоит миллиметры в секунду и закрывается
            // КОРРЕКЦИЕЙ 2 — из-за него нельзя выбрасывать весь план. Реальный недобор (сотни м/с)
            // по-прежнему отменяет план.
            double deficitTol = FlybyDeficitToleranceM + (1e-4d * (dep.Burn.Dv + win.ArrDv));
            if (!fl.Feasible && fl.DeficitDv > deficitTol)
            {
                d.Note = "С вашей орбиты пролёт не стыкуется (недобор поворота " + fl.DeficitDv.ToString("F0") + " м/с). Пересчитайте окно.";
                yield break;
            }

            // Цена патч-коник считается по ФАКТИЧЕСКОЙ дуге отлёта и ФАКТИЧЕСКОМУ пролёту
            // (fl), а не по идеальным значениям окна: иначе подпись «ПАТЧ-КОНИКИ» расходится
            // со списком импульсов ниже.
            d.PatchedDv = dep.Burn.Dv + fl.PoweredDv + win.ArrDv;
            if (!fl.Feasible)
            {
                d.Note = "Недобор поворота " + fl.DeficitDv.ToString("0.###") + " м/с закрывается КОРРЕКЦИЕЙ 2.";
            }

            d.FlybyPeriapsisRadius = fl.PeriapsisRadius;
            var inHyp = new HyperbolaAtPeriapsis { Valid = true, AxisX = fl.PeriapsisDir, MotionY = fl.MotionDir, Rp = fl.PeriapsisRadius, Vp = fl.SpeedPeriIn, Normal = Vector3d.Cross(fl.PeriapsisDir, fl.MotionDir) };
            Vector3d dvDep = dep.Burn.DvWorld;
            double tTcm1 = dep.BurnTime + (0.4d * (win.T1 - dep.BurnTime)), tTcm2 = win.T1 + (0.4d * (win.T2 - win.T1));

            // захват у цели по участку 2
            if (!CaptureGeometry.TryIncoming(spec.MuTo, win.VInfArr, spec.ParkRadiusTo, spec.Normal, out HyperbolaAtPeriapsis aimB)) { d.Note = "Нет геометрии захвата."; yield break; }

            Vector3d dvTcm1 = Vector3d.Zero, dvTcm2 = Vector3d.Zero, dvFly = fl.MotionDir * (fl.SpeedPeriOut - fl.SpeedPeriIn);
            Vector3d wpT1 = Vector3d.Zero, wvT1 = Vector3d.Zero, wpT2 = Vector3d.Zero, wvT2 = Vector3d.Zero, wpFly = Vector3d.Zero, wvFly = Vector3d.Zero, wpCap = Vector3d.Zero, wvCap = Vector3d.Zero;
            bool refined = false;
            if (prop != null)
            {
                const double dayS = 86400d;
                w.TryBodyState(rq.From, dep.BurnTime, out Vector3d bpD, out Vector3d bvD);
                Vector3d p0 = bpD + dep.RShip, v0 = bvD + dep.VShip;
                CaptureGeometry.TryPositionBefore(spec.MuVia, inHyp, 5d * dayS, out Vector3d farG);
                CaptureGeometry.TryPositionBefore(spec.MuVia, inHyp, 0.5d * dayS, out Vector3d midG);
                var m1 = new PlanShootingModel(w, prop, rq.Via, dep.BurnTime, p0, v0);
                var s0 = new ShootingCorrector.Shot { Model = m1, Dv = dvDep, Time = win.T1 };
                yield return s0;
                d.MissBeforeM = s0.Ok ? (s0.RelPos - inHyp.PeriapsisPosition).Magnitude : double.NaN;
                var resA = new ShootingCorrector.Result();
                foreach (ShootingCorrector.Shot sh in ShootingCorrector.SolveCo(m1, win.T1 - (5d * dayS), dvDep, farG, 5e8d, 8, 0.05d, resA)) { yield return sh; }
                Vector3d dvDepFixed = resA.Dv;
                var m1b = new PlanShootingModel(w, prop, rq.Via, dep.BurnTime, p0, v0) { VariableTime = tTcm1 };
                m1b.Fixed.Add((dep.BurnTime, dvDepFixed));
                var ladder1 = new[]
                {
                    new ShootingCorrector.Aim { Time = win.T1 - (5d * dayS), Position = farG, ToleranceM = 1e6d },
                    new ShootingCorrector.Aim { Time = win.T1 - (0.5d * dayS), Position = midG, ToleranceM = 5e4d },
                    new ShootingCorrector.Aim { Time = win.T1, Position = inHyp.PeriapsisPosition, ToleranceM = 1e4d },
                };
                var res1 = new ShootingCorrector.Result();
                foreach (ShootingCorrector.Shot sh in ShootingCorrector.SolveLadderCo(m1b, Vector3d.Zero, ladder1, 12, 0.01d, res1)) { yield return sh; }
                dvTcm1 = res1.Dv; double miss1 = res1.Miss;
                // Лучший результат берётся и при частичной сходимости (промах ≪ радиуса сферы действия): выбрасывать найденное хуже
                if (miss1 < 1e6d)
                {
                    dvDep = dvDepFixed;
                    prop.TryPropagate(dep.BurnTime, p0, v0 + dvDep, tTcm1, out wpT1, out wvT1);
                    prop.TryPropagate(tTcm1, wpT1, wvT1 + dvTcm1, win.T1, out wpFly, out wvFly);
                    w.TryBodyState(rq.Via, win.T1, out Vector3d bpG, out Vector3d bvG);
                    Vector3d vRelG = wvFly - bvG;
                    dvFly = vRelG.Normalized * (fl.SpeedPeriOut - vRelG.Magnitude);   // доводка до скорости выхода по ФАКТИЧЕСКОМУ периапсису
                    CaptureGeometry.TryPositionBefore(spec.MuTo, aimB, 20d * dayS, out Vector3d farB);
                    CaptureGeometry.TryPositionBefore(spec.MuTo, aimB, 2d * dayS, out Vector3d midB);
                    var m2 = new PlanShootingModel(w, prop, rq.To, win.T1, wpFly, wvFly + dvFly) { VariableTime = tTcm2 };
                    var ladder2 = new[]
                    {
                        new ShootingCorrector.Aim { Time = win.T2 - (20d * dayS), Position = farB, ToleranceM = 1e6d },
                        new ShootingCorrector.Aim { Time = win.T2 - (2d * dayS), Position = midB, ToleranceM = 5e4d },
                        new ShootingCorrector.Aim { Time = win.T2, Position = aimB.PeriapsisPosition, ToleranceM = 1e4d },
                    };
                    var res2 = new ShootingCorrector.Result();
                    foreach (ShootingCorrector.Shot sh in ShootingCorrector.SolveLadderCo(m2, Vector3d.Zero, ladder2, 12, 0.01d, res2)) { yield return sh; }
                    bool ok2 = res2.Ok; double miss2 = res2.Miss; dvTcm2 = res2.Dv;
                    d.MissAfterM = miss2;
                    if (miss2 < 1e6d)
                    {
                        refined = true;
                        prop.TryPropagate(win.T1, wpFly, wvFly + dvFly, tTcm2, out wpT2, out wvT2);
                        prop.TryPropagate(tTcm2, wpT2, wvT2 + dvTcm2, win.T2, out wpCap, out wvCap);
                        if (!ok2) { d.Note = "Участок 2: промах " + miss2.ToString("F0") + " м (допуск не достигнут, но ≪ радиуса планеты)."; }
                    }
                    else { d.Note = "n-body уточнение участка 2 не сошлось (промах " + (miss2 / 1e3).ToString("F0") + " км)."; }
                }
                else { d.Note = "n-body уточнение участка 1 не сошлось (промах " + (miss1 / 1e3).ToString("F0") + " км)."; }
            }

            d.Refined = refined;
            if (!refined)
            {
                // патч-коники: состояния из дуг Ламберта. Импульсы отлёта и пролёта тоже возвращаем к патч-коникам:
                // если участок 1 сошёлся, а участок 2 нет, иначе в плане останется смесь n-body-импульсов с
                // патч-коническими состояниями (цена/подпись «ПАТЧ-КОНИКИ» перестают соответствовать плану).
                dvTcm1 = dvTcm2 = Vector3d.Zero;
                dvDep = dep.Burn.DvWorld;
                dvFly = fl.MotionDir * (fl.SpeedPeriOut - fl.SpeedPeriIn);
                UniversalKepler.TryPropagate(spec.MuCentral, dep.Transfer.RFrom, dep.Transfer.VTransferDep, tTcm1 - dep.BurnTime, out Vector3d r1, out Vector3d vv1);
                w.TryBodyState(parent, tTcm1, out Vector3d pp1, out Vector3d pv1); wpT1 = pp1 + r1; wvT1 = pv1 + vv1;
                w.TryBodyState(rq.Via, win.T1, out Vector3d bpG, out Vector3d bvG); wpFly = bpG + inHyp.PeriapsisPosition; wvFly = bvG + inHyp.PeriapsisVelocity;
                UniversalKepler.TryPropagate(spec.MuCentral, win.RVia, win.VLeg2Dep, tTcm2 - win.T1, out Vector3d r2, out Vector3d vv2);
                w.TryBodyState(parent, tTcm2, out Vector3d pp2, out Vector3d pv2); wpT2 = pp2 + r2; wvT2 = pv2 + vv2;
                w.TryBodyState(rq.To, win.T2, out Vector3d bpB, out Vector3d bvB); wpCap = bpB + aimB.PeriapsisPosition; wvCap = bvB + aimB.PeriapsisVelocity;
            }

            // собираем список импульсов с узловыми состояниями (относительно FrameBody)
            void Add(ImpulseRole role, double t, int frame, Vector3d dv, string label, Vector3d wp, Vector3d wv)
            {
                w.TryBodyState(frame, t, out Vector3d fb, out Vector3d fv);
                d.Impulses.Add(new PlannedImpulse { Role = role, Time = t, FrameBody = frame, DvWorld = dv, RelPos = wp - fb, RelVel = wv - fv, WorldPos = wp, WorldVel = wv, Label = label });
            }

            w.TryBodyState(rq.From, dep.BurnTime, out Vector3d bpA, out Vector3d bvA);
            Add(ImpulseRole.Departure, dep.BurnTime, rq.From, dvDep, "ОТЛЁТ", bpA + dep.RShip, bvA + dep.VShip);
            Add(ImpulseRole.Correction, tTcm1, parent, dvTcm1, "КОРРЕКЦИЯ 1", wpT1, wvT1);
            Add(ImpulseRole.FlybyBurn, win.T1, rq.Via, dvFly, "ПЕРИАПСИС У ГИГАНТА", wpFly, wvFly);
            Add(ImpulseRole.Correction, tTcm2, parent, dvTcm2, "КОРРЕКЦИЯ 2", wpT2, wvT2);
            // CaptureKind.None — пролёт мимо конечной цели: прицеливание в aimB остаётся, торможения в плане нет.
            if (rq.Capture != CaptureKind.None)
            {
                w.TryBodyState(rq.To, win.T2, out Vector3d bpC, out Vector3d bvC);
                Vector3d vRelC = wvCap - bvC, rRelC = wpCap - bpC;
                double vT = rq.Capture == CaptureKind.Elliptic ? Math.Sqrt(spec.MuTo * (1d + rq.CaptureEccentricity) / rRelC.Magnitude) : Math.Sqrt(spec.MuTo / rRelC.Magnitude);
                Add(ImpulseRole.Capture, win.T2, rq.To, PlannerEngine.CaptureBurn(rRelC, vRelC, vT), "ЗАХВАТ", wpCap, wvCap);
            }
            d.RefinedDv = d.TotalDv;
            d.Valid = true;
            yield break;
        }
    }

    /// <summary>Модель «импульс → положение относительно тела-цели» поверх IPlannerPropagator: фиксированные импульсы + один переменный.</summary>
    public sealed class PlanShootingModel : IShootingModel
    {
        private readonly IPlannerWorld world; private readonly IPlannerPropagator prop; private readonly int target;
        private readonly double t0; private readonly Vector3d p0, v0;
        public readonly List<(double time, Vector3d dv)> Fixed = new List<(double, Vector3d)>();
        /// <summary>NaN — переменный импульс прикладывается в момент старта t0 (отлёт).</summary>
        public double VariableTime = double.NaN;

        public PlanShootingModel(IPlannerWorld world, IPlannerPropagator prop, int target, double t0, Vector3d worldPos, Vector3d worldVel)
        { this.world = world; this.prop = prop; this.target = target; this.t0 = t0; p0 = worldPos; v0 = worldVel; }

        public bool TryShoot(Vector3d dvVar, double targetTime, out Vector3d relPos)
        {
            relPos = Vector3d.Zero;
            var events = new List<(double t, Vector3d dv)>(Fixed);
            events.Add((double.IsNaN(VariableTime) ? t0 : VariableTime, dvVar));
            events.Sort((a, b) => a.t.CompareTo(b.t));
            double t = t0; Vector3d p = p0, v = v0;
            foreach ((double te, Vector3d dv) in events)
            {
                if (te > targetTime) { break; }
                if (te > t && !prop.TryPropagate(t, p, v, te, out p, out v)) { return false; }
                t = Math.Max(t, te); v += dv;
            }

            if (targetTime > t && !prop.TryPropagate(t, p, v, targetTime, out p, out v)) { return false; }
            if (!world.TryBodyState(target, targetTime, out Vector3d bp, out _)) { return false; }
            relPos = p - bp;
            return true;
        }
    }
}

