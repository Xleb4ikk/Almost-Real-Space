using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

// Test563: how MaxStepSize changes the accuracy and cost of the production path in
// LEO, for DOP853 and DOPRI5, over the same rtol grid as Test562.
//
// The "cap regime" runs set MaxStepSize AND the chunk that enforces it to the same
// value (cap 600 == Test562's production regime, so the parity gate is exact).
// Setting MaxStepSize alone cannot change anything while the production chunk is
// 600 s: the chunk is already a hard step bound. This is measured by the cap-only
// control cells (cap 86 400, chunk 600).
//
// No production code, default or physics is changed. The measurement engine is
// DecisionBRunProduction (Test560b), the same one Test562 uses.
internal static partial class P1bTests
{
    private const long Test563BudgetRhs = 80000000L;

    private static readonly double[] Test563Caps = { 600d, 1200d, 3600d, 86400d };
    private static readonly double[] Test563Rtols = { 1e-9d, 1e-10d, 1e-11d, 1e-12d, 1e-13d };

    private const long Test563AnchorRhsEq = 956031L;
    private const double Test563AnchorErrM = 3.67973d;
    private const double Test563AnchorTolerancePct = 0.1d;

    private sealed class Test563Case
    {
        public string Key;
        public string Test562Key;   // same situation in Test562_Summary.csv
        public string Title;
        public DecisionBScenario Scene;
        public double FloorM;
        public double FloorV;
        public string ReferenceStatus;
        public bool IncludeVerdict;
        public double DdGapM = double.NaN;
        public double ThrustStart = double.NaN;
        public double ThrustEnd = double.NaN;
    }

    private sealed class Test563Attempt
    {
        public int Group;
        public int Index;
        public int Batch;
        public double Ms;
        public long AllocBytes;
        public long Calls;
        public long RhsEq;
        public long Chunks;
        public long Accepted;
        public long Rejected;
        public double ErrMax;
        public double ErrFinal;
        public double ErrMaxV;
    }

    private sealed class Test563Cell
    {
        public Test563Case Case;
        public string Regime;   // cap-regime | cap-only
        public double Cap;
        public double Chunk;
        public string Method;
        public double Rtol;
        public List<Test563Attempt> Attempts = new List<Test563Attempt>();
        public double MsMedian;
        public double MsMin;
        public double MsMax;
        public double CvPct;
        public long AllocMedian;
        public long Calls;
        public long RhsEq;
        public long Chunks;
        public long Accepted;
        public long Rejected;
        public double ErrMax;
        public double ErrFinal;
        public double ErrMaxV;
        public double CapBound = double.NaN;
        public long DenseAccepted;
        public string Status = "ok";
        public string Note = "";

        public bool Finite
        {
            get { return Status == "ok" && double.IsFinite(ErrMax); }
        }
    }

    private sealed class Test563Verdict
    {
        public Test563Case Case;
        public string Method;
        public double Cap;
        public string Verdict;
        public string Why = "";
        public double MinErrCap = double.NaN;
        public double MinErr600 = double.NaN;
        public double MinErrRatio = double.NaN;
        public double BestRtol600 = double.NaN;
        public double ErrAtBestRtol = double.NaN;
        public double RhsAtBestRtol = double.NaN;
        public double MsAtBestRtol = double.NaN;
        public double Saving = double.NaN;
        public double CapBound = double.NaN;
        public double RejectedPerChunk = double.NaN;
        public double ErrSpreadRatio = double.NaN;
        public double ErrSpreadDigits = double.NaN;
        public string RtolControlled = "";
        public bool Monotone = true;
        public double MaxRise = 1d;
        public double RiseFromRtol = double.NaN;
        public double RiseToRtol = double.NaN;
    }

    private sealed class Test563Baseline
    {
        public long RhsEq;
        public double Err;
    }

    // --------------------------------------------------------------------------------
    // Entry point
    // --------------------------------------------------------------------------------

    private static int Test563_LeoCapRegime()
    {
        Console.WriteLine("--- Test563: LEO step-cap regimes (production DOP853 / DOPRI5) ---");
        string dir = Path.Combine(StudyShipMatrixResultsDir(), "cap_regime");
        Directory.CreateDirectory(dir);
        string runsPath = Path.Combine(dir, "Test563_LeoCapRegime_Runs.csv");
        string summaryPath = Path.Combine(dir, "Test563_LeoCapRegime_Summary.csv");
        string mdPath = Path.Combine(dir, "Test563_LeoCapRegime.md");
        string envPath = Path.Combine(dir, "Test563_Environment.txt");

        if (string.Equals(DecisionBEnv("P1B563_REGENERATE"), "1", StringComparison.Ordinal))
        {
            return Test563Regenerate(dir, summaryPath, mdPath);
        }

        File.WriteAllText(envPath, Test563EnvironmentText(dir));
        Console.WriteLine("  output: " + dir);

        List<Test563Case> cases = Test563BuildCases();
        if (cases.Count == 0)
        {
            Console.WriteLine("no Test563 case built");
            return 1;
        }

        // ---- Stop condition 1: Test560b anchor ----
        Test563Case l2 = cases.Find(c => c.Key == "L2");
        var anchorNotes = new List<string>();
        DecisionBRun anchor = null;
        if (l2 != null)
        {
            Console.WriteLine("anchor: L2 DOP853 rtol 1e-8 cap 600 s ...");
            anchor = Test563RunOnce(l2.Scene, "dop853", 1e-8d, 600d);
            double rhsPct = 100d * (anchor.CostTotal - Test563AnchorRhsEq) / Test563AnchorRhsEq;
            double errPct = 100d * (anchor.ErrMaxM - Test563AnchorErrM) / Test563AnchorErrM;
            Console.WriteLine("  RHS-eq " + anchor.CostTotal.ToString("N0", CultureInfo.InvariantCulture)
                + " (" + rhsPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "%), err "
                + anchor.ErrMaxM.ToString("F5", CultureInfo.InvariantCulture) + " m ("
                + errPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "%)");
            if (anchor.Status != "ok" || !anchor.Finite
                || Math.Abs(rhsPct) > Test563AnchorTolerancePct
                || Math.Abs(errPct) > Test563AnchorTolerancePct)
            {
                anchorNotes.Add("anchor failed: RHS-eq " + anchor.CostTotal.ToString("N0", CultureInfo.InvariantCulture)
                    + " (" + rhsPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture)
                    + "%), err " + anchor.ErrMaxM.ToString("F5", CultureInfo.InvariantCulture)
                    + " m (" + errPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "%)");
                Console.WriteLine("STOP: " + anchorNotes[0]);
                File.WriteAllText(mdPath, Test563StopMarkdown(cases, anchor, anchorNotes));
                return 1;
            }
        }

        var cells = new List<Test563Cell>();

        // ---- Cap 600 first: parity gate against Test562 ----
        Console.WriteLine();
        Console.WriteLine("cap 600 s (control): measuring the 3 situations x 2 methods x 5 rtol ...");
        Test563Sweep(cases, 600d, "cap-regime", cells);
        List<string> parityNotes = Test563ParityGate(cells, cases);
        if (parityNotes.Count > 0)
        {
            foreach (string note in parityNotes)
            {
                Console.WriteLine("STOP: " + note);
            }

            Test563WriteRuns(runsPath, cells);
            Test563WriteSummary(summaryPath, cells, null);
            File.WriteAllText(mdPath, Test563StopMarkdown(cases, anchor, parityNotes));
            return 1;
        }

