using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static partial class P1bTests
{
    private static (double dt, double after) JplTimeOffset(
        JplFile jpl, StudyNBody nb, double[] p, double days, string body)
    {
        int sun = JplIndex(nb, "Sun");
        int index = JplIndex(nb, body);
        JplState state = jpl.Bodies.First(b => b.Name == body)
            .States.First(s => Math.Abs(s.Days - days) < 1e-9d);
        double dx = (p[3 * index] - p[3 * sun]) - state.P[0];
        double dy = (p[(3 * index) + 1] - p[(3 * sun) + 1]) - state.P[1];
        double dz = (p[(3 * index) + 2] - p[(3 * sun) + 2]) - state.P[2];
        double vx = state.V[0];
        double vy = state.V[1];
        double vz = state.V[2];
        double v2 = (vx * vx) + (vy * vy) + (vz * vz);
        double dt = ((dx * vx) + (dy * vy) + (dz * vz)) / v2;
        double rx = dx - (vx * dt);
        double ry = dy - (vy * dt);
        double rz = dz - (vz * dt);
        return (dt, Math.Sqrt((rx * rx) + (ry * ry) + (rz * rz)));
    }

    private static int Test534_JupiterVariants()
    {
        Console.WriteLine("--- Jupiter: barycenter vs center, planet vs system GM, 10 years ---");
        Console.WriteLine("expected before the run:");
        Console.WriteLine("  (b)-(a): Jupiter fractions of km, Saturn a few km; tens of km would be unexpected.");
        Console.WriteLine("  (c) vs center: small; (d) vs center: ~200 km (barycenter wobble).");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] barycenter =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        string[] centerMoons =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "JupiterCenter", "Io", "Europa", "Ganymede", "Callisto",
            "Saturn", "Uranus", "Neptune"
        };
        string[] centerOnly =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "JupiterCenter", "Saturn", "Uranus", "Neptune"
        };

        var a = JplNBody(jpl, barycenter, false, true);
        var b = JplNBody(jpl, barycenter, true, true);
        var c = JplNBody(jpl, centerMoons, true, true);
        var d = JplNBody(jpl, centerOnly, true, true);
        var bp = JplNBody(jpl, centerOnly, true, true, true);
        var ra = JplDop853(a, 3650d, 1e-6d, 1e-9d, 1e-13d, false);
        var rb = JplDop853(b, 3650d, 1e-6d, 1e-9d, 1e-13d, false);
        var rc = JplDop853(c, 3650d, 1e-6d, 1e-9d, 1e-13d, false);
        var rd = JplDop853(d, 3650d, 1e-6d, 1e-9d, 1e-13d, false);
        var rbp = JplDop853(bp, 3650d, 1e-6d, 1e-9d, 1e-13d, false);
        var rbPn = JplDop853(b, 3650d, 1e-6d, 1e-9d, 1e-13d, true);

        double ja = JplBodyError(jpl, a, ra.p, 3650d, "Jupiter", false);
        double jb = JplBodyError(jpl, b, rb.p, 3650d, "Jupiter", false);
        double jc = JplBodyError(jpl, c, rc.p, 3650d, "JupiterCenter", false);
        double jd = JplBodyError(jpl, d, rd.p, 3650d, "JupiterCenter", false);
        double jbp = JplBodyError(jpl, bp, rbp.p, 3650d, "JupiterCenter", false);
        double sa = JplBodyError(jpl, a, ra.p, 3650d, "Saturn", false);
        double sb = JplBodyError(jpl, b, rb.p, 3650d, "Saturn", false);
        double jbPn = JplBodyError(jpl, b, rbPn.p, 3650d, "Jupiter", false);
        double sbPn = JplBodyError(jpl, b, rbPn.p, 3650d, "Saturn", false);
        Console.WriteLine("variant  Jupiter(km)   Saturn(km)   steps");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "(a)      {0,11:F2}   {1,10:F2}   {2}", ja / 1000d, sa / 1000d, ra.steps));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "(b)      {0,11:F2}   {1,10:F2}   {2}", jb / 1000d, sb / 1000d, rb.steps));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "(b')     {0,11:F2}   {1,10}   {2}", jbp / 1000d, "-", rbp.steps));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "(b+1PN)  {0,11:F2}   {1,10:F2}   {2}", jbPn / 1000d, sbPn / 1000d, rbPn.steps));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "(c)      {0,11:F2}   {1,10}   {2}", jc / 1000d, "-", rc.steps));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "(d)      {0,11:F2}   {1,10}   {2}", jd / 1000d, "-", rd.steps));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "deltas: (b)-(a) Jupiter={0:F2} km, Saturn={1:F2} km; (c)-(d)={2:F2} km; (b')-(d)={3:F2} km",
            (jb - ja) / 1000d, (sb - sa) / 1000d, (jc - jd) / 1000d, (jbp - jd) / 1000d));
        Check(double.IsFinite(ja) && double.IsFinite(jb) && double.IsFinite(jc) && double.IsFinite(jd),
            "variants finite", jb.ToString("E2", CultureInfo.InvariantCulture));

        Console.WriteLine("time-offset diagnostic (heliocentric):");
        var offJ = JplTimeOffset(jpl, b, rb.p, 3650d, "Jupiter");
        var offS = JplTimeOffset(jpl, b, rb.p, 3650d, "Saturn");
        var offE = JplTimeOffset(jpl, b, rb.p, 3650d, "Earth");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  (b) Jupiter dt={0:F2} s, after={1:F2} km; Saturn dt={2:F2} s, after={3:F2} km; Earth dt={4:F2} s, after={5:F2} km",
            offJ.dt, offJ.after / 1000d, offS.dt, offS.after / 1000d, offE.dt, offE.after / 1000d));
        var offC = JplTimeOffset(jpl, c, rc.p, 3650d, "JupiterCenter");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  (c) JupiterCenter dt={0:F2} s, after={1:F2} km", offC.dt, offC.after / 1000d));

        Console.WriteLine("GM fit, Jupiter system GM multiplier (planet GM 1-sigma ~1.0e-8 relative):");
        double[] ks = { 1d - 5e-8d, 1d - 2e-8d, 1d, 1d + 2e-8d, 1d + 5e-8d };
        double bestJ = double.MaxValue;
        double bestKJ = 1d;
        for (int i = 0; i < ks.Length; i++)
        {
            var fit = JplNBody(jpl, barycenter, true, true);
            int jupiter = JplIndex(fit, "Jupiter");
            fit.Mu[jupiter] *= ks[i];
            var rf = JplDop853(fit, 3650d, 1e-6d, 1e-9d, 1e-13d, false);
            double err = JplBodyError(jpl, fit, rf.p, 3650d, "Jupiter", false);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  k={0:E2}: Jupiter={1:F2} km", ks[i] - 1d, err / 1000d));
            if (err < bestJ)
            {
                bestJ = err;
                bestKJ = ks[i];
            }
        }

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  best Jupiter k-1={0:E2}, residual={1:F2} km", bestKJ - 1d, bestJ / 1000d));
        Console.WriteLine("GM fit, Saturn system GM multiplier:");
        double bestS = double.MaxValue;
        double bestKS = 1d;
        for (int i = 0; i < ks.Length; i++)
        {
            var fit = JplNBody(jpl, barycenter, true, true);
            int saturn = JplIndex(fit, "Saturn");
            fit.Mu[saturn] *= ks[i];
            var rf = JplDop853(fit, 3650d, 1e-6d, 1e-9d, 1e-13d, false);
            double err = JplBodyError(jpl, fit, rf.p, 3650d, "Saturn", false);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  k={0:E2}: Saturn={1:F2} km", ks[i] - 1d, err / 1000d));
            if (err < bestS)
            {
                bestS = err;
                bestKS = ks[i];
            }
        }

        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  best Saturn k-1={0:E2}, residual={1:F2} km", bestKS - 1d, bestS / 1000d));
        Console.WriteLine("--- Jupiter variants complete ---");
        return 0;
    }
}
