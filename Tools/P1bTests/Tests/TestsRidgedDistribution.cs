using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Распределение ridged-составляющей: legacy (сумма октав + smoothstep)
    /// против multifractal. Нужно для фита аффинной ремапы по двум параметрам —
    /// среднему и разбросу. Среднее и разброс меряются по всей сфере, квантили
    /// печатаются для глаза: по форме распределения видно, не ломает ли
    /// ремапа хвосты.
    ///
    /// Это разбор, а не приёмка: приёмку диапазона делает T117.
    /// </summary>
    private static int Test112c_RidgedDistribution()
    {
        const int n = 400000;
        var dirs = new double3[n];
        double ga = Math.PI * (3.0 - Math.Sqrt(5.0));
        for (int i = 0; i < n; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / n;
            double r = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            dirs[i] = new double3(Math.Cos(a) * r, Math.Sin(a) * r, z);
        }

        for (int style = 0; style <= 1; style++)
        {
            double legacyMean = 0d, legacySd = 0d, legacyP05 = 0d, legacyP95 = 0d;
            for (int mode = 0; mode <= 1; mode++)
            {
                HeightfieldTerrain t = SceneLikeTerrain();
                t.Seed = 24334543;
                t.NoiseStyle = style;
                t.RidgedMode = mode;
                t.RidgedSharpness = 2d;
                t.RidgedWeightGain = 2d;
                TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);

                var v = new double[n];
                double sum = 0d, sum2 = 0d;
                for (int i = 0; i < n; i++)
                {
                    double d = TerrainNoise.SampleRidged(p, dirs[i], 0.5d, 2d);
                    v[i] = d;
                    sum += d;
                    sum2 += d * d;
                }

                double mean = sum / n;
                double sd = Math.Sqrt(Math.Max(0.0, (sum2 / n) - (mean * mean)));
                Array.Sort(v);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "    {0,-10} {1,-14} min={2:F4} p05={3:F4} p50={4:F4} p95={5:F4} max={6:F4} "
                    + "среднее={7:F4} σ={8:F4}",
                    style == 0 ? "value" : "perlin",
                    mode == 0 ? "legacy" : "multifractal",
                    v[0], v[(int)(n * 0.05)], v[(int)(n * 0.5)], v[(int)(n * 0.95)], v[n - 1],
                    mean, sd));

                if (mode == 0)
                {
                    legacyMean = mean;
                    legacySd = sd;
                    legacyP05 = v[(int)(n * 0.05)];
                    legacyP95 = v[(int)(n * 0.95)];
                }
                else
                {
                    // Кандидаты на ремапу multifractal под статистику legacy.
                    // Аффинная по среднему и σ: среднее совпадает, но максимум
                    // вылезает за 1, а нижний хвост остаётся задранным — форма
                    // распределений разная, и сдвига её не выправить.
                    double a = legacySd / sd;
                    double b = legacyMean - (a * mean);
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "        аффинная по (среднее, σ):  a={0:F5} b={1:F5} -> min={2:F4} max={3:F4}",
                        a, b, b, (a * v[n - 1]) + b));

                    // Аффинная по краям диапазона: в [0,1] по построению, но
                    // среднее проваливается с 0.73 до 0.46 — та же проблема.
                    a = (legacyP95 - legacyP05) / v[n - 1];
                    b = legacyP05;
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "        аффинная по (p05, p95):    a={0:F5} b={1:F5} -> среднее={2:F4} (legacy {3:F4})",
                        a, b, (a * mean) + b, legacyMean));

                    // Тот же контраст, что у legacy: smoothstep. Диапазон [0,1]
                    // по построению, и обе схемы становятся сравнимы напрямую.
                    double ssMean = 0d, ssSum2 = 0d, ssMin = 1d, ssMax = 0d;
                    for (int i = 0; i < n; i++)
                    {
                        double raw = v[i];
                        double s = raw <= 0d ? 0d : (raw >= 1d ? 1d : raw * raw * (3d - (2d * raw)));
                        ssMean += s;
                        ssSum2 += s * s;
                        if (s < ssMin) ssMin = s;
                        if (s > ssMax) ssMax = s;
                    }

                    ssMean /= n;
                    double ssSd = Math.Sqrt(Math.Max(0.0, (ssSum2 / n) - (ssMean * ssMean)));
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "        + smoothstep (как у legacy): min={0:F4} max={1:F4} среднее={2:F4} σ={3:F4}"
                        + "   [legacy: среднее {4:F4} σ {5:F4}]",
                        ssMin, ssMax, ssMean, ssSd, legacyMean, legacySd));
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Складка гребня не должна выпускать |v| за единицу. Здесь был баг: у
    /// нормированного градиентного примитива |v| доходит до 1.43, и без
    /// насыщения `1 − |v|` уходил в минус, квадрат делал его снова
    /// положительным, и на линии |v| = 1 возникал ложный вторичный хребет.
    /// Тест печатает, что было бы без насыщения, и проверяет, что стало.
    /// </summary>
    private static int Test118_RidgeFoldRange()
    {
        const double amp = 1.43d; // измеренный пик октавы Перлина, T111
        double withoutSaturate = 1d - amp;
        double withoutSq = withoutSaturate * withoutSaturate;
        double withSaturate = TerrainNoise.RidgeFold(amp, 2);

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    |v|={0:F3}: без насыщения 1−|v|={1:F3}, после квадрата {2:F3} (ложный хребет); "
            + "с насыщением {3:F3}",
            amp, withoutSaturate, withoutSq, withSaturate));

        Check(withoutSq > 0d, "T118 ridge-fold",
            "без насыщения квадрат действительно даёт положительный сигнал на ложном гребне: "
            + withoutSq.ToString("F4", CultureInfo.InvariantCulture));

        // Диапазон складки на большой выборке и для обоих примитивов.
        double worst = 0d;
        var rng = new Random(60613);
        for (int i = 0; i < 2000000; i++)
        {
            for (int style = 0; style <= 1; style++)
            {
                double3 p = new double3(
                    (rng.NextDouble() * 40000d) - 20000d,
                    (rng.NextDouble() * 40000d) - 20000d,
                    (rng.NextDouble() * 40000d) - 20000d);
                double v = style == 0 ? TerrainNoise.ValueNoise(p) : TerrainNoise.GradientNoise(p);
                double s = TerrainNoise.RidgeFold(v, 2);
                if (s < 0d || s > 1d)
                {
                    worst = Math.Max(worst, Math.Min(-s, s - 1d));
                }
            }
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture, "    худший выход за [0,1] на 4M складках: {0:E3}", worst));
        Check(worst == 0d, "T118 ridge-fold",
            "складка держит [0,1] для обоих примитивов: худший выход "
            + worst.ToString("E3", CultureInfo.InvariantCulture));
        return 0;
    }
}
