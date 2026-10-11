using System.Collections.Generic;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// СТАРТОВАЯ ПЛОЩАДКА как зона строительства: вешается на объект
    /// «Площадка стартовая» (SurfaceRoad-полигон) в префабе места. Даёт:
    ///   • полигон пятна (оси места: x — север, z — восток) для проверок границ;
    ///   • верх плиты в точке (луч в MeshCollider дороги);
    ///   • перевод осей места ↔ render ↔ double-мир (astro) со скоростью кадра;
    ///   • точку спавна аппарата (центр пятна) и игрока (рядом).
    ///
    /// Точки полигона читаются из самого SurfaceRoad (единственный источник),
    /// поэтому правка формы площадки в редакторе автоматически правит зону.
    /// В Play поза места обновляется SurfaceSite каждый кадр — зона просто
    /// едет в его иерархии (локальные координаты плиты не меняются).
    /// </summary>
    [DefaultExecutionOrder(-120)]
    public sealed class LaunchPadZone : MonoBehaviour
    {
        [Tooltip("Дорога-плита (SurfaceRoad с Shape = Polygon). Пусто — берётся на этом же объекте.")]
        public SurfaceRoad Pad;

        [Tooltip("SimulationRunner сцены. Пусто — найдётся сам при старте.")]
        public SimulationRunner Runner;

        [Tooltip("Смещение игрока от центра площадки вдоль востока, м (спавн рядом с аппаратом).")]
        public float PlayerSpawnEastOffset = 10f;

        /// <summary>Полигон плиты в осях места: x — север, y — восток.</summary>
        private readonly List<Vector2> polygon = new List<Vector2>();

        /// <summary>Полигон плиты (x — север, y — восток). Пусто, пока не готово.</summary>
        public IReadOnlyList<Vector2> Polygon => polygon;

        /// <summary>Центр пятна в осях места (XZ; Y — верх плиты).</summary>
        public Vector3 CenterLocal { get; private set; }

        /// <summary>Точка постановки аппарата (оси места).</summary>
        public Vector3 VesselSpawnLocal { get; private set; }

        /// <summary>Точка спавна игрока (оси места).</summary>
        public Vector3 PlayerSpawnLocal { get; private set; }

        /// <summary>Всё собрано: тело места разрешено, полигон и высоты посчитаны.</summary>
        public bool Ready { get; private set; }

        /// <summary>Коллайдер плиты (MeshCollider дороги) — по нему лучи постановки.</summary>
        public Collider PadCollider => padCollider;

        private SurfaceSite site;
        private Collider padCollider;
        private OrbitingBody body;

        /// <summary>Готовность к работе; неготовый Update зовёт Refresh сам.</summary>
        private void Update()
        {
            if (!Ready)
            {
                Refresh();
            }
        }

        /// <summary>Собрать полигон, коллайдер плиты и точки спавна.</summary>
        [ContextMenu("Refresh build zone")]
        public void Refresh()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (Pad == null)
            {
                Pad = GetComponent<SurfaceRoad>();
            }

            if (site == null)
            {
                site = GetComponentInParent<SurfaceSite>();
            }

            if (Pad == null || site == null || Runner == null || Runner.SystemState == null)
            {
                return;
            }

            // Живое тело места: в Play SurfaceSite первым кадром его разрешает.
            body = site.BodyState;
            if (body == null)
            {
                return;
            }

            // Плита должна быть построена (её меш и коллайдер), а поза места —
            // осмысленной: первые кадры место может стоять в астро-координатах
            // (якорь рендера ещё не выставлен), и лучевая высота в таких
            // координатах — мусор в километры. Ждём и пробуем снова.
            if (!Pad.IsBuilt)
            {
                return;
            }

            if (site.transform.position.magnitude > 1e6f)
            {
                return;
            }

            // Полигон: точки дороги в локальных осях места (x — север, z — восток).
            polygon.Clear();
            List<Vector3> points = Pad.Points;
            if (points == null || points.Count < 3)
            {
                return;
            }

            float minN = float.MaxValue, maxN = float.MinValue;
            float minE = float.MaxValue, maxE = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                var p = new Vector2(points[i].x, points[i].z);
                polygon.Add(p);
                minN = Mathf.Min(minN, p.x);
                maxN = Mathf.Max(maxN, p.x);
                minE = Mathf.Min(minE, p.y);
                maxE = Mathf.Max(maxE, p.y);
            }

            padCollider = GetComponent<Collider>();
            var center = new Vector2((minN + maxN) * 0.5f, (minE + maxE) * 0.5f);
            if (!TryPadTopYAt(center, out float centerY))
            {
                return;
            }

            var playerPoint = new Vector2(center.x, center.y + PlayerSpawnEastOffset);
            if (!TryPadTopYAt(playerPoint, out float playerY))
            {
                return;
            }

            CenterLocal = new Vector3(center.x, centerY, center.y);
            VesselSpawnLocal = CenterLocal;
            PlayerSpawnLocal = new Vector3(playerPoint.x, playerY, playerPoint.y);
            Ready = true;
        }

        /// <summary>
        /// Высота ВЕРХНЕЙ поверхности места в точке: самый верхний коллайдер
        /// МЕСТА (плита ИЛИ стартовый стол — модель стола стоит на плите и
        /// выше неё; спавн/пиннинг обязаны садиться на верх, иначе аппарат
        /// появляется внутри коллайдеров стола, и PhysX выбрасывает его рывком).
        /// </summary>
        private bool TryPadTopYAt(Vector2 northEast, out float y)
        {
            y = 0f;
            Vector3 origin = transform.TransformPoint(new Vector3(northEast.x, 60f, northEast.y));
            RaycastHit[] hits = Physics.RaycastAll(
                new Ray(origin, -transform.up), 200f, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
            {
                return false;
            }

            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (!IsSiteCollider(hits[i].collider))
                {
                    continue;
                }

                if (hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    best = i;
                }
            }

            if (best < 0)
            {
                return false;
            }

            y = transform.InverseTransformPoint(hits[best].point).y;
            return true;
        }

        /// <summary>Коллайдер принадлежит месту (плита, стол, постройки) — по нему можно ставить.</summary>
        public static bool IsSiteCollider(Collider collider)
        {
            return collider != null && collider.GetComponentInParent<SurfaceSite>() != null;
        }

        /// <summary>Высота верхней поверхности места в точке осей места (плита или стартовый стол).</summary>
        public float PadTopYAt(Vector2 northEast)
        {
            if (TryPadTopYAt(northEast, out float y))
            {
                return y;
            }

            // Ничего не нашлось (точка вне построек): честная опора — рельеф под точкой.
            if (body != null)
            {
                Vector3 render = transform.TransformPoint(new Vector3(northEast.x, 0f, northEast.y));
                Vector3d astro = FloatingOrigin.Anchor + AstroFrame.ToAstro(render);
                body.SurfaceLatLonAt(astro, Runner != null ? Runner.TimeSeconds : 0d, out double lat, out double lon);
                double ground = body.Terrain != null
                    ? body.Terrain.GetHeightMeters(body, KeplerMath.DegreesToRadians(lat), KeplerMath.DegreesToRadians(lon))
                    : 0d;
                body.EvaluateWorldState(Runner != null ? Runner.TimeSeconds : 0d, out Vector3d bodyPos, out _);
                Vector3d up = (astro - bodyPos).Normalized;
                Vector3d groundPoint = bodyPos + (up * (body.Radius + ground));
                return transform.InverseTransformPoint(FloatingOrigin.ToRender(groundPoint)).y;
            }

            return 0f;
        }

        // ─── Переводы координат ──────────────────────────────────────────────

        /// <summary>Оси места → render-пространство (для лучей и визуала).</summary>
        public Vector3 SiteToRender(Vector3 local)
        {
            return transform.TransformPoint(local);
        }

        /// <summary>Render-пространство → оси места.</summary>
        public Vector3 RenderToSite(Vector3 render)
        {
            return transform.InverseTransformPoint(render);
        }

        /// <summary>Поворот осей места в render-пространстве.</summary>
        public Quaternion SiteRenderRotation => transform.rotation;

        /// <summary>
        /// Оси места → double-мир (астро-кадр) со скоростью места в точке.
        /// Точность: тело-фиксированное смещение малым кватернионом (в пределах
        /// площадки это доли миллиметра), поверх — точная точка места из ядра.
        /// </summary>
        public void SiteToAstro(Vector3 local, out Vector3d astro, out Vector3d velocity)
        {
            SiteToAstroAt(local, Runner != null ? Runner.TimeSeconds : 0d, out astro, out velocity);
        }

        /// <summary>
        /// То же, но на заданное время. Важно для пиннинга аппарата в стройке:
        /// время обязано совпадать с временем кадра игрока (иначе при быстром
        /// вращении планеты аппарат отстаёт от плиты на скорость_поверхности·dt).
        /// </summary>
        public void SiteToAstroAt(Vector3 local, double timeSeconds, out Vector3d astro, out Vector3d velocity)
        {
            double t = timeSeconds;
            double lat = site.LatitudeDegrees;
            double lon = site.LongitudeDegrees;
            double latRad = KeplerMath.DegreesToRadians(lat);
            double lonRad = KeplerMath.DegreesToRadians(lon);

            body.GetSurfaceState(lat, lon, site.AltitudeMeters, t, out Vector3d origin, out _);

            QuaternionD orientation = body.GetVisualOrientation(t);
            QuaternionD simRotation = SurfaceFrameMath.SimulationRotation(orientation, latRad, lonRad);
            Vector3 renderOffset = AstroFrame.SimulationQuaternion(simRotation) * local;
            astro = origin + AstroFrame.ToAstro(renderOffset);

            body.EvaluateWorldState(t, out Vector3d bodyPos, out Vector3d bodyVel);
            Vector3d omega = body.SpinAxis * body.SpinAngularSpeed;
            velocity = bodyVel + Vector3d.Cross(omega, astro - bodyPos);
        }

        private void OnDrawGizmosSelected()
        {
            SurfaceRoad road = Pad != null ? Pad : GetComponent<SurfaceRoad>();
            if (road == null || road.Points == null || road.Points.Count < 3)
            {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            for (int i = 0; i < road.Points.Count; i++)
            {
                Vector3 a = road.Points[i];
                Vector3 b = road.Points[(i + 1) % road.Points.Count];
                Gizmos.DrawLine(transform.TransformPoint(a), transform.TransformPoint(b));
            }
        }
    }
}
