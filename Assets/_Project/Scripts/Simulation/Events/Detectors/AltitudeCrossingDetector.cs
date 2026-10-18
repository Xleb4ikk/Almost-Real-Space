using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// Crossing of an altitude shell around a body:
    /// g = |r_ship − r_body(t)| − (Radius + ThresholdAltitude).
    /// One class for two thresholds: atmosphere enter/exit (threshold =
    /// TopAltitudeMeters) and surface touch (threshold = 0). The body position is
    /// computed on the fly via EvaluateWorldState — a read, not a mutation, so the
    /// detector's pure contract is not violated. The factories below define
    /// meaningful "threshold + direction + priority" combos so callers do not have
    /// to assemble them by hand and get the signs wrong.
    /// </summary>
    public sealed class AltitudeCrossingDetector : ICrossingDetector
    {
        private readonly OrbitingBody body;
        private readonly double thresholdAltitude;
        private readonly ITerrainModel terrain;

        public EventDirection Direction { get; }

        public int Priority { get; }

        public string Name { get; }

        public EventKind Kind { get; }

        public OrbitingBody Body => body;

        /// <summary>Altitude threshold above Radius (m): the sphere whose crossing is detected.</summary>
        public double ThresholdAltitude => thresholdAltitude;

        public AltitudeCrossingDetector(
            OrbitingBody body, double thresholdAltitude, EventDirection direction, int priority, string name, EventKind kind)
            : this(body, thresholdAltitude, direction, priority, name, kind, null)
        {
        }

        /// <summary>
        /// Terrain variant: the threshold is measured from the local surface height
        /// (used for touch: g = |rel| − (R + H(lat,lon))). null — spherical path.
        /// </summary>
        public AltitudeCrossingDetector(
            OrbitingBody body, double thresholdAltitude, EventDirection direction, int priority, string name, EventKind kind, ITerrainModel terrainModel)
        {
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            this.thresholdAltitude = thresholdAltitude;
            terrain = terrainModel;
            Direction = direction;
            Priority = priority;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
        }

        /// <summary>Atmosphere entry top to bottom (g: + → −).</summary>
        public static AltitudeCrossingDetector ForAtmosphereEntry(OrbitingBody body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (body.Atmosphere == null)
                throw new ArgumentException("У тела нет атмосферы — вход детектировать не на чем.", nameof(body));
            return new AltitudeCrossingDetector(
                body, body.Atmosphere.TopAltitudeMeters, EventDirection.Falling,
                EventPriorities.Atmosphere, "AtmosphereEntry:" + body.Name, EventKind.AtmosphereEntry);
        }

        /// <summary>Atmosphere exit bottom to top (g: − → +).</summary>
        public static AltitudeCrossingDetector ForAtmosphereExit(OrbitingBody body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (body.Atmosphere == null)
                throw new ArgumentException("У тела нет атмосферы — выход детектировать не на чем.", nameof(body));
            return new AltitudeCrossingDetector(
                body, body.Atmosphere.TopAltitudeMeters, EventDirection.Rising,
                EventPriorities.Atmosphere, "AtmosphereExit:" + body.Name, EventKind.AtmosphereExit);
        }

        /// <summary>Surface touch (g: + → − through Radius); the body's terrain is taken into account automatically.</summary>
        public static AltitudeCrossingDetector ForTouchdown(OrbitingBody body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            return new AltitudeCrossingDetector(
                body, 0d, EventDirection.Falling,
                EventPriorities.Touchdown, "Touchdown:" + body.Name, EventKind.Touchdown, body.Terrain);
        }

        public double Evaluate(SpacecraftIntegrationState state, double timeSeconds)
        {
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            Vector3d relative = state.Position - bodyPosition;
            if (terrain == null)
            {
                return relative.Magnitude - (body.Radius + thresholdAltitude);
            }

            body.SurfaceLatLonAt(state.Position, timeSeconds, out double latDeg, out double lonDeg);
            double surfaceRadius = body.Radius + thresholdAltitude
                + terrain.GetHeightMeters(body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d));
            return relative.Magnitude - surfaceRadius;
        }
    }
}
