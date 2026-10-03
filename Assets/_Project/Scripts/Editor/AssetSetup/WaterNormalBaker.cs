using System.IO;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Запекает 2 тайлящиеся normal map воды (HOWTO-Water: «Normalmap», две карты,
    /// вторая мельче первой, скролл в разные стороны). Замена скачанным CC0:
    /// внешняя сеть в этой среде недоступна, поэтому карты генерятся детерминированно
    /// в репозиторий — никаких проприетарных битов, можно позже заменить скачанными
    /// CC0 один в один без правок кода (имена/слоты сохранятся).
    ///
    /// Использование в обоих планах:
    ///   - План А (HDRP Water): слоты normal/ripples Water Surface;
    ///   - План Б (Galilego/WaterSurface): _NormalMap0/1 со скроллом в противоход.
    /// </summary>
    public static class WaterNormalBaker
    {
        private const string Folder = "Assets/_Project/Resources/Water";
        private const int Size = 512;

        [MenuItem("Tools/Galilego/Bake water normal maps")]
        public static void Bake()
        {
            string absoluteFolder = Path.Combine(Application.dataPath, "_Project/Resources/Water");
            Directory.CreateDirectory(absoluteFolder);

            BakeOne(Path.Combine(absoluteFolder, "water_normal_a.png"),
                "Assets/_Project/Resources/Water/water_normal_a.png",
                seed: 1337, baseCells: 6, octaves: 5, strength: 2.0f);
            BakeOne(Path.Combine(absoluteFolder, "water_normal_b.png"),
                "Assets/_Project/Resources/Water/water_normal_b.png",
                seed: 7211, baseCells: 9, octaves: 5, strength: 1.4f);

            AssetDatabase.Refresh();
            Debug.Log("[WaterNormalBaker] готово: " + Folder + "/water_normal_{a,b}.png (NormalMap, Repeat, без sRGB).");
        }

        private static void BakeOne(string absolutePath, string assetPath, int seed, int baseCells, int octaves, float strength)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGB24, false, true);
            Color[] pixels = new Color[Size * Size];
            float epsilon = 1f / Size;

            for (int y = 0; y < Size; y++)
            {
                float v = (float)y / Size;
                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / Size;
                    float hC = Fbm(u, v, seed, baseCells, octaves);
                    float hX = Fbm(u + epsilon, v, seed, baseCells, octaves);
                    float hY = Fbm(u, v + epsilon, seed, baseCells, octaves);
                    Vector3 n = new Vector3((hC - hX) * strength * Size * 0.05f,
                        (hC - hY) * strength * Size * 0.05f, 1f).normalized;
                    pixels[(y * Size) + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.sRGBTexture = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
        }

        private static float Fbm(float u, float v, int seed, int baseCells, int octaves)
        {
            float sum = 0f;
            float amp = 0.5f;
            int period = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * ValueNoise(u * period, v * period, seed + (o * 101), period);
                period *= 2;
                amp *= 0.5f;
            }

            return sum;
        }

        private static float ValueNoise(float x, float y, int seed, int period)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - Mathf.Floor(x);
            float yf = y - Mathf.Floor(y);
            float u = xf * xf * (3f - (2f * xf));
            float v = yf * yf * (3f - (2f * yf));

            float a = Hash(Wrap(xi, period), Wrap(yi, period), seed);
            float b = Hash(Wrap(xi + 1, period), Wrap(yi, period), seed);
            float c = Hash(Wrap(xi, period), Wrap(yi + 1, period), seed);
            float d = Hash(Wrap(xi + 1, period), Wrap(yi + 1, period), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static int Wrap(int i, int period)
        {
            int r = i % period;
            return r < 0 ? r + period : r;
        }

        private static float Hash(int x, int y, int seed)
        {
            // Детерминированный хэш решётки (без таблиц перестановок).
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 974634211);
                h = ((h >> 13) ^ h) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / (float)0x1000000;
            }
        }
    }
}
