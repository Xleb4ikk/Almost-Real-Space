using Galilego.Core;

namespace Galilego.Events
{
    /// <summary>
    /// Плановое событие в точное время: отсечка двигателя, сброс массы ступени.
    /// Не детектируется, а наступает по часам — драйвер пропагает ровно в
    /// TimeSeconds и применяет разрыв непрерывности на границе принятого шага.
    /// Машина событий не знает про баки/ступени/VAB (их ещё нет): только
    /// «масса скачком изменилась на столько-то в этот момент». Когда придёт
    /// настоящая система ступеней, она сама посчитает DeltaMass и передаст сюда.
    /// Проверка MinimumMassKg — громким исключением, не clamp: тихий clamp
    /// спрятал бы ошибку модели ступеней, а событие обязано fail loudly.
    /// DeltaVelocity — ОТНОСИТЕЛЬНАЯ скорость разведения ступеней Δv_rel
    /// (м/с, вектор: величина задаётся здесь, направление — отдельным входом
    /// точки применения, обычно из AttitudeState; см. StageSeparation).
    /// Внутренний импульс действует только между частями — внешних сил нет,
    /// поэтому v1 = v0 + (m2/M)·Δv_rel, v2 = v0 − (m1/M)·Δv_rel сохраняют
    /// суммарный импульс точно по построению коэффициентов. В P1a-драйвере
    /// НЕ применяется (драйвер только детектит/рвёт массу, тел не создаёт).
    /// </summary>
    public sealed class TimedEvent
    {
        public double TimeSeconds;
        public double DeltaMass;
        public double MinimumMassKg;
        public Vector3d DeltaVelocity;

        public int Priority => EventPriorities.Planned;

        public string Name => "TimedEvent@" + TimeSeconds.ToString("F3") + "s";
    }
}
