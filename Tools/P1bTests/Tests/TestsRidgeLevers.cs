using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Подбор уровня гребня БЕЗ gamma-ремапы. γ = 0.30 отвергнут: s^0.3
    /// сплющивает гребни (0.5 и 0.9 become 0.81 и 0.97 — разница 0.4 → 0.16),
    /// растягивает долины и имеет производную, уходящую к бесконечности при
    /// s → 0, что для нормалей плохо. Порядок рычагов: сначала показатель
    /// степени k в (1−|v|)^k, потом порог/смещение, и только в последнюю
    /// очередь γ, ограничив его снизу 0.6.
    ///
    /// Для каждого варианта печатается: гистограмма ridged (10 квантилей), доля
    /// суши, p99, кривизна гребня и максимальный наклон около s → 0.
    /// Критерий приёмки: доля суши 0.47 ± 0.02 ПРИ кривизне гребня не ниже,
    /// чем у ветки γ = 1.0.
    /// </summary>
    private static int Test123_RidgeLevelLevers()
    {
        const int n = 200000;
        var seeds = new int[8];
        for (int i = 0; i < seeds.Length; i++)
        {
            seeds[i] = 1013 + (i * 7919);
        }

        // Опорная ветка: k=2, γ=1, порог −0.1 — то, что в коде по умолчанию.
        // Её кривизна гребня и есть уровень, ниже которого опускаться нельзя
        // (критерий приёмки: суша 0.47 ± 0.02 ПРИ кривизне не ниже опорной).
        Summary baseline = Summarise("k=2 γ=1 порог −0.10 (ОПОРНАЯ)", seeds, n, 2, 1.0d, -0.1d);
        Console.WriteLine();
        Console.WriteLine("    === рычаг 1: показатель степени k в (1−|v|)^k ===");
        Summary[] byK = new Summary[4];
        for (int k = 1; k <= 4; k++)
        {
            byK[k - 1] = Summarise("k=" + k, seeds, n, k, 1.0d, -0.1d);
        }

        // Рычаг 2: порог континента ВНИЗ. continentRaw попадает в маску выше
        // порога, и понижение порога поднимает долю суши. Первая версия свипала
        // вверх и монотонно уводила сушу вниз, то есть мерила ровно
        // противоположное нужному.
        Console.WriteLine();
        Console.WriteLine("    === рычаг 2: порог континента вниз ===");
        double[] thresholds = { -0.1d, -0.2d, -0.3d, -0.4d, -0.5d, -0.6d };
        Summary[] byThresh = new Summary[thresholds.Length * 2];
        for (int ti = 0; ti < thresholds.Length; ti++)
        {
            for (int ki = 0; ki < 2; ki++)
            {
                int k = ki + 1;
                byThresh[(ki * thresholds.Length) + ti] = Summarise(
                    "k=" + k + " порог " + thresholds[ti].ToString("F2", CultureInfo.InvariantCulture),
                    seeds, n, k, 1.0d, thresholds[ti]);
            }
        }

        // Рычаг 3: γ, ограниченный снизу 0.6 — и с проверкой, что гребни не
        // сплющиваются (это и было причиной отвергнуть γ=0.3 в прошлый раз).
        Console.WriteLine();
        Console.WriteLine("    === рычаг 3: gamma ≥ 0.6 при k=2, порог −0.1 ===");
        Summary[] byGamma = new Summary[4];
        double[] gammas = { 1.0d, 0.8d, 0.7d, 0.6d };
        for (int gi = 0; gi < gammas.Length; gi++)
        {
            byGamma[gi] = Summarise(
                "γ=" + gammas[gi].ToString("F1", CultureInfo.InvariantCulture), seeds, n, 2, gammas[gi], -0.1d);
        }

        // Решение: суша в допуске И кривизна не ниже опорной.
        Summary best = baseline;
        string bestWhy = "опорная ветка";
        Action<Summary, string> consider = (s, why) =>
        {
            bool landOk = System.Math.Abs(s.Land - 0.47d) <= 0.02d;
            bool curvOk = s.Curvature >= baseline.Curvature;
            if (landOk && curvOk)
            {
                bool better = System.Math.Abs(s.Land - 0.47d) < System.Math.Abs(best.Land - 0.47d);
                if (better || ReferenceEquals(best, baseline))
                {
                    best = s;
                    bestWhy = why;
                }
            }
        };

        foreach (Summary s in byK) { consider(s, "рычаг k"); }
        foreach (Summary s in byThresh) { consider(s, "порог"); }
        foreach (Summary s in byGamma) { consider(s, "gamma"); }

        Console.WriteLine();
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    РЕШЕНИЕ: {0}  →  суша {1:F4} (Δ{2:+0.0000;-0.0000}), кривизна {3:F1} м "
            + "(опорная {4:F1}), p99 {5:F0} м, наклон {6:F3}",
            best.Label, best.Land, best.Land - 0.47d, best.Curvature, baseline.Curvature,
            best.P99, best.MaxSlope));

        bool anyGood = false;
        foreach (Summary s in byThresh)
        {
            if (System.Math.Abs(s.Land - 0.47d) <= 0.02d && s.Curvature >= baseline.Curvature)
            {
                anyGood = true;
            }
        }

        Check(anyGood, "T123 ridge-levers",
            "хотя бы один вариант без γ попадает в сушу 0.47 ± 0.02 при кривизне не ниже "
            + baseline.Curvature.ToString("F1", CultureInfo.InvariantCulture) + " м (опорная ветка)");
        return 0;
    }

    private struct Summary
    {
        public string Label;
        public double Land;
        public double P99;
        public double Curvature;
        public double MaxSlope;
    }

    private static Summary Summarise(string label, int[] seeds, int n, int sharpness, double gamma, double threshold)
    {
        double land = 0d, p99 = 0d, curv = 0d, slope = 0d;
        var dec = new double[10];
        foreach (int seed in seeds)
        {
            HeightfieldTerrain t = RidgeVariant(seed, sharpness, gamma, threshold);
            RidgeMetrics m = MeasureRidge(t, n, 1000);
            land += m.LandFraction;
            p99 += m.P99;
            curv += m.RidgeCurvature;
            slope += m.MaxSlope;
            for (int q = 0; q < 10; q++)
            {
                dec[q] += m.Deciles[q] / seeds.Length;
            }
        }

        land /= seeds.Length;
        p99 /= seeds.Length;
        curv /= seeds.Length;
        slope /= seeds.Length;
        string decStr = string.Join(" ", Array.ConvertAll(dec, d => d.ToString("F3", CultureInfo.InvariantCulture)));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    {0,-30} land={1:F4} (Δ{2:+0.0000;-0.0000}) p99={3,6:F0} крив={4,7:F1} накл={5,6:F3}",
            label, land, land - 0.47d, p99, curv, slope));
        Console.WriteLine("      квантили s: " + decStr);
        return new Summary
        {
            Label = label,
            Land = land,
            P99 = p99,
            Curvature = curv,
            MaxSlope = slope
        };
    }

    private static double AverageLand(int[] seeds, int sharpness, double gamma, double threshold)
    {
        double land = 0d;
        foreach (int seed in seeds)
        {
            land += LandFraction(RidgeVariant(seed, sharpness, gamma, threshold), 120000);
        }

        return land / seeds.Length;
    }

    private static HeightfieldTerrain RidgeVariant(int seed, int sharpness, double gamma, double threshold)
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        t.Seed = seed;
        t.NoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.MaskNoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.RidgedMode = (int)TerrainRidgedMode.Multifractal;
        t.RidgedSharpness = sharpness;
        t.RidgedGamma = gamma;
        t.ContinentThreshold = threshold;
        t.SlopeDamp = 0d;
        t.SlopeDampMode = (int)TerrainSlopeDampMode.Off;
        return t;
    }

    private struct RidgeMetrics
    {
        public double RidgeCurvature;
        public double MaxSlope;
        public double LandFraction;
        public double P99;
        public double[] Deciles;
        public int RidgeCount;
    }

    // Кривизна и наклон меряются ВДОЛЬ МЕРИДИАНОВ с фиксированным угловым
    // шагом. Первая версия брала вторую разность по индексу на сфере Фибоначчи,
    // где соседние индексы НЕ соседи в пространстве, и «кривизна» выходила
    // ~10000 м, то есть мерила разницу высот между случайными точками планеты.
    // Шаг обязан быть задан в метрах, иначе величина несравнима между
    // вариантами.
    private const double MeridianStepRad = 0.0009d; // ≈ 5.7 м у поверхности
    private const double PlanetRadiusMeters = 6371000d;
    private const int Meridians = 1000;

    private static RidgeMetrics MeasureRidge(HeightfieldTerrain t, int n, int ridgeCount)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double3[] dirs = SphereDirs(n);

        var ridged = new double[n];
        var height = new double[n];
        int land = 0;
        for (int i = 0; i < n; i++)
        {
            ridged[i] = TerrainNoise.SampleRidged(p, dirs[i], 0.5d, 2d);
            height[i] = TerrainNoise.SampleHeight(p, dirs[i]) * t.AmplitudeMeters;
            if (height[i] > t.SeaLevelMeters + (t.AmplitudeMeters * 0.001d))
            {
                land++;
            }
        }

        var deciles = new double[10];
        var sortedR = (double[])ridged.Clone();
        Array.Sort(sortedR);
        for (int q = 0; q < 10; q++)
        {
            deciles[q] = sortedR[(int)(((q + 0.5) * n) / 10.0)];
        }

        var sortedH = (double[])height.Clone();
        Array.Sort(sortedH);

        double stepMeters = MeridianStepRad * PlanetRadiusMeters;
        double curvSum = 0d;
        double maxSlope = 0d;
        int taken = 0;
        int count = 385;

        for (int m = 0; m < Meridians && taken < ridgeCount; m++)
        {
            double lat = -1.4d + (2.8d * m / Meridians);
            double cl = System.Math.Cos(lat);
            double sl = System.Math.Sin(lat);
            var h = new double[count];
            for (int i = 0; i < count; i++)
            {
                double lon = -System.Math.PI + ((2d * System.Math.PI * i) / (count - 1));
                h[i] = TerrainNoise.SampleHeight(
                    p, new double3(cl * System.Math.Cos(lon), sl, cl * System.Math.Sin(lon)))
                    * t.AmplitudeMeters;
            }

            for (int i = 1; i < count - 1 && taken < ridgeCount; i++)
            {
                double slope = System.Math.Abs(h[i + 1] - h[i]) / stepMeters;
                if (slope > maxSlope)
                {
                    maxSlope = slope;
                }

                if (!(h[i] > h[i - 1] && h[i] >= h[i + 1]))
                {
                    continue;
                }

                curvSum += System.Math.Abs(h[i - 1] - (2d * h[i]) + h[i + 1]);
                taken++;
            }
        }

        return new RidgeMetrics
        {
            RidgeCurvature = taken > 0 ? curvSum / taken : 0d,
            MaxSlope = maxSlope,
            LandFraction = (double)land / n,
            P99 = sortedH[(int)(n * 0.99)],
            Deciles = deciles,
            RidgeCount = taken
        };
    }

    private static void ReportRidgeVariant(
        string label, int[] seeds, int n, int sharpness, double gamma, double threshold,
        ref double baseCurvature, bool isBase)
    {
        double land = 0d, p99 = 0d, curv = 0d, slope = 0d;
        var dec = new double[10];
        foreach (int seed in seeds)
        {
            HeightfieldTerrain t = RidgeVariant(seed, sharpness, gamma, threshold);
            RidgeMetrics m = MeasureRidge(t, n, 1000);
            land += m.LandFraction;
            p99 += m.P99;
            curv += m.RidgeCurvature;
            slope += m.MaxSlope;
            for (int q = 0; q < 10; q++)
            {
                dec[q] += m.Deciles[q] / seeds.Length;
            }
        }

        land /= seeds.Length;
        p99 /= seeds.Length;
        curv /= seeds.Length;
        slope /= seeds.Length;
        if (isBase)
        {
            baseCurvature = curv;
        }

        string decStr = string.Join(" ", Array.ConvertAll(dec, d => d.ToString("F3", CultureInfo.InvariantCulture)));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    {0,-14} land={1:F4} (Δ{2:+0.0000;-0.0000})  p99={3,6:F0} м  кривизна={4,7:F2} м  наклон max={5,6:F3}  {6}",
            label, land, land - 0.47d, p99, curv, slope,
            curv >= baseCurvature ? "кривизна ОК" : "кривизна ХУЖЕ базы"));
        Console.WriteLine("      квантили s: " + decStr);
    }

    /// <summary>Детерминированные направления по Фибоначчи для замеров рельефа.</summary>
    private static double3[] SphereDirs(int n)
    {
        var dirs = new double3[n];
        double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        for (int i = 0; i < n; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / n;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            dirs[i] = new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z);
        }

        return dirs;
    }

    private static double LandFraction(HeightfieldTerrain t, int n)
    {
        double3[] dirs = SphereDirs(n);
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        int land = 0;
        for (int i = 0; i < n; i++)
        {
            double h = TerrainNoise.SampleHeight(p, dirs[i]) * t.AmplitudeMeters;
            if (h > t.SeaLevelMeters + (t.AmplitudeMeters * 0.001d))
            {
                land++;
            }
        }

        return (double)land / n;
    }
}
