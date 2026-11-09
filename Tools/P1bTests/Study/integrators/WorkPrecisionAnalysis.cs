using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal static partial class P1bTests
{
    private const string WorkPrecisionHeader =
        "test,scenario,method,variant,control,err_m,cost_rhs_eq,steps,rejected,status,finite";

    private sealed class WorkPoint
    {
        public string Test;
        public string Scenario;
        public string Method;
        public string Variant;
        public double Control;
        public double Err;
        public double Cost;
        public long Steps;
        public long Rejected;
        public string Status;
        public bool Finite;
    }

    private sealed class WorkCurve
    {
        public string Key;
        public List<WorkPoint> Points = new List<WorkPoint>();
    }

    private sealed class WorkCross
    {
        public string Scenario;
        public double Error;
        public string From;
        public string To;
        public string Note;
    }

    private static int Test556_IntegratorWorkPrecisionCrossovers()
    {
        Console.WriteLine("--- Integrator framework: work-precision curves and crossovers (CSV replay) ---");
        string dir = StudyShipMatrixResultsDir();
        var tests = new[]
        {
            new { Name = "Test552", File = "Test552_FrameworkShipSweep.csv" },
            new { Name = "Test553", File = "Test553_FrameworkPlanetSweep.csv" },
            new { Name = "Test554", File = "Test554_FrameworkOrbitEdgeCases.csv" },
            new { Name = "Test559", File = "Test559_PureGravity.csv" }
        };

        var all = new List<WorkPoint>();
        foreach (var test in tests)
        {
            string path = Path.Combine(dir, test.File);
            if (!File.Exists(path))
            {
                Console.WriteLine("missing " + path + "; run " + test.Name + " first");
                continue;
            }

            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0 || line.StartsWith("scenario,", StringComparison.Ordinal))
                {
                    continue;
                }

                List<string> f = ParseCsv(line);
                if (f.Count < 19)
                {
                    continue;
                }

                var p = new WorkPoint
                {
                    Test = test.Name,
                    Scenario = f[0],
                    Method = f[1],
                    Variant = f[2],
                    Control = ParseDouble(f[4]),
                    Status = f[5],
                    Finite = f[6] == "1",
                    Err = ParseDouble(f[7]),
                    Cost = ParseDouble(f[12]),
                    Steps = ParseLong(f[13]),
                    Rejected = ParseLong(f[14])
                };
                if (p.Status == "ok" && p.Finite
                    && double.IsFinite(p.Err) && p.Err > 0d
                    && double.IsFinite(p.Cost) && p.Cost > 0d)
                {
                    all.Add(p);
                }
            }
        }

        string combined = Path.Combine(dir, "Test_ALL_WorkPrecision.csv");
        var csv = new StringBuilder();
        csv.AppendLine(WorkPrecisionHeader);
        foreach (WorkPoint p in all)
        {
            csv.Append(p.Test).Append(',')
                .Append(Escape(p.Scenario)).Append(',')
                .Append(Escape(p.Method)).Append(',')
                .Append(Escape(p.Variant)).Append(',')
                .Append(p.Control.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(p.Err.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(p.Cost.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(p.Steps.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(p.Rejected.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(p.Status).Append(',')
                .Append(p.Finite ? "1" : "0").AppendLine();
        }

        File.WriteAllText(combined, csv.ToString());

        var crossovers = new List<WorkCross>();
        foreach (string testName in new[] { "Test552", "Test553", "Test554", "Test559" })
        {
            var scenarios = new List<string>();
            foreach (WorkPoint p in all)
            {
                if (p.Test == testName && !scenarios.Contains(p.Scenario))
                {
                    scenarios.Add(p.Scenario);
                }
            }

            foreach (string scenario in scenarios)
            {
                var curves = new List<WorkCurve>();
                var byKey = new Dictionary<string, WorkCurve>(StringComparer.Ordinal);
                foreach (WorkPoint p in all)
                {
                    if (p.Test != testName || p.Scenario != scenario || p.Variant.Length != 0)
                    {
                        continue;
                    }

                    string key = p.Variant.Length == 0
                        ? p.Method
                        : p.Method + " [" + p.Variant + "]";
                    WorkCurve c;
                    if (!byKey.TryGetValue(key, out c))
                    {
                        c = new WorkCurve { Key = key };
                        byKey.Add(key, c);
                        curves.Add(c);
                    }

                    c.Points.Add(p);
                }

                foreach (WorkCurve c in curves)
                {
                    c.Points.Sort(delegate (WorkPoint a, WorkPoint b)
                    {
                        int e = a.Err.CompareTo(b.Err);
                        return e != 0 ? e : a.Cost.CompareTo(b.Cost);
                    });
                }

                // Log grid from 10 km down to 1 micron; the actual crossovers are
                // refined by bisection below. The grid only has to bracket changes.
                const int grid = 320;
                double previousError = double.NaN;
                string previousWinner = null;
                for (int i = 0; i <= grid; i++)
                {
                    double error = Math.Pow(10d, 4d - (10d * i / grid));
                    string winner;
                    bool edge;
                    double cost;
                    string note;
                    if (!WorkWinner(curves, error, out winner, out cost, out edge, out note))
                    {
                        previousError = error;
                        previousWinner = null;
                        continue;
                    }

                    if (previousWinner != null && winner != previousWinner
                        && !double.IsNaN(previousError))
                    {
                        double lo = Math.Min(previousError, error);
                        double hi = Math.Max(previousError, error);
                        string from = previousWinner;
                        string to = winner;
                        // Bisect on log10(error) until the crossing is sharp.
                        for (int k = 0; k < 48; k++)
                        {
                            double mid = Math.Sqrt(lo * hi);
                            string w;
                            bool e2;
                            double c2;
                            string n2;
                            if (!WorkWinner(curves, mid, out w, out c2, out e2, out n2))
                            {
                                break;
                            }

                            if (w == from)
                            {
                                hi = mid;
                            }
                            else
                            {
                                lo = mid;
                            }
                        }

                        bool crossingEdge;
                        double crossingCost;
                        string crossingNote;
                        WorkWinner(curves, Math.Sqrt(lo * hi), out to, out crossingCost, out crossingEdge, out crossingNote);
                        if (string.IsNullOrEmpty(to) || to == from)
                        {
                            previousError = error;
                            previousWinner = winner;
                            continue;
                        }

                        double crossingError = Math.Sqrt(lo * hi);
                        bool duplicate = false;
                        foreach (WorkCross existing in crossovers)
                        {
                            if (existing.Scenario == scenario
                                && Math.Abs(existing.Error - crossingError)
                                    <= 1e-3d * Math.Max(1d, Math.Abs(crossingError))
                                && existing.From == from && existing.To == to)
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (duplicate)
                        {
                            previousError = error;
                            previousWinner = winner;
                            continue;
                        }

                        crossovers.Add(new WorkCross
                        {
                            Scenario = scenario,
                            Error = crossingError,
                            From = from,
                            To = to,
                            Note = crossingEdge ? "EDGE" : (crossingNote ?? "")
                        });
                    }

                    previousError = error;
                    previousWinner = winner;
                }
            }
        }

        var md = new StringBuilder();
        md.AppendLine("# Work-precision crossovers");
        md.AppendLine();
        md.AppendLine("Replayed from the existing `Test552/553/554` CSVs; no integrator run was performed here.");
        md.AppendLine("A crossover is the error where the cheapest method changes. `EDGE` means one side is an upper bound (the cheapest tested point is already below the target); treat that crossing as provisional.");
        md.AppendLine();
        md.AppendLine("| scenario | error, m | from | to | note |");
        md.AppendLine("|---|---|---|---|---|");
        crossovers.Sort(delegate (WorkCross a, WorkCross b)
        {
            int s = string.CompareOrdinal(a.Scenario, b.Scenario);
            return s != 0 ? s : b.Error.CompareTo(a.Error);
        });
        foreach (WorkCross c in crossovers)
        {
            md.Append('|').Append(Escape(c.Scenario)).Append('|')
                .Append(c.Error.ToString("E3", CultureInfo.InvariantCulture)).Append('|')
                .Append(Escape(c.From)).Append('|')
                .Append(Escape(c.To)).Append('|')
                .Append(Escape(c.Note)).AppendLine("|");
        }

        md.AppendLine();
        md.AppendLine("## Where the 6th-order advantage ends");
        md.AppendLine();
        md.AppendLine("Rows where a WH method leaves the lead (or takes it) are the practical boundary of the Kepler-split regime.");
        md.AppendLine();
        md.AppendLine("| scenario | error, m | from | to |");
        md.AppendLine("|---|---|---|---|");
        foreach (WorkCross c in crossovers)
        {
            bool whFrom = c.From.IndexOf("WH-", StringComparison.Ordinal) >= 0;
            bool whTo = c.To.IndexOf("WH-", StringComparison.Ordinal) >= 0;
            if (whFrom != whTo)
            {
                md.Append('|').Append(Escape(c.Scenario)).Append('|')
                    .Append(c.Error.ToString("E3", CultureInfo.InvariantCulture)).Append('|')
                    .Append(Escape(c.From)).Append('|')
                    .Append(Escape(c.To)).AppendLine("|");
            }
        }

        string mdPath = Path.Combine(dir, "Test_ALL_WorkPrecisionCrossovers.md");
        File.WriteAllText(mdPath, md.ToString());

        Check(all.Count > 0, "work-precision replay found finite points",
            all.Count.ToString(CultureInfo.InvariantCulture) + " point(s)");
        Check(File.Exists(combined), "work-precision combined CSV written", combined);
        Check(File.Exists(mdPath), "work-precision crossover markdown written", mdPath);
        Console.WriteLine("combined: " + combined);
        Console.WriteLine("crossovers: " + mdPath + " (" + crossovers.Count.ToString(CultureInfo.InvariantCulture) + ")");
        return 0;
    }

    private static bool WorkWinner(
        List<WorkCurve> curves, double target,
        out string winner, out double cost, out bool edge, out string note)
    {
        winner = null;
        cost = double.PositiveInfinity;
        edge = false;
        note = "";
        foreach (WorkCurve c in curves)
        {
            if (c.Points.Count == 0)
            {
                continue;
            }

            bool isEdge;
            string pointNote;
            double value;
            if (!WorkCostAt(c, target, out value, out isEdge, out pointNote))
            {
                continue;
            }

            if (value < cost)
            {
                cost = value;
                winner = c.Key;
                edge = isEdge;
                note = pointNote;
            }
        }

        return winner != null;
    }

    private static bool WorkCostAt(
        WorkCurve c, double target, out double cost, out bool edge, out string note)
    {
        cost = double.NaN;
        edge = false;
        note = "";
        List<WorkPoint> p = c.Points;
        if (p.Count == 0)
        {
            return false;
        }

        if (target <= p[0].Err)
        {
            cost = p[0].Cost;
            edge = true;
            note = "EDGE";
            return true;
        }

        if (target > p[p.Count - 1].Err)
        {
            return false;
        }

        for (int i = 1; i < p.Count; i++)
        {
            if (p[i].Err < target)
            {
                continue;
            }

            double e0 = p[i - 1].Err;
            double e1 = p[i].Err;
            double c0 = p[i - 1].Cost;
            double c1 = p[i].Cost;
            if (!(e1 > e0) || !(c0 > 0d) || !(c1 > 0d) || !(target > 0d))
            {
                cost = Math.Min(c0, c1);
                note = "flat";
                return true;
            }

            double w = (Math.Log(target) - Math.Log(e0)) / (Math.Log(e1) - Math.Log(e0));
            w = Math.Max(0d, Math.Min(1d, w));
            cost = Math.Exp(Math.Log(c0) + (w * (Math.Log(c1) - Math.Log(c0))));
            note = "interpolated";
            return true;
        }

        return false;
    }

    private static List<string> ParseCsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                fields.Add(sb.ToString());
                sb.Length = 0;
            }
            else
            {
                sb.Append(ch);
            }
        }

        fields.Add(sb.ToString());
        return fields;
    }

    private static double ParseDouble(string s)
    {
        double v;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            ? v : double.NaN;
    }

    private static long ParseLong(string s)
    {
        long v;
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0L;
    }

    private static string Escape(string s)
    {
        return (s ?? "").Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
    }
}
