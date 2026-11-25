using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Galilego.Universe;
using Unity.Mathematics;

// Offline crater-morphology laboratory. Not part of the game: lives outside Assets/
// and is run with
//   unity command run_script --file AgentScripts/CraterLab.cs --entry CraterLab.Run
// It reads the real profiles, renders shaded relief at several scales with physically
// scaled normals (1 px = its true size), prints the sampling cost, and writes PNGs
// under AgentScripts/out/lab/.
public static class CraterLab
{
    const string OutDir = "AgentScripts/out/lab";
    const int PX = 420;
    const int SS = 2; // supersampling factor

    static LunarParams LunarP;
    static MartianReliefParams MarsP;
    static int MarsSeed;
    static double LunarRadius;

    public static string Run()
    {
        Prepare();
        string bench = Bench();
        RunMoon();
        RunMars();
        return bench;
    }

    /// <summary>Single close patch, for fast iteration.</summary>
    public static string RunOne()
    {
        Prepare();
        string log = "";
        log += Prepare() + "\n";
        log += FindHighland() + "\n";
        log += One("one_mid", 2.4, 14.0) + "\n";
        log += One("one_near", 0.03, 10.0) + "\n";
        log += One("one_near2", 0.06, 10.0) + "\n";
        return log + Bench();
    }

    static double ProbeLon = 11.0;
    static double ProbeLat = 43.0;
    static double PitLon = 11.0;
    static double PitLat = 43.0;

    /// <summary>Numeric roughness check along a 300 m line at 1 m steps.</summary>
    public static string Rough()
    {
        string log = Prepare() + "\n" + FindHighland() + "\n";
        double spanM = 300.0;
        int n = 300;
        double step = (spanM / LunarRadius) / n;
        var hs = new double[n];
        for (int i = 0; i < n; i++)
        {
            hs[i] = LunarHeight(Move(ProbeLon, ProbeLat, (i - (n * 0.5)) * step, 0));
        }

        double sum = 0;
        for (int i = 1; i < n; i++)
        {
            sum += Math.Abs(hs[i] - hs[i - 1]);
        }

        string s = "Rough at " + ProbeLon.ToString("F1") + "," + ProbeLat.ToString("F1") + ": mean |dh| per metre = "
            + (sum / (n - 1)).ToString("F3") + " m; h[0..12] = ";
        for (int i = 0; i < 12; i++)
        {
            s += hs[i].ToString("F2") + " ";
        }

        return log + s;
    }

    /// <summary>Ground-level look: find the deepest pit near the probe and render at a range of scales.</summary>
    public static string RunPit()
    {
        Prepare();
        string log = Prepare() + "\n" + FindHighland() + "\n";
        log += FindPit(2500.0) + "\n";
        log += Patch("/pit_6km", 6000.0, 12.0) + "\n";
        log += Patch("/pit_1k5", 1500.0, 12.0) + "\n";
        log += Patch("/pit_600", 600.0, 12.0) + "\n";
        return log + Bench();
    }

    static string Patch(string name, double spanMeters, double alt)
    {
        return SaveSpherePatchStats(OutDir + "/" + name + ".png", 420, PitLon, PitLat,
            spanMeters / LunarRadius * 180.0 / Math.PI, alt,
            (dir) => LunarHeight(dir), LunarRadius, new Color(0.74f, 0.72f, 0.69f));
    }

    static string FindPit(double searchMeters)
    {
        double spanRad = searchMeters / LunarRadius;
        int n = 200;
        double step = spanRad / n;
        double best = 1e9;
        double bdx = 0;
        double bdy = 0;
        for (int y = 0; y < n; y++)
        {
            double dy = (y - ((n - 1) * 0.5)) * step;
            for (int x = 0; x < n; x++)
            {
                double dx = (x - ((n - 1) * 0.5)) * step;
                double v = LunarHeight(Move(ProbeLon, ProbeLat, dx, dy));
                if (v < best)
                {
                    best = v;
                    bdx = dx;
                    bdy = dy;
                }
            }
        }

        // convert the offset back into lon/lat: shift the probe by (bdx, bdy)
        double3 dir = Move(ProbeLon, ProbeLat, bdx, bdy);
        double lat = Math.Asin(Math.Max(-1, Math.Min(1, dir.z))) * 180 / Math.PI;
        double lon = Math.Atan2(dir.y, dir.x) * 180 / Math.PI;
        PitLon = lon;
        PitLat = lat;
        return "pit at lon " + lon.ToString("F2") + " lat " + lat.ToString("F2") + " h " + best.ToString("F1")
            + " offset " + (Math.Sqrt((bdx * bdx) + (bdy * bdy)) * LunarRadius).ToString("F0") + " m";
    }

