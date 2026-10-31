using Galilego.Core;
using Galilego.Spacecraft;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Synthetic mass-flow source тАФ a fast explicit function of time:
    /// massFlow = k┬╖(1 + 0.9┬╖sin(2╧Аt/P)). Has no physical meaning.
    /// With no gravity or thrust the ship coasts in a straight line, which
    /// DOPRI5 integrates exactly for any step size, so position/velocity
    /// truncation error is identically zero: the only truncation error source
    /// is mass, so any step subdivision finer than the maximum can only be
    /// the mass tolerance at work.
    /// </summary>
    internal sealed class OscillatingMassFlowSource : IDynamicsSource
    {
        private readonly double rate;
        private readonly double period;

        public OscillatingMassFlowSource(double rate, double period)
        {
            this.rate = rate;
            this.period = period;
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            double flow = rate * (1d + 0.9d * System.Math.Sin(2d * System.Math.PI * timeSeconds / period));
            return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, flow, Vector3d.Zero);
        }
    }

    /// <summary>
    /// RHS-call-counting decorator. The number of derivative calls proxies the
    /// integrator's step count (6 new evaluations per step attempt plus FSAL
    /// reuse): if a tolerance really drives step control, tightening it must
    /// increase the count.
    /// </summary>
    internal sealed class CountingSource : IDynamicsSource
    {
        private readonly IDynamicsSource inner;
        public int Calls;

        public CountingSource(IDynamicsSource inner)
        {
            this.inner = inner;
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            Calls++;
            return inner.Evaluate(position, velocity, mass, timeSeconds);
        }
    }

    /// <summary>
    /// Test D тАФ MassAbsoluteTolerance really drives the step, not just exists as
    /// a parameter. Controlled experiment: same scene (gravity-free drift plus a
    /// fast oscillating flow), same position/velocity tolerances and rtol тАФ only
    /// MassAbsoluteTolerance differs (tight 1e-9 vs effectively disabling 1e3).
    /// With the loose tolerance the integrator must take maximum 600 s steps;
    /// with the tight one it must subdivide under the 50 s flow oscillation.
    /// Additionally the tight run must converge to m(T) = m0 тИТ k┬╖T (the sine
    /// integrates to zero over a whole number of periods).
    /// </summary>
    public sealed class MassToleranceTests
    {
        private readonly ITestOutputHelper output;

        public MassToleranceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const double Rate = 0.1;
        private const double OscillationPeriod = 50.0;
        private const double TotalTime = 3600.0; // 72 full oscillation periods
        private const double InitialMass = 1000.0;

        private static int RunCase(double massAtol, out double finalMass)
        {
            var counter = new CountingSource(new OscillatingMassFlowSource(Rate, OscillationPeriod));

            var physics = new SpacecraftPhysics
            {
                PositionAbsoluteTolerance = 1e-2,
                VelocityAbsoluteTolerance = 1e-5,
                MassAbsoluteTolerance = massAtol,
                RelativeTolerance = 1e-10
            };
            physics.Sources.Add(counter);

            var ship = new Spacecraft(Vector3d.Zero, Vector3d.Zero, InitialMass);
            physics.Step(ship, 0d, TotalTime);

            finalMass = ship.Mass;
            return counter.Calls;
        }

        [Fact]
        public void TightMassTolerance_IncreasesStepCount()
        {
            int callsLoose = RunCase(1e3, out double mLoose);
            int callsTight = RunCase(1e-9, out double mTight);

            double ratio = (double)callsTight / callsLoose;

            // Analytic: over a whole number of periods the sine averages to zero.
            double expectedMass = InitialMass - Rate * TotalTime;
            double tightRelErr = System.Math.Abs(mTight - expectedMass) / expectedMass;

            output.WriteLine($"╨Т╤Л╨╖╨╛╨▓╨╛╨▓ RHS: ╨╝╤П╨│╨║╨╕╨╣ ╨┤╨╛╨┐╤Г╤Б╨║ = {callsLoose}, ╨╢╤С╤Б╤В╨║╨╕╨╣ = {callsTight}, ╨╛╤В╨╜╨╛╤И╨╡╨╜╨╕╨╡ = {ratio:F1}.");
            output.WriteLine($"╨Ь╨░╤Б╤Б╨░: ╨╝╤П╨│╨║╨░╤П = {mLoose:R} ╨║╨│, ╨╢╤С╤Б╤В╨║╨░╤П = {mTight:R} ╨║╨│, ╨░╨╜╨░╨╗╨╕╤В╨╕╨║╨░ = {expectedMass:R} ╨║╨│.");
            output.WriteLine($"╨Ю╤В╨╜. ╨╛╤И╨╕╨▒╨║╨░ ╨╢╤С╤Б╤В╨║╨╛╨│╨╛ ╨┐╤А╨╛╨│╨╛╨╜╨░ vs ╨░╨╜╨░╨╗╨╕╤В╨╕╨║╨╕: {tightRelErr:G}.");

            Assert.True(callsTight > 3 * callsLoose,
                $"╨Ц╤С╤Б╤В╨║╨╕╨╣ MassAbsoluteTolerance ╨╜╨╡ ╨┤╤А╨╛╨▒╨╕╤В ╤И╨░╨│: {callsTight} vs {callsLoose}. ╨Ф╨╛╨┐╤Г╤Б╨║ ╨╜╨╡ ╤Г╤З╨░╤Б╤В╨▓╤Г╨╡╤В ╨▓ ╤Г╨┐╤А╨░╨▓╨╗╨╡╨╜╨╕╨╕ ╤И╨░╨│╨╛╨╝.");
            Assert.True(tightRelErr < 1e-6,
                $"╨Ц╤С╤Б╤В╨║╨╕╨╣ ╨┐╤А╨╛╨│╨╛╨╜ ╨╜╨╡ ╤Б╨╛╤И╤С╨╗╤Б╤П ╨║ ╨░╨╜╨░╨╗╨╕╤В╨╕╨║╨╡ m(T) = m0 тИТ kT: ╨╛╤И╨╕╨▒╨║╨░ {tightRelErr:G}.");
        }
    }
}
