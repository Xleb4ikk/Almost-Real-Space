using Galilego.Core;

namespace Galilego.Events
{
    /// <summary>
    /// Исчерпание топлива — событие ПО СОСТОЯНИЮ, а не по времени: масса
    /// пересекает порог сухой массы сверху вниз (g = mass − dryMass, Falling).
    /// Структурно то же, что AltitudeCrossingDetector.Touchdown, только ключ —
    /// масса, а не высота. Покрывает случай, который TimedEvent покрыть не
    /// может: игрок держит газ, а топливо кончается раньше расчётного момента.
    /// Pure, как остальные детекторы. Приоритет — Planned: порядок при
    /// совпадении корней во времени решает время (ранний побеждает), а не вес.
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
