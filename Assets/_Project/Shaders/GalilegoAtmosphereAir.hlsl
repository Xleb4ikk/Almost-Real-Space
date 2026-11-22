// One atmosphere march, two consumers today: the sky dome (Galilego/PlanetAtmosphere)
// and the Moon's far disc (Galilego/BodyFarDisc), the object the renderer draws ON TOP
// of that dome but behind the air.
//
// Why this file exists at all:
//   The dome is a screen-space pass at queue Transparent that REPLACES the frame
//   with (background · T + in-scatter). Anything drawn later — the sun disc
//   (Transparent+2), a moon far disc (Transparent+4) — is composited on top of a
//   finished sky, so without this march it is painted as if the observer stood
//   in vacuum. That is invisible at night (the air is dark) and glaring by day:
//   the Moon becomes a white lamp on a blue sky instead of a pale blue disc.
//
// The march MUST be the same code for both, not a copy: a second implementation
// that drifts by a few percent shows up as a visible circle where the disc
// meets the sky. All parameters are globals set once per frame by
// PlanetAtmosphereView; nothing here is material state.
//
// Not yet a consumer: Galilego/GasGiantDisc (also Transparent+N). It has the same
// problem in principle, but from Terra the gas giant is a 2 px dot — see
// Docs/MoonLook.md.

#ifndef GALILEGO_ATMOSPHERE_AIR_INCLUDED
#define GALILEGO_ATMOSPHERE_AIR_INCLUDED

// --- Published by PlanetAtmosphereView (Shader.SetGlobal*, 1 = active) -----
float3 _AtmCameraWS;      // camera in render space
float3 _AtmBodyCenterWS;  // planet centre in render space
float3 _AtmSunDirWS;      // direction TOWARD the star
float3 _AtmSunColor;      // photosphere, luminance 1
float _AtmPlanetRadius;
float _AtmRadius;         // top of the atmosphere
float _AtmAtmDepth;       // _AtmRadius - _AtmPlanetRadius
float3 _AtmBetaRayleigh;
float3 _AtmBetaMie;
float3 _AtmBetaOzone;
float _AtmHr;
float _AtmHm;
float _AtmOzoneCenter;
float _AtmOzoneWidth;
float _AtmMieG;
float _AtmIntensity;
float _AtmStepCount;
float _AtmActive;         // 0 = no atmosphere at this observer: air is identity

// CPU LUTs: Sun transmittance and multiple scattering.
TEXTURE2D(_AtmTransmittanceTex);
SAMPLER(sampler_AtmTransmittanceTex);
TEXTURE2D(_AtmMultiScatterTex);
SAMPLER(sampler_AtmMultiScatterTex);

#define ATM_PI 3.14159265358979

// Scene radiance calibration applied on top of Intensity. Shared, so the disc
// and the dome keep the same absolute scale (see PlanetAtmosphere.shader).
#define ATM_RADIANCE_SCALE 5.0

// Ray/sphere intersection in the sphere's own radius units: the observer sits at
// planetary radius (1.1e6 m) and float32 would lose the differences if the
// quadratic were solved in metres.
bool AtmRaySphere(float3 ro, float3 rd, float radius, out float t0, out float t1)
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

float AtmRayleighPhase(float c)
{
    return (3.0 / (16.0 * ATM_PI)) * (1.0 + (c * c));
}

// Cornette-Shanks phase (normalised): haze/glow around the Sun.
float AtmMiePhase(float c, float g)
{
    float g2 = g * g;
    float num = 3.0 * (1.0 - g2) * (1.0 + (c * c));
    float den = 8.0 * ATM_PI * (2.0 + g2) * pow(max(1e-4, 1.0 + g2 - (2.0 * g * c)), 1.5);
    return num / den;
}

float3 AtmExtinction(float height)
{
    float dR = exp(-height / max(1.0, _AtmHr));
    float dM = exp(-height / max(1.0, _AtmHm));
    float dO = saturate(1.0 - abs((height - _AtmOzoneCenter) / max(1.0, _AtmOzoneWidth)));
    return (_AtmBetaRayleigh * dR) + (_AtmBetaMie * dM) + (_AtmBetaOzone * dO);
}

float3 AtmScattering(float height)
{
    float dR = exp(-height / max(1.0, _AtmHr));
    float dM = exp(-height / max(1.0, _AtmHm));
    return (_AtmBetaRayleigh * dR) + (_AtmBetaMie * dM);
}

float3 AtmSampleTransmittance(float height, float cosTheta)
{
    float2 uv = float2(cosTheta * 0.5 + 0.5, saturate(height / max(1e-3, _AtmAtmDepth)));
    return SAMPLE_TEXTURE2D_LOD(_AtmTransmittanceTex, sampler_AtmTransmittanceTex, uv, 0).rgb;
}

float3 AtmSampleMultiScatter(float height, float cosTheta)
{
    float2 uv = float2(cosTheta * 0.5 + 0.5, saturate(height / max(1e-3, _AtmAtmDepth)));
    return SAMPLE_TEXTURE2D_LOD(_AtmMultiScatterTex, sampler_AtmMultiScatterTex, uv, 0).rgb;
}

