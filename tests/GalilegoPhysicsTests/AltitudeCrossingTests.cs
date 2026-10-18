using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Test E — altitude-shell crossing. An elliptical orbit (rp = 7e6,
    /// ra = 9e6) crosses the 7.5e6 shell twice per revolution; the first
    /// (rising) crossing is checked against an independent reference — fixed-step
    /// RK4 (a different method, not the same code) with its own bisection.
    /// </summary>
    public sealed class AltitudeCrossingTests
    {
        private readonly ITestOutputHelper output;

        public AltitudeCrossingTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        internal const double Periapsis = 7.0e6;
        internal const double Apoapsis = 9.0e6;
        internal const double ShellRadius = 7.5e6;

        internal static StarSystem CreateSystem(out OrbitingBody star)
        {
            star = new OrbitingBody
            {
                Name = "TestBody",
                StandardGravitationalParameter = TestScene.Mu
            };
            return new StarSystem(star);
        }

        internal static EventDrivenPropagator CreatePropagator(StarSystem system)
        {
            var physics = new SpacecraftPhysics();
            physics.Sources.Add(new GravitySource(system));
            return new EventDrivenPropagator(physics);
        }

        /// <summary>
        /// Independent reference: fixed-step RK4 with a 1 s step up to the bracket,
        /// then 40 bisection iterations re-running RK4 from the start.
        /// </summary>
        internal static double ReferenceCrossingTime(
            StarSystem system, Vector3d pos0, Vector3d vel0, double shellRadius, double scanLimit)
        {
            System.Func<Vector3d, double, Vector3d> accel = (p, t) => system.EvaluateShipAcceleration(p, t);

            Vector3d pos = pos0;
            Vector3d vel = vel0;
            double t = 0d;
            const double dt = 1.0;
            double gPrev = pos.Magnitude - shellRadius;

            while (t < scanLimit)
            {
                IntegrationResult step = PhysicsSolver.RK4(pos, vel, t, dt, accel);
                double gNext = step.Position.Magnitude - shellRadius;
                if (gPrev < 0d && gNext >= 0d)
                {
                    double a = t;
                    double b = t + dt;
                    for (int i = 0; i < 40; i++)
                    {
                        double m = 0.5 * (a + b);
                        Vector3d pm = PropagateRk4(pos0, vel0, m, accel);
                        if (pm.Magnitude - shellRadius < 0d)
                            a = m;
                        else
                            b = m;
                    }
                    return 0.5 * (a + b);
                }
                pos = step.Position;
                vel = step.Velocity;
                t += dt;
                gPrev = gNext;
            }

            throw new System.InvalidOperationException("Референс не нашёл пересечения — неверная сцена.");
        }

        internal static Vector3d PropagateRk4(
            Vector3d pos0, Vector3d vel0, double targetTime,
            System.Func<Vector3d, double, Vector3d> accel)
        {
            Vector3d pos = pos0;
            Vector3d vel = vel0;
            double t = 0d;
            const double dt = 1.0;
            while (t < targetTime)
            {
                double h = System.Math.Min(dt, targetTime - t);
                IntegrationResult step = PhysicsSolver.RK4(pos, vel, t, h, accel);
                pos = step.Position;
                vel = step.Velocity;
                t += h;
            }
            return pos;
        }

        [Fact]
        public void RisingShellCrossing_MatchesIndependentReference()
        {
            StarSystem system = CreateSystem(out OrbitingBody star);
            TestScene.EllipticalOrbit(Periapsis, Apoapsis, TestScene.Mu, out Vector3d pos0, out Vector3d vel0);
            double period = TestScene.EllipticalPeriod(Periapsis, Apoapsis, TestScene.Mu);

            var propagator = CreatePropagator(system);
            propagator.CrossingDetectors.Add(new AltitudeCrossingDetector(
                star, ShellRadius - star.Radius, EventDirection.Either,
                EventPriorities.Atmosphere, "TestShell", EventKind.AtmosphereEntry));

            var ship = new Spacecraft(pos0, vel0, TestScene.ShipMass);
            EventOccurrence? occurrence = propagator.Propagate(ship, 0d, period);

            Assert.True(occurrence.HasValue, "Событие пересечения не найдено за виток.");
            double tDetect = occurrence.Value.TimeSeconds;
            double rDetect = occurrence.Value.State.Position.Magnitude;

            double tRef = ReferenceCrossingTime(system, pos0, vel0, ShellRadius, period);
            double timeErr = System.Math.Abs(tDetect - tRef);
            double radiusErr = System.Math.Abs(rDetect - ShellRadius);

            output.WriteLine($"Детект: t = {tDetect:F6} с, r = {rDetect:F4} м.");
            output.WriteLine($"Референс RK4: t = {tRef:F6} с. Ошибка по времени: {timeErr:G} с, по радиусу: {radiusErr:G} м.");

            Assert.True(timeErr < 0.5, $"Время события разошлось с референсом: {timeErr:G} с.");
            Assert.True(radiusErr < 5.0, $"Радиус в корне не на оболочке: {radiusErr:G} м.");
            Assert.Same(star, occurrence.Value.Body);
        }
    }
}
