using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace P1bStudy.Framework
{
    /// <summary>Cost (and time) needed to reach one error target, with an explicit status so a grid edge is never presented as a result.</summary>
    internal sealed class StudyCell
    {
        /// <summary>OK = bracketed and interpolated; EDGE = even the coarsest point is below the target (cost is an upper bound);
        /// NR = not reached; SKIP = target below the reference floor margin; NA = method not valid for this problem.</summary>
        public string Status = "NR";
        public double Cost = double.NaN;
        public double Ms = double.NaN;
        public string Note = "";
    }

    internal static class StudyReport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void FinalizeCosts(List<StudyRow> rows, double keplerWeight, double rhsMultiplier)
        {
            foreach (StudyRow r in rows)
            {
                r.Cost = (r.Calls * rhsMultiplier) + (keplerWeight * r.Aux);
            }
        }

        /// <summary>
        /// Cost to reach `target`, by log-log interpolation between the two bracketing points of the
        /// lower-left (cost, error) envelope of one integrator variant. No extrapolation is ever done.
        /// </summary>
        public static StudyCell CostAtTarget(List<StudyRow> variantRows, double target)
        {
            var pts = new List<StudyRow>();
            foreach (StudyRow r in variantRows)
            {
                if (r.Status == "ok" && r.Finite && r.Cost > 0d)
                {
                    pts.Add(r);
                }
            }

            var cell = new StudyCell();
            if (pts.Count == 0)
            {
                cell.Status = "NR";
                cell.Note = "no finite point";
                return cell;
            }

            pts.Sort(delegate (StudyRow a, StudyRow b) { return a.Cost.CompareTo(b.Cost); });
            var env = new List<StudyRow>();
            double bestErr = double.PositiveInfinity;
            foreach (StudyRow r in pts)
            {
                if (r.Err < bestErr)
                {
                    env.Add(r);
                    bestErr = r.Err;
                }
            }

            if (env[0].Err <= target)
            {
                cell.Status = "EDGE";
                cell.Cost = env[0].Cost;
                cell.Ms = env[0].Ms;
                cell.Note = "coarsest tested point already below target";
                return cell;
            }

            for (int i = 0; i + 1 < env.Count; i++)
            {
                StudyRow a = env[i];
                StudyRow b = env[i + 1];
                if (a.Err > target && b.Err <= target)
                {
                    double la = Math.Log(Math.Max(a.Err, 1e-300d));
                    double lb = Math.Log(Math.Max(b.Err, 1e-300d));
                    double lt = Math.Log(Math.Max(target, 1e-300d));
                    double f = la == lb ? 1d : (la - lt) / (la - lb);
                    cell.Status = "OK";
                    cell.Cost = Math.Exp(Math.Log(a.Cost) + (f * (Math.Log(b.Cost) - Math.Log(a.Cost))));
                    if (a.Ms > 0d && b.Ms > 0d)
                    {
                        cell.Ms = Math.Exp(Math.Log(a.Ms) + (f * (Math.Log(b.Ms) - Math.Log(a.Ms))));
                    }

                    return cell;
                }
            }

            cell.Status = "NR";
            cell.Note = "finest error " + env[env.Count - 1].Err.ToString("E2", Inv);
            return cell;
        }

        public static List<string> Keys(List<StudyRow> rows)
        {
            var keys = new List<string>();
            foreach (StudyRow r in rows)
            {
                if (!keys.Contains(r.Key))
                {
                    keys.Add(r.Key);
                }
            }

            return keys;
        }

        public static List<StudyRow> RowsOf(List<StudyRow> rows, string key)
        {
            var list = new List<StudyRow>();
            foreach (StudyRow r in rows)
            {
                if (r.Key == key)
                {
                    list.Add(r);
                }
            }

            return list;
        }

        public static StudyCell CellFor(StudyScenarioResult res, string key, StudyTarget target, StudyOptions o)
        {
            List<StudyRow> rows = RowsOf(res.Rows, key);
            var cell = new StudyCell();
            if (rows.Count > 0 && rows[0].Status == "unsupported")
            {
                cell.Status = "NA";
                cell.Note = rows[0].Note;
                return cell;
            }

            double floor = double.IsNaN(res.Problem.Floor) ? 0d : res.Problem.Floor;
            if (target.Value < o.FloorMargin * floor)
            {
                cell.Status = "SKIP";
                cell.Note = "below reference floor";
                return cell;
            }

            return CostAtTarget(rows, target.Value);
        }

        /// <summary>Per-variant findings that make a number untrustworthy. Every entry is a sentence a person can act on.</summary>
        public static List<string> Diagnose(StudyScenarioResult res)
        {
            var notes = new List<string>();
            StudyProblem p = res.Problem;
            double floor = double.IsNaN(p.Floor) ? 0d : p.Floor;
            if (!p.Certified || double.IsNaN(p.Floor))
            {
                notes.Add("REFERENCE NOT CERTIFIED: every number of this scenario is provisional (no reference floor).");
            }

            foreach (string key in Keys(res.Rows))
            {
                List<StudyRow> rows = RowsOf(res.Rows, key);
                if (rows[0].Status == "unsupported")
                {
                    continue;
                }

                int censored = 0;
                int errors = 0;
                int nonFinite = 0;
                string firstError = "";
                int capBound = 0;
                foreach (StudyRow r in rows)
                {
                    if (r.Status == "censored")
                    {
                        censored++;
                    }

                    if (r.Status == "error")
                    {
                        errors++;
                        if (firstError.Length == 0)
                        {
                            firstError = r.Note;
                        }
                    }

                    if (r.Status == "ok" && !r.Finite)
                    {
                        nonFinite++;
                    }

                    if (r.Kind == StudyKind.Adaptive && r.Steps > 0 && r.CapLimited * 2 >= r.Steps)
                    {
                        capBound++;
                    }
                }

                if (censored > 0)
                {
                    notes.Add(key + ": " + censored.ToString(Inv) + " run(s) hit the RHS budget (cost not reached = censored, not 'expensive').");
                }

                if (errors > 0)
                {
                    notes.Add(key + ": " + errors.ToString(Inv) + " run(s) failed: " + firstError);
                }

                if (nonFinite > 0)
                {
                    notes.Add(key + ": " + nonFinite.ToString(Inv)
                        + " run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.");
                }

                if (capBound > 0)
                {
                    notes.Add(key + ": CAP-BOUND in " + capBound.ToString(Inv) + " run(s); the step cap, not the tolerance, sets the cost there.");
                }

                if (rows[0].Kind == StudyKind.Fixed)
                {
                    DiagnoseFixed(key, rows, p, floor, notes);
                }
            }

            return notes;
        }

        private static void DiagnoseFixed(string key, List<StudyRow> rows, StudyProblem p, double floor, List<string> notes)
        {
            var pts = new List<StudyRow>();
            foreach (StudyRow r in rows)
            {
                if (r.Status == "ok" && r.Finite)
                {
                    pts.Add(r);
                }
            }

            pts.Sort(delegate (StudyRow a, StudyRow b) { return b.Control.CompareTo(a.Control); });
            var slopes = new List<double>();
            int grows = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                StudyRow a = pts[i];
                StudyRow b = pts[i + 1];
                if (!(b.Control < a.Control))
                {
                    continue;
                }

                bool usable = a.Err > 30d * floor && b.Err > 30d * floor
                    && a.Err < 0.05d * p.ScaleLength && b.Err < 0.05d * p.ScaleLength;
                if (b.Err > 2d * a.Err && b.Err > 3d * floor && a.Err < 0.05d * p.ScaleLength)
                {
                    grows++;
                }

                if (usable && b.Err < a.Err)
                {
                    slopes.Add(Math.Log(a.Err / b.Err) / Math.Log(a.Control / b.Control));
                }
            }

            if (grows > 0)
            {
                notes.Add(key + ": error GROWS as h shrinks in " + grows.ToString(Inv)
                    + " place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').");
            }

            double expected = rows[0].ExpectedOrder;
            if (slopes.Count >= 2 && !double.IsNaN(expected))
            {
                slopes.Sort();
                double med = slopes[slopes.Count / 2];
                if (med < expected - 1d)
                {
                    notes.Add(key + ": observed order " + med.ToString("F2", Inv) + " vs expected "
                        + expected.ToString("F0", Inv) + " (" + slopes.Count.ToString(Inv) + " pairs): check the implementation or the regime.");
                }
            }
        }

        // -------------------------------------------------------------------------------
        // Tables
        // -------------------------------------------------------------------------------

        public static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "-";
            }

            if (v >= 1e6 || (v != 0d && v < 1e-2))
            {
                return v.ToString("E2", Inv);
            }

            return v.ToString("N0", Inv);
        }

        public static string CellText(StudyCell c)
        {
            switch (c.Status)
            {
                case "OK":
                    return Num(c.Cost) + (double.IsNaN(c.Ms) ? "" : " (" + c.Ms.ToString("F0", Inv) + " ms)");
                case "EDGE":
                    return "<=" + Num(c.Cost) + " EDGE";
                case "NA":
                    return "n/a";
                case "SKIP":
                    return "skip";
                default:
                    // Keep the best achieved error visible: on the planet sweep the
                    // uncapped adaptive branch misses 100 m but does reach hundreds of
                    // metres, which is a real work-precision point, not "no data".
                    if (c.Note != null && c.Note.StartsWith("finest error ", StringComparison.Ordinal))
                    {
                        return "not reached (best " + c.Note.Substring("finest error ".Length) + ")";
                    }

                    return "not reached";
            }
        }

        /// <summary>
        /// Winner among OK cells, or an EDGE cell when its upper bound is already
        /// cheaper than every bracketed cell: in that case the method is definitely
        /// cheaper, so naming the bracketed cell and calling it "unresolved" hid a
        /// real winner (the lunar flyby at 10 m). `unresolved` is kept in the
        /// signature for callers, but a cheaper EDGE cell is now a resolved answer.
        /// </summary>
        public static string Winner(StudyScenarioResult res, StudyTarget target, StudyOptions o, out bool unresolved)
        {
            unresolved = false;
            string bestKey = null;
            string bestEdgeKey = null;
            double bestCost = double.PositiveInfinity;
            double bestEdge = double.PositiveInfinity;
            foreach (string key in Keys(res.Rows))
            {
                StudyCell c = CellFor(res, key, target, o);
                if (c.Status == "OK" && c.Cost < bestCost)
                {
                    bestCost = c.Cost;
                    bestKey = key;
                }
                else if (c.Status == "EDGE" && c.Cost < bestEdge)
                {
                    bestEdge = c.Cost;
                    bestEdgeKey = key;
                }
            }

            if (bestEdgeKey != null && bestEdge < bestCost)
            {
                return bestEdgeKey + " (<= " + Num(bestEdge) + ") EDGE";
            }

            if (bestKey == null)
            {
                return "-";
            }

            return bestKey + " (" + Num(bestCost) + ")";
        }

        public static void Print(TextWriter w, StudyScenarioResult res, StudyOptions o)
        {
            StudyProblem p = res.Problem;
            w.WriteLine();
            w.WriteLine("=== " + p.Label + " ===");
            w.WriteLine("reference floor: " + (double.IsNaN(p.Floor) ? "NOT CERTIFIED" : p.Floor.ToString("E2", Inv) + " m")
                + "; Kepler-solve weight: " + res.KeplerWeight.ToString("F2", Inv) + " RHS"
                + "; RHS multiplier: " + res.RhsMultiplier.ToString("G", Inv)
                + "; build: " + (StudyCalibration.IsDebugBuild ? "DEBUG (ms not representative)" : "Release"));
            List<StudyTarget> targets = o.Targets(p);
            var header = new StringBuilder();
            header.Append("integrator".PadRight(30));
            foreach (StudyTarget t in targets)
            {
                header.Append(t.Label.PadRight(28));
            }

            w.WriteLine(header.ToString());
            foreach (string key in Keys(res.Rows))
            {
                var line = new StringBuilder();
                line.Append(key.PadRight(30));
                foreach (StudyTarget t in targets)
                {
                    line.Append(CellText(CellFor(res, key, t, o)).PadRight(28));
                }

                w.WriteLine(line.ToString());
            }

            foreach (StudyTarget t in targets)
            {
                bool unresolved;
                string win = Winner(res, t, o, out unresolved);
                w.WriteLine("  winner @ " + t.Label + ": " + win + (unresolved ? "  [UNRESOLVED: an EDGE cell may be cheaper]" : ""));
            }

            foreach (string note in Diagnose(res))
            {
                w.WriteLine("  ! " + note);
            }
        }

        public static void WriteCsv(string path, List<StudyScenarioResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("scenario,integrator,variant,config,control,status,finite,err_m,err_final_m,err_rel,calls,aux_ops,cost_rhs_eq,steps,rejected,cap_limited,ms,floor_m,certified,note");
            foreach (StudyScenarioResult res in results)
            {
                foreach (StudyRow r in res.Rows)
                {
                    sb.Append(Csv(r.Scenario)).Append(',')
                        .Append(Csv(r.Integrator)).Append(',')
                        .Append(Csv(r.Variant)).Append(',')
                        .Append(Csv(r.Config)).Append(',')
                        .Append(r.Control.ToString("R", Inv)).Append(',')
                        .Append(r.Status).Append(',')
                        .Append(r.Finite ? "1" : "0").Append(',')
                        .Append(r.Err.ToString("R", Inv)).Append(',')
                        .Append(r.ErrFinal.ToString("R", Inv)).Append(',')
                        .Append((r.Err / res.Problem.ScaleLength).ToString("R", Inv)).Append(',')
                        .Append(r.Calls.ToString(Inv)).Append(',')
                        .Append(r.Aux.ToString(Inv)).Append(',')
                        .Append(r.Cost.ToString("R", Inv)).Append(',')
                        .Append(r.Steps.ToString(Inv)).Append(',')
                        .Append(r.Rejected.ToString(Inv)).Append(',')
                        .Append(r.CapLimited.ToString(Inv)).Append(',')
                        .Append(r.Ms.ToString("R", Inv)).Append(',')
                        .Append(res.Problem.Floor.ToString("R", Inv)).Append(',')
                        .Append(res.Problem.Certified ? "1" : "0").Append(',')
                        .Append(Csv(r.Note)).AppendLine();
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
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

        public static void WriteMarkdown(string path, List<StudyScenarioResult> results, StudyOptions o)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Integrator comparison");
            sb.AppendLine();
            sb.AppendLine("Cost = RHS-equivalent calls needed to reach the error target (log-log interpolation between the two bracketing runs; never extrapolated).");
            sb.AppendLine("`EDGE` = the coarsest tested point was already below the target (cost is an upper bound, not a result). `not reached` = the finest run (or the RHS budget) stayed above the target; the parenthesised value is the best error it did reach. `n/a` = method not valid for this problem.");
            sb.AppendLine("Build: " + (StudyCalibration.IsDebugBuild ? "**DEBUG - ms are not representative**" : "Release") + ". Per-run RHS budget: " + o.MaxCostRhs.ToString("N0", Inv) + ".");
            sb.AppendLine();
            sb.AppendLine("## Who wins where");
            sb.AppendLine();
            if (results.Count > 0)
            {
                List<string> summary = o.SummaryLabels();
                sb.Append("| scenario |");
                foreach (string label in summary)
                {
                    sb.Append(' ').Append(label.Replace("|", "/")).Append(" |");
                }

                sb.AppendLine();
                sb.Append("|---|");
                foreach (string label in summary)
                {
                    sb.Append("---|");
                }

                sb.AppendLine();
                foreach (StudyScenarioResult res in results)
                {
                    sb.Append("| ").Append(res.Problem.Label).Append(res.Problem.Certified ? "" : " (ref. NOT certified)").Append(" |");
                    foreach (StudyTarget t in o.Targets(res.Problem))
                    {
                        bool unresolved;
                        string win = Winner(res, t, o, out unresolved);
                        sb.Append(' ').Append(win).Append(unresolved ? " **unresolved**" : "").Append(" |");
                    }

                    sb.AppendLine();
                }
            }

            foreach (StudyScenarioResult res in results)
            {
                StudyProblem p = res.Problem;
                sb.AppendLine();
                sb.AppendLine("## " + p.Label);
                sb.AppendLine();
                sb.AppendLine("Reference floor: " + (double.IsNaN(p.Floor) ? "not certified" : p.Floor.ToString("E2", Inv) + " m")
                    + ". Kepler-solve weight: " + res.KeplerWeight.ToString("F2", Inv) + " RHS. RHS multiplier: "
                    + res.RhsMultiplier.ToString("G", Inv) + ".");
                sb.AppendLine();
                List<StudyTarget> targets = o.Targets(p);
                sb.Append("| integrator |");
                foreach (StudyTarget t in targets)
                {
                    sb.Append(' ').Append(t.Label.Replace("|", "/")).Append(" |");
                }

                sb.AppendLine();
                sb.Append("|---|");
                foreach (StudyTarget t in targets)
                {
                    sb.Append("---|");
                }

                sb.AppendLine();
                foreach (string key in Keys(res.Rows))
                {
                    sb.Append("| ").Append(key).Append(" |");
                    foreach (StudyTarget t in targets)
                    {
                        sb.Append(' ').Append(CellText(CellFor(res, key, t, o))).Append(" |");
                    }

                    sb.AppendLine();
                }

                List<string> notes = Diagnose(res);
                if (notes.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Findings:");
                    foreach (string n in notes)
                    {
                        sb.AppendLine("- " + n);
                    }
                }
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
