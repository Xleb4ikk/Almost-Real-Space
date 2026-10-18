using Galilego.Core;
using Galilego.Spacecraft;

namespace Galilego.Flight
{
    /// <summary>
    /// 6-DOF dynamics source: takes the force, torque and flow computed by the model per chunk
    /// (Prepare), rotates the force by the attitude snapshot. Pure: reads only.
    /// Replaces StageThrustSource when the ship has a flight model.
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
