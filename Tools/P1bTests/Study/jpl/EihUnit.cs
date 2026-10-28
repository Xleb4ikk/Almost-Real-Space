using System;
using System.Globalization;

internal static partial class P1bTests
{
    private const double EihTestC2 = 8.9875517873681764e16d;

    // 1PN relative acceleration in harmonic coordinates (Damour-Deruelle / Blanchet):
    //   a = -(mu/r^2) [ (1+A) n + B v ],   x = x_B - x_A,  v = v_B - v_A,  n = x/r
    //   rdot = n.v,  nu = mA*mB/M^2,  mu = G*M
    //   A = (1/c^2) [ -3/2 rdot^2 nu + v^2 + 3 nu v^2 - (mu/r)(4 + 2 nu) ]
    //   B = (1/c^2) [ -4 rdot + 2 nu rdot ]
    private static void DdRelative1Pn(double[] x, double[] v, double mu, double nu, double[] a)
    {
        double r2 = (x[0] * x[0]) + (x[1] * x[1]) + (x[2] * x[2]);
        double r = Math.Sqrt(r2);
        double rd = ((x[0] * v[0]) + (x[1] * v[1]) + (x[2] * v[2])) / r;
        double v2 = (v[0] * v[0]) + (v[1] * v[1]) + (v[2] * v[2]);
        double bigA = ((-1.5d * rd * rd * nu) + v2 + (3d * nu * v2)
            - ((mu / r) * (4d + (2d * nu)))) / EihTestC2;
        double bigB = ((-4d * rd) + (2d * nu * rd)) / EihTestC2;
        for (int i = 0; i < 3; i++)
        {
            double n = x[i] / r;
            a[i] = -(mu / r2) * (((1d + bigA) * n) + (bigB * v[i]));
        }
    }

    private static void EihRandomUnit(Random rng, double[] vec)
    {
        double norm;
        do
        {
            for (int i = 0; i < 3; i++)
            {
                vec[i] = (2d * rng.NextDouble()) - 1d;
            }

            norm = Math.Sqrt((vec[0] * vec[0]) + (vec[1] * vec[1]) + (vec[2] * vec[2]));
        }
        while (norm < 1e-6d);
        for (int i = 0; i < 3; i++)
        {
            vec[i] /= norm;
        }
    }

    private static int Test536_EihTwoBodyDd()
    {
        Console.WriteLine("--- EIH two-body vs Damour-Deruelle 1PN, 2000 random CM states ---");
        const int trials = 2000;
        // The 1PN part is only ~1e-8 of the Newtonian term in slow, distant states,
        // so it is compared against the total acceleration (machine precision ~1e-15).
        const double tolerance = 1e-12d;
        const double solarMu = 1.32712440018e20d;
        var rng = new Random(20261028);
        var p = new double[6];
        var v = new double[6];
        var a = new double[6];
        var mu = new double[2];
        var x = new double[3];
        var vr = new double[3];
        var refAcc = new double[3];
        double worst = 0d;

        for (int t = 0; t < trials; t++)
        {
            mu[0] = solarMu * (0.05d + (0.95d * rng.NextDouble()));
            mu[1] = solarMu * (0.05d + (0.95d * rng.NextDouble()));
            double muTot = mu[0] + mu[1];
            double nu = (mu[0] * mu[1]) / (muTot * muTot);

            EihRandomUnit(rng, x);
            double radius = 2e11 + (2.8e12 * rng.NextDouble());
            for (int i = 0; i < 3; i++)
            {
                x[i] *= radius;
            }

            EihRandomUnit(rng, vr);
            double speed = 0.1d * 299792458d * rng.NextDouble();
            for (int i = 0; i < 3; i++)
            {
                vr[i] *= speed;
            }

            // zero-momentum barycentric state: x_A = -(mB/M) x, x_B = (mA/M) x
            double fA = mu[1] / muTot;
            double fB = mu[0] / muTot;
            for (int i = 0; i < 3; i++)
            {
                p[i] = -fA * x[i];
                p[3 + i] = fB * x[i];
                v[i] = -fA * vr[i];
                v[3 + i] = fB * vr[i];
            }

            JplAccEih(p, v, a, mu, 2);
            DdRelative1Pn(x, vr, muTot, nu, refAcc);

            double errNum = 0d;
            double errDen = 0d;
            for (int i = 0; i < 3; i++)
            {
                double rel = a[3 + i] - a[i];
                double d1 = rel - refAcc[i];
                errNum += d1 * d1;
                errDen += refAcc[i] * refAcc[i];
            }

            double err = Math.Sqrt(errNum) / Math.Sqrt(errDen);
            if (err > worst)
            {
                worst = err;
            }
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "trials={0}  worst relative error vs DD: {1:E3}",
            trials,
            worst));
        Check(worst < tolerance, "EIH matches Damour-Deruelle 1PN",
            worst.ToString("E2", CultureInfo.InvariantCulture));
        return 0;
    }
}
