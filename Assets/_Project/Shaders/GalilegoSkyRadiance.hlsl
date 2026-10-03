// Небо по направлению для ШЕЙДЕРА ВОДЫ: окно Снелла смотрит в небо, и
// смотреть оно должно тем же небом, что и небо над водой.
//
// Почему отдельный include, а не функция в WaterSurface.shader: воде нужны
// ровно те же глобалы, что и рельефу (SkyEnvironment) и облакам
// (PlanetCloudsView), но подключать GalilegoLighting.hlsl в воду нельзя —
// он тянет HDShadow, а водяной ForwardOnly-пас сознательно идёт без вариантов
// теней (DEBUG-MINIMAL3 в WaterSurface.shader). Поэтому здесь объявления
// глобалов и сам расчёт неба, а GalileoLighting.hlsl остаётся как был.
//
// Что здесь НЕ делается и почему:
//   - Не используется IntegrateAtmosphere из PlanetAtmosphere.shader. Точное
//     совпадение с небом над водой стоит ~48 шагов raymarch с двумя выборками
//     LUT на пиксель, а потолок занимает весь экран при взгляде вверх: это
//     удвоение стоимости неба. Вместо этого — аналитика на тех же
//     физических глобалах.
//   - Не берётся IntegrateAtmosphere и для окна: тот же raymarch, но уже
//     один раз на кадр.
//
// Точность на планетарном радиусе: все вычисления облаков идут по
// НАПРАВЛЕНИЯМ (единичный вектор) и по относительному радиусу, а не по
// абсолютным координатам вида. RaySphere нормирует начало луча на радиус
// оболочки перед решением квадратного уравнения (CloudRaySphere), поэтому
// при R = 6.4e6 м ошибка float32 в ULP (~0.5 м) попадает в коэффициенты
// после деления, а не в Position в метрах. Остаточная неточность длины
// луча — 0.5 м на 8000 м пути до облаков, то есть 6e-5 радиана в
// направлении: облако смещается меньше чем на миллиметр. Мерцания не будет.

#ifndef GALILEGO_SKY_RADIANCE_INCLUDED
#define GALILEGO_SKY_RADIANCE_INCLUDED

// GalilegoCloudField.hlsl подключается ради SampleCloudEarthMask и
// CloudRaySphere. У него собственный guard, поэтому повторное подключение
// безопасно: WaterSurface.shader уже включает его раньше.
#include "GalilegoCloudField.hlsl"

// --- Глобалы SkyEnvironment (тот же набор, что в GalilegoLighting.hlsl) -----
float3 _SkyAmbientColor;   // тинт ambient × нормированная яркость неба
float3 _SunLightColor;     // фотосфера × прозрачность атмосферы
float3 _TerrainSunDir;     // направление НА солнце
float3 _SkyUp;             // локальная вверх наблюдателя
float _SkyAmbient;         // множитель дневного ambient
float _TerrainSun;         // сила солнечного члена сцены
float _TerrainRadianceScale; // HDR-буст земли (ставит SkyEnvironment)
float _NightAmbient;       // ночная засветка
float _StarVisibility;     // видимость звёзд 0..1 (день 0, ночь 1)

// --- Геометрия оболочки облаков для выборки по преломлённому лучу ---------
// Объявлены в GalilegoCloudField.hlsl: _CldBottomRadius, _CldTopRadius,
// _CldCoverage, _CldBottomFeather, _CldTopFeather, _CldWorldToBody.

/// <summary>
/// Угловой радиус Солнца в радианах: 0.265° — половина видимого диаметра
/// 0.53°. Это ЖЁСТКИЙ НИЖНИЙ предел ширины блика: уже в штиль диск не может
/// быть резче своего настоящего размера.
/// </summary>
static const float SunAngularRadius = 0.00463;

// Множитель, переводящий «ambient неба» (_SkyAmbientColor · _SkyAmbient) в
// яркость неба для окна. Ambient — это СРЕДНЯЯ яркость полусферы, а не
// небо в конкретном направлении, и неба в направлении зенита она выше в
// несколько раз. Калибровано так, чтобы окно было примерно в 7-12 раз
// ярче вуали (0.02..0.25 линейных в дне) — это и есть требуемое соотношение
// без пересвета.
static const float SkyBaseGain = 20.0;

// ЛУНА — ТОЧКА РАСШИРЕНИЯ. Сейчас ночное окно показывает только звёзды:
// луны в рендер-пайплайне проекта нет (только в эфемеридах звёздной системы,
// ни один рендер-объект её не рисует). Когда появится тело с направлением на
// луну, подставь сюда диск и гало по той же схеме, что и Солнце ниже:
//   float3 moonDir = normalize(_UwMoonDirWS);
//   float3 moon = GalilegoSunDisc(dir, moonDir, _UwMoonRadiance, sigmaAngle)
//       + GalilegoSunAureole(dir, moonDir, _UwMoonRadiance * 0.06f);
/// <summary>
/// Ночная луна в окне Снелла. Пока тела Луны нет — ноль, и ночное окно
/// держится на звёздах. См. врезку выше: это точка вставки диска.
/// </summary>
float3 GalilegoMoonRadiance(float3 dir, float normalSigmaAngle)
{
    return 0.0;
}

