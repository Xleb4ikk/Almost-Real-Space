#ifndef GALILEGO_GASGIANT_COMMON_INCLUDED
#define GALILEGO_GASGIANT_COMMON_INCLUDED

// Shared surface model for Galilego/GasGiantSurface and Galilego/GasGiantDisc.
//
// The technique is the one used for real Jupiter flyby imagery: a 1D latitude
// LUT for the zonal colour bands, a 1D wind LUT for the differential rotation
// and the belt/zone boundary mask, then three superimposed scales of 3D noise
// (domain warp -> cloud cells -> east-west fibrils) evaluated on the unit
// sphere. 3D noise has no UV seam and no polar pinch, so the same function is
// valid on the cube-sphere and on the far billboard.
//
// The palette, band layout and storms belong to the fictional Aeolus, but they
// follow Jupiter's contrast: nearly white ammonia zones, deep red-brown belts,
// and a large salmon-red oval in the South Tropical Zone.

#define GG_PI 3.14159265358979323846
#define GG_INV_PI 0.31830988618379067154
#define GG_INV_TWO_PI 0.15915494309189533577
#define GG_MAX_VORTICES 8

TEXTURE2D(_GG_BandLut);
SAMPLER(sampler_GG_BandLut);
TEXTURE2D(_GG_WindLut);
SAMPLER(sampler_GG_WindLut);
TEXTURE2D(_GG_SurfaceMap);
SAMPLER(sampler_GG_SurfaceMap);
float4 _GG_SurfaceMap_TexelSize;
float _GG_SurfaceMapStrength;
float _GG_MapLongitudeOffset;
float _GG_MapBump;

float3 _GG_SunDirWS;
float4 _GG_SunColor;
float _GG_Eclipse;
float3 _GG_CameraPosWS;
float3 _GG_BodyCenterWS;
float _GG_PlanetRadius;
float _GG_Oblateness;

float _GG_ShearExaggeration;
float _GG_WindTimeA;
float _GG_WindTimeB;
float _GG_VortexTimeA;
float _GG_VortexTimeB;
float _GG_LayerBlend;
float _GG_PatternSpin;

float _GG_WarpAmplitude;
float _GG_WarpScale;
float _GG_TurbulenceScale;
float _GG_TurbulenceAnisotropy;
float _GG_HazeDensity;
float _GG_LimbDarkening;
float _GG_WrapDiffuse;
float _GG_RimIntensity;
float _GG_Saturation;
float _GG_VortexSpinPeriod;
float _GG_IntensityScale;
float _GG_DebugMode;

// New in the Jupiter pass: cloud-top relief, band contrast, pole tint and the
// strength of the fine filament layer.
float _GG_CloudBump;
float _GG_Contrast;
float _GG_PolarTint;
float _GG_DetailStrength;

float4 _GG_NorthPoleColor;
float4 _GG_SouthPoleColor;
float4 _GG_HazeColor;
float4 _GG_RimColor;

float4 _GG_VortexParams0[GG_MAX_VORTICES]; // latC, lon, radiusLat, radiusLon (radians)
float4 _GG_VortexParams1[GG_MAX_VORTICES]; // spin, intensity, drift deg/day, swirl
float4 _GG_VortexCore[GG_MAX_VORTICES];
float4 _GG_VortexCollar[GG_MAX_VORTICES];
int _GG_VortexCount;

float3 GGRotateZ(float3 p, float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float3((c * p.x) - (s * p.y), (s * p.x) + (c * p.y), p.z);
}

float4 SampleBandLut(float latC)
{
    float lat01 = saturate((latC * GG_INV_PI) + 0.5);
    return SAMPLE_TEXTURE2D(_GG_BandLut, sampler_GG_BandLut, float2(lat01, 0.5));
}

float4 SampleWindLut(float latC)
{
    float lat01 = saturate((latC * GG_INV_PI) + 0.5);
    return SAMPLE_TEXTURE2D(_GG_WindLut, sampler_GG_WindLut, float2(lat01, 0.5));
}

// Equirectangular mapping of the baked Jupiter map. The equator is at v=0.5,
// the north pole at v=1. The longitude offset lets the game place the Great
// Red Spot at the desired prime meridian.
float2 GGSurfaceUv(float3 p)
{
    float lon = atan2(p.y, p.x);
    float lat = asin(clamp(p.z, -1.0, 1.0));
    float u = frac((lon * GG_INV_TWO_PI) + 0.5 + _GG_MapLongitudeOffset);
    float v = (lat * GG_INV_PI) + 0.5;
    return float2(u, v);
}

