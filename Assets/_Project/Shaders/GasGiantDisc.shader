Shader "Galilego/GasGiantDisc"
{
    Properties
    {
        _GG_BandLut("Band LUT", 2D) = "white" {}
        _GG_WindLut("Wind LUT", 2D) = "white" {}
        [HideInInspector] _GG_SurfaceMap("Surface Map", 2D) = "white" {}
        [HideInInspector] _GG_SurfaceMapStrength("Surface Map Strength", Float) = 0
        [HideInInspector] _GG_MapLongitudeOffset("Map Longitude Offset", Float) = 0
        [HideInInspector] _GG_MapBump("Map Bump", Float) = 0
        _GG_HazeColor("Haze Color", Color) = (0.62, 0.69, 0.78, 1)
        _GG_RimColor("Rim Color", Color) = (0.62, 0.69, 0.78, 1)
        _GG_IntensityScale("Intensity Scale", Float) = 1
        [HideInInspector] _GG_SunDirWS("Sun Direction", Vector) = (1, 1, 0, 0)
        [HideInInspector] _GG_SunColor("Sun Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _GG_CameraPosWS("Camera Position", Vector) = (0, 0, 0, 0)
        [HideInInspector] _GG_BodyCenterWS("Body Center", Vector) = (0, 0, 0, 0)
        [HideInInspector] _GG_PlanetRadius("Planet Radius", Float) = 60303800
        [HideInInspector] _GG_Oblateness("Oblateness", Range(0.5, 1)) = 0.935
        [HideInInspector] _GG_ShearExaggeration("Shear Exaggeration", Float) = 18
        [HideInInspector] _GG_WindTimeA("Wind Time A", Float) = 0
        [HideInInspector] _GG_WindTimeB("Wind Time B", Float) = 0
        [HideInInspector] _GG_VortexTimeA("Vortex Time A", Float) = 0
        [HideInInspector] _GG_VortexTimeB("Vortex Time B", Float) = 0
        [HideInInspector] _GG_PhaseWeight("Phase Weight", Float) = 1
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
        // After the atmosphere, stars and the Sun disc. The gas giant is an
        // opaque body, but it is drawn as a transparent billboard at 0.9*far;
        // premultiplied blending lets it cover the stars without an extra
        // opaque depth pass.
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+3" }

        Pass
        {
            Name "GasGiantDiscForward"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GasGiantCommon.hlsl"

            // Disc basis, all in the body frame:
            //   _GG_DiscV  = body -> camera
            //   _GG_DiscE1 = screen right on the body ellipsoid
            //   _GG_DiscE2 = projected north on the body ellipsoid
            // The quad is camera-facing and its UV (x,y) is centered; the shader
            // solves the ellipsoid equation for z instead of drawing a flat disc.
            float3 _GG_DiscE1;
            float3 _GG_DiscE2;
            float3 _GG_DiscV;
            float4x4 _GG_BodyToWorld;
            float _GG_DiscDistance;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Hide the disc behind the planet/terrain. The disc is at 0.9*far;
                // a depth farther than that is a real occluder. Nearer depth is
                // handled by ZTest (the observer's own craft must occlude it).
                float rawDepth = LoadCameraDepth(uint2(input.positionCS.xy));
                if (!IsSky(rawDepth)
                    && LinearEyeDepth(rawDepth, _ZBufferParams) > _GG_DiscDistance)
                {
                    discard;
                }

                float2 centered = (input.uv * 2.0) - 1.0;
                float3 q0 = (centered.x * _GG_DiscE1) + (centered.y * _GG_DiscE2);

                // Ellipsoid: x^2/R^2 + y^2/R^2 + z^2/(kR)^2 = 1. In units of R
                // the shape is A = (1, 1, 1/k^2). Solve for the near intersection
                // along v; D < 0 means outside the projected ellipse.
                float k = max(0.5, _GG_Oblateness);
                float3 A = float3(1.0, 1.0, 1.0 / (k * k));
                float3 v = normalize(_GG_DiscV);
                float3 Av = v * A;

                float a = dot(v, Av);
                float b = 2.0 * dot(q0, Av);
                float c = dot(q0 * A, q0) - 1.0;
                float D = (b * b) - (4.0 * a * c);
                if (D < 0.0)
                {
                    discard;
                }

                float z = (-b + sqrt(D)) / (2.0 * a);
                if (z <= 0.0)
                {
                    discard;
                }

                float3 q = q0 + (z * v);
                float3 p = normalize(q);
                float3 nBody = normalize(q * A);
                float3 nWorld = normalize(mul((float3x3)_GG_BodyToWorld, nBody));
                float3 vWorld = normalize(mul((float3x3)_GG_BodyToWorld, v));

                float3 color = ShadeGasGiant(p, nWorld, vWorld);
                float alpha = smoothstep(0.0, 0.025, D);
                return float4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
