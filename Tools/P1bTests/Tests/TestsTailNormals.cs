using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Профиль ровно как в EarthLike_Perlin.asset, включая сжатие хвоста.
    /// Держим здесь руками, а не читаем ассет: .NET-стенд не умеет грузить
    /// Unity-ассеты, а расхождение фикстуры с ассетом сделало бы замер
    /// бессмысленным. Значения помечены, откуда каждое.
    /// </summary>
    private static HeightfieldTerrain PerlinAsset()
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        t.NoiseStyle = (int)TerrainNoiseStyle.Perlin;      // NoiseStyle: 1
        t.MaskNoiseStyle = -1;                             // MaskNoiseStyle: -1
        t.AmplitudeMeters = 13662d;                        // AmplitudeMeters: 13662
        t.BaseFrequency = 20d;                             // BaseFrequency: 20
        t.Octaves = 10;                                    // Octaves: 10
        t.Lacunarity = 2d;                                 // Lacunarity: 2
        t.Gain = 0.5d;                                     // Gain: 0.5
        t.RidgedMix = 0.7d;                                // RidgedMix: 0.7
        t.RidgedMode = (int)TerrainRidgedMode.Multifractal; // RidgedMode: 1
        t.RidgedSharpness = 2;                             // RidgedSharpness: 2
        t.RidgedWeightGain = 2d;                           // RidgedWeightGain: 2
        t.RidgedGamma = 1d;                                // RidgedGamma: 1
        t.ContinentFrequency = 4d;                         // ContinentFrequency: 4
        t.ContinentOctaves = 3;                            // ContinentOctaves: 3
        t.ContinentThreshold = -0.4d;                      // ContinentThreshold: -0.4
        t.ContinentSharpness = 0.3d;                       // ContinentSharpness: 0.3
        t.ContinentDepth = 1.2d;                           // ContinentDepth: 1.2
        t.PlainMix = 0.85d;                                // PlainMix: 0.85
        t.PlainFrequency = 1.8d;                           // PlainFrequency: 1.8
        t.PlainOctaves = 2;                                // PlainOctaves: 2
        t.PlainThreshold = 0.05d;                          // PlainThreshold: 0.05
        t.PlainSharpness = 0.25d;                          // PlainSharpness: 0.25
        t.PlainElevation = 0.1d;                           // PlainElevation: 0.1
        t.TailKnee = 0.13d;                                // TailKnee: 0.13
        t.TailThreshold = 0.5656d;                         // TailThreshold: 0.5656
        t.BeachHeightMeters = 12d;                         // BeachHeightMeters: 12
        t.BeachShelfAltitudeMeters = 8d;                   // BeachShelfAltitudeMeters: 8
        t.BeachShelfWidth = 0.08d;                         // BeachShelfWidth: 0.08
        t.DetailMix = 0d;                                  // DetailMix: 0
        t.DetailFrequency = 0d;                            // DetailFrequency: 0
        t.WarpStrength = 0.1d;                             // WarpStrength: 0.1
        t.WarpFrequency = 2d;                              // WarpFrequency: 2
        t.WarpOctaves = 2;                                 // WarpOctaves: 2
        t.SlopeDamp = 0d;                                  // SlopeDamp: 0
        t.SlopeDampMode = (int)TerrainSlopeDampMode.Off;   // SlopeDampMode: 0
        return t;
    }

    /// <summary>
    /// T84 сценарий 2 на тайле 0.5°/65 вершин: нормали double против нормалей
    /// после приведения позиций к float. Порог T84 - 2e-3 рад.
    ///
    /// Две плитки, потому что сжатие хвоста трогает поле только в одном
    /// месте, и проверять надо именно его:
    ///   • гребень  - самая высокая точка, там поле сжато сильнее всего;
    ///   • порог   - точка, где форма равна TailThreshold, то есть колено.
    ///     Плитка вокруг неё содержит и сжатые, и несжатые точки сразу, и это
    ///     самый напряжённый случай для производной.
    /// </summary>
    private static int Test133_TailNormalParity()
    {
        HeightfieldTerrain adv = PerlinAsset();
        OrbitingBody body = MakeTerrainBody();
        body.Terrain = adv;
        TerrainNoiseParams par = TerrainNoiseParams.FromTerrain(adv);
        double amp = adv.AmplitudeMeters;
        double sea = adv.SeaLevelMeters;
        double thresholdMeters = adv.TailThreshold * amp;

        // Ищем две опорные точки на сфере: гребень и порог.
        const int scanN = 120;
        int count = (scanN + 1) * (scanN + 1);
        double bestH = double.NegativeInfinity, bestLat = 0d, bestLon = 0d;
        double kneeGap = double.MaxValue, kneeLat = 0d, kneeLon = 0d, kneeH = 0d;
        for (int i = 0; i <= scanN; i++)
        {
            for (int j = 0; j <= scanN; j++)
            {
                double latDeg = -85d + (170d * i / scanN);
                double lonDeg = -180d + (360d * j / scanN);
                double lat = latDeg * (Math.PI / 180d);
                double lon = lonDeg * (Math.PI / 180d);
                double3 dir = DirOf(lat, lon);
                double h = TerrainNoise.SampleHeight(par, dir) * amp;
                if (h > bestH)
                {
                    bestH = h;
                    bestLat = lat;
                    bestLon = lon;
                }

                // Работаем в НЕсжатой форме, иначе порог всегда достигается
                // ровно и расстояние до него всегда ноль.
                double raw = TerrainNoise.SampleHeight(TerrainNoiseParams.FromTerrain(
                    PerlinAssetNoTail()), dir) * amp;
                double gap = System.Math.Abs(raw - thresholdMeters);
                if (gap < kneeGap)
                {
                    kneeGap = gap;
                    kneeLat = lat;
                    kneeLon = lon;
                    kneeH = raw;
                }
            }
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    порог сжатия = {0:N0} м. Опорные точки:\n"
            + "      гребень: {1:N0} м при ({2:F1}°, {3:F1}°)\n"
            + "      порог : {4:N0} м при ({5:F1}°, {6:F1}°), расстояние до порога {7:N0} м",
            thresholdMeters, bestH, bestLat * 180d / Math.PI, bestLon * 180d / Math.PI,
            kneeH, kneeLat * 180d / Math.PI, kneeLon * 180d / Math.PI, kneeGap));

        double crestAngle = TileNormalAngle(body, adv, par, amp, sea, bestLat, bestLon);
        double kneeAngle = TileNormalAngle(body, adv, par, amp, sea, kneeLat, kneeLon);

        ReportMargin("гребень ", crestAngle);
        ReportMargin("порог  ", kneeAngle);

        const double budget = 2e-3d;
        Check(crestAngle <= budget, "T133 tail-normal-parity",
            "нормали на гребне расходятся сильнее бюджета T84: " + crestAngle.ToString("E2", CultureInfo.InvariantCulture) + " рад");
        Check(kneeAngle <= budget, "T133 tail-normal-parity",
            "нормали у порога сжатия расходятся сильнее бюджета T84: " + kneeAngle.ToString("E2", CultureInfo.InvariantCulture) + " рад");

        return 0;
    }

    private static void ReportMargin(string label, double angle)
    {
        double budget = 2e-3d;
        double margin = budget / System.Math.Max(angle, 1e-30d);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    {0} worstNormalAngle = {1:E3} рад, запас до 2e-3 = {2:F1}×{3}",
            label, angle, margin, margin < 2d ? "  <-- МАЛО" : ""));
    }

    private static HeightfieldTerrain PerlinAssetNoTail()
    {
        HeightfieldTerrain t = PerlinAsset();
        t.TailKnee = 0d;
        return t;
    }

    private static double3 DirOf(double latRad, double lonRad)
    {
        double c = Math.Cos(latRad);
        return new double3(c * Math.Cos(lonRad), c * Math.Sin(lonRad), Math.Sin(latRad));
    }

    /// <summary>Одна плитка 0.5°/65 и worstNormalAngle по T84 сценарию 2.</summary>
    private static double TileNormalAngle(
        OrbitingBody body, HeightfieldTerrain adv, TerrainNoiseParams par,
        double amp, double sea, double centerLat, double centerLon)
    {
        body.EvaluateWorldState(0d, out Vector3d bodyPos, out _);

        const int res = 65;
        int m = res + 2;
        double step = 0.5d / (res - 1);
        var posD = new Vector3d[m, m];
        var posF = new UnityEngine.Vector3[m, m];

        for (int row = 0; row < m; row++)
        {
            double latDeg = (centerLat * (180d / Math.PI)) + ((row - ((m - 1) * 0.5d)) * step);
            latDeg = latDeg > 88d ? 88d : (latDeg < -88d ? -88d : latDeg);
            double lat = latDeg * (Math.PI / 180d);
            for (int col = 0; col < m; col++)
            {
                double lon = (centerLon * (180d / Math.PI)) + ((col - ((m - 1) * 0.5d)) * step);
                double lonRad = lon * (Math.PI / 180d);
                double h = adv.GetHeightMeters(body, lat, lonRad);
                body.GetSurfaceState(latDeg, lon, h, 0d, out Vector3d world, out _);
                posD[row, col] = world - bodyPos;

                double hf = TerrainNoise.SampleHeight(par, DirOf(lat, lonRad)) * amp;
                if (hf < sea)
                {
                    hf = sea;
                }

                body.GetSurfaceState(latDeg, lon, hf, 0d, out Vector3d worldF, out _);
                Vector3d relF = worldF - bodyPos;
                posF[row, col] = new UnityEngine.Vector3((float)relF.X, (float)relF.Y, (float)relF.Z);
            }
        }

        double worst = 0d;
        for (int row = 0; row < res; row++)
        {
            for (int col = 0; col < res; col++)
            {
                int r = row + 1;
                int c = col + 1;
                Vector3d dLonD = posD[r, c + 1] - posD[r, c - 1];
                Vector3d dLatD = posD[r + 1, c] - posD[r - 1, c];
                Vector3d nD = Vector3d.Cross(dLonD, dLatD).Normalized;
                UnityEngine.Vector3 dLonF = posF[r, c + 1] - posF[r, c - 1];
                UnityEngine.Vector3 dLatF = posF[r + 1, c] - posF[r - 1, c];
                UnityEngine.Vector3 nF = UnityEngine.Vector3.Cross(dLonF, dLatF);
                float lenF = nF.magnitude;
                nF = lenF > 1e-10f ? nF / lenF : posF[r, c].normalized;
                double angle = Math.Acos(Math.Min(1d, Math.Max(-1d, Vector3d.Dot(nD, new Vector3d(nF.x, nF.y, nF.z)))));
                if (angle > worst)
                {
                    worst = angle;
                }
            }
        }

        return worst;
    }

    /// <summary>
    /// Порог сжатия должен попадать между p99.8 и p99.95 высот. Смысл: сжатие
    /// обязано бытьRare - если порог ниже p99.8, оно трогает больше 0.2% точек
    /// и перестаёт быть "только хвост"; если выше p99.95, оно не режет
    /// ничего, и вершины остаются 11 км.
    ///
    /// Считается на ФИКСТУРЕ ИЗ АССЕТА, то есть с отключённым сжатием, иначе
    /// получился бы тавтологический тест.
    /// </summary>
    private static int Test134_TailThresholdPlacement()
    {
        const int n = 4000000;
        HeightfieldTerrain raw = PerlinAssetNoTail();
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(raw);
        double amp = raw.AmplitudeMeters;
        double thresholdMeters = raw.TailThreshold * amp;

        double[] h = new double[n];
        double ga = Math.PI * (3.0 - Math.Sqrt(5.0));
        for (int i = 0; i < n; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / n;
            double r = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            h[i] = TerrainNoise.SampleHeight(p, new double3(Math.Cos(a) * r, Math.Sin(a) * r, z)) * amp;
        }

        Array.Sort(h);
        double p998 = h[(int)(n * 0.998)];
        double p9995 = h[(int)(n * 0.9995)];
        double p999 = h[(int)(n * 0.999)];

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    порог TailThreshold*Amplitude = {0:N0} м\n"
            + "    p99.8  = {1:N0} м\n"
            + "    p99.9  = {2:N0} м\n"
            + "    p99.95 = {3:N0} м",
            thresholdMeters, p998, p999, p9995));

        Check(thresholdMeters >= p998, "T134 tail-threshold-placement",
            "порог сжатия ниже p99.8: сжатие затронет больше 0.2% точек, это уже не хвост. "
            + thresholdMeters.ToString("N0", CultureInfo.InvariantCulture) + " м < p99.8 "
            + p998.ToString("N0", CultureInfo.InvariantCulture) + " м");

        Check(thresholdMeters <= p9995, "T134 tail-threshold-placement",
            "порог сжатия выше p99.95: сжатие почти нечего резать, вершины останутся высокими. "
            + thresholdMeters.ToString("N0", CultureInfo.InvariantCulture) + " м > p99.95 "
            + p9995.ToString("N0", CultureInfo.InvariantCulture) + " м");

        return 0;
    }
}
