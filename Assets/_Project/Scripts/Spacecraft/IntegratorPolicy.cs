// TODO(INTEGRATOR-V2): the policy is intentionally dormant. Activation is NOT
// in this file: use SimulationRunner.EnableRegimeIntegratorPolicy /
// EnableSunCoastPolicy (copied to VesselStep in SimulationRunner.Awake).
// Keep the defaults false until the remaining integrators are ported and their
// production tests pass. Surface v1 first; Sun-coast only after a real
// VesselStep cost/event test and never for 5 AU/cislunar.
namespace Galilego.Spacecraft
{
    /// <summary>
    /// Inputs of the integrator choice for ONE propagation call. Pure data:
    /// the policy must not look at globals, time-of-day or Unity state, so a
    /// replay with the same context always picks the same integrator.
    /// </summary>
    public readonly struct IntegratorContext
    {
        /// <summary>Chunk length passed to Propagate/Step (seconds).</summary>
        public readonly double ChunkSeconds;

        /// <summary>true when the 6-DOF flight model owns this chunk (0.05 s grain).</summary>
        public readonly bool FlightModelActive;

        /// <summary>true when the current dominant body is the system root (Sun).</summary>
        public readonly bool SunDominant;

        /// <summary>
        /// true when the scene explicitly allows the Sun-coast branch. This is
        /// the allowlist that keeps 5 AU / cislunar / untested heliocentric
        /// regimes on DOP853 until their own tests pass.
        /// </summary>
        public readonly bool SunCoastAllowed;

        /// <summary>
        /// true when all non-gravity sources currently contribute zero
        /// (LongWarpDriver.CanEnterLongWarp). This is the explicit
        /// no-thrust/no-drag/no-mass-flow guard: LongWarp alone does not
        /// guarantee it.
        /// </summary>
        public readonly bool CanCoast;

        /// <summary>Altitude above the current dominant body (m).</summary>
        public readonly double AltitudeMeters;

        /// <summary>Radial speed relative to the current dominant body (m/s, + up).</summary>
        public readonly double VerticalSpeedMps;

        public IntegratorContext(
            double chunkSeconds,
            bool flightModelActive,
            bool sunDominant,
            bool sunCoastAllowed,
            bool canCoast,
            double altitudeMeters,
            double verticalSpeedMps)
        {
            ChunkSeconds = chunkSeconds;
            FlightModelActive = flightModelActive;
            SunDominant = sunDominant;
            SunCoastAllowed = sunCoastAllowed;
            CanCoast = canCoast;
            AltitudeMeters = altitudeMeters;
            VerticalSpeedMps = verticalSpeedMps;
        }
    }

    /// <summary>
    /// Per-chunk DOPRI5/DOP853 choice. The criterion is measured, not
    /// mission-typed: DOPRI5 is cheaper only when the chunk, not the tolerance,
    /// bounds the step (6 stages vs 12), and it is selected only where the
    /// tested error margin is large.
    ///
    /// v1 scope:
    ///   * surface / 6-DOF flight at the 0.05 s control grain, above the
    ///     touchdown guard;
    ///   * Sun-dominated coast with no non-gravity contributions, at any warp
    ///     (the 30-50 s live chunk and the 600 s stress chunk are both
    ///     chunk-bound there).
    /// Everything else stays DOP853 — the certified default.
    /// </summary>
    public static class IntegratorPolicy
    {
        public const double SurfaceChunkSeconds = 0.05d;
        public const double SurfaceAltitudeMeters = 10_000d;

        /// <summary>
        /// Inner heliocentric guard: below ~0.067 AU the DOPRI5 step is no
        /// longer guaranteed to be chunk-bound, so the Sun-coast branch stays
        /// off. 0.3 AU (perihelion 0.15 AU) and far flight pass; a sungrazing
        /// pass does not.
        /// </summary>
        public const double MinSunDistanceMeters = 1e10d;

        /// <summary>
        /// Below this altitude a descending ship stays on DOP853 until a
        /// dedicated touchdown event test certifies DOPRI5. Temporary guard:
        /// it keeps the event path on the known-good integrator.
        /// </summary>
        public const double TouchdownGuardMeters = 2_000d;

        public static SpacecraftIntegrator Select(in IntegratorContext context)
        {
            bool surface = context.FlightModelActive
                && context.ChunkSeconds <= SurfaceChunkSeconds + 1e-9d
                && context.AltitudeMeters < SurfaceAltitudeMeters;

            if (surface
                && !(context.VerticalSpeedMps < 0d && context.AltitudeMeters < TouchdownGuardMeters))
            {
                return SpacecraftIntegrator.Dopri5;
            }

            // Sun-dominated coast: explicit scene allowlist (5 AU/cislunar stay
            // off until their own tests pass), no non-gravity contributions,
            // and outside the inner-heliocentric guard. The 600 s and 30-50 s
            // tests both show a chunk-bound step and a large error margin.
            if (context.SunDominant
                && context.SunCoastAllowed
                && context.CanCoast
                && context.AltitudeMeters >= MinSunDistanceMeters)
            {
                return SpacecraftIntegrator.Dopri5;
            }

            return SpacecraftIntegrator.Dop853;
        }
    }
}
