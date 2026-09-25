Shader "Galilego/UnderwaterCaustics"
{
    Properties
    {
        _UnderwaterBodyAnchor("Underwater Body Anchor", Vector) = (0, 0, 0, 0)
        _UnderwaterCameraPos("Underwater Camera Position", Vector) = (0, 0, 0, 0)
        _UnderwaterUp("Underwater Up", Vector) = (0, 1, 0, 0)
        _UnderwaterSunDir("Underwater Sun Direction", Vector) = (0, 1, 0, 0)
        _UnderwaterEyeDepth("Underwater Eye Depth", Float) = 0
        _UnderwaterTime("Underwater Time", Float) = 0
        _UnderwaterCausticColor("Underwater Caustic Color", Color) = (1, 1, 1, 1)
        _UnderwaterCausticStrength("Underwater Caustic Strength", Float) = 0.75
        _UnderwaterCausticScale("Underwater Caustic Scale", Float) = 0.7
        _UnderwaterCausticFadeStart("Underwater Caustic Fade Start", Float) = 3
        _UnderwaterCausticFadeEnd("Underwater Caustic Fade End", Float) = 28
        _UnderwaterEffectWeight("Underwater Effect Weight", Range(0.0, 1.0)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+190" }
        Pass
        {
            Name "UnderwaterCausticsForward"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoUnderwaterCaustics.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float mask = UnderwaterCausticMask(input.positionWS, input.normalWS);
                return float4(_UnderwaterCausticColor.rgb * mask, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
