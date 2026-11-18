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
            // The discard below does the occlusion by hand. A hardware ZTest would
            // reject the disc behind ANY nearer surface — including the observer's
            // own craft, whose dark side is invisible against space — so the star
            // died exactly when it drifted behind the ship.
            ZTest Always
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
            // Set by SunBillboard: clearance (m) around the eye where solid geometry
            // is the observer's own craft, not the world. Inside that radius the disc
            // is kept — an EVA hull is a black silhouette against space and would
            // otherwise swallow the star without showing anything.
            float _SunSelfOccluder;
            // Far plane (m), from SunBillboard. A depth at/over it is "no geometry",
            // not an occluder: the test never relies on IsSky alone.
            float _SunFarLimit;

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

                // Occlusion by the scene depth, with a craft-sized hole around the eye.
                // Everything farther than _SunSelfOccluder is the real world (the planet,
                // the terrain, buildings) and hides the star; everything nearer is the
                // observer's own craft — the hull, the hatch frame, the canopy — which
                // must not. A depth at the far plane is empty space, not an occluder, so
                // the test does not depend on IsSky() alone. In a cockpit SunBillboard
                // publishes 0: the cabin wall still hides the sun and only the glass
                // (transparent, no depth) lets it through.
                float rawDepth = LoadCameraDepth(uint2(input.positionCS.xy));
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                if (!IsSky(rawDepth)
                    && eyeDepth > max(_SunSelfOccluder, 0.05)
                    && eyeDepth < _SunFarLimit)
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
