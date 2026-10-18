namespace Galilego.Events
{
    /// <summary>
    /// Immutable input snapshot at a control tick. Passed INTO the RHS as a
    /// parameter rather than read from the live controller inside the call: the RHS
    /// must stay pure (multiple calls per step, including rejected ones), so side
    /// effects of reading input inside the derivative are forbidden. The snapshot's
    /// Throttle is consumed by ThrustSource through the ThrottleAt schedule — the
    /// snapshot update rate sets the control grain.
    /// </summary>
    public readonly struct ControlInputSnapshot
    {
        public readonly double TickTimeSeconds;
        public readonly double Throttle;

        public ControlInputSnapshot(double tickTimeSeconds, double throttle)
        {
            TickTimeSeconds = tickTimeSeconds;
            Throttle = throttle;
        }

        public static ControlInputSnapshot Idle(double tickTimeSeconds)
        {
            return new ControlInputSnapshot(tickTimeSeconds, 0d);
        }
    }

    /// <summary>
    /// Physical warp: the same RHS/integrator/event layer as at ×1 — the only
    /// difference is how much simulated time is asked to pass per real frame.
    /// The quantum Δt_control is a boundary no accepted chunk may cross while
    /// thrust is active: under physical warp control does not coarsen, input is
    /// sampled with the same simulated-time step as at ×1.
    ///
    /// The factor ladder is discrete [1, 5, 10, 100, 500, 1000, 3000] with two caps:
    /// in the atmosphere effective = min(rung, 3); thrust is allowed only at
    /// effective ≤ 3 — above that the engine is forced to zero and flight is
    /// ballistic (long warp). Long warp inside the atmosphere is impossible by
    /// construction (drag contribution → CanEnterLongWarp=false), and the ×3 cap
    /// makes physical warp with thrust legitimate there.
    /// </summary>
    public sealed class WarpController
    {
        public const double ControlTickSeconds = 0.5d;
        public const double MinWarpFactor = 1d;

        /// <summary>Discrete time-acceleration rungs.</summary>
        public static readonly double[] WarpRungs = { 1d, 5d, 10d, 100d, 500d, 1000d, 3000d };

        /// <summary>Top rung (space, no thrust — long warp).</summary>
        public const double MaxWarpFactor = 3000d;

        /// <summary>Cap on the effective factor inside the atmosphere.</summary>
        public const double AtmosphereMaxWarpFactor = 3d;

        /// <summary>Thrust is allowed only at effective ≤ this value.</summary>
        public const double ThrustMaxWarpFactor = 3d;

        public double WarpFactor { get; private set; } = 1d;

        public ControlInputSnapshot CurrentControlSnapshot { get; private set; } =
            ControlInputSnapshot.Idle(0d);

        private double nextTickBoundary = ControlTickSeconds;

        /// <summary>
        /// true — the next SampleControl must bypass the tick grid
        /// (after ResetTicks/PrepareForLongWarp/start).
        /// </summary>
        private bool snapshotPending = true;

        /// <summary>Snaps the request to the nearest ladder rung downwards (request 7 → ×5).</summary>
        public void SetWarpFactor(double factor)
        {
            double rung = WarpRungs[0];
            for (int i = 0; i < WarpRungs.Length; i++)
            {
                if (factor >= WarpRungs[i])
                {
                    rung = WarpRungs[i];
                }
            }

            WarpFactor = rung;
        }

        /// <summary>
        /// Frame effective factor: in the atmosphere any rung above the cap gives
        /// exactly AtmosphereMaxWarpFactor. The rule lives here rather than in the
        /// caller so the cap cannot be bypassed silently.
        /// </summary>
        public double EffectiveWarpFactor(bool inAtmosphere)
        {
            return inAtmosphere && WarpFactor > AtmosphereMaxWarpFactor ? AtmosphereMaxWarpFactor : WarpFactor;
        }

        /// <summary>
        /// Thrust above physical warp is fully forbidden: at effective > 3
        /// throttle is forced to 0 (rungs 5+ are ballistics only).
        /// </summary>
        public double EffectiveThrottle(double rawThrottle, double effectiveFactor)
        {
            return effectiveFactor > ThrustMaxWarpFactor ? 0d : rawThrottle;
        }

        /// <summary>
        /// Zeroes the throttle snapshot before entering long warp: CanEnterLongWarp
        /// checks the actual contribution of thrust sources, which reads the last
        /// snapshot — without zeroing, an old non-zero throttle would silently block
        /// legitimate fuel-less warp. Unconditional (not via SampleControl): the old
        /// sample must be overwritten at any time.
        /// </summary>
        public void PrepareForLongWarp(double currentTimeSeconds)
        {
            CurrentControlSnapshot = ControlInputSnapshot.Idle(currentTimeSeconds);
            nextTickBoundary = currentTimeSeconds + ControlTickSeconds;
            snapshotPending = true;
        }

        /// <summary>
        /// Updates the input snapshot at the tick boundary. Called from outside, not from the RHS.
        /// </summary>
        public void SampleControl(double currentTimeSeconds, double throttle)
        {
            if (snapshotPending || currentTimeSeconds >= nextTickBoundary)
            {
                CurrentControlSnapshot = new ControlInputSnapshot(currentTimeSeconds, throttle);
                nextTickBoundary = currentTimeSeconds + ControlTickSeconds;
                snapshotPending = false;
            }
        }

        /// <summary>
        /// Maximum size of the next chunk taking the control-tick boundary into
        /// account. Without thrust the only limit is above (maxChunk); with thrust
        /// the chunk is cut at the tick boundary and never crosses it.
        /// The side effect is part of the contract: if the boundary lags behind
        /// currentTime it is shifted right here (single-threaded sequential calls
        /// from the game loop are the only consumption pattern).
        /// </summary>
        public double GetMaxChunkSeconds(double currentTimeSeconds, double maxChunk, bool thrustActive)
        {
            if (!thrustActive)
            {
                return maxChunk;
            }

            double untilTick = nextTickBoundary - currentTimeSeconds;
            if (untilTick <= 0d)
            {
                untilTick = ControlTickSeconds;
                nextTickBoundary = currentTimeSeconds + ControlTickSeconds;
            }

            return untilTick < maxChunk ? untilTick : maxChunk;
        }

        public void ResetTicks(double currentTimeSeconds)
        {
            nextTickBoundary = currentTimeSeconds + ControlTickSeconds;
            CurrentControlSnapshot = ControlInputSnapshot.Idle(currentTimeSeconds);
            snapshotPending = true;
        }
    }
}
