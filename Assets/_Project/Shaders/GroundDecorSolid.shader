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
        // Тинт альбедо vertex-цветом (ствол/листва деревьев). 0 — прочие
        // слои декора (трава, камни) рисуются как раньше.
        _VertexColorTint("Vertex Color Tint", Range(0.0, 1.0)) = 0.0
        // Двусторонний лист: свет считаем как abs(N·L) — у плоских травинок
        // нормаль смотрит вбок, и односторонний ndl делал половину клинков
        // чёрной («обводка» на ковре). Деревьям нужен обычный односторонний
        // свет, поэтому это свойство материала, а не глобальная правка.
        _TwoSided("Two Sided Lighting", Range(0.0, 1.0)) = 0.0
        // Домножает альбедо декора к тону земли: рельеф дополнительно темнеет
        // окклюзией/моттлингом и текстурами, без этого трава светлее земли.
        _GroundTint("Ground Match Tint", Color) = (1, 1, 1, 1)
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
            #pragma shader_feature_local _DECOR_INDIRECT_MATRICES
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoLighting.hlsl"
#if defined(_DECOR_INDIRECT_MATRICES)
            // Идентификаторы индирект-рисования (RenderMeshIndirect): без этого
            // SV_InstanceID на DX12 не заполняется и матрицы читаются мусором.
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
#endif

            // Время — ставит PlanetSurfaceRenderer.
            float _GroundDecorTime;

            // Индирект-путь травы: матрицы инстансов (чанк-фрейм) в буфере,
            // чанк→мир — через MaterialPropertyBlock. Включается ключом
            // _DECOR_INDIRECT_MATRICES на материале травы; остальные слои декора
            // идут обычным инстансингом (UNITY_MATRIX_M).
            float4x4 _DecorChunkToWorld;
            StructuredBuffer<float4x4> _DecorMatrices;

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _WindStrength;
            float _WindSpeed;
            float _Translucency;
            float _VertexColorTint;
            float _TwoSided;
            float4 _GroundTint;

            float4x4 DecorInstanceMatrix(uint instanceId)
            {
                // Данные в буфере — сырой Matrix4x4 (column-major) → HLSL читает
                // их как транспонированные, поэтому transpose.
                return mul(_DecorChunkToWorld, transpose(_DecorMatrices[instanceId]));
            }

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
#if defined(_DECOR_INDIRECT_MATRICES) && !defined(UNITY_INSTANCING_ENABLED)
                uint decorInstanceId : SV_InstanceID;
#endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 color      : COLOR;
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
                // Vertex color.a — маска ветра: 1 листва, 0 ствол (деревья).
                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.y * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.a;
                float gust = 0.55 + (0.45 * sin(phase));
                positionOS.z += bend * gust;

                float3 positionWS;
                float3 normalWS;
#if defined(_DECOR_INDIRECT_MATRICES) && defined(UNITY_INSTANCING_ENABLED)
                float4x4 instanceMatrix = DecorInstanceMatrix(input.instanceID);
                positionWS = mul(instanceMatrix, float4(positionOS, 1.0)).xyz;
                normalWS = mul((float3x3)instanceMatrix, input.normalOS);
#elif defined(_DECOR_INDIRECT_MATRICES)
                InitIndirectDrawArgs(0);
                uint decorId = GetIndirectInstanceID(globalIndirectDrawArgs, input.decorInstanceId);
                float4x4 instanceMatrix = DecorInstanceMatrix(decorId);
                positionWS = mul(instanceMatrix, float4(positionOS, 1.0)).xyz;
                normalWS = mul((float3x3)instanceMatrix, input.normalOS);
#else
                positionWS = TransformObjectToWorld(positionOS);
                normalWS = TransformObjectToWorldNormal(input.normalOS);
