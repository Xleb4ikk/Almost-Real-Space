using Galilego.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe
{
    /// <summary>
    /// Local flat HDRP Water patch at the player.
    ///
    /// HDRP water simulation runs in the water's local space (waterToWorld/worldToWater
    /// are built from transform.position + transform.rotation, UpVector() = transform.up),
    /// so a rotated transform works.
    ///
    /// Orientation: the same "local up" as FirstPersonCamera.ComputeLocalUp():
    /// outward = PlayerPosition − bodyPos, up = ToSimulation(outward/|outward|).
    /// Position via FloatingOrigin.ToRender (anchor = PlayerPosition) to avoid float
    /// jitter at a radius of ~1.1e6 m.
    ///
    /// Curvature of Terra (R = 1143000 m, h ≈ d²/2R): a 512 m patch droops ~3 cm at the
    /// edge (wave 0.9 m) — invisible. Do not enlarge the patch beyond ~700 m
    /// (edge −21 cm) without re-evaluating the seam with the distant chunks.
    ///
    /// Created automatically by PlanetSurfaceRenderer.EnsureHdrpWaterPatch() at start
    /// (flag UseHdrpWaterPatch) — nothing needs to be added to the scene by hand. The
    /// manual setup also works: an empty GameObject + Water Surface (Ocean, Sea or Lake)
    /// + this script, Play near the sea.
    /// Underwater: the HDRP underwater volume is NOT enabled (underWater=false) — it is
    /// finite (repetitionSize), and its edge is visible as a dark square under the
    /// player from below. The global UnderwaterEffect (full-screen fog, no bounds)
    /// handles underwater, and this script never mutes it.
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
        /// Places the patch at sea level under the player and orients +Y along the
        /// local vertical. false — the patch is invalid (no sea/body).
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
            // Do NOT enable the built-in underwater volume: it is bounded by the quad
            // extent (repetitionSize) and reads from below as a dark 512 m square
            // under the player. Underwater fog is the global UnderwaterEffect.
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
        /// Underwater verdict: underwater fog is always driven by the global
        /// UnderwaterEffect (full screen, unbounded). The built-in HDRP underwater
        /// volume is not enabled (see ConfigureSurface): it is finite and visible as a
        /// square from below. The mute is kept only as a manual override via
        /// MuteUnderwaterEffectWhileActive (off by default).
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
                submerged = WaterQuery.IsSubmergedAt(
                    Runner.DominantBody, Runner.PlayerPosition, Runner.TimeSeconds, out _);
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
