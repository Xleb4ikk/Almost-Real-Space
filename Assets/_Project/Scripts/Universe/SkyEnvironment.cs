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
    public sealed class SkyEnvironment : MonoBehaviour
    {
        /// <summary>Линейный цвет фотосферы (5772 K) — единый источник для света, диска и неба.</summary>
        public static readonly Vector3d PhotosphereLinear = StarColorUtil.FromTemperature(5772d);

        /// <summary>Светимость фотосферы: нормировка света, чтобы тон звезды не съедал яркость.</summary>
        private static readonly double PhotosphereLuminance =
            (0.2126d * PhotosphereLinear.X) + (0.7152d * PhotosphereLinear.Y) + (0.0722d * PhotosphereLinear.Z);

        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Directional Light солнца (гасится/краснится по физике).")]
        public Light SunLight;

        [Tooltip("Базовая интенсивность света днём в зените (люкс, HDRP physical units): ~80000 — яркий день.")]
        public float SunLightDayLux = 80000f;

        [Tooltip("Диагностика заката: раз в 2 с писать высоту солнца, T, ambient, день/ночь.")]
        public bool SunsetDiagLog;

        [Tooltip("Множитель видимости звёзд (k в exp(−L·k)): больше — звёзды прячутся раньше.")]
        public float StarVisibilityK = 6f;

        [Tooltip("Сила солнечного члена террейна. Честный член (может превышать 1), пересвет разруливает тонмаппинг HDRP. Калибровать вместе с TerrainRadianceScale; старт 1.")]
        public float TerrainSunIntensity = 1f;

        [Tooltip("HDR-буст земли (аналог ATM_RADIANCE_SCALE неба = 5): прямой свет и ambient террейна/декора. 1 = как раньше; подбирать глазами (ориентир 2–4).")]
        public float TerrainRadianceScale = 1f;

        [Tooltip("Ночная засветка террейна (звёздный свет + NightExposure глаза); 0 = кромешная тьма.")]
        public float NightAmbient = 0.01f;

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
        public float ShadowHighDistanceMeters = 150f;

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
            // [ГРАФИКА] Максимум разрешения теней: в сцене у солнца стоит ручной
            // override 512 — поднимаем до 4096. Пресеты (GraphicsQualityController)
            // позже будут задавать это сами.
            var hdLight = SunLight != null
                ? SunLight.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>()
                : null;
            if (hdLight != null)
            {
                hdLight.SetShadowResolutionOverride(true);
                hdLight.SetShadowResolution(4096);
            }
        }

        private void LateUpdate()
        {
            if (Runner == null || Runner.SystemState == null || Runner.DominantBody == null
                || Runner.SystemState.Root == null || Runner.Ship == null)
            {
                return;
            }

            OrbitingBody body = Runner.DominantBody;
            AtmosphereProfile atmosphere = body.Atmosphere;

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d relative = Runner.PlayerPosition - bodyPos;
            double distance = relative.Magnitude;
            double altitude = System.Math.Max(0d, distance - body.Radius);
            Vector3d up = distance > 0d ? relative / distance : new Vector3d(0d, 0d, 1d);

            Runner.SystemState.Root.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPos, out _);
            Vector3d toStar = starPos - Runner.PlayerPosition;
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

            if (SunLight != null)
            {
                // Directional Light в физических люксах (HDRP): яркость — по
                // светимости прямого света, оттенок — фотосфера × T. Днём у
                // земли ~0.9·DayLux, на горизонте — красные тысячи, ночью 0.
                // Старое SunLightIntensity=1.6 в люксах гасило солнце в 50000 раз.
                double brightness = Luminance(sunLight);
                double peak = System.Math.Max(sunLight.X, System.Math.Max(sunLight.Y, sunLight.Z));
                if (peak > 1e-6d && brightness > 0d)
                {
                    SunLight.color = new Color(
                        (float)(sunLight.X / peak), (float)(sunLight.Y / peak), (float)(sunLight.Z / peak), 1f);
                    SunLight.intensity = SunLightDayLux * (float)(brightness / 1d);
                }
                else
                {
                    SunLight.color = new Color(0f, 0f, 0f, 1f);
                    SunLight.intensity = 0f;
                }
            }

            if (SunsetDiagLog && Runner.TimeSeconds >= nextDiagTime)
            {
                nextDiagTime = Runner.TimeSeconds + 2d;
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
        }
    }
}
