Shader "Galilego/PlanetAtmosphere"
{
    // Planet atmosphere: camera-centred dome (PlanetAtmosphereView), analytic
    // ray-shell intersection, single-scattering raymarch + multiple-scattering
    // approximation (LUT, Hillaire EGSR 2020).
    //
    // Physics: β comes from C# (AtmosphereOptics — single source shared with
    // CPU SkyPhysics); the Sun transmittance and multiple-scattering LUTs are
    // generated on the CPU and only sampled here.
    //
    // Horizon: the ray path is mathematically discontinuous at the tangent
    // (≈2 km to the surface ↔ ≈hundreds of km to the shell exit). To avoid a
    // hard step, sky pixels that hit the planet sphere evaluate TWO
    // contributions — "ground" (short path) and "sky" (full path) — blended
    // smoothly in a narrow window just below the tangent (HorizonFade). Where
    // there is no terrain, the sphere is filled with shaded, hazy ground
    // instead of dark clear colour.
    //
    // Composite: the opaque scene colour is already in the buffer — we sample
    // it, attenuate by transmittance and add in-scatter. Blend One Zero means
    // replace, with the background handled manually.
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "PlanetAtmosphereForward"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One Zero
            ZWrite Off
            ZTest Always
            // Cull Back, not Cull Off: the dome is centred on the camera
            // (PlanetAtmosphereView: shell.position = cameraPosition), so the
            // camera is inside the closed sphere; Cull Off would rasterise the
            // fullscreen pass twice with an identical result. Not Cull Front:
            // BuildSphereMesh winds triangles with the normal pointing inward,
            // so the dome's inside is its front face and Cull Front would
            // remove it entirely.
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // Shared with Galilego/BodyFarDisc: the camera/body/sun uniforms, the
            // optics uniforms, the two CPU LUTs and the march itself. One copy,
            // because the Moon's far disc must add the SAME air this dome does —
            // a second implementation drifts and leaves a circle at the disc edge.
            #include "GalilegoAtmosphereAir.hlsl"

            // Dome-only state: no other consumer of the air needs it.
            float3 _AtmCameraForwardWS;
            float _AtmPlanetOcclusion;
            float _AtmHorizonFade;
            float4 _AtmGroundColor;
            float _AtmDebugMode;
            // Set by PlanetAtmosphereView (IgnoreCockpitDepth): when 1, a depth nearer
            // than the atmosphere entry is treated as the observer's own craft and
            // ignored. Off by default: an opaque cockpit wall must keep its own colour.
            float _AtmIgnoreCockpitDepth;

            // Closer than this, a non-sky depth is the observer's own craft (hull,
            // hatch frame, canopy) and must still occlude the atmosphere; farther,
            // while the camera is above the shell, it is the planet — whose depth is
            // quantized by the screen-space projection and replaced by the analytic
            // sphere.
            #define ATM_NEAR_OCCLUDER_M 1000.0

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.dir = input.positionOS; // unit-radius dome, centre = camera
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 rd = normalize(input.dir);
                float3 ro = _AtmCameraWS - _AtmBodyCenterWS;
                float3 sunDir = normalize(_AtmSunDirWS);

                float a0;
                float a1;
                if (!AtmRaySphere(ro, rd, _AtmRadius, a0, a1))
                {
                    discard;
                }

                float tAtm = max(a1, 0.0);
                if (tAtm <= 0.0)
                {
                    discard;
                }

                // Where the march STARTS. The camera is usually ABOVE the shell (orbit),
                // and the old code integrated from 0: most of the (small) step budget was
                // spent on vacuum. At 700 km altitude the 24-step budget left ~3 samples
                // inside the 100 km atmosphere, so the haze over the planet's disc almost
                // vanished — while the thin limb ring (a much longer path) still looked
                // plausible. Start at the shell entry instead (0 when the camera is inside).
                float tAtmStart = max(a0, 0.0);

                float r0 = length(ro);
                float3 n0 = ro / max(r0, 1e-3);

                // Scene depth: path to the actual terrain. Sky pixels are
                // detected by the standard IsSky (raw depth = far clip).
                uint2 pixel = uint2(input.positionCS.xy);
                float cosFwd = dot(rd, _AtmCameraForwardWS);
                bool hasTerrain = false;
                float tDepth = 1e30;
                float eyeDepth = 1e30;
                if (cosFwd > 1e-4)
                {
                    eyeDepth = LinearEyeDepth(LoadCameraDepth(pixel), _ZBufferParams);
                    hasTerrain = !IsSky(pixel) && eyeDepth > 0.0;
                    if (hasTerrain)
                    {
                        tDepth = eyeDepth / cosFwd;
                    }
                }

                // Sea-level sphere — occluder/fallback ground.
                float p0;
                float p1;
                bool hitPlanet = false;
                float tPlanet = 1e30;
                if (AtmRaySphere(ro, rd, _AtmPlanetRadius, p0, p1) && p1 > 0.0)
                {
                    hitPlanet = true;
                    tPlanet = max(p0, 0.0);
                }

                bool planetOccludes = hitPlanet && _AtmPlanetOcclusion > 0.5;

                // The scene depth is a SCREEN-SPACE value. With the cockpit's 3 cm
                // near plane against a far plane of hundreds of km, its float clip
                // precision at planetary distances is coarse: at 200 km the
                // reconstructed distance snaps between ~126 and ~252 km. A cockpit
                // surface (hull, hatch frame, canopy) is metres from the eye; anything
                // the depth reports farther than ATM_NEAR_OCCLUDER_M while the camera
                // is above the atmosphere is the planet itself, so the exact analytic
                // sphere replaces the quantized depth. Without this the depth can land
                // inside the atmosphere entry and erase the haze over the disc — the
                // limb survives only because it integrates its own tangent path. The
                // effect flips with the near plane (3 cm seated vs 10 cm in EVA),
                // which is why the haze "appeared" on leaving the craft.
                if (hasTerrain && hitPlanet && r0 > _AtmRadius && tDepth > ATM_NEAR_OCCLUDER_M)
                {
                    tDepth = tPlanet;
                }

                // Outside the shell nothing physical can be closer than the atmosphere
                // entry: a nearer depth would be the observer's own craft (cockpit hull,
                // hatch frame, canopy). Ignore it — otherwise a cockpit surface cuts the
                // path to ~1 m and the planet loses its haze/limb, while the planet
                // itself (an opaque mesh) still shows through. The real backdrop along
                // the ray is the planet, or empty space when the ray misses it.
                // Opt-in (IgnoreCockpitDepth): an OPAQUE cockpit wall must not be turned
                // into "planet + haze" — it is the wall's own colour that must stay.
                if (_AtmIgnoreCockpitDepth > 0.5
                    && hasTerrain && r0 > _AtmRadius && tDepth < max(a0, 0.0))
                {
                    hasTerrain = hitPlanet;
                    tDepth = hitPlanet ? tPlanet : 1e30;
                }

                // Background colour — the colour pyramid with dynamic resolution
                // (HDRP Lit.hlsl pattern, transparent refraction).
                float2 screenUv = input.positionCS.xy * _ScreenSize.zw;
                float2 colorUv = min(screenUv * _RTHandleScaleHistory.xy,
                    _ColorPyramidUvScaleAndLimitCurrentFrame.zw);
                float3 bgRaw = SAMPLE_TEXTURE2D_X_LOD(
                    _ColorPyramidTexture, s_trilinear_clamp_sampler, colorUv, 0).rgb;

                float3 radianceScale = _AtmSunColor * (_AtmIntensity * ATM_RADIANCE_SCALE);

                bool hasGround = hasTerrain || planetOccludes;
                float tGround = min(tDepth, planetOccludes ? tPlanet : 1e30);
                tGround = min(tGround, tAtm);

                float3 groundT = 1.0;
                float3 skyT = 1.0;
                float3 groundColor = bgRaw;
                if (hasGround)
                {
                    float3 groundBg = bgRaw;
                    if (!hasTerrain && planetOccludes)
                    {
                        // Fallback ground: shaded Lambert with Sun
                        // transmittance, so undrawn terrain shows hazy
                        // ground instead of a hole.
                        float3 hp = ro + (rd * tPlanet);
                        float3 N = hp / max(length(hp), 1e-3);
                        float cosSunG = dot(N, sunDir);
                        float3 sunG = AtmSampleTransmittance(0.0, cosSunG);
                        groundBg = _AtmGroundColor.rgb * saturate(cosSunG) * sunG;
                    }

                    float3 groundScatter = AtmIntegrate(ro, rd, sunDir, tAtmStart, tGround, groundT);
                    groundColor = (groundBg * groundT) + (groundScatter * radianceScale);
                }

                // Ray zenith cosine and tangent boundary.
                float cosChi = dot(n0, rd);
                float sinHor = clamp(_AtmPlanetRadius / max(r0, _AtmPlanetRadius), 0.0, 1.0);
                float cosHor = -sqrt(max(0.0, 1.0 - (sinHor * sinHor)));
                float horizon = smoothstep(-max(0.001, _AtmHorizonFade), 0.0, cosChi - cosHor);

                float3 skyColor = 0.0;
                if (!hasTerrain && !planetOccludes)
                {
                    float3 skyScatter = AtmIntegrate(ro, rd, sunDir, tAtmStart, tAtm, skyT);
                    skyColor = (bgRaw * skyT) + (skyScatter * radianceScale);
                }

                float3 color;
                if (hasTerrain)
                {
                    color = groundColor;
                }
                else if (planetOccludes)
                {
                    color = groundColor;
                    if (horizon > 0.001)
                    {
                        // Sky exactly along the tangent direction (not blocked
                        // by the planet) — a continuous reference for the
                        // blend. Integrating "through" the planet would
                        // produce a bright false band near the surface.
                        float3 dirHorDelta = rd + ((cosHor - cosChi) * n0);
                        if (dot(dirHorDelta, dirHorDelta) > 1e-12)
                        {
                            float3 dirHor = normalize(dirHorDelta);
                            float ha0;
                            float ha1;
                            float tAtmHor = tAtm;
                            float tHorStart = tAtmStart;
                            if (AtmRaySphere(ro, dirHor, _AtmRadius, ha0, ha1))
                            {
                                tAtmHor = max(ha1, 0.0);
                                tHorStart = max(ha0, 0.0);
                            }

                            float3 horT;
                            float3 horScatter = AtmIntegrate(ro, dirHor, sunDir, tHorStart, tAtmHor, horT);
                            float3 skyHorizonColor = (bgRaw * horT) + (horScatter * radianceScale);
                            color = lerp(groundColor, skyHorizonColor, horizon);
                        }
                    }
                }
                else
                {
                    color = skyColor;
                }

                // --- Debug (phase 0) ---
                if (_AtmDebugMode > 0.5)
                {
                    if (_AtmDebugMode < 1.5)
                    {
                        return float4(hasTerrain ? groundT : skyT, 1.0);
                    }

                    if (_AtmDebugMode < 2.5)
                    {
                        return float4(tAtm / max(1.0, _AtmAtmDepth), 0.0, 0.0, 1.0);
                    }

                    if (_AtmDebugMode < 3.5)
                    {
                        float tEnd = hasGround ? tGround : tAtm;
                        return float4(tEnd / max(1.0, _AtmAtmDepth), 0.0, 0.0, 1.0);
                    }

                    if (_AtmDebugMode < 4.5)
                    {
                        return float4(frac(eyeDepth * 0.0001), 0.0, 0.0, 1.0);
                    }

                    if (_AtmDebugMode < 5.5)
                    {
                        return float4(AtmExtinction(0.0) * 1e5, 1.0);
                    }

                    if (_AtmDebugMode < 6.5)
                    {
                        float3 dbgT;
                        float3 dbgScatter = AtmIntegrate(ro, rd, sunDir, tAtmStart,
                            hasGround ? tGround : tAtm, dbgT);
                        return float4(dbgScatter * radianceScale, 1.0);
                    }

                    if (_AtmDebugMode < 7.5)
                    {
                        float3 hp = ro + (rd * min(hasGround ? tGround : tAtm, 1000.0));
                        float3 N = hp / max(length(hp), 1e-3);
                        return float4(AtmSampleTransmittance(max(0.0, length(hp) - _AtmPlanetRadius), dot(N, sunDir)), 1.0);
                    }

                    if (_AtmDebugMode < 8.5)
                    {
                        return float4(_RTHandleScaleHistory.xy, 0.0, 1.0);
                    }

                    return float4(colorUv, 0.0, 1.0);
                }

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
