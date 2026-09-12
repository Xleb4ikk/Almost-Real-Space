using System.IO;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Процедурная замена текстур рельефа: в исходных фото-текстурах запечён
    /// направленный волнистый паттерн («газонные полосы»): на тайле 25 м он
    /// читается как регулярные диагональные линии и мерцает/«двигается» на
    /// мипах при движении. Здесь берём СРЕДНИЙ цвет исходника как базовый тон
    /// и наносим изотропное бесшовное value-noise зерно — тон сохраняется,
    /// направленный паттерн исчезает. Occlusion пересобирается мягким шумом.
    /// Запуск один раз по маркеру или из меню.
    /// </summary>
    public static class TerrainTextureDeband
    {
        internal const string Marker = "Temp/terrain-textures-clean-v1.done";
        private const string Folder = "Assets/_Project/Textures/Terrain";
        private const int Size = 1024;

        [MenuItem("Tools/Galilego/Regenerate clean terrain textures")]
        public static void Run()
        {
            Regenerate("terrain_low", 0.10f, 0.030f, 1);
            Regenerate("terrain_mid", 0.16f, 0.035f, 2);
            Regenerate("terrain_high", 0.18f, 0.040f, 3);
            Regenerate("terrain_steep", 0.16f, 0.030f, 4);
            GenerateOcclusion("terrain_occlusion", 5);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            AssignToProfiles();
            Debug.Log("[TerrainTextureDeband] чистые текстуры рельефа сгенерированы и назначены.");
        }

        private static void Regenerate(string name, float grainContrast, float speckle, int seed)
        {
            string sourcePath = Folder + "/" + name + ".jpg";
            byte[] bytes = File.ReadAllBytes(sourcePath);
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(source, bytes, false))
            {
                Object.DestroyImmediate(source);
                Debug.LogWarning("[TerrainTextureDeband] не удалось прочитать " + sourcePath);
                return;
            }

            Color32[] src = source.GetPixels32();
            double sumR = 0d;
            double sumG = 0d;
            double sumB = 0d;
            for (int i = 0; i < src.Length; i++)
            {
                sumR += src[i].r;
                sumG += src[i].g;
                sumB += src[i].b;
            }

            double baseR = sumR / src.Length;
            double baseG = sumG / src.Length;
            double baseB = sumB / src.Length;
            Object.DestroyImmediate(source);

            var output = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                float v = y / (float)Size;
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    // Многооктавный бесшовный шум: крупные пятна + средний + мелкое зерно.
                    float mottle = Fbm(u, v, 24, 4, 0.55f, seed) * 0.55f + Fbm(u, v, 96, 2, 0.5f, seed + 11) * 0.45f;
                    float fine = Fbm(u, v, 384, 1, 0.5f, seed + 23);
                    float n = (mottle * 0.7f) + (fine * 0.3f);
                    float speck = (Hash01(x, y, seed + 31) * 2f - 1f) * speckle;
                    float k = 1f + (n * grainContrast) + speck;
                    if (k < 0.35f)
                    {
                        k = 0.35f;
                    }

                    pixels[(y * Size) + x] = new Color32(
                        ClampByte(baseR * k), ClampByte(baseG * k), ClampByte(baseB * k), 255);
                }
            }

            output.SetPixels32(pixels);
            output.Apply();
            WriteTexture(output, name + "_clean");
            Object.DestroyImmediate(output);
        }

        private static void GenerateOcclusion(string name, int seed)
        {
            var output = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                float v = y / (float)Size;
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float n = Fbm(u, v, 24, 4, 0.55f, seed) * 0.5f + Fbm(u, v, 128, 2, 0.5f, seed + 7) * 0.5f;
                    float k = 0.86f + (n * 0.10f);
                    byte b = ClampByte(k * 255d);
                    pixels[(y * Size) + x] = new Color32(b, b, b, 255);
                }
            }

            output.SetPixels32(pixels);
            output.Apply();
            WriteTexture(output, name + "_clean");
            Object.DestroyImmediate(output);
        }

        private static void WriteTexture(Texture2D texture, string name)
        {
            byte[] jpg = ImageConversion.EncodeToJPG(texture, 92);
            string path = Folder + "/" + name + ".jpg";
            File.WriteAllBytes(path, jpg);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.anisoLevel = 4;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
        }

        private static void AssignToProfiles()
        {
            Texture2D low = Load("terrain_low_clean");
            Texture2D mid = Load("terrain_mid_clean");
            Texture2D high = Load("terrain_high_clean");
            Texture2D steep = Load("terrain_steep_clean");
            Texture2D occlusion = Load("terrain_occlusion_clean");
            if (low == null || mid == null || high == null || steep == null)
            {
                Debug.LogWarning("[TerrainTextureDeband] чистые текстуры не найдены — назначение пропущено.");
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
                if (occlusion != null)
                {
                    asset.Profile.TextureOcclusion = occlusion;
                }

                EditorUtility.SetDirty(asset);
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTextureDeband] текстуры назначены в " + count + " профилей.");
        }

        private static Texture2D Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + name + ".jpg");
        }

        private static byte ClampByte(double value)
        {
            if (value < 0d)
            {
                return 0;
            }

            if (value > 255d)
            {
                return 255;
            }

            return (byte)(value + 0.5d);
        }

        // --- Бесшовный (периодический) value-noise ---------------------------

        private static float Fbm(float u, float v, int basePeriod, int octaves, float gain, int seed)
        {
            float amplitude = 1f;
            float frequency = basePeriod;
            float sum = 0f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * ValueNoise(u * frequency, v * frequency, (int)frequency, seed + (i * 131));
                norm += amplitude;
                amplitude *= gain;
                frequency *= 2f;
            }

            return norm > 0f ? sum / norm : 0f;
        }

        private static float ValueNoise(float x, float y, int period, int seed)
        {
            float xf = Mathf.Floor(x);
            float yf = Mathf.Floor(y);
            int ix = (int)xf;
            int iy = (int)yf;
            float fx = x - xf;
            float fy = y - yf;
            float ux = fx * fx * fx * (fx * ((fx * 6f) - 15f) + 10f);
            float uy = fy * fy * fy * (fy * ((fy * 6f) - 15f) + 10f);

            float c00 = Hash(ix, iy, period, seed);
            float c10 = Hash(ix + 1, iy, period, seed);
            float c01 = Hash(ix, iy + 1, period, seed);
            float c11 = Hash(ix + 1, iy + 1, period, seed);
            float x0 = Mathf.Lerp(c00, c10, ux);
            float x1 = Mathf.Lerp(c01, c11, ux);
            return Mathf.Lerp(x0, x1, uy);
        }

        private static float Hash(int x, int y, int period, int seed)
        {
            int px = ((x % period) + period) % period;
            int py = ((y % period) + period) % period;
            uint h = ((uint)px * 374761393u) ^ ((uint)py * 668265263u) ^ ((uint)seed * 2147483647u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return ((h & 0xFFFFu) / 32767.5f) - 1f;
        }

        private static float Hash01(int x, int y, int seed)
        {
            uint h = ((uint)x * 374761393u) ^ ((uint)y * 668265263u) ^ ((uint)seed * 2147483647u);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFu) / 65535f;
        }
    }

    /// <summary>Автозапуск один раз по маркеру (правки пользователя потом не затираются).</summary>
    [InitializeOnLoad]
    internal static class TerrainTextureDebandAuto
    {
        static TerrainTextureDebandAuto()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Не пропускаем сборку навсегда: ждём выхода из Play.
                EditorApplication.delayCall += Run;
                return;
            }

            if (File.Exists(TerrainTextureDeband.Marker))
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Textures/Terrain/terrain_low.jpg") == null)
            {
                return;
            }

            try
            {
                TerrainTextureDeband.Run();
                File.WriteAllText(TerrainTextureDeband.Marker, System.DateTime.Now.ToString("O"));
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[TerrainTextureDeband] ошибка: " + exception);
            }
        }
    }
}
