using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using P1bStudy.Framework;

// Test564 M3: support matrix and gates G1 (accuracy), G2 (observed order), G5 (finiteness),
// G6 (determinism) on the K and T catalogue.
//
// The sweep itself is the framework's StudySweepRunner: it owns the control grid, the
// interpolation/refinement and the RHS/aux cost accounting. This file only maps its rows to
// the PASS / FAIL / N/A / SKIP / ERROR vocabulary of TASK.md section 8 and writes runs.csv /
// matrix.csv.
//
// No method is named in any exclusion condition: N/A comes only from Supports(...), and the
// set of "splitting" methods is classified automatically by probing Supports on two synthetic
// problems that differ only in ConservativeSecondOrder.
internal static partial class P1bTests
{
    /// <summary>TASK.md section 7. Absolute level = factor * problem ScaleLength.</summary>
    private static readonly double[] QualificationLevelFactors = { 1e-6d, 1e-9d, 1e-12d };

    private static readonly string[] QualificationLevelNames = { "A1", "A2", "A3" };

    /// <summary>Ordered goal names of one problem: A1-A3 (if enabled) then M100…M0.01.</summary>
    private static List<QualificationGoal> QualificationGoalsOf(
        QualificationProblem q, StudyProblem p, QualificationProfile profile)
    {
        var goals = new List<QualificationGoal>();
        if (q.AnalyticLevels)
        {
            for (int i = 0; i < QualificationLevelFactors.Length; i++)
            {
                var g = new QualificationGoal();
                g.Name = QualificationLevelNames[i];
                g.Factor = QualificationLevelFactors[i];
                g.Threshold = g.Factor * p.ScaleLength;
                goals.Add(g);
            }
        }

        if (q.MeterTargets != null)
        {
            foreach (double target in q.MeterTargets)
            {
                if (!(target > 0d) || !double.IsFinite(target))
                {
                    continue;
                }

                // Smoke is the minutes-long profile (TASK.md 13): it evaluates the coarse
                // metre goals M100…M10 and leaves 1 m and finer to full. Full evaluates A7 whole.
                if (profile == QualificationProfile.Smoke && target < 10d)
                {
                    continue;
                }

                var g = new QualificationGoal();
                g.Name = QualificationMeterGoalName(target);
                g.Threshold = target;
                g.Factor = p.ScaleLength > 0d ? target / p.ScaleLength : double.NaN;
                g.Meter = true;
                goals.Add(g);
            }
        }

        return goals;
    }

    /// <summary>Cell count of a problem without building it (resume completeness check).</summary>
    private static int QualificationGoalCount(QualificationProblem q, QualificationProfile profile)
    {
        int n = q.AnalyticLevels ? QualificationLevelFactors.Length : 0;
        if (q.MeterTargets != null)
        {
            foreach (double target in q.MeterTargets)
            {
                if (target > 0d && double.IsFinite(target)
                    && !(profile == QualificationProfile.Smoke && target < 10d))
                {
                    n++;
                }
            }
        }

        return n;
    }

    private static string QualificationMeterGoalName(double target)
    {
        return "M" + target.ToString("0.##", QualificationInv);
    }

    private static double QualificationGoalThreshold(QualificationGoal g, StudyProblem p)
    {
        return g.Meter ? g.Threshold : g.Factor * p.ScaleLength;
    }

    private sealed class QualificationCell
    {
        public string ProblemId = "";
        public string Method = "";
        public string Level = "";
        public string Status = "";
        public string Reason = "";
        public double BestControl = double.NaN;
        public double Price = double.NaN;
        public double Err = double.NaN;
        public bool Meter;
        public StudyRow Best;
    }

    /// <summary>
    /// A symmetric order-refinement pass (owner decision 2026-11-13, explicit exception to
    /// §14.4): for EVERY fixed-step method with a declared order, after the main sweep add up
    /// to eight controls below the current finest one, until the error reaches the G2 window's
    /// lower edge, the budget censors a point, or the control range ends. The gate thresholds,
    /// the window and the 0.5 tolerance are untouched; only the sampled branch becomes long
    /// enough to show the asymptotic slope. The pass runs for all fixed methods, not only for
    /// those that currently fail, so the result may move in both directions.
    /// </summary>
    private static void QualificationOrderRefine(
        StudyProblem p, IStudyIntegrator method, List<StudyRow> rows, StudyOptions options,
        TextWriter log)
    {
        if (method.Kind != StudyKind.Fixed || double.IsNaN(method.ExpectedOrder))
        {
            return;
        }

        StudyRow finest = null;
        foreach (StudyRow r in rows)
        {
            if (r.Integrator != method.Name || r.Variant.Length != 0 || r.Status != "ok"
                || !r.Finite || !(r.Control > 0d) || !(r.Err > 0d))
            {
                continue;
            }

            if (finest == null || r.Control < finest.Control)
            {
                finest = r;
            }
        }

        if (finest == null)
        {
            return;
        }

        double coarse;
        double fine;
        double ratio;
        method.ControlRange(p, out coarse, out fine, out ratio);
        if (!(ratio > 1d))
        {
            return;
        }

        double floor = double.IsNaN(p.Floor) ? 0d : p.Floor;
        double low = Math.Max(100d * floor, 1e-16d * p.ScaleLength);
        double control = finest.Control;
        int added = 0;
        for (int k = 0; k < QualificationOrderRefinePoints; k++)
        {
            double next = control / ratio;
            if (next < fine * 0.999d)
            {
                break;
            }

            StudyRow row = QualificationRunSinglePoint(p, method, next, options);
            rows.Add(row);
            added++;
            if (row.Status != "ok" || !row.Finite || !(row.Err > 0d))
            {
                break;
            }

            if (row.Err <= low)
            {
                break;
            }

            control = next;
        }

        if (added > 0)
        {
            log.WriteLine("order refine " + method.Name + " " + p.Label + ": +"
                + added.ToString(QualificationInv) + " point(s), finest err "
                + QualificationNumber(finest.Err));
        }
    }

    private const int QualificationOrderRefinePoints = 8;

    /// <summary>One executed control point, with the same accounting as the sweep's RunPoint.</summary>
    private static StudyRow QualificationRunSinglePoint(
        StudyProblem p, IStudyIntegrator method, double control, StudyOptions options)
    {
        StudyConfig cfg = method.MakeConfig(p, control, double.PositiveInfinity);
        var row = new StudyRow();
        row.Scenario = p.Label;
        row.Integrator = method.Name;
        row.Variant = "";
        row.Config = cfg.Label;
        row.Family = method.Family;
        row.Stages = StudyReport.StagesOf(method.Name);
        row.Kind = method.Kind;
        row.ExpectedOrder = method.ExpectedOrder;
        row.Control = control;
        row.Nck = p.CheckpointTimes.Length;
        row.StepsForcedMin = StudyStops.Build(p).Times.Length;
        row.CheckpointMode = "all";
        try
        {
            var ctx = new StudyContext(p, options.MaxCostRhs);
            StudyRun run = method.Run(p, cfg, ctx);
            row.Calls = ctx.Calls;
            row.Aux = ctx.AuxOps;
            row.StartupCalls = ctx.StartupCalls;
            row.Steps = run.Steps;
            row.Rejected = run.Rejected;
            row.CapLimited = run.CapLimited;
            row.CheckpointBound = row.Steps <= 1.5d * row.Nck;
            StudySweepRunner.EvaluateError(p, run, row);
        }
        catch (StudyBudgetExceeded)
        {
            row.Status = "censored";
            row.Calls = options.MaxCostRhs;
            row.Note = "RHS budget " + options.MaxCostRhs.ToString(QualificationInv)
                + " exceeded (order refinement)";
        }
        catch (Exception ex)
        {
            row.Status = "error";
            row.Note = ex.GetType().Name + ": " + ex.Message;
        }

        return row;
    }

