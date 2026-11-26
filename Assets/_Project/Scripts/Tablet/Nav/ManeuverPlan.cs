using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Planning;

namespace Galilego.Tablet
{
    /// <summary>Роль манёвра на карточке; Manual — созданный игроком вручную.</summary>
    public enum ManeuverRole { Manual = 0, PrepOrbit = 1, Departure = 2, Correction = 3, FlybyBurn = 4, Capture = 5 }

    /// <summary>Состояние тела в мировой СК (для пересчёта осей манёвра при смене системы координат).</summary>
    public interface IBodyStateSource
    {
        bool TryGetBodyState(int bodyIndex, double universeTimeS, out Vector3d position, out Vector3d velocity);
    }

    /// <summary>One impulsive maneuver. Axes are tangent / normal / binormal in the frame of <see cref="FrameBody"/>.</summary>
    public sealed class Maneuver
    {
        /// <summary>Seconds from now until the burn starts (counts down in real time).</summary>
        public double TimeToStartS;

        /// <summary>ΔV along the velocity relative to FrameBody, m/s.</summary>
        public double Tangent;

        /// <summary>ΔV in the orbital plane, perpendicular to velocity (towards the centre), m/s.</summary>
        public double Normal;

        /// <summary>ΔV perpendicular to the orbital plane (along r × v), m/s.</summary>
        public double Binormal;

        /// <summary>Index of the body whose frame defines the axes.</summary>
        public int FrameBody;

        /// <summary>Burn duration, filled by <see cref="ManeuverPlan.ComputeBurns"/>.</summary>
        public double BurnTimeS;

        /// <summary>0 — ручной манёвр; иначе номер решения планировщика, создавшего его.</summary>
        public int PlannerId;

        public ManeuverRole Role;

        /// <summary>
        /// СК манёвра задана планировщиком (отлёт — тело старта, захват — тело цели, коррекция — родитель). Общий селектор
        /// «СИСТЕМА КООРДИНАТ МАНЁВРА» такой манёвр не трогает; смена идёт через <see cref="ManeuverPlan.ReframeManeuver"/>,
        /// которая пересчитывает T/N/B так, чтобы физический импульс остался прежним.
        /// </summary>
        public bool FrameLocked;

        /// <summary>Решение планировщика (не зависит от СК): импульс в инерциальных осях и момент старта на часах плана.</summary>
        public Vector3d PlannedDvWorld;
        public double PlannedStartClockS;

        /// <summary>
        /// Состояние корабля в момент импульса по последнему прогнозу (заполняет NavMapModel.ApplyBurn): мировое и
        /// относительно тела FrameBody. По нему T/N/B пересчитываются в мировой вектор и обратно.
        /// </summary>
        public bool NodeValid;
        public double NodeTimeUniverseS;
        public Vector3d NodeWorldPos, NodeWorldVel;
        public Vector3d NodeRelPos, NodeRelVel;

        public double DvMagnitude
        {
            get { return Math.Sqrt(Tangent * Tangent + Normal * Normal + Binormal * Binormal); }
        }

        public double GetDv(int axis)
        {
            return axis == 0 ? Tangent : axis == 1 ? Normal : Binormal;
        }

        internal void SetDvRaw(int axis, double v)
        {
            if (axis == 0) { Tangent = v; }
            else if (axis == 1) { Normal = v; }
            else { Binormal = v; }
        }
    }

    /// <summary>
    /// Ordered list of maneuvers. List order is application order. If a maneuver starts later than the
    /// next one in the list, it is in conflict: the UI paints it red and the predicted line is cut at its start time.
    /// Every edit bumps <see cref="Version"/> and lowers <see cref="DirtyFrom"/>, so the trajectory worker can
    /// recompute only from the first changed maneuver.
    /// </summary>
    public sealed class ManeuverPlan
    {
        /// <summary>Earliest allowed start. 0 would execute immediately and delete the maneuver.</summary>
        public const double MinStartS = 5d;
        /// <summary>Предела нет: межпланетные манёвры (≫ 6 км/с) нельзя обрезать. Защита только от NaN/∞.</summary>
        public const double MaxDvPerAxis = double.MaxValue;
        public const double StandardGravity = 9.80665d;

