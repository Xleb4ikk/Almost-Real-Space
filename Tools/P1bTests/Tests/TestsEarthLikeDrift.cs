using System;
using System.Globalization;
using System.IO;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// The bench and the game read DIFFERENT copies of the EarthLike preset, and
    /// they can drift apart.
    ///
    /// Three sources:
    ///   1. TerrainProfile.CreateEarthLike() — C# constructor, used only by tests.
    ///   2. Assets/_Project/Profiles/Terrain/EarthLike.asset — what OutdoorsScene
    ///      actually loads.
    ///   3. SceneLikeTerrain() — a third copy, separate from the first two, and the
    ///      one the noise statistics are measured on.
    ///
    /// The test reads the .asset as text and compares key fields — a full YAML
    /// parse is unnecessary, the format is stable and flat.
    /// </summary>
    private static int Test117_EarthLikeProfileDrift()
    {
        string path = FindEarthLikeAsset();
        if (path == null)
        {
            // The bench may run outside the repo (e.g. a CI container with sources
            // only). Skip quietly: a missing file is not a value divergence.
            Console.WriteLine("    EarthLike.asset не найден рядом со стендом — проверка пропущена");
            Check(true, "T117 earthlike-drift", "ассет не найден, проверка пропущена");
            return 0;
        }

        TerrainProfile code = TerrainProfile.CreateEarthLike(9144d / 0.008d);
        var asset = ReadScalarFields(path);

        string[] tracked =
        {
            "AmplitudeMeters", "BaseFrequency", "Octaves", "SeaLevelMeters", "Lacunarity", "Gain",
            "RidgedMix", "ContinentFrequency", "ContinentOctaves", "ContinentThreshold",
            "ContinentSharpness", "ContinentDepth", "PlainMix", "PlainFrequency", "PlainOctaves",
            "PlainThreshold", "PlainSharpness", "PlainElevation", "DetailMix", "DetailFrequency",
            "DetailOctaves", "WarpStrength", "WarpFrequency", "WarpOctaves", "WarpSeedOffset",
            "BeachHeightMeters", "BeachShelfAltitudeMeters", "BeachShelfWidth",
            "BeachShelfWidthMaxScale", "BeachShelfWidthNoiseFrequency",
            "BeachShelfWidthNoiseOctaves",
        };

        var fieldValue = new System.Collections.Generic.Dictionary<string, double>();
        foreach (System.Reflection.FieldInfo f in typeof(TerrainProfile).GetFields())
        {
            if (f.FieldType == typeof(double) || f.FieldType == typeof(int))
            {
                fieldValue[f.Name] = Convert.ToDouble(f.GetValue(code));
            }
        }

        var mismatches = new System.Collections.Generic.List<string>();
        foreach (string name in tracked)
        {
            if (!asset.ContainsKey(name))
            {
                mismatches.Add(name + " (нет в ассете)");
                continue;
            }

            if (!fieldValue.TryGetValue(name, out double expected))
            {
                continue;
            }

            if (Math.Abs(asset[name] - expected) > 1e-9d)
            {
                mismatches.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: код {1} ≠ ассет {2}", name, expected, asset[name]));
            }
        }

        foreach (string m in mismatches)
        {
            Console.WriteLine("    расхождение — " + m);
        }

        Check(mismatches.Count == 0, "T117 earthlike-drift",
            "CreateEarthLike() совпадает с EarthLike.asset"
            + (mismatches.Count == 0 ? string.Empty : "; расхождений: " + mismatches.Count));
        return 0;
    }

    private static string FindEarthLikeAsset()
    {
        return FindProfileAsset("EarthLike.asset");
    }

    /// <summary>Searches for a profile asset upward from the bench working directory.</summary>
    private static string FindProfileAsset(string fileName)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(
                dir.FullName, "Assets", "_Project", "Profiles", "Terrain", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            string nested = Path.Combine(
                dir.FullName, "Almost Real Space", "Assets", "_Project", "Profiles", "Terrain", fileName);
            if (File.Exists(nested))
            {
                return nested;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Scalar fields of the Profile block from the asset YAML. A flat format like
    /// "  Name: number"; nesting is not parsed: for this task it is enough to
    /// collect every numeric top-level pair of the block.
    /// </summary>
    private static System.Collections.Generic.Dictionary<string, double> ReadScalarFields(string path)
    {
        var result = new System.Collections.Generic.Dictionary<string, double>();
        bool inProfile = false;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("  Profile:", StringComparison.Ordinal))
            {
                inProfile = true;
                continue;
            }

            if (!inProfile)
            {
                continue;
            }

            // Left the Profile block: the next line is one level up.
            if (line.Length > 0 && !line.StartsWith("    ", StringComparison.Ordinal))
            {
                break;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();
            if (key.Length == 0 || value.Length == 0 || value.IndexOf('{') >= 0)
            {
                continue;
            }

            double parsed;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                result[key] = parsed;
            }
        }

        return result;
    }
}
