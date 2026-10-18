Shader "Galilego/SunDisc"
{
    Properties
    {
        _SunColor("Sun Color", Color) = (1, 1, 1, 1)
        _CoreRadius("Core Radius", Range(0, 1)) = 0.5
        _GlowFalloff("Glow Falloff", Range(0.5, 8)) = 3.0
    }
    SubShader
    {
        // Transparent+2: drawn after the atmosphere (its manual composite replaces
        // the background) so the sun disc is not overwritten.
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+2" }

        Pass
        {
            Name "SunDiscForward"
            Tags { "LightMode" = "ForwardOnly" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            float4 _SunColor;
            float _CoreRadius;
            float _GlowFalloff;
            // Set by SkyEnvironment: transmittance to the sun (space → 1, sunset → red).
            float3 _SunTransmittance;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Radial falloff: core + soft corona; outside the circle it is
                // transparent (the quad no longer looks like a square).
                float2 centered = (input.uv * 2.0) - 1.0;
                float radius = length(centered);
                float core = 1.0 - smoothstep(_CoreRadius - 0.08, _CoreRadius, radius);
                float glow = pow(saturate(1.0 - radius), _GlowFalloff);
                float alpha = saturate((core + glow) * 0.9);
                if (alpha <= 0.001)
                {
                    discard;
                }

                // Hide the sun behind the planet by depth (not by the disc ZTest,
                // which at large distances ends up in front of the planet).
                if (!IsSky(uint2(input.positionCS.xy)))
                {
                    discard;
                }

                float3 color = _SunColor.rgb * _SunTransmittance * input.color.rgb * (core + (glow * 0.6));
                return float4(color, alpha * _SunColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
