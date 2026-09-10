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

            if (args[i] == "--filter" && i + 1 < args.Length)
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
        }

        foreach (P1bCase entry in TestRegistry())
        {
            if (filter != null && entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
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

            entry.Run();
        }

        Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURES");
        return failures;
    }
}
