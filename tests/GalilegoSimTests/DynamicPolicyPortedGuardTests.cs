using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// D3/G1 fix: the policy chooses WH-BM14A once per Propagate/AdvanceToTarget, but a
    /// long warp can leave the certified band (200-2000 km, 0 &lt;= e &lt; 0.05,
    /// DECISION_TABLE.md R1). SpacecraftPhysics now checks the invariants at the start
    /// of every WH chunk and falls back to DOP853 for that chunk (Fallbacks++).
    /// The pre-fix behaviour is measured in D2/T3 (11 of 24 cases left the band while
    /// WH kept integrating); this test pins the fallback.
    /// </summary>
    public sealed class DynamicPolicyPortedGuardTests
    {
        private const double EarthMu = 3.986004418e14d;
        private const double EarthRadius = 6.371e6d;
        private const double SunMu = 1.32712440018e20d;

        private readonly ITestOutputHelper output;

        public DynamicPolicyPortedGuardTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void InBandChunkStaysOnWhWithoutFallback()
        {
            StarSystem system = CreateSunEarth();
            OrbitingBody earth = system.AllBodies[1];
            SpacecraftPhysics physicsUnderTest = CreatePhysics(system);
            Orbit start = OrbitAt(earth, 400_000d, 0d, 0d);
            var ship = start.CreateShip();

            physicsUnderTest.Step(ship, 0d, 600d);

            output.WriteLine("guard in-band: fallbacks=" + physicsUnderTest.StepDiagnostics.Fallbacks);
            Assert.Equal(0L, physicsUnderTest.StepDiagnostics.Fallbacks);
        }

        [Fact]
        public void ChunkStartingAboveTheBandFallsBackToDop853()
        {
            StarSystem system = CreateSunEarth();
            OrbitingBody earth = system.AllBodies[1];
            Orbit start = OrbitAt(earth, 2_500_000d, 0d, 0d);

            Spacecraft whShip = start.CreateShip();
            Spacecraft dopShip = start.CreateShip();

            SpacecraftPhysics wh = CreatePhysics(system);
            wh.Step(whShip, 0d, 600d);

            SpacecraftPhysics dop = CreatePhysics(system, SpacecraftIntegrator.Dop853);
            dop.Step(dopShip, 0d, 600d);

            output.WriteLine("guard above band: fallbacks=" + wh.StepDiagnostics.Fallbacks
                + ", dP=" + (whShip.Position - dopShip.Position).Magnitude.ToString("E3"));
            Assert.Equal(1L, wh.StepDiagnostics.Fallbacks);
            Assert.Equal(dopShip.Position.X, whShip.Position.X);
            Assert.Equal(dopShip.Position.Y, whShip.Position.Y);
            Assert.Equal(dopShip.Position.Z, whShip.Position.Z);
            Assert.Equal(dopShip.Velocity.X, whShip.Velocity.X);
            Assert.Equal(dopShip.Velocity.Y, whShip.Velocity.Y);
            Assert.Equal(dopShip.Velocity.Z, whShip.Velocity.Z);
        }

        [Fact]
        public void EccentricChunkFallsBackToDop853()
        {
            StarSystem system = CreateSunEarth();
            OrbitingBody earth = system.AllBodies[1];
            Orbit start = OrbitAt(earth, 400_000d, 0.2d, 0d);
            var ship = start.CreateShip();

            SpacecraftPhysics physicsUnderTest = CreatePhysics(system);
            physicsUnderTest.Step(ship, 0d, 600d);

            output.WriteLine("guard e=0.2: fallbacks=" + physicsUnderTest.StepDiagnostics.Fallbacks);
            Assert.Equal(1L, physicsUnderTest.StepDiagnostics.Fallbacks);
        }

        [Fact]
        public void DriftOutOfBandFallsBackOnTheNextChunk()
        {
            StarSystem system = CreateSunEarth();
            OrbitingBody earth = system.AllBodies[1];
            Orbit start = OrbitAt(earth, 2_000_000d, 0.04d, 0d);
            var ship = start.CreateShip();
            SpacecraftPhysics physicsUnderTest = CreatePhysics(system);

            physicsUnderTest.Step(ship, 0d, 600d);
            long afterFirst = physicsUnderTest.StepDiagnostics.Fallbacks;
            physicsUnderTest.Step(ship, 600d, 600d);
            long afterSecond = physicsUnderTest.StepDiagnostics.Fallbacks;

            output.WriteLine("guard drift: fallbacks after chunk 1 = " + afterFirst
                + ", after chunk 2 = " + afterSecond);
            Assert.Equal(0L, afterFirst);
            Assert.Equal(1L, afterSecond);
        }

        private static SpacecraftPhysics CreatePhysics(
            StarSystem system, SpacecraftIntegrator integrator = SpacecraftIntegrator.WhBm14a)
        {
            var physics = new SpacecraftPhysics
            {
                Integrator = integrator,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            return physics;
        }

        private static StarSystem CreateSunEarth()
        {
            var sun = new OrbitingBody
            {
                Name = "Sun",
                StandardGravitationalParameter = SunMu,
                Radius = 6.957e8d
            };
            var earth = new OrbitingBody
            {
                Name = "Earth",
                StandardGravitationalParameter = EarthMu,
                Radius = EarthRadius,
                SemiMajorAxis = 1.495978707e11d,
                Eccentricity = 0d,
                InclinationDegrees = 0d,
                Parent = sun,
                EpochTimeSeconds = 0d
            };
            sun.Children.Add(earth);
            sun.SyncMassFromGravitationalParameter();
            earth.SyncMassFromGravitationalParameter();
            return new StarSystem(sun);
        }

        private static Orbit OrbitAt(
            OrbitingBody body, double altitude, double eccentricity, double inclinationDegrees)
        {
            body.EvaluateWorldState(0d, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            double radius = body.Radius + altitude;
            double speed = Math.Sqrt(body.StandardGravitationalParameter * (1d + eccentricity) / radius);
            double inclination = inclinationDegrees * Math.PI / 180d;
            return new Orbit(
                bodyPosition + new Vector3d(radius, 0d, 0d),
                bodyVelocity + new Vector3d(
                    0d, speed * Math.Cos(inclination), speed * Math.Sin(inclination)),
                1000d);
        }

        private readonly struct Orbit
        {
            private readonly Vector3d position;
            private readonly Vector3d velocity;
            private readonly double mass;

            public Orbit(Vector3d position, Vector3d velocity, double mass)
            {
                this.position = position;
                this.velocity = velocity;
                this.mass = mass;
            }

            public Spacecraft CreateShip()
            {
                return new Spacecraft(position, velocity, mass);
            }
        }
    }
}
