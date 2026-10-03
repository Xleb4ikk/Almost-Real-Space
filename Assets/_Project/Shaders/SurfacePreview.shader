Shader "Galilego/SurfacePreview"
{
    Properties
    {
        _PreviewSunDir("Sun Direction", Vector) = (0.55, 0.45, 0.70, 0)
        _PreviewSunColor("Sun Color", Color) = (1.0, 0.97, 0.92, 1)
        _PreviewAmbient("Ambient", Color) = (0.22, 0.27, 0.34, 1)
        _PreviewExposure("Exposure", Range(0.1, 8.0)) = 1.6
        _ContourSpacing("Contour Spacing meters, 0 = off", Float) = 0
        _ContourStrength("Contour Strength", Range(0.0, 1.0)) = 0.35
        _SlopeShade("Slope Shade", Range(0.0, 1.0)) = 0.55
        _UnderwaterTint("Underwater Tint", Color) = (0.16, 0.34, 0.52, 1)
        _PreviewSeaLevel("Sea Level meters", Float) = -1000000000
        // Сетка масштаба по локальной XZ (север/восток) фрейма. Без неё
        // превью — просто зелёный купол: не видно, 10 это метров или километр,
        // и ставить базу не по чему.
        _PreviewGridSpacing("Grid Spacing meters, 0 = off", Float) = 0
        _PreviewGridMinor("Grid Minor Fraction", Range(0.05, 1.0)) = 0.2
        _PreviewGridStrength("Grid Strength", Range(0.0, 1.0)) = 0.5
        _PreviewGridFade("Grid Fade Distance, cell units", Float) = 900
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "SurfacePreviewForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            float4 _PreviewSunDir;
            float4 _PreviewSunColor;
            float4 _PreviewAmbient;
            float _PreviewExposure;
            float _ContourSpacing;
            float _ContourStrength;
            float _SlopeShade;
            float4 _UnderwaterTint;
            float _PreviewSeaLevel;
            float _PreviewGridSpacing;
            float _PreviewGridMinor;
            float _PreviewGridStrength;
            float _PreviewGridFade;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 extra      : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 localPos   : TEXCOORD3;
                float4 color      : COLOR;
                float4 extra      : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.localPos = input.positionOS;
                output.color = input.color;
                output.extra = input.extra;
                return output;
            }

            // Линия сетки по экранной ширине: толщина постоянная в пикселях,
            // иначе на большом масштабе каждая линия — пол-экрана, а на
            // маленьком исчезает в шум.
            float GridLine(float coord, float widthPixels)
            {
                float d = abs(frac(coord - 0.5) - 0.5) / max(fwidth(coord), 1e-6);
                return 1.0 - smoothstep(0.0, widthPixels, d);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 l = normalize(_PreviewSunDir.xyz);

                // Честный ламберт + небольшой wrap. Полу-ламберт
                // (dot*0.5+0.5) съедал уклоны: при 9° наклона разница в
                // освещении выходила 8%, и рельеф с перепадом 92 м на 600 м
                // читался как ровная столешница. Wrap оставлен маленьким, чтобы
                // теневые склоны не проваливались в чёрное.
                float ndl = saturate(dot(n, l));
                float wrap = saturate(dot(n, l) * 0.5 + 0.5);
                float direct = (ndl * 0.85) + (wrap * 0.15);
                float3 albedo = input.color.rgb;

                // Склон: гасим пологое, подсвечиваем крутое — рельеф читается
                // даже на однородном зелёном пресете.
                float slope = saturate(1.0 - input.extra.y);
                albedo *= lerp(1.0, 0.45, saturate(slope * _SlopeShade));

                float3 color = albedo * (_PreviewAmbient.rgb + (_PreviewSunColor.rgb * direct));

                // Под водой — тонируем в синий: граница «суша/море» в превью
                // должна читаться, иначе видно дно вместо океана.
                float sea = step(input.extra.x, _PreviewSeaLevel);
                color = lerp(color, color * _UnderwaterTint.rgb, sea * 0.75);

                if (_ContourSpacing > 0.0)
                {
                    // Горизонтали: расстояние до ближайшей полосы в единицах шага,
                    // толщина линии — один пиксель (fwidth). Имя НЕ "line":
                    // это зарезервированное слово HLSL (модификатор интерполяции).
                    float band = input.extra.x / _ContourSpacing;
                    float toLine = abs(frac(band) - 0.5);
                    float contour = 1.0 - smoothstep(0.0, max(fwidth(band), 1e-5), toLine);
                    color = lerp(color, color * 0.55, contour * _ContourStrength);
                }

                // Сетка масштаба: локальные XZ меша — это север/восток фрейма
                // в метрах (меш строится в осях фрейма, не мира). Прячется к
                // горизонту, иначе море линий.
                if (_PreviewGridSpacing > 0.0)
                {
                    float2 km = input.localPos.xz * 0.001;
                    float major = GridLine(km.x / _PreviewGridSpacing, 1.4)
                                + GridLine(km.y / _PreviewGridSpacing, 1.4);
                    float2 cell = km / (_PreviewGridSpacing * _PreviewGridMinor);
                    float minor = GridLine(cell.x, 1.0) + GridLine(cell.y, 1.0);
                    float fade = saturate(1.0 - length(km) / max(_PreviewGridFade, 1e-3));
                    fade *= fade;
                    float g = saturate((major + minor * 0.45) * fade) * _PreviewGridStrength;
                    color = lerp(color, color * 0.4, saturate(g));
                }

                return float4(color * _PreviewExposure, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct DepthAttributes
            {
                float3 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            float4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
