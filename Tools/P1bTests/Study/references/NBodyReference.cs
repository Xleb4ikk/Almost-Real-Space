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
{    // ------------------------------------------------------------------
    // Independent n-body reference (same method family as the report)
    // ------------------------------------------------------------------

    private static void StudyAcc(double[] p, double[] a, double[] mu)
    {
        int n = mu.Length;
        for (int i = 0; i < 3 * n; i++)
        {
            a[i] = 0d;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = p[3 * j] - p[3 * i];
                double dy = p[(3 * j) + 1] - p[(3 * i) + 1];
                double dz = p[(3 * j) + 2] - p[(3 * i) + 2];
                double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                double inv = 1d / (r2 * Math.Sqrt(r2));
                double fi = mu[j] * inv;
                double fj = mu[i] * inv;
                a[3 * i] += fi * dx;
                a[(3 * i) + 1] += fi * dy;
                a[(3 * i) + 2] += fi * dz;
                a[3 * j] -= fj * dx;
                a[(3 * j) + 1] -= fj * dy;
                a[(3 * j) + 2] -= fj * dz;
            }
        }
    }

    private static void StudyAccC(double[] p, double[] pl, double[] a, double[] mu)
    {
        int n = mu.Length;
        for (int i = 0; i < 3 * n; i++)
        {
            a[i] = 0d;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = (p[3 * j] - p[3 * i]) + (pl[3 * j] - pl[3 * i]);
                double dy = (p[(3 * j) + 1] - p[(3 * i) + 1]) + (pl[(3 * j) + 1] - pl[(3 * i) + 1]);
                double dz = (p[(3 * j) + 2] - p[(3 * i) + 2]) + (pl[(3 * j) + 2] - pl[(3 * i) + 2]);
                double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                double inv = 1d / (r2 * Math.Sqrt(r2));
                double fi = mu[j] * inv;
                double fj = mu[i] * inv;
                a[3 * i] += fi * dx;
                a[(3 * i) + 1] += fi * dy;
                a[(3 * i) + 2] += fi * dz;
                a[3 * j] -= fj * dx;
                a[(3 * j) + 1] -= fj * dy;
                a[(3 * j) + 2] -= fj * dz;
            }
        }
    }

    private static void StudyAdd(ref double hi, ref double lo, double inc)
    {
        double t = hi + inc;
        if (Math.Abs(hi) >= Math.Abs(inc))
        {
            lo += (hi - t) + inc;
        }
        else
        {
            lo += (inc - t) + hi;
        }

        hi = t;
    }

}

