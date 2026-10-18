Shader "Galilego/WaterSurface"
{
    Properties
    {
        _WaterDeep("Water Deep", Color) = (0.003, 0.07, 0.20, 1)
        _WaterMid("Water Mid", Color) = (0.012, 0.22, 0.45, 1)
        _WaterShallow("Water Shallow", Color) = (0.10, 0.55, 0.60, 1)
        _FoamColor("Foam Color", Color) = (0.88, 0.93, 0.95, 1)
        _UnderwaterColor("Underwater Tint", Color) = (0.02, 0.16, 0.30, 1)
        _SpecularPower("Specular Power", Float) = 420.0
        _SpecularIntensity("Specular Intensity", Float) = 1.0
        // Two scrolling normal maps (HOWTO-Water scheme: the second is finer than
        // the first, scrolling in opposite directions). Default "bump" = flat
        // normal; the renderer substitutes baked ones (Resources/Water/water_normal_{a,b}).
        _NormalMap0("Water Normal A", 2D) = "bump" {}
        _NormalMap1("Water Normal B", 2D) = "bump" {}
        _UseNormalMap("Use Normal Maps", Float) = 0.0
        _NormalStrength("Normal Map Strength", Float) = 1.0
        _NormalScale0("Normal Scale A", Float) = 0.08
        _NormalScale1("Normal Scale B", Float) = 0.16
        _NormalScroll0("Normal Scroll A (uv/sec)", Vector) = (0.06, 0.02, 0, 0)
        _NormalScroll1("Normal Scroll B (uv/sec)", Vector) = (-0.045, 0.03, 0, 0)
        _WaveStrength("Wave Strength", Float) = 0.75
        _WaveScale("Wave Scale", Float) = 0.18
        _WaveAmplitude("Wave Amplitude (m)", Float) = 0.9
        _SparkleStrength("Sparkle Strength", Float) = 0.85
        _SparkleScale("Sparkle Scale Mul", Float) = 8.0
        _ShadingFadeFloor("Shading Fade Floor", Range(0.0, 1.0)) = 0.85
        _DetailFadeFloor("Detail Fade Floor", Range(0.0, 1.0)) = 0.0
        _RippleStrength("Ripple Strength", Float) = 1.0
        _RippleScale("Ripple Scale Mul", Float) = 10.0
        _RippleSpeed("Ripple Speed", Float) = 2.2
        _FoamCrestStrength("Foam Crest", Float) = 0.8
        _FoamDepthMeters("Foam Depth (m)", Float) = 6.0
        _FoamSlopeGain("Foam Slope Gain", Float) = 3.0
        _ShoreFadeMeters("Shore Fade (m)", Float) = 8.0
        _AbsorbShallow("Absorb Shallow (m)", Float) = 4.5
        _AbsorbDeep("Absorb Deep (m)", Float) = 28.0
        _ShallowAlpha("Shallow Alpha", Range(0.0, 1.0)) = 0.7
        _SurfStrength("Surf Strength", Float) = 1.2
        _SurfSpeed("Surf Speed", Float) = 2.0
        _FresnelStrength("Fresnel Strength", Float) = 1.2
        _MinAlpha("Min Alpha", Range(0.0, 1.0)) = 0.93
        _MaxAlpha("Max Alpha", Range(0.0, 1.0)) = 1.0
        _ShallowAlphaMeters("Shallow Alpha Band (m)", Float) = 0.35
        _UnderwaterAlpha("Underwater Alpha", Range(0.0, 1.0)) = 1.0
        _UnderwaterRimStrength("Underwater Window Rim", Float) = 0.5
        _UnderwaterSunStrength("Underwater Sun Strength", Range(0.0, 1.0)) = 0.12
        // Slope of the fine ceiling ripple. The deviation coefficient is
        // (n−1) = 0.33, NOT (1−1/n) = 0.25: the ray exits into air at asin(n·sinβ),
        // i.e. it deviates FROM VERTICAL by (n−1)·β.
        _UnderwaterRippleSlope("Underwater Ripple Slope", Range(0.0, 0.6)) = 0.10
        // Window brightness multiplier relative to the physical sky.
        _UnderwaterWindowGain("Underwater Window Gain", Float) = 3.0
        // How much light the mirror (total internal reflection) reflects: this is
        // the color of the water BODY, not a sky reflection.
        _UnderwaterMirrorGain("Underwater Mirror Gain", Float) = 1.5
        // Snell-window debug output (checked with a PNG capture):
        // 0 = normal view, 1 = Fresnel F in grayscale,
        // 2 = window mask (1 — ray passes, 0 — total internal reflection),
        // 3 = log2(luminance), 0.5 — luminance 1.0 (for a frame histogram).
        _UnderwaterDebugMode("Underwater Debug Mode", Range(0.0, 3.0)) = 0
    }
    SubShader
    {
        // Water is in the AlphaTest queue (opaque list): HDRP does not draw our
        // ForwardOnly pass in the Transparent queue at all. So shallow-water
        // transparency is 1px IGN screen-door dither: fine grain, tied to the
        // world by the alphaT 0-2 m band at the shoreline.
        // Do NOT quantize the threshold by world cells — they give large block
        // pixels up close. Chunks are coplanar (bob=0), so there is no z-fight.
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Pass
        {
            Name "WaterSurfaceForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

             #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
             #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
             // The sky for the Snell window comes from GalilegoSkyRadiance.hlsl:
             // it holds both the global declarations and the computation itself
             // (water doesn't need HDShadow).
             #include "GalilegoSkyRadiance.hlsl"

            // World camera position (set by PlanetSurfaceRenderer every frame).
            float3 _PlanetCameraPos;
            // Planet center in render space: a single radial basis for water
            // across all chunks/LODs (without it, a brightness step at seams).
            float3 _PlanetWaterCenter;
            // Wave animation time (set by PlanetSurfaceRenderer every frame).
            float _WaterTime;
            // Ties the pattern to the sea, not the player: render space moves with
            // the floating origin (anchored at the player), so a pattern taken
            // directly from positionWS crawls along with the player. The shader
            // takes the delta from the camera (small exact numbers), rotates it
            // into body-fixed axes and adds the player's absolute phase — the same
            // trick as _TerrainWorldToBody + _TerrainUVPhase for terrain (set by
            // PlanetSurfaceRenderer).
            float4x4 _WaterWorldToBody;
            float3 _WaterBodyAnchor;

            float4 _WaterDeep;
            float4 _WaterMid;
            float4 _WaterShallow;
            float4 _FoamColor;
            float4 _UnderwaterColor;
            float _SpecularPower;
            float _SpecularIntensity;
            sampler2D _NormalMap0;
            sampler2D _NormalMap1;
            float _UseNormalMap;
            float _NormalStrength;
            float _NormalScale0;
            float _NormalScale1;
            float4 _NormalScroll0;
            float4 _NormalScroll1;
            float _WaveStrength;
            float _WaveScale;
            float _WaveAmplitude;
            float _SparkleStrength;
            float _SparkleScale;
            float _ShadingFadeFloor;
            float _DetailFadeFloor;
            float _RippleStrength;
            float _RippleScale;
            float _RippleSpeed;
            float _FoamCrestStrength;
            float _FoamDepthMeters;
            float _FoamSlopeGain;
            float _ShoreFadeMeters;
            float _AbsorbShallow;
            float _AbsorbDeep;
            float _ShallowAlpha;
            float _SurfStrength;
            float _SurfSpeed;
            float _FresnelStrength;
            float _MinAlpha;
            float _MaxAlpha;
            float _ShallowAlphaMeters;
            float _UnderwaterAlpha;
            float _UnderwaterCameraDepth;
            float _UnderwaterRimStrength;
            float _UnderwaterSunStrength;
            float _UnderwaterRippleSlope;
            float _UnderwaterWindowGain;
            float _UnderwaterMirrorGain;
            float _UnderwaterDebugMode;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 waterExtra : TEXCOORD2; // x: water depth (m, 0 above land)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float depthMeters : TEXCOORD2;
                float waveH       : TEXCOORD3;
            };

            // Procedural value-noise/FBM without textures: breaks up the regularity
            // of waves and foam (two layers in opposite directions, as is usual for
            // sea foam), micro-detail of normals instead of the "velvet" of sines.
            // Lattice hash on INTEGER PCG, no sin: the integer-by-bits substitution
            // never loses precision.
                uint whashInt(uint x)
                {
                    x = x * 747796405u + 2891336453u;
                    uint w = ((x >> ((x >> 28u) + 4u)) ^ x) * 277803737u;
                    return (w >> 22u) ^ w;
                }
                float whash(float2 p)
                {
                    // The integer part of uv stays within +-P/cell size, so it fits
                    // in int32 with margin; the float->int here is safe precisely
                    // because the integer part of uv stays within the period.
                    int2 c = (int2)floor(p);
                    uint h = whashInt((uint)(c.x * 73856093) ^ (uint)(c.y * 19349663));
                    return (h & 0x00FFFFFFu) * (1.0 / 16777216.0);
                }
            float wnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(whash(i), whash(i + float2(1.0, 0.0)), u.x),
                    lerp(whash(i + float2(0.0, 1.0)), whash(i + float2(1.0, 1.0)), u.x), u.y);
            }
            float wfbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int k = 0; k < 3; k++)
                {
                    v += a * wnoise(p);
                    p = p * 2.03 + float2(17.3, 9.1);
                    a *= 0.5;
                }
                return v;
            }

            // Common pattern-period scale: 2*pi/P with P = 1024 m, and ripple
            // vectors given as INTEGER pairs. The phase at uv+period shifts by a
            // multiple of 2*pi, so the pattern doesn't "jump" at the period edge.
            static const float kBaseUw = 0.006135923151542565;
            static const float2 kRip1 = float2(883.0, 522.0);
            // |K| = 2556 -> 15.7 rad/m (~0.4 m).
            static const float2 kRip2 = float2(-1254.0, 2227.0);

            // Body-fixed pattern coordinates (m): delta from the camera in small
            // numbers plus the player's absolute phase. Without this the pattern is
            // computed from render coordinates, whose zero moves with the floating
            // origin = the player, so the water texture crawls across the sea with
            // the player.
            // The anchor arrives already reduced modulo 51 200 m (see UpdateWaterAnchor).
            float3 WaterBodyFixed(float3 positionWS)
            {
                float3 relBody = mul((float3x3)_WaterWorldToBody, positionWS - _PlanetCameraPos);
                return relBody + _WaterBodyAnchor;
            }

            // Living ocean: 5 travelling waves (simplified Gerstner: vertical only —
            // physics stays flat via WaterQuery, chunks rotate with the planet body).
            // Amplitude sum = 1 → returns [-1,1].
            // The fifth octave is a short chop (~1.5 m), giving "small waves" up
            // close and fading out at distance via fade and fwidth AA.
            float WaterWaveHeight01(float2 uv, float t, out float2 gradTangent)
            {
                float2 p = uv;
                // Wave vectors are INTEGER vectors K, phase = dot(p,K)*kBase.
                // The pattern is then periodic over P = 1024 m: a shift by P gives
                // dot += |K|, i.e. phase += 2*pi*|K|. Direction and frequency come
                // from decomposing K rather than being set separately — otherwise
                // periodicity breaks on non-integer products.
                const float kBase = 0.006135923151542565;  // 2*pi/1024
                const float2 K1 = float2(156.0, 47.0);
                const float2 K2 = float2(-153.0, 231.0);
                const float2 K3 = float2(311.0, -311.0);
                const float2 K4 = float2(-144.0, -719.0);
                const float2 K5 = float2(948.0, -635.0);
                float L1 = length(K1); float L2 = length(K2);
                float L3 = length(K3); float L4 = length(K4);
                float L5 = length(K5);
                float2 d1 = K1 / L1; float2 d2 = K2 / L2;
                float2 d3 = K3 / L3; float2 d4 = K4 / L4;
                float2 d5 = K5 / L5;
                float f1 = L1 * kBase; float f2 = L2 * kBase;
                float f3 = L3 * kBase; float f4 = L4 * kBase;
                float f5 = L5 * kBase;
                float p1 = dot(p, K1) * kBase + (t * 1.1);
                float p2 = dot(p, K2) * kBase - (t * 1.35);
                float p3 = dot(p, K3) * kBase + (t * 1.9);
                float p4 = dot(p, K4) * kBase - (t * 2.6);
                float p5 = dot(p, K5) * kBase + (t * 3.4);
                float s1 = sin(p1);
                float s2 = sin(p2);
                float s3 = sin(p3);
                float s4 = sin(p4);
                float s5 = sin(p5);
                float h = (0.38 * s1) + (0.26 * s2) + (0.18 * s3) + (0.11 * s4) + (0.07 * s5);
                // Trochoidal crest sharpening (Gerstner spirit: crests higher and
                // sharper, troughs wider and flatter; physics stays flat).
                // The derivative is corrected analytically — normals stay honest.
                float qShape = 0.25;
                float dhds = 1.0 + (2.0 * qShape * h);
                float2 grad = ((0.38 * f1 * d1) * cos(p1)
                    + (0.26 * f2 * d2) * cos(p2)
                    + (0.18 * f3 * d3) * cos(p3)
                    + (0.11 * f4 * d4) * cos(p4)
                    + (0.07 * f5 * d5) * cos(p5)) * dhds;
                gradTangent = grad;
                return h + (qShape * h * h);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 baseWS = TransformObjectToWorld(input.positionOS);
                // Radial from the single center, not the mesh vertex normal:
                // it differs between adjacent LODs, causing a bright seam.
                // Fall back to the mesh normal while the center is unset (zeros).
                float3 centerDeltaV = baseWS - _PlanetWaterCenter;
                float3 baseN = dot(centerDeltaV, centerDeltaV) > 1.0
                    ? normalize(centerDeltaV)
                    : TransformObjectToWorldNormal(input.normalOS);
                // Live wave along the radial normal (physics stays flat).
                // Fades to zero at distance: coarse LOD + short wave = aliasing.
                float camDistV = length(_PlanetCameraPos - baseWS);
                float fadeV = exp(-camDistV * 0.00012);
                // Pattern in body-fixed .xz space.
                float2 patternUv = WaterBodyFixed(baseWS).xz;
                float2 gradV;
                float h01 = WaterWaveHeight01(patternUv * _WaveScale, _WaterTime, gradV);
                float3 gdirV = float3(gradV.x, 0.0, gradV.y);
                float h = _WaveAmplitude * h01 * fadeV;
                output.positionWS = baseWS + (baseN * h);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = baseN;
                output.depthMeters = input.waterExtra.x;
                output.waveH = h01;
                return output;
            }

            // Pretty sea: turquoise shallows, ocean depth, a sun path with
            // glitter, white surf foam and whitecaps, a Fresnel sky mirror, horizon
            // haze. The face is hand-computed (dot); the edge comes from vertex
            // depth (robust without a depth buffer).
            float4 Frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(_PlanetCameraPos - input.positionWS);
                float3 color = float3(0.0, 0.0, 0.0);
                float alpha = 1.0;
                float cameraDist = length(_PlanetCameraPos - input.positionWS);
                 float sun = _TerrainSun;
                 float3 sunDir = normalize(_TerrainSunDir);
                 float cloudShadow = SampleCloudShadow(input.positionWS);
                // The same single radial as in the vertex shader.
                float3 centerDeltaF = input.positionWS - _PlanetWaterCenter;
                float3 baseNFace = dot(centerDeltaF, centerDeltaF) > 1.0
                    ? normalize(centerDeltaF)
                    : normalize(input.normalWS);

                // Water computes light at "scale 1": _TerrainRadianceScale (2.5 in
                // the scene) is tuned for land albedo ~0.4 and would blow water out
                // to white. This affects water only, not terrain.
                float waterRad = min(_TerrainRadianceScale, 1.0);

                // --- Underwater -----------------------------------------------
                // Camera below the surface: the view goes against the normal.
                // This branch is opaque and WITHOUT screen-door (see end of shader):
                // the water ceiling seen from below must be solid, otherwise the
                // discard holes give grain all over the screen.
                // SNELL WINDOW BY PHYSICS. An upward ray is refracted by the wave
                // normal (water 1.33 → air 1.0). Beyond the 48.75° critical angle
                // the ray does not exit at all: total internal reflection, and the
                // eye sees the water body. The window boundary and its rim come
                // from the same Fresnel formula, not from a smoothstep on cosUp —
                // a real window has a physical boundary and reads as a round edge.
                //
                // Deliberately absent here:
                //  - its own exp(-dist/28): absorption is done by the
                //    UnderwaterViewFog pass via spectral σ;
                //  - caustics on the ceiling: caustics are light CONCENTRATED on
                //    the upward-lit surface, invisible from the inside, visible on
                //    the bottom (UnderwaterCaustics.shader).
                // We are underwater exactly when the SURFACE IS ABOVE THE CAMERA:
                // dot(camera→surface, up) > 0. This is a geometric sign that cannot
                // disagree with the image under any world settings. Mind the sign:
                // viewDir here is surface → camera, so "up" is -viewDir.
                //
                // _UnderwaterCameraDepth is only a fallback: it is set from the
                // "camera below sea level" query and can lag a frame or reset on a
                // body change. The 0.25 threshold (not 0) is deliberate: looking at
                // the horizon dot ≈ 0 and the sign is unstable, while 0.25 means
                // looking almost straight up (within 14° of vertical).
                float viewUp = dot(-viewDir, baseNFace);
                bool isUnderwater = (_UnderwaterCameraDepth > 0.0) || (viewUp > 0.25);
                if (isUnderwater)
                {
                    float3 upW = baseNFace;
                    // viewDir: surface -> camera (down). toSurf: camera -> surface (up).
                    float3 toSurf = -viewDir;

                    // Sun above the LOCAL horizon is the only honest time-of-day
                    // cue available to the shader. The ±2° shelf mimics a real sunset.
                    float sunAboveHorizon = dot(sunDir, _SkyUp);
                    float sunFactor = smoothstep(-0.035, 0.06, sunAboveHorizon);

                    // Same Gerstner and ripple as above water, in the same tangent
                    // UVs: ceiling and surface don't diverge.
                    float3 wposUw = WaterBodyFixed(input.positionWS);
                    float2 patternUw = wposUw.xz;
                    float2 gradUw;
                    float h01Uw = WaterWaveHeight01(patternUw * _WaveScale, _WaterTime, gradUw);
                    float3 gradWUw = mul(float3(gradUw.x, 0.0, gradUw.y) * _WaveScale, (float3x3)_WaterWorldToBody);
                    float3 gradTUw = gradWUw - (upW * dot(gradWUw, upW));
                    // Macro slope. A real sea has an RMS slope of 10-15°, i.e.
                    // ~0.2 rad.
                    float macroKUw = (_WaveAmplitude * 2.0 + _WaveStrength * 1.5);

                    // Fine ripple as two travelling octaves. The slope is given as
                    // an ANGLE (_UnderwaterRippleSlope, rad), not a gradient factor:
                    // otherwise "slope boost" means different things at different
                    // wavelengths. Frequencies 6.3 and 15.7 rad/m are ~1 m and
                    // ~0.4 m ripple.
                    // The AA limit uses the pattern's own footprint, not
                    // positionWS.x/z: on a slanted surface the render-coordinate
                    // derivatives can approach zero, leaving the limit useless.
                    float fwUw = max(fwidth(patternUw.x), fwidth(patternUw.y));

                    // The ripple vectors are declared above (common period scale); here only lengths.
                    float rfUw1 = length(kRip1) * kBaseUw;
                    float rfUw2 = length(kRip2) * kBaseUw;

                    // Slope limit from the pixel footprint in pattern axes: slopes
                    // steeper than 1/(2*fw) can't be represented in one pixel.
                    float slopeLimitUw = 0.5 / max(fwUw, 1e-4);
                    float slopeUw = min(_UnderwaterRippleSlope, slopeLimitUw);
                    float aaUw1 = saturate(1.0 - (fwUw * rfUw1 * 0.35));
                    float aaUw2 = saturate(1.0 - (fwUw * rfUw2 * 0.35));
                    float2 ripDir1 = normalize(kRip1);
                    float2 ripDir2 = normalize(kRip2);
                    float rpUw1 = dot(patternUw, ripDir1) * rfUw1 + (_WaterTime * _RippleSpeed * 2.0);
                    float rpUw2 = dot(patternUw, ripDir2) * rfUw2 - (_WaterTime * _RippleSpeed * 3.1);

                    // Octave amplitudes are given DIRECTLY in radians, and the sum is
                    // then softly capped. There is deliberately no normalization to a
                    // constant modulus: a sum of cosines has vector zeros near which
                    // the direction flips 180°, which would make the slope IDENTICAL
                    // in every pixel.
                    // Amplitudes 0.75/0.53/0.38 give a sum RMS of ~1.0*slope for
                    // random phases, so the parameter means average slope, not peak.
                    float2 rgradUw = (ripDir1 * (0.75 * slopeUw * cos(rpUw1) * aaUw1))
                        + (ripDir2 * (0.53 * slopeUw * cos(rpUw2) * aaUw2));

                    // Third component is FBM, not a sine: two sines gave perfectly
                    // regular concentric rings (read as a fingerprint). Real ripple is
                    // irregular, and the broken structure hides the sun disc in a
                    // stable scatter rather than garbage. The UV scale is also a
                    // multiple of 2*pi/P, otherwise the FBM doesn't fit the period.
                    const float fbmScaleUw = kBaseUw * 440.0;  // 2.6998 rad/m
                    float2 microUv = (patternUw * fbmScaleUw)
                        + float2(_WaterTime * 0.07, -_WaterTime * 0.05);
                    float mEps = 0.35;
                    float m0 = wfbm(microUv);
                    float mx = wfbm(microUv + float2(mEps, 0.0));
                    float mz = wfbm(microUv + float2(0.0, mEps));
                    float2 mgrad = float2((mx - m0), (mz - m0)) / mEps;
                    // The FBM direction is normalized (a sum of octaves doesn't use
                    // its zeros as an amplitude measure), but its amplitude enters the
                    // overall RMS, also in radians.
                    rgradUw += (mgrad / max(length(mgrad), 1e-4)) * (0.38 * slopeUw * aaUw2);

                    // Soft limit on the total slope. The magnitude is NOT forced to a
                    // set value — it is only capped from above, so at the sum's zeros
                    // the slope quietly goes to zero instead of flipping 180°.
                    float rlenUw = length(rgradUw);
                    float rcapUw = slopeUw * 1.8;
                    rgradUw *= min(1.0, rcapUw / max(rlenUw, 1e-4));
                    float3 rgradTUw = mul(float3(rgradUw.x, 0.0, rgradUw.y), (float3x3)_WaterWorldToBody);
                    rgradTUw -= upW * dot(rgradTUw, upW);

                    // Macro slope is softly limited (tanh) to about 0.25 rad (14°):
                    // a real sea has an RMS slope of 10-15°.
                    const float kMacroSlopeUw = 0.25;
                    float3 macroTiltUw = gradTUw * (macroKUw * 0.7);
                    float macroLenUw = length(macroTiltUw);
                    macroTiltUw *= (kMacroSlopeUw * tanh(macroLenUw / kMacroSlopeUw)) / max(macroLenUw, 1e-5);
                    float3 nUw = normalize(upW - macroTiltUw - rgradTUw);

                    // Baked normal maps in the underwater branch. Above water they add
                    // micro-relief over the shader waves; from below they are the
                    // 10-50 cm ripple the procedural octaves lack, and they break the
                    // regularity of the sines.
                    //
                    // Their contribution is limited by the SAME slope limit as the
                    // octaves above: a map tiled at 0.24 m on a grazing ray gives tens
                    // of degrees of slope per pixel, and without the limit the window
                    // falls apart into blobs.
                    if (_UseNormalMap > 0.5)
                    {
                        // Tiles are divisors of P (powers of two): 0.625 m and 0.25 m.
                        // Then uv + P gives a whole number of tiles and shifting the
                        // anchor by a period doesn't change the image.
                        float2 uvN0 = (patternUw * 1.6) + (_NormalScroll0.xy * _WaterTime);
                        float2 uvN1 = (patternUw * 4.0) + (_NormalScroll1.xy * _WaterTime);
                        float3 tx0 = tex2D(_NormalMap0, uvN0).rgb * 2.0 - 1.0;
                        float3 tx1 = tex2D(_NormalMap1, uvN1).rgb * 2.0 - 1.0;
                        // Reference axis from RENDER space: a world-Y reference would
                        // degenerate at the planet's poles (where upW ≈ ±Y). _SkyUp is
                        // not usable here: it is simulation space, while upW is render.
                        float3 refUw = abs(upW.z) < 0.9 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
                        float3 tanUw = normalize(cross(refUw, upW));
                        float3 binUw = cross(upW, tanUw);
                        float2 tPert = tx0.xy + (tx1.xy * 0.5);
                        nUw = normalize(nUw + ((tanUw * tPert.x + binUw * tPert.y)
                            * (_NormalStrength * slopeUw * 1.5)));
                    }
                    // --- Snell window: water -> air Fresnel ----------------------
                    // The full dielectric formula, not an artistic smoothstep: for
                    // water F0 = ((1.33-1)/(1.33+1))^2 = 0.020, and F goes to 1 toward
                    // the critical angle. This yields both the bright window rim and
                    // the mirror beyond it — no separate curves needed.
                    float cosI = saturate(dot(toSurf, nUw));
                    float sinT = 1.33 * sqrt(saturate(1.0 - (cosI * cosI)));
                    bool transmitted = (sinT < 1.0);
                    float fresnel = 1.0;
                    float3 skyDir = upW;
                    if (transmitted)
                    {
                        float cosT = sqrt(1.0 - (sinT * sinT));
                        float rs = ((1.33 * cosI) - cosT) / ((1.33 * cosI) + cosT);
                        float rp = ((1.33 * cosT) - cosI) / ((1.33 * cosT) + cosI);
                        fresnel = 0.5 * ((rs * rs) + (rp * rp));
                        // Outgoing ray: refract already returns a unit vector.
                        skyDir = refract(toSurf, -nUw, 1.33);
                    }

                    // Sun-glint and star width come from the per-pixel normal spread,
                    // the same signal that damps the octave aliasing. The disc can't
                    // be sharper than a real one (GalilegoSunDisc holds a lower
                    // bound), so it is round in calm water and breaks into a stable
                    // scatter on waves. A strict cone would flicker at ±8° slopes.
                    float3 ddxN = ddx(nUw);
                    float3 ddyN = ddy(nUw);
                    float normalSigma = min(sqrt(dot(ddxN, ddxN) + dot(ddyN, ddyN)), 0.35);

                    float3 skySeen = 0.0;
                    if (transmitted && fresnel < 0.999)
                    {
                        skySeen = GalilegoSkyRadiance(skyDir, sunDir, normalSigma, sunFactor,
                                _CldBodyCenterWS, _CldBottomRadius, _CldTopRadius)
                            * (_UnderwaterWindowGain * waterRad);
                    }

                    // Mirror: reflection of the BODY, not the sky. Physically, beyond
                    // the critical angle the ray doesn't leave the water and the eye
                    // sees what is below: scattered body light, darkening as the angle
                    // grows (less light from the zenith). The light bands are
                    // low-frequency (15-60 m) because there are no individual bright
                    // points on the mirror: caustics collect on the upward-lit
                    // surface, not on the ceiling.
                    float3 mirrorDir = reflect(toSurf, nUw);
                    float downness = saturate(-dot(mirrorDir, upW));
                    float lightLevel = (_SkyAmbient * 2.2)
                        + (saturate(sunAboveHorizon) * _UnderwaterSunStrength * 6.0);
                    float3 mirror = _UnderwaterColor.rgb * (waterRad * lightLevel * _UnderwaterMirrorGain)
                        * lerp(1.0, 0.25, downness);
                    float2 bandUv = (patternUw * (kBaseUw * 5.0))
                        + float2(_WaterTime * 0.012, -_WaterTime * 0.009);
                    float band = (wfbm(bandUv) * 0.6) + (wfbm((bandUv * 2.3) + 11.0) * 0.4);
                    mirror *= 0.75 + (0.5 * band);

                    color = lerp(skySeen, mirror, fresnel);

                    // Window rim — the same Fresnel: where reflection is strong but
                    // not yet total.
                    color += skySeen * (_UnderwaterRimStrength * fresnel * (1.0 - fresnel) * 2.0);

                    // Window debug: a PNG capture is checked against a reference computation.
                    if (_UnderwaterDebugMode > 0.5)
                    {
                        if (_UnderwaterDebugMode < 1.5) { return float4(fresnel.xxx, 1.0); }
                        if (_UnderwaterDebugMode < 2.5) { return float4(transmitted ? 1.0 : 0.0, 1.0, 1.0, 1.0); }
                        float lum = dot(color, float3(0.2126, 0.7152, 0.0722));
                        return float4((log2(max(lum, 1e-4)) * 0.1) + 0.5, 0.0, 0.0, 1.0);
                    }

                    alpha = _UnderwaterAlpha;
                }
                else
                {

                // --- Wave normal ----------------------------------------------
                // shadingFade — macro (gradient + foam) with a floor: water doesn't
                // turn into a dead flat fill at distance.
                // detailFade — high frequency (ripple/glitter) WITHOUT a floor,
                // squared: it aliases sub-pixel first, so cut it before the base wave.
                // Plus per-pixel AA via fwidth: an octave fades when its wavelength
                // is < ~3 pixels.
                float waveFade = exp(-cameraDist * 0.00012);
                float shadingFade = _ShadingFadeFloor + ((1.0 - _ShadingFadeFloor) * waveFade);
                float detailFade = _DetailFadeFloor + ((1.0 - _DetailFadeFloor) * waveFade * waveFade);
                float t = _WaterTime;
                // The whole pattern (wave, chop, ripple, foam, sparkle) is in tangent
                // UVs: otherwise it stays put only while the player stands still and
                // crawls across the sea with them when moving. The same UV as in the
                // vertex shader and the underwater branch, so geometry, normals and
                // ceiling stay consistent (see WaterBodyFixed).
                float2 patternUv = WaterBodyFixed(input.positionWS).xz;
                float2 gradF;
                float h01F = WaterWaveHeight01(patternUv * _WaveScale, t, gradF);
                float3 gdirF = float3(gradF.x, 0.0, gradF.y);
                float3 centerDeltaN = input.positionWS - _PlanetWaterCenter;
                float3 baseN = dot(centerDeltaN, centerDeltaN) > 1.0
                    ? normalize(centerDeltaN)
                    : normalize(input.normalWS);
                float3 gradW = gdirF * _WaveScale;
                float3 gradT = gradW - (baseN * dot(gradW, baseN));
                // Boosted response: waves read both up close and at distance.
                float macroK = (_WaveAmplitude * 2.0 + _WaveStrength * 1.5);
                // Fine chop: two travelling octaves over world XZ (rad/m, independent
                // of _WaveScale). Feeds the main normal → visible in diffuse, fresnel
                // and skyRef, not just the spec.
                // AA limit from the pattern's footprint in its own axes.
                float fwW = max(fwidth(patternUv.x), fwidth(patternUv.y));
                // Chop frequencies are rounded to multiples of 2*pi/P, otherwise the
                // pattern doesn't fit the period and "jumps" when the anchor wraps.
                float rf1 = max(kBaseUw * 131.0, _RippleScale * 0.35);
                float rf2 = rf1 * 2.0;
                float rs1 = _RippleSpeed * 0.8;
                float rs2 = _RippleSpeed * 1.27;
                float2 rd1 = normalize(kRip1);
                float2 rd2 = normalize(kRip2);
                float rp1 = dot(patternUv, rd1) * rf1 + (t * rs1);
                float rp2 = dot(patternUv, rd2) * rf2 - (t * rs2);
                float aa1 = saturate(1.0 - (fwW * rf1 * 0.35));
                float aa2 = saturate(1.0 - (fwW * rf2 * 0.35));
                float2 rgrad = (rd1 * (cos(rp1) * rf1 * aa1))
                    + (rd2 * (0.5 * cos(rp2) * rf2 * aa2));
                float3 rgradW = float3(rgrad.x, 0.0, rgrad.y);
                float3 rgradT = rgradW - (baseN * dot(rgradW, baseN));
                // FBM micro-relief (two layers in opposite directions): breaks the
                // "velvet" of the sines, gives living ripple up close. Fades at
                // distance (detailFade).
                float2 nUv1 = patternUv * 0.55 + float2(t * 0.22, t * 0.13);
                float2 nUv2 = patternUv * 1.15 - float2(t * 0.17, -t * 0.21);
                float ne = 0.6;
                float nn0 = wfbm(nUv1) * 0.65 + wfbm(nUv2) * 0.35;
                float nnx = wfbm(nUv1 + float2(ne, 0.0)) * 0.65 + wfbm(nUv2 + float2(ne, 0.0)) * 0.35;
                float nnz = wfbm(nUv1 + float2(0.0, ne)) * 0.65 + wfbm(nUv2 + float2(0.0, ne)) * 0.35;
                float3 ngradW = float3((nnx - nn0) / ne, 0.0, (nnz - nn0) / ne);
                float3 ngradT = ngradW - (baseN * dot(ngradW, baseN));
                float3 n = normalize(baseN
                    - (gradT * (macroK * shadingFade))
                    - (rgradT * (_RippleStrength * 0.12 * detailFade))
                    - (ngradT * (0.12 * detailFade)));
                // Texture ripple (HOWTO: two normal maps in opposite directions, the
                // second finer). Unwrapped in a tangent basis around baseN.
                if (_UseNormalMap > 0.5)
                {
                    float2 uvA = patternUv * _NormalScale0 + (_NormalScroll0.xy * t);
                    float2 uvB = patternUv * _NormalScale1 + (_NormalScroll1.xy * t);
                    float3 txA = tex2D(_NormalMap0, uvA).rgb * 2.0 - 1.0;
                    float3 txB = tex2D(_NormalMap1, uvB).rgb * 2.0 - 1.0;
                    float3 upRef = abs(baseN.y) > 0.9 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
                    float3 tan1 = normalize(cross(upRef, baseN));
                    float3 tan2 = cross(baseN, tan1);
                    float2 tPert = txA.xy + (txB.xy * 0.5);
                    n = normalize(n + ((tan1 * tPert.x + tan2 * tPert.y)
                        * (_NormalStrength * detailFade)));
                }

                // --- Depth base: turquoise → ocean → deep navy ----------------
                // Absorption is exponential and fast: shallows for the first
                // _AbsorbShallow (~2 m) → mid → solid deep by _AbsorbDeep (~14 m).
                // The bottom reads through the water only at the very edge; beyond
                // that it's an opaque ocean, not a "tinted bottom".
                float depth = max(0.0, input.depthMeters);
                float absorbDeep = 1.0 - exp(-depth / max(1.0, _AbsorbDeep));
                float deepT = pow(saturate(absorbDeep), 0.6);
                float midT = smoothstep(0.0, 0.35, deepT);
                float deepW = smoothstep(0.30, 1.0, deepT);
                float3 waterBase = lerp(_WaterShallow.rgb, _WaterMid.rgb, midT);
                waterBase = lerp(waterBase, _WaterDeep.rgb, deepW);
                // Glowing edge: the first metres are slightly brighter than shallow.
                float shallowKick = 1.0 - exp(-depth / max(0.5, _AbsorbShallow));
                waterBase = lerp(_WaterShallow.rgb * 1.10, waterBase, saturate(shallowKick));

                // Water lighting: a relief term alone goes black here (sun at the
                // horizon -> ndl=0, sky 0.1). Water glows from its body: wrap diffuse
                // plus a boosted hemispherical skylight, otherwise looking straight
                // down is a flat black fill.
                 float ndl = saturate(dot(n, sunDir));
                 float ndlWrap = saturate((dot(n, sunDir) + 0.6) / 1.6);
                // HOWTO water glows from reflection, not ambient over-lighting.
                float dayLocal = smoothstep(-0.12, 0.08, dot(baseN, sunDir));
                float3 waterSky = GalilegoSkyAmbient(n) * waterRad * 1.15 * dayLocal;
                float3 lightTerm = float3(_NightAmbient, _NightAmbient, _NightAmbient)
                    + waterSky
                     + (_SunLightColor * (sun * (ndl * 0.35 + ndlWrap * 0.65) * cloudShadow) * waterRad);

                float cosT = saturate(dot(n, viewDir));
                float fresnelPow = pow(1.0 - cosT, 5.0);
                // Sheen, water F0 0.02-0.04 as in nature (Crest/OceanShader):
                // at distance a mirror, looking down a transparent body, not milk.
                float fresnelF = 0.03 + (0.97 * fresnelPow);

                // Sun path: a broad glint plus grainy glitter. A separate
                // high-frequency octave for specular only: shatters the blob into
                // sparkles at sunset; diffuse/foam don't see it.
                float sparkleFade = detailFade;
                const float2 kSpark = float2(660.0, -749.0);
                float2 sd1 = normalize(kSpark);
                float sf1 = max(length(kSpark) * kBaseUw, _SparkleScale * 2.2);
                float sp1 = dot(patternUv, sd1) * sf1 + (t * 3.1);
                float saa1 = saturate(1.0 - (fwW * sf1 * 0.35));
                float2 sgrad = sd1 * (cos(sp1) * sf1 * saa1);
                float3 sgradW = float3(sgrad.x, 0.0, sgrad.y);
                float3 sgradT = sgradW - (baseN * dot(sgradW, baseN));
                float3 nSpec = normalize(n
                    - (sgradT * (_SparkleStrength * 0.06 * sparkleFade)));
                float3 halfVec = normalize(sunDir + viewDir);
                float spec = pow(saturate(dot(nSpec, halfVec)), max(1.0, _SpecularPower)) * _SpecularIntensity;
                float glitter = 0.5 + (0.5 * sin((sp1 * 1.7) - (t * 2.3)));
                spec *= (0.2 + (0.7 * glitter * saturate(sparkleFade + 0.25))) * dayLocal;

                color = (waterBase * lightTerm)
                     + (spec * sun * _SunLightColor * waterRad * cloudShadow);

                // Wave-body glow (SSS approximation as in Crest/NorthStar): looking
                // through a crest toward the sun, the wave glows turquoise (forward
                // scattering), troughs stay deep. Red extinguishes first — teal only.
                // Beyond that there are no shallows.
                float crest01 = saturate(h01F * 0.5 + 0.5);
                float sunView = saturate(dot(viewDir, sunDir));
                float sssForward = pow(sunView, 3.0) * pow(crest01, 2.0);
                float sssThick = (1.0 - crest01) * 0.35;
                float3 sssColor = float3(0.05, 0.38, 0.40);
                 color += sssColor * (sssThick + sssForward * 0.9)
                     * saturate(sun * ndl + (0.25 * dayLocal)) * (1.0 - deepW) * waterRad * cloudShadow;

                // Sky mirror by Schlick (water F0 0.06 for a pretty sea): at distance
                // the sea brightens toward the horizon instead of staying a dark fill.
                // Generous sky blend plus a warm sun tint — the sea mirrors the sky
                // like a real ocean on a clear day.
                float3 reflectDir = reflect(-viewDir, n);
                 float3 skyRef = (GalilegoSkyAmbient(reflectDir) * waterRad * 1.0
                     + (_SunLightColor * waterRad * 0.06 * cloudShadow)) * dayLocal;
                // Broad sun path on the water (sheen toward the sun over the mirror;
                // the glitter itself feeds the spec below).
                float sunPath = pow(saturate(dot(reflectDir, sunDir)), 10.0);
                 skyRef += _SunLightColor * (sunPath * 0.05 * waterRad * cloudShadow) * dayLocal;
                color = lerp(color, skyRef, saturate(fresnelF * _FresnelStrength));
                // Distant water haze: the horizon merges into the sky.
                // Boosted for open sea — oceans all the way to the horizon.
                float horizonT = (1.0 - waveFade) * saturate(fresnelPow + 0.35);
                color = lerp(color, skyRef, horizonT * 0.65 * _FresnelStrength);

                // --- Shore foam ----------------------------------------------
                float slopeW = fwidth(input.depthMeters);
                float bandW = max(max(0.5, _FoamDepthMeters), slopeW * max(0.0, _FoamSlopeGain));
                float foamBand = 1.0 - smoothstep(0.0, bandW, depth);
                float foamFreqBoost = 1.0 + (2.0 * exp(-cameraDist * 0.01));
                // Foam FBM in two opposite layers (a procedural-ocean trick): tears
                // regular bands into lace. A weak depth term leaves a hint of surf
                // lines at the shore.
                // Scales are multiples of kBase so foam also fits the period P.
                float foamN1 = wfbm(patternUv * (kBaseUw * 228.0) + float2(t * 0.15, -t * 0.11));
                float foamN2 = wfbm(patternUv * (kBaseUw * 456.0) - float2(t * 0.12, t * 0.16));
                float foamNoise = saturate(foamN1 * 0.6 + foamN2 * 0.4 + 0.15
                    + (0.1 * sin((depth * 1.7 * foamFreqBoost) - (t * 2.2))));
                // Lace, not a blanket: noise through a threshold gives torn foam
                // patches with clean turquoise between them. 0.70-0.92 keeps the
                // foam at ~10-15% of the band area — lace visible at the shoreline,
                // no wash-out.
                float foamLace = smoothstep(0.70, 0.92, foamNoise);
                float foamBase = saturate(foamBand * foamLace * shadingFade * 0.9 + (foamBand * foamBand * 0.03)
                    + (foamBand * crest01 * _FoamCrestStrength * shadingFade * 0.5));
                // Running surf: bright lines wash onto the shore. The band is narrow
                // and close to the shoreline, otherwise the whole shelf whitens.
                const float2 kSurfA = float2(57.0, 68.0);
                const float2 kSurfB = float2(-50.0, 44.0);
                float surfNoise = sin(dot(patternUv, kSurfA) * kBaseUw + (t * 0.9))
                    + (0.5 * sin(dot(patternUv, kSurfB) * kBaseUw - (t * 0.7)));
                float surfPhase = (depth * 2.1) - (t * _SurfSpeed) + (surfNoise * 1.5);
                float surfLines = smoothstep(0.78, 0.98, (sin(surfPhase) * 0.5 + 0.5));
                float surfAtten = 1.0 - smoothstep(0.0, max(1.0, _FoamDepthMeters * 0.9), depth);
                float surf = surfLines * surfAtten * _SurfStrength * shadingFade * 0.5;
                // Whitecaps in open sea: foam only where the crest is high AND the
                // noise agrees (a Dupuy-style threshold) — sparse torn patches rather
                // than a milky wash. Fades at distance.
                float capN = wfbm(patternUv * (kBaseUw * 326.0) + float2(-t * 0.2, t * 0.14));
                float whitecaps = smoothstep(1.0, 1.15, crest01 * 0.8 + capN * 0.4)
                    * _FoamCrestStrength * 0.7 * shadingFade;
                float foam = saturate(foamBase + surf + whitecaps);

                // --- Soft shore from vertex depth (no depth buffer) -----------
                // Reading the depth buffer (LoadCameraDepth) in an HDRP transparent
                // pass can give an empty frame on some configurations, so the edge
                // comes from the chunk's vertex depth (waterExtra.x): robust on any
                // hardware and LOD.
                // Shore foam and shallow color come from the same depth.
                float shoreSoft = saturate(depth / max(0.25, _ShoreFadeMeters));
                {
                    // Shoreline foam — the same lace within the foamBand (1-2 m),
                    // not a fill of everything shallower than ShoreFade (8 m).
                    float shoreFoam = foamBand * smoothstep(0.70, 0.92, foamNoise);
                    foam = max(foam, shoreFoam);
                    color = lerp(_WaterShallow.rgb * lightTerm * 1.05, color, smoothstep(0.0, 0.7, shoreSoft));
                }
                // Foam albedo is near-white with no extra boost.
                color = lerp(color, _FoamColor.rgb * lightTerm, foam * 0.75);

                // --- Alpha: below ~8 m the water is fully opaque ---------------
                // Shallows are semi-transparent (the bottom shows through), depth and
                // foam are nearly opaque. There is no blending (AlphaTest queue):
                // transparency is stochastic (screen-door via clip below) — discarded
                // pixels show the bottom under the water.
                // Depth is additionally read through color (waterBase).
                float shallowT = smoothstep(0.0, 8.0, depth);
                // The partial-transparency band is NARROW, in metres, not a "shader
                // range": on a gentle shelf (measured: depths 1.8..30 m per kilometre)
                // a fixed 2 m would stretch over hundreds of metres of shoreline. Only
                // the thin edge itself is semi-transparent (_ShallowAlphaMeters);
                // beyond that the water is dense.
                float alphaT = smoothstep(0.0, max(0.02, _ShallowAlphaMeters), depth);
                float alphaBase = lerp(_MinAlpha, _MaxAlpha, max(alphaT, fresnelPow));
                float alphaShore = max(foam * 0.98, _ShallowAlpha * (1.0 - (alphaT * 0.35)));
                alpha = clamp(max(alphaBase, alphaShore), 0.0, 1.0);
                }
                // 1px IGN screen-door: fine grain instead of blending (HDRP doesn't
                // draw our pass in Transparent). No quantization by world cells.
                // Above water only: from below the ceiling is solid (alpha=1, discard
                // forbidden), otherwise diving covers the screen in grain.
                if (!isUnderwater)
                {
                    float ign = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));
                    clip(alpha - ign);
                }
                return float4(color, 1.0);
            }
            ENDHLSL
        }
        // Water must enter the depth pyramid. Otherwise transparent shaders
        // (atmosphere, stars, sun disc) treat water pixels as sky in IsSky() and
        // draw sky OVER the ocean. At the horizon the path through the atmosphere
        // is huge, so this yields a flat opaque band of atmosphere color exactly at
        // the horizon line: the ocean beneath is hidden and the band follows the
        // shoreline silhouette. Terrain already covers this with its
        // DepthForwardOnly pass (PlanetSurface.shader); water does not.
        // HDRP requires DepthForwardOnly on forward materials
        // (see HDRenderPipeline.RenderGraph.cs:952-956).
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
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // The same globals and the same wave as in WaterSurfaceForward: the
            // displacement must be BIT-FOR-BIT identical. Otherwise the prepass sits
            // on the undisturbed surface, and a forward vertex with the wave below it
            // falls under its own depth and is culled by ZTest LEqual — black holes
            // in the water in wave troughs.
            float3 _PlanetCameraPos;
            float3 _PlanetWaterCenter;
            float _WaterTime;
            float _WaveScale;
            float _WaveAmplitude;
            float4x4 _WaterWorldToBody;
            float3 _WaterBodyAnchor;

            float3 WaterBodyFixedDepth(float3 positionWS)
            {
                float3 relBody = mul((float3x3)_WaterWorldToBody, positionWS - _PlanetCameraPos);
                return relBody + _WaterBodyAnchor;
            }

            float WaterWaveHeight01Depth(float2 uv, float t, out float2 gradTangent)
            {
                float2 p = uv;
                // The same integer K and the same kBase as in WaterWaveHeight01:
                // otherwise the ceiling (Depth pass) and the surface above diverge.
                const float kBase = 0.006135923151542565;  // 2*pi/1024
                const float2 K1 = float2(156.0, 47.0);
                const float2 K2 = float2(-153.0, 231.0);
                const float2 K3 = float2(311.0, -311.0);
                const float2 K4 = float2(-144.0, -719.0);
                const float2 K5 = float2(948.0, -635.0);
                float L1 = length(K1); float L2 = length(K2);
                float L3 = length(K3); float L4 = length(K4);
                float L5 = length(K5);
                float2 d1 = K1 / L1; float2 d2 = K2 / L2;
                float2 d3 = K3 / L3; float2 d4 = K4 / L4;
                float2 d5 = K5 / L5;
                float f1 = L1 * kBase; float f2 = L2 * kBase;
                float f3 = L3 * kBase; float f4 = L4 * kBase;
                float f5 = L5 * kBase;
                float p1 = dot(p, K1) * kBase + (t * 1.1);
                float p2 = dot(p, K2) * kBase - (t * 1.35);
                float p3 = dot(p, K3) * kBase + (t * 1.9);
                float p4 = dot(p, K4) * kBase - (t * 2.6);
                float p5 = dot(p, K5) * kBase + (t * 3.4);
                float s1 = sin(p1);
                float s2 = sin(p2);
                float s3 = sin(p3);
                float s4 = sin(p4);
                float s5 = sin(p5);
                float h = (0.38 * s1) + (0.26 * s2) + (0.18 * s3) + (0.11 * s4) + (0.07 * s5);
                float qShape = 0.25;
                float dhds = 1.0 + (2.0 * qShape * h);
float2 grad = ((0.38 * f1 * d1) * cos(p1)
                    + (0.26 * f2 * d2) * cos(p2)
                    + (0.18 * f3 * d3) * cos(p3)
                    + (0.11 * f4 * d4) * cos(p4)
                    + (0.07 * f5 * d5) * cos(p5)) * dhds;
                // Gradient in the same coordinates it was computed in (see
                // WaterBodyFixed); the caller converts it to render axes via
                // _WaterWorldToBody.
                gradTangent = grad;
                return h + (qShape * h * h);
            }

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
                float3 baseWS = TransformObjectToWorld(input.positionOS);
                float3 centerDeltaV = baseWS - _PlanetWaterCenter;
                float3 baseN = dot(centerDeltaV, centerDeltaV) > 1.0
                    ? normalize(centerDeltaV)
                    : TransformObjectToWorldNormal(float3(0.0, 0.0, 1.0));
                float camDistV = length(_PlanetCameraPos - baseWS);
                float fadeV = exp(-camDistV * 0.00012);
                float2 gradDepth;
                float h01 = WaterWaveHeight01Depth(
                    WaterBodyFixedDepth(baseWS).xz * _WaveScale, _WaterTime, gradDepth);
                float h = _WaveAmplitude * h01 * fadeV;
                output.positionCS = TransformWorldToHClip(baseWS + (baseN * h));
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
