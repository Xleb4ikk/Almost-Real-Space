Shader "Galilego/StarCorona"
{
    Properties
    {
        _CoronaColor("Corona Color (linear)", Color) = (1, 1, 1, 1)
        _CoronaIc("Corona Ic (scene radiance)", Float) = 0
        _CoronaExtent("Extent, stellar radii", Float) = 12
        _CoronaTime("Time, s", Float) = 0
        _CoronaRayCount("Polar ray count", Float) = 9
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+4" }

        Pass
        {
            Name "StarCoronaForward"
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
            #include "GalilegoStarOcclusion.hlsl"

            float4 _CoronaColor;
            float _CoronaIc;
            float _CoronaExtent;
            float _CoronaTime;
            float _CoronaRayCount;

            struct Attributes
            {
                float3 positionOS : POSITION;
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
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                return output;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
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

            float4 Frag(Varyings input) : SV_Target
            {
                if (_CoronaIc <= 0.0)
                {
                    discard;
                }

                float2 p = input.positionOS.xy * 2.0;
                float r = length(p);
                if (r >= 1.0)
                {
                    discard;
                }

                float extent = max(_CoronaExtent, 1.01);
                float rho = r * extent;
                if (rho < 1.0 || rho > extent)
                {
                    discard;
                }

                // Same contract as the disc and the glare: the corona is hidden
                // by whatever opaque surface hides the star.
                if (StarPixelOccluded(uint2(input.positionCS.xy)))
                {
                    discard;
                }

                float phi = atan2(p.y, p.x);
                float3 noiseCoord = float3(
                    cos(phi) * _CoronaRayCount,
                    sin(phi) * _CoronaRayCount,
                    (rho * 0.9) + (_CoronaTime * 0.025));
                float polar = ValueNoise3(noiseCoord);
                float sharp = pow(0.5 + 0.5 * sin((phi * _CoronaRayCount) + (polar * 5.0)), 2.0);
                float rays = (0.35 + 0.65 * polar) * (0.55 + 0.45 * sharp);

                float radial = (0.7 * pow(rho, -7.0)) + (0.3 * pow(rho, -2.5));
                float inner = smoothstep(1.0, 1.015, rho);
                float outer = 1.0 - smoothstep(extent * 0.72, extent, rho);

                float I = _CoronaIc * radial * rays * inner * outer;
                return float4(_CoronaColor.rgb * I, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
