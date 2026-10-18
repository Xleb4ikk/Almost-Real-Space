using System.Collections.Generic;
using Galilego.Building;
using Galilego.Core;
using Galilego.Universe;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Tablet
{
    /// <summary>
    /// Bridge "Almost Real Space simulation ↔ tablet": a TabletLink subclass that the
    /// game attaches to the tablet object (ShipTablet.Link). Fill pulls
    /// telemetry from SimulationRunner; commands are executed by what already
    /// exists in the game:
    ///  • speed reference frame (surface/orbit) and reference body really
    ///    change the readouts (default body is the dominant one, from the SOI);
    ///  • SAS/RCS on/off goes to the flight model autopilot
    ///    (VesselFlightModel.Controls); directional SAS modes (prograde,
    ///    normal, etc.) are not implemented — noted honestly on screen;
    ///  • ship control: throttle (RawThrottle), engine master switch
    ///    (SetAllEngines) and stage (SpacecraftBuilder.StageJettison).
    ///
    /// ΔV is not recomputed: it takes the same panel that BuilderUI draws
    /// (DeltaVPanel.Live ?? Plan) and copies it via DvTabletAdapter.
    ///
    /// Navball: the game has no true ship orientation, so Attitude is the
    /// pilot's (camera) look direction in the local horizon frame
    /// (X=east, Y=up, Z=north), and markers are real velocity directions
    /// in the same frame.
    /// </summary>
    public sealed class GameTabletLink : TabletLink
    {
        [Tooltip("SimulationRunner сцены. Пусто — найдётся автоматически.")]
        public SimulationRunner Runner;

        [Tooltip("BuilderUI (источник панели ΔV). Пусто — найдётся автоматически.")]
        public BuilderUI BuilderUi;

        [Tooltip("SpacecraftBuilder (исполнитель команд ступени). Пусто — найдётся автоматически.")]
        public SpacecraftBuilder Builder;

        private static readonly double G0 = DvEnvironment.G0;

        // Command state (shown on screen).
        private bool sasEnabled;
        private SasMode sasMode = SasMode.Stability;
        private bool rcsEnabled;
        private SpeedFrame frame = SpeedFrame.Surface;
        private int referenceBodyIndex = -1;      // -1 — auto: dominant body

        private string[] bodyNames = { "—" };
        private int bodyCountSeen = -1;

        // Proper acceleration measured between Fill calls.
        private Vector3d lastVelocity;
        private double lastVelocityTime = double.NaN;
        private double properAcceleration;
        private bool hasProperAcceleration;

        public override void SetSasEnabled(bool on)
        {
            sasEnabled = on;
            Galilego.Flight.VesselFlightModel flight = Runner != null ? Runner.Flight : null;
            if (flight != null)
            {
                flight.Controls.SasOn = on;
                if (on)
                {
                    flight.HoldCurrentAttitude();
                }
            }
        }

        public override void SetSasMode(SasMode mode)
        {
            sasMode = mode;
        }

        public override void SetRcsEnabled(bool on)
        {
            rcsEnabled = on;
            Galilego.Flight.VesselFlightModel flight = Runner != null ? Runner.Flight : null;
            if (flight != null)
            {
                flight.Controls.RcsOn = on;
            }
        }

        public override void SetSpeedFrame(SpeedFrame f)
        {
            frame = f;
        }

        public override void SetReferenceBody(int index)
        {
            referenceBodyIndex = index;
        }

        public override void SetThrottle(double throttle01)
        {
            if (Runner != null)
            {
                Runner.RawThrottle = System.Math.Max(0d, System.Math.Min(1d, throttle01));
            }
        }

        public override void SetEnginesEnabled(bool on)
        {
            Galilego.Flight.VesselFlightModel flight = Runner != null ? Runner.Flight : null;
            if (flight != null)
            {
                flight.SetAllEngines(on);
            }
        }

        public override void RequestStage()
        {
            if (Builder == null)
            {
                Builder = FindAnyObjectByType<SpacecraftBuilder>();
            }

            if (Builder != null)
            {
                Builder.StageJettison();
            }
        }

        public override void Fill(TabletTelemetry t)
        {
            EnsureRefs();

            Ship ship = Runner != null ? Runner.Ship : null;
            if (ship == null || Runner.SystemState == null || bodyCountSeen != Runner.SystemState.AllBodies.Count)
            {
                EnsureBodies();
            }

            t.ReferenceBodies = bodyNames;
            t.Frame = frame;
            t.SasMode = sasMode;

            // Systems and controls: the live flight model owns the state (T/G keys
            // and tablet buttons act on the same Controls).
            Galilego.Flight.VesselFlightModel flight = Runner != null ? Runner.Flight : null;
            if (flight != null)
            {
                sasEnabled = flight.Controls.SasOn;
                rcsEnabled = flight.Controls.RcsOn;
            }

            t.SasEnabled = sasEnabled;
            t.RcsEnabled = rcsEnabled;

            t.Throttle01 = Runner != null ? Runner.RawThrottle : 0d;
            t.EngineAvailable = flight != null;
            t.EngineOn = false;
            if (flight != null)
            {
                for (int i = 0; i < flight.MainEngineCount; i++)
                {
                    Galilego.Flight.Thruster engine = flight.MainEngine(i);
                    if (engine != null && engine.Enabled)
                    {
                        t.EngineOn = true;
                        break;
                    }
                }
            }

            Galilego.Spacecraft.StagePropulsion propulsion = Runner != null ? Runner.Propulsion : null;
            int activeStage = propulsion != null ? propulsion.ActiveStageIndex : -1;
            t.StageIgnited = activeStage >= 0;
            t.CanStage = propulsion != null
                && (activeStage < 0 ? propulsion.StageCount > 0 : propulsion.FirstAttached + 1 < propulsion.StageCount);

            OrbitingBody body = ResolveBody(t);
            if (ship == null || body == null)
            {
                t.ReferenceBodyIndex = 0;
                return;
            }

            double time = Runner.TimeSeconds;
            body.EvaluateWorldState(time, out Vector3d bodyPos, out Vector3d bodyVel);
            Vector3d r = ship.Position - bodyPos;
            Vector3d vInertial = ship.Velocity - bodyVel;
            double radius = r.Magnitude;
            Vector3d up = radius > 1e-6d ? r / radius : new Vector3d(0d, 0d, 1d);

            // Local horizon basis: X=east, Y=up, Z=north.
            HorizonBasis(body, up, out Vector3d east, out Vector3d north);

            // Speed in the selected reference frame: surface — minus body
            // rotation, orbit — relative to the body center (inertial).
            Vector3d omega = body.SpinAxis.Normalized * body.SpinAngularSpeed;
            Vector3d vSurface = vInertial - Vector3d.Cross(omega, r);
            Vector3d v = frame == SpeedFrame.Surface ? vSurface : vInertial;

            t.SpeedMs = v.Magnitude;
            t.VerticalSpeedMs = Vector3d.Dot(v, up);

            // Gravity summed over the whole system, as in flight physics.
            Vector3d gravity = Runner.SystemState.EvaluateShipAcceleration(ship.Position, time);
            t.GravityG = gravity.Magnitude / G0;

            UpdateProperAcceleration(ship, gravity, time);
            t.AccelerationMs2 = hasProperAcceleration ? properAcceleration : 0d;
            t.GForce = hasProperAcceleration ? properAcceleration / G0 : 0d;

            // Altitudes.
            double sea = radius - body.Radius;
            double surface = sea;
            if (body.Terrain != null)
            {
                body.SurfaceLatLonAt(ship.Position, time, out double latDeg, out double lonDeg);
                surface -= body.Terrain.GetHeightMeters(
                    body, latDeg * (System.Math.PI / 180d), lonDeg * (System.Math.PI / 180d));
            }

            t.AltitudeSeaLevelM = sea;
            t.AltitudeSurfaceM = surface;
            t.PressurePa = PressureAt(body, sea);

            FillAttitude(t, east, up, north);
            FillMarkers(t, v, up, east, north);
            FillOrbit(t, body, r, vInertial);

            // Mass / thrust / TWR.
            t.CurrentMassKg = ship.Mass;
            double throttle = Runner.Warp != null ? Runner.Warp.CurrentControlSnapshot.Throttle : 0d;
            double thrust = 0d;
            if (Runner.Propulsion != null)
            {
                thrust = Runner.Propulsion.ThrustN * throttle;
            }
            else if (Runner.MainThrustNewtons > 0d)
            {
                thrust = Runner.MainThrustNewtons * throttle;
            }

            t.ThrustN = thrust;
            double gSurface = body.Radius > 0d
                ? body.ResolveStandardGravitationalParameter() / (body.Radius * body.Radius)
                : G0;
            t.Twr = ship.Mass > 0d ? thrust / (ship.Mass * gSurface) : 0d;

            FillStages(t, ship);
            AppendCommandNote(t);
        }

        private void EnsureRefs()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (BuilderUi == null)
            {
                BuilderUi = FindAnyObjectByType<BuilderUI>();
            }

            if (Builder == null)
            {
                Builder = FindAnyObjectByType<SpacecraftBuilder>();
            }
        }

        /// <summary>System body names for the reference-body button (cached).</summary>
        private void EnsureBodies()
        {
            var all = Runner != null && Runner.SystemState != null ? Runner.SystemState.AllBodies : null;
            if (all == null || all.Count == 0)
            {
                bodyNames = new[] { "—" };
                bodyCountSeen = -1;
                return;
            }

            var names = new string[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                names[i] = (all[i].Name ?? "?").ToUpperInvariant();
            }

            bodyNames = names;
            bodyCountSeen = all.Count;
        }

        /// <summary>
        /// Reference body: the player's choice or the dominant one (SOI). Writes the index to
        /// ReferenceBodyIndex — exactly what highlights the tablet button.
        /// </summary>
        private OrbitingBody ResolveBody(TabletTelemetry t)
        {
            var all = Runner != null && Runner.SystemState != null ? Runner.SystemState.AllBodies : null;
            if (all == null || all.Count == 0)
            {
                t.ReferenceBodies = new[] { "—" };
                return null;
            }

            int index = referenceBodyIndex;
            if (index < 0 || index >= all.Count)
            {
                index = 0;
                OrbitingBody dominant = Runner.DominantBody;
                for (int i = 0; i < all.Count; i++)
                {
                    if (ReferenceEquals(all[i], dominant))
                    {
                        index = i;
                    }
                }
            }

            t.ReferenceBodies = bodyNames;
            t.ReferenceBodyIndex = index;
            return all[index];
        }

        private static void HorizonBasis(OrbitingBody body, Vector3d up, out Vector3d east, out Vector3d north)
        {
            Vector3d axis = body.SpinAxis.Normalized;
            north = axis - (up * Vector3d.Dot(axis, up));
            if (north.SqrMagnitude < 1e-12d)
            {
                north = Vector3d.Cross(up, new Vector3d(1d, 0d, 0d));
                if (north.SqrMagnitude < 1e-12d)
                {
                    north = Vector3d.Cross(up, new Vector3d(0d, 1d, 0d));
                }
            }

            north = north.Normalized;
            east = Vector3d.Cross(up, north).Normalized;
        }

        /// <summary>Astro-frame vector into the tablet horizon frame (east, up, north).</summary>
        private static Vector3 ToHorizon(Vector3d d, Vector3d east, Vector3d up, Vector3d north)
        {
            return new Vector3(
                (float)Vector3d.Dot(d, east),
                (float)Vector3d.Dot(d, up),
                (float)Vector3d.Dot(d, north));
        }

        /// <summary>
        /// Navball orientation. The ship has no angular state of its own, so this is
        /// the pilot's look direction: velocity markers and the compass stay in one
        /// (horizon) frame and remain consistent.
        /// </summary>
        private void FillAttitude(TabletTelemetry t, Vector3d east, Vector3d up, Vector3d north)
        {
            t.MarkersValid = false;
            Camera cam = Camera.main;
            if (cam == null)
            {
                t.Attitude = Quaternion.identity;
                return;
            }

            Vector3d look = AstroFrame.ToAstro(cam.transform.forward);
            Vector3d camUp = AstroFrame.ToAstro(cam.transform.up);
            Vector3 f = ToHorizon(look.Normalized, east, up, north);
            Vector3 u = ToHorizon(camUp.Normalized, east, up, north);
            if (f.sqrMagnitude < 1e-6f)
            {
                t.Attitude = Quaternion.identity;
                return;
            }

            f.Normalize();
            u -= Vector3.Dot(u, f) * f;
            if (u.sqrMagnitude < 1e-6f)
            {
                u = Vector3.Cross(f, Vector3.right);
                if (u.sqrMagnitude < 1e-6f)
                {
                    u = Vector3.Cross(f, Vector3.up);
                }
            }

            t.Attitude = Quaternion.LookRotation(f, u.normalized);
            t.MarkersValid = true;
        }

        /// <summary>
        /// Navball markers: prograde/retrograde from the velocity vector of the selected
        /// reference frame, normal from the orbit moment, radial from the radius
        /// in the orbit plane. Degenerate cases (near-zero speed, radial
        /// flight) hide the markers instead of showing noise.
        /// </summary>
        private void FillMarkers(TabletTelemetry t, Vector3d v, Vector3d up, Vector3d east, Vector3d north)
        {
            bool ok = false;
            if (v.Magnitude > 0.5d)
            {
                Vector3d prograde = v.Normalized;
                Vector3d normal = Vector3d.Cross(up, prograde);
                if (normal.SqrMagnitude > 1e-10d)
                {
                    normal = normal.Normalized;
                    Vector3d radialOut = Vector3d.Cross(prograde, normal).Normalized;
                    t.ProgradeDir = ToHorizon(prograde, east, up, north);
                    t.NormalDir = ToHorizon(normal, east, up, north);
                    t.RadialOutDir = ToHorizon(radialOut, east, up, north);
                    ok = true;
                }
            }

            t.MarkersValid = t.MarkersValid && ok;
        }

        private static void FillOrbit(TabletTelemetry t, OrbitingBody body, Vector3d r, Vector3d vRel)
        {
            double mu = body.ResolveStandardGravitationalParameter();
            OrbitalElements el = OrbitalElements.FromState(r, vRel, mu);
            var orbit = new TabletOrbit();
            if (el.IsValid)
            {
                orbit.Valid = true;
                orbit.Eccentricity = el.Eccentricity;
                orbit.InclinationDeg = el.InclinationDegrees;
                bool bound = el.IsBound && el.Eccentricity < 1d;
                orbit.HasApoapsis = bound;
                orbit.PeAltitudeM = el.PeriapsisDistance - body.Radius;
                orbit.PeSpeedMs = VisViva(mu, el.PeriapsisDistance, el.SemiMajorAxis);
                if (bound)
                {
                    orbit.ApAltitudeM = el.ApoapsisDistance - body.Radius;
                    orbit.ApSpeedMs = VisViva(mu, el.ApoapsisDistance, el.SemiMajorAxis);
                    orbit.PeriodS = el.OrbitalPeriodSeconds;
                }
                else
                {
                    orbit.ApAltitudeM = double.NaN;
                    orbit.ApTimeS = double.NaN;
                    orbit.ApSpeedMs = double.NaN;
                }

                if (el.TryGetTimeToApsides(mu, out double toPeriapsis, out double toApoapsis))
                {
                    orbit.PeTimeS = toPeriapsis;
                    if (bound)
                    {
                        orbit.ApTimeS = toApoapsis;
                    }
                }
                else
                {
                    orbit.PeTimeS = double.NaN;
                }
            }

            t.Orbit = orbit;
        }

        /// <summary>Speed at a radius by "vis-viva": v = √(μ·(2/r − 1/a)).</summary>
        private static double VisViva(double mu, double distance, double semiMajorAxis)
        {
            if (!(distance > 0d) || semiMajorAxis == 0d)
            {
                return double.NaN;
            }

            double energyTerm = (2d / distance) - (1d / semiMajorAxis);
            return energyTerm > 0d ? System.Math.Sqrt(mu * energyTerm) : double.NaN;
        }

        /// <summary>
        /// Pressure: the game has no pressure model of its own — derived from its
        /// exponential atmosphere density (p ∝ ρ), scaled to Earth
        /// (101325 Pa at 1.225 kg/m³). Zero for vacuum bodies.
        /// </summary>
        private static double PressureAt(OrbitingBody body, double altitudeMeters)
        {
            AtmosphereProfile atmosphere = body.Atmosphere;
            if (atmosphere == null
                || !(atmosphere.ScaleHeightMeters > 0d)
                || !(atmosphere.SeaLevelDensityKgPerCubicMeter > 0d))
            {
                return 0d;
            }

            double h = System.Math.Max(0d, altitudeMeters);
            if (h >= atmosphere.TopAltitudeMeters)
            {
                return 0d;
            }

            double density = atmosphere.SeaLevelDensityKgPerCubicMeter
                * System.Math.Exp(-h / atmosphere.ScaleHeightMeters);
            return 101325d * (density / 1.225d);
        }

        /// <summary>
        /// Proper acceleration (g-load) is the numerical derivative of inertial
        /// velocity minus gravity: ≈ 0 in free fall, ≈ F/m under thrust,
        /// ≈ g standing on the surface. Measurement window ≤ 1 s of sim time: longer
        /// means warp, where the average is meaningless (keeps the previous value).
        /// </summary>
        private void UpdateProperAcceleration(Ship ship, Vector3d gravity, double time)
        {
            if (double.IsFinite(lastVelocityTime))
            {
                double dt = time - lastVelocityTime;
                if (dt > 1e-4d && dt <= 1d)
                {
                    Vector3d inertialAcceleration = (ship.Velocity - lastVelocity) / dt;
                    double measured = (inertialAcceleration - gravity).Magnitude;
                    properAcceleration = hasProperAcceleration
                        ? (properAcceleration * 0.6d) + (measured * 0.4d)
                        : measured;
                    hasProperAcceleration = true;
                }
            }

            lastVelocity = ship.Velocity;
            lastVelocityTime = time;
        }

        /// <summary>
        /// ΔV from the builder panel (panel.Live ?? panel.Plan), fields copied
        /// as is. Mass out of flight is the full assembly mass (in flight the
        /// adapter sets it from the model state).
        /// </summary>
        private void FillStages(TabletTelemetry t, Ship ship)
        {
            DvResult result = null;
            if (BuilderUi != null && BuilderUi.DeltaV != null)
            {
                result = BuilderUi.DeltaV.Live ?? BuilderUi.DeltaV.Plan;
            }

            DvTabletAdapter.Apply(result, t);
            if (result != null && !result.InFlight)
            {
                t.CurrentMassKg = result.TotalMassKg;
                t.ThrustN = 0d;
                t.Twr = 0d;
            }
        }

        /// <summary>
        /// SAS/RCS are executed by the flight model, but directional SAS modes (prograde,
        /// normal…) are absent from the autopilot — the selected mode must be
        /// explained on screen, otherwise the interface silently lies.
        /// </summary>
        private void AppendCommandNote(TabletTelemetry t)
        {
            if (sasMode == SasMode.Stability)
            {
                return;
            }

            var notes = new List<string>(t.Notes ?? new string[0]);
            notes.Add("Направленные режимы САС (по скорости, нормаль и т.д.) ещё не исполняются: автопилот держит текущую ориентацию");
            t.Notes = notes.ToArray();
        }
    }
}
