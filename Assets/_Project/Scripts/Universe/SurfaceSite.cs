using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// МЕСТО НА ПЛАНЕТЕ: база, точка задания,anything. Хранит lat/lon и является
    /// корнем содержимого — все объекты места это его дети, с обычными
    /// локальными трансформами.
    ///
    /// Почему место это ПРЕФАБ, а не объект сцены: на планете R = 1143 км и
    /// полуось 2.6e10 м, мировые координаты в сцене не вывозят (float32 на
    /// 2.6e10 даёт шаг ~2 км). Несколько мест в одной сцене невозможно ещё и
    /// потому, что все они оказались бы в одной точке у нуля. Префаб решает
    /// оба: содержимое авторится в нормальном локальном пространстве, lat/lon
    /// едет вместе с префабом, а в сцене одновременно редактируется ОДНО место.
    ///
    /// Рабочий цикл: «Правка → Новое место у игрока» создаёт префаб с
    /// содержимым в точке, где игрок стоял; «Места» в окне показывает список,
    /// переключает на любое и умеет телепортировать игрока туда в Play.
    ///
    /// Место переносит себя в НОЛЬ сцены сам (как SurfaceFrame), поэтому
    /// Scene view показывает ровно то, что автор собрал, с нормальной точностью
    /// координат.
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

        /// <summary>Живое тело места; null, если система не собралась.</summary>
        public OrbitingBody BodyState { get; private set; }

        /// <summary>Живой рельеф; null = гладкая сфера.</summary>
        public HeightfieldTerrain Terrain => BodyState?.Terrain as HeightfieldTerrain;

        /// <summary>Высота рельефа в точке места, м (без смещения).</summary>
        public double GroundHeightMeters => SurfaceSceneSystem.GroundHeight(BodyState, LatitudeDegrees, LongitudeDegrees);

        /// <summary>Высота места над радиусом тела, м.</summary>
        public double AltitudeMeters => SnapToTerrain ? (GroundHeightMeters + HeightOffsetMeters) : HeightOffsetMeters;

        /// <summary>Есть ли под точкой вода (для выбора места под базу).</summary>
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
            Refresh();
        }

        /// <summary>Пересчитать позу и рассадить содержимое по рельефу.</summary>
        [ContextMenu("Refresh site")]
        public void Refresh()
        {
            if (!TryResolve(out OrbitingBody body))
            {
                BodyState = null;
                return;
            }

            BodyState = body;

            // Время: в Play — системное (планета вращается, место обязано ехать
            // вместе с ней), в редакторе — ноль, иначе Update двигал бы объект
            // каждый кадр на 1.9 м/с собственного вращения Terra.
            double time = Application.isPlaying && Runner != null && Runner.SystemState != null
                ? Runner.TimeSeconds
                : 0d;

            // Поза привязана к НУЛЮ сцены, а не к активному SurfaceFrame:
            // место — это рабочая копия одной точки, и его локальные координаты
            // должны быть метровыми всегда (см. SurfaceFrame.OriginAnchoredPose).
            SurfaceFrame.OriginAnchoredPose(
                body, LatitudeDegrees, LongitudeDegrees, AltitudeMeters, time,
                out Vector3 position, out Quaternion rotation);

            ApplyPose(position, rotation);
            SurfaceGrounded.RefreshAll(transform);
        }

        private bool TryResolve(out OrbitingBody body)
        {
            HeightfieldTerrain ignored;
            if (Body != null)
            {
                BodyName = Body.gameObject.name;
                return SurfaceSceneSystem.TryResolve(Body, out body, out ignored);
            }

            if (Application.isPlaying && Runner != null && Runner.SystemState != null)
            {
                foreach (OrbitingBody candidate in Runner.SystemState.AllBodies)
                {
                    if (candidate.Name == BodyName)
                    {
                        body = candidate;
                        return true;
                    }
                }
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

            // Оси: X — север, Y — зенит, Z — восток (совпадает с локальными
            // осями префаба, поэтому «север» читается прямо на сцене).
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
