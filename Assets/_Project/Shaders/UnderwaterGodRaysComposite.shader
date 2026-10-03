Shader "Galilego/UnderwaterGodRaysComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "UnderwaterGodRaysComposite"
            ZWrite Off
            ZTest Always
            Blend One One
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FullScreenPass
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

            float4 FullScreenPass(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.positionCS.xy * _ScreenSize.zw;
                float4 customColor = CustomPassSampleCustomColor(uv);
                return float4(customColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
