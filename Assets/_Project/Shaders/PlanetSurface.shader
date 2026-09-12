Shader "Galilego/PlanetSurface"
{
    Properties
    {
        _WaterDeep("Water Deep", Color) = (0.02, 0.09, 0.22, 1)
        _WaterShallow("Water Shallow", Color) = (0.10, 0.32, 0.52, 1)
        _SpecularPower("Specular Power", Float) = 120.0
        _SpecularIntensity("Specular Intensity", Float) = 0.8
        _RimColor("Water Sky Rim", Color) = (0.35, 0.55, 0.85, 1)
        // _TexLow/_TexMid/_TexHigh/_TexSteep/_TexOcclusion НЕ в Properties:
        // иначе рантайм-материал получает свои дефолтные "white" текстуры,
        // которые перекрывают глобалы PlanetSurfaceRenderer (земля белела).
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "PlanetSurfaceForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoLighting.hlsl"

            // Глобально, ставит PlanetSurfaceRenderer (мировая позиция камеры).
            float3 _PlanetCameraPos;

            // Трипланарные UV текстур рельефа — camera-relative: дельты мировой
            // позиции от камеры малы и точны (floating origin), поворот в
            // тело-fixed оси даёт _TerrainWorldToBody (ставит PlanetSurfaceRenderer
            // каждый кадр). Абсолютные координаты ~1.14e6 м во float квантуются
            // шагом ~6 см (~2.5 текселя при scale 0.04) — отсюда шли «полосы».
            float4x4 _TerrainWorldToBody;
            float4 _TerrainUVPhase0; // uvX.xy: (z, y), uvY.xy: (x, z)
            float4 _TerrainUVPhase1; // uvZ.xy: (x, y)

            float4 _WaterDeep;
            float4 _WaterShallow;
            float4 _RimColor;
            float _SpecularPower;
            float _SpecularIntensity;

            // Палитра/шум per-pixel альбедо: ставит PlanetSurfaceRenderer из
            // HeightfieldTerrain + TerrainPalette (единственный источник правды).
            float _TerrainAmplitude;
            float _TerrainSeaLevel;
            float _TerrainSeed;
            float _TerrainGain;
            float _TerrainLacunarity;
            float _TerrainRockSlopeTan;
            float _TerrainRockSlopeWidth;
            float _TerrainRockHeightMin;
            float _TerrainSnowSlopeTan;
            float _ColorNoiseFrequency;
            float _ColorNoiseOctaves;
            float _ColorNoiseStrength;
            float _ColorDetailFrequency;
            float _ColorDetailOctaves;
            float _ColorDetailStrength;

            float4 _ColSand;
            float4 _ColDesert;
            float4 _ColDryGrass;
            float4 _ColGrass;
            float4 _ColForest;
            float4 _ColTundra;
            float4 _ColRock;
            float4 _ColSnow;
            float4 _ColSea;
            float4 _ColSoil;
            float4 _ColLush;

            sampler2D _TexLow;
            sampler2D _TexMid;
            sampler2D _TexHigh;
            sampler2D _TexSteep;
            sampler2D _TexOcclusion;
            float _TerrainTextureScale;
            float _TerrainUseTextures;
            float _LowMidBlendStart;
            float _LowMidBlendEnd;
            float _MidHighBlendStart;
            float _MidHighBlendEnd;
            float _SteepBlendStart;
            float _SteepBlendEnd;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float3 dirOS      : TEXCOORD1; // тел-fixed единичное направление
                float2 extra      : TEXCOORD2; // x: сырая высота (м), y: косинус уклона
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 dirOS      : TEXCOORD2;
                float2 extra      : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.dirOS = input.dirOS;
                output.extra = input.extra;
                return output;
            }

            // --- Процедурный шум (порт TerrainNoise.ValueNoise/SampleFbmEx) ------
            // Сид-оффсеты C# ~1.5e9 в double; во float столько знаков нет, поэтому
            // потоки разводим МАЛЫМИ float-safe оффсетами. Паттерн отличается от
            // вершинного варианта, но остаётся детерминированным и связным.
            float3 StreamOffset(float salt)
            {
                float s = (_TerrainSeed * 0.0001) + (salt * 13.37);
                return float3((s * 1.7) + (salt * 3.1), (s * 2.3) + 41.7, (s * 3.1) + 89.3);
            }

            float Quintic(float t)
            {
                return t * t * t * (t * ((t * 6.0) - 15.0) + 10.0);
            }

            float LatticeValue(int x, int y, int z)
            {
                // uint-арифметика: переполнение определено (wrap mod 2^32),
                // в отличие от знакового int (UB для оптимизатора).
                uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u) + ((uint)z * 2147483647u);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return ((h & 0xFFFFu) / 32767.5) - 1.0;
            }

            float ValueNoise(float3 p)
            {
                float xf = floor(p.x);
                float yf = floor(p.y);
                float zf = floor(p.z);
                int ix = (int)xf;
                int iy = (int)yf;
                int iz = (int)zf;
                float fx = p.x - xf;
                float fy = p.y - yf;
                float fz = p.z - zf;
                float ux = Quintic(fx);
                float uy = Quintic(fy);
                float uz = Quintic(fz);

                float c000 = LatticeValue(ix, iy, iz);
                float c100 = LatticeValue(ix + 1, iy, iz);
                float c010 = LatticeValue(ix, iy + 1, iz);
                float c110 = LatticeValue(ix + 1, iy + 1, iz);
                float c001 = LatticeValue(ix, iy, iz + 1);
                float c101 = LatticeValue(ix + 1, iy, iz + 1);
                float c011 = LatticeValue(ix, iy + 1, iz + 1);
                float c111 = LatticeValue(ix + 1, iy + 1, iz + 1);

                float x00 = c000 + (ux * (c100 - c000));
                float x10 = c010 + (ux * (c110 - c010));
                float x01 = c001 + (ux * (c101 - c001));
                float x11 = c011 + (ux * (c111 - c011));
                float y0 = x00 + (uy * (x10 - x00));
                float y1 = x01 + (uy * (x11 - x01));
                return y0 + (uz * (y1 - y0));
            }

            float Fbm(float3 direction, float baseFrequency, int octaves, float3 offset)
            {
                int n = octaves < 1 ? 1 : (octaves > 8 ? 8 : octaves);
                float amplitude = 1.0;
                float frequency = max(baseFrequency, 1e-6);
                float sum = 0.0;
                float norm = 0.0;
                float3 o = offset;
                for (int i = 0; i < 8; i++)
                {
                    if (i >= n)
                    {
                        break;
                    }

                    sum += amplitude * ValueNoise((direction * frequency) + o);
                    norm += amplitude;
                    amplitude *= _TerrainGain;
                    frequency *= _TerrainLacunarity;
                    o = float3(o.y + 19.19, o.z + 7.47, o.x + 3.13);
                }

                return norm > 0.0 ? sum / norm : 0.0;
            }

            // --- Палитра (порт TerrainPalette.HeightColorEx) --------------------
            float PaletteSmoothstep(float t)
            {
                t = saturate(t);
                return t * t * (3.0 - (2.0 * t));
            }

            float SlopeTanFromCos(float cosA)
            {
                if (!(cosA > 1e-6))
                {
                    return 1e6;
                }

                float sinA = sqrt(max(0.0, 1.0 - (cosA * cosA)));
                return sinA / cosA;
            }

            float3 BiomeColor(float wet)
            {
                if (wet < 0.18)
                {
                    return _ColDesert.rgb;
                }

                if (wet < 0.38)
                {
                    return lerp(_ColDesert.rgb, _ColDryGrass.rgb, (wet - 0.18) / 0.20);
                }

                if (wet < 0.58)
                {
                    return lerp(_ColDryGrass.rgb, _ColGrass.rgb, (wet - 0.38) / 0.20);
                }

                if (wet < 0.80)
                {
                    return lerp(_ColGrass.rgb, _ColForest.rgb, (wet - 0.58) / 0.22);
                }

                return _ColForest.rgb;
            }

            float3 TerrainAlbedo(float raw, float mask, float detail, float slopeTan, float lat01)
            {
                float amp = _TerrainAmplitude;
                float sea = _TerrainSeaLevel;
                if (raw <= sea + (amp * 0.001))
                {
                    return _ColSea.rgb;
                }

                float height = max(raw, sea);
                float t = (height - sea) / max(1.0, amp);
                bool maskOn = _ColorNoiseStrength != 0.0;
                if (maskOn)
                {
                    t += mask * _ColorNoiseStrength;
                }

                t = max(t, 0.0);

                float3 c;
                if (t < 0.03)
                {
                    c = _ColSand.rgb;
                }
                else
                {
                    float wet = maskOn ? saturate(0.5 + (clamp(mask, -1.0, 1.0) * 1.6)) : 0.5;
                    float3 lowland = BiomeColor(wet);
                    if (t < 0.45)
                    {
                        c = lowland;
                    }
                    else if (t < 0.70)
                    {
                        c = lerp(lowland, _ColRock.rgb, (t - 0.45) / 0.25);
                    }
                    else
                    {
                        c = lerp(_ColRock.rgb, _ColSnow.rgb, (t - 0.70) / 0.30);
                    }

                    float tundra = PaletteSmoothstep((lat01 - 0.52) / 0.22);
                    float ice = PaletteSmoothstep((lat01 - 0.70) / 0.16);
                    if (tundra > 0.0)
                    {
                        c = lerp(c, _ColTundra.rgb, tundra * 0.85);
                    }

                    if (ice > 0.0)
                    {
                        c = lerp(c, _ColSnow.rgb, ice);
                    }
                }

                // Моттлинг земли: пятна почвы / сочной зелени. Только на земле
                // выше пляжной зоны — на песке пятен быть не должно.
                if (_ColorDetailStrength != 0.0 && t >= 0.03)
                {
                    float d = clamp(detail, -1.0, 1.0);
                    if (d > 0.0)
                    {
                        c = lerp(c, _ColSoil.rgb, d * _ColorDetailStrength);
                    }
                    else
                    {
                        c = lerp(c, _ColLush.rgb, (-d) * _ColorDetailStrength * 0.6);
                    }
                }

                // Rock-override: скала только на крутых И достаточно высоких
                // склонах (крупные горы). Порог по высоте отсекает пляж, дюны
                // и низменности, где скалы не должны появляться.
                if (_TerrainRockSlopeTan > 0.0 && slopeTan >= _TerrainRockSlopeTan
                    && t >= _TerrainRockHeightMin)
                {
                    float w = min(1.0, (slopeTan - _TerrainRockSlopeTan) / max(1e-9, _TerrainRockSlopeWidth));
                    w = w * w * (3.0 - (2.0 * w));
                    c = lerp(c, _ColRock.rgb, w);
                }

                // Снежная полоса на крутых склонах — скала вместо снега.
                float snowT = (height - sea) / max(1.0, amp);
                if (snowT >= 0.55 && _TerrainSnowSlopeTan > 0.0 && slopeTan > _TerrainSnowSlopeTan)
                {
                    c = _ColRock.rgb;
                }

                return c;
            }

            float3 Triplanar(sampler2D tex, float2 uvX, float2 uvY, float2 uvZ, float3 weights)
            {
                return (tex2D(tex, uvX).rgb * weights.x)
                    + (tex2D(tex, uvY).rgb * weights.y)
                    + (tex2D(tex, uvZ).rgb * weights.z);
            }

            // Альбедо из текстур рельефа: низ/середина/верх по высоте над
            // морем, скалы по склону, мягкий AO из occlusion. Биомный тинт
            // (процедурная палитра) сохраняет климатические зоны читаемыми.
            // Координаты — дельта от камеры, повёрнутая в тело-fixed оси:
            // малые числа без квантования ~6 см (см. _TerrainWorldToBody).
            float3 TerrainTextureAlbedo(float3 positionWS, float raw, float slopeTan, float3 normalObject, float3 biome)
            {
                float3 relBody = mul((float3x3)_TerrainWorldToBody, positionWS - _PlanetCameraPos);
                float2 uvX = (relBody.zy * _TerrainTextureScale) + _TerrainUVPhase0.xy;
                float2 uvY = (relBody.xz * _TerrainTextureScale) + _TerrainUVPhase0.zw;
                float2 uvZ = (relBody.xy * _TerrainTextureScale) + _TerrainUVPhase1.xy;

                float3 tri = abs(normalObject);
                tri = tri * tri * tri * tri;
                tri /= max(1e-5, tri.x + tri.y + tri.z);

                float altitude = max(raw - _TerrainSeaLevel, 0.0);
                float lowW = 1.0 - smoothstep(_LowMidBlendStart, _LowMidBlendEnd, altitude);
                float highW = smoothstep(_MidHighBlendStart, _MidHighBlendEnd, altitude);
                float midW = max(0.0, 1.0 - lowW - highW);

                float3 c = (Triplanar(_TexLow, uvX, uvY, uvZ, tri) * lowW)
                    + (Triplanar(_TexMid, uvX, uvY, uvZ, tri) * midW)
                    + (Triplanar(_TexHigh, uvX, uvY, uvZ, tri) * highW);
                c = lerp(c, Triplanar(_TexSteep, uvX, uvY, uvZ, tri), smoothstep(_SteepBlendStart, _SteepBlendEnd, slopeTan));

                float occl = dot(Triplanar(_TexOcclusion, uvX, uvY, uvZ, tri), float3(0.3333, 0.3333, 0.3333));
                c *= lerp(1.0, saturate(occl * 1.3), 0.65);

                c *= lerp(float3(1.0, 1.0, 1.0), saturate(biome * 1.9), 0.45);
                return c;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 sunDir = normalize(_TerrainSunDir);
                float3 viewDir = normalize(_PlanetCameraPos - input.positionWS);
                float ndl = saturate(dot(normal, sunDir));

                // Тень HDRP (PCSS/PCF) от деревьев/камней/рельефа; гасит только
                // солнечный член — ambient остаётся, теневые зоны не чёрные.
                float shadow = GalilegoSunShadow(input.positionCS.xy, input.positionWS, normal, sunDir);

                // Единый световой член, пофрагментный: ночная засветка (звёзды) +
                // небесная засветка и солнце по нормали к НЕЙ. Ночью ndl=0 →
                // остаётся только _NightAmbient (≈0) → поверхность почти черна.
                // Сжимаем САМ параметр солнца (а не итоговый свет): тогда при любом
                // значении _TerrainSun сохраняется затенение по нормали (рельеф
                // читается), но яркость не улетает в белый.
                float sun = _TerrainSun / (1.0 + _TerrainSun);
                float light = _NightAmbient + (_SkyAmbient * ndl) + (sun * ndl * shadow);
                float3 lightTerm = float3(light, light, light);

                // --- Per-pixel альбедо -----------------------------------------
                float3 dir = normalize(input.dirOS);
                float raw = input.extra.x;
                float slopeCos = clamp(input.extra.y, -1.0, 1.0);
                float slopeTan = SlopeTanFromCos(slopeCos);
                float lat01 = saturate(abs(asin(clamp(dir.z, -1.0, 1.0))) / 1.5707963267948966);

                // След пикселя в решётке шума: гасим деталь, когда она мельче
                // пикселя (иначе высокочастотный шум мерцает/алиасится вдали).
                float angular = max(max(fwidth(dir.x), fwidth(dir.y)), fwidth(dir.z));

                float mask = 0.0;
                if (_ColorNoiseStrength != 0.0 && _ColorNoiseFrequency > 0.0)
                {
                    mask = Fbm(dir, _ColorNoiseFrequency, (int)_ColorNoiseOctaves, StreamOffset(4.0));
                }

                float detail = 0.0;
                if (_ColorDetailStrength != 0.0 && _ColorDetailFrequency > 0.0)
                {
                    detail = Fbm(dir, _ColorDetailFrequency, (int)_ColorDetailOctaves, StreamOffset(7.0));
                    float footprint = angular * _ColorDetailFrequency;
                    detail *= saturate(1.0 - (footprint * 0.7));
                }

                float3 albedo = TerrainAlbedo(raw, mask, detail, slopeTan, lat01);

                // Текстуры рельефа вместо процедурного альбедо —
                // только на суше: море остаётся процедурным/водным.
                float isWater = raw <= _TerrainSeaLevel + (_TerrainAmplitude * 0.001) ? 1.0 : 0.0;
                if (_TerrainUseTextures > 0.5 && isWater < 0.5)
                {
                    float3 normalObject = normalize(TransformWorldToObjectNormal(normal));
                    albedo = TerrainTextureAlbedo(input.positionWS, raw, slopeTan, normalObject, albedo);
                }

                // Суша.
                float3 land = albedo * lightTerm;

                // Вода: глубина из сырой высоты (0 — мелководье, 1 — глубина),
                // процедурная рябь ломает зеркальную нормаль, блик солнца —
                // только на освещённой стороне.
                float waterDepth = saturate((_TerrainSeaLevel - raw) / max(1.0, _TerrainAmplitude));
                float3 n = normal
                    + (float3(
                        sin(dot(input.positionWS, float3(0.31, 0.17, 0.23))),
                        0.0,
                        sin(dot(input.positionWS, float3(-0.19, 0.29, 0.13)))) * 0.035);
                n = normalize(n);
                float fresnel = pow(1.0 - saturate(dot(n, viewDir)), 5.0);
                float3 halfVec = normalize(sunDir + viewDir);
                float spec = pow(saturate(dot(n, halfVec)), max(1.0, _SpecularPower)) * _SpecularIntensity;
                float3 waterBase = lerp(_WaterShallow.rgb, _WaterDeep.rgb, waterDepth);
                float3 water = (waterBase * lightTerm)
                    + (_RimColor.rgb * fresnel * lightTerm)
                    + (spec * _TerrainSun * shadow);

                float3 color = lerp(land, water, isWater);
                return float4(color, 1.0);
            }
            ENDHLSL
        }

        // Глубина для depth-prepass HDRP. Без этого пасса рельеф не попадает
        // в depth pyramid, которую читают прозрачные шейдеры (атмосфера,
        // звёзды, диск солнца): их IsSky() считает пиксели гор небом, и небо
        // рисуется сквозь рельеф. HDRP требует DepthForwardOnly у forward-
        // материалов (см. HDRenderPipeline.RenderGraph.cs:952-956).
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
