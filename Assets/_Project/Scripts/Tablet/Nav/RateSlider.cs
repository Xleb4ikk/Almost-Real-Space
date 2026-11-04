using System;

namespace Galilego.Tablet
{
    /// <summary>
    /// "Rate" slider: the thumb rests in the centre; dragging left/right changes a value at a speed
    /// proportional to the deflection (squared, for fine control near the centre); releasing returns the thumb.
    /// Pure logic: the screen feeds pointer position (0..1 across the track) and calls Tick every frame.
    /// </summary>
    public sealed class RateSlider
    {
        /// <summary>Thumb deflection −1 (left) … +1 (right); 0 is the centre.</summary>
        public float Deviation { get; private set; }

        public bool Dragging { get; private set; }

        /// <summary>Thumb position 0..1 across the track (for drawing).</summary>
        public float ThumbPos01 { get { return 0.5f + Deviation * 0.5f; } }

        /// <summary>Signed rate multiplier −1…+1 (deviation × |deviation|).</summary>
        public double Curve { get { return Deviation * Math.Abs(Deviation); } }

        public void Press(float pos01)
        {
            Dragging = true;
            Drag(pos01);
        }

        public void Drag(float pos01)
        {
            if (!Dragging) { return; }
            Deviation = Math.Max(-1f, Math.Min(1f, (pos01 - 0.5f) * 2f));
        }

        public void Release()
        {
            Dragging = false;
        }

        /// <summary>Call once per frame. Returns the curve to apply this frame (0 unless dragging) and relaxes the thumb to centre.</summary>
        public double Tick(float dt)
        {
            if (Dragging) { return Curve; }
            if (Deviation != 0f)
            {
                Deviation *= (float)Math.Pow(0.7, Math.Max(0f, dt) * 60d);
                if (Math.Abs(Deviation) < 0.02f) { Deviation = 0f; }
            }

            return 0d;
        }

        /// <summary>value += curve × maxRate × dt, clamped. For ΔV (m/s per s) and start times (s per s).</summary>
        public static double AdvanceLinear(double value, double curve, double maxRatePerS, double dt, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value + curve * maxRatePerS * dt));
        }

        /// <summary>
        /// Multiplicative change for lengths spanning minutes…decades: value *= exp(curve × maxFactor × dt).
        /// With allowOff the value may drop below min to exactly 0 ("off") and rise from 0 back to min.
        /// </summary>
        public static double AdvanceLog(double value, double curve, double maxFactorPerS, double dt, double min, double max, bool allowOff)
        {
            if (value <= 0d)
            {
                return allowOff ? (curve > 0d ? min : 0d) : min;
            }

            double v = Math.Min(max, value * Math.Exp(curve * maxFactorPerS * dt));
            if (v < min)
            {
                return allowOff && curve < 0d ? 0d : min;
            }

            return v;
        }
    }
}
