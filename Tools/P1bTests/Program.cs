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
    private static int failures;

    private static void Check(bool cond, string name, string detail)
    {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + name + " | " + detail);
        if (!cond)
        {
            Interlocked.Increment(ref failures);
        }
    }

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
            Console.WriteLine("ALL PASS");
            return 0;
        }

        foreach (string name in failed)
        {
            Console.WriteLine("FAILED " + name);
        }

        Console.WriteLine(failed.Count + " FAILED CASE(S)");
        return failed.Count;
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
        int from = int.MinValue;
        int to = int.MaxValue;
        int jobs = 1;
        int repeat = 1;
        int benchJobs = 0;
        string exact = null;
        string traceDir = null;
        string logDir = null;
        string logFile = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--list")
            {
                foreach (P1bCase entry in TestRegistry())
                {
                    Console.WriteLine((entry.Slow ? "slow " : "fast ") + entry.Name);
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

        if (logFile != null)
        {
            string fullLog = Path.GetFullPath(logFile);
            Directory.CreateDirectory(Path.GetDirectoryName(fullLog));
            var writer = new StreamWriter(fullLog, false, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(writer);
            Console.SetError(writer);
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
            probe.Run();
            singleWatch.Stop();
            if (failures != before)
            {
                Console.WriteLine("probe failed; benchmark aborted");
                return failures - before;
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
            // test names are numbered (Test9, Test90�Test99), so "Test9" would
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
            return RunParallel(selected, jobs, logDir);
        }

        foreach (P1bCase entry in selected)
        {
            var caseWatch = Stopwatch.StartNew();
            int before = failures;
            entry.Run();
            Console.WriteLine("[" + entry.Name + "] " + (failures == before ? "ok" : "FAIL") + " in " + caseWatch.ElapsedMilliseconds + " ms");
        }

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURES");
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
