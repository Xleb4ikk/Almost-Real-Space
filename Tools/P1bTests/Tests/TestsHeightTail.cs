using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Хвост распределения высоты для legacy и Perlin-профиля. Нужен потому,
    /// что калибровка Perlin-профиля шла по p99, а горы интересуют по максимуму:
    /// при одном и том же p99 профиль с более тяжёлым хвостом даёт вершины,
    /// которые торчат выше облаков, и это видно только в хвосте.
    ///
    /// Считает всё, что зашито в профиле: ContinentDepth, AmplitudeMeters,
    /// ContinentThreshold, RidgedMix, warp, - и сравнивает два профиля на
    /// одной и той же выборке направлений, чтобы разница была честной.
    ///
    /// Отдельно печатает долю суши, среднюю высоту суши, p99, p99.9, p99.99
    /// и максимум, плюс высоту в метрах от центра планеты при максимуме.
    /// </summary>
    private static int Test129_HeightTail()
    {
        const int n = 4000000;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    выборка {0} направлений, одна и та же для обоих профилей\n", n));

        Measure("legacy EarthLike (value)", LegacyProfile(), n);
        Measure("EarthLike_Perlin", PerlinProfile(), n);

        return 0;
    }

    private static HeightfieldTerrain LegacyProfile()
    {
        return SceneLikeTerrain();
    }

    private static HeightfieldTerrain PerlinProfile()
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        t.NoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.MaskNoiseStyle = -1;
        t.RidgedMode = (int)TerrainRidgedMode.Multifractal;
        t.RidgedSharpness = 2;
        t.RidgedWeightGain = 2d;
        t.RidgedGamma = 1d;
        // Значения ниже сняты с EarthLike_Perlin.asset, а не подобраны:
        // AmplitudeMeters = 13662, RidgedMix = 0.7, ContinentDepth = 1.2,
        // ContinentThreshold = -0.4, WarpStrength = 0.1, Gain = 0.5, Octaves = 10.
        // Расхождение с ассетом здесь означало бы, что тест меряет не то.
        t.AmplitudeMeters = 13662d;
        t.RidgedMix = 0.7d;
        t.ContinentThreshold = -0.4d;
        t.ContinentDepth = 1.2d;
        t.WarpStrength = 0.1d;
        t.Octaves = 10;
        t.Gain = 0.5d;
        t.SlopeDamp = 0d;
        t.SlopeDampMode = (int)TerrainSlopeDampMode.Off;
        t.TailKnee = 0.13d;                                // TailKnee: 0.13
        t.TailThreshold = 0.5656d;                         // TailThreshold: 0.5656
        return t;
    }

    private static void Measure(string label, HeightfieldTerrain t, int n)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double[] h = new double[n];
        double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        for (int i = 0; i < n; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / n;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            double3 d = new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z);
            h[i] = TerrainNoise.SampleHeight(p, d);
        }

        Array.Sort(h);
        double min = h[0];
        double max = h[n - 1];
        double p50 = h[n / 2];
        double p99 = h[(int)(n * 0.99)];
        double p999 = h[(int)(n * 0.999)];
        double p9999 = h[(int)(n * 0.9999)];

        double landCount = 0d, landSum = 0d, oceanSum = 0d;
        for (int i = 0; i < n; i++)
        {
            if (h[i] > 0d)
            {
                landCount += 1d;
                landSum += h[i];
            }
            else
            {
                oceanSum += h[i];
            }
        }

        double landFrac = landCount / n;
        double meanLand = landCount > 0d ? landSum / landCount : 0d;
        double meanOcean = n - landCount > 0d ? oceanSum / (n - landCount) : 0d;

        double amp = p.AmplitudeMeters;
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0}\n"
            + "    амплитуда профиля        {1,12:N0} м\n"
            + "    суша                     {2,7:P2}   (море {3,7:P2})\n"
            + "    медиана                  {4,12:N0} м\n"
            + "    средняя высота суши      {5,12:N0} м\n"
            + "    средняя глубина моря     {6,12:N0} м\n"
            + "    p99                      {7,12:N0} м\n"
            + "    p99.9                    {8,12:N0} м\n"
            + "    p99.99                   {9,12:N0} м\n"
            + "    максимум (абс.)          {10,12:N0} м\n"
            + "    минимум                  {11,12:N0} м\n"
            + "    максимум / p99           {12,12:F2}\n"
            + "    максимум от центра (Земля) {13,10:N0} м\n"
            + "    облака 7000..8500 м ниже вершины: {14}\n",
            label, amp, landFrac, 1d - landFrac, p50 * amp, meanLand * amp, meanOcean * amp,
            p99 * amp, p999 * amp, p9999 * amp, max * amp, min * amp,
            max / p99, 6371000d + (max * amp),
            (max * amp) > 7000d ? (max * amp) > 8500d ? "ДА, выше обоих краёв" : "ДА, выше низа" : "нет"));

        // Проверка не "вершина ниже облаков", а математический потолок колена:
        // сжатие физически не может дать больше TailThreshold + TailKnee, поэтому
        // превышение означало бы, что сжатие не применилось. Раньше здесь стояло
        // max < 8500 (верх облаков) - это уже не критерий: максимум 8.8-9.0 км
        // принят осознанно, облака с вершинами пересекаются и это нормально.
        if (p.TailKnee > 0d)
        {
            double ceiling = (p.TailThreshold + p.TailKnee) * amp;
            Check(max * amp <= ceiling, "T129 height-tail",
                "вершина профиля '" + label + "' выше потолка сжатия: "
                + (max * amp).ToString("N0", CultureInfo.InvariantCulture) + " м > "
                + ceiling.ToString("N0", CultureInfo.InvariantCulture) + " м, значит сжатие хвоста не применилось");
        }
    }
}
