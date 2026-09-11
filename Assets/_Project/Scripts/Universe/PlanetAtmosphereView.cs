using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Визуальная атмосфера. Рисуется КАМЕРА-ЦЕНТРИРОВАННЫМ куполом-сферой
    /// (радиус 0.5·far, мировая ориентация), шейдер Galilego/PlanetAtmosphere
    /// марширует луч и аналитически пересекает его со сферами атмосферы и
    /// планеты в мировом кадре. Купол всегда влезает в far, поэтому основную
    /// камеру НЕ надо растягивать под атмосферу (иначе far/near → z-fighting).
    ///
    /// Физические коэффициенты β берутся из AtmosphereOptics (общий источник с
    /// CPU SkyPhysics/SkyEnvironment). Тут же на CPU строятся две LUT, которые
    /// шейдер только сэмплирует:
    ///   * Transmittance LUT — прозрачность к Солнцу от (высота, cos θ);
    ///   * Multi-Scattering LUT — приближение бесконечного многократного
    ///     рассеяния (синий зенит без пересвета горизонта).
    /// LUT зависят только от профиля/радиуса, поэтому строятся один раз при
    /// старте (и пересчитываются, если параметры изменились).
    ///
    /// Все параметры — из AtmosphereProfile, тумблер VisualEnabled, плюс
    /// DebugMode для визуализации полей шейдера.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-35)]
    public sealed class PlanetAtmosphereView : MonoBehaviour
    {
        public enum AtmosphereDebugMode
        {
            Final = 0,
            ViewTransmittance = 1,
            RayEntry = 2,
            RayExit = 3,
            SceneDepth = 4,
            Extinction = 5,
            InScatterOnly = 6,
            SunTransmittance = 7,
            RTHandleScale = 8,
            ColorUv = 9,
        }

        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Сегментов по широте купола.")]
        [Range(4, 128)]
        public int LatitudeSegments = 24;

        [Tooltip("Сегментов по долготе купола.")]
        [Range(8, 256)]
        public int LongitudeSegments = 48;

        [Tooltip("Отладочный вывод вместо финального кадра (0 = выключено).")]
        public AtmosphereDebugMode DebugMode = AtmosphereDebugMode.Final;

        private OrbitingBody body;
        private AtmosphereProfile profile;
        private Transform shell;
        private Material materialCache;
        private Texture2D transmittanceLut;
        private Texture2D multiScatterLut;
        private bool loggedOnce;

        // Подпись параметров, от которых зависят LUT. Пока совпадает — LUT не
        // пересобираем. Иначе смена AerosolScale/плотности/озона в рантайме не
        // влияла бы на небо (LUT запекались один раз в Start).
        private bool lutBuilt;
        private double lutDensity;
        private double lutScaleHeight;
        private double lutAerosolScale;
        private bool lutOzone;
        private double lutTopAltitude;
        private double lutRadius;

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

            profile = body?.Atmosphere;
            if (body == null || profile == null)
            {
                enabled = false;
                return;
            }

            Shader shader = Shader.Find("Galilego/PlanetAtmosphere");
            materialCache = new Material(shader);

            BuildLuts();

            // Купол — top-level (НЕ под Terra: у неё скейл 2286000).
            GameObject go = new GameObject("AtmosphereDome");
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

            if (transmittanceLut != null)
            {
                Destroy(transmittanceLut);
            }

            if (multiScatterLut != null)
            {
                Destroy(multiScatterLut);
            }
        }

        private bool LutNeedsRebuild()
        {
            return !lutBuilt
                || lutDensity != profile.SeaLevelDensityKgPerCubicMeter
                || lutScaleHeight != profile.ScaleHeightMeters
                || lutAerosolScale != profile.AerosolScale
                || lutOzone != profile.OzoneEnabled
                || lutTopAltitude != profile.TopAltitudeMeters
                || lutRadius != body.Radius;
        }

        private void BuildLuts()
        {
            lutBuilt = false;

            if (transmittanceLut != null)
            {
                Destroy(transmittanceLut);
                transmittanceLut = null;
            }

            if (multiScatterLut != null)
            {
                Destroy(multiScatterLut);
                multiScatterLut = null;
            }

            if (profile.ScaleHeightMeters <= 0d || profile.SeaLevelDensityKgPerCubicMeter <= 0d
                || profile.TopAltitudeMeters <= 0d)
            {
                return;
            }

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            AtmosphereOptics.Coefficients coeff = profile.ToOptics();
            double planetRadius = body.Radius;
            double atmosphereRadius = body.Radius + profile.TopAltitudeMeters;

            AtmosphereOptics.Lut transmittance = AtmosphereOptics.BuildTransmittanceLut(
                coeff, planetRadius, atmosphereRadius, 48);
            AtmosphereOptics.Lut multiScatter = AtmosphereOptics.BuildMultiScatteringLut(
                coeff, planetRadius, atmosphereRadius, 32, 32);

            transmittanceLut = BuildTexture(transmittance);
            multiScatterLut = BuildTexture(multiScatter);
            watch.Stop();

            lutDensity = profile.SeaLevelDensityKgPerCubicMeter;
            lutScaleHeight = profile.ScaleHeightMeters;
            lutAerosolScale = profile.AerosolScale;
            lutOzone = profile.OzoneEnabled;
            lutTopAltitude = profile.TopAltitudeMeters;
            lutRadius = body.Radius;
            lutBuilt = true;

            Debug.Log(string.Format(
                "[PlanetAtmosphere] LUT построены за {0} мс (R={1:E2}, R_atm={2:E2}, Hr={3}, Hm={4}, aerosol={5})",
                watch.ElapsedMilliseconds, planetRadius, atmosphereRadius,
                coeff.RayleighScaleHeight, coeff.MieScaleHeight, profile.AerosolScale));
        }

        private static Texture2D BuildTexture(AtmosphereOptics.Lut lut)
        {
            Texture2D texture = new Texture2D(lut.Width, lut.Height, TextureFormat.RGBAHalf, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            Color[] pixels = new Color[lut.Width * lut.Height];
            for (int i = 0; i < pixels.Length; i++)
            {
                Vector3d v = lut.Pixels[i];
                pixels[i] = new Color((float)v.X, (float)v.Y, (float)v.Z, 1f);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void LateUpdate()
        {
            if (body == null || profile == null || shell == null)
            {
                return;
            }

            bool active = profile.VisualEnabled
                && profile.TopAltitudeMeters > 0d
                && Runner.DominantBody == body;
            if (shell.gameObject.activeSelf != active)
            {
                shell.gameObject.SetActive(active);
            }

            if (!active)
            {
                return;
            }

            // Профиль мог смениться в рантайме (AerosolScale и т.п.) — тогда
            // пересобираем LUT, иначе небо не отреагирует.
            if (LutNeedsRebuild())
            {
                BuildLuts();
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            double atmosphereRadius = body.Radius + profile.TopAltitudeMeters;
            double atmosphereDepth = profile.TopAltitudeMeters;
            AtmosphereOptics.Coefficients coeff = profile.ToOptics();

            Vector3 cameraPosition = camera.transform.position;
            // Меш купола — единичный радиус (BuildSphereMesh(1f, …)), поэтому
            // localScale == мировой радиус. Радиус 0.5·far держит поверхность
            // купола ВНУТРИ far: иначе вершины у края фрустума зарезаются
            // far-плоскостью и в небе появляются чёрные дыры.
            float domeRadius = Mathf.Max(1f, camera.farClipPlane * 0.5f);
            shell.position = cameraPosition;
            shell.rotation = Quaternion.identity;
            shell.localScale = new Vector3(domeRadius, domeRadius, domeRadius);

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);

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

            // Цвет фотосферы (~5772 K) для вклада рассеяния: единый источник
            // с SunBillboard/SkyEnvironment. Формула Планка → линейный sRGB.
            Vector3d starLinear = StarColorUtil.FromTemperature(5772d);
            Shader.SetGlobalVector("_AtmSunColor",
                new Vector3((float)starLinear.X, (float)starLinear.Y, (float)starLinear.Z));

            // Тинты — только множители поверх физики (1 = без изменений).
            Color rayleighTint = profile.RayleighColor;
            Color mieTint = profile.MieColor;
            Vector3d betaRayleigh = AtmosphereOptics.VecMul(
                coeff.RayleighScattering,
                new Vector3d(rayleighTint.r, rayleighTint.g, rayleighTint.b));
            Vector3d betaMie = AtmosphereOptics.VecMul(
                coeff.MieScattering,
                new Vector3d(mieTint.r, mieTint.g, mieTint.b));

            Shader.SetGlobalVector("_AtmCameraWS", cameraPosition);
            Shader.SetGlobalVector("_AtmCameraForwardWS", camera.transform.forward);
            Shader.SetGlobalVector("_AtmBodyCenterWS", FloatingOrigin.ToRender(bodyPos));
            Shader.SetGlobalVector("_AtmSunDirWS", sunDirWorld);
            Shader.SetGlobalFloat("_AtmPlanetRadius", (float)body.Radius);
            Shader.SetGlobalFloat("_AtmRadius", (float)atmosphereRadius);
            Shader.SetGlobalFloat("_AtmAtmDepth", (float)atmosphereDepth);
            Shader.SetGlobalVector("_AtmBetaRayleigh",
                new Vector3((float)betaRayleigh.X, (float)betaRayleigh.Y, (float)betaRayleigh.Z));
            Shader.SetGlobalVector("_AtmBetaMie",
                new Vector3((float)betaMie.X, (float)betaMie.Y, (float)betaMie.Z));
            Shader.SetGlobalVector("_AtmBetaOzone",
                new Vector3((float)coeff.OzoneAbsorption.X, (float)coeff.OzoneAbsorption.Y, (float)coeff.OzoneAbsorption.Z));
            Shader.SetGlobalFloat("_AtmHr", (float)coeff.RayleighScaleHeight);
            Shader.SetGlobalFloat("_AtmHm", (float)coeff.MieScaleHeight);
            Shader.SetGlobalFloat("_AtmOzoneCenter", (float)coeff.OzoneCenterAltitude);
            Shader.SetGlobalFloat("_AtmOzoneWidth", (float)coeff.OzoneHalfWidth);
            Shader.SetGlobalFloat("_AtmMieG", (float)coeff.MieAnisotropy);
            Shader.SetGlobalFloat("_AtmIntensity", profile.Intensity);
            Shader.SetGlobalFloat("_AtmStepCount", Mathf.Clamp(profile.StepCount, 2, 96));
            Shader.SetGlobalFloat("_AtmPlanetOcclusion", profile.PlanetOcclusion ? 1f : 0f);
            Shader.SetGlobalColor("_AtmGroundColor", profile.GroundColor);
            Shader.SetGlobalFloat("_AtmHorizonFade", Mathf.Max(0.001f, profile.HorizonFade));
            Shader.SetGlobalFloat("_AtmDebugMode", (float)(int)DebugMode);

            if (transmittanceLut != null)
            {
                Shader.SetGlobalTexture("_AtmTransmittanceTex", transmittanceLut);
            }

            if (multiScatterLut != null)
            {
                Shader.SetGlobalTexture("_AtmMultiScatterTex", multiScatterLut);
            }

            if (!loggedOnce)
            {
                loggedOnce = true;
                Debug.Log(string.Format(
                    "[PlanetAtmosphere] active=True, domeR={0:E2}, R={1}, R_atm={2}, Hr={3}, Hm={4}, Intensity={5}, dominant={6}",
                    domeRadius, body.Radius, atmosphereRadius,
                    coeff.RayleighScaleHeight, coeff.MieScaleHeight, profile.Intensity,
                    Runner.DominantBody != null ? Runner.DominantBody.Name : "null"));
            }
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
