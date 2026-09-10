Shader "Galilego/PlanetAtmosphere"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "PlanetAtmosphereForward"
            Tags { "LightMode" = "ForwardOnly" }

            // Камера-центрированный купол (не меш вокруг планеты!): всегда
            // влезает в far, поэтому основную камеру НЕ надо растягивать под
            // атмосферу (иначе far/near → z-fighting). Пересечение луча с
            // атмосферой/планетой считается аналитически в мировом кадре.
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            float3 _AtmCameraWS;
            float3 _AtmBodyCenterWS;
            float3 _AtmSunDirWS;
            float _AtmPlanetRadius;
            float _AtmRadius;
            float _AtmScaleHeight;
            float4 _AtmRayleighColor;
            float4 _AtmMieColor;
            float _AtmIntensity;
            float _AtmMieAnisotropy;
            float _AtmStepCount;
            float _AtmDensityFalloff;
            float _AtmPlanetOcclusion;
            float _AtmMultiScatter;

            #define ATM_PI 3.14159265

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
                output.dir = input.positionOS;
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

                float t0 = max(a0, 0.0);
                float t1 = a1;
                if (t1 <= t0)
                {
                    discard;
                }

                if (_AtmPlanetOcclusion > 0.5)
                {
                    float p0;
                    float p1;
                    if (RaySphere(ro, rd, _AtmPlanetRadius, p0, p1) && p1 > 0.0)
                    {
                        t1 = min(t1, max(p0, 0.0));
                    }
                }

                if (t1 <= t0)
                {
                    discard;
                }

                int steps = (int)_AtmStepCount;
                float ds = (t1 - t0) / max(1.0, (float)steps);
                float hScale = max(1.0, _AtmScaleHeight);

                float jitter = frac(sin(dot(input.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453);

                float3 accum = 0.0;
                for (int i = 0; i < 128; i++)
                {
                    if (i >= steps)
                    {
                        break;
                    }

                    float t = t0 + ((i + jitter) * ds);
                    float3 p = ro + (rd * t);
                    float height = length(p) - _AtmPlanetRadius;
                    if (height < 0.0)
                    {
                        continue;
                    }

                    float density = exp(-(height / hScale) * _AtmDensityFalloff);

                    // Тень планеты: луч к солнцу из точки пересёк планету.
                    if (_AtmPlanetOcclusion > 0.5)
                    {
                        float q0;
                        float q1;
                        if (RaySphere(p, sunDir, _AtmPlanetRadius, q0, q1) && q1 > 0.0 && q0 > 0.0)
                        {
                            continue;
                        }
                    }

                    const float scatterScale = 4.0e-5;
                    float s0;
                    float s1;
                    float sunDepth = 0.0;
                    if (RaySphere(p, sunDir, _AtmRadius, s0, s1))
                    {
                        sunDepth = max(s1, 0.0) * density * scatterScale;
                    }

                    float3 trans = exp(-_AtmRayleighColor.rgb * sunDepth * 2.0);

                    float cosA = dot(rd, sunDir);
                    float rayleighPhase = (3.0 / (16.0 * ATM_PI)) * (1.0 + (cosA * cosA));
                    float g = clamp(_AtmMieAnisotropy, -0.9, 0.9);
                    float miePhase = (1.0 - (g * g))
                        / (4.0 * ATM_PI * pow(1.0 + (g * g) - (2.0 * g * cosA), 1.5));

                    float3 scatter = (_AtmRayleighColor.rgb * rayleighPhase)
                        + (_AtmMieColor.rgb * miePhase * 0.15);
                    accum += scatter * density * trans * ds * scatterScale;
                }

                accum *= _AtmIntensity;

                // Многократное рассеяние: голубой зенит.
                float3 up0 = normalize(ro);
                float height0 = max(0.0, length(ro) - _AtmPlanetRadius);
                float dens0 = exp(-height0 / hScale);
                float dayMass = saturate(dot(up0, sunDir));
                accum += _AtmRayleighColor.rgb * (dayMass * dens0 * _AtmMultiScatter);

                return float4(accum, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
