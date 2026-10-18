using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Flight;
using Galilego.Spacecraft;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Universe
{
    /// <summary>
    /// Per-frame ship step: flight (orbital chain with events), landing
    /// (constrained surface dynamics), event reactions. Extracted from
    /// SimulationRunner so the game stays a thin MonoBehaviour wrapper and the
    /// step can run without Unity (test bench, landing traces).
    ///
    /// State ownership: the ship, its regime (Flying/Landed/Destroyed), the
    /// dominant body and time with the Kahan accumulator live here. The game
    /// loop (SimulationRunner) reads them and is responsible only for input,
    /// the player, the render anchor and debris updates.
    /// </summary>
    public sealed class VesselStep
    {
        public readonly StarSystem SystemState;
        public readonly WarpController Warp;
        public readonly EventDrivenPropagator Propagator;
        public readonly LongWarpDriver Driver;
        public readonly ThrustSource MainThrust;

        /// <summary>
        /// Active thrust source: legacy MainThrust or the staged
        /// StageThrustSource (set by SimulationRunner.SetVesselPropulsion).
        /// </summary>
        public IDynamicsSource Thrust;

        /// <summary>
        /// Staged propulsion model. null — legacy scenes without a builder: fuel is
        /// not tracked, the dry-mass threshold lives in ThrustSource.
        /// </summary>
        public StagePropulsion Propulsion;

        /// <summary>
        /// 6-DOF flight model (engines at their real points, CoM/inertia from current
        /// masses, gimbal, RCS, SAS). null — the previous path: point mass without orientation.
        /// </summary>
        public VesselFlightModel Flight;

        /// <summary>Thrust source of the flight model (given the per-chunk attitude snapshot).</summary>
        public FlightThrustSource FlightSource;

        /// <summary>Ambient pressure (Pa) at a point and time; set by SimulationRunner.</summary>
        public Func<Vector3d, double, double> PressureAt;

        /// <summary>Flight control step: gimbal/RCS/autopilot update on it (before ×3 warp).</summary>
        public const double FlightChunkSeconds = 0.05d;

        /// <summary>Burn-section depletion detector (armed before a chunk).</summary>
        public StageDepletionDetector DepletionDetector;

        public readonly IBreakupModel BreakupModel;
        public readonly DebrisPool Debris;

        /// <summary>
        /// Assembly parts. Not readonly: build mode swaps the list on the fly
        /// (handing the craft to physics and reverting back to building).
        /// </summary>
        public PartDefinition[] Parts;
        public readonly double SpawnEpsilonMeters;
        public readonly double DebrisLifetimeSeconds;

        /// <summary>Ship. The game loop sets it at spawn and never replaces it afterwards.</summary>
        public Ship Ship;

        /// <summary>Ship regime: flying, resting on the surface or destroyed.</summary>
        public VesselRegime Regime;

        /// <summary>Body the ship currently lives relative to (floating origin, SOI).</summary>
        public OrbitingBody DominantBody;

        /// <summary>Simulated step time (s).</summary>
        public double TimeSeconds { get; private set; }

        /// <summary>Raw throttle 0..1 from input; read in the step when thrust is active.</summary>
        public double RawThrottle;

        /// <summary>Observability: called before event handling, state is the moment of impact.</summary>
        public Action<EventOccurrence> OccurrenceHandled;

        // Not readonly: KahanAccumulator is a struct, so mutating methods on a
        // readonly field would operate on a copy.
        private KahanAccumulator time = new KahanAccumulator(0d);
        private static readonly SphericalTerrain surfaceTerrain = new SphericalTerrain();

        /// <summary>Mass invariant violated (logged on the edge, not every chunk).</summary>
        private bool massInvariantBroken;

        /// <summary>Upper bound on a surface substep (s), consistent with the control-tick grain.</summary>
        private const double SurfaceMaxStepSeconds = 0.5d;

        public VesselStep(
            StarSystem systemState,
            WarpController warp,
            EventDrivenPropagator propagator,
            LongWarpDriver driver,
            ThrustSource mainThrust,
            IBreakupModel breakupModel,
            DebrisPool debris,
            PartDefinition[] parts,
            double spawnEpsilonMeters,
            double debrisLifetimeSeconds)
        {
            SystemState = systemState ?? throw new ArgumentNullException(nameof(systemState));
            Warp = warp ?? throw new ArgumentNullException(nameof(warp));
            Propagator = propagator ?? throw new ArgumentNullException(nameof(propagator));
            Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            MainThrust = mainThrust;
            Thrust = mainThrust;
            BreakupModel = breakupModel ?? throw new ArgumentNullException(nameof(breakupModel));
            Debris = debris ?? throw new ArgumentNullException(nameof(debris));
            Parts = parts;
            SpawnEpsilonMeters = spawnEpsilonMeters;
            DebrisLifetimeSeconds = debrisLifetimeSeconds;
            TimeSeconds = 0d;
            Regime = VesselRegime.Flying;
        }

        /// <summary>
        /// Resets the Kahan accumulator to the current time: after external ship
        /// moves (teleport) the accumulated compensation does not apply to the
        /// new trajectory.
        /// </summary>
        public void ResetTimeAccumulator()
        {
            time.Reset(TimeSeconds);
        }

        /// <summary>
        /// Advances step time WITHOUT physics: the contact zone drives the ship itself (PhysX),
        /// but world time flows (the planet moves, ephemerides are read at the current t).
        /// The accumulator is updated the same way as in the steps (time.Reset).
        /// Touches no existing step methods: only TimeSeconds and the accumulator.
        /// </summary>
        public void AdvanceTime(double dt)
        {
            if (!(dt > 0d) || !double.IsFinite(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt),
                    "AdvanceTime: dt обязан быть конечным и положительным, получено " + dt + ".");
            }

            TimeSeconds += dt;
            time.Reset(TimeSeconds);
        }

        /// <summary>
        /// Flight for a frame: orbital chain in chunks by the control-tick boundary,
        /// the event is handled here too. playerInShip=false (player outside the
        /// ship) forbids long warp and caps physical ×4 — otherwise the player could
        /// not be caught up by substeps.
        /// </summary>
        public void StepFlying(float realDt, bool playerInShip)
        {
            WarpFrameDecision decision = EphemerisRuntime.ComputeFrame(
                SystemState, Warp, Ship.Position, TimeSeconds, realDt);
            double playerCap = playerInShip ? double.PositiveInfinity : 4d;
            double factor = Math.Min(decision.EffectiveFactor, playerCap);
            double frameEnd = TimeSeconds + (realDt * factor);

            if (decision.UseLongWarp && playerCap == double.PositiveInfinity)
            {
                // Long warp: ballistics entirely, thrust forbidden by construction.
                if (Flight != null)
                {
                    Flight.Silence();
                }

                Warp.PrepareForLongWarp(TimeSeconds);
                Driver.CancelRequested = false;
                LongWarpResult result = Driver.AdvanceToTarget(Ship, TimeSeconds, frameEnd);
                TimeSeconds = result.ReachedTimeSeconds;
                time.Reset(TimeSeconds);
                return;
            }

            double throttle = Warp.EffectiveThrottle(RawThrottle, factor);
            bool thrustActive = Thrust != null && throttle > 0d
                && (Propulsion == null || Propulsion.MassFlowKgS > 0d);
            bool flightOn = Flight != null;
            bool flightAllowed = flightOn && factor <= WarpController.ThrustMaxWarpFactor;
            if (flightAllowed)
            {
                thrustActive = true;
            }

            while (TimeSeconds < frameEnd - 1e-12d)
            {
                double chunk = Warp.GetMaxChunkSeconds(TimeSeconds, OrbitIntegrator.DefaultMaxStepSize, thrustActive);
                if (flightAllowed)
                {
                    chunk = Math.Min(chunk, FlightChunkSeconds);
                }

                chunk = Math.Min(chunk, frameEnd - TimeSeconds);
                if (chunk <= 0d)
                {
                    chunk = Math.Min(OrbitIntegrator.DefaultMinStepSize, frameEnd - TimeSeconds);
                }

                if (flightOn)
                {
                    double pressure = PressureAt != null ? PressureAt(Ship.Position, TimeSeconds) : 0d;
                    Flight.Prepare(chunk, Ship.Attitude, Ship.AngularVelocity, throttle, pressure, flightAllowed);
                    FlightSource.Attitude = Ship.Attitude;
                    chunk = Math.Max(Math.Min(chunk, Flight.MaxChunkSeconds()), 1e-4d);
                }

                Warp.SampleControl(TimeSeconds, throttle);
                RearmDepletion();
                double massBefore = Ship.Mass;
                double chunkStart = TimeSeconds;
                EventOccurrence? occurrence = Propagator.Propagate(Ship, TimeSeconds, chunk);
                double auxBurn = 0d;
                if (flightOn)
                {
                    double done = occurrence.HasValue ? Math.Max(0d, occurrence.Value.TimeSeconds - chunkStart) : chunk;
                    auxBurn = Flight.Finish(done);
                    StepAttitude(done, chunkStart);
                }

                CommitBurn(massBefore - Ship.Mass - auxBurn);
                if (occurrence.HasValue)
                {
                    TimeSeconds = occurrence.Value.TimeSeconds;
                    time.Reset(TimeSeconds);
                    HandleOccurrence(occurrence.Value);
                    if (Regime != VesselRegime.Flying)
                    {
                        return;
                    }
                }
                else
                {
                    time.Add(chunk);
                    TimeSeconds = time.Sum;
                }
            }
        }

        /// <summary>
        /// Rotation over a chunk: Euler equations + quaternion (RK4) from the flight
        /// model torque computed on THIS chunk (the same snapshot as the force in the orbital integrator).
        /// </summary>
        private void StepAttitude(double dt, double timeSeconds)
        {
            if (!(dt > 0d) || !(Flight.MassKg > 0d))
            {
                return;
            }

            AttitudeState next = AttitudePhysics.Step(
                new AttitudeState(Ship.Attitude, Ship.AngularVelocity), Flight.Inertia,
                Flight.TorqueBody, timeSeconds, dt);
            Ship.Attitude = next.Attitude;
            Ship.AngularVelocity = next.AngularVelocity;
        }

        /// <summary>
        /// Landing for a frame: constrained surface dynamics in chunks. Long warp is
        /// forbidden, physical factor capped at ×3 (as in the atmosphere). Thrust above
        /// the press-down lifts the ship off (TryLiftoff) — returning to Flying here too.
        /// </summary>
        public void StepLanded(float realDt, bool playerInShip)
        {
            WarpFrameDecision decision = EphemerisRuntime.ComputeFrame(
                SystemState, Warp, Ship.Position, TimeSeconds, realDt);
            double playerCap = playerInShip ? double.PositiveInfinity : 4d;
            double factor = Math.Min(Math.Min(decision.EffectiveFactor, WarpController.AtmosphereMaxWarpFactor), playerCap);
            double frameEnd = TimeSeconds + (realDt * factor);
            double throttle = Warp.EffectiveThrottle(RawThrottle, factor);
            bool thrustActive = Thrust != null && throttle > 0d
                && (Propulsion == null || Propulsion.MassFlowKgS > 0d);

            while (TimeSeconds < frameEnd - 1e-12d)
            {
                if (thrustActive)
                {
                    Vector3d gravity = SystemState.EvaluateShipAcceleration(Ship.Position, TimeSeconds);
                    DynamicsContribution thrust = Thrust.Evaluate(Ship.Position, Ship.Velocity, Ship.Mass, TimeSeconds);
                    if (EventReactions.TryLiftoff(Ship, DominantBody, thrust.Force / Ship.Mass, gravity, TimeSeconds, out _)
                        == VesselRegime.Flying)
                    {
                        Regime = VesselRegime.Flying;
                        Warp.ResetTicks(TimeSeconds);
                        time.Reset(TimeSeconds);
                        return;
                    }
                }

                double chunk = Warp.GetMaxChunkSeconds(TimeSeconds, SurfaceMaxStepSeconds, thrustActive);
                chunk = Math.Min(chunk, frameEnd - TimeSeconds);
                if (chunk <= 0d)
                {
                    chunk = Math.Min(SurfaceMaxStepSeconds, frameEnd - TimeSeconds);
                }

                Warp.SampleControl(TimeSeconds, throttle);
                SurfaceMotionResult result = SurfaceMotion.Step(
                    Ship.Position, Ship.Velocity, DominantBody, surfaceTerrain,
                    (position, t) => SystemState.EvaluateShipAcceleration(position, t),
                    TimeSeconds, chunk);
                Ship.Position = result.Position;
                Ship.Velocity = result.Velocity;
                time.Add(chunk);
                TimeSeconds = time.Sum;
                if (result.Regime == VesselRegime.Flying)
                {
                    Regime = VesselRegime.Flying;
                    Warp.ResetTicks(TimeSeconds);
                    return;
                }
            }
        }

        private void HandleOccurrence(EventOccurrence occurrence)
        {
            // Observability hook — before reactions: the bench sees the moment of
            // impact, not the result of projection/breakup.
            OccurrenceHandled?.Invoke(occurrence);
            switch (occurrence.Kind)
            {
                case EventKind.Touchdown:
                    TouchdownOutcome outcome = EventReactions.ApplyTouchdown(
                        Ship, occurrence, occurrence.Body, BreakupModel, SpawnEpsilonMeters,
                        NormalizeAssembly(Ship.Mass));
                    if (outcome.IsLanding)
                    {
                        Regime = VesselRegime.Landed;
                        DominantBody = occurrence.Body;
                        Warp.ResetTicks(occurrence.TimeSeconds);
                        LandedState landed = outcome.Landed;
                        Debug.Log("Посадка на \"" + occurrence.Body.Name + "\" ("
                            + landed.LatitudeDegrees.ToString("F2") + "°, "
                            + landed.LongitudeDegrees.ToString("F2") + "°), v_t = "
                            + landed.TangentialSpeed.ToString("F1") + " м/с");
                    }
                    else
                    {
                        ApplyBreakup(occurrence, outcome.Specs);
                    }

                    break;
                case EventKind.PropellantDepleted:
                    if (Propulsion != null)
                    {
                        HandleStageDepletion();
                    }
                    else
                    {
                        EventReactions.ApplyDepletion(Ship, occurrence);
                    }

                    break;
                case EventKind.EphemerisEnd:
                    Debug.LogWarning("Конец испечённого мира — варп ограничен кеплеровыми рельсами.");
                    break;
            }
        }

        /// <summary>
        /// Root of a staged-propulsion burn-section depletion event: the mass has
        /// already been charged off by CommitBurn (fact of the step), the pool is
        /// exhausted exactly at the boundary. Auto-ignite (stage-1 debug) picks up the
        /// next stage once the current one has fully flamed out; separation is a separate command.
        /// </summary>
        private void HandleStageDepletion()
        {
            double flow = Propulsion.MassFlowKgS;
            if (flow > 0d)
            {
                Debug.Log(string.Format(
                    "[Prop] ступень {0}: группа заглохла по пулу, расход {1:F1} кг/с",
                    Propulsion.ActiveStageIndex + 1, flow));
                return;
            }

            Debug.Log(string.Format("[Prop] ступень {0}: топливо кончилось",
                Propulsion.ActiveStageIndex + 1));
            if (Propulsion.AutoIgnite && Propulsion.IgniteNextAttached())
            {
                Debug.Log(string.Format("[Prop] автозажигание (отладка): ступень {0}",
                    Propulsion.ActiveStageIndex + 1));
            }
        }

        /// <summary>
        /// Charges the mass actually burned over the step off the staged model (the
        /// source of truth is mass: Ship.Mass has already decreased by the integrator massFlow)
        /// and checks the invariant "dry + attached pools + mass outside the assembly == Ship.Mass".
        /// </summary>
        private void CommitBurn(double burnedKg)
        {
            if (Propulsion == null || !(burnedKg > 0d))
            {
                return;
            }

            Propulsion.CommitBurn(burnedKg);
            CheckMassInvariant();
        }

        /// <summary>Arms the depletion detector for the current section and mass (before a chunk).</summary>
        private void RearmDepletion()
        {
            if (DepletionDetector != null)
            {
                DepletionDetector.Arm(Propulsion, Ship.Mass);
            }
        }

        /// <summary>
        /// Mass invariant: the model must account for Ship.Mass entirely. A discrepancy
        /// above 1e-6 relative means double/missing accounting (logged on the edge, no spam).
        /// </summary>
        private void CheckMassInvariant()
        {
            double expected = Propulsion.AttachedTotalKg() + Propulsion.DetachedMassKg;
            double relative = Math.Abs(expected - Ship.Mass) / Math.Max(1d, Ship.Mass);
            if (relative > 1e-6d)
            {
                if (!massInvariantBroken)
                {
                    massInvariantBroken = true;
                    Debug.LogError(string.Format(
                        "[Prop] инвариант массы нарушен: модель {0:F3} кг (вне сборки {1:F3}), Ship.Mass {2:F3} кг, отн. {3:E2}",
                        expected, Propulsion.DetachedMassKg, Ship.Mass, relative));
                }
            }
            else if (massInvariantBroken)
            {
                massInvariantBroken = false;
            }
        }

        /// <summary>
        /// Part assembly normalised to the actual ship mass (part masses define
        /// PROPORTIONS: fuel burns off, the total mass drifts).
        /// null if no parts are set — the breakup model's whole-ship path.
        /// </summary>
        private List<Part> NormalizeAssembly(double shipMass)
        {
            return PartAssembly.FromDefinitions(Parts, shipMass);
        }

        /// <summary>
        /// Hard impact: broken-off parts → debris, the surviving assembly stays the
        /// ship (mass = remainder, landing continues via SurfaceMotion).
        /// Empty spec list = joints hold → landing with damage.
        /// No intact part left → Destroyed.
        /// </summary>
        private void ApplyBreakup(EventOccurrence occurrence, System.Collections.Generic.IReadOnlyList<PartSeparationSpec> specs)
        {
            double brokenMass = 0d;
            for (int i = 0; i < specs.Count; i++)
            {
                Debris.Spawn(specs[i].Position, specs[i].Velocity, specs[i].Mass,
                    occurrence.TimeSeconds, DebrisLifetimeSeconds);
                brokenMass += specs[i].Mass;
            }

            double survivorMass = Ship.Mass - brokenMass;
            DominantBody = occurrence.Body;

            // Breakup tears the assembly apart: the staged model (cores/pools) no longer
            // matches the ship — disable it (the panel falls back to the legacy branch).
            Propulsion = null;
            DepletionDetector = null;
            Thrust = MainThrust;
            if (survivorMass > 1e-9d)
            {
                Ship.Mass = survivorMass;
                Regime = VesselRegime.Landed;
                Warp.ResetTicks(occurrence.TimeSeconds);
                Debug.Log("Удар о \"" + occurrence.Body.Name + "\": потеряно "
                    + brokenMass.ToString("F0") + " кг, осталось "
                    + specs.Count + " отделившихся деталей; сборка села.");
            }
            else
            {
                Regime = VesselRegime.Destroyed;
                Warp.SetWarpFactor(WarpController.MinWarpFactor);
                Debug.LogWarning("Удар о \"" + occurrence.Body.Name + "\": сборка разрушена полностью ("
                    + specs.Count + " обломков).");
            }
        }
    }
}
