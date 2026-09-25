Shader "Galilego/UnderwaterSunRays"
{
    Properties
    {
        _RayColor("Ray Color", Color) = (0.35, 0.8, 1.0, 1)
        _RayStrength("Ray Strength", Float) = 1
        _RaySurfaceFade("Surface Fade", Range(0.0, 1.0)) = 1
        _RayTime("Ray Time", Float) = 0
        _RayNoiseScale("Noise Scale", Float) = 0.75
        _RayNoiseSpeed("Noise Speed", Float) = 0.8
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+180" }
        Pass
        {
            Name "UnderwaterSunRaysForward"
            Tags { "LightMode" = "ForwardOnly" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoCloudField.hlsl"

            float4 _RayColor;
            float _RayStrength;
            float _RaySurfaceFade;
            float _RayTime;
            float _RayNoiseScale;
            float _RayNoiseSpeed;
            float3 _UnderwaterRayCameraPos;
            float4x4 _UnderwaterRayWorldToBody;
            float3 _UnderwaterRayBodyAnchor;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float edge = 1.0 - smoothstep(0.68, 1.0, abs((input.uv.x * 2.0) - 1.0));
                float ends = smoothstep(0.0, 0.12, input.uv.y) * (1.0 - smoothstep(0.72, 1.0, input.uv.y));
                float3 bodyFixed = mul((float3x3)_UnderwaterRayWorldToBody, input.positionWS - _UnderwaterRayCameraPos) + _UnderwaterRayBodyAnchor;
                float3 p = bodyFixed * _RayNoiseScale;
                p += float3(0.0, _RayTime * _RayNoiseSpeed, 0.0);
                float n1 = CloudFbm(p);
                float n2 = CloudFbm((p * 1.73) + float3(11.3, 7.1, 4.7));
                float cloud = SampleCloudShadow(input.positionWS);
                float mask = saturate(0.28 + (0.72 * pow(saturate((n1 * 0.65) + (n2 * 0.35)), 2.5)));
                float3 color = _RayColor.rgb * _RayStrength * _RaySurfaceFade * mask * edge * ends * cloud;
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
