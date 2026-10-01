using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// Подводное затухание ВИДА: спектральное поглощение по закону
    /// Бера — Ламберта, полноэкранным проходом HDRP.
    ///
    /// Чем отличается от UnderwaterEffect: тот задаёт тон и экспозицию
    /// (ColorAdjustments) и подменяет фон камеры, но НЕ добавляет
    /// затухания по расстоянию - HDRP Fog у него выключен флагом
    /// enabled, поэтому весь override игнорируется. Из-за этого под водой
    /// видно километры с чёткими силуэтами.
    ///
    /// Почему свой проход, а не включить тот Fog: встроенный Fog
    /// одноцветный (цвет + meanFreePath) и не различает каналы, поэтому
    /// дальнее уходит в серую дымку, а не в сине-зелёный. Ради последнего
    /// и нужен спектральный sigma.
    ///
    /// Где стоит в кадре: BeforePostProcess, а не AfterPostProcess.
    /// На AfterPostProcess буфер глубины в HDRP уже невалиден (у
    /// UnderwaterSunRays там стоит targetDepthBuffer = None именно поэтому),
    /// а нам深度 читать обязательно. BeforePostProcess - после
    /// непрозрачной геометрии, неба и прозрачных, до тонмаппинга и UI.
    ///
    /// Точность: высоту камеры над уровнем моря и k = sea^2 - |C|^2 считаем
    /// в double здесь и передаём готовыми. В шейдере length(C) - sea при
    /// R = 6 371 000 м даёт шум ±0.5 м (ULP float32), а по этой величине
    /// решается «под водой ли камера» - будет мигание на границе.
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

        [Header("Переход через поверхность")]
        [Tooltip("Ширина плавного перехода по высоте камеры (м). Строго НЕ ноль: при if-ветке мигает на границе, здесь идёт smoothstep.")]
        [Min(0.02f)]
        public float TransitionMeters = 0.8f;

        public bool Enabled = true;

        /// <summary>
        /// Отладочный вывод одного слагаемого прямо в кадр, без затухания.
        /// Нужен, когда картинка «идёт слоями»: сразу видно, КАКОЙ из членов
        /// полосит - глубина, путь в воде, пропускание или сам захват кадра.
        /// 0 = выключено (обычный вид).
        /// 1 = под (плавный переход), 2 = путь в воде pathIn/50 м (красный),
        /// 3 = пропускание T, 4 = eyeDepth/1000 м, 5 = выход tExit/5000 м,
        /// 6 = uv.x (проверка попадания в кадр), 7 = исходный цвет сцены.
        /// </summary>
        [Range(0, 7)]
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
            // Позиция камеры в абсолютных (astro) координатах - та же, что
            // считает UnderwaterEffect: FloatingOrigin.Anchor плюс локальная
            // позиция камеры.
            Vector3d observer = FloatingOrigin.Anchor
                + AstroFrame.ToAstro(camera.transform.position);
            body.EvaluateWorldState(Effect.Runner.TimeSeconds, out Vector3d bodyPosition, out _);

            double seaRadius = body.Radius + SeaLevelMetersOf(body);
            double r = (observer - bodyPosition).Magnitude;

            // Всё, что ниже, - в double. Именно здесь решается «под водой ли
            // камера» с точностью лучше сантиметра, а не ±0.5 м.
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
            // Уровень моря живёт в модели рельефа. Берём его ОТТУДА ЖЕ, откуда
            // берёт WaterQuery, иначе сфера «вода» и сфера «погружение» разойдутся
            // на величину уровня моря и граница перехода уедет.
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

                // Custom - свой буфер, чтобы не читать и писать буфер камеры
                // одним ресурсом.
                //
                // targetDepthBuffer = None, и это НЕ ошибка: у HDRP в
                // TargetBuffer только Camera/Custom/None, отдельного "Depth"
                // нет. Глубину читает CustomPassLoadCameraDepth, которая
                // берёт ГЛОБАЛЬНЫЙ буфер камеры и от объявления targetDepthBuffer
                // не зависит. None здесь ещё и потому, что на BeforePostProcess
                // буфер камеры жив и валиден, а вот на AfterPostProcess (как у
                // UnderwaterSunRays) он уже непригоден.
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

            // BeforePostProcess, а не AfterPostProcess: там буфер глубины уже
            // невалиден, а нам он читается попиксельно.
            volume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            volume.customPasses.Add(fogPass);
            volume.customPasses.Add(compositePass);
            volumeObject.SetActive(true);
            return true;
        }

        private void SetVolumeActive(bool active)
        {
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
