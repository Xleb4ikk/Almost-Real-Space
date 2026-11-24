// Camera-optics glare around the star: veiling glare, aperture ghosts and the
// diffraction star of the iris blades.
//
// This is the light that scatters INSIDE the observer's optics, between the scene
// and the sensor, so unlike Galilego/StarGlare (an atmospheric aureole that fades
// to zero in vacuum) it is present in space as well. The pass runs at
// BeforePostProcess and adds scene radiance, so exposure, bloom and tonemapping
// apply to it exactly as they apply to the star disc.
//
// Every amplitude is computed on the CPU by Galilego.Core.LensOptics from the
// rendered disc radiance V' and the star's angular radius; the shader only
// evaluates the shapes:
//   * veil   — Moffat glare spread function around the source;
//   * ghosts — the aperture polygon reflected through the optical axis;
//   * star   — N thin spikes with a 1/θ radial profile, λ/D wide.
// Occlusion is shared with the disc/glare/corona proxies (GalilegoStarOcclusion.hlsl):
// an opaque hull between the eye and the star kills the whole flare, because the
// entrance pupil receives no light at all.

Shader "Galilego/StarLensFlare"
{
    Properties
    {
        _FlareVeil("Veil: peak radiance / theta0 / beta", Vector) = (0, 0.0262, 2.2, 0)
        _FlareStar("Star: blades / peak / width / extent", Vector) = (6, 0, 0.0007, 0.08)
        _FlareGhostCount("Ghost count", Float) = 0
        _FlareChroma("Ghost chromatic fringe", Float) = 0.012
        _FlareDebug("Debug mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "StarLensFlare"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
            #include "GalilegoStarOcclusion.hlsl"

            #define FLARE_TWO_PI 6.283185307179586
            #define FLARE_PI 3.141592653589793

            // xy: direction TO the star in NDC (y up), z: angular radius in NDC-y,
            // w: angular radius in radians.
            float4 _FlareSun;
            // xy: tan(half FOV) x/y, z: aspect, w: unused.
            float4 _FlareScreen;
            // rgb: star colour (black body × atmospheric transmittance), unit scale.
            float4 _FlareColor;
            // x: veil peak radiance, y: Moffat theta0 (rad), z: beta, w: unused.
            float4 _FlareVeil;
            // The wide scattering term: same packing as _FlareVeil.
            float4 _FlareVeilWide;
            // x: iris blades, y: spike peak radiance, z: spike width (tan units),
            // w: spike extent (tan units).
            float4 _FlareStar;
            // xy: ghost centre NDC, z: ghost angular radius NDC-y, w: peak radiance.
            float4 _FlareGhostPose[6];
            // rgb: coating tint × star colour; a: aperture image rotation (rad).
            float4 _FlareGhostColor[6];
            float _FlareGhostCount;
            float _FlareChroma;
            float _FlareGhostRim;
            float _FlareBladePhase;
            float _FlareDebug;

            // Same contract as StarPixelOccluded, but reading through the custom
            // pass API: at BeforePostProcess CustomPassLoadCameraDepth() resolves
            // to LoadCameraDepth(), so the answer is identical to the star proxies.
            float FlarePixelOccluded(uint2 pixel)
            {
                float rawDepth = CustomPassLoadCameraDepth(pixel);
                if (IsSky(rawDepth))
                {
                    return 0.0;
                }

                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                return (eyeDepth > max(_SunSelfOccluder, 0.05) && eyeDepth < _SunFarLimit) ? 1.0 : 0.0;
            }

            // Fraction of the star's disc that actually reaches the entrance pupil.
            // Five taps over the disc footprint: a strut crossing the Sun dims the
            // flare instead of switching it off the moment it covers the centre.
            float FlareSunCoverage(float2 sunNdc, float sunRadiusNdc)
            {
                float2 size = _ScreenSize.xy;
                float2 centre = ((sunNdc * 0.5) + 0.5) * size;
                float2 limit = size - 1.0;
                float stepPx = max(1.0, 0.5 * sunRadiusNdc * size.y);

                float open = 0.0;
                open += 1.0 - FlarePixelOccluded((uint2)clamp(centre, 0.0, limit));
                open += 1.0 - FlarePixelOccluded((uint2)clamp(centre + float2(stepPx, 0.0), 0.0, limit));
                open += 1.0 - FlarePixelOccluded((uint2)clamp(centre - float2(stepPx, 0.0), 0.0, limit));
                open += 1.0 - FlarePixelOccluded((uint2)clamp(centre + float2(0.0, stepPx), 0.0, limit));
                open += 1.0 - FlarePixelOccluded((uint2)clamp(centre - float2(0.0, stepPx), 0.0, limit));
                return open * 0.2;
            }

            // Coverage of the aperture image (circumradius 1) at p. `softness` is
            // the convolution edge: the source's angular radius relative to the
            // ghost radius, so a ghost barely larger than the Sun has a soft edge
            // and a small ghost is overfilled by the disc.
            float ApertureBody(float2 p, int blades, float phase, float softness)
            {
                float radius = length(p);
                float limit;
                float c;

                if (blades < 3)
                {
                    // Round iris: the ghost is a disc.
                    limit = 1.0;
                    c = radius;
                }
                else
                {
                    float sector = FLARE_TWO_PI / (float)blades;
                    float angle = atan2(p.y, p.x);
                    float rel = frac(((angle - phase) / sector) + 0.5) - 0.5;
                    c = radius * cos(rel * sector);
                    limit = cos(FLARE_PI / (float)blades);
                }

                // Anti-reflection coating reflectance grows with the angle of
                // incidence, so a real aperture ghost is brightest toward the rim of
                // the pupil image and flatter in the middle. edge01 = 0 at the rim.
                float edge01 = saturate((limit - c) / max(limit, 1e-4));
                float rim = 1.0 + (_FlareGhostRim * exp(-pow(edge01 / 0.10, 2.0)));

                float aa = max(max(softness, fwidth(c)), 1e-5);
                return saturate(((limit - c) / aa) + 0.5) * rim;
            }

            // Per-channel aperture image: lateral chromatic aberration makes the
            // blue ghost slightly larger than the red one, so the polygon's edge
            // picks up the coating colour.
            float3 ApertureBodyChromatic(float2 p, int blades, float phase, float softness)
            {
                float3 body;
                body.r = ApertureBody(p * (1.0 + _FlareChroma), blades, phase, softness);
                body.g = ApertureBody(p, blades, phase, softness);
                body.b = ApertureBody(p * (1.0 - _FlareChroma), blades, phase, softness);
                return body;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.positionCS.xy * _ScreenSize.zw;
                float2 ndc = (uv * 2.0) - 1.0;
                uint2 pixel = (uint2)input.positionCS.xy;

                float2 sun = _FlareSun.xy;
                float sunRadiusNdc = max(_FlareSun.z, 1e-6);

                float coverage = FlareSunCoverage(sun, sunRadiusNdc);
                if (_FlareDebug > 0.5 && _FlareDebug < 1.5)
                {
                    return float4(coverage.xxx, 1.0);
                }

                if (coverage <= 0.0)
                {
                    discard;
                }

                float2 dNdc = ndc - sun;
                // Image-plane offset in tan units (≈ radians for the small angles
                // the veil lives in). Spikes and ghost centres are straight lines
                // in this space, which is what a real focal plane is.
                float2 dTan = dNdc * _FlareScreen.xy;

                int blades = (int)round(_FlareStar.x);

                // ---- veiling glare -------------------------------------------------
                // Two scattering populations: a dense core (surface/interface
                // microroughness) and a wide haze (bulk scattering). For the eye
                // that is the cornea and the lens; for a camera, the glass and its
                // coatings. Each term is a Moffat GSF with its own share, computed
                // on the CPU by LensOptics.
                float veil = 0.0;
                if (_FlareVeil.x > 0.0 || _FlareVeilWide.x > 0.0)
                {
                    float theta = atan(length(dTan));
                    float qCore = theta / max(_FlareVeil.y, 1e-6);
                    float qWide = theta / max(_FlareVeilWide.y, 1e-6);
                    veil = (_FlareVeil.x * pow(1.0 + (qCore * qCore), -_FlareVeil.z))
                        + (_FlareVeilWide.x * pow(1.0 + (qWide * qWide), -_FlareVeilWide.z));

                    // The halo is a smooth 1/θ^b falloff over a black sky: a few
                    // hundredths of a stop of noise is enough to hide the 8-bit
                    // steps of the tonemapper, and TAA averages it away.
                    veil *= 1.0 + ((InterleavedGradientNoise(pixel, 0) - 0.5) * 0.02);
                }

                // ---- aperture ghosts -----------------------------------------------
                float3 ghosts = 0.0;
                if (_FlareGhostCount > 0.0)
                {
                    [unroll]
                    for (int i = 0; i < 6; i++)
                    {
                        if (i >= (int)_FlareGhostCount)
                        {
                            break;
                        }

                        float4 pose = _FlareGhostPose[i];
                        if (pose.w <= 0.0)
                        {
                            continue;
                        }

                        float2 q = float2((ndc.x - pose.x) * _FlareScreen.z, ndc.y - pose.y)
                            / max(pose.z, 1e-5);
                        if (dot(q, q) > 2.25)
                        {
                            continue;
                        }

                        float softness = _FlareSun.z / max(pose.z, 1e-5);
                        float3 body = ApertureBodyChromatic(
                            q,
                            blades,
                            _FlareBladePhase + _FlareGhostColor[i].w,
                            max(softness, 1.0 / _ScreenSize.y));
                        ghosts += _FlareGhostColor[i].rgb * (pose.w * body);
                    }
                }

                // ---- diffraction star ----------------------------------------------
                float spikes = 0.0;
                if (_FlareStar.y > 0.0)
                {
                    int lines = (blades < 3) ? 0 : ((blades % 2) == 0 ? blades / 2 : blades);
                    float width = max(_FlareStar.z, 1e-6);
                    float extent = max(_FlareStar.w, 1e-5);
                    float alongRadius = length(dTan);
                    float fade = exp(-alongRadius / extent);

                    [unroll]
                    for (int i = 0; i < 6; i++)
                    {
                        if (i >= lines)
                        {
                            break;
                        }

                        float angle = _FlareBladePhase + (FLARE_TWO_PI * (float)i / (float)blades);
                        float2 dir = float2(cos(angle), sin(angle));
                        float perp = dot(dTan, float2(-dir.y, dir.x));
                        float along = abs(dot(dTan, dir));

                        // A spike is a thin ridge: Gaussian across, 1/θ along, so its
                        // integrated flux falls off as 1/θ like a real diffraction
                        // spike.
                        float ridge = exp(-(perp * perp) / (width * width));
                        float radial = width / (along + width);
                        spikes += ridge * radial;
                    }

                    // …and the whole star is cut off at the diffraction extent,
                    // otherwise the 1/θ tail would reach the frame corner.
                    spikes *= fade;
                }

                float3 flare;
                if (_FlareDebug > 1.5 && _FlareDebug < 2.5)
                {
                    flare = _FlareColor.rgb * veil;
                }
                else if (_FlareDebug > 2.5 && _FlareDebug < 3.5)
                {
                    flare = ghosts;
                }
                else if (_FlareDebug > 3.5)
                {
                    flare = _FlareColor.rgb * (_FlareStar.y * spikes);
                }
                else
                {
                    flare = _FlareColor.rgb
                        * (veil + (_FlareStar.y * spikes))
                        + ghosts;
                }

                flare *= coverage;
                if (max(flare.r, max(flare.g, flare.b)) <= 1e-7)
                {
                    discard;
                }

                return float4(flare, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
