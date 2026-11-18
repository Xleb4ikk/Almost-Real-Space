using Galilego.Core;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// Axis math for the contact zone, kept free of UnityEngine so the test stand can
    /// compile it (Test323/324). Site axes are (east, north, up); the zone root's local
    /// Unity axes are (north, up, east) — the same permutation as ContactZoneProxy.ToUnity.
    /// </summary>
    public static class ContactZoneFrameMath
    {
        /// <summary>Site axes (east, north, up) → zone-local Unity axes (north, up, east).</summary>
        public static Vector3d SiteToZone(Vector3d site)
        {
            return new Vector3d(site.Y, site.Z, site.X);
        }

        /// <summary>Zone-local Unity axes (north, up, east) → site axes (east, north, up).</summary>
        public static Vector3d ZoneToSite(Vector3d zone)
        {
            return new Vector3d(zone.Z, zone.X, zone.Y);
        }

        /// <summary>Zone-local axes → world through the site basis (east, north, up), без скорости начала кадра.</summary>
        public static Vector3d ZoneToWorld(Vector3d zone, Vector3d east, Vector3d north, Vector3d up)
        {
            return (north * zone.X) + (up * zone.Y) + (east * zone.Z);
        }
    }
}
