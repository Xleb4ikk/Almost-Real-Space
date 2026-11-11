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
        public StudyRow Best;
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

    // ---------------------------------------------------------------------------------
    // Entry
    // ---------------------------------------------------------------------------------

    private static void QualificationRunCatalogue(
        List<QualificationProblem> catalog, QualificationProfile profile, TextWriter log,
        string dir)
    {
        QualificationPrepareKeplerPins();
        List<IStudyIntegrator> methods = StudyRegistry.All();
        QualificationCheckSupportFlags(catalog, methods, log);

        var cells = new List<QualificationCell>();
        var rows = new List<string>();
        var runRows = new List<string>();

        foreach (QualificationProblem q in catalog)
        {
            if (q.Status != "VALID")
            {
                log.WriteLine(q.Id + ": skipped (oracle " + q.Status + ")");
                continue;
            }

            StudyProblem p = q.Problem;
            var options = new StudyOptions();
            options.RelTargets = new double[0];
            var targets = new double[QualificationLevelFactors.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                targets[i] = QualificationLevelFactors[i] * p.ScaleLength;
            }

            options.AbsTargets = targets;
            options.AdaptiveMaxSteps = new[] { double.PositiveInfinity };
            options.MaxCostRhs = 2000000L;
            options.RefineTies = profile == QualificationProfile.Full;
            options.TimingReps = 1;

            log.WriteLine(q.Id + " " + q.Title + " (" + p.Label + ")");
            StudyScenarioResult result = StudySweepRunner.Run(p, methods, options, log);

            var orders = new QualificationOrder[methods.Count];
            var mineByMethod = new List<List<StudyRow>>(methods.Count);
            var detByMethod = new bool[methods.Count];
            var detNoteByMethod = new string[methods.Count];
            var cellsForProblem = new List<QualificationCell>();
            for (int m = 0; m < methods.Count; m++)
            {
                orders[m] = QualificationObservedOrder(p, result.Rows, methods[m], options);
                List<StudyRow> mine = QualificationRowsOf(result.Rows, methods[m].Name);
                mineByMethod.Add(mine);
                detByMethod[m] = QualificationDeterminism(
                    p, methods[m], mine, options, QualificationLevelFactors[0] * p.ScaleLength,
                    out detNoteByMethod[m]);

                for (int level = 0; level < QualificationLevelFactors.Length; level++)
                {
                    cellsForProblem.Add(QualificationCellFor(
                        q, p, methods[m], mine, level, orders[m], detByMethod[m], detNoteByMethod[m]));
                }
            }

            // G7: one sequential pass over the certificate controls (warm-up + median of 5/3
            // repeats, Test562's scheme). G4: forward-backward at the cheapest passing control.
            var measured = new Dictionary<string, QualificationMeasure>();
            var reversibility = new Dictionary<string, QualificationReversibility>();
            for (int m = 0; m < methods.Count; m++)
            {
                IStudyIntegrator method = methods[m];
                double tightest = double.NaN;
                double loosest = double.NaN;
                foreach (QualificationCell c in cellsForProblem)
                {
                    if (c.Method != method.Name || c.Status != "PASS" || !(c.BestControl > 0d))
                    {
                        continue;
                    }

                    string key = method.Name + "|" + c.BestControl.ToString("R", QualificationInv);
                    if (!measured.ContainsKey(key))
                    {
                        measured[key] = QualificationMeasureRun(
                            p, method, c.BestControl, options.MaxCostRhs);
                    }

                    if (double.IsNaN(loosest) || c.BestControl > loosest)
                    {
                        loosest = c.BestControl;
                    }

                    if (double.IsNaN(tightest) || c.BestControl < tightest)
                    {
                        tightest = c.BestControl;
                    }
                }

                if (!double.IsNaN(loosest))
                {
                    reversibility[method.Name] = QualificationReversibilityOf(q, p, method, loosest);
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

            cells.AddRange(cellsForProblem);

            // Partial results survive an interrupted run: rewrite both CSVs after every problem.
            QualificationWriteCsv(Path.Combine(dir, "runs.csv"), QualificationRunsHeader, runRows);
            QualificationWriteCsv(Path.Combine(dir, "matrix.csv"), QualificationMatrixHeader,
                QualificationMatrixRows(cells));
            log.WriteLine("  -> " + cells.Count.ToString(QualificationInv) + " matrix cells so far");
        }

        QualificationLogMatrixSummary(cells, log);
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
        var p = new StudyProblem();
        p.Label = "Test564 support probe";
        p.Dim = 6;
        p.PosDim = 3;
        p.CentralMu = 1d;
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
    /// non-conservative without hardcoding any problem id.
    /// </summary>
    private static bool QualificationForceDependsOnVelocity(StudyProblem p, out bool massDependent)
    {
        massDependent = false;
        int pos = Math.Max(p.PosDim, 0);
        var y0 = p.Y0;
        var dy0 = new double[p.Dim];
        p.Rhs(p.T0, y0, dy0);

        var yv = (double[])y0.Clone();
        for (int i = 0; i < pos; i++)
        {
            yv[pos + i] = yv[pos + i] + (1e-3d * (Math.Abs(yv[pos + i]) + 1d));
        }

        var dyv = new double[p.Dim];
        p.Rhs(p.T0, yv, dyv);
        // Second-order layout: dy[0..PosDim-1] is just v, so only the acceleration block can
        // reveal a velocity-dependent force. A first-order problem has no split: compare all.
        bool velocity = pos > 0
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
            p.Rhs(p.T0, ym, dym);
            massDependent = pos > 0
                ? QualificationRhsChanged(dy0, dym, pos, pos)
                : QualificationRhsChanged(dy0, dym, 0, p.Dim);
        }

        return velocity;
    }

    private static bool QualificationRhsChanged(double[] a, double[] b, int start, int count)
    {
        for (int i = start; i < start + count && i < a.Length; i++)
        {
            double scale = 1d + Math.Abs(a[i]);
            if (Math.Abs(a[i] - b[i]) > 1e-9d * scale)
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

        points.Sort(delegate (StudyRow a, StudyRow b) { return a.Control.CompareTo(b.Control); });

        // Asymptotic prefix: a refinement must at least halve the error. A plateau (round-off
        // or a pre-asymptotic branch) is not part of the order estimate even when it sits
        // above the reference floor.
        var fit = new List<StudyRow>();
        fit.Add(points[0]);
        double bestErr = points[0].Err;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].Err <= 0.5d * bestErr)
            {
                fit.Add(points[i]);
                bestErr = points[i].Err;
            }
            else
            {
                break;
            }
        }

        q.Points = fit.Count;
        if (fit.Count < 4)
        {
            q.Note = "n/a (few points in the asymptotic prefix)";
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
        q.Observed = slopes[slopes.Count / 2];
        q.Note = "median of " + slopes.Count.ToString(QualificationInv) + " pairwise slopes";
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
        int level, QualificationOrder order, bool deterministic, string detNote)
    {
        var cell = new QualificationCell();
        cell.ProblemId = q.Id;
        cell.Method = method.Name;
        cell.Level = QualificationLevelNames[level];

        double threshold = QualificationLevelFactors[level] * p.ScaleLength;
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
                // G5: NaN/Inf or an aborted run anywhere in the grid is a violation.
                anyNonFinite = true;
                nonFiniteNote = r.Note;
                continue;
            }

            if (r.Status != "ok" || !r.Finite || !(r.Err <= threshold))
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

        if (anyNonFinite)
        {
            cell.Status = "FAIL";
            cell.Reason = "G5 finiteness: " + nonFiniteNote;
            return cell;
        }

        if (best != null)
        {
            cell.BestControl = best.Control;
            cell.Price = best.Cost;
            cell.Err = best.Err;
            cell.Best = best;

            string gate = QualificationGateFailure(method, order, deterministic, detNote, p, best, threshold);
            cell.Status = gate.Length == 0 ? "PASS" : "FAIL";
            cell.Reason = gate;
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
        StudyProblem p, StudyRow best, double threshold)
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

        // G3: invariants, only on conservative problems that expose them.
        if (p.EnergyFn != null && double.IsFinite(best.EnergyDriftFromInitial))
        {
            double e0 = p.EnergyFn(p.Y0);
            double eScale = Math.Max(Math.Abs(e0), QualificationEnergyFloor(p));
            double rel = best.EnergyDriftFromInitial / eScale;
            if (double.IsFinite(rel) && rel > 10d * threshold)
            {
                return "G3 energy drift " + QualificationFormat(rel) + " > 10*" + QualificationFormat(threshold);
            }
        }

        if (p.AngularMomentumFn != null && double.IsFinite(best.AngularDriftFromInitial))
        {
            double l0 = p.AngularMomentumFn(p.Y0);
            double lScale = Math.Max(Math.Abs(l0), QualificationAngularFloor(p));
            double rel = best.AngularDriftFromInitial / lScale;
            if (double.IsFinite(rel) && rel > 10d * threshold)
            {
                return "G3 angular momentum drift " + QualificationFormat(rel)
                    + " > 10*" + QualificationFormat(threshold);
            }
        }

        return "";
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

        var warm = System.Diagnostics.Stopwatch.StartNew();
        method.Run(p, cfg, new StudyContext(p, budget));
        warm.Stop();
        int reps = warm.Elapsed.TotalMilliseconds > 250d ? 3 : 5;

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
            // For an autonomous second-order system the time reverse is (x, -v) propagated
            // with the SAME right-hand side; the two sign flips (t and v) cancel for a(x).
            // A velocity-dependent force is not reversible by construction, which is exactly
            // what rev_err should show (G4 is informational).
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
            && Math.Abs(rev.Control - row.Control) <= 1e-12d * Math.Max(rev.Control, 1d);

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

    private static void QualificationLogMatrixSummary(List<QualificationCell> cells, TextWriter log)
    {
        var counts = new Dictionary<string, int>();
        foreach (QualificationCell c in cells)
        {
            int n;
            counts.TryGetValue(c.Status, out n);
            counts[c.Status] = n + 1;
        }

        var sb = new StringBuilder("matrix: ");
        foreach (KeyValuePair<string, int> pair in counts)
        {
            sb.Append(pair.Key).Append('=').Append(pair.Value.ToString(QualificationInv)).Append(' ');
        }

        log.WriteLine(sb.ToString().TrimEnd());
    }
}
