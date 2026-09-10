using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Тест A — регрессия против старого пути: чистая гравитация (MassFlow = 0)
    /// обязана давать те же Position/Velocity, что существующий
    /// OrbitIntegrator.StepForward. Доказывает, что рефакторинг не изменил
    /// поведение для гравитационного случая.
    /// </summary>
    public sealed class RegressionTests
    {
        private readonly ITestOutputHelper output;

        public RegressionTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void NewPath_MatchesOldPath_OnPureGravity()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            TestScene.CircularOrbit(out Vector3d pos0, out Vector3d vel0);

            var physics = new SpacecraftPhysics
            {
                PositionAbsoluteTolerance = 1e-9,
                VelocityAbsoluteTolerance = 1e-9,
                MassAbsoluteTolerance = 1e-9,
                RelativeTolerance = 1e-10
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(pos0, vel0, TestScene.ShipMass);

            Vector3d oldPos = pos0;
            Vector3d oldVel = vel0;

            const double chunkDt = 60.0;
            const int chunks = 60;
            double time = 0d;
            double maxPosErr = 0d;
            double maxVelErr = 0d;

            for (int i = 0; i < chunks; i++)
            {
                IntegrationResult oldResult = OrbitIntegrator.StepForward(
                    oldPos, oldVel, time, chunkDt,
                    (p, t) => system.EvaluateShipAcceleration(p, t),
                    absoluteTolerance: 1e-9,
                    relativeTolerance: 1e-10);
                oldPos = oldResult.Position;
                oldVel = oldResult.Velocity;

                physics.Step(ship, time, chunkDt);
                time += chunkDt;

                double posErr = TestScene.RelativeError(ship.Position, oldPos);
                double velErr = TestScene.RelativeError(ship.Velocity, oldVel);
                if (posErr > maxPosErr) maxPosErr = posErr;
                if (velErr > maxVelErr) maxVelErr = velErr;

                Assert.True(posErr < 1e-9, $"Шаг {i}: расхождение позиции {posErr:G} превышает 1e-9.");
                Assert.True(velErr < 1e-9, $"Шаг {i}: расхождение скорости {velErr:G} превышает 1e-9.");
                Assert.Equal(TestScene.ShipMass, ship.Mass);
            }

            output.WriteLine($"Чанков: {chunks} x {chunkDt} с. Макс. отн. ошибка позиции: {maxPosErr:G}, скорости: {maxVelErr:G}.");
            output.WriteLine($"Масса неизменна: {ship.Mass} кг.");
        }
    }
}