    private sealed class QualificationGoal
    {
        public string Name = "";
        public double Threshold;
        /// <summary>Threshold / ScaleLength: the dimensionless band used by G3.</summary>
        public double Factor;
        public bool Meter;
    }

    private sealed class QualificationMeasure
    {
        public double MsMedian = double.NaN;
        public long AllocBytes;
        public int Reps;
    }

    private sealed class QualificationOrder
    {
        public double Observed = double.NaN;
        public int Points;
        public string Note = "";
    }

    /// <summary>
    /// Resume state: problems already finished in a previous run of the same folder. Their CSV
    /// lines are reused verbatim; the sweep, the oracle rebuild and the determinism probe are
    /// not executed for them.
    /// </summary>
    private sealed class QualificationResumeContext
    {
        public string Dir = "";
        public HashSet<string> Completed = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> RunLines =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> MatrixLines =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public int ReusedCells;
        public int ReusedProblems;
    }

    private static QualificationResumeContext QualificationLoadResume(
        string dir, int methods, Dictionary<string, int> goalsPerProblem)
    {
        var resume = new QualificationResumeContext();
        resume.Dir = dir;
        List<QualificationMatrixEntry> matrix = QualificationReadMatrix(dir);
        if (matrix.Count == 0)
        {
            return resume;
        }

        Dictionary<string, int> matrixCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (QualificationMatrixEntry e in matrix)
        {
            int n;
            matrixCount.TryGetValue(e.Problem, out n);
            matrixCount[e.Problem] = n + 1;
        }

        foreach (KeyValuePair<string, int> pair in matrixCount)
        {
            int goals;
            if (goalsPerProblem.TryGetValue(pair.Key, out goals)
                && pair.Value == methods * goals)
            {
                resume.Completed.Add(pair.Key);
            }
        }

        List<string[]> matrixRows = QualificationReadCsv(Path.Combine(dir, "matrix.csv"));
        for (int i = 1; i < matrixRows.Count; i++)
        {
            string[] f = matrixRows[i];
            if (f.Length < 3)
            {
                continue;
            }

            if (!resume.Completed.Contains(f[0]))
            {
                continue;
            }

            List<string> list;
            if (!resume.MatrixLines.TryGetValue(f[0], out list))
            {
                list = new List<string>();
                resume.MatrixLines[f[0]] = list;
            }

            list.Add(QualificationJoinCsv(f));
        }

        List<string[]> runRows = QualificationReadCsv(Path.Combine(dir, "runs.csv"));
        for (int i = 1; i < runRows.Count; i++)
        {
            string[] f = runRows[i];
            if (f.Length < 3 || !resume.Completed.Contains(f[0]))
            {
                continue;
            }

            List<string> list;
            if (!resume.RunLines.TryGetValue(f[0], out list))
            {
                list = new List<string>();
                resume.RunLines[f[0]] = list;
            }

            list.Add(QualificationJoinCsv(f));
        }

        foreach (string id in resume.Completed)
        {
            List<string> list;
            if (resume.MatrixLines.TryGetValue(id, out list))
            {
                resume.ReusedCells += list.Count;
            }
        }

        resume.ReusedProblems = resume.Completed.Count;
        return resume;
    }

    /// <summary>Re-quotes a parsed CSV row so it can be written back unchanged.</summary>
    private static string QualificationJoinCsv(string[] fields)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(QualificationCsvField(fields[i]));
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------
    // Entry
    // ---------------------------------------------------------------------------------

