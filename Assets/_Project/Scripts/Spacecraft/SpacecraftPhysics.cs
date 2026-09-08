using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Продвигает Spacecraft во времени через SpacecraftOrbitIntegrator (тот же
    /// DOPRI5, что и Core.OrbitIntegrator, но над полным состоянием
    /// (позиция, скорость, масса)). Суммирует вклады всех зарегистрированных
    /// IDynamicsSource: готовые ускорения (гравитация) плюс силы (тяга,
    /// сопротивление), поделённые на массу ПОДШАГА — один раз, в одном месте.
    /// Масса эволюционирует внутри интегратора как dm/dt = -расход, синхронно
    /// с позицией/скоростью на каждой RK-подстадии, а не отдельным Update()
    /// после шага: менять её постфактум значило бы считать все подстадии шага
    /// по устаревшей массе и потерять формальный порядок точности метода.
    ///
    /// EvaluateDerivative — pure: вызывается много раз на шаг, включая
    /// отклонённые шаги, поэтому только суммирует вклады и возвращает
    /// производную. Мутация корабля — лишь после принятого шага, в Step.
    /// </summary>
    public sealed class SpacecraftPhysics
    {
        public readonly List<IDynamicsSource> Sources = new List<IDynamicsSource>();

        // Раздельные допуски по величинам: позицию меряем в метрах, скорость
        // в м/с, массу в кг — один общий atol для всех трёх был бы физически
        // бессмысленным. Ориентиры (донастроить по игровому опыту):
        // позиция ~1e-3..1e-1 м, скорость ~1e-6..1e-4 м/с,
        // масса ~1e-6..1e-3 кг, rtol ~1e-9..1e-11.
        public double PositionAbsoluteTolerance = 1e-2;
        public double VelocityAbsoluteTolerance = 1e-5;
        public double MassAbsoluteTolerance = 1e-4;
        public double RelativeTolerance = 1e-10;

        /// <summary>
        /// Снимок суммарного момента (Н·м, body-frame) последней подстадии
        /// последнего ПРИНЯТОГО шага. Канал оживлён (аудит S3): момент всех
        /// IDynamicsSource суммируется рядом с силой в EvaluateDerivative,
        /// и сила+момент идут из одного источника — игровой цикл читает снимок
        /// после phys.Step и кормит AttitudePhysics.Step с зерном шага
        /// (та же семантика управления, что throttle-снимок). НЕ производная
        /// для интегрирования вращения: EvaluateDerivative зовётся много раз
        /// на шаг (включая отклонённые), снимок — только управление.
        /// </summary>
        public Vector3d TotalTorqueBody { get; private set; }

        private Vector3d lastEvaluatedTorque = Vector3d.Zero;

        /// <summary>
        /// Save/restore снимка вокруг probe-шагов драйвера событий: бисекция
        /// и dense-погоны идут через тот же physics-экземпляр и без
        /// save/restore оставляли бы после Propagate снимок от probe-состояния
        /// (близко, но не то состояние, где реально остался корабль).
        /// internal — инструмент драйвера, игровой цикл читает только
        /// TotalTorqueBody.
        /// </summary>
        internal void SuspendTorqueSnapshot(out Vector3d lastEvaluated, out Vector3d total)
        {
            lastEvaluated = lastEvaluatedTorque;
            total = TotalTorqueBody;
        }

        internal void RestoreTorqueSnapshot(Vector3d lastEvaluated, Vector3d total)
        {
            lastEvaluatedTorque = lastEvaluated;
            TotalTorqueBody = total;
        }

        public void Step(Spacecraft ship, double currentTimeSeconds, double dt)
        {
            StepWithSegments(ship, currentTimeSeconds, dt, null);
        }

        /// <summary>
        /// Тот же шаг, плюс dense output принятых внутренних шагов в segments
        /// (null — не собирать, поведение бит-в-бит как Step).
        /// </summary>
        public void StepWithSegments(Spacecraft ship, double currentTimeSeconds, double dt, System.Collections.Generic.IList<DenseSegment> segments)
        {
            var initialState = new SpacecraftIntegrationState
            {
                Position = ship.Position,
                Velocity = ship.Velocity,
                Mass = ship.Mass
            };

            SpacecraftIntegrationResult result = SpacecraftOrbitIntegrator.StepForward(
                initialState,
                currentTimeSeconds,
                dt,
                EvaluateDerivative,
                PositionAbsoluteTolerance,
                VelocityAbsoluteTolerance,
                MassAbsoluteTolerance,
                RelativeTolerance,
                segments);

            ship.Position = result.State.Position;
            ship.Velocity = result.State.Velocity;
            ship.Mass = result.State.Mass;
            TotalTorqueBody = lastEvaluatedTorque;
        }

        private SpacecraftIntegrationState EvaluateDerivative(SpacecraftIntegrationState state, double timeSeconds)
        {
            // Масса ≤ 0 или не-конечная — деление даст NaN/Inf, который расползётся
            // молча; громко здесь (аудит S1/F4). Источник, сводящий массу в минус,
            // получит громкий отказ на первом шаге.
            if (!(state.Mass > 0d) || !double.IsFinite(state.Mass))
            {
                throw new InvalidOperationException(
                    "Масса корабля " + state.Mass.ToString("R") + " кг на t=" + timeSeconds.ToString("R") +
                    " — интеграция невозможна (источник расходует больше, чем есть?).");
            }

            Vector3d totalSpecificAcceleration = Vector3d.Zero;
            Vector3d totalForce = Vector3d.Zero;
            Vector3d totalTorque = Vector3d.Zero;
            double totalMassFlow = 0d;

            for (int i = 0; i < Sources.Count; i++)
            {
                DynamicsContribution c = Sources[i].Evaluate(state.Position, state.Velocity, state.Mass, timeSeconds);
                totalSpecificAcceleration += c.SpecificAcceleration;
                totalForce += c.Force;
                totalTorque += c.Torque;
                totalMassFlow += c.MassFlow;
            }

            if (!totalForce.IsFinite || !totalSpecificAcceleration.IsFinite || !double.IsFinite(totalMassFlow) || !totalTorque.IsFinite)
            {
                throw new InvalidOperationException(
                    "Источник динамики вернул не-конечный вклад на t=" + timeSeconds.ToString("R") + ".");
            }

            lastEvaluatedTorque = totalTorque;
            Vector3d acceleration = totalSpecificAcceleration + (totalForce / state.Mass);

            return new SpacecraftIntegrationState
            {
                Position = state.Velocity,
                Velocity = acceleration,
                Mass = -totalMassFlow
            };
        }
    }
}
