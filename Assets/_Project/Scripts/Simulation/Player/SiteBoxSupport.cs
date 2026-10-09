using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Опора — мешевые коллайдеры построек (SiteBox) в render-пространстве:
    /// Floor — луч вниз от точки ног, Ceiling — луч вверх от головы,
    /// Sweep — CapsuleCast горизонтально. В отличие от старого PushOutMesh,
    /// вертикаль НЕ обнуляется: крыши и верхние грани — полноценная опора.
    /// Находим только коллайдеры SiteBox (террейн ведёт TerrainSupport).
    /// </summary>
    public sealed class SiteBoxSupport : IPlayerSupport
    {
        /// <summary>База идентификаторов опор-построек в композите.</summary>
        public const int SourceIdBase = 100;

        private SiteFrame frame;
        private Vector3 renderOrigin;
        private Vector3 renderUp;
        private Vector3 renderEast;
        private Vector3 renderNorth;
        private bool ready;

        // Стабильные идентификаторы построек (sourceId для переноса/адресации).
        private readonly System.Collections.Generic.Dictionary<SiteBox, int> siteIds
            = new System.Collections.Generic.Dictionary<SiteBox, int>();
        private int nextSiteId = SourceIdBase;

        private int SourceIdFor(SiteBox box)
        {
            if (!siteIds.TryGetValue(box, out int id))
            {
                id = nextSiteId++;
                siteIds.Add(box, id);
            }

            return id;
        }

        /// <summary>
        /// Владелец коллайдера: SiteBox в предках (авторские коллайдеры) или
        /// SiteBoxPart.Owner (холдеры — дети частей, SiteBox лежит рядом).
        /// </summary>
        public static SiteBox ResolveSiteBox(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            SiteBox box = collider.GetComponentInParent<SiteBox>();
            if (box != null)
            {
                return box;
            }

            SiteBoxPart part = collider.GetComponentInParent<SiteBoxPart>();
            return part != null ? part.Owner : null;
        }

        public void SetFrame(SiteFrame siteFrame)
        {
            frame = siteFrame;
            renderOrigin = ToRender(Vector3d.Zero);
            renderUp = Direction(new Vector3d(0d, 0d, 1d));
            renderEast = Direction(new Vector3d(1d, 0d, 0d));
            renderNorth = Direction(new Vector3d(0d, 1d, 0d));
            ready = true;
        }

        private Vector3 ToRender(Vector3d local)
        {
            frame.ToWorld(local, Vector3d.Zero, out Vector3d world, out _);
            return FloatingOrigin.ToRender(world);
        }

        private Vector3 Direction(Vector3d localDirection)
        {
            return (ToRender(localDirection) - ToRender(Vector3d.Zero)).normalized;
        }

        private Vector3 ToRenderPoint(Vector3d local)
        {
            return renderOrigin
                + (renderEast * (float)local.X)
                + (renderNorth * (float)local.Y)
                + (renderUp * (float)local.Z);
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

            // Опора под стопой: центральный луч + четыре по краю стопы (0.3 м).
            // Одиночный луч проскакивает узкие щели между коллайдерами и кромки —
            // игрок «проваливался между ними». Берём САМУЮ ВЫСОКУЮ опору в следе
            // стопы: на неё и встают ноги.
            bool found = false;
            double best = double.NegativeInfinity;
            for (int k = 0; k < 5; k++)
            {
                Vector3d sample = from;
                switch (k)
                {
                    case 1: sample = from + new Vector3d(0.3d, 0d, 0d); break;
                    case 2: sample = from + new Vector3d(-0.3d, 0d, 0d); break;
                    case 3: sample = from + new Vector3d(0d, 0.3d, 0d); break;
                    case 4: sample = from + new Vector3d(0d, -0.3d, 0d); break;
                }

                Vector3 origin = ToRenderPoint(sample);
                RaycastHit[] hits = Physics.RaycastAll(origin, -renderUp, (float)maxDrop, ~0, QueryTriggerInteraction.Ignore);
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
                SiteBox box = ResolveSiteBox(hit.collider);
                if (box == null || hit.distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = hit.distance;
                height = LocalHeight(hit.point);
                normal = LocalDirection(hit.normal);
                sourceId = SourceIdFor(box);
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

            Vector3 origin = ToRenderPoint(head);
            RaycastHit[] hits = Physics.RaycastAll(origin, renderUp, (float)maxRise, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            float best = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                SiteBox box = ResolveSiteBox(hit.collider);
                if (box == null || hit.distance >= best)
                {
                    continue;
                }

                best = hit.distance;
                height = LocalHeight(hit.point);
                found = true;
            }

            return found;
        }

        public bool Sweep(Vector3d from, Vector3d to, double radius, out Vector3d normal, out double fraction)
        {
            normal = new Vector3d(0d, 0d, 1d);
            fraction = 1d;
            if (!ready)
            {
                return false;
            }

            // Капсула: нижняя точка полусферы — ровно ноги (from), верх — from + высота.
            // CapsuleCast добавляет радиус СВЕРХ концов отрезка, поэтому сегмент
            // начинается на радиус выше ног (иначе полусфера уходит на 0.45 м под
            // пол и свип ложно цепляет крышу/пол под ногами).
            Vector3 p1 = ToRenderPoint(from) + (renderUp * (float)radius);
            Vector3 p2 = p1 + (renderUp * Mathf.Max(0.01f, (float)(PlayerSurfaceController.CapsuleHeight - (2d * radius))));
            Vector3 delta = ToRenderPoint(to) - ToRenderPoint(from);
            float distance = delta.magnitude;
            if (distance < 1e-6f)
            {
                return false;
            }

            RaycastHit[] hits = Physics.CapsuleCastAll(p1, p2, (float)radius, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            float bestFraction = 1f;
            Vector3 bestNormal = renderUp;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                SiteBox box = ResolveSiteBox(hit.collider);
                if (box == null || hit.distance < 0f)
                {
                    continue;
                }

                // Касание в старте (стоим на стыке двух коллайдеров — капсула уже
                // соприкасается): это не стена. Иначе свип блокирует ход на швах
                // («застреваю на стыке рампы и площадки»).
                if (hit.distance < 0.002f)
                {
                    continue;
                }

                float f = hit.distance / distance;
                if (f < bestFraction)
                {
                    bestFraction = f;
                    bestNormal = hit.normal;
                    found = true;
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
