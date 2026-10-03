using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// Зафиксированное событие: propagate-to-event-and-stop. P1a только
    /// детектирует и останавливается с точными временем/состоянием в корне —
    /// никакой реакции (аэро, посадка, варп) здесь нет, это задача P1b.
    /// Body — тело-участник (для геометрических событий), null для плановых.
    /// Kind — типизированный вид для таблицы реакций (парсинг Name запрещён).
    /// SourceDetector — детектор-источник (контекст реакции: сухая масса,
    /// тело); null только для TimedEvent и EphemerisEnd. Осознанный компромисс связанности:
    /// реакции уже владеют детекторами (регистрировали их), новых знаний
    /// о симуляции это поле не добавляет — не разрастать дальше этого.
    /// </summary>
    public readonly struct EventOccurrence
    {
        public readonly double TimeSeconds;
        public readonly SpacecraftIntegrationState State;
        public readonly string DetectorName;
        public readonly OrbitingBody Body;
        public readonly EventKind Kind;
        public readonly object SourceDetector;

        public EventOccurrence(double timeSeconds, SpacecraftIntegrationState state, string detectorName, OrbitingBody body, EventKind kind, object sourceDetector)
        {
            TimeSeconds = timeSeconds;
            State = state;
            DetectorName = detectorName;
            Body = body;
            Kind = kind;
            SourceDetector = sourceDetector;
        }
    }
}
