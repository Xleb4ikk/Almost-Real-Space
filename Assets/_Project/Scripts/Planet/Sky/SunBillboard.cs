using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Full star visual, created entirely in code (no assets or models):
    ///   — FAR (disc beyond the horizon): billboard quad along the direction to
    ///     the star, inside the far plane; direction and ANGULAR SIZE from the
    ///     real core geometry (2·atan(R★/d); Earth analogue ≈ 0.54°). Behind the
    ///     planet the disc is hidden by the depth buffer (honest sunrises/sunsets).
    ///   — NEAR (fits inside the far plane): glowing sphere Ø 2·R★ at the floating
    ///     origin position (like ShipView: dominant body + bridged delta).
    ///   — Color is physics: Planck spectrum → linear sRGB at TemperatureKelvin
    ///     (StarColorUtil). HDR multiplier DiscBrightness feeds bloom (the corona
    ///     is produced by post-processing).
    ///   — Directional Light: ROTATION only, toward the star; the light color
    ///     (photosphere × atmospheric transmittance) is set by SkyEnvironment
    ///     every frame — day/night, sunset and space with no second owner.
    /// Called from LateUpdate AFTER FirstPersonCamera (reads farClipPlane).
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-30)]
    public sealed class SunBillboard : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Камера игрока (FirstPersonCamera). Пусто — Camera.main.")]
        public Camera PlayerCamera;

        [Tooltip("Directional Light, имитирующий солнечное освещение (необязательно).")]
        public Light SunLight;

        [Tooltip("Физика неба (прозрачность/день-ночь). Пусто — диск без покраснения.")]
        public SkyEnvironment Sky;

        [Tooltip("Температура фотосферы (К). 5772 K = жёлтый карлик аналога Солнца.")]
        public double TemperatureKelvin = 5772d;

        [Tooltip("HDR-яркость диска/сферы (>1 питает bloom = корона).")]
        public float DiscBrightness = 4f;

        [Tooltip("Доля far plane для диск-билборда.")]
        public float BillboardFarFraction = 0.9f;

        [Tooltip("Разрешение процедурной текстуры грануляции (по горизонтали).")]
        public int SurfaceTextureWidth = 512;

        [Tooltip("Зерно грануляции: одинаковый seed = одинаковые пятна.")]
        public int SurfaceSeed = 1;

        [Tooltip("Декоративный тинт поверх физического цвета (по умолчанию чистый).")]
        public Color Tint = Color.white;

        [Tooltip("Оверлей отладки: высота солнца над локальным горизонтом.")]
        public bool DebugHud;

        [Tooltip("Рисовать дальний диск солнца.")]
        public bool RenderDisc = true;

        [Tooltip("Рисовать ближнюю сферу солнца (когда влезает в far).")]
        public bool RenderBodySphere = true;

        private bool loggedDiag;

        private double sunElevationDeg;
        private bool loggedSpawn;
        private double nextLogTime;

        private OrbitingBody star;
        private Transform billboard;
        private Transform nearSphere;
        private Material starMaterial;
        private Material sphereMaterial;
        private Color starColor;

        private void Start()
        {
            if (Runner == null || Runner.SystemState == null)
            {
                Debug.LogWarning("[SunBillboard] ОТКЛЮЧЁН: не назначен Runner или у него нет SystemState " +
                    "(Runner = " + (Runner != null ? "назначен" : "null") + "). Диск солнца создан НЕ будет.");
                enabled = false;
                return;
            }

            Shader shader = FindShader();
            Debug.Log("[SunBillboard] инициализирован: Runner ok, шейдер = " +
                (shader != null ? shader.name : "НЕ НАЙДЕН"));
            star = Runner.SystemState.Root;
            Debug.Log("[SunBillboard] звезда: " + star.Name + ", радиус " + star.Radius.ToString("E2") + " м" +
                (star.Radius < 1e6d
                    ? " — СЛИШКОМ МАЛ: диск будет субпиксельным и невидимым! Проверь Radius в BodyAuthoring звезды."
                    : ""));
            starColor = Tint * ToUnityColor(StarColorUtil.FromTemperature(TemperatureKelvin)) * DiscBrightness;

            starMaterial = new Material(FindDiscShader(shader)) { color = starColor };
            ApplyColor(starMaterial, starColor);
            starMaterial.SetColor("_SunColor", starColor);
            starMaterial.SetFloat("_CoreRadius", 0.66f);
            starMaterial.SetFloat("_GlowFalloff", 3f);

            billboard = CreateQuad("SunBillboard_Disc", starMaterial);

            // Sphere gets its own material copy with procedural granulation
            // (Worley cells like the real Sun; visible when approaching/zooming in).
            sphereMaterial = new Material(shader) { color = starColor };
            ApplyColor(sphereMaterial, starColor);
            Texture2D surface = StarSurfaceTexture.GenerateTexture(
                System.Math.Max(64, SurfaceTextureWidth), System.Math.Max(32, SurfaceTextureWidth / 2), SurfaceSeed,
                StarColorUtil.FromTemperature(TemperatureKelvin));
            sphereMaterial.SetTexture("_BaseColorMap", surface);
            sphereMaterial.SetTexture("_MainTex", surface);
            nearSphere = CreateSphere("SunBillboard_Body", sphereMaterial, star.Radius * 2d);
            nearSphere.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (star == null || Runner.Ship == null)
            {
                return;
            }

            Camera camera = PlayerCamera != null ? PlayerCamera : Camera.main;
            if (camera == null)
            {
                return;
            }

            bool underwater = IsUnderwater(camera);

            star.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPos, out _);
            Vector3d direction = starPos - Runner.Ship.Position;
            double distanceMeters = direction.Magnitude;
            if (!(distanceMeters > 0d))
            {
                return;
            }

            direction = direction / distanceMeters;
            Vector3 simDirection = AstroFrame.ToSimulation(direction);
            // Direction to the star for the planetary shader (Galilego/PlanetSurface).
            Shader.SetGlobalVector("_TerrainSunDir", simDirection);
            float far = camera.farClipPlane;
            Vector3 cameraPosition = camera.transform.position;

            // Sun elevation above the local horizon (for DebugHud).
            sunElevationDeg = double.NaN;
            if (Runner.DominantBody != null)
            {
                Runner.SystemState.EvaluateBodyState(Runner.DominantBody, Runner.TimeSeconds, out Vector3d bodyPosition, out _);
                Vector3d up = Runner.Ship.Position - bodyPosition;
                double upMagnitude = up.Magnitude;
                if (upMagnitude > 0d)
                {
                    double cosAngle = Vector3d.Dot(direction, up / upMagnitude);
                    sunElevationDeg = 90d - (System.Math.Acos(KeplerMath.Clamp(cosAngle, -1d, 1d)) * (180d / System.Math.PI));
                }
            }

            // Auto log: always writes the sun elevation to the console (first
            // frame + every 10 s), so diagnostics do not depend on checkboxes.
            if (!loggedSpawn && !double.IsNaN(sunElevationDeg))
            {
                loggedSpawn = true;
                nextLogTime = Runner.TimeSeconds + 10d;
                Debug.Log(string.Format(
                    "[SunBillboard] СТАРТ: высота солнца {0:F1}° (должно быть 90 − фаза = {1:F1}°), " +
                    "дистанция {2:E2} м, SpawnPhaseDegrees = {3}",
                    sunElevationDeg, 90d - Runner.SpawnPhaseDegrees, distanceMeters, Runner.SpawnPhaseDegrees));
            }
            else if (loggedSpawn && Runner.TimeSeconds >= nextLogTime)
            {
                nextLogTime = Runner.TimeSeconds + 2d;
                Vector3 toSun = billboard.position - camera.transform.position;
                Vector3 viewport = camera.WorldToViewportPoint(billboard.position);
                Renderer billboardRenderer = billboard.GetComponent<Renderer>();
                Vector3 camSpace = camera.transform.InverseTransformDirection(toSun.normalized);
                float turnYaw = Mathf.Atan2(camSpace.x, camSpace.z) * Mathf.Rad2Deg;
                float turnPitch = Mathf.Asin(Mathf.Clamp(camSpace.y, -1f, 1f)) * Mathf.Rad2Deg;
                Debug.Log(string.Format(
                    "[SunBillboard] t = {0:F0} с: высота солнца {1:F1}°; поверни камеру: " +
                    "{2:F0}° по горизонтали ({3}), {4:F0}° по вертикали ({5}); " +
                    "viewport ({6:F2}, {7:F2}, {8:E2}); виден рендеру: {9}",
                    Runner.TimeSeconds, sunElevationDeg,
                    Mathf.Abs(turnYaw), turnYaw >= 0f ? "вправо" : "влево",
                    Mathf.Abs(turnPitch), turnPitch >= 0f ? "вверх" : "вниз",
                    viewport.x, viewport.y, viewport.z,
                    billboardRenderer != null && billboardRenderer.isVisible));
            }

            // Sphere position via floating origin: dominant body + bridged delta
            // to the star (same bridge as in ShipView — double precision).
            Vector3 sphereUnityPosition;
            if (Runner.DominantBody != null
                && Runner.SystemView != null
                && Runner.SystemView.TryGetBodyTransform(Runner.DominantBody.Name, out Transform bodyTransform))
            {
                Runner.SystemState.EvaluateBodyState(Runner.DominantBody, Runner.TimeSeconds, out Vector3d bodyPos, out _);
                sphereUnityPosition = bodyTransform.position + AstroFrame.ToSimulation(starPos - bodyPos);
            }
            else
            {
                sphereUnityPosition = cameraPosition + (simDirection * (float)distanceMeters);
            }

            float sphereDistance = Vector3.Distance(sphereUnityPosition, cameraPosition);
            bool sphereVisible = RenderBodySphere && !underwater && sphereDistance < (far * 0.95f);

            if (sphereVisible)
            {
                nearSphere.position = sphereUnityPosition;
                // Granulation rotates with the body: orientation from the core
                // (tilt+spin) — same bridge as BodyView (T76).
                Runner.SystemState.GetVisualFrame(star, Runner.TimeSeconds, out _, out _, out QuaternionD orientation);
                nearSphere.rotation = AstroFrame.ToSimulation(orientation);
                nearSphere.gameObject.SetActive(true);
                billboard.gameObject.SetActive(false);
            }
            else
            {
                nearSphere.gameObject.SetActive(false);
                // Disc: direction is real, size is 2·atan(R★/d), distance is inside far.
                double angularRadius = System.Math.Atan2(star.Radius, distanceMeters);
                float place = Mathf.Max(1f, far * BillboardFarFraction);
                float size = place * 2f * Mathf.Tan((float)angularRadius) * 1.5f;
                billboard.position = cameraPosition + (simDirection * place);
                // Unity's quad faces along −Z: turn −Z TOWARD the camera
                // (+Z along the star direction), otherwise the disc renders its
                // back side and is culled as a backface.
                billboard.rotation = Quaternion.LookRotation(simDirection);
                billboard.localScale = new Vector3(size, size, 1f);
                billboard.gameObject.SetActive(RenderDisc && !underwater);
            }

            if (!loggedDiag)
            {
                loggedDiag = true;
                Debug.Log(string.Format(
                    "[SunBillboard][DIAG] far={0:E3}, angularRadius={1:E3} рад, discSize={2:E1} м, discActive={3}, sphereVisible={4}, sphereDist={5:E3}",
                    far, System.Math.Atan2(star.Radius, distanceMeters),
                    Mathf.Max(1f, far * BillboardFarFraction) * 2f * Mathf.Tan((float)System.Math.Atan2(star.Radius, distanceMeters)) * 1.5f,
                    billboard.gameObject.activeSelf, sphereVisible, sphereDistance));
            }

            if (SunLight != null)
            {
                // A Directional Light emits along its forward (+Z), so forward
                // must point AWAY from the sun (rays travel toward the scene),
                // otherwise day/night would be inverted.
                SunLight.transform.rotation = Quaternion.LookRotation(-simDirection);
            }

            // The near sun sphere reddens/dims like the disc (the far disc reads
            // transmittance from the global in the Galilego/SunDisc shader).
            if (Sky != null && sphereMaterial != null)
            {
                Vector3 t = Sky.Transmittance;
                ApplyColor(sphereMaterial, new Color(starColor.r * t.x, starColor.g * t.y, starColor.b * t.z, starColor.a));
            }
        }

        private bool IsUnderwater(Camera camera)
        {
            if (Runner == null || Runner.DominantBody == null || camera == null)
            {
                return false;
            }

            OrbitingBody body = Runner.DominantBody;
            if (!WaterQuery.HasOcean(body))
            {
                return false;
            }

            Vector3d observer = FloatingOrigin.Anchor + AstroFrame.ToAstro(camera.transform.position);
            double depth = WaterQuery.SubmersionDepthAt(body, observer, Runner.TimeSeconds);
            return !double.IsNaN(depth) && depth > 0d;
        }

        private void OnGUI()
        {
            if (!DebugHud)
            {
                return;
            }

            string lightState = SunLight != null ? "on" : "OFF (не назначен!)";
            GUI.Label(new Rect(10f, 10f, 600f, 30f), string.Format(
                "Солнце над горизонтом: {0:F1}°   |   Directional Light: {1}   |   Фаза орбиты: зенит 0° → горизонт 90° → ночь 180°, T=38 мин",
                sunElevationDeg, lightState));
        }

        private static Color ToUnityColor(Vector3d linear)
        {
            return new Color((float)linear.X, (float)linear.Y, (float)linear.Z, 1f);
        }

        private static Transform CreateQuad(string name, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.name = name;
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            return quad.transform;
        }

        private static Transform CreateSphere(string name, Material material, double diameter)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(sphere.GetComponent<Collider>());
            sphere.name = name;
            sphere.GetComponent<MeshRenderer>().sharedMaterial = material;
            sphere.transform.localScale = new Vector3((float)diameter, (float)diameter, (float)diameter);
            return sphere.transform;
        }

        private static Shader FindShader()
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            return shader;
        }

        /// <summary>Round disc: radial falloff; fallback is an unlit shader (quad).</summary>
        private static Shader FindDiscShader(Shader fallback)
        {
            Shader disc = Shader.Find("Galilego/SunDisc");
            return disc != null ? disc : fallback;
        }

        /// <summary>
        /// The color is set into every shader slot we know of: Material.color
        /// ([MainColor]), _UnlitColor (HDRP/Unlit), _BaseColor (HDRP/Lit family).
        /// Missing properties are silently ignored.
        /// </summary>
        private static void ApplyColor(Material material, Color color)
        {
            material.color = color;
            material.SetColor("_UnlitColor", color);
            material.SetColor("_BaseColor", color);
        }
    }
}
