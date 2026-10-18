using Galilego.Core;

namespace Galilego.Events
{
    /// <summary>
    /// Propellant depletion is a STATE event, not a time event: mass crosses the
    /// dry-mass threshold top to bottom (g = mass − dryMass, Falling). Structurally
    /// the same as AltitudeCrossingDetector.Touchdown, just keyed on mass instead of
    /// altitude. Covers a case TimedEvent cannot: the player holds the throttle and
    /// fuel runs out earlier than planned. Pure, like the other detectors. Priority is
    /// Planned: on simultaneous roots in time, time decides (the earlier wins), not weight.
    /// </summary>
    public sealed class PropellantDepletionDetector : ICrossingDetector
    {
        private readonly double dryMassKg;

        public EventDirection Direction => EventDirection.Falling;

        public int Priority => EventPriorities.Planned;

        public string Name { get; }

        public EventKind Kind => EventKind.PropellantDepleted;

        public double DryMassKg => dryMassKg;

        public PropellantDepletionDetector(double dryMassKg, string tankName)
        {
            this.dryMassKg = dryMassKg;
            Name = "PropellantDepleted:" + (tankName ?? "tank");
        }

        public double Evaluate(SpacecraftIntegrationState state, double timeSeconds)
        {
            return state.Mass - dryMassKg;
        }
    }
}
