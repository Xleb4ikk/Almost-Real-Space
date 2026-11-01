using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Direct production-path checks: SpacecraftPhysics + GravitySource +
    /// ToleranceFrameSystem, not the bare SpacecraftOrbitIntegrator used by the
    /// Core matrix. The reference here is the closed-form two-body Kepler
    /// solution, independent of the integrator and of the wrapper.
    /// </summary>
    public sealed class ProductionPathTests
    {
        private readonly ITestOutputHelper output;

        public ProductionPathTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const double OrbitA = 1.0e7d;
        private const double OrbitE = 0.5d;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ProductionStepMatchesAnalyticKeplerForOnePeriod(bool useToleranceFrame)
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            double periapsis = OrbitA * (1d - OrbitE);
            double apoapsis = OrbitA * (1d + OrbitE);
            TestScene.EllipticalOrbit(periapsis, apoapsis, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);
            double period = TestScene.EllipticalPeriod(periapsis, apoapsis, TestScene.Mu);

            var physics = new SpacecraftPhysics();
            if (useToleranceFrame)
            {
                physics.ToleranceFrameSystem = system;
            }

            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);

            const double chunkDt = 600d;
            double time = 0d;
            double maxPositionError = 0d;
            double maxVelocityError = 0d;
            while (time < period)
            {
                double dt = Math.Min(chunkDt, period - time);
                physics.Step(ship, time, dt);
                time += dt;

                KeplerState reference = KeplerState.AtTime(OrbitA, OrbitE, TestScene.Mu, time);
                maxPositionError = Math.Max(maxPositionError,
                    (ship.Position - reference.Position).Magnitude);
                maxVelocityError = Math.Max(maxVelocityError,
                    (ship.Velocity - reference.Velocity).Magnitude);
            }

            output.WriteLine(
                "localFrame=" + useToleranceFrame + ": max |dP|=" +
                maxPositionError.ToString("E3") + " m, max |dV|=" +
                maxVelocityError.ToString("E3") + " m/s over one period.");

            Assert.True(maxPositionError < 1e-3d,
                "production position error " + maxPositionError.ToString("E3") +
                " m exceeds 1e-3 m.");
            Assert.True(maxVelocityError < 1e-6d,
                "production velocity error " + maxVelocityError.ToString("E3") +
                " m/s exceeds 1e-6 m/s.");
        }

        [Fact]
        public void ProductionDopri5OptionMatchesAnalyticKepler()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            double periapsis = OrbitA * (1d - OrbitE);
            double apoapsis = OrbitA * (1d + OrbitE);
            TestScene.EllipticalOrbit(periapsis, apoapsis, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);
            double period = TestScene.EllipticalPeriod(periapsis, apoapsis, TestScene.Mu);

            var physics = new SpacecraftPhysics
            {
                Integrator = SpacecraftIntegrator.Dopri5
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);

            const double chunkDt = 600d;
            double time = 0d;
            double maxPositionError = 0d;
            double maxVelocityError = 0d;
            while (time < period)
            {
                double dt = Math.Min(chunkDt, period - time);
                physics.Step(ship, time, dt);
                time += dt;

                KeplerState reference = KeplerState.AtTime(OrbitA, OrbitE, TestScene.Mu, time);
                maxPositionError = Math.Max(maxPositionError,
                    (ship.Position - reference.Position).Magnitude);
                maxVelocityError = Math.Max(maxVelocityError,
                    (ship.Velocity - reference.Velocity).Magnitude);
            }

            output.WriteLine(
                "Dopri5 production: max |dP|=" + maxPositionError.ToString("E3") +
                " m, max |dV|=" + maxVelocityError.ToString("E3") + " m/s.");

            Assert.True(maxPositionError < 1e-1d,
                "DOPRI5 production position error " + maxPositionError.ToString("E3") +
                " m exceeds 1e-1 m.");
            Assert.True(maxVelocityError < 1e-4d,
                "DOPRI5 production velocity error " + maxVelocityError.ToString("E3") +
                " m/s exceeds 1e-4 m/s.");
        }

        [Fact]
        public void ToleranceFrameDoesNotChangeThePhysicalAnswer()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            double periapsis = OrbitA * (1d - OrbitE);
            double apoapsis = OrbitA * (1d + OrbitE);
            TestScene.EllipticalOrbit(periapsis, apoapsis, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);

            var local = new SpacecraftPhysics { ToleranceFrameSystem = system };
            local.Sources.Add(new GravitySource(system));
            var absolute = new SpacecraftPhysics();
            absolute.Sources.Add(new GravitySource(system));
            var localShip = new Spacecraft(position, velocity, TestScene.ShipMass);
            var absoluteShip = new Spacecraft(position, velocity, TestScene.ShipMass);

            const double chunkDt = 600d;
            const int chunks = 6;
            double time = 0d;
            for (int i = 0; i < chunks; i++)
            {
                local.Step(localShip, time, chunkDt);
                absolute.Step(absoluteShip, time, chunkDt);
                time += chunkDt;
            }

            double positionDifference = (localShip.Position - absoluteShip.Position).Magnitude;
            double velocityDifference = (localShip.Velocity - absoluteShip.Velocity).Magnitude;
            output.WriteLine(
                "local vs absolute production path: |dP|=" +
                positionDifference.ToString("E3") + " m, |dV|=" +
                velocityDifference.ToString("E3") + " m/s.");

            Assert.True(positionDifference < 1e-3d,
                "enabling ToleranceFrameSystem changed the position by " +
                positionDifference.ToString("E3") + " m.");
            Assert.True(velocityDifference < 1e-6d,
                "enabling ToleranceFrameSystem changed the velocity by " +
                velocityDifference.ToString("E3") + " m/s.");
        }

        [Fact]
        public void GravitySourceIncludesJ2AtTheAnalyticMagnitude()
        {
            const double radius = 6.371e6d;
            const double j2 = 1.08263e-3d;
            const double r = 7.0e6d;
            var body = new OrbitingBody
            {
                Name = "Oblate",
                StandardGravitationalParameter = TestScene.Mu,
                Radius = radius,
                J2 = j2,
                NorthPoleDirection = new Vector3d(0d, 0d, 1d)
            };
            var system = new StarSystem(body);
            var source = new GravitySource(system);

            double point = TestScene.Mu / (r * r);
            double scale = 1.5d * j2 * TestScene.Mu * radius * radius / (r * r * r * r);

            Vector3d expectedEquator = new Vector3d(-(point + scale), 0d, 0d);
            DynamicsContribution equator = source.Evaluate(
                new Vector3d(r, 0d, 0d), Vector3d.Zero, TestScene.ShipMass, 0d);
            Assert.True((equator.SpecificAcceleration - expectedEquator).Magnitude < 1e-12d,
                "GravitySource equatorial J2 acceleration is " +
                equator.SpecificAcceleration + ", expected " + expectedEquator + ".");

            Vector3d expectedPole = new Vector3d(0d, 0d, -point + (2d * scale));
            DynamicsContribution pole = source.Evaluate(
                new Vector3d(0d, 0d, r), Vector3d.Zero, TestScene.ShipMass, 0d);
            Assert.True((pole.SpecificAcceleration - expectedPole).Magnitude < 1e-12d,
                "GravitySource polar J2 acceleration is " +
                pole.SpecificAcceleration + ", expected " + expectedPole + ".");
        }

        private readonly struct KeplerState
        {
            public readonly Vector3d Position;
            public readonly Vector3d Velocity;

            private KeplerState(Vector3d position, Vector3d velocity)
            {
                Position = position;
                Velocity = velocity;
            }

            public static KeplerState AtTime(double a, double e, double mu, double time)
            {
                double meanMotion = Math.Sqrt(mu / (a * a * a));
                double meanAnomaly = meanMotion * time;
                double eccentricAnomaly = meanAnomaly;
                for (int i = 0; i < 50; i++)
                {
                    double correction =
                        (eccentricAnomaly - (e * Math.Sin(eccentricAnomaly)) - meanAnomaly) /
                        (1d - (e * Math.Cos(eccentricAnomaly)));
                    eccentricAnomaly -= correction;
                    if (Math.Abs(correction) < 1e-15d)
                    {
                        break;
                    }
                }

                double cosE = Math.Cos(eccentricAnomaly);
                double sinE = Math.Sin(eccentricAnomaly);
                double radius = a * (1d - (e * cosE));
                double scale = a * meanMotion * (a / radius);
                return new KeplerState(
                    new Vector3d(a * (cosE - e), a * Math.Sqrt(1d - (e * e)) * sinE, 0d),
                    new Vector3d(-scale * sinE, scale * Math.Sqrt(1d - (e * e)) * cosE, 0d));
            }
        }
    }
}
