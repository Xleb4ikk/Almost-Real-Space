Shader "Galilego/GroundDecorBlob"
{
    Properties
    {
        _Color("Shadow Color", Color) = (0.035, 0.045, 0.03, 1)
        _Cutoff("Radius", Range(0.05, 0.95)) = 0.62
        _Softness("Edge Softness", Range(0.0, 1.0)) = 0.45
        _Falloff("Falloff", Range(0.5, 4.0)) = 1.4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Pass
        {
            Name "GroundDecorBlobForward"
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

            // Ставит SkyEnvironment: видимость звёзд (1 ночью, 0 днём). Ночью
            // блоб гаснет полностью (mask → 0 и весь квад clip'ается).
            float _StarVisibility;

            float4 _Color;
            float _Cutoff;
            float _Softness;
            float _Falloff;

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
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Радиальное затухание по UV квада: форма пятна без текстуры.
                float2 centered = (input.uv * 2.0) - 1.0;
                float radial = saturate(1.0 - length(centered));
                float mask = pow(radial, _Falloff) * saturate(1.0 - _StarVisibility);

                // Opaque alpha-test (не blend): рендерится тем же проходом, что
                // и сама трава, — надёжно в HDRP. Край «смягчаем» дизером порога;
                // паттерн в UV квада (в мире), а не в экране — не «плавает».
                float dither = InterleavedGradientNoise(input.uv * 256.0, 0);
                float threshold = lerp(_Cutoff, dither, _Softness);
                clip(mask - threshold);
                return float4(_Color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
