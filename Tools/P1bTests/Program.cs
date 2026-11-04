using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal sealed class TestThrustSource : IDynamicsSource
{
    public double ThrustNewtons;
    public double MassFlowKgS;
    public Vector3d Direction = new Vector3d(1d, 0d, 0d);
    public Func<double, bool> ActiveAt;

    public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
    {
        bool active = ActiveAt != null ? ActiveAt(timeSeconds) : ThrustNewtons > 0d;
        if (!active)
        {
            return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
        }

        return new DynamicsContribution(Vector3d.Zero, Direction.Normalized * ThrustNewtons, MassFlowKgS, Vector3d.Zero);
    }
}

internal static partial class P1bTests
{
    private static bool allowStaleRun;

    private static int RunCaseProcess(string name, string logDir)
    {
        string host = Environment.ProcessPath;
        if (string.IsNullOrEmpty(host))
        {
            host = "dotnet";
        }

        var start = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory
        };
        string entry = Assembly.GetEntryAssembly()?.Location;
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(entry))
        {
            start.ArgumentList.Add(entry);
        }

        if (!string.IsNullOrEmpty(logDir))
        {
            Directory.CreateDirectory(logDir);
            string safeName = name;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalid, '_');
            }

            start.ArgumentList.Add("--log");
            start.ArgumentList.Add(Path.Combine(logDir, safeName + ".log"));
        }

        if (allowStaleRun)
        {
            start.ArgumentList.Add("--allow-stale");
        }

        start.ArgumentList.Add("--exact");
        start.ArgumentList.Add(name);
        using (Process process = Process.Start(start))
        {
            if (process == null)
            {
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
    }

    private static int RunParallel(List<P1bCase> cases, int jobs, string logDir)
    {
        Console.WriteLine("Parallel run: " + cases.Count + " case(s), " + jobs + " job(s)");
        if (!string.IsNullOrEmpty(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        var failed = new System.Collections.Concurrent.ConcurrentBag<string>();
        int next = -1;
        var gate = new object();
        var watch = Stopwatch.StartNew();
        Parallel.For(
            0,
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = jobs },
            worker =>
            {
                while (true)
                {
                    int index;
                    lock (gate)
                    {
                        index = ++next;
                    }

                    if (index >= cases.Count)
                    {
                        return;
                    }

                    P1bCase entry = cases[index];
                    var caseWatch = Stopwatch.StartNew();
                    int code = RunCaseProcess(entry.Name, logDir);
                    Console.WriteLine(
                        "[" + entry.Name + "] " + (code == 0 ? "ok" : "FAIL")
                        + (entry.Diagnostic ? " (diagnostic, no checks)" : "")
                        + " in " + caseWatch.ElapsedMilliseconds + " ms");
                    if (code != 0)
                    {
                        failed.Add(entry.Name + " (exit " + code + ")");
                    }
                }
            });
        Console.WriteLine("Parallel wall: " + watch.ElapsedMilliseconds + " ms");
        if (failed.Count == 0)
        {
            bool anyChecked = false;
            foreach (P1bCase entry in cases)
            {
                if (!entry.Diagnostic)
                {
                    anyChecked = true;
                    break;
                }
            }

            Console.WriteLine(anyChecked ? "ALL PASS" : "DIAGNOSTICS COMPLETE (no checks)");
            return 0;
        }

        foreach (string name in failed)
        {
            Console.WriteLine("FAILED " + name);
        }

        Console.WriteLine(failed.Count + " FAILED CASE(S)");
        return failed.Count;
    }

    private static bool RunSerialCase(P1bCase entry, string phase)
    {
        var caseWatch = Stopwatch.StartNew();
        int before = failures;
        int code = entry.Run();
        bool ok = code == 0 && failures == before;
        if (!ok && failures == before)
        {
            failures++;
        }

        Console.WriteLine("[" + entry.Name + "] " + (ok ? "ok" : "FAIL (exit " + code + ")") +
                          (entry.Diagnostic ? " (diagnostic, no checks)" : "") +
                          " in " + caseWatch.ElapsedMilliseconds + " ms" +
                          (string.IsNullOrEmpty(phase) ? "" : " (" + phase + ")"));
        return ok;
    }

    private static string ResolveUserPath(string path)
    {
        return string.IsNullOrEmpty(path) ? path : Path.GetFullPath(path);
    }

    private static string FindHarnessRoot()
    {
        // Prefer the checkout layout so Study/ and Data/ resolve regardless of
        // the shell's working directory. A published harness with its own
        // Study/ directory keeps that directory.
        if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Study")))
        {
            return AppContext.BaseDirectory;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "Tools", "P1bTests");
            if (Directory.Exists(Path.Combine(candidate, "Study")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static DateTime LatestSourceWriteTimeUtc(string root)
    {
        // A --no-build run with a stale assembly is the failure mode this guards:
        // it looks like a green suite while the results belong to old code.
        // Only sources that actually feed the harness are considered; an
        // unrelated Unity editor script must not force a rebuild.
        DateTime latest = DateTime.MinValue;
        var roots = new List<string>
        {
            Path.Combine(root, "Study"),
            Path.Combine(root, "Tests"),
            Path.Combine(root, "Reference"),
            Path.Combine(root, "..", "..", "Assets", "_Project", "Scripts", "Core"),
            Path.Combine(root, "..", "..", "Assets", "_Project", "Scripts", "Spacecraft"),
            Path.Combine(root, "..", "..", "Assets", "_Project", "Scripts", "Universe"),
            Path.Combine(root, "..", "..", "Assets", "_Project", "Scripts", "Ephemeris")
        };
        foreach (string directory in roots)
        {
            string full = Path.GetFullPath(directory);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                DateTime written = File.GetLastWriteTimeUtc(file);
                if (written > latest)
                {
                    latest = written;
                }
            }
        }

        foreach (string file in new[]
                 {
                     Path.Combine(root, "Program.cs"),
                     Path.Combine(root, "TestSupport.cs"),
                     Path.Combine(root, "CheckSupport.cs"),
                     Path.Combine(root, "TestRegistry.cs"),
                     Path.Combine(root, "UnityStubs.cs"),
                     Path.Combine(root, "..", "GameSources.props")
                 })
        {
            string full = Path.GetFullPath(file);
            if (!File.Exists(full))
            {
                continue;
            }

            DateTime written = File.GetLastWriteTimeUtc(full);
            if (written > latest)
            {
                latest = written;
            }
        }

        return latest;
    }

    private static string TryReadGitHead()
    {
        DirectoryInfo directory = new DirectoryInfo(FindHarnessRoot());
        while (directory != null)
        {
            string git = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(git))
            {
                string head = Path.Combine(git, "HEAD");
                if (File.Exists(head))
                {
                    string text = File.ReadAllText(head).Trim();
                    const string prefix = "ref: ";
                    if (text.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        string reference = Path.Combine(git, text.Substring(prefix.Length)
                            .Replace('/', Path.DirectorySeparatorChar));
                        return File.Exists(reference)
                            ? File.ReadAllText(reference).Trim()
                            : text;
                    }

                    return text;
                }
            }

            directory = directory.Parent;
        }

        return "unknown";
    }

    private static bool WarnIfStale(string root)
    {
        string assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
        {
            return false;
        }

        DateTime built = File.GetLastWriteTimeUtc(assemblyPath);
        DateTime latest = LatestSourceWriteTimeUtc(root);
        if (latest <= built)
        {
            return false;
        }

        Console.WriteLine("STALE BUILD: " + assemblyPath);
        Console.WriteLine("  assembly written: " + built.ToString("O", CultureInfo.InvariantCulture));
        Console.WriteLine("  newest source:    " + latest.ToString("O", CultureInfo.InvariantCulture));
        Console.WriteLine("  rebuild with: dotnet build Tools/P1bTests/P1bTests.csproj");
        Console.WriteLine("  (or pass --allow-stale to run anyway)");
        return true;
    }

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.WriteLine("P1b tests...");
        string filter = null;
        bool fastOnly = false;
        bool slowOnly = false;
        bool includeGenerators = false;
        bool includeDiagnostics = false;
        int from = int.MinValue;
        int to = int.MaxValue;
        int jobs = 1;
        int repeat = 1;
        int benchJobs = 0;
        string exact = null;
        string traceDir = null;
        string logDir = null;
        string logFile = null;
        bool allowStale = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--list")
            {
                foreach (P1bCase entry in TestRegistry())
                {
                    string kind = entry.Diagnostic
                        ? "diagnostic "
                        : entry.Generator ? "generator " : entry.Slow ? "slow " : "fast ";
                    Console.WriteLine(kind + entry.Name);
                }

                return 0;
            }

            if (args[i] == "--from" && i + 1 < args.Length)
            {
                from = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                i++;
            }
            else if (args[i] == "--to" && i + 1 < args.Length)
            {
                to = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
                i++;
            }
            else if (args[i] == "--filter" && i + 1 < args.Length)
            {
                filter = args[i + 1];
                i++;
            }
            else if (args[i] == "--exact" && i + 1 < args.Length)
            {
                exact = args[i + 1];
                i++;
            }
            else if (args[i] == "--parallel" || args[i] == "--jobs")
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    jobs = Math.Max(1, int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture));
                    i++;
                }
                else
                {
                    jobs = Math.Max(1, Environment.ProcessorCount);
                }
            }
            else if (args[i] == "--repeat" && i + 1 < args.Length)
            {
                repeat = Math.Max(1, int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture));
                i++;
            }
            else if (args[i] == "--bench-parallel")
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    benchJobs = Math.Max(1, int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture));
                    i++;
                }
                else
                {
                    benchJobs = Math.Max(1, Environment.ProcessorCount);
                }
            }
            else if (args[i] == "--fast")
            {
                fastOnly = true;
            }
            else if (args[i] == "--slow")
            {
                slowOnly = true;
            }
            else if (args[i] == "--generators" || args[i] == "--include-generators")
            {
                includeGenerators = true;
            }
            else if (args[i] == "--diagnostics" || args[i] == "--include-diagnostics")
            {
                includeDiagnostics = true;
            }
            else if (args[i] == "--allow-stale")
            {
                allowStale = true;
            }
            else if (args[i] == "--log-dir" && i + 1 < args.Length)
            {
                logDir = args[i + 1];
                i++;
            }
            else if (args[i] == "--log" && i + 1 < args.Length)
            {
                logFile = args[i + 1];
                i++;
            }
            else if (args[i] == "--trace" && i + 1 < args.Length)
            {
                traceDir = args[i + 1];
                i++;
            }
        }

        // Study/ and Data/ paths in this harness are relative to the P1bTests
        // project, not to the shell's current directory. Resolve user paths
        // first, then pin the process there so "dotnet run --project
        // Tools/P1bTests" from the repository root reaches the same files as a
        // run from Tools/P1bTests. Child processes inherit the pinned
        // directory through RunCaseProcess.
        allowStaleRun = allowStale;
        logDir = ResolveUserPath(logDir);
        traceDir = ResolveUserPath(traceDir);
        logFile = ResolveUserPath(logFile);
        Environment.CurrentDirectory = FindHarnessRoot();

        if (logFile != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            var writer = new StreamWriter(logFile, false, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(writer);
            Console.SetError(writer);
            Console.WriteLine("# P1bTests log");
            Console.WriteLine("# commit: " + TryReadGitHead());
            string assemblyPath = Assembly.GetEntryAssembly()?.Location ?? "";
            Console.WriteLine("# assembly: " + assemblyPath);
            if (assemblyPath.Length > 0 && File.Exists(assemblyPath))
            {
                Console.WriteLine("# assembly written: "
                    + File.GetLastWriteTimeUtc(assemblyPath).ToString("O", CultureInfo.InvariantCulture));
            }

            Console.WriteLine("# newest source: "
                + LatestSourceWriteTimeUtc(Environment.CurrentDirectory)
                    .ToString("O", CultureInfo.InvariantCulture));
        }

        if (!allowStale && WarnIfStale(Environment.CurrentDirectory))
        {
            return 1;
        }

        if (traceDir != null)
        {
            return RunLandingTraces(traceDir);
        }

        if (benchJobs > 0)
        {
            P1bCase probe = default(P1bCase);
            bool foundProbe = false;
            foreach (P1bCase entry in TestRegistry())
            {
                if (entry.Name == "Test520_ParallelProbe")
                {
                    probe = entry;
                    foundProbe = true;
                    break;
                }
            }

            if (!foundProbe)
            {
                Console.WriteLine("Test520_ParallelProbe is not registered");
                return 1;
            }

            Console.WriteLine("Parallel benchmark: one probe, then " + benchJobs + " process copies");
            var singleWatch = Stopwatch.StartNew();
            int before = failures;
            int probeCode = probe.Run();
            singleWatch.Stop();
            if (probeCode != 0 || failures != before)
            {
                Console.WriteLine("probe failed; benchmark aborted");
                return probeCode != 0 ? probeCode : failures - before;
            }

            var copies = new List<P1bCase>(benchJobs);
            for (int i = 0; i < benchJobs; i++)
            {
                copies.Add(probe);
            }

            var parallelWatch = Stopwatch.StartNew();
            int code = RunParallel(copies, benchJobs, null);
            parallelWatch.Stop();
            double speedup = (double)singleWatch.ElapsedMilliseconds * benchJobs
                / Math.Max(1L, parallelWatch.ElapsedMilliseconds);
            Console.WriteLine(
                "Benchmark single=" + singleWatch.ElapsedMilliseconds
                + " ms, parallel=" + parallelWatch.ElapsedMilliseconds
                + " ms, speedup=" + speedup.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "x");
            return code;
        }

        var selected = new List<P1bCase>();
        foreach (P1bCase entry in TestRegistry())
        {
            if (exact != null && entry.Name != exact)
            {
                continue;
            }

            if (filter != null && entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            // --from/--to are needed because --filter matches a substring and
            // test names are numbered (Test9, Test90..Test99), so "Test9" would
            // cover ten different numbers at once. Splitting into groups for
            // sequential runs without loading the CPU all at once is otherwise
            // impossible.
            if (from != int.MinValue || to != int.MaxValue)
            {
                int num = TestNumber(entry.Name);
                if (num < 0 || num < from || num > to)
                {
                    continue;
                }
            }

            if (entry.Generator && !includeGenerators && exact == null)
            {
                continue;
            }

            if (entry.Diagnostic && !includeDiagnostics && exact == null)
            {
                continue;
            }

            if (fastOnly && entry.Slow)
            {
                continue;
            }

            if (slowOnly && !entry.Slow)
            {
                continue;
            }

            selected.Add(entry);
        }

        if (exact != null && selected.Count == 0)
        {
            Console.WriteLine("No exact case: " + exact);
            return 1;
        }

        if (selected.Count == 0)
        {
            // A filter that matched nothing (or only opt-in diagnostic/generator
            // cases) must not look like a green run in CI.
            Console.WriteLine("No cases selected (diagnostics/generators are opt-in).");
            return 1;
        }

        if (repeat > 1)
        {
            var expanded = new List<P1bCase>(selected.Count * repeat);
            for (int copy = 0; copy < repeat; copy++)
            {
                expanded.AddRange(selected);
            }

            selected = expanded;
        }

        if (jobs > 1 && exact == null)
        {
            // Test518 rewrites the shared bake exports that Test523/527/528
            // read. Run it to completion before the parallel wave so a reader
            // can never see a half-written JSON file.
            var fixtureWriters = new List<P1bCase>();
            var parallelCases = new List<P1bCase>(selected.Count);
            foreach (P1bCase entry in selected)
            {
                if (entry.Name == "Test518_EphemerisExport" ||
                    entry.Name == "Test529_TenYearDdGolden")
                {
                    fixtureWriters.Add(entry);
                }
                else
                {
                    parallelCases.Add(entry);
                }
            }

            foreach (P1bCase writer in fixtureWriters)
            {
                if (!RunSerialCase(writer, "fixture writer"))
                {
                    Console.WriteLine(failures + " FAILURES");
                    return failures;
                }
            }

            if (parallelCases.Count == 0)
            {
                Console.WriteLine("ALL PASS");
                return 0;
            }

            return RunParallel(parallelCases, jobs, logDir);
        }

        foreach (P1bCase entry in selected)
        {
            RunSerialCase(entry, null);
        }

        bool anyCheckedSerial = false;
        foreach (P1bCase entry in selected)
        {
            if (!entry.Diagnostic)
            {
                anyCheckedSerial = true;
                break;
            }
        }

        Console.WriteLine(failures == 0
            ? (anyCheckedSerial ? "ALL PASS" : "DIAGNOSTICS COMPLETE (no checks)")
            : failures + " FAILURES");
        return failures;
    }

    /// <summary>Test number from a name like "Test112b_GradientSetAnalysis" > 112.</summary>
    private static int TestNumber(string name)
    {
        if (!name.StartsWith("Test", System.StringComparison.Ordinal))
        {
            return -1;
        }

        int i = 4;
        int value = 0;
        while (i < name.Length && name[i] >= '0' && name[i] <= '9')
        {
            value = (value * 10) + (name[i] - '0');
            i++;
        }

        return i > 4 ? value : -1;
    }
}
