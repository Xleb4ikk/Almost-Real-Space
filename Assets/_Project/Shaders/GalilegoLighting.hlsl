#ifndef GALILEGO_LIGHTING_INCLUDED
#define GALILEGO_LIGHTING_INCLUDED

#include "GalilegoCloudField.hlsl"

// Lighting part of the custom Galilego forward shaders: HDRP directional shadow
// and ambient. In a fragment pass the shadow quality variants must be declared:
//   #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
//   #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
//   #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
//
// Distance LOD of filtering: up to _ShadowHighDistance the stock HQ filter HDRP
// (PCSS per asset); up to _ShadowMediumDistance GATHER (4 taps); beyond that a
// single tap. Band transitions are chosen by dithering per screen coordinate
// rather than lerping two real samples, so the transition zone pays only one cost.

// Set by SkyEnvironment every frame.
float3 _TerrainSunDir;
float _NightAmbient;
float _SkyAmbient;
float _TerrainSun;
float _ShadowStrength;
float _ShadowHighDistance;
float _ShadowMediumDistance;
float _ShadowBlendWidth;

// Direct sunlight colour: photosphere × atmosphere transparency (white in space,
// yellow near the ground, red at sunset) — shared by all custom surface shaders.
float3 _SunLightColor;

// Ambient tint (day: blue, sunset: warm) × sky brightness.
float3 _SkyAmbientColor;

// Observer's local up (set by SkyEnvironment): for the hemispheric ambient
// gradient in world space.
float3 _SkyUp;

// Ground HDR boost (analogue of the sky's ATM_RADIANCE_SCALE), set by SkyEnvironment.
// 1 = neutral; tune visually together with TerrainSunIntensity (reference 2-4).
float _TerrainRadianceScale;

// Sky lighting of the surface: tint and magnitude from the CPU sky-brightness
// estimate (_SkyAmbientColor/_SkyAmbient), gradient over the normal hemisphere.
// CRITICAL: independent of the sun direction — at sunset and dusk the sky glows
// even when the direct ray has faded.
float3 GalilegoSkyAmbient(float3 normalWS)
{
    float hemi = saturate((dot(normalWS, _SkyUp) * 0.5) + 0.5);
    return _SkyAmbientColor * (_SkyAmbient * (0.35 + 0.65 * hemi));
}

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"

// Coordinates of the selected cascade for a point (like EvalShadow_CascadedDepth_Dither,
// but without selecting the next cascade): false = point outside the cascades (lit).
bool GalilegoShadowCascadeCoords(float3 positionWS, float3 normalWS, float3 sunDir,
    out float3 posTC, out float fadeLastCascade)
{
    posTC = float3(0.0, 0.0, 0.0);
    fadeLastCascade = 0.0;

    HDShadowContext shadowContext = InitShadowContext();
    float alpha;
    int cascadeCount;
    // Index 0: a single shadowed directional light; if more shadowed sources appear,
    // move the index to a global (like _ShadowStrength).
    int split = EvalShadow_GetSplitIndex(shadowContext, 0, positionWS, alpha, cascadeCount);
    if (split < 0)
    {
        return false;
    }

    HDShadowData sd = shadowContext.shadowDatas[0];
    LoadDirectionalShadowDatas(sd, shadowContext, split);

    float3 biasedWS = positionWS + sd.cacheTranslationDelta.xyz;
#if SHADOW_AUTO_FLIP_NORMAL
    normalWS *= FastSign(dot(normalWS, sunDir));
#endif
    biasedWS += EvalShadow_NormalBiasOrtho(sd.worldTexelSize, sd.normalBias, normalWS);
    posTC = EvalShadow_GetTexcoordsAtlas(sd, _CascadeShadowAtlasSize.zw, biasedWS, false);

    // Alpha of the last cascade — smooth shadow-to-light fade at the distance boundary.
    if (split == cascadeCount - 1)
    {
        fadeLastCascade = alpha;
    }

    return true;
}

