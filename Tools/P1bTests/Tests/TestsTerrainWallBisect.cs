using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// Бисект разрыва высоты, найденного T900: между двумя сэмплами сетки
    /// (6.85 м) высота падает с ~977 м до ровно 12.00 м (высота береговой полки).
    /// Здесь: (1) ширина перехода с шагом 2 см; (2) какой слой шума за него
    /// отвечает — проверяется выключением слоёв по одному.
    /// </summary>
    private static void JumpLatLon(double latDeg, double lonDeg, double northM, double eastM,
        out double latOut, out double lonOut)
    {
        double lat = latDeg * (Math.PI / 180d);
        latOut = lat + (northM / ProbeRadius);
        lonOut = (lonDeg * (Math.PI / 180d)) + (eastM / (ProbeRadius * Math.Cos(lat)));
    }

    /// <summary>Максимальный перепад между соседними сэмплами сетки листа вдоль случайных линий.</summary>
    private static double MaxGridJump(HeightfieldTerrain terrain, OrbitingBody body, double extentM, int lines, out double atNorth, out double atEast)
    {
        var rng = new Random(4242);
        double worst = 0d;
        atNorth = 0d;
        atEast = 0d;
        for (int line = 0; line < lines; line++)
        {
            double a = rng.NextDouble() * Math.PI * 2d;
            double n = (rng.NextDouble() * 2d * extentM) - extentM;
            double e = (rng.NextDouble() * 2d * extentM) - extentM;
            double dn = Math.Cos(a) * LeafQuadMeters;
            double de = Math.Sin(a) * LeafQuadMeters;
            JumpLatLon(PlayerLat, PlayerLon, n, e, out double la, out double lo);
            double prev = terrain.GetRawHeightMeters(body, la, lo);
            for (int s = 1; s < 300; s++)
            {
                n += dn;
                e += de;
                JumpLatLon(PlayerLat, PlayerLon, n, e, out la, out lo);
                double h = terrain.GetRawHeightMeters(body, la, lo);
                double d = Math.Abs(h - prev);
                if (d > worst)
                {
                    worst = d;
                    atNorth = n;
                    atEast = e;
                }

                prev = h;
            }
        }

        return worst;
    }

    private static void ReportVariant(string name, HeightfieldTerrain terrain, OrbitingBody body, double extentM)
    {
        double jump = MaxGridJump(terrain, body, extentM, 250, out double n, out double e);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "    {0,-42} макс. перепад на шаге сетки {1,9:F2} м   в точке N{2:F0} E{3:F0}",
            name, jump, n, e));
    }

    private static int Test901_TerrainWallBisect()
    {
        var inv = CultureInfo.InvariantCulture;
        HeightfieldTerrain baseline = SceneTerrain();
        OrbitingBody body = ProbeBody(baseline);
        const double Extent = 4000d;

        Console.WriteLine("    выключение слоёв по одному (окрестность игрока ±4 км):");
        ReportVariant("базовый профиль (как в сцене)", baseline, body, Extent);

        HeightfieldTerrain noShelf = SceneTerrain();
        noShelf.BeachShelfWidth = 0d;
        ReportVariant("BeachShelfWidth = 0 (полки нет)", noShelf, body, Extent);

        HeightfieldTerrain constWidth = SceneTerrain();
        constWidth.BeachShelfWidthMaxScale = 1d;
        ReportVariant("BeachShelfWidthMaxScale = 1 (ширина постоянна)", constWidth, body, Extent);

        HeightfieldTerrain noWidthNoise = SceneTerrain();
        noWidthNoise.BeachShelfWidthNoiseFrequency = 0d;
        ReportVariant("BeachShelfWidthNoiseFrequency = 0", noWidthNoise, body, Extent);

        HeightfieldTerrain highShelf = SceneTerrain();
        highShelf.BeachShelfAltitudeMeters = 200d;
        ReportVariant("BeachShelfAltitude = 200 м (метка полки)", highShelf, body, Extent);

        HeightfieldTerrain noPlain = SceneTerrain();
        noPlain.PlainMix = 0d;
        ReportVariant("PlainMix = 0 (равнин нет)", noPlain, body, Extent);

        HeightfieldTerrain noFloor = SceneTerrain();
        noFloor.InteriorFloor = 0d;
        ReportVariant("InteriorFloor = 0", noFloor, body, Extent);

        HeightfieldTerrain noRidge = SceneTerrain();
        noRidge.RidgedMix = 0d;
        ReportVariant("RidgedMix = 0", noRidge, body, Extent);

        HeightfieldTerrain noTail = SceneTerrain();
        noTail.TailKnee = 0d;
        ReportVariant("TailKnee = 0", noTail, body, Extent);

        HeightfieldTerrain noOrogeny = SceneTerrain();
        noOrogeny.OrogenyFrequency = 0d;
        ReportVariant("OrogenyFrequency = 0", noOrogeny, body, Extent);

        HeightfieldTerrain noOcean = SceneTerrain();
        noOcean.OceanFloorDepth = 0d;
        noOcean.OceanShelfDepth = 0d;
        ReportVariant("OceanFloorDepth = 0 (старый океан)", noOcean, body, Extent);

        HeightfieldTerrain noContRidge = SceneTerrain();
        noContRidge.ContinentRidgeMix = 0d;
        ReportVariant("ContinentRidgeMix = 0", noContRidge, body, Extent);

        HeightfieldTerrain noLatBias = SceneTerrain();
        noLatBias.ContinentLatitudeBias = 0d;
        ReportVariant("ContinentLatitudeBias = 0", noLatBias, body, Extent);

        HeightfieldTerrain noWarp = SceneTerrain();
        noWarp.WarpStrength = 0d;
        noWarp.ContinentWarpStrength = 0d;
        ReportVariant("оба warp = 0", noWarp, body, Extent);

        HeightfieldTerrain noContinent = SceneTerrain();
        noContinent.ContinentFrequency = 0d;
        ReportVariant("ContinentFrequency = 0", noContinent, body, Extent);

        // ===== Ширина перехода: шаг 2 см через найденный разрыв =====
        double jumpNs = 0d;
        double jumpEe = 0d;
        double baseJump = MaxGridJump(baseline, body, Extent, 250, out jumpNs, out jumpEe);
        Console.WriteLine(string.Format(inv,
            "    бисект: разрыв {0:F1} м на N{1:F1} E{2:F1}; профиль с шагом 0.02 м поперёк линии север-юг:",
            baseJump, jumpNs, jumpEe));
        double prevH = double.NaN;
        double width = 0d;
        double northPrev = 0d;
        for (int k = -40; k <= 40; k++)
        {
            double north = jumpNs + (k * 0.02d);
            JumpLatLon(PlayerLat, PlayerLon, north, jumpEe, out double la, out double lo);
            double h = baseline.GetRawHeightMeters(body, la, lo);
            if (k % 8 == 0 || (h < 200d && h > 5d))
            {
                Console.WriteLine(string.Format(inv, "      N{0,10:F3}  h={1,10:F3} м", north, h));
            }

            if (!double.IsNaN(prevH) && Math.Abs(h - prevH) > 1d)
            {
                width += 0.02d;
                northPrev = north;
            }

            prevH = h;
        }

        Console.WriteLine(string.Format(inv,
            "    суммарная длина участков с перепадом >1 м на шаге 2 см: {0:F3} м (конец N{1:F3})", width, northPrev));

        // ===== Тот же разрыв, но без полки: есть ли он в чистом рельефе =====
        double ns2 = 0d;
        double ee2 = 0d;
        double noShelfJump = MaxGridJump(noShelf, body, Extent, 250, out ns2, out ee2);
        Console.WriteLine(string.Format(inv,
            "    без полки: макс. перепад {0:F2} м на N{1:F0} E{2:F0} (там же, где с полкой: {3})",
            noShelfJump, ns2, ee2,
            Math.Abs(ns2 - jumpNs) < 50d && Math.Abs(ee2 - jumpEe) < 50d ? "да" : "нет"));

        Check(true, "T901 terrain-wall-bisect", "бисект отработал");
        return 0;
    }
}
