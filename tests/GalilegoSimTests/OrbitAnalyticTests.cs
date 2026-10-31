using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Test B тАФ analytic orbit check: after one full period
    /// T = 2╧АтИЪ(a┬│/╬╝) a ship on a circular orbit should return approximately
    /// to its start point (comparing the new path against the analytic one).
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

            output.WriteLine($"╨Я╨╡╤А╨╕╨╛╨┤ T = {period:F3} ╤Б. ╨Ю╤В╨╜. ╨╛╤И╨╕╨▒╨║╨░ ╨┐╨╛╨╖╨╕╤Ж╨╕╨╕: {posErr:G} ({posAbsErr:F4} ╨╝), ╤Б╨║╨╛╤А╨╛╤Б╤В╨╕: {velErr:G}.");

            Assert.True(posErr < 1e-6, $"╨Я╨╛╨╖╨╕╤Ж╨╕╤П ╨╜╨╡ ╨▓╨╡╤А╨╜╤Г╨╗╨░╤Б╤М: ╨╛╤В╨╜. ╨╛╤И╨╕╨▒╨║╨░ {posErr:G}.");
            Assert.True(velErr < 1e-6, $"╨б╨║╨╛╤А╨╛╤Б╤В╤М ╨╜╨╡ ╨▓╨╡╤А╨╜╤Г╨╗╨░╤Б╤М: ╨╛╤В╨╜. ╨╛╤И╨╕╨▒╨║╨░ {velErr:G}.");
        }
    }
}
