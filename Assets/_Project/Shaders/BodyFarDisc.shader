Shader "Galilego/BodyFarDisc"
{
    // Billboarding stand-in for a solid body that is OUTSIDE the camera's far plane.
    // The terrain camera clips at the physical horizon (~300 km from the ground on
    // Terra), so a moon at 83 000 km is never drawn by its own chunks; this disc
    // keeps the same angular size and the correct phase, like GasGiantView's far disc.
    Properties
    {
        [HideInInspector] _BFD_Albedo("Albedo", Color) = (0.5, 0.49, 0.47, 1)
        [HideInInspector] _BFD_SunDirWS("Sun Direction", Vector) = (1, 0, 0, 0)
        [HideInInspector] _BFD_RightWS("Screen Right", Vector) = (1, 0, 0, 0)
        [HideInInspector] _BFD_UpWS("Screen Up", Vector) = (0, 1, 0, 0)
        [HideInInspector] _BFD_ViewWS("View Direction", Vector) = (0, 0, 1, 0)
        [HideInInspector] _BFD_IntensityScale("Intensity Scale", Float) = 1
        [HideInInspector] _BFD_DiscDistance("Disc Distance", Float) = 1000
        [HideInInspector] _BFD_LimbDarkening("Limb Darkening", Float) = 0.35
        [HideInInspector] _BFD_Ambient("Ambient", Float) = 0.015
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+4" }

        Pass
        {
            Name "BodyFarDiscForward"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            float4 _BFD_Albedo;
            float4 _BFD_SunDirWS;
            float4 _BFD_RightWS;
            float4 _BFD_UpWS;
            float4 _BFD_ViewWS;
            float _BFD_IntensityScale;
            float _BFD_DiscDistance;
            float _BFD_LimbDarkening;
            float _BFD_Ambient;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 centered = (input.uv * 2.0) - 1.0;
                float r2 = dot(centered, centered);
                if (r2 > 1.0)
                {
                    discard;
                }

                // Sphere normal in world space: the quad is camera-facing, so the
                // sphere is reconstructed from the screen basis and the disc normal.
                float nz = sqrt(max(0.0, 1.0 - r2));
                float3 normalWS = normalize(
                    (_BFD_RightWS.xyz * centered.x) + (_BFD_UpWS.xyz * centered.y) + (_BFD_ViewWS.xyz * nz));

                float ndl = saturate(dot(normalWS, _BFD_SunDirWS.xyz));
                float limb = 1.0 - (_BFD_LimbDarkening * (1.0 - nz));
                float3 color = _BFD_Albedo.rgb * (ndl + _BFD_Ambient) * limb * _BFD_IntensityScale;

                // One-pixel feather: a 6 px disc stepped hard reads as a square.
                float edge = max(1e-5, fwidth(r2) * 1.5);
                float alpha = saturate((1.0 - r2) / edge);

                return float4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
