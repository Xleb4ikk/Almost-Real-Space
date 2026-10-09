using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Spacecraft.Solver
{
    /// <summary>
    /// Коллайдер постройки в ЛОКАЛЬНОМ кадре места (x — восток, y — север, z — вверх
    /// по осям SiteFrame; у экспортированных данных — оси префаба места).
    /// Запрос точки: depth &gt; 0 — точка внутри, normal — наружная нормаль.
    /// </summary>
    public interface ISiteCollider
    {
        bool QueryPoint(Vector3d point, out double depth, out Vector3d normal);

        void GetAabb(out Vector3d min, out Vector3d max);
    }

    /// <summary>Выпуклая часть постройки (точки — вершины оболочки, кадр места).</summary>
    public sealed class SiteHullCollider : ISiteCollider
    {
        public readonly ConvexHull Hull;

        public SiteHullCollider(IReadOnlyList<Vector3d> points)
        {
            Hull = ConvexHull.Build(points);
        }

        public bool QueryPoint(Vector3d point, out double depth, out Vector3d normal)
        {
            return Hull.QueryPoint(point, out depth, out normal);
        }

        public void GetAabb(out Vector3d min, out Vector3d max)
        {
            min = Hull.Min;
            max = Hull.Max;
        }
    }

    /// <summary>Ориентированный бокс (воксель вогнутой части, кадр места).</summary>
    public sealed class SiteBoxCollider : ISiteCollider
    {
        public readonly Vector3d Center;
        public readonly Vector3d HalfExtents;
        public readonly QuaternionD Orientation;

        public SiteBoxCollider(Vector3d center, Vector3d halfExtents, QuaternionD orientation)
        {
            Center = center;
            HalfExtents = halfExtents;
            Orientation = orientation.Normalized;
        }

        public bool QueryPoint(Vector3d point, out double depth, out Vector3d normal)
        {
            Vector3d local = Orientation.Conjugated.Rotate(point - Center);

            double dx = HalfExtents.X - Math.Abs(local.X);
            double dy = HalfExtents.Y - Math.Abs(local.Y);
            double dz = HalfExtents.Z - Math.Abs(local.Z);

            double best = dx;
            Vector3d axis = new Vector3d(local.X >= 0d ? 1d : -1d, 0d, 0d);
            if (dy < best)
            {
                best = dy;
                axis = new Vector3d(0d, local.Y >= 0d ? 1d : -1d, 0d);
            }

            if (dz < best)
            {
                best = dz;
                axis = new Vector3d(0d, 0d, local.Z >= 0d ? 1d : -1d);
            }

            if (!(best > 0d))
            {
                depth = 0d;
                normal = Vector3d.Zero;
                return false;
            }

            depth = best;
            normal = Orientation.Rotate(axis);
            return true;
        }

        public void GetAabb(out Vector3d min, out Vector3d max)
        {
            Vector3d ex = Orientation.Rotate(new Vector3d(HalfExtents.X, 0d, 0d));
            Vector3d ey = Orientation.Rotate(new Vector3d(0d, HalfExtents.Y, 0d));
            Vector3d ez = Orientation.Rotate(new Vector3d(0d, 0d, HalfExtents.Z));
            Vector3d ext = new Vector3d(
                Math.Abs(ex.X) + Math.Abs(ey.X) + Math.Abs(ez.X),
                Math.Abs(ex.Y) + Math.Abs(ey.Y) + Math.Abs(ez.Y),
                Math.Abs(ex.Z) + Math.Abs(ey.Z) + Math.Abs(ez.Z));
            min = Center - ext;
            max = Center + ext;
        }
    }

    /// <summary>
    /// Набор коллайдеров постройки: запрос точки — самый глубокий из содержащих её
    /// (объединение тел). Широкая фаза — по AABB.
    /// </summary>
    public sealed class SiteColliderSet
    {
        private readonly List<ISiteCollider> colliders = new List<ISiteCollider>();
        private readonly List<Vector3d> aabbMin = new List<Vector3d>();
        private readonly List<Vector3d> aabbMax = new List<Vector3d>();

        // Широкая фаза: равномерная сетка по XZ (постройки вытянуты горизонтально).
        private Dictionary<long, List<int>> grid;
        private List<int> oversized;
        private double gridCellSize;

        public int Count => colliders.Count;

        /// <summary>Коллайдер по индексу (для тестов/отладки).</summary>
        public ISiteCollider GetCollider(int index) => colliders[index];

        /// <summary>Мировой (в кадре места) AABB коллайдера по индексу.</summary>
        public void GetColliderAabb(int index, out Vector3d min, out Vector3d max)
        {
            min = aabbMin[index];
            max = aabbMax[index];
        }

        public void Add(ISiteCollider collider)
        {
            if (collider == null)
            {
                throw new ArgumentNullException(nameof(collider));
            }

            colliders.Add(collider);
            collider.GetAabb(out Vector3d min, out Vector3d max);
            aabbMin.Add(min);
            aabbMax.Add(max);
        }

        /// <summary>Построить сетку широкой фазы (клетка, м). Без неё — перебор всех.</summary>
        public void BuildGrid(double cellSizeMeters)
        {
            if (!(cellSizeMeters > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeMeters));
            }

            gridCellSize = cellSizeMeters;
            grid = new Dictionary<long, List<int>>(colliders.Count * 2);
            oversized = new List<int>();
            for (int i = 0; i < colliders.Count; i++)
            {
                int x0 = (int)Math.Floor(aabbMin[i].X / gridCellSize);
                int x1 = (int)Math.Floor(aabbMax[i].X / gridCellSize);
                int y0 = (int)Math.Floor(aabbMin[i].Y / gridCellSize);
                int y1 = (int)Math.Floor(aabbMax[i].Y / gridCellSize);
                int z0 = (int)Math.Floor(aabbMin[i].Z / gridCellSize);
                int z1 = (int)Math.Floor(aabbMax[i].Z / gridCellSize);
                long cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
                if (cells > 512)
                {
                    oversized.Add(i);
                    continue;
                }

                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int cy = y0; cy <= y1; cy++)
                    {
                        for (int cz = z0; cz <= z1; cz++)
                        {
                            long key = CellKey(cx, cy, cz);
                            if (!grid.TryGetValue(key, out List<int> list))
                            {
                                list = new List<int>(4);
                                grid.Add(key, list);
                            }

                            list.Add(i);
                        }
                    }
                }
            }
        }

        private static long CellKey(int cx, int cy, int cz)
        {
            // 21 бит на ось: сетка 8 м покрывает ±8.8 млн м по каждой оси.
            return ((long)cx & 0x1FFFFFL)
                | (((long)cy & 0x1FFFFFL) << 21)
                | (((long)cz & 0x1FFFFFL) << 42);
        }

        public bool QueryPoint(Vector3d point, out double depth, out Vector3d normal)
        {
            depth = 0d;
            normal = Vector3d.Zero;
            double best = 0d;

            if (grid != null)
            {
                int cx = (int)Math.Floor(point.X / gridCellSize);
                int cy = (int)Math.Floor(point.Y / gridCellSize);
                int cz = (int)Math.Floor(point.Z / gridCellSize);
                if (grid.TryGetValue(CellKey(cx, cy, cz), out List<int> cell))
                {
                    for (int i = 0; i < cell.Count; i++)
                    {
                        TryQuery(cell[i], point, ref best, ref normal);
                    }
                }

                for (int i = 0; i < oversized.Count; i++)
                {
                    TryQuery(oversized[i], point, ref best, ref normal);
                }
            }
            else
            {
                for (int i = 0; i < colliders.Count; i++)
                {
                    TryQuery(i, point, ref best, ref normal);
                }
            }

            depth = best;
            return best > 0d;
        }

        private bool TryQuery(int index, Vector3d point, ref double best, ref Vector3d normal)
        {
            Vector3d min = aabbMin[index];
            Vector3d max = aabbMax[index];
            if (point.X < min.X || point.X > max.X
                || point.Y < min.Y || point.Y > max.Y
                || point.Z < min.Z || point.Z > max.Z)
            {
                return false;
            }

            if (colliders[index].QueryPoint(point, out double d, out Vector3d n) && d > best)
            {
                best = d;
                normal = n;
                return true;
            }

            return false;
        }

        public void GetAabb(out Vector3d min, out Vector3d max)
        {
            if (colliders.Count == 0)
            {
                min = Vector3d.Zero;
                max = Vector3d.Zero;
                return;
            }

            min = aabbMin[0];
            max = aabbMax[0];
            for (int i = 1; i < colliders.Count; i++)
            {
                min = new Vector3d(
                    Math.Min(min.X, aabbMin[i].X),
                    Math.Min(min.Y, aabbMin[i].Y),
                    Math.Min(min.Z, aabbMin[i].Z));
                max = new Vector3d(
                    Math.Max(max.X, aabbMax[i].X),
                    Math.Max(max.Y, aabbMax[i].Y),
                    Math.Max(max.Z, aabbMax[i].Z));
            }
        }
    }

    /// <summary>Опора-постройка как источник контактов решателя (фаза 6).</summary>
    public sealed class SiteContactGround : IContactGround
    {
        public SiteColliderSet Colliders { get; }

        public SiteContactGround(SiteColliderSet colliders)
        {
            Colliders = colliders ?? throw new ArgumentNullException(nameof(colliders));
        }

        public void Query(Vector3d point, out double depth, out Vector3d normal)
        {
            Colliders.QueryPoint(point, out depth, out normal);
        }
    }

    /// <summary>Объединение двух источников контакта: побеждает более глубокий.</summary>
    public sealed class CompositeGround : IContactGround
    {
        private readonly IContactGround primary;
        private readonly IContactGround secondary;

        public CompositeGround(IContactGround primary, IContactGround secondary)
        {
            this.primary = primary ?? throw new ArgumentNullException(nameof(primary));
            this.secondary = secondary ?? throw new ArgumentNullException(nameof(secondary));
        }

        public void Query(Vector3d point, out double depth, out Vector3d normal)
        {
            primary.Query(point, out double d1, out Vector3d n1);
            secondary.Query(point, out double d2, out Vector3d n2);
            if (d2 > d1)
            {
                depth = d2;
                normal = n2;
            }
            else
            {
                depth = d1;
                normal = n1;
            }
        }
    }
}
