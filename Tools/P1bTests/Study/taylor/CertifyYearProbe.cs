using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test524_CertifyYearProbe()
    {
        Console.WriteLine("--- 1-year self-certification probe (K+4, tol/100) ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("Certify year", real, nb, 1.1d, 1d);

        string path = Path.Combine("Study", "taylor", "golden", "golden_real_1y_hi.json");
        GoldFile golden = JsonSerializer.Deserialize<GoldFile>(File.ReadAllText(path));
        GoldCp g = null;
        foreach (GoldCp cp in golden.Checkpoints)
        {
            if (Math.Abs(cp.Days - 365d) < 1e-9d)
            {
                g = cp;
            }
        }

        Check(g != null, "certify year golden", path);
        if (g == null)
        {
            return 0;
        }

        OrbitingBody body = real.AllBodies[1];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        real.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 4.6371e7d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;

        var labels = new[]
        {
            "K24 tol 1e-12/1e-15 hmax 900",
            "K28 tol 1e-12/1e-15 hmax 900",
            "K28 tol 1e-14/1e-17 hmax 900"
        };
        var orders = new[] { 24, 28, 28 };
        var positionTolerances = new[] { 1e-12d, 1e-12d, 1e-14d };
        var velocityTolerances = new[] { 1e-15d, 1e-15d, 1e-17d };
        var results = new TaylorShipDdResult[labels.Length];
        Parallel.For(0, labels.Length, i =>
        {
            results[i] = TaylorShip.RunDdSeconds(
                real, p0, v0, t0, 365d * 86400d, orders[i],
                positionTolerances[i], velocityTolerances[i], 900d, 1e-6d);
        });

        double baseP = DdGoldErr(results[0], g.P);
        double baseV = DdGoldErrV(results[0], g.V);
        for (int i = 0; i < labels.Length; i++)
        {
            double dp = DdGoldErr(results[i], g.P);
            double dv = DdGoldErrV(results[i], g.V);
            double dpVsBase = DdNorm(
                new[] { results[i].DdPx, results[i].DdPy, results[i].DdPz },
                new[] { results[0].DdPx, results[0].DdPy, results[0].DdPz });
            double dvVsBase = DdNorm(
                new[] { results[i].DdVx, results[i].DdVy, results[i].DdVz },
                new[] { results[0].DdVx, results[0].DdVy, results[0].DdVz });
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: golden dp={1:E3} dv={2:E3}, vs K24 dp={3:E3} dv={4:E3}, steps={5}",
                labels[i], dp, dv, dpVsBase, dvVsBase, results[i].Steps));
            Check(double.IsFinite(dp) && double.IsFinite(dv),
                "certify year " + i + " finite", "dp/dv");
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "certificate (K28 K24) / base: dp={0:E3}, dv={1:E3}",
            DdNorm(
                new[] { results[2].DdPx, results[2].DdPy, results[2].DdPz },
                new[] { results[0].DdPx, results[0].DdPy, results[0].DdPz }) / Math.Max(baseP, 1e-300d),
            DdNorm(
                new[] { results[2].DdVx, results[2].DdVy, results[2].DdVz },
                new[] { results[0].DdVx, results[0].DdVy, results[0].DdVz }) / Math.Max(baseV, 1e-300d)));
        Console.WriteLine("--- 1-year self-certification probe complete ---");
        return 0;
    }
}

