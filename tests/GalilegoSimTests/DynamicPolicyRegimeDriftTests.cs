using System;
using System.Globalization;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// D2/T3 (hypothesis G1): the game selects the integrator once per
    /// Propagate/AdvanceToTarget call, while long warp can run to the horizon
    /// (TASK_D.md §6: 30 days here, because the stock scenes have no baked
    /// ephemerides -> BakedEndSeconds() is +inf). This test selects ONCE on a state
    /// inside the Ported band (R1) and then records altitude and osculating e on
    /// every 600 s chunk for one 30-day warp.
    ///
    /// It is a measurement, not a gate: TASK_D.md D2 says a test that exposes a
    /// defect is not committed red; the numbers go to PROGRESS.md and the fix goes
    /// to D3. Scenarios and orbit types are those of the stand catalogue S01, S02,
    /// S03, S09 (LEO 400 km circular / polar / sun-synchronous J2 / LEO+J2); the
    /// altitudes and e come from TASK_D.md D2 T3.
    /// </summary>
    public sealed class DynamicPolicyRegimeDriftTests
    {
        private const double ChunkSeconds = 600d;
        private const double HorizonSeconds = 30d * 86400d;
        private const double AltitudeMin = 200_000d;
        private const double AltitudeMax = 2_000_000d;
        private const double MaxEccentricity = 0.05d;
        private const double EarthMu = 3.986004418e14d;
        private const double EarthRadius = 6.371e6d;
        private const double SunMu = 1.32712440018e20d;
        private const double MoonMu = 4.9028695e12d;

        private readonly ITestOutputHelper output;

        public DynamicPolicyRegimeDriftTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void PortedRegimeInvariantOverOneThirtyDayWarp()
        {
            var scenarios = new (string Id, double InclinationDegrees, bool J2)[]
            {
                ("S01", 0d, false),
                ("S02", 90d, false),
                ("S03", 98d, true),
                ("S09", 0d, true)
            };
            double[] altitudes = { 200_000d, 400_000d, 2_000_000d };
            double[] eccentricities = { 0d, 0.04d };
            int cases = 0;
            int broken = 0;

            foreach ((string id, double inclinationDegrees, bool j2) in scenarios)
            {
                foreach (double altitude in altitudes)
                {
                    foreach (double eccentricity in eccentricities)
                    {
                        cases++;
                        if (!RunCase(id, inclinationDegrees, j2, altitude, eccentricity))
                        {
                            broken++;
                        }
                    }
                }
            }

            output.WriteLine("T3 summary: cases=" + cases + ", invariant broken in " + broken + " case(s)");
            Assert.True(cases == 24, "expected 24 cases, got " + cases);
        }

        private bool RunCase(
            string id, double inclinationDegrees, bool j2, double altitude, double eccentricity)
        {
            StarSystem system = CreateSunEarthMoon(j2);
            OrbitingBody earth = system.AllBodies[1];
            earth.EvaluateWorldState(0d, out Vector3d earthPosition, out Vector3d earthVelocity);

            double radius = earth.Radius + altitude;
            double speed = Math.Sqrt(EarthMu * (1d + eccentricity) / radius);
            double inclination = inclinationDegrees * Math.PI / 180d;
            var offset = new Vector3d(radius, 0d, 0d);
            var relativeVelocity = new Vector3d(
                0d, speed * Math.Cos(inclination), speed * Math.Sin(inclination));
            var ship = new Spacecraft(
                earthPosition + offset, earthVelocity + relativeVelocity, 1000d);

            OrbitalElements elements = OrbitalElements.FromState(
                offset, relativeVelocity, EarthMu);
            double startEccentricity = elements.IsValid ? elements.Eccentricity : double.NaN;

            // One selection per Propagate/AdvanceToTarget, exactly as the game does.
            var context = new IntegratorContext(
                ChunkSeconds, false, false, false, true, altitude, 0d,
                EarthMu, startEccentricity, true);
            SpacecraftIntegrator selected = IntegratorPolicy.Select(context);

            var physics = new SpacecraftPhysics
            {
                Integrator = selected,
                ToleranceFrameSystem = system
            };
            physics.Sources.Add(new GravitySource(system));

            double minAltitude = double.PositiveInfinity;
            double maxAltitude = double.NegativeInfinity;
            double maxEccentricity = 0d;
            double firstViolationTime = double.NaN;
            double firstViolationAltitude = double.NaN;
            double firstViolationEccentricity = double.NaN;
            long chunks = 0L;
            bool finite = true;

            for (double time = 0d; time < HorizonSeconds - 0.5d * ChunkSeconds; time += ChunkSeconds)
            {
                physics.Step(ship, time, ChunkSeconds);
                chunks++;
                if (!ship.Position.IsFinite || !ship.Velocity.IsFinite)
                {
                    finite = false;
                    break;
                }

                double next = time + ChunkSeconds;
                earth.EvaluateWorldState(next, out Vector3d bodyPosition, out Vector3d bodyVelocity);
                Vector3d relativePosition = ship.Position - bodyPosition;
                Vector3d relativeVelocityAtChunk = ship.Velocity - bodyVelocity;
                double altitudeNow = relativePosition.Magnitude - earth.Radius;
                OrbitalElements atChunk = OrbitalElements.FromState(
                    relativePosition, relativeVelocityAtChunk, EarthMu);
                double eccentricityNow = atChunk.IsValid ? atChunk.Eccentricity : double.NaN;
                minAltitude = Math.Min(minAltitude, altitudeNow);
                maxAltitude = Math.Max(maxAltitude, altitudeNow);
                if (double.IsFinite(eccentricityNow))
                {
                    maxEccentricity = Math.Max(maxEccentricity, eccentricityNow);
                }

                bool outside = altitudeNow < AltitudeMin
                    || altitudeNow > AltitudeMax
                    || !(eccentricityNow >= 0d)
                    || eccentricityNow >= MaxEccentricity;
                if (outside && double.IsNaN(firstViolationTime))
                {
                    firstViolationTime = next;
                    firstViolationAltitude = altitudeNow;
                    firstViolationEccentricity = eccentricityNow;
                }
            }

            string verdict = double.IsNaN(firstViolationTime) ? "holds" : "BROKEN";
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "T3 {0} alt0={1:F0} m e0={2:F3} j2={3} -> selected={4}, chunks={5}, " +
                "alt[min={6:F0}, max={7:F0}] m, e_max={8:F4}, first violation t={9} s " +
                "(alt={10} m, e={11}) -> {12}",
                id, altitude, eccentricity, j2 ? 1 : 0, selected, chunks,
                minAltitude, maxAltitude, maxEccentricity,
                double.IsNaN(firstViolationTime) ? "none" : firstViolationTime.ToString("F0", CultureInfo.InvariantCulture),
                double.IsNaN(firstViolationAltitude) ? "-" : firstViolationAltitude.ToString("F0", CultureInfo.InvariantCulture),
                double.IsNaN(firstViolationEccentricity) ? "-" : firstViolationEccentricity.ToString("F4", CultureInfo.InvariantCulture),
                verdict));

            Assert.True(finite, id + " alt=" + altitude + " produced a non-finite state");
            Assert.True(chunks > 0L, id + " alt=" + altitude + " did not advance");
            Assert.True(selected == SpacecraftIntegrator.WhBm14a,
                id + " alt=" + altitude + " e=" + eccentricity + " selected " + selected +
                " instead of WH-BM14A; start e=" + startEccentricity.ToString("R", CultureInfo.InvariantCulture));
            return double.IsNaN(firstViolationTime);
        }

        private static StarSystem CreateSunEarthMoon(bool earthJ2)
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
            if (earthJ2)
            {
                earth.J2 = 1.08262668e-3d;
                earth.J2ReferenceRadius = EarthRadius;
            }

            var moon = new OrbitingBody
            {
                Name = "Moon",
                StandardGravitationalParameter = MoonMu,
                Radius = 1.7374e6d,
                SemiMajorAxis = 3.844e8d,
                Eccentricity = 0.0549d,
                InclinationDegrees = 5.145d,
                Parent = earth,
                EpochTimeSeconds = 0d
            };

            sun.Children.Add(earth);
            earth.Children.Add(moon);
            sun.SyncMassFromGravitationalParameter();
            earth.SyncMassFromGravitationalParameter();
            moon.SyncMassFromGravitationalParameter();
            return new StarSystem(sun);
        }
    }
}
