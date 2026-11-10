using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using P1bStudy.Framework;
using Galilego.Core;

// Test560b output: all cells, method coverage, decision table (cost_total),
// dominance audit and isolation. Everything numeric is read back from CSV.
internal static partial class P1bTests
{
    private sealed class DecisionBMatrixRow
    {
        public string Scenario = "";
        public string Regime = "";
        public string Config = "";
        public string Method = "";
        public string MethodClass = "";
        public double MaxStep = double.NaN;
        public double ConfigMaxStep = double.NaN;
        public string Chunk = "";
        public double Target;
        public string TargetKind = "";
        public string Control = "";
        public string ControlKind = "";
        public double ErrMaxM = double.NaN;
        public double ErrFinalM = double.NaN;
        public double ErrMaxV = double.NaN;
        public double CostRhs = double.NaN;
        public double CostLocalFrame = double.NaN;
        public double CostAux = double.NaN;
        public double CostTotal = double.NaN;
        public double CostIfPorted = double.NaN;
        public double EdgeCostTotal = double.NaN;
        public double EdgeCostIfPorted = double.NaN;
        public double EdgeErrMaxM = double.NaN;
        public double EdgeControl = double.NaN;
        public string EdgeStatus = "none";
        public double CostMs = double.NaN;
        public double MsMedian = double.NaN;
        public double MsSpreadPct = double.NaN;
        public long GcBytes;
        public long Steps;
        public long Accepted;
        public long Rejected;
        public long Chunks;
        public long RawRhs;
        public long AuxOps;
        public double FloorM = double.NaN;
        public double FloorV = double.NaN;
        public double TargetOverFloor = double.NaN;
        public string Status = "";
        public string Confidence = "";
        public string Note = "";
        public string ReferenceMethod = "";
        public string ReferenceResolution = "";
        public double EventTargetS = double.NaN;
        public double EventErrS = double.NaN;
        public double EventFloorS = double.NaN;
        public string EventStatus = "";
        public bool Sensitivity;
    }

    private sealed class DecisionBCoverageRow
    {
        public string Regime = "";
        public string Scenario = "";
        public string Config = "";
        public string Method = "";
        public string MethodClass = "";
        public string Status = "";
        public string Reason = "";
        public double MaxStep = double.NaN;
        public int Runs;
    }

    private sealed class DecisionBTableRow
    {
        public string Regime = "";
        public string Scenario = "";
        public string Config = "";
        public double Target;
        public string TargetKind = "";
        public string BestAll = "";
        public string BestAllClass = "";
        public double BestAllCost = double.NaN;
        public string BestProduction = "";
        public double BestProductionCost = double.NaN;
        public string BestPorted = "";
        public double BestPortedCost = double.NaN;
        public string BestStandRaw = "";
        public double BestStandRawCost = double.NaN;
        public double ProductionVsBestAllPct = double.NaN;
        public double BestAllVsProductionPct = double.NaN;
        public double ProductionVsStandRawPct = double.NaN;
        /// <summary>Cost of the margin-selected ok run that defines best_all.</summary>
        public double SelectedBestAllTotal = double.NaN;
        /// <summary>Cost of best_production (comparable; production already pays the local frame).</summary>
        public double SelectedProductionTotal = double.NaN;
        public string SelectedProductionMethod = "";
        public string CheapestEdgeMethod = "";
        public double CheapestEdgeTotal = double.NaN;
        public string EdgePolicy = "";
        public string ProductionConfidence = "";
        public string Acceptable = "";
        public string Na = "";
        public string Unresolved = "";
        public string NotRun = "";
        public string Confidence = "";
        public string Why = "";
    }

    private static List<string> DecisionBCsvSplit(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
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
        return fields;
    }

    private static double DecisionBParseDouble(string s)
    {
        double v;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            ? v
            : double.NaN;
    }

