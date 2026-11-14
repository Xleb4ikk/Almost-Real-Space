using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// TASK.md Appendix B, B7: REAL_SYSTEM_REPORT.md.
//
// Everything here is built from CSV files of the current run:
//   * matrix.csv          statuses, prices, best controls;
//   * nasa_validation.csv E_model (layer V) of the task's model and window;
//   * real_system.csv     err_max, rms8_ref, moon_ref and the reference-only Horizons column
//                         at the certifying control.
// The Horizons residual never ranks anything (B1/B6): it is printed in its own table with the
// "mixes model and integrator error" caption.
internal static partial class P1bTests
{
    private sealed class QualificationRealRow
    {
        public string Problem;
        public string Method;
        public string Tier;
        public double Control = double.NaN;
        public double Err = double.NaN;
        public double Rms8 = double.NaN;
        public double Moon = double.NaN;
        public double Horizons = double.NaN;
    }

    private sealed class QualificationModelError
    {
        public string Window;
        public string Model;
        public double Rms8Km = double.NaN;
        public double MoonKm = double.NaN;
    }

    private static void QualificationWriteRealSystemReport(
        string dir, List<QualificationProblem> catalog, List<QualificationMatrixEntry> matrix,
        TextWriter log)
    {
        var tasks = new List<QualificationProblem>();
        foreach (QualificationProblem q in catalog)
        {
            if (q.Id.StartsWith("H", StringComparison.Ordinal) || q.Id == "R2")
            {
                tasks.Add(q);
            }
        }

        if (tasks.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("# REAL_SYSTEM_REPORT: integrators on the real system (Appendix B, tier H)");
        sb.AppendLine();
        sb.AppendLine("Generated from `matrix.csv`, `nasa_validation.csv` and `real_system.csv` of this run;");
        sb.AppendLine("no hand edits. The ranking uses only the certified reference of the SAME model");
        sb.AppendLine("(TASK.md B1). The Horizons residual is a reference-only column.");
        sb.AppendLine();
        sb.AppendLine("**Production-интегратор игры в этот отчёт не входит.**");
        sb.AppendLine();

        List<QualificationModelError> models = QualificationModelErrors(Path.Combine(dir, "nasa_validation.csv"));
        List<QualificationRealRow> real = QualificationReadRealSystem(Path.Combine(dir, "real_system.csv"));

        sb.AppendLine("## E_model from layer V (`nasa_validation.csv`)");
        sb.AppendLine();
        sb.AppendLine("| task | window | model | E_model rms8_end (km) | E_model Moon 10 y (km) |");
        sb.AppendLine("|---|---|---|---:|---:|");
        foreach (QualificationProblem q in tasks)
        {
            QualificationModelError match = QualificationModelFor(q, models);
            sb.Append("| ").Append(q.Id).Append(" | ")
                .Append(match == null ? "n/a" : match.Window).Append(" | ")
                .Append(match == null ? "n/a" : match.Model).Append(" | ")
                .Append(match == null ? "n/a" : QualificationRealKm(match.Rms8Km)).Append(" | ")
                .Append(match == null ? "n/a" : QualificationRealKm(match.MoonKm)).AppendLine(" |");
        }

        List<string> goals = QualificationGoalNamesInOrder(matrix);
        sb.AppendLine();
        sb.AppendLine("## Goals by task (M and Q)");
        sb.AppendLine();
        sb.AppendLine("| task | goal | winner | price | second | margin | excluded N/A | excluded FAIL |");
        sb.AppendLine("|---|---|---|---:|---|---:|---|---|");
        foreach (QualificationProblem q in tasks)
        {
            foreach (string goal in goals)
            {
                if (!QualificationGoalPresent(matrix, q.Id, goal))
                {
                    continue;
                }

                string winner;
                string second;
                string margin;
                string na;
                string fail;
                double price;
                QualificationScenarioBest(matrix, q.Id, goal, out winner, out price, out second,
                    out margin, out na, out fail);
                sb.Append("| ").Append(q.Id).Append(" | ").Append(goal).Append(" | ")
                    .Append(winner).Append(" | ")
                    .Append(double.IsFinite(price) ? QualificationFormat(price) : "—").Append(" | ")
                    .Append(second).Append(" | ").Append(margin).Append(" | ")
                    .Append(na).Append(" | ").Append(fail).AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Integrator against the model (same-model error at the certifying control)");
        sb.AppendLine();
        sb.AppendLine("| task | goal | winner | model error (km) | method error (km) | ratio |");
        sb.AppendLine("|---|---|---|---:|---:|---:|");
        foreach (QualificationProblem q in tasks)
        {
            QualificationModelError match = QualificationModelFor(q, models);
            foreach (string goal in goals)
            {
                if (!QualificationGoalPresent(matrix, q.Id, goal))
                {
                    continue;
                }

                string winner;
                string second;
                string margin;
                string na;
                string fail;
                double price;
                QualificationScenarioBest(matrix, q.Id, goal, out winner, out price, out second,
                    out margin, out na, out fail);
                QualificationRealRow row = QualificationRealFind(real, q.Id, winner, goal);
                double modelKm = match == null ? double.NaN : match.Rms8Km;
                double errKm = row == null || !double.IsFinite(row.Err) ? double.NaN : row.Err / 1000d;
                sb.Append("| ").Append(q.Id).Append(" | ").Append(goal).Append(" | ")
                    .Append(winner).Append(" | ")
                    .Append(double.IsFinite(modelKm) ? QualificationRealKm(modelKm) : "n/a").Append(" | ")
                    .Append(double.IsFinite(errKm) ? QualificationRealKm(errKm) : "n/a").Append(" | ")
                    .Append(double.IsFinite(modelKm) && double.IsFinite(errKm) && errKm > 0d
                        ? (modelKm / errKm).ToString("E2", QualificationCatalogInv) + "×"
                        : "n/a")
                    .AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Reference column vs Horizons — mixes model and integrator error, never ranks");
        sb.AppendLine();
        sb.AppendLine("| task | goal | winner | err_vs_horizons_rms8 (km) |");
        sb.AppendLine("|---|---|---|---:|");
        foreach (QualificationProblem q in tasks)
        {
            foreach (string goal in goals)
            {
                if (!QualificationGoalPresent(matrix, q.Id, goal))
                {
                    continue;
                }

                string winner;
                string second;
                string margin;
                string na;
                string fail;
                double price;
                QualificationScenarioBest(matrix, q.Id, goal, out winner, out price, out second,
                    out margin, out na, out fail);
                QualificationRealRow row = QualificationRealFind(real, q.Id, winner, goal);
                sb.Append("| ").Append(q.Id).Append(" | ").Append(goal).Append(" | ")
                    .Append(winner).Append(" | ")
                    .Append(row == null || !double.IsFinite(row.Horizons)
                        ? "n/a"
                        : QualificationRealKm(row.Horizons / 1000d))
                    .AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Class summary `real-system` (TASK.md §11)");
        sb.AppendLine();
        sb.AppendLine("A class of fewer than two tasks is reported as a fact about one task, not as a");
        sb.AppendLine("class conclusion (TASK.md §11, rule 2).");
        sb.AppendLine();
        sb.AppendLine("| goal | tasks | winner | second | margin | excluded N/A | excluded FAIL |");
        sb.AppendLine("|---|---|---|---|---:|---|---|");
        var ids = new List<string>();
        foreach (QualificationProblem q in tasks)
        {
            ids.Add(q.Id);
        }

        foreach (string goal in goals)
        {
            var withGoal = new List<string>();
            foreach (string id in ids)
            {
                if (QualificationGoalPresent(matrix, id, goal))
                {
                    withGoal.Add(id);
                }
            }

            if (withGoal.Count == 0)
            {
                continue;
            }

            if (withGoal.Count < 2)
            {
                sb.Append("| ").Append(goal).Append(" | ").Append(string.Join(", ", withGoal))
                    .Append(" | not proven (one task) | — | — | — | — |").AppendLine();
                continue;
            }

            string winner;
            string second;
            string margin;
            string na;
            string fail;
            QualificationClassBest(matrix, withGoal, goal, out winner, out second, out margin,
                out na, out fail);
            sb.Append("| ").Append(goal).Append(" | ").Append(string.Join(", ", withGoal))
                .Append(" | ").Append(winner).Append(" | ").Append(second).Append(" | ")
                .Append(margin).Append(" | ").Append(na).Append(" | ").Append(fail)
                .AppendLine(" |");
        }

        File.WriteAllText(Path.Combine(dir, "REAL_SYSTEM_REPORT.md"), sb.ToString(),
            new UTF8Encoding(false));
        log.WriteLine("reports: REAL_SYSTEM_REPORT.md");
    }

    private static string QualificationRealKm(double value)
    {
        return double.IsNaN(value) ? "n/a" : value.ToString("F3", QualificationCatalogInv);
    }

    private static QualificationModelError QualificationModelFor(
        QualificationProblem q, List<QualificationModelError> models)
    {
        if (string.IsNullOrEmpty(q.EpochWindow) || string.IsNullOrEmpty(q.ModelName))
        {
            return null;
        }

        string model = q.ModelName;
        if (model == "full409")
        {
            model = "409small+J2+EJ2";
        }

        foreach (QualificationModelError entry in models)
        {
            if (string.Equals(entry.Window, q.EpochWindow, StringComparison.Ordinal)
                && string.Equals(entry.Model, model, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>rms8_end and geocentric Moon at 10 years, recomputed from nasa_validation.csv.</summary>
    private static List<QualificationModelError> QualificationModelErrors(string path)
    {
        var result = new List<QualificationModelError>();
        List<string[]> rows = QualificationReadCsv(path);
        for (int i = 1; i < rows.Count; i++)
        {
            string[] f = rows[i];
            if (f.Length < 6)
            {
                continue;
            }

            string window = f[0];
            string model = f[1];
            string body = f[2];
            double days = QualificationParseDouble(f[3]);
            double dp = QualificationParseDouble(f[4]);
            if (Math.Abs(days - 3650d) > 1e-9d || !double.IsFinite(dp))
            {
                continue;
            }

            QualificationModelError entry = null;
            foreach (QualificationModelError candidate in result)
            {
                if (candidate.Window == window && candidate.Model == model)
                {
                    entry = candidate;
                    break;
                }
            }

            if (entry == null)
            {
                entry = new QualificationModelError { Window = window, Model = model };
                result.Add(entry);
            }

            if (body == "Moon")
            {
                entry.MoonKm = dp;
            }
            else
            {
                entry.Rms8Km = double.IsNaN(entry.Rms8Km)
                    ? dp * dp
                    : entry.Rms8Km + (dp * dp);
            }
        }

        foreach (QualificationModelError entry in result)
        {
            // The accumulation above is a sum of squares over the eight planets.
            entry.Rms8Km = double.IsNaN(entry.Rms8Km) ? double.NaN : Math.Sqrt(entry.Rms8Km / 8d);
        }

        return result;
    }

    private static List<QualificationRealRow> QualificationReadRealSystem(string path)
    {
        var rows = new List<QualificationRealRow>();
        foreach (string[] f in QualificationReadCsv(path))
        {
            if (f.Length < 8 || f[0] == "problem_id")
            {
                continue;
            }

            rows.Add(new QualificationRealRow
            {
                Problem = f[0],
                Method = f[1],
                Tier = f[2],
                Control = QualificationParseDouble(f[3]),
                Err = QualificationParseDouble(f[4]),
                Rms8 = QualificationParseDouble(f[5]),
                Moon = QualificationParseDouble(f[6]),
                Horizons = QualificationParseDouble(f[7])
            });
        }

        return rows;
    }

    private static QualificationRealRow QualificationRealFind(
        List<QualificationRealRow> rows, string problem, string method, string tier)
    {
        foreach (QualificationRealRow row in rows)
        {
            if (row.Problem == problem && row.Method == method && row.Tier == tier)
            {
                return row;
            }
        }

        return null;
    }
}
