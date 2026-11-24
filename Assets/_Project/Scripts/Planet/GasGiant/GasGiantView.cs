using System;
using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Galilego.Universe
{
    /// <summary>
    /// Gas giant view. The body is a normal BodyAuthoring/BodyView object; this
    /// component only builds the visual:
    ///   * NEAR: a cube-sphere with Galilego/GasGiantSurface. The mesh is a unit
    ///     sphere scaled to (R, R, R*oblateness); normals use inverse-transpose
    ///     (TransformObjectToWorldNormal), so the ellipsoid is lit correctly.
    ///   * FAR: a camera-facing quad with Galilego/GasGiantDisc. The disc is not
    ///     a circle: the shader solves the oblate ellipsoid for every pixel, so
    ///     the silhouette and normals are correct even for a tilted axis.
    ///
    /// The gas giant is usually far beyond the camera's physical far plane (the
    /// FirstPersonCamera sets it to the horizon). Like StarRenderer, the far
    /// disc is placed at 0.25*far with the true angular radius, and switches to
    /// the real mesh when the body fits inside the far plane.
    ///
    /// Lighting direction is computed from the actual body/star positions:
    /// normalize(starPos - bodyPos), NOT the observer's _TerrainSunDir.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class GasGiantView : MonoBehaviour
    {
        public SimulationRunner Runner;

        [Tooltip("РљР°РјРµСЂР° РёРіСЂРѕРєР°. РџСѓСЃС‚Рѕ вЂ” Camera.main.")]
        public Camera PlayerCamera;

        [Tooltip("РџСЂРµСЃРµС‚. Р•СЃР»Рё РїСѓСЃС‚Рѕ вЂ” Р±РµСЂС‘С‚СЃСЏ BodyAuthoring.GasGiantPreset СЃ СЌС‚РѕРіРѕ Р¶Рµ РѕР±СЉРµРєС‚Р°.")]
        public GasGiantProfileAsset Profile;

        [Tooltip("РњР°С‚РµСЂРёР°Р» Р±Р»РёР¶РЅРµР№ СЃС„РµСЂС‹ (Galilego/GasGiantSurface). РџСѓСЃС‚Рѕ вЂ” СЃРѕР·РґР°С‘С‚СЃСЏ РІ СЂР°РЅС‚Р°Р№РјРµ.")]
        public Material SurfaceMaterial;

        [Tooltip("РњР°С‚РµСЂРёР°Р» РґР°Р»СЊРЅРµРіРѕ РґРёСЃРєР° (Galilego/GasGiantDisc). РџСѓСЃС‚Рѕ вЂ” СЃРѕР·РґР°С‘С‚СЃСЏ РІ СЂР°РЅС‚Р°Р№РјРµ.")]
        public Material DiscMaterial;

        public bool RenderNearSphere = true;
        public bool RenderFarDisc = true;

        [Tooltip("РњРёРЅРёРјР°Р»СЊРЅС‹Р№ СЌРєСЂР°РЅРЅС‹Р№ СЂР°РґРёСѓСЃ РґРёСЃРєР°, px. 0 = СЃС‚СЂРѕРіРѕ С„РёР·РёС‡РµСЃРєРёР№ СЂР°Р·РјРµСЂ (РЅР° 1080p Aeolus Р±СѓРґРµС‚ СЃСѓР±РїРёРєСЃРµР»СЊРЅС‹Рј).")]
        [Range(0f, 8f)]
        public float MinScreenRadiusPixels = 2f;

        [Tooltip("Минимальный экранный радиус, px, при котором ещё рисуется НАСТОЯЩАЯ сфера. " +
            "Меньше — диск: меш на 0.3 px только мерцает, а диск держит честный угловой размер " +
            "с полом MinScreenRadiusPixels. Порог не зависит от far-плоскости: в перелёте far — " +
            "это горизонт Солнца (~1e11 м), и одного «помещается в far» мало.")]
        [Range(0f, 64f)]
        public float MeshHandoverPixels = 8f;

        [Tooltip("HDR-СЏСЂРєРѕСЃС‚СЊ Р±Р»РёР¶РЅРµР№ СЃС„РµСЂС‹. Р¤РёР·РёС‡РµСЃРєР°СЏ РїРѕРІРµСЂС…РЅРѕСЃС‚СЊ РіР°Р·РѕРІРѕРіРѕ РіРёРіР°РЅС‚Р° С‚РµРјРЅРµРµ, С‡РµРј РѕР¶РёРґР°РµС‚ С‚РѕРЅРјР°РїРїРёРЅРі СЃС†РµРЅС‹; 1 = СЃС‚СЂРѕРіРѕ С„РёР·РёС‡РµСЃРєРё.")]
        [Range(0.2f, 5f)]
        public float SurfaceBrightness = 3.0f;

        [Tooltip("HDR-СЏСЂРєРѕСЃС‚СЊ РґР°Р»СЊРЅРµРіРѕ РґРёСЃРєР°. Р¤РёР·РёС‡РµСЃРєР°СЏ РїРѕРІРµСЂС…РЅРѕСЃС‚СЊ РіР°Р·РѕРІРѕРіРѕ РіРёРіР°РЅС‚Р° РЅР° С‚Р°РєРѕР№ РґРёСЃС‚Р°РЅС†РёРё СЃР»РёС€РєРѕРј С‚СѓСЃРєР»Р°СЏ РґР»СЏ С‚РѕРЅРјР°РїРїРёРЅРіР° СЃС†РµРЅС‹.")]
        [Range(1f, 100f)]
        public float FarDiscBrightness = 3f;   // = SurfaceBrightness: the disc and the near sphere match at the hand-over.

        [Tooltip("РџРѕР» СЏСЂРєРѕСЃС‚Рё РїРѕСЃР»Рµ РєРѕРјРїРµРЅСЃР°С†РёРё РјРёРЅРёРјР°Р»СЊРЅРѕРіРѕ СЂР°РґРёСѓСЃР°. 0 = СЃС‚СЂРѕРіР°СЏ СЌРЅРµСЂРіРѕСЃР±РµСЂРµРіР°СЋС‰Р°СЏ РєРѕРјРїРµРЅСЃР°С†РёСЏ (РґРёСЃРє РїРѕС‡С‚Рё РіР°СЃРЅРµС‚).")]
        [Range(0f, 1f)]
        public float MinEnergyScale = 0.3f;     // keeps the old distant look while the 8 px switch is brightness-matched.

        [Range(16, 192)]
        public int MeshResolution = 96;

        public bool LogDiagnostics = true;

        private static readonly int EyeGainId = Shader.PropertyToID("_EyeGain");

        private OrbitingBody body;
        private OrbitingBody star;
        private StarRenderer starRenderer;
        private GasGiantProfile profile;
        private Material surfaceMaterial;
        private Material discMaterial;
        private Texture2D bandLut;
        private Texture2D windLut;
        private Mesh nearMesh;
        private Mesh discMesh;
        private Transform nearVisual;
        private Transform farDisc;
        private Renderer nearRenderer;
        private Renderer farRenderer;
        private bool initialized;
        private bool loggedStart;
        private int vortexCount;
        private readonly Vector4[] vortexParams0 = new Vector4[8];
        private readonly Vector4[] vortexParams1 = new Vector4[8];
        private readonly Vector4[] vortexCores = new Vector4[8];
        private readonly Vector4[] vortexCollars = new Vector4[8];

        private void Start()
        {
            if (Runner == null || Runner.SystemState == null)
            {
                Debug.LogWarning("[GasGiant] Runner/SystemState РЅРµ РЅР°Р·РЅР°С‡РµРЅ вЂ” РІРёРґ РѕС‚РєР»СЋС‡С‘РЅ.");
                enabled = false;
                return;
            }

            foreach (OrbitingBody candidate in Runner.SystemState.AllBodies)
            {
                if (candidate.Name == gameObject.name)
                {
                    body = candidate;
                    break;
                }
            }

            if (body == null)
            {
                Debug.LogWarning("[GasGiant] С‚РµР»Рѕ \"" + gameObject.name + "\" РЅРµ РЅР°Р№РґРµРЅРѕ РІ СЃРёСЃС‚РµРјРµ вЂ” РІРёРґ РѕС‚РєР»СЋС‡С‘РЅ.");
                enabled = false;
                return;
            }

            star = Runner.SystemState.Root;
            if (star == null)
            {
                Debug.LogWarning("[GasGiant] Сѓ СЃРёСЃС‚РµРјС‹ РЅРµС‚ РєРѕСЂРЅРµРІРѕР№ Р·РІРµР·РґС‹ вЂ” РІРёРґ РѕС‚РєР»СЋС‡С‘РЅ.");
                enabled = false;
                return;
            }

            starRenderer = FindAnyObjectByType<StarRenderer>();

            BodyAuthoring authoring = GetComponent<BodyAuthoring>();
            GasGiantProfileAsset preset = Profile != null
                ? Profile
                : authoring != null ? authoring.GasGiantPreset : null;

            if (preset == null || preset.Profile == null)
            {
                Debug.LogWarning("[GasGiant] Сѓ С‚РµР»Р° \"" + gameObject.name +
                    "\" РЅРµС‚ GasGiantPreset (Profile) вЂ” РІРёРґ РѕС‚РєР»СЋС‡С‘РЅ.");
                enabled = false;
                return;
            }

            profile = preset.Profile.Clone();
            profile.Validate();
            if (!profile.VisualEnabled)
            {
                enabled = false;
                return;
            }

            MeshResolution = Mathf.Clamp(MeshResolution > 0 ? MeshResolution : profile.MeshResolution, 16, 192);
            MinScreenRadiusPixels = Mathf.Max(MinScreenRadiusPixels, profile.MinScreenRadiusPixels);

            BuildLuts();
            BuildMaterials();
            BuildVisuals();
            initialized = true;

            if (LogDiagnostics)
            {
                Debug.Log(string.Format(
                    "[GasGiant] {0}: R={1:N0} Рј, oblateness={2:F3}, bands={3}, winds={4}, vortices={5}, " +
                    "mesh={6}, minPx={7}",
                    body.Name, body.Radius, profile.OblatenessRatio,
                    profile.Bands.Count, profile.Winds.Count, profile.Vortices.Count,
                    MeshResolution, MinScreenRadiusPixels));
            }
        }

        private void OnDestroy()
        {
            if (nearVisual != null)
            {
                Destroy(nearVisual.gameObject);
            }

            if (farDisc != null)
            {
                Destroy(farDisc.gameObject);
            }

            if (nearMesh != null)
            {
                Destroy(nearMesh);
            }

            if (discMesh != null)
            {
                Destroy(discMesh);
            }

            if (bandLut != null)
            {
                Destroy(bandLut);
            }

            if (windLut != null)
            {
                Destroy(windLut);
            }

            if (surfaceMaterial != null)
            {
                Destroy(surfaceMaterial);
            }

            if (discMaterial != null)
            {
                Destroy(discMaterial);
            }
        }

        private void BuildLuts()
        {
            profile.BuildLuts(out Vector4[] bandPixels, out Vector4[] windPixels);

            bandLut = new Texture2D(profile.BandLutWidth, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "AeolusBandLut",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };
            bandLut.SetPixelData(bandPixels, 0);
            bandLut.Apply(false, false);

            windLut = new Texture2D(profile.WindLutWidth, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "AeolusWindLut",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };
            windLut.SetPixelData(windPixels, 0);
            windLut.Apply(false, false);
        }

        private void BuildMaterials()
        {
            Shader surfaceShader = Shader.Find("Galilego/GasGiantSurface");
            Shader discShader = Shader.Find("Galilego/GasGiantDisc");
            if (surfaceShader == null || discShader == null)
            {
                Debug.LogError("[GasGiant] РЅРµ РЅР°Р№РґРµРЅС‹ С€РµР№РґРµСЂС‹ Galilego/GasGiantSurface РёР»Рё Galilego/GasGiantDisc.");
                enabled = false;
                return;
            }

            surfaceMaterial = SurfaceMaterial != null
                ? new Material(SurfaceMaterial) { name = "AeolusSurface (runtime)" }
                : new Material(surfaceShader) { name = "AeolusSurface (runtime)" };
            if (surfaceMaterial.shader == null || surfaceMaterial.shader.name != "Galilego/GasGiantSurface")
            {
                surfaceMaterial.shader = surfaceShader;
            }

            discMaterial = DiscMaterial != null
                ? new Material(DiscMaterial) { name = "AeolusDisc (runtime)" }
                : new Material(discShader) { name = "AeolusDisc (runtime)" };
            if (discMaterial.shader == null || discMaterial.shader.name != "Galilego/GasGiantDisc")
            {
                discMaterial.shader = discShader;
            }

            ConfigureMaterial(surfaceMaterial, false);
            ConfigureMaterial(discMaterial, true);
        }

        private void ConfigureMaterial(Material material, bool isDisc)
        {
            material.SetTexture("_GG_BandLut", bandLut);
            material.SetTexture("_GG_WindLut", windLut);
            if (profile.SurfaceMap != null)
            {
                material.SetTexture("_GG_SurfaceMap", profile.SurfaceMap);
                material.SetFloat("_GG_SurfaceMapStrength", profile.SurfaceMapStrength);
                material.SetFloat("_GG_MapLongitudeOffset", profile.SurfaceMapLongitudeOffset);
                material.SetFloat("_GG_MapBump", profile.SurfaceMapBump);
            }
            else
            {
                material.SetTexture("_GG_SurfaceMap", null);
                material.SetFloat("_GG_SurfaceMapStrength", 0f);
            }

            ApplyProfileConstants(material);

            if (isDisc)
            {
                material.SetFloat("_GG_DiscDistance", 1e30f);
            }
        }

        /// <summary>
        /// Pushes every profile constant that is not time-dependent. Called once
        /// in Start and again every frame: with Enter-Play-Mode Options the
        /// runtime material can survive a domain reload, and a material created
        /// from an asset can be reused before its hidden (Properties-less)
        /// uniforms were written. Re-applying makes the shader deterministic.
        /// </summary>
        private void ApplyProfileConstants(Material material)
        {
            if (material == null || profile == null)
            {
                return;
            }

            material.SetColor("_GG_NorthPoleColor", profile.NorthPoleColor);
            material.SetColor("_GG_SouthPoleColor", profile.SouthPoleColor);
            material.SetColor("_GG_HazeColor", profile.HazeColor);
            material.SetColor("_GG_RimColor", profile.RimColor);

            material.SetFloat("_GG_Oblateness", profile.OblatenessRatio);
            material.SetFloat("_GG_ShearExaggeration", profile.ShearExaggeration);
            material.SetFloat("_GG_WarpAmplitude", profile.WarpAmplitude);
            material.SetFloat("_GG_WarpScale", profile.WarpScale);
            material.SetFloat("_GG_TurbulenceScale", profile.TurbulenceScale);
            material.SetFloat("_GG_TurbulenceAnisotropy", profile.TurbulenceAnisotropy);
            material.SetFloat("_GG_HazeDensity", profile.HazeDensity);
            material.SetFloat("_GG_LimbDarkening", profile.LimbDarkening);
            material.SetFloat("_GG_WrapDiffuse", profile.WrapDiffuse);
            material.SetFloat("_GG_RimIntensity", profile.RimIntensity);
            material.SetFloat("_GG_Saturation", profile.Saturation);
            material.SetFloat("_GG_CloudBump", profile.CloudBump);
            material.SetFloat("_GG_Contrast", profile.Contrast);
            material.SetFloat("_GG_PolarTint", profile.PolarTint);
            material.SetFloat("_GG_DetailStrength", profile.DetailStrength);
            material.SetTexture("_GG_SurfaceMap", profile.SurfaceMap);
            material.SetFloat("_GG_SurfaceMapStrength", profile.SurfaceMap != null ? profile.SurfaceMapStrength : 0f);
            material.SetFloat("_GG_MapLongitudeOffset", profile.SurfaceMapLongitudeOffset);
            material.SetFloat("_GG_MapBump", profile.SurfaceMapBump);
            material.SetFloat("_GG_VortexSpinPeriod", profile.VortexSpinPeriodSeconds);
            // The disc branch overwrites this with the energy/brightness scale;
            // the near sphere stays physically calibrated.
            material.SetFloat("_GG_IntensityScale", 1f);

            // The baked Jupiter map already contains the real storms. Drawing
            // the authored vortices on top would add a second Great Red Spot
            // and dark rings, so they are only used by the procedural fallback.
            vortexCount = profile.SurfaceMap != null
                ? 0
                : Mathf.Min(8, profile.Vortices.Count);
            for (int i = 0; i < vortexCount; i++)
            {
                GasGiantVortex vortex = profile.Vortices[i];
                float latC = profile.PlanetocentricFromPlanetographic(vortex.LatitudeDegrees) * Mathf.Deg2Rad;
                float lat0 = profile.PlanetocentricFromPlanetographic(vortex.LatitudeDegrees - vortex.RadiusLatDegrees) * Mathf.Deg2Rad;
                float lat1 = profile.PlanetocentricFromPlanetographic(vortex.LatitudeDegrees + vortex.RadiusLatDegrees) * Mathf.Deg2Rad;
                float radiusLat = Mathf.Max(0.001f, Mathf.Abs(lat1 - lat0) * 0.5f);
                float radiusLon = Mathf.Max(0.001f, vortex.RadiusLonDegrees * Mathf.Deg2Rad);
                float lon = vortex.LongitudeDegrees * Mathf.Deg2Rad;

                vortexParams0[i] = new Vector4(latC, lon, radiusLat, radiusLon);
                vortexParams1[i] = new Vector4(vortex.Spin, vortex.Intensity, vortex.DriftDegreesPerDay, vortex.SwirlStrength);

                Color core = vortex.CoreColor.linear;
                Color collar = vortex.CollarColor.linear;
                vortexCores[i] = new Vector4(core.r, core.g, core.b, vortex.CollarWidth);
                vortexCollars[i] = new Vector4(collar.r, collar.g, collar.b, 0f);
            }

            material.SetVectorArray("_GG_VortexParams0", vortexParams0);
            material.SetVectorArray("_GG_VortexParams1", vortexParams1);
            material.SetVectorArray("_GG_VortexCore", vortexCores);
            material.SetVectorArray("_GG_VortexCollar", vortexCollars);
            material.SetInt("_GG_VortexCount", vortexCount);
        }

        private void BuildVisuals()
        {
            // Old runtime children can survive an Enter-Play-Mode without domain
            // reload. Clean only our own names; no user content is touched.
            Transform oldVisual = transform.Find("AeolusVisual");
            if (oldVisual != null)
            {
                Destroy(oldVisual.gameObject);
            }

            GameObject oldDisc = GameObject.Find("AeolusDisc");
            if (oldDisc != null)
            {
                Destroy(oldDisc);
            }

            float radius = (float)body.Radius;
            float polar = radius * Mathf.Clamp(profile.OblatenessRatio, 0.5f, 1f);

            GameObject visual = new GameObject("AeolusVisual");
            nearVisual = visual.transform;
            nearVisual.SetParent(transform, false);
            nearVisual.localPosition = Vector3.zero;
            nearVisual.localRotation = Quaternion.identity;
            nearVisual.localScale = new Vector3(radius, radius, polar);

            MeshFilter filter = visual.AddComponent<MeshFilter>();
            nearMesh = BuildCubeSphereMesh(MeshResolution);
            filter.sharedMesh = nearMesh;

            nearRenderer = visual.AddComponent<MeshRenderer>();
            nearRenderer.sharedMaterial = surfaceMaterial;
            nearRenderer.shadowCastingMode = ShadowCastingMode.Off;
            nearRenderer.receiveShadows = false;
            nearRenderer.lightProbeUsage = LightProbeUsage.Off;
            nearRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            nearRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            GameObject disc = new GameObject("AeolusDisc");
            farDisc = disc.transform;
            farDisc.SetParent(null, false);
            MeshFilter discFilter = disc.AddComponent<MeshFilter>();
            discMesh = BuildQuadMesh();
            discFilter.sharedMesh = discMesh;
            farRenderer = disc.AddComponent<MeshRenderer>();
            farRenderer.sharedMaterial = discMaterial;
            farRenderer.shadowCastingMode = ShadowCastingMode.Off;
            farRenderer.receiveShadows = false;
            farRenderer.lightProbeUsage = LightProbeUsage.Off;
            farRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            farRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            nearVisual.gameObject.SetActive(false);
            farDisc.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!initialized || body == null || star == null || profile == null)
            {
                return;
            }

            if (Runner == null || Runner.SystemState == null)
            {
                return;
            }

            Camera camera = PlayerCamera != null ? PlayerCamera : Camera.main;
            if (camera == null)
            {
                return;
            }

            if (UnderwaterEffect.CameraIsUnderwater)
            {
                SetVisualsActive(false, false);
                return;
            }

            Runner.SystemState.EvaluateBodyState(body, Runner.TimeSeconds, out Vector3d bodyPos, out _);
            star.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPos, out _);

            Vector3d toBody = bodyPos - Runner.PlayerPosition;
            double distance = toBody.Magnitude;
            if (!(distance > 1d))
            {
                SetVisualsActive(false, false);
                return;
            }

            Vector3 simDirection = AstroFrame.ToSimulation(toBody / distance);
            Vector3 sunDirection = AstroFrame.ToSimulation((starPos - bodyPos).Normalized);

            // A moon (or another planet) can hide the star from the giant: the same
            // disc-overlap geometry as the star renderer and the far discs.
            double eclipse = Runner.SystemState != null
                ? Runner.SystemState.StarVisibilityForBody(body, Runner.TimeSeconds, out _)
                : 1d;

            // The giant's own brightness follows the star's L/d² at ITS position, not
            // a fixed "solar" scale: at 1.64 AU with the 0.369-scale Sun it sees
            // ~0.056 E_sun. SurfaceBrightness/FarDiscBrightness remain the artistic
            // HDR boost; the star factor is physical. The pupil (_EyeGain, written by
            // SkyEnvironment from the observer's own light) is the scene's one
            // non-physical step — without it the giant is a black ball at 1.64 AU while
            // Terra's ground is readable.
            double bodyStarDistanceKm = (starPos - bodyPos).Magnitude / 1000d;
            double bodyStarScale = starRenderer != null && starRenderer.IsReady
                ? starRenderer.State.IrradianceWm2(bodyStarDistanceKm) / StarPhysics.Esun1Au
                    * starRenderer.State.VisibleEtaRatioToSun
                : 1d;
            float eyeGain = Shader.GetGlobalFloat(EyeGainId);
            float starLightScale = (float)bodyStarScale * (eyeGain > 0f ? eyeGain : 1f);

            float far = Mathf.Max(1f, camera.farClipPlane);
            // The switch is about the NEAR SURFACE, not the centre: at 1.1 R
            // the centre is 60 000 km away but the cloud tops are only 5 500 km
            // away and are well inside the far plane.
            double nearSurfaceDistance = Math.Max(0d, distance - body.Radius);
            float angularRadius = Mathf.Atan2((float)body.Radius, (float)distance);
            float fovRadians = Mathf.Max(0.01f, camera.fieldOfView) * Mathf.Deg2Rad;
            float screenHeight = Mathf.Max(1f, Screen.height);
            float pixelRadius = angularRadius / Mathf.Tan(fovRadians * 0.5f) * (screenHeight * 0.5f);

            // Two independent conditions, both about REPRESENTATION:
            //  * the sphere must fit inside the far plane, or it is clipped;
            //  * it must be big enough on screen that a 128x128 mesh is a better
            //    picture than a flat disc. During an interplanetary leg the far
            //    plane is the SUN's horizon (~1e11 m), so the "fits" test alone
            //    kept the mesh active at 0.3 px for the whole flight while the
            //    disc (with its MinScreenRadiusPixels floor) was switched off.
            //    Below the hand-over the disc owns the body; above it the mesh does.
            bool nearVisible = RenderNearSphere
                && nearSurfaceDistance < (far * 0.95d)
                && pixelRadius >= Mathf.Max(0f, MeshHandoverPixels);

            UpdateMaterialTime(camera, bodyPos, starPos, sunDirection);

            if (nearVisible)
            {
                SetVisualsActive(true, false);
                if (surfaceMaterial != null)
                {
                    surfaceMaterial.SetVector("_GG_SunDirWS", ToVec(sunDirection));
                    surfaceMaterial.SetVector("_GG_SunColor", SunColor());
                    surfaceMaterial.SetFloat("_GG_Eclipse", (float)eclipse);
                    surfaceMaterial.SetVector("_GG_CameraPosWS", camera.transform.position);
                    surfaceMaterial.SetVector("_GG_BodyCenterWS", FloatingOrigin.ToRender(bodyPos));
                    surfaceMaterial.SetFloat("_GG_PlanetRadius", (float)body.Radius);
                    surfaceMaterial.SetFloat("_GG_IntensityScale", SurfaceBrightness * starLightScale);
                }

                return;
            }

            SetVisualsActive(false, RenderFarDisc);
            if (!RenderFarDisc || farDisc == null || discMaterial == null)
            {
                return;
            }

            // 0.9*far put the quad's clip-space depth on the far plane (near/far ~ 1e7)
            // and the rasterizer dropped it entirely. 0.25*far keeps the same angular
            // size; the shader's depth test only needs the disc to be behind the body.
            // The 0.55*nearSurface cap keeps the billboard IN FRONT of the body when the
            // mesh is hidden for the size reason (the far plane can be well beyond it).
            float discDistance = Mathf.Max(1f, Mathf.Min(far * 0.25f, (float)(nearSurfaceDistance * 0.55d)));
            Vector3 cameraPosition = camera.transform.position;
            farDisc.position = cameraPosition + (simDirection * discDistance);

            Runner.SystemState.GetVisualFrame(body, Runner.TimeSeconds, out _, out _, out QuaternionD orientation);
            Quaternion bodyRotation = AstroFrame.ToSimulation(orientation);

            Vector3 northWorld = bodyRotation * Vector3.forward;
            Vector3 projectedNorth = Vector3.ProjectOnPlane(northWorld, simDirection);
            if (projectedNorth.sqrMagnitude < 1e-10f)
            {
                projectedNorth = bodyRotation * Vector3.right;
            }

            farDisc.rotation = Quaternion.LookRotation(simDirection, projectedNorth.normalized);

            float size = discDistance * 2f * Mathf.Tan(angularRadius) * 1.02f;

            // A 0.038-degree disc is subpixel on 1080p. A hard subpixel quad
            // flickers as the planet moves; expand to a minimum screen radius
            // and scale the radiance by the area ratio (energy conservation).
            float minPixels = Mathf.Max(0f, MinScreenRadiusPixels);
            float pixelScale = (minPixels > 0f && pixelRadius > 1e-6f)
                ? Mathf.Max(1f, minPixels / pixelRadius)
                : 1f;
            farDisc.localScale = new Vector3(size * pixelScale, size * pixelScale, 1f);

            float energyScale = Mathf.Max(MinEnergyScale, 1f / (pixelScale * pixelScale));
            discMaterial.SetFloat("_GG_IntensityScale", energyScale * FarDiscBrightness * starLightScale);
            discMaterial.SetFloat("_GG_DiscDistance", discDistance);
            discMaterial.SetVector("_GG_SunDirWS", ToVec(sunDirection));
            discMaterial.SetVector("_GG_SunColor", SunColor());
            discMaterial.SetFloat("_GG_Eclipse", (float)eclipse);
            discMaterial.SetVector("_GG_CameraPosWS", cameraPosition);
            discMaterial.SetVector("_GG_BodyCenterWS", FloatingOrigin.ToRender(bodyPos));
            discMaterial.SetFloat("_GG_PlanetRadius", (float)body.Radius);
            discMaterial.SetMatrix("_GG_BodyToWorld", Matrix4x4.TRS(Vector3.zero, bodyRotation, Vector3.one));

            // The disc shader reconstructs the ellipsoid in body space. The
            // basis is orthonormal: v = body->camera, e1 = sky horizontal,
            // e2 = projection of the north pole onto the sky plane.
            Vector3 vWorld = -simDirection;
            Vector3 vBody = Quaternion.Inverse(bodyRotation) * vWorld;
            Vector3 e1Body = Vector3.Cross(Vector3.forward, vBody);
            if (e1Body.sqrMagnitude < 1e-10f)
            {
                e1Body = Vector3.right;
            }

            e1Body.Normalize();
            Vector3 e2Body = Vector3.Cross(vBody, e1Body).normalized;

            // LookingRotation(forward, up) gives local X = cross(up, forward),
            // which is -e1 for an observer outside the globe. The shader's UV x
            // must map to that same screen-right axis; otherwise the far disc is
            // mirrored east-west relative to the near sphere. e2 is unchanged.
            discMaterial.SetVector("_GG_DiscE1", -e1Body);
            discMaterial.SetVector("_GG_DiscE2", e2Body);
            discMaterial.SetVector("_GG_DiscV", vBody.normalized);

            if (LogDiagnostics && !loggedStart)
            {
                loggedStart = true;
                Debug.Log(string.Format(
                    "[GasGiant] far disc: d={0:E3} Рј, angularRadius={1:E3} СЂР°Рґ, pixelRadius={2:F2}, " +
                    "pixelScale={3:F2}, disc={4:E2} Рј",
                    distance, angularRadius, pixelRadius, pixelScale, size * pixelScale));
            }
        }

        private void UpdateMaterialTime(Camera camera, Vector3d bodyPos, Vector3d starPos, Vector3 sunDirection)
        {
            double t = Runner.TimeSeconds;
            double phasePeriod = Mathf.Max(1f, profile.PhasePeriodSeconds);
            double phase = t / phasePeriod;
            phase -= Math.Floor(phase);

            float tA = (float)(phase * phasePeriod);
            float tB = (float)(((phase + 0.5d) % 1d) * phasePeriod);

            double physicalRotation = body.RotationPeriodSeconds > 0d
                ? body.RotationPeriodSeconds
                : 1d;
            double visualScale = physicalRotation / Mathf.Max(1f, profile.VisualSpinPeriodSeconds);

            // Continuous art clock. The old tA/tB values wrapped every
            // PhasePeriodSeconds, so the whole cloud field jumped once per
            // window; now only the vortices use that short loop. The wind
            // layers run on the unbounded clock.
            double windTimeA = t * visualScale;
            double windTimeB = windTimeA * (1d + Math.Max(0d, (double)profile.LayerSpeedDelta));

            // The cloud deck is fixed to the body. BodyView already rotates the
            // mesh at the physical period (Aeolus: 37 800 s = 10.5 h); an extra
            // art-clock shell spin here would make the visible planet turn every
            // VisualSpinPeriodSeconds instead. That clock now only speeds up the
            // differential winds/vortices (see visualScale above), never the
            // planet's apparent rotation.
            float patternSpin = 0f;

            ApplyProfileConstants(surfaceMaterial);
            ApplyProfileConstants(discMaterial);
            SetTimeUniforms(surfaceMaterial, windTimeA, windTimeB, tA, tB, patternSpin);
            SetTimeUniforms(discMaterial, windTimeA, windTimeB, tA, tB, patternSpin);
        }

        private void SetTimeUniforms(Material material, double windTimeA, double windTimeB,
            float vortexTimeA, float vortexTimeB, float patternSpin)
        {
            if (material == null)
            {
                return;
            }

            material.SetFloat("_GG_WindTimeA", (float)windTimeA);
            material.SetFloat("_GG_WindTimeB", (float)windTimeB);
            material.SetFloat("_GG_VortexTimeA", vortexTimeA);
            material.SetFloat("_GG_VortexTimeB", vortexTimeB);
            // Weight of the secondary (faster) cloud deck. Previously this
            // uniform was a phase triangle that made the deck fade in and out.
            material.SetFloat("_GG_LayerBlend", profile.LayerBlend);
            material.SetFloat("_GG_PatternSpin", patternSpin);
        }

        private void SetVisualsActive(bool nearActive, bool farActive)
        {
            if (nearVisual != null && nearVisual.gameObject.activeSelf != nearActive)
            {
                nearVisual.gameObject.SetActive(nearActive);
            }

            if (farDisc != null && farDisc.gameObject.activeSelf != farActive)
            {
                farDisc.gameObject.SetActive(farActive);
            }
        }

        private static Vector4 ToVec(Vector3 value)
        {
            return new Vector4(value.x, value.y, value.z, 0f);
        }

        private static Vector4 SunColor()
        {
            Vector3d photosphere = SkyEnvironment.PhotosphereLinear;
            double luminance = (0.2126d * photosphere.X) + (0.7152d * photosphere.Y) + (0.0722d * photosphere.Z);
            if (!(luminance > 1e-12d))
            {
                return new Vector4(1f, 1f, 1f, 1f);
            }

            return new Vector4(
                (float)(photosphere.X / luminance),
                (float)(photosphere.Y / luminance),
                (float)(photosphere.Z / luminance),
                1f);
        }

        private static Mesh BuildCubeSphereMesh(int resolution)
        {
            int res = Mathf.Clamp(resolution, 8, 192);
            int n = res + 1;
            int vertsPerFace = n * n;
            int vertexCount = 6 * vertsPerFace;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var indices = new int[6 * (n - 1) * (n - 1) * 6];

            int v = 0;
            int t = 0;
            for (int face = 0; face < 6; face++)
            {
                int baseVertex = v;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        Vector3d dir = CubeSphere.Direction(face, (float)x / res, (float)y / res);
                        Vector3 position = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
                        vertices[v] = position;
                        normals[v] = position;
                        v++;
                    }
                }

                for (int y = 0; y < n - 1; y++)
                {
                    for (int x = 0; x < n - 1; x++)
                    {
                        int a = baseVertex + (y * n) + x;
                        int b = a + 1;
                        int c = a + n;
                        int d = c + 1;
                        // Clockwise seen from OUTSIDE, so Unity's front face is
                        // the visible shell. The a,c,b order used by
                        // BuildSphereMesh (clouds/atmosphere domes) is the
                        // opposite: those meshes are viewed from inside, and an
                        // inward winding here made Cull Back discard the near
                        // hemisphere and show the far hemisphere's interior.
                        indices[t++] = a;
                        indices[t++] = b;
                        indices[t++] = c;
                        indices[t++] = b;
                        indices[t++] = d;
                        indices[t++] = c;
                    }
                }
            }

            var mesh = new Mesh
            {
                name = "AeolusCubeSphere",
                indexFormat = IndexFormat.UInt32,
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildQuadMesh()
        {
            var mesh = new Mesh { name = "AeolusDiscQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.normals = new[]
            {
                new Vector3(0f, 0f, -1f),
                new Vector3(0f, 0f, -1f),
                new Vector3(0f, 0f, -1f),
                new Vector3(0f, 0f, -1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}