/// <summary>
/// Полусферический ambient — та же формула, что в GalilegoLighting.hlsl.
/// Дублируется здесь потому, что вода не включает GalilegoLighting.hlsl.
/// </summary>
float3 GalilegoSkyAmbient(float3 normalWS)
{
    float hemi = saturate((dot(normalWS, _SkyUp) * 0.5) + 0.5);
    return _SkyAmbientColor * (_SkyAmbient * (0.35 + 0.65 * hemi));
}

/// <summary>
/// Мягкий диск по Коксу — Манку: exp(−θ²/2σ²), где σ = sqrt(σ_диск² + σ_наклон²).
///
/// Почему не pow(dot, 900) и не жёсткий smoothstep по углу: при наклонах
/// волны ±8° преломление разносит направление на солнце по площади окна на
/// углы в несколько раз больше солнечного диска. Жёсткий конус там превращается
/// в набор редких пикселей и мерцает при движении, а Гаусс той же суммарной
/// энергии распадается на устойчивые блики — на спокойной воде почти круглый
/// диск, на волне — россыпь. Это тот же приём, что и в specular anti-aliasing,
/// только ширина берётся из разброса нормали по пикселю.
///
/// sigmaAngle — разброс нормали в радианах на пиксель (см.
/// WaterSurfaceUnderwaterSigma). Диск в спокойной воде не может быть уже
/// собственного углового радиуса Солнца.
/// </summary>
float3 GalilegoSunDisc(float3 dir, float3 sunDir, float3 radiance, float sigmaAngle)
{
    float cosA = dot(dir, sunDir);
    if (cosA <= 0.0)
    {
        return 0.0;
    }

    // θ ≈ sqrt(2(1−cosθ)) — точная аппроксимация на малых углах (диск и
    // гало — это как раз малые углы), зато без acos на весь экран.
    float theta2 = 2.0 * (1.0 - cosA);
    float sigma = sqrt(max(SunAngularRadius * SunAngularRadius, sigmaAngle * sigmaAngle));
    float lobe = exp(-theta2 / (2.0 * sigma * sigma));
    return radiance * lobe;
}

/// <summary>
/// Гало (ауреоль) вокруг Солнца: Ми с широким хвостом. Тоже сглаженное, иначе
/// на границе окна рождается кольцо из aliasing-мусора.
/// </summary>
float3 GalilegoSunAureole(float3 dir, float3 sunDir, float3 radiance, float sigmaAngle)
{
    float cosA = dot(dir, sunDir);
    if (cosA <= 0.0)
    {
        return 0.0;
    }

    float theta = sqrt(max(0.0, 2.0 * (1.0 - cosA)));
    float sigma = 0.075 + sigmaAngle;
    return radiance * exp(-theta / sigma);
}

/// <summary>
/// Хеш ячейки направления — тот же, что в StarField.shader (звёзды должны
/// стоять там же, где в небе).
/// </summary>
float GalilegoHash31(float3 p)
{
    p = frac((p * float3(0.1031, 0.1030, 0.0973)));
    p += dot(p, p.yxz + 33.33);
    return frac((p.x + p.y) * p.z);
}

/// <summary>
/// Один слой звёзд, та же схема, что в StarField.shader: по ячейкам направления
/// звезда со своим положением, яркостью и температурой. Нужна для ночного
/// окна Снелла — ночью небо иначе равномерно чёрное.
/// </summary>
float2 GalilegoStarLayer(float3 dir, float density, float keepThreshold, float size, float seed)
{
    float3 p = dir * density;
    float3 q = floor(p);
    float3 r = frac(p);
    float3 starPos = float3(
        GalilegoHash31(q + seed),
        GalilegoHash31(q + (seed + 11.1)),
        GalilegoHash31(q + (seed + 23.2)));
    float d = length(r - starPos);
    float keep = step(keepThreshold, GalilegoHash31(q + (seed + 7.7)));
    float brightness = keep * smoothstep(size, 0.0, d);
    float temp = GalilegoHash31(q + (seed + 41.0));
    return float2(brightness, temp);
}

