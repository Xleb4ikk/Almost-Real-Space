using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Объект, прибитый к точке поверхности ТЕЛА: хранит lat/lon/высоту, а не
    /// мировую позицию. Каждый кадр пересчитывает позу через общий с фреймом
    /// путь (SurfaceFrame.RenderPosition/RenderRotation), поэтому едет за
    /// планетой, её вращением и варпом, а в сцене стоит в локальных координатах
    /// километрового масштаба.
    ///
    /// Нужен только объектам ВНЕ SurfaceFrame. То, что лежит ДЕТЬМИ фрейма,
    ///component'а не требует: фрейм переставляет себя, Unity домножает
    /// иерархию, и обычный локальный трансформ ребёнка и есть авторское намерение.
    ///
    /// Чего компонент НЕ делает: мировую позицию в сцене он не хранит и не
    /// выгружает — на 2.6e10 float квантует её до ~2 км, поэтому сохранять там
    /// нечего. Всё, что нужно игре, выводится из lat/lon + смещений.
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

        /// <summary>Живое тело; null, если система не собралась или имя не найдено.</summary>
        public OrbitingBody BodyState { get; private set; }

        /// <summary>Высота рельефа в опорной точке, м.</summary>
        public double GroundHeightMeters =>
            SurfaceSceneSystem.GroundHeight(BodyState, LatitudeDegrees, LongitudeDegrees);

        /// <summary>Высота опорной точки над радиусом тела, м.</summary>
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
            // В Play поза живёт в LateUpdate: якорь игрока и время системы
            // выставляет SimulationRunner.Update, читать их раньше — читать
            // прошлый кадр.
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

        /// <summary>Пересчитать позу принудительно (после правки профиля рельефа).</summary>
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
            // Смещение — в локальных осях фрейма (x=север, y=зенит, z=восток),
            // поэтому поворачивается тем же поворотом, что и сама точка.
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
        /// Доворот ENU-фрейма на наклон нормали рельефа. Нормаль приходит в
        /// астро-кадре, проецируется в базис ENU (SurfaceFrameMath.ToLocal) и
        /// становится обычным Unity-поворотом от зенита — без обратной
        /// quaternion-алгебры в рантайме.
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
