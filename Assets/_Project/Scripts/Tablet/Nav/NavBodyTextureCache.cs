using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Galilego.Universe;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>CPU-side equirectangular colour texture of a body's real terrain.</summary>
    internal sealed class NavBodyTexture
    {
        public Color32[] Pixels;
        public int Width;
        public int Height;
    }

    /// <summary>
    /// Cached terrain colour maps for the globe. A small map is built immediately
    /// (the map must open without a freeze); the detailed one is generated on a
    /// worker thread and swapped in when ready, so the main thread never spends
    /// frames sampling the heightfield for the globe.
    ///
    /// The texture is the same HeightfieldTerrain the physics uses, with the
    /// heightfield's own slope baked in as relief shading — otherwise a
    /// low-resolution colour map reads as flat colour bands.
    /// </summary>
    internal static class NavBodyTextureCache
    {
        private const int LowWidth = 192;
        private const int LowHeight = 96;
        private const int HighWidth = 1024;
        private const int HighHeight = 512;
        private const int MaxConcurrentBuilds = 2;

        private static readonly Dictionary<OrbitingBody, NavBodyTexture> Ready =
            new Dictionary<OrbitingBody, NavBodyTexture>();

        private static readonly Dictionary<OrbitingBody, BuildJob> Jobs =
            new Dictionary<OrbitingBody, BuildJob>();

        private static readonly Queue<OrbitingBody> Pending = new Queue<OrbitingBody>();
        private static readonly HashSet<OrbitingBody> Unsupported = new HashSet<OrbitingBody>();
        private static int running;

        /// <summary>Bumped when a detailed map replaces a low-resolution one.</summary>
        public static int Version { get; private set; }

        /// <summary>
        /// Static caches outlive a play session when domain reload is disabled in
        /// the editor; clear them so a new universe does not inherit old bodies.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Ready.Clear();
            Jobs.Clear();
            Pending.Clear();
            Unsupported.Clear();
            running = 0;
            Version = 0;
        }

        public static NavBodyTexture Get(OrbitingBody body)
        {
            if (body == null)
            {
                return null;
            }

            if (Ready.TryGetValue(body, out NavBodyTexture ready))
            {
                EnsureHighBuild(body);
                return ready;
            }

            NavBodyTexture low = BuildLow(body);
            Ready[body] = low;
            EnsureHighBuild(body);
            return low;
        }

        /// <summary>
        /// Polls the background jobs. Kept as a method (and with the old signature)
        /// so callers do not change; the budget is no longer spent on the main thread.
        /// </summary>
        public static void Advance(int sampleBudget)
        {
            StartPending();
            if (Jobs.Count == 0)
            {
                return;
            }

            List<OrbitingBody> finished = null;
            foreach (KeyValuePair<OrbitingBody, BuildJob> pair in Jobs)
            {
                if (!pair.Value.Done)
                {
                    continue;
                }

                if (finished == null)
                {
                    finished = new List<OrbitingBody>(Jobs.Count);
                }

                finished.Add(pair.Key);
            }

            if (finished == null)
            {
                return;
            }

            for (int i = 0; i < finished.Count; i++)
            {
                OrbitingBody body = finished[i];
                BuildJob job = Jobs[body];
                Jobs.Remove(body);
                running--;
                if (job.Texture != null)
                {
                    Ready[body] = job.Texture;
                    Version++;
                }
                else
                {
                    // Do not respawn a failing job on every map tick.
                    Unsupported.Add(body);
                }
            }

            StartPending();
        }

        private static void EnsureHighBuild(OrbitingBody body)
        {
            if (Ready.TryGetValue(body, out NavBodyTexture current)
                && current.Width >= HighWidth)
            {
                return;
            }

            if (Unsupported.Contains(body) || Jobs.ContainsKey(body))
            {
                return;
            }

            // The queue is tiny (one entry per body); a linear scan beats a set.
            foreach (OrbitingBody queued in Pending)
            {
                if (ReferenceEquals(queued, body))
                {
                    return;
                }
            }

            Pending.Enqueue(body);
            StartPending();
        }

        private static void StartPending()
        {
            while (running < MaxConcurrentBuilds && Pending.Count > 0)
            {
                OrbitingBody body = Pending.Dequeue();
                if (body == null || Jobs.ContainsKey(body))
                {
                    continue;
                }

                BuildInput input = BuildInput.Capture(body, HighWidth, HighHeight);
                if (input == null)
                {
                    Unsupported.Add(body);
                    continue;
                }

                var job = new BuildJob(input);
                Jobs[body] = job;
                running++;
                job.Start();
            }
        }

        private static NavBodyTexture BuildLow(OrbitingBody body)
        {
            BuildInput input = BuildInput.Capture(body, LowWidth, LowHeight);
            if (input != null)
            {
                try
                {
                    return Build(input);
                }
                catch (Exception)
                {
                    // Fall through to the flat placeholder: the map is a display
                    // and must not take the frame down.
                }
            }

            // A body without a heightfield (spherical placeholder): flat sea colour.
            var flat = new NavBodyTexture
            {
                Width = LowWidth,
                Height = LowHeight,
                Pixels = new Color32[LowWidth * LowHeight],
            };
            Color32 color = NavMapRenderer.TerrainColor(
                NavMapRenderer.DefaultPalette, 0d, 0d, 1d, 0d, 0d, 0d, 1f);
            for (int i = 0; i < flat.Pixels.Length; i++)
            {
                flat.Pixels[i] = color;
            }

            return flat;
        }

        /// <summary>
        /// All height sampling and colouring happens here, off the main thread. The
        /// input snapshot carries scalar noise parameters only: the live modifier /
        /// decor-exclusion NativeArrays are owned by the terrain and may be rebuilt
        /// on the main thread, so they must not be touched from a worker.
        /// </summary>
        private static NavBodyTexture Build(BuildInput input)
        {
            int width = input.Width;
            int height = input.Height;
            var heights = new double[width * height];
            var pixels = new Color32[width * height];

            int threads = Math.Max(1, Math.Min(6, Environment.ProcessorCount - 1));
            const double twoPi = Math.PI * 2d;

            Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = threads }, y =>
            {
                double lat = (-Math.PI * 0.5d) + (((y + 0.5d) / height) * Math.PI);
                double cosLat = Math.Cos(lat);
                double sinLat = Math.Sin(lat);
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    double lon = (-Math.PI) + (((x + 0.5d) / width) * twoPi);
                    var dir = new double3(cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), sinLat);
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
                }
            });

            double radius = Math.Max(1d, input.RadiusMeters);
            double eastStepEquator = (twoPi * radius) / width;
            double northStep = (Math.PI * radius) / height;
            const double sunEast = -0.45d;
            const double sunNorth = 0.55d;
            const double sunUp = 0.70d;

            Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = threads }, y =>
            {
                double lat = (-Math.PI * 0.5d) + (((y + 0.5d) / height) * Math.PI);
                double eastStep = Math.Max(1d, eastStepEquator * Math.Cos(lat));
                int row = y * width;
                int rowDown = Math.Max(0, y - 1) * width;
                int rowUp = Math.Min(height - 1, y + 1) * width;
                for (int x = 0; x < width; x++)
                {
                    int left = row + Math.Max(0, x - 1);
                    int right = row + Math.Min(width - 1, x + 1);
                    double slopeEast = (heights[right] - heights[left]) / (2d * eastStep);
                    double slopeNorth = (heights[rowUp + x] - heights[rowDown + x]) / (2d * northStep);

                    float light = ReliefLight(slopeEast, slopeNorth, sunEast, sunNorth, sunUp);
                    Color32 color = NavMapRenderer.TerrainColor(
                        input.Palette, heights[row + x], input.SeaLevelMeters,
                        input.AmplitudeMeters, lat, slopeEast, slopeNorth, 1f);

                    // Mild baked relief: the globe raster adds its own day/night shade,
                    // this only stops the texture from reading as flat colour bands.
                    float relief = 0.78f + (0.44f * light);
                    pixels[row + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * relief), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * relief), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * relief), 0, 255),
                        255);
                }
            });

            return new NavBodyTexture
            {
                Width = width,
                Height = height,
                Pixels = pixels,
            };
        }

        private static float ReliefLight(double slopeEast, double slopeNorth,
            double sunEast, double sunNorth, double sunUp)
        {
            double nx = -slopeEast * 4d;
            double ny = -slopeNorth * 4d;
            double invNormal = 1d / Math.Sqrt((nx * nx) + (ny * ny) + 1d);
            double lightInv = 1d / Math.Sqrt((sunEast * sunEast) + (sunNorth * sunNorth) + (sunUp * sunUp));
            double dot = ((nx * sunEast) + (ny * sunNorth) + sunUp) * invNormal * lightInv;
            return (float)Math.Max(0d, Math.Min(1d, dot));
        }

        /// <summary>Scalar snapshot of everything a worker needs to evaluate the terrain.</summary>
        private sealed class BuildInput
        {
            public TerrainNoiseParams Params;
            public TerrainPaletteData Palette;
            public double SeaLevelMeters;
            public double AmplitudeMeters;
            public double RadiusMeters;
            public int Width;
            public int Height;

            public static BuildInput Capture(OrbitingBody body, int width, int height)
            {
                HeightfieldTerrain terrain = body != null ? body.Terrain as HeightfieldTerrain : null;
                if (terrain == null)
                {
                    return null;
                }

                TerrainNoiseParams parameters = TerrainNoiseParams.FromTerrain(terrain);

                // Owned by the terrain and rebuilt on the main thread — never read
                // from a worker. A globe at this scale does not show pads/zones.
                parameters.Mods = default;
                parameters.DecorExclusions = default;

                double amplitude = terrain.AmplitudeMeters > 1d ? terrain.AmplitudeMeters : 1d;
                return new BuildInput
                {
                    Params = parameters,
                    Palette = terrain.Palette != null ? terrain.Palette.Clone() : new TerrainPaletteData(),
                    SeaLevelMeters = terrain.GetSeaLevelMeters(),
                    AmplitudeMeters = amplitude,
                    RadiusMeters = body.Radius,
                    Width = width,
                    Height = height,
                };
            }
        }

        private sealed class BuildJob
        {
            private readonly BuildInput input;
            private volatile bool done;
            public NavBodyTexture Texture;

            public BuildJob(BuildInput input)
            {
                this.input = input;
            }

            public bool Done { get { return done; } }

            public void Start()
            {
                Task.Run(() =>
                {
                    try
                    {
                        Texture = Build(input);
                    }
                    catch (Exception)
                    {
                        Texture = null;
                    }
                    finally
                    {
                        done = true;
                    }
                });
            }
        }
    }
}
