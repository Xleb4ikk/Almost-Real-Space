using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace P1bStudy.Framework
{
    /// <summary>
    /// One analysis path for every table derived from a sweep CSV. The CSV is the
    /// source of truth: the same rows feed the mode summary, the 2D maps, the final
    /// regime table and the drift ranking. No hand-built table and no second
    /// implementation of the interpolation live here.
    /// </summary>
    internal static class StudyRegimeAnalysis
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed class Scenario
        {
            public string Name;
            public string Mode;
            public string[] Tags;
            public string ReferenceFamily;
            public double Floor;
            public double ScaleLength;
            public bool Certified;
            public bool Archived;
            public bool KeplerExact;
            public List<StudyRow> Rows = new List<StudyRow>();
            public StudyScenarioResult Result;
        }

        private sealed class MethodPoint
        {
            public string Base;
            public string Key;
            public string Variant;
            public string Family;
            public int Stages;
            public bool SameFamily;
            public StudyCell Cell;
            public double CheapestCost = double.PositiveInfinity;
            public double CheapestErr = double.NaN;
        }

        private sealed class CellResult
        {
            public string Scenario;
            public string Mode;
            public double Target;
            public string Class = "no-winner";
            public string Winner = "-";
            public double WinnerCost = double.NaN;
            public int WinnerStages;
            public string WinnerFamily = "";
            public double Margin = double.NaN;
            public bool Trivial;
            public bool FloorLimited;
            public bool SameFamilyReference;
            public readonly List<string> Equivalent = new List<string>();
            public readonly List<string> FloorMethods = new List<string>();
            public readonly List<string> SameFamilyMethods = new List<string>();
            public readonly List<MethodPoint> Points = new List<MethodPoint>();
        }

        private sealed class ModeAgg
        {
            public string Mode;
            public readonly HashSet<string> Scenarios = new HashSet<string>(StringComparer.Ordinal);
            public int Cells;
            public int Decisive;
            public int Marginal;
            public int Equivalent;
            public int Trivial;
            public int FloorLimited;
            public int Unresolved;
            public int NoWinner;
            public int SameFamily;
            public int ArchivedScenarios;
            public int ExcludedScenarios;
            public readonly Dictionary<string, int> DecisiveWins = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> EquivalentAppearances = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly List<double> Margins = new List<double>();
            public readonly HashSet<string> FailedValidity = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Tags = new HashSet<string>(StringComparer.Ordinal);
        }

        private sealed class DriftEntry
        {
            public string Method;
            public double Cost;
            public double Err;
            public double Energy;
            public double Angular;
        }

        // ---------------------------------------------------------------------------
        // Entry points
        // ---------------------------------------------------------------------------

        /// <summary>Mode summary only (used by Test552/553/558): the same rules, no maps.</summary>
        public static void WriteModeSummary(
            string path, string title, List<StudyScenarioResult> results, StudyOptions options)
        {
            string csv = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + ".analysis.csv");
            StudyReport.WriteCsv(csv, results);
            WriteModeSummaryFromCsv(path, title, csv, options);
        }

        public static void WriteModeSummaryFromCsv(
            string path, string title, string csvPath, StudyOptions options)
        {
            List<Scenario> scenarios = Load(csvPath);
            var modes = Aggregate(scenarios, options);
            File.WriteAllText(path, RenderModeSummary(title, modes), new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(path) ?? ".", title + "_FinalMap.md"),
                RenderFinalMap(title, modes), new UTF8Encoding(false));
        }

        /// <summary>All Test559 tables. tieCsv is optional (the 1.15 grid rerun).</summary>
        public static void WriteAll(
            string csvPath, string outputDir, string title, StudyOptions options,
            string tieCsvPath, string[] driftCsvPaths)
        {
            List<Scenario> scenarios = Load(csvPath);
            List<Scenario> tieScenarios = string.IsNullOrEmpty(tieCsvPath) || !File.Exists(tieCsvPath)
                ? null
                : Load(tieCsvPath);
            if (tieScenarios != null)
            {
                MergeHigherResolution(scenarios, tieScenarios);
            }

            var modes = Aggregate(scenarios, options);

            File.WriteAllText(Path.Combine(outputDir, title + "_ModeSummary.md"),
                RenderModeSummary(title, modes), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outputDir, title + "_Analysis.md"),
                RenderAnalysis(title, scenarios, modes, options, tieScenarios, driftCsvPaths),
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outputDir, title + "_FinalMap.md"),
                RenderFinalMap(title, modes), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outputDir, title + "_MoonFlybyMap.md"),
                RenderMoonFlyby(title, scenarios, modes, options), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outputDir, title + "_Transitions.md"),
                RenderTransitions(title, scenarios, modes, options), new UTF8Encoding(false));
        }

        // ---------------------------------------------------------------------------
        // CSV loading
        // ---------------------------------------------------------------------------

        private static List<Scenario> Load(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }

            List<string[]> records = ParseCsv(text);
            if (records.Count == 0)
            {
                return new List<Scenario>();
            }

            string[] header = records[0];
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < header.Length; i++)
            {
                index[header[i]] = i;
            }

            var map = new Dictionary<string, Scenario>(StringComparer.Ordinal);
            var order = new List<Scenario>();
            for (int r = 1; r < records.Count; r++)
            {
                string[] f = records[r];
                if (f.Length == 0 || (f.Length == 1 && f[0].Length == 0))
                {
                    continue;
                }

                string scenarioName = Get(f, index, "scenario");
                Scenario s;
                if (!map.TryGetValue(scenarioName, out s))
                {
                    s = new Scenario
                    {
                        Name = scenarioName,
                        Mode = Get(f, index, "mode"),
                        Tags = SplitTags(Get(f, index, "mode_tags")),
                        ReferenceFamily = Get(f, index, "reference_family"),
                        Floor = GetDouble(f, index, "floor_m"),
                        ScaleLength = GetDouble(f, index, "scale_length"),
                        Certified = Get(f, index, "certified") == "1",
                        Archived = Get(f, index, "archived") == "1",
                        KeplerExact = Get(f, index, "kepler_exact") == "1"
                    };
                    if (string.IsNullOrEmpty(s.Mode))
                    {
                        s.Mode = "other";
                    }

                    if (string.IsNullOrEmpty(s.ReferenceFamily))
                    {
                        s.ReferenceFamily = "Runge-Kutta";
                    }

                    s.Result = new StudyScenarioResult();
                    s.Result.Problem = new StudyProblem
                    {
                        Label = s.Name,
                        Mode = s.Mode,
                        ModeTags = s.Tags,
                        Floor = s.Floor,
                        ScaleLength = s.ScaleLength > 0d ? s.ScaleLength : 1d,
                        Certified = s.Certified,
                        ReferenceFamily = s.ReferenceFamily
                    };
                    s.Result.Rows = s.Rows;
                    map[s.Name] = s;
                    order.Add(s);
                }

                var row = new StudyRow
                {
                    Scenario = scenarioName,
                    Integrator = Get(f, index, "integrator"),
                    Variant = Get(f, index, "variant"),
                    Config = Get(f, index, "config"),
                    Family = Get(f, index, "family"),
                    Stages = (int)GetDouble(f, index, "stages"),
                    Kind = Get(f, index, "kind") == "Adaptive" ? StudyKind.Adaptive : StudyKind.Fixed,
                    ExpectedOrder = GetDouble(f, index, "expected_order"),
                    Control = GetDouble(f, index, "control"),
                    Status = Get(f, index, "status"),
                    Finite = Get(f, index, "finite") == "1",
                    Err = GetDouble(f, index, "err_m"),
                    ErrFinal = GetDouble(f, index, "err_final_m"),
                    Calls = (long)GetDouble(f, index, "calls"),
                    Aux = (long)GetDouble(f, index, "aux_ops"),
                    Cost = GetDouble(f, index, "cost_rhs_eq"),
                    Steps = (long)GetDouble(f, index, "steps"),
                    Rejected = (long)GetDouble(f, index, "rejected"),
                    CapLimited = (long)GetDouble(f, index, "cap_limited"),
                    Ms = GetDouble(f, index, "ms"),
                    Note = Get(f, index, "note"),
                    EnergyDriftVsRef = GetDouble(f, index, "energy_drift_vs_ref"),
                    AngularDriftVsRef = GetDouble(f, index, "angular_drift_vs_ref")
                };
                s.Rows.Add(row);
            }

            return order;
        }

        private static string[] SplitTags(string text)
        {
            return string.IsNullOrEmpty(text)
                ? new string[0]
                : text.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// The 1.15-grid rerun is the authoritative measurement for the cells it
        /// covers. Replacing the rows keeps one analysis path: the mode summary,
        /// maps and final table all see the finer measurement, not a hand merge.
        /// </summary>
        private static void MergeHigherResolution(List<Scenario> main, List<Scenario> fine)
        {
            foreach (Scenario f in fine)
            {
                Scenario target = null;
                foreach (Scenario m in main)
                {
                    if (m.Name == f.Name)
                    {
                        target = m;
                        break;
                    }
                }

                if (target == null)
                {
                    continue;
                }

                target.Rows.Clear();
                target.Rows.AddRange(f.Rows);
                target.Floor = f.Floor;
                target.Certified = f.Certified;
                target.ReferenceFamily = f.ReferenceFamily;
                target.ScaleLength = f.ScaleLength;
                target.Result.Problem.Floor = f.Floor;
                target.Result.Problem.Certified = f.Certified;
                target.Result.Problem.ReferenceFamily = f.ReferenceFamily;
                target.Result.Problem.ScaleLength = f.ScaleLength > 0d ? f.ScaleLength : 1d;
            }
        }

        private static string Get(string[] fields, Dictionary<string, int> index, string name)
        {
            int i;
            return index.TryGetValue(name, out i) && i < fields.Length ? fields[i] : "";
        }

        private static double GetDouble(string[] fields, Dictionary<string, int> index, string name)
        {
            string text = Get(fields, index, name);
            double v;
            return double.TryParse(text, NumberStyles.Float, Inv, out v) ? v : double.NaN;
        }

        private static List<string[]> ParseCsv(string text)
        {
            var records = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                }
                else if (c == '\n')
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                    records.Add(fields.ToArray());
                    fields.Clear();
                }
                else if (c != '\r')
                {
                    field.Append(c);
                }
            }

            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                records.Add(fields.ToArray());
            }

            return records;
        }

        // ---------------------------------------------------------------------------
        // Cell classification
        // ---------------------------------------------------------------------------

        private static CellResult Classify(Scenario s, double targetValue, StudyOptions options)
        {
            var result = new CellResult
            {
                Scenario = s.Name,
                Mode = s.Mode,
                Target = targetValue
            };
            var target = new StudyTarget { Value = targetValue, Label = targetValue.ToString("G3", Inv) + " m" };
            var points = new List<MethodPoint>();
            var bases = new List<string>();
            foreach (string key in StudyReport.Keys(s.Rows))
            {
                StudyRow first = null;
                foreach (StudyRow r in s.Rows)
                {
                    if (r.Key == key)
                    {
                        first = r;
                        break;
                    }
                }

                if (first == null)
                {
                    continue;
                }

                StudyCell cell = StudyReport.CellFor(s.Result, key, target, options);
                if (cell.Status == "SKIP" || cell.Status == "NA" || cell.Status == "NR"
                    || cell.Status == "FLOOR")
                {
                    continue;
                }

                // A cap variant is a separate method in every summary: group by the full
                // key (integrator + variant), never by the bare integrator name.
                string baseName = first.Key;
                if (s.KeplerExact && baseName.IndexOf("Encke", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                bool same = string.Equals(first.Family, s.ReferenceFamily, StringComparison.Ordinal);
                var p = new MethodPoint
                {
                    Base = baseName,
                    Key = key,
                    Variant = first.Variant,
                    Family = first.Family,
                    Stages = first.Stages,
                    SameFamily = same,
                    Cell = cell
                };
                foreach (StudyRow r in s.Rows)
                {
                    if (r.Key == key && r.Status == "ok" && r.Finite
                        && r.Cost > 0d && r.Cost < p.CheapestCost)
                    {
                        p.CheapestCost = r.Cost;
                        p.CheapestErr = r.Err;
                    }
                }

                int at = bases.IndexOf(baseName);
                if (at < 0)
                {
                    bases.Add(baseName);
                    points.Add(p);
                }
                else if (CellCost(p) < CellCost(points[at]))
                {
                    points[at] = p;
                }
            }

            points.Sort(delegate (MethodPoint a, MethodPoint b) { return CellCost(a).CompareTo(CellCost(b)); });
            var ok = new List<MethodPoint>();
            var edge = new List<MethodPoint>();
            foreach (MethodPoint p in points)
            {
                result.Points.Add(p);
                if (p.Cell.Status == "OK")
                {
                    ok.Add(p);
                }
                else if (p.Cell.Status == "EDGE")
                {
                    edge.Add(p);
                    result.FloorMethods.Add(p.Base);
                }

                if (p.SameFamily)
                {
                    result.SameFamilyMethods.Add(p.Base);
                }
            }

            if (ok.Count == 0)
            {
                if (edge.Count > 0)
                {
                    MethodPoint bestEdge = edge[0];
                    result.Class = "floor-limited";
                    result.FloorLimited = true;
                    result.Winner = bestEdge.Base;
                    result.WinnerCost = bestEdge.Cell.Cost;
                    result.WinnerStages = bestEdge.Stages;
                    result.WinnerFamily = bestEdge.Family;
                }
                else
                {
                    result.Class = "no-winner";
                }

                return result;
            }

            MethodPoint best = ok[0];
            result.Winner = best.Base;
            result.WinnerCost = best.Cell.Cost;
            result.WinnerStages = best.Stages;
            result.WinnerFamily = best.Family;
            result.SameFamilyReference = best.SameFamily;
            result.Trivial = best.Cell.Cost < 200d;

            double second = ok.Count > 1 ? ok[1].Cell.Cost : double.PositiveInfinity;
            result.Margin = double.IsFinite(second) ? second / Math.Max(best.Cell.Cost, 1e-300d) : double.PositiveInfinity;

            bool cheaperFloor = false;
            foreach (MethodPoint p in edge)
            {
                if (p.Cell.Cost <= best.Cell.Cost * 1.5d)
                {
                    cheaperFloor = true;
                }
            }

            if (cheaperFloor)
            {
                result.Class = "floor-limited";
                result.FloorLimited = true;
                return result;
            }

            if (best.Cell.Cost < 200d)
            {
                result.Class = "trivial";
                return result;
            }

            if (result.Margin > 2d)
            {
                result.Class = best.SameFamily ? "uncertified-reference" : "decisive";
                result.Equivalent.Add(best.Base);
                return result;
            }

            // A 1.5x band is the equivalence band. Name every OK method inside it;
            // a grid-edge method is floor-limited and never enters this set.
            double limit = best.Cell.Cost * 1.5d;
            foreach (MethodPoint p in ok)
            {
                if (p.Cell.Cost <= limit)
                {
                    result.Equivalent.Add(p.Base);
                }
            }

            if (result.Equivalent.Count <= 1)
            {
                result.Equivalent.Add(best.Base);
            }

            if (result.Margin >= 1.5d)
            {
                result.Class = best.SameFamily ? "uncertified-reference" : "marginal";
            }
            else
            {
                // An equivalent set is a statement about price, not about a
                // decisive accuracy win; a same-family reference does not turn it
                // into an uncertified decisive claim.
                result.Class = "equivalent";
                result.SameFamilyReference = best.SameFamily;
            }

            return result;
        }

        private static double CellCost(MethodPoint p)
        {
            return double.IsNaN(p.Cell.Cost) || p.Cell.Cost <= 0d
                ? double.PositiveInfinity
                : p.Cell.Cost;
        }

        // ---------------------------------------------------------------------------
        // Aggregation
        // ---------------------------------------------------------------------------

        private static List<ModeAgg> Aggregate(List<Scenario> scenarios, StudyOptions options)
        {
            var modes = new Dictionary<string, ModeAgg>(StringComparer.Ordinal);
            foreach (Scenario s in scenarios)
            {
                ModeAgg agg;
                if (!modes.TryGetValue(s.Mode, out agg))
                {
                    agg = new ModeAgg { Mode = s.Mode };
                    modes.Add(s.Mode, agg);
                }

                agg.Scenarios.Add(s.Name);
                foreach (string tag in s.Tags)
                {
                    agg.Tags.Add(tag);
                }

                if (Array.IndexOf(s.Tags, "near-uniform-field") >= 0)
                {
                    agg.ExcludedScenarios++;
                    continue;
                }

                if (s.Archived)
                {
                    agg.ExcludedScenarios++;
                    continue;
                }

                if (!s.Certified)
                {
                    agg.Unresolved++;
                    continue;
                }

                foreach (StudyTarget t in options.Targets(s.Result.Problem))
                {
                    agg.Cells++;
                    CellResult c = Classify(s, t.Value, options);
                    if (double.IsFinite(c.Margin)
                        && c.Class != "trivial"
                        && c.Class != "floor-limited"
                        && c.Class != "no-winner")
                    {
                        agg.Margins.Add(c.Margin);
                    }

                    switch (c.Class)
                    {
                        case "decisive":
                            agg.Decisive++;
                            Bump(agg.DecisiveWins, c.Winner);
                            break;
                        case "marginal":
                            agg.Marginal++;
                            foreach (string m in c.Equivalent)
                            {
                                Bump(agg.EquivalentAppearances, m);
                            }

                            break;
                        case "equivalent":
                            agg.Equivalent++;
                            foreach (string m in c.Equivalent)
                            {
                                Bump(agg.EquivalentAppearances, m);
                            }

                            break;
                        case "trivial":
                            agg.Trivial++;
                            break;
                        case "floor-limited":
                            agg.FloorLimited++;
                            break;
                        case "uncertified-reference":
                            agg.SameFamily++;
                            foreach (string m in c.Equivalent)
                            {
                                Bump(agg.EquivalentAppearances, m);
                            }

                            break;
                        case "no-winner":
                            agg.NoWinner++;
                            break;
                        default:
                            agg.Unresolved++;
                            break;
                    }

                    foreach (MethodPoint p in c.Points)
                    {
                        if (p.Cell.Status == "EDGE" && p.Cell.Unresolved)
                        {
                            // counted once per cell, not per method
                        }
                    }
                }

                foreach (StudyRow row in s.Rows)
                {
                    if (row.Status == "error"
                        && (row.Note.IndexOf("Kepler", StringComparison.OrdinalIgnoreCase) >= 0
                            || row.Note.IndexOf("singular", StringComparison.OrdinalIgnoreCase) >= 0
                            || row.Note.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        agg.FailedValidity.Add(row.Integrator);
                    }
                }
            }

            var list = new List<ModeAgg>(modes.Values);
            list.Sort(delegate (ModeAgg a, ModeAgg b) { return string.CompareOrdinal(a.Mode, b.Mode); });
            return list;
        }

        private static void Bump(Dictionary<string, int> map, string key)
        {
            int n;
            map.TryGetValue(key, out n);
            map[key] = n + 1;
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return double.NaN;
            }

            var copy = new List<double>(values);
            copy.Sort();
            int mid = copy.Count / 2;
            return copy.Count % 2 == 1 ? copy[mid] : 0.5d * (copy[mid - 1] + copy[mid]);
        }

        private static string Confidence(ModeAgg agg)
        {
            double median = Median(agg.Margins);
            double bad = agg.Cells <= 0 ? 1d : (double)(agg.Trivial + agg.FloorLimited + agg.Unresolved + agg.NoWinner) / agg.Cells;
            int scenarios = agg.Scenarios.Count - agg.ExcludedScenarios;
            if (agg.Cells == 0)
            {
                return "none";
            }

            if (scenarios >= 3 && agg.Decisive >= 3 && median > 1.5d && bad < 0.25d)
            {
                return "high";
            }

            if (scenarios >= 2 && agg.Decisive >= 1 && median > 1.0d && bad < 0.60d)
            {
                return "medium";
            }

            return "low";
        }

        // ---------------------------------------------------------------------------
        // Renderers
        // ---------------------------------------------------------------------------

        private static string RenderModeSummary(string title, List<ModeAgg> modes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + title + ": mode summary");
            sb.AppendLine();
            sb.AppendLine("Generated from the sweep CSV by `StudyRegimeAnalysis`; the same rows produce the 2D maps and the final regime table. `decisive` = margin > 2, `marginal` = 1.5-2, `equivalent` = < 1.5. `floor-limited` = a cheaper grid point already met the target (EDGE) or the cheapest point is the grid minimum; such a cell is never called equivalent. `trivial` = the cheapest bracketed cost is below 200 RHS calls. `uncertified-reference` = the winner belongs to the same broad family as the reference, so it is not a decisive accuracy claim. Confidence uses the rule in `ORBIT_INTEGRATOR_TEST_SCHEME.md`.");
            sb.AppendLine();
            sb.AppendLine("| mode | scenarios | cells | decisive | marginal | equivalent | trivial | floor-limited | same-family ref | unresolved | no winner | median margin | bad share | confidence |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (ModeAgg agg in modes)
            {
                double median = Median(agg.Margins);
                double bad = agg.Cells <= 0 ? 1d : (double)(agg.Trivial + agg.FloorLimited + agg.Unresolved + agg.NoWinner) / agg.Cells;
                sb.Append('|').Append(agg.Mode).Append('|')
                    .Append(agg.Scenarios.Count.ToString(Inv)).Append('|')
                    .Append(agg.Cells.ToString(Inv)).Append('|')
                    .Append(agg.Decisive.ToString(Inv)).Append('|')
                    .Append(agg.Marginal.ToString(Inv)).Append('|')
                    .Append(agg.Equivalent.ToString(Inv)).Append('|')
                    .Append(agg.Trivial.ToString(Inv)).Append('|')
                    .Append(agg.FloorLimited.ToString(Inv)).Append('|')
                    .Append(agg.SameFamily.ToString(Inv)).Append('|')
                    .Append(agg.Unresolved.ToString(Inv)).Append('|')
                    .Append(agg.NoWinner.ToString(Inv)).Append('|')
                    .Append(Format(median)).Append('|')
                    .Append(bad.ToString("F2", Inv)).Append('|')
                    .Append(Confidence(agg)).AppendLine("|");
            }

            sb.AppendLine();
            sb.AppendLine("Confidence rule: `high` = N>=3, W>=3, median margin > 1.5, bad share (trivial+floor-limited+unresolved)/cells < 0.25; `medium` = N>=2, W>=1, median margin > 1.0, bad share < 0.60; `low` otherwise; `none` = no certified cells. `N` excludes near-uniform-field scenarios; `W` is decisive cells only.");
            return sb.ToString();
        }

        private static string RenderFinalMap(string title, List<ModeAgg> modes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + title + ": final regime map");
            sb.AppendLine();
            sb.AppendLine("One row per mode, generated from the same CSV as the mode summary. A decisive winner is named only when margin > 2 and the reference is a different family. Otherwise the union of the equivalent sets is shown. Floor-limited and uncertified cells are listed separately and never promoted.");
            sb.AppendLine();
            sb.AppendLine("| mode | scenarios | cells | decisive winner / equivalent set | failed-validity | floor-limited | uncertified-ref | trivial | N | W | median margin | bad share | confidence |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (ModeAgg agg in modes)
            {
                string winner = BestDecisive(agg);
                if (winner == "-")
                {
                    winner = BestEquivalent(agg);
                }

                double median = Median(agg.Margins);
                double bad = agg.Cells <= 0 ? 1d : (double)(agg.Trivial + agg.FloorLimited + agg.Unresolved + agg.NoWinner) / agg.Cells;
                int n = agg.Scenarios.Count - agg.ExcludedScenarios;
                sb.Append('|').Append(agg.Mode).Append('|')
                    .Append(agg.Scenarios.Count.ToString(Inv)).Append('|')
                    .Append(agg.Cells.ToString(Inv)).Append('|')
                    .Append(winner).Append('|')
                    .Append(agg.FailedValidity.Count == 0 ? "-" : string.Join(", ", agg.FailedValidity)).Append('|')
                    .Append(agg.FloorLimited.ToString(Inv)).Append('|')
                    .Append(agg.SameFamily.ToString(Inv)).Append('|')
                    .Append(agg.Trivial.ToString(Inv)).Append('|')
                    .Append(n.ToString(Inv)).Append('|')
                    .Append(agg.Decisive.ToString(Inv)).Append('|')
                    .Append(Format(median)).Append('|')
                    .Append(bad.ToString("F2", Inv)).Append('|')
                    .Append(Confidence(agg)).AppendLine("|");
            }

            return sb.ToString();
        }

        private static string BestDecisive(ModeAgg agg)
        {
            string best = "-";
            int count = 0;
            foreach (KeyValuePair<string, int> pair in agg.DecisiveWins)
            {
                if (pair.Value > count || (pair.Value == count && string.CompareOrdinal(pair.Key, best) < 0))
                {
                    best = pair.Key;
                    count = pair.Value;
                }
            }

            return count == 0 ? "-" : best + " (" + count.ToString(Inv) + ")";
        }

        private static string BestEquivalent(ModeAgg agg)
        {
            var items = new List<KeyValuePair<string, int>>(agg.EquivalentAppearances);
            items.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int c = b.Value.CompareTo(a.Value);
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            });
            var names = new List<string>();
            for (int i = 0; i < items.Count && i < 5; i++)
            {
                names.Add(items[i].Key);
            }

            return names.Count == 0 ? "-" : "eq: " + string.Join(", ", names);
        }

        private static string RenderAnalysis(
            string title, List<Scenario> scenarios, List<ModeAgg> modes, StudyOptions options,
            List<Scenario> tieScenarios, string[] driftCsvPaths)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + title + ": analysis");
            sb.AppendLine();
            sb.AppendLine("Everything below is computed from `" + title + "_PureGravity.csv` by `StudyRegimeAnalysis` (one code path). Interpolation, floor checks and tie bands are the same `StudyReport` logic used by the sweep, not a second implementation.");
            sb.AppendLine();
            sb.AppendLine("Classes: `D` decisive (margin > 2), `M` marginal (1.5-2), `E` equivalent (< 1.5, set listed), `F` floor-limited (a cheaper grid point already met the target), `T` trivial (cost < 200), `S` same-family reference (not a decisive accuracy claim), `U` unresolved/no winner.");
            sb.AppendLine();

            sb.AppendLine("## Cell table (1 m and 0.01 m)");
            sb.AppendLine();
            sb.AppendLine("| scenario | target | class | winner | cost | stages | margin | equivalent / floor-limited |");
            sb.AppendLine("|---|---|---|---|---:|---:|---|---|");
            foreach (Scenario s in scenarios)
            {
                foreach (double target in new[] { 1d, 0.01d })
                {
                    CellResult c = Classify(s, target, options);
                    string set = c.Class == "equivalent"
                        ? string.Join(", ", c.Equivalent)
                        : c.Class == "floor-limited"
                            ? "floor: " + string.Join(", ", c.FloorMethods)
                            : c.Equivalent.Count > 1
                                ? string.Join(", ", c.Equivalent)
                                : "-";
                    sb.Append("| ").Append(s.Name).Append(" | ")
                        .Append(target.ToString("G3", Inv)).Append(" m | ")
                        .Append(Letter(c.Class)).Append(" | ")
                        .Append(c.Winner).Append(" | ")
                        .Append(FormatCost(c.WinnerCost)).Append(" | ")
                        .Append(c.WinnerStages.ToString(Inv)).Append(" | ")
                        .Append(Format(c.Margin)).Append(" | ")
                        .Append(set).AppendLine(" |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## 2D maps");
            sb.AppendLine();
            RenderHeliocentricMap(sb, scenarios, options, 1d, "1 m");
            RenderHeliocentricMap(sb, scenarios, options, 0.01d, "0.01 m");
            RenderPlanetMap(sb, scenarios, options, 1d, "1 m");
            RenderPlanetMap(sb, scenarios, options, 0.01d, "0.01 m");

            sb.AppendLine("## Floor-limited detail");
            sb.AppendLine();
            sb.AppendLine("For every method in a floor-limited/equivalent cell: cost and error at its cheapest tested grid point, and whether that point already met the target. A method whose cheapest point is the grid minimum cannot be priced lower without a genuinely coarser run; it is not an equivalent method.");
            sb.AppendLine();
            sb.AppendLine("| scenario | target | method | cheapest cost | error at cheapest | meets target | class |");
            sb.AppendLine("|---|---|---|---:|---:|---|---|");
            foreach (Scenario s in scenarios)
            {
                foreach (double target in new[] { 1d, 0.01d })
                {
                    CellResult c = Classify(s, target, options);
                    if (c.Class != "floor-limited" && c.Class != "equivalent")
                    {
                        continue;
                    }

                    foreach (MethodPoint p in c.Points)
                    {
                        bool meets = double.IsFinite(p.CheapestErr) && p.CheapestErr <= target;
                        sb.Append("| ").Append(s.Name).Append(" | ")
                            .Append(target.ToString("G3", Inv)).Append(" m | ")
                            .Append(p.Base).Append(" | ")
                            .Append(FormatCost(p.CheapestCost)).Append(" | ")
                            .Append(Format(p.CheapestErr)).Append(" | ")
                            .Append(meets ? "yes" : "no").Append(" | ")
                            .Append(p.Cell.Status).AppendLine(" |");
                    }
                }
            }

            if (tieScenarios != null && tieScenarios.Count > 0)
            {
                sb.AppendLine("## Tie rerun at grid ratio 1.15");
                sb.AppendLine();
                sb.AppendLine("The cells below were rerun with a 1.15 control grid (instead of 1.5). If the margin stays below 1.5, the methods are **неразличимы по цене**; no winner is named.");
                sb.AppendLine();
                sb.AppendLine("| scenario | target | class | first | second | margin | equivalent set |");
                sb.AppendLine("|---|---|---|---|---|---:|---|");
                foreach (Scenario s in tieScenarios)
                {
                    foreach (double target in new[] { 1d, 0.01d })
                    {
                        CellResult c = Classify(s, target, options);
                        string second = c.Equivalent.Count > 1 ? c.Equivalent[1] : "-";
                        sb.Append("| ").Append(s.Name).Append(" | ")
                            .Append(target.ToString("G3", Inv)).Append(" m | ")
                            .Append(c.Class).Append(" | ")
                            .Append(c.Winner).Append(" | ")
                            .Append(second).Append(" | ")
                            .Append(Format(c.Margin)).Append(" | ")
                            .Append(string.Join(", ", c.Equivalent)).AppendLine(" |");
                    }
                }
            }

            sb.AppendLine("## WH invalid (failed validity)");
            sb.AppendLine();
            sb.AppendLine("Only rows whose run aborted in the Kepler drift are listed. `h<0` is a negative *substep* of the BM14A composition, not a negative outer step; the outer step is positive. After the Kepler solver fallback fix this section is expected to be empty.");
            sb.AppendLine();
            sb.AppendLine("| scenario | method | config | note |");
            sb.AppendLine("|---|---|---|---|");
            int whRows = 0;
            foreach (Scenario s in scenarios)
            {
                foreach (StudyRow r in s.Rows)
                {
                    if (r.Status == "error"
                        && (r.Integrator.IndexOf("WH", StringComparison.Ordinal) >= 0
                            || r.Integrator.IndexOf("Kepler", StringComparison.Ordinal) >= 0)
                        && r.Note.IndexOf("Kepler", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        whRows++;
                        sb.Append("| ").Append(s.Name).Append(" | ")
                            .Append(r.Integrator).Append(" | ")
                            .Append(r.Config).Append(" | ")
                            .Append(r.Note.Replace("|", "/")).AppendLine(" |");
                    }
                }
            }

            if (whRows == 0)
            {
                sb.AppendLine("| — | — | — | no Kepler-drift abort in this run |");
            }

            sb.AppendLine();
            sb.AppendLine("## Energy and angular-momentum drift at equal RHS budgets");
            sb.AppendLine();
            sb.AppendLine("For each scenario and budget, the run of each method with the largest cost inside `[0.5 x budget, budget]`. Runs that stop well below the budget are not compared as if they had spent it; a method with no run in that band is omitted. `dE` and `dL` are the maximum absolute changes over the checkpoints; for J2, `dE` uses the J2 potential. Values are only comparable within one row of the table.");
            sb.AppendLine();
            if (driftCsvPaths != null && driftCsvPaths.Length > 0)
            {
                RenderDrift(sb, driftCsvPaths);
            }
            else
            {
                sb.AppendLine("No drift CSVs supplied.");
            }

            return sb.ToString();
        }

        private static string Letter(string cls)
        {
            switch (cls)
            {
                case "decisive": return "D";
                case "marginal": return "M";
                case "equivalent": return "E";
                case "floor-limited": return "F";
                case "trivial": return "T";
                case "uncertified-reference": return "S";
                default: return "U";
            }
        }

        private static string Format(double v)
        {
            if (double.IsNaN(v))
            {
                return "-";
            }

            if (double.IsInfinity(v))
            {
                return "inf";
            }

            return v.ToString("F2", Inv);
        }

        private static string FormatCost(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "-";
            }

            return v >= 1e5 ? v.ToString("E2", Inv) : v.ToString("N0", Inv);
        }

        private static void RenderHeliocentricMap(
            StringBuilder sb, List<Scenario> scenarios, StudyOptions options, double target, string label)
        {
            var values = new Dictionary<string, CellResult>(StringComparer.Ordinal);
            var rows = new List<double>();
            var cols = new List<double>();
            foreach (Scenario s in scenarios)
            {
                if (s.Mode != "heliocentric-orbit")
                {
                    continue;
                }

                double a = TagValue(s.Tags, "a-au=");
                double e = TagValue(s.Tags, "e=");
                if (!double.IsFinite(a) || !double.IsFinite(e))
                {
                    continue;
                }

                if (!rows.Contains(a)) rows.Add(a);
                if (!cols.Contains(e)) cols.Add(e);
                values[Key(a, e)] = Classify(s, target, options);
            }

            if (values.Count == 0)
            {
                return;
            }

            rows.Sort();
            cols.Sort();
            sb.AppendLine("### Heliocentric a x e — " + label);
            sb.AppendLine();
            sb.Append("| a \\ e |");
            foreach (double e in cols) sb.Append(' ').Append(e.ToString("G", Inv)).Append(" |");
            sb.AppendLine();
            sb.Append("|---|");
            foreach (double e in cols) sb.Append("---|");
            sb.AppendLine();
            foreach (double a in rows)
            {
                sb.Append("| ").Append(a.ToString("G", Inv)).Append(" |");
                foreach (double e in cols)
                {
                    CellResult c;
                    sb.Append(' ').Append(values.TryGetValue(Key(a, e), out c) ? CellText(c) : "—").Append(" |");
                }

                sb.AppendLine();
            }

            sb.AppendLine();
        }

        private static void RenderPlanetMap(
            StringBuilder sb, List<Scenario> scenarios, StudyOptions options, double target, string label)
        {
            var values = new Dictionary<string, CellResult>(StringComparer.Ordinal);
            var rows = new List<double>();
            var cols = new List<double>();
            foreach (Scenario s in scenarios)
            {
                if (s.Mode != "planet-flyby")
                {
                    continue;
                }

                double rp = TagValue(s.Tags, "periapsis-radii=");
                double vinf = TagValue(s.Tags, "vinf=");
                if (!double.IsFinite(rp) || !double.IsFinite(vinf))
                {
                    continue;
                }

                if (!rows.Contains(rp)) rows.Add(rp);
                if (!cols.Contains(vinf)) cols.Add(vinf);
                values[Key(rp, vinf)] = Classify(s, target, options);
            }

            if (values.Count == 0)
            {
                return;
            }

            rows.Sort();
            cols.Sort();
            sb.AppendLine("### Planet flyby periapsis x v_inf — " + label);
            sb.AppendLine();
            sb.Append("| R \\ v_inf |");
            foreach (double v in cols) sb.Append(' ').Append(v.ToString("G", Inv)).Append(" |");
            sb.AppendLine();
            sb.Append("|---|");
            foreach (double v in cols) sb.Append("---|");
            sb.AppendLine();
            foreach (double rp in rows)
            {
                sb.Append("| ").Append(rp.ToString("G", Inv)).Append(" |");
                foreach (double v in cols)
                {
                    CellResult c;
                    sb.Append(' ').Append(values.TryGetValue(Key(rp, v), out c) ? CellText(c) : "—").Append(" |");
                }

                sb.AppendLine();
            }

            sb.AppendLine();
        }

        private static string CellText(CellResult c)
        {
            string text = c.Winner;
            if (c.Class == "decisive" || c.Class == "marginal")
            {
                text += " " + Letter(c.Class) + " " + Format(c.Margin);
            }
            else if (c.Class == "equivalent")
            {
                text = string.Join(" ~ ", c.Equivalent) + " E";
            }
            else if (c.Class == "floor-limited")
            {
                text = "floor: " + string.Join(", ", c.FloorMethods);
            }
            else if (c.Class == "uncertified-reference")
            {
                text = c.Winner + " S";
            }
            else if (c.Class == "trivial")
            {
                text = c.Winner + " T (" + c.WinnerStages.ToString(Inv) + " st)";
            }
            else
            {
                text = "—";
            }

            return text;
        }

        private static string RenderMoonFlyby(
            string title, List<Scenario> scenarios, List<ModeAgg> modes, StudyOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + title + ": Moon flyby map");
            sb.AppendLine();
            sb.AppendLine("Generated from the same CSV. `D`/`M` show margin; `E` is an equivalent set; `F` is floor-limited; `S` is a same-family reference; `T` is trivial.");
            sb.AppendLine();
            foreach (double target in new[] { 1d, 0.01d })
            {
                var values = new Dictionary<string, CellResult>(StringComparer.Ordinal);
                var rows = new List<double>();
                var cols = new List<double>();
                foreach (Scenario s in scenarios)
                {
                    if (s.Mode != "moon-flyby")
                    {
                        continue;
                    }

                    double vinf = TagValue(s.Tags, "vinf=");
                    double alt = TagValue(s.Tags, "periapsis-km=");
                    if (!double.IsFinite(vinf) || !double.IsFinite(alt))
                    {
                        continue;
                    }

                    if (!rows.Contains(vinf)) rows.Add(vinf);
                    if (!cols.Contains(alt)) cols.Add(alt);
                    values[Key(vinf, alt)] = Classify(s, target, options);
                }

                rows.Sort();
                cols.Sort();
                sb.AppendLine("## " + target.ToString("G3", Inv) + " m");
                sb.AppendLine();
                sb.Append("| v_inf \\ alt km |");
                foreach (double alt in cols) sb.Append(' ').Append(alt.ToString("G", Inv)).Append(" |");
                sb.AppendLine();
                sb.Append("|---|");
                foreach (double alt in cols) sb.Append("---|");
                sb.AppendLine();
                foreach (double vinf in rows)
                {
                    sb.Append("| ").Append(vinf.ToString("G", Inv)).Append(" |");
                    foreach (double alt in cols)
                    {
                        CellResult c;
                        sb.Append(' ').Append(values.TryGetValue(Key(vinf, alt), out c) ? CellText(c) : "—").Append(" |");
                    }

                    sb.AppendLine();
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string RenderTransitions(
            string title, List<Scenario> scenarios, List<ModeAgg> modes, StudyOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + title + ": regime transitions");
            sb.AppendLine();
            sb.AppendLine("Generated from the same CSV at 1 m. A transition is listed only when the *method* changes; a change between two equivalent cells is not a change of winner.");
            sb.AppendLine();
            foreach (string mode in new[] { "moon-orbit", "moon-flyby", "heliocentric-orbit", "planet-flyby", "small-body-flyby" })
            {
                var list = new List<Scenario>();
                foreach (Scenario s in scenarios)
                {
                    if (s.Mode == mode) list.Add(s);
                }

                if (list.Count == 0)
                {
                    continue;
                }

                sb.AppendLine("## " + mode);
                sb.AppendLine();
                sb.AppendLine("| axis | from | to | first | second |");
                sb.AppendLine("|---|---|---|---|---|");
                if (mode == "moon-flyby")
                {
                    TransitionAxis(sb, list, options, "vinf=", "v_inf");
                    TransitionAxis(sb, list, options, "periapsis-km=", "periapsis-km");
                }
                else if (mode == "heliocentric-orbit")
                {
                    TransitionAxis(sb, list, options, "e=", "eccentricity");
                    TransitionAxis(sb, list, options, "a-au=", "a-AU");
                }
                else if (mode == "planet-flyby")
                {
                    TransitionAxis(sb, list, options, "periapsis-radii=", "periapsis-radii");
                    TransitionAxis(sb, list, options, "vinf=", "vinf");
                }
                else if (mode == "small-body-flyby")
                {
                    TransitionAxis(sb, list, options, "vinf=", "vinf");
                }
                else
                {
                    TransitionAxis(sb, list, options, "distance-radii=", "distance-radii");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void TransitionAxis(
            StringBuilder sb, List<Scenario> scenarios, StudyOptions options, string tag, string axis)
        {
            var byValue = new SortedDictionary<double, CellResult>();
            foreach (Scenario s in scenarios)
            {
                double v = TagValue(s.Tags, tag);
                if (!double.IsFinite(v))
                {
                    continue;
                }

                byValue[v] = Classify(s, 1d, options);
            }

            double previousValue = double.NaN;
            CellResult previous = null;
            foreach (KeyValuePair<double, CellResult> pair in byValue)
            {
                if (previous != null && previous.Winner != pair.Value.Winner
                    && (previous.Class == "decisive" || previous.Class == "marginal")
                    && (pair.Value.Class == "decisive" || pair.Value.Class == "marginal"))
                {
                    sb.Append('|').Append(axis).Append('|')
                        .Append(previousValue.ToString("G", Inv)).Append('|')
                        .Append(pair.Key.ToString("G", Inv)).Append('|')
                        .Append(previous.Winner).Append(" (").Append(Letter(previous.Class)).Append(")|")
                        .Append(pair.Value.Winner).Append(" (").Append(Letter(pair.Value.Class)).AppendLine(")|");
                }

                previousValue = pair.Key;
                previous = pair.Value;
            }
        }

        private static double TagValue(string[] tags, string prefix)
        {
            foreach (string tag in tags)
            {
                if (tag.StartsWith(prefix, StringComparison.Ordinal))
                {
                    double v;
                    if (double.TryParse(tag.Substring(prefix.Length), NumberStyles.Float, Inv, out v))
                    {
                        return v;
                    }
                }
            }

            return double.NaN;
        }

        private static string Key(double a, double b)
        {
            return a.ToString("R", Inv) + "/" + b.ToString("R", Inv);
        }

        // ---------------------------------------------------------------------------
        // Drift ranking
        // ---------------------------------------------------------------------------

        private static void RenderDrift(StringBuilder sb, string[] csvPaths)
        {
            var wanted = new[]
            {
                "Moon orbit 100 km x1000 orbits",
                "Heliocentric 1 AU e 0.5",
                "Earth LEO 400 km, 30 d",
                "J2 x1 inc 98"
            };
            foreach (string path in csvPaths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                List<Scenario> scenarios = Load(path);
                foreach (string scenarioName in wanted)
                {
                    Scenario s = null;
                    foreach (Scenario candidate in scenarios)
                    {
                        if (candidate.Name == scenarioName)
                        {
                            s = candidate;
                            break;
                        }
                    }

                    if (s == null)
                    {
                        continue;
                    }

                    foreach (long budget in new[] { 100000L, 1000000L })
                    {
                        var entries = new List<DriftEntry>();
                        var methods = new List<string>();
                        foreach (StudyRow r in s.Rows)
                        {
                            if (!methods.Contains(r.Integrator))
                            {
                                methods.Add(r.Key);
                            }
                        }

                        foreach (string method in methods)
                        {
                            StudyRow best = null;
                            foreach (StudyRow r in s.Rows)
                            {
                                if (r.Key != method || r.Status != "ok" || !r.Finite
                                    || r.Cost < 0.5d * budget || r.Cost > budget
                                    || double.IsNaN(r.EnergyDriftVsRef))
                                {
                                    continue;
                                }

                                if (best == null || r.Cost > best.Cost
                                    || (Math.Abs(r.Cost - best.Cost) < 1e-9d && r.Err < best.Err))
                                {
                                    best = r;
                                }
                            }

                            if (best != null)
                            {
                                entries.Add(new DriftEntry
                                {
                                    Method = method,
                                    Cost = best.Cost,
                                    Err = best.Err,
                                    Energy = best.EnergyDriftVsRef,
                                    Angular = best.AngularDriftVsRef
                                });
                            }
                        }

                        entries.Sort(delegate (DriftEntry a, DriftEntry b) { return a.Energy.CompareTo(b.Energy); });
                        sb.AppendLine("### " + scenarioName + " — budget " + budget.ToString("N0", Inv) + " RHS");
                        sb.AppendLine();
                        sb.AppendLine("| rank | method | cost | dE | dL | position error |");
                        sb.AppendLine("|---:|---|---:|---:|---:|---:|");
                        for (int i = 0; i < entries.Count; i++)
                        {
                            DriftEntry e = entries[i];
                            sb.Append("| ").Append((i + 1).ToString(Inv)).Append(" | ")
                                .Append(e.Method).Append(" | ")
                                .Append(FormatCost(e.Cost)).Append(" | ")
                                .Append(FormatSci(e.Energy)).Append(" | ")
                                .Append(FormatSci(e.Angular)).Append(" | ")
                                .Append(FormatSci(e.Err)).AppendLine(" |");
                        }

                        sb.AppendLine();
                    }
                }
            }
        }

        private static string FormatSci(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? "-" : v.ToString("E2", Inv);
        }
    }
}
