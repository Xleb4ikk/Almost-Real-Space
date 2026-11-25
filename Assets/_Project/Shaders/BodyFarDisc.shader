Shader "Galilego/BodyFarDisc"
{
    // Billboarding stand-in for a solid body that is OUTSIDE the camera's far plane.
    // The terrain camera clips at the physical horizon (~300 km from the ground on
    // Terra), so a moon at 83 000 km is never drawn by its own chunks; this disc
    // keeps the same angular size and the correct phase, like GasGiantView's far disc.
    Properties
    {
        [HideInInspector] _BFD_Map("Surface Map", 2D) = "white" {}
        [HideInInspector] _BFD_MapStrength("Map Strength", Float) = 0
        // 0 — the lunar brightness map (factor/2 in R), 1 — a real RGB albedo map
        // (the terrain-derived disc of an atmospheric planet, where a scalar would
        // turn the oceans red). A material only carries declared properties: an
        // undeclared switch reads 0 and a colour map would be sampled as R only.
        [HideInInspector] _BFD_MapColor("Map Is Colour", Float) = 0
        [HideInInspector] _BFD_NormalMap("Surface Relief (body-space normal)", 2D) = "white" {}
        [HideInInspector] _BFD_NormalStrength("Relief Strength", Float) = 0
        // VECTOR, not Color. BodyFarDiscView passes an ALREADY-LINEAR albedo
        // (TerrainPaletteData.SnowLinear = 0.214 for the 0.5 grey the palette is
        // authored in). A ShaderLab Color property is sRGB and Unity DECODES it to
        // linear on upload in this project (Linear colour space), so the value was
        // converted twice: 0.214 reached the shader as 0.0366 and every far disc was
        // 5.8x darker than the body's own terrain under the same sun — the "grey
        // moon in space that turns bright only when you arrive" report. The
        // SetVector/GetVector round-trip in C# never shows it (both hold the stored
        // gamma value), and the material readback of the property looks correct.
        // A Vector property is uploaded verbatim, which is what the call site means.
        [HideInInspector] _BFD_Albedo("Albedo (linear)", Vector) = (0.214, 0.205, 0.187, 1)
        [HideInInspector] _BFD_SunDirWS("Sun Direction", Vector) = (1, 0, 0, 0)
        [HideInInspector] _BFD_RightWS("Screen Right", Vector) = (1, 0, 0, 0)
        [HideInInspector] _BFD_UpWS("Screen Up", Vector) = (0, 1, 0, 0)
        [HideInInspector] _BFD_ViewWS("View Direction", Vector) = (0, 0, 1, 0)
        [HideInInspector] _BFD_IntensityScale("Intensity Scale", Float) = 1
        [HideInInspector] _BFD_DiscDistance("Disc Distance", Float) = 1000
        [HideInInspector] _BFD_LimbDarkening("Limb Darkening", Float) = 0.35
        [HideInInspector] _BFD_Ambient("Ambient", Float) = 0
        [HideInInspector] _BFD_Opacity("Opacity", Float) = 1
        [HideInInspector] _BFD_Eclipse("Eclipse Visibility", Float) = 1
        [HideInInspector] _BFD_EclipseRed("Eclipse Atmospheric Refraction", Float) = 1
        // These three MUST be declared here, not only in HLSL below. A material can
        // only carry the properties the shader declares: Material.SetVector/SetFloat
        // on an undeclared uniform is silently dropped, the shader reads 0, and the
        // disc's radiance becomes 0 — the billboard turns invisible, so a body outside
        // the camera's far plane (the Moon from > ~2 000 km) simply is not drawn, while
        // its terrain chunks still show it from up close ("the Moon is black, I can
        // only see it when I'm near"). _BFD_Haze had the same hole: the aerial
        // perspective of the disc was never applied at all.
        [HideInInspector] _BFD_SunColor("Sun Color", Vector) = (1, 1, 1, 0)
        [HideInInspector] _BFD_SunScale("Sun Scale", Float) = 1
        [HideInInspector] _BFD_Haze("Haze", Float) = 1
        // The body->world rotation as its three columns. A Matrix property cannot be
        // declared in ShaderLab, and an undeclared one is not carried by the material
        // (Unity logs "doesn't have a matrix property" and the shader keeps an identity),
        // so the body-fixed map lookup rotated with the WORLD instead of with the body.
        [HideInInspector] _BFD_BodyRight("Body Right", Vector) = (1, 0, 0, 0)
        [HideInInspector] _BFD_BodyUp("Body Up", Vector) = (0, 1, 0, 0)
        [HideInInspector] _BFD_BodyForward("Body Forward", Vector) = (0, 0, 1, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+4" }

        Pass
        {
            Name "BodyFarDiscForward"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // The same atmosphere march the sky dome runs. The disc is drawn AFTER
            // the dome (queue Transparent+4 vs Transparent), so the frame behind it
            // is already a finished sky: the disc must not paint over that sky as if
            // there were no air. `T` is the transmittance of the whole column to
            // space, `air` the in-scattered radiance of that column — the disc
            // composites as `T·body + air`, which is what makes a daytime Moon a
            // pale blue disc instead of a white lamp on a blue sky.
            #include "GalilegoAtmosphereAir.hlsl"

            float4 _BFD_Albedo;
            TEXTURE2D(_BFD_Map);
            float _BFD_MapStrength;
            float _BFD_MapColor;
            TEXTURE2D(_BFD_NormalMap);
            float _BFD_NormalStrength;
            float4 _BFD_BodyRight;
            float4 _BFD_BodyUp;
            float4 _BFD_BodyForward;

            // Body <-> world for the baked maps. Same result as the old matrix mul, but
            // through properties a material can actually carry (see the Properties block).
            float3 BodyToWorldDir(float3 v)
            {
                return (_BFD_BodyRight.xyz * v.x) + (_BFD_BodyUp.xyz * v.y) + (_BFD_BodyForward.xyz * v.z);
            }

            float3 WorldToBodyDir(float3 v)
            {
                return float3(dot(v, _BFD_BodyRight.xyz), dot(v, _BFD_BodyUp.xyz), dot(v, _BFD_BodyForward.xyz));
            }
            float4 _BFD_SunDirWS;
            float4 _BFD_RightWS;
            float4 _BFD_UpWS;
            float4 _BFD_ViewWS;
            float _BFD_IntensityScale;
            float _BFD_DiscDistance;
            float _BFD_LimbDarkening;
            float _BFD_Ambient;
            float _BFD_Opacity;
            float _BFD_Eclipse;
            float _BFD_EclipseRed;
            // The star's photosphere (luminance 1) and the scene's albedo·ndl → radiance
            // factor. Both come from BodyFarDiscView, so the disc is lit by the star and
            // shares the terrain's scale even when no atmosphere is drawn.
            float4 _BFD_SunColor;
            float _BFD_SunScale;
            // How much of the air in front of the disc to apply. 1 = the dome's own
            // physics, 0 = the old vacuum look. Keep 1: the veil in front of the Moon
            // IS the sky behind it, so thinning it makes the unlit half darker than
            // the sky and the disc reads as a night moon again. "Less haze" is tuned
            // with the disc's brightness, not here.
            float _BFD_Haze;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 centered = (input.uv * 2.0) - 1.0;
                float r2 = dot(centered, centered);
                if (r2 > 1.0)
                {
                    discard;
                }

                // Sphere normal in world space: the quad is camera-facing, so the
                // sphere is reconstructed from the screen basis and the disc normal.
                float nz = sqrt(max(0.0, 1.0 - r2));
                float3 normalWS = normalize(
                    (_BFD_RightWS.xyz * centered.x) + (_BFD_UpWS.xyz * centered.y) + (_BFD_ViewWS.xyz * nz));

                float ndl = saturate(dot(normalWS, _BFD_SunDirWS.xyz));
                float limb = 1.0 - (_BFD_LimbDarkening * (1.0 - nz));
                // Body-fixed albedo map (maria / highlands) so a 20 px disc still reads as
                // THIS moon and not as a featureless ball. Same lat/lon convention as
                // LunarRelief: dir = (cos lat cos lon, cos lat sin lon, sin lat).
                float3 nb = WorldToBodyDir(normalWS);
                float lat = asin(clamp(nb.z, -1.0, 1.0));
                float lon = atan2(nb.y, nb.x);
                float2 uvMap = float2((lon * 0.15915494) + 0.5, (lat * 0.31830989) + 0.5);

                // Baked body-space SURFACE normal: the disc is a flat quad with a sphere
                // reconstructed from the screen basis, so without this the approach shows
                // a painted ball and the craters appear only when the chunks take over.
                // The map holds the real relief (rims, basin walls, rilles), lit by the
                // same Lambert term, which is what makes the hand-over invisible.
                if (_BFD_NormalStrength > 0.0)
                {
                    float3 mapN = (SAMPLE_TEXTURE2D(_BFD_NormalMap, s_linear_repeat_sampler, uvMap).xyz * 2.0) - 1.0;
                    float len2 = dot(mapN, mapN);
                    if (len2 > 1e-4)
                    {
                        nb = normalize(lerp(nb, mapN * rsqrt(len2), _BFD_NormalStrength));
                        float3 nWorld = normalize(BodyToWorldDir(nb));
                        ndl = saturate(dot(nWorld, _BFD_SunDirWS.xyz));
                    }
                }

                // The baked map stores factor/2 (0..1 in an 8-bit texture), so the albedo
                // multiplier is read back at its true 0.28..1.8 range. A colour map (the
                // body's own terrain palette) is used as-is: it already IS albedo, and
                // taking only R would paint an ocean world red. Repeat sampling: u spans
                // the full 360 deg and the antimeridian is a real wrap, not a clamp edge.
                float3 mapSample = SAMPLE_TEXTURE2D(_BFD_Map, s_linear_repeat_sampler, uvMap).rgb;
                float3 mapAlbedo = _BFD_MapColor > 0.5 ? mapSample : (mapSample.r * 2.0).xxx;
                float3 albedo = _BFD_Albedo.rgb * lerp(1.0, mapAlbedo, _BFD_MapStrength);

                // Lunar eclipse: the Sun is hidden by the parent planet, and the only
                // light is refracted through its atmosphere — a dim copper disc, not a
                // black hole and not a full moon. The floor (0.002) keeps totality
                // visible; the geometric flux is far smaller.
                float eclipse = saturate(_BFD_Eclipse);
                float eclipseGain = 0.002 + (0.998 * eclipse);
                // Refraction through the occluder's air is the copper "blood moon"
                // colour. An airless occluder (or a small one that only clips the
                // disc) leaves a neutral shadow: no atmosphere, no red ring.
                float3 eclipseTint = lerp(
                    lerp(float3(1.0, 1.0, 1.0), float3(0.62, 0.16, 0.07), saturate(_BFD_EclipseRed)),
                    float3(1.0, 1.0, 1.0),
                    eclipse);

                // Surface radiance in SCENE units: the same "sunlit albedo·ndl"
                // scale the terrain and the decor use, so the disc has the same
                // brightness as the body's own relief under it at the hand-over.
                // _BFD_SunColor is the star's photosphere, luminance 1, with no
                // atmosphere in it: the disc is lit by the star, not by the
                // observer's sky.
                float3 sunlit = _BFD_SunColor.rgb * _BFD_SunScale;
                float3 radiance = albedo * eclipseTint * (ndl + _BFD_Ambient)
                    * limb * sunlit * _BFD_IntensityScale * eclipseGain;

                // Aerial perspective. The moon is behind the observer's whole air
                // column: T dims and warms it, `air` is the daylight scattered in
                // front of it. At night air ≈ 0 and T ≈ 1, so the night moon is
                // untouched by this; by day the added blue is what makes it read
                // as a moon behind an atmosphere rather than one in vacuum.
                //
                // HazeStrength interpolates between vacuum (0) and the dome's own
                // column (1) — the same fraction of the veil and of the dimming, so
                // 1 stays exactly the physics the sky is drawn with.
                float haze = saturate(_BFD_Haze);
                float3 atmosphereTransmittance;
                float3 airlight = AtmSkyRadiance(normalize(-_BFD_ViewWS.xyz), atmosphereTransmittance);
                float3 veil = airlight * haze;
                float3 transmittance = saturate(1.0 - ((1.0 - atmosphereTransmittance) * haze));
                float3 color = (radiance * transmittance) + veil;

                // One-pixel feather: a 6 px disc stepped hard reads as a square.
                float edge = max(1e-5, fwidth(r2) * 1.5);
                float alpha = saturate((1.0 - r2) / edge) * _BFD_Opacity;

                return float4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
