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
{    private static void StudyStep(
        string method, double[] p, double[] v, double h, double[] mu, long steps, ref long calls)
    {
        switch (method)
        {
            case "RK4":
                StudyRk4Step(p, v, h, mu, steps, ref calls);
                break;
            case "DOPRI5":
                StudyDopri5Fixed(p, v, h, mu, steps, ref calls);
                break;
            case "PEFRL":
                StudyPefrl(p, v, h, mu, steps, ref calls);
                break;
            case "Y6":
                StudyY6(p, v, h, mu, steps, ref calls);
                break;
            case "Y6C":
                StudyY6C(p, v, h, mu, steps, ref calls);
                break;
            default:
                throw new ArgumentException("Unknown method " + method);
        }
    }

    private static double[] StudySampleP(double[] p, double[] pl, int length)
    {
        var s = new double[length];
        for (int i = 0; i < length; i++)
        {
            s[i] = p[i] + (pl == null ? 0d : pl[i]);
        }

        return s;
    }
}

