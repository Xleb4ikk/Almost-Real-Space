Shader "Galilego/GroundDecorBillboard"
{
    Properties
    {
        _BaseColorMap("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.30, 0.50, 0.20, 1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Translucency("Translucency", Range(0.0, 1.0)) = 0.35
        // Домножает альбедо к тону земли (окклюзия/моттлинг/текстуры рельефа).
        _GroundTint("Ground Match Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Pass
        {
            Name "GroundDecorBillboardForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoLighting.hlsl"

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _Translucency;
            float4 _GroundTint;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
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

                // Биллборд разворачивает РЕНДЕРЕР (C#): матрица инстанса уже
                // смотрит в камеру вокруг нормали поверхности. Шейдер — простой
                // passthrough: никакой шейдерной ориентации (та ломалась под
                // HDRP-инстансингом и ложила траву плашмя).
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 tex = tex2D(_BaseColorMap, input.uv);
                float alpha = tex.a * _BaseColor.a;
                // Край листа в текстуре тёмный (RGB на низкой альфе уходит в
                // чёрное) — именно это читалось «чёрной обводкой» вокруг травы.
                // Подмешиваем базовый цвет по альфе: пиксели у самой границы
                // клипа получают цвет травы, а не грязь текстуры.
                float3 albedo = tex.rgb * _BaseColor.rgb;
                albedo = lerp(_BaseColor.rgb, albedo, saturate(alpha * 1.6));
                clip(alpha - _Cutoff);

                float3 n = normalize(input.normalWS);
                float3 l = normalize(_TerrainSunDir);
                // Двусторонний лист: светим с обеих сторон.
                float ndl = saturate(abs(dot(n, l)));
                // Просвет против солнца — как у solid-слоя, иначе дальняя
                // трава в контровом свете темнее ближней.
                float back = saturate(dot(-n, l)) * _Translucency;
                // Свет — как у рельефа: честное солнце (_TerrainSun, может
                // превышать 1), ambient — полусферический от неба (без *ndl:
                // на закате небо светит, даже когда прямой луч погас); цвет
                // солнца (фотосфера × T) и ambient — общие глобалы.
                // Тень HDRP гасит прямой свет, ambient остаётся.
                 float shadow = GalilegoSunShadow(input.positionCS.xy, input.positionWS, n, l);
                 float cloudShadow = SampleCloudShadow(input.positionWS);
                 float sun = _TerrainSun;
                float3 light = float3(_NightAmbient, _NightAmbient, _NightAmbient)
                    + (GalilegoSkyAmbient(n) * _TerrainRadianceScale)
                     + (_SunLightColor * (sun * ndl * shadow * cloudShadow) * _TerrainRadianceScale)
                     + (_SunLightColor * (sun * back * shadow * cloudShadow) * _TerrainRadianceScale);
                return float4(albedo * light * _GroundTint.rgb, 1.0);
            }
            ENDHLSL
        }

        // Глубина для depth-prepass HDRP (см. GroundDecorSolid): без пасса
        // биллборды на фоне неба выглядят полупрозрачными.
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

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
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
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Alpha-clip обязателен и здесь (см. GroundDecorSolid).
                float4 albedo = tex2D(_BaseColorMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
