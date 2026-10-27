using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test514_DdShip()
    {
        Console.WriteLine("--- Eccentric ship: double vs double-double, 1000 periods ---");
        double[] pt = Golden(new[]
        {
            "0.010000000000000008881732849162012514977487483110357",
            "-0.0000000000014295607723158804152423118877007949172005436444678",
            "0.0"
        });
        double[] vt = Golden(new[]
        {
            "0.0000000010133887629119273034890065081145049902060281462693",
            "14.106735979665877778116152358071374581072676292422",
            "0.0"
        });
        const double T = 6283.185307179493d;
        var body = new OrbitingBody
        {
            Name = "Central",
            StandardGravitationalParameter = 1d,
            Radius = 1d
        };
        var sys = new StarSystem(body);
        var p0 = new Vector3d(0.010000000000000009d, 0d, 0d);
        var v0 = new Vector3d(0d, 14.106735979665878d, 0d);
        var ship = TaylorShip.RunSeconds(
            sys, p0, v0, 0d, T, 20, 1e-16d, 1e-16d, T, 1e-12d);
        double shipDp = Distance(ship.P, pt);
        double shipDv = Distance(ship.V, vt);

        var dd = TaylorShip.RunDdSeconds(
            sys, p0, v0, 0d, T, 20, 1e-18d, 1e-18d, T, 1e-12d);
        double ddDp = Distance(dd.P, pt);
        double ddDv = Distance(dd.V, vt);

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "double: dp={0:E2} dv={1:E2} steps={2} | dd: dp={3:E2} dv={4:E2} steps={5}",
            shipDp, shipDv, ship.Steps, ddDp, ddDv, dd.Steps));
        Check(double.IsFinite(shipDp) && double.IsFinite(shipDv), "dd ship double finite", "dp/dv");
        Check(double.IsFinite(ddDp) && double.IsFinite(ddDv), "dd ship finite", "dp/dv");
        Check(ddDp < 1e-8d, "dd ship dp", ddDp.ToString("E2", CultureInfo.InvariantCulture));
        Check(ddDv < 1e-5d, "dd ship dv", ddDv.ToString("E2", CultureInfo.InvariantCulture));
        Check(ddDp < shipDp * 0.1d, "dd ship dp win", (shipDp / Math.Max(ddDp, 1e-300d)).ToString("E1", CultureInfo.InvariantCulture));
        Check(ddDv < shipDv * 0.1d, "dd ship dv win", (shipDv / Math.Max(ddDv, 1e-300d)).ToString("E1", CultureInfo.InvariantCulture));
        Console.WriteLine("--- Eccentric ship complete ---");
        return 0;
    }
}
