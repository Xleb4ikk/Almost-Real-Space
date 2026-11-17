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
        public const int BodyOrbitSamples = 160;
        public const int ForecastSamples = 160;
        public const int MaxHistoryPoints = 512;

        /// <summary>Reused buffer for one body's non-uniform orbit sample times.</summary>
        private readonly double[] orbitSampleTimes = new double[BodyOrbitSamples];

        /// <summary>Longest forecast actually integrated for the map, seconds (visual/CPU budget).</summary>
        public const double MaxForecastSeconds = 60d * 86400d;

        /// <summary>Longest history reconstructed from the ephemerides, seconds (same ≈100-year ceiling as the UI).</summary>
        public const double MaxHistorySeconds = 3.156e9d;

        private const double DaySeconds = 86400d;
        private const double HourSeconds = 3600d;

        public readonly List<NavLine> Lines = new List<NavLine>();
        public readonly List<NavBodyMarker> Bodies = new List<NavBodyMarker>();
        public readonly List<NavShipSample> History = new List<NavShipSample>();
        public readonly List<NavBurnMarker> BurnMarkers = new List<NavBurnMarker>();

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

            BuildBodyOrbits(system, timeSeconds, frameIndex, orbitVisible);
            BuildFrameOrbit(system, timeSeconds, frameIndex, orbitVisible);
            BuildBodyTrails(system, timeSeconds, frameIndex, centerIndex, orbitVisible,
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
        /// Apsis markers of the CURRENT osculating orbit (the same two-body elements the
        /// orbit panel shows). N-body perturbations move them from one frame to the next;
        /// the map is a snapshot, not a frozen prediction.
        /// </summary>
        private void BuildApsides(StarSystem system, Ship ship, double time, OrbitingBody frame)
        {
            HasApsides = false;
            HasApoapsis = false;
            if (ship == null)
            {
                return;
            }

            OrbitingBody dominant = system.FindDominantBody(ship.Position, time);
            if (dominant == null)
            {
                return;
            }

            dominant.EvaluateWorldState(time, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d r = ship.Position - bodyPosition;
            Vector3d v = ship.Velocity - bodyVelocity;
            OrbitalElements elements = OrbitalElements.FromState(r, v, dominant.ResolveStandardGravitationalParameter());
            if (!elements.IsValid || elements.Eccentricity <= 1e-9d)
            {
                return;
            }

            Vector3d eDirection = elements.EccentricityVector / elements.Eccentricity;
            Vector3d local = bodyPosition - FramePosition;
            PeriapsisPosition = local + (eDirection * elements.PeriapsisDistance);
            if (elements.IsBound && double.IsFinite(elements.ApoapsisDistance))
            {
                ApoapsisPosition = local - (eDirection * elements.ApoapsisDistance);
                HasApoapsis = true;
            }
            else
            {
                ApoapsisPosition = local;
            }

            HasApsides = true;
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

        private void BuildBodyOrbits(StarSystem system, double time, int frameIndex, bool[] orbitVisible)
        {
            for (int i = 0; i < system.AllBodies.Count; i++)
            {
                if (i == frameIndex)
                {
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

                double period = OrbitalPeriodSeconds(body);
                if (!double.IsFinite(period))
                {
                    continue;
                }

                double lo = body.Baked != null ? body.Baked.T0Seconds : double.NegativeInfinity;
                double hi = body.Baked != null ? body.Baked.EndSeconds : double.PositiveInfinity;
                double start = time - (period * 0.5d);
                double end = time + (period * 0.5d);

                var line = new NavLine
                {
                    Kind = NavLineKind.BodyOrbit,
                    BodyIndex = i,
                    ParentIndex = IndexOf(system, body.Parent),
                    StartTimeS = start,
                    EndTimeS = end,
                };

                int n = BodyOrbitSamples;
                SampleOrbitTimes(body, time, n, period, orbitSampleTimes);
                for (int k = 0; k < n; k++)
                {
                    double ts = orbitSampleTimes[k];
                    if (ts < lo)
                    {
                        ts = lo;
                    }

                    if (ts > hi)
                    {
                        ts = hi;
                    }

                    try
                    {
                        body.EvaluateWorldState(ts, out Vector3d p, out _);
                        OrbitingBody frame = system.AllBodies[frameIndex];
                        frame.EvaluateWorldState(ts, out Vector3d fp, out _);
                        line.Points.Add(p - fp);
                    }
                    catch (EphemerisRangeException)
                    {
                        break;
                    }
                }

                if (line.Points.Count > n / 2)
                {
                    // The marker is evaluated at exactly `time`, which falls between
                    // two samples. Insert it as a vertex so the body is visibly ON
                    // its orbit at planet zoom instead of a chord passing wide.
                    line.Points.Insert(n / 2, Bodies[i].Position);
                }

                if (line.Points.Count >= 2)
                {
                    Lines.Add(line);
                }
            }
        }

        /// <summary>
        /// Orbit of the reference body around its parent, translated so the body sits
        /// at the map origin at the current time. Without this a body-centred map has
        /// no orbit for the body itself — the "Земля не на своей орбите" view — and
        /// the orbit toggle in its row does nothing.
        /// </summary>
        private void BuildFrameOrbit(StarSystem system, double time, int frameIndex, bool[] orbitVisible)
        {
            if (frameIndex < 0 || frameIndex >= system.AllBodies.Count)
            {
                return;
            }

            if (orbitVisible != null && frameIndex < orbitVisible.Length && !orbitVisible[frameIndex])
            {
                return;
            }

            OrbitingBody body = system.AllBodies[frameIndex];
            OrbitingBody parent = body.Parent;
            if (parent == null)
            {
                return;
            }

            double period = OrbitalPeriodSeconds(body);
            if (!double.IsFinite(period))
            {
                return;
            }

            double start = time - (period * 0.5d);
            double end = time + (period * 0.5d);
            var line = new NavLine
            {
                Kind = NavLineKind.BodyOrbit,
                BodyIndex = frameIndex,
                ParentIndex = IndexOf(system, parent),
                StartTimeS = start,
                EndTimeS = end,
            };

            int n = BodyOrbitSamples;
            SampleOrbitTimes(body, time, n, period, orbitSampleTimes);
            for (int k = 0; k < n; k++)
            {
                double ts = orbitSampleTimes[k];
                try
                {
                    // Path around the parent, shifted so the CURRENT body position
                    // is the origin: the marker at the centre lies exactly on it.
                    body.EvaluateWorldState(ts, out Vector3d p, out _);
                    line.Points.Add(p - FramePosition);
                }
                catch (EphemerisRangeException)
                {
                    break;
                }
            }

            if (line.Points.Count > n / 2)
            {
                // The frame body is at the map origin now; insert it as a vertex so
                // its own orbit visibly passes through the body, not beside it.
                line.Points.Insert(n / 2, Vector3d.Zero);
            }

            if (line.Points.Count >= 2)
            {
                Lines.Add(line);
            }
        }

        /// <summary>
        /// Orbital period of a body around its parent (parent + subtree μ), or +∞ if
        /// the rail is not a drawable ellipse. One definition for the orbit line and
        /// the trail caps.
        /// </summary>
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
        /// One full revolution of an elliptical rail, sampled uniformly in TRUE
        /// anomaly. Uniform time clusters samples at apoapsis and leaves the fast,
        /// high-curvature periapsis as a few long chords; equal anomaly steps keep the
        /// drawn orbit smooth for any eccentricity. Falls back to uniform time for
        /// circular rails and hyperbolic rails.
        /// </summary>
        private static void SampleOrbitTimes(
            OrbitingBody body, double time, int count, double period, double[] times)
        {
            int n = Math.Max(2, count);
            double twoPi = 2d * Math.PI;
            double eccentricity = body.Eccentricity;
            if (!(period > 0d) || !double.IsFinite(period))
            {
                for (int k = 0; k < n; k++)
                {
                    times[k] = time;
                }

                return;
            }

            if (!(eccentricity > 1e-9d)
                || !double.IsFinite(body.MeanAnomalyAtEpochDegrees)
                || !double.IsFinite(body.EpochTimeSeconds))
            {
                double start = time - (period * 0.5d);
                for (int k = 0; k < n; k++)
                {
                    times[k] = start + (period * k / (n - 1d));
                }

                return;
            }

            double meanMotion = twoPi / period;
            double meanNow = KeplerMath.NormalizeAngle(
                KeplerMath.DegreesToRadians(body.MeanAnomalyAtEpochDegrees)
                + (meanMotion * (time - body.EpochTimeSeconds)));
            double eccentricNow = KeplerMath.SolveEccentricAnomaly(meanNow, eccentricity);
            double trueNow = 2d * Math.Atan2(
                Math.Sqrt(1d + eccentricity) * Math.Sin(0.5d * eccentricNow),
                Math.Sqrt(1d - eccentricity) * Math.Cos(0.5d * eccentricNow));

            for (int k = 0; k < n; k++)
            {
                double trueAnomaly = trueNow + (twoPi * ((k / (n - 1d)) - 0.5d));
                double mean = MeanAnomalyFromTrueAnomaly(
                    KeplerMath.NormalizeAngle(trueAnomaly), eccentricity);
                double delta = mean - meanNow;
                while (delta > Math.PI)
                {
                    delta -= twoPi;
                }

                while (delta < -Math.PI)
                {
                    delta += twoPi;
                }

                times[k] = time + (delta / meanMotion);
            }
        }

        /// <summary>
        /// History/forecast of the body the map is centred on. A body's path is a
        /// known ephemeris, so unlike the ship's live trail it can be sampled both
        /// into the past and into the future: the sliders select the span.
        /// </summary>
        private void BuildBodyTrails(StarSystem system, double time, int frameIndex, int centerIndex,
            bool[] orbitVisible, double historySeconds, double forecastSeconds)
        {
            // When the map is centred on the craft, the planet it is standing on is
            // the meaningful body trail (and the one the system view is fitted to).
            int trailIndex = centerIndex;
            if (trailIndex == ShipIndex && DominantBodyIndex >= 0)
            {
                trailIndex = DominantBodyIndex;
            }

            if (trailIndex < 0 || trailIndex >= system.AllBodies.Count || trailIndex == frameIndex)
            {
                return;
            }

            if (orbitVisible != null && trailIndex < orbitVisible.Length && !orbitVisible[trailIndex])
            {
                return;
            }

            OrbitingBody body = system.AllBodies[trailIndex];
            OrbitingBody frame = system.AllBodies[frameIndex];

            // A short-period moon over a multi-day slider would be a scribble of
            // dozens of overlapping revolutions sampled by 160 chords. The same
            // three-revolution budget as the craft forecast keeps the line readable
            // and dense enough to be a curve.
            double historyWindow = Math.Min(historySeconds, MaxHistorySeconds);
            // Body paths are ephemeris samples, not an N-body integration: the
            // 60-day ship-forecast budget does not apply here. The slider value is
            // honoured (up to the UI ceiling), so a 500-day orbit is drawn fully.
            double forecastWindow = Math.Min(forecastSeconds, MaxHistorySeconds);
            double period = OrbitalPeriodSeconds(body);
            if (double.IsFinite(period))
            {
                double threeOrbits = 3d * period;
                historyWindow = Math.Min(historyWindow, threeOrbits);
                forecastWindow = Math.Min(forecastWindow, threeOrbits);
            }

            if (historyWindow > 1d)
            {
                NavLine history = SampleBodyPath(body, frame, time - historyWindow, time);
                if (history != null)
                {
                    history.Kind = NavLineKind.BodyHistory;
                    history.BodyIndex = trailIndex;
                    history.ParentIndex = IndexOf(system, body.Parent);
                    Lines.Add(history);
                    HasBodyHistory = true;
                }
            }

            if (forecastWindow > 1d)
            {
                NavLine forecast = SampleBodyPath(body, frame, time, time + forecastWindow);
                if (forecast != null)
                {
                    forecast.Kind = NavLineKind.BodyForecast;
                    forecast.BodyIndex = trailIndex;
                    forecast.ParentIndex = IndexOf(system, body.Parent);
                    forecast.Dashed = true;
                    Lines.Add(forecast);
                    HasBodyForecast = true;
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

                    impactSeconds = EstimateSurfaceImpactSeconds(elements, dominant.Radius, mu);
                }
            }

            // Planned burns enter the prediction exactly where the countdown will
            // fire them. Conflict entries are never applied (same rule as the live
            // AdvanceTime), so the line changes at the point the plan stops being
            // executable.
            var burns = new List<PlannedBurn>();
            if (plan != null)
            {
                PlanConflict = plan.ConflictIndex >= 0;
                int valid = plan.ValidCount;
                for (int i = 0; i < valid; i++)
                {
                    Maneuver maneuver = plan[i];
                    if (maneuver == null || !(maneuver.DvMagnitude > 1e-9d))
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

            bool planned = burns.Count > 0 && plannedWindow > 1d;

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
            double ballisticReached = time;
            bool ballisticAny = false;
            string ballisticImpact = null;
            var ballisticProbe = new Ship(ship.Position, ship.Velocity, ship.Mass);
            BuildBallisticPath(physics, system, ballisticProbe, frame, time, window, impactSeconds,
                ref ballisticReached, ref ballisticAny, ref ballisticImpact);

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
        }

        /// <summary>
        /// Coast-only forecast. It is drawn solid and stays on the map after a
        /// maneuver is planned, so the player can compare the flown path with the
        /// plan. A suborbital conic is sampled densely over the actual flight and
        /// coarsely for the rest of the window.
        /// </summary>
        private void BuildBallisticPath(SpacecraftPhysics physics, StarSystem system, Ship probe,
            OrbitingBody frame, double time, double window, double impactSeconds,
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
            double t = time;
            try
            {
                while (impact == null && t < endTime - 1e-9d)
                {
                    double step = Math.Min(NextForecastStep(t, denseEnd, dt, coarseDt), endTime - t);
                    if (!AdvanceForecast(physics, system, probe, frame, line, ref t, step, out OrbitingBody hit))
                    {
                        impact = "ПРОГНОЗ ОСТАНОВЛЕН: НЕКОНЕЧНОЕ СОСТОЯНИЕ";
                        break;
                    }

                    if (hit != null)
                    {
                        impact = "УДАР О " + (hit.Name ?? "?").ToUpperInvariant();
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
                        if (!AdvanceForecast(physics, system, probe, frame, line, ref t, step, out OrbitingBody hit))
                        {
                            impact = "ПРОГНОЗ ОСТАНОВЛЕН: НЕКОНЕЧНОЕ СОСТОЯНИЕ";
                            break;
                        }

                        if (hit != null)
                        {
                            impact = "УДАР О " + (hit.Name ?? "?").ToUpperInvariant();
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
                    started = true;
                }

                if (impact == null)
                {
                    while (t < endTime - 1e-9d)
                    {
                        double step = Math.Min(dt, endTime - t);
                        if (!AdvanceForecast(physics, system, probe, frame, line, ref t, step, out OrbitingBody hit))
                        {
                            impact = "ПРОГНОЗ ОСТАНОВЛЕН: НЕКОНЕЧНОЕ СОСТОЯНИЕ";
                            break;
                        }

                        if (hit != null)
                        {
                            impact = "УДАР О " + (hit.Name ?? "?").ToUpperInvariant();
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
        /// </summary>
        private bool ApplyBurn(StarSystem system, Ship probe, OrbitingBody mapFrame,
            PlannedBurn burn, double t, double ispS)
        {
            Maneuver maneuver = burn.Item;
            double dv = maneuver.DvMagnitude;
            if (!(dv > 1e-9d) || !probe.Position.IsFinite || !probe.Velocity.IsFinite)
            {
                return false;
            }

            OrbitingBody reference = null;
            int referenceIndex = -1;
            if (maneuver.FrameBody >= 0 && maneuver.FrameBody < system.AllBodies.Count)
            {
                reference = system.AllBodies[maneuver.FrameBody];
                referenceIndex = maneuver.FrameBody;
            }
            else
            {
                reference = system.FindDominantBody(probe.Position, t);
                if (reference != null)
                {
                    referenceIndex = IndexOf(system, reference);
                }
            }

            if (reference == null)
            {
                return false;
            }

            reference.EvaluateWorldState(t, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d r = probe.Position - bodyPosition;
            Vector3d v = probe.Velocity - bodyVelocity;
            Vector3d tangent = v.Normalized;
            if (!tangent.IsFinite || !(tangent.SqrMagnitude > 0.5d))
            {
                return false;
            }

            Vector3d binormal = Vector3d.Cross(r, v).Normalized;
            if (!binormal.IsFinite || !(binormal.SqrMagnitude > 0.5d))
            {
                // Radial flight has no orbital plane: fall back to the map plane.
                binormal = OrbitNormal.IsFinite && OrbitNormal.SqrMagnitude > 0.5d
                    ? OrbitNormal.Normalized
                    : new Vector3d(0d, 0d, 1d);
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

            mapFrame.EvaluateWorldState(t, out Vector3d framePosition, out _);
            BurnMarkers.Add(new NavBurnMarker
            {
                Index = burn.Number,
                TimeSeconds = t,
                Position = probe.Position - framePosition,
                DvMs = dv,
                FrameBodyIndex = referenceIndex,
            });
            AppliedManeuverCount++;
            return true;
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
        /// One forecast step with an exact surface stop. Returns false on a non-finite
        /// state; <paramref name="hit"/> is non-null when the step ends on the ground,
        /// in which case the last line point is the real surface crossing, not the
        /// first underground sample.
        /// </summary>
        private static bool AdvanceForecast(
            SpacecraftPhysics physics,
            StarSystem system,
            Ship probe,
            OrbitingBody frame,
            NavLine line,
            ref double t,
            double stepSeconds,
            out OrbitingBody hit)
        {
            Vector3d fromPosition = probe.Position;
            Vector3d fromVelocity = probe.Velocity;
            double fromMass = probe.Mass;
            double fromTime = t;

            physics.Step(probe, fromTime, stepSeconds);
            t = fromTime + stepSeconds;

            if (!probe.Position.IsFinite || !probe.Velocity.IsFinite)
            {
                hit = null;
                return false;
            }

            frame.EvaluateWorldState(t, out Vector3d framePosition, out _);
            line.Points.Add(probe.Position - framePosition);
            line.EndTimeS = t;

            if (!TryFindImpact(system, probe.Position, t, out hit))
            {
                hit = null;
                return true;
            }

            if (TryRefineImpact(physics, hit, fromPosition, fromVelocity, fromMass,
                    fromTime, stepSeconds, out Vector3d impactPosition, out double impactTime))
            {
                // A coarse step can be minutes long (an orbital decay), so the last
                // chord would still dive into the globe. Subdivide the final step so
                // the approach follows the real curve all the way to the surface.
                const int tailSamples = 8;
                var tail = new Vector3d[tailSamples - 1];
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
                    tail[tailCount++] = sub.Position - subFrame;
                }

                frame.EvaluateWorldState(impactTime, out framePosition, out _);
                line.Points[line.Points.Count - 1] = impactPosition - framePosition;
                for (int i = 0; i < tailCount; i++)
                {
                    line.Points.Insert(line.Points.Count - 1, tail[i]);
                }

                line.EndTimeS = impactTime;
                t = impactTime;
            }

            return true;
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
            out double impactTime)
        {
            impactPosition = default;
            impactTime = fromTime;
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
        /// Time until a bound, ballistic arc reaches the body's surface, from the
        /// osculating conic (two-body). Positive infinity when the periapsis is above
        /// the ground or the orbit is not an ellipse. Drag can only remove energy and
        /// make this earlier, never later.
        /// </summary>
        private static double EstimateSurfaceImpactSeconds(OrbitalElements elements, double bodyRadius, double mu)
        {
            if (!elements.IsValid || !elements.IsBound || !(bodyRadius > 0d) || !(mu > 0d))
            {
                return double.PositiveInfinity;
            }

            double eccentricity = elements.Eccentricity;
            if (!(eccentricity > 1e-9d) || !(elements.SemiMajorAxis > 0d))
            {
                return double.PositiveInfinity;
            }

            double p = elements.SpecificAngularMomentum * elements.SpecificAngularMomentum / mu;
            if (!(p > 0d) || !(p / (1d + eccentricity) < bodyRadius))
            {
                return double.PositiveInfinity;
            }

            // Descending crossing of the r = bodyRadius sphere.
            double cosine = ((p / bodyRadius) - 1d) / eccentricity;
            if (!(cosine > -1d && cosine < 1d))
            {
                return double.PositiveInfinity;
            }

            double hitAnomaly = (2d * Math.PI) - Math.Acos(cosine);
            double meanNow = elements.MeanAnomalyDegrees * (Math.PI / 180d);
            double meanHit = MeanAnomalyFromTrueAnomaly(hitAnomaly, eccentricity);

            double delta = meanHit - meanNow;
            double twoPi = 2d * Math.PI;
            delta %= twoPi;
            if (delta <= 0d)
            {
                delta += twoPi;
            }

            double meanMotion = Math.Sqrt(mu / (elements.SemiMajorAxis * elements.SemiMajorAxis * elements.SemiMajorAxis));
            if (!(meanMotion > 0d) || !double.IsFinite(meanMotion))
            {
                return double.PositiveInfinity;
            }

            return delta / meanMotion;
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
                if ((position - bodyPosition).Magnitude <= body.Radius)
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
