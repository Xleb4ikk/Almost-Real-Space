using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Визуальные облака. Как PlanetAtmosphereView — камера-центрированный
    /// купол-сфера (мировая ориентация, радиус 0.5·far), шейдер
    /// Galilego/PlanetClouds марширует луч и аналитически пересекает его с
    /// ДВУМЯ сферами облачного слоя (BottomAltitude/TopAltitude) в мировом
    /// кадре.
    ///
    /// Рисуется ПОСЛЕ атмосферы (Queue "Transparent+50" в шейдере) обычным
    /// premultiplied alpha-блендом (Blend One OneMinusSrcAlpha) поверх уже
    /// скомпонованного кадра — в отличие от атмосферы, купол облаков НЕ
    /// пересэмплирует _ColorPyramidTexture и не делает Blend One Zero: если
    /// бы два таких прохода шли подряд, второй читал бы ДОСЕЙ-transparent
    /// снимок пирамиды (без вклада первого) и стирал бы атмосферу под собой.
    /// Обычный альфа-бленд поверх фреймбуфера этой проблемы не имеет и не
    /// требует знать, что нарисовано снизу.
    ///
    /// Домены шума (shape/detail/weather) сэмплируются в МИРОВЫХ декартовых
    /// координатах относительно центра планеты — НЕ в lat-long/UV. Это
    /// осознанный выбор: любая широтно-долготная развёртка на радиусе
    /// ~1000+ км даёт анизотропию у полюсов и вытянутые вдоль параллелей
    /// полосы (похоже на артефакт "гигантские линии облаков" из более
    /// ранних раундов). 3D-тайловые текстуры, сэмплируемые напрямую по XYZ,
    /// этой проблемы не имеют в принципе — швов и полюсов у них нет.
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

        private Texture3D shapeTex;
        private Texture3D detailTex;
        private Texture3D weatherTex;
        private bool texturesBuilt;
        private bool loggedOnce;

        // Подпись параметров, от которых зависят текстуры шума. Пока совпадает —
        // не пересобираем (печать не бесплатна, ~сотни мс на профиль).
        private int bakedShapeRes;
        private int bakedDetailRes;
        private int bakedWeatherRes;
        private int bakedSeed;

        // Накопленный сдвиг domain-координат от ветра. Копится в double и
        // заворачивается по модулю — иначе за долгую игровую сессию float
        // потеряет точность и облака начнут "прыгать"/дрожать при сэмплинге.
        private Vector3d windOffset = Vector3d.Zero;
        private const double WindWrapMeters = 2_000_000d; // период шума всё равно периодичен — обёртка тут не даёт видимого шва

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

            Shader shader = Shader.Find("Galilego/PlanetClouds");
            if (shader == null)
            {
                Debug.LogError("[PlanetClouds] Шейдер Galilego/PlanetClouds не найден.");
                enabled = false;
                return;
            }

            materialCache = new Material(shader);

            BuildTextures();

            // Купол — top-level, как AtmosphereDome (см. PlanetAtmosphereView).
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
        }

        private bool TexturesNeedRebuild()
        {
            return !texturesBuilt
                || bakedShapeRes != profile.ShapeTextureResolution
                || bakedDetailRes != profile.DetailTextureResolution
                || bakedWeatherRes != profile.WeatherTextureResolution
                || bakedSeed != profile.NoiseSeed;
        }

        private void BuildTextures()
        {
            texturesBuilt = false;

            if (shapeTex != null) { Destroy(shapeTex); shapeTex = null; }
            if (detailTex != null) { Destroy(detailTex); detailTex = null; }
            if (weatherTex != null) { Destroy(weatherTex); weatherTex = null; }

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            shapeTex = CloudNoiseBaker.BuildShapeTexture(profile.ShapeTextureResolution, profile.NoiseSeed);
            detailTex = CloudNoiseBaker.BuildDetailTexture(profile.DetailTextureResolution, profile.NoiseSeed);
            weatherTex = CloudNoiseBaker.BuildWeatherTexture(profile.WeatherTextureResolution, profile.NoiseSeed);

            watch.Stop();

            bakedShapeRes = profile.ShapeTextureResolution;
            bakedDetailRes = profile.DetailTextureResolution;
            bakedWeatherRes = profile.WeatherTextureResolution;
            bakedSeed = profile.NoiseSeed;
            texturesBuilt = true;

            Debug.Log(string.Format(
                "[PlanetClouds] Шум напечен за {0} мс (shape={1}³, detail={2}³, weather={3}³, seed={4})",
                watch.ElapsedMilliseconds, bakedShapeRes, bakedDetailRes, bakedWeatherRes, bakedSeed));
        }

        private void LateUpdate()
        {
            if (body == null || profile == null || shell == null)
            {
                return;
            }

            bool active = profile.VisualEnabled
                && profile.TopAltitudeMeters > profile.BottomAltitudeMeters
                && Runner.DominantBody == body;
            if (shell.gameObject.activeSelf != active)
            {
                shell.gameObject.SetActive(active);
            }

            if (!active)
            {
                return;
            }

            if (TexturesNeedRebuild())
            {
                BuildTextures();
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            // Тот же приём, что у AtmosphereDome: держим купол внутри far,
            // иначе вершины у края фрустума зарезаются far-плоскостью.
            float domeRadius = Mathf.Max(1f, camera.farClipPlane * 0.5f);
            Vector3 cameraPosition = camera.transform.position;
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

            // Ветер: сдвиг доменных координат шума во времени, в метрах.
            // Копится в double (см. комментарий у windOffset), в шейдер уходит
            // как float — на масштабе одного кадра ветра точности с запасом.
            double dt = Time.deltaTime;
            windOffset += new Vector3d(profile.WindVelocityMps.x, profile.WindVelocityMps.y, profile.WindVelocityMps.z) * dt;
            windOffset = new Vector3d(
                WrapDouble(windOffset.X, WindWrapMeters),
                WrapDouble(windOffset.Y, WindWrapMeters),
                WrapDouble(windOffset.Z, WindWrapMeters));

            double planetRadius = body.Radius;
            double bottomRadius = planetRadius + profile.BottomAltitudeMeters;
            double topRadius = planetRadius + profile.TopAltitudeMeters;

            Vector3d starLinear = SkyEnvironment.PhotosphereLinear;

            float shapeFreq = 1f / Mathf.Max(1f, profile.ShapeCellMeters);
            float detailFreq = 1f / Mathf.Max(1f, profile.DetailCellMeters);
            float weatherFreq = 1f / Mathf.Max(1f, profile.WeatherCellMeters);
            // Мировой размер одного тексела mip0 shape-текстуры — точка отсчёта
            // для footprint-based LOD в шейдере (round 3: LOD по шагу марша, не
            // по расстоянию до камеры — иначе мимо цели на больших дистанциях).
            float shapeTexelWorldSize = 1f / Mathf.Max(1f, shapeFreq * Mathf.Max(1, bakedShapeRes));

            Shader.SetGlobalVector("_CldCameraWS", cameraPosition);
            Shader.SetGlobalVector("_CldCameraForwardWS", camera.transform.forward);
            Shader.SetGlobalVector("_CldBodyCenterWS", FloatingOrigin.ToRender(bodyPos));
            Shader.SetGlobalVector("_CldSunDirWS", sunDirWorld);
            Shader.SetGlobalVector("_CldSunColor",
                new Vector3((float)starLinear.X, (float)starLinear.Y, (float)starLinear.Z));
            Shader.SetGlobalFloat("_CldPlanetRadius", (float)planetRadius);
            Shader.SetGlobalFloat("_CldBottomRadius", (float)bottomRadius);
            Shader.SetGlobalFloat("_CldTopRadius", (float)topRadius);

            Shader.SetGlobalVector("_CldWindOffset", new Vector3((float)windOffset.X, (float)windOffset.Y, (float)windOffset.Z));

            Shader.SetGlobalFloat("_CldShapeFreq", shapeFreq);
            Shader.SetGlobalFloat("_CldDetailFreq", detailFreq);
            Shader.SetGlobalFloat("_CldWeatherFreq", weatherFreq);
            Shader.SetGlobalFloat("_CldShapeTexelWorldSize", shapeTexelWorldSize);

            Shader.SetGlobalFloat("_CldCoverage", profile.Coverage);
            Shader.SetGlobalFloat("_CldDetailErosion", profile.DetailErosion);
            Shader.SetGlobalFloat("_CldBottomFeather", profile.BottomFeather);
            Shader.SetGlobalFloat("_CldTopFeather", profile.TopFeather);

            Shader.SetGlobalFloat("_CldExtinction", Mathf.Max(0f, profile.Extinction));
            Shader.SetGlobalFloat("_CldScatterAlbedo", Mathf.Clamp01(profile.ScatterAlbedo));
            Shader.SetGlobalFloat("_CldPhaseG", Mathf.Clamp(profile.PhaseAnisotropy, 0f, 0.95f));
            Shader.SetGlobalFloat("_CldPowderStrength", Mathf.Max(0f, profile.PowderStrength));
            Shader.SetGlobalFloat("_CldIntensity", Mathf.Max(0f, profile.Intensity));
            Shader.SetGlobalColor("_CldAmbientTint", profile.AmbientTint);

            Shader.SetGlobalFloat("_CldStepCount", Mathf.Clamp(profile.StepCount, 8, 128));
            Shader.SetGlobalFloat("_CldLightSteps", Mathf.Clamp(profile.LightSteps, 2, 8));
            Shader.SetGlobalFloat("_CldEarlyExitT", Mathf.Clamp(profile.EarlyExitTransmittance, 0.001f, 0.1f));
            Shader.SetGlobalFloat("_CldDebugMode", (float)(int)profile.DebugMode);

            if (shapeTex != null) Shader.SetGlobalTexture("_CldShapeTex", shapeTex);
            if (detailTex != null) Shader.SetGlobalTexture("_CldDetailTex", detailTex);
            if (weatherTex != null) Shader.SetGlobalTexture("_CldWeatherTex", weatherTex);

            if (!loggedOnce)
            {
                loggedOnce = true;
                Debug.Log(string.Format(
                    "[PlanetClouds] active=True, domeR={0:E2}, planetR={1}, bottomR={2}, topR={3}, shapeCell={4}m, dominant={5}",
                    domeRadius, planetRadius, bottomRadius, topRadius, profile.ShapeCellMeters,
                    Runner.DominantBody != null ? Runner.DominantBody.Name : "null"));
            }
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
