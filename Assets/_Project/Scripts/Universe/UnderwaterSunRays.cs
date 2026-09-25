using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    [DefaultExecutionOrder(10)]
    public sealed class UnderwaterSunRays : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            UnderwaterEffect effect = FindAnyObjectByType<UnderwaterEffect>();
            if (effect != null && effect.GetComponent<UnderwaterSunRays>() == null)
            {
                effect.gameObject.AddComponent<UnderwaterSunRays>();
            }
        }

        [Header("Ленты")]
        public bool EnableRibbons = true;
        [Min(1)]
        public int RayCount = 8;
        [Min(1f)]
        public float RayLengthMeters = 12f;
        [Min(0.1f)]
        public float RayFieldRadiusMeters = 3.5f;
        [Min(0.01f)]
        public float RayWidthMeters = 0.12f;
        [Min(0f)]
        public float RayStrength = 0.18f;
        [Min(0.01f)]
        public float RayNoiseScale = 0.75f;
        [Min(0f)]
        public float RayNoiseSpeed = 0.8f;

        [Header("Экранные лучи")]
        public bool EnableScreenGodRays = false;
        [Range(4, 64)]
        public int ScreenRaySamples = 24;
        [Min(0f)]
        public float ScreenRayStrength = 0.45f;
        [Min(0f)]
        public float ScreenRayDensity = 0.72f;
        [Range(0.5f, 0.99f)]
        public float ScreenRayDecay = 0.92f;
        [Min(0f)]
        public float ScreenRayThreshold = 0.8f;

        [Header("Каустика")]
        public bool EnableCaustics = true;
        [Min(0f)]
        public float CausticStrength = 0.75f;
        [Min(0.001f)]
        public float CausticScale = 0.7f;
        [Min(0.1f)]
        public float CausticFadeStartMeters = 3f;
        [Min(0.2f)]
        public float CausticFadeEndMeters = 28f;

        private UnderwaterEffect effect;
        private GameObject ribbonObject;
        private MeshFilter ribbonFilter;
        private MeshRenderer ribbonRenderer;
        private Mesh ribbonMesh;
        private Material ribbonMaterial;
        private Material screenMaterial;
        private Material screenCompositeMaterial;
        private CustomPassVolume screenVolume;
        private FullScreenCustomPass screenPass;
        private FullScreenCustomPass screenCompositePass;
        private Vector3[] ribbonVertices;
        private Vector2[] ribbonUvs;
        private int[] ribbonTriangles;
        private int ribbonVertexCapacity;
        private int ribbonIndexCapacity;
        private bool resourcesWarningLogged;
        private Vector3d ribbonAnchorBodyFixed;
        private double ribbonAnchorDepthMeters;
        private bool ribbonAnchorValid;

        private void Awake()
        {
            effect = GetComponent<UnderwaterEffect>();
            if (effect == null)
            {
                effect = FindAnyObjectByType<UnderwaterEffect>();
            }

            ResetCausticGlobals();
        }

        private void LateUpdate()
        {
            if (effect == null || !effect.isActiveAndEnabled)
            {
                DisableEffects();
                return;
            }

            Camera camera = effect.EffectCamera != null ? effect.EffectCamera : Camera.main;
            if (camera == null)
            {
                DisableEffects();
                return;
            }

            effect.EffectCamera = camera;
            EnsureResources(camera);

            bool active = EnableRibbons || EnableScreenGodRays || EnableCaustics;
            bool sunAboveWater = Vector3.Dot(effect.SunDirection, effect.LocalUp) > 0.015f
                && effect.SunDirection.sqrMagnitude > 0.5f;
            float depthFade = effect.EffectWeight * Mathf.Exp(-Mathf.Max(0f, (float)effect.EyeDepthMeters) / 8f);
            Vector3 sunColor = GetSunColor();
            Vector3 refractedSun = Refract(-effect.SunDirection, effect.LocalUp, 1f / 1.333f);
            bool refracted = sunAboveWater
                && refractedSun.sqrMagnitude > 0.001f
                && Vector3.Dot(refractedSun, effect.LocalUp) < -0.001f;

            if (!active || !effect.IsUnderwater || depthFade <= 0.001f)
            {
                DisableEffects();
                return;
            }

            if (!ribbonAnchorValid && !CaptureRibbonAnchor(camera))
            {
                DisableEffects();
                return;
            }

            if (EnableRibbons && refracted && ribbonRenderer != null && ribbonMaterial != null
                && TryGetRibbonFrame(camera, out Vector3 ribbonAnchor, out Vector3 ribbonUp,
                    out Matrix4x4 ribbonWorldToBody, out Vector3 ribbonBodyAnchor))
            {
                UpdateRibbonMesh(
                    ribbonAnchor,
                    ribbonUp,
                    refractedSun,
                    depthFade,
                    sunColor,
                    ribbonWorldToBody,
                    ribbonBodyAnchor);
                ribbonRenderer.enabled = true;
            }
            else if (ribbonRenderer != null)
            {
                ribbonRenderer.enabled = false;
            }

            UpdateScreenPass(camera, sunColor, depthFade, sunAboveWater);
            UpdateCaustics(sunColor, depthFade, sunAboveWater);
        }

        private void EnsureResources(Camera camera)
        {
            if (ribbonObject == null)
            {
                EnsureRibbonResources();
            }

            if (screenVolume == null)
            {
                if (screenMaterial == null)
                {
                    Shader shader = Shader.Find("Galilego/UnderwaterGodRays");
                    if (shader == null)
                    {
                        WarnMissingShader("Galilego/UnderwaterGodRays");
                        return;
                    }

                    screenMaterial = new Material(shader);
                }

                if (screenCompositeMaterial == null)
                {
                    Shader shader = Shader.Find("Galilego/UnderwaterGodRaysComposite");
                    if (shader == null)
                    {
                        WarnMissingShader("Galilego/UnderwaterGodRaysComposite");
                        return;
                    }

                    screenCompositeMaterial = new Material(shader);
                }

                screenPass = new FullScreenCustomPass
                {
                    name = "Underwater God Rays Source",
                    fullscreenPassMaterial = screenMaterial,
                    fetchColorBuffer = true,
                    materialPassName = "UnderwaterGodRays",
                    targetColorBuffer = CustomPass.TargetBuffer.Custom,
                    targetDepthBuffer = CustomPass.TargetBuffer.None,
                    clearFlags = ClearFlag.Color,
                };
                screenCompositePass = new FullScreenCustomPass
                {
                    name = "Underwater God Rays Composite",
                    fullscreenPassMaterial = screenCompositeMaterial,
                    fetchColorBuffer = false,
                    materialPassName = "UnderwaterGodRaysComposite",
                    targetColorBuffer = CustomPass.TargetBuffer.Camera,
                    targetDepthBuffer = CustomPass.TargetBuffer.None,
                    clearFlags = ClearFlag.None,
                };
                screenVolume = CreateVolume("UnderwaterGodRaysVolume", camera, 0f, screenPass, screenCompositePass);
            }
            else
            {
                screenVolume.targetCamera = camera;
            }
        }

        private void EnsureRibbonResources()
        {
            if (ribbonObject != null)
            {
                return;
            }

            Shader shader = Shader.Find("Galilego/UnderwaterSunRays");
            if (shader == null)
            {
                WarnMissingShader("Galilego/UnderwaterSunRays");
                return;
            }

            ribbonMaterial = new Material(shader);
            ribbonObject = new GameObject("UnderwaterSunRayRibbons");
            ribbonObject.layer = gameObject.layer;
            ribbonFilter = ribbonObject.AddComponent<MeshFilter>();
            ribbonRenderer = ribbonObject.AddComponent<MeshRenderer>();
            ribbonRenderer.sharedMaterial = ribbonMaterial;
            ribbonRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ribbonRenderer.receiveShadows = false;
            ribbonRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            ribbonObject.SetActive(false);
        }

        private CustomPassVolume CreateVolume(string objectName, Camera camera, float priority, params CustomPass[] passes)
        {
            GameObject volumeObject = new GameObject(objectName);
            volumeObject.SetActive(false);
            volumeObject.layer = gameObject.layer;
            CustomPassVolume volume = volumeObject.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.targetCamera = camera;
            volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            volume.priority = priority;
            foreach (CustomPass pass in passes)
            {
                volume.customPasses.Add(pass);
            }

            volumeObject.SetActive(true);
            return volume;
        }

        private bool CaptureRibbonAnchor(Camera camera)
        {
            if (effect == null || effect.Runner == null || effect.Runner.DominantBody == null)
            {
                return false;
            }

            OrbitingBody body = effect.Runner.DominantBody;
            body.EvaluateWorldState(effect.Runner.TimeSeconds, out Vector3d bodyPosition, out _);
            QuaternionD orientation = body.GetVisualOrientation(effect.Runner.TimeSeconds);
            Vector3d observer = FloatingOrigin.Anchor + AstroFrame.ToAstro(camera.transform.position);
            double depth = WaterQuery.SubmersionDepthAt(body, observer, effect.Runner.TimeSeconds);
            if (double.IsNaN(depth) || depth <= 0d)
            {
                return false;
            }

            Vector3d bodyFixed = orientation.Conjugated.Rotate(observer - bodyPosition);
            if (bodyFixed.Magnitude <= 1e-6d)
            {
                return false;
            }

            ribbonAnchorBodyFixed = bodyFixed;
            ribbonAnchorDepthMeters = depth;
            ribbonAnchorValid = true;
            return true;
        }

        private bool TryGetRibbonFrame(
            Camera camera,
            out Vector3 anchorPosition,
            out Vector3 up,
            out Matrix4x4 worldToBody,
            out Vector3 bodyAnchor)
        {
            anchorPosition = Vector3.zero;
            up = Vector3.up;
            worldToBody = Matrix4x4.identity;
            bodyAnchor = Vector3.zero;
            if (!ribbonAnchorValid || camera == null || effect == null || effect.Runner == null
                || effect.Runner.DominantBody == null)
            {
                return false;
            }

            OrbitingBody body = effect.Runner.DominantBody;
            body.EvaluateWorldState(effect.Runner.TimeSeconds, out Vector3d bodyPosition, out _);
            QuaternionD orientation = body.GetVisualOrientation(effect.Runner.TimeSeconds);
            Vector3d radial = ribbonAnchorBodyFixed;
            if (radial.Magnitude <= 1e-6d)
            {
                return false;
            }

            Quaternion simOrientation = AstroFrame.ToSimulation(orientation);
            worldToBody = Matrix4x4.Rotate(Quaternion.Inverse(simOrientation));
            Vector3d observer = FloatingOrigin.Anchor + AstroFrame.ToAstro(camera.transform.position);
            Vector3d cameraBodyFixed = orientation.Conjugated.Rotate(observer - bodyPosition);
            bodyAnchor = AstroFrame.ToSimulation(cameraBodyFixed);
            Shader.SetGlobalVector("_UnderwaterRayCameraPos", camera.transform.position);
            up = AstroFrame.ToSimulation(radial / radial.Magnitude);
            Vector3d absolute = bodyPosition + orientation.Rotate(ribbonAnchorBodyFixed);
            anchorPosition = FloatingOrigin.ToRender(absolute);
            return true;
        }

        private void UpdateRibbonMesh(
            Vector3 anchorPosition,
            Vector3 up,
            Vector3 direction,
            float depthFade,
            Vector3 sunColor,
            Matrix4x4 worldToBody,
            Vector3 bodyAnchor)
        {
            int count = Mathf.Clamp(RayCount, 1, 32);
            EnsureMeshCapacity(count);
            if (ribbonMesh == null)
            {
                return;
            }

            Vector3 rayDirection = direction.normalized;
            Vector3 side = Vector3.Cross(rayDirection, up);
            if (side.sqrMagnitude < 1e-5f)
            {
                side = Vector3.Cross(rayDirection, Vector3.right);
            }

            if (side.sqrMagnitude < 1e-5f)
            {
                side = Vector3.right;
            }

            side.Normalize();
            Vector3 other = Vector3.Cross(rayDirection, side).normalized;
            float depth = Mathf.Max(0f, (float)ribbonAnchorDepthMeters);
            float halfLength = Mathf.Max(0.5f, RayLengthMeters * 0.5f);
            Vector3 origin = anchorPosition + (up * (depth + 0.35f));
            int vertex = 0;
            int triangle = 0;

            for (int i = 0; i < count; i++)
            {
                float angle = (Mathf.PI * 2f * i / count) + (0.17f * i);
                Vector3 center = (side * Mathf.Cos(angle)) + (other * Mathf.Sin(angle));
                center *= Mathf.Max(0.1f, RayFieldRadiusMeters);
                Vector3 start = origin + center - (rayDirection * halfLength);
                Vector3 end = origin + center + (rayDirection * halfLength);
                AddRibbon(anchorPosition, start, end, side, RayWidthMeters, ref vertex, ref triangle);
                AddRibbon(anchorPosition, start, end, other, RayWidthMeters * 0.72f, ref vertex, ref triangle);
            }

            ribbonMesh.vertices = ribbonVertices;
            ribbonMesh.uv = ribbonUvs;
            ribbonMesh.triangles = ribbonTriangles;
            ribbonMesh.RecalculateBounds();
            ribbonObject.transform.position = anchorPosition;
            ribbonObject.transform.rotation = Quaternion.identity;
            ribbonObject.SetActive(true);
            Shader.SetGlobalMatrix("_UnderwaterRayWorldToBody", worldToBody);
            Shader.SetGlobalVector("_UnderwaterRayBodyAnchor", new Vector4(
                bodyAnchor.x, bodyAnchor.y, bodyAnchor.z, 0f));
            ribbonMaterial.SetColor("_RayColor", new Color(sunColor.x, sunColor.y, sunColor.z, 1f));
            ribbonMaterial.SetFloat("_RayStrength", Mathf.Max(0f, RayStrength));
            ribbonMaterial.SetFloat("_RaySurfaceFade", depthFade);
            ribbonMaterial.SetFloat("_RayTime", Time.time);
            ribbonMaterial.SetFloat("_RayNoiseScale", Mathf.Max(0.01f, RayNoiseScale));
            ribbonMaterial.SetFloat("_RayNoiseSpeed", Mathf.Max(0f, RayNoiseSpeed));
        }

        private void EnsureMeshCapacity(int count)
        {
            int vertexCapacity = count * 8;
            int indexCapacity = count * 12;
            if (ribbonMesh != null && vertexCapacity <= ribbonVertexCapacity && indexCapacity <= ribbonIndexCapacity)
            {
                return;
            }

            if (ribbonMesh != null)
            {
                Destroy(ribbonMesh);
            }

            ribbonVertexCapacity = vertexCapacity;
            ribbonIndexCapacity = indexCapacity;
            ribbonVertices = new Vector3[ribbonVertexCapacity];
            ribbonUvs = new Vector2[ribbonVertexCapacity];
            ribbonTriangles = new int[ribbonIndexCapacity];
            ribbonMesh = new Mesh { name = "UnderwaterSunRayRibbons" };
            ribbonMesh.MarkDynamic();
            ribbonFilter.sharedMesh = ribbonMesh;
        }

        private void AddRibbon(Vector3 meshOrigin, Vector3 start, Vector3 end, Vector3 widthAxis, float width, ref int vertex, ref int triangle)
        {
            float halfWidth = Mathf.Max(0.001f, width * 0.5f);
            Vector3 offset = widthAxis.normalized * halfWidth;
            ribbonVertices[vertex] = start - offset - meshOrigin;
            ribbonUvs[vertex++] = new Vector2(0f, 0f);
            ribbonVertices[vertex] = start + offset - meshOrigin;
            ribbonUvs[vertex++] = new Vector2(1f, 0f);
            ribbonVertices[vertex] = end + offset - meshOrigin;
            ribbonUvs[vertex++] = new Vector2(1f, 1f);
            ribbonVertices[vertex] = end - offset - meshOrigin;
            ribbonUvs[vertex++] = new Vector2(0f, 1f);
            int first = vertex - 4;
            ribbonTriangles[triangle++] = first;
            ribbonTriangles[triangle++] = first + 1;
            ribbonTriangles[triangle++] = first + 2;
            ribbonTriangles[triangle++] = first;
            ribbonTriangles[triangle++] = first + 2;
            ribbonTriangles[triangle++] = first + 3;
        }

        private void UpdateScreenPass(Camera camera, Vector3 sunColor, float depthFade, bool sunAboveWater)
        {
            if (screenPass == null || screenCompositePass == null || screenMaterial == null)
            {
                return;
            }

            Vector3 viewport = camera.WorldToViewportPoint(camera.transform.position + (effect.SunDirection * 10000f));
            bool visible = sunAboveWater && viewport.z > 0f
                && viewport.x > -0.2f && viewport.x < 1.2f
                && viewport.y > -0.2f && viewport.y < 1.2f;
            bool passActive = EnableScreenGodRays && visible && depthFade > 0.001f;
            screenPass.enabled = passActive;
            screenCompositePass.enabled = passActive;
            screenMaterial.SetColor("_GodRayColor", new Color(sunColor.x, sunColor.y, sunColor.z, 1f));
            screenMaterial.SetVector("_GodRaySunViewport", new Vector4(viewport.x, viewport.y, 0f, 0f));
            screenMaterial.SetFloat("_GodRayStrength", Mathf.Max(0f, ScreenRayStrength));
            screenMaterial.SetFloat("_GodRayDensity", Mathf.Max(0.01f, ScreenRayDensity));
            screenMaterial.SetFloat("_GodRayDecay", Mathf.Clamp(ScreenRayDecay, 0.5f, 0.99f));
            screenMaterial.SetFloat("_GodRayThreshold", Mathf.Max(0f, ScreenRayThreshold));
            screenMaterial.SetInt("_GodRaySamples", Mathf.Clamp(ScreenRaySamples, 4, 64));
            screenMaterial.SetFloat("_GodRayDepthFade", depthFade);
        }

        private void UpdateCaustics(Vector3 sunColor, float depthFade, bool sunAboveWater)
        {
            if (effect.Runner == null || effect.Runner.DominantBody == null)
            {
                ResetCausticGlobals();
                return;
            }

            OrbitingBody body = effect.Runner.DominantBody;
            body.EvaluateWorldState(effect.Runner.TimeSeconds, out Vector3d bodyPosition, out _);
            QuaternionD orientation = body.GetVisualOrientation(effect.Runner.TimeSeconds);
            Quaternion simOrientation = AstroFrame.ToSimulation(orientation);
            Matrix4x4 worldToBody = Matrix4x4.Rotate(Quaternion.Inverse(simOrientation));
            Vector3d observer = effect.Runner.PlayerPosition;
            if (effect.EffectCamera != null)
            {
                observer = FloatingOrigin.Anchor + AstroFrame.ToAstro(effect.EffectCamera.transform.position);
            }

            Vector3d relative = observer - bodyPosition;
            Vector3d bodyFixed = orientation.Conjugated.Rotate(relative);
            Vector3 bodyAnchor = new Vector3((float)bodyFixed.X, (float)bodyFixed.Z, (float)-bodyFixed.Y);
            Vector3 cameraPosition = effect.EffectCamera != null
                ? effect.EffectCamera.transform.position
                : Vector3.zero;
            Color causticColor = new Color(sunColor.x, sunColor.y, sunColor.z, 1f);
            float causticFadeStart = Mathf.Max(0.1f, CausticFadeStartMeters);
            float causticFadeEnd = Mathf.Max(causticFadeStart + 0.1f, CausticFadeEndMeters);
            float enabled = EnableCaustics && sunAboveWater && depthFade > 0.001f ? 1f : 0f;

            Shader.SetGlobalMatrix("_UnderwaterWorldToBody", worldToBody);
            Shader.SetGlobalVector("_UnderwaterBodyAnchor", new Vector4(bodyAnchor.x, bodyAnchor.y, bodyAnchor.z, 0f));
            Shader.SetGlobalVector("_UnderwaterCameraPos", new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 0f));
            Shader.SetGlobalVector("_UnderwaterUp", new Vector4(effect.LocalUp.x, effect.LocalUp.y, effect.LocalUp.z, 0f));
            Shader.SetGlobalVector("_UnderwaterSunDir", new Vector4(effect.SunDirection.x, effect.SunDirection.y, effect.SunDirection.z, 0f));
            Shader.SetGlobalFloat("_UnderwaterEyeDepth", (float)effect.EyeDepthMeters);
            Shader.SetGlobalFloat("_UnderwaterTime", Time.time);
            Shader.SetGlobalColor("_UnderwaterCausticColor", causticColor);
            Shader.SetGlobalFloat("_UnderwaterCausticStrength", Mathf.Max(0f, CausticStrength));
            Shader.SetGlobalFloat("_UnderwaterCausticScale", Mathf.Max(0.01f, CausticScale));
            Shader.SetGlobalFloat("_UnderwaterCausticFadeStart", causticFadeStart);
            Shader.SetGlobalFloat("_UnderwaterCausticFadeEnd", causticFadeEnd);
            Shader.SetGlobalFloat("_UnderwaterEffectWeight", depthFade);
            Shader.SetGlobalFloat("_UnderwaterCausticsEnabled", enabled);
        }

        private Vector3 GetSunColor()
        {
            Vector4 global = Shader.GetGlobalVector("_SunLightColor");
            Vector3 color = new Vector3(global.x, global.y, global.z);
            if (color.sqrMagnitude < 0.0001f)
            {
                color = Vector3.one;
            }

            return color;
        }

        private static Vector3 Refract(Vector3 incident, Vector3 normal, float eta)
        {
            float dot = Vector3.Dot(normal, incident);
            float k = 1f - (eta * eta * (1f - (dot * dot)));
            if (k < 0f)
            {
                return Vector3.zero;
            }

            return (incident * eta) - (normal * (eta * dot + Mathf.Sqrt(k)));
        }

        private void DisableEffects()
        {
            if (ribbonRenderer != null)
            {
                ribbonRenderer.enabled = false;
            }

            if (screenPass != null)
            {
                screenPass.enabled = false;
            }

            if (screenCompositePass != null)
            {
                screenCompositePass.enabled = false;
            }

            ribbonAnchorValid = false;
            ResetCausticGlobals();
        }

        private void ResetCausticGlobals()
        {
            Shader.SetGlobalMatrix("_UnderwaterWorldToBody", Matrix4x4.identity);
            Shader.SetGlobalVector("_UnderwaterBodyAnchor", Vector4.zero);
            Shader.SetGlobalVector("_UnderwaterRayCameraPos", Vector3.zero);
            Shader.SetGlobalMatrix("_UnderwaterRayWorldToBody", Matrix4x4.identity);
            Shader.SetGlobalVector("_UnderwaterRayBodyAnchor", Vector3.zero);
            Shader.SetGlobalVector("_UnderwaterCameraPos", Vector4.zero);
            Shader.SetGlobalVector("_UnderwaterUp", new Vector4(0f, 1f, 0f, 0f));
            Shader.SetGlobalVector("_UnderwaterSunDir", Vector4.zero);
            Shader.SetGlobalFloat("_UnderwaterEyeDepth", 0f);
            Shader.SetGlobalFloat("_UnderwaterTime", 0f);
            Shader.SetGlobalColor("_UnderwaterCausticColor", Color.black);
            Shader.SetGlobalFloat("_UnderwaterCausticStrength", 0f);
            Shader.SetGlobalFloat("_UnderwaterCausticScale", 0.7f);
            Shader.SetGlobalFloat("_UnderwaterCausticFadeStart", 3f);
            Shader.SetGlobalFloat("_UnderwaterCausticFadeEnd", 28f);
            Shader.SetGlobalFloat("_UnderwaterEffectWeight", 0f);
            Shader.SetGlobalFloat("_UnderwaterCausticsEnabled", 0f);
        }

        private void WarnMissingShader(string shaderName)
        {
            if (resourcesWarningLogged)
            {
                return;
            }

            resourcesWarningLogged = true;
            Debug.LogError("[UnderwaterSunRays] Shader not found: " + shaderName);
        }

        private void OnDisable()
        {
            DisableEffects();
        }

        private void OnDestroy()
        {
            DisableEffects();
            if (ribbonObject != null)
            {
                Destroy(ribbonObject);
            }

            if (ribbonMesh != null)
            {
                Destroy(ribbonMesh);
            }

            if (ribbonMaterial != null)
            {
                Destroy(ribbonMaterial);
            }

            if (screenMaterial != null)
            {
                Destroy(screenMaterial);
            }

            if (screenCompositeMaterial != null)
            {
                Destroy(screenCompositeMaterial);
            }

            if (screenVolume != null)
            {
                Destroy(screenVolume.gameObject);
            }
        }
    }
}
