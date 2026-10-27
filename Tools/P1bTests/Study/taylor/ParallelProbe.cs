using System;
using System.Diagnostics;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test520_ParallelProbe()
    {
        Console.WriteLine("--- Parallel probe: 1-year one-body Kepler ---");
        double mu = 3.986004418e14d;
        double r = 6.771e6d;
        double v = Math.Sqrt(mu / r);
        var p0 = new Vector3d(r, 0d, 0d);
        var v0 = new Vector3d(0d, v, 0d);
        var body = new OrbitingBody
        {
            Name = "Probe",
            StandardGravitationalParameter = mu,
            Radius = 1d
        };
        var sys = new StarSystem(body);
        double seconds = 365.25d * 86400d;
        TaylorShip.RunSeconds(
            sys, p0, v0, 0d, 86400d, 24, 1e-12d, 1e-15d, 600d, 1e-3d);
        var watch = Stopwatch.StartNew();
        var run = TaylorShip.RunSeconds(
            sys, p0, v0, 0d, seconds, 24, 1e-12d, 1e-15d, 600d, 1e-3d);
        watch.Stop();
        double period = 2d * Math.PI * Math.Sqrt((r * r * r) / mu);
        double turns = seconds / period;
        double theta = 2d * Math.PI * (turns - Math.Floor(turns));
        var expected = new Vector3d(r * Math.Cos(theta), r * Math.Sin(theta), 0d);
        double dp = (run.P - expected).Magnitude;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "probe ms={0}, steps={1}, dp={2:E2} m",
            watch.ElapsedMilliseconds, run.Steps, dp));
        Check(double.IsFinite(dp) && dp < 1e-1d, "parallel probe dp",
            dp.ToString("E2", CultureInfo.InvariantCulture));
        Console.WriteLine("--- Parallel probe complete ---");
        return 0;
    }
}
