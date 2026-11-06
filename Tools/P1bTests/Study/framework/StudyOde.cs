using System;
using System.Collections.Generic;
using System.Globalization;

namespace P1bStudy.Framework
{
    /// <summary>First-order right-hand side: dy = f(t, y). Must not allocate state that survives the call.</summary>
    internal delegate void OdeRhs(double t, double[] y, double[] dy);

    /// <summary>Thrown by StudyContext.Rhs when a run exceeds its RHS budget. The sweep records the row as censored.</summary>
    internal sealed class StudyBudgetExceeded : Exception
    {
        public StudyBudgetExceeded(long calls)
            : base("RHS budget exceeded after " + calls.ToString(CultureInfo.InvariantCulture) + " calls")
        {
        }
    }

    /// <summary>
    /// One benchmark problem, independent of the game engine. The bridge file
    /// (StudyBridge.cs) is the only place that knows how to build it from a
    /// Test549 case or a planet fixture.
    ///
    /// State layout for second-order problems: y = [x(0..K-1), v(K..2K-1), extras...]
    /// with K = PosDim. The ship uses K = 3 and one extra (mass); the planet table
    /// uses K = 3n and no extras.
    /// </summary>
    internal sealed class StudyProblem
    {
        public string Label = "";
        public int Dim;
        public double T0;
        public double[] Y0;
        public OdeRhs Rhs;

        /// <summary>Strictly increasing times, all &gt; T0. The last one is the end time.</summary>
        public double[] CheckpointTimes;

        /// <summary>Reference state at each checkpoint (length Dim, or at least what ErrorFn reads).</summary>
        public double[][] CheckpointRef;

        /// <summary>Optional force discontinuities (burn start/end, ramp corners). Integrators land exactly on them.</summary>
        public double[] Breakpoints;

        /// <summary>K for the [x, v, extras] layout; 0 when the problem has no second-order structure.</summary>
        public int PosDim;

        /// <summary>
        /// True only if the acceleration depends on (t, x) alone and the extras are constant.
        /// Symplectic / Kepler-split integrators refuse every other problem.
        /// </summary>
        public bool ConservativeSecondOrder;

        /// <summary>&gt; 0: force = Kepler(CentralMu, centre at the origin of x[0..2]) + perturbation. Needs PosDim == 3.</summary>
        public double CentralMu;

        /// <summary>Error-control blocks {start, length}. Null = derived from PosDim (one block per 3-vector).</summary>
        public int[][] ErrorBlocks;

        /// <summary>Optional per-block absolute tolerances for adaptive methods (same length as the blocks). Null = 0.</summary>
        public double[] AdaptiveAtol;

        /// <summary>
        /// Optional per-block magnitude used by the adaptive controller instead of the
        /// state magnitude (same length as GetBlocks(), 0 = state magnitude). Needed when
        /// the state carries a large common offset that is irrelevant to the metric: the
        /// planet sweep stores barycentric positions (~1e11..1e12 m) but scores relative
        /// pair separations, so controlling a 1e-6 relative to 1e11 would allow a lunar
        /// orbit to lose 1e5 m per step. With this, a body's position block is scaled by
        /// its distance to the nearest other body and a velocity block by the circular
        /// speed of that pair.
        /// </summary>
        public double[] AdaptiveScales;

        /// <summary>
        /// Optional physical-sanity check on an accepted state. Returns null when the state
        /// is acceptable, or a short reason when it is not (a pair distance collapsed, a
        /// body left the system, ...). The run then stops with that reason instead of
        /// grinding the step size down at a singularity; the report distinguishes such a
        /// divergence from a controller failure.
        /// </summary>
        public Func<double[], string> DomainViolation;

        /// <summary>Metric (state, reference) -> error in problem units. Null = norm of the first 3 components.</summary>
        public Func<double[], double[], double> ErrorFn;

        /// <summary>Reference's own uncertainty in the same unit as the error. NaN = reference not certified.</summary>
        public double Floor = double.NaN;

        /// <summary>Why the reference is (or is not) certified; shown next to the floor.</summary>
        public string ReferenceNote = "";

        public bool Certified;

        /// <summary>Time scale (orbital period etc.) used to seed the coarse end of fixed-step sweeps.</summary>
        public double CharTime;

        /// <summary>
        /// Largest control value the sweep may try; 0 = no limit. Engine bridges set it when
        /// the force is only defined on a finite time window (baked ephemerides): a step above
        /// this can evaluate the force outside that window.
        /// </summary>
        public double MaxControl;

        /// <summary>Length scale used to turn relative targets into absolute ones.</summary>
        public double ScaleLength = 1d;

        public double EndTime
        {
            get { return CheckpointTimes[CheckpointTimes.Length - 1]; }
        }

        public double Duration
        {
            get { return EndTime - T0; }
        }

        public int[][] GetBlocks()
        {
            if (ErrorBlocks != null)
            {
                return ErrorBlocks;
            }

            var list = new List<int[]>();
            if (PosDim >= 3 && PosDim % 3 == 0 && 2 * PosDim <= Dim)
            {
                for (int i = 0; i < PosDim; i += 3)
                {
                    list.Add(new[] { i, 3 });
                }

                for (int i = 0; i < PosDim; i += 3)
                {
                    list.Add(new[] { PosDim + i, 3 });
                }

                for (int i = 2 * PosDim; i < Dim; i++)
                {
                    list.Add(new[] { i, 1 });
                }
            }
            else
            {
                list.Add(new[] { 0, Dim });
            }

            ErrorBlocks = list.ToArray();
            return ErrorBlocks;
        }

