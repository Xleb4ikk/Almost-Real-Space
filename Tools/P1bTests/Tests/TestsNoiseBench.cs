using System;
using System.Diagnostics;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    private const int NoiseBenchSamples = 200000;

    /// <summary>
    /// Равномерная сетка направлений через золотое сечение — та же схема, что в
    /// T87/T107, чтобы доли суши отсюда и оттуда были сравнимы.
    /// </summary>
    private static double3[] NoiseBenchDirections(int n)
    {
        var dirs = new double3[n];
        double ga = Math.PI * (3.0 - Math.Sqrt(5.0));
        for (int i = 0; i < n; i++)
        {
            double z = 1.0 - (2.0 * (i + 0.5)) / n;
            double r = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));
            double a = ga * i;
            dirs[i] = new double3(Math.Cos(a) * r, Math.Sin(a) * r, z);
        }

        return dirs;
    }

    private struct NoiseBenchStats
    {
        public double Min;
        public double Max;
        public double StdDev;
        public double LandFraction;
        public double Peak99;
    }

    /// <summary>
    /// Снимок статистики формы. Это ровно тот набор чисел, который уезжает при
    /// смене шума, и на который нацеплены T87 (доля суши), T89 (спектр) и
    /// T94 (высота пиков) — держать его под рукой дешевле, чем ловить падения
    /// по трём тестам сразу.
    /// </summary>
    private static NoiseBenchStats NoiseBenchMeasure(HeightfieldTerrain t, double3[] dirs)
    {
        TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
        double min = double.MaxValue;
        double max = double.MinValue;
        double sum = 0.0;
        double sum2 = 0.0;
        int land = 0;
        int count = dirs.Length;
        var heights = new double[count];
        for (int i = 0; i < count; i++)
        {
            double h = TerrainNoise.SampleHeight(p, dirs[i]) * t.AmplitudeMeters;
            heights[i] = h;
            if (h < min) min = h;
            if (h > max) max = h;
            sum += h;
            sum2 += h * h;
            if (h > t.SeaLevelMeters + (t.AmplitudeMeters * 0.001d))
            {
                land++;
            }
        }

        double mean = sum / count;
        Array.Sort(heights);
        return new NoiseBenchStats
        {
            Min = min,
            Max = max,
            StdDev = Math.Sqrt(Math.Max(0.0, (sum2 / count) - (mean * mean))),
            LandFraction = (double)land / count,
            Peak99 = heights[(int)(count * 0.99)]
        };
    }

    /// <summary>
    /// Замер в три повтора, берём минимум: это чистая стоимость вычисления, а не
    /// время под нагрузкой (GC, прерывания, троттлинг) — повторы нужны, чтобы
    /// разница между двумя схемами шума не съедалась разбросом.
    /// </summary>
    private static double NoiseBenchMs(HeightfieldTerrain t, double3[] dirs, int repeats)
    {
        double best = double.MaxValue;
        double sink = 0.0;
        for (int r = 0; r < repeats; r++)
        {
            TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < dirs.Length; i++)
            {
                sink += TerrainNoise.SampleHeight(p, dirs[i]);
            }

            sw.Stop();
            if (sw.Elapsed.TotalMilliseconds < best)
            {
                best = sw.Elapsed.TotalMilliseconds;
            }
        }

        // Не даём компилятору выкинуть цикл: сумма используется в Nan-проверке.
        if (double.IsNaN(sink))
        {
            Console.WriteLine("  (!!) nan в сумме");
        }

        return best;
    }

    private static void NoiseBenchReport(
        string label, HeightfieldTerrain t, double3[] dirs, double baselineMs)
    {
        double ms = NoiseBenchMs(t, dirs, 3);
        NoiseBenchStats s = NoiseBenchMeasure(t, dirs);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0,-24} {1,7:F2} ms  x{2,5:F2}  min={3,7:F0} max={4,7:F0} sd={5,6:F0} "
            + "land={6,5:F3} p99={7,7:F0} m",
            label, ms, baselineMs > 0.0 ? ms / baselineMs : 1.0,
            s.Min, s.Max, s.StdDev, s.LandFraction, s.Peak99));
    }

    /// <summary>SceneLikeTerrain с одной осью измерения выключенной.</summary>
    private static HeightfieldTerrain NoiseBenchVariant(
        bool warp, bool ridged, bool plains, int octaves)
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        t.Octaves = octaves;
        t.NoiseStyle = (int)TerrainNoiseStyle.Value;
        t.RidgedMode = (int)TerrainRidgedMode.Legacy;
        if (!warp)
        {
            t.WarpStrength = 0d;
        }

        if (!ridged)
        {
            t.RidgedMix = 0d;
        }

        if (!plains)
        {
            t.PlainMix = 0d;
        }

        return t;
    }

    /// <summary>Та же конфигурация, но с одним из трёх новых параметров.</summary>
    private static HeightfieldTerrain NoiseBenchStyled(
        TerrainNoiseStyle style, TerrainRidgedMode ridgedMode, double slopeDamp, int octaves)
    {
        HeightfieldTerrain t = NoiseBenchVariant(true, true, true, octaves);
        t.NoiseStyle = (int)style;
        t.RidgedMode = (int)ridgedMode;
        t.SlopeDamp = slopeDamp;
        t.SlopeDampMode = (int)TerrainSlopeDampMode.Off;
        return t;
    }

    /// <summary>То же, но с явно выбранным источником наклона.</summary>
    private static HeightfieldTerrain NoiseBenchDamped(
        TerrainSlopeDampMode mode, double slopeDamp, int octaves)
    {
        HeightfieldTerrain t = NoiseBenchVariant(true, true, true, octaves);
        t.NoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.RidgedMode = (int)TerrainRidgedMode.Legacy;
        t.SlopeDamp = slopeDamp;
        t.SlopeDampMode = (int)mode;
        return t;
    }

    /// <summary>Конфигурация EarthLike_Perlin для бюджетных замеров T110.</summary>
    private static HeightfieldTerrain PerlinBudgetPreset()
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        t.NoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.MaskNoiseStyle = (int)TerrainNoiseStyle.Perlin;
        t.RidgedMode = (int)TerrainRidgedMode.Multifractal;
        t.RidgedGamma = 0.3d;
        t.SlopeDamp = 0.6d;
        t.SlopeDampMode = (int)TerrainSlopeDampMode.Gradient;
        return t;
    }

    private static int Test110_TerrainNoiseBench()
    {
        double3[] dirs = NoiseBenchDirections(NoiseBenchSamples);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "BENCH terrain noise: {0} направлений, SceneLikeTerrain, 3 повтора, берём минимум",
            NoiseBenchSamples));
        Console.WriteLine("  x — к стоимости базовой строки, цена за 200k SampleHeight.");

        // Базовая строка — ровно то, что стояло в проекте до перехода на Перлин.
        HeightfieldTerrain full = NoiseBenchVariant(true, true, true, 10);
        double baseMs = NoiseBenchMs(full, dirs, 3);
        NoiseBenchReport("value+legacy (база)", full, dirs, baseMs);

        // Вклад каждой стадии в цену: сколько стоит warp/ridged/plains и сколько
        // останется на новую схему шума, если бюджет придётся ужимать.
        NoiseBenchReport("без warp", NoiseBenchVariant(false, true, true, 10), dirs, baseMs);
        NoiseBenchReport("без ridged", NoiseBenchVariant(true, false, true, 10), dirs, baseMs);
        NoiseBenchReport("без plains", NoiseBenchVariant(true, true, false, 10), dirs, baseMs);
        NoiseBenchReport("только базовый fBm", NoiseBenchVariant(false, false, false, 10), dirs, baseMs);

        // Новые схемы: каждая строка — один включённый слой, чтобы слагаемые
        // можно было сложить в прогноз полной конфигурации.
        Console.WriteLine("  новые схемы (по одной за раз):");
        NoiseBenchReport("+Перлин", NoiseBenchStyled(TerrainNoiseStyle.Perlin, TerrainRidgedMode.Legacy, 0d, 10), dirs, baseMs);
        NoiseBenchReport("+ridged multifractal", NoiseBenchStyled(TerrainNoiseStyle.Value, TerrainRidgedMode.Multifractal, 0d, 10), dirs, baseMs);
        NoiseBenchReport("всё вместе (домен выкл)", NoiseBenchStyled(TerrainNoiseStyle.Perlin, TerrainRidgedMode.Multifractal, 0.6d, 10), dirs, baseMs);

        // Цена домена — с РАЗДЕЛЬНО выбранным источником наклона. Раньше строки
        // «+ домен по склону» совпадали с «+ Перлин» по land и p99 и были даже
        // дешевле: источник наклона был привязан к примитиву, и в конфигурации
        // без Перлина домен молча вырождался. Теперь режим включается явно, и
        // видно, что он вообще меняет форму.
        Console.WriteLine("  домен по октавам (Perlin, legacy ridged, SlopeDamp 0.6):");
        NoiseBenchReport("SlopeDamp Off", NoiseBenchDamped(TerrainSlopeDampMode.Off, 0.6d, 10), dirs, baseMs);
        NoiseBenchReport("Accum", NoiseBenchDamped(TerrainSlopeDampMode.Accum, 0.6d, 10), dirs, baseMs);
        NoiseBenchReport("Gradient", NoiseBenchDamped(TerrainSlopeDampMode.Gradient, 0.6d, 10), dirs, baseMs);
        NoiseBenchReport("Gradient + multifractal", NoiseBenchDamped(TerrainSlopeDampMode.Gradient, 0.6d, 10), dirs, baseMs);

        // Порог решения шага 3: влезает ли полная схема в бюджет, и сколько
        // октав можно снять, чтобы вернуть базовую цену.
        Console.WriteLine("  полная схема, октавы — рычаг бюджета:");
        for (int oct = 10; oct >= 5; oct--)
        {
            NoiseBenchReport("perlin octaves=" + oct,
                NoiseBenchStyled(TerrainNoiseStyle.Perlin, TerrainRidgedMode.Multifractal, 0.6d, oct), dirs, baseMs);
        }

        // Бюджет: что можно снять ДО замера в Unity. Три гипотезы, все дешёвые.
        Console.WriteLine("  бюджетные варианты (Perlin, multifractal γ0.3, 10 октав):");
        HeightfieldTerrain perlinMulti = PerlinBudgetPreset();
        double perlinMultiMs = NoiseBenchMs(perlinMulti, dirs, 3);
        NoiseBenchReport("полный (эталон)", perlinMulti, dirs, baseMs);

        // (1) домен по накопленному значению вместо производной: снимает
        //     трилинейные смешивания на октаву, но домен работает по высоте.
        HeightfieldTerrain accumOnly = PerlinBudgetPreset();
        accumOnly.SlopeDampMode = (int)TerrainSlopeDampMode.Accum;
        NoiseBenchReport("домен Accum (без ∇)", accumOnly, dirs, perlinMultiMs);

        // (2) форма на Перлине, маски и warp на value: маски всё равно не
        //     чувствительны к примитиву по смыслу (порог квантильный, доля суши
        //     уже выровнена нормировкой), а стоят заметно дешевле.
        HeightfieldTerrain mixed = PerlinBudgetPreset();
        mixed.MaskNoiseStyle = (int)TerrainNoiseStyle.Value;
        NoiseBenchReport("маски на value", mixed, dirs, perlinMultiMs);

        // (3) оба сразу — самый дешёвый разумный вариант.
        HeightfieldTerrain lean = PerlinBudgetPreset();
        lean.MaskNoiseStyle = (int)TerrainNoiseStyle.Value;
        lean.SlopeDampMode = (int)TerrainSlopeDampMode.Accum;
        NoiseBenchReport("Accum + маски на value", lean, dirs, perlinMultiMs);

        double sanity = NoiseBenchMs(full, dirs, 1);
        Check(sanity > 0.0 && sanity < 60000.0, "BENCH terrain-noise",
            "замер состоялся: " + sanity.ToString("F2", CultureInfo.InvariantCulture) + " ms");
        return 0;
    }
}
