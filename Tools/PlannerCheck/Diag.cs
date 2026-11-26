using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Galilego.Core;
using Galilego.Planning;

// Focused diagnostics: capture geometry, orbit prep, flyby draft knife-edge.
internal static class Diag
{
    private static ulong _s = 0xDEADBEEFCAFEBABEUL;
    private static double Rnd()
    {
        _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17;
        return (_s >> 11) * (1.0 / 9007199254740992.0);
    }
    private static double Rnd(double lo, double hi) { return lo + ((hi - lo) * Rnd()); }
    private static string D(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

    private static Vector3d RandomUnit()
    {
        double z = Rnd(-1d, 1d);
        double phi = Rnd(0d, 2d * Math.PI);
        double r = Math.Sqrt(Math.Max(0d, 1d - (z * z)));
        return new Vector3d(r * Math.Cos(phi), r * Math.Sin(phi), z);
    }

    private const double Au = 1.495978707e11;

    internal static void Run(string outPath)
    {
        var sb = new StringBuilder();
        // ---------------- capture geometry
        double mu = 4.282837e13, rp = 3.3895e6 + 400e3;
        for (int i = 0; i < 20; i++)
        {
            Vector3d vinf = RandomUnit() * Rnd(500, 12000);
            Vector3d hpref = RandomUnit();
            bool ok = CaptureGeometry.TryIncoming(mu, vinf, rp, hpref, out HyperbolaAtPeriapsis h);
            sb.AppendLine("CAP," + i + "," + (ok ? "1" : "0") + ","
                + D(vinf.X) + "," + D(vinf.Y) + "," + D(vinf.Z) + ","
                + D(h.AxisX.X) + "," + D(h.AxisX.Y) + "," + D(h.AxisX.Z) + ","
                + D(h.MotionY.X) + "," + D(h.MotionY.Y) + "," + D(h.MotionY.Z) + ","
                + D(h.Normal.X) + "," + D(h.Normal.Y) + "," + D(h.Normal.Z) + ","
                + D(h.Rp) + "," + D(h.Vp) + "," + D(h.Eccentricity));
        }

        // ---------------- Hohmann prep
        double muE = 3.986004418e14;
        for (int i = 0; i < 10; i++)
        {
            double r1 = 6.571e6 + i * 1.5e6;
            double r2 = r1 + Rnd(-2e6, 6e6);
            Vector3d r0 = new Vector3d(r1, 0, 0);
            Vector3d v0 = new Vector3d(0, Math.Sqrt(muE / r1), 0);
            bool ok = OrbitPrep.TryHohmann(muE, 1000d, r0, v0, r2, 0.02, out PrepPlan p);
            sb.AppendLine("PREP," + i + "," + (ok ? "1" : "0") + "," + D(r1) + "," + D(r2) + ","
                + D(p.Dv1.Magnitude) + "," + D(p.Dv2.Magnitude) + "," + D(p.T2 - p.T1) + ","
                + D(p.RAfter.Magnitude) + "," + D(p.VAfter.Magnitude) + ","
                + D(p.VAfter.X) + "," + D(p.VAfter.Y) + "," + D(p.VAfter.Z));
        }

        System.IO.File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine("wrote " + outPath);
    }
}

