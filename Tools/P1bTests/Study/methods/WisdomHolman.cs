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
        if (!(a > 0d))
        {
            throw new InvalidOperationException("WH: unbound orbit");
        }

        if (dt == 0d)
        {
            r2 = r;
            v2 = v;
            return;
        }

        Vector3d h = Vector3d.Cross(r, v);
        double hMag = h.Magnitude;
        Vector3d eVec = (Vector3d.Cross(v, h) / mu) - (r / rMag);
        double eDirect = eVec.Magnitude;
        if (eDirect < 1e-10d)
        {
            double n = Math.Sqrt(mu / (rMag * rMag * rMag));
            double theta = n * dt;
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);
            r2 = (r * c) + (v * (s / n));
            v2 = (v * c) - (r * (n * s));
            return;
        }

        if (!(eDirect > 0d) || !(eDirect < 1d))
        {
            r2 = r;
            v2 = v;
            return;
        }

        Vector3d periapsis = eVec / eDirect;
        Vector3d normal = h / hMag;
        Vector3d transverse = Vector3d.Cross(normal, periapsis);
        double cosNu = Vector3d.Dot(r, periapsis) / rMag;
        double sinNu = Vector3d.Dot(r, transverse) / rMag;
        double nu = Math.Atan2(sinNu, cosNu);
        double E = 2d * Math.Atan2(
            Math.Sqrt(1d - eDirect) * Math.Sin(0.5d * nu),
            Math.Sqrt(1d + eDirect) * Math.Cos(0.5d * nu));
        double M = E - (eDirect * Math.Sin(E));
        M += Math.Sqrt(mu / (a * a * a)) * dt;
        M -= 2d * Math.PI * Math.Floor((M + Math.PI) / (2d * Math.PI));
        double ecc = KeplerMath.SolveEccentricAnomaly(M, eDirect);
        double cosE = Math.Cos(ecc);
        double sinE = Math.Sin(ecc);
        double yScale = Math.Sqrt(1d - (eDirect * eDirect));
        double radius = a * (1d - (eDirect * cosE));
        double factor = Math.Sqrt(mu * a) / radius;
        r2 = (periapsis * (a * (cosE - eDirect))) + (transverse * (a * yScale * sinE));
        v2 = (periapsis * (-factor * sinE)) + (transverse * (factor * yScale * cosE));
    }

    private static void StudyWhStep(
        StarSystem sys, int central, double mu, ref Vector3d p, ref Vector3d v,
        double t, double dt, ref long calls, double tEnd = double.NaN)
    {
        double end = double.IsNaN(tEnd) ? t + dt : tEnd;
        sys.EvaluateBodyState(sys.AllBodies[central], t, out Vector3d bc, out Vector3d bv);
        Vector3d total = sys.EvaluateShipAccelerationCached(p, t);
        calls++;
        Vector3d centralBodyAccel = sys.EvaluateShipAccelerationCached(bc, t);
        calls++;
        Vector3d pert = total
            - PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(p, bc, mu)
            - centralBodyAccel;
        v += pert * (0.5d * dt);

        sys.EvaluateBodyState(sys.AllBodies[central], end, out Vector3d bc2, out Vector3d bv2);
        StudyKeplerDrift(p - bc, v - bv, mu, dt, out Vector3d relP2, out Vector3d relV2);
        p = bc2 + relP2;
        v = bv2 + relV2;

        total = sys.EvaluateShipAccelerationCached(p, end);
        calls++;
        centralBodyAccel = sys.EvaluateShipAccelerationCached(bc2, end);
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
        double span = days * 86400d;
        long full = (long)Math.Floor(span / h);
        double rem = span - (full * h);
        var p = p0;
        var v = v0;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
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
                StudyWhStep(
                    sys, central, mu, ref p, ref v,
                    tBase + (cw[k] * hStep), dt, ref calls,
                    tBase + (cw[k + 1] * hStep));
            }
        }

        sw.Stop();
        return (p, v, calls, sw.Elapsed.TotalMilliseconds);
    }

    private static void StudyWhStepLocal(
        StarSystem sys, int central, double mu, ref Vector3d relP, ref Vector3d relV,
        double t, double dt, ref long calls, bool directForce, double tEnd = double.NaN)
    {
        double end = double.IsNaN(tEnd) ? t + dt : tEnd;
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

        sys.EvaluateBodyState(sys.AllBodies[central], end, out Vector3d bc2, out _);
        StudyKeplerDrift(relP, relV, mu, dt, out Vector3d relP2, out Vector3d relV2);
        relP = relP2;
        relV = relV2;

        direct = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
            bc2 + relP, bc2, mu);
        if (directForce)
        {
            pert = StudyLocalAccelerationDirect(sys, central, relP, end) - direct;
            calls++;
        }
        else
        {
            Vector3d total = sys.EvaluateShipAccelerationCached(bc2 + relP, end);
            Vector3d centralBodyAccel = sys.EvaluateShipAccelerationCached(bc2, end);
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
        double span = days * 86400d;
        long full = (long)Math.Floor(span / h);
        double rem = span - (full * h);
        Vector3d relP = relP0;
        Vector3d relV = relV0;
        long calls = 0;
        var sw = Stopwatch.StartNew();
        double[] ws = StudyY6Weights();
        double[] cw = StudyCumulativeWeights(ws);
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
                StudyWhStepLocal(
                    sys, central, mu, ref relP, ref relV,
                    tBase + (cw[k] * hStep), dt, ref calls, directForce,
                    tBase + (cw[k + 1] * hStep));
            }
        }

        sw.Stop();
        return (relP, relV, calls, sw.Elapsed.TotalMilliseconds);
    }
}

