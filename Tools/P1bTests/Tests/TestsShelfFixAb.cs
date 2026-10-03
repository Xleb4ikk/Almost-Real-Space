using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// A/B правки береговой полки: один и тот же детерминированный набор точек
    /// считается ДО правки (файл-эталон) и ПОСЛЕ неё (сравнение). Отвечает на
    /// вопрос «рельеф остался тем же?» числами: сколько точек изменилось и на
    /// сколько, и где именно.
    /// </summary>
    private static void AbLatLon(double latDeg, double lonDeg, double northM, double eastM,
        out double latOut, out double lonOut)
    {
        double lat = latDeg * (Math.PI / 180d);
        latOut = lat + (northM / ProbeRadius);
        lonOut = (lonDeg * (Math.PI / 180d)) + (eastM / (ProbeRadius * Math.Cos(lat)));
    }

    private static List<double[]> ShelfAbPoints()
    {
        var pts = new List<double[]>(52000);
        var rng = new Random(31337);

        // 1) Окрестность кадра: ±3 км шагом 30 м (индекс 100*201+100 — точка кадра).
        for (int i = 0; i < 201; i++)
        {
            for (int j = 0; j < 201; j++)
            {
                AbLatLon(PlayerLat, PlayerLon, (i - 100) * 30d, (j - 100) * 30d, out double la, out double lo);
                pts.Add(new[] { la, lo });
            }
        }

        // 2) Восемь площадок ±6 км шагом 200 м в случайных местах планеты.
        for (int s = 0; s < 8; s++)
        {
            double lat0 = (rng.NextDouble() * 120d) - 60d;
            double lon0 = rng.NextDouble() * 360d;
            for (int i = 0; i < 61; i++)
            {
                for (int j = 0; j < 61; j++)
                {
                    AbLatLon(lat0, lon0, (i - 30) * 200d, (j - 30) * 200d, out double la, out double lo);
                    pts.Add(new[] { la, lo });
                }
            }
        }

        return pts;
    }

    private static int Test903_ShelfFixAb()
    {
        var inv = CultureInfo.InvariantCulture;
        HeightfieldTerrain terrain = SceneTerrain();
        OrbitingBody body = ProbeBody(terrain);
        List<double[]> pts = ShelfAbPoints();
        var h = new double[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            h[i] = terrain.GetRawHeightMeters(body, pts[i][0], pts[i][1]);
        }

        string path = Path.Combine(Directory.GetCurrentDirectory(), "shelf-fix-dump.txt");
        int playerIndex = (100 * 201) + 100;
        Console.WriteLine(string.Format(inv, "    точек: {0}; высота в точке кадра: {1:F6} м", h.Length, h[playerIndex]));

        if (!File.Exists(path))
        {
            var sb = new System.Text.StringBuilder(h.Length * 22);
            for (int i = 0; i < h.Length; i++)
            {
                sb.Append(h[i].ToString("R", inv)).Append('\n');
            }

            File.WriteAllText(path, sb.ToString());
            Console.WriteLine("    эталон записан: " + path);
            Check(true, "T903 shelf-fix-ab", "эталон снят");
            return 0;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length != h.Length)
        {
            Check(false, "T903 shelf-fix-ab", "длина эталона не совпала: " + lines.Length + " vs " + h.Length);
            return 0;
        }

        double maxAbs = 0d;
        double maxRise = 0d;
        double sumAbs = 0d;
        int over001 = 0;
        int over1 = 0;
        int over10 = 0;
        int over50 = 0;
        int over200 = 0;
        int maxIndex = 0;
        double playerBefore = double.NaN;
        int boxChanged = 0;
        const int BoxPoints = 201 * 201;
        for (int i = 0; i < h.Length; i++)
        {
            double before = double.Parse(lines[i], NumberStyles.Float, inv);
            double delta = h[i] - before;
            double d = Math.Abs(delta);
            sumAbs += d;
            if (d > 0.01d) over001++;
            if (d > 1d) over1++;
            if (d > 10d) over10++;
            if (d > 50d) over50++;
            if (d > 200d) over200++;
            if (delta > maxRise) maxRise = delta;
            if (i < BoxPoints && d > 0.01d) boxChanged++;
            if (d > maxAbs)
            {
                maxAbs = d;
                maxIndex = i;
            }

            if (i == playerIndex)
            {
                playerBefore = before;
            }
        }

        Console.WriteLine(string.Format(inv,
            "    было/стало: точка кадра {0:F6} → {1:F6} м (Δ {2:E3})",
            playerBefore, h[playerIndex], h[playerIndex] - playerBefore));
        Console.WriteLine(string.Format(inv,
            "    |Δ| : макс {0:F3} м, среднее {1:F4} м; точек >0.01 м: {2} ({3:P3}), >1 м: {4}, >10 м: {5}, >50 м: {6}, >200 м: {7}",
            maxAbs, sumAbs / h.Length, over001, (double)over001 / h.Length, over1, over10, over50, over200));
        Console.WriteLine(string.Format(inv,
            "    подъёмов рельефа правкой: {0} (макс +{1:F4} м) — полка теперь только опускает",
            maxRise > 0.01d ? "есть" : "нет", maxRise));
        Console.WriteLine(string.Format(inv,
            "    окрестность кадра ±3 км: изменилось {0} точек из {1} ({2:P2})",
            boxChanged, BoxPoints, (double)boxChanged / BoxPoints));
        Console.WriteLine(string.Format(inv,
            "    максимум Δ на lat {0:F6} lon {1:F6}",
            pts[maxIndex][0] * (180d / Math.PI), pts[maxIndex][1] * (180d / Math.PI)));

        Check(h[playerIndex] == playerBefore, "T903 shelf-fix-ab", "высота в точке кадра бит-в-бит прежняя");
        Check(maxRise <= 0.01d, "T903 shelf-fix-ab", "правка нигде не подняла рельеф");
        Check((double)over001 / h.Length < 0.10d, "T903 shelf-fix-ab", "изменилось меньше 10 % точек");
        return 0;
    }
}
