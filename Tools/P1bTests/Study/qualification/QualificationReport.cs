using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using P1bStudy.Framework;

// M6: METHOD_QUALIFICATION.md and RECOMMENDATIONS.md are generated from the run's own
// matrix.csv / runs.csv (no manual edits, no numbers from memory).
//
// QualificationCompareRuns reads a previous run folder and reports status/error/price changes.
internal static partial class P1bTests
{
    private sealed class QualificationMatrixEntry
    {
        public string Problem;
        public string Method;
        public string Level;
        public string Status;
        public string Reason;
        public double Control = double.NaN;
        public double Price = double.NaN;
    }

    private sealed class QualificationRunEntry
    {
        public string Problem;
        public string Method;
        public string Family;
        public double Control = double.NaN;
        public string Status;
        public double Err = double.NaN;
        public double ErrEnd = double.NaN;
        public double OrderObs = double.NaN;
        public double OrderExp = double.NaN;
        public double Energy = double.NaN;
        public double Angular = double.NaN;
        public double Com = double.NaN;
        public double Rev = double.NaN;
        public long Steps;
        public long Calls;
        public double Price = double.NaN;
        public double Wall = double.NaN;
        public long Alloc;
        public bool Deterministic;
    }

    // ---------------------------------------------------------------------------------
    // CSV reading (quoted fields, invariant numbers)
    // ---------------------------------------------------------------------------------

    private static List<string[]> QualificationReadCsv(string path)
    {
        var rows = new List<string[]>();
        if (!File.Exists(path))
        {
            return rows;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0)
            {
                continue;
            }

            rows.Add(QualificationSplitCsv(line));
        }

