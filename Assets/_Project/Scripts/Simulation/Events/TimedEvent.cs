using Galilego.Core;

namespace Galilego.Events
{
    /// <summary>
    /// A planned event at an exact time: engine cutoff, stage mass drop. It is not
    /// detected but arrives by the clock — the driver propagates exactly to
    /// TimeSeconds and applies a discontinuity at the accepted step boundary.
    /// MinimumMassKg is checked with a loud exception, not a clamp: a silent clamp
    /// would hide a staging model bug, and the event must fail loudly.
    /// DeltaVelocity is the RELATIVE separation speed Δv_rel (m/s, vector:
    /// magnitude here, direction at the application point, usually from
    /// AttitudeState; see StageSeparation). The internal impulse acts only between
    /// the parts — no external forces — so v1 = v0 + (m2/M)·Δv_rel, v2 = v0 −
    /// (m1/M)·Δv_rel preserve total momentum exactly by construction of the
    /// coefficients. Not applied by the P1a driver (it only detects/breaks mass,
    /// creating no bodies).
    /// </summary>
    public sealed class TimedEvent
    {
        public double TimeSeconds;
        public double DeltaMass;
        public double MinimumMassKg;
        public Vector3d DeltaVelocity;

        public int Priority => EventPriorities.Planned;

        public string Name => "TimedEvent@" + TimeSeconds.ToString("F3") + "s";
    }
}
