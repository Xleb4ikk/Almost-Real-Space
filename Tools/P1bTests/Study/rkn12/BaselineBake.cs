using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    // ────────────────────────────────────────────────────────────────────────
    // R0 baseline probe for TASK_RKN12 §R0.
    //
    // It does NOT touch the game code: it only calls the production
    // EphemerisBaker with the current (DOP853) engine and records time, file
    // size, sha256, Chebyshev-node error against an independent reference
    // (analytic Kepler for 2 bodies; dd-Taylor is opt-in for the 4-body preset)
    // and the true Chebyshev seam discontinuity. Results go to
    // Tools/P1bTests/Study/rkn12/results/baseline/ (local artifacts; the
    // committed summary is BASELINE.md).
    //
    // Command:
    //   P1bTests.exe --exact Rkn12BaselineDop853 --log <path>
    //
    // Optional environment:
    //   RKN12_BASELINE_DIR   output directory (default Study/rkn12/results/baseline)
    //   RKN12_BASELINE_DD    OPT-IN dd-Taylor endpoint reference: comma list of
    //                        horizons (years) or "all". Default "none": dd-Taylor
    //                        is not part of the default run (owner decision,
    //                        2026-11-15); 1.2/10 y cost seconds, 100 y ~5 min,
    //                        1024 y ~1 h.
    //   RKN12_BASELINE_TIGHT comma list of horizons for the tighter-DOP853
    //                        consistency probe, or "all" (default "1.2,10")
    // ────────────────────────────────────────────────────────────────────────

    private const double BaselineYearSeconds = 365d * 86400d;

    private static BakeConfig BaselineConfig()
    {
        return new BakeConfig
        {
            Degree = 12,
            PhasePerStep = 0.02d,
            MaxStepSeconds = 900d,
            QuantBudgetMeters = 2d,
            SegmentPeriodDivisor = 4d
        };
    }

    private static BakeConfig BaselineTightConfig()
    {
        return new BakeConfig
        {
            Degree = 12,
            PhasePerStep = 0.002d,
            MaxStepSeconds = 60d,
            QuantBudgetMeters = 2d,
            SegmentPeriodDivisor = 4d
        };
    }

    private sealed class BaselineSystem
    {
        public string Name;
        public string Source;
        public bool AnalyticKepler;
        public Func<StarSystem> Create;
    }

    private sealed class BaselineRun
    {
        public StarSystem System;
        public double Years;
        public string Directory;
        public double WallSeconds;
        public long TotalBytes;
        public int FileCount;
        public string CombinedSha256;
        public Dictionary<string, string> BodySha256;
        public Dictionary<OrbitingBody, BakedEphemeris> Ephemerides;
        public long SegmentCount;
    }

    private static int Rkn12BaselineDop853()
    {
        string outDir = Environment.GetEnvironmentVariable("RKN12_BASELINE_DIR");
        if (string.IsNullOrEmpty(outDir))
        {
            outDir = Path.Combine("Study", "rkn12", "results", "baseline");
        }

        string bakeRoot = Path.Combine(outDir, "bake");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(bakeRoot);

        string ddEnv = Environment.GetEnvironmentVariable("RKN12_BASELINE_DD");
        string tightEnv = Environment.GetEnvironmentVariable("RKN12_BASELINE_TIGHT");
        string horizonEnv = Environment.GetEnvironmentVariable("RKN12_BASELINE_HORIZONS");
        bool ddAll = string.Equals(ddEnv, "all", StringComparison.OrdinalIgnoreCase);
        bool tightAll = string.Equals(tightEnv, "all", StringComparison.OrdinalIgnoreCase);
        bool ddNone = string.Equals(ddEnv, "none", StringComparison.OrdinalIgnoreCase);
        bool tightNone = string.Equals(tightEnv, "none", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(ddEnv))
        {
            // Owner decision 2026-11-15: dd-Taylor is NOT part of the default run.
            // It is opt-in only, and only for short horizons (1.2/10 y are seconds).
            ddEnv = "none";
        }

        if (string.IsNullOrEmpty(tightEnv))
        {
            tightEnv = "1.2,10";
        }

        double[] horizons = string.IsNullOrEmpty(horizonEnv)
            ? new[] { 1.2d, 10d, 100d, 1024d }
            : BaselineParseHorizons(horizonEnv);

        var systems = new[]
        {
            new BaselineSystem
            {
                Name = "Outdoors",
                Source = "Assets/_Project/Scenes/OutdoorsScene.unity: Sol (423-436), Terra (950-963)",
                AnalyticKepler = true,
                Create = BaselineOutdoorsSystem
            },
            new BaselineSystem
            {
                Name = "ContactZone",
                Source = "Assets/_Project/Scenes/ContactZoneSlice.unity: Star (387-398), Terra (166-177)",
                AnalyticKepler = true,
                Create = BaselineContactZoneSystem
            },
            new BaselineSystem
            {
                Name = "Example4",
                Source = "Assets/_Project/Scripts/Universe/ExampleSystemPresets.cs:14-63",
                AnalyticKepler = false,
                Create = ExampleSystemPresets.CreateExampleSystem
            }
        };

        var runRows = new List<string>();
        var errorRows = new List<string>();
        var determinantRows = new List<string>();
        var invariantRows = new List<string>();
        var tightRows = new List<string>();

        bool tightOnly = string.Equals(
            Environment.GetEnvironmentVariable("RKN12_BASELINE_TIGHT_ONLY"),
            "1", StringComparison.Ordinal);

        if (tightOnly)
        {
            foreach (BaselineSystem s in systems)
            {
                foreach (double years in horizons)
                {
                    BaselineRun run = BaselineBakeOnce(s.Create(), years, BaselineConfig(),
                        Path.Combine(bakeRoot, s.Name + "_" + Fmt(years) + "y"));
                    BaselineTightConsistency(s, run, years, tightRows);
                }
            }

            BaselineWriteTightCsv(outDir, tightRows);
            Console.WriteLine("R0 tight-consistency artifacts: " + Path.GetFullPath(outDir));
            return 0;
        }

        foreach (BaselineSystem s in systems)
        {
            foreach (double years in horizons)
            {
                Console.WriteLine();
                Console.WriteLine("=== R0 baseline: {0}, {1} y (DOP853, degree 12, 900 s, quant 2 m) ===",
                    s.Name, Fmt(years));

                int repeats = years <= 1.5d ? 5 : (years <= 10.5d ? 3 : 1);
                var wall = new double[repeats];
                BaselineRun last = null;
                string firstHash = null;
                string secondHash = null;
                string dir = Path.Combine(bakeRoot, s.Name + "_" + Fmt(years) + "y");

                for (int r = 0; r < repeats; r++)
                {
                    BaselineRun run = BaselineBakeOnce(s.Create(), years, BaselineConfig(), dir);
                    wall[r] = run.WallSeconds;
                    last = run;
                    if (r == 0)
                    {
                        firstHash = run.CombinedSha256;
                    }
                    else if (r == 1)
                    {
                        secondHash = run.CombinedSha256;
                    }

                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  run {0}: {1:F3} s, {2} bytes, {3} files, {4} segments, sha256 {5}",
                        r + 1, run.WallSeconds, run.TotalBytes, run.FileCount, run.SegmentCount,
                        run.CombinedSha256));
                }

                if (secondHash != null)
                {
                    bool equal = string.Equals(firstHash, secondHash, StringComparison.Ordinal);
                    determinantRows.Add(BaselineCsvRow(s.Name, Fmt(years), repeats,
                        firstHash, secondHash, equal ? "IDENTICAL" : "MISMATCH"));
                    Check(equal, "R0 determinism " + s.Name + " " + Fmt(years) + " y",
                        firstHash + " / " + secondHash);
                }

                double median = BaselineMedian(wall);
                double cv = BaselineCv(wall);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  wall median {0:F3} s, CV {1:F1} %, bytes {2}, sha {3}",
                    median, cv * 100d, last.TotalBytes, last.CombinedSha256));

                // Per-body run rows: size, seams.
                var seamByBody = new Dictionary<string, double[]>();
                foreach (var kv in last.Ephemerides)
                {
                    OrbitingBody body = kv.Key;
                    BakedEphemeris eph = kv.Value;
                    BakedEphemeris mem = eph.SegmentCount > 0
                        ? eph.LoadRangeToMemory(0d, eph.EndSeconds, 1000000)
                        : null;
                    if (mem == null)
                    {
                        throw new InvalidOperationException("R0: ephemeris did not load for " + body.Name);
                    }

                    double seamPos;
                    double seamVel;
                    BaselineSeam(mem, out seamPos, out seamVel);
                    seamByBody[body.Name] = new[] { seamPos, seamVel };

                    long bytes = 0L;
                    string bodyHash = "";
                    if (last.BodySha256.TryGetValue(body.Name, out string hash))
                    {
                        bodyHash = hash;
                    }

                    if (!string.IsNullOrEmpty(eph.FilePath) && File.Exists(eph.FilePath))
                    {
                        bytes = new FileInfo(eph.FilePath).Length;
                    }

                    runRows.Add(BaselineCsvRow(
                        s.Name, Fmt(years), body.Name, mem.SegmentCount, mem.Degree,
                        mem.SegmentLengthSeconds, bytes, bodyHash,
                        seamPos.ToString("R", CultureInfo.InvariantCulture),
                        seamVel.ToString("R", CultureInfo.InvariantCulture),
                        median.ToString("R", CultureInfo.InvariantCulture),
                        (cv * 100d).ToString("R", CultureInfo.InvariantCulture),
                        last.TotalBytes.ToString(CultureInfo.InvariantCulture),
                        last.CombinedSha256));
                }

                // Node errors.
                BaselineNodeErrors(s, last, errorRows);

                // Independent dd-Taylor endpoint (4-body) where affordable.
                if (!s.AnalyticKepler && !ddNone && (ddAll || BaselineListContains(ddEnv, years)))
                {
                    BaselineDdEndpoint(s, last, errorRows);
                }

                // Tighter DOP853 is a CONSISTENCY check only (§5).
                if (!tightNone && (tightAll || BaselineListContains(tightEnv, years)))
                {
                    BaselineTightConsistency(s, last, years, tightRows);
                }

                // Energy drift of the delivered ephemeris (relative coordinates).
                BaselineEnergyRow(s, last, invariantRows);

                // Flush after every horizon: a 1024-year dd reference may run for
                // an hour, and a later failure must not throw away earlier rows.
                BaselineWriteCsv(outDir, runRows, errorRows, determinantRows, invariantRows);
                BaselineWriteTightCsv(outDir, tightRows);
            }
        }

        BaselineWriteCsv(outDir, runRows, errorRows, determinantRows, invariantRows);
        BaselineWriteTightCsv(outDir, tightRows);

        Console.WriteLine();
        Console.WriteLine("R0 baseline artifacts: " + Path.GetFullPath(outDir));
        return 0;
    }

    private static StarSystem BaselineOutdoorsSystem()
    {
        // Source: OutdoorsScene.unity — Sol (mu 2.65433e19, R 234 500 000),
        // Terra (mu 1.28e13, R 1 143 000, a 55 187 600 000, e 0.017, angles 0).
        var bp = new SystemBlueprint();
        bp.Bodies.Add(StudyBody("Sol", -1, 2.65433e19d, 2.345e8d, 0d, 0d, 0d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Terra", 0, 1.28e13d, 1.143e6d, 5.51876e10d, 0.017d, 0d, 0d, 0d, 0d));
        return bp.Build();
    }

    private static StarSystem BaselineContactZoneSystem()
    {
        // Source: ContactZoneSlice.unity — Star (mu 1.327e20, R 696 000 000),
        // Terra (mu 1.28e13, R 1 143 000, a 150 000 000 000, e 0.017, angles 0).
        var bp = new SystemBlueprint();
        bp.Bodies.Add(StudyBody("Star", -1, 1.327e20d, 6.96e8d, 0d, 0d, 0d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Terra", 0, 1.28e13d, 1.143e6d, 1.5e11d, 0.017d, 0d, 0d, 0d, 0d));
        return bp.Build();
    }

    private static BaselineRun BaselineBakeOnce(
        StarSystem sys, double years, BakeConfig config, string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }

        Directory.CreateDirectory(dir);
        var watch = Stopwatch.StartNew();
        Dictionary<OrbitingBody, BakedEphemeris> eph = EphemerisBaker.BakeToFiles(
            sys, dir, years, config, null, null, null, compress: true);
        watch.Stop();

        var run = new BaselineRun
        {
            System = sys,
            Years = years,
            Directory = dir,
            WallSeconds = watch.Elapsed.TotalSeconds,
            Ephemerides = eph,
            BodySha256 = new Dictionary<string, string>(StringComparer.Ordinal)
        };

        string[] files = Directory.GetFiles(dir, "*.bin");
        Array.Sort(files, StringComparer.Ordinal);
        long total = 0L;
        foreach (string file in files)
        {
            total += new FileInfo(file).Length;
        }

        run.TotalBytes = total;
        run.FileCount = files.Length;
        long segments = 0L;
        foreach (var kv in eph)
        {
            segments += kv.Value.SegmentCount;
            if (!string.IsNullOrEmpty(kv.Value.FilePath) && File.Exists(kv.Value.FilePath))
            {
                run.BodySha256[kv.Key.Name] = BaselineHashFile(kv.Value.FilePath);
            }
        }

        run.SegmentCount = segments;
        run.CombinedSha256 = BaselineHashDirectory(dir, files);
        return run;
    }

    private static string BaselineHashFile(string path)
    {
        using (var sha = SHA256.Create())
        using (FileStream fs = File.OpenRead(path))
        {
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
    }

    /// <summary>Hash of manifest.txt followed by every .bin file in ordinal file-name order.</summary>
    private static string BaselineHashDirectory(string dir, string[] files)
    {
        using (var sha = SHA256.Create())
        {
            string manifest = Path.Combine(dir, "manifest.txt");
            if (File.Exists(manifest))
            {
                byte[] mb = File.ReadAllBytes(manifest);
                sha.TransformBlock(mb, 0, mb.Length, null, 0);
            }

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                byte[] nb = Encoding.UTF8.GetBytes(name);
                sha.TransformBlock(nb, 0, nb.Length, null, 0);
                byte[] data = File.ReadAllBytes(file);
                sha.TransformBlock(data, 0, data.Length, null, 0);
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash);
        }
    }

    private static void BaselineSeam(BakedEphemeris e, out double maxPos, out double maxVel)
    {
        maxPos = 0d;
        maxVel = 0d;
        if (e.SegmentCount < 2 || e.Coeffs == null)
        {
            return;
        }

        int width = e.Degree + 1;
        int stride = BakedEphemeris.AxisStride(e.Degree);
        double scale = 2d / e.SegmentLengthSeconds;
        for (int k = 1; k < e.SegmentCount; k++)
        {
            int left = (k - 1) * stride;
            int right = k * stride;
            for (int axis = 0; axis < 3; axis++)
            {
                int offL = left + (axis * width);
                int offR = right + (axis * width);
                double pL = BakedEphemeris.Clenshaw(e.Coeffs, offL, e.Degree, 1d);
                double pR = BakedEphemeris.Clenshaw(e.Coeffs, offR, e.Degree, -1d);
                maxPos = Math.Max(maxPos, Math.Abs(pL - pR));
                double vL = BakedEphemeris.DerivSum(e.Coeffs, offL, e.Degree, 1d) * scale;
                double vR = BakedEphemeris.DerivSum(e.Coeffs, offR, e.Degree, -1d) * scale;
                maxVel = Math.Max(maxVel, Math.Abs(vL - vR));
            }
        }
    }

    private static void BaselineNodeErrors(BaselineSystem s, BaselineRun run, List<string> rows)
    {
        foreach (var kv in run.Ephemerides)
        {
            OrbitingBody body = kv.Key;
            if (body.Parent == null)
            {
                continue;
            }

            BakedEphemeris mem = kv.Value.LoadRangeToMemory(0d, kv.Value.EndSeconds, 1000000);
            if (mem == null)
            {
                continue;
            }

            double[] nodes = BaselineNodeTimes(mem, 4096);
            double maxPos = 0d;
            double sumSq = 0d;
            double maxVel = 0d;
            int count = 0;

            if (s.AnalyticKepler)
            {
                Vector3d rail0;
                Vector3d railV0;
                body.EvaluateKeplerOffset(0d, out rail0, out railV0);
                mem.TryEvaluate(0d, out Vector3d baked0, out _);
                double init = (baked0 - rail0).Magnitude;

                foreach (double t in nodes)
                {
                    double tt = Math.Min(t, mem.EndSeconds);
                    if (!mem.TryEvaluate(tt, out Vector3d bp, out Vector3d bv))
                    {
                        continue;
                    }

                    body.EvaluateKeplerOffset(tt, out Vector3d rp, out Vector3d rv);
                    double dp = (bp - rp).Magnitude;
                    double dv = (bv - rv).Magnitude;
                    maxPos = Math.Max(maxPos, dp);
                    maxVel = Math.Max(maxVel, dv);
                    sumSq += dp * dp;
                    count++;
                }

                rows.Add(BaselineCsvRow(
                    s.Name, Fmt(run.Years), body.Name, "analytic-kepler-rail", count,
                    maxPos.ToString("R", CultureInfo.InvariantCulture),
                    Math.Sqrt(sumSq / Math.Max(1, count)).ToString("R", CultureInfo.InvariantCulture),
                    maxVel.ToString("R", CultureInfo.InvariantCulture),
                    "", "init |baked(0)-rail(0)| = " + init.ToString("E3", CultureInfo.InvariantCulture) + " m"));
            }
            else
            {
                // No independent dense reference is affordable for the 4-body
                // preset in R0; the endpoint dd-Taylor row and the tighter-DOP853
                // consistency row carry the 4-body error information.
                rows.Add(BaselineCsvRow(
                    s.Name, Fmt(run.Years), body.Name, "none-in-R0", 0, "", "", "", "",
                    "4-body dense independent reference is deferred (R3/R5); see dd-taylor endpoint and tighter-DOP853 consistency rows"));
            }
        }
    }

    private static double[] BaselineNodeTimes(BakedEphemeris e, int maxSamples)
    {
        long total = (long)e.SegmentCount * (e.Degree + 1);
        int stride = total <= maxSamples ? 1 : (int)Math.Ceiling(total / (double)maxSamples);
        var times = new List<double>((int)Math.Min(total, maxSamples + 2));
        long index = 0L;
        for (int seg = 0; seg < e.SegmentCount; seg++)
        {
            double baseT = e.T0Seconds + (seg * e.SegmentLengthSeconds);
            for (int j = 0; j <= e.Degree; j++)
            {
                if (index % stride == 0L)
                {
                    double off = e.SegmentLengthSeconds * (1d - Math.Cos(j * Math.PI / e.Degree)) * 0.5d;
                    times.Add(Math.Min(baseT + off, e.EndSeconds));
                }

                index++;
            }
        }

        if (times.Count == 0 || times[times.Count - 1] < e.EndSeconds)
        {
            times.Add(e.EndSeconds);
        }

        return times.ToArray();
    }

    private static BakedEphemeris BaselineFindByName(
        Dictionary<OrbitingBody, BakedEphemeris> map, string name)
    {
        foreach (var kv in map)
        {
            if (string.Equals(kv.Key.Name, name, StringComparison.Ordinal))
            {
                return kv.Value;
            }
        }

        return null;
    }

    private static void BaselineDdEndpoint(BaselineSystem s, BaselineRun run, List<string> rows)
    {
        if (s.AnalyticKepler)
        {
            return;
        }

        var input = new TaylorNBodyInput
        {
            Names = new string[run.System.AllBodies.Count],
            Mu = new double[run.System.AllBodies.Count],
            P0 = null,
            V0 = null
        };

        BaselineInitialState(run.System, out double[] p0, out double[] v0, out double[] mu);
        for (int i = 0; i < mu.Length; i++)
        {
            input.Names[i] = run.System.AllBodies[i].Name;
            input.Mu[i] = mu[i];
        }

        input.P0 = p0;
        input.V0 = v0;

        double totalSeconds = run.Years * BaselineYearSeconds;
        double ddMaxStep = 86400d;
        string ddMaxStepEnv = Environment.GetEnvironmentVariable("RKN12_BASELINE_DD_MAXSTEP");
        if (!string.IsNullOrEmpty(ddMaxStepEnv))
        {
            double.TryParse(ddMaxStepEnv, NumberStyles.Float, CultureInfo.InvariantCulture, out ddMaxStep);
        }

        Console.WriteLine("  dd-Taylor endpoint reference: order 32, tol 1e-30, max step " +
            ddMaxStep.ToString("G", CultureInfo.InvariantCulture) + " s, horizon " +
            Fmt(run.Years) + " y ...");
        var watch = Stopwatch.StartNew();
        DdNBodyResult dd = RunTaylorNBodyDdOptSeconds(
            input, totalSeconds, 32, 1e-30d, 1e-30d, ddMaxStep, 1e-6d);
        watch.Stop();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  dd-Taylor done: {0} steps, {1:F1} s", dd.Steps, watch.Elapsed.TotalSeconds));

        int n = run.System.AllBodies.Count;
        for (int i = 0; i < n; i++)
        {
            OrbitingBody body = run.System.AllBodies[i];
            if (body.Parent == null)
            {
                continue;
            }

            int parent = BaselineIndexOf(run.System.AllBodies, body.Parent);
            run.Ephemerides.TryGetValue(body, out BakedEphemeris eph);
            if (eph == null)
            {
                continue;
            }

            double t = Math.Min(totalSeconds, eph.EndSeconds);
            eph.TryEvaluate(t, out Vector3d bp, out Vector3d bv);
            double rpx = dd.P[3 * i] - dd.P[3 * parent];
            double rpy = dd.P[(3 * i) + 1] - dd.P[(3 * parent) + 1];
            double rpz = dd.P[(3 * i) + 2] - dd.P[(3 * parent) + 2];
            double rvx = dd.V[3 * i] - dd.V[3 * parent];
            double rvy = dd.V[(3 * i) + 1] - dd.V[(3 * parent) + 1];
            double rvz = dd.V[(3 * i) + 2] - dd.V[(3 * parent) + 2];
            double dp = Math.Sqrt(
                ((bp.X - rpx) * (bp.X - rpx)) +
                ((bp.Y - rpy) * (bp.Y - rpy)) +
                ((bp.Z - rpz) * (bp.Z - rpz)));
            double dv = Math.Sqrt(
                ((bv.X - rvx) * (bv.X - rvx)) +
                ((bv.Y - rvy) * (bv.Y - rvy)) +
                ((bv.Z - rvz) * (bv.Z - rvz)));

            rows.Add(BaselineCsvRow(
                s.Name, Fmt(run.Years), body.Name, "dd-taylor-K32-tol1e-30", 1,
                dp.ToString("R", CultureInfo.InvariantCulture), "",
                dv.ToString("R", CultureInfo.InvariantCulture),
                watch.Elapsed.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture),
                "endpoint node; dd steps=" + dd.Steps.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static void BaselineTightConsistency(
        BaselineSystem s, BaselineRun run, double years, List<string> rows)
    {
        string dir = Path.Combine(Path.GetDirectoryName(run.Directory) ?? ".", "tight_" + s.Name + "_" + Fmt(years) + "y");
        Console.WriteLine("  tighter DOP853 consistency bake (60 s, phase 0.002) ...");
        BaselineRun tight = BaselineBakeOnce(s.Create(), years, BaselineTightConfig(), dir);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  tighter DOP853 done: {0:F1} s, {1} bytes", tight.WallSeconds, tight.TotalBytes));

        foreach (var kv in run.Ephemerides)
        {
            OrbitingBody body = kv.Key;
            if (body.Parent == null)
            {
                continue;
            }

            BakedEphemeris baseMem = kv.Value.LoadRangeToMemory(0d, kv.Value.EndSeconds, 1000000);
            BakedEphemeris tightEph = BaselineFindByName(tight.Ephemerides, body.Name);
            BakedEphemeris tightMem = tightEph != null
                ? tightEph.LoadRangeToMemory(0d, tightEph.EndSeconds, 1000000)
                : null;
            if (baseMem == null || tightMem == null)
            {
                continue;
            }

            double[] nodes = BaselineNodeTimes(baseMem, 512);
            double maxPos = 0d;
            double maxVel = 0d;
            double sumSq = 0d;
            int count = 0;
            foreach (double t in nodes)
            {
                double tt = Math.Min(t, Math.Min(baseMem.EndSeconds, tightMem.EndSeconds));
                if (!baseMem.TryEvaluate(tt, out Vector3d p, out Vector3d v))
                {
                    continue;
                }

                if (!tightMem.TryEvaluate(tt, out Vector3d rp, out Vector3d rv))
                {
                    continue;
                }

                double dp = (p - rp).Magnitude;
                double dv = (v - rv).Magnitude;
                maxPos = Math.Max(maxPos, dp);
                maxVel = Math.Max(maxVel, dv);
                sumSq += dp * dp;
                count++;
            }

            rows.Add(BaselineCsvRow(
                s.Name, Fmt(years), body.Name, "tighter-DOP853-consistency", count,
                maxPos.ToString("R", CultureInfo.InvariantCulture),
                Math.Sqrt(sumSq / Math.Max(1, count)).ToString("R", CultureInfo.InvariantCulture),
                maxVel.ToString("R", CultureInfo.InvariantCulture),
                tight.WallSeconds.ToString("R", CultureInfo.InvariantCulture),
                "60 s / phase 0.002 bake; consistency only, NOT an independent reference (§5)"));
        }
    }

    private static void BaselineEnergyRow(BaselineSystem s, BaselineRun run, List<string> rows)
    {
        int n = run.System.AllBodies.Count;
        var mu = new double[n];
        for (int i = 0; i < n; i++)
        {
            mu[i] = run.System.AllBodies[i].ResolveStandardGravitationalParameter();
        }

        int samples = 257;
        double maxRel = 0d;
        double e0 = 0d;
        double eEnd = 0d;
        for (int k = 0; k < samples; k++)
        {
            double t = run.Years * BaselineYearSeconds * k / (samples - 1.0d);
            double e = BaselineEnergy(run, mu, t, out bool ok);
            if (!ok)
            {
                continue;
            }

            if (k == 0)
            {
                e0 = e;
            }

            eEnd = e;
            if (e0 != 0d && double.IsFinite(e))
            {
                maxRel = Math.Max(maxRel, Math.Abs((e - e0) / e0));
            }
        }

        rows.Add(BaselineCsvRow(
            s.Name, Fmt(run.Years), samples,
            maxRel.ToString("R", CultureInfo.InvariantCulture),
            e0.ToString("R", CultureInfo.InvariantCulture),
            eEnd.ToString("R", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Root-relative state at t: EphemerisBaker stores each body's offset from its
    /// PARENT, so the chain must be summed (a moon's offset alone is not a
    /// heliocentric position).
    /// </summary>
    private static bool BaselineRootRelative(
        BaselineRun run, OrbitingBody body, double t, out Vector3d position, out Vector3d velocity)
    {
        if (body.Parent == null)
        {
            position = Vector3d.Zero;
            velocity = Vector3d.Zero;
            return true;
        }

        run.Ephemerides.TryGetValue(body, out BakedEphemeris eph);
        if (eph == null)
        {
            position = Vector3d.Zero;
            velocity = Vector3d.Zero;
            return false;
        }

        double tt = Math.Max(0d, Math.Min(t, eph.EndSeconds));
        if (!eph.TryEvaluate(tt, out Vector3d localP, out Vector3d localV))
        {
            position = Vector3d.Zero;
            velocity = Vector3d.Zero;
            return false;
        }

        if (!BaselineRootRelative(run, body.Parent, t, out Vector3d parentP, out Vector3d parentV))
        {
            position = Vector3d.Zero;
            velocity = Vector3d.Zero;
            return false;
        }

        position = parentP + localP;
        velocity = parentV + localV;
        return true;
    }

    private static double BaselineEnergy(BaselineRun run, double[] mu, double t, out bool ok)
    {
        int n = run.System.AllBodies.Count;
        var r = new double[3 * n];
        var u = new double[3 * n];
        ok = true;
        for (int i = 0; i < n; i++)
        {
            OrbitingBody body = run.System.AllBodies[i];
            if (body.Parent == null)
            {
                continue;
            }

            Vector3d relP;
            Vector3d relV;
            if (!BaselineRootRelative(run, body, t, out relP, out relV))
            {
                ok = false;
                return 0d;
            }

            r[3 * i] = relP.X;
            r[(3 * i) + 1] = relP.Y;
            r[(3 * i) + 2] = relP.Z;
            u[3 * i] = relV.X;
            u[(3 * i) + 1] = relV.Y;
            u[(3 * i) + 2] = relV.Z;
        }

        // Root velocity from total momentum = 0 (the bake's hierarchy correction
        // makes the whole-system barycentre coincide with the root at t=0).
        double totalMass = 0d;
        double px = 0d;
        double py = 0d;
        double pz = 0d;
        for (int i = 0; i < n; i++)
        {
            totalMass += mu[i];
            px += mu[i] * u[3 * i];
            py += mu[i] * u[(3 * i) + 1];
            pz += mu[i] * u[(3 * i) + 2];
        }

        double vrx = -px / totalMass;
        double vry = -py / totalMass;
        double vrz = -pz / totalMass;
        double kinetic = 0d;
        for (int i = 0; i < n; i++)
        {
            double vx = u[3 * i] + vrx;
            double vy = u[(3 * i) + 1] + vry;
            double vz = u[(3 * i) + 2] + vrz;
            kinetic += 0.5d * mu[i] * ((vx * vx) + (vy * vy) + (vz * vz));
        }

        double potential = 0d;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = r[3 * i] - r[3 * j];
                double dy = r[(3 * i) + 1] - r[(3 * j) + 1];
                double dz = r[(3 * i) + 2] - r[(3 * j) + 2];
                double dist = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                if (!(dist > 0d))
                {
                    ok = false;
                    return 0d;
                }

                potential -= mu[i] * mu[j] / dist;
            }
        }

        return kinetic + potential;
    }

    private static void BaselineWriteTightCsv(string outDir, List<string> rows)
    {
        BaselineMergeWrite(Path.Combine(outDir, "tight_consistency.csv"),
            "system,horizon_years,body,reference,samples,max_pos_m,rms_pos_m,max_vel_mps,tight_wall_s,note",
            rows);
    }

    private static void BaselineWriteCsv(
        string outDir, List<string> runRows, List<string> errorRows,
        List<string> determinantRows, List<string> invariantRows)
    {
        BaselineMergeWrite(Path.Combine(outDir, "bake_runs.csv"),
            "system,horizon_years,body,segments,degree,segment_seconds,body_bytes,body_sha256," +
            "seam_pos_m,seam_vel_mps,wall_median_s,wall_cv_percent,total_bytes,combined_sha256",
            runRows);

        BaselineMergeWrite(Path.Combine(outDir, "node_errors.csv"),
            "system,horizon_years,body,reference,samples,max_pos_m,rms_pos_m,max_vel_mps," +
            "reference_ms,note",
            errorRows);

        BaselineMergeWrite(Path.Combine(outDir, "determinism.csv"),
            "system,horizon_years,runs,sha_first,sha_second,verdict",
            determinantRows);

        BaselineMergeWrite(Path.Combine(outDir, "invariants.csv"),
            "system,horizon_years,samples,energy_rel_drift,e0,e_end",
            invariantRows);
    }

    /// <summary>
    /// Rewrites only the (system, horizon) keys present in <paramref name="rows"/> and
    /// keeps all other existing rows. This lets a short re-run (e.g. only 1024 years
    /// with dd-Taylor disabled) extend the baseline without losing earlier evidence.
    /// </summary>
    private static void BaselineMergeWrite(string path, string header, List<string> rows)
    {
        var replace = new HashSet<string>(StringComparer.Ordinal);
        foreach (string row in rows)
        {
            replace.Add(BaselineCsvKey(row));
        }

        var merged = new List<string> { header };
        if (File.Exists(path))
        {
            string[] existing = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 1; i < existing.Length; i++)
            {
                string line = existing[i];
                if (line.Length > 0 && !replace.Contains(BaselineCsvKey(line)))
                {
                    merged.Add(line);
                }
            }
        }

        merged.AddRange(rows);
        File.WriteAllLines(path, merged, new UTF8Encoding(false));
    }

    /// <summary>Key = first two CSV fields (system, horizon); neither is ever quoted.</summary>
    private static string BaselineCsvKey(string line)
    {
        int first = line.IndexOf(',');
        if (first < 0)
        {
            return line;
        }

        int second = line.IndexOf(',', first + 1);
        return second < 0 ? line : line.Substring(0, second);
    }

    /// <summary>
    /// Exact copy of EphemerisBaker.RunBake's initial state: world states at t=0,
    /// masses mu/G, leaf-first subtree barycentric correction. The dd-Taylor
    /// reference must start from the same state as the bake.
    /// </summary>
    private static void BaselineInitialState(
        StarSystem sys, out double[] p, out double[] v, out double[] mu)
    {
        var bodies = sys.AllBodies;
        int n = bodies.Count;
        p = new double[3 * n];
        v = new double[3 * n];
        mu = new double[n];
        for (int i = 0; i < n; i++)
        {
            bodies[i].EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
            mu[i] = bodies[i].ResolveStandardGravitationalParameter();
            p[3 * i] = bp.X;
            p[(3 * i) + 1] = bp.Y;
            p[(3 * i) + 2] = bp.Z;
            v[3 * i] = bv.X;
            v[(3 * i) + 1] = bv.Y;
            v[(3 * i) + 2] = bv.Z;
        }

        double[] railP = (double[])p.Clone();
        double[] railV = (double[])v.Clone();
        for (int i = n - 1; i >= 0; i--)
        {
            OrbitingBody b = bodies[i];
            if (b.Children.Count == 0 || mu[i] <= 0d)
            {
                continue;
            }

            double mSub = mu[i];
            double sx = 0d;
            double sy = 0d;
            double sz = 0d;
            double svx = 0d;
            double svy = 0d;
            double svz = 0d;
            for (int j = 0; j < b.Children.Count; j++)
            {
                int c = BaselineIndexOf(bodies, b.Children[j]);
                if (c < 0)
                {
                    continue;
                }

                double mc = BaselineSubtreeMu(bodies, mu, c);
                mSub += mc;
                sx += mc * (railP[3 * c] - railP[3 * i]);
                sy += mc * (railP[(3 * c) + 1] - railP[(3 * i) + 1]);
                sz += mc * (railP[(3 * c) + 2] - railP[(3 * i) + 2]);
                svx += mc * (railV[3 * c] - railV[3 * i]);
                svy += mc * (railV[(3 * c) + 1] - railV[(3 * i) + 1]);
                svz += mc * (railV[(3 * c) + 2] - railV[(3 * i) + 2]);
            }

            BaselineShiftSubtree(bodies, i, p, v,
                -sx / mSub, -sy / mSub, -sz / mSub,
                -svx / mSub, -svy / mSub, -svz / mSub);
        }
    }

    private static int BaselineIndexOf(IReadOnlyList<OrbitingBody> bodies, OrbitingBody b)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            if (ReferenceEquals(bodies[i], b))
            {
                return i;
            }
        }

        return -1;
    }

    private static double BaselineSubtreeMu(
        IReadOnlyList<OrbitingBody> bodies, double[] mu, int index)
    {
        double total = mu[index];
        OrbitingBody b = bodies[index];
        for (int j = 0; j < b.Children.Count; j++)
        {
            int c = BaselineIndexOf(bodies, b.Children[j]);
            if (c >= 0)
            {
                total += BaselineSubtreeMu(bodies, mu, c);
            }
        }

        return total;
    }

    private static void BaselineShiftSubtree(
        IReadOnlyList<OrbitingBody> bodies, int index, double[] p, double[] v,
        double dx, double dy, double dz, double dvx, double dvy, double dvz)
    {
        p[3 * index] += dx;
        p[(3 * index) + 1] += dy;
        p[(3 * index) + 2] += dz;
        v[3 * index] += dvx;
        v[(3 * index) + 1] += dvy;
        v[(3 * index) + 2] += dvz;
        OrbitingBody b = bodies[index];
        for (int j = 0; j < b.Children.Count; j++)
        {
            int c = BaselineIndexOf(bodies, b.Children[j]);
            if (c >= 0)
            {
                BaselineShiftSubtree(bodies, c, p, v, dx, dy, dz, dvx, dvy, dvz);
            }
        }
    }

    private static double BaselineMedian(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        int n = copy.Length;
        if (n == 0)
        {
            return 0d;
        }

        return (n & 1) == 1 ? copy[n / 2] : 0.5d * (copy[(n / 2) - 1] + copy[n / 2]);
    }

    private static double BaselineCv(double[] values)
    {
        if (values.Length < 2)
        {
            return 0d;
        }

        double mean = 0d;
        for (int i = 0; i < values.Length; i++)
        {
            mean += values[i];
        }

        mean /= values.Length;
        if (!(mean > 0d))
        {
            return 0d;
        }

        double sq = 0d;
        for (int i = 0; i < values.Length; i++)
        {
            double d = values[i] - mean;
            sq += d * d;
        }

        return Math.Sqrt(sq / (values.Length - 1)) / mean;
    }

    private static bool BaselineListContains(string list, double years)
    {
        if (string.IsNullOrEmpty(list))
        {
            return false;
        }

        string[] parts = list.Split(',');
        foreach (string part in parts)
        {
            if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && Math.Abs(value - years) < 1e-9d)
            {
                return true;
            }
        }

        return false;
    }

    private static double[] BaselineParseHorizons(string list)
    {
        var result = new List<double>();
        string[] parts = list.Split(',');
        foreach (string part in parts)
        {
            if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && value > 0d)
            {
                result.Add(value);
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("RKN12_BASELINE_HORIZONS has no positive numbers: " + list);
        }

        return result.ToArray();
    }

    private static string BaselineCsvRow(params object[] cells)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            string text = cells[i] == null ? "" : Convert.ToString(cells[i], CultureInfo.InvariantCulture);
            if (text.IndexOfAny(new[] { ',', '"', '\n' }) >= 0)
            {
                sb.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
            }
            else
            {
                sb.Append(text);
            }
        }

        return sb.ToString();
    }

    private static string Fmt(double years)
    {
        return years.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
