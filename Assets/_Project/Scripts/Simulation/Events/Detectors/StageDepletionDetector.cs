using Galilego.Core;
using Galilego.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Depletion of the active stage's burn segment. The event function is:
    /// segment reserve − burned since arming. Armed BEFORE each chunk (Arm) with the
    /// current SegmentBurnKg: crossing zero marks a change in the set of firing engines
    /// (a group cut out on the pool), not necessarily a full stage cutoff. Placing the
    /// event on SegmentBurnKg (not CutoffBurnKg) is mandatory, otherwise physics would
    /// skip the thrust/mass-flow change.
    ///
    /// After the event root the owner (VesselStep) commits the actual mass via
    /// StagePropulsion.CommitBurn and re-arms the detector for the next segment.
    /// Pure: Evaluate only reads the passed state.
    /// </summary>
    public sealed class StageDepletionDetector : ICrossingDetector
    {
        private double armedMassKg;
        private double armedBurnKg;

        /// <summary>Arm before a chunk: ship mass and segment reserve right now.</summary>
        public void Arm(StagePropulsion propulsion, double shipMassKg)
        {
            armedMassKg = shipMassKg;
            armedBurnKg = propulsion != null ? propulsion.SegmentBurnKg : 0d;
        }

        public double Evaluate(SpacecraftIntegrationState state, double timeSeconds)
        {
            return armedBurnKg - (armedMassKg - state.Mass);
        }

        public EventDirection Direction { get { return EventDirection.Falling; } }

        public int Priority { get { return EventPriorities.Planned; } }

        public string Name { get { return "StageBurnSegment"; } }

        public EventKind Kind { get { return EventKind.PropellantDepleted; } }
    }
}
