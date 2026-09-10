using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Тест H — смена SOI. Минимальная система без лун (звезда + одна планета
    /// на круговой орбите): корабль стартует внутри SOI планеты на радиальной
    /// траектории убегания и ровно один раз переходит под доминанту звезды.
    /// Брекет перехода находится грубым сканированием в самом тесте, драйвер
    /// обязан вернуть событие внутри брекета; доминанты по сторонам корня
    /// обязаны различаться (планета → звезда).
    /// </summary>
    public sealed class SoiChangeTests
    {
        private readonly ITestOutputHelper output;

        public SoiChangeTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static StarSystem CreateTwoBodySystem(out OrbitingBody star, out OrbitingBody planet)
        {
            star = new OrbitingBody
            {
                Name = "TestStar",
                StandardGravitationalParameter = 1.327e20
            };
            planet = new OrbitingBody
            {
                Name = "TestPlanet",
                StandardGravitationalParameter = 3.986e14,
                Radius = 6.371e6,
                SemiMajorAxis = 1.5e11,
                Eccentricity = 0d,
                InclinationDegrees = 0d,
                LongitudeOfAscendingNodeDegrees = 0d,
                ArgumentOfPeriapsisDegrees = 0d,
                MeanAnomalyAtEpochDegrees = 0d,
                EpochTimeSeconds = 0d,
                Parent = star
            };
            star.Children.Add(planet);
            return new StarSystem(star);
        }

        [Fact]
        public void EscapeFromSoi_ReportsTransitionInsideBracket()
        {
            StarSystem system = CreateTwoBodySystem(out OrbitingBody star, out OrbitingBody planet);
            double soi = planet.SphereOfInfluenceRadius;
            output.WriteLine($"SOI планеты: {soi:E3} м.");

            planet.EvaluateWorldState(0d, out Vector3d planetPos, out Vector3d planetVel);
            Vector3d pos0 = planetPos + new Vector3d(1.0e8, 0d, 0d);
            Vector3d vel0 = planetVel + new Vector3d(3500d, 0d, 0d);

            Assert.Same(planet, system.FindDominantBody(pos0, 0d));

            // Грубое сканирование брекета в самом тесте.
            var scanPhysics = new SpacecraftPhysics();
            scanPhysics.Sources.Add(new GravitySource(system));
            var probe = new Spacecraft(pos0, vel0, TestScene.ShipMass);
            double tPrev = 0d;
            double tNow = 0d;
            const double scanDt = 3600d;
            bool flipped = false;
            for (int i = 0; i < 200; i++)
            {
                scanPhysics.Step(probe, tNow, scanDt);
                tPrev = tNow;
                tNow += scanDt;
                if (!ReferenceEquals(system.FindDominantBody(probe.Position, tNow), planet))
                {
                    flipped = true;
                    break;
                }
            }
            Assert.True(flipped, "Скан не нашёл выхода из SOI — неверная сцена.");
            output.WriteLine($"Брекет перехода: [{tPrev:F0}, {tNow:F0}] с.");

            // Драйвер поверх той же физики.
            var physics = new SpacecraftPhysics();
            physics.Sources.Add(new GravitySource(system));
            var propagator = new EventDrivenPropagator(physics);
            propagator.TransitionDetectors.Add(new SoiChangeDetector(system));

            var ship = new Spacecraft(pos0, vel0, TestScene.ShipMass);
            EventOccurrence? occurrence = propagator.Propagate(ship, 0d, tNow + scanDt);

            Assert.True(occurrence.HasValue, "Событие смены SOI не найдено.");
            double tEvent = occurrence.Value.TimeSeconds;
            output.WriteLine($"Детект: t = {tEvent:F6} с, новый доминант: {occurrence.Value.Body?.Name}.");

            Assert.True(tEvent >= tPrev && tEvent <= tNow,
                $"Корень {tEvent:F3} с вне брекета [{tPrev:F0}, {tNow:F0}].");
            Assert.Same(star, occurrence.Value.Body);

            const double eps = 1.0;
            OrbitingBody before = system.FindDominantBody(occurrence.Value.State.Position, tEvent - eps);
            Assert.Same(planet, before);
        }
    }
}
