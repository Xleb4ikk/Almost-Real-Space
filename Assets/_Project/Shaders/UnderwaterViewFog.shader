// Подводное затухание по закону Бера — Ламберта, спектральное (поглощение
// по каналам), полноэкранный проход HDRP.
//
// Почему не встроенный HDRP Fog: он одноцветный (цвет + meanFreePath) и не
// умеет разное поглощение по каналам. Из-за этого дальнее уходит в серую
// дымку, а не в сине-зелёный, и вода не читается как вода. Спектральное
// поглощение физически и даёт этот переход: красный (σ максимальный) умирает
// первым, синий (σ минимальный) держится дальше всех.
//
// ТОЧНОСТЬ — главная тонкость, поэтому вынесена в комментарии.
//
//  1) «Под водой или нет» НЕЛЬЗЯ решать в шейдере как length(C) - sea.
//     Радиус Земли 6 371 000 м, ULP float32 на таком масштабе около 0.5 м.
//     Разность двух таких чисел даёт шум ±0.5 м в величине, по которой
//     решается, под водой ли камера, при ширине перехода всего ±0.4 м —
//     будет мигание на границе. Поэтому camAlt считается в double на CPU и
//     приезжает готовым (_UwCamAltitude).
//
//  2) Выход луча через поверхность. Из |C + tD|^2 = sea^2 получаем
//     t = -b + sqrt(b^2 + k), где b = dot(C,D), k = sea^2 - |C|^2.
//     При b ≈ 6.4e6 и k ≈ 1.3e8 (глубина 10 м) разность sqrt(b^2+k) - b
//     равна ~10 м, то есть вычитаются два числа порядка 6.4e6. В float32
//     это снос остатка в ноль. Поэтому берётся форма с сопряжённым
//     множителем: t = k / (sqrt(b^2+k) + b) — там только сумма, и k
//     приезжает точным (double на CPU), ошибка ~1e-7.
//
//  3) Направление луча строится из БАЗИСА камеры (три единичных вектора), а
//     не как normalize(positionWS - camPosWS): во-первых, Vert из
//     CustomPassCommon заполняет только positionCS, positionWS там нет; во-
//     вторых, вычитание больших координат в float32 съело бы точность.
//     Всё, что попадает в шейдер, единичного масштаба.
//
//  4) k = (sea - |C|)(sea + |C|) считается на CPU в double, приезжает готовым.

