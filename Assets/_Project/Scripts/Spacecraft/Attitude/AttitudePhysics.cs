using System;
using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Rotation state: attitude (body→world) + angular velocity in the body frame.
    /// </summary>
    public readonly struct AttitudeState
    {
        public readonly QuaternionD Attitude;
        public readonly Vector3d AngularVelocity;

        public AttitudeState(QuaternionD attitude, Vector3d angularVelocity)
        {
            Attitude = attitude;
            AngularVelocity = angularVelocity;
        }
    }

    /// <summary>
    /// Torque source for the rotation stepper. Pure function (called multiple
    /// times per step): state is read via parameters, no mutation.
    /// </summary>
    /// <param name="attitude">Attitude body→world.</param>
    /// <param name="angularVelocityBody">Angular velocity in the body frame.</param>
    public delegate Vector3d TorqueFunction(QuaternionD attitude, Vector3d angularVelocityBody, double timeSeconds);

    /// <summary>
    /// Rigid-body rotation: Euler's equations ω̇=I⁻¹(τ−ω×Iω) plus kinematics
    /// q̇=½q⊗ω, classic fixed-step RK4 (default 0.02 s) with quaternion
    /// normalisation every step.
    ///
    /// A separate stepper (not SpacecraftOrbitIntegrator): rotation samples the slow
    /// orbital dynamics, while the orbit does not depend on rotation. TODO: revisit
    /// once there is aerodynamic torque.
    /// </summary>
    public static class AttitudePhysics
    {
        public const double DefaultStepSeconds = 0.02d;

        public static AttitudeState Step(AttitudeState state, Matrix3x3 inertia, TorqueFunction torque, double timeSeconds, double dt)
        {
            if (torque == null)
                throw new ArgumentNullException(nameof(torque));
            if (dt <= 0d)
                throw new ArgumentOutOfRangeException(nameof(dt), "Шаг вращения обязан быть положительным.");

            return StepCore(state, inertia, torque, timeSeconds, dt);
        }

        /// <summary>
        /// Rotation step from a torque CONSTANT over the step — the same snapshot
        /// as throttle control: SpacecraftPhysics.TotalTorqueBody after phys.Step.
        /// τ is held constant over all sub-steps, the same seed semantics as thrust.
        /// </summary>
        public static AttitudeState Step(AttitudeState state, Matrix3x3 inertia, Vector3d constantTorqueBody, double timeSeconds, double dt)
        {
            return StepCore(state, inertia, (q, w, t) => constantTorqueBody, timeSeconds, dt);
        }

        private static AttitudeState StepCore(AttitudeState state, Matrix3x3 inertia, TorqueFunction torque, double timeSeconds, double dt)
        {
            Matrix3x3 inverse = inertia.Inverse();
            QuaternionD q0 = state.Attitude.Normalized;
            Vector3d w0 = state.AngularVelocity;

            QuaternionD kq1 = QuaternionD.Derivative(q0, w0);
            Vector3d kw1 = Accelerate(w0, torque(q0, w0, timeSeconds), inertia, inverse);

            QuaternionD q2 = (q0 + (kq1 * (0.5d * dt))).Normalized;
            Vector3d w2 = w0 + (kw1 * (0.5d * dt));
            QuaternionD kq2 = QuaternionD.Derivative(q2, w2);
            Vector3d kw2 = Accelerate(w2, torque(q2, w2, timeSeconds + (0.5d * dt)), inertia, inverse);

            QuaternionD q3 = (q0 + (kq2 * (0.5d * dt))).Normalized;
            Vector3d w3 = w0 + (kw2 * (0.5d * dt));
            QuaternionD kq3 = QuaternionD.Derivative(q3, w3);
            Vector3d kw3 = Accelerate(w3, torque(q3, w3, timeSeconds + (0.5d * dt)), inertia, inverse);

            QuaternionD q4 = (q0 + (kq3 * dt)).Normalized;
            Vector3d w4 = w0 + (kw3 * dt);
            QuaternionD kq4 = QuaternionD.Derivative(q4, w4);
            Vector3d kw4 = Accelerate(w4, torque(q4, w4, timeSeconds + dt), inertia, inverse);

            QuaternionD qNext = (q0 + ((kq1 + (kq2 * 2d) + (kq3 * 2d) + kq4) * (dt / 6d))).Normalized;
            Vector3d wNext = w0 + ((kw1 + (kw2 * 2d) + (kw3 * 2d) + kw4) * (dt / 6d));
            return new AttitudeState(qNext, wNext);
        }

        public static Vector3d AngularAcceleration(Vector3d angularVelocity, Vector3d torque, Matrix3x3 inertia)
        {
            return inertia.Inverse() * (torque - Vector3d.Cross(angularVelocity, inertia * angularVelocity));
        }

        private static Vector3d Accelerate(Vector3d angularVelocity, Vector3d torque, Matrix3x3 inertia, Matrix3x3 inverse)
        {
            return inverse * (torque - Vector3d.Cross(angularVelocity, inertia * angularVelocity));
        }
    }
}
