using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

internal static partial class P1bTests
{
    private static StudyNBody JplRingNBody(
        JplFile jpl, string[] names, int count, double totalGm, double rInAu, double rOutAu)
    {
        return JplAppendRing(JplNBody(jpl, names, true, true), count, totalGm, rInAu, rOutAu);
    }

    private static StudyNBody JplAppendRing(
        StudyNBody nb, int count, double totalGm, double rInAu, double rOutAu)
    {
        int n0 = nb.Count;
        var ext = new StudyNBody
        {
            Names = new string[n0 + count],
            Mu = new double[n0 + count],
            P0 = new double[3 * (n0 + count)],
            V0 = new double[3 * (n0 + count)]
        };
        Array.Copy(nb.Names, ext.Names, n0);
        Array.Copy(nb.Mu, ext.Mu, n0);
        Array.Copy(nb.P0, ext.P0, 3 * n0);
        Array.Copy(nb.V0, ext.V0, 3 * n0);

        double gmEach = totalGm / count;
        double rIn = rInAu * StudyAU;
        double rOut = rOutAu * StudyAU;
        double sunMu = nb.Mu[0];
        double sx = nb.P0[0];
        double sy = nb.P0[1];
        double sz = nb.P0[2];
        double svx = nb.V0[0];
        double svy = nb.V0[1];
        double svz = nb.V0[2];
        const double goldenAngle = 2.3999632297286533d;
        for (int k = 0; k < count; k++)
        {
            // Uniform surface density in the annulus, golden-angle phases.
            double u = (k + 0.5d) / count;
            double r = Math.Sqrt((rIn * rIn) + (u * ((rOut * rOut) - (rIn * rIn))));
            double theta = goldenAngle * k;
            double cos = Math.Cos(theta);
            double sin = Math.Sin(theta);
            double v = Math.Sqrt(sunMu / r);
            int i = n0 + k;
            ext.Names[i] = "Belt" + k.ToString("D2", CultureInfo.InvariantCulture);
            ext.Mu[i] = gmEach;
            ext.P0[3 * i] = sx + (r * cos);
            ext.P0[(3 * i) + 1] = sy + (r * sin);
            ext.P0[(3 * i) + 2] = sz;
            ext.V0[3 * i] = svx - (v * sin);
            ext.V0[(3 * i) + 1] = svy + (v * cos);
            ext.V0[(3 * i) + 2] = svz;
        }

        return ext;
    }

    private static int Test535_RingBelt()
    {
        Console.WriteLine("--- 10 bodies vs 10 + main-belt ring (GM 2.3e11), 10 years ---");
        Console.WriteLine("expectation: the belt removes most of the 18.75 km Jupiter and 11.20 km Saturn residuals.");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] ten =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        double ringGm = 2.3e11d;
        StudyNBody nb10 = JplNBody(jpl, ten, true, true);
        var r10 = JplDop853(nb10, 3650d, 1e-6d, 1e-9d, 1e-13d, true);
        string[] bodies = { "Jupiter", "Saturn", "Uranus", "Neptune", "Earth" };
        Console.WriteLine("ring: GM={0:E2} m^3/s^2, 2.2-3.3 AU", ringGm);
        Console.WriteLine("body        10-body(km)");
        double[] dp10 = new double[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            dp10[i] = JplBodyError(jpl, nb10, r10.p, 3650d, bodies[i], false);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture, "{0,-11} {1,11:F2}", bodies[i], dp10[i] / 1000d));
        }

        int[] counts = { 64, 128 };
        double jupiterCoarse = 0d;
        double jupiterFine = 0d;
        double saturnFine = 0d;
        Console.WriteLine("ring   body        +belt(km)   change(km)");
        for (int c = 0; c < counts.Length; c++)
        {
            int ringCount = counts[c];
            StudyNBody nbBelt = JplRingNBody(jpl, ten, ringCount, ringGm, 2.2d, 3.3d);
            var rb = JplDop853(nbBelt, 3650d, 1e-6d, 1e-9d, 1e-13d, true);
            for (int i = 0; i < bodies.Length; i++)
            {
                double dpBelt = JplBodyError(jpl, nbBelt, rb.p, 3650d, bodies[i], false);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-6} {1,-11} {2,10:F2}   {3,10:F2}",
                    ringCount, bodies[i], dpBelt / 1000d, (dpBelt - dp10[i]) / 1000d));
                Check(double.IsFinite(dpBelt), "belt " + ringCount + " " + bodies[i] + " finite",
                    dpBelt.ToString("E2", CultureInfo.InvariantCulture));
                if (bodies[i] == "Jupiter")
                {
                    if (c == 0)
                    {
                        jupiterCoarse = dpBelt;
                    }
                    else
                    {
                        jupiterFine = dpBelt;
                    }
                }
                else if (bodies[i] == "Saturn" && c == counts.Length - 1)
                {
                    saturnFine = dpBelt;
                }
            }
        }

        Check(Math.Abs(jupiterFine - jupiterCoarse) < 500d, "belt ring converged at km level",
            ((jupiterFine - jupiterCoarse) / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " km");
        Check(jupiterFine < dp10[0], "belt improves Jupiter",
            (dp10[0] / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " -> "
            + (jupiterFine / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " km");
        Check(saturnFine < dp10[1], "belt improves Saturn",
            (dp10[1] / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " -> "
            + (saturnFine / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " km");
        Console.WriteLine("--- Belt ring probe complete ---");
        return 0;
    }
}
