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
    /// Источник глубины — WaterQuery.SubmersionDepthAt по Runner.PlayerPosition
    /// (то же значение, которым физика плавания уже пользуется в
    /// SimulationRunner.StepPlayerSwimming) — эффект и физика согласованы.
    ///
    /// Сила эффекта растёт с глубиной: у самой поверхности почти не видна
    /// (мягкий SurfaceFadeMeters), к FullEffectDepthMeters выходит на
    /// потолок — дальняя видимость падает (meanFreePath) и картинка темнеет
    /// (ColorAdjustments.postExposure, EV). Это "базовый вариант" —
    /// без цветовой аберрации у кромки, без капель на линзе и т.п.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class UnderwaterEffect : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены. Пусто — найдётся сам через FindObjectOfType при старте.")]
        public SimulationRunner Runner;

        [Header("Внешний вид")]
        [Tooltip("Цвет мутной толщи воды (linear). Для консистентности стоит взять близко к _UnderwaterColor в WaterSurface.shader.")]
        public Color WaterFogColor = new Color(0.02f, 0.16f, 0.30f);

        [Tooltip("Дальность видимости у самой поверхности (м). 20-30 м: силуэты читаются, даль тонет в дымке.")]
        [Min(1f)]
        public float ShallowVisibilityMeters = 28f;

        [Tooltip("Дальность видимости на предельной глубине (м) — мутная вода, тонет силуэт вдали.")]
        [Min(0.5f)]
        public float DeepVisibilityMeters = 4f;

        [Tooltip("Технический потолок дальности тумана (м) — не меняется с глубиной, просто отсекает расчёт вдали.")]
        [Min(10f)]
        public float MaxFogDistanceMeters = 250f;

        [Header("Затемнение")]
        [Tooltip("Глубина (м), на которой видимость/затемнение выходят на потолок.")]
        [Min(1f)]
        public float FullEffectDepthMeters = 25f;

        [Tooltip("Затемнение на предельной глубине (EV, отрицательное = темнее). Накладывается ПОВЕРХ обычной экспозиции сцены.")]
        public float MaxDarkeningEv = -3.5f;

        [Header("Переход у поверхности")]
        [Tooltip("Глубина (м), за которую эффект плавно набирает полный вес после входа в воду — прячет резкий щелчок на границе.")]
        [Min(0.05f)]
        public float SurfaceFadeMeters = 1f;

        [Header("Привязка к глазам")]
        [Tooltip("Высота глаз камеры над позицией игрока (м). Глубина считается от позиции игрока, а глаза выше — вычитаем, иначе экран мутнеет уже вплавь на поверхности. Должно совпадать с EyeHeightMeters на FirstPersonCamera.")]
        [Min(0f)]
        public float EyeHeightMeters = 2f;

        [Tooltip("Высота глаз вплавь (м): пловец лежит. Должно совпадать с SwimEyeHeightMeters на FirstPersonCamera и SimulationRunner — иначе эффект и физика разойдутся.")]
        [Min(0f)]
        public float SwimEyeHeightMeters = 0.5f;

        private Volume volume;
        private Fog fog;
        private ColorAdjustments colorAdjustments;

        private void Awake()
        {
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

            fog = profile.Add<Fog>(true);
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.ConstantColor);
            fog.color.Override(WaterFogColor);
            fog.maxFogDistance.Override(MaxFogDistanceMeters);
            fog.meanFreePath.Override(ShallowVisibilityMeters);

            colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.Override(0f);

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
            float weight = 0f;
            if (Runner != null && Runner.DominantBody != null)
            {
                OrbitingBody body = Runner.DominantBody;
                if (WaterQuery.HasOcean(body))
                {
                    double depth = WaterQuery.SubmersionDepthAt(body, Runner.PlayerPosition, Runner.TimeSeconds);
                    // Глубина глаз, а не ног: камера выше позиции игрока —
                    // вплавь пловец лежит (низкий SwimEyeHeightMeters), иначе
                    // пока глаза над водой, эффекта нет.
                    float eyeH = Runner.PlayerMode == PlayerMode.Swimming ? SwimEyeHeightMeters : EyeHeightMeters;
                    double eyeDepth = depth - eyeH;
                    if (!double.IsNaN(eyeDepth) && eyeDepth > 0d)
                    {
                        EnsureVolume();

                        float depthMeters = (float)eyeDepth;
                        float t = Mathf.Clamp01(depthMeters / Mathf.Max(0.01f, FullEffectDepthMeters));
                        weight = SurfaceFadeMeters > 0f
                            ? Mathf.Clamp01(depthMeters / SurfaceFadeMeters)
                            : 1f;

                        fog.color.Override(WaterFogColor);
                        fog.meanFreePath.Override(Mathf.Lerp(ShallowVisibilityMeters, DeepVisibilityMeters, t));
                        colorAdjustments.postExposure.Override(Mathf.Lerp(0f, MaxDarkeningEv, t) * weight);
                    }
                }
            }

            if (volume != null)
            {
                volume.weight = weight;
            }
        }

        private void OnDestroy()
        {
            if (volume != null && volume.profile != null)
            {
                Destroy(volume.profile);
            }
        }
    }
}
