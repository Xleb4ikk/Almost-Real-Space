using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Analytical two-body prediction: state (r0, v0) → (+dt) with no integrator step.
    /// Ellipse via the existing FromState/SolveEccentricAnomaly (the same math as the
    /// OrbitingBody rails, reused rather than re-copied); hyperbola via asinh-Newton.
    /// Parabola (|e−1|≈0) throws rather than silently. Used for transfer preview lines,
    /// sampling porkchop arcs for through-body checks (TryEvaluate), and Lambert
    /// arrival validation.
    /// </summary>
    public static class KeplerPredictor
    {
        public static void Advance(Vector3d relativePosition, Vector3d relativeVelocity, double mu, double dtSeconds, out Vector3d position, out Vector3d velocity)
        {
            if (mu <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(mu), "Гравитационный параметр обязан быть положительным.");
            }

            // The predictor is analytical, not an integrator: negative dt is valid
            // (the flyby bridge runs backwards between SOI). Only NaN is rejected.
            if (!double.IsFinite(dtSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(dtSeconds), "dt обязан быть конечным.");
            }

            OrbitalElements elements = OrbitalElements.FromState(relativePosition, relativeVelocity, mu);
            if (!elements.IsValid)
            {
                throw new ArgumentException("Состояние вырождено: элементы не определены.");
            }

            double eccentricity = elements.Eccentricity;
            if (Math.Abs(eccentricity - 1d) < 1e-9d)
            {
                throw new NotSupportedException("Околопараболические дуги не поддерживаются.");
            }

            double ascendingNode = KeplerMath.DegreesToRadians(elements.LongitudeOfAscendingNodeDegrees);
            double inclination = KeplerMath.DegreesToRadians(elements.InclinationDegrees);
            double periapsis = KeplerMath.DegreesToRadians(elements.ArgumentOfPeriapsisDegrees);

            Vector3d orbitalPosition;
            Vector3d orbitalVelocity;
            if (eccentricity < 1d)
            {
                double semiMajor = elements.SemiMajorAxis;
                // Energy ≈ 0 (parabola by FromState's eps classification) gives a=+Inf
                // for e<1: sqrt(mu/Inf³)=0 — time freezes and radius goes Inf/NaN
                // silently. Fail loudly with the same semantics as |e−1|.
                if (!double.IsFinite(semiMajor) || !(semiMajor > 0d))
                {
                    throw new NotSupportedException("Околопараболические дуги не поддерживаются (a=" + semiMajor + ").");
                }

                double meanMotion = Math.Sqrt(mu / (semiMajor * semiMajor * semiMajor));
                // Reduce dt by the period BEFORE multiplying: for dt in years M ~ 1e10 rad,
                // ulp(1e10) ≈ 2e-6 rad — phase is silently lost even before NormalizeAngle.
                double period = (2d * Math.PI) / meanMotion;
                double dtReduced = dtSeconds - (Math.Floor(dtSeconds / period) * period);
                double meanAnomaly = KeplerMath.NormalizeAngle(
                    KeplerMath.DegreesToRadians(elements.MeanAnomalyDegrees) + (meanMotion * dtReduced));
                double eccentricAnomaly = KeplerMath.SolveEccentricAnomaly(meanAnomaly, eccentricity);
                double cosE = Math.Cos(eccentricAnomaly);
                double sinE = Math.Sin(eccentricAnomaly);
                double radius = semiMajor * (1d - (eccentricity * cosE));
                double yScale = Math.Sqrt(1d - (eccentricity * eccentricity));
                orbitalPosition = new Vector3d(
                    semiMajor * (cosE - eccentricity),
                    semiMajor * yScale * sinE,
                    0d);
                double velocityFactor = Math.Sqrt(mu * semiMajor) / radius;
                orbitalVelocity = new Vector3d(
                    -velocityFactor * sinE,
                    velocityFactor * yScale * cosE,
                    0d);
            }
            else
            {
                double alpha = -elements.SemiMajorAxis;
                double meanMotion = Math.Sqrt(mu / (alpha * alpha * alpha));
                double trueAnomaly = KeplerMath.DegreesToRadians(elements.TrueAnomalyDegrees);
                double eFactor = Math.Sqrt((eccentricity - 1d) / (eccentricity + 1d));
                double h0 = 2d * AtanhBounded(eFactor * Math.Tan(0.5d * trueAnomaly));
                double hyperbolicAnomaly = SolveHyperbolicAnomaly(
                    eccentricity * Math.Sinh(h0) - h0 + (meanMotion * dtSeconds), eccentricity);
                double cosH = Math.Cosh(hyperbolicAnomaly);
                double sinH = Math.Sinh(hyperbolicAnomaly);
                double radius = alpha * ((eccentricity * cosH) - 1d);
                double yScale = Math.Sqrt((eccentricity * eccentricity) - 1d);
                orbitalPosition = new Vector3d(
                    alpha * (eccentricity - cosH),
                    alpha * yScale * sinH,
                    0d);
                double velocityFactor = Math.Sqrt(mu * alpha) / radius;
                orbitalVelocity = new Vector3d(
                    -velocityFactor * sinH,
                    velocityFactor * yScale * cosH,
                    0d);
            }

            position = KeplerMath.RotateOrbitalToWorld(orbitalPosition, ascendingNode, inclination, periapsis);
            velocity = KeplerMath.RotateOrbitalToWorld(orbitalVelocity, ascendingNode, inclination, periapsis);
        }

        /// <summary>
        /// Internal for direct unit testing of the residual (the test project compiles
        /// the game sources into the same assembly). Newton with 20 iterations plus a
        /// residual check with a bisection fallback: F grows strictly for e&gt;1
        /// (F'=e·cosh−1 ≥ e−1 &gt; 0); the bracket is expanded from the Newton estimate with
        /// a cap of ±700 (beyond that the sign of F is set by the asymptotes: +∞ right,
        /// −∞ left). For e≤1 only Newton runs. The residual check is inverted (!≤ tol):
        /// an unbracketed bisection interval is a loud failure, not a silent wrong root.
        /// </summary>
        internal static double SolveHyperbolicAnomaly(double meanAnomaly, double eccentricity)
        {
            if (!double.IsFinite(meanAnomaly) || !double.IsFinite(eccentricity))
            {
                throw new ArgumentOutOfRangeException(nameof(meanAnomaly), "M и e обязаны быть конечными.");
            }

            double estimate = Math.Log((2d * Math.Abs(meanAnomaly) / eccentricity) + 1.8d);
            if (meanAnomaly < 0d)
            {
                estimate = -estimate;
            }

            for (int i = 0; i < 20; i++)
            {
                double function = (eccentricity * Math.Sinh(estimate)) - estimate - meanAnomaly;
                double derivative = (eccentricity * Math.Cosh(estimate)) - 1d;
                estimate -= function / derivative;
                if (!double.IsFinite(estimate))
                {
                    // Newton diverged (e≈1: derivative ≈ 1e-9) — clean start for bisection.
                    estimate = meanAnomaly >= 0d ? 0d : -0d;
                    break;
                }
            }

            if (eccentricity > 1d)
            {
                double tol = 1e-9d * Math.Max(1d, Math.Abs(meanAnomaly));
                double residual = (eccentricity * Math.Sinh(estimate)) - estimate - meanAnomaly;
                if (!(Math.Abs(residual) <= tol))
                {
                    estimate = BisectHyperbolic(meanAnomaly, eccentricity, estimate);
                }
            }

            return estimate;
        }

        private static double HyperbolicFunction(double anomaly, double meanAnomaly, double eccentricity)
        {
            return (eccentricity * Math.Sinh(anomaly)) - anomaly - meanAnomaly;
        }

        private static double EvalCapped(double anomaly, double meanAnomaly, double eccentricity, double cap)
        {
            if (anomaly <= -cap)
            {
                return double.NegativeInfinity;
            }

            if (anomaly >= cap)
            {
                return double.PositiveInfinity;
            }

            return HyperbolicFunction(anomaly, meanAnomaly, eccentricity);
        }

        private static double BisectHyperbolic(double meanAnomaly, double eccentricity, double estimate)
        {
            const double cap = 700d;
            if (!double.IsFinite(estimate))
            {
                estimate = 0d;
            }

            double lo = estimate - 1d;
            double hi = estimate + 1d;
            double fLo = EvalCapped(lo, meanAnomaly, eccentricity, cap);
            double fHi = EvalCapped(hi, meanAnomaly, eccentricity, cap);
            for (int i = 0; i < 60 && !(fLo < 0d && fHi > 0d); i++)
            {
                double width = Math.Max(hi - lo, 1d);
                if (fLo >= 0d)
                {
                    hi = lo;
                    fHi = fLo;
                    lo = Math.Max(-cap, lo - width);
                }
                else
                {
                    lo = hi;
                    fLo = fHi;
                    hi = Math.Min(cap, hi + width);
                }

                fLo = EvalCapped(lo, meanAnomaly, eccentricity, cap);
                fHi = EvalCapped(hi, meanAnomaly, eccentricity, cap);
            }

            if (!(fLo < 0d && fHi > 0d))
            {
                // Unbracketed interval: bisecting it would give a silently wrong root.
                // Fail loudly.
                throw new InvalidOperationException(
                    "Гиперболическая аномалия: не найден брекет знака для M=" + meanAnomaly.ToString("R") +
                    ", e=" + eccentricity.ToString("R") + ".");
            }

            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5d * (lo + hi);
                if (HyperbolicFunction(mid, meanAnomaly, eccentricity) > 0d)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            return 0.5d * (lo + hi);
        }

        private static double AtanhBounded(double x)
        {
            if (x >= 1d)
            {
                return 20d;
            }

            if (x <= -1d)
            {
                return -20d;
            }

            return 0.5d * Math.Log((1d + x) / (1d - x));
        }
    }
}
