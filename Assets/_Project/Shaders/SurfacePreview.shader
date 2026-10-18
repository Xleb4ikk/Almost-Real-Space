Shader "Galilego/SurfacePreview"
{
    Properties
    {
        _PreviewSunDir("Sun Direction", Vector) = (0.55, 0.45, 0.70, 0)
        _PreviewSunColor("Sun Color", Color) = (1.0, 0.97, 0.92, 1)
        _PreviewAmbient("Ambient", Color) = (0.22, 0.27, 0.34, 1)
        _PreviewExposure("Exposure", Range(0.1, 8.0)) = 1.6
        _ContourSpacing("Contour Spacing meters, 0 = off", Float) = 0
        _ContourStrength("Contour Strength", Range(0.0, 1.0)) = 0.35
        _SlopeShade("Slope Shade", Range(0.0, 1.0)) = 0.55
        _UnderwaterTint("Underwater Tint", Color) = (0.16, 0.34, 0.52, 1)
        _PreviewSeaLevel("Sea Level meters", Float) = -1000000000
        // Scale grid over the frame's local XZ (north/east). Without it the
        // preview is just a green dome: you can't tell whether 10 is metres or a
        // kilometre, and there is no reference for placing the base.
        _PreviewGridSpacing("Grid Spacing meters, 0 = off", Float) = 0
        _PreviewGridMinor("Grid Minor Fraction", Range(0.05, 1.0)) = 0.2
        _PreviewGridStrength("Grid Strength", Range(0.0, 1.0)) = 0.5
        _PreviewGridFade("Grid Fade Distance, cell units", Float) = 900
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "SurfacePreviewForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            float4 _PreviewSunDir;
            float4 _PreviewSunColor;
            float4 _PreviewAmbient;
            float _PreviewExposure;
            float _ContourSpacing;
            float _ContourStrength;
            float _SlopeShade;
            float4 _UnderwaterTint;
            float _PreviewSeaLevel;
            float _PreviewGridSpacing;
            float _PreviewGridMinor;
            float _PreviewGridStrength;
            float _PreviewGridFade;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 extra      : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 localPos   : TEXCOORD3;
                float4 color      : COLOR;
                float4 extra      : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.localPos = input.positionOS;
                output.color = input.color;
                output.extra = input.extra;
                return output;
            }

            // Grid line by screen width: constant thickness in pixels, otherwise
            // at a large scale each line is half the screen, and at a small one it
            // disappears into noise.
            float GridLine(float coord, float widthPixels)
            {
                float d = abs(frac(coord - 0.5) - 0.5) / max(fwidth(coord), 1e-6);
                return 1.0 - smoothstep(0.0, widthPixels, d);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 l = normalize(_PreviewSunDir.xyz);

                // True Lambert + a small wrap. Half-Lambert (dot*0.5+0.5) flattened
                // slopes: at a 9° incline the lighting difference was only 8 %, so
                // terrain with 92 m over 600 m read as a flat tabletop. Wrap is kept
                // small so shadowed slopes don't crush to black.
                float ndl = saturate(dot(n, l));
                float wrap = saturate(dot(n, l) * 0.5 + 0.5);
                float direct = (ndl * 0.85) + (wrap * 0.15);
                float3 albedo = input.color.rgb;

                // Slope: darken shallow, brighten steep — terrain reads
                // even on a uniform green preset.
                float slope = saturate(1.0 - input.extra.y);
                albedo *= lerp(1.0, 0.45, saturate(slope * _SlopeShade));

                float3 color = albedo * (_PreviewAmbient.rgb + (_PreviewSunColor.rgb * direct));

                // Underwater: tint blue so the land/sea boundary reads in the
                // preview, otherwise you see the bottom instead of the ocean.
                float sea = step(input.extra.x, _PreviewSeaLevel);
                color = lerp(color, color * _UnderwaterTint.rgb, sea * 0.75);

                if (_ContourSpacing > 0.0)
                {
                    // Contours: distance to the nearest band in step units, line
                    // thickness is one pixel (fwidth). The name is NOT "line": that is an
                    // HLSL reserved word (interpolation modifier).
                    float band = input.extra.x / _ContourSpacing;
                    float toLine = abs(frac(band) - 0.5);
                    float contour = 1.0 - smoothstep(0.0, max(fwidth(band), 1e-5), toLine);
                    color = lerp(color, color * 0.55, contour * _ContourStrength);
                }

                // Scale grid: mesh-local XZ are the frame's north/east in metres
                // (the mesh is built in frame axes, not world). Fades towards the
                // horizon, otherwise it becomes a sea of lines.
                if (_PreviewGridSpacing > 0.0)
                {
                    float2 km = input.localPos.xz * 0.001;
                    float major = GridLine(km.x / _PreviewGridSpacing, 1.4)
                                + GridLine(km.y / _PreviewGridSpacing, 1.4);
                    float2 cell = km / (_PreviewGridSpacing * _PreviewGridMinor);
                    float minor = GridLine(cell.x, 1.0) + GridLine(cell.y, 1.0);
                    float fade = saturate(1.0 - length(km) / max(_PreviewGridFade, 1e-3));
                    fade *= fade;
                    float g = saturate((major + minor * 0.45) * fade) * _PreviewGridStrength;
                    color = lerp(color, color * 0.4, saturate(g));
                }

                return float4(color * _PreviewExposure, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct DepthAttributes
            {
                float3 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            float4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
