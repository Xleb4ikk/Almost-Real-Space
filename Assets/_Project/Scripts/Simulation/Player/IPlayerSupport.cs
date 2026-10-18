using Galilego.Core;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Источник геометрии опоры для игрока, в ЛОКАЛЬНЫХ метрах касательной системы
    /// места (x = восток, y = север, z = вверх; см. SiteFrame). Реализации:
    /// аналитический рельеф (регресс), мешевые коллайдеры зданий (Unity),
    /// тестовая заглушка платформы (стенд).
    /// </summary>
    public interface IPlayerSupport
    {
        /// <summary>
        /// Самая высокая опора ниже from в пределах maxDrop. height — высота опоры
        /// по локальной вертикали, normal — внешняя нормаль поверхности,
        /// sourceId — идентификатор опоры (для переноса движущейся платформой).
        /// </summary>
        bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d normal, out int sourceId);

        /// <summary>Потолок выше head в пределах maxRise (height — низ потолка).</summary>
        bool Ceiling(Vector3d head, double maxRise, out double height);

        /// <summary>
        /// Горизонтальная блокировка капсулой (радиус radius, высота height от from):
        /// доля пути до первого касания и нормаль стены. Высота — параметр, а не
        /// константа: пригнувшийся игрок (рост капсулы меньше) проходит под
        /// низкие проёмы — люк капсулы Orion.
        /// </summary>
        bool Sweep(Vector3d from, Vector3d to, double radius, double height, out Vector3d normal, out double fraction);
    }

    /// <summary>
    /// Опциональное расширение: движущаяся опора (платформа, деталь корабля).
    /// Игрок переносится вместе с ней на её горизонтальной скорости.
    /// </summary>
    public interface IMovingSupport
    {
        bool TryGetSupportVelocity(int sourceId, out Vector3d velocity);
    }

    /// <summary>
    /// Опциональное расширение: «капсула с центром в feet, радиусом radius и высотой
    /// height пересекает опору». Нужна, чтобы вставать из пригиба ТОЛЬКО там, где
    /// влезает весь объём капсулы (скошенная стена кабины боковым лучом не видна).
    /// </summary>
    public interface IOverlapSupport
    {
        bool Overlaps(Vector3d feet, double radius, double height);
    }
}