#endif
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 tex = tex2D(_BaseColorMap, input.uv);
                float alpha = tex.a * _BaseColor.a;
                // Край листа текстуры: RGB на низкой альфе уходит в чёрное —
                // это читалось «чёрной обводкой» вокруг травы/карточек.
                // Подмешиваем базовый цвет по альфе (у непрозрачных материалов
                // с белой текстурой результат не меняется).
                float3 albedoRgb = tex.rgb * _BaseColor.rgb;
                albedoRgb = lerp(_BaseColor.rgb, albedoRgb, saturate(alpha * 1.6));
                float4 albedo = float4(albedoRgb, alpha);
                // Vertex RGB — цвет части меша (деревья: кора/листва).
                albedo.rgb *= lerp(float3(1.0, 1.0, 1.0), input.color.rgb, _VertexColorTint);
                clip(alpha - _Cutoff);

                float3 n = normalize(input.normalWS);
                float3 l = normalize(_TerrainSunDir);
                // Нормаль клинка совпадает с нормалью поверхности (нормали меша
                // запечены вдоль роста), поэтому ndl берём как у земли.
                // _TwoSided оставлен как опция: 0 — ровно земная модель.
                float oneSided = saturate(dot(n, l));
                float twoSided = saturate(abs(dot(n, l)));
                float ndl = lerp(oneSided, twoSided, saturate(_TwoSided));
                float back = saturate(dot(-n, l)) * _Translucency * lerp(1.0, 0.25, saturate(_TwoSided));

                // Тень HDRP (PCSS/PCF): гасит прямой свет, ambient остаётся.
                 float shadow = GalilegoSunShadow(input.positionCS.xy, input.positionWS, n, l);
                 float cloudShadow = SampleCloudShadow(input.positionWS);

                 // Свет — РОВНО как у рельефа (PlanetSurface.shader): честное
                // солнце (_TerrainSun, может превышать 1), ambient —
                // полусферический от неба (см. GalilegoSkyAmbient: без *ndl,
                // иначе закат чёрный); цвет солнца (фотосфера × T) и ambient —
                // общие глобалы. Плюс просвет листвы против солнца (back):
                // в контровом свете крона тёпло светится насквозь,
                // а не чёрным силуэтом.
                float sun = _TerrainSun;
                float3 light = float3(_NightAmbient, _NightAmbient, _NightAmbient)
                    + (GalilegoSkyAmbient(n) * _TerrainRadianceScale)
                     + (_SunLightColor * (sun * ndl * shadow * cloudShadow) * _TerrainRadianceScale)
                     + (_SunLightColor * (sun * back * shadow * cloudShadow) * _TerrainRadianceScale);
                return float4(albedo.rgb * light * _GroundTint.rgb, 1.0);
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
            #pragma shader_feature_local _DECOR_INDIRECT_MATRICES
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#if defined(_DECOR_INDIRECT_MATRICES)
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
#endif

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _GroundDecorTime;
            float _WindStrength;
            float _WindSpeed;

            float4x4 _DecorChunkToWorld;
            StructuredBuffer<float4x4> _DecorMatrices;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
#if defined(_DECOR_INDIRECT_MATRICES) && !defined(UNITY_INSTANCING_ENABLED)
                uint decorInstanceId : SV_InstanceID;
#endif
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
                // color.a — маска ветра (1 листва, 0 ствол).
                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.y * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.a;
                float gust = 0.55 + (0.45 * sin(phase));
                positionOS.z += bend * gust;

#if defined(_DECOR_INDIRECT_MATRICES) && defined(UNITY_INSTANCING_ENABLED)
                float4x4 instanceMatrix = mul(_DecorChunkToWorld, _DecorMatrices[input.instanceID]);
                output.positionCS = TransformWorldToHClip(mul(instanceMatrix, float4(positionOS, 1.0)).xyz);
#elif defined(_DECOR_INDIRECT_MATRICES)
                InitIndirectDrawArgs(0);
                uint decorId = GetIndirectInstanceID(globalIndirectDrawArgs, input.decorInstanceId);
                float4x4 instanceMatrix = mul(_DecorChunkToWorld, transpose(_DecorMatrices[decorId]));
                output.positionCS = TransformWorldToHClip(mul(instanceMatrix, float4(positionOS, 1.0)).xyz);
#else
                output.positionCS = TransformObjectToHClip(positionOS);
#endif
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

        // Тени: этим пассом HDRP рисует кастеров в shadow map. Альфа-клип и
        // ветер — ровно как в forward: иначе силуэт тени не совпадает с
        // геометрией и «дрожит» относительно неё.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma shader_feature_local _DECOR_INDIRECT_MATRICES
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#if defined(_DECOR_INDIRECT_MATRICES)
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
#endif

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _GroundDecorTime;
            float _WindStrength;
            float _WindSpeed;

            float4x4 _DecorChunkToWorld;
            StructuredBuffer<float4x4> _DecorMatrices;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
#if defined(_DECOR_INDIRECT_MATRICES) && !defined(UNITY_INSTANCING_ENABLED)
                uint decorInstanceId : SV_InstanceID;
#endif
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

                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.y * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.a;
                float gust = 0.55 + (0.45 * sin(phase));
                positionOS.z += bend * gust;

#if defined(_DECOR_INDIRECT_MATRICES) && defined(UNITY_INSTANCING_ENABLED)
                float4x4 instanceMatrix = mul(_DecorChunkToWorld, _DecorMatrices[input.instanceID]);
                output.positionCS = TransformWorldToHClip(mul(instanceMatrix, float4(positionOS, 1.0)).xyz);
#elif defined(_DECOR_INDIRECT_MATRICES)
                InitIndirectDrawArgs(0);
                uint decorId = GetIndirectInstanceID(globalIndirectDrawArgs, input.decorInstanceId);
                float4x4 instanceMatrix = mul(_DecorChunkToWorld, transpose(_DecorMatrices[decorId]));
                output.positionCS = TransformWorldToHClip(mul(instanceMatrix, float4(positionOS, 1.0)).xyz);
#else
                output.positionCS = TransformObjectToHClip(positionOS);
#endif
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