// Medium tier: one GATHER (4 taps) over the cascade map.
float GalilegoShadowMedium(float3 positionWS, float3 normalWS, float3 sunDir)
{
    float3 posTC;
    float fade;
    if (!GalilegoShadowCascadeCoords(positionWS, normalWS, sunDir, posTC, fade))
    {
        return 1.0;
    }

    float shadow = SampleShadow_Gather_PCF(
        _CascadeShadowAtlasSize.zwxy, posTC, _ShadowmapCascadeAtlas,
        s_linear_clamp_compare_sampler, FIXED_UNIFORM_BIAS);
    return lerp(shadow, 1.0, fade);
}

// Cheap tier: a single tap (hardware 2x2 PCF on desktop).
float GalilegoShadowCheap(float3 positionWS, float3 normalWS, float3 sunDir)
{
    float3 posTC;
    float fade;
    if (!GalilegoShadowCascadeCoords(positionWS, normalWS, sunDir, posTC, fade))
    {
        return 1.0;
    }

#if SHADOW_USE_DEPTH_BIAS
    posTC.z += FIXED_UNIFORM_BIAS;
#endif
    float shadow = SAMPLE_TEXTURE2D_SHADOW(
        _ShadowmapCascadeAtlas, s_linear_clamp_compare_sampler, posTC).x;
    return lerp(shadow, 1.0, fade);
}

// Sun shadow: positionSS is SV_Position.xy (in pixels), positionWS/normalWS are
// world position and normal, sunDir points TOWARD the sun.
float GalilegoSunShadow(float2 positionSS, float3 positionWS, float3 normalWS, float3 sunDir)
{
    HDShadowContext shadowContext = InitShadowContext();
    float dist = distance(positionWS, GetCameraPositionWS());

    float high0 = _ShadowHighDistance - (_ShadowBlendWidth * 0.5);
    float high1 = _ShadowHighDistance + (_ShadowBlendWidth * 0.5);
    float med0 = _ShadowMediumDistance - (_ShadowBlendWidth * 0.5);
    float med1 = _ShadowMediumDistance + (_ShadowBlendWidth * 0.5);
    float blend = max(1e-4, _ShadowBlendWidth);
    uint taaFrame = (uint)_TaaFrameInfo.z;
    float dither = InterleavedGradientNoise(positionSS, taaFrame);

    float shadow;
    if (dist <= high0)
    {
        shadow = GetDirectionalShadowAttenuation(shadowContext, positionSS, positionWS, normalWS, 0, sunDir);
    }
    else if (dist <= high1)
    {
        // HQ <-> MQ: stochastic tier selection (we don't pay both filters).
        shadow = (dither < saturate((dist - high0) / blend))
            ? GalilegoShadowMedium(positionWS, normalWS, sunDir)
            : GetDirectionalShadowAttenuation(shadowContext, positionSS, positionWS, normalWS, 0, sunDir);
    }
    else if (dist <= med0)
    {
        shadow = GalilegoShadowMedium(positionWS, normalWS, sunDir);
    }
    else if (dist <= med1)
    {
        // MQ <-> LQ: same.
        shadow = (dither < saturate((dist - med0) / blend))
            ? GalilegoShadowCheap(positionWS, normalWS, sunDir)
            : GalilegoShadowMedium(positionWS, normalWS, sunDir);
    }
    else
    {
        shadow = GalilegoShadowCheap(positionWS, normalWS, sunDir);
    }

    return lerp(1.0, shadow, saturate(_ShadowStrength));
}

// [DEPRECATED] Unused by any shader (use GalilegoSkyAmbient above). Kept so
// external calls don't break.
float3 GalilegoAmbient(float3 normalWS, float3 albedo)
{
    float hemi = saturate((normalWS.y * 0.5) + 0.5);
    return albedo * (_NightAmbient + (_SkyAmbient * (0.6 + (0.8 * hemi))));
}

#endif
