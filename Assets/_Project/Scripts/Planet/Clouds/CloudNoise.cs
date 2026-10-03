using System;
using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Чистая математика тайлового 3D-шума (без зависимостей от Unity Texture).
    /// Все функции периодичны: значение в точке p и в точке p+period·k
    /// (по любой из осей) идентично — обязательное условие для GPU wrap
    /// (Repeat) без швов на границе текстуры. Период передаётся явно на
    /// каждом вызове (а не фиксирован в текстуре), поэтому одна и та же
    /// решётка используется для нескольких октав с разной частотой.
    ///
    /// Perlin — для связной, "дутой" базовой формы облака. Worley
    /// (клеточный/cellular) — инвертированный, даёт выпуклые "кучевые" зерна;
    /// комбинация (Perlin-Worley) — рецепт Horizon Zero Dawn (см. ссылку в
    /// сопроводительном .md).
    /// </summary>
    internal static class CloudNoiseGen
    {
        private static uint Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u;
                h = (h ^ (uint)x) * 668265263u;
                h ^= (uint)y + 0x9E3779B9u + (h << 6) + (h >> 2);
                h = (h ^ (uint)z) * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }

        private static int Wrap(int v, int period)
        {
            int m = v % period;
            return m < 0 ? m + period : m;
        }

        /// <summary>Псевдослучайная точка в [0,1)^3 для узла решётки (лат.точка), период — Worley feature point.</summary>
        private static Vector3 LatticePoint(int x, int y, int z, int period, int seed)
        {
            uint h = Hash(Wrap(x, period), Wrap(y, period), Wrap(z, period), seed);
            uint hx = h;
            uint hy = h * 2654435761u;
            uint hz = h * 2246822519u;
            return new Vector3(
                (hx & 0xFFFFFF) / (float)0x1000000,
                (hy & 0xFFFFFF) / (float)0x1000000,
                (hz & 0xFFFFFF) / (float)0x1000000);
        }

        /// <summary>Псевдослучайный единичный градиент для узла решётки (Perlin).</summary>
        private static Vector3 LatticeGradient(int x, int y, int z, int period, int seed)
        {
            uint h = Hash(Wrap(x, period), Wrap(y, period), Wrap(z, period), seed ^ 0x5bd1e995);
            // 12 направлений на рёбра куба (классический выбор градиентов Перлина).
            switch ((int)(h % 12u))
            {
                case 0: return new Vector3(1, 1, 0).normalized;
                case 1: return new Vector3(-1, 1, 0).normalized;
                case 2: return new Vector3(1, -1, 0).normalized;
                case 3: return new Vector3(-1, -1, 0).normalized;
                case 4: return new Vector3(1, 0, 1).normalized;
                case 5: return new Vector3(-1, 0, 1).normalized;
                case 6: return new Vector3(1, 0, -1).normalized;
                case 7: return new Vector3(-1, 0, -1).normalized;
                case 8: return new Vector3(0, 1, 1).normalized;
                case 9: return new Vector3(0, -1, 1).normalized;
                case 10: return new Vector3(0, 1, -1).normalized;
                default: return new Vector3(0, -1, -1).normalized;
            }
        }

        private static float Quintic(float t)
        {
            return t * t * t * (t * ((t * 6f) - 15f) + 10f);
        }

        /// <summary>Тайловый Перлин. p — координаты в ЯЧЕЙКАХ решётки (не [0,1)); period — целое число ячеек по оси.</summary>
        public static float Perlin(Vector3 p, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x);
            int y0 = Mathf.FloorToInt(p.y);
            int z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0;
            float fy = p.y - y0;
            float fz = p.z - z0;

            float n000 = Vector3.Dot(LatticeGradient(x0, y0, z0, period, seed), new Vector3(fx, fy, fz));
            float n100 = Vector3.Dot(LatticeGradient(x0 + 1, y0, z0, period, seed), new Vector3(fx - 1, fy, fz));
            float n010 = Vector3.Dot(LatticeGradient(x0, y0 + 1, z0, period, seed), new Vector3(fx, fy - 1, fz));
            float n110 = Vector3.Dot(LatticeGradient(x0 + 1, y0 + 1, z0, period, seed), new Vector3(fx - 1, fy - 1, fz));
            float n001 = Vector3.Dot(LatticeGradient(x0, y0, z0 + 1, period, seed), new Vector3(fx, fy, fz - 1));
            float n101 = Vector3.Dot(LatticeGradient(x0 + 1, y0, z0 + 1, period, seed), new Vector3(fx - 1, fy, fz - 1));
            float n011 = Vector3.Dot(LatticeGradient(x0, y0 + 1, z0 + 1, period, seed), new Vector3(fx, fy - 1, fz - 1));
            float n111 = Vector3.Dot(LatticeGradient(x0 + 1, y0 + 1, z0 + 1, period, seed), new Vector3(fx - 1, fy - 1, fz - 1));

            float u = Quintic(fx);
            float v = Quintic(fy);
            float w = Quintic(fz);

            float nx00 = Mathf.Lerp(n000, n100, u);
            float nx10 = Mathf.Lerp(n010, n110, u);
            float nx01 = Mathf.Lerp(n001, n101, u);
            float nx11 = Mathf.Lerp(n011, n111, u);
            float nxy0 = Mathf.Lerp(nx00, nx10, v);
            float nxy1 = Mathf.Lerp(nx01, nx11, v);
            // ~[-1,1] (не строго нормировано для 3D градиентов, но в диапазоне остаётся почти всегда).
            return Mathf.Lerp(nxy0, nxy1, w) * 1.2f;
        }

        /// <summary>Тайловый Worley (cellular). Возвращает расстояние до ближайшей feature-точки среди 3x3x3 соседних ячеек, в единицах ячейки (обычно 0..~1.2).</summary>
        public static float Worley(Vector3 p, int period, int seed)
        {
            int xi = Mathf.FloorToInt(p.x);
            int yi = Mathf.FloorToInt(p.y);
            int zi = Mathf.FloorToInt(p.z);

            float minDistSq = 9f;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int cx = xi + dx;
                        int cy = yi + dy;
                        int cz = zi + dz;
                        Vector3 feature = LatticePoint(cx, cy, cz, period, seed);
                        Vector3 featureWorld = new Vector3(cx + feature.x, cy + feature.y, cz + feature.z);
                        Vector3 delta = featureWorld - p;
                        float d = delta.sqrMagnitude;
                        if (d < minDistSq)
                        {
                            minDistSq = d;
                        }
                    }
                }
            }

            return Mathf.Sqrt(minDistSq);
        }

        /// <summary>fBm суммы Перлина, period0 — период первой октавы (ячеек); каждая октава удваивает период (обязательно для бесшовности).</summary>
        public static float PerlinFbm(Vector3 pCells0, int period0, int octaves, float gain, int seed)
        {
            float sum = 0f;
            float amp = 1f;
            float totalAmp = 0f;
            int period = period0;
            Vector3 p = pCells0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(p, period, seed + (i * 101)) * amp;
                totalAmp += amp;
                amp *= gain;
                p *= 2f;
                period *= 2;
            }

            return totalAmp > 0f ? sum / totalAmp : 0f; // ~[-1,1]
        }

        /// <summary>fBm инвертированного Worley (1 - distance, зажато в 0..1) — "кучевые" выпуклости, чем больше октав, тем детальнее эрозия.</summary>
        public static float WorleyFbm(Vector3 pCells0, int period0, int octaves, float gain, int seed)
        {
            float sum = 0f;
            float amp = 1f;
            float totalAmp = 0f;
            int period = period0;
            Vector3 p = pCells0;
            for (int i = 0; i < octaves; i++)
            {
                float w = 1f - Mathf.Clamp01(Worley(p, period, seed + (i * 197)));
                sum += w * amp;
                totalAmp += amp;
                amp *= gain;
                p *= 2f;
                period *= 2;
            }

            return totalAmp > 0f ? sum / totalAmp : 0f; // [0,1]
        }

        public static float Remap(float x, float a, float b, float c, float d)
        {
            return c + ((Mathf.Clamp(x, Mathf.Min(a, b), Mathf.Max(a, b)) - a) * (d - c) / Mathf.Max(1e-5f, b - a));
        }
    }

    /// <summary>
    /// Печёт 3D/статические текстуры облаков на CPU (как AtmosphereOptics печёт
    /// LUT атмосферы) — один раз при старте PlanetCloudsView, пересборка только
    /// при смене профиля. Компьют-шейдер не используется: разрешения намеренно
    /// скромные (64³/32³/24³), печать занимает доли секунды — тот же бюджет,
    /// что и у существующих LUT атмосферы.
    /// </summary>
    internal static class CloudNoiseBaker
    {
        /// <summary>
        /// R = Perlin-Worley база (низкая частота, period=3 ячейки на всю
        /// текстуру — "средние" кучевые формы). G/B/A = fBm инвертированного
        /// Worley на трёх нарастающих частотах (period 4/7/11) — веса эрозии
        /// в шейдере (SampleCloudDensity), от крупной до мелкой.
        /// </summary>
        public static Texture3D BuildShapeTexture(int resolution, int seed)
        {
            resolution = Mathf.Clamp(resolution, 8, 128);
            var pixels = new Color[resolution * resolution * resolution];

            const int basePeriod = 3;
            for (int z = 0; z < resolution; z++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        Vector3 uvw = new Vector3(x, y, z) / resolution;

                        float perlin = CloudNoiseGen.PerlinFbm(uvw * basePeriod, basePeriod, 4, 0.5f, seed);
                        perlin = (perlin * 0.5f) + 0.5f; // -> [0,1]

                        float worleyBase = CloudNoiseGen.WorleyFbm(uvw * basePeriod, basePeriod, 3, 0.5f, seed + 991);

                        // Рецепт Horizon Zero Dawn: Worley "разбухает" Perlin, сохраняя связность.
                        float perlinWorley = CloudNoiseGen.Remap(perlin, worleyBase - 1f, 1f, 0f, 1f);

                        float w1 = CloudNoiseGen.WorleyFbm(uvw * 4, 4, 3, 0.5f, seed + 13);
                        float w2 = CloudNoiseGen.WorleyFbm(uvw * 7, 7, 3, 0.5f, seed + 29);
                        float w3 = CloudNoiseGen.WorleyFbm(uvw * 11, 11, 3, 0.5f, seed + 47);

                        int i = x + (y * resolution) + (z * resolution * resolution);
                        pixels[i] = new Color(perlinWorley, w1, w2, w3);
                    }
                }
            }

            return ToTexture3D(pixels, resolution, TextureFormat.RGBA32);
        }

        /// <summary>RGB = 3 октавы fBm Worley на нарастающей частоте (period 3/6/10) — мелкая эрозия краёв ("дым" у основания).</summary>
        public static Texture3D BuildDetailTexture(int resolution, int seed)
        {
            resolution = Mathf.Clamp(resolution, 4, 64);
            var pixels = new Color[resolution * resolution * resolution];

            for (int z = 0; z < resolution; z++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        Vector3 uvw = new Vector3(x, y, z) / resolution;

                        float d1 = CloudNoiseGen.WorleyFbm(uvw * 3, 3, 2, 0.5f, seed + 501);
                        float d2 = CloudNoiseGen.WorleyFbm(uvw * 6, 6, 2, 0.5f, seed + 613);
                        float d3 = CloudNoiseGen.WorleyFbm(uvw * 10, 10, 2, 0.5f, seed + 727);

                        int i = x + (y * resolution) + (z * resolution * resolution);
                        pixels[i] = new Color(d1, d2, d3, 1f);
                    }
                }
            }

            return ToTexture3D(pixels, resolution, TextureFormat.RGB24);
        }

        private static Vector3 FaceDirection(int face, float u, float v)
        {
            switch (face)
            {
                case 0: return new Vector3(1f, -v, -u);
                case 1: return new Vector3(-1f, -v, u);
                case 2: return new Vector3(u, 1f, v);
                case 3: return new Vector3(u, -1f, -v);
                case 4: return new Vector3(u, -v, 1f);
                default: return new Vector3(-u, -v, -1f);
            }
        }

        private static float UnitNoise(Vector3 direction, int period, int octaves, float gain, int seed)
        {
            float value = CloudNoiseGen.PerlinFbm(direction * period, period, octaves, gain, seed);
            return Mathf.Clamp01((value * 0.5f) + 0.5f);
        }

        private static Vector3 WarpDirection(Vector3 direction, int scale, int seed)
        {
            float x = UnitNoise(direction, Mathf.Max(2, scale / 2), 2, 0.5f, seed + 811);
            float y = UnitNoise(direction, Mathf.Max(2, scale / 2), 2, 0.5f, seed + 1223);
            float z = UnitNoise(direction, Mathf.Max(2, scale / 2), 2, 0.5f, seed + 1637);
            Vector3 warp = new Vector3(x, y, z) - new Vector3(0.5f, 0.5f, 0.5f);
            return (direction + (warp * 0.18f)).normalized;
        }

        public static Cubemap BuildWeatherCubemap(
            int resolution,
            float weatherCellMeters,
            float clearZoneCellMeters,
            float clearZoneFraction,
            float clearZoneSoftness,
            double planetRadius,
            int seed)
        {
            resolution = Mathf.Clamp(resolution, 16, 256);
            int weatherPeriod = Mathf.Clamp(
                Mathf.RoundToInt((float)((2.0 * Math.PI * planetRadius) / Math.Max(1.0, weatherCellMeters))),
                2, 32);
            int clearPeriod = Mathf.Clamp(
                Mathf.RoundToInt((float)((2.0 * Math.PI * planetRadius) / Math.Max(1.0, clearZoneCellMeters))),
                2, 32);

            var faces = new Color[6][];
            var clearFields = new float[6][];
            var clearValues = new List<float>(resolution * resolution * 6);
            for (int face = 0; face < 6; face++)
            {
                faces[face] = new Color[resolution * resolution];
                clearFields[face] = new float[resolution * resolution];
            }

            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    float v = (y + 0.5f) / resolution;
                    for (int x = 0; x < resolution; x++)
                    {
                        float u = (x + 0.5f) / resolution;
                        Vector3 direction = FaceDirection(face, u, v).normalized;
                        Vector3 warped = WarpDirection(direction, weatherPeriod, seed);

                        float broad = UnitNoise(warped, weatherPeriod, 4, 0.55f, seed + 71);
                        float medium = UnitNoise(direction, weatherPeriod * 2, 3, 0.5f, seed + 173);
                        float coverage = Mathf.Clamp01(((broad * 0.72f) + (medium * 0.28f) - 0.16f) / 0.68f);

                        float size = UnitNoise(warped, Mathf.Max(2, weatherPeriod - 1), 3, 0.55f, seed + 311);
                        size = Mathf.Clamp01((size - 0.2f) / 0.6f);
                        float clear = UnitNoise(direction, clearPeriod, 3, 0.5f, seed + 577);
                        float storm = Mathf.Clamp01((coverage * 0.65f) + (size * 0.35f));

                        int index = x + (y * resolution);
                        faces[face][index] = new Color(coverage, size, clear, storm);
                        clearFields[face][index] = clear;
                        clearValues.Add(clear);
                    }
                }
            }

            float[] sortedClear = clearValues.ToArray();
            Array.Sort(sortedClear);
            float fraction = Mathf.Clamp(clearZoneFraction, 0f, 0.1f);
            int thresholdIndex = Mathf.Clamp(
                Mathf.FloorToInt((sortedClear.Length - 1) * (1f - fraction)),
                0,
                sortedClear.Length - 1);
            float threshold = sortedClear[thresholdIndex];
            float softness = Mathf.Max(0.001f, clearZoneSoftness);

            var weather = new Cubemap(resolution, TextureFormat.RGBAHalf, true)
            {
                name = "CloudWeather",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 0,
            };

            for (int face = 0; face < 6; face++)
            {
                Color[] pixels = faces[face];
                for (int i = 0; i < pixels.Length; i++)
                {
                    float clear = clearFields[face][i];
                    float t = Mathf.Clamp01((clear - (threshold - softness)) / (softness * 2f));
                    pixels[i].b = t * t * (3f - (2f * t));
                }

                weather.SetPixels(pixels, (CubemapFace)face);
            }

            weather.Apply(true, false);
            return weather;
        }

        private static Texture3D ToTexture3D(Color[] pixels, int resolution, TextureFormat format)
        {
            var tex = new Texture3D(resolution, resolution, resolution, format, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 0,
            };
            tex.SetPixels(pixels);
            tex.Apply(true, false);
            return tex;
        }
    }
}
