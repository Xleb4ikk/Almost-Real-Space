using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Depth histogram: the distribution of the seafloor, not just the minimum.
    /// Earth reference points: mean depth 3.7 km, shelf to 200 m, maximum 11 km,
    /// and deeper than 6 km is only a small fraction of the area.
    ///
    /// Percentiles are computed AMONG OCEAN points (h &lt; 0), otherwise p50 would
    /// be positive land and the numbers would mean nothing. p1 is the deepest 1%
    /// of the ocean вЂ” the tail that breaks the picture.
    /// </summary>
    private static int Test135_OceanDepthHistogram()
    {
        const int n = 4000000;

        Histogram("legacy EarthLike (value)", SceneLikeTerrain(), n);
        Histogram("EarthLike_Perlin (Р±РµР· СЃР¶Р°С‚РёСЏ Р»РѕР¶Р°)", PerlinAssetNoDepth(), n);

        // Two tools from the plan. ContinentDepth sinks both the seafloor and the
        // shelf, so it is unsuitable as the only tool - visible in p25.
        HeightfieldTerrain lessDepth = PerlinAssetNoDepth();
        lessDepth.ContinentDepth = 0.9d;
        Histogram("Perlin, (a) ContinentDepth 1.2 -> 0.9", lessDepth, n);

        // Floor knee: chosen so min lands at the realistic 11 km. The seafloor
        // floor = (DepthThreshold - DepthKnee) * Amp, so threshold -0.30 and knee
        // 0.51 give a floor of -0.81, i.e. -11.1 km.
        foreach (double knee in new double[] { 0.40d, 0.51d, 0.62d })
        {
            HeightfieldTerrain k = PerlinAssetNoDepth();
            k.DepthKnee = knee;
            k.DepthThreshold = -0.30d;
            Histogram(string.Format(
                CultureInfo.InvariantCulture,
                "Perlin, (b) DepthKnee {0:F2} РїСЂРё РїРѕСЂРѕРіРµ -0.30 (РїРѕР» {1:N0} Рј)",
                knee, (-0.30d - knee) * 13662d), k, n);
        }

        return 0;
    }

    private static void Histogram(string label, HeightfieldTerrain t, int n)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double amp = p.AmplitudeMeters;
        var depths = new System.Collections.Generic.List<double>(n / 2);
        double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        for (int i = 0; i < n; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / n;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            double h = TerrainNoise.SampleHeight(p, new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z));
            if (h < 0d)
            {
                depths.Add(-h * amp);
            }
        }

        int m = depths.Count;
        depths.Sort();
        double p50 = depths[(int)(m * 0.50)];
        double p5 = depths[(int)(m * 0.05)];
        double p1 = depths[(int)(m * 0.01)];
        double p001 = depths[(int)(m * 0.001)];
        double min = depths[m - 1];
        double shelf = depths[(int)(m * 0.25)];
        double deep6 = 0d, deep11 = 0d;
        for (int i = 0; i < m; i++)
        {
            if (depths[i] > 6000d)
            {
                deep6 += 1d;
            }

            if (depths[i] > 11000d)
            {
                deep11 += 1d;
            }
        }

        Check(m > 0, "T135 ocean points " + label, m.ToString());
        Check(p50 > 0d && p50 < 6000d, "T135 p50 " + label, p50.ToString("F0"));
        Check(min > 0d && min < 30000d, "T135 min " + label, min.ToString("F0"));
        Check(shelf > 0d && shelf < 5000d, "T135 p25 shelf " + label, shelf.ToString("F0"));
        Check(deep6 / m < 0.6d, "T135 deep6 fraction " + label, (deep6 / m).ToString("P1"));

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0}\n"
            + "    РѕРєРµР°РЅ {1,6:P2} РїР»РѕС‰Р°РґРё | РґРЅРѕ: p50 {2,8:N0} Рј (РјРµРґРёР°РЅР°), p25 {3,7:N0} Рј (С€РµР»СЊС„),"
            + " p5 {4,8:N0} Рј, p1 {5,8:N0} Рј, p0.1 {6,8:N0} Рј, min {7,9:N0} Рј\n"
            + "    РіР»СѓР±Р¶Рµ 6 РєРј: {8,7:P3} РѕРєРµР°РЅР° | РіР»СѓР±Р¶Рµ 11 РєРј: {9,7:P3} РѕРєРµР°РЅР°"
            + " (Р—РµРјР»СЏ: РµРґРёРЅРёС†С‹ РїСЂРѕС†РµРЅС‚Р° Рё РјРµРЅСЊС€Рµ)",
            label, (double)m / n, p50, shelf, p5, p1, p001, min, deep6 / m, deep11 / m));
    }

    /// <summary>
    /// Coast transect: shelf, slope, seafloor вЂ” what the eye should read.
    /// Seafloor compression must not eat the shelf, and lowering ContinentDepth
    /// must not raise the bottom too much.
    ///
    /// Finds a point where the shape crosses sea level and prints the profile
    /// along the meridian. Two variants next to the control.
    /// </summary>
    private static int Test136_CoastProfile()
    {
        Console.WriteLine("    РїСЂРѕС„РёР»СЊ С‡РµСЂРµР· Р±РµСЂРµРі (РїРѕ РјРµСЂРёРґРёР°РЅСѓ, 0.02В° РЅР° С€Р°Рі, 120 С‚РѕС‡РµРє РІ СЃС‚РѕСЂРѕРЅСѓ):");

        HeightfieldTerrain control = PerlinAssetNoDepth();
        HeightfieldTerrain knee = PerlinAssetNoDepth();
        knee.DepthKnee = 0.51d;
        knee.DepthThreshold = -0.30d;

        CoastRun("A. РєРѕРЅС‚СЂРѕР»СЊ (ContinentDepth 1.2, Р»РѕР¶Рµ РЅРµ СЃР¶Р°С‚Рѕ)", control);
        Console.WriteLine();
        CoastRun("B. DepthKnee 0.51 РїСЂРё РїРѕСЂРѕРіРµ -0.30 (РїРѕР» -11.1 РєРј)", knee);

        return 0;
    }

    private static void CoastRun(string label, HeightfieldTerrain t)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double amp = p.AmplitudeMeters;

        // Find a sea-level crossing: the largest |gradient| among points near zero
        // gives the steepest coast, but a typical shelf is wanted.
        const int scan = 200000;
        double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        double bestH = 0d, bestGap = double.MaxValue;
        double3 coastDir = new double3(0d, 0d, 1d);
        for (int i = 0; i < scan; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / scan;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            double3 d = new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z);
            double h = TerrainNoise.SampleHeight(p, d);
            double gap = System.Math.Abs(h);
            if (gap < bestGap)
            {
                bestGap = gap;
                bestH = h;
                coastDir = d;
            }
        }

        Check(bestGap < 0.01d, "T136 coast crossing " + label, bestGap.ToString("E2"));
        double3 up = System.Math.Abs(coastDir.z) < 0.9d ? new double3(0d, 0d, 1d) : new double3(1d, 0d, 0d);
        double3 e1 = Normalize(math.cross(up, coastDir));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0} (Р±РµСЂРµРі РЅР° {1:N0} Рј)\n"
            + "      СЃСѓС€Р° в†’ РјРѕСЂРµ, С€Р°Рі 0.02В° (~2.2 РєРј):",
            label, bestH * amp));

        const double stepDeg = 0.02d;
        for (int k = 0; k <= 40; k++)
        {
            double ang = (k - 20) * stepDeg * System.Math.PI / 180d;
            double3 dir = Normalize((coastDir * System.Math.Cos(ang)) + (e1 * System.Math.Sin(ang)));
            double h = TerrainNoise.SampleHeight(p, dir) * amp;
            string mark = h > 0d ? "СЃСѓС€Р°" : "РјРѕСЂРµ";
            if (k % 4 == 0)
            {
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "      {0,7:F2}В° {1,5} {2,9:N0} Рј{3}",
                    (k - 20) * stepDeg, mark, h, Bar(h, amp)));
            }
        }

        // Far scale: from the coast to the seafloor. The near transect only reaches
        // 1.3 km while seafloor compression starts at 4.1 km, so without this
        // section profiles A and B look identical - proving nothing. Step 1.5В°,
        // span 45В° (~5,000 km) reaches the deep bottom.
        Console.WriteLine("      --- РґР°Р»СЊРЅРёР№ РјР°СЃС€С‚Р°Р±, С€Р°Рі 1.5В° (СЂР°Р·РјР°С… 45В°) ---");
        for (int k = 0; k <= 30; k++)
        {
            double ang = k * 1.5d * System.Math.PI / 180d;
            double3 dir = Normalize((coastDir * System.Math.Cos(ang)) + (e1 * System.Math.Sin(ang)));
            double h = TerrainNoise.SampleHeight(p, dir) * amp;
            string mark = h > 0d ? "СЃСѓС€Р°" : "РјРѕСЂРµ";
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "      {0,6:F1}В° {1,5} {2,9:N0} Рј{3}",
                k * 1.5d, mark, h, Bar(h, amp)));
        }
    }

    private static string Bar(double h, double amp)
    {
        int width = 40;
        double frac = System.Math.Max(-1d, System.Math.Min(1d, h / amp));
        int fill = (int)System.Math.Round(System.Math.Abs(frac) * width);
        char c = h > 0d ? '#' : '~';
        int pad = (width / 2) - (h > 0d ? fill : 0);
        return new string(' ', pad < 0 ? 0 : pad) + new string(c, fill);
    }

    private static double3 Normalize(double3 v)
    {
        double len = math.length(v);
        return len > 1e-12d ? v / len : new double3(1d, 0d, 0d);
    }

    private static HeightfieldTerrain PerlinAssetNoDepth()
    {
        HeightfieldTerrain t = PerlinTuned(0.13d, 0.5656d);
        t.AmplitudeMeters = 13662d;
        t.RidgedMix = 0.7d;
        t.ContinentThreshold = -0.4d;
        t.ContinentDepth = 1.2d;
        t.WarpStrength = 0.1d;
        t.Octaves = 10;
        t.Gain = 0.5d;
        t.PlainMix = 0.85d;
        t.PlainElevation = 0.1d;
        t.BeachHeightMeters = 12d;
        t.BeachShelfAltitudeMeters = 8d;
        t.BeachShelfWidth = 0.08d;
        t.DepthKnee = 0d;
        t.DepthThreshold = 0d;
        return t;
    }
}

