using System;
using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// РџСЂРѕС†РµРґСѓСЂРЅР°СЏ РїРѕРІРµСЂС…РЅРѕСЃС‚СЊ Р·РІРµР·РґС‹: СЃРѕР»РЅРµС‡РЅР°СЏ РіСЂР°РЅСѓР»СЏС†РёСЏ вЂ” РєРѕРЅРІРµРєС‚РёРІРЅС‹Рµ
    /// СЏС‡РµР№РєРё (СЏСЂРєРёРµ В«Р·С‘СЂРЅР°В» ~1000 РєРј Сѓ РЎРѕР»РЅС†Р°) СЃ С‚С‘РјРЅС‹РјРё РјРµР¶РіСЂР°РЅСѓР»СЊРЅС‹РјРё
    /// РґРѕСЂРѕР¶РєР°РјРё Рё СЂРµРґРєРёРјРё РєСЂСѓРїРЅС‹РјРё РїСЏС‚РЅР°РјРё. Р‘РµСЃС€РѕРІРЅРѕ: С€СѓРј СЃС‡РёС‚Р°РµС‚СЃСЏ РЅР°
    /// РўР•Р›Р•-FIXED РµРґРёРЅРёС‡РЅРѕРј РЅР°РїСЂР°РІР»РµРЅРёРё (РЅРµ РЅР° UV вЂ” Р±РµР· С€РІРѕРІ Рё РїРѕР»СЋСЃРЅС‹С…
    /// РІС‹СЂРѕР¶РґРµРЅРёР№; UV-СЃС„РµСЂР° СЂР°РІРЅРѕРїСЂРѕРјРµР¶СѓС‚РѕС‡РЅР°СЏ в†’ Р»Р°С‚/Р»РѕРЅ в†’ РЅР°РїСЂР°РІР»РµРЅРёРµ).
    /// Worley F1 РґР°С‘С‚ СЏС‡РµРёСЃС‚СѓСЋ СЃС‚СЂСѓРєС‚СѓСЂСѓ РіСЂР°РЅСѓР», fBm вЂ” РєСЂСѓРїРЅСѓСЋ РїСЏС‚РЅРёСЃС‚РѕСЃС‚СЊ
    /// (С„Р°РєРµР»С‹/РїСЏС‚РЅР°). Р¦РІРµС‚РѕРІР°СЏ СЂР°СЃРєР»Р°РґРєР°: СЏРґСЂРѕ РіСЂР°РЅСѓР»С‹ вЂ” С†РІРµС‚ Р·РІРµР·РґС‹,
    /// РґРѕСЂРѕР¶РєРё С‚РµРјРЅРµРµ Рё В«С…РѕР»РѕРґРЅРµРµВ» (РєСЂР°СЃРЅРµРµ вЂ” РїСЂРёРіР»СѓС€Р°РµРј G/B СЃРёР»СЊРЅРµРµ R).
    /// Granulation(dir, seed) вЂ” pure, С‚РµСЃС‚РёСЂСѓРµС‚СЃСЏ РЅР° СЃС‚РµРЅРґРµ (T81).
    /// </summary>
    public static class StarSurfaceTexture
    {

        /// <summary>
        /// Р“СЂР°РЅСѓР»СЏС†РёСЏ С‚РѕС‡РєРё РїРѕРІРµСЂС…РЅРѕСЃС‚Рё: 0 вЂ” С‚С‘РјРЅР°СЏ РґРѕСЂРѕР¶РєР°, 1 вЂ” СЏРґСЂРѕ РіСЂР°РЅСѓР»С‹.
        /// dir вЂ” РµРґРёРЅРёС‡РЅРѕРµ С‚РµР»-fixed РЅР°РїСЂР°РІР»РµРЅРёРµ. Р”РµС‚РµСЂРјРёРЅРёСЂРѕРІР°РЅ seed'РѕРј.
        /// </summary>
        public static double Granulation(Vector3d direction, int seed)
        {
            // Р“СЂР°РЅСѓР»С‹: С‡Р°СЃС‚РѕС‚Р° в‰€ 60 РЅР° РµРґРёРЅРёС‡РЅС‹Р№ РІРµРєС‚РѕСЂ (~СЏС‡РµР№РєР° 1/60 СЃС„РµСЂС‹ вЂ”
            // РјР°СЃС€С‚Р°Р± РїРѕСЂСЏРґРєР° СЃРѕС‚РµРЅ РєРј РґР»СЏ Р·РІРµР·РґС‹ СЂР°РґРёСѓСЃРѕРј 1e8 Рј).
            double cells = WorleyF1(direction * 60d, seed);
            // РљСЂСѓРїРЅР°СЏ РїСЏС‚РЅРёСЃС‚РѕСЃС‚СЊ (СЃСѓРїРµСЂРіСЂР°РЅСѓР»СЏС†РёСЏ/С„Р°РєРµР»С‹): С‡Р°СЃС‚РѕС‚Р° 4.
            double mottle = ValueFbm(direction * 4d, seed * 7919);
            double spots = ValueFbm(direction * 2.5d, seed * 104729);

            // РЇРґСЂРѕ/РґРѕСЂРѕР¶РєР°: F1 РЅРѕСЂРјРёСЂРѕРІР°РЅ ~[0,1]; СЏСЂРєРёРµ С†РµРЅС‚СЂС‹ СЏС‡РµРµРє.
            double granule = 1d - Math.Min(1d, cells * 1.35d);
            granule = Math.Pow(Math.Max(0d, granule), 1.2d);

            double brightness = (0.35d + (0.65d * granule)) * (0.75d + (0.5d * mottle));
            // Р РµРґРєРёРµ С‚С‘РјРЅС‹Рµ РїСЏС‚РЅР°: РЅРёР·РєРѕС‡Р°СЃС‚РѕС‚РЅС‹Р№ С€СѓРј РЅРёР¶Рµ РїРѕСЂРѕРіР°.
            if (spots < 0.32d)
            {
                brightness *= 0.35d + (1.6d * spots);
            }

            return Math.Max(0d, Math.Min(1d, brightness));
        }

        /// <summary>
        /// РўРµРєСЃС‚СѓСЂР° РїРѕРІРµСЂС…РЅРѕСЃС‚Рё РґР»СЏ СЃС„РµСЂС‹ (СЂР°РІРЅРѕРїСЂРѕРјРµР¶СѓС‚РѕС‡РЅР°СЏ СЂР°Р·РІС‘СЂС‚РєР°):
        /// РєР°Р¶РґС‹Р№ С‚РµРєРµР»СЊ в†’ Р»Р°С‚/Р»РѕРЅ в†’ РЅР°РїСЂР°РІР»РµРЅРёРµ в†’ Granulation. Р‘Р°Р·РѕРІС‹Р№ С†РІРµС‚ вЂ”
        /// Р»РёРЅРµР№РЅС‹Р№ sRGB Р·РІРµР·РґС‹; РґРѕСЂРѕР¶РєРё В«С…РѕР»РѕРґРЅРµРµВ» (G/B РґР°РІСЏС‚СЃСЏ СЃРёР»СЊРЅРµРµ R).
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
                    // Р”РѕСЂРѕР¶РєРё С‚РµРјРЅРµРµ Рё РєСЂР°СЃРЅРµРµ: РїСЂРёРіР»СѓС€РµРЅРёРµ G/B СЃРёР»СЊРЅРµРµ R.
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

        /// <summary>Worley F1: СЂР°СЃСЃС‚РѕСЏРЅРёРµ РґРѕ Р±Р»РёР¶Р°Р№С€РµР№ С‚РѕС‡РєРё СЂРµС€С‘С‚РєРё (СЃРѕСЃРµРґСЃС‚РІРѕ 3Г—3Г—3), ~[0,1].</summary>
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

        /// <summary>РўРѕС‡РєР° РІРЅСѓС‚СЂРё СЏС‡РµР№РєРё СЂРµС€С‘С‚РєРё РёР· С…РµС€Р° вЂ” [0,1]Ві.</summary>
        private static Vector3d LatticePoint(int x, int y, int z, int seed)
        {
            return new Vector3d(Hash01(x, y, z, seed), Hash01(y, z, x, seed * 3), Hash01(z, x, y, seed * 7));
        }

        /// <summary>fBm value-noise (4 РѕРєС‚Р°РІС‹), ~[0,1].</summary>
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

        /// <summary>Р¦РµР»РѕС‡РёСЃР»РµРЅРЅС‹Р№ С…РµС€ СЂРµС€С‘С‚РєРё в†’ [0,1]. Р”РµС‚РµСЂРјРёРЅРёСЂРѕРІР°РЅ Р±РёС‚-РІ-Р±РёС‚.</summary>
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
