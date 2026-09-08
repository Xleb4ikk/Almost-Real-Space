using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Гравитация от всех тел StarSystem (звезда + все планеты + все спутники
    /// разом). Чистая функция позиции/времени: скорость и масса подшага
    /// игнорируются, потому что гравитация от них не зависит. Возвращает
    /// готовое ускорение (м/с²) — делить на массу здесь нечего.
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
