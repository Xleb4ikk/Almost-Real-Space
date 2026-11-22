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

        [Tooltip("Минимальный радиус диска на экране, px. Физика тут бессильна: 0.57° при FOV 83° " +
            "и 720p — это 5 px, точка. Пол включается плавно по мере удаления и гаснет к моменту, " +
            "когда подхватывают настоящие чанки. 0 = строго физический размер.")]
        public float MinScreenRadiusPixels = 11f;

        [Tooltip("Компенсация яркости при раздувании диска. 1 = поверхностная яркость сохраняется " +
            "(луна просто крупнее), 0.08 = сохраняется полный поток (крупный диск тускнеет).")]
        public float MinEnergyScale = 1f;

        [Tooltip("Яркость диска относительно альбедо (подгоняется глазами: диск должен " +
            "читаться как освещённая поверхность, а не как лампа).")]
        public float Brightness = 14f;

        [Range(0f, 1f)]
        [Tooltip("Затемнение к краю: у безатмосферного тела лимб не чёрный, но и не плоский.")]
        public float LimbDarkening = 0.35f;

        [Tooltip("Альбедо диска в линейном пространстве. При UseTerrainPalette берётся " +
            "из палитры профиля рельефа этого тела.")]
        public Color Albedo = new Color(0.5f, 0.49f, 0.47f);

        [Tooltip("Взять альбедо из BodyAuthoring.TerrainPreset (палитра Snow).")]
        public bool UseTerrainPalette = true;

        [Tooltip("Карта альбедо в телесных координатах (maria/нагорья). Без неё диск — ровный шар.")]
        public Texture2D SurfaceMap;

        [Range(0f, 1f)]
        [Tooltip("Сила карты альбедо.")]
        public float SurfaceMapStrength = 1f;

        private OrbitingBody body;
        private OrbitingBody star;
        private Transform disc;
        private Material material;
        private Vector3d LastBodyPosition;
        private double LastRadius;
        private float LastFar;
        private float LastPixelRadius;

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
            if (!(distance > 1d))
            {
                SetActive(false);
                return;
            }

            float angularRadius = Mathf.Atan2((float)body.Radius, (float)distance);
            float fovRadians = Mathf.Max(0.01f, camera.fieldOfView) * Mathf.Deg2Rad;
            float pixelRadius = angularRadius / Mathf.Tan(fovRadians * 0.5f) * (Screen.height * 0.5f);
            float floorPixels = Mathf.Max(pixelRadius, MinScreenRadiusPixels);
            LastBodyPosition = bodyPos;
            LastRadius = body.Radius;
            LastFar = far;
            LastPixelRadius = pixelRadius;

            // The disc stands in for a body the camera cannot draw OR draws so small that
            // it reads as a star. It must not depend on `far` alone: the far plane jumps to
            // the SOI radius in flyby mode, and then "inside far" hid the disc while the
            // real body was still 5 px - the moon visibly shrank to a speck on approach.
            // Hand the body over only when the real chunks are both inside the frustum AND
            // already at least as big as the floor, so the sizes match at the swap.
            bool chunksCover = nearSurface < (far * 0.95d)
                && nearSurface < (body.Radius * 1.5d)
                && pixelRadius >= MinScreenRadiusPixels;
            if (chunksCover)
            {
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

            float size = discDistance * 2f * Mathf.Tan(angularRadius) * 1.02f;

            // Subpixel discs flicker; the floor keeps them readable, and MinEnergyScale
            // decides whether the extra area costs surface brightness or total flux.
            float pixelScale = pixelRadius > 1e-6f ? Mathf.Max(1f, floorPixels / pixelRadius) : 1f;
            disc.localScale = new Vector3(size * pixelScale, size * pixelScale, 1f);
            float energyScale = Mathf.Max(MinEnergyScale, 1f / (pixelScale * pixelScale));

            star.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPos, out _);
            Vector3 sunDirection = AstroFrame.ToSimulation((starPos - bodyPos).Normalized);

            Runner.SystemState.GetVisualFrame(body, Runner.TimeSeconds, out _, out _, out QuaternionD orientation);
            Quaternion bodyRotation = AstroFrame.ToSimulation(orientation);

            material.SetColor("_BFD_Albedo", Albedo);
            material.SetVector("_BFD_SunDirWS", ToVec(sunDirection));
            material.SetVector("_BFD_RightWS", disc.right);
            material.SetVector("_BFD_UpWS", disc.up);
            material.SetVector("_BFD_ViewWS", ToVec(-simDirection));
            material.SetFloat("_BFD_IntensityScale", Brightness * energyScale);
            material.SetFloat("_BFD_DiscDistance", discDistance);
            material.SetFloat("_BFD_LimbDarkening", LimbDarkening);
            material.SetTexture("_BFD_Map", SurfaceMap);
            material.SetFloat("_BFD_MapStrength", SurfaceMap != null ? SurfaceMapStrength : 0f);
            material.SetMatrix("_BFD_BodyToWorld", Matrix4x4.TRS(Vector3.zero, bodyRotation, Vector3.one));

            SetActive(true);
        }

        private void SetActive(bool active)
        {
            if (disc != null && disc.gameObject.activeSelf != active)
            {
                disc.gameObject.SetActive(active);
                Debug.Log("[BodyFarDisc] " + name + (active ? " ON" : " OFF")
                    + " | alt=" + (((Runner.PlayerPosition - LastBodyPosition).Magnitude - LastRadius) / 1000d).ToString("F0")
                    + " km far=" + (LastFar / 1000f).ToString("F0")
                    + " km pxR=" + LastPixelRadius.ToString("F2")
                    + " floor=" + MinScreenRadiusPixels.ToString("F1"));
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
