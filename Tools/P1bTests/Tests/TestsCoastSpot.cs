using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Печатает координаты хорошего берега для съёмки. Считается офлайн на
    /// стенде, потому что поиск по сетке в Editor упирается в лимит главного
    /// потока (GetHeightMeters там дорогой), а здесь та же функция стоит
    /// копейки.
    ///
    /// «Хороший» = есть и суша, и море в пределах ~0.6°, и в радиусе 40°
    /// встречается глубокое дно, чтобы в кадр попал и шельф, и ложе.
    /// </summary>
    private static int Test138_CoastSpot()
    {
        HeightfieldTerrain t = PerlinAssetNoDepth();
        t.TailKnee = 0.13d;
        t.TailThreshold = 0.5656d;
        t.DepthKnee = 0.51d;
        t.DepthThreshold = -0.30d;
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double amp = p.AmplitudeMeters;

        double bestScore = double.MinValue;
        double bestLat = 0d, bestLon = 0d;

        const int ni = 120, nj = 240;
        for (int i = 0; i < ni; i++)
        {
            double latDeg = -60d + (120d * i / ni);
            for (int j = 0; j < nj; j++)
            {
                double lonDeg = -180d + (360d * j / nj);
                double h = TerrainNoise.SampleHeight(p, DirDeg(latDeg, lonDeg)) * amp;
                if (System.Math.Abs(h) > 800d)
                {
                    continue;
                }

                int land = 0, sea = 0;
                for (int a = -1; a <= 1; a++)
                {
                    for (int b = -1; b <= 1; b++)
                    {
                        if (a == 0 && b == 0)
                        {
                            continue;
                        }

                        double hh = TerrainNoise.SampleHeight(p, DirDeg(latDeg + (a * 0.5d), lonDeg + (b * 0.5d))) * amp;
                        if (hh > 0d)
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

                // Глубина неподалёку: ищем океан глубже 2 км в радиусе 25°.
                double deep = 0d;
                for (int a = 0; a < 12; a++)
                {
                    double da = a * 25d * System.Math.PI / 180d;
                    for (int b = 0; b < 24; b++)
                    {
                        double db = b * 15d * System.Math.PI / 180d;
                        double3 d = DirDeg(latDeg, lonDeg);
                        double3 x = d;
                        double c = System.Math.Cos(da), s = System.Math.Sin(da);
                        double x2 = (x.x * c) - (x.y * s);
                        double y2 = (x.x * s) + (x.y * c);
                        double3 y = new double3(x2, y2, x.z);
                        double c2 = System.Math.Cos(db), s2 = System.Math.Sin(db);
                        double3 rot = new double3(
                            (y.x * c2) - (y.z * s2),
                            y.y,
                            (y.x * s2) + (y.z * c2));
                        double hh = TerrainNoise.SampleHeight(p, rot) * amp;
                        if (hh < deep)
                        {
                            deep = hh;
                        }
                    }
                }

                double score = (double)System.Math.Min(land, sea) - (System.Math.Abs(h) * 0.01d)
                    + (deep < -2000d ? 3d : 0d);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLat = latDeg;
                    bestLon = lonDeg;
                }
            }
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "    БЕРЕГ ДЛЯ СЪЁМКИ: lat={0:F4} lon={1:F4}  (score {2:F1})",
            bestLat, bestLon, bestScore));
        Console.WriteLine("    ожидаемая высота рельефа: " + (TerrainNoise.SampleHeight(p, DirDeg(bestLat, bestLon)) * amp).ToString("N0", CultureInfo.InvariantCulture) + " м");
        return 0;
    }

    private static double3 DirDeg(double latDeg, double lonDeg)
    {
        double lat = latDeg * System.Math.PI / 180d;
        double lon = lonDeg * System.Math.PI / 180d;
        double c = System.Math.Cos(lat);
        return new double3(c * System.Math.Cos(lon), c * System.Math.Sin(lon), System.Math.Sin(lat));
    }
}
