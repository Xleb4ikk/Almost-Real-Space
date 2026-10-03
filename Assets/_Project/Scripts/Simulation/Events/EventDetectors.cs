using Galilego.Core;

namespace Galilego.Events
{
    /// <summary>
    /// Зафиксированные приоритеты событий: меньше число — выше приоритет.
    /// Побеждает ранний корень; корни, совпавшие в пределах допуска бисекции,
    /// разрешаются по приоритету. Порядок — физический смысл потери: сначала
    /// то, что уничтожает корабль или необратимо меняет режим полёта.
    /// </summary>
    public static class EventPriorities
    {
        public const int Touchdown = 0;
        public const int Atmosphere = 10;
        public const int SoiChange = 20;
        public const int Planned = 30;
    }

    /// <summary>
    /// Типизированный вид события. Таблица реакций (EventReactions) идёт по
    /// Kind, а не по строке Name: парсинг имён запрещён. Новое событие = новый
    /// член enum + ветка реакции, молчаливое переиспользование чужого — нет.
    /// </summary>
    public enum EventKind
    {
        Touchdown,
        AtmosphereEntry,
        AtmosphereExit,
        SoiChange,
        PropellantDepleted,
        Timed,
        EphemerisEnd
    }

    /// <summary>
    /// Непрерывное событие: нуль непрерывной функции состояния. Реализация
    /// ОБЯЗАНА быть pure (как RHS интегратора): вызывается многократно,
    /// включая бисекционные перепогоны и отклонённые шаги, — внутри нельзя
    /// мутировать корабль, источники или сам детектор (бейзлайн хранит
    /// вызывающий драйвер, не детектор).
    /// </summary>
    public interface ICrossingDetector
    {
        double Evaluate(SpacecraftIntegrationState state, double timeSeconds);

        EventDirection Direction { get; }

        int Priority { get; }

        string Name { get; }

        EventKind Kind { get; }
    }

    /// <summary>
    /// Дискретное событие: смена ключа (например, доминирующего тела) между
    /// двумя точками. Непрерывной нуль-функции здесь нет, поэтому уточнение
    /// корня идёт бисекцией по булеву предикату «ключ уже новый». Ключ обязан
    /// быть стабильной ссылкой (сравнение — референсное): для смены SOI это
    /// сам OrbitingBody. Реализация GetKey — pure, как и Evaluate выше.
    /// </summary>
    public interface ITransitionDetector
    {
        object GetKey(SpacecraftIntegrationState state, double timeSeconds);

        int Priority { get; }

        string Name { get; }

        EventKind Kind { get; }
    }
}
