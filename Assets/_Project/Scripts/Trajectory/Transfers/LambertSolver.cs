using System;

namespace Galilego.Core
{
    /// <summary>
    /// Lambert problem result: velocities at the arc endpoints (inertial vectors).
    /// Single-rev only: multi-rev solutions are not built (throws).
    /// </summary>
    public readonly struct LambertSolution
    {
        public readonly Vector3d DepartureVelocity;
        public readonly Vector3d ArrivalVelocity;
        public readonly bool LongWay;

        public LambertSolution(Vector3d departureVelocity, Vector3d arrivalVelocity, bool longWay)
        {
            DepartureVelocity = departureVelocity;
            ArrivalVelocity = arrivalVelocity;
            LongWay = longWay;
        }
    }

    /// <summary>
    /// Lambert problem (r1 → r2 over dt) by the Bate–Mueller–White method with
    /// bisection on the universal variable z instead of Newton: tof(z) is monotone
    /// on the single-rev family, bisection is unkillable (200 iterations, 1e-13
    /// tolerance) and needs no derivatives. The cost is microseconds per porkchop cell.
    ///
        /// The degenerate 180° case (g = A√(y/μ) = 0, division by zero) is solved NOT by
        /// nudging the geometry but by an exact construction: at Δν=π the arc lies in
        /// the plane with normal n=normalize(r1×ref) (ref=+Z, fallback +Y), and the
        /// conic family is parameterized by eccentricity at constant p=2r1r2/(r1+r2) —
        /// see SolveHalfPlane (the BMWS machine loses the 2e·sinE1 correction here and is
        /// unusable). The neighborhood |sinΔν|&lt;1e-7 takes the same
        /// path (error O(angle) — acceptable for planning, zero for an exact 180°).
        /// Same point (cosΔν≈1) — ambiguity (any number of revolutions) → throws.
    /// </summary>
    public static class LambertSolver
    {
        private const double HalfPlaneSineTolerance = 1e-7d;

        private const double SamePointTolerance = 1e-12d;

        private const int MaxBisectionIterations = 200;

        private const double BisectionRelativeTolerance = 1e-13d;

        public static LambertSolution Solve(Vector3d relativePosition1, Vector3d relativePosition2, double dtSeconds, double mu)
        {
            return Solve(relativePosition1, relativePosition2, dtSeconds, mu, false);
        }

        public static LambertSolution Solve(Vector3d relativePosition1, Vector3d relativePosition2, double dtSeconds, double mu, bool longWay)
        {
            if (dtSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Время перелёта обязано быть положительным.");
            }

            if (mu <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(mu), "Гравитационный параметр обязан быть положительным.");
            }

            double r1 = relativePosition1.Magnitude;
            double r2 = relativePosition2.Magnitude;
            if (r1 <= 0d || r2 <= 0d)
            {
                throw new ArgumentException("Радиус-векторы обязаны быть ненулевыми.");
            }

            double cosDelta = Vector3d.Dot(relativePosition1, relativePosition2) / (r1 * r2);
            if (cosDelta > 1d)
            {
                cosDelta = 1d;
            }
            else if (cosDelta < -1d)
            {
                cosDelta = -1d;
            }

            if (cosDelta > 1d - SamePointTolerance)
            {
                throw new ArgumentException("Совпадающие точки: бесконечно много решений (витки).");
            }

            double sinDelta = Math.Sqrt(Math.Max(0d, 1d - (cosDelta * cosDelta)));
            if (longWay)
            {
                sinDelta = -sinDelta;
            }

            if (cosDelta < 0d && Math.Abs(sinDelta) < HalfPlaneSineTolerance)
            {
                return SolveHalfPlane(relativePosition1, relativePosition2, r1, r2, mu, longWay, dtSeconds);
            }

