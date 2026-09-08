using System;

namespace Galilego.Core
{
    /// <summary>
    /// Полное состояние корабля для интегрирования: позиция, скорость, масса.
    /// Масса — часть состояния (а не отдельное поле, меняемое после шага),
    /// потому что расход топлива описывается как dm/dt = -массовый расход —
    /// обычное ОДУ первого порядка, которое должно эволюционировать СИНХРОННО
    /// с позицией/скоростью на каждой RK-подстадии, а не постфактум. Иначе все
    /// внутренние подстадии DOPRI5 считали бы ускорение (сила/масса) по
    /// устаревшей массе и формальный порядок точности метода был бы потерян.
    /// Операторы ниже — ровно то, что нужно DOPRI5 для линейных комбинаций
    /// стадий; больше ничего этой структуре не положено уметь.
    /// </summary>
    [Serializable]
    public struct SpacecraftIntegrationState
    {
        public Vector3d Position;
        public Vector3d Velocity;
        public double Mass;

        /// <summary>Все компоненты конечны (NaN/Inf в состоянии — порча интеграции).</summary>
        public bool IsFinite =>
            Position.IsFinite && Velocity.IsFinite && !double.IsNaN(Mass) && !double.IsInfinity(Mass);

        public static readonly SpacecraftIntegrationState Zero =
            new SpacecraftIntegrationState { Position = Vector3d.Zero, Velocity = Vector3d.Zero, Mass = 0d };

        public static SpacecraftIntegrationState operator +(SpacecraftIntegrationState a, SpacecraftIntegrationState b) =>
            new SpacecraftIntegrationState
            {
                Position = a.Position + b.Position,
                Velocity = a.Velocity + b.Velocity,
                Mass = a.Mass + b.Mass
            };

        public static SpacecraftIntegrationState operator -(SpacecraftIntegrationState a, SpacecraftIntegrationState b) =>
            new SpacecraftIntegrationState
            {
                Position = a.Position - b.Position,
                Velocity = a.Velocity - b.Velocity,
                Mass = a.Mass - b.Mass
            };

        public static SpacecraftIntegrationState operator *(SpacecraftIntegrationState s, double scalar) =>
            new SpacecraftIntegrationState { Position = s.Position * scalar, Velocity = s.Velocity * scalar, Mass = s.Mass * scalar };

        public static SpacecraftIntegrationState operator *(double scalar, SpacecraftIntegrationState s) => s * scalar;
    }
}
