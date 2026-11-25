using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Galilego.Universe;
using Unity.Mathematics;

// Offline shaded-relief previews for the two cratered bodies. Not part of the game:
// lives outside Assets/ and is run with
//   unity command run_script --file AgentScripts/PlanetPreview.cs --entry PlanetPreview.RunMaps
//   unity command run_script --file AgentScripts/PlanetPreview.cs --entry PlanetPreview.RunShots
// It only reads the profiles and writes PNGs under AgentScripts/out/.
public static class PlanetPreview
{
    const int MAP_W = 512;
    const int MAP_H = 256;
    const int SHOT = 256;
    const string OutDir = "AgentScripts/out";

    static LunarParams LunarP;
    static MartianReliefParams MarsP;
    static int MarsSeed;

    static void Prepare()
    {
        TerrainProfile lunarProfile = LoadProfile("Assets/_Project/Profiles/Terrain/Lunar/Artemis.asset");
        TerrainProfile marsProfile = LoadProfile("Assets/_Project/Profiles/Terrain/MarsLike_Metus.asset");
        LunarP = lunarProfile.Lunar.ToParams(FindSeed(true));
        LunarP.Enabled = 1;
        MarsP = marsProfile.Martian;
        MarsP.Enabled = 1;
        MarsSeed = FindSeed(false);
        Directory.CreateDirectory(Path.GetFullPath(OutDir));
        Debug.Log("[PlanetPreview] lunar seed=" + LunarP.Seed + " R=" + LunarP.Radius
            + " dens=" + LunarP.CraterDensity + " levels=" + LunarP.LevelCount
            + " mars seed=" + MarsSeed + " dens=" + MarsP.CraterDensity + " classes=" + MarsP.CraterClasses);
    }

    public static string RunMaps()
    {
        Prepare();
        SaveMap(OutDir + "/lunar_map.png", MAP_W, MAP_H, Lunar);
        SaveMap(OutDir + "/mars_map.png", MAP_W, MAP_H, Mars);
        return "maps";
    }

    public static string RunShots()
    {
        Prepare();
        RunMoonShots();
        RunMarsShots();
        return "shots";
    }

    public static string RunMoonShots()
    {
        Prepare();
        double[,] lunarShots =
        {
            { 11.0, 43.0, 6.0 },
            { -23.0, 128.0, 6.0 },
            { 47.0, 250.0, 6.0 },
            { 11.0, 43.0, 1.4 },
            { -23.0, 128.0, 1.4 },
        };
        for (int i = 0; i < lunarShots.GetLength(0); i++)
        {
            double lon = lunarShots[i, 0];
            double lat = lunarShots[i, 1];
            double deg = lunarShots[i, 2];
            SavePatch(OutDir + "/lunar_shot" + (i + 1) + ".png", SHOT, lon, lat, deg,
                (dx, dy) => LunarLocal(lon, lat, dx, dy), new Color(0.72f, 0.71f, 0.69f));
        }

        return "moon shots";
    }

    public static string RunMarsShots()
    {
        Prepare();
        double[,] marsShots =
        {
            { 42.0, 12.0, 9.0 },
            { -18.0, 73.0, 9.0 },
            { -2.0, 164.0, 9.0 },
            { 42.0, 12.0, 2.2 },
            { -18.0, 73.0, 2.2 },
        };
        for (int i = 0; i < marsShots.GetLength(0); i++)
        {
            double lon = marsShots[i, 0];
            double lat = marsShots[i, 1];
            double deg = marsShots[i, 2];
            SavePatch(OutDir + "/mars_shot" + (i + 1) + ".png", SHOT, lon, lat, deg,
                (dx, dy) => MarsLocal(lon, lat, dx, dy), new Color(0.72f, 0.46f, 0.28f));
        }

        return "mars shots";
    }

    static TerrainProfile LoadProfile(string path)
    {
        TerrainProfileAsset a = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(path);
        if (a == null)
        {
            throw new InvalidOperationException("profile not found: " + path);
        }

        return a.Profile;
    }

    static int FindSeed(bool lunar)
    {
        foreach (BodyAuthoring b in UnityEngine.Object.FindObjectsByType<BodyAuthoring>(FindObjectsInactive.Exclude))
        {
            if (b.TerrainPreset == null || b.TerrainPreset.Profile == null)
            {
                continue;
            }

            bool isLunar = b.TerrainPreset.Profile.Lunar != null && b.TerrainPreset.Profile.Lunar.Enabled;
            if (isLunar == lunar)
            {
                return b.TerrainSeed;
            }
        }

        return lunar ? 20260601 : 20260602;
    }

    static double Lunar(double lonDeg, double latDeg)
    {
        double a;
        return LunarRelief.Sample(LunarP, Dir(lonDeg, latDeg), true, out a);
    }