            double aParameter = sinDelta * Math.Sqrt((r1 * r2) / (1d - cosDelta));
            double z = BisectZ(r1, r2, aParameter, dtSeconds, mu);
            Stumpff.CS(z, out double c, out double s);
            double y = r1 + r2 + (aParameter * ((z * s) - 1d) / Math.Sqrt(c));
            double x = Math.Sqrt(y / c);
            double f = 1d - (y / r1);
            double g = aParameter * Math.Sqrt(y / mu);
            double gDot = 1d - (y / r2);
            Vector3d v1 = (relativePosition2 - (relativePosition1 * f)) / g;
            Vector3d v2 = ((relativePosition2 * gDot) - relativePosition1) / g;
            return new LambertSolution(v1, v2, longWay);
        }

        /// <summary>
        /// Degenerate 180° case (A = sinΔν·√(r1r2/(1−cosΔν)) → 0).
        /// The BMWS machine is NOT usable here: its tof term x³·s/√μ = a^1.5(ΔE−sinΔE)/√μ
        /// loses the 2e·sinE1 correction (it lived in A√y, which vanishes with A) and
        /// gives the arc time from periapsis rather than between the antipodes. The
        /// correct family parameterization: at Δν=π the focal parameter p = 2r1r2/(r1+r2)
        /// is CONSTANT over the whole conic family; only e ∈ [|κ|, 1) varies,
        /// κ = (r2−r1)/(r1+r2) = e·cosν1 (ν1 = acos(κ/e) ∈ [0,π] — the first arc end).
        /// tof(e) = a^1.5·(π + 2e·sinE1)/√μ is monotone in e: the minimum is the
        /// half-period of the min-energy ellipse (e=|κ|, ends at the apsides,
        /// a=(r1+r2)/2), ∞ as e→1. Hyperbola/parabola do not connect antipodal points
        /// from the focus — dt &lt; tof_min means NO solutions, fail loudly.
        /// Velocities are exact components: v_r = (μ/h)·e·sinν = (μ/h)·√(e²−κ²)
        /// (outward at ν1, inward at ν1+π), v_t = h/r, h = √(μp). Purely tangential
        /// ends ONLY at e=|κ|.
        /// longWay — the same conic traversed in reverse: v_long = −v_short exactly.
        /// The plane at Δν=π is not determined by two points — fix it with a reference
        /// vector (ref=+Z, fallback +Y).
        /// </summary>
        private static LambertSolution SolveHalfPlane(Vector3d r1Vec, Vector3d r2Vec, double r1, double r2, double mu, bool longWay, double dtSeconds)
        {
            const double eCap = 1d - 1e-9d; // analogous to the z=39 cap: e→1 gives tof→∞, but a numerical cap is needed

            double kappa = (r2 - r1) / (r1 + r2); // = e·cosν1, |κ| ≤ 1
            double absKappa = Math.Abs(kappa);
            double p = (2d * r1 * r2) / (r1 + r2); // focal parameter, constant of the family
            double aMin = 0.5d * (r1 + r2);
            double tofMin = Math.PI * Math.Sqrt((aMin * aMin * aMin) / mu);
            if (dtSeconds < tofMin * (1d - 1e-12d))
            {
                throw new ArgumentOutOfRangeException(nameof(dtSeconds),
                    "Для дуги 180° минимальное время перелёта — полупериод min-energy эллипса (" +
                    tofMin.ToString("F0") + " с); запрошено " + dtSeconds.ToString("F0") + " с — решений нет.");
            }

            double FamilyTimeOfFlight(double e)
            {
                // cosE = (e + cosν)/(1 + e·cosν). IMPORTANT: antipodality in ν does NOT
                // carry over to E2 = E1 + π (that holds only for e = 0) — E1 and E2
                // for ν1 and ν1+π are computed INDEPENDENTLY, otherwise tof is
                // understated and the round-trip misses r2.
                double cosNu1 = kappa / e; // ∈ (−1, 1) for e ∈ (|κ|, 1)
                // cosE = (e + cosν)/(1 + e·cosν); in the family e·cosν1 = κ exactly,
                // so the denominators are constants (1±κ).
                double cosE1 = (e + cosNu1) / (1d + kappa);
                double cosE2 = (e - cosNu1) / (1d - kappa); // cosν2 = −cosν1
                double e1 = Math.Acos(Math.Max(-1d, Math.Min(1d, cosE1))); // ν1 ∈ [0, π] ⇒ E1 ∈ [0, π]
                double e2 = (2d * Math.PI) - Math.Acos(Math.Max(-1d, Math.Min(1d, cosE2))); // ν2 ∈ [π, 2π] ⇒ E2 ∈ [π, 2π]
                double sinE1 = Math.Sin(e1);
                double sinE2 = -Math.Sqrt(Math.Max(0d, 1d - (cosE2 * cosE2))); // E2 ∈ [π, 2π]
                double deltaM = (e2 - e1) + (e * (sinE1 - sinE2));
                double a = p / (1d - (e * e));
                return (a * Math.Sqrt(a) * deltaM) / Math.Sqrt(mu);
            }

            double tofCap = FamilyTimeOfFlight(eCap);
            if (dtSeconds > tofCap)
            {
                throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Время перелёта слишком велико для single-rev семейства этой геометрии.");
            }

            // dt ≈ tofMin is exactly min-energy: ends at the apsides, e = |κ| exactly,
            // radial component strictly zero. Snap to it so bisection does not produce
            // e = |κ|+δ, where √(e²−κ²) turns δ~1e-12 into a finite radial speed.
            if (dtSeconds <= tofMin * (1d + 1e-12d))
            {
                return HalfPlaneVelocity(r1Vec, r2Vec, r1, r2, mu, longWay, absKappa, p, kappa);
            }

            // Bisection over e ∈ [|κ|, eCap]: tof(e) is strictly monotone.
            double lo = absKappa;
            double hi = eCap;
            for (int i = 0; i < MaxBisectionIterations; i++)
            {
                double mid = 0.5d * (lo + hi);
                if (mid <= lo || mid >= hi)
                {
                    break; // double precision limit
                }

                double err = (FamilyTimeOfFlight(mid) - dtSeconds) / dtSeconds;
                if (Math.Abs(err) < BisectionRelativeTolerance)
                {
                    break;
                }

                if (err < 0d)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return HalfPlaneVelocity(r1Vec, r2Vec, r1, r2, mu, longWay, 0.5d * (lo + hi), p, kappa);
        }

        /// <summary>Velocities at the ends of the 180° arc for a given eccentricity.</summary>
        private static LambertSolution HalfPlaneVelocity(
            Vector3d r1Vec, Vector3d r2Vec, double r1, double r2, double mu, bool longWay, double ecc, double p, double kappa)
        {
            double semiMajor = p / (1d - (ecc * ecc));
            double h = Math.Sqrt(mu * p);
            double radialSpeed = (mu / h) * Math.Sqrt(Math.Max(0d, (ecc * ecc) - (kappa * kappa))); // (μ/h)·e·sinν1, ≥0
            double tangentialSpeed1 = h / r1;
            double tangentialSpeed2 = h / r2;

            Vector3d reference = Math.Abs(r1Vec.Z / r1) < 0.9d
                ? new Vector3d(0d, 0d, 1d)
                : new Vector3d(0d, 1d, 0d);
            Vector3d normal = Vector3d.Cross(r1Vec, reference).Normalized;
            Vector3d rHat1 = r1Vec / r1;
            Vector3d rHat2 = r2Vec / r2; // ≈ −rHat1 by the branch geometry
            Vector3d tangent1 = Vector3d.Cross(normal, rHat1); // direction of increasing ν (prograde around n)
            Vector3d tangent2 = Vector3d.Cross(normal, rHat2);

            // Short arc: ν grows from ν1 to ν1+π ⇒ v_r(ν1) > 0, v_r(ν1+π) < 0.
            Vector3d v1 = (rHat1 * radialSpeed) + (tangent1 * tangentialSpeed1);
            Vector3d v2 = (rHat2 * (-radialSpeed)) + (tangent2 * tangentialSpeed2);
            if (longWay)
            {
                v1 = -v1;
                v2 = -v2;
            }

            return new LambertSolution(v1, v2, longWay);
        }

        private static double BisectZ(double r1, double r2, double aParameter, double dtSeconds, double mu)
        {
            double tofZero = TimeOfFlight(0d, r1, r2, aParameter, mu);
            double lo;
            double hi;
            if (dtSeconds > tofZero)
            {
                lo = 0d;
                hi = 1d;
                while (TimeOfFlight(hi, r1, r2, aParameter, mu) < dtSeconds)
                {
                    hi *= 2d;
                    if (hi > 39d)
                    {
                        hi = 39d;
                        // The cap z=39 is the boundary of the single-rev family (c→0 as z→4π²).
                        // If even at the cap tof is less than requested there is no solution:
                        // fail loudly rather than silently sliding to the cap with a wrong
                        // answer. Porkchop catches ArgumentException → invalid.
                        if (TimeOfFlight(hi, r1, r2, aParameter, mu) < dtSeconds)
                        {
                            throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Время перелёта слишком велико для single-rev семейства этой геометрии.");
                        }

                        break;
                    }
                }
            }
            else if (dtSeconds < tofZero)
            {
                hi = 0d;
                lo = -1d;
                while (TimeOfFlight(lo, r1, r2, aParameter, mu) > dtSeconds)
                {
                    lo *= 2d;
                    if (lo < -1e4d)
                    {
                        throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Время перелёта слишком мало для этой геометрии.");
                    }
                }
            }
            else
            {
                return 0d;
            }

            double z = 0.5d * (lo + hi);
            for (int i = 0; i < MaxBisectionIterations; i++)
            {
                z = 0.5d * (lo + hi);
                double err = (TimeOfFlight(z, r1, r2, aParameter, mu) - dtSeconds) / dtSeconds;
                if (Math.Abs(err) < BisectionRelativeTolerance)
                {
                    break;
                }

                if (err > 0d)
                {
                    hi = z;
                }
                else
                {
                    lo = z;
                }
            }

            return z;
        }

        private static double TimeOfFlight(double z, double r1, double r2, double aParameter, double mu)
        {
            Stumpff.CS(z, out double c, out double s);
            if (c <= 0d)
            {
                return double.PositiveInfinity;
            }

            double y = r1 + r2 + (aParameter * ((z * s) - 1d) / Math.Sqrt(c));
            if (y <= 0d)
            {
                return double.PositiveInfinity;
            }

            double x = Math.Sqrt(y / c);
            return ((x * x * x * s) + (aParameter * Math.Sqrt(y))) / Math.Sqrt(mu);
        }
    }

    /// <summary>
    /// Stumpff functions C(z), S(z): trigonometric (z&gt;0), hyperbolic (z&lt;0),
    /// series (|z|≤1e-6). Internal, not exposed outside.
    /// </summary>
    internal static class Stumpff
    {
        public static void CS(double z, out double c, out double s)
        {
            if (z > 1e-6d)
            {
                double root = Math.Sqrt(z);
                c = (1d - Math.Cos(root)) / z;
                s = (root - Math.Sin(root)) / (root * z);
            }
            else if (z < -1e-6d)
            {
                double root = Math.Sqrt(-z);
                c = (Math.Cosh(root) - 1d) / -z;
                s = (Math.Sinh(root) - root) / (root * -z);
            }
            else
            {
                double z2 = z * z;
                c = 0.5d - (z / 24d) + (z2 / 720d) - ((z2 * z) / 40320d);
                s = (1d / 6d) - (z / 120d) + (z2 / 5040d) - ((z2 * z) / 362880d);
            }
        }
    }
}
