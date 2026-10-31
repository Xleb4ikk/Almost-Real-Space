using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Test F тАФ timed event. Circular-orbit drift plus a TimedEvent in the middle
    /// of the interval: the event time must be exactly TimeSeconds, the mass jump
    /// exactly ╬Фm (the discontinuity is applied at the accepted step boundary,
    /// bit-for-bit), and continuing past the event must not fire it again
    /// (the event is consumed). Violating MinimumMassKg throws loudly, not a silent clamp.
    /// </summary>
    public sealed class TimedEventTests
    {
        private readonly ITestOutputHelper output;

        public TimedEventTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void TimedMassDrop_ExactTimeAndExactJump()
        {
            StarSystem system = AltitudeCrossingTests.CreateSystem(out _);
            TestScene.CircularOrbit(out var pos0, out var vel0);

            var physics = new SpacecraftPhysics();
            physics.Sources.Add(new GravitySource(system));
            var propagator = new EventDrivenPropagator(physics);
            propagator.TimedEvents.Add(new TimedEvent
            {
                TimeSeconds = 1000.0,
                DeltaMass = -200.0,
                MinimumMassKg = 500.0
            });

            var ship = new Spacecraft(pos0, vel0, TestScene.ShipMass);
            EventOccurrence? first = propagator.Propagate(ship, 0d, 2000d);

            Assert.True(first.HasValue, "╨Я╨╗╨░╨╜╨╛╨▓╨╛╨╡ ╤Б╨╛╨▒╤Л╤В╨╕╨╡ ╨╜╨╡ ╤Б╤А╨░╨▒╨╛╤В╨░╨╗╨╛.");
            Assert.Equal(1000.0, first.Value.TimeSeconds);
            Assert.Equal(800.0, first.Value.State.Mass);
            Assert.Equal(800.0, ship.Mass);

            // Restart from the root: the event is consumed, pure drift to the end after that.
            EventOccurrence? second = propagator.Propagate(ship, first.Value.TimeSeconds, 1000d);
            Assert.False(second.HasValue, "╨Я╨╛╤В╤А╨╡╨▒╨╗╤С╨╜╨╜╨╛╨╡ ╤Б╨╛╨▒╤Л╤В╨╕╨╡ ╤Б╤А╨░╨▒╨╛╤В╨░╨╗╨╛ ╨┐╨╛╨▓╤В╨╛╤А╨╜╨╛.");
            Assert.Equal(800.0, ship.Mass);

            output.WriteLine($"╨б╨╛╨▒╤Л╤В╨╕╨╡ ╤А╨╛╨▓╨╜╨╛ ╨▓ t = {first.Value.TimeSeconds:F3} ╤Б, ╨╝╨░╤Б╤Б╨░ 1000 тЖТ {ship.Mass:F3} ╨║╨│, ╨┐╨╛╨▓╤В╨╛╤А╨░ ╨╜╨╡╤В.");
        }

        [Fact]
        public void TimedMassDrop_BelowMinimum_Throws()
        {
            StarSystem system = AltitudeCrossingTests.CreateSystem(out _);
            TestScene.CircularOrbit(out var pos0, out var vel0);

            var physics = new SpacecraftPhysics();
            physics.Sources.Add(new GravitySource(system));
            var propagator = new EventDrivenPropagator(physics);
            propagator.TimedEvents.Add(new TimedEvent
            {
                TimeSeconds = 1000.0,
                DeltaMass = -200.0,
                MinimumMassKg = 900.0
            });

            var ship = new Spacecraft(pos0, vel0, TestScene.ShipMass);
            Assert.Throws<System.InvalidOperationException>(() => propagator.Propagate(ship, 0d, 2000d));
            output.WriteLine("╨Э╨░╤А╤Г╤И╨╡╨╜╨╕╨╡ MinimumMassKg тАФ ╨│╤А╨╛╨╝╨║╨╕╨╣ throw, ╨║╨░╨║ ╤В╤А╨╡╨▒╤Г╨╡╤В╤Б╤П.");
        }
    }
}