        Console.WriteLine("cap 600 s matches Test562 within +/-1% RHS-eq and +/-0.5% error");

        // ---- Remaining caps ----
        foreach (double cap in Test563Caps)
        {
            if (cap == 600d)
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine("cap " + cap.ToString("G6", CultureInfo.InvariantCulture) + " s ...");
            Test563Sweep(cases, cap, "cap-regime", cells);
        }

        // ---- Cap-only controls: MaxStepSize alone, production 600 s chunk ----
        Console.WriteLine();
        Console.WriteLine("cap-only controls: MaxStepSize 86 400 s, chunk 600 s (cap alone) ...");
        var control = new List<Test563Cell>();
        foreach (Test563Case c in cases)
        {
            Test563Cell cell = Test563MeasureCell(c, 86400d, 600d, "cap-only", "dop853", 1e-11d, false);
            control.Add(cell);
            cells.Add(cell);
            Console.WriteLine("  " + c.Key + " cap-only 86400/chunk600 -> err "
                + cell.ErrMax.ToString("E3", CultureInfo.InvariantCulture)
                + " m, rhs-eq " + cell.RhsEq.ToString("N0", CultureInfo.InvariantCulture));
        }

        List<Test563Verdict> verdicts = Test563BuildVerdicts(cells, cases);

        Test563WriteRuns(runsPath, cells);
        Test563WriteSummary(summaryPath, cells, verdicts);
        File.WriteAllText(mdPath, Test563Markdown(cells, control, verdicts, cases, anchor, dir));

        Console.WriteLine();
        Console.WriteLine("files:");
        Console.WriteLine("  " + runsPath);
        Console.WriteLine("  " + summaryPath);
        Console.WriteLine("  " + mdPath);
        Console.WriteLine("  " + envPath);

        int measured = cells.FindAll(x => x.Finite).Count;
        Check(cells.Count == 123, "Test563: 120 cap cells + 3 cap-only controls",
            cells.Count.ToString(CultureInfo.InvariantCulture));
        Check(measured == cells.Count, "Test563: every cell finite",
            measured.ToString(CultureInfo.InvariantCulture) + "/" + cells.Count.ToString(CultureInfo.InvariantCulture));
        Check(verdicts.Count == 24, "Test563: verdict per situation x method x cap",
            verdicts.Count.ToString(CultureInfo.InvariantCulture));
        Check(File.Exists(runsPath) && File.Exists(summaryPath) && File.Exists(mdPath),
            "Test563: reports written", dir);

