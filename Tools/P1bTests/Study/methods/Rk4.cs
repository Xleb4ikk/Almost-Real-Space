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
{    private static void StudyRk4Step(double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        int n = mu.Length;
        int L = 3 * n;
        var a1 = new double[L];
        var a2 = new double[L];
        var a3 = new double[L];
        var a4 = new double[L];
        var tp = new double[L];
        var v2 = new double[L];
        var v3 = new double[L];
        var v4 = new double[L];
        for (long s = 0; s < steps; s++)
        {
            StudyAcc(p, a1, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                tp[i] = p[i] + (0.5d * h * v[i]);
                v2[i] = v[i] + (0.5d * h * a1[i]);
            }

            StudyAcc(tp, a2, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                tp[i] = p[i] + (0.5d * h * v2[i]);
                v3[i] = v[i] + (0.5d * h * a2[i]);
            }

            StudyAcc(tp, a3, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                tp[i] = p[i] + (h * v3[i]);
                v4[i] = v[i] + (h * a3[i]);
            }

            StudyAcc(tp, a4, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                p[i] += h / 6d * (v[i] + (2d * v2[i]) + (2d * v3[i]) + v4[i]);
                v[i] += h / 6d * (a1[i] + (2d * a2[i]) + (2d * a3[i]) + a4[i]);
            }
        }
    }
}

