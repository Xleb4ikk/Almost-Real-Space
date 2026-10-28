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
        var t = TaylorNBody.Run(ToTaylor(nb), years, 24, 1e-9d, 1e-12d, 86400d, 1e-6d, 0d);
        var r28 = StudyRefRun(nb, "Y6C", 28.125d, years, 0d);
        var r14 = StudyRefRun(nb, "Y6C", 14.0625d, years, 0d);
        var y6 = StudyRefRun(nb, "Y6", 7200d, years, 0d);
        var rk4 = StudyRefRun(nb, "RK4", 900d, years, 0d);
        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);
        double errTaylor = StudyRel(t.P, 2, 1, r28.P);
        double errSelf = StudyRel(r28.P, 2, 1, r14.P);
        double errY6 = StudyRel(y6.P, 2, 1, r28.P);
        double errRk4 = StudyRel(rk4.P, 2, 1, r28.P);
        Console.WriteLine(
            label + " | Taylor hmax 1d vs Y6C 28.125: err Moon/Earth={0:E3} m, dE/E={1:E2}, calls={2:N0}, steps={3:N0}, {4:F0} ms",
            errTaylor,
            Math.Abs((StudyEnergy(t.P, t.V, nb.Mu) - e0) / e0),
            t.Calls, t.Steps, t.Ms);
        Console.WriteLine(label + " | Y6C 28.125 vs 14.0625: err={0:E3} m", errSelf);
        Console.WriteLine(label + " | Y6 7200 vs Y6C 28.125: err={0:E3} m", errY6);
        Console.WriteLine(label + " | RK4 900 vs Y6C 28.125: err={0:E3} m", errRk4);
        Check(double.IsFinite(errTaylor),
            label + " finite", errTaylor.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        Check(errTaylor < 5d, label + " Taylor hmax 1d vs Y6C", errTaylor.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        Check(errSelf < 1e-2d, label + " Y6C self-convergence", errSelf.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
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
        var taylorBig = TaylorShip.Run(sys, p0, v0, t0, days, 24, 1e-9d, 1e-12d, 86400d, 1e-3d);
        sw.Stop();
        Vector3d taylorRel = taylor.P - bpT;

        var tight = StudyShipDop853(sys, central, p0, v0, t0, days, 1e-12d, 1e-15d, 1e-14d);
        Vector3d tightRel = tight.p - bpT;
        var direct = StudyShipDop853Local(
            sys, central, relP, relV, t0, days, directForce: true);

        double errTaylor = (taylorRel - refRel).Magnitude;
        double errBig = ((taylorBig.P - bpT) - refRel).Magnitude;
        double errTight = (tightRel - refRel).Magnitude;
        double errDirect = (direct.p - refRel).Magnitude;
        Console.WriteLine(
            label + " | Taylor: err vs local A={0:E3} m, calls={1:N0}, steps={2:N0}, {3:F0} ms",
            errTaylor, taylor.Calls, taylor.Steps, taylor.Ms);
        Console.WriteLine(label + " | Taylor maxStep=86400: err vs local A={0:E3} m, steps={1:N0}",
            errBig, taylorBig.Steps);
        Console.WriteLine(
            label + " | DOP853 tight world: err vs local A={0:E3} m, calls={1:N0}, {2:F0} ms",
            errTight, tight.calls, tight.ms);
        Console.WriteLine(
            label + " | DOP853 direct local: err vs local A={0:E3} m, calls={1:N0}, {2:F0} ms",
            errDirect, direct.calls, direct.ms);
        Console.WriteLine("alloc note: Taylor jets are allocated per step; see calls/steps above");
        Check(double.IsFinite(errTaylor) && double.IsFinite(errBig) && double.IsFinite(errDirect),
            label + " ship finite", errTaylor.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        Check(errTaylor < 5d, label + " Taylor vs local ref", errTaylor.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        Check(errBig < 5d, label + " Taylor maxStep 1d", errBig.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        Check(errDirect < 0.1d, label + " direct local", errDirect.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void TaylorKeplerCase(string label, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== Taylor exact circular Kepler one-body (" + label + ") ===");
        double mu = 3.986004418e14d;
        double r = 6.771e6d;
        double v = Math.Sqrt(mu / r);
        var p0 = new Vector3d(r, 0d, 0d);
        var v0 = new Vector3d(0d, v, 0d);
        var body = new OrbitingBody { Name = "TestBody", StandardGravitationalParameter = mu, Radius = 1d };
        StarSystem one = new StarSystem(body);
        double seconds = years * 365.25d * 86400d;
        double period = 2d * Math.PI * Math.Sqrt((r * r * r) / mu);
        double turns = seconds / period;
        double theta = 2d * Math.PI * (turns - Math.Floor(turns));
        var re = new Vector3d(r * Math.Cos(theta), r * Math.Sin(theta), 0d);
        var ve = new Vector3d(-v * Math.Sin(theta), v * Math.Cos(theta), 0d);
        double maxStep = years > 1d ? 3600d : 600d;
        var taylor = TaylorShip.RunSeconds(
            one, p0, v0, 0d, seconds, 24, 1e-12d, 1e-15d, maxStep, 1e-3d);
        double dp = (taylor.P - re).Magnitude;
        double dv = (taylor.V - ve).Magnitude;
        Console.WriteLine(label + " | exact vs Taylor: dp={0:E3} m, dv={1:E3} m/s, calls={2:N0}, steps={3:N0}, {4:F0} ms",
            dp, dv, taylor.Calls, taylor.Steps, taylor.Ms);
        double dpGate = years > 1d ? 1e-1d : 1e-3d;
        double dvGate = years > 1d ? 1e-4d : 1e-6d;
        Check(double.IsFinite(dp) && double.IsFinite(dv),
            "kepler " + label + " finite", dp.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        Check(dp < dpGate, "kepler " + label + " dp", dp.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        Check(dv < dvGate, "kepler " + label + " dv", dv.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int Test504_TaylorReference()
    {
        Console.WriteLine("--- Taylor reference ---");
        Parallel.Invoke(
            () =>
            {
                StarSystem real = StudyRealSolar7();
                StudyNBody realNb = StudyMakeNBody(real);
                StudyBake("Taylor real", real, realNb, 1d, 1d);
                TaylorNBodyCase("real solar 7-body", realNb, 10d);
                TaylorShipCase("Earth ecc 400/40000", real, realNb, 1, 6.771e6d, 4.6371e7d, 30d);
            },
            () =>
            {
                StarSystem custom = StudyCustomSystem();
                StudyNBody customNb = StudyMakeNBody(custom);
                StudyBake("Taylor custom", custom, customNb, 1d, 1d);
                TaylorNBodyCase("custom 6-body", customNb, 10d);
                TaylorShipCase("Terra ecc 400/40000", custom, customNb, 1, 1.543e6d, 4.114e7d, 30d);
            },
            () =>
            {
                TaylorKeplerCase("30 d", 30d / 365.25d);
                TaylorKeplerCase("10 y", 10d);
            });

        Console.WriteLine("--- Taylor reference complete ---");
        return 0;
    }
}

