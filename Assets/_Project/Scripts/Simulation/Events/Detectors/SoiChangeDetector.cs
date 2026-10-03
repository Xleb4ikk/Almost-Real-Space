using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// Смена доминирующего тела (выход/вход в SOI). Дискретное событие:
    /// непрерывной нуль-функции нет, ключ — сама ссылка OrbitingBody из
    /// StarSystem.FindDominantBody (сравнение референсное). Проверяется каждый
    /// чанк без эвристик «только рядом с границей»: дерево маленькое,
    /// correctness first — эвристика могла бы сама пропустить границу.
    /// Body в событии — ново-доминирующее тело (старое восстанавливается
    /// запросом FindDominantBody чуть раньше корня, если нужно).
    /// </summary>
    public sealed class SoiChangeDetector : ITransitionDetector
    {
        private readonly StarSystem starSystem;

        public int Priority => EventPriorities.SoiChange;

        public string Name => "SoiChange";

        public EventKind Kind => EventKind.SoiChange;

        public SoiChangeDetector(StarSystem starSystem)
        {
            this.starSystem = starSystem ?? throw new ArgumentNullException(nameof(starSystem));
        }

        public object GetKey(SpacecraftIntegrationState state, double timeSeconds)
        {
            return starSystem.FindDominantBody(state.Position, timeSeconds);
        }
    }
}
