// Silhouette outline for interactable objects (HDRP Custom Pass, BeforePostProcess).
//
// Pass 0 (InteractMask): draws the selected renderers into a mask (R=1) with the
//   camera depth test - parts hidden behind a wall do not enter the mask.
// Pass 1 (InteractOutline): builds a line of constant pixel width from the mask
//   (sample rings -> distance to the silhouette, so the line follows any corners and
//   protrusions instead of being a scaled copy) plus a soft glow outside.
Shader "Galilego/InteractOutline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "InteractMask"
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex VertMask
            #pragma fragment FragMask
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct MaskAttributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            MaskVaryings VertMask(MaskAttributes input)
            {
                MaskVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                // Nudge towards the camera (relative, 0.03% of depth): otherwise the object
                // z-fights with itself and the mask flickers.
                #if UNITY_REVERSED_Z
                    output.positionCS.z *= 1.0003;
                #else
                    output.positionCS.z *= 0.9997;
                #endif
                return output;
            }

            float4 FragMask(MaskVaryings input) : SV_Target
            {
                return float4(1.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "InteractOutline"
            ZWrite Off
            ZTest Always
            Cull Off
            // Premultiplied alpha: the line covers the frame, the glow only adds light.
            Blend One OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOutline
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

            float4 _InteractColor;   // rgb = color * brightness (linear, HDR), a = overall visibility (fade)
            float4 _InteractParams;  // x = line width, px; y = glow radius, px; z = glow strength; w = time, s

            float MaskAt(float2 uv)
            {
                return CustomPassSampleCustomColor(uv).r;
            }

            // Ring of 10 samples: x = fraction of samples in the mask (soft), y = maximum (is any mask on the ring).
            float2 Ring(float2 uv, float radiusPx, float phase)
            {
                float2 px = _ScreenSize.zw;
                float sum = 0.0;
                float mx = 0.0;
                [unroll]
                for (int d = 0; d < 10; d++)
                {
                    float a = phase + (d * 0.62831853);
                    float m = MaskAt(uv + (float2(cos(a), sin(a)) * radiusPx * px));
                    sum += m;
                    mx = max(mx, m);
                }

                return float2(sum * 0.1, mx);
            }

            float4 FragOutline(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.positionCS.xy * _ScreenSize.zw;

                float center = MaskAt(uv);
                if (center > 0.99)
                {
                    return float4(0.0, 0.0, 0.0, 0.0); // no line inside the object
                }

                float w = max(_InteractParams.x, 0.5);
                float g = max(_InteractParams.y, 1.0);

                // Line: distance to the silhouette via rings; the outer ring gives a soft edge.
                float lineA = Ring(uv, w * 0.45, 0.10).y;
                lineA = max(lineA, Ring(uv, w * 0.90, 0.37).y);
                lineA = max(lineA, Ring(uv, w * 1.35, 0.71).y * 0.40);

                // Glow: weighted coverage on three rings - a smooth gradient.
                float glow = (Ring(uv, g * 0.40, 0.23).x * 0.50)
                    + (Ring(uv, g * 0.75, 0.52).x * 0.30)
                    + (Ring(uv, g * 1.10, 0.91).x * 0.20);
                float pulse = 1.0 + (0.10 * sin(_InteractParams.w * 3.0));
                glow = saturate(glow * _InteractParams.z * pulse);

                float outside = 1.0 - center;
                float coreA = lineA * outside;
                float glowA = glow * outside;
                float fade = _InteractColor.a;

                return float4(_InteractColor.rgb * (coreA + (glowA * 0.6)) * fade, coreA * fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
