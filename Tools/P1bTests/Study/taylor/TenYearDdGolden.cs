using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private sealed class TenYearDdGoldenFile
    {
        [JsonPropertyName("method")] public string Method { get; set; }
        [JsonPropertyName("order")] public int Order { get; set; }
        [JsonPropertyName("position_tolerance")] public double PositionTolerance { get; set; }
        [JsonPropertyName("velocity_tolerance")] public double VelocityTolerance { get; set; }
        [JsonPropertyName("max_step_seconds")] public double MaxStepSeconds { get; set; }
        [JsonPropertyName("bake_export")] public string BakeExport { get; set; }
        [JsonPropertyName("t0")] public double T0 { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; }
        [JsonPropertyName("checkpoints")] public GoldCp[] Checkpoints { get; set; }
    }

    private static string GoldFromFixed(BigInteger v)
    {
        bool neg = v.Sign < 0;
        if (neg)
        {
            v = -v;
        }

        string digits = v.ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= 30)
        {
            digits = digits.PadLeft(31, '0');
        }

        string s = digits.Substring(0, digits.Length - 30)
            + "." + digits.Substring(digits.Length - 30);
        s = s.TrimEnd('0');
        if (s.EndsWith(".", StringComparison.Ordinal))
        {
            s += "0";
        }

        return (neg ? "-" : "") + s;
    }

    private static string GoldFromDouble(double x)
    {
        return GoldFromFixed(DblFix(x));
    }

    private static string GoldFromDd(StudyDd x)
    {
        return GoldFromFixed(BigInteger.Add(DblFix(x.Hi), DblFix(x.Lo)));
    }

    private static int Test529_TenYearDdGolden()
    {
        Console.WriteLine("--- dd-Taylor K28: 10-year ship golden, annual checkpoints ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(real);
        StudyBake("TenYear dd golden", real, nb, 10.1d, 20d);

        string exportPath = Path.Combine("Study", "taylor", "golden", "real_bake_export_10y.json");
        GoldenRequireFile(exportPath, "FF5E6C2DB5E001762E6AC5BA7768E5254AE1B2FBCBB9EB29EDADCAD47E1A80F0");

        GoldFile partial = null;
        string partialPath = Path.Combine("Study", "taylor", "golden", "golden_real_10y_k24.partial.json");
        if (File.Exists(partialPath))
        {
            partial = JsonSerializer.Deserialize<GoldFile>(File.ReadAllText(partialPath));
        }

        OrbitingBody body = real.AllBodies[1];
        double mu = body.ResolveStandardGravitationalParameter();
        double t0 = StudyShipT0;
        real.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
        StudyShipOrbit(6.771e6d, 4.6371e7d, mu, out Vector3d relP, out Vector3d relV);
        var p0 = bp + relP;
        var v0 = bv + relV;

        double[] days = { 365d, 730d, 1095d, 1460d, 1825d, 2190d, 2555d, 2920d, 3285d, 3650d };
        var checkpointTimes = new double[days.Length];
        for (int i = 0; i < days.Length; i++)
        {
            checkpointTimes[i] = t0 + (days[i] * 86400d);
        }

        Console.WriteLine("running K28 to 10 years; this takes about 1.5 hours...");
        TaylorShipDdResult[] results = TaylorShip.RunDdSecondsCheckpoints(
            real, p0, v0, t0, days[days.Length - 1] * 86400d,
            28, 1e-14d, 1e-17d, 3600d, 1e-6d, checkpointTimes);

        var checkpoints = new GoldCp[days.Length];
        for (int i = 0; i < days.Length; i++)
        {
            double T = checkpointTimes[i];
            real.EvaluateBodyState(body, T, out Vector3d cp, out Vector3d cv);
            TaylorShipDdResult r = results[i];
            StudyDd relX = StudyDd.Sub(r.DdPx, StudyDd.From(cp.X));
            StudyDd relY = StudyDd.Sub(r.DdPy, StudyDd.From(cp.Y));
            StudyDd relZ = StudyDd.Sub(r.DdPz, StudyDd.From(cp.Z));
            StudyDd relVx = StudyDd.Sub(r.DdVx, StudyDd.From(cv.X));
            StudyDd relVy = StudyDd.Sub(r.DdVy, StudyDd.From(cv.Y));
            StudyDd relVz = StudyDd.Sub(r.DdVz, StudyDd.From(cv.Z));

            var err = new Dictionary<string, string>();
            GoldCp mp = null;
            if (partial != null)
            {
                foreach (GoldCp candidate in partial.Checkpoints)
                {
                    if (Math.Abs(candidate.Days - days[i]) < 1e-9d)
                    {
                        mp = candidate;
                    }
                }
            }

            string dpText = "n/a";
            string dvText = "n/a";
            if (mp != null)
            {
                dpText = DdGoldErr(r, mp.P).ToString("E6", CultureInfo.InvariantCulture);
                dvText = DdGoldErrV(r, mp.V).ToString("E6", CultureInfo.InvariantCulture);
                err["source"] = "mp-golden";
                err["dp"] = dpText;
                err["dv"] = dvText;
                Check(DdGoldErr(r, mp.P) < 1e-6d,
                    "dd K28 " + days[i] + " d vs mp dp", dpText);
            }

            checkpoints[i] = new GoldCp
            {
                Days = days[i],
                P = new[] { GoldFromDd(r.DdPx), GoldFromDd(r.DdPy), GoldFromDd(r.DdPz) },
                V = new[] { GoldFromDd(r.DdVx), GoldFromDd(r.DdVy), GoldFromDd(r.DdVz) },
                CentralP = new[] { GoldFromDouble(cp.X), GoldFromDouble(cp.Y), GoldFromDouble(cp.Z) },
                CentralV = new[] { GoldFromDouble(cv.X), GoldFromDouble(cv.Y), GoldFromDouble(cv.Z) },
                RelP = new[] { GoldFromDd(relX), GoldFromDd(relY), GoldFromDd(relZ) },
                RelV = new[] { GoldFromDd(relVx), GoldFromDd(relVy), GoldFromDd(relVz) },
                Err = err
            };

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,5:F0} d: steps={1,8} calls={2,9} dp_mp={3,12} dv_mp={4,12}",
                days[i], r.Steps, r.Calls, dpText, dvText));
            Check(double.IsFinite(r.P.X) && double.IsFinite(r.P.Y) && double.IsFinite(r.P.Z),
                "dd K28 " + days[i] + " d finite", r.Steps.ToString(CultureInfo.InvariantCulture));
        }

        var file = new TenYearDdGoldenFile
        {
            Method = "Taylor dd",
            Order = 28,
            PositionTolerance = 1e-14d,
            VelocityTolerance = 1e-17d,
            MaxStepSeconds = 3600d,
            BakeExport = Path.GetFileName(exportPath),
            T0 = t0,
            Note = "K28 anchored to mp-golden at 365/730/1095 d; 10-year endpoint is not mp-certified.",
            Checkpoints = checkpoints
        };

        string outPath = Path.Combine("Study", "taylor", "golden", "golden_real_10y_k28.json");
        string tmpPath = outPath + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmpPath, outPath, true);
        Console.WriteLine("wrote " + outPath);
        GoldenPrintHash(outPath);
        Console.WriteLine("--- dd-Taylor 10-year golden complete ---");
        return 0;
    }
}
