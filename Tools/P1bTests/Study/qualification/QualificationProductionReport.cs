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
        QualificationProductionGateTable(sb, matrix, gameControls, chunks, classes);
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

    private static void QualificationProductionGateTable(
        StringBuilder sb, List<QualificationMatrixEntry> matrix,
        List<QualificationGameControlEntry> gameControls,
        List<QualificationChunkEntry> chunks, Dictionary<string, string[]> classes)
    {
        sb.AppendLine("## Port gates (C6)");
        sb.AppendLine();
        sb.AppendLine("Gate values are frozen in `PROGRESS.md`: price ratio ≥ 1.5 on ≥ 2 classes,");
        sb.AppendLine("wall time per chunk not worse than `Prod-DOP853` (≤ 1.0) at 1/60, 30, 600 s,");
        sb.AppendLine("error not worse than `Prod-DOP853` with a 1.5× noise allowance.");
        sb.AppendLine();
        sb.AppendLine("C3 candidates (TASK.md C3): RKN12(10), WH-BM14A/WH-Y6, IAS15. Other rows are");
        sb.AppendLine("shown for context; the gate does not invent a candidate list of its own.");
        sb.AppendLine();
        sb.AppendLine("| method | classes won (ratio) | wall | error | verdict |");
        sb.AppendLine("|---|---|---|---|---|");
        int passed = 0;
        var pricePass = new List<string>();
        foreach (string method in QualificationMethodsInOrder(matrix))
        {
            if (method == "Prod-DOP853")
            {
                sb.AppendLine("| Prod-DOP853 | baseline | baseline | baseline | **baseline** |");
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

                // Wall time gate: the class's chunks at 1/60, 30 and 600 s.
                foreach (string problem in problems)
                {
                    foreach (double chunk in new[] { 1d / 60d, 30d, 600d })
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
                    else if (gcM.Err > 1.5d * gcP.Err)
                    {
                        errorOk = false;
                        errorNotes.Add(problem + " " + goal + " err "
                            + QualificationFormat(gcM.Err) + " > 1.5×"
                            + QualificationFormat(gcP.Err));
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

            sb.Append("| ").Append(method).Append(" | ")
                .Append(classText.ToString().TrimEnd(' ', ';')).Append(" | ")
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
        sb.AppendLine("2. `chunks.csv` wall time is a median of 5 sequential repetitions per cell; the");
        sb.AppendLine("   arc is 10 s for 0.05 s and 1/60 s chunks and 600 s for 0.5/30/600 s chunks.");
        sb.AppendLine("   For study methods it includes the stand's per-chunk `StudyProblem`/`StudyStops`");
        sb.AppendLine("   overhead; at 1/60 s and 30 s that overhead is comparable to the integration,");
        sb.AppendLine("   so the wall gate is conservative for a ported method (a port has its own");
        sb.AppendLine("   per-chunk entry cost, not the stand's). The 600 s column is the cleanest");
        sb.AppendLine("   integration-dominated comparison.");
        sb.AppendLine("3. The chunk-test reference is a tight game DOP853 (rtol 1e-14, 64 sub-chunks)");
        sb.AppendLine("   over the same RHS: a self-convergence reference, not a G8 oracle and not a");
        sb.AppendLine("   PASS/FAIL gate. Production cells use the task's certified reference.");
        sb.AppendLine("4. The 600 s step cap and the game tolerances are read from");
        sb.AppendLine("   `OrbitIntegrator.DefaultMaxStepSize` and `SpacecraftPhysics`; no new value is used.");
        sb.AppendLine("5. `heliocentric` has no task in the C4 list (open question 10).");
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
