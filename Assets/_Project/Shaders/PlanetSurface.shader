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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float4 color      : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
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
                float light = _NightAmbient + ((_SkyAmbient + _TerrainSun) * ndl);
                float3 lightTerm = float3(light, light, light);

                // Суша (color.a = 0).
                float3 land = input.color.rgb * lightTerm;

                // Вода (color.a = 1): базовый цвет и отражение неба под светом,
                // блик солнца — только на освещённой стороне.
                float fresnel = pow(1.0 - saturate(dot(normal, viewDir)), 5.0);
                float3 halfVec = normalize(sunDir + viewDir);
                float spec = pow(saturate(dot(normal, halfVec)), max(1.0, _SpecularPower)) * _SpecularIntensity;
                float3 water = (lerp(_WaterDeep.rgb, _WaterShallow.rgb, ndl) * lightTerm)
                    + (_RimColor.rgb * fresnel * lightTerm)
                    + (spec * _TerrainSun);

                float3 color = lerp(land, water, saturate(input.color.a));
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
