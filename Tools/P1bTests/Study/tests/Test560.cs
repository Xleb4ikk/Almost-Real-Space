using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Spacecraft;

// Test560: mode -> integrator -> accuracy -> cost -> confidence.
// Measurement only; the production default is never changed.
internal static partial class P1bTests
{
    private static int Test560_IntegratorDecisionMatrix()
    {
        Console.WriteLine("--- Integrator decision matrix (production path + stand-only) ---");
        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        string runsPath = Path.Combine(dir, "Test560_Runs.csv");
        string matrixPath = Path.Combine(dir, "Test560_DecisionMatrix.csv");
        string decisionPath = Path.Combine(dir, "Test560_DecisionTable.csv");
        string mdPath = Path.Combine(dir, "Test560_DecisionMatrix.md");
        string validityPath = Path.Combine(dir, "Test560_Validity.csv");
        string eventPath = Path.Combine(dir, "Test560_Events.csv");
        string warpPath = Path.Combine(dir, "Test560_Warp.csv");

        if (string.Equals(DecisionEnv("P1B560_REGENERATE"), "1", StringComparison.Ordinal))
        {
            if (!File.Exists(matrixPath))
            {
                Console.WriteLine("regenerate: " + matrixPath + " is missing");
                return 1;
            }

            List<DecisionTableRow> regenerated = DecisionReadTable(matrixPath);
            DecisionWriteDecisionCsv(decisionPath, regenerated);
            DecisionWriteMarkdown(mdPath, matrixPath, decisionPath, validityPath, eventPath, warpPath);
            Console.WriteLine("regenerated decision table and Markdown from " + matrixPath);
            return 0;
        }

        string pinPath = Path.Combine(dir, "Test560_kepler_weights.csv");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("P1B_KEPLER_WEIGHTS")))
        {
            Environment.SetEnvironmentVariable("P1B_KEPLER_WEIGHTS", pinPath);
        }

        List<DecisionScenario> scenarios = DecisionBuildScenarios();
        string regimeFilter = DecisionEnv("P1B560_REGIMES");
        string methodFilter = DecisionEnv("P1B560_METHODS");
        if (!string.IsNullOrWhiteSpace(regimeFilter))
        {
            scenarios = scenarios.FindAll(s =>
                DecisionSelected(s.Regime, regimeFilter) || DecisionSelected(s.Label, regimeFilter));
        }

        List<DecisionMethod> methods = DecisionMethods();
        if (!string.IsNullOrWhiteSpace(methodFilter))
        {
            methods = methods.FindAll(m => DecisionSelected(m.Name, methodFilter));
        }

        Console.WriteLine("scenarios: " + scenarios.Count.ToString(CultureInfo.InvariantCulture)
            + ", methods: " + methods.Count.ToString(CultureInfo.InvariantCulture)
            + (DecisionQuick() ? " (quick grid)" : ""));

        foreach (DecisionScenario s in scenarios)
        {
            if (s.Regime == "high-orbit" || s.Regime == "cislunar"
                || s.Regime == "heliocentric" || s.Regime == "far-flight")
            {
                var options = new StudyOptions();
                options.KeplerWeight = double.NaN;
                StudyKeplerWeight kw = StudyCalibration.Resolve(s.Problem, options);
                s.KeplerWeight = kw.Weight;
                s.KeplerCv = kw.Cv;
                s.KeplerSource = kw.Source;
                Console.WriteLine("  Kepler weight " + s.Label + ": "
                    + s.KeplerWeight.ToString("G6", CultureInfo.InvariantCulture)
                    + " (CV " + s.KeplerCv.ToString("G3", CultureInfo.InvariantCulture)
                    + ", " + s.KeplerSource + ")");
            }
        }

        var results = new List<DecisionMethodResult>();
        foreach (DecisionScenario s in scenarios)
        {
            Console.WriteLine();
            Console.WriteLine("sweeping: " + s.Label);
            List<DecisionMethodResult> one = DecisionSweepScenario(s, methods);
            results.AddRange(one);
            foreach (DecisionMethodResult mr in one)
            {
                if (mr.BestHit != null)
                {
                    DecisionTimeHit(s, mr.Method, mr.BestHit);
                }
            }
        }

        // ---- Event times (touchdown / SOI exit) ----
        var eventRows = new List<DecisionEventRow>();
        var eventMap = new Dictionary<string, DecisionEventResult>(StringComparer.Ordinal);
        var eventFloors = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (DecisionScenario s in scenarios)
        {
            if (string.IsNullOrEmpty(s.EventKind))
            {
                continue;
            }

            double hA = s.EventKind == "touchdown" ? 0.002d : 60d;
            double hB = s.EventKind == "touchdown" ? 0.001d : 30d;
            double callsA;
            double callsB;
            double refA = DecisionReferenceEventTime(s, hA, out callsA);
            double refB = DecisionReferenceEventTime(s, hB, out callsB);
            double floor = Math.Abs(refA - refB);
            eventFloors[s.Label] = floor;
            eventFloors[s.Label + "|ref"] = refA;
            Console.WriteLine("  event reference " + s.Label + ": t="
                + (double.IsFinite(refA) ? refA.ToString("R", CultureInfo.InvariantCulture) : "NaN")
                + " s, floor=" + (double.IsFinite(floor) ? floor.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                + " s, target=" + s.EventTargetSeconds.ToString("G3", CultureInfo.InvariantCulture) + " s");

            foreach (DecisionMethodResult mr in results)
            {
                if (mr.Scenario != s || mr.Status == "unsupported")
                {
                    continue;
                }

                DecisionRun controlRun = mr.BestHit;
                if (controlRun == null)
                {
                    foreach (DecisionRun r in mr.Runs)
                    {
                        if (r.Status == "ok" && r.Finite)
                        {
                            controlRun = r;
                        }
                    }
                }

                if (controlRun == null)
                {
                    continue;
                }

                DecisionEventResult ev = mr.Method.Production
                    ? DecisionFindEventProduction(s, mr.Method.Kernel, controlRun.Control)
                    : DecisionFindEventStand(s, mr.Method, controlRun.Control);
                eventMap[s.Label + "|" + mr.Method.Name] = ev;

                var row = new DecisionEventRow
                {
                    Scenario = s.Label,
                    Regime = s.Regime,
                    Method = mr.Method.Name,
                    Control = controlRun.Control,
                    Kind = s.EventKind,
                    TargetSeconds = s.EventTargetSeconds,
                    TimeSeconds = ev.Time,
                    ErrorSeconds = double.IsFinite(ev.Time) && double.IsFinite(refA)
                        ? Math.Abs(ev.Time - refA)
                        : double.NaN,
                    FloorSeconds = floor,
                    CostRhs = ev.CostRhs,
                    Status = ev.Status,
                    Note = ev.Note
                };
                eventRows.Add(row);
                Console.WriteLine("    event " + s.Label + " / " + mr.Method.Name + ": "
                    + (double.IsFinite(row.ErrorSeconds)
                        ? row.ErrorSeconds.ToString("E3", CultureInfo.InvariantCulture) + " s"
                        : ev.Status)
                    + " (cost " + row.CostRhs.ToString("N0", CultureInfo.InvariantCulture) + ")");
            }
        }

        if (string.Equals(DecisionEnv("P1B560_DIAG"), "2", StringComparison.Ordinal))
        {
            DecisionScenario leoDiag = scenarios.Find(x => x.Label.IndexOf("LEO 300", StringComparison.Ordinal) >= 0);
            if (leoDiag != null)
            {
                foreach (string kernel in new[] { "DOPRI5-adaptive", "DOP853-adaptive" })
                {
                    var dm = new DecisionMethod
                    {
                        Name = kernel,
                        Class = "stand-only",
                        Kernel = kernel,
                        Stand = DecisionStand(kernel)
                    };
                    DecisionRun diag = DecisionRunStand(leoDiag, dm, 1e-12d, true);
                    Console.WriteLine("  diag stand " + kernel + " LEO 300: status=" + diag.Status
                        + " err=" + (diag.Finite ? diag.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                        + " cost=" + diag.CostRhs.ToString("N0", CultureInfo.InvariantCulture));
                }
            }
        }

        // ---- Warp cost ----
        List<DecisionWarpRow> warpRows = DecisionWarpTable(scenarios, results);

        // ---- Validity ----
        List<DecisionValidityRow> validity = DecisionValidityChecks(scenarios, results, methods);

        // ---- Output ----
        DecisionWriteRunsCsv(runsPath, results);
        DecisionWriteMatrixCsv(matrixPath, results, eventMap, eventFloors);
        List<DecisionTableRow> table = DecisionReadTable(matrixPath);
        DecisionWriteDecisionCsv(decisionPath, table);
        DecisionWriteValidityCsv(validityPath, validity);
        DecisionWriteEventCsv(eventPath, eventRows);
        DecisionWriteWarpCsv(warpPath, warpRows);
        DecisionWriteMarkdown(mdPath, matrixPath, decisionPath, validityPath, eventPath, warpPath);

        Console.WriteLine();
        Console.WriteLine("files:");
        Console.WriteLine("  " + runsPath);
        Console.WriteLine("  " + matrixPath);
        Console.WriteLine("  " + decisionPath);
        Console.WriteLine("  " + mdPath);
        Console.WriteLine("  " + validityPath);
        Console.WriteLine("  " + eventPath);
        Console.WriteLine("  " + warpPath);

        // ---- Acceptance checks ----
        Check(scenarios.Count >= 6, "Test560: at least six regimes",
            scenarios.Count.ToString(CultureInfo.InvariantCulture));
        Check(methods.Count(m => m.Production) == 2, "Test560: two production methods",
            methods.Count(m => m.Production).ToString(CultureInfo.InvariantCulture));

        int uncertified = 0;
        foreach (DecisionScenario s in scenarios)
        {
            if (!double.IsFinite(s.FloorM))
            {
                uncertified++;
                Console.WriteLine("UNCERTIFIED " + s.Label + ": " + (s.Refs.LocalKind ?? "no note"));
            }
        }

        Check(uncertified == 0, "Test560: every reference certified with a finite floor",
            uncertified.ToString(CultureInfo.InvariantCulture) + " uncertified");

        int unsupported = 0;
        int okCells = 0;
        int skipped = 0;
        int noHit = 0;
        foreach (DecisionMethodResult mr in results)
        {
            if (mr.Status == "unsupported")
            {
                unsupported++;
            }
        }

        foreach (DecisionTableRow r in table)
        {
            if (r.Confidence == "low" && r.Recommended == "unresolved")
            {
                noHit++;
            }
            else
            {
                okCells++;
            }
            if (r.Na != "—")
            {
                skipped++;
            }
        }

        Check(table.Count > 0, "Test560: decision table generated from the CSV",
            table.Count.ToString(CultureInfo.InvariantCulture) + " row(s)");
        Check(okCells > 0, "Test560: at least one target resolved by an executed run",
            okCells.ToString(CultureInfo.InvariantCulture) + " resolved");
        int withUnresolved = table.Count(r => r.Unresolved.Count > 0);
        Console.WriteLine("cells: " + okCells.ToString(CultureInfo.InvariantCulture) + " resolved, "
            + noHit.ToString(CultureInfo.InvariantCulture) + " fully unresolved, "
            + withUnresolved.ToString(CultureInfo.InvariantCulture) + " with unresolved methods, "
            + skipped.ToString(CultureInfo.InvariantCulture) + " with n/a methods, "
            + unsupported.ToString(CultureInfo.InvariantCulture) + " unsupported method/scenario pairs");

        bool parityPass = false;
        bool determinismPass = false;
        bool chunkPass = false;
        bool defaultPass = false;
        foreach (DecisionValidityRow v in validity)
        {
            if (v.Check == "production_parity" && v.Status == "PASS")
            {
                parityPass = true;
            }
            if (v.Check == "determinism" && v.Status == "PASS")
            {
                determinismPass = true;
            }
            if (v.Check == "chunk_invariance" && v.Status == "PASS")
            {
                chunkPass = true;
            }
            if (v.Check == "production_default" && v.Status == "PASS")
            {
                defaultPass = true;
            }
        }

        bool hasDeterminismScenario = scenarios.Any(s => s.Label.IndexOf("LEO 200 km", StringComparison.Ordinal) >= 0
            || s.Label.IndexOf("GEO", StringComparison.Ordinal) >= 0);
        bool hasParityScenario = scenarios.Any(s => s.Label.IndexOf("Surface ascent", StringComparison.Ordinal) >= 0
            || s.Label.IndexOf("LEO 200 km", StringComparison.Ordinal) >= 0);
        bool hasChunkScenario = scenarios.Any(s => s.Label.IndexOf("LEO 200 km", StringComparison.Ordinal) >= 0);

        Check(defaultPass, "Test560: production default is still DOP853", "SpacecraftPhysics.Integrator");
        if (hasDeterminismScenario)
        {
            Check(determinismPass, "Test560: determinism (two runs identical)", "err/cost exact");
        }
        else
        {
            Console.WriteLine("determinism: not run (scenario filtered)");
        }

        if (hasParityScenario)
        {
            Check(parityPass, "Test560: production parity (production vs stand)", "<= target/10");
        }
        else
        {
            Console.WriteLine("production parity: not run (scenario filtered)");
        }

        if (hasChunkScenario)
        {
            Check(chunkPass, "Test560: chunk invariance", "<= 10 x target");
        }
        else
        {
            Console.WriteLine("chunk invariance: not run (scenario filtered)");
        }

        int eventOk = 0;
        foreach (DecisionEventRow e in eventRows)
        {
            if (e.Status == "ok" && double.IsFinite(e.ErrorSeconds)
                && e.ErrorSeconds <= e.TargetSeconds)
            {
                eventOk++;
            }
        }

        Console.WriteLine("events: " + eventOk.ToString(CultureInfo.InvariantCulture)
            + " / " + eventRows.Count.ToString(CultureInfo.InvariantCulture) + " within target");
        if (eventRows.Count > 0)
        {
            Check(eventOk > 0, "Test560: event time measured within target",
                eventOk.ToString(CultureInfo.InvariantCulture) + " / "
                + eventRows.Count.ToString(CultureInfo.InvariantCulture));
        }

        bool anyWarpError = warpRows.Any(r => r.Status != "ok");
        Check(!anyWarpError, "Test560: warp table completed", warpRows.Count.ToString(CultureInfo.InvariantCulture) + " row(s)");

        Console.WriteLine("done: " + (failures == 0 ? "ALL PASS" : failures.ToString(CultureInfo.InvariantCulture) + " FAILURE(S)"));
        return 0;
    }

    private static bool DecisionSelected(string value, string filter)
    {
        foreach (string part in filter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string frag = part.Trim();
            if (frag.Length > 0 && value.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
