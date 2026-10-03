using System;
using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Состояние вращения: ориентация (body→world) + угловая скорость в body-frame.
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
    /// Источник момента для степпера вращения. Чистая функция (многократные
    /// вызовы на шаг): состояние читается параметрами, мутаций нет.
    /// </summary>
    /// <param name="attitude">Ориентация body→world.</param>
    /// <param name="angularVelocityBody">Угловая скорость в body-frame.</param>
    public delegate Vector3d TorqueFunction(QuaternionD attitude, Vector3d angularVelocityBody, double timeSeconds);

    /// <summary>
    /// Вращение твёрдого тела: уравнения Эйлера ω̇=I⁻¹(τ−ω×Iω) + кинематика
    /// q̇=½q⊗ω, классический RK4 с фиксированным шагом (дефолт 0.02с) и
    /// нормализацией кватерниона каждый шаг.
    ///
    /// Отдельный степпер (не SpacecraftOrbitIntegrator) — осознанно, а не
    /// срезание угла: сила и момент несвязаны (нет зависимости ускорения от
    /// ориентации внутри RK-подстадий, в отличие от массы, которая обязана была
    /// войти в тот же вектор состояния). Плотная синхронизация подшагов
    /// орбиты и вращения не нужна: вращение сэмплирует медленную орбитальную
    /// динамику, орбита от вращения не зависит вовсе (пока нет аэродинамики
    /// с моментом — тогда пересмотреть, TODO-узел зафиксирован).
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
        /// Шаг вращения от ПОСТОЯННОГО за шаг момента — того же снимка, что
        /// throttle-управление: SpacecraftPhysics.TotalTorqueBody после
        /// phys.Step. Сила+момент из одного источника: снимок τ вычислен в
        /// EvaluateDerivative орбитального пути на зерне шага. τ держится
        /// константой все подстадии — та же семантика зерна, что у тяги (T2).
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
