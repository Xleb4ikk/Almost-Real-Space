using System;
using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Stage stack thrust as IDynamicsSource: takes thrust and mass flow from the
    /// active StagePropulsion stage (engines fire simultaneously, cut out per core
    /// pool). Pure: only reads the model; fuel consumption is CommitBurn after an
    /// ACCEPTED step, not here.
    ///
    /// Direction as in ThrustSource: attitude snapshot (body frame) or the legacy
    /// world field; throttle is the ThrottleAt(t) schedule 0..1 (clamped). At zero
    /// throttle (or with no lit stage) the contribution is exactly zero — a required
    /// condition for long warp (CanEnterLongWarp scans source contributions).
    /// </summary>
    public sealed class StageThrustSource : IDynamicsSource
    {
        /// <summary>Stack model (shared with physics and the panel).</summary>
        public StagePropulsion Model;

        public Vector3d ThrustDirection = new Vector3d(1d, 0d, 0d);
        public Vector3d BodyThrustDirection = new Vector3d(0d, 0d, 1d);
        public QuaternionD? AttitudeSnapshot;

        /// <summary>Throttle schedule over time 0..1. null = cut (0).</summary>
        public Func<double, double> ThrottleAt;

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            double throttle = ThrottleAt != null ? ThrottleAt(timeSeconds) : 0d;
            if (throttle < 0d)
            {
                throttle = 0d;
            }
            else if (throttle > 1d)
            {
                throttle = 1d;
            }

            StagePropulsion model = Model;
            if (throttle <= 0d || model == null)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            double thrustMax = model.ThrustN;
            double flowMax = model.MassFlowKgS;
            if (thrustMax <= 0d || flowMax <= 0d)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            Vector3d worldDirection = AttitudeSnapshot.HasValue
                ? AttitudeSnapshot.Value.Normalized.Rotate(BodyThrustDirection).Normalized
                : ThrustDirection.Normalized;
            return new DynamicsContribution(Vector3d.Zero, worldDirection * (throttle * thrustMax),
                throttle * flowMax, Vector3d.Zero);
        }
    }
}
