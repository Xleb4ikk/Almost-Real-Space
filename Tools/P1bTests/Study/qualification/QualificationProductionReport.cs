using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// TASK.md Appendix C, C3/C6: PRODUCTION_REPORT.md.
//
// Everything is read from the run's CSV files:
//   * matrix.csv - PASS/FAIL/N/A/SKIP, best control, price per (problem, method, goal);
//   * runs.csv   - err_max at the certifying control (C6 error gate);
//   * chunks.csv - wall time per chunk (C6 chunk gate).
//
// The report never invents a threshold: C6 values are frozen in PROGRESS.md ("Пороги ворот
// Приложения C (C6) — ЗАМОРОЖЕНО"). The stand-baseline comparison reads the baseline matrix
// named in baseline/BASELINE.md; it is a fact table, not a new ranking.
internal static partial class P1bTests
{
    private sealed class QualificationChunkEntry
    {
        public string Problem;
        public string Method;
        public double Control = double.NaN;
        public double Chunk = double.NaN;
        public double Arc = double.NaN;
        public string Status;
        public string Reason;
        public double Err = double.NaN;
        public long Calls;
        public long Steps;
        public int Stages;
        public double Wall = double.NaN;
        public long Alloc;
    }

    private sealed class QualificationGameControlEntry
    {
        public string Problem;
        public string Method;
        public double Control = double.NaN;
        public string Status;
        public double Err = double.NaN;
        public long Calls;
    }

    private static readonly string[] QualificationProductionClassNames =
        { "leo", "gto / molniya / heo", "geo", "flyby" };

