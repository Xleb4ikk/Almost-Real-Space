using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Test G — priority plus zero-time-loop protection. Two detectors on the
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
            Assert.True(first.HasValue, "Первое пересечение не найдено.");
            Assert.Equal("HighPriority", first.Value.DetectorName);
            double t1 = first.Value.TimeSeconds;

            // Restart from the root: the same root must not be re-detected.
            EventOccurrence? second = propagator.Propagate(ship, t1, period - t1);
            Assert.True(second.HasValue, "Второе пересечение не найдено.");
            double t2 = second.Value.TimeSeconds;
            Assert.True(t2 > t1, $"Рестарт не продвинулся: t2 = {t2:F6} <= t1 = {t1:F6} — zero-time loop.");
            Assert.Equal("HighPriority", second.Value.DetectorName);

            double r1 = System.Math.Abs(first.Value.State.Position.Magnitude - shell);
            double r2 = System.Math.Abs(second.Value.State.Position.Magnitude - shell);

            output.WriteLine($"t1 = {t1:F6} с (r-ош. {r1:G} м), t2 = {t2:F6} с (r-ош. {r2:G} м). Оба — HighPriority, зацикливания нет.");
            Assert.True(r1 < 1.0, $"Первый корень не на оболочке: {r1:G} м.");
            Assert.True(r2 < 1.0, $"Второй корень не на оболочке: {r2:G} м.");
        }

        [Fact]
        public void LowPriorityDetectorAlone_FindsSameRoot()
        {
            // Control for the coincident case above: proves LowPriority really
            // fires on the same root (rather than staying silent and letting
            // HighPriority win by default). Same chunking, same bisection — the
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
            Assert.True(combined.HasValue, "Комбинированный прогон не нашёл корень.");
            double tCombined = combined.Value.TimeSeconds;

            var lonePhysics = new SpacecraftPhysics();
            lonePhysics.Sources.Add(new GravitySource(system));
            var lonePropagator = new EventDrivenPropagator(lonePhysics);
            lonePropagator.CrossingDetectors.Add(new AltitudeCrossingDetector(
                star, shell - star.Radius, EventDirection.Either, 5, "LowPriority", EventKind.AtmosphereEntry));

            var loneShip = new Spacecraft(pos0, vel0, TestScene.ShipMass);
            EventOccurrence? lone = lonePropagator.Propagate(loneShip, 0d, period);
            Assert.True(lone.HasValue, "Одинокий LowPriority не фаерит — победа HighPriority была бы по умолчанию.");
            Assert.Equal("LowPriority", lone.Value.DetectorName);

            double dt = System.Math.Abs(lone.Value.TimeSeconds - tCombined);
            output.WriteLine($"Комбинированный: t = {tCombined:F6} с (HighPriority), одинокий Low: t = {lone.Value.TimeSeconds:F6} с. Расхождение: {dt:G} с.");
            Assert.True(dt < 1e-3, $"Low фаерит не на том же корне: расхождение {dt:G} с.");
        }
    }
}
