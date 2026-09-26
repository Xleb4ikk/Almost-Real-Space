Shader "Galilego/PlanetAtmosphere"
{
    // Атмосфера планеты: камера-центрированный купол (PlanetAtmosphereView),
    // аналитическое пересечение луча с оболочками, raymarch одиночного
    // рассеяния + приближение многократного (LUT, Hillaire EGSR 2020).
    //
    // Физика: β из C# (AtmosphereOptics — единый источник с CPU SkyPhysics),
    // LUT прозрачности к Солнцу и многократного рассеяния генерируются на CPU
    // и лишь сэмплируются тут.
    //
    // Горизонт: путь луча у касательной математически разрывен (≈2 км до
    // поверхности ↔ ≈сотни км до выхода из оболочки). Чтобы не было жёсткой
    // ступеньки, для пикселей неба, попавших по сфере планеты, считаются ДВА
    // вклада — «земля» (короткий путь) и «небо» (полный путь) — и плавно
    // смешиваются в узком окне чуть ниже касательной (HorizonFade). Там, где
    // рельефа нет, сфера закрашивается затенённой землёй с дымкой, а не
    // тёмным клиром.
    //
    // Композит: цвет opaque-сцены уже в буфере — сэмплим сами, гасим по
    // transmittance и добавляем in-scatter. Blend One Zero = замена, фон учтён
    // вручную.
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "PlanetAtmosphereForward"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One Zero
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
            float3 _AtmCameraWS;
            float3 _AtmCameraForwardWS;
            float3 _AtmBodyCenterWS;
            float3 _AtmSunDirWS;
            float3 _AtmSunColor;

            // --- Геометрия и оптика (м⁻¹) из AtmosphereOptics ---
            float _AtmPlanetRadius;
            float _AtmRadius;
            float _AtmAtmDepth;
            float3 _AtmBetaRayleigh;
            float3 _AtmBetaMie;
            float3 _AtmBetaOzone;
            float _AtmHr;
            float _AtmHm;
            float _AtmOzoneCenter;
            float _AtmOzoneWidth;
            float _AtmMieG;
            float _AtmIntensity;
            float _AtmStepCount;
            float _AtmPlanetOcclusion;
            float _AtmHorizonFade;
            float4 _AtmGroundColor;
            float _AtmDebugMode;

            // CPU-LUT: прозрачность к Солнцу и многократное рассеяние.
            TEXTURE2D(_AtmTransmittanceTex);
            SAMPLER(sampler_AtmTransmittanceTex);
            TEXTURE2D(_AtmMultiScatterTex);
            SAMPLER(sampler_AtmMultiScatterTex);

            #define ATM_PI 3.14159265358979

            // Калибровка радианса сцены: реальные β Рэлея слабее прежних
            // эмпирических; регулируется Intensity.
            #define ATM_RADIANCE_SCALE 5.0

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
                float safeRadius = max(abs(radius), 1e-4);
                float3 safeDirection = rd * rsqrt(max(dot(rd, rd), 1e-8));
                float3 normalizedOrigin = ro / safeRadius;
                float b = dot(normalizedOrigin, safeDirection);
                float c = dot(normalizedOrigin, normalizedOrigin) - 1.0;
                float h = (b * b) - c;
                if (h < -1e-7)
                {
                    t0 = 0.0;
                    t1 = 0.0;
                    return false;
                }

                h = sqrt(max(h, 0.0));
                t0 = (-b - h) * safeRadius;
                t1 = (-b + h) * safeRadius;
                return true;
            }

            float RayleighPhase(float c)
            {
                return (3.0 / (16.0 * ATM_PI)) * (1.0 + (c * c));
            }

            // Фаза Корнетта-Шенкса (нормированная), дымка/ореол вокруг Солнца.
            float MiePhase(float c, float g)
            {
                float g2 = g * g;
                float num = 3.0 * (1.0 - g2) * (1.0 + (c * c));
                float den = 8.0 * ATM_PI * (2.0 + g2) * pow(max(1e-4, 1.0 + g2 - (2.0 * g * c)), 1.5);
                return num / den;
            }

            float3 AtmExtinction(float height)
            {
                float dR = exp(-height / max(1.0, _AtmHr));
                float dM = exp(-height / max(1.0, _AtmHm));
                float dO = saturate(1.0 - abs((height - _AtmOzoneCenter) / max(1.0, _AtmOzoneWidth)));
                return (_AtmBetaRayleigh * dR) + (_AtmBetaMie * dM) + (_AtmBetaOzone * dO);
            }

            float3 AtmScattering(float height)
            {
                float dR = exp(-height / max(1.0, _AtmHr));
                float dM = exp(-height / max(1.0, _AtmHm));
                return (_AtmBetaRayleigh * dR) + (_AtmBetaMie * dM);
            }

            float3 SampleTransmittance(float height, float cosTheta)
            {
                float2 uv = float2(cosTheta * 0.5 + 0.5, saturate(height / max(1e-3, _AtmAtmDepth)));
                return SAMPLE_TEXTURE2D_LOD(_AtmTransmittanceTex, sampler_AtmTransmittanceTex, uv, 0).rgb;
            }

            float3 SampleMultiScatter(float height, float cosTheta)
            {
                float2 uv = float2(cosTheta * 0.5 + 0.5, saturate(height / max(1e-3, _AtmAtmDepth)));
                return SAMPLE_TEXTURE2D_LOD(_AtmMultiScatterTex, sampler_AtmMultiScatterTex, uv, 0).rgb;
            }

            // Аналитический интеграл однородного сегмента:
            // transmittance · (S − S·transSeg) / sigmaE.
            float3 IntegrateOverSegment(float3 S, float3 transSeg, float3 transmittance, float3 sigmaE)
            {
                return transmittance * (S - (S * transSeg)) / max(sigmaE, 1e-12);
            }

            // Один raymarch от tStart до tEnd. Возвращает in-scatter, в
            // outTransmittance — прозрачность к концу пути.
            //
            // Сэмплы — середины N равных сегментов (смещение 0.5), т.е.
            // стратифицированная выборка. Раньше смещение бралось из per-pixel
            // белого шума: он разбирается только временным накоплением, а TAA в
            // проекте выключен (FirstPersonCamera.TemporalAA = false, стоит
            // SMAA) — поэтому шум оставался прибитым к экрану и шёл за поворотом
            // камеры (сетка точек в ореоле Солнца и по диску планеты из космоса).
            // При 48 сегментах и Hr ≈ 8.5 км погрешность midpoint гладкая, мерцания
            // и бандинга не даёт, поэтому разброс шага не нужен.
            float3 IntegrateAtmosphere(float3 ro, float3 rd, float3 sunDir,
                float tStart, float tEnd, out float3 outTransmittance)
            {
                int steps = clamp((int)_AtmStepCount, 2, 128);
                float ds = (tEnd - tStart) / (float)steps;

                float cosA = dot(rd, sunDir);
                float phaseR = RayleighPhase(cosA);
                float phaseM = MiePhase(cosA, clamp(_AtmMieG, -0.9, 0.9));

                float3 inScatter = 0.0;
                float3 transmittance = 1.0;

                for (int i = 0; i < 128; i++)
                {
                    if (i >= steps)
                    {
                        break;
                    }

                    float t = tStart + ((i + 0.5) * ds);
                    float3 p = ro + (rd * t);
                    float r = length(p);
                    float height = max(0.0, r - _AtmPlanetRadius);
                    float3 n = p / max(r, 1e-3);

                    float3 sigmaE = AtmExtinction(height);
                    float3 scattering = AtmScattering(height);
                    float3 transSeg = exp(-sigmaE * ds);

                    float cosSun = dot(n, sunDir);
                    float3 sunT = SampleTransmittance(height, cosSun);
                    float3 ms = SampleMultiScatter(height, cosSun);

                    float3 rayleighScatter = _AtmBetaRayleigh * exp(-height / max(1.0, _AtmHr));
                    float3 mieScatter = _AtmBetaMie * exp(-height / max(1.0, _AtmHm));
                    float3 phaseScatter = (rayleighScatter * phaseR) + (mieScatter * phaseM);

                    float3 S = (sunT * phaseScatter) + (ms * scattering);

                    inScatter += IntegrateOverSegment(S, transSeg, transmittance, sigmaE);
                    transmittance *= transSeg;
                }

                outTransmittance = transmittance;
                return inScatter;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 rd = normalize(input.dir);
                float3 ro = _AtmCameraWS - _AtmBodyCenterWS;
                float3 sunDir = normalize(_AtmSunDirWS);

                float a0;
                float a1;
                if (!RaySphere(ro, rd, _AtmRadius, a0, a1))
                {
                    discard;
                }

                float tAtm = max(a1, 0.0);
                if (tAtm <= 0.0)
                {
                    discard;
                }

                float r0 = length(ro);
                float3 n0 = ro / max(r0, 1e-3);

                // Глубина сцены: путь до реального рельефа. Пиксель неба
                // определяется штатным IsSky (сырая глубина = дальний клир).
                uint2 pixel = uint2(input.positionCS.xy);
                float cosFwd = dot(rd, _AtmCameraForwardWS);
                bool hasTerrain = false;
                float tDepth = 1e30;
                float eyeDepth = 1e30;
                if (cosFwd > 1e-4)
                {
                    eyeDepth = LinearEyeDepth(LoadCameraDepth(pixel), _ZBufferParams);
                    hasTerrain = !IsSky(pixel) && eyeDepth > 0.0;
                    if (hasTerrain)
                    {
                        tDepth = eyeDepth / cosFwd;
                    }
                }

                // Сфера уровня моря — окклюдер/фолбэк-земля.
                float p0;
                float p1;
                bool hitPlanet = false;
                float tPlanet = 1e30;
                if (RaySphere(ro, rd, _AtmPlanetRadius, p0, p1) && p1 > 0.0)
                {
                    hitPlanet = true;
                    tPlanet = max(p0, 0.0);
                }

                bool planetOccludes = hitPlanet && _AtmPlanetOcclusion > 0.5;

                // Цвет фона — цветовая пирамида с учётом dynamic resolution
                // (паттерн HDRP Lit.hlsl, рефракция прозрачных).
                float2 screenUv = input.positionCS.xy * _ScreenSize.zw;
                float2 colorUv = min(screenUv * _RTHandleScaleHistory.xy,
                    _ColorPyramidUvScaleAndLimitCurrentFrame.zw);
                float3 bgRaw = SAMPLE_TEXTURE2D_X_LOD(
                    _ColorPyramidTexture, s_trilinear_clamp_sampler, colorUv, 0).rgb;

                float3 radianceScale = _AtmSunColor * (_AtmIntensity * ATM_RADIANCE_SCALE);

                bool hasGround = hasTerrain || planetOccludes;
                float tGround = min(tDepth, planetOccludes ? tPlanet : 1e30);
                tGround = min(tGround, tAtm);

                float3 groundT = 1.0;
                float3 skyT = 1.0;
                float3 groundColor = bgRaw;
                if (hasGround)
                {
                    float3 groundBg = bgRaw;
                    if (!hasTerrain && planetOccludes)
                    {
                        // Фолбэк-земля: затенённый Ламберт с прозрачностью к
                        // Солнцу, чтобы за недорисованным рельефом была не
                        // дыра, а дымчатая земля.
                        float3 hp = ro + (rd * tPlanet);
                        float3 N = hp / max(length(hp), 1e-3);
                        float cosSunG = dot(N, sunDir);
                        float3 sunG = SampleTransmittance(0.0, cosSunG);
                        groundBg = _AtmGroundColor.rgb * saturate(cosSunG) * sunG;
                    }

                    float3 groundScatter = IntegrateAtmosphere(ro, rd, sunDir, 0.0, tGround, groundT);
                    groundColor = (groundBg * groundT) + (groundScatter * radianceScale);
                }

                // Косинус зенита луча и граница касательной.
                float cosChi = dot(n0, rd);
                float sinHor = clamp(_AtmPlanetRadius / max(r0, _AtmPlanetRadius), 0.0, 1.0);
                float cosHor = -sqrt(max(0.0, 1.0 - (sinHor * sinHor)));
                float horizon = smoothstep(-max(0.001, _AtmHorizonFade), 0.0, cosChi - cosHor);

                float3 skyColor = 0.0;
                if (!hasTerrain && !planetOccludes)
                {
                    float3 skyScatter = IntegrateAtmosphere(ro, rd, sunDir, 0.0, tAtm, skyT);
                    skyColor = (bgRaw * skyT) + (skyScatter * radianceScale);
                }

                float3 color;
                if (hasTerrain)
                {
                    color = groundColor;
                }
                else if (planetOccludes)
                {
                    color = groundColor;
                    if (horizon > 0.001)
                    {
                        // Небо ровно в направлении касательной (планета его не
                        // перекрывает) — непрерывный ориентир для плавного
                        // стыка. Интегрировать луч «сквозь» планету нельзя:
                        // плотность у поверхности даёт яркую ложную полосу.
                        float3 dirHorDelta = rd + ((cosHor - cosChi) * n0);
                        if (dot(dirHorDelta, dirHorDelta) > 1e-12)
                        {
                            float3 dirHor = normalize(dirHorDelta);
                            float ha0;
                            float ha1;
                            float tAtmHor = tAtm;
                            if (RaySphere(ro, dirHor, _AtmRadius, ha0, ha1))
                            {
                                tAtmHor = max(ha1, 0.0);
                            }

                            float3 horT;
                            float3 horScatter = IntegrateAtmosphere(ro, dirHor, sunDir, 0.0, tAtmHor, horT);
                            float3 skyHorizonColor = (bgRaw * horT) + (horScatter * radianceScale);
                            color = lerp(groundColor, skyHorizonColor, horizon);
                        }
                    }
                }
                else
                {
                    color = skyColor;
                }

                // --- Debug (Фаза 0) ---
                if (_AtmDebugMode > 0.5)
                {
                    if (_AtmDebugMode < 1.5)
                    {
                        return float4(hasTerrain ? groundT : skyT, 1.0);
                    }

                    if (_AtmDebugMode < 2.5)
                    {
                        return float4(tAtm / max(1.0, _AtmAtmDepth), 0.0, 0.0, 1.0);
                    }

                    if (_AtmDebugMode < 3.5)
                    {
                        float tEnd = hasGround ? tGround : tAtm;
                        return float4(tEnd / max(1.0, _AtmAtmDepth), 0.0, 0.0, 1.0);
                    }

                    if (_AtmDebugMode < 4.5)
                    {
                        return float4(frac(eyeDepth * 0.0001), 0.0, 0.0, 1.0);
                    }

                    if (_AtmDebugMode < 5.5)
                    {
                        return float4(AtmExtinction(0.0) * 1e5, 1.0);
                    }

                    if (_AtmDebugMode < 6.5)
                    {
                        float3 dbgT;
                        float3 dbgScatter = IntegrateAtmosphere(ro, rd, sunDir, 0.0,
                            hasGround ? tGround : tAtm, dbgT);
                        return float4(dbgScatter * radianceScale, 1.0);
                    }

                    if (_AtmDebugMode < 7.5)
                    {
                        float3 hp = ro + (rd * min(hasGround ? tGround : tAtm, 1000.0));
                        float3 N = hp / max(length(hp), 1e-3);
                        return float4(SampleTransmittance(max(0.0, length(hp) - _AtmPlanetRadius), dot(N, sunDir)), 1.0);
                    }

                    if (_AtmDebugMode < 8.5)
                    {
                        return float4(_RTHandleScaleHistory.xy, 0.0, 1.0);
                    }

                    return float4(colorUv, 0.0, 1.0);
                }

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