    private static string DecisionBResultsDir()
    {
        string dir = Path.Combine(StudyShipMatrixResultsDir(), "decision");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string DecisionBCsvField(string value)
    {
        if (value == null)
        {
            return "";
        }

        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string DecisionBCsvNumber(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? ""
            : value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string DecisionBCsvLong(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string DecisionBFormatTarget(double value, string kind)
    {
        if (kind == "velocity")
        {
            return value.ToString("G6", CultureInfo.InvariantCulture) + " m/s";
        }

        if (value >= 1000d)
        {
            return (value / 1000d).ToString("G6", CultureInfo.InvariantCulture) + " km";
        }

        return value.ToString("G6", CultureInfo.InvariantCulture) + " m";
    }

    private static void DecisionBWriteRunsCsv(string path, List<DecisionBMethodResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario,regime,config,method,method_class,max_step,control,control_kind,status,"
            + "err_max_m,err_final_m,err_max_v,cost_rhs,cost_local_frame,cost_aux,cost_total,"
            + "cost_if_ported,cost_ms,gc_bytes,steps,accepted,rejected,chunks,raw_rhs,aux_ops,note,sensitivity");
        foreach (DecisionBMethodResult mr in results)
        {
            foreach (DecisionBRun r in mr.Runs)
            {
                sb.Append(DecisionBCsvField(mr.Scenario.Label)).Append(',')
                    .Append(DecisionBCsvField(mr.Scenario.Regime)).Append(',')
                    .Append(DecisionBCsvField(mr.Scenario.Config)).Append(',')
                    .Append(DecisionBCsvField(mr.Method.Name)).Append(',')
                    .Append(DecisionBCsvField(mr.Method.Class)).Append(',')
                    .Append(DecisionBCsvNumber(mr.EffectiveMaxStep)).Append(',')
                    .Append(DecisionBCsvNumber(r.Control)).Append(',')
                    .Append(DecisionBCsvField(r.ControlKind)).Append(',')
                    .Append(DecisionBCsvField(r.Status)).Append(',')
                    .Append(DecisionBCsvNumber(r.ErrMaxM)).Append(',')
                    .Append(DecisionBCsvNumber(r.ErrFinalM)).Append(',')
                    .Append(DecisionBCsvNumber(r.ErrMaxV)).Append(',')
                    .Append(DecisionBCsvNumber(r.CostRhs)).Append(',')
                    .Append(DecisionBCsvNumber(r.CostLocalFrame)).Append(',')
                    .Append(DecisionBCsvNumber(r.CostAux)).Append(',')
                    .Append(DecisionBCsvNumber(r.CostTotal)).Append(',')
                    .Append(DecisionBCsvNumber(r.CostIfPorted)).Append(',')
                    .Append(DecisionBCsvNumber(r.Ms)).Append(',')
                    .Append(DecisionBCsvLong(r.GcBytes)).Append(',')
                    .Append(DecisionBCsvLong(r.Steps)).Append(',')
                    .Append(DecisionBCsvLong(r.Accepted)).Append(',')
                    .Append(DecisionBCsvLong(r.Rejected)).Append(',')
                    .Append(DecisionBCsvLong(r.Chunks)).Append(',')
                    .Append(DecisionBCsvLong(r.RawCalls)).Append(',')
                    .Append(DecisionBCsvLong(r.Aux)).Append(',')
                    .Append(DecisionBCsvField(r.Note)).Append(',')
                    .Append(mr.Scenario.Sensitivity ? "1" : "0").AppendLine();
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBWriteMatrixCsv(
        string path, List<DecisionBScenario> scenarios, List<DecisionBMethodResult> results,
        Dictionary<string, DecisionBEventResult> events,
        Dictionary<string, double> eventFloors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario,regime,config,method,method_class,max_step,config_max_step,chunk,target,target_kind,"
            + "control,control_kind,err_max_m,err_final_m,err_max_v,"
            + "edge_err_max_m,edge_cost_total,edge_cost_if_ported,edge_control,edge_status,"
            + "cost_rhs,cost_local_frame,"
            + "cost_aux,cost_total,cost_if_ported,cost_ms,ms_median,ms_spread_pct,gc_bytes,gc_bytes_per_step,"
            + "cost_per_sim_second,cost_per_chunk,steps,accepted,rejected,chunks,raw_rhs,aux_ops,"
            + "floor_m,floor_v,target_over_floor,status,confidence,note,reference_method,"
            + "reference_resolution,event_target_s,event_err_s,event_floor_s,event_status,sensitivity");

        foreach (DecisionBScenario s in scenarios)
        {
            foreach (DecisionBMethodResult mr in results)
            {
                if (mr.Scenario != s)
                {
                    continue;
                }

                foreach (DecisionBTarget target in s.Targets)
                {
                    var row = new DecisionBMatrixRow
                    {
                        Scenario = s.Label,
                        Regime = s.Regime,
                        Config = s.Config,
                        Method = mr.Method.Name,
                        MethodClass = mr.Method.Class,
                        MaxStep = mr.EffectiveMaxStep,
                        ConfigMaxStep = s.MaxStep,
                        Chunk = s.ChunkDescription,
                        Target = target.Value,
                        TargetKind = target.Kind,
                        FloorM = s.FloorM,
                        FloorV = s.FloorV,
                        ReferenceMethod = s.Refs.LocalKind ?? "",
                        ReferenceResolution = s.Refs.LocalAsymptotic ? "ratio_ok"
                            : (s.Refs.LocalResolved ? "resolved_finest_delta" : "not_certified"),
                        Sensitivity = s.Sensitivity
                    };

                    double floor = target.Kind == "velocity" ? s.FloorV : s.FloorM;
                    row.TargetOverFloor = double.IsFinite(floor) && floor > 0d
                        ? target.Value / floor
                        : double.NaN;

                    if (mr.Status == "n/a" || mr.Status == "not run")
                    {
                        row.Status = mr.Status;
                        row.Confidence = "n/a";
                        row.Note = mr.Reason;
                    }
                    else if (!double.IsFinite(floor))
                    {
                        row.Status = "skip";
                        row.Confidence = "low";
                        row.Note = "reference floor is not certified for this quantity";
                    }
                    else if (target.Value < 10d * floor)
                    {
                        row.Status = "skip";
                        row.Confidence = "low";
                        row.Note = "target " + DecisionBFormatTarget(target.Value, target.Kind)
                            + " < 10 x reference floor " + floor.ToString("E3", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        DecisionBRun hit = DecisionBCheapestHit(mr.Runs, target.Value, target.Kind);
                        DecisionBRun edgeHit = DecisionBCheapestEdgeHit(mr.Runs, target.Value, target.Kind);
                        if (edgeHit != null)
                        {
                            row.EdgeCostTotal = edgeHit.CostTotal;
                            row.EdgeCostIfPorted = edgeHit.CostIfPorted;
                            row.EdgeErrMaxM = DecisionBRunError(edgeHit, target.Kind);
                            row.EdgeControl = edgeHit.Control;
                            row.EdgeStatus = ReferenceEquals(edgeHit, hit) ? "edge-selected" : "edge";
                        }

                        if (hit != null)
                        {
                            double err = DecisionBRunError(hit, target.Kind);
                            row.Status = err <= 0.8d * target.Value ? "ok" : "edge";
                            row.Confidence = err <= 0.5d * target.Value ? "high" : "medium";
                            DecisionBFillRow(row, hit, s);
                        }
                        else
                        {
                            DecisionBRun best = null;
                            foreach (DecisionBRun r in mr.Runs)
                            {
                                if (r.Status != "ok" || !r.Finite)
                                {
                                    continue;
                                }

                                if (best == null
                                    || DecisionBRunError(r, target.Kind) < DecisionBRunError(best, target.Kind))
                                {
                                    best = r;
                                }
                            }

                            if (best != null)
                            {
                                row.Status = "no_hit";
                                row.Confidence = "low";
                                DecisionBFillRow(row, best, s);
                                row.Note = "finest executed run: "
                                    + DecisionBRunError(best, target.Kind).ToString("E3", CultureInfo.InvariantCulture)
                                    + "; target " + DecisionBFormatTarget(target.Value, target.Kind);
                            }
                            else if (mr.Runs.Count == 0)
                            {
                                row.Status = mr.Status;
                                row.Confidence = "low";
                                row.Note = mr.Reason;
                            }
                            else
                            {
                                row.Status = "unresolved";
                                row.Confidence = "low";
                                row.Note = "no finite executed run";
                            }
                        }
                    }

                    string eventKey = s.Label + "|" + mr.Method.Name;
                    DecisionBEventResult ev;
                    if (events.TryGetValue(eventKey, out ev))
                    {
                        row.EventTargetS = s.EventTargetSeconds;
                        row.EventErrS = ev.Time;
                        row.EventStatus = ev.Status;
                        double evFloor;
                        if (eventFloors.TryGetValue(s.Label, out evFloor))
                        {
                            row.EventFloorS = evFloor;
                            if (ev.Status == "ok" && double.IsFinite(ev.Time))
                            {
                                double refTime = eventFloors[s.Label + "|ref"];
                                row.EventErrS = Math.Abs(ev.Time - refTime);
                            }
                        }

                        if (!string.IsNullOrEmpty(ev.Note))
                        {
                            row.Note = (row.Note.Length == 0 ? "" : row.Note + "; ") + ev.Note;
                        }
                    }

                    sb.Append(DecisionBCsvField(row.Scenario)).Append(',')
                        .Append(DecisionBCsvField(row.Regime)).Append(',')
                        .Append(DecisionBCsvField(row.Config)).Append(',')
                        .Append(DecisionBCsvField(row.Method)).Append(',')
                        .Append(DecisionBCsvField(row.MethodClass)).Append(',')
                        .Append(DecisionBCsvNumber(row.MaxStep)).Append(',')
                        .Append(DecisionBCsvNumber(row.ConfigMaxStep)).Append(',')
                        .Append(DecisionBCsvField(row.Chunk)).Append(',')
                        .Append(DecisionBCsvNumber(row.Target)).Append(',')
                        .Append(DecisionBCsvField(row.TargetKind)).Append(',')
                        .Append(DecisionBCsvField(row.Control)).Append(',')
                        .Append(DecisionBCsvField(row.ControlKind)).Append(',')
                        .Append(DecisionBCsvNumber(row.ErrMaxM)).Append(',')
                        .Append(DecisionBCsvNumber(row.ErrFinalM)).Append(',')
                        .Append(DecisionBCsvNumber(row.ErrMaxV)).Append(',')
                        .Append(DecisionBCsvNumber(row.EdgeErrMaxM)).Append(',')
                        .Append(DecisionBCsvNumber(row.EdgeCostTotal)).Append(',')
                        .Append(DecisionBCsvNumber(row.EdgeCostIfPorted)).Append(',')
                        .Append(DecisionBCsvNumber(row.EdgeControl)).Append(',')
                        .Append(DecisionBCsvField(row.EdgeStatus)).Append(',')
                        .Append(DecisionBCsvNumber(row.CostRhs)).Append(',')
                        .Append(DecisionBCsvNumber(row.CostLocalFrame)).Append(',')
                        .Append(DecisionBCsvNumber(row.CostAux)).Append(',')
                        .Append(DecisionBCsvNumber(row.CostTotal)).Append(',')
                        .Append(DecisionBCsvNumber(row.CostIfPorted)).Append(',')
                        .Append(DecisionBCsvNumber(row.CostMs)).Append(',')
                        .Append(DecisionBCsvNumber(row.MsMedian)).Append(',')
                        .Append(DecisionBCsvNumber(row.MsSpreadPct)).Append(',')
                        .Append(DecisionBCsvLong(row.GcBytes)).Append(',')
                        .Append(DecisionBCsvNumber(row.Steps > 0 ? (double)row.GcBytes / row.Steps : double.NaN)).Append(',')
                        .Append(DecisionBCsvNumber(s.Case.Seconds > 0d ? row.CostTotal / s.Case.Seconds : double.NaN)).Append(',')
                        .Append(DecisionBCsvNumber(row.Chunks > 0 ? row.CostTotal / row.Chunks : double.NaN)).Append(',')
                        .Append(DecisionBCsvLong(row.Steps)).Append(',')
                        .Append(DecisionBCsvLong(row.Accepted)).Append(',')
                        .Append(DecisionBCsvLong(row.Rejected)).Append(',')
                        .Append(DecisionBCsvLong(row.Chunks)).Append(',')
                        .Append(DecisionBCsvLong(row.RawRhs)).Append(',')
                        .Append(DecisionBCsvLong(row.AuxOps)).Append(',')
                        .Append(DecisionBCsvNumber(row.FloorM)).Append(',')
                        .Append(DecisionBCsvNumber(row.FloorV)).Append(',')
                        .Append(DecisionBCsvNumber(row.TargetOverFloor)).Append(',')
                        .Append(DecisionBCsvField(row.Status)).Append(',')
                        .Append(DecisionBCsvField(row.Confidence)).Append(',')
                        .Append(DecisionBCsvField(row.Note)).Append(',')
                        .Append(DecisionBCsvField(row.ReferenceMethod)).Append(',')
                        .Append(DecisionBCsvField(row.ReferenceResolution)).Append(',')
                        .Append(DecisionBCsvNumber(row.EventTargetS)).Append(',')
                        .Append(DecisionBCsvNumber(row.EventErrS)).Append(',')
                        .Append(DecisionBCsvNumber(row.EventFloorS)).Append(',')
                        .Append(DecisionBCsvField(row.EventStatus)).Append(',')
                        .Append(row.Sensitivity ? "1" : "0").AppendLine();
                }
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBFillRow(DecisionBMatrixRow row, DecisionBRun hit, DecisionBScenario s)
    {
        row.Control = hit.Control.ToString("R", CultureInfo.InvariantCulture);
        row.ControlKind = hit.ControlKind;
        row.ErrMaxM = hit.ErrMaxM;
        row.ErrFinalM = hit.ErrFinalM;
        row.ErrMaxV = hit.ErrMaxV;
        row.CostRhs = hit.CostRhs;
        row.CostLocalFrame = hit.CostLocalFrame;
        row.CostAux = hit.CostAux;
        row.CostTotal = hit.CostTotal;
        row.CostIfPorted = hit.CostIfPorted;
        row.CostMs = hit.Ms;
        row.MsMedian = hit.MsMedian;
        row.MsSpreadPct = hit.MsSpreadPct;
        row.GcBytes = hit.GcBytes;
        row.Steps = hit.Steps;
        row.Accepted = hit.Accepted;
        row.Rejected = hit.Rejected;
        row.Chunks = hit.Chunks;
        row.RawRhs = hit.RawCalls;
        row.AuxOps = hit.Aux;
        row.Note = hit.Note;
    }

    private static void DecisionBWriteCoverageCsv(
        string path, List<DecisionBScenario> scenarios, List<DecisionBMethodResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("regime,scenario,config,method,method_class,status,reason,effective_max_step,runs");
        foreach (DecisionBScenario s in scenarios)
        {
            foreach (DecisionBMethodResult mr in results)
            {
                if (mr.Scenario != s)
                {
                    continue;
                }

                string reason = mr.Reason;
                if (mr.EffectiveMaxStep > 0d && double.IsFinite(mr.EffectiveMaxStep)
                    && double.IsFinite(s.MaxStep)
                    && Math.Abs(mr.EffectiveMaxStep - s.MaxStep) > 1e-9d)
                {
                    reason = "sensitivity max-step config (method " + mr.EffectiveMaxStep.ToString("G6", CultureInfo.InvariantCulture)
                        + " s vs scenario " + s.MaxStep.ToString("G6", CultureInfo.InvariantCulture) + " s); " + reason;
                }

                sb.Append(DecisionBCsvField(s.Regime)).Append(',')
                    .Append(DecisionBCsvField(s.Label)).Append(',')
                    .Append(DecisionBCsvField(s.Config)).Append(',')
                    .Append(DecisionBCsvField(mr.Method.Name)).Append(',')
                    .Append(DecisionBCsvField(mr.Method.Class)).Append(',')
                    .Append(DecisionBCsvField(mr.Status)).Append(',')
                    .Append(DecisionBCsvField(reason)).Append(',')
                    .Append(DecisionBCsvNumber(mr.EffectiveMaxStep)).Append(',')
                    .Append(mr.Runs.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    // ------------------------------------------------------------------------------------
    // CSV reader and decision table
    // ------------------------------------------------------------------------------------

    private static List<DecisionBMatrixRow> DecisionBReadMatrix(string csvPath)
    {
        var rows = new List<DecisionBMatrixRow>();
        if (!File.Exists(csvPath))
        {
            return rows;
        }

        string[] lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2)
        {
            return rows;
        }

        List<string> header = DecisionBCsvSplit(lines[0]);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            index[header[i]] = i;
        }

        Func<List<string>, string, string> get = (f, name) =>
        {
            int i;
            return index.TryGetValue(name, out i) && i < f.Count ? f[i] : "";
        };

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            List<string> f = DecisionBCsvSplit(lines[i]);
            var row = new DecisionBMatrixRow
            {
                Scenario = get(f, "scenario"),
                Regime = get(f, "regime"),
                Config = get(f, "config"),
                Method = get(f, "method"),
                MethodClass = get(f, "method_class"),
                MaxStep = DecisionBParseDouble(get(f, "max_step")),
                ConfigMaxStep = DecisionBParseDouble(get(f, "config_max_step")),
                Chunk = get(f, "chunk"),
                Target = DecisionBParseDouble(get(f, "target")),
                TargetKind = get(f, "target_kind"),
                Control = get(f, "control"),
                ControlKind = get(f, "control_kind"),
                ErrMaxM = DecisionBParseDouble(get(f, "err_max_m")),
                ErrFinalM = DecisionBParseDouble(get(f, "err_final_m")),
                ErrMaxV = DecisionBParseDouble(get(f, "err_max_v")),
                EdgeErrMaxM = DecisionBParseDouble(get(f, "edge_err_max_m")),
                EdgeCostTotal = DecisionBParseDouble(get(f, "edge_cost_total")),
                EdgeCostIfPorted = DecisionBParseDouble(get(f, "edge_cost_if_ported")),
                EdgeControl = DecisionBParseDouble(get(f, "edge_control")),
                EdgeStatus = get(f, "edge_status"),
                CostRhs = DecisionBParseDouble(get(f, "cost_rhs")),
                CostLocalFrame = DecisionBParseDouble(get(f, "cost_local_frame")),
                CostAux = DecisionBParseDouble(get(f, "cost_aux")),
                CostTotal = DecisionBParseDouble(get(f, "cost_total")),
                CostIfPorted = DecisionBParseDouble(get(f, "cost_if_ported")),
                CostMs = DecisionBParseDouble(get(f, "cost_ms")),
                MsMedian = DecisionBParseDouble(get(f, "ms_median")),
                MsSpreadPct = DecisionBParseDouble(get(f, "ms_spread_pct")),
                GcBytes = (long)DecisionBParseDouble(get(f, "gc_bytes")),
                Steps = (long)DecisionBParseDouble(get(f, "steps")),
                Accepted = (long)DecisionBParseDouble(get(f, "accepted")),
                Rejected = (long)DecisionBParseDouble(get(f, "rejected")),
                Chunks = (long)DecisionBParseDouble(get(f, "chunks")),
                RawRhs = (long)DecisionBParseDouble(get(f, "raw_rhs")),
                AuxOps = (long)DecisionBParseDouble(get(f, "aux_ops")),
                FloorM = DecisionBParseDouble(get(f, "floor_m")),
                FloorV = DecisionBParseDouble(get(f, "floor_v")),
                TargetOverFloor = DecisionBParseDouble(get(f, "target_over_floor")),
                Status = get(f, "status"),
                Confidence = get(f, "confidence"),
                Note = get(f, "note"),
                ReferenceMethod = get(f, "reference_method"),
                ReferenceResolution = get(f, "reference_resolution"),
                EventTargetS = DecisionBParseDouble(get(f, "event_target_s")),
                EventErrS = DecisionBParseDouble(get(f, "event_err_s")),
                EventFloorS = DecisionBParseDouble(get(f, "event_floor_s")),
                EventStatus = get(f, "event_status"),
                Sensitivity = get(f, "sensitivity") == "1"
            };
            rows.Add(row);
        }

        return rows;
    }

    private static double DecisionBRowError(DecisionBMatrixRow r)
    {
        return r.TargetKind == "velocity" ? r.ErrMaxV : r.ErrMaxM;
    }

    private static double DecisionBComparableCost(DecisionBMatrixRow r)
    {
        // Production rows already pay the local frame; stand rows get the port
        // estimate. Missing column on an old CSV falls back to raw cost_total.
        return double.IsFinite(r.CostIfPorted) ? r.CostIfPorted : r.CostTotal;
    }

    private static string DecisionBPct(double value)
    {
        return (value >= 0d ? "+" : "") + value.ToString("F0", CultureInfo.InvariantCulture);
    }

    private static string DecisionBRelation(double value, double baseCost)
    {
        if (!double.IsFinite(value) || !double.IsFinite(baseCost) || baseCost <= 0d)
        {
            return "base n/a";
        }

        if (value < baseCost)
        {
            return (100d * (1d - (value / baseCost))).ToString("F0", CultureInfo.InvariantCulture)
                + "% cheaper than";
        }

        return (100d * (value / baseCost - 1d)).ToString("F0", CultureInfo.InvariantCulture)
            + "% more expensive than";
    }

    private static string DecisionBMethodLabel(DecisionBMatrixRow r)
    {
        if (r == null)
        {
            return "unresolved";
        }

        string label = r.Method;
        if (r.Status == "edge")
        {
            label += " (edge)";
        }

        if (r.MethodClass == "stand-only")
        {
            label += " (requires port)";
        }

        return label;
    }

    private static string DecisionBRawLabel(DecisionBMatrixRow r)
    {
        if (r == null)
        {
            return "unresolved";
        }

        return r.Method + (r.Status == "edge" ? " (edge)" : "") + " (raw, requires port, not comparable)";
    }

    private static string DecisionBPrescribedForceNote(DecisionBTableRow t)
    {
        if (t.Scenario.IndexOf("Surface hover", StringComparison.Ordinal) >= 0
            || t.Scenario.IndexOf("Surface level flight", StringComparison.Ordinal) >= 0)
        {
            return "; prescribed-force hover/level: no closed-loop altitude hold";
        }

        return "";
    }

    private static bool DecisionBIsPrescribed(DecisionBTableRow t)
    {
        return t.Scenario.IndexOf("Surface hover", StringComparison.Ordinal) >= 0
            || t.Scenario.IndexOf("Surface level flight", StringComparison.Ordinal) >= 0;
    }

    private static string DecisionBShortWhy(DecisionBTableRow t)
    {
        var parts = new List<string>();
        parts.Add(t.BestAllClass == "stand-only"
            ? "best_all is a port candidate"
            : "best_all is production");
        if (double.IsFinite(t.ProductionVsBestAllPct))
        {
            parts.Add("production " + DecisionBPct(t.ProductionVsBestAllPct) + "% vs best_all");
        }

        if (double.IsFinite(t.CheapestEdgeTotal))
        {
            string relation = DecisionBRelation(t.CheapestEdgeTotal, t.SelectedBestAllTotal);
            string prodRelation = DecisionBRelation(t.CheapestEdgeTotal, t.SelectedProductionTotal);
            parts.Add("edge " + t.CheapestEdgeTotal.ToString("N0", CultureInfo.InvariantCulture)
                + " (" + relation + " best_all, " + prodRelation + " production)");
        }
        else
        {
            parts.Add("no edge runs");
        }

        if (DecisionBIsPrescribed(t))
        {
            parts.Add("prescribed-force; no closed-loop altitude hold");
        }

        return string.Join("; ", parts);
    }

    private static void DecisionBAppendCompactTable(StringBuilder sb, List<DecisionBTableRow> rows)
    {
        sb.AppendLine("| regime | scenario | target | best_production | best_ported | best_all | confidence | why |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (DecisionBTableRow t in rows)
        {
            sb.Append("| ").Append(t.Regime).Append(" | ")
                .Append(DecisionBCsvField(t.Scenario)).Append(" | ")
                .Append(DecisionBFormatTarget(t.Target, t.TargetKind)).Append(" | ")
                .Append(t.BestProduction).Append(" | ")
                .Append(t.BestPorted).Append(" | ")
                .Append(t.BestAll).Append(" | ")
                .Append(t.Confidence).Append(" | ")
                .Append(DecisionBShortWhy(t)).AppendLine(" |");
        }
    }

    private static List<DecisionBTableRow> DecisionBBuildTable(List<DecisionBMatrixRow> rows)
    {
        var table = new List<DecisionBTableRow>();
        var groups = new Dictionary<string, List<DecisionBMatrixRow>>(StringComparer.Ordinal);
        foreach (DecisionBMatrixRow r in rows)
        {
            // Two comparable configurations: the game's production max step and the
            // explicit regime max step (far/heliocentric only). They are separate
            // rows and are never mixed; auto-bug and chunk sweeps are not decisions.
            if (r.Config != "production" && r.Config != "regime86400")
            {
                continue;
            }

            // A cap variant with a different max step is a different configuration,
            // not a method in this scenario. It is reported in ChunkSensitivity.
            if (double.IsFinite(r.ConfigMaxStep) && double.IsFinite(r.MaxStep)
                && Math.Abs(r.MaxStep - r.ConfigMaxStep) > 1e-9d)
            {
                continue;
            }

            string key = r.Scenario + "|" + r.Config + "|" + r.Target.ToString("R", CultureInfo.InvariantCulture)
                + "|" + r.TargetKind;
            List<DecisionBMatrixRow> list;
            if (!groups.TryGetValue(key, out list))
            {
                list = new List<DecisionBMatrixRow>();
                groups[key] = list;
            }

            list.Add(r);
        }

        foreach (var group in groups)
        {
            List<DecisionBMatrixRow> list = group.Value;
            var ok = list.FindAll(r => r.Status == "ok");
            var edge = list.FindAll(r => r.Status == "edge");
            var na = list.FindAll(r => r.Status == "n/a");
            var unresolved = list.FindAll(r => r.Status == "unresolved" || r.Status == "no_hit");
            var notRun = list.FindAll(r => r.Status == "not run");

            ok.Sort((a, b) => DecisionBComparableCost(a).CompareTo(DecisionBComparableCost(b)));
            edge.Sort((a, b) => DecisionBComparableCost(a).CompareTo(DecisionBComparableCost(b)));

            var t = new DecisionBTableRow
            {
                Regime = list[0].Regime,
                Scenario = list[0].Scenario,
                Config = list[0].Config,
                Target = list[0].Target,
                TargetKind = list[0].TargetKind,
                Na = na.Count == 0 ? "-" : string.Join(", ", na.Select(x => x.Method)),
                Unresolved = unresolved.Count == 0 ? "-"
                    : string.Join(", ", unresolved.Select(x => x.Method + ":" + x.Status)),
                NotRun = notRun.Count == 0 ? "-" : string.Join(", ", notRun.Select(x => x.Method))
            };

            var okProduction = ok.FindAll(r => r.MethodClass == "production");
            var okStand = ok.FindAll(r => r.MethodClass == "stand-only");
            var edgeProduction = edge.FindAll(r => r.MethodClass == "production");
            var edgeStand = edge.FindAll(r => r.MethodClass == "stand-only");

            var prodCandidates = new List<DecisionBMatrixRow>();
            prodCandidates.AddRange(okProduction.Count > 0 ? okProduction : edgeProduction);
            prodCandidates.Sort((a, b) => DecisionBComparableCost(a).CompareTo(DecisionBComparableCost(b)));

            var portedCandidates = new List<DecisionBMatrixRow>();
            portedCandidates.AddRange(okStand.Count > 0 ? okStand : edgeStand);
            portedCandidates.Sort((a, b) => DecisionBComparableCost(a).CompareTo(DecisionBComparableCost(b)));

            var rawStandCandidates = new List<DecisionBMatrixRow>();
            rawStandCandidates.AddRange(okStand);
            rawStandCandidates.AddRange(edgeStand);
            rawStandCandidates.Sort((a, b) => a.CostTotal.CompareTo(b.CostTotal));

            var allCandidates = new List<DecisionBMatrixRow>();
            allCandidates.AddRange(ok);
            if (allCandidates.Count == 0)
            {
                allCandidates.AddRange(edge);
            }

            allCandidates.Sort((a, b) => DecisionBComparableCost(a).CompareTo(DecisionBComparableCost(b)));

            DecisionBMatrixRow prodBest = prodCandidates.Count > 0 ? prodCandidates[0] : null;
            DecisionBMatrixRow portedBest = portedCandidates.Count > 0 ? portedCandidates[0] : null;
            DecisionBMatrixRow rawBest = rawStandCandidates.Count > 0 ? rawStandCandidates[0] : null;
            DecisionBMatrixRow allBest = allCandidates.Count > 0 ? allCandidates[0] : null;

            t.BestProduction = DecisionBMethodLabel(prodBest);
            t.BestProductionCost = prodBest != null ? DecisionBComparableCost(prodBest) : double.NaN;
            t.SelectedProductionMethod = t.BestProduction;
            t.SelectedProductionTotal = t.BestProductionCost;
            t.BestPorted = DecisionBMethodLabel(portedBest);
            t.BestPortedCost = portedBest != null ? DecisionBComparableCost(portedBest) : double.NaN;
            t.BestStandRaw = DecisionBRawLabel(rawBest);
            t.BestStandRawCost = rawBest != null ? rawBest.CostTotal : double.NaN;
            t.BestAllClass = allBest != null ? allBest.MethodClass : "";

            if (prodBest != null)
            {
                t.ProductionConfidence = DecisionBProductionConfidence(okProduction, prodBest);
            }
            else
            {
                t.ProductionConfidence = "low";
            }

            if (allBest == null)
            {
                t.BestAll = "unresolved";
                t.Acceptable = "-";
                t.Confidence = "low";
                t.Why = "no executed method reached " + DecisionBFormatTarget(t.Target, t.TargetKind);
                table.Add(t);
                continue;
            }

            double allCost = DecisionBComparableCost(allBest);
            t.BestAllCost = allCost;

            var allTies = ok.FindAll(r => DecisionBComparableCost(r) <= 1.20d * Math.Max(allCost, 1e-30d));
            if (allBest.Status == "ok" && allTies.Count > 1)
            {
                t.BestAll = "tie: " + string.Join(", ", allTies.Select(DecisionBMethodLabel));
            }
            else
            {
                t.BestAll = DecisionBMethodLabel(allBest);
            }

            if (prodBest != null)
            {
                double prodCost = DecisionBComparableCost(prodBest);
                t.ProductionVsBestAllPct = 100d * (prodCost / Math.Max(allCost, 1e-30d) - 1d);
                t.BestAllVsProductionPct = 100d * (allCost / Math.Max(prodCost, 1e-30d) - 1d);
                if (rawBest != null && rawBest.CostTotal > 0d)
                {
                    t.ProductionVsStandRawPct = 100d * (prodCost / rawBest.CostTotal - 1d);
                }
            }

            var okForSelection = ok.Count > 0 ? ok : edge;
            DecisionBMatrixRow selectedOk = okForSelection.Count > 0 ? okForSelection[0] : null;
            t.SelectedBestAllTotal = selectedOk != null ? DecisionBComparableCost(selectedOk) : allCost;

            DecisionBMatrixRow cheapEdge = null;
            double cheapEdgeCost = double.PositiveInfinity;
            foreach (DecisionBMatrixRow r in list)
            {
                double edgeCost = double.IsFinite(r.EdgeCostIfPorted)
                    ? r.EdgeCostIfPorted
                    : (double.IsFinite(r.EdgeCostTotal)
                        ? r.EdgeCostTotal
                        : (r.Status == "edge" ? DecisionBComparableCost(r) : double.NaN));
                if (double.IsFinite(edgeCost) && edgeCost < cheapEdgeCost)
                {
                    cheapEdgeCost = edgeCost;
                    cheapEdge = r;
                }
            }

            t.CheapestEdgeMethod = cheapEdge == null ? "none" : DecisionBMethodLabel(cheapEdge);
            t.CheapestEdgeTotal = cheapEdge == null ? double.NaN : cheapEdgeCost;

            if (cheapEdge == null)
            {
                t.EdgePolicy = "edge policy: no edge runs in the executed grid";
            }
            else
            {
                string allRelation = DecisionBRelation(cheapEdgeCost, t.SelectedBestAllTotal);
                string prodRelation = DecisionBRelation(cheapEdgeCost, t.SelectedProductionTotal);
                string selectedText = ok.Count == 0
                    ? "no ok run; edge selected"
                    : "selected ok for margin; edge not selected because err/target > 0.8";
                t.EdgePolicy = "edge policy: " + selectedText + "; cheapest edge "
                    + cheapEdge.Method + " " + cheapEdgeCost.ToString("N0", CultureInfo.InvariantCulture)
                    + " is " + allRelation + " selected_best_all "
                    + (double.IsFinite(t.SelectedBestAllTotal)
                        ? t.SelectedBestAllTotal.ToString("N0", CultureInfo.InvariantCulture) : "n/a")
                    + " and " + prodRelation + " selected_production "
                    + (double.IsFinite(t.SelectedProductionTotal)
                        ? t.SelectedProductionTotal.ToString("N0", CultureInfo.InvariantCulture) : "n/a")
                    + " (" + t.SelectedProductionMethod + ")";
            }

            int edgeRunCount = list.Count(r => r.Status == "edge" || double.IsFinite(r.EdgeCostTotal));
            var acceptable = new List<string>();
            var ranked = new List<DecisionBMatrixRow>();
            ranked.AddRange(ok);
            ranked.AddRange(edge);
            foreach (DecisionBMatrixRow r in ranked)
            {
                if (ReferenceEquals(r, allBest))
                {
                    continue;
                }

                double cost = DecisionBComparableCost(r);
                string name = r.Method
                    + (r.Status == "edge" ? " [edge]" : "")
                    + (r.MethodClass == "stand-only" ? " [requires port]" : "");
                double vsProduction = double.IsFinite(t.BestProductionCost) && t.BestProductionCost > 0d
                    ? 100d * (cost / t.BestProductionCost - 1d)
                    : double.NaN;
                double vsAll = allCost > 0d ? 100d * (cost / allCost - 1d) : double.NaN;
                string vsRaw = double.IsFinite(t.BestStandRawCost) && t.BestStandRawCost > 0d
                    ? ", " + DecisionBPct(100d * (cost / t.BestStandRawCost - 1d))
                      + "% vs best_stand_raw (raw, not comparable)"
                    : "";
                acceptable.Add(name + " (" + (double.IsFinite(vsProduction) ? DecisionBPct(vsProduction) + "% vs best_production" : "no production base")
                    + ", " + (double.IsFinite(vsAll) ? DecisionBPct(vsAll) + "% vs best_all" : "no all base")
                    + vsRaw + ")");
            }

            t.Acceptable = acceptable.Count == 0 ? "-" : string.Join(", ", acceptable);

            double floor = t.TargetKind == "velocity" ? allBest.FloorV : allBest.FloorM;
            double margin = ok.Count > 1
                ? DecisionBComparableCost(ok[1]) / Math.Max(DecisionBComparableCost(ok[0]), 1e-30d) - 1d
                : double.PositiveInfinity;
            bool floorHigh = double.IsFinite(floor) && floor <= t.Target / 100d;
            bool floorOk = double.IsFinite(floor) && floor <= t.Target / 10d;
            bool allProduction = allBest.MethodClass == "production";
            double errFraction = DecisionBRowError(allBest) / t.Target;

            if (allBest.Status == "ok" && allTies.Count > 1)
            {
                t.Confidence = "low";
            }
            else if (!floorOk)
            {
                t.Confidence = "low";
            }
            else if (allProduction && margin >= 0.20d && floorHigh && allBest.Status == "ok")
            {
                t.Confidence = "high";
            }
            else if (margin >= 0.20d)
            {
                t.Confidence = "medium";
            }
            else
            {
                t.Confidence = "low";
            }

            if (!allProduction && t.Confidence == "high")
            {
                // A port estimate is not a measured production path: never claim
                // high confidence for a stand-only winner.
                t.Confidence = "medium";
            }

            if (errFraction > 0.8d)
            {
                if (t.Confidence == "high")
                {
                    t.Confidence = "medium";
                }
                else if (t.Confidence == "medium")
                {
                    t.Confidence = "low";
                }
            }

            string second = ok.Count > 1 ? ok[1].Method : "-";
            string secondCost = ok.Count > 1
                ? DecisionBComparableCost(ok[1]).ToString("N0", CultureInfo.InvariantCulture)
                : "-";
            string baseText = allProduction
                ? "best_all base = cost_total (production)"
                : "best_all base = cost_if_ported (stand port estimate)";
            string portText = allProduction
                ? ""
                : "; requires port: cost_if_ported = cost_total + 2*(raw - chunks)";
            string prescribedText = DecisionBPrescribedForceNote(t);
            t.Why = allBest.Method + " is selected as cheapest comparable hit at "
                + allCost.ToString("N0", CultureInfo.InvariantCulture)
                + " (rhs " + allBest.CostRhs.ToString("N0", CultureInfo.InvariantCulture)
                + " + local " + allBest.CostLocalFrame.ToString("N0", CultureInfo.InvariantCulture)
                + " + aux " + allBest.CostAux.ToString("N0", CultureInfo.InvariantCulture)
                + (double.IsFinite(allBest.CostIfPorted) ? "; cost_if_ported "
                    + allBest.CostIfPorted.ToString("N0", CultureInfo.InvariantCulture) : "")
                + ")" + portText
                + "; status " + allBest.Status
                + "; next " + second + " " + secondCost
                + "; err/target " + errFraction.ToString("G3", CultureInfo.InvariantCulture)
                + "; floor " + (double.IsFinite(floor) ? floor.ToString("E2", CultureInfo.InvariantCulture) : "NaN")
                + "; selected_production = " + t.SelectedProductionMethod
                + " cost_total " + (double.IsFinite(t.SelectedProductionTotal)
                    ? t.SelectedProductionTotal.ToString("N0", CultureInfo.InvariantCulture) : "n/a")
                + "; selected_best_all = "
                + (double.IsFinite(t.SelectedBestAllTotal)
                    ? t.SelectedBestAllTotal.ToString("N0", CultureInfo.InvariantCulture) : "n/a")
                + " (" + baseText + ")"
                + "; " + t.EdgePolicy
                + prescribedText
                + "; tie band 20%; " + ok.Count + " ok, " + edgeRunCount + " edge-run row(s), "
                + na.Count + " n/a, " + unresolved.Count + " unresolved";
            table.Add(t);
        }

        table.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.Regime, b.Regime);
            if (c != 0)
            {
                return c;
            }

            c = string.CompareOrdinal(a.Scenario, b.Scenario);
            if (c != 0)
            {
                return c;
            }

            return a.Target.CompareTo(b.Target);
        });
        return table;
    }

    private static string DecisionBProductionConfidence(
        List<DecisionBMatrixRow> okProduction, DecisionBMatrixRow pb)
    {
        double pfloor = pb.TargetKind == "velocity" ? pb.FloorV : pb.FloorM;
        double pmargin = okProduction.Count > 1
            ? DecisionBComparableCost(okProduction[1]) / Math.Max(DecisionBComparableCost(okProduction[0]), 1e-30d) - 1d
            : double.PositiveInfinity;
        double perr = DecisionBRowError(pb) / pb.Target;
        bool pfloorHigh = double.IsFinite(pfloor) && pfloor <= pb.Target / 100d;
        bool pfloorOk = double.IsFinite(pfloor) && pfloor <= pb.Target / 10d;
        string confidence = pb.Status == "ok" && pfloorHigh
            && (okProduction.Count == 1 || pmargin >= 0.20d) ? "high"
            : (pfloorOk ? "medium" : "low");
        if (perr > 0.8d && confidence == "high")
        {
            confidence = "medium";
        }
        else if (perr > 0.8d && confidence == "medium")
        {
            confidence = "low";
        }

        return confidence;
    }

    private sealed class DecisionBEventRow
    {
        public string Scenario;
        public string Regime;
        public string Method;
        public double Control;
        public string Kind;
        public double TargetSeconds;
        public double TimeSeconds;
        public double ErrorSeconds;
        public double FloorSeconds;
        public double CostTotal;
        public string Status;
        public string Note;
    }

    private static void DecisionBWriteValidityCsv(string path, List<DecisionBValidityRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("check,scenario,method,metric,value,threshold,status,note");
        foreach (DecisionBValidityRow r in rows)
        {
            sb.Append(DecisionBCsvField(r.Check)).Append(',')
                .Append(DecisionBCsvField(r.Scenario)).Append(',')
                .Append(DecisionBCsvField(r.Method)).Append(',')
                .Append(DecisionBCsvField(r.Metric)).Append(',')
                .Append(DecisionBCsvNumber(r.Value)).Append(',')
                .Append(DecisionBCsvNumber(r.Threshold)).Append(',')
                .Append(DecisionBCsvField(r.Status)).Append(',')
                .Append(DecisionBCsvField(r.Note)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBWriteEventsCsv(string path, List<DecisionBEventRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario,regime,method,control,event_kind,event_target_s,event_time_s,"
            + "event_err_s,event_floor_s,cost_total,status,note");
        foreach (DecisionBEventRow r in rows)
        {
            sb.Append(DecisionBCsvField(r.Scenario)).Append(',')
                .Append(DecisionBCsvField(r.Regime)).Append(',')
                .Append(DecisionBCsvField(r.Method)).Append(',')
                .Append(DecisionBCsvNumber(r.Control)).Append(',')
                .Append(DecisionBCsvField(r.Kind)).Append(',')
                .Append(DecisionBCsvNumber(r.TargetSeconds)).Append(',')
                .Append(DecisionBCsvNumber(r.TimeSeconds)).Append(',')
                .Append(DecisionBCsvNumber(r.ErrorSeconds)).Append(',')
                .Append(DecisionBCsvNumber(r.FloorSeconds)).Append(',')
                .Append(DecisionBCsvNumber(r.CostTotal)).Append(',')
                .Append(DecisionBCsvField(r.Status)).Append(',')
                .Append(DecisionBCsvField(r.Note)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBWriteWarpCsv(string path, List<DecisionBWarpRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario,regime,method,factor,sim_seconds,chunks,cost_rhs,cost_local_frame,"
            + "cost_aux,cost_total,ms,ms_per_real_second,gc_bytes,status,note");
        foreach (DecisionBWarpRow r in rows)
        {
            sb.Append(DecisionBCsvField(r.Scenario)).Append(',')
                .Append(DecisionBCsvField(r.Regime)).Append(',')
                .Append(DecisionBCsvField(r.Method)).Append(',')
                .Append(DecisionBCsvNumber(r.Factor)).Append(',')
                .Append(DecisionBCsvNumber(r.SimSeconds)).Append(',')
                .Append(DecisionBCsvLong(r.Chunks)).Append(',')
                .Append(DecisionBCsvNumber(r.CostRhs)).Append(',')
                .Append(DecisionBCsvNumber(r.CostLocalFrame)).Append(',')
                .Append(DecisionBCsvNumber(r.CostAux)).Append(',')
                .Append(DecisionBCsvNumber(r.CostTotal)).Append(',')
                .Append(DecisionBCsvNumber(r.Ms)).Append(',')
                .Append(DecisionBCsvNumber(r.Ms)).Append(',')
                .Append(DecisionBCsvLong(r.GcBytes)).Append(',')
                .Append(DecisionBCsvField(r.Status)).Append(',')
                .Append(DecisionBCsvField(r.Note)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBWriteSensitivityCsv(string path, List<DecisionBMatrixRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario,regime,config,method,method_class,max_step,config_max_step,chunk,target,target_kind,"
            + "err_max_m,err_max_v,cost_rhs,cost_local_frame,cost_aux,cost_total,cost_if_ported,"
            + "edge_cost_total,edge_cost_if_ported,edge_err_max_m,control,status,note");
        foreach (DecisionBMatrixRow r in rows)
        {
            bool maxStepMismatch = double.IsFinite(r.ConfigMaxStep) && double.IsFinite(r.MaxStep)
                && Math.Abs(r.MaxStep - r.ConfigMaxStep) > 1e-9d;
            if (!r.Sensitivity && r.Config == "production" && !maxStepMismatch)
            {
                continue;
            }

            sb.Append(DecisionBCsvField(r.Scenario)).Append(',')
                .Append(DecisionBCsvField(r.Regime)).Append(',')
                .Append(DecisionBCsvField(r.Config)).Append(',')
                .Append(DecisionBCsvField(r.Method)).Append(',')
                .Append(DecisionBCsvField(r.MethodClass)).Append(',')
                .Append(DecisionBCsvNumber(r.MaxStep)).Append(',')
                .Append(DecisionBCsvNumber(r.ConfigMaxStep)).Append(',')
                .Append(DecisionBCsvField(r.Chunk)).Append(',')
                .Append(DecisionBCsvNumber(r.Target)).Append(',')
                .Append(DecisionBCsvField(r.TargetKind)).Append(',')
                .Append(DecisionBCsvNumber(r.ErrMaxM)).Append(',')
                .Append(DecisionBCsvNumber(r.ErrMaxV)).Append(',')
                .Append(DecisionBCsvNumber(r.CostRhs)).Append(',')
                .Append(DecisionBCsvNumber(r.CostLocalFrame)).Append(',')
                .Append(DecisionBCsvNumber(r.CostAux)).Append(',')
                .Append(DecisionBCsvNumber(r.CostTotal)).Append(',')
                .Append(DecisionBCsvNumber(r.CostIfPorted)).Append(',')
                .Append(DecisionBCsvNumber(r.EdgeCostTotal)).Append(',')
                .Append(DecisionBCsvNumber(r.EdgeCostIfPorted)).Append(',')
                .Append(DecisionBCsvNumber(r.EdgeErrMaxM)).Append(',')
                .Append(DecisionBCsvField(r.Control)).Append(',')
                .Append(DecisionBCsvField(r.Status)).Append(',')
                .Append(DecisionBCsvField(r.Note)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBWriteTableCsv(string path, List<DecisionBTableRow> table)
    {
        var sb = new StringBuilder();
        sb.AppendLine("regime,scenario,config,target,target_kind,best_all_valid,best_all_class,best_all_cost,"
            + "best_production,best_production_cost,best_ported,best_ported_cost,"
            + "best_stand_raw,best_stand_raw_cost,production_vs_best_all_pct,best_all_vs_production_pct,"
            + "best_production_vs_best_stand_raw_pct,"
            + "selected_best_all_total,selected_production_method,selected_production_total,"
            + "cheapest_edge_method,cheapest_edge_total,edge_policy,"
            + "production_confidence,acceptable,n_a,unresolved,not_run,confidence,why");
        foreach (DecisionBTableRow r in table)
        {
            sb.Append(DecisionBCsvField(r.Regime)).Append(',')
                .Append(DecisionBCsvField(r.Scenario)).Append(',')
                .Append(DecisionBCsvField(r.Config)).Append(',')
                .Append(DecisionBCsvNumber(r.Target)).Append(',')
                .Append(DecisionBCsvField(r.TargetKind)).Append(',')
                .Append(DecisionBCsvField(r.BestAll)).Append(',')
                .Append(DecisionBCsvField(r.BestAllClass)).Append(',')
                .Append(DecisionBCsvNumber(r.BestAllCost)).Append(',')
                .Append(DecisionBCsvField(r.BestProduction)).Append(',')
                .Append(DecisionBCsvNumber(r.BestProductionCost)).Append(',')
                .Append(DecisionBCsvField(r.BestPorted)).Append(',')
                .Append(DecisionBCsvNumber(r.BestPortedCost)).Append(',')
                .Append(DecisionBCsvField(r.BestStandRaw)).Append(',')
                .Append(DecisionBCsvNumber(r.BestStandRawCost)).Append(',')
                .Append(DecisionBCsvNumber(r.ProductionVsBestAllPct)).Append(',')
                .Append(DecisionBCsvNumber(r.BestAllVsProductionPct)).Append(',')
                .Append(DecisionBCsvNumber(r.ProductionVsStandRawPct)).Append(',')
                .Append(DecisionBCsvNumber(r.SelectedBestAllTotal)).Append(',')
                .Append(DecisionBCsvField(r.SelectedProductionMethod)).Append(',')
                .Append(DecisionBCsvNumber(r.SelectedProductionTotal)).Append(',')
                .Append(DecisionBCsvField(r.CheapestEdgeMethod)).Append(',')
                .Append(DecisionBCsvNumber(r.CheapestEdgeTotal)).Append(',')
                .Append(DecisionBCsvField(r.EdgePolicy)).Append(',')
                .Append(DecisionBCsvField(r.ProductionConfidence)).Append(',')
                .Append(DecisionBCsvField(r.Acceptable)).Append(',')
                .Append(DecisionBCsvField(r.Na)).Append(',')
                .Append(DecisionBCsvField(r.Unresolved)).Append(',')
                .Append(DecisionBCsvField(r.NotRun)).Append(',')
                .Append(DecisionBCsvField(r.Confidence)).Append(',')
                .Append(DecisionBCsvField(r.Why)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static List<Dictionary<string, string>> DecisionBReadSimple(string path)
    {
        var rows = new List<Dictionary<string, string>>();
        if (!File.Exists(path))
        {
            return rows;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            return rows;
        }

        List<string> header = DecisionBCsvSplit(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            List<string> f = DecisionBCsvSplit(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int c = 0; c < header.Count && c < f.Count; c++)
            {
                row[header[c]] = f[c];
            }

            rows.Add(row);
        }

        return rows;
    }

    private static void DecisionBWriteMarkdown(
        string path, string matrixCsv, string coverageCsv, string tableCsv,
        string dominanceCsv, string isolationCsv, string validityCsv,
        string eventsCsv, string warpCsv)
    {
        List<DecisionBMatrixRow> matrix = DecisionBReadMatrix(matrixCsv);
        List<DecisionBTableRow> table = DecisionBBuildTable(matrix);
        var sb = new StringBuilder();
        sb.AppendLine("# Test560b - integrator decision matrix (all StudyRegistry methods)");
        sb.AppendLine();
        sb.AppendLine("Generated from `" + Path.GetFileName(matrixCsv) + "` on "
            + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC.");
        sb.AppendLine("Every numeric cell is an executed run; all runs, including losers and failures,");
        sb.AppendLine("are in `Test560b_Runs.csv`. Cost is ranked by `cost_total` = `cost_rhs` +");
        sb.AppendLine("`cost_local_frame` + `cost_aux`; comparisons that include stand-only methods");
        sb.AppendLine("use the disclosed port estimate `cost_if_ported`. No interpolation.");
        sb.AppendLine();
        sb.AppendLine("**Scope:** every scenario integrates the translational centre of mass");
        sb.AppendLine("(position, velocity, mass). `AttitudePhysics`, `VesselFlightModel`, gimbal/RCS,");
        sb.AppendLine("the contact/landing model and rigid-body rotation are not exercised here. The");
        sb.AppendLine("`touchdown` event is a sphere-crossing time, not a contact model.");
        sb.AppendLine();

        sb.AppendLine("## Audit of the existing stand");
        sb.AppendLine();
        sb.AppendLine("- Test551-Test559 cover the framework, ship/planet sweeps, edge cases, J2/Sun and");
        sb.AppendLine("  pure-gravity flybys. The audited results in `results/` are restored from git and");
        sb.AppendLine("  are not read or overwritten by Test560b; `results_baseline/` and the old numbers");
        sb.AppendLine("  in `ORBIT_INTEGRATOR_TEST_SCHEME.md` are not used.");
        sb.AppendLine("- Test560b covers the full `StudyRegistry.All()` list plus the existing 600 s and");
        sb.AppendLine("  86 400 s cap variants of DOPRI5/DOP853/RKN12/IAS15. Production rows are only");
        sb.AppendLine("  DOP853/DOPRI5; every other row is `stand-only` and marked as requiring a port.");
        sb.AppendLine();

        sb.AppendLine("## Method and cost model");
        sb.AppendLine();
        sb.AppendLine("- Chunks: surface ascent/descent/hover/level 0.05 s flight (0.5 s only as");
        sb.AppendLine("  sensitivity for ascent/descent); LEO 600 s coast and 0.05 s burn; high");
        sb.AppendLine("  orbit/cislunar 600 s; heliocentric 600 s production and 86 400 s");
        sb.AppendLine("  regime-sensitivity; far flight 86 400 s outer chunk with production max step 600 s");
        sb.AppendLine("  and a separate 86 400 s regime configuration.");
        sb.AppendLine("- `cost_rhs` = all RHS/force calls, including rejected steps and restarts.");
        sb.AppendLine("- `cost_local_frame` = production-only extra two gravity sums per local RHS");
        sb.AppendLine("  (the local derivative has three sums; one is already in `cost_rhs`). Stand methods");
        sb.AppendLine("  report 0 here because their RHS is already the local force.");
        sb.AppendLine("- `cost_if_ported` = `cost_total` for production; for a stand method it is");
        sb.AppendLine("  `cost_total + 2*(raw_rhs - chunks)`, the measured production convention for the");
        sb.AppendLine("  local-frame wrapper. It is an estimate, not a re-run, and is used whenever a");
        sb.AppendLine("  stand method is compared with production (`requires port`).");
        sb.AppendLine("- `cost_aux` = measured Kepler weight * auxiliary solves (WH/Y6/Encke); WH's");
        sb.AppendLine("  measured CV is in `Test560b_kepler_weights.csv`.");
        sb.AppendLine("- `cost_total` is the raw ranking metric inside a method class. `best_all_valid`");
        sb.AppendLine("  and `best_ported` rank by `cost_if_ported`; `best_stand_raw` is shown only as");
        sb.AppendLine("  a raw stand number and is explicitly not comparable. `ms` is diagnostic.");
        sb.AppendLine("- Max step: production config uses the game's 600 s cap; far/heliocentric have a");
        sb.AppendLine("  separate `regime86400` configuration. Production and stand methods are compared");
        sb.AppendLine("  only within the same config/max step.");
        sb.AppendLine("- Status: `ok` = error <= 80% target, `edge` = 80-100%, `no_hit` = >100%,");
        sb.AppendLine("  `n/a` = method cannot support the force, `unresolved` = no finite run,");
        sb.AppendLine("  `not run` = deliberate exclusion with a reason. An edge winner gets a margin");
        sb.AppendLine("  recommendation; ties are <=20% by the comparable cost.");
        sb.AppendLine("- Edge policy: `selected_best_all_total` is the chosen ok run that defines");
        sb.AppendLine("  `best_all`; `selected_production_total` is the production base. `cheapest_edge_total`");
        sb.AppendLine("  is compared with both in `edge_policy`; a cheaper edge is never promoted to ok.");
        sb.AppendLine();

        sb.AppendLine("## Scenarios");
        sb.AppendLine();
        sb.AppendLine("| scenario | regime | config | chunk | targets | floor (m) |");
        sb.AppendLine("|---|---|---|---:|---|---:|");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DecisionBMatrixRow r in matrix)
        {
            string key = r.Scenario + "|" + r.Config;
            if (!seen.Add(key))
            {
                continue;
            }

            var targets = matrix.FindAll(x => x.Scenario == r.Scenario && x.Config == r.Config)
                .Select(x => DecisionBFormatTarget(x.Target, x.TargetKind)).Distinct();
            sb.Append("| ").Append(DecisionBCsvField(r.Scenario)).Append(" | ")
                .Append(r.Regime).Append(" | ").Append(r.Config).Append(" | ")
                .Append(r.Chunk).Append(" | ").Append(string.Join(", ", targets)).Append(" | ")
                .Append(double.IsFinite(r.FloorM) ? r.FloorM.ToString("E2", CultureInfo.InvariantCulture) : "NaN")
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## Method coverage");
        sb.AppendLine();
        List<Dictionary<string, string>> coverage = DecisionBReadSimple(coverageCsv);
        var coverageCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in coverage)
        {
            string st = c.ContainsKey("status") ? c["status"] : "";
            int n;
            coverageCount.TryGetValue(st, out n);
            coverageCount[st] = n + 1;
        }

        sb.Append("Coverage rows: ");
        sb.Append(string.Join(", ", coverageCount.Select(kv => kv.Key + "=" + kv.Value)));
        sb.AppendLine(". Full method x regime/config coverage and reasons: `" + Path.GetFileName(coverageCsv) + "`.");
        sb.AppendLine();

        sb.AppendLine("## Per-regime cells (production and regime configs shown separately)");
        sb.AppendLine();
        foreach (DecisionBTableRow t in table)
        {
            sb.AppendLine("### " + t.Scenario + " / " + DecisionBFormatTarget(t.Target, t.TargetKind));
            sb.AppendLine();
            sb.AppendLine("| method | class | max step | err " + (t.TargetKind == "velocity" ? "max v" : "max r")
                + " | cost rhs | local | aux | total | ported | edge (ported) | ms | status |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");
            foreach (DecisionBMatrixRow r in matrix)
            {
                bool mismatch = double.IsFinite(r.ConfigMaxStep) && double.IsFinite(r.MaxStep)
                    && Math.Abs(r.MaxStep - r.ConfigMaxStep) > 1e-9d;
                if (r.Scenario != t.Scenario || r.Config != t.Config || mismatch
                    || Math.Abs(r.Target - t.Target) > 1e-12d || r.TargetKind != t.TargetKind)
                {
                    continue;
                }

                double err = t.TargetKind == "velocity" ? r.ErrMaxV : r.ErrMaxM;
                sb.Append("| ").Append(r.Method).Append(" | ").Append(r.MethodClass).Append(" | ")
                    .Append(DecisionBCsvNumber(r.MaxStep)).Append(" | ")
                    .Append(double.IsFinite(err) ? err.ToString("E3", CultureInfo.InvariantCulture) : "-").Append(" | ")
                    .Append(DecisionBCsvNumber(r.CostRhs)).Append(" | ")
                    .Append(DecisionBCsvNumber(r.CostLocalFrame)).Append(" | ")
                    .Append(DecisionBCsvNumber(r.CostAux)).Append(" | ")
                    .Append(DecisionBCsvNumber(r.CostTotal)).Append(" | ")
                    .Append(DecisionBCsvNumber(r.CostIfPorted)).Append(" | ")
                    .Append(DecisionBCsvNumber(r.EdgeCostIfPorted)).Append(" | ")
                    .Append(double.IsFinite(r.MsMedian) ? r.MsMedian.ToString("F1", CultureInfo.InvariantCulture)
                        : (double.IsFinite(r.CostMs) ? r.CostMs.ToString("F1", CultureInfo.InvariantCulture) : "-"))
                    .Append(" | ").Append(r.Status).AppendLine(" |");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Decision table (compact; cost_total / cost_if_ported)");
        sb.AppendLine();
        var mainRows = table.FindAll(t => !DecisionBIsPrescribed(t));
        DecisionBAppendCompactTable(sb, mainRows);
        sb.AppendLine();
        sb.AppendLine("Full acceptable percentages, selected_best_all_total, selected_production_total and");
        sb.AppendLine("the per-row edge policy are in `Test560b_DecisionTable.csv` and");
        sb.AppendLine("`Test560b_DecisionDetails.md`.");
        sb.AppendLine();
        var prescribedRows = table.FindAll(DecisionBIsPrescribed);
        if (prescribedRows.Count > 0)
        {
            sb.AppendLine("### Prescribed-force surface hover/level (separate section)");
            sb.AppendLine();
            sb.AppendLine("These are prescribed-force trajectories, not closed-loop altitude hold.");
            sb.AppendLine("`ThrustScalesWithMass` uses a linear mass estimate; the force is prescribed.");
            sb.AppendLine();
            DecisionBAppendCompactTable(sb, prescribedRows);
            sb.AppendLine();
        }
        sb.AppendLine("## Far / heliocentric: production-600 vs regime86400");
        sb.AppendLine();
        sb.AppendLine("The production-600 cell is the game's current configuration. The 86 400 s");
        sb.AppendLine("cell is an explicit regime sensitivity, not the current production default.");
        sb.AppendLine("Both are shown; the ratio is not hidden inside a single winner.");
        sb.AppendLine();
        sb.AppendLine("| scenario | target | best prod-600 | cost | best regime86400 | cost | best ratio | DOP853 prod-600 | DOP853 regime86400 | DOP853 ratio |");
        sb.AppendLine("|---|---|---|---:|---|---:|---:|---:|---:|---:|");
        var pairGroups = new Dictionary<string, List<DecisionBMatrixRow>>(StringComparer.Ordinal);
        foreach (DecisionBMatrixRow r in matrix)
        {
            if (r.Config != "production" && r.Config != "regime86400")
            {
                continue;
            }

            if (r.Regime != "far-flight" && r.Regime != "heliocentric")
            {
                continue;
            }

            // Cap variants are a separate max-step configuration; never let one
            // stand in for the regime's production max step in this pairing.
            if (double.IsFinite(r.ConfigMaxStep) && double.IsFinite(r.MaxStep)
                && Math.Abs(r.MaxStep - r.ConfigMaxStep) > 1e-9d)
            {
                continue;
            }

            string baseLabel = r.Scenario;
            int bracket = baseLabel.IndexOf(" [", StringComparison.Ordinal);
            if (bracket > 0)
            {
                baseLabel = baseLabel.Substring(0, bracket);
            }

            string key = baseLabel + "|" + r.Target.ToString("R", CultureInfo.InvariantCulture) + "|" + r.TargetKind;
            List<DecisionBMatrixRow> list;
            if (!pairGroups.TryGetValue(key, out list))
            {
                list = new List<DecisionBMatrixRow>();
                pairGroups[key] = list;
            }

            list.Add(r);
        }

        foreach (var kv in pairGroups)
        {
            List<DecisionBMatrixRow> list = kv.Value;
            DecisionBMatrixRow prod = list.FindAll(x => x.Config == "production"
                    && (x.Status == "ok" || x.Status == "edge"))
                .OrderBy(DecisionBComparableCost).FirstOrDefault();
            DecisionBMatrixRow regime = list.FindAll(x => x.Config == "regime86400"
                    && (x.Status == "ok" || x.Status == "edge"))
                .OrderBy(DecisionBComparableCost).FirstOrDefault();
            if (prod == null || regime == null)
            {
                continue;
            }

            double p = DecisionBComparableCost(prod);
            double g = DecisionBComparableCost(regime);
            DecisionBMatrixRow prod853 = list.Find(x => x.Config == "production"
                && x.Method == "DOP853 [production]" && (x.Status == "ok" || x.Status == "edge"));
            DecisionBMatrixRow regime853 = list.Find(x => x.Config == "regime86400"
                && x.Method == "DOP853 [production]" && (x.Status == "ok" || x.Status == "edge"));
            double p853 = prod853 == null ? double.NaN : DecisionBComparableCost(prod853);
            double g853 = regime853 == null ? double.NaN : DecisionBComparableCost(regime853);
            sb.Append("| ").Append(DecisionBCsvField(list[0].Scenario)).Append(" | ")
                .Append(DecisionBFormatTarget(list[0].Target, list[0].TargetKind)).Append(" | ")
                .Append(DecisionBMethodLabel(prod)).Append(" | ")
                .Append(p.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(DecisionBMethodLabel(regime)).Append(" | ")
                .Append(g.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                .Append((p / Math.Max(g, 1e-30d)).ToString("F1", CultureInfo.InvariantCulture)).Append("x | ")
                .Append(DecisionBCsvNumber(p853)).Append(" | ")
                .Append(DecisionBCsvNumber(g853)).Append(" | ")
                .Append(double.IsFinite(p853) && double.IsFinite(g853) && g853 > 0d
                    ? (p853 / g853).ToString("F1", CultureInfo.InvariantCulture) + "x" : "-")
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## Wall-clock and metric sensitivity (all methods vs production-only)");
        sb.AppendLine();
        List<Dictionary<string, string>> domRows = DecisionBReadSimple(dominanceCsv);
        Func<string, string, Dictionary<string, string>> findDom = (check, metric) =>
            domRows.Find(x => x.ContainsKey("check") && x["check"] == check
                && (!x.ContainsKey("metric") || x["metric"] == metric));
        var allMs = findDom("cost_metric", "ms");
        var prodMs = findDom("production_cost_metric_ms", "ms");
        var prodRhs = findDom("production_cost_metric_rhs", "cost_rhs");
        var ported = findDom("cost_metric", "cost_if_ported");
        var allDom = findDom("dominance_count", "cost_total");
        var prodDom = findDom("production_dominance_count", "cost_total");
        sb.AppendLine("- all-method `cost_total` wins: "
            + (allDom != null && allDom.ContainsKey("note") ? allDom["note"] : "not available") + ".");
        sb.AppendLine("- production-only `cost_total` wins: "
            + (prodDom != null && prodDom.ContainsKey("note") ? prodDom["note"] : "not available") + ".");
        sb.AppendLine("- all-method wall-clock vs `cost_total`: "
            + (allMs != null && allMs.ContainsKey("note") ? allMs["note"] : "not available") + ".");
        sb.AppendLine("- production-only wall-clock vs `cost_total`: "
            + (prodMs != null && prodMs.ContainsKey("note") ? prodMs["note"] : "not available") + ".");
        sb.AppendLine("- production-only `cost_rhs` vs `cost_total`: "
            + (prodRhs != null && prodRhs.ContainsKey("note") ? prodRhs["note"] : "not available") + ".");
        sb.AppendLine("- all-method `cost_if_ported` vs `cost_total`: "
            + (ported != null && ported.ContainsKey("note") ? ported["note"] : "not available") + ".");
        sb.AppendLine();

        sb.AppendLine("## Validity summary (numbers from `" + Path.GetFileName(validityCsv) + "`)");
        sb.AppendLine();
        sb.AppendLine("| check | scenario | method | metric | value | threshold | status | note |");
        sb.AppendLine("|---|---|---|---|---:|---:|---|---|");
        foreach (var v in DecisionBReadSimple(validityCsv))
        {
            if (!v.ContainsKey("check"))
            {
                continue;
            }

            if (v["check"] != "production_default"
                && !v["check"].StartsWith("production_parity", StringComparison.Ordinal)
                && v["check"] != "local_frame_error_control")
            {
                continue;
            }

            sb.Append("| ").Append(v["check"]).Append(" | ").Append(v["scenario"]).Append(" | ")
                .Append(v["method"]).Append(" | ").Append(v["metric"]).Append(" | ")
                .Append(v["value"]).Append(" | ").Append(v["threshold"]).Append(" | ")
                .Append(v["status"]).Append(" | ").Append(v["note"]).AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## Sensitivity (not production)");
        sb.AppendLine();
        sb.AppendLine("| scenario | config | method | max step | chunk | target | err | total | status |");
        sb.AppendLine("|---|---|---|---:|---|---:|---:|---:|---|");
        foreach (DecisionBMatrixRow r in matrix)
        {
            if (!r.Sensitivity && r.Config == "production")
            {
                continue;
            }

            sb.Append("| ").Append(DecisionBCsvField(r.Scenario)).Append(" | ").Append(r.Config).Append(" | ")
                .Append(r.Method).Append(" | ").Append(DecisionBCsvNumber(r.MaxStep)).Append(" | ")
                .Append(r.Chunk).Append(" | ").Append(DecisionBFormatTarget(r.Target, r.TargetKind)).Append(" | ")
                .Append(DecisionBCsvNumber(r.TargetKind == "velocity" ? r.ErrMaxV : r.ErrMaxM)).Append(" | ")
                .Append(DecisionBCsvNumber(r.CostTotal)).Append(" | ").Append(r.Status).AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## Dominance audit");
        sb.AppendLine();
        List<Dictionary<string, string>> dom = DecisionBReadSimple(dominanceCsv);
        var verdict = dom.Find(x => x.ContainsKey("check") && x["check"] == "verdict");
        sb.AppendLine("**Verdict (all methods):** "
            + (verdict != null && verdict.ContainsKey("note") ? verdict["note"] : "not available"));
        var prodVerdict = dom.Find(x => x.ContainsKey("check") && x["check"] == "production_verdict");
        sb.AppendLine("**Production-only verdict:** "
            + (prodVerdict != null && prodVerdict.ContainsKey("note") ? prodVerdict["note"] : "not available"));
        sb.AppendLine();
        sb.AppendLine("`cell_winner`, `dominance_count`, `target_sweep` and `chunk_sweep` use raw");
        sb.AppendLine("`cost_total`; `cost_if_ported` is reported as a separate `cost_metric` row.");
        sb.AppendLine("The decision table above uses `cost_if_ported` for any stand/production comparison.");
        sb.AppendLine();
        sb.AppendLine("| check | scenario | target | winner | value | note |");
        sb.AppendLine("|---|---|---|---|---:|---|");
        foreach (var d in dom)
        {
            if (!d.ContainsKey("check"))
            {
                continue;
            }

            if (d["check"] != "dominance_count" && d["check"] != "chunk_sweep"
                && d["check"] != "cell_winner" && d["check"] != "cost_metric"
                && !d["check"].StartsWith("production_", StringComparison.Ordinal))
            {
                continue;
            }

            sb.Append("| ").Append(d["check"]).Append(" | ").Append(d["scenario"]).Append(" | ")
                .Append(d["target"]).Append(" | ").Append(d["winner"]).Append(" | ")
                .Append(d["value"]).Append(" | ").Append(d["note"]).AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("The first Test560 ranked by `cost_rhs` with LEO 60 s and surface ascent 0.5 s,");
        sb.AppendLine("a 5-method subset, soft 1x targets and raw stand cost. DOPRI5's 6 stages beat");
        sb.AppendLine("DOP853's 12 when the chunk allowed one step and neither the local-frame nor the");
        sb.AppendLine("port overhead was in the ranking. This table separates `cost_total` (raw stand),");
        sb.AppendLine("`cost_if_ported` (comparable with production) and `best_production`, and repeats");
        sb.AppendLine("the winner count under cost_rhs/target/chunk sweeps.");
        sb.AppendLine();

        sb.AppendLine("## Heliocentric 5 AU / 10 y isolation");
        sb.AppendLine();
        sb.AppendLine("| variant | method | dominant | frame | max step | err max m | cost total | status |");
        sb.AppendLine("|---|---|---|---|---:|---:|---:|---|");
        foreach (var r in DecisionBReadSimple(isolationCsv))
        {
            if (!r.ContainsKey("variant")) continue;
            sb.Append("| ").Append(r["variant"]).Append(" | ").Append(r["method"]).Append(" | ")
                .Append(r["dominant"]).Append(" | ").Append(r["use_frame"]).Append(" | ")
                .Append(r["max_step"]).Append(" | ").Append(r["err_max_m"]).Append(" | ")
                .Append(r["cost_total"]).Append(" | ").Append(r["status"]).AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("The `auto` row is the current production path (dominant-body switch). The forced-Sun");
        sb.AppendLine("and absolute rows isolate it. If `auto` is orders of magnitude worse while forced/absolute");
        sb.AppendLine("agree with the Sun-centred reference, the scenario is a production frame-switch finding,");
        sb.AppendLine("not an integrator result; the decision table uses the forced-Sun comparison and keeps");
        sb.AppendLine("the auto path visible.");
        sb.AppendLine();

        sb.AppendLine("## Validity / events / warp");
        sb.AppendLine();
        sb.AppendLine("- `" + Path.GetFileName(validityCsv) + "`: production default, parity, determinism,");
        sb.AppendLine("  chunk invariance. FAIL/finding rows are listed there.");
        sb.AppendLine("- `" + Path.GetFileName(eventsCsv) + "`: touchdown/SOI event time.");
        sb.AppendLine("- `" + Path.GetFileName(warpCsv) + "`: real 60-frame warp runs, cost per real second.");
        sb.AppendLine();

        sb.AppendLine("## Failed / unresolved / n/a");
        sb.AppendLine();
        foreach (DecisionBTableRow t in table)
        {
            if (t.Unresolved == "-" && t.Na == "-" && t.NotRun == "-")
            {
                continue;
            }

            sb.AppendLine("- " + t.Scenario + " / " + DecisionBFormatTarget(t.Target, t.TargetKind)
                + ": unresolved [" + t.Unresolved + "], n/a [" + t.Na + "], not run [" + t.NotRun + "]");
        }

        sb.AppendLine();
        sb.AppendLine("## Production-available vs stand-only");
        sb.AppendLine();
        sb.AppendLine("- Production-available: DOP853, DOPRI5. The default is still DOP853.");
        sb.AppendLine("- Stand-only (requires port): every other StudyRegistry method and its cap variants.");
        sb.AppendLine("  `WH-Y6`, `WH-BM14A` and `Encke-IAS15` require a central body; `PEFRL/Y6/Y6C/");
        sb.AppendLine("  Leapfrog/RKN12/SRKN14A/GJ8` require a conservative second-order force and are");
        sb.AppendLine("  reported n/a on drag/thrust; RK4/DOPRI5-fixed/DOPRI5/DOP853/IAS15/Encke are");
        sb.AppendLine("  applicable where their Supports() says so.");
        sb.AppendLine("- A stand-only winner is a port candidate, not a production recommendation:");
        sb.AppendLine("  `cost_if_ported` is an estimate from the measured production local-frame");
        sb.AppendLine("  convention, and the port itself was not run.");
        sb.AppendLine();

        File.WriteAllText(path, sb.ToString());
    }
}
