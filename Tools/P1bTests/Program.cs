using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            failures++;
        }
    }

    public static int Main(string[] args)
    {
        Console.WriteLine("P1b tests...");
        string filter = null;
        bool fastOnly = false;
        bool slowOnly = false;
        int from = int.MinValue;
        int to = int.MaxValue;
        string traceDir = null;
        string traceLegacyDir = null;
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
            else if (args[i] == "--fast")
            {
                fastOnly = true;
            }
            else if (args[i] == "--slow")
            {
                slowOnly = true;
            }
            else if (args[i] == "--trace" && i + 1 < args.Length)
            {
                traceDir = args[i + 1];
                i++;
            }
            else if (args[i] == "--trace-legacy" && i + 1 < args.Length)
            {
                traceLegacyDir = args[i + 1];
                i++;
            }
        }

        if (traceLegacyDir != null)
        {
            return RunLandingTraces(traceLegacyDir, legacy: true);
        }

        if (traceDir != null)
        {
            return RunLandingTraces(traceDir, legacy: false);
        }

        foreach (P1bCase entry in TestRegistry())
        {
            if (filter != null && entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            // --from/--to нужны потому, что --filter ищет подстроку, а имена
            // тестов нумеруются (Test9, Test90…Test99), и подстрока "Test9"
            // накрыла бы сразу десять разных номеров. Разбиение на группы для
            // поочерёдного прогона без одновременной нагрузки на CPU иначе
            // не выразить.
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

            entry.Run();
        }

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURES");
        return failures;
    }

    /// <summary>Номер теста из имени вида "Test112b_GradientSetAnalysis" → 112.</summary>
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
