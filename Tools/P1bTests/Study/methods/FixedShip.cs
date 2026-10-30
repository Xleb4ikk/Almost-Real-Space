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
{    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipFixed(
        StarSystem sys, Vector3d p0, Vector3d v0, double t0, double days, double h, string method)
    {
        double span = days * 86400d;
        long full = (long)Math.Floor(span / h);
        double rem = span - (full * h);
        var p = new[] { p0.X, p0.Y, p0.Z };
        var v = new[] { v0.X, v0.Y, v0.Z };
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        if (method == "Y6")
        {
            Vector3d acc = sys.EvaluateShipAccelerationCached(
                new Vector3d(p[0], p[1], p[2]), t0);
            calls++;
            for (long s = 0; s <= full; s++)
            {
                double hStep = s < full ? h : rem;
                if (hStep <= 1e-9d)
                {
                    break;
                }

                double tBase = t0 + (s * h);
                for (int k = 0; k < ws.Length; k++)
                {
                    double dt = ws[k] * hStep;
                    for (int i = 0; i < 3; i++)
                    {
                        v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        p[i] += dt * v[i];
                    }

                    acc = sys.EvaluateShipAccelerationCached(
                        new Vector3d(p[0], p[1], p[2]), tBase + (cw[k + 1] * hStep));
                    calls++;
                    for (int i = 0; i < 3; i++)
                    {
                        v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                    }
                }
            }
        }
        else if (method == "PEFRL")
        {
            const double xi = 0.1786178958448091d;
            const double lam = -0.2123418310626054d;
            const double chi = -0.06626458266981849d;
            for (long s = 0; s <= full; s++)
            {
                double hStep = s < full ? h : rem;
                if (hStep <= 1e-9d)
                {
                    break;
                }

                double tBase = t0 + (s * h);
                for (int i = 0; i < 3; i++)
                {
                    p[i] += xi * hStep * v[i];
                }

                Vector3d acc = sys.EvaluateShipAccelerationCached(
                    new Vector3d(p[0], p[1], p[2]), tBase + (xi * hStep));
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += (1d - (2d * lam)) * hStep / 2d * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += chi * hStep * v[i];
                }

                acc = sys.EvaluateShipAccelerationCached(
                    new Vector3d(p[0], p[1], p[2]), tBase + ((xi + chi) * hStep));
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += lam * hStep * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += (1d - (2d * (chi + xi))) * hStep * v[i];
                }

                acc = sys.EvaluateShipAccelerationCached(
                    new Vector3d(p[0], p[1], p[2]), tBase + ((1d - chi - xi) * hStep));
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += lam * hStep * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += chi * hStep * v[i];
                }

                acc = sys.EvaluateShipAccelerationCached(
                    new Vector3d(p[0], p[1], p[2]), tBase + ((1d - xi) * hStep));
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += (1d - (2d * lam)) * hStep / 2d * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += xi * hStep * v[i];
                }
            }
        }
        else
        {
            throw new ArgumentException("Unknown ship method " + method);
        }

        sw.Stop();
        return (new Vector3d(p[0], p[1], p[2]), new Vector3d(v[0], v[1], v[2]),
            calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipFixedLocal(
        StarSystem sys, int central, Vector3d relP0, Vector3d relV0,
        double t0, double days, double h, string method, bool directForce = false)
    {
        double span = days * 86400d;
        long full = (long)Math.Floor(span / h);
        double rem = span - (full * h);
        var p = new[] { relP0.X, relP0.Y, relP0.Z };
        var v = new[] { relV0.X, relV0.Y, relV0.Z };
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        Vector3d Acc(Vector3d rp, double tt) => directForce
            ? StudyLocalAccelerationDirect(sys, central, rp, tt)
            : StudyLocalAcceleration(sys, central, rp, tt);
        if (method == "Y6")
        {
            Vector3d acc = Acc(new Vector3d(p[0], p[1], p[2]), t0);
            calls++;
            for (long s = 0; s <= full; s++)
            {
                double hStep = s < full ? h : rem;
                if (hStep <= 1e-9d)
                {
                    break;
                }

                double tBase = t0 + (s * h);
                for (int k = 0; k < ws.Length; k++)
                {
                    double dt = ws[k] * hStep;
                    for (int i = 0; i < 3; i++)
                    {
                        v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        p[i] += dt * v[i];
                    }

                    acc = Acc(new Vector3d(p[0], p[1], p[2]), tBase + (cw[k + 1] * hStep));
                    calls++;
                    for (int i = 0; i < 3; i++)
                    {
                        v[i] += 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                    }
                }
            }
        }
        else if (method == "PEFRL")
        {
            const double xi = 0.1786178958448091d;
            const double lam = -0.2123418310626054d;
            const double chi = -0.06626458266981849d;
            for (long s = 0; s <= full; s++)
            {
                double hStep = s < full ? h : rem;
                if (hStep <= 1e-9d)
                {
                    break;
                }

                double tBase = t0 + (s * h);
                for (int i = 0; i < 3; i++)
                {
                    p[i] += xi * hStep * v[i];
                }

                Vector3d acc = Acc(new Vector3d(p[0], p[1], p[2]), tBase + (xi * hStep));
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += (1d - (2d * lam)) * hStep / 2d * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += chi * hStep * v[i];
                }

                acc = Acc(new Vector3d(p[0], p[1], p[2]), tBase + ((xi + chi) * hStep));
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += lam * hStep * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += (1d - (2d * (chi + xi))) * hStep * v[i];
                }

                acc = Acc(new Vector3d(p[0], p[1], p[2]), tBase + ((1d - chi - xi) * hStep));
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += lam * hStep * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += chi * hStep * v[i];
                }

                acc = Acc(new Vector3d(p[0], p[1], p[2]), tBase + ((1d - xi) * hStep));
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    v[i] += (1d - (2d * lam)) * hStep / 2d * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z));
                }

                for (int i = 0; i < 3; i++)
                {
                    p[i] += xi * hStep * v[i];
                }
            }
        }
        else
        {
            throw new ArgumentException("Unknown ship method " + method);
        }

        sw.Stop();
        return (new Vector3d(p[0], p[1], p[2]), new Vector3d(v[0], v[1], v[2]),
            calls, sw.Elapsed.TotalMilliseconds);
    }
}

