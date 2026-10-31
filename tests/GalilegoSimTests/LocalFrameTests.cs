using System.Collections.Generic;
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

        [Fact]
        public void LocalFrameMatchesAnIndependentLocalIntegrator()
        {
            StarSystem system = CreatePlanetSystem(out OrbitingBody planet);
            system.EvaluateBodyState(planet, 0d, out Vector3d planetP, out Vector3d planetV);
            const double radius = 7.0e6d;
            double speed = System.Math.Sqrt(planet.StandardGravitationalParameter / radius);
            var localStart = new SpacecraftIntegrationState
            {
                Position = new Vector3d(radius, 0d, 0d),
                Velocity = new Vector3d(0d, speed, 0d),
                Mass = TestScene.ShipMass
            };

            Vector3d reference = PropagateLocalRk4(
                system, planet, localStart.Position, localStart.Velocity, 86400d, 2d);

            var physics = new SpacecraftPhysics { ToleranceFrameSystem = system };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(
                planetP + localStart.Position,
                planetV + localStart.Velocity,
                TestScene.ShipMass);
            const double chunkDt = 3600d;
            const int chunks = 24;
            double time = 0d;
            for (int i = 0; i < chunks; i++)
            {
                physics.Step(ship, time, chunkDt);
                time += chunkDt;
            }

            double error = (ship.LocalPosition - reference).Magnitude;
            output.WriteLine("local vs independent RK4 = " + error.ToString("E3") + " m.");
            Assert.True(error < 1e-2d,
                "local-frame path differs from an independent RK4 reference by " +
                error.ToString("E3") + " m.");
        }

        private static Vector3d PropagateLocalRk4(
            StarSystem system, OrbitingBody planet, Vector3d r0, Vector3d v0,
            double duration, double step)
        {
            Vector3d r = r0;
            Vector3d v = v0;
            double time = 0d;
            while (time < duration)
            {
                double h = System.Math.Min(step, duration - time);
                Vector3d a1 = LocalAcceleration(system, planet, r, time);
                Vector3d k1r = v;
                Vector3d k2r = v + (a1 * (0.5d * h));
                Vector3d a2 = LocalAcceleration(system, planet, r + (k1r * (0.5d * h)), time + (0.5d * h));
                Vector3d k3r = v + (a2 * (0.5d * h));
                Vector3d a3 = LocalAcceleration(system, planet, r + (k2r * (0.5d * h)), time + (0.5d * h));
                Vector3d k4r = v + (a3 * h);
                Vector3d a4 = LocalAcceleration(system, planet, r + (k3r * h), time + h);
                r += (k1r + (k2r * 2d) + (k3r * 2d) + k4r) * (h / 6d);
                v += (a1 + (a2 * 2d) + (a3 * 2d) + a4) * (h / 6d);
                time += h;
            }

            return r;
        }

        private static Vector3d LocalAcceleration(
            StarSystem system, OrbitingBody planet, Vector3d relative, double time)
        {
            system.EvaluateBodyState(planet, time, out Vector3d planetP, out _);
            double muPlanet = planet.StandardGravitationalParameter;
            double muStar = system.Root.StandardGravitationalParameter;
            Vector3d absolute = planetP + relative;
            Vector3d aPlanet = relative * (-muPlanet / System.Math.Pow(relative.Magnitude, 3d));
            Vector3d aStarShip = absolute * (-muStar / System.Math.Pow(absolute.Magnitude, 3d));
            Vector3d aStarPlanet = planetP * (-muStar / System.Math.Pow(planetP.Magnitude, 3d));
            return aPlanet + aStarShip - aStarPlanet;
        }

        [Fact]
        public void LocalFrameDenseSegmentsAreAbsoluteAndContinuous()
        {
            StarSystem system = CreatePlanetSystem(out OrbitingBody planet);
            system.EvaluateBodyState(planet, 0d, out Vector3d planetP, out Vector3d planetV);
            const double radius = 7.0e6d;
            double speed = System.Math.Sqrt(planet.StandardGravitationalParameter / radius);
            var ship = new Spacecraft(
                planetP + new Vector3d(radius, 0d, 0d),
                planetV + new Vector3d(0d, speed, 0d),
                TestScene.ShipMass);

            var local = new SpacecraftPhysics { ToleranceFrameSystem = system };
            local.Sources.Add(new GravitySource(system));
            var track = new List<DenseSegment>();
            var chunkStates = new List<SpacecraftIntegrationState>();
            const double chunkDt = 3600d;
            const int chunks = 24;
            double time = 0d;
            for (int i = 0; i < chunks; i++)
            {
                local.StepWithSegments(ship, time, chunkDt, track);
                time += chunkDt;
                chunkStates.Add(new SpacecraftIntegrationState
                {
                    Position = ship.Position,
                    Velocity = ship.Velocity,
                    Mass = ship.Mass
                });
            }

            Assert.True(track.Count >= chunks,
                "expected at least one dense segment per chunk, got " + track.Count);
            Assert.True(track[0].T0 == 0d, "first segment starts at t=0.");
            Assert.True(System.Math.Abs(track[track.Count - 1].T1 - time) < 1e-6d,
                "last segment ends at the chunk end.");
            Assert.True(track[track.Count - 1].Y1.Position.X == chunkStates[chunks - 1].Position.X &&
                        track[track.Count - 1].Y1.Position.Y == chunkStates[chunks - 1].Position.Y &&
                        track[track.Count - 1].Y1.Position.Z == chunkStates[chunks - 1].Position.Z,
                "last dense endpoint is not the final ship position.");

            foreach (DenseSegment segment in track)
            {
                Assert.True(segment.ReferenceBody != null,
                    "local-frame segment lost its reference body after AsAbsolute.");
                SpacecraftIntegrationState start = DenseSegment.EvaluateTrack(track, segment.T0);
                SpacecraftIntegrationState end = DenseSegment.EvaluateTrack(track, segment.T1);
                Assert.True((start.Position - segment.Y0.Position).Magnitude < 1e-3d,
                    "track start does not match segment Y0.");
                Assert.True((end.Position - segment.Y1.Position).Magnitude < 1e-3d,
                    "track end does not match segment Y1.");
                SpacecraftIntegrationState midpoint =
                    segment.Evaluate(segment.T0 + (0.5d * (segment.T1 - segment.T0)));
                Assert.True(midpoint.Position.IsFinite && midpoint.Velocity.IsFinite,
                    "local+dense midpoint is not finite.");
            }
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

