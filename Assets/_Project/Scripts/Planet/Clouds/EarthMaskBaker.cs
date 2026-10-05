using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Предзапекание процедурной маски облаков (EarthClusters) в 3D-текстуру.
    ///
    /// На GPU SampleCloudEarthMask() вызывается из марчинга облаков (до ~48
    /// шагов × (1 + _CldLightSteps) сэмплов) и заново для каждого пикселя
    /// земли/воды/декора в SampleCloudShadow. Каждый вызов — CloudFbm на 3
    /// октавы плюс одна CloudNoise, то есть 4 × 8 = 32 тяжёлых хеш-вычисления
    /// 3D value-noise. Поле зависит ТОЛЬКО от направления (bodyPos/|bodyPos|),
    /// поэтому его достаточно посчитать один раз и хранить в объёме.
    ///
    /// Математика — точная копия GLSL-варианта из GalilegoCloudField.hlsl,
    /// чтобы картинка после подмены не изменилась (с точностью до фильтрации).
    /// </summary>
    internal static class EarthMaskBaker
    {
        public static Texture3D BuildEarthMaskTexture(int resolution)
        {
            Texture3D tex = new Texture3D(resolution, resolution, resolution, TextureFormat.RFloat, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            int res = resolution;
            Color[] pixels = new Color[res * res * res];
            int idx = 0;
            for (int z = 0; z < res; z++)
            {
                float nz = (z + 0.5f) / res * 2f - 1f;
                for (int y = 0; y < res; y++)
                {
                    float nyy = (y + 0.5f) / res * 2f - 1f;
                    for (int x = 0; x < res; x++, idx++)
                    {
                        float nxx = (x + 0.5f) / res * 2f - 1f;
                        Vector3 dir = new Vector3(nxx, nyy, nz).normalized;
                        pixels[idx] = new Color(Sample(dir), 0f, 0f, 0f);
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            tex.name = "CldEarthMask";
            return tex;
        }

        static float Sample(Vector3 direction)
        {
            float broad = CloudFbm(direction * 7f + new Vector3(2.3f, 7.1f, 4.6f));
            float detail = CloudNoise(direction * 18f + new Vector3(6.2f, 1.4f, 8.7f));
            float field = Mathf.Clamp01((broad * 0.90f + detail * 0.10f - 0.28f) / 0.72f);
            return Smoothstep(0.415f, 0.795f, field);
        }

        static float CloudHash(Vector3 p)
        {
            p = Frac3(p * 0.3183099f + new Vector3(0.13f, 0.17f, 0.19f));
            p *= 17f;
            return Frac((p.x * p.y * p.z) * (p.x + p.y + p.z));
        }

        static float CloudNoise(Vector3 p)
        {
            Vector3 i = new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
            Vector3 f = p - i;
            f = new Vector3(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y), f.z * f.z * (3f - 2f * f.z));

            float n000 = CloudHash(i);
            float n100 = CloudHash(i + new Vector3(1f, 0f, 0f));
            float n010 = CloudHash(i + new Vector3(0f, 1f, 0f));
            float n110 = CloudHash(i + new Vector3(1f, 1f, 0f));
            float n001 = CloudHash(i + new Vector3(0f, 0f, 1f));
            float n101 = CloudHash(i + new Vector3(1f, 0f, 1f));
            float n011 = CloudHash(i + new Vector3(0f, 1f, 1f));
            float n111 = CloudHash(i + new Vector3(1f, 1f, 1f));

            float nx00 = Mathf.Lerp(n000, n100, f.x);
            float nx10 = Mathf.Lerp(n010, n110, f.x);
            float nx01 = Mathf.Lerp(n001, n101, f.x);
            float nx11 = Mathf.Lerp(n011, n111, f.x);
            float nxy0 = Mathf.Lerp(nx00, nx10, f.y);
            float nxy1 = Mathf.Lerp(nx01, nx11, f.y);
            return Mathf.Lerp(nxy0, nxy1, f.z);
        }

        static float CloudFbm(Vector3 p)
        {
            float value = 0f;
            float amplitude = 0.5f;
            for (int i = 0; i < 3; i++)
            {
                value += CloudNoise(p) * amplitude;
                p = p * 2.03f + new Vector3(1.7f, 9.2f, 3.1f);
                amplitude *= 0.5f;
            }
            return value / 0.875f;
        }

        static float Frac(float x)
        {
            return x - Mathf.Floor(x);
        }

        static Vector3 Frac3(Vector3 v)
        {
            return new Vector3(Frac(v.x), Frac(v.y), Frac(v.z));
        }

        static float Smoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
