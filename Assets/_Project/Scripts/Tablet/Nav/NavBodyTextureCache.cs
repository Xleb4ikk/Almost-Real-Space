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
        // Alpha carries the water mask (255 = below sea level). The map is never
        // uploaded as a texture, so the alpha channel is free and sampling it
        // costs nothing extra next to the colour.
    }

    /// <summary>
    /// Cached terrain colour maps for the globe. Nothing is built on the main
    /// thread: the first (map-opening) pass is a small 640×320 map on a worker,
    /// then a detailed 2048×1024 map swaps in when ready. The renderer draws a
    /// shaded placeholder sphere while the first map is in flight, so opening
    /// the tablet never freezes the frame and never shows a blocky planet.
    ///
    /// The texture is the same HeightfieldTerrain the physics uses, with the
    /// heightfield's own slope baked in as relief shading — otherwise a
    /// low-resolution colour map reads as flat colour bands.
    /// </summary>
    internal static class NavBodyTextureCache
    {
        private const int FirstWidth = 640;
        private const int FirstHeight = 320;
        // 2048×1024 is four times the old map. It is built on a worker thread, so
        // the only main-thread cost is the final texture raster (cached per view).
        private const int HighWidth = 2048;
        private const int HighHeight = 1024;
        // One detailed build at a time: two parallel fbm fills oversubscribe the
        // CPU and can stutter the simulation while the map is open.
        private const int MaxConcurrentBuilds = 1;

        private static readonly Dictionary<OrbitingBody, NavBodyTexture> Ready =
            new Dictionary<OrbitingBody, NavBodyTexture>();

        private static readonly Dictionary<OrbitingBody, BuildJob> Jobs =
            new Dictionary<OrbitingBody, BuildJob>();

        private static readonly Queue<BuildRequest> Pending = new Queue<BuildRequest>();
        private static readonly HashSet<OrbitingBody> Unsupported = new HashSet<OrbitingBody>();
        private static int running;

        private struct BuildRequest
        {
            public OrbitingBody Body;
            public int Width;
            public int Height;
        }

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

            if (Unsupported.Contains(body))
            {
                return null;
            }

            // A body without a heightfield (spherical placeholder): a flat colour.
            // Building this is trivial, so it stays synchronous.
            HeightfieldTerrain terrain = body.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                NavBodyTexture flat = BuildFlat();
                Ready[body] = flat;
                return flat;
            }

            // The first globe map is a worker job too; the renderer shows a shaded
            // placeholder sphere for a few frames instead of a blocky one.
            Request(body, FirstWidth, FirstHeight);
            return null;
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

                    // Keep refining after the map closes: a detailed globe should be
                    // ready by the next time the tablet is opened.
                    if (job.Texture.Width < HighWidth)
                    {
                        Request(body, HighWidth, HighHeight);
                    }
                }
                else
                {
                    // Do not respawn a failing job on every map tick. A flat globe is
                    // a better fallback than a permanent "still loading" sphere.
                    Unsupported.Add(body);
                    if (!Ready.ContainsKey(body))
                    {
                        Ready[body] = BuildFlat();
                        Version++;
                    }
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

            Request(body, HighWidth, HighHeight);
        }

        /// <summary>Queues a build once: a body has at most one job and one pending entry.</summary>
        private static void Request(OrbitingBody body, int width, int height)
        {
            if (body == null || Unsupported.Contains(body) || Jobs.ContainsKey(body))
            {
                return;
            }

            // The queue is tiny (one entry per body); a linear scan beats a set.
            foreach (BuildRequest queued in Pending)
            {
                if (ReferenceEquals(queued.Body, body))
                {
                    return;
                }
            }

            Pending.Enqueue(new BuildRequest { Body = body, Width = width, Height = height });
            StartPending();
        }

        private static void StartPending()
        {
            while (running < MaxConcurrentBuilds && Pending.Count > 0)
            {
                BuildRequest request = Pending.Dequeue();
                OrbitingBody body = request.Body;
                if (body == null || Jobs.ContainsKey(body))
                {
                    continue;
                }

                BuildInput input = BuildInput.Capture(body, request.Width, request.Height);
                if (input == null)
                {
                    Unsupported.Add(body);
                    if (!Ready.ContainsKey(body))
                    {
                        Ready[body] = BuildFlat();
                        Version++;
                    }

                    continue;
                }

                var job = new BuildJob(input);
                Jobs[body] = job;
                running++;
                job.Start();
            }
        }

        private static NavBodyTexture BuildFlat()
        {
            const int width = 32;
            const int height = 16;
            var flat = new NavBodyTexture
            {
                Width = width,
                Height = height,
                Pixels = new Color32[width * height],
            };
            Color32 color = NavMapRenderer.TerrainColor(
                NavMapRenderer.DefaultPalette, 0d, 0d, 1d, 0d, 0d, 0d, 1f);
            color.a = 0;
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
            var weights = new double[width * height];
            var pixels = new Color32[width * height];
            bool hasSea = input.SeaLevelMeters > -1e29d;

            // Leave the rest of the cores to the simulation: the 2048×1024 build
            // is a background nicety, not a reason to stutter the flight.
            int threads = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));
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
                    weights[row + x] = TerrainNoise.SamplePolarIceWeight(input.Params, dir);
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
                        input.AmplitudeMeters, lat, slopeEast, slopeNorth, 1f, weights[row + x]);

                    // Mild baked relief: the globe raster adds its own day/night shade,
                    // this only stops the texture from reading as flat colour bands.
                    // Softer than before, so the real sun shading dominates and the
                    // relief does not rotate with an obviously baked light.
                    float relief = 0.88f + (0.26f * light);
                    pixels[row + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * relief), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * relief), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * relief), 0, 255),
                        (byte)(hasSea && heights[row + x] < input.SeaLevelMeters ? 255 : 0));
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
