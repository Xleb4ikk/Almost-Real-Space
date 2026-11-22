using System;
using System.IO;
using System.Threading.Tasks;
using Galilego.Core;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Bakes the two body-fixed equirect maps an airless body needs to look real ABOVE
    /// the mesh: albedo (maria, fresh ejecta, rays) and the SURFACE normal (crater rims,
    /// basin walls, rilles).
    ///
    /// Why a bake at all: at 300 km the terrain still has ~1 km quads, so every piece of
    /// albedo detail smaller than a quad is a vertex-interpolated smear, and the shader
    /// kills it outright (footprint fade). The macro map is sampled per fragment from
    /// the body direction, so the same field stays sharp from orbit down to the altitude
    /// where its texel becomes bigger than a pixel — the shader switches on the pixel
    /// angle vs the texel angle, not on a hand-tuned distance.
    ///
    /// The same two textures feed <see cref="BodyFarDiscView"/>: one bake, one look, and
    /// the billboard-to-terrain hand-over stops being a change of material.
    ///
    /// Source of truth is LunarRelief (the same function the mesh and the physics use),
    /// so the map cannot drift from the ground.
    ///
    /// Menu: Tools/Galilego/Airless Bodies/Bake lunar macro maps.
    /// </summary>
    public static class LunarMacroMapBaker
    {
        private const string Folder = "Assets/_Project/Profiles/Terrain/Lunar";
        private const int Width = 2048;
        private const int Height = 1024;
        private const float AlbedoRangeMin = 0.15f;
        private const float AlbedoRangeMax = 1.8f;

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
                FindObjectsInactive.Include, FindObjectsSortMode.None);
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

        /// <summary>Rebake one body. Kept public: the Lunar Body Creator and tests call it.</summary>
        public static void BakeOne(BodyAuthoring body, TerrainProfileAsset asset)
        {
            TerrainProfile profile = asset.Profile;
            LunarParams lunar = profile.Lunar.ToParams(body.TerrainSeed);
            TerrainPaletteData palette = profile.Palette ?? new TerrainPaletteData();

            var heights = new double[Width * Height];
            var albedo = new float[Width * Height];

            // Parallel: Sample() is pure and the params are a struct copy. ~57k samples/s
            // single-threaded, 2.1M texels total, so seconds instead of a minute.
            Parallel.For(0, Height, y =>
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
            });

            var albedoPixels = new Color32[Width * Height];
            var normalPixels = new Color32[Width * Height];
            double radius = Math.Max(1000d, profile.Lunar.RadiusMeters);
            double dLon = (2d * Math.PI) / Width;
            double dLat = Math.PI / Height;

            Parallel.For(0, Height, y =>
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
            });

            EnsureFolder(Folder);
            // Name the files after the PROFILE, not the body: two bodies can share one
            // profile, and ASCII asset paths survive CI and non-Windows tooling.
            string safe = Sanitize(string.IsNullOrEmpty(asset.name) ? body.name : asset.name);
            string albedoPath = Folder + "/" + safe + "_macro_albedo.png";
            string normalPath = Folder + "/" + safe + "_macro_normal.png";
            WritePng(albedoPath, albedoPixels);
            WritePng(normalPath, normalPixels);

            Texture2D albedoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            Texture2D normalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

            profile.MacroAlbedo = albedoTexture;
            profile.MacroNormal = normalTexture;
            EditorUtility.SetDirty(asset);

            // The far disc of this body uses the very same bake: the disc and the terrain
            // then differ only in geometry, not in what the surface looks like.
            BodyFarDiscView disc = body.GetComponent<BodyFarDiscView>();
            if (disc != null)
            {
                disc.SurfaceMap = albedoTexture;
                disc.NormalMap = normalTexture;
                EditorUtility.SetDirty(disc);
                EditorSceneManager.MarkSceneDirty(body.gameObject.scene);
            }

            Debug.Log("[LunarMacroMapBaker] " + body.name + ": " + albedoPath + " (" + Width + "x" + Height + ")");
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

        private static void WritePng(string path, Color32[] pixels)
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
            importer.alphaSource = TextureImporterAlphaSource.None;
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
