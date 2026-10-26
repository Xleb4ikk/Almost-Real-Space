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
{    // ------------------------------------------------------------------
    // Test cases
    // ------------------------------------------------------------------

    private static void StudyReferenceRow(
        string method, Vector3d rel, Vector3d refARel, Vector3d refBRel,
        long calls, double ms, long allocBytes)
    {
        double us = calls > 0L ? ms * 1000d / calls : 0d;
        Console.WriteLine(
            "{0,-18} {1,12:E3} {2,12:E3} {3,10:N0} {4,8:F0} {5,7:F2} {6,9:N0}",
            method, (rel - refARel).Magnitude, (rel - refBRel).Magnitude,
            calls, ms, us, allocBytes / 1024d);
    }

    private static void StudyReferenceCase(
        string label, StarSystem sys, StudyNBody nb, int central, double rp, double ra, double days, bool dd)
    {
        Console.WriteLine();
        Console.WriteLine("=== Reference convergence: " + label + " ===");
        OrbitingBody body = sys.AllBodies[central];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        double T = t0 + (days * 86400d);
        sys.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(rp, ra, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;
        sys.EvaluateBodyState(body, T, out Vector3d bpT, out _);

        var r5 = StudyShipRefA(sys, p0, v0, t0, days, 5d);
        var r25 = StudyShipRefA(sys, p0, v0, t0, days, 2.5d);
        var r125 = StudyShipRefA(sys, p0, v0, t0, days, 1.25d);
        var r0625 = StudyShipRefA(sys, p0, v0, t0, days, 0.625d);
        double d1 = (r5.p - r25.p).Magnitude;
        double d2 = (r25.p - r125.p).Magnitude;
        double d3 = (r125.p - r0625.p).Magnitude;
        Console.WriteLine("Y6C 5->2.5: {0:E3} m; 2.5->1.25: {1:E3}; 1.25->0.625: {2:E3}; ratios {3:F1}/{4:F1}",
            d1, d2, d3, d1 / Math.Max(d2, 1e-30), d2 / Math.Max(d3, 1e-30));
        Console.WriteLine("reference error estimate (0.625 s): {0:E3} m", d3 / 63d);

        Vector3d refARel = r0625.p - bpT;

        var bodiesAtT0 = StudyRefRun(nb, "Y6C", 5d, t0 / StudyYear, 0d);
        var shipNb = new StudyNBody
        {
            Names = new string[nb.Count + 1],
            Mu = new double[nb.Count + 1],
            P0 = new double[3 * (nb.Count + 1)],
            V0 = new double[3 * (nb.Count + 1)]
        };
        Array.Copy(bodiesAtT0.P, shipNb.P0, 3 * nb.Count);
        Array.Copy(bodiesAtT0.V, shipNb.V0, 3 * nb.Count);
        Array.Copy(nb.Mu, shipNb.Mu, nb.Count);
        Array.Copy(nb.Names, shipNb.Names, nb.Count);
        shipNb.Names[nb.Count] = "Ship";
        shipNb.Mu[nb.Count] = 0d;
        shipNb.P0[3 * nb.Count] = bodiesAtT0.P[3 * central] + relP.X;
        shipNb.P0[(3 * nb.Count) + 1] = bodiesAtT0.P[(3 * central) + 1] + relP.Y;
        shipNb.P0[(3 * nb.Count) + 2] = bodiesAtT0.P[(3 * central) + 2] + relP.Z;
        shipNb.V0[3 * nb.Count] = bodiesAtT0.V[3 * central] + relV.X;
        shipNb.V0[(3 * nb.Count) + 1] = bodiesAtT0.V[(3 * central) + 1] + relV.Y;
        shipNb.V0[(3 * nb.Count) + 2] = bodiesAtT0.V[(3 * central) + 2] + relV.Z;
        var refB5 = StudyRefRun(shipNb, "Y6C", 5d, days / 365.25d, 0d);
        var refB25 = StudyRefRun(shipNb, "Y6C", 2.5d, days / 365.25d, 0d);
        int ship = nb.Count;
        Vector3d refBRel = new Vector3d(
            refB5.P[3 * ship] - refB5.P[3 * central],
            refB5.P[(3 * ship) + 1] - refB5.P[(3 * central) + 1],
            refB5.P[(3 * ship) + 2] - refB5.P[(3 * central) + 2]);
        Vector3d refBRel25 = new Vector3d(
            refB25.P[3 * ship] - refB25.P[3 * central],
            refB25.P[(3 * ship) + 1] - refB25.P[(3 * central) + 1],
            refB25.P[(3 * ship) + 2] - refB25.P[(3 * central) + 2]);
        Console.WriteLine("A vs B: {0:E3} m; B self 5 vs 2.5: {1:E3} m",
            (refARel - refBRel).Magnitude, (refBRel - refBRel25).Magnitude);

        Console.WriteLine("{0,-18} {1,12} {2,12} {3,10} {4,8} {5,7} {6,9}",
            "method", "err A,m", "err B,m", "calls", "ms", "us/call", "KiB");

        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var game = StudyShipGame(sys, p0, v0, t0, days);
        long gameAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("DOPRI5 (game)", game.p - bpT, refARel, refBRel, game.calls, game.ms, gameAlloc);

        a0 = GC.GetAllocatedBytesForCurrentThread();
        var dop = StudyShipDop853(sys, central, p0, v0, t0, days);
        long dopAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("DOP853", dop.p - bpT, refARel, refBRel, dop.calls, dop.ms, dopAlloc);

        a0 = GC.GetAllocatedBytesForCurrentThread();
        var dopTight = StudyShipDop853(sys, central, p0, v0, t0, days, 1e-12d, 1e-15d, 1e-14d);
        long tightAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("DOP853 tight", dopTight.p - bpT, refARel, refBRel, dopTight.calls, dopTight.ms, tightAlloc);

        a0 = GC.GetAllocatedBytesForCurrentThread();
        var y6 = StudyShipFixed(sys, p0, v0, t0, days, 30d, "Y6");
        long y6Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("Y6 30 s", y6.p - bpT, refARel, refBRel, y6.calls, y6.ms, y6Alloc);

        a0 = GC.GetAllocatedBytesForCurrentThread();
        var wh = StudyShipWh(sys, central, p0, v0, t0, days, 480d);
        long whAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        StudyReferenceRow("WH 480 s", wh.p - bpT, refARel, refBRel, wh.calls, wh.ms, whAlloc);

        if (dd)
        {
            a0 = GC.GetAllocatedBytesForCurrentThread();
            var ddRun = StudyShipY6CDd(sys, p0, v0, t0, days, 5d);
            long ddAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            StudyReferenceRow("Y6C double-double", ddRun.p - bpT, refARel, refBRel, ddRun.calls, ddRun.ms, ddAlloc);
        }

        Console.WriteLine("ref B 5 s: calls={0:N0}, {1:F0} ms", refB5.Calls, refB5.Ms);
    }

    private static void StudyKeplerReference()
    {
        Console.WriteLine();
        Console.WriteLine("=== Exact Kepler one-body reference (30 d) ===");
        double mu = 3.986004418e14d;
        double r = 6.771e6d;
        double v = Math.Sqrt(mu / r);
        var p0 = new Vector3d(r, 0d, 0d);
        var v0 = new Vector3d(0d, v, 0d);
        var star = new OrbitingBody { Name = "TestBody", StandardGravitationalParameter = mu, Radius = 1d };
        StarSystem one = new StarSystem(star);
        StudyKeplerDrift(p0, v0, mu, 30d * 86400d, out Vector3d re, out Vector3d ve);

        var y6c = StudyShipRefA(one, p0, v0, 0d, 30d, 5d);
        var dop = StudyShipDop853(one, 0, p0, v0, 0d, 30d, 1e-12d, 1e-15d, 1e-14d);
        var dd = StudyShipY6CDd(one, p0, v0, 0d, 30d, 5d);
        var y6 = StudyShipFixed(one, p0, v0, 0d, 30d, 30d, "Y6");
        Console.WriteLine("exact vs Y6C 5 s:   dp={0:E3} m, dv={1:E3} m/s",
            (y6c.p - re).Magnitude, (y6c.v - ve).Magnitude);
        Console.WriteLine("exact vs DOP853 tight: dp={0:E3} m, dv={1:E3} m/s",
            (dop.p - re).Magnitude, (dop.v - ve).Magnitude);
        Console.WriteLine("exact vs Y6C double-double: dp={0:E3} m, dv={1:E3} m/s",
            (dd.p - re).Magnitude, (dd.v - ve).Magnitude);
        Console.WriteLine("exact vs Y6 30 s:   dp={0:E3} m, dv={1:E3} m/s",
            (y6.p - re).Magnitude, (y6.v - ve).Magnitude);
    }

    private static void StudyTimeReversal()
    {
        Console.WriteLine();
        Console.WriteLine("=== Y6C time reversal, exact one-body (30 d) ===");
        double mu = 3.986004418e14d;
        double r = 6.771e6d;
        double v = Math.Sqrt(mu / r);
        var p0 = new Vector3d(r, 0d, 0d);
        var v0 = new Vector3d(0d, v, 0d);
        var star = new OrbitingBody { Name = "TestBody", StandardGravitationalParameter = mu, Radius = 1d };
        StarSystem one = new StarSystem(star);
        var fwd = StudyShipRefA(one, p0, v0, 0d, 30d, 5d);
        var back = StudyShipRefA(one, fwd.p, -fwd.v, 30d * 86400d, 30d, 5d);
        Console.WriteLine("|p_back-p0|={0:E3} m, |v_back+v0|={1:E3} m/s",
            (back.p - p0).Magnitude, (back.v + v0).Magnitude);
    }

    private static void StudyPlanetReference(StudyNBody nb, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== Planet reference convergence: " + years + " y ===");
        var r112 = StudyRefRun(nb, "Y6C", 112.5d, years, 0d);
        var r56 = StudyRefRun(nb, "Y6C", 56.25d, years, 0d);
        var r28 = StudyRefRun(nb, "Y6C", 28.125d, years, 0d);
        Console.WriteLine("112.5->56.25: {0:E3} m; 56.25->28.125: {1:E3} m; ratio {2:F1}",
            StudyRel(r112.P, 2, 1, r56.P),
            StudyRel(r56.P, 2, 1, r28.P),
            StudyRel(r112.P, 2, 1, r56.P) / Math.Max(StudyRel(r56.P, 2, 1, r28.P), 1e-30));
        var y6 = StudyRefRun(nb, "Y6", 7200d, years, 0d);
        var rk4 = StudyRefRun(nb, "RK4", 900d, years, 0d);
        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);
        Console.WriteLine("Y6 7200: error={0:E3} m, dE/E={1:E2}",
            StudyRel(y6.P, 2, 1, r28.P),
            Math.Abs((StudyEnergy(y6.P, y6.V, nb.Mu) - e0) / e0));
        Console.WriteLine("RK4 900: error={0:E3} m, dE/E={1:E2}",
            StudyRel(rk4.P, 2, 1, r28.P),
            Math.Abs((StudyEnergy(rk4.P, rk4.V, nb.Mu) - e0) / e0));
    }
}

