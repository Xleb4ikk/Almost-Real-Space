#ifndef GALILEGO_STAR_OCCLUSION_INCLUDED
#define GALILEGO_STAR_OCCLUSION_INCLUDED

// Shared occlusion test for the star proxies (disc, glare, corona).
// All three are camera-facing quads drawn with ZTest Always, so the scene depth
// is the only thing that can hide the star; the star itself is never in the
// depth buffer (it sits at a fixed proxy distance).

// Set once per frame by StarRenderer. Both are global floats.
float _SunSelfOccluder;
float _SunFarLimit;

// True where an opaque surface between the eye and space hides the star.
// A depth at/over the far plane is empty sky, not an occluder (the test never
// relies on IsSky alone), and anything nearer than _SunSelfOccluder is the
// observer's own craft in EVA - a black silhouette against space that would
// otherwise swallow the star without showing anything.
bool StarPixelOccluded(uint2 pixel)
{
    float rawDepth = LoadCameraDepth(pixel);
    if (IsSky(rawDepth))
    {
        return false;
    }

    float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    return eyeDepth > max(_SunSelfOccluder, 0.05) && eyeDepth < _SunFarLimit;
}

#endif
