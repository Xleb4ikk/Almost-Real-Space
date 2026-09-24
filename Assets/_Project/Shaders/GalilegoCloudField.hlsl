#ifndef GALILEGO_CLOUD_FIELD_INCLUDED
#define GALILEGO_CLOUD_FIELD_INCLUDED

float3 _CldBodyCenterWS;
float3 _CldSunDirWS;
float3 _CldSkyAmbient;
float4x4 _CldWorldToBody;
float _CldPlanetRadius;
float _CldBottomRadius;
float _CldTopRadius;
float _CldCoverage;
float _CldNoiseStyle;
float _CldCloudActive;
float _CldShadowStrength;
float _CldExtinction;
float _CldBottomFeather;
float _CldTopFeather;

float CloudHash(float3 p)
{
    p = frac((p * 0.3183099) + float3(0.13, 0.17, 0.19));
    p *= 17.0;
    return frac((p.x * p.y * p.z) * (p.x + p.y + p.z));
}

float CloudNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - (2.0 * f));
    float n000 = CloudHash(i);
    float n100 = CloudHash(i + float3(1.0, 0.0, 0.0));
    float n010 = CloudHash(i + float3(0.0, 1.0, 0.0));
    float n110 = CloudHash(i + float3(1.0, 1.0, 0.0));
    float n001 = CloudHash(i + float3(0.0, 0.0, 1.0));
    float n101 = CloudHash(i + float3(1.0, 0.0, 1.0));
    float n011 = CloudHash(i + float3(0.0, 1.0, 1.0));
    float n111 = CloudHash(i + float3(1.0, 1.0, 1.0));
    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);
    return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
}

float CloudFbm(float3 p)
{
    float value = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 3; i++)
    {
        value += CloudNoise(p) * amplitude;
        p = (p * 2.03) + float3(1.7, 9.2, 3.1);
        amplitude *= 0.5;
    }
    return value / 0.875;
}

float SampleCloudEarthMask(float3 posRel)
{
    float3 bodyPos = mul(_CldWorldToBody, float4(posRel, 1.0)).xyz;
    float3 direction = bodyPos / max(length(bodyPos), 1e-5);
    float broad = CloudFbm((direction * 7.0) + float3(2.3, 7.1, 4.6));
    float detail = CloudNoise((direction * 18.0) + float3(6.2, 1.4, 8.7));
    float field = saturate(((broad * 0.90) + (detail * 0.10) - 0.28) / 0.72);
    return smoothstep(0.415, 0.795, field);
}

bool CloudRaySphere(float3 ro, float3 rd, float radius, out float t0, out float t1)
{
    float safeRadius = max(abs(radius), 1e-4);
    float3 safeDirection = rd * rsqrt(max(dot(rd, rd), 1e-8));
    float3 normalizedOrigin = ro / safeRadius;
    float b = dot(normalizedOrigin, safeDirection);
    float c = dot(normalizedOrigin, normalizedOrigin) - 1.0;
    float h = (b * b) - c;
    if (h < -1e-7)
    {
        t0 = 0.0;
        t1 = 0.0;
        return false;
    }

    h = sqrt(max(h, 0.0));
    t0 = (-b - h) * safeRadius;
    t1 = (-b + h) * safeRadius;
    return true;
}

float SampleCloudShadow(float3 positionWS)
{
    if (_CldCloudActive < 0.5 || _CldShadowStrength <= 0.0 || _CldNoiseStyle < 0.5)
    {
        return 1.0;
    }

    float3 origin = positionWS - _CldBodyCenterWS;
    float3 direction = normalize(_CldSunDirWS);
    float top0;
    float top1;
    if (!CloudRaySphere(origin, direction, _CldTopRadius, top0, top1))
    {
        return 1.0;
    }

    float bottom0;
    float bottom1;
    bool hitBottom = CloudRaySphere(origin, direction, _CldBottomRadius, bottom0, bottom1);
    float start = max(top0, 0.0);
    float end = (hitBottom && bottom0 > start) ? bottom0 : top1;
    if (end <= start)
    {
        return 1.0;
    }

    float stepLength = (end - start) / 4.0;
    float opticalDepth = 0.0;
    for (int i = 0; i < 4; i++)
    {
        float3 samplePosition = origin + (direction * (start + (stepLength * (i + 0.5))));
        float radius = length(samplePosition);
        float heightFraction = saturate((radius - _CldBottomRadius) / max(1.0, _CldTopRadius - _CldBottomRadius));
        float bottomFeather = smoothstep(0.0, max(0.001, _CldBottomFeather), heightFraction);
        float topFeather = 1.0 - smoothstep(1.0 - max(0.001, _CldTopFeather), 1.0, heightFraction);
        float density = smoothstep(0.08, 0.78, SampleCloudEarthMask(samplePosition))
            * saturate(_CldCoverage * 1.35)
            * bottomFeather
            * topFeather;
        opticalDepth += density * stepLength;
    }

    float attenuation = exp(-opticalDepth * max(_CldExtinction, 0.0) * 0.35);
    attenuation = max(attenuation, 0.28);
    return lerp(1.0, attenuation, saturate(_CldShadowStrength));
}

#endif
