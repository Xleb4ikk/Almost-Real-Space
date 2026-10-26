using System;
using System.Diagnostics;
using System.Threading.Tasks;
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

    private static void TaylorNBodyCase(string label, StudyNBody nb, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== Taylor n-body: " + label + ", " + years + " y ===");
        var sw = Stopwatch.StartNew();
        var t = TaylorNBody.Run(ToTaylor(nb), years, 24, 1e-9d, 1e-12d, 3600d, 1e-6d, 0d);
        sw.Stop();
        var r28 = StudyRefRun(nb, "Y6C", 28.125d, years, 0d);
        var y6 = StudyRefRun(nb, "Y6", 7200d, years, 0d);
        var rk4 = StudyRefRun(nb, "RK4", 900d, years, 0d);
        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);
        Console.WriteLine(
            "Taylor: err Moon/Earth={0:E3} m, dE/E={1:E2}, calls={2:N0}, steps={3:N0}, {4:F0} ms",
            StudyRel(t.P, 2, 1, r28.P),
            Math.Abs((StudyEnergy(t.P, t.V, nb.Mu) - e0) / e0),
            t.Calls, t.Steps, t.Ms);
        Console.WriteLine("Y6C 28.125: err={0:E3} m", StudyRel(r28.P, 2, 1, r28.P));
        Console.WriteLine("Y6 7200 vs Y6C 28.125: err={0:E3} m", StudyRel(y6.P, 2, 1, r28.P));
        Console.WriteLine("RK4 900 vs Y6C 28.125: err={0:E3} m", StudyRel(rk4.P, 2, 1, r28.P));
    }

    private static void TaylorShipCase(
        string label, StarSystem sys, StudyNBody nb, int central, double rp, double ra, double days)
    {
        Console.WriteLine();
        Console.WriteLine("=== Taylor ship: " + label + ", " + days + " d ===");
        OrbitingBody body = sys.AllBodies[central];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        double T = t0 + (days * 86400d);
        sys.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(rp, ra, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;
        sys.EvaluateBodyState(body, T, out Vector3d bpT, out _);

        var refLocal = StudyShipRefLocal(sys, central, relP, relV, t0, days, 1.25d);
        Vector3d refRel = refLocal.p;

        var sw = Stopwatch.StartNew();
        var taylor = TaylorShip.Run(sys, p0, v0, t0, days, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
        sw.Stop();
        Vector3d taylorRel = taylor.P - bpT;

        var tight = StudyShipDop853(sys, central, p0, v0, t0, days, 1e-12d, 1e-15d, 1e-14d);
        Vector3d tightRel = tight.p - bpT;
        var direct = StudyShipDop853Local(
            sys, central, relP, relV, t0, days, directForce: true);

        Console.WriteLine(
            "Taylor: err vs local A={0:E3} m, calls={1:N0}, steps={2:N0}, {3:F0} ms",
            (taylorRel - refRel).Magnitude, taylor.Calls, taylor.Steps, taylor.Ms);
        Console.WriteLine(
            "DOP853 tight world: err vs local A={0:E3} m, calls={1:N0}, {2:F0} ms",
            (tightRel - refRel).Magnitude, tight.calls, tight.ms);
        Console.WriteLine(
            "DOP853 direct local: err vs local A={0:E3} m, calls={1:N0}, {2:F0} ms",
            (direct.p - refRel).Magnitude, direct.calls, direct.ms);
        Console.WriteLine("alloc note: Taylor jets are allocated per step; see calls/steps above");
    }

    private static void TaylorKeplerCase()
    {
        Console.WriteLine();
        Console.WriteLine("=== Taylor exact Kepler one-body (30 d) ===");
        double mu = 3.986004418e14d;
        double r = 6.771e6d;
        double v = Math.Sqrt(mu / r);
        var p0 = new Vector3d(r, 0d, 0d);
        var v0 = new Vector3d(0d, v, 0d);
        var body = new OrbitingBody { Name = "TestBody", StandardGravitationalParameter = mu, Radius = 1d };
        StarSystem one = new StarSystem(body);
        StudyKeplerDrift(p0, v0, mu, 30d * 86400d, out Vector3d re, out Vector3d ve);
        var taylor = TaylorShip.Run(one, p0, v0, 0d, 30d, 24, 1e-12d, 1e-15d, 600d, 1e-3d);
        Console.WriteLine("exact vs Taylor: dp={0:E3} m, dv={1:E3} m/s, calls={2:N0}, steps={3:N0}, {4:F0} ms",
            (taylor.P - re).Magnitude, (taylor.V - ve).Magnitude, taylor.Calls, taylor.Steps, taylor.Ms);
    }

    private static int Test504_TaylorReference()
    {
        Console.WriteLine("--- Taylor reference ---");
        Parallel.Invoke(
            () =>
            {
                StarSystem real = StudyRealSolar7();
                StudyNBody realNb = StudyMakeNBody(real);
                StudyBake("Taylor real", real, realNb, 1d);
                TaylorNBodyCase("real solar 7-body", realNb, 10d);
                TaylorShipCase("Earth ecc 400/40000", real, realNb, 1, 6.771e6d, 4.6371e7d, 30d);
            },
            () =>
            {
                StarSystem custom = StudyCustomSystem();
                StudyNBody customNb = StudyMakeNBody(custom);
                StudyBake("Taylor custom", custom, customNb, 1d);
                TaylorNBodyCase("custom 6-body", customNb, 10d);
                TaylorShipCase("Terra ecc 400/40000", custom, customNb, 1, 1.543e6d, 4.114e7d, 30d);
            },
            TaylorKeplerCase);

        Console.WriteLine("--- Taylor reference complete ---");
        return 0;
    }
}
