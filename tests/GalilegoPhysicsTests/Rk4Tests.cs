using System;
using Galilego.Core;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Проверяем старый RK4 из PhysicsSolver на простых примерах,
    /// где ответ можно посчитать вручную.
    /// </summary>
    public sealed class Rk4Tests
    {
        private readonly ITestOutputHelper output;

        public Rk4Tests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void FreeFall_MatchesSchoolFormulas()
        {
            // Камень летит горизонтально со скоростью 3 м/с
            // и одновременно падает с высоты 100 м.
            //
            // Через 1 секунду:
            //   x = 3 * 1 = 3 м
            //   y = 100 - 9.81 * 1² / 2 = 95.095 м
            //   vy = -9.81 * 1 = -9.81 м/с
            var startPosition = new Vector3d(0d, 100d, 0d);
            var startVelocity = new Vector3d(3d, 0d, 0d);
            var gravity = new Vector3d(0d, -9.81d, 0d);

            const double dt = 0.01d;
            const int steps = 100;

            (Vector3d position, Vector3d velocity) = RunRk4(
                startPosition, startVelocity, dt, steps, (p, t) => gravity);

            var expectedPosition = new Vector3d(3d, 100d - (0.5d * 9.81d), 0d);
            var expectedVelocity = new Vector3d(3d, -9.81d, 0d);

            double positionError = (position - expectedPosition).Magnitude;
            double velocityError = (velocity - expectedVelocity).Magnitude;

            output.WriteLine($"Позиция:   ({position.X:F4}, {position.Y:F4}, {position.Z:F4}) м");
            output.WriteLine($"Ожидалось: ({expectedPosition.X:F4}, {expectedPosition.Y:F4}, {expectedPosition.Z:F4}) м");
            output.WriteLine($"Скорость:  ({velocity.X:F4}, {velocity.Y:F4}, {velocity.Z:F4}) м/с");
            output.WriteLine($"Ожидалось: ({expectedVelocity.X:F4}, {expectedVelocity.Y:F4}, {expectedVelocity.Z:F4}) м/с");
            output.WriteLine($"Ошибка позиции: {positionError:G} м");
            output.WriteLine($"Ошибка скорости: {velocityError:G} м/с");

            Assert.True(positionError < 1e-9d,
                $"RK4 ошибся в позиции на {positionError:G} м — это больше 1e-9.");
            Assert.True(velocityError < 1e-9d,
                $"RK4 ошибся в скорости на {velocityError:G} м/с — это больше 1e-9.");
        }

        [Fact]
        public void GrowingAcceleration_MatchesAnalyticSolution()
        {
            // Ускорение по X постепенно растёт: a(t) = 2t.
            // Скорость — интеграл ускорения: v(t) = t².
            // Путь — интеграл скорости: x(t) = t³ / 3.
            //
            // Через 3 секунды:
            //   v = 3² = 9 м/с
            //   x = 3³ / 3 = 9 м
            var startPosition = Vector3d.Zero;
            var startVelocity = Vector3d.Zero;

            const double dt = 0.1d;
            const int steps = 30;

            (Vector3d position, Vector3d velocity) = RunRk4(
                startPosition,
                startVelocity,
                dt,
                steps,
                (p, t) => new Vector3d(2d * t, 0d, 0d));

            var expectedPosition = new Vector3d(9d, 0d, 0d);
            var expectedVelocity = new Vector3d(9d, 0d, 0d);

            double positionError = (position - expectedPosition).Magnitude;
            double velocityError = (velocity - expectedVelocity).Magnitude;

            output.WriteLine($"Позиция:   ({position.X:F4}, {position.Y:F4}, {position.Z:F4}) м");
            output.WriteLine($"Ожидалось: ({expectedPosition.X:F4}, {expectedPosition.Y:F4}, {expectedPosition.Z:F4}) м");
            output.WriteLine($"Скорость:  ({velocity.X:F4}, {velocity.Y:F4}, {velocity.Z:F4}) м/с");
            output.WriteLine($"Ожидалось: ({expectedVelocity.X:F4}, {expectedVelocity.Y:F4}, {expectedVelocity.Z:F4}) м/с");
            output.WriteLine($"Ошибка позиции: {positionError:G} м");
            output.WriteLine($"Ошибка скорости: {velocityError:G} м/с");

            Assert.True(positionError < 1e-9d,
                $"RK4 ошибся в позиции на {positionError:G} м — это больше 1e-9.");
            Assert.True(velocityError < 1e-9d,
                $"RK4 ошибся в скорости на {velocityError:G} м/с — это больше 1e-9.");
        }

        private static (Vector3d Position, Vector3d Velocity) RunRk4(
            Vector3d position,
            Vector3d velocity,
            double dt,
            int steps,
            Func<Vector3d, double, Vector3d> acceleration)
        {
            double time = 0d;

            // PhysicsSolver.RK4 помечен Obsolete: игра перешла на другой путь.
            // Но этот тест проверяет именно его, потому что он используется
            // как независимый эталон в других тестах.
#pragma warning disable 0618
            for (int i = 0; i < steps; i++)
            {
                IntegrationResult result = PhysicsSolver.RK4(
                    position, velocity, time, dt, acceleration);

                position = result.Position;
                velocity = result.Velocity;
                time += dt;
            }
#pragma warning restore 0618

            return (position, velocity);
        }
    }
}
