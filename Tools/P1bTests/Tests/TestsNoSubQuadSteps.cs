using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// Регресс на разрывы масштаба сетки: высота не должна прыгать на целые
    /// сотни метров между СОСЕДНИМИ вершинами листа. Так выглядел баг
    /// береговой полки (T900/T901): на пороге континентальной маски высота
    /// падала с 977 м до 12 м за один квад 6.85 м, и меш рисовал обрыв
    /// частоколом вертикальных треугольников.
    ///
    /// Порог 250 м, а не «сколько получится»: природный рельеф планеты даёт
    /// на квад единицы метров (замер: 7.3 м в окрестности кадра), крутой, но
    /// НЕПРЕРЫВНЫЙ склон после правки — до ~30 м. 250 м ловит именно разрыв,
    /// а не крутизну.
    /// </summary>
    private static int Test904_NoSubQuadSteps()
    {
        var inv = CultureInfo.InvariantCulture;
        HeightfieldTerrain terrain = SceneTerrain();
        OrbitingBody body = ProbeBody(terrain);

        const double WorstAllowed = 250d;
        double worst = 0d;
        double worstLat = 0d;
        double worstLon = 0d;
        var rng = new Random(90210);
        int regions = 6;
        int lines = 150;
        int perLine = 300;

        // Окрестность кадра + случайные площадки по планете: разрыв может
        // оказаться в любом месте изолинии порога, а не только у игрока.
        for (int region = 0; region <= regions; region++)
        {
            double lat0 = region == 0 ? PlayerLat : (rng.NextDouble() * 140d) - 70d;
            double lon0 = region == 0 ? PlayerLon : rng.NextDouble() * 360d;
            double regionWorst = 0d;
            for (int line = 0; line < lines; line++)
            {
                double a = rng.NextDouble() * Math.PI * 2d;
                double north = (rng.NextDouble() * 6000d) - 3000d;
                double east = (rng.NextDouble() * 6000d) - 3000d;
                double dn = Math.Cos(a) * LeafQuadMeters;
                double de = Math.Sin(a) * LeafQuadMeters;
                AbLatLon(lat0, lon0, north, east, out double la, out double lo);
                double prev = terrain.GetRawHeightMeters(body, la, lo);
                for (int s = 1; s < perLine; s++)
                {
                    north += dn;
                    east += de;
                    AbLatLon(lat0, lon0, north, east, out la, out lo);
                    double h = terrain.GetRawHeightMeters(body, la, lo);
                    double d = Math.Abs(h - prev);
                    if (d > worst)
                    {
                        worst = d;
                        worstLat = la * (180d / Math.PI);
                        worstLon = lo * (180d / Math.PI);
                    }

                    if (d > regionWorst)
                    {
                        regionWorst = d;
                    }

                    prev = h;
                }
            }

            Console.WriteLine(string.Format(inv,
                "    площадка {0}: lat {1,8:F3} lon {2,8:F3} — макс. перепад на квад {3,8:F2} м",
                region, lat0, lon0, regionWorst));
        }

        Console.WriteLine(string.Format(inv,
            "    худшее по {0} площадкам: {1:F2} м (lat {2:F3} lon {3:F3}), допуск {4:F0} м",
            regions + 1, worst, worstLat, worstLon, WorstAllowed));
        Check(worst < WorstAllowed, "T904 no-sub-quad-steps",
            "нет перепада больше " + WorstAllowed.ToString("F0", inv) + " м между соседними вершинами листа");
        return 0;
    }
}
