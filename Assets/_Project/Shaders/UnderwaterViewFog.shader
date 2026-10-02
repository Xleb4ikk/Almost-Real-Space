// Подводное затухание по закону Бера — Ламберта, спектральное, полноэкранный
// проход HDRP. ПОЛНАЯ ЗАМЕНА файла Shaders/UnderwaterViewFog.shader.
//
// Что изменено относительно прежней версии:
//
//  1) Рассеянный свет (вуаль) теперь гаснет с глубиной КАМЕРЫ. Раньше
//     гасла только солнечная часть, а _UwAmbient * skyAmbient оставался
//     константой - поэтому на любой глубине экран был одного и того же
//     голубого и никакого затемнения не было.
//
//  2) Путь луча считается как РАССТОЯНИЕ ВДОЛЬ ЛУЧА (eyeDepth / cos к оси
//     камеры), а не как eyeDepth. LinearEyeDepth - это z в пространстве
//     камеры; на краю экрана при FOV 90 градусов настоящий путь на 40 %
//     длиннее, и затухание там занижалось.
//
//  3) Потолок воды (окно Снелла) рисуется здесь аналитически, если меш
//     воды не оставил в буфере глубины поверхности над камерой (режим Auto)
//     или всегда (режим Always, для проверки). Если меш воды рисуется
//     нормально, Auto ничего не трогает.
//
// Точность (см. прежние комментарии): высота камеры и k = sea^2 - |C|^2
// считаются на CPU в double; выход луча через поверхность берётся в форме
// t = k / (sqrt(b^2 + k) + b), без вычитания больших чисел.

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
            float  _UwAmbientDecay;      // доля sigma, с которой гаснет рассеянный свет с глубиной
            float  _UwSunShapeFloor;     // нижняя доля солнечного света при N·L = 0
            float  _UwSunPenetration;    // насколько солнце гаснет с глубиной
            float  _UwTransitionMeters;  // ширина плавного перехода через поверхность
            float  _UwDownwardDarkening; // насколько темнеет при взгляде вниз
            int    _UwCeilingMode;       // 0 выкл, 1 авто (только если меш не нарисован), 2 всегда

            // --- геометрия, посчитанная на CPU в double --------------------------
            float  _UwCamAltitude;       // высота камеры над уровнем моря, м (<0 - под водой)
            float  _UwSeaK;              // k = sea^2 - |C|^2, м^2
            float  _UwRadius;            // |C|, м
            float3 _UwUp;                // локальная вверх, simulation-пространство
            float3 _UwSunDir;            // направление НА солнце

            // Глобалы SkyEnvironment (те же, что читает WaterSurface.shader).
            float3 _SkyAmbientColor;
            float3 _SunLightColor;
            float  _SkyAmbient;
            float  _TerrainRadianceScale;

            // Глобалы воды (ставит PlanetSurfaceRenderer): узор потолка берётся
            // в тех же тело-fixed координатах, что и у меша воды.
            float  _WaterTime;
            float4x4 _WaterWorldToBody;
            float3 _WaterBodyAnchor;

            // --- базис камеры для восстановления направления луча ----------------
            float3 _UwCamRight;
            float3 _UwCamUp;
            float3 _UwCamFwd;
            float  _UwTanHalfFovX;
            float  _UwTanHalfFovY;

            // Отладка, см. UnderwaterViewFog.DebugMode.
            int    _UwDebugMode;

            float ExitDistance(float3 D)
            {
                float b = _UwRadius * dot(_UwUp, D);
                return _UwSeaK / (sqrt(max(b * b + _UwSeaK, 0.0)) + b);
            }

            float3 ViewDirection(float2 uv)
            {
                // Канонический способ HDRP: две точки одного и того же луча
                // (на ближней и на дальней плоскости) через обратную VP-матрицу.
                // Разность не зависит ни от camera-relative режима, ни от знака
                // Y на платформе, ни от FOV и lens shift.
                float3 nearWS = ComputeWorldSpacePosition(uv, 1.0, UNITY_MATRIX_I_VP);
                float3 farWS = ComputeWorldSpacePosition(uv, 0.0, UNITY_MATRIX_I_VP);
                return normalize(farWS - nearWS);
            }

            // Небо в направлении dir (луч НАРУЖУ из воды). Те же формулы и
            // константы, что в GalilegoSkyRadiance.hlsl, но без облаков и звёзд.
            float3 WindowSky(float3 dir)
            {
                float upness = saturate(dot(dir, _UwUp));
                float3 horizonTint = _SkyAmbientColor * float3(1.22, 1.06, 0.90);
                float3 zenithTint = _SkyAmbientColor * float3(0.70, 0.90, 1.28);
                float3 sky = lerp(horizonTint, zenithTint, pow(upness, 0.45)) * (_SkyAmbient * 20.0);

                float sunFactor = smoothstep(-0.035, 0.06, dot(_UwSunDir, _UwUp));
                float cosA = dot(dir, _UwSunDir);
                if (sunFactor > 0.001 && cosA > 0.0)
                {
                    float theta = sqrt(max(0.0, 2.0 * (1.0 - cosA)));
                    const float sigma = 0.03;
                    sky += _SunLightColor * (0.05 * sunFactor) * exp(-theta / 0.075);
                    sky += _SunLightColor * (22.0 * sunFactor) * exp(-(theta * theta) / (2.0 * sigma * sigma));
                }

                return max(sky, 0.0);
            }

            // Наклон волны на потолке: две бегущие ряби ~1 м и ~0.4 м плюс
            // длинная зыбь. P - вектор от камеры до точки на поверхности
            // (мировые оси). Узор берётся в тело-fixed координатах: он стоит
            // на месте относительно моря, а не едет за камерой.
            float3 CeilingNormal(float3 P, float pathLen)
            {
                const float kBase = 0.006135923151542565; // 2*pi/1024
                const float2 kRip1 = float2(883.0, 522.0);
                const float2 kRip2 = float2(-588.0, 1043.0);
                const float2 kSwell = float2(156.0, 47.0);
                const float slope = 0.17;

                float3 bodyPos = mul((float3x3)_WaterWorldToBody, P) + _WaterBodyAnchor;
                float2 p = bodyPos.xz;

                float2 d1 = normalize(kRip1);
                float2 d2 = normalize(kRip2);
                float2 d3 = normalize(kSwell);
                float ph1 = dot(p, d1) * (length(kRip1) * kBase) + (_WaterTime * 4.4);
                float ph2 = dot(p, d2) * (length(kRip2) * kBase) - (_WaterTime * 6.8);
                float ph3 = dot(p, d3) * (length(kSwell) * kBase * 0.18) + (_WaterTime * 1.1);

                float2 g = (d1 * (0.75 * slope * cos(ph1)))
                    + (d2 * (0.53 * slope * cos(ph2)))
                    + (d3 * (0.30 * slope * cos(ph3)));

                // Далеко рябь меньше пикселя - гасим, иначе мерцание.
                g *= rcp(1.0 + (pathLen * 0.03));
                g *= min(1.0, (slope * 1.8) / max(length(g), 1e-4));

                float3 g3 = float3(g.x, 0.0, g.y);
                g3 -= _UwUp * dot(g3, _UwUp);
                return normalize(_UwUp - g3);
            }

            // Потолок воды снизу: окно Снелла (Френель вода -> воздух) и за
            // критическим углом - зеркало толщи (veilMirror).
            float3 CeilingColor(float3 D, float tExit, float3 veilMirror)
            {
                float waterRad = min(_TerrainRadianceScale, 1.0);
                float3 n = CeilingNormal(D * tExit, tExit);

                float cosI = saturate(dot(D, n));
                float sinT = 1.33 * sqrt(saturate(1.0 - (cosI * cosI)));
                bool transmitted = (sinT < 1.0);

                float fresnel = 1.0;
                float3 skySeen = 0.0;
                if (transmitted)
                {
                    float cosT = sqrt(max(1.0 - (sinT * sinT), 0.0));
                    float rs = ((1.33 * cosI) - cosT) / max((1.33 * cosI) + cosT, 1e-4);
                    float rp = ((1.33 * cosT) - cosI) / max((1.33 * cosT) + cosI, 1e-4);
                    fresnel = saturate(0.5 * ((rs * rs) + (rp * rp)));

                    float3 skyDir = refract(D, -n, 1.33);
                    skySeen = WindowSky(skyDir) * (3.0 * waterRad);
                }

                float3 color = lerp(skySeen, veilMirror, fresnel);
                // Светлый ободок у границы окна - тот же Френель.
                color += skySeen * (0.5 * fresnel * (1.0 - fresnel) * 2.0);
                return color;
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

                if (_UwDebugMode == 1) { return float4(under.xxx, 1.0); }
                if (_UwDebugMode == 6) { return float4(uv.x.xxx, 1.0); }
                if (_UwDebugMode == 7) { return float4(scene, 1.0); }

                // Расстояние до непрозрачного пикселя ВДОЛЬ ЛУЧА. Обратный Z:
                // небо и дальняя плоскость дают огромное значение - это и есть
                // «бесконечный» путь.
                float rawDepth = CustomPassLoadCameraDepth(pixel);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 D = ViewDirection(uv);
                float cosFwd = max(dot(D, _UwCamFwd), 1e-3);
                float sceneDist = eyeDepth / cosFwd;

                bool inWater = (_UwCamAltitude < 0.0);
                float tExit = inWater ? ExitDistance(D) : -1.0;
                bool lookingUp = (dot(D, _UwUp) > 0.0);

                // Освещение: свет гаснет с глубиной КАМЕРЫ, и солнечный, и
                // рассеянный (рассеянный чуть медленнее - он диффузный).
                float depthBelow = max(-_UwCamAltitude, 0.0);
                float3 sunT = exp(-_UwSigma * (depthBelow * _UwSunPenetration));
                float3 ambT = exp(-_UwSigma * (depthBelow * _UwAmbientDecay));
                float ndl = saturate(dot(_UwUp, _UwSunDir));
                float shape = _UwSunShapeFloor + ((1.0 - _UwSunShapeFloor) * ndl);
                float skyAmbient = saturate(dot(_SkyAmbientColor, float3(0.2126, 0.7152, 0.0722)));
                float3 veil = _UwWaterColor * ((sunT * shape) + (_UwAmbient * skyAmbient * ambT));

                // Взгляд вниз темнее: меньше света от зенита.
                float downK = lerp(1.0, _UwDownwardDarkening, saturate(-dot(D, _UwUp)));
                float3 scatter = veil * downK;

                // Нужно ли дорисовать потолок.
                bool ceilingHere = false;
                bool surfaceInDepth = false;
                if (inWater && lookingUp && tExit > 0.0 && _UwCeilingMode > 0)
                {
                    float tol = max(1.0, 0.25 * tExit);
                    surfaceInDepth = (sceneDist < (tExit + tol));
                    if (_UwCeilingMode >= 2)
                    {
                        // Всегда: потолок там, где перед поверхностью ничего нет.
                        ceilingHere = (sceneDist > (tExit - tol));
                    }
                    else
                    {
                        // Авто: только если меш воды не оставил глубины на поверхности.
                        ceilingHere = !surfaceInDepth;
                    }
                }

                if (_UwDebugMode == 8)
                {
                    // R - дорисовываем потолок, G - поверхность есть в глубине, B - под водой.
                    return float4(ceilingHere ? 1.0 : 0.0, surfaceInDepth ? 1.0 : 0.0, under, 1.0);
                }

                if (ceilingHere)
                {
                    scene = CeilingColor(D, tExit, veil * _UwDownwardDarkening);
                }

                // Путь в воде: пиксель под водой - до него; пиксель над водой -
                // до выхода луча через поверхность.
                float pathIn = sceneDist;
                if (inWater && tExit > 0.0)
                {
                    pathIn = min(pathIn, tExit);
                }

                float3 T = exp(-_UwSigma * pathIn);

                if (_UwDebugMode == 2) { return float4(pathIn / 50.0, 0, 0, 1.0); }
                if (_UwDebugMode == 3) { return float4(T, 1.0); }
                if (_UwDebugMode == 4) { return float4(eyeDepth / 1000.0, 0, 0, 1.0); }
                if (_UwDebugMode == 5)
                {
                    float vis = (tExit > 0.0) ? (tExit / 5000.0) : -1.0;
                    return float4(vis.xxx, 1.0);
                }

                if (under <= 0.002)
                {
                    return float4(scene, 1.0);
                }

                float3 fogged = (scene * T) + (scatter * (1.0 - T));
                return float4(lerp(scene, fogged, under), 1.0);
            }
            ENDHLSL
        }

        // Проход 2: перенос результата в кадр. Не менялся.
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

                uint2 p = uint2(input.positionCS.xy);
                float dither = (InterleavedGradientNoise(p, 0) - 0.5) * (_UwDitherAmount / 255.0);
                return float4(c + dither, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
