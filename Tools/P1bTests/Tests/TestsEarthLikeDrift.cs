using System;
using System.Globalization;
using System.IO;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// Стенд и игра смотрят на РАЗНЫЕ копии пресета EarthLike, и они уже разошлись.
    ///
    /// Три источника:
    ///   1. TerrainProfile.CreateEarthLike() — C#-конструктор. Игрой не
    ///      вызывается вообще, только тестами.
    ///   2. Assets/_Project/Profiles/Terrain/EarthLike.asset — то, что реально
    ///      грузит сцена OutdoorsScene.
    ///   3. SceneLikeTerrain() — третья копия, отдельная от первых двух, и именно
    ///      по ней меряется статистика шума.
    ///
    /// Расхождение между (1) и (2) уже есть: ContinentDepth 0.9 против 1.2, плюс
    /// в ассете есть поля пляжа, которых в коде нет. Расхождение между (3) и (1):
    /// ColorDetailFrequency 1500 против 400, ColorDetailStrength 0.35 против 0.15.
    ///
    /// Ничего это не ловило: CreateEarthLike() нигде не сверяется с ассетом, и
    /// тесты меряют ту копию, которую никто не грузит. Тест читает .asset как
    /// текст и сверяет ключевые поля — YAML разбирать целиком незачем, формат
    /// стабильный и плоский.
    /// </summary>
    private static int Test117_EarthLikeProfileDrift()
    {
        string path = FindEarthLikeAsset();
        if (path == null)
        {
            // Стенд запускают не из репозитория (например, из CI-контейнера с
            // только исходниками). Молча пропускаем: отсутствие файла — не
            // расхождение значений.
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
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(
                dir.FullName, "Assets", "_Project", "Profiles", "Terrain", "EarthLike.asset");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            string nested = Path.Combine(
                dir.FullName, "Almost Real Space", "Assets", "_Project", "Profiles", "Terrain", "EarthLike.asset");
            if (File.Exists(nested))
            {
                return nested;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Скалярные поля блока Profile из YAML ассета. Плоский формат вида
    /// «  Имя: число», вложенность в разбор не входит: для этой задачи достаточно
    /// собрать все числовые пары верхнего уровня блока.
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

            // Вышли из блока Profile: следующая строка уровнем выше.
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
