namespace Galilego.Core
{
    /// <summary>
    /// Compensated (Kahan–Babuška) summation for long-lived time accumulators:
    /// EventDrivenPropagator (thousands of chunks) and the ManeuverEvaluator burn
    /// loop. Over years of warp a naive t += chunk accumulates error comparable to
    /// the 1e-6 s root tolerance; compensation keeps it far smaller. The exact error
    /// depends on the number sequence and IEEE-754 and is NOT guaranteed here —
    /// only the much slower error growth (measured by T21a). Plain struct, zero
    /// allocations; no implicit conversions on purpose, so compensated and raw
    /// double accumulation stay visibly distinct.
    /// </summary>
    public struct KahanAccumulator
    {
        private double sum;
        private double compensation;

        public double Sum => sum;

        public KahanAccumulator(double initialValue)
        {
            sum = initialValue;
            compensation = 0d;
        }

        public void Add(double value)
        {
            double y = value - compensation;
            double t = sum + y;
            compensation = (t - sum) - y;
            sum = t;
        }

        public void Reset(double value)
        {
            sum = value;
            compensation = 0d;
        }
    }
}
