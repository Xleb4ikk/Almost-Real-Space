using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{    private static StudyRefResult StudyRefRun(
        StudyNBody nb, string method, double h, double years, double sampleEvery)
    {
        int n = nb.Count;
        int L = 3 * n;
        var p = (double[])nb.P0.Clone();
        var v = (double[])nb.V0.Clone();
        double T = years * StudyYear;
        long fullSteps = (long)Math.Floor(T / h);
        double remainder = T - (fullSteps * h);
        var samples = sampleEvery > 0d ? new List<StudySample>() : null;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        if (fullSteps > 0)
        {
            if (method == "Y6C")
            {
                StudyY6C(p, v, h, nb.Mu, fullSteps, ref calls, samples, sampleEvery, 0d);
            }
            else
            {
                StudyStep(method, p, v, h, nb.Mu, fullSteps, ref calls);
            }
        }

        if (remainder > 1e-9d)
        {
            if (method == "Y6C")
            {
                StudyY6C(p, v, remainder, nb.Mu, 1, ref calls, samples, sampleEvery, fullSteps * h);
            }
            else
            {
                StudyStep(method, p, v, remainder, nb.Mu, 1, ref calls);
            }
        }

        sw.Stop();
        return new StudyRefResult { P = p, V = v, Samples = samples, Calls = calls, Ms = sw.Elapsed.TotalMilliseconds };
    }

    private static double StudyRel(double[] p, int i, int j, double[] r)
    {
        double dx = (p[3 * i] - p[3 * j]) - (r[3 * i] - r[3 * j]);
        double dy = (p[(3 * i) + 1] - p[(3 * j) + 1]) - (r[(3 * i) + 1] - r[(3 * j) + 1]);
        double dz = (p[(3 * i) + 2] - p[(3 * j) + 2]) - (r[(3 * i) + 2] - r[(3 * j) + 2]);
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static double StudyEnergy(double[] p, double[] v, double[] mu)
    {
        int n = mu.Length;
        double t = 0d;
        double u = 0d;
        for (int i = 0; i < n; i++)
        {
            t += 0.5d * mu[i] * ((v[3 * i] * v[3 * i]) + (v[(3 * i) + 1] * v[(3 * i) + 1]) + (v[(3 * i) + 2] * v[(3 * i) + 2]));
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = p[3 * i] - p[3 * j];
                double dy = p[(3 * i) + 1] - p[(3 * j) + 1];
                double dz = p[(3 * i) + 2] - p[(3 * j) + 2];
                u -= mu[i] * mu[j] / Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }
        }

        return t + u;
    }
}

