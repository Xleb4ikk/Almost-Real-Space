using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private sealed class KeplerGolden
    {
        public string Label;
        public double Mu;
        public double TotalSeconds;
        public double[] P0;
        public double[] V0;
        public string[] PT;
        public string[] VT;
        public int Order = 12;
        public double Tolerance = 1e-12d;
        public double PositionGate = 1e-6d;
        public double VelocityGate = 1e-5d;
    }

    private static double[] Golden(string[] values)
    {
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = double.Parse(values[i], CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static double Distance(double[] a, double[] b)
    {
        double sum = 0d;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            sum += d * d;
        }

        return Math.Sqrt(sum);
    }

    private static double Distance(Vector3d a, double[] b)
    {
        double dx = a.X - b[0];
        double dy = a.Y - b[1];
        double dz = a.Z - b[2];
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static KeplerGolden ShipCase(
        string label, double mu, double totalSeconds,
        double[] p0, double[] v0, string[] pt, string[] vt)
    {
        return new KeplerGolden
        {
            Label = label,
            Mu = mu,
            TotalSeconds = totalSeconds,
            P0 = p0,
            V0 = v0,
            PT = pt,
            VT = vt
        };
    }

    private static KeplerGolden NBodyCase(
        string label, double mu, double totalSeconds,
        double[] p0, double[] v0, string[] pt, string[] vt)
    {
        return new KeplerGolden
        {
            Label = label,
            Mu = mu,
            TotalSeconds = totalSeconds,
            P0 = p0,
            V0 = v0,
            PT = pt,
            VT = vt
        };
    }

    private static void RunShipGolden(KeplerGolden c)
    {
        var body = new OrbitingBody
        {
            Name = "Central",
            StandardGravitationalParameter = c.Mu,
            Radius = 1d
        };
        var sys = new StarSystem(body);
        var p0 = new Vector3d(c.P0[0], c.P0[1], c.P0[2]);
        var v0 = new Vector3d(c.V0[0], c.V0[1], c.V0[2]);
        var r = TaylorShip.RunSeconds(
            sys, p0, v0, 0d, c.TotalSeconds, c.Order, c.Tolerance, c.Tolerance, c.TotalSeconds, 1e-12d);
        double[] pt = Golden(c.PT);
        double[] vt = Golden(c.VT);
        double dp = Distance(r.P, pt);
        double dv = Distance(r.V, vt);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-28} ship: dp={1:E3} dv={2:E3} steps={3:N0} calls={4:N0} {5:F0} ms",
            c.Label, dp, dv, r.Steps, r.Calls, r.Ms));
        Check(double.IsFinite(dp) && double.IsFinite(dv), c.Label + " ship finite", "dp/dv finite");
        Check(dp < c.PositionGate, c.Label + " ship dp", dp.ToString("E3", CultureInfo.InvariantCulture));
        Check(dv < c.VelocityGate, c.Label + " ship dv", dv.ToString("E3", CultureInfo.InvariantCulture));
        CheckReferenceMargin(c.Label + " ship", Math.Max(dp, dv), 1e-40d);
    }

    private static void RunNBodyGolden(KeplerGolden c)
    {
        var input = new TaylorNBodyInput
        {
            Names = new[] { "A", "B" },
            Mu = new[] { c.Mu * 0.5d, c.Mu * 0.5d },
            P0 = c.P0,
            V0 = c.V0
        };
        var r = TaylorNBody.RunSeconds(
            input, c.TotalSeconds, c.Order, c.Tolerance, c.Tolerance, c.TotalSeconds, 1e-12d, 0d);
        double[] pt = Golden(c.PT);
        double[] vt = Golden(c.VT);
        double dp = Distance(r.P, pt);
        double dv = Distance(r.V, vt);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-28} nbody: dp={1:E3} dv={2:E3} steps={3:N0} calls={4:N0} {5:F0} ms",
            c.Label, dp, dv, r.Steps, r.Calls, r.Ms));
        Check(double.IsFinite(dp) && double.IsFinite(dv), c.Label + " nbody finite", "dp/dv finite");
        Check(dp < c.PositionGate, c.Label + " nbody dp", dp.ToString("E3", CultureInfo.InvariantCulture));
        Check(dv < c.VelocityGate, c.Label + " nbody dv", dv.ToString("E3", CultureInfo.InvariantCulture));
        CheckReferenceMargin(c.Label + " nbody", Math.Max(dp, dv), 1e-40d);
    }

    private static int Test507_KeplerGolden()
    {
        Console.WriteLine("--- Kepler golden: mpmath 60 digits ---");

        var ship00 = ShipCase(
            "Kepler e=0, 10000 periods", 1d, 62831.853071795864d,
            new[] { 1d, 0d, 0d }, new[] { 0d, 1d, 0d },
            new[]
            {
                "0.99999999999999999999999952822530210986811626499318",
                "-0.00000000000097136470791369796784019323523240491071247949976360",
                "0.0"
            },
            new[]
            {
                "0.00000000000097136470791369796784019323523240491071247949976360",
                "0.99999999999999999999999952822530210986811626499318",
                "0.0"
            });

        var ship09 = ShipCase(
            "Kepler e=0.9, 1000 periods", 1d, 6283.1853071795795d,
            new[] { 0.09999999999999998d, 0d, 0d }, new[] { 0d, 4.358898943540674d, 0d },
            new[]
            {
                "0.099999999999999977795539432164323325910383647557567",
                "0.00000000000016919328423118467969003196266166394064546261424126",
                "0.0"
            },
            new[]
            {
                "-0.0000000000038815601467867792514660053202472802678242125316044",
                "4.3588989435406739758604966387494210107083367148478",
                "0.0"
            });

        var ship099 = ShipCase(
            "Kepler e=0.99, 1000 periods", 1d, 6283.185307179493d,
            new[] { 0.010000000000000009d, 0d, 0d }, new[] { 0d, 14.106735979665878d, 0d },
            new[]
            {
                "0.010000000000000008881732849162012514977487483110357",
                "-0.0000000000014295607723158804152423118877007949172005436444678",
                "0.0"
            },
            new[]
            {
                "0.0000000010133887629119273034890065081145049902060281462693",
                "14.106735979665877778116152358071374581072676292422",
                "0.0"
            });

        var nbody09 = NBodyCase(
            "Two-body e=0.9, 1000 periods", 2d, 4442.8829381583555d,
            new[]
            {
                -0.04999999999999999d, 0d, 0d,
                0.04999999999999999d, 0d, 0d
            },
            new[]
            {
                0d, -3.0822070014844885d, 0d,
                0d, 3.0822070014844885d, 0d
            },
            new[]
            {
                "-0.049999999999999988897765833256160585144236108536815",
                "-0.00000000000086307214765743511010118965486174030013328862086488",
                "0.0",
                "0.049999999999999988897765833256160585144236108536815",
                "0.00000000000086307214765743511010118965486174030013328862086488",
                "0.0"
            },
            new[]
            {
                "0.000000000028001758066273700266594996413282816476302894197706",
                "-3.0822070014844884511260118189196358524865030340670",
                "0.0",
                "-0.000000000028001758066273700266594996413282816476302894197706",
                "3.0822070014844884511260118189196358524865030340670",
                "0.0"
            });

        var nbody099 = NBodyCase(
            "Two-body e=0.99, 1000 periods", 2d, 4442.882938158194d,
            new[]
            {
                -0.0050000000000000044d, 0d, 0d,
                0.0050000000000000044d, 0d, 0d
            },
            new[]
            {
                0d, -9.974968671629997d, 0d,
                0d, 9.974968671629997d, 0d
            },
            new[]
            {
                "-0.0050000000000000044407259736715663607302372300729737",
                "-0.0000000000018182090359169491488597503092096211278108580764855",
                "0.0",
                "0.0050000000000000044407259736715663607302372300729737",
                "0.0000000000018182090359169491488597503092096211278108580764855",
                "0.0"
            },
            new[]
            {
                "0.0000000018227716755523752058318214001027160837566814774351",
                "-9.9749686716299965635340432190269446109334918770461",
                "0.0",
                "-0.0000000018227716755523752058318214001027160837566814774351",
                "9.9749686716299965635340432190269446109334918770461",
                "0.0"
            });

        ship00.Order = 16;
        ship00.Tolerance = 1e-14d;
        ship09.Order = 16;
        ship09.Tolerance = 1e-14d;
        ship099.Order = 20;
        ship099.Tolerance = 1e-16d;
        ship099.PositionGate = 1e-5d;
        ship099.VelocityGate = 1e-3d;
        nbody09.Order = 16;
        nbody09.Tolerance = 1e-14d;
        nbody099.Order = 20;
        nbody099.Tolerance = 1e-16d;
        nbody099.PositionGate = 1e-6d;
        nbody099.VelocityGate = 1e-4d;

        RunShipGolden(ship00);
        RunShipGolden(ship09);
        RunShipGolden(ship099);
        RunNBodyGolden(nbody09);
        RunNBodyGolden(nbody099);

        Console.WriteLine("--- Kepler golden complete ---");
        return 0;
    }
}




