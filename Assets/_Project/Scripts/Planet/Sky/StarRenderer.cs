using System;
using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// Physically based main-sequence star render. Replaces the old SunBillboard on
    /// the same GameObject (the script GUID is preserved), so the scene wiring stays
    /// valid. All photometric values come from <see cref="StarPreset"/> through
    /// <see cref="StarPhysics"/>; nothing is hand-tuned in the component.
    ///
    /// Rendered as emissive billboards in angular coordinates, never at the real
    /// position (float precision):
    ///   — disc: a camera-facing quad that is an analytic sphere impostor
    ///     (μ = sqrt(1 − r²), limb darkening, granulation, spots, flares);
    ///   — glare: Moffat halo, amplitude from E and the eclipse-visible fraction;
    ///   — corona: radial power law with polar noise, physically additive;
    ///   — chromosphere: thin ring, visible only when the disc is eclipsed.
    /// The Directional Light is physical too: intensity is the irradiance in scene
    /// units (HDRP: ×π for lux), colour is the adapted black-body vector.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public sealed class StarRenderer : MonoBehaviour
    {
        [Header("Сцена")]
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Камера игрока. Пусто — Camera.main.")]
        public Camera PlayerCamera;

        [Tooltip("Directional Light звезды. Пусто — первый directional в сцене.")]
        public Light SunLight;

        [Tooltip("Физика неба: атмосферная прозрачность к цвету света И к собственному излучению звезды " +
                 "(диск, хромосфера, гало, корона) — у земли солнце тёплое, в космосе белое. Необязательно.")]
        public SkyEnvironment Sky;

        [Header("Звезда")]
        [Tooltip("Пресет звезды. Один ассет = одна звезда: M…O, без правки кода.")]
        public StarPreset Preset;

        [Tooltip("Volume с Bloom/Tonemapping/Exposure. Пусто — будет создан на старте (runtime).")]
        public Volume StarVolume;

        [Header("Устаревшие поля SunBillboard (совместимость сцены)")]
        [Tooltip("Используется только если Preset не назначен: температура фотосферы, K.")]
        public double TemperatureKelvin = 5772d;

        [Tooltip("Не используется новой физикой; оставлено для совместимости сериализации.")]
        public float DiscBrightness = 4f;

        [Tooltip("Доля far clip для прокси-сферы. Жёстко ограничена 0.5·far.")]
        public float BillboardFarFraction = 0.25f;

        [Tooltip("Радиус вокруг камеры (м) при EVA, внутри которого геометрия считается своим кораблём и не скрывает звезду.")]
        public float EvaCraftClearanceMeters = 25f;

        [Tooltip("Рисовать диск звезды.")]
        public bool RenderDisc = true;

        [Tooltip("Оверлей отладки.")]
        public bool DebugHud;

        // ------------------------------------------------------------------
        // Public state for SkyEnvironment and diagnostics
        // ------------------------------------------------------------------

        public bool IsReady => state != null && star != null;
        public StarState State => state;

        /// <summary>Adapted linear sRGB, luminance = 1 (the colour of the light).</summary>
        public Vector3d LightColorLinear => state == null
            ? new Vector3d(1d, 1d, 1d)
            : new Vector3d(state.ColorR, state.ColorG, state.ColorB);

        /// <summary>(E/E_ref)·(eta/eta_sun)·f_vis — the Directional Light scale in scene units.</summary>
        public double SceneLightScale { get; private set; } = 1d;

        public double IrradianceWm2 { get; private set; }
        public double ApparentMagnitude { get; private set; }
        public double AngularDiameterDeg { get; private set; }
        public double DistanceKm { get; private set; }

        /// <summary>Fraction of the stellar disc visible around all occluders, 0..1.</summary>
        public double EclipseVisibility { get; private set; } = 1d;

        /// <summary>
        /// Temporary negative EV applied while the auto exposure is finding the
        /// scene level. Prevents the first visible frames from flashing when the
        /// star is much brighter than the E_ref baseline (e.g. a solar star at
        /// 0.368 AU is +2.9 EV).
        /// </summary>
        public double StartupExposureBiasEv { get; private set; }

        public int ScreenDiscPixels { get; private set; }
        public bool IsSubpixel { get; private set; }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private OrbitingBody star;
        private StarState state;
        private StarPreset runtimePreset;
        private Material discMaterial;
        private Material glareMaterial;
        private Material coronaMaterial;
        private Transform discObject;
        private Transform glareObject;
        private Transform coronaObject;
        private Renderer discRenderer;
        private Renderer glareRenderer;
        private Renderer coronaRenderer;
        private Volume runtimeVolume;
        private Bloom bloom;
        private Exposure exposure;
        private Tonemapping tonemapping;
        private int startupExposureFrames;
        private bool startupExposureArmed = true;
        private double sunElevationDeg = double.NaN;
        private bool loggedSummary;
        private bool loggedMissingShader;

        /// <summary>
        /// HDRP auto exposure starts from a default and only then stops down. With a
        /// solar star at the scene's 0.368 AU the first frames are ~3 EV too bright;
        /// run the exposure adaptation fast for the first few rendered frames, then
        /// restore the artistic asymmetric speeds.
        /// </summary>
        private const int StartupExposureFrameCount = 90;
        private const float StartupAdaptationSpeed = 50f;

        private static readonly int StarColorId = Shader.PropertyToID("_StarColor");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int LimbDarkeningId = Shader.PropertyToID("_LimbDarkening");
        private static readonly int GranulationCellsId = Shader.PropertyToID("_GranulationCells");
        private static readonly int GranulationContrastId = Shader.PropertyToID("_GranulationContrast");
        private static readonly int GranulationFrequencyId = Shader.PropertyToID("_GranulationFrequency");
        private static readonly int GranulationPhaseId = Shader.PropertyToID("_GranulationPhase");
        private static readonly int GranulationTimeId = Shader.PropertyToID("_GranulationTime");
        private static readonly int GranulationEnabledId = Shader.PropertyToID("_GranulationEnabled");
        private static readonly int SpotCoverageId = Shader.PropertyToID("_SpotCoverage");
        private static readonly int SpotUmbraColorId = Shader.PropertyToID("_SpotUmbraColor");
        private static readonly int SpotPenumbraColorId = Shader.PropertyToID("_SpotPenumbraColor");
        private static readonly int ActivityId = Shader.PropertyToID("_Activity");
        private static readonly int StarToCameraLocalId = Shader.PropertyToID("_StarToCameraLocal");
        private static readonly int StarTangentLocalId = Shader.PropertyToID("_StarTangentLocal");
        private static readonly int StarBitangentLocalId = Shader.PropertyToID("_StarBitangentLocal");
        private static readonly int ChromosphereIntensityId = Shader.PropertyToID("_ChromosphereIntensity");
        private static readonly int ChromosphereColorId = Shader.PropertyToID("_ChromosphereColor");
        private static readonly int EclipseVisibilityId = Shader.PropertyToID("_EclipseVisibility");
        private static readonly int PixelAngleId = Shader.PropertyToID("_PixelAngle");
        private static readonly int DiscOverscanId = Shader.PropertyToID("_DiscOverscan");
        private static readonly int StarSeedId = Shader.PropertyToID("_Seed");

        private static readonly int GlareColorId = Shader.PropertyToID("_GlareColor");
        private static readonly int GlareAmplitudeId = Shader.PropertyToID("_GlareAmplitude");
        private static readonly int GlareTheta0Id = Shader.PropertyToID("_GlareTheta0");
        private static readonly int GlareBetaId = Shader.PropertyToID("_GlareBeta");
        private static readonly int GlareExtentId = Shader.PropertyToID("_GlareExtent");
        private static readonly int GlareRayStrengthId = Shader.PropertyToID("_GlareRayStrength");

        private static readonly int CoronaColorId = Shader.PropertyToID("_CoronaColor");
        private static readonly int CoronaIcId = Shader.PropertyToID("_CoronaIc");
        private static readonly int CoronaExtentId = Shader.PropertyToID("_CoronaExtent");
        private static readonly int CoronaTimeId = Shader.PropertyToID("_CoronaTime");
        private static readonly int CoronaRayCountId = Shader.PropertyToID("_CoronaRayCount");

        private static readonly int SunSelfOccluderId = Shader.PropertyToID("_SunSelfOccluder");
        private static readonly int SunFarLimitId = Shader.PropertyToID("_SunFarLimit");

        private const float MaxProxyFarFraction = 0.25f;
        private const double DegToRad = Math.PI / 180d;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private void Start()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (PlayerCamera == null)
            {
                PlayerCamera = Camera.main;
            }

            if (Sky == null)
            {
                Sky = FindAnyObjectByType<SkyEnvironment>();
            }

            if (SunLight == null)
            {
                foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                {
                    if (light.type == LightType.Directional)
                    {
                        SunLight = light;
                        break;
                    }
                }
            }

            if (Runner == null || Runner.SystemState == null || Runner.SystemState.Root == null)
            {
                Debug.LogWarning("[StarRenderer] ОТКЛЮЧЁН: нет Runner/SystemState. Назначь SimulationRunner в сцене.");
                enabled = false;
                return;
            }

            star = Runner.SystemState.Root;

            if (Preset == null)
            {
                runtimePreset = ScriptableObject.CreateInstance<StarPreset>();
                runtimePreset.name = "StarPreset (runtime, mass = 1)";
                runtimePreset.MassSolar = 1d;
                runtimePreset.EffectiveTemperatureKOverride = TemperatureKelvin;
                Preset = runtimePreset;
                Debug.LogWarning(
                    "[StarRenderer] Preset не назначен: создан временный солнечный пресет (M = 1). " +
                    "Назначь StarPreset, чтобы менять звезду без правки сцены.");
            }

            state = Preset.Derive();
            if (state.HasOverrides)
            {
                Debug.LogWarning("[StarRenderer] " + state.OverrideWarning);
            }

            if (Preset.ApplyDerivedRadiusToStarBody)
            {
                double radiusMeters = state.RadiusKm * 1000d;
                if (Math.Abs(star.Radius - radiusMeters) > 1d)
                {
                    Debug.Log(string.Format(
                        "[StarRenderer] Радиус звезды {0}: {1:E3} м → {2:E3} м (M = {3} M☉). " +
                        "Гравитация (μ) системы не меняется.",
                        star.Name, star.Radius, radiusMeters, state.MassSolar));
                    star.Radius = radiusMeters;
                }
            }

            CreateVisuals();
            ConfigureVolume();
            LogSummary();
        }

        private void LateUpdate()
        {
            if (!IsReady)
            {
                return;
            }

            if (startupExposureArmed)
            {
                // The first frames include the terrain/cloud build hitch. Do not
                // spend the startup window on frames the player never sees: arm the
                // countdown on the first normal frame after the hitch.
                if (Time.unscaledDeltaTime < 0.1f)
                {
                    startupExposureArmed = false;
                    startupExposureFrames = StartupExposureFrameCount;
                }
            }
            else if (startupExposureFrames > 0)
            {
                startupExposureFrames--;
                if (startupExposureFrames == 0 && exposure != null)
                {
                    exposure.adaptationSpeedLightToDark.Override(3f);
                    exposure.adaptationSpeedDarkToLight.Override(0.7f);
                }
            }

            Camera camera = PlayerCamera != null ? PlayerCamera : Camera.main;
            if (camera == null)
            {
                return;
            }

            star.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPosition, out _);
            Vector3d observer = FloatingOrigin.Anchor + AstroFrame.ToAstro(camera.transform.position);
            Vector3d delta = starPosition - observer;
            double distanceMeters = delta.Magnitude;
            if (!(distanceMeters > 1d))
            {
                return;
            }

            Vector3d direction = delta / distanceMeters;
            DistanceKm = distanceMeters / 1000d;
            double alpha = state.AngularRadiusRad(DistanceKm);
            AngularDiameterDeg = alpha * (360d / Math.PI);
            IrradianceWm2 = state.IrradianceWm2(DistanceKm);
            ApparentMagnitude = state.ApparentMagnitude(DistanceKm);

            double distanceAu = DistanceKm / StarPhysics.AuKm;
            double fluxRatio = IrradianceWm2 / StarPhysics.Esun1Au;
            double etaRatio = state.VisibleEtaRatioToSun;
            EclipseVisibility = ComputeEclipseVisibility(observer, direction, distanceMeters, alpha, Runner.TimeSeconds);

            double pixelAngle = StarPhysics.PixelAngleRad(camera.fieldOfView * DegToRad, camera.pixelHeight);
            if (!(pixelAngle > 0d))
            {
                pixelAngle = StarPhysics.PixelAngleRad(60d * DegToRad, 1080);
            }

            StarPhysics.SubpixelDisc(alpha, pixelAngle, out double drawAlpha, out double energyScale);
            IsSubpixel = alpha < (0.5d * pixelAngle);
            ScreenDiscPixels = (int)Math.Round((2d * alpha) / pixelAngle);

            double discBrightness = state.DiscBrightnessCompressed * energyScale;

            // Everything the star itself emits is seen THROUGH the observer's
            // atmosphere: the disc, chromosphere, glare and corona all lose the
            // blue end of the spectrum to Rayleigh extinction before they reach
            // the eye. T comes from the same single-source integral
            // (AtmosphereOptics.TransmittanceToSpace) that builds the GPU
            // transmittance LUT and colours the Directional Light / _SunLightColor,
            // evaluated for the current eye altitude and sun elevation. At the
            // ground it is ≈(0.93, 0.83, 0.70) for a 44° sun — the photosphere
            // takes the warm cast; above the atmosphere it is exactly (1,1,1),
            // so the disc becomes its true white.
            Vector3 transmittance = Sky != null && Sky.isActiveAndEnabled
                ? Sky.Transmittance
                : Vector3.one;

            // The bright aureole around the sun is scattered sunlight: it belongs
            // to the atmosphere, not to the star. In vacuum there is nothing to
            // scatter, so the glare quad fades out with the air column and the
            // disc keeps a sharp edge. 0.15 ≈ the vertical optical depth that
            // already visibly warms the direct beam (T_luminance ≈ 0.86).
            double transmittanceLuminance = (0.2126d * transmittance.x)
                + (0.7152d * transmittance.y)
                + (0.0722d * transmittance.z);
            double aureole01 = StarPhysics.Clamp(
                -Math.Log(StarPhysics.Clamp(transmittanceLuminance, 1e-6d, 1d)) / 0.15d, 0d, 1d);

            // The corona is the star's outer atmosphere: it is always there, but
            // the photosphere outshines it by orders of magnitude. Only a total
            // eclipse uncovers it, so the visible amount follows the obscured
            // fraction (cubed: a partial eclipse still hides it).
            double coronaVisibility = Math.Pow(StarPhysics.Clamp(1d - EclipseVisibility, 0d, 1d), 3d);
            double coronaIc = 1.5e-5d * state.DiscBrightnessCompressed * Preset.CoronaGain
                * (0.3d + (0.7d * state.Activity01)) * coronaVisibility;

            SceneLightScale = fluxRatio * etaRatio;

            double exposureExcess = fluxRatio * etaRatio;
            int startupFramesLeft = startupExposureArmed ? StartupExposureFrameCount : startupExposureFrames;
            StartupExposureBiasEv = startupFramesLeft > 0 && exposureExcess > 1d
                ? (-Math.Log(exposureExcess, 2d)) * (startupFramesLeft / (double)StartupExposureFrameCount)
                : 0d;

            Vector3 simDirection = AstroFrame.ToSimulation(direction);
            Vector3 cameraPosition = camera.transform.position;
            float far = Mathf.Max(camera.farClipPlane, 1f);
            float renderDistance = ResolveRenderDistance(far);
            bool eva = Runner.PlayerMode == PlayerMode.EVA;
            Shader.SetGlobalFloat(SunSelfOccluderId, eva ? Mathf.Max(0f, EvaCraftClearanceMeters) : 0f);
            Shader.SetGlobalFloat(SunFarLimitId, far * 0.999f);

            // Sun elevation for the HUD (same convention as the old SunBillboard).
            sunElevationDeg = double.NaN;
            if (Runner.DominantBody != null)
            {
                Runner.SystemState.EvaluateBodyState(
                    Runner.DominantBody, Runner.TimeSeconds, out Vector3d bodyPosition, out _);
                Vector3d up = observer - bodyPosition;
                double upMagnitude = up.Magnitude;
                if (upMagnitude > 0d)
                {
                    double cosAngle = Vector3d.Dot(direction, up / upMagnitude);
                    sunElevationDeg = 90d - (Math.Acos(StarPhysics.Clamp(cosAngle, -1d, 1d)) * (180d / Math.PI));
                }
            }

            // Star-local basis for the 3D granulation/spot pattern. The pattern is
            // fixed to the star, so it rotates with the body instead of sliding over
            // the disc in screen space.
            QuaternionD orientation = QuaternionD.Identity;
            if (Runner.SystemState != null)
            {
                Runner.SystemState.GetVisualFrame(star, Runner.TimeSeconds, out _, out _, out orientation);
            }

            Quaternion bodyRotation = AstroFrame.ToSimulation(orientation);
            Vector3 quadForward = simDirection;
            Vector3 quadUpHint = Mathf.Abs(Vector3.Dot(quadForward, Vector3.up)) > 0.98f
                ? Vector3.right
                : Vector3.up;
            Vector3 quadRight = Vector3.Cross(quadUpHint, quadForward).normalized;
            Vector3 quadUp = Vector3.Cross(quadForward, quadRight).normalized;
            Quaternion inverseBody = Quaternion.Inverse(bodyRotation);
            Vector3 toCameraLocal = inverseBody * (-simDirection);
            Vector3 tangentLocal = inverseBody * quadRight;
            Vector3 bitangentLocal = inverseBody * quadUp;

            double granulationFrequency = alpha > 1e-9d
                ? state.GranulationCells / (2d * alpha)
                : 0d;
            bool granulationEnabled = RenderDisc
                && state.GranulationContrast > 1e-4d
                && state.GranulationCells >= 3d
                && !IsSubpixel
                && alpha > 1e-7d;

            double phase = (Runner.TimeSeconds / Math.Max(1d, Preset.GranulationPeriodSeconds)) * 2d * Math.PI;
            phase -= Math.Floor(phase / (2d * Math.PI)) * (2d * Math.PI);

            // The quad covers the disc plus a pixel-wide edge margin; the shader
            // needs the ratio to know where the silhouette is.
            double discOverscan = StarPhysics.Clamp(
                1d + ((4d * pixelAngle) / Math.Max(drawAlpha, 1e-9d)), 1.01d, 32d);
            double discHalfAngle = Math.Min(1.3d, drawAlpha * discOverscan);

            PositionQuad(
                discObject,
                cameraPosition + (simDirection * renderDistance),
                simDirection,
                renderDistance,
                discHalfAngle);
            PositionQuad(
                glareObject,
                cameraPosition + (simDirection * renderDistance),
                simDirection,
                renderDistance,
                Math.Min(60d, Preset.GlareExtentDegrees) * DegToRad);
            PositionQuad(
                coronaObject,
                cameraPosition + (simDirection * renderDistance),
                simDirection,
                renderDistance,
                Math.Min(60d * DegToRad, alpha * Math.Max(1d, Preset.CoronaExtentRadii)));

            if (discMaterial != null)
            {
                Color starColor = Tinted(transmittance, state.ColorR, state.ColorG, state.ColorB);
                Vector3 umbra = new Vector3(
                    (float)state.SpotUmbraR,
                    (float)state.SpotUmbraG,
                    (float)state.SpotUmbraB);
                Vector3 penumbra = new Vector3(
                    (float)state.SpotPenumbraR,
                    (float)state.SpotPenumbraG,
                    (float)state.SpotPenumbraB);

                discMaterial.SetColor(StarColorId, starColor);
                discMaterial.SetFloat(BrightnessId, (float)discBrightness);
                discMaterial.SetVector(LimbDarkeningId, new Vector4(
                    (float)state.LimbR, (float)state.LimbG, (float)state.LimbB, 0f));
                discMaterial.SetFloat(GranulationCellsId, (float)state.GranulationCells);
                discMaterial.SetFloat(GranulationContrastId, (float)state.GranulationContrast);
                discMaterial.SetFloat(GranulationFrequencyId, (float)granulationFrequency);
                discMaterial.SetFloat(GranulationPhaseId, (float)phase);
                discMaterial.SetFloat(GranulationTimeId, (float)Runner.TimeSeconds);
                discMaterial.SetFloat(GranulationEnabledId, granulationEnabled ? 1f : 0f);
                discMaterial.SetFloat(SpotCoverageId, (float)state.SpotCoverage);
                discMaterial.SetVector(SpotUmbraColorId, umbra);
                discMaterial.SetVector(SpotPenumbraColorId, penumbra);
                discMaterial.SetFloat(ActivityId, (float)state.Activity01);
                discMaterial.SetVector(StarToCameraLocalId, toCameraLocal);
                discMaterial.SetVector(StarTangentLocalId, tangentLocal);
                discMaterial.SetVector(StarBitangentLocalId, bitangentLocal);
                discMaterial.SetFloat(ChromosphereIntensityId,
                    (float)(1e-3d * state.DiscBrightnessCompressed * (1d - EclipseVisibility)));
                discMaterial.SetColor(ChromosphereColorId, new Color(
                    0.9f * transmittance.x,
                    0.25f * transmittance.y,
                    0.3f * transmittance.z,
                    1f));
                discMaterial.SetFloat(EclipseVisibilityId, (float)EclipseVisibility);
                discMaterial.SetFloat(PixelAngleId, (float)pixelAngle);
                discMaterial.SetFloat(DiscOverscanId, (float)discOverscan);
                discMaterial.SetFloat(StarSeedId, Preset != null ? Preset.SurfaceSeed : 1f);
            }

            if (glareMaterial != null)
            {
                double theta0 = Math.Max(0.001d, Preset.GlareTheta0Degrees) * DegToRad;
                double moffatPeak = (1.8d - 1d) / (Math.PI * theta0 * theta0);
                double peakRadiance = state.DiscBrightnessCompressed * fluxRatio * etaRatio
                    * Preset.GlareGain * EclipseVisibility * aureole01;
                glareMaterial.SetColor(GlareColorId, Tinted(transmittance, state.ColorR, state.ColorG, state.ColorB));
                glareMaterial.SetFloat(GlareAmplitudeId, (float)(peakRadiance / Math.Max(1e-12d, moffatPeak)));
                glareMaterial.SetFloat(GlareTheta0Id, (float)theta0);
                glareMaterial.SetFloat(GlareBetaId, 1.8f);
                glareMaterial.SetFloat(GlareExtentId, (float)(Math.Min(60d, Preset.GlareExtentDegrees) * DegToRad));
                glareMaterial.SetFloat(GlareRayStrengthId, Preset.DiffractionRays ? 0.08f : 0f);
            }

            if (coronaMaterial != null)
            {
                coronaMaterial.SetColor(CoronaColorId, Tinted(transmittance, state.ColorR, state.ColorG, state.ColorB));
                coronaMaterial.SetFloat(CoronaIcId, (float)coronaIc);
                coronaMaterial.SetFloat(CoronaExtentId, (float)Math.Max(1d, Preset.CoronaExtentRadii));
                coronaMaterial.SetFloat(CoronaTimeId, (float)Runner.TimeSeconds);
                coronaMaterial.SetFloat(CoronaRayCountId, 9f);
            }

            if (discObject != null)
            {
                discObject.gameObject.SetActive(RenderDisc);
                discRenderer.enabled = RenderDisc;
            }

            if (glareObject != null)
            {
                bool visibleGlare = glareMaterial != null && EclipseVisibility > 0d && aureole01 > 0d
                    && state.DiscBrightnessCompressed * fluxRatio * etaRatio * Preset.GlareGain * aureole01 > 1e-7d;
                glareObject.gameObject.SetActive(visibleGlare);
            }

            if (coronaObject != null)
            {
                coronaObject.gameObject.SetActive(coronaIc > 0d && alpha * Preset.CoronaExtentRadii > pixelAngle);
            }

            // Base exposure bias + startup anti-flash term. SkyEnvironment (when it
            // is running) adds the deterministic twilight/night compensation on top
            // of the same instance later in the frame.
            if (exposure != null)
            {
                exposure.compensation.Override(
                    (float)((Preset != null ? Preset.ExposureBias : 0d) + StartupExposureBiasEv));
            }

            // Directional Light: apply the vacuum value every frame. SkyEnvironment
            // runs after this component and overrides it with the atmosphere
            // transmittance when a dominant body is present; in deep space it leaves
            // this value untouched.
            if (SunLight != null)
            {
                SunLight.transform.rotation = Quaternion.LookRotation(-simDirection);
                double calibration = Sky != null
                    ? Sky.TerrainRadianceScale * Sky.TerrainSunIntensity * Sky.MaterialLightScale
                    : 1d;
                ApplyLight(SunLight, Vector3.one, 1d, calibration);
            }
        }

        private void OnDestroy()
        {
            DestroyMaterial(ref discMaterial);
            DestroyMaterial(ref glareMaterial);
            DestroyMaterial(ref coronaMaterial);
            DestroyNow(discObject);
            DestroyNow(glareObject);
            DestroyNow(coronaObject);
            if (runtimeVolume != null)
            {
                DestroyNow(runtimeVolume.transform);
            }

            if (runtimePreset != null)
            {
                DestroyNow(runtimePreset);
            }
        }

        // ------------------------------------------------------------------
        // Light and volume
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes the physically derived light colour/intensity. Called by
        /// SkyEnvironment after the atmosphere transmittance is known, so the star
        /// light has exactly one owner in a scene with a sky.
        /// </summary>
        public void ApplyLight(Light light, Vector3 atmosphereTransmittance, double occlusion, double sceneCalibration = 1d)
        {
            if (light == null || state == null)
            {
                return;
            }

            double r = state.ColorR * atmosphereTransmittance.x;
            double g = state.ColorG * atmosphereTransmittance.y;
            double b = state.ColorB * atmosphereTransmittance.z;
            double peak = Math.Max(r, Math.Max(g, b));
            if (!(peak > 1e-9d))
            {
                light.color = Color.black;
                light.intensity = 0f;
                return;
            }

            // Scene unit: a white Lambertian surface under E_ref has radiance 1.
            // HDRP directional diffuse = albedo·E/π, so the lux value is π·E_scene.
            // The celestial eclipse (f_vis) and the local horizon model are the same
            // geometric event; take the stricter one instead of multiplying them
            // twice at sunset.
            double effectiveOcclusion = Math.Min(EclipseVisibility, StarPhysics.Clamp(occlusion, 0d, 1d));
            double sceneScale = SceneLightScale * Math.Max(0d, effectiveOcclusion) * Math.Max(0d, sceneCalibration);
            double intensityLux = Math.PI * sceneScale * peak;

            light.color = new Color(
                (float)(r / peak),
                (float)(g / peak),
                (float)(b / peak),
                1f);
            light.intensity = (float)intensityLux;
        }

        private void ConfigureVolume()
        {
            if (StarVolume == null)
            {
                GameObject volumeObject = new GameObject("Star Volume (runtime)");
                StarVolume = volumeObject.AddComponent<Volume>();
                StarVolume.isGlobal = true;
                StarVolume.priority = 100f;
                StarVolume.weight = 1f;
                runtimeVolume = StarVolume;
            }

            if (StarVolume.sharedProfile == null)
            {
                StarVolume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            }

            VolumeProfile profile = StarVolume.profile;
            if (profile == null)
            {
                Debug.LogWarning("[StarRenderer] Volume не имеет профиля — bloom/tonemapping/exposure не настроены.");
                return;
            }

            if (!profile.TryGet(out bloom))
            {
                bloom = profile.Add<Bloom>(true);
            }

            bloom.active = true;
            bloom.threshold.Override(1f);
            double requestedBloom = Preset.BloomGain;
            if (requestedBloom > 1d)
            {
                Debug.LogWarning(
                    "[StarRenderer] HDRP Bloom.intensity ограничен 1.0; bloomGain " +
                    Preset.BloomGain + " будет обрезан до 1.0.");
            }

            bloom.intensity.Override((float)StarPhysics.Clamp(requestedBloom, 0d, 1d));
            // Tight scatter: a wide halo reads as an atmospheric diffusion
            // around the sun, and in vacuum there is nothing to diffuse.
            bloom.scatter.Override(0.5f);
            // Neutral tint: the bloom takes the colour of what it blooms. The disc,
            // glare and corona already carry the star's colour times the atmospheric
            // transmittance, so tinting the volume here as well would apply the
            // photosphere colour (and the atmospheric warm cast) twice and turn the
            // space sun's halo yellow.
            bloom.tint.Override(Color.white);

            if (!profile.TryGet(out tonemapping))
            {
                tonemapping = profile.Add<Tonemapping>(true);
            }

            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.ACES);
            tonemapping.useFullACES.Override(false);

            if (!profile.TryGet(out exposure))
            {
                exposure = profile.Add<Exposure>(true);
            }

            exposure.active = true;
            exposure.mode.Override(ExposureMode.Automatic);
            exposure.meteringMode.Override(MeteringMode.CenterWeighted);
            exposure.compensation.Override((float)Preset.ExposureBias);
            exposure.limitMin.Override(-1f);
            exposure.limitMax.Override(14f);
            exposure.adaptationMode.Override(AdaptationMode.Progressive);
            // The steady-state speeds are 3 (darken quickly) and 0.7 (brighten
            // slowly). The startup window below temporarily overrides both so the
            // first frames do not flash while HDRP finds the scene exposure.
            exposure.adaptationSpeedLightToDark.Override(StartupAdaptationSpeed);
            exposure.adaptationSpeedDarkToLight.Override(StartupAdaptationSpeed);
            startupExposureFrames = StartupExposureFrameCount;
        }

        // ------------------------------------------------------------------
        // Visual construction and placement
        // ------------------------------------------------------------------

        private void CreateVisuals()
        {
            Shader discShader = Shader.Find("Galilego/StarDisc");
            Shader glareShader = Shader.Find("Galilego/StarGlare");
            Shader coronaShader = Shader.Find("Galilego/StarCorona");
            if (discShader == null || glareShader == null || coronaShader == null)
            {
                if (!loggedMissingShader)
                {
                    loggedMissingShader = true;
                    Debug.LogError(
                        "[StarRenderer] Не найдены шейдеры Galilego/StarDisc|StarGlare|StarCorona. " +
                        "Проверь Assets/_Project/Shaders и консоль компиляции шейдеров.");
                }
            }

            Shader fallback = Shader.Find("HDRP/Unlit");
            discMaterial = new Material(discShader != null ? discShader : fallback);
            glareMaterial = new Material(glareShader != null ? glareShader : fallback);
            coronaMaterial = new Material(coronaShader != null ? coronaShader : fallback);

            discObject = CreateQuad("StarRenderer_Disc", discMaterial, out discRenderer);
            glareObject = CreateQuad("StarRenderer_Glare", glareMaterial, out glareRenderer);
            coronaObject = CreateQuad("StarRenderer_Corona", coronaMaterial, out coronaRenderer);
        }

        private static Transform CreateQuad(string name, Material material, out Renderer renderer)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            quad.name = name;
            quad.layer = 0;
            renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            renderer.allowOcclusionWhenDynamic = false;
            return quad.transform;
        }

        private static void PositionQuad(
            Transform quad,
            Vector3 position,
            Vector3 forward,
            float renderDistance,
            double halfAngleRad)
        {
            if (quad == null)
            {
                return;
            }

            double halfSize = renderDistance * Math.Tan(Math.Min(1.3d, Math.Max(1e-9d, halfAngleRad)));
            if (!(halfSize > 0d) || double.IsInfinity(halfSize))
            {
                halfSize = 0.001d;
            }

            quad.position = position;
            Vector3 upHint = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f
                ? Vector3.right
                : Vector3.up;
            quad.rotation = Quaternion.LookRotation(forward, upHint);
            quad.localScale = new Vector3((float)(2d * halfSize), (float)(2d * halfSize), 1f);
        }

        private float ResolveRenderDistance(float far)
        {
            if (Preset != null && Preset.RenderDistanceMeters > 0d)
            {
                return Mathf.Clamp((float)Preset.RenderDistanceMeters, 1f, far * MaxProxyFarFraction);
            }

            float fraction = Mathf.Clamp(BillboardFarFraction, 0.01f, MaxProxyFarFraction);
            return Mathf.Clamp(far * fraction, 1f, far * MaxProxyFarFraction);
        }

        // ------------------------------------------------------------------
        // Eclipse geometry
        // ------------------------------------------------------------------

        private double ComputeEclipseVisibility(
            Vector3d observer,
            Vector3d starDirection,
            double starDistanceMeters,
            double starAngularRadius,
            double timeSeconds)
        {
            double visible = 1d;
            if (Runner.SystemState == null)
            {
                return 1d;
            }

            foreach (OrbitingBody body in Runner.SystemState.AllBodies)
            {
                if (body == null || ReferenceEquals(body, star) || !(body.Radius > 0d))
                {
                    continue;
                }

                body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
                Vector3d toBody = bodyPosition - observer;
                double bodyDistance = toBody.Magnitude;
                if (!(bodyDistance > 1d) || bodyDistance >= starDistanceMeters)
                {
                    continue;
                }

                double occluderRadius = Math.Asin(StarPhysics.Clamp(body.Radius / bodyDistance, 0d, 1d));
                double separation = Math.Acos(StarPhysics.Clamp(
                    Vector3d.Dot(starDirection, toBody / bodyDistance), -1d, 1d));
                visible *= StarPhysics.EclipseVisibleFraction(
                    starAngularRadius, occluderRadius, separation);

                if (visible <= 1e-6d)
                {
                    return 0d;
                }
            }

            return StarPhysics.Clamp(visible, 0d, 1d);
        }

        // ------------------------------------------------------------------
        // Diagnostics
        // ------------------------------------------------------------------

        private void LogSummary()
        {
            if (loggedSummary || state == null)
            {
                return;
            }

            loggedSummary = true;
            double diameter075 = state.AngularDiameterDeg(0.075d * StarPhysics.AuKm);
            double diameter1 = state.AngularDiameterDeg(StarPhysics.AuKm);
            double magnitude1 = state.ApparentMagnitude(StarPhysics.AuKm);
            Debug.Log(string.Format(
                "[StarRenderer] {0}: M={1:F3} M☉ → L={2:E4} L☉, R={3:E4} R☉ ({4:E1} км), " +
                "Teff={5:F0} K, g={6:F1} м/с², cell={7:F1} км, N={8:F0}, " +
                "spots={9:P2} (Tумбра={10:F0} K), eta/eta☉={11:F3}, V={12:E1} → V'={13:F2}, " +
                "d(0.075 а.е.)={14:F2}°, d(1 а.е.)={15:F2}′, m(1 а.е.)={16:F2}. " +
                "Пресет: {17}{18}",
                star != null ? star.Name : "звезда",
                state.MassSolar,
                state.LuminositySolar,
                state.RadiusSolar,
                state.RadiusKm,
                state.EffectiveTemperatureK,
                state.SurfaceGravityMps2,
                state.GranulationCellKm,
                state.GranulationCells,
                state.SpotCoverage,
                state.SpotUmbraTemperatureK,
                state.VisibleEtaRatioToSun,
                state.DiscBrightness,
                state.DiscBrightnessCompressed,
                diameter075,
                diameter1 * 60d,
                magnitude1,
                Preset != null ? Preset.name : "runtime",
                state.HasOverrides ? " (есть переопределения!)" : ""));
        }

        private void OnGUI()
        {
            if (!DebugHud)
            {
                return;
            }

            string lightState = SunLight != null ? "on" : "OFF";
            GUI.Label(new Rect(10f, 10f, 900f, 44f), string.Format(
                "Звезда: M={0:F2} M☉  Teff={1:F0} K  угол={2:F3}°  E={3:E3} Вт/м²  m={4:F2}  " +
                "диск={5} px{6}  f_vis={7:F3}  свет={8}",
                state != null ? state.MassSolar : 0d,
                state != null ? state.EffectiveTemperatureK : 0d,
                AngularDiameterDeg,
                IrradianceWm2,
                ApparentMagnitude,
                ScreenDiscPixels,
                IsSubpixel ? " (субпиксельный)" : string.Empty,
                EclipseVisibility,
                lightState));
            GUI.Label(new Rect(10f, 34f, 900f, 30f), string.Format(
                "Высота над горизонтом: {0:F1}°  |  V'={1:F2}  |  прокси {2:F0} м",
                sunElevationDeg,
                state != null ? state.DiscBrightnessCompressed : 0d,
                discObject != null ? Vector3.Distance(discObject.position, Camera.main != null
                    ? Camera.main.transform.position : Vector3.zero) : 0f));
        }

        /// <summary>
        /// Per-channel atmospheric transmittance applied to a star colour
        /// (linear). 1 = vacuum, so the photosphere keeps its true colour.
        /// </summary>
        private static Color Tinted(Vector3 transmittance, double r, double g, double b)
        {
            return new Color(
                (float)(r * transmittance.x),
                (float)(g * transmittance.y),
                (float)(b * transmittance.z),
                1f);
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }

            material = null;
        }

        private static void DestroyNow(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }
    }
}
