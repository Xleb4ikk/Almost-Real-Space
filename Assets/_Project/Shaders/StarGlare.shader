Shader "Galilego/StarGlare"
{
    Properties
    {
        _GlareColor("Glare Color (linear)", Color) = (1, 1, 1, 1)
        _GlareAmplitude("Peak amplitude (scene radiance)", Float) = 0
        _GlareTheta0("Moffat core θ0, rad", Float) = 0.0026
        _GlareBeta("Moffat beta", Float) = 1.8
        _GlareExtent("Quad angular extent, rad", Float) = 0.14
        _GlareRayStrength("Diffraction rays strength", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+3" }

        Pass
        {
            Name "StarGlareForward"
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
            #include "GalilegoStarOcclusion.hlsl"

            float4 _GlareColor;
            float _GlareAmplitude;
            float _GlareTheta0;
            float _GlareBeta;
            float _GlareExtent;
            float _GlareRayStrength;

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionOS = input.positionOS;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                if (_GlareAmplitude <= 0.0)
                {
                    discard;
                }

                float2 p = input.positionOS.xy * 2.0;
                float r = length(p);
                if (r >= 1.0)
                {
                    discard;
                }

                // The halo is scene radiance, not a lens artefact: an opaque
                // hull in front of the star hides it, glare included. Without
                // this the additive quad painted a full halo through the ship.
                if (StarPixelOccluded(uint2(input.positionCS.xy)))
                {
                    discard;
                }

                float theta = r * _GlareExtent;
                float q = theta / max(_GlareTheta0, 1e-6);
                float norm = (_GlareBeta - 1.0) / (3.141592653589793 * _GlareTheta0 * _GlareTheta0);
                float moffat = norm * pow(1.0 + q * q, -_GlareBeta);
                float fade = 1.0 - smoothstep(0.82, 1.0, r);

                float3 I = _GlareColor.rgb * (_GlareAmplitude * moffat * fade);

                // Optional weak four-ray diffraction cross (off by default).
                if (_GlareRayStrength > 0.0)
                {
                    float peak = _GlareAmplitude * norm;
                    float angle = atan2(p.y, p.x);
                    float rays = pow(abs(cos(2.0 * angle)), 40.0) * exp(-r * 5.0);
                    I += _GlareColor.rgb * (peak * _GlareRayStrength * rays * fade);
                }

                return float4(I, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
