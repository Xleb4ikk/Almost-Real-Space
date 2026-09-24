Shader "Galilego/PlanetClouds"
{
    // Объёмные облака планеты: камера-центрированный купол (PlanetCloudsView),
    // аналитическое пересечение луча с ДВУМЯ сферами облачного слоя
    // (BottomRadius/TopRadius), raymarch плотности из 3D Perlin-Worley
    // (форма) + Worley (эрозия краёв) + весь домен шума в МИРОВЫХ декартовых
    // координатах относительно центра планеты — без lat-long/UV (см.
    // PlanetCloudsView.cs, почему: развёртка сферы на радиусе ~1000+ км даёт
    // анизотропию у полюсов и полосы вдоль параллелей).
    //
    // Свет: Beer-Lambert экстинкция по основному лучу + короткий (4–6 шагов)
    // марш к солнцу для самозатенения, фаза Henyey-Greenstein (форвард-
    // рассеяние, ореол вокруг солнца), эффект пудры (тёмные края к солнцу
    // в глубине облака).
    //
    // Композит: ОБЫЧНЫЙ premultiplied alpha-бленд (Blend One OneMinusSrcAlpha)
    // поверх уже нарисованного кадра — ЭТО ОТЛИЧАЕТСЯ от PlanetAtmosphere,
    // который сам себя компонует через _ColorPyramidTexture (Blend One Zero).
    // Если бы облака делали то же самое ПОСЛЕ атмосферы, они читали бы
    // ДОСЕЙ-transparent снимок пирамиды (атмосфера ещё не успела туда
    // попасть) и стирали бы небо под собой. Обычный альфа-бленд корректно
    // ложится поверх чего угодно уже в буфере (терраформ, атмосфера, вода) —
    // поэтому Queue выставлен ВЫШЕ атмосферы (см. ниже), чтобы рисоваться
    // после неё.
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+50" }

        Pass
        {
            Name "PlanetCloudsForward"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoCloudField.hlsl"

            float3 _CldCameraWS;
            float3 _CldCameraForwardWS;
            float3 _CldSunColor;

            // --- Domain шума (мировые координаты относительно центра тела) ---
            float3 _CldWindOffset;
            float3 _CldSmallWindOffset;
            float3 _CldLargeWindOffset;
             float3 _CldDetailWindOffset;
             float _CldSmallShapeFreq;
            float _CldMediumShapeFreq;
            float _CldLargeShapeFreq;
            float _CldDetailFreq;
            float _CldShapeTexelWorldSize;
            float _CldWeatherTexelWorldSize;
             float _CldSizeVariation;
             float _CldShapeWarp;

            // --- Форма/покрытие ---
            float _CldDetailErosion;

            // --- Оптика ---
            float _CldScatterAlbedo;
             float _CldPhaseG;
             float _CldPowderStrength;
             float _CldMultipleScattering;
             float _CldIntensity;
            float4 _CldAmbientTint;

            // --- Raymarch ---
            float _CldStepCount;
            float _CldLightSteps;
            float _CldEarlyExitT;
            float _CldDebugMode;

            TEXTURE3D(_CldShapeTex);
            SAMPLER(sampler_CldShapeTex);
            TEXTURE3D(_CldDetailTex);
            SAMPLER(sampler_CldDetailTex);
            TEXTURECUBE(_CldWeatherTex);
            SAMPLER(sampler_CldWeatherTex);

            #define CLD_PI 3.14159265358979

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.dir = input.positionOS; // купол единичного радиуса, центр = камера
                return output;
            }

            // c + (x-a)*(d-c)/(b-a), без внутреннего saturate — вызывающий код
            // сатурейтит результат там, где это физически осмысленно.
            float CloudRemap(float x, float a, float b, float c, float d)
            {
                return c + (((x - a) / max(1e-5, b - a)) * (d - c));
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 / (4.0 * CLD_PI)) * (1.0 - g2) / pow(max(1e-4, 1.0 + g2 - (2.0 * g * cosTheta)), 1.5);
            }

            float4 SampleWeather(float3 posRel, float weatherLod)
            {
                float3 bodyPos = mul(_CldWorldToBody, float4(posRel, 1.0)).xyz;
                float3 direction = normalize(bodyPos);
                float4 weather = SAMPLE_TEXTURECUBE_LOD(
                    _CldWeatherTex,
                    sampler_CldWeatherTex,
                    direction,
                    weatherLod);
                float coverageScale = _CldNoiseStyle < 0.5 ? 2.0 : 1.5;
                float coverage = saturate(weather.r * _CldCoverage * coverageScale);
                return float4(coverage, weather.g, weather.b, weather.a);
            }

            float MakeCloudDensity(float shape, float coverage, float heightGradient)
            {
                float normalizedShape = saturate((shape - 0.1) / 0.6);
                float safeCoverage = max(0.001, coverage);
                float threshold = 1.0 - safeCoverage;
                float density = saturate((normalizedShape - threshold) / safeCoverage);
                return density * safeCoverage * heightGradient;
            }

            float MakeEarthCloudDensity(float earthMask, float heightGradient)
            {
                float earthCoverage = saturate(_CldCoverage * 1.35);
                return smoothstep(0.08, 0.78, earthMask) * earthCoverage * heightGradient;
            }

            float SampleCloudDensity(float3 posRel, float lod, float weatherLod, float earthMask)
            {
                float r = length(posRel);
                float heightFrac = saturate((r - _CldBottomRadius) / max(1.0, _CldTopRadius - _CldBottomRadius));
                float bottomFeather = smoothstep(0.0, max(0.001, _CldBottomFeather), heightFrac);
                float topFeather = 1.0 - smoothstep(1.0 - max(0.001, _CldTopFeather), 1.0, heightFrac);
                float heightGradient = bottomFeather * topFeather;
                if (heightGradient <= 0.0)
                {
                    return 0.0;
                }

                if (_CldNoiseStyle > 0.5)
                {
                    earthMask = SampleCloudEarthMask(posRel);
                    return MakeEarthCloudDensity(earthMask, heightGradient);
                }

                float4 weather = SampleWeather(posRel, weatherLod);
                if (_CldNoiseStyle < 0.5 && weather.x <= 0.001)
                {
                    return 0.0;
                }

                float clearFactor = _CldNoiseStyle > 0.5 ? 1.0 : saturate(1.0 - weather.z);
                float3 bodyPos = mul(_CldWorldToBody, float4(posRel, 1.0)).xyz;
                float3 warp = (weather.rgb - 0.5) * _CldShapeWarp;
                float3 samplePos = bodyPos + warp;
                float shapeLod = lod;

                float4 smallSample = SAMPLE_TEXTURE3D_LOD(
                    _CldShapeTex, sampler_CldShapeTex,
                    (samplePos + _CldSmallWindOffset) * _CldSmallShapeFreq, shapeLod);
                float4 mediumSample = SAMPLE_TEXTURE3D_LOD(
                    _CldShapeTex, sampler_CldShapeTex,
                    (samplePos + _CldWindOffset) * _CldMediumShapeFreq, shapeLod);
                float4 largeSample = SAMPLE_TEXTURE3D_LOD(
                    _CldShapeTex, sampler_CldShapeTex,
                    (samplePos + _CldLargeWindOffset) * _CldLargeShapeFreq, shapeLod);

                float sizeT = saturate(0.5 + ((weather.y - 0.5) * (1.0 + (_CldSizeVariation * 2.0))));
                float smallWeight = 1.0 - smoothstep(0.18, 0.42, sizeT);
                float mediumWeight = smoothstep(0.18, 0.42, sizeT) *
                    (1.0 - smoothstep(0.62, 0.86, sizeT));
                float largeWeight = smoothstep(0.68, 0.92, sizeT);

                float density;
                if (_CldNoiseStyle < 0.5)
                {
                    float smallDensity = MakeCloudDensity(smallSample.g, weather.x, heightGradient);
                    float mediumDensity = MakeCloudDensity(mediumSample.r, weather.x, heightGradient);
                    float largeDensity = MakeCloudDensity(largeSample.b, weather.x * (0.85 + weather.w * 0.2), heightGradient);
                    density = max(smallDensity * smallWeight, max(mediumDensity * mediumWeight, largeDensity * largeWeight));
                }
                else
                {
                    density = MakeEarthCloudDensity(
                        earthMask,
                        heightGradient);
                }
                if (density <= 0.0)
                {
                    return 0.0;
                }

                 if (_CldNoiseStyle > 0.5)
                 {
                     return density;
                 }

                 float3 detailSample = SAMPLE_TEXTURE3D_LOD(
                     _CldDetailTex, sampler_CldDetailTex,
                     (samplePos + _CldDetailWindOffset) * _CldDetailFreq, shapeLod).rgb;
                 float detailFbm = dot(detailSample, float3(0.55, 0.3, 0.15));
                 float legacyErodeWeight = lerp(0.15, 1.0, 1.0 - heightGradient) * _CldDetailErosion;
                 return saturate(CloudRemap(density, legacyErodeWeight * detailFbm, 1.0, 0.0, 1.0)) * clearFactor;
            }

            // Короткий марш к солнцу (самозатенение). Растущий шаг —
            // дешёвая замена honest cone sampling.
            float LightMarch(float3 posRel, float3 sunDir, float baseStep, float lod, float weatherLod, float earthMask)
            {
                float totalDensity = 0.0;
                float stepSize = max(1.0, baseStep);
                float t = stepSize * 0.5;
                int steps = clamp((int)_CldLightSteps, 2, 8);

                for (int i = 0; i < 8; i++)
                {
                    if (i >= steps)
                    {
                        break;
                    }

                    float3 p = posRel + (sunDir * t);
                    totalDensity += SampleCloudDensity(p, lod + 1.0, weatherLod, earthMask) * stepSize;
                    t += stepSize;
                    stepSize *= 1.7;
                }

                return totalDensity;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 rd = normalize(input.dir);
                float3 ro = _CldCameraWS - _CldBodyCenterWS;
                float3 sunDir = normalize(_CldSunDirWS);

                float top0;
                float top1;
                bool hitTop = CloudRaySphere(ro, rd, _CldTopRadius, top0, top1);
                if (!hitTop || top1 < 0.0)
                {
                    discard;
                }

                float bot0;
                float bot1;
                bool hitBot = CloudRaySphere(ro, rd, _CldBottomRadius, bot0, bot1);

                float r0 = length(ro);
                float marchStart;
                float marchEnd;
                if (r0 > _CldTopRadius)
                {
                    // Камера над слоем (космос/орбита): входим сверху.
                    marchStart = max(top0, 0.0);
                    marchEnd = (hitBot && bot0 > marchStart) ? bot0 : top1;
                }
                else if (r0 >= _CldBottomRadius)
                {
                    // Камера внутри слоя (полёт сквозь облака).
                    marchStart = 0.0;
                    marchEnd = (hitBot && bot0 > 0.0) ? bot0 : top1;
                }
                else
                {
                    // Камера под слоем (земля/низкий полёт под облачностью).
                    marchStart = hitBot ? max(bot1, 0.0) : max(top0, 0.0);
                    marchEnd = top1;
                }

                if (marchEnd <= marchStart)
                {
                    discard;
                }

                // Глубина сцены/планета-окклюдер — тот же приём, что у
                // PlanetAtmosphere (LoadCameraDepth/IsSky, HDRP depth-atlas).
                uint2 pixel = uint2(input.positionCS.xy);
                float cosFwd = dot(rd, _CldCameraForwardWS);
                float tDepth = 1e30;
                if (cosFwd > 1e-4)
                {
                    float eyeDepth = LinearEyeDepth(LoadCameraDepth(pixel), _ZBufferParams);
                    if (!IsSky(pixel) && eyeDepth > 0.0)
                    {
                        tDepth = eyeDepth / cosFwd;
                    }
                }

                float planet0;
                float planet1;
                bool hitPlanet = CloudRaySphere(ro, rd, _CldPlanetRadius, planet0, planet1) && planet1 > 0.0;
                float tPlanet = hitPlanet ? max(planet0, 0.0) : 1e30;

                marchEnd = min(marchEnd, tDepth);
                marchEnd = min(marchEnd, tPlanet);
                if (marchEnd <= marchStart)
                {
                    discard;
                }

                int steps = clamp((int)_CldStepCount, 8, 128);
                float ds = (marchEnd - marchStart) / (float)steps;
                float pixelWorldSize = max(length(fwidth(rd)) * max(marchStart + ds, 1.0), ds);
                 float lod = clamp(log2(max(pixelWorldSize / max(1e-3, _CldShapeTexelWorldSize), 1.0)), 0.0, 6.0);
                 float weatherLod = clamp(log2(max(pixelWorldSize / max(1e-3, _CldWeatherTexelWorldSize), 1.0)), 0.0, 5.0);
                 float earthMask = _CldNoiseStyle > 0.5
                     ? SampleCloudEarthMask(ro + (rd * marchStart))
                     : 0.0;

                 float jitter = frac(sin(dot(input.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453);

                 float cosSunView = dot(rd, sunDir);
                 float phase = HenyeyGreenstein(cosSunView, clamp(_CldPhaseG, 0.0, 0.95));
                 float fill = lerp(0.04, 0.12, saturate(_CldMultipleScattering));
                 float fillScatter = lerp(0.05, 0.16, saturate(_CldMultipleScattering));
                 float3 neutralScatterColor = lerp(float3(1.0, 1.0, 1.0), _CldAmbientTint.rgb, 0.25);
                 float3 ambient = (_CldAmbientTint.rgb * (fill + (_CldSkyAmbient * 0.35))) + (_CldSunColor * 0.02);

                float sigmaExt = max(1e-5, _CldExtinction);
                float lightStepBase = max(1.0, (_CldTopRadius - _CldBottomRadius) / max(1.0, _CldLightSteps));

                float transmittance = 1.0;
                float3 inScatter = 0.0;
                float debugSteps = 0.0;
                float4 debugWeather = SampleWeather(ro + (rd * marchStart), weatherLod);
                float debugCoverage = debugWeather.x;
                float debugClear = debugWeather.z;
                float debugSize = debugWeather.y;

                [loop]
                for (int i = 0; i < 128; i++)
                {
                    if (i >= steps)
                    {
                        break;
                    }

                    float t = marchStart + ((i + jitter) * ds);
                    float3 p = ro + (rd * t);

                     float density = SampleCloudDensity(p, lod, weatherLod, earthMask);
                    if (density > 0.0001)
                    {
                        debugSteps += 1.0;

                         float sunOpticalDepth = LightMarch(p, sunDir, lightStepBase, lod, weatherLod, earthMask);
                         float radialSun = saturate(dot(normalize(p), sunDir));
                         float sunFacing = smoothstep(-0.05, 0.72, radialSun);
                         float sunTrans = exp(-sunOpticalDepth * sigmaExt * 0.08);
                         sunTrans = max(sunTrans, 0.12 + (sunFacing * 0.40));

                         float powderRaw = 1.0 - exp(-density * sigmaExt * 2.0 * _CldPowderStrength);
                         // Пудра заметнее на "тёмной" от камеры стороне облака.
                         float powder = lerp(1.0, powderRaw, saturate((cosSunView * 0.5) + 0.5));

                         float directLighting = lerp(0.12, 0.30, sunFacing);
                         float3 sunLit = _CldSunColor * (directLighting + (phase * sunTrans * 0.75)) * powder;
                         float3 lit = (sunLit + ambient) * _CldScatterAlbedo * _CldIntensity;
                         lit += neutralScatterColor * (fillScatter * density);

                        float sigmaT = sigmaExt * density;
                        float segTrans = exp(-sigmaT * ds);

                        float3 integScatter = lit * (1.0 - segTrans) / max(sigmaT, 1e-6);
                        inScatter += transmittance * integScatter;
                        transmittance *= segTrans;

                         if (transmittance < _CldEarlyExitT)
                         {
                             break;
                         }
                     }
                 }

                 float3 highlight = max(inScatter - 0.55, 0.0);
                 inScatter -= highlight / (1.0 + (1.5 * highlight));

                 float alpha = saturate(1.0 - transmittance);

                if (_CldDebugMode > 0.5)
                {
                    if (_CldDebugMode < 1.5)
                    {
                        return float4(alpha.xxx, 1.0);
                    }

                    if (_CldDebugMode < 2.5)
                    {
                        return float4(debugCoverage.xxx, 1.0);
                    }

                    if (_CldDebugMode < 3.5)
                    {
                        return float4(transmittance.xxx, 1.0);
                    }

                    if (_CldDebugMode < 4.5)
                    {
                        return float4(debugSteps / (float)steps, 0.0, 0.0, 1.0);
                    }

                    if (_CldDebugMode < 5.5)
                    {
                        return float4(lod / 6.0, 0.0, 0.0, 1.0);
                    }

                    if (_CldDebugMode < 6.5)
                    {
                        return float4(debugClear.xxx, 1.0);
                    }

                    if (_CldDebugMode < 7.5)
                    {
                        return float4(debugSize.xxx, 1.0);
                    }

                    return float4(weatherLod / 5.0, 0.0, 0.0, 1.0);
                }

                return float4(inScatter, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