    private static void QualificationRunCatalogue(
        List<QualificationProblem> catalog, QualificationProfile profile, TextWriter log,
        string dir, QualificationResumeContext resume)
    {
        QualificationPrepareKeplerPins();
        List<IStudyIntegrator> methods = StudyRegistry.All();
        QualificationCheckSupportFlags(catalog, methods, log);

        var runRows = new List<string>();
        var matrixRows = new List<string>();

        foreach (QualificationProblem q in catalog)
        {
            if (q.Status != "VALID")
            {
                log.WriteLine(q.Id + ": skipped (oracle " + q.Status + ")");
                continue;
            }

            var problemWatch = System.Diagnostics.Stopwatch.StartNew();

            if (resume != null && resume.Completed.Contains(q.Id))
            {
                List<string> oldRuns;
                if (resume.RunLines.TryGetValue(q.Id, out oldRuns))
                {
                    runRows.AddRange(oldRuns);
                }

                List<string> oldMatrix;
                if (resume.MatrixLines.TryGetValue(q.Id, out oldMatrix))
                {
                    matrixRows.AddRange(oldMatrix);
                }

                log.WriteLine(q.Id + ": reused "
                    + (oldMatrix == null ? 0 : oldMatrix.Count).ToString(QualificationInv)
                    + " cells from " + Path.GetFileName(resume.Dir));
                QualificationWriteCsv(Path.Combine(dir, "runs.csv"), QualificationRunsHeader, runRows);
                QualificationWriteCsv(Path.Combine(dir, "matrix.csv"), QualificationMatrixHeader, matrixRows);
                continue;
            }

            StudyProblem p = q.Problem;
            if (double.IsNaN(p.Floor) && double.IsFinite(q.Floor))
            {
                // The audit normally copies the floor into the problem; on resume it is skipped
                // and the floor comes from oracle_audit.md instead.
                p.Floor = q.Floor;
            }

            var options = new StudyOptions();
            options.RelTargets = new double[0];
            List<QualificationGoal> goals = QualificationGoalsOf(q, p, profile);
            var targets = new List<double>();

            // The coarse end of the sweep must stay where the A-level sweep of the baseline
            // put it: adding a coarser metre goal would change `maxTarget`, start the sweep
            // at a finer control and move every A1 price. The sweep targets are therefore:
            //   * analytic levels: A1-A3 exactly as before (baseline behaviour);
            //   * plus the single finest metre goal, if it is finer than the finest A level
            //     (so a metre goal below the A grid is actually reachable);
            //   * no analytic levels (S/P/F): only the coarsest and the finest metre goal —
            //     the middle goals are evaluated from the executed rows.
            var evaluatedGoals = new List<QualificationGoal>();
            foreach (QualificationGoal goal in goals)
            {
                double threshold = QualificationGoalThreshold(goal, p);
                if (double.IsFinite(p.Floor) && p.Floor > 0.1d * threshold)
                {
                    continue;
                }

                if (threshold > 0d && double.IsFinite(threshold))
                {
                    evaluatedGoals.Add(goal);
                }
            }

            QualificationGoal coarsestGoal = null;
            QualificationGoal finestGoal = null;
            double coarsestThreshold = double.NaN;
            double finestThreshold = double.NaN;
            double finestAnalytic = double.NaN;
            foreach (QualificationGoal goal in evaluatedGoals)
            {
                double threshold = QualificationGoalThreshold(goal, p);
                if (double.IsNaN(coarsestThreshold) || threshold > coarsestThreshold)
                {
                    coarsestThreshold = threshold;
                    coarsestGoal = goal;
                }

                if (double.IsNaN(finestThreshold) || threshold < finestThreshold)
                {
                    finestThreshold = threshold;
                    finestGoal = goal;
                }

                if (!goal.Meter && (double.IsNaN(finestAnalytic) || threshold < finestAnalytic))
                {
                    finestAnalytic = threshold;
                }
            }

            if (q.AnalyticLevels)
            {
                foreach (QualificationGoal goal in evaluatedGoals)
                {
                    if (!goal.Meter)
                    {
                        targets.Add(QualificationGoalThreshold(goal, p));
                    }
                }

                if (finestGoal != null && finestGoal.Meter
                    && (double.IsNaN(finestAnalytic) || finestGoal.Threshold < finestAnalytic))
                {
                    targets.Add(finestGoal.Threshold);
                }
            }
            else if (coarsestGoal != null && finestGoal != null)
            {
                targets.Add(coarsestThreshold);
                targets.Add(finestThreshold);
            }

            targets.Sort();
            options.AbsTargets = targets.ToArray();
            options.AdaptiveMaxSteps = new[] { double.PositiveInfinity };
            // Owner decision 2026-11-11: smoke keeps 2e6 (minutes), full gets 10e6 so that
            // RK4 L2 A2 (~4e6 calls) is no longer censored. G7 still turns a real budget hit
            // into SKIP, not FAIL.
            options.MaxCostRhs = profile == QualificationProfile.Full ? 10000000L : 2000000L;
            options.RefineTies = profile == QualificationProfile.Full;
            options.TimingReps = 1;

            log.WriteLine(q.Id + " " + q.Title + " (" + p.Label + ")");
            long sweepStart = problemWatch.ElapsedMilliseconds;
            StudyScenarioResult result = StudySweepRunner.Run(p, methods, options, log);
            long sweepMs = problemWatch.ElapsedMilliseconds - sweepStart;
            long cellsStart = problemWatch.ElapsedMilliseconds;

            var orders = new QualificationOrder[methods.Count];
            var mineByMethod = new List<List<StudyRow>>(methods.Count);
            var detByMethod = new bool[methods.Count];
            var detNoteByMethod = new string[methods.Count];
            var cellsForProblem = new List<QualificationCell>();
            for (int m = 0; m < methods.Count; m++)
            {
                if (methods[m].Kind == StudyKind.Fixed)
                {
                    QualificationOrderRefine(p, methods[m], result.Rows, options, log);
                    StudyReport.FinalizeCosts(result.Rows, result.KeplerWeight);
                }

                orders[m] = QualificationObservedOrder(p, result.Rows, methods[m], options);
                log.WriteLine("order " + methods[m].Name + " " + q.Id + ": p="
                    + QualificationNumber(orders[m].Observed)
                    + " points=" + orders[m].Points.ToString(QualificationInv)
                    + " note=" + (orders[m].Note.Length == 0 ? "ok" : orders[m].Note));
                List<StudyRow> mine = QualificationRowsOf(result.Rows, methods[m].Name);
                mineByMethod.Add(mine);
                detByMethod[m] = QualificationDeterminism(
                    p, methods[m], mine, options, QualificationLevelFactors[0] * p.ScaleLength,
                    out detNoteByMethod[m]);

                for (int level = 0; level < goals.Count; level++)
                {
                    cellsForProblem.Add(QualificationCellFor(
                        q, p, methods[m], mine, goals[level], orders[m], detByMethod[m],
                        detNoteByMethod[m]));
                }
            }

            // G7: one sequential pass over the certificate controls (warm-up + median of 5/3
            // repeats, Test562's scheme). G4: forward-backward at the cheapest passing control.
            long measureStart = problemWatch.ElapsedMilliseconds;
            var measured = new Dictionary<string, QualificationMeasure>();
            var reversibility = new Dictionary<string, QualificationReversibility>();
            for (int m = 0; m < methods.Count; m++)
            {
                IStudyIntegrator method = methods[m];
                double loosestAnalytic = double.NaN;
                double tightestMeter = double.NaN;
                foreach (QualificationCell c in cellsForProblem)
                {
                    if (c.Method != method.Name || c.Status != "PASS" || !(c.BestControl > 0d))
                    {
                        continue;
                    }

                    if (c.Meter)
                    {
                        if (double.IsNaN(tightestMeter) || c.BestControl < tightestMeter)
                        {
                            tightestMeter = c.BestControl;
                        }

                        continue;
                    }

                    string key = method.Name + "|" + c.BestControl.ToString("R", QualificationInv);
                    if (!measured.ContainsKey(key))
                    {
                        measured[key] = QualificationMeasureRun(p, method, c.BestControl, options.MaxCostRhs);
                    }

                    if (double.IsNaN(loosestAnalytic) || c.BestControl > loosestAnalytic)
                    {
                        loosestAnalytic = c.BestControl;
                    }
                }

                // Metre goals share the A-level controls; timing the tightest one keeps the
                // wall-time pass bounded (TASK.md A9: time is reported where measured).
                if (double.IsFinite(tightestMeter) && tightestMeter > 0d)
                {
                    string key = method.Name + "|" + tightestMeter.ToString("R", QualificationInv);
                    if (!measured.ContainsKey(key))
                    {
                        measured[key] = QualificationMeasureRun(p, method, tightestMeter, options.MaxCostRhs);
                    }
                }

                double revControl = !double.IsNaN(loosestAnalytic) ? loosestAnalytic : tightestMeter;
                if (double.IsFinite(revControl) && revControl > 0d)
                {
                    reversibility[method.Name] = QualificationReversibilityOf(q, p, method, revControl);
                }
            }

            for (int m = 0; m < methods.Count; m++)
            {
                List<StudyRow> mine = mineByMethod[m];
                for (int i = 0; i < mine.Count; i++)
                {
                    QualificationMeasure meas = null;
                    measured.TryGetValue(
                        methods[m].Name + "|" + mine[i].Control.ToString("R", QualificationInv), out meas);
                    QualificationReversibility rev = null;
                    reversibility.TryGetValue(methods[m].Name, out rev);
                    runRows.Add(QualificationRunRow(
                        q, p, methods[m], mine[i], orders[m], detByMethod[m], detNoteByMethod[m],
                        meas, rev));
                }
            }

            matrixRows.AddRange(QualificationMatrixRows(cellsForProblem));

            // Partial results survive an interrupted run: rewrite both CSVs after every problem.
            QualificationWriteCsv(Path.Combine(dir, "runs.csv"), QualificationRunsHeader, runRows);
            QualificationWriteCsv(Path.Combine(dir, "matrix.csv"), QualificationMatrixHeader, matrixRows);
            log.WriteLine("  -> " + matrixRows.Count.ToString(QualificationInv) + " matrix cells so far, "
                + q.Id + " took " + problemWatch.ElapsedMilliseconds.ToString(QualificationInv) + " ms"
                + " (sweep " + sweepMs.ToString(QualificationInv)
                + ", cells " + (measureStart - cellsStart).ToString(QualificationInv)
                + ", measure+rev " + (problemWatch.ElapsedMilliseconds - measureStart)
                    .ToString(QualificationInv) + ")");
        }

        QualificationLogMatrixSummary(dir, log);
    }

