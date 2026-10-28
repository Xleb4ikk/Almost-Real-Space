using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static partial class P1bTests
{
    private static (double angle, double a, double e) JplPerihelion(
        StudyNBody nb, double[] p, double[] v, double mu)
    {
        int sun = JplIndex(nb, "Sun");
        int planet = JplIndex(nb, nb.Names[1]);
        double rx = p[3 * planet] - p[3 * sun];
        double ry = p[(3 * planet) + 1] - p[(3 * sun) + 1];
        double rz = p[(3 * planet) + 2] - p[(3 * sun) + 2];
        double vx = v[3 * planet] - v[3 * sun];
        double vy = v[(3 * planet) + 1] - v[(3 * sun) + 1];
        double vz = v[(3 * planet) + 2] - v[(3 * sun) + 2];
        double r2 = (rx * rx) + (ry * ry) + (rz * rz);
        double r = Math.Sqrt(r2);
        double v2 = (vx * vx) + (vy * vy) + (vz * vz);
        double a = 1d / ((2d / r) - (v2 / mu));
        double hx = (ry * vz) - (rz * vy);
        double hy = (rz * vx) - (rx * vz);
        double hz = (rx * vy) - (ry * vx);
        double ex = (((vy * hz) - (vz * hy)) / mu) - (rx / r);
        double ey = (((vz * hx) - (vx * hz)) / mu) - (ry / r);
        double ez = (((vx * hy) - (vy * hx)) / mu) - (rz / r);
        double e = Math.Sqrt((ex * ex) + (ey * ey) + (ez * ez));
        return (Math.Atan2(ey, ex), a, e);
    }

    private static double JplPrecessionFit(
        StudyNBody nb, double years, double h, bool pn)
    {
        int n = nb.Count;
        int length = 3 * n;
        var p = (double[])nb.P0.Clone();
        var v = (double[])nb.V0.Clone();
        var a = new double[length];
        var p2 = new double[length];
        var v2 = new double[length];
        var p3 = new double[length];
        var v3 = new double[length];
        var p4 = new double[length];
        var v4 = new double[length];
        var kp = new double[4][];
        var kv = new double[4][];
        for (int i = 0; i < 4; i++)
        {
            kp[i] = new double[length];
            kv[i] = new double[length];
        }

        double T = years * 365.25d * 86400d;
        double sampleEvery = 5d * 86400d;
        var ts = new List<double>();
        var angles = new List<double>();
        double mu = nb.Mu[0];
        ts.Add(0d);
        angles.Add(JplPerihelion(nb, p, v, mu).angle);
        JplAcc1Pn(p, v, a, nb.Mu, n, pn);
        double t = 0d;
        double nextSample = sampleEvery;
        while (t < T - 1e-9d)
        {
            double step = Math.Min(h, T - t);
            for (int i = 0; i < length; i++)
            {
                kp[0][i] = v[i];
                kv[0][i] = a[i];
                p2[i] = p[i] + (0.5d * step * kp[0][i]);
                v2[i] = v[i] + (0.5d * step * kv[0][i]);
            }

            JplAcc1Pn(p2, v2, a, nb.Mu, n, pn);
            for (int i = 0; i < length; i++)
            {
                kp[1][i] = v2[i];
                kv[1][i] = a[i];
                p3[i] = p[i] + (0.5d * step * kp[1][i]);
                v3[i] = v[i] + (0.5d * step * kv[1][i]);
            }

            JplAcc1Pn(p3, v3, a, nb.Mu, n, pn);
            for (int i = 0; i < length; i++)
            {
                kp[2][i] = v3[i];
                kv[2][i] = a[i];
                p4[i] = p[i] + (step * kp[2][i]);
                v4[i] = v[i] + (step * kv[2][i]);
            }

            JplAcc1Pn(p4, v4, a, nb.Mu, n, pn);
            for (int i = 0; i < length; i++)
            {
                kp[3][i] = v4[i];
                kv[3][i] = a[i];
                p[i] += (step / 6d) * (kp[0][i] + (2d * kp[1][i]) + (2d * kp[2][i]) + kp[3][i]);
                v[i] += (step / 6d) * (kv[0][i] + (2d * kv[1][i]) + (2d * kv[2][i]) + kv[3][i]);
            }

            t += step;
            if (t >= nextSample - 1e-9d)
            {
                ts.Add(t);
                angles.Add(JplPerihelion(nb, p, v, mu).angle);
                nextSample += sampleEvery;
            }

            JplAcc1Pn(p, v, a, nb.Mu, n, pn);
        }

        for (int i = 1; i < angles.Count; i++)
        {
            double delta = angles[i] - angles[i - 1];
            if (delta > Math.PI)
            {
                angles[i] -= 2d * Math.PI;
            }
            else if (delta < -Math.PI)
            {
                angles[i] += 2d * Math.PI;
            }

            angles[i] = angles[i - 1] + ((angles[i] - angles[i - 1]));
        }

        double meanT = 0d;
        double meanA = 0d;
        for (int i = 0; i < ts.Count; i++)
        {
            meanT += ts[i];
            meanA += angles[i];
        }

        meanT /= ts.Count;
        meanA /= angles.Count;
        double num = 0d;
        double den = 0d;
        for (int i = 0; i < ts.Count; i++)
        {
            double dt = ts[i] - meanT;
            num += dt * (angles[i] - meanA);
            den += dt * dt;
        }

        return num / den;
    }

    private static int Test532_Precession()
    {
        Console.WriteLine("--- 1PN perihelion precession: Sun + planet, 100 years ---");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        double years = 100d;
        double days = years * 365.25d;
        double c2 = 8.9875517873681764e16d;
        double arcsec = 180d / Math.PI * 3600d;
        string[] planets = { "Mercury", "Venus", "Earth" };
        Console.WriteLine("method: fit of the perihelion angle over 5-day samples, 100 years.");
        Console.WriteLine("The endpoint row is diagnostic: it includes the periodic eccentricity-vector term.");
        Console.WriteLine("planet    rtol      measured(\")   analytic(\")   ratio");
        double[] rtols = { 1e-13d, 1e-14d };
        for (int i = 0; i < planets.Length; i++)
        {
            string name = planets[i];
            StudyNBody nb = JplNBody(jpl, new[] { name });
            double mu = nb.Mu[0];
            var orbit0 = JplPerihelion(nb, nb.P0, nb.V0, mu);
            double period = 2d * Math.PI * Math.Sqrt(
                (orbit0.a * orbit0.a * orbit0.a) / mu);
            double orbits = (days * 86400d) / period;
            double analytic = 6d * Math.PI * mu / (c2 * orbit0.a * (1d - (orbit0.e * orbit0.e)));
            analytic *= orbits;
            for (int r = 0; r < rtols.Length; r++)
            {
                var newton = JplDop853(nb, days, 1e-6d, 1e-9d, rtols[r], false);
                var pn = JplDop853(nb, days, 1e-6d, 1e-9d, rtols[r], true);
                var orbitN = JplPerihelion(nb, newton.p, newton.v, mu);
                var orbitP = JplPerihelion(nb, pn.p, pn.v, mu);
                double measured = orbitP.angle - orbitN.angle;
                measured = Math.Atan2(Math.Sin(measured), Math.Cos(measured));
                double ratio = measured / analytic;
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-9} {1,8:E0} {2,12:F4}   {3,12:F4}   {4,7:F4}",
                    name, rtols[r], measured * arcsec, analytic * arcsec, ratio));
                Check(double.IsFinite(measured) && double.IsFinite(analytic),
                    "precession " + name + " finite", ratio.ToString("F3", CultureInfo.InvariantCulture));
            }

            double fitNewton = JplPrecessionFit(nb, years, 3600d, false);
            double fitPn = JplPrecessionFit(nb, years, 3600d, true);
            double fitMeasured = (fitPn - fitNewton) * (days * 86400d);
            double fitRatio = fitMeasured / analytic;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-9} {1,8} {2,12:F4}   {3,12:F4}   {4,7:F4}",
                name, "fit", fitMeasured * arcsec, analytic * arcsec, fitRatio));
            double tol = name == "Mercury" ? 0.05d : 0.10d;
            Check(Math.Abs(fitRatio - 1d) < tol, "precession " + name + " fit ratio",
                fitRatio.ToString("F4", CultureInfo.InvariantCulture));
        }

        Console.WriteLine("--- Precession probe complete ---");
        return 0;
    }
}
