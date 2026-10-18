using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Gravity via StarSystem's sub-step position cache (keyed by body × sub-step
    /// time within one RHS call). The maths is bit-for-bit identical to GravitySource:
    /// the same μ/r³ sum, the same numerical floor. The only difference is the number
    /// of EvaluateWorldState calls: the naive path recomputes the parent recursively
    /// for every child, the cache does it once. Pure, as IDynamicsSource requires.
    /// </summary>
    public sealed class CachedGravitySource : IDynamicsSource
    {
        private readonly StarSystem starSystem;

        public CachedGravitySource(StarSystem starSystem)
        {
            this.starSystem = starSystem ?? throw new System.ArgumentNullException(nameof(starSystem));
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            return DynamicsContribution.FromSpecificAcceleration(
                starSystem.EvaluateShipAccelerationCached(position, timeSeconds));
        }
    }
}
