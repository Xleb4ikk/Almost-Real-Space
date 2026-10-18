using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Gravity from all StarSystem bodies (star + all planets + all moons at once).
    /// A pure function of position/time: sub-step velocity and mass are ignored
    /// because gravity does not depend on them. Returns a ready acceleration
    /// (m/s²) — there is nothing to divide by mass here.
    /// </summary>
    public sealed class GravitySource : IDynamicsSource
    {
        private readonly StarSystem starSystem;

        public GravitySource(StarSystem starSystem)
        {
            this.starSystem = starSystem;
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            return DynamicsContribution.FromSpecificAcceleration(
                starSystem.EvaluateShipAcceleration(position, timeSeconds));
        }
    }
}
