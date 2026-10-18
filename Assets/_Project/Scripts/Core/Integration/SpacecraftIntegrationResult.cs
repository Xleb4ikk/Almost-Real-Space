namespace Galilego.Core
{
    /// <summary>
    /// Result of a SpacecraftOrbitIntegrator step — analogous to IntegrationResult,
    /// but carrying the full state (position, velocity, mass) instead of a
    /// Position/Velocity pair. The struct mirrors IntegrationResult so both
    /// steppers return the same result shape.
    /// </summary>
    public struct SpacecraftIntegrationResult
    {
        public SpacecraftIntegrationState State { get; }

        public SpacecraftIntegrationResult(SpacecraftIntegrationState state)
        {
            State = state;
        }
    }
}
