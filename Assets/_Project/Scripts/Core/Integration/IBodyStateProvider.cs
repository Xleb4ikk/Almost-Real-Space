namespace Galilego.Core
{
    /// <summary>
    /// World-state provider used by dense segments integrated in a body-local
    /// frame. Implemented by the universe body model, so Core depends on this
    /// interface instead of the concrete body type.
    /// </summary>
    public interface IBodyStateProvider
    {
        void EvaluateWorldState(double timeSeconds, out Vector3d position, out Vector3d velocity);
    }
}
