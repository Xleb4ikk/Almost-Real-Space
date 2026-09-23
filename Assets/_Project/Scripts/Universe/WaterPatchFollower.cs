using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// СПАЙК Этапа 0: локальный плоский патч штатной HDRP Water у игрока.
    ///
    /// Вопрос спайка (exit-критерий): умеет ли Water Surface типа Ocean/Sea/Lake
    /// корректно работать при повёрнутом трансформе. Симуляция HDRP считается в
    /// локальном пространстве воды (waterToWorld/worldToWater строятся из
    /// transform.position + transform.rotation, UpVector() = transform.up,
    /// underwater-объём для infinite проверяется проекцией на upDirection),
    /// поэтому поворот ДОЛЖЕН работать. Если на Play волны/пена едут боком,
    /// рвутся или патч лежит не по горизонту — спайк ПРОВАЛЕН: сразу План Б
    /// (текстуры в Galilego/WaterSurface), не форсить геометрией/трюками.
    ///
    /// Ориентация: тот же «локальный верх», что FirstPersonCamera.ComputeLocalUp():
    /// outward = PlayerPosition − bodyPos, up = ToSimulation(outward/|outward|).
    /// Позиция — через FloatingOrigin.ToRender (якорь = PlayerPosition), иначе
    /// float-дрожание на радиусе ~1.1e6 м.
    ///
    /// Кривизна Terra (R = 1143000 м, h ≈ d²/2R): патч 512 м → просадка края
    /// ~3 см (волна 0.9 м) — глазом не видно. Не расширять патч за ~700 м
    /// (край −21 см) без переоценки шва с дальними чанками.
    ///
    /// Создание: автоматически PlanetSurfaceRenderer.EnsureHdrpWaterPatch()
    /// при старте (флаг UseHdrpWaterPatch) — руками в сцену ничего добавлять
    /// не нужно. Ручной вариант тоже работает: пустой GameObject + Water
    /// Surface (Ocean, Sea or Lake) + этот скрипт, Play у моря.
    /// Подводность: штатный underwater-объём HDRP НЕ включаем (underWater=false) —
    /// он конечен (repetitionSize): из-под воды его граница видна как тёмный
    /// квадрат под игроком. Под воду ведёт глобальный UnderwaterEffect (fog на
    /// весь экран, без границ), этот скрипт его никогда не гасит.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class WaterPatchFollower : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены. Пусто — найдётся сам при старте.")]
        public SimulationRunner Runner;

        [Tooltip("Камера для стабилизации рысканья патча. Пусто — Camera.main.")]
        public Transform HeadingCamera;

        [Tooltip("Размер процедурного Quad-патча = repetitionSize HDRP (м). 512 м: край −3 см от кривизны.")]
        [Min(64f)]
        public float PatchSizeMeters = 512f;

        [Tooltip("Подъём патча над уровнем моря (м): старая чанковая вода ходит ±0.9 м, патч должен лежать выше неё, иначе z-fight. Дальний фон — старая вода.")]
        public float PatchLiftMeters = 0.4f;

        [Tooltip("Каждый кадр прописывать настройки спайка в Water Surface (тип/геометрия/underwater).")]
        public bool ApplySpikeSettings = true;

        [Tooltip("Пока патч активен — гасить UnderwaterEffect, чтобы не двоить туман со штатным underwater HDRP. ДЕРЖАТЬ ВЫКЛ: штатный underwater-объём конечен и из-под воды виден квадратом, подводность ведёт глобальный UnderwaterEffect.")]
        public bool MuteUnderwaterEffectWhileActive = false;

        private WaterSurface waterSurface;
        private UnderwaterEffect underwaterEffect;
        private bool underwaterWasEnabled = true;
        private bool nullSurfaceWarned;

        private void Awake()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (HeadingCamera == null && Camera.main != null)
            {
                HeadingCamera = Camera.main.transform;
            }

            waterSurface = GetComponent<WaterSurface>();
            underwaterEffect = FindAnyObjectByType<UnderwaterEffect>();
            if (underwaterEffect != null)
            {
                underwaterWasEnabled = underwaterEffect.enabled;
            }
        }

        private void LateUpdate()
        {
            bool active = TryPlacePatch();
            ApplyUnderwaterDiscipline(active);
        }

        private void OnDisable()
        {
            RestoreUnderwaterEffect();
        }

        /// <summary>
        /// Ставит патч на уровень моря под игроком, разворачивает +Y по
        /// локальной вертикали. false — патч невалиден (нет моря/тела).
        /// </summary>
        private bool TryPlacePatch()
        {
            if (Runner == null || Runner.DominantBody == null)
            {
                SetSurfaceEnabled(false);
                return false;
            }

            OrbitingBody body = Runner.DominantBody;
            if (!WaterQuery.HasOcean(body))
            {
                SetSurfaceEnabled(false);
                return false;
            }

            double surfaceRadius = WaterQuery.SeaSurfaceRadius(body);
            if (double.IsNaN(surfaceRadius))
            {
                SetSurfaceEnabled(false);
                return false;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d outward = Runner.PlayerPosition - bodyPos;
            double magnitude = outward.Magnitude;
            if (magnitude <= 0d)
            {
                SetSurfaceEnabled(false);
                return false;
            }

            Vector3d dir = outward / magnitude;
            Vector3 up = AstroFrame.ToSimulation(dir);

            Vector3d seaAbsolute = bodyPos + (dir * surfaceRadius);
            transform.position = FloatingOrigin.ToRender(seaAbsolute) + (up * PatchLiftMeters);
            transform.rotation = BuildRotation(up);
            ConfigureSurface();
            SetSurfaceEnabled(true);
            return true;
        }

        private Quaternion BuildRotation(Vector3 up)
        {
            Vector3 forward = Vector3.forward;
            if (HeadingCamera != null)
            {
                forward = HeadingCamera.forward;
            }

            Vector3 flat = Vector3.ProjectOnPlane(forward, up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = Vector3.ProjectOnPlane(transform.forward, up);
            }

            if (flat.sqrMagnitude < 1e-6f)
            {
                flat = Vector3.ProjectOnPlane(Vector3.forward, up);
            }

            if (flat.sqrMagnitude < 1e-6f)
            {
                return Quaternion.FromToRotation(Vector3.up, up);
            }

            return Quaternion.LookRotation(flat.normalized, up);
        }

        private void ConfigureSurface()
        {
            if (!ApplySpikeSettings)
            {
                return;
            }

            if (waterSurface == null)
            {
                waterSurface = GetComponent<WaterSurface>();
            }

            if (waterSurface == null)
            {
                if (!nullSurfaceWarned)
                {
                    nullSurfaceWarned = true;
                    Debug.LogWarning("[WaterPatchFollower] нет Water Surface на объекте — добавьте компонент " +
                        "Water Surface (Ocean, Sea or Lake) и нажмите Play у моря. Без него спайк нечем проверять.");
                }

                return;
            }

            waterSurface.surfaceType = WaterSurfaceType.OceanSeaLake;
            waterSurface.geometryType = WaterGeometryType.Quad;
            waterSurface.repetitionSize = Mathf.Max(64f, PatchSizeMeters);
            waterSurface.timeMultiplier = 1f;
            // НЕ включать штатный underwater-объём: он ограничен экстентом квада
            // (repetitionSize) и из-под воды читается как тёмный квадрат 512 м
            // под игроком. Подводный fog — глобальный UnderwaterEffect.
            waterSurface.underWater = false;
        }

        private void SetSurfaceEnabled(bool enabled)
        {
            if (waterSurface == null)
            {
                waterSurface = GetComponent<WaterSurface>();
            }

            if (waterSurface != null && waterSurface.enabled != enabled)
            {
                waterSurface.enabled = enabled;
            }
        }

        /// <summary>
        /// Вердикт по подводности: подводный fog всегда ведёт глобальный
        /// UnderwaterEffect (на весь экран, без границ). Штатный underwater-объём
        /// HDRP не включаем (см. ConfigureSurface): он конечен и из-под воды
        /// виден квадратом. Гашение оставлено только как ручной оверрайд флагом
        /// MuteUnderwaterEffectWhileActive (по умолчанию выкл).
        /// </summary>
        private void ApplyUnderwaterDiscipline(bool patchActive)
        {
            if (!MuteUnderwaterEffectWhileActive || underwaterEffect == null)
            {
                return;
            }

            if (waterSurface == null)
            {
                waterSurface = GetComponent<WaterSurface>();
            }

            bool submerged = false;
            if (patchActive && Runner != null && Runner.DominantBody != null
                && WaterQuery.HasOcean(Runner.DominantBody))
            {
                double depth = WaterQuery.SubmersionDepthAt(
                    Runner.DominantBody, Runner.PlayerPosition, Runner.TimeSeconds);
                submerged = !double.IsNaN(depth) && depth > 0d;
            }

            bool hdrpLeads = submerged && waterSurface != null
                && waterSurface.enabled && waterSurface.underWater;
            if (underwaterEffect.enabled == !hdrpLeads)
            {
                return;
            }

            underwaterEffect.enabled = !hdrpLeads;
            Debug.Log(hdrpLeads
                ? "[WaterPatchFollower] подводность ведёт штатный underwater HDRP; UnderwaterEffect выключен (без двоения fog)."
                : "[WaterPatchFollower] патч неактивен; UnderwaterEffect возвращён.");
        }

        private void RestoreUnderwaterEffect()
        {
            if (underwaterEffect != null)
            {
                underwaterEffect.enabled = underwaterWasEnabled;
            }
        }
    }
}
