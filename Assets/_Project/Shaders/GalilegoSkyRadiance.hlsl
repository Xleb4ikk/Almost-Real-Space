// Sky by direction for the WATER shader: the Snell window looks at the sky and
// must show the same sky as above the water.
//
// A separate include (not a function in WaterSurface.shader) because the water needs
// exactly the same globals as the terrain (SkyEnvironment) and clouds (PlanetCloudsView),
// but GalilegoLighting.hlsl cannot be included in water: it pulls in HDShadow, and the
// water ForwardOnly pass deliberately runs without shadow variants.
//
// What is NOT done here and why:
//   - IntegrateAtmosphere from PlanetAtmosphere.shader is not used. An exact match with
//     the sky above water would cost ~48 raymarch steps with two LUT samples per pixel,
//     and the ceiling fills the whole screen when looking up. Instead an analytic
//     approximation on the same physical globals.
//
// Precision at planetary radius: all cloud computations use DIRECTIONS (unit vector)
// and relative radius, not absolute world coordinates. CloudRaySphere normalizes the
// ray origin by the shell radius before solving the quadratic, so at R = 6.4e6 m a
// float32 ULP error (~0.5 m) lands in the coefficients after the division, not in the
// position in metres.

#ifndef GALILEGO_SKY_RADIANCE_INCLUDED
#define GALILEGO_SKY_RADIANCE_INCLUDED

// GalilegoCloudField.hlsl is included for SampleCloudEarthMask and CloudRaySphere.
// It has its own guard, so re-inclusion is safe: WaterSurface.shader already includes it.
#include "GalilegoCloudField.hlsl"

// --- SkyEnvironment globals (same set as in GalilegoLighting.hlsl) ---------
float3 _SkyAmbientColor;   // ambient tint × normalized sky brightness
float3 _SunLightColor;     // photosphere × atmosphere transparency
float3 _TerrainSunDir;     // direction TOWARD the sun
float3 _SkyUp;             // observer's local up
float _SkyAmbient;         // daytime ambient multiplier
float _TerrainSun;         // strength of the scene's sun term
float _EyeGain;            // the observer's pupil (SkyEnvironment; 2^14 at night)
float _TerrainRadianceScale; // ground HDR boost (set by SkyEnvironment)
float _NightAmbient;       // night ambient
float _StarVisibility;     // star visibility 0..1 (0 by day, 1 by night)

// Moonlight (MoonlightSystem), duplicated here because water does not include
// GalilegoLighting.hlsl.
float3 _TerrainMoonDir;    // direction TOWARD the moon
float3 _MoonLightColor;    // reflected light × scene intensity (phase/eclipse/horizon included)
float3 _MoonAmbientColor;  // night-sky ambient scattered from the moon
float _MoonShadowActive;   // 1 = the one HDRP cascade map belongs to the moon

// --- Cloud shell geometry for sampling along the refracted ray ------------
// Declared in GalilegoCloudField.hlsl: _CldBottomRadius, _CldTopRadius,
// _CldCoverage, _CldBottomFeather, _CldTopFeather, _CldWorldToBody.

/// <summary>
/// Solar angular radius in radians: 0.265° — half of the 0.53° apparent diameter.
/// A hard lower bound on the glare width: even on calm water the disc cannot be
/// sharper than its real size.
/// </summary>
static const float SunAngularRadius = 0.00463;

// Multiplier converting sky ambient (_SkyAmbientColor · _SkyAmbient) into sky
// brightness for the window. Ambient is the AVERAGE brightness of a hemisphere, not
// the sky in a specific direction. Calibrated so the window is roughly 7-12x brighter
// than the veil without blowing out.
static const float SkyBaseGain = 20.0;

/// <summary>
/// Hemispheric ambient — the same formula as in GalilegoLighting.hlsl. Duplicated
/// here because water does not include GalilegoLighting.hlsl.
/// </summary>
float3 GalilegoSkyAmbient(float3 normalWS)
{
    float hemi = saturate((dot(normalWS, _SkyUp) * 0.5) + 0.5);
    return _SkyAmbientColor * (_SkyAmbient * (0.35 + 0.65 * hemi));
}

// Same contract as GalilegoLighting.hlsl: fallback avoids normalize(0) when no
// moon exists (other scenes) or it is below the horizon.
float3 GalilegoMoonDir(float3 fallbackDir)
{
    float moonLength = length(_TerrainMoonDir);
    return moonLength > 1e-5 ? (_TerrainMoonDir / moonLength) : fallbackDir;
}

float3 GalilegoMoonAmbient(float3 normalWS)
{
    float hemi = saturate((dot(normalWS, _SkyUp) * 0.5) + 0.5);
    return _MoonAmbientColor * (0.35 + 0.65 * hemi);
}

