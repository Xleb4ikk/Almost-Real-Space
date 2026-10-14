using Galilego.Core;
using Galilego.Spacecraft;

namespace Galilego.Events
{
    /// <summary>
    /// Исчерпание участка выработки активной ступени. Функция события:
    /// запас участка − сгоревшее с момента взведения. Взводится ПЕРЕД каждым
    /// чанком (Arm) на текущий SegmentBurnKg: пересечение нуля — смена состава
    /// работающих двигателей (группа заглохла по пулу), а не обязательно полный
    /// останов ступени. Ставить событие на SegmentBurnKg (а не CutoffBurnKg)
    /// обязательно: иначе физика проскочит смену тяги/расхода.
    ///
    /// Владелец (VesselStep) после корня события списывает фактическую массу
    /// через StagePropulsion.CommitBurn и взводит детектор заново на следующий
    /// участок. Pure: Evaluate только читает переданное состояние.
    /// </summary>
    public sealed class StageDepletionDetector : ICrossingDetector
    {
        private double armedMassKg;
        private double armedBurnKg;

        /// <summary>Взвести перед чанком: масса корабля и запас участка сейчас.</summary>
        public void Arm(StagePropulsion propulsion, double shipMassKg)
        {
            armedMassKg = shipMassKg;
            armedBurnKg = propulsion != null ? propulsion.SegmentBurnKg : 0d;
        }

        public double Evaluate(SpacecraftIntegrationState state, double timeSeconds)
        {
            return armedBurnKg - (armedMassKg - state.Mass);
        }

        public EventDirection Direction { get { return EventDirection.Falling; } }

        public int Priority { get { return EventPriorities.Planned; } }

        public string Name { get { return "StageBurnSegment"; } }

        public EventKind Kind { get { return EventKind.PropellantDepleted; } }
    }
}