// Analytic derivatives of the equirectangular mapping. The hardware
// differentiates frac(atan2(y,x)/2pi + const), which jumps by a whole map
// width across the seam; that spike forces the smallest mip and paints a
// blurred meridian on the globe. Here the atan2 branch cut cancels in the
// quotient, so the derivatives stay finite and mip selection is correct.
float2 GGSurfaceUvDerivative(float3 p, float3 dp)
{
    float r2 = max((p.x * p.x) + (p.y * p.y), 1e-12);
    float du = ((p.x * dp.y) - (p.y * dp.x)) / (r2 * (2.0 * GG_PI));
    float sinLat = sqrt(max(1.0 - (p.z * p.z), 1e-12));
    float dv = dp.z / (sinLat * GG_PI);
    return float2(du, dv);
}

float3 GGSampleSurfaceMap(float3 p)
{
    float2 uv = GGSurfaceUv(p);
    float2 ddxUv = GGSurfaceUvDerivative(p, ddx(p));
    float2 ddyUv = GGSurfaceUvDerivative(p, ddy(p));
    return SAMPLE_TEXTURE2D_GRAD(_GG_SurfaceMap, sampler_GG_SurfaceMap, uv, ddxUv, ddyUv).rgb;
}

float GGHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

// Quintic interpolation removes the visible lattice of the old smoothstep
// noise and keeps the fibrils crisp at grazing angles.
float GGNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * f * ((f * (f * 6.0 - 15.0)) + 10.0);

    float n000 = GGHash31(i + float3(0.0, 0.0, 0.0));
    float n100 = GGHash31(i + float3(1.0, 0.0, 0.0));
    float n010 = GGHash31(i + float3(0.0, 1.0, 0.0));
    float n110 = GGHash31(i + float3(1.0, 1.0, 0.0));
    float n001 = GGHash31(i + float3(0.0, 0.0, 1.0));
    float n101 = GGHash31(i + float3(1.0, 0.0, 1.0));
    float n011 = GGHash31(i + float3(0.0, 1.0, 1.0));
    float n111 = GGHash31(i + float3(1.0, 1.0, 1.0));

    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    float nxy0 = lerp(nx00, nx10, u.y);
    float nxy1 = lerp(nx01, nx11, u.y);
    return lerp(nxy0, nxy1, u.z);
}

float GGFbm3(float3 p)
{
    float sum = (GGNoise3(p) * 0.5) + (GGNoise3((p * 2.03) + float3(17.1, 9.2, 3.7)) * 0.26)
        + (GGNoise3((p * 4.11) + float3(3.4, 29.8, 13.3)) * 0.14)
        + (GGNoise3((p * 8.37) + float3(11.9, 7.7, 31.1)) * 0.07);
    return sum / 0.97;
}

// Ridged noise: the sharp crests read as the thin bright/dark filaments that
// stream along the belt edges on Jupiter.
float GGRidge3(float3 p)
{
    float n0 = 1.0 - abs((GGNoise3(p) * 2.0) - 1.0);
    float n1 = 1.0 - abs((GGNoise3((p * 2.07) + float3(5.3, 19.1, 2.7)) * 2.0) - 1.0);
    float n2 = 1.0 - abs((GGNoise3((p * 4.19) + float3(23.7, 3.1, 9.4)) * 2.0) - 1.0);
    return (n0 * 0.55) + (n1 * 0.3) + (n2 * 0.15);
}

struct GGVortexSample
{
    float latWarp;
    float3 color;
    float weight;
};

