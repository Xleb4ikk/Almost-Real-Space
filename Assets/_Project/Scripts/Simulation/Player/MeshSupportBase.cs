using System.Collections.Generic;
using Galilego.Building;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Общая механика опоры игрока по PhysX-коллайдерам в render-пространстве:
    /// Floor — лучи вниз от точки ног (5 проб по следу стопы), Ceiling — 5 проб
    /// по следу головы вверх, Sweep — горизонтальный CapsuleCast со скольжением
    /// вдоль стен. Наследники решают, какие коллайдеры считать опорой (постройки,
    /// детали корабля) и как выдавать стабильные sourceId.
    /// </summary>
    public abstract class MeshSupportBase : IPlayerSupport
    {
        /// <summary>Диагностика ходьбы: включена галочкой Debug Overlay на PlayerController.</summary>
        public static bool DebugEnabled;

        /// <summary>Последний заблокировавший свип коллайдер (для оверлея диагностики).</summary>
        public static string DebugBlocker = "-";

        protected SiteFrame frame;
        protected Vector3 renderOrigin;
        protected Vector3 renderUp;
        protected Vector3 renderEast;
        protected Vector3 renderNorth;
        protected bool ready;

        /// <summary>О невыпуклых коллайдерах предупреждаем один раз на коллайдер.</summary>
        private readonly HashSet<Collider> warnedNonConvex = new HashSet<Collider>();

        /// <summary>Кэш «коллайдер — только препятствие» (мебель, мониторы): не пол и не ступенька.</summary>
        private readonly Dictionary<Collider, bool> obstacleOnly = new Dictionary<Collider, bool>();

        private bool IsObstacleOnly(Collider collider)
        {
            if (collider == null) return false;
            if (!obstacleOnly.TryGetValue(collider, out bool value))
            {
                if (obstacleOnly.Count > 512) obstacleOnly.Clear();
                value = collider.GetComponent<PlayerObstacleOnly>() != null;
                obstacleOnly[collider] = value;
            }
            return value;
        }

        /// <summary>Коллайдер принадлежит опоре этого источника.</summary>
        protected abstract bool IsSupportCollider(Collider collider);

        /// <summary>Стабильный идентификатор коллайдера-опоры (sourceId).</summary>
        protected abstract int SourceIdFor(Collider collider);

        public virtual void SetFrame(SiteFrame siteFrame)
        {
            // Коллайдеры двигаются в LateUpdate вместе с floating origin, а
            // Physics.autoSyncTransforms выключен: без синхронизации запросы опоры
            // (лучи/капсулы в Update) видели бы позы прошлых кадров — ошибка равна
            // смещению игрока с момента последней синхронизации (единицы см,
            // зависит от FPS — отсюда нестабильность багов «застрял/прошёл сквозь»).
            Physics.SyncTransforms();
            frame = siteFrame;
            renderOrigin = ToRender(Vector3d.Zero);
            renderUp = Direction(new Vector3d(0d, 0d, 1d));
            renderEast = Direction(new Vector3d(1d, 0d, 0d));
            renderNorth = Direction(new Vector3d(0d, 1d, 0d));
            ready = true;
        }

        protected Vector3 ToRender(Vector3d local)
        {
            frame.ToWorld(local, Vector3d.Zero, out Vector3d world, out _);
            return FloatingOrigin.ToRender(world);
        }

        protected Vector3 Direction(Vector3d localDirection)
        {
            return (ToRender(localDirection) - ToRender(Vector3d.Zero)).normalized;
        }

        protected Vector3 ToRenderPoint(Vector3d local)
        {
            return renderOrigin
                + (renderEast * (float)local.X)
                + (renderNorth * (float)local.Y)
                + (renderUp * (float)local.Z);
        }

        /// <summary>
        /// Капсула пересекает коллайдер — направление выталкивания (нормаль стены)
        /// для свипа. Нормаль CapsuleCast при старте в проникновении недостоверна
        /// (обычно -moveDir), поэтому для таких попаданий берём нормаль из
        /// Physics.ComputePenetration. Касание пола/потолка даёт вертикальное
        /// выталкивание — проекция на плоскость вырождается, метод возвращает
        /// false (это не стена, горизонталь ведёт логика опоры/шага).
        /// </summary>
        protected bool TryPushOut(Collider collider, Vector3 feet, float radius, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (collider == null)
            {
                return false;
            }

            if (collider is MeshCollider mc && !mc.convex && warnedNonConvex.Add(mc))
            {
                Debug.LogWarning("[Support] невыпуклый MeshCollider на '" + collider.name
                    + "' — ComputePenetration не работает", collider);
            }

            CapsuleCollider probe = SiteBoxRegistry.Probe(radius);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, renderUp);
            bool overlap = Physics.ComputePenetration(
                probe, feet, rotation,
                collider, collider.transform.position, collider.transform.rotation,
                out direction, out _);
            direction = Vector3.ProjectOnPlane(direction, renderUp);
            if (!overlap || direction.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            direction.Normalize();
            return true;
        }

        public bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d normal, out int sourceId)
        {
            height = 0d;
            normal = new Vector3d(0d, 0d, 1d);
            sourceId = -1;
            if (!ready || !(maxDrop > 0d))
            {
                return false;
            }

            // Опора под стопой: центральный луч + четыре по краю стопы (0.28 м,
            // по радиусу капсулы). Одиночный луч проскакивает узкие щели между
            // коллайдерами и кромки — игрок «проваливался между ними». Берём
            // САМУЮ ВЫСОКУЮ опору в следе стопы: на неё и встают ноги.
            bool found = false;
            double best = double.NegativeInfinity;
            for (int k = 0; k < 5; k++)
            {
                Vector3d sample = from;
                switch (k)
                {
                    case 1: sample = from + new Vector3d(0.28d, 0d, 0d); break;
                    case 2: sample = from + new Vector3d(-0.28d, 0d, 0d); break;
                    case 3: sample = from + new Vector3d(0d, 0.28d, 0d); break;
                    case 4: sample = from + new Vector3d(0d, -0.28d, 0d); break;
                }

                // Луч стартует на 5 см выше точки ног: при микропроникновении ноги
                // оказываются внутри выпуклого коллайдера и луч из точки старта не
                // находит поверхность — опоры «нет», и тело падает сквозь пол.
                // Длина увеличена на тот же подъём, поэтому глубина опоры не меняется.
                const float Lift = 0.05f;
                Vector3 origin = ToRenderPoint(sample) + (renderUp * Lift);
                RaycastHit[] hits = Physics.RaycastAll(origin, -renderUp, (float)maxDrop + Lift, ~0, QueryTriggerInteraction.Ignore);
                if (!TryPickFloor(hits, out double h, out Vector3d n, out int id))
                {
                    continue;
                }

                if (!found || h > best)
                {
                    best = h;
                    height = h;
                    normal = n;
                    sourceId = id;
                    found = true;
                }
            }

            return found;
        }

        private bool TryPickFloor(RaycastHit[] hits, out double height, out Vector3d normal, out int sourceId)
        {
            height = 0d;
            normal = new Vector3d(0d, 0d, 1d);
            sourceId = -1;
            bool found = false;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsSupportCollider(hit.collider) || IsObstacleOnly(hit.collider) || hit.distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = hit.distance;
                height = LocalHeight(hit.point);
                normal = LocalDirection(hit.normal);
                sourceId = SourceIdFor(hit.collider);
                found = true;
            }

            return found;
        }

        public bool Ceiling(Vector3d head, double maxRise, out double height)
        {
            height = 0d;
            if (!ready || !(maxRise > 0d))
            {
                return false;
            }

            // Потолок ищем по следу головы (центр + четыре смещения 0.25 м):
            // одиночный луч проскакивает зазоры между коллайдерами и кромки,
            // и прыжок/джетпак «пробивал» крышу. Берём САМЫЙ НИЗКИЙ потолок следа.
            bool found = false;
            float best = float.PositiveInfinity;
            for (int k = 0; k < 5; k++)
            {
                Vector3d sample = head;
                switch (k)
                {
                    case 1: sample = head + new Vector3d(0.25d, 0d, 0d); break;
                    case 2: sample = head + new Vector3d(-0.25d, 0d, 0d); break;
                    case 3: sample = head + new Vector3d(0d, 0.25d, 0d); break;
                    case 4: sample = head + new Vector3d(0d, -0.25d, 0d); break;
                }

                RaycastHit[] hits = Physics.RaycastAll(ToRenderPoint(sample), renderUp, (float)maxRise, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits.Length; i++)
                {
                    if (!IsSupportCollider(hits[i].collider) || hits[i].distance >= best)
                    {
                        continue;
                    }

                    best = hits[i].distance;
                    height = LocalHeight(hits[i].point);
                    found = true;
                }
            }

            return found;
        }

        public bool Sweep(Vector3d from, Vector3d to, double radius, double height, out Vector3d normal, out double fraction)
        {
            normal = new Vector3d(0d, 0d, 1d);
            fraction = 1d;
            if (!ready)
            {
                return false;
            }

            // Капсула: нижняя точка полусферы — ровно ноги (from), верх — from + height.
            // CapsuleCast добавляет радиус СВЕРХ концов отрезка, поэтому сегмент
            // начинается на радиус выше ног (иначе полусфера уходит на 0.45 м под
            // пол и свип ложно цепляет крышу/пол под ногами).
            Vector3 p1 = ToRenderPoint(from) + (renderUp * (float)radius);
            Vector3 p2 = p1 + (renderUp * Mathf.Max(0.01f, (float)(height - (2d * radius))));
            Vector3 delta = ToRenderPoint(to) - ToRenderPoint(from);
            float distance = delta.magnitude;
            if (distance < 1e-6f)
            {
                return false;
            }

            Vector3 moveDir = delta / distance;
            RaycastHit[] hits = Physics.CapsuleCastAll(p1, p2, (float)radius, moveDir, distance, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            float bestFraction = 1f;
            Vector3 bestNormal = renderUp;

            // Зазор у стены: останавливаемся не в точке касания (иначе следующий
            // подшаг начинается уже внутри контактного допуска PhysX), а за Skin
            // до неё.
            const float Skin = 0.015f;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsSupportCollider(hit.collider) || hit.distance < 0f)
                {
                    continue;
                }

                Vector3 wallN = hit.normal;
                float f;
                if (hit.distance <= 0f)
                {
                    // Старт в проникновении: нормаль CapsuleCast недостоверна — берём
                    // направление выталкивания. Уход от стены свободен, вход глубже
                    // блокируется (см. проверку направления ниже). Касание пола или
                    // потолка стеной не считается (вертикальное выталкивание).
                    if (!TryPushOut(hit.collider, p1 - (renderUp * (float)radius), (float)radius, out wallN))
                    {
                        continue;
                    }

                    f = 0f;
                }
                else
                {
                    // Касание пола/склона/потолка (нормаль почти вертикальна) не
                    // блокирует горизонтальный ход: его ведёт логика опоры/шага.
                    // Для мебели/мониторов (PlayerObstacleOnly) полом считается
                    // лишь почти горизонтальная грань: фаски и скосы 30–45°
                    // (|n·up| ~ 0.7–0.9) теперь блокируют как стена.
                    float verticalLimit = IsObstacleOnly(hit.collider) ? 0.95f : 0.7f;
                    if (Mathf.Abs(Vector3.Dot(hit.normal, renderUp)) > verticalLimit)
                    {
                        continue;
                    }

                    f = Mathf.Max(0f, (hit.distance - Skin) / distance);
                }

                // Блокирует только движение В поверхность: уход от стены и движение
                // вдоль касания свободны, вход глубже — блок. Нормаль выталкивания
                // достоверна и у старта в проникновении (было: CapsuleInside
                // игнорировал коллайдер целиком — 0–5 см давали залипание,
                // глубже 5 см — свободный проход сквозь стену).
                if (Vector3.Dot(moveDir, wallN) > -1e-4f)
                {
                    continue;
                }

                if (f < bestFraction)
                {
                    bestFraction = f;
                    bestNormal = wallN;
                    found = true;

                    if (DebugEnabled)
                    {
                        DebugBlocker = hit.collider.name + " | родитель: " + hit.collider.transform.parent?.name
                            + " | obstacle=" + IsObstacleOnly(hit.collider)
                            + " | |n·up|=" + Mathf.Abs(Vector3.Dot(wallN, renderUp)).ToString("F2")
                            + " | f=" + f.ToString("F2");
                    }
                }
            }

            if (found)
            {
                fraction = bestFraction;
                normal = LocalDirection(bestNormal);
            }

            return found;
        }

        private double LocalHeight(Vector3 point)
        {
            return Vector3.Dot(point - renderOrigin, renderUp);
        }

        private Vector3d LocalDirection(Vector3 renderDirection)
        {
            return new Vector3d(
                Vector3.Dot(renderDirection, renderEast),
                Vector3.Dot(renderDirection, renderNorth),
                Vector3.Dot(renderDirection, renderUp));
        }
    }
}
