using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Spacecraft;

// Test560b: every StudyRegistry method, production chunks, full cost and confidence.
internal static partial class P1bTests
{
    private static int Test560b_IntegratorDecisionMatrix()
    {
        Console.WriteLine("--- Test560b: all-integrator decision matrix (cost_total) ---");
        string dir = DecisionBResultsDir();
        string runsPath = Path.Combine(dir, "Test560b_Runs.csv");
        string matrixPath = Path.Combine(dir, "Test560b_DecisionMatrix.csv");
        string coveragePath = Path.Combine(dir, "Test560b_MethodCoverage.csv");
        string tablePath = Path.Combine(dir, "Test560b_DecisionTable.csv");
        string dominancePath = Path.Combine(dir, "Test560b_DominanceAudit.csv");
        string dominanceMd = Path.Combine(dir, "Test560b_DominanceAudit.md");
        string isolationPath = Path.Combine(dir, "Test560b_HeliocentricIsolation.csv");
        string isolationMd = Path.Combine(dir, "Test560b_HeliocentricIsolation.md");
        string validityPath = Path.Combine(dir, "Test560b_Validity.csv");
        string eventsPath = Path.Combine(dir, "Test560b_Events.csv");
        string warpPath = Path.Combine(dir, "Test560b_Warp.csv");
        string sensitivityPath = Path.Combine(dir, "Test560b_ChunkSensitivity.csv");
        string tableMd = Path.Combine(dir, "Test560b_DecisionTable.md");
        string detailsMd = Path.Combine(dir, "Test560b_DecisionDetails.md");
        string matrixMd = Path.Combine(dir, "Test560b_DecisionMatrix.md");

        if (string.Equals(DecisionBEnv("P1B560B_REGENERATE"), "1", StringComparison.Ordinal))
        {
            if (!File.Exists(matrixPath))
            {
                Console.WriteLine("regenerate: missing " + matrixPath);
                return 1;
            }

            List<DecisionBMatrixRow> regenRows = DecisionBReadMatrix(matrixPath);
            List<DecisionBTableRow> regenTable = DecisionBBuildTable(regenRows);
            // Recompute the heuristic bound from steps/chunks/duration and write
            // it back to the matrix CSV; refresh the tie-break validity rows so
            // they match the rebuilt table (the other validity rows are
            // measurements and are kept as-is).
            DecisionBRewriteMatrixBoundColumn(matrixPath, regenRows);
            DecisionBRefreshTieBreakValidity(validityPath, regenTable);
            DecisionBWriteTableCsv(tablePath, regenTable);
            DecisionBWriteSensitivityCsv(sensitivityPath, regenRows);
            DecisionBWriteMarkdown(matrixMd, matrixPath, coveragePath, tablePath,
                dominancePath, isolationPath, validityPath, eventsPath, warpPath);
            DecisionBWriteTableMd(tableMd, regenTable, regenRows);
            DecisionBWriteDecisionDetailsMd(detailsMd, regenTable, regenRows);
            Console.WriteLine("regenerated from " + matrixPath);
            return 0;
        }

        string pinPath = Path.Combine(dir, "Test560b_kepler_weights.csv");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("P1B_KEPLER_WEIGHTS")))
        {
            Environment.SetEnvironmentVariable("P1B_KEPLER_WEIGHTS", pinPath);
        }

        List<DecisionBScenario> scenarios = DecisionBBuildScenarios();
        string scenarioFilter = DecisionBEnv("P1B560B_REGIMES");
        if (!string.IsNullOrWhiteSpace(scenarioFilter))
        {
            scenarios = scenarios.FindAll(s =>
                DecisionBSelected(s.Regime, scenarioFilter) || DecisionBSelected(s.Label, scenarioFilter));
        }

        List<DecisionBMethod> methods = DecisionBMethods();
        string methodFilter = DecisionBEnv("P1B560B_METHODS");
        if (!string.IsNullOrWhiteSpace(methodFilter))
        {
            methods = methods.FindAll(m => DecisionBSelected(m.Name, methodFilter));
        }

        Console.WriteLine("scenarios: " + scenarios.Count.ToString(CultureInfo.InvariantCulture)
            + ", methods: " + methods.Count.ToString(CultureInfo.InvariantCulture)
            + (DecisionBQuick() ? " (quick)" : ""));

        // Kepler weights: one measurement per base problem; clones share the label.
        var weighted = new HashSet<string>(StringComparer.Ordinal);
        foreach (DecisionBScenario s in scenarios)
        {
            if (s.Problem == null || weighted.Contains(s.Problem.Label))
            {
                continue;
            }

            weighted.Add(s.Problem.Label);
            var options = new StudyOptions();
            options.KeplerWeight = double.NaN;
            StudyKeplerWeight kw = StudyCalibration.Resolve(s.Problem, options);
            foreach (DecisionBScenario other in scenarios)
            {
                if (ReferenceEquals(other.Problem, s.Problem)
                    || string.Equals(other.Problem.Label, s.Problem.Label, StringComparison.Ordinal))
                {
                    other.KeplerWeight = kw.Weight;
                    other.KeplerCv = kw.Cv;
                    other.KeplerSource = kw.Source;
                }
            }
            Console.WriteLine("  Kepler weight " + s.Problem.Label + ": "
                + kw.Weight.ToString("G6", CultureInfo.InvariantCulture)
                + " (CV " + kw.Cv.ToString("G3", CultureInfo.InvariantCulture) + ")");
        }

        var results = new List<DecisionBMethodResult>();
        var cache = new Dictionary<string, DecisionBRun>(StringComparer.Ordinal);
        foreach (DecisionBScenario s in scenarios)
        {
            Console.WriteLine();
            Console.WriteLine("sweeping: " + s.Label + " [" + s.Config + "]");
            List<DecisionBMethodResult> one = DecisionBSweepScenario(s, methods, cache);
            results.AddRange(one);
            foreach (DecisionBMethodResult mr in one)
            {
                if (mr.BestHit != null)
                {
                    DecisionBTimeHit(s, mr.Method, mr.BestHit);
                }
            }
        }

        // Events: production scenarios with a touchdown/SOI event.
        var eventRows = new List<DecisionBEventRow>();
        var eventMap = new Dictionary<string, DecisionBEventResult>(StringComparer.Ordinal);
        var eventFloors = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (DecisionBScenario s in scenarios)
        {
            if (string.IsNullOrEmpty(s.EventKind) || s.Sensitivity || s.Config != "production")
            {
                continue;
            }

            double hA = s.EventKind == "touchdown" ? 0.002d : 60d;
            double hB = s.EventKind == "touchdown" ? 0.001d : 30d;
            double callsA;
            double callsB;
            double refA = DecisionBReferenceEventTime(s, hA, out callsA);
            double refB = DecisionBReferenceEventTime(s, hB, out callsB);
            double floor = Math.Abs(refA - refB);
            eventFloors[s.Label] = floor;
            eventFloors[s.Label + "|ref"] = refA;
            Console.WriteLine("  event reference " + s.Label + ": t="
                + (double.IsFinite(refA) ? refA.ToString("R", CultureInfo.InvariantCulture) : "NaN")
                + " s floor=" + (double.IsFinite(floor) ? floor.ToString("E3", CultureInfo.InvariantCulture) : "NaN"));

            foreach (DecisionBMethodResult mr in results)
            {
                if (mr.Scenario != s || mr.Status == "n/a" || mr.BestHit == null)
                {
                    continue;
                }

                DecisionBEventResult ev = mr.Method.Production
                    ? DecisionBFindEventProduction(s, mr.Method.Kernel, mr.BestHit.Control,
                        DecisionBEffectiveMaxStep(s, mr.Method))
                    : DecisionBFindEventStand(s, mr.Method, mr.BestHit.Control,
                        DecisionBEffectiveMaxStep(s, mr.Method));
                eventMap[s.Label + "|" + mr.Method.Name] = ev;
                eventRows.Add(new DecisionBEventRow
                {
                    Scenario = s.Label,
                    Regime = s.Regime,
                    Method = mr.Method.Name,
                    Control = mr.BestHit.Control,
                    Kind = s.EventKind,
                    TargetSeconds = s.EventTargetSeconds,
                    TimeSeconds = ev.Time,
                    ErrorSeconds = double.IsFinite(ev.Time) && double.IsFinite(refA)
                        ? Math.Abs(ev.Time - refA)
                        : double.NaN,
                    FloorSeconds = floor,
                    CostTotal = ev.CostTotal,
                    Status = ev.Status,
                    Note = ev.Note
                });
            }
        }

        List<DecisionBWarpRow> warpRows = DecisionBWarpTable(scenarios);
        List<DecisionBValidityRow> validity = DecisionBValidityChecks(scenarios, results, methods, cache);
        List<DecisionBIsolationRow> isolation = DecisionBHeliocentricIsolation(scenarios);

        DecisionBWriteRunsCsv(runsPath, results);
        DecisionBWriteMatrixCsv(matrixPath, scenarios, results, eventMap, eventFloors);
        DecisionBWriteCoverageCsv(coveragePath, scenarios, results);
        DecisionBWriteEventsCsv(eventsPath, eventRows);
        DecisionBWriteWarpCsv(warpPath, warpRows);
        DecisionBWriteIsolationCsv(isolationPath, isolation);
        DecisionBWriteIsolationMd(isolationMd, isolation);

        List<DecisionBMatrixRow> matrix = DecisionBReadMatrix(matrixPath);
        List<DecisionBTableRow> table = DecisionBBuildTable(matrix);
        // Tie-class, tie-break, selection-metric and per-scenario parity rows
        // need the built table or the executed run list, so the validity CSV is
        // written after them (the reference/parity rows are already in `validity`).
        DecisionBAppendTieClassChecks(validity, table);
        DecisionBAppendTieBreakChecks(validity, table);
        DecisionBAppendSelectionMetricChecks(validity, results, matrix);
        validity.AddRange(DecisionBProductionParityAll(scenarios, results, matrix, validity));
        DecisionBWriteValidityCsv(validityPath, validity);
        DecisionBWriteTableCsv(tablePath, table);
        DecisionBWriteTableMd(tableMd, table, matrix);
        DecisionBWriteDecisionDetailsMd(detailsMd, table, matrix);
        DecisionBWriteSensitivityCsv(sensitivityPath, matrix);

        List<DecisionBDominanceRow> dominance = DecisionBDominanceAudit(
            matrix, scenarios, results, methods, cache);
        DecisionBWriteDominanceCsv(dominancePath, dominance);
        DecisionBWriteDominanceMd(dominanceMd, dominance);

        DecisionBWriteMarkdown(matrixMd, matrixPath, coveragePath, tablePath,
            dominancePath, isolationPath, validityPath, eventsPath, warpPath);

        Console.WriteLine();
        Console.WriteLine("files:");
        foreach (string p in new[]
        {
            runsPath, matrixPath, coveragePath, tablePath, dominancePath, dominanceMd,
            isolationPath, isolationMd, validityPath, eventsPath, warpPath, sensitivityPath,
            tableMd, detailsMd, matrixMd
        })
        {
            Console.WriteLine("  " + p);
        }

        // ---- Acceptance checks ----
        int productionScenarios = scenarios.Count(s => !s.Sensitivity && s.Config == "production");
        if (string.IsNullOrWhiteSpace(scenarioFilter))
        {
            Check(productionScenarios >= 6, "Test560b: at least six production regimes",
                productionScenarios.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Console.WriteLine("filtered run: at least six production regimes not required ("
                + productionScenarios.ToString(CultureInfo.InvariantCulture) + ")");
        }
        Check(StudyRegistry.All().Count >= 15, "Test560b: registry methods present",
            StudyRegistry.All().Count.ToString(CultureInfo.InvariantCulture));

        int methodCount = methods.Count;
        var coverage = DecisionBReadSimple(coveragePath);
        var coveredMethods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in coverage)
        {
            if (c.ContainsKey("method")) coveredMethods.Add(c["method"]);
        }

        Check(coveredMethods.Count == methodCount, "Test560b: every method appears in coverage",
            coveredMethods.Count.ToString(CultureInfo.InvariantCulture) + "/"
            + methodCount.ToString(CultureInfo.InvariantCulture));

        var registryNames = new List<string>();
        foreach (IStudyIntegrator integ in StudyRegistry.All())
        {
            registryNames.Add(integ.Name);
        }

        var missingRegistry = registryNames.FindAll(n => !coveredMethods.Contains(n));
        bool fullRegistryExpected = string.IsNullOrWhiteSpace(methodFilter);
        Check(!fullRegistryExpected || missingRegistry.Count == 0,
            "Test560b: every StudyRegistry method in coverage",
            !fullRegistryExpected ? "filtered methods: not required"
                : (missingRegistry.Count == 0 ? registryNames.Count.ToString(CultureInfo.InvariantCulture) + " registry names"
                    : string.Join(", ", missingRegistry)));

        var allowedCoverageStatus = new HashSet<string>(StringComparer.Ordinal)
        {
            "ranked", "n/a", "unresolved", "not run"
        };
        var badCoverageStatus = coverage.FindAll(c => c.ContainsKey("status")
            && !allowedCoverageStatus.Contains(c["status"]));
        Check(badCoverageStatus.Count == 0, "Test560b: coverage status vocabulary",
            badCoverageStatus.Count == 0 ? "ranked / n/a / unresolved / not run"
                : string.Join(", ", badCoverageStatus.Select(c => c["method"] + ":" + c["status"])));

        int edgeRows = matrix.Count(r => r.EdgeStatus == "edge" || r.EdgeStatus == "edge-selected");
        Check(edgeRows == 0 || table.Exists(t => double.IsFinite(t.CheapestEdgeTotal)),
            "Test560b: edge runs are visible in the table",
            edgeRows.ToString(CultureInfo.InvariantCulture) + " edge row(s)");

        bool surfaceSelected = scenarios.Exists(s => s.Regime == "surface");
        bool hasHover = scenarios.Any(s => s.Label.IndexOf("Surface hover", StringComparison.Ordinal) >= 0
            && s.Config == "production" && !s.Sensitivity);
        bool hasLevel = scenarios.Any(s => s.Label.IndexOf("Surface level flight", StringComparison.Ordinal) >= 0
            && s.Config == "production" && !s.Sensitivity);
        Check(!surfaceSelected || hasHover, "Test560b: surface hover scenario present",
            surfaceSelected ? (hasHover ? "yes" : "missing") : "filtered out");
        Check(!surfaceSelected || hasLevel, "Test560b: surface level-flight scenarios present",
            surfaceSelected ? (hasLevel ? "yes" : "missing") : "filtered out");

        bool hasPortColumn = File.Exists(tablePath)
            && File.ReadLines(tablePath).FirstOrDefault() != null
            && File.ReadLines(tablePath).First().Contains("best_ported");
        Check(hasPortColumn, "Test560b: cost_if_ported and best_ported in table",
            hasPortColumn ? "column present" : "missing");

        var badTieClasses = table.FindAll(t => t.BestAll.StartsWith("tie:", StringComparison.Ordinal)
            && t.BestAll.IndexOf("[production]", StringComparison.Ordinal) >= 0
            && (t.BestAllClass == "stand-only" || t.BestAllClass == "tie: stand-only"));
        Check(badTieClasses.Count == 0, "Test560b: mixed tie class includes production",
            badTieClasses.Count == 0 ? "no stand-only mixed ties"
                : string.Join(", ", badTieClasses.Select(t => t.Scenario + ":" + t.BestAllClass)));

        bool farSelected = scenarios.Exists(s => s.Regime == "far-flight");
        bool helioSelected = scenarios.Exists(s => s.Regime == "heliocentric");
        bool farBoth = scenarios.Exists(s => s.Regime == "far-flight" && s.Config == "production" && !s.Sensitivity)
            && scenarios.Exists(s => s.Regime == "far-flight" && s.Config == "regime86400");
        bool helioBoth = scenarios.Exists(s => s.Regime == "heliocentric" && s.Config == "production"
                && s.Label.IndexOf("AU", StringComparison.Ordinal) >= 0 && !s.Sensitivity)
            && scenarios.Exists(s => s.Regime == "heliocentric" && s.Config == "regime86400"
                && s.Label.IndexOf("AU", StringComparison.Ordinal) >= 0);
        Check(!farSelected || farBoth, "Test560b: far flight has production-600 and regime86400",
            farSelected ? (farBoth ? "both" : "missing") : "filtered out");
        Check(!helioSelected || helioBoth, "Test560b: heliocentric has production-600 and regime86400",
            helioSelected ? (helioBoth ? "both" : "missing") : "filtered out");

        bool leo300Selected = scenarios.Exists(s => s.Label.IndexOf("LEO 300 km", StringComparison.Ordinal) >= 0
            && !s.Sensitivity);
        bool dopri5Game = validity.Exists(v => v.Check == "production_parity_game_default"
            && v.Method == "DOPRI5" && v.Scenario.IndexOf("LEO 300", StringComparison.Ordinal) >= 0);
        bool dopri5Matched = validity.Exists(v => v.Check == "production_parity_matched_atol"
            && v.Method == "DOPRI5");
        bool localFrameRow = validity.Exists(v => v.Check == "local_frame_error_control");
        Check(!leo300Selected || dopri5Game, "Test560b: DOPRI5 game-default parity row",
            leo300Selected ? (dopri5Game ? "present" : "missing") : "filtered out");
        Check(!leo300Selected || dopri5Matched, "Test560b: DOPRI5 matched-atol parity row",
            leo300Selected ? (dopri5Matched ? "present" : "missing") : "filtered out");
        Check(!leo300Selected || localFrameRow, "Test560b: local_frame_error_control row",
            leo300Selected ? (localFrameRow ? "present" : "missing") : "filtered out");

        var referenceScenarios = scenarios.FindAll(
            s => s.Config == "production" || s.Config == "regime86400");
        int ratioOk = referenceScenarios.Count(s => s.Refs.LocalAsymptotic);
        int resolvedFinest = referenceScenarios.Count(s => !s.Refs.LocalAsymptotic
            && s.Refs.LocalUnratioedResolutionUsed);
        int notCertified = referenceScenarios.Count(s => !s.Refs.LocalValid || !s.Refs.LocalResolved);
        Console.WriteLine("references (" + referenceScenarios.Count.ToString(CultureInfo.InvariantCulture)
            + " production/regime configs): " + ratioOk.ToString(CultureInfo.InvariantCulture) + " ratio_ok, "
            + resolvedFinest.ToString(CultureInfo.InvariantCulture) + " resolved_finest_delta, "
            + notCertified.ToString(CultureInfo.InvariantCulture) + " not_certified");
        Check(notCertified == 0,
            "Test560b: every reference is ratio_ok or resolved_finest_delta (unratioed is not called certified)",
            ratioOk.ToString(CultureInfo.InvariantCulture) + " ratio_ok, "
            + resolvedFinest.ToString(CultureInfo.InvariantCulture) + " resolved_finest_delta, "
            + notCertified.ToString(CultureInfo.InvariantCulture) + " not_certified");

        Check(table.Count > 0, "Test560b: decision table generated from CSV",
            table.Count.ToString(CultureInfo.InvariantCulture) + " row(s)");
        Check(File.Exists(detailsMd), "Test560b: decision details generated", detailsMd);
        Check(File.Exists(dominancePath), "Test560b: dominance audit exists", dominancePath);

        // Production chunk contract.
        DecisionBScenario leo = scenarios.Find(x => x.Label.IndexOf("LEO 200", StringComparison.Ordinal) >= 0
            && x.Config == "production" && !x.Sensitivity);
        if (leo != null)
        {
            double coast = leo.ChunkAt(StudyShipT0 + 100d);
            double burn = leo.ChunkAt(leo.Case.ThrustStart + 1d);
            Check(Math.Abs(coast - 600d) < 1e-9d && Math.Abs(burn - 0.05d) < 1e-9d,
                "Test560b: LEO coast/burn chunks", coast.ToString("G6", CultureInfo.InvariantCulture)
                + " / " + burn.ToString("G6", CultureInfo.InvariantCulture));
        }

        DecisionBScenario surface = scenarios.Find(x => x.Label.IndexOf("Surface ascent", StringComparison.Ordinal) >= 0
            && x.Config == "production" && !x.Sensitivity);
        if (surface != null)
        {
            Check(Math.Abs(surface.ChunkAt(StudyShipT0 + 10d) - 0.05d) < 1e-9d,
                "Test560b: surface flight chunk 0.05 s", "0.05");
        }

        bool parityPass = false;
        bool determinismPass = false;
        bool chunkPass = false;
        bool defaultPass = false;
        foreach (DecisionBValidityRow v in validity)
        {
            if (v.Check.StartsWith("production_parity", StringComparison.Ordinal) && v.Status == "PASS") parityPass = true;
            if (v.Check == "determinism" && v.Status == "PASS") determinismPass = true;
            if (v.Check == "chunk_invariance" && v.Status == "PASS") chunkPass = true;
            if (v.Check == "production_default" && v.Status == "PASS") defaultPass = true;
        }

        Check(defaultPass, "Test560b: production default unchanged", "DOP853");
        bool fullMatrix = productionScenarios >= 6;
        if (fullMatrix)
        {
            Check(determinismPass, "Test560b: determinism", "exact err/cost");
            Check(parityPass, "Test560b: production parity", "<= target/10");
            Check(chunkPass, "Test560b: chunk invariance", "<= 10 x target");
        }
        else
        {
            Console.WriteLine("filtered run: determinism/parity/chunk checks not required");
        }

        var verdict = dominance.Find(x => x.Check == "verdict");
        Check(verdict != null, "Test560b: dominance verdict present", verdict == null ? "missing" : verdict.Note);
        Console.WriteLine("dominance: " + (verdict == null ? "missing" : verdict.Note));

        var localFrame = validity.Find(x => x.Check == "local_frame_error_control");
        var dopriGameRow = validity.Find(x => x.Check == "production_parity_game_default"
            && x.Method == "DOPRI5" && x.Scenario.IndexOf("LEO 300", StringComparison.Ordinal) >= 0);
        var dopriMatchedRow = validity.Find(x => x.Check == "production_parity_matched_atol"
            && x.Method == "DOPRI5");
        Console.WriteLine("DOPRI5 game-default parity: "
            + (dopriGameRow == null ? "missing" : dopriGameRow.Status + " value="
                + dopriGameRow.Value.ToString("E3", CultureInfo.InvariantCulture)));
        Console.WriteLine("DOPRI5 matched-atol parity: "
            + (dopriMatchedRow == null ? "missing" : dopriMatchedRow.Status + " value="
                + dopriMatchedRow.Value.ToString("E3", CultureInfo.InvariantCulture)));
        Console.WriteLine("local_frame_error_control: "
            + (localFrame == null ? "missing"
                : localFrame.Status + " value=" + localFrame.Value.ToString("E3", CultureInfo.InvariantCulture)
                  + " threshold=" + localFrame.Threshold.ToString("E3", CultureInfo.InvariantCulture)
                  + " (" + (localFrame.Threshold > 0d
                      ? (100d * localFrame.Value / localFrame.Threshold).ToString("F2", CultureInfo.InvariantCulture) + "% of threshold"
                      : "n/a") + ")"));

        int findingCount = validity.Count(v => v.Status == "FINDING");
        int borderlineCount = validity.Count(v => v.Status == "BORDERLINE");
        if (findingCount > 0 || borderlineCount > 0)
        {
            Console.WriteLine("validity findings/borderlines:");
            foreach (DecisionBValidityRow v in validity)
            {
                if (v.Status == "FINDING" || v.Status == "BORDERLINE")
                {
                    Console.WriteLine("  " + v.Status + " " + v.Check + " / " + v.Scenario
                        + " value=" + v.Value.ToString("E3", CultureInfo.InvariantCulture)
                        + " threshold=" + v.Threshold.ToString("E3", CultureInfo.InvariantCulture));
                }
            }
        }

        bool isolationOk = isolation.Exists(x => x.Variant == "forced-sun" && x.Status == "ok");
        Console.WriteLine("heliocentric isolation rows: " + isolation.Count.ToString(CultureInfo.InvariantCulture)
            + (isolationOk ? " (forced-Sun measured)" : " (not measured)"));

        Console.WriteLine("borderline rows: " + borderlineCount.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("done: " + (failures == 0
            ? "ALL PASS (" + findingCount.ToString(CultureInfo.InvariantCulture) + " FINDING"
              + (findingCount == 1 ? "" : "S") + ")"
            : failures.ToString(CultureInfo.InvariantCulture) + " FAILURE(S)"));
        return 0;
    }

    private static void DecisionBWriteTableMd(
        string path, List<DecisionBTableRow> table, List<DecisionBMatrixRow> matrix)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Test560b - decision table (compact)");
        sb.AppendLine();
        sb.AppendLine("Generated from `Test560b_DecisionMatrix.csv`; all numbers are executed runs.");
        sb.AppendLine("`best_all` uses `cost_if_ported` for stand candidates; `best_production` uses");
        sb.AppendLine("`cost_total`. Full acceptable percentages and selected totals are in");
        sb.AppendLine("`Test560b_DecisionTable.csv` and `Test560b_DecisionDetails.md`.");
        sb.AppendLine("`scope` is `config`/`max_step`/`chunk`; `bound` is the winner's heuristic label");
        sb.AppendLine("and `production_bound` is the selected production row's label. `chunk` when");
        sb.AppendLine("steps <= 1.2*chunks, `max-step` when");
        sb.AppendLine("0.8*(duration/maxStep) <= steps <= 1.2*(duration/maxStep), otherwise `tolerance`.");
        sb.AppendLine("It is not a proof of the controller state.");
        sb.AppendLine("Per-method fixed-step `h_req`/`h_avg` is in the dedicated table");
        sb.AppendLine("\"Fixed-step h_req vs h_avg\" below.");
        sb.AppendLine();
        sb.AppendLine("## Core decision table");
        sb.AppendLine();
        DecisionBAppendCompactTable(sb, table.FindAll(t => !DecisionBIsPrescribed(t)));
        sb.AppendLine();

        var prescribed = table.FindAll(DecisionBIsPrescribed);
        if (prescribed.Count > 0)
        {
            sb.AppendLine("## Prescribed-force surface hover/level (separate section)");
            sb.AppendLine();
            sb.AppendLine("These are prescribed-force trajectories, not closed-loop altitude hold;");
            sb.AppendLine("`ThrustScalesWithMass` uses a linear mass estimate for the force schedule.");
            sb.AppendLine();
            DecisionBAppendCompactTable(sb, prescribed);
            sb.AppendLine();
        }

        sb.AppendLine("## Sensitivity (separate from the decision table)");
        sb.AppendLine();
        sb.AppendLine("| scenario | config | method | target | err | total | status |");
        sb.AppendLine("|---|---|---|---|---:|---:|---|");
        foreach (DecisionBMatrixRow r in matrix)
        {
            if (!r.Sensitivity && r.Config != "chunk-sweep")
            {
                continue;
            }

            double err = r.TargetKind == "velocity" ? r.ErrMaxV : r.ErrMaxM;
            sb.Append("| ").Append(DecisionBCsvField(r.Scenario)).Append(" | ")
                .Append(r.Config).Append(" | ").Append(r.Method).Append(" | ")
                .Append(DecisionBFormatTarget(r.Target, r.TargetKind)).Append(" | ")
                .Append(double.IsFinite(err) ? err.ToString("E3", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(DecisionBCsvNumber(r.CostTotal)).Append(" | ")
                .Append(r.Status).AppendLine(" |");
        }

        sb.AppendLine();
        DecisionBAppendMaxStepSection(sb, table, matrix);
        DecisionBAppendFixedStepSection(sb, matrix);
        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBWriteDecisionDetailsMd(
        string path, List<DecisionBTableRow> table, List<DecisionBMatrixRow> matrix)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Test560b - decision details");
        sb.AppendLine();
        sb.AppendLine("Per-row detail for `Test560b_DecisionTable.csv`: acceptable percentages,");
        sb.AppendLine("selected_best_all_total, selected_production_total, edge policy, reference");
        sb.AppendLine("certificate, fixed-step h_req/h_avg and the heuristic bound label.");
        sb.AppendLine();
        foreach (DecisionBTableRow t in table)
        {
            sb.AppendLine("## " + t.Scenario);
            sb.AppendLine();
            sb.AppendLine("- regime/config: `" + t.Regime + "` / `" + t.Config + "`");
            sb.AppendLine("- target: " + DecisionBFormatTarget(t.Target, t.TargetKind));
            sb.AppendLine("- scope/bound: `" + t.Scope + "`; winner bound `" + t.Bound
                + "`, production bound `" + t.ProductionBound + "` (both heuristic)");
            sb.AppendLine("- reference: " + (string.IsNullOrEmpty(t.ReferenceResolution) ? "n/a" : t.ReferenceResolution)
                + ", floor_m " + DecisionBCsvNumber(t.FloorM)
                + ", floor_v " + DecisionBCsvNumber(t.FloorV));
            sb.AppendLine("- best_production: `" + t.BestProduction + "` at "
                + DecisionBCsvNumber(t.BestProductionCost)
                + " (kind: " + (string.IsNullOrEmpty(t.BestProductionKind) ? "n/a" : t.BestProductionKind) + ")");
            sb.AppendLine("- best_ported: `" + t.BestPorted + "` at "
                + DecisionBCsvNumber(t.BestPortedCost));
            sb.AppendLine("- best_stand_raw: `" + t.BestStandRaw + "` at "
                + DecisionBCsvNumber(t.BestStandRawCost));
            sb.AppendLine("- best_all: `" + t.BestAll + "` at "
                + DecisionBCsvNumber(t.BestAllCost) + " (" + t.BestAllClass + ")");
            sb.AppendLine("- selected_best_all_total: " + DecisionBCsvNumber(t.SelectedBestAllTotal));
            sb.AppendLine("- selected_production: `" + t.SelectedProductionMethod + "` at "
                + DecisionBCsvNumber(t.SelectedProductionTotal));
            sb.AppendLine("- selected_production_vs_best_all: "
                + (double.IsFinite(t.ProductionVsBestAllPct) ? DecisionBPct(t.ProductionVsBestAllPct) + "%" : "n/a")
                + "; best_all_vs_production: "
                + (double.IsFinite(t.BestAllVsProductionPct) ? DecisionBPct(t.BestAllVsProductionPct) + "%" : "n/a"));
            sb.AppendLine("- cheapest_edge: `" + t.CheapestEdgeMethod + "` at "
                + DecisionBCsvNumber(t.CheapestEdgeTotal));
            var fixedRows = matrix.FindAll(r => r.Scenario == t.Scenario && r.Config == t.Config
                && Math.Abs(r.Target - t.Target) <= 1e-12d && r.TargetKind == t.TargetKind
                && r.ControlKind == "h" && (r.Status == "ok" || r.Status == "edge")
                && double.IsFinite(r.HAvg));
            if (fixedRows.Count > 0)
            {
                var parts = new List<string>();
                foreach (DecisionBMatrixRow r in fixedRows)
                {
                    parts.Add(r.Method + " h_req=" + DecisionBCsvNumber(DecisionBParseDouble(r.Control))
                        + " s, h_avg=" + DecisionBCsvNumber(r.HAvg) + " s");
                }

                sb.AppendLine("- fixed_step_h: " + string.Join("; ", parts));
            }

            sb.AppendLine("- edge_policy: " + t.EdgePolicy);
            sb.AppendLine("- acceptable: " + t.Acceptable);
            sb.AppendLine("- n/a: " + t.Na);
            sb.AppendLine("- unresolved: " + t.Unresolved);
            sb.AppendLine("- not_run: " + t.NotRun);
            sb.AppendLine("- why: " + t.Why);
            sb.AppendLine();
        }

        DecisionBAppendMaxStepSection(sb, table, matrix);
        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBWriteDominanceMd(string path, List<DecisionBDominanceRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Test560b - dominance audit");
        sb.AppendLine();
        var verdict = rows.Find(x => x.Check == "verdict");
        sb.AppendLine("**Verdict (all methods):** " + (verdict == null ? "missing" : verdict.Note));
        var prodVerdict = rows.Find(x => x.Check == "production_verdict");
        sb.AppendLine();
        sb.AppendLine("**Production-only verdict:** " + (prodVerdict == null ? "missing" : prodVerdict.Note));
        sb.AppendLine();
        sb.AppendLine("`cell_winner` and `dominance_count` use raw `cost_total`; `cost_if_ported`");
        sb.AppendLine("is a separate `cost_metric` row. The decision table uses `cost_if_ported`");
        sb.AppendLine("for any stand/production comparison.");
        sb.AppendLine();
        sb.AppendLine("| check | scenario | target | winner | value | note |");
        sb.AppendLine("|---|---|---|---|---:|---|");
        foreach (DecisionBDominanceRow r in rows)
        {
            if (r.Check != "dominance_count" && r.Check != "chunk_sweep"
                && r.Check != "cost_metric" && r.Check != "target_sweep_executed" && r.Check != "cell_winner"
                && !r.Check.StartsWith("production_", StringComparison.Ordinal))
            {
                continue;
            }

            sb.Append("| ").Append(r.Check).Append(" | ").Append(DecisionBCsvField(r.Scenario)).Append(" | ")
                .Append(DecisionBCsvField(r.Target)).Append(" | ").Append(DecisionBCsvField(r.Winner)).Append(" | ")
                .Append(DecisionBCsvNumber(r.Value)).Append(" | ").Append(DecisionBCsvField(r.Note)).AppendLine(" |");
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBWriteIsolationMd(string path, List<DecisionBIsolationRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Test560b - heliocentric 5 AU / 10 y isolation");
        sb.AppendLine();
        sb.AppendLine("The production `auto` local frame is the current game path. `forced-sun` and");
        sb.AppendLine("`absolute` isolate the dominant-body switch. All rows are compared to the same");
        sb.AppendLine("Sun-centred certified reference.");
        sb.AppendLine();
        sb.AppendLine("| variant | method | dominant | frame | max step | chunk | err max m | cost total | status | note |");
        sb.AppendLine("|---|---|---|---|---:|---:|---:|---:|---|---|");
        foreach (DecisionBIsolationRow r in rows)
        {
            sb.Append("| ").Append(r.Variant).Append(" | ").Append(r.Method).Append(" | ")
                .Append(r.Dominant).Append(" | ").Append(r.UseFrame).Append(" | ")
                .Append(DecisionBCsvNumber(r.MaxStep)).Append(" | ").Append(DecisionBCsvNumber(r.Chunk)).Append(" | ")
                .Append(DecisionBCsvNumber(r.ErrMaxM)).Append(" | ").Append(DecisionBCsvNumber(r.CostTotal)).Append(" | ")
                .Append(r.Status).Append(" | ").Append(r.Note).AppendLine(" |");
        }

        double auto = double.NaN;
        double forced = double.NaN;
        double absolute = double.NaN;
        foreach (DecisionBIsolationRow r in rows)
        {
            if (r.Variant == "auto") auto = r.ErrMaxM;
            if (r.Variant == "forced-sun") forced = r.ErrMaxM;
            if (r.Variant == "absolute") absolute = r.ErrMaxM;
        }

        sb.AppendLine();
        if (double.IsFinite(auto) && double.IsFinite(forced)
            && auto > 1e5d && forced < 1e3d)
        {
            sb.AppendLine("**Finding:** the auto dominant-body switch is the outlier; forced-Sun and");
            sb.AppendLine("absolute agree with the reference. This is a production frame-switch bug, not");
            sb.AppendLine("an integrator ranking. The decision table uses the forced-Sun comparison and");
            sb.AppendLine("keeps the auto row visible.");
        }
        else
        {
            sb.AppendLine("**Finding:** not classified as a dominant-body bug by the auto/forced/absolute");
            sb.AppendLine("comparison; see the numbers above.");
        }

        File.WriteAllText(path, sb.ToString());
    }
}
