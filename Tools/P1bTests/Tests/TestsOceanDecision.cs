using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Итог Шага 3: глубины и профиль через берег ДО и ПОСЛЕ сжатия ложа,
    /// плюс альтернативный параметрический набор с более низким порогом.
    ///
    /// Альтернативный набор НЕ включён в ассет - он посчитан здесь, чтобы
    /// выбор оставался за владельцем профиля. Порог уводится глубже
    /// (~-4.5 км), чтобы сжатие не трогало материковый склон, и пол
    /// поднимается до ~8 км вместо ~11 км.
    /// </summary>
    private static int Test137_OceanDepthDecision()
    {
        const int n = 4000000;

        HeightfieldTerrain before = PerlinAssetNoDepth();
        HeightfieldTerrain chosen = PerlinAssetNoDepth();
        chosen.DepthKnee = 0.51d;
        chosen.DepthThreshold = -0.30d;
        HeightfieldTerrain alt = PerlinAssetNoDepth();
        alt.DepthKnee = 0.26d;
        alt.DepthThreshold = -0.33d;

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    A. выбранное (DepthKnee 0.51 @ -0.30, пол {0:N0} м) | "
            + "B. альтернатива (DepthKnee 0.26 @ -0.33, пол {1:N0} м)",
            (-0.30d - 0.51d) * 13662d, (-0.33d - 0.26d) * 13662d));
        Console.WriteLine();

        Depths("ДО  (сжатия ложа нет)", before, n);
        Depths("ПОСЛЕ (выбранное 0.51 @ -0.30)", chosen, n);
        Depths("АЛЬТ (0.26 @ -0.33, не включено)", alt, n);

        Console.WriteLine();
        Console.WriteLine("    меридиан через берег, шаг 1.5° (размах 45°) — ложе:");
        Console.WriteLine("      угол      ДО         ПОСЛЕ      разница");
        CoastTransect(before, chosen);
        Console.WriteLine();
        Console.WriteLine("    ближний масштаб через берег, шаг 0.02° — шельф и склон:");
        Console.WriteLine("      угол      ДО         ПОСЛЕ      разница");
        CoastNear(before, chosen);

        return 0;
    }

    private static void Depths(string label, HeightfieldTerrain t, int n)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double amp = p.AmplitudeMeters;
        var d = new System.Collections.Generic.List<double>(n / 2);
        double ga = System.Math.PI * (3.0 - System.Math.Sqrt(5.0));
        for (int i = 0; i < n; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / n;
            double r = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            double h = TerrainNoise.SampleHeight(p, new double3(System.Math.Cos(a) * r, System.Math.Sin(a) * r, z));
            if (h < 0d)
            {
                d.Add(-h * amp);
            }
        }

        d.Sort();
        int m = d.Count;
        double p5 = d[(int)(m * 0.05)];
        double p1 = d[(int)(m * 0.01)];
        double deep6 = 0d;
        for (int i = 0; i < m; i++)
        {
            if (d[i] > 6000d)
            {
                deep6 += 1d;
            }
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0,-36} океан {1,6:P2} | min {2,8:N0} м | p1 {3,8:N0} м | p5 {4,7:N0} м | глубже 6 км {5,7:P2}",
            label, (double)m / n, d[m - 1], p1, p5, deep6 / m));
    }

    /// <summary>Находит типичный берег и печатает дальний трансект для двух профилей.</summary>
    private static void CoastTransect(HeightfieldTerrain a, HeightfieldTerrain b)
    {
        double bestGap = double.MaxValue, clat = 0, clon = 0;
        FindCoast(a, ref bestGap, ref clat, ref clon);
        double3 origin = CoastDir(a, clat, clon);
        double3 e1 = Perp(origin);
        TerrainNoiseParams pa = TerrainNoiseParams.FromTerrain(a);
        TerrainNoiseParams pb = TerrainNoiseParams.FromTerrain(b);

        // Идём от берега наружу, пока не найдём глубокое ложе, и печатаем окрестность
        // найденной точки. Раньше печатались все строки сразу, но берег мог
        // оказаться мелким, и таблица выходила пустой.
        double3 deepDir = origin;
        double deepest = 0d;
        for (int k = 0; k <= 60; k++)
        {
            double ang = k * 1.5d * System.Math.PI / 180d;
            double3 dir = Unit((origin * System.Math.Cos(ang)) + (e1 * System.Math.Sin(ang)));
            double h = TerrainNoise.SampleHeight(pa, dir);
            if (h < deepest)
            {
                deepest = h;
                deepDir = dir;
            }

            if (h < -0.20d)
            {
                break;
            }
        }

        double3 deepE1 = Perp(deepDir);
        for (int k = 0; k <= 12; k++)
        {
            double ang = (k - 6) * 3.0d * System.Math.PI / 180d;
            double3 dir = Unit((deepDir * System.Math.Cos(ang)) + (deepE1 * System.Math.Sin(ang)));
            double ha = TerrainNoise.SampleHeight(pa, dir);
            double hb = TerrainNoise.SampleHeight(pb, dir);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "      {0,6:F1}° {1,10:N0} м {2,10:N0} м {3,10:N0} м",
                (k - 6) * 3.0d, ha * a.AmplitudeMeters, hb * b.AmplitudeMeters,
                (ha - hb) * a.AmplitudeMeters));
        }
    }

    /// <summary>Ближний трансект: шельф и материковый склон должны совпасть до метра.</summary>
    private static void CoastNear(HeightfieldTerrain a, HeightfieldTerrain b)
    {
        double bestGap = double.MaxValue, clat = 0, clon = 0;
        FindCoast(a, ref bestGap, ref clat, ref clon);
        double3 e1 = Perp(CoastDir(a, clat, clon));
        TerrainNoiseParams pa = TerrainNoiseParams.FromTerrain(a);
        TerrainNoiseParams pb = TerrainNoiseParams.FromTerrain(b);

        for (int k = 0; k <= 40; k++)
        {
            double ang = (k - 20) * 0.02d * System.Math.PI / 180d;
            double3 dir = Unit((CoastDir(a, clat, clon) * System.Math.Cos(ang)) + (e1 * System.Math.Sin(ang)));
            double ha = TerrainNoise.SampleHeight(pa, dir) * a.AmplitudeMeters;
            double hb = TerrainNoise.SampleHeight(pb, dir) * b.AmplitudeMeters;
            if (k % 5 != 0)
            {
                continue;
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "      {0,6:F2}° {1,9:N0} м {2,9:N0} м {3,9:N0} м",
                (k - 20) * 0.02d, ha, hb, ha - hb));
        }
    }

    private static void FindCoast(HeightfieldTerrain t, ref double bestGap, ref double clat, ref double clon)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        const int ni = 120, nj = 240;
        for (int i = 0; i < ni; i++)
        {
            double lat = (-78.0 + (156.0 * i / ni)) * System.Math.PI / 180d;
            for (int j = 0; j < nj; j++)
            {
                double lon = (-180.0 + (360.0 * j / nj)) * System.Math.PI / 180d;
                double3 d = new double3(
                    System.Math.Cos(lat) * System.Math.Cos(lon),
                    System.Math.Cos(lat) * System.Math.Sin(lon),
                    System.Math.Sin(lat));
                double h = TerrainNoise.SampleHeight(p, d);
                if (System.Math.Abs(h) > 0.05d)
                {
                    continue;
                }

                int land = 0, sea = 0;
                for (int a = -1; a <= 1; a++)
                {
                    for (int b = -1; b <= 1; b++)
                    {
                        double3 n = Unit(d + (new double3(a, b, a + b) * 0.02d));
                        if (TerrainNoise.SampleHeight(p, n) > 0d)
                        {
                            land++;
                        }
                        else
                        {
                            sea++;
                        }
                    }
                }

                if (land < 2 || sea < 2)
                {
                    continue;
                }

                double score = (double)System.Math.Min(land, sea);
                if (score > bestGap)
                {
                    bestGap = score;
                    clat = lat;
                    clon = lon;
                }
            }
        }
    }

    private static double3 CoastDir(HeightfieldTerrain t, double lat, double lon)
    {
        return new double3(
            System.Math.Cos(lat) * System.Math.Cos(lon),
            System.Math.Cos(lat) * System.Math.Sin(lon),
            System.Math.Sin(lat));
    }

    private static double3 Perp(double3 d)
    {
        double3 up = System.Math.Abs(d.z) < 0.9d ? new double3(0d, 0d, 1d) : new double3(1d, 0d, 0d);
        return Unit(math.cross(up, d));
    }

    private static double3 Unit(double3 v)
    {
        double len = math.length(v);
        return len > 1e-12d ? v / len : new double3(1d, 0d, 0d);
    }
}
