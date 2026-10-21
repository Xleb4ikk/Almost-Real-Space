using System.Collections.Generic;
using Galilego.Building;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
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
    /// Navball: Attitude is the CREW CAPSULE's real orientation (its own PhysX
    /// body inside the contact zone, the capsule visual in build mode,
    /// Ship.Attitude × the capsule part frame in free flight), falling back to
    /// the whole craft if the assembly has no capsule. The chart frame is
    /// right-handed: X = up × north (chart right, west), Y = up, Z = north.
    /// The navball centre is the capsule's nose (part +Y). Markers are the
    /// capsule's own velocity directions in that same frame (its PhysX body in
    /// the contact zone, v_com + ω × r in free flight); all six stay available
    /// (prograde falls back to the body-relative velocity when the selected
    /// frame is at rest, e.g. sitting on the launch pad).
    /// </summary>
    public sealed class GameTabletLink : TabletLink
    {
        [Tooltip("SimulationRunner сцены. Пусто — найдётся автоматически.")]
        public SimulationRunner Runner;

        [Tooltip("BuilderUI (источник панели ΔV). Пусто — найдётся автоматически.")]
        public BuilderUI BuilderUi;

        [Tooltip("SpacecraftBuilder (исполнитель команд ступени). Пусто — найдётся автоматически.")]
        public SpacecraftBuilder Builder;

        [Tooltip("ContactZoneHost сцены. Пусто — найдётся автоматически.")]
        public ContactZoneHost Zone;

        private static readonly double G0 = DvEnvironment.G0;

        // Command state (shown on screen).
        private bool sasEnabled;
        private SasMode sasMode = SasMode.Stability;
        private bool rcsEnabled;
        private SpeedFrame frame = SpeedFrame.Surface;
        private int referenceBodyIndex = -1;      // -1 — auto: dominant body

        private string[] bodyNames = { "—" };
        private int bodyCountSeen = -1;

        /// <summary>Crew capsule door (the placed capsule's model), found lazily.</summary>
        private OrionCrewModule hatch;

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

        public override void SetHatchOpen(bool open)
        {
            OrionCrewModule module = FindHatch();
            if (module != null)
            {
                module.SetHatchOpen(open);
            }
        }

        /// <summary>
        /// The placed capsule's OrionCrewModule. The build-mode preview also clones
        /// the model (with its animator), so the draft is the source of truth; the
        /// scene-wide search is only for a flight scene without the builder.
        /// </summary>
        private OrionCrewModule FindHatch()
        {
            if (hatch != null)
            {
                return hatch;
            }

            if (Builder == null)
            {
                Builder = FindAnyObjectByType<SpacecraftBuilder>();
            }

            if (Builder != null)
            {
                IReadOnlyList<PlacedPart> parts = Builder.Parts;
                for (int i = 0; i < parts.Count; i++)
                {
                    PlacedPart part = parts[i];
                    if (part.Kind != PartKind.Capsule || part.Visual == null || part.Visual.Root == null)
                    {
                        continue;
                    }

                    OrionCrewModule found = part.Visual.Root.GetComponentInChildren<OrionCrewModule>(true);
                    if (found != null)
                    {
                        hatch = found;
                        return hatch;
                    }
                }
            }

            if (Runner == null || !Runner.BuildMode)
            {
                hatch = FindAnyObjectByType<OrionCrewModule>();
            }

            return hatch;
        }

        public override void Fill(TabletTelemetry t)
        {
            EnsureRefs();

            // A lone capsule has no vessel controls or propulsion, but its own
            // telemetry (motion, gravity, altitude, orbit) is still known.
            bool hasCapsule;
            bool controlsAvailable = CapsuleControlsAvailable(out hasCapsule, out double capsuleMass);
            t.ControlsAvailable = controlsAvailable;

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

            // The hatch belongs to the capsule, not to the vessel: its button stays
            // usable even when the capsule is not connected to anything.
            OrionCrewModule module = FindHatch();
            t.HatchAvailable = module != null && module.HatchAvailable;
            t.HatchOpen = module != null && module.HatchOpen;

            OrbitingBody body = ResolveBody(t);
            if (ship == null || body == null)
            {
                t.ReferenceBodyIndex = 0;
                return;
            }

            double time = Runner.TimeSeconds;

            // Everything kinematic is the capsule's: its own body in the contact zone,
            // its visual pose on the pad in build mode, v_com + ω × r in free flight.
            // The rocket COM must not drive the readouts.
            Vector3d statePosition = ship.Position;
            Vector3d stateVelocity = ship.Velocity;
            if (TryCapsuleBody(out Rigidbody capsule))
            {
                if (TryCapsuleState(capsule, time, out Vector3d capsulePosition, out Vector3d capsuleVelocity))
                {
                    statePosition = capsulePosition;
                    stateVelocity = capsuleVelocity;
                }
            }
            else if (TryCapsuleKinematics(ship, out Vector3d capsulePosition, out Vector3d capsuleVelocity))
            {
                statePosition = capsulePosition;
                stateVelocity = capsuleVelocity;
            }

            body.EvaluateWorldState(time, out Vector3d bodyPos, out Vector3d bodyVel);
            Vector3d r = statePosition - bodyPos;
            Vector3d vInertial = stateVelocity - bodyVel;
            double radius = r.Magnitude;
            Vector3d up = radius > 1e-6d ? r / radius : new Vector3d(0d, 0d, 1d);

            // Local horizon basis of the selected body. The chart is right-handed:
            // right = up × north (points west), up, north. This is what the navball
            // projection needs; a heading is read toward −X, see NavballRenderer.Euler.
            HorizonBasis(body, up, out Vector3d chartRight, out Vector3d north);

            // Speed in the selected reference frame: surface — minus body
            // rotation, orbit — relative to the body center (inertial).
            Vector3d omega = body.SpinAxis.Normalized * body.SpinAngularSpeed;
            Vector3d vSurface = vInertial - Vector3d.Cross(omega, r);
            Vector3d v = frame == SpeedFrame.Surface ? vSurface : vInertial;

            t.SpeedMs = v.Magnitude;
            t.VerticalSpeedMs = Vector3d.Dot(v, up);

            // Gravity summed over the whole system, as in flight physics.
            Vector3d gravity = Runner.SystemState.EvaluateShipAcceleration(statePosition, time);
            t.GravityG = gravity.Magnitude / G0;

            UpdateProperAcceleration(stateVelocity, gravity, time);
            t.AccelerationMs2 = hasProperAcceleration ? properAcceleration : 0d;
            t.GForce = hasProperAcceleration ? properAcceleration / G0 : 0d;

            // Altitudes.
            double sea = radius - body.Radius;
            double surface = sea;
            if (body.Terrain != null)
            {
                body.SurfaceLatLonAt(statePosition, time, out double latDeg, out double lonDeg);
                surface -= body.Terrain.GetHeightMeters(
                    body, latDeg * (System.Math.PI / 180d), lonDeg * (System.Math.PI / 180d));
            }

            t.AltitudeSeaLevelM = sea;
            t.AltitudeSurfaceM = surface;
            t.PressurePa = PressureAt(body, sea);

            FillAttitude(t, ship, chartRight, up, north);
            FillMarkers(t, v, vInertial, r, up, chartRight, north);
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

            if (hasCapsule && !controlsAvailable)
            {
                ZeroUnconnected(t, capsuleMass);
            }
        }

        /// <summary>
        /// The crew capsule is attached to at least one other part (parent or child).
        /// hasCapsule=false — the assembly has no capsule, so the gate does not apply.
        /// capsuleMass — the capsule's own mass (valid when hasCapsule).
        /// </summary>
        private bool CapsuleControlsAvailable(out bool hasCapsule, out double capsuleMass)
        {
            hasCapsule = false;
            capsuleMass = 0d;

            if (Runner != null && Runner.BuildMode && Builder != null)
            {
                IReadOnlyList<PlacedPart> draft = Builder.Parts;
                int capsule = -1;
                for (int i = 0; i < draft.Count; i++)
                {
                    if (draft[i].Kind == PartKind.Capsule)
                    {
                        capsule = i;
                        break;
                    }
                }

                if (capsule < 0)
                {
                    return true;
                }

                hasCapsule = true;
                capsuleMass = draft[capsule].Metrics.MassKg;
                if (draft[capsule].ParentIndex >= 0)
                {
                    return true;
                }

                for (int i = 0; i < draft.Count; i++)
                {
                    if (i != capsule && draft[i].ParentIndex == capsule)
                    {
                        return true;
                    }
                }

                return false;
            }

            int partIndex = FindCapsulePart(out PartDefinition def);
            if (def == null)
            {
                return true;
            }

            hasCapsule = true;
            capsuleMass = def.MassKg;
            if (def.ParentIndex >= 0)
            {
                return true;
            }

            PartDefinition[] defs = Runner != null && Runner.Flight != null
                ? Runner.Flight.Defs
                : Runner != null ? Runner.Parts : null;
            if (defs != null)
            {
                for (int i = 0; i < defs.Length; i++)
                {
                    if (i != partIndex && defs[i] != null && defs[i].ParentIndex == partIndex)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Lone capsule: motion, gravity, altitude, orbit and its own mass stay (they
        /// are the capsule's); engines, fuel, ΔV and controls need a vessel.
        /// </summary>
        private static void ZeroUnconnected(TabletTelemetry t, double capsuleMass)
        {
            t.SasEnabled = false;
            t.SasMode = SasMode.Stability;
            t.RcsEnabled = false;

            t.Throttle01 = 0d;
            t.EngineOn = false;
            t.EngineAvailable = false;
            t.CanStage = false;
            t.StageIgnited = false;

            t.CurrentMassKg = capsuleMass;
            t.ThrustN = 0d;
            t.Twr = 0d;
            t.TotalDeltaVMs = 0d;
            t.PlanDeltaVMs = 0d;
            t.TotalBurnTimeS = 0d;
            t.Stages = new TabletStage[0];
            t.Notes = new[] { "Капсула не подключена к аппарату — двигатели, топливо и управление недоступны" };
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

            if (Zone == null)
            {
                Zone = FindAnyObjectByType<ContactZoneHost>();
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

        private static void HorizonBasis(OrbitingBody body, Vector3d up, out Vector3d chartRight, out Vector3d north)
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
            // Right-handed chart frame (chartRight, up, north): the navball renders
            // with X to the screen right, so this axis must be up × north, i.e. WEST
            // on a prograde body. With true east the triple would be left-handed and
            // the quaternion projection would mirror the ball.
            chartRight = Vector3d.Cross(up, north).Normalized;
        }

        /// <summary>Astro-frame vector into the tablet horizon chart frame (right, up, north).</summary>
        private static Vector3 ToHorizon(Vector3d d, Vector3d chartRight, Vector3d up, Vector3d north)
        {
            return new Vector3(
                (float)Vector3d.Dot(d, chartRight),
                (float)Vector3d.Dot(d, up),
                (float)Vector3d.Dot(d, north));
        }

        /// <summary>
        /// Navball orientation: the crew capsule's real pose in the selected body's
        /// local frame. Contact zone — the capsule's own PhysX body (it can flex on
        /// the joints relative to the rest of the stack); build mode — the capsule
        /// visual; free flight — Ship.Attitude × the capsule part frame. Falls back
        /// to the whole craft when the assembly has no capsule. The pilot's head
        /// does not affect it.
        ///
        /// Navball axes: local Z = the capsule's nose (part +Y), local Y = the
        /// capsule's -Z (roll reference), which makes local X the capsule's +X.
        /// On the launch pad this reads pitch +90° with zero roll.
        /// </summary>
        private void FillAttitude(TabletTelemetry t, Ship ship, Vector3d chartRight, Vector3d up, Vector3d north)
        {
            t.MarkersValid = false;
            if (!TryCapsuleAxes(ship, out Vector3d bx, out Vector3d by, out Vector3d bz)
                && !TryCraftAxes(ship, out bx, out by, out bz))
            {
                t.Attitude = Quaternion.identity;
                return;
            }

            Vector3 f = ToHorizon(by, chartRight, up, north);
            Vector3 u = ToHorizon(-bz, chartRight, up, north);
            if (f.sqrMagnitude < 1e-6f)
            {
                t.Attitude = Quaternion.identity;
                return;
            }

            f.Normalize();
            u -= Vector3.Dot(u, f) * f;
            if (u.sqrMagnitude < 1e-6f)
            {
                u = Vector3.Cross(f, ToHorizon(bx, chartRight, up, north));
                if (u.sqrMagnitude < 1e-6f)
                {
                    u = Vector3.Cross(f, Vector3.up);
                }
            }

            t.Attitude = Quaternion.LookRotation(f, u.normalized);
            t.MarkersValid = true;
        }

        /// <summary>
        /// Capsule axes (right, up/nose, forward) in the astro frame. Inside the
        /// contact zone the capsule has its own proxy body: joints between parts can
        /// flex, so the root part is not necessarily the capsule's pose. In free
        /// flight the assembly is rigid, so the capsule frame is the ship attitude
        /// times the part orientation. false — there is no capsule in the assembly.
        /// </summary>
        private bool TryCapsuleAxes(Ship ship, out Vector3d x, out Vector3d y, out Vector3d z)
        {
            int partIndex = FindCapsulePart(out PartDefinition def);

            if (ContactZoneHost.Active)
            {
                // The zone's capsule body is the live pose. No capsule body — let the
                // caller use the root body; Ship.Attitude is stale inside the zone.
                if (!TryCapsuleBody(out Rigidbody body))
                {
                    return NoAxes(out x, out y, out z);
                }

                return AxesFromTransform(body.transform, out x, out y, out z);
            }

            if (Runner != null && Runner.BuildMode)
            {
                // The draft is the source of truth; a capsule left in Runner.Parts
                // from a previous flight must not leak into the build-mode pose.
                if (Builder != null)
                {
                    IReadOnlyList<PlacedPart> draft = Builder.Parts;
                    for (int i = 0; i < draft.Count; i++)
                    {
                        PlacedPart part = draft[i];
                        if (part.Kind == PartKind.Capsule && part.Visual != null && part.Visual.Root != null)
                        {
                            return AxesFromTransform(part.Visual.Root.transform, out x, out y, out z);
                        }
                    }
                }

                return NoAxes(out x, out y, out z);
            }

            if (ship != null && def != null
                && (Runner == null || Runner.Flight == null || !Runner.Flight.IsGone(partIndex)))
            {
                Vector4 o = def.Orientation;
                QuaternionD partFrame = new QuaternionD(o.x, o.y, o.z, o.w).Normalized;
                QuaternionD q = (ship.Attitude.Normalized * partFrame).Normalized;
                x = q.Rotate(new Vector3d(1d, 0d, 0d));
                y = q.Rotate(new Vector3d(0d, 1d, 0d));
                z = q.Rotate(new Vector3d(0d, 0d, 1d));
                return true;
            }

            return NoAxes(out x, out y, out z);
        }

        /// <summary>The capsule's live PhysX body in the contact zone (null elsewhere).</summary>
        private bool TryCapsuleBody(out Rigidbody body)
        {
            body = null;
            if (!ContactZoneHost.Active || Zone == null || Zone.Proxy == null || Runner == null)
            {
                return false;
            }

            int partIndex = FindCapsulePart(out _);
            if (partIndex < 0 || (Runner.Flight != null && Runner.Flight.IsGone(partIndex)))
            {
                return false;
            }

            int bodyIndex = Runner.Flight != null ? Runner.Flight.BodyOfPart(partIndex) : partIndex;
            body = bodyIndex >= 0 ? Zone.Proxy.BodyAt(bodyIndex) : null;
            return body != null;
        }

        /// <summary>
        /// Capsule world state in the astro frame. The zone bodies live in the site
        /// frame: their velocity is site-relative and must go through SiteFrame.ToWorld
        /// (origin velocity + planet rotation), not just AstroFrame.
        /// </summary>
        private bool TryCapsuleState(Rigidbody capsule, double time, out Vector3d position, out Vector3d velocity)
        {
            SiteFrame frame = Zone != null ? Zone.ZoneFrameAt(time) : null;
            if (frame == null || Zone.Proxy == null)
            {
                position = FloatingOrigin.Anchor + AstroFrame.ToAstro(capsule.position);
                velocity = AstroFrame.ToAstro(capsule.linearVelocity);
                return true;
            }

            Transform root = Zone.Proxy.transform;
            Vector3 localPosition = root.InverseTransformPoint(capsule.position);
            Vector3 localVelocity = root.InverseTransformDirection(capsule.linearVelocity);

            // Zone Unity axes are x=north, y=up, z=east; SiteFrame is x=east,
            // y=north, z=up (ContactZoneProxy.FromUnity).
            Vector3d sitePosition = new Vector3d(localPosition.z, localPosition.x, localPosition.y);
            Vector3d siteVelocity = new Vector3d(localVelocity.z, localVelocity.x, localVelocity.y);
            frame.ToWorld(sitePosition, siteVelocity, out position, out velocity);
            return true;
        }

        /// <summary>
        /// Capsule state outside the contact zone. Build mode — the capsule visual's
        /// position and the pad velocity (the craft is static). Free flight — the part
        /// offset from the ship point, rotated with the attitude, plus the rotation
        /// lever arm ω × r. false — there is no capsule.
        /// </summary>
        private bool TryCapsuleKinematics(Ship ship, out Vector3d position, out Vector3d velocity)
        {
            position = Vector3d.Zero;
            velocity = Vector3d.Zero;
            if (ship == null)
            {
                return false;
            }

            if (Runner != null && Runner.BuildMode)
            {
                if (Builder == null)
                {
                    return false;
                }

                IReadOnlyList<PlacedPart> draft = Builder.Parts;
                for (int i = 0; i < draft.Count; i++)
                {
                    PlacedPart part = draft[i];
                    if (part.Kind == PartKind.Capsule && part.Visual != null && part.Visual.Root != null)
                    {
                        Transform root = part.Visual.Root.transform;
                        Vector3 center = root.position + (root.up * part.Metrics.Center);
                        position = FloatingOrigin.Anchor + AstroFrame.ToAstro(center);
                        velocity = ship.Velocity;
                        return true;
                    }
                }

                return false;
            }

            int partIndex = FindCapsulePart(out PartDefinition def);
            if (def == null || (Runner != null && Runner.Flight != null && Runner.Flight.IsGone(partIndex)))
            {
                return false;
            }

            Vector3d offset = new Vector3d(def.Offset.x, def.Offset.y, def.Offset.z);
            if (ship.PositionIsCom && Runner != null && Runner.Flight != null)
            {
                // Part offsets are measured from the ship point; in free flight the
                // ship point is the COM, so shift them into the COM frame.
                offset -= Runner.Flight.Com;
            }

            QuaternionD attitude = ship.Attitude.Normalized;
            Vector3d worldOffset = attitude.Rotate(offset);
            Vector3d omegaWorld = attitude.Rotate(ship.AngularVelocity);
            position = ship.Position + worldOffset;
            velocity = ship.Velocity + Vector3d.Cross(omegaWorld, worldOffset);
            return true;
        }

        private static bool NoAxes(out Vector3d x, out Vector3d y, out Vector3d z)
        {
            x = y = z = Vector3d.Zero;
            return false;
        }

        /// <summary>Index of the crew capsule (the PlayerPassthrough part), −1 if none.</summary>
        private int FindCapsulePart(out PartDefinition def)
        {
            PartDefinition[] defs = Runner != null && Runner.Flight != null
                ? Runner.Flight.Defs
                : Runner != null ? Runner.Parts : null;
            if (defs != null)
            {
                for (int i = 0; i < defs.Length; i++)
                {
                    if (defs[i] != null && defs[i].PlayerPassthrough)
                    {
                        def = defs[i];
                        return i;
                    }
                }
            }

            def = null;
            return -1;
        }

        private static bool AxesFromTransform(Transform tr, out Vector3d x, out Vector3d y, out Vector3d z)
        {
            x = AstroFrame.ToAstro(tr.right).Normalized;
            y = AstroFrame.ToAstro(tr.up).Normalized;
            z = AstroFrame.ToAstro(tr.forward).Normalized;
            return true;
        }

        /// <summary>
        /// Whole-craft axes (right, up/nose, forward) in the astro frame — fallback
        /// when the assembly has no capsule. Inside the contact zone Ship.Attitude is
        /// not driven by PhysX, so the root body is the live source; in build mode
        /// the craft is drawn from the vessel root pose.
        /// </summary>
        private bool TryCraftAxes(Ship ship, out Vector3d x, out Vector3d y, out Vector3d z)
        {
            if (ContactZoneHost.Active && Zone != null && Zone.Proxy != null)
            {
                Rigidbody body = Zone.Proxy.BodyAt(Zone.Proxy.RootBodyIndex());
                if (body != null)
                {
                    return AxesFromTransform(body.transform, out x, out y, out z);
                }
            }

            if (Runner != null && Runner.BuildMode && Runner.VesselRoot != null)
            {
                return AxesFromTransform(Runner.VesselRoot, out x, out y, out z);
            }

            if (ship != null)
            {
                QuaternionD q = ship.Attitude.Normalized;
                x = q.Rotate(new Vector3d(1d, 0d, 0d));
                y = q.Rotate(new Vector3d(0d, 1d, 0d));
                z = q.Rotate(new Vector3d(0d, 0d, 1d));
                return true;
            }

            x = y = z = Vector3d.Zero;
            return false;
        }

        /// <summary>
        /// Navball markers. Prograde/retrograde follow the velocity of the selected
        /// speed frame; when it is at rest (on the launch pad in surface mode) the
        /// body-relative velocity is used so the markers do not vanish. Normal is the
        /// real orbit normal r × v (not a horizontal guess from the course), radial
        /// out is the local vertical at the capsule. At least one fallback keeps all
        /// six markers defined even for a purely radial trajectory.
        /// </summary>
        private void FillMarkers(TabletTelemetry t, Vector3d v, Vector3d vInertial, Vector3d r, Vector3d up,
            Vector3d chartRight, Vector3d north)
        {
            const double MinSpeed = 0.1d;

            Vector3d radialUp = r.Magnitude > 1e-6d ? r / r.Magnitude : up;

            Vector3d prograde = v;
            if (prograde.Magnitude < MinSpeed)
            {
                prograde = vInertial;
            }

            if (prograde.SqrMagnitude < 1e-12d)
            {
                prograde = north;
            }

            prograde = prograde.Normalized;

            Vector3d normal = Vector3d.Cross(r, vInertial);
            if (normal.SqrMagnitude < 1e-12d * System.Math.Max(1d, r.SqrMagnitude * vInertial.SqrMagnitude))
            {
                // Radial flight: the orbit plane is undefined, use the local level
                // normal of the course instead of hiding the whole marker set.
                normal = Vector3d.Cross(radialUp, prograde);
            }

            if (normal.SqrMagnitude < 1e-12d)
            {
                normal = north;
            }

            normal = normal.Normalized;

            t.ProgradeDir = ToHorizon(prograde, chartRight, up, north);
            t.NormalDir = ToHorizon(normal, chartRight, up, north);
            t.RadialOutDir = ToHorizon(radialUp, chartRight, up, north);
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
        /// Proper acceleration (g-load) is the numerical derivative of the capsule's
        /// inertial velocity minus gravity: ≈ 0 in free fall, ≈ F/m under thrust,
        /// ≈ g standing on the surface. Measurement window ≤ 1 s of sim time: longer
        /// means warp, where the average is meaningless (keeps the previous value).
        /// </summary>
        private void UpdateProperAcceleration(Vector3d velocity, Vector3d gravity, double time)
        {
            if (double.IsFinite(lastVelocityTime))
            {
                double dt = time - lastVelocityTime;
                if (dt > 1e-4d && dt <= 1d)
                {
                    Vector3d inertialAcceleration = (velocity - lastVelocity) / dt;
                    double measured = (inertialAcceleration - gravity).Magnitude;
                    properAcceleration = hasProperAcceleration
                        ? (properAcceleration * 0.6d) + (measured * 0.4d)
                        : measured;
                    hasProperAcceleration = true;
                }
            }

            lastVelocity = velocity;
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