// Analytic integral over a homogeneous segment:
// transmittance · (S − S·transSeg) / sigmaE.
float3 AtmIntegrateOverSegment(float3 S, float3 transSeg, float3 transmittance, float3 sigmaE)
{
    return transmittance * (S - (S * transSeg)) / max(sigmaE, 1e-12);
}

// One raymarch from tStart to tEnd. Returns in-scatter; outTransmittance
// is the transmittance at the end of the path.
//
// Samples are the midpoints of N equal segments (offset 0.5), i.e. stratified
// sampling. With 48 segments and Hr ≈ 8.5 km the midpoint error is smooth and
// produces no flicker or banding, so no step jitter is needed.
float3 AtmIntegrate(float3 ro, float3 rd, float3 sunDir,
    float tStart, float tEnd, out float3 outTransmittance)
{
    int maxSteps = clamp((int)_AtmStepCount, 2, 128);

    // Empty (or inverted) segment: no atmosphere between the observer and the
    // surface. Must be explicit: tStart is the shell entry, and a cockpit wall
    // can be NEARER than it — a negative ds would turn exp(-sigma·ds) into a
    // bright artifact instead of "nothing".
    if (tEnd <= tStart)
    {
        outTransmittance = 1.0;
        return 0.0;
    }

    // Number of steps depends on PATH LENGTH, not always on the max.
    //
    // The density profile is exponential with scale Hr and is resolved when
    // ds = L/N is much smaller than Hr. Requiring ds <= Hr/16 (density change
    // per step <= 6 %) gives N = 16·L/Hr; the midpoint integral error is then
    // ~0.4 %, below the visibility threshold.
    //
    // Sky paths are hundreds of km, so N reaches maxSteps and the sky image is
    // unchanged. Terrain paths are metres, where a few long steps integrate the
    // exponential accurately and 48 fixed steps were pure overhead.
    int steps = clamp((int)round((16.0 * (tEnd - tStart)) / max(1.0, _AtmHr)), 1, maxSteps);
    float ds = (tEnd - tStart) / (float)steps;

    float cosA = dot(rd, sunDir);
    float phaseR = AtmRayleighPhase(cosA);
    float phaseM = AtmMiePhase(cosA, clamp(_AtmMieG, -0.9, 0.9));

    float3 inScatter = 0.0;
    float3 transmittance = 1.0;

    for (int i = 0; i < 128; i++)
    {
        if (i >= steps)
        {
            break;
        }

        float t = tStart + ((i + 0.5) * ds);
        float3 p = ro + (rd * t);
        float r = length(p);
        float height = max(0.0, r - _AtmPlanetRadius);
        float3 n = p / max(r, 1e-3);

        float3 sigmaE = AtmExtinction(height);
        float3 scattering = AtmScattering(height);
        float3 transSeg = exp(-sigmaE * ds);

        float cosSun = dot(n, sunDir);
        float3 sunT = AtmSampleTransmittance(height, cosSun);
        float3 ms = AtmSampleMultiScatter(height, cosSun);

        float3 rayleighScatter = _AtmBetaRayleigh * exp(-height / max(1.0, _AtmHr));
        float3 mieScatter = _AtmBetaMie * exp(-height / max(1.0, _AtmHm));
        float3 phaseScatter = (rayleighScatter * phaseR) + (mieScatter * phaseM);

        float3 S = (sunT * phaseScatter) + (ms * scattering);

        inScatter += AtmIntegrateOverSegment(S, transSeg, transmittance, sigmaE);
        transmittance *= transSeg;
    }

    outTransmittance = transmittance;
    return inScatter;
}

// Sky radiance seen through the whole air column along rd, in the same units the
// dome writes into the frame, plus the transmittance of that column.
//
// This is exactly the dome's sky branch (no terrain, planet not occluding), so
// an object composited as `T · objectRadiance + air` lands on the sky it is
// drawn over with no seam. Missed shell (observer in space, ray to open sky) or
// a disabled atmosphere returns an identity: T = 1, air = 0.
float3 AtmSkyRadiance(float3 rd, out float3 transmittance)
{
    transmittance = 1.0;
    if (_AtmActive < 0.5)
    {
        return 0.0;
    }

    float3 ro = _AtmCameraWS - _AtmBodyCenterWS;
    float a0;
    float a1;
    if (!AtmRaySphere(ro, rd, _AtmRadius, a0, a1))
    {
        return 0.0;
    }

    float tEnd = max(a1, 0.0);
    if (tEnd <= 0.0)
    {
        return 0.0;
    }

    float3 sunDir = normalize(_AtmSunDirWS);
    float3 scatter = AtmIntegrate(ro, rd, sunDir, max(a0, 0.0), tEnd, transmittance);

    float3 radianceScale = _AtmSunColor * (_AtmIntensity * ATM_RADIANCE_SCALE);
    return scatter * radianceScale;
}

#endif
