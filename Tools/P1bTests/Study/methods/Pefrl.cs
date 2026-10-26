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
{    private static void StudyPefrl(double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        const double xi = 0.1786178958448091d;
        const double lam = -0.2123418310626054d;
        const double chi = -0.06626458266981849d;
        int L = 3 * mu.Length;
        var a = new double[L];
        for (long s = 0; s < steps; s++)
        {
            for (int i = 0; i < L; i++)
            {
                p[i] += xi * h * v[i];
            }

            StudyAcc(p, a, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += (1d - (2d * lam)) * h / 2d * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += chi * h * v[i];
            }

            StudyAcc(p, a, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += lam * h * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += (1d - (2d * (chi + xi))) * h * v[i];
            }

            StudyAcc(p, a, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += lam * h * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += chi * h * v[i];
            }

            StudyAcc(p, a, mu);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += (1d - (2d * lam)) * h / 2d * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += xi * h * v[i];
            }
        }
    }
}

