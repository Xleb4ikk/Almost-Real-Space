// Galilego/EnginePlume — volumetric engine exhaust for HDRP (unlit, premultiplied blend).
// A box is drawn (back faces only); each pixel ray-marches the analytic plume field inside it.
// All parameters are pushed by PlumeEmitter.cs. Lengths inside the field are in nozzle-exit radii.
Shader "Galilego/EnginePlume"
{
    Properties
    {
        _PlumeA ("Plume A (theta, n, sourceDist, exitRadius)", Vector) = (0.05, 4, 20, 0.5)
        _PlumeB ("Plume B (nits, envLength, diamondGain, cellLength)", Vector) = (6000, 14, 1.6, 3)
        _PlumeC ("Plume C (barrel, afterburnK, air, turbulence)", Vector) = (0.22, 0.35, 1, 0.35)
        _PlumeD ("Plume D (halfWidth, length, absorption, free)", Vector) = (5, 60, 0, 0)
        _ColCore ("Core", Color) = (0.55, 0.70, 1.00, 1)
        _ColEdge ("Edge", Color) = (0.30, 0.28, 0.95, 1)
        _ColAfter ("Afterburn", Color) = (1.00, 0.55, 0.30, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 d3d12 playstation xboxone xboxseries vulkan metal switch
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _PlumeA;   // thetaB, n, sourceDist, exitRadius (m)
                float4 _PlumeB;   // intensity (nits), envLength, diamondGain, cellLength
                float4 _PlumeC;   // barrelAmp, afterburnK, air, turbulence
                float4 _PlumeD;   // boxHalfWidth (m), boxLength (m), absorption, free
                float4 _ColCore;
                float4 _ColEdge;
                float4 _ColAfter;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionRWS : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 rws = TransformObjectToWorld(v.positionOS);
                o.positionRWS = rws;
                o.positionOS = v.positionOS;
                o.positionCS = TransformWorldToHClip(rws);
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(Hash31(i), Hash31(i + float3(1, 0, 0)), f.x),
                               lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x),
                               lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            // Plume field at (z along the exhaust, x/y across), all in exit radii.
            // core: free-expansion / jet emission; ab: afterburning sheath.
            void PlumeField(float z, float2 xy, out float core, out float ab)
            {
                core = 0; ab = 0;
                if (z < 0.0) return;

                float thetaB = _PlumeA.x, n = _PlumeA.y, ds = _PlumeA.z;
                float lEnv = _PlumeB.y, dGain = _PlumeB.z, lc = max(_PlumeB.w, 0.1);
                float amp = _PlumeC.x, abK = _PlumeC.y, air = _PlumeC.z, turb = _PlumeC.w;
                float freeK = _PlumeD.w;

                float rho = length(xy);
                float sn = abs(sin(PI * z / lc));
                float k = 1.0 + amp * sn * exp(-z / (3.0 * lc));
                float r = rho / k;
                float zz = z + ds;
                float R = sqrt(r * r + zz * zz);

                // Ragged, travelling boundary: the shear layer of a real jet is never a clean
                // cone — the edge boils and the whole boundary breathes with the flow.
                float t = _TimeParameters.x;
                float ang = atan2(xy.y, xy.x);
                float wob = sin(ang * 3.0 + (z * 0.22) - (t * 5.0)) * 0.6
                          + sin((ang * 5.0) - (z * 0.40) - (t * 8.5)) * 0.4;
                float thLim = thetaB * (1.0 + (0.55 * turb * wob));

                float th = atan2(r, zz);
                if (th >= thLim) { core = 0; }
                else
                {
                    float x = saturate(th / thLim);
                    float g = pow(max(cos(0.5 * PI * x), 0.0), n);
                    float D = (ds / R) * (ds / R) * g;
                    float dia = 1.0 + dGain * pow(1.0 - sn, 6.0) * exp(-z / (4.0 * lc));
                    core = D * exp(-z / lEnv) * dia;
                }

                // Churn: the gas pattern scrolls downstream (faster near the nozzle) and is
                // strongest at the shear layer, so the edge boils instead of fading as a
                // smooth gradient. Runs in vacuum too (turb has a floor there).
                float rb = max(zz * tan(min(thLim, 1.2)) * k, 1e-3);
                float edgeW = smoothstep(0.3, 1.0, rho / rb);
                if (turb > 0.001)
                {
                    float flow = t * (6.0 + (14.0 / (1.0 + (z * 0.18))));
                    float n1 = Noise3(float3(xy * 1.05, (z * 0.55) - flow));
                    float n2 = Noise3(float3((xy * 2.6) + 11.7, (z * 1.45) - (flow * 1.8)));
                    float churn = ((n1 - 0.5) * 0.7) + ((n2 - 0.5) * 0.3);
                    core *= max(0.0, 1.0 + (turb * churn * 4.5 * (0.2 + (1.1 * edgeW))));
                }

                float sh = exp(-pow(((rho / rb) - 0.95) / 0.28, 2.0));
                float ramp = (1.0 - exp(-z / 6.0)) * exp(-z / max(2.2 * lEnv, 1.0));
                ab = abK * sh * ramp * (1.0 - freeK);
                ab *= 1.0 + (turb * wob * 0.5);

                // Fade out before the far face of the box: the line of sight is longest at the
                // plane where the volume is cut, so without this taper the plume ends in a
                // bright rounded "cap" instead of dissolving into space.
                float boxLen = _PlumeD.y / max(_PlumeA.w, 1e-4);
                float taper = 1.0 - smoothstep(0.55 * boxLen, boxLen, z);
                core *= taper;
                ab *= taper;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float3 ro = TransformWorldToObject(float3(0, 0, 0));   // camera in camera-relative space
                float3 rd = i.positionOS - ro;                          // t = 1 at this (back-face) fragment
                rd += float3(1e-8, 1e-8, 1e-8) * sign(rd + 1e-12);
                float3 inv = 1.0 / rd;
                float3 t0 = (-0.5 - ro) * inv, t1 = (0.5 - ro) * inv;
                float3 tmin = min(t0, t1), tmax = max(t0, t1);
                float tn = max(max(tmin.x, tmin.y), max(tmin.z, 0.0));
                float tf = min(min(tmax.x, tmax.y), min(tmax.z, 1.0));

                // Do not paint over nearer geometry: stop the ray at the scene depth.
                float distFrag = max(length(i.positionRWS), 1e-4);
                float3 viewDir = i.positionRWS / distFrag;
                float eye = LinearEyeDepth(LoadCameraDepth(i.positionCS.xy), _ZBufferParams);
                float sceneDist = eye / max(dot(viewDir, GetViewForwardDir()), 1e-3);
                tf = min(tf, sceneDist / distFrag);
                if (tf <= tn) return float4(0, 0, 0, 0);

                float rE = _PlumeA.w;
                float2 boxXY = float2(_PlumeD.x * 2.0, _PlumeD.y);   // scale of the unit cube: (2W, L)
                const int STEPS = 28;
                float dt = (tf - tn) / STEPS;
                float seg = dt * distFrag / rE;                        // segment length in exit radii
                float jit = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));

                float3 sumCore = 0, sumAb = 0;
                float optical = 0;
                for (int s = 0; s < STEPS; s++)
                {
                    float3 p = ro + rd * (tn + (s + jit) * dt);
                    float zRe = (p.z + 0.5) * _PlumeD.y / rE;
                    float2 xyRe = p.xy * boxXY.x / rE;
                    float core, ab;
                    PlumeField(zRe, xyRe, core, ab);
                    float hot = smoothstep(0.02, 0.6, core);
                    float3 col = lerp(_ColEdge.rgb, _ColCore.rgb, hot);
                    sumCore += col * core * seg;
                    sumAb += _ColAfter.rgb * ab * seg;
                    optical += core * seg;
                }

                float3 rgb = (sumCore + sumAb) * _PlumeB.x * GetCurrentExposureMultiplier();
                rgb = min(rgb, 60000.0);
                float alpha = 1.0 - exp(-_PlumeD.z * optical);
                return float4(rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