/// <summary>
/// Покрытие облаками вдоль луча: одна выборка поля на входе луча в слой.
/// Дёшево (~32 хеша), но рябь двигает точку входа, поэтому облака в окне
/// шевелятся вместе с волнами — это и нужно для «живого» окна.
///
/// Луч берём из камеры (начало в нуле render-пространства), а не из точки
/// поверхности: разница в несколько метров при 8 км до слоя смещает вход
/// меньше чем на сантиметр, зато начало луча остаётся в нуле и вход
/// считается через CloudRaySphere, который нормирует начало на радиус
/// оболочки — на R = 6.4e6 м это единственная форма, переживающая точность
/// float32 (см. врезку в шапке файла).
/// </summary>
float CloudShellCoverage(float3 dir, float3 bodyCenterWS, float radiusBottom, float radiusTop)
{
    float3 origin = -bodyCenterWS;
    float tTop0, tTop1, tBottom0, tBottom1;
    if (!CloudRaySphere(origin, dir, radiusTop, tTop0, tTop1))
    {
        return 0.0;
    }

    bool hitBottom = CloudRaySphere(origin, dir, radiusBottom, tBottom0, tBottom1);
    float tEnter = max(tTop0, 0.0);
    float tExit = (hitBottom && tBottom0 > tEnter) ? tBottom0 : tTop1;
    if (tExit <= tEnter)
    {
        return 0.0;
    }

    // Середина пути внутри слоя: берём её, а не вход, чтобы высота не
    // попадала на самый край оболочки, где перья ещё не разрослись.
    float3 bodyPoint = (bodyCenterWS + (dir * (0.5 * (tEnter + tExit))));
    float radius = length(bodyPoint - bodyCenterWS);
    float heightFraction = saturate((radius - radiusBottom) / max(1.0, radiusTop - radiusBottom));
    float bottomFeather = smoothstep(0.0, max(0.001, _CldBottomFeather), heightFraction);
    float topFeather = 1.0 - smoothstep(1.0 - max(0.001, _CldTopFeather), 1.0, heightFraction);
    return smoothstep(0.08, 0.78, SampleCloudEarthMask(bodyPoint))
        * saturate(_CldCoverage * 1.35) * bottomFeather * topFeather;
}

/// <summary>
/// Небесная яркость в направлении dir для окна Снелла.
///
/// Состав: градиент горизонт→зенит из физического ambient неба, гало и диск
/// Солнца, облачный слой вдоль луча, ночью — звёзды. Солнце и Луна гасятся
/// солнечным фактором, который считает вызывающий: когда солнце ушло под
/// горизонт, диска быть не должно, а не только потому, что он «сам погас»
/// по dot — ночью dot вверх всё равно отрицателен только если солнце низко,
/// а в сумерках при 1° над горизонтом картинка ещё светлая.
/// </summary>
float3 GalilegoSkyRadiance(float3 dir, float3 sunDir, float normalSigmaAngle, float sunFactor,
    float3 bodyCenterWS, float cloudRadiusBottom, float cloudRadiusTop)
{
    // Градиент: горизонт теплее и ярче (длинный путь рэлеевского рассеяния),
    // зенит холоднее. Обе краски — из одного ambient-цвета неба, поэтому
    // закат и ночь красятся сами собой, без отдельной ветки.
    float upness = saturate(dot(dir, _SkyUp));
    float3 horizonTint = _SkyAmbientColor * float3(1.22, 1.06, 0.90);
    float3 zenithTint = _SkyAmbientColor * float3(0.70, 0.90, 1.28);
    float3 sky = lerp(horizonTint, zenithTint, pow(upness, 0.45)) * (_SkyAmbient * SkyBaseGain);

    if (sunFactor > 0.001)
    {
        sky += GalilegoSunAureole(dir, sunDir, _SunLightColor * (0.05 * sunFactor), normalSigmaAngle);
        sky += GalilegoSunDisc(dir, sunDir, _SunLightColor * (22.0 * sunFactor), normalSigmaAngle);
    }

    // Облака. Одна выборка поля вдоль луча, только если луч реально входит в
    // оболочку (в зеркальной зоне небо всё равно не видно, и платить за
    // raymarch там незачем).
    if (_CldCloudActive > 0.5 && _CldCoverage > 0.001 && upness > 0.02)
    {
        float heightFraction = CloudShellCoverage(dir, bodyCenterWS, cloudRadiusBottom, cloudRadiusTop);
        sky = lerp(sky, sky * 0.82 + (_SkyAmbientColor * (_SkyAmbient * SkyBaseGain * 0.75)), heightFraction);
    }

    if (_StarVisibility > 0.01)
    {
        float3 night = 0.0;
        float2 bright = GalilegoStarLayer(dir, 160.0, 0.93, 0.10, 0.0);
        night += lerp(float3(1.0, 0.78, 0.58), float3(0.74, 0.84, 1.0), bright.y) * bright.x * 4.0;
        float2 faint = GalilegoStarLayer(dir, 420.0, 0.86, 0.08, 3.3);
        night += lerp(float3(1.0, 0.78, 0.58), float3(0.74, 0.84, 1.0), faint.y) * faint.x * 0.9;
        sky += night * (_StarVisibility * 0.35);
        sky += GalilegoMoonRadiance(dir, normalSigmaAngle);
    }

    return max(sky, 0.0);
}


#endif
