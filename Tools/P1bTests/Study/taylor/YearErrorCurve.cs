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
    private static int Test525_YearErrorCurve()
    {
        Console.WriteLine("--- 1-year error curve vs multi-checkpoint golden (K20/tol1e-12) ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("Error curve", real, nb, 1.1d, 1d);

        string path = Path.Combine("Study", "taylor", "golden", "golden_real_1y_probe.json");
        GoldFile golden = JsonSerializer.Deserialize<GoldFile>(File.ReadAllText(path));
        GoldCp[] cps = golden.Checkpoints;
        Console.WriteLine("checkpoints=" + cps.Length + ", K20/tol1e-12/dps30");

        OrbitingBody body = real.AllBodies[1];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        real.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 4.6371e7d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;

        var results = new TaylorShipDdResult[cps.Length];
        Parallel.For(0, cps.Length, i =>
        {
            results[i] = TaylorShip.RunDdSeconds(
                real, p0, v0, t0, cps[i].Days * 86400d, 24, 1e-12d, 1e-15d, 900d, 1e-6d);
        });

        Console.WriteLine("days        dp(m)       dv(m/s)     steps    dp/day");
        var rows = new List<string>();
        for (int i = 0; i < cps.Length; i++)
        {
            double dp = DdGoldErr(results[i], cps[i].P);
            double dv = DdGoldErrV(results[i], cps[i].V);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,7:F1}  {1,11:E3}  {2,11:E3}  {3,7}  {4,11:E3}",
                cps[i].Days, dp, dv, results[i].Steps, dp / cps[i].Days));
            Check(double.IsFinite(dp) && double.IsFinite(dv),
                "year curve " + cps[i].Days + " finite", "dp/dv");
            rows.Add(StudyCsvRow("day " + cps[i].Days.ToString("F0", CultureInfo.InvariantCulture),
                dp, dv, results[i].Steps));
        }

        StudyWriteCsv("Test525_YearErrorCurve.csv", "case,dp_m,dv_mps,steps", rows);
        Console.WriteLine("--- 1-year error curve complete ---");
        return 0;
    }
}

