Shader "Galilego/StarField"
{
    SubShader
    {
        // Transparent+1: рисуется ПОСЛЕ атмосферы (она делает ручной композит
        // с заменой фона), иначе звёзды затёрлись бы её выводом.
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+1" }

        Pass
        {
            Name "StarFieldForward"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // Ставит SkyEnvironment: видимость звёзд (космос/ночь 1 → день 0).
            float _StarVisibility;

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

            float hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            // Один слой звёзд: по ячейкам направления случайная звезда со своим
            // положением/яркостью/температурой. Возвращает (яркость, temp).
            float2 StarLayer(float3 dir, float density, float keepThreshold, float size, float seed)
            {
                float3 p = dir * density;
                float3 q = floor(p);
                float3 r = frac(p);
                float3 starPos = float3(hash31(q + seed), hash31(q + (seed + 11.1)), hash31(q + (seed + 23.2)));
                float d = length(r - starPos);
                float keep = step(keepThreshold, hash31(q + (seed + 7.7)));
                float brightness = keep * smoothstep(size, 0.0, d);
                float temp = hash31(q + (seed + 41.0));
                return float2(brightness, temp);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                if (_StarVisibility <= 0.001)
                {
                    discard;
                }

                // Окклюзия по глубине, а не по радиусу купола: если в пикселе
                // есть непрозрачная геометрия (планета/рельеф) — звёзды не
                // рисуем. Иначе на дистанции больше купола (0.5·far) звёзды
                // оказывались бы ПЕРЕД далёкой планетой.
                if (!IsSky(uint2(input.positionCS.xy)))
                {
                    discard;
                }

                float3 rd = normalize(input.dir);
                float3 col = 0.0;

                // Яркие редкие.
                float2 s1 = StarLayer(rd, 160.0, 0.93, 0.10, 0.0);
                col += lerp(float3(1.0, 0.78, 0.58), float3(0.74, 0.84, 1.0), s1.y) * s1.x * 4.0;

                // Тусклые частые.
                float2 s2 = StarLayer(rd, 420.0, 0.86, 0.08, 3.3);
                col += lerp(float3(1.0, 0.78, 0.58), float3(0.74, 0.84, 1.0), s2.y) * s2.x * 0.9;

                return float4(col * _StarVisibility, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
