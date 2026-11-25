using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Assigns the terrain detail textures and saves:
    ///  * the EARTH photo set to atmosphere/climate profiles;
    ///  * the LUNAR regolith set to airless (Lunar.Enabled) profiles, as a RELATIVE
    ///    modulation with a measured mean (TerrainProfile.DetailAlbedoMean).
    /// Auto-runs once by a Temp marker (user edits are not overwritten afterwards)
    /// or from the menu.
    ///
    /// The separation is not cosmetic. Stamping the Earth set onto Artemis replaced
    /// the Moon's palette + macro albedo with a dark green grass photo and a 0.6
    /// biome tint: the ground came out at 0.05 linear instead of 0.2, i.e. ~4x darker
    /// than the body's own far disc, so the Moon visibly darkened while approaching.
    /// </summary>
    public static class TerrainTextureSetup
    {
        internal const string Marker = "Temp/terrain-textures-v1.done";

        private const string Folder = "Assets/_Project/Textures/Terrain";
        private const string LunarFolder = Folder + "/Lunar";

        [MenuItem("Tools/Galilego/Assign terrain textures")]
        public static void Run()
        {
            Texture2D low = Load(Folder + "/terrain_low");
            Texture2D mid = Load(Folder + "/terrain_mid");
            Texture2D high = Load(Folder + "/terrain_high");
            Texture2D steep = Load(Folder + "/terrain_steep");
            Texture2D occlusion = Load(Folder + "/terrain_occlusion");
            if (low == null || mid == null || high == null || steep == null)
            {
                Debug.LogWarning("[TerrainTextureSetup] нет текстур в " + Folder + " — пропуск.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:TerrainProfileAsset");
            int count = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TerrainProfileAsset asset = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(path);
                if (asset == null || asset.Profile == null)
                {
                    continue;
                }

                // Airless bodies own their regolith set; Earth's grass here was the bug.
                if (asset.Profile.Lunar != null && asset.Profile.Lunar.Enabled)
                {
                    continue;
                }

                asset.Profile.TextureLow = low;
                asset.Profile.TextureMid = mid;
                asset.Profile.TextureHigh = high;
                asset.Profile.TextureSteep = steep;
                asset.Profile.TextureOcclusion = occlusion;
                asset.Profile.TextureScale = 0.04d;
                asset.Profile.DetailAlbedoMean = 0f;
                asset.Profile.LowMidBlendStart = 30d;
                asset.Profile.LowMidBlendEnd = 60d;
                asset.Profile.MidHighBlendStart = 4500d;
                asset.Profile.MidHighBlendEnd = 5500d;
                asset.Profile.SteepBlendStart = 0.7d;
                asset.Profile.SteepBlendEnd = 1.4d;
                EditorUtility.SetDirty(asset);
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTextureSetup] текстуры рельефа назначены в " + count + " профилей.");
        }

        [MenuItem("Tools/Galilego/Assign lunar regolith textures")]
        public static void AssignLunar()
        {
            Texture2D flat = AssetDatabase.LoadAssetAtPath<Texture2D>(
                LunarFolder + "/regolith_flat_clean.png");
            Texture2D rock = AssetDatabase.LoadAssetAtPath<Texture2D>(
                LunarFolder + "/regolith_rock_clean.png");
            Texture2D flatNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                LunarFolder + "/regolith_flat_normal.png");
            Texture2D rockNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                LunarFolder + "/regolith_rock_normal.png");
            if (flat == null || rock == null)
            {
                Debug.LogWarning("[TerrainTextureSetup] нет набора реголита в " + LunarFolder +
                    " — запустите Tools/Textures/GenerateRegolithTextures.py.");
                return;
            }

            // The generator writes the mean it actually encoded; the shader divides
            // the sample by it, so a regenerated set cannot silently change the albedo.
            float mean = 0.292f;
            string meanPath = LunarFolder + "/regolith_mean.txt";
            if (File.Exists(meanPath))
            {
                string text = File.ReadAllText(meanPath).Trim();
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                    && parsed > 0.01f)
                {
                    mean = parsed;
                }
            }

            string[] guids = AssetDatabase.FindAssets("t:TerrainProfileAsset");
            int count = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TerrainProfileAsset asset = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(path);
                if (asset == null || asset.Profile == null
                    || asset.Profile.Lunar == null || !asset.Profile.Lunar.Enabled)
                {
                    continue;
                }

                // Low/Mid are never sampled on an airless body: with no sea level every
                // altitude maps to the high band, so they alias the flat map instead of
                // carrying a second Earth photo nobody sees.
                asset.Profile.TextureLow = flat;
                asset.Profile.TextureMid = flat;
                asset.Profile.TextureHigh = flat;
                asset.Profile.TextureSteep = rock;
                asset.Profile.TextureOcclusion = null;
                // Micro-relief: the normal maps are what make a 1 cm regolith grain
                // read at all. Without them the ground is painted cloth under a low sun.
                asset.Profile.TextureDetailNormal = flatNormal;
                asset.Profile.TextureDetailNormalSteep = rockNormal;
                asset.Profile.DetailNormalStrength = 0.75f;
                asset.Profile.TextureScale = 0.04d;
                asset.Profile.DetailAlbedoMean = mean;
                asset.Profile.LowMidBlendStart = 30d;
                asset.Profile.LowMidBlendEnd = 60d;
                asset.Profile.MidHighBlendStart = 4500d;
                asset.Profile.MidHighBlendEnd = 5500d;
                asset.Profile.SteepBlendStart = 0.7d;
                asset.Profile.SteepBlendEnd = 1.4d;
                // The lunar albedo (maria dark, fresh ejecta bright) rides in the
                // per-vertex detail channel. The shader no longer fades it by pixel
                // footprint on airless bodies (the macro map is the LOD authority),
                // so it is visible underfoot — 0 made the ground one flat grey.
                asset.Profile.ColorDetailStrength = 0.85d;
                asset.Profile.ColorRockSlopeTan = 0d;
                EditorUtility.SetDirty(asset);
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTextureSetup] реголит назначен в " + count +
                " безатмосферных профилей (mean " + mean.ToString("F3", CultureInfo.InvariantCulture) + ").");
        }

        private static Texture2D Load(string pathWithoutExtension)
        {
            // Prefer the "clean" textures without the directional pattern
            // (TerrainTextureDeband).
            Texture2D clean = AssetDatabase.LoadAssetAtPath<Texture2D>(pathWithoutExtension + "_clean.jpg");
            if (clean != null)
            {
                return clean;
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(pathWithoutExtension + ".jpg");
        }
    }

    /// <summary>First-run auto-start: while the marker is absent and the textures exist.</summary>
    [InitializeOnLoad]
    internal static class TerrainTextureSetupAuto
    {
        static TerrainTextureSetupAuto()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (File.Exists(TerrainTextureSetup.Marker))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/Terrain/terrain_low.jpg") == null)
            {
                return;
            }

            try
            {
                TerrainTextureSetup.Run();
                TerrainTextureSetup.AssignLunar();
                File.WriteAllText(TerrainTextureSetup.Marker, System.DateTime.Now.ToString("O"));
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[TerrainTextureSetup] ошибка: " + exception);
            }
        }
    }
}
