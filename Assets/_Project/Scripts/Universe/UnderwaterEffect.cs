using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// Базовый эффект погружения под воду: пока камера ниже уровня моря,
    /// подмешивает СОБСТВЕННЫЙ локальный Volume (Fog + ColorAdjustments) —
    /// общий ExposureVolume дня/ночи (SkyEnvironment) не трогаем, чтобы не
    /// гоняться с ним за одним и тем же полем в разных LateUpdate.
    ///
    /// Источник подводного состояния и глубины — WaterQuery.IsSubmergedAt по
    /// абсолютной позиции камеры (при наличии Camera), иначе по Runner.PlayerPosition.
    ///
    /// Сила эффекта растёт с глубиной: у самой поверхности почти не видна
    /// (мягкий SurfaceFadeMeters), к FullEffectDepthMeters выходит на
    /// потолок — дальняя видимость падает (meanFreePath) и картинка темнеет
    /// (ColorAdjustments.postExposure, EV). Это "базовый вариант" —
    /// без цветовой аберрации у кромки, без капель на линзе и т.п.
    /// </summary>
    [DefaultExecutionOrder(-39)]
    public sealed class UnderwaterEffect : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены. Пусто — найдётся сам через FindObjectOfType при старте.")]
        public SimulationRunner Runner;

        [Tooltip("Камера, для которой считается подводное состояние. Пусто — Camera.main.")]
        public Camera EffectCamera;

        [Header("Внешний вид")]
        [Tooltip("Цвет мутной толщи воды (linear). Для консистентности стоит взять близко к _UnderwaterColor в WaterSurface.shader.")]
        public Color WaterFogColor = new Color(0.02f, 0.16f, 0.30f);

        [Tooltip("Дальность видимости у самой поверхности (м). 20-30 м: силуэты читаются, даль тонет в дымке.")]
        [Min(1f)]
        public float ShallowVisibilityMeters = 400f;

        [Tooltip("Дальность видимости на предельной глубине (м) — мутная вода, тонет силуэт вдали.")]
        [Min(0.5f)]
        public float DeepVisibilityMeters = 150f;

        [Tooltip("Технический потолок дальности тумана (м) — не меняется с глубиной, просто отсекает расчёт вдали.")]
        [Min(10f)]
        public float MaxFogDistanceMeters = 64f;

        [Header("Затемнение")]
        [Tooltip("Глубина (м), на которой видимость/затемнение выходят на потолок.")]
        [Min(1f)]
        public float FullEffectDepthMeters = 50f;

        [Tooltip("Затемнение на предельной глубине (EV, отрицательное = темнее). Накладывается ПОВЕРХ обычной экспозиции сцены.")]
        public float MaxDarkeningEv = -1.5f;

        [Header("Переход у поверхности")]
        [Tooltip("Глубина (м), за которую эффект плавно набирает полный вес после входа в воду — прячет резкий щелчок на границе.")]
        [Min(0.05f)]
        public float SurfaceFadeMeters = 3f;

        [Header("Привязка к глазам")]
        [Tooltip("Высота глаз камеры над позицией игрока (м). Глубина считается от позиции игрока, а глаза выше — вычитаем, иначе экран мутнеет уже вплавь на поверхности. Должно совпадать с EyeHeightMeters на FirstPersonCamera.")]
        [Min(0f)]
        public float EyeHeightMeters = 2f;

        [Tooltip("Высота глаз вплавь (м): пловец лежит. Должно совпадать с SwimEyeHeightMeters на FirstPersonCamera и SimulationRunner — иначе эффект и физика разойдутся.")]
        [Min(0f)]
        public float SwimEyeHeightMeters = 0.5f;

        public bool IsUnderwater { get; private set; }
        public double EyeDepthMeters { get; private set; }
        public float EffectWeight { get; private set; }
        public Vector3 LocalUp { get; private set; } = Vector3.up;
        public Vector3 SunDirection { get; private set; } = Vector3.zero;
        public static bool CameraIsUnderwater { get; private set; }

        private Volume volume;
        private Fog fog;
        private ColorAdjustments colorAdjustments;
        private Camera capturedCamera;
        private CameraClearFlags surfaceClearFlags;
        private Color surfaceBackgroundColor;
        private HDAdditionalCameraData capturedHdCamera;
        private HDAdditionalCameraData.ClearColorMode surfaceClearColorMode;
        private Color surfaceHdrBackgroundColor;

        private void Awake()
        {
            CameraIsUnderwater = false;
            Shader.SetGlobalFloat("_UnderwaterCameraDepth", 0f);
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            EnsureVolume();
        }

        private void EnsureVolume()
        {
            if (volume != null)
            {
                return;
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "UnderwaterProfile (auto)";

            fog = profile.Add<Fog>(false);
            fog.enabled.Override(false);
            fog.colorMode.Override(FogColorMode.ConstantColor);
            fog.color.Override(WaterFogColor);
            fog.albedo.Override(WaterFogColor);
            fog.maxFogDistance.Override(MaxFogDistanceMeters);
            fog.meanFreePath.Override(ShallowVisibilityMeters);
            fog.enableVolumetricFog.Override(false);
            fog.anisotropy.Override(0.5f);
            fog.denoisingMode.Override(FogDenoisingMode.Gaussian);
            fog.directionalLightsOnly.Override(true);
            fog.depthExtent.Override(Mathf.Min(MaxFogDistanceMeters, 32f));

            colorAdjustments = profile.Add<ColorAdjustments>(false);
            colorAdjustments.postExposure.Override(0f);
            colorAdjustments.colorFilter.Override(new Color(0.55f, 0.80f, 1f, 1f));

            var volumeGo = new GameObject("UnderwaterVolume (auto)");
            volumeGo.transform.SetParent(transform, false);
            volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 0f;
            volume.profile = profile;
        }

        private void LateUpdate()
        {
            IsUnderwater = false;
            CameraIsUnderwater = false;
            EyeDepthMeters = 0d;
            EffectWeight = 0f;
            LocalUp = Vector3.up;
            SunDirection = Vector3.zero;
            Shader.SetGlobalFloat("_UnderwaterCameraDepth", 0f);

            Camera camera = EffectCamera != null ? EffectCamera : Camera.main;
            if (camera != null)
            {
                EffectCamera = camera;
            }

            if (Runner != null && Runner.DominantBody != null)
            {
                OrbitingBody body = Runner.DominantBody;
                if (WaterQuery.HasOcean(body))
                {
                    body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPosition, out _);
                    Vector3d observer = Runner.PlayerPosition;
                    if (camera != null)
                    {
                        observer = FloatingOrigin.Anchor + AstroFrame.ToAstro(camera.transform.position);
                    }

                    Vector3d outward = observer - bodyPosition;
                    double outwardMagnitude = outward.Magnitude;
                    if (outwardMagnitude > 0d)
                    {
                        LocalUp = AstroFrame.ToSimulation(outward / outwardMagnitude);
                    }

                    bool submerged = WaterQuery.IsSubmergedAt(
                        body, observer, Runner.TimeSeconds, out double depth);
                    if (camera == null)
                    {
                        float eyeHeight = Runner.PlayerMode == PlayerMode.Swimming
                            ? SwimEyeHeightMeters
                            : EyeHeightMeters;
                        depth -= eyeHeight;
                        submerged = submerged && depth > 0d;
                    }

                    if (Runner.SystemState != null && Runner.SystemState.Root != null)
                    {
                        Runner.SystemState.Root.EvaluateWorldState(
                            Runner.TimeSeconds, out Vector3d starPosition, out _);
                        Vector3d toStar = starPosition - observer;
                        double starDistance = toStar.Magnitude;
                        if (starDistance > 0d)
                        {
                            SunDirection = AstroFrame.ToSimulation(toStar / starDistance);
                        }
                    }

                    if (submerged)
                    {
                        EnsureVolume();
                        IsUnderwater = true;
                        CameraIsUnderwater = true;
                        EyeDepthMeters = depth;
                        Shader.SetGlobalFloat("_UnderwaterCameraDepth", (float)depth);

                        float depthMeters = (float)depth;
                        float t = Mathf.Clamp01(depthMeters / Mathf.Max(0.01f, FullEffectDepthMeters));
                        EffectWeight = SurfaceFadeMeters > 0f
                            ? Mathf.Clamp01(depthMeters / SurfaceFadeMeters)
                            : 1f;

                        fog.color.Override(WaterFogColor);
                        fog.albedo.Override(WaterFogColor);
                        fog.maxFogDistance.Override(MaxFogDistanceMeters);
                        fog.meanFreePath.Override(Mathf.Lerp(ShallowVisibilityMeters, DeepVisibilityMeters, t));
                        colorAdjustments.postExposure.Override(Mathf.Lerp(0f, MaxDarkeningEv, t));
                    }
                }
            }

            if (fog != null)
            {
                fog.enabled.Override(false);
            }

            if (volume != null)
            {
                volume.weight = EffectWeight;
            }

            ApplyCameraBackground(camera, IsUnderwater);
        }

        private void ApplyCameraBackground(Camera camera, bool underwater)
        {
            if (camera == null)
            {
                return;
            }

            if (capturedCamera != camera)
            {
                capturedCamera = camera;
                surfaceClearFlags = camera.clearFlags;
                surfaceBackgroundColor = camera.backgroundColor;
                capturedHdCamera = camera.GetComponent<HDAdditionalCameraData>();
                if (capturedHdCamera != null)
                {
                    surfaceClearColorMode = capturedHdCamera.clearColorMode;
                    surfaceHdrBackgroundColor = capturedHdCamera.backgroundColorHDR;
                }
            }

            if (underwater)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = WaterFogColor;
                // HDRP чистит кадр по СВОИМ полям HDAdditionalCameraData, а не по
                // Camera.backgroundColor: без этого небо/пустота остаются звёздно-
                // чёрными даже при clearFlags=SolidColor. Поэтому дублируем и сюда.
                if (capturedHdCamera != null)
                {
                    capturedHdCamera.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                    capturedHdCamera.backgroundColorHDR = WaterFogColor;
                }
            }
            else
            {
                camera.clearFlags = surfaceClearFlags;
                camera.backgroundColor = surfaceBackgroundColor;
                if (capturedHdCamera != null)
                {
                    capturedHdCamera.clearColorMode = surfaceClearColorMode;
                    capturedHdCamera.backgroundColorHDR = surfaceHdrBackgroundColor;
                }
            }
        }

        private void RestoreCameraBackground()
        {
            if (capturedCamera == null)
            {
                return;
            }

            capturedCamera.clearFlags = surfaceClearFlags;
            capturedCamera.backgroundColor = surfaceBackgroundColor;
            if (capturedHdCamera != null)
            {
                capturedHdCamera.clearColorMode = surfaceClearColorMode;
                capturedHdCamera.backgroundColorHDR = surfaceHdrBackgroundColor;
                capturedHdCamera = null;
            }
            capturedCamera = null;
        }

        private void OnDisable()
        {
            IsUnderwater = false;
            CameraIsUnderwater = false;
            EyeDepthMeters = 0d;
            EffectWeight = 0f;
            Shader.SetGlobalFloat("_UnderwaterCameraDepth", 0f);
            RestoreCameraBackground();
            if (volume != null)
            {
                volume.weight = 0f;
            }
        }

        private void OnDestroy()
        {
            CameraIsUnderwater = false;
            Shader.SetGlobalFloat("_UnderwaterCameraDepth", 0f);
            RestoreCameraBackground();
            if (volume != null)
            {
                if (volume.profile != null)
                {
                    Destroy(volume.profile);
                }

                Destroy(volume.gameObject);
                volume = null;
            }
        }
    }
}
