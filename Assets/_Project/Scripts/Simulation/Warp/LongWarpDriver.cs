using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Result of a long jump: reached the target or stopped on an event.
    /// For smooth rendering — AdvanceToTargetWithSegments: a chain of dense output
    /// for the traversed path, sampled with no Step at all (see DenseSegment).
    /// </summary>
    public readonly struct LongWarpResult
    {
        public readonly double ReachedTimeSeconds;
        public readonly EventOccurrence? StoppingEvent;
        public readonly bool ReachedTarget;

        public LongWarpResult(double reachedTimeSeconds, EventOccurrence? stoppingEvent, bool reachedTarget)
        {
            ReachedTimeSeconds = reachedTimeSeconds;
            StoppingEvent = stoppingEvent;
            ReachedTarget = reachedTarget;
        }
    }

    /// <summary>
    /// Long warp — not a new engine but another way of calling the existing
    /// EventDrivenPropagator straight to a large target mark. On smooth stretches
    /// the adaptive step grows itself, and the layer stops advancing at the first
    /// event. The entry condition is formal: no IDynamicsSource other than gravity
    /// contributes non-zero right now (checked by actual contribution, not by
    /// player mode). Cancelling on thrust touch is silent, BEFORE the next RHS
    /// call, so no call ever sees a warp-scale step together with an active
    /// non-gravity source.
    /// </summary>
    public sealed class LongWarpDriver
    {
        private readonly SpacecraftPhysics physics;
        private readonly EventDrivenPropagator propagator;

        /// <summary>
        /// Checked on every RHS through a wrapper: if the cancel flag is set
        /// (the player touched thrust), further computation is forbidden.
        /// The check is cheap.
        /// </summary>
        public bool CancelRequested;

        public LongWarpDriver(SpacecraftPhysics physics, EventDrivenPropagator propagator)
        {
            this.physics = physics ?? throw new ArgumentNullException(nameof(physics));
            this.propagator = propagator ?? throw new ArgumentNullException(nameof(propagator));
        }

        /// <summary>
        /// Formal entry check: all sources except GravitySource and
        /// CachedGravitySource contribute zero at the current point.
        /// </summary>
        public bool CanEnterLongWarp(SpacecraftIntegrationState state, double timeSeconds)
        {
            for (int i = 0; i < physics.Sources.Count; i++)
            {
                IDynamicsSource source = physics.Sources[i];
                if (source is GravitySource || source is CachedGravitySource)
                {
                    continue;
                }

                DynamicsContribution c = source.Evaluate(state.Position, state.Velocity, state.Mass, timeSeconds);
                if (c.Force.SqrMagnitude > 0d || c.SpecificAcceleration.SqrMagnitude > 0d || c.MassFlow != 0d)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Runs to targetTimeSeconds or to the first event. Silent reset: if
        /// CancelRequested is set before/during the computation, we stop at the
        /// current point without throwing; the ship stays valid at the boundary
        /// of the already-accepted chunks.
        /// </summary>
        public LongWarpResult AdvanceToTarget(Ship ship, double currentTimeSeconds, double targetTimeSeconds)
        {
            return AdvanceToTargetWithSegments(ship, currentTimeSeconds, targetTimeSeconds, null);
        }

        /// <summary>
        /// The same jump plus a chain of dense segments for the traversed path
        /// (only up to the event/target) for render sampling without Step.
        /// </summary>
        public LongWarpResult AdvanceToTargetWithSegments(Ship ship, double currentTimeSeconds, double targetTimeSeconds, System.Collections.Generic.List<DenseSegment> render)
        {
            if (ship == null)
            {
                throw new ArgumentNullException(nameof(ship));
            }

            if (targetTimeSeconds <= currentTimeSeconds)
            {
                return new LongWarpResult(currentTimeSeconds, null, true);
            }

            double time = currentTimeSeconds;
            while (time < targetTimeSeconds)
            {
                if (CancelRequested)
                {
                    return new LongWarpResult(time, null, false);
                }

                double remaining = targetTimeSeconds - time;
                EventOccurrence? hit = propagator.PropagateWithSegments(ship, time, remaining, render);
                if (hit.HasValue)
                {
                    return new LongWarpResult(hit.Value.TimeSeconds, hit, false);
                }

                time = targetTimeSeconds;
            }

            return new LongWarpResult(targetTimeSeconds, null, true);
        }

        /// <summary>Silent warp cancel (thrust touch): the next RHS will see it.</summary>
        public void RequestSilentCancel()
        {
            CancelRequested = true;
        }

        public void ClearCancel()
        {
            CancelRequested = false;
        }
    }
}
