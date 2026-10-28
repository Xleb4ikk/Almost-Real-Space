using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static double[] TenYearDdDays()
    {
        string overrideDays = Environment.GetEnvironmentVariable("P1B_TENYEAR_DAYS");
        if (!string.IsNullOrEmpty(overrideDays))
        {
            string[] parts = overrideDays.Split(',');
            var parsed = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                parsed[i] = double.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            }

            Array.Sort(parsed);
            return parsed;
        }

        if (Environment.GetEnvironmentVariable("P1B_TENYEAR_FULL") == "1")
        {
            return new[] { 365d, 730d, 1095d, 1460d, 1825d, 2190d, 2555d, 2920d, 3285d, 3650d };
        }

        return new[] { 365d };
    }

    private static int Test527_TenYearDd()
    {
        double[] days = TenYearDdDays();
        Console.WriteLine("--- dd long-horizon reference: " + days.Length + " checkpoint(s) x (K24,K28); "
            + "P1B_TENYEAR_FULL=1 runs the 10-year matrix ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("TenYear dd", real, nb, 10.1d, 20d);

        string exportPath = Path.Combine("Study", "taylor", "golden", "real_bake_export_10y.json");
        GoldenRequireFile(exportPath, "FF5E6C2DB5E001762E6AC5BA7768E5254AE1B2FBCBB9EB29EDADCAD47E1A80F0");
        ExportData export = JsonSerializer.Deserialize<ExportData>(File.ReadAllText(exportPath));
        int badBodies = 0;
        for (int i = 0; i < real.AllBodies.Count; i++)
        {
            OrbitingBody body = real.AllBodies[i];
            ExportBody eb = export.Bodies[i];
            if (body.Baked == null)
            {
                continue;
            }

            if (body.Baked.SegmentCount != eb.Segments
                || body.Baked.SegmentLengthSeconds != eb.SegmentLength
                || body.Baked.Degree != eb.Degree
                || body.Baked.T0Seconds != eb.T0)
            {
                badBodies++;
                continue;
            }

            int stride = BakedEphemeris.AxisStride(body.Baked.Degree);
            for (int s = 0; s < eb.Segments; s++)
            {
                for (int k = 0; k < stride; k++)
                {
                    if (eb.Coeffs[s][k] != body.Baked.Coeffs[(s * stride) + k])
                    {
                        badBodies++;
                        s = eb.Segments;
                        break;
                    }
                }
            }
        }

        Check(badBodies == 0, "10y export matches in-memory bake",
            badBodies.ToString(CultureInfo.InvariantCulture));

        GoldFile partial = null;
        string partialPath = Path.Combine("Study", "taylor", "golden", "golden_real_10y_k24.partial.json");
        if (File.Exists(partialPath))
        {
            partial = JsonSerializer.Deserialize<GoldFile>(File.ReadAllText(partialPath));
        }

        OrbitingBody bodyEarth = real.AllBodies[1];
        double mu = bodyEarth.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        real.EvaluateBodyState(bodyEarth, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 4.6371e7d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;

        var checkpointTimes = new double[days.Length];
        for (int i = 0; i < days.Length; i++)
        {
            checkpointTimes[i] = t0 + (days[i] * 86400d);
        }

        TaylorShipDdResult[] k24 = null;
        TaylorShipDdResult[] k28 = null;
        double horizonSeconds = days[days.Length - 1] * 86400d;
        var watch = Stopwatch.StartNew();
        Parallel.Invoke(
            () =>
            {
                k24 = TaylorShip.RunDdSecondsCheckpoints(
                    real, p0, v0, t0, horizonSeconds, 24, 1e-12d, 1e-15d, 3600d, 1e-6d,
                    checkpointTimes);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  done K24: {0} steps, {1:F0} s",
                    k24[k24.Length - 1].Steps, k24[k24.Length - 1].Ms / 1000d));
            },
            () =>
            {
                k28 = TaylorShip.RunDdSecondsCheckpoints(
                    real, p0, v0, t0, horizonSeconds, 28, 1e-14d, 1e-17d, 3600d, 1e-6d,
                    checkpointTimes);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  done K28: {0} steps, {1:F0} s",
                    k28[k28.Length - 1].Steps, k28[k28.Length - 1].Ms / 1000d));
            });
        watch.Stop();

        Console.WriteLine("days      steps24   steps28   dp(24 vs 28)  dv(24 vs 28)  dp24-vs-mp   dp28-vs-mp");
        var rows = new List<string>();
        for (int i = 0; i < days.Length; i++)
        {
            TaylorShipDdResult a = k24[i];
            TaylorShipDdResult b = k28[i];
            double dpAb = DdNorm(
                new[] { a.DdPx, a.DdPy, a.DdPz },
                new[] { b.DdPx, b.DdPy, b.DdPz });
            double dvAb = DdNorm(
                new[] { a.DdVx, a.DdVy, a.DdVz },
                new[] { b.DdVx, b.DdVy, b.DdVz });

            GoldCp mp = null;
            if (partial != null)
            {
                foreach (GoldCp cp in partial.Checkpoints)
                {
                    if (Math.Abs(cp.Days - days[i]) < 1e-9d)
                    {
                        mp = cp;
                    }
                }
            }

            double dp24 = mp != null ? DdGoldErr(a, mp.P) : double.NaN;
            double dp28 = mp != null ? DdGoldErr(b, mp.P) : double.NaN;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,6:F0}  {1,8}  {2,8}  {3,12:E3}  {4,12:E3}  {5,11:E3}  {6,11:E3}",
                days[i], a.Steps, b.Steps, dpAb, dvAb, dp24, dp28));
            Check(double.IsFinite(dpAb) && double.IsFinite(dvAb),
                "10y dd " + days[i] + " finite", "dp/dv");
            Check(dpAb < 1d, "10y dd " + days[i] + " K24 vs K28",
                dpAb.ToString("E3", CultureInfo.InvariantCulture));
            rows.Add(StudyCsvRow("day " + days[i].ToString("F0", CultureInfo.InvariantCulture),
                dpAb, dvAb, a.Steps, b.Steps, dp24, dp28));
        }

        StudyWriteCsv("Test527_TenYearDd.csv",
            "case,dp_24_28_m,dv_24_28_mps,steps24,steps28,dp24_mp_m,dp28_mp_m", rows);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "dd trajectory wall={0:F0} s", watch.Elapsed.TotalSeconds));
        Console.WriteLine("--- 10-year dd reference complete ---");
        return 0;
    }
}
