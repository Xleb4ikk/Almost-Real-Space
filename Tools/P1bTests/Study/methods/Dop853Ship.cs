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
{    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipDop853(
        StarSystem sys, int central, Vector3d p0, Vector3d v0, double t0, double days,
        double posAtol = 1e-4d, double velAtol = 1e-8d, double rtol = 1e-11d)
    {
        const double maxStep = 600d;
        const double minStep = 1e-6d;
        double T = t0 + (days * 86400d);
        double t = t0;
        double dt = Math.Min(maxStep, T - t0);
        var p = p0;
        var v = v0;
        var kp = new Vector3d[12];
        var kv = new Vector3d[12];
        bool fsal = false;
        Vector3d fsalP = Vector3d.Zero;
        Vector3d fsalV = Vector3d.Zero;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        while (t < T - 1e-9d)
        {
            double h = Math.Min(dt, T - t);
            Vector3d kp0;
            Vector3d kv0;
            if (fsal)
            {
                kp0 = fsalP;
                kv0 = fsalV;
            }
            else
            {
                kp0 = v;
                kv0 = sys.EvaluateShipAccelerationCached(p, t);
                calls++;
            }

            kp[0] = kp0;
            kv[0] = kv0;
            for (int s = 1; s < 12; s++)
            {
                Vector3d dp = Vector3d.Zero;
                Vector3d dv = Vector3d.Zero;
                for (int j = 0; j < s; j++)
                {
                    double a = StudyDop853A[s][j];
                    if (a == 0d)
                    {
                        continue;
                    }

                    dp += kp[j] * a;
                    dv += kv[j] * a;
                }

                Vector3d ps = p + (dp * h);
                Vector3d vs = v + (dv * h);
                kp[s] = vs;
                kv[s] = sys.EvaluateShipAccelerationCached(ps, t + (StudyDop853C[s] * h));
                calls++;
            }

            Vector3d pn = p;
            Vector3d vn = v;
            for (int j = 0; j < 12; j++)
            {
                double b = StudyDop853B[j];
                if (b == 0d)
                {
                    continue;
                }

                pn += kp[j] * (h * b);
                vn += kv[j] * (h * b);
            }

            Vector3d aNew = sys.EvaluateShipAccelerationCached(pn, t + h);
            calls++;

            Vector3d ep = Vector3d.Zero;
            Vector3d ev = Vector3d.Zero;
            for (int j = 0; j < 12; j++)
            {
                double e = StudyDop853E5[j];
                if (e == 0d)
                {
                    continue;
                }

                ep += kp[j] * (h * e);
                ev += kv[j] * (h * e);
            }

            sys.EvaluateBodyState(sys.AllBodies[central], t, out Vector3d bc, out Vector3d bv);
            sys.EvaluateBodyState(sys.AllBodies[central], t + h, out Vector3d bc2, out Vector3d bv2);
            double floor = ToleranceReference.FloatingPointFloor;
            double scaleP = posAtol
                + (rtol * Math.Max((p - bc).Magnitude, (pn - bc2).Magnitude))
                + (floor * Math.Max(p.Magnitude, pn.Magnitude));
            double scaleV = velAtol
                + (rtol * Math.Max((v - bv).Magnitude, (vn - bv2).Magnitude))
                + (floor * Math.Max(v.Magnitude, vn.Magnitude));
            double err = Math.Max(ep.Magnitude / scaleP, ev.Magnitude / scaleV);

            if (err <= 1d)
            {
                p = pn;
                v = vn;
                t += h;
                fsalP = vn;
                fsalV = aNew;
                fsal = true;
                double factor = Math.Min(5d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-10d), 1d / 8d)));
                dt = Math.Min(maxStep, Math.Max(minStep, h * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-10d), 1d / 8d)));
                dt = Math.Max(minStep, h * factor);
                fsal = false;
            }
        }

        sw.Stop();
        return (p, v, calls, sw.Elapsed.TotalMilliseconds);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipDop853Local(
        StarSystem sys, int central, Vector3d relP0, Vector3d relV0,
        double t0, double days, double posAtol = 1e-4d, double velAtol = 1e-8d,
        double rtol = 1e-11d, bool directForce = false)
    {
        const double maxStep = 600d;
        const double minStep = 1e-6d;
        double T = t0 + (days * 86400d);
        double t = t0;
        double tLo = 0d;
        double dt = Math.Min(maxStep, T - t0);
        var p = relP0;
        var v = relV0;
        var kp = new Vector3d[12];
        var kv = new Vector3d[12];
        bool fsal = false;
        Vector3d fsalP = Vector3d.Zero;
        Vector3d fsalV = Vector3d.Zero;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        Vector3d Acc(Vector3d rp, double tt) => directForce
            ? StudyLocalAccelerationDirect(sys, central, rp, tt)
            : StudyLocalAcceleration(sys, central, rp, tt);
        while (t < T - 1e-9d)
        {
            double h = Math.Min(dt, T - t);
            Vector3d kp0;
            Vector3d kv0;
            if (fsal)
            {
                kp0 = fsalP;
                kv0 = fsalV;
            }
            else
            {
                kp0 = v;
                kv0 = Acc(p, t);
                calls++;
            }

            kp[0] = kp0;
            kv[0] = kv0;
            for (int s = 1; s < 12; s++)
            {
                Vector3d dp = Vector3d.Zero;
                Vector3d dv = Vector3d.Zero;
                for (int j = 0; j < s; j++)
                {
                    double a = StudyDop853A[s][j];
                    if (a == 0d)
                    {
                        continue;
                    }

                    dp += kp[j] * a;
                    dv += kv[j] * a;
                }

                Vector3d ps = p + (dp * h);
                Vector3d vs = v + (dv * h);
                kp[s] = vs;
                kv[s] = Acc(ps, t + (StudyDop853C[s] * h));
                calls++;
            }

            Vector3d pn = p;
            Vector3d vn = v;
            for (int j = 0; j < 12; j++)
            {
                double b = StudyDop853B[j];
                if (b == 0d)
                {
                    continue;
                }

                pn += kp[j] * (h * b);
                vn += kv[j] * (h * b);
            }

            Vector3d aNew = Acc(pn, t + h);
            calls++;

            Vector3d ep = Vector3d.Zero;
            Vector3d ev = Vector3d.Zero;
            for (int j = 0; j < 12; j++)
            {
                double e = StudyDop853E5[j];
                if (e == 0d)
                {
                    continue;
                }

                ep += kp[j] * (h * e);
                ev += kv[j] * (h * e);
            }

            double floor = ToleranceReference.FloatingPointFloor;
            double scaleP = posAtol
                + (rtol * Math.Max(p.Magnitude, pn.Magnitude))
                + (floor * Math.Max(p.Magnitude, pn.Magnitude));
            double scaleV = velAtol
                + (rtol * Math.Max(v.Magnitude, vn.Magnitude))
                + (floor * Math.Max(v.Magnitude, vn.Magnitude));
            double err = Math.Max(ep.Magnitude / scaleP, ev.Magnitude / scaleV);
            if (!double.IsFinite(err))
            {
                err = 1e3d;
            }

            if (err <= 1d)
            {
                p = pn;
                v = vn;
                StudyAdd(ref t, ref tLo, h);
                fsalP = vn;
                fsalV = aNew;
                fsal = true;
                double factor = Math.Min(5d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-10d), 1d / 8d)));
                dt = Math.Min(maxStep, Math.Max(minStep, h * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-10d), 1d / 8d)));
                dt = Math.Max(minStep, h * factor);
                fsal = false;
            }
        }

        sw.Stop();
        return (p, v, calls, sw.Elapsed.TotalMilliseconds);
    }
}

