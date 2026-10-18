using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// A SITE ON THE PLANET: a base, a quest point, anything. It stores lat/lon and
    /// is the root of the content — every site object is its child, with ordinary
    /// local transforms.
    ///
    /// Why a site is a PREFAB and not a scene object: on a planet R = 1143 km with a
    /// semi-major axis of 2.6e10 m, world coordinates in the scene cannot cope
    /// (float32 at 2.6e10 has a step of ~2 km). Several sites in one scene are also
    /// impossible because they would all end up at one point near zero. A prefab
    /// solves both: the content is authored in a normal local space, lat/lon travels
    /// with the prefab, and only ONE site is edited in the scene at a time.
    ///
    /// Workflow: "Edit → New site at player" creates a prefab with the content at
    /// the point where the player stood; "Sites" in the window lists them, switches
    /// to any and can teleport the player there in Play.
    ///
    /// The site moves itself to the scene ZERO (like SurfaceFrame), so the Scene
    /// view shows exactly what the author built, with normal coordinate precision.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-90)]
    [ExecuteAlways]
    public sealed class SurfaceSite : MonoBehaviour
    {
        [Header("Идентификация")]
        [Tooltip("Имя места — для списка, заданий и отладки.")]
        public string SiteName = "Место";

        [TextArea(2, 4)]
        [Tooltip("Заметка: что тут происходит, для чего место.")]
        public string Purpose = string.Empty;

        [Header("Где на планете (body-fixed)")]
        [Tooltip("BodyAuthoring тела. null = искать по BodyName.")]
        public BodyAuthoring Body;

        [Tooltip("Имя GameObject'а тела, если Body не назначен.")]
        public string BodyName = "Terra";

        [Tooltip("Широта, градусы. Единственный источник истины о положении места.")]
        public double LatitudeDegrees;

        [Tooltip("Долгота от нулевого меридиана тела, градусы.")]
        public double LongitudeDegrees;

        [Header("Высота")]
        [Tooltip("Привязать к поверхности: высота = рельеф в точке + смещение.")]
        public bool SnapToTerrain = true;

        [Tooltip("Смещение над рельефом, м. Отрицательное — утопить фундамент.")]
        public double HeightOffsetMeters;

        [Header("Рантайм")]
        [Tooltip("SimulationRunner сцены. Нужен, чтобы место ехало за якорем игрока.")]
        public SimulationRunner Runner;

        [Header("Отладка")]
        [Tooltip("Кольца-рулетка масштаба в сцене, м.")]
        public double[] ScaleRingsMeters = { 1d, 10d, 100d };

        /// <summary>Live site body; null if the system failed to build.</summary>
        public OrbitingBody BodyState { get; private set; }

        /// <summary>Live terrain; null = smooth sphere.</summary>
        public HeightfieldTerrain Terrain => BodyState?.Terrain as HeightfieldTerrain;

        /// <summary>Terrain height at the site point, m (without offset).</summary>
        public double GroundHeightMeters => SurfaceSceneSystem.GroundHeight(BodyState, LatitudeDegrees, LongitudeDegrees);

        /// <summary>Site altitude above the body radius, m.</summary>
        public double AltitudeMeters => SnapToTerrain ? (GroundHeightMeters + HeightOffsetMeters) : HeightOffsetMeters;

        /// <summary>Whether there is water under the point (for choosing a base site).</summary>
        public bool IsWater => BodyState != null && WaterQuery.IsWaterAt(BodyState, LatitudeDegrees, LongitudeDegrees);

        private bool poseValid;
        private readonly int[] ringSegments = new int[12];

        private void OnEnable()
        {
            poseValid = false;
            Refresh();
        }

        private void OnValidate()
        {
            poseValid = false;
        }

        private void Update()
        {
            // In Play this is LateUpdate: the time and player anchor are set by
            // SimulationRunner.Update (order 0), while the site is declared with -90
            // and in Update read the previous frame. Same as in SurfacePlaced.
            if (!Application.isPlaying)
            {
                Refresh();
            }
        }

        private void LateUpdate()
        {
            if (Application.isPlaying)
            {
                Refresh();
            }
        }

        /// <summary>Recompute the pose and seat the content onto the terrain.</summary>
        [ContextMenu("Refresh site")]
        public void Refresh()
        {
            if (Runner == null && Application.isPlaying)
            {
                Runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            }

            if (!TryResolve(out OrbitingBody body))
            {
                BodyState = null;
                return;
            }

            BodyState = body;

            // Time: in Play the system time (the planet rotates, the site must move
            // with it), in the editor zero — otherwise Update would move the object
            // every frame by 1.9 m/s of Terra's own rotation.
            double time = Application.isPlaying && Runner != null && Runner.SystemState != null
                ? Runner.TimeSeconds
                : 0d;

            // The pose is anchored to the scene ZERO, not to the active SurfaceFrame:
            // a site is a working copy of one point, and its local coordinates must
            // always be metres (see SurfaceFrame.OriginAnchoredPose).
            SurfaceFrame.OriginAnchoredPose(
                body, LatitudeDegrees, LongitudeDegrees, AltitudeMeters, time,
                out Vector3 position, out Quaternion rotation);

            ApplyPose(position, rotation);
            SurfaceGrounded.RefreshAll(transform);
        }

        private bool TryResolve(out OrbitingBody body)
        {
            HeightfieldTerrain ignored;

            // In Play the body comes from the runner's LIVE system (Runner is found
            // in Refresh), as SurfaceFrame does: the SurfaceSceneSystem editor cache
            // in Play is built with a different signature and for time 0, so the site
            // slips out from under the player. The Body != null branch comes AFTER
            // it — otherwise the assigned body would lead back into the cache.
            if (Application.isPlaying && Runner != null && Runner.SystemState != null)
            {
                string wanted = Body != null ? Body.gameObject.name : BodyName;
                foreach (OrbitingBody candidate in Runner.SystemState.AllBodies)
                {
                    if (candidate.Name == wanted)
                    {
                        body = candidate;
                        return true;
                    }
                }

                body = null;
                return false;
            }

            if (Body != null)
            {
                BodyName = Body.gameObject.name;
                return SurfaceSceneSystem.TryResolve(Body, out body, out ignored);
            }

            return SurfaceSceneSystem.TryResolve(BodyName, out body, out ignored);
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            if (Application.isPlaying)
            {
                transform.SetPositionAndRotation(position, rotation);
                // Part colliders move with the site every frame, and Physics.autoSyncTransforms
                // is off: without syncing, the player support queries (rays/capsules of
                // SiteBoxSupport in Update) would see previous-frame poses.
                Physics.SyncTransforms();
                poseValid = true;
                return;
            }

            if (poseValid
                && (transform.position - position).sqrMagnitude <= 1e-12f
                && UnityEngine.Quaternion.Angle(transform.rotation, rotation) <= 1e-3f)
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
            poseValid = true;
        }

        private void OnDrawGizmos()
        {
            if (BodyState == null)
            {
                return;
            }

            // Axes: X north, Y zenith, Z east (matches the prefab's local axes, so
            // "north" reads directly in the scene).
            const float axis = 8f;
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(axis, 0f, 0f));
            Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, axis, 0f));
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0f, axis));

            if (ScaleRingsMeters == null)
            {
                return;
            }

            Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
            for (int i = 0; i < ScaleRingsMeters.Length; i++)
            {
                float r = (float)ScaleRingsMeters[i];
                if (r <= 0f || r > 1e6f)
                {
                    continue;
                }

                int segments = Segments(r);
                Vector3 prev = new Vector3(r, 0f, 0f);
                for (int s = 1; s <= segments; s++)
                {
                    float a = (s / (float)segments) * Mathf.PI * 2f;
                    Vector3 next = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }
            }
        }

        private int Segments(float radius)
        {
            int slot = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(radius) / Mathf.Log(10f)) + 11, 0, ringSegments.Length - 1);
            if (ringSegments[slot] == 0)
            {
                ringSegments[slot] = Mathf.Clamp(Mathf.CeilToInt(radius * 2f), 48, 512);
            }

            return ringSegments[slot];
        }
    }
}
