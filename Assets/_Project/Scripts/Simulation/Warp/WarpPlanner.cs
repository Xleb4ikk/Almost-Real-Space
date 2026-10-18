using System;

namespace Galilego.Events
{
    /// <summary>Frame decision: how much simulated time to advance and by which layer.</summary>
    public readonly struct WarpFrameDecision
    {
        /// <summary>Effective factor after the atmosphere cap.</summary>
        public double EffectiveFactor { get; }

        /// <summary>Simulated seconds for this frame (effective × realDt).</summary>
        public double SimSeconds { get; }

        /// <summary>true — long warp (LongWarpDriver, ballistics, thrust forbidden);
        /// false — physical chain (Propagate/SpacecraftPhysics, thrust allowed).</summary>
        public bool UseLongWarp { get; }

        public WarpFrameDecision(double effectiveFactor, double simSeconds, bool useLongWarp)
        {
            EffectiveFactor = effectiveFactor;
            SimSeconds = simSeconds;
            UseLongWarp = useLongWarp;
        }
    }

    /// <summary>
    /// Pure accelerated-time frame planner: effective factor (the atmosphere cap
    /// lives in WarpController) → simulated seconds → execution layer. Thrust is
    /// allowed only at effective ≤ ThrustMaxWarpFactor; higher rungs are ballistics
    /// (LongWarpDriver), where CanEnterLongWarp loudly guards against forgotten
    /// thrust. horizonSeconds caps at the end of the world
    /// (BakedEndSeconds − t): past the end a frame simply does not fly.
    /// </summary>
    public static class WarpPlanner
    {
        public static WarpFrameDecision ComputeFrame(WarpController warp, bool inAtmosphere, double realDtSeconds, double horizonSeconds)
        {
            if (warp == null)
            {
                throw new ArgumentNullException(nameof(warp));
            }

            if (!(realDtSeconds > 0d) || !double.IsFinite(realDtSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(realDtSeconds),
                    "realDtSeconds обязан быть конечным и положительным, получено " + realDtSeconds + ".");
            }

            double effective = warp.EffectiveWarpFactor(inAtmosphere);
            double sim = effective * realDtSeconds;
            bool useLongWarp = effective > WarpController.ThrustMaxWarpFactor;

            if (double.IsFinite(horizonSeconds) && sim > horizonSeconds)
            {
                sim = Math.Max(0d, horizonSeconds);
            }

            return new WarpFrameDecision(effective, sim, useLongWarp);
        }
    }
}
