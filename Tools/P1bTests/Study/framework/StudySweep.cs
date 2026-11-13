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

        /// <summary>Cost of one auxiliary operation (Kepler solve) in RHS calls. NaN = use the pinned value or measure it.</summary>
        public double KeplerWeight = double.NaN;

        /// <summary>
        /// Legacy compatibility only: the current unit is `cost = calls + w*aux` and this
        /// value is ignored. It stays so archived Test555 compiles without a source change.
        /// </summary>
        [Obsolete("Ignored. The cost unit is calls + w*aux.")]
        public double RhsCostMultiplier = 1d;

        /// <summary>
        /// Adaptive methods are run once per cap. Include +Infinity (the method's own choice) AND the
        /// production cap (600 s ship / 86400 s planets): a cap that binds makes cost independent of rtol.
        /// </summary>
        public double[] AdaptiveMaxSteps = new[] { double.PositiveInfinity };

        /// <summary>A target below FloorMargin * reference floor is not ranked (it would measure reference noise).</summary>
        public double FloorMargin = 10d;

        /// <summary>Largest rtol the coarse sweep may try. Weaker tolerances can close EDGE cells.</summary>
        public double AdaptiveControlMax = 3d;

        /// <summary>Tie band for interpolated cells (1.10 = 10%). Unbracketed cells use one grid step instead.</summary>
        public double TieFactor = 1.10d;

        /// <summary>Optional override of every method's control grid ratio (e.g. 1.15 for a tie rerun). NaN = method default.</summary>
        public double GridRatio = double.NaN;

        /// <summary>
        /// After the coarse sweep, bisect the control bracket around each target. Reporting
        /// interpolates the cost inside the bracket, but extra executed points still make the
        /// interpolation local and expose non-monotonic branches; without them the bracket can
        /// span 1.5x in h or sqrt(10)x in rtol.
        /// </summary>
        public bool RefineTies = true;

        public int RefineIterations = 5;

        public double RefineCostTolerance = 0.10d;

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

        /// <summary>
        /// Column labels for the cross-scenario "who wins" table. They must not contain
        /// per-problem values: a relative target is a different number of metres in every
        /// scenario, and using the first problem's labels mislabels every other row.
        /// </summary>
        public List<string> SummaryLabels()
        {
            var list = new List<string>();
            foreach (double rel in RelTargets)
            {
                list.Add(rel.ToString("0.0E+0", CultureInfo.InvariantCulture) + " rel");
            }

            foreach (double abs in AbsTargets)
            {
                list.Add(abs.ToString("G3", CultureInfo.InvariantCulture) + " m");
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
        public string Family = "";
        public int Stages;
        public StudyKind Kind;
        public double ExpectedOrder = double.NaN;
        public double Control;
        public string Status = "ok";          // ok | censored | error | unsupported
        public string Note = "";
        public bool Finite;
        public double Err = double.NaN;       // max over checkpoints
        public double ErrFinal = double.NaN;
        // Q goals of TASK.md Appendix B: values at the LAST checkpoint (B6: rms8_end, Moon).
        public double QErrRms8 = double.NaN;
        public double QErrMoon = double.NaN;
        // Reference-only column of B6: rms8 of this run against Horizons (mixes model and
        // integrator error; never used for ranking).
        public double QErrHorizons = double.NaN;

        /// <summary>Worst error over the checkpoints reached before an abort (NaN when none).</summary>
        public double PartialErr = double.NaN;
        public long Calls;
        public long Aux;
        public long StartupCalls;
        public long Steps;
        public long Rejected;
        public long CapLimited;
        public int Nck;
        public int StepsForcedMin;
        public bool CheckpointBound;
        public string CheckpointMode = "all";
        public double Cost;                   // set by StudyReport.FinalizeCosts
        public double Ms = double.NaN;
        /// <summary>Max |E(sample)-E(initial)| over reached checkpoints; NaN when not requested.</summary>
        public double EnergyDriftFromInitial = double.NaN;
        /// <summary>Max |E(sample)-E(reference at the same checkpoint)|; NaN when not requested.</summary>
        public double EnergyDriftVsRef = double.NaN;
        /// <summary>Max |E(reference)-E(initial)|; the scenario's own physical drift.</summary>
        public double EnergyDriftRef = double.NaN;
        /// <summary>Max |L(sample)-L(initial)| over reached checkpoints; NaN when not requested.</summary>
        public double AngularDriftFromInitial = double.NaN;
        /// <summary>Max |L(sample)-L(reference at the same checkpoint)|; NaN when not requested.</summary>
        public double AngularDriftVsRef = double.NaN;
        /// <summary>Max |L(reference)-L(initial)|; the scenario's own physical drift.</summary>
        public double AngularDriftRef = double.NaN;

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
        public double KeplerCv;
        public string KeplerSource = "fixed";
        public string KeplerNote = "";
        public long MaxCostRhs;
    }

    internal sealed class StudyKeplerWeight
    {
        public double Weight;
        public double Cv;
        public string Source = "fixed";
        public string Note = "";
        public string MeasuredUtc = "";
        public double DtSeconds;
        public int Blocks;
        public int CallsPerBlock;
    }

    /// <summary>Read/write the pinned per-scenario Kepler weights. One row per scenario.</summary>
    internal static class StudyKeplerPins
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string Header =
            "scenario,kepler_weight,kepler_cv,source,measured_utc,dotnet,arch,dt_seconds,blocks,calls_per_block,note";

        public static StudyKeplerWeight Read(string path, string scenario)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 1; i < lines.Length; i++)
                {
                    List<string> f = Split(lines[i]);
                    if (f.Count < 4 || !string.Equals(f[0], scenario, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var r = new StudyKeplerWeight();
                    r.Weight = ParseDouble(f[1]);
                    r.Cv = ParseDouble(f[2]);
                    r.Source = "pinned";
                    r.MeasuredUtc = f.Count > 4 ? f[4] : "";
                    r.DtSeconds = f.Count > 7 ? ParseDouble(f[7]) : double.NaN;
                    r.Blocks = f.Count > 8 ? (int)ParseDouble(f[8]) : 0;
                    r.CallsPerBlock = f.Count > 9 ? (int)ParseDouble(f[9]) : 0;
                    r.Note = f.Count > 10 ? f[10] : "";
                    return r;
                }
            }
            catch (Exception)
            {
                // A corrupt pin file must not kill a sweep: fall through to a fresh measurement.
            }

            return null;
        }

        public static void Upsert(string path, string scenario, StudyKeplerWeight r)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var rows = new List<string>();
                if (File.Exists(path))
                {
                    rows.AddRange(File.ReadAllLines(path));
                }

                if (rows.Count == 0)
                {
                    rows.Add(Header);
                }

                string line = Csv(scenario) + ","
                    + r.Weight.ToString("R", Inv) + ","
                    + r.Cv.ToString("R", Inv) + ","
                    + (r.Source ?? "") + ","
                    + (r.MeasuredUtc ?? "") + ","
                    + Environment.Version.ToString() + ","
                    + (Environment.Is64BitProcess ? "x64" : "x86") + ","
                    + r.DtSeconds.ToString("R", Inv) + ","
                    + r.Blocks.ToString(Inv) + ","
                    + r.CallsPerBlock.ToString(Inv) + ","
                    + Csv(r.Note ?? "");

                bool replaced = false;
                for (int i = 1; i < rows.Count; i++)
                {
                    List<string> f = Split(rows[i]);
                    if (f.Count > 0 && string.Equals(f[0], scenario, StringComparison.Ordinal))
                    {
                        rows[i] = line;
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                {
                    rows.Add(line);
                }

                string tmp = path + ".tmp";
                File.WriteAllLines(tmp, rows);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tmp, path);
            }
            catch (Exception)
            {
                // Pinning is an optimization for repeatability; a read-only tree must not stop the run.
            }
        }

        private static double ParseDouble(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, Inv, out v) ? v : double.NaN;
        }

        private static string Csv(string s)
        {
            if (s == null)
            {
                return "";
            }

            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return s;
            }

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static List<string> Split(string line)
        {
            var fields = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(c);
                }
            }

            fields.Add(sb.ToString());
            return fields;
        }
    }

    internal static class StudyCalibration
    {
        public const int KeplerBlocks = 11;
        public const int KeplerCallsPerBlock = 100000;
        private static readonly object PinLock = new object();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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
        /// Resolve the price of one Kepler solve: explicit option, environment override, pinned
        /// per-scenario measurement, or a fresh measurement that is then pinned. The pin file makes
        /// repeated runs comparable even though wall-clock w varies between processes.
        /// </summary>
        public static StudyKeplerWeight Resolve(StudyProblem p, StudyOptions o)
        {
            if (!double.IsNaN(o.KeplerWeight))
            {
                return Fixed(o.KeplerWeight, "options", "");
            }

            string envWeight = Environment.GetEnvironmentVariable("P1B_KEPLER_WEIGHT");
            double parsed;
            if (!string.IsNullOrWhiteSpace(envWeight)
                && double.TryParse(envWeight, NumberStyles.Float, Inv, out parsed))
            {
                return Fixed(parsed, "env", "P1B_KEPLER_WEIGHT");
            }

            string path = PinPath();
            bool force = string.Equals(Environment.GetEnvironmentVariable("P1B_MEASURE_KEPLER"), "1",
                StringComparison.Ordinal);
            if (!force)
            {
                StudyKeplerWeight pinned = StudyKeplerPins.Read(path, p.Label);
                if (pinned != null)
                {
                    pinned.Source = "pinned";
                    return pinned;
                }
            }

            StudyKeplerWeight measured = Measure(p);
            measured.Source = "measured";
            measured.MeasuredUtc = DateTime.UtcNow.ToString("o", Inv);
            lock (PinLock)
            {
                StudyKeplerPins.Upsert(path, p.Label, measured);
            }

            return measured;
        }

        private static StudyKeplerWeight Fixed(double weight, string source, string note)
        {
            var r = new StudyKeplerWeight();
            r.Weight = weight;
            r.Cv = 0d;
            r.Source = source;
            r.Note = note;
            return r;
        }

        /// <summary>
        /// Measures one Kepler solve in units of one RHS call of THIS problem. 11 blocks of
        /// 100 000 paired calls, warm-up before each block, median ratio, CV over the blocks.
        /// A Kepler solve is called exactly as EnckeStepper calls it: copy the state, then
        /// universal-variable propagation over dt = 1e-3 * CharTime.
        /// </summary>
        public static StudyKeplerWeight Measure(StudyProblem p)
        {
            var result = new StudyKeplerWeight();
            result.Blocks = KeplerBlocks;
            result.CallsPerBlock = KeplerCallsPerBlock;

            if (!(p.CentralMu > 0d) || p.PosDim != 3)
            {
                result.Weight = 0d;
                result.Cv = 0d;
                result.Note = "no central body; no Kepler solve is used";
                return result;
            }

            double dt = 1e-3d * (p.CharTime > 0d ? p.CharTime : 1d);
            result.DtSeconds = dt;
            try
            {
                double[] y = (double[])p.Y0.Clone();
                double[] dy = new double[p.Dim];
                double[] z = new double[p.Dim];

                // Warm-up: JIT (including tiered compilation), cache, branch predictor.
                for (int i = 0; i < 20000; i++)
                {
                    p.Rhs(p.T0, y, dy);
                }

                for (int i = 0; i < 20000; i++)
                {
                    Array.Copy(p.Y0, z, p.Dim);
                    KeplerUniversal.Propagate(p.CentralMu, z, 0, 3, dt);
                }

                // One extra block is discarded: the first block can still see tiered JIT.
                var ratios = new double[KeplerBlocks + 1];
                var sw = new Stopwatch();
                for (int b = 0; b < KeplerBlocks + 1; b++)
                {
                    sw.Restart();
                    for (int i = 0; i < KeplerCallsPerBlock; i++)
                    {
                        p.Rhs(p.T0, y, dy);
                    }

                    sw.Stop();
                    double tRhs = sw.Elapsed.TotalMilliseconds;

                    sw.Restart();
                    for (int i = 0; i < KeplerCallsPerBlock; i++)
                    {
                        Array.Copy(p.Y0, z, p.Dim);
                        KeplerUniversal.Propagate(p.CentralMu, z, 0, 3, dt);
                    }

                    sw.Stop();
                    double tKep = sw.Elapsed.TotalMilliseconds;
                    ratios[b] = tKep / Math.Max(tRhs, 1e-9d);
                }

                var used = new double[KeplerBlocks];
                Array.Copy(ratios, 1, used, 0, KeplerBlocks);
                Array.Sort(used);
                result.Weight = used[KeplerBlocks / 2];
                double mean = 0d;
                for (int i = 0; i < used.Length; i++)
                {
                    mean += used[i];
                }

                mean /= used.Length;
                double variance = 0d;
                for (int i = 0; i < used.Length; i++)
                {
                    double d = used[i] - mean;
                    variance += d * d;
                }

                variance /= used.Length;
                result.Cv = mean > 0d ? Math.Sqrt(variance) / mean : 0d;
            }
            catch (Exception ex)
            {
                result.Weight = 1d;
                result.Cv = double.NaN;
                result.Note = "measurement failed: " + ex.GetType().Name + ": " + ex.Message;
            }

            if (!double.IsFinite(result.Weight) || result.Weight < 0d)
            {
                result.Weight = 1d;
                result.Cv = double.NaN;
                result.Note = "non-finite weight; fallback 1";
            }

            return result;
        }

        public static string PinPath()
        {
            string overridePath = Environment.GetEnvironmentVariable("P1B_KEPLER_WEIGHTS");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath);
            }

            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Tools", "P1bTests", "Program.cs")))
                {
                    return Path.Combine(dir, "Tools", "P1bTests", "Study", "integrators", "kepler_weights.csv");
                }

                dir = Path.GetDirectoryName(dir);
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "Study", "integrators", "kepler_weights.csv");
        }
    }

    internal static class StudySweepRunner
    {
        public static StudyScenarioResult Run(
            StudyProblem p, IList<IStudyIntegrator> integrators, StudyOptions o, TextWriter log)
        {
            return Run(p, integrators, o, log, false);
        }

        /// <summary>One recorded checkpoint at the end; mandatory force breakpoints remain.</summary>
        public static StudyScenarioResult RunEndOnly(
            StudyProblem p, IList<IStudyIntegrator> integrators, StudyOptions o, TextWriter log)
        {
            return Run(p, integrators, o, log, true);
        }

        private static StudyScenarioResult Run(
            StudyProblem p, IList<IStudyIntegrator> integrators, StudyOptions o, TextWriter log,
            bool endOnly)
        {
            if (endOnly)
            {
                p = p.CloneEndOnly();
            }

            string why;
            if (!p.Validate(out why))
            {
                throw new ArgumentException(p.Label + ": " + why);
            }

            var result = new StudyScenarioResult();
            result.Problem = p;
            result.MaxCostRhs = o.MaxCostRhs;
            StudyKeplerWeight kwr = StudyCalibration.Resolve(p, o);
            double kw = kwr.Weight;
            result.KeplerWeight = kw;
            result.KeplerCv = kwr.Cv;
            result.KeplerSource = kwr.Source;
            result.KeplerNote = kwr.Note;
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
                    na.Nck = p.CheckpointTimes.Length;
                    na.StepsForcedMin = StudyStops.Build(p).Times.Length;
                    na.CheckpointMode = endOnly ? "end" : "all";
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
                        SweepOne(p, integ, cap, o, kw, result.Rows, log, endOnly);
                    }
                }
                else
                {
                    SweepOne(p, integ, double.PositiveInfinity, o, kw, result.Rows, log, endOnly);
                }
            }

            StudyReport.FinalizeCosts(result.Rows, kw);
            return result;
        }

        private static void SweepOne(
            StudyProblem p, IStudyIntegrator integ, double cap, StudyOptions o, double keplerWeight,
            List<StudyRow> rows, TextWriter log, bool endOnly)
        {
            double coarse;
            double fine;
            double ratio;
            integ.ControlRange(p, out coarse, out fine, out ratio);
            if (o.GridRatio > 1d)
            {
                ratio = o.GridRatio;
            }
            if (p.MaxControl > 0d && coarse > p.MaxControl)
            {
                coarse = p.MaxControl;
            }

            double controlMax = integ.Kind == StudyKind.Fixed
                ? Math.Max(coarse, p.Duration)
                : o.AdaptiveControlMax;
            if (p.MaxControl > 0d && controlMax > p.MaxControl)
            {
                controlMax = p.MaxControl;
            }
            double floor = double.IsNaN(p.Floor) ? 0d : p.Floor;
            // A fixed-step plateau is only terminal when it is at the reference floor:
            // there is nothing left to resolve. A plateau above it (e.g. WH-Y6 at large
            // h on a circular orbit) is a pre-asymptotic regime that still improves at
            // smaller h, so it must not stop the sweep.
            double plateauFloor = Math.Max(10d * floor, 1e-12d * p.ScaleLength);
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
            StudyRow r = RunPoint(p, integ, c, cap, variant, o, rows, endOnly);
            int guard = 0;
            while (r.Status == "ok" && r.Finite && r.Err <= o.LoosenFactor * maxTarget
                && c * ratio <= controlMax && guard++ < 12)
            {
                c *= ratio;
                r = RunPoint(p, integ, c, cap, variant, o, rows, endOnly);
            }

            double best = double.PositiveInfinity;
            int stall = 0;
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
                    else if (integ.Kind == StudyKind.Fixed && r.Err <= plateauFloor)
                    {
                        // Fixed-step round-off plateaus at the reference floor are
                        // terminal: finer h cannot improve them. Adaptive methods are
                        // different: a plateau far above the targets is the
                        // loose-tolerance/diverged regime, and tightening rtol can still
                        // enter the convergent branch (the planet sweep loses the whole
                        // useful branch if it stops here).
                        stall++;
                    }

                    if (stall >= 3)
                    {
                        r.Note = (r.Note + " plateau: error stopped improving").Trim();
                        break;
                    }
                }

                // A flat cost across two loose tolerances does NOT mean the whole
                // branch is cap-bound. While the cap binds, the controller cannot
                // ask for the smaller step a tighter tolerance needs; once rtol is
                // tight enough the cap stops binding and the error falls again.
                // Stopping here hid that branch (the planet sweep lost all
                // adaptive points, and DOP853 [cap=600 s] on LEO looked unable to
                // reach 100 m). Keep sweeping to the fine bound or the budget.

                double next = c / ratio;
                // The 0.1% guard keeps c/ratio = 1e-13 from rounding just below the
                // fine bound and skipping the last intended point.
                if (next < fine * 0.999d)
                {
                    r.Note = (r.Note + " reached finest control").Trim();
                    break;
                }

                c = next;
                r = RunPoint(p, integ, c, cap, variant, o, rows, endOnly);
            }

            RefineTargetBrackets(p, integ, cap, variant, o, keplerWeight, rows, endOnly);

            if (log != null)
            {
                log.WriteLine("  " + (variant.Length == 0 ? integ.Name : integ.Name + " [" + variant + "]")
                    + ": " + CountRows(rows, p.Label, integ.Name, variant).ToString(CultureInfo.InvariantCulture) + " points");
            }
        }

        /// <summary>
        /// The coarse sweep brackets a target with one grid step (1.5x in h, sqrt(10)x in rtol).
        /// After switching to "cheapest executed run", that step becomes the resolution of the
        /// reported cost and is coarser than the tie band. Bisect only the winning bracket:
        /// run a point between the cheapest hit and the next cheaper miss until their costs are
        /// closer than the tie tolerance. Every added point is a real run, so the cost stays honest.
        /// </summary>
        private static void RefineTargetBrackets(
            StudyProblem p, IStudyIntegrator integ, double cap, string variant,
            StudyOptions o, double keplerWeight, List<StudyRow> rows, bool endOnly)
        {
            if (!o.RefineTies)
            {
                return;
            }

            double floor = double.IsNaN(p.Floor) ? 0d : p.Floor;
            foreach (StudyTarget target in o.Targets(p))
            {
                if (target.Value < o.FloorMargin * floor)
                {
                    continue;
                }

                // At most 12 extra executed runs per cell. The reported cost is always
                // the cheapest executed hit; the bracket is only used to decide whether
                // a cheaper hit can still be found.
                int addedRuns = 0;
                while (addedRuns < 12)
                {
                    StudyRow hit = null;
                    double hitCost = double.PositiveInfinity;
                    StudyRow miss = null;
                    double missCost = double.NegativeInfinity;
                    foreach (StudyRow r in rows)
                    {
                        if (r.Scenario != p.Label || r.Integrator != integ.Name
                            || r.Variant != variant || r.Status != "ok" || !r.Finite)
                        {
                            continue;
                        }

                        double cost = ProvisionalCost(r, o, keplerWeight);
                        if (r.Err <= target.Value && cost < hitCost)
                        {
                            hitCost = cost;
                            hit = r;
                        }
                    }

                    if (hit == null)
                    {
                        break;
                    }

                    foreach (StudyRow r in rows)
                    {
                        if (r.Scenario != p.Label || r.Integrator != integ.Name
                            || r.Variant != variant || r.Status != "ok" || !r.Finite)
                        {
                            continue;
                        }

                        double cost = ProvisionalCost(r, o, keplerWeight);
                        if (r.Err > target.Value && cost < hitCost && cost > missCost)
                        {
                            missCost = cost;
                            miss = r;
                        }
                    }

                    if (miss == null)
                    {
                        break;
                    }

                    if (hitCost <= 1.10d * missCost)
                    {
                        break;
                    }

                    // All-checkpoint fixed-step grid: with equal intervals the step
                    // count jumps by nck (WH-BM14A: 20 -> 40). No intermediate run
                    // exists, so spending the 12-run budget here is pointless.
                    if (integ.Kind == StudyKind.Fixed && !endOnly && hit.Nck > 1
                        && hit.Steps > 0L && miss.Steps > 0L && hit.Steps != miss.Steps
                        && hit.Steps % hit.Nck == 0L && miss.Steps % hit.Nck == 0L)
                    {
                        break;
                    }

                    double control;
                    if (integ.Kind == StudyKind.Fixed && endOnly)
                    {
                        // End-only has one interval: h = duration / integer steps.
                        long mid = (hit.Steps + miss.Steps) / 2L;
                        if (mid <= 0L || mid == hit.Steps || mid == miss.Steps)
                        {
                            break;
                        }

                        control = p.Duration / mid;
                    }
                    else
                    {
                        control = Math.Sqrt(hit.Control * miss.Control);
                    }

                    double lo = Math.Min(hit.Control, miss.Control);
                    double hi = Math.Max(hit.Control, miss.Control);
                    if (!(control > lo) || !(control < hi))
                    {
                        break;
                    }

                    bool exists = false;
                    foreach (StudyRow r in rows)
                    {
                        if (r.Scenario != p.Label || r.Integrator != integ.Name || r.Variant != variant)
                        {
                            continue;
                        }

                        if (Math.Abs(r.Control - control) <= 1e-9d * Math.Max(Math.Abs(control), 1e-300d))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (exists)
                    {
                        break;
                    }

                    addedRuns++;
                    StudyRow added = RunPoint(p, integ, control, cap, variant, o, rows, endOnly);
                    if (added.Status != "ok" || !added.Finite)
                    {
                        break;
                    }
                }
            }
        }

        private static double ProvisionalCost(StudyRow r, StudyOptions o, double keplerWeight)
        {
            double weight = double.IsNaN(keplerWeight) ? 0d : keplerWeight;
            return r.Calls + (weight * r.Aux);
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
            StudyOptions o, List<StudyRow> rows, bool endOnly)
        {
            // One control point is run once per (scenario, method, variant, mode). The
            // coarse-up and fine-down passes of the sweep share a grid, so without this
            // cache every coarse point is executed a second time.
            foreach (StudyRow prior in rows)
            {
                if (prior.Scenario == p.Label && prior.Integrator == integ.Name
                    && prior.Variant == variant
                    && string.Equals(prior.CheckpointMode, endOnly ? "end" : "all", StringComparison.Ordinal)
                    && Math.Abs(prior.Control - control)
                        <= 1e-9d * Math.Max(Math.Abs(control), 1e-300d))
                {
                    return prior;
                }
            }

            StudyConfig cfg = integ.MakeConfig(p, control, cap);
            var row = new StudyRow();
            row.Scenario = p.Label;
            row.Integrator = integ.Name;
            row.Variant = variant;
            row.Config = cfg.Label;
            row.Family = integ.Family;
            row.Stages = StudyReport.StagesOf(integ.Name);
            row.Kind = integ.Kind;
            row.ExpectedOrder = integ.ExpectedOrder;
            row.Control = control;
            row.Nck = p.CheckpointTimes.Length;
            row.StepsForcedMin = StudyStops.Build(p).Times.Length;
            row.CheckpointMode = endOnly ? "end" : "all";
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
                        row.StartupCalls = ctx.StartupCalls;
                        row.Steps = run.Steps;
                        row.Rejected = run.Rejected;
                        row.CapLimited = run.CapLimited;
                        row.CheckpointBound = row.Steps <= 1.5d * row.Nck;
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

        internal static void EvaluateError(StudyProblem p, StudyRun run, StudyRow row)
        {
            double worst = 0d;
            bool finite = true;
            bool complete = true;
            double last = double.NaN;
            double energy0 = p.EnergyFn != null ? p.EnergyFn(p.Y0) : double.NaN;
            double angular0 = p.AngularMomentumFn != null ? p.AngularMomentumFn(p.Y0) : double.NaN;
            double energyDrift = 0d;
            double energyVsRef = 0d;
            double energyRefDrift = 0d;
            double angularDrift = 0d;
            double angularVsRef = 0d;
            double angularRefDrift = 0d;
            bool energySeen = false;
            bool energyRefSeen = false;
            bool angularSeen = false;
            bool angularRefSeen = false;
            double qRms8 = double.NaN;
            double qMoon = double.NaN;
            double qHorizons = double.NaN;
            for (int k = 0; k < p.CheckpointTimes.Length; k++)
            {
                double[] s = run.Samples[k];
                if (s == null)
                {
                    complete = false;
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

                double[] reference = p.CheckpointRef[k];
                bool hasRef = reference != null && reference.Length >= 6;
                if (p.EnergyFn != null && double.IsFinite(energy0))
                {
                    double energy = p.EnergyFn(s);
                    if (double.IsFinite(energy))
                    {
                        energyDrift = Math.Max(energyDrift, Math.Abs(energy - energy0));
                        energySeen = true;
                        if (hasRef)
                        {
                            double energyRef = p.EnergyFn(reference);
                            if (double.IsFinite(energyRef))
                            {
                                energyVsRef = Math.Max(energyVsRef, Math.Abs(energy - energyRef));
                                energyRefDrift = Math.Max(energyRefDrift, Math.Abs(energyRef - energy0));
                                energyRefSeen = true;
                            }
                        }
                    }
                }

                if (p.AngularMomentumFn != null && double.IsFinite(angular0))
                {
                    double a = p.AngularMomentumFn(s);
                    if (double.IsFinite(a))
                    {
                        angularDrift = Math.Max(angularDrift, Math.Abs(a - angular0));
                        angularSeen = true;
                        if (hasRef)
                        {
                            double angularRef = p.AngularMomentumFn(reference);
                            if (double.IsFinite(angularRef))
                            {
                                angularVsRef = Math.Max(angularVsRef, Math.Abs(a - angularRef));
                                angularRefDrift = Math.Max(angularRefDrift, Math.Abs(angularRef - angular0));
                                angularRefSeen = true;
                            }
                        }
                    }
                }

                double e = p.Error(s, p.CheckpointRef[k]);
                if (double.IsNaN(e) || double.IsInfinity(e))
                {
                    finite = false;
                    break;
                }

                if (p.QErrorFn != null && hasRef)
                {
                    // Additive hook (Appendix B, B6): the last checkpoint's two Q values.
                    double[] q = p.QErrorFn(s, reference);
                    if (q != null && q.Length >= 2)
                    {
                        qRms8 = q[0];
                        qMoon = q[1];
                        if (q.Length >= 3)
                        {
                            qHorizons = q[2];
                        }
                    }
                }

                if (e > worst)
                {
                    worst = e;
                }

                last = e;
            }

            row.Finite = finite && complete;
            row.Err = row.Finite ? worst : double.NaN;
            row.ErrFinal = row.Finite ? last : double.NaN;
            row.QErrRms8 = qRms8;
            row.QErrMoon = qMoon;
            row.QErrHorizons = qHorizons;
            row.EnergyDriftFromInitial = energySeen ? energyDrift : double.NaN;
            row.EnergyDriftVsRef = energyRefSeen ? energyVsRef : double.NaN;
            row.EnergyDriftRef = energyRefSeen ? energyRefDrift : double.NaN;
            row.AngularDriftFromInitial = angularSeen ? angularDrift : double.NaN;
            row.AngularDriftVsRef = angularRefSeen ? angularVsRef : double.NaN;
            row.AngularDriftRef = angularRefSeen ? angularRefDrift : double.NaN;
            if (!complete)
            {
                row.PartialErr = finite && last >= 0d ? worst : double.NaN;
                string reason = run.Aborted
                    ? "aborted at t=" + run.AbortTime.ToString("E3", CultureInfo.InvariantCulture)
                      + " s: " + run.Abort
                    : "the run stopped before the horizon without a recorded reason";
                if (row.PartialErr >= 0d)
                {
                    reason += "; worst error at completed checkpoints "
                        + row.PartialErr.ToString("E2", CultureInfo.InvariantCulture) + " m";
                }

                row.Note = (row.Note.Length == 0 ? "" : row.Note + "; ") + reason;
            }
            else if (!finite)
            {
                row.Note = (row.Note.Length == 0 ? "" : row.Note + "; ")
                    + "non-finite state at a checkpoint (RHS overflow or a diverged trajectory)";
            }
        }
    }
}
