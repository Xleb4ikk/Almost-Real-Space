using System;
using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// Physical moonlight for the body under the camera.
    ///
    /// A moon is a mirror of the star: its light on the ground is
    ///   p · E_star · (R_moon / d)² · Φ(α) · f_eclipse,
    /// where p is the geometric albedo, α the phase angle (star–moon–observer) and
    /// Φ the empirical lunar phase law (see <see cref="MoonlightPhysics"/>). The
    /// result is multiplied by the observer's atmospheric transmittance along the
    /// MOON's own line of sight (so a low moon dims and reddens exactly like a low
    /// sun) and by the local horizon occlusion.
    ///
    /// The physical moon/sun ratio is ~2.9·10⁻⁶ — invisible under a day-calibrated
    /// exposure. <see cref="NightAdaptationGainEv"/> is the one non-physical step:
    /// the simulated eye's dark-adaptation gain, in EV. With 16 EV the game's full
    /// moon is ~0.15 of the noon sun, a moonlit night that a tonemapper can show.
    /// Every dynamic term (phase, distance, altitude, eclipse) stays physical.
    ///
    /// Outputs:
    ///   * shader globals _TerrainMoonDir / _MoonLightColor / _MoonAmbientColor,
    ///     consumed by the terrain, grass, decor and water shaders;
    ///   * a real Directional Light for HDRP/Lit objects;
    ///   * shadow ownership, because HDRP carries only ONE cascaded directional
    ///     light (HDShadowManager: "we only support directional light shadow").
    ///     The sun owns the cascades by day, the moon by night.
    ///
    /// Runs on the CAMERA body (Runner.CameraBody) — same rule as SkyEnvironment:
    /// standing on Artemis, Terra's moons must not light the lunar ground.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-50)]
    public sealed class MoonlightSystem : MonoBehaviour
    {
        [Header("Сцена")]
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Физический рендер звезды (цвет, светимость). Пусто — найдётся на сцене.")]
        public StarRenderer Star;

        [Tooltip("Физика неба: отсюда берутся TerrainRadianceScale/TerrainSunIntensity и SunLight. Пусто — найдётся.")]
        public SkyEnvironment Sky;

        [Tooltip("Directional Light солнца: им владеет SkyEnvironment/StarRenderer, здесь только переключается каскадная тень.")]
        public Light SunLight;

        [Tooltip("Directional Light луны. Должен существовать в сцене (дубликат Sun с HDAdditionalLightData). " +
                 "Интенсивность, цвет и поворот пишет этот компонент каждый кадр.")]
        public Light MoonLight;

        [Header("Луна")]
        // NOT serialized: Unity instantiates a plain [Serializable] class field as a
        // default "Body" (R = 1, no parent) even when the author never assigned one,
        // which silently disables auto-selection. The moon is chosen from
        // CameraBody.Children; use MoonName to pin a specific one.
        [NonSerialized]
        public OrbitingBody Moon;

        [Tooltip("Имя конкретной луны. Пусто — автоматически выбирается самый яркий спутник CameraBody.")]
        public string MoonName = string.Empty;

        [Tooltip("Геометрическое альбедо реголита. Реальная Луна 0.136; 0 — свет выключен.")]
        public double GeometricAlbedo = MoonlightPhysics.MoonGeometricAlbedo;

        [Tooltip("Общий множитель яркости (1 = физика + NightAdaptationGainEv).")]
        public float MoonlightScale = 1f;

        [Tooltip("Тёмновая адаптация, зашитая в САМ свет луны, EV. 0 = физика: луна отдаёт " +
                 "2.9e-6 полуденного солнца, и подъём делает зрачок (SkyEnvironment." +
                 "NightAdaptationMaxEv) — так ночь одна система, а не две. >0 — старый " +
                 "подход: адаптация запечена в лунный свет (14 EV ≈ 0.036 полудня), " +
                 "зрачок ночью почти не открывается. Оба сразу дают «дневной» асфальт.")]
        public float NightAdaptationGainEv = 0f;

        [Tooltip("Оттенок отражённого света. Луна отражает почти нейтральный свет; лёгкий холод " +
                 "передаёт сине-серое восприятие ночного зрения (эффект Пуркинье).")]
        public Color MoonlightTint = new Color(0.78f, 0.88f, 1f);

        [Tooltip("Доля прямого света луны, уходящая в небесную подсветку (свет ночного неба, " +
                 "рассеянный атмосферой). 0 = тени абсолютно чёрные.")]
        [Range(0f, 0.2f)]
        public float MoonSkyAmbient = 0.01f;

        [Header("Тени")]
        [Tooltip("Передавать луне единственный каскадный directional shadow HDRP, когда солнце село. " +
                 "Днём тени принадлежат солнцу, ночью — луне; одновременно они невозможны.")]
        public bool ShareShadowCascades = true;

        [Tooltip("Солнце ниже этой высоты (°) — луна забирает каскады.")]
        public float MoonShadowEnterSunElevationDeg = -2f;

        [Tooltip("Солнце выше этой высоты (°) — каскады возвращаются солнцу (гистерезис).")]
        public float MoonShadowExitSunElevationDeg = -0.5f;

        [Tooltip("Ниже этой высоты луны (°) её тень не нужна.")]
        public float MoonShadowMinElevationDeg = 1f;

        [Header("Диагностика")]
        [Tooltip("Раз в 2 с писать фазу, высоту, отношение и яркость в консоль.")]
        public bool DebugLog;

        [Tooltip("HUD с параметрами луны.")]
        public bool DebugHud;

        // ------------------------------------------------------------------
        // Diagnostics (read by tests/HUD)
        // ------------------------------------------------------------------

        public OrbitingBody ActiveMoon { get; private set; }
        public Vector3 MoonDirectionSim { get; private set; }
        public float MoonElevationDeg { get; private set; }
        public float PhaseAngleDeg { get; private set; }
        public float IlluminatedFraction { get; private set; }
        public float MoonVisibility { get; private set; }
        public float EclipseVisibility { get; private set; }
        public float IrradianceRatioToSun { get; private set; }
        public float SceneIntensity { get; private set; }
        public float IlluminanceLux { get; private set; }
        public float SunElevationDeg { get; private set; }

        private static readonly int TerrainMoonDirId = Shader.PropertyToID("_TerrainMoonDir");
        private static readonly int MoonLightColorId = Shader.PropertyToID("_MoonLightColor");
        private static readonly int MoonAmbientColorId = Shader.PropertyToID("_MoonAmbientColor");
        private static readonly int MoonShadowActiveId = Shader.PropertyToID("_MoonShadowActive");

        private LightShadows sunShadowMode = LightShadows.Soft;
        private bool sunShadowModeStored;
        private bool moonOwnsShadows;
        private bool shadowOwnerApplied;
        private bool shadowOwnerWasMoon;
        private double nextLogTime;

        // Editor preview dedupe (same contract as SkyEnvironment: never touch the
        // scene's lights outside Play, only the shader globals).
        private float editNextSyncTime;
        private OrbitingBody editLastBody;
        private Vector3d editLastObserver;
        private double editLastTime = double.NaN;

        private struct MoonCandidate
        {
            public OrbitingBody Body;
            public Vector3d ToMoon;
            public double Distance;
            public double PhaseAngle;
            public double Eclipse;
            public double Ratio;
            public double SinElevation;
            public double Visibility;
            public double Score;
        }

        private void Start()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (Star == null)
            {
                Star = FindAnyObjectByType<StarRenderer>();
            }

            if (Sky == null)
            {
                Sky = FindAnyObjectByType<SkyEnvironment>();
            }

            if (SunLight == null && Sky != null)
            {
                SunLight = Sky.SunLight;
            }

            if (MoonLight == null)
            {
                foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                {
                    if (light.type != LightType.Directional || light == SunLight)
                    {
                        continue;
                    }

                    string lower = light.name.ToLowerInvariant();
                    if (lower.Contains("moon") || lower.Contains("лун"))
                    {
                        MoonLight = light;
                        break;
                    }
                }
            }

            // In Play the body tree must exist; in the editor preview the SurfaceFrame
            // resolves the body instead, so the runner may be unbuilt there.
            if (Application.isPlaying && (Runner == null || Runner.SystemState == null))
            {
                Debug.LogWarning("[MoonlightSystem] ОТКЛЮЧЁН: нет Runner/SystemState.");
                enabled = false;
                return;
            }

            if (!string.IsNullOrEmpty(MoonName) && Runner != null && Runner.SystemState != null)
            {
                foreach (OrbitingBody body in Runner.SystemState.AllBodies)
                {
                    if (body != null && body.Name == MoonName)
                    {
                        Moon = body;
                        break;
                    }
                }

                if (Moon == null)
                {
                    Debug.LogWarning("[MoonlightSystem] MoonName \"" + MoonName +
                        "\" не найдена — включён автоподбор спутника CameraBody.");
                }
            }

            if (MoonLight == null)
            {
                Debug.LogWarning(
                    "[MoonlightSystem] ОТКЛЮЧЁН: не назначен MoonLight. Нужен Directional Light " +
                    "с HDAdditionalLightData (дубликат Sun).");
                enabled = false;
                return;
            }

            // Light writes only in Play: in the editor the duplicated Sun already
            // carries the right shadow tier, and touching the scene here would dirty it
            // on every domain reload (the editor preview only needs the globals).
            if (Application.isPlaying)
            {
                if (SunLight != null && !sunShadowModeStored)
                {
                    sunShadowMode = SunLight.shadows;
                    sunShadowModeStored = true;
                }

                // Same rule as the sun: the HDRP asset decides the cascade resolution;
                // the moon inherits the sun's tier by being a duplicate of the Sun object.
                HDAdditionalLightData moonHd = MoonLight.GetComponent<HDAdditionalLightData>();
                if (moonHd != null)
                {
                    moonHd.SetShadowResolutionOverride(false);
                }

                if (ShareShadowCascades)
                {
                    MoonLight.shadows = LightShadows.None;
                }
            }

            Shader.SetGlobalVector(TerrainMoonDirId, Vector4.zero);
            Shader.SetGlobalVector(MoonLightColorId, Vector4.zero);
            Shader.SetGlobalVector(MoonAmbientColorId, Vector4.zero);
            Shader.SetGlobalFloat(MoonShadowActiveId, 0f);
        }

        private void OnDisable()
        {
            // Shader globals survive scene loads: a scene without a MoonlightSystem
            // must not inherit the previous scene's moon light.
            Shader.SetGlobalVector(TerrainMoonDirId, Vector4.zero);
            Shader.SetGlobalVector(MoonLightColorId, Vector4.zero);
            Shader.SetGlobalVector(MoonAmbientColorId, Vector4.zero);
            Shader.SetGlobalFloat(MoonShadowActiveId, 0f);
            if (Sky != null)
            {
                Sky.MoonlightLevel = 0f;
            }

            if (Application.isPlaying)
            {
                SetMoonShadowOwner(false);
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                EditorSyncGlobals();
                return;
            }

            if (Runner == null || Runner.SystemState == null || Runner.SystemState.Root == null
                || Runner.CameraBody == null)
            {
                return;
            }

            ApplyNow(Runner.CameraBody, ObserverPosition(), Runner.TimeSeconds, applyToLight: true);
        }

        /// <summary>Camera eye, exactly like SkyEnvironment: the seat position can lag the craft.</summary>
        private Vector3d ObserverPosition()
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                return FloatingOrigin.Anchor + AstroFrame.ToAstro(camera.transform.position);
            }

            return Runner.PlayerPosition;
        }

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
            double time = Sky != null ? Sky.EditorTimeSeconds : 0d;
            bool same = ReferenceEquals(frame.BodyState, editLastBody)
                && (observer - editLastObserver).Magnitude < 1d
                && editLastTime == time;
            if (same)
            {
                return;
            }

            editLastBody = frame.BodyState;
            editLastObserver = observer;
            editLastTime = time;

            ApplyNow(frame.BodyState, observer, time, applyToLight: false);
        }

        /// <summary>
        /// Recompute the moon and write the globals. Extracted so the editor surface
        /// preview gets exactly the game's light, and so tests can drive an arbitrary
        /// body/observer/time.
        /// </summary>
        public void ApplyNow(OrbitingBody body, Vector3d observerPosition, double timeSeconds, bool applyToLight)
        {
            if (body == null)
            {
                return;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            Vector3d relative = observerPosition - bodyPosition;
            double observerDistance = relative.Magnitude;
            double altitude = Math.Max(0d, observerDistance - body.Radius);
            Vector3d up = observerDistance > 0d ? relative / observerDistance : new Vector3d(0d, 0d, 1d);

            OrbitingBody root = SkyEnvironment.RootOf(body);
            if (root == null)
            {
                return;
            }

            root.EvaluateWorldState(timeSeconds, out Vector3d starPosition, out _);
            Vector3d toStar = starPosition - observerPosition;
            double starDistance = toStar.Magnitude;
            Vector3d sunDirection = starDistance > 0d ? toStar / starDistance : up;
            double sunSinElevation = Vector3d.Dot(sunDirection, up);
            SunElevationDeg = (float)(Math.Asin(SkyPhysics.Clamp(sunSinElevation, -1d, 1d)) * (180d / Math.PI));

            AtmosphereProfile atmosphere = body.Atmosphere;
            AtmosphereOptics.Coefficients coefficients = atmosphere != null
                ? atmosphere.ToOptics()
                : AtmosphereOptics.FromProfile(0d, 0d, 0d, 0d, false);
            double topAltitude = atmosphere != null ? atmosphere.TopAltitudeMeters : 0d;

            MoonCandidate best = default;
            best.Score = double.NegativeInfinity;

            // The light of a moon is relevant only to its parent body: on Artemis
            // there is no lunar moonlight to light the lunar ground. The star has no
            // ground at all: its children are planets, not moons, and treating the
            // nearest one as a moon at 1e11 m stole the shadow cascade mid-transfer.
            if (body.Parent == null)
            {
                // best stays at -Infinity: the block below clears every moon global.
            }
            else if (Moon != null)
            {
                MoonCandidate candidate = EvaluateCandidate(
                    Moon, bodyPosition, body.Radius, observerPosition, altitude,
                    timeSeconds, starPosition);
                if (candidate.Score > best.Score)
                {
                    best = candidate;
                }
            }
            else
            {
                for (int i = 0; i < body.Children.Count; i++)
                {
                    OrbitingBody child = body.Children[i];
                    if (child == null || !(child.Radius > 0d))
                    {
                        continue;
                    }

                    MoonCandidate candidate = EvaluateCandidate(
                        child, bodyPosition, body.Radius, observerPosition, altitude,
                        timeSeconds, starPosition);
                    if (candidate.Score > best.Score)
                    {
                        best = candidate;
                    }
                }
            }

            if (!(best.Score > 0d) || best.Body == null)
            {
                ActiveMoon = null;
                MoonDirectionSim = Vector3.zero;
                MoonElevationDeg = 0f;
                PhaseAngleDeg = 0f;
                IlluminatedFraction = 0f;
                MoonVisibility = 0f;
                EclipseVisibility = 0f;
                IrradianceRatioToSun = 0f;
                SceneIntensity = 0f;
                IlluminanceLux = 0f;
                if (Sky != null)
                {
                    Sky.MoonlightLevel = 0f;
                }

                Shader.SetGlobalVector(TerrainMoonDirId, Vector4.zero);
                Shader.SetGlobalVector(MoonLightColorId, Vector4.zero);
                Shader.SetGlobalVector(MoonAmbientColorId, Vector4.zero);
                Shader.SetGlobalFloat(MoonShadowActiveId, 0f);

                if (applyToLight && MoonLight != null)
                {
                    MoonLight.intensity = 0f;
                    SetMoonShadowOwner(false);
                }

                return;
            }

            ActiveMoon = best.Body;
            MoonDirectionSim = AstroFrame.ToSimulation(best.ToMoon);
            MoonElevationDeg = (float)(Math.Asin(SkyPhysics.Clamp(best.SinElevation, -1d, 1d)) * (180d / Math.PI));
            PhaseAngleDeg = (float)(best.PhaseAngle * (180d / Math.PI));
            IlluminatedFraction = (float)MoonlightPhysics.IlluminatedFraction(best.PhaseAngle);
            MoonVisibility = (float)best.Visibility;
            EclipseVisibility = (float)best.Eclipse;
            IrradianceRatioToSun = (float)best.Ratio;

            // The moon's light crosses the SAME atmosphere as the sun's, but along
            // the moon's own line of sight: a low moon is dimmer and redder, at a
            // different time of night than the sun at dusk.
            Vector3d transmittance;
            if (atmosphere == null || topAltitude <= 0d || altitude >= topAltitude)
            {
                transmittance = new Vector3d(1d, 1d, 1d);
            }
            else
            {
                transmittance = AtmosphereOptics.TransmittanceToSpace(
                    coefficients, body.Radius, body.Radius + topAltitude,
                    body.Radius + altitude, best.SinElevation);
            }

            // top-of-atmosphere scene brightness; the colour vector below carries T
            double sceneIntensity = MoonlightPhysics.SceneIntensity(best.Ratio, NightAdaptationGainEv)
                * Math.Max(0f, MoonlightScale) * best.Visibility;

            double transmittanceLuminance = (0.2126d * transmittance.X)
                + (0.7152d * transmittance.Y) + (0.0722d * transmittance.Z);

            if (Star != null && Star.IsReady)
            {
                IlluminanceLux = (float)(MoonlightPhysics.IlluminanceLux(best.Ratio, Star.IrradianceWm2)
                    * transmittanceLuminance);
            }
            else
            {
                IlluminanceLux = 0f;
            }

            Vector3d starColor = Star != null && Star.IsReady
                ? Star.LightColorLinear
                : new Vector3d(1d, 1d, 1d);

            Vector3d moonColor = new Vector3d(
                starColor.X * transmittance.X * MoonlightTint.r,
                starColor.Y * transmittance.Y * MoonlightTint.g,
                starColor.Z * transmittance.Z * MoonlightTint.b);

            double peak = Math.Max(moonColor.X, Math.Max(moonColor.Y, moonColor.Z));
            if (!(peak > 1e-9d))
            {
                peak = 1d;
            }

            Vector3 normalised = new Vector3(
                (float)(moonColor.X / peak),
                (float)(moonColor.Y / peak),
                (float)(moonColor.Z / peak));

            // The transmittance is part of moonColor, so the final vector is
            // normalised × (sceneIntensity · peak) — a low moon really loses flux,
            // it is not just recoloured.
            double colorScale = sceneIntensity * peak;
            double colorLuminance = (0.2126d * normalised.x)
                + (0.7152d * normalised.y) + (0.0722d * normalised.z);

            SceneIntensity = (float)colorScale;
            if (Sky != null)
            {
                // The pupil reads the PHYSICAL illuminance, before the night gain is
                // folded in below — otherwise the gain would feed itself.
                // Exposure follows the luminous, not the peak channel: a reddened
                // low moon should open the night exposure less.
                Sky.MoonlightLevel = (float)(colorScale * colorLuminance);
            }

            // Where the night lift lives. The moon is physical (NightAdaptationGainEv = 0),
            // the scene's pupil is not: it is fully open at night (SkyEnvironment.EyeEv),
            // and its linear factor multiplies the moon's light exactly as _TerrainSun
            // multiplies the star's. Keeping it here (and not in the shader's `sun`) is
            // what keeps the star term at 1x at night — otherwise a sunlit planet seen
            // from its own night side was bleached white (measured 2026-11-24: _TerrainSun
            // = 1 745 at 54 000 km with the sun 5.5 deg below the horizon).
            // In daylight / in orbit the gain is ~1 and the moon stays physical.
            double moonGain = Sky != null && Sky.MoonlightGain > 0.1f ? Sky.MoonlightGain : 1d;
            double litScene = colorScale * moonGain;

            Vector3 lightColor = normalised * (float)litScene;
            Vector3 ambientColor = normalised
                * (float)(litScene * Math.Max(0d, MoonSkyAmbient));

            Shader.SetGlobalVector(TerrainMoonDirId, MoonDirectionSim);
            Shader.SetGlobalVector(MoonLightColorId, lightColor);
            Shader.SetGlobalVector(MoonAmbientColorId, ambientColor);

            bool wantMoonShadows = applyToLight
                && ShareShadowCascades
                && best.SinElevation > Math.Sin(MoonShadowMinElevationDeg * Math.PI / 180d)
                && litScene > 1e-5d
                && SunElevationDeg < MoonShadowEnterSunElevationDeg;

            if (moonOwnsShadows
                && (SunElevationDeg > MoonShadowExitSunElevationDeg
                    || best.SinElevation <= Math.Sin(MoonShadowMinElevationDeg * Math.PI / 180d)
                    || litScene <= 1e-5d))
            {
                moonOwnsShadows = false;
            }
            else if (wantMoonShadows)
            {
                moonOwnsShadows = true;
            }

            Shader.SetGlobalFloat(MoonShadowActiveId, moonOwnsShadows ? 1f : 0f);

            if (applyToLight && MoonLight != null)
            {
                float calibration = Sky != null
                    ? Sky.TerrainRadianceScale * Sky.TerrainSunIntensity * Sky.MaterialLightScale
                    : 1f;

                // The custom ground shaders live in the normalised "sun = 1" scale;
                // the moon light must land on the same scale or a white wall and the
                // white ground under the moon would disagree. (The sun's HDRP light
                // additionally carries SceneLightScale; that is the sun's calibration,
                // not the moon's.)
                //
                // litScene already carries the night pupil (moonGain), the same factor the
                // ground's moon term gets through _MoonLightColor — so the HDRP/Lit props
                // (launch pad, roads, VAB) and the custom terrain share one exposure.
                MoonLight.color = new Color(normalised.x, normalised.y, normalised.z, 1f);
                MoonLight.intensity = (float)(Math.PI * litScene * calibration);

                if (litScene > 1e-9d)
                {
                    MoonLight.transform.rotation = Quaternion.LookRotation(-MoonDirectionSim);
                }

                HDAdditionalLightData moonHd = MoonLight.GetComponent<HDAdditionalLightData>();
                if (moonHd != null && best.Distance > body.Radius)
                {
                    // The moon's shadow penumbra is the moon's real angular size
                    // (0.5° on Terra), not the sun's (1.5° at 0.37 AU).
                    moonHd.angularDiameter = (float)(
                        2d * Math.Atan(best.Body.Radius / best.Distance) * (180d / Math.PI));
                }

                SetMoonShadowOwner(moonOwnsShadows);
            }
            else
            {
                Shader.SetGlobalFloat(MoonShadowActiveId, 0f);
            }

            if (DebugLog && Application.isPlaying && timeSeconds >= nextLogTime)
            {
                nextLogTime = timeSeconds + 2d;
                Debug.Log(string.Format(
                    "[Moonlight] {0}: el={1:F1}° phase={2:F1}° lit={3:P0} eclipse={4:P0} " +
                    "ratio={5:E2} scene={6:E2} lux={7:F2} sunEl={8:F1}° shadows={9}",
                    ActiveMoon != null ? ActiveMoon.Name : "—",
                    MoonElevationDeg, PhaseAngleDeg, IlluminatedFraction, EclipseVisibility,
                    IrradianceRatioToSun, SceneIntensity, IlluminanceLux, SunElevationDeg,
                    moonOwnsShadows ? "moon" : "sun"));
            }
        }

        private MoonCandidate EvaluateCandidate(
            OrbitingBody moon,
            Vector3d bodyPosition,
            double bodyRadius,
            Vector3d observerPosition,
            double altitude,
            double timeSeconds,
            Vector3d starPosition)
        {
            MoonCandidate candidate = default;
            candidate.Body = moon;
            moon.EvaluateWorldState(timeSeconds, out Vector3d moonPosition, out _);

            Vector3d toMoon = moonPosition - observerPosition;
            double distance = toMoon.Magnitude;
            if (!(distance > moon.Radius))
            {
                return candidate;
            }

            candidate.ToMoon = toMoon / distance;
            candidate.Distance = distance;
            candidate.PhaseAngle = MoonlightPhysics.PhaseAngleRad(moonPosition, starPosition, observerPosition);

            // The moon can be shadowed by its parent, by a sister moon, or by any
            // other body of the system: use the one shadow rule, not a parent-only
            // special case. Editor preview has no StarSystem, so walk the tree.
            candidate.Eclipse = Runner != null && Runner.SystemState != null
                ? Runner.SystemState.StarVisibilityForBody(moon, timeSeconds, out _)
                : StarOcclusion.VisibilityForBody(moon, timeSeconds, out _);

            candidate.Ratio = MoonlightPhysics.IrradianceRatioToSun(
                GeometricAlbedo, moon.Radius, distance, candidate.PhaseAngle, candidate.Eclipse);

            if (!(candidate.Ratio > 0d))
            {
                return candidate;
            }

            Vector3d relative = observerPosition - bodyPosition;
            double observerDistance = relative.Magnitude;
            Vector3d up = observerDistance > 0d ? relative / observerDistance : new Vector3d(0d, 0d, 1d);
            candidate.SinElevation = Vector3d.Dot(candidate.ToMoon, up);
            candidate.Visibility = SkyPhysics.SunOcclusion(candidate.SinElevation, altitude, bodyRadius);

            // Rank by what actually lands on a horizontal surface: brightness
            // (phase + eclipse + distance) × how high the moon is above the horizon.
            candidate.Score = candidate.Ratio * candidate.Visibility * Math.Max(0d, candidate.SinElevation);
            return candidate;
        }

        private void SetMoonShadowOwner(bool moonOwns)
        {
            if (MoonLight == null)
            {
                return;
            }

            // Light.shadows writes dirty the HDRP light and can rebuild the shadow
            // atlas; write only when the owner actually changes (twice per day).
            if (shadowOwnerApplied && shadowOwnerWasMoon == moonOwns)
            {
                return;
            }

            shadowOwnerApplied = true;
            shadowOwnerWasMoon = moonOwns;

            if (moonOwns)
            {
                if (SunLight != null && sunShadowModeStored)
                {
                    SunLight.shadows = LightShadows.None;
                }

                MoonLight.shadows = LightShadows.Soft;
            }
            else
            {
                MoonLight.shadows = LightShadows.None;
                if (SunLight != null && sunShadowModeStored)
                {
                    SunLight.shadows = sunShadowMode;
                }
            }
        }

        private void OnGUI()
        {
            if (!DebugHud)
            {
                return;
            }

            GUI.Label(new Rect(10f, 64f, 900f, 44f), string.Format(
                "Луна: {0}  высота={1:F1}°  фаза={2:F1}°  освещено={3:P0}  затмение={4:P0}  " +
                "луна/солнце={5:E2}  сцена={6:E2}  люкс={7:F2}  тень={8}",
                ActiveMoon != null ? ActiveMoon.Name : "—",
                MoonElevationDeg,
                PhaseAngleDeg,
                IlluminatedFraction,
                EclipseVisibility,
                IrradianceRatioToSun,
                SceneIntensity,
                IlluminanceLux,
                moonOwnsShadows ? "луна" : "солнце"));
        }
    }
}
