using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// Underwater fade of the VIEW: spectral absorption by the Beer-Lambert law,
    /// as a full-screen HDRP pass.
    ///
    /// How it differs from UnderwaterEffect: that one sets tone and exposure
    /// (ColorAdjustments) and swaps the camera background, but adds NO
    /// distance-based fade — its HDRP Fog is off via the enabled flag, so the whole
    /// override is ignored. Without it, kilometers are visible underwater with sharp
    /// silhouettes.
    ///
    /// Why a custom pass instead of enabling that Fog: the built-in Fog is
    /// monochrome (color + meanFreePath) and does not distinguish channels, so
    /// distance turns grey rather than blue-green. Spectral sigma is what makes the
    /// latter happen.
    ///
    /// Where it sits in the frame: BeforePostProcess, not AfterPostProcess.
    /// AfterPostProcess the HDRP depth buffer is already invalid (which is why
    /// UnderwaterSunRays sets targetDepthBuffer = None), and depth must be read here.
    /// BeforePostProcess runs after opaque geometry, sky and transparents, before
    /// tonemapping and UI.
    ///
    /// Precision: the camera height above sea level and k = sea^2 - |C|^2 are
    /// computed in double here and passed ready-made. In the shader length(C) - sea
    /// at R = 6 371 000 m gives ±0.5 m of noise (float32 ULP), and "is the camera
    /// underwater" is decided from that value — this would flicker at the boundary.
    /// </summary>
    [DefaultExecutionOrder(-38)]
    public sealed class UnderwaterViewFog : MonoBehaviour
    {
        [Header("Источник состояния")]
        [Tooltip("UnderwaterEffect сцены: он уже считает глубину, локальную вверх и направление на солнце. Пусто — найдётся сам.")]
        public UnderwaterEffect Effect;

        [Tooltip("Камера. Пусто — Camera.main.")]
        public Camera EffectCamera;

        [Header("Поглощение")]
        [Tooltip("Поглощение по каналам R,G,B в 1/м. Красный умирает первым, синий держится дальше — это и даёт сине-зелёный переход. 0.40/0.12/0.08 — «океан по умолчанию», видимость по зелёному ~25 м.")]
        public Vector3 SigmaPerMeter = new Vector3(0.40f, 0.12f, 0.08f);

        [Header("Вуаль")]
        [Tooltip("Цвет рассеянного света воды (linear), к которому сходится всё вдали. Взято из _UnderwaterColor проекте, чтобы вид из воды и вид сверху совпадали.")]
        public Color WaterColor = new Color(0.02f, 0.16f, 0.30f);

        [Tooltip("Уровень рассеянного света, не зависящий от направления на солнце: подсветка от неба и многократное рассеяние. Внимание: это УРОВЕНЬ СВЕТА, а не доля альбедо. При 0.08 вуаль выходила ~0.025 линейного и вся картинка умещалась в единицы 8-битных кодов - картинка шла полосами вместо градиента (посчитано в T140).")]
        [Range(0f, 2f)]
        public float Ambient = 0.55f;

        [Tooltip("Нижняя доля солнечного света, когда солнце ровно с горизонтом (N·L = 0). Рассеяние в воде почти изотропно, поэтому ламбертовский cos без смягчения съедал яркость и уводил вуаль в чёрное.")]
        [Range(0f, 1f)]
        public float SunShapeFloor = 0.35f;

        [Tooltip("Сила дизеринга в 8-битных кодах. Подводный градиент пологий, и без дизеринга остаются полосы шириной в десятки строк. 0 - выключить.")]
        [Range(0f, 2f)]
        public float DitherAmount = 1f;

        [Tooltip("Насколько солнечный свет гаснет с глубины камеры. 1 = тот же закон, что и у горизонтального пути.")]
        [Range(0f, 3f)]
        public float SunPenetration = 1f;

[Tooltip("Насколько темнеет при взгляде вниз (меньше света от зенита).")]
        [Range(0f, 1f)]
        public float DownwardDarkening = 0.6f;

        [Header("Затемнение с глубиной")]
        [Tooltip("С какой долей sigma гаснет РАССЕЯННЫЙ свет с глубиной камеры. 0 = не гаснет (старое поведение), 0.5 = плавно до чёрного к 100–150 м, 1 = быстро.")]
        [Range(0f, 1f)]
        public float AmbientDecay = 0.5f;

        public enum CeilingMode { Off = 0, Auto = 1, Always = 2 }

        [Tooltip("Потолок воды (окно Снелла) из туманного прохода. Auto — только если меш воды не нарисовал поверхность. Always — всегда (для проверки). Off — выключено.")]
        public CeilingMode Ceiling = CeilingMode.Off;

        [Header("Переход через поверхность")]
        [Tooltip("Ширина плавного перехода по высоте камеры (м). Строго НЕ ноль: при if-ветке мигает на границе, здесь идёт smoothstep.")]
        [Min(0.02f)]
        public float TransitionMeters = 0.8f;

        public bool Enabled = true;

        /// <summary>
        /// Debug output of a single term straight into the frame, without fade.
        /// Useful when the image "comes in layers": it shows WHICH term is banding —
        /// depth, path in water, transmittance or the frame capture itself.
        /// 0 = off (normal view).
        /// 1 = depth (smooth transition), 2 = path in water pathIn/50 m (red),
        /// 3 = transmittance T, 4 = eyeDepth/1000 m, 5 = exit tExit/5000 m,
/// 6 = uv.x (framing check), 7 = source scene color,
/// 8 = ceiling mask: R - ceiling is drawn in, G - water mesh is present
/// at depth, B - underwater.
        /// </summary>
        [Range(0, 8)]
        public int DebugMode = 0;

        private CustomPassVolume volume;
        private FullScreenCustomPass fogPass;
        private FullScreenCustomPass compositePass;
        private Material fogMaterial;
        private Material compositeMaterial;
        private Camera capturedCamera;

        private static readonly int SigmaId = Shader.PropertyToID("_UwSigma");
        private static readonly int WaterColorId = Shader.PropertyToID("_UwWaterColor");
        private static readonly int AmbientId = Shader.PropertyToID("_UwAmbient");
        private static readonly int SunPenId = Shader.PropertyToID("_UwSunPenetration");
        private static readonly int SunShapeFloorId = Shader.PropertyToID("_UwSunShapeFloor");
        private static readonly int DitherAmountId = Shader.PropertyToID("_UwDitherAmount");
        private static readonly int TransitionId = Shader.PropertyToID("_UwTransitionMeters");
        private static readonly int DownwardId = Shader.PropertyToID("_UwDownwardDarkening");
        private static readonly int AmbientDecayId = Shader.PropertyToID("_UwAmbientDecay");
        private static readonly int CeilingModeId = Shader.PropertyToID("_UwCeilingMode");
        private static readonly int CamAltId = Shader.PropertyToID("_UwCamAltitude");
        private static readonly int SeaKId = Shader.PropertyToID("_UwSeaK");
        private static readonly int RadiusId = Shader.PropertyToID("_UwRadius");
        private static readonly int UpId = Shader.PropertyToID("_UwUp");
        private static readonly int SunDirId = Shader.PropertyToID("_UwSunDir");
        private static readonly int CamRightId = Shader.PropertyToID("_UwCamRight");
        private static readonly int CamUpId = Shader.PropertyToID("_UwCamUp");
        private static readonly int CamFwdId = Shader.PropertyToID("_UwCamFwd");
        private static readonly int TanXId = Shader.PropertyToID("_UwTanHalfFovX");
        private static readonly int TanYId = Shader.PropertyToID("_UwTanHalfFovY");
        private static readonly int DebugModeId = Shader.PropertyToID("_UwDebugMode");

        private void Awake()
        {
            if (Effect == null)
            {
                Effect = FindAnyObjectByType<UnderwaterEffect>();
            }
        }

        private void LateUpdate()
        {
            Camera camera = EffectCamera != null ? EffectCamera : Camera.main;
            if (camera == null)
            {
                return;
            }

            if (capturedCamera != camera)
            {
                capturedCamera = camera;
                DisposePasses();
            }

            if (!Enabled || Effect == null || !Effect.IsUnderwater || Effect.Runner == null)
            {
                SetVolumeActive(false);
                return;
            }

            OrbitingBody body = Effect.Runner.DominantBody;
            if (body == null)
            {
                SetVolumeActive(false);
                return;
            }

            if (!EnsurePasses(camera))
            {
                return;
            }

            SetVolumeActive(true);
            UploadGlobals(camera, body);
        }

        private void UploadGlobals(Camera camera, OrbitingBody body)
        {
            // Camera position in absolute (astro) coordinates — same as
            // UnderwaterEffect computes: FloatingOrigin.Anchor plus the camera's
            // local position.
            Vector3d observer = FloatingOrigin.Anchor
                + AstroFrame.ToAstro(camera.transform.position);
            body.EvaluateWorldState(Effect.Runner.TimeSeconds, out Vector3d bodyPosition, out _);

            double seaRadius = body.Radius + SeaLevelMetersOf(body);
            double r = (observer - bodyPosition).Magnitude;

            // Everything below is in double. This is where "is the camera underwater"
            // is decided with better than centimetre precision, not ±0.5 m.
            double camAltitude = r - seaRadius;
            double k = (seaRadius - r) * (seaRadius + r);

            Vector3 up = AstroFrame.ToSimulation(
                r > 1e-9 ? (observer - bodyPosition) / r : new Vector3d(0d, 0d, 1d));

            Transform t = camera.transform;
            Vector3 camRight = t.right;
            Vector3 camUp = t.up;
            Vector3 camFwd = t.forward;

            float tanHalfFovY = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanHalfFovX = tanHalfFovY * camera.aspect;

            Shader.SetGlobalVector(SigmaId, SigmaPerMeter);
            Shader.SetGlobalColor(WaterColorId, WaterColor);
            Shader.SetGlobalFloat(AmbientId, Ambient);
            Shader.SetGlobalFloat(SunPenId, SunPenetration);
            Shader.SetGlobalFloat(SunShapeFloorId, SunShapeFloor);
            Shader.SetGlobalFloat(DitherAmountId, DitherAmount);
            Shader.SetGlobalFloat(TransitionId, TransitionMeters);
            Shader.SetGlobalFloat(DownwardId, DownwardDarkening);
            Shader.SetGlobalFloat(AmbientDecayId, AmbientDecay);
            Shader.SetGlobalFloat(CeilingModeId, (float)Ceiling);
            Shader.SetGlobalFloat(CamAltId, (float)camAltitude);
            Shader.SetGlobalFloat(SeaKId, (float)k);
            Shader.SetGlobalFloat(RadiusId, (float)r);
            Shader.SetGlobalVector(UpId, up);
            Shader.SetGlobalVector(SunDirId, Effect.SunDirection);
            Shader.SetGlobalVector(CamRightId, camRight);
            Shader.SetGlobalVector(CamUpId, camUp);
            Shader.SetGlobalVector(CamFwdId, camFwd);
            Shader.SetGlobalFloat(TanXId, tanHalfFovX);
            Shader.SetGlobalFloat(TanYId, tanHalfFovY);
            Shader.SetGlobalFloat(DebugModeId, DebugMode);
        }

        private double SeaLevelMetersOf(OrbitingBody body)
        {
            // Sea level lives in the terrain model. Take it from the SAME place
            // WaterQuery does, otherwise the "water" sphere and the "submersion"
            // sphere would differ by the sea level and the transition edge would drift.
            return body.Terrain == null ? 0d : body.Terrain.GetSeaLevelMeters();
        }

        private bool EnsurePasses(Camera camera)
        {
            if (volume != null)
            {
                volume.targetCamera = camera;
                return true;
            }

            Shader fogShader = Shader.Find("Galilego/UnderwaterViewFog");
            if (fogShader == null)
            {
                Debug.LogWarning("[UnderwaterViewFog] не найден шейдер Galilego/UnderwaterViewFog - затухание выключено.");
                return false;
            }

            fogMaterial = new Material(fogShader);
            compositeMaterial = new Material(fogShader);

            fogPass = new FullScreenCustomPass
            {
                name = "Underwater View Fog",
                fullscreenPassMaterial = fogMaterial,
                fetchColorBuffer = true,
                materialPassName = "UnderwaterViewFog",

                // Custom — its own buffer, so the camera buffer is not read and
                // written as a single resource.
                //
                // targetDepthBuffer = None is NOT a mistake: HDRP's TargetBuffer has
                // only Camera/Custom/None, there is no separate "Depth". Depth is read
                // by CustomPassLoadCameraDepth, which takes the GLOBAL camera buffer
                // and does not depend on the targetDepthBuffer declaration. None is also
                // here because at BeforePostProcess the camera buffer is alive and valid,
                // whereas at AfterPostProcess (as in UnderwaterSunRays) it is unusable.
                targetColorBuffer = CustomPass.TargetBuffer.Custom,
                targetDepthBuffer = CustomPass.TargetBuffer.None,
                clearFlags = ClearFlag.Color,
            };

            compositePass = new FullScreenCustomPass
            {
                name = "Underwater View Fog Composite",
                fullscreenPassMaterial = compositeMaterial,
                fetchColorBuffer = false,
                materialPassName = "UnderwaterViewFogComposite",
                targetColorBuffer = CustomPass.TargetBuffer.Camera,
                targetDepthBuffer = CustomPass.TargetBuffer.None,
                clearFlags = ClearFlag.None,
            };

            GameObject volumeObject = new GameObject("UnderwaterViewFogVolume");
            volumeObject.SetActive(false);
            volumeObject.layer = gameObject.layer;
            volume = volumeObject.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.targetCamera = camera;

            // BeforePostProcess, not AfterPostProcess: there the depth buffer is
            // already invalid, and it is read per-pixel here.
            volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            volume.customPasses.Add(fogPass);
            volume.customPasses.Add(compositePass);
            volumeObject.SetActive(true);
            return true;
        }

private void SetVolumeActive(bool active)
        {
            // When inactive the global is not updated (early return) and would stay
            // negative forever, so set it well above water.
            if (!active)
            {
                Shader.SetGlobalFloat(CamAltId, 1.0e6f);
            }

            if (volume != null && volume.gameObject.activeSelf != active)
            {
                volume.gameObject.SetActive(active);
            }
        }

        private void DisposePasses()
        {
            if (volume != null)
            {
                Destroy(volume.gameObject);
                volume = null;
            }

            if (fogMaterial != null)
            {
                Destroy(fogMaterial);
                fogMaterial = null;
            }

            if (compositeMaterial != null)
            {
                Destroy(compositeMaterial);
                compositeMaterial = null;
            }

            fogPass = null;
            compositePass = null;
        }

        private void OnDisable()
        {
            SetVolumeActive(false);
        }

        private void OnDestroy()
        {
            DisposePasses();
        }
    }
}
