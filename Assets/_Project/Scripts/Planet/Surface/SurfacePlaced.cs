using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Object pinned to a point on the BODY surface: it stores lat/lon/altitude
    /// rather than a world position. Every frame it recomputes its pose through the
    /// path shared with the frame (SurfaceFrame.RenderPosition/RenderRotation), so
    /// it follows the planet, its rotation and warp, and stands in the scene in
    /// kilometre-scale local coordinates.
    ///
    /// Needed only for objects OUTSIDE a SurfaceFrame. Anything that is a CHILD of
    /// the frame needs no component: the frame moves itself, Unity multiplies the
    /// hierarchy, and the child's ordinary local transform is the authoring intent.
    ///
    /// What the component does NOT do: it neither stores nor saves a world position —
    /// at 2.6e10 float quantizes it to ~2 km, so there is nothing to save. Everything
    /// the game needs is derived from lat/lon + offsets.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-85)]
    [ExecuteAlways]
    public sealed class SurfacePlaced : MonoBehaviour
    {
        [Header("Тело")]
        [Tooltip("BodyAuthoring тела-поверхности. null = искать по BodyName.")]
        public BodyAuthoring Body;

        [Tooltip("Имя GameObject'а тела, если Body не назначен (ключ связи, как в BodyView).")]
        public string BodyName = "Terra";

        [Header("Точка на поверхности (body-fixed)")]
        public double LatitudeDegrees;
        public double LongitudeDegrees;

        [Header("Высота")]
        [Tooltip("Привязать к поверхности: высота = рельеф в точке + смещение.")]
        public bool SnapToTerrain = true;

        [Tooltip("Высота над радиусом тела, м (к рельефу при SnapToTerrain, иначе — к опорной сфере).")]
        public double HeightOffsetMeters;

        [Tooltip("Развернуть объект по нормали рельефа (для скаль/наклонных площадок).")]
        public bool AlignToSurfaceNormal;

        [Tooltip("Доворот объекта вокруг собственной вертикали, градусы (0 = по северу, как +X фрейма). Ориентацию меняет, положение — нет: смещение всегда в осях фрейма.")]
        public double LocalYawDegrees;

        [Header("Смещение от точки, м (локальные оси ENU: X север, Y зенит, Z восток)")]
        public double OffsetNorthMeters;
        public double OffsetUpMeters;
        public double OffsetEastMeters;

        [Header("Рантайм")]
        [Tooltip("SimulationRunner сцены. Нужен, чтобы объект ехал за якорем игрока и временем системы.")]
        public SimulationRunner Runner;

        private bool poseValid;

        /// <summary>Live body; null if the system failed to build or the name was not found.</summary>
        public OrbitingBody BodyState { get; private set; }

        /// <summary>Terrain height at the reference point, m.</summary>
        public double GroundHeightMeters =>
            SurfaceSceneSystem.GroundHeight(BodyState, LatitudeDegrees, LongitudeDegrees);

        /// <summary>Altitude of the reference point above the body radius, m.</summary>
        public double AltitudeMeters =>
            SnapToTerrain ? (GroundHeightMeters + HeightOffsetMeters) : HeightOffsetMeters;

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
            // In Play the pose lives in LateUpdate: the player anchor and system time
            // are set by SimulationRunner.Update, reading them earlier reads the
            // previous frame.
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

        /// <summary>Force a pose refresh (after editing the terrain profile).</summary>
        [ContextMenu("Refresh placement")]
        public void Refresh()
        {
            if (!TryResolve(out OrbitingBody body))
            {
                BodyState = null;
                return;
            }

            BodyState = body;

            double time = Application.isPlaying && Runner != null && Runner.SystemState != null
                ? Runner.TimeSeconds
                : 0d;

            double lat = KeplerMath.DegreesToRadians(LatitudeDegrees);
            double lon = KeplerMath.DegreesToRadians(LongitudeDegrees);

            Vector3 position = SurfaceFrame.RenderPosition(
                body, LatitudeDegrees, LongitudeDegrees, AltitudeMeters, time, out _);

            Quaternion rotation = SurfaceFrame.RenderRotation(body, LatitudeDegrees, LongitudeDegrees, time);
            // The offset is in the frame's local axes (x=north, y=zenith, z=east),
            // so it is rotated by the same rotation as the point itself.
            Vector3d frameLocal = SurfaceFrameMath.EnuToFrameLocal(
                OffsetEastMeters, OffsetNorthMeters, OffsetUpMeters);
            position += rotation * new Vector3(
                (float)frameLocal.X, (float)frameLocal.Y, (float)frameLocal.Z);

            if (AlignToSurfaceNormal)
            {
                rotation = ComposeNormalAligned(body, rotation, time);
            }

            rotation = rotation * Quaternion.AngleAxis((float)LocalYawDegrees, Vector3.up);
            ApplyPose(position, rotation);
        }

        /// <summary>
        /// Tilts the ENU frame by the terrain normal slope. The normal arrives in
        /// the astro frame, is projected into the ENU basis (SurfaceFrameMath.ToLocal)
        /// and becomes an ordinary Unity rotation from zenith — with no inverse
        /// quaternion algebra at runtime.
        /// </summary>
        private Quaternion ComposeNormalAligned(OrbitingBody body, Quaternion enuRotation, double time)
        {
            double lat = KeplerMath.DegreesToRadians(LatitudeDegrees);
            double lon = KeplerMath.DegreesToRadians(LongitudeDegrees);
            Vector3d normalAstro = SurfaceFrameMath.Up(lat, lon);

            HeightfieldTerrain terrain = body.Terrain as HeightfieldTerrain;
            if (terrain != null)
            {
                normalAstro = terrain.GetOutwardNormal(
                    body, normalAstro * (body.Radius + AltitudeMeters), time);
            }

            Vector3d local = SurfaceFrameMath.ToLocal(lat, lon, normalAstro.Normalized);
            Vector3 to = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
            if (to.sqrMagnitude <= 1e-12f)
            {
                return enuRotation;
            }

            return enuRotation * Quaternion.FromToRotation(Vector3.up, to.normalized);
        }

        private bool TryResolve(out OrbitingBody body)
        {
            HeightfieldTerrain ignored;
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
                poseValid = true;
                return;
            }

            if (poseValid
                && (transform.position - position).sqrMagnitude <= 1e-12f
                && Quaternion.Angle(transform.rotation, rotation) <= 1e-3f)
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
            poseValid = true;
        }

        private void OnDrawGizmos()
        {
            if (!DrawMarker())
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawLine(transform.position, transform.position + (transform.up * 3f));
        }

        private bool DrawMarker() => isActiveAndEnabled;
    }
}
