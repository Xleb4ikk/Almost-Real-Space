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
{    private static double[] StudyCumulativeWeights(double[] ws)
    {
        var cw = new double[ws.Length + 1];
        for (int i = 0; i < ws.Length; i++)
        {
            cw[i + 1] = cw[i] + ws[i];
        }

        cw[ws.Length] = 1d;
        return cw;
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipY6CDd(
        StarSystem sys, Vector3d p0, Vector3d v0, double t0, double days, double h,
        bool[] bodyMask = null, bool frozenTime = false, double frozenAt = 0d)
    {
        double span = days * 86400d;
        long full = (long)Math.Floor(span / h);
        double rem = span - (full * h);
        StudyDd3 p = StudyDd3.From(p0);
        StudyDd3 v = StudyDd3.From(v0);
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        long calls = 0;
        var sw = Stopwatch.StartNew();
        StudyDd3 a = StudyForceDd(sys, p, t0, bodyMask, frozenTime, frozenAt);
        calls++;
        for (long s = 0; s <= full; s++)
        {
            double hStep = s < full ? h : rem;
            if (hStep <= 1e-9d)
            {
                break;
            }

            double tBase = t0 + (s * h);
            for (int i = 0; i < ws.Length; i++)
            {
                double dt = ws[i] * hStep;
                v += a * (0.5d * dt);
                p += v * dt;
                a = StudyForceDd(
                    sys, p, tBase + (cw[i + 1] * hStep), bodyMask, frozenTime, frozenAt);
                calls++;
                v += a * (0.5d * dt);
            }
        }

        sw.Stop();
        return (p.ToVector3d(), v.ToVector3d(), calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v) StudyShipRefA(
        StarSystem sys, Vector3d p0, Vector3d v0, double t0, double days, double h)
    {
        double span = days * 86400d;
        long full = (long)Math.Floor(span / h);
        double rem = span - (full * h);
        var p = new[] { p0.X, p0.Y, p0.Z };
        var v = new[] { v0.X, v0.Y, v0.Z };
        var pl = new double[3];
        var vl = new double[3];
        long calls = 0;
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        Vector3d acc = sys.EvaluateShipAccelerationCached(
            new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]), t0);
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
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z)));
                }

                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref p[i], ref pl[i], dt * (v[i] + vl[i]));
                }

                acc = sys.EvaluateShipAccelerationCached(
                    new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]),
                    tBase + (cw[k + 1] * hStep));
                calls++;
                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z)));
                }
            }
        }

        return (new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]),
            new Vector3d(v[0] + vl[0], v[1] + vl[1], v[2] + vl[2]));
    }

    /// <summary>
    /// Y6C reference in the central body's local frame. The local state is small
    /// (planet radius, not solar-system radius), so the integration does not pay the
    /// absolute-coordinate cancellation that dominates the world-frame ship test.
    /// </summary>
    private static (Vector3d p, Vector3d v) StudyShipRefLocal(
        StarSystem sys, int central, Vector3d relP0, Vector3d relV0,
        double t0, double days, double h)
    {
        double span = days * 86400d;
        long full = (long)Math.Floor(span / h);
        double rem = span - (full * h);
        var p = new[] { relP0.X, relP0.Y, relP0.Z };
        var v = new[] { relV0.X, relV0.Y, relV0.Z };
        var pl = new double[3];
        var vl = new double[3];
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
        Vector3d acc = StudyLocalAcceleration(
            sys, central, new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]), t0);
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
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z)));
                }

                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref p[i], ref pl[i], dt * (v[i] + vl[i]));
                }

                acc = StudyLocalAcceleration(
                    sys, central, new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]),
                    tBase + (cw[k + 1] * hStep));
                for (int i = 0; i < 3; i++)
                {
                    StudyAdd(ref v[i], ref vl[i], 0.5d * dt * (i == 0 ? acc.X : (i == 1 ? acc.Y : acc.Z)));
                }
            }
        }

        return (new Vector3d(p[0] + pl[0], p[1] + pl[1], p[2] + pl[2]),
            new Vector3d(v[0] + vl[0], v[1] + vl[1], v[2] + vl[2]));
    }
}