    static double Mars(double lonDeg, double latDeg)
    {
        return MartianRelief.SampleHeight(MarsP, MarsSeed, Dir(lonDeg, latDeg)) * MarsP.HeightUnitMeters;
    }

    static double LunarLocal(double lon0, double lat0, double dxRad, double dyRad)
    {
        double a;
        return LunarRelief.Sample(LunarP, Move(lon0, lat0, dxRad, dyRad), false, out a);
    }

    static double MarsLocal(double lon0, double lat0, double dxRad, double dyRad)
    {
        return MartianRelief.SampleHeight(MarsP, MarsSeed, Move(lon0, lat0, dxRad, dyRad)) * MarsP.HeightUnitMeters;
    }

    static double3 Dir(double lonDeg, double latDeg)
    {
        double la = latDeg * Math.PI / 180.0;
        double lo = lonDeg * Math.PI / 180.0;
        double c = Math.Cos(la);
        return new double3(c * Math.Cos(lo), c * Math.Sin(lo), Math.Sin(la));
    }

    static double3 Move(double lon0, double lat0, double dxRad, double dyRad)
    {
        double la = lat0 * Math.PI / 180.0;
        double lo = lon0 * Math.PI / 180.0;
        double3 up = Dir(lon0, lat0);
        double3 east = new double3(-Math.Sin(lo), Math.Cos(lo), 0.0);
        double3 north = math.cross(up, east);
        return math.normalize(up + (dxRad * east) + (dyRad * north));
    }

    delegate double HeightFn(double lon, double lat);

    static void SaveMap(string path, int w, int h, HeightFn f)
    {
        double[] hs = new double[w * h];
        for (int y = 0; y < h; y++)
        {
            double lat = 90.0 - ((y + 0.5) * 180.0 / h);
            for (int x = 0; x < w; x++)
            {
                double lon = ((x + 0.5) * 360.0 / w) - 180.0;
                hs[(y * w) + x] = f(lon, lat);
            }
        }

        WriteShaded(path, w, h, hs, 0.55, 25.0, new Color(0.74f, 0.70f, 0.64f));
    }

    static void SavePatch(string path, int w, double lon0, double lat0, double spanDeg, HeightFn f, Color tint)
    {
        double spanRad = spanDeg * Math.PI / 180.0;
        double stepRad = spanRad / w;
        double[] hs = new double[w * w];
        for (int y = 0; y < w; y++)
        {
            double dy = (y - ((w - 1) * 0.5)) * stepRad;
            for (int x = 0; x < w; x++)
            {
                double dx = (x - ((w - 1) * 0.5)) * stepRad;
                hs[(y * w) + x] = f(dx, dy);
            }
        }

        WriteShaded(path, w, w, hs, 0.5, 22.0, tint);
    }

    static void WriteShaded(string path, int w, int h, double[] hs, double reliefScale, double sunAltDeg, Color tint)
    {
        double lo = double.MaxValue;
        double hi = double.MinValue;
        for (int i = 0; i < hs.Length; i++)
        {
            if (hs[i] < lo) lo = hs[i];
            if (hs[i] > hi) hi = hs[i];
        }

        double span = Math.Max(1e-6, hi - lo);
        // Sun from the north-west, low over the horizon: this is what makes relief read.
        double az = 315.0 * Math.PI / 180.0;
        double alt = sunAltDeg * Math.PI / 180.0;
        double sx = Math.Cos(alt) * Math.Sin(az);
        double sy = Math.Cos(alt) * Math.Cos(az);
        double sz = Math.Sin(alt);

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        Color[] px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w) + x;
                int xl = x > 0 ? x - 1 : x;
                int xr = x < w - 1 ? x + 1 : x;
                int yu = y > 0 ? y - 1 : y;
                int yd = y < h - 1 ? y + 1 : y;
                double gx = (hs[(y * w) + xr] - hs[(y * w) + xl]) / Math.Max(1, xr - xl);
                double gy = (hs[(yd * w) + x] - hs[(yu * w) + x]) / Math.Max(1, yd - yu);
                double nx = -gx * reliefScale;
                double ny = -gy * reliefScale;
                double inv = 1.0 / Math.Sqrt((nx * nx) + (ny * ny) + 1.0);
                double d = Math.Max(0.0, ((nx * sx) + (ny * sy) + sz) * inv);
                double rel = (hs[i] - lo) / span;
                double shade = 0.35 + (0.65 * d);
                Color c = new Color(
                    (float)(tint.r * shade * (0.85 + (0.3 * rel))),
                    (float)(tint.g * shade * (0.85 + (0.3 * rel))),
                    (float)(tint.b * shade * (0.85 + (0.3 * rel))),
                    1f);
                px[i] = c;
            }
        }

        tex.SetPixels(px);
        tex.Apply(false);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }
}
