Shader "Galilego/PlanetSurface"
{
    Properties
    {
        _WaterDeep("Water Deep", Color) = (0.02, 0.09, 0.22, 1)
        _WaterShallow("Water Shallow", Color) = (0.10, 0.32, 0.52, 1)
        _SpecularPower("Specular Power", Float) = 120.0
        _SpecularIntensity("Specular Intensity", Float) = 0.8
        _RimColor("Water Sky Rim", Color) = (0.35, 0.55, 0.85, 1)
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
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // Глобально, ставит SunBillboard каждый кадр (направление НА звезду).
            float3 _TerrainSunDir;
            // Глобально, ставит PlanetSurfaceRenderer (мировая позиция камеры).
            float3 _PlanetCameraPos;
            // Глобально, ставит SkyEnvironment: звёздный/небесный ambient и солнце.
            float _NightAmbient;
            float _SkyAmbient;
            float _TerrainSun;

            float4 _WaterDeep;
            float4 _WaterShallow;
            float4 _RimColor;
            float _SpecularPower;
            float _SpecularIntensity;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float4 color      : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float2 uv         : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 sunDir = normalize(_TerrainSunDir);
                float3 viewDir = normalize(_PlanetCameraPos - input.positionWS);
                float ndl = saturate(dot(normal, sunDir));

                // Единый световой член, пофрагментный: ночная засветка (звёзды) +
                // небесная засветка и солнце по нормали к НЕЙ. Ночью ndl=0 →
                // остаётся только _NightAmbient (≈0) → поверхность почти черна.
                // Сжимаем САМ параметр солнца (а не итоговый свет): тогда при любом
                // значении _TerrainSun сохраняется затенение по нормали (рельеф
                // читается), но яркость не улетает в белый.
                float sun = _TerrainSun / (1.0 + _TerrainSun);
                float light = _NightAmbient + ((_SkyAmbient + sun) * ndl);
                float3 lightTerm = float3(light, light, light);

                // Суша (color.a = 0).
                float3 land = input.color.rgb * lightTerm;

                // Вода (color.a = 1): глубина из uv.x (0 — мелководье, 1 — глубина),
                // процедурная рябь ломает зеркальную нормаль, блик солнца —
                // только на освещённой стороне.
                float waterDepth = saturate(input.uv.x);
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
                    + (spec * _TerrainSun);

                float3 color = lerp(land, water, saturate(input.color.a));
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
