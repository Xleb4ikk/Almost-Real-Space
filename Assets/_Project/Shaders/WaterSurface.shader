Shader "Galilego/WaterSurface"
{
    Properties
    {
        _WaterDeep("Water Deep", Color) = (0.003, 0.07, 0.20, 1)
        _WaterMid("Water Mid", Color) = (0.012, 0.22, 0.45, 1)
        _WaterShallow("Water Shallow", Color) = (0.10, 0.55, 0.60, 1)
        _FoamColor("Foam Color", Color) = (0.88, 0.93, 0.95, 1)
        _UnderwaterColor("Underwater Tint", Color) = (0.02, 0.16, 0.30, 1)
        _SpecularPower("Specular Power", Float) = 420.0
        _SpecularIntensity("Specular Intensity", Float) = 1.0
        // Две скроллящиеся normal map (схема HOWTO-Water: вторая мельче первой,
        // скролл в противоход). Дефолт "bump" = плоская нормаль, рендерер
        // подменяет запечёнными (Resources/Water/water_normal_{a,b}).
        _NormalMap0("Water Normal A", 2D) = "bump" {}
        _NormalMap1("Water Normal B", 2D) = "bump" {}
        _UseNormalMap("Use Normal Maps", Float) = 0.0
        _NormalStrength("Normal Map Strength", Float) = 1.0
        _NormalScale0("Normal Scale A", Float) = 0.08
        _NormalScale1("Normal Scale B", Float) = 0.16
        _NormalScroll0("Normal Scroll A (uv/sec)", Vector) = (0.06, 0.02, 0, 0)
        _NormalScroll1("Normal Scroll B (uv/sec)", Vector) = (-0.045, 0.03, 0, 0)
        _WaveStrength("Wave Strength", Float) = 0.75
        _WaveScale("Wave Scale", Float) = 0.18
        _WaveAmplitude("Wave Amplitude (m)", Float) = 0.9
        _SparkleStrength("Sparkle Strength", Float) = 0.85
        _SparkleScale("Sparkle Scale Mul", Float) = 8.0
        _ShadingFadeFloor("Shading Fade Floor", Range(0.0, 1.0)) = 0.85
        _DetailFadeFloor("Detail Fade Floor", Range(0.0, 1.0)) = 0.0
        _RippleStrength("Ripple Strength", Float) = 1.0
        _RippleScale("Ripple Scale Mul", Float) = 10.0
        _RippleSpeed("Ripple Speed", Float) = 2.2
        _FoamCrestStrength("Foam Crest", Float) = 0.8
        _FoamDepthMeters("Foam Depth (m)", Float) = 6.0
        _FoamSlopeGain("Foam Slope Gain", Float) = 3.0
        _ShoreFadeMeters("Shore Fade (m)", Float) = 8.0
        _AbsorbShallow("Absorb Shallow (m)", Float) = 4.5
        _AbsorbDeep("Absorb Deep (m)", Float) = 28.0
        _ShallowAlpha("Shallow Alpha", Range(0.0, 1.0)) = 0.7
        _SurfStrength("Surf Strength", Float) = 1.2
        _SurfSpeed("Surf Speed", Float) = 2.0
        _FresnelStrength("Fresnel Strength", Float) = 1.2
        _MinAlpha("Min Alpha", Range(0.0, 1.0)) = 0.55
        _MaxAlpha("Max Alpha", Range(0.0, 1.0)) = 1.0
        _UnderwaterAlpha("Underwater Alpha", Range(0.0, 1.0)) = 1.0
        _UnderwaterRippleStrength("Underwater Ceiling Ripple", Float) = 0.4
        _UnderwaterCausticStrength("Underwater Caustic Strength", Float) = 0.6
        _UnderwaterCausticScale("Underwater Caustic Scale", Float) = 0.4
        _UnderwaterCausticSpeed("Underwater Caustic Speed", Float) = 1.6
        _UnderwaterRimStrength("Underwater Window Rim", Float) = 0.5
    }
    SubShader
    {
        // Вода — AlphaTest-очередь (opaque-список): проверено — Transparent-очередь
        // HDRP наш ForwardOnly-пас не рисует вообще (вода пропадает, видно голое
        // дно). Поэтому прозрачность мелководья — screen-door dither 1px IGN:
        // мелкое зерно, к миру привязано полосой alphaT 0-2 м у уреза.
        // НЕ квантовать порог по мировым ячейкам — они дают крупные блок-пиксели
        // вблизи. Чанки копланарны (bob=0), z-fight нет.
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        Pass
        {
            Name "WaterSurfaceForward"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            // DEBUG-MINIMAL3: без shadow-вариантов и без GalilegoLighting
            // (HDShadow). Если вода появится — виноват один из них.
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            // DEBUG-MINIMAL3: локальные стабы вместо GalilegoLighting.hlsl.
            float3 _TerrainSunDir;
            float _NightAmbient;
            float _SkyAmbient;
            float _TerrainSun;
            float3 _SunLightColor;
            float3 _SkyAmbientColor;
            float3 _SkyUp;
            float _TerrainRadianceScale;
            float3 GalilegoSkyAmbient(float3 n)
            {
                float hemi = saturate((dot(n, _SkyUp) * 0.5) + 0.5);
                return _SkyAmbientColor * (_SkyAmbient * (0.35 + 0.65 * hemi));
            }

            // Мировая позиция камеры (ставит PlanetSurfaceRenderer каждый кадр).
            float3 _PlanetCameraPos;
            // Центр планеты в render-пространстве: единый радиальный базис
            // воды для всех чанков/LOD (без него — ступень яркости на стыках).
            float3 _PlanetWaterCenter;
            // Время анимации волн (ставит PlanetSurfaceRenderer каждый кадр).
            float _WaterTime;
            // Привязка узора к морю, а не к игроку: render-пространство едет
            // вместе с floating origin (якорь = игрок), поэтому узор от
            // positionWS напрямую ползёт за игроком. Шейдер берёт дельту от
            // камеры (малые точные числа), крутит в тело-fixed оси и прибавляет
            // абсолютную фазу игрока — тот же приём, что _TerrainWorldToBody
            // + _TerrainUVPhase у террейна (ставит PlanetSurfaceRenderer).
            float4x4 _WaterWorldToBody;
            float3 _WaterBodyAnchor;

            float4 _WaterDeep;
            float4 _WaterMid;
            float4 _WaterShallow;
            float4 _FoamColor;
            float4 _UnderwaterColor;
            float _SpecularPower;
            float _SpecularIntensity;
            sampler2D _NormalMap0;
            sampler2D _NormalMap1;
            float _UseNormalMap;
            float _NormalStrength;
            float _NormalScale0;
            float _NormalScale1;
            float4 _NormalScroll0;
            float4 _NormalScroll1;
            float _WaveStrength;
            float _WaveScale;
            float _WaveAmplitude;
            float _SparkleStrength;
            float _SparkleScale;
            float _ShadingFadeFloor;
            float _DetailFadeFloor;
            float _RippleStrength;
            float _RippleScale;
            float _RippleSpeed;
            float _FoamCrestStrength;
            float _FoamDepthMeters;
            float _FoamSlopeGain;
            float _ShoreFadeMeters;
            float _AbsorbShallow;
            float _AbsorbDeep;
            float _ShallowAlpha;
            float _SurfStrength;
            float _SurfSpeed;
            float _FresnelStrength;
            float _MinAlpha;
            float _MaxAlpha;
            float _UnderwaterAlpha;
            float _UnderwaterRippleStrength;
            float _UnderwaterCausticStrength;
            float _UnderwaterCausticScale;
            float _UnderwaterCausticSpeed;
            float _UnderwaterRimStrength;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 waterExtra : TEXCOORD2; // x: глубина воды (м, 0 над сушей)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float depthMeters : TEXCOORD2;
                float waveH       : TEXCOORD3;
            };

            // Процедурный value-noise/FBM без текстур: разбивает регулярность
            // волн и пены (два слоя в противоход, как принято для морской
            // пены), микродеталь нормалей вместо «вельвета» синусов.
            float whash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            float wnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(whash(i), whash(i + float2(1.0, 0.0)), u.x),
                    lerp(whash(i + float2(0.0, 1.0)), whash(i + float2(1.0, 1.0)), u.x), u.y);
            }
            float wfbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int k = 0; k < 3; k++)
                {
                    v += a * wnoise(p);
                    p = p * 2.03 + float2(17.3, 9.1);
                    a *= 0.5;
                }
                return v;
            }

            // Тело-fixed координаты узора (м): дельта от камеры в малых числах
            // + абсолютная фаза игрока. Без этого узор считается от
            // render-координат, чей ноль едет с floating origin = игроком,
            // и текстура воды ползёт по морю за игроком.
            float3 WaterBodyFixed(float3 positionWS)
            {
                float3 relBody = mul((float3x3)_WaterWorldToBody, positionWS - _PlanetCameraPos);
                return relBody + _WaterBodyAnchor;
            }

            // Живой океан: 5 бегущих волн (упрощённый Герстнер: только
            // вертикаль — физика плоская через WaterQuery, чанки повёрнуты
            // с телом планеты). Сумма амплитуд = 1 → возврат [-1,1].
            // Пятая октава — короткий чоп (~1.5 м), он даёт "небольшие волны"
            // вблизи, вдали гаснет через fade и fwidth-AA.
            float WaterWaveHeight01(float3 wp, float t, out float3 gradDir)
            {
                float2 p = wp.xz;
                float2 d1 = float2(0.958, 0.287);
                float2 d2 = float2(-0.552, 0.834);
                float2 d3 = float2(0.707, -0.707);
                float2 d4 = float2(-0.196, -0.981);
                float2 d5 = float2(0.831, -0.556);
                float p1 = dot(p, d1) * 1.0 + (t * 1.1);
                float p2 = dot(p, d2) * 1.7 - (t * 1.35);
                float p3 = dot(p, d3) * 2.7 + (t * 1.9);
                float p4 = dot(p, d4) * 4.5 - (t * 2.6);
                float p5 = dot(p, d5) * 7.0 + (t * 3.4);
                float s1 = sin(p1);
                float s2 = sin(p2);
                float s3 = sin(p3);
                float s4 = sin(p4);
                float s5 = sin(p5);
                float h = (0.38 * s1) + (0.26 * s2) + (0.18 * s3) + (0.11 * s4) + (0.07 * s5);
                // Трохоидальное заострение гребней (дух Герстнера: гребни выше
                // и острее, впадины шире и площе; физика остаётся плоской).
                // Производная корректируется аналитически — нормали честные.
                float qShape = 0.25;
                float dhds = 1.0 + (2.0 * qShape * h);
                float2 grad = ((0.38 * 1.0 * d1) * cos(p1)
                    + (0.26 * 1.7 * d2) * cos(p2)
                    + (0.18 * 2.7 * d3) * cos(p3)
                    + (0.11 * 4.5 * d4) * cos(p4)
                    + (0.07 * 7.0 * d5) * cos(p5)) * dhds;
                gradDir = float3(grad.x, 0.0, grad.y);
                return h + (qShape * h * h);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 baseWS = TransformObjectToWorld(input.positionOS);
                // Радиаль из единого центра, а не вершинная нормаль меша:
                // у соседних LOD она разная — отсюда светлая полоса стыка.
                // Фолбэк на меш-нормаль, пока центр не проставлен (нули).
                float3 centerDeltaV = baseWS - _PlanetWaterCenter;
                float3 baseN = dot(centerDeltaV, centerDeltaV) > 1.0
                    ? normalize(centerDeltaV)
                    : TransformObjectToWorldNormal(input.normalOS);
                // Живая волна вдоль радиальной нормали (физика плоская).
                // Fade в ноль вдали: грубый LOD + короткая волна = алиасинг.
                float camDistV = length(_PlanetCameraPos - baseWS);
                float fadeV = exp(-camDistV * 0.00012);
                float3 gdirV;
                // Волна в тело-fixed координатах: иначе геометрия зыби едет
                // за игроком вместе с floating origin (см. WaterBodyFixed).
                float h01 = WaterWaveHeight01(WaterBodyFixed(baseWS) * _WaveScale, _WaterTime, gdirV);
                float h = _WaveAmplitude * h01 * fadeV;
                output.positionWS = baseWS + (baseN * h);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = baseN;
                output.depthMeters = input.waterExtra.x;
                output.waveH = h01;
                return output;
            }

            // Красивое море: бирюзовое мелководье, океанская глубина,
            // солнечная дорожка с глиттером, белая пена прибоя и барашки,
            // зеркало неба по Френелю, дымка горизонта. Лицо — вручную
            // (dot), кромка — по вершинной глубине (robust без depth buffer).
            float4 Frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(_PlanetCameraPos - input.positionWS);
                float3 color = float3(0.0, 0.0, 0.0);
                float alpha = 1.0;
                float cameraDist = length(_PlanetCameraPos - input.positionWS);
                float sun = _TerrainSun;
                float3 sunDir = normalize(_TerrainSunDir);
                // Та же единая радиаль, что в вершинном шейдере.
                float3 centerDeltaF = input.positionWS - _PlanetWaterCenter;
                float3 baseNFace = dot(centerDeltaF, centerDeltaF) > 1.0
                    ? normalize(centerDeltaF)
                    : normalize(input.normalWS);

                // Вода считает свет в «шкале 1»: _TerrainRadianceScale (2.5 в
                // сцене) поднят под альбедо суши ~0.4; на воде (shallow G/B
                // ~0.6, пена ~0.9, прямое зеркало неба) он выжигал всё в
                // молоко. Террейна это не касается — только вода.
                float waterRad = min(_TerrainRadianceScale, 1.0);

                // --- Под водой ------------------------------------------------
                // Камера ниже поверхности: взгляд идёт против нормали.
                // Ветка непрозрачная и БЕЗ screen-door (см. конец шейдера):
                // потолок воды из-под неё обязан быть сплошным, иначе 18%
                // дыр discard дают зерно на весь экран.
                // Вид снизу: окно Снелла + рассеяние солнца сквозь толщу.
                // Прямо вверх — яркое окно в небо с солнечным диском и
                // волновой рябью, к горизонту — глухое зеркало толщи
                // (полное внутреннее отражение). Без этого потолок — плоская
                // заливка и солнце не читается.
                bool isUnderwater = (dot(baseNFace, viewDir) < 0.0);
                if (isUnderwater)
                {
                    float3 upW = baseNFace;
                    // viewDir: поверхность -> камера (вниз). toSurf: камера -> поверхность (вверх).
                    float3 toSurf = -viewDir;
                    float camDistUw = length(_PlanetCameraPos - input.positionWS);
                    // Волновой скос потолка: та же Герстнер-сумма, что над водой,
                    // в тело-fixed координатах — рябь искажает окно Снелла и блик.
                    float3 wposUw = WaterBodyFixed(input.positionWS);
                    float3 gdirUw;
                    float h01Uw = WaterWaveHeight01(wposUw * _WaveScale, _WaterTime, gdirUw);
                    float3 gradWUw = gdirUw * _WaveScale;
                    float3 gradTUw = gradWUw - (upW * dot(gradWUw, upW));
                    float macroKUw = (_WaveAmplitude * 2.0 + _WaveStrength * 1.5);

                    // ИСПРАВЛЕНИЕ "плоского потолка": раньше потолок гнула только
                    // одна макро-волна — окно Снелла выходило гладкой заливкой без
                    // деталей (отсюда жалоба "выглядит ужасно" при взгляде вверх).
                    // Добавлен тот же двухоктавный чоп, что и у поверхности сверху
                    // (см. rgradT в else-ветке), тело-fixed XZ — потолок теперь
                    // реально волнистый, а не идеально гладкая плоскость.
                    float2 xzUw = wposUw.xz;
                    float rfUw1 = max(0.8, _RippleScale * 0.35);
                    float rfUw2 = rfUw1 * 2.13;
                    float rpUw1 = dot(xzUw, float2(0.86, 0.51)) * rfUw1 + (_WaterTime * _RippleSpeed * 0.8);
                    float rpUw2 = dot(xzUw, float2(-0.49, 0.87)) * rfUw2 - (_WaterTime * _RippleSpeed * 1.27);
                    float2 rgradUw = (float2(0.86, 0.51) * (cos(rpUw1) * rfUw1))
                        + (float2(-0.49, 0.87) * (0.5 * cos(rpUw2) * rfUw2));
                    float3 rgradWUw = float3(rgradUw.x, 0.0, rgradUw.y);
                    float3 rgradTUw = rgradWUw - (upW * dot(rgradWUw, upW));

                    float3 nUw = normalize(upW
                        - (gradTUw * (macroKUw * 0.7))
                        - (rgradTUw * (_UnderwaterRippleStrength * 0.12)));
                    float cosUp = saturate(dot(toSurf, nUw));
                    float dayDimUw = 0.15 + (0.85 * saturate(sun));
                    // Окно Снелла (~48.6°, cos ~0.66): внутри — пропуск неба/солнца,
                    // снаружи — тёмное зеркало толщи. Добавлен яркий ободок у самой
                    // критической границы (реальная оптика: пропускание растёт к
                    // грани окна перед полным внутренним отражением) — без него
                    // граница была просто мягким смешением, а не узнаваемым окном.
                    float snell = smoothstep(0.45, 0.75, cosUp);
                    float rim = _UnderwaterRimStrength
                        * smoothstep(0.55, 0.661, cosUp)
                        * (1.0 - smoothstep(0.661, 0.80, cosUp));

                    // Каустики: рваные пятна яркого преломлённого света (как в
                    // бассейне), а не гладкая заливка. Две FBM-выборки в противоход,
                    // заострены степенью — узкие блёстки вместо облака.
                    float2 causticUv1 = xzUw * _UnderwaterCausticScale
                        + float2(_WaterTime * _UnderwaterCausticSpeed * 0.13, -_WaterTime * _UnderwaterCausticSpeed * 0.09);
                    float2 causticUv2 = xzUw * _UnderwaterCausticScale * 1.7
                        - float2(_WaterTime * _UnderwaterCausticSpeed * 0.10, _WaterTime * _UnderwaterCausticSpeed * 0.15);
                    float causticN = wfbm(causticUv1) * 0.55 + wfbm(causticUv2) * 0.45;
                    float caustic = pow(saturate(causticN), 4.0) * _UnderwaterCausticStrength;

                    float3 deepRefl = (_UnderwaterColor.rgb * waterRad * dayDimUw) * (0.6 + 0.4 * (1.0 - cosUp));
                    // Каустики слабо просвечивают и в зеркальной зоне (рассеянный
                    // свет толщи), заметнее — в окне (добавлено ниже к sunThrough).
                    deepRefl += _SunLightColor * waterRad * dayDimUw * caustic * 0.25;

                    float3 skyThrough = (GalilegoSkyAmbient(toSurf) * waterRad * 1.2
                        + (_SunLightColor * waterRad * 0.12)) * dayDimUw;
                    // Солнце сквозь воду: диск + ближнее гало + широкое рассеяние.
                    // Смотрим на солнце через потолок — ярко, в сторону — спад.
                    float sunDotUw = saturate(dot(toSurf, sunDir));
                    float sunDiskUw = pow(sunDotUw, 900.0) * 8.0
                        + pow(sunDotUw, 24.0) * 0.9
                        + pow(sunDotUw, 6.0) * 0.35;
                    float glitterUw = 0.75 + (0.5 * h01Uw);
                    float3 sunThrough = _SunLightColor * (sun * sunDiskUw * glitterUw) * waterRad;
                    // Каустики в окне — самое яркое и читаемое место: солнечная
                    // рябь преломляется в пятна света, как настоящее дно бассейна.
                    sunThrough += _SunLightColor * waterRad * sun * caustic * 0.8;
                    // Поглощение до поверхности: видимость толщи 20-30 м —
                    // в упор потолок яркий и читаемый, вдали тонет в цвет воды.
                    float absorbUw = exp(-camDistUw / 28.0);
                    float3 transmit = (skyThrough + sunThrough) * (0.35 + 0.65 * absorbUw)
                        + (_UnderwaterColor.rgb * waterRad * dayDimUw * 0.25 * (1.0 - absorbUw));
                    color = lerp(deepRefl, transmit, snell);
                    color += _SunLightColor * waterRad * dayDimUw * rim * 0.5;
                    // Живая рябь яркости по волне (гребень светлее впадины).
                    // Диапазон расширен (было ±10%) — иначе потолок между
                    // блёстками всё ещё читался как почти ровная заливка.
                    color *= 0.8 + (0.4 * (h01Uw * 0.5 + 0.5));
                    alpha = _UnderwaterAlpha;
                }
                else
                {

                // --- Волновая нормаль -----------------------------------------
                // shadingFade — макро (градиент + пена) с floor: вода не
                // превращается в мёртвую заливку вдали.
                // detailFade — ВЧ (рябь/глиттер) БЕЗ floor, квадратом: первая
                // уходит в субпиксельный алиасинг, режем раньше базовой волны.
                // Плюс попиксельный AA через fwidth: октава гаснет, когда её
                // длина волны < ~3 пикселей.
                float waveFade = exp(-cameraDist * 0.00012);
                float shadingFade = _ShadingFadeFloor + ((1.0 - _ShadingFadeFloor) * waveFade);
                float detailFade = _DetailFadeFloor + ((1.0 - _DetailFadeFloor) * waveFade * waveFade);
                float t = _WaterTime;
                // Весь узор (волна, чоп, рябь, пена, спаркл) — в тело-fixed
                // координатах: иначе стоит на месте только пока стоит игрок,
                // а при движении ползёт по морю за ним (см. WaterBodyFixed).
                // Совпадает с вертексом — геометрия и нормали согласованы.
                float3 wposF = WaterBodyFixed(input.positionWS);
                float3 wp = wposF * _WaveScale;
                float3 gdirF;
                float h01F = WaterWaveHeight01(wp, t, gdirF);
                float3 centerDeltaN = input.positionWS - _PlanetWaterCenter;
                float3 baseN = dot(centerDeltaN, centerDeltaN) > 1.0
                    ? normalize(centerDeltaN)
                    : normalize(input.normalWS);
                float3 gradW = gdirF * _WaveScale;
                float3 gradT = gradW - (baseN * dot(gradW, baseN));
                // Усиленный отклик: волны читаются и вблизи, и вдали.
                float macroK = (_WaveAmplitude * 2.0 + _WaveStrength * 1.5);
                // Мелкий чоп: две бегущие октавы по world XZ (рад/м, не зависят
                // от _WaveScale). Идёт в основную нормаль → виден в diffuse,
                // fresnel и skyRef, а не только в спеке.
                float2 xzW = wposF.xz;
                float fwW = fwidth(input.positionWS.x) + fwidth(input.positionWS.z);
                float rf1 = max(0.8, _RippleScale * 0.35);
                float rf2 = rf1 * 2.13;
                float rs1 = _RippleSpeed * 0.8;
                float rs2 = _RippleSpeed * 1.27;
                float2 rd1 = float2(0.86, 0.51);
                float2 rd2 = float2(-0.49, 0.87);
                float rp1 = dot(xzW, rd1) * rf1 + (t * rs1);
                float rp2 = dot(xzW, rd2) * rf2 - (t * rs2);
                float aa1 = saturate(1.0 - (fwW * rf1 * 0.35));
                float aa2 = saturate(1.0 - (fwW * rf2 * 0.35));
                float2 rgrad = (rd1 * (cos(rp1) * rf1 * aa1))
                    + (rd2 * (0.5 * cos(rp2) * rf2 * aa2));
                float3 rgradW = float3(rgrad.x, 0.0, rgrad.y);
                float3 rgradT = rgradW - (baseN * dot(rgradW, baseN));
                // Микрорельеф FBM (два слоя в противоход): ломает «вельвет»
                // синусов, даёт живую рябь вблизи. Гаснет вдали (detailFade).
                float2 nUv1 = xzW * 0.55 + float2(t * 0.22, t * 0.13);
                float2 nUv2 = xzW * 1.15 - float2(t * 0.17, -t * 0.21);
                float ne = 0.6;
                float nn0 = wfbm(nUv1) * 0.65 + wfbm(nUv2) * 0.35;
                float nnx = wfbm(nUv1 + float2(ne, 0.0)) * 0.65 + wfbm(nUv2 + float2(ne, 0.0)) * 0.35;
                float nnz = wfbm(nUv1 + float2(0.0, ne)) * 0.65 + wfbm(nUv2 + float2(0.0, ne)) * 0.35;
                float3 ngradW = float3((nnx - nn0) / ne, 0.0, (nnz - nn0) / ne);
                float3 ngradT = ngradW - (baseN * dot(ngradW, baseN));
                float3 n = normalize(baseN
                    - (gradT * (macroK * shadingFade))
                    - (rgradT * (_RippleStrength * 0.12 * detailFade))
                    - (ngradT * (0.12 * detailFade)));
                // Текстурная рябь (HOWTO: две normal map в противоход, вторая
                // мельче). Разворачиваем в касательном базисе вокруг baseN.
                if (_UseNormalMap > 0.5)
                {
                    float2 uvA = xzW * _NormalScale0 + (_NormalScroll0.xy * t);
                    float2 uvB = xzW * _NormalScale1 + (_NormalScroll1.xy * t);
                    float3 txA = tex2D(_NormalMap0, uvA).rgb * 2.0 - 1.0;
                    float3 txB = tex2D(_NormalMap1, uvB).rgb * 2.0 - 1.0;
                    float3 upRef = abs(baseN.y) > 0.9 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
                    float3 tan1 = normalize(cross(upRef, baseN));
                    float3 tan2 = cross(baseN, tan1);
                    float2 tPert = txA.xy + (txB.xy * 0.5);
                    n = normalize(n + ((tan1 * tPert.x + tan2 * tPert.y)
                        * (_NormalStrength * detailFade)));
                }

                // --- База по глубине: бирюза → океан → глубокий navy ---------
                // Поглощение экспоненциальное, быстрое: мелководье первые
                // _AbsorbShallow (~2 м) → mid → глухой deep к _AbsorbDeep
                // (~14 м). Дно сквозь воду читается только у самой кромки,
                // дальше — непрозрачный океан, а не "прокрашенное дно".
                float depth = max(0.0, input.depthMeters);
                float absorbDeep = 1.0 - exp(-depth / max(1.0, _AbsorbDeep));
                float deepT = pow(saturate(absorbDeep), 0.6);
                float midT = smoothstep(0.0, 0.35, deepT);
                float deepW = smoothstep(0.30, 1.0, deepT);
                float3 waterBase = lerp(_WaterShallow.rgb, _WaterMid.rgb, midT);
                waterBase = lerp(waterBase, _WaterDeep.rgb, deepW);
                // Светящаяся кромка: первые метры чуть ярче shallow.
                float shallowKick = 1.0 - exp(-depth / max(0.5, _AbsorbShallow));
                waterBase = lerp(_WaterShallow.rgb * 1.10, waterBase, saturate(shallowKick));

                // Свет воды: рельефный член здесь даёт черноту (солнце у
                // горизонта -> ndl=0, небо 0.1). Вода светится толщей:
                // wrap-диффуз + усиленный полусферический скайлайт, иначе
                // взгляд строго вниз — плоская чёрная заливка.
                float ndl = saturate(dot(n, sunDir));
                float ndlWrap = saturate((dot(n, sunDir) + 0.6) / 1.6);
                // HOWTO-вода светится отражением, а не ambient-пересветом:
                // было *3.0 — мелководье выжигалось в молочно-белое.
                float3 waterSky = GalilegoSkyAmbient(n) * waterRad * 1.15;
                float3 lightTerm = float3(_NightAmbient, _NightAmbient, _NightAmbient)
                    + waterSky
                    + (_SunLightColor * (sun * (ndl * 0.35 + ndlWrap * 0.65)) * waterRad);

                float cosT = saturate(dot(n, viewDir));
                float fresnelPow = pow(1.0 - cosT, 5.0);
                // Шлик, F0 воды 0.02-0.04 как в природе (Crest/OceanShader):
                // вдаль — зеркало, вниз — прозрачная толща, а не молоко.
                float fresnelF = 0.03 + (0.97 * fresnelPow);

                // Солнечная дорожка: широкий блик + рассыпчатый глиттер.
                // Отдельная ВЧ-октава только для specular: дробит блин на
                // блёстки на закате; diffuse/foam её не видят.
                float sparkleFade = detailFade;
                float2 sd1 = float2(0.66, -0.75);
                float sf1 = max(3.0, _SparkleScale * 2.2);
                float sp1 = dot(xzW, sd1) * sf1 + (t * 3.1);
                float saa1 = saturate(1.0 - (fwW * sf1 * 0.35));
                float2 sgrad = sd1 * (cos(sp1) * sf1 * saa1);
                float3 sgradW = float3(sgrad.x, 0.0, sgrad.y);
                float3 sgradT = sgradW - (baseN * dot(sgradW, baseN));
                float3 nSpec = normalize(n
                    - (sgradT * (_SparkleStrength * 0.06 * sparkleFade)));
                float3 halfVec = normalize(sunDir + viewDir);
                float spec = pow(saturate(dot(nSpec, halfVec)), max(1.0, _SpecularPower)) * _SpecularIntensity;
                float glitter = 0.5 + (0.5 * sin((sp1 * 1.7) - (t * 2.3)));
                spec *= (0.2 + (0.7 * glitter * saturate(sparkleFade + 0.25)));

                color = (waterBase * lightTerm)
                    + (spec * sun * _SunLightColor * waterRad);

                // Подсветка толщи волны (SSS-приближение как в Crest/NorthStar):
                // смотря сквозь гребень на солнце, волна светится бирюзой
                // (прямое рассеяние), впадины остаются глубокими. Красный
                // гаснет первым — только teal-оттенок. Дальше мелководья нет.
                float crest01 = saturate(h01F * 0.5 + 0.5);
                float sunView = saturate(dot(viewDir, sunDir));
                float sssForward = pow(sunView, 3.0) * pow(crest01, 2.0);
                float sssThick = (1.0 - crest01) * 0.35;
                float3 sssColor = float3(0.05, 0.38, 0.40);
                color += sssColor * (sssThick + sssForward * 0.9)
                    * saturate(sun * ndl + 0.25) * (1.0 - deepW) * waterRad;

                // Зеркало неба по Шлику (F0 воды 0.06 для красивого моря):
                // вдаль море светлеет к горизонту, а не остаётся тёмной заливкой.
                // Неба подмешиваем щедро + тёплый солнечный оттенок — море
                // зеркалит небо, как настоящий океан в ясный день.
                float3 reflectDir = reflect(-viewDir, n);
                float3 skyRef = GalilegoSkyAmbient(reflectDir) * waterRad * 1.0
                    + (_SunLightColor * waterRad * 0.06);
                // Широкая солнечная дорожка на воде (шеен к солнцу поверх
                // зеркала; сам глиттер даёт spec ниже).
                float sunPath = pow(saturate(dot(reflectDir, sunDir)), 10.0);
                skyRef += _SunLightColor * (sunPath * 0.05 * waterRad);
                color = lerp(color, skyRef, saturate(fresnelF * _FresnelStrength));
                // Дальняя дымка воды: горизонт уходит в небо.
                // Усилена для морских просторов — океаны до горизонта.
                float horizonT = (1.0 - waveFade) * saturate(fresnelPow + 0.35);
                color = lerp(color, skyRef, horizonT * 0.65 * _FresnelStrength);

                // --- Пена у берега -------------------------------------------
                float slopeW = fwidth(input.depthMeters);
                float bandW = max(max(0.5, _FoamDepthMeters), slopeW * max(0.0, _FoamSlopeGain));
                float foamBand = 1.0 - smoothstep(0.0, bandW, depth);
                float foamFreqBoost = 1.0 + (2.0 * exp(-cameraDist * 0.01));
                // Пена FBM двумя слоями в противоход (приём из процедурных
                // океанов): рвёт регулярные полосы, даёт кружево. Слабый
                // глубинный член оставляет намёк прибойных линий у берега.
                // HOWTO: мелкое кружево у уреза — частоты подняты (было
                // 0.35/0.8 — пятна по 3 м заливали весь шельф молоком).
                float foamN1 = wfbm(xzW * 1.4 + float2(t * 0.15, -t * 0.11));
                float foamN2 = wfbm(xzW * 2.8 - float2(t * 0.12, t * 0.16));
                float foamNoise = saturate(foamN1 * 0.6 + foamN2 * 0.4 + 0.15
                    + (0.1 * sin((depth * 1.7 * foamFreqBoost) - (t * 2.2))));
                // Кружево, а не одеяло: шум через порог — рваные пятна пены,
                // между ними чистая бирюза. Было foamBand*foamNoise — на
                // пологом шельфе молочная заливка во весь экран.
                // Порог поднят (было 0.66-0.90): пена ~10-15% площади полосы.
                // 0.70-0.92 — баланс: кружево видно у уреза, заливки нет.
                float foamLace = smoothstep(0.70, 0.92, foamNoise);
                float foamBase = saturate(foamBand * foamLace * shadingFade * 0.9 + (foamBand * foamBand * 0.03)
                    + (foamBand * crest01 * _FoamCrestStrength * shadingFade * 0.5));
                // Бегущий прибой: светлые линии набегают на берег. Полоса уже
                // и ближе к урезу, чем раньше — иначе весь шельф белеет.
                float surfNoise = sin(dot(xzW, float2(0.35, 0.42)) + (t * 0.9))
                    + (0.5 * sin(dot(xzW, float2(-0.31, 0.27)) - (t * 0.7)));
                float surfPhase = (depth * 2.1) - (t * _SurfSpeed) + (surfNoise * 1.5);
                float surfLines = smoothstep(0.78, 0.98, (sin(surfPhase) * 0.5 + 0.5));
                float surfAtten = 1.0 - smoothstep(0.0, max(1.0, _FoamDepthMeters * 0.9), depth);
                float surf = surfLines * surfAtten * _SurfStrength * shadingFade * 0.5;
                // Барашки в открытом море: пена только где гребень высок И шум
                // утверждён (порог в духе Dupuy whitecaps) — редкие рваные
                // пятна вместо молочной заливки. Вдали гаснет.
                float capN = wfbm(xzW * 2.0 + float2(-t * 0.2, t * 0.14));
                float whitecaps = smoothstep(1.0, 1.15, crest01 * 0.8 + capN * 0.4)
                    * _FoamCrestStrength * 0.7 * shadingFade;
                float foam = saturate(foamBase + surf + whitecaps);

                // --- Мягкий берег по вершинной глубине (без depth buffer) ----
                // Красивое море обязано РИСОВАТЬСЯ: чтение depth buffer
                // (LoadCameraDepth) в transparent-проходе HDRP на части
                // конфигураций даёт пустой кадр — вся вода пропадает и видно
                // только тёмное дно. Поэтому кромка — по вершинной глубине
                // чанка (waterExtra.x): robust на любом железе и LOD.
                // Пена у уреза и мелководный цвет — из той же глубины.
                float shoreSoft = saturate(depth / max(0.25, _ShoreFadeMeters));
                {
                    // Пена уреза — то же кружево в полосе foamBand (1-2 м),
                    // а не заливка всего, что мельче ShoreFade (8 м).
                    float shoreFoam = foamBand * smoothstep(0.70, 0.92, foamNoise);
                    foam = max(foam, shoreFoam);
                    color = lerp(_WaterShallow.rgb * lightTerm * 1.05, color, smoothstep(0.0, 0.7, shoreSoft));
                }
                // Пена — альбедо около белого без дополнительного разгона:
                // разгон ×1.9 под физическим солнцем выжигал весь шельф в молоко.
                color = lerp(color, _FoamColor.rgb * lightTerm, foam * 0.75);

                // --- Альфа: глубже ~8 м вода полностью непрозрачна ------------
                // Мелководье полупрозрачное (дно просвечивает), глубина и пена —
                // почти непрозрачны. Блендинга нет (AlphaTest-очередь):
                // прозрачность стохастическая (screen-door через clip ниже) —
                // отброшенные пиксели показывают дно под водой.
                // Глубина дополнительно читается цветом (waterBase).
                float shallowT = smoothstep(0.0, 8.0, depth);
                // Полоса прозрачности УЖЕ цветовой (8 м): зерно screen-door держим
                // только у самого уреза (0-2 м), иначе при ходьбе пол-экрана
                // зернистой ряби ползёт вместе с камерой.
                float alphaT = smoothstep(0.0, 2.0, depth);
                float alphaBase = lerp(_MinAlpha, _MaxAlpha, max(alphaT, fresnelPow));
                float alphaShore = max(foam * 0.98, _ShallowAlpha * (1.0 - (alphaT * 0.35)));
                alpha = clamp(max(alphaBase, alphaShore), 0.0, 1.0);
                }
                // Screen-door 1px IGN: мелкое зерно вместо блендинга (Transparent
                // наш пас не рисует). Без квантования по мировым ячейкам.
                // Только НАД водой: из-под воды потолок сплошной (alpha=1,
                // discard запрещён), иначе зерно на весь экран при нырянии.
                if (!isUnderwater)
                {
                    float ign = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));
                    clip(alpha - ign);
                }
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
