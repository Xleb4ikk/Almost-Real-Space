using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Tablet
{
    public enum NavLineKind
    {
        BodyOrbit = 0,
        ShipHistory = 1,
        ShipForecast = 2,

        /// <summary>Reference body's own path over the history window (ephemeris, not a live sample).</summary>
        BodyHistory = 3,

        /// <summary>Reference body's own path over the forecast window (ephemeris, not an integration).</summary>
        BodyForecast = 4,

        /// <summary>Predicted path after a planned maneuver (same integrator, ΔV applied at ignition).</summary>
        ManeuverForecast = 5,
    }

    /// <summary>What the map should show at the current ship state.</summary>
    public enum NavViewMode
    {
        Orbits = 0,
        Surface = 1,
    }

    /// <summary>One polyline in the map, already in the reference frame (frame body at the origin).</summary>
    public sealed class NavLine
    {
        public NavLineKind Kind;
        public int BodyIndex = -1;
        public int ParentIndex = -1;
        public bool Dashed;
        public double StartTimeS;
        public double EndTimeS;
        public readonly List<Vector3d> Points = new List<Vector3d>();

        /// <summary>
        /// Simulation time of each point (same count as <see cref="Points"/> for an
        /// integrated ship path). Body rails do not fill it: only the forecast cache
        /// needs per-vertex time, to rebuild a path in another frame without
        /// integrating it again.
        /// </summary>
        public readonly List<double> Times = new List<double>();
    }

    /// <summary>Body marker at the current simulation time.</summary>
    public sealed class NavBodyMarker
    {
        public int Index;
        public string Name = "?";
        public double RadiusMeters;
        public Vector3d Position;
        public bool IsFrame;
        public OrbitingBody Body;
    }

    /// <summary>Planned burn point of the forecast, already in the reference frame.</summary>
    public sealed class NavBurnMarker
    {
        /// <summary>1-based number of the maneuver in the plan.</summary>
        public int Index;

        public double TimeSeconds;
        public Vector3d Position;
        public double DvMs;
        public int FrameBodyIndex = -1;
    }

    /// <summary>
    /// Where a forecast leg stops on the ground, in the reference frame. The line
    /// cannot continue past the contact (there is nothing to fly through), so the
    /// map marks the point, the time and the impact speed instead of leaving the
    /// player with a polyline that simply ends in the middle of a globe.
    /// </summary>
    public sealed class NavImpactMarker
    {
        /// <summary>Absolute simulation time of the contact.</summary>
        public double TimeSeconds;

        /// <summary>Contact point in the reference frame (frame body at its centre).</summary>
        public Vector3d Position;

        /// <summary>Speed relative to the surface at the contact, m/s (not the orbital speed).</summary>
        public double SpeedMps;

        public int BodyIndex = -1;
        public string BodyName = "?";

        /// <summary>True when the contact ends the planned (dashed) leg, false when it ends the coast.</summary>
        public bool Planned;
    }

    /// <summary>Actual ship state sample kept for the history trail.</summary>
    public struct NavShipSample
    {
        public double TimeSeconds;
        public Vector3d Position;

        public NavShipSample(double timeSeconds, Vector3d position)
        {
            TimeSeconds = timeSeconds;
            Position = position;
        }
    }

    /// <summary>
    /// Geometry source of the navigation map. It uses only the game's own orbital
    /// physics: body positions/velocities come from the baked ephemerides
    /// (OrbitingBody.EvaluateWorldState), the ship's future is integrated with the
    /// same N-body gravity + drag sources as SimulationRunner (planned maneuvers
    /// enter as impulses at their ignition time), and the history is sampled from
    /// the actually simulated ship state. No Kepler preview is substituted for the
    /// game physics.
    ///
    /// Everything is stored in the selected reference frame: the frame body is at
    /// the origin at time TimeSeconds, and each point is (world point - frame body
    /// world position at the same time).
    /// </summary>
    public sealed class NavMapModel
    {
        /// <summary>
        /// Samples per body trail arc. These are ephemeris evaluations, not an
        /// integration, so they are cheap; 256 of them keep a zoomed trail a curve
        /// even before the renderer's smoothing.
        /// </summary>
        public const int BodyOrbitSamples = 256;
        public const int ForecastSamples = 160;
        public const int MaxHistoryPoints = 512;

        /// <summary>Longest forecast actually integrated for the map, seconds (visual/CPU budget).</summary>
        public const double MaxForecastSeconds = 60d * 86400d;

        /// <summary>Longest history reconstructed from the ephemerides, seconds (same ≈100-year ceiling as the UI).</summary>
        public const double MaxHistorySeconds = 3.156e9d;

        /// <summary>
        /// Integrator tolerance of the map's own forecast. The map is a picture, not
        /// the flown state: a 3-orbit LEO forecast takes ~460 ms at the production
        /// 1e-11 and ~31 ms at 1e-7, while the trajectory moves by centimetres —
        /// orders of magnitude below one pixel. Kept as a field so a test can pin it.
        /// </summary>
        public double ForecastRelativeTolerance = 1e-7d;

        /// <summary>Absolute position tolerance of the forecast, metres (one map pixel is kilometres).</summary>
        public double ForecastPositionToleranceMeters = 1e-2d;

        private const double DaySeconds = 86400d;
        private const double HourSeconds = 3600d;

        public readonly List<NavLine> Lines = new List<NavLine>();
        public readonly List<NavBodyMarker> Bodies = new List<NavBodyMarker>();
        public readonly List<NavShipSample> History = new List<NavShipSample>();
        public readonly List<NavBurnMarker> BurnMarkers = new List<NavBurnMarker>();

        /// <summary>Surface contacts found while building the forecast (one per leg that hits the ground).</summary>
        public readonly List<NavImpactMarker> Impacts = new List<NavImpactMarker>();

        /// <summary>Maneuvers actually folded into the forecast (not just present in the plan).</summary>
        public int AppliedManeuverCount;

        /// <summary>True when the plan is time-conflicting: only the part before the conflict is predicted.</summary>
        public bool PlanConflict;

        /// <summary>True when a planned maneuver lies beyond the requested forecast window.</summary>
        public bool PlanOutsideWindow;

        public double TimeSeconds;
        public int FrameIndex;
        public int CenterIndex;
        public int ShipIndex;
        public bool HasShip;
        public Vector3d FramePosition;
        public Vector3d ShipPosition;
        public Vector3d ShipVelocity;
        public Vector3d FrameRelativeShipPosition;
        public Vector3d FrameRelativeShipVelocity;
        public Vector3d CenterPosition;

        /// <summary>Map camera plane normal: the reference body's spin axis, fixed (not the ship's orbit).</summary>
        public Vector3d OrbitNormal = new Vector3d(0d, 0d, 1d);

        /// <summary>Fixed in-plane direction of the map camera basis.</summary>
        public Vector3d OrbitRight = new Vector3d(1d, 0d, 0d);

        /// <summary>True while the engine is firing (a maneuver), false on a ballistic coast.
        /// Set by the station; the map tints the ship, periapsis and apoapsis markers by it.</summary>
        public bool ShipThrusting;
        public bool HasApsides;
        public bool HasApoapsis;
        public Vector3d PeriapsisPosition;
        public Vector3d ApoapsisPosition;

        /// <summary>Apsis altitude above the dominant body's mean radius (same value as the orbit panel).</summary>
        public double PeriapsisAltitudeMeters = double.NaN;
        public double ApoapsisAltitudeMeters = double.NaN;

        /// <summary>Speed at the apsis from the osculating orbit's angular momentum (h / r).</summary>
        public double PeriapsisSpeedMps = double.NaN;
        public double ApoapsisSpeedMps = double.NaN;

        /// <summary>
        /// Apsides of the orbit the plan will actually fly: the osculating conic
        /// right after the LAST applied burn. Drawn as a second, differently
        /// coloured Ap/Pe pair next to the ballistic ones, so the player sees what
        /// the maneuver changes before igniting. False when the plan changes
        /// nothing (no ΔV), the burn leg is not drawn, or the resulting orbit is
        /// circular/unbound in a way that has no meaningful pair.
        /// </summary>
        public bool HasPlannedApsides;
        public bool HasPlannedApoapsis;
        public Vector3d PlannedPeriapsisPosition;
        public Vector3d PlannedApoapsisPosition;
        public double PlannedPeriapsisAltitudeMeters = double.NaN;
        public double PlannedApoapsisAltitudeMeters = double.NaN;
        public double PlannedPeriapsisSpeedMps = double.NaN;
        public double PlannedApoapsisSpeedMps = double.NaN;

        /// <summary>Body the planned apsides are measured around; null when unknown.</summary>
        public string PlannedApsisBodyName;

        public string Note = string.Empty;
        public bool ForecastValid;
        public bool ForecastTruncated;
        public double ForecastSeconds;
        public double ForecastWindowSeconds;

        /// <summary>End of the coast-only (no maneuver) line, seconds from now.</summary>
        public double BallisticWindowSeconds;

        /// <summary>End of the planned (maneuver) line, seconds from now; 0 when no plan is drawn.</summary>
        public double PlannedWindowSeconds;

        public double HistorySeconds;

        /// <summary>True when the centred body has a history/forecast arc for the current sliders.</summary>
        public bool HasBodyHistory;
        public bool HasBodyForecast;

        /// <summary>True when the standing ship's path was built from the site ephemeris, not live samples.</summary>
        public bool HasSurfaceHistory;
        public bool HasSurfaceForecast;

        // ── Planet / surface view ─────────────────────────────────────────────
        public NavViewMode ViewMode;
        public OrbitingBody DominantBody;
        public int DominantBodyIndex = -1;
        public bool HasSurfaceFrame;
        public double ShipAltitudeMeters;
        public double ShipHeightAboveTerrain;
        public double ShipLatitudeDegrees;
        public double ShipLongitudeDegrees;
        public double ShipVerticalSpeedMps;
        public double BodyRadiusMeters;
        public double BodySeaLevelMeters;
        public double BodyTerrainAmplitudeMeters = 1000d;
        public Vector3d SunDirection = new Vector3d(1d, 0d, 0d);
        public double ShipGroundEastMps;
        public double ShipGroundNorthMps;
        public Vector3d ShipUp = new Vector3d(0d, 0d, 1d);
        public Vector3d ShipEast = new Vector3d(1d, 0d, 0d);
        public Vector3d ShipNorth = new Vector3d(0d, 1d, 0d);

        public string FrameName
        {
            get
            {
                if (FrameIndex >= 0 && FrameIndex < Bodies.Count)
                {
                    return Bodies[FrameIndex].Name;
                }

                return "?";
            }
        }

        /// <summary>
        /// Build the map. allowForecast=false (surface/contact/build) still gives the
        /// real body positions and the real history trail. <paramref name="parked"/>
        /// means the ship is standing on <see cref="DominantBody"/>: its history and
        /// forecast are then the site path from the ephemerides (the planet's orbital
        /// and rotational motion), not a ballistic integration.
        /// </summary>
        public void Rebuild(
            StarSystem system,
            Ship ship,
            double timeSeconds,
            int frameIndex,
            int centerIndex,
            bool[] orbitVisible,
            double forecastSeconds,
            double historySeconds,
            List<NavShipSample> history,
            bool allowForecast,
            bool parked = false,
            ManeuverPlan plan = null,
            double ispS = 0d)
        {
            Lines.Clear();
            Bodies.Clear();
            History.Clear();
            BurnMarkers.Clear();
            Impacts.Clear();
            AppliedManeuverCount = 0;
            PlanConflict = false;
            PlanOutsideWindow = false;
            TimeSeconds = timeSeconds;
            FrameIndex = frameIndex;
            CenterIndex = centerIndex;
            HasShip = ship != null;
            ForecastValid = false;
            ForecastTruncated = false;
            ForecastWindowSeconds = 0d;
            BallisticWindowSeconds = 0d;
            PlannedWindowSeconds = 0d;
            HasBodyHistory = false;
            HasBodyForecast = false;
            HasSurfaceHistory = false;
            HasSurfaceForecast = false;
            Note = string.Empty;
            ForecastSeconds = forecastSeconds;
            HistorySeconds = historySeconds;
            HasApsides = false;
            HasApoapsis = false;
            PeriapsisAltitudeMeters = double.NaN;
            ApoapsisAltitudeMeters = double.NaN;
            PeriapsisSpeedMps = double.NaN;
            ApoapsisSpeedMps = double.NaN;
            HasPlannedApsides = false;
            HasPlannedApoapsis = false;
            PlannedPeriapsisAltitudeMeters = double.NaN;
            PlannedApoapsisAltitudeMeters = double.NaN;
            PlannedPeriapsisSpeedMps = double.NaN;
            PlannedApoapsisSpeedMps = double.NaN;
            PlannedApsisBodyName = null;
            HasSurfaceFrame = false;
            ViewMode = NavViewMode.Orbits;
            DominantBody = null;
            DominantBodyIndex = -1;
            ShipPosition = ship != null ? ship.Position : Vector3d.Zero;
            ShipVelocity = ship != null ? ship.Velocity : Vector3d.Zero;
            if (history != null)
            {
                History.AddRange(history);
            }

            if (system == null || system.AllBodies.Count == 0)
            {
                Note = "СИСТЕМА НЕ ЗАГРУЖЕНА";
                return;
            }

            if (frameIndex < 0 || frameIndex >= system.AllBodies.Count)
            {
                frameIndex = 0;
            }

            FrameIndex = frameIndex;
            OrbitingBody frame = system.AllBodies[frameIndex];
            frame.EvaluateWorldState(timeSeconds, out FramePosition, out Vector3d frameVelocity);
            ShipIndex = system.AllBodies.Count;

            Vector3d shipRelative = HasShip ? ShipPosition - FramePosition : Vector3d.Zero;
            FrameRelativeShipPosition = shipRelative;
            FrameRelativeShipVelocity = HasShip ? ShipVelocity - frameVelocity : Vector3d.Zero;

            // Markers first: they are needed for the frame body itself and for labels.
            for (int i = 0; i < system.AllBodies.Count; i++)
            {
                OrbitingBody body = system.AllBodies[i];
                body.EvaluateWorldState(timeSeconds, out Vector3d position, out _);
                Bodies.Add(new NavBodyMarker
                {
                    Index = i,
                    Name = (body.Name ?? "?").ToUpperInvariant(),
                    RadiusMeters = body.Radius,
                    Position = position - FramePosition,
                    IsFrame = i == frameIndex,
                    Body = body,
                });
            }

            // Map plane: the reference body's equatorial plane, fixed north-up.
            // It must not follow the ship's instantaneous r×v: pitching a rocket
            // during ascent used to rotate the whole globe on the tablet.
            BuildPlane(frame, out OrbitNormal, out OrbitRight);
            BuildApsides(system, ship, timeSeconds, frame);
            BuildSurfaceFrame(system, ship, timeSeconds);

            if (centerIndex == ShipIndex && HasShip)
            {
                CenterPosition = shipRelative;
            }
            else if (centerIndex >= 0 && centerIndex < Bodies.Count)
            {
                CenterPosition = Bodies[centerIndex].Position;
            }
            else
            {
                CenterPosition = Vector3d.Zero;
            }

            BuildBodyTrails(system, timeSeconds, frameIndex, orbitVisible,
                historySeconds, forecastSeconds);

            bool shipLinesVisible = orbitVisible == null
                || ShipIndex >= orbitVisible.Length
                || orbitVisible[ShipIndex];

            // A parked craft is carried by the site frame. Its site path over one
            // rotation is a ring at ~4 m AGL and reads as a very low orbit, so it is
            // deliberately NOT drawn: the standing marker is enough. The surface
            // history/forecast builders stay available for a future local-track view.
            bool surfacePath = parked && HasShip && HasSurfaceFrame && DominantBody != null;

            if (shipLinesVisible && historySeconds > 0d && HasShip && !surfacePath)
            {
                if (history != null)
                {
                    BuildHistory(system, frame, timeSeconds, historySeconds, history);
                }
            }

            if (shipLinesVisible && HasShip && ship.Mass > 0d && allowForecast)
            {
                BuildForecast(system, ship, timeSeconds, frame, forecastSeconds, plan, ispS);
            }

            if (!allowForecast && !ForecastValid)
            {
                Note = surfacePath
                    ? "АППАРАТ НА ПОВЕРХНОСТИ · ОРБИТА НЕ СТРОИТСЯ"
                    : "ПРОГНОЗ ВЫКЛЮЧЕН: НЕТ СВОБОДНОГО ПОЛЁТА (КОНТАКТ/ПОВЕРХНОСТЬ/СТРОЙКА)";
            }

            if (HasShip && string.IsNullOrEmpty(Note))
            {
                Note = ForecastValid ? "БАЛЛИСТИКА · N-BODY + ЭФЕМЕРИДЫ" : "ЭФЕМЕРИДЫ · N-BODY";
            }
        }

        /// <summary>
        /// Apsis markers of the CURRENT osculating orbit around the dominant body,
        /// placed in the SELECTED reference frame. The orbit itself does not depend
        /// on the frame, but the map draws every point as
        /// (world point - frame body at that same time), so the marker is evaluated
        /// at the moment the craft really reaches the apsis. Anchoring it at "now"
        /// left it behind when the frame body moved along its own path, which is
        /// what made Ap look frozen after switching SOL ↔ TERRA. Altitude and speed
        /// stay body-relative: they describe the orbit around the dominant body, not
        /// a fictitious conic around the frame origin.
        /// </summary>
        private void BuildApsides(StarSystem system, Ship ship, double time, OrbitingBody frame)
        {
            HasApsides = false;
            HasApoapsis = false;
            PeriapsisAltitudeMeters = double.NaN;
            ApoapsisAltitudeMeters = double.NaN;
            PeriapsisSpeedMps = double.NaN;
            ApoapsisSpeedMps = double.NaN;
            if (ship == null || frame == null)
            {
                return;
            }

            OrbitingBody dominant = system.FindDominantBody(ship.Position, time);
            if (!TryBuildApsisSet(dominant, frame, time, ship.Position, ship.Velocity, out ApsisSet apsis))
            {
                return;
            }

            HasApsides = apsis.HasPeriapsis;
            PeriapsisPosition = apsis.PeriapsisPosition;
            PeriapsisAltitudeMeters = apsis.PeriapsisAltitudeMeters;
            PeriapsisSpeedMps = apsis.PeriapsisSpeedMps;
            HasApoapsis = apsis.HasApoapsis;
            if (apsis.HasApoapsis)
            {
                ApoapsisPosition = apsis.ApoapsisPosition;
                ApoapsisAltitudeMeters = apsis.ApoapsisAltitudeMeters;
                ApoapsisSpeedMps = apsis.ApoapsisSpeedMps;
            }
            else
            {
                // Unbound: no apoapsis, and the marker is never drawn. Keep the
                // body centre as the placeholder (same as before the refactor).
                dominant.EvaluateWorldState(time, out Vector3d bodyPosition, out _);
                ApoapsisPosition = bodyPosition - FramePosition;
            }
        }

        /// <summary>One osculating orbit's apsides: map-frame points plus body-relative data.</summary>
        private struct ApsisSet
        {
            public bool HasPeriapsis;
            public bool HasApoapsis;
            public Vector3d PeriapsisPosition;
            public Vector3d ApoapsisPosition;
            public double PeriapsisAltitudeMeters;
            public double ApoapsisAltitudeMeters;
            public double PeriapsisSpeedMps;
            public double ApoapsisSpeedMps;
        }

        /// <summary>
        /// Apsides of the osculating conic of an arbitrary state vector around
        /// <paramref name="dominant"/> at <paramref name="stateTime"/>. The ballistic
        /// orbit ("now") and the post-burn orbit (at the last ignition) use the same
        /// math, so the two marker pairs stay directly comparable.
        /// </summary>
        private static bool TryBuildApsisSet(OrbitingBody dominant, OrbitingBody frame, double stateTime,
            Vector3d position, Vector3d velocity, out ApsisSet apsis)
        {
            apsis = default;
            if (dominant == null || frame == null)
            {
                return false;
            }

            dominant.EvaluateWorldState(stateTime, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            double mu = dominant.ResolveStandardGravitationalParameter();
            Vector3d r = position - bodyPosition;
            Vector3d v = velocity - bodyVelocity;
            OrbitalElements elements = OrbitalElements.FromState(r, v, mu);
            if (!elements.IsValid || elements.Eccentricity <= 1e-9d)
            {
                return false;
            }

            double toPeriapsis = 0d;
            double toApoapsis = 0d;
            if (!elements.TryGetTimeToApsides(mu, out toPeriapsis, out toApoapsis))
            {
                toPeriapsis = 0d;
                toApoapsis = 0d;
            }

            Vector3d eDirection = elements.EccentricityVector / elements.Eccentricity;
            apsis.HasPeriapsis = true;
            apsis.PeriapsisPosition = ApsisPointInFrame(dominant, frame, stateTime, toPeriapsis,
                eDirection, elements.PeriapsisDistance);
            apsis.PeriapsisAltitudeMeters = elements.PeriapsisDistance - dominant.Radius;
            apsis.PeriapsisSpeedMps = ApsisSpeed(elements.SpecificAngularMomentum, elements.PeriapsisDistance);
            if (elements.IsBound && double.IsFinite(elements.ApoapsisDistance))
            {
                apsis.HasApoapsis = true;
                apsis.ApoapsisPosition = ApsisPointInFrame(dominant, frame, stateTime, toApoapsis,
                    -eDirection, elements.ApoapsisDistance);
                apsis.ApoapsisAltitudeMeters = elements.ApoapsisDistance - dominant.Radius;
                apsis.ApoapsisSpeedMps = ApsisSpeed(elements.SpecificAngularMomentum, elements.ApoapsisDistance);
            }

            return true;
        }

        /// <summary>
        /// Apsides of the orbit the plan flies: the osculating conic immediately
        /// after the last applied burn. Like the ballistic pair they are placed in
        /// the reference frame at the time the craft really reaches each apsis, so
        /// they sit on the dashed maneuver path.
        /// </summary>
        private void BuildPlannedApsides(StarSystem system, OrbitingBody frame, double burnTime,
            Vector3d position, Vector3d velocity)
        {
            HasPlannedApsides = false;
            HasPlannedApoapsis = false;
            PlannedPeriapsisAltitudeMeters = double.NaN;
            PlannedApoapsisAltitudeMeters = double.NaN;
            PlannedPeriapsisSpeedMps = double.NaN;
            PlannedApoapsisSpeedMps = double.NaN;
            PlannedApsisBodyName = null;
            if (system == null || frame == null)
            {
                return;
            }

            OrbitingBody dominant = system.FindDominantBody(position, burnTime);
            if (!TryBuildApsisSet(dominant, frame, burnTime, position, velocity, out ApsisSet apsis))
            {
                return;
            }

            HasPlannedApsides = apsis.HasPeriapsis;
            PlannedPeriapsisPosition = apsis.PeriapsisPosition;
            PlannedPeriapsisAltitudeMeters = apsis.PeriapsisAltitudeMeters;
            PlannedPeriapsisSpeedMps = apsis.PeriapsisSpeedMps;
            HasPlannedApoapsis = apsis.HasApoapsis;
            PlannedApoapsisPosition = apsis.ApoapsisPosition;
            PlannedApoapsisAltitudeMeters = apsis.ApoapsisAltitudeMeters;
            PlannedApoapsisSpeedMps = apsis.ApoapsisSpeedMps;
            PlannedApsisBodyName = (dominant.Name ?? "?").ToUpperInvariant();
        }

        /// <summary>
        /// Apsis point in the map frame: the dominant body's conic apse, translated by
        /// the frame body's position AT THE APSIS TIME (not at "now"). The trajectory
        /// lines use the same per-sample translation, so the marker stays on the drawn
        /// frame-relative path instead of hanging at the previous frame's location.
        /// </summary>
        private static Vector3d ApsisPointInFrame(OrbitingBody dominant, OrbitingBody frame,
            double time, double secondsToApsis, Vector3d direction, double distance)
        {
            if (double.IsFinite(secondsToApsis) && Math.Abs(secondsToApsis) > 1e-6d)
            {
                try
                {
                    double t = time + secondsToApsis;
                    dominant.EvaluateWorldState(t, out Vector3d bodyAtApsis, out _);
                    frame.EvaluateWorldState(t, out Vector3d frameAtApsis, out _);
                    return (bodyAtApsis - frameAtApsis) + (direction * distance);
                }
                catch (EphemerisRangeException)
                {
                    // Apsis outside the baked span: fall back to the current instant.
                }
            }

            dominant.EvaluateWorldState(time, out Vector3d bodyPosition, out _);
            frame.EvaluateWorldState(time, out Vector3d framePosition, out _);
            return (bodyPosition - framePosition) + (direction * distance);
        }

        /// <summary>Speed at an apsis: the specific angular momentum h = r·v is constant, so v = h / r.</summary>
        private static double ApsisSpeed(double specificAngularMomentum, double distance)
        {
            return distance > 0d && double.IsFinite(specificAngularMomentum)
                ? specificAngularMomentum / distance
                : double.NaN;
        }

        /// <summary>
        /// Real local surface frame: the ship's dominant body, lat/lon, height above
        /// the real terrain and its local east/north/up axes. The map uses it for the
        /// terrain view; the orbit chart keeps using the frame/inertial geometry.
        /// </summary>
        private void BuildSurfaceFrame(StarSystem system, Ship ship, double time)
        {
            HasSurfaceFrame = false;
            ViewMode = NavViewMode.Orbits;

            if (ship == null)
            {
                return;
            }

            OrbitingBody dominant = system.FindDominantBody(ship.Position, time);
            if (dominant == null)
            {
                return;
            }

            DominantBody = dominant;
            DominantBodyIndex = IndexOf(system, dominant);
            BodyRadiusMeters = dominant.Radius;
            dominant.EvaluateWorldState(time, out Vector3d bodyPosition, out Vector3d bodyVelocity);

            Vector3d relative = ship.Position - bodyPosition;
            double radius = relative.Magnitude;
            if (!(radius > 1e-6d))
            {
                return;
            }

            ShipUp = relative / radius;
            ShipAltitudeMeters = radius - dominant.Radius;
            Vector3d relativeVelocity = ship.Velocity - bodyVelocity;
            ShipVerticalSpeedMps = Vector3d.Dot(relativeVelocity, ShipUp);

            dominant.SurfaceLatLonAt(ship.Position, time, out double latitudeDegrees, out double longitudeDegrees);
            ShipLatitudeDegrees = latitudeDegrees;
            ShipLongitudeDegrees = longitudeDegrees;

            ITerrainModel terrain = dominant.Terrain;
            double terrainHeight = 0d;
            if (terrain != null)
            {
                double latRad = latitudeDegrees * (Math.PI / 180d);
                double lonRad = longitudeDegrees * (Math.PI / 180d);
                terrainHeight = terrain.GetHeightMeters(dominant, latRad, lonRad);
                BodySeaLevelMeters = terrain.GetSeaLevelMeters();
            }
            else
            {
                BodySeaLevelMeters = double.NegativeInfinity;
            }

            ShipHeightAboveTerrain = Math.Max(0d, radius - (dominant.Radius + terrainHeight));
            HeightfieldTerrain heightfield = terrain as HeightfieldTerrain;
            BodyTerrainAmplitudeMeters = heightfield != null && heightfield.AmplitudeMeters > 1d
                ? heightfield.AmplitudeMeters
                : 1000d;

            Vector3d east = Vector3d.Cross(dominant.SpinAxis, ShipUp);
            if (east.SqrMagnitude < 1e-12d)
            {
                east = Vector3d.Cross(new Vector3d(0d, 0d, 1d), ShipUp);
            }

            if (east.SqrMagnitude < 1e-12d)
            {
                east = Vector3d.Cross(new Vector3d(0d, 1d, 0d), ShipUp);
            }

            ShipEast = east.Normalized;
            ShipNorth = Vector3d.Cross(ShipUp, ShipEast).Normalized;
            ShipGroundEastMps = Vector3d.Dot(relativeVelocity, ShipEast);
            ShipGroundNorthMps = Vector3d.Dot(relativeVelocity, ShipNorth);

            OrbitingBody root = system.Root;
            root.EvaluateWorldState(time, out Vector3d rootPosition, out _);
            Vector3d toSun = rootPosition - bodyPosition;
            SunDirection = toSun.SqrMagnitude > 1e-6d ? toSun.Normalized : new Vector3d(0d, 0d, 1d);

            HasSurfaceFrame = true;
            // The map is a planet view first: the whole body must be visible with the
            // craft on it. The local surface relief stays available in the model for
            // a future explicit "surface map" toggle, but it is not the default.
            ViewMode = NavViewMode.Orbits;
        }

        /// <summary>
        /// Stable map camera basis. The normal is the reference body's spin axis;
        /// the right direction is a fixed world axis projected into the equatorial
        /// plane. Deliberately independent of the ship: the old ship r×v basis
        /// re-oriented the whole globe whenever the craft pitched during ascent.
        /// </summary>
        private static void BuildPlane(OrbitingBody frame, out Vector3d normal, out Vector3d right)
        {
            normal = frame.SpinAxis.Normalized;
            if (!normal.IsFinite || normal.SqrMagnitude < 0.5d)
            {
                normal = new Vector3d(0d, 0d, 1d);
            }

            Vector3d reference = new Vector3d(0d, 0d, 1d);
            if (Math.Abs(Vector3d.Dot(reference, normal)) > 0.9d)
            {
                reference = new Vector3d(1d, 0d, 0d);
            }

            right = Vector3d.Cross(normal, reference).Normalized;
            if (!right.IsFinite || right.SqrMagnitude < 0.5d)
            {
                right = Vector3d.Cross(normal, new Vector3d(0d, 1d, 0d)).Normalized;
            }

            normal = normal.Normalized;
            // Complete the right-handed (right, up, normal) triple.
            right = (right - (normal * Vector3d.Dot(right, normal))).Normalized;
        }

        /// <summary>Orbital period of a body around its parent (parent + subtree μ), or +∞ if the rail is not an ellipse.</summary>
        private static double OrbitalPeriodSeconds(OrbitingBody body)
        {
            if (body == null || body.Parent == null)
            {
                return double.PositiveInfinity;
            }

            double mu = body.Parent.ResolveStandardGravitationalParameter()
                + body.ResolveSubtreeStandardGravitationalParameter();
            double semiMajor = body.SemiMajorAxis;
            double eccentricity = body.Eccentricity;
            if (!(mu > 0d) || !(semiMajor > 0d) || !(eccentricity >= 0d) || !(eccentricity < 1d))
            {
                return double.PositiveInfinity;
            }

            double period = 2d * Math.PI * Math.Sqrt((semiMajor * semiMajor * semiMajor) / mu);
            return double.IsFinite(period) && period > 0d ? period : double.PositiveInfinity;
        }

        /// <summary>
        /// History/forecast of EVERY body with a parent, over the slider windows and
        /// in the selected frame: point(t) = body(t) − frame(t). The per-body dot in
        /// the frames table is the visibility switch.
        ///
        /// Only the selected span is drawn — never a full unflown period. At T+00:43
        /// a complete lunar ellipse reads as a prediction that ignores the sliders
        /// (the "green phantom" regression), and it is not the path the body took in
        /// that ISK during the shown time. A body's path is a known ephemeris, so it
        /// can be sampled into the past and the future; the sliders select the span.
        /// </summary>
        private void BuildBodyTrails(StarSystem system, double time, int frameIndex,
            bool[] orbitVisible, double historySeconds, double forecastSeconds)
        {
            OrbitingBody frame = system.AllBodies[frameIndex];

            // Body paths are ephemeris samples, not an N-body integration: the
            // 60-day ship-forecast budget does not apply here. The slider value is
            // honoured (up to the UI ceiling).
            double historyWindow = Math.Min(historySeconds, MaxHistorySeconds);
            double forecastWindow = Math.Min(forecastSeconds, MaxHistorySeconds);
            if (!(historyWindow > 1d) && !(forecastWindow > 1d))
            {
                return;
            }

            for (int i = 0; i < system.AllBodies.Count; i++)
            {
                if (i == frameIndex)
                {
                    // The frame body's path in its own frame is identically zero.
                    continue;
                }

                if (orbitVisible != null && i < orbitVisible.Length && !orbitVisible[i])
                {
                    continue;
                }

                OrbitingBody body = system.AllBodies[i];
                if (body.Parent == null)
                {
                    continue;
                }

                // A short-period moon over a multi-day slider would be a scribble of
                // dozens of overlapping revolutions sampled by 160 chords. The same
                // three-revolution budget as the craft forecast keeps the line
                // readable and dense enough to be a curve.
                double bodyHistory = historyWindow;
                double bodyForecast = forecastWindow;
                double period = OrbitalPeriodSeconds(body);
                if (double.IsFinite(period))
                {
                    double threeOrbits = 3d * period;
                    bodyHistory = Math.Min(bodyHistory, threeOrbits);
                    bodyForecast = Math.Min(bodyForecast, threeOrbits);
                }

                if (bodyHistory > 1d)
                {
                    NavLine history = SampleBodyPath(body, frame, time - bodyHistory, time);
                    if (history != null)
                    {
                        history.Kind = NavLineKind.BodyHistory;
                        history.BodyIndex = i;
                        history.ParentIndex = IndexOf(system, body.Parent);
                        Lines.Add(history);
                        HasBodyHistory = true;
                    }
                }

                if (bodyForecast > 1d)
                {
                    NavLine forecast = SampleBodyPath(body, frame, time, time + bodyForecast);
                    if (forecast != null)
                    {
                        forecast.Kind = NavLineKind.BodyForecast;
                        forecast.BodyIndex = i;
                        forecast.ParentIndex = IndexOf(system, body.Parent);
                        forecast.Dashed = true;
                        Lines.Add(forecast);
                        HasBodyForecast = true;
                    }
                }
            }
        }

        private static NavLine SampleBodyPath(OrbitingBody body, OrbitingBody frame,
            double start, double end)
        {
            if (body == null || frame == null || !(end > start)
                || !double.IsFinite(start) || !double.IsFinite(end))
            {
                return null;
            }

            int n = BodyOrbitSamples;
            var line = new NavLine
            {
                StartTimeS = start,
                EndTimeS = start,
            };
            for (int k = 0; k < n; k++)
            {
                double t = start + ((end - start) * k / (n - 1d));
                try
                {
                    body.EvaluateWorldState(t, out Vector3d p, out _);
                    frame.EvaluateWorldState(t, out Vector3d fp, out _);
                    line.Points.Add(p - fp);
                    line.EndTimeS = t;
                }
                catch (EphemerisRangeException)
                {
                    break;
                }
            }

            return line.Points.Count >= 2 ? line : null;
        }

        /// <summary>
        /// A standing craft follows its landing site: the site is fixed in the body
        /// frame, so the planet's rotation and heliocentric motion are the craft's
        /// real history — computable backwards from the same ephemerides.
        /// </summary>
        private void BuildSurfaceHistory(double time, OrbitingBody frame, double historySeconds)
        {
            if (!HasSurfaceFrame || DominantBody == null)
            {
                return;
            }

            double window = Math.Min(historySeconds, MaxHistorySeconds);
            if (ReferenceEquals(frame, DominantBody) && DominantBody.RotationPeriodSeconds > 0d)
            {
                // In the planet's own frame the site path is one closed circle.
                window = Math.Min(window, DominantBody.RotationPeriodSeconds);
            }

            if (!(window > 1d))
            {
                return;
            }

            var line = new NavLine
            {
                Kind = NavLineKind.ShipHistory,
                StartTimeS = double.NaN,
                EndTimeS = time,
            };

            int n = ForecastSamples;
            for (int k = 0; k < n; k++)
            {
                double t = time - window + (window * k / (n - 1d));
                try
                {
                    DominantBody.GetSurfaceState(ShipLatitudeDegrees, ShipLongitudeDegrees,
                        Math.Max(0d, ShipAltitudeMeters), t,
                        out Vector3d position, out _);
                    frame.EvaluateWorldState(t, out Vector3d framePosition, out _);
                    line.Points.Add(position - framePosition);
                    if (double.IsNaN(line.StartTimeS))
                    {
                        line.StartTimeS = t;
                    }
                }
                catch (EphemerisRangeException)
                {
                    break;
                }
            }

            if (line.Points.Count >= 1 && HasShip)
            {
                line.Points.Add(FrameRelativeShipPosition);
                line.EndTimeS = time;
            }

            if (line.Points.Count >= 2)
            {
                Lines.Add(line);
                HasSurfaceHistory = true;
            }
        }

        /// <summary>
        /// Forward site path for a standing craft: planet orbit + rotation. This is
        /// a real prediction of where the craft will be, not a ballistic coast, so
        /// it is built while landed instead of switching the forecast off.
        /// </summary>
        private void BuildSurfaceForecast(double time, OrbitingBody frame, double forecastSeconds)
        {
            if (!HasSurfaceFrame || DominantBody == null)
            {
                return;
            }

            double window = Math.Min(forecastSeconds, MaxForecastSeconds);
            bool rotationCap = false;
            if (ReferenceEquals(frame, DominantBody) && DominantBody.RotationPeriodSeconds > 0d
                && window > DominantBody.RotationPeriodSeconds)
            {
                // In the planet frame the site circles the body once per rotation;
                // repeating the same circle for days is noise.
                window = DominantBody.RotationPeriodSeconds;
                rotationCap = true;
            }

            if (!(window > 1d))
            {
                return;
            }

            var line = new NavLine
            {
                Kind = NavLineKind.ShipForecast,
                Dashed = true,
                StartTimeS = time,
                EndTimeS = time,
            };

            int n = ForecastSamples;
            for (int k = 0; k < n; k++)
            {
                double t = time + (window * k / (n - 1d));
                try
                {
                    DominantBody.GetSurfaceState(ShipLatitudeDegrees, ShipLongitudeDegrees,
                        Math.Max(0d, ShipAltitudeMeters), t,
                        out Vector3d position, out _);
                    frame.EvaluateWorldState(t, out Vector3d framePosition, out _);
                    line.Points.Add(position - framePosition);
                    line.EndTimeS = t;
                }
                catch (EphemerisRangeException)
                {
                    break;
                }
            }

            if (line.Points.Count < 2)
            {
                return;
            }

            Lines.Add(line);
            ForecastValid = true;
            HasSurfaceForecast = true;
            ForecastWindowSeconds = line.EndTimeS - time;
            ForecastTruncated = rotationCap || ForecastWindowSeconds < forecastSeconds - 1e-6d;
            Note = rotationCap
                ? "ПРОГНОЗ СТОЯНКИ · 1 ОБОРОТ"
                : "ПРОГНОЗ СТОЯНКИ · ВРАЩЕНИЕ + ОРБИТА";
        }

        private void BuildHistory(StarSystem system, OrbitingBody frame, double time, double historySeconds,
            List<NavShipSample> history)
        {
            double start = time - historySeconds;
            var line = new NavLine
            {
                Kind = NavLineKind.ShipHistory,
                StartTimeS = double.NaN,
                EndTimeS = time,
            };

            for (int i = 0; i < history.Count; i++)
            {
                NavShipSample sample = history[i];
                if (!double.IsFinite(sample.TimeSeconds) || sample.TimeSeconds < start || sample.TimeSeconds > time + 1e-6d)
                {
                    continue;
                }

                try
                {
                    frame.EvaluateWorldState(sample.TimeSeconds, out Vector3d fp, out _);
                    line.Points.Add(sample.Position - fp);
                    if (double.IsNaN(line.StartTimeS))
                    {
                        line.StartTimeS = sample.TimeSeconds;
                    }
                }
                catch (EphemerisRangeException)
                {
                    break;
                }
            }

            if (HasShip)
            {
                line.Points.Add(FrameRelativeShipPosition);
            }

            if (line.Points.Count >= 2)
            {
                Lines.Add(line);
            }
        }

        private void BuildForecast(StarSystem system, Ship ship, double time, OrbitingBody frame,
            double forecastSeconds, ManeuverPlan plan, double ispS)
        {
            if (!(forecastSeconds > 1d) || !(ship.Mass > 0d))
            {
                return;
            }

            double requested = Math.Min(forecastSeconds, MaxForecastSeconds);
            double window = requested;
            ForecastTruncated = window < forecastSeconds - 1e-6d;
            bool revolutionsLimited = false;
            double orbitalPeriod = double.NaN;

            // A map with hundreds of overlapping revolutions is noise: show at most
            // three revolutions of the current osculating orbit. The old 6 h floor
            // could stretch that to four or more revolutions for a low orbit, so the
            // same 160 samples were spread over a longer path (visible corners).
            OrbitingBody dominant = system.FindDominantBody(ship.Position, time);
            double impactSeconds = double.PositiveInfinity;
            if (dominant != null)
            {
                dominant.EvaluateWorldState(time, out Vector3d bodyPosition, out Vector3d bodyVelocity);
                double mu = dominant.ResolveStandardGravitationalParameter();
                Vector3d r = ship.Position - bodyPosition;
                Vector3d v = ship.Velocity - bodyVelocity;
                OrbitalElements elements = OrbitalElements.FromState(r, v, mu);
                if (elements.IsValid && elements.IsBound && double.IsFinite(elements.OrbitalPeriodSeconds))
                {
                    orbitalPeriod = elements.OrbitalPeriodSeconds;
                    double threeOrbits = 3d * elements.OrbitalPeriodSeconds;
                    if (threeOrbits > 1d && threeOrbits < window)
                    {
                        window = threeOrbits;
                        revolutionsLimited = true;
                    }
                }

                // A dive is not necessarily a closed orbit: a hyperbolic approach into
                // the ground is the steepest case of all, and it needs the same dense
                // sampling up to the contact as a decaying ellipse does.
                if (elements.IsValid)
                {
                    impactSeconds = EstimateSurfaceImpactSeconds(elements, dominant.Radius, mu);
                }
            }

            // Planned burns enter the prediction exactly where the countdown will
            // fire them. Conflict entries are never applied (same rule as the live
            // AdvanceTime), so the line changes at the point the plan stops being
            // executable. A maneuver with ΔV = 0 is still listed: the card asks for
            // the time first, and the map must already show where that ignition
            // happens even though it changes nothing yet.
            lastPostBurnValid = false;
            var burns = new List<PlannedBurn>();
            bool anyBurnDv = false;
            if (plan != null)
            {
                PlanConflict = plan.ConflictIndex >= 0;
                int valid = plan.ValidCount;
                for (int i = 0; i < valid; i++)
                {
                    Maneuver maneuver = plan[i];
                    if (maneuver == null)
                    {
                        continue;
                    }

                    double ignite = time + maneuver.TimeToStartS;
                    if (maneuver.TimeToStartS <= 1e-6d)
                    {
                        continue;
                    }

                    if (ignite > time + requested + 1e-6d)
                    {
                        PlanOutsideWindow = true;
                        continue;
                    }

                    burns.Add(new PlannedBurn
                    {
                        Item = maneuver,
                        TimeSeconds = ignite,
                        Number = i + 1,
                    });

                    if (maneuver.DvMagnitude > 1e-9d)
                    {
                        anyBurnDv = true;
                    }
                }

                burns.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            }

            // The 3-orbit visual cap would hide the result of a burn scheduled near
            // the end of that window (the default LEO plan is added at ~1/10 of the
            // slider, which is already almost 3 orbits). Extend just enough to show
            // two post-burn orbits, never beyond what the user asked for or six
            // initial orbits (a longer window becomes a scribble at 160 samples).
            if (burns.Count > 0 && double.IsFinite(orbitalPeriod) && orbitalPeriod > 1d)
            {
                double lastBurn = burns[burns.Count - 1].TimeSeconds - time;
                double wanted = Math.Min(requested,
                    Math.Min(lastBurn + (2d * orbitalPeriod), 6d * orbitalPeriod));
                if (wanted > window)
                {
                    window = wanted;
                }
            }

            if (burns.Count > 0)
            {
                int before = burns.Count;
                burns.RemoveAll(b => b.TimeSeconds > time + window + 1e-6d);
                if (burns.Count < before)
                {
                    PlanOutsideWindow = true;
                }
            }

            // A time-conflicting plan is only executable up to the first conflict;
            // cut the planned line there. The ballistic line keeps the full window:
            // it is the reference "what the craft does without the maneuver".
            double plannedWindow = window;
            if (plan != null && PlanConflict && double.IsFinite(plan.CutTimeS)
                && plan.CutTimeS > 0d)
            {
                plannedWindow = Math.Min(plannedWindow, Math.Max(0d, plan.CutTimeS));
            }

            // A maneuver orbit exists only when at least one burn really changes
            // the trajectory. Time-only maneuvers leave the solid coast line alone
            // and only get their ignition marker.
            bool planned = anyBurnDv && plannedWindow > 1d;

            // While the craft coasts it flies a fixed curve in world space, so a
            // rebuild can replay the previous integration (re-translated into the
            // current frame) instead of integrating again. That is the difference
            // between a map that redraws smoothly and one that stalls for a third of
            // a second on every tick — and the cache is dropped the moment anything
            // real changes (a burn, a new plan, another window or frame).
            int planVersion = plan != null ? plan.Version : -1;
            if (TryReplayForecast(system, ship, frame, time, window, plannedWindow, planned, planVersion))
            {
                return;
            }

            // The same force sources as SimulationRunner: N-body gravity (baked
            // ephemerides + the indirect star term + J2) and drag in atmospheres.
            // A planned burn is an impulse at ignition: the map is a plan view, and
            // the card shows the finite burn time separately.
            int n = ForecastSamples;
            double maxDt = Math.Max(window / (n - 1d), plannedWindow / (n - 1d));
            var physics = new SpacecraftPhysics
            {
                ToleranceFrameSystem = system,
                MaxStepSize = Math.Min(Math.Max(OrbitIntegrator.DefaultMaxStepSize, maxDt), 6d * HourSeconds),

                // A preview, not the flown state: at the map's best zoom one pixel is
                // kilometres, so the production 1e-11 relative target buys nothing
                // visible and costs a lot. Measured on the real system, a 3-orbit LEO
                // forecast drops from ~460 ms to ~31 ms at 1e-7 while the drawn
                // polyline moves by centimetres — and that cost is what made the map
                // stall for a third of a second every tick.
                RelativeTolerance = ForecastRelativeTolerance,
                PositionAbsoluteTolerance = ForecastPositionToleranceMeters,
            };
            physics.Sources.Add(new CachedGravitySource(system));
            for (int i = 0; i < system.AllBodies.Count; i++)
            {
                OrbitingBody body = system.AllBodies[i];
                if (body.Atmosphere != null)
                {
                    physics.Sources.Add(new DragSource(body));
                }
            }

            // Two probes: the solid line is always the pure coast, the dashed line
            // is the plan. Keeping the coast visible is the whole point: after a
            // maneuver is added the player must still see where the craft would go
            // without it.
            //
            // The coast is sampled exactly at every ignition time, so the burn
            // marker sits on a real vertex of the drawn orbit. Only a plan with no
            // ΔV at all records its markers here: once a burn changes the orbit,
            // the later ignitions are on the dashed line, not on the coast.
            double ballisticReached = time;
            bool ballisticAny = false;
            string ballisticImpact = null;
            var ballisticProbe = new Ship(ship.Position, ship.Velocity, ship.Mass);
            BuildBallisticPath(physics, system, ballisticProbe, frame, time, window, impactSeconds,
                burns, !anyBurnDv, ref ballisticReached, ref ballisticAny, ref ballisticImpact);

            double plannedReached = time;
            bool plannedAny = false;
            string plannedImpact = null;
            if (planned)
            {
                var plannedProbe = new Ship(ship.Position, ship.Velocity, ship.Mass);
                BuildPlannedPath(physics, system, plannedProbe, frame, time, plannedWindow, burns, ispS,
                    ref plannedReached, ref plannedAny, ref plannedImpact);
            }

            BallisticWindowSeconds = ballisticAny ? Math.Max(0d, ballisticReached - time) : 0d;
            PlannedWindowSeconds = plannedAny ? Math.Max(0d, plannedReached - time) : 0d;

            if (ballisticAny || plannedAny)
            {
                ForecastValid = true;
                ForecastWindowSeconds = Math.Max(BallisticWindowSeconds, PlannedWindowSeconds);
                Note = plannedAny
                    ? (plannedImpact != null
                        ? "ПЛАН: " + plannedImpact
                        : PlannedNote(true, revolutionsLimited, window))
                    : (ballisticImpact ?? PlannedNote(false, revolutionsLimited, window));
            }
            else
            {
                string impact = plannedImpact ?? ballisticImpact;
                if (!string.IsNullOrEmpty(impact))
                {
                    Note = impact;
                }
            }

            StoreForecastCache(ship, frame, time, window, plannedWindow, planned, planVersion);
        }

        // ─── Forecast cache ───────────────────────────────────────────────────
        //
        // The forecast is the only part of a rebuild that costs real time (an
        // N-body integration of up to 160 chunks per probe). While the craft coasts,
        // that whole path is a fixed curve in world space: only the reference frame
        // it is drawn in moves. So the integration result is kept in ABSOLUTE
        // coordinates and replayed — re-translated into the current frame — until
        // something genuinely changes: a burn, a new plan, another window, another
        // frame, or the craft leaving the cached path.
        //
        // This is purely an optimisation: every replay is validated against the live
        // state, and a miss falls back to exactly the old integration path.

        private sealed class ForecastCache
        {
            public bool Valid;
            public int FrameIndex = -1;
            public int PlanVersion = -1;
            public bool HasPlan;
            public double WindowSeconds;
            public double PlannedWindowSeconds;
            public double StartTime;
            public double EndTime;
            public double ShipMass;
            public int AppliedManeuvers;
            public bool PlanConflicted;
            public bool PlanOutside;
            public bool Truncated;
            public double BallisticEnd;
            public double PlannedEnd;
            public string Note = string.Empty;
            public bool HasPostBurn;
            public double PostBurnTime;
            public Vector3d PostBurnPosition;
            public Vector3d PostBurnVelocity;
            public readonly List<CachedPath> Paths = new List<CachedPath>();
            public readonly List<CachedBurn> Burns = new List<CachedBurn>();
            public readonly List<CachedImpact> Impacts = new List<CachedImpact>();

            public void Clear()
            {
                Valid = false;
                Paths.Clear();
                Burns.Clear();
                Impacts.Clear();
                HasPostBurn = false;
                Note = string.Empty;
            }
        }

        private sealed class CachedPath
        {
            public NavLineKind Kind;
            public readonly List<NavShipSample> Samples = new List<NavShipSample>();
        }

        private sealed class CachedBurn
        {
            public int Index;
            public double TimeSeconds;
            public Vector3d Position;
            public double DvMs;
            public int FrameBodyIndex;
        }

        /// <summary>Ground contact of a cached forecast, in absolute coordinates.</summary>
        private sealed class CachedImpact
        {
            public double TimeSeconds;
            public Vector3d Position;
            public double SpeedMps;
            public int BodyIndex = -1;
            public string BodyName = "?";
            public bool Planned;
        }

        private readonly ForecastCache forecastCache = new ForecastCache();

        /// <summary>Post-burn state of the last plan build, in absolute coordinates (cache input).</summary>
        private bool lastPostBurnValid;
        private double lastPostBurnTime;
        private Vector3d lastPostBurnPosition;
        private Vector3d lastPostBurnVelocity;

        /// <summary>
        /// Replays the cached forecast when the craft still flies it. Returns false
        /// (and leaves the state untouched) on any doubt, so the caller integrates.
        /// </summary>
        private bool TryReplayForecast(StarSystem system, Ship ship, OrbitingBody frame, double time,
            double window, double plannedWindow, bool planned, int planVersion)
        {
            ForecastCache cache = forecastCache;
            if (!cache.Valid
                || cache.FrameIndex != FrameIndex
                || cache.PlanVersion != planVersion
                || cache.HasPlan != planned
                || Math.Abs(cache.WindowSeconds - window) > WindowMatchSeconds(window)
                || Math.Abs(cache.PlannedWindowSeconds - plannedWindow) > WindowMatchSeconds(plannedWindow)
                || Math.Abs(ship.Mass - cache.ShipMass) > 1e-9d * Math.Max(1d, cache.ShipMass)
                || cache.Paths.Count == 0
                || time < cache.StartTime - 1e-6d
                || time > cache.EndTime + 1e-6d)
            {
                return false;
            }

            // Still on the cached coast? Two independent checks:
            //  * the craft mass is unchanged — a burn (or staging) consumes propellant,
            //    and a coast does not, so this catches a burn at its first frame;
            //  * the craft sits on the cached curve — the threshold has to allow for
            //    the chord sag of a polyline whose vertices are ~60 s apart (kilometres),
            //    otherwise the check fails randomly on a perfectly good cache.
            if (!TrySampleWorld(cache.Paths[0], time, out Vector3d expected))
            {
                return false;
            }

            frame.EvaluateWorldState(time, out Vector3d frameNow, out _);
            double scale = Math.Max(1d, (ship.Position - frameNow).Magnitude);
            if ((ship.Position - expected).Magnitude > Math.Max(7_500d, scale * 5e-3d))
            {
                return false;
            }

            int replayed = 0;
            for (int i = 0; i < cache.Paths.Count; i++)
            {
                NavLine line = ReplayPath(cache.Paths[i], frame, time, FrameRelativeShipPosition);
                if (line != null)
                {
                    Lines.Add(line);
                    replayed++;
                }
            }

            if (replayed == 0)
            {
                // Nothing survives the trim (the window ends before "now"): let the
                // caller integrate instead of leaving the map with a missing path.
                return false;
            }

            if (cache.Burns.Count > 0)
            {
                for (int i = 0; i < cache.Burns.Count; i++)
                {
                    CachedBurn burn = cache.Burns[i];
                    if (burn.TimeSeconds < time - 1e-6d)
                    {
                        continue; // already fired: its result is the live orbit
                    }

                    try
                    {
                        frame.EvaluateWorldState(burn.TimeSeconds, out Vector3d frameAtBurn, out _);
                        BurnMarkers.Add(new NavBurnMarker
                        {
                            Index = burn.Index,
                            TimeSeconds = burn.TimeSeconds,
                            Position = burn.Position - frameAtBurn,
                            DvMs = burn.DvMs,
                            FrameBodyIndex = burn.FrameBodyIndex,
                        });
                    }
                    catch (EphemerisRangeException)
                    {
                        break;
                    }
                }
            }

            for (int i = 0; i < cache.Impacts.Count; i++)
            {
                CachedImpact impact = cache.Impacts[i];
                try
                {
                    frame.EvaluateWorldState(impact.TimeSeconds, out Vector3d frameAtImpact, out _);
                    Impacts.Add(new NavImpactMarker
                    {
                        TimeSeconds = impact.TimeSeconds,
                        Position = impact.Position - frameAtImpact,
                        SpeedMps = impact.SpeedMps,
                        BodyIndex = impact.BodyIndex,
                        BodyName = impact.BodyName,
                        Planned = impact.Planned,
                    });
                }
                catch (EphemerisRangeException)
                {
                    break;
                }
            }

            AppliedManeuverCount = cache.AppliedManeuvers;
            PlanConflict = cache.PlanConflicted;
            PlanOutsideWindow = cache.PlanOutside;
            ForecastTruncated = cache.Truncated;
            BallisticWindowSeconds = cache.BallisticEnd > 0d ? Math.Max(0d, cache.BallisticEnd - time) : 0d;
            PlannedWindowSeconds = cache.PlannedEnd > 0d ? Math.Max(0d, cache.PlannedEnd - time) : 0d;
            ForecastWindowSeconds = Math.Max(BallisticWindowSeconds, PlannedWindowSeconds);
            Note = cache.Note;

            if (cache.HasPostBurn)
            {
                BuildPlannedApsides(system, frame, cache.PostBurnTime,
                    cache.PostBurnPosition, cache.PostBurnVelocity);
            }

            ForecastValid = replayed > 0;
            return true;
        }

        /// <summary>Stores the just-integrated forecast in absolute coordinates for the next rebuild.</summary>
        private void StoreForecastCache(Ship ship, OrbitingBody frame, double time,
            double window, double plannedWindow, bool planned, int planVersion)
        {
            ForecastCache cache = forecastCache;
            cache.Clear();
            cache.FrameIndex = FrameIndex;
            cache.PlanVersion = planVersion;
            cache.HasPlan = planned;
            cache.WindowSeconds = window;
            cache.PlannedWindowSeconds = plannedWindow;
            cache.StartTime = time;
            cache.EndTime = time;
            cache.ShipMass = ship.Mass;
            cache.AppliedManeuvers = AppliedManeuverCount;
            cache.PlanConflicted = PlanConflict;
            cache.PlanOutside = PlanOutsideWindow;
            cache.Truncated = ForecastTruncated;
            cache.BallisticEnd = BallisticWindowSeconds > 0d ? time + BallisticWindowSeconds : 0d;
            cache.PlannedEnd = PlannedWindowSeconds > 0d ? time + PlannedWindowSeconds : 0d;
            cache.Note = Note;
            cache.HasPostBurn = lastPostBurnValid;
            cache.PostBurnTime = lastPostBurnTime;
            cache.PostBurnPosition = lastPostBurnPosition;
            cache.PostBurnVelocity = lastPostBurnVelocity;

            bool complete = true;
            for (int i = 0; i < Lines.Count; i++)
            {
                NavLine line = Lines[i];
                if (line.Kind != NavLineKind.ShipForecast && line.Kind != NavLineKind.ManeuverForecast)
                {
                    continue;
                }

                if (line.Times.Count != line.Points.Count)
                {
                    // A path with an unknown time per vertex cannot be re-translated;
                    // rather leave the cache empty than replay a wrong curve.
                    complete = false;
                    break;
                }

                var path = new CachedPath { Kind = line.Kind };
                for (int k = 0; k < line.Points.Count; k++)
                {
                    try
                    {
                        frame.EvaluateWorldState(line.Times[k], out Vector3d frameAtPoint, out _);
                        path.Samples.Add(new NavShipSample(line.Times[k], line.Points[k] + frameAtPoint));
                        if (line.Times[k] > cache.EndTime)
                        {
                            cache.EndTime = line.Times[k];
                        }
                    }
                    catch (EphemerisRangeException)
                    {
                        complete = false;
                        break;
                    }
                }

                if (!complete || path.Samples.Count < 2)
                {
                    complete = false;
                    break;
                }

                cache.Paths.Add(path);
            }

            if (cache.Paths.Count == 0)
            {
                complete = false;
            }

            for (int i = 0; i < BurnMarkers.Count && complete; i++)
            {
                NavBurnMarker marker = BurnMarkers[i];
                try
                {
                    frame.EvaluateWorldState(marker.TimeSeconds, out Vector3d frameAtBurn, out _);
                    cache.Burns.Add(new CachedBurn
                    {
                        Index = marker.Index,
                        TimeSeconds = marker.TimeSeconds,
                        Position = marker.Position + frameAtBurn,
                        DvMs = marker.DvMs,
                        FrameBodyIndex = marker.FrameBodyIndex,
                    });
                }
                catch (EphemerisRangeException)
                {
                    complete = false;
                }
            }

            for (int i = 0; i < Impacts.Count && complete; i++)
            {
                NavImpactMarker marker = Impacts[i];
                try
                {
                    frame.EvaluateWorldState(marker.TimeSeconds, out Vector3d frameAtImpact, out _);
                    cache.Impacts.Add(new CachedImpact
                    {
                        TimeSeconds = marker.TimeSeconds,
                        Position = marker.Position + frameAtImpact,
                        SpeedMps = marker.SpeedMps,
                        BodyIndex = marker.BodyIndex,
                        BodyName = marker.BodyName,
                        Planned = marker.Planned,
                    });
                }
                catch (EphemerisRangeException)
                {
                    complete = false;
                }
            }

            cache.Valid = complete;
            if (!complete)
            {
                cache.Clear();
            }
        }

        /// <summary>Samples a cached absolute path at a time, linearly between its vertices.</summary>
        private static bool TrySampleWorld(CachedPath path, double time, out Vector3d position)
        {
            position = default;
            List<NavShipSample> samples = path.Samples;
            if (samples.Count == 0 || time < samples[0].TimeSeconds - 1e-6d
                || time > samples[samples.Count - 1].TimeSeconds + 1e-6d)
            {
                return false;
            }

            int lo = 0;
            int hi = samples.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (samples[mid].TimeSeconds <= time)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            NavShipSample a = samples[lo];
            if (lo + 1 >= samples.Count || time <= a.TimeSeconds)
            {
                position = a.Position;
                return true;
            }

            NavShipSample b = samples[lo + 1];
            double span = b.TimeSeconds - a.TimeSeconds;
            double f = span > 1e-9d ? (time - a.TimeSeconds) / span : 0d;
            position = a.Position + ((b.Position - a.Position) * f);
            return true;
        }

        /// <summary>
        /// Re-translates one cached absolute path into the current frame, trimmed at
        /// "now". The live craft position is used as the first vertex when the path
        /// already started: interpolating between two cached vertices would put the
        /// start on the chord of a 60 s arc, i.e. a couple of kilometres inside the
        /// real curve — a visible gap between the line and the ship marker at zoom.
        /// </summary>
        private NavLine ReplayPath(CachedPath path, OrbitingBody frame, double time, Vector3d shipPoint)
        {
            List<NavShipSample> samples = path.Samples;
            var line = new NavLine
            {
                Kind = path.Kind,
                Dashed = path.Kind == NavLineKind.ManeuverForecast,
                StartTimeS = samples[0].TimeSeconds,
                EndTimeS = samples[samples.Count - 1].TimeSeconds,
            };

            bool started = false;
            if (samples[0].TimeSeconds <= time + 1e-6d && shipPoint.IsFinite)
            {
                line.Points.Add(shipPoint);
                line.Times.Add(time);
                line.StartTimeS = time;
                started = true;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                NavShipSample sample = samples[i];
                if (sample.TimeSeconds <= time + 1e-6d)
                {
                    continue;
                }

                if (TryToFrame(frame, sample.TimeSeconds, sample.Position, out Vector3d point))
                {
                    line.Points.Add(point);
                    line.Times.Add(sample.TimeSeconds);
                    started = true;
                }
            }

            return started && line.Points.Count >= 2 ? line : null;
        }

        /// <summary>
        /// Window match for the cache. The window is derived from the live osculating
        /// period, so it jitters by microseconds between rebuilds; requiring an exact
        /// match would throw the cache away for nothing.
        /// </summary>
        private static double WindowMatchSeconds(double window)
        {
            return Math.Max(1d, Math.Abs(window) * 1e-6d);
        }

        private static bool TryToFrame(OrbitingBody frame, double time, Vector3d world, out Vector3d point)
        {
            try
            {
                frame.EvaluateWorldState(time, out Vector3d framePosition, out _);
                point = world - framePosition;
                return point.IsFinite;
            }
            catch (EphemerisRangeException)
            {
                point = default;
                return false;
            }
        }

        /// <summary>
        /// Coast-only forecast. It is drawn solid and stays on the map after a
        /// maneuver is planned, so the player can compare the flown path with the
        /// plan. A suborbital conic is sampled densely over the actual flight and
        /// coarsely for the rest of the window.
        /// <paramref name="ignitions"/> makes the loop end its steps exactly on the
        /// planned burn times, so a burn point is a real vertex of the drawn line and
        /// not a point in the middle of a chord. <paramref name="recordMarkers"/> is
        /// set when the plan has no ΔV at all: then every planned ignition lies on
        /// this very coast line and the markers are recorded here.
        /// </summary>
        private void BuildBallisticPath(SpacecraftPhysics physics, StarSystem system, Ship probe,
            OrbitingBody frame, double time, double window, double impactSeconds,
            List<PlannedBurn> ignitions, bool recordMarkers,
            ref double reachedTime, ref bool anyLine, ref string impact)
        {
            double sampleWindow = window;
            if (impactSeconds > 1d && double.IsFinite(impactSeconds) && impactSeconds < window)
            {
                sampleWindow = impactSeconds * 1.05d;
            }

            int n = ForecastSamples;
            double dt = sampleWindow / (n - 1d);
            if (!(dt > 0d) || !double.IsFinite(dt))
            {
                return;
            }

            double endTime = time + window;
            double denseEnd = time + sampleWindow;
            double coarseDt = sampleWindow < window - 1e-6d
                ? (window - sampleWindow) / 48d
                : 0d;

            var line = NewForecastLine(NavLineKind.ShipForecast, time);
            line.Points.Add(probe.Position - FramePosition);
            line.Times.Add(time);
            double t = time;
            int ignition = 0;
            try
            {
                while (impact == null && t < endTime - 1e-9d)
                {
                    double step = Math.Min(NextForecastStep(t, denseEnd, dt, coarseDt), endTime - t);

                    if (ignitions != null)
                    {
                        while (ignition < ignitions.Count && ignitions[ignition].TimeSeconds <= t + 1e-9d)
                        {
                            ignition++;
                        }

                        if (ignition < ignitions.Count)
                        {
                            double ignite = ignitions[ignition].TimeSeconds;
                            if (ignite < t + step - 1e-9d)
                            {
                                step = ignite - t;
                            }
                        }
                    }

                    if (!AdvanceForecast(physics, system, probe, frame, line, ref t, step, out SurfaceContact hit))
                    {
                        impact = "ПРОГНОЗ ОСТАНОВЛЕН: НЕКОНЕЧНОЕ СОСТОЯНИЕ";
                        break;
                    }

                    if (ignitions != null && recordMarkers)
                    {
                        while (ignition < ignitions.Count
                            && Math.Abs(t - ignitions[ignition].TimeSeconds) <= 1e-6d)
                        {
                            PlannedBurn marker = ignitions[ignition];
                            ResolveBurnReference(system, probe, marker, t, out int markerBody);
                            AddBurnMarker(frame, probe, marker, t, markerBody);
                            ignition++;
                        }
                    }

                    if (hit.Valid)
                    {
                        impact = "УДАР О " + (hit.Body.Name ?? "?").ToUpperInvariant();
                        AddImpactMarker(system, frame, hit, false);
                    }
                }
            }
            catch (EphemerisRangeException)
            {
                impact = "ПРОГНОЗ ОСТАНОВЛЕН: КОНЕЦ ЭФЕМЕРИД";
            }
            catch (InvalidOperationException)
            {
                impact = "ПРОГНОЗ ОСТАНОВЛЕН: ИНТЕГРАТОР";
            }

            FinishForecastLine(line, ref reachedTime, ref anyLine);
        }

        /// <summary>
        /// Maneuver forecast, drawn dashed. The coast before the first ignition is
        /// not repeated: the solid ballistic line already shows it. The dashed line
        /// starts exactly at the first burn and follows every subsequent leg.
        /// </summary>
        private void BuildPlannedPath(SpacecraftPhysics physics, StarSystem system, Ship probe,
            OrbitingBody frame, double time, double window, List<PlannedBurn> burns, double ispS,
            ref double reachedTime, ref bool anyLine, ref string impact)
        {
            int n = ForecastSamples;
            double dt = window / (n - 1d);
            if (!(dt > 0d) || !double.IsFinite(dt))
            {
                return;
            }

            double endTime = time + window;
            NavLine line = NewForecastLine(NavLineKind.ManeuverForecast, time);
            double t = time;
            int burnIndex = 0;
            bool started = false;

            // State right after the last applied impulse: its osculating orbit IS
            // the maneuver orbit, and its apsides are the second Ap/Pe pair on the
            // map. Recorded per burn, so a multi-burn plan shows the final leg.
            double postBurnTime = double.NaN;
            Vector3d postBurnPosition = default;
            Vector3d postBurnVelocity = default;
            bool hasPostBurnState = false;
            try
            {
                while (impact == null && burnIndex < burns.Count)
                {
                    double ignite = burns[burnIndex].TimeSeconds;
                    if (ignite >= endTime - 1e-6d)
                    {
                        break;
                    }

                    // Coast exactly to ignition. Before the first successful burn
                    // these points are discarded: they duplicate the solid line.
                    while (t < ignite - 1e-9d && impact == null)
                    {
                        double step = Math.Min(dt, ignite - t);
                        if (!AdvanceForecast(physics, system, probe, frame, line, ref t, step, out SurfaceContact hit))
                        {
                            impact = "ПРОГНОЗ ОСТАНОВЛЕН: НЕКОНЕЧНОЕ СОСТОЯНИЕ";
                            break;
                        }

                        if (hit.Valid)
                        {
                            impact = "УДАР О " + (hit.Body.Name ?? "?").ToUpperInvariant();
                            AddImpactMarker(system, frame, hit, true);
                        }
                    }

                    if (impact != null)
                    {
                        break;
                    }

                    PlannedBurn burn = burns[burnIndex];
                    burnIndex++;
                    if (!ApplyBurn(system, probe, frame, burn, t, ispS))
                    {
                        continue;
                    }

                    if (started)
                    {
                        FinishForecastLine(line, ref reachedTime, ref anyLine);
                    }

                    line = NewForecastLine(NavLineKind.ManeuverForecast, t);
                    frame.EvaluateWorldState(t, out Vector3d burnFrame, out _);
                    line.Points.Add(probe.Position - burnFrame);
                    line.Times.Add(t);
                    started = true;
                    postBurnTime = t;
                    postBurnPosition = probe.Position;
                    postBurnVelocity = probe.Velocity;
                    hasPostBurnState = true;
                    lastPostBurnValid = true;
                    lastPostBurnTime = postBurnTime;
                    lastPostBurnPosition = postBurnPosition;
                    lastPostBurnVelocity = postBurnVelocity;
                }

                if (impact == null)
                {
                    // The guard matters: after a ground contact this loop used to keep
                    // integrating from a state that is already below the surface, for
                    // the whole remaining window. That is both the "the calculation goes
                    // on after the collision" lag (1/r² collapses the integrator) and
                    // the fan of dashed garbage drawn past the globe.
                    while (impact == null && t < endTime - 1e-9d)
                    {
                        double step = Math.Min(dt, endTime - t);
                        if (!AdvanceForecast(physics, system, probe, frame, line, ref t, step, out SurfaceContact hit))
                        {
                            impact = "ПРОГНОЗ ОСТАНОВЛЕН: НЕКОНЕЧНОЕ СОСТОЯНИЕ";
                            break;
                        }

                        if (hit.Valid)
                        {
                            impact = "УДАР О " + (hit.Body.Name ?? "?").ToUpperInvariant();
                            AddImpactMarker(system, frame, hit, true);
                        }
                    }
                }
            }
            catch (EphemerisRangeException)
            {
                impact = "ПРОГНОЗ ОСТАНОВЛЕН: КОНЕЦ ЭФЕМЕРИД";
            }
            catch (InvalidOperationException)
            {
                impact = "ПРОГНОЗ ОСТАНОВЛЕН: ИНТЕГРАТОР";
            }

            if (started)
            {
                FinishForecastLine(line, ref reachedTime, ref anyLine);
            }

            // Only when the dashed maneuver orbit really exists: a marker pair
            // without a drawn orbit would be a lie. The impact cases still keep
            // anyLine=true when the post-burn leg was sampled before the hit.
            if (hasPostBurnState && anyLine)
            {
                BuildPlannedApsides(system, frame, postBurnTime, postBurnPosition, postBurnVelocity);
            }
        }

        private struct PlannedBurn
        {
            public Maneuver Item;
            public double TimeSeconds;
            public int Number;
        }

        private static NavLine NewForecastLine(NavLineKind kind, double startTime)
        {
            return new NavLine
            {
                Kind = kind,
                Dashed = true,
                StartTimeS = startTime,
                EndTimeS = startTime,
            };
        }

        /// <summary>Dense stride up to the sample window, then the coarse fallback stride.</summary>
        private static double NextForecastStep(double t, double denseEnd, double dt, double coarseDt)
        {
            if (t < denseEnd - 1e-9d)
            {
                return Math.Min(dt, denseEnd - t);
            }

            return coarseDt > 0d ? coarseDt : dt;
        }

        private void FinishForecastLine(NavLine line, ref double reachedTime, ref bool anyLine)
        {
            if (line == null || line.Points.Count < 2)
            {
                return;
            }

            Lines.Add(line);
            anyLine = true;
            if (line.EndTimeS > reachedTime)
            {
                reachedTime = line.EndTimeS;
            }
        }

        /// <summary>
        /// Applies one planned impulse in the maneuver's frame axes: tangent along the
        /// frame-relative velocity, normal towards that body, binormal along r×v. Mass
        /// follows the rocket equation with the same Isp the burn-time card uses.
        /// Its ignition marker is recorded even when ΔV is still zero.
        /// </summary>
        private bool ApplyBurn(StarSystem system, Ship probe, OrbitingBody mapFrame,
            PlannedBurn burn, double t, double ispS)
        {
            if (!probe.Position.IsFinite || !probe.Velocity.IsFinite)
            {
                return false;
            }

            Maneuver maneuver = burn.Item;
            OrbitingBody reference = ResolveBurnReference(system, probe, burn, t, out int referenceIndex);
            if (reference == null)
            {
                return false;
            }

            // ΔV is the last thing the player sets on the card, so the marker must
            // exist before the impulse (and therefore before the dashed orbit).
            AddBurnMarker(mapFrame, probe, burn, t, referenceIndex);

            // The node state is written even for a zero ΔV: the planner and the
            // maneuver frame switch need the world impulse expressed in T/N/B, and
            // that is only defined together with the ship state at the burn.
            reference.EvaluateWorldState(t, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d r = probe.Position - bodyPosition;
            Vector3d v = probe.Velocity - bodyVelocity;
            maneuver.NodeValid = true;
            maneuver.NodeTimeUniverseS = t;
            maneuver.NodeWorldPos = probe.Position;
            maneuver.NodeWorldVel = probe.Velocity;
            maneuver.NodeRelPos = r;
            maneuver.NodeRelVel = v;

            double dv = maneuver.DvMagnitude;
            if (!(dv > 1e-9d))
            {
                return false;
            }

            Vector3d tangent = v.Normalized;
            if (!tangent.IsFinite || !(tangent.SqrMagnitude > 0.5d))
            {
                return false;
            }

            Vector3d binormal = Vector3d.Cross(r, v).Normalized;
            if (!binormal.IsFinite || !(binormal.SqrMagnitude > 0.5d))
            {
                // Radial flight has no orbital plane. ManeuverAxes (and therefore every planner/card T/N/B)
                // uses world Z as the fallback; the forecast must use the SAME normal, or an identical T/N/B
                // triple means a different world impulse in the plan and in the predicted line.
                binormal = new Vector3d(0d, 0d, 1d);
            }

            Vector3d normal = Vector3d.Cross(binormal, tangent).Normalized;
            if (!normal.IsFinite || !(normal.SqrMagnitude > 0.5d))
            {
                return false;
            }

            Vector3d impulse = (tangent * maneuver.Tangent)
                + (normal * maneuver.Normal)
                + (binormal * maneuver.Binormal);
            if (!impulse.IsFinite)
            {
                return false;
            }

            probe.Velocity += impulse;
            if (ispS > 0d)
            {
                probe.Mass *= Math.Exp(-dv / (ispS * ManeuverPlan.StandardGravity));
            }

            AppliedManeuverCount++;
            return true;
        }

        /// <summary>The body whose axes define the burn (the card's frame, else the dominant body).</summary>
        private static OrbitingBody ResolveBurnReference(StarSystem system, Ship probe,
            PlannedBurn burn, double t, out int index)
        {
            index = -1;
            int frameBody = burn.Item.FrameBody;
            if (frameBody >= 0 && frameBody < system.AllBodies.Count)
            {
                index = frameBody;
                return system.AllBodies[frameBody];
            }

            OrbitingBody body = system.FindDominantBody(probe.Position, t);
            if (body != null)
            {
                index = IndexOf(system, body);
            }

            return body;
        }

        /// <summary>Records one planned ignition in the map frame at its real time.</summary>
        private void AddBurnMarker(OrbitingBody mapFrame, Ship probe, PlannedBurn burn, double t, int referenceIndex)
        {
            if (!probe.Position.IsFinite || mapFrame == null)
            {
                return;
            }

            mapFrame.EvaluateWorldState(t, out Vector3d framePosition, out _);
            BurnMarkers.Add(new NavBurnMarker
            {
                Index = burn.Number,
                TimeSeconds = t,
                Position = probe.Position - framePosition,
                DvMs = burn.Item.DvMagnitude,
                FrameBodyIndex = referenceIndex,
            });
        }

        /// <summary>
        /// Records a ground contact in the map frame. Two legs that hit the same spot
        /// (a plan that barely changes the dive) are one marker, and the planned one
        /// wins: it is the path the player is editing.
        /// </summary>
        private void AddImpactMarker(StarSystem system, OrbitingBody mapFrame, SurfaceContact contact, bool planned)
        {
            if (!contact.Valid || contact.Body == null || mapFrame == null || !contact.Position.IsFinite)
            {
                return;
            }

            Vector3d position;
            double speed;
            try
            {
                mapFrame.EvaluateWorldState(contact.TimeSeconds, out Vector3d framePosition, out _);
                contact.Body.EvaluateWorldState(contact.TimeSeconds, out _, out Vector3d bodyVelocity);
                position = ClampToSurface(contact.Body, contact.Position, contact.TimeSeconds) - framePosition;
                speed = (contact.Velocity - bodyVelocity).Magnitude;
            }
            catch (EphemerisRangeException)
            {
                return;
            }

            for (int i = 0; i < Impacts.Count; i++)
            {
                NavImpactMarker existing = Impacts[i];
                if (existing.BodyIndex != IndexOf(system, contact.Body)
                    || Math.Abs(existing.TimeSeconds - contact.TimeSeconds) > 120d
                    || (existing.Position - position).Magnitude > 20_000d)
                {
                    continue;
                }

                if (planned && !existing.Planned)
                {
                    existing.Planned = true;
                    existing.TimeSeconds = contact.TimeSeconds;
                    existing.Position = position;
                    existing.SpeedMps = speed;
                }

                return;
            }

            Impacts.Add(new NavImpactMarker
            {
                TimeSeconds = contact.TimeSeconds,
                Position = position,
                SpeedMps = speed,
                BodyIndex = IndexOf(system, contact.Body),
                BodyName = contact.Body.Name ?? "?",
                Planned = planned,
            });
        }

        private string PlannedNote(bool planned, bool revolutionsLimited, double window)
        {
            string text;
            if (planned)
            {
                text = "БАЛЛИСТИКА + МАНЁВР · N-BODY";
            }
            else if (ForecastTruncated)
            {
                text = "БАЛЛИСТИКА · N-BODY, ПЕРВЫЕ " + FormatDays(window);
            }
            else
            {
                text = revolutionsLimited
                    ? "БАЛЛИСТИКА · N-BODY, 3 ВИТКА"
                    : "БАЛЛИСТИКА · N-BODY + ЭФЕМЕРИДЫ";
            }

            if (PlanConflict)
            {
                text = "ПЛАН: КОНФЛИКТ · " + text;
            }
            else if (PlanOutsideWindow)
            {
                text = "МАНЁВР ЗА ОКНОМ · " + text;
            }

            return text;
        }

        /// <summary>
        /// Margin added to a body radius when a surface contact is detected. The
        /// osculating conic ignores J2 and drag, so a step capped at the crossing can
        /// end a few metres above the mean radius; without some slack the next step
        /// would dive inside the body again. Kept small on purpose: one map pixel is
        /// kilometres, so this is invisible, while a craft genuinely hovering a hundred
        /// metres up must not read as a crash.
        /// </summary>
        private const double GroundContactMarginMeters = 50d;

        /// <summary>Below this the contact is "now": stepping a millisecond would only loop.</summary>
        private const double GroundContactMinStepSeconds = 1e-3d;

        /// <summary>First ground contact of a forecast leg, in absolute coordinates.</summary>
        private struct SurfaceContact
        {
            public bool Valid;
            public OrbitingBody Body;
            public Vector3d Position;
            public Vector3d Velocity;
            public double TimeSeconds;
        }

        /// <summary>
        /// One forecast step with an exact surface stop. Returns false on a non-finite
        /// state; <paramref name="contact"/> is valid when the step ends on the ground,
        /// in which case the last line point is the real surface crossing, not the
        /// first underground sample.
        ///
        /// The step is capped at the first ground crossing of the osculating conic,
        /// and that is not cosmetic: a step here is minutes long (a 3-day window over
        /// 160 samples is 27 min), so an uncapped step can carry the probe through a
        /// planet's interior and out the other side. The crossing is then never seen on
        /// a step boundary — the map keeps drawing a path through the globe — and the
        /// leg goes on integrating below the surface for the rest of the window. A dive
        /// that passes near the body centre is worse still: the unclamped 1/r² term
        /// throws the state around and collapses the integrator onto its 1 µs floor.
        /// </summary>
        private static bool AdvanceForecast(
            SpacecraftPhysics physics,
            StarSystem system,
            Ship probe,
            OrbitingBody frame,
            NavLine line,
            ref double t,
            double stepSeconds,
            out SurfaceContact contact)
        {
            contact = default;

            Vector3d fromPosition = probe.Position;
            Vector3d fromVelocity = probe.Velocity;
            double fromMass = probe.Mass;
            double fromTime = t;

            double toGround = SecondsToGround(system, probe, fromTime, out OrbitingBody ground);
            if (toGround <= GroundContactMinStepSeconds)
            {
                // The cap of the previous step landed on the contact sphere: finish the
                // line on the ground instead of stepping a fraction of a millisecond
                // (which would not advance the clock at all).
                Vector3d surface = ClampToSurface(ground, probe.Position, fromTime);
                frame.EvaluateWorldState(fromTime, out Vector3d frameAtContact, out _);
                if (line.Points.Count > 0)
                {
                    line.Points[line.Points.Count - 1] = surface - frameAtContact;
                    line.EndTimeS = fromTime;
                }

                contact = new SurfaceContact
                {
                    Valid = true,
                    Body = ground,
                    Position = surface,
                    Velocity = probe.Velocity,
                    TimeSeconds = fromTime,
                };
                return true;
            }

            if (toGround < stepSeconds)
            {
                stepSeconds = toGround;
            }

            physics.Step(probe, fromTime, stepSeconds);
            t = fromTime + stepSeconds;

            if (!probe.Position.IsFinite || !probe.Velocity.IsFinite)
            {
                return false;
            }

            frame.EvaluateWorldState(t, out Vector3d framePosition, out _);
            line.Points.Add(probe.Position - framePosition);
            line.Times.Add(t);
            line.EndTimeS = t;

            if (!TryFindImpact(system, probe.Position, t, out OrbitingBody hit))
            {
                return true;
            }

            Vector3d impactPosition = probe.Position;
            Vector3d impactVelocity = probe.Velocity;
            double impactTime = t;
            if (IsInsideBody(hit, probe.Position, t)
                && TryRefineImpact(physics, hit, fromPosition, fromVelocity, fromMass,
                    fromTime, stepSeconds, out Vector3d refinedPosition, out double refinedTime,
                    out Vector3d refinedVelocity))
            {
                impactPosition = refinedPosition;
                impactVelocity = refinedVelocity;
                impactTime = refinedTime;

                // A coarse step can be minutes long (an orbital decay), so the last
                // chord would still dive into the globe. Subdivide the final step so
                // the approach follows the real curve all the way to the surface.
                const int tailSamples = 8;
                var tail = new Vector3d[tailSamples - 1];
                var tailTimes = new double[tailSamples - 1];
                int tailCount = 0;
                double span = impactTime - fromTime;
                for (int j = 1; j < tailSamples && span > 0d; j++)
                {
                    double offset = span * j / tailSamples;
                    var sub = new Ship(fromPosition, fromVelocity, fromMass);
                    physics.Step(sub, fromTime, offset);
                    if (!sub.Position.IsFinite)
                    {
                        break;
                    }

                    frame.EvaluateWorldState(fromTime + offset, out Vector3d subFrame, out _);
                    tailTimes[tailCount] = fromTime + offset;
                    tail[tailCount++] = sub.Position - subFrame;
                }

                frame.EvaluateWorldState(impactTime, out framePosition, out _);
                impactPosition = ClampToSurface(hit, impactPosition, impactTime);
                line.Points[line.Points.Count - 1] = impactPosition - framePosition;
                if (line.Times.Count == line.Points.Count)
                {
                    line.Times[line.Times.Count - 1] = impactTime;
                }

                for (int i = 0; i < tailCount; i++)
                {
                    line.Points.Insert(line.Points.Count - 1, tail[i]);
                    if (line.Times.Count == line.Points.Count - 1)
                    {
                        line.Times.Insert(line.Times.Count - 1, tailTimes[i]);
                    }
                }

                line.EndTimeS = impactTime;
                t = impactTime;
            }
            else
            {
                // The refinement is a display nicety and must never abort the forecast;
                // put the endpoint on the surface instead of leaving it underground.
                impactPosition = ClampToSurface(hit, probe.Position, t);
                impactVelocity = probe.Velocity;
                impactTime = t;
                line.Points[line.Points.Count - 1] = impactPosition - framePosition;
            }

            contact = new SurfaceContact
            {
                Valid = true,
                Body = hit,
                Position = impactPosition,
                Velocity = impactVelocity,
                TimeSeconds = impactTime,
            };
            return true;
        }

        /// <summary>
        /// Seconds until the craft reaches the surface of the body it is falling
        /// towards, from its osculating conic (two-body, plus a contact margin).
        /// Positive infinity when the conic stays clear of the ground.
        /// </summary>
        private static double SecondsToGround(StarSystem system, Ship probe, double time, out OrbitingBody body)
        {
            body = system.FindDominantBody(probe.Position, time);
            if (body == null || !(body.Radius > 0d))
            {
                return double.PositiveInfinity;
            }

            body.EvaluateWorldState(time, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            double distance = (probe.Position - bodyPosition).Magnitude;
            if (distance <= body.Radius + GroundContactMarginMeters)
            {
                return 0d;
            }

            double mu = body.ResolveStandardGravitationalParameter();
            OrbitalElements elements = OrbitalElements.FromState(
                probe.Position - bodyPosition, probe.Velocity - bodyVelocity, mu);
            return EstimateSurfaceImpactSeconds(elements, body.Radius + GroundContactMarginMeters, mu);
        }

        /// <summary>Projects a contact point onto the mean surface, so the marker sits on the ground.</summary>
        private static Vector3d ClampToSurface(OrbitingBody body, Vector3d position, double time)
        {
            body.EvaluateWorldState(time, out Vector3d bodyPosition, out _);
            Vector3d offset = position - bodyPosition;
            double distance = offset.Magnitude;
            if (!(distance > 1e-6d))
            {
                return position;
            }

            return bodyPosition + (offset * (body.Radius / distance));
        }

        /// <summary>
        /// Bisects the last integration step for the first contact with
        /// <paramref name="hit"/>. The endpoint is known to be inside; without this a
        /// 135 s step can end tens of kilometres underground and the drawn line dives
        /// into the globe instead of ending on it.
        /// </summary>
        private static bool TryRefineImpact(
            SpacecraftPhysics physics,
            OrbitingBody hit,
            Vector3d fromPosition,
            Vector3d fromVelocity,
            double fromMass,
            double fromTime,
            double stepSeconds,
            out Vector3d impactPosition,
            out double impactTime,
            out Vector3d impactVelocity)
        {
            impactPosition = default;
            impactTime = fromTime;
            impactVelocity = fromVelocity;
            if (hit == null || !(stepSeconds > 1e-6d))
            {
                return false;
            }

            try
            {
                double lo = 0d;
                double hi = stepSeconds;
                for (int i = 0; i < 28; i++)
                {
                    double mid = 0.5d * (lo + hi);
                    var test = new Ship(fromPosition, fromVelocity, fromMass);
                    physics.Step(test, fromTime, mid);
                    if (IsInsideBody(hit, test.Position, fromTime + mid))
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                }

                var surface = new Ship(fromPosition, fromVelocity, fromMass);
                physics.Step(surface, fromTime, hi);
                impactTime = fromTime + hi;
                impactPosition = surface.Position;
                impactVelocity = surface.Velocity;
                return impactPosition.IsFinite;
            }
            catch (Exception)
            {
                // A display refinement must not abort the whole forecast; the
                // underground endpoint is still a valid last sample.
                return false;
            }
        }

        private static bool IsInsideBody(OrbitingBody body, Vector3d position, double time)
        {
            body.EvaluateWorldState(time, out Vector3d bodyPosition, out _);
            return (position - bodyPosition).Magnitude <= body.Radius;
        }

        /// <summary>
        /// Time until a ballistic arc reaches the body's surface, from the osculating
        /// conic (two-body). Positive infinity when the periapsis is above the ground,
        /// the conic is (nearly) parabolic, or the craft is already past the inbound
        /// crossing. A bound ellipse uses the mean anomaly of its DESCENDING crossing;
        /// a hyperbola the inbound one — a dive into the ground is usually hyperbolic,
        /// and it is the steepest case of all. Drag can only remove energy and make
        /// this earlier, never later.
        /// </summary>
        private static double EstimateSurfaceImpactSeconds(OrbitalElements elements, double bodyRadius, double mu)
        {
            if (!elements.IsValid || !(bodyRadius > 0d) || !(mu > 0d))
            {
                return double.PositiveInfinity;
            }

            double eccentricity = elements.Eccentricity;
            if (!(eccentricity > 1e-9d))
            {
                return double.PositiveInfinity;
            }

            double semiMajor = elements.SemiMajorAxis;
            if (!double.IsFinite(semiMajor) || !(Math.Abs(semiMajor) > 0d) || Math.Abs(eccentricity - 1d) < 1e-6d)
            {
                return double.PositiveInfinity;
            }

            // Semi-latus rectum and periapsis radius: a conic that never comes down
            // to the ground cannot hit it, and the answer is needed before any solve.
            double p = elements.SpecificAngularMomentum * elements.SpecificAngularMomentum / mu;
            if (!(p > 0d) || !(p / (1d + eccentricity) < bodyRadius))
            {
                return double.PositiveInfinity;
            }

            // Crossing of the |r| = bodyRadius sphere (cos ν from p = r(1 + e·cos ν)).
            double cosine = ((p / bodyRadius) - 1d) / eccentricity;
            if (!(cosine > -1d && cosine < 1d))
            {
                return double.PositiveInfinity;
            }

            double meanMotion;
            double meanHit;
            double meanNow;
            if (eccentricity < 1d)
            {
                meanMotion = Math.Sqrt(mu / (semiMajor * semiMajor * semiMajor));
                meanHit = MeanAnomalyFromTrueAnomaly((2d * Math.PI) - Math.Acos(cosine), eccentricity);
                meanNow = elements.MeanAnomalyDegrees * (Math.PI / 180d);
            }
            else
            {
                // Hyperbola: alpha = -a > 0; the ground is met on the inbound branch at
                // the true anomaly -acos(...), and being still outside the sphere means
                // the craft is before that point.
                double alpha = -semiMajor;
                meanMotion = Math.Sqrt(mu / (alpha * alpha * alpha));
                double hitAnomaly = -Math.Acos(cosine);
                double nowAnomaly = NormalizeToPi(elements.TrueAnomalyDegrees * (Math.PI / 180d));
                if (!(nowAnomaly < hitAnomaly))
                {
                    return double.PositiveInfinity;
                }

                meanHit = HyperbolicMeanAnomaly(hitAnomaly, eccentricity);
                meanNow = HyperbolicMeanAnomaly(nowAnomaly, eccentricity);
                if (!double.IsFinite(meanHit) || !double.IsFinite(meanNow))
                {
                    return double.PositiveInfinity;
                }
            }

            if (!(meanMotion > 0d) || !double.IsFinite(meanMotion))
            {
                return double.PositiveInfinity;
            }

            double delta = meanHit - meanNow;
            if (eccentricity < 1d)
            {
                double twoPi = 2d * Math.PI;
                delta %= twoPi;
                if (delta <= 0d)
                {
                    delta += twoPi;
                }
            }
            else if (!(delta > 0d))
            {
                return double.PositiveInfinity;
            }

            return delta / meanMotion;
        }

        /// <summary>M = e·sinh H − H of the hyperbolic anomaly derived from the true anomaly.</summary>
        private static double HyperbolicMeanAnomaly(double trueAnomaly, double eccentricity)
        {
            double x = Math.Sqrt((eccentricity - 1d) / (eccentricity + 1d)) * Math.Tan(0.5d * trueAnomaly);
            if (!double.IsFinite(x) || !(Math.Abs(x) < 1d))
            {
                return double.NaN;
            }

            double h = 0.5d * Math.Log((1d + x) / (1d - x));
            return (eccentricity * Math.Sinh(h)) - h;
        }

        /// <summary>Angle reduced to (-π, π], the branch a hyperbolic true anomaly lives on.</summary>
        private static double NormalizeToPi(double radians)
        {
            double twoPi = 2d * Math.PI;
            radians %= twoPi;
            if (radians > Math.PI)
            {
                radians -= twoPi;
            }
            else if (radians <= -Math.PI)
            {
                radians += twoPi;
            }

            return radians;
        }

        private static double MeanAnomalyFromTrueAnomaly(double trueAnomaly, double eccentricity)
        {
            double eccentricAnomaly = 2d * Math.Atan2(
                Math.Sqrt(1d - eccentricity) * Math.Sin(trueAnomaly * 0.5d),
                Math.Sqrt(1d + eccentricity) * Math.Cos(trueAnomaly * 0.5d));
            double meanAnomaly = eccentricAnomaly - (eccentricity * Math.Sin(eccentricAnomaly));
            double twoPi = 2d * Math.PI;
            meanAnomaly %= twoPi;
            if (meanAnomaly < 0d)
            {
                meanAnomaly += twoPi;
            }

            return meanAnomaly;
        }

        private static string FormatDays(double seconds)
        {
            if (seconds >= DaySeconds)
            {
                return (seconds / DaySeconds).ToString("F0") + " СУТ";
            }

            return (seconds / HourSeconds).ToString("F1") + " Ч";
        }

        private static bool TryFindImpact(StarSystem system, Vector3d position, double time, out OrbitingBody hit)
        {
            for (int i = 0; i < system.AllBodies.Count; i++)
            {
                OrbitingBody body = system.AllBodies[i];
                body.EvaluateWorldState(time, out Vector3d bodyPosition, out _);
                if ((position - bodyPosition).Magnitude <= body.Radius + GroundContactMarginMeters)
                {
                    hit = body;
                    return true;
                }
            }

            hit = null;
            return false;
        }

        private static int IndexOf(StarSystem system, OrbitingBody body)
        {
            if (body == null)
            {
                return -1;
            }

            for (int i = 0; i < system.AllBodies.Count; i++)
            {
                if (ReferenceEquals(system.AllBodies[i], body))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
