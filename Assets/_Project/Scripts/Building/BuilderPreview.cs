using Galilego.Spacecraft;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Building
{
    /// <summary>
    /// 3D part preview for the inventory window: a dedicated camera renders the
    /// part into a RenderTexture, the object slowly spins, and sizes come from
    /// metrics (the visual is rebuilt when the size changes). The rig lives on
    /// layer 31 far from the world — the main camera never sees it (and the
    /// layer is cleared from its mask); rendering is manual, at most 30 fps.
    ///
    /// The camera is disabled (enabled = false): with a targetTexture it would
    /// otherwise draw a frame every tick — frames are requested only from Ensure().
    /// </summary>
    internal sealed class BuilderPreview
    {
        private const int Layer = 31;

        /// <summary>
        /// Layer of the ICON visual (separate from the preview layer): the icon
        /// camera masks only this layer, so the preview visual never appears in
        /// the icon frame.
        /// </summary>
        private const int IconLayer = 30;
        private const int Width = 512;
        private const int Height = 384;
        private const int IconSize = 256;
        private const float SpinDegreesPerSecond = 20f;
        private const float FieldOfView = 30f;
        private const float FrameMargin = 1.18f;
        private const float IconMargin = 1.16f;
        private const float IconSpinDegrees = 25f;
        private const float RenderInterval = 1f / 30f;

        /// <summary>Fixed exposure of the preview frame (EV100).</summary>
        private const float FixedExposureEv = -3f;

        /// <summary>Frame background (LINEAR) — matches the Viewport/Cell fill (sRGB 0.168, 0.176, 0.186) to avoid seams.</summary>
        private static readonly Color Background = new Color(0.0240f, 0.0261f, 0.0289f, 1f);

        private GameObject rig;
        private Transform spinner;
        private Camera camera;
        private RenderTexture target;
        private PartCatalog.PartVisual visual;
        private PartKind kind = (PartKind)(-1);
        private string engineId;
        private float radius = -1f;
        private float topRadius = -1f;
        private float height = -1f;
        private float spin;
        private float nextRender;
        private Light keyLight;
        private VolumeProfile previewProfile;
        private bool iconsReady;

        public Texture Texture => target;

        /// <summary>
        /// Update the preview: set up the rig, rebuild the visual when the
        /// part/size changes, spin it and (at most 30 Hz) render a frame.
        /// </summary>
        public void Ensure(PartKind nextKind, PartMetrics metrics, PartCatalog.EngineTemplate engine,
            Material tankSide, Material tankCap, Material separator)
        {
            EnsureRig();
            if (rig == null)
            {
                return;
            }

            string nextEngineId = engine != null && engine.Spec != null ? engine.Spec.Id : null;
            if (nextKind != kind
                || nextEngineId != engineId
                || !Mathf.Approximately(radius, metrics.Radius)
                || !Mathf.Approximately(topRadius, metrics.TopRadius)
                || !Mathf.Approximately(height, metrics.Height))
            {
                Rebuild(nextKind, metrics, engine, tankSide, tankCap, separator);
            }

            spin += Time.unscaledDeltaTime * SpinDegreesPerSecond;
            spinner.localRotation = Quaternion.Euler(0f, spin, 0f);

            if (Time.unscaledTime >= nextRender)
            {
                nextRender = Time.unscaledTime + RenderInterval;
                camera.Render();
            }
        }

        /// <summary>
        /// Render the part-kind "portraits" once: the same studio light and
        /// three-quarter angle as the preview, but on a TRANSPARENT background,
        /// so icons fit on any slot. Sizes are the defaults; hotbar slots with
        /// their own sizes re-render themselves (CaptureIcon). Each engine gets
        /// its own icon (by model Id). Called at startup.
        /// </summary>
        public void PrepareIcons(System.Collections.Generic.IReadOnlyList<PartCatalog.EngineTemplate> engines,
            Material tankSide, Material tankCap, Material separator)
        {
            if (iconsReady)
            {
                return;
            }

            EnsureRig();
            if (rig == null)
            {
                return;
            }

            iconsReady = true;

            PartCatalog.EngineTemplate firstEngine = engines != null && engines.Count > 0 ? engines[0] : null;
            if (engines != null)
            {
                for (int i = 0; i < engines.Count; i++)
                {
                    PartCatalog.EngineTemplate engine = engines[i];
                    StoreIcon(PartKind.Engine, engine.Spec != null ? engine.Spec.Id : null,
                        engine.Metrics, engine, tankSide, tankCap, separator);
                }
            }

            StoreIcon(PartKind.FuelTank, null,
                PartCatalog.Measure(PartKind.FuelTank, PartCatalog.TankDefaultRadius, PartCatalog.TankDefaultHeight, firstEngine),
                firstEngine, tankSide, tankCap, separator);
            StoreIcon(PartKind.Separator, null,
                PartCatalog.Measure(PartKind.Separator, PartCatalog.SeparatorDefaultRadius, 0f, firstEngine),
                firstEngine, tankSide, tankCap, separator);

            // Capsule: dimensions from the template (RegisterCapsule), icon from
            // the model render. Without a prefab, Measure returns empty metrics
            // and the icon stays procedural (tank default).
            StoreIcon(PartKind.Capsule, null,
                PartCatalog.Measure(PartKind.Capsule, 0f, 0f, firstEngine),
                null, tankSide, tankCap, separator);

            // RCS block: the saddle-mounted model (Models/Parts/RCS). Without the
            // model registered the root is empty and the icon stays procedural.
            StoreIcon(PartKind.RcsBlock, null,
                PartCatalog.Measure(PartKind.RcsBlock, 0f, 0f, firstEngine),
                null, tankSide, tankCap, separator);
        }

        /// <summary>
        /// Render a part icon for ARBITRARY sizes (a hotbar slot with its own
        /// sizes): same studio and angle as the default icons. The texture
        /// belongs to the caller, which destroys it. null if the rig/geometry
        /// is unavailable.
        /// </summary>
        public Texture2D CaptureIcon(PartKind kind, PartMetrics metrics, PartCatalog.EngineTemplate engine,
            Material tankSide, Material tankCap, Material separator)
        {
            EnsureRig();
            if (rig == null || metrics.Radius <= 0f)
            {
                return null;
            }

            // Transparent background and a temporary small target: the icon fits
            // a slot of any color, and the preview frame is unaffected. Camera
            // and spinner are restored afterwards: icon framing is its OWN (the
            // icon frame margin), while the preview re-frames only on part change.
            var hd = camera.GetComponent<HDAdditionalCameraData>();
            Color previousBackground = hd.backgroundColorHDR;
            Color transparent = new Color(Background.r, Background.g, Background.b, 0f);
            hd.backgroundColorHDR = transparent;
            camera.backgroundColor = transparent;

            var iconTarget = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32)
            {
                name = "BuilderIconRT",
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            iconTarget.Create();

            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = iconTarget;

            float previousSpin = spin;
            spinner.localRotation = Quaternion.Euler(0f, IconSpinDegrees, 0f);
            Transform cameraTransform = camera.transform;
            Vector3 cameraPosition = cameraTransform.position;
            Quaternion cameraRotation = cameraTransform.rotation;
            float cameraNear = camera.nearClipPlane;
            float cameraFar = camera.farClipPlane;

            // The icon is captured with the mask on its layer ONLY: the preview
            // visual (layer Layer) never enters the frame.
            int previousMask = camera.cullingMask;
            camera.cullingMask = 1 << IconLayer;

            Texture2D texture = CaptureIconPixels(kind, metrics, engine, tankSide, tankCap, separator);

            camera.cullingMask = previousMask;
            cameraTransform.SetPositionAndRotation(cameraPosition, cameraRotation);
            camera.nearClipPlane = cameraNear;
            camera.farClipPlane = cameraFar;
            spinner.localRotation = Quaternion.Euler(0f, previousSpin, 0f);
            camera.targetTexture = previousTarget;
            hd.backgroundColorHDR = previousBackground;
            camera.backgroundColor = previousBackground;
            iconTarget.Release();
            Object.Destroy(iconTarget);
            return texture;
        }

        private void StoreIcon(PartKind kind, string engineId, PartMetrics metrics, PartCatalog.EngineTemplate engine,
            Material tankSide, Material tankCap, Material separator)
        {
            Texture2D texture = CaptureIcon(kind, metrics, engine, tankSide, tankCap, separator);
            if (texture != null)
            {
                PartIcons.Set(kind, engineId, texture);
            }
        }

        private Texture2D CaptureIconPixels(PartKind kind, PartMetrics metrics, PartCatalog.EngineTemplate engine,
            Material tankSide, Material tankCap, Material separator)
        {
            PartCatalog.PartVisual visual = PartCatalog.CreateVisual(
                kind, metrics, engine, tankSide, tankCap, separator, spinner, withCollider: false);
            if (visual == null || visual.Root == null)
            {
                return null;
            }

            SetLayerRecursive(visual.Root, IconLayer);

            // The separator ring is flat: raise the camera slightly, otherwise
            // the frame shows a thin stripe instead of a ring (light stays sun-based).
            Vector3 direction = ViewDirection();
            if (kind == PartKind.Separator)
            {
                direction = (direction + (Vector3.up * 0.60f)).normalized;
            }

            Frame(metrics, direction, IconMargin);
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
            var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
            {
                name = "PartIcon_" + kind,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
            KeyOutBackground(texture);
            texture.Apply();
            RenderTexture.active = previous;

            // Destroy is deferred to the end of the frame, so deactivate now:
            // otherwise the next icon renders together with the previous one.
            visual.Root.SetActive(false);
            Object.Destroy(visual.Root);
            return texture;
        }

        /// <summary>
        /// The icon must sit on a cell of ANY colour (normal, hover, selected). HDRP does
        /// not always keep the alpha of the clear colour, so when the frame came out opaque
        /// the flat background (known from the corner pixel) is keyed out here.
        /// </summary>
        private static void KeyOutBackground(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            Color32 key = pixels[0];
            if (key.a < 250)
            {
                return;                              // already transparent — nothing to do
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                int d = Mathf.Max(Mathf.Abs(p.r - key.r), Mathf.Max(Mathf.Abs(p.g - key.g), Mathf.Abs(p.b - key.b)));
                if (d <= 2)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                if (d >= 10)
                {
                    continue;                        // clearly the part
                }

                // Soft edge: un-mix the background so there is no dark/light halo.
                float a = Mathf.Clamp01((d - 2f) / 8f);
                byte r = (byte)Mathf.Clamp(key.r + ((p.r - key.r) / a), 0f, 255f);
                byte g = (byte)Mathf.Clamp(key.g + ((p.g - key.g) / a), 0f, 255f);
                byte b = (byte)Mathf.Clamp(key.b + ((p.b - key.b) / a), 0f, 255f);
                pixels[i] = new Color32(r, g, b, (byte)Mathf.RoundToInt(a * 255f));
            }

            texture.SetPixels32(pixels);
        }

        /// <summary>Tear down the rig and texture (window closed / component disabled).</summary>
        public void Release()
        {
            if (rig != null)
            {
                Object.Destroy(rig);
            }

            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }

            if (previewProfile != null)
            {
                Object.Destroy(previewProfile);
            }

            rig = null;
            spinner = null;
            camera = null;
            target = null;
            visual = null;
            previewProfile = null;
            kind = (PartKind)(-1);
            engineId = null;
            radius = -1f;
            topRadius = -1f;
            height = -1f;
        }

        private void EnsureRig()
        {
            if (rig != null)
            {
                KeepHidden();
                return;
            }

            rig = new GameObject("BuilderPreview")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = Layer
            };
            // Far from the world: even if the camera mask is not cleared, the
            // object never enters the frame.
            rig.transform.position = new Vector3(0f, -10000f, 0f);

            var spinnerGo = new GameObject("Spinner") { layer = Layer };
            spinnerGo.transform.SetParent(rig.transform, false);
            spinner = spinnerGo.transform;

            var cameraGo = new GameObject("Camera") { layer = Layer };
            cameraGo.transform.SetParent(rig.transform, false);
            camera = cameraGo.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 200f;
            camera.allowHDR = true;
            camera.useOcclusionCulling = false;

            var hd = cameraGo.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Background;
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "BuilderPreviewRT",
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            camera.targetTexture = target;

            // Own exposure for the preview frame: scene auto-exposure would sink
            // the part into darkness against the bright background. Fixed EV
            // gives a stable "studio" look at any time of day.
            BuildExposureVolume(cameraGo);

            // Metal needs an environment for reflections, otherwise the tank is a
            // black silhouette: a cubic "studio" (top-to-bottom gradient) + Custom probe.
            BuildReflectionProbe();

            keyLight = FindKeyLight();
            KeepHidden();
        }

        /// <summary>Reflection Probe with a "studio" cubemap.</summary>
        private void BuildReflectionProbe()
        {
            var probeGo = new GameObject("PreviewReflection") { layer = Layer };
            probeGo.transform.SetParent(rig.transform, false);

            var cubemap = BuildStudioCubemap();

            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = cubemap;
            probe.size = new Vector3(400f, 400f, 400f);
            probe.intensity = 1f;
            probe.nearClipPlane = 0.1f;
            probe.farClipPlane = 500f;

            // HDRP takes the custom cubemap from its own data (hd.customTexture),
            // not from ReflectionProbe.customBakedTexture.
            var hd = probeGo.AddComponent<HDAdditionalReflectionData>();
            hd.mode = ProbeSettings.Mode.Custom;
            hd.customTexture = cubemap;
        }

        /// <summary>"Studio" cubemap: bright sky, dark bottom.</summary>
        private static Cubemap BuildStudioCubemap()
        {
            const int size = 32;
            var cubemap = new Cubemap(size, TextureFormat.RGBA32, false)
            {
                name = "BuilderPreviewStudio",
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color[size * size];
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float u = ((x + 0.5f) / size) * 2f - 1f;
                        float v = ((y + 0.5f) / size) * 2f - 1f;
                        Vector3 dir = CubemapDirection((CubemapFace)face, u, v);
                        float h = dir.y * 0.5f + 0.5f;              // 0 bottom, 1 top
                        float top = Mathf.Lerp(0.30f, 1.05f, h * h); // soft neutral gradient
                        pixels[(y * size) + x] = new Color(top, top, top, 1f);
                    }
                }

                cubemap.SetPixels(pixels, (CubemapFace)face);
            }

            cubemap.Apply();
            return cubemap;
        }

        /// <summary>Cubemap face pixel direction (Unity order).</summary>
        private static Vector3 CubemapDirection(CubemapFace face, float u, float v)
        {
            switch (face)
            {
                case CubemapFace.PositiveX: return new Vector3(1f, -v, -u).normalized;
                case CubemapFace.NegativeX: return new Vector3(-1f, -v, u).normalized;
                case CubemapFace.PositiveY: return new Vector3(u, 1f, v).normalized;
                case CubemapFace.NegativeY: return new Vector3(u, -1f, -v).normalized;
                case CubemapFace.PositiveZ: return new Vector3(u, -v, 1f).normalized;
                default: return new Vector3(-u, -v, -1f).normalized;
            }
        }

        /// <summary>
        /// Volume on the preview camera: Fixed Exposure only. The collider box
        /// makes the Volume non-global so it affects only this camera (the main
        /// camera is 10 km away, outside the box).
        /// </summary>
        private void BuildExposureVolume(GameObject cameraGo)
        {
            var volumeGo = new GameObject("PreviewExposure") { layer = Layer };
            volumeGo.transform.SetParent(cameraGo.transform, false);

            previewProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            previewProfile.name = "BuilderPreviewProfile";
            previewProfile.hideFlags = HideFlags.HideAndDontSave;

            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.priority = 1000f;
            volume.weight = 1f;
            volume.profile = previewProfile;

            var exposure = previewProfile.Add<Exposure>(true);
            exposure.mode.value = ExposureMode.Fixed;
            exposure.fixedExposure.value = FixedExposureEv;
            exposure.compensation.value = 0f;

            var box = volumeGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(4000f, 4000f, 4000f);
        }

        /// <summary>Brightest enabled directional light — the camera is positioned by it.</summary>
        private static Light FindKeyLight()
        {
            Light best = null;
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light.type != LightType.Directional || !light.isActiveAndEnabled)
                {
                    continue;
                }

                if (best == null || light.intensity > best.intensity)
                {
                    best = light;
                }
            }

            return best;
        }

        /// <summary>
        /// Preview camera direction: from the SUN side, otherwise the part is a
        /// dark silhouette (camera on the shadowed side). Slightly above, 3/4 view.
        /// </summary>
        private Vector3 ViewDirection()
        {
            if (keyLight == null)
            {
                return new Vector3(0.72f, 0.38f, 1f).normalized;
            }

            Vector3 toLight = -keyLight.transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toLight);
            if (side.sqrMagnitude < 1e-4f)
            {
                side = Vector3.right;
            }

            side.Normalize();
            return (toLight + (side * 0.55f) + (Vector3.up * 0.30f)).normalized;
        }

        /// <summary>Preview and icon layers must not be rendered by the main camera.</summary>
        private void KeepHidden()
        {
            Camera main = Camera.main;
            if (main == null)
            {
                return;
            }

            main.cullingMask &= ~(1 << Layer);
            main.cullingMask &= ~(1 << IconLayer);
        }

        private void Rebuild(PartKind nextKind, PartMetrics metrics, PartCatalog.EngineTemplate engine,
            Material tankSide, Material tankCap, Material separator)
        {
            kind = nextKind;
            engineId = engine != null && engine.Spec != null ? engine.Spec.Id : null;
            radius = metrics.Radius;
            topRadius = metrics.TopRadius;
            height = metrics.Height;

            if (visual != null && visual.Root != null)
            {
                visual.Root.SetActive(false);
                Object.Destroy(visual.Root);
            }

            visual = PartCatalog.CreateVisual(kind, metrics, engine, tankSide, tankCap, separator, spinner, withCollider: false);
            if (visual == null || visual.Root == null)
            {
                return;
            }

            SetLayerRecursive(visual.Root, Layer);
            Frame(metrics);
        }

        /// <summary>Frame the camera on the part's dimensions (height and diameter).</summary>
        private void Frame(PartMetrics metrics)
        {
            Frame(metrics, ViewDirection(), FrameMargin);
        }

        /// <summary>Framing for a given camera direction and frame margin.</summary>
        private void Frame(PartMetrics metrics, Vector3 direction, float margin)
        {
            float biggest = Mathf.Max(metrics.Height, metrics.Radius * 2f);
            float half = Mathf.Max(0.05f, biggest * 0.5f);
            float distance = (half / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad)) * margin;

            Vector3 targetPoint = spinner.position + (Vector3.up * metrics.Center);
            camera.transform.position = targetPoint + (direction * distance);
            camera.transform.LookAt(targetPoint, Vector3.up);
            camera.nearClipPlane = Mathf.Max(0.05f, distance - (half * 2.5f));
            camera.farClipPlane = distance + (half * 4f) + 5f;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
            }
        }
    }
}