        private readonly List<Maneuver> items = new List<Maneuver>();

        public IList<Maneuver> Items { get { return items.AsReadOnly(); } }
        public int Count { get { return items.Count; } }
        public int Version { get; private set; }

        /// <summary>Часы плана: растут вместе с AdvanceTime. StartClock = ClockS + TimeToStartS не меняется при обратном отсчёте.</summary>
        public double ClockS { get; private set; }

        /// <summary>Lowest list index edited since <see cref="ClearDirty"/>; int.MaxValue when nothing changed.</summary>
        public int DirtyFrom { get; private set; } = int.MaxValue;

        public Maneuver this[int i] { get { return items[i]; } }

        /// <summary>Index of the first maneuver that starts later than its successor, or −1.</summary>
        public int ConflictIndex
        {
            get
            {
                for (int i = 0; i < items.Count - 1; i++)
                {
                    if (items[i].TimeToStartS > items[i + 1].TimeToStartS) { return i; }
                }

                return -1;
            }
        }

        /// <summary>Maneuvers that are applied to the predicted trajectory (everything before the conflict).</summary>
        public int ValidCount { get { int c = ConflictIndex; return c < 0 ? items.Count : c; } }

        /// <summary>Time at which the maneuver line is cut because of a conflict; +∞ when there is none.</summary>
        public double CutTimeS { get { int c = ConflictIndex; return c < 0 ? double.PositiveInfinity : items[c].TimeToStartS; } }

        public double NextStartS
        {
            get
            {
                double best = double.PositiveInfinity;
                for (int i = 0; i < items.Count; i++) { best = Math.Min(best, items[i].TimeToStartS); }
                return best;
            }
        }

        public double TotalDv
        {
            get { double s = 0d; for (int i = 0; i < items.Count; i++) { s += items[i].DvMagnitude; } return s; }
        }

        public double TotalBurnS
        {
            get { double s = 0d; for (int i = 0; i < items.Count; i++) { s += items[i].BurnTimeS; } return s; }
        }

        /// <summary>Appends a maneuver after the last one (default: after the latest start plus a gap).</summary>
        public Maneuver Add(int frameBody, double gapS)
        {
            double last = 0d;
            for (int i = 0; i < items.Count; i++) { last = Math.Max(last, items[i].TimeToStartS); }
            var m = new Maneuver { TimeToStartS = Math.Max(MinStartS, last + Math.Max(gapS, 1d)), FrameBody = frameBody };
            items.Add(m);
            Touch(items.Count - 1);
            return m;
        }

        public bool Remove(int i)
        {
            if (i < 0 || i >= items.Count) { return false; }
            items.RemoveAt(i);
            Touch(i);
            return true;
        }

        /// <summary>Swaps with the previous maneuver.</summary>
        public bool MoveUp(int i)
        {
            if (i <= 0 || i >= items.Count) { return false; }
            Swap(i, i - 1);
            return true;
        }

        /// <summary>Swaps with the next maneuver.</summary>
        public bool MoveDown(int i)
        {
            if (i < 0 || i >= items.Count - 1) { return false; }
            Swap(i, i + 1);
            return true;
        }

        public void SetTime(int i, double seconds)
        {
            if (double.IsNaN(seconds)) { return; }
            items[i].TimeToStartS = Math.Max(MinStartS, seconds);
            Touch(i);
        }

        /// <summary>The "0" button next to the time: earliest possible start.</summary>
        public void ResetTime(int i)
        {
            SetTime(i, MinStartS);
        }

        public void SetDv(int i, int axis, double v)
        {
            if (!double.IsFinite(v)) { return; }
            items[i].SetDvRaw(axis, Math.Max(-MaxDvPerAxis, Math.Min(MaxDvPerAxis, v)));
            Touch(i);
        }

