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
{    private static void StudyDopri5Fixed(double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        int n = mu.Length;
        int L = 3 * n;
        double[][] A =
        {
            new[] { 1d / 5d },
            new[] { 3d / 40d, 9d / 40d },
            new[] { 44d / 45d, -56d / 15d, 32d / 9d },
            new[] { 19372d / 6561d, -25360d / 2187d, 64448d / 6561d, -212d / 729d },
            new[] { 9017d / 3168d, -355d / 33d, 46732d / 5247d, 49d / 176d, -5103d / 18656d }
        };
        double[] b = { 35d / 384d, 0d, 500d / 1113d, 125d / 192d, -2187d / 6784d, 11d / 84d };
        var kp = new double[6][];
        var kv = new double[6][];
        for (int s = 0; s < 6; s++)
        {
            kp[s] = new double[L];
            kv[s] = new double[L];
        }

        var tp = new double[L];
        var tv = new double[L];
        Array.Copy(v, kp[0], L);
        StudyAcc(p, kv[0], mu);
        calls++;
        for (long st = 0; st < steps; st++)
        {
            for (int s = 1; s < 6; s++)
            {
                for (int i = 0; i < L; i++)
                {
                    double sp = 0d;
                    double sv = 0d;
                    for (int j = 0; j < s; j++)
                    {
                        sp += A[s - 1][j] * kp[j][i];
                        sv += A[s - 1][j] * kv[j][i];
                    }

                    tp[i] = p[i] + (h * sp);
                    tv[i] = v[i] + (h * sv);
                }

                Array.Copy(tv, kp[s], L);
                StudyAcc(tp, kv[s], mu);
                calls++;
            }

            for (int i = 0; i < L; i++)
            {
                double sp = 0d;
                double sv = 0d;
                for (int j = 0; j < 6; j++)
                {
                    sp += b[j] * kp[j][i];
                    sv += b[j] * kv[j][i];
                }

                p[i] += h * sp;
                v[i] += h * sv;
            }

            Array.Copy(v, kp[0], L);
            StudyAcc(p, kv[0], mu);
            calls++;
        }
    }
}

