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
    // Ship table
    // ------------------------------------------------------------------

    private static void StudyShipOrbit(
        double rp, double ra, double mu, out Vector3d relP, out Vector3d relV)
    {
        double a = 0.5d * (rp + ra);
        relP = new Vector3d(rp, 0d, 0d);
        double vp = ra == rp
            ? Math.Sqrt(mu / rp)
            : Math.Sqrt(mu * (2d / rp - 1d / a));
        relV = new Vector3d(0d, vp, 0d);
    }

    /// <summary>
    /// Acceleration of the ship relative to the central body. Both samples use the
    /// same world-frame field, so the frame's indirect term cancels; only the
    /// difference of the field at the ship and at the central body remains.
    /// </summary>
    private static Vector3d StudyLocalAcceleration(
        StarSystem sys, int central, Vector3d relP, double t)
    {
        sys.EvaluateBodyState(sys.AllBodies[central], t, out Vector3d bp, out _);
        Vector3d shipAcc = sys.EvaluateShipAccelerationCached(bp + relP, t);
        Vector3d centralAcc = sys.EvaluateShipAccelerationCached(bp, t);
        return shipAcc - centralAcc;
    }

    /// <summary>
    /// Same relative acceleration, but each body's differential term is formed
    /// from relative vectors instead of subtracting two ~mu/r^2 fields. The test
    /// bodies have J2 = 0; the root indirect term cancels in a relative frame.
    /// </summary>
    private static Vector3d StudyLocalAccelerationDirect(
        StarSystem sys, int central, Vector3d relP, double t)
    {
        var bodies = sys.AllBodies;
        sys.EvaluateBodyState(bodies[central], t, out Vector3d bc, out _);
        Vector3d total = Vector3d.Zero;
        for (int i = 0; i < bodies.Count; i++)
        {
            OrbitingBody body = bodies[i];
            double mu = body.ResolveStandardGravitationalParameter();
            if (mu == 0d)
            {
                continue;
            }

            if (i == central)
            {
                total += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    relP, Vector3d.Zero, mu);
                continue;
            }

            sys.EvaluateBodyState(body, t, out Vector3d bp, out _);
            Vector3d dc = bc - bp;
            total += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                dc + relP, Vector3d.Zero, mu);
            total -= PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                dc, Vector3d.Zero, mu);
        }

        return total;
    }

    private static void StudyShipTable(
        string label, StarSystem sys, StudyNBody nb, int central, double rp, double ra, double days)
    {
        bool custom = label.IndexOf("Custom", StringComparison.Ordinal) >= 0;
        Console.WriteLine();
        Console.WriteLine("=== " + label + ": rp={0:E3} m, ra={1:E3} m, {2} d ===", rp, ra, days);
        Console.WriteLine("ship state is local to the central body; the game DOPRI5 row is the actual world-state path");
        Console.WriteLine("'direct' forms differential acceleration from relative vectors; 'field-diff' subtracts two world-field samples");
        OrbitingBody body = sys.AllBodies[central];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        double T = t0 + days * 86400d;
        sys.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(rp, ra, mu, out Vector3d relP, out Vector3d relV);
        Vector3d p0 = bp + relP;
        Vector3d v0 = bv + relV;

        var refA = StudyShipRefLocal(sys, central, relP, relV, t0, days, 2.5d);
        var refAHalf = StudyShipRefLocal(sys, central, relP, relV, t0, days, 1.25d);

        // Full n-body reference B: bodies at t0, then the massless ship.
        var bodiesAtT0 = StudyRefRun(nb, "Y6C", 10d, t0 / StudyYear, 0d);
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

        var refB = StudyRefRun(shipNb, "Y6C", 5d, days / 365.25d, 0d);
        int ship = nb.Count;
        Vector3d refBRel = new Vector3d(
            refB.P[3 * ship] - refB.P[3 * central],
            refB.P[(3 * ship) + 1] - refB.P[(3 * central) + 1],
            refB.P[(3 * ship) + 2] - refB.P[(3 * central) + 2]);

        sys.EvaluateBodyState(body, T, out Vector3d bpT, out _);
        Vector3d refARel = refA.p;

        Console.WriteLine("ref A local self-convergence (2.5 s vs 1.25 s): dp={0:E3} m, dv={1:E3} m/s",
            (refA.p - refAHalf.p).Magnitude, (refA.v - refAHalf.v).Magnitude);
        Console.WriteLine("ref B (full n-body, 5 s): pre={0:N0}+{1:N0} calls, {2:F0}+{3:F0} ms",
            bodiesAtT0.Calls, refB.Calls, bodiesAtT0.Ms, refB.Ms);
        Console.WriteLine("A vs B at end: {0:E3} m", (refARel - refBRel).Magnitude);

        (Vector3d p, Vector3d v, long calls, double ms) game = default;
        (Vector3d p, Vector3d v, long calls, double ms) dop853 = default;
        (Vector3d p, Vector3d v, long calls, double ms) dop853Direct = default;
        (Vector3d p, Vector3d v, long calls, double ms) y6_60 = default;
        (Vector3d p, Vector3d v, long calls, double ms) y6_30 = default;
        (Vector3d p, Vector3d v, long calls, double ms) pefrl_30 = default;
        (Vector3d p, Vector3d v, long calls, double ms) wh_120 = default;
        (Vector3d p, Vector3d v, long calls, double ms) wh_240 = default;
        (Vector3d p, Vector3d v, long calls, double ms) wh_480 = default;
        long gameAlloc = 0L;
        long dopAlloc = 0L;
        long dopDirectAlloc = 0L;
        long y6_60Alloc = 0L;
        long y6_30Alloc = 0L;
        long pefrlAlloc = 0L;
        long wh120Alloc = 0L;
        long wh240Alloc = 0L;
        long wh480Alloc = 0L;

        Parallel.Invoke(
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                game = StudyShipGame(methodSys, p0, v0, t0, days);
                gameAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                dop853 = StudyShipDop853Local(methodSys, central, relP, relV, t0, days);
                dopAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                dop853Direct = StudyShipDop853Local(
                    methodSys, central, relP, relV, t0, days, directForce: true);
                dopDirectAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                y6_60 = StudyShipFixedLocal(
                    methodSys, central, relP, relV, t0, days, 60d, "Y6", directForce: true);
                y6_60Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                y6_30 = StudyShipFixedLocal(
                    methodSys, central, relP, relV, t0, days, 30d, "Y6", directForce: true);
                y6_30Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                pefrl_30 = StudyShipFixedLocal(
                    methodSys, central, relP, relV, t0, days, 30d, "PEFRL", directForce: true);
                pefrlAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                wh_120 = StudyShipWhLocal(
                    methodSys, central, relP, relV, t0, days, 120d, directForce: true);
                wh120Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                wh_240 = StudyShipWhLocal(
                    methodSys, central, relP, relV, t0, days, 240d, directForce: true);
                wh240Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            },
            () =>
            {
                StarSystem methodSys = StudyCloneBakedSystem(sys, custom);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                wh_480 = StudyShipWhLocal(
                    methodSys, central, relP, relV, t0, days, 480d, directForce: true);
                wh480Alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            });

        Vector3d gameRel = game.p - bpT;
        Console.WriteLine("{" + "system,-24} {" + "method,-22} {" + "h, s,6} {" + "calls,11} {" +
            "err vs A, m,13} {" + "err vs B, m,13} {" + "ms,8} {" + "us/call,9} {" + "KiB,8}");
        PrintShipRow(label, "DOPRI5 (game, world)", 0d, game.calls, gameRel, refARel, refBRel, game.ms, gameAlloc);
        PrintShipRow(label, "DOP853 field-diff", 0d, dop853.calls, dop853.p, refARel, refBRel, dop853.ms, dopAlloc);
        PrintShipRow(label, "DOP853 direct", 0d, dop853Direct.calls, dop853Direct.p, refARel, refBRel, dop853Direct.ms, dopDirectAlloc);
        PrintShipRow(label, "Y6 direct", 60d, y6_60.calls, y6_60.p, refARel, refBRel, y6_60.ms, y6_60Alloc);
        PrintShipRow(label, "Y6 direct", 30d, y6_30.calls, y6_30.p, refARel, refBRel, y6_30.ms, y6_30Alloc);
        PrintShipRow(label, "PEFRL direct", 30d, pefrl_30.calls, pefrl_30.p, refARel, refBRel, pefrl_30.ms, pefrlAlloc);
        PrintShipRow(label, "WH Y6 direct", 120d, wh_120.calls, wh_120.p, refARel, refBRel, wh_120.ms, wh120Alloc);
        PrintShipRow(label, "WH Y6 direct", 240d, wh_240.calls, wh_240.p, refARel, refBRel, wh_240.ms, wh240Alloc);
        PrintShipRow(label, "WH Y6 direct", 480d, wh_480.calls, wh_480.p, refARel, refBRel, wh_480.ms, wh480Alloc);
        Console.WriteLine();
    }

    private static void PrintShipRow(
        string label, string method, double h, long calls, Vector3d rel,
        Vector3d refARel, Vector3d refBRel, double ms, long allocBytes)
    {
        double usPerCall = calls > 0L ? ms * 1000d / calls : 0d;
        Console.WriteLine(
            "{0,-24} {1,-22} {2,6:F0} {3,11:N0} {4,13:E3} {5,13:E3} {6,8:F0} {7,9:F2} {8,8:N0}",
            label, method, h, calls, (rel - refARel).Magnitude, (rel - refBRel).Magnitude,
            ms, usPerCall, allocBytes / 1024d);
    }
}

