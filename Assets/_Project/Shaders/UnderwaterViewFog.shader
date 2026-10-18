// Underwater attenuation following Beer-Lambert, spectral, a full-screen
// HDRP pass.
//
// Scattered light (veil) attenuates with CAMERA depth, and the light path is
// measured as DISTANCE ALONG THE RAY (eyeDepth / cos to the camera axis) so
// the screen edges are not under-attenuated at wide FOV.
//
// The water ceiling (Snell's window) is drawn analytically when the water mesh
// did not leave surface depth above the camera (Auto mode), or always (Always
// mode, for checking). If the water mesh renders normally, Auto does nothing.
//
// Accuracy: camera altitude and k = sea^2 - |C|^2 are computed on the CPU in
// double; the ray exit through the surface uses t = k / (sqrt(b^2 + k) + b),
// without subtracting large numbers.

Shader "Galilego/UnderwaterViewFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        // Pass 1: compute attenuation and write it to a dedicated buffer.
        Pass
        {
            Name "UnderwaterViewFog"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FogPass
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

            // --- water profile parameters ------------------------------------------
            float3 _UwSigma;             // absorption, 1/m, per R,G,B channel
            float3 _UwWaterColor;        // scattered light colour (veil), linear
            float  _UwAmbient;           // constant veil term
            float  _UwAmbientDecay;      // fraction of sigma by which scattered light fades with depth
            float  _UwSunShapeFloor;     // lower fraction of sunlight at N·L = 0
            float  _UwSunPenetration;    // how much the sun fades with depth
            float  _UwTransitionMeters;  // width of the smooth transition through the surface
            float  _UwDownwardDarkening; // how much it darkens when looking down
            int    _UwCeilingMode;       // 0 off, 1 auto (only if the mesh is not drawn), 2 always

            // --- geometry computed on the CPU in double ---------------------------
            float  _UwCamAltitude;       // camera altitude above sea level, m (<0 - underwater)
            float  _UwSeaK;              // k = sea^2 - |C|^2, m^2
            float  _UwRadius;            // |C|, m
            float3 _UwUp;                // local up, simulation space
            float3 _UwSunDir;            // direction TO the sun

            // SkyEnvironment globals (the same ones WaterSurface.shader reads).
            float3 _SkyAmbientColor;
            float3 _SunLightColor;
            float  _SkyAmbient;
            float  _TerrainRadianceScale;

            // Water globals (set by PlanetSurfaceRenderer): the ceiling pattern uses
            // the same body-fixed coordinates as the water mesh.
            float  _WaterTime;
            float4x4 _WaterWorldToBody;
            float3 _WaterBodyAnchor;

            // --- camera basis for reconstructing the ray direction -----------------
            float3 _UwCamRight;
            float3 _UwCamUp;
            float3 _UwCamFwd;
            float  _UwTanHalfFovX;
            float  _UwTanHalfFovY;

            // Debug, see UnderwaterViewFog.DebugMode.
            int    _UwDebugMode;

            float ExitDistance(float3 D)
            {
                float b = _UwRadius * dot(_UwUp, D);
                return _UwSeaK / (sqrt(max(b * b + _UwSeaK, 0.0)) + b);
            }

            float3 ViewDirection(float2 uv)
            {
                // Canonical HDRP method: two points of the same ray (near and far
                // plane) through the inverse VP matrix. The difference is independent of
                // camera-relative mode, platform Y sign, FOV and lens shift.
                float3 nearWS = ComputeWorldSpacePosition(uv, 1.0, UNITY_MATRIX_I_VP);
                float3 farWS = ComputeWorldSpacePosition(uv, 0.0, UNITY_MATRIX_I_VP);
                return normalize(farWS - nearWS);
            }

            // Sky in direction dir (a ray OUT of the water). Same formulas and
            // constants as GalilegoSkyRadiance.hlsl, but without clouds and stars.
            float3 WindowSky(float3 dir)
            {
                float upness = saturate(dot(dir, _UwUp));
                float3 horizonTint = _SkyAmbientColor * float3(1.22, 1.06, 0.90);
                float3 zenithTint = _SkyAmbientColor * float3(0.70, 0.90, 1.28);
                float3 sky = lerp(horizonTint, zenithTint, pow(upness, 0.45)) * (_SkyAmbient * 20.0);

                float sunFactor = smoothstep(-0.035, 0.06, dot(_UwSunDir, _UwUp));
                float cosA = dot(dir, _UwSunDir);
                if (sunFactor > 0.001 && cosA > 0.0)
                {
                    float theta = sqrt(max(0.0, 2.0 * (1.0 - cosA)));
                    const float sigma = 0.03;
                    sky += _SunLightColor * (0.05 * sunFactor) * exp(-theta / 0.075);
                    sky += _SunLightColor * (22.0 * sunFactor) * exp(-(theta * theta) / (2.0 * sigma * sigma));
                }

                return max(sky, 0.0);
            }

            // Wave slope on the ceiling: two travelling ripples ~1 m and ~0.4 m plus
            // a long swell. P is the vector from the camera to a surface point (world
            // axes). The pattern uses body-fixed coordinates so it stays fixed relative
            // to the sea, not to the camera.
            float3 CeilingNormal(float3 P, float pathLen)
            {
                const float kBase = 0.006135923151542565; // 2*pi/1024
                const float2 kRip1 = float2(883.0, 522.0);
                const float2 kRip2 = float2(-588.0, 1043.0);
                const float2 kSwell = float2(156.0, 47.0);
                const float slope = 0.17;

                float3 bodyPos = mul((float3x3)_WaterWorldToBody, P) + _WaterBodyAnchor;
                float2 p = bodyPos.xz;

                float2 d1 = normalize(kRip1);
                float2 d2 = normalize(kRip2);
                float2 d3 = normalize(kSwell);
                float ph1 = dot(p, d1) * (length(kRip1) * kBase) + (_WaterTime * 4.4);
                float ph2 = dot(p, d2) * (length(kRip2) * kBase) - (_WaterTime * 6.8);
                float ph3 = dot(p, d3) * (length(kSwell) * kBase * 0.18) + (_WaterTime * 1.1);

                float2 g = (d1 * (0.75 * slope * cos(ph1)))
                    + (d2 * (0.53 * slope * cos(ph2)))
                    + (d3 * (0.30 * slope * cos(ph3)));

                // Far away the ripple is smaller than a pixel - attenuate to avoid flicker.
                g *= rcp(1.0 + (pathLen * 0.03));
                g *= min(1.0, (slope * 1.8) / max(length(g), 1e-4));

                float3 g3 = float3(g.x, 0.0, g.y);
                g3 -= _UwUp * dot(g3, _UwUp);
                return normalize(_UwUp - g3);
            }

            // Water ceiling from below: Snell's window (Fresnel water -> air) and,
            // beyond the critical angle, the depth mirror (veilMirror).
            float3 CeilingColor(float3 D, float tExit, float3 veilMirror)
            {
                float waterRad = min(_TerrainRadianceScale, 1.0);
                float3 n = CeilingNormal(D * tExit, tExit);

                float cosI = saturate(dot(D, n));
                float sinT = 1.33 * sqrt(saturate(1.0 - (cosI * cosI)));
                bool transmitted = (sinT < 1.0);

                float fresnel = 1.0;
                float3 skySeen = 0.0;
                if (transmitted)
                {
                    float cosT = sqrt(max(1.0 - (sinT * sinT), 0.0));
                    float rs = ((1.33 * cosI) - cosT) / max((1.33 * cosI) + cosT, 1e-4);
                    float rp = ((1.33 * cosT) - cosI) / max((1.33 * cosT) + cosI, 1e-4);
                    fresnel = saturate(0.5 * ((rs * rs) + (rp * rp)));

                    float3 skyDir = refract(D, -n, 1.33);
                    skySeen = WindowSky(skyDir) * (3.0 * waterRad);
                }

                float3 color = lerp(skySeen, veilMirror, fresnel);
                // Bright rim at the window edge - the same Fresnel term.
                color += skySeen * (0.5 * fresnel * (1.0 - fresnel) * 2.0);
                return color;
            }

            float4 FogPass(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.positionCS.xy * _ScreenSize.zw;
                uint2 pixel = uint2(input.positionCS.xy);

                // Smooth transition, NOT if - otherwise it flickers while rocking.
                float halfT = max(0.01, _UwTransitionMeters * 0.5);
                float under = 1.0 - smoothstep(-halfT, halfT, _UwCamAltitude);

                float3 scene = CustomPassLoadCameraColor(pixel, 0);

                if (_UwDebugMode == 1) { return float4(under.xxx, 1.0); }
                if (_UwDebugMode == 6) { return float4(uv.x.xxx, 1.0); }
                if (_UwDebugMode == 7) { return float4(scene, 1.0); }

                // Distance to the opaque pixel ALONG THE RAY. Reversed Z: sky and the
                // far plane give a huge value - that is the "infinite" path.
                float rawDepth = CustomPassLoadCameraDepth(pixel);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 D = ViewDirection(uv);
                float cosFwd = max(dot(D, _UwCamFwd), 1e-3);
                float sceneDist = eyeDepth / cosFwd;

                bool inWater = (_UwCamAltitude < 0.0);
                float tExit = inWater ? ExitDistance(D) : -1.0;
                bool lookingUp = (dot(D, _UwUp) > 0.0);

                // Lighting: both sun and scattered light attenuate with CAMERA depth
                // (scattered slightly slower because it is diffuse).
                float depthBelow = max(-_UwCamAltitude, 0.0);
                float3 sunT = exp(-_UwSigma * (depthBelow * _UwSunPenetration));
                float3 ambT = exp(-_UwSigma * (depthBelow * _UwAmbientDecay));
                float ndl = saturate(dot(_UwUp, _UwSunDir));
                float shape = _UwSunShapeFloor + ((1.0 - _UwSunShapeFloor) * ndl);
                float skyAmbient = saturate(dot(_SkyAmbientColor, float3(0.2126, 0.7152, 0.0722)));
                float3 veil = _UwWaterColor * ((sunT * shape) + (_UwAmbient * skyAmbient * ambT));

                // Looking down is darker: less light from the zenith.
                float downK = lerp(1.0, _UwDownwardDarkening, saturate(-dot(D, _UwUp)));
                float3 scatter = veil * downK;

                // Whether the ceiling must be drawn.
                bool ceilingHere = false;
                bool surfaceInDepth = false;
                if (inWater && lookingUp && tExit > 0.0 && _UwCeilingMode > 0)
                {
                    float tol = max(1.0, 0.25 * tExit);
                    surfaceInDepth = (sceneDist < (tExit + tol));
                    if (_UwCeilingMode >= 2)
                    {
                        // Always: ceiling where nothing is in front of the surface.
                        ceilingHere = (sceneDist > (tExit - tol));
                    }
                    else
                    {
                        // Auto: only if the water mesh left no surface depth.
                        ceilingHere = !surfaceInDepth;
                    }
                }

                if (_UwDebugMode == 8)
                {
                    // R - draw ceiling, G - surface present in depth, B - underwater.
                    return float4(ceilingHere ? 1.0 : 0.0, surfaceInDepth ? 1.0 : 0.0, under, 1.0);
                }

                if (ceilingHere)
                {
                    scene = CeilingColor(D, tExit, veil * _UwDownwardDarkening);
                }

                // Path in water: for an underwater pixel, to it; for a pixel above
                // water, to the ray's exit through the surface.
                float pathIn = sceneDist;
                if (inWater && tExit > 0.0)
                {
                    pathIn = min(pathIn, tExit);
                }

                float3 T = exp(-_UwSigma * pathIn);

                if (_UwDebugMode == 2) { return float4(pathIn / 50.0, 0, 0, 1.0); }
                if (_UwDebugMode == 3) { return float4(T, 1.0); }
                if (_UwDebugMode == 4) { return float4(eyeDepth / 1000.0, 0, 0, 1.0); }
                if (_UwDebugMode == 5)
                {
                    float vis = (tExit > 0.0) ? (tExit / 5000.0) : -1.0;
                    return float4(vis.xxx, 1.0);
                }

                if (under <= 0.002)
                {
                    return float4(scene, 1.0);
                }

                float3 fogged = (scene * T) + (scatter * (1.0 - T));
                return float4(lerp(scene, fogged, under), 1.0);
            }
            ENDHLSL
        }

        // Pass 2: blit the result into the frame.
        Pass
        {
            Name "UnderwaterViewFogComposite"
            ZWrite Off
            ZTest Always
            Blend One Zero
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositePass
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

            float _UwDitherAmount;

            float4 CompositePass(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.positionCS.xy * _ScreenSize.zw;
                float3 c = CustomPassSampleCustomColor(uv).rgb;

                uint2 p = uint2(input.positionCS.xy);
                float dither = (InterleavedGradientNoise(p, 0) - 0.5) * (_UwDitherAmount / 255.0);
                return float4(c + dither, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
