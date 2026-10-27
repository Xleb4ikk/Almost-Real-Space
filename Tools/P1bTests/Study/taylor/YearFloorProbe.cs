using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test522_YearFloorProbe()
    {
        Console.WriteLine("--- 1-year floor probe: tol/hmax sweep ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("Year floor", real, nb, 1.1d);

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

        Check(g != null, "year floor golden", path);
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
            "tol 1e-12/1e-15 hmax 1800",
            "tol 1e-14/1e-17 hmax 900",
            "tol 1e-12/1e-15 hmax 900"
        };
        var positionTolerances = new[] { 1e-12d, 1e-14d, 1e-12d };
        var velocityTolerances = new[] { 1e-15d, 1e-17d, 1e-15d };
        var maxSteps = new[] { 1800d, 900d, 900d };
        var results = new TaylorShipDdResult[labels.Length];
        Parallel.For(0, labels.Length, i =>
        {
            results[i] = TaylorShip.RunDdSeconds(
                real, p0, v0, t0, 365d * 86400d, 24,
                positionTolerances[i], velocityTolerances[i], maxSteps[i], 1e-6d);
        });

        for (int i = 0; i < labels.Length; i++)
        {
            double dp = DdGoldErr(results[i], g.P);
            double dv = DdGoldErrV(results[i], g.V);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: dp={1:E3} m, dv={2:E3} m/s, steps={3}",
                labels[i], dp, dv, results[i].Steps));
            Check(double.IsFinite(dp) && double.IsFinite(dv),
                "year floor " + i + " finite", "dp/dv");
        }

        Console.WriteLine("--- 1-year floor probe complete ---");
        return 0;
    }
}