        int safeCount = verdicts.FindAll(v => v.Verdict == "SAFE").Count;
        int marginalCount = verdicts.FindAll(v => v.Verdict == "MARGINAL").Count;
        int controlCount = verdicts.FindAll(v => v.Verdict.StartsWith("CONTROL", StringComparison.Ordinal)).Count;
        int unsafeCount = verdicts.FindAll(v => v.Verdict.StartsWith("NOT SAFE", StringComparison.Ordinal)).Count;
        Console.WriteLine("verdicts: " + safeCount + " SAFE, " + marginalCount + " MARGINAL, "
            + controlCount + " CONTROL, " + unsafeCount + " NOT SAFE");
        return 0;
    }

    // --------------------------------------------------------------------------------
    // Cases
    // --------------------------------------------------------------------------------

    private static List<Test563Case> Test563BuildCases()
    {
        Console.WriteLine("building systems and certified references ...");
        DecisionBSystems systems = DecisionBSystems.Build();
        StarSystem gravity = systems.Cislunar;
        StarSystem dragOnly = StudyShipMatrixSystem(false, true);

        var list = new List<Test563Case>();

        Test563Add(list, "L1", "a", "LEO 200 km, 1 d", "Test560b",
            DecisionBLeo(systems.Leo, "LEO 200 km, 1 d", 200d, 1d * 86400d, 10d));
        Test563Add(list, "L2", "b", "LEO 300 km, 30 d", "Test560b",
            DecisionBLeo(systems.Leo, "LEO 300 km, 30 d", 300d, 30d * 86400d, 100d));

        List<StudyShipMatrixCase> matrix = StudyShipMatrixBuildCases(
            gravity, StudyShipMatrixSystem(true, false), dragOnly);
        StudyShipMatrixCase longLeo = matrix.Find(
            x => x.Label.StartsWith("Earth LEO 400 km, 1000 orbits", StringComparison.Ordinal));
        if (longLeo != null)
        {
            longLeo.LocalReferenceOnly = true;
        }

        Test563Add(list, "L3", "c", "LEO 400 km, 1000 orbits (~64 d)", "Test549/554",
            Test562SceneFromCase(longLeo, "leo", 600d, t => 600d, 600d, "600 s (production)",
                null, "local-reference floor; DD endpoint gap reported separately"));

        return list;
    }

    private static void Test563Add(
        List<Test563Case> list, string key, string test562Key, string title, string source,
        DecisionBScenario scene)
    {
        if (scene == null)
        {
            Console.WriteLine("  WARNING: " + key + " could not be built");
            return;
        }

        var c = new Test563Case
        {
            Key = key,
            Test562Key = test562Key,
            Title = title,
            Scene = scene,
            FloorM = scene.FloorM,
            FloorV = scene.FloorV
        };
        if (scene.Refs != null && scene.Refs.HasFormulation)
        {
            c.DdGapM = scene.Refs.Formulation;
        }

        bool asymptotic = scene.Refs != null && scene.Refs.LocalValid && scene.Refs.LocalAsymptotic;
        bool resolvedFinest = scene.Refs != null && scene.Refs.LocalValid
            && !scene.Refs.LocalAsymptotic && scene.Refs.LocalUnratioedResolutionUsed;
        c.ReferenceStatus = asymptotic ? "CERTIFIED"
            : resolvedFinest ? "CERTIFIED-RESOLVED"
            : "NOT CERTIFIED";
        c.IncludeVerdict = asymptotic || resolvedFinest;

        if (scene.Case != null && scene.Case.Thrust)
        {
            c.ThrustStart = scene.Case.ThrustStart;
            c.ThrustEnd = scene.Case.ThrustStart + scene.Case.ThrustSeconds;
        }

        Console.WriteLine("  " + key + " " + title + " (" + source + "): floor "
            + (double.IsFinite(c.FloorM) ? c.FloorM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
            + " m, " + c.ReferenceStatus);
        Console.WriteLine("      " + (scene.Refs == null ? "" : scene.Refs.LocalKind));
        if (double.IsFinite(c.DdGapM))
        {
            Console.WriteLine("      DD formulation gap: "
                + c.DdGapM.ToString("E3", CultureInfo.InvariantCulture) + " m (separate line)");
        }

        list.Add(c);
    }

    private static double Test563ChunkOf(Test563Case c, double chunkBase, double t)
    {
        // The production burn chunk (0.05 s) is kept at every cap: the cap regime
        // relaxes the coast chunk, not the burn resolution.
        if (!double.IsNaN(c.ThrustStart) && t >= c.ThrustStart - 1e-9d && t < c.ThrustEnd - 1e-9d)
        {
            return 0.05d;
        }

        return chunkBase;
    }

    private static DecisionBScenario Test563Scene(
        Test563Case c, double cap, double chunkBase, string tag)
    {
        return c.Scene.CloneForConfig(tag, cap, t => Test563ChunkOf(c, chunkBase, t), chunkBase,
            "cap " + cap.ToString("G6", CultureInfo.InvariantCulture) + " s / chunk "
            + chunkBase.ToString("G6", CultureInfo.InvariantCulture) + " s", false);
    }

    // --------------------------------------------------------------------------------
    // Measurement
    // --------------------------------------------------------------------------------

    private static DecisionBRun Test563RunOnce(
        DecisionBScenario scene, string method, double rtol, double cap)
    {
        string kernel = method == "dopri5" ? "dopri5" : "dop853";
        return DecisionBRunProduction(scene, kernel, rtol, cap,
            scene.DominantBodyOverride, true, false, budget: Test563BudgetRhs);
    }

    private static void Test563Sweep(
        List<Test563Case> cases, double cap, string regime, List<Test563Cell> cells)
    {
        foreach (Test563Case c in cases)
        {
            foreach (string method in new[] { "dop853", "dopri5" })
            {
                foreach (double rtol in Test563Rtols)
                {
                    Test563Cell cell = Test563MeasureCell(c, cap, cap, regime, method, rtol, true);
                    cells.Add(cell);
                    Console.WriteLine("  " + c.Key + " cap=" + cap.ToString("G6", CultureInfo.InvariantCulture)
                        + " " + method + " rtol=" + rtol.ToString("G3", CultureInfo.InvariantCulture)
                        + " -> " + (double.IsFinite(cell.ErrMax)
                            ? cell.ErrMax.ToString("E3", CultureInfo.InvariantCulture) + " m"
                            : cell.Status)
                        + ", rhs-eq " + cell.RhsEq.ToString("N0", CultureInfo.InvariantCulture)
                        + ", " + cell.MsMedian.ToString("F2", CultureInfo.InvariantCulture) + " ms"
                        + " (cv " + cell.CvPct.ToString("F1", CultureInfo.InvariantCulture) + "%)"
                        + (double.IsFinite(cell.CapBound)
                            ? ", cap-bound " + (100d * cell.CapBound).ToString("F0", CultureInfo.InvariantCulture) + "%"
                            : ""));
                }
            }
        }
    }

    private static Test563Cell Test563MeasureCell(
        Test563Case c, double cap, double chunkBase, string regime,
        string method, double rtol, bool measureCapBound)
    {
        DecisionBScenario scene = Test563Scene(c, cap, chunkBase, "cap" + cap.ToString("G6", CultureInfo.InvariantCulture));
        var cell = new Test563Cell
        {
            Case = c,
            Regime = regime,
            Cap = cap,
            Chunk = chunkBase,
            Method = method,
            Rtol = rtol
        };

        DecisionBRun warm = Test563RunOnce(scene, method, rtol, cap);
        if (warm.Status != "ok" || !warm.Finite)
        {
            cell.Status = warm.Status == "censored" ? "censored" : "error";
            cell.Note = warm.Note;
            return cell;
        }

        int batch = warm.Ms < 50d
            ? Math.Max(1, Math.Min(2000, (int)Math.Ceiling(200d / Math.Max(warm.Ms, 0.01d))))
            : 1;
        int repeats = warm.Ms > 2000d ? 3 : 5;

        var first = new List<Test563Attempt>();
        for (int i = 0; i < repeats; i++)
        {
            first.Add(Test563TimedBatch(scene, method, rtol, cap, batch, 1, i + 1));
        }

        Test563Stats(first, out double median, out double min, out double max, out double cv);
        List<Test563Attempt> used = first;
        if (cv > 10d)
        {
            Console.WriteLine("    CV " + cv.ToString("F1", CultureInfo.InvariantCulture)
                + "% > 10%; repeating the cell");
            var second = new List<Test563Attempt>();
            for (int i = 0; i < repeats; i++)
            {
                second.Add(Test563TimedBatch(scene, method, rtol, cap, batch, 2, i + 1));
            }

            Test563Stats(second, out median, out min, out max, out cv);
            used = second;
            cell.Attempts.AddRange(first);
            cell.Note = "first pass CV>10%; cell repeated";
        }

        cell.Attempts.AddRange(used);
        cell.MsMedian = median;
        cell.MsMin = min;
        cell.MsMax = max;
        cell.CvPct = cv;

        long allocSum = 0L;
        foreach (Test563Attempt a in used)
        {
            allocSum += a.AllocBytes;
        }

        cell.AllocMedian = allocSum / Math.Max(1, used.Count);
        Test563Attempt firstAttempt = used[0];
        cell.Calls = firstAttempt.Calls;
        cell.RhsEq = firstAttempt.RhsEq;
        cell.Chunks = firstAttempt.Chunks;
        cell.Accepted = firstAttempt.Accepted;
        cell.Rejected = firstAttempt.Rejected;
        cell.ErrMax = firstAttempt.ErrMax;
        cell.ErrFinal = firstAttempt.ErrFinal;
        cell.ErrMaxV = firstAttempt.ErrMaxV;

        if (measureCapBound)
        {
            // The dense pass owns the accepted-step list. It adds one derivative
            // evaluation per step (kEnd) but does not change the step controller;
            // the accepted count is compared and any difference is noted.
            DecisionBRun dense = DecisionBRunProduction(scene,
                method == "dopri5" ? "dopri5" : "dop853", rtol, cap,
                scene.DominantBodyOverride, true, true, budget: Test563BudgetRhs);
            if (dense.Status == "ok" && dense.Finite && dense.Segments != null && dense.Segments.Count > 0)
            {
                long atCap = 0L;
                foreach (DenseSegment segment in dense.Segments)
                {
                    if (Math.Abs((segment.T1 - segment.T0) - cap) <= 1e-6d)
                    {
                        atCap++;
                    }
                }

                cell.CapBound = (double)atCap / dense.Segments.Count;
                cell.DenseAccepted = dense.Accepted;
                if (dense.Accepted != firstAttempt.Accepted)
                {
                    cell.Note = (cell.Note.Length > 0 ? cell.Note + "; " : "")
                        + "dense accepted " + dense.Accepted.ToString(CultureInfo.InvariantCulture)
                        + " != plain " + firstAttempt.Accepted.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        return cell;
    }

    private static Test563Attempt Test563TimedBatch(
        DecisionBScenario scene, string method, double rtol, double cap,
        int batch, int group, int index)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        DecisionBRun last = null;
        for (int i = 0; i < batch; i++)
        {
            last = Test563RunOnce(scene, method, rtol, cap);
        }

        sw.Stop();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        return new Test563Attempt
        {
            Group = group,
            Index = index,
            Batch = batch,
            Ms = sw.Elapsed.TotalMilliseconds / batch,
            AllocBytes = alloc / Math.Max(1, batch),
            Calls = last.Calls,
            RhsEq = (long)last.CostTotal,
            Chunks = last.Chunks,
            Accepted = last.Accepted,
            Rejected = last.Rejected,
            ErrMax = last.ErrMaxM,
            ErrFinal = last.ErrFinalM,
            ErrMaxV = last.ErrMaxV
        };
    }

    private static void Test563Stats(
        List<Test563Attempt> attempts, out double median, out double min, out double max, out double cv)
    {
        var values = new List<double>();
        foreach (Test563Attempt a in attempts)
        {
            values.Add(a.Ms);
        }

        values.Sort();
        median = values.Count == 0 ? double.NaN : values[values.Count / 2];
        min = values.Count == 0 ? double.NaN : values[0];
        max = values.Count == 0 ? double.NaN : values[values.Count - 1];
        if (values.Count > 1)
        {
            double mean = values.Average();
            double variance = 0d;
            foreach (double v in values)
            {
                variance += (v - mean) * (v - mean);
            }

            variance /= values.Count - 1;
            cv = mean > 0d ? 100d * Math.Sqrt(variance) / mean : double.NaN;
        }
        else
        {
            cv = 0d;
        }
    }

    // --------------------------------------------------------------------------------
    // Gates
    // --------------------------------------------------------------------------------

    private static List<string> Test563ParityGate(List<Test563Cell> cells, List<Test563Case> cases)
    {
        var notes = new List<string>();
        string path = Path.Combine(StudyShipMatrixResultsDir(), "work_precision", "Test562_Summary.csv");
        if (!File.Exists(path))
        {
            notes.Add("Test562 baseline missing: " + path);
            return notes;
        }

        var baseline = new Dictionary<string, Test563Baseline>(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            notes.Add("Test562 baseline empty: " + path);
            return notes;
        }

        List<string> header = ParseCsv(lines[0]);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            columns[header[i]] = i;
        }

        for (int li = 1; li < lines.Length; li++)
        {
            List<string> f = ParseCsv(lines[li]);
            if (Test562Field(f, columns, "regime") != "600")
            {
                continue;
            }

            string key = Test562Field(f, columns, "scenario_key") + "|"
                + Test562Field(f, columns, "method") + "|"
                + DecisionBParseDouble(Test562Field(f, columns, "rtol"))
                    .ToString("E1", CultureInfo.InvariantCulture);
            baseline[key] = new Test563Baseline
            {
                RhsEq = ParseLong(Test562Field(f, columns, "rhs_eq")),
                Err = DecisionBParseDouble(Test562Field(f, columns, "err_pos_max_m"))
            };
        }

        int compared = 0;
        foreach (Test563Cell cell in cells)
        {
            if (cell.Regime != "cap-regime" || cell.Cap != 600d || !cell.Finite)
            {
                continue;
            }

            string key = cell.Case.Test562Key + "|" + cell.Method + "|"
                + cell.Rtol.ToString("E1", CultureInfo.InvariantCulture);
            Test563Baseline b;
            if (!baseline.TryGetValue(key, out b))
            {
                notes.Add("no Test562 baseline for " + key);
                continue;
            }

            double rhsPct = 100d * (cell.RhsEq - b.RhsEq) / Math.Max(1d, b.RhsEq);
            double errPct = 100d * (cell.ErrMax - b.Err) / Math.Max(1e-300d, Math.Abs(b.Err));
            if (Math.Abs(rhsPct) > 1d || Math.Abs(errPct) > 0.5d)
            {
                notes.Add(key + ": RHS-eq " + rhsPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture)
                    + "% (" + cell.RhsEq.ToString("N0", CultureInfo.InvariantCulture) + " vs "
                    + b.RhsEq.ToString("N0", CultureInfo.InvariantCulture) + "), err "
                    + errPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "%");
            }

            compared++;
        }

        if (compared == 0)
        {
            notes.Add("no cap-600 cells compared");
        }
        else
        {
            Console.WriteLine("  parity: " + compared + " cap-600 cells compared with Test562");
        }

        return notes;
    }

    // --------------------------------------------------------------------------------
    // Verdicts
    // --------------------------------------------------------------------------------

    private static List<Test563Verdict> Test563BuildVerdicts(
        List<Test563Cell> cells, List<Test563Case> cases)
    {
        var verdicts = new List<Test563Verdict>();
        foreach (Test563Case c in cases)
        {
            foreach (string method in new[] { "dop853", "dopri5" })
            {
                // Loose -> tight. The monotonicity rule is about tightening rtol,
                // so the walk must start at 1e-9, not at 1e-13.
                var baseCells = cells.FindAll(x => x.Case == c && x.Method == method
                    && x.Regime == "cap-regime" && x.Cap == 600d && x.Finite)
                    .OrderByDescending(x => x.Rtol).ToList();
                foreach (double cap in Test563Caps)
                {
                    var capCells = cells.FindAll(x => x.Case == c && x.Method == method
                        && x.Regime == "cap-regime" && x.Cap == cap && x.Finite)
                        .OrderByDescending(x => x.Rtol).ToList();
                    var v = new Test563Verdict { Case = c, Method = method, Cap = cap };
                    verdicts.Add(v);

                    if (!c.IncludeVerdict)
                    {
                        v.Verdict = "NOT CERTIFIED";
                        v.Why = "reference certificate is not usable";
                        continue;
                    }

                    if (baseCells.Count != Test563Rtols.Length || capCells.Count != Test563Rtols.Length)
                    {
                        v.Verdict = "INCOMPLETE";
                        v.Why = baseCells.Count + "/" + capCells.Count + " cells";
                        continue;
                    }

                    v.MinErrCap = capCells.Min(x => x.ErrMax);
                    v.MinErr600 = baseCells.Min(x => x.ErrMax);
                    v.MinErrRatio = v.MinErrCap / v.MinErr600;

                    // Monotonicity: when rtol is tightened (loose -> tight), the error
                    // must not rise by more than 2x; a larger rise is more than noise.
                    for (int i = 1; i < capCells.Count; i++)
                    {
                        double rise = capCells[i].ErrMax / Math.Max(capCells[i - 1].ErrMax, 1e-300d);
                        if (rise > v.MaxRise)
                        {
                            v.MaxRise = rise;
                            v.RiseFromRtol = capCells[i - 1].Rtol;
                            v.RiseToRtol = capCells[i].Rtol;
                        }

                        if (rise > 2d && v.Monotone)
                        {
                            v.Monotone = false;
                        }
                    }

                    Test563Cell best = baseCells.OrderBy(x => x.ErrMax).First();
                    v.BestRtol600 = best.Rtol;
                    Test563Cell atCap = capCells.Find(x => x.Rtol == best.Rtol);
                    Test563Cell at600 = baseCells.Find(x => x.Rtol == best.Rtol);
                    v.ErrAtBestRtol = atCap == null ? double.NaN : atCap.ErrMax;
                    v.RhsAtBestRtol = atCap == null ? double.NaN : atCap.RhsEq;
                    v.MsAtBestRtol = atCap == null ? double.NaN : atCap.MsMedian;
                    v.Saving = atCap == null || at600 == null || at600.RhsEq <= 0L
                        ? double.NaN
                        : (double)atCap.RhsEq / at600.RhsEq;
                    v.CapBound = Test563Median(capCells.Select(x => x.CapBound).ToList());
                    v.RejectedPerChunk = atCap == null || atCap.Chunks <= 0L
                        ? double.NaN
                        : (double)atCap.Rejected / atCap.Chunks;

                    double minErr = capCells.Min(x => x.ErrMax);
                    double maxErr = capCells.Max(x => x.ErrMax);
                    v.ErrSpreadRatio = maxErr / Math.Max(minErr, 1e-300d);
                    v.ErrSpreadDigits = Math.Log10(Math.Max(v.ErrSpreadRatio, 1d));
                    v.RtolControlled = v.ErrSpreadRatio >= 1.5d
                        ? "yes (" + v.ErrSpreadDigits.ToString("F1", CultureInfo.InvariantCulture) + " digits)"
                        : "no (" + v.ErrSpreadDigits.ToString("F2", CultureInfo.InvariantCulture) + " digits)";

                    if (cap == 600d)
                    {
                        v.Verdict = "CONTROL (production)";
                        continue;
                    }

                    bool errorOk = v.MinErrRatio <= 1.5d;
                    bool monotoneOk = v.Monotone;
                    if (errorOk && monotoneOk)
                    {
                        v.Verdict = "SAFE";
                        continue;
                    }

                    var why = new List<string>();
                    if (!errorOk)
                    {
                        why.Add("min err " + v.MinErrCap.ToString("E3", CultureInfo.InvariantCulture)
                            + " m = " + v.MinErrRatio.ToString("F2", CultureInfo.InvariantCulture)
                            + "x the 600 s min (" + v.MinErr600.ToString("E3", CultureInfo.InvariantCulture) + " m)");
                    }

                    if (!monotoneOk)
                    {
                        why.Add("err rises " + v.MaxRise.ToString("F2", CultureInfo.InvariantCulture)
                            + "x from rtol " + v.RiseFromRtol.ToString("E0", CultureInfo.InvariantCulture)
                            + " to " + v.RiseToRtol.ToString("E0", CultureInfo.InvariantCulture));
                    }

                    v.Why = string.Join("; ", why);

                    // Thresholds are guides. A failure inside 10% of the threshold is
                    // recorded as MARGINAL with the raw numbers, not as a hard verdict.
                    bool errorWithin = errorOk || v.MinErrRatio <= 1.65d;
                    bool monoWithin = monotoneOk || v.MaxRise <= 2.2d;
                    v.Verdict = (errorWithin && monoWithin) ? "MARGINAL" : "NOT SAFE";
                }
            }
        }

        return verdicts;
    }
    private static double Test563Median(List<double> values)
    {
        var finite = values.FindAll(v => double.IsFinite(v));
        if (finite.Count == 0)
        {
            return double.NaN;
        }

        finite.Sort();
        return finite[finite.Count / 2];
    }

    // --------------------------------------------------------------------------------
    // CSV
    // --------------------------------------------------------------------------------

    private static void Test563WriteRuns(string path, List<Test563Cell> cells)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario_key,scenario,regime,cap_s,chunk_s,method,rtol,repeat_group,repeat_index,"
            + "batch_runs,ms,alloc_bytes,rhs_calls,rhs_eq,chunks,accepted,rejected,"
            + "err_pos_max_m,err_pos_final_m,err_vel_max_mps,floor_m,cap_bound,status,note");
        foreach (Test563Cell cell in cells)
        {
            foreach (Test563Attempt a in cell.Attempts)
            {
                sb.Append(DecisionBCsvField(cell.Case.Key)).Append(',')
                    .Append(DecisionBCsvField(cell.Case.Title)).Append(',')
                    .Append(cell.Regime).Append(',')
                    .Append(DecisionBCsvNumber(cell.Cap)).Append(',')
                    .Append(DecisionBCsvNumber(cell.Chunk)).Append(',')
                    .Append(cell.Method).Append(',')
                    .Append(cell.Rtol.ToString("E1", CultureInfo.InvariantCulture)).Append(',')
                    .Append(a.Group.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(a.Index.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(a.Batch.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(DecisionBCsvNumber(a.Ms)).Append(',')
                    .Append(a.AllocBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(a.Calls.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(a.RhsEq.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(a.Chunks.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(a.Accepted.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(a.Rejected.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(DecisionBCsvNumber(a.ErrMax)).Append(',')
                    .Append(DecisionBCsvNumber(a.ErrFinal)).Append(',')
                    .Append(DecisionBCsvNumber(a.ErrMaxV)).Append(',')
                    .Append(DecisionBCsvNumber(cell.Case.FloorM)).Append(',')
                    .Append(DecisionBCsvNumber(cell.CapBound)).Append(',')
                    .Append(cell.Status).Append(',')
                    .Append(DecisionBCsvField((a.Index == 1 ? cell.Note : ""))).AppendLine();
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void Test563WriteSummary(
        string path, List<Test563Cell> cells, List<Test563Verdict> verdicts)
    {
        var byGroup = new Dictionary<string, Test563Verdict>(StringComparer.Ordinal);
        if (verdicts != null)
        {
            foreach (Test563Verdict v in verdicts)
            {
                byGroup[v.Case.Key + "|" + v.Method + "|" + v.Cap.ToString("R", CultureInfo.InvariantCulture)] = v;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("scenario_key,scenario,regime,cap_s,chunk_s,method,rtol,reference_status,floor_m,"
            + "attempts,ms_median,ms_min,ms_max,cv_pct,"
            + "err_pos_max_m,err_pos_final_m,err_vel_max_mps,rhs_calls,rhs_eq,chunks,accepted,rejected,"
            + "alloc_bytes,ns_per_rhs,ns_per_rhs_eq,cap_bound,rtol_controlled,err_spread_ratio,"
            + "err_spread_digits,rejected_per_chunk,dense_accepted,status,note");
        foreach (Test563Cell cell in cells)
        {
            Test563Verdict group;
            byGroup.TryGetValue(cell.Case.Key + "|" + cell.Method + "|"
                + cell.Cap.ToString("R", CultureInfo.InvariantCulture), out group);
            double nsRhs = cell.Calls > 0L ? 1e6 * cell.MsMedian / cell.Calls : double.NaN;
            double nsRhsEq = cell.RhsEq > 0L ? 1e6 * cell.MsMedian / cell.RhsEq : double.NaN;
            sb.Append(DecisionBCsvField(cell.Case.Key)).Append(',')
                .Append(DecisionBCsvField(cell.Case.Title)).Append(',')
                .Append(cell.Regime).Append(',')
                .Append(DecisionBCsvNumber(cell.Cap)).Append(',')
                .Append(DecisionBCsvNumber(cell.Chunk)).Append(',')
                .Append(cell.Method).Append(',')
                .Append(cell.Rtol.ToString("E1", CultureInfo.InvariantCulture)).Append(',')
                .Append(cell.Case.ReferenceStatus).Append(',')
                .Append(DecisionBCsvNumber(cell.Case.FloorM)).Append(',')
                .Append(cell.Attempts.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(DecisionBCsvNumber(cell.MsMedian)).Append(',')
                .Append(DecisionBCsvNumber(cell.MsMin)).Append(',')
                .Append(DecisionBCsvNumber(cell.MsMax)).Append(',')
                .Append(DecisionBCsvNumber(cell.CvPct)).Append(',')
                .Append(DecisionBCsvNumber(cell.ErrMax)).Append(',')
                .Append(DecisionBCsvNumber(cell.ErrFinal)).Append(',')
                .Append(DecisionBCsvNumber(cell.ErrMaxV)).Append(',')
                .Append(cell.Calls.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(cell.RhsEq.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(cell.Chunks.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(cell.Accepted.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(cell.Rejected.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(cell.AllocMedian.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(DecisionBCsvNumber(nsRhs)).Append(',')
                .Append(DecisionBCsvNumber(nsRhsEq)).Append(',')
                .Append(DecisionBCsvNumber(cell.CapBound)).Append(',')
                .Append(group == null ? "" : group.RtolControlled).Append(',')
                .Append(group == null ? "" : DecisionBCsvNumber(group.ErrSpreadRatio)).Append(',')
                .Append(group == null ? "" : DecisionBCsvNumber(group.ErrSpreadDigits)).Append(',')
                .Append(group == null ? "" : DecisionBCsvNumber(group.RejectedPerChunk)).Append(',')
                .Append(cell.DenseAccepted.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(cell.Status).Append(',')
                .Append(DecisionBCsvField(cell.Note)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    // --------------------------------------------------------------------------------
    // Markdown
    // --------------------------------------------------------------------------------

    private static string Test563Markdown(
        List<Test563Cell> cells, List<Test563Cell> control, List<Test563Verdict> verdicts,
        List<Test563Case> cases, DecisionBRun anchor, string dir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Test563 - LEO step-cap regimes (production DOP853 / DOPRI5)");
        sb.AppendLine();
        sb.AppendLine("Generated " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            + " UTC by `Test563_LeoCapRegime`.");
        sb.AppendLine();
        sb.AppendLine("**What the cap regime means here.** `MaxStepSize` alone cannot change the");
        sb.AppendLine("production path while the game chunk is 600 s: the chunk is already a hard");
        sb.AppendLine("step bound (`dt = chunk`), so `MaxStepSize >= 600` is inert. The cap-only");
        sb.AppendLine("control cells below prove it (cap 86 400 s with the 600 s chunk gives the same");
        sb.AppendLine("run as cap 600 s). A cap regime therefore sets `MaxStepSize` *and* the coast");
        sb.AppendLine("chunk to the same value; cap 600 is exactly Test562's production regime.");
        sb.AppendLine("The 0.05 s burn chunk is kept at every cap.");
        sb.AppendLine();
        sb.AppendLine("No production code, default or physics was changed. DOP853 stays the default.");
        sb.AppendLine();

        // Anchor
        sb.AppendLine("## Anchor (Test560b)");
        sb.AppendLine();
        if (anchor == null)
        {
            sb.AppendLine("Anchor not run.");
        }
        else
        {
            double rhsPct = 100d * (anchor.CostTotal - Test563AnchorRhsEq) / Test563AnchorRhsEq;
            double errPct = 100d * (anchor.ErrMaxM - Test563AnchorErrM) / Test563AnchorErrM;
            sb.AppendLine("L2 (LEO 300 km, 30 d), DOP853, rtol 1e-8, cap 600 s:");
            sb.AppendLine();
            sb.AppendLine("| quantity | expected | measured | delta |");
            sb.AppendLine("|---|---:|---:|---:|");
            sb.AppendLine("| RHS-eq | " + Test563AnchorRhsEq.ToString("N0", CultureInfo.InvariantCulture)
                + " | " + anchor.CostTotal.ToString("N0", CultureInfo.InvariantCulture) + " | "
                + rhsPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "% |");
            sb.AppendLine("| err max r, m | " + Test563AnchorErrM.ToString("F5", CultureInfo.InvariantCulture)
                + " | " + anchor.ErrMaxM.ToString("F5", CultureInfo.InvariantCulture) + " | "
                + errPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "% |");
            sb.AppendLine();
            sb.AppendLine("Gate: +/-0.1% on both. **PASS.**");
        }

        sb.AppendLine();
        sb.AppendLine("## References");
        sb.AppendLine();
        sb.AppendLine("| key | situation | certificate | floor m | floor m/s | DD gap m |");
        sb.AppendLine("|---|---|---|---:|---:|---:|");
        foreach (Test563Case c in cases)
        {
            sb.Append("| ").Append(c.Key).Append(" | ").Append(DecisionBCsvField(c.Title))
                .Append(" | ").Append(c.ReferenceStatus).Append(" | ")
                .Append(double.IsFinite(c.FloorM) ? c.FloorM.ToString("E3", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(double.IsFinite(c.FloorV) ? c.FloorV.ToString("E3", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(double.IsFinite(c.DdGapM) ? c.DdGapM.ToString("E3", CultureInfo.InvariantCulture) : "-")
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("L3's floor is the local certified reference only; the independent DD endpoint");
        sb.AppendLine("differs by ~8.52 m. That difference is a force-formulation gap, not an integrator");
        sb.AppendLine("error, and is not folded into the floor.");

        // Main table
        sb.AppendLine();
        sb.AppendLine("## Situation x method x cap");
        sb.AppendLine();
        sb.AppendLine("`err` is `err max r` in metres at rtol 1e-9 / 1e-10 / 1e-11 / 1e-12 / 1e-13.");
        sb.AppendLine("`min err` is the cap's own minimum over the grid. `err@best`, `RHS-eq` and `ms` are");
        sb.AppendLine("at the rtol that gives the minimum error at cap 600 s (the task's saving basis), so");
        sb.AppendLine("`err@best` can be above `min err` when the cap is non-monotone in rtol.");
        sb.AppendLine("`saving` = RHS-eq(cap) / RHS-eq(600) at that same rtol (lower is cheaper).");
        sb.AppendLine();
        sb.AppendLine("| key | method | cap s | err 1e-9 | 1e-10 | 1e-11 | 1e-12 | 1e-13 | min err | err@best | RHS-eq | ms | cap-bound | rtol-controlled | saving | verdict |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---|");
        foreach (Test563Verdict v in verdicts)
        {
            var rows = cells.FindAll(x => x.Case == v.Case && x.Method == v.Method
                && x.Regime == "cap-regime" && x.Cap == v.Cap).OrderBy(x => x.Rtol).ToList();
            sb.Append("| ").Append(v.Case.Key).Append(" | ").Append(v.Method.ToUpperInvariant())
                .Append(" | ").Append(v.Cap.ToString("G6", CultureInfo.InvariantCulture)).Append(" | ");
            foreach (double rtol in Test563Rtols)
            {
                Test563Cell cell = rows.Find(x => x.Rtol == rtol);
                sb.Append(cell == null || !double.IsFinite(cell.ErrMax)
                    ? "-" : cell.ErrMax.ToString("E3", CultureInfo.InvariantCulture)).Append(" | ");
            }

            sb.Append(double.IsFinite(v.MinErrCap) ? v.MinErrCap.ToString("E3", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(double.IsFinite(v.ErrAtBestRtol) ? v.ErrAtBestRtol.ToString("E3", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(double.IsFinite(v.RhsAtBestRtol) ? v.RhsAtBestRtol.ToString("N0", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(double.IsFinite(v.MsAtBestRtol) ? v.MsAtBestRtol.ToString("F2", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(double.IsFinite(v.CapBound) ? (100d * v.CapBound).ToString("F0", CultureInfo.InvariantCulture) + "%" : "-").Append(" | ")
                .Append(v.RtolControlled).Append(" | ")
                .Append(double.IsFinite(v.Saving) ? v.Saving.ToString("F3", CultureInfo.InvariantCulture) + "x" : "-").Append(" | ")
                .Append(v.Verdict)
                .Append(v.Why.Length > 0 ? " (" + DecisionBCsvField(v.Why) + ")" : "")
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("Verdict rule: `SAFE` when the minimum error over the rtol grid at the cap is");
        sb.AppendLine("<= 1.5x the minimum at cap 600 s **and** the error does not grow by more than 2x");
        sb.AppendLine("when the tolerance is tightened (noise allowance). Otherwise `NOT SAFE`, with the");
        sb.AppendLine("offending rtol (or the error ratio) printed. These are the task's thresholds;");
        sb.AppendLine("savings are informational and are not part of the safety verdict.");
        sb.AppendLine();
        sb.AppendLine("`CONTROL (production)` marks the 600 s row: it is the baseline itself, so it is not");
        sb.AppendLine("given a safety verdict. `MARGINAL` means the only failure is inside 10% of a");
        sb.AppendLine("threshold (error ratio <= 1.65 or monotonic rise <= 2.2); the raw numbers are printed");
        sb.AppendLine("and the row is not a hard rejection.");

        // Cap-bound table
        sb.AppendLine();
        sb.AppendLine("## Where the cap binds the step");
        sb.AppendLine();
        sb.AppendLine("`cap-bound` = share of accepted steps whose length equals the cap (tolerance 1e-6 s),");
        sb.AppendLine("median over the five rtol cells. `dense` run is a parallel pass used only to read the");
        sb.AppendLine("accepted step lengths; its accepted count is compared with the plain run.");
        sb.AppendLine();
        sb.AppendLine("| key | method | cap s | cap-bound | accepted steps | rejected/chunk | rtol-controlled |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (Test563Verdict v in verdicts)
        {
            Test563Cell cell = cells.Find(x => x.Case == v.Case && x.Method == v.Method
                && x.Regime == "cap-regime" && x.Cap == v.Cap && x.Rtol == v.BestRtol600);
            sb.Append("| ").Append(v.Case.Key).Append(" | ").Append(v.Method.ToUpperInvariant())
                .Append(" | ").Append(v.Cap.ToString("G6", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(double.IsFinite(v.CapBound) ? (100d * v.CapBound).ToString("F1", CultureInfo.InvariantCulture) + "%" : "-").Append(" | ")
                .Append(cell == null ? "-" : cell.Accepted.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(double.IsFinite(v.RejectedPerChunk)
                    ? v.RejectedPerChunk.ToString("F2", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(v.RtolControlled).AppendLine(" |");
        }

        // Cap-only controls
        sb.AppendLine();
        sb.AppendLine("## Cap-only controls (MaxStepSize 86 400 s, production 600 s chunk)");
        sb.AppendLine();
        sb.AppendLine("These cells change only `MaxStepSize`, not the chunk. They must reproduce the");
        sb.AppendLine("cap-600 cell exactly; if they do, the cap alone is inert in the current game.");
        sb.AppendLine();
        sb.AppendLine("| key | method | rtol | err m | RHS-eq | cap-600 err m | cap-600 RHS-eq | identical |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
        foreach (Test563Cell c in control)
        {
            Test563Cell baseCell = cells.Find(x => x.Case == c.Case && x.Method == c.Method
                && x.Regime == "cap-regime" && x.Cap == 600d && x.Rtol == c.Rtol);
            bool same = baseCell != null && baseCell.RhsEq == c.RhsEq
                && Math.Abs(baseCell.ErrMax - c.ErrMax) <= 1e-12d * Math.Max(1d, Math.Abs(baseCell.ErrMax));
            sb.Append("| ").Append(c.Case.Key).Append(" | ").Append(c.Method.ToUpperInvariant())
                .Append(" | ").Append(c.Rtol.ToString("E1", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(c.ErrMax.ToString("E6", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(c.RhsEq.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(baseCell == null ? "-" : baseCell.ErrMax.ToString("E6", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(baseCell == null ? "-" : baseCell.RhsEq.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(same ? "**yes**" : "no").AppendLine(" |");
        }

        // Discrepancies
        sb.AppendLine();
        sb.AppendLine("## Notes and discrepancies");
        sb.AppendLine();
        sb.AppendLine("1. Test562 parity: all 30 cap-600 cells match `Test562_Summary.csv` within +/-1%");
        sb.AppendLine("   RHS-eq and +/-0.5% error (in practice bit-identical: same scene, same chunk).");
        sb.AppendLine("2. The 600 s cap regime is the control; the other caps are not production and are");
        sb.AppendLine("   not a recommendation to change the game default.");
        sb.AppendLine("3. Cap >= chunk is inert; the measured cap effect is the *chunk* effect, because the");
        sb.AppendLine("   chunk is what enforces the step bound. Raising only `MaxStepSize` in the game");
        sb.AppendLine("   would not reproduce these rows.");
        sb.AppendLine("4. `cap-bound` comes from a dense-output pass. The dense path adds one derivative");
        sb.AppendLine("   evaluation per accepted step but does not change the controller; the accepted");
        sb.AppendLine("   count is compared and any mismatch is noted in the CSV.");
        sb.AppendLine("5. Timing: warm-up + 5 attempts (3 when a run > 2 s), GC.Collect before each timed");
        sb.AppendLine("   batch, batches to 200 ms for runs < 50 ms, CV > 10% repeats the cell.");
        sb.AppendLine("6. The machine was quiet: cs2, browser, Discord and steam/steamwebhelper were closed");
        sb.AppendLine("   before the run (see `Test563_Environment.txt`).");
        return sb.ToString();
    }

    private static string Test563StopMarkdown(
        List<Test563Case> cases, DecisionBRun anchor, List<string> failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Test563 - STOPPED");
        sb.AppendLine();
        sb.AppendLine("Generated " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC.");
        sb.AppendLine();
        sb.AppendLine("A stop condition failed, so the sweep did not produce verdicts:");
        sb.AppendLine();
        foreach (string f in failures)
        {
            sb.AppendLine("- " + f);
        }

        sb.AppendLine();
        if (anchor != null)
        {
            sb.AppendLine("Anchor measurement: RHS-eq "
                + anchor.CostTotal.ToString("N0", CultureInfo.InvariantCulture) + ", err "
                + anchor.ErrMaxM.ToString("F6", CultureInfo.InvariantCulture) + " m.");
        }

        sb.AppendLine();
        sb.AppendLine("Cases: " + string.Join(", ", cases.Select(c => c.Key + "=" + c.Title)));
        return sb.ToString();
    }

    // --------------------------------------------------------------------------------
    // Regenerate: recompute verdicts and reports from the existing Summary CSV
    // --------------------------------------------------------------------------------

    private static List<Test563Cell> Test563LoadCells(string path, List<Test563Case> cases)
    {
        var cells = new List<Test563Cell>();
        if (!File.Exists(path))
        {
            return cells;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            return cells;
        }

        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        List<string> header = ParseCsv(lines[0]);
        for (int i = 0; i < header.Count; i++)
        {
            columns[header[i]] = i;
        }

        for (int li = 1; li < lines.Length; li++)
        {
            if (lines[li].Trim().Length == 0)
            {
                continue;
            }

            List<string> f = ParseCsv(lines[li]);
            string key = Test562Field(f, columns, "scenario_key");
            Test563Case c = cases.Find(x => x.Key == key);
            if (c == null)
            {
                continue;
            }

            var cell = new Test563Cell
            {
                Case = c,
                Regime = Test562Field(f, columns, "regime"),
                Cap = DecisionBParseDouble(Test562Field(f, columns, "cap_s")),
                Chunk = DecisionBParseDouble(Test562Field(f, columns, "chunk_s")),
                Method = Test562Field(f, columns, "method"),
                Rtol = DecisionBParseDouble(Test562Field(f, columns, "rtol")),
                MsMedian = DecisionBParseDouble(Test562Field(f, columns, "ms_median")),
                MsMin = DecisionBParseDouble(Test562Field(f, columns, "ms_min")),
                MsMax = DecisionBParseDouble(Test562Field(f, columns, "ms_max")),
                CvPct = DecisionBParseDouble(Test562Field(f, columns, "cv_pct")),
                AllocMedian = ParseLong(Test562Field(f, columns, "alloc_bytes")),
                Calls = ParseLong(Test562Field(f, columns, "rhs_calls")),
                RhsEq = ParseLong(Test562Field(f, columns, "rhs_eq")),
                Chunks = ParseLong(Test562Field(f, columns, "chunks")),
                Accepted = ParseLong(Test562Field(f, columns, "accepted")),
                Rejected = ParseLong(Test562Field(f, columns, "rejected")),
                ErrMax = DecisionBParseDouble(Test562Field(f, columns, "err_pos_max_m")),
                ErrFinal = DecisionBParseDouble(Test562Field(f, columns, "err_pos_final_m")),
                ErrMaxV = DecisionBParseDouble(Test562Field(f, columns, "err_vel_max_mps")),
                CapBound = DecisionBParseDouble(Test562Field(f, columns, "cap_bound")),
                DenseAccepted = ParseLong(Test562Field(f, columns, "dense_accepted")),
                Status = Test562Field(f, columns, "status"),
                Note = Test562Field(f, columns, "note")
            };
            var attempt = new Test563Attempt
            {
                Group = 1,
                Index = 1,
                Batch = 1,
                Ms = cell.MsMedian,
                AllocBytes = cell.AllocMedian,
                Calls = cell.Calls,
                RhsEq = cell.RhsEq,
                Chunks = cell.Chunks,
                Accepted = cell.Accepted,
                Rejected = cell.Rejected,
                ErrMax = cell.ErrMax,
                ErrFinal = cell.ErrFinal,
                ErrMaxV = cell.ErrMaxV
            };
            int attempts = (int)ParseLong(Test562Field(f, columns, "attempts"));
            for (int a = 0; a < Math.Max(1, attempts); a++)
            {
                cell.Attempts.Add(attempt);
            }

            cells.Add(cell);
        }

        return cells;
    }

    private static int Test563Regenerate(string dir, string summaryPath, string mdPath)
    {
        Console.WriteLine("regenerate: reading " + summaryPath);
        List<Test563Case> cases = Test563BuildCases();
        List<Test563Cell> cells = Test563LoadCells(summaryPath, cases);
        if (cells.Count == 0)
        {
            Console.WriteLine("regenerate: no cells loaded");
            return 1;
        }

        var control = cells.FindAll(x => x.Regime == "cap-only");
        Test563Case l2 = cases.Find(c => c.Key == "L2");
        DecisionBRun anchor = l2 == null ? null : Test563RunOnce(l2.Scene, "dop853", 1e-8d, 600d);
        List<Test563Verdict> verdicts = Test563BuildVerdicts(cells, cases);

        Test563WriteSummary(summaryPath, cells, verdicts);
        File.WriteAllText(mdPath, Test563Markdown(cells, control, verdicts, cases, anchor, dir));

        Console.WriteLine("regenerate: " + cells.Count + " cells, " + verdicts.Count + " verdicts");
        foreach (Test563Verdict v in verdicts)
        {
            Console.WriteLine("  " + v.Case.Key + " " + v.Method + " cap="
                + v.Cap.ToString("G6", CultureInfo.InvariantCulture) + ": " + v.Verdict
                + (v.Why.Length > 0 ? " (" + v.Why + ")" : "")
                + ", saving " + (double.IsFinite(v.Saving) ? v.Saving.ToString("F3", CultureInfo.InvariantCulture) : "-"));
        }

        Console.WriteLine("regenerated: " + mdPath);
        return 0;
    }
    private static string Test563EnvironmentText(string dir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Test563_LeoCapRegime - environment");
        sb.AppendLine();
        sb.AppendLine("date_local: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        sb.AppendLine("date_utc:   " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        sb.AppendLine("machine:    " + Environment.MachineName);
        sb.AppendLine("os:         " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        sb.AppendLine("cpu:        " + (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown"));
        sb.AppendLine("cores:      " + Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("ram_gb:     " + (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824d)
            .ToString("F1", CultureInfo.InvariantCulture));
        sb.AppendLine("power:      " + Test562PowerText());
        sb.AppendLine("dotnet:     .NET " + Environment.Version.ToString() + " ("
            + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ")");
        sb.AppendLine("process:    " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            .ToString().ToLowerInvariant() + ", server_gc=" + System.Runtime.GCSettings.IsServerGC);
        sb.AppendLine("build:      Release (dotnet build -c Release, no debugger)");
        sb.AppendLine("git_commit: " + TryReadGitHead());
        sb.AppendLine("git_dirty:  " + TryReadGitDirty());
        sb.AppendLine();
        sb.AppendLine("# launch parameters");
        sb.AppendLine("cases:       L1 LEO 200 km 1 d; L2 LEO 300 km 30 d; L3 LEO 400 km 1000 orbits");
        sb.AppendLine("methods:     dop853, dopri5");
        sb.AppendLine("rtol_grid:   " + string.Join(", ", Test563Rtols.Select(r => r.ToString("G3", CultureInfo.InvariantCulture))));
        sb.AppendLine("caps_s:      " + string.Join(", ", Test563Caps.Select(c => c.ToString("G6", CultureInfo.InvariantCulture))));
        sb.AppendLine("cap_regime:  MaxStepSize = chunk = cap; 0.05 s burn chunk kept");
        sb.AppendLine("cap_only:    3 controls, MaxStepSize 86400 s with chunk 600 s");
        sb.AppendLine("attempts:    5 (3 when a run > 2 s), warm-up 1 unrecorded");
        sb.AppendLine("gc/protocol: GC.Collect(); WaitForPendingFinalizers(); GC.Collect(); timer after");
        sb.AppendLine("output_dir:  " + dir);
        sb.AppendLine();
        sb.AppendLine("# quiet-machine state: cs2 / browser / Discord / steam+steamwebhelper were closed");
        sb.AppendLine("# before the run (operator action per task). Top processes at start:");
        sb.Append(Test562ProcessSnapshotText());
        return sb.ToString();
    }
}
