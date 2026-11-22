using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Galilego.Universe
{
    /// <summary>
    /// Camera-facing stand-in for a body that is farther than the camera can draw.
    ///
    /// The terrain camera's far plane is the PHYSICAL horizon (≈300 km standing on
    /// Terra), so a moon at 83 000 km is never rasterised: its chunks are clipped and
    /// the body simply does not exist in the frame. This component keeps a quad at
    /// 0.28·far with the body's exact angular radius and a Lambert phase, so the disc
    /// looks like the real one without touching near/far (near/far ≈ 3·10^6 is what
    /// Doc/SkyDomes.md warns about).
    ///
    /// Same contract as GasGiantView's far disc: the cloud tops/surface switch it off
    /// as soon as they fit inside the far plane, and the material is premultiplied so
    /// it covers the star field without an extra opaque depth pass.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-60)]
    public sealed class BodyFarDiscView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Камера игрока. Пусто = Camera.main.")]
        public Camera PlayerCamera;

        [Tooltip("Доля дальней плоскости камеры, на которой висит диск. 0.25 совпадает с " +
            "солнечным билбордом, 0.28 держит луну за ним (затмение рисует солнце сверху).")]
        public float FarFraction = 0.28f;

        [Tooltip("Меньше этого радиуса диск не рисуется: субпиксельная точка мигает. " +
            "Площадь сохраняется множителем яркости.")]
        public float MinScreenRadiusPixels = 2.5f;

        [Tooltip("Нижний предел компенсации яркости при раздувании субпиксельного диска.")]
        public float MinEnergyScale = 0.08f;

        [Tooltip("Яркость диска относительно альбедо (подгоняется глазами: диск должен " +
            "читаться как освещённая поверхность, а не как лампа).")]
        public float Brightness = 2.2f;

        [Range(0f, 1f)]
        [Tooltip("Затемнение к краю: у безатмосферного тела лимб не чёрный, но и не плоский.")]
        public float LimbDarkening = 0.35f;

        [Tooltip("Альбедо диска в линейном пространстве. При UseTerrainPalette берётся " +
            "из палитры профиля рельефа этого тела.")]
        public Color Albedo = new Color(0.5f, 0.49f, 0.47f);

        [Tooltip("Взять альбедо из BodyAuthoring.TerrainPreset (палитра Snow).")]
        public bool UseTerrainPalette = true;

        private OrbitingBody body;
        private OrbitingBody star;
        private Transform disc;
        private Material material;

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

            if (body == null)
            {
                Debug.LogWarning("BodyFarDiscView: тело \"" + gameObject.name + "\" не найдено в системе — диск отключён.");
                enabled = false;
                return;
            }

            star = Runner.SystemState.Root;

            BodyAuthoring authoring = GetComponent<BodyAuthoring>();
            if (UseTerrainPalette && authoring != null && authoring.TerrainPreset != null
                && authoring.TerrainPreset.Profile != null && authoring.TerrainPreset.Profile.Palette != null)
            {
                Albedo = authoring.TerrainPreset.Profile.Palette.SnowLinear;
            }

            Shader shader = Shader.Find("Galilego/BodyFarDisc");
            if (shader == null)
            {
                Debug.LogWarning("BodyFarDiscView: нет шейдера Galilego/BodyFarDisc — диск отключён.");
                enabled = false;
                return;
            }

            GameObject go = new GameObject(name + "FarDisc");
            go.hideFlags = HideFlags.DontSave;
            disc = go.transform;
            disc.SetParent(null, false);

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildQuad();

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            material = new Material(shader) { name = name + "FarDisc (runtime)" };
            material.SetColor("_BFD_Albedo", Albedo);
            material.SetFloat("_BFD_LimbDarkening", LimbDarkening);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            disc.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (disc != null)
            {
                Destroy(disc.gameObject);
            }

            if (material != null)
            {
                Destroy(material);
            }
        }

        private void LateUpdate()
        {
            if (body == null || star == null || disc == null || Runner == null || Runner.SystemState == null)
            {
                return;
            }

            Camera camera = PlayerCamera != null ? PlayerCamera : Camera.main;
            if (camera == null)
            {
                SetActive(false);
                return;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d toBody = bodyPos - Runner.PlayerPosition;
            double distance = toBody.Magnitude;

            float far = Mathf.Max(1f, camera.farClipPlane);
            double nearSurface = distance - body.Radius;
            if (!(distance > 1d) || nearSurface < far * 0.95d)
            {
                // Inside the far plane the real chunks draw it; the disc must not double it.
                SetActive(false);
                return;
            }

            Vector3 simDirection = AstroFrame.ToSimulation(toBody / distance);
            float discDistance = Mathf.Max(1f, far * FarFraction);
            Vector3 cameraPosition = camera.transform.position;
            disc.position = cameraPosition + (simDirection * discDistance);

            Vector3 up = Vector3.ProjectOnPlane(camera.transform.up, simDirection);
            if (up.sqrMagnitude < 1e-8f)
            {
                up = Vector3.ProjectOnPlane(Vector3.up, simDirection);
            }

            if (up.sqrMagnitude < 1e-8f)
            {
                up = Vector3.ProjectOnPlane(Vector3.right, simDirection);
            }

            disc.rotation = Quaternion.LookRotation(simDirection, up.normalized);

            float angularRadius = Mathf.Atan2((float)body.Radius, (float)distance);
            float size = discDistance * 2f * Mathf.Tan(angularRadius) * 1.02f;

            // A 0.5-degree moon is 6 px at 900p: a hard subpixel quad flickers as the
            // body drifts, so expand to a minimum screen radius and keep the energy.
            float fovRadians = Mathf.Max(0.01f, camera.fieldOfView) * Mathf.Deg2Rad;
            float pixelRadius = angularRadius / Mathf.Tan(fovRadians * 0.5f) * (Screen.height * 0.5f);
            float pixelScale = (MinScreenRadiusPixels > 0f && pixelRadius > 1e-6f)
                ? Mathf.Max(1f, MinScreenRadiusPixels / pixelRadius)
                : 1f;
            disc.localScale = new Vector3(size * pixelScale, size * pixelScale, 1f);
            float energyScale = Mathf.Max(MinEnergyScale, 1f / (pixelScale * pixelScale));

            star.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPos, out _);
            Vector3 sunDirection = AstroFrame.ToSimulation((starPos - bodyPos).Normalized);

            material.SetColor("_BFD_Albedo", Albedo);
            material.SetVector("_BFD_SunDirWS", ToVec(sunDirection));
            material.SetVector("_BFD_RightWS", disc.right);
            material.SetVector("_BFD_UpWS", disc.up);
            material.SetVector("_BFD_ViewWS", ToVec(-simDirection));
            material.SetFloat("_BFD_IntensityScale", Brightness * energyScale);
            material.SetFloat("_BFD_DiscDistance", discDistance);
            material.SetFloat("_BFD_LimbDarkening", LimbDarkening);

            SetActive(true);
        }

        private void SetActive(bool active)
        {
            if (disc != null && disc.gameObject.activeSelf != active)
            {
                disc.gameObject.SetActive(active);
            }
        }

        private static Vector4 ToVec(Vector3 v)
        {
            return new Vector4(v.x, v.y, v.z, 0f);
        }

        /// <summary>Unit quad centred on the origin, UV 0..1 — the disc is cut in the shader.</summary>
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "BodyFarDiscQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
