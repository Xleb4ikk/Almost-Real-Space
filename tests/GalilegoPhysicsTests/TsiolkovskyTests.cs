using Galilego.Core;
using Galilego.Spacecraft;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Тестовая заглушка тяги: постоянная сила вдоль фиксированного направления
    /// и постоянный массовый расход. Живёт ТОЛЬКО в тестовом проекте, не в
    /// игровом коде. Проверяет, что масса интегрируется внутри состояния.
    /// </summary>
    internal sealed class ConstantThrustSource : IDynamicsSource
    {
        private readonly Vector3d force;
        private readonly double massFlow;

        public ConstantThrustSource(Vector3d force, double massFlow)
        {
            this.force = force;
            this.massFlow = massFlow;
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            return new DynamicsContribution(Vector3d.Zero, force, massFlow);
        }
    }

    /// <summary>
    /// Тест C — проверка массы / уравнение Циолковского: горение с постоянной
    /// силой F и постоянным расходом ṁ без гравитации. При ПОСТОЯННОМ расходе
    /// масса убывает ЛИНЕЙНО: m(t) = m0 − ṁ·t (не экспонента — та была бы при
    /// расходе, пропорциональном массе). Итоговое Δv обязано совпасть с
    /// формулой Циолковского для этого случая: Δv = (F/ṁ)·ln(m0/m1).
    /// </summary>
    public sealed class TsiolkovskyTests
    {
        private readonly ITestOutputHelper output;

        public TsiolkovskyTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void ConstantThrust_MatchesTsiolkovsky()
        {
            const double m0 = 1000.0;
            const double thrust = 100.0;
            const double massFlow = 1.0;
            const double burnTime = 100.0;

            var physics = new SpacecraftPhysics
            {
                PositionAbsoluteTolerance = 1e-9,
                VelocityAbsoluteTolerance = 1e-9,
                MassAbsoluteTolerance = 1e-9,
                RelativeTolerance = 1e-11
            };
            physics.Sources.Add(new ConstantThrustSource(new Vector3d(thrust, 0d, 0d), massFlow));

            var ship = new Spacecraft(Vector3d.Zero, Vector3d.Zero, m0);
            physics.Step(ship, 0d, burnTime);

            // Аналитика: линейная масса + Циолковский.
            double expectedMass = m0 - massFlow * burnTime;
            double expectedDv = (thrust / massFlow) * System.Math.Log(m0 / expectedMass);

            double massRelErr = System.Math.Abs(ship.Mass - expectedMass) / expectedMass;
            double dv = ship.Velocity.Magnitude;
            double dvRelErr = System.Math.Abs(dv - expectedDv) / expectedDv;

            output.WriteLine($"m1 числ. = {ship.Mass:R}, аналит. = {expectedMass:R}, отн. ошибка: {massRelErr:G}.");
            output.WriteLine($"Δv числ. = {dv:R} м/с, аналит. = {expectedDv:R} м/с, отн. ошибка: {dvRelErr:G}.");
            output.WriteLine($"Направление Δv: ({ship.Velocity.X:G}, {ship.Velocity.Y:G}, {ship.Velocity.Z:G}).");

            Assert.True(massRelErr < 1e-9, $"Масса не по линейному закону: ошибка {massRelErr:G}.");
            Assert.True(dvRelErr < 1e-6, $"Δv не по Циолковскому: ошибка {dvRelErr:G}.");
            Assert.True(System.Math.Abs(ship.Velocity.Y) / dv < 1e-9, "Тяга обязана быть строго вдоль +X.");
            Assert.True(System.Math.Abs(ship.Velocity.Z) / dv < 1e-9, "Тяга обязана быть строго вдоль +X.");
        }
    }
}