        public void ResetDv(int i, int axis)
        {
            SetDv(i, axis, 0d);
        }

        /// <summary>
        /// Общий селектор «СИСТЕМА КООРДИНАТ МАНЁВРА»: применяется к РУЧНЫМ манёврам (как раньше). Манёвры планировщика
        /// (FrameLocked) не трогаются: молча сменив им СК, мы изменили бы физический импульс и сломали решение.
        /// </summary>
        public void SetFrameBody(int body)
        {
            bool any = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].FrameLocked) { continue; }
                items[i].FrameBody = body;
                any = true;
            }

            if (any) { Touch(0); }
        }

        public double StartClockS(Maneuver m) { return ClockS + m.TimeToStartS; }

        /// <summary>Мировой вектор импульса по текущим T/N/B и последнему состоянию узла. false — состояния ещё нет.</summary>
        public static bool TryGetDvWorld(Maneuver m, out Vector3d dvWorld)
        {
            dvWorld = Vector3d.Zero;
            return m.NodeValid && ManeuverAxes.TryToWorld(m.NodeRelPos, m.NodeRelVel, new Vector3d(0d, 0d, 1d), m.Tangent, m.Normal, m.Binormal, out dvWorld);
        }

        /// <summary>Манёвр планировщика, который игрок изменил (время или импульс). Ручной — всегда false.</summary>
        public bool IsEdited(Maneuver m)
        {
            if (m.PlannerId == 0) { return false; }
            if (Math.Abs(StartClockS(m) - m.PlannedStartClockS) > 1d) { return true; }
            return TryGetDvWorld(m, out Vector3d dv) && (dv - m.PlannedDvWorld).Magnitude > 0.05d;
        }

        /// <summary>
        /// Добавляет манёвр решения планировщика в порядке времени. Оси T/N/B считаются по состоянию узла из модели
        /// планировщика (до первого прогноза); прогноз потом заменит его своим (NodeValid обновится в ApplyBurn).
        /// </summary>
        public Maneuver AddPlanned(ManeuverRole role, int plannerId, int frameBody, double timeToStartS,
            Vector3d dvWorld, Vector3d nodeRelPos, Vector3d nodeRelVel, Vector3d nodeWorldPos, Vector3d nodeWorldVel, double nodeTimeUniverseS)
        {
            var m = new Maneuver
            {
                TimeToStartS = Math.Max(MinStartS, timeToStartS), FrameBody = frameBody, PlannerId = plannerId, Role = role,
                FrameLocked = true, PlannedDvWorld = dvWorld, NodeValid = true, NodeTimeUniverseS = nodeTimeUniverseS,
                NodeRelPos = nodeRelPos, NodeRelVel = nodeRelVel, NodeWorldPos = nodeWorldPos, NodeWorldVel = nodeWorldVel,
            };
            if (dvWorld.Magnitude > 0d && ManeuverAxes.TryToComponents(nodeRelPos, nodeRelVel, new Vector3d(0d, 0d, 1d), dvWorld, out double t, out double n, out double b))
            {
                m.Tangent = t; m.Normal = n; m.Binormal = b;
            }

            m.PlannedStartClockS = StartClockS(m);
            int at = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].TimeToStartS > m.TimeToStartS) { at = i; break; }
            }

            items.Insert(at, m);
            Touch(at);
            return m;
        }

        /// <summary>Удаляет все манёвры решения (при пересчёте планировщиком).</summary>
        public int RemoveByPlanner(int plannerId)
        {
            int removed = 0, first = int.MaxValue;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].PlannerId != plannerId) { continue; }
                items.RemoveAt(i); removed++; first = Math.Min(first, i);
            }

            if (removed > 0) { Touch(first); }
            return removed;
        }

        /// <summary>«ВЕРНУТЬ РЕШЕНИЕ»: время и импульс как в решении, в текущей СК манёвра.</summary>
        public bool RestorePlanned(int i)
        {
            Maneuver m = items[i];
            if (m.PlannerId == 0 || !m.NodeValid) { return false; }
            if (!ManeuverAxes.TryToComponents(m.NodeRelPos, m.NodeRelVel, new Vector3d(0d, 0d, 1d), m.PlannedDvWorld, out double t, out double n, out double b)) { return false; }
            m.Tangent = t; m.Normal = n; m.Binormal = b;
            m.TimeToStartS = Math.Max(MinStartS, m.PlannedStartClockS - ClockS);
            Touch(i);
            return true;
        }

        /// <summary>
        /// Смена СК ОДНОГО манёвра с сохранением физического импульса: мировой ΔV из старых осей → новые T/N/B
        /// относительно нового тела по тому же состоянию узла. false — нет состояния узла или тела.
        /// </summary>
        public bool ReframeManeuver(int i, int newBody, IBodyStateSource bodies)
        {
            Maneuver m = items[i];
            if (m.FrameBody == newBody) { return true; }
            if (bodies == null || !m.NodeValid || !TryGetDvWorld(m, out Vector3d dv)) { return false; }
            if (!bodies.TryGetBodyState(newBody, m.NodeTimeUniverseS, out Vector3d bp, out Vector3d bv)) { return false; }
            Vector3d r = m.NodeWorldPos - bp, v = m.NodeWorldVel - bv;
            if (!ManeuverAxes.TryToComponents(r, v, new Vector3d(0d, 0d, 1d), dv, out double t, out double n, out double b)) { return false; }
            m.Tangent = t; m.Normal = n; m.Binormal = b;
            m.FrameBody = newBody; m.NodeRelPos = r; m.NodeRelVel = v;
            Touch(i);
            return true;
        }

        /// <summary>
        /// Real-time countdown. Returns (in time order) the maneuvers that are due and valid; they are removed
        /// from the plan — the caller applies their ΔV to the live state. Maneuvers inside a conflict are never executed.
        /// </summary>
        public List<Maneuver> AdvanceTime(double dt)
        {
            var due = new List<Maneuver>();
            if (dt > 0d) { ClockS += dt; }
            if (dt <= 0d || items.Count == 0) { return due; }
            for (int i = 0; i < items.Count; i++) { items[i].TimeToStartS -= dt; }
            int valid = ValidCount;
            for (int i = valid - 1; i >= 0; i--)
            {
                if (items[i].TimeToStartS <= 0d)
                {
                    due.Insert(0, items[i]);
                    items.RemoveAt(i);
                }
            }

            for (int i = 0; i < items.Count; i++) { items[i].TimeToStartS = Math.Max(0d, items[i].TimeToStartS); }
            if (due.Count > 0) { Touch(0); }
            due.Sort((a, b) => a.TimeToStartS.CompareTo(b.TimeToStartS));
            return due;
        }

        /// <summary>
        /// Burn times by the rocket equation, maneuvers taken in time order with mass depletion.
        /// Single engine group for now; later feed it per-stage data from DvTabletAdapter.
        /// </summary>
        public void ComputeBurns(double massKg, double thrustN, double ispS)
        {
            var order = new List<Maneuver>(items);
            order.Sort((a, b) => a.TimeToStartS.CompareTo(b.TimeToStartS));
            double ve = ispS * StandardGravity;
            double mass = massKg;
            foreach (Maneuver m in order)
            {
                double dv = m.DvMagnitude;
                if (thrustN <= 0d || ve <= 0d || mass <= 0d) { m.BurnTimeS = 0d; continue; }
                m.BurnTimeS = mass * (1d - Math.Exp(-dv / ve)) * ve / thrustN;
                mass *= Math.Exp(-dv / ve);
            }
        }

        public void ClearDirty()
        {
            DirtyFrom = int.MaxValue;
        }

        private void Swap(int a, int b)
        {
            Maneuver t = items[a];
            items[a] = items[b];
            items[b] = t;
            Touch(Math.Min(a, b));
        }

        private void Touch(int index)
        {
            Version++;
            DirtyFrom = Math.Min(DirtyFrom, Math.Max(0, index));
        }
    }
}