GGVortexSample SampleVortices(float3 q, float timeSeconds)
{
    GGVortexSample result;
    result.latWarp = 0.0;
    result.color = float3(0.0, 0.0, 0.0);
    result.weight = 0.0;

    [loop]
    for (int i = 0; i < GG_MAX_VORTICES; i++)
    {
        if (i >= _GG_VortexCount)
        {
            break;
        }

        float4 p0 = _GG_VortexParams0[i];
        float4 p1 = _GG_VortexParams1[i];
        float lat0 = p0.x;
        float lon0 = p0.y + (p1.z * (timeSeconds / 86400.0) * (GG_PI / 180.0));
        float rLat = max(p0.z, 1e-4);
        float rLon = max(p0.w, 1e-4);

        float3 center = float3(cos(lat0) * cos(lon0), cos(lat0) * sin(lon0), sin(lat0));
        float3 east = float3(-sin(lon0), cos(lon0), 0.0);
        float3 north = normalize(cross(east, center));
        float3 local = q - center;
        float dx = dot(local, east) / rLon;
        float dy = dot(local, north) / rLat;
        float r2 = (dx * dx) + (dy * dy);
        if (r2 >= 1.0)
        {
            continue;
        }

        float falloff = 1.0 - smoothstep(0.12, 1.0, r2);
        float coreMask = 1.0 - smoothstep(0.04, 0.40, r2);
        float collarMask = smoothstep(0.28, 0.64, r2) * (1.0 - smoothstep(0.80, 1.0, r2));
        float spin = (p1.x * (timeSeconds / max(1.0, _GG_VortexSpinPeriod)) * 2.0 * GG_PI);
        float angle = atan2(dy, dx) + spin;
        float spiral = 0.86 + (0.14 * (0.5 + (0.5 * sin((angle * 3.0) + (r2 * 12.0)))));
        float streamers = 0.92 + (0.08 * GGFbm3(float3(dx, dy, r2) * 6.0));

        float4 core = _GG_VortexCore[i];
        float4 collarColor = _GG_VortexCollar[i];
        float intensity = saturate(p1.y);
        float3 vortexColor = lerp(collarColor.rgb, core.rgb * spiral * streamers, coreMask);
        vortexColor *= lerp(0.55, 1.0, collarMask);
        float vortexWeight = saturate(intensity * saturate(falloff + (collarMask * 0.45)));

        result.color = lerp(result.color, vortexColor, vortexWeight);
        result.weight = saturate(result.weight + vortexWeight);

        // A strong tangential warp bends the neighbouring bands around the
        // storm: this is what makes a Great Red Spot read as a vortex instead
        // of a decal.
        float swirl = p1.w * (1.0 - saturate(r2));
        float azimuth = atan2(dy, dx);
        result.latWarp += (swirl * 0.34) * (dy * (1.0 - saturate(r2)))
            + (swirl * 0.10) * sin(azimuth + (r2 * 7.0));
    }

    return result;
}

