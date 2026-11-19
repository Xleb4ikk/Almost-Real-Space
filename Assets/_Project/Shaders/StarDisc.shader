Shader "Galilego/StarDisc"
{
    Properties
    {
        _StarColor("Star Color (linear, adapted)", Color) = (1, 1, 1, 1)
        _Brightness("Compressed disc brightness V'", Float) = 1
        _LimbDarkening("Limb darkening R/G/B", Vector) = (0.62, 0.78, 0.95, 0)
        _GranulationCells("Granulation cells across the disc", Float) = 100
        _GranulationContrast("Granulation contrast", Float) = 0.07
        _GranulationFrequency("Granulation cells per radian", Float) = 10000
        _GranulationEnabled("Granulation enabled", Float) = 0
        _SpotCoverage("Spot coverage 0..1", Float) = 0
        _Activity("Activity 0..1", Float) = 0
        _Seed("Surface seed", Float) = 1
    }

    SubShader
    {
        // Drawn after the atmosphere; the manual depth test below is the only
        // occluder (same contract as the old SunDisc shader).
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+2" }

        Pass
        {
            Name "StarDiscForward"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            float4 _StarColor;
            float _Brightness;
            float4 _LimbDarkening;
            float _GranulationCells;
            float _GranulationContrast;
            float _GranulationFrequency;
            float _GranulationPhase;
            float _GranulationTime;
            float _GranulationEnabled;
            float _SpotCoverage;
            float4 _SpotUmbraColor;
            float4 _SpotPenumbraColor;
            float _Activity;
            float3 _StarToCameraLocal;
            float3 _StarTangentLocal;
            float3 _StarBitangentLocal;
            float _ChromosphereIntensity;
            float4 _ChromosphereColor;
            float _EclipseVisibility;
            float _PixelAngle;
            float _DiscOverscan;
            float _Seed;

            // Set by SunBillboard/StarRenderer: the observer's own craft must not
            // swallow the star, but the world (planet, terrain, buildings) must.
            float _SunSelfOccluder;
            float _SunFarLimit;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionOS = input.positionOS;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // 3D Worley F1: 27 hash samples, no UV and no pole seams.
            float Cellular3(float3 p)
            {
                float3 ip = floor(p);
                float3 fp = frac(p);
                float best = 8.0;
                [unroll]
                for (int z = -1; z <= 1; z++)
                {
                    [unroll]
                    for (int y = -1; y <= 1; y++)
                    {
                        [unroll]
                        for (int x = -1; x <= 1; x++)
                        {
                            float3 g = float3(x, y, z);
                            float3 o = Hash33(ip + g);
                            float3 r = g + o - fp;
                            best = min(best, dot(r, r));
                        }
                    }
                }

                return sqrt(best);
            }

            float ValueNoise3(float3 p)
            {
                float3 ip = floor(p);
                float3 fp = frac(p);
                fp = fp * fp * (3.0 - 2.0 * fp);

                float n000 = Hash31(ip + float3(0, 0, 0));
                float n100 = Hash31(ip + float3(1, 0, 0));
                float n010 = Hash31(ip + float3(0, 1, 0));
                float n110 = Hash31(ip + float3(1, 1, 0));
                float n001 = Hash31(ip + float3(0, 0, 1));
                float n101 = Hash31(ip + float3(1, 0, 1));
                float n011 = Hash31(ip + float3(0, 1, 1));
                float n111 = Hash31(ip + float3(1, 1, 1));

                float nx00 = lerp(n000, n100, fp.x);
                float nx10 = lerp(n010, n110, fp.x);
                float nx01 = lerp(n001, n101, fp.x);
                float nx11 = lerp(n011, n111, fp.x);
                float nxy0 = lerp(nx00, nx10, fp.y);
                float nxy1 = lerp(nx01, nx11, fp.y);
                return lerp(nxy0, nxy1, fp.z);
            }

            float Fbm3(float3 p)
            {
                float sum = ValueNoise3(p);
                sum += 0.5 * ValueNoise3((p * 2.03) + 17.1);
                sum += 0.25 * ValueNoise3((p * 4.11) + 31.7);
                return sum / 1.75;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionOS.xy * 2.0;
                float rn = length(p) * _DiscOverscan;
                float aa = max(fwidth(rn), 0.0005);
                float edge = 1.0 - smoothstep(1.0 - aa, 1.0 + aa, rn);
                if (edge <= 0.001)
                {
                    discard;
                }

                float rawDepth = LoadCameraDepth(uint2(input.positionCS.xy));
                if (!IsSky(rawDepth))
                {
                    float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                    if (eyeDepth > max(_SunSelfOccluder, 0.05) && eyeDepth < _SunFarLimit)
                    {
                        discard;
                    }
                }

                float mu = sqrt(saturate(1.0 - rn * rn));

                // Linear limb darkening, normalised so the integrated flux does
                // not depend on u: I0 = V·c / (1 − u/3).
                float3 u = saturate(_LimbDarkening.rgb);
                float3 denom = max(1e-4, 1.0 - u / 3.0);
                float3 I = _Brightness * _StarColor.rgb / denom * (1.0 - u * (1.0 - mu));

                // Surface direction in star-local coordinates. The star's own
                // orientation rotates the pattern; the basis is the quad's local
                // X/Y mapped into the star frame, so no UV poles exist.
                float3 dirLocal = normalize(
                    _StarToCameraLocal + (p.x * _StarTangentLocal) + (p.y * _StarBitangentLocal));

                // Granulation: two octaves of 3D Worley cells, faded out when the
                // screen footprint is below one cell (no shimmer / aliasing).
                if (_GranulationEnabled > 0.5)
                {
                    float ca = cos(_GranulationPhase);
                    float sa = sin(_GranulationPhase);
                    float3 gp = dirLocal * _GranulationFrequency;
                    float3 rp = float3(
                        (ca * gp.x) - (sa * gp.y),
                        (sa * gp.x) + (ca * gp.y),
                        gp.z);
                    rp += float3(0.37, 0.11, 0.83) * (_GranulationTime * 0.02);

                    float footprint = max(fwidth(rp.x), max(fwidth(rp.y), fwidth(rp.z)));
                    float fade = 1.0 - smoothstep(0.18, 0.65, footprint);
                    if (fade > 0.002)
                    {
                        float c1 = Cellular3(rp);
                        float c2 = Cellular3((rp * 2.17) + 11.3);
                        float g1 = 1.0 - smoothstep(0.0, 0.72, c1);
                        float g2 = 1.0 - smoothstep(0.0, 0.72, c2);
                        float g = (((g1 * 0.67) + (g2 * 0.33)) - 0.5) * 2.0;
                        I *= 1.0 + (_GranulationContrast * g * fade);
                    }
                }

                // Spots: low-frequency 3D noise + threshold. Coverage is the
                // requested area fraction; the threshold is the uniform-noise
                // quantile 1 − f. Umbra is cooler than the surrounding penumbra.
                float spotField = Fbm3((dirLocal * 18.0) + float3(_Seed, _Seed * 0.7, _Seed * 1.3));
                float threshold = 1.0 - saturate(_SpotCoverage);
                float penumbra = smoothstep(threshold - 0.035, threshold + 0.035, spotField);
                float umbra = smoothstep(threshold + 0.055, threshold + 0.135, spotField);
                float3 spotColor = lerp(_SpotPenumbraColor.rgb, _SpotUmbraColor.rgb, umbra);
                I *= lerp(float3(1.0, 1.0, 1.0), spotColor, penumbra);

                // Faculae: +3…8 % near the limb, proportional to activity.
                float flareBand = smoothstep(0.3, 0.0, mu);
                float flareNoise = ValueNoise3((dirLocal * 7.0) + (_GranulationTime * 0.03));
                I += _Brightness * _StarColor.rgb * (_Activity * 0.055 * flareBand * (0.5 + 0.5 * flareNoise));

                // Chromosphere: thin H-alpha ring, only when the disc is eclipsed.
                float ring = exp(-pow((rn - 1.0) / 0.02, 2.0));
                I += _ChromosphereColor.rgb * (_ChromosphereIntensity * ring);

                I *= _EclipseVisibility * edge;
                return float4(I, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
