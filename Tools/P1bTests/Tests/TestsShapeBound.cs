using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Upper bound of the terrain shape. The test measures the max shape over a
    /// large sample of directions for both schemes and checks it against
    /// TerrainNoise.ShapeBound × AmplitudeMeters — exactly the quantity on which
    /// everything clipping by height relies.
    /// </summary>
    private static int Test128_ShapeBound()
    {
        const int n = 10000000;
        HeightfieldTerrain[] cases =
        {
            Case(TerrainNoiseStyle.Value, TerrainRidgedMode.Legacy, 1.0d, -0.1d, "value + legacy"),
            Case(TerrainNoiseStyle.Perlin, TerrainRidgedMode.Legacy, 1.0d, -0.1d, "perlin + legacy"),
            Case(TerrainNoiseStyle.Perlin, TerrainRidgedMode.Multifractal, 1.0d, -0.4d, "perlin + multifr (ассет)"),
        };

        bool allOk = true;
        for (int c = 0; c < cases.Length; c++)
        {
            HeightfieldTerrain t = cases[c];
            TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(t);
            double3[] dirs = SphereDirs(n);
            double maxForm = double.MinValue;
            double minForm = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                double h = TerrainNoise.SampleHeight(p, dirs[i]);
                if (h > maxForm)
                {
                    maxForm = h;
                }

                if (h < minForm)
                {
                    minForm = h;
                }
            }

            // SHAPE bound, not octave. ShapeBound limits a single octave
            // (√3/2 · normalization), while the full shape also dips by
            // ContinentDepth. The full-shape contract is therefore
            // ShapeBound + ContinentDepth, and everything clipping by height
            // must reference it.
            double bound = (TerrainNoise.ShapeBound + System.Math.Max(0d, t.ContinentDepth)) * t.AmplitudeMeters;
            double observed = System.Math.Max(System.Math.Abs(maxForm), System.Math.Abs(minForm)) * t.AmplitudeMeters;
            bool ok = observed <= bound;
            allOk &= ok;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                                "    {0,-24} |форма| max={1:F4}  высота |h| max={2:F0} м  "
                + "граница (ShapeBound+ContinentDepth)·amp={3:F0} м  запас {4:F2}×  {5}",
                Label(c), System.Math.Max(System.Math.Abs(maxForm), System.Math.Abs(minForm)), observed, bound, bound / observed, ok ? "ok" : "ПРЕВЫШЕН"));
        }

        Check(allOk, "T128 shape-bound",
            "максимум формы ≤ TerrainNoise.ShapeBound для всех схем");
        return 0;
    }

    private static string Label(int c)
    {
        return c == 0 ? "value + legacy" : c == 1 ? "perlin + legacy" : "perlin + multifr (ассет)";
    }

    private static HeightfieldTerrain Case(
        TerrainNoiseStyle style, TerrainRidgedMode ridged, double gamma, double threshold, string name)
    {
        HeightfieldTerrain t = SceneLikeTerrain();
        t.NoiseStyle = (int)style;
        t.MaskNoiseStyle = (int)style;
        t.RidgedMode = (int)ridged;
        t.RidgedGamma = gamma;
        t.ContinentThreshold = threshold;
        t.SlopeDamp = 0d;
        t.SlopeDampMode = (int)TerrainSlopeDampMode.Off;
        return t;
    }
}
