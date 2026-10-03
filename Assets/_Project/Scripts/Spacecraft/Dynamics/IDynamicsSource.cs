using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Один источник вклада в динамику корабля: гравитация, тяга, сопротивление
    /// атмосферы. Явно принимает position/velocity/
    /// mass ПОДШАГА интегратора отдельными параметрами — НЕ читает их из
    /// Spacecraft напрямую, потому что на промежуточных RK-стадиях эти значения
    /// отличаются от текущего подтверждённого состояния корабля. Реализация
    /// ОБЯЗАНА быть pure: вызывается много раз на шаг, включая отклонённые
    /// шаги, поэтому внутри нельзя менять состояние корабля, источников,
    /// копить телеметрию или списывать топливо.
    /// </summary>
    public interface IDynamicsSource
    {
        DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds);
    }
}
