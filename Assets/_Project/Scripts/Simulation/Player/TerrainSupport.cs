using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Support — analytic terrain (ITerrainModel) in the local site frame: height and
    /// normal from the SAME shape function (ITerrainModel contract).
    /// </summary>
    public sealed class TerrainSupport : IPlayerSupport
    {
        /// <summary>Terrain support id in the composite.</summary>
        public const int SourceId = 0;

        private OrbitingBody body;
        private ITerrainModel terrain;
        private SiteFrame frame;
        private double time;

        public void SetFrame(OrbitingBody body, ITerrainModel terrain, SiteFrame frame, double time)
        {
            this.body = body;
            this.terrain = terrain;
            this.frame = frame;
            this.time = time;
        }

        public bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d normal, out int sourceId)
        {
            height = 0d;
            normal = new Vector3d(0d, 0d, 1d);
            sourceId = SourceId;
            if (body == null || terrain == null || frame == null)
            {
                return false;
            }

            // Frame-lateral point → world → lat/lon → height from the same function as physics.
            Vector3d lateral = frame.Origin + (frame.East * from.X) + (frame.North * from.Y);
            body.EvaluateWorldState(time, out Vector3d center, out _);
            Vector3d radial = (lateral - center).Normalized;
            Vector3d sample = center + (radial * body.Radius);
            body.SurfaceLatLonAt(sample, time, out double latDeg, out double lonDeg);
            double h = terrain.GetHeightMeters(body, latDeg * (System.Math.PI / 180d), lonDeg * (System.Math.PI / 180d));
            Vector3d surface = center + (radial * (body.Radius + h));
            double localHeight = Vector3d.Dot(surface - frame.Origin, frame.Up);
            if (localHeight > from.Z + 1e-9d || localHeight < from.Z - maxDrop)
            {
                return false;
            }

            height = localHeight;
            Vector3d n = terrain.GetOutwardNormal(body, surface - center, time).Normalized;
            normal = new Vector3d(
                Vector3d.Dot(n, frame.East),
                Vector3d.Dot(n, frame.North),
                Vector3d.Dot(n, frame.Up));
            return true;
        }

        public bool Ceiling(Vector3d head, double maxRise, out double height)
        {
            height = 0d;
            return false;
        }

        public bool Sweep(Vector3d from, Vector3d to, double radius, double height, out Vector3d normal, out double fraction)
        {
            normal = new Vector3d(0d, 0d, 1d);
            fraction = 1d;
            return false;
        }
    }
}
