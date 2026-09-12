#ifndef GALILEGO_LIGHTING_INCLUDED
#define GALILEGO_LIGHTING_INCLUDED

// Световая часть кастомных forward-шейдеров Galilego: направленная тень HDRP
// и ambient. В fragment-пассе обязательно объявить варианты качества теней:
//   #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
//   #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
//   #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
//
// Дистанционный LOD фильтрации: до _ShadowHighDistance — штатный HQ-фильтр
// HDRP (PCSS по ассету); до _ShadowMediumDistance — GATHER (4 taps); дальше —
// один tap. Переходы в band'ах выбираются dither'ом по экранным координатам,
// а не лерпом двух реальных сэмплов: в зоне перехода не платим обе стоимости.

// Ставит SkyEnvironment каждый кадр.
float3 _TerrainSunDir;
float _NightAmbient;
float _SkyAmbient;
float _TerrainSun;
float _ShadowStrength;
float _ShadowHighDistance;
float _ShadowMediumDistance;
float _ShadowBlendWidth;

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"

// Координаты выбранного каскада для точки (как EvalShadow_CascadedDepth_Dither,
// но без выбора следующего каскада): false — точка вне каскадов (свет).
bool GalilegoShadowCascadeCoords(float3 positionWS, float3 normalWS, float3 sunDir,
    out float3 posTC, out float fadeLastCascade)
{
    posTC = float3(0.0, 0.0, 0.0);
    fadeLastCascade = 0.0;

    HDShadowContext shadowContext = InitShadowContext();
    float alpha;
    int cascadeCount;
    // Индекс 0: один directional light с тенями; при появлении других
    // теневых источников вынести индекс в глобал (как _ShadowStrength).
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

    // Alpha последнего каскада — плавный уход тени в свет на границе дистанции.
    if (split == cascadeCount - 1)
    {
        fadeLastCascade = alpha;
    }

    return true;
}

// Средний тир: один GATHER (4 taps) по каскадной карте.
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

// Дешёвый тир: один tap (на десктопе аппаратный 2x2 PCF).
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

// Тень солнца: positionSS — SV_Position.xy (в пикселях), positionWS/normalWS —
// мировые позиция и нормаль, sunDir — направление НА солнце.
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
        // HQ <-> MQ: стохастический выбор тира (не платим оба фильтра).
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
        // MQ <-> LQ: то же самое.
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

// Ambient: ночная засветка + небо (верх) / слабое отражение от земли (низ).
// Мягкий градиент по вертикали нормали: бока — как раньше, верх чуть светлее.
float3 GalilegoAmbient(float3 normalWS, float3 albedo)
{
    float hemi = saturate((normalWS.y * 0.5) + 0.5);
    return albedo * (_NightAmbient + (_SkyAmbient * (0.6 + (0.8 * hemi))));
}

#endif
