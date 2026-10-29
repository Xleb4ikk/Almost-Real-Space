using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static TaylorNBodyInput ToTaylor(StudyNBody nb)
    {
        return new TaylorNBodyInput
        {
            Names = nb.Names,
            Mu = nb.Mu,
            P0 = nb.P0,
            V0 = nb.V0
        };
    }

    private static void CompareHeader()
    {
        Console.WriteLine("Taylor vs Y6 вЂ” local comparison (double precision)");
        Console.WriteLine("Reference: Y6C (compensated sum). Taylor and Y6 are plain double.");
        Console.WriteLine("Meters are at/near the double rounding floor; compare errors at similar call counts.");
    }

    private static void PrintPlanetRow(
        string method, int order, double h, long steps, long calls, double ms, double allocKiB,
        double inner, double mid, double outer, double de)
    {
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-10} {1,3} {2,8:F0} {3,9} {4,11} {5,9:F1} {6,10:F1} {7,12:E3} {8,12:E3} {9,12:E3} {10,9:E2}",
            method, order, h, steps, calls, ms, allocKiB, inner, mid, outer, de));
    }

    private static void ComparePlanetCase(string label, StarSystem sys, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== N-body: " + label + ", " + years.ToString(CultureInfo.InvariantCulture) + " y ===");
        StudyNBody nb = StudyMakeNBody(sys);

        long r0 = GC.GetAllocatedBytesForCurrentThread();
        StudyRefResult reference = StudyRefRun(nb, "Y6C", 28.125d, years, 0d);
        StudyRefResult referenceCoarse = StudyRefRun(nb, "Y6C", 56.25d, years, 0d);
        long refKiB = (GC.GetAllocatedBytesForCurrentThread() - r0) / 1024L;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "Y6C h=28.125 s: self-convergence vs 56.25 s (inner) = {0:E3} m; calls={1}; {2:F0} ms; {3} KiB",
            StudyRel(reference.P, 2, 1, referenceCoarse.P), reference.Calls, reference.Ms, refKiB));

        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);
        Console.WriteLine("method       K        h     steps       calls        ms   allocKiB        inner          mid        outer       dE/E");

        foreach (double h in new[] { 1800d, 3600d, 7200d, 14400d })
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            StudyRefResult r = StudyRefRun(nb, "Y6", h, years, 0d);
            double kib = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024d;
            long steps = (long)Math.Floor((years * StudyYear) / h);
            if ((years * StudyYear) - (steps * h) > 1e-9d)
            {
                steps++;
            }

            PrintPlanetRow(
                "Y6", 6, h, steps, r.Calls, r.Ms, kib,
                StudyRel(r.P, 2, 1, reference.P),
                StudyRel(r.P, 1, 0, reference.P),
                StudyRel(r.P, StudyOuterIndex(nb), 0, reference.P),
                Math.Abs((StudyEnergy(r.P, r.V, nb.Mu) - e0) / e0));
        }

        TaylorNBodyInput input = ToTaylor(nb);
        var configs = new[]
        {
            (Order: 12, Step: 3600d),
            (Order: 24, Step: 3600d),
            (Order: 12, Step: 14400d),
            (Order: 24, Step: 14400d)
        };
        foreach (var cfg in configs)
        {
            Console.WriteLine("  [running Taylor K=" + cfg.Order + " h=" + cfg.Step.ToString(CultureInfo.InvariantCulture) + " ...]");
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            TaylorNBodyResult t = TaylorNBody.Run(
                input, years, cfg.Order, 1e-9d, 1e-12d, cfg.Step, 1e-6d, 0d);
            double kib = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024d;
            PrintPlanetRow(
                "Taylor", cfg.Order, cfg.Step, t.Steps, t.Calls, t.Ms, kib,
                StudyRel(t.P, 2, 1, reference.P),
                StudyRel(t.P, 1, 0, reference.P),
                StudyRel(t.P, StudyOuterIndex(nb), 0, reference.P),
                Math.Abs((StudyEnergy(t.P, t.V, nb.Mu) - e0) / e0));
        }
    }

    private static void PrintShipRow(
        string method, double h, long steps, long calls, double ms, double allocKiB,
        double errWorld, double errLocal)
    {
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-10} {1,8:F0} {2,9} {3,11} {4,9:F1} {5,10:F1} {6,13:E3} {7,13:E3}",
            method, h, steps, calls, ms, allocKiB, errWorld, errLocal));
    }

    private static void CompareShipCase(
        string label, StarSystem sys, int central, double rp, double ra, double days)
    {
        Console.WriteLine();
        Console.WriteLine("=== Ship: " + label + ", " + days.ToString(CultureInfo.InvariantCulture) + " d ===");
        OrbitingBody body = sys.AllBodies[central];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        double T = t0 + (days * 86400d);
        sys.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(rp, ra, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;
        sys.EvaluateBodyState(body, T, out Vector3d bpT, out _);

        (Vector3d p, Vector3d v) refWorld = StudyShipRefA(sys, p0, v0, t0, days, 1.25d);
        (Vector3d p, Vector3d v) refLocal = StudyShipRefLocal(sys, central, relP, relV, t0, days, 1.25d);
        Vector3d refWorldRel = refWorld.p - bpT;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "ref world vs ref local (both Y6C 1.25 s): {0:E3} m",
            (refWorldRel - refLocal.p).Magnitude));

        Console.WriteLine("method           h     steps       calls        ms   allocKiB       errWorld      errLocal");
        var y6Points = new System.Collections.Generic.List<(double h, Vector3d p)>();
        foreach (double h in new[] { 30d, 60d, 120d })
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            (Vector3d p, Vector3d v, long calls, double ms) r = StudyShipFixed(
                sys, p0, v0, t0, days, h, "Y6");
            double kib = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024d;
            long steps = (long)Math.Floor((days * 86400d) / h);
            y6Points.Add((h, r.p));
            PrintShipRow(
                "Y6", h, steps, r.calls, r.ms, kib,
                (r.p - bpT - refWorldRel).Magnitude,
                (r.p - bpT - refLocal.p).Magnitude);
        }

        Console.WriteLine("  [running Taylor K=24 h<=3600 ...]");
        long ta0 = GC.GetAllocatedBytesForCurrentThread();
        TaylorShipResult t = TaylorShip.Run(sys, p0, v0, t0, days, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
        double tkib = (GC.GetAllocatedBytesForCurrentThread() - ta0) / 1024d;
        PrintShipRow(
            "Taylor", 3600d, t.Steps, t.Calls, t.Ms, tkib,
            (t.P - bpT - refWorldRel).Magnitude,
            (t.P - bpT - refLocal.p).Magnitude);

        foreach (var point in y6Points)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "|Y6 h={0:F0} - Taylor| = {1:E3} m (same world formulation)",
                point.h, (point.p - t.P).Magnitude));
        }
    }

    private static void CompareY6COnly(string label, StarSystem sys, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== Y6C row: " + label + ", " + years.ToString(CultureInfo.InvariantCulture) + " y ===");
        StudyNBody nb = StudyMakeNBody(sys);
        StudyRefResult reference = StudyRefRun(nb, "Y6C", 28.125d, years, 0d);
        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        StudyRefResult r = StudyRefRun(nb, "Y6C", 112.5d, years, 0d);
        double kib = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024d;
        long steps = (long)Math.Floor((years * StudyYear) / 112.5d);
        PrintPlanetRow(
            "Y6C", 6, 112.5d, steps, r.Calls, r.Ms, kib,
            StudyRel(r.P, 2, 1, reference.P),
            StudyRel(r.P, 1, 0, reference.P),
            StudyRel(r.P, StudyOuterIndex(nb), 0, reference.P),
            Math.Abs((StudyEnergy(r.P, r.V, nb.Mu) - e0) / e0));
    }

    private static void CompareKeplerCase()
    {
        Console.WriteLine();
        Console.WriteLine("=== Exact Kepler one-body, 30 d ===");
        double mu = 3.986004418e14d;
        double r = 6.771e6d;
        double v = Math.Sqrt(mu / r);
        var p0 = new Vector3d(r, 0d, 0d);
        var v0 = new Vector3d(0d, v, 0d);
        var star = new OrbitingBody { Name = "TestBody", StandardGravitationalParameter = mu, Radius = 1d };
        StarSystem one = new StarSystem(star);
        StudyKeplerDrift(p0, v0, mu, 30d * 86400d, out Vector3d re, out Vector3d ve);

        Console.WriteLine("method       K        h     steps       calls        ms   allocKiB           dp           dv");
        foreach (int order in new[] { 12, 24 })
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            TaylorShipResult t = TaylorShip.Run(
                one, p0, v0, 0d, 30d, order, 1e-12d, 1e-15d, 600d, 1e-3d);
            double kib = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024d;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-10} {1,3} {2,8:F0} {3,9} {4,11} {5,9:F1} {6,10:F1} {7,12:E3} {8,12:E3}",
                "Taylor", order, 600d, t.Steps, t.Calls, t.Ms, kib,
                (t.P - re).Magnitude, (t.V - ve).Magnitude));
        }

        foreach (double h in new[] { 5d, 10d, 30d, 60d, 120d })
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            (Vector3d p, Vector3d v, long calls, double ms) y6 = StudyShipFixed(
                one, p0, v0, 0d, 30d, h, "Y6");
            double kib = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024d;
            long steps = (long)Math.Floor((30d * 86400d) / h);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-10} {1,3} {2,8:F0} {3,9} {4,11} {5,9:F1} {6,10:F1} {7,12:E3} {8,12:E3}",
                "Y6", 6, h, steps, y6.calls, y6.ms, kib,
                (y6.p - re).Magnitude, (y6.v - ve).Magnitude));
        }

        foreach (double h in new[] { 30d, 60d, 120d })
        {
            (Vector3d p, Vector3d v) y6c = StudyShipRefA(one, p0, v0, 0d, 30d, h);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-10} {1,3} {2,8:F0} {3,9} {4,11} {5,9} {6,10} {7,12:E3} {8,12:E3}",
                "Y6C", 6, h, (long)Math.Floor((30d * 86400d) / h), "-", "-", "-",
                (y6c.p - re).Magnitude, (y6c.v - ve).Magnitude));
        }
    }

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        bool all = args.Length == 0;
        bool runReal = all;
        bool runCustom = all;
        bool runShips = all;
        bool runKepler = all;
        bool runY6COnly = false;
        bool runDdReal1y = false;
        bool runDdCustom1y = false;
        bool runDdReal10y = false;
        bool runDdCustom10y = false;
        bool ddK24 = false;
        bool runDdSelfCheck = false;
        bool ddH1800 = false;
        bool runY6CFineReal = false;
        bool runY6CFineCustom = false;
        bool runY6CFinerCustom = false;
        bool ddUseOpt = false;
        bool runDdOptCmp1yReal = false;
        bool runDdOptCmp1yCustom = false;
        bool runDdOptCmp10yReal = false;
        bool runDdOptCmp10yCustom = false;
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--real":
                    runReal = true;
                    break;
                case "--custom":
                    runCustom = true;
                    break;
                case "--ships":
                    runShips = true;
                    break;
                case "--kepler":
                    runKepler = true;
                    break;
                case "--y6c":
                    runY6COnly = true;
                    break;
                case "--dd-real-1y":
                    runDdReal1y = true;
                    break;
                case "--dd-custom-1y":
                    runDdCustom1y = true;
                    break;
                case "--dd-real-10y":
                    runDdReal10y = true;
                    break;
                case "--dd-custom-10y":
                    runDdCustom10y = true;
                    break;
                case "--y6c-fine-real":
                    runY6CFineReal = true;
                    break;
                case "--y6c-finer-custom":
                    runY6CFinerCustom = true;
                    break;
                case "--y6c-fine-custom":
                    runY6CFineCustom = true;
                    break;
                case "--dd-h1800":
                    ddH1800 = true;
                    break;
                case "--dd-opt":
                    ddUseOpt = true;
                    break;
                case "--dd-opt-compare-1y-real":
                    runDdOptCmp1yReal = true;
                    break;
                case "--dd-opt-compare-1y-custom":
                    runDdOptCmp1yCustom = true;
                    break;
                case "--dd-opt-compare-10y-real":
                    runDdOptCmp10yReal = true;
                    break;
                case "--dd-opt-compare-10y-custom":
                    runDdOptCmp10yCustom = true;
                    break;
                case "--dd-selfcheck":
                    runDdSelfCheck = true;
                    break;
                case "--dd-k24":
                    ddK24 = true;
                    break;
                case "--all":
                    runReal = true;
                    runCustom = true;
                    runShips = true;
                    runKepler = true;
                    break;
            }
        }

        DdSmallStep = ddH1800;
        DdUseOpt = ddUseOpt;
        CompareHeader();

        if (runReal)
        {
            Console.WriteLine();
            Console.WriteLine("--- Planes: real solar 7-body ---");
            ComparePlanetCase("real solar 7-body", StudyRealSolar7(), 10d);
        }

        if (runCustom)
        {
            Console.WriteLine();
            Console.WriteLine("--- Planes: custom 6-body ---");
            ComparePlanetCase("custom 6-body", StudyCustomSystem(), 10d);
        }

        if (runShips)
        {
            Console.WriteLine();
            Console.WriteLine("--- Ships: bake 1 y so Taylor and Y6 see the same body states ---");
            StarSystem realShip = StudyRealSolar7();
            EphemerisBaker.BakeAndAttach(realShip, 1d, 900d, 12, null);
            StarSystem customShip = StudyCustomSystem();
            EphemerisBaker.BakeAndAttach(customShip, 1d, 900d, 12, null);

            CompareShipCase("real Earth LEO 400 km", realShip, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 30d);
            CompareShipCase("real Earth 400/40000 km", realShip, 1, 6.371e6d + 4e5d, 6.371e6d + 4e7d, 30d);
            CompareShipCase("real Sun 1 AU", realShip, 0, StudyAU, StudyAU, 30d);
            CompareShipCase("real Sun 0.1/5 AU", realShip, 0, 0.1d * StudyAU, 5d * StudyAU, 30d);
            CompareShipCase("custom Terra LEO 400 km", customShip, 1, 1.143e6d + 4e5d, 1.143e6d + 4e5d, 30d);
            CompareShipCase("custom Terra 400/40000 km", customShip, 1, 1.143e6d + 4e5d, 1.143e6d + 4e7d, 30d);
        }

        if (runKepler)
        {
            CompareKeplerCase();
        }

        if (runY6COnly)
        {
            CompareY6COnly("real solar 7-body", StudyRealSolar7(), 10d);
            CompareY6COnly("custom 6-body", StudyCustomSystem(), 10d);
        }

        if (runDdReal1y)
        {
            CompareDdCase("real solar 7-body", StudyRealSolar7(), 1d, 28.125d, 14.0625d, ddK24);
        }

        if (runDdCustom1y)
        {
            CompareDdCase("custom 6-body", StudyCustomSystem(), 1d, 28.125d, 14.0625d, ddK24);
        }

        if (runDdReal10y)
        {
            CompareDdCase("real solar 7-body", StudyRealSolar7(), 10d, 14.0625d, 28.125d, false);
        }

        if (runDdSelfCheck)
        {
            RunDdSelfCheck();
        }

        if (runDdCustom10y)
        {
            CompareDdCase("custom 6-body", StudyCustomSystem(), 10d, 14.0625d, 28.125d, false);
        }

        if (runY6CFineReal)
        {
            CompareY6CFine("real solar 7-body", StudyRealSolar7(), 10d);
        }

        if (runY6CFinerCustom)
        {
            CompareY6CFiner("custom 6-body", StudyCustomSystem(), 10d);
        }

        if (runDdOptCmp1yReal)
        {
            CompareDdImplementations("real solar 7-body", StudyRealSolar7(), 1d, 28.125d);
        }

        if (runDdOptCmp1yCustom)
        {
            CompareDdImplementations("custom 6-body", StudyCustomSystem(), 1d, 28.125d);
        }

        if (runDdOptCmp10yReal)
        {
            CompareDdImplementations("real solar 7-body", StudyRealSolar7(), 10d, 14.0625d);
        }

        if (runDdOptCmp10yCustom)
        {
            CompareDdImplementations("custom 6-body", StudyCustomSystem(), 10d, 14.0625d);
        }

        if (runY6CFineCustom)
        {
            CompareY6CFine("custom 6-body", StudyCustomSystem(), 10d);
        }

        Console.WriteLine();
        Console.WriteLine("done.");
        return 0;
    }
}






