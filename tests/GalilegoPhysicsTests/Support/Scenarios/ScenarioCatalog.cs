using System;
using System.Collections.Generic;
using Galilego.Core;
using GalilegoPhysicsTests.Support.Oracles;

namespace GalilegoPhysicsTests.Support.Scenarios
{
    internal static class ScenarioCatalog
    {
        private const double Mu = 3.986004418e14d;

        // Restricted N-body accuracy is an absolute requirement, not a fraction
        // of a loose 1% ceiling: 300 m at step endpoints and 600 m through dense
        // output. Both are tighter than the calibrated regression gates.
        private const double RestrictedStepPositionLimitMeters = 300d;
        private const double RestrictedDensePositionLimitMeters = 600d;

        // The flyby oracle is a cubic-Hermite track. Its midpoint error scales
        // as h^4, and the old 0.1 s build sat at the same order as the 1e-9
        // production gate, making the 1e-9/1e-12 rows indistinguishable.
        // Twenty-five milliseconds puts the oracle floor well below the gates.
        public const double RestrictedFlybyOracleStepSeconds = 0.025d;

        /// <summary>
        /// Exact constant-velocity drift. The error estimate is identically zero,
        /// so it exercises the 600 s step cap rather than the adaptive controller;
        /// StepCapTests owns it and it is deliberately outside the matrix.
        /// </summary>
        public static readonly Scenario LinearDrift = CreateLinearDrift();

        /// <summary>
        /// Heliocentric 1 AU circular arc, 30 days. This is deliberately outside
        /// the per-tolerance matrix: at 1 AU the 600 s step cap, not the error
        /// controller, chooses the step, so 1e-6 and 1e-9 produce the same run.
        /// SunRegimeTests owns the claim (step-endpoint accuracy and cost against
        /// DOPRI5 at the same cap), which the Earth-radius matrix cannot make.
        /// </summary>
        public static readonly Scenario SunOneAu = CreateKepler(
            "sun-1au", "Kepler Sun 1 AU circular | 30 d", ScenarioClass.KeplerCircularLong,
            1.495978707e11d, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d, ScenarioTier.Fast,
            new[] { 1e-9d }, 30.0d * 86400.0d, null, 1.32712440018e20d);

        public static readonly Scenario[] All =
        {
            CreateKepler("kepler-circular", "Kepler circular | 1 period", ScenarioClass.KeplerCircular,
                7.0e6d, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d, 1e-12d }),

            CreateKepler("kepler-e01", "Kepler e=0.1 | 1 period", ScenarioClass.KeplerEccentric,
                1.0e7d, 0.1d, 10.0d, 20.0d, 30.0d, 10.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }),

