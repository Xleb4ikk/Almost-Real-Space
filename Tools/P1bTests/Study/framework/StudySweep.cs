using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace P1bStudy.Framework
{
    internal sealed class StudyTarget
    {
        public string Label = "";
        public double Value;
    }

    internal sealed class StudyOptions
    {
        /// <summary>Targets relative to StudyProblem.ScaleLength (LEO and 1 AU are then comparable).</summary>
        public double[] RelTargets = new[] { 1e-6d, 1e-8d, 1e-10d };

        /// <summary>Targets in the problem's error unit (metres).</summary>
        public double[] AbsTargets = new double[0];

        /// <summary>RHS budget of ONE run. A run that exceeds it is recorded as censored, never silently shortened.</summary>
        public long MaxCostRhs = 2000000L;

        /// <summary>Cost of one auxiliary operation (Kepler solve) in RHS calls. NaN = measure it per scenario.</summary>
        public double KeplerWeight = double.NaN;

        /// <summary>
        /// Price of one RHS call of the real game path relative to the study RHS (e.g. 3 for the
        /// production-local path that sums three gravity fields). Applied to calls, not to the Kepler solve.
        /// </summary>
        public double RhsCostMultiplier = 1d;

        /// <summary>
        /// Adaptive methods are run once per cap. Include +Infinity (the method's own choice) AND the
        /// production cap (600 s ship / 86400 s planets): a cap that binds makes cost independent of rtol.
        /// </summary>
        public double[] AdaptiveMaxSteps = new[] { double.PositiveInfinity };

        /// <summary>A target below FloorMargin * reference floor is not ranked (it would measure reference noise).</summary>
        public double FloorMargin = 3d;

        public int MaxPoints = 40;

        /// <summary>The coarse end is loosened until its error exceeds LoosenFactor * the loosest target, so no winner sits on the grid edge.</summary>
        public double LoosenFactor = 10d;

        public int TimingReps = 3;
        public double TimingRepThresholdMs = 250d;

        public List<StudyTarget> Targets(StudyProblem p)
        {
            var list = new List<StudyTarget>();
            foreach (double rel in RelTargets)
            {
                var t = new StudyTarget();
                t.Value = rel * p.ScaleLength;
                t.Label = rel.ToString("0.0E+0", CultureInfo.InvariantCulture) + " rel (" + t.Value.ToString("G3", CultureInfo.InvariantCulture) + " m)";
                list.Add(t);
            }

            foreach (double abs in AbsTargets)
            {
                var t = new StudyTarget();
                t.Value = abs;
                t.Label = abs.ToString("G3", CultureInfo.InvariantCulture) + " m";
                list.Add(t);
            }

            return list;
        }
    }

    /// <summary>One run of one integrator at one control value.</summary>
    internal sealed class StudyRow
    {
        public string Scenario = "";
        public string Integrator = "";
        public string Variant = "";
        public string Config = "";
        public StudyKind Kind;
        public double ExpectedOrder = double.NaN;
        public double Control;
        public string Status = "ok";          // ok | censored | error | unsupported
        public string Note = "";
        public bool Finite;
        public double Err = double.NaN;       // max over checkpoints
        public double ErrFinal = double.NaN;
        public long Calls;
        public long Aux;
        public long Steps;
        public long Rejected;
        public long CapLimited;
        public double Cost;                   // set by StudyReport.FinalizeCosts
        public double Ms = double.NaN;

        public string Key
        {
            get { return Variant.Length == 0 ? Integrator : Integrator + " [" + Variant + "]"; }
        }
    }

    internal sealed class StudyScenarioResult
    {
        public StudyProblem Problem;
        public List<StudyRow> Rows = new List<StudyRow>();
        public double KeplerWeight;
        public double RhsMultiplier = 1d;
    }

    internal static class StudyCalibration
    {
        public static bool IsDebugBuild
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Measures how many RHS calls one Kepler solve is worth on this problem (wall clock, so use a Release build).
        /// Returns 0 when the problem has no central body, 1 if the measurement itself fails.
        /// </summary>
        public static double MeasureKeplerWeight(StudyProblem p)
        {
            if (!(p.CentralMu > 0d) || p.PosDim != 3)
            {
                return 0d;
            }

            try
            {
                const int n = 20000;
                double[] y = (double[])p.Y0.Clone();
                double[] dy = new double[p.Dim];
                for (int i = 0; i < 2000; i++)
                {
                    p.Rhs(p.T0, y, dy);
                }

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < n; i++)
                {
                    p.Rhs(p.T0, y, dy);
                }

                sw.Stop();
                double tRhs = sw.Elapsed.TotalMilliseconds;
                double dt = 1e-3d * (p.CharTime > 0d ? p.CharTime : 1d);
                double[] z = new double[p.Dim];
                for (int i = 0; i < 2000; i++)
                {
                    Array.Copy(p.Y0, z, p.Dim);
                    KeplerUniversal.Propagate(p.CentralMu, z, 0, 3, dt);
                }

                sw.Restart();
                for (int i = 0; i < n; i++)
                {
                    Array.Copy(p.Y0, z, p.Dim);
                    KeplerUniversal.Propagate(p.CentralMu, z, 0, 3, dt);
                }

                sw.Stop();
                double tKep = sw.Elapsed.TotalMilliseconds;
                double w = tKep / Math.Max(tRhs, 1e-9d);
                return Math.Min(50d, Math.Max(0.05d, w));
            }
            catch (Exception)
            {
                return 1d;
            }
        }
    }

    internal static class StudySweepRunner
    {
        public static StudyScenarioResult Run(
            StudyProblem p, IList<IStudyIntegrator> integrators, StudyOptions o, TextWriter log)
        {
            string why;
            if (!p.Validate(out why))
            {
                throw new ArgumentException(p.Label + ": " + why);
            }

            var result = new StudyScenarioResult();
            result.Problem = p;
            result.RhsMultiplier = o.RhsCostMultiplier;
            double kw = double.IsNaN(o.KeplerWeight) ? StudyCalibration.MeasureKeplerWeight(p) : o.KeplerWeight;
            result.KeplerWeight = kw;
            foreach (IStudyIntegrator integ in integrators)
            {
                string reason;
                if (!integ.Supports(p, out reason))
                {
                    var na = new StudyRow();
                    na.Scenario = p.Label;
                    na.Integrator = integ.Name;
                    na.Kind = integ.Kind;
                    na.Status = "unsupported";
                    na.Note = reason;
                    result.Rows.Add(na);
                    if (log != null)
                    {
                        log.WriteLine("  " + integ.Name + ": n/a (" + reason + ")");
                    }

                    continue;
                }

                if (integ.Kind == StudyKind.Adaptive)
                {
                    foreach (double cap in o.AdaptiveMaxSteps)
                    {
                        SweepOne(p, integ, cap, o, result.Rows, log);
                    }
                }
                else
                {
                    SweepOne(p, integ, double.PositiveInfinity, o, result.Rows, log);
                }
            }

            StudyReport.FinalizeCosts(result.Rows, kw, o.RhsCostMultiplier);
            return result;
        }

        private static void SweepOne(
            StudyProblem p, IStudyIntegrator integ, double cap, StudyOptions o,
            List<StudyRow> rows, TextWriter log)
        {
            double coarse;
            double fine;
            double ratio;
            integ.ControlRange(p, out coarse, out fine, out ratio);
            if (p.MaxControl > 0d && coarse > p.MaxControl)
            {
                coarse = p.MaxControl;
            }

            double controlMax = integ.Kind == StudyKind.Fixed ? Math.Max(coarse, p.Duration) : 0.3d;
            if (p.MaxControl > 0d && controlMax > p.MaxControl)
            {
                controlMax = p.MaxControl;
            }
            double floor = double.IsNaN(p.Floor) ? 0d : p.Floor;
            double minTarget = double.NaN;
            double maxTarget = double.NaN;
            foreach (StudyTarget t in o.Targets(p))
            {
                if (t.Value < o.FloorMargin * floor)
                {
                    continue;
                }

                if (double.IsNaN(minTarget) || t.Value < minTarget)
                {
                    minTarget = t.Value;
                }

                if (double.IsNaN(maxTarget) || t.Value > maxTarget)
                {
                    maxTarget = t.Value;
                }
            }

            if (double.IsNaN(minTarget))
            {
                minTarget = Math.Max(o.FloorMargin * floor, 1e-12d * p.ScaleLength);
                maxTarget = minTarget;
            }

            string variant = double.IsInfinity(cap) ? "" : "cap=" + cap.ToString("G", CultureInfo.InvariantCulture) + " s";
            double c = coarse;
            StudyRow r = RunPoint(p, integ, c, cap, variant, o, rows);
            int guard = 0;
            while (r.Status == "ok" && r.Finite && r.Err <= o.LoosenFactor * maxTarget
                && c * ratio <= controlMax && guard++ < 12)
            {
                c *= ratio;
                r = RunPoint(p, integ, c, cap, variant, o, rows);
            }

            double best = double.PositiveInfinity;
            int stall = 0;
            int same = 0;
            long prevCalls = -1L;
            double stopErr = 0.25d * minTarget;
            for (int i = 0; i < o.MaxPoints; i++)
            {
                if (r.Status != "ok")
                {
                    break;
                }

                if (r.Finite)
                {
                    if (r.Err <= stopErr)
                    {
                        break;
                    }

                    if (r.Err < 0.9d * best)
                    {
                        best = r.Err;
                        stall = 0;
                    }
                    else if (r.Err < 0.01d * p.ScaleLength)
                    {
                        stall++;
                    }

                    if (stall >= 3)
                    {
                        r.Note = (r.Note + " plateau: error stopped improving").Trim();
                        break;
                    }
                }

                if (integ.Kind == StudyKind.Adaptive)
                {
                    same = r.Calls == prevCalls ? same + 1 : 0;
                    prevCalls = r.Calls;
                    if (same >= 2)
                    {
                        r.Note = (r.Note + " cap-bound: cost does not depend on rtol").Trim();
                        break;
                    }
                }

                double next = c / ratio;
                if (next < fine)
                {
                    r.Note = (r.Note + " reached finest control").Trim();
                    break;
                }

                c = next;
                r = RunPoint(p, integ, c, cap, variant, o, rows);
            }

            if (log != null)
            {
                log.WriteLine("  " + (variant.Length == 0 ? integ.Name : integ.Name + " [" + variant + "]")
                    + ": " + CountRows(rows, p.Label, integ.Name, variant).ToString(CultureInfo.InvariantCulture) + " points");
            }
        }

        private static int CountRows(List<StudyRow> rows, string scenario, string name, string variant)
        {
            int n = 0;
            foreach (StudyRow r in rows)
            {
                if (r.Scenario == scenario && r.Integrator == name && r.Variant == variant)
                {
                    n++;
                }
            }

            return n;
        }

        private static StudyRow RunPoint(
            StudyProblem p, IStudyIntegrator integ, double control, double cap, string variant,
            StudyOptions o, List<StudyRow> rows)
        {
            StudyConfig cfg = integ.MakeConfig(p, control, cap);
            var row = new StudyRow();
            row.Scenario = p.Label;
            row.Integrator = integ.Name;
            row.Variant = variant;
            row.Config = cfg.Label;
            row.Kind = integ.Kind;
            row.ExpectedOrder = integ.ExpectedOrder;
            row.Control = control;
            rows.Add(row);
            double bestMs = double.PositiveInfinity;
            try
            {
                int reps = 0;
                while (true)
                {
                    var ctx = new StudyContext(p, o.MaxCostRhs);
                    var sw = Stopwatch.StartNew();
                    StudyRun run = integ.Run(p, cfg, ctx);
                    sw.Stop();
                    double ms = sw.Elapsed.TotalMilliseconds;
                    if (reps == 0)
                    {
                        row.Calls = ctx.Calls;
                        row.Aux = ctx.AuxOps;
                        row.Steps = run.Steps;
                        row.Rejected = run.Rejected;
                        row.CapLimited = run.CapLimited;
                        EvaluateError(p, run, row);
                    }

                    if (ms < bestMs)
                    {
                        bestMs = ms;
                    }

                    reps++;
                    if (reps >= o.TimingReps || ms >= o.TimingRepThresholdMs)
                    {
                        break;
                    }
                }

                row.Ms = bestMs;
            }
            catch (StudyBudgetExceeded)
            {
                row.Status = "censored";
                row.Calls = o.MaxCostRhs;
                row.Note = "RHS budget " + o.MaxCostRhs.ToString(CultureInfo.InvariantCulture) + " exceeded (cost is a lower bound)";
            }
            catch (Exception ex)
            {
                row.Status = "error";
                row.Note = ex.GetType().Name + ": " + ex.Message;
            }

            return row;
        }

        private static void EvaluateError(StudyProblem p, StudyRun run, StudyRow row)
        {
            double worst = 0d;
            bool finite = true;
            double last = double.NaN;
            for (int k = 0; k < p.CheckpointTimes.Length; k++)
            {
                double[] s = run.Samples[k];
                if (s == null)
                {
                    finite = false;
                    break;
                }

                for (int i = 0; i < s.Length; i++)
                {
                    if (double.IsNaN(s[i]) || double.IsInfinity(s[i]))
                    {
                        finite = false;
                        break;
                    }
                }

                if (!finite)
                {
                    break;
                }

                double e = p.Error(s, p.CheckpointRef[k]);
                if (double.IsNaN(e) || double.IsInfinity(e))
                {
                    finite = false;
                    break;
                }

                if (e > worst)
                {
                    worst = e;
                }

                last = e;
            }

            row.Finite = finite;
            row.Err = finite ? worst : double.NaN;
            row.ErrFinal = finite ? last : double.NaN;
        }
    }
}
