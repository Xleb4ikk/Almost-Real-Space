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
{    private static readonly double StudyY1 = 0.78451361047755726382d;
    private static readonly double StudyY2 = 0.23557321335935813368d;
    private static readonly double StudyY3 = -1.17767998417887100695d;

    private static double[] StudyY6Weights()
    {
        double w0 = 1d - 2d * (StudyY1 + StudyY2 + StudyY3);
        return new[] { StudyY1, StudyY2, StudyY3, w0, StudyY3, StudyY2, StudyY1 };
    }
    private static void StudyY6(double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        int L = 3 * mu.Length;
        var a = new double[L];
        double[] ws = StudyY6Weights();
        StudyAcc(p, a, mu);
        calls++;
        for (long s = 0; s < steps; s++)
        {
            foreach (double w in ws)
            {
                double dt = w * h;
                for (int i = 0; i < L; i++)
                {
                    v[i] += 0.5d * dt * a[i];
                }

                for (int i = 0; i < L; i++)
                {
                    p[i] += dt * v[i];
                }

                StudyAcc(p, a, mu);
                calls++;
                for (int i = 0; i < L; i++)
                {
                    v[i] += 0.5d * dt * a[i];
                }
            }
        }
    }

    private static void StudyY6C(
        double[] p, double[] v, double h, double[] mu, long steps, ref long calls,
        List<StudySample> samples = null, double sampleEvery = 0d, double t0 = 0d)
    {
        int L = 3 * mu.Length;
        var a = new double[L];
        var pl = new double[L];
        var vl = new double[L];
        double[] ws = StudyY6Weights();
        double t = t0;
        double nextSample = samples != null && sampleEvery > 0d ? t0 + sampleEvery : double.PositiveInfinity;
        StudyAccC(p, pl, a, mu);
        calls++;
        for (long s = 0; s < steps; s++)
        {
            foreach (double w in ws)
            {
                double dt = w * h;
                for (int i = 0; i < L; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * a[i]);
                }

                for (int i = 0; i < L; i++)
                {
                    StudyAdd(ref p[i], ref pl[i], dt * (v[i] + vl[i]));
                }

                StudyAccC(p, pl, a, mu);
                calls++;
                for (int i = 0; i < L; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * a[i]);
                }
            }

            t += h;
            if (samples != null && sampleEvery > 0d && t >= nextSample - 1e-9d)
            {
                samples.Add(new StudySample(t, StudySampleP(p, pl, L), StudySampleP(v, vl, L)));
                nextSample += sampleEvery;
            }
        }

        if (samples != null && sampleEvery > 0d
            && (samples.Count == 0 || samples[samples.Count - 1].T < t - 1e-9d))
        {
            samples.Add(new StudySample(t, StudySampleP(p, pl, L), StudySampleP(v, vl, L)));
        }

        for (int i = 0; i < L; i++)
        {
            p[i] += pl[i];
            v[i] += vl[i];
        }
    }
}

