using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Galilego.Core;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Bakes the body-fixed equirect maps an airless body needs to look real ABOVE the
    /// mesh: albedo (maria, fresh ejecta, rays), the SURFACE normal (crater rims, basin
    /// walls, rilles) and the terrain HORIZON (what shadows a crater floor at a 1° sun).
    ///
    /// Why a bake at all: at 300 km the terrain still has ~1 km quads, so every piece of
    /// albedo detail smaller than a quad is a vertex-interpolated smear, and the shader
    /// kills it outright (footprint fade). The macro map is sampled per fragment from
    /// the body direction, so the same field stays sharp from orbit down to the altitude
    /// where its texel becomes bigger than a pixel — the shader switches on the pixel
    /// angle vs the texel angle, not on a hand-tuned distance.
    ///
    /// The same maps feed <see cref="BodyFarDiscView"/> (albedo/normal) and the terrain
    /// shader (all four): one bake, one look, and the billboard-to-terrain hand-over
    /// stops being a change of material.
    ///
    /// Source of truth is LunarRelief (the same function the mesh and the physics use),
    /// so the map cannot drift from the ground.
    ///
    /// The bake is also run automatically: <see cref="LunarMacroMapAutoBaker"/> watches
    /// the profile + TerrainSeed fingerprint (<see cref="TerrainProfile.MacroBakeHash"/>)
    /// and rebakes after the edit settles. The menu below stays as the explicit path.
    ///
    /// Menu: Tools/Galilego/Airless Bodies/Bake lunar macro maps.
    /// </summary>
    public static class LunarMacroMapBaker
    {
        internal const string Folder = "Assets/_Project/Profiles/Terrain/Lunar";
        internal const int Width = 2048;
        internal const int Height = 1024;

        /// <summary>Bump when the bake's OUTPUT changes (not when only its speed does).
        /// It is part of the fingerprint, so every body rebakes once after an algorithm
        /// change instead of keeping maps that no longer match the code.</summary>
        internal const int BakeVersion = 4;

        private const float AlbedoRangeMin = 0.15f;
        private const float AlbedoRangeMax = 1.8f;

        /// <summary>True while a manual bake runs: the auto-baker must not start a
        /// second one on top of it.</summary>
        public static bool IsBaking { get; private set; }

        // ------------------------------------------------------------------ inputs / result

        /// <summary>Thread-safe snapshot of everything the bake reads. The live
        /// TerrainProfile is edited by the inspector while a background bake may be
        /// running, so it must never be read from the worker.</summary>
        internal sealed class BakeInputs
        {
            public LunarReliefSettings Lunar;
            public TerrainPaletteData Palette;
            public int Seed;
            public long Hash;
        }

        /// <summary>Pixels only: no Unity objects, safe to produce on a worker.</summary>
        internal sealed class BakeResult
        {
            public long Hash;
            public Color32[] Albedo;
            public Color32[] Normal;
            public Color32[] HorizonA;
            public Color32[] HorizonB;
        }

        /// <summary>
        /// Fingerprint of the authoring state a bake depends on: all LunarParams (which
        /// already include the body seed and radius), the three palette colours the macro
        /// albedo is derived from, and the baker version. 0 is reserved for "never baked".
        /// </summary>
        internal static long ComputeHash(TerrainProfile profile, int seed)
        {
            if (profile == null || profile.Lunar == null)
            {
                return 0;
            }

            LunarParams p = profile.Lunar.ToParams(seed);
            long h = p.ContentHash;
            TerrainPaletteData palette = profile.Palette;
            if (palette != null)
            {
                MixColor(ref h, palette.Snow);
                MixColor(ref h, palette.Soil);
                MixColor(ref h, palette.Lush);
            }

            h = Mix(h, BakeVersion);
            return h == 0 ? 1 : h;
        }

        /// <summary>Snapshot the profile for a bake. The hash is captured from the SAME
        /// snapshot the pixels come from, so a mid-bake edit can be detected later.</summary>
        internal static BakeInputs Capture(TerrainProfile profile, int seed)
        {
            return new BakeInputs
            {
                Lunar = profile.Lunar != null ? profile.Lunar.Clone() : new LunarReliefSettings(),
                Palette = profile.Palette != null ? profile.Palette.Clone() : new TerrainPaletteData(),
                Seed = seed,
                Hash = ComputeHash(profile, seed),
            };
        }

        // ------------------------------------------------------------------ menus

        [MenuItem("Tools/Galilego/Airless Bodies/Bake lunar macro maps", priority = 21)]
        private static void BakeAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Lunar macro maps",
                    "Остановите Play: бейк пишет ассеты, а в Play они не сохранятся.", "OK");
                return;
            }

            BodyAuthoring[] bodies = UnityEngine.Object.FindObjectsByType<BodyAuthoring>(
                FindObjectsInactive.Include);
            int baked = 0;
            try
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    BodyAuthoring body = bodies[i];
                    TerrainProfileAsset asset = body.TerrainPreset;
                    if (asset == null || asset.Profile == null || asset.Profile.Lunar == null
                        || !asset.Profile.Lunar.Enabled)
                    {
                        continue;
                    }

                    if (!EditorUtility.DisplayCancelableProgressBar("Lunar macro maps",
                            body.name + ": " + Width + "x" + Height, i / (float)bodies.Length))
                    {
                        BakeOne(body, asset);
                        baked++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[LunarMacroMapBaker] готово, тел: " + baked);
        }

        /// <summary>Rebake one body synchronously. Used by the menu and the Lunar Body
        /// Creator; the automatic path calls <see cref="Compute"/> on a worker instead.</summary>
        public static void BakeOne(BodyAuthoring body, TerrainProfileAsset asset)
        {
            if (body == null || asset == null || asset.Profile == null)
            {
                return;
            }

            // A manual bake must not race the background one for the same PNGs.
            LunarMacroMapAutoBaker.CancelForManualBake();

            IsBaking = true;
            try
            {
                EditorUtility.DisplayProgressBar("Lunar macro maps",
                    body.name + ": " + Width + "x" + Height, 0.1f);
                BakeInputs inputs = Capture(asset.Profile, body.TerrainSeed);
                BakeResult result = Compute(inputs, null, CancellationToken.None);
                WriteAndAssign(body, asset, result);
            }
            finally
            {
                IsBaking = false;
                EditorUtility.ClearProgressBar();
            }
        }

        // ------------------------------------------------------------------ compute (worker-safe)

        /// <summary>
        /// All pixel work. PURE: no Unity objects, no editor API — only arrays, Lua
        /// params and math. <paramref name="progress"/> is called from worker threads and
        /// must only store a number (the editor UI is pumped by the caller).
        /// </summary>
        internal static BakeResult Compute(BakeInputs input, Action<float> progress,
            CancellationToken token)
        {
            LunarParams lunar = input.Lunar.ToParams(input.Seed);
            TerrainPaletteData palette = input.Palette;

            var heights = new double[Width * Height];
            var albedo = new float[Width * Height];

            // Parallel: Sample() is pure and the params are a struct copy. ~57k samples/s
            // single-threaded, 2.1M texels total, so seconds instead of a minute.
            int rowsDone = 0;
            Parallel.For(0, Height, new ParallelOptions { CancellationToken = token }, y =>
            {
                double lat = (((y + 0.5d) / Height) - 0.5d) * Math.PI;
                double cl = Math.Cos(lat);
                double sl = Math.Sin(lat);
                for (int x = 0; x < Width; x++)
                {
                    double lon = ((((x + 0.5d) / Width) * 2d) - 1d) * Math.PI;
                    var dir = new double3(cl * Math.Cos(lon), cl * Math.Sin(lon), sl);
                    double a;
                    heights[(y * Width) + x] = LunarRelief.Sample(lunar, dir, true, out a);
                    albedo[(y * Width) + x] = AlbedoFactor(a, palette);
                }

                Report(progress, Interlocked.Increment(ref rowsDone), Height, 0f, 0.55f);
            });

            var albedoPixels = new Color32[Width * Height];
            var normalPixels = new Color32[Width * Height];
            double radius = Math.Max(1000d, input.Lunar.RadiusMeters);
            double dLon = (2d * Math.PI) / Width;
            double dLat = Math.PI / Height;

            rowsDone = 0;
            Parallel.For(0, Height, new ParallelOptions { CancellationToken = token }, y =>
            {
                double lat = (((y + 0.5d) / Height) - 0.5d) * Math.PI;
                double cl = Math.Cos(lat);
                double sl = Math.Sin(lat);
                int yu = Math.Max(0, y - 1);
                int yd = Math.Min(Height - 1, y + 1);
                for (int x = 0; x < Width; x++)
                {
                    int i = (y * Width) + x;
                    int xl = (x + Width - 1) % Width;
                    int xr = (x + 1) % Width;
                    double lon = ((((x + 0.5d) / Width) * 2d) - 1d) * Math.PI;

                    // Albedo: the same lerp the palette/shader does (dark maria -> Soil,
                    // bright rays -> Lush), stored as a multiplier of the base albedo / 2
                    // — the encoding BodyFarDisc.shader already reads.
                    float factor = albedo[i];
                    byte r = (byte)Mathf.Clamp(Mathf.RoundToInt((factor * 0.5f) * 255f), 0, 255);
                    albedoPixels[i] = new Color32(r, r, r, 255);

                    // Surface normal from the height gradient on the sphere:
                    //   dr/dlon = (R+h) cos(lat) east + h_lon dir
                    //   dr/dlat = (R+h) north + h_lat dir
                    //   n = normalize(cross(dr/dlon, dr/dlat))   (east x north = dir)
                    double hLon = (heights[(y * Width) + xr] - heights[(y * Width) + xl]) / (2d * dLon);
                    double hLat = (heights[(yd * Width) + x] - heights[(yu * Width) + x]) / (2d * dLat);
                    double hCenter = heights[i];
                    double rr = radius + hCenter;
                    double cosLon = Math.Cos(lon);
                    double sinLon = Math.Sin(lon);

                    var dir = new double3(cl * cosLon, cl * sinLon, sl);
                    var east = new double3(-sinLon, cosLon, 0d);
                    var north = new double3(-sl * cosLon, -sl * sinLon, cl);
                    double3 dLonVec = (east * (rr * cl)) + (dir * hLon);
                    double3 dLatVec = (north * rr) + (dir * hLat);
                    double3 n = math.normalize(math.cross(dLonVec, dLatVec));
                    if (double.IsNaN(n.x) || double.IsNaN(n.y) || double.IsNaN(n.z))
                    {
                        n = dir;
                    }

                    normalPixels[i] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(((float)n.x * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(((float)n.y * 0.5f + 0.5f) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(((float)n.z * 0.5f + 0.5f) * 255f), 0, 255),
                        255);
                }

                Report(progress, Interlocked.Increment(ref rowsDone), Height, 0.55f, 0.85f);
            });

            var horizonPixelsA = new Color32[Width * Height];
            var horizonPixelsB = new Color32[Width * Height];
            BuildHorizon(heights, Width, Height, radius, horizonPixelsA, horizonPixelsB,
                progress, 0.85f, 1f, token);

            return new BakeResult
            {
                Hash = input.Hash,
                Albedo = albedoPixels,
                Normal = normalPixels,
                HorizonA = horizonPixelsA,
                HorizonB = horizonPixelsB,
            };
        }

        /// <summary>Writes the PNGs, reimports them and points the profile (and the
        /// body's far disc) at the fresh textures. MAIN THREAD ONLY.</summary>
        internal static void WriteAndAssign(BodyAuthoring body, TerrainProfileAsset asset,
            BakeResult result)
        {
            EnsureFolder(Folder);
            // Name the files after the PROFILE, not the body: two bodies can share one
            // profile, and ASCII asset paths survive CI and non-Windows tooling.
            string safe = Sanitize(string.IsNullOrEmpty(asset.name) ? body.name : asset.name);
            string albedoPath = Folder + "/" + safe + "_macro_albedo.png";
            string normalPath = Folder + "/" + safe + "_macro_normal.png";
            string horizonAPath = Folder + "/" + safe + "_macro_horizon0.png";
            string horizonBPath = Folder + "/" + safe + "_macro_horizon1.png";
            WritePng(albedoPath, result.Albedo);
            WritePng(normalPath, result.Normal);
            // The horizon maps pack EIGHT azimuth bins into two RGBA PNGs: the fourth
            // bin of each lives in the ALPHA channel. Their importer must keep it —
            // with `alphaSource = None` the GPU reads alpha as 1.0, which decodes to
            // a +90° horizon for bins 3 and 7 and shadows half the daylight disc.
            WritePng(horizonAPath, result.HorizonA, keepAlpha: true);
            WritePng(horizonBPath, result.HorizonB, keepAlpha: true);

            Texture2D albedoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            Texture2D normalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            Texture2D horizonTextureA = AssetDatabase.LoadAssetAtPath<Texture2D>(horizonAPath);
            Texture2D horizonTextureB = AssetDatabase.LoadAssetAtPath<Texture2D>(horizonBPath);

            TerrainProfile profile = asset.Profile;
            profile.MacroAlbedo = albedoTexture;
            profile.MacroNormal = normalTexture;
            profile.MacroHorizonA = horizonTextureA;
            profile.MacroHorizonB = horizonTextureB;
            profile.MacroBakeHash = result.Hash;
            EditorUtility.SetDirty(asset);

            // The far disc of this body uses the very same bake: the disc and the terrain
            // then differ only in geometry, not in what the surface looks like.
            BodyFarDiscView disc = body.GetComponent<BodyFarDiscView>();
            if (disc != null)
            {
                bool changed = disc.SurfaceMap != albedoTexture || disc.NormalMap != normalTexture;
                disc.SurfaceMap = albedoTexture;
                disc.NormalMap = normalTexture;
                if (changed)
                {
                    EditorUtility.SetDirty(disc);
                    EditorSceneManager.MarkSceneDirty(body.gameObject.scene);
                }
            }

            Debug.Log("[LunarMacroMapBaker] " + body.name + ": " + albedoPath + " (" + Width + "x" + Height + ")");
        }

        // ------------------------------------------------------------------ progress / hash helpers

        private static void Report(Action<float> progress, int done, int total, float from, float to)
        {
            // Called from Parallel.For workers: the editor UI is pumped by the caller,
            // this only hands over a number. 64 rows ≈ 6% of the map is smooth enough.
            if (progress == null || (done & 63) != 0)
            {
                return;
            }

            progress(from + ((to - from) * (done / (float)total)));
        }

        private static long Mix(long h, long value)
        {
            unchecked
            {
                return (h ^ value) * 1099511628211L;
            }
        }

        private static void MixColor(ref long h, Color c)
        {
            unchecked
            {
                h = Mix(h, BitConverter.SingleToInt32Bits(c.r));
                h = Mix(h, BitConverter.SingleToInt32Bits(c.g));
                h = Mix(h, BitConverter.SingleToInt32Bits(c.b));
                h = Mix(h, BitConverter.SingleToInt32Bits(c.a));
            }
        }

        // ------------------------------------------------------------------ horizon

        /// <summary>Azimuth bins of the baked horizon: 45° apart, 0 = north, 90° = east.</summary>
        private const int HorizonAzimuths = 8;

        /// <summary>March length in height-map texels. 56 texels of a 2048-wide map is
        /// ~70 km on Artemis — past the horizon of every peak on a 412 km body.</summary>
        private const int HorizonSteps = 56;

        /// <summary>
        /// Bakes the terrain horizon: for every texel and every 45° azimuth, the highest
        /// elevation angle of the terrain along that azimuth, out to <see cref="HorizonSteps"/>
        /// texels. The runtime compares it with the sun's local elevation, so a crater rim
        /// shadows its floor even when the sun is 1° above the horizon — HDRP's cascades
        /// (200 m…2 km) cannot reach that far, and without this map every inner wall facing
        /// the sun glows out of a black landscape at the terminator.
        ///
        /// March is a rhumb line in the equirect grid (straight in lat/lon), which differs
        /// from the great circle by a few percent over the 10–15° of arc that matter here:
        /// far below the 1.26 km texel. The curvature drop d²/(2R) is included exactly, so
        /// a target beyond the geometric horizon is never above it.
        ///
        /// Encoding: 8 bits per angle, NONLINEAR — u = sign(a)·sqrt(|a|/90°), so a step near
        /// the horizon is 90/255² ≈ 0.0014° while the coarsest step (near ±90°) is 0.7°.
        /// The terminator lives at 0°, so that is where the precision has to be.
        /// </summary>
        private static void BuildHorizon(double[] heights, int width, int height, double radius,
            Color32[] mapA, Color32[] mapB, Action<float> progress, float progressFrom,
            float progressTo, CancellationToken token)
        {
            double twoPi = Math.PI * 2d;
            double texelMeters = (twoPi * radius) / width;
            double stepMeters = texelMeters;
            double curvature = 1d / (2d * radius);
            var sinAz = new double[HorizonAzimuths];
            var cosAz = new double[HorizonAzimuths];
            for (int k = 0; k < HorizonAzimuths; k++)
            {
                double a = (k * twoPi) / HorizonAzimuths;
                sinAz[k] = Math.Sin(a);
                cosAz[k] = Math.Cos(a);
            }

            int rowsDone = 0;
            Parallel.For(0, height, new ParallelOptions { CancellationToken = token }, y =>
            {
                double lat = (((y + 0.5d) / height) - 0.5d) * Math.PI;
                double cl = Math.Cos(lat);
                if (cl < 1e-3d)
                {
                    // The equirect longitude degenerates at the poles; the horizon there
                    // is the local terrain anyway, so a 0.06° longitudes-per-metre cap
                    // only changes the direction spread, not the result.
                    cl = 1e-3d;
                }

                int row = y * width;
                var angles = new byte[HorizonAzimuths];
                for (int x = 0; x < width; x++)
                {
                    int index = row + x;
                    double h0 = heights[index];
                    double startLon = ((((x + 0.5d) / width) * 2d) - 1d) * Math.PI;
                    double invLonScale = 1d / (radius * cl);
                    double invLatScale = 1d / radius;

                    for (int k = 0; k < HorizonAzimuths; k++)
                    {
                        double eastMeters = sinAz[k] * stepMeters;
                        double northMeters = cosAz[k] * stepMeters;
                        double dLon = eastMeters * invLonScale;
                        double dLat = northMeters * invLatScale;
                        double bestTan = double.NegativeInfinity;
                        for (int s = 1; s <= HorizonSteps; s++)
                        {
                            double dist = s * stepMeters;
                            double lon2 = startLon + (dLon * s);
                            double lat2 = lat + (dLat * s);

                            // Bilinear height sample on the equirect grid (wrap in lon,
                            // clamp at the poles).
                            double fx = ((lon2 / twoPi) + 0.5d) * width - 0.5d;
                            double fy = ((lat2 / Math.PI) + 0.5d) * height - 0.5d;
                            if (fy < 0d)
                            {
                                fy = 0d;
                            }
                            else if (fy > height - 1d)
                            {
                                fy = height - 1d;
                            }

                            int x0 = (int)Math.Floor(fx);
                            double tx = fx - x0;
                            x0 %= width;
                            if (x0 < 0)
                            {
                                x0 += width;
                            }

                            int x1 = x0 + 1;
                            if (x1 >= width)
                            {
                                x1 = 0;
                            }

                            int y0 = (int)fy;
                            int y1 = y0 + 1 < height ? y0 + 1 : height - 1;
                            double ty = fy - y0;
                            int r0 = y0 * width;
                            int r1 = y1 * width;
                            double h00 = heights[r0 + x0];
                            double h10 = heights[r0 + x1];
                            double h01 = heights[r1 + x0];
                            double h11 = heights[r1 + x1];
                            double h2 = (((h00 * (1d - tx)) + (h10 * tx)) * (1d - ty))
                                + (((h01 * (1d - tx)) + (h11 * tx)) * ty);

                            double tangent = ((h2 - h0) - (dist * dist * curvature)) / dist;
                            if (tangent > bestTan)
                            {
                                bestTan = tangent;
                            }
                        }

                        angles[k] = EncodeHorizon(Math.Atan(bestTan));
                    }

                    mapA[index] = new Color32(angles[0], angles[1], angles[2], angles[3]);
                    mapB[index] = new Color32(angles[4], angles[5], angles[6], angles[7]);
                }

                Report(progress, Interlocked.Increment(ref rowsDone), height, progressFrom,
                    progressTo);
            });
        }

        /// <summary>Nonlinear 8-bit encoding of a horizon elevation angle in radians.</summary>
        private static byte EncodeHorizon(double angleRadians)
        {
            double degrees = angleRadians * (180d / Math.PI);
            if (degrees < -90d)
            {
                degrees = -90d;
            }
            else if (degrees > 90d)
            {
                degrees = 90d;
            }

            double u = Math.Sign(degrees) * Math.Sqrt(Math.Abs(degrees) / 90d);
            int value = (int)Math.Round(((u * 0.5d) + 0.5d) * 255d);
            return (byte)(value < 0 ? 0 : (value > 255 ? 255 : value));
        }

        /// <summary>LunarRelief albedo (-1 bright .. +1 mare) → multiplier of the palette albedo.</summary>
        private static float AlbedoFactor(double a, TerrainPaletteData palette)
        {
            Color snow = palette.Snow;
            Color target = a >= 0d
                ? Color.Lerp(snow, palette.Soil, (float)a)
                : Color.Lerp(snow, palette.Lush, (float)(-a * 0.6d));
            float baseValue = Mathf.Max(0.05f, snow.grayscale);
            float factor = target.grayscale / baseValue;
            return Mathf.Clamp(factor, AlbedoRangeMin, AlbedoRangeMax);
        }

        private static void WritePng(string path, Color32[] pixels, bool keepAlpha = false)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = keepAlpha
                ? TextureImporterAlphaSource.FromInput
                : TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            // BC7, not the default BC1: the default compressor turned the body-space
            // normal into a black/cyan smear when sampled (BC1's 4-colour block palette
            // cannot hold the sphere's smooth 3-channel gradient), while BC7 — designed
            // for exactly this — reproduces it. Same cost class, far better payload.
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static string Sanitize(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var buffer = name.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (Array.IndexOf(invalid, buffer[i]) >= 0)
                {
                    buffer[i] = '_';
                }
            }

            return new string(buffer);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
