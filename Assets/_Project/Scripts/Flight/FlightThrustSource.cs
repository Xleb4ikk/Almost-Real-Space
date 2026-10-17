using Galilego.Core;
using Galilego.Spacecraft;

namespace Galilego.Flight
{
    /// <summary>
    /// Источник динамики 6-DOF: берёт силу, момент и расход, посчитанные моделью на чанк
    /// (Prepare), поворачивает силу снимком ориентации. Pure: только читает.
    /// Заменяет StageThrustSource, когда у корабля есть модель полёта.
    /// </summary>
    public sealed class FlightThrustSource : IDynamicsSource
    {
        public VesselFlightModel Model;
        public QuaternionD Attitude = QuaternionD.Identity;

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            VesselFlightModel m = Model;
            if (m == null || !m.AnyThrust)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            return new DynamicsContribution(
                Vector3d.Zero, Attitude.Normalized.Rotate(m.ForceBody), m.FlowTotal, m.TorqueBody);
        }
    }
}
