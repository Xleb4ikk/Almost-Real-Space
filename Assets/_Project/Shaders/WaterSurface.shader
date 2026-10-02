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
        _MinAlpha("Min Alpha", Range(0.0, 1.0)) = 0.93
        _MaxAlpha("Max Alpha", Range(0.0, 1.0)) = 1.0
        _ShallowAlphaMeters("Shallow Alpha Band (m)", Float) = 0.35
        _UnderwaterAlpha("Underwater Alpha", Range(0.0, 1.0)) = 1.0
        _UnderwaterRimStrength("Underwater Window Rim", Float) = 0.5
        _UnderwaterSunStrength("Underwater Sun Strength", Range(0.0, 1.0)) = 0.12
        // Наклон мелкой ряби на потолке. Было 0.12 (при длине волны 4.5 м это
        // наклон около 8°, а преломление отклоняет луч на (n−1)·slope, то
        // есть на 2.6°, и небо в окне почти не искажалось). Диапазон
        // 0.15-0.30 даёт видимую рябь на 2-3 м.
        // Коэффициент именно (n−1) = 0.33, а НЕ (1−1/n) = 0.25: луч уходит
        // в воздух под asin(n·sinβ), то есть отклоняется ОТ ВЕРТИКАЛИ на
        // (n−1)·β. Проверено тестом Test139_SnellWindowReference.
        _UnderwaterRippleSlope("Underwater Ripple Slope", Range(0.0, 0.6)) = 0.10
        // Множитель яркости окна относительно физического неба. Подобран по
        // замеру: при 1.0 окно читалось в ~2 раза ярче воды после ACES, то
        // есть как слабый блик, а не как небо. 3.0 даёт ~6-8 раз в линейных
        // и ~200/255 в кадре при сохранённой детали градиента.
        _UnderwaterWindowGain("Underwater Window Gain", Float) = 3.0
        // Сколько света отражает зеркало (полное внутреннее отражение): это
        // цвет ТОЛЩИ воды, а не отражение неба.
        _UnderwaterMirrorGain("Underwater Mirror Gain", Float) = 1.5
        // Отладочный вывод окна Снелла (проверяется снимком PNG):
        // 0 = обычный вид, 1 = Френель F в градациях серого,
        // 2 = маска окна (1 — луч проходит, 0 — полное внутреннее отражение),
        // 3 = log2(яркость), 0.5 — яркость 1.0 (для гистограммы кадра).
        _UnderwaterDebugMode("Underwater Debug Mode", Range(0.0, 3.0)) = 0
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
             // DEBUG-MINIMAL3: без shadow-вариантов и без GalilegoLighting
             // (HDShadow). Если вода появится — виноват один из них. Небо для
             // окна Снелла приходит из GalilegoSkyRadiance.hlsl: там и
             // объявления глобалов, и сам расчёт (воде не нужен HDShadow).
             #include "GalilegoSkyRadiance.hlsl"

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
            float _ShallowAlphaMeters;
            float _UnderwaterAlpha;
            float _UnderwaterCameraDepth;
            float _UnderwaterRimStrength;
            float _UnderwaterSunStrength;
            float _UnderwaterRippleSlope;
            float _UnderwaterWindowGain;
            float _UnderwaterMirrorGain;
            float _UnderwaterDebugMode;

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
            // Хеш решётки на ЦЕЛОМ PCG, без sin. Прежний frac(sin(dot)*43758)
                // вырождался в полосы: аргумент dot достигал ~1e8, где sin
                // в float32 уже не имеет точности. PCG-подстановка целым по
                // битам не теряет точность никогда.
                uint whashInt(uint x)
                {
                    x = x * 747796405u + 2891336453u;
                    uint w = ((x >> ((x >> 28u) + 4u)) ^ x) * 277803737u;
                    return (w >> 22u) ^ w;
                }
                float whash(float2 p)
                {
                    // Целая часть в uv лежит в пределах +-P/размер ячейки,
                    // то есть влезает в int32 с запасом; float->int здесь
                    // безопасен именно из-за того, что целая часть uv держится в пределах
                    // периода.
                    int2 c = (int2)floor(p);
                    uint h = whashInt((uint)(c.x * 73856093) ^ (uint)(c.y * 19349663));
                    return (h & 0x00FFFFFFu) * (1.0 / 16777216.0);
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

            // Общий масштаб периода узора: 2*pi/P при P = 1024 м, и векторы ряби,
            // заданные ЦЕЛЫМИ парами. Фаза в uv+period сдвигается на
            // кратное 2*pi, поэтому узор не «прыгает» на границе периода.
            static const float kBaseUw = 0.006135923151542565;
            static const float2 kRip1 = float2(883.0, 522.0);
            // |K| = 2556 -> 15.7 рад/м (~0.4 м). Прежние (-588, 1043) давали 7.3 рад/м, а не заявленные 15.7.
            static const float2 kRip2 = float2(-1254.0, 2227.0);

            // Тело-fixed координаты узора (м): дельта от камеры в малых числах
            // + абсолютная фаза игрока. Без этого узор считается от
            // render-координат, чей ноль едет с floating origin = игроком,
            // и текстура воды ползёт по морю за игроком.
            // Якорь приходит уже уменьшенным по модулю 51 200 м (см. UpdateWaterAnchor).
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
            float WaterWaveHeight01(float2 uv, float t, out float2 gradTangent)
            {
                float2 p = uv;
                // Волновые векторы - ЦЕЛЫЕ векторы K, фаза = dot(p,K)*kBase.
                // Тогда узор периодичен по P = 1024 м: сдвиг на P даёт
                // dot += |K|, то есть фаза += 2*pi*|K|. Направление и
                // частота берутся разложением K, а не задаются отдельно -
                // иначе периодичность ломается на нецелых произведениях.
                const float kBase = 0.006135923151542565;  // 2*pi/1024
                const float2 K1 = float2(156.0, 47.0);
                const float2 K2 = float2(-153.0, 231.0);
                const float2 K3 = float2(311.0, -311.0);
                const float2 K4 = float2(-144.0, -719.0);
                const float2 K5 = float2(948.0, -635.0);
                float L1 = length(K1); float L2 = length(K2);
                float L3 = length(K3); float L4 = length(K4);
                float L5 = length(K5);
                float2 d1 = K1 / L1; float2 d2 = K2 / L2;
                float2 d3 = K3 / L3; float2 d4 = K4 / L4;
                float2 d5 = K5 / L5;
                float f1 = L1 * kBase; float f2 = L2 * kBase;
                float f3 = L3 * kBase; float f4 = L4 * kBase;
                float f5 = L5 * kBase;
                float p1 = dot(p, K1) * kBase + (t * 1.1);
                float p2 = dot(p, K2) * kBase - (t * 1.35);
                float p3 = dot(p, K3) * kBase + (t * 1.9);
                float p4 = dot(p, K4) * kBase - (t * 2.6);
                float p5 = dot(p, K5) * kBase + (t * 3.4);
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
                float2 grad = ((0.38 * f1 * d1) * cos(p1)
                    + (0.26 * f2 * d2) * cos(p2)
                    + (0.18 * f3 * d3) * cos(p3)
                    + (0.11 * f4 * d4) * cos(p4)
                    + (0.07 * f5 * d5) * cos(p5)) * dhds;
                gradTangent = grad;
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
                // Узор в .xz тело-fixed пространства. Шаг 2 (касательные UV с базисом
                // east/north от CPU) откачен: он дал фиолетовую заливку и
                // плоский синий фон вместо поверхности. Причина пунктов 1 и 3
                // лечится модулем якоря и мягким пределом СКО; пункт 2
                // (растяжение по меридиану) остаётся нерешённым.
                float2 patternUv = WaterBodyFixed(baseWS).xz;
                float2 gradV;
                float h01 = WaterWaveHeight01(patternUv * _WaveScale, _WaterTime, gradV);
                float3 gdirV = float3(gradV.x, 0.0, gradV.y);
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
                 float cloudShadow = SampleCloudShadow(input.positionWS);
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
                // ОКНО СНЕЛЛА ПО ФИЗИКЕ. Луч вверх преломляется нормалью волны
                // (вода 1.33 → воздух 1.0). За критическим углом 48.75° луч не
                // выходит вовсе: там полное внутреннее отражение и глаз видит
                // толщу. Граница окна и её ободок берутся из той же формулы
                // Френеля, а не из smoothstep по cosUp — у настоящего окна
                // физическая граница, и она читается как круглая кромка.
                //
                // Чего здесь принципиально нет:
                //  - своего exp(-dist/28): поглощение делает проход
                //    UnderwaterViewFog по спектральной σ. Своё затухание
                //    поверх спектрального давало две модели сразу и мутный
                //    потолок уже на 2-3 м;
                //  - каустик на потолке. Каустика — это свет, СОБРАННЫЙ на
                //    освещённой сверху поверхности; с внутренней стороны её
                //    не видно, её видно на дне (UnderwaterCaustics.shader).
                //    На зеркале — низкочастотная модуляция лучами света.
                // Под водой мы тогда и только тогда, когда ПОВЕРХНОСТЬ НАД
                // КАМЕРОЙ: dot(камера→поверхность, вверх) > 0. Это геометрический
                // признак, и он не может разойтись с картинкой ни при каких
                // настройках мира. Внимание к знаку: viewDir здесь — поверхность
                // → камера, поэтому «вверх» это -viewDir.
                //
                // Зачем страховка к глобалу _UnderwaterCameraDepth: глобал
                // ставится из запроса «камера ниже уровня моря», и он может
                // отставать на кадр или обнулиться при смене тела — тогда вода
                // рисовалась бы ВЕРХНЕЙ веткой: пена прибоя и screen-door
                // dither, снятые снизу (именно это и было «ужасной поверхностью
                // из-под воды»). Порог 0.25, а не 0, умышленно: при взгляде на
                // горизонт dot ≈ 0 и признак неустойчив, а 0.25 — это взгляд
                // строго вверх (не дальше 14° от вертикали), где сомнений быть
                // не может.
                float viewUp = dot(-viewDir, baseNFace);
                bool isUnderwater = (_UnderwaterCameraDepth > 0.0) || (viewUp > 0.25);
                if (isUnderwater)
                {
                    float3 upW = baseNFace;
                    // viewDir: поверхность -> камера (вниз). toSurf: камера -> поверхность (вверх).
                    float3 toSurf = -viewDir;

                    // Солнце над ЛОКАЛЬНЫМ горизонтом — единственный честный
                    // признак времени суток, доступный шейдеру. Раньше здесь
                    // стоял _TerrainSun, а это константа сцены
                    // (SkyEnvironment.TerrainSunIntensity), от солнца не
                    // зависящая: dayDim = 1 всегда, и ночью в окне оставался
                    // солнечный диск. Полка ±2° — как у реального заката.
                    float sunAboveHorizon = dot(sunDir, _SkyUp);
                    float sunFactor = smoothstep(-0.035, 0.06, sunAboveHorizon);

                    // Тот же Герстнер и та же рябь, что над водой, и в тех же
                    // касательных UV: потолок и поверхность не расходятся.
                    float3 wposUw = WaterBodyFixed(input.positionWS);
                    float2 patternUw = wposUw.xz;
                    float2 gradUw;
                    float h01Uw = WaterWaveHeight01(patternUw * _WaveScale, _WaterTime, gradUw);
                    float3 gradWUw = mul(float3(gradUw.x, 0.0, gradUw.y) * _WaveScale, (float3x3)_WaterWorldToBody);
                    float3 gradTUw = gradWUw - (upW * dot(gradWUw, upW));
                    // Макро-наклон. У реального моря СКО наклона 10-15°,
                    // то есть ~0.2 рад; прежнее (WaveAmplitude*2 +
                    // WaveStrength*1.5)*0.7 давало до 41°, и это отдельный
                    // дефект: макро-волна гнёт преломлённый луч сильнее,
                    // чем весь микро-узор вместе взятый.
                    float macroKUw = (_WaveAmplitude * 2.0 + _WaveStrength * 1.5);

                    // Мелкая рябь двумя бегущими октавами. Наклон задан УГЛОМ
                    // (_UnderwaterRippleSlope, рад), а не множителем градиента:
                    // иначе «усиление наклона» означает разное при разной
                    // длине волны. Частоты 6.3 и 15.7 рад/м — это рябь ~1 м и
                    // ~0.4 м, то есть те самые 10-50 см, которые читаются с
                    // 2-3 м (было 1.4 рад/м, то есть 4.5 м, и наклоны ~3°).
                    // Предел AA берётся по следу самого узора, а не по positionWS.x/z:
                    // на наклонной поверхности производные рендер-координат
                    // могут сойтись почти в ноль, и предел переставал
                    // ограничивать что-либо.
                    float fwUw = max(fwidth(patternUw.x), fwidth(patternUw.y));

                    // Векторы ряби объявлены выше (общий масштаб периода), здесь только длины.
                    float rfUw1 = length(kRip1) * kBaseUw;
                    float rfUw2 = length(kRip2) * kBaseUw;

                    // Предел наклона по следу пикселя в осях узора: наклоны
                    // круче, чем 1/(2*fw), не отдаются в одном пикселе.
                    float slopeLimitUw = 0.5 / max(fwUw, 1e-4);
                    float slopeUw = min(_UnderwaterRippleSlope, slopeLimitUw);
                    float aaUw1 = saturate(1.0 - (fwUw * rfUw1 * 0.35));
                    float aaUw2 = saturate(1.0 - (fwUw * rfUw2 * 0.35));
                    float2 ripDir1 = normalize(kRip1);
                    float2 ripDir2 = normalize(kRip2);
                    float rpUw1 = dot(patternUw, ripDir1) * rfUw1 + (_WaterTime * _RippleSpeed * 2.0);
                    float rpUw2 = dot(patternUw, ripDir2) * rfUw2 - (_WaterTime * _RippleSpeed * 3.1);

                    // Амплитуда октав задаётся СРАЗУ в радианах, а сумма потом
                    // мягко ограничивается. Раньше сумма нормировалась к
                    // постоянному модулю: у суммы косинусов есть нули вектора,
                    // возле них направление скачет на 180 градусов, и наклон
                    // становился ОДИНАКОВЫМ в каждом пикселе - это и давало
                    // жёсткие границы полос и скачки окно/зеркало. Нормировки
                    // здесь нет намеренно.
                    // Амплитуды 0.75/0.53/0.38 дают СКО суммы ~1.0*slope при
                    // случайных фазах, то есть параметр означает средний
                    // наклон, а не максимальный.
                    float2 rgradUw = (ripDir1 * (0.75 * slopeUw * cos(rpUw1) * aaUw1))
                        + (ripDir2 * (0.53 * slopeUw * cos(rpUw2) * aaUw2));

                    // Третья составляющая — FBM, а не синус. Две синусоиды
                    // дали идеально правильные концентрические кольца (на
                    // снимке читалось как отпечаток пальца): настоящая рябь
                    // нерегулярна, и именно ломаная структура прячет диск
                    // солнца в устойчивую россыпь, а не в мусор.
// Третья составляющая - FBM, а не синус: две синусоиды давали
                    // идеально правильные концентрические кольца. Масштаб UV
                    // тоже кратен 2*pi/P, иначе FBM не вписывается в период.
                    const float fbmScaleUw = kBaseUw * 440.0;  // 2.6998 рад/м
                    float2 microUv = (patternUw * fbmScaleUw)
                        + float2(_WaterTime * 0.07, -_WaterTime * 0.05);
                    float mEps = 0.35;
                    float m0 = wfbm(microUv);
                    float mx = wfbm(microUv + float2(mEps, 0.0));
                    float mz = wfbm(microUv + float2(0.0, mEps));
                    float2 mgrad = float2((mx - m0), (mz - m0)) / mEps;
                    // Направление FBM нормируется (у суммы октав нули не
                    // используются как мера амплитуды), но её амплитуда
                    // входит в общий СКО и тоже в радианах.
                    rgradUw += (mgrad / max(length(mgrad), 1e-4)) * (0.38 * slopeUw * aaUw2);

                    // Мягкий предел суммарного наклона. Модуль НЕ приводится к
                    // заданному значению - он только ограничивается сверху, и
                    // в нулях суммы наклон спокойно сходит к нулю, а не
                    // переворачивается на 180 градусов.
                    float rlenUw = length(rgradUw);
                    float rcapUw = slopeUw * 1.8;
                    rgradUw *= min(1.0, rcapUw / max(rlenUw, 1e-4));
                    float3 rgradTUw = mul(float3(rgradUw.x, 0.0, rgradUw.y), (float3x3)_WaterWorldToBody);
                    rgradTUw -= upW * dot(rgradTUw, upW);

                    // Макро-наклон ограничен мягко (tanh) примерно 0.25 рад (14°): у реального моря СКО
                    // наклона 10-15°. Прежнее значение доходило до ~0.72 рад (41°), из-за чего луч вверх
                    // почти везде попадал в полное внутреннее отражение (потолок = ровная заливка цветом толщи).
                    const float kMacroSlopeUw = 0.25;
                    float3 macroTiltUw = gradTUw * (macroKUw * 0.7);
                    float macroLenUw = length(macroTiltUw);
                    macroTiltUw *= (kMacroSlopeUw * tanh(macroLenUw / kMacroSlopeUw)) / max(macroLenUw, 1e-5);
                    float3 nUw = normalize(upW - macroTiltUw - rgradTUw);

                    // Запечённые normal map в подводной ветке. Над водой они
                    // дают микрорельеф поверх шейдерных волн; из-под воды они и
                    // есть та самая рябь 10-50 см, которой не хватает
                    // процедурным октавам, плюс ломают регулярность синусов.
                    //
                    // Вклад ограничен ТЕМ ЖЕ пределом наклона, что и октавы
                    // выше: карта с плиткой 0.24 м на касательном луче даёт
                    // наклон в десятки градусов на пиксель, и без ограничения
                    // окно рассыпается в пятна.
                    if (_UseNormalMap > 0.5)
                    {
                        // Плитки - делители P (степени двойки): 0.625 м и 0.25 м.
                        // Тогда uv + P даёт целое число плиток и сдвиг
                        // якоря на период не меняет картинку.
                        float2 uvN0 = (patternUw * 1.6) + (_NormalScroll0.xy * _WaterTime);
                        float2 uvN1 = (patternUw * 4.0) + (_NormalScroll1.xy * _WaterTime);
                        float3 tx0 = tex2D(_NormalMap0, uvN0).rgb * 2.0 - 1.0;
                        float3 tx1 = tex2D(_NormalMap1, uvN1).rgb * 2.0 - 1.0;
                        // Опорная ось от RENDER-пространства: эталонный код
                        // брал её от мирового Y, то есть у полюсов планеты
                        // (где upW ≈ ±Y) базис вырождался. _SkyUp здесь нельзя:
                        // это simulation-пространство, а upW — render.
                        float3 refUw = abs(upW.z) < 0.9 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
                        float3 tanUw = normalize(cross(refUw, upW));
                        float3 binUw = cross(upW, tanUw);
                        float2 tPert = tx0.xy + (tx1.xy * 0.5);
                        nUw = normalize(nUw + ((tanUw * tPert.x + binUw * tPert.y)
                            * (_NormalStrength * slopeUw * 1.5)));
                    }
                    // --- Окно Снелла: Френель воды -> воздух --------------------
                    // Полная формула диэлектрика, а не художественный
                    // smoothstep: у воды F0 = ((1.33-1)/(1.33+1))^2 = 0.020, и
                    // к критическому углу F идёт к 1. Отсюда сами собой и
                    // яркий ободок окна, и зеркало за ним — отдельных кривых
                    // больше не нужно.
                    float cosI = saturate(dot(toSurf, nUw));
                    float sinT = 1.33 * sqrt(saturate(1.0 - (cosI * cosI)));
                    bool transmitted = (sinT < 1.0);
                    float fresnel = 1.0;
                    float3 skyDir = upW;
                    if (transmitted)
                    {
                        float cosT = sqrt(1.0 - (sinT * sinT));
                        float rs = ((1.33 * cosI) - cosT) / ((1.33 * cosI) + cosT);
                        float rp = ((1.33 * cosT) - cosI) / ((1.33 * cosT) + cosI);
                        fresnel = 0.5 * ((rs * rs) + (rp * rp));
                        // Луч наружу: refract уже отдаёт единичный вектор.
                        skyDir = refract(toSurf, -nUw, 1.33);
                    }

                    // Ширина солнечного блика и звёзд — из разброса нормали по
                    // пикселю, то есть из того же сигнала, которым гасится
                    // алиасинг октав. Диск не может быть резче настоящего
                    // (GalilegoSunDisc держит нижнюю границу), поэтому на штиль
                    // он круглый, а на волне распадается на устойчивую
                    // россыпь. Строгий конус при наклонах ±8° мерцал бы.
                    float3 ddxN = ddx(nUw);
                    float3 ddyN = ddy(nUw);
                    float normalSigma = min(sqrt(dot(ddxN, ddxN) + dot(ddyN, ddyN)), 0.35);

                    float3 skySeen = 0.0;
                    if (transmitted && fresnel < 0.999)
                    {
                        skySeen = GalilegoSkyRadiance(skyDir, sunDir, normalSigma, sunFactor,
                                _CldBodyCenterWS, _CldBottomRadius, _CldTopRadius)
                            * (_UnderwaterWindowGain * waterRad);
                    }

                    // Зеркало: отражение ТОЛЩИ, а не неба. Физически за
                    // критическим углом луч не выходит из воды, и глаз видит то,
                    // что ниже: рассеянный свет толщи, темнеющий с ростом
                    // угла (меньше света от зенита). Полосы света — низкочастотные
                    // (15-60 м), потому что отдельных светлых точек на зеркале
                    // нет: каустики собираются на освещённой сверху поверхности,
                    // а не на потолке.
                    float3 mirrorDir = reflect(toSurf, nUw);
                    float downness = saturate(-dot(mirrorDir, upW));
                    float lightLevel = (_SkyAmbient * 2.2)
                        + (saturate(sunAboveHorizon) * _UnderwaterSunStrength * 6.0);
                    float3 mirror = _UnderwaterColor.rgb * (waterRad * lightLevel * _UnderwaterMirrorGain)
                        * lerp(1.0, 0.25, downness);
                    float2 bandUv = (patternUw * (kBaseUw * 5.0))
                        + float2(_WaterTime * 0.012, -_WaterTime * 0.009);
                    float band = (wfbm(bandUv) * 0.6) + (wfbm((bandUv * 2.3) + 11.0) * 0.4);
                    mirror *= 0.75 + (0.5 * band);

                    color = lerp(skySeen, mirror, fresnel);

                    // Ободок окна — тот же Френель: там, где отражение ещё не
                    // полное, но уже сильное. Отдельного smoothstep больше нет.
                    color += skySeen * (_UnderwaterRimStrength * fresnel * (1.0 - fresnel) * 2.0);

                    // Отладка окна: снимок PNG сверяется с эталонным расчётом.
                    if (_UnderwaterDebugMode > 0.5)
                    {
                        if (_UnderwaterDebugMode < 1.5) { return float4(fresnel.xxx, 1.0); }
                        if (_UnderwaterDebugMode < 2.5) { return float4(transmitted ? 1.0 : 0.0, 1.0, 1.0, 1.0); }
                        float lum = dot(color, float3(0.2126, 0.7152, 0.0722));
                        return float4((log2(max(lum, 1e-4)) * 0.1) + 0.5, 0.0, 0.0, 1.0);
                    }

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
                // Весь узор (волна, чоп, рябь, пена, спаркл) — в касательных UV:
                // иначе он стоит на месте только пока стоит игрок, а при
                // движении ползёт по морю за ним. Тот же UV, что в вертине
                // и в подводной ветке, поэтому геометрия, нормали и потолок
                // согласованы (см. WaterBodyFixed).
                // Узор в .xz тело-fixed пространства. Шаг 2 (касательные UV с базисом
                // east/north от CPU) откачен: он дал фиолетовую заливку и
                // плоский синий фон вместо поверхности.
                float2 patternUv = WaterBodyFixed(input.positionWS).xz;
                float2 gradF;
                float h01F = WaterWaveHeight01(patternUv * _WaveScale, t, gradF);
                float3 gdirF = float3(gradF.x, 0.0, gradF.y);
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
                // Предел AA — по следу узора в его собственных осях.
                float fwW = max(fwidth(patternUv.x), fwidth(patternUv.y));
                // Частоты чопа округлены до кратных 2*pi/P, иначе узор не
                // вписывается в период и «прыгает» при обнулении якоря.
                float rf1 = max(kBaseUw * 131.0, _RippleScale * 0.35);
                float rf2 = rf1 * 2.0;
                float rs1 = _RippleSpeed * 0.8;
                float rs2 = _RippleSpeed * 1.27;
                float2 rd1 = normalize(kRip1);
                float2 rd2 = normalize(kRip2);
                float rp1 = dot(patternUv, rd1) * rf1 + (t * rs1);
                float rp2 = dot(patternUv, rd2) * rf2 - (t * rs2);
                float aa1 = saturate(1.0 - (fwW * rf1 * 0.35));
                float aa2 = saturate(1.0 - (fwW * rf2 * 0.35));
                float2 rgrad = (rd1 * (cos(rp1) * rf1 * aa1))
                    + (rd2 * (0.5 * cos(rp2) * rf2 * aa2));
                float3 rgradW = float3(rgrad.x, 0.0, rgrad.y);
                float3 rgradT = rgradW - (baseN * dot(rgradW, baseN));
                // Микрорельеф FBM (два слоя в противоход): ломает «вельвет»
                // синусов, даёт живую рябь вблизи. Гаснет вдали (detailFade).
                float2 nUv1 = patternUv * 0.55 + float2(t * 0.22, t * 0.13);
                float2 nUv2 = patternUv * 1.15 - float2(t * 0.17, -t * 0.21);
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
                    float2 uvA = patternUv * _NormalScale0 + (_NormalScroll0.xy * t);
                    float2 uvB = patternUv * _NormalScale1 + (_NormalScroll1.xy * t);
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
                     + (_SunLightColor * (sun * (ndl * 0.35 + ndlWrap * 0.65) * cloudShadow) * waterRad);

                float cosT = saturate(dot(n, viewDir));
                float fresnelPow = pow(1.0 - cosT, 5.0);
                // Шлик, F0 воды 0.02-0.04 как в природе (Crest/OceanShader):
                // вдаль — зеркало, вниз — прозрачная толща, а не молоко.
                float fresnelF = 0.03 + (0.97 * fresnelPow);

                // Солнечная дорожка: широкий блик + рассыпчатый глиттер.
                // Отдельная ВЧ-октава только для specular: дробит блин на
                // блёстки на закате; diffuse/foam её не видят.
                float sparkleFade = detailFade;
                const float2 kSpark = float2(660.0, -749.0);
                float2 sd1 = normalize(kSpark);
                float sf1 = max(length(kSpark) * kBaseUw, _SparkleScale * 2.2);
                float sp1 = dot(patternUv, sd1) * sf1 + (t * 3.1);
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
                     + (spec * sun * _SunLightColor * waterRad * cloudShadow);

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
                     * saturate(sun * ndl + 0.25) * (1.0 - deepW) * waterRad * cloudShadow;

                // Зеркало неба по Шлику (F0 воды 0.06 для красивого моря):
                // вдаль море светлеет к горизонту, а не остаётся тёмной заливкой.
                // Неба подмешиваем щедро + тёплый солнечный оттенок — море
                // зеркалит небо, как настоящий океан в ясный день.
                float3 reflectDir = reflect(-viewDir, n);
                 float3 skyRef = GalilegoSkyAmbient(reflectDir) * waterRad * 1.0
                     + (_SunLightColor * waterRad * 0.06 * cloudShadow);
                // Широкая солнечная дорожка на воде (шеен к солнцу поверх
                // зеркала; сам глиттер даёт spec ниже).
                float sunPath = pow(saturate(dot(reflectDir, sunDir)), 10.0);
                 skyRef += _SunLightColor * (sunPath * 0.05 * waterRad * cloudShadow);
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
                // Масштабы кратны kBase, чтобы пена тоже укладывалась в период P.
                float foamN1 = wfbm(patternUv * (kBaseUw * 228.0) + float2(t * 0.15, -t * 0.11));
                float foamN2 = wfbm(patternUv * (kBaseUw * 456.0) - float2(t * 0.12, t * 0.16));
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
                const float2 kSurfA = float2(57.0, 68.0);
                const float2 kSurfB = float2(-50.0, 44.0);
                float surfNoise = sin(dot(patternUv, kSurfA) * kBaseUw + (t * 0.9))
                    + (0.5 * sin(dot(patternUv, kSurfB) * kBaseUw - (t * 0.7)));
                float surfPhase = (depth * 2.1) - (t * _SurfSpeed) + (surfNoise * 1.5);
                float surfLines = smoothstep(0.78, 0.98, (sin(surfPhase) * 0.5 + 0.5));
                float surfAtten = 1.0 - smoothstep(0.0, max(1.0, _FoamDepthMeters * 0.9), depth);
                float surf = surfLines * surfAtten * _SurfStrength * shadingFade * 0.5;
                // Барашки в открытом море: пена только где гребень высок И шум
                // утверждён (порог в духе Dupuy whitecaps) — редкие рваные
                // пятна вместо молочной заливки. Вдали гаснет.
                float capN = wfbm(patternUv * (kBaseUw * 326.0) + float2(-t * 0.2, t * 0.14));
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
                // Полоса частичной прозрачности — УЗКАЯ, в метрах, а не в
                // «диапазоне шейдера». Раньше здесь стояло smoothstep(0, 2, depth):
                // на пологом шельфе (замерено: глубины 1.8..30 м на километр)
                // эти 2 м растягивались на сотни метров уреза, вода рисовалась
                // screen-door'ом (clip(alpha - ign)) поверх СЫРОГО песка — и давала
                // ровную «шахматную» полосу цвета пляжа с резкой прямой границей
                // там, где альфа насыщается. Теперь полупрозрачна только тонкая
                // кромка самого уреза (_ShallowAlphaMeters), дальше вода плотная.
                float alphaT = smoothstep(0.0, max(0.02, _ShallowAlphaMeters), depth);
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
        // Вода обязана попадать в depth pyramid. Иначе прозрачные шейдеры
        // (атмосфера, звёзды, диск солнца) в IsSky() считают пиксели воды небом
        // и рисуют небо ПОВЕРХ океана. На горизонте путь через атмосферу
        // огромный, поэтому там это даёт ровную непрозрачную полосу цвета
        // атмосферы ровно по линии горизонта: океан под ней не виден, и полоса
        // повторяет силуэт берега. Рельеф ту же проблему уже закрыл пассом
        // DepthForwardOnly (PlanetSurface.shader), вода — нет.
        // HDRP требует DepthForwardOnly у forward-материалов
        // (см. HDRenderPipeline.RenderGraph.cs:952-956).
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // Те же глобалы и та же волна, что в WaterSurfaceForward: смещение
            // обязано быть ПОБАЙТОВО тем же. Без него препасс лежит на
            // невозмущённой поверхности, а forward-vertex с волной ниже неё
            // проваливается под свой же depth и отсекается ZTest LEqual —
            // чёрные дыры на воде в ложбинах гребней.
            float3 _PlanetCameraPos;
            float3 _PlanetWaterCenter;
            float _WaterTime;
            float _WaveScale;
            float _WaveAmplitude;
            float4x4 _WaterWorldToBody;
            float3 _WaterBodyAnchor;

            float3 WaterBodyFixedDepth(float3 positionWS)
            {
                float3 relBody = mul((float3x3)_WaterWorldToBody, positionWS - _PlanetCameraPos);
                return relBody + _WaterBodyAnchor;
            }

            float WaterWaveHeight01Depth(float2 uv, float t, out float2 gradTangent)
            {
                float2 p = uv;
                // Те же целые K и тот же kBase, что в WaterWaveHeight01: иначе
                // потолок (Depth-проход) и поверхность сверху разойдутся.
                const float kBase = 0.006135923151542565;  // 2*pi/1024
                const float2 K1 = float2(156.0, 47.0);
                const float2 K2 = float2(-153.0, 231.0);
                const float2 K3 = float2(311.0, -311.0);
                const float2 K4 = float2(-144.0, -719.0);
                const float2 K5 = float2(948.0, -635.0);
                float L1 = length(K1); float L2 = length(K2);
                float L3 = length(K3); float L4 = length(K4);
                float L5 = length(K5);
                float2 d1 = K1 / L1; float2 d2 = K2 / L2;
                float2 d3 = K3 / L3; float2 d4 = K4 / L4;
                float2 d5 = K5 / L5;
                float f1 = L1 * kBase; float f2 = L2 * kBase;
                float f3 = L3 * kBase; float f4 = L4 * kBase;
                float f5 = L5 * kBase;
                float p1 = dot(p, K1) * kBase + (t * 1.1);
                float p2 = dot(p, K2) * kBase - (t * 1.35);
                float p3 = dot(p, K3) * kBase + (t * 1.9);
                float p4 = dot(p, K4) * kBase - (t * 2.6);
                float p5 = dot(p, K5) * kBase + (t * 3.4);
                float s1 = sin(p1);
                float s2 = sin(p2);
                float s3 = sin(p3);
                float s4 = sin(p4);
                float s5 = sin(p5);
                float h = (0.38 * s1) + (0.26 * s2) + (0.18 * s3) + (0.11 * s4) + (0.07 * s5);
                float qShape = 0.25;
                float dhds = 1.0 + (2.0 * qShape * h);
float2 grad = ((0.38 * f1 * d1) * cos(p1)
                    + (0.26 * f2 * d2) * cos(p2)
                    + (0.18 * f3 * d3) * cos(p3)
                    + (0.11 * f4 * d4) * cos(p4)
                    + (0.07 * f5 * d5) * cos(p5)) * dhds;
                // Градиент в тех же координатах, где посчитан (см. WaterBodyFixed);
                // вызывающий переводит его в рендер-оси через _WaterWorldToBody.
                gradTangent = grad;
                return h + (qShape * h * h);
            }

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 baseWS = TransformObjectToWorld(input.positionOS);
                float3 centerDeltaV = baseWS - _PlanetWaterCenter;
                float3 baseN = dot(centerDeltaV, centerDeltaV) > 1.0
                    ? normalize(centerDeltaV)
                    : TransformObjectToWorldNormal(float3(0.0, 0.0, 1.0));
                float camDistV = length(_PlanetCameraPos - baseWS);
                float fadeV = exp(-camDistV * 0.00012);
                float2 gradDepth;
                float h01 = WaterWaveHeight01Depth(
                    WaterBodyFixedDepth(baseWS).xz * _WaveScale, _WaterTime, gradDepth);
                float h = _WaveAmplitude * h01 * fadeV;
                output.positionCS = TransformWorldToHClip(baseWS + (baseN * h));
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