        return rows;
    }

    private static string[] QualificationSplitCsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
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
        return fields.ToArray();
    }

    private static double QualificationParseDouble(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return double.NaN;
        }

        double d;
        return double.TryParse(value, NumberStyles.Float, QualificationCatalogInv, out d)
            ? d
            : double.NaN;
    }

    private static long QualificationParseLong(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0L;
        }

        long l;
        return long.TryParse(value, NumberStyles.Integer, QualificationCatalogInv, out l) ? l : 0L;
    }

    private static List<QualificationMatrixEntry> QualificationReadMatrix(string dir)
    {
        var list = new List<QualificationMatrixEntry>();
        List<string[]> rows = QualificationReadCsv(Path.Combine(dir, "matrix.csv"));
        for (int i = 1; i < rows.Count; i++)
        {
            string[] f = rows[i];
            if (f.Length < 7)
            {
                continue;
            }

            var e = new QualificationMatrixEntry();
            e.Problem = f[0];
            e.Method = f[1];
            e.Level = f[2];
            e.Status = f[3];
            e.Reason = f[4];
            e.Control = QualificationParseDouble(f[5]);
            e.Price = QualificationParseDouble(f[6]);
            list.Add(e);
        }

        return list;
    }

    private static List<QualificationRunEntry> QualificationReadRuns(string dir)
    {
        var list = new List<QualificationRunEntry>();
        List<string[]> rows = QualificationReadCsv(Path.Combine(dir, "runs.csv"));
        for (int i = 1; i < rows.Count; i++)
        {
            string[] f = rows[i];
            if (f.Length < 23)
            {
                continue;
            }

            var e = new QualificationRunEntry();
            e.Problem = f[0];
            e.Method = f[1];
            e.Family = f[2];
            e.Control = QualificationParseDouble(f[4]);
            e.Status = f[5];
            e.Err = QualificationParseDouble(f[7]);
            e.ErrEnd = QualificationParseDouble(f[8]);
            e.OrderObs = QualificationParseDouble(f[9]);
            e.OrderExp = QualificationParseDouble(f[10]);
            e.Energy = QualificationParseDouble(f[11]);
            e.Angular = QualificationParseDouble(f[12]);
            e.Com = QualificationParseDouble(f[13]);
            e.Rev = QualificationParseDouble(f[14]);
            e.Steps = QualificationParseLong(f[15]);
            e.Calls = QualificationParseLong(f[17]);
            e.Price = QualificationParseDouble(f[18]);
            e.Wall = QualificationParseDouble(f[19]);
            e.Alloc = QualificationParseLong(f[20]);
            e.Deterministic = f[21] == "1";
            list.Add(e);
        }

        return list;
    }

    // ---------------------------------------------------------------------------------
    // Reports
    // ---------------------------------------------------------------------------------

    private static void QualificationWriteReports(
        string dir, List<QualificationProblem> catalog, TextWriter log)
    {
        List<QualificationMatrixEntry> matrix = QualificationReadMatrix(dir);
        List<QualificationRunEntry> runs = QualificationReadRuns(dir);
        if (matrix.Count == 0)
        {
            log.WriteLine("reports: matrix.csv is empty, nothing to report");
            return;
        }

        Dictionary<string, List<string>> bestAt;
        string recommendations = QualificationRecommendationsText(matrix, catalog, out bestAt);

        string qualification = QualificationMethodReport(matrix, runs, catalog, dir, bestAt);
        File.WriteAllText(Path.Combine(dir, "METHOD_QUALIFICATION.md"), qualification, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "RECOMMENDATIONS.md"), recommendations, new UTF8Encoding(false));

        log.WriteLine("reports: METHOD_QUALIFICATION.md, RECOMMENDATIONS.md");
    }

    private static Dictionary<string, string[]> QualificationTags(List<QualificationProblem> catalog)
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (QualificationProblem q in catalog)
        {
            map[q.Id] = q.Tags;
        }

        return map;
    }

    private static List<string> QualificationMethodsInOrder(List<QualificationMatrixEntry> matrix)
    {
        var methods = new List<string>();
        foreach (QualificationMatrixEntry e in matrix)
        {
            if (!methods.Contains(e.Method))
            {
                methods.Add(e.Method);
            }
        }

        return methods;
    }

    private static List<string> QualificationProblemsInOrder(List<QualificationMatrixEntry> matrix)
    {
        var problems = new List<string>();
        foreach (QualificationMatrixEntry e in matrix)
        {
            if (!problems.Contains(e.Problem))
            {
                problems.Add(e.Problem);
            }
        }

        return problems;
    }

    private static string QualificationEnvironmentField(string dir, string key)
    {
        string path = Path.Combine(dir, "environment.txt");
        if (!File.Exists(path))
        {
            return "unknown";
        }

        foreach (string line in File.ReadAllLines(path))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && string.Equals(line.Substring(0, colon).Trim(), key, StringComparison.Ordinal))
            {
                return line.Substring(colon + 1).Trim();
            }
        }

        return "unknown";
    }

    private static string QualificationMethodReport(
        List<QualificationMatrixEntry> matrix, List<QualificationRunEntry> runs,
        List<QualificationProblem> catalog, string dir, Dictionary<string, List<string>> bestAt)
    {
        var sb = new StringBuilder();
        string profile = QualificationEnvironmentField(dir, "profile");
        string commit = QualificationEnvironmentField(dir, "commit");
        List<string> methods = QualificationMethodsInOrder(matrix);
        List<string> problems = QualificationProblemsInOrder(matrix);
        Dictionary<string, string[]> tags = QualificationTags(catalog);
        var byMethod = new Dictionary<string, List<QualificationMatrixEntry>>(StringComparer.Ordinal);
        foreach (QualificationMatrixEntry e in matrix)
        {
            List<QualificationMatrixEntry> list;
            if (!byMethod.TryGetValue(e.Method, out list))
            {
                list = new List<QualificationMatrixEntry>();
                byMethod[e.Method] = list;
            }

            list.Add(e);
        }

        sb.AppendLine("# Method qualification (Test564)");
        sb.AppendLine();
        sb.AppendLine("Generated from `matrix.csv` and `runs.csv` of run `" + Path.GetFileName(dir) + "`.");
        sb.AppendLine("Profile: **" + profile + "**; commit: `" + commit + "`; "
            + problems.Count.ToString(QualificationCatalogInv) + " problems × "
            + methods.Count.ToString(QualificationCatalogInv) + " methods × 3 levels.");
        sb.AppendLine();
        sb.AppendLine("Statuses: PASS / FAIL / N/A (from `Supports`) / SKIP (oracle floor or budget) / ERROR.");
        sb.AppendLine("Gate thresholds are frozen; see `PROGRESS.md` in this folder.");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| method | family | PASS | FAIL | N/A | SKIP | ERROR | best at |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
        foreach (string method in methods)
        {
            List<QualificationMatrixEntry> mine = byMethod[method];
            int pass = 0;
            int fail = 0;
            int na = 0;
            int skip = 0;
            int error = 0;
            foreach (QualificationMatrixEntry e in mine)
            {
                if (e.Status == "PASS")
                {
                    pass++;
                }
                else if (e.Status == "FAIL")
                {
                    fail++;
                }
                else if (e.Status == "N/A")
                {
                    na++;
                }
                else if (e.Status == "SKIP")
                {
                    skip++;
                }
                else if (e.Status == "ERROR")
                {
                    error++;
                }
            }

            List<string> best;
            bestAt.TryGetValue(method, out best);
            sb.AppendLine("| " + method + " | " + QualificationFamilyOf(runs, method) + " | " + pass
                + " | " + fail + " | " + na + " | " + skip + " | " + error
                + " | " + (best == null || best.Count == 0 ? "—" : string.Join(", ", best)) + " |");
        }

        sb.AppendLine();
        sb.AppendLine("## Method × problem (A1 / A2 / A3)");
        sb.AppendLine();
        sb.Append("| method |");
        foreach (string problem in problems)
        {
            sb.Append(' ').Append(problem).Append(" |");
        }

        sb.AppendLine();
        sb.Append("|---|");
        foreach (string problem in problems)
        {
            sb.Append("---|");
        }

        sb.AppendLine();
        foreach (string method in methods)
        {
            sb.Append("| ").Append(method).Append(" |");
            foreach (string problem in problems)
            {
                sb.Append(' ').Append(QualificationCellSummary(matrix, problem, method)).Append(" |");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Legend: P = PASS, F = FAIL, n/a = not applicable, skip = budget/oracle floor.");
        sb.AppendLine("**Valid for** (in the per-method sections) lists the tags of problems where the method");
        sb.AppendLine("has at least one PASS; it is not a claim that every level of those problems passes.");
        sb.AppendLine("**Gate notes on PASS** shows gates that were skipped by the model-drift guard (G3).");
        sb.AppendLine("**Observed order** is filled for adaptive methods too (informative only): G2 applies");
        sb.AppendLine("only to fixed-step methods with a declared ExpectedOrder.");
        sb.AppendLine();
        sb.AppendLine("## Per method");
        sb.AppendLine();
        foreach (string method in methods)
        {
            List<QualificationMatrixEntry> mine = byMethod[method];
            sb.AppendLine("### " + method);
            sb.AppendLine();
            sb.AppendLine("* **Valid for (≥1 PASS):** " + QualificationValidFor(mine, tags));
            List<string> best;
            bestAt.TryGetValue(method, out best);
            sb.AppendLine("* **Best at:** " + (best == null || best.Count == 0 ? "—" : string.Join(", ", best)));
            sb.AppendLine("* **N/A:** " + QualificationGroupedReasons(mine, "N/A"));
            sb.AppendLine("* **FAIL:** " + QualificationGroupedReasons(mine, "FAIL"));
            sb.AppendLine("* **SKIP:** " + QualificationGroupedReasons(mine, "SKIP"));
            string passNotes = QualificationPassNotes(mine);
            if (passNotes.Length > 0)
            {
                sb.AppendLine("* **Gate notes on PASS:** " + passNotes);
            }

            sb.AppendLine("* **Observed order:** " + QualificationOrderSummary(runs, method));
            sb.AppendLine("* **Typical invariants (all rows):** " + QualificationDriftSummary(runs, method));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string QualificationFamilyOf(List<QualificationRunEntry> runs, string method)
    {
        foreach (QualificationRunEntry r in runs)
        {
            if (r.Method == method && !string.IsNullOrEmpty(r.Family))
            {
                return r.Family;
            }
        }

        return "-";
    }

    private static string QualificationCellSummary(
        List<QualificationMatrixEntry> matrix, string problem, string method)
    {
        string a1 = "?";
        string a2 = "?";
        string a3 = "?";
        foreach (QualificationMatrixEntry e in matrix)
        {
            if (e.Problem != problem || e.Method != method)
            {
                continue;
            }

            string shortName = e.Status == "PASS" ? "P"
                : e.Status == "FAIL" ? "F"
                : e.Status == "N/A" ? "n/a"
                : e.Status == "SKIP" ? "skip"
                : "ERR";
            if (e.Level == "A1")
            {
                a1 = shortName;
            }
            else if (e.Level == "A2")
            {
                a2 = shortName;
            }
            else
            {
                a3 = shortName;
            }
        }

        return a1 + "/" + a2 + "/" + a3;
    }

    private static string QualificationValidFor(
        List<QualificationMatrixEntry> mine, Dictionary<string, string[]> tags)
    {
        var list = new List<string>();
        foreach (QualificationMatrixEntry e in mine)
        {
            if (e.Status != "PASS")
            {
                continue;
            }

            string[] t;
            if (!tags.TryGetValue(e.Problem, out t))
            {
                continue;
            }

            foreach (string tag in t)
            {
                if (!list.Contains(tag))
                {
                    list.Add(tag);
                }
            }
        }

        return list.Count == 0 ? "—" : string.Join(", ", list);
    }

    private static string QualificationGroupedReasons(
        List<QualificationMatrixEntry> mine, string status)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (QualificationMatrixEntry e in mine)
        {
            if (e.Status != status)
            {
                continue;
            }

            string reason = string.IsNullOrEmpty(e.Reason) ? "(no reason)" : e.Reason;
            List<string> list;
            if (!groups.TryGetValue(reason, out list))
            {
                list = new List<string>();
                groups[reason] = list;
                order.Add(reason);
            }

            string key = e.Problem + "." + e.Level;
            if (!list.Contains(key))
            {
                list.Add(key);
            }
        }

        if (order.Count == 0)
        {
            return "—";
        }

        var sb = new StringBuilder();
        foreach (string reason in order)
        {
            sb.Append(reason).Append(": ").Append(string.Join(", ", groups[reason])).Append("; ");
        }

        return sb.ToString().TrimEnd(' ', ';');
    }

    private static string QualificationPassNotes(List<QualificationMatrixEntry> mine)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (QualificationMatrixEntry e in mine)
        {
            if (e.Status != "PASS" || string.IsNullOrEmpty(e.Reason))
            {
                continue;
            }

            List<string> list;
            if (!groups.TryGetValue(e.Reason, out list))
            {
                list = new List<string>();
                groups[e.Reason] = list;
                order.Add(e.Reason);
            }

            string key = e.Problem + "." + e.Level;
            if (!list.Contains(key))
            {
                list.Add(key);
            }
        }

        if (order.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach (string reason in order)
        {
            sb.Append(reason).Append(": ").Append(string.Join(", ", groups[reason])).Append("; ");
        }

        return sb.ToString().TrimEnd(' ', ';');
    }

    private static string QualificationOrderSummary(List<QualificationRunEntry> runs, string method)
    {
        var parts = new List<string>();
        var seen = new List<string>();
        foreach (QualificationRunEntry r in runs)
        {
            if (r.Method != method || double.IsNaN(r.OrderObs) || seen.Contains(r.Problem))
            {
                continue;
            }

            seen.Add(r.Problem);
            parts.Add(r.Problem + " " + r.OrderObs.ToString("F2", QualificationCatalogInv)
                + " vs " + (double.IsNaN(r.OrderExp) ? "n/a" : r.OrderExp.ToString("F2", QualificationCatalogInv)));
        }

        return parts.Count == 0 ? "n/a (too few asymptotic points)" : string.Join("; ", parts);
    }

    private static string QualificationDriftSummary(List<QualificationRunEntry> runs, string method)
    {
        double de = 0d;
        double dl = 0d;
        double com = 0d;
        double rev = 0d;
        bool anyE = false;
        bool anyL = false;
        bool anyCom = false;
        bool anyRev = false;
        foreach (QualificationRunEntry r in runs)
        {
            if (r.Method != method)
            {
                continue;
            }

            if (double.IsFinite(r.Energy) && (!anyE || r.Energy > de))
            {
                de = r.Energy;
                anyE = true;
            }

            if (double.IsFinite(r.Angular) && (!anyL || r.Angular > dl))
            {
                dl = r.Angular;
                anyL = true;
            }

            if (double.IsFinite(r.Com) && (!anyCom || r.Com > com))
            {
                com = r.Com;
                anyCom = true;
            }

            if (double.IsFinite(r.Rev) && (!anyRev || r.Rev > rev))
            {
                rev = r.Rev;
                anyRev = true;
            }
        }

        var sb = new StringBuilder();
        sb.Append("max |dE|/E = ").Append(anyE ? QualificationFormat(de) : "n/a");
        sb.Append(", max |dL|/L = ").Append(anyL ? QualificationFormat(dl) : "n/a");
        if (anyCom)
        {
            sb.Append(", max dCOM/scale = ").Append(QualificationFormat(com));
        }

        if (anyRev)
        {
            sb.Append(", worst rev_err = ").Append(QualificationFormat(rev));
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------
    // Recommendations (TASK.md section 11)
    // ---------------------------------------------------------------------------------

    private static string QualificationRecommendationsText(
        List<QualificationMatrixEntry> matrix, List<QualificationProblem> catalog,
        out Dictionary<string, List<string>> bestAt)
    {
        bestAt = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        var tags = new List<string>();
        var tagProblems = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (QualificationProblem q in catalog)
        {
            if (q.Status != "VALID")
            {
                continue;
            }

            foreach (string tag in q.Tags)
            {
                if (!tagProblems.ContainsKey(tag))
                {
                    tagProblems[tag] = new List<string>();
                    tags.Add(tag);
                }

                if (!tagProblems[tag].Contains(q.Id))
                {
                    tagProblems[tag].Add(q.Id);
                }
            }
        }

        tags.Sort(StringComparer.Ordinal);
        sb.AppendLine("# Recommendations (Test564)");
        sb.AppendLine();
        sb.AppendLine("Generated from `matrix.csv`; the price is `calls + w·aux` (single-price rule).");
        var excludedProblems = new List<string>();
        foreach (QualificationProblem q in catalog)
        {
            if (q.Status != "VALID")
            {
                excludedProblems.Add(q.Id + " (" + q.Status + ")");
            }
        }

        if (excludedProblems.Count > 0)
        {
            sb.AppendLine("Problems excluded because their oracle is not VALID: "
                + string.Join(", ", excludedProblems) + " (see `oracle_audit.md`).");
        }
        else
        {
            sb.AppendLine("All catalog problems have a VALID oracle.");
        }

        sb.AppendLine();
        sb.AppendLine("Rule (frozen, TASK.md section 11): for each tag and level, use only problems whose");
        sb.AppendLine("level is evaluated by the oracle; need at least 2 problems; candidates must PASS on");
        sb.AppendLine("all of them; the price is normalised per problem and averaged geometrically; a");
        sb.AppendLine("second method within 15% is declared a tie.");
        sb.AppendLine();

        var body = new StringBuilder();
        foreach (string tag in tags)
        {
            body.AppendLine("## " + tag);
            body.AppendLine();
            foreach (string level in QualificationLevelNames)
            {
                body.AppendLine("### " + level);
                body.AppendLine();
                var evaluated = new List<string>();
                foreach (string problem in tagProblems[tag])
                {
                    if (QualificationLevelEvaluated(matrix, problem, level))
                    {
                        evaluated.Add(problem);
                    }
                }

                if (evaluated.Count < 2)
                {
                    body.AppendLine("*Not proven (fewer than 2 problems with an evaluated level).* Problems: "
                        + (evaluated.Count == 0 ? "none" : string.Join(", ", evaluated)));
                    body.AppendLine();
                    continue;
                }

                var candidates = new List<string>();
                var naMethods = new List<string>();
                var failMethods = new List<string>();
                var skipMethods = new List<string>();
                foreach (string method in QualificationMethodsInOrder(matrix))
                {
                    bool allPass = true;
                    bool anyNa = false;
                    bool anyFail = false;
                    bool anySkip = false;
                    foreach (string problem in evaluated)
                    {
                        QualificationMatrixEntry e = QualificationFind(matrix, problem, method, level);
                        if (e == null)
                        {
                            continue;
                        }

                        if (e.Status == "PASS")
                        {
                            continue;
                        }

                        allPass = false;
                        if (e.Status == "N/A")
                        {
                            anyNa = true;
                        }
                        else if (e.Status == "FAIL" || e.Status == "ERROR")
                        {
                            anyFail = true;
                        }
                        else
                        {
                            anySkip = true;
                        }
                    }

                    if (allPass)
                    {
                        candidates.Add(method);
                        continue;
                    }

                    // A method can be N/A on one problem and FAIL on another; it belongs to
                    // both exclusion lists, not only to the first one that matched.
                    if (anyFail)
                    {
                        failMethods.Add(method);
                    }

                    if (anyNa)
                    {
                        naMethods.Add(method);
                    }

                    if (anySkip && !anyFail && !anyNa)
                    {
                        skipMethods.Add(method);
                    }
                }

                if (candidates.Count == 0)
                {
                    body.AppendLine("*No method passes on all " + evaluated.Count.ToString(QualificationCatalogInv)
                        + " problems; no recommendation.*");
                    body.AppendLine("*Excluded N/A:* " + (naMethods.Count == 0 ? "none" : string.Join(", ", naMethods)));
                    body.AppendLine("*Excluded FAIL:* " + (failMethods.Count == 0 ? "none" : string.Join(", ", failMethods)));
                    if (skipMethods.Count > 0)
                    {
                        body.AppendLine("*Excluded SKIP:* " + string.Join(", ", skipMethods));
                    }

                    body.AppendLine();
                    continue;
                }

                var scores = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (string method in candidates)
                {
                    double logSum = 0d;
                    foreach (string problem in evaluated)
                    {
                        QualificationMatrixEntry e = QualificationFind(matrix, problem, method, level);
                        double bestPrice = double.PositiveInfinity;
                        foreach (string other in candidates)
                        {
                            QualificationMatrixEntry o = QualificationFind(matrix, problem, other, level);
                            if (o != null && o.Price > 0d && o.Price < bestPrice)
                            {
                                bestPrice = o.Price;
                            }
                        }

                        double ratio = e != null && e.Price > 0d && double.IsFinite(bestPrice)
                            ? e.Price / bestPrice
                            : 1d;
                        logSum += Math.Log(ratio);
                    }

                    scores[method] = Math.Exp(logSum / evaluated.Count);
                }

                var ranked = new List<KeyValuePair<string, double>>();
                foreach (KeyValuePair<string, double> pair in scores)
                {
                    ranked.Add(pair);
                }

                ranked.Sort(delegate (KeyValuePair<string, double> a, KeyValuePair<string, double> b)
                {
                    int c = a.Value.CompareTo(b.Value);
                    return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
                });

                string winner = ranked[0].Key;
                double winnerScore = ranked[0].Value;
                string second = ranked.Count > 1 ? ranked[1].Key : "none";
                double secondScore = ranked.Count > 1 ? ranked[1].Value : double.NaN;
                bool tie = ranked.Count > 1 && secondScore <= 1.15d * winnerScore;
                string margin = ranked.Count > 1
                    ? ((secondScore / winnerScore - 1d) * 100d).ToString("F1", QualificationCatalogInv) + "%"
                    : "n/a";

                body.AppendLine("*Problems:* " + string.Join(", ", evaluated));
                body.AppendLine("*Recommended:* **" + winner + "** (score "
                    + winnerScore.ToString("F3", QualificationCatalogInv) + ")"
                    + (tie ? " — tie with **" + second + "** within 15%" : ""));
                body.AppendLine("*Second:* " + second + " (score "
                    + (double.IsNaN(secondScore) ? "n/a" : secondScore.ToString("F3", QualificationCatalogInv))
                    + ", margin " + margin + ")");
                body.AppendLine("*Excluded N/A:* " + (naMethods.Count == 0 ? "none" : string.Join(", ", naMethods)));
                body.AppendLine("*Excluded FAIL:* " + (failMethods.Count == 0 ? "none" : string.Join(", ", failMethods)));
                if (skipMethods.Count > 0)
                {
                    body.AppendLine("*Excluded SKIP:* " + string.Join(", ", skipMethods));
                }

                body.AppendLine();

                List<string> best;
                if (!bestAt.TryGetValue(winner, out best))
                {
                    best = new List<string>();
                    bestAt[winner] = best;
                }

                best.Add(tag + " " + level);

                if (tie)
                {
                    if (!bestAt.TryGetValue(second, out best))
                    {
                        best = new List<string>();
                        bestAt[second] = best;
                    }

                    best.Add(tag + " " + level);
                }
            }
        }

        sb.Append(body);
        return sb.ToString();
    }

    private static bool QualificationLevelEvaluated(
        List<QualificationMatrixEntry> matrix, string problem, string level)
    {
        foreach (QualificationMatrixEntry e in matrix)
        {
            if (e.Problem == problem && e.Level == level
                && (e.Status == "PASS" || e.Status == "FAIL" || e.Status == "ERROR"))
            {
                return true;
            }
        }

        return false;
    }

    private static QualificationMatrixEntry QualificationFind(
        List<QualificationMatrixEntry> matrix, string problem, string method, string level)
    {
        foreach (QualificationMatrixEntry e in matrix)
        {
            if (e.Problem == problem && e.Method == method && e.Level == level)
            {
                return e;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------
    // --compare
    // ---------------------------------------------------------------------------------

    private static void QualificationCompareRuns(string currentDir, string otherDir, TextWriter log)
    {
        List<QualificationMatrixEntry> now = QualificationReadMatrix(currentDir);
        List<QualificationMatrixEntry> before = QualificationReadMatrix(otherDir);
        List<QualificationRunEntry> nowRuns = QualificationReadRuns(currentDir);
        List<QualificationRunEntry> beforeRuns = QualificationReadRuns(otherDir);

        var sb = new StringBuilder();
        sb.AppendLine("# Comparison with " + Path.GetFileName(otherDir));
        sb.AppendLine();

        int statusChanges = 0;
        foreach (QualificationMatrixEntry a in now)
        {
            QualificationMatrixEntry b = QualificationFind(before, a.Problem, a.Method, a.Level);
            if (b == null)
            {
                sb.AppendLine("* new cell " + a.Problem + " " + a.Method + " " + a.Level
                    + ": " + a.Status);
                statusChanges++;
                continue;
            }

            if (a.Status != b.Status)
            {
                sb.AppendLine("* status " + a.Problem + " " + a.Method + " " + a.Level
                    + ": " + b.Status + " -> " + a.Status
                    + (a.Reason.Length > 0 ? " (" + a.Reason + ")" : ""));
                statusChanges++;
            }
        }

        int errChanges = 0;
        int priceChanges = 0;
        foreach (QualificationRunEntry a in nowRuns)
        {
            QualificationRunEntry b = QualificationFindRun(beforeRuns, a.Problem, a.Method, a.Control);
            if (b == null)
            {
                continue;
            }

            if (double.IsFinite(a.Err) && double.IsFinite(b.Err) && b.Err > 0d
                && Math.Abs(a.Err - b.Err) > 0.10d * b.Err)
            {
                sb.AppendLine("* error " + a.Problem + " " + a.Method + " h/rtol="
                    + a.Control.ToString("G6", QualificationCatalogInv) + ": "
                    + b.Err.ToString("E3", QualificationCatalogInv) + " -> "
                    + a.Err.ToString("E3", QualificationCatalogInv));
                errChanges++;
            }

            if (double.IsFinite(a.Price) && double.IsFinite(b.Price) && b.Price > 0d
                && Math.Abs(a.Price - b.Price) > 0.10d * b.Price)
            {
                sb.AppendLine("* price " + a.Problem + " " + a.Method + " h/rtol="
                    + a.Control.ToString("G6", QualificationCatalogInv) + ": "
                    + b.Price.ToString("G6", QualificationCatalogInv) + " -> "
                    + a.Price.ToString("G6", QualificationCatalogInv));
                priceChanges++;
            }
        }

        if (statusChanges == 0 && errChanges == 0 && priceChanges == 0)
        {
            sb.AppendLine("No changes: statuses, errors and prices are identical within 10%.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("Summary: " + statusChanges + " status change(s), "
                + errChanges + " error change(s) > 10%, " + priceChanges + " price change(s) > 10%.");
        }

        string text = sb.ToString();
        File.WriteAllText(Path.Combine(currentDir, "compare.md"), text, new UTF8Encoding(false));
        log.WriteLine("compare: " + statusChanges + " status, " + errChanges + " error, "
            + priceChanges + " price change(s) vs " + Path.GetFileName(otherDir));
        log.Write(text);
    }

    private static QualificationRunEntry QualificationFindRun(
        List<QualificationRunEntry> runs, string problem, string method, double control)
    {
        foreach (QualificationRunEntry r in runs)
        {
            if (r.Problem != problem || r.Method != method)
            {
                continue;
            }

            // rtol values reach 1e-16, so the tolerance must be relative, not an absolute 1e-12.
            double scale = Math.Max(Math.Abs(r.Control), Math.Abs(control));
            double diff = Math.Abs(r.Control - control);
            if (scale <= 0d ? diff == 0d : diff <= 1e-9d * scale)
            {
                return r;
            }
        }

        return null;
    }
}
