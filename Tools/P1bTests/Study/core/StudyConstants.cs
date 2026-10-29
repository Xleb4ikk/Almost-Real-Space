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
{    private const double StudyYear = 365.25d * 86400d;
    private const double StudyAU = 1.495978707e11d;
    // The report's ship runs start two days after the bake epoch: the Y6 composition
    // contains negative sub-step weights, so the force must never see t < 0.
    private const double StudyShipT0 = 172800d;

    private static int StudyOuterIndex(StudyNBody nb)
    {
        int best = 0;
        double bestDistance = -1d;
        for (int i = 1; i < nb.Count; i++)
        {
            double dx = nb.P0[3 * i] - nb.P0[0];
            double dy = nb.P0[(3 * i) + 1] - nb.P0[1];
            double dz = nb.P0[(3 * i) + 2] - nb.P0[2];
            double distance = (dx * dx) + (dy * dy) + (dz * dz);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private sealed class StudyNBody
    {
        public string[] Names;
        public double[] Mu;
        public double[] P0;
        public double[] V0;

        public int Count => Names.Length;
    }

    private sealed class StudyRefResult
    {
        public double[] P;
        public double[] V;
        public List<StudySample> Samples;
        public long Calls;
        public double Ms;
    }

    private readonly struct StudySample
    {
        public readonly double T;
        public readonly double[] P;
        public readonly double[] V;

        public StudySample(double t, double[] p)
        {
            T = t;
            P = p;
            V = null;
        }

        public StudySample(double t, double[] p, double[] v)
        {
            T = t;
            P = p;
            V = v;
        }
    }

}

