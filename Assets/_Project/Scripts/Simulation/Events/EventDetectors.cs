using Galilego.Core;

namespace Galilego.Events
{
    /// <summary>
    /// Fixed event priorities: a smaller number is higher priority. The earlier root
    /// wins; roots that coincide within the bisection tolerance are resolved by
    /// priority. The order reflects physical severity: first what destroys the ship
    /// or irreversibly changes the flight regime.
    /// </summary>
    public static class EventPriorities
    {
        public const int Touchdown = 0;
        public const int Atmosphere = 10;
        public const int SoiChange = 20;
        public const int Planned = 30;
    }

    /// <summary>
    /// Typed event kind. The reaction table (EventReactions) is keyed by Kind, not by
    /// the Name string: parsing names is forbidden. A new event means a new enum member
    /// plus a reaction branch; silently reusing someone else's is not allowed.
    /// </summary>
    public enum EventKind
    {
        Touchdown,
        AtmosphereEntry,
        AtmosphereExit,
        SoiChange,
        PropellantDepleted,
        Timed,
        EphemerisEnd
    }

    /// <summary>
    /// Continuous event: a zero of a continuous state function. The implementation
    /// MUST be pure (like the integrator RHS): it is called many times, including
    /// bisection re-runs and rejected steps — it must not mutate the ship, the sources
    /// or the detector itself (the baseline is held by the calling driver, not the detector).
    /// </summary>
    public interface ICrossingDetector
    {
        double Evaluate(SpacecraftIntegrationState state, double timeSeconds);

        EventDirection Direction { get; }

        int Priority { get; }

        string Name { get; }

        EventKind Kind { get; }
    }

    /// <summary>
    /// Discrete event: a change of key (e.g. the dominant body) between two points.
    /// There is no continuous zero function here, so the root is refined by bisection
    /// on the boolean predicate "key already new". The key must be a stable reference
    /// (comparison is reference-based): for an SOI change it is the OrbitingBody itself.
    /// GetKey is pure, like Evaluate above.
    /// </summary>
    public interface ITransitionDetector
    {
        object GetKey(SpacecraftIntegrationState state, double timeSeconds);

        int Priority { get; }

        string Name { get; }

        EventKind Kind { get; }
    }
}
