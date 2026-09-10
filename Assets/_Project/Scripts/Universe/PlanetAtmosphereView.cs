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
    /// Все параметры — из AtmosphereProfile (общий с физикой), тумблер VisualEnabled.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-35)]
    public sealed class PlanetAtmosphereView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Сегментов по широте купола.")]
        public int LatitudeSegments = 24;

        [Tooltip("Сегментов по долготе купола.")]
        public int LongitudeSegments = 48;

        private OrbitingBody body;
        private AtmosphereProfile profile;
        private Transform shell;
        private Material materialCache;
        private bool loggedOnce;

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

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            double radius = body.Radius + profile.TopAltitudeMeters;
            float scaleHeight = profile.ScaleHeightMeters > 0d
                ? (float)profile.ScaleHeightMeters
                : (float)(profile.TopAltitudeMeters / 8d);

            Vector3 cameraPosition = camera.transform.position;
            // Меш купола — единичный радиус (BuildSphereMesh(1f, …)), поэтому
            // localScale == мировой радиус. Радиус 0.5·far держит поверхность
            // купола ВНУТРИ far: если выставить её ровно в far, вершины у края
            // фрустума зарезаются far-плоскостью по округлению — в небе
            // появляются чёрные дыры-кляксы, «плывущие» при повороте камеры.
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

            Shader.SetGlobalVector("_AtmCameraWS", cameraPosition);
            Shader.SetGlobalVector("_AtmBodyCenterWS", FloatingOrigin.ToRender(bodyPos));
            Shader.SetGlobalVector("_AtmSunDirWS", sunDirWorld);
            Shader.SetGlobalFloat("_AtmPlanetRadius", (float)body.Radius);
            Shader.SetGlobalFloat("_AtmRadius", (float)radius);
            Shader.SetGlobalFloat("_AtmScaleHeight", scaleHeight);
            Shader.SetGlobalColor("_AtmRayleighColor", profile.RayleighColor);
            Shader.SetGlobalColor("_AtmMieColor", profile.MieColor);
            Shader.SetGlobalFloat("_AtmIntensity", profile.Intensity);
            Shader.SetGlobalFloat("_AtmMieAnisotropy", profile.MieAnisotropy);
            Shader.SetGlobalFloat("_AtmStepCount", Mathf.Clamp(profile.StepCount, 2, 96));
            Shader.SetGlobalFloat("_AtmDensityFalloff", Mathf.Max(0.01f, profile.DensityFalloff));
            Shader.SetGlobalFloat("_AtmPlanetOcclusion", profile.PlanetOcclusion ? 1f : 0f);
            Shader.SetGlobalFloat("_AtmMultiScatter", profile.MultiScatter);

            if (!loggedOnce)
            {
                loggedOnce = true;
                Debug.Log(string.Format(
                    "[PlanetAtmosphere] active=True, domeR={0:E2}, R={1}, R_atm={2}, H={3}, Intensity={4}, dominant={5}",
                    domeRadius, body.Radius, radius, scaleHeight, profile.Intensity,
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
