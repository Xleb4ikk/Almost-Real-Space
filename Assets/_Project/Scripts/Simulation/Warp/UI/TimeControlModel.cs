using System;
using System.Collections.Generic;

namespace Galilego.Universe
{
    /// <summary>Which kind of time acceleration is active.</summary>
    internal enum WarpKind
    {
        /// <summary>Physical warp: the physics stays the same, only more sim time per frame (x1, x2, x3, x4, x6).</summary>
        Physical,

        /// <summary>Full (long) warp: huge factors, ballistics only (x10 ... x1 000 000).</summary>
        Full
    }

    /// <summary>
    /// Everything <see cref="TimeControlPanel"/> draws and nothing else — the view-model.
    /// The panel only READS this and reports what the player did through the events, so
    /// the real rewind / warp logic can later be plugged in without touching the design.
    ///
    /// Track axis = REAL minutes of the flight: point k is created when the k-th real
    /// minute is reached (k = 0 is the start). The time written on a point is the
    /// FLIGHT time (sim time since launch) at that moment, which differs from k minutes
    /// as soon as warp was used.
    /// </summary>
    internal sealed class TimeControlModel
    {
        /// <summary>Sim time since launch, in seconds — the main clock.</summary>
        public double CurrentFlightSeconds;

        /// <summary>Playhead position on the track, in real flight minutes (fractional, e.g. 14.62).</summary>
        public double CurrentRealMinutes;

        /// <summary>
        /// Flight seconds stored at checkpoint k (index = real minute). Points whose index is
        /// greater than <see cref="CurrentRealMinutes"/> are "future": left over after a
        /// rewind, still selectable, overwritten when the playhead passes them again.
        /// </summary>
        public readonly List<double> PointFlightSeconds = new List<double>();

        public WarpKind ActiveKind = WarpKind.Physical;

        /// <summary>Physical factor shown in the slot (one of <see cref="TimeControlPanel.PhysicalFactors"/>).</summary>
        public int PhysicalFactor = 1;

        /// <summary>Index into <see cref="TimeControlPanel.FullFactors"/>, valid when ActiveKind == Full.</summary>
        public int FullIndex = -1;

        /// <summary>Player asked to rewind/jump to this point index (button "Перемотать время").</summary>
        public event Action<int> RewindRequested;

        /// <summary>Player picked a physical factor (x1..x6).</summary>
        public event Action<int> PhysicalFactorChosen;

        /// <summary>Player picked a full warp rung (index into FullFactors).</summary>
        public event Action<int> FullFactorChosen;

        public int PointCount { get { return PointFlightSeconds.Count; } }

        internal void RaiseRewind(int point) { RewindRequested?.Invoke(point); }
        internal void RaisePhysical(int factor) { PhysicalFactorChosen?.Invoke(factor); }
        internal void RaiseFull(int index) { FullFactorChosen?.Invoke(index); }

        /// <summary>
        /// Sample data so the panel can be judged on its own: 14 real minutes of flight,
        /// the first 7 at x1, then jumps through warp (so point times are NOT k minutes).
        /// </summary>
        public static TimeControlModel CreateDemo()
        {
            var m = new TimeControlModel();
            double[] pts =
            {
                0, 60, 120, 180, 240, 300, 360,
                4560, 64560, 70560, 71160, 71760, 72360, 73560, 74160
            };
            m.PointFlightSeconds.AddRange(pts);
            m.CurrentRealMinutes = 14.62d;
            m.CurrentFlightSeconds = 74160d + 37d;
            return m;
        }
    }
}
