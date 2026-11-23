Shader "Galilego/GasGiantSurface"
{
    Properties
    {
        _GG_BandLut("Band LUT", 2D) = "white" {}
        _GG_WindLut("Wind LUT", 2D) = "white" {}
        [HideInInspector] _GG_SurfaceMap("Surface Map", 2D) = "white" {}
        [HideInInspector] _GG_SurfaceMapStrength("Surface Map Strength", Float) = 0
        [HideInInspector] _GG_MapLongitudeOffset("Map Longitude Offset", Float) = 0
        [HideInInspector] _GG_MapBump("Map Bump", Float) = 0
        _GG_SunDirWS("Sun Direction", Vector) = (1, 1, 0, 0)
        _GG_SunColor("Sun Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _GG_Eclipse("Eclipse Visibility", Float) = 1
        _GG_HazeColor("Haze Color", Color) = (0.62, 0.69, 0.78, 1)
        _GG_RimColor("Rim Color", Color) = (0.62, 0.69, 0.78, 1)
        _GG_PlanetRadius("Planet Radius", Float) = 60303800
        _GG_Oblateness("Oblateness", Range(0.5, 1)) = 0.935
        _GG_IntensityScale("Intensity Scale", Float) = 1
        [HideInInspector] _GG_CameraPosWS("Camera Position", Vector) = (0, 0, 0, 0)
        [HideInInspector] _GG_BodyCenterWS("Body Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _GG_ShearExaggeration("Shear Exaggeration", Float) = 18
        [HideInInspector] _GG_WindTimeA("Wind Time A", Float) = 0
        [HideInInspector] _GG_WindTimeB("Wind Time B", Float) = 0
        [HideInInspector] _GG_VortexTimeA("Vortex Time A", Float) = 0
        [HideInInspector] _GG_VortexTimeB("Vortex Time B", Float) = 0
        [HideInInspector] _GG_LayerBlend("Layer Blend", Float) = 0.18
        [HideInInspector] _GG_PatternSpin("Pattern Spin", Float) = 0
        [HideInInspector] _GG_WarpAmplitude("Warp Amplitude", Float) = 0.22
        [HideInInspector] _GG_WarpScale("Warp Scale", Float) = 3.2
        [HideInInspector] _GG_TurbulenceScale("Turbulence Scale", Float) = 16
        [HideInInspector] _GG_TurbulenceAnisotropy("Turbulence Anisotropy", Float) = 7
        [HideInInspector] _GG_HazeDensity("Haze Density", Float) = 0.09
        [HideInInspector] _GG_LimbDarkening("Limb Darkening", Float) = 0.75
        [HideInInspector] _GG_WrapDiffuse("Wrap Diffuse", Float) = 0.22
        [HideInInspector] _GG_RimIntensity("Rim Intensity", Float) = 0.5
        [HideInInspector] _GG_Saturation("Saturation", Float) = 1.28
        [HideInInspector] _GG_CloudBump("Cloud Bump", Float) = 0.5
        [HideInInspector] _GG_Contrast("Contrast", Float) = 1.12
        [HideInInspector] _GG_PolarTint("Polar Tint", Float) = 0.42
        [HideInInspector] _GG_DetailStrength("Detail Strength", Float) = 1.15
        [HideInInspector] _GG_VortexSpinPeriod("Vortex Spin Period", Float) = 40
        [HideInInspector] _GG_DebugMode("Debug Mode", Float) = 0
        [HideInInspector] _GG_NorthPoleColor("North Pole Color", Color) = (0.5, 0.53, 0.57, 1)
        [HideInInspector] _GG_SouthPoleColor("South Pole Color", Color) = (0.42, 0.45, 0.49, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "GasGiantSurfaceForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GasGiantCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 directionOS: TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                // Inverse-transpose: the visual is an oblate ellipsoid, not a
                // uniformly scaled sphere.
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.directionOS = input.positionOS;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 p = normalize(input.directionOS);
                float3 N = normalize(input.normalWS);
                float3 V = normalize(_GG_CameraPosWS - input.positionWS);
                float3 color = ShadeGasGiant(p, N, V);
                return float4(color, 1.0);
            }
            ENDHLSL
        }

        // Depth for the HDRP depth pyramid: the atmosphere/clouds/volumetrics
        // and the far disc's depth test need the gas giant in the depth buffer.
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
