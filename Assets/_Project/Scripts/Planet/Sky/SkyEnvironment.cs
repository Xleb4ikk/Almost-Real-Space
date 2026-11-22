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

        [Tooltip("Дополнительная адаптация глаза в лунную ночь (EV): полная луна над горизонтом " +
                 "раскрывает экспозицию на столько сверх NightExposure. Без этого физический свет луны " +
                 "(~0.15 сцены при gain 16) читается как почти чёрная земля. 0 = выключено.")]
        public float MoonlightExposureBoost = 0.5f;

        /// <summary>
        /// Scene-space moon intensity (0..~0.4), written by MoonlightSystem before this
        /// component's LateUpdate. The eye's night adaptation follows it: a moonlit
        /// landscape is visible because the pupil opens, not because the moon is bright.
        /// A property, not a field: it must never be serialized into the scene.
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

        /// <summary>Star visibility, 0..1.</summary>
        public float StarVisibility { get; private set; } = 1f;

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
            Vector3d sunLight = AtmosphereOptics.VecMul(photosphere, transmittance);
            Shader.SetGlobalVector("_SunLightColor",
                new Vector3((float)sunLight.X, (float)sunLight.Y, (float)sunLight.Z));

            // Direction to the star for the planar terrain shader. Same global
            // StarRenderer sets at runtime: preview and game must be lit from the
            // same angle.
            LastSunDirectionSim = AstroFrame.ToSimulation(sunDir);
            Shader.SetGlobalVector("_TerrainSunDir", LastSunDirectionSim);

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
                // HDRP directional PCSS shadow: Sol at 1 AU is 0.53°, the same star
                // seen from 0.37 AU is 1.44°. A hand-tuned constant (the scene once
                // carried 2.5°) blurs a 10 m object's shadow into an unreadable blob,
                // so the value follows the actual distance. The cap only guards the
                // approach to the star, where the shadow would dissolve completely.
                var hdLight = SunLight.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                if (hdLight != null)
                {
                    double starRadius = root.Radius;
                    double angularDeg = (starRadius > 0d) && (starDistance > starRadius)
                        ? 2d * System.Math.Atan(starRadius / starDistance) * (180d / System.Math.PI)
                        : FallbackSunAngularDiameterDeg;
                    float target = (float)System.Math.Min(angularDeg, SunShadowMaxAngularDiameterDeg);
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
                        TerrainRadianceScale * TerrainSunIntensity * MaterialLightScale);
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
                    // sun it had before the physical star.
                    double starScale = Star != null && Star.IsReady ? Star.SceneLightScale : 1d;
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
                    "[SkyEnvironment][SUNSET] el={0:F1}° T=({1:F3},{2:F3},{3:F3}) ambient={4:F3} day={5:F2} occ={6:F2} star={7:F2} ev={8:F2}",
                    SunElevationDeg, Transmittance.x, Transmittance.y, Transmittance.z,
                    AmbientAmount, DayFactor, SunOcclusion, StarVisibility, ExposureCompensation));
            }

            // Eye adaptation: exposure compensation from physical illumination.
            // Deterministic (independent of frame contents): a histogram meter on
            // a bright sky would crush the ground in backlight. 0 by day, up to
            // TwilightExposureMax at twilight, floor NightExposure at night.
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

            // Night adaptation to moonlight: a full moon on the ground is ~0.1 of the
            // noon sun, which a day-calibrated exposure renders nearly black. The eye
            // opens; so does the camera. Fades out before civil twilight, where the
            // sky — not the moon — already sets the exposure. MoonlightLevel is set by
            // MoonlightSystem (0 = no moon body in this scene).
            if (MoonlightExposureBoost > 0f && MoonlightLevel > 0f)
            {
                double night = 1d - SkyPhysics.Smoothstep(-0.18d, -0.02d, sinEl);
                double moonAdapt = System.Math.Sqrt(
                    SkyPhysics.Clamp(MoonlightLevel / 0.15f, 0d, 1d));
                exposureComp += MoonlightExposureBoost * night * moonAdapt;
            }

            ExposureCompensation = (float)exposureComp;

            // The star's dedicated Volume (priority 100) owns Exposure when it is
            // present; otherwise the scene's Sky and Fog Volume does. Writing the
            // deterministic twilight/night compensation into the same instance keeps
            // the two exposure models from fighting: HDRP supplies the automatic
            // histogram/hysteresis, this term supplies the scene-level twilight
            // adaptation, and the preset adds its artistic exposureBias.
            UnityEngine.Rendering.Volume exposureTarget = ExposureVolume;
            if (Star != null && Star.StarVolume != null)
            {
                exposureTarget = Star.StarVolume;
            }

            if (exposureTarget != null && exposureTarget.profile != null
                && exposureTarget.profile.TryGet<UnityEngine.Rendering.HighDefinition.Exposure>(out var exposure))
            {
                double bias = Star != null && Star.Preset != null ? Star.Preset.ExposureBias : 0d;
                if (Star != null)
                {
                    bias += Star.StartupExposureBiasEv;
                }

                exposure.compensation.Override((float)(exposureComp + bias));
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
                // together with the direct light and does not shine at night.
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
