namespace Galilego.Core
{
    /// <summary>
    /// Результат шага SpacecraftOrbitIntegrator — аналог IntegrationResult,
    /// но с полным состоянием (позиция, скорость, масса) вместо пары
    /// Position/Velocity. Структура повторяет IntegrationResult, чтобы оба
    /// степпера отдавали одинаковую форму результата.
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
