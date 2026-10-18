using Galilego.Core;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Композит опор: рельеф (аналитический), постройки (мешевые коллайдеры)
    /// и детали аппарата (визуалы в стройке / тела прокси в полёте).
    /// Пол — самая высокая из трёх; потолок — самый низкий из найденных;
    /// стены — первая по пути. Идентификаторы источников: рельеф —
    /// TerrainSupport.SourceId, постройки — SiteBoxSupport.SourceIdBase + id,
    /// детали — VesselSupport.SourceIdBase + id.
    /// </summary>
    public sealed class CompositePlayerSupport : IPlayerSupport, IOverlapSupport
    {
        public IPlayerSupport Terrain;
        public IPlayerSupport Sites;
        public IPlayerSupport Vessel;

        public bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d normal, out int sourceId)
        {
            height = 0d;
            normal = new Vector3d(0d, 0d, 1d);
            sourceId = -1;
            bool found = false;
            TryFloor(Terrain, from, maxDrop, ref found, ref height, ref normal, ref sourceId);
            TryFloor(Sites, from, maxDrop, ref found, ref height, ref normal, ref sourceId);
            TryFloor(Vessel, from, maxDrop, ref found, ref height, ref normal, ref sourceId);
            return found;
        }

        private static void TryFloor(IPlayerSupport source, Vector3d from, double maxDrop,
            ref bool found, ref double height, ref Vector3d normal, ref int sourceId)
        {
            if (source == null
                || !source.Floor(from, maxDrop, out double h, out Vector3d n, out int id))
            {
                return;
            }

            if (found && h <= height)
            {
                return;
            }

            found = true;
            height = h;
            normal = n;
            sourceId = id;
        }

        public bool Ceiling(Vector3d head, double maxRise, out double height)
        {
            height = double.PositiveInfinity;
            bool found = false;
            found |= TryCeiling(Sites, head, maxRise, ref height);
            found |= TryCeiling(Vessel, head, maxRise, ref height);
            return found;
        }

        private static bool TryCeiling(IPlayerSupport source, Vector3d head, double maxRise, ref double height)
        {
            if (source == null || !source.Ceiling(head, maxRise, out double h))
            {
                return false;
            }

            if (h < height)
            {
                height = h;
            }

            return true;
        }

        /// <summary>Капсула пересекает постройку или деталь аппарата (рельеф — аналитический, не считается).</summary>
        public bool Overlaps(Vector3d feet, double radius, double height)
        {
            return (Sites is IOverlapSupport sites && sites.Overlaps(feet, radius, height))
                || (Vessel is IOverlapSupport vessel && vessel.Overlaps(feet, radius, height));
        }

        public bool Sweep(Vector3d from, Vector3d to, double radius, double height, out Vector3d normal, out double fraction)
        {
            normal = new Vector3d(0d, 0d, 1d);
            fraction = 1d;
            bool found = false;
            found |= TrySweep(Sites, from, to, radius, height, ref normal, ref fraction);
            found |= TrySweep(Vessel, from, to, radius, height, ref normal, ref fraction);
            return found;
        }

        private static bool TrySweep(IPlayerSupport source, Vector3d from, Vector3d to, double radius, double height,
            ref Vector3d normal, ref double fraction)
        {
            if (source == null
                || !source.Sweep(from, to, radius, height, out Vector3d n, out double f)
                || f >= fraction)
            {
                return false;
            }

            normal = n;
            fraction = f;
            return true;
        }
    }
}
