namespace Galilego.Events
{
    /// <summary>
    /// Altitude band that limits the FULL (long) warp ladder. Owner's rule:
    ///   • below 15 km  — full warp is forbidden (the physical ladder ×1…×6 stays legal);
    ///   • 15…80 km     — full warp up to ×50;
    ///   • 80 km and up — the whole ladder ×10…×1 000 000.
    /// The band is measured ABOVE THE SURFACE of the body the craft is at (radius +
    /// terrain is not needed: the rule is about sea level / mean radius), and it does
    /// not apply to a STANDING craft — a parked vessel may warp at any factor at any
    /// altitude (see SimulationRunner.TryParkedWarpStep).
    ///
    /// Pure arithmetic, no Unity: the planner and the panel both read it, and the
    /// numbers can be tested without a scene.
    /// </summary>
    public enum WarpAltitudeBand
    {
        /// <summary>Below <see cref="WarpAltitudeGate.LowBandTopMeters"/>: no full warp at all.</summary>
        Low = 0,

        /// <summary>15…80 km: full warp up to <see cref="WarpAltitudeGate.MidBandMaxFullFactor"/>.</summary>
        Mid = 1,

        /// <summary>80 km and above: the full ladder.</summary>
        High = 2
    }

    public static class WarpAltitudeGate
    {
        /// <summary>Top of the "no full warp" band, m above the surface.</summary>
        public const double LowBandTopMeters = 15000d;

        /// <summary>Top of the "up to ×50" band, m above the surface.</summary>
        public const double MidBandTopMeters = 80000d;

        /// <summary>Highest full-warp factor allowed in the middle band.</summary>
        public const double MidBandMaxFullFactor = 50d;

        /// <summary>
        /// Band for an altitude above the surface, m. NaN/negative falls into the
        /// strictest band: an unknown altitude must never unlock warp.
        /// </summary>
        public static WarpAltitudeBand BandAt(double altitudeMeters)
        {
            // Written as a positive test so NaN lands in Low.
            if (!(altitudeMeters >= LowBandTopMeters))
            {
                return WarpAltitudeBand.Low;
            }

            return altitudeMeters < MidBandTopMeters ? WarpAltitudeBand.Mid : WarpAltitudeBand.High;
        }

        /// <summary>
        /// Highest FULL warp factor the band allows. 0 — full warp is forbidden
        /// (the physical ladder is still legal); +∞ — the whole ladder.
        /// </summary>
        public static double MaxFullFactor(WarpAltitudeBand band)
        {
            switch (band)
            {
                case WarpAltitudeBand.Low:
                    return 0d;
                case WarpAltitudeBand.Mid:
                    return MidBandMaxFullFactor;
                default:
                    return double.PositiveInfinity;
            }
        }

        /// <summary>Highest FULL warp factor at this altitude (see the band overload).</summary>
        public static double MaxFullFactor(double altitudeMeters)
        {
            return MaxFullFactor(BandAt(altitudeMeters));
        }

        /// <summary>Short player-facing reason for a clamp/reset, "" for the open band.</summary>
        public static string CapReason(WarpAltitudeBand band)
        {
            switch (band)
            {
                case WarpAltitudeBand.Low:
                    return "ниже 15 км";
                case WarpAltitudeBand.Mid:
                    return "ниже 80 км";
                default:
                    return string.Empty;
            }
        }
    }
}
