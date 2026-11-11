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
        public bool Deterministic = true;
        public bool DeterminismKnown;
        // Values recorded in the Summary CSV when this cell was loaded by the
        // regenerate path; used to refuse a stale reference/floor mixture.
        public double CsvFloorM = double.NaN;
        public string CsvReferenceStatus = "";

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
        public string Verdict;                 // min-over-grid ("tuning") verdict
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

        // Production-rtol (1e-11) decision columns. These are what the game
        // actually does; the min-over-grid row above is a tuning view.
        public double ErrProductionCap = double.NaN;
        public double ErrProduction600 = double.NaN;
        public double RhsProductionCap = double.NaN;
        public double RhsProduction600 = double.NaN;
        public double SavingProduction = double.NaN;
        public double ErrProductionRatio = double.NaN;
        public bool FloorLimitedProduction;
        public string VerdictProduction = "";
        public string WhyProduction = "";

        // Floor guard on the min-over-grid row (same 10 x floor rule).
        public bool FloorLimitedTuning;
    }

    private sealed class Test563Baseline
    {
        public long Calls;
        public long RhsEq;
        public long Chunks;
        public double Err;
    }

    // --------------------------------------------------------------------------------
    // Entry point
    // --------------------------------------------------------------------------------

    private static int Test563_LeoCapRegime()
    {
        Console.WriteLine("--- Test563: LEO step-cap regimes (production methods) ---");
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
        string[] requiredCases = { "L1", "L2", "L3" };
        List<string> missingCases = requiredCases.Where(k => !cases.Exists(c => c.Key == k)).ToList();
        if (cases.Count == 0 || missingCases.Count > 0)
        {
            Console.WriteLine("STOP: Test563 requires cases " + string.Join(", ", requiredCases)
                + "; missing " + (missingCases.Count == 0 ? "all" : string.Join(", ", missingCases)));
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

        Console.WriteLine("cap 600 s matches Test562 exactly (calls, chunks, RHS-eq, err max r)");

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
        int censored = cells.FindAll(x => x.Status == "censored").Count;
        int errorCells = cells.FindAll(x => x.Status == "error").Count;
        int methodCount = Test562AvailableMethods().Length;
        // One cell per case x method x cap x rtol; one verdict per case x method x cap.
        int expectedCapCells = cases.Count * methodCount * Test563Caps.Length * Test563Rtols.Length;
        int expectedCells = expectedCapCells + cases.Count;
        int expectedVerdicts = cases.Count * methodCount * Test563Caps.Length;
        Check(cells.Count == expectedCells, "Test563: " + expectedCapCells.ToString(CultureInfo.InvariantCulture)
            + " cap cells + " + cases.Count.ToString(CultureInfo.InvariantCulture) + " cap-only controls",
            cells.Count.ToString(CultureInfo.InvariantCulture));
        Check(errorCells == 0, "Test563: no error cells (censored is a valid outcome)",
            errorCells.ToString(CultureInfo.InvariantCulture));
        Check(measured + censored == cells.Count,
            "Test563: every cell is measured or censored",
            (measured + censored).ToString(CultureInfo.InvariantCulture) + "/"
            + cells.Count.ToString(CultureInfo.InvariantCulture));
        int nonDeterministic = cells.FindAll(x => x.DeterminismKnown && !x.Deterministic).Count;
        Check(nonDeterministic == 0, "Test563: deterministic calls/chunks/error across repeats",
            nonDeterministic.ToString(CultureInfo.InvariantCulture));
        Check(verdicts.Count == expectedVerdicts, "Test563: verdict per situation x method x cap",
            verdicts.Count.ToString(CultureInfo.InvariantCulture));
        Check(File.Exists(runsPath) && File.Exists(summaryPath) && File.Exists(mdPath),
            "Test563: reports written", dir);

        int safeCount = verdicts.FindAll(v => v.VerdictProduction == "SAFE").Count;
        int marginalCount = verdicts.FindAll(v => v.VerdictProduction == "MARGINAL").Count;
        int controlCount = verdicts.FindAll(v => v.VerdictProduction.StartsWith("CONTROL", StringComparison.Ordinal)).Count;
        int unsafeCount = verdicts.FindAll(v => v.VerdictProduction == "NOT SAFE").Count;
        int floorLimited = verdicts.FindAll(v => v.VerdictProduction == "FLOOR-LIMITED").Count;
        Console.WriteLine("production verdicts (rtol 1e-11): " + safeCount + " SAFE, " + marginalCount
            + " MARGINAL, " + controlCount + " CONTROL, " + floorLimited + " FLOOR-LIMITED, "
            + unsafeCount + " NOT SAFE");
        Console.WriteLine("tuning verdicts (min over rtol grid): "
            + verdicts.FindAll(v => v.Verdict == "SAFE").Count + " SAFE, "
            + verdicts.FindAll(v => v.Verdict.StartsWith("FLOOR-LIMITED", StringComparison.Ordinal)).Count
            + " FLOOR-LIMITED, "
            + verdicts.FindAll(v => v.Verdict.StartsWith("NOT SAFE", StringComparison.Ordinal)).Count + " NOT SAFE");
        if (censored > 0)
        {
            Console.WriteLine("censored cells: " + censored.ToString(CultureInfo.InvariantCulture)
                + " (RHS budget); they are counted, not failed");
            foreach (Test563Cell cell in cells.FindAll(x => x.Status == "censored"))
            {
                Console.WriteLine("  " + cell.Case.Key + " " + cell.Method + " cap="
                    + cell.Cap.ToString("G6", CultureInfo.InvariantCulture) + " rtol="
                    + cell.Rtol.ToString("G3", CultureInfo.InvariantCulture) + ": " + cell.Note);
            }
        }

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
        Test562MethodInfo info = Test562MethodLookup(method);
        if (info == null)
        {
            throw new InvalidOperationException("unknown production kernel: " + method);
        }

        return DecisionBRunProduction(scene, info.Kernel, rtol, cap,
            scene.DominantBodyOverride, true, false, budget: Test563BudgetRhs);
    }

    private static void Test563Sweep(
        List<Test563Case> cases, double cap, string regime, List<Test563Cell> cells)
    {
        foreach (Test563Case c in cases)
        {
            foreach (string method in Test562AvailableMethods())
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
                            ? ", accepted@cap " + (100d * cell.CapBound).ToString("F0", CultureInfo.InvariantCulture) + "%"
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

        // Determinism: every recorded attempt of the same cell must execute the
        // same trajectory. Timing may differ; calls, chunks and the graded error
        // may not.
        foreach (Test563Attempt a in cell.Attempts)
        {
            if (a.Calls != firstAttempt.Calls || a.Chunks != firstAttempt.Chunks
                || Math.Abs(a.ErrMax - firstAttempt.ErrMax) > 1e-9d * Math.Max(1d, firstAttempt.ErrMax))
            {
                cell.Deterministic = false;
            }
        }

        if (!cell.Deterministic)
        {
            cell.Note = (cell.Note.Length > 0 ? cell.Note + "; " : "")
                + "non-deterministic caller/error between repeats";
        }

        cell.DeterminismKnown = true;

        if (measureCapBound)
        {
            // The dense pass owns the accepted-step list. It adds one derivative
            // evaluation per step (kEnd) but does not change the step controller;
            // the accepted count is compared and any difference is noted.
            Test562MethodInfo info = Test562MethodLookup(method);
            if (info == null)
            {
                throw new InvalidOperationException("unknown production kernel: " + method);
            }

            DecisionBRun dense = DecisionBRunProduction(scene, info.Kernel, rtol, cap,
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
        median = Test563MedianOfSorted(values);
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
            notes.Add("Test562 baseline missing: " + path
                + " - run Test562_ProductionWorkPrecision first (Test563 parity gate needs it)");
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
                Calls = ParseLong(Test562Field(f, columns, "rhs_calls")),
                RhsEq = ParseLong(Test562Field(f, columns, "rhs_eq")),
                Chunks = ParseLong(Test562Field(f, columns, "chunks")),
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
            if (cell.Calls != b.Calls || cell.RhsEq != b.RhsEq || cell.Chunks != b.Chunks
                || cell.ErrMax != b.Err)
            {
                notes.Add(key + ": exact parity failed - calls " + cell.Calls.ToString(CultureInfo.InvariantCulture)
                    + " vs " + b.Calls.ToString(CultureInfo.InvariantCulture) + ", RHS-eq "
                    + cell.RhsEq.ToString("N0", CultureInfo.InvariantCulture) + " vs "
                    + b.RhsEq.ToString("N0", CultureInfo.InvariantCulture) + " ("
                    + rhsPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "%), chunks "
                    + cell.Chunks.ToString(CultureInfo.InvariantCulture) + " vs "
                    + b.Chunks.ToString(CultureInfo.InvariantCulture) + ", err "
                    + cell.ErrMax.ToString("E6", CultureInfo.InvariantCulture) + " vs "
                    + b.Err.ToString("E6", CultureInfo.InvariantCulture) + " ("
                    + errPct.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) + "%)");
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
            foreach (string method in Test562AvailableMethods())
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
                    var capAll = cells.FindAll(x => x.Case == c && x.Method == method
                        && x.Regime == "cap-regime" && x.Cap == cap);
                    var baseAll = cells.FindAll(x => x.Case == c && x.Method == method
                        && x.Regime == "cap-regime" && x.Cap == 600d);
                    var v = new Test563Verdict { Case = c, Method = method, Cap = cap };
                    verdicts.Add(v);

                    if (!c.IncludeVerdict)
                    {
                        v.Verdict = "NOT CERTIFIED";
                        v.VerdictProduction = "NOT CERTIFIED";
                        v.Why = "reference certificate is not usable";
                        continue;
                    }

                    bool anyError = capAll.Exists(x => x.Status == "error")
                        || baseAll.Exists(x => x.Status == "error");
                    int censored = capAll.FindAll(x => x.Status == "censored").Count
                        + baseAll.FindAll(x => x.Status == "censored").Count;
                    if (anyError)
                    {
                        v.Verdict = "INCOMPLETE";
                        v.VerdictProduction = "INCOMPLETE";
                        v.Why = "an error cell is present";
                        continue;
                    }

                    if (censored > 0)
                    {
                        v.Verdict = "CENSORED";
                        v.VerdictProduction = "CENSORED";
                        v.Why = censored.ToString(CultureInfo.InvariantCulture)
                            + " cell(s) hit the RHS budget; no verdict";
                        continue;
                    }

                    if (baseCells.Count != Test563Rtols.Length || capCells.Count != Test563Rtols.Length)
                    {
                        v.Verdict = "INCOMPLETE";
                        v.VerdictProduction = "INCOMPLETE";
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

                    // ---- Production-rtol (1e-11) decision columns ----
                    Test563Cell prodCap = capCells.Find(x => x.Rtol == 1e-11d);
                    Test563Cell prod600 = baseCells.Find(x => x.Rtol == 1e-11d);
                    if (prodCap == null || prod600 == null)
                    {
                        v.VerdictProduction = "INCOMPLETE";
                        v.WhyProduction = "no rtol 1e-11 cell";
                    }
                    else
                    {
                        v.ErrProductionCap = prodCap.ErrMax;
                        v.ErrProduction600 = prod600.ErrMax;
                        v.RhsProductionCap = prodCap.RhsEq;
                        v.RhsProduction600 = prod600.RhsEq;
                        v.ErrProductionRatio = v.ErrProductionCap / Math.Max(v.ErrProduction600, 1e-300d);
                        v.SavingProduction = v.RhsProduction600 > 0d
                            ? v.RhsProductionCap / v.RhsProduction600
                            : double.NaN;
                        double floorLimit = Test562BelowFloorFactor * c.FloorM;
                        v.FloorLimitedProduction = double.IsFinite(c.FloorM)
                            && (v.ErrProductionCap < floorLimit || v.ErrProduction600 < floorLimit);
                        if (cap == 600d)
                        {
                            v.VerdictProduction = "CONTROL (production)";
                            if (v.FloorLimitedProduction)
                            {
                                v.WhyProduction = Test563FloorReason(
                                    v.ErrProductionCap, v.ErrProduction600, c.FloorM);
                            }
                        }
                        else if (v.FloorLimitedProduction)
                        {
                            // The game runs rtol 1e-11; the floor error is the accuracy
                            // limit of the reference, so a ratio inside it is not a verdict.
                            v.VerdictProduction = "FLOOR-LIMITED";
                            v.WhyProduction = Test563FloorReason(
                                v.ErrProductionCap, v.ErrProduction600, c.FloorM);
                        }
                        else if (v.ErrProductionRatio <= 1.5d)
                        {
                            v.VerdictProduction = "SAFE";
                            v.WhyProduction = Test563ProductionWhy(v);
                        }
                        else if (v.ErrProductionRatio <= 1.65d)
                        {
                            v.VerdictProduction = "MARGINAL";
                            v.WhyProduction = Test563ProductionWhy(v);
                        }
                        else
                        {
                            v.VerdictProduction = "NOT SAFE";
                            v.WhyProduction = Test563ProductionWhy(v);
                        }
                    }

                    // ---- Min-over-grid ("tuning") verdict, same floor guard ----
                    double tuningLimit = Test562BelowFloorFactor * c.FloorM;
                    v.FloorLimitedTuning = double.IsFinite(c.FloorM)
                        && (v.MinErrCap < tuningLimit || v.MinErr600 < tuningLimit);
                    if (cap == 600d)
                    {
                        v.Verdict = "CONTROL (production)";
                        continue;
                    }

                    if (v.FloorLimitedTuning)
                    {
                        v.Verdict = "FLOOR-LIMITED (tuning, no verdict)";
                        v.Why = Test563FloorReason(v.MinErrCap, v.MinErr600, c.FloorM);
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

    private static string Test563FloorReason(double errCap, double err600, double floorM)
    {
        double limit = Test562BelowFloorFactor * floorM;
        var parts = new List<string>();
        if (errCap < limit)
        {
            parts.Add("err(cap) " + errCap.ToString("E3", CultureInfo.InvariantCulture) + " m");
        }

        if (err600 < limit)
        {
            parts.Add("err(600) " + err600.ToString("E3", CultureInfo.InvariantCulture) + " m");
        }

        return (parts.Count == 0 ? "error" : string.Join(" and ", parts))
            + " < " + Test562BelowFloorFactor.ToString("G", CultureInfo.InvariantCulture)
            + " x floor (" + limit.ToString("E3", CultureInfo.InvariantCulture) + " m)";
    }

    private static string Test563ProductionWhy(Test563Verdict v)
    {
        return "prod err " + v.ErrProductionCap.ToString("E3", CultureInfo.InvariantCulture)
            + " m = " + v.ErrProductionRatio.ToString("F2", CultureInfo.InvariantCulture)
            + "x the 600 s prod err (" + v.ErrProduction600.ToString("E3", CultureInfo.InvariantCulture)
            + " m)";
    }
    private static double Test563Median(List<double> values)
    {
        var finite = values.FindAll(v => double.IsFinite(v));
        if (finite.Count == 0)
        {
            return double.NaN;
        }

        finite.Sort();
        return Test563MedianOfSorted(finite);
    }

    /// <summary>Median of an already sorted list: mean of the two central values for an even count.</summary>
    private static double Test563MedianOfSorted(List<double> sorted)
    {
        if (sorted.Count == 0)
        {
            return double.NaN;
        }

        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : 0.5d * (sorted[mid - 1] + sorted[mid]);
    }

    // --------------------------------------------------------------------------------
    // CSV
    // --------------------------------------------------------------------------------

    private static void Test563WriteRuns(string path, List<Test563Cell> cells)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario_key,scenario,regime,cap_s,chunk_s,method,rtol,repeat_group,repeat_index,"
            + "batch_runs,ms,alloc_bytes,rhs_calls,rhs_eq,chunks,accepted,rejected,"
            + "err_pos_max_m,err_pos_final_m,err_vel_max_mps,floor_m,accepted_at_cap_share,status,note");
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
                // Regime and chunk are part of the key: cap-only control rows
                // (cap 86 400, chunk 600) must not inherit cap-regime statistics.
                byGroup[v.Case.Key + "|" + v.Method + "|" + v.Cap.ToString("R", CultureInfo.InvariantCulture)
                    + "|cap-regime|" + v.Cap.ToString("R", CultureInfo.InvariantCulture)] = v;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("scenario_key,scenario,regime,cap_s,chunk_s,method,rtol,reference_status,floor_m,"
            + "attempts,ms_median,ms_min,ms_max,cv_pct,"
            + "err_pos_max_m,err_pos_final_m,err_vel_max_mps,rhs_calls,rhs_eq,chunks,accepted,rejected,"
            + "alloc_bytes,ns_per_rhs,ns_per_rhs_eq,accepted_at_cap_share,rtol_controlled,err_spread_ratio,"
            + "err_spread_digits,rejected_per_chunk,dense_accepted,deterministic,status,note,"
            + "err_prod_1e11,rhs_prod_1e11,err_base_prod_1e11,rhs_base_prod_1e11,saving_prod,"
            + "floor_limited_prod,verdict_prod,floor_limited_tuning,verdict_tuning");
        foreach (Test563Cell cell in cells)
        {
            Test563Verdict group = null;
            if (cell.Regime == "cap-regime")
            {
                byGroup.TryGetValue(cell.Case.Key + "|" + cell.Method + "|"
                    + cell.Cap.ToString("R", CultureInfo.InvariantCulture)
                    + "|cap-regime|" + cell.Chunk.ToString("R", CultureInfo.InvariantCulture), out group);
            }

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
                .Append(cell.DeterminismKnown ? (cell.Deterministic ? "1" : "0") : "").Append(',')
                .Append(cell.Status).Append(',')
                .Append(DecisionBCsvField(cell.Note)).Append(',')
                .Append(group == null ? "" : DecisionBCsvNumber(group.ErrProductionCap)).Append(',')
                .Append(group == null ? "" : DecisionBCsvNumber(group.RhsProductionCap)).Append(',')
                .Append(group == null ? "" : DecisionBCsvNumber(group.ErrProduction600)).Append(',')
                .Append(group == null ? "" : DecisionBCsvNumber(group.RhsProduction600)).Append(',')
                .Append(group == null ? "" : DecisionBCsvNumber(group.SavingProduction)).Append(',')
                .Append(group == null ? "" : (group.FloorLimitedProduction ? "1" : "0")).Append(',')
                .Append(group == null ? "" : group.VerdictProduction).Append(',')
                .Append(group == null ? "" : (group.FloorLimitedTuning ? "1" : "0")).Append(',')
                .Append(group == null ? "" : group.Verdict).AppendLine();
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
        sb.AppendLine("# Test563 - LEO step-cap regimes (production "
            + string.Join(" / ", Test562AvailableMethods().Select(Test562MethodDisplay)) + ")");
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
        sb.AppendLine("`err@1e-11`, `vs600@1e-11`, `RHS-eq@1e-11`, `saving@1e-11` and `verdict@1e-11` are the");
        sb.AppendLine("production columns: the game runs at rtol 1e-11, so this is the decision row. `vs600@1e-11`");
        sb.AppendLine("is err(cap, 1e-11) / err(600, 1e-11); `saving@1e-11` = RHS-eq(cap, 1e-11) / RHS-eq(600, 1e-11).");
        sb.AppendLine("`floor-limited` means the error the verdict is built on is below 10 x the reference floor;");
        sb.AppendLine("such a row has no accuracy verdict. The `min err` / `err@best` / `saving@best` /");
        sb.AppendLine("`verdict (tuning)` columns are the old min-over-grid view and are kept separate: they are");
        sb.AppendLine("a tuning exploration, not the production decision.");
        sb.AppendLine();
        sb.AppendLine("| key | method | cap s | err 1e-9 | 1e-10 | 1e-11 | 1e-12 | 1e-13 | min err | err@best | RHS-eq@best | ms@best | saving@best | accepted@cap | rtol-controlled | verdict (tuning) | err@1e-11 | vs600@1e-11 | RHS-eq@1e-11 | saving@1e-11 | floor-limited | verdict@1e-11 |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---|---|");
        foreach (Test563Verdict v in verdicts)
        {
            var rows = cells.FindAll(x => x.Case == v.Case && x.Method == v.Method
                && x.Regime == "cap-regime" && x.Cap == v.Cap).OrderBy(x => x.Rtol).ToList();
            sb.Append("| ").Append(v.Case.Key).Append(" | ").Append(Test562MethodDisplay(v.Method))
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
                .Append(double.IsFinite(v.Saving) ? v.Saving.ToString("F3", CultureInfo.InvariantCulture) + "x" : "-").Append(" | ")
                .Append(double.IsFinite(v.CapBound) ? (100d * v.CapBound).ToString("F0", CultureInfo.InvariantCulture) + "%" : "-").Append(" | ")
                .Append(v.RtolControlled).Append(" | ")
                .Append(v.Verdict)
                .Append(v.Why.Length > 0 && !v.Verdict.StartsWith("SAFE", StringComparison.Ordinal)
                    ? " (" + DecisionBCsvField(v.Why) + ")" : "")
                .Append(" | ")
                .Append(double.IsFinite(v.ErrProductionCap) ? v.ErrProductionCap.ToString("E3", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(double.IsFinite(v.ErrProductionRatio) ? v.ErrProductionRatio.ToString("F2", CultureInfo.InvariantCulture) + "x" : "-").Append(" | ")
                .Append(double.IsFinite(v.RhsProductionCap) ? v.RhsProductionCap.ToString("N0", CultureInfo.InvariantCulture) : "-").Append(" | ")
                .Append(double.IsFinite(v.SavingProduction) ? v.SavingProduction.ToString("F3", CultureInfo.InvariantCulture) + "x" : "-").Append(" | ")
                .Append(v.FloorLimitedProduction ? "yes" : "no").Append(" | ")
                .Append(v.VerdictProduction)
                .Append(v.VerdictProduction == "FLOOR-LIMITED" && v.WhyProduction.Length > 0
                    ? " (" + DecisionBCsvField(v.WhyProduction) + ")" : "")
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("**Verdict rule - production (the game decision).** rtol is fixed at 1e-11, as in the");
        sb.AppendLine("game. `SAFE` when err(cap, 1e-11) <= 1.5x err(600, 1e-11); `MARGINAL` when it is");
        sb.AppendLine("<= 1.65x; otherwise `NOT SAFE`. If either the cap or the 600 s production error is");
        sb.AppendLine("below 10 x the reference floor, the row is `FLOOR-LIMITED` and gets no verdict: the");
        sb.AppendLine("ratio is inside the uncertainty of the reference, not evidence about the cap.");
        sb.AppendLine();
        sb.AppendLine("**Verdict rule - tuning.** The old `min err` view takes the minimum over the whole rtol");
        sb.AppendLine("grid: `SAFE` when min err(cap) <= 1.5x min err(600) **and** the error does not grow by");
        sb.AppendLine("more than 2x when the tolerance is tightened. This is a tuning exploration of the");
        sb.AppendLine("integrator, not a game decision, and it is never mixed with the production verdict.");
        sb.AppendLine("The same 10 x floor guard applies (`floor_limited_tuning` in the CSV).");
        sb.AppendLine();
        sb.AppendLine("`CONTROL (production)` marks the 600 s row: it is the baseline itself, so it is not");
        sb.AppendLine("given a safety verdict. `MARGINAL` means the only failure is inside 10% of a");
        sb.AppendLine("threshold (error ratio <= 1.65 or monotonic rise <= 2.2); the raw numbers are printed");
        sb.AppendLine("and the row is not a hard rejection.");

        // Cap-bound table
        sb.AppendLine();
        sb.AppendLine("## Where the cap binds the step");
        sb.AppendLine();
        sb.AppendLine("`accepted_at_cap_share` = share of accepted steps whose length equals the cap");
        sb.AppendLine("(tolerance 1e-6 s), median over the five rtol cells. This is not a measure of");
        sb.AppendLine("cap activity: the first step of every chunk is attempted at the cap by");
        sb.AppendLine("construction, and the controller then rejects it and accepts a shorter step, so");
        sb.AppendLine("0% is the expected value when the cap is a hard attempt bound. `rejected/chunk`");
        sb.AppendLine("is the mean number of rejected attempts per chunk at the production rtol.");
        sb.AppendLine("`dense` run is a parallel pass used only to read the accepted step lengths; its");
        sb.AppendLine("accepted count is compared with the plain run.");
        sb.AppendLine();
        sb.AppendLine("| key | method | cap s | accepted at cap | accepted steps | rejected/chunk | rtol-controlled |");
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
        sb.AppendLine("1. Test562 parity: all 30 cap-600 cells match `Test562_Summary.csv` exactly");
        sb.AppendLine("   (RHS-eq, chunks and err max r, the same scene and the same chunk).");
        sb.AppendLine("2. The 600 s cap regime is the control; the other caps are not production and are");
        sb.AppendLine("   not a recommendation to change the game default.");
        sb.AppendLine("3. Cap >= chunk is inert; the measured cap effect is the *chunk* effect, because the");
        sb.AppendLine("   chunk is what enforces the step bound. Raising only `MaxStepSize` in the game");
        sb.AppendLine("   would not reproduce these rows.");
        sb.AppendLine("4. `accepted_at_cap_share` comes from a dense-output pass. The dense path adds one");
        sb.AppendLine("   derivative evaluation per accepted step but does not change the controller; the");
        sb.AppendLine("   accepted count is compared and any mismatch is noted in the CSV.");
        sb.AppendLine("5. Timing: warm-up + 5 attempts (3 when a run > 2 s), GC.Collect before each timed");
        sb.AppendLine("   batch, batches to 200 ms for runs < 50 ms, CV > 10% repeats the cell. The ms");
        sb.AppendLine("   column is the outer end-to-end stopwatch (it includes scene construction and the");
        sb.AppendLine("   error evaluation); it is not directly comparable with Test562's integration-only");
        sb.AppendLine("   timing. Cross-report ms comparisons are invalid.");
        sb.AppendLine("6. Timing is end-to-end and was taken in the machine state recorded at the top of");
        sb.AppendLine("   `Test563_Environment.txt` (top processes at start). Treat it as an upper bound,");
        sb.AppendLine("   not a quiet-machine number.");
        sb.AppendLine("7. Determinism: every recorded attempt of a cell must have the same Calls, Chunks and");
        sb.AppendLine("   ErrMax. A fresh run writes 1/0 in the `deterministic` column; a loaded CSV without");
        sb.AppendLine("   that column leaves it blank rather than claiming an unverified 1.");
        sb.AppendLine("8. The parity gate requires `Test562_Summary.csv`. Run Test562 before Test563 on a");
        sb.AppendLine("   clean tree; a missing baseline stops the sweep with an explicit message.");
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

            string detField = Test562Field(f, columns, "deterministic");
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
                CapBound = Test563LoadDouble(f, columns, "accepted_at_cap_share", "cap_bound"),
                DenseAccepted = ParseLong(Test562Field(f, columns, "dense_accepted")),
                Deterministic = detField != "0",
                DeterminismKnown = detField.Length > 0,
                Status = Test562Field(f, columns, "status"),
                Note = Test562Field(f, columns, "note"),
                CsvFloorM = DecisionBParseDouble(Test562Field(f, columns, "floor_m")),
                CsvReferenceStatus = Test562Field(f, columns, "reference_status")
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

    private static double Test563LoadDouble(
        List<string> fields, Dictionary<string, int> columns, string name, string fallback)
    {
        string value = Test562Field(fields, columns, name);
        if (value.Length == 0 && !string.IsNullOrEmpty(fallback))
        {
            value = Test562Field(fields, columns, fallback);
        }

        return DecisionBParseDouble(value);
    }

    /// <summary>
    /// Refuse a regenerate pass that would mix errors measured against a
    /// different reference/floor with the floor of the current build.
    /// </summary>
    private static List<string> Test563ValidateLoadedCells(
        List<Test563Cell> cells, List<Test563Case> cases)
    {
        var stale = new List<string>();
        foreach (Test563Case c in cases)
        {
            var rows = cells.FindAll(x => x.Case == c);
            if (rows.Count == 0)
            {
                stale.Add(c.Key + ": no cells in the Summary CSV");
                continue;
            }

            foreach (Test563Cell cell in rows)
            {
                if (!double.IsFinite(cell.CsvFloorM))
                {
                    stale.Add(c.Key + " " + cell.Regime + " cap " + cell.Cap.ToString("G6", CultureInfo.InvariantCulture)
                        + ": floor_m missing in the CSV");
                    continue;
                }

                if (!double.IsFinite(c.FloorM)
                    || Math.Abs(cell.CsvFloorM - c.FloorM) > 0.01d * Math.Max(Math.Abs(c.FloorM), 1e-30d))
                {
                    stale.Add(c.Key + ": CSV floor " + cell.CsvFloorM.ToString("E3", CultureInfo.InvariantCulture)
                        + " m != current floor " + c.FloorM.ToString("E3", CultureInfo.InvariantCulture) + " m");
                }

                if (!string.IsNullOrEmpty(cell.CsvReferenceStatus)
                    && !string.Equals(cell.CsvReferenceStatus, c.ReferenceStatus, StringComparison.Ordinal))
                {
                    stale.Add(c.Key + ": CSV reference " + cell.CsvReferenceStatus
                        + " != current " + c.ReferenceStatus);
                }
            }
        }

        return stale;
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

        List<string> stale = Test563ValidateLoadedCells(cells, cases);
        if (stale.Count > 0)
        {
            Console.WriteLine("STALE REFERENCE: regenerate refused; the CSV was measured against a different floor/reference.");
            foreach (string note in stale.Take(10))
            {
                Console.WriteLine("  " + note);
            }

            if (stale.Count > 10)
            {
                Console.WriteLine("  ... " + (stale.Count - 10).ToString(CultureInfo.InvariantCulture) + " more");
            }

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
                + v.Cap.ToString("G6", CultureInfo.InvariantCulture)
                + ": prod " + v.VerdictProduction
                + (v.FloorLimitedProduction ? " [floor-limited]" : "")
                + ", tuning " + v.Verdict
                + ", saving@1e-11 " + (double.IsFinite(v.SavingProduction)
                    ? v.SavingProduction.ToString("F3", CultureInfo.InvariantCulture) : "-"));
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
        sb.AppendLine("methods:     " + string.Join(", ", Test562AvailableMethods()));
        sb.AppendLine("rtol_grid:   " + string.Join(", ", Test563Rtols.Select(r => r.ToString("G3", CultureInfo.InvariantCulture))));
        sb.AppendLine("caps_s:      " + string.Join(", ", Test563Caps.Select(c => c.ToString("G6", CultureInfo.InvariantCulture))));
        sb.AppendLine("cap_regime:  MaxStepSize = chunk = cap; 0.05 s burn chunk kept");
        sb.AppendLine("cap_only:    3 controls, MaxStepSize 86400 s with chunk 600 s");
        sb.AppendLine("attempts:    5 (3 when a run > 2 s), warm-up 1 unrecorded");
        sb.AppendLine("gc/protocol: GC.Collect(); WaitForPendingFinalizers(); GC.Collect(); timer after");
        sb.AppendLine("output_dir:  " + dir);
        sb.AppendLine();
        sb.AppendLine("# top processes by working set at start (timing is end-to-end; treat it as an upper bound):");
        sb.Append(Test562ProcessSnapshotText());
        return sb.ToString();
    }
}
