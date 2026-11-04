using System;
using System.Collections.Generic;

namespace Galilego.Tablet
{
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
        public const double MaxDvPerAxis = 6000d;
        public const double StandardGravity = 9.80665d;

        private readonly List<Maneuver> items = new List<Maneuver>();

        public IList<Maneuver> Items { get { return items.AsReadOnly(); } }
        public int Count { get { return items.Count; } }
        public int Version { get; private set; }

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
            if (double.IsNaN(v)) { return; }
            items[i].SetDvRaw(axis, Math.Max(-MaxDvPerAxis, Math.Min(MaxDvPerAxis, v)));
            Touch(i);
        }

        public void ResetDv(int i, int axis)
        {
            SetDv(i, axis, 0d);
        }

        /// <summary>Maneuver frame (the "СИСТЕМА КООРДИНАТ МАНЁВРА" selector) applies to every maneuver.</summary>
        public void SetFrameBody(int body)
        {
            for (int i = 0; i < items.Count; i++) { items[i].FrameBody = body; }
            if (items.Count > 0) { Touch(0); }
        }

        /// <summary>
        /// Real-time countdown. Returns (in time order) the maneuvers that are due and valid; they are removed
        /// from the plan — the caller applies their ΔV to the live state. Maneuvers inside a conflict are never executed.
        /// </summary>
        public List<Maneuver> AdvanceTime(double dt)
        {
            var due = new List<Maneuver>();
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
