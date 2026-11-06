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
        /// <summary>OK = cheapest executed run at or below the target; EDGE = even the cheapest (coarsest) run is
        /// below the target, so the cost is an upper bound; NR = not reached; SKIP = target below the reference
        /// floor margin; NA = method not valid for this problem.</summary>
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
        /// Cost of the cheapest actually executed run whose measured error is at or below `target`.
        /// No interpolation and no extrapolation: the number is work that was really spent, so a
        /// non-smooth error-vs-control curve cannot make an adaptive method look cheaper than it ran.
        /// EDGE means even the cheapest (coarsest) tested run already met the target, so the true cost
        /// is an upper bound and a coarser run might be cheaper.
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
            StudyRow cheapest = pts[0];
            StudyRow bestBelow = null;
            StudyRow finest = cheapest;
            for (int i = 0; i < pts.Count; i++)
            {
                StudyRow r = pts[i];
                if (r.Err < finest.Err)
                {
                    finest = r;
                }

                if (bestBelow == null && r.Err <= target)
                {
                    bestBelow = r;
                }
            }

            if (bestBelow == null)
            {
                cell.Status = "NR";
                cell.Note = "finest error " + finest.Err.ToString("E2", Inv);
                return cell;
            }

            cell.Cost = bestBelow.Cost;
            cell.Ms = bestBelow.Ms;
            cell.Status = ReferenceEquals(bestBelow, cheapest) ? "EDGE" : "OK";
            cell.Note = ReferenceEquals(bestBelow, cheapest)
                ? "cheapest tested point already below target"
                : "cheapest executed point at or below target";
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
        public static List<string> Diagnose(StudyScenarioResult res, StudyOptions o)
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

            DiagnoseAdaptiveCap(res, notes);
            DiagnoseTies(res, o, notes);
            DiagnoseRoundoff(res, o, notes);
            return notes;
        }

        /// <summary>
        /// A capped variant that is dramatically better than its uncapped twin is a
        /// result in itself: the controller does not survive the horizon without the
        /// cap, so the cap is load-bearing and must not be read as a cosmetic config.
        /// </summary>
        private static void DiagnoseAdaptiveCap(StudyScenarioResult res, List<string> notes)
        {
            var bases = new List<string>();
            foreach (StudyRow r in res.Rows)
            {
                if (r.Kind == StudyKind.Adaptive && !bases.Contains(r.Integrator))
                {
                    bases.Add(r.Integrator);
                }
            }

            foreach (string baseName in bases)
            {
                double uncapped = double.PositiveInfinity;
                bool uncappedFinite = false;
                bool uncappedSeen = false;
                string capVariant = null;
                double capped = double.PositiveInfinity;
                foreach (StudyRow r in res.Rows)
                {
                    if (r.Integrator != baseName || r.Status != "ok")
                    {
                        continue;
                    }

                    if (r.Variant.Length == 0)
                    {
                        uncappedSeen = true;
                        if (r.Finite)
                        {
                            uncappedFinite = true;
                            uncapped = Math.Min(uncapped, r.Err);
                        }
                    }
                    else if (r.Finite && r.Err < capped)
                    {
                        capped = r.Err;
                        capVariant = r.Key;
                    }
                }

                if (capVariant == null || !double.IsFinite(capped))
                {
                    continue;
                }

                double limit = 100d * Math.Max(capped, 1e-30d);
                bool uncappedBad = uncappedSeen && (!uncappedFinite || uncapped > limit);
                if (uncappedBad)
                {
                    notes.Add(baseName + ": the uncapped branch "
                        + (uncappedFinite
                            ? "reached only " + uncapped.ToString("E2", Inv) + " m"
                            : "produced no finite samples")
                        + " while " + capVariant + " reached " + capped.ToString("E2", Inv)
                        + " m; the step cap is load-bearing on this horizon.");
                }
            }
        }

        private static string BaseKey(string key)
        {
            int i = key.IndexOf(" [", StringComparison.Ordinal);
            return i >= 0 ? key.Substring(0, i) : key;
        }

        private static void DiagnoseTies(StudyScenarioResult res, StudyOptions o, List<string> notes)
        {
            foreach (StudyTarget target in o.Targets(res.Problem))
            {
                var baseKeys = new List<string>();
                var keys = new List<string>();
                var costs = new List<double>();
                double best = double.PositiveInfinity;
                foreach (string key in Keys(res.Rows))
                {
                    StudyCell c = CellFor(res, key, target, o);
                    if (c.Status != "OK")
                    {
                        continue;
                    }

                    string baseKey = BaseKey(key);
                    int at = baseKeys.IndexOf(baseKey);
                    if (at < 0)
                    {
                        baseKeys.Add(baseKey);
                        keys.Add(key);
                        costs.Add(c.Cost);
                    }
                    else if (c.Cost < costs[at])
                    {
                        keys[at] = key;
                        costs[at] = c.Cost;
                    }

                    if (c.Cost < best)
                    {
                        best = c.Cost;
                    }
                }

                int ties = 0;
                string detail = "";
                double limit = best * Math.Max(1d, o.TieFactor);
                for (int i = 0; i < keys.Count; i++)
                {
                    if (costs[i] <= limit)
                    {
                        ties++;
                        if (detail.Length < 160)
                        {
                            detail += (detail.Length == 0 ? "" : ", ")
                                + keys[i] + " (" + Num(costs[i]) + ")";
                        }
                    }
                }

                if (ties > 1)
                {
                    notes.Add("TIE at " + target.Label + ": " + detail
                        + " are within " + ((o.TieFactor - 1d) * 100d).ToString("F0", Inv)
                        + "%; do not read the ranking as resolved.");
                }
            }
        }

        private const double UnitRoundoff = 16d * 2.220446049250313e-16;

        /// <summary>
        /// A long integration at a large distance is limited by double-precision accumulation,
        /// not by the method's order. The estimate uses the state scale and the largest step
        /// count in the table: when the tightest target sits at that scale, the column measures
        /// summation quality (e.g. compensated Y6C), and the report must say so instead of
        /// implying that every other method converges too slowly.
        /// </summary>
        private static void DiagnoseRoundoff(StudyScenarioResult res, StudyOptions o, List<string> notes)
        {
            StudyProblem p = res.Problem;
            long maxSteps = 0L;
            foreach (StudyRow r in res.Rows)
            {
                if (r.Status == "ok" && r.Finite && r.Steps > maxSteps)
                {
                    maxSteps = r.Steps;
                }
            }

            if (maxSteps < 1L)
            {
                return;
            }

            double estimate = UnitRoundoff * Math.Max(p.ScaleLength, 1d) * Math.Sqrt(maxSteps);
            if (!(estimate > 0d) || double.IsNaN(estimate) || double.IsInfinity(estimate))
            {
                return;
            }

            double tightest = double.PositiveInfinity;
            double floor = double.IsNaN(p.Floor) ? 0d : p.Floor;
            foreach (StudyTarget target in o.Targets(p))
            {
                if (target.Value >= o.FloorMargin * floor && target.Value < tightest)
                {
                    tightest = target.Value;
                }
            }

            if (!(tightest < 10d * estimate))
            {
                return;
            }

            notes.Add("double-precision round-off estimate " + estimate.ToString("E2", Inv)
                + " m (16 eps * scale * sqrt(max steps=" + maxSteps.ToString("N0", Inv)
                + ")); the tightest target " + tightest.ToString("E2", Inv)
                + " m is at that scale, so that column measures summation quality, not method order.");
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
        /// OK cells within TieFactor of the best are appended with "~": on a flat
        /// work-precision curve a 3 % difference is measurement noise, not a ranking.
        /// </summary>
        public static string Winner(StudyScenarioResult res, StudyTarget target, StudyOptions o, out bool unresolved)
        {
            unresolved = false;
            var okKeys = new List<string>();
            var okCosts = new List<double>();
            string bestEdgeKey = null;
            double bestEdge = double.PositiveInfinity;
            foreach (string key in Keys(res.Rows))
            {
                StudyCell c = CellFor(res, key, target, o);
                if (c.Status == "OK")
                {
                    okKeys.Add(key);
                    okCosts.Add(c.Cost);
                }
                else if (c.Status == "EDGE" && c.Cost < bestEdge)
                {
                    bestEdge = c.Cost;
                    bestEdgeKey = key;
                }
            }

            string bestKey = null;
            double bestCost = double.PositiveInfinity;
            for (int i = 0; i < okKeys.Count; i++)
            {
                if (okCosts[i] < bestCost)
                {
                    bestCost = okCosts[i];
                    bestKey = okKeys[i];
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

            string text = bestKey + " (" + Num(bestCost) + ")";
            double tieLimit = bestCost * Math.Max(1d, o.TieFactor);
            var tieBases = new List<string>();
            var tieKeys = new List<string>();
            var tieCosts = new List<double>();
            for (int i = 0; i < okKeys.Count; i++)
            {
                string baseKey = BaseKey(okKeys[i]);
                if (okKeys[i] == bestKey || baseKey == BaseKey(bestKey))
                {
                    continue;
                }

                if (okCosts[i] <= tieLimit)
                {
                    int at = tieBases.IndexOf(baseKey);
                    if (at < 0)
                    {
                        tieBases.Add(baseKey);
                        tieKeys.Add(okKeys[i]);
                        tieCosts.Add(okCosts[i]);
                    }
                    else if (okCosts[i] < tieCosts[at])
                    {
                        tieKeys[at] = okKeys[i];
                        tieCosts[at] = okCosts[i];
                    }
                }
            }

            int ties = 0;
            for (int i = 0; i < tieKeys.Count && ties < 3; i++)
            {
                text += " ~ " + tieKeys[i] + " (" + Num(tieCosts[i]) + ")";
                ties++;
            }

            if (ties > 0)
            {
                text += " [tie";
                if (ties > 1)
                {
                    text += "s";
                }

                text += " within " + ((o.TieFactor - 1d) * 100d).ToString("F0", Inv) + "%]";
            }

            return text;
        }

        public static void Print(TextWriter w, StudyScenarioResult res, StudyOptions o)
        {
            StudyProblem p = res.Problem;
            w.WriteLine();
            w.WriteLine("=== " + p.Label + " ===");
            w.WriteLine("reference floor: " + (double.IsNaN(p.Floor) ? "NOT CERTIFIED" : p.Floor.ToString("E2", Inv) + " m")
                + "; Kepler-solve weight: " + res.KeplerWeight.ToString("F2", Inv) + " RHS"
                + "; RHS multiplier: " + res.RhsMultiplier.ToString("G", Inv)
                + "; RHS budget: " + res.MaxCostRhs.ToString("N0", Inv)
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

            foreach (string note in Diagnose(res, o))
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
            sb.AppendLine("Cost = RHS-equivalent calls of the cheapest actually executed run whose measured error is at or below the target (no interpolation, never extrapolated).");
            sb.AppendLine("`EDGE` = even the cheapest tested point was already below the target (cost is an upper bound, not a result). `~` marks methods within " + ((o.TieFactor - 1d) * 100d).ToString("F0", Inv) + "% of the winner: that ranking is not resolved. `not reached` = the finest run (or the RHS budget) stayed above the target; the parenthesised value is the best error it did reach. `n/a` = method not valid for this problem.");
            sb.AppendLine("Build: " + (StudyCalibration.IsDebugBuild ? "**DEBUG - ms are not representative**" : "Release") + ". Per-run RHS budget: per scenario (see section headers).");
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
                    + res.RhsMultiplier.ToString("G", Inv) + ". RHS budget: "
                    + res.MaxCostRhs.ToString("N0", Inv) + ".");
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

                List<string> notes = Diagnose(res, o);
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
