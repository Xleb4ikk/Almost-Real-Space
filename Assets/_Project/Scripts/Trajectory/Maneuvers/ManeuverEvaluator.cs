using System;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Spacecraft;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Burn-mode tolerances are a CALL parameter, not a built-in assumption.
    /// A chemical burn (thrust of a few g) is limited by mass and direction error —
    /// so massTol is tight; low-thrust (ion, thrust below gravity) is limited by
    /// position/velocity error over a long arc. Choosing the wrong one solves the
    /// wrong problem, so the preset is passed explicitly.
    /// </summary>
    public readonly struct BurnTolerancePreset
    {
        public readonly double PositionAbsoluteTolerance;
        public readonly double VelocityAbsoluteTolerance;
        public readonly double MassAbsoluteTolerance;
        public readonly double RelativeTolerance;

        public BurnTolerancePreset(double positionAbsoluteTolerance, double velocityAbsoluteTolerance, double massAbsoluteTolerance, double relativeTolerance)
        {
            PositionAbsoluteTolerance = positionAbsoluteTolerance;
            VelocityAbsoluteTolerance = velocityAbsoluteTolerance;
            MassAbsoluteTolerance = massAbsoluteTolerance;
            RelativeTolerance = relativeTolerance;
        }

        /// <summary>Chemical engine: mass/direction dominate.</summary>
        public static BurnTolerancePreset Chemical => new BurnTolerancePreset(1e-3d, 1e-6d, 1e-7d, 1e-11d);

        /// <summary>Low thrust: position/velocity over a long arc dominate.</summary>
        public static BurnTolerancePreset LowThrust => new BurnTolerancePreset(1e-2d, 1e-5d, 1e-6d, 1e-10d);
    }

    /// <summary>
    /// Result of the coast+burn sequence: where it stopped, why (event or cutoff),
    /// and whether the burn ran through to the planned cutoff.
    /// </summary>
    public readonly struct ManeuverResult
    {
        public readonly double EndTimeSeconds;
        public readonly SpacecraftIntegrationState EndState;
        public readonly EventOccurrence? StoppingEvent;
        public readonly bool CompletedBurn;

        public ManeuverResult(double endTimeSeconds, SpacecraftIntegrationState endState, EventOccurrence? stoppingEvent, bool completedBurn)
        {
            EndTimeSeconds = endTimeSeconds;
            EndState = endState;
            StoppingEvent = stoppingEvent;
            CompletedBurn = completedBurn;
        }
    }

    /// <summary>
    /// Coast→burn sequence built on the existing drivers, not a new engine: coast to
    /// ignition is LongWarpDriver (ballistic; throttle is zero there and entry is
    /// formally allowed), the burn itself is EventDrivenPropagator in chunks clipped
    /// by WarpController at the control tick. The tolerance preset is applied for the
    /// burn and restored in finally: the architecture does not know whether this is
    /// "chemical" or ion.
    ///
    /// Planned cutoff comes from the ThrottleAt schedule (continuous to zero); stage
    /// jettison is StageSeparation (mass break/impulse, discrete); unplanned depletion
    /// is handled by PropellantDepletionDetector in the burn propagator.
    /// </summary>
    public sealed class ManeuverEvaluator
    {
        private readonly SpacecraftPhysics physics;
        private readonly EventDrivenPropagator coastPropagator;
        private readonly LongWarpDriver coastDriver;
        private readonly EventDrivenPropagator burnPropagator;
        private readonly WarpController warp;

        public ManeuverEvaluator(
            SpacecraftPhysics physics,
            EventDrivenPropagator coastPropagator,
            LongWarpDriver coastDriver,
            EventDrivenPropagator burnPropagator,
            WarpController warp)
        {
            this.physics = physics ?? throw new ArgumentNullException(nameof(physics));
            this.coastPropagator = coastPropagator ?? throw new ArgumentNullException(nameof(coastPropagator));
            this.coastDriver = coastDriver ?? throw new ArgumentNullException(nameof(coastDriver));
            this.burnPropagator = burnPropagator ?? throw new ArgumentNullException(nameof(burnPropagator));
            this.warp = warp ?? throw new ArgumentNullException(nameof(warp));
        }

        public ManeuverResult CoastThenBurn(
            Ship ship,
            double startTimeSeconds,
            double igniteTimeSeconds,
            double cutTimeSeconds,
            BurnTolerancePreset preset,
            double frameSimSeconds,
            Func<double, double> throttleAt = null)
        {
            if (ship == null)
            {
                throw new ArgumentNullException(nameof(ship));
            }

            if (cutTimeSeconds <= igniteTimeSeconds || igniteTimeSeconds < startTimeSeconds)
            {
                throw new ArgumentOutOfRangeException("Окно прожига обязано лежать впереди старта.");
            }

            // frameSimSeconds ≤ 0 gives frameEnd = t: the inner loop never runs and
            // timeAcc never grows — an infinite loop. Fail loudly.
            if (!(frameSimSeconds > 0d) || !double.IsFinite(frameSimSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(frameSimSeconds),
                    "frameSimSeconds обязан быть конечным и положительным, получено " + frameSimSeconds + ".");
            }

            // Throttle: a schedule 0..1 over time. null = stub until the staging/input
            // system exists — full throttle for the whole burn.
            Func<double, double> throttle = throttleAt ?? (_ => 1d);

            LongWarpResult coast = coastDriver.AdvanceToTarget(ship, startTimeSeconds, igniteTimeSeconds);
            if (coast.StoppingEvent.HasValue)
            {
                return new ManeuverResult(coast.ReachedTimeSeconds, Capture(ship), coast.StoppingEvent, false);
            }

            double savedPosTol = physics.PositionAbsoluteTolerance;
            double savedVelTol = physics.VelocityAbsoluteTolerance;
            double savedMassTol = physics.MassAbsoluteTolerance;
            double savedRtol = physics.RelativeTolerance;
            physics.PositionAbsoluteTolerance = preset.PositionAbsoluteTolerance;
            physics.VelocityAbsoluteTolerance = preset.VelocityAbsoluteTolerance;
            physics.MassAbsoluteTolerance = preset.MassAbsoluteTolerance;
            physics.RelativeTolerance = preset.RelativeTolerance;
            try
            {
                warp.ResetTicks(igniteTimeSeconds);
                var timeAcc = new KahanAccumulator(igniteTimeSeconds);
                while (timeAcc.Sum < cutTimeSeconds - 1e-12d)
                {
                    double t = timeAcc.Sum;
                    double frameEnd = Math.Min(t + frameSimSeconds, cutTimeSeconds);
                    while (t < frameEnd - 1e-12d)
                    {
                        double maxChunk = warp.GetMaxChunkSeconds(t, burnPropagator.MaxChunkSeconds, true);
                        double chunk = Math.Min(maxChunk, frameEnd - t);
                        if (chunk <= 0d)
                        {
                            // A zero from the warp gate is a legitimate refusal to advance
                            // (a veto), not a "we'll just sub-step ourselves": fail loudly.
                            throw new InvalidOperationException(
                                "Warp-контроллер запретил продвижение (maxChunk=" + maxChunk +
                                ") на t=" + t.ToString("R") + " — продлевать окно симуляции нельзя без диагностики.");
                        }

                        double throttleNow = throttle(t);
                        // A NaN throttle silently passes the clamp (Max/Min with NaN = NaN)
                        // and reaches the propagator.
                        if (!double.IsFinite(throttleNow))
                        {
                            throw new InvalidOperationException(
                                "Расписание газа вернуло не-конечное значение на t=" + t.ToString("R") + ".");
                        }

                        warp.SampleControl(t, Math.Max(0d, Math.Min(1d, throttleNow)));
                        EventOccurrence? hit = burnPropagator.Propagate(ship, t, chunk);
                        if (hit.HasValue)
                        {
                            return new ManeuverResult(hit.Value.TimeSeconds, Capture(ship), hit, false);
                        }

                        timeAcc.Add(chunk);
                        t = timeAcc.Sum;
                    }
                }

                return new ManeuverResult(cutTimeSeconds, Capture(ship), null, true);
            }
            finally
            {
                physics.PositionAbsoluteTolerance = savedPosTol;
                physics.VelocityAbsoluteTolerance = savedVelTol;
                physics.MassAbsoluteTolerance = savedMassTol;
                physics.RelativeTolerance = savedRtol;
            }
        }

        /// <summary>
        /// Stage jettison conserving momentum. The internal impulse separates TWO
        /// bodies: v1 = v0 + (m2/M)·Δv_rel for the remainder, v2 = v0 − (m1/M)·Δv_rel
        /// for the jettisoned stage; m1·v1 + m2·v2 = M·v0 holds by construction of the
        /// coefficients. The Δv_rel direction is supplied as a ready vector by the
        /// caller (taken from AttitudeState; Events does not own attitude). The stage
        /// is spawned in DebrisPool the same way debris is and lives longer than
        /// default debris. Throws loudly if the remaining mass drops below the minimum.
        /// </summary>
        public static int StageSeparation(
            Ship ship,
            double stageMassKg,
            Vector3d relativeVelocity,
            DebrisPool pool,
            double timeSeconds,
            double minimumMassKg,
            double lifetimeSeconds = 1e7d)
        {
            if (ship == null)
            {
                throw new ArgumentNullException(nameof(ship));
            }

            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            if (stageMassKg <= 0d || stageMassKg >= ship.Mass)
            {
                throw new ArgumentOutOfRangeException(nameof(stageMassKg), "Масса ступени обязана быть в (0, M).");
            }

            double totalMass = ship.Mass;
            double remainingMass = totalMass - stageMassKg;
            if (remainingMass < minimumMassKg)
            {
                throw new InvalidOperationException(
                    "Staging: масса после сброса " + remainingMass.ToString("F3") +
                    " кг ниже минимума " + minimumMassKg.ToString("F3") + " кг.");
            }

            Vector3d initialVelocity = ship.Velocity;
            ship.Mass = remainingMass;
            ship.Velocity = initialVelocity + (relativeVelocity * (stageMassKg / totalMass));
            Vector3d stageVelocity = initialVelocity - (relativeVelocity * (remainingMass / totalMass));
            return pool.Spawn(ship.Position, stageVelocity, stageMassKg, timeSeconds, lifetimeSeconds);
        }

        private static SpacecraftIntegrationState Capture(Ship ship)
        {
            return new SpacecraftIntegrationState
            {
                Position = ship.Position,
                Velocity = ship.Velocity,
                Mass = ship.Mass
            };
        }
    }
}
