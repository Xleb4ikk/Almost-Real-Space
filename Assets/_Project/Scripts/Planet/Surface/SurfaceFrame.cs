using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Локальный картографический фрейм на поверхности тела: контейнер для
    /// авторинга базы. Ставит свой GameObject в НОЛЬ сцены и ориентирует его
    /// локальными осями ENU (+X север, +Y зенит, +Z восток), поэтому внутри
    /// фрейма обычные Unity-координаты километрового масштаба с миллиметровой
    /// точностью.
    ///
    /// Зачем. Тело лежит на полуоси 2.6e10 м, где ulp float32 ≈ 2048 м: точку
    /// спавна и объект базы физически нельзя хранить в мировых координатах
    /// сцены. Рантайм это обходит floating origin'ом (якорь = позиция игрока),
    /// и здесь ровно тот же приём, только якорем служит сам фрейм.
    ///
    /// Широта/долгота — ЕДИНСТВЕННЫЙ источник истины о положении фрейма.
    /// Трансформ как смысл не сериализуется: он каждый раз выводится из
    /// lat/lon + высоты, и лежать в сцене может любой (на входе в Play фрейм
    /// прыгает на ~4 м — это якорь игрока, а не ошибка).
    ///
    /// Дети фрейма — это база: обычные GO/префабы с обычными локальными
    /// трансформами, собираемые привычными инструментами сцены. Собственного
    /// обновления им не нужно: фрейм переставляет себя, Unity домножает
    /// иерархию. SurfacePlaced нужен только объектам ВНЕ фрейма.
    ///
    /// [ExecuteAlways] + запись трансформа только при изменении позы: иначе
    /// Update в редакторе помечал бы сцену грязной каждый кадр.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-90)]
    [ExecuteAlways]
    public sealed class SurfaceFrame : MonoBehaviour
    {
        [Header("Тело")]
        [Tooltip("BodyAuthoring тела-поверхности. null = искать по BodyName.")]
        public BodyAuthoring Body;

        [Tooltip("Имя GameObject'а тела, если Body не назначен (ключ связи, как в BodyView).")]
        public string BodyName = "Terra";

        [Header("Точка на поверхности (body-fixed)")]
        [Tooltip("Широта, градусы. Единственный источник истины о положении фрейма.")]
        public double LatitudeDegrees;

        [Tooltip("Долгота от нулевого меридиана тела, градусы (body-fixed, как в GetSurfaceState).")]
        public double LongitudeDegrees;

        [Header("Высота")]
        [Tooltip("Привязать к поверхности: высота фрейма = рельеф в точке + смещение.")]
        public bool SnapToTerrain = true;

        [Tooltip("Высота фрейма над радиусом тела, м (к рельефу при SnapToTerrain, иначе — к опорной сфере).")]
        public double HeightOffsetMeters;

        [Header("Рантайм")]
        [Tooltip("SimulationRunner сцены. Нужен, чтобы фрейм ехал за якорем игрока; без него в Play стоит на месте.")]
        public SimulationRunner Runner;

        [Header("Отладка")]
        [Tooltip("Рисовать оси ENU и масштабные кольца в сцене.")]
        public bool DrawGizmos = true;

        [Tooltip("Радиусы колец-рулетки, м.")]
        public double[] ScaleRingsMeters = { 10d, 100d, 1000d, 10000d };

        /// <summary>Абсолютная астропозиция фрейма (double, астро-кадр) на последнем пересчёте.</summary>
        public Vector3d AnchorAstro { get; private set; }

        /// <summary>Body-fixed позиция фрейма (без орбиты и собственного вращения) — для превью рельефа.</summary>
        public Vector3d AnchorBodyFixed { get; private set; }

        /// <summary>Живое тело, на котором стоит фрейм; null, если система не собралась.</summary>
        public OrbitingBody BodyState { get; private set; }

        /// <summary>Живой рельеф тела фрейма; null = гладкая сфера.</summary>
        public HeightfieldTerrain Terrain => BodyState?.Terrain as HeightfieldTerrain;

        /// <summary>Радиус тела фрейма, м.</summary>
        public double BodyRadius => BodyState != null ? BodyState.Radius : 0d;

        /// <summary>Высота рельефа в точке фрейма, м (без смещения).</summary>
        public double GroundHeightMeters => SurfaceSceneSystem.GroundHeight(BodyState, LatitudeDegrees, LongitudeDegrees);

        /// <summary>Высота фрейма над радиусом тела, м — то, что уходит в GetSurfaceState.</summary>
        public double AltitudeMeters =>
            SnapToTerrain ? (GroundHeightMeters + HeightOffsetMeters) : HeightOffsetMeters;

        private bool poseValid;
        private readonly int[] gizmoRingSegments = new int[12];

        /// <summary>
        /// Абсолютная астро-позиция, принятая за НОЛЬ локального пространства в
        /// редакторе. Её выставляет активный фрейм; всё остальное (SurfacePlaced,
        /// превью) вычитает её из своей абсолютной позиции и получает километровые
        /// координаты рядом с фреймом вместо 2.6e10. Ноль означает «фрейма нет» —
        /// тогда объекты остаются в астро-координатах, что честно видно по
        /// инспектору.
        /// </summary>
        public static Vector3d EditAnchorAstro { get; set; }

        /// <summary>
        /// Фрейм, задающий ноль редактора: первый SurfaceFrame в сцене.
        /// Резолвится с кэшем на 0.5 с — иначе FindAnyObjectByType дёргался бы
        /// на каждый Update каждого SurfacePlaced в сцене.
        /// </summary>
        public static SurfaceFrame ActiveEditFrame()
        {
            if (Application.isPlaying)
            {
                return null;
            }

            if (cachedFrame != null
                && cachedFrame.IsUsable
                && Time.realtimeSinceStartup - cachedFrameTime < 0.5d)
            {
                return cachedFrame;
            }

            SurfaceFrame[] all = FindObjectsByType<SurfaceFrame>(FindObjectsInactive.Include);
            cachedFrame = all.Length > 0 ? all[0] : null;
            cachedFrameTime = Time.realtimeSinceStartup;
            if (cachedFrame == null)
            {
                EditAnchorAstro = Vector3d.Zero;
            }

            return cachedFrame;
        }

        private static SurfaceFrame cachedFrame;
        private static float cachedFrameTime;

        /// <summary>Фрейм пригоден для превью и потомков: активен и тело найдено.</summary>
        public bool IsUsable => isActiveAndEnabled && BodyState != null;

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

        /// <summary>Пересчитать позу принудительно (после правки профиля рельефа в инспекторе).</summary>
        [ContextMenu("Refresh frame")]
        public void Refresh()
        {
            if (!TryResolve(out OrbitingBody body))
            {
                BodyState = null;
                return;
            }

            BodyState = body;

            // Время: в Play — системное (планета вращается, фрейм обязан ехать
            // вместе с ней), в редакторе — ноль, иначе Update двигал бы фрейм
            // каждый кадр на 1.9 м/с собственного вращения Terra.
            double time = Application.isPlaying && Runner != null && Runner.SystemState != null
                ? Runner.TimeSeconds
                : 0d;

            double lat = KeplerMath.DegreesToRadians(LatitudeDegrees);
            double lon = KeplerMath.DegreesToRadians(LongitudeDegrees);
            Vector3d bodyFixed = SurfaceFrameMath.Up(lat, lon) * (body.Radius + AltitudeMeters);
            AnchorBodyFixed = bodyFixed;

            QuaternionD orientation = body.GetVisualOrientation(time);
            QuaternionD renderRotation = (orientation * SurfaceFrameMath.Rotation(lat, lon)).Normalized;

            if (Application.isPlaying)
            {
                // Рантайм: точка в абсолютном астро-мире (floating origin), фрейм
                // встаёт в ToRender от неё — рядом с игроком, а не на 2.6e10.
                body.GetSurfaceState(LatitudeDegrees, LongitudeDegrees, AltitudeMeters, time,
                    out Vector3d surfacePos, out _);
                AnchorAstro = surfacePos;
                ApplyPose(
                    FloatingOrigin.ToRender(surfacePos),
                    FloatingOrigin.RenderRotation(renderRotation));
                return;
            }

            // Редактор: первый фрейм сцены становится нулём локального
            // пространства. Остальные фреймы (если их несколько) встают не в
            // ноль, а на своё честное смещение от него — так локальные координаты
            // общие для всей сцены и ничего не «прыгает» от выделения в иерархии.
            body.EvaluateWorldState(time, out Vector3d bodyPos, out _);
            AnchorAstro = bodyPos + orientation.Rotate(bodyFixed);
            if (ReferenceEquals(ActiveEditFrame(), this))
            {
                EditAnchorAstro = AnchorAstro;
            }

            ApplyPose(
                AstroFrame.ToSimulation(AnchorAstro - EditAnchorAstro),
                AstroFrame.ToSimulation(renderRotation));
        }

        /// <summary>
        /// Позиция точки поверхности в render-пространстве. ЕДИНАЯ точка для
        /// фрейма, SurfacePlaced и превью: одна математика — одно расхождение
        /// невозможно. В Play — от floating origin (рядом с игроком), в
        /// редакторе — от фрейма (ноль сцены), поэтому координаты километровые.
        /// </summary>
        public static Vector3 RenderPosition(
            OrbitingBody body, double latitudeDegrees, double longitudeDegrees,
            double altitudeMeters, double timeSeconds, out Vector3d surfaceAstro)
        {
            body.GetSurfaceState(latitudeDegrees, longitudeDegrees, altitudeMeters, timeSeconds,
                out surfaceAstro, out _);
            return Application.isPlaying
                ? FloatingOrigin.ToRender(surfaceAstro)
                : AstroFrame.ToSimulation(surfaceAstro - EditAnchorAstro);
        }

        /// <summary>
        /// Поза точки поверхности, ЯВНО привязанная к нулю сцены — в отличие от
        /// RenderPosition, который в редакторе считает от активного SurfaceFrame.
        ///
        /// Нужна местам (SurfaceSite). Место — это редакторская рабочая копия
        /// одной точки: оно всегда должно стоять в нуле, иначе lat/lon, далёкие
        /// от фрейма сцены, дают километровые координаты. При R = 1143 км хорда
        /// между точками достигает 2.29e6 м, а float32 на таком масштабе даёт
        /// шаг 0.25 м — и на расстоянии в тысячи километров шаг переваливает за
        /// метры, то есть база в Scene view «рассыпается». Ноль сцены снимает
        /// вопрос: локальные координаты места всегда метровые.
        ///
        /// В Play ведёт себя как RenderPosition — едет за якорем игрока.
        /// </summary>
        public static void OriginAnchoredPose(
            OrbitingBody body, double latitudeDegrees, double longitudeDegrees,
            double altitudeMeters, double timeSeconds, out Vector3 position, out Quaternion rotation)
        {
            body.GetSurfaceState(latitudeDegrees, longitudeDegrees, altitudeMeters, timeSeconds,
                out Vector3d surfaceAstro, out _);

            rotation = RenderRotation(body, latitudeDegrees, longitudeDegrees, timeSeconds);
            position = Application.isPlaying
                ? FloatingOrigin.ToRender(surfaceAstro)
                : Vector3.zero;
        }

        /// <summary>Ориентация локальных осей ENU точки поверхности в render-пространстве.</summary>
        public static Quaternion RenderRotation(
            OrbitingBody body, double latitudeDegrees, double longitudeDegrees, double timeSeconds)
        {
            QuaternionD orientation = (
                body.GetVisualOrientation(timeSeconds)
                * SurfaceFrameMath.Rotation(
                    KeplerMath.DegreesToRadians(latitudeDegrees),
                    KeplerMath.DegreesToRadians(longitudeDegrees))).Normalized;
            return Application.isPlaying
                ? FloatingOrigin.RenderRotation(orientation)
                : AstroFrame.ToSimulation(orientation);
        }

        /// <summary>Body-fixed позиция точки поверхности (без орбиты и вращения) — вход для превью.</summary>
        public static Vector3d BodyFixedPoint(OrbitingBody body, double latitudeDegrees, double longitudeDegrees, double altitudeMeters)
        {
            return SurfaceFrameMath.Up(
                KeplerMath.DegreesToRadians(latitudeDegrees),
                KeplerMath.DegreesToRadians(longitudeDegrees)) * (body.Radius + altitudeMeters);
        }

        private bool TryResolve(out OrbitingBody body)
        {
            HeightfieldTerrain ignored;

            // В Play тело берём из ЖИВОЙ системы раннера: она уже собрана и
            // точна. Редакторный кэш SurfaceSceneSystem там вообще не нужен и
            // только мешает (в Play он собирается из сцены, которая ещё
            // догружается, и кэш может остаться неполным).
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
                // Каждый кадр: якорь игрока едет, едет и фрейм.
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
            if (!DrawGizmos || (!Application.isPlaying && BodyState == null))
            {
                return;
            }

            // Локальные оси = ENU фрейма, рисуем в его системе.
            const float axis = 12f;
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

            // Кольца-рулетка: «сантиметр, метр, сотня, километр» в локальных
            // единицах. Читаются как масштаб и служат страховкой: если превью
            // не построилось или камера ушла, по кольцам всё равно видно, где
            // фрейм и какого он размера.
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            for (int i = 0; i < ScaleRingsMeters.Length; i++)
            {
                float r = (float)ScaleRingsMeters[i];
                if (r <= 0f || r > 1e7f)
                {
                    continue;
                }

                int segments = RingSegments(r);
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

        private int RingSegments(float radius)
        {
            int slot = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(radius) / Mathf.Log(10f)) + 11, 0, gizmoRingSegments.Length - 1);
            if (gizmoRingSegments[slot] == 0)
            {
                // ~4 м на сегмент, но не меньше 64 и не больше 512.
                gizmoRingSegments[slot] = Mathf.Clamp(Mathf.CeilToInt(radius / 4f), 64, 512);
            }

            return gizmoRingSegments[slot];
        }
    }
}
