Shader "Galilego/GrassBlade"
{
    // Specialised grass shader, kept separate from GroundDecorSolid because its
    // optimisations do not apply to trees/rocks/cacti.
    //
    // Lighting is computed in the VERTEX shader; the fragment outputs the ready colour.
    // Grass cost is fill rate, and a blade mesh has a single normal (uniqueNormals = 1),
    // so per-vertex lighting is exact here.
    //
    // Only shadow tiers that don't need positionSS are used (GalilegoShadowMedium /
    // GalilegoShadowCheap); GalilegoSunShadow needs SV_Position, unavailable in a vertex
    // shader. Trade-off: no soft PCSS shadow up close, invisible on a carpet of thin blades.
    //
    // No clip() and no DepthForwardOnly pass: discard was dead but disabled Early-Z, and
    // the depth pass duplicated all geometry. Grass is opaque with ZWrite On, so the depth
    // buffer is already correct.
    //
    // Cull Off is intentional: a blade is a folded plane with a one-sided wing, so without
    // it half the blades would vanish on the back side.

    Properties
    {
        _BaseColor("Base Color", Color) = (0.146, 0.371, 0.201, 1)
        _WindStrength("Wind Strength", Range(0.0, 1.0)) = 0.15
        _WindSpeed("Wind Speed", Float) = 1.5
        _Translucency("Translucency", Range(0.0, 1.0)) = 0.35
        _TwoSided("Two Sided Lighting", Range(0.0, 1.0)) = 1.0
        _ShadowMul("Shadow Multiplier", Range(0.0, 1.0)) = 1.0
        // Blend to the ground colour at distance. Grass density must fall with distance,
        // otherwise the zone edge reads as a bald circle. The blend tints sparse far
        // grass like the ground beneath it, so the far zone looks like a smooth field.
        _FarFadeStart("Far Fade Start (m)", Float) = 60.0
        _FarFadeEnd("Far Fade End (m)", Float) = 200.0
        _FarFadeColor("Far Fade Color", Color) = (0.146, 0.371, 0.201, 1)
        _FarFadeStrength("Far Fade Strength", Range(0.0, 1.0)) = 1.0
        _GroundTint("Ground Match Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "GrassBladeForward"
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

            // Time — set by PlanetSurfaceRenderer.
            float _GroundDecorTime;

            float4 _BaseColor;
            float _WindStrength;
            float _WindSpeed;
            float _Translucency;
            float _TwoSided;
            float _ShadowMul;
            float _FarFadeStart;
            float _FarFadeEnd;
            float4 _FarFadeColor;
            float _FarFadeStrength;
            float4 _GroundTint;
            float3 _TerrainBodyCenterWS;
            // Terrain palette (set by PlanetSurfaceRenderer.ApplyTerrainGlobals).
            float4 _ColGrass;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 color      : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Wind: bend the blade top (local Y) in the local XZ axes — world XZ is
                // not horizontal on a planet. color.a is the wind mask: 1 foliage, 0 trunk.
                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.y * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.a;
                float gust = 0.55 + (0.45 * sin(phase));
                positionOS.z += bend * gust;

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // ---- All lighting here, in the vertex shader ----
                float3 n = normalize(normalWS);
                float3 l = normalize(_TerrainSunDir);
                float oneSided = saturate(dot(n, l));
                float twoSided = saturate(abs(dot(n, l)));
                float ndl = lerp(oneSided, twoSided, saturate(_TwoSided));
                float back = saturate(dot(-n, l)) * _Translucency * lerp(1.0, 0.25, saturate(_TwoSided));

                // Sun shadow: only world-space tiers, positionSS is unavailable here;
                // the medium tier gives 0 where grass stands in terrain shadow.
                float shadow = (distance(positionWS, GetCameraPositionWS()) <= _ShadowMediumDistance)
                    ? GalilegoShadowMedium(positionWS, n, l)
                    : GalilegoShadowCheap(positionWS, n, l);
                shadow = lerp(1.0, shadow, saturate(_ShadowMul));

                float cloudShadow = SampleCloudShadow(positionWS);

                float sun = _TerrainSun;
                float3 radialUp = normalize(positionWS - _TerrainBodyCenterWS);
                float dayLocal = smoothstep(-0.12, 0.08, dot(radialUp, l));
                float3 light = float3(_NightAmbient, _NightAmbient, _NightAmbient)
                    + (GalilegoSkyAmbient(n) * _TerrainRadianceScale * dayLocal)
                    + (_SunLightColor * (sun * ndl * shadow * cloudShadow) * _TerrainRadianceScale)
                    + (_SunLightColor * (sun * back * shadow * cloudShadow) * _TerrainRadianceScale);

                // Blend to the ground colour: the tone comes from the terrain palette
                // (_ColGrass, set by the renderer) and goes through the same _GroundTint
                // as close grass, otherwise the blend gives a wrong hue at sunset.
                float3 albedo = _BaseColor.rgb;
                float farT = saturate((distance(positionWS, GetCameraPositionWS()) - _FarFadeStart)
                    / max(1e-3f, _FarFadeEnd - _FarFadeStart));
                albedo = lerp(albedo, _ColGrass.rgb, farT * _FarFadeStrength);

                float3 color = albedo * light * _GroundTint.rgb;
                color += _UnderwaterCausticColor.rgb * UnderwaterCausticMask(positionWS, n);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return float4(input.color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
