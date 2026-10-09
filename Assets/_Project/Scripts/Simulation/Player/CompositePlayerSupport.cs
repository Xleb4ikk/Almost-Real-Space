using Galilego.Core;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Композит опор: рельеф (аналитический) и постройки (мешевые коллайдеры).
    /// Пол — самая высокая из двух; потолок и стены — только постройки.
    /// Идентификаторы источников: рельеф — TerrainSupport.SourceId, постройки —
    /// SiteBoxSupport.SourceIdBase + id коллайдера.
    /// </summary>
    public sealed class CompositePlayerSupport : IPlayerSupport
    {
        public IPlayerSupport Terrain;
        public IPlayerSupport Sites;

        public bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d normal, out int sourceId)
        {
            double terrainHeight = 0d;
            Vector3d terrainNormal = new Vector3d(0d, 0d, 1d);
            int terrainId = -1;
            bool hasTerrain = false;
            if (Terrain != null)
            {
                hasTerrain = Terrain.Floor(from, maxDrop, out terrainHeight, out terrainNormal, out terrainId);
            }

            double siteHeight = 0d;
            Vector3d siteNormal = new Vector3d(0d, 0d, 1d);
            int siteId = -1;
            bool hasSite = false;
            if (Sites != null)
            {
                hasSite = Sites.Floor(from, maxDrop, out siteHeight, out siteNormal, out siteId);
            }

            if (hasSite && (!hasTerrain || siteHeight >= terrainHeight))
            {
                height = siteHeight;
                normal = siteNormal;
                sourceId = siteId;
                return true;
            }

            if (hasTerrain)
            {
                height = terrainHeight;
                normal = terrainNormal;
                sourceId = terrainId;
                return true;
            }

            height = 0d;
            normal = new Vector3d(0d, 0d, 1d);
            sourceId = -1;
            return false;
        }

        public bool Ceiling(Vector3d head, double maxRise, out double height)
        {
            if (Sites != null)
            {
                return Sites.Ceiling(head, maxRise, out height);
            }

            height = 0d;
            return false;
        }

        public bool Sweep(Vector3d from, Vector3d to, double radius, out Vector3d normal, out double fraction)
        {
            if (Sites != null)
            {
                return Sites.Sweep(from, to, radius, out normal, out fraction);
            }

            normal = new Vector3d(0d, 0d, 1d);
            fraction = 1d;
            return false;
        }
    }
}
