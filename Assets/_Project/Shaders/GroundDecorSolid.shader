Shader "Galilego/GroundDecorSolid"
{
    Properties
    {
        _BaseColorMap("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.30, 0.50, 0.20, 1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _WindStrength("Wind Strength", Range(0.0, 1.0)) = 0.15
        _WindSpeed("Wind Speed", Float) = 1.5
        _Translucency("Translucency", Range(0.0, 1.0)) = 0.35
        // Albedo tint by vertex color (tree trunk/foliage). 0 — other decor
        // layers (grass, rocks) are unaffected.
        _VertexColorTint("Vertex Color Tint", Range(0.0, 1.0)) = 0.0
        // Two-sided leaf lighting: use abs(N·L) — flat grass blades have their
        // normal pointing sideways. Trees need ordinary one-sided light, so
        // this is a material property, not a global change.
        _TwoSided("Two Sided Lighting", Range(0.0, 1.0)) = 0.0
        // Multiplies decor albedo to match the ground tone: the terrain
        // darkens further with occlusion/mottling and textures.
        _GroundTint("Ground Match Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Pass
        {
            Name "GroundDecorSolidForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma shader_feature_local _DECOR_INDIRECT_MATRICES
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoLighting.hlsl"
            #include "GalilegoUnderwaterCaustics.hlsl"
#if defined(_DECOR_INDIRECT_MATRICES)
            // Indirect draw ids (RenderMeshIndirect): SV_InstanceID is
            // otherwise not filled on DX12 and matrices read as garbage.
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
#endif

            // Time — set by PlanetSurfaceRenderer.
            float _GroundDecorTime;

            // Grass indirect path: instance matrices (chunk frame) in a buffer,
            // chunk→world via MaterialPropertyBlock. Enabled by the
            // _DECOR_INDIRECT_MATRICES keyword on the grass material.
            float4x4 _DecorChunkToWorld;
            StructuredBuffer<float4x4> _DecorMatrices;

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _WindStrength;
            float _WindSpeed;
            float _Translucency;
            float _VertexColorTint;
            float _TwoSided;
            float4 _GroundTint;
            float3 _TerrainBodyCenterWS;

            float4x4 DecorInstanceMatrix(uint instanceId)
            {
                // Buffer data is a raw Matrix4x4 (column-major) → HLSL reads it
                // transposed, hence transpose.
                return mul(_DecorChunkToWorld, transpose(_DecorMatrices[instanceId]));
            }

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
#if defined(_DECOR_INDIRECT_MATRICES) && !defined(UNITY_INSTANCING_ENABLED)
                uint decorInstanceId : SV_InstanceID;
#endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Wind: bend the mesh top (local Y) in LOCAL XZ axes. World XZ
                // on a planet is not horizontal. Vertex color.a is the wind
                // mask: 1 foliage, 0 trunk (trees).
                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.y * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.a;
                float gust = 0.55 + (0.45 * sin(phase));
                positionOS.z += bend * gust;

                float3 positionWS;
                float3 normalWS;
#if defined(_DECOR_INDIRECT_MATRICES) && defined(UNITY_INSTANCING_ENABLED)
                float4x4 instanceMatrix = DecorInstanceMatrix(input.instanceID);
                positionWS = mul(instanceMatrix, float4(positionOS, 1.0)).xyz;
                normalWS = mul((float3x3)instanceMatrix, input.normalOS);
#elif defined(_DECOR_INDIRECT_MATRICES)
                InitIndirectDrawArgs(0);
                uint decorId = GetIndirectInstanceID(globalIndirectDrawArgs, input.decorInstanceId);
                float4x4 instanceMatrix = DecorInstanceMatrix(decorId);
                positionWS = mul(instanceMatrix, float4(positionOS, 1.0)).xyz;
                normalWS = mul((float3x3)instanceMatrix, input.normalOS);
#else
                positionWS = TransformObjectToWorld(positionOS);
                normalWS = TransformObjectToWorldNormal(input.normalOS);
#endif
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 tex = tex2D(_BaseColorMap, input.uv);
                float alpha = tex.a * _BaseColor.a;
                // Texture edge: RGB goes to black at low alpha. Blend in the
                // base color by alpha (unchanged for opaque materials with a
                // white texture).
                float3 albedoRgb = tex.rgb * _BaseColor.rgb;
                albedoRgb = lerp(_BaseColor.rgb, albedoRgb, saturate(alpha * 1.6));
                float4 albedo = float4(albedoRgb, alpha);
                // Vertex RGB — the mesh part's color (trees: bark/foliage).
                albedo.rgb *= lerp(float3(1.0, 1.0, 1.0), input.color.rgb, _VertexColorTint);
                clip(alpha - _Cutoff);

                float3 n = normalize(input.normalWS);
                float3 l = normalize(_TerrainSunDir);
                // The blade normal matches the surface normal (mesh normals are
                // baked along growth), so ndl is taken as for the ground.
                // _TwoSided is kept as an option: 0 — exactly the ground model.
                float oneSided = saturate(dot(n, l));
                float twoSided = saturate(abs(dot(n, l)));
                float ndl = lerp(oneSided, twoSided, saturate(_TwoSided));
                float back = saturate(dot(-n, l)) * _Translucency * lerp(1.0, 0.25, saturate(_TwoSided));

                // HDRP shadow (PCSS/PCF): attenuates direct light; ambient remains.
                 float shadow = GalilegoSunShadow(input.positionCS.xy, input.positionWS, n, l);
                 float cloudShadow = SampleCloudShadow(input.positionWS);

                 // Lighting is exactly the terrain model (PlanetSurface.shader):
                // real sun (_TerrainSun, may exceed 1), hemispherical sky
                // ambient (see GalilegoSkyAmbient: no *ndl, otherwise the
                // sunset is black); plus foliage translucency (back).
                float sun = _TerrainSun;
                float3 radialUp = normalize(input.positionWS - _TerrainBodyCenterWS);
                float dayLocal = smoothstep(-0.12, 0.08, dot(radialUp, l));
                float3 light = float3(_NightAmbient, _NightAmbient, _NightAmbient)
                    + (GalilegoSkyAmbient(n) * _TerrainRadianceScale * dayLocal)
                     + (_SunLightColor * (sun * ndl * shadow * cloudShadow) * _TerrainRadianceScale)
                     + (_SunLightColor * (sun * back * shadow * cloudShadow) * _TerrainRadianceScale);
                float3 color = albedo.rgb * light * _GroundTint.rgb;
                color += _UnderwaterCausticColor.rgb * UnderwaterCausticMask(input.positionWS, n);
                return float4(color, 1.0);
            }
            ENDHLSL
        }

        // Depth for the HDRP depth-prepass: transparent shaders (atmosphere,
        // stars, sun disc) read the depth pyramid, and without this pass their
        // IsSky() treats tree/grass pixels as sky. Alpha-clip is required:
        // otherwise clipped pixels write depth and cover the sky.
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
            #pragma shader_feature_local _DECOR_INDIRECT_MATRICES
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#if defined(_DECOR_INDIRECT_MATRICES)
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
#endif

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _GroundDecorTime;
            float _WindStrength;
            float _WindSpeed;

            float4x4 _DecorChunkToWorld;
            StructuredBuffer<float4x4> _DecorMatrices;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
#if defined(_DECOR_INDIRECT_MATRICES) && !defined(UNITY_INSTANCING_ENABLED)
                uint decorInstanceId : SV_InstanceID;
#endif
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

                // Wind is the same as in the forward pass: otherwise the two
                // passes' depths diverge → holes and black screen artifacts
                // that crawl with the wind. color.a is the wind mask.
                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.y * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.a;
                float gust = 0.55 + (0.45 * sin(phase));
                positionOS.z += bend * gust;

#if defined(_DECOR_INDIRECT_MATRICES) && defined(UNITY_INSTANCING_ENABLED)
                float4x4 instanceMatrix = mul(_DecorChunkToWorld, _DecorMatrices[input.instanceID]);
                output.positionCS = TransformWorldToHClip(mul(instanceMatrix, float4(positionOS, 1.0)).xyz);
#elif defined(_DECOR_INDIRECT_MATRICES)
                InitIndirectDrawArgs(0);
                uint decorId = GetIndirectInstanceID(globalIndirectDrawArgs, input.decorInstanceId);
                float4x4 instanceMatrix = mul(_DecorChunkToWorld, transpose(_DecorMatrices[decorId]));
                output.positionCS = TransformWorldToHClip(mul(instanceMatrix, float4(positionOS, 1.0)).xyz);
#else
                output.positionCS = TransformObjectToHClip(positionOS);
#endif
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 albedo = tex2D(_BaseColorMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // Shadows: HDRP draws casters into the shadow map with this pass.
        // Alpha-clip and wind are exactly as in forward: otherwise the shadow
        // silhouette does not match the geometry.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma shader_feature_local _DECOR_INDIRECT_MATRICES
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#if defined(_DECOR_INDIRECT_MATRICES)
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
#endif

            sampler2D _BaseColorMap;
            float4 _BaseColorMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _GroundDecorTime;
            float _WindStrength;
            float _WindSpeed;

            float4x4 _DecorChunkToWorld;
            StructuredBuffer<float4x4> _DecorMatrices;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
#if defined(_DECOR_INDIRECT_MATRICES) && !defined(UNITY_INSTANCING_ENABLED)
                uint decorInstanceId : SV_InstanceID;
#endif
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

                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.y * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.a;
                float gust = 0.55 + (0.45 * sin(phase));
                positionOS.z += bend * gust;

#if defined(_DECOR_INDIRECT_MATRICES) && defined(UNITY_INSTANCING_ENABLED)
                float4x4 instanceMatrix = mul(_DecorChunkToWorld, _DecorMatrices[input.instanceID]);
                output.positionCS = TransformWorldToHClip(mul(instanceMatrix, float4(positionOS, 1.0)).xyz);
#elif defined(_DECOR_INDIRECT_MATRICES)
                InitIndirectDrawArgs(0);
                uint decorId = GetIndirectInstanceID(globalIndirectDrawArgs, input.decorInstanceId);
                float4x4 instanceMatrix = mul(_DecorChunkToWorld, transpose(_DecorMatrices[decorId]));
                output.positionCS = TransformWorldToHClip(mul(instanceMatrix, float4(positionOS, 1.0)).xyz);
#else
                output.positionCS = TransformObjectToHClip(positionOS);
#endif
                output.uv = TRANSFORM_TEX(input.uv, _BaseColorMap);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 albedo = tex2D(_BaseColorMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
