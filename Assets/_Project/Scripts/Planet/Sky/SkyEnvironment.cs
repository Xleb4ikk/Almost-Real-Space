using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Физика неба в рантайме: считает прозрачность к солнцу, окклюзию телом,
    /// дневной фактор и видимость звёзд для наблюдателя и кладёт их в глобалы
    /// шейдеров, плюс красит Directional Light по физике (день→закат→ночь).
    ///
    /// Прозрачность к Солнцу берётся из AtmosphereOptics.TransmittanceToSpace —
    /// той же функции, что строит transmittance-LUT GPU-неба. Это единая модель
    /// по фактической геометрии планеты (радиус, верх атмосферы, высотные
    /// профили β), поэтому диск солнца, цвет прямого света и небо не расходятся
    /// на закате. Эмпирический SkyPhysics.SunTransmittance (Kasten–Young для
    /// радиуса Земли) остаётся только в тестах: на маленькой Терре он давал в
    /// разы более раннее и сильное покраснение, чем GPU-небо, а ниже −1.6°
    /// воздушная масса становилась отрицательной (T > 1).
    ///
    /// Вызов — LateUpdate; потребители (SunBillboard, StarField) читают
    /// вычисленные значения того же/предыдущего кадра.
    /// </summary>
    [ExecuteAlways]
    public sealed class SkyEnvironment : MonoBehaviour
    {
        /// <summary>Линейный цвет фотосферы (5772 K) — единый источник для света, диска и неба.</summary>
        public static readonly Vector3d PhotosphereLinear = StarColorUtil.FromTemperature(5772d);

        /// <summary>Светимость фотосферы: нормировка света, чтобы тон звезды не съедал яркость.</summary>
        private static readonly double PhotosphereLuminance =
            (0.2126d * PhotosphereLinear.X) + (0.7152d * PhotosphereLinear.Y) + (0.0722d * PhotosphereLinear.Z);

        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Время симуляции для превью освещения в Scene view (вне Play).")]
        public double EditorTimeSeconds = 0d;

        /// <summary>Направление НА солнце в координатах симуляции (то же, что _TerrainSunDir).</summary>
        public Vector3 LastSunDirectionSim { get; private set; }

        [Tooltip("Directional Light солнца (гасится/краснится по физике).")]
        public Light SunLight;

        [Tooltip("Directional Light без теней: заполняющий ambient неба для обычных HDRP-материалов (здания, пропсы). Пусто — только солнце.")]
        public Light SkyFillLight;

        [Tooltip("Множитель заполняющего света. Старт 2–3, подбирать глазами.")]
        public float FillLightScale = 2f;

        [Tooltip("Физический ориентир дня (люкс, HDRP physical units): ~80000 — яркий день. В intensities света НЕ участвует: сцена считает свет в искусственном масштабе (см. TerrainRadianceScale), и Directional Light питает только объекты на обычных HDRP-материалах — его ярность задаёт MaterialLightScale.")]
        public float SunLightDayLux = 80000f;

        [Tooltip("Множитель ярности Directional Light для обычных HDRP-материалов (здания, пропсы). 1 = свет ровно в масштабе сцены, >1 = пересвет. Небо, рельеф и декор свет считают сами (_SunLightColor/_TerrainSun/_TerrainRadianceScale) и живут в масштабе albedo·ndl·TerrainRadianceScale; у HDRP/Lit диффуз = albedo·E·ndl/π, поэтому масштаб сцены — E = π·TerrainRadianceScale·TerrainSunIntensity. При E = 80000 (сырые люксы) белая стена выходила ~21 000 и уходила в чистый белый с ореолом от bloom, пока рельеф в метре от неё оставался тёмным.")]
        public float MaterialLightScale = 1f;

        [Tooltip("Диагностика заката: раз в 2 с писать высоту солнца, T, ambient, день/ночь.")]
        public bool SunsetDiagLog;

        [Tooltip("Множитель видимости звёзд (k в exp(−L·k)): больше — звёзды прячутся раньше.")]
        public float StarVisibilityK = 6f;

        [Tooltip("Сила солнечного члена террейна. Честный член (может превышать 1), пересвет разруливает тонмаппинг HDRP. Калибровать вместе с TerrainRadianceScale; старт 1.")]
        public float TerrainSunIntensity = 1f;

        [Tooltip("HDR-буст земли (аналог ATM_RADIANCE_SCALE неба = 5): прямой свет и ambient террейна/декора. 1 = как раньше; подбирать глазами (ориентир 2–4).")]
        public float TerrainRadianceScale = 1f;

        [Tooltip("Ночная засветка террейна (звёздный свет + NightExposure глаза); 0 = кромешная тьма.")]
        public float NightAmbient = 0f;

        [Tooltip("Дневная засветка террейна небом.")]
        public float SkyAmbient = 0.1f;

        [Tooltip("Сила тени HDRP 0..1: 1 — тень гасит прямой свет полностью (чёрно), 0 — тени нет.")]
        public float ShadowStrength = 0.85f;

        [Tooltip("Volume с Exposure для компенсации глаза (Sky and Fog Volume). Пусто — без компенсации.")]
        public UnityEngine.Rendering.Volume ExposureVolume;

        [Tooltip("Макс. подъём экспозиции в сумерках (EV): глаз раскрывается при падении освещённости.")]
        public float TwilightExposureMax = 2f;

        [Tooltip("Пол компенсации ночью (EV): ночь остаётся ночью, звёздный свет читается.")]
        public float NightExposure = 0.5f;

        [Tooltip("До этой дистанции тени фильтруются штатным HQ-фильтром HDRP (PCSS).")]
        public float ShadowHighDistanceMeters = 70f;

        [Tooltip("До этой дистанции — средний фильтр (GATHER, 4 taps); дальше самый дешёвый (1 tap).")]
        public float ShadowMediumDistanceMeters = 400f;

        [Tooltip("Ширина переходной зоны между тирами фильтрации (м): выбор тира — dither, без двойной стоимости.")]
        public float ShadowBlendWidthMeters = 25f;

        /// <summary>Прозрачность к солнцу по каналам (обновляется каждый кадр).</summary>
        public Vector3 Transmittance { get; private set; } = Vector3.one;

        /// <summary>Дневной фактор прямого света 0..1.</summary>
        public float DayFactor { get; private set; }

        /// <summary>Высота солнца над локальным горизонтом, градусы (диагностика).</summary>
        public float SunElevationDeg { get; private set; }

        /// <summary>Ambient-отношение к опорному дню 0..2 (диагностика).</summary>
        public float AmbientAmount { get; private set; }

        /// <summary>Текущая компенсация экспозиции глаза, EV (диагностика).</summary>
        public float ExposureCompensation { get; private set; }

        /// <summary>Окклюзия солнца телом (0/1, сглажено).</summary>
        public float SunOcclusion { get; private set; }

        /// <summary>Видимость звёзд 0..1.</summary>
        public float StarVisibility { get; private set; } = 1f;

        // Кэш опорной яркости неба (день, солнце в зените, уровень моря) для
        // нормировки ambient: _SkyAmbient сохраняет смысл «сколько света» при
        // любом профиле (плотная/разреженная атмосфера, другая высота слоя).
        private bool ambientRefValid;
        private AtmosphereOptics.Coefficients ambientRefCoeff;
        private double ambientRefRadius;
        private double ambientRefTop;
        private double ambientRefValue;
        private double nextDiagTime;

        private static double Luminance(in Vector3d c) =>
            (0.2126d * c.X) + (0.7152d * c.Y) + (0.0722d * c.Z);

        private double AmbientReferenceLuminance(in AtmosphereOptics.Coefficients coeff, double planetRadius, double topAltitude)
        {
            if (!ambientRefValid
                || ambientRefRadius != planetRadius
                || ambientRefTop != topAltitude
                || !CoefficientsEqual(ambientRefCoeff, coeff))
            {
                Vector3d reference = AtmosphereOptics.SkyAmbientRadiance(
                    coeff, planetRadius, planetRadius + topAltitude, planetRadius, 1d);
                ambientRefValue = Luminance(reference);
                ambientRefCoeff = coeff;
                ambientRefRadius = planetRadius;
                ambientRefTop = topAltitude;
                ambientRefValid = true;
            }

            return ambientRefValue;
        }

        private static bool CoefficientsEqual(in AtmosphereOptics.Coefficients a, in AtmosphereOptics.Coefficients b)
        {
            return a.RayleighScattering.X == b.RayleighScattering.X
                && a.RayleighScattering.Y == b.RayleighScattering.Y
                && a.RayleighScattering.Z == b.RayleighScattering.Z
                && a.MieScattering.X == b.MieScattering.X
                && a.MieScattering.Y == b.MieScattering.Y
                && a.MieScattering.Z == b.MieScattering.Z
                && a.OzoneAbsorption.X == b.OzoneAbsorption.X
                && a.OzoneAbsorption.Y == b.OzoneAbsorption.Y
                && a.OzoneAbsorption.Z == b.OzoneAbsorption.Z
                && a.RayleighScaleHeight == b.RayleighScaleHeight
                && a.MieScaleHeight == b.MieScaleHeight
                && a.OzoneCenterAltitude == b.OzoneCenterAltitude
                && a.OzoneHalfWidth == b.OzoneHalfWidth
                && a.MieAnisotropy == b.MieAnisotropy
                && a.GroundAlbedo == b.GroundAlbedo
                && a.OzoneEnabled == b.OzoneEnabled;
        }

        private void Start()
        {
            // [ГРАФИКА] Разрешение теней солнца задаётся HDRP-ассетом
            // (Max Directional Shadow Map Resolution), а не этим кодом: ручной
            // override здесь перебивал бы и ассет, и пресеты
            // (GraphicsQualityController). В сцене стоял override 512 — снимаем.
            var hdLight = SunLight != null
                ? SunLight.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>()
                : null;
            if (hdLight != null)
            {
                hdLight.SetShadowResolutionOverride(false);
            }
        }

        public static OrbitingBody RootOf(OrbitingBody body)
        {
            OrbitingBody current = body;
            int guard = 0;
            while (current != null && current.Parent != null && guard++ < 64)
            {
                current = current.Parent;
            }

            return current;
        }

        private OrbitingBody editLastBody;
        private Vector3d editLastObserver;
        private double editLastTime = double.NaN;
        private float editNextSyncTime;

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                EditorSyncGlobals();
                return;
            }

            if (Runner == null || Runner.SystemState == null || Runner.DominantBody == null
                || Runner.SystemState.Root == null || Runner.Ship == null)
            {
                return;
            }

            ApplyGlobalsNow(Runner.DominantBody, Runner.PlayerPosition, Runner.TimeSeconds);
        }

        /// <summary>
        /// Вне Play глобалы ставит только этот метод. Вызов ApplyGlobalsNow
        /// тяжёлый (интеграл атмосферы), поэтому не каждый кадр: только при смене
        /// тела или точки наблюдения и не чаще раза в 0.5 с.
        /// </summary>
        private void EditorSyncGlobals()
        {
            if (Time.realtimeSinceStartup < editNextSyncTime)
            {
                return;
            }

            editNextSyncTime = Time.realtimeSinceStartup + 0.5f;

            SurfaceFrame frame = SurfaceFrame.ActiveEditFrame() ?? FindAnyObjectByType<SurfaceFrame>();
            if (frame == null || !frame.IsUsable)
            {
                return;
            }

            Vector3d observer = frame.AnchorAstro;
            bool same = ReferenceEquals(frame.BodyState, editLastBody)
                && (observer - editLastObserver).Magnitude < 1d
                && editLastTime == EditorTimeSeconds;
            if (same)
            {
                return;
            }

            editLastBody = frame.BodyState;
            editLastObserver = observer;
            editLastTime = EditorTimeSeconds;

            // Свет как в Play: цвет и яркость Sun, а не только глобалы шейдеров.
            ApplyGlobalsNow(frame.BodyState, observer, EditorTimeSeconds, applyToLight: true);

            // Тень HDRP идёт от поворота Sun. В Play его ставит SunBillboard,
            // в редакторе это делает этот код. Иначе шейдер и тень используют
            // разное солнце, и границы каскадов видны как чёрная сфера вокруг
            // камеры.
            if (SunLight != null && LastSunDirectionSim.sqrMagnitude > 0.5f)
            {
                SunLight.transform.rotation = Quaternion.LookRotation(-LastSunDirectionSim);
            }
        }

        /// <summary>
        /// Пересчитать и выставить все глобалы освещения для произвольного
        /// наблюдателя. Вынесено из LateUpdate, чтобы редакторское превью
        /// поверхности получало РОВНО тот же свет, что и игра, не запуская Play.
        /// Единственный источник правды: своя копия этой физики в редакторе
        /// разошлась бы с игровой на первом же изменении коэффициентов.
        ///
        /// applyToLight — трогать ли Directional Light сцены. Для игры да,
        /// для редакторского превью нет: превью нужно только заполнить глобалы
        /// шейдеров, а запись в SunLight засоряла бы сцену и меняла свет у
        /// автора между правками.
        /// </summary>
        public void ApplyGlobalsNow(OrbitingBody body, Vector3d observerPosition, double timeSeconds, bool applyToLight = true)
        {
            if (body == null)
            {
                return;
            }

            AtmosphereProfile atmosphere = body.Atmosphere;

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPos, out _);
            Vector3d relative = observerPosition - bodyPos;
            double distance = relative.Magnitude;
            double altitude = System.Math.Max(0d, distance - body.Radius);
            Vector3d up = distance > 0d ? relative / distance : new Vector3d(0d, 0d, 1d);

            // Корень дерева тел ищем по Parent, а НЕ через Runner.SystemState.
            // Раньше здесь стояло Runner.SystemState.Root — и как только метод
            // начали звать из редактора (превью поверхности), он падал на null
            // 999 раз в секунду: вне Play у раннера SystemState не собран.
            // По той же причине все ссылки на Runner внутри метода недопустимы.
            OrbitingBody root = RootOf(body);
            if (root == null)
            {
                return;
            }

            root.EvaluateWorldState(timeSeconds, out Vector3d starPos, out _);
            Vector3d toStar = starPos - observerPosition;
            double starDistance = toStar.Magnitude;
            Vector3d sunDir = starDistance > 0d ? toStar / starDistance : up;

            double sinEl = Vector3d.Dot(sunDir, up);

            // Единый источник β: те же коэффициенты, что у GPU-купола
            // (AtmosphereOptics). Раньше здесь был эмпирический ExtinctionPerMeter —
            // небо и свет Солнца считались по разным моделям и расходились.
            AtmosphereOptics.Coefficients coeff = atmosphere != null
                ? atmosphere.ToOptics()
                : AtmosphereOptics.FromProfile(0d, 0d, 0d, 0d, false);
            double scaleHeight = coeff.RayleighScaleHeight;
            double topAltitude = atmosphere != null ? atmosphere.TopAltitudeMeters : 0d;
            double density = SkyPhysics.Density(altitude, scaleHeight, topAltitude);

            // Единая прозрачность: тот же интеграл по той же геометрии, что у
            // transmittance-LUT GPU-неба (AtmosphereOptics.TransmittanceToSpace).
            // В вакууме/выше атмосферы — ровно (1,1,1).
            Vector3d transmittance;
            if (atmosphere == null || topAltitude <= 0d || altitude >= topAltitude)
            {
                transmittance = new Vector3d(1d, 1d, 1d);
            }
            else
            {
                transmittance = AtmosphereOptics.TransmittanceToSpace(
                    coeff, body.Radius, body.Radius + topAltitude, body.Radius + altitude, sinEl);
            }

            double occlusion = SkyPhysics.SunOcclusion(sinEl, altitude, body.Radius);
            double day = SkyPhysics.DayFactor(sinEl);
            double skyLuminance = SkyPhysics.SkyLuminance(sinEl, density);
            double starVisibility = SkyPhysics.StarVisibility(skyLuminance, StarVisibilityK);

            Transmittance = new Vector3((float)transmittance.X, (float)transmittance.Y, (float)transmittance.Z);
            DayFactor = (float)day;
            SunOcclusion = (float)occlusion;
            StarVisibility = (float)starVisibility;
            SunElevationDeg = (float)(System.Math.Asin(SkyPhysics.Clamp(sinEl, -1d, 1d)) * (180d / System.Math.PI));

            Shader.SetGlobalVector("_SunTransmittance", Transmittance);
            Shader.SetGlobalFloat("_StarVisibility", StarVisibility);
            Shader.SetGlobalVector("_SkySunDir", AstroFrame.ToSimulation(sunDir));
            Shader.SetGlobalVector("_SkyUp", AstroFrame.ToSimulation(up));

            // Цвет прямого солнечного света для шейдеров поверхности: фотосфера ×
            // прозрачность атмосферы. В космосе T=(1,1,1) — белый; у земли синий
            // канал гаснет первым — тёплый жёлтый; на закате остаётся красный.
            // По высоте меняется плавно: T — интеграл плотности над наблюдателем.
            // Нормируем на светимость фотосферы: при T=1 свет ровно единичной
            // яркости (как прежний белый), тёплый тон звезды ничего не затемняет,
            // а атмосфера только красит и гасит.
            Vector3d sunLight = AtmosphereOptics.VecMul(PhotosphereLinear, transmittance) / PhotosphereLuminance;
            Shader.SetGlobalVector("_SunLightColor",
                new Vector3((float)sunLight.X, (float)sunLight.Y, (float)sunLight.Z));

            // Направление на звезду для планарного шейдера рельефа. Тот же
            // глобал, что ставит SunBillboard в рантайме: превью и игра должны
            // светиться с одного угла, иначе склоны читаются противоположно.
            LastSunDirectionSim = AstroFrame.ToSimulation(sunDir);
            Shader.SetGlobalVector("_TerrainSunDir", LastSunDirectionSim);

            Shader.SetGlobalVector("_TerrainBodyCenterWS", FloatingOrigin.ToRender(bodyPos));

            // Ambient террейна — средняя яркость неба над наблюдателем
            // (полусферический интеграл single-scatter, та же физика, что у
            // GPU-неба). В отличие от тинта×skyLuminance, он НЕ гаснет на
            // терминаторе: пока небо светится (сумерки), земля получает
            // рассеянный свет — иначе при ярком небе земля была чёрной.
            // Нормируем на опорное значение (день, зенит): _SkyAmbient остаётся
            // «сколько света», а цвет/затухание — из физики. Вне атмосферы —
            // 0, остаётся _NightAmbient. От позиции наблюдателя не зависит —
            // ночная сторона темна всегда.
            Vector3d ambientRadiance = Vector3d.Zero;
            double ambientAmount = 0d;
            Vector3d ambientHue = new Vector3d(1d, 1d, 1d);
            if (atmosphere != null && topAltitude > 0d && altitude < topAltitude)
            {
                ambientRadiance = AtmosphereOptics.SkyAmbientRadiance(
                    coeff, body.Radius, body.Radius + topAltitude, body.Radius + altitude, sinEl);
                double ambientLuminance = Luminance(ambientRadiance);
                double referenceLuminance = AmbientReferenceLuminance(coeff, body.Radius, topAltitude);
                if (ambientLuminance > 0d && referenceLuminance > 1e-12d)
                {
                    ambientAmount = System.Math.Min(ambientLuminance / referenceLuminance, 2d);
                    ambientHue = ambientRadiance / ambientLuminance;
                }
            }

            AmbientAmount = (float)ambientAmount;

            Color ambientColor = new Color(
                (float)(ambientHue.X * ambientAmount),
                (float)(ambientHue.Y * ambientAmount),
                (float)(ambientHue.Z * ambientAmount),
                1f);
            Shader.SetGlobalColor("_SkyAmbientColor", ambientColor);
            Shader.SetGlobalFloat("_NightAmbient", NightAmbient);
            Shader.SetGlobalFloat("_SkyAmbient", SkyAmbient);
            Shader.SetGlobalFloat("_TerrainSun", TerrainSunIntensity);
            Shader.SetGlobalFloat("_TerrainRadianceScale", TerrainRadianceScale);
            // В сумерках тени раскрываем (иначе контровый свет даёт pure black):
            // днём полная сила, ниже −5° остаётся треть.
            double shadowTwilight = 0.35d + (0.65d * SkyPhysics.Smoothstep(-0.08d, 0.12d, sinEl));
            Shader.SetGlobalFloat("_ShadowStrength", Mathf.Clamp01(ShadowStrength * (float)shadowTwilight));
            Shader.SetGlobalFloat("_ShadowHighDistance", Mathf.Max(0f, ShadowHighDistanceMeters));
            Shader.SetGlobalFloat("_ShadowMediumDistance", Mathf.Max(0f, ShadowMediumDistanceMeters));
            Shader.SetGlobalFloat("_ShadowBlendWidth", Mathf.Max(1f, ShadowBlendWidthMeters));

            if (applyToLight && SunLight != null)
            {
                // Directional Light — свет для объектов на обычных HDRP-материалах.
                // Небо/рельеф/декор считают свет сами (глобалы _SunLightColor,
                // _TerrainSun, _TerrainRadianceScale) и живут в искусственном
                // масштабе «albedo·ndl», где полуденное солнце даёт
                // TerrainRadianceScale. У HDRP/Lit диффузный член = albedo·E·ndl/π,
                // поэтому тот же масштаб — это E = π·TerrainRadianceScale·
                // TerrainSunIntensity (при 2.5 и 1 это ≈7.85). Светить сюда
                // сырыми SunLightDayLux нельзя: 80 000 — это физические люксы,
                // на 4 порядка больше масштаба сцены, и белый фасад уходил в
                // чистый белый, раздувая bloom, при тёмном рельефе рядом.
                //
                // Цвет — по-прежнему физика: фотосфера × прозрачность атмосферы,
                // оттенок тот же, что у неба, день/ночь — та же яркость.
                //
                // applyToLight=false для редакторского превью: там свет нужен
                // только шейдерам, а трогать SunLight сцены нельзя — превью
                // засоряло бы сцену и меняло бы свет у автора между правками.
                double brightness = Luminance(sunLight);
                double peak = System.Math.Max(sunLight.X, System.Math.Max(sunLight.Y, sunLight.Z));
                if (peak > 1e-6d && brightness > 0d)
                {
                    SunLight.color = new Color(
                        (float)(sunLight.X / peak), (float)(sunLight.Y / peak), (float)(sunLight.Z / peak), 1f);
                    SunLight.intensity = (float)(brightness
                        * System.Math.PI * TerrainRadianceScale * TerrainSunIntensity * MaterialLightScale);
                }
                else
                {
                    SunLight.color = new Color(0f, 0f, 0f, 1f);
                    SunLight.intensity = 0f;
                }
            }

            // Заполняющий свет неба для обычных HDRP-материалов. _SkyAmbientColor
            // читают только кастомные шейдеры рельефа и декора — здание на
            // HDRP/Lit в стороне без прямого солнца остаётся чёрным. Этот
            // Directional Light берёт цвет и яркость из того же ambient, что и
            // рельеф, и светит сбоку-снизу с противоположной от солнца стороны.
            if (applyToLight && SkyFillLight != null)
            {
                Vector3 sunW = AstroFrame.ToSimulation(sunDir);
                Vector3 upW = AstroFrame.ToSimulation(up);
                Vector3 side = Vector3.ProjectOnPlane(-sunW, upW);
                if (side.sqrMagnitude < 1e-8f)
                {
                    side = Vector3.Cross(upW, Vector3.right);
                }

                if (side.sqrMagnitude > 1e-8f)
                {
                    Vector3 from = (side.normalized + (upW * 0.6f)).normalized;
                    SkyFillLight.transform.rotation = Quaternion.LookRotation(-from, upW);
                }

                float peak = Mathf.Max(ambientColor.r, Mathf.Max(ambientColor.g, ambientColor.b));
                if (peak > 1e-5f)
                {
                    SkyFillLight.color = new Color(
                        ambientColor.r / peak, ambientColor.g / peak, ambientColor.b / peak, 1f);
                    SkyFillLight.intensity =
                        AmbientAmount * Mathf.PI * SkyAmbient * TerrainRadianceScale * FillLightScale;
                }
                else
                {
                    SkyFillLight.intensity = 0f;
                }
            }

            if (SunsetDiagLog && Application.isPlaying && timeSeconds >= nextDiagTime)
            {
                nextDiagTime = timeSeconds + 2d;
                Debug.Log(string.Format(
                    "[SkyEnvironment][SUNSET] el={0:F1}° T=({1:F3},{2:F3},{3:F3}) ambient={4:F3} day={5:F2} occ={6:F2} star={7:F2} ev={8:F2}",
                    SunElevationDeg, Transmittance.x, Transmittance.y, Transmittance.z,
                    AmbientAmount, DayFactor, SunOcclusion, StarVisibility, ExposureCompensation));
            }

            // Глаз-адаптация: компенсация экспозиции по физической освещённости.
            // Детерминирована (не зависит от содержимого кадра): histogram-метр
            // по яркому небу душил бы землю в контровом свете. Днём 0,
            // в сумерках до TwilightExposureMax, ночью — пол NightExposure.
            double directLum = Luminance(sunLight);
            double lightLevel = SkyPhysics.Clamp((0.65d * directLum) + (0.35d * ambientAmount), 0d, 1d);
            double exposureComp;
            if (ambientAmount < 0.03d && directLum < 0.02d)
            {
                exposureComp = NightExposure;
            }
            else
            {
                exposureComp = TwilightExposureMax * (1d - SkyPhysics.Smoothstep(0.15d, 1d, lightLevel));
            }

            ExposureCompensation = (float)exposureComp;
            if (ExposureVolume != null && ExposureVolume.profile != null
                && ExposureVolume.profile.TryGet<UnityEngine.Rendering.HighDefinition.Exposure>(out var exposure))
            {
                exposure.compensation.Override((float)exposureComp);
            }

            // Настоящий ambient полусферы неба для обычных HDRP-материалов.
            // Один Directional Light даёт только одну освещённую сторону, а
            // небо светит со всех сторон сразу — поэтому отдаём HDRP сам
            // Gradient Sky и подмешиваем в него цвета отсюда. HDRP сворачивает
            // купол в SH (AmbientProbeConvolution) и без APV отдаёт его
            // материалам через EvaluateAmbientProbe, то есть стены получают
            // освещение по нормали без единого дополнительного источника.
            //
            // Масштаб — тот же, что у рельефа (albedo · ambient · SkyAmbient ·
            // TerrainRadianceScale), иначе здание и земля разойдутся по яркости.
            // Фон камеры при этом не меняется: StarFieldView ставит clearColor
            // в чёрный, а небо рисует атмосфера, поэтому Gradient Sky виден
            // только как источник ambient/отражений, а не как картинка.
            if (ExposureVolume != null && ExposureVolume.profile != null
                && ExposureVolume.profile.TryGet<UnityEngine.Rendering.HighDefinition.GradientSky>(out var grad))
            {
                float k = SkyAmbient * TerrainRadianceScale;
                Color sky = new Color(ambientColor.r * k, ambientColor.g * k, ambientColor.b * k, 1f);

                // Отражённый от земли свет: зависит от высоты солнца, поэтому
                // гаснет вместе с прямым светом и не светит ночью.
                float bounce = DayFactor * (float)directLum * TerrainRadianceScale * TerrainSunIntensity * 0.15f;
                Color ground = new Color(0.20f * bounce, 0.30f * bounce, 0.12f * bounce, 1f);

                grad.top.Override(sky);
                grad.middle.Override(sky * 0.65f);
                grad.bottom.Override(ground);
                grad.multiplier.Override(1f);
            }
        }
    }
}
