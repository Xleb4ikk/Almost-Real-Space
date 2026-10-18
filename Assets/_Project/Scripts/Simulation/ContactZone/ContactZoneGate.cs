using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// Decision about the ship entering the contact zone. The zone is landing only: entry requires
    /// low altitude above the terrain and low speed relative to the surface. Orbital and entry
    /// regimes (high speed) never enable the zone by construction. Hysteresis: enter (h &lt; Enter
    /// and v &lt; EnterSpeed), exit (h &gt; Exit or v &gt; ExitSpeed). The class does not change ship
    /// state: Update takes position and velocity by value.
    /// </summary>
    public sealed class ContactZoneGate
    {
        public double EnterAltitudeMeters { get; set; } = 300d;
        public double ExitAltitudeMeters { get; set; } = 400d;
        public double EnterSpeedMps { get; set; } = 60d;
        public double ExitSpeedMps { get; set; } = 120d;

        public bool Inside { get; private set; }

        /// <summary>Altitude above terrain and speed relative to the surface (co-rotating).</summary>
        public static void Measure(OrbitingBody body, ITerrainModel terrain, Vector3d position, Vector3d velocity,
            double timeSeconds, out double altitudeAboveTerrain, out double relativeSpeed)
        {
            body.SurfaceLatLonAt(position, timeSeconds, out double latDeg, out double lonDeg);
            double terrainHeight = terrain.GetHeightMeters(body, KeplerMath.DegreesToRadians(latDeg),
                KeplerMath.DegreesToRadians(lonDeg));
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            double radial = (position - bodyPosition).Magnitude - body.Radius;
            altitudeAboveTerrain = radial - terrainHeight;

            body.GetSurfaceState(latDeg, lonDeg, radial, timeSeconds, out _, out Vector3d surfaceVelocity);
            relativeSpeed = (velocity - surfaceVelocity).Magnitude;
        }

        /// <summary>Update the decision for the current frame. Returns true if the zone is active.</summary>
        public bool Update(OrbitingBody body, ITerrainModel terrain, Vector3d position, Vector3d velocity, double timeSeconds)
        {
            Measure(body, terrain, position, velocity, timeSeconds, out double altitude, out double speed);
            if (!Inside)
            {
                if (altitude < EnterAltitudeMeters && speed < EnterSpeedMps)
                {
                    Inside = true;
                }
            }
            else if (altitude > ExitAltitudeMeters || speed > ExitSpeedMps)
            {
                Inside = false;
            }

            return Inside;
        }

        public void Reset()
        {
            Inside = false;
        }
    }
}
