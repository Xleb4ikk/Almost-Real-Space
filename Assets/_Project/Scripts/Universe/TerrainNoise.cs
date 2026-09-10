using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Galilego.Universe
{
    /// <summary>
    /// Плоские параметры шума для Burst-job: только blittable-поля, без ссылок.
    /// Все double (касты float запрещены): внутренности шума обязаны идти в
    /// double — float не держит схему оффсетов (seed·91.7 при warp-offset уже
    /// ~5e6, ulp ~0.5; замер T84: 827 м расхождения). Float — только массивы
    /// масок на выходе (цвет физику не касается).
    /// </summary>
    public struct TerrainNoiseParams
    {
        public int Seed;
        public double BaseFrequency;
        public int Octaves;
        public double Lacunarity;
        public double Gain;
        public double ContinentFrequency;
        public int ContinentOctaves;
        public double ContinentThreshold;
        public double ContinentSharpness;
        public double ContinentDepth;
        public double RidgedMix;
        public double WarpStrength;
        public double WarpFrequency;
        public int WarpOctaves;
        public int WarpSeedOffset;
        public double ColorNoiseFrequency;
        public int ColorNoiseOctaves;
        public int ColorNoiseSeedOffset;

        /// <summary>Считать ли маску в job'е (иначе пишет 0). Экономит ~15% при выключенной маске.</summary>
        public bool ComputeMask;

        public static TerrainNoiseParams FromTerrain(HeightfieldTerrain terrain)
        {
            return new TerrainNoiseParams
            {
                Seed = terrain.Seed,
                BaseFrequency = terrain.BaseFrequency,
                Octaves = terrain.Octaves,
                Lacunarity = terrain.Lacunarity,
                Gain = terrain.Gain,
                ContinentFrequency = terrain.ContinentFrequency,
                ContinentOctaves = terrain.ContinentOctaves,
                ContinentThreshold = terrain.ContinentThreshold,
                ContinentSharpness = terrain.ContinentSharpness,
                ContinentDepth = terrain.ContinentDepth,
                RidgedMix = terrain.RidgedMix,
                WarpStrength = terrain.WarpStrength,
                WarpFrequency = terrain.WarpFrequency,
                WarpOctaves = terrain.WarpOctaves,
                WarpSeedOffset = terrain.WarpSeedOffset,
                ColorNoiseFrequency = terrain.ColorNoiseFrequency,
                ColorNoiseOctaves = terrain.ColorNoiseOctaves,
                ColorNoiseSeedOffset = terrain.ColorNoiseSeedOffset,
                ComputeMask = terrain.ColorNoiseFrequency > 0d && terrain.ColorNoiseStrength != 0d
            };
        }
    }

    /// <summary>
    /// Burst-совместимая форма heightfield: та же формула, что HeightfieldTerrain
    /// (порядок операций зеркальный — расхождение только в последнем ulp
    /// кодогенерации, меряет T84). Чистые статики на double3/math: ни ссылок,
    /// ни аллокаций, ни System.Math. HeightfieldTerrain делегирует сюда форму —
    /// дублирования нет, паритет визуал/физика по построению, а не допуском.
    /// </summary>
    public static class TerrainNoise
    {
        public static double SampleHeight(TerrainNoiseParams p, double3 direction)
        {
            double gain = EffectiveGain(p.Gain);
            double lacunarity = EffectiveLacunarity(p.Lacunarity);
            if (p.WarpStrength <= 0d && p.RidgedMix <= 0d && p.ContinentFrequency <= 0d
                && lacunarity == 2d && gain == 0.5d)
            {
                return SampleFbmLegacy(p, direction);
            }

            double3 q = direction;
            if (p.WarpStrength > 0d)
            {
                q = ApplyWarp(p, q, gain, lacunarity);
            }

            double baseHeight = SampleFbmEx(q, p.BaseFrequency, p.Octaves, p.Seed, 0, gain, lacunarity);

            double continent = 1d;
            if (p.ContinentFrequency > 0d)
            {
                double c = SampleFbmEx(q, p.ContinentFrequency, p.ContinentOctaves, p.Seed, 1, gain, lacunarity);
                continent = Smoothstep01((c - (p.ContinentThreshold - p.ContinentSharpness))
                    / math.max(1e-9d, 2d * p.ContinentSharpness));
            }

            double h = baseHeight;
            if (p.RidgedMix > 0d)
            {
                double ridged = (SampleRidged(p, q, gain, lacunarity) * 2d) - 1d;
                double k = math.min(1d, math.max(0d, p.RidgedMix)) * continent;
                h = baseHeight + (ridged - baseHeight) * k;
            }

            h -= (1d - continent) * math.max(0d, p.ContinentDepth);
            return h;
        }

        public static double SampleColorNoise(TerrainNoiseParams p, double3 direction)
        {
            double gain = EffectiveGain(p.Gain);
            double lacunarity = EffectiveLacunarity(p.Lacunarity);
            return SampleFbmEx(
                direction, p.ColorNoiseFrequency, p.ColorNoiseOctaves,
                p.Seed + (p.ColorNoiseSeedOffset * 7919), 4, gain, lacunarity);
        }

        public static double EffectiveGain(double gain)
        {
            return gain > 0d && gain <= 1d ? gain : 0.5d;
        }

        public static double EffectiveLacunarity(double lacunarity)
        {
            return lacunarity >= 1d && lacunarity <= 8d ? lacunarity : 2d;
        }

        private static double SampleFbmLegacy(TerrainNoiseParams p, double3 direction)
        {
            int octaves = p.Octaves < 1 ? 1 : p.Octaves;
            double amplitude = 1d;
            double frequency = p.BaseFrequency < 1e-6d ? 1e-6d : p.BaseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = new double3(p.Seed * 17.31d, p.Seed * 7.77d, p.Seed * 29.13d);
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * ValueNoise(direction * frequency + offset);
                norm += amplitude;
                amplitude *= 0.5d;
                frequency *= 2d;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            return norm > 0d ? sum / norm : 0d;
        }

        private static double3 ApplyWarp(TerrainNoiseParams p, double3 direction, double gain, double lacunarity)
        {
            double frequency = p.WarpFrequency < 1e-6d ? 1e-6d : p.WarpFrequency;
            int octaves = p.WarpOctaves < 1 ? 1 : p.WarpOctaves;
            int seed = p.Seed + (p.WarpSeedOffset * 7919);
            double3 warp = new double3(
                SampleFbmEx(direction, frequency, octaves, seed, 100, gain, lacunarity),
                SampleFbmEx(direction, frequency, octaves, seed, 200, gain, lacunarity),
                SampleFbmEx(direction, frequency, octaves, seed, 300, gain, lacunarity));
            return direction + (warp * p.WarpStrength);
        }

        private static double SampleFbmEx(double3 direction, double baseFrequency, int octaves, int seed, int salt, double gain, double lacunarity)
        {
            int n = octaves < 1 ? 1 : octaves;
            double amplitude = 1d;
            double frequency = baseFrequency < 1e-6d ? 1e-6d : baseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = SaltOffset(seed, salt);
            for (int o = 0; o < n; o++)
            {
                sum += amplitude * ValueNoise(direction * frequency + offset);
                norm += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            return norm > 0d ? sum / norm : 0d;
        }

        private static double SampleRidged(TerrainNoiseParams p, double3 direction, double gain, double lacunarity)
        {
            int n = p.Octaves < 1 ? 1 : p.Octaves;
            double amplitude = 1d;
            double frequency = p.BaseFrequency < 1e-6d ? 1e-6d : p.BaseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = SaltOffset(p.Seed, 2);
            for (int o = 0; o < n; o++)
            {
                double v = ValueNoise(direction * frequency + offset);
                sum += amplitude * (1d - (v >= 0d ? v : -v));
                norm += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            return norm > 0d ? sum / norm : 0d;
        }

        private static double3 SaltOffset(int seed, int salt)
        {
            switch (salt)
            {
                case 1:
                    return new double3(seed * 57.31d + 101.3d, seed * 43.77d + 67.9d, seed * 71.13d + 37.1d);
                case 2:
                    return new double3(seed * 23.17d + 503.7d, seed * 89.31d + 311.3d, seed * 11.71d + 877.9d);
                case 4:
                    return new double3(seed * 17.13d + 2100.7d, seed * 37.31d + 1900.4d, seed * 73.17d + 2300.8d);
                case 100:
                    return new double3(seed * 91.7d + 1000.3d, seed * 47.31d + 700.7d, seed * 13.17d + 400.9d);
                case 200:
                    return new double3(seed * 31.7d + 1400.1d, seed * 77.13d + 1100.5d, seed * 53.71d + 900.2d);
                default:
                    return new double3(seed * 61.3d + 1700.9d, seed * 19.77d + 1300.3d, seed * 83.31d + 1500.6d);
            }
        }

        private static double Smoothstep01(double t)
        {
            if (t <= 0d)
            {
                return 0d;
            }

            if (t >= 1d)
            {
                return 1d;
            }

            return t * t * (3d - (2d * t));
        }

        private static double Quintic(double t)
        {
            return t * t * t * (t * ((t * 6d) - 15d) + 10d);
        }

        private static double ValueNoise(double3 p)
        {
            // floor отдельно: дробная часть p - floor(p) всегда ∈ [0,1), а индекс
            // ячейки хешируется через long→int. Прямой (int)floor(p) ломается,
            // когда сид-оффсеты (seed·89.31 и т.п.) выходят за int.MaxValue —
            // тогда p - (int)floor(p) ~ 4e9 и Quintic взрывается в 1e298.
            double xf = math.floor(p.x);
            double yf = math.floor(p.y);
            double zf = math.floor(p.z);
            int ix = unchecked((int)(long)xf);
            int iy = unchecked((int)(long)yf);
            int iz = unchecked((int)(long)zf);
            double fx = p.x - xf;
            double fy = p.y - yf;
            double fz = p.z - zf;
            double ux = Quintic(fx);
            double uy = Quintic(fy);
            double uz = Quintic(fz);

            double c000 = LatticeValue(ix, iy, iz);
            double c100 = LatticeValue(ix + 1, iy, iz);
            double c010 = LatticeValue(ix, iy + 1, iz);
            double c110 = LatticeValue(ix + 1, iy + 1, iz);
            double c001 = LatticeValue(ix, iy, iz + 1);
            double c101 = LatticeValue(ix + 1, iy, iz + 1);
            double c011 = LatticeValue(ix, iy + 1, iz + 1);
            double c111 = LatticeValue(ix + 1, iy + 1, iz + 1);

            double x00 = c000 + (ux * (c100 - c000));
            double x10 = c010 + (ux * (c110 - c010));
            double x01 = c001 + (ux * (c101 - c001));
            double x11 = c011 + (ux * (c111 - c011));
            double y0 = x00 + (uy * (x10 - x00));
            double y1 = x01 + (uy * (x11 - x01));
            return y0 + (uz * (y1 - y0));
        }

        private static double LatticeValue(int x, int y, int z)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263) + (z * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return ((h & 0xFFFF) / 32767.5d) - 1d;
            }
        }
    }

    /// <summary>
    /// Пачка вершин тайла: направления (unit, body-fixed, double3) → форма
    /// (double) + цветовая маска (float; цвет физику не касается).
    /// Вызывает те же статики, что меряет T84.
    /// </summary>
    [BurstCompile]
    public struct TerrainTileJob : IJobParallelFor
    {
        [ReadOnly]
        public TerrainNoiseParams Params;

        [ReadOnly]
        public NativeArray<double3> Directions;

        public NativeArray<double> Heights;
        public NativeArray<float> ColorMasks;

        public void Execute(int index)
        {
            double3 direction = Directions[index];
            Heights[index] = TerrainNoise.SampleHeight(Params, direction);
            ColorMasks[index] = Params.ComputeMask ? (float)TerrainNoise.SampleColorNoise(Params, direction) : 0f;
        }
    }
}
