using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// A recorded event: propagate-to-event-and-stop. P1a only detects and stops
    /// at the exact root time/state — no reaction (aero, landing, warp) here, that
    /// is P1b's job. Body — involved body (for geometric events), null for planned
    /// ones. Kind — typed kind for the reaction table (parsing Name is forbidden).
    /// SourceDetector — source detector (reaction context: dry mass, body); null
    /// only for TimedEvent and EphemerisEnd.
    /// </summary>
    public readonly struct EventOccurrence
    {
        public readonly double TimeSeconds;
        public readonly SpacecraftIntegrationState State;
        public readonly string DetectorName;
        public readonly OrbitingBody Body;
        public readonly EventKind Kind;
        public readonly object SourceDetector;

        public EventOccurrence(double timeSeconds, SpacecraftIntegrationState state, string detectorName, OrbitingBody body, EventKind kind, object sourceDetector)
        {
            TimeSeconds = timeSeconds;
            State = state;
            DetectorName = detectorName;
            Body = body;
            Kind = kind;
            SourceDetector = sourceDetector;
        }
    }
}
