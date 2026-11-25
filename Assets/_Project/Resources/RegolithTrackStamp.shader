Shader "Galilego/RegolithTrackStamp"
{
    // Writes one footprint / rut / landing blast into the RG16F track map.
    //
    // R = depression depth (0 = untouched, accumulates),
    // G = compaction mask   (0 = untouched, accumulates; the terrain shader
    //                        darkens the compacted ring).
    //
    // Drawn by RegolithTracks through a CommandBuffer: a small quad around the
    // stamp centre, NOT a full-screen pass, so a footprint costs a few hundred
    // fragments and a 55 m landing blast a few hundred thousand.
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "RegolithTrackStamp"
            Blend One One
            BlendOp Add
            ZWrite Off
            ZTest Always
            Cull Off
            ColorMask RG

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            // xy = UV centre, z = quad half-size in UV, w = cos(yaw)
            float4 _StampCenter;
            // x = sin(yaw), y = depth fraction, z = compaction mask, w = kind
            // (0 boot, 1 wheel, 2 landing blast)
            float4 _StampParams;

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 p : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float2 p = input.positionOS.xy;              // -1..1
                float c = _StampCenter.w;
                float s = _StampParams.x;
                float2 rotated = float2((p.x * c) - (p.y * s), (p.x * s) + (p.y * c));
                float2 uv = _StampCenter.xy + (rotated * _StampCenter.z);
                output.positionCS = float4((uv * 2.0) - 1.0, 0.0, 1.0);
                output.p = p;
                return output;
            }

            float BootShape(float2 p)
            {
                // The local +y axis is the walking direction; x is across the sole.
                // Keep the sole wide enough to cover 2-3 texels at 6.25 cm/texel:
                // a narrower boot misses every pixel centre on a 5-texel quad and
                // the print is simply not rasterised.
                float2 sole = float2(p.x * 1.6, p.y);
                float2 heel = float2(p.x * 1.6, (p.y + 0.52) * 1.5);
                float toe = 1.0 - smoothstep(0.72, 0.98, length(sole));
                float back = 1.0 - smoothstep(0.55, 0.88, length(heel));
                return saturate(max(toe, back * 0.92));
            }

            float WheelShape(float2 p)
            {
                float across = abs(p.x);
                float along = abs(p.y);
                float edge = 1.0 - smoothstep(0.72, 0.95, across);
                float ends = 1.0 - smoothstep(0.82, 1.0, along);
                // Tread bars: a rover tyre leaves a chevron, and a rut without them
                // reads as a painted stripe, not a wheel.
                float tread = 0.68 + 0.32 * saturate(sin((p.y * 34.0) + (p.x * 10.0)));
                return saturate(edge * ends * tread);
            }

            float BlastShape(float2 p)
            {
                // Flat fan footprint of the exhaust: a wide, very shallow disc with a
                // soft edge. No crater, no rim — the loose layer was blown away and
                // the ground below is compacted.
                float r = length(p);
                return saturate(1.0 - smoothstep(0.35, 1.0, r));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float c = _StampCenter.w;
                float s = _StampParams.x;
                float2 p = float2(
                    (input.p.x * c) + (input.p.y * s),
                    (-input.p.x * s) + (input.p.y * c));

                float kind = _StampParams.w;
                float shape;
                if (kind < 0.5)
                {
                    shape = BootShape(p);
                }
                else if (kind < 1.5)
                {
                    shape = WheelShape(p);
                }
                else
                {
                    shape = BlastShape(p);
                }

                return float4(shape * _StampParams.y, shape * _StampParams.z, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