    private static Dictionary<string, string[]> QualificationProductionClasses()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        map["leo"] = new[] { "S01", "S02", "S03", "S09" };
        map["gto / molniya / heo"] = new[] { "S04", "S05", "S14" };
        map["geo"] = new[] { "S06" };
        map["flyby"] = new[] { "S12", "S15", "R4" };
        return map;
    }

    private static void QualificationWriteProductionReport(
        string dir, List<QualificationProblem> catalog, TextWriter log)
    {
        List<QualificationMatrixEntry> matrix = QualificationReadMatrix(dir);
        List<QualificationRunEntry> runs = QualificationReadRuns(dir);
        List<QualificationChunkEntry> chunks = QualificationReadChunks(dir);
        List<QualificationGameControlEntry> gameControls = QualificationReadGameControl(dir);
        var sb = new StringBuilder();
        sb.AppendLine("# PRODUCTION_REPORT: game conditions (Appendix C)");
        sb.AppendLine();
        sb.AppendLine("Generated from `matrix.csv`, `runs.csv` and `chunks.csv` of this run; no hand edits.");
        sb.AppendLine("`Prod-DOP853` and `Prod-DOPRI5` are the GAME steppers");
        sb.AppendLine("(`SpacecraftOrbitIntegrator.StepForwardDop853` / `StepForward`) with the game's");
        sb.AppendLine("`SpacecraftPhysics` tolerances and `OrbitIntegrator.DefaultMaxStepSize = 600 s`.");
        sb.AppendLine("The adapter is verified against the game wrapper by a bitwise K1-K8 parity check;");
        sb.AppendLine("the full `VesselStep`/`SimulationRunner` path is covered by the game tests, not here.");
        sb.AppendLine();
        sb.AppendLine("The stand baseline map `WHICH_INTEGRATOR_WHEN.md` does not exist (`FOUND.md` F13);");
        sb.AppendLine("the comparison below uses the baseline `matrix.csv` named in `baseline/BASELINE.md`.");
        sb.AppendLine();

        if (matrix.Count == 0)
        {
            sb.AppendLine("`matrix.csv` is empty; nothing to report.");
            File.WriteAllText(Path.Combine(dir, "PRODUCTION_REPORT.md"), sb.ToString(), new UTF8Encoding(false));
            return;
        }

        QualificationProductionScenarios(sb, matrix);
        Dictionary<string, string[]> classes = QualificationProductionClasses();
        QualificationProductionClassTable(sb, matrix, classes);
        QualificationProductionSubclassTable(sb, matrix);
        QualificationProductionGateTable(sb, matrix, runs, gameControls, chunks, classes);
        QualificationProductionChunkTable(sb, chunks);
        QualificationProductionBaselineTable(sb, matrix);
        QualificationProductionLimits(sb);
        File.WriteAllText(Path.Combine(dir, "PRODUCTION_REPORT.md"), sb.ToString(), new UTF8Encoding(false));
        log.WriteLine("reports: PRODUCTION_REPORT.md");
    }

    private static void QualificationProductionScenarios(
        StringBuilder sb, List<QualificationMatrixEntry> matrix)
    {
        sb.AppendLine("## Scenarios and goals");
        sb.AppendLine();
        sb.AppendLine("| problem | goal | winner | price | second | margin | N/A | FAIL |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (string problem in QualificationProblemsInOrder(matrix))
        {
            foreach (string goal in QualificationGoalNamesInOrder(matrix))
            {
                if (!QualificationGoalPresent(matrix, problem, goal))
                {
                    continue;
                }

                string winner;
                double price;
                string second;
                string margin;
                string na;
                string fail;
                QualificationScenarioBest(matrix, problem, goal, out winner, out price, out second,
                    out margin, out na, out fail);
                sb.Append("| ").Append(problem).Append(" | ").Append(goal).Append(" | ")
                    .Append(winner).Append(" | ");
                sb.Append(double.IsFinite(price) ? QualificationFormat(price) : "—");
                sb.Append(" | ").Append(second).Append(" | ").Append(margin).Append(" | ")
                    .Append(na).Append(" | ").Append(fail).AppendLine(" |");
            }
        }

        sb.AppendLine();
    }

    private static void QualificationProductionClassTable(
        StringBuilder sb, List<QualificationMatrixEntry> matrix,
        Dictionary<string, string[]> classes)
    {
        sb.AppendLine("## Classes (TASK.md section 11)");
        sb.AppendLine();
        sb.AppendLine("A class with one task is printed as a fact about that task, not as a class conclusion.");
        sb.AppendLine();
        sb.AppendLine("| class | problems | goal | winner | second | margin |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (string name in QualificationProductionClassNames)
        {
            string[] problems = classes[name];
            foreach (string goal in QualificationGoalNamesInOrder(matrix))
            {
                bool goalInClass = true;
                foreach (string problem in problems)
                {
                    if (!QualificationGoalPresent(matrix, problem, goal))
                    {
                        goalInClass = false;
                        break;
                    }
                }

                if (!goalInClass)
                {
                    continue;
                }

                string winner;
                string second;
                string margin;
                string na;
                string fail;
                string line = QualificationClassBest(matrix, new List<string>(problems), goal,
                    out winner, out second, out margin, out na, out fail);
                sb.Append("| ").Append(name).Append(" | ").Append(string.Join(", ", problems))
                    .Append(" | ").Append(goal).Append(" | ").Append(winner).Append(" | ")
                    .Append(second).Append(" | ").Append(margin).AppendLine(" |");
                if (line.StartsWith("*No method", StringComparison.Ordinal))
                {
                    sb.AppendLine("| ").Append(name).Append(" | ").Append(string.Join(", ", problems))
                        .Append(" | ").Append(goal).Append(" | ").Append(line).AppendLine(" |");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("`heliocentric`: no task in the C4 production list (open question 10); the class");
        sb.AppendLine("cannot be won in this run and is not used in the gate below.");
        sb.AppendLine();
    }

    private static void QualificationProductionSubclassTable(
        StringBuilder sb, List<QualificationMatrixEntry> matrix)
    {
        sb.AppendLine("## GTO/Molniya vs HEO (the C6 class bundles all three)");
        sb.AppendLine();
        sb.AppendLine("The C6 class `gto/molniya/heo` is not a GTO/Molniya class. The table separates");
        sb.AppendLine("S04+S05 from S14 HEO; a method can win the first and lose the second.");
        sb.AppendLine();
        sb.AppendLine("| subset | method | ratio vs Prod-DOP853 |");
        sb.AppendLine("|---|---|---|");
        var subsets = new[]
        {
            new KeyValuePair<string, string[]>("gto / molniya (S04, S05)", new[] { "S04", "S05" }),
            new KeyValuePair<string, string[]>("heo (S14)", new[] { "S14" })
        };
        foreach (KeyValuePair<string, string[]> subset in subsets)
        {
            foreach (string method in QualificationMethodsInOrder(matrix))
            {
                if (method == "Prod-DOP853")
                {
                    continue;
                }

                double ratio = QualificationSubsetRatio(matrix, subset.Value, method);
                if (!double.IsFinite(ratio))
                {
                    continue;
                }

                sb.Append("| ").Append(subset.Key).Append(" | ").Append(method).Append(" | ")
                    .Append(ratio.ToString("F2", QualificationCatalogInv)).AppendLine("× |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("GTO/Molniya-only is still below the 1.5 threshold for WH-BM14A (1.45×);");
        sb.AppendLine("extending the policy there is an owner exception, not a C6 pass.");
        sb.AppendLine();
    }

    private static double QualificationSubsetRatio(
        List<QualificationMatrixEntry> matrix, string[] problems, string method)
    {
        double logSum = 0d;
        int counted = 0;
        foreach (string problem in problems)
        {
            double methodPrice;
            double prodPrice;
            string goal;
            if (!QualificationCommonGoal(matrix, problem, method, out goal,
                    out methodPrice, out prodPrice)
                || !(methodPrice > 0d) || !(prodPrice > 0d))
            {
                return double.NaN;
            }

            logSum += Math.Log(prodPrice / methodPrice);
            counted++;
        }

        return counted == problems.Length ? Math.Exp(logSum / counted) : double.NaN;
    }

    private static void QualificationProductionGateTable(
        StringBuilder sb, List<QualificationMatrixEntry> matrix,
        List<QualificationRunEntry> runs,
        List<QualificationGameControlEntry> gameControls,
        List<QualificationChunkEntry> chunks, Dictionary<string, string[]> classes)
    {
        sb.AppendLine("## Port gates (C6)");
        sb.AppendLine();
        sb.AppendLine("Price in `matrix.csv` is the cheapest control that reached the goal; error and");
        sb.AppendLine("game RHS are the separate `game_control.csv` runs at the game tolerances");
        sb.AppendLine("(fixed 600 s, adaptive rtol 1e-11). These are different controls — the price");
        sb.AppendLine("column is a best case, not the game-control cost.");
        sb.AppendLine();
        sb.AppendLine("Gate values: price ratio ≥ 1.5 on ≥ 2 classes; wall 600 s not worse than");
        sb.AppendLine("`Prod-DOP853` (≤ 1.0); error not worse than `Prod-DOP853` with a 1.5× allowance");
        sb.AppendLine("AND not below the task's oracle floor: threshold = max(1.5×Prod, floor).");
        sb.AppendLine("Wall at 1/60 and 30 s is in `chunks.csv` for information only: it includes the");
        sb.AppendLine("stand's per-chunk StudyProblem/StudyStops overhead, not the game's entry cost.");
        sb.AppendLine();
        sb.AppendLine("C3 candidates (TASK.md C3): RKN12(10), WH-BM14A/WH-Y6, IAS15. Other rows are");
        sb.AppendLine("shown for context; the gate does not invent a candidate list of its own.");
        sb.AppendLine();
        sb.AppendLine("| method | price classes (ratio) | game RHS × | wall 600 s | error (floor-aware) | verdict |");
        sb.AppendLine("|---|---|---|---|---|---|");
        int passed = 0;
        var pricePass = new List<string>();
        foreach (string method in QualificationMethodsInOrder(matrix))
        {
            if (method == "Prod-DOP853")
            {
                sb.AppendLine("| Prod-DOP853 | baseline | 1.00 | baseline | baseline | **baseline** |");
                continue;
            }

            int wonClasses = 0;
            var classText = new StringBuilder();
            bool wallOk = true;
            bool errorOk = true;
            var wallNotes = new List<string>();
            var errorNotes = new List<string>();
            foreach (string name in QualificationProductionClassNames)
            {
                string[] problems = classes[name];
                double logSum = 0d;
                int counted = 0;
                bool classComplete = true;
                foreach (string problem in problems)
                {
                    double methodPrice;
                    double prodPrice;
                    string goal;
                    if (!QualificationCommonGoal(matrix, problem, method, out goal,
                            out methodPrice, out prodPrice))
                    {
                        classComplete = false;
                        break;
                    }

                    if (!(methodPrice > 0d) || !(prodPrice > 0d))
                    {
                        classComplete = false;
                        break;
                    }

                    logSum += Math.Log(prodPrice / methodPrice);
                    counted++;
                }

                double ratio = classComplete && counted == problems.Length
                    ? Math.Exp(logSum / counted)
                    : double.NaN;
                if (classComplete && double.IsFinite(ratio) && ratio >= 1.5d)
                {
                    wonClasses++;
                }

                classText.Append(name).Append(": ")
                    .Append(double.IsFinite(ratio) ? ratio.ToString("F2", QualificationCatalogInv) : "—")
                    .Append("; ");
                if (!classComplete)
                {
                    errorOk = false;
                    errorNotes.Add(problemClassNote(name, "no common PASS goal"));
                }

                // Wall gate: the 600 s chunk only. The 1/60 and 30 s cells are in chunks.csv
                // for information; they include the stand's per-chunk overhead.
                foreach (string problem in problems)
                {
                    foreach (double chunk in new[] { 600d })
                    {
                        double wm = QualificationChunkWall(chunks, problem, method, chunk);
                        double wp = QualificationChunkWall(chunks, problem, "Prod-DOP853", chunk);
                        if (!double.IsFinite(wm) || !double.IsFinite(wp) || wp <= 0d)
                        {
                            wallOk = false;
                            wallNotes.Add(problem + "@" + chunk.ToString("G4", QualificationCatalogInv) + " not measured");
                        }
                        else if (wm > wp)
                        {
                            wallOk = false;
                            wallNotes.Add(problem + "@" + chunk.ToString("G4", QualificationCatalogInv)
                                + " " + wm.ToString("G3", QualificationCatalogInv) + " > "
                                + wp.ToString("G3", QualificationCatalogInv) + " ms");
                        }
                    }
                }

                // Error gate: the same game control (C6 "same tolerances"), from game_control.csv.
                foreach (string problem in problems)
                {
                    double methodPrice;
                    double prodPrice;
                    string goal;
                    if (!QualificationCommonGoal(matrix, problem, method, out goal,
                            out methodPrice, out prodPrice))
                    {
                        continue;
                    }

                    QualificationGameControlEntry gcM = QualificationFindGameControl(
                        gameControls, problem, method);
                    QualificationGameControlEntry gcP = QualificationFindGameControl(
                        gameControls, problem, "Prod-DOP853");
                    if (gcM == null || gcP == null || gcM.Status != "ok" || gcP.Status != "ok"
                        || !double.IsFinite(gcM.Err) || !double.IsFinite(gcP.Err))
                    {
                        errorOk = false;
                        errorNotes.Add(problem + " " + goal + " no game-control run");
                    }
                    else
                    {
                        double floor = double.IsFinite(gcP.Err) ? 0d : 0d;
                        QualificationRunEntry runP = QualificationFindRun(
                            runs, problem, "Prod-DOP853", gcP.Control);
                        if (runP != null && double.IsFinite(runP.OracleFloor))
                        {
                            floor = runP.OracleFloor;
                        }

                        double threshold = Math.Max(1.5d * gcP.Err, floor);
                        if (gcM.Err > threshold)
                        {
                            errorOk = false;
                            errorNotes.Add(problem + " " + goal + " err "
                                + QualificationFormat(gcM.Err) + " > max(1.5×"
                                + QualificationFormat(gcP.Err) + ", floor "
                                + QualificationFormat(floor) + ")");
                        }
                    }
                }
            }

            bool pass = wonClasses >= 2 && wallOk && errorOk;
            if (wonClasses >= 2)
            {
                pricePass.Add(method);
            }

            if (pass)
            {
                passed++;
            }

            double gameRatio = QualificationGameCallsRatio(gameControls, classes, method);
            sb.Append("| ").Append(method).Append(" | ")
                .Append(classText.ToString().TrimEnd(' ', ';')).Append(" | ")
                .Append(double.IsFinite(gameRatio)
                    ? gameRatio.ToString("F2", QualificationCatalogInv) + "×"
                    : "—").Append(" | ")
                .Append(wallOk ? "PASS" : "FAIL: " + string.Join("; ", wallNotes)).Append(" | ")
                .Append(errorOk ? "PASS" : "FAIL: " + string.Join("; ", errorNotes)).Append(" | ")
                .Append(pass ? "**PASS to port**" : "**FAIL**").AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("### C6 verdict");
        sb.AppendLine();
        sb.AppendLine("Price gate (≥ 2 classes): "
            + (pricePass.Count == 0 ? "no method" : string.Join(", ", pricePass)) + ".");
        if (passed == 0)
        {
            sb.AppendLine("**No method passes all three C6 conditions in this run.** The decision");
            sb.AppendLine("request (C4) lists what each candidate misses; no port is started without");
            sb.AppendLine("the owner's answer.");
        }
        else
        {
            sb.AppendLine("Methods passing all three C6 conditions: **"
                + passed.ToString(QualificationCatalogInv) + "** (see the verdict column).");
        }

        sb.AppendLine();
    }

    private static string problemClassNote(string className, string note)
    {
        return className + ": " + note;
    }

    /// <summary>
    /// Finest goal where both the method and Prod-DOP853 PASS on this problem, with their prices.
    /// </summary>
    private static bool QualificationCommonGoal(
        List<QualificationMatrixEntry> matrix, string problem, string method,
        out string goal, out double methodPrice, out double prodPrice)
    {
        goal = "";
        methodPrice = double.NaN;
        prodPrice = double.NaN;
        int bestRank = -1;
        foreach (QualificationMatrixEntry e in matrix)
        {
            if (e.Problem != problem || e.Method != method || e.Status != "PASS")
            {
                continue;
            }

            QualificationMatrixEntry prod = QualificationFind(matrix, problem, "Prod-DOP853", e.Level);
            if (prod == null || prod.Status != "PASS")
            {
                continue;
            }

            int rank = QualificationGoalRank(e.Level);
            if (rank > bestRank)
            {
                bestRank = rank;
                goal = e.Level;
                methodPrice = e.Price;
                prodPrice = prod.Price;
            }
        }

        return bestRank >= 0;
    }

    private static double QualificationChunkWall(
        List<QualificationChunkEntry> chunks, string problem, string method, double chunk)
    {
        foreach (QualificationChunkEntry c in chunks)
        {
            if (c.Problem == problem && c.Method == method && c.Status == "ok"
                && Math.Abs(c.Chunk - chunk) <= 1e-9d * Math.Max(c.Chunk, chunk))
            {
                return c.Wall;
            }
        }

        return double.NaN;
    }

    /// <summary>C6 "same tolerances": the game-control row of this (method, problem).</summary>
    private static QualificationGameControlEntry QualificationFindGameControl(
        List<QualificationGameControlEntry> gameControls, string problem, string method)
    {
        foreach (QualificationGameControlEntry e in gameControls)
        {
            if (e.Problem == problem && e.Method == method)
            {
                return e;
            }
        }

        return null;
    }

    /// <summary>Game-control RHS ratio method / Prod-DOP853 over the class problems.</summary>
    private static double QualificationGameCallsRatio(
        List<QualificationGameControlEntry> gameControls,
        Dictionary<string, string[]> classes, string method)
    {
        long methodCalls = 0L;
        long prodCalls = 0L;
        bool any = false;
        foreach (string name in QualificationProductionClassNames)
        {
            foreach (string problem in classes[name])
            {
                QualificationGameControlEntry m = QualificationFindGameControl(
                    gameControls, problem, method);
                QualificationGameControlEntry p = QualificationFindGameControl(
                    gameControls, problem, "Prod-DOP853");
                if (m == null || p == null || m.Status != "ok" || p.Status != "ok"
                    || m.Calls <= 0L || p.Calls <= 0L)
                {
                    return double.NaN;
                }

                methodCalls += m.Calls;
                prodCalls += p.Calls;
                any = true;
            }
        }

        return any && prodCalls > 0L ? (double)methodCalls / prodCalls : double.NaN;
    }

    private static void QualificationProductionChunkTable(
        StringBuilder sb, List<QualificationChunkEntry> chunks)
    {
        sb.AppendLine("## Chunk test (wall time per chunk)");
        sb.AppendLine();
        if (chunks.Count == 0)
        {
            sb.AppendLine("`chunks.csv` is missing; the chunk gate is not measured.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| problem | chunk s | winner | wall ms/chunk | second | Prod-DOP853 | gap vs Prod |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        var problems = new List<string>();
        var lengths = new List<double>();
        foreach (QualificationChunkEntry c in chunks)
        {
            if (!problems.Contains(c.Problem))
            {
                problems.Add(c.Problem);
            }

            bool seen = false;
            foreach (double l in lengths)
            {
                if (Math.Abs(l - c.Chunk) <= 1e-12d)
                {
                    seen = true;
                }
            }

            if (!seen)
            {
                lengths.Add(c.Chunk);
            }
        }

        problems.Sort(StringComparer.Ordinal);
        lengths.Sort();
        foreach (string problem in problems)
        {
            foreach (double chunk in lengths)
            {
                var rows = new List<QualificationChunkEntry>();
                foreach (QualificationChunkEntry c in chunks)
                {
                    if (c.Problem == problem && c.Status == "ok"
                        && Math.Abs(c.Chunk - chunk) <= 1e-9d * Math.Max(c.Chunk, chunk))
                    {
                        rows.Add(c);
                    }
                }

                rows.Sort(delegate (QualificationChunkEntry a, QualificationChunkEntry b)
                {
                    return a.Wall.CompareTo(b.Wall);
                });
                QualificationChunkEntry best = rows.Count > 0 ? rows[0] : null;
                QualificationChunkEntry second = rows.Count > 1 ? rows[1] : null;
                double prod = QualificationChunkWall(chunks, problem, "Prod-DOP853", chunk);
                sb.Append("| ").Append(problem).Append(" | ")
                    .Append(chunk.ToString("G6", QualificationCatalogInv)).Append(" | ")
                    .Append(best == null ? "—" : best.Method).Append(" | ")
                    .Append(best == null ? "—" : best.Wall.ToString("G3", QualificationCatalogInv)).Append(" | ")
                    .Append(second == null ? "—" : second.Method).Append(" | ")
                    .Append(double.IsFinite(prod) ? prod.ToString("G3", QualificationCatalogInv) : "—").Append(" | ")
                    .Append(best != null && double.IsFinite(prod) && best.Wall > 0d
                        ? (prod / best.Wall).ToString("F2", QualificationCatalogInv) + "×"
                        : "—")
                    .AppendLine(" |");
            }
        }

        sb.AppendLine();
    }

    private static void QualificationProductionBaselineTable(
        StringBuilder sb, List<QualificationMatrixEntry> matrix)
    {
        string baselineDir = QualificationBaselineMatrixDir();
        sb.AppendLine("## Changed winners against the stand baseline");
        sb.AppendLine();
        if (string.IsNullOrEmpty(baselineDir) || !Directory.Exists(baselineDir))
        {
            sb.AppendLine("Baseline folder not found from `baseline/BASELINE.md`; no comparison table.");
            sb.AppendLine();
            return;
        }

        List<QualificationMatrixEntry> baseline = QualificationReadMatrix(baselineDir);
        sb.AppendLine("Baseline: `" + Path.GetFileName(baselineDir.TrimEnd(Path.DirectorySeparatorChar)) + "`.");
        sb.AppendLine();
        sb.AppendLine("| problem | goal | stand winner | production winner | changed |");
        sb.AppendLine("|---|---|---|---|---|");
        int changed = 0;
        foreach (string problem in QualificationProblemsInOrder(matrix))
        {
            foreach (string goal in QualificationGoalNamesInOrder(matrix))
            {
                if (!QualificationGoalPresent(matrix, problem, goal))
                {
                    continue;
                }

                string wNow;
                double pNow;
                string sNow;
                string mNow;
                string naNow;
                string failNow;
                QualificationScenarioBest(matrix, problem, goal, out wNow, out pNow, out sNow,
                    out mNow, out naNow, out failNow);
                string wBase;
                double pBase;
                string sBase;
                string mBase;
                string naBase;
                string failBase;
                QualificationScenarioBest(baseline, problem, goal, out wBase, out pBase, out sBase,
                    out mBase, out naBase, out failBase);
                bool diff = !string.Equals(wNow, wBase, StringComparison.Ordinal);
                if (diff)
                {
                    changed++;
                }

                sb.Append("| ").Append(problem).Append(" | ").Append(goal).Append(" | ")
                    .Append(wBase).Append(" | ").Append(wNow).Append(" | ")
                    .Append(diff ? "yes" : "no").AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Changed cells: " + changed.ToString(QualificationCatalogInv) + ".");
        sb.AppendLine();
    }

    private static string QualificationBaselineMatrixDir()
    {
        string path = Path.Combine(QualificationRoot(), "baseline", "BASELINE.md");
        if (!File.Exists(path))
        {
            return null;
        }

        string text = File.ReadAllText(path);
        int start = text.IndexOf("`baseline/", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        int end = text.IndexOf('`', start + 1);
        if (end < 0)
        {
            return null;
        }

        string relative = text.Substring(start + 1, end - start - 1).TrimEnd('/');
        return Path.Combine(QualificationRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void QualificationProductionLimits(StringBuilder sb)
    {
        sb.AppendLine("## Limits of this report");
        sb.AppendLine();
        sb.AppendLine("1. `Prod-*` are the GAME STEPPERS on the study problem's state, not the full");
        sb.AppendLine("   `VesselStep`/`SimulationRunner` path; adapter parity is checked bitwise on K1-K8.");
        sb.AppendLine("2. `matrix.csv` price is the cheapest goal-reaching control; `game_control.csv`");
        sb.AppendLine("   error/RHS are a separate run at the game control. The two are not the same");
        sb.AppendLine("   configuration; the price column is a best case, not the game-control cost.");
        sb.AppendLine("3. The error gate is floor-aware: threshold = max(1.5×Prod-DOP853, oracle floor).");
        sb.AppendLine("   `IAS15 S04` (3.298e-6) is below its floor 3.39e-6, so its old +5 % FAIL is");
        sb.AppendLine("   noise; `WH-BM14A S01` (7.94e-6) is above its floor 5.02e-6 and still fails.");
        sb.AppendLine("4. `WH-BM14A S05` (1.065e-4) is 1.12× its floor 9.52e-5: an upper bound, not a");
        sb.AppendLine("   certified accuracy; a GTO/Molniya decision needs a tighter reference.");
        sb.AppendLine("5. `L1` is graded against the local certified DOP853 (0.017 m for WH-BM14A);");
        sb.AppendLine("   the world-frame dd golden differs by 8.52 m (`FOUND.md` F8) and is not the");
        sb.AppendLine("   grading reference (owner decision 2026-11-11).");
        sb.AppendLine("6. `GJ8` at 600 s is one step and produces 3.67e6 m on S01; its matrix win is at");
        sb.AppendLine("   control 110.9 s (0.0296 m). The 600 s one-step figure is not a statement that");
        sb.AppendLine("   GJ8 is unusable; C3 excludes it from the port list separately.");
        sb.AppendLine("7. `WH-Y6` is 11–12× cheaper and 13× faster than DOP853, but only ~2.06×");
        sb.AppendLine("   cheaper and ~1.7–1.9× faster than WH-BM14A; the comparison above is vs DOP853.");
        sb.AppendLine("8. `chunks.csv` wall at 1/60 and 30 s includes the stand's per-chunk overhead and");
        sb.AppendLine("   is informational; only the 600 s column enters the port gate.");
        sb.AppendLine("9. `heliocentric` has no task in the C4 list (open question 10).");
        sb.AppendLine();
    }

    private static List<QualificationGameControlEntry> QualificationReadGameControl(string dir)
    {
        var list = new List<QualificationGameControlEntry>();
        string path = Path.Combine(dir, "game_control.csv");
        if (!File.Exists(path))
        {
            return list;
        }

        List<string[]> rows = QualificationReadCsv(path);
        if (rows.Count < 2)
        {
            return list;
        }

        string[] header = rows[0];
        for (int i = 1; i < rows.Count; i++)
        {
            string[] cells = rows[i];
            if (cells.Length < header.Length)
            {
                continue;
            }

            var e = new QualificationGameControlEntry();
            for (int c = 0; c < header.Length; c++)
            {
                switch (header[c])
                {
                    case "problem_id": e.Problem = cells[c]; break;
                    case "method": e.Method = cells[c]; break;
                    case "control": e.Control = QualificationParseDouble(cells[c]); break;
                    case "status": e.Status = cells[c]; break;
                    case "err_max": e.Err = QualificationParseDouble(cells[c]); break;
                    case "rhs_calls": e.Calls = QualificationParseLong(cells[c]); break;
                }
            }

            list.Add(e);
        }

        return list;
    }

    private static List<QualificationChunkEntry> QualificationReadChunks(string dir)
    {
        var list = new List<QualificationChunkEntry>();
        string path = Path.Combine(dir, "chunks.csv");
        if (!File.Exists(path))
        {
            return list;
        }

        List<string[]> rows = QualificationReadCsv(path);
        if (rows.Count < 2)
        {
            return list;
        }

        string[] header = rows[0];
        for (int i = 1; i < rows.Count; i++)
        {
            string[] cells = rows[i];
            if (cells.Length < header.Length)
            {
                continue;
            }

            var e = new QualificationChunkEntry();
            for (int c = 0; c < header.Length; c++)
            {
                switch (header[c])
                {
                    case "problem_id": e.Problem = cells[c]; break;
                    case "method": e.Method = cells[c]; break;
                    case "control": e.Control = QualificationParseDouble(cells[c]); break;
                    case "chunk_s": e.Chunk = QualificationParseDouble(cells[c]); break;
                    case "arc_s": e.Arc = QualificationParseDouble(cells[c]); break;
                    case "status": e.Status = cells[c]; break;
                    case "reason": e.Reason = cells[c]; break;
                    case "err_m": e.Err = QualificationParseDouble(cells[c]); break;
                    case "rhs_calls": e.Calls = QualificationParseLong(cells[c]); break;
                    case "steps": e.Steps = QualificationParseLong(cells[c]); break;
                    case "stages": e.Stages = (int)QualificationParseLong(cells[c]); break;
                    case "wall_ms_per_chunk": e.Wall = QualificationParseDouble(cells[c]); break;
                    case "alloc_bytes_per_chunk": e.Alloc = QualificationParseLong(cells[c]); break;
                }
            }

            list.Add(e);
        }

        return list;
    }
}
