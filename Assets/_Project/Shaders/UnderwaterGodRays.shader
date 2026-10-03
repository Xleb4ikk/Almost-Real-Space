Shader "Galilego/UnderwaterGodRays"
{
    Properties
    {
        _GodRayColor("God Ray Color", Color) = (1, 1, 1, 1)
        _GodRaySunViewport("Sun Viewport", Vector) = (0.5, 0.5, 0, 0)
        _GodRayStrength("God Ray Strength", Float) = 0.45
        _GodRayDensity("God Ray Density", Float) = 0.72
        _GodRayDecay("God Ray Decay", Float) = 0.92
        _GodRayThreshold("God Ray Threshold", Float) = 0.8
        _GodRayDepthFade("God Ray Depth Fade", Range(0.0, 1.0)) = 1
        _GodRaySamples("God Ray Samples", Range(4, 64)) = 24
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "UnderwaterGodRays"
            ZWrite Off
            ZTest Always
            Blend One One
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FullScreenPass
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

            float4 _GodRayColor;
            float4 _GodRaySunViewport;
            float _GodRayStrength;
            float _GodRayDensity;
            float _GodRayDecay;
            float _GodRayThreshold;
            float _GodRayDepthFade;
            int _GodRaySamples;

            float4 FullScreenPass(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.positionCS.xy * _ScreenSize.zw;
                float2 sun = _GodRaySunViewport.xy;
                float2 delta = (uv - sun) / max(1.0, (float)_GodRaySamples) * _GodRayDensity;
                float2 coord = uv;
                float3 rays = 0.0;
                float decay = 1.0;
                float sourceMask = 1.0 - smoothstep(0.12, 0.85, length(uv - sun));

                for (int i = 0; i < 64; i++)
                {
                    if (i >= _GodRaySamples)
                    {
                        break;
                    }

                    coord -= delta;
                    float2 sampleUv = saturate(coord);
                    float3 sampleColor = CustomPassLoadCameraColor(uint2(sampleUv * max(float2(1.0, 1.0), _ScreenSize.xy - 1.0)), 0);
                    float brightness = max(sampleColor.r, max(sampleColor.g, sampleColor.b));
                    float brightMask = smoothstep(_GodRayThreshold, _GodRayThreshold + 0.75, brightness);
                    rays += sampleColor * brightMask * decay;
                    decay *= _GodRayDecay;
                }

                float3 color = rays * _GodRayColor.rgb * _GodRayStrength * _GodRayDepthFade * sourceMask;
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