    static string One(string name, double span, double alt)
    {
        return SaveSpherePatchStats(OutDir + "/" + name + ".png", 360, ProbeLon, ProbeLat, span, alt,
            (dir) => LunarHeight(dir), LunarRadius, new Color(0.74f, 0.72f, 0.69f));
    }

    static string FindHighland()
    {
        double best = 1e9;
        double bl = 0;
        double bb = 0;
        for (int i = 0; i < 3000; i++)
        {
            double z = ((i * 2654435761u) % 20000) / 10000.0 - 1.0;
            double lon = ((i * 7919) % 360) - 180;
            double lat = Math.Asin(Math.Max(-1, Math.Min(1, z))) * 180 / Math.PI;
            if (Math.Abs(lat) > 40.0)
            {
                continue;
            }

            double alb;
            double h = LunarRelief.Sample(LunarP, Dir(lon, lat), true, out alb);
            double score = alb - (h * 0.0);
            if (score < best)
            {
                best = score;
                bl = lon;
                bb = lat;
            }
        }

        ProbeLon = bl;
        ProbeLat = bb;
        return "highland at lon " + bl.ToString("F1") + " lat " + bb.ToString("F1") + " albedo " + best.ToString("F3");
    }

    public static string Bench()
    {
        const int N = 30000;
        var rng = new System.Random(7);
        double acc = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < N; i++)
        {
            double z = (rng.NextDouble() * 2) - 1;
            double a = rng.NextDouble() * Math.PI * 2;
            double r = Math.Sqrt(Math.Max(0, 1 - (z * z)));
            double al;
            acc += LunarRelief.Sample(LunarP, new double3(r * Math.Cos(a), r * Math.Sin(a), z), false, out al);
        }

