using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Fast production-path check for the local-frame step: a ship in LEO around
    /// a planet at 1 AU, where the absolute frame loses ulp(1.5e11) per step.
    /// The local path must match a tight local reference and beat the absolute path.
    /// </summary>
    public sealed class LocalFrameTests
    {
        private readonly ITestOutputHelper output;

        public LocalFrameTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void LocalFrameBeatsTheAbsolutePathAtOneAu()
        {
            StarSystem system = CreatePlanetSystem(out OrbitingBody planet);
            system.EvaluateBodyState(planet, 0d, out Vector3d planetP, out Vector3d planetV);
            const double radius = 7.0e6d;
            double speed = System.Math.Sqrt(planet.StandardGravitationalParameter / radius);
            var ship0 = new Spacecraft(
                planetP + new Vector3d(radius, 0d, 0d),
                planetV + new Vector3d(0d, speed, 0d),
                TestScene.ShipMass);

            var local = new SpacecraftPhysics { ToleranceFrameSystem = system };
            local.Sources.Add(new GravitySource(system));
            var reference = new SpacecraftPhysics
            {
                ToleranceFrameSystem = system,
                PositionAbsoluteTolerance = 1e-9d,
                VelocityAbsoluteTolerance = 1e-12d,
                MassAbsoluteTolerance = 1e-9d,
                RelativeTolerance = 1e-14d
            };
            reference.Sources.Add(new GravitySource(system));
            var absolute = new SpacecraftPhysics();
            absolute.Sources.Add(new GravitySource(system));

            var localShip = new Spacecraft(ship0.Position, ship0.Velocity, ship0.Mass);
            var referenceShip = new Spacecraft(ship0.Position, ship0.Velocity, ship0.Mass);
            var absoluteShip = new Spacecraft(ship0.Position, ship0.Velocity, ship0.Mass);

            const double chunkDt = 3600d;
            const int chunks = 24;
            double time = 0d;
            for (int i = 0; i < chunks; i++)
            {
                local.Step(localShip, time, chunkDt);
                reference.Step(referenceShip, time, chunkDt);
                absolute.Step(absoluteShip, time, chunkDt);
                time += chunkDt;
            }

            double localError = (localShip.Position - referenceShip.Position).Magnitude;
            double absoluteError = (absoluteShip.Position - referenceShip.Position).Magnitude;
            output.WriteLine("local vs tight local reference = " + localError.ToString("E3") +
                             " m; absolute vs reference = " + absoluteError.ToString("E3") + " m.");

            Assert.True(localError < 1d,
                "local-frame error " + localError.ToString("E3") + " m exceeds 1 m.");
            Assert.True(localError < absoluteError * 0.1d,
                "local-frame error " + localError.ToString("E3") +
                " m is not at least 10x better than the absolute path " +
                absoluteError.ToString("E3") + " m.");
        }

        private static StarSystem CreatePlanetSystem(out OrbitingBody planet)
        {
            var star = new OrbitingBody
            {
                Name = "Star",
                StandardGravitationalParameter = 1.32712440018e20d,
                Radius = 7.0e8d
            };
            planet = new OrbitingBody
            {
                Name = "Planet",
                Parent = star,
                StandardGravitationalParameter = 3.986004418e14d,
                Radius = 7.0e6d,
                SemiMajorAxis = 1.5e11d,
                MeanAnomalyAtEpochDegrees = 0d
            };
            star.Children.Add(planet);
            return new StarSystem(star);
        }
    }
}
