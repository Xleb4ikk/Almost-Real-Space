using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Import settings for ground decor textures: `*nrm` — normal map (sRGB off),
    /// wind maps (`grassuv*`) — linear, albedo — sRGB + alphaIsTransparency
    /// (grass/billboards carry alpha in the texture).
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
                // Detail normal maps are LINEAR data, not colour: sRGB decoding bends
                // every component and the surface lights as if the normals were rotated.
                bool terrainNormal = terrainFile.Contains("normal");
                bool lunar = assetPath.IndexOf("/Lunar/", StringComparison.OrdinalIgnoreCase) >= 0;
                TextureImporter terrainImporter = (TextureImporter)assetImporter;
                terrainImporter.textureType = TextureImporterType.Default;
                terrainImporter.sRGBTexture = !occlusion && !terrainNormal;
                terrainImporter.alphaIsTransparency = false;
                terrainImporter.mipmapEnabled = true;
                terrainImporter.wrapMode = TextureWrapMode.Repeat;
                terrainImporter.maxTextureSize = 2048;
                if (lunar)
                {
                    // BC7 for the albedo alpha (cavity) and the normals. The default
                    // compressor (BC1) drops alpha and smears the normals.
                    terrainImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                    terrainImporter.filterMode = FilterMode.Trilinear;
                    terrainImporter.anisoLevel = 4;
                }
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
            // IMPORTANT: source normals are usually DirectX (green channel inverted
            // vs Unity); decor shaders do not sample them yet — check when hooking
            // them up.
        }
    }
}
