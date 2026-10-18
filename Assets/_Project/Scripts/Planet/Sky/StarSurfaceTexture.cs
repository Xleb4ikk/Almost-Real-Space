using System;
using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Procedural stellar surface: solar granulation — convective cells (bright
    /// "grains" ~1000 km on the Sun) with dark intergranular lanes and rare large
    /// spots. Seamless: the noise is evaluated on the UNIT body-fixed direction
    /// (not on UVs — no seams or polar degeneracies; the UV sphere is uniformly
    /// spaced → lat/lon → direction).
    /// Worley F1 gives the cellular granulation structure, fBm the large mottling
    /// (faculae/spots). Color mapping: granule cores get the star color, lanes are
    /// darker and "cooler" (redder — G/B are suppressed more than R).
    /// Granulation(dir, seed) — pure, tested on the harness (T81).
    /// </summary>
    public static class StarSurfaceTexture
    {

        /// <summary>
        /// Granulation at a surface point: 0 — dark lane, 1 — granule core.
        /// dir is a unit body-fixed direction. Deterministic in seed.
        /// </summary>
        public static double Granulation(Vector3d direction, int seed)
        {
            // Granules: frequency ≈ 60 per unit vector (a cell is ~1/60 of the
            // sphere — a scale of hundreds of km for a star of radius 1e8 m).
            double cells = WorleyF1(direction * 60d, seed);
            // Large mottling (supergranulation/faculae): frequency 4.
            double mottle = ValueFbm(direction * 4d, seed * 7919);
            double spots = ValueFbm(direction * 2.5d, seed * 104729);

            // Core/lane: F1 normalized to ~[0,1]; bright cell centers.
            double granule = 1d - Math.Min(1d, cells * 1.35d);
            granule = Math.Pow(Math.Max(0d, granule), 1.2d);

            double brightness = (0.35d + (0.65d * granule)) * (0.75d + (0.5d * mottle));
            // Rare dark spots: low-frequency noise below a threshold.
            if (spots < 0.32d)
            {
                brightness *= 0.35d + (1.6d * spots);
            }

            return Math.Max(0d, Math.Min(1d, brightness));
        }

        /// <summary>
        /// Surface texture for a sphere (equirectangular layout): each texel →
        /// lat/lon → direction → Granulation. The base color is the star's linear
        /// sRGB; lanes are "cooler" (G/B suppressed more than R).
        /// </summary>
        public static Texture2D GenerateTexture(int width, int height, int seed, Vector3d baseLinearColor)
        {
            var texture = new Texture2D(width, height);
            var pixels = new Color[width * height];
            double toRad = Math.PI / 180d;
            for (int row = 0; row < height; row++)
            {
                double lat = (90d - (180d * ((row + 0.5d) / height))) * toRad;
                for (int col = 0; col < width; col++)
                {
                    double lon = ((360d * ((col + 0.5d) / width)) - 180d) * toRad;
                    double cosLat = Math.Cos(lat);
                    var direction = new Vector3d(cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
                    double t = Granulation(direction, seed);
                    // Lanes are darker and redder: G/B are suppressed more than R.
                    double g = baseLinearColor.Y * (0.55d + (0.45d * t));
                    double b = baseLinearColor.Z * (0.45d + (0.55d * t));
                    pixels[(row * width) + col] = new Color(
                        (float)baseLinearColor.X,
                        (float)Math.Min(1d, g),
                        (float)Math.Min(1d, b),
                        1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Worley F1: distance to the nearest lattice point (3×3×3 neighborhood), ~[0,1].</summary>
        private static double WorleyF1(Vector3d p, int seed)
        {
            int ix = (int)Math.Floor(p.X);
            int iy = (int)Math.Floor(p.Y);
            int iz = (int)Math.Floor(p.Z);
            double best = double.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        Vector3d cellPoint = LatticePoint(ix + dx, iy + dy, iz + dz, seed);
                        Vector3d diff = (new Vector3d(ix + dx, iy + dy, iz + dz) + cellPoint) - p;
                        double d = diff.SqrMagnitude;
                        if (d < best)
                        {
                            best = d;
                        }
                    }
                }
            }

            return Math.Sqrt(best) * 0.7d;
        }

        /// <summary>Point inside a lattice cell from the hash — [0,1]³.</summary>
        private static Vector3d LatticePoint(int x, int y, int z, int seed)
        {
            return new Vector3d(Hash01(x, y, z, seed), Hash01(y, z, x, seed * 3), Hash01(z, x, y, seed * 7));
        }

        /// <summary>fBm value noise (4 octaves), ~[0,1].</summary>
        private static double ValueFbm(Vector3d p, int seed)
        {
            double amplitude = 1d;
            double sum = 0d;
            double norm = 0d;
            for (int o = 0; o < 4; o++)
            {
                sum += amplitude * ValueNoise(p, seed + (o * 131));
                norm += amplitude;
                amplitude *= 0.5d;
                p = p * 2.03d + new Vector3d(11.1d, 7.7d, 3.3d);
            }

            return (sum / norm) * 0.5d + 0.5d;
        }

        private static double ValueNoise(Vector3d p, int seed)
        {
            int ix = (int)Math.Floor(p.X);
            int iy = (int)Math.Floor(p.Y);
            int iz = (int)Math.Floor(p.Z);
            double fx = p.X - ix;
            double fy = p.Y - iy;
            double fz = p.Z - iz;
            double ux = Quintic(fx);
            double uy = Quintic(fy);
            double uz = Quintic(fz);
            double c000 = Hash01(ix, iy, iz, seed);
            double c100 = Hash01(ix + 1, iy, iz, seed);
            double c010 = Hash01(ix, iy + 1, iz, seed);
            double c110 = Hash01(ix + 1, iy + 1, iz, seed);
            double c001 = Hash01(ix, iy, iz + 1, seed);
            double c101 = Hash01(ix + 1, iy, iz + 1, seed);
            double c011 = Hash01(ix, iy + 1, iz + 1, seed);
            double c111 = Hash01(ix + 1, iy + 1, iz + 1, seed);
            double x00 = c000 + (ux * (c100 - c000));
            double x10 = c010 + (ux * (c110 - c010));
            double x01 = c001 + (ux * (c101 - c001));
            double x11 = c011 + (ux * (c111 - c011));
            double y0 = x00 + (uy * (x10 - x00));
            double y1 = x01 + (uy * (x11 - x01));
            return y0 + (uz * (y1 - y0));
        }

        private static double Quintic(double t)
        {
            return t * t * t * (t * ((t * 6d) - 15d) + 10d);
        }

        /// <summary>Integer lattice hash → [0,1]. Bit-for-bit deterministic.</summary>
        private static double Hash01(int x, int y, int z, int seed)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263) + (z * 2147483647) + (seed * 668265263);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215d;
            }
        }
    }
}
