using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// Change of the dominant body (SOI exit/entry). A discrete event: there is no
    /// continuous zero function; the key is the OrbitingBody reference itself from
    /// StarSystem.FindDominantBody (reference comparison). Checked every chunk with no
    /// "only near the boundary" heuristics: the tree is small, correctness first — a
    /// heuristic could miss the boundary. The Body in the event is the newly dominant
    /// body (the old one is recovered by querying FindDominantBody slightly before the
    /// root, if needed).
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
