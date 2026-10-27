using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static double[] Cross3(double[] a, double[] b)
    {
        return new[]
        {
            (a[1] * b[2]) - (a[2] * b[1]),
            (a[2] * b[0]) - (a[0] * b[2]),
            (a[0] * b[1]) - (a[1] * b[0])
        };
    }

    private static double[] ExpectedInertial(double[] relative, double f1, double f2)
    {
        var result = new double[6];
        for (int i = 0; i < 3; i++)
        {
            result[i] = f1 * relative[i];
            result[3 + i] = f2 * relative[i];
        }

        return result;
    }

    private static double[] Negate(double[] a)
    {
        var result = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            result[i] = -a[i];
        }

        return result;
    }

    private static void CheckDdCase(
        Kepler3DCase c, TaylorNBodyInput input, double[] pt6, double[] vt6)
    {
        DdNBodyResult dd = RunTaylorNBodyDdOptSeconds(
            input, c.TotalSeconds, c.Order, c.Tolerance, c.Tolerance,
            c.TotalSeconds, 1e-12d);
        double ddDp = Distance(dd.P, pt6);
        double ddDv = Distance(dd.V, vt6);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "dd {0}: dp={1:E2} dv={2:E2} steps={3}",
            c.Label, ddDp, ddDv, dd.Steps));
        Check(ddDp < 1e-10d, "matrix dd dp " + c.Label, ddDp.ToString("E2", CultureInfo.InvariantCulture));
        Check(ddDv < 1e-8d, "matrix dd dv " + c.Label, ddDv.ToString("E2", CultureInfo.InvariantCulture));
    }

    private static int Test510_TaylorMatrix()
    {
        Console.WriteLine("--- Taylor matrix: 3D Kepler, N-body, dd, invariants ---");
        foreach (Kepler3DCase c in Kepler3DMatrix)
        {
            double[] pt = Golden(c.PT);
            double[] vt = Golden(c.VT);

            var body = new OrbitingBody
            {
                Name = "Central",
                StandardGravitationalParameter = c.Mu,
                Radius = 1d
            };
            var sys = new StarSystem(body);
            var ship = TaylorShip.RunSeconds(
                sys,
                new Vector3d(c.P0[0], c.P0[1], c.P0[2]),
                new Vector3d(c.V0[0], c.V0[1], c.V0[2]),
                0d, c.TotalSeconds, c.Order, c.Tolerance, c.Tolerance, c.TotalSeconds, 1e-12d);
            double shipDp = Distance(ship.P, pt);
            double shipDv = Distance(ship.V, vt);

            double m1 = c.Mu / (1d + c.MassRatio);
            double m2 = c.Mu - m1;
            double f1 = -m2 / c.Mu;
            double f2 = m1 / c.Mu;
            var nbodyInput = new TaylorNBodyInput
            {
                Names = new[] { "A", "B" },
                Mu = new[] { m1, m2 },
                P0 = new[]
                {
                    f1 * c.P0[0], f1 * c.P0[1], f1 * c.P0[2],
                    f2 * c.P0[0], f2 * c.P0[1], f2 * c.P0[2]
                },
                V0 = new[]
                {
                    f1 * c.V0[0], f1 * c.V0[1], f1 * c.V0[2],
                    f2 * c.V0[0], f2 * c.V0[1], f2 * c.V0[2]
                }
            };
            var nbody = TaylorNBody.RunSeconds(
                nbodyInput, c.TotalSeconds, c.Order, c.Tolerance, c.Tolerance,
                c.TotalSeconds, 1e-12d, 0d);
            double[] pt6 = ExpectedInertial(pt, f1, f2);
            double[] vt6 = ExpectedInertial(vt, f1, f2);
            double nbodyDp = Distance(nbody.P, pt6);
            double nbodyDv = Distance(nbody.V, vt6);

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-18} ship dp={1:E2} dv={2:E2} | nbody dp={3:E2} dv={4:E2}",
                c.Label, shipDp, shipDv, nbodyDp, nbodyDv));

            Check(double.IsFinite(shipDp) && double.IsFinite(shipDv), c.Label + " ship finite", "dp/dv");
            Check(double.IsFinite(nbodyDp) && double.IsFinite(nbodyDv), c.Label + " nbody finite", "dp/dv");
            Check(shipDp < c.PositionGate, c.Label + " ship dp", shipDp.ToString("E2", CultureInfo.InvariantCulture));
            Check(shipDv < c.VelocityGate, c.Label + " ship dv", shipDv.ToString("E2", CultureInfo.InvariantCulture));
            Check(nbodyDp < c.PositionGate, c.Label + " nbody dp", nbodyDp.ToString("E2", CultureInfo.InvariantCulture));
            Check(nbodyDv < c.VelocityGate, c.Label + " nbody dv", nbodyDv.ToString("E2", CultureInfo.InvariantCulture));
            CheckReferenceMargin(c.Label + " dp", Math.Max(shipDp, nbodyDp), 1e-40d);
            CheckReferenceMargin(c.Label + " dv", Math.Max(shipDv, nbodyDv), 1e-40d);

            double e0 = StudyEnergy(nbodyInput.P0, nbodyInput.V0, nbodyInput.Mu);
            double e1 = StudyEnergy(nbody.P, nbody.V, nbodyInput.Mu);
            double dE = Math.Abs((e1 - e0) / e0);
            double[] mom0 = TotalMomentum(nbodyInput.V0, nbodyInput.Mu);
            double[] mom1 = TotalMomentum(nbody.V, nbodyInput.Mu);
            double[] ang0 = TotalAngular(nbodyInput.P0, nbodyInput.V0, nbodyInput.Mu);
            double[] ang1 = TotalAngular(nbody.P, nbody.V, nbodyInput.Mu);
            double dP = Magnitude3(Sub3(mom1, mom0));
            double dL = Magnitude3(Sub3(ang1, ang0)) / Math.Max(Magnitude3(ang0), 1e-300d);
            Check(dE < 1e-11d, c.Label + " energy", dE.ToString("E2", CultureInfo.InvariantCulture));
            Check(dP < 1e-12d, c.Label + " momentum", dP.ToString("E2", CultureInfo.InvariantCulture));
            Check(dL < 1e-11d, c.Label + " angular", dL.ToString("E2", CultureInfo.InvariantCulture));

            if (c.Label == "e=0.9 inc=45 100p")
            {
                var backInput = new TaylorNBodyInput
                {
                    Names = nbodyInput.Names,
                    Mu = nbodyInput.Mu,
                    P0 = (double[])nbody.P.Clone(),
                    V0 = Negate(nbody.V)
                };
                var back = TaylorNBody.RunSeconds(
                    backInput, c.TotalSeconds, c.Order, c.Tolerance, c.Tolerance,
                    c.TotalSeconds, 1e-12d, 0d);
                double reverseP = Distance(back.P, nbodyInput.P0);
                double reverseV = Distance(back.V, Negate(nbodyInput.V0));
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "time reversal: dp={0:E2} dv={1:E2}", reverseP, reverseV));
                Check(reverseP < 1e-9d, "matrix time reversal dp", reverseP.ToString("E2", CultureInfo.InvariantCulture));
                Check(reverseV < 1e-7d, "matrix time reversal dv", reverseV.ToString("E2", CultureInfo.InvariantCulture));

                double[] pRel = new[]
                {
                    nbody.P[3] - nbody.P[0],
                    nbody.P[4] - nbody.P[1],
                    nbody.P[5] - nbody.P[2]
                };
                double[] vRel = new[]
                {
                    nbody.V[3] - nbody.V[0],
                    nbody.V[4] - nbody.V[1],
                    nbody.V[5] - nbody.V[2]
                };
                double[] h = Cross3(pRel, vRel);
                double rn = Math.Sqrt((pRel[0] * pRel[0]) + (pRel[1] * pRel[1]) + (pRel[2] * pRel[2]));
                double[] eh = Cross3(vRel, h);
                double[] lrl = new[]
                {
                    (eh[0] / c.Mu) - (pRel[0] / rn),
                    (eh[1] / c.Mu) - (pRel[1] / rn),
                    (eh[2] / c.Mu) - (pRel[2] / rn)
                };
                double[] p0Rel = new[] { c.P0[0], c.P0[1], c.P0[2] };
                double[] v0Rel = new[] { c.V0[0], c.V0[1], c.V0[2] };
                double[] h0 = Cross3(p0Rel, v0Rel);
                double r0n = Math.Sqrt((p0Rel[0] * p0Rel[0]) + (p0Rel[1] * p0Rel[1]) + (p0Rel[2] * p0Rel[2]));
                double[] eh0 = Cross3(v0Rel, h0);
                double[] lrl0 = new[]
                {
                    (eh0[0] / c.Mu) - (p0Rel[0] / r0n),
                    (eh0[1] / c.Mu) - (p0Rel[1] / r0n),
                    (eh0[2] / c.Mu) - (p0Rel[2] / r0n)
                };
                double dLrl = Math.Sqrt(
                    ((lrl[0] - lrl0[0]) * (lrl[0] - lrl0[0]))
                    + ((lrl[1] - lrl0[1]) * (lrl[1] - lrl0[1]))
                    + ((lrl[2] - lrl0[2]) * (lrl[2] - lrl0[2])));
                Console.WriteLine("LRL drift: " + dLrl.ToString("E2", CultureInfo.InvariantCulture));
                Check(dLrl < 2e-12d, "matrix LRL", dLrl.ToString("E2", CultureInfo.InvariantCulture));
            }

            if (c.Label == "e=0.99 inc=45 10p"
                || c.Label == "e=0.5 inc=45 100p ratio=0.01")
            {
                CheckDdCase(c, nbodyInput, pt6, vt6);
            }
        }

        Console.WriteLine("--- Taylor matrix complete ---");
        return 0;
    }
}