        public double Error(double[] state, double[] reference)
        {
            if (ErrorFn != null)
            {
                return ErrorFn(state, reference);
            }

            int n = Math.Min(3, Dim);
            double s = 0d;
            for (int i = 0; i < n; i++)
            {
                double d = state[i] - reference[i];
                s += d * d;
            }

            return Math.Sqrt(s);
        }

        public bool Validate(out string why)
        {
            if (Rhs == null || Y0 == null || Y0.Length != Dim || Dim <= 0)
            {
                why = "Rhs/Y0/Dim inconsistent";
                return false;
            }

            if (CheckpointTimes == null || CheckpointTimes.Length == 0
                || CheckpointRef == null || CheckpointRef.Length != CheckpointTimes.Length)
            {
                why = "checkpoints missing or reference count differs";
                return false;
            }

            double prev = T0;
            for (int i = 0; i < CheckpointTimes.Length; i++)
            {
                if (!(CheckpointTimes[i] > prev) || CheckpointRef[i] == null)
                {
                    why = "checkpoint " + i.ToString(CultureInfo.InvariantCulture)
                        + " not increasing or has no reference";
                    return false;
                }

                prev = CheckpointTimes[i];
            }

            if (PosDim != 0 && 2 * PosDim > Dim)
            {
                why = "PosDim too large for Dim";
                return false;
            }

            if (CentralMu > 0d && PosDim != 3)
            {
                why = "CentralMu needs PosDim == 3";
                return false;
            }

            why = "";
            return true;
        }
    }

    /// <summary>Parameters of one run. Which fields matter depends on the integrator kind.</summary>
    internal sealed class StudyConfig
    {
        public string Label = "";
        public double Control;                       // the swept value: h (fixed) or rtol (adaptive)
        public double H;                             // fixed step
        public double Rtol;
        public double Atol;
        public double MaxStep = double.PositiveInfinity;
        public double H0;                            // adaptive initial step, 0 = automatic
    }

    /// <summary>What an integrator returns. Calls/AuxOps/time are measured by the harness, not reported by the integrator.</summary>
    internal sealed class StudyRun
    {
        public double[] Final;
        public double[][] Samples;                   // one state per checkpoint
        public long Steps;
        public long Rejected;
        public long CapLimited;                      // accepted steps where the controller wanted more than MaxStep

        /// <summary>True when the run stopped before the last checkpoint (step floor or domain violation).</summary>
        public bool Aborted;

        public double AbortTime = double.NaN;

        /// <summary>Human-readable reason for Aborted; empty on a complete run.</summary>
        public string Abort = "";
    }

    /// <summary>
    /// The only way an integrator may evaluate the force. Counts every call and
    /// enforces the budget, so no integrator can under-report its cost.
    /// </summary>
    internal sealed class StudyContext
    {
        private readonly OdeRhs rhs;
        private readonly long budget;

        public long Calls;
        public long AuxOps;

        /// <summary>Set by a stepper that cannot continue (e.g. the universal Kepler solver failed); the driver turns it into an aborted run.</summary>
        public string Abort;

        public StudyContext(StudyProblem p, long budget)
        {
            rhs = p.Rhs;
            this.budget = budget;
        }

        public void Fail(string reason)
        {
            if (Abort == null)
            {
                Abort = reason;
            }
        }

        public void Rhs(double t, double[] y, double[] dy)
        {
            Calls++;
            if (Calls > budget)
            {
                throw new StudyBudgetExceeded(Calls);
            }

            rhs(t, y, dy);
        }

        /// <summary>Counts non-RHS work (e.g. one Kepler solve). It is priced by StudyCalibration, not by the integrator.</summary>
        public void CountAux(long n)
        {
            AuxOps += n;
        }
    }

    /// <summary>Checkpoints merged with force breakpoints. Index &gt;= 0 marks a recorded checkpoint, -1 a landing-only breakpoint.</summary>
    internal sealed class StudyStops
    {
        public double[] Times;
        public int[] CheckpointIndex;

        public static StudyStops Build(StudyProblem p)
        {
            var items = new List<KeyValuePair<double, int>>();
            for (int k = 0; k < p.CheckpointTimes.Length; k++)
            {
                items.Add(new KeyValuePair<double, int>(p.CheckpointTimes[k], k));
            }

            if (p.Breakpoints != null)
            {
                foreach (double b in p.Breakpoints)
                {
                    if (!(b > p.T0) || !(b < p.EndTime))
                    {
                        continue;
                    }

                    items.Add(new KeyValuePair<double, int>(b, -1));
                }
            }

            items.Sort(delegate (KeyValuePair<double, int> a, KeyValuePair<double, int> b)
            {
                int c = a.Key.CompareTo(b.Key);
                if (c != 0)
                {
                    return c;
                }

                // checkpoint first, so a coincident breakpoint is dropped below
                return b.Value.CompareTo(a.Value);
            });

            var times = new List<double>();
            var index = new List<int>();
            foreach (KeyValuePair<double, int> item in items)
            {
                if (times.Count > 0)
                {
                    double tol = 1e-9 * Math.Max(1d, Math.Abs(item.Key));
                    if (Math.Abs(item.Key - times[times.Count - 1]) <= tol)
                    {
                        // duplicate time: keep the checkpoint flag if either has one
                        if (item.Value >= 0 && index[index.Count - 1] < 0)
                        {
                            index[index.Count - 1] = item.Value;
                        }

                        continue;
                    }
                }

                times.Add(item.Key);
                index.Add(item.Value);
            }

            var stops = new StudyStops();
            stops.Times = times.ToArray();
            stops.CheckpointIndex = index.ToArray();
            return stops;
        }
    }
}
