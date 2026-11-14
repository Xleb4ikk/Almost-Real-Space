using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Appendix C, C5': the ported WH-BM14A through the REAL production wrapper
    /// (SpacecraftPhysics + GravitySource + ToleranceFrameSystem), not the bare Core
    /// stepper. In pure central gravity the Kepler split is exact, so the closed-form
    /// Kepler solution is an independent reference.
    /// </summary>
    public sealed class WhBm14aProductionTests
    {
        private readonly ITestOutputHelper output;

        public WhBm14aProductionTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void LocalFrameMatchesAnalyticKeplerForOnePeriod()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            double periapsis = 1.0e7d * 0.5d;
            double apoapsis = 1.0e7d * 1.5d;
            TestScene.EllipticalOrbit(periapsis, apoapsis, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);
            double period = TestScene.EllipticalPeriod(periapsis, apoapsis, TestScene.Mu);

            var physics = new SpacecraftPhysics
            {
                Integrator = SpacecraftIntegrator.WhBm14a,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);
            var initial = new SpacecraftIntegrationState
            {
                Position = position,
                Velocity = velocity,
                Mass = TestScene.ShipMass
            };

            const double chunkDt = 600d;
            double time = 0d;
            double maxPositionError = 0d;
            double maxVelocityError = 0d;
            while (time < period)
            {
                double dt = Math.Min(chunkDt, period - time);
                physics.Step(ship, time, dt);
                time += dt;
                SpacecraftIntegrationState reference = initial;
                KeplerUniversalDrift.Propagate(TestScene.Mu, ref reference, time);
                maxPositionError = Math.Max(maxPositionError,
                    (ship.Position - reference.Position).Magnitude);
                maxVelocityError = Math.Max(maxVelocityError,
                    (ship.Velocity - reference.Velocity).Magnitude);
            }

            output.WriteLine("WH-BM14A local: max |dP|=" + maxPositionError.ToString("E3")
                + " m, max |dV|=" + maxVelocityError.ToString("E3") + " m/s");
            Assert.True(maxPositionError < 1e-6d,
                "WH-BM14A position error " + maxPositionError.ToString("E3") + " m");
            Assert.True(maxVelocityError < 1e-9d,
                "WH-BM14A velocity error " + maxVelocityError.ToString("E3") + " m/s");
            Assert.Equal(0L, physics.StepDiagnostics.Fallbacks);
        }

        [Fact]
        public void AbsolutePathFallsBackToDop853()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            TestScene.EllipticalOrbit(1.0e7d * 0.5d, 1.0e7d * 1.5d, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);

            var physics = new SpacecraftPhysics
            {
                Integrator = SpacecraftIntegrator.WhBm14a
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);
            physics.Step(ship, 0d, 600d);

            Assert.True(physics.StepDiagnostics.Fallbacks > 0L,
                "the absolute path must fall back to DOP853");
            Assert.True(ship.Position.IsFinite && ship.Velocity.IsFinite,
                "fallback produced a non-finite state");
        }

        [Fact]
        public void DenseSegmentIsProducedForRendering()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            TestScene.EllipticalOrbit(1.0e7d * 0.5d, 1.0e7d * 1.5d, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);

            var physics = new SpacecraftPhysics
            {
                Integrator = SpacecraftIntegrator.WhBm14a,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);
            var segments = new List<DenseSegment>();
            physics.StepWithSegments(ship, 0d, 600d, segments);

            Assert.Single(segments);
            Assert.Equal(ship.Position.X, segments[0].Y1.Position.X);
            Assert.Equal(ship.Position.Y, segments[0].Y1.Position.Y);
            Assert.Equal(ship.Position.Z, segments[0].Y1.Position.Z);
        }

        [Fact]
        public void TouchdownEventTimeMatchesDop853()
        {
            double wh = RunTouchdown(SpacecraftIntegrator.WhBm14a, false);
            double dop = RunTouchdown(SpacecraftIntegrator.Dop853, true);
            output.WriteLine("touchdown WH=" + wh.ToString("R") + " s, DOP853=" + dop.ToString("R") + " s");
            Assert.True(Math.Abs(wh - dop) <= 1e-3d,
                "event time difference " + Math.Abs(wh - dop).ToString("E3") + " s");
        }

        private static double RunTouchdown(SpacecraftIntegrator integrator, bool dense)
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            system.Root.Radius = 6.371e6d;
            var physics = new SpacecraftPhysics
            {
                Integrator = integrator,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            var propagator = new EventDrivenPropagator(physics)
            {
                UseDenseRefinement = dense
            };
            propagator.CrossingDetectors.Add(AltitudeCrossingDetector.ForTouchdown(system.Root));
            var ship = new Spacecraft(
                new Vector3d(system.Root.Radius + 400_000d, 0d, 0d),
                new Vector3d(0d, -100d, 0d),
                TestScene.ShipMass);
            EventOccurrence? occurrence = propagator.Propagate(ship, 0d, 6000d);
            Assert.True(occurrence.HasValue, "touchdown event was not reached");
            return occurrence.Value.TimeSeconds;
        }

        [Fact]
        public void RepeatedRunsAreBitwiseIdentical()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            TestScene.EllipticalOrbit(1.0e7d * 0.5d, 1.0e7d * 1.5d, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);

            Spacecraft a = RunWhOnce(system, position, velocity);
            Spacecraft b = RunWhOnce(system, position, velocity);
            Assert.Equal(a.Position.X, b.Position.X);
            Assert.Equal(a.Position.Y, b.Position.Y);
            Assert.Equal(a.Position.Z, b.Position.Z);
            Assert.Equal(a.Velocity.X, b.Velocity.X);
            Assert.Equal(a.Velocity.Y, b.Velocity.Y);
            Assert.Equal(a.Velocity.Z, b.Velocity.Z);
            Assert.Equal(a.Mass, b.Mass);
        }

        [Fact]
        public void AllocationsAreNotHigherThanDop853()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            TestScene.EllipticalOrbit(1.0e7d * 0.5d, 1.0e7d * 1.5d, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);

            long wh = AllocFor(system, position, velocity, SpacecraftIntegrator.WhBm14a);
            long dop = AllocFor(system, position, velocity, SpacecraftIntegrator.Dop853);
            output.WriteLine("alloc WH=" + wh + " B, DOP853=" + dop + " B");
            Assert.True(wh <= dop, "WH allocated " + wh + " B vs DOP853 " + dop + " B");
        }

        private static Spacecraft RunWhOnce(
            StarSystem system, Vector3d position, Vector3d velocity)
        {
            var physics = new SpacecraftPhysics
            {
                Integrator = SpacecraftIntegrator.WhBm14a,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);
            for (int i = 0; i < 20; i++)
            {
                physics.Step(ship, i * 600d, 600d);
            }

            return ship;
        }

        private static long AllocFor(
            StarSystem system, Vector3d position, Vector3d velocity,
            SpacecraftIntegrator integrator)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var physics = new SpacecraftPhysics
            {
                Integrator = integrator,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);
            for (int i = 0; i < 50; i++)
            {
                physics.Step(ship, i * 600d, 600d);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [Fact]
        public void J2AbsoluteErrorStaysBelowOneMetre()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            system.Root.Radius = 6.371e6d;
            system.Root.J2 = 1.08263e-3d;
            double radius = system.Root.Radius + 400_000d;
            double circular = Math.Sqrt(TestScene.Mu / radius);
            var startPosition = new Vector3d(radius, 0d, 0d);
            var startVelocity = new Vector3d(0d, 0d, circular);

            Spacecraft wh = RunOneDay(system, startPosition, startVelocity, SpacecraftIntegrator.WhBm14a);
            Spacecraft dop = RunOneDay(system, startPosition, startVelocity, SpacecraftIntegrator.Dop853);
            double dp = (wh.Position - dop.Position).Magnitude;
            output.WriteLine("J2 one day: WH vs DOP853 dp=" + dp.ToString("E3") + " m");
            Assert.True(dp < 1d, "WH-BM14A J2 absolute error " + dp.ToString("E3") + " m");
        }

        private static Spacecraft RunOneDay(
            StarSystem system, Vector3d position, Vector3d velocity, SpacecraftIntegrator integrator)
        {
            var physics = new SpacecraftPhysics
            {
                Integrator = integrator,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);
            const double chunk = 600d;
            double end = 86400d;
            for (double t = 0d; t < end; t += chunk)
            {
                physics.Step(ship, t, Math.Min(chunk, end - t));
            }

            return ship;
        }

        [Fact]
        public void TimedEventIsReachedWithWhBm14a()
        {
            StarSystem system = TestScene.CreateSingleBodySystem();
            TestScene.EllipticalOrbit(1.0e7d * 0.5d, 1.0e7d * 1.5d, TestScene.Mu,
                out Vector3d position, out Vector3d velocity);

            var physics = new SpacecraftPhysics
            {
                Integrator = SpacecraftIntegrator.WhBm14a,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));
            var propagator = new EventDrivenPropagator(physics)
            {
                UseDenseRefinement = false
            };
            propagator.TimedEvents.Add(new TimedEvent
            {
                TimeSeconds = 1234.5d,
                DeltaMass = 0d,
                MinimumMassKg = 0d,
                DeltaVelocity = Vector3d.Zero
            });
            var ship = new Spacecraft(position, velocity, TestScene.ShipMass);
            EventOccurrence? occurrence = propagator.Propagate(ship, 0d, 5000d);

            Assert.True(occurrence.HasValue, "timed event was not reached");
            Assert.True(Math.Abs(occurrence.Value.TimeSeconds - 1234.5d) <= 1e-6d,
                "event time " + occurrence.Value.TimeSeconds.ToString("R"));
            Assert.True(ship.Position.IsFinite && ship.Velocity.IsFinite,
                "event path produced a non-finite state");
        }
    }
}