            CreateKepler("kepler-e05", "Kepler e=0.5 | 1 period", ScenarioClass.KeplerEccentric,
                1.0e7d, 0.5d, 25.0d, 40.0d, 60.0d, 20.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d, 1e-12d }),

            CreateKepler("kepler-e09", "Kepler e=0.9 | 1 period", ScenarioClass.KeplerHighEccentric,
                1.0e7d, 0.9d, 0.0d, 0.0d, 0.0d, 0.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }, 0d, new[] { 1e-9d, 1e-12d }),

            CreateKepler("kepler-e099", "Kepler e=0.99 | 1 period", ScenarioClass.KeplerHighEccentric,
                1.0e7d, 0.99d, 0.0d, 0.0d, 0.0d, 0.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }, 0d, new[] { 1e-9d, 1e-12d }),

            CreateKepler("kepler-hyperbolic", "Kepler hyperbolic e=1.5 | 1 h", ScenarioClass.KeplerHyperbolic,
                -1.4e7d, 1.5d, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }, 3600.0d),

            CreateKepler("kepler-near-parabolic", "Kepler near-parabolic e=0.999 | 100 s", ScenarioClass.KeplerNearParabolic,
                1.0e7d, 0.999d, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }, 100.0d, new[] { 1e-9d, 1e-12d }),

            CreateKepler("kepler-retrograde", "Kepler retrograde i=180 | 1 period", ScenarioClass.KeplerCircular,
                7.0e6d, 0.0d, 180.0d, 0.0d, 0.0d, 0.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }),

            CreateKepler("kepler-inclined", "Kepler inclined e=0.2 i=63.4 | 1 period", ScenarioClass.KeplerEccentric,
                8.0e6d, 0.2d, 63.4d, 30.0d, 45.0d, 10.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }),

            CreateThrust(),

            CreateHarmonicDrift(),

            CreateRestrictedNBody(),

            CreateRestrictedFlyby(),

            CreateKepler("kepler-circular-100p", "Kepler circular | 100 periods", ScenarioClass.KeplerCircularLong,
                7.0e6d, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d, 100.0d, ScenarioTier.Slow,
                new[] { 1e-10d, 1e-12d }),

            CreateKepler("kepler-e05-50p", "Kepler e=0.5 | 50 periods", ScenarioClass.KeplerEccentricLong,
                1.0e7d, 0.5d, 25.0d, 40.0d, 60.0d, 20.0d, 50.0d, ScenarioTier.Slow,
                new[] { 1e-10d, 1e-12d }),

            CreateKepler("kepler-e09-10p", "Kepler e=0.9 | 10 periods", ScenarioClass.KeplerHighEccentricLong,
                1.0e7d, 0.9d, 0.0d, 0.0d, 0.0d, 0.0d, 10.0d, ScenarioTier.Slow,
                new[] { 1e-10d, 1e-12d }),

            CreateKepler("kepler-hyperbolic-1d", "Kepler hyperbolic e=1.5 | 1 day", ScenarioClass.KeplerHyperbolicLong,
                -1.4e7d, 1.5d, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d, ScenarioTier.Slow,
                new[] { 1e-9d, 1e-12d }, 86400.0d)
        };

        private static readonly Dictionary<string, Scenario> ById = BuildIndex();

        public static Scenario Get(string id)
        {
            if (!ById.TryGetValue(id, out Scenario scenario))
            {
                throw new ArgumentException("Неизвестный сценарий: " + id + ".", nameof(id));
            }

            return scenario;
        }

        public static IEnumerable<Scenario> Fast()
        {
            foreach (Scenario scenario in All)
            {
                if (scenario.Tier == ScenarioTier.Fast)
                {
                    yield return scenario;
                }
            }
        }

        public static IEnumerable<Scenario> Slow()
        {
            foreach (Scenario scenario in All)
            {
                if (scenario.Tier == ScenarioTier.Slow)
                {
                    yield return scenario;
                }
            }
        }

        private static Dictionary<string, Scenario> BuildIndex()
        {
            var index = new Dictionary<string, Scenario>(StringComparer.Ordinal);
            index.Add(LinearDrift.Id, LinearDrift);
            foreach (Scenario scenario in All)
            {
                index.Add(scenario.Id, scenario);
            }

            return index;
        }

        private static Scenario CreateKepler(
            string id,
            string title,
            ScenarioClass scenarioClass,
            double semiMajorAxis,
            double eccentricity,
            double inclinationDegrees,
            double raanDegrees,
            double argumentOfPeriapsisDegrees,
            double trueAnomalyDegrees,
            double horizonPeriods,
            ScenarioTier tier,
            double[] tolerances,
            double explicitDurationSeconds = 0d,
            double[] dopri5Tolerances = null,
            double centralMu = 0d)
        {
            double mu = centralMu > 0d ? centralMu : Mu;
            InitialOrbit.StateFromElements(
                mu, semiMajorAxis, eccentricity, inclinationDegrees, raanDegrees,
                argumentOfPeriapsisDegrees, trueAnomalyDegrees,
                out Double3 position, out Double3 velocity);

            double duration = explicitDurationSeconds > 0d
                ? explicitDurationSeconds
                : horizonPeriods * InitialOrbit.Period(mu, semiMajorAxis);

            var scenario = new Scenario();
            scenario.Id = id;
            scenario.Title = title;
            scenario.Family = "Kepler";
            scenario.Class = scenarioClass;
            scenario.Tier = tier;
            scenario.Mu = mu;
            scenario.DurationSeconds = duration;
            scenario.SemiMajorAxisMeters = semiMajorAxis;
            scenario.Eccentricity = eccentricity;
            scenario.PeriapsisMeters = Math.Abs(semiMajorAxis * (1d - eccentricity));
            scenario.ApoapsisMeters = eccentricity < 1d
                ? semiMajorAxis * (1d + eccentricity)
                : double.PositiveInfinity;
            scenario.PeriapsisSpeedMetersPerSecond = Math.Sqrt(
                mu * (1d + eccentricity) / scenario.PeriapsisMeters);
            scenario.Dopri5Tolerances = dopri5Tolerances;
            scenario.InitialState = new SpacecraftIntegrationState
            {
                Position = position.ToVector3d(),
                Velocity = velocity.ToVector3d(),
                Mass = 1000.0d
            };
            Double3 r0 = position;
            Double3 v0 = velocity;
            scenario.Derivative = (state, time) => new SpacecraftIntegrationState
            {
                Position = state.Velocity,
                Velocity = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    state.Position, Vector3d.Zero, mu),
                Mass = 0d
            };
            scenario.Oracle = time => KeplerOracle.Propagate(r0, v0, mu, time);
            scenario.CheckEnergy = true;
            scenario.CheckAngularMomentum = true;
            scenario.CheckLrl = true;
            scenario.Tolerances = tolerances;
            return scenario;
        }

        private static Scenario CreateThrust()
        {
            const double mass0 = 1000.0d;
            const double thrust = 100.0d;
            const double massFlow = 1.0d;
            const double burn = 100.0d;

            var scenario = new Scenario();
            scenario.Id = "thrust-tsiolkovsky";
            scenario.Title = "Thrust constant 100 N, flow 1 kg/s | 100 s";
            scenario.Family = "Thrust";
            scenario.Class = ScenarioClass.Thrust;
            scenario.Tier = ScenarioTier.Fast;
            scenario.CheckPhysicalAccuracy = false;
            scenario.Mu = 0d;
            scenario.DurationSeconds = burn;
            scenario.InitialState = new SpacecraftIntegrationState
            {
                Position = Vector3d.Zero,
                Velocity = Vector3d.Zero,
                Mass = mass0
            };
            scenario.Derivative = (state, time) => new SpacecraftIntegrationState
            {
                Position = state.Velocity,
                Velocity = new Vector3d(thrust / state.Mass, 0d, 0d),
                Mass = -massFlow
            };
            scenario.Oracle = time =>
            {
                double mass = mass0 - (massFlow * time);
                double log = Math.Log(mass0 / mass);
                double speed = (thrust / massFlow) * log;
                double position = (thrust / massFlow) * (time - ((mass / massFlow) * log));
                return new OracleState(new Double3(position, 0d, 0d), new Double3(speed, 0d, 0d), mass);
            };
            scenario.CheckEnergy = false;
            scenario.CheckAngularMomentum = false;
            scenario.CheckLrl = false;
            scenario.Tolerances = new[] { 1e-7d, 1e-9d };
            return scenario;
        }

        private static Scenario CreateLinearDrift()
        {
            const double duration = 3600.0d;
            Double3 r0 = new Double3(1.0e7d, 0d, 0d);
            Double3 v0 = new Double3(100.0d, 0d, 0d);
            var scenario = new Scenario();
            scenario.Id = "drift-linear";
            scenario.Title = "Zero-g drift | 1 h";
            scenario.Family = "Drift";
            scenario.Class = ScenarioClass.Drift;
            scenario.Tier = ScenarioTier.Fast;
            scenario.CheckPhysicalAccuracy = false;
            scenario.Mu = 0d;
            scenario.DurationSeconds = duration;
            scenario.InitialState = new SpacecraftIntegrationState
            {
                Position = r0.ToVector3d(),
                Velocity = v0.ToVector3d(),
                Mass = 1000.0d
            };
            scenario.Derivative = (state, time) => new SpacecraftIntegrationState
            {
                Position = state.Velocity,
                Velocity = Vector3d.Zero,
                Mass = 0d
            };
            scenario.Oracle = time => new OracleState(r0 + (v0 * time), v0, 1000.0d);
            scenario.CheckEnergy = false;
            scenario.CheckAngularMomentum = false;
            scenario.CheckLrl = false;
            scenario.Tolerances = new[] { 1e-6d, 1e-9d, 1e-12d };
            return scenario;
        }

        private static Scenario CreateHarmonicDrift()
        {
            const double duration = 3600.0d;
            const double omega = 2.0d * Math.PI / duration;
            Double3 r0 = new Double3(1.0e7d, 0d, 0d);
            Double3 v0 = new Double3(0d, 1.0e5d, 0d);
            var scenario = new Scenario();
            scenario.Id = "drift-harmonic";
            scenario.Title = "Harmonic drift | 1 h";
            scenario.Family = "Drift";
            scenario.Class = ScenarioClass.Drift;
            scenario.Tier = ScenarioTier.Fast;
            scenario.CheckPhysicalAccuracy = false;
            scenario.Mu = 0d;
            scenario.DurationSeconds = duration;
            scenario.InitialState = new SpacecraftIntegrationState
            {
                Position = r0.ToVector3d(),
                Velocity = v0.ToVector3d(),
                Mass = 1000.0d
            };
            scenario.Derivative = (state, time) => new SpacecraftIntegrationState
            {
                Position = state.Velocity,
                Velocity = state.Position * (-omega * omega),
                Mass = 0d
            };
            scenario.Oracle = time =>
            {
                double cos = Math.Cos(omega * time);
                double sin = Math.Sin(omega * time);
                Double3 position = (r0 * cos) + (v0 * (sin / omega));
                Double3 velocity = (r0 * (-omega * sin)) + (v0 * cos);
                return new OracleState(position, velocity, 1000.0d);
            };
            scenario.CheckEnergy = false;
            scenario.CheckAngularMomentum = false;
            scenario.CheckLrl = false;
            scenario.Tolerances = new[] { 1e-6d, 1e-9d, 1e-12d };
            return scenario;
        }

        private static Scenario CreateRestrictedNBody()
        {
            // Earth-Moon restricted three-body problem: a LEO test particle in
            // the Earth field with the Moon as a moving perturber. The short
            // orbital period makes the adaptive controller act; a heliocentric
            // 1 h arc was capped at 600 s for every tolerance.
            const double centralMu = 3.986004418e14d;
            const double radius = 7.0e6d;
            const double bodyRadius = 3.844e8d;
            const double bodyMu = 4.9048695e12d;
            double speed = Math.Sqrt(centralMu / radius);
            var body = new RestrictedBody(
                bodyMu, bodyRadius, 0d,
                Math.Sqrt(centralMu / (bodyRadius * bodyRadius * bodyRadius)));
            var bodies = new[] { body };
            Double3 r0 = new Double3(radius, 0d, 0d);
            Double3 v0 = new Double3(0d, speed, 0d);
            const double duration = 3600.0d;

            var scenario = new Scenario();
            scenario.Id = "restricted-3body";
            scenario.Title = "Restricted 3-body Earth-Moon test particle | 1 h";
            scenario.Family = "NBody";
            scenario.Class = ScenarioClass.RestrictedNBody;
            scenario.Tier = ScenarioTier.Fast;
            scenario.Mu = centralMu;
            scenario.DurationSeconds = duration;
            scenario.PeriapsisMeters = radius;
            scenario.PeriapsisSpeedMetersPerSecond = speed;
            scenario.PhysicalStepPositionFraction = RestrictedStepPositionLimitMeters / radius;
            scenario.PhysicalDensePositionFraction = RestrictedDensePositionLimitMeters / radius;
            scenario.RestrictedBodies = bodies;
            scenario.RestrictedCentralMu = centralMu;
            scenario.InitialState = new SpacecraftIntegrationState
            {
                Position = r0.ToVector3d(),
                Velocity = v0.ToVector3d(),
                Mass = 1000.0d
            };
            scenario.Derivative = (state, time) =>
            {
                Vector3d acceleration = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    state.Position, Vector3d.Zero, centralMu);
                foreach (RestrictedBody body in bodies)
                {
                    body.PositionAt(time, out double bx, out double by);
                    acceleration += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                        state.Position, new Vector3d(bx, by, 0d), body.Mu);
                }

                return new SpacecraftIntegrationState
                {
                    Position = state.Velocity,
                    Velocity = acceleration,
                    Mass = 0d
                };
            };
            scenario.Oracle = time => RestrictedNBodyOracle.Propagate(r0, v0, centralMu, bodies, time);
            scenario.CheckEnergy = false;
            scenario.CheckAngularMomentum = false;
            scenario.CheckLrl = false;
            scenario.Tolerances = new[] { 1e-6d, 1e-9d };
            return scenario;
        }

        private static Scenario CreateRestrictedFlyby()
        {
            const double centralMu = 1.32712440018e20d;
            const double bodyR = 1.0e10d;
            const double bodyMu = 3.986004418e14d;
            const double separation = 3.0e7d;
            const double relativeSpeed = 3000d;
            const double duration = 15000d;

            // The perturbing body is fixed at (bodyR, 0, 0). The particle starts
            // 30,000 km away and falls inward: the closest approach is ~1.35e7 m,
            // so the body acceleration reaches the same order as the central one
            // and the step controller must act.
            var body = new RestrictedBody(bodyMu, bodyR, 0d, 0d);
            var bodies = new[] { body };
            Double3 r0 = new Double3(bodyR + separation, 0d, 0d);
            Double3 v0 = new Double3(0d, relativeSpeed, 0d);

            RestrictedNBodyTrajectory trajectory = RestrictedNBodyOracle.Build(
                r0, v0, centralMu, bodies, duration,
                stepSeconds: RestrictedFlybyOracleStepSeconds);

            // Periapsis here is the closest approach to the perturbing body.
            // Take both distance and speed from the oracle instead of assuming
            // the initial 3 km/s is the periapsis speed.
            double closestDistance = double.PositiveInfinity;
            double closestSpeed = 0d;
            const int closestSamples = 20000;
            for (int i = 0; i <= closestSamples; i++)
            {
                double time = duration * i / closestSamples;
                OracleState state = trajectory.Evaluate(time);
                double dx = state.Position.X - bodyR;
                double dy = state.Position.Y;
                double dz = state.Position.Z;
                double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestSpeed = state.Velocity.Magnitude;
                }
            }

            var scenario = new Scenario();
            scenario.Id = "restricted-flyby";
            scenario.Title = "Restricted flyby near a second body | 15000 s";
            scenario.Family = "NBody";
            scenario.Class = ScenarioClass.RestrictedFlyby;
            scenario.Tier = ScenarioTier.Fast;
            scenario.Mu = centralMu;
            scenario.DurationSeconds = duration;
            // In this restricted model the perturber is fixed, so this is the
            // inertial speed at closest approach (~11 km/s), not the initial
            // 3 km/s and not a speed relative to a moving body.
            scenario.PeriapsisMeters = closestDistance;
            scenario.PeriapsisSpeedMetersPerSecond = closestSpeed;
            scenario.PhysicalStepPositionFraction = RestrictedStepPositionLimitMeters / closestDistance;
            scenario.PhysicalDensePositionFraction = RestrictedDensePositionLimitMeters / closestDistance;
            scenario.RestrictedBodies = bodies;
            scenario.RestrictedCentralMu = centralMu;
            scenario.InitialState = new SpacecraftIntegrationState
            {
                Position = r0.ToVector3d(),
                Velocity = v0.ToVector3d(),
                Mass = 1000d
            };
            scenario.Derivative = (state, time) =>
            {
                Vector3d acceleration = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    state.Position, Vector3d.Zero, centralMu);
                body.PositionAt(time, out double bx, out double by);
                acceleration += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    state.Position, new Vector3d(bx, by, 0d), bodyMu);
                return new SpacecraftIntegrationState
                {
                    Position = state.Velocity,
                    Velocity = acceleration,
                    Mass = 0d
                };
            };
            scenario.Oracle = trajectory.Evaluate;
            scenario.CheckEnergy = false;
            scenario.CheckAngularMomentum = false;
            scenario.CheckLrl = false;
            scenario.Tolerances = new[] { 1e-6d, 1e-9d, 1e-12d };
            return scenario;
        }
    }
}


