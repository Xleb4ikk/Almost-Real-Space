Shader "Galilego/GroundDecorSolid"
{
    Properties
    {
        _BaseColorMap("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.30, 0.50, 0.20, 1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _WindStrength("Wind Strength", Range(0.0, 1.0)) = 0.15
        _WindSpeed("Wind Speed", Float) = 1.5
        _Translucency("Translucency", Range(0.0, 1.0)) = 0.35
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Pass
        {
            Name "GroundDecorSolidForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // Глобально: солнце/ambient ставит SkyEnvironment, время — PlanetSurfaceRenderer.
            float3 _TerrainSunDir;
            float _NightAmbient;
            float _SkyAmbient;
            float _TerrainSun;
            float _GroundDecorTime;

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _WindStrength;
            float _WindSpeed;
            float _Translucency;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Ветер: гнём верх меша (локальный Y) в ЛОКАЛЬНЫХ осях XZ.
                // Мировые XZ на планете не горизонтальны — из-за этого трава
                // «летала» вверх-вниз и уходила под землю на склонах.
                // Vertex color.r — маска ветра: 1 листва, 0 ствол (деревья).
                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.z * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.r;
                positionOS.x += sin(phase) * bend;
                positionOS.z += cos(phase * 0.83) * bend;

                float3 positionWS = TransformObjectToWorld(positionOS);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 albedo = tex2D(_BaseColorMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);

                float3 n = normalize(input.normalWS);
                float3 l = normalize(_TerrainSunDir);
                float ndl = saturate(dot(n, l));
                // Аппроксимация подповерхностного рассеяния: свет проходит сквозь лист.
                float back = saturate(dot(-n, l)) * _Translucency;

                float3 ambient = (_NightAmbient + _SkyAmbient) * albedo.rgb;
                float3 direct = albedo.rgb * (_TerrainSun * (ndl + back));
                return float4(ambient + direct, 1.0);
            }
            ENDHLSL
        }

        // Глубина для depth-prepass HDRP: прозрачные шейдеры (атмосфера, звёзды,
        // диск солнца) читают depth pyramid, и без этого пасса их IsSky() считает
        // пиксели деревьев/травы небом — атмосфера просвечивает сквозь них.
        // Alpha-clip обязателен: иначе вырезанные пиксели пишут глубину и
        // закрывают небо (чёрные квадраты).
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
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _GroundDecorTime;
            float _WindStrength;
            float _WindSpeed;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Ветер — тот же, что в forward-пассе: иначе глубины двух
                // проходов расходятся (forward смещён, depth нет) → дыры и
                // чёрные экранные артефакты, ползущие вместе с ветром.
                // color.r — маска ветра (1 листва, 0 ствол).
                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.z * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.r;
                positionOS.x += sin(phase) * bend;
                positionOS.z += cos(phase * 0.83) * bend;

                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 albedo = tex2D(_BaseColorMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
