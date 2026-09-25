using System;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static int Test105_WaterQuery()
    {
        var body = new OrbitingBody
        {
            Name = "Terra",
            StandardGravitationalParameter = 3.986e14d,
            Radius = 6.371e6d,
            RotationPeriodSeconds = 3600d
        };
        body.Terrain = new HeightfieldTerrain
        {
            Seed = 42,
            AmplitudeMeters = 2000d,
            BaseFrequency = 3d,
            Octaves = 5,
            SeaLevelMeters = -500d
        };

        Check(WaterQuery.HasOcean(body), "T105 water-query", "океан есть (море −500 м)");
        Check(WaterQuery.TryGetSeaLevel(body, out double sea) && sea == -500d,
            "T105 water-query", "уровень моря −500 м");

        // Паритет сырого дна с TerrainNoise + кламп: raw ≤ clamped, на суше равны.
        bool parity = true;
        bool foundWater = false;
        bool foundLand = false;
        double waterLat = 0d;
        double waterLon = 0d;
        double landLat = 0d;
        double landLon = 0d;
        for (int i = 0; i <= 60 && parity; i++)
        {
            for (int j = 0; j <= 120 && parity; j++)
            {
                double latDeg = -85d + (170d * i / 60d);
                double lonDeg = -180d + (360d * j / 120d);
                double raw = WaterQuery.RawSeabedHeightAt(body, latDeg, lonDeg);
                double clamped = body.Terrain.GetHeightMeters(
                    body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d));
                if (double.IsNaN(raw) || raw > clamped + 1e-9d)
                {
                    parity = false;
                    break;
                }

                if (raw < sea)
                {
                    foundWater = true;
                    waterLat = latDeg;
                    waterLon = lonDeg;
                    // В океане кламп обязан держать ровно уровень моря.
                    if (clamped != sea)
                    {
                        parity = false;
                        break;
                    }

                    double depth = WaterQuery.WaterDepthAt(body, latDeg, lonDeg);
                    if (Math.Abs(depth - (sea - raw)) > 1e-9d || !WaterQuery.IsWaterAt(body, latDeg, lonDeg))
                    {
                        parity = false;
                        break;
                    }
                }
                else
                {
                    foundLand = true;
                    landLat = latDeg;
                    landLon = lonDeg;
                    if (Math.Abs(raw - clamped) > 1e-9d
                        || WaterQuery.WaterDepthAt(body, latDeg, lonDeg) != 0d
                        || WaterQuery.IsWaterAt(body, latDeg, lonDeg))
                    {
                        parity = false;
                        break;
                    }
                }
            }
        }

        Check(parity, "T105 water-query", "raw ≤ clamped, глубина = море − дно");
        Check(foundWater && foundLand, "T105 water-query",
            "на сетке есть и вода, и суша (вода " + waterLat.ToString("F1") + "," + waterLon.ToString("F1") + ")");

        // Погружение: точка на уровне моря → 0, на 10 м ниже → +10, выше → −.
        double surfaceR = WaterQuery.SeaSurfaceRadius(body);
        Check(!double.IsNaN(surfaceR) && Math.Abs(surfaceR - (body.Radius - 500d)) < 1e-6d,
            "T105 water-query", "радиус поверхности моря = R − 500");
        body.EvaluateWorldState(0d, out Vector3d bodyPos, out _);
        body.GetSurfaceState(waterLat, waterLon, -500d, 0d, out Vector3d atSurface, out _);
        body.GetSurfaceState(waterLat, waterLon, -510d, 0d, out Vector3d tenBelow, out _);
        body.GetSurfaceState(waterLat, waterLon, -490d, 0d, out Vector3d tenAbove, out _);
        double sub0 = WaterQuery.SubmersionDepthAt(body, atSurface, 0d);
        double subBelow = WaterQuery.SubmersionDepthAt(body, tenBelow, 0d);
        double subAbove = WaterQuery.SubmersionDepthAt(body, tenAbove, 0d);
        Check(Math.Abs(sub0) < 1e-6d && Math.Abs(subBelow - 10d) < 1e-6d && Math.Abs(subAbove + 10d) < 1e-6d,
            "T105 water-query", "погружение: 0/−10/+10 м → " + sub0.ToString("F3") + "/" + subBelow.ToString("F3") + "/" + subAbove.ToString("F3"));

        bool submergedInWater = WaterQuery.IsSubmergedAt(body, tenBelow, 0d, out double waterDepth);
        Check(submergedInWater && Math.Abs(waterDepth - 10d) < 1e-6d,
            "T105 water-query", "точка ниже поверхности над водой считается погружённой");
        Check(!WaterQuery.IsSubmergedAt(body, atSurface, 0d, out _)
            && !WaterQuery.IsSubmergedAt(body, tenAbove, 0d, out _),
            "T105 water-query", "точка на поверхности и выше водой не считается погружённой");

        body.GetSurfaceState(landLat, landLon, sea - 10d, 0d, out Vector3d landInsideSeaShell, out _);
        bool submergedOnLand = WaterQuery.IsSubmergedAt(body, landInsideSeaShell, 0d, out double landDepth);
        Check(WaterQuery.SubmersionDepthAt(body, landInsideSeaShell, 0d) > 0d
            && !submergedOnLand && Math.Abs(landDepth - 10d) < 1e-6d,
            "T105 water-query", "точка ниже моря, но внутри суши не считается погружённой");

        // Просвет над дном: точка на сыром дне → ~0.
        double rawWater = WaterQuery.RawSeabedHeightAt(body, waterLat, waterLon);
        body.GetSurfaceState(waterLat, waterLon, rawWater, 0d, out Vector3d atSeabed, out _);
        double clearance = WaterQuery.ClearanceAboveSeabedAt(body, atSeabed, 0d);
        Check(Math.Abs(clearance) < 1e-3d, "T105 water-query", "просвет на дне ~0 (" + clearance.ToString("E2") + ")");

        // SplashdownInfo согласован с запросами.
        WaterQuery.SplashdownInfo infoWater = WaterQuery.GetSplashdownInfo(body, waterLat, waterLon);
        WaterQuery.SplashdownInfo infoLand = WaterQuery.GetSplashdownInfo(body, landLat, landLon);
        Check(infoWater.IsWater && Math.Abs(infoWater.WaterDepthMeters - (-500d - rawWater)) < 1e-9d
            && infoWater.SeaLevelMeters == -500d && infoWater.SeabedHeightMeters == rawWater,
            "T105 water-query", "splashdown над водой: глубина " + infoWater.WaterDepthMeters.ToString("F0") + " м");
        Check(!infoLand.IsWater && infoLand.WaterDepthMeters == 0d,
            "T105 water-query", "splashdown над сушей: не вода");

        // Тело без океана: всё честно пустое.
        var dryBody = new OrbitingBody { Name = "Dry", Radius = 1000d };
        dryBody.Terrain = new SphericalTerrain();
        bool drySubmerged = WaterQuery.IsSubmergedAt(dryBody, Vector3d.Zero, 0d, out double dryDepth);
        Check(!WaterQuery.HasOcean(dryBody) && !WaterQuery.TryGetSeaLevel(dryBody, out _)
            && WaterQuery.WaterDepthAt(dryBody, 0d, 0d) == 0d
            && !WaterQuery.IsWaterAt(dryBody, 0d, 0d)
            && !drySubmerged && double.IsNaN(dryDepth)
            && double.IsNaN(WaterQuery.SeaSurfaceRadius(dryBody))
            && double.IsNaN(WaterQuery.SubmersionDepthAt(dryBody, Vector3d.Zero, 0d)),
            "T105 water-query", "тело без океана: HasOcean=false, везде NaN/0");
        WaterQuery.SplashdownInfo infoDry = WaterQuery.GetSplashdownInfo(dryBody, 0d, 0d);
        Check(!infoDry.IsWater && infoDry.WaterDepthMeters == 0d && double.IsNaN(infoDry.SeaLevelMeters),
            "T105 water-query", "splashdown без океана: не вода");
        return 0;
    }
}
