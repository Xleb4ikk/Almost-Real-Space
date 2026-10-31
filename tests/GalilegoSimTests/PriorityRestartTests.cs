using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Test G тАФ priority plus zero-time-loop protection. Two detectors on the
    /// same shell with different priorities: the first call must return the
    /// higher-priority one; restarting from the returned root must always advance
    /// (g = 0 at the restart point is not an event) and find the next crossing
    /// strictly later rather than looping on the same point.
    /// </summary>
    public sealed class PriorityRestartTests
    {
        private readonly ITestOutputHelper output;

        public PriorityRestartTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void SameRoot_PriorityWins_RestartAdvances()
        {
            StarSystem system = AltitudeCrossingTests.CreateSystem(out OrbitingBody star);
            TestScene.EllipticalOrbit(
                AltitudeCrossingTests.Periapsis, AltitudeCrossingTests.Apoapsis, TestScene.Mu,
                out var pos0, out var vel0);
            double period = TestScene.EllipticalPeriod(
                AltitudeCrossingTests.Periapsis, AltitudeCrossingTests.Apoapsis, TestScene.Mu);
            double shell = AltitudeCrossingTests.ShellRadius;

            var propagator = AltitudeCrossingTests.CreatePropagator(system);
            propagator.CrossingDetectors.Add(new AltitudeCrossingDetector(
                star, shell - star.Radius, EventDirection.Either, 0, "HighPriority", EventKind.AtmosphereEntry));
            propagator.CrossingDetectors.Add(new AltitudeCrossingDetector(
                star, shell - star.Radius, EventDirection.Either, 5, "LowPriority", EventKind.AtmosphereEntry));

            var ship = new Spacecraft(pos0, vel0, TestScene.ShipMass);
            EventOccurrence? first = propagator.Propagate(ship, 0d, period);
            Assert.True(first.HasValue, "╨Я╨╡╤А╨▓╨╛╨╡ ╨┐╨╡╤А╨╡╤Б╨╡╤З╨╡╨╜╨╕╨╡ ╨╜╨╡ ╨╜╨░╨╣╨┤╨╡╨╜╨╛.");
            Assert.Equal("HighPriority", first.Value.DetectorName);
            double t1 = first.Value.TimeSeconds;

            // Restart from the root: the same root must not be re-detected.
            EventOccurrence? second = propagator.Propagate(ship, t1, period - t1);
            Assert.True(second.HasValue, "╨Т╤В╨╛╤А╨╛╨╡ ╨┐╨╡╤А╨╡╤Б╨╡╤З╨╡╨╜╨╕╨╡ ╨╜╨╡ ╨╜╨░╨╣╨┤╨╡╨╜╨╛.");
            double t2 = second.Value.TimeSeconds;
            Assert.True(t2 > t1, $"╨а╨╡╤Б╤В╨░╤А╤В ╨╜╨╡ ╨┐╤А╨╛╨┤╨▓╨╕╨╜╤Г╨╗╤Б╤П: t2 = {t2:F6} <= t1 = {t1:F6} тАФ zero-time loop.");
            Assert.Equal("HighPriority", second.Value.DetectorName);

            double r1 = System.Math.Abs(first.Value.State.Position.Magnitude - shell);
            double r2 = System.Math.Abs(second.Value.State.Position.Magnitude - shell);

            output.WriteLine($"t1 = {t1:F6} ╤Б (r-╨╛╤И. {r1:G} ╨╝), t2 = {t2:F6} ╤Б (r-╨╛╤И. {r2:G} ╨╝). ╨Ю╨▒╨░ тАФ HighPriority, ╨╖╨░╤Ж╨╕╨║╨╗╨╕╨▓╨░╨╜╨╕╤П ╨╜╨╡╤В.");
            Assert.True(r1 < 1.0, $"╨Я╨╡╤А╨▓╤Л╨╣ ╨║╨╛╤А╨╡╨╜╤М ╨╜╨╡ ╨╜╨░ ╨╛╨▒╨╛╨╗╨╛╤З╨║╨╡: {r1:G} ╨╝.");
            Assert.True(r2 < 1.0, $"╨Т╤В╨╛╤А╨╛╨╣ ╨║╨╛╤А╨╡╨╜╤М ╨╜╨╡ ╨╜╨░ ╨╛╨▒╨╛╨╗╨╛╤З╨║╨╡: {r2:G} ╨╝.");
        }

        [Fact]
        public void LowPriorityDetectorAlone_FindsSameRoot()
        {
            // Control for the coincident case above: proves LowPriority really
            // fires on the same root (rather than staying silent and letting
            // HighPriority win by default). Same chunking, same bisection тАФ the
            // roots must coincide tightly.
            StarSystem system = AltitudeCrossingTests.CreateSystem(out OrbitingBody star);
            TestScene.EllipticalOrbit(
                AltitudeCrossingTests.Periapsis, AltitudeCrossingTests.Apoapsis, TestScene.Mu,
                out var pos0, out var vel0);
            double period = TestScene.EllipticalPeriod(
                AltitudeCrossingTests.Periapsis, AltitudeCrossingTests.Apoapsis, TestScene.Mu);
            double shell = AltitudeCrossingTests.ShellRadius;

            var propagator = AltitudeCrossingTests.CreatePropagator(system);
            propagator.CrossingDetectors.Add(new AltitudeCrossingDetector(
                star, shell - star.Radius, EventDirection.Either, 0, "HighPriority", EventKind.AtmosphereEntry));
            propagator.CrossingDetectors.Add(new AltitudeCrossingDetector(
                star, shell - star.Radius, EventDirection.Either, 5, "LowPriority", EventKind.AtmosphereEntry));

            var ship = new Spacecraft(pos0, vel0, TestScene.ShipMass);
            EventOccurrence? combined = propagator.Propagate(ship, 0d, period);
            Assert.True(combined.HasValue, "╨Ъ╨╛╨╝╨▒╨╕╨╜╨╕╤А╨╛╨▓╨░╨╜╨╜╤Л╨╣ ╨┐╤А╨╛╨│╨╛╨╜ ╨╜╨╡ ╨╜╨░╤И╤С╨╗ ╨║╨╛╤А╨╡╨╜╤М.");
            double tCombined = combined.Value.TimeSeconds;

            var lonePhysics = new SpacecraftPhysics();
            lonePhysics.Sources.Add(new GravitySource(system));
            var lonePropagator = new EventDrivenPropagator(lonePhysics);
            lonePropagator.CrossingDetectors.Add(new AltitudeCrossingDetector(
                star, shell - star.Radius, EventDirection.Either, 5, "LowPriority", EventKind.AtmosphereEntry));

            var loneShip = new Spacecraft(pos0, vel0, TestScene.ShipMass);
            EventOccurrence? lone = lonePropagator.Propagate(loneShip, 0d, period);
            Assert.True(lone.HasValue, "╨Ю╨┤╨╕╨╜╨╛╨║╨╕╨╣ LowPriority ╨╜╨╡ ╤Д╨░╨╡╤А╨╕╤В тАФ ╨┐╨╛╨▒╨╡╨┤╨░ HighPriority ╨▒╤Л╨╗╨░ ╨▒╤Л ╨┐╨╛ ╤Г╨╝╨╛╨╗╤З╨░╨╜╨╕╤О.");
            Assert.Equal("LowPriority", lone.Value.DetectorName);

            double dt = System.Math.Abs(lone.Value.TimeSeconds - tCombined);
            output.WriteLine($"╨Ъ╨╛╨╝╨▒╨╕╨╜╨╕╤А╨╛╨▓╨░╨╜╨╜╤Л╨╣: t = {tCombined:F6} ╤Б (HighPriority), ╨╛╨┤╨╕╨╜╨╛╨║╨╕╨╣ Low: t = {lone.Value.TimeSeconds:F6} ╤Б. ╨а╨░╤Б╤Е╨╛╨╢╨┤╨╡╨╜╨╕╨╡: {dt:G} ╤Б.");
            Assert.True(dt < 1e-3, $"Low ╤Д╨░╨╡╤А╨╕╤В ╨╜╨╡ ╨╜╨░ ╤В╨╛╨╝ ╨╢╨╡ ╨║╨╛╤А╨╜╨╡: ╤А╨░╤Б╤Е╨╛╨╢╨┤╨╡╨╜╨╕╨╡ {dt:G} ╤Б.");
        }
    }
}