        sw.Stop();
        double us = sw.Elapsed.TotalMilliseconds * 1000.0 / N;
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < N; i++)
        {
            double z = (rng.NextDouble() * 2) - 1;
            double a = rng.NextDouble() * Math.PI * 2;
            double r = Math.Sqrt(Math.Max(0, 1 - (z * z)));
            acc += MartianRelief.SampleHeight(MarsP, MarsSeed, new double3(r * Math.Cos(a), r * Math.Sin(a), z));
        }

        sw2.Stop();
        double us2 = sw2.Elapsed.TotalMilliseconds * 1000.0 / N;
        return "lunar " + us.ToString("F1") + " us/sample, mars " + us2.ToString("F1")
            + " us/sample (acc " + acc.ToString("F0") + ")";
    }

    public static string Prepare()
    {
        AssetDatabase.Refresh();
        TerrainProfile lunarProfile = LoadProfile("Assets/_Project/Profiles/Terrain/Lunar/Artemis.asset");
        TerrainProfile marsProfile = LoadProfile("Assets/_Project/Profiles/Terrain/MarsLike_Metus.asset");
        LunarP = lunarProfile.Lunar.ToParams(FindSeed(true));
        LunarP.Enabled = 1;
        LunarRadius = profileRadius(lunarProfile);
        MarsP = marsProfile.Martian;
        MarsP.Enabled = 1;
        MarsSeed = FindSeed(false);
        Directory.CreateDirectory(Path.GetFullPath(OutDir));
        return "params: regolith " + LunarP.RegolithAmp + " m / " + LunarP.RegolithBaseFreq + " Hz / "
            + LunarP.RegolithOctaves + " oct, mega " + LunarP.MegaAmp + " / " + LunarP.MegaOctaves
            + " oct, irr " + LunarP.CraterIrregularity + ", clusters " + LunarP.SecondaryClusters
            + ", seed " + LunarP.Seed + ", R " + LunarP.Radius
            + ", mareAge " + LunarP.MareAge + ", meanOffset " + LunarP.MeanOffset
            + ", density " + LunarP.CraterDensity + ", rimVariety " + LunarP.RimVariety;
    }

    static double profileRadius(TerrainProfile p)
    {
        return p != null && p.Lunar != null ? p.Lunar.RadiusMeters : 0;
    }

    public static string MarsMap()
    {
        Prepare();
        int w = 512;
        int h = 256;
        var hs = new double[w * h];
        for (int y = 0; y < h; y++)
        {
            double lat = 90.0 - ((y + 0.5) * 180.0 / h);
            for (int x = 0; x < w; x++)
            {
                double lon = ((x + 0.5) * 360.0 / w) - 180.0;
                hs[(y * w) + x] = MarsHeight(Dir(lon, lat));
            }
        }

        double lo = double.MaxValue;
        double hi = double.MinValue;
        for (int i = 0; i < hs.Length; i++)
        {
            if (hs[i] < lo) lo = hs[i];
            if (hs[i] > hi) hi = hs[i];
        }

        double mPerPx = (Math.PI * 400000.0 / 2.0) / w;
        double alt = 20.0 * Math.PI / 180.0;
        double az = 305.0 * Math.PI / 180.0;
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
                int xl = Math.Max(0, x - 1);
                int xr = Math.Min(w - 1, x + 1);
                int yu = Math.Max(0, y - 1);
                int yd = Math.Min(h - 1, y + 1);
                double gx = (hs[(y * w) + xr] - hs[(y * w) + xl]) / Math.Max(1, xr - xl) / mPerPx;
                double gy = (hs[(yd * w) + x] - hs[(yu * w) + x]) / Math.Max(1, yd - yu) / mPerPx;
                double nx = -gx;
                double ny = -gy;
                double inv = 1.0 / Math.Sqrt((nx * nx) + (ny * ny) + 1.0);
                double d = ((nx * sx) + (ny * sy) + sz) * inv;
                double shade = 0.035 + (1.05 * (d > 0 ? Math.Pow(d, 0.85) : 0));
                Color c = new Color((float)Math.Min(1, 0.70 * shade), (float)Math.Min(1, 0.47 * shade), (float)Math.Min(1, 0.31 * shade), 1f);
                px[(y * w) + x] = c;
            }
        }

        tex.SetPixels(px);
        tex.Apply(false);
        File.WriteAllBytes(OutDir + "/mars_map.png", tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        return "mars map, range " + (hi - lo).ToString("F0") + " m";
    }

    public static string MarsFind()
    {
        Prepare();
        int w = 360;
        int h = 180;
        var hs = new double[w * h];
        for (int y = 0; y < h; y++)
        {
            double lat = 90.0 - ((y + 0.5) * 180.0 / h);
            for (int x = 0; x < w; x++)
            {
                double lon = ((x + 0.5) * 360.0 / w) - 180.0;
                hs[(y * w) + x] = MarsHeight(Dir(lon, lat));
            }
        }

        // Count pits per 10-degree block: a pit is lower than all four neighbours by > 40 m.
        int bw = w / 10;
        int bh = h / 10;
        var count = new int[bw * bh];
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = (y * w) + x;
                double v = hs[i];
                if (v < hs[i - 1] - 40 && v < hs[i + 1] - 40 && v < hs[i - w] - 40 && v < hs[i + w] - 40)
                {
                    count[((y / 10) * bw) + (x / 10)]++;
                }
            }
        }

        int best = -1;
        int bx = 0;
        int by = 0;
        for (int y = 1; y < bh - 1; y++)
        {
            for (int x = 0; x < bw; x++)
            {
                int c = count[(y * bw) + x] + count[((y - 1) * bw) + x] + count[((y + 1) * bw) + x];
                if (c > best)
                {
                    best = c;
                    bx = x;
                    by = y;
                }
            }
        }

        double blon = ((bx + 0.5) * 10.0 * 360.0 / w) - 180.0 + 5.0;
        double blat = 90.0 - ((by + 0.5) * 10.0 * 180.0 / h);
        return "densest block: lon " + blon.ToString("F0") + " lat " + blat.ToString("F0") + " pits " + best;
    }

    /// <summary>Body mean height and flooded fraction — the mare level is relative to the mean.</summary>
    public static string Mean()
    {
        Prepare();
        const int N = 40000;
        var rng = new System.Random(11);
        double sum = 0;
        double lo = 1e30;
        double hi = -1e30;
        int below = 0;
        for (int i = 0; i < N; i++)
        {
            double z = (rng.NextDouble() * 2.0) - 1.0;
            double a = rng.NextDouble() * Math.PI * 2.0;
            double r = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));
            double h = LunarHeight(new double3(r * Math.Cos(a), r * Math.Sin(a), z));
            sum += h;
            if (h < lo) lo = h;
            if (h > hi) hi = h;
            if (h < LunarP.MareLevel) below++;
        }

        double mean = sum / N;
        return "mean " + mean.ToString("F1") + " m, lo " + lo.ToString("F0") + ", hi " + hi.ToString("F0")
            + ", below mare level " + (100.0 * below / N).ToString("F1") + " %"
            + ", current MeanOffset " + LunarP.MeanOffset.ToString("F1")
            + " → set " + (LunarP.MeanOffset - mean).ToString("F1");
    }

    public static string RunMare()
    {
        string log = Prepare() + "\n";
        double best = -1e9;
        double bl = 0;
        double bb = 0;
        for (int i = 0; i < 4000; i++)
        {
            double z = ((i * 2654435761u) % 20000) / 10000.0 - 1.0;
            double lon = ((i * 7919) % 360) - 180;
            double lat = Math.Asin(Math.Max(-1, Math.Min(1, z))) * 180 / Math.PI;
            if (Math.Abs(lat) > 35.0)
            {
                continue;
            }

            double alb;
            LunarRelief.Sample(LunarP, Dir(lon, lat), true, out alb);
            if (alb > best)
            {
                best = alb;
                bl = lon;
                bb = lat;
            }
        }

        ProbeLon = bl;
        ProbeLat = bb;
        log += "mare at lon " + bl.ToString("F1") + " lat " + bb.ToString("F1") + " albedo " + best.ToString("F3") + "\n";
        log += SaveSpherePatchStats(OutDir + "/lunar_mare_mid.png", PX, ProbeLon, ProbeLat, 2.4, 18.0,
            (dir) => LunarHeight(dir), LunarRadius, new Color(0.74f, 0.72f, 0.69f)) + "\n";
        log += SaveSpherePatchStats(OutDir + "/lunar_mare_wide.png", PX, ProbeLon, ProbeLat, 14.0, 18.0,
            (dir) => LunarHeight(dir), LunarRadius, new Color(0.74f, 0.72f, 0.69f)) + "\n";
        return log + Bench();
    }

    static void RunMoon()
    {
        FindHighland();
        double[,] shots =
        {
            { 14.0, 18.0 },
            { 2.4, 18.0 },
            { 0.5, 16.0 },
            { 0.12, 16.0 },
        };

        string[] names = { "wide", "mid", "close", "micro" };

        for (int i = 0; i < shots.GetLength(0); i++)
        {
            double span = shots[i, 0];
            double alt = shots[i, 1];
            SaveSpherePatchStats(OutDir + "/lunar_" + names[i] + ".png", PX, ProbeLon, ProbeLat, span, alt,
                (dir) => LunarHeight(dir), LunarRadius, new Color(0.74f, 0.72f, 0.69f));
        }
    }

    static void RunMars()
    {
        // Metus: the old southern highlands are the cratered part.
        double[,] shots =
        {
            { -10.0, 15.0, 2.0, 18.0 },
            { -10.0, 15.0, 0.4, 16.0 },
            { 59.0, 13.0, 0.4, 16.0 },
        };

        for (int i = 0; i < shots.GetLength(0); i++)
        {
            double lon = shots[i, 0];
            double lat = shots[i, 1];
            double span = shots[i, 2];
            double alt = shots[i, 3];
            SaveSpherePatchStats(OutDir + "/mars_" + i + ".png", PX, lon, lat, span, alt,
                (dir) => MarsHeight(dir), 400000.0, new Color(0.70f, 0.47f, 0.31f));
        }
    }

    static TerrainProfile LoadProfile(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
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

    static double LunarHeight(double3 dir)
    {
        double a;
        return LunarRelief.Sample(LunarP, dir, false, out a);
    }

    static double MarsHeight(double3 dir)
    {
        return MartianRelief.SampleHeight(MarsP, MarsSeed, dir) * MarsP.HeightUnitMeters;
    }

    // ---------------------------------------------------------------- rendering

    delegate double HeightFn(double3 dir);

    static void SaveSpherePatch(string path, int w, double lon0, double lat0, double spanDeg,
        double sunAltDeg, HeightFn f, double radius, Color tint)
    {
        double spanRad = spanDeg * Math.PI / 180.0;
        double stepRad = spanRad / w;
        int n = w * SS;
        double step = spanRad / n;
        double[] hs = new double[n * n];
        for (int y = 0; y < n; y++)
        {
            double dy = (y - ((n - 1) * 0.5)) * step;
            for (int x = 0; x < n; x++)
            {
                double dx = (x - ((n - 1) * 0.5)) * step;
                hs[(y * n) + x] = f(Move(lon0, lat0, dx, dy));
            }
        }

        double mPerPx = Math.Max(1e-6, (spanRad * radius) / n);
        WriteShaded(path, w, n, hs, mPerPx, sunAltDeg, tint);
    }

    static string SaveSpherePatchStats(string path, int w, double lon0, double lat0, double spanDeg,
        double sunAltDeg, HeightFn f, double radius, Color tint)
    {
        double spanRad = spanDeg * Math.PI / 180.0;
        int n = w * SS;
        double step = spanRad / n;
        double[] hs = new double[n * n];
        double lo = double.MaxValue;
        double hi = double.MinValue;
        for (int y = 0; y < n; y++)
        {
            double dy = (y - ((n - 1) * 0.5)) * step;
            for (int x = 0; x < n; x++)
            {
                double dx = (x - ((n - 1) * 0.5)) * step;
                double v = f(Move(lon0, lat0, dx, dy));
                hs[(y * n) + x] = v;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
        }

        double mPerPx = Math.Max(1e-6, (spanRad * radius) / n);
        WriteShaded(path, w, n, hs, mPerPx, sunAltDeg, tint);
        return Path.GetFileName(path) + " range " + (hi - lo).ToString("F1") + " m (lo " + lo.ToString("F0") + ")";
    }

    static void WriteShaded(string path, int outW, int n, double[] hs, double mPerPx, double sunAltDeg, Color tint)
    {        double lo = double.MaxValue;
        double hi = double.MinValue;
        for (int i = 0; i < hs.Length; i++)
        {
            if (hs[i] < lo) lo = hs[i];
            if (hs[i] > hi) hi = hs[i];
        }

        double az = 305.0 * Math.PI / 180.0;
        double alt = sunAltDeg * Math.PI / 180.0;
        double sx = Math.Cos(alt) * Math.Sin(az);
        double sy = Math.Cos(alt) * Math.Cos(az);
        double sz = Math.Sin(alt);

        int w = outW;
        double k = (double)n / w;
        Texture2D tex = new Texture2D(w, w, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        Color[] px = new Color[w * w];
        for (int y = 0; y < w; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (int)(((y + 0.5) * k) * n) + (int)((x + 0.5) * k);
                int xl = Math.Max(0, (int)((x + 0.5) * k) - 1);
                int xr = Math.Min(n - 1, (int)((x + 0.5) * k) + 1);
                int yu = Math.Max(0, (int)((y + 0.5) * k) - 1);
                int yd = Math.Min(n - 1, (int)((y + 0.5) * k) + 1);
                int row = (int)((y + 0.5) * k) * n;
                double gx = (hs[row + xr] - hs[row + xl]) / Math.Max(1, xr - xl) / mPerPx;
                double gy = (hs[(yd * n) + ((int)((x + 0.5) * k))] - hs[(yu * n) + ((int)((x + 0.5) * k))]) / Math.Max(1, yd - yu) / mPerPx;
                // heightfield normal in (east, north, up); the PNG is north-up
                double nx = -gx;
                double ny = -gy;
                double inv = 1.0 / Math.Sqrt((nx * nx) + (ny * ny) + 1.0);
                double d = ((nx * sx) + (ny * sy) + sz) * inv;
                double lit = d > 0 ? Math.Pow(d, 0.85) : 0;
                // small ambient so cast shadow areas are not pure black
                double shade = 0.035 + (1.05 * lit);
                double rel = (hs[i] - lo) / Math.Max(1e-6, hi - lo);
                double tone = 0.88 + (0.24 * rel);
                Color c = new Color(
                    (float)Math.Min(1.0, tint.r * shade * tone),
                    (float)Math.Min(1.0, tint.g * shade * tone),
                    (float)Math.Min(1.0, tint.b * shade * tone),
                    1f);
                px[(y * w) + x] = c;
            }
        }

        tex.SetPixels(px);
        tex.Apply(false);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        // False-colour copy: low = blue, high = red. Removes every lighting ambiguity.
        Color[] hc = new Color[w * w];
        for (int y = 0; y < w; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (int)(((y + 0.5) * k) * n) + (int)((x + 0.5) * k);
                float t = (float)((hs[i] - lo) / Math.Max(1e-6, hi - lo));
                hc[(y * w) + x] = t < 0.5f
                    ? new Color(0f, t * 2f, 1f - (t * 2f), 1f)
                    : new Color((t - 0.5f) * 2f, 1f - ((t - 0.5f) * 2f), 0f, 1f);
            }
        }

        Texture2D ht = new Texture2D(w, w, TextureFormat.RGBA32, false);
        ht.hideFlags = HideFlags.HideAndDontSave;
        ht.SetPixels(hc);
        ht.Apply(false);
        File.WriteAllBytes(Path.ChangeExtension(path, ".h.png"), ht.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(ht);
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
}
