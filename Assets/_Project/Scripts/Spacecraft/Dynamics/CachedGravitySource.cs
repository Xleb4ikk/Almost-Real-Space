using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Гравитация через кэш позиций подстадии StarSystem (по паре
    /// тело×время-подстадии внутри одного RHS-вызова). Математика бит-в-бит
    /// совпадает с GravitySource: та же сумма μ/r³, тот же численный пол.
    /// Разница только в числе вызовов EvaluateWorldState: наивный путь
    /// пересчитывает родителя рекурсией для каждого потомка, кэш — один раз.
    /// Pure, как требует IDynamicsSource.
    /// </summary>
    public sealed class CachedGravitySource : IDynamicsSource
    {
        private readonly StarSystem starSystem;

        public CachedGravitySource(StarSystem starSystem)
        {
            this.starSystem = starSystem ?? throw new System.ArgumentNullException(nameof(starSystem));
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            return DynamicsContribution.FromSpecificAcceleration(
                starSystem.EvaluateShipAccelerationCached(position, timeSeconds));
        }
    }
}
