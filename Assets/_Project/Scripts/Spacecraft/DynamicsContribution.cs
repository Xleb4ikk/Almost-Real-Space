using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Вклад одного источника в правую часть ОДУ корабля. Разделение
    /// SpecificAcceleration/Force — чтобы не путать единицы: гравитация не
    /// зависит от массы корабля и возвращает готовое ускорение (м/с²), а
    /// тяга/сопротивление зависят от массы и возвращают силу (Н) —
    /// деление на массу происходит один раз, в одном месте (SpacecraftPhysics),
    /// а не размазано по каждой реализации источника.
    /// Torque (Н·м, body-frame) — суммируется рядом с силой в
    /// SpacecraftPhysics.EvaluateDerivative и отдаётся снимком TotalTorqueBody
    /// (управление вращением с зерном шага); в производную вращения сам по
    /// себе не интегрируется — вращение степпером AttitudePhysics (см. его doc).
    /// Канал оживлён (аудит S3): RCS идёт полным Evaluate (сила+момент),
    /// будущий аэродинамический момент ляжет сюда же.
    /// </summary>
    public readonly struct DynamicsContribution
    {
        public readonly Vector3d SpecificAcceleration; // м/с²
        public readonly Vector3d Force; // Н
        public readonly double MassFlow; // кг/с, положительное значение = расход
        public readonly Vector3d Torque; // Н·м, body-frame

        public DynamicsContribution(Vector3d specificAcceleration, Vector3d force, double massFlow, Vector3d torque)
        {
            SpecificAcceleration = specificAcceleration;
            Force = force;
            MassFlow = massFlow;
            Torque = torque;
        }

        public static DynamicsContribution FromSpecificAcceleration(Vector3d specificAcceleration) =>
            new DynamicsContribution(specificAcceleration, Vector3d.Zero, 0d, Vector3d.Zero);

        public static DynamicsContribution operator +(DynamicsContribution a, DynamicsContribution b) =>
            new DynamicsContribution(
                a.SpecificAcceleration + b.SpecificAcceleration,
                a.Force + b.Force,
                a.MassFlow + b.MassFlow,
                a.Torque + b.Torque);
    }
}
