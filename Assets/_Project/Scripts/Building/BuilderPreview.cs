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
        /// Layer of the preview hangar (floor, beams, lamps). Separate from the part
        /// layer: the icon pass renders the part only, and the reflection probe renders
        /// the hangar only, so neither one leaks into the other.
        /// </summary>
        private const int HangarLayer = 29;

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

        /// <summary>Hangar footprint: half width of the room, metres.</summary>
        private const float RoomHalf = 13f;

        /// <summary>Hangar height, metres.</summary>
        private const float RoomHeight = 12f;

        /// <summary>
        /// Fixed exposure of the preview frame (EV100). Calibrated for the scene's
        /// physical sun (Sol at 0.368 AU ≈ 7.6× the reference irradiance): the parts
        /// read the same as before the star became physical, without clipping.
        /// The value is only meaningful because the rig's local volume is actually
        /// applied (see the camera volume layer mask in EnsureRig).
        /// </summary>
        private const float FixedExposureEv = 0f;

        /// <summary>Frame background (LINEAR) — matches the Viewport/Cell fill (sRGB 0.168, 0.176, 0.186) to avoid seams.</summary>
        private static readonly Color Background = new Color(0.0240f, 0.0261f, 0.0289f, 1f);

        private GameObject rig;
        private GameObject hangar;
        private Cubemap probeCubemap;
        private bool hangarCaptured;
        private int rigBuiltFrame;
        private readonly System.Collections.Generic.List<Material> hangarMaterials = new System.Collections.Generic.List<Material>();
        private Transform spinner;
        private Camera camera;
        private RenderTexture target;
        private PartCatalog.PartVisual visual;
        private PartKind kind = (PartKind)(-1);
        private string engineId;
        private float radius = -1f;
        private float topRadius = -1f;
        private float height = -1f;
        private bool hullRingSealed;
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
            Material tankSide, Material tankCap, Material separator, bool hullRingSealed = false)
        {
            EnsureRig();
            if (rig == null)
            {
                return;
            }

            if (!hangarCaptured && Time.frameCount > rigBuiltFrame)
            {
                // A full frame has passed since the rig was built, so the hangar
                // lights are registered and the capture is not black.
                CaptureHangar();
                hangarCaptured = true;
            }

            string nextEngineId = engine != null && engine.Spec != null ? engine.Spec.Id : null;
            if (nextKind != kind
                || nextEngineId != engineId
                || hullRingSealed != this.hullRingSealed
                || !Mathf.Approximately(radius, metrics.Radius)
                || !Mathf.Approximately(topRadius, metrics.TopRadius)
                || !Mathf.Approximately(height, metrics.Height))
            {
                Rebuild(nextKind, metrics, engine, tankSide, tankCap, separator, hullRingSealed);
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

            // The HDRP exposure debug overlay is a global full-screen pass; if the
            // Rendering Debugger left it on, every icon/preview texture bakes it in.
            // Turn it off for the game session (the debugger can re-enable it).
            SetExposureDebugMode(ExposureDebugMode.None);

            EnsureRig();
            if (rig == null)
            {
                return;
            }

            // Warm the HDRP pipeline with the overlay off: the debug display state is
            // latched at the start of a camera render, so without this warm-up the very
            // first icon still bakes the exposure overlay (it did for the RS-25).
            camera.Render();

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

            // Fairing: plate with a demo shell (CaptureIconPixels swaps the metrics).
            StoreIcon(PartKind.Fairing, null,
                PartCatalog.Measure(PartKind.Fairing, PartCatalog.SeparatorDefaultRadius, 0f, firstEngine),
                null, tankSide, tankCap, separator);

            // Hull ring: the M-05 double-wall segment, open cavity.
            StoreIcon(PartKind.HullRing, null,
                PartCatalog.MeasureHullRing(
                    PartCatalog.HullRingDefaultRadius, PartCatalog.HullRingDefaultHeight, false),
                null, tankSide, tankCap, separator);
        }

        /// <summary>
        /// HDRP's Rendering Debugger exposure overlay is a global full-screen pass. It
        /// lands in the icon/preview render textures and made the inventory/hotbar icons
        /// look empty. Access the live pipeline debug settings; false — nothing to do.
        /// </summary>
        private static bool TryGetExposureDebugMode(out ExposureDebugMode mode)
        {
            mode = ExposureDebugMode.None;
            HDRenderPipeline pipeline = RenderPipelineManager.currentPipeline as HDRenderPipeline;
            DebugDisplaySettings settings = pipeline != null ? pipeline.debugDisplaySettings : null;
            if (settings == null || settings.data == null || settings.data.lightingDebugSettings == null)
            {
                return false;
            }

            mode = settings.data.lightingDebugSettings.exposureDebugMode;
            return true;
        }

        private static void SetExposureDebugMode(ExposureDebugMode mode)
        {
            HDRenderPipeline pipeline = RenderPipelineManager.currentPipeline as HDRenderPipeline;
            DebugDisplaySettings settings = pipeline != null ? pipeline.debugDisplaySettings : null;
            if (settings == null || settings.data == null || settings.data.lightingDebugSettings == null)
            {
                return;
            }

            if (settings.data.lightingDebugSettings.exposureDebugMode != mode)
            {
                settings.data.lightingDebugSettings.exposureDebugMode = mode;
            }
        }

        /// <summary>
        /// Render a part icon for ARBITRARY sizes (a hotbar slot with its own
        /// sizes): same studio and angle as the default icons. The texture
        /// belongs to the caller, which destroys it. null if the rig/geometry
        /// is unavailable.
        /// </summary>
        public Texture2D CaptureIcon(PartKind kind, PartMetrics metrics, PartCatalog.EngineTemplate engine,
            Material tankSide, Material tankCap, Material separator, bool hullRingSealed = false)
        {
            EnsureRig();
            if (rig == null || metrics.Radius <= 0f)
            {
                return null;
            }

            // Temporary small target and the icon's own framing: the preview frame is
            // unaffected (camera/spinner restored afterwards).
            // The icon frame used to render on a transparent clear colour, but HDRP's
            // opaque pass writes alpha 0, so the whole frame came out transparent and
            // the part vanished from the slot. Render against the known studio
            // background and key it out below; the part pixels are forced opaque.
            HDAdditionalCameraData hd = camera.GetComponent<HDAdditionalCameraData>();
            Color previousBackground = hd.backgroundColorHDR;
            Color iconBackground = Background;
            hd.backgroundColorHDR = iconBackground;
            camera.backgroundColor = iconBackground;

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

            // The exposure debug overlay is global; keep it out of this capture too
            // (the user may re-enable the Rendering Debugger after startup).
            bool hadExposureDebug = TryGetExposureDebugMode(out ExposureDebugMode previousExposureDebug);
            if (hadExposureDebug && previousExposureDebug != ExposureDebugMode.None)
            {
                SetExposureDebugMode(ExposureDebugMode.None);
            }

            Texture2D texture = CaptureIconPixels(kind, metrics, engine, tankSide, tankCap, separator, hullRingSealed);

            if (hadExposureDebug && previousExposureDebug != ExposureDebugMode.None)
            {
                SetExposureDebugMode(previousExposureDebug);
            }

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
            Material tankSide, Material tankCap, Material separator, bool hullRingSealed = false)
        {
            if (kind == PartKind.Fairing)
            {
                metrics = PartCatalog.MeasureFairingIcon(metrics.Radius);   // plate + demo shell
            }

            PartCatalog.PartVisual visual = PartCatalog.CreateVisual(
                kind, metrics, engine, tankSide, tankCap, separator, spinner,
                withCollider: false, hullRingSealed: hullRingSealed);
            if (visual == null || visual.Root == null)
            {
                return null;
            }

            SetLayerRecursive(visual.Root, IconLayer);

            // The separator and the hull ring are flat: raise the camera slightly,
            // otherwise the frame shows a thin stripe instead of a ring.
            Vector3 direction = ViewDirection();
            if (kind == PartKind.Separator)
            {
                direction = (direction + (Vector3.up * 0.60f)).normalized;
            }
            else if (kind == PartKind.HullRing)
            {
                direction = (direction + (Vector3.up * 0.45f)).normalized;
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
            bool transparentBackground = key.a < 250;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                int d = Mathf.Max(Mathf.Abs(p.r - key.r), Mathf.Max(Mathf.Abs(p.g - key.g), Mathf.Abs(p.b - key.b)));
                if (d <= 2)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                if (transparentBackground)
                {
                    // HDRP wrote alpha 0 for the whole frame, including the opaque part.
                    // The background RGB is known, so the pixels that differ from it are
                    // the part: make them opaque.
                    pixels[i] = new Color32(p.r, p.g, p.b, 255);
                    continue;
                }

                if (d >= 10)
                {
                    // Clear part pixel: HDRP's opaque alpha is 0, force the icon visible.
                    pixels[i] = new Color32(p.r, p.g, p.b, 255);
                    continue;
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

            for (int i = 0; i < hangarMaterials.Count; i++)
            {
                if (hangarMaterials[i] != null)
                {
                    Object.Destroy(hangarMaterials[i]);
                }
            }

            hangarMaterials.Clear();
            hangar = null;

            if (probeCubemap != null)
            {
                Object.Destroy(probeCubemap);
                probeCubemap = null;
            }

            hangarCaptured = false;

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
            rigBuiltFrame = Time.frameCount;

            var spinnerGo = new GameObject("Spinner") { layer = Layer };
            spinnerGo.transform.SetParent(rig.transform, false);
            spinner = spinnerGo.transform;

            var cameraGo = new GameObject("Camera") { layer = Layer };
            cameraGo.transform.SetParent(rig.transform, false);
            camera = cameraGo.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = (1 << Layer) | (1 << HangarLayer);
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

            // HDRP only evaluates a local (non-global) Volume when its GameObject
            // is in the camera's volume layer mask; the default mask is layer 0
            // alone, while the rig lives on layer 31. Without this the PreviewExposure
            // volume (fixed exposure, bloom off) was silently ignored and the icons
            // rendered with the scene's automatic exposure and bloom.
            hd.volumeLayerMask = (1 << 0) | (1 << Layer);

            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "BuilderPreviewRT",
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            camera.targetTexture = target;

            // Own exposure for the preview frame: scene auto-exposure would sink
            // the part into darkness or clip it against the star brightness. The
            // fixed EV gives a stable "studio" look at any time of day, and the same
            // volume also keeps the scene bloom out of the frame.
            BuildExposureVolume(cameraGo);

            // A real hangar around the part and a realtime probe that captures it:
            // the mirror finish then reflects actual geometry and actual light.
            BuildHangar();
            BuildReflectionProbe();

            keyLight = FindKeyLight();
            KeepHidden();
        }

        /// <summary>
        /// Captures the hangar into a cubemap and feeds it to the probe.
        /// The capture is a real render of the real geometry (Camera.RenderToCubemap
        /// works with HDRP), so the mirror wall reflects the beams, the crates and
        /// the lamps as they actually are, under the actual lights. Everything is
        /// static, so one capture is enough; dragging a beam in the code re-bakes it.
        /// </summary>
        private void BuildReflectionProbe()
        {
            var probeGo = new GameObject("PreviewReflection") { layer = Layer };
            probeGo.transform.SetParent(rig.transform, false);

            // The texture is filled on the next frame (see Ensure): HDRP only picks
            // up the hangar lights after a full frame, and a capture taken in the
            // same frame as their creation comes out black.
            probeCubemap = new Cubemap(256, TextureFormat.RGBA32, true)
            {
                name = "PreviewHangarCubemap",
                hideFlags = HideFlags.HideAndDontSave
            };

            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = probeCubemap;
            // The influence box must swallow the camera too: HDRP fades a probe out
            // as the view leaves its volume, and the preview camera sits high and
            // wide to frame the part.
            probe.size = new Vector3(RoomHalf * 3f, RoomHeight * 2.5f, RoomHalf * 3f);
            probe.intensity = 1f;
            probe.nearClipPlane = 0.1f;
            probe.farClipPlane = 200f;

            var hd = probeGo.AddComponent<HDAdditionalReflectionData>();
            hd.mode = ProbeSettings.Mode.Custom;
            hd.customTexture = probeCubemap;
        }

        /// <summary>
        /// Bakes the hangar into the probe texture. A real render of the real
        /// geometry (Camera.RenderToCubemap works with HDRP), so the mirror wall
        /// reflects the beams, the crates and the lamps as they are, under the real
        /// lights of the rig. Static scene, one capture.
        /// </summary>
        private void CaptureHangar()
        {
            var camGo = new GameObject("~HangarCapture") { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 2.4f, 0f);

            var capture = camGo.AddComponent<Camera>();
            capture.cullingMask = 1 << HangarLayer;
            capture.fieldOfView = 90f;
            capture.nearClipPlane = 0.1f;
            capture.farClipPlane = 200f;
            capture.allowHDR = true;

            // Same exposure volume as the preview frame, otherwise the capture is
            // auto-exposed from an empty history and comes out black.
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.volumeLayerMask = (1 << 0) | (1 << Layer);
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Background;

            capture.RenderToCubemap(probeCubemap);
            Object.DestroyImmediate(camGo);
        }

        /// <summary>
        /// The hangar the part stands in: floor, walls, scaffolding beams, yellow
        /// safety trim, ceiling light strips and wall lamps. Real geometry with real
        /// lights on its own layer — that is what makes the reflection live: move a
        /// beam and the highlight on the tank moves with it. The icon pass renders
        /// only the part layer, so the hangar never lands in a slot thumbnail.
        /// </summary>
        private void BuildHangar()
        {
            hangar = new GameObject("PreviewHangar")
            {
                layer = HangarLayer,
                hideFlags = HideFlags.HideAndDontSave
            };
            hangar.transform.SetParent(rig.transform, false);

            Material floor = HangarMaterial("HangarFloor", new Color(0.26f, 0.265f, 0.27f), 0.10f, 0.55f);
            Material wall = HangarMaterial("HangarWall", new Color(0.55f, 0.56f, 0.57f), 0.15f, 0.55f);
            Material beam = HangarMaterial("HangarBeam", new Color(0.20f, 0.205f, 0.21f), 0.60f, 0.60f);
            Material trim = HangarMaterial("HangarTrim", new Color(0.72f, 0.46f, 0.03f), 0.10f, 0.45f);
            Material lamp = HangarLampMaterial("HangarLamp", new Color(7f, 6.8f, 6.3f));

            const float t = 0.5f;

            // Shell.
            Box("Floor", new Vector3(0f, -t * 0.5f, 0f), new Vector3(RoomHalf * 2f, t, RoomHalf * 2f), floor);
            Box("Ceiling", new Vector3(0f, RoomHeight + (t * 0.5f), 0f), new Vector3(RoomHalf * 2f, t, RoomHalf * 2f), floor);
            Box("WallNorth", new Vector3(0f, RoomHeight * 0.5f, RoomHalf), new Vector3(RoomHalf * 2f, RoomHeight, t), wall);
            Box("WallSouth", new Vector3(0f, RoomHeight * 0.5f, -RoomHalf), new Vector3(RoomHalf * 2f, RoomHeight, t), wall);
            Box("WallEast", new Vector3(RoomHalf, RoomHeight * 0.5f, 0f), new Vector3(t, RoomHeight, RoomHalf * 2f), wall);
            Box("WallWest", new Vector3(-RoomHalf, RoomHeight * 0.5f, 0f), new Vector3(t, RoomHeight, RoomHalf * 2f), wall);

            // Scaffolding: two rings of beams around the room.
            float[] ringHeights = { 3.1f, 8.9f };
            for (int i = 0; i < ringHeights.Length; i++)
            {
                float y = ringHeights[i];
                Box("BeamNorth" + i, new Vector3(0f, y, RoomHalf - 0.55f), new Vector3(RoomHalf * 2f, 0.36f, 0.36f), beam);
                Box("BeamSouth" + i, new Vector3(0f, y, -RoomHalf + 0.55f), new Vector3(RoomHalf * 2f, 0.36f, 0.36f), beam);
                Box("BeamEast" + i, new Vector3(RoomHalf - 0.55f, y, 0f), new Vector3(0.36f, 0.36f, RoomHalf * 2f), beam);
                Box("BeamWest" + i, new Vector3(-RoomHalf + 0.55f, y, 0f), new Vector3(0.36f, 0.36f, RoomHalf * 2f), beam);
            }

            // Yellow safety rail under the lower beams (the warm accents of the
            // reference hangar; they also land on the tank as warm horizontal glints).
            float railY = ringHeights[0] - 0.42f;
            Box("RailNorth", new Vector3(0f, railY, RoomHalf - 0.55f), new Vector3(RoomHalf * 2f, 0.12f, 0.12f), trim);
            Box("RailSouth", new Vector3(0f, railY, -RoomHalf + 0.55f), new Vector3(RoomHalf * 2f, 0.12f, 0.12f), trim);
            Box("RailEast", new Vector3(RoomHalf - 0.55f, railY, 0f), new Vector3(0.12f, 0.12f, RoomHalf * 2f), trim);
            Box("RailWest", new Vector3(-RoomHalf + 0.55f, railY, 0f), new Vector3(0.12f, 0.12f, RoomHalf * 2f), trim);

            // Vertical columns: the strong verticals the chrome barrel picks up.
            for (float x = -12f; x <= 12f; x += 6f)
            {
                Box("ColumnN" + x, new Vector3(x, RoomHeight * 0.5f, RoomHalf - 0.55f), new Vector3(0.42f, RoomHeight, 0.42f), beam);
                Box("ColumnS" + x, new Vector3(x, RoomHeight * 0.5f, -RoomHalf + 0.55f), new Vector3(0.42f, RoomHeight, 0.42f), beam);
            }

            for (float z = -12f; z <= 12f; z += 8f)
            {
                Box("ColumnE" + z, new Vector3(RoomHalf - 0.55f, RoomHeight * 0.5f, z), new Vector3(0.42f, RoomHeight, 0.42f), beam);
                Box("ColumnW" + z, new Vector3(-RoomHalf + 0.55f, RoomHeight * 0.5f, z), new Vector3(0.42f, RoomHeight, 0.42f), beam);
            }

            // Ceiling light strips: the long bright bars that read as chrome.
            for (int i = 0; i < 4; i++)
            {
                float z = -9.6f + (i * 6.4f);
                Box("CeilingStrip" + i, new Vector3(0f, RoomHeight - 0.3f, z), new Vector3(RoomHalf * 1.75f, 0.2f, 1.6f), lamp);
            }

            // Low wall lamps.
            for (int i = 0; i < 3; i++)
            {
                float x = -10f + (i * 10f);
                Box("WallLampN" + i, new Vector3(x, 6.4f, RoomHalf - 0.45f), new Vector3(2.2f, 1.1f, 0.16f), lamp);
            }

            // A few crates for depth, so the reflection is not a flat tiled box.
            Box("CrateA", new Vector3(-11f, 0.9f, 11f), new Vector3(2.4f, 1.8f, 2.4f), beam);
            Box("CrateB", new Vector3(11.5f, 1.2f, 12f), new Vector3(3f, 2.4f, 2.2f), beam);
            Box("CrateC", new Vector3(-12.5f, 1.6f, -10f), new Vector3(2.2f, 3.2f, 2.2f), trim);

            // Practical lights: the sun is the key, these fill the interior. The
            // capture below happens once, so these values decide how bright the
            // hangar is inside every reflection of the part.
            PointLight("HangarLightA", new Vector3(-5f, RoomHeight - 2.4f, -5f), 9000f, 40f, new Color(1f, 0.96f, 0.90f));
            PointLight("HangarLightB", new Vector3(5f, RoomHeight - 2.4f, 0f), 9000f, 40f, new Color(1f, 0.96f, 0.90f));
            PointLight("HangarLightC", new Vector3(-2f, RoomHeight - 2.4f, 6f), 7000f, 40f, new Color(1f, 0.93f, 0.84f));

            // Real lights: ceiling pendants over the floor (the sun is the key, these
            // are the hangar's practicals — they light the interior the way the lamp
            // banks of the reference hangar do).
        }

        /// <summary>One solid box of the hangar: no collider, no shadow casting (the
        /// sun must reach the interior, otherwise the diorama reads as a black box).</summary>
        private void Box(string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = HangarLayer;
            go.transform.SetParent(hangar.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Object.Destroy(collider);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        private Material HangarMaterial(string name, Color baseColor, float metallic, float smoothness)
        {
            var material = new Material(Shader.Find("HDRP/Lit"))
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            hangarMaterials.Add(material);
            return material;
        }

        /// <summary>Emissive lamp surface: a Lit material with an HDR emissive colour.</summary>
        private Material HangarLampMaterial(string name, Color emission)
        {
            var material = new Material(Shader.Find("HDRP/Lit"))
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetColor("_BaseColor", Color.black);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.2f);
            material.SetColor("_EmissiveColor", emission);
            material.SetFloat("_EmissiveIntensity", 1f);
            material.SetFloat("_EmissiveExposureWeight", 0f);
            hangarMaterials.Add(material);
            return material;
        }

        /// <summary>
        /// One hangar practical. They live on the hangar layer, which every other
        /// camera has masked out, so the lights exist only for the preview.
        /// </summary>
        private void PointLight(string name, Vector3 position, float lumens, float range, Color color)
        {
            var go = new GameObject(name) { layer = HangarLayer };
            go.transform.SetParent(hangar.transform, false);
            go.transform.localPosition = position;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.shadows = LightShadows.None;
            light.lightUnit = LightUnit.Lumen;
            light.intensity = lumens;
        }

        /// <summary>
        /// Volume on the preview camera: fixed exposure and bloom off. The collider
        /// box makes the Volume non-global so it affects only this camera (the main
        /// camera is 10 km away, outside the box); the camera's volume layer mask
        /// includes the rig layer so HDRP actually applies it (see EnsureRig).
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

            // Icons are UI thumbnails: the scene Bloom (Star Volume, threshold 1)
            // turned every specular highlight of the part into a halo baked into
            // the icon texture. The background key then kept the halo as "part",
            // so the slots glowed. No bloom in the preview/icon frame.
            var bloom = previewProfile.Add<Bloom>(true);
            bloom.active = true;
            bloom.intensity.Override(0f);

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
        /// dark silhouette (camera on the shadowed side). The sun's azimuth is kept,
        /// its elevation is fixed: in play the star moves and a high sun would put
        /// the camera overhead, which reads badly against the hangar walls.
        /// </summary>
        private Vector3 ViewDirection()
        {
            if (keyLight == null)
            {
                return new Vector3(0.72f, 0.34f, 1f).normalized;
            }

            Vector3 toLight = -keyLight.transform.forward;
            Vector3 horizontal = new Vector3(toLight.x, 0f, toLight.z);
            if (horizontal.sqrMagnitude < 1e-4f)
            {
                horizontal = Vector3.forward;
            }

            horizontal.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, horizontal);
            side.Normalize();
            return (horizontal + (side * 0.55f) + (Vector3.up * 0.34f)).normalized;
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
            main.cullingMask &= ~(1 << HangarLayer);
        }

        private void Rebuild(PartKind nextKind, PartMetrics metrics, PartCatalog.EngineTemplate engine,
            Material tankSide, Material tankCap, Material separator, bool hullRingSealed = false)
        {
            if (nextKind == PartKind.Fairing)
            {
                metrics = PartCatalog.MeasureFairingIcon(metrics.Radius);   // plate + demo shell
            }

            kind = nextKind;
            engineId = engine != null && engine.Spec != null ? engine.Spec.Id : null;
            radius = metrics.Radius;
            topRadius = metrics.TopRadius;
            height = metrics.Height;
            this.hullRingSealed = hullRingSealed;

            if (visual != null && visual.Root != null)
            {
                visual.Root.SetActive(false);
                Object.Destroy(visual.Root);
            }

            visual = PartCatalog.CreateVisual(kind, metrics, engine, tankSide, tankCap, separator, spinner,
                withCollider: false, hullRingSealed: hullRingSealed);
            if (visual == null || visual.Root == null)
            {
                return;
            }

            SetLayerRecursive(visual.Root, Layer);

            // The preview may not throw a shadow into the hangar: the shadow of a
            // 6 m tank across the floor reads as a blob, and the hangar lighting is
            // soft anyway.
            Renderer[] previewRenderers = visual.Root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < previewRenderers.Length; i++)
            {
                previewRenderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }

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
            camera.farClipPlane = distance + 80f;
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
