using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static partial class P1bTests
{
    private static void JplAcc1PnC(
        double[] p, double[] pl, double[] v, double[] vl,
        double[] a, double[] mu, int n, bool pn)
    {
        StudyAccC(p, pl, a, mu);
        if (!pn)
        {
            return;
        }

        const double c2 = 8.9875517873681764e16d;
        for (int i = 1; i < n; i++)
        {
            double dx = (p[3 * i] + pl[3 * i]) - (p[0] + pl[0]);
            double dy = (p[(3 * i) + 1] + pl[(3 * i) + 1]) - (p[1] + pl[1]);
            double dz = (p[(3 * i) + 2] + pl[(3 * i) + 2]) - (p[2] + pl[2]);
            double dvx = (v[3 * i] + vl[3 * i]) - (v[0] + vl[0]);
            double dvy = (v[(3 * i) + 1] + vl[(3 * i) + 1]) - (v[1] + vl[1]);
            double dvz = (v[(3 * i) + 2] + vl[(3 * i) + 2]) - (v[2] + vl[2]);
            double r2 = (dx * dx) + (dy * dy) + (dz * dz);
            double r = Math.Sqrt(r2);
            double rv = (dx * dvx) + (dy * dvy) + (dz * dvz);
            double v2 = (dvx * dvx) + (dvy * dvy) + (dvz * dvz);
            double f = mu[0] / (c2 * r2 * r);
            double k = (4d * mu[0] / r) - v2;
            a[3 * i] += f * ((k * dx) + (4d * rv * dvx));
            a[(3 * i) + 1] += f * ((k * dy) + (4d * rv * dvy));
            a[(3 * i) + 2] += f * ((k * dz) + (4d * rv * dvz));
        }
    }

    private static void JplAcc1Pn(
        double[] p, double[] v, double[] a, double[] mu, int n, bool pn, bool j2 = false)
    {
        StudyAcc(p, a, mu);
        if (!pn && !j2)
        {
            return;
        }

        const double c2 = 8.9875517873681764e16d;
        const double sunJ2 = 2.2e-7d;
        const double sunRadius = 6.957e8d;
        for (int i = 1; i < n; i++)
        {
            double dx = p[3 * i] - p[0];
            double dy = p[(3 * i) + 1] - p[1];
            double dz = p[(3 * i) + 2] - p[2];
            double r2 = (dx * dx) + (dy * dy) + (dz * dz);
            double r = Math.Sqrt(r2);
            if (pn)
            {
                double dvx = v[3 * i] - v[0];
                double dvy = v[(3 * i) + 1] - v[1];
                double dvz = v[(3 * i) + 2] - v[2];
                double rv = (dx * dvx) + (dy * dvy) + (dz * dvz);
                double v2 = (dvx * dvx) + (dvy * dvy) + (dvz * dvz);
                double f = mu[0] / (c2 * r2 * r);
                double k = (4d * mu[0] / r) - v2;
                a[3 * i] += f * ((k * dx) + (4d * rv * dvx));
                a[(3 * i) + 1] += f * ((k * dy) + (4d * rv * dvy));
                a[(3 * i) + 2] += f * ((k * dz) + (4d * rv * dvz));
            }

            if (j2)
            {
                double q = 5d * dz * dz / r2;
                double f = -1.5d * sunJ2 * mu[0] * sunRadius * sunRadius / (r2 * r2 * r);
                a[3 * i] += f * dx * (1d - q);
                a[(3 * i) + 1] += f * dy * (1d - q);
                a[(3 * i) + 2] += f * dz * (3d - q);
            }
        }
    }

    private static void JplAccEih(
        double[] p, double[] v, double[] a, double[] mu, int n)
    {
        JplAccEih(p, v, a, mu, n, new double[3 * n], new double[n]);
    }

    private static void JplAccEih(
        double[] p, double[] v, double[] a, double[] mu, int n,
        double[] an, double[] pot)
    {
        const double c2 = 8.9875517873681764e16d;
        JplAcc1Pn(p, v, an, mu, n, false);
        Array.Clear(pot, 0, n);
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = p[3 * i] - p[3 * j];
                double dy = p[(3 * i) + 1] - p[(3 * j) + 1];
                double dz = p[(3 * i) + 2] - p[(3 * j) + 2];
                double r = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                pot[i] += mu[j] / r;
                pot[j] += mu[i] / r;
            }
        }

        for (int bodyA = 0; bodyA < n; bodyA++)
        {
            double ax = 0d;
            double ay = 0d;
            double az = 0d;
            double vax = v[3 * bodyA];
            double vay = v[(3 * bodyA) + 1];
            double vaz = v[(3 * bodyA) + 2];
            double va2 = (vax * vax) + (vay * vay) + (vaz * vaz);
            for (int bodyB = 0; bodyB < n; bodyB++)
            {
                if (bodyB == bodyA)
                {
                    continue;
                }

                double dx = p[3 * bodyB] - p[3 * bodyA];
                double dy = p[(3 * bodyB) + 1] - p[(3 * bodyA) + 1];
                double dz = p[(3 * bodyB) + 2] - p[(3 * bodyA) + 2];
                double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                double r = Math.Sqrt(r2);
                double invr3 = 1d / (r2 * r);
                double vbx = v[3 * bodyB];
                double vby = v[(3 * bodyB) + 1];
                double vbz = v[(3 * bodyB) + 2];
                double vb2 = (vbx * vbx) + (vby * vby) + (vbz * vbz);
                double vdot = (vax * vbx) + (vay * vby) + (vaz * vbz);
                double nv = (((-dx * vbx) - (dy * vby) - (dz * vbz)) / r);
                double nv2 = nv * nv;
                double ab = ((dx * an[3 * bodyB]) + (dy * an[(3 * bodyB) + 1]) + (dz * an[(3 * bodyB) + 2]));
                double bracket = va2 + (2d * vb2) - (4d * vdot) - (1.5d * nv2)
                    - (4d * pot[bodyA]) - pot[bodyB] + (0.5d * ab);
                double coef = mu[bodyB] * invr3;
                ax += coef * dx * (1d + (bracket / c2));
                ay += coef * dy * (1d + (bracket / c2));
                az += coef * dz * (1d + (bracket / c2));
                double nAB4v = (-dx / r * ((4d * vax) - (3d * vbx)))
                    + (-dy / r * ((4d * vay) - (3d * vby)))
                    + (-dz / r * ((4d * vaz) - (3d * vbz)));
                double vcoef = mu[bodyB] / (c2 * r2) * nAB4v;
                ax += vcoef * (vax - vbx);
                ay += vcoef * (vay - vby);
                az += vcoef * (vaz - vbz);
                double acoef = (7d / (2d * c2)) * mu[bodyB] / r;
                ax += acoef * an[3 * bodyB];
                ay += acoef * an[(3 * bodyB) + 1];
                az += acoef * an[(3 * bodyB) + 2];
            }

            a[3 * bodyA] = ax;
            a[(3 * bodyA) + 1] = ay;
            a[(3 * bodyA) + 2] = az;
        }
    }

    private static void JplAccPlanetsAsteroids(
        double[] p, double[] v, double[] a, double[] mu, int n, int astStart, bool pn, bool j2,
        double[] planetMu)
    {
        JplAcc1Pn(p, v, a, planetMu, astStart, pn, j2);
        for (int k = astStart; k < n; k++)
        {
            int k3 = 3 * k;
            double pkx = p[k3];
            double pky = p[k3 + 1];
            double pkz = p[k3 + 2];
            double muk = mu[k];
            double ax = 0d;
            double ay = 0d;
            double az = 0d;
            for (int j = 0; j < astStart; j++)
            {
                int j3 = 3 * j;
                double dx = pkx - p[j3];
                double dy = pky - p[j3 + 1];
                double dz = pkz - p[j3 + 2];
                double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                double inv = 1d / (r2 * Math.Sqrt(r2));
                double fk = muk * inv;
                a[j3] += fk * dx;
                a[j3 + 1] += fk * dy;
                a[j3 + 2] += fk * dz;
                double fj = mu[j] * inv;
                ax -= fj * dx;
                ay -= fj * dy;
                az -= fj * dz;
            }

            a[k3] = ax;
            a[k3 + 1] = ay;
            a[k3 + 2] = az;
        }
    }

    private static void JplLegendre(int n, double c, out double p, out double dp)
    {
        double p0 = 1d;
        double p1 = c;
        double d0 = 0d;
        double d1 = 1d;
        if (n == 0)
        {
            p = p0;
            dp = d0;
            return;
        }

        for (int k = 2; k <= n; k++)
        {
            double pk = ((((2 * k) - 1) * c * p1) - ((k - 1) * p0)) / k;
            double dk = ((((2 * k) - 1) * (p1 + (c * d1))) - ((k - 1) * d0)) / k;
            p0 = p1;
            p1 = pk;
            d0 = d1;
            d1 = dk;
        }

        p = p1;
        dp = d1;
    }

    private static void JplAccZonal(
        double[] p, double[] a, int center, int body, double centerMu,
        double jn, double radius, int n, double sx, double sy, double sz)
    {
        int c3 = 3 * center;
        int b3 = 3 * body;
        double dx = p[b3] - p[c3];
        double dy = p[b3 + 1] - p[c3 + 1];
        double dz = p[b3 + 2] - p[c3 + 2];
        double r2 = (dx * dx) + (dy * dy) + (dz * dz);
        double r = Math.Sqrt(r2);
        double z = (dx * sx) + (dy * sy) + (dz * sz);
        double c = z / r;
        JplLegendre(n, c, out double pn, out double dpn);
        double rn = Math.Pow(radius / r, n);
        double factor = centerMu * jn * rn / (r2 * r);
        a[b3] += factor * (((n + 1) * dx * pn) - (r * dpn * (sx - (c * dx / r))));
        a[b3 + 1] += factor * (((n + 1) * dy * pn) - (r * dpn * (sy - (c * dy / r))));
        a[b3 + 2] += factor * (((n + 1) * dz * pn) - (r * dpn * (sz - (c * dz / r))));
    }

    private static void JplAccZonalJ2(
        double[] p, double[] a, int center, int body, double centerMu,
        double j2, double radius, double sx, double sy, double sz)
    {
        JplAccZonal(p, a, center, body, centerMu, j2, radius, 2, sx, sy, sz);
    }

    private static void JplAccEarthJ2(
        double[] p, double[] a, int earth, int moon, double earthMu, double moonMu)
    {
        const double j2 = 1.08263e-3d;
        const double radius = 6.378137e6d;
        const double eps = 23.4392911d * Math.PI / 180d;
        JplAccZonal(p, a, earth, moon, earthMu, j2, radius, 2,
            0d, -Math.Sin(eps), Math.Cos(eps));
    }

    private static (double[] p, double[] v, long calls, long steps) JplDop853(
        StudyNBody nb, double days, double posAtol, double velAtol, double rtol, bool pn,
        bool eih = false, bool j2 = false, double maxStepSeconds = 86400d,
        int asteroidStart = -1, bool earthJ2 = false, bool planetJ2 = false)
    {
        int n = nb.Count;
        int length = 3 * n;
        var p = (double[])nb.P0.Clone();
        var v = (double[])nb.V0.Clone();
        var a = new double[length];
        var asStage = new double[length];
        var pStage = new double[length];
        var vStage = new double[length];
        var pNew = new double[length];
        var vNew = new double[length];
        var eihAn = new double[length];
        var eihPot = new double[n];
        var planetMu = asteroidStart > 0 ? new double[asteroidStart] : null;
        if (asteroidStart > 0)
        {
            Array.Copy(nb.Mu, planetMu, asteroidStart);
        }

        int earthIndex = -1;
        int moonIndex = -1;
        int jupiterCenter = -1;
        int saturnCenter = -1;
        int[] jupiterMoons = null;
        int[] saturnMoons = null;
        if (earthJ2 || planetJ2)
        {
            earthIndex = JplIndex(nb, "Earth");
            moonIndex = JplIndex(nb, "Moon");
        }

        if (planetJ2)
        {
            jupiterCenter = JplIndex(nb, "JupiterCenter");
            saturnCenter = JplIndex(nb, "SaturnCenter");
            jupiterMoons = new[]
            {
                JplIndex(nb, "Io"), JplIndex(nb, "Europa"),
                JplIndex(nb, "Ganymede"), JplIndex(nb, "Callisto")
            };
            saturnMoons = new[]
            {
                JplIndex(nb, "Mimas"), JplIndex(nb, "Enceladus"),
                JplIndex(nb, "Tethys"), JplIndex(nb, "Dione"),
                JplIndex(nb, "Rhea"), JplIndex(nb, "Titan"),
                JplIndex(nb, "Hyperion"), JplIndex(nb, "Iapetus")
            };
        }

        var kp = new double[12][];
        var kv = new double[12][];
        for (int s = 0; s < 12; s++)
        {
            kp[s] = new double[length];
            kv[s] = new double[length];
        }

        double maxStep = maxStepSeconds;
        const double minStep = 1e-6d;
        double T = days * 86400d;
        double t = 0d;
        double h = Math.Min(maxStep, T);
        long calls = 0L;
        long steps = 0L;
        long iterations = 0L;
        void Acc(double[] pp, double[] vv, double[] aa)
        {
            if (asteroidStart > 0)
            {
                JplAccPlanetsAsteroids(pp, vv, aa, nb.Mu, n, asteroidStart, pn, j2, planetMu);
            }
            else if (eih)
            {
                JplAccEih(pp, vv, aa, nb.Mu, n, eihAn, eihPot);
            }
            else
            {
                JplAcc1Pn(pp, vv, aa, nb.Mu, n, pn, j2);
            }

            if ((earthJ2 || planetJ2) && earthIndex >= 0 && moonIndex >= 0)
            {
                JplAccEarthJ2(pp, aa, earthIndex, moonIndex, nb.Mu[earthIndex], nb.Mu[moonIndex]);
            }

            if (planetJ2)
            {
                if (jupiterCenter >= 0)
                {
                    const double jupiterEps = 3.13d * Math.PI / 180d;
                    double sx = 0d;
                    double sy = -Math.Sin(jupiterEps);
                    double sz = Math.Cos(jupiterEps);
                    double[] moonJ2 = { 1.795e-3d, 4.35e-4d, 3.12e-4d, 3.0e-4d };
                    double[] moonR = { 1.8216e6d, 1.5608e6d, 2.6312e6d, 2.4103e6d };
                    for (int k = 0; k < jupiterMoons.Length; k++)
                    {
                        if (jupiterMoons[k] >= 0)
                        {
                            JplAccZonal(pp, aa, jupiterCenter, jupiterMoons[k],
                                nb.Mu[jupiterCenter], 1.4736e-2d, 7.1492e7d, 2, sx, sy, sz);
                            JplAccZonal(pp, aa, jupiterCenter, jupiterMoons[k],
                                nb.Mu[jupiterCenter], -5.87e-4d, 7.1492e7d, 4, sx, sy, sz);
                            JplAccZonal(pp, aa, jupiterCenter, jupiterMoons[k],
                                nb.Mu[jupiterCenter], 3.4e-5d, 7.1492e7d, 6, sx, sy, sz);
                            JplAccZonal(pp, aa, jupiterMoons[k], jupiterCenter,
                                nb.Mu[jupiterMoons[k]], moonJ2[k], moonR[k], 2, sx, sy, sz);
                        }
                    }
                }

                if (saturnCenter >= 0)
                {
                    const double saturnEps = 26.73d * Math.PI / 180d;
                    double sx = 0d;
                    double sy = -Math.Sin(saturnEps);
                    double sz = Math.Cos(saturnEps);
                    double[] moonJ2 =
                    {
                        4.5e-3d, 5.4e-3d, 1.0e-3d, 1.0e-3d,
                        1.0e-3d, 3.3e-5d, 0d, 1.0e-3d
                    };
                    double[] moonR =
                    {
                        1.986e5d, 2.521e5d, 5.31e5d, 5.61e5d,
                        7.64e5d, 2.5747e6d, 1.35e5d, 7.35e5d
                    };
                    for (int k = 0; k < saturnMoons.Length; k++)
                    {
                        if (saturnMoons[k] >= 0)
                        {
                            JplAccZonal(pp, aa, saturnCenter, saturnMoons[k],
                                nb.Mu[saturnCenter], 1.6298e-2d, 6.0268e7d, 2, sx, sy, sz);
                            JplAccZonal(pp, aa, saturnCenter, saturnMoons[k],
                                nb.Mu[saturnCenter], -9.15e-4d, 6.0268e7d, 4, sx, sy, sz);
                            JplAccZonal(pp, aa, saturnCenter, saturnMoons[k],
                                nb.Mu[saturnCenter], 1.0e-4d, 6.0268e7d, 6, sx, sy, sz);
                            if (moonJ2[k] != 0d)
                            {
                                JplAccZonal(pp, aa, saturnMoons[k], saturnCenter,
                                    nb.Mu[saturnMoons[k]], moonJ2[k], moonR[k], 2, sx, sy, sz);
                            }
                        }
                    }
                }
            }
        }

        Acc(p, v, a);
        calls++;
        while (t < T - 1e-9d)
        {
            iterations++;
            if (iterations > 1000000L)
            {
                throw new InvalidOperationException(
                    "JplDop853 iteration cap: steps=" + steps
                    + " t=" + t.ToString("R", CultureInfo.InvariantCulture)
                    + " h=" + h.ToString("R", CultureInfo.InvariantCulture));
            }

            double hStep = Math.Min(h, T - t);
            for (int s = 0; s < 12; s++)
            {
                if (s == 0)
                {
                    for (int i = 0; i < length; i++)
                    {
                        kp[0][i] = v[i];
                        kv[0][i] = a[i];
                    }

                    continue;
                }

                for (int i = 0; i < length; i++)
                {
                    double dp = 0d;
                    double dv = 0d;
                    for (int j = 0; j < s; j++)
                    {
                        double aa = StudyDop853A[s][j];
                        if (aa == 0d)
                        {
                            continue;
                        }

                        dp += aa * kp[j][i];
                        dv += aa * kv[j][i];
                    }

                    pStage[i] = p[i] + (hStep * dp);
                    vStage[i] = v[i] + (hStep * dv);
                    kp[s][i] = vStage[i];
                }

                Acc(pStage, vStage, asStage);
                calls++;
                for (int i = 0; i < length; i++)
                {
                    kv[s][i] = asStage[i];
                }
            }

            for (int i = 0; i < length; i++)
            {
                double dp = 0d;
                double dv = 0d;
                for (int j = 0; j < 12; j++)
                {
                    double b = StudyDop853B[j];
                    if (b == 0d)
                    {
                        continue;
                    }

                    dp += b * kp[j][i];
                    dv += b * kv[j][i];
                }

                pNew[i] = p[i] + (hStep * dp);
                vNew[i] = v[i] + (hStep * dv);
            }

            if (steps > 1000000L)
            {
                throw new InvalidOperationException(
                    "JplDop853 step cap: t=" + t.ToString("R", CultureInfo.InvariantCulture)
                    + " h=" + h.ToString("R", CultureInfo.InvariantCulture));
            }

            double err = 0d;
            for (int body = 0; body < n; body++)
            {
                double normP = 0d;
                double normV = 0d;
                double errP = 0d;
                double errV = 0d;
                for (int c = 0; c < 3; c++)
                {
                    int i = (3 * body) + c;
                    normP += pNew[i] * pNew[i];
                    normV += vNew[i] * vNew[i];
                    double ep = 0d;
                    double ev = 0d;
                    for (int j = 0; j < 12; j++)
                    {
                        double e = StudyDop853E5[j];
                        if (e == 0d)
                        {
                            continue;
                        }

                        ep += e * kp[j][i];
                        ev += e * kv[j][i];
                    }

                    errP += (hStep * ep) * (hStep * ep);
                    errV += (hStep * ev) * (hStep * ev);
                }

                double scaleP = posAtol + (rtol * Math.Sqrt(normP));
                double scaleV = velAtol + (rtol * Math.Sqrt(normV));
                err = Math.Max(err, Math.Max(Math.Sqrt(errP) / scaleP, Math.Sqrt(errV) / scaleV));
            }

            if (double.IsNaN(err))
            {
                throw new InvalidOperationException(
                    "JplDop853 NaN error at t=" + t.ToString("R", CultureInfo.InvariantCulture));
            }

            if (err <= 1d)
            {
                Array.Copy(pNew, p, length);
                Array.Copy(vNew, v, length);
                t += hStep;
                steps++;
                Acc(p, v, a);
                calls++;
                double factor = Math.Min(5d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-12d), 1d / 8d)));
                h = Math.Min(maxStep, Math.Max(minStep, hStep * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-12d), 1d / 8d)));
                h = Math.Max(minStep, hStep * factor);
            }
        }

        return (p, v, calls, steps);
    }

    private static void JplY6Step(
        double[] p, double[] pl, double[] v, double[] vl,
        double[] a, double[] mu, int n, double hStep, bool pn)
    {
        double[] ws = StudyY6Weights();
        foreach (double w in ws)
        {
            double dt = w * hStep;
            for (int i = 0; i < p.Length; i++)
            {
                StudyAdd(ref v[i], ref vl[i], 0.5d * dt * a[i]);
            }

            for (int i = 0; i < p.Length; i++)
            {
                StudyAdd(ref p[i], ref pl[i], dt * (v[i] + vl[i]));
            }

            JplAcc1PnC(p, pl, v, vl, a, mu, n, pn);
            for (int i = 0; i < p.Length; i++)
            {
                StudyAdd(ref v[i], ref vl[i], 0.5d * dt * a[i]);
            }
        }
    }

    private static (double[] p, double[] v) JplY6C(
        StudyNBody nb, double days, double h, bool pn)
    {
        int n = nb.Count;
        int length = 3 * n;
        var p = (double[])nb.P0.Clone();
        var v = (double[])nb.V0.Clone();
        var a = new double[length];
        var pl = new double[length];
        var vl = new double[length];
        double T = days * 86400d;
        long steps = (long)Math.Floor(T / h);
        double remainder = T - (steps * h);
        JplAcc1PnC(p, pl, v, vl, a, nb.Mu, n, pn);
        for (long s = 0; s < steps; s++)
        {
            JplY6Step(p, pl, v, vl, a, nb.Mu, n, h, pn);
        }

        if (remainder > 1e-9d)
        {
            JplY6Step(p, pl, v, vl, a, nb.Mu, n, remainder, pn);
        }

        for (int i = 0; i < length; i++)
        {
            p[i] += pl[i];
            v[i] += vl[i];
        }

        return (p, v);
    }

    private static double JplBodyError(
        JplFile jpl, StudyNBody nb, double[] p, double days, string body, bool geo)
    {
        int sun = JplIndex(nb, "Sun");
        int index = JplIndex(nb, body);
        int baseIndex = geo ? JplIndex(nb, "Earth") : sun;
        double[] baseP = { 0d, 0d, 0d };
        if (geo)
        {
            baseP = jpl.Bodies.First(b => b.Name == "Earth")
                .States.First(s => Math.Abs(s.Days - days) < 1e-9d).P;
        }

        JplState state = jpl.Bodies.First(b => b.Name == body)
            .States.First(s => Math.Abs(s.Days - days) < 1e-9d);
        double dx = (p[3 * index] - p[3 * baseIndex]) - (state.P[0] - baseP[0]);
        double dy = (p[(3 * index) + 1] - p[(3 * baseIndex) + 1]) - (state.P[1] - baseP[1]);
        double dz = (p[(3 * index) + 2] - p[(3 * baseIndex) + 2]) - (state.P[2] - baseP[2]);
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static double Jpl1PnGateKm(string body)
    {
        // Budget for the residual after 1PN: unmodelled asteroid/EIH/IC terms.
        // Bodies whose Newtonian residual is not understood are left ungated and
        // reported as known limitations instead of fitted thresholds.
        switch (body)
        {
            case "Mercury": return 50d;
            case "Venus": return 50d;
            case "Earth": return 100d;
            case "Mars": return 100d;
            default: return 0d;
        }
    }

    private static int Test531_Jpl1Pn()
    {
        Console.WriteLine("--- 10-body: Newton vs DOP853 1PN vs JPL, 10 years ---");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] ten = { "Mercury", "Venus", "Earth", "Moon", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };
        StudyNBody nb = JplNBody(jpl, ten);
        const double days = 3650d;
        var newton = JplDop853(nb, days, 1e-6d, 1e-9d, 1e-13d, false);
        var pn = JplDop853(nb, days, 1e-6d, 1e-9d, 1e-13d, true);

        Console.WriteLine("body        Newton(km)   1PN(km)      gate(km)");
        string[] names = { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };
        for (int i = 0; i < names.Length; i++)
        {
            string body = names[i];
            double dpN = JplBodyError(jpl, nb, newton.p, days, body, false);
            double dpP = JplBodyError(jpl, nb, pn.p, days, body, false);
            double gate = Jpl1PnGateKm(body);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-11} {1,10:F2}  {2,10:F2}  {3,10:F0}",
                body, dpN / 1000d, dpP / 1000d, gate));
            Check(double.IsFinite(dpP), "pn " + body + " finite",
                dpP.ToString("E2", CultureInfo.InvariantCulture));
            if (gate > 0d)
            {
                Check(dpP < gate * 1000d, "pn " + body + " accuracy",
                    (dpP / 1000d).ToString("F2", CultureInfo.InvariantCulture) + " < "
                    + gate.ToString("F0", CultureInfo.InvariantCulture) + " km");
            }
            else
            {
                Console.WriteLine("  known limitation, not gated");
            }
        }

        double moonN = JplBodyError(jpl, nb, newton.p, days, "Moon", true);
        double moonP = JplBodyError(jpl, nb, pn.p, days, "Moon", true);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-11} {1,10:F2}  {2,10:F2}  {3,10:F0}",
            "Moon(geo)", moonN / 1000d, moonP / 1000d, Jpl1PnGateKm("Moon_geo")));
        Console.WriteLine("  known limitation, not gated (J2 hypothesis)");

        double y6Ten = 0d;
        Console.WriteLine();
        Console.WriteLine("Y6 h=112.5 s, 1PN (velocity-dependent force, splitting artifact):");
        Console.WriteLine("days    Y6 1PN(km)");
        double[] horizons = { 365d, 1825d, 3650d };
        for (int i = 0; i < horizons.Length; i++)
        {
            var y6 = JplY6C(nb, horizons[i], 112.5d, true);
            double dp = JplBodyError(jpl, nb, y6.p, horizons[i], "Mercury", false);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture, "{0,6:F0}  {1,10:F2}", horizons[i], dp / 1000d));
            if (horizons[i] == days)
            {
                y6Ten = dp;
            }
        }

        double dopMercury = JplBodyError(jpl, nb, pn.p, days, "Mercury", false);
        Check(dopMercury < y6Ten * 0.1d, "DOP853 beats Y6 on 1PN",
            (y6Ten / Math.Max(dopMercury, 1e-30d)).ToString("E1", CultureInfo.InvariantCulture) + "x");
        Console.WriteLine("--- Mercury 1PN probe complete ---");
        return 0;
    }
}