float3 SampleCloudLayer(float3 p, float windTime, float vortexTime, out float3 windCoord)
{
    float lat = asin(clamp(p.z, -1.0, 1.0));
    float cosLat = max(cos(lat), 0.16);
    float4 weather = SampleWindLut(lat);

    float theta = weather.r * windTime * _GG_ShearExaggeration / (_GG_PlanetRadius * cosLat);
    float3 q = GGRotateZ(p, theta);
    windCoord = q;

    float latQ = asin(clamp(q.z, -1.0, 1.0));

    // Large-scale domain warp: a gentle, smooth undulation of the whole belt
    // system. Keep it low-frequency and small in amplitude: Jupiter's belts
    // are laminar, not a marble pattern. A second octave adds a little
    // irregularity without breaking the latitudinal structure.
    float3 warpCoord = q * _GG_WarpScale;
    float warp1 = GGFbm3(warpCoord + float3(13.7, 7.1, 3.9));
    float warp2 = GGFbm3((warpCoord * 2.3) + float3(3.1, 19.4, 8.6));
    float warp = (((warp1 - 0.5) * 1.8) + ((warp2 - 0.5) * 0.55))
        * _GG_WarpAmplitude * lerp(0.6, 1.15, weather.g);

    GGVortexSample vortices = SampleVortices(q, vortexTime);
    float latW = latQ + warp + vortices.latWarp;
    float4 band = SampleBandLut(latW);
    float4 windAtLat = SampleWindLut(latW);

    // Cloud cells: broad mottling contained by the belts and zones. These are
    // deliberately weak; the belt structure must remain readable.
    float cells = GGFbm3((q * 9.0) + float3(41.3, 5.9, 22.1));
    float cells2 = GGFbm3((q * 23.0) + float3(7.7, 31.4, 3.3));

    // East-west fibrils. The z scale is multiplied by the anisotropy so the
    // noise is stretched along longitude, like Jovian cloud streets.
    float3 fiberCoord = q * float3(_GG_TurbulenceScale, _GG_TurbulenceScale,
        _GG_TurbulenceScale * _GG_TurbulenceAnisotropy);
    float fiber = GGFbm3(fiberCoord + float3(51.1, 4.3, 18.7));
    float ridge = GGRidge3((fiberCoord * 0.62) + float3(2.9, 12.1, 27.3));

    float edge = windAtLat.b;
    float zone = band.a;
    float detail = ((cells - 0.5) * 0.18) + ((cells2 - 0.5) * 0.08);
    float fine = (fiber - 0.5) * (0.07 + (0.22 * edge));
    float filaments = (ridge - 0.5) * (0.04 + (0.13 * edge)) * lerp(0.8, 1.25, zone);

    float3 albedo = band.rgb;
    albedo *= 1.0 + (detail * _GG_DetailStrength);
    albedo += (fine + filaments) * _GG_DetailStrength * lerp(-0.20, 0.24, zone);

    // The baked global map is the primary albedo when present. It carries the
    // real Jovian belt structure, the Great Red Spot, white ovals, festoons and
    // the polar mottling that a 1D LUT cannot reproduce.
    float mapStrength = saturate(_GG_SurfaceMapStrength);
    if (mapStrength > 0.001)
    {
        // q, not p: the baked cloud map follows the same zonal winds as the
        // procedural detail. Adjacent latitude bands therefore slide at their
        // own speeds (the wind LUT), instead of the map being frozen to the
        // body while only the faint procedural haze moves.
        albedo = lerp(albedo, GGSampleSurfaceMap(q), mapStrength);
    }

    // Polar hoods: cooler and mottled, but not a solid cap. Disabled when the
    // map already provides the real polar cloud deck.
    float polar = smoothstep(0.72, 0.99, abs(sin(latW)));
    float3 poleColor = latW > 0.0 ? _GG_NorthPoleColor.rgb : _GG_SouthPoleColor.rgb;
    float polarMask = saturate(polar * _GG_PolarTint * (0.55 + (0.45 * cells))) * (1.0 - mapStrength);
    albedo = lerp(albedo, poleColor * (0.82 + (0.18 * cells)), polarMask);

    // Thin haze above the ammonia deck; stronger in the zones.
    float haze = windAtLat.a * (0.55 + (0.45 * cells)) * (1.0 - (mapStrength * 0.85));
    albedo = lerp(albedo, _GG_HazeColor.rgb, saturate(haze));
    albedo *= 1.0 - (0.06 * haze);

    float lum = dot(albedo, float3(0.2126, 0.7152, 0.0722));
    albedo = lerp(lum.xxx, albedo, _GG_Saturation);
    albedo = saturate((albedo - 0.5) * _GG_Contrast + 0.5);

    // Apply the storms AFTER the haze and contrast. With a surface map the
    // real storms are already in the texture, but the authored hero vortex is
    // still drawn on top: it gives the Great Red Spot a consistent longitude,
    // colour and animated swirl independent of the map's layout.
    albedo = lerp(albedo, vortices.color, saturate(vortices.weight));
    return albedo;
}

// Perturbs the geometric normal with the gradient of the fine cloud field.
// Cheap finite differences in the local east/north frame; the relief is what
// separates the white ovals and the belt filaments from a flat texture.
float3 GGCloudNormal(float3 p, float3 n, float3 west, float3 north)
{
    if (_GG_SurfaceMapStrength > 0.5)
    {
        if (_GG_MapBump <= 0.0001)
        {
            return n;
        }

        float2 uv = GGSurfaceUv(p);
        float2 texel = _GG_SurfaceMap_TexelSize.xy;
        float2 ddxUv = GGSurfaceUvDerivative(p, ddx(p));
        float2 ddyUv = GGSurfaceUvDerivative(p, ddy(p));
        float3 luma = float3(0.2126, 0.7152, 0.0722);
        float h0 = dot(SAMPLE_TEXTURE2D_GRAD(_GG_SurfaceMap, sampler_GG_SurfaceMap, uv, ddxUv, ddyUv).rgb, luma);
        float hE = dot(SAMPLE_TEXTURE2D_GRAD(_GG_SurfaceMap, sampler_GG_SurfaceMap,
            uv + float2(texel.x, 0.0), ddxUv, ddyUv).rgb, luma);
        float hN = dot(SAMPLE_TEXTURE2D_GRAD(_GG_SurfaceMap, sampler_GG_SurfaceMap,
            uv + float2(0.0, texel.y), ddxUv, ddyUv).rgb, luma);
        float3 grad = (west * (h0 - hE)) + (north * (h0 - hN));
        return normalize(n + (grad * (_GG_MapBump * 10.0)));
    }

    if (_GG_CloudBump <= 0.0001)
    {
        return n;
    }

    float3 coord = p * float3(_GG_TurbulenceScale * 1.6, _GG_TurbulenceScale * 1.6,
        _GG_TurbulenceScale * _GG_TurbulenceAnisotropy * 1.6);
    float3 cart = float3(12.7, 4.1, 29.3);
    float h0 = GGFbm3(coord + cart);
    float hW = GGFbm3((coord + (west * 0.045)) + cart);
    float hN = GGFbm3((coord + (north * 0.045)) + cart);
    float3 grad = (west * (h0 - hW)) + (north * (h0 - hN));
    return normalize(n + (grad * (_GG_CloudBump * 16.0)));
}

