using System;
using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Тяга ступенчатой установки как IDynamicsSource: берёт тягу и расход у
    /// активной ступени StagePropulsion (двигатели работают одновременно,
    /// глохнут по пулам самих ядер). Pure: только читает модель; списание
    /// топлива — CommitBurn после ПРИНЯТОГО шага, не здесь.
    ///
    /// Направление — как у ThrustSource: снимок ориентации (body-frame) или
    /// legacy мировое поле; газ — расписание ThrottleAt(t) 0..1 (клампится).
    /// При нулевом газе (или без зажжённой ступени) вклад ровно нулевой —
    /// это обязательное условие дальнего варпа (CanEnterLongWarp сканирует
    /// вклад источников).
    /// </summary>
    public sealed class StageThrustSource : IDynamicsSource
    {
        /// <summary>Модель установки (общая с физикой и панелью).</summary>
        public StagePropulsion Model;

        public Vector3d ThrustDirection = new Vector3d(1d, 0d, 0d);
        public Vector3d BodyThrustDirection = new Vector3d(0d, 0d, 1d);
        public QuaternionD? AttitudeSnapshot;

        /// <summary>Расписание газа по времени 0..1. null = заглушено (0).</summary>
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
