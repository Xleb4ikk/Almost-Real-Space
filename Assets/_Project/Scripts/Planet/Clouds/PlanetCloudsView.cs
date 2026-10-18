using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Visual clouds. Like PlanetAtmosphereView — a camera-centred
    /// dome sphere (world orientation, radius 0.5·far); the
    /// Galilego/PlanetClouds shader raymarches the ray and analytically intersects it with
    /// TWO spheres of the cloud layer (BottomAltitude/TopAltitude) in the world
    /// frame.
    ///
    /// Drawn AFTER the atmosphere (Queue "Transparent+50" in the shader) with a plain
    /// premultiplied alpha blend (Blend One OneMinusSrcAlpha) over the already
    /// composited frame — unlike the atmosphere, the cloud dome does NOT
    /// resample _ColorPyramidTexture and does not use Blend One Zero: if
    /// two such passes ran in succession, the second would read the PRE-transparent
    /// pyramid snapshot (without the first's contribution) and erase the atmosphere beneath it.
    /// A plain alpha blend over the framebuffer has no such problem and does not
    /// need to know what is drawn underneath.
    ///
    /// Noise domains (shape/detail/weather) are sampled in WORLD Cartesian
    /// coordinates relative to the planet centre — NOT in lat-long/UV. This is a
    /// deliberate choice: any latitude-longitude unwrap at radius
    /// ~1000+ km gives anisotropy at the poles and stripes stretched along the
    /// parallels. 3D tiled textures sampled directly by XYZ avoid this
    /// entirely: no seams and no poles means no such distortion anywhere
    /// on the sphere.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class PlanetCloudsView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Сегментов по широте купола.")]
        [Range(4, 128)]
        public int LatitudeSegments = 24;

        [Tooltip("Сегментов по долготе купола.")]
        [Range(8, 256)]
        public int LongitudeSegments = 48;

        private OrbitingBody body;
        private CloudProfile profile;
        private Transform shell;
        private Material materialCache;
        private SkyEnvironment skyEnvironment;

        private Texture3D shapeTex;
        private Texture3D detailTex;
        private Cubemap weatherTex;
        private Texture3D earthMaskTex;
        private bool texturesBuilt;
        private bool loggedOnce;

        private int bakedShapeRes;
        private int bakedDetailRes;
        private int bakedWeatherRes;
        private int bakedSeed;
        private float bakedWeatherCellMeters;
        private float bakedClearZoneCellMeters;
        private float bakedClearZoneFraction;
        private float bakedClearZoneSoftness;

        private void Start()
        {
            if (Runner == null || Runner.SystemState == null)
            {
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

            profile = body?.Clouds;
            if (body == null || profile == null)
            {
                enabled = false;
                return;
            }

            skyEnvironment = FindAnyObjectByType<SkyEnvironment>();

            Shader shader = Shader.Find("Galilego/PlanetClouds");
            if (shader == null)
            {
                Debug.LogError("[PlanetClouds] Шейдер Galilego/PlanetClouds не найден.");
                enabled = false;
                return;
            }

            materialCache = new Material(shader);

            BuildTextures();

            // Dome is top-level, like AtmosphereDome (see PlanetAtmosphereView).
            GameObject go = new GameObject("CloudsDome");
            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = materialCache;
            filter.sharedMesh = BuildSphereMesh(1f, Mathf.Max(6, LatitudeSegments), Mathf.Max(12, LongitudeSegments));
            shell = go.transform;
            shell.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (shell != null)
            {
                Destroy(shell.gameObject);
            }

            if (materialCache != null)
            {
                Destroy(materialCache);
            }

            if (shapeTex != null)
            {
                Destroy(shapeTex);
            }

            if (detailTex != null)
            {
                Destroy(detailTex);
            }

            if (weatherTex != null)
            {
                Destroy(weatherTex);
            }

            if (earthMaskTex != null)
            {
                Destroy(earthMaskTex);
            }
        }

        private bool TexturesNeedRebuild()
        {
            return !texturesBuilt
                || bakedShapeRes != profile.ShapeTextureResolution
                || bakedDetailRes != profile.DetailTextureResolution
                || bakedWeatherRes != profile.WeatherTextureResolution
                || bakedWeatherCellMeters != profile.WeatherCellMeters
                || bakedClearZoneCellMeters != profile.ClearZoneCellMeters
                || bakedClearZoneFraction != profile.ClearZoneFraction
                || bakedClearZoneSoftness != profile.ClearZoneSoftness
                || bakedSeed != profile.NoiseSeed;
        }

        private void BuildTextures()
        {
            texturesBuilt = false;

            if (shapeTex != null) { Destroy(shapeTex); shapeTex = null; }
            if (detailTex != null) { Destroy(detailTex); detailTex = null; }
            if (weatherTex != null) { Destroy(weatherTex); weatherTex = null; }
            if (earthMaskTex != null) { Destroy(earthMaskTex); earthMaskTex = null; }

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            shapeTex = CloudNoiseBaker.BuildShapeTexture(profile.ShapeTextureResolution, profile.NoiseSeed);
            detailTex = CloudNoiseBaker.BuildDetailTexture(profile.DetailTextureResolution, profile.NoiseSeed);
            weatherTex = CloudNoiseBaker.BuildWeatherCubemap(
                profile.WeatherTextureResolution,
                profile.WeatherCellMeters,
                profile.ClearZoneCellMeters,
                profile.ClearZoneFraction,
                profile.ClearZoneSoftness,
                body.Radius,
                profile.NoiseSeed);

            earthMaskTex = EarthMaskBaker.BuildEarthMaskTexture(128);

            watch.Stop();

            bakedShapeRes = profile.ShapeTextureResolution;
            bakedDetailRes = profile.DetailTextureResolution;
            bakedWeatherRes = profile.WeatherTextureResolution;
            bakedWeatherCellMeters = profile.WeatherCellMeters;
            bakedClearZoneCellMeters = profile.ClearZoneCellMeters;
            bakedClearZoneFraction = profile.ClearZoneFraction;
            bakedClearZoneSoftness = profile.ClearZoneSoftness;
            bakedSeed = profile.NoiseSeed;
            texturesBuilt = true;

            Debug.Log(string.Format(
                "[PlanetClouds] Шум напечен за {0} мс (shape={1}³, detail={2}³, weather={3}², seed={4})",
                watch.ElapsedMilliseconds, bakedShapeRes, bakedDetailRes, bakedWeatherRes, bakedSeed));
        }

        private void LateUpdate()
        {
            Shader.SetGlobalFloat("_CldCloudActive", 0f);
            Shader.SetGlobalFloat("_CldShadowStrength", 0f);

            if (Runner == null || Runner.SystemState == null || Runner.SystemState.Root == null
                || body == null || profile == null || shell == null)
            {
                return;
            }

            bool active = profile.VisualEnabled
                && profile.TopAltitudeMeters > profile.BottomAltitudeMeters
                && (Runner.DominantBody == body || IsCloudBodyRelevant(Camera.main))
                && !UnderwaterEffect.CameraIsUnderwater;
            if (shell.gameObject.activeSelf != active)
            {
                shell.gameObject.SetActive(active);
            }

            if (!active)
            {
                return;
            }

            Shader.SetGlobalFloat("_CldCloudActive", 1f);
            Shader.SetGlobalFloat("_CldShadowStrength", Mathf.Clamp01(profile.CloudShadowStrength));

            if (TexturesNeedRebuild())
            {
                BuildTextures();
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            // Same approach as AtmosphereDome: keep the dome inside far,
            // otherwise vertices at the frustum edge are clipped by the far plane.
            float domeRadius = Mathf.Max(1f, camera.farClipPlane * 0.5f);
            Vector3 cameraPosition = camera.transform.position;
            shell.position = cameraPosition;
            shell.rotation = Quaternion.identity;
            shell.localScale = new Vector3(domeRadius, domeRadius, domeRadius);

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3 bodyRenderPosition = FloatingOrigin.ToRender(bodyPos);
            float bodyDistance = Vector3.Distance(cameraPosition, bodyRenderPosition);
            if (bodyDistance > (float)body.Radius * 1.2f)
            {
                float requiredDomeRadius = bodyDistance + (float)body.Radius + (float)profile.TopAltitudeMeters + 1000f;
                float requiredFar = requiredDomeRadius * 2f;
                if (camera.farClipPlane < requiredFar)
                {
                    camera.farClipPlane = requiredFar;
                    domeRadius = requiredDomeRadius;
                    shell.localScale = Vector3.one * domeRadius;
                }
            }

            Vector3 sunDirWorld = Vector3.up;
            if (Runner.SystemState.Root != null && Runner.Ship != null)
            {
                Runner.SystemState.Root.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPos, out _);
                Vector3d toStar = starPos - Runner.PlayerPosition;
                double mag = toStar.Magnitude;
                if (mag > 0d)
                {
                    sunDirWorld = AstroFrame.ToSimulation(toStar / mag);
                }
            }

             Vector3d windVelocity = new Vector3d(
                profile.WindVelocityMps.x,
                profile.WindVelocityMps.y,
                profile.WindVelocityMps.z);
            Vector3d mediumWindOffset = WrapVector(windVelocity * Runner.TimeSeconds, profile.MediumCloudCellMeters);
            Vector3d smallWindOffset = WrapVector(windVelocity * Runner.TimeSeconds, profile.SmallCloudCellMeters);
            Vector3d largeWindOffset = WrapVector(windVelocity * Runner.TimeSeconds, profile.LargeCloudCellMeters);
            Vector3d detailWindOffset = WrapVector(windVelocity * Runner.TimeSeconds, profile.DetailCellMeters);

            double planetRadius = body.Radius;
            double bottomRadius = planetRadius + profile.BottomAltitudeMeters;
            double topRadius = planetRadius + profile.TopAltitudeMeters;

            Vector3d starLinear = SkyEnvironment.PhotosphereLinear;
            Vector3 cloudSunColor = new Vector3((float)starLinear.X, (float)starLinear.Y, (float)starLinear.Z);
            if (skyEnvironment != null)
            {
                Vector3 transmittance = skyEnvironment.Transmittance;
                float directVisibility = Mathf.Clamp01(skyEnvironment.DayFactor * skyEnvironment.SunOcclusion);
                cloudSunColor = new Vector3(
                    cloudSunColor.x * transmittance.x * directVisibility,
                    cloudSunColor.y * transmittance.y * directVisibility,
                    cloudSunColor.z * transmittance.z * directVisibility);
            }
            Vector4 skyAmbientVector = Shader.GetGlobalVector("_SkyAmbientColor");
            Vector3 cloudSkyAmbient = new Vector3(skyAmbientVector.x, skyAmbientVector.y, skyAmbientVector.z);

            float shapeFreq = 1f / Mathf.Max(1f, profile.ShapeCellMeters);
            float smallFreq = 1f / Mathf.Max(1f, profile.SmallCloudCellMeters);
            float mediumFreq = 1f / Mathf.Max(1f, profile.MediumCloudCellMeters);
            float largeFreq = 1f / Mathf.Max(1f, profile.LargeCloudCellMeters);
            float detailFreq = 1f / Mathf.Max(1f, profile.DetailCellMeters);
            float shapeTexelWorldSize = 1f / Mathf.Max(1f, shapeFreq * Mathf.Max(1, bakedShapeRes));
            float weatherTexelWorldSize = (float)(2.0 * System.Math.PI * planetRadius) /
                Mathf.Max(1f, bakedWeatherRes);

            Quaternion bodyRotation = FloatingOrigin.RenderRotation(body.GetVisualOrientation(Runner.TimeSeconds));
            Matrix4x4 worldToBody = Matrix4x4.Rotate(Quaternion.Inverse(bodyRotation));

            Shader.SetGlobalVector("_CldCameraWS", cameraPosition);
            Shader.SetGlobalVector("_CldCameraForwardWS", camera.transform.forward);
             Shader.SetGlobalVector("_CldBodyCenterWS", bodyRenderPosition);
            Shader.SetGlobalVector("_CldSunDirWS", sunDirWorld);
            Shader.SetGlobalVector("_CldSunColor", cloudSunColor);
            Shader.SetGlobalVector("_CldSkyAmbient", cloudSkyAmbient);
            Shader.SetGlobalFloat("_CldPlanetRadius", (float)planetRadius);
            Shader.SetGlobalFloat("_CldBottomRadius", (float)bottomRadius);
            Shader.SetGlobalFloat("_CldTopRadius", (float)topRadius);

            Shader.SetGlobalMatrix("_CldWorldToBody", worldToBody);
            Shader.SetGlobalVector("_CldWindOffset", new Vector3((float)mediumWindOffset.X, (float)mediumWindOffset.Y, (float)mediumWindOffset.Z));
            Shader.SetGlobalVector("_CldSmallWindOffset", new Vector3((float)smallWindOffset.X, (float)smallWindOffset.Y, (float)smallWindOffset.Z));
            Shader.SetGlobalVector("_CldLargeWindOffset", new Vector3((float)largeWindOffset.X, (float)largeWindOffset.Y, (float)largeWindOffset.Z));
            Shader.SetGlobalVector("_CldDetailWindOffset", new Vector3((float)detailWindOffset.X, (float)detailWindOffset.Y, (float)detailWindOffset.Z));

            Shader.SetGlobalFloat("_CldSmallShapeFreq", smallFreq);
            Shader.SetGlobalFloat("_CldMediumShapeFreq", mediumFreq);
            Shader.SetGlobalFloat("_CldLargeShapeFreq", largeFreq);
            Shader.SetGlobalFloat("_CldDetailFreq", detailFreq);
            Shader.SetGlobalFloat("_CldShapeTexelWorldSize", shapeTexelWorldSize);
            Shader.SetGlobalFloat("_CldWeatherTexelWorldSize", weatherTexelWorldSize);
             Shader.SetGlobalFloat("_CldSizeVariation", profile.SizeVariation);
             Shader.SetGlobalFloat("_CldNoiseStyle", (float)profile.NoiseStyle);
             materialCache.SetFloat("_CldNoiseStyle", (float)profile.NoiseStyle);
             Shader.SetGlobalFloat("_CldShapeWarp", Mathf.Max(0f, profile.ShapeWarpMeters));

            Shader.SetGlobalFloat("_CldCoverage", profile.Coverage);
            Shader.SetGlobalFloat("_CldDetailErosion", profile.DetailErosion);
            Shader.SetGlobalFloat("_CldBottomFeather", profile.BottomFeather);
            Shader.SetGlobalFloat("_CldTopFeather", profile.TopFeather);

            Shader.SetGlobalFloat("_CldExtinction", Mathf.Max(0f, profile.Extinction));
            Shader.SetGlobalFloat("_CldScatterAlbedo", Mathf.Clamp01(profile.ScatterAlbedo));
            Shader.SetGlobalFloat("_CldPhaseG", Mathf.Clamp(profile.PhaseAnisotropy, 0f, 0.95f));
             Shader.SetGlobalFloat("_CldPowderStrength", Mathf.Max(0f, profile.PowderStrength));
             Shader.SetGlobalFloat("_CldMultipleScattering", Mathf.Clamp01(profile.MultipleScattering));
             Shader.SetGlobalFloat("_CldIntensity", Mathf.Max(0f, profile.Intensity));
            Shader.SetGlobalColor("_CldAmbientTint", profile.AmbientTint);

            Shader.SetGlobalFloat("_CldStepCount", Mathf.Clamp(profile.StepCount, 8, 128));
            Shader.SetGlobalFloat("_CldLightSteps", Mathf.Clamp(profile.LightSteps, 2, 8));
            Shader.SetGlobalFloat("_CldEarlyExitT", Mathf.Clamp(profile.EarlyExitTransmittance, 0.001f, 0.1f));
            Shader.SetGlobalFloat("_CldDebugMode", (float)(int)profile.DebugMode);
            materialCache.SetFloat("_CldDebugMode", (float)(int)profile.DebugMode);

            if (shapeTex != null) Shader.SetGlobalTexture("_CldShapeTex", shapeTex);
            if (detailTex != null) Shader.SetGlobalTexture("_CldDetailTex", detailTex);
            if (weatherTex != null) Shader.SetGlobalTexture("_CldWeatherTex", weatherTex);
            if (earthMaskTex != null)
            {
                Shader.SetGlobalTexture("_CldEarthMaskTex", earthMaskTex);
                Shader.SetGlobalFloat("_CldEarthMaskReady", 1f);
            }

            if (!loggedOnce)
            {
                loggedOnce = true;
                Debug.Log(string.Format(
                    "[PlanetClouds] active=True, domeR={0:E2}, planetR={1}, bottomR={2}, topR={3}, shapeCell={4}m, dominant={5}",
                    domeRadius, planetRadius, bottomRadius, topRadius, profile.ShapeCellMeters,
                    Runner.DominantBody != null ? Runner.DominantBody.Name : "null"));
            }
        }

        private bool IsCloudBodyRelevant(Camera camera)
        {
            if (camera == null)
            {
                return false;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPosition, out _);
            Vector3 renderPosition = FloatingOrigin.ToRender(bodyPosition);
            double distance = (camera.transform.position - renderPosition).magnitude;
            double limit = System.Math.Max(
                body.Radius * 1000d,
                body.SphereOfInfluenceRadius * 4d);
            return distance <= limit;
        }

        private static Vector3d WrapVector(Vector3d value, double period)
        {
            double safePeriod = System.Math.Max(1.0, period);
            return new Vector3d(
                WrapDouble(value.X, safePeriod),
                WrapDouble(value.Y, safePeriod),
                WrapDouble(value.Z, safePeriod));
        }

        private static double WrapDouble(double v, double period)
        {
            double m = v % period;
            return m < 0d ? m + period : m;
        }

        private static Mesh BuildSphereMesh(float radius, int latSegments, int lonSegments)
        {
            int latVerts = latSegments + 1;
            int lonVerts = lonSegments + 1;
            var vertices = new Vector3[latVerts * lonVerts];
            var normals = new Vector3[latVerts * lonVerts];
            var uv = new Vector2[latVerts * lonVerts];
            for (int lat = 0; lat <= latSegments; lat++)
            {
                double theta = System.Math.PI * lat / latSegments;
                double sinTheta = System.Math.Sin(theta);
                double cosTheta = System.Math.Cos(theta);
                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    double phi = 2d * System.Math.PI * lon / lonSegments;
                    var dir = new Vector3(
                        (float)(sinTheta * System.Math.Cos(phi)),
                        (float)cosTheta,
                        (float)(sinTheta * System.Math.Sin(phi)));
                    int index = (lat * lonVerts) + lon;
                    vertices[index] = dir * radius;
                    normals[index] = dir;
                    uv[index] = new Vector2((float)lon / lonSegments, (float)lat / latSegments);
                }
            }

            var triangles = new int[latSegments * lonSegments * 6];
            int t = 0;
            for (int lat = 0; lat < latSegments; lat++)
            {
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    int a = (lat * lonVerts) + lon;
                    int b = a + 1;
                    int c = a + lonVerts;
                    int d = c + 1;
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
