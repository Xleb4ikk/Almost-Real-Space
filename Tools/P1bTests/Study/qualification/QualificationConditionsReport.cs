using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// TASK.md Appendix A, A9.1: CONDITIONS_REPORT.md is generated from conditions.csv and
// matrix.csv only. It answers "which method reaches 100 / 10 / 1 / 0.1 / 0.01 m in this
// flight regime, at what price" and summarises the regime classes with section 11's rule
// (at least two tasks; a single task is reported as a fact about that task, not a class).
internal static partial class P1bTests
{
    private sealed class QualificationConditionRow
    {
        public string Problem = "";
        public string Tags = "";
        public string CentralBody = "";
        public string Forces = "";
        public string Targets = "";
        public double RMin = double.NaN;
        public double RMax = double.NaN;
        public double VMin = double.NaN;
        public double VMax = double.NaN;
        public double Horizon = double.NaN;
        public double Floor = double.NaN;
        public bool VelocityDependent;
    }

    private static List<QualificationConditionRow> QualificationReadConditions(string dir)
    {
        var list = new List<QualificationConditionRow>();
        List<string[]> rows = QualificationReadCsv(Path.Combine(dir, "conditions.csv"));
        for (int i = 1; i < rows.Count; i++)
        {
            string[] f = rows[i];
            if (f.Length < 13)
            {
                continue;
            }

            var r = new QualificationConditionRow();
            r.Problem = f[0];
            r.Tags = f[1];
            r.CentralBody = f[2];
            r.RMin = QualificationParseDouble(f[3]);
            r.RMax = QualificationParseDouble(f[4]);
            r.VMin = QualificationParseDouble(f[5]);
            r.VMax = QualificationParseDouble(f[6]);
            r.Horizon = QualificationParseDouble(f[7]);
            r.Forces = f[8];
            r.VelocityDependent = f[9] == "1";
            r.Floor = QualificationParseDouble(f[11]);
            r.Targets = f[12];
            list.Add(r);
        }

        return list;
    }

