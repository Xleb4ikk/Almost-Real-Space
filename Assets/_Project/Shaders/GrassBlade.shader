Shader "Galilego/GrassBlade"
{
    // Специализированный шейдер травы. Отделён от GroundDecorSolid, потому
    // что оптимизации здесь неприменимы к деревьям/камням/кактусам.
    //
    // Что и почему сделано (все три пункта — измерением, не догадкой):
    //
    // 1. СВЕТ СЧИТАЕТСЯ В ВЕРШИНЕ, фрагмент отдаёт готовый цвет.
    //    Стоимость травы — не геометрия, а заполнение: игрок стоит на ковре
    //    из тысяч клинков, и каждый экранный пиксель земли перекрывается
    //    десятками клинков. У GroundDecorSolid на каждый фрагмент шли
    //    GalilegoSunShadow (до PCSS), SampleCloudShadow, GalilegoSkyAmbient и
    //    UnderwaterCausticMask. Перенос этого в вершину срезает самую дорогую
    //    часть целиком, потому что у клинка нормаль одна на весь меш
    //    (uniqueNormals = 1) — освещение по вершинам тут физически точное.
    //
    // 2. ТОЛЬКО ТИРЫ ТЕНЕЙ, КОТОРЫМ НЕ НУЖЕН positionSS.
    //    GalilegoSunShadow() берёт SV_Position в пикселях: для дизеринга
    //    InterleavedGradientNoise и для высокого тира PCSS. В вершинном
    //    шейдере этого нет, поэтому берём GalilegoShadowMedium /
    //    GalilegoShadowCheap — они считают только по positionWS/normalWS
    //    (GATHER_TEXTURE2D, без градиентов — вершинный texture fetch законен).
    //    Плата: у травы вблизи нет мягкой PCSS-тени. Для ковра из тонких
    //    клинков это незаметно.
    //
    // 3. НЕТ clip() И НЕТ DepthForwardOnly.
    //    У травы не было ни текстуры, ни вырезки (_Cutoff = 0), так что
    //    discard был мёртвым, но держал шейдер в режиме «может отбросить
    //    фрагмент» и ломал Early-Z. Пасс DepthForwardOnly отдавал ещё одну
    //    копию всей геометрии (ровно ×2 к треугольникам кадра): трава
    //    непрозрачна, ZWrite On, и после forward-паса глубина в буфере и так
    //    верна — атмосфера и звёзды её увидят.
    //
    // Cull Off остаётся намеренно: клинок — сложенная плоскость с
    // односторонним вингом (зеркальных пар треугольников в меше нет), и без
    // Cull Off половина клинков просто исчезала бы на обороте.

    Properties
    {
        _BaseColor("Base Color", Color) = (0.146, 0.371, 0.201, 1)
        _WindStrength("Wind Strength", Range(0.0, 1.0)) = 0.15
        _WindSpeed("Wind Speed", Float) = 1.5
        _Translucency("Translucency", Range(0.0, 1.0)) = 0.35
        _TwoSided("Two Sided Lighting", Range(0.0, 1.0)) = 1.0
        _ShadowMul("Shadow Multiplier", Range(0.0, 1.0)) = 1.0
        // Подмешивание к цвету земли на большой дистанции. Плотность травы
        // обязана падать с расстоянием (иначе инстансов больше, чем пикселей),
        // и без этого граница зоны читается как лысый круг. Подмешивание
        // делает редкую дальнюю траву того же тона, что и земля под ней, —
        // дальняя зона выглядит ровным полем, а не концом травы.
        _FarFadeStart("Far Fade Start (m)", Float) = 60.0
        _FarFadeEnd("Far Fade End (m)", Float) = 200.0
        _FarFadeColor("Far Fade Color", Color) = (0.146, 0.371, 0.201, 1)
        _FarFadeStrength("Far Fade Strength", Range(0.0, 1.0)) = 1.0
        _GroundTint("Ground Match Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "GrassBladeForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
            #pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
            #pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "GalilegoLighting.hlsl"
            #include "GalilegoUnderwaterCaustics.hlsl"

            // Время — ставит PlanetSurfaceRenderer.
            float _GroundDecorTime;

            float4 _BaseColor;
            float _WindStrength;
            float _WindSpeed;
            float _Translucency;
            float _TwoSided;
            float _ShadowMul;
            float _FarFadeStart;
            float _FarFadeEnd;
            float4 _FarFadeColor;
            float _FarFadeStrength;
            float4 _GroundTint;
            float3 _TerrainBodyCenterWS;
            // Палитра рельефа (ставит PlanetSurfaceRenderer.ApplyTerrainGlobals).
            float4 _ColGrass;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 color      : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Ветер: гнём верх клинка (локальный Y) в локальных осях XZ.
                // Мировые XZ на планете не горизонтальны — из-за этого трава
                // «летала» вверх-вниз и уходила под землю на склонах.
                // color.a — маска ветра: 1 листва, 0 ствол (деревья).
                float3 positionOS = input.positionOS;
                float phase = (_GroundDecorTime * _WindSpeed) + (positionOS.x * 2.1) + (positionOS.y * 1.7);
                float bend = saturate(positionOS.y) * _WindStrength * input.color.a;
                float gust = 0.55 + (0.45 * sin(phase));
                positionOS.z += bend * gust;

                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // ---- Всё освещение здесь, в вершине ----
                float3 n = normalize(normalWS);
                float3 l = normalize(_TerrainSunDir);
                float oneSided = saturate(dot(n, l));
                float twoSided = saturate(abs(dot(n, l)));
                float ndl = lerp(oneSided, twoSided, saturate(_TwoSided));
                float back = saturate(dot(-n, l)) * _Translucency * lerp(1.0, 0.25, saturate(_TwoSided));

                // Тень солнца: только world-space тиры, positionSS тут недоступен.
                // tierMedium сразу даёт 0 там, где трава стоит в тени рельефа.
                float shadow = (distance(positionWS, GetCameraPositionWS()) <= _ShadowMediumDistance)
                    ? GalilegoShadowMedium(positionWS, n, l)
                    : GalilegoShadowCheap(positionWS, n, l);
                shadow = lerp(1.0, shadow, saturate(_ShadowMul));

                float cloudShadow = SampleCloudShadow(positionWS);

                float sun = _TerrainSun;
                float3 radialUp = normalize(positionWS - _TerrainBodyCenterWS);
                float dayLocal = smoothstep(-0.12, 0.08, dot(radialUp, l));
                float3 light = float3(_NightAmbient, _NightAmbient, _NightAmbient)
                    + (GalilegoSkyAmbient(n) * _TerrainRadianceScale * dayLocal)
                    + (_SunLightColor * (sun * ndl * shadow * cloudShadow) * _TerrainRadianceScale)
                    + (_SunLightColor * (sun * back * shadow * cloudShadow) * _TerrainRadianceScale);

                // Подмешивание к цвету земли: дальняя трава обязана становиться
                // реже, поэтому на границе зоны без этого шага виден лысый круг.
                // Тон берём у палитры рельефа (_ColGrass ставит рендерер) и
                // гоним его через тот же _GroundTint, что и у ближней травы, —
                // иначе на закате подмешивание даёт чужой оттенок.
                float3 albedo = _BaseColor.rgb;
                float farT = saturate((distance(positionWS, GetCameraPositionWS()) - _FarFadeStart)
                    / max(1e-3f, _FarFadeEnd - _FarFadeStart));
                albedo = lerp(albedo, _ColGrass.rgb, farT * _FarFadeStrength);

                float3 color = albedo * light * _GroundTint.rgb;
                color += _UnderwaterCausticColor.rgb * UnderwaterCausticMask(positionWS, n);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return float4(input.color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
