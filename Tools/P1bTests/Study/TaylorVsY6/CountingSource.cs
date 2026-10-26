using Galilego.Core;
using Galilego.Spacecraft;

// Counts force evaluations of the wrapped source. The main harness defines this
// inside TestsWarpEvents.cs; the standalone Taylor-vs-Y6 project needs its own copy.
internal sealed class CountingSource : IDynamicsSource
{
    public long Calls;
    private readonly IDynamicsSource inner;

    public CountingSource(IDynamicsSource inner)
    {
        this.inner = inner;
    }

    public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
    {
        Calls++;
        return inner.Evaluate(position, velocity, mass, timeSeconds);
    }
}
