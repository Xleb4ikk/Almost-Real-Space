using System;
using System.Diagnostics;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    private const int NoiseBenchSamples = 200000;

    /// <summary>
    /// Uniform direction grid via the golden ratio — same scheme as in
    /// T87/T107 so land fractions here and there are comparable.
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
    /// Shape-statistics snapshot. This is exactly the set of numbers that moves
    /// when the noise changes, and which T87 (land fraction), T89 (spectrum) and
    /// T94 (peak height) key off — having it at hand is cheaper than catching
    /// failures across three tests at once.
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
    /// Triple-repeat measurement, taking the minimum: this is pure compute cost,
    /// not time under load (GC, interrupts, throttling) — repeats keep the
    /// difference between two noise schemes from being swallowed by spread.
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

        // Keep the compiler from dropping the loop: the sum is used in the NaN check.
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

    /// <summary>SceneLikeTerrain with one measurement axis disabled.</summary>
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

    /// <summary>Same configuration, but with one of the three new parameters.</summary>
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

    /// <summary>Same, but with an explicitly chosen slope source.</summary>
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

    /// <summary>EarthLike_Perlin configuration for the T110 budget measurements.</summary>
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

        // Baseline row: value + legacy ridged.
        HeightfieldTerrain full = NoiseBenchVariant(true, true, true, 10);
        double baseMs = NoiseBenchMs(full, dirs, 3);
        NoiseBenchReport("value+legacy (база)", full, dirs, baseMs);

        // Cost contribution of each stage: what warp/ridged/plains cost and how
        // much is left for the new noise scheme if the budget must be squeezed.
        NoiseBenchReport("без warp", NoiseBenchVariant(false, true, true, 10), dirs, baseMs);
        NoiseBenchReport("без ridged", NoiseBenchVariant(true, false, true, 10), dirs, baseMs);
        NoiseBenchReport("без plains", NoiseBenchVariant(true, true, false, 10), dirs, baseMs);
        NoiseBenchReport("только базовый fBm", NoiseBenchVariant(false, false, false, 10), dirs, baseMs);

        // New schemes: each row has one layer enabled so the terms can be added
        // up into a prediction for the full configuration.
        Console.WriteLine("  новые схемы (по одной за раз):");
        NoiseBenchReport("+Перлин", NoiseBenchStyled(TerrainNoiseStyle.Perlin, TerrainRidgedMode.Legacy, 0d, 10), dirs, baseMs);
        NoiseBenchReport("+ridged multifractal", NoiseBenchStyled(TerrainNoiseStyle.Value, TerrainRidgedMode.Multifractal, 0d, 10), dirs, baseMs);
        NoiseBenchReport("всё вместе (домен выкл)", NoiseBenchStyled(TerrainNoiseStyle.Perlin, TerrainRidgedMode.Multifractal, 0.6d, 10), dirs, baseMs);

        // Domain cost with an independently chosen slope source. The mode is now
        // enabled explicitly, which shows that it actually changes the shape.
        Console.WriteLine("  домен по октавам (Perlin, legacy ridged, SlopeDamp 0.6):");
        NoiseBenchReport("SlopeDamp Off", NoiseBenchDamped(TerrainSlopeDampMode.Off, 0.6d, 10), dirs, baseMs);
        NoiseBenchReport("Accum", NoiseBenchDamped(TerrainSlopeDampMode.Accum, 0.6d, 10), dirs, baseMs);
        NoiseBenchReport("Gradient", NoiseBenchDamped(TerrainSlopeDampMode.Gradient, 0.6d, 10), dirs, baseMs);
        NoiseBenchReport("Gradient + multifractal", NoiseBenchDamped(TerrainSlopeDampMode.Gradient, 0.6d, 10), dirs, baseMs);

        // Step-3 decision threshold: whether the full scheme fits the budget and
        // how many octaves can be dropped to restore the baseline cost.
        Console.WriteLine("  полная схема, октавы — рычаг бюджета:");
        for (int oct = 10; oct >= 5; oct--)
        {
            NoiseBenchReport("perlin octaves=" + oct,
                NoiseBenchStyled(TerrainNoiseStyle.Perlin, TerrainRidgedMode.Multifractal, 0.6d, oct), dirs, baseMs);
        }

        // Budget: what can be dropped BEFORE measuring in Unity. Three hypotheses, all cheap.
        Console.WriteLine("  бюджетные варианты (Perlin, multifractal γ0.3, 10 октав):");
        HeightfieldTerrain perlinMulti = PerlinBudgetPreset();
        double perlinMultiMs = NoiseBenchMs(perlinMulti, dirs, 3);
        NoiseBenchReport("полный (эталон)", perlinMulti, dirs, baseMs);

        // (1) domain by accumulated value instead of the derivative: removes
        //     the trilinear blends per octave, but the domain works on height.
        HeightfieldTerrain accumOnly = PerlinBudgetPreset();
        accumOnly.SlopeDampMode = (int)TerrainSlopeDampMode.Accum;
        NoiseBenchReport("домен Accum (без ∇)", accumOnly, dirs, perlinMultiMs);

        // (2) shape on Perlin, masks and warp on value: the masks are not
        //     meaningfully sensitive to the primitive anyway (the threshold is
        //     quantile-based, land fraction already normalized), and cost much less.
        HeightfieldTerrain mixed = PerlinBudgetPreset();
        mixed.MaskNoiseStyle = (int)TerrainNoiseStyle.Value;
        NoiseBenchReport("маски на value", mixed, dirs, perlinMultiMs);

        // (3) both at once — the cheapest reasonable option.
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
