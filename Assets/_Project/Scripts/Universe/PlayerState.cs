using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>Режим игрока: в корабле / EVA (полёт) / на поверхности / в воде.</summary>
    public enum PlayerMode
    {
        InShip,
        EVA,
        OnSurface,
        Swimming
    }

    /// <summary>
    /// Топливо джетпака. Контракт под будущее: сейчас бесконечный (MVP),
    /// потом бак с расходом — имплементировать этот интерфейс и подменить
    /// в PlayerController.JetpackFuel, физика не меняется.
    /// </summary>
    public interface IJetpackFuel
    {
        /// <summary>Запас в секундах работы (для UI). ∞ = бесконечный.</summary>
        double RemainingSeconds { get; }

        /// <summary>Списать время работы; false — топлива нет.</summary>
        bool TryConsume(double seconds);
    }

    /// <summary>MVP-топливо джетпака: бесконечное.</summary>
    public sealed class InfiniteJetpackFuel : IJetpackFuel
    {
        public double RemainingSeconds => double.PositiveInfinity;

        public bool TryConsume(double seconds)
        {
            return true;
        }
    }

    /// <summary>
    /// Намерение игрока на кадр (заполняет PlayerController, исполняет
    /// SimulationRunner). Разделение ввода и физики: контроллер знает только
    /// кнопки, раннер — только интеграцию.
    /// </summary>
    public struct PlayerIntent
    {
        /// <summary>Направление ходьбы (касательная плоскость, единичный или ноль).</summary>
        public Vector3d WalkDirection;

        /// <summary>Скорость ходьбы (м/с).</summary>
        public double WalkSpeed;

        /// <summary>Прыжок (срабатывает один раз, на земле).</summary>
        public bool Jump;

        /// <summary>Ускорение джетпака (м/с², только в EVA).</summary>
        public Vector3d JetpackAccel;

        /// <summary>Направление плавания (мировой астро-кадр, единичный или ноль).</summary>
        public Vector3d SwimDirection;

        /// <summary>Скорость плавания (м/с).</summary>
        public double SwimSpeed;

        public static PlayerIntent Idle => default;
    }
}
