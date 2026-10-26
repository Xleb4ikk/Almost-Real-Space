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
{    /// <summary>
    /// Wisdom-Holman-style step for the ship: the dominant body's Kepler motion is
    /// analytic (so a high-e perigee needs no small step), the rest is a kick.
    /// Composition with the Y6 weights makes the outer map 6th order.
    /// </summary>
    private static void StudyKeplerDrift(
        Vector3d r, Vector3d v, double mu, double dt, out Vector3d r2, out Vector3d v2)
    {
        double rMag = r.Magnitude;
        double vv = v.SqrMagnitude;
        double energy = 0.5d * vv - mu / rMag;
        double a = -mu / (2d * energy);
        if (!(a > 0d) || dt == 0d)
        {
            r2 = r;
            v2 = v;
            return;
        }

        Vector3d h = Vector3d.Cross(r, v);
        double hMag = h.Magnitude;
        double e = Math.Sqrt(Math.Max(0d, 1d - (hMag * hMag) / (mu * a)));
        if (e < 1e-8d)
        {
            double n = Math.Sqrt(mu / (rMag * rMag * rMag));
            double theta = n * dt;
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            r2 = (r * c) + (v * (s / n));
            v2 = (v * c) - (r * (n * s));
            return;
        }

        OrbitalElements el = OrbitalElements.FromState(r, v, mu);
        if (!el.IsValid || !(el.Eccentricity < 1d) || !(el.SemiMajorAxis > 0d))
        {
            r2 = r;
            v2 = v;
            return;
        }

        double ea = el.Eccentricity;
        double sma = el.SemiMajorAxis;
        double nMean = Math.Sqrt(mu / (sma * sma * sma));
        double m0 = el.MeanAnomalyDegrees * Math.PI / 180d;
        double mean = m0 + (nMean * dt);
        double ecc = KeplerMath.SolveEccentricAnomaly(mean, ea);
        double cosE = Math.Cos(ecc);
        double sinE = Math.Sin(ecc);
        double radius = sma * (1d - (ea * cosE));
        double yScale = Math.Sqrt(1d - (ea * ea));
        var orbitalP = new Vector3d(sma * (cosE - ea), sma * yScale * sinE, 0d);
        double factor = Math.Sqrt(mu * sma) / radius;
        var orbitalV = new Vector3d(-factor * sinE, factor * yScale * cosE, 0d);
        double node = el.LongitudeOfAscendingNodeDegrees * Math.PI / 180d;
        double inc = el.InclinationDegrees * Math.PI / 180d;
        double argp = el.ArgumentOfPeriapsisDegrees * Math.PI / 180d;
        r2 = KeplerMath.RotateOrbitalToWorld(orbitalP, node, inc, argp);
        v2 = KeplerMath.RotateOrbitalToWorld(orbitalV, node, inc, argp);
    }

    private static void StudyWhStep(
        StarSystem sys, int central, double mu, ref Vector3d p, ref Vector3d v,
        double t, double dt, ref long calls)
    {
        sys.EvaluateBodyState(sys.AllBodies[central], t, out Vector3d bc, out Vector3d bv);
        Vector3d total = sys.EvaluateShipAccelerationCached(p, t);
        calls++;
        Vector3d centralBodyAccel = sys.EvaluateShipAccelerationCached(bc, t);
        calls++;
        Vector3d pert = total
            - PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(p, bc, mu)
            - centralBodyAccel;
        v += pert * (0.5d * dt);

        sys.EvaluateBodyState(sys.AllBodies[central], t + dt, out Vector3d bc2, out Vector3d bv2);
        StudyKeplerDrift(p - bc, v - bv, mu, dt, out Vector3d relP2, out Vector3d relV2);
        p = bc2 + relP2;
        v = bv2 + relV2;

        total = sys.EvaluateShipAccelerationCached(p, t + dt);
        calls++;
        centralBodyAccel = sys.EvaluateShipAccelerationCached(bc2, t + dt);
        calls++;
        pert = total
            - PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(p, bc2, mu)
            - centralBodyAccel;
        v += pert * (0.5d * dt);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipWh(
        StarSystem sys, int central, Vector3d p0, Vector3d v0, double t0, double days, double h)
    {
        double mu = sys.AllBodies[central].ResolveStandardGravitationalParameter();
        double T = t0 + (days * 86400d);
        double t = t0;
        var p = p0;
        var v = v0;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        while (t < T - 1e-9d)
        {
            double hStep = Math.Min(h, T - t);
            foreach (double w in ws)
            {
                double dt = w * hStep;
                StudyWhStep(sys, central, mu, ref p, ref v, t, dt, ref calls);
                t += dt;
            }
        }

        sw.Stop();
        return (p, v, calls, sw.Elapsed.TotalMilliseconds);
    }

    private static void StudyWhStepLocal(
        StarSystem sys, int central, double mu, ref Vector3d relP, ref Vector3d relV,
        double t, double dt, ref long calls, bool directForce)
    {
        sys.EvaluateBodyState(sys.AllBodies[central], t, out Vector3d bc, out _);
        Vector3d direct = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            bc + relP, bc, mu);
        Vector3d pert;
        if (directForce)
        {
            pert = StudyLocalAccelerationDirect(sys, central, relP, t) - direct;
            calls++;
        }
        else
        {
            Vector3d total = sys.EvaluateShipAccelerationCached(bc + relP, t);
            Vector3d centralBodyAccel = sys.EvaluateShipAccelerationCached(bc, t);
            calls += 2;
            pert = total - centralBodyAccel - direct;
        }

        relV += pert * (0.5d * dt);

        sys.EvaluateBodyState(sys.AllBodies[central], t + dt, out Vector3d bc2, out _);
        StudyKeplerDrift(relP, relV, mu, dt, out Vector3d relP2, out Vector3d relV2);
        relP = relP2;
        relV = relV2;

        direct = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            bc2 + relP, bc2, mu);
        if (directForce)
        {
            pert = StudyLocalAccelerationDirect(sys, central, relP, t + dt) - direct;
            calls++;
        }
        else
        {
            Vector3d total = sys.EvaluateShipAccelerationCached(bc2 + relP, t + dt);
            Vector3d centralBodyAccel = sys.EvaluateShipAccelerationCached(bc2, t + dt);
            calls += 2;
            pert = total - centralBodyAccel - direct;
        }

        relV += pert * (0.5d * dt);
    }

    private static (Vector3d p, Vector3d v, long calls, double ms) StudyShipWhLocal(
        StarSystem sys, int central, Vector3d relP0, Vector3d relV0,
        double t0, double days, double h, bool directForce = false)
    {
        double mu = sys.AllBodies[central].ResolveStandardGravitationalParameter();
        double T = t0 + (days * 86400d);
        double t = t0;
        long step = 0L;
        Vector3d relP = relP0;
        Vector3d relV = relV0;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        while (t < T - 1e-9d)
        {
            double target = Math.Min(T, t0 + ((step + 1L) * h));
            double hStep = target - t;
            foreach (double w in ws)
            {
                double dt = w * hStep;
                StudyWhStepLocal(
                    sys, central, mu, ref relP, ref relV, t, dt, ref calls, directForce);
                t += dt;
            }

            t = target;
            step++;
        }

        sw.Stop();
        return (relP, relV, calls, sw.Elapsed.TotalMilliseconds);
    }
}

