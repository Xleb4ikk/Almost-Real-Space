using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Тест B — аналитическая проверка орбиты: после одного полного периода
    /// T = 2π√(a³/μ) корабль на круговой орбите должен вернуться примерно
    /// в исходную точку (сравнение нового пути с аналитикой).
    /// </summary>
    public sealed class OrbitAnalyticTests
    {
        private readonly ITestOutputHelper output;

        public OrbitAnalyticTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void CircularOrbit_ReturnsToStart_AfterOnePeriod()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            TestScene.CircularOrbit(out Vector3d pos0, out Vector3d vel0);
            double period = TestScene.CircularPeriod();

            var physics = new SpacecraftPhysics();
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(pos0, vel0, TestScene.ShipMass);

            const double chunkDt = 120.0;
            double time = 0d;
            while (time < period)
            {
                double dt = System.Math.Min(chunkDt, period - time);
                physics.Step(ship, time, dt);
                time += dt;
            }

            double posErr = TestScene.RelativeError(ship.Position, pos0);
            double velErr = TestScene.RelativeError(ship.Velocity, vel0);
            double posAbsErr = (ship.Position - pos0).Magnitude;

            output.WriteLine($"Период T = {period:F3} с. Отн. ошибка позиции: {posErr:G} ({posAbsErr:F4} м), скорости: {velErr:G}.");

            Assert.True(posErr < 1e-6, $"Позиция не вернулась: отн. ошибка {posErr:G}.");
            Assert.True(velErr < 1e-6, $"Скорость не вернулась: отн. ошибка {velErr:G}.");
        }
    }
}
