using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Runtime sky physics: computes transmittance to the sun, body occlusion,
    /// day factor and star visibility for the observer and puts them into shader
    /// globals, plus colors the Directional Light physically (day→sunset→night).
    ///
    /// Transmittance to the Sun comes from AtmosphereOptics.TransmittanceToSpace —
    /// the same function that builds the GPU sky's transmittance LUT. This is a
    /// single model over the actual planet geometry (radius, top of atmosphere,
    /// vertical β profiles), so the sun disc, the direct light color and the sky
    /// do not diverge at sunset. SkyPhysics.SunTransmittance is only used in tests.
    ///
    /// Called from LateUpdate; consumers (StarRenderer, StarField) read the values
    /// computed this or the previous frame.
    /// </summary>
    [ExecuteAlways]
    public sealed class SkyEnvironment : MonoBehaviour
    {
        /// <summary>Linear photosphere color (5772 K) — single source for the light, disc and sky.</summary>
        public static readonly Vector3d PhotosphereLinear = StarColorUtil.FromTemperature(5772d);

        /// <summary>Photosphere luminance: light normalization so the star's hue does not eat brightness.</summary>
        private static readonly double PhotosphereLuminance =
            (0.2126d * PhotosphereLinear.X) + (0.7152d * PhotosphereLinear.Y) + (0.0722d * PhotosphereLinear.Z);

        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Время симуляции для превью освещения в Scene view (вне Play).")]
        public double EditorTimeSeconds = 0d;

        /// <summary>Direction TO the sun in simulation coordinates (same as _TerrainSunDir).</summary>
        public Vector3 LastSunDirectionSim { get; private set; }

        [Tooltip("Directional Light солнца (гасится/краснится по физике).")]
        public Light SunLight;

        [Tooltip("Физический рендер звезды. Если назначен, цвет и освещённость Directional Light берутся из StarPhysics " +
                 "(масса → L, Teff, E). Пусто — прежняя калибровка фотосферы 5772 K.")]
        public StarRenderer Star;

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

        [Tooltip("Сила солнечного члена террейна. Честный член (может превышать 1), пересвет разруливает тонмаппинг HDRP. Калибровать вместе с TerrainRadianceScale; старт 1. Итоговый _TerrainSun = это поле × StarRenderer.SceneLightScale, т.е. следует L/d² звезды.")]
        public float TerrainSunIntensity = 1f;

        [Tooltip("HDR-буст земли (аналог ATM_RADIANCE_SCALE неба = 5): прямой свет и ambient террейна/декора. 1 = как раньше; подбирать глазами (ориентир 2–4).")]
        public float TerrainRadianceScale = 1f;

        [Tooltip("Ночная засветка террейна (звёздный свет + NightExposure глаза); 0 = кромешная тьма.")]
        public float NightAmbient = 0f;

        [Tooltip("Дневная засветка террейна небом.")]
        public float SkyAmbient = 0.1f;

        [Tooltip("Сила тени HDRP 0..1: 1 — тень гасит прямой свет полностью (чёрно), 0 — тени нет.")]
        public float ShadowStrength = 0.85f;

        [Tooltip("Volume с HDRP-настройками неба: отсюда SkyEnvironment берёт GradientSky " +
                 "(ambient/отражения), и в нём же лежит Exposure. ВНИМАНИЕ: Exposure двигает " +
                 "ТОЛЬКО HDRP/Lit-объекты (пандус/дороги/VAB/корабль): их материал умножает " +
                 "pre-exposure, который uber-post в режиме Automatic не отменяет. Кастомные " +
                 "шейдеры (рельеф, небо, звезда) экспозицию не читают вовсе. Поэтому зрачок " +
                 "живёт в свете сцены (_TerrainSun / Directional Light / _EyeGain), а " +
                 "Exposure.limitMin в StarRenderer держится на -1 EV: с -8 автоэкспозиция " +
                 "ночью поднимала асфальт до 118/255 (замер 2026-11-24).")]
        public UnityEngine.Rendering.Volume ExposureVolume;

        [Header("Зрачок")]
        [Tooltip("Доля физического дефицита света, которую компенсирует глаз (0 = чистый фотометр: " +
                 "Марс снова чёрный; 1 = полностью адаптированный глаз: полдень Марса как полдень Терры). " +
                 "0.66 — зрачок открылся на две трети: тусклый, но читаемый день.")]
        [Range(0f, 1f)]
        public float EyeAdaptationShare = 0.66f;

        [Tooltip("Потолок раскрытия зрачка, EV (8 EV = в 256 раз). Дальше свет падает как 1/d².")]
        public float EyeAdaptationMaxEv = 8f;

        [Tooltip("Время привыкания к более яркому свету (зрачок сужается), с. Быстро: это рефлекс.")]
        public float EyeAdaptBrightSeconds = 0.25f;

        [Tooltip("Время привыкания к темноте (зрачок расширяется), с. Медленнее, чем сужение.")]
        public float EyeAdaptDarkSeconds = 1.2f;

        [Tooltip("Освещённость грунта (доля полудня Терры), при которой звёзды полностью гаснут. " +
                 "1e-4 ≈ 10 лк: днём на безатмосферном теле звёзд не видно (Аполлон). 0 = не гасить.")]
        public float StarHideDayLevel = 1e-4f;

        [Tooltip("Потолок ночного раскрытия зрачка, EV. Ночь физическая (луна — 2.9e-6 полудня), " +
                 "поэтому глаз раскрывается целиком (share = 1) и упирается в этот потолок: " +
                 "14 EV = 0.036 полудня ≈ 2.4 лк — самый тёмный уровень, который ещё читается " +
                 "как освещённый пейзаж. Это и есть «зрачок максимально открыт ночью»; " +
                 "уменьшать — ночь станет темнее, увеличивать — асфальт снова станет дневным.")]
        public float NightAdaptationMaxEv = 14f;

        /// <summary>
        /// Scene-space moon intensity (0..~0.4), written by MoonlightSystem before this
        /// component's LateUpdate. With a physical moon the value is the real
        /// E_moon/E_sun at the eye (~2.9e-6 at full); the pupil opens on it, so a
        /// moonlit landscape is visible because the eye adapts, not because the moon
        /// is brightened. A property, not a field: it must never be serialized.
        /// </summary>
        public float MoonlightLevel { get; set; }

        [Tooltip("До этой дистанции тени фильтруются штатным HQ-фильтром HDRP (PCSS).")]
        public float ShadowHighDistanceMeters = 70f;

        [Tooltip("До этой дистанции — средний фильтр (GATHER, 4 taps); дальше самый дешёвый (1 tap).")]
        public float ShadowMediumDistanceMeters = 400f;

        [Tooltip("Ширина переходной зоны между тирами фильтрации (м): выбор тира — dither, без двойной стоимости.")]
        public float ShadowBlendWidthMeters = 25f;

        [Tooltip("Потолок видимого углового диаметра звезды, по которому считается полутень тени (градусы). " +
                 "Сам диаметр берётся из орбиты, а не задаётся руками: у звезды в 1 а.е. это 0.53°.")]
        public float SunShadowMaxAngularDiameterDeg = 2f;

        [Tooltip("Максимальная дистанция теней на БЕЗАТМОСФЕРНОМ теле (м). Тени HDRP живут " +
                 "в объёме и общие для всей сцены, поэтому на Терре остаются авторские 250 м " +
                 "(ракета 3 м), а на Луне дистанция поднимается до 2 км: иначе вал кратера в " +
                 "сотне метров не отбрасывает тень, хотя при солнце в 1° она длиной в километры. " +
                 "0 = не переопределять.")]
        public float AirlessShadowDistanceMeters = 2000f;

        /// <summary>Angular diameter of Sol at 1 AU, used until the star data is ready.</summary>
        private const double FallbackSunAngularDiameterDeg = 0.53d;

        /// <summary>Per-channel transmittance to the sun (updated every frame).</summary>
        public Vector3 Transmittance { get; private set; } = Vector3.one;

        /// <summary>Day factor of direct light, 0..1.</summary>
        public float DayFactor { get; private set; }

        /// <summary>Sun elevation above the local horizon, degrees (diagnostics).</summary>
        public float SunElevationDeg { get; private set; }

        /// <summary>Ambient ratio to the reference day, 0..2 (diagnostics).</summary>
        public float AmbientAmount { get; private set; }

        /// <summary>Current eye exposure compensation in EV (diagnostics).</summary>
        public float ExposureCompensation { get; private set; }

        /// <summary>Solar occlusion by the body (0/1, smoothed).</summary>
        public float SunOcclusion { get; private set; }

        /// <summary>
        /// Fraction of the star's disc visible from the eye after every other body
        /// has cast its shadow (0 = total eclipse, 1 = clear). The camera body
        /// itself is excluded: its own limb is the smooth <see cref="SunOcclusion"/>.
        /// </summary>
        public float EclipseVisibility { get; private set; } = 1f;

        /// <summary>Star visibility, 0..1.</summary>
        public float StarVisibility { get; private set; } = 1f;

        /// <summary>
        /// Current pupil opening in EV: 0 in full daylight (Terra's noon), positive when
        /// the observer's light is below it. The scene's lit surfaces are scaled by
        /// <see cref="EyeGain"/>, which is what a real eye does with its iris.
        /// </summary>
        public float EyeEv { get; private set; }

        /// <summary>Linear scene-light multiplier of the current pupil opening.</summary>
        public float EyeGain => (float)EyeAdaptation.Gain(EyeEv);

        // False until the first frame the star is known: the pupil must SNAP there,
        // not ramp, or a spawn in full sun flashes for a quarter of a second.
        private bool eyeReady;

        private static readonly int EyeGainId = Shader.PropertyToID("_EyeGain");

        // Cache of the reference sky luminance (day, sun at zenith, sea level)
        // for ambient normalization: _SkyAmbient keeps the meaning "how much
        // light" for any profile (dense/sparse atmosphere, different layer height).
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
            if (Star == null)
            {
                Star = FindAnyObjectByType<StarRenderer>();
            }

            // Sun shadow resolution is set by the HDRP asset
            // (Max Directional Shadow Map Resolution), not by this code: a manual
            // override here would fight both the asset and the presets
            // (GraphicsQualityController).
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

            if (Runner == null || Runner.SystemState == null || Runner.CameraBody == null
                || Runner.SystemState.Root == null || Runner.Ship == null)
            {
                return;
            }

            // The body under the CAMERA, not under the ship: ambient, sun transmittance
            // and _TerrainBodyCenterWS must describe the ground the eye is over. With the
            // ship's body every one of them described Terra while the player stood above
            // Artemis — the lunar relief was lit by the wrong centre (dayLocal) and the
            // airless body got an atmosphere's worth of ambient.
            ApplyGlobalsNow(Runner.CameraBody, ObserverPosition(), Runner.TimeSeconds);
        }

        /// <summary>
        /// The observer for the sky/ambient is the CAMERA's eye, not Runner.PlayerPosition.
        /// The seated/standing player position is written through the site frame when the
        /// builder hands the craft to physics; it can stay at the pad while the craft is
        /// already in orbit. The atmosphere integral then sees a ground observer, turns the
        /// sky fill light on (AmbientAmount > 0) and lights the cabin with a phantom ambient
        /// sunlight that disappears as soon as the player leaves the seat/EVA (correct
        /// PlayerPosition). The camera is always in render space around the floating origin,
        /// so this conversion is the actual eye position whatever the player state is.
        /// </summary>
        private Vector3d ObserverPosition()
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                return FloatingOrigin.Anchor + AstroFrame.ToAstro(camera.transform.position);
            }

            return Runner.PlayerPosition;
        }

        /// <summary>
        /// Star visibility at the eye, ignoring the body under the eye (its horizon
        /// is <see cref="SunOcclusion"/>). In Play the system's cached positions are
        /// used; the editor preview walks the body tree.
        /// </summary>
        private double ResolveEclipseVisibility(
            OrbitingBody body, OrbitingBody root, Vector3d observer, double timeSeconds)
        {
            if (body == null)
            {
                return 1d;
            }

            if (Application.isPlaying && Runner != null && Runner.SystemState != null)
            {
                return Runner.SystemState.StarVisibilityAt(observer, timeSeconds, out _, body);
            }

            return StarOcclusion.VisibilityAt(root, observer, timeSeconds, body, out _);
        }

        /// <summary>
        /// Outside Play only this method sets the globals. ApplyGlobalsNow is
        /// heavy (atmosphere integral), so it is not called every frame: only on
        /// a body or observer change and at most once per 0.5 s.
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

            // Light as in Play: Sun color and intensity, not just the shader globals.
            ApplyGlobalsNow(frame.BodyState, observer, EditorTimeSeconds, applyToLight: true);

            // The HDRP shadow follows the Sun rotation. In Play it is set by
            // StarRenderer, in the editor by this code; otherwise the shader and
            // the shadow would use different suns.
            if (SunLight != null && LastSunDirectionSim.sqrMagnitude > 0.5f)
            {
                SunLight.transform.rotation = Quaternion.LookRotation(-LastSunDirectionSim);
            }
        }

        /// <summary>
        /// Recompute and set all lighting globals for an arbitrary observer.
        /// Extracted from LateUpdate so the editor surface preview gets EXACTLY
        /// the same light as the game without entering Play — a separate copy of
        /// this physics in the editor would diverge on the first coefficient change.
        ///
        /// applyToLight — whether to touch the scene's Directional Light. Yes for
        /// the game, no for the editor preview: the preview only needs the shader
        /// globals filled, while writing to SunLight would dirty the scene and
        /// change the author's lighting between edits.
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

            // The body-tree root is found via Parent, NOT via Runner.SystemState:
            // outside Play the runner's SystemState is not built, so all Runner
            // references are forbidden inside this method (the editor preview
            // calls it).
            OrbitingBody root = RootOf(body);
            if (root == null)
            {
                return;
            }

            root.EvaluateWorldState(timeSeconds, out Vector3d starPos, out _);
            Vector3d toStar = starPos - observerPosition;
            double starDistance = toStar.Magnitude;
            Vector3d sunDir = starDistance > 0d ? toStar / starDistance : up;

            // A body can stand between the eye and the star: the Moon from Terra,
            // Terra from Artemis, a planet from its moon. This factor multiplies the
            // DIRECT sun term of the terrain/decor/water shaders and the sky ambient,
            // so an eclipse is not just a dark disc over a fully lit landscape.
            double eclipse = ResolveEclipseVisibility(body, root, observerPosition, timeSeconds);
            EclipseVisibility = (float)eclipse;

            double sinEl = Vector3d.Dot(sunDir, up);

            // Single source of β: the same coefficients as the GPU dome
            // (AtmosphereOptics).
            AtmosphereOptics.Coefficients coeff = atmosphere != null
                ? atmosphere.ToOptics()
                : AtmosphereOptics.FromProfile(0d, 0d, 0d, 0d, false);
            double scaleHeight = coeff.RayleighScaleHeight;
            double topAltitude = atmosphere != null ? atmosphere.TopAltitudeMeters : 0d;
            double density = SkyPhysics.Density(altitude, scaleHeight, topAltitude);

            // Single transmittance: the same integral over the same geometry as
            // the GPU sky's transmittance LUT (AtmosphereOptics.TransmittanceToSpace).
            // In vacuum / above the atmosphere it is exactly (1,1,1).
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

            // A star is not ground. When the camera body IS the root (interplanetary
            // space), "up" points away from the star while sunDir points at it, so
            // sinEl = -1 and the horizon formula returned 0.5: the HDRP sun was at half
            // intensity for the whole leg. An observer outside the photosphere is not
            // eclipsed by it.
            double occlusion = body.Parent == null
                ? 1d
                : SkyPhysics.SunOcclusion(sinEl, altitude, body.Radius);
            double day = SkyPhysics.DayFactor(sinEl) * eclipse;
            double skyLuminance = SkyPhysics.SkyLuminance(sinEl, density) * eclipse;

            // The star's actual irradiance at this body, not a fixed "solar" 1.0:
            // StarRenderer.SceneLightScale = (E/E_ref)·(eta/eta_sun) and is 1 for the
            // reference star at 1 AU. Everything below that needs "how much starlight is
            // there" reads this one number.
            double starLightScale = Star != null && Star.IsReady ? Star.SceneLightScale : 1d;

            // Stars need a DARK-ADAPTED eye. The sky-luminance term above cannot see
            // that in vacuum (it is exactly 0), so a sunlit airless surface would show
            // the full star field at local noon - Apollo could not see stars from the
            // lunar surface. The eye is adapted to the GROUND, so the field fades with
            // the sunlit ground level (1 = Terra's noon): gone above ~10 lx, full below
            // ~0.3 lx. The sun does it because the sun, not the sky, sets the pupil.
            double dayGround = starLightScale * System.Math.Max(0d, sinEl) * eclipse;
            double groundStarHide = StarHideDayLevel > 0f
                ? SkyPhysics.Smoothstep(StarHideDayLevel * 0.03d, StarHideDayLevel, dayGround)
                : 0d;
            double starVisibility = SkyPhysics.StarVisibility(skyLuminance, StarVisibilityK)
                * (1d - groundStarHide);

            Transmittance = new Vector3((float)transmittance.X, (float)transmittance.Y, (float)transmittance.Z);
            DayFactor = (float)day;
            SunOcclusion = (float)occlusion;
            StarVisibility = (float)starVisibility;
            SunElevationDeg = (float)(System.Math.Asin(SkyPhysics.Clamp(sinEl, -1d, 1d)) * (180d / System.Math.PI));

            Shader.SetGlobalVector("_SunTransmittance", Transmittance);
            Shader.SetGlobalFloat("_StarVisibility", StarVisibility);
            Shader.SetGlobalVector("_SkySunDir", AstroFrame.ToSimulation(sunDir));
            Shader.SetGlobalVector("_SkyUp", AstroFrame.ToSimulation(up));

            // Direct sunlight color for surface shaders: photosphere ×
            // atmospheric transmittance. In space T=(1,1,1) — white; near the
            // ground the blue channel dies first — warm yellow; at sunset red
            // remains. T is the density integral above the observer, so the color
            // varies smoothly with altitude. Normalized by photosphere luminance:
            // at T=1 the light is exactly unit brightness, the star's warm hue
            // darkens nothing, and the atmosphere only tints and attenuates.
            //
            // With a StarRenderer the photosphere is the preset's physically
            // derived black body (luminance = 1 by construction).
            Vector3d photosphere = Star != null && Star.IsReady
                ? Star.LightColorLinear
                : PhotosphereLinear / PhotosphereLuminance;
            Vector3d sunLight = AtmosphereOptics.VecMul(photosphere, transmittance) * eclipse;
            Shader.SetGlobalVector("_SunLightColor",
                new Vector3((float)sunLight.X, (float)sunLight.Y, (float)sunLight.Z));

            // Direction to the star for the planar terrain shader. Same global
            // StarRenderer sets at runtime: preview and game must be lit from the
            // same angle.
            LastSunDirectionSim = AstroFrame.ToSimulation(sunDir);
            Shader.SetGlobalVector("_TerrainSunDir", LastSunDirectionSim);

            // The same direction in the CAMERA BODY's fixed frame: the baked macro
            // maps and the horizon map live there, so their azimuth/elevation cannot
            // use the inertial frame (the body spins under it).
            QuaternionD bodyOrientation = body.GetVisualOrientation(timeSeconds);
            Vector3d sunBody = bodyOrientation.Conjugated.Rotate(sunDir);
            Shader.SetGlobalVector("_TerrainSunDirBody",
                new Vector3((float)sunBody.X, (float)sunBody.Y, (float)sunBody.Z));

            // Angular diameter of the star's disc → the horizon shadow edge must be
            // exactly as soft as the disc (the same number drives HDRP's PCSS below).
            double sunAngularDeg = (root.Radius > 0d) && (starDistance > root.Radius)
                ? 2d * System.Math.Atan(root.Radius / starDistance) * (180d / System.Math.PI)
                : FallbackSunAngularDiameterDeg;
            float sunAngularDegCapped = (float)System.Math.Min(sunAngularDeg, SunShadowMaxAngularDiameterDeg);
            Shader.SetGlobalFloat("_SunAngularRadiusRad",
                0.5f * sunAngularDegCapped * (float)(System.Math.PI / 180d));

            // The baked horizon belongs to the CAMERA body and is a shader global.
            // Setting it here, once per frame, instead of in PlanetSurfaceRenderer is
            // deliberate: every visible body's renderer calls ApplyTerrainGlobals, so
            // whichever ran last would own the map — Artemis's horizon would vanish
            // while Terra was on screen. The sun direction above has the same owner.
            HeightfieldTerrain horizonTerrain = body.Terrain as HeightfieldTerrain;
            bool useHorizon = horizonTerrain != null && horizonTerrain.MacroHorizonA != null
                && horizonTerrain.MacroHorizonB != null && horizonTerrain.MacroHorizonStrength > 0f;
            // Detail-albedo mode, same owner as the horizon: the CAMERA body's terrain.
            // The per-body renderer loop would let the last rasterisable body win.
            PlanetSurfaceRenderer.ApplyDetailAlbedoMean(
                horizonTerrain != null ? horizonTerrain.DetailAlbedoMean : 0f);
            Shader.SetGlobalFloat("_TerrainHorizonOn",
                useHorizon ? Mathf.Clamp01(horizonTerrain.MacroHorizonStrength) : 0f);
            Shader.SetGlobalTexture("_TerrainMacroHorizonMapA",
                useHorizon ? horizonTerrain.MacroHorizonA : Texture2D.blackTexture);
            Shader.SetGlobalTexture("_TerrainMacroHorizonMapB",
                useHorizon ? horizonTerrain.MacroHorizonB : Texture2D.blackTexture);

            Shader.SetGlobalVector("_TerrainBodyCenterWS", FloatingOrigin.ToRender(bodyPos));

            // Terrain ambient — average sky luminance above the observer
            // (hemispherical single-scatter integral, same physics as the GPU
            // sky). It does not fade at the terminator: while the sky still
            // glows, the ground receives scattered light. Normalized to the
            // reference value (day, zenith): _SkyAmbient remains "how much
            // light" while color/falloff come from physics. Outside the
            // atmosphere it is 0, _NightAmbient remains.
            Vector3d ambientRadiance = Vector3d.Zero;
            double ambientAmount = 0d;
            Vector3d ambientHue = new Vector3d(1d, 1d, 1d);
            if (atmosphere != null && topAltitude > 0d && altitude < topAltitude)
            {
                ambientRadiance = AtmosphereOptics.SkyAmbientRadiance(
                    coeff, body.Radius, body.Radius + topAltitude, body.Radius + altitude, sinEl)
                    * eclipse;
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

            // The terrain, decor, water and far disc all multiply their direct term by
            // _TerrainSun, so feeding the scale here makes every planet follow L/d² — and
            // makes the ground match the Directional Light (ApplyLight already scales by
            // the same value). At the reference body this is exactly the old 1.0; at
            // 4.1 AU the ground is ~128x darker. That physical number alone renders a
            // sunlit distant planet as a black disc: 11 W/m2 is a dull day to a living
            // eye, not darkness, but a fixed-exposure camera sees only 1/128 of the
            // reference. The pupil (EyeAdaptation, below) pays back a share of that
            // deficit; it is the ONLY non-physical factor between the star's flux and
            // the scene's light, and the star's own disc/glare stay physical.
            //
            // ---- The pupil ---------------------------------------------------------
            // Illuminance at the eye in reference units (1 = Terra's noon): the star's
            // flux at this observer, times how far the sun is above the local horizon,
            // plus the sky's diffuse share, plus the moon's reflected light. It is
            // PHYSICAL and independent of frame content: a histogram meter would be
            // dragged around by the ground filling the view, and the whole point is that
            // the eye's state must not depend on where the camera happens to point.
            //
            // The moon is physical too (MoonlightSystem.NightAdaptationGainEv = 0): a
            // full moon is ~2.9e-6 of noon, so the night is 18…20 EV down and the eye
            // must do the whole lift itself — that IS the night adaptation, there is no
            // second baked-in gain. A dark-adapted eye is open all the way (share = 1 for
            // the night branch); the ceiling NightAdaptationMaxEv is the authored night
            // level, not a share: raising it makes the moonlit ground brighter, lowering
            // it darker. The day branch keeps the 0.66 share, because a distant star's
            // noon (`share = 1`) would read as Terra's noon.
            double moonIlluminance = System.Math.Max(0f, MoonlightLevel);
            double illuminance = dayGround + (ambientAmount * starLightScale) + moonIlluminance;
            double targetEv = EyeAdaptation.TargetEv(
                illuminance, EyeAdaptationShare, EyeAdaptationMaxEv);

            // Night is not "a very dim landscape": the sun is gone, so the pupil is
            // fully open and follows the MOON — it opens at moonrise, closes at
            // moonset, and a moonless night simply has nothing to scale (it stays
            // black). Blended through civil twilight, where the sun still sets the level.
            double night = 1d - SkyPhysics.Smoothstep(-0.18d, -0.02d, sinEl);
            double nightTargetEv = EyeAdaptation.TargetEv(illuminance, 1d, NightAdaptationMaxEv);
            targetEv = (targetEv * (1d - night)) + (nightTargetEv * night);

            // The eye is a PHYSICAL object at spawn: snapping to the target on the first
            // frame the star is known is what stops the "spawn under the sun" flash. The
            // first frames also carry the terrain build hitch, so ramping from a default
            // would be a guaranteed flash on the brightest possible scene.
            double step = System.Math.Min(Time.deltaTime, 0.1f);
            if (!eyeReady)
            {
                EyeEv = (float)targetEv;
                eyeReady = Star == null || Star.IsReady;
            }
            else
            {
                EyeEv = (float)EyeAdaptation.Approach(
                    EyeEv, targetEv, step, EyeAdaptBrightSeconds, EyeAdaptDarkSeconds);
            }

            double eyeGain = EyeAdaptation.Gain(EyeEv);
            ExposureCompensation = EyeEv;
            Shader.SetGlobalFloat(EyeGainId, (float)eyeGain);

            // Everything lit by the star: physical flux x the pupil. The star's own
            // disc/glare/corona are NOT in this scale (their surface brightness is
            // physical, only the observer's sensitivity changes).
            double starLightingScale = starLightScale * eyeGain;
            Shader.SetGlobalFloat("_SkyAmbient", SkyAmbient * (float)starLightingScale);
            Shader.SetGlobalFloat("_TerrainSun", TerrainSunIntensity * (float)starLightingScale);
            Shader.SetGlobalFloat("_TerrainRadianceScale", TerrainRadianceScale);

            // Twilight opens up the shadows (backlight would otherwise be pure
            // black): full strength by day, one third below −5°.
            double shadowTwilight = 0.35d + (0.65d * SkyPhysics.Smoothstep(-0.08d, 0.12d, sinEl));
            Shader.SetGlobalFloat("_ShadowStrength", Mathf.Clamp01(ShadowStrength * (float)shadowTwilight));
            Shader.SetGlobalFloat("_ShadowHighDistance", Mathf.Max(0f, ShadowHighDistanceMeters));
            Shader.SetGlobalFloat("_ShadowMediumDistance", Mathf.Max(0f, ShadowMediumDistanceMeters));
            Shader.SetGlobalFloat("_ShadowBlendWidth", Mathf.Max(1f, ShadowBlendWidthMeters));

            if (applyToLight && SunLight != null)
            {
                // The sun's angular diameter is the physical penumbra width of every
                // HDRP directional PCSS shadow: the scene's 0.369-scale Sun is 0.53° at
                // 0.369 AU — the real Sun seen from Earth. A hand-tuned constant (the
                // scene once carried 2.5°) blurs a 10 m object's shadow into an
                // unreadable blob, so the value follows the actual distance. The cap only guards the
                // approach to the star, where the shadow would dissolve completely.
                var hdLight = SunLight.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                if (hdLight != null)
                {
                    float target = sunAngularDegCapped;
                    if (System.Math.Abs(hdLight.angularDiameter - target) > 1e-4f)
                    {
                        hdLight.angularDiameter = target;
                    }
                }

                // Directional Light — light for objects with regular HDRP materials.
                // The sky/terrain/decor compute their own light (_SunLightColor,
                // _TerrainSun, _TerrainRadianceScale globals) in the artificial
                // albedo·ndl scale, where the noon sun gives TerrainRadianceScale.
                // The HDRP/Lit diffuse term is albedo·E·ndl/π, so the matching
                // scale is E = π·TerrainRadianceScale·TerrainSunIntensity.
                // Intensity here must NOT use raw SunLightDayLux: those are
                // physical luxes, orders of magnitude above the scene scale.
                //
                // With StarRenderer the base value is the physical irradiance of
                // the preset star, (E/E_ref)·(eta/eta_sun)·f_vis; the renderer owns
                // the peak-normalised colour and the ×π HDRP conversion.
                //
                // applyToLight=false for the editor preview: only shaders need the
                // light there, and writing to the scene's SunLight would dirty it.
                if (Star != null && Star.IsReady)
                {
                    Star.ApplyLight(
                        SunLight,
                        Transmittance,
                        occlusion,
                        TerrainRadianceScale * TerrainSunIntensity * MaterialLightScale * eyeGain);
                }
                else
                {
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
            }

            // Sky fill light for regular HDRP materials: _SkyAmbientColor is read
            // only by the custom terrain and decor shaders, so an HDRP/Lit building
            // out of direct sun would stay black. This Directional Light takes its
            // color and brightness from the same ambient as the terrain and shines
            // from the side/below, opposite the sun.
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
                    // The Directional Light above runs in the star's physical scale
                    // (StarRenderer.SceneLightScale), which is not 1 in general. A
                    // fixed fill would then be a fraction of the OLD, unscaled sun:
                    // shadows go pitch black and dark materials (asphalt) read as a
                    // hole in the ground. Keep the fill at the same fraction of the
                    // sun it had before the physical star — same eye-adapted scale as
                    // the terrain, or the fill would be 124x too weak at Mars.
                    double starScale = starLightingScale;
                    SkyFillLight.color = new Color(
                        ambientColor.r / peak, ambientColor.g / peak, ambientColor.b / peak, 1f);
                    SkyFillLight.intensity =
                        AmbientAmount * Mathf.PI * SkyAmbient * TerrainRadianceScale * FillLightScale
                        * MaterialLightScale * (float)starScale;
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
                    "[SkyEnvironment][SUNSET] el={0:F1}° T=({1:F3},{2:F3},{3:F3}) ambient={4:F3} day={5:F2} occ={6:F2} ecl={7:F2} star={8:F2} ev={9:F2}",
                    SunElevationDeg, Transmittance.x, Transmittance.y, Transmittance.z,
                    AmbientAmount, DayFactor, SunOcclusion, EclipseVisibility, StarVisibility, ExposureCompensation));
            }

            // True sky-hemisphere ambient for regular HDRP materials: a single
            // Directional Light lights only one side, so the colors are fed into
            // HDRP's Gradient Sky, which HDRP convolves into SH
            // (AmbientProbeConvolution) and hands to materials via
            // EvaluateAmbientProbe — walls get normal-based lighting without an
            // extra light source.
            //
            // Scale matches the terrain (albedo · ambient · SkyAmbient ·
            // TerrainRadianceScale), or building and ground would diverge in
            // brightness. The camera background is unchanged: StarFieldView
            // clears to black and the atmosphere draws the sky, so Gradient Sky
            // acts only as an ambient/reflection source, not as an image.
            if (ExposureVolume != null && ExposureVolume.profile != null
                && ExposureVolume.profile.TryGet<UnityEngine.Rendering.HighDefinition.GradientSky>(out var grad))
            {
                float k = SkyAmbient * TerrainRadianceScale;
                Color sky = new Color(ambientColor.r * k, ambientColor.g * k, ambientColor.b * k, 1f);

                // Ground-reflected light: depends on sun elevation, so it fades
                // together with the direct light and does not shine at night. The
                // physical star flux and the pupil scale it exactly like the terrain.
                float bounce = DayFactor * (float)(Luminance(sunLight) * starLightingScale)
                    * TerrainRadianceScale * TerrainSunIntensity * 0.15f;
                Color ground = new Color(0.20f * bounce, 0.30f * bounce, 0.12f * bounce, 1f);

                grad.top.Override(sky);
                grad.middle.Override(sky * 0.65f);
                grad.bottom.Override(ground);
                grad.multiplier.Override(1f);
            }

            // Shadow range: HDRP has ONE global shadow distance, so an airless body
            // gets its own override volume (priority below the player's settings volume).
            if (Application.isPlaying)
            {
                ApplyShadowDistance(atmosphere == null);
            }
        }

        private UnityEngine.Rendering.Volume shadowDistanceVolume;
        private UnityEngine.Rendering.HighDefinition.HDShadowSettings shadowDistanceSettings;
        private float appliedShadowDistance = -1f;

        /// <summary>
        /// Raises the HDRP shadow range on an airless body (2000 m by default) and
        /// restores the scene's own value everywhere else (Terra keeps its 250 m, where
        /// the 3 m rocket is the thing shadows are for).
        ///
        /// Why a volume: maxShadowDistance is a property of the HDShadowSettings volume
        /// override, not of a light, and the scene's Sky and Fog Volume is shared. A
        /// dedicated global volume at priority 999 wins over the scene profile but still
        /// loses to GameSettingsVolume (1000), so a shadow distance chosen by the player
        /// is never overridden.
        /// </summary>
        private void ApplyShadowDistance(bool airless)
        {
            float wanted = airless ? Mathf.Max(0f, AirlessShadowDistanceMeters) : 0f;
            if (wanted <= 0f)
            {
                if (shadowDistanceVolume != null && shadowDistanceVolume.enabled)
                {
                    shadowDistanceVolume.enabled = false;
                    appliedShadowDistance = -1f;
                }

                return;
            }

            if (shadowDistanceVolume == null)
            {
                var go = new GameObject("AirlessShadowDistance");
                go.transform.SetParent(transform, false);
                go.hideFlags = HideFlags.DontSave;
                shadowDistanceVolume = go.AddComponent<UnityEngine.Rendering.Volume>();
                shadowDistanceVolume.isGlobal = true;
                shadowDistanceVolume.priority = 999f;
                var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                profile.name = "AirlessShadowDistance";
                shadowDistanceSettings = profile.Add<UnityEngine.Rendering.HighDefinition.HDShadowSettings>(true);
                // Same split shape as the scene profile (5% / 18% / 45% of the range):
                // the near cascade stays 100 m at 2 km, so a rocket shadow keeps its
                // 5 cm texels while the far cascade reaches the crater rims.
                shadowDistanceSettings.cascadeShadowSplitCount.Override(3);
                shadowDistanceSettings.cascadeShadowSplit0.Override(0.05f);
                shadowDistanceSettings.cascadeShadowSplit1.Override(0.18f);
                shadowDistanceSettings.cascadeShadowSplit2.Override(0.45f);
                shadowDistanceVolume.sharedProfile = profile;
            }

            if (!shadowDistanceVolume.enabled)
            {
                shadowDistanceVolume.enabled = true;
            }

            if (Mathf.Abs(appliedShadowDistance - wanted) > 0.01f)
            {
                shadowDistanceSettings.active = true;
                shadowDistanceSettings.maxShadowDistance.Override(wanted);
                appliedShadowDistance = wanted;
            }
        }
    }
}