    private static void QualificationLogMatrixSummary(string dir, TextWriter log)
    {
        var counts = new Dictionary<string, int>();
        foreach (QualificationMatrixEntry entry in QualificationReadMatrix(dir))
        {
            int n;
            counts.TryGetValue(entry.Status, out n);
            counts[entry.Status] = n + 1;
        }

        var sb = new StringBuilder("matrix: ");
        foreach (KeyValuePair<string, int> pair in counts)
        {
            sb.Append(pair.Key).Append('=').Append(pair.Value.ToString(QualificationInv)).Append(' ');
        }

        log.WriteLine(sb.ToString().TrimEnd());
    }

    // ---------------------------------------------------------------------------------
    // Optional pins: keep the Kepler weight stable between runs and inside this folder
    // ---------------------------------------------------------------------------------

    private static void QualificationPrepareKeplerPins()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("P1B_KEPLER_WEIGHTS")))
        {
            return;
        }

        string path = Path.Combine(QualificationRoot(), "kepler_weights.csv");
        Environment.SetEnvironmentVariable("P1B_KEPLER_WEIGHTS", path);
    }

    // ---------------------------------------------------------------------------------
    // Section 8 rule 3: automatic flag consistency check
    // ---------------------------------------------------------------------------------

    private static void QualificationCheckSupportFlags(
        List<QualificationProblem> catalog, List<IStudyIntegrator> methods, TextWriter log)
    {
        // Which methods require ConservativeSecondOrder? Probe Supports on two problems that
        // differ only in that flag (with a central body, so the WH methods reach their second
        // condition). No method name is used anywhere.
        var conservativeRequired = new List<IStudyIntegrator>();
        foreach (IStudyIntegrator method in methods)
        {
            string why;
            bool onConservative = method.Supports(QualificationProbeProblem(true), out why);
            bool onNonConservative = method.Supports(QualificationProbeProblem(false), out why);
            if (onConservative && !onNonConservative)
            {
                conservativeRequired.Add(method);
            }
        }

        log.WriteLine("flag check: " + conservativeRequired.Count.ToString(QualificationInv)
            + " method(s) require a conservative second-order force");

        // A4 also names Encke: a method that needs a central body but does not require the
        // conservative flag. It is identified by behaviour (supports a non-conservative
        // central problem, refuses the same problem without a central body), not by name.
        var centralOnly = new List<IStudyIntegrator>();
        StudyProblem centralNonConservative = QualificationProbeProblem(false);
        StudyProblem noCentralNonConservative = QualificationProbeProblemNoCentral(false);
        foreach (IStudyIntegrator method in methods)
        {
            if (conservativeRequired.Contains(method))
            {
                continue;
            }

            string whyCentral;
            string whyNoCentral;
            if (method.Supports(centralNonConservative, out whyCentral)
                && !method.Supports(noCentralNonConservative, out whyNoCentral))
            {
                centralOnly.Add(method);
            }
        }

        log.WriteLine("A4 flag check: " + centralOnly.Count.ToString(QualificationInv)
            + " central-only method(s) that do not require a conservative force");

        int violations = 0;
        foreach (QualificationProblem q in catalog)
        {
            if (q.Status != "VALID")
            {
                continue;
            }

            StudyProblem p = q.Problem;
            bool massDependent;
            bool velocityDependent = QualificationForceDependsOnVelocity(p, out massDependent);
            bool nonConservativeForce = velocityDependent || massDependent;
            log.WriteLine("flag check " + q.Id + ": velocity-dependent=" + velocityDependent
                + " mass-dependent=" + massDependent
                + " ConservativeSecondOrder=" + p.ConservativeSecondOrder);

            if (nonConservativeForce && p.ConservativeSecondOrder)
            {
                log.WriteLine("  VIOLATION: force depends on velocity/mass but ConservativeSecondOrder is true");
                violations++;
            }

            if (!nonConservativeForce)
            {
                continue;
            }

            foreach (IStudyIntegrator method in conservativeRequired)
            {
                string why;
                if (method.Supports(p, out why))
                {
                    log.WriteLine("  VIOLATION: " + method.Name + " claims support on a non-conservative force");
                    violations++;
                }
                else if (string.IsNullOrEmpty(why))
                {
                    log.WriteLine("  VIOLATION: " + method.Name + " refused a non-conservative force without a reason");
                    violations++;
                }
            }

            foreach (IStudyIntegrator method in centralOnly)
            {
                string why;
                if (method.Supports(p, out why))
                {
                    // A4 names Encke explicitly and expects N/A here. The code's Supports says
                    // yes and its IAS15 core can integrate velocity/mass-dependent forces, so
                    // this is a documented expectation mismatch (FOUND.md F10, owner question
                    // 7), not a silent pass: the line is in every run log.
                    log.WriteLine("  A4 CHECK NOTE: " + method.Name + " claims support on " + q.Id
                        + " (non-conservative force); TASK.md A4 expects N/A - FOUND.md F10");
                }
            }
        }

        if (violations > 0)
        {
            throw new InvalidOperationException(
                "support-flag consistency check failed with " + violations.ToString(QualificationInv)
                + " violation(s); see run.log");
        }
    }

    private static StudyProblem QualificationProbeProblem(bool conservative)
    {
        return QualificationProbeProblemCore(conservative, 1d);
    }

    private static StudyProblem QualificationProbeProblemNoCentral(bool conservative)
    {
        return QualificationProbeProblemCore(conservative, 0d);
    }

    private static StudyProblem QualificationProbeProblemCore(bool conservative, double centralMu)
    {
        var p = new StudyProblem();
        p.Label = "Test564 support probe";
        p.Dim = 6;
        p.PosDim = 3;
        p.CentralMu = centralMu;
        p.ConservativeSecondOrder = conservative;
        p.T0 = 0d;
        p.Y0 = new double[6];
        p.Rhs = QualificationKeplerRhs(1d);
        p.CheckpointTimes = new[] { 1d };
        p.CheckpointRef = new[] { new double[6] };
        p.ScaleLength = 1d;
        p.CharTime = 1d;
        return p;
    }

    /// <summary>
    /// Numerical probe: does the acceleration change when only the velocities (or only the
    /// non-[x,v] extras, e.g. mass) change? Used to decide whether a problem's force is
    /// non-conservative without hardcoding any problem id. The probe runs at the initial
    /// time, mid-horizon and inside every breakpoint interval: a thrust arc that starts
    /// after T0 (S11/S17) has no force at all at T0, so a single t = T0 probe would call a
    /// thrust case mass-independent.
    /// </summary>
    private static bool QualificationForceDependsOnVelocity(StudyProblem p, out bool massDependent)
    {
        massDependent = false;
        if (p.Rhs == null || p.Y0 == null || p.Dim <= 0)
        {
            return false;
        }

        int pos = Math.Max(p.PosDim, 0);
        var y0 = p.Y0;
        bool velocity = false;
        foreach (double t in QualificationProbeTimes(p))
        {
            var dy0 = new double[p.Dim];
            p.Rhs(t, y0, dy0);

            var yv = (double[])y0.Clone();
            for (int i = 0; i < pos; i++)
            {
                yv[pos + i] = yv[pos + i] + (1e-3d * (Math.Abs(yv[pos + i]) + 1d));
            }

            var dyv = new double[p.Dim];
            p.Rhs(t, yv, dyv);
            // Second-order layout: dy[0..PosDim-1] is just v, so only the acceleration block
            // can reveal a velocity-dependent force. A first-order problem has no split.
            velocity |= pos > 0
                ? QualificationRhsChanged(dy0, dyv, pos, pos)
                : QualificationRhsChanged(dy0, dyv, 0, p.Dim);

            if (2 * pos < p.Dim)
            {
                var ym = (double[])y0.Clone();
                for (int i = 2 * pos; i < p.Dim; i++)
                {
                    ym[i] = ym[i] + (0.1d * (Math.Abs(ym[i]) + 1d));
                }

                var dym = new double[p.Dim];
                p.Rhs(t, ym, dym);
                massDependent |= pos > 0
                    ? QualificationRhsChanged(dy0, dym, pos, pos)
                    : QualificationRhsChanged(dy0, dym, 0, p.Dim);
            }
        }

        return velocity;
    }

    private static List<double> QualificationProbeTimes(StudyProblem p)
    {
        var times = new List<double>();
        times.Add(p.T0);
        if (p.Duration > 0d && double.IsFinite(p.Duration))
        {
            times.Add(p.T0 + (0.5d * p.Duration));
        }

        if (p.Breakpoints != null && p.Breakpoints.Length > 0)
        {
            var edges = new List<double>(p.Breakpoints);
            edges.Sort();
            foreach (double edge in edges)
            {
                if (edge > p.T0 && edge < p.EndTime)
                {
                    times.Add(edge + 1e-3d);
                }
            }

            for (int i = 0; i + 1 < edges.Count; i++)
            {
                double mid = 0.5d * (edges[i] + edges[i + 1]);
                if (mid > p.T0 && mid < p.EndTime)
                {
                    times.Add(mid);
                }
            }
        }

        return times;
    }

    private static bool QualificationRhsChanged(double[] a, double[] b, int start, int count)
    {
        // Relative to the largest acceleration in the block. The old absolute test
        // (1e-9 * (1 + |a|)) could not see the Sun-only 1PN term: its change under a
        // 0.1% velocity shift is ~4e-11 of the Newtonian acceleration.
        double scale = 0d;
        for (int i = start; i < start + count && i < a.Length; i++)
        {
            scale = Math.Max(scale, Math.Abs(a[i]));
            scale = Math.Max(scale, Math.Abs(b[i]));
        }

        double tol = Math.Max(1e-12d * scale, 1e-300d);
        for (int i = start; i < start + count && i < a.Length; i++)
        {
            if (Math.Abs(a[i] - b[i]) > tol)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------
    // Orders (G2)
    // ---------------------------------------------------------------------------------

    private static QualificationOrder QualificationObservedOrder(
        StudyProblem p, List<StudyRow> rows, IStudyIntegrator method, StudyOptions options)
    {
        var q = new QualificationOrder();
        double floor = double.IsNaN(p.Floor) ? 0d : p.Floor;
        double low = Math.Max(100d * floor, 1e-16d * p.ScaleLength);
        double high = 1e-2d * p.ScaleLength;

        var points = new List<StudyRow>();
        foreach (StudyRow r in rows)
        {
            if (r.Integrator != method.Name || r.Variant.Length != 0)
            {
                continue;
            }

            if (r.Status != "ok" || !r.Finite || !(r.Err > 0d) || !(r.Control > 0d))
            {
                continue;
            }

            // Points at or below the run's own round-off floor measure summation noise, not
            // order (same rule the report uses for FLOOR cells). Including them turns a clean
            // slope into a plateau artefact.
            double roundoff = StudyReport.RoundoffFloor(p, r);
            if (double.IsFinite(roundoff) && r.Err <= roundoff)
            {
                continue;
            }

            if (r.Err >= low && r.Err <= high)
            {
                points.Add(r);
            }
        }

        q.Points = points.Count;
        if (points.Count < 4)
        {
            q.Note = "n/a (few points)";
            return q;
        }

        // Fine -> coarse. The asymptotic branch is the small-step end: from there, a coarser
        // step must increase the error. Walking from the coarse end instead started inside the
        // pre-asymptotic branch, where the error falls faster than h^p, and inflated the slope
        // (RK4 on K8 read 6.33 instead of ~3.9). The window, the 0.5 tolerance and the >=4
        // points rule are unchanged.
        points.Sort(delegate (StudyRow a, StudyRow b) { return a.Control.CompareTo(b.Control); });

        // Start at the finest point in the window and walk towards coarser steps: the error
        // must at least double at every step. The first violation ends the branch (a noisy
        // point near the round-off floor gives "n/a", not a fabricated slope).
        var fit = new List<StudyRow>();
        fit.Add(points[0]);
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].Err >= 2d * fit[fit.Count - 1].Err)
            {
                fit.Add(points[i]);
            }
            else
            {
                break;
            }
        }

        q.Points = fit.Count;
        if (fit.Count < 4)
        {
            q.Note = "n/a (few points in the asymptotic branch)";
            return q;
        }

        var slopes = new List<double>();
        for (int i = 0; i + 1 < fit.Count; i++)
        {
            double num = Math.Log(fit[i + 1].Err / fit[i].Err);
            double den = Math.Log(fit[i + 1].Control / fit[i].Control);
            if (den != 0d && double.IsFinite(num / den))
            {
                slopes.Add(num / den);
            }
        }

        if (slopes.Count == 0)
        {
            q.Note = "n/a (degenerate grid)";
            return q;
        }

        slopes.Sort();
        double median = slopes[slopes.Count / 2];

        // G2 is an order check, not a budget or reference-floor check (owner decision).
        // 1) A censored row finer than the branch means the asymptotic part was never run:
        //    that is a G7 budget SKIP, not an order FAIL.
        foreach (StudyRow r in rows)
        {
            if (r.Integrator != method.Name || r.Variant.Length != 0)
            {
                continue;
            }

            if (r.Status == "censored" && r.Control > 0d && r.Control < fit[0].Control)
            {
                q.Points = fit.Count;
                q.Note = "n/a (budget before the asymptotic branch)";
                return q;
            }
        }

        // 2) Floor screening: when the branch start is within 10x of the window edge, the frozen
        //    window may have cut a transition. This is a guard against a FALSE FAIL only: if the
        //    branch already passes (or G2 does not apply), the measured order is kept. n/a needs
        //    evidence: a trusted ok point below the window whose slope disagrees with the branch.
        //    If it continues the branch (RK4 R2: 4.94 vs 4.97), the branch is certifiable and the
        //    gate applies (FAIL with the number recorded in FOUND.md).
        bool gateApplies = method.Kind == StudyKind.Fixed && !double.IsNaN(method.ExpectedOrder);
        bool wouldFail = gateApplies && Math.Abs(median - method.ExpectedOrder) > 0.5d;
        if (wouldFail && fit[0].Err <= 10d * low)
        {
            StudyRow sub = null;
            foreach (StudyRow r in rows)
            {
                if (r.Integrator != method.Name || r.Variant.Length != 0)
                {
                    continue;
                }

                if (r.Status != "ok" || !r.Finite || !(r.Err > 0d) || !(r.Control > 0d)
                    || !(r.Control < fit[0].Control))
                {
                    continue;
                }

                if (sub == null || r.Control > sub.Control)
                {
                    sub = r;
                }
            }

            if (sub != null)
            {
                double subSlope = Math.Log(fit[0].Err / sub.Err)
                    / Math.Log(fit[0].Control / sub.Control);
                if (double.IsFinite(subSlope)
                    && Math.Abs(subSlope - median) > 0.25d * Math.Max(1d, Math.Abs(median)))
                {
                    q.Points = fit.Count;
                    q.Note = "n/a (floor-limited window: sub-window slope "
                        + subSlope.ToString("F2", QualificationInv) + " vs branch "
                        + median.ToString("F2", QualificationInv) + ")";
                    return q;
                }
            }
        }

        q.Observed = median;
        q.Note = "median of " + slopes.Count.ToString(QualificationInv)
            + " pairwise slopes (fine->coarse, doubled-error branch)";
        return q;
    }

    // ---------------------------------------------------------------------------------
    // Determinism (G6)
    // ---------------------------------------------------------------------------------

    private static bool QualificationDeterminism(
        StudyProblem p, IStudyIntegrator method, List<StudyRow> rows, StudyOptions options,
        double looseThreshold, out string note)
    {
        note = "";
        StudyRow best = null;
        foreach (StudyRow r in rows)
        {
            if (r.Integrator != method.Name || r.Variant.Length != 0)
            {
                continue;
            }

            if (r.Status != "ok" || !r.Finite || !(r.Err <= looseThreshold))
            {
                continue;
            }

            if (best == null || r.Cost < best.Cost)
            {
                best = r;
            }
        }

        if (best == null)
        {
            // No level was reached: exercise the finest finite point instead.
            foreach (StudyRow r in rows)
            {
                if (r.Integrator != method.Name || r.Variant.Length != 0 || r.Status != "ok" || !r.Finite)
                {
                    continue;
                }

                if (best == null || r.Err < best.Err)
                {
                    best = r;
                }
            }
        }

        if (best == null)
        {
            return true; // nothing finite to compare; G5 already reports the failure
        }

        try
        {
            StudyConfig cfg = method.MakeConfig(p, best.Control, double.PositiveInfinity);
            var ctx1 = new StudyContext(p, options.MaxCostRhs);
            StudyRun run1 = method.Run(p, cfg, ctx1);
            var ctx2 = new StudyContext(p, options.MaxCostRhs);
            StudyRun run2 = method.Run(p, cfg, ctx2);
            bool same = run1.Final != null && run2.Final != null
                && run1.Final.Length == run2.Final.Length;
            if (same)
            {
                for (int i = 0; i < run1.Final.Length; i++)
                {
                    if (BitConverter.DoubleToInt64Bits(run1.Final[i]) != BitConverter.DoubleToInt64Bits(run2.Final[i]))
                    {
                        same = false;
                        break;
                    }
                }
            }

            note = same ? "bit-identical repeat" : "repeat differs";
            return same;
        }
        catch (Exception ex)
        {
            note = "repeat failed: " + ex.GetType().Name;
            return false;
        }
    }

    // ---------------------------------------------------------------------------------
    // Status mapping
    // ---------------------------------------------------------------------------------

    private static QualificationCell QualificationCellFor(
        QualificationProblem q, StudyProblem p, IStudyIntegrator method, List<StudyRow> rows,
        QualificationGoal goal, QualificationOrder order, bool deterministic, string detNote)
    {
        var cell = new QualificationCell();
        cell.ProblemId = q.Id;
        cell.Method = method.Name;
        cell.Level = goal.Name;
        cell.Meter = goal.Meter;

        double threshold = QualificationGoalThreshold(goal, p);
        string why;
        if (!method.Supports(p, out why))
        {
            cell.Status = "N/A";
            cell.Reason = why;
            return cell;
        }

        if (double.IsFinite(p.Floor) && p.Floor > 0.1d * threshold)
        {
            cell.Status = "SKIP";
            cell.Reason = "oracle floor " + QualificationFormat(p.Floor) + " > 0.1*" + QualificationFormat(threshold);
            return cell;
        }

        bool anyError = false;
        bool anyCensored = false;
        bool anyNonFinite = false;
        bool anyFinite = false;
        string nonFiniteNote = "";
        string errorNote = "";
        StudyRow best = null;
        foreach (StudyRow r in rows)
        {
            if (r.Integrator != method.Name || r.Variant.Length != 0)
            {
                continue;
            }

            if (r.Status == "error")
            {
                anyError = true;
                errorNote = r.Note;
                continue;
            }

            if (r.Status == "censored")
            {
                anyCensored = true;
                continue;
            }

            if (r.Status == "ok" && !r.Finite)
            {
                // G5 is applied to the runs that certify the level (owner decision):
                // an abort on a coarse control that the level does not need is recorded in
                // runs.csv but does not fail the cell.
                anyNonFinite = true;
                nonFiniteNote = r.Note;
                continue;
            }

            if (r.Status != "ok" || !r.Finite)
            {
                continue;
            }

            anyFinite = true;
            if (!(r.Err <= threshold))
            {
                continue;
            }

            if (best == null || r.Cost < best.Cost)
            {
                best = r;
            }
        }

        if (anyError)
        {
            cell.Status = "ERROR";
            cell.Reason = errorNote;
            return cell;
        }

        if (best != null)
        {
            cell.BestControl = best.Control;
            cell.Price = best.Cost;
            cell.Err = best.Err;
            cell.Best = best;

            string gate = QualificationGateFailure(
                method, order, deterministic, detNote, p, best, goal.Factor);
            bool failed = gate.Length > 0
                && !gate.StartsWith("G3 skipped", StringComparison.Ordinal);
            cell.Status = failed ? "FAIL" : "PASS";
            cell.Reason = gate;
            return cell;
        }

        if (anyNonFinite && !anyFinite)
        {
            // No finite run at all: that is a genuine finiteness failure for this level.
            cell.Status = "FAIL";
            cell.Reason = "G5 finiteness: " + nonFiniteNote;
            return cell;
        }

        if (anyCensored && QualificationAllFiniteRowsAbove(rows, method, threshold))
        {
            cell.Status = "SKIP";
            cell.Reason = "RHS budget reached before the level";
            return cell;
        }

        cell.Status = "FAIL";
        cell.Reason = "level not reached at the finest control";
        return cell;
    }

    private static bool QualificationAllFiniteRowsAbove(
        List<StudyRow> rows, IStudyIntegrator method, double threshold)
    {
        bool any = false;
        foreach (StudyRow r in rows)
        {
            if (r.Integrator != method.Name || r.Variant.Length != 0)
            {
                continue;
            }

            if (r.Status == "ok" && r.Finite)
            {
                any = true;
                if (r.Err <= threshold)
                {
                    return false;
                }
            }
        }

        return any;
    }

    private static string QualificationGateFailure(
        IStudyIntegrator method, QualificationOrder order, bool deterministic, string detNote,
        StudyProblem p, StudyRow best, double levelFactor)
    {
        // G2: fixed-step only, and only when the method declares an order and the grid has
        // at least 4 points in the window (fewer points is "order n/a", not a failure).
        if (method.Kind == StudyKind.Fixed && !double.IsNaN(method.ExpectedOrder)
            && order.Points >= 4 && double.IsFinite(order.Observed))
        {
            if (Math.Abs(order.Observed - method.ExpectedOrder) > 0.5d)
            {
                return "G2 order: observed " + order.Observed.ToString("F2", QualificationInv)
                    + " vs expected " + method.ExpectedOrder.ToString("F2", QualificationInv);
            }
        }

        if (!deterministic)
        {
            return "G6 determinism: " + detNote;
        }

        // G3: the drift is dimensionless, so it is compared with the dimensionless level
        // factor (1e-6 / 1e-9 / 1e-12), NOT with factor * ScaleLength (metres). A model whose
        // reference itself drifts more than the band cannot be gated on that invariant.
        var notes = new StringBuilder();
        if (p.EnergyFn != null && double.IsFinite(best.EnergyDriftFromInitial))
        {
            double e0 = p.EnergyFn(p.Y0);
            double eScale = Math.Max(Math.Abs(e0), QualificationEnergyFloor(p));
            double band = 10d * levelFactor;
            double refRel = double.IsFinite(best.EnergyDriftRef) && eScale > 0d
                ? best.EnergyDriftRef / eScale
                : double.NaN;
            if (double.IsFinite(refRel) && refRel > band)
            {
                notes.Append("G3 skipped: model drift (energy ")
                    .Append(QualificationFormat(refRel)).Append(" > ")
                    .Append(QualificationFormat(band)).Append("); ");
            }
            else
            {
                double rel = best.EnergyDriftFromInitial / eScale;
                if (double.IsFinite(rel) && rel > band)
                {
                    return "G3 energy drift " + QualificationFormat(rel)
                        + " > 10*" + QualificationFormat(levelFactor);
                }
            }
        }

        if (p.AngularMomentumFn != null && double.IsFinite(best.AngularDriftFromInitial))
        {
            double l0 = p.AngularMomentumFn(p.Y0);
            double lScale = Math.Max(Math.Abs(l0), QualificationAngularFloor(p));
            double band = 10d * levelFactor;
            double refRel = double.IsFinite(best.AngularDriftRef) && lScale > 0d
                ? best.AngularDriftRef / lScale
                : double.NaN;
            if (double.IsFinite(refRel) && refRel > band)
            {
                notes.Append("G3 skipped: model drift (angular ")
                    .Append(QualificationFormat(refRel)).Append(" > ")
                    .Append(QualificationFormat(band)).Append("); ");
            }
            else
            {
                double rel = best.AngularDriftFromInitial / lScale;
                if (double.IsFinite(rel) && rel > band)
                {
                    return "G3 angular momentum drift " + QualificationFormat(rel)
                        + " > 10*" + QualificationFormat(levelFactor);
                }
            }
        }

        return notes.ToString().TrimEnd(' ', ';');
    }

    /// <summary>
    /// Energy scale for the relative drift when E0 is near zero (a parabolic orbit has E = 0
    /// exactly, so |dE/E| would be 0/0).
    /// </summary>
    private static double QualificationEnergyFloor(StudyProblem p)
    {
        if (p.CentralMu > 0d && p.ScaleLength > 0d)
        {
            return 0.5d * p.CentralMu / p.ScaleLength;
        }

        return 1e-300d;
    }

    /// <summary>Angular-momentum scale: sqrt(mu*a) for a central field, else |L0| only.</summary>
    private static double QualificationAngularFloor(StudyProblem p)
    {
        if (p.CentralMu > 0d && p.ScaleLength > 0d)
        {
            return Math.Sqrt(p.CentralMu * p.ScaleLength);
        }

        return 1e-300d;
    }

    // ---------------------------------------------------------------------------------
    // G7 (timing) and G4 (reversibility)
    // ---------------------------------------------------------------------------------

    private sealed class QualificationReversibility
    {
        public double Control = double.NaN;
        public double RevErr = double.NaN;
        public double ComRel = double.NaN;
    }

    /// <summary>
    /// Sequential timing pass: one warm-up, then 5 repeats (3 when a single run exceeds
    /// 250 ms), median of the wall time, median allocation per run. This is Test562's scheme
    /// (warm-up, GC.Collect before each repeat, median), applied to the certificate control.
    /// </summary>
    private static QualificationMeasure QualificationMeasureRun(
        StudyProblem p, IStudyIntegrator method, double control, long budget)
    {
        var m = new QualificationMeasure();
        StudyConfig cfg = method.MakeConfig(p, control, double.PositiveInfinity);
        method.Run(p, cfg, new StudyContext(p, budget));

        // TASK.md section 9: median of at least 5 repeats, always. No short-circuit to 3.
        const int reps = 5;
        var times = new List<double>(reps);
        var allocs = new List<long>(reps);
        for (int k = 0; k < reps; k++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            method.Run(p, cfg, new StudyContext(p, budget));
            sw.Stop();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            times.Add(sw.Elapsed.TotalMilliseconds);
            allocs.Add(a1 - a0);
        }

        times.Sort();
        allocs.Sort();
        m.MsMedian = times[times.Count / 2];
        m.AllocBytes = allocs[allocs.Count / 2];
        m.Reps = reps;
        return m;
    }

    /// <summary>
    /// G4: forward to the horizon, then backward with the negated right-hand side. The
    /// catalogue is autonomous, so t is not used by any Rhs. Also records the worst COM drift.
    /// Informational only: adaptive methods are not required to be reversible.
    /// </summary>
    private static QualificationReversibility QualificationReversibilityOf(
        QualificationProblem q, StudyProblem p, IStudyIntegrator method, double control)
    {
        var r = new QualificationReversibility();
        r.Control = control;
        StudyConfig cfg = method.MakeConfig(p, control, double.PositiveInfinity);
        StudyRun forward;
        try
        {
            forward = method.Run(p, cfg, new StudyContext(p, 20000000L));
        }
        catch (Exception)
        {
            return r;
        }

        if (forward.Aborted || forward.Samples == null)
        {
            return r;
        }

        if (q.ComFn != null)
        {
            double[] com0 = q.ComFn(p.Y0);
            double worst = 0d;
            for (int k = 0; k < forward.Samples.Length; k++)
            {
                double[] s = forward.Samples[k];
                if (s == null)
                {
                    break;
                }

                double[] com = q.ComFn(s);
                for (int i = 0; i < 3; i++)
                {
                    worst = Math.Max(worst, Math.Abs(com[i] - com0[i]));
                }
            }

            r.ComRel = worst / Math.Max(p.ScaleLength, 1e-300d);
        }

        try
        {
            StudyProblem back = p.ShallowClone();
            back.T0 = 0d;
            // Time reversal: X(0) = x(T), V(0) = -v(T), and X' = V, V' = -a(X).
            var backY = (double[])forward.Final.Clone();
            for (int i = p.PosDim; i < 2 * p.PosDim && i < backY.Length; i++)
            {
                backY[i] = -backY[i];
            }

            back.Y0 = backY;
            back.Breakpoints = null;
            back.DomainViolation = null;
            back.CheckpointTimes = new[] { p.EndTime - p.T0 };
            back.CheckpointRef = new[] { (double[])p.Y0.Clone() };
            // Time reversal: X(0) = x(T), V(0) = -v(T), and X' = V, V' = -a(X, T - tau).
            // For an autonomous second-order system the two sign flips cancel and the same
            // RHS works. For a time-dependent force (the Jupiter rail of P6, baked ephemeris
            // of the ship problems) the reverse run must evaluate the force at the MIRRORED
            // time T - tau; using t = tau directly made rev_err ~1e8 m on P6 (all methods),
            // which measured the ephemeris, not the method.
            double reverseEnd = p.EndTime;
            StudyProblem original = p;
            back.Rhs = delegate (double tau, double[] yy, double[] dy)
            {
                original.Rhs(reverseEnd - tau, yy, dy);
            };
            // A velocity/mass-dependent force is not reversible by construction, which is
            // exactly what rev_err should show (G4 is informational).
            StudyRun reverse = method.Run(back, cfg, new StudyContext(back, 20000000L));
            if (reverse.Final != null && !reverse.Aborted)
            {
                // The reversed run ends at (x0, -v0); compare against that same convention.
                var target = (double[])p.Y0.Clone();
                for (int i = p.PosDim; i < 2 * p.PosDim && i < target.Length; i++)
                {
                    target[i] = -target[i];
                }

                r.RevErr = p.Error(reverse.Final, target);
            }
        }
        catch (Exception)
        {
            r.RevErr = double.NaN;
        }

        return r;
    }

    // ---------------------------------------------------------------------------------
    // CSV
    // ---------------------------------------------------------------------------------

    private static List<StudyRow> QualificationRowsOf(List<StudyRow> rows, string method)
    {
        var list = new List<StudyRow>();
        foreach (StudyRow r in rows)
        {
            if (r.Integrator == method && r.Variant.Length == 0)
            {
                list.Add(r);
            }
        }

        return list;
    }

    private static string QualificationRunRow(
        QualificationProblem q, StudyProblem p, IStudyIntegrator method, StudyRow row,
        QualificationOrder order, bool deterministic, string detNote,
        QualificationMeasure meas, QualificationReversibility rev)
    {
        string status = row.Status;
        string reason = row.Note ?? "";
        if (row.Status == "ok" && !row.Finite)
        {
            status = "nonfinite";
        }

        double eScale = double.NaN;
        if (p.EnergyFn != null)
        {
            eScale = Math.Max(Math.Abs(p.EnergyFn(p.Y0)), QualificationEnergyFloor(p));
        }

        double lScale = double.NaN;
        if (p.AngularMomentumFn != null)
        {
            lScale = Math.Max(Math.Abs(p.AngularMomentumFn(p.Y0)), QualificationAngularFloor(p));
        }

        bool sameControl = rev != null && rev.Control > 0d
            && Math.Abs(rev.Control - row.Control) <= 1e-9d * Math.Max(rev.Control, Math.Abs(row.Control));

        var sb = new StringBuilder();
        sb.Append(QualificationCsvField(q.Id)).Append(',');
        sb.Append(QualificationCsvField(method.Name)).Append(',');
        sb.Append(QualificationCsvField(method.Family)).Append(',');
        sb.Append(method.Kind == StudyKind.Fixed ? "fixed" : "adaptive").Append(',');
        sb.Append(QualificationCsvNumber(row.Control)).Append(',');
        sb.Append(QualificationCsvField(status)).Append(',');
        sb.Append(QualificationCsvField(reason)).Append(',');
        sb.Append(QualificationCsvNumber(row.Err)).Append(',');
        sb.Append(QualificationCsvNumber(row.ErrFinal)).Append(',');
        sb.Append(QualificationCsvNumber(order.Points >= 4 ? order.Observed : double.NaN)).Append(',');
        sb.Append(QualificationCsvNumber(method.ExpectedOrder)).Append(',');
        sb.Append(QualificationCsvNumber(double.IsFinite(eScale) && double.IsFinite(row.EnergyDriftFromInitial)
            ? row.EnergyDriftFromInitial / eScale
            : double.NaN)).Append(',');
        sb.Append(QualificationCsvNumber(double.IsFinite(lScale) && double.IsFinite(row.AngularDriftFromInitial)
            ? row.AngularDriftFromInitial / lScale
            : double.NaN)).Append(',');
        sb.Append(QualificationCsvNumber(sameControl ? rev.ComRel : double.NaN)).Append(',');
        sb.Append(QualificationCsvNumber(sameControl ? rev.RevErr : double.NaN)).Append(',');
        sb.Append(row.Steps.ToString(QualificationInv)).Append(',');
        sb.Append(row.Rejected.ToString(QualificationInv)).Append(',');
        sb.Append(row.Calls.ToString(QualificationInv)).Append(',');
        sb.Append(QualificationCsvNumber(row.Cost)).Append(',');
        sb.Append(QualificationCsvNumber(meas != null ? meas.MsMedian : double.NaN)).Append(',');
        sb.Append(meas != null && meas.AllocBytes > 0 ? meas.AllocBytes.ToString(QualificationInv) : "").Append(',');
        sb.Append(deterministic ? "1" : "0").Append(',');
        sb.Append(QualificationCsvField(q.OracleId)).Append(',');
        sb.Append(QualificationCsvNumber(q.Floor));
        return sb.ToString();
    }

    private static List<string> QualificationMatrixRows(List<QualificationCell> cells)
    {
        var rows = new List<string>(cells.Count);
        foreach (QualificationCell c in cells)
        {
            var sb = new StringBuilder();
            sb.Append(QualificationCsvField(c.ProblemId)).Append(',');
            sb.Append(QualificationCsvField(c.Method)).Append(',');
            sb.Append(QualificationCsvField(c.Level)).Append(',');
            sb.Append(QualificationCsvField(c.Status)).Append(',');
            sb.Append(QualificationCsvField(c.Reason)).Append(',');
            sb.Append(QualificationCsvNumber(c.BestControl)).Append(',');
            sb.Append(QualificationCsvNumber(c.Price));
            rows.Add(sb.ToString());
        }

        return rows;
    }

    private static void QualificationWriteCsv(string path, string header, List<string> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(header);
        foreach (string row in rows)
        {
            sb.AppendLine(row);
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string QualificationCsvField(string value)
    {
        if (value == null)
        {
            return "";
        }

        bool quote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0
            || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
        if (!quote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string QualificationCsvNumber(double value)
    {
        if (double.IsNaN(value))
        {
            return "";
        }

        if (double.IsInfinity(value))
        {
            return value > 0d ? "inf" : "-inf";
        }

        return value.ToString("E17", QualificationInv);
    }
}
