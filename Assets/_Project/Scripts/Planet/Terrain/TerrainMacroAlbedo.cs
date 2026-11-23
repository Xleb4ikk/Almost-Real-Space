using System;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Equirectangular ALBEDO map of a heightfield body, built once on a worker.
    ///
    /// The far disc (BodyFarDiscView) is a flat quad: a body seen from another
    /// body's surface is beyond the camera far plane, so its real chunks are
    /// clipped and only this map is left. A flat grey ball at 83 000 km is not
    /// Terra — the disc must carry the SAME palette the terrain uses (ocean,
    /// land, beaches, polar caps), otherwise "the planet looks broken" is just
    /// a missing map.
    ///
    /// This is the nav globe's colour path (NavMapRenderer.TerrainColor) with
    /// light = 1: no baked sun. The disc shader applies the real Lambert term
    /// and the sun direction itself, so a baked light here would rotate with
    /// the world and double the shading.
    ///
    /// Cost: 512×256 = 131 k height samples, a few milliseconds on the worker
    /// pool, paid only when the disc is first needed (never on the ground where
    /// the real chunks are drawn). The texture is the fallback for a body that
    /// is still visible but cannot be rasterised, so 512 is ample: Terra seen
    /// from Artemis is ~15 px across.
    /// </summary>
    internal static class TerrainMacroAlbedo
    {
        internal sealed class Input
        {
            public TerrainNoiseParams Params;
            public TerrainPaletteData Palette;
            public double SeaLevelMeters;
            public double AmplitudeMeters;
            public double RadiusMeters;
            public int Width;
            public int Height;
        }

        /// <summary>
        /// Scalar snapshot for the worker. The live modifier / decor-exclusion
        /// NativeArrays belong to the terrain and are rebuilt on the main thread:
        /// a distant disc has no pads or zones in it, so they are dropped here.
        /// </summary>
        internal static Input Capture(HeightfieldTerrain terrain, double radiusMeters, int width, int height)
        {
            if (terrain == null || width < 2 || height < 2)
            {
                return null;
            }

            TerrainNoiseParams parameters = TerrainNoiseParams.FromTerrain(terrain);
            parameters.Mods = default;
            parameters.DecorExclusions = default;

            return new Input
            {
                Params = parameters,
                Palette = terrain.Palette != null ? terrain.Palette.Clone() : new TerrainPaletteData(),
                SeaLevelMeters = terrain.GetSeaLevelMeters(),
                AmplitudeMeters = terrain.AmplitudeMeters > 1d ? terrain.AmplitudeMeters : 1d,
                RadiusMeters = System.Math.Max(1d, radiusMeters),
                Width = width,
                Height = height,
            };
        }

        internal static Color32[] Build(Input input)
        {
            if (input == null)
            {
                return null;
            }

            int width = input.Width;
            int height = input.Height;
            var heights = new double[width * height];
            var weights = new double[width * height];
            var pixels = new Color32[width * height];

            // Leave a core for the simulation: this is a one-off background bake.
            int threads = System.Math.Max(1, System.Math.Min(4, Environment.ProcessorCount - 1));
            const double twoPi = System.Math.PI * 2d;

            Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = threads }, y =>
            {
                double lat = (-System.Math.PI * 0.5d) + (((y + 0.5d) / height) * System.Math.PI);
                double cosLat = System.Math.Cos(lat);
                double sinLat = System.Math.Sin(lat);
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    double lon = (-System.Math.PI) + (((x + 0.5d) / width) * twoPi);
                    var dir = new double3(cosLat * System.Math.Cos(lon), cosLat * System.Math.Sin(lon), sinLat);
                    double meters;
                    try
                    {
                        meters = TerrainNoise.SampleHeight(input.Params, dir) * input.AmplitudeMeters;
                    }
                    catch (Exception)
                    {
                        meters = 0d;
                    }

                    heights[row + x] = meters;
                    weights[row + x] = TerrainNoise.SamplePolarIceWeight(input.Params, dir);
                }
            });

            double radius = input.RadiusMeters;
            double eastStepEquator = (twoPi * radius) / width;
            double northStep = (System.Math.PI * radius) / height;

            Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = threads }, y =>
            {
                double lat = (-System.Math.PI * 0.5d) + (((y + 0.5d) / height) * System.Math.PI);
                double eastStep = System.Math.Max(1d, eastStepEquator * System.Math.Cos(lat));
                int row = y * width;
                int rowDown = System.Math.Max(0, y - 1) * width;
                int rowUp = System.Math.Min(height - 1, y + 1) * width;
                for (int x = 0; x < width; x++)
                {
                    int left = row + System.Math.Max(0, x - 1);
                    int right = row + System.Math.Min(width - 1, x + 1);
                    double slopeEast = (heights[right] - heights[left]) / (2d * eastStep);
                    double slopeNorth = (heights[rowUp + x] - heights[rowDown + x]) / (2d * northStep);

                    // light = 1: pure albedo, the disc shader owns the sun.
                    pixels[row + x] = TerrainPalette.SurfaceColor(
                        input.Palette, heights[row + x], input.SeaLevelMeters,
                        input.AmplitudeMeters, lat, slopeEast, slopeNorth, 1f, weights[row + x]);
                }
            });

            return pixels;
        }
    }
}
