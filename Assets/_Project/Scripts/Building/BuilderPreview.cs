using Galilego.Spacecraft;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Building
{
    /// <summary>
    /// 3D-предпросмотр детали для окна инвентаря: отдельная камера рисует
    /// деталь в RenderTexture, объект медленно вращается, размеры берутся из
    /// метрик (при смене размера визуал пересобирается). Риг живёт на слое 31
    /// далеко от мира — главная камера его не видит (и слой вычищается из её
    /// маски), рендер идёт вручную и не чаще 30 кадров в секунду.
    ///
    /// Камера выключена (enabled = false): с targetTexture она иначе рисовала
    /// бы кадр каждый тик — здесь кадр запрашивается только из Ensure().
    /// </summary>
    internal sealed class BuilderPreview
    {
        private const int Layer = 31;

        /// <summary>
        /// Слой визуала ИКОНКИ (отдельный от слоя превью): камера снимает
        /// иконку маской только по этому слою. Так в кадр иконки не попадает
        /// визуал превью — выключение объекта (SetActive) не спасало: HDRP
        /// GPU Resident Drawer дорисовывал его из батча прошлого кадра
        /// (двигатель «просвечивал» сквозь иконку отделителя).
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

        /// <summary>Фиксированная экспозиция кадра превью (EV100).</summary>
        private const float FixedExposureEv = -3f;

        /// <summary>Фон кадра — под цвет рамки Viewport, чтобы не было швов.</summary>
        private static readonly Color Background = new Color(0.038f, 0.038f, 0.040f, 1f);

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
        /// Обновить предпросмотр: собрать риг, пересобрать визуал при смене
        /// детали/размера, прокрутить и (не чаще 30 Гц) отрисовать кадр.
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
        /// Один раз отрисовать «портреты» видов деталей: те же студийный свет и
        /// ракурс «три четверти», что у превью, но с ПРОЗРАЧНЫМ фоном — иконки
        /// лягут на любой слот. Размеры — базовые; слоты панели со своими
        /// размерами дорисовывают себя сами (CaptureIcon). У каждого двигателя
        /// иконка своя (по Id модели). Вызывается при старте.
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
        }

        /// <summary>
        /// Отрисовать иконку детали под ПРОИЗВОЛЬНЫЕ размеры (слот панели со
        /// своими размерами): та же студия и ракурс, что у дефолтных иконок.
        /// Текстура принадлежит вызывающему — он её и уничтожает. null, если
        /// риг/геометрия недоступны.
        /// </summary>
        public Texture2D CaptureIcon(PartKind kind, PartMetrics metrics, PartCatalog.EngineTemplate engine,
            Material tankSide, Material tankCap, Material separator)
        {
            EnsureRig();
            if (rig == null || metrics.Radius <= 0f)
            {
                return null;
            }

            // Прозрачный фон и временный маленький таргет: иконка ляжет на слот
            // любого цвета, а кадр превью не пострадает. Камеру и спинеер
            // возвращаем как были: кадрирование иконки — СВОЁ (иконный запас
            // кадра), а превью перекадрируется только при смене детали.
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

            // Иконка снимается маской ТОЛЬКО по своему слою: визуал превью
            // (слой Layer) в кадр не попадает вовсе.
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

            // Кольцо отделителя плоское: чуть приподнимаем камеру, иначе в
            // кадре — тонкая полоска вместо кольца (свет остаётся солнечный).
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
            texture.Apply();
            RenderTexture.active = previous;

            // Destroy отложен до конца кадра: гасим сразу, иначе следующая
            // иконка рендерится вместе с предыдущей (обе под спинеером).
            visual.Root.SetActive(false);
            Object.Destroy(visual.Root);
            return texture;
        }

        /// <summary>Снести риг и текстуру (окно закрылось / компонент выключен).</summary>
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
            // Далеко от мира: даже если маска камеры не вычистится, объект
            // никогда не попадёт в кадр.
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

            // Своя экспозиция для кадра превью: авто-экспозиция сцены считает
            // кадр от яркого фона и топит деталь в темноту. Фиксированная EV
            // даёт стабильный «студийный» вид в любое время суток.
            BuildExposureVolume(cameraGo);

            // Металлу нужна среда для отражений: без неё бак — чёрный силуэт.
            // Кубическая «студия» (градиент сверху-вниз) + Custom-проба.
            BuildReflectionProbe();

            keyLight = FindKeyLight();
            KeepHidden();
        }

        /// <summary>Reflection Probe со «студийной» кубической текстурой.</summary>
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

            // HDRP берёт кастомную кубическую текстуру из своих данных
            // (hd.customTexture), а не из ReflectionProbe.customBakedTexture.
            var hd = probeGo.AddComponent<HDAdditionalReflectionData>();
            hd.mode = ProbeSettings.Mode.Custom;
            hd.customTexture = cubemap;
        }

        /// <summary>Кубическая текстура «студии»: светлое небо, тёмный низ.</summary>
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
                        float h = dir.y * 0.5f + 0.5f;              // 0 низ, 1 верх
                        float top = Mathf.Lerp(0.30f, 1.05f, h * h); // мягкий нейтральный градиент
                        pixels[(y * size) + x] = new Color(top, top, top, 1f);
                    }
                }

                cubemap.SetPixels(pixels, (CubemapFace)face);
            }

            cubemap.Apply();
            return cubemap;
        }

        /// <summary>Направление пикселя грани кубической текстуры (Unity-порядок).</summary>
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
        /// Volume у камеры превью: только Fixed Exposure. Коллайдер-короб
        /// нужен, чтобы Volume считался не-глобальным и влиял только на эту
        /// камеру (главная камера в 10 км — вне короба).
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

        /// <summary>Самый яркий включённый направленный свет — по нему ставим камеру.</summary>
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
        /// Направление камеры превью: со СТОРОНЫ СОЛНЦА, иначе деталь — тёмный
        /// силуэт (камера на теневой стороне). Чуть сверху и под 3/4.
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

        /// <summary>Слои превью и иконок не должны рисоваться главной камерой.</summary>
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

        /// <summary>Кадрировать камеру по габаритам детали (высота и диаметр).</summary>
        private void Frame(PartMetrics metrics)
        {
            Frame(metrics, ViewDirection(), FrameMargin);
        }

        /// <summary>Кадрирование под заданное направление камеры и запас кадра.</summary>
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