float3 GalilegoShadowLightDir(float3 sunDir)
{
    return _MoonShadowActive > 0.5 ? GalilegoMoonDir(sunDir) : sunDir;
}

/// <summary>
/// Soft disc per Cox-Munk: exp(−θ²/2σ²), with σ = sqrt(σ_disc² + σ_slope²).
///
/// Why not pow(dot, 900) or a hard smoothstep over the angle: at wave slopes of ±8°
/// the refraction spreads the sun direction across the window by several times the
/// solar disc. A hard cone there turns into sparse flickering pixels, while a Gaussian
/// of the same total energy spreads into stable glints. Same idea as specular
/// anti-aliasing, but the width comes from the per-pixel normal spread.
///
/// sigmaAngle is the per-pixel normal spread in radians. The disc on calm water cannot
/// be narrower than the Sun's own angular radius.
/// </summary>
float3 GalilegoSunDisc(float3 dir, float3 sunDir, float3 radiance, float sigmaAngle)
{
    float cosA = dot(dir, sunDir);
    if (cosA <= 0.0)
    {
        return 0.0;
    }

    // θ ≈ sqrt(2(1−cosθ)) — accurate for small angles (disc and halo are small-angle),
    // but without an acos over the whole screen.
    float theta2 = 2.0 * (1.0 - cosA);
    float sigma = sqrt(max(SunAngularRadius * SunAngularRadius, sigmaAngle * sigmaAngle));
    float lobe = exp(-theta2 / (2.0 * sigma * sigma));
    return radiance * lobe;
}

/// <summary>
/// Halo (aureole) around the Sun: Mie with a wide tail. Also smoothed, otherwise
/// aliasing rings appear at the window edge.
/// </summary>
float3 GalilegoSunAureole(float3 dir, float3 sunDir, float3 radiance, float sigmaAngle)
{
    float cosA = dot(dir, sunDir);
    if (cosA <= 0.0)
    {
        return 0.0;
    }

    float theta = sqrt(max(0.0, 2.0 * (1.0 - cosA)));
    float sigma = 0.075 + sigmaAngle;
    return radiance * exp(-theta / sigma);
}

/// <summary>
/// Moon in the Snell window, the same disc + halo model as the Sun. Radiance is
/// scaled far above _MoonLightColor (that is ground illuminance): the moon's own
/// surface is ~10^5 times brighter than the ground it lights, and the eye is
/// adapted to the ground, so the disc must read as a bright lamp on the water.
/// Since the moon itself is physical (NightAdaptationGainEv = 0), the scene's
/// pupil (_EyeGain) is what lifts it to the ground's exposure — the same eye that
/// reads the ground reads this reflection. Zero when MoonlightSystem has no moon,
/// so the night window rests on the stars.
/// </summary>
float3 GalilegoMoonRadiance(float3 dir, float normalSigmaAngle)
{
    if (dot(_MoonLightColor, _MoonLightColor) < 1e-12)
    {
        return 0.0;
    }

    float3 moonDir = GalilegoMoonDir(_TerrainSunDir);
    float3 disc = GalilegoSunDisc(dir, moonDir, _MoonLightColor * 12.0 * _EyeGain, normalSigmaAngle);
    float3 halo = GalilegoSunAureole(dir, moonDir, _MoonLightColor * 0.35 * _EyeGain, normalSigmaAngle);
    return disc + halo;
}

/// <summary>
/// Direction-cell hash — the same as in StarField.shader (stars must sit where they
/// do in the sky).
/// </summary>
float GalilegoHash31(float3 p)
{
    p = frac((p * float3(0.1031, 0.1030, 0.0973)));
    p += dot(p, p.yxz + 33.33);
    return frac((p.x + p.y) * p.z);
}

/// <summary>
/// One star layer, the same scheme as StarField.shader: per direction cell a star with
/// its own position, brightness and temperature. Needed for the night Snell window —
/// otherwise the sky is uniformly black at night.
/// </summary>
float2 GalilegoStarLayer(float3 dir, float density, float keepThreshold, float size, float seed)
{
    float3 p = dir * density;
    float3 q = floor(p);
    float3 r = frac(p);
    float3 starPos = float3(
        GalilegoHash31(q + seed),
        GalilegoHash31(q + (seed + 11.1)),
        GalilegoHash31(q + (seed + 23.2)));
    float d = length(r - starPos);
    float keep = step(keepThreshold, GalilegoHash31(q + (seed + 7.7)));
    float brightness = keep * smoothstep(size, 0.0, d);
    float temp = GalilegoHash31(q + (seed + 41.0));
    return float2(brightness, temp);
}