Shader "Galilego/UnderwaterViewFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        // Проход 1: считаем затухание, пишем в собственный буфер.
        Pass
        {
            Name "UnderwaterViewFog"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FogPass
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

            // --- параметры профиля воды -------------------------------------------
            float3 _UwSigma;             // поглощение, 1/м, по каналам R,G,B
            float3 _UwWaterColor;        // цвет рассеянного света (вуали), linear
            float  _UwAmbient;           // постоянная составляющая вуали
            float  _UwSunShapeFloor;     // нижняя доля солнечного света при N·L = 0
            float  _UwSunPenetration;    // насколько солнце гаснет с глубиной
            float  _UwTransitionMeters;  // ширина плавного перехода через поверхность
            float  _UwDownwardDarkening; // насколько темнеет при взгляде вниз

            // --- геометрия, посчитанная на CPU в double --------------------------
            float  _UwCamAltitude;       // высота камеры над уровнем моря, м (<0 — под водой)
            float  _UwSeaK;              // k = sea^2 - |C|^2, м^2
            float  _UwRadius;            // |C|, м
            float3 _UwUp;                // локальная вверх, simulation-пространство
            float3 _UwSunDir;            // направление НА солнце

            // Небесная засветка (SkyEnvironment). Нужна, чтобы вуаль гасла
            // ночью вместе с небом.
            //
            // Без неё _UwAmbient — константа, то есть вода светилась ровно
            // на 0.55·_UwWaterColor круглые сутки, и ночью в окне не было
            // НИЧЕГО: замер снимка дал p90/p50 = 1.03, то есть картинка
            // отличалась от фона только шумом. _SkyAmbientColor уже нормирован
            // к опорному дню (скалоляция(цвет) ≈ ambientAmount, 0..2), поэтому
            // достаточно насытить его в 0..1.
            float3 _SkyAmbientColor;

            // --- базис камеры для восстановления направления луча ----------------
            float3 _UwCamRight;
            float3 _UwCamUp;
            float3 _UwCamFwd;
            float  _UwTanHalfFovX;
            float  _UwTanHalfFovY;

            // Отладочный вывод одного члена, см. UnderwaterViewFog.DebugMode.
            // Нужен, когда картинка идёт слоями: видно, ЧТО именно полосит.
            int    _UwDebugMode;

            // b = dot(C,D) = |C| * dot(up,D) - так не приходится тащить в шейдер
            // большой вектор C.
            float ExitDistance(float3 D)
            {
                float b = _UwRadius * dot(_UwUp, D);
                return _UwSeaK / (sqrt(b * b + _UwSeaK) + b);
            }

            float3 ViewDirection(float2 uv)
            {
                float2 ndc = (uv * 2.0) - 1.0;

                // На D3D/Vulkan uv.y = 0 внизу кадра, на GL — вверху, поэтому знак
                // экрана инвертируется на площадках, где UV начинается сверху.
                #if UNITY_UV_STARTS_AT_TOP
                    ndc.y = -ndc.y;
                #endif

                return normalize(_UwCamFwd
                    + (_UwCamRight * (ndc.x * _UwTanHalfFovX))
                    + (_UwCamUp * (ndc.y * _UwTanHalfFovY)));
            }

            float4 FogPass(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.positionCS.xy * _ScreenSize.zw;
                uint2 pixel = uint2(input.positionCS.xy);

                // Плавный переход, НЕ if - иначе мигает при качке.
                float halfT = max(0.01, _UwTransitionMeters * 0.5);
                float under = 1.0 - smoothstep(-halfT, halfT, _UwCamAltitude);

                float3 scene = CustomPassLoadCameraColor(pixel, 0);

                // Отладка отдаёт СЫРОЙ член, без затухания, иначе полосы
                // замазались бы тем самым эффектом, который мы и проверяем.
                if (_UwDebugMode > 0)
                {
                    if (_UwDebugMode == 1) { return float4(under.xxx, 1.0); }
                    if (_UwDebugMode == 7) { return float4(scene, 1.0); }

                    // 6 - попадание в кадр. Если картинка идёт полосами, а здесь
                    // ровный градиент, значит полосит не чтение кадра, а что-то
                    // другое; если полосы есть и здесь - виновата выборка буфера.
                    if (_UwDebugMode == 6) { return float4(uv.x.xxx, 1.0); }

                    float rawDepthDbg = CustomPassLoadCameraDepth(pixel);
                    float eyeDepthDbg = LinearEyeDepth(rawDepthDbg, _ZBufferParams);

                    if (_UwDebugMode == 4) { return float4(eyeDepthDbg / 1000.0, 0, 0, 1.0); }

                    float3 Ddbg = ViewDirection(uv);
                    float tExitDbg = ExitDistance(Ddbg);

                    if (_UwDebugMode == 5)
                    {
                        // -1, если луч не выходит через поверхность (смотрит вниз).
                        float vis = (tExitDbg > 0.0) ? (tExitDbg / 5000.0) : -1.0;
                        return float4(vis.xxx, 1.0);
                    }

                    float pathDbg = eyeDepthDbg;
                    if (_UwCamAltitude < 0.0 && tExitDbg > 0.0)
                    {
                        pathDbg = min(pathDbg, tExitDbg);
                    }

                    if (_UwDebugMode == 2) { return float4(pathDbg / 50.0, 0, 0, 1.0); }
                    if (_UwDebugMode == 3) { return float4(exp(-_UwSigma * pathDbg), 1.0); }
                }

                if (under <= 0.002)
                {
                    return float4(scene, 1.0);
                }

                // Расстояние до непрозрачного пикселя. Обратный Z: небо и дальняя
                // плоскость дают depth около нуля, и LinearEyeDepth разворачивает
                // это в дальнюю плоскость - то есть нужный «бесконечный» путь.
                float rawDepth = CustomPassLoadCameraDepth(pixel);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                float3 D = ViewDirection(uv);

                // Путь в воде: пиксель под водой - до него; пиксель над водой -
                // до выхода луча через поверхность, что и даёт затухание неба.
                float pathIn = eyeDepth;
                if (_UwCamAltitude < 0.0)
                {
                    float tExit = ExitDistance(D);
                    if (tExit > 0.0)
                    {
                        pathIn = min(pathIn, tExit);
                    }
                }

                float3 T = exp(-_UwSigma * pathIn);

                // Свет, дошедший до камеры. Здесь была физическая ошибка: цвет воды
                // трактовался как АЛЬБЕДО и домножался на маленькое число света
                // (0.36*0.43 + 0.08 = 0.24), отчего вуаль выходила ~0.025 линейного
                // и вся картинка умещалась в 4-19 кодов из 256 - отсюда полосы
                // вместо градиента (посчитано в T140).
                //
                // Рассеянный свет в объёме - ИЗЛУЧЕНИЕ, а не отражение, поэтому
                // уровень освещения берётся около единицы и гаснет только с
                // глубиной. И направление на солнце смягчено: рассеяние в воде
                // почти изотропно, ламбертовский cos неуместен и съедал яркость.
                float depthBelow = max(-_UwCamAltitude, 0.0);
                float3 sunT = exp(-_UwSigma * (depthBelow * _UwSunPenetration));
                float ndl = saturate(dot(_UwUp, _UwSunDir));
                float shape = _UwSunShapeFloor + ((1.0 - _UwSunShapeFloor) * ndl);

                // Вуаль от неба, а не константа: яркость неба уже посчитана на
                // CPU той же физикой (AtmosphereOptics.SkyAmbientRadiance) и
                // приходит нормированной к дню, то есть ночью здесь ноль.
                // Раньше _UwAmbient стоял константой, и ночью вода светилась
                // ровно так же, как днём — за окном не было ни звёзд, ни
                // лунного света, только ровная синяя заливка.
                float skyAmbient = saturate(dot(_SkyAmbientColor, float3(0.2126, 0.7152, 0.0722)));
                float3 scatter = _UwWaterColor * ((sunT * shape) + (_UwAmbient * skyAmbient));

                // Взгляд вниз темнее: меньше света от зенита.
                scatter *= lerp(1.0, _UwDownwardDarkening, saturate(-dot(D, _UwUp)));

                float3 fogged = (scene * T) + (scatter * (1.0 - T));
                return float4(lerp(scene, fogged, under), 1.0);
            }
            ENDHLSL
        }

        // Проход 2: перенос результата в кадр. Отдельный, потому что первый
        // читает буфер камеры, и писать в него же одновременно - чтение/запись
        // по одному ресурсу. Тот же приём, что в UnderwaterGodRays.
        Pass
        {
            Name "UnderwaterViewFogComposite"
            ZWrite Off
            ZTest Always
            Blend One Zero
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositePass
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

            float _UwDitherAmount;

            float4 CompositePass(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.positionCS.xy * _ScreenSize.zw;
                float3 c = CustomPassSampleCustomColor(uv).rgb;

                // Дизеринг. Даже после правильного освещения подводный градиент
                // пологий: на 1440 строки приходится лишь несколько десятков
                // 8-битных кодов, и без дизеринга остаются полосы шириной в
                // десятки строк. Треугольная ППФ со смещением на полшага кода
                // разбивает квантование на шум, не добавляя постоянного смещения.
                uint2 p = uint2(input.positionCS.xy);
                float dither = (InterleavedGradientNoise(p, 0) - 0.5) * (_UwDitherAmount / 255.0);
                return float4(c + dither, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

