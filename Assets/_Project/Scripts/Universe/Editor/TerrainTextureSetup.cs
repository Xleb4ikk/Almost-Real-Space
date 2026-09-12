using System.IO;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Назначает текстуры рельефа (low/mid/high/steep + occlusion) во
    /// все TerrainProfileAsset и сохраняет. Автозапуск один раз по маркеру в
    /// Temp (правки пользователя потом не затираются) или из меню.
    /// </summary>
    public static class TerrainTextureSetup
    {
        internal const string Marker = "Temp/terrain-textures-v1.done";

        private const string Folder = "Assets/_Project/Textures/Terrain";

        [MenuItem("Tools/Galilego/Assign terrain textures")]
        public static void Run()
        {
            Texture2D low = Load("terrain_low");
            Texture2D mid = Load("terrain_mid");
            Texture2D high = Load("terrain_high");
            Texture2D steep = Load("terrain_steep");
            Texture2D occlusion = Load("terrain_occlusion");
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

                asset.Profile.TextureLow = low;
                asset.Profile.TextureMid = mid;
                asset.Profile.TextureHigh = high;
                asset.Profile.TextureSteep = steep;
                asset.Profile.TextureOcclusion = occlusion;
                asset.Profile.TextureScale = 0.04d;
                asset.Profile.LowMidBlendStart = 30d;
                asset.Profile.LowMidBlendEnd = 60d;
                asset.Profile.MidHighBlendStart = 2500d;
                asset.Profile.MidHighBlendEnd = 3500d;
                asset.Profile.SteepBlendStart = 0.7d;
                asset.Profile.SteepBlendEnd = 1.4d;
                EditorUtility.SetDirty(asset);
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTextureSetup] текстуры рельефа назначены в " + count + " профилей.");
        }

        private static Texture2D Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + name + ".jpg");
        }
    }

    /// <summary>Первичный автозапуск: пока маркера нет и текстуры на месте.</summary>
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
                File.WriteAllText(TerrainTextureSetup.Marker, System.DateTime.Now.ToString("O"));
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[TerrainTextureSetup] ошибка: " + exception);
            }
        }
    }
}