/// <summary>
/// Cloud coverage along the ray: one field sample at the ray's entry into the layer.
/// Cheap (~32 hashes), but the ripple moves the entry point, so clouds in the window
/// move together with the waves — that is what a "living" window needs.
///
/// The ray is taken from the camera (origin at zero in render space), not from the
/// surface point: a few metres' difference at 8 km to the layer shifts the entry by
/// less than a centimetre, while the origin stays at zero and the entry is computed
/// via CloudRaySphere, which normalizes the origin by the shell radius — at
/// R = 6.4e6 m the only form that survives float32 (see the file header).
/// </summary>
float CloudShellCoverage(float3 dir, float3 bodyCenterWS, float radiusBottom, float radiusTop)
{
    float3 origin = -bodyCenterWS;
    float tTop0, tTop1, tBottom0, tBottom1;
    if (!CloudRaySphere(origin, dir, radiusTop, tTop0, tTop1))
    {
        return 0.0;
    }

    bool hitBottom = CloudRaySphere(origin, dir, radiusBottom, tBottom0, tBottom1);
    float tEnter = max(tTop0, 0.0);
    float tExit = (hitBottom && tBottom0 > tEnter) ? tBottom0 : tTop1;
    if (tExit <= tEnter)
    {
        return 0.0;
    }

    // Midpoint of the path through the layer, rather than the entry, so the height
    // doesn't land on the very edge of the shell where the feathers haven't grown in.
    float3 bodyPoint = (bodyCenterWS + (dir * (0.5 * (tEnter + tExit))));
    float radius = length(bodyPoint - bodyCenterWS);
    float heightFraction = saturate((radius - radiusBottom) / max(1.0, radiusTop - radiusBottom));
    float bottomFeather = smoothstep(0.0, max(0.001, _CldBottomFeather), heightFraction);
    float topFeather = 1.0 - smoothstep(1.0 - max(0.001, _CldTopFeather), 1.0, heightFraction);
    return smoothstep(0.08, 0.78, SampleCloudEarthMask(bodyPoint))
        * saturate(_CldCoverage * 1.35) * bottomFeather * topFeather;
}

/// <summary>
/// Sky brightness in direction dir for the Snell window.
///
/// Composition: horizon-to-zenith gradient from the physical sky ambient, the Sun
/// halo and disc, the cloud layer along the ray, and stars at night. Sun and Moon
/// are gated by the caller-provided sunFactor: with the sun below the horizon there
/// must be no disc, not merely a disc "faded out" by dot, since at dusk the picture
/// is still bright with the sun 1° above the horizon.
/// </summary>
float3 GalilegoSkyRadiance(float3 dir, float3 sunDir, float normalSigmaAngle, float sunFactor,
    float3 bodyCenterWS, float cloudRadiusBottom, float cloudRadiusTop)
{
    // Gradient: the horizon is warmer and brighter (long Rayleigh scattering path),
    // the zenith cooler. Both tints come from the single sky ambient colour, so sunset
    // and night tint themselves without a separate branch.
    float upness = saturate(dot(dir, _SkyUp));
    float3 horizonTint = _SkyAmbientColor * float3(1.22, 1.06, 0.90);
    float3 zenithTint = _SkyAmbientColor * float3(0.70, 0.90, 1.28);
    float3 sky = lerp(horizonTint, zenithTint, pow(upness, 0.45)) * (_SkyAmbient * SkyBaseGain);

    if (sunFactor > 0.001)
    {
        sky += GalilegoSunAureole(dir, sunDir, _SunLightColor * (0.05 * sunFactor), normalSigmaAngle);
        sky += GalilegoSunDisc(dir, sunDir, _SunLightColor * (22.0 * sunFactor), normalSigmaAngle);
    }

    // Clouds. One field sample along the ray, only if the ray really enters the shell
    // (in the mirror zone the sky isn't visible anyway, so a raymarch there is wasted).
    if (_CldCloudActive > 0.5 && _CldCoverage > 0.001 && upness > 0.02)
    {
        float heightFraction = CloudShellCoverage(dir, bodyCenterWS, cloudRadiusBottom, cloudRadiusTop);
        sky = lerp(sky, sky * 0.82 + (_SkyAmbientColor * (_SkyAmbient * SkyBaseGain * 0.75)), heightFraction);
    }

    if (_StarVisibility > 0.01)
    {
        float3 night = 0.0;
        float2 bright = GalilegoStarLayer(dir, 160.0, 0.93, 0.10, 0.0);
        night += lerp(float3(1.0, 0.78, 0.58), float3(0.74, 0.84, 1.0), bright.y) * bright.x * 4.0;
        float2 faint = GalilegoStarLayer(dir, 420.0, 0.86, 0.08, 3.3);
        night += lerp(float3(1.0, 0.78, 0.58), float3(0.74, 0.84, 1.0), faint.y) * faint.x * 0.9;
        sky += night * (_StarVisibility * 0.35);
        sky += GalilegoMoonRadiance(dir, normalSigmaAngle);
    }

    return max(sky, 0.0);
}


#endif
