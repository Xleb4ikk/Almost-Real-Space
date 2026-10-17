using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Состояние корабля. В отличие от Core.CelestialBody (у него масса задаётся
    /// один раз в конструкторе и не меняется — разумно для звёзд и планет), масса
    /// корабля будет меняться по мере расхода топлива, поэтому это отдельный,
    /// лёгкий класс с изменяемым состоянием, а не переиспользование CelestialBody.
    /// </summary>
    public sealed class Spacecraft
    {
        public Vector3d Position;
        public Vector3d Velocity;
        public double Mass;

        /// <summary>Ориентация body→мир и угловая скорость в осях корабля (6-DOF полёт).</summary>
        public QuaternionD Attitude = QuaternionD.Identity;
        public Vector3d AngularVelocity;

        /// <summary>true — Position это центр масс (после выхода из зоны контакта), false — низ аппарата.</summary>
        public bool PositionIsCom;

        public Spacecraft(Vector3d position, Vector3d velocity, double mass)
        {
            Position = position;
            Velocity = velocity;
            Mass = mass;
        }
    }
}
