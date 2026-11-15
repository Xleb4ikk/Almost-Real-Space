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
    /// same N-body gravity + drag sources as SimulationRunner, and the history is
    /// sampled from the actually simulated ship state. No Kepler preview is
    /// substituted for the game physics.
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

        /// <summary>Longest forecast actually integrated for the map, seconds (visual/CPU budget).</summary>
        public const double MaxForecastSeconds = 60d * 86400d;

        private const double DaySeconds = 86400d;
        private const double HourSeconds = 3600d;

        public readonly List<NavLine> Lines = new List<NavLine>();
        public readonly List<NavBodyMarker> Bodies = new List<NavBodyMarker>();
        public readonly List<NavShipSample> History = new List<NavShipSample>();

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
        public Vector3d OrbitNormal = new Vector3d(0d, 0d, 1d);
        public Vector3d OrbitRight = new Vector3d(1d, 0d, 0d);
        public bool HasApsides;
        public bool HasApoapsis;
        public Vector3d PeriapsisPosition;
        public Vector3d ApoapsisPosition;
        public string Note = string.Empty;
        public bool ForecastValid;

        /// <summary>True when the forecast line is the landed site's rotation path, not a ballistic coast.</summary>
        public bool SurfaceForecast;
        public bool ForecastTruncated;
        public double ForecastSeconds;
        public double ForecastWindowSeconds;
        public double HistorySeconds;

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
        /// real body positions and the real history trail.
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
            bool allowForecast)
        {
            Lines.Clear();
            Bodies.Clear();
            History.Clear();
            TimeSeconds = timeSeconds;
            FrameIndex = frameIndex;
            CenterIndex = centerIndex;
            HasShip = ship != null;
            ForecastValid = false;
            SurfaceForecast = false;
            ForecastTruncated = false;
            ForecastWindowSeconds = 0d;
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

            // Map plane: the ship's current orbital plane around its dominant body
            // (face-on view); on the ground — the frame body's equatorial plane.
            BuildPlane(system, ship, timeSeconds, frame, out OrbitNormal, out OrbitRight);
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

            bool shipLinesVisible = orbitVisible == null
                || ShipIndex >= orbitVisible.Length
                || orbitVisible[ShipIndex];

            if (shipLinesVisible && historySeconds > 0d && history != null && HasShip)
            {
                BuildHistory(system, frame, timeSeconds, historySeconds, history);
            }

            bool surfaceForecast = false;
            if (shipLinesVisible && allowForecast && HasShip && ship.Mass > 0d)
            {
                BuildForecast(system, ship, timeSeconds, frame, forecastSeconds);
            }
            else if (shipLinesVisible && HasShip && HasSurfaceFrame
                && ShipHeightAboveTerrain < 50_000d && forecastSeconds > 1d)
            {
                // A supported craft cannot coast, but its landing site still moves
                // with the planet. That rotation path is the honest forecast here.
                BuildSurfaceForecast(timeSeconds, frame, forecastSeconds);
                surfaceForecast = ForecastValid;
            }

            if (!allowForecast && !surfaceForecast)
            {
                Note = "ПРОГНОЗ ВЫКЛЮЧЕН: НЕТ СВОБОДНОГО ПОЛЁТА (КОНТАКТ/ПОВЕРХНОСТЬ/СТРОЙКА)";
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

        private static void BuildPlane(StarSystem system, Ship ship, double time, OrbitingBody frame,
            out Vector3d normal, out Vector3d right)
        {
            normal = frame.SpinAxis.Normalized;
            right = Vector3d.Zero;

            if (ship != null)
            {
                OrbitingBody dominant = system.FindDominantBody(ship.Position, time);
                if (dominant != null)
                {
                    dominant.EvaluateWorldState(time, out Vector3d bodyPosition, out Vector3d bodyVelocity);
                    Vector3d r = ship.Position - bodyPosition;
                    Vector3d v = ship.Velocity - bodyVelocity;
                    Vector3d h = Vector3d.Cross(r, v);
                    if (h.SqrMagnitude > 1e-12d * Math.Max(1d, r.SqrMagnitude * v.SqrMagnitude))
                    {
                        normal = h.Normalized;
                        Vector3d projected = r - (normal * Vector3d.Dot(r, normal));
                        if (projected.SqrMagnitude > 1e-12d)
                        {
                            right = projected.Normalized;
                        }
                    }
                    else if (r.SqrMagnitude > 1e-12d && r.Magnitude > dominant.Radius)
                    {
                        Vector3d radial = r.Normalized;
                        right = Vector3d.Cross(normal, radial);
                    }
                }
            }

            if (right.SqrMagnitude < 0.5d)
            {
                Vector3d reference = frame.SpinAxis.Normalized;
                right = Vector3d.Cross(normal, reference);
                if (right.SqrMagnitude < 1e-12d)
                {
                    right = Vector3d.Cross(normal, new Vector3d(1d, 0d, 0d));
                }

                if (right.SqrMagnitude < 1e-12d)
                {
                    right = new Vector3d(1d, 0d, 0d);
                }

                right = right.Normalized;
            }

            normal = normal.Normalized;
            // Complete the right-handed (right, up, normal) triple.
            right = (right - (normal * Vector3d.Dot(right, normal))).Normalized;
        }

        /// <summary>
        /// Forecast for a craft standing on the surface: it cannot coast, but the
        /// landing site travels with the planet's rotation. Draw that path instead
        /// of pretending the craft is in free fall. Capped at one rotation — after
        /// that the same circle repeats.
        /// </summary>
        private void BuildSurfaceForecast(double time, OrbitingBody frame, double forecastSeconds)
        {
            OrbitingBody body = DominantBody;
            if (body == null || !HasSurfaceFrame || !(forecastSeconds > 1d))
            {
                return;
            }

            double window = forecastSeconds;
            if (body.RotationPeriodSeconds > 0d)
            {
                window = Math.Min(window, body.RotationPeriodSeconds);
            }
            else
            {
                window = Math.Min(window, 86400d);
            }

            if (!(window > 1d))
            {
                return;
            }

            int n = ForecastSamples;
            var line = new NavLine
            {
                Kind = NavLineKind.ShipForecast,
                Dashed = true,
                StartTimeS = time,
                EndTimeS = time + window,
            };

            double altitude = Math.Max(0d, ShipAltitudeMeters);
            for (int k = 0; k < n; k++)
            {
                double t = time + (window * k / (n - 1d));
                try
                {
                    body.GetSurfaceState(ShipLatitudeDegrees, ShipLongitudeDegrees, altitude,
                        t, out Vector3d point, out _);
                    frame.EvaluateWorldState(t, out Vector3d framePoint, out _);
                    line.Points.Add(point - framePoint);
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
            SurfaceForecast = true;
            ForecastWindowSeconds = window;
            ForecastTruncated = window < forecastSeconds - 1e-6d;
            Note = ForecastTruncated
                ? "ПРОГНОЗ ВРАЩЕНИЯ ПЛОЩАДКИ · 1 ОБОРОТ"
                : "ПРОГНОЗ ВРАЩЕНИЯ ПЛОЩАДКИ";
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

                double mu = body.Parent.ResolveStandardGravitationalParameter()
                    + body.ResolveSubtreeStandardGravitationalParameter();
                double a = body.SemiMajorAxis;
                if (!(mu > 0d) || !(a > 0d) || !(body.Eccentricity >= 0d) || !(body.Eccentricity < 1d))
                {
                    continue;
                }

                double period = 2d * Math.PI * Math.Sqrt((a * a * a) / mu);
                if (!double.IsFinite(period) || period <= 0d)
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
                for (int k = 0; k < n; k++)
                {
                    double ts = start + ((end - start) * k / (n - 1d));
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

            double mu = parent.ResolveStandardGravitationalParameter()
                + body.ResolveSubtreeStandardGravitationalParameter();
            double a = body.SemiMajorAxis;
            if (!(mu > 0d) || !(a > 0d) || !(body.Eccentricity >= 0d) || !(body.Eccentricity < 1d))
            {
                return;
            }

            double period = 2d * Math.PI * Math.Sqrt((a * a * a) / mu);
            if (!double.IsFinite(period) || period <= 0d)
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
            for (int k = 0; k < n; k++)
            {
                double ts = start + ((end - start) * k / (n - 1d));
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

        private void BuildForecast(StarSystem system, Ship ship, double time, OrbitingBody frame, double forecastSeconds)
        {
            if (!(forecastSeconds > 1d) || !(ship.Mass > 0d))
            {
                return;
            }

            double window = Math.Min(forecastSeconds, MaxForecastSeconds);
            ForecastTruncated = window < forecastSeconds - 1e-6d;
            bool revolutionsLimited = false;

            // A map with hundreds of overlapping revolutions is noise: show at most
            // three revolutions of the current osculating orbit, but never less than
            // six hours of flight.
            OrbitingBody dominant = system.FindDominantBody(ship.Position, time);
            if (dominant != null)
            {
                dominant.EvaluateWorldState(time, out Vector3d bodyPosition, out Vector3d bodyVelocity);
                double mu = dominant.ResolveStandardGravitationalParameter();
                Vector3d r = ship.Position - bodyPosition;
                Vector3d v = ship.Velocity - bodyVelocity;
                OrbitalElements elements = OrbitalElements.FromState(r, v, mu);
                if (elements.IsValid && elements.IsBound && double.IsFinite(elements.OrbitalPeriodSeconds))
                {
                    double threeOrbits = 3d * elements.OrbitalPeriodSeconds;
                    if (threeOrbits > 1d && threeOrbits < window)
                    {
                        window = Math.Min(window, Math.Max(threeOrbits, 6d * HourSeconds));
                        revolutionsLimited = true;
                    }
                }
            }

            int n = ForecastSamples;
            double dt = window / (n - 1d);
            if (!(dt > 0d) || !double.IsFinite(dt))
            {
                return;
            }

            ForecastWindowSeconds = window;

            // The same force sources as SimulationRunner: N-body gravity (baked
            // ephemerides + the indirect star term + J2) and drag in atmospheres.
            // Thrust is intentionally absent: the line is a coast forecast; the
            // engine state is shown separately on the tablet.
            var physics = new SpacecraftPhysics
            {
                ToleranceFrameSystem = system,
                MaxStepSize = Math.Min(Math.Max(OrbitIntegrator.DefaultMaxStepSize, dt), 6d * HourSeconds),
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

            var probe = new Ship(ship.Position, ship.Velocity, ship.Mass);
            var line = new NavLine
            {
                Kind = NavLineKind.ShipForecast,
                Dashed = true,
                StartTimeS = time,
                EndTimeS = time + window,
            };

            line.Points.Add(ship.Position - FramePosition);
            string impact = null;
            double t = time;
            try
            {
                for (int k = 1; k < n; k++)
                {
                    physics.Step(probe, t, dt);
                    t += dt;

                    if (!probe.Position.IsFinite || !probe.Velocity.IsFinite)
                    {
                        impact = "ПРОГНОЗ ОСТАНОВЛЕН: НЕКОНЕЧНОЕ СОСТОЯНИЕ";
                        break;
                    }

                    frame.EvaluateWorldState(t, out Vector3d fp, out _);
                    line.Points.Add(probe.Position - fp);
                    line.EndTimeS = t;

                    if (TryFindImpact(system, probe.Position, t, out OrbitingBody hit))
                    {
                        impact = "УДАР О " + (hit.Name ?? "?").ToUpperInvariant();
                        break;
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

            if (line.Points.Count >= 2)
            {
                Lines.Add(line);
                ForecastValid = true;
                Note = impact ?? (ForecastTruncated
                    ? "БАЛЛИСТИКА · N-BODY, ПЕРВЫЕ " + FormatDays(window)
                    : (revolutionsLimited
                        ? "БАЛЛИСТИКА · N-BODY, 3 ВИТКА"
                        : "БАЛЛИСТИКА · N-BODY + ЭФЕМЕРИДЫ"));
            }
            else if (!string.IsNullOrEmpty(impact))
            {
                Note = impact;
            }
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
