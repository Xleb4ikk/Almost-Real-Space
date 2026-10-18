using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// One source of contribution to ship dynamics: gravity, thrust, atmospheric
    /// drag. Explicitly takes the integrator's SUB-STEP position/velocity/mass as
    /// separate parameters — it does NOT read them from Spacecraft directly, because
    /// at intermediate RK stages those values differ from the ship's current
    /// confirmed state. An implementation MUST be pure: it is called many times per
    /// step, including rejected steps, so it must not mutate ship or source state,
    /// accumulate telemetry or consume fuel.
    /// </summary>
    public interface IDynamicsSource
    {
        DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds);
    }
}
