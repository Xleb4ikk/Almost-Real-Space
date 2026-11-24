Shader "Galilego/PlanetSurface"
{
    Properties
    {
        _WaterDeep("Water Deep", Color) = (0.02, 0.09, 0.22, 1)
        _WaterShallow("Water Shallow", Color) = (0.10, 0.32, 0.52, 1)
        _SpecularPower("Specular Power", Float) = 120.0
        _SpecularIntensity("Specular Intensity", Float) = 0.8
        _RimColor("Water Sky Rim", Color) = (0.35, 0.55, 0.85, 1)
        // _ShoreWetMeters/_ShoreWetTint are intentionally NOT in Properties:
        // a material property overrides the global, and the runtime material
        // (new Material(shader)) would silently use its default. Values come
        // only from PlanetSurfaceRenderer.ApplyTerrainGlobals, like
        // _BeachHeightMeters. _TexLow/_TexMid/_TexHigh/_TexSteep/_TexOcclusion
        // are also not in Properties: otherwise the runtime material gets its
        // default "white" textures, which override the PlanetSurfaceRenderer
        // globals.
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "PlanetSurfaceForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoLighting.hlsl"
            #include "GalilegoUnderwaterCaustics.hlsl"

            // Global, set by PlanetSurfaceRenderer (world camera position).
            float3 _PlanetCameraPos;

            // Terrain texture triplanar UVs are camera-relative: world-position
            // deltas from the camera are small and precise (floating origin),
            // rotated into body-fixed axes by _TerrainWorldToBody (set by
            // PlanetSurfaceRenderer each frame). Absolute coordinates ~1.14e6 m
            // quantise to ~6 cm in float (~2.5 texels at scale 0.04).
            float4x4 _TerrainWorldToBody;
            float4 _TerrainUVPhase0; // uvX.xy: (z, y), uvY.xy: (x, z)
            float4 _TerrainUVPhase1; // uvZ.xy: (x, y)

            float4 _WaterDeep;
            float4 _WaterShallow;
            float4 _RimColor;
            float _SpecularPower;
            float _SpecularIntensity;

            // Per-pixel albedo palette/noise: set by PlanetSurfaceRenderer from
            // HeightfieldTerrain + TerrainPalette (single source of truth).
            float _TerrainAmplitude;
            float _TerrainSeaLevel;
            float3 _TerrainBodyCenterWS;
            float _TerrainSeed;
            float _TerrainGain;
            float _TerrainLacunarity;
            float _TerrainRockSlopeTan;
            float _TerrainRockSlopeWidth;
            float _TerrainRockHeightMin;
            float _TerrainSnowSlopeTan;
            float _ColorNoiseFrequency;
            float _ColorNoiseOctaves;
            float _ColorNoiseStrength;
            float _ColorDetailFrequency;
            float _ColorDetailOctaves;
            float _ColorDetailStrength;
            float _BeachHeightMeters;
            float _ShoreWetMeters;
            float4 _ShoreWetTint;

            float4 _ColSand;
            float4 _ColDesert;
            float4 _ColDryGrass;
            float4 _ColGrass;
            float4 _ColForest;
            float4 _ColTundra;
            float4 _ColRock;
            float4 _ColSnow;
            float4 _ColSea;
            float4 _ColSoil;
            float4 _ColLush;

            sampler2D _TexLow;
            sampler2D _TexMid;
            sampler2D _TexHigh;
            sampler2D _TexSteep;
            sampler2D _TexOcclusion;
            float _TerrainTextureScale;
            float _TerrainUseTextures;
            // Mean linear albedo of the detail set. >0 = airless regolith: the set is a
            // relative modulation of the palette + macro albedo (see the albedo block).
            // 0 = the photo set REPLACES the colour, the way Terra is authored.
            float _TerrainDetailMean;
            float _LowMidBlendStart;
            float _LowMidBlendEnd;
            float _MidHighBlendStart;
            float _MidHighBlendEnd;
            float _SteepBlendStart;
            float _SteepBlendEnd;

            // Baked macro map (equirect, body-fixed). Global, from PlanetSurfaceRenderer:
            // R = albedo multiplier / 2, normal xyz = n*0.5+0.5 in BODY space.
            sampler2D _TerrainMacroAlbedoMap;
            sampler2D _TerrainMacroNormalMap;
            float _TerrainMacroOn;
            float _TerrainMacroAlbedoStrength;
            float _TerrainMacroNormalStrength;
            float _TerrainMacroTexelAngular;

            // Baked terrain horizon (equirect, body-fixed, 8 azimuth bins). A texel holds
            // the highest terrain elevation angle seen from that point along the bin's
            // azimuth, encoded nonlinearly (see DecodeHorizonAngle). This is what shadows
            // a crater floor when the sun is 1° above the horizon but the rim is 10 km
            // away — far beyond HDRP's cascades.
            sampler2D _TerrainMacroHorizonMapA;
            sampler2D _TerrainMacroHorizonMapB;
            float _TerrainHorizonOn;

            // Direction TO the sun in the same body-fixed frame as dirOS, and the sun's
            // angular radius (radians) — the horizon edge must be as soft as the disc.
            float3 _TerrainSunDirBody;
            float _SunAngularRadiusRad;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float3 dirOS      : TEXCOORD1; // body-fixed unit direction
                float4 extra      : TEXCOORD2; // x: raw height (m), y: slope cosine, z: CPU color mask, w: CPU detail
                float2 extra2     : TEXCOORD3; // x: polar ice weight 0..1 (CPU, same noise as geometry)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 dirOS      : TEXCOORD2;
                float4 extra      : TEXCOORD3;
                float2 extra2     : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.dirOS = input.dirOS;
                output.extra = input.extra;
                output.extra2 = input.extra2;
                return output;
            }

            // --- Procedural noise: UNUSED --------------------------------------
            // Dead code kept from the version where the shader computed the mask
            // itself; Fbm/ValueNoise/StreamOffset are not called. The seed
            // offsets below are small float-safe values (C# seeds are ~1.5e9
            // in double, too large for float).
            float3 StreamOffset(float salt)
            {
                float s = (_TerrainSeed * 0.0001) + (salt * 13.37);
                return float3((s * 1.7) + (salt * 3.1), (s * 2.3) + 41.7, (s * 3.1) + 89.3);
            }

            float Quintic(float t)
            {
                return t * t * t * (t * ((t * 6.0) - 15.0) + 10.0);
            }

            float LatticeValue(int x, int y, int z)
            {
                // uint arithmetic: overflow is defined (wrap mod 2^32),
                // unlike signed int (UB for the optimiser).
                uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u) + ((uint)z * 2147483647u);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return ((h & 0xFFFFu) / 32767.5) - 1.0;
            }

            float ValueNoise(float3 p)
            {
                float xf = floor(p.x);
                float yf = floor(p.y);
                float zf = floor(p.z);
                int ix = (int)xf;
                int iy = (int)yf;
                int iz = (int)zf;
                float fx = p.x - xf;
                float fy = p.y - yf;
                float fz = p.z - zf;
                float ux = Quintic(fx);
                float uy = Quintic(fy);
                float uz = Quintic(fz);

                float c000 = LatticeValue(ix, iy, iz);
                float c100 = LatticeValue(ix + 1, iy, iz);
                float c010 = LatticeValue(ix, iy + 1, iz);
                float c110 = LatticeValue(ix + 1, iy + 1, iz);
                float c001 = LatticeValue(ix, iy, iz + 1);
                float c101 = LatticeValue(ix + 1, iy, iz + 1);
                float c011 = LatticeValue(ix, iy + 1, iz + 1);
                float c111 = LatticeValue(ix + 1, iy + 1, iz + 1);

                float x00 = c000 + (ux * (c100 - c000));
                float x10 = c010 + (ux * (c110 - c010));
                float x01 = c001 + (ux * (c101 - c001));
                float x11 = c011 + (ux * (c111 - c011));
                float y0 = x00 + (uy * (x10 - x00));
                float y1 = x01 + (uy * (x11 - x01));
                return y0 + (uz * (y1 - y0));
            }

            float Fbm(float3 direction, float baseFrequency, int octaves, float3 offset)
            {
                int n = octaves < 1 ? 1 : (octaves > 8 ? 8 : octaves);
                float amplitude = 1.0;
                float frequency = max(baseFrequency, 1e-6);
                float sum = 0.0;
                float norm = 0.0;
                float3 o = offset;
                for (int i = 0; i < 8; i++)
                {
                    if (i >= n)
                    {
                        break;
                    }

                    sum += amplitude * ValueNoise((direction * frequency) + o);
                    norm += amplitude;
                    amplitude *= _TerrainGain;
                    frequency *= _TerrainLacunarity;
                    o = float3(o.y + 19.19, o.z + 7.47, o.x + 3.13);
                }

                return norm > 0.0 ? sum / norm : 0.0;
            }

            // --- Palette (port of TerrainPalette.HeightColorEx) -----------------
            float PaletteSmoothstep(float t)
            {
                t = saturate(t);
                return t * t * (3.0 - (2.0 * t));
            }

            float SlopeTanFromCos(float cosA)
            {
                if (!(cosA > 1e-6))
                {
                    return 1e6;
                }

                float sinA = sqrt(max(0.0, 1.0 - (cosA * cosA)));
                return sinA / cosA;
            }

            float3 BiomeColor(float wet)
            {
                if (wet < 0.18)
                {
                    return _ColDesert.rgb;
                }

                if (wet < 0.38)
                {
                    return lerp(_ColDesert.rgb, _ColDryGrass.rgb, (wet - 0.18) / 0.20);
                }

                if (wet < 0.58)
                {
                    return lerp(_ColDryGrass.rgb, _ColGrass.rgb, (wet - 0.38) / 0.20);
                }

                if (wet < 0.80)
                {
                    return lerp(_ColGrass.rgb, _ColForest.rgb, (wet - 0.58) / 0.22);
                }

                return _ColForest.rgb;
            }

            // Sea-level tolerance is ABSOLUTE, in metres, and nearly independent
            // of amplitude. A constant-height contour on a sphere projects almost
            // as a straight horizontal line across the frame.
            static float SeaEpsilon(float amp)
            {
                return max(0.05, amp * 1e-6);
            }

            // Sand weight by height above sea. Numbers mirror TerrainPalette.
            // SandWeight: full band up to 0.4·beach, zero from 1.6·beach, exactly
            // half at beach. One function for all three places (paint, textures,
            // wet edge) — otherwise the sand boundaries drift apart.
            float SandWeight(float aboveSea)
            {
                return _BeachHeightMeters > 0.0
                    ? 1.0 - smoothstep(_BeachHeightMeters * 0.4, _BeachHeightMeters * 1.6, aboveSea)
                    : 0.0;
            }

            float3 TerrainAlbedo(float raw, float mask, float detail, float slopeTan, float lat01)
            {
                float amp = _TerrainAmplitude;
                float sea = _TerrainSeaLevel;
                if (raw <= sea + SeaEpsilon(amp))
                {
                    return _ColSea.rgb;
                }

                float height = max(raw, sea);
                float aboveSea = height - sea;
                float t = aboveSea / max(1.0, amp);
                bool maskOn = _ColorNoiseStrength != 0.0;
                if (maskOn)
                {
                    t += mask * _ColorNoiseStrength;
                }

                t = max(t, 0.0);

                // Hard ceiling: airless bodies have no sea, so their profile carries
                // SeaLevelMeters = -1e30 as the "no water" sentinel and aboveSea/amp blows
                // up to ~1e26. The rock→snow lerp below then extrapolates past snow instead
                // of stopping at it (a white HDR disc with a bloom halo). Every band is
                // defined on t ∈ [0,1] anyway, so clamp once here.
                t = min(t, 1.0);

                // Sand by height above sea, not erased by the mask: otherwise on
                // "positive" shores greenery starts at the waterline. Sand is an
                // absolute shelf at the water (mirrors TerrainPalette.SandWeight
                // and the BeachHeightMeters decor filter). It is a falloff, not a
                // step: full band to 0.4·beach, zero from 1.6·beach, half at beach.
                float sandW = SandWeight(aboveSea);
                float3 c;
                if (sandW >= 1.0)
                {
                    c = _ColSand.rgb;
                }
                else
                {
                    // wet01 is the SHARED curve with decor (GroundDecorDistribution.
                    // BiomeWetness) and with the CPU palette. Soft, tanh instead of clamp:

                    // the mask is wide (p10=−0.33, p90=+0.32); clamp(0.5+1.6·mask)
                    // saturated 21% of the planet to flat 0/1, producing large dead
                    // zones without grass/trees and flat tonal fills.
                    float wet = maskOn ? (0.5 + 0.5 * tanh(1.6 * clamp(mask, -1.0, 1.0))) : 0.5;
                    float3 lowland = BiomeColor(wet);
                    if (t < 0.45)
                    {
                        c = lowland;
                    }
                    else if (t < 0.70)
                    {
                        c = lerp(lowland, _ColRock.rgb, (t - 0.45) / 0.25);
                    }
                    else
                    {
                        c = lerp(_ColRock.rgb, _ColSnow.rgb, (t - 0.70) / 0.30);
                    }

                    float tundra = PaletteSmoothstep((lat01 - 0.52) / 0.22);
                    float ice = PaletteSmoothstep((lat01 - 0.70) / 0.16);
                    if (tundra > 0.0)
                    {
                        c = lerp(c, _ColTundra.rgb, tundra * 0.85);
                    }

                    if (ice > 0.0)
                    {
                        c = lerp(c, _ColSnow.rgb, ice);
                    }

                    // Sand over the biome: in the falloff the green biome shows
                    // through the sand instead of vanishing at the band edge.
                    c = lerp(c, _ColSand.rgb, sandW);
                }

                // Ground mottling: patches of soil / lush green. Only on land above
                // the beach zone — no patches on sand. The threshold uses sand weight,
                // not a hard height, so patches don't switch on abruptly at the top of
                // the falloff.
                bool isBeach = sandW > 0.0;
                if (_ColorDetailStrength != 0.0 && !isBeach)
                {
                    float d = clamp(detail, -1.0, 1.0);
                    if (d > 0.0)
                    {
                        c = lerp(c, _ColSoil.rgb, d * _ColorDetailStrength);
                    }
                    else
                    {
                        c = lerp(c, _ColLush.rgb, (-d) * _ColorDetailStrength * 0.6);
                    }
                }

                // Rock override: rock only on steep AND sufficiently high slopes
                // (large mountains). The height threshold excludes beach, dunes and
                // lowlands where rock should not appear.
                if (_TerrainRockSlopeTan > 0.0 && slopeTan >= _TerrainRockSlopeTan
                    && t >= _TerrainRockHeightMin && !isBeach)
                {
                    float w = min(1.0, (slopeTan - _TerrainRockSlopeTan) / max(1e-9, _TerrainRockSlopeWidth));
                    w = w * w * (3.0 - (2.0 * w));
                    c = lerp(c, _ColRock.rgb, w);
                }

                // Snow band on steep slopes — rock instead of snow.
                float snowT = (height - sea) / max(1.0, amp);
                if (snowT >= 0.55 && _TerrainSnowSlopeTan > 0.0 && slopeTan > _TerrainSnowSlopeTan)
                {
                    c = _ColRock.rgb;
                }

                return c;
            }

            float3 Triplanar(sampler2D tex, float2 uvX, float2 uvY, float2 uvZ, float3 weights)
            {
                return (tex2D(tex, uvX).rgb * weights.x)
                    + (tex2D(tex, uvY).rgb * weights.y)
                    + (tex2D(tex, uvZ).rgb * weights.z);
            }

            // Triplanar detail albedo with the height/slope weights. NO colour rules:
            // the Earth path (TerrainTextureAlbedo) adds AO, biome tint and sand on
            // top of it, the airless path uses it as a RELATIVE modulation of the
            // palette + macro albedo. The weights live here once: a duplicated set
            // would drift and the low/mid/high blend would show a seam.
            //
            // Coordinates are camera deltas rotated into body-fixed axes: small
            // numbers without ~6 cm quantisation.
            float3 TerrainDetailSample(float3 positionWS, float raw, float slopeTan, float3 normalObject,
                out float2 uvX, out float2 uvY, out float2 uvZ, out float3 tri)
            {
                float3 relBody = mul((float3x3)_TerrainWorldToBody, positionWS - _PlanetCameraPos);
                uvX = (relBody.zy * _TerrainTextureScale) + _TerrainUVPhase0.xy;
                uvY = (relBody.xz * _TerrainTextureScale) + _TerrainUVPhase0.zw;
                uvZ = (relBody.xy * _TerrainTextureScale) + _TerrainUVPhase1.xy;

                tri = abs(normalObject);
                tri = tri * tri * tri * tri;
                tri /= max(1e-5, tri.x + tri.y + tri.z);

                float altitude = max(raw - _TerrainSeaLevel, 0.0);
                float lowW = 1.0 - smoothstep(_LowMidBlendStart, _LowMidBlendEnd, altitude);
                float highW = smoothstep(_MidHighBlendStart, _MidHighBlendEnd, altitude);
                float midW = max(0.0, 1.0 - lowW - highW);
                float steepW = smoothstep(_SteepBlendStart, _SteepBlendEnd, slopeTan);

                // Each Triplanar is 3 texture samples, and there are five
                // (low/mid/high/steep/occlusion) = 15 tex2D per land pixel.
                // Height weights sum to 1, so on flat ground lowW = 1 and
                // midW = highW = 0; three quarters of the samples were multiplied
                // by zero. Branches are coherent (height and slope vary slowly
                // per 8x8 quad), so there is no divergence. A typical plain uses
                // 6 samples instead of 15.
                float3 c = float3(0.0, 0.0, 0.0);
                if (lowW > 0.002)
                {
                    c += Triplanar(_TexLow, uvX, uvY, uvZ, tri) * lowW;
                }
                if (midW > 0.002)
                {
                    c += Triplanar(_TexMid, uvX, uvY, uvZ, tri) * midW;
                }
                if (highW > 0.002)
                {
                    c += Triplanar(_TexHigh, uvX, uvY, uvZ, tri) * highW;
                }
                if (steepW > 0.002)
                {
                    c = lerp(c, Triplanar(_TexSteep, uvX, uvY, uvZ, tri), steepW);
                }

                return c;
            }

            // EARTH PATH: the photo textures replace the procedural colour, with a
            // soft AO and a weak biome tint so the climate zones stay readable.
            // Used only when _TerrainDetailMean == 0.
            float3 TerrainTextureAlbedo(float3 positionWS, float raw, float slopeTan, float3 normalObject, float3 biome)
            {
                float2 uvX, uvY, uvZ;
                float3 tri;
                float3 c = TerrainDetailSample(positionWS, raw, slopeTan, normalObject, uvX, uvY, uvZ, tri);

                float altitude = max(raw - _TerrainSeaLevel, 0.0);

                // AO texture: full triplanar up close, a single projection
                // farther away. At hundreds of metres the AO texel is much
                // smaller than a pixel, so all three projections hit the same
                // distant mip and give almost identical values.
                float occl = distance(positionWS, GetCameraPositionWS()) < 220.0
                    ? dot(Triplanar(_TexOcclusion, uvX, uvY, uvZ, tri), float3(0.3333, 0.3333, 0.3333))
                    : dot(Triplanar(_TexOcclusion, uvX, uvY, uvZ, float3(0.0, 0.0, 1.0)), float3(0.0, 0.0, 1.0));
                c *= lerp(1.0, saturate(occl * 1.3), 0.65);

                c *= lerp(float3(1.0, 1.0, 1.0), saturate(biome * 1.9), 0.65);

                // Beach over the textures: the low-altitude texture is green
                // photo, so without this the sand band would be erased. Same
                // weight as the procedural paint so the edges match.
                c = lerp(c, _ColSand.rgb, SandWeight(altitude));

                return c;
            }

            // Inverse of LunarMacroMapBaker.EncodeHorizon: 8 bits, nonlinear so the
            // precision is ~0.0014° near the horizon (where the terminator lives) and
            // 0.7° near ±90°. Result in RADIANS.
            float DecodeHorizonAngle(float encoded)
            {
                float u = (encoded * 2.0) - 1.0;
                return sign(u) * u * u * 90.0 * 0.0174532925199433;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // ---- Albedo inputs, read BEFORE lighting: the macro map also supplies
                // the surface normal (crater rims seen from orbit are shading, not
                // silhouette).
                //
                // Both directions are normalised by hand, not with normalize(): a
                // degenerate interpolated value (a zero normal on a coarse quad, a zero
                // dirOS at a cube corner) produces NaN, and ONE NaN pixel poisons HDRP's
                // auto-exposure — the whole frame collapses to a flat teal until the
                // offender leaves the view. Seen in Play after a teleport to the Moon.
                float3 dirRaw = input.dirOS;
                float dirLen2 = dot(dirRaw, dirRaw);
                float3 dir = dirLen2 > 1e-12 ? dirRaw * rsqrt(dirLen2) : float3(0.0, 0.0, 1.0);

                float3 normalRaw = input.normalWS;
                float normalLen2 = dot(normalRaw, normalRaw);
                float3 normal = normalLen2 > 1e-12
                    ? normalRaw * rsqrt(normalLen2)
                    // dirOS is the ASTRO body direction (Z = pole); the chunk mesh is in
                    // SIM axes (Y = up), so the axis bridge is not optional here.
                    : TransformObjectToWorldDir(float3(dir.x, dir.z, -dir.y));

                float3 sunDir = normalize(_TerrainSunDir);
                float3 moonDir = GalilegoMoonDir(sunDir);

                float raw = input.extra.x;
                float slopeCos = clamp(input.extra.y, -1.0, 1.0);
                float slopeTan = SlopeTanFromCos(slopeCos);
                float lat01 = saturate(abs(asin(clamp(dir.z, -1.0, 1.0))) / 1.5707963267948966);

                // Pixel footprint on the unit body direction, i.e. the pixel's angular
                // size. A texel of the baked map spans _TerrainMacroTexelAngular:
                //   pixel < texel  -> the map is the blurry thing, fade it OUT;
                //   pixel > texel  -> the chunk quad and its interpolated vertex mask
                //                     are the blurry thing, the map takes over.
                // That one comparison replaces every distance/altitude threshold: it is
                // the true LOD criterion, in radians.
                float angular = max(max(fwidth(dir.x), fwidth(dir.y)), fwidth(dir.z));
                float macroW = 0.0;
                float2 macroUv = float2(0.5, 0.5);
                if (_TerrainMacroOn > 0.5 || _TerrainHorizonOn > 0.0)
                {
                    // The horizon is sampled at every distance (a rim 10 km away still
                    // shadows this pixel), so the UV is computed even when the albedo /
                    // normal part of the macro map is faded out.
                    macroUv = float2((atan2(dir.y, dir.x) * 0.15915494) + 0.5,
                                     (asin(clamp(dir.z, -1.0, 1.0)) * 0.31830989) + 0.5);
                }

                if (_TerrainMacroOn > 0.5)
                {
                    float texel = max(_TerrainMacroTexelAngular, 1e-6);
                    // Full weight as soon as a pixel covers a whole texel. Half weight there
                    // would leave the MESH normal in the lighting at orbital distances — and
                    // the mesh normal is computed per chunk by one-sided differences, so it
                    // jumps across a cube-face boundary: a 1 px line straight down the body.
                    // The map is continuous over the same seam, so trusting it fully is both
                    // sharper and seamless.
                    macroW = saturate(angular / texel) * _TerrainMacroAlbedoStrength;
                }

                if (macroW > 0.0 && _TerrainMacroNormalStrength > 0.0)
                {
                    float3 mapN = (tex2D(_TerrainMacroNormalMap, macroUv).xyz * 2.0) - 1.0;
                    float len2 = dot(mapN, mapN);
                    if (len2 > 1e-4)
                    {
                        // The map and dirOS share the CHUNK's object frame (that is the
                        // frame the bake was taken in), so the body -> world rotation for
                        // the normal is the mesh transform itself. Using the
                        // _TerrainWorldToBody global here was wrong: that one answers for
                        // positionWS (camera-relative sim axes), not for object-space
                        // directions, and its mismatch turned every reconstructed normal
                        // away from the sun (the body rendered black).
                        //
                        // The bake's frame is ASTRO body axes (Z = pole, see the macroUv
                        // above), while the chunk mesh lives in SIM axes (Y = up, the
                        // builder writes (x, z, -y)). Transforming the astro triple with
                        // the mesh matrix turned every normal ~90 degrees away from the
                        // sun: the sunlit side of a moon went black at the altitude where
                        // the map takes over (700 km) and stayed lit near the ground,
                        // where the mesh normal still dominated.
                        float3 nWorld = TransformObjectToWorldDir(
                            float3(mapN.x, mapN.z, -mapN.y) * rsqrt(len2));
                        float tilt2 = dot(nWorld, nWorld);
                        // A degenerate direction must never reach normalize(): one NaN
                        // pixel poisons HDRP's auto-exposure and the whole frame.
                        if (tilt2 > 1e-8)
                        {
                            normal = normalize(lerp(normal, nWorld * rsqrt(tilt2),
                                saturate(macroW * _TerrainMacroNormalStrength)));
                        }
                    }
                }

                float ndl = saturate(dot(normal, sunDir));
                float moonNdl = saturate(dot(normal, moonDir));

                // Long-range sun occlusion from the BAKED HORIZON MAP. HDRP's cascades
                // end at ShadowDistance (2 km airless / 250 m elsewhere); a crater rim
                // 5–50 km away still blocks a sun that is only a degree above the local
                // horizon. The map stores, per body-fixed texel and 45° azimuth bin, the
                // highest terrain elevation seen along that azimuth, so this test makes
                // the terminator follow the real terrain: crater floors go black, high
                // rims keep catching the sun, and no cascade can be "out of range".
                float horizonVis = 1.0;
                if (_TerrainHorizonOn > 0.0)
                {
                    float3 sb = _TerrainSunDirBody;
                    float sbLen2 = dot(sb, sb);
                    if (sbLen2 > 1e-12)
                    {
                        sb *= rsqrt(sbLen2);

                        // Local east/north in the bake's body frame: east = cross(Z, dir),
                        // north = cross(dir, east); azimuth 0 = north, +90° = east.
                        float2 eq = dir.xy;
                        float eq2 = dot(eq, eq);
                        float3 east = eq2 > 1e-8
                            ? float3(-dir.y, dir.x, 0.0) * rsqrt(eq2)
                            : float3(1.0, 0.0, 0.0);
                        float3 north = cross(dir, east);
                        float sunElevation = asin(clamp(dot(sb, dir), -1.0, 1.0));
                        float azimuth = atan2(dot(sb, east), dot(sb, north));
                        azimuth = azimuth < 0.0 ? azimuth + 6.283185307179586 : azimuth;

                        float binPos = azimuth * 1.2732395447351628; // 8 / (2*pi)
                        float binFloor = floor(binPos);
                        int bin0 = (int)binFloor & 7;
                        float binT = binPos - binFloor;

                        float4 hA = tex2D(_TerrainMacroHorizonMapA, macroUv);
                        float4 hB = tex2D(_TerrainMacroHorizonMapB, macroUv);
                        float h[8] = { hA.x, hA.y, hA.z, hA.w, hB.x, hB.y, hB.z, hB.w };
                        float horizon = lerp(DecodeHorizonAngle(h[bin0]),
                            DecodeHorizonAngle(h[(bin0 + 1) & 7]), binT);

                        // The sun is a disc, not a point: the shadow edge is as wide as
                        // its angular radius (0.7° at 0.37 AU), like HDRP's PCSS penumbra.
                        float sunRadius = max(_SunAngularRadiusRad, 0.0005);
                        horizonVis = smoothstep(horizon - sunRadius, horizon + sunRadius,
                            sunElevation);
                        horizonVis = lerp(1.0, horizonVis, saturate(_TerrainHorizonOn));
                    }
                }

                // NOTE: a far-side clip was tried here (a per-fragment test that would drop
                // the FAR half of a chunk straddling the limb, which `Cull Off` paints as a
                // dark shell at altitude). Both formulations cut a razor-straight line across
                // the GROUND: `positionWS` in this pass is not in the space that
                // _TerrainBodyCenterWS lives in, so any absolute test taken from them is
                // offset by the camera/floating-origin delta and removes near-side terrain.
                // Kept out until it can be done in one explicit space (or by making the
                // skirts double-sided and switching to Cull Back).

                // HDRP shadow (PCSS/PCF) from trees/rocks/terrain; attenuates the
                // direct term — ambient remains, shadowed areas are not black.
                // HDRP has ONE cascaded directional shadow: the sun owns it by day,
                // the moon by night, so the sample follows the current owner.
                float3 shadowDir = GalilegoShadowLightDir(sunDir);
                float shadow = GalilegoSunShadow(input.positionCS.xy, input.positionWS, normal, shadowDir);
                float cloudShadow = SampleCloudShadowDirection(input.positionWS, shadowDir);

                // Skirt walls duplicate the chunk edge one node BELOW the surface, so the
                // shadow pass sees them inside the terrain and paints them black. At a LOD
                // junction the two edges cross and a pixel-wide sliver of that wall shows:
                // the "cracked moon" hairline. Unshadowed, the wall is exactly the colour
                // of the ground it hides behind, so the junction disappears.
                // Only the skirt vertices carry the flag (Extra2.y = 1); a skirt triangle
                // has one core vertex and two skirt ones, hence the step, not a lerp.
                float skirt = step(0.01, input.extra2.y);
                if (skirt > 0.0)
                {
                    shadow = lerp(shadow, 1.0, skirt);
                    cloudShadow = lerp(cloudShadow, 1.0, skirt);
                }

                // Single per-fragment light term: night ambient (stars) + sky
                // ambient (mean sky brightness: blue by day, warm at sunset —
                // hemispherical gradient, NOT dependent on ndl) + the sun by
                // the surface normal, with photosphere × atmosphere T colour.
                // At night only _NightAmbient remains (≈0) → almost black. The
                // sun term can exceed 1; HDRP global tonemapping handles it.
                float sun = _TerrainSun;
                float3 radialUp = normalize(input.positionWS - _TerrainBodyCenterWS);
                float dayLocal = smoothstep(-0.12, 0.08, dot(radialUp, sunDir));
                float3 lightTerm = float3(_NightAmbient, _NightAmbient, _NightAmbient)
                    + (GalilegoSkyAmbient(normal) * _TerrainRadianceScale * dayLocal)
                    + (GalilegoMoonAmbient(normal) * _TerrainRadianceScale)
                     + (_SunLightColor * (sun * ndl * shadow * cloudShadow * horizonVis) * _TerrainRadianceScale)
                     + (_MoonLightColor * (sun * moonNdl * shadow * cloudShadow) * _TerrainRadianceScale);

                // --- Per-pixel albedo -----------------------------------------
                // Mask and detail are interpolated vertex values computed by the same
                // CPU noise the decor reads (paint/placement parity). The shader no
                // longer has its own Fbm: its float-safe offsets produced a different
                // pattern, so visual forest would be steppe for the decor.
                //
                // The macro map carries the SAME field at a resolution the mesh cannot
                // (2048x1024 texels over the whole body). Where it is trusted, the
                // interpolated copy is faded out instead of being multiplied in: a
                // blurred mare times a sharp mare would read twice as dark.
                float vertexDetail = saturate(1.0 - macroW);
                float mask = input.extra.z * vertexDetail;
                float detail = input.extra.w;
                if (_ColorDetailStrength != 0.0 && _ColorDetailFrequency > 0.0)
                {
                    float footprint = angular * _ColorDetailFrequency;
                    detail *= saturate(1.0 - (footprint * 0.7));
                }

                detail *= vertexDetail;

                float3 albedo = TerrainAlbedo(raw, mask, detail, slopeTan, lat01);

                // Terrain textures instead of procedural albedo —
                // land only: the sea stays procedural/water.
                float isWater = raw <= _TerrainSeaLevel + SeaEpsilon(_TerrainAmplitude) ? 1.0 : 0.0;
                bool detailOn = _TerrainUseTextures > 0.5 && isWater < 0.5;
                float detailMean = _TerrainDetailMean;
                if (detailMean > 0.0)
                {
                    // AIRLESS REGOLITH (mean > 0): the detail set is a RELATIVE
                    // modulation — dividing by its own mean makes the average 1, so
                    // the palette and the macro albedo keep the absolute brightness
                    // (maria ~0.07, highlands ~0.21 linear). The ground then matches
                    // the far disc, and the disc→mesh hand-over has no step. A photo
                    // set here would REPLACE that field with Earth's grass and darken
                    // the moon ~4x, which is exactly what the Moon used to do.
                    if (detailOn)
                    {
                        float2 duvX, duvY, duvZ;
                        float3 dtri;
                        float3 detailSample = TerrainDetailSample(input.positionWS, raw, slopeTan,
                            normalize(TransformWorldToObjectNormal(normal)), duvX, duvY, duvZ, dtri);
                        albedo *= detailSample * (1.0 / max(detailMean, 0.02));
                    }
                }
                else if (detailOn)
                {
                    float3 normalObject = normalize(TransformWorldToObjectNormal(normal));
                    albedo = TerrainTextureAlbedo(input.positionWS, raw, slopeTan, normalObject, albedo);
                }

                // Macro albedo AFTER the textures: the body-fixed maria/highland field
                // must survive on the ground, where the detail set is strongest. The
                // legacy path keeps the pixel-vs-texel weight; the airless regolith
                // applies the field at full strength at EVERY distance — a distance
                // dependent weight would itself be a visible step while descending.
                // The macro NORMAL above still follows macroW: relief read from a
                // kilometre-sized texel is wrong underfoot, the smooth albedo field is not.
                float macroAlbedoW = detailMean > 0.0 ? saturate(_TerrainMacroAlbedoStrength) : macroW;
                if (macroAlbedoW > 0.0 && _TerrainMacroOn > 0.5)
                {
                    // Stored as factor / 2 (0..1 in 8 bit), the same encoding the far
                    // disc map uses, so one bake feeds both views.
                    float macroAlbedo = tex2D(_TerrainMacroAlbedoMap, macroUv).r * 2.0;
                    albedo *= lerp(1.0, macroAlbedo, macroAlbedoW);
                }

                // Polar ice: LAST, over textures, rock and mottling. The per-vertex
                // weight is the exact CPU value that built the ice geometry, so the
                // white edge matches the edge of the ice sheet (no shader-side noise).
                float polarIce = saturate(input.extra2.x * 2.0);
                albedo = lerp(albedo, _ColSnow.rgb, PaletteSmoothstep(polarIce));

                // Land.
                float3 land = albedo * lightTerm;

                // Ocean floor (raw height below sea): chunk geometry is clamped
                // to the real bottom, and water is a separate nearly opaque chunk
                // mesh (Galilego/WaterSurface: waves, specular, foam). Absorption
                // is in METRES of depth; the floor fades quickly to darkness, so
                // detailed seafloor relief is not visible through the water.
                float depthMeters = max(0.0, _TerrainSeaLevel - raw);
                float absorb = sqrt(saturate(depthMeters / 10.0));
                float3 shallowBed = _ColSand.rgb * float3(0.30, 0.38, 0.36);
                float3 seabedBase = lerp(shallowBed, _WaterDeep.rgb, smoothstep(0.0, 0.45, absorb));
                float3 seabed = seabedBase * lightTerm * (1.0 - (0.85 * absorb));

                // Wet edge: a gradient in METRES above sea level. At the
                // waterline it is dark, cool wet sand (red fades first, as
                // underwater sand); higher it blends to the normal colour. The
                // band is limited to the beach zone (beachW), so only ~3 m of
                // shore stays wet, not the whole beach.
                float shoreAlt = max(raw - _TerrainSeaLevel, 0.0);
                float beachW = SandWeight(shoreAlt);
                float wetBand = (1.0 - smoothstep(0.0, max(0.05, _ShoreWetMeters), shoreAlt))
                    * (1.0 - isWater) * beachW;
                float3 landWet = land * lerp(float3(1.0, 1.0, 1.0), _ShoreWetTint.rgb, wetBand);

                float3 color = lerp(landWet, seabed, isWater);
                color += _UnderwaterCausticColor.rgb * UnderwaterCausticMask(input.positionWS, normal);

                // Last line of defence for HDRP's auto-exposure: one NaN/Inf pixel in the
                // frame makes the exposure unbounded and the whole image collapses to a
                // flat teal until the offender leaves the view.
                if (any(isnan(color)) || any(isinf(color)))
                {
                    color = 0.0;
                }

                return float4(min(color, 65504.0), 1.0);
            }
            ENDHLSL
        }

        // Depth for the HDRP depth-prepass. Without this pass the terrain is
        // missing from the depth pyramid read by transparent shaders
        // (atmosphere, stars, sun disc): their IsSky() treats mountain pixels
        // as sky. HDRP requires DepthForwardOnly on forward materials.
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
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // The terrain MUST cast into HDRP's shadow map. Without this pass a crater
        // rim has no shadow at all: near the terminator the sun is a degree above
        // the horizon, so a 50 m rim shadows kilometres of ground — yet the shader
        // only had ndl, and every sun-facing inner wall glowed out of a black
        // landscape. The receiver already samples this map (GalilegoSunShadow);
        // this pass is what puts the terrain into it.
        //
        // Cull Off matches the forward pass: the chunks are open shells with skirts,
        // and a shadow ray must not slip through a chunk seam because the far side
        // was culled.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

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
                output.positionCS = TransformObjectToHClip(input.positionOS);
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
