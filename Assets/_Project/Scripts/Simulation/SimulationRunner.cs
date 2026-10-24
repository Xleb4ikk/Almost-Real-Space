using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Simulation.ContactZone;
using Galilego.Simulation.Player;
using Galilego.Spacecraft;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Universe
{
    // UNITY INTEGRATION CHECKLIST: copy SimulationRunner.cs (this file), BodyView.cs,
    // ShipController.cs, ShipView.cs, BodyAuthoring.cs, StarSystemAuthoring.cs,
    // EphemerisRuntime.cs and Editor/StarSystemAuthoringEditor.cs; do NOT copy
    // P1bTests/ (duplicate UnityEngine types). Requires com.unity.mathematics +
    // com.unity.collections (the Jobs path OrbitalElements.CalculateBatch).
    // Scene: a GameObject hierarchy of bodies with BodyAuthoring (GO name = body name)
    // under StarSystemAuthoring at the root, BodyView on each body, and SimulationRunner
    // (System = root), ShipController, ShipView on the ship GameObject.
    // Execution order: SimulationRunner.Update → BodyView.LateUpdate → ShipView.LateUpdate.

    /// <summary>
    /// Owner of time and the physics loop (B1). The only MonoBehaviour that
    /// advances the simulation; BodyView/ShipView read the finished states.
    ///
    /// Mode automaton (SurfaceMotion.VesselRegime):
    ///   Flying  — orbital chain: chunks by control-tick, SampleControl,
    ///             Propagate; the returned event is handled here.
    ///   Landed  — constrained SurfaceMotion.Step dynamics by chunks; on thrust
    ///             EventReactions.TryLiftoff returns to Flying.
    ///   Destroyed — the ship simulation is stopped (debris — stage R2/A4).
    ///
    /// Frame contract (implements EphemerisRuntime.ComputeFrame):
    /// 1. decision = EphemerisRuntime.ComputeFrame(sys, warp, ship, t, realDt);
    /// 2. useLongWarp → warp.PrepareForLongWarp(t) + LongWarpDriver.AdvanceToTarget
    ///    (ballistics, thrust fully forbidden);
    /// 3. otherwise — the physical chain with the same RHS as at ×1: chunks by the
    ///    control-tick boundary, SampleControl on each chunk, Propagate.
    /// HardHorizonSeconds is set from BakedEndSeconds() at start — warp will not
    /// fly past the end of the baked world (the EphemerisEnd event fires honestly).
    /// The physical chain's time accumulates via Kahan (years of warp, thousands of
    /// frames); after a long warp the accumulator is reset to the result time.
    /// On the ground time also uses Kahan: after exact-root events the accumulator
    /// is reset to the event time.
    /// Calls — main thread only.
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [Header("Система")]
        [Tooltip("StarSystemAuthoring на корневом GameObject иерархии тел.")]
        public StarSystemAuthoring System;

        [Tooltip("Пауза симуляции (время не идёт, рендер остаётся).")]
        public bool Paused;

        [Header("Корабль")]
        [Tooltip("Начальная высота над поверхностью тела спавна (м).")]
        public double SpawnAltitudeMeters = 200000d;

        [Tooltip("Спавнить корабль на солнечной стороне (со стороны звезды) вместо противоположной.")]
        public bool SpawnFacingSun;

        [Tooltip("Стартовая фаза по орбите (градусы). 0 — солнце в зените (при SpawnFacingSun), 90 — солнце на горизонте, 180 — за планетой. Фаза вращает точку старта вдоль плоскости орбиты.")]
        public double SpawnPhaseDegrees;

        [Tooltip("Начальная горизонтальная скорость (м/с): 0 — сидим, ~7800 — орбита.")]
        public double SpawnHorizontalSpeedMps = 7800d;

        [Tooltip("Начальная масса корабля, кг.")]
        public double SpawnMassKg = 5000d;

        [Tooltip("Максимальная тяга главного двигателя, Н (0 — корабль без тяги).")]
        public double MainThrustNewtons;

        [Tooltip("Удельный импульс, с.")]
        public double MainIspSeconds = 320d;

        [Tooltip("Сухая масса (без топлива), кг.")]
        public double DryMassKg = 3000d;

        [Tooltip("Тело спавна (имя из иерархии); пусто — самое глубокое тело под корнем.")]
        public string SpawnBodyName;

        [Tooltip("Спавнить игрока пешком на поверхности (OnSurface), корабль — рядом на земле. Иначе старт в корабле на орбите.")]
        public bool SpawnOnSurface;

        [Tooltip("Спавн по координатам: вместо точки из орбитальной фазы игрок и корабль ставятся в lat/lon ниже (тело — SpawnBodyName). Для проверки биомов и декора в конкретных местах.")]
        public bool SpawnByCoordinates;

        [Tooltip("Широта точки спавна (градусы, −90..90). Работает только со SpawnByCoordinates и SpawnOnSurface.")]
        public double SpawnLatitudeDegrees;

        [Tooltip("Долгота точки спавна (градусы). Работает только со SpawnByCoordinates и SpawnOnSurface.")]
        public double SpawnLongitudeDegrees;

        /// <summary>Last walking-player coordinate output (spam throttle).</summary>
        private double lastPosLogSeconds = -1e9d;

        /// <summary>
        /// Walking player coordinates (throttled to 15 s of sim time): so a screenshot
        /// of a bare clearing can be pinned to a point (lat/lon/terrain) and its filters checked.
        /// </summary>
        private void LogSurfacePosition()
        {
            if (PlayerMode != PlayerMode.OnSurface || DominantBody == null)
            {
                return;
            }

            if (TimeSeconds - lastPosLogSeconds < 15d)
            {
                return;
            }

            lastPosLogSeconds = TimeSeconds;
            OrbitingBody body = DominantBody;
            body.SurfaceLatLonAt(PlayerPosition, TimeSeconds, out double latDeg, out double lonDeg);
            Debug.Log(string.Format(
                "[Walk] {0}: lat={1:F2}°, lon={2:F2}°, relief={3:F0} м",
                body.Name, latDeg, lonDeg, GroundHeight(body, latDeg, lonDeg)));
        }

        [Tooltip("Отступ спавна обломков над поверхностью при разрушении (м).")]
        public double SpawnEpsilonMeters = 1d;

        [Header("Сборка (для разрушения по стыкам)")]
        [Tooltip("Детали сборки. Пусто — корабль цельный (whole-ship разрушение). Массы нормируются к SpawnMassKg.")]
        public PartDefinition[] Parts = new PartDefinition[0];

        [Header("Обломки")]
        [Tooltip("Время жизни обломка (с).")]
        public double DebrisLifetimeSeconds = 600d;

        [Tooltip("Коэффициент отскока обломков от поверхности (0 — исчезают при входе в тело).")]
        public double DebrisSurfaceRestitution = 0.2d;

        /// <summary>Simulated system time (s since the ephemeris epoch).</summary>
        public double TimeSeconds => step != null ? step.TimeSeconds : 0d;

        public StarSystem SystemState { get; private set; }

        // The properties below are read by other scripts BEFORE the step exists (the
        // first frames of Play, before Init). A getter that threw a NullReference
        // turned every "if (Runner.Ship == null)" guard into a per-frame exception
        // and flooded the console, so they are null-safe here.
        public WarpController Warp => step != null ? step.Warp : null;

        public Ship Ship => step != null ? step.Ship : null;

        /// <summary>Ship regime: flying, resting on the surface or destroyed.</summary>
        public VesselRegime Regime => step != null ? step.Regime : VesselRegime.Destroyed;

        /// <summary>Player mode: in the ship / EVA / on the surface.</summary>
        public PlayerMode PlayerMode { get; private set; }

        public bool JetpackActive { get; private set; }

        /// <summary>
        /// Hover: gravity removed, input drives velocity (WASD horizontally,
        /// Space/Shift up/down). Toggled by double-tapping Shift in flight;
        /// landing/water/ship reset the mode.
        /// </summary>
        public bool PlayerHoverActive { get; private set; }

        public bool PlayerAirborne => playerAirborne;

        /// <summary>Player position in the double world (astro frame, inertial).</summary>
        public Vector3d PlayerPosition { get; private set; }

        /// <summary>Player velocity in the double world.</summary>
        public Vector3d PlayerVelocity { get; private set; }

        /// <summary>
        /// Render-space player point for the camera. While the player is carried by a
        /// vessel part, this rides the part's CURRENT pose (the mirror is applied after
        /// the step), so the view stays fixed in a falling cabin instead of lagging a
        /// physics frame behind it. false - not carried: use PlayerPosition.
        /// </summary>
        public bool TryGetPlayerRenderPoint(out Vector3 renderPoint)
        {
            return platformCarry.TryGetRenderPoint(out renderPoint);
        }

        /// <summary>Cabin-mode state for the debug overlay (empty when the player is not aboard).</summary>
        public string CabinDebug
        {
            get
            {
                if (!platformCarry.Active)
                {
                    return "cabin: -";
                }

                return "cabin: " + platformCarry.Platform.name
                    + "  pos=" + platformCarry.Position.X.ToString("F2") + "," + platformCarry.Position.Y.ToString("F2") + "," + platformCarry.Position.Z.ToString("F2")
                    + "  vel=" + platformCarry.Velocity.X.ToString("F2") + "," + platformCarry.Velocity.Y.ToString("F2") + "," + platformCarry.Velocity.Z.ToString("F2")
                    + "  weight=" + playerController.Gravity.ToString("F1") + " m/s2"
                    + "  zone=" + ContactZoneHost.Active
                    + "  shipAcc=" + shipAcceleration.Magnitude.ToString("F1");
            }
        }

        /// <summary>The cabin's up in render space while the player is aboard (the camera's vertical).</summary>
        public bool TryGetCarriedUpRender(out Vector3 up)
        {
            return platformCarry.TryGetUp(out up);
        }

        /// <summary>Player seated: the physics step is frozen at PlayerSeatedPosition.</summary>
        public bool PlayerSeated { get; private set; }

        /// <summary>
        /// The player is carried by a vessel part right now (cabin mode): the cabin walls,
        /// the monitors and the hull are metres away, not a planet radius away.
        /// </summary>
        public bool PlayerCarried => platformCarry.Active;

        /// <summary>
        /// Support point of the seated player (feet at the seat), double world; driven by StepPlayer.
        /// This is the RENDER ANCHOR (FloatingOrigin.Anchor), not the visible camera pose:
        /// the camera sits at the drawn seat anchor (PlayerView), so lag/jitter of this
        /// point does not affect the picture.
        /// </summary>
        public Vector3d PlayerSeatedPosition { get; set; }

        /// <summary>
        /// Seat "up" in render space: the seated camera lays the eye height along it
        /// (in the same axes the ship is drawn in — the astro version via the flat
        /// bridge lies here in the contact zone). While the player is seated,
        /// PlayerView updates it every frame — a live seat "up".
        /// </summary>
        public Vector3 PlayerSeatedUpRender { get; set; }

        /// <summary>Player intent for the frame (filled by PlayerController).</summary>
        public PlayerIntent PlayerIntent;

        /// <summary>
        /// Noclip enabled (cheat-menu toggle). While on, the player mode is forced
        /// to EVA: walking/swimming would project the player back onto the terrain,
        /// and inside the ship they would be bound to the ship point.
        /// </summary>
        public bool NoclipActive { get; private set; }

        /// <summary>
        /// Enable/disable noclip. exitDirection — where to eject the player from the
        /// ship on enable (camera direction); zero — outward from the body.
        /// On disable the player stays where they flew: if inside terrain or water
        /// they are landed on the surface (otherwise the mode step would push them
        /// out of the planet), otherwise free EVA flight continues.
        /// </summary>
        public void SetNoclip(bool active, Vector3d exitDirection)
        {
            if (NoclipActive == active)
            {
                return;
            }

            NoclipActive = active;
            if (active)
            {
                if (PlayerMode == PlayerMode.InShip)
                {
                    TryExitShip(exitDirection.SqrMagnitude > 1e-12d ? exitDirection.Normalized : OutwardFromBody(), 1d);
                }

                PlayerMode = PlayerMode.EVA;
                playerAirborne = true;
                JetpackActive = false;
                PlayerHoverActive = false;
                return;
            }

            PlayerMode = PlayerMode.EVA;
            playerAirborne = true;
            JetpackActive = false;
            PlayerHoverActive = false;
            evaCollisionGraceUntil = TimeSeconds + 2.0d;

            // Leaving noclip: drop the cheat speed, the player stays still relative to the frame.
            PlayerVelocity = LocalFrameVelocity(TimeSeconds);
            OrbitingBody body = DominantBody;
            if (body == null)
            {
                return;
            }

            // Inside terrain/water — land on the surface: otherwise the very next mode
            // step (EVA/OnSurface) would treat the point as contact and push the player
            // out from its side of the planet.
            body.EvaluateWorldState(TimeSeconds, out Vector3d bodyPos, out _);
            body.SurfaceLatLonAt(PlayerPosition, TimeSeconds, out double latDeg, out double lonDeg);
            double surfaceRadius = body.Radius + GroundHeight(body, latDeg, lonDeg);
            if ((PlayerPosition - bodyPos).Magnitude <= surfaceRadius)
            {
                LandPlayer(body, TimeSeconds);
            }
        }

        /// <summary>Direction from the dominant body centre to the player (zenith).</summary>
        private Vector3d OutwardFromBody()
        {
            OrbitingBody body = DominantBody;
            if (body == null)
            {
                return new Vector3d(0d, 0d, 1d);
            }

            body.EvaluateWorldState(TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d radial = PlayerPosition - bodyPos;
            return radial.SqrMagnitude > 1e-12d ? radial.Normalized : new Vector3d(0d, 0d, 1d);
        }

        /// <summary>Raw throttle 0..1 from input (ShipController); EffectiveThrottle caps above ×3.</summary>
        public double RawThrottle { get; set; }

        /// <summary>Registry of body transforms (filled by BodyView); ShipView reads it for the floating origin.</summary>
        public BodyTransformRegistry SystemView { get; set; }

        /// <summary>Body the ship currently lives relative to (floating origin, SOI).</summary>
        public OrbitingBody DominantBody => step != null ? step.DominantBody : null;

        /// <summary>Debris pool (slots are reused); DebrisView reads it for rendering.</summary>
        public DebrisPool Debris => step.Debris;

        /// <summary>
        /// Staged propulsion model (null — legacy: a single thrust from the inspector).
        /// Read by the ΔV panel; set by SpacecraftBuilder when flight starts.
        /// </summary>
        public StagePropulsion Propulsion => step != null ? step.Propulsion : null;

        /// <summary>Ship step: flight, landing, events. All state logic lives there.</summary>
        private VesselStep step;
        private DebrisUpdater debrisUpdater;
        private SpacecraftPhysics physics;
        private ThrustSource mainThrustSource;
        private StageThrustSource stageThrustSource;
        private StageDepletionDetector stageDetector;

        // Player walking: a pure support controller + the local site frame.
        private readonly PlayerSurfaceController playerController = new PlayerSurfaceController();

        /// <summary>Whether the player is standing on support (for probes/debug).</summary>
        public bool PlayerGrounded => !playerController.Airborne;

        /// <summary>
        /// Whether the player is crouched (automatically, to pass a low opening).
        /// The camera takes the eye height from here (FirstPersonCamera).
        /// </summary>
        public bool PlayerCrouched => playerController.Crouched;

        /// <summary>Walking diagnostic: the player's input is blocked in this substep.</summary>
        public bool PlayerBlocked => playerController.Blocked;

        /// <summary>Walking diagnostic: foot step-up over the last substep (m).</summary>
        public double PlayerDebugLift => playerController.DebugLift;

        /// <summary>Current player capsule height (m): crouches under a slanted wall/hatch.</summary>
        public double PlayerCapsuleHeight => playerController.CurrentHeightMeters;

        /// <summary>Stoop fraction 0..1 (0 — full height, 1 — minimum height). The camera lowers the eyes by it.</summary>
        public float PlayerStoopFraction
        {
            get
            {
                // EVA near the hatch: duck the camera together with the collision body.
                if (PlayerMode == PlayerMode.EVA && evaCrouched)
                {
                    return 1f;
                }

                return (float)Math.Max(0d, Math.Min(1d,
                    (PlayerSurfaceController.CapsuleHeight - playerController.CurrentHeightMeters)
                    / (PlayerSurfaceController.CapsuleHeight - PlayerSurfaceController.CapsuleHeightCrouched)));
            }
        }

        /// <summary>Support id under the player (-1 — no support; see TerrainSupport/SiteBoxSupport).</summary>
        public int PlayerGroundSourceId => playerController.GroundSourceId;

        private readonly TerrainSupport terrainSupport = new TerrainSupport();
        private readonly SiteBoxSupport siteSupport = new SiteBoxSupport();
        private readonly VesselSupport vesselSupport = new VesselSupport();
        private readonly CompositePlayerSupport playerSupport = new CompositePlayerSupport();
        private readonly TerrainPatchSupport patchSupport = new TerrainPatchSupport();
        private readonly CompositePlayerSupport cabinSupport = new CompositePlayerSupport();
        private readonly PlayerPlatformCarry platformCarry = new PlayerPlatformCarry();
        private ContactZoneHost zoneHost;

        // Inertial acceleration of the vessel (gravity included), low-pass filtered. A carried
        // player feels gravity MINUS this: thrust presses them to the floor, free fall — weightless.
        private Vector3d shipAcceleration;
        private Vector3d lastShipVelocity;
        private double lastShipAccelerationTime;
        private bool shipAccelerationValid;

        private const double ShipAccelerationFilterSeconds = 0.1d;
        private const double ShipAccelerationClampMps2 = 3000d;

        /// <summary>
        /// Root of the vessel visuals (ShipView's object). Outside the contact zone it is
        /// put at Ship.Position BEFORE the player step, so the player collides with the
        /// vessel where it is now, not where it was last frame.
        /// </summary>
        public Transform VesselRoot;

        [Tooltip("Коллизия игрока с корпусом в EVA. Выключи для диагностики дрейфа.")]
        public bool EvaHullCollision = true;

        /// <summary>Cumulative EVA hull correction events (diagnostics).</summary>
        public int EvaHullCorrections { get; private set; }

        /// <summary>Cumulative EVA hull correction distance, m (diagnostics).</summary>
        public double EvaHullCorrectionMeters { get; private set; }

        private static readonly SphericalTerrain playerFallbackTerrain = new SphericalTerrain();

        // EVA hull collision: a swept capsule plus a start-overlap push-out. The sweep stops the
        // motion (no tunnelling at orbital speeds); the push-out exists because a vessel that is
        // rotating can turn a hull into the body while it is already touching it, and a cast
        // ignores colliders it starts inside. The push direction is biased to the side the player
        // came from, so a thin shell cannot resolve to its inner side and "teleport" them.
        private readonly RaycastHit[] evaHits = new RaycastHit[32];
        private readonly Collider[] evaOverlaps = new Collider[32];
        private GameObject evaProbeObject;
        private CapsuleCollider evaProbeCollider;

        /// <summary>No hull collision for a moment after leaving the cabin/seat: the player is still in the hatch.</summary>
        private double evaCollisionGraceUntil;

        // EVA auto-crouch near the capsule: the Orion hatch is ~1 m, the standing
        // collision capsule is 1.45 m, so approaching the door pushed the player
        // sideways instead of letting them in. Near the capsule the body ducks.
        private bool evaCrouched;
        private Transform evaCapsule;
        private Renderer[] evaCapsuleRenderers;
        private bool evaCapsuleSearched;

        // EVA frame in free flight. In weightlessness there is no planet vertical to stand on,
        // and the hatch is cut for a person lying along the capsule axis. TWO axes are kept:
        // - evaUpAstro is the CAMERA/INPUT vertical. It is inherited from the cabin at the hatch
        //   and then stays INERTIAL: a spinning vessel must not turn the player's view.
        // - evaCollisionUpAstro is the invisible COLLISION body axis. It follows the capsule
        //   while the player is near, so the body lines up with the hatch and can pass it.
        private Vector3d evaUpAstro;
        private bool evaUpValid;
        private Vector3d evaCollisionUpAstro;
        private bool evaCollisionUpValid;
        private bool evaAlignedToVessel;
        private float evaCapsuleDistance = float.MaxValue;

        /// <summary>Distance to the capsule (m) below which the COLLISION body follows the capsule axis.</summary>
        private const float EvaAlignEnterMeters = 30f;

        /// <summary>Distance (m) above which the COLLISION body goes back to the planet radial (hysteresis).</summary>
        private const float EvaAlignExitMeters = 60f;

        /// <summary>How fast the CAMERA/INPUT vertical eases back to the planet radial far away (1/s).</summary>
        private const double EvaUpAlignRate = 2.5d;

        /// <summary>Felt weight below this (m/s²) in free flight counts as zero: filtered-acceleration noise must not "drop" a player standing in the hatch onto the hull.</summary>
        private const double CabinWeightlessDeadbandMps2 = 0.15d;

        // Ship state at the START of the current frame. The player is stepped after the
        // whole ship step, so Ship.Position/Velocity are already at the END of the
        // frame: moving the player with them for the whole frame made the player travel
        // farther than the craft by half its acceleration × frame² — a steady drift
        // that was independent of input and got worse at low FPS.
        private Vector3d frameShipStartPosition;
        private Vector3d frameEvaOffset;
        private double frameShipStartTime;

        // EVA velocity RELATIVE to the vessel frame, kept across frames. Do not re-derive
        // it as PlayerVelocity - frameVelocity every step: the frame velocity already
        // contains the ship's acceleration, and the smooth brake would then leave a
        // permanent lag of about a/response (≈1.6 m/s at g) — the craft "falls away"
        // even though nothing is pressed. The relative state is the player's own.
        private Vector3d evaRelativeVelocity;
        private bool evaRelativeValid;

        private bool playerAirborne;
        private bool playerJumpQueued;
        private bool jetpackToggleQueued;
        private bool hoverToggleQueued;
        private bool jetpackBeforeHover;

        /// <summary>Player jump speed (m/s, vertically from the surface).</summary>
        private const double PlayerJumpSpeed = 5d;

        /// <summary>Velocity response while hovering (1/s): stopping and accelerating in air.</summary>
        private const double PlayerHoverResponse = 6d;

        /// <summary>Horizontal velocity response in air (1/s), the same with and without the jetpack.</summary>
        private const double PlayerAirResponse = 6d;

        /// <summary>Maximum player integration substep (s).</summary>
        private const double PlayerMaxStepSeconds = 0.5d;

        /// <summary>
        /// Below this altitude (m) and slower than the speed below, free EVA flight is handed
        /// to the surface controller: it is the only one that sees buildings and the vessel.
        /// </summary>
        private const double SurfaceControllerAltitudeMeters = 3000d;
        private const double SurfaceControllerMaxRelativeSpeedMps = 200d;

        /// <summary>Player radius for tree-trunk collisions (m).</summary>
        private const double PlayerCollisionRadiusMeters = 0.45d;

        private void Awake()
        {
            // Colliders of the vessel move every frame (mirror, ship view); every query — the
            // player's, the E rays, the tablet pointer — must see the CURRENT poses, not those of
            // the last physics step (at orbital speeds that is hundreds of metres).
            Physics.autoSyncTransforms = true;

            if (System == null)
            {
                enabled = false;
                Debug.LogWarning("SimulationRunner: не назначен StarSystemAuthoring — симуляция отключена.");
                return;
            }

            SystemState = System.BuildBlueprint().Build();
            bool baked = EphemerisRuntime.AttachFromStreamingAssets(SystemState);

            var warp = new WarpController();
            physics = new SpacecraftPhysics { ToleranceFrameSystem = SystemState };
            physics.Sources.Add(new CachedGravitySource(SystemState));
            mainThrustSource = null;
            if (MainThrustNewtons > 0d)
            {
                mainThrustSource = new ThrustSource(MainThrustNewtons, DryMassKg, new ConstantIsp(MainIspSeconds));
                mainThrustSource.ThrottleAt = t => warp.CurrentControlSnapshot.Throttle;
                physics.Sources.Add(mainThrustSource);
            }

            var propagator = new EventDrivenPropagator(physics);
            foreach (OrbitingBody body in SystemState.AllBodies)
            {
                propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(body));
                if (body.Atmosphere != null)
                {
                    propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereEntry(body));
                    propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForAtmosphereExit(body));
                    physics.Sources.Add(new DragSource(body));
                }
            }

            // Horizon contract: long warp does not fly past the end of the baked world
            // (duplicated explicitly in the ComputeFrame wiring — without it the
            // EphemerisEnd event would not fire).
            propagator.HardHorizonSeconds = SystemState.BakedEndSeconds();
            var driver = new LongWarpDriver(physics, propagator);
            step = new VesselStep(
                SystemState, warp, propagator, driver, mainThrustSource,
                new JointedBreakup(), new DebrisPool(),
                Parts, SpawnEpsilonMeters, DebrisLifetimeSeconds);

            debrisUpdater = new DebrisUpdater(SystemState)
            {
                SurfaceRestitution = DebrisSurfaceRestitution
            };

            SpawnShip();
            PlayerMode = PlayerMode.InShip;
            JetpackActive = false;
            PlayerHoverActive = false;
            PlayerPosition = Ship.Position;
            PlayerVelocity = Ship.Velocity;
            if (SpawnOnSurface)
            {
                SpawnPlayerOnSurface();
            }

            FloatingOrigin.Anchor = PlayerPosition;

            if (!baked)
            {
                Debug.LogWarning("SimulationRunner: эфемериды не прицеплены — тела на кеплеровых рельсах (штатно, точность ниже).");
            }
        }

        private void SpawnShip()
        {
            step.DominantBody = FindSpawnBody();
            DominantBody.EvaluateWorldState(0d, out Vector3d bodyP, out Vector3d bodyV);
            Vector3d radial = bodyP - SystemState.Root.RootPosition;
            if (radial.SqrMagnitude <= 0d)
            {
                radial = new Vector3d(0d, 0d, 1d);
            }

            radial = radial.Normalized;
            if (SpawnFacingSun)
            {
                radial = -radial;
            }

            Vector3d horizontal = Vector3d.Cross(new Vector3d(0d, 0d, 1d), radial);
            if (horizontal.SqrMagnitude < 1e-30d)
            {
                horizontal = new Vector3d(1d, 0d, 0d);
            }

            horizontal = horizontal.Normalized;

            // Phase: rotates the start point along the orbital plane. 0° — the point
            // "under the sun" (or opposite it), 90° — a quarter orbit (sun on the
            // horizon), 180° — the opposite side of the planet.
            double phase = SpawnPhaseDegrees * (Math.PI / 180d);
            Vector3d positionDirection = radial;
            Vector3d velocityDirection = horizontal;
            if (Math.Abs(phase) > 1e-12d)
            {
                Vector3d normal = Vector3d.Cross(radial, horizontal);
                Vector3d towardPhase = Vector3d.Cross(normal, radial);
                positionDirection = (radial * Math.Cos(phase)) + (towardPhase * Math.Sin(phase));
                velocityDirection = Vector3d.Cross(normal, positionDirection);
            }

            step.Ship = new Ship(
                bodyP + (positionDirection * (DominantBody.Radius + SpawnAltitudeMeters)),
                bodyV + (velocityDirection * SpawnHorizontalSpeedMps),
                SpawnMassKg);
        }

        /// <summary>
        /// Teleports the player to an arbitrary surface point without changing the
        /// mode. Needed by the editor site tool (SurfaceSitesWindow) to jump to a
        /// saved site right in Play.
        ///
        /// The ship moves with the player: on the surface the player is bound to the
        /// ship (the disembark mechanic), otherwise the teleport would detach them from
        /// the site and "E" would no longer work. Mode and orientation are preserved,
        /// so disembark/splashdown/walking keep working as usual.
        /// </summary>
        public void TeleportTo(Vector3d surfacePosition, Vector3d surfaceVelocity)
        {
            if (!SystemStateInitialized())
            {
                Debug.LogWarning("[Teleport] Система не инициализирована — некуда телепортироваться.");
                return;
            }

            Vector3d delta = surfacePosition - PlayerPosition;
            Ship.Position = Ship.Position + delta;
            Ship.Velocity = surfaceVelocity;
            PlayerPosition = surfacePosition;
            PlayerVelocity = surfaceVelocity;
            evaRelativeValid = false;
            step.ResetTimeAccumulator();
            FloatingOrigin.Anchor = PlayerPosition;
            Debug.Log(string.Format("[Teleport] игрок -> lat/lon места, смещение {0:F0} м", delta.Magnitude));
        }

        private bool SystemStateInitialized()
        {
            return SystemState != null && Ship != null;
        }

        /// <summary>
        /// Walking spawn: the ship is placed on the terrain at the spawn point, the
        /// player a few metres away along the tangent (so the camera is not inside
        /// the ship but the E entry distance is within EnterDistance).
        /// </summary>
        private void SpawnPlayerOnSurface()
        {
            OrbitingBody body = DominantBody;
            body.EvaluateWorldState(0d, out Vector3d bodyP, out _);
            body.SurfaceLatLonAt(Ship.Position, 0d, out double latDeg, out double lonDeg);
            if (SpawnByCoordinates)
            {
                latDeg = Math.Max(-90d, Math.Min(90d, SpawnLatitudeDegrees));
                lonDeg = SpawnLongitudeDegrees;
            }

            double ground = GroundHeight(body, latDeg, lonDeg);
            Debug.Log(string.Format(
                "[Spawn] {0}: тело={1} lat={2:F3}° lon={3:F3}° relief={4:F0} м",
                SpawnByCoordinates ? "по координатам" : "точка фазы",
                body.Name, latDeg, lonDeg, ground));
            body.GetSurfaceState(latDeg, lonDeg, ground, 0d, out Vector3d surfacePos, out Vector3d surfaceVel);

            Ship.Position = surfacePos;
            Ship.Velocity = surfaceVel;
            step.Regime = VesselRegime.Landed;

            Vector3d up = (surfacePos - bodyP).Normalized;
            Vector3d east = Vector3d.Cross(new Vector3d(0d, 0d, 1d), up);
            if (east.SqrMagnitude < 1e-12d)
            {
                east = new Vector3d(1d, 0d, 0d);
            }

            east = east.Normalized;
            body.SurfaceLatLonAt(surfacePos + (east * 4d), 0d, out double playLat, out double playLon);
            double playGround = GroundHeight(body, playLat, playLon);
            body.GetSurfaceState(playLat, playLon, playGround, 0d, out Vector3d playPos, out Vector3d playVel);

            PlayerPosition = playPos;
            PlayerVelocity = playVel;
            // Spawning in the ocean — swim immediately (for water checks: coordinate
            // spawn into the ocean in biome tests).
            PlayerMode = WaterQuery.IsWaterAt(body, playLat, playLon)
                ? PlayerMode.Swimming
                : PlayerMode.OnSurface;
            playerAirborne = false;
            JetpackActive = false;
            PlayerHoverActive = false;
        }

        private OrbitingBody FindSpawnBody()
        {
            if (!string.IsNullOrEmpty(SpawnBodyName))
            {
                foreach (OrbitingBody body in SystemState.AllBodies)
                {
                    if (body.Name == SpawnBodyName)
                    {
                        return body;
                    }
                }

                throw new InvalidOperationException("SimulationRunner: тело спавна \"" + SpawnBodyName + "\" не найдено в системе.");
            }

            // The deepest body under the root (the first planet in the hierarchy).
            OrbitingBody deepest = SystemState.Root;
            while (deepest.Children.Count > 0)
            {
                deepest = deepest.Children[0];
            }

            return deepest;
        }

        private void Update()
        {
            // step == null — initialisation failed (early error/disable): stay silent
            // like a disabled runner rather than throwing NRE every frame.
            if (step == null || Paused || Ship == null || Regime == VesselRegime.Destroyed)
            {
                return;
            }

            float realDt = Time.deltaTime;
            if (!(realDt > 0f))
            {
                return;
            }

            // Where the craft was before this frame's step: the EVA frame velocity is
            // derived from the ACTUAL displacement over the frame, not from the
            // end-of-frame linear velocity.
            frameShipStartPosition = Ship.Position;
            frameShipStartTime = TimeSeconds;

            // Build mode: the craft is static (its position is held by build mode at
            // execution order -110, before this Update), so the ship step is skipped.
            // World time flows (the planet moves and the site rides with it — craft
            // pinning accounts for that) and the player lives normally: walking, water,
            // render anchor.
            if (BuildMode)
            {
                double buildFrameStart = TimeSeconds;
                step.AdvanceTime(realDt);
                StepPlayer(buildFrameStart);
                // Build mode does the pinning and hands the craft to physics — EXACTLY
                // HERE and at exactly this time: computed earlier (before AdvanceTime)
                // the craft lags the pad by the surface velocity × dt (the planet spins fast).
                BuildModeFrameHook?.Invoke(TimeSeconds);
                step.DominantBody = SystemState.FindDominantBody(Ship.Position, TimeSeconds);
                FloatingOrigin.Anchor = PlayerPosition;
                LogSurfacePosition();
                return;
            }

            // The contact zone drives the ship itself (vertical slice): the physics step is
            // skipped, but world time flows with the real dt — TimeSeconds advances and the
            // planet moves (VesselStep.AdvanceTime, no orbital logic). The player is stepped
            // too: they walk near the craft on the pad. Debris and tracing inside the zone
            // are not updated — deferred to phase 7. Outside the zone — as usual.
            if (ContactZoneHost.Active)
            {
                double zoneFrameStart = TimeSeconds;
                step.AdvanceTime(realDt);
                StepPlayer(zoneFrameStart);
                step.DominantBody = SystemState.FindDominantBody(Ship.Position, TimeSeconds);
                FloatingOrigin.Anchor = PlayerPosition;
                LogSurfacePosition();
                return;
            }

            step.RawThrottle = RawThrottle;
            double frameStartTime = TimeSeconds;
            bool playerInShip = PlayerMode == PlayerMode.InShip || PlayerSeated;
            if (Regime == VesselRegime.Landed)
            {
                step.StepLanded(realDt, playerInShip);
            }
            else
            {
                step.StepFlying(realDt, playerInShip);
            }

            if (Debris != null)
            {
                debrisUpdater.AdvanceAll(Debris, TimeSeconds, Ship.Position);
            }

            SyncVesselRoot();
            StepPlayer(frameStartTime);
            step.DominantBody = SystemState.FindDominantBody(Ship.Position, TimeSeconds);

            // Render anchor — the player position (floating origin): views read the fresh
            // anchor in LateUpdate, so it is set at the very end of the physics step.
            FloatingOrigin.Anchor = PlayerPosition;

            LogSurfacePosition();
        }

        /// <summary>
        /// Build mode: the craft is static, the ship physics is not stepped.
        /// Set by SpacecraftBuilder on entering build and cleared on flight start.
        /// </summary>
        public bool BuildMode { get; set; }

        /// <summary>
        /// Build-mode hook: called every frame AFTER the time and player steps, at the
        /// frame's current time. It pins the craft to the pad and hands it to physics
        /// (set by SpacecraftBuilder). Called from the main thread, from the runner's Update.
        /// </summary>
        public System.Action<double> BuildModeFrameHook;

        /// <summary>
        /// Set the ship assembly from outside: both the public Parts field (read by the
        /// contact zone) and the copy in the step (breakup/landing). Build mode calls it
        /// when handing the craft to physics.
        /// </summary>
        public void SetVesselParts(PartDefinition[] parts)
        {
            Parts = parts;
            step.Parts = parts;
        }

        /// <summary>
        /// Set the staged propulsion model (build mode on flight start): the legacy
        /// source is removed from physics, the staged one is enabled (thrust/flow of the
        /// active stage) and the section-depletion detector is registered in the propagator.
        /// null — revert to the legacy path.
        /// </summary>
        public void SetVesselPropulsion(StagePropulsion propulsion)
        {
            if (step == null)
            {
                return;
            }

            if (propulsion == null)
            {
                SetVesselFlight(null);
                if (stageThrustSource != null)
                {
                    physics.Sources.Remove(stageThrustSource);
                }

                // Put the legacy source back in its original slot (gravity,
                // thrust, then drag): the order determines the summation order
                // of the contributions, i.e. byte-identical traces.
                if (mainThrustSource != null && !physics.Sources.Contains(mainThrustSource))
                {
                    physics.Sources.Insert(Math.Min(1, physics.Sources.Count), mainThrustSource);
                }

                if (stageDetector != null)
                {
                    step.Propagator.CrossingDetectors.Remove(stageDetector);
                }

                step.Propulsion = null;
                step.DepletionDetector = null;
                step.Thrust = step.MainThrust;
                return;
            }

            if (stageThrustSource == null)
            {
                stageThrustSource = new StageThrustSource
                {
                    ThrottleAt = t => step.Warp.CurrentControlSnapshot.Throttle
                };
                stageDetector = new StageDepletionDetector();
            }

            stageThrustSource.Model = propulsion;
            if (mainThrustSource != null)
            {
                physics.Sources.Remove(mainThrustSource);
            }

            if (!physics.Sources.Contains(stageThrustSource))
            {
                physics.Sources.Add(stageThrustSource);
            }

            if (!step.Propagator.CrossingDetectors.Contains(stageDetector))
            {
                step.Propagator.CrossingDetectors.Add(stageDetector);
            }

            step.Propulsion = propulsion;
            step.DepletionDetector = stageDetector;
            step.Thrust = stageThrustSource;

            double expected = propulsion.AttachedTotalKg() + propulsion.DetachedMassKg;
            double relative = Math.Abs(expected - Ship.Mass) / Math.Max(1d, Ship.Mass);
            if (relative > 1e-6d)
            {
                Debug.LogWarning(string.Format(
                    "[Prop] модель не сходится с массой корабля: ядра {0:F1} кг + вне сборки {1:F1} кг, Ship.Mass {2:F1} кг (отн. {3:E2})",
                    propulsion.AttachedTotalKg(), propulsion.DetachedMassKg, Ship.Mass, relative));
            }
            else
            {
                Debug.Log(string.Format(
                    "[Prop] ступенчатая тяга: ступеней {0}, масса по модели {1:F0} кг, автозажигание {2}",
                    propulsion.StageCount, expected, propulsion.AutoIgnite ? "вкл" : "выкл"));
            }
        }

        private Galilego.Flight.FlightThrustSource flightSource;

        /// <summary>6-DOF flight model of the current craft (null — none).</summary>
        public Galilego.Flight.VesselFlightModel Flight => step != null ? step.Flight : null;

        /// <summary>
        /// Attach the flight model (after SetVesselPropulsion): engine forces/torques at
        /// their real points instead of centre thrust. null — detach (back to building).
        /// </summary>
        public void SetVesselFlight(Galilego.Flight.VesselFlightModel model)
        {
            if (step == null)
            {
                return;
            }

            if (model == null)
            {
                if (flightSource != null)
                {
                    physics.Sources.Remove(flightSource);
                }

                step.Flight = null;
                step.FlightSource = null;
                return;
            }

            if (flightSource == null)
            {
                flightSource = new Galilego.Flight.FlightThrustSource();
            }

            model.Propulsion = step.Propulsion;
            flightSource.Model = model;
            if (stageThrustSource != null)
            {
                physics.Sources.Remove(stageThrustSource);
            }

            if (!physics.Sources.Contains(flightSource))
            {
                physics.Sources.Add(flightSource);
            }

            step.Flight = model;
            step.FlightSource = flightSource;
            step.PressureAt = AmbientPressurePa;
            step.Thrust = flightSource;
        }

        /// <summary>
        /// Static ambient pressure at a point, Pa: p = ρ0·g·H·exp(−h/H) (isothermal
        /// atmosphere, consistent with the body profile's density and scale height). Vacuum — 0.
        /// </summary>
        public double AmbientPressurePa(Galilego.Core.Vector3d position, double timeSeconds)
        {
            OrbitingBody body = DominantBody;
            AtmosphereProfile atm = body != null ? body.Atmosphere : null;
            if (atm == null || atm.ScaleHeightMeters <= 0d || atm.SeaLevelDensityKgPerCubicMeter <= 0d || body.Radius <= 0d)
            {
                return 0d;
            }

            body.EvaluateWorldState(timeSeconds, out Galilego.Core.Vector3d bodyPosition, out _);
            double h = (position - bodyPosition).Magnitude - body.Radius;
            if (h >= atm.TopAltitudeMeters)
            {
                return 0d;
            }

            double g = body.StandardGravitationalParameter / (body.Radius * body.Radius);
            return atm.SeaLevelDensityKgPerCubicMeter * g * atm.ScaleHeightMeters * Math.Exp(-Math.Max(0d, h) / atm.ScaleHeightMeters);
        }

        /// <summary>Remove the staged model (back to building) — returns to the legacy path.</summary>
        public void ClearVesselPropulsion()
        {
            SetVesselPropulsion(null);
        }

        /// <summary>
        /// Ship dry mass (without burnable fuel) for thrust: without it ThrustSource
        /// keeps the 3000 kg default and "eats" the tank. Build mode calls it on flight
        /// start (BuildMath.EffectiveDryMass).
        /// </summary>
        public void SetDryMass(double dryMassKg)
        {
            DryMassKg = dryMassKg;
            if (step != null && step.MainThrust != null)
            {
                step.MainThrust.DryMassKg = dryMassKg;
            }
        }

        /// <summary>
        /// Set the ship state from outside (build mode: handing the craft to physics and
        /// reverting to build). Resets the time accumulator like a teleport: the
        /// accumulated compensation does not apply to the new point.
        /// </summary>
        public void SetShipState(Vector3d position, Vector3d velocity, VesselRegime regime)
        {
            Ship.Position = position;
            Ship.Velocity = velocity;
            step.Regime = regime;
            step.ResetTimeAccumulator();
            FloatingOrigin.Anchor = PlayerPosition;
        }

        /// <summary>
        /// Place the player on foot on the surface (build mode: move the spawn to the
        /// launch pad). Velocity — the surface velocity.
        /// </summary>
        public void SetPlayerOnSurface(Vector3d position, Vector3d velocity)
        {
            PlayerPosition = position;
            PlayerVelocity = velocity;
            PlayerMode = PlayerMode.OnSurface;
            playerAirborne = false;
            JetpackActive = false;
            PlayerHoverActive = false;
            FloatingOrigin.Anchor = PlayerPosition;
        }

        // --- Player ----------------------------------------------------------

        /// <summary>
        /// Exit the ship (only from InShip). The player is placed 3 m along
        /// exitDirection with a small push; if the ship is on the surface, the very
        /// first step lands the player on the ground (OnSurface).
        /// </summary>
        public bool TryExitShip(Vector3d exitDirection, double pushSpeed)
        {
            if (PlayerMode != PlayerMode.InShip || Ship == null)
            {
                return false;
            }

            Vector3d direction = exitDirection.Normalized;
            Vector3d exitPoint = Ship.Position + (direction * 3d);
            PlayerPosition = exitPoint;
            PlayerVelocity = Ship.Velocity + (direction * pushSpeed);

            // Free flight: the vessel may be rotating, and the exit point is not the COM.
            // The player must leave with the rigid-body velocity at their own point.
            if (!ContactZoneHost.Active && Ship.PositionIsCom)
            {
                Vector3d omega = Ship.Attitude.Rotate(Ship.AngularVelocity);
                PlayerVelocity += Vector3d.Cross(omega, exitPoint - Ship.Position);
            }

            PlayerMode = PlayerMode.EVA;
            playerAirborne = false;
            JetpackActive = false;
            PlayerHoverActive = false;
            evaCollisionGraceUntil = TimeSeconds + 2.0d;
            return true;
        }

        /// <summary>
        /// Sit in the seat: the anchor (the seated EYES) and the seat "up" arrive in
        /// render space (the anchor transform) and are converted once here into the
        /// double world via the site frame — the same basis the player supports use.
        /// After that the pose rides the SHIP POINT (StepPlayer) rather than being
        /// recomputed from the render seat position (which jitters with the contact-zone
        /// physics). The seat can be in any mode — seating outranks the mode step
        /// (walking/falling freeze).
        /// </summary>
        public void SeatPlayerAtRender(Vector3 seatEyeRender, Vector3 seatUpRender, double eyeHeight)
        {
            Vector3 upRender = seatUpRender.sqrMagnitude > 1e-8f ? seatUpRender.normalized : Vector3.up;
            Vector3 feetRender = seatEyeRender - (upRender * (float)eyeHeight);

            RenderToAstro(feetRender, out Vector3d feet);

            if (!PlayerSeated)
            {
                PlayerSeated = true;
                platformCarry.Reset();
                PlayerVelocity = Ship != null ? Ship.Velocity : PlayerVelocity;
                playerAirborne = false;
                JetpackActive = false;
                PlayerHoverActive = false;
            }

            PlayerSeatedPosition = feet;
            PlayerSeatedUpRender = upRender;
            seatedShipAnchor = Ship != null ? Ship.Position : Vector3d.Zero;
            smoothedShipPosition = seatedShipAnchor;
        }

        /// <summary>
        /// Stand up: the support point next to the seat (render space). In a vessel the player
        /// goes straight into cabin mode: their state is stored in the part's frame, so the
        /// first frame cannot lose the floor whatever the vessel is doing.
        /// </summary>
        public void UnseatPlayerAtRender(Vector3 feetRender, Transform platform = null)
        {
            PlayerSeated = false;
            playerAirborne = false;
            JetpackActive = false;
            PlayerHoverActive = false;

            if (platform != null && Ship != null && !NoclipActive && !BuildMode)
            {
                PlayerMode = PlayerMode.OnSurface;
                platformCarry.Attach(platform, feetRender, Vector3d.Zero);
                playerController.SetGroundSource(VesselSupport.SourceIdBase);
                SyncWorldFromCabin(TimeSeconds);
                return;
            }

            RenderToAstro(feetRender, out Vector3d feet);
            PlayerPosition = feet;
            PlayerVelocity = Ship != null ? Ship.Velocity : PlayerVelocity;
        }

        /// <summary>Ship point at seating time: the "seated, riding the ship" deltas are measured against it.</summary>
        private Vector3d seatedShipAnchor;

        /// <summary>Smoothed ship point for the seated player: the zone writes the point from physics, and without smoothing its jitter goes straight into the anchor.</summary>
        private Vector3d smoothedShipPosition;

        /// <summary>
        /// Render-space point > double world. In the contact zone the assembly and
        /// terrain are drawn in SITE-FRAME AXES (the zone root is rotated by the
        /// frame), so the conversion goes through the same frame and basis as the
        /// player supports: the "flat" FloatingOrigin bridge errs the more the farther
        /// the point is from the frame origin (at the seat that is ~40 m to the rocket's
        /// bottom — tens of metres off). Outside the zone (n-body flight) — the flat bridge.
        /// </summary>
        private void RenderToAstro(Vector3 renderPoint, out Vector3d astroPoint)
        {
            if (TryPlayerFrame(out SiteFrame frame, out Vector3 renderOrigin, out Vector3 east, out Vector3 north, out Vector3 up))
            {
                Vector3 rel = renderPoint - renderOrigin;
                var local = new Vector3d(
                    Vector3.Dot(rel, east),
                    Vector3.Dot(rel, north),
                    Vector3.Dot(rel, up));
                frame.ToWorld(local, Vector3d.Zero, out astroPoint, out _);
                return;
            }

            astroPoint = FloatingOrigin.Anchor + AstroFrame.ToAstro(renderPoint);
        }

        /// <summary>
        /// The site frame and its render basis — exactly as player walking builds them
        /// (StepPlayerSurface + MeshSupportBase.SetFrame). false — no frame (no body/zone):
        /// the world is drawn with the "flat" bridge.
        /// </summary>
        private bool TryPlayerFrame(out SiteFrame frame, out Vector3 renderOrigin,
            out Vector3 east, out Vector3 north, out Vector3 up)
        {
            frame = default;
            renderOrigin = Vector3.zero;
            east = Vector3.right;
            north = Vector3.forward;
            up = Vector3.up;

            OrbitingBody body = DominantBody;
            if (!ContactZoneHost.Active || body == null)
            {
                return false;
            }

            ITerrainModel terrain = body.Terrain ?? playerFallbackTerrain;
            frame = SiteFrame.Anchor(body, terrain, PlayerPosition, TimeSeconds);
            renderOrigin = FloatingOrigin.ToRender(FrameToWorld(frame, Vector3d.Zero));
            east = FrameRenderDirection(frame, new Vector3d(1d, 0d, 0d));
            north = FrameRenderDirection(frame, new Vector3d(0d, 1d, 0d));
            up = FrameRenderDirection(frame, new Vector3d(0d, 0d, 1d));
            return true;
        }

        /// <summary>Local frame point in body axes.</summary>
        private static Vector3d FrameToWorld(SiteFrame frame, Vector3d local)
        {
            frame.ToWorld(local, Vector3d.Zero, out Vector3d world, out _);
            return world;
        }

        /// <summary>Local frame direction (north/zenith/east — metres) in render space.</summary>
        private static Vector3 FrameRenderDirection(SiteFrame frame, Vector3d localDirection)
        {
            Vector3 origin = FloatingOrigin.ToRender(FrameToWorld(frame, Vector3d.Zero));
            Vector3 tip = FloatingOrigin.ToRender(FrameToWorld(frame, localDirection));
            return (tip - origin).normalized;
        }

        /// <summary>
        /// Enter the ship: EVA/OnSurface and distance to the ship ≤ EnterDistance.
        /// No entry in noclip: the player is in free flight, and a forced seat would
        /// yank the camera out of the player's control.
        /// </summary>
        public bool TryEnterShip(double enterDistanceMeters)
        {
            if (PlayerMode == PlayerMode.InShip || Ship == null || NoclipActive || platformCarry.Active)
            {
                return false;
            }

            // A crewed build is boarded through its seat (PlayerController.TryBoardNearestSeat).
            // The old InShip path teleported to the ship point INSIDE the hull: no
            // collision, no seat, and the camera looked through the single-sided shell.
            // An uncrewed test stack (no seat) keeps the legacy shortcut.
            if (Flight != null && HasBuiltVesselSeat())
            {
                return false;
            }

            if ((PlayerPosition - Ship.Position).Magnitude > enterDistanceMeters)
            {
                return false;
            }

            PlayerMode = PlayerMode.InShip;
            PlayerPosition = Ship.Position;
            PlayerVelocity = Ship.Velocity;
            playerAirborne = false;
            JetpackActive = false;
            PlayerHoverActive = false;
            return true;
        }

        /// <summary>A real vessel part carries at least one cockpit seat (a preview/ghost has no VesselPartCollider).</summary>
        private static bool HasBuiltVesselSeat()
        {
            CockpitSeat[] seats = FindObjectsByType<CockpitSeat>(FindObjectsInactive.Exclude);
            for (int i = 0; i < seats.Length; i++)
            {
                CockpitSeat seat = seats[i];
                if (seat != null && seat.GetComponentInParent<Galilego.Building.VesselPartCollider>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Per-frame player step. While the player is outside the ship, physical time is
        /// capped (see StepFlying/StepLanded: warp factor ≤ 4), so a substep ≤ 0.5 s
        /// always fits a frame; long warp only while the player is in the ship.
        /// </summary>
        private void StepPlayer(double fromTime)
        {
            playerJumpQueued |= PlayerIntent.Jump;
            jetpackToggleQueued |= PlayerIntent.JetpackToggle;
            hoverToggleQueued |= PlayerIntent.HoverToggle;
            PlayerIntent.Jump = false;
            PlayerIntent.JetpackToggle = false;
            PlayerIntent.HoverToggle = false;

            if (jetpackToggleQueued)
            {
                jetpackToggleQueued = false;
                ToggleJetpack();
            }

            if (hoverToggleQueued)
            {
                hoverToggleQueued = false;
                ToggleHover();
            }

            UpdateShipAcceleration();
            UpdateCabinKinematics(fromTime);

            // EVA offset at the START of the frame: the player is still at the previous
            // end point while Ship.Position is already at the end of this frame. Using
            // PlayerPosition - Ship.Position for the ω×r term would add ω × (ship
            // displacement over the frame) — with 5.7 km/s orbital speed and ~3°/s turn
            // that is a spurious several m/s radial velocity, felt as "flying up".
            frameEvaOffset = PlayerPosition - frameShipStartPosition;

            double t = fromTime;
            bool jump = playerJumpQueued;
            while (t < TimeSeconds - 1e-12)
            {
                double dt = Math.Min(PlayerMaxStepSeconds, TimeSeconds - t);
                if (NoclipActive)
                {
                    evaRelativeValid = false;
                    StepPlayerNoclip(t, dt);
                    t += dt;
                    continue;
                }

                if (PlayerSeated)
                {
                    evaRelativeValid = false;
                    // Seated: no gravity, no walking. We ride the OFFICIAL ship point
                    // — but SMOOTHED: the contact zone writes it from physics and it can
                    // jitter frame to frame. This is only the render anchor; the visible
                    // camera does NOT depend on it — PlayerView places it at the drawn
                    // seat anchor, so lag/jitter here does not shake the picture.
                    // The point is predicted by ship velocity and only the residual is
                    // filtered, which damps physics jitter without a steady lag.
                    double leadSeconds = (t + dt) - fromTime;
                    Vector3d shipTarget = Ship.Position + (Ship.Velocity * leadSeconds);
                    Vector3d predicted = smoothedShipPosition + (Ship.Velocity * dt);
                    smoothedShipPosition = predicted + ((shipTarget - predicted) * 0.2d);
                    PlayerSeatedPosition += smoothedShipPosition - seatedShipAnchor;
                    seatedShipAnchor = smoothedShipPosition;
                    PlayerPosition = PlayerSeatedPosition;
                    PlayerVelocity = TryZoneVesselVelocityAt(PlayerPosition, t + dt, out Vector3d seatVelocity)
                        ? seatVelocity
                        : (Ship.PositionIsCom ? VesselVelocityAt(PlayerPosition) : Ship.Velocity);
                }
                else
                {
                    switch (PlayerMode)
                    {
                        case PlayerMode.InShip:
                            evaRelativeValid = false;
                            PlayerPosition = Ship.Position;
                            PlayerVelocity = Ship.Velocity;
                            break;
                        case PlayerMode.EVA:
                            StepPlayerEva(t, dt);
                            break;
                        case PlayerMode.OnSurface:
                            evaRelativeValid = false;
                            StepPlayerSurface(t, dt, jump);
                            playerJumpQueued = false;
                            jump = false;
                            break;
                        case PlayerMode.Swimming:
                            evaRelativeValid = false;
                            StepPlayerSwimming(t, dt);
                            break;
                    }
                }

                t += dt;
            }

            if (PlayerMode != PlayerMode.OnSurface)
            {
                playerJumpQueued = false;
            }
        }

        private void ToggleJetpack()
        {
            if (PlayerMode != PlayerMode.EVA && PlayerMode != PlayerMode.OnSurface)
            {
                return;
            }

            bool launchFromGround = PlayerMode == PlayerMode.OnSurface && !playerAirborne;
            JetpackActive = !JetpackActive;
            if (JetpackActive)
            {
                PlayerHoverActive = false;      // flight modes are mutually exclusive
            }

            if (!launchFromGround || !JetpackActive)
            {
                return;
            }

            playerAirborne = true;
            Vector3d normal = new Vector3d(0d, 0d, 1d);
            OrbitingBody body = DominantBody;
            if (body != null)
            {
                body.EvaluateWorldState(TimeSeconds, out Vector3d bodyPos, out _);
                Vector3d radial = PlayerPosition - bodyPos;
                if (radial.SqrMagnitude > 1e-12d)
                {
                    normal = radial.Normalized;
                }

                if (body.Terrain != null)
                {
                    body.SurfaceLatLonAt(PlayerPosition, TimeSeconds, out double latDeg, out double lonDeg);
                    Vector3d terrainNormal = body.Terrain.GetOutwardNormal(body, radial, TimeSeconds).Normalized;
                    if (terrainNormal.SqrMagnitude > 1e-12d)
                    {
                        normal = terrainNormal;
                    }
                }
            }

            PlayerVelocity += normal * PlayerJumpSpeed;
        }

        /// <summary>
        /// Toggle hover (double-tap Shift in flight). Hovering is possible in EVA and
        /// in the air above the surface; on the ground/in water/in the ship the toggle
        /// is ignored. Hover and jetpack are mutually exclusive.
        /// </summary>
        private void ToggleHover()
        {
            bool canHover = PlayerMode == PlayerMode.EVA
                || (PlayerMode == PlayerMode.OnSurface && playerAirborne);
            if (!canHover)
            {
                return;
            }

            PlayerHoverActive = !PlayerHoverActive;
            if (PlayerHoverActive)
            {
                // Flight modes are mutually exclusive, but remember the jetpack: leaving
                // hover must not silently leave the player with NO vertical control at all.
                jetpackBeforeHover = JetpackActive;
                JetpackActive = false;
            }
            else
            {
                JetpackActive = jetpackBeforeHover;
            }
        }

        /// <summary>
        /// EVA hover step: no gravity, the velocity relative to the local frame moves
        /// towards the desired one (input), and to zero with no input (stops in air).
        /// The frame is the vessel: "stop" must mean beside the rocket, not zero
        /// orbital velocity relative to the planet (which would leave it behind).
        /// </summary>
        private void StepPlayerHoverEva(double t, double dt)
        {
            Vector3d frameVelocity = EvaFrameVelocity(t);

            // Same stateful relative velocity as normal EVA: deriving it again from
            // PlayerVelocity - frameVelocity would make hover lag the ship's acceleration
            // (≈a/response) and drift radially in orbit, where gravity is perpendicular
            // to the motion. Hover is a velocity command relative to the vessel.
            if (!evaRelativeValid)
            {
                evaRelativeVelocity = PlayerVelocity - frameVelocity;
                evaRelativeValid = true;
            }

            Vector3d relative = evaRelativeVelocity;
            double blend = Math.Min(1d, dt * PlayerHoverResponse);
            relative += (PlayerIntent.HoverVelocity - relative) * blend;
            evaRelativeVelocity = relative;
            PlayerVelocity = frameVelocity + relative;
            PlayerPosition += PlayerVelocity * dt;
        }

        /// <summary>
        /// Noclip step for a substep: the player flies at a speed RELATIVE TO THE
        /// LOCAL FRAME — like walking, swimming and jetpack flight. No gravity,
        /// no terrain landing, no water and no decor collisions — that is the "cheat".
        /// The frame velocity (orbital + body spin) is essential, or a player
        /// standing on the surface would stay put in the inertial frame.
        /// </summary>
        private void StepPlayerNoclip(double t, double dt)
        {
            Vector3d direction = PlayerIntent.NoclipDirection;
            double speed = PlayerIntent.NoclipSpeed;
            PlayerVelocity = LocalFrameVelocity(t) + (direction * speed);
            PlayerPosition += PlayerVelocity * dt;
            playerAirborne = true;
            JetpackActive = false;
            PlayerHoverActive = false;
            playerJumpQueued = false;
        }

        /// <summary>EVA station-keeping radius (m): inside it the reference is the vessel's rigid-body velocity at the player's point.</summary>
        private const double VesselEvaFrameRadiusMeters = 100d;

        /// <summary>
        /// EVA reference velocity: the vessel's own rigid-body velocity at the player —
        /// linear plus ω×r. Stepping out must not change the player's velocity, and the
        /// craft (engine off) must stay beside them; the planet frame would brake the
        /// shared orbital velocity.
        ///
        /// The player step runs AFTER the whole ship step, so both Ship.Position and
        /// Ship.Velocity are already at the END of the frame. Using the end velocity for
        /// the whole frame made the player cover more distance than the craft by roughly
        /// half the ship acceleration × frame² — a steady drift in a fixed direction that
        /// no amount of braking could remove (worse at low FPS). The frame's ACTUAL
        /// displacement and attitude delta are the ground truth: the average velocity
        /// below moves the player exactly by the craft's displacement over the frame.
        /// </summary>
        private Vector3d EvaFrameVelocity(double t)
        {
            if (Ship == null)
            {
                return LocalFrameVelocity(t);
            }

            if (ContactZoneHost.Active
                && (PlayerPosition - Ship.Position).SqrMagnitude <= VesselEvaFrameRadiusMeters * VesselEvaFrameRadiusMeters
                && TryZoneVesselVelocityAt(PlayerPosition, t, out Vector3d zoneVelocityAtPlayer))
            {
                // Zone: exact PhysX rigid-body velocity at the player's point (linear + ω×r).
                return zoneVelocityAtPlayer;
            }

            if (ContactZoneHost.Active)
            {
                // Player far from the vessel inside the zone: PhysX linear velocity.
                return Ship.Velocity;
            }

            double span = TimeSeconds - frameShipStartTime;
            if (span > 1e-6d)
            {
                Vector3d average = (Ship.Position - frameShipStartPosition) * (1d / span);
                // Use the PHYSICAL body rate, not an attitude-delta average. The visual
                // attitude delta (Ship.Attitude over the frame) is not a reliable rate in
                // this scene: the probe measured a delta equal to the rate per SECOND
                // divided by a frame of ~10 ms, i.e. a 100x oversized omega, which flung
                // the player away. The body rate is what the hull rigid body actually
                // rotates with.
                Vector3d omega = Ship.PositionIsCom
                    ? Ship.Attitude.Rotate(Ship.AngularVelocity)
                    : Vector3d.Zero;
                return average + Vector3d.Cross(omega, frameEvaOffset);
            }

            // Called outside a stepped frame (e.g. a test): fall back to the instantaneous value.
            return VesselVelocityAt(PlayerPosition);
        }

        /// <summary>
        /// Contact zone: EXACT rigid-body velocity (world, inertial) of the vessel at an astro point,
        /// from PhysX (linear + ω×r). false — no zone.
        /// </summary>
        private bool TryZoneVesselVelocityAt(Vector3d astroPosition, double t, out Vector3d velocity)
        {
            velocity = Vector3d.Zero;
            ContactZoneHost host = CarryZone();
            SiteFrame f = host != null ? host.ZoneFrameAt(t) : null;
            if (f == null)
            {
                return false;
            }

            f.ToLocal(astroPosition, Vector3d.Zero, out Vector3d lp, out _);
            Transform zone = host.Proxy.transform;
            // Site (east, north, up) > zone axes (north, up, east).
            Vector3 zonePoint = new Vector3((float)lp.Y, (float)lp.Z, (float)lp.X);
            if (!host.Proxy.TryGetPointVelocity(null, zone.TransformPoint(zonePoint), out Vector3 worldVelocity))
            {
                return false;
            }

            Vector3 vz = zone.InverseTransformDirection(worldVelocity);
            f.ToWorld(lp, new Vector3d(vz.z, vz.x, vz.y), out _, out velocity);
            return true;
        }

        /// <summary>Rigid-body velocity of the vessel at an astro point (linear + ω×r).</summary>
        private Vector3d VesselVelocityAt(Vector3d position)
        {
            if (Ship == null)
            {
                return Vector3d.Zero;
            }

            Vector3d omega = Ship.Attitude.Rotate(Ship.AngularVelocity);
            return Ship.Velocity + Vector3d.Cross(omega, position - Ship.Position);
        }

        /// <summary>
        /// Local frame velocity at the player point: the body's orbital velocity plus
        /// its own spin (the same as GetSurfaceState gives for the surface — walking
        /// velocity comes from there too).
        /// </summary>
        private Vector3d LocalFrameVelocity(double t)
        {
            OrbitingBody body = DominantBody;
            if (body == null)
            {
                return Vector3d.Zero;
            }

            body.EvaluateWorldState(t, out Vector3d bodyPos, out Vector3d bodyVel);
            Vector3d omega = body.SpinAxis * body.SpinAngularSpeed;
            return bodyVel + Vector3d.Cross(omega, PlayerPosition - bodyPos);
        }

        /// <summary>
        /// Player velocity relative to the local frame — the "speedometer". The inertial
        /// speed when running on the surface equals the planet's orbital speed (~30 km/s
        /// for Terra), so the readings use the difference.
        /// </summary>
        public double PlayerFrameRelativeSpeed(double t)
        {
            return (PlayerVelocity - LocalFrameVelocity(t)).Magnitude;
        }

        /// <summary>
        /// Near the ground and slow — EVA flight has no collisions at all (only the planet
        /// radius), so it must go through the surface controller instead.
        /// </summary>
        private bool ShouldUseSurfaceController(OrbitingBody body, double t)
        {
            if (body == null)
            {
                return false;
            }

            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            body.SurfaceLatLonAt(PlayerPosition, t, out double latDeg, out double lonDeg);
            double altitude = (PlayerPosition - bodyPos).Magnitude - body.Radius - GroundHeight(body, latDeg, lonDeg);
            return altitude < SurfaceControllerAltitudeMeters
                && PlayerFrameRelativeSpeed(t) < SurfaceControllerMaxRelativeSpeedMps;
        }

        /// <summary>Vessel acceleration from the velocity change between two player steps.</summary>
        private void UpdateShipAcceleration()
        {
            if (Ship == null)
            {
                shipAcceleration = Vector3d.Zero;
                shipAccelerationValid = false;
                return;
            }

            double now = TimeSeconds;
            if (shipAccelerationValid)
            {
                double dt = now - lastShipAccelerationTime;
                if (dt > 1e-6d && dt < 0.5d)
                {
                    Vector3d raw = (Ship.Velocity - lastShipVelocity) * (1d / dt);
                    double magnitude = raw.Magnitude;
                    if (magnitude > ShipAccelerationClampMps2)
                    {
                        // A state rewrite (zone entry/exit, staging), not a real acceleration.
                        raw = raw * (ShipAccelerationClampMps2 / magnitude);
                    }

                    shipAcceleration += (raw - shipAcceleration) * Math.Min(1d, dt / ShipAccelerationFilterSeconds);
                }
                else if (dt >= 0.5d)
                {
                    shipAcceleration = Vector3d.Zero;
                }
            }

            lastShipVelocity = Ship.Velocity;
            lastShipAccelerationTime = now;
            shipAccelerationValid = true;
        }

        /// <summary>Put the vessel root where ShipView will put it in LateUpdate (same formula).</summary>
        private void SyncVesselRoot()
        {
            if (VesselRoot == null || Ship == null)
            {
                return;
            }

            if (TryGetFlightVesselRootPose(out Vector3 position, out Quaternion rotation))
            {
                VesselRoot.SetPositionAndRotation(position, rotation);
            }
            else
            {
                VesselRoot.position = FloatingOrigin.ToRender(Ship.Position);
            }

            Physics.SyncTransforms();
        }

        /// <summary>
        /// Render pose of the vessel root in FREE FLIGHT. There Ship.Position is the
        /// CENTER OF MASS, while the visual root's origin is the craft's bottom pivot:
        /// the root is shifted back along the attitude by Flight.Com, and the attitude
        /// itself becomes the root rotation. false — the zone/build owns the root pose
        /// (there the ship point is the bottom and the attitude is stale).
        /// </summary>
        public bool TryGetFlightVesselRootPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (ContactZoneHost.Active || Ship == null || !Ship.PositionIsCom || Flight == null)
            {
                return false;
            }

            QuaternionD attitude = Ship.Attitude.Normalized;
            // Body axes = the vessel's build axes (Y along the rocket): bridge only the world side.
            rotation = AstroFrame.SimulationQuaternion(AstroFrame.BodyToSimulationFrame(attitude));
            Vector3d root = Ship.Position - attitude.Rotate(Flight.Com);
            position = FloatingOrigin.ToRender(root);
            return true;
        }

        /// <summary>The player may live in a vessel's frame at all.</summary>
        private bool CarryAllowed()
        {
            return PlayerMode == PlayerMode.OnSurface && !NoclipActive && !PlayerSeated && !BuildMode;
        }

        /// <summary>Contact zone (the vessel is PhysX bodies); null — the vessel is in free flight.</summary>
        private ContactZoneHost CarryZone()
        {
            if (!ContactZoneHost.Active)
            {
                return null;
            }

            if (zoneHost == null)
            {
                zoneHost = FindAnyObjectByType<ContactZoneHost>();
            }

            return zoneHost != null && zoneHost.Proxy != null ? zoneHost : null;
        }

        /// <summary>Once per frame: the vessel's own velocity/acceleration (zone) before the cabin step.</summary>
        private void UpdateCabinKinematics(double poseTime)
        {
            if (!platformCarry.Active)
            {
                return;
            }

            ContactZoneHost host = CarryZone();
            if (host != null)
            {
                platformCarry.UpdateZoneKinematics(host.Proxy.transform, poseTime);
            }
            else
            {
                platformCarry.ResetKinematics();
            }
        }

        /// <summary>
        /// Weight along the cabin's up: gravity minus the vessel's acceleration. Thrust presses
        /// the player to the floor, a pad holding the vessel gives ordinary weight, free fall
        /// is weightless. Zone: render-space effective gravity of the zone minus the platform's
        /// measured acceleration. Free flight: gravity minus the vessel's inertial acceleration.
        /// </summary>
        private double CabinGravity(Transform root, double t)
        {
            Vector3 effective;
            ContactZoneHost host = CarryZone();
            if (host != null)
            {
                effective = Physics.gravity - host.Proxy.transform.TransformDirection(platformCarry.PlatformAccelerationZone);
            }
            else
            {
                Vector3d g = SystemState.EvaluateShipAcceleration(PlayerPosition, t) - shipAcceleration;
                effective = AstroFrame.ToSimulation(g);
            }

            PlayerPlatformCarry.Axes(root, out _, out _, out Vector3 up);
            double weight = Math.Max(0d, -(double)Vector3.Dot(effective, up));

            // Free flight: the weight is a DIFFERENCE of two estimates (gravity minus the filtered
            // vessel acceleration), so in orbit it is never exactly zero. A few cm/s² were enough
            // to slowly "land" a player drifting out of the hatch onto the hull below.
            if (host == null && weight < CabinWeightlessDeadbandMps2)
            {
                return 0d;
            }

            return weight;
        }

        /// <summary>A world-axes intent vector (built from the camera) as controller axes of the platform, horizontal only.</summary>
        private static Vector3d CabinIntent(Vector3d worldIntent, Transform root)
        {
            Vector3d local = PlayerPlatformCarry.LocalDirection(root, AstroFrame.ToSimulation(worldIntent));
            return new Vector3d(local.X, local.Y, 0d);
        }

        /// <summary>
        /// Derive the double-world player state from the cabin state: position through the bridge
        /// that matches how the vessel itself is placed (zone: body-fixed zone coordinates and the
        /// zone frame; free flight: the flat bridge), velocity = vessel + relative.
        /// </summary>
        private void SyncWorldFromCabin(double tEnd)
        {
            if (!platformCarry.Active || Ship == null)
            {
                return;
            }

            Vector3 render = platformCarry.RenderPoint();
            Vector3 relative = platformCarry.RenderDirection(platformCarry.Velocity);
            ContactZoneHost host = CarryZone();
            SiteFrame zoneFrame = host != null ? host.ZoneFrameAt(tEnd) : null;
            if (zoneFrame != null)
            {
                Transform zone = host.Proxy.transform;
                Vector3 zonePoint = zone.InverseTransformPoint(render);
                // STRICT: the platform's velocity is the PhysX rigid-body velocity of the player's point
                // (linear + ω×r), not a finite difference of the (smoothed, fixed-step) visual pose.
                Vector3 platformVelocityZone = platformCarry.PlatformVelocityZone;
                if (host.Proxy.TryGetPointVelocity(platformCarry.Platform, render, out Vector3 pointVelocity))
                {
                    platformVelocityZone = zone.InverseTransformDirection(pointVelocity);
                }

                Vector3 zoneVelocity = zone.InverseTransformDirection(relative) + platformVelocityZone;

                // Zone axes (x north, y up, z east) > site (east, north, up).
                zoneFrame.ToWorld(new Vector3d(zonePoint.z, zonePoint.x, zonePoint.y),
                    new Vector3d(zoneVelocity.z, zoneVelocity.x, zoneVelocity.y),
                    out Vector3d worldPosition, out Vector3d worldVelocity);
                PlayerPosition = worldPosition;
                PlayerVelocity = worldVelocity;
                return;
            }

            PlayerPosition = FloatingOrigin.Anchor + AstroFrame.ToAstro(render);
            PlayerVelocity = VesselVelocityAt(PlayerPosition) + AstroFrame.ToAstro(relative);
        }

        /// <summary>Leave cabin mode: the world state is derived one last time, free flight becomes EVA.</summary>
        private void DetachCabin()
        {
            if (!platformCarry.Active)
            {
                return;
            }

            SyncWorldFromCabin(TimeSeconds);

            // Leaving the cabin: both EVA axes START as the cabin's up. The camera/input axis
            // then stays inertial (a spinning vessel must not turn the view); the collision
            // axis follows the capsule as UpdateEvaUp decides.
            if (!ContactZoneHost.Active && platformCarry.TryGetUp(out Vector3 cabinUp) && cabinUp.sqrMagnitude > 1e-8f)
            {
                Vector3d cabinUpAstro = AstroFrame.ToAstro(cabinUp.normalized).Normalized;
                evaUpAstro = cabinUpAstro;
                evaCollisionUpAstro = cabinUpAstro;
                evaUpValid = true;
                evaCollisionUpValid = true;
                evaAlignedToVessel = true;
            }

            platformCarry.Reset();
            OrbitingBody body = DominantBody;
            if (body != null && PlayerMode == PlayerMode.OnSurface && !ShouldUseSurfaceController(body, TimeSeconds))
            {
                PlayerMode = PlayerMode.EVA;
                playerAirborne = true;
                evaCollisionGraceUntil = TimeSeconds + 2.0d;
            }
        }

        /// <summary>One substep of the player living in the vessel's frame.</summary>
        private void StepPlayerCabin(double t, double dt, bool jumpRequested)
        {
            Transform root = platformCarry.Platform;
            vesselSupport.SetPlatformFrame(root);
            siteSupport.SetPlatformFrame(root);
            patchSupport.SetPlatformFrame(root);
            cabinSupport.Terrain = patchSupport;
            cabinSupport.Sites = siteSupport;
            cabinSupport.Vessel = vesselSupport;

            playerController.Position = platformCarry.Position;
            playerController.Velocity = platformCarry.Velocity;
            playerController.Airborne = playerAirborne;
            playerController.Gravity = CabinGravity(root, t);
            playerController.Hovering = false;
            playerController.HoverVelocity = Vector3d.Zero;

            Vector3d walkLocal = CabinIntent(PlayerIntent.WalkDirection * PlayerIntent.WalkSpeed, root);
            Vector3d airLocal = CabinIntent(PlayerIntent.AirVelocity, root);
            playerController.Step(cabinSupport, dt, playerAirborne ? airLocal : walkLocal, Vector3d.Zero);

            platformCarry.Position = playerController.Position;
            platformCarry.Velocity = playerController.Velocity;
            playerAirborne = playerController.Airborne;
            PlayerHoverActive = false;

            if (jumpRequested && !playerAirborne)
            {
                platformCarry.Velocity += new Vector3d(0d, 0d, PlayerJumpSpeed);
                playerAirborne = true;
            }

            // Which part carries the player now: the floor under them (a part of the vessel),
            // or — in the air — the part whose bounds they are still inside. Anything else
            // (terrain, a building, open space) ends the cabin mode.
            Vector3 render = platformCarry.RenderPoint();
            Transform next = null;
            if (!playerAirborne)
            {
                int id = playerController.GroundSourceId;
                if (id >= VesselSupport.SourceIdBase)
                {
                    vesselSupport.TryGetPlatform(id, out next);
                }
            }
            else if (platformCarry.InsideBounds(render))
            {
                next = platformCarry.Platform;
            }

            if (next == null)
            {
                DetachCabin();
                return;
            }

            platformCarry.Reframe(next);
            SyncWorldFromCabin(t + dt);
        }

        /// <summary>
        /// Surface mode, end of a substep: the player stands on a vessel part — from now on they
        /// live in its frame. The point is taken where the controller believes the player is, in
        /// the very space the colliders live in.
        /// </summary>
        private void TryAttachCabin()
        {
            if (platformCarry.Active || playerAirborne || !CarryAllowed())
            {
                return;
            }

            int id = playerController.GroundSourceId;
            if (id < VesselSupport.SourceIdBase || !vesselSupport.TryGetPlatform(id, out Transform platform))
            {
                return;
            }

            Vector3 point = vesselSupport.RenderPointOf(playerController.Position);
            Vector3 velocity = vesselSupport.RenderDirectionOf(playerController.Velocity);
            platformCarry.Attach(platform, point, PlayerPlatformCarry.LocalDirection(platform, velocity));
            PlayerHoverActive = false;
        }

        private void StepPlayerEva(double t, double dt)
        {
            if (ShouldUseSurfaceController(DominantBody, t))
            {
                PlayerMode = PlayerMode.OnSurface;
                playerAirborne = true;
                evaRelativeValid = false;
                StepPlayerSurface(t, dt, false);
                return;
            }

            UpdateEvaCrouch(t);

            OrbitingBody body = DominantBody;
            Vector3d up = UpdateEvaUp(body, t, dt);

            Vector3d previousPosition = PlayerPosition;
            if (PlayerHoverActive)
            {
                StepPlayerHoverEva(t, dt);
            }
            else
            {
                Vector3d jetpackAccel = JetpackActive ? PlayerIntent.JetpackAccel : Vector3d.Zero;
                Vector3d frameVelocity = EvaFrameVelocity(t);

                // Relative velocity is STATE, not a per-step difference (see the field
                // comment). The vessel frame already carries gravity and thrust; the
                // player's own relative velocity must not absorb the frame's acceleration.
                // With no jetpack input BOTH axes brake to the vessel (the old code left
                // the vertical component untouched, so an exit push or one jetpack tap
                // drifted forever).
                if (!evaRelativeValid)
                {
                    evaRelativeVelocity = PlayerVelocity - frameVelocity;
                    evaRelativeValid = true;
                }

                Vector3d relative = evaRelativeVelocity;
                double verticalSpeed = Vector3d.Dot(relative, up);
                Vector3d horizontal = relative - (up * verticalSpeed);
                horizontal += (PlayerIntent.AirVelocity - horizontal)
                    * Math.Min(1d, dt * PlayerAirResponse);

                if (jetpackAccel.SqrMagnitude <= 1e-12d)
                {
                    // No vertical input: station-keeping, exactly like the horizontal.
                    verticalSpeed += (0d - verticalSpeed) * Math.Min(1d, dt * PlayerAirResponse);
                }

                evaRelativeVelocity = horizontal + (up * verticalSpeed) + (jetpackAccel * dt);
                PlayerVelocity = frameVelocity + evaRelativeVelocity;
                PlayerPosition += PlayerVelocity * dt;
            }

            // The hull is solid in EVA too (walking support only worked on the surface
            // controller). Without this the player flew through the capsule, the camera
            // went inside the single-sided shell ("the rocket turned transparent").
            // The invisible body lines up with the hatch; the camera/input axis does not.
            Vector3d collisionUp = evaCollisionUpValid ? evaCollisionUpAstro : up;
            ResolveEvaHullCollision(previousPosition, collisionUp, t, dt);

            if (body == null)
            {
                return;
            }

            // The position is already at t+dt — check contact and land at t+dt, not t.
            double tEnd = t + dt;
            body.EvaluateWorldState(tEnd, out Vector3d bodyPosEnd, out _);
            body.SurfaceLatLonAt(PlayerPosition, tEnd, out double latDeg, out double lonDeg);
            double terrainRadius = body.Radius + GroundHeight(body, latDeg, lonDeg);
            if ((PlayerPosition - bodyPosEnd).Magnitude <= terrainRadius)
            {
                LandPlayer(body, tEnd);
            }
        }

        /// <summary>Planet vertical at the player (radial from the dominant body's centre), astro axes.</summary>
        private Vector3d RadialUpAstro(OrbitingBody body, double t)
        {
            if (body != null)
            {
                body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
                Vector3d radial = PlayerPosition - bodyPos;
                if (radial.SqrMagnitude > 1e-12d)
                {
                    return radial.Normalized;
                }
            }

            return new Vector3d(0d, 0d, 1d);
        }

        /// <summary>
        /// The vessel is drawn at the END of the frame while the player is being stepped at
        /// time t: their render poses differ by the ship's motion over the REMAINING part of
        /// the frame. In low orbit that is tens of metres - enough to make "near the capsule"
        /// false every frame (the hatch crouch and the EVA vertical never engaged) and to turn
        /// the hull sweep into a spurious high-speed approach. Shift the player to the drawn
        /// ship's time before measuring anything against its colliders.
        /// </summary>
        private Vector3 FrameShipDeltaRender()
        {
            if (Ship == null)
            {
                return Vector3.zero;
            }

            return AstroFrame.ToSimulation(Ship.Position - frameShipStartPosition);
        }

        /// <summary>Fraction of the current frame still ahead at t (1 at frame start, 0 at its end).</summary>
        private double FrameRemainingFraction(double t)
        {
            double span = TimeSeconds - frameShipStartTime;
            if (span <= 1e-12d)
            {
                return 0d;
            }

            double remaining = (TimeSeconds - t) / span;
            return remaining < 0d ? 0d : (remaining > 1d ? 1d : remaining);
        }

        /// <summary>
        /// One substep of the two EVA axes (see evaUpAstro). The CAMERA/INPUT axis is inertial:
        /// inherited at the hatch, then held - a spinning vessel must not turn the player; it only
        /// eases back to the planet radial once the vessel is far. The COLLISION axis follows the
        /// capsule while near, so the invisible body can line up with the hatch.
        /// </summary>
        private Vector3d UpdateEvaUp(OrbitingBody body, double t, double dt)
        {
            Vector3d radial = RadialUpAstro(body, t);
            if (ContactZoneHost.Active)
            {
                evaUpValid = false;
                evaCollisionUpValid = false;
                return radial;
            }

            if (!evaUpValid)
            {
                evaUpAstro = radial;
                evaUpValid = true;
            }

            if (evaCapsule != null && evaCapsuleDistance < EvaAlignEnterMeters)
            {
                evaAlignedToVessel = true;
            }
            else if (evaCapsule == null || evaCapsuleDistance > EvaAlignExitMeters)
            {
                evaAlignedToVessel = false;
            }

            // Collision body: exactly the capsule's axis while near the hatch. It is invisible,
            // so following a spinning vessel here is fine - it is what lets the body pass the hatch.
            Vector3d capsuleUp = evaCapsule != null ? AstroFrame.ToAstro(evaCapsule.up) : Vector3d.Zero;
            bool haveCapsuleUp = capsuleUp.SqrMagnitude > 1e-12d;
            if (evaAlignedToVessel && haveCapsuleUp)
            {
                evaCollisionUpAstro = capsuleUp.Normalized;
                evaCollisionUpValid = true;
            }
            else if (!evaCollisionUpValid)
            {
                evaCollisionUpAstro = radial;
                evaCollisionUpValid = true;
            }
            else
            {
                double collisionK = Math.Min(1d, dt * EvaUpAlignRate);
                Vector3d blendedCollision = (evaCollisionUpAstro * (1d - collisionK)) + (radial * collisionK);
                evaCollisionUpAstro = blendedCollision.SqrMagnitude > 1e-12d
                    ? blendedCollision.Normalized
                    : radial;
            }

            // Camera/input axis: hold it while the vessel is near; ease to the planet radial only
            // when it is far. It must NEVER read the vessel's rotation - that turned the view with
            // a spinning craft.
            Vector3d target = evaCapsule != null && evaCapsuleDistance < EvaAlignExitMeters
                ? evaUpAstro
                : radial;
            double k = Math.Min(1d, dt * EvaUpAlignRate);
            Vector3d blended = (evaUpAstro * (1d - k)) + (target * k);
            evaUpAstro = blended.SqrMagnitude > 1e-12d ? blended.Normalized : target;
            return evaUpAstro;
        }

        /// <summary>
        /// The player's CAMERA/INPUT vertical in astro axes: the platform's up aboard a vessel,
        /// the inertial EVA vertical in free flight, otherwise the planet radial. The collision
        /// body has its own axis (see evaCollisionUpAstro).
        /// </summary>
        public Vector3d PlayerUpAstro
        {
            get
            {
                if (!ContactZoneHost.Active)
                {
                    if (platformCarry.Active && platformCarry.TryGetUp(out Vector3 carriedUp)
                        && carriedUp.sqrMagnitude > 1e-8f)
                    {
                        return AstroFrame.ToAstro(carriedUp.normalized).Normalized;
                    }

                    if (PlayerMode == PlayerMode.EVA && evaUpValid)
                    {
                        return evaUpAstro;
                    }
                }

                return RadialUpAstro(DominantBody, TimeSeconds);
            }
        }

        /// <summary>The EVA vertical in render space (the camera's up in free-flight EVA). false — not applicable.</summary>
        public bool TryGetEvaUpRender(out Vector3 up)
        {
            if (!evaUpValid || ContactZoneHost.Active || PlayerMode != PlayerMode.EVA)
            {
                up = Vector3.up;
                return false;
            }

            up = AstroFrame.ToSimulation(evaUpAstro).normalized;
            return true;
        }

        /// <summary>
        /// EVA auto-crouch: near the capsule the body is 0.8 m tall (the Orion hatch is
        /// ~1 m), far away it stands. Without this the full capsule hit the hatch frame
        /// and the push-out threw the player sideways instead of letting them in.
        /// The player point is shifted to the DRAWN ship's time (see FrameShipDeltaRender).
        /// </summary>
        private void UpdateEvaCrouch(double t)
        {
            if (!evaCapsuleSearched)
            {
                evaCapsuleSearched = true;
                CockpitSeat[] seats = FindObjectsByType<CockpitSeat>(FindObjectsInactive.Exclude);
                for (int i = 0; i < seats.Length; i++)
                {
                    CockpitSeat seat = seats[i];
                    Galilego.Building.VesselPartCollider part = seat != null
                        ? seat.GetComponentInParent<Galilego.Building.VesselPartCollider>()
                        : null;
                    if (part == null)
                    {
                        continue;
                    }

                    evaCapsule = part.transform;
                    evaCapsuleRenderers = evaCapsule.GetComponentsInChildren<Renderer>(true);
                    break;
                }
            }

            if (evaCapsule == null || evaCapsuleRenderers == null || evaCapsuleRenderers.Length == 0)
            {
                evaCrouched = false;
                evaCapsuleDistance = float.MaxValue;
                return;
            }

            Bounds bounds = evaCapsuleRenderers[0].bounds;
            for (int i = 1; i < evaCapsuleRenderers.Length; i++)
            {
                if (evaCapsuleRenderers[i] != null && evaCapsuleRenderers[i].enabled)
                {
                    bounds.Encapsulate(evaCapsuleRenderers[i].bounds);
                }
            }

            Vector3 playerPoint = FloatingOrigin.ToRender(PlayerPosition)
                + (FrameShipDeltaRender() * (float)FrameRemainingFraction(t));
            evaCapsuleDistance = Vector3.Distance(bounds.ClosestPoint(playerPoint), playerPoint);
            evaCrouched = evaCapsuleDistance < 1.5f;
        }

        /// <summary>Thin hull pieces must not be skipped at high relative speed (m).</summary>
        private const float EvaHullSkinMeters = 0.02f;

        /// <summary>
        /// EVA hull collision: the player capsule is swept to the new point and pushed
        /// out of vessel colliders, so the craft cannot be flown through and the camera
        /// cannot end up inside the single-sided shell. Only vessel parts count; the
        /// contact-zone proxy bodies are skipped while the zone owns the collision, and
        /// PlayerIgnore colliders (interaction boxes, monitors) are not the body.
        /// The vessel frame velocity is removed before the normal component is killed:
        /// hitting the hull must stop the approach, not the shared orbital motion.
        /// </summary>
        private void ResolveEvaHullCollision(Vector3d previousPosition, Vector3d upAstro, double t, double dt)
        {
            if (Ship == null || ContactZoneHost.Active || NoclipActive || !EvaHullCollision
                || TimeSeconds < evaCollisionGraceUntil)
            {
                return;
            }

            Vector3 up = AstroFrame.ToSimulation(upAstro).normalized;
            float radius = (float)PlayerSurfaceController.CapsuleRadius;
            float height = evaCrouched
                ? (float)PlayerSurfaceController.CapsuleHeightCrouched
                : (float)PlayerSurfaceController.CapsuleHeight;
            float skin = EvaHullSkinMeters;

            // Both points are converted through the CURRENT anchor (the frame start), so
            // the vessel colliders and the path live in the same render space. They do NOT
            // live at the same TIME: the colliders are drawn at the end of the frame, the
            // player is at t / t+dt. In low orbit the frame carries the craft tens of metres,
            // so without this shift every frame looked like a high-speed approach to the
            // hull and the sweep threw the player onto its near side (the "teleport").
            Vector3 shipDelta = FrameShipDeltaRender();
            float startShift = (float)FrameRemainingFraction(t);
            float endShift = (float)FrameRemainingFraction(t + dt);
            Vector3 previous = FloatingOrigin.ToRender(previousPosition) + (shipDelta * startShift);
            Vector3 next = FloatingOrigin.ToRender(PlayerPosition) + (shipDelta * endShift);
            Vector3 delta = next - previous;
            float travel = delta.magnitude;
            bool hit = false;

            Vector3d frameVelocity = EvaFrameVelocity(t);

            if (travel > 1e-5f)
            {
                Vector3 direction = delta / travel;
                Vector3 p1 = previous + (up * radius);
                Vector3 p2 = previous + (up * (height - radius));
                int count = Physics.CapsuleCastNonAlloc(p1, p2, radius, direction, evaHits,
                    travel + skin, ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                Vector3 nearestNormal = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    RaycastHit candidate = evaHits[i];
                    if (!IsEvaHullCollider(candidate.collider) || candidate.distance >= nearest)
                    {
                        continue;
                    }

                    nearest = candidate.distance;
                    nearestNormal = candidate.normal;
                }

                if (nearest <= travel)
                {
                    float allowed = Mathf.Max(0f, nearest - skin);
                    next = previous + (direction * allowed);
                    hit = true;
                    EvaHullCorrections++;
                    EvaHullCorrectionMeters += Mathf.Max(0f, travel - allowed);
                    CancelEvaVelocityInto(nearestNormal, frameVelocity);
                }
            }

            bool overlapFixed = ResolveEvaHullOverlap(ref next, previous, up, radius, height, frameVelocity);
            if (!hit && !overlapFixed)
            {
                return;
            }

            Vector3 unshifted = next - (shipDelta * endShift);
            PlayerPosition = FloatingOrigin.Anchor + AstroFrame.ToAstro(unshifted);
        }

        /// <summary>
        /// Resolve a START overlap: a cast ignores colliders it already touches. A vessel that is
        /// rotating can turn a hull into the body while the player is touching it, and without
        /// this the sweep never sees the contact - the player gets dragged along inside the wall.
        /// The push is biased to the side the player came from (or away from the capsule centre
        /// when there is no motion), so a thin shell cannot resolve to its inner side.
        /// </summary>
        private bool ResolveEvaHullOverlap(ref Vector3 next, Vector3 previous, Vector3 up,
            float radius, float height, Vector3d frameVelocity)
        {
            CapsuleCollider probe = EvaProbeCollider();
            if (probe == null)
            {
                return false;
            }

            float bodyHeight = Mathf.Max(height, (radius * 2f) + 0.02f);
            probe.radius = radius;
            probe.height = bodyHeight;
            Quaternion probeRotation = Quaternion.FromToRotation(Vector3.up, up);
            float half = bodyHeight * 0.5f;
            Vector3 capsuleCenter = evaCapsule != null ? evaCapsule.position : next;
            Vector3 escape = previous - next;
            bool corrected = false;

            for (int iteration = 0; iteration < 2; iteration++)
            {
                Vector3 capsuleStart = next + (up * radius);
                Vector3 capsuleEnd = next + (up * (bodyHeight - radius));
                int count = Physics.OverlapCapsuleNonAlloc(capsuleStart, capsuleEnd,
                    radius + EvaHullSkinMeters, evaOverlaps, ~0, QueryTriggerInteraction.Ignore);
                float deepest = 0f;
                Vector3 pushDirection = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    Collider hull = evaOverlaps[i];
                    if (!IsEvaHullCollider(hull))
                    {
                        continue;
                    }

                    Vector3 center = next + (up * half);
                    if (!ComputeEvaPenetration(probe, center, probeRotation, hull,
                            out Vector3 direction, out float distance)
                        || distance <= 1e-4f || direction.sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }

                    Vector3 outward = center - capsuleCenter;
                    float alongEscape = escape.sqrMagnitude > 1e-6f
                        ? Vector3.Dot(direction, escape)
                        : 0f;
                    if (escape.sqrMagnitude > 1e-6f && Mathf.Abs(alongEscape) > 0.2f)
                    {
                        // Clearly moving into (or out of) the hull: push back the way we came.
                        if (alongEscape < 0f)
                        {
                            direction = -direction;
                        }
                    }
                    else if (outward.sqrMagnitude > 1e-8f && Vector3.Dot(direction, outward) < 0f)
                    {
                        // Sliding along the hull: never push through it towards the centre.
                        direction = -direction;
                    }

                    if (distance > deepest)
                    {
                        deepest = distance;
                        pushDirection = direction;
                    }
                }

                if (deepest <= 1e-4f)
                {
                    break;
                }

                float step = Mathf.Min(deepest + EvaHullSkinMeters, 0.5f);
                next += pushDirection.normalized * step;
                CancelEvaVelocityInto(pushDirection, frameVelocity);
                EvaHullCorrections++;
                EvaHullCorrectionMeters += step;
                corrected = true;
            }

            return corrected;
        }

        /// <summary>Capsule-vs-hull penetration; the hidden trigger probe is outside the physics scene.</summary>
        private bool ComputeEvaPenetration(CapsuleCollider probe, Vector3 position, Quaternion rotation,
            Collider hull, out Vector3 direction, out float distance)
        {
            bool ok = Physics.ComputePenetration(probe, position, rotation,
                hull, hull.transform.position, hull.transform.rotation, out direction, out distance);
            if (!ok && evaProbeObject != null && !evaProbeObject.activeSelf)
            {
                evaProbeObject.SetActive(true);
                ok = Physics.ComputePenetration(probe, position, rotation,
                    hull, hull.transform.position, hull.transform.rotation, out direction, out distance);
                evaProbeObject.SetActive(false);
            }

            return ok;
        }

        /// <summary>Hidden trigger capsule used only for penetration queries (never collides).</summary>
        private CapsuleCollider EvaProbeCollider()
        {
            if (evaProbeCollider == null)
            {
                evaProbeObject = new GameObject("EVA Body Probe");
                evaProbeObject.hideFlags = HideFlags.HideAndDontSave;
                evaProbeCollider = evaProbeObject.AddComponent<CapsuleCollider>();
                evaProbeCollider.direction = 1;
                evaProbeCollider.isTrigger = true;
                evaProbeObject.SetActive(false);
            }

            return evaProbeCollider;
        }

        /// <summary>Remove the velocity component INTO a hull normal, measured against the vessel frame.</summary>
        private void CancelEvaVelocityInto(Vector3 renderNormal, Vector3d frameVelocity)
        {
            if (renderNormal.sqrMagnitude < 1e-8f)
            {
                return;
            }

            Vector3d normal = AstroFrame.ToAstro(renderNormal.normalized);
            Vector3d relative = evaRelativeValid ? evaRelativeVelocity : (PlayerVelocity - frameVelocity);
            double into = Vector3d.Dot(relative, normal);
            if (into < 0d)
            {
                relative -= normal * into;
                if (evaRelativeValid)
                {
                    evaRelativeVelocity = relative;
                }

                PlayerVelocity = frameVelocity + relative;
            }
        }

        private static bool IsEvaHullCollider(Collider collider)
        {
            if (collider == null || collider.isTrigger)
            {
                return false;
            }

            Galilego.Building.VesselPartCollider part = VesselSupport.ResolvePart(collider);
            if (part == null || (part.VisualCovered && ContactZoneHost.Active))
            {
                return false;
            }

            return collider.GetComponent<Galilego.Building.PlayerIgnore>() == null;
        }

        /// <summary>
        /// Player touching the surface: over water a splash (EnterWater), over land a
        /// projection onto the surface, the normal velocity is absorbed, the tangential
        /// one is kept. Impact severity is logged (injuries/death — a later stage).
        /// No spawn epsilon is needed — the player lands exactly on the surface.
        /// </summary>
        private void LandPlayer(OrbitingBody body, double t)
        {
            body.SurfaceLatLonAt(PlayerPosition, t, out double latDeg, out double lonDeg);
            if (WaterQuery.IsWaterAt(body, latDeg, lonDeg))
            {
                EnterWater(body, t);
                return;
            }

            body.GetSurfaceState(latDeg, lonDeg, GroundHeight(body, latDeg, lonDeg), t, out Vector3d surfacePos, out Vector3d surfaceVel);
            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            Vector3d normal = body.Terrain != null
                ? body.Terrain.GetOutwardNormal(body, PlayerPosition - bodyPos, t).Normalized
                : (PlayerPosition - bodyPos).Normalized;
            Vector3d relativeVelocity = PlayerVelocity - surfaceVel;
            double normalSpeed = Vector3d.Dot(relativeVelocity, normal);
            Vector3d tangential = relativeVelocity - (normal * normalSpeed);
            PlayerPosition = surfacePos;
            PlayerVelocity = surfaceVel + tangential;
            PlayerMode = PlayerMode.OnSurface;
            playerAirborne = false;
            if (normalSpeed < -10d)
            {
                Debug.Log("Жёсткое приземление игрока: " + (-normalSpeed).ToString("F1") + " м/с (травмы — будущий этап)");
            }
        }

        /// <summary>Clearance above the raw seabed (m): the player in water does not fall through the bottom.</summary>
        private const double SwimSeabedClearanceMeters = 0.3d;

        /// <summary>
        /// Free ascend/descend speed with no input near the surface (m/s): towards
        /// hovering at SwimSurfaceHoldMeters below the surface. Deeper than
        /// SwimNeutralDepthMeters — neutral buoyancy (hovering): dive, release the keys
        /// and you float looking up, not pushed out. Ascend/descend there is input only
        /// (Space/Ctrl or look+W/S).
        /// </summary>
        private const double SwimBuoyancySpeed = 0.6d;

        /// <summary>
        /// Foot hover depth near the surface with no input (m): the head (~2 m above the
        /// feet) stays above water, so the effect/fog does not blur while swimming.
        /// </summary>
        private const double SwimSurfaceHoldMeters = 0.4d;

        /// <summary>
        /// Foot depth (m) beyond which, with no input, it is neutral (0): the swimmer's
        /// eyes (~0.5 m above the feet — the swimmer lies flat, see SwimEyeHeightMeters)
        /// are already underwater, so the player floats looking at the surface.
        /// Must be &gt; SwimEyeHeightMeters.
        /// </summary>
        private const double SwimNeutralDepthMeters = 1d;

        /// <summary>
        /// Swimmer eye height above the feet (m): in water the body lies flat, the head
        /// ~0.5 m above the feet (not 2 m as when standing). Synchronised with
        /// FirstPersonCamera.SwimEyeHeightMeters and
        /// UnderwaterEffect.SwimEyeHeightMeters: at SwimSurfaceHoldMeters the head stays
        /// above water, at neutral it is already underwater.
        /// </summary>
        private const double SwimEyeHeightMeters = 0.5d;

        /// <summary>
        /// Water entry: the position is clamped between the surface and seabed+clearance,
        /// the velocity is damped as a splash (tangential ×0.3, incoming normal — at most
        /// 3 m/s down). The same idea will underlie future soft ship splashdown
        /// (see WaterQuery.GetSplashdownInfo).
        /// </summary>
        private void EnterWater(OrbitingBody body, double t)
        {
            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            body.SurfaceLatLonAt(PlayerPosition, t, out double latDeg, out double lonDeg);
            double sea = body.Terrain.GetSeaLevelMeters();
            double raw = WaterQuery.RawSeabedHeightAt(body, latDeg, lonDeg);
            body.GetSurfaceState(latDeg, lonDeg, sea, t, out _, out Vector3d surfaceVel);

            Vector3d radial = PlayerPosition - bodyPos;
            double dist = radial.Magnitude;
            Vector3d up = dist > 1e-9d ? radial / dist : new Vector3d(0d, 0d, 1d);
            double clampedDist = Math.Min(dist, body.Radius + sea);
            if (!double.IsNaN(raw))
            {
                clampedDist = Math.Max(clampedDist, body.Radius + raw + SwimSeabedClearanceMeters);
            }

            PlayerPosition = bodyPos + (up * clampedDist);

            Vector3d relativeVelocity = PlayerVelocity - surfaceVel;
            double normalSpeed = Vector3d.Dot(relativeVelocity, up);
            Vector3d tangential = relativeVelocity - (up * normalSpeed);
            double softNormal = normalSpeed < 0d ? Math.Max(normalSpeed * 0.2d, -3d) : 0d;
            PlayerVelocity = surfaceVel + (tangential * 0.3d) + (up * softNormal);
            PlayerMode = PlayerMode.Swimming;
            playerAirborne = false;
            JetpackActive = false;
            PlayerHoverActive = false;
            if (normalSpeed < -10d)
            {
                Debug.Log("Всплеск: вход в воду на " + (-normalSpeed).ToString("F1") + " м/с погашен водой");
            }
        }

        /// <summary>
        /// Swimming step for a substep: velocity = planet co-rotation + intent
        /// (water holds you — no ballistics); with no input near the surface it drifts
        /// to SwimSurfaceHoldMeters, and deeper than SwimNeutralDepthMeters it is neutral
        /// hover (not pushed out, can look up). Bumps against the raw seabed, clamped at
        /// the surface; jumping out of deep water goes to EVA (will fall back), reaching
        /// land goes through LandPlayer.
        /// </summary>
        private void StepPlayerSwimming(double t, double dt)
        {
            OrbitingBody body = DominantBody;
            if (body == null || body.Terrain == null || !WaterQuery.HasOcean(body))
            {
                PlayerMode = PlayerMode.EVA;
                playerAirborne = false;
                JetpackActive = false;
                PlayerHoverActive = false;
                return;
            }

            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            double sea = body.Terrain.GetSeaLevelMeters();
            double seaRadius = body.Radius + sea;
            double dist = (PlayerPosition - bodyPos).Magnitude;
            double submersion = seaRadius - dist;

            body.SurfaceLatLonAt(PlayerPosition, t, out double latDeg, out double lonDeg);
            if (!WaterQuery.IsWaterAt(body, latDeg, lonDeg))
            {
                // The current carried the player ashore: near the surface stand up, otherwise go airborne.
                if (submersion > -1d)
                {
                    LandPlayer(body, t);
                }
                else
                {
                    PlayerMode = PlayerMode.EVA;
                    playerAirborne = false;
                    JetpackActive = false;
                    PlayerHoverActive = false;
                }

                return;
            }

            Vector3d up = dist > 1e-9d ? (PlayerPosition - bodyPos) / dist : new Vector3d(0d, 0d, 1d);
            body.GetSurfaceState(latDeg, lonDeg, sea, t, out _, out Vector3d surfaceVel);

            Vector3d swimVel = PlayerIntent.SwimDirection * PlayerIntent.SwimSpeed;
            if (swimVel.SqrMagnitude < 1e-12d)
            {
                // Shallow shore water (depth does not reach neutral + seabed
                // clearance): nowhere to hover — neutral everywhere.
                double rawHere = WaterQuery.RawSeabedHeightAt(body, latDeg, lonDeg);
                double waterDepthHere = double.IsNaN(rawHere) ? double.PositiveInfinity : sea - rawHere;
                if (waterDepthHere < SwimNeutralDepthMeters + SwimSeabedClearanceMeters + 0.2d)
                {
                    swimVel = new Vector3d(0d, 0d, 0d);
                }
                else if (submersion > SwimNeutralDepthMeters)
                {
                    swimVel = new Vector3d(0d, 0d, 0d);
                }
                else
                {
                    swimVel = up * Math.Max(-SwimBuoyancySpeed, Math.Min(SwimBuoyancySpeed, submersion - SwimSurfaceHoldMeters));
                }
            }

            PlayerVelocity = surfaceVel + swimVel;

            // Orbit curvature: linear advection at co-rotation speed (~5 km/s) gives a
            // radial error v²dt²/2R, so track depth explicitly: tangential by advection,
            // radial by intent (submersion = seaRadius − dist). Walking does not need
            // this — it re-projects onto the surface, while swimming has no projection
            // (depth is state).
            double intendedSubmersion = submersion - Vector3d.Dot(swimVel, up) * dt;
            double tAfter = t + dt;
            body.EvaluateWorldState(tAfter, out Vector3d bodyPosAfter, out _);
            Vector3d advected = PlayerPosition + PlayerVelocity * dt;
            Vector3d radialAfter = advected - bodyPosAfter;
            double distAfter = radialAfter.Magnitude;
            Vector3d upAfter = distAfter > 1e-9d ? radialAfter / distAfter : up;
            PlayerPosition = bodyPosAfter + (upAfter * (seaRadius - intendedSubmersion));

            // Clamps AT THE END of the substep (same principle as walking: project at
            // toTime, otherwise the body's orbital velocity gives a bodyVel·dt error).
            double tEnd = t + dt;
            body.EvaluateWorldState(tEnd, out Vector3d bodyPosEnd, out _);
            body.SurfaceLatLonAt(PlayerPosition, tEnd, out double latEnd, out double lonEnd);
            double rawEnd = WaterQuery.RawSeabedHeightAt(body, latEnd, lonEnd);
            Vector3d radialEnd = PlayerPosition - bodyPosEnd;
            double distEnd = radialEnd.Magnitude;
            Vector3d upEnd = distEnd > 1e-9d ? radialEnd / distEnd : up;

            if (!double.IsNaN(rawEnd))
            {
                double minDist = body.Radius + rawEnd + SwimSeabedClearanceMeters;
                if (distEnd < minDist)
                {
                    PlayerPosition = bodyPosEnd + (upEnd * minDist);
                    distEnd = minDist;
                }
            }

            double submEnd = seaRadius - distEnd;
            if (submEnd < -0.3d)
            {
                if (WaterQuery.WaterDepthAt(body, latEnd, lonEnd) > 1d)
                {
                    PlayerMode = PlayerMode.EVA;
                    playerAirborne = false;
                    JetpackActive = false;
                    PlayerHoverActive = false;
                }
                else
                {
                    LandPlayer(body, tEnd);
                }
            }
        }

        // Pure walking controller: vertical support, step, slope, ceiling, corners.
        // Works in the local site frame (metres), orbital state in the double world.
        private void StepPlayerSurface(double t, double dt, bool jumpRequested)
        {
            if (platformCarry.Active)
            {
                if (CarryAllowed())
                {
                    StepPlayerCabin(t, dt, jumpRequested);
                    return;
                }

                DetachCabin();
                if (PlayerMode != PlayerMode.OnSurface)
                {
                    return;
                }
            }

            OrbitingBody body = DominantBody;
            if (body == null)
            {
                return;
            }

            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);

            ITerrainModel playerTerrain = body.Terrain ?? playerFallbackTerrain;
            SiteFrame frame = SiteFrame.Anchor(body, playerTerrain, PlayerPosition, t);
            frame.ToLocal(PlayerPosition, PlayerVelocity, out Vector3d localPosition, out Vector3d localVelocity);

            playerController.Position = localPosition;
            playerController.Velocity = localVelocity;
            playerController.Airborne = playerAirborne;
            // Weight = effective gravity of the site frame (same definition as the contact zone): all bodies,
            // minus the planet's own acceleration, minus the centrifugal term of its spin.
            Vector3d effectiveGravity = SurfaceGravity.EffectiveLocal(SystemState, body, frame, t);
            playerController.Gravity = Math.Max(0d, -effectiveGravity.Z);

            terrainSupport.SetFrame(body, playerTerrain, frame, t);
            siteSupport.SetFrame(frame);
            vesselSupport.SetFrame(frame);
            playerSupport.Terrain = terrainSupport;
            playerSupport.Sites = siteSupport;
            playerSupport.Vessel = vesselSupport;

            Vector3d walkWorld = PlayerIntent.WalkDirection * PlayerIntent.WalkSpeed;
            Vector3d walkLocal = new Vector3d(
                Vector3d.Dot(walkWorld, frame.East),
                Vector3d.Dot(walkWorld, frame.North),
                0d);
            Vector3d airWorld = PlayerIntent.AirVelocity;
            Vector3d airLocal = new Vector3d(
                Vector3d.Dot(airWorld, frame.East),
                Vector3d.Dot(airWorld, frame.North),
                0d);
            Vector3d jetpack = JetpackActive ? PlayerIntent.JetpackAccel : Vector3d.Zero;
            Vector3d jetpackLocal = new Vector3d(
                Vector3d.Dot(jetpack, frame.East),
                Vector3d.Dot(jetpack, frame.North),
                Vector3d.Dot(jetpack, frame.Up));

            Vector3d hover = PlayerHoverActive ? PlayerIntent.HoverVelocity : Vector3d.Zero;
            Vector3d hoverLocal = new Vector3d(
                Vector3d.Dot(hover, frame.East),
                Vector3d.Dot(hover, frame.North),
                Vector3d.Dot(hover, frame.Up));

            playerController.Hovering = PlayerHoverActive;
            playerController.HoverVelocity = hoverLocal;
            playerController.Step(playerSupport, dt, playerAirborne ? airLocal : walkLocal, jetpackLocal);

            // To world — with the frame AT THE END of the substep (frame.At(t+dt)): the
            // frame at time t lags the body's orbital motion by v·dt, and projecting onto
            // the surface at t+dt would bake that lag into the position every substep.
            double tEnd = t + dt;
            frame.At(tEnd).ToWorld(playerController.Position, playerController.Velocity,
                out Vector3d worldPosition, out Vector3d worldVelocity);
            PlayerPosition = worldPosition;
            PlayerVelocity = worldVelocity;
            playerAirborne = playerController.Airborne;

            // Touched support — hover turns off (the mode is air-only).
            if (!playerAirborne && PlayerHoverActive)
            {
                PlayerHoverActive = false;
            }

            // Tree-trunk collision, after the step.
            if (GroundDecorCollisionRegistry.TryResolve(body.Name, PlayerPosition, PlayerCollisionRadiusMeters, out Vector3d treePushed))
            {
                PlayerPosition = treePushed;
            }

            // Water and projection onto the surface AT THE END of the substep (t+dt).
            body.SurfaceLatLonAt(PlayerPosition, tEnd, out double newLat, out double newLon);
            if (WaterQuery.IsWaterAt(body, newLat, newLon))
            {
                EnterWater(body, tEnd);
                return;
            }

            // Safety: body below the terrain (sank in) — put it back on the surface,
            // otherwise support queries can no longer find it (the surface stayed above).
            // A possibly "inside a wall" body gets itself out: the sweep blocks only
            // motion INTO the surface, not contact (see SiteBoxSupport.Sweep).
            body.EvaluateWorldState(tEnd, out Vector3d rescueBodyPos, out _);
            double rescueGround = GroundHeight(body, newLat, newLon);
            if ((PlayerPosition - rescueBodyPos).Magnitude - body.Radius < rescueGround - 0.5d)
            {
                Vector3d rescueRadial = (PlayerPosition - rescueBodyPos).Normalized;
                PlayerPosition = rescueBodyPos + (rescueRadial * (body.Radius + rescueGround));
                PlayerVelocity -= rescueRadial * Vector3d.Dot(PlayerVelocity, rescueRadial);
                playerAirborne = false;
                playerController.SetGroundSource(TerrainSupport.SourceId);
            }

            // Projection onto the terrain — only when the player stands on terrain (not on
            // a structure): roofs hold the controller's local height and projection would pull it down.
            if (!playerAirborne && playerController.GroundSourceId == TerrainSupport.SourceId)
            {
                double newGround = GroundHeight(body, newLat, newLon);
                body.EvaluateWorldState(tEnd, out Vector3d bodyPos3, out _);
                Vector3d radial = PlayerPosition - bodyPos3;
                PlayerPosition = bodyPos3 + (radial.Normalized * (body.Radius + newGround));
            }

            if (jumpRequested)
            {
                playerAirborne = true;
                Vector3d normal = body.Terrain != null
                    ? body.Terrain.GetOutwardNormal(body, PlayerPosition - bodyPos, t).Normalized
                    : (PlayerPosition - bodyPos).Normalized;
                PlayerVelocity += normal * PlayerJumpSpeed;
            }

            TryAttachCabin();
        }

        /// <summary>Body terrain height at a point (lat/lon, degrees); a sphere when null.</summary>
        private static double GroundHeight(OrbitingBody body, double latDeg, double lonDeg)
        {
            return body.Terrain?.GetHeightMeters(body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d)) ?? 0d;
        }
    }
}
