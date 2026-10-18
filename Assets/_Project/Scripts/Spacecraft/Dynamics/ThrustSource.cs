using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Specific-impulse model. Extracted into an interface because constant-Isp
    /// (checked against Tsiolkovsky) and altitude interpolation of sea-level/vacuum
    /// (needed for a surface launch through the atmosphere) are two different
    /// assumptions, and the choice must be visible in code, not silently hard-wired
    /// inside the thrust source.
    /// </summary>
    public interface IIspModel
    {
        double GetSpecificImpulseSeconds(Vector3d position, double timeSeconds);
    }

    /// <summary>
    /// Constant Isp. Exactly what is checked against the Tsiolkovsky analytics.
    /// </summary>
    public sealed class ConstantIsp : IIspModel
    {
        public double SpecificImpulseSeconds;

        public ConstantIsp(double specificImpulseSeconds)
        {
            SpecificImpulseSeconds = specificImpulseSeconds;
        }

        public double GetSpecificImpulseSeconds(Vector3d position, double timeSeconds)
        {
            return SpecificImpulseSeconds;
        }
    }

    /// <summary>
    /// Isp interpolation by pressure: Isp(h) = Ivac − (Ivac − Isl)·(p(h)/p0),
    /// p(h)/p0 = exp(−h/H) from the body's AtmosphereProfile. At ground (h≤0) exactly
    /// sea-level, high up asymptotically vacuum, monotone between. A body without
    /// an atmosphere or with incomplete data gives pure vacuum, without exceptions.
    /// </summary>
    public sealed class AltitudeInterpolatedIsp : IIspModel
    {
        private readonly OrbitingBody body;
        private readonly double seaLevelIspSeconds;
        private readonly double vacuumIspSeconds;

        public AltitudeInterpolatedIsp(OrbitingBody body, double seaLevelIspSeconds, double vacuumIspSeconds)
        {
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            this.seaLevelIspSeconds = seaLevelIspSeconds;
            this.vacuumIspSeconds = vacuumIspSeconds;
        }

        public double GetSpecificImpulseSeconds(Vector3d position, double timeSeconds)
        {
            AtmosphereProfile atmosphere = body.Atmosphere;
            if (atmosphere == null || atmosphere.ScaleHeightMeters <= 0d || body.Radius <= 0d)
            {
                return vacuumIspSeconds;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            double altitude = (position - bodyPosition).Magnitude - body.Radius;
            if (altitude <= 0d)
            {
                return seaLevelIspSeconds;
            }

            double pressureRatio = Math.Exp(-altitude / atmosphere.ScaleHeightMeters);
            return vacuumIspSeconds - ((vacuumIspSeconds - seaLevelIspSeconds) * pressureRatio);
        }
    }

    /// <summary>
    /// Chemical/electric engine as IDynamicsSource:
    /// Force = throttle·Tmax·dir, MassFlow = throttle·Tmax/(Isp·g0).
    /// Throttle comes from the ThrottleAt(t) schedule 0..1 (clamped) — the same way
    /// WarpController samples input per control tick, so physics at ×3 matches ×1.
    /// Direction is inertial, normalised on use. Pure: only reads fields, no mutation.
    ///
    /// Dry mass is a hard boundary: at mass ≤ DryMassKg the contribution is zero
    /// (engine cut out, fuel exhausted). The depletion moment is caught NOT here but
    /// by the state event PropellantDepletionDetector (mass crosses the threshold
    /// top-down): inside RK sub-steps the integrator may look slightly below the
    /// threshold; those are trial states, the driver commits at the root.
    /// </summary>
    public sealed class ThrustSource : IDynamicsSource
    {
        public const double StandardGravity = 9.80665d;

        public double ThrustMaxNewtons;
        public double DryMassKg;
        public Vector3d ThrustDirection = new Vector3d(1d, 0d, 0d);

        /// <summary>
        /// Thrust direction in the body frame ("along the nose", default +Z). Active
        /// only when AttitudeSnapshot is set: world direction = snapshot(bodyDir).
        /// Without a snapshot the legacy ThrustDirection field acts as world direction;
        /// the two are never mixed — snapshot present → body frame, absent → world field.
        /// The snapshot is read as-is (state at the start of the step, the
        /// ControlInputSnapshot pattern): recomputing per RK sub-step is unnecessary —
        /// rotation over an inner step is negligible.
        /// </summary>
        public Vector3d BodyThrustDirection = new Vector3d(0d, 0d, 1d);

        public QuaternionD? AttitudeSnapshot;

        /// <summary>Throttle schedule over time 0..1. null = cut (0).</summary>
        public Func<double, double> ThrottleAt;

        public IIspModel IspModel;

        public ThrustSource(double thrustMaxNewtons, double dryMassKg, IIspModel ispModel)
        {
            ThrustMaxNewtons = thrustMaxNewtons;
            DryMassKg = dryMassKg;
            IspModel = ispModel ?? throw new ArgumentNullException(nameof(ispModel));
        }

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

            if (throttle <= 0d || mass <= DryMassKg || ThrustMaxNewtons <= 0d)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            double isp = IspModel.GetSpecificImpulseSeconds(position, timeSeconds);
            if (isp <= 0d)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            double thrust = throttle * ThrustMaxNewtons;
            double massFlow = thrust / (isp * StandardGravity);
            Vector3d worldDirection = AttitudeSnapshot.HasValue
                ? AttitudeSnapshot.Value.Normalized.Rotate(BodyThrustDirection).Normalized
                : ThrustDirection.Normalized;
            return new DynamicsContribution(Vector3d.Zero, worldDirection * thrust, massFlow, Vector3d.Zero);
        }
    }
}