float3 ShadeGasGiant(float3 pBody, float3 normalWS, float3 viewWS)
{
    pBody = GGRotateZ(pBody, _GG_PatternSpin);

    float3 windCoordA;
    float3 windCoordB;
    float3 colorA = SampleCloudLayer(pBody, _GG_WindTimeA, _GG_VortexTimeA, windCoordA);
    float3 colorB = SampleCloudLayer(pBody, _GG_WindTimeB, _GG_VortexTimeB, windCoordB);

    // The second sample is a slightly faster cloud deck. Blending it in at
    // LayerBlend adds parallax between the upper/lower deck without smearing
    // the primary map the way a 50/50 average used to.
    float3 albedo = lerp(colorA, colorB, saturate(_GG_LayerBlend));

    float3 Ng = normalize(normalWS);
    float3 L = normalize(_GG_SunDirWS);
    float3 V = normalize(viewWS);

    // Local tangent frame for the cloud relief, in the advected cloud frame
    // so the bump gradients follow the moving deck.
    float3 west = cross(float3(0.0, 0.0, 1.0), windCoordA);
    if (dot(west, west) < 1e-6)
    {
        west = float3(1.0, 0.0, 0.0);
    }
    west = normalize(west);
    float3 north = normalize(cross(windCoordA, west));

    // The bump samples the same advected coordinate as the albedo, so the
    // relief travels with the cloud deck instead of staying frozen to the body.
    float3 N = GGCloudNormal(windCoordA, Ng, west, north);

    float ndl = dot(N, L);
    float diffuse = saturate((ndl + _GG_WrapDiffuse) / (1.0 + _GG_WrapDiffuse));
    float mu = saturate(dot(Ng, V));
    float limb = lerp(1.0, pow(mu, 0.55), _GG_LimbDarkening);

    if (_GG_DebugMode > 0.5)
    {
        if (_GG_DebugMode < 1.5) return albedo;
        if (_GG_DebugMode < 2.5) return N * 0.5 + 0.5;
        if (_GG_DebugMode < 3.5) return diffuse.xxx;
        if (_GG_DebugMode < 4.5) return limb.xxx;
        if (_GG_DebugMode < 5.5) return _GG_SunColor.rgb;
        if (_GG_DebugMode < 6.5) return _GG_HazeColor.rgb;
        if (_GG_DebugMode < 7.5) return _GG_RimColor.rgb;
        return float3(1.0, 0.0, 1.0);
    }

    float eclipse = saturate(_GG_Eclipse);
    float3 color = albedo * _GG_SunColor.rgb * diffuse * limb * eclipse;
    color += albedo * 0.04 * eclipse;

    // A thin blue-grey scattering rim on the illuminated limb. It fades out
    // on the night side, so it reads as atmosphere, not an outline.
    float fresnel = pow(1.0 - mu, 8.0);
    color += _GG_RimColor.rgb * fresnel * _GG_RimIntensity * saturate(ndl * 2.5) * eclipse;

    // Warm the terminator slightly, as a thin high haze does on Jupiter.
    float terminator = pow(saturate(1.0 - abs(ndl)), 6.0);
    color += _GG_HazeColor.rgb * terminator * 0.02 * eclipse;

    return color * _GG_IntensityScale;
}

#endif
