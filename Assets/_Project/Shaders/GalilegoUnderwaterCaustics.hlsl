#ifndef GALILEGO_UNDERWATER_CAUSTICS_INCLUDED
#define GALILEGO_UNDERWATER_CAUSTICS_INCLUDED

#include "GalilegoCloudField.hlsl"

float4x4 _UnderwaterWorldToBody;
float3 _UnderwaterBodyAnchor;
float3 _UnderwaterCameraPos;
float3 _UnderwaterUp;
float3 _UnderwaterSunDir;
float _UnderwaterEyeDepth;
float _UnderwaterTime;
float4 _UnderwaterCausticColor;
float _UnderwaterCausticStrength;
float _UnderwaterCausticScale;
float _UnderwaterCausticFadeStart;
float _UnderwaterCausticFadeEnd;
float _UnderwaterEffectWeight;
float _UnderwaterCausticsEnabled;

float UnderwaterCausticMask(float3 positionWS, float3 normalWS)
{
    if (_UnderwaterCausticsEnabled < 0.5 || _UnderwaterEffectWeight <= 0.0001)
    {
        return 0.0;
    }

    float upLength = length(_UnderwaterUp);
    float normalLength = length(normalWS);
    if (upLength < 1e-5 || normalLength < 1e-5)
    {
        return 0.0;
    }

    float3 relative = positionWS - _UnderwaterCameraPos;
    float3 bodyFixed = mul((float3x3)_UnderwaterWorldToBody, relative) + _UnderwaterBodyAnchor;
    float3 bodyNormal = normalize(mul((float3x3)_UnderwaterWorldToBody, normalWS / normalLength));
    float3 weights = pow(abs(bodyNormal), float3(4.0, 4.0, 4.0));
    weights /= max(1e-5, weights.x + weights.y + weights.z);

    float t = _UnderwaterTime;
    float scale = max(0.001, _UnderwaterCausticScale);
    float2 uvX = (bodyFixed.zy * scale) + float2(t * 0.13, -t * 0.09);
    float2 uvY = (bodyFixed.xz * (scale * 1.37)) - float2(t * 0.10, t * 0.15);
    float2 uvZ = (bodyFixed.xy * (scale * 0.83)) + float2(-t * 0.08, t * 0.11);

    float nX = CloudFbm(float3(uvX.x, uvX.y, 0.0));
    float nY = CloudFbm(float3(uvY.x, uvY.y, 17.0));
    float nZ = CloudFbm(float3(uvZ.x, uvZ.y, 31.0));
    float pattern = (nX * weights.x) + (nY * weights.y) + (nZ * weights.z);
    pattern = pow(saturate(pattern), 4.0);

    float3 up = _UnderwaterUp / upLength;
    float3 nWS = normalWS / normalLength;
    float depth = _UnderwaterEyeDepth - dot(relative, up);
    float waterMask = smoothstep(0.0, 0.2, depth)
        * (1.0 - smoothstep(_UnderwaterCausticFadeStart, _UnderwaterCausticFadeEnd, depth));
    float faceMask = saturate((dot(nWS, up) * 0.75) + 0.25);
    float cloud = SampleCloudShadow(positionWS);
    return pattern * waterMask * faceMask * cloud * _UnderwaterEffectWeight * _UnderwaterCausticStrength;
}

#endif
