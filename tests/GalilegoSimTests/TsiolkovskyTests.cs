using Galilego.Core;
using Galilego.Spacecraft;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Test thrust stub: a constant force along a fixed direction and a constant
    /// mass flow. Lives ONLY in the test project, not in game code. Verifies that
    /// mass is integrated inside the state.
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
            return new DynamicsContribution(Vector3d.Zero, force, massFlow, Vector3d.Zero);
        }
    }

    /// <summary>
    /// Test C тАФ mass / Tsiolkovsky check: a burn with constant force F and
    /// constant flow с╣Б, no gravity. With a CONSTANT flow mass decreases
    /// LINEARLY: m(t) = m0 тИТ с╣Б┬╖t (not an exponential тАФ that would require a
    /// flow proportional to mass). The resulting ╬Фv must match the Tsiolkovsky
    /// formula for this case: ╬Фv = (F/с╣Б)┬╖ln(m0/m1).
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

            // Analytic: linear mass + Tsiolkovsky.
            double expectedMass = m0 - massFlow * burnTime;
            double expectedDv = (thrust / massFlow) * System.Math.Log(m0 / expectedMass);

            double massRelErr = System.Math.Abs(ship.Mass - expectedMass) / expectedMass;
            double dv = ship.Velocity.Magnitude;
            double dvRelErr = System.Math.Abs(dv - expectedDv) / expectedDv;

            output.WriteLine($"m1 ╤З╨╕╤Б╨╗. = {ship.Mass:R}, ╨░╨╜╨░╨╗╨╕╤В. = {expectedMass:R}, ╨╛╤В╨╜. ╨╛╤И╨╕╨▒╨║╨░: {massRelErr:G}.");
            output.WriteLine($"╬Фv ╤З╨╕╤Б╨╗. = {dv:R} ╨╝/╤Б, ╨░╨╜╨░╨╗╨╕╤В. = {expectedDv:R} ╨╝/╤Б, ╨╛╤В╨╜. ╨╛╤И╨╕╨▒╨║╨░: {dvRelErr:G}.");
            output.WriteLine($"╨Э╨░╨┐╤А╨░╨▓╨╗╨╡╨╜╨╕╨╡ ╬Фv: ({ship.Velocity.X:G}, {ship.Velocity.Y:G}, {ship.Velocity.Z:G}).");

            Assert.True(massRelErr < 1e-9, $"╨Ь╨░╤Б╤Б╨░ ╨╜╨╡ ╨┐╨╛ ╨╗╨╕╨╜╨╡╨╣╨╜╨╛╨╝╤Г ╨╖╨░╨║╨╛╨╜╤Г: ╨╛╤И╨╕╨▒╨║╨░ {massRelErr:G}.");
            Assert.True(dvRelErr < 1e-6, $"╬Фv ╨╜╨╡ ╨┐╨╛ ╨ж╨╕╨╛╨╗╨║╨╛╨▓╤Б╨║╨╛╨╝╤Г: ╨╛╤И╨╕╨▒╨║╨░ {dvRelErr:G}.");
            Assert.True(System.Math.Abs(ship.Velocity.Y) / dv < 1e-9, "╨в╤П╨│╨░ ╨╛╨▒╤П╨╖╨░╨╜╨░ ╨▒╤Л╤В╤М ╤Б╤В╤А╨╛╨│╨╛ ╨▓╨┤╨╛╨╗╤М +X.");
            Assert.True(System.Math.Abs(ship.Velocity.Z) / dv < 1e-9, "╨в╤П╨│╨░ ╨╛╨▒╤П╨╖╨░╨╜╨░ ╨▒╤Л╤В╤М ╤Б╤В╤А╨╛╨│╨╛ ╨▓╨┤╨╛╨╗╤М +X.");
        }
    }
}
