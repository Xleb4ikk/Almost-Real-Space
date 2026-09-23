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

            // --- Камера/тело/солнце ---
            float3 _CldCameraWS;
            float3 _CldCameraForwardWS;
            float3 _CldBodyCenterWS;
            float3 _CldSunDirWS;
            float3 _CldSunColor;

            // --- Геометрия слоя ---
            float _CldPlanetRadius;
            float _CldBottomRadius;
            float _CldTopRadius;

            // --- Domain шума (мировые координаты относительно центра тела) ---
            float3 _CldWindOffset;
            float _CldShapeFreq;
            float _CldDetailFreq;
            float _CldWeatherFreq;
            float _CldShapeTexelWorldSize;

            // --- Форма/покрытие ---
            float _CldCoverage;
            float _CldDetailErosion;
            float _CldBottomFeather;
            float _CldTopFeather;

            // --- Оптика ---
            float _CldExtinction;
            float _CldScatterAlbedo;
            float _CldPhaseG;
            float _CldPowderStrength;
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
            TEXTURE3D(_CldWeatherTex);
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

            bool RaySphere(float3 ro, float3 rd, float radius, out float t0, out float t1)
            {
                float b = dot(ro, rd);
                float c = dot(ro, ro) - (radius * radius);
                float h = (b * b) - c;
                if (h < 0.0)
                {
                    t0 = 0.0;
                    t1 = 0.0;
                    return false;
                }

                h = sqrt(h);
                t0 = -b - h;
                t1 = -b + h;
                return true;
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

            // Крупномасштабное покрытие (погодные фронты). Сэмплируется по
            // МИРОВОЙ позиции относительно центра тела — бесшовно на всей
            // сфере, полюсов/швов нет в принципе (в отличие от lat-long).
            float SampleWeatherCoverage(float3 posRel)
            {
                float4 w = SAMPLE_TEXTURE3D_LOD(_CldWeatherTex, sampler_CldWeatherTex,
                    (posRel * _CldWeatherFreq) + (_CldWindOffset * _CldWeatherFreq * 0.15), 0.0);
                // *2: чтобы Coverage=0.5 давал ~половину неба, а не четверть
                // (weather-текстура сама по себе центрирована на 0.5).
                return saturate(w.r * _CldCoverage * 2.0);
            }

            // Плотность облака в точке (posRel — относительно центра тела).
            // lod — footprint-based LOD текстуры формы (см. PlanetCloudsView).
            float SampleCloudDensity(float3 posRel, float lod)
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

                float coverage = SampleWeatherCoverage(posRel);
                if (coverage <= 0.001)
                {
                    return 0.0;
                }

                float3 windedPos = posRel + _CldWindOffset;

                float4 shapeSample = SAMPLE_TEXTURE3D_LOD(_CldShapeTex, sampler_CldShapeTex, windedPos * _CldShapeFreq, lod);
                float baseShape = shapeSample.r;

                float shaped = saturate(baseShape * heightGradient);
                float baseCloud = saturate(CloudRemap(shaped, 1.0 - coverage, 1.0, 0.0, 1.0)) * coverage;
                if (baseCloud <= 0.0)
                {
                    return 0.0;
                }

                float3 detailSample = SAMPLE_TEXTURE3D_LOD(_CldDetailTex, sampler_CldDetailTex, windedPos * _CldDetailFreq, lod).rgb;
                float detailFbm = dot(detailSample, float3(0.55, 0.3, 0.15));

                // Эрозия сильнее у низа/верха (там, где heightGradient уже
                // мал) — даёт рваный "дымный" край именно там, где по ТЗ
                // нужен пролёт сквозь дым, а не жёсткий блоб.
                float erodeWeight = lerp(0.15, 1.0, 1.0 - heightGradient) * _CldDetailErosion;
                float finalCloud = CloudRemap(baseCloud, erodeWeight * detailFbm, 1.0, 0.0, 1.0);

                return saturate(finalCloud);
            }

            // Короткий марш к солнцу (самозатенение). Растущий шаг —
            // дешёвая замена honest cone sampling.
            float LightMarch(float3 posRel, float3 sunDir, float baseStep, float lod)
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
                    totalDensity += SampleCloudDensity(p, lod + 1.0) * stepSize;
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
                bool hitTop = RaySphere(ro, rd, _CldTopRadius, top0, top1);
                if (!hitTop || top1 < 0.0)
                {
                    discard;
                }

                float bot0;
                float bot1;
                bool hitBot = RaySphere(ro, rd, _CldBottomRadius, bot0, bot1);

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
                bool hitPlanet = RaySphere(ro, rd, _CldPlanetRadius, planet0, planet1) && planet1 > 0.0;
                float tPlanet = hitPlanet ? max(planet0, 0.0) : 1e30;

                marchEnd = min(marchEnd, tDepth);
                marchEnd = min(marchEnd, tPlanet);
                if (marchEnd <= marchStart)
                {
                    discard;
                }

                int steps = clamp((int)_CldStepCount, 8, 128);
                float ds = (marchEnd - marchStart) / (float)steps;
                float lod = clamp(log2(max(ds / max(1e-3, _CldShapeTexelWorldSize), 1.0)), 0.0, 6.0);

                // Тот же джиттер, что и в PlanetAtmosphere (проверенный,
                // не даёт дополнительной screen-space периодики).
                float jitter = frac(sin(dot(input.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453);

                float cosSunView = dot(rd, sunDir);
                float phase = HenyeyGreenstein(cosSunView, clamp(_CldPhaseG, 0.0, 0.95));
                float3 ambient = _CldAmbientTint.rgb * _CldSunColor * 0.35;

                float sigmaExt = max(1e-5, _CldExtinction);
                float lightStepBase = max(1.0, (_CldTopRadius - _CldBottomRadius) / max(1.0, _CldLightSteps));

                float transmittance = 1.0;
                float3 inScatter = 0.0;
                float debugSteps = 0.0;
                float debugCoverage = SampleWeatherCoverage(ro + (rd * marchStart));

                [loop]
                for (int i = 0; i < 128; i++)
                {
                    if (i >= steps)
                    {
                        break;
                    }

                    float t = marchStart + ((i + jitter) * ds);
                    float3 p = ro + (rd * t);

                    float density = SampleCloudDensity(p, lod);
                    if (density > 0.0001)
                    {
                        debugSteps += 1.0;

                        float sunOpticalDepth = LightMarch(p, sunDir, lightStepBase, lod);
                        float sunTrans = exp(-sunOpticalDepth * sigmaExt);

                        float powderRaw = 1.0 - exp(-density * sigmaExt * 2.0 * _CldPowderStrength);
                        // Пудра заметнее на "тёмной" от камеры стороне облака.
                        float powder = lerp(1.0, powderRaw, saturate((cosSunView * 0.5) + 0.5));

                        float3 sunLit = _CldSunColor * sunTrans * phase * powder;
                        float3 lit = (sunLit + ambient) * _CldScatterAlbedo * _CldIntensity;

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

                    return float4(lod / 6.0, 0.0, 0.0, 1.0);
                }

                return float4(inScatter, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
