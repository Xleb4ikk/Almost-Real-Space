Shader "Galilego/GroundDecorBillboard"
{
    Properties
    {
        _BaseColorMap("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.30, 0.50, 0.20, 1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Translucency("Translucency", Range(0.0, 1.0)) = 0.35
        // Multiplies albedo toward the ground tone (occlusion/mottling/terrain textures).
        _GroundTint("Ground Match Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Pass
        {
            Name "GroundDecorBillboardForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoLighting.hlsl"
            #include "GalilegoUnderwaterCaustics.hlsl"

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _Translucency;
            float4 _GroundTint;
            float3 _TerrainBodyCenterWS;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // The RENDERER (C#) orients the billboard: the instance matrix already
                // faces the camera around the surface normal. The shader is a plain
                // passthrough with no shader-side orientation.
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 tex = tex2D(_BaseColorMap, input.uv);
                float alpha = tex.a * _BaseColor.a;
                // The leaf edge in the texture is dark (RGB goes black at low alpha).
                // Blend in the base colour by alpha so pixels at the clip edge get grass
                // colour rather than texture dirt.
                float3 albedo = tex.rgb * _BaseColor.rgb;
                albedo = lerp(_BaseColor.rgb, albedo, saturate(alpha * 1.6));
                clip(alpha - _Cutoff);

                float3 n = normalize(input.normalWS);
                float3 l = normalize(_TerrainSunDir);
                // Two-sided leaf: light both sides.
                float ndl = saturate(abs(dot(n, l)));
                // Backlight against the sun — as in the solid layer, otherwise distant
                // grass in backlight is darker than near grass.
                float back = saturate(dot(-n, l)) * _Translucency;
                // Lighting as on terrain: true sun (_TerrainSun, may exceed 1), hemispheric
                // ambient from the sky (no *ndl: at sunset the sky glows even when the direct
                // ray has faded); the sun colour (photosphere × T) and ambient are shared globals.
                // The HDRP shadow dims direct light; ambient remains.
                 float shadow = GalilegoSunShadow(input.positionCS.xy, input.positionWS, n, l);
                 float cloudShadow = SampleCloudShadow(input.positionWS);
                 float sun = _TerrainSun;
                float3 radialUp = normalize(input.positionWS - _TerrainBodyCenterWS);
                float dayLocal = smoothstep(-0.12, 0.08, dot(radialUp, l));
                float3 light = float3(_NightAmbient, _NightAmbient, _NightAmbient)
                    + (GalilegoSkyAmbient(n) * _TerrainRadianceScale * dayLocal)
                     + (_SunLightColor * (sun * ndl * shadow * cloudShadow) * _TerrainRadianceScale)
                     + (_SunLightColor * (sun * back * shadow * cloudShadow) * _TerrainRadianceScale);
                float3 color = albedo * light * _GroundTint.rgb;
                color += _UnderwaterCausticColor.rgb * UnderwaterCausticMask(input.positionWS, n);
                return float4(color, 1.0);
            }
            ENDHLSL
        }

        // Depth pass for the HDRP depth-prepass (see GroundDecorSolid): without it
        // billboards look translucent against the sky.
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Alpha-clip is required here too (see GroundDecorSolid).
                float4 albedo = tex2D(_BaseColorMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
