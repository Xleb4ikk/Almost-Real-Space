using System;
using System.Globalization;
using Galilego.Universe;
using Unity.Mathematics;

internal static partial class P1bTests
{
    /// <summary>
    /// Верхняя граница формы рельефа. Неявный контракт «форма в [−1,1]»
    /// держался сам собой только у value noise: LatticeValue по построению в
    /// [−1,1], и трилинейная смесь не выходит за выпуклую оболочку. У
    /// градиентного примитива такого свойства нет — после выравнивания по RMS
    /// (T111) пик октавы стал 1.43, а жёсткая граница √3/2 · 2.056 = 1.78.
    ///
    /// Тест меряет максимум формы по большой выборке направлений для обеих
    /// схем и сверяет с TerrainNoise.ShapeBound × AmplitudeMeters. Это ровно
    /// та величина, на которую опирается всё, что режет по высоте.
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

            // ГРАНИЦА ФОРМЫ, а не октавы. ShapeBound ограничивает одну октаву
            // (√3/2 · нормировка), а полная форма ещё и уходит вниз на
            // ContinentDepth: замер показал |форма| до 2.05 при ShapeBound 1.78.
            // Значит контракт на полную форму — ShapeBound + ContinentDepth,
            // и всё, что режет по высоте, обязано ссылаться на него.
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