    private static string QualificationWriteConditionsReport(
        string dir, List<QualificationProblem> catalog, List<QualificationMatrixEntry> matrix)
    {
        List<QualificationConditionRow> conditions = QualificationReadConditions(dir);
        var sb = new StringBuilder();
        sb.AppendLine("# Conditions report (Test564, Appendix A)");
        sb.AppendLine();
        sb.AppendLine("Generated from `conditions.csv` and `matrix.csv` of run `"
            + Path.GetFileName(dir) + "`; no number is typed by hand.");
        sb.AppendLine("Extrema are computed from the reference checkpoints plus the initial state; for a");
        sb.AppendLine("single particle in a central field the osculating conic adds the periapsis/apoapsis");
        sb.AppendLine("extreme the sparse checkpoint grid could miss. Price is `calls + w·aux`.");
        sb.AppendLine();

        sb.AppendLine("## Flight conditions");
        sb.AppendLine();
        sb.AppendLine("| problem | tags | central body | r_min, m | r_max, m | v_min, m/s | v_max, m/s | horizon, s | forces | vel/mass | oracle floor, m |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---|---:|---:|");
        foreach (QualificationConditionRow r in conditions)
        {
            sb.Append("| ").Append(r.Problem).Append(" | ").Append(r.Tags).Append(" | ")
                .Append(r.CentralBody).Append(" | ")
                .Append(QualificationConditionsCell(r.RMin, "E4")).Append(" | ")
                .Append(QualificationConditionsCell(r.RMax, "E4")).Append(" | ")
                .Append(QualificationConditionsCell(r.VMin, "E4")).Append(" | ")
                .Append(QualificationConditionsCell(r.VMax, "E4")).Append(" | ")
                .Append(QualificationConditionsCell(r.Horizon, "G6")).Append(" | ")
                .Append(r.Forces).Append(" | ")
                .Append(r.VelocityDependent ? "yes" : "no").Append(" | ")
                .Append(QualificationConditionsCell(r.Floor, "E3")).AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## Scenario results by metre goal");
        sb.AppendLine();
        sb.AppendLine("| problem | goal | winner | price | second | margin | excluded N/A | excluded FAIL |");
        sb.AppendLine("|---|---|---|---:|---|---:|---|---|");
        List<string> problems = QualificationProblemsInOrder(matrix);
        List<string> goals = QualificationGoalNamesInOrder(matrix);
        foreach (string problem in problems)
        {
            foreach (string goal in goals)
            {
                if (!goal.StartsWith("M", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!QualificationGoalPresent(matrix, problem, goal))
                {
                    continue;
                }

                string winner;
                string second;
                string margin;
                string na;
                string fail;
                double price;
                QualificationScenarioBest(matrix, problem, goal, out winner, out price, out second,
                    out margin, out na, out fail);
                sb.Append("| ").Append(problem).Append(" | ").Append(goal).Append(" | ")
                    .Append(winner).Append(" | ")
                    .Append(double.IsFinite(price) ? QualificationFormat(price) : "—").Append(" | ")
                    .Append(second).Append(" | ").Append(margin).Append(" | ")
                    .Append(na).Append(" | ").Append(fail).AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Class summary");
        sb.AppendLine();
        sb.AppendLine("Classes are the groups of A9.1. The rule is section 11: at least two tasks with the");
        sb.AppendLine("goal evaluated; candidates must pass on all of them. A single task is reported as its");
        sb.AppendLine("own fact, never as a class conclusion.");
        sb.AppendLine();
        var classes = new[]
        {
            new { Name = "leo / gto, gravity", Tags = new[] { "leo", "gto" }, GravityOnly = true },
            new { Name = "molniya / heo", Tags = new[] { "molniya", "heo" }, GravityOnly = true },
            new { Name = "geo", Tags = new[] { "geo" }, GravityOnly = true },
            new { Name = "heliocentric / far-flight", Tags = new[] { "heliocentric", "far-flight" }, GravityOnly = true },
            new { Name = "flyby / moon-soi", Tags = new[] { "flyby", "moon-soi" }, GravityOnly = true },
            new { Name = "j2", Tags = new[] { "j2" }, GravityOnly = true },
            new { Name = "drag / thrust", Tags = new[] { "drag", "thrust" }, GravityOnly = false },
            new { Name = "planets / probe-central", Tags = new[] { "planets", "probe-central" }, GravityOnly = true }
        };
        Dictionary<string, string[]> tagsOf = QualificationTags(catalog);
        foreach (var cls in classes)
        {
            sb.AppendLine("### " + cls.Name);
            sb.AppendLine();
            foreach (string goal in goals)
            {
                if (!goal.StartsWith("M", StringComparison.Ordinal))
                {
                    continue;
                }

                var members = new List<string>();
                foreach (QualificationProblem q in catalog)
                {
                    if (q.Status != "VALID" || !QualificationHasAnyTag(q, cls.Tags))
                    {
                        continue;
                    }

                    // Owner decision 2026-11-13: the gravity class must not mix in drag/thrust
                    // scenarios — they have their own class and would otherwise hide the fact
                    // that on pure gravity the splitting methods / RKN12 lead.
                    if (cls.GravityOnly && QualificationHasAnyTag(q, new[] { "drag", "thrust" }))
                    {
                        continue;
                    }

                    if (QualificationGoalPresent(matrix, q.Id, goal))
                    {
                        members.Add(q.Id);
                    }
                }

                if (members.Count == 0)
                {
                    continue;
                }

                sb.AppendLine("**" + goal + "**");
                sb.AppendLine();
                if (members.Count < 2)
                {
                    sb.AppendLine("*Not proven (one task, a fact about it only):* "
                        + QualificationScenarioFacts(matrix, members[0], goal));
                    sb.AppendLine();
                    continue;
                }

                var evaluated = new List<string>();
                foreach (string problem in members)
                {
                    if (QualificationLevelEvaluated(matrix, problem, goal))
                    {
                        evaluated.Add(problem);
                    }
                }

                if (evaluated.Count < 2)
                {
                    sb.AppendLine("*Not proven (" + evaluated.Count.ToString(QualificationCatalogInv)
                        + " task(s) with the goal evaluated):* "
                        + QualificationScenarioFacts(matrix, members[0], goal));
                    sb.AppendLine();
                    continue;
                }

                string winner;
                string second;
                string margin;
                string na;
                string fail;
                string details = QualificationClassBest(matrix, evaluated, goal, out winner,
                    out second, out margin, out na, out fail);
                sb.AppendLine(details);
                sb.AppendLine("*Excluded N/A:* " + na);
                sb.AppendLine("*Excluded FAIL:* " + fail);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static bool QualificationHasAnyTag(QualificationProblem q, string[] tags)
    {
        foreach (string tag in tags)
        {
            foreach (string own in q.Tags)
            {
                if (own == tag)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool QualificationGoalPresent(
        List<QualificationMatrixEntry> matrix, string problem, string goal)
    {
        foreach (QualificationMatrixEntry e in matrix)
        {
            if (e.Problem == problem && e.Level == goal)
            {
                return true;
            }
        }

        return false;
    }

    private static string QualificationConditionsCell(double value, string format)
    {
        return double.IsFinite(value) ? value.ToString(format, QualificationCatalogInv) : "";
    }

    private static void QualificationScenarioBest(
        List<QualificationMatrixEntry> matrix, string problem, string goal,
        out string winner, out double price, out string second, out string margin,
        out string na, out string fail)
    {
        var passed = new List<QualificationMatrixEntry>();
        var naList = new List<string>();
        var failList = new List<string>();
        foreach (QualificationMatrixEntry e in matrix)
        {
            if (e.Problem != problem || e.Level != goal)
            {
                continue;
            }

            if (e.Status == "PASS")
            {
                passed.Add(e);
            }
            else if (e.Status == "N/A")
            {
                naList.Add(e.Method);
            }
            else if (e.Status == "FAIL" || e.Status == "ERROR")
            {
                failList.Add(e.Method + " (" + (e.Reason.Length == 0 ? e.Status : e.Reason) + ")");
            }
        }

        passed.Sort(delegate (QualificationMatrixEntry a, QualificationMatrixEntry b)
        {
            int c = a.Price.CompareTo(b.Price);
            return c != 0 ? c : string.CompareOrdinal(a.Method, b.Method);
        });
        QualificationMatrixEntry best = passed.Count > 0 ? passed[0] : null;
        QualificationMatrixEntry next = passed.Count > 1 ? passed[1] : null;
        winner = best == null ? "—" : best.Method;
        price = best == null ? double.NaN : best.Price;
        second = next == null ? "—" : next.Method;
        margin = best != null && next != null && best.Price > 0d
            ? (next.Price / best.Price).ToString("F2", QualificationCatalogInv) + "×"
            : "—";
        na = naList.Count == 0 ? "none" : string.Join(", ", naList);
        fail = failList.Count == 0 ? "none" : string.Join("; ", failList);
    }

    private static string QualificationScenarioFacts(
        List<QualificationMatrixEntry> matrix, string problem, string goal)
    {
        string winner;
        double price;
        string second;
        string margin;
        string na;
        string fail;
        QualificationScenarioBest(matrix, problem, goal, out winner, out price, out second,
            out margin, out na, out fail);
        return "winner " + winner
            + (double.IsFinite(price) ? " at " + QualificationFormat(price) : "")
            + ", second " + second + ", margin " + margin
            + "; N/A: " + na + "; FAIL: " + fail;
    }

    /// <summary>Section 11 ranking over an explicit problem list (class summary).</summary>
    private static string QualificationClassBest(
        List<QualificationMatrixEntry> matrix, List<string> problems, string goal,
        out string winner, out string second, out string margin, out string na, out string fail)
    {
        var candidates = new List<string>();
        var naList = new List<string>();
        var failList = new List<string>();
        var failSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string method in QualificationMethodsInOrder(matrix))
        {
            bool allPass = true;
            bool anyNa = false;
            bool anyFail = false;
            string failReason = "";
            foreach (string problem in problems)
            {
                QualificationMatrixEntry e = QualificationFind(matrix, problem, method, goal);
                if (e == null)
                {
                    continue;
                }

                if (e.Status == "PASS")
                {
                    continue;
                }

                allPass = false;
                anyNa |= e.Status == "N/A";
                if (e.Status == "FAIL" || e.Status == "ERROR")
                {
                    anyFail = true;
                    if (failReason.Length == 0)
                    {
                        failReason = e.Reason;
                    }
                }
            }

            if (allPass)
            {
                candidates.Add(method);
                continue;
            }

            if (anyNa)
            {
                naList.Add(method);
            }

            if (anyFail && failSeen.Add(method))
            {
                failList.Add(method + " (" + (failReason.Length == 0 ? "FAIL" : failReason) + ")");
            }
        }

        na = naList.Count == 0 ? "none" : string.Join(", ", naList);
        fail = failList.Count == 0 ? "none" : string.Join(", ", failList);
        if (candidates.Count == 0)
        {
            winner = "—";
            second = "—";
            margin = "—";
            return "*No method passes on all " + problems.Count.ToString(QualificationCatalogInv)
                + " problems; no recommendation.*";
        }

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string method in candidates)
        {
            double logSum = 0d;
            foreach (string problem in problems)
            {
                QualificationMatrixEntry e = QualificationFind(matrix, problem, method, goal);
                double bestPrice = double.PositiveInfinity;
                foreach (string other in candidates)
                {
                    QualificationMatrixEntry o = QualificationFind(matrix, problem, other, goal);
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

            scores[method] = Math.Exp(logSum / problems.Count);
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

        winner = ranked[0].Key;
        double winnerScore = ranked[0].Value;
        second = ranked.Count > 1 ? ranked[1].Key : "—";
        double secondScore = ranked.Count > 1 ? ranked[1].Value : double.NaN;
        bool tie = ranked.Count > 1 && secondScore <= 1.15d * winnerScore;
        margin = ranked.Count > 1
            ? ((secondScore / winnerScore - 1d) * 100d).ToString("F1", QualificationCatalogInv) + "%"
            : "—";
        return "*Problems:* " + string.Join(", ", problems)
            + ". *Recommended:* **" + winner + "** (score "
            + winnerScore.ToString("F3", QualificationCatalogInv) + ")"
            + (tie ? " — tie with **" + second + "** within 15%" : "")
            + "; second " + second + " (margin " + margin + ").";
    }
}
