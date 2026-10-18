using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// propagate-to-event-and-stop driver over SpacecraftPhysics.
    /// Chunks go through SpacecraftPhysics.Step; roots are refined by bisection
    /// re-stepping from the chunk start (the event functions are geometric, so
    /// bisection is deterministic and derivative-free). P1a only detects and stops
    /// at the exact root time/state — there is no reaction.
    ///
    /// The ship mutates only on commit of an accepted chunk or truncated root:
    /// probing (chunk end, bisection midpoints) runs on temporary Spacecraft, so
    /// nothing the caller sees changes until the decision. Detectors stay pure —
    /// the driver holds the chunk's start baseline.
    ///
    /// Zero-time loop protection rests on four mechanisms: a crossing requires a
    /// STRICT sign change (g=0 at the restart point is not an event); truncation
    /// leaves the ship strictly PAST the event, so the restart sees the post side
    /// and does not reopen the same root; a planned event fires only strictly after
    /// the current time; plus a hard chunk-count cap that throws when exceeded.
    /// </summary>
    public sealed class EventDrivenPropagator
    {
        /// <summary>
        /// Fixed absolute root-localization tolerance in seconds. Absolute, not a
        /// fraction of the step: a far-future chunk becomes a warp of thousands of
        /// seconds, and the tolerance must not grow with it — an atmospheric entry
        /// after a long transfer needs the same accuracy as real time. Numerically
        /// safe: at t ~ 3e7 s the double ulp is ~7.5e-9, two orders of margin
        /// below 1e-6.
        /// </summary>
        public const double DefaultRootToleranceSeconds = 1e-6;

        public const int MaxRefineIterations = 100;

        public readonly List<ICrossingDetector> CrossingDetectors = new List<ICrossingDetector>();
        public readonly List<ITransitionDetector> TransitionDetectors = new List<ITransitionDetector>();
        public readonly List<TimedEvent> TimedEvents = new List<TimedEvent>();

        /// <summary>
        /// Upper bound of a chunk. Defaults to the integrator's MaxStepSize, not a
        /// new value: an event bracket is never wider than the internal step.
        /// </summary>
        public double MaxChunkSeconds = OrbitIntegrator.DefaultMaxStepSize;

        /// <summary>
        /// Number of subchunks each chunk is split into for detection.
        /// Grain = MaxChunkSeconds/DetectionSubdivisions; a crossing pair that
        /// falls entirely inside one subchunk is lost.
        /// </summary>
        public int DetectionSubdivisions = 20;

        public double RootToleranceSeconds = DefaultRootToleranceSeconds;

        public double HardHorizonSeconds = double.PositiveInfinity;

        private readonly SpacecraftPhysics physics;

        public EventDrivenPropagator(SpacecraftPhysics physics)
        {
            this.physics = physics ?? throw new ArgumentNullException(nameof(physics));
        }

        /// <summary>
        /// Root-refinement switch: true — bisection over the chunk's dense
        /// segments (0 reintegrations), false — legacy replay of PropagateState
        /// from the chunk start. The invariants (strict sign change, returning end
        /// b, anti-zero-time-loop) are the same on both paths; only cost and
        /// interpolant accuracy differ.
        /// </summary>
        public bool UseDenseRefinement = true;

        /// <summary>
        /// System for adaptive detection grain. null — grain is fixed by
        /// DetectionSubdivisions. When set, the subchunk count per chunk is derived
        /// from the state's characteristic time, but never below
        /// DetectionSubdivisions — finer only, never coarser.
        /// </summary>
        public StarSystem SystemForGrain { get; set; }

        /// <summary>
        /// Advances the ship forward by no more than targetDt. Returns an event —
        /// stopped exactly at the root (the ship is already in it). Returns null —
        /// reached the end with no events. Forward only: events back in time
        /// (planned in the past, detection backwards) are out of P1a scope.
        /// </summary>
        public EventOccurrence? Propagate(Ship ship, double currentTimeSeconds, double targetDt)
        {
            return PropagateWithSegments(ship, currentTimeSeconds, targetDt, null);
        }

        /// <summary>
        /// Same propagate-to-event-and-stop, plus the chain of dense segments of
        /// the traveled path for sampling the render without a single Step. The
        /// chain stops at the event: the segment covering the root is included
        /// whole. render == null — the chain is not returned; chunk segments are
        /// still collected when UseDenseRefinement is on (for bisection).
        /// </summary>
        public EventOccurrence? PropagateWithSegments(Ship ship, double currentTimeSeconds, double targetDt, List<DenseSegment> render)
        {
            if (ship == null)
                throw new ArgumentNullException(nameof(ship));
            if (targetDt < 0d)
                throw new ArgumentOutOfRangeException(nameof(targetDt), "Драйвер событий работает только вперёд.");
            if (targetDt == 0d)
                return null;
            if (MaxChunkSeconds <= 0d)
                throw new InvalidOperationException("MaxChunkSeconds обязан быть положительным.");
            if (DetectionSubdivisions < 1)
                throw new InvalidOperationException("DetectionSubdivisions обязан быть не меньше 1.");
            // Chunk-boundary invariant: actual grain = MaxChunkSeconds/DetectionSubdivisions,
            // but the guard below counts from DetectionGrain.MaxSubdivisions — fail loudly on a bad configuration.
            if (DetectionSubdivisions > DetectionGrain.MaxSubdivisions)
                throw new InvalidOperationException(
                    "DetectionSubdivisions=" + DetectionSubdivisions + " > MaxSubdivisions=" +
                    DetectionGrain.MaxSubdivisions + " — граница числа чанков считается от капа, конфигурация уйдёт в throw на варпе.");
            if (RootToleranceSeconds <= 0d)
                throw new InvalidOperationException("RootToleranceSeconds обязан быть положительным.");
            bool capped = false;
            if (currentTimeSeconds + targetDt > HardHorizonSeconds)
            {
                targetDt = HardHorizonSeconds - currentTimeSeconds;
                capped = true;
            }

            if (capped && targetDt <= OrbitIntegrator.DefaultMinStepSize)
            {
                return new EventOccurrence(currentTimeSeconds, Capture(ship), "EphemerisEnd", null, EventKind.EphemerisEnd, null);
            }

            // Long-lived accumulators are compensated: over thousands of chunks a
            // naive t += chunk drifts toward the root tolerance. The remainder is
            // compensated too: the last chunk size and the exit depend on it.
            var timeAcc = new KahanAccumulator(currentTimeSeconds);
            var remainingAcc = new KahanAccumulator(targetDt);
            double finestGrain = MaxChunkSeconds / DetectionGrain.MaxSubdivisions;
            long maxChunks = (long)Math.Ceiling(targetDt / finestGrain) + 2;
            long chunks = 0;

            while (remainingAcc.Sum > OrbitIntegrator.DefaultMinStepSize)
            {
                if (++chunks > maxChunks)
                    throw new InvalidOperationException("Исчерпана граница числа чанков — цикл детекта не сошёлся.");

                double time = timeAcc.Sum;
                double remaining = remainingAcc.Sum;
                SpacecraftIntegrationState start = Capture(ship);
                int subdiv = DetectionSubdivisions;
                if (SystemForGrain != null)
                {
                    // A0: the DetectionSubdivisions floor applies only when a surface
                    // crossing is physically possible inside the chunk. Far from bodies
                    // (pure ballistics) fine grain is unnecessary — the chunk runs whole,
                    // otherwise a distant warp pays x20 for nothing.
                    if (DetectionGrain.NeedsFineGrain(SystemForGrain, CrossingDetectors, start, time, MaxChunkSeconds))
                    {
                        double tau = DetectionGrain.CharacteristicTimeSeconds(SystemForGrain, start, time);
                        subdiv = DetectionGrain.Subdivisions(MaxChunkSeconds, DetectionSubdivisions, tau);
                    }
                    else
                    {
                        subdiv = 1;
                    }
                }

                double chunkDt = Math.Min(MaxChunkSeconds / subdiv, remaining);
                double chunkEnd = time + chunkDt;
                List<DenseSegment> seg = (UseDenseRefinement || render != null) ? new List<DenseSegment>() : null;
                SpacecraftIntegrationState end = PropagateState(start, time, chunkEnd, seg);

                TimedEvent timed = FindTimedEvent(time, chunkEnd);
                Candidate best = Candidate.None;

                for (int i = 0; i < CrossingDetectors.Count; i++)
                {
                    ICrossingDetector detector = CrossingDetectors[i];
                    double g0 = detector.Evaluate(start, time);
                    double g1 = detector.Evaluate(end, chunkEnd);
                    // A non-finite g is not "no crossing" (false in IsFiring from
                    // failed comparisons) but corrupted state: silently skipping an
                    // event is unacceptable (NaN cascade).
                    if (!double.IsFinite(g0) || !double.IsFinite(g1))
                    {
                        throw new InvalidOperationException(
                            "Детектор " + detector.GetType().Name + " получил не-конечное значение g (" +
                            g0.ToString("R") + " → " + g1.ToString("R") + ") на [" + time + "; " + chunkEnd +
                            "] — состояние корабля или тела испорчено.");
                    }

                    if (!IsFiring(detector.Direction, g0, g1))
                        continue;
                    double rootTime;
                    SpacecraftIntegrationState rootState;
                    if (UseDenseRefinement)
                        RefineCrossingDense(detector, seg, time, g0, chunkEnd, out rootTime, out rootState);
                    else
                        RefineCrossing(detector, start, time, g0, chunkEnd, out rootTime, out rootState);
                    // Only the concrete detector knows the involved body; the base
                    // interface does not promise it — fetch it via the known type.
                    OrbitingBody involved = (detector as AltitudeCrossingDetector)?.Body;
                    best = Closer(best, Candidate.ForCrossing(rootTime, rootState, detector, involved));
                }

                for (int i = 0; i < TransitionDetectors.Count; i++)
                {
                    ITransitionDetector detector = TransitionDetectors[i];
                    object key0 = detector.GetKey(start, time);
                    object key1 = detector.GetKey(end, chunkEnd);
                    if (ReferenceEquals(key0, key1))
                        continue;
                    double rootTime;
                    SpacecraftIntegrationState rootState;
                    if (UseDenseRefinement)
                        RefineTransitionDense(detector, seg, time, key1, chunkEnd, out rootTime, out rootState);
                    else
                        RefineTransition(detector, start, time, key1, chunkEnd, out rootTime, out rootState);
                    best = Closer(best, Candidate.ForTransition(rootTime, rootState, detector, key1 as OrbitingBody));
                }

                if (timed != null)
                {
                    SpacecraftIntegrationState timedState = UseDenseRefinement
                        ? DenseSegment.EvaluateTrack(seg, timed.TimeSeconds)
                        : PropagateState(start, time, timed.TimeSeconds);
                    best = Closer(best, Candidate.ForTimed(timedState, timed));
                }

                if (!best.Found)
                {
                    Commit(ship, end);
                    AppendRender(render, seg, double.PositiveInfinity);
                    timeAcc.Add(chunkDt);
                    remainingAcc.Add(-chunkDt);
                    continue;
                }

                if (best.Timed != null)
                {
                    AppendRender(render, seg, best.Time);
                    return ApplyTimedEvent(ship, best);
                }

                AppendRender(render, seg, best.Time);
                Commit(ship, best.State);
                return new EventOccurrence(best.Time, best.State, best.Name, best.Body, best.Kind, best.Source);
            }

            if (capped)
            {
                return new EventOccurrence(HardHorizonSeconds, Capture(ship), "EphemerisEnd", null, EventKind.EphemerisEnd, null);
            }

            return null;
        }

        private static void AppendRender(List<DenseSegment> render, List<DenseSegment> seg, double uptoTime)
        {
            if (render == null || seg == null)
                return;
            for (int i = 0; i < seg.Count; i++)
            {
                if (seg[i].T0 < uptoTime)
                    render.Add(seg[i]);
            }
        }

        private static bool IsFiring(EventDirection direction, double g0, double g1)
        {
            // Strict sign change: g == 0 at either end is not a crossing. This is
            // exactly what stops a restart from the root from re-finding the same event.
            if (g0 == 0d || g1 == 0d)
                return false;
            bool falling = g0 > 0d && g1 < 0d;
            bool rising = g0 < 0d && g1 > 0d;
            if (direction == EventDirection.Falling)
                return falling;
            if (direction == EventDirection.Rising)
                return rising;
            return falling || rising;
        }

        private Candidate Closer(Candidate current, Candidate contender)
        {
            if (!contender.Found)
                return current;
            if (!current.Found)
                return contender;
            // The earlier root wins; roots equal within the bisection tolerance are
            // indistinguishable, so priority decides (a smaller number ranks higher).
            if (contender.Time < current.Time - RootToleranceSeconds)
                return contender;
            if (Math.Abs(contender.Time - current.Time) <= RootToleranceSeconds && contender.Priority < current.Priority)
                return contender;
            return current;
        }

        private TimedEvent FindTimedEvent(double time, double chunkEnd)
        {
            TimedEvent winner = null;
            for (int i = 0; i < TimedEvents.Count; i++)
            {
                TimedEvent te = TimedEvents[i];
                // Strictly later than the current time: a just-processed event
                // (Time == now after a restart) must not fire again. Events in the
                // past are skipped silently — they are already consumed.
                if (te.TimeSeconds > time && te.TimeSeconds <= chunkEnd)
                {
                    if (winner == null || te.TimeSeconds < winner.TimeSeconds)
                        winner = te;
                }
            }
            return winner;
        }

        private void RefineCrossing(
            ICrossingDetector detector,
            SpacecraftIntegrationState start, double t0, double g0, double t1,
            out double rootTime, out SpacecraftIntegrationState rootState)
        {
            // Bracket invariant: g(a) has the sign of g0 (before the crossing), g(b)
            // has the sign of g1 (after). The end b is returned, NOT the midpoint:
            // truncation must leave the ship strictly PAST the event (within
            // tolerance), otherwise a restart from the root would reopen the same
            // root — the classic zero-time loop. The time error is still no worse
            // than the tolerance.
            double a = t0;
            double ga = g0;
            double b = t1;
            for (int i = 0; i < MaxRefineIterations; i++)
            {
                if (b - a <= RootToleranceSeconds)
                    break;
                double m = 0.5 * (a + b);
                SpacecraftIntegrationState sm = PropagateState(start, t0, m, null, true);
                double gm = detector.Evaluate(sm, m);
                if (gm == 0d)
                {
                    // Exact hit: g = 0 at the restart point is suppressed by the
                    // strict sign-change rule — no reopening.
                    rootTime = m;
                    rootState = sm;
                    return;
                }
                if (Math.Sign(gm) == Math.Sign(ga))
                {
                    a = m;
                    ga = gm;
                }
                else
                {
                    b = m;
                }
            }
            rootTime = b;
            rootState = PropagateState(start, t0, b, null, true);
        }

        private void RefineTransition(
            ITransitionDetector detector,
            SpacecraftIntegrationState start, double t0, object newKey, double t1,
            out double rootTime, out SpacecraftIntegrationState rootState)
        {
            // Bisection on the predicate "key is already new". One transition in the
            // bracket is assumed (locally true for SOI); a sliding case with a third
            // key inside the bracket converges only approximately — chunks are short
            // relative to the encounter geometry, acceptable for P1a. The end b
            // (new-key side) is returned — same principle as RefineCrossing: the
            // restart must see the new key in the baseline.
            double a = t0;
            double b = t1;
            for (int i = 0; i < MaxRefineIterations; i++)
            {
                if (b - a <= RootToleranceSeconds)
                    break;
                double m = 0.5 * (a + b);
                SpacecraftIntegrationState sm = PropagateState(start, t0, m, null, true);
                if (ReferenceEquals(detector.GetKey(sm, m), newKey))
                    b = m;
                else
                    a = m;
            }
            rootTime = b;
            rootState = PropagateState(start, t0, b, null, true);
        }

        /// <summary>
        /// The same bisection as RefineCrossing, but the midpoint state comes from
        /// the chunk's dense track instead of a reintegration from its start: 0
        /// integrator calls per iteration. Same invariant — the end b (post side)
        /// is returned.
        /// </summary>
        private void RefineCrossingDense(
            ICrossingDetector detector,
            List<DenseSegment> seg, double t0, double g0, double t1,
            out double rootTime, out SpacecraftIntegrationState rootState)
        {
            double a = t0;
            double ga = g0;
            double b = t1;
            for (int i = 0; i < MaxRefineIterations; i++)
            {
                if (b - a <= RootToleranceSeconds)
                    break;
                double m = 0.5 * (a + b);
                SpacecraftIntegrationState sm = DenseSegment.EvaluateTrack(seg, m);
                double gm = detector.Evaluate(sm, m);
                if (gm == 0d)
                {
                    rootTime = m;
                    rootState = sm;
                    return;
                }
                if (Math.Sign(gm) == Math.Sign(ga))
                {
                    a = m;
                    ga = gm;
                }
                else
                {
                    b = m;
                }
            }
            rootTime = b;
            rootState = DenseSegment.EvaluateTrack(seg, b);
        }

        private void RefineTransitionDense(
            ITransitionDetector detector,
            List<DenseSegment> seg, double t0, object newKey, double t1,
            out double rootTime, out SpacecraftIntegrationState rootState)
        {
            double a = t0;
            double b = t1;
            for (int i = 0; i < MaxRefineIterations; i++)
            {
                if (b - a <= RootToleranceSeconds)
                    break;
                double m = 0.5 * (a + b);
                SpacecraftIntegrationState sm = DenseSegment.EvaluateTrack(seg, m);
                if (ReferenceEquals(detector.GetKey(sm, m), newKey))
                    b = m;
                else
                    a = m;
            }
            rootTime = b;
            rootState = DenseSegment.EvaluateTrack(seg, b);
        }

        private EventOccurrence ApplyTimedEvent(Ship ship, Candidate best)
        {
            TimedEvent te = best.Timed;
            double newMass = best.State.Mass + te.DeltaMass;
            if (newMass < te.MinimumMassKg)
            {
                throw new InvalidOperationException(
                    "TimedEvent @" + te.TimeSeconds.ToString("F3") + "s: масса после сброса " +
                    newMass.ToString("F3") + " кг ниже минимума " + te.MinimumMassKg.ToString("F3") + " кг.");
            }
            var applied = new SpacecraftIntegrationState
            {
                Position = best.State.Position,
                Velocity = best.State.Velocity,
                Mass = newMass
            };
            // DeltaVelocity is intentionally NOT applied in P1a (reserved for the
            // separation impulse, see TimedEvent) — only the mass discontinuity.
            Commit(ship, applied);
            return new EventOccurrence(best.Time, applied, te.Name, null, EventKind.Timed, null);
        }

        private SpacecraftIntegrationState PropagateState(SpacecraftIntegrationState from, double fromTime, double toTime)
        {
            return PropagateState(from, fromTime, toTime, null);
        }

        private SpacecraftIntegrationState PropagateState(SpacecraftIntegrationState from, double fromTime, double toTime, List<DenseSegment> segments)
        {
            return PropagateState(from, fromTime, toTime, segments, false);
        }

        /// <summary>
        /// isProbe — refinement runs (bisection, dense refinement): they go through
        /// the same physics instance, and the moment snapshot around them is
        /// save/restored so TotalTorqueBody after Propagate reflects the accepted
        /// ship trajectory step, not the probe state. Main ship steps (chunks,
        /// engine to a timed event) run without save/restore — their substeps are
        /// the accepted ones.
        /// </summary>
        private SpacecraftIntegrationState PropagateState(
            SpacecraftIntegrationState from, double fromTime, double toTime, List<DenseSegment> segments, bool isProbe)
        {
            Vector3d savedLast = default;
            Vector3d savedTotal = default;
            if (isProbe)
            {
                physics.SuspendTorqueSnapshot(out savedLast, out savedTotal);
            }

            var probe = new Ship(from.Position, from.Velocity, from.Mass);
            physics.StepWithSegments(probe, fromTime, toTime - fromTime, segments);
            if (isProbe)
            {
                physics.RestoreTorqueSnapshot(savedLast, savedTotal);
            }

            return new SpacecraftIntegrationState
            {
                Position = probe.Position,
                Velocity = probe.Velocity,
                Mass = probe.Mass
            };
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

        private static void Commit(Ship ship, SpacecraftIntegrationState state)
        {
            ship.Position = state.Position;
            ship.Velocity = state.Velocity;
            ship.Mass = state.Mass;
        }

        private struct Candidate
        {
            public bool Found;
            public double Time;
            public int Priority;
            public SpacecraftIntegrationState State;
            public string Name;
            public OrbitingBody Body;
            public TimedEvent Timed;
            public EventKind Kind;
            public object Source;

            public static Candidate None => new Candidate { Found = false };

            public static Candidate ForCrossing(double time, SpacecraftIntegrationState state, ICrossingDetector detector, OrbitingBody body)
            {
                return new Candidate
                {
                    Found = true,
                    Time = time,
                    Priority = detector.Priority,
                    State = state,
                    Name = detector.Name,
                    Body = body,
                    Timed = null,
                    Kind = detector.Kind,
                    Source = detector
                };
            }

            public static Candidate ForTransition(double time, SpacecraftIntegrationState state, ITransitionDetector detector, OrbitingBody body)
            {
                return new Candidate
                {
                    Found = true,
                    Time = time,
                    Priority = detector.Priority,
                    State = state,
                    Name = detector.Name,
                    Body = body,
                    Timed = null,
                    Kind = detector.Kind,
                    Source = detector
                };
            }

            public static Candidate ForTimed(SpacecraftIntegrationState state, TimedEvent timed)
            {
                return new Candidate
                {
                    Found = true,
                    Time = timed.TimeSeconds,
                    Priority = timed.Priority,
                    State = state,
                    Name = timed.Name,
                    Body = null,
                    Timed = timed,
                    Kind = EventKind.Timed,
                    Source = null
                };
            }
        }
    }
}
