using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Настройки импорта для текстур декора местности: `*nrm` — normal map
    /// (sRGB off), карты ветра (`grassuv*`) — линейные, альбедо — sRGB +
    /// alphaIsTransparency (у травы/биллбордов альфа в текстуре).
    /// </summary>
    public sealed class GroundDecorTexturePostprocessor : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/Textures/Decor/";
        private const string TerrainFolder = "Assets/_Project/Textures/Terrain/";

        private void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(TerrainFolder, StringComparison.OrdinalIgnoreCase))
            {
                string terrainFile = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
                bool occlusion = terrainFile.Contains("occlusion");
                TextureImporter terrainImporter = (TextureImporter)assetImporter;
                terrainImporter.textureType = TextureImporterType.Default;
                terrainImporter.sRGBTexture = !occlusion;
                terrainImporter.alphaIsTransparency = false;
                terrainImporter.mipmapEnabled = true;
                terrainImporter.wrapMode = TextureWrapMode.Repeat;
                terrainImporter.maxTextureSize = 2048;
                return;
            }

            if (!assetPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string file = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            bool normal = file.EndsWith("nrm") || file.EndsWith("normal");
            bool wind = file.Contains("grassuv");
            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && !wind;
            importer.alphaIsTransparency = !normal && !wind;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 2048;
            // ВАЖНО: исходные нормали обычно DirectX (зелёный инвертирован к
            // Unity); шейдеры декора пока нормали не сэмплят — проверить при
            // подключении.
        }
    }
}
