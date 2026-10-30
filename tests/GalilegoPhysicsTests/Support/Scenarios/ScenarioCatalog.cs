using System;
using System.Collections.Generic;
using Galilego.Core;
using GalilegoPhysicsTests.Support.Oracles;

namespace GalilegoPhysicsTests.Support.Scenarios
{
    internal static class ScenarioCatalog
    {
        private const double Mu = 3.986004418e14d;

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
                new[] { 1e-6d, 1e-9d }),

            CreateKepler("kepler-e099", "Kepler e=0.99 | 1 period", ScenarioClass.KeplerHighEccentric,
                1.0e7d, 0.99d, 0.0d, 0.0d, 0.0d, 0.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }),

            CreateKepler("kepler-hyperbolic", "Kepler hyperbolic e=1.5 | 1 h", ScenarioClass.KeplerHyperbolic,
                -1.4e7d, 1.5d, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }, 3600.0d),

            CreateKepler("kepler-near-parabolic", "Kepler near-parabolic e=0.999 | 100 s", ScenarioClass.KeplerNearParabolic,
                1.0e7d, 0.999d, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }, 100.0d),

            CreateKepler("kepler-retrograde", "Kepler retrograde i=180 | 1 period", ScenarioClass.KeplerCircular,
                7.0e6d, 0.0d, 180.0d, 0.0d, 0.0d, 0.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }),

            CreateKepler("kepler-inclined", "Kepler inclined e=0.2 i=63.4 | 1 period", ScenarioClass.KeplerEccentric,
                8.0e6d, 0.2d, 63.4d, 30.0d, 45.0d, 10.0d, 1.0d, ScenarioTier.Fast,
                new[] { 1e-6d, 1e-9d }),

            CreateThrust(),

            CreateDrift(),

            CreateRestrictedNBody(),

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
                throw new ArgumentException("РќРµРёР·РІРµСЃС‚РЅС‹Р№ СЃС†РµРЅР°СЂРёР№: " + id + ".", nameof(id));
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

        private static Dictionary<string, Scenario> BuildIndex()
        {
            var index = new Dictionary<string, Scenario>(StringComparer.Ordinal);
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
            double explicitDurationSeconds = 0d)
        {
            InitialOrbit.StateFromElements(
                Mu, semiMajorAxis, eccentricity, inclinationDegrees, raanDegrees,
                argumentOfPeriapsisDegrees, trueAnomalyDegrees,
                out Double3 position, out Double3 velocity);

            double duration = explicitDurationSeconds > 0d
                ? explicitDurationSeconds
                : horizonPeriods * InitialOrbit.Period(Mu, semiMajorAxis);

            var scenario = new Scenario();
            scenario.Id = id;
            scenario.Title = title;
            scenario.Family = "Kepler";
            scenario.Class = scenarioClass;
            scenario.Tier = tier;
            scenario.Mu = Mu;
            scenario.DurationSeconds = duration;
            scenario.InitialState = new SpacecraftIntegrationState
            {
                Position = position.ToVector3d(),
                Velocity = velocity.ToVector3d(),
                Mass = 1000.0d
            };
            Double3 r0 = position;
            Double3 v0 = velocity;
            double mu = Mu;
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

        private static Scenario CreateDrift()
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
            scenario.Tolerances = new[] { 1e-9d };
            return scenario;
        }

        private static Scenario CreateRestrictedNBody()
        {
            const double centralMu = 1.32712440018e20d;
            double radius = 1.5e10d;
            double speed = Math.Sqrt(centralMu / radius);
            RestrictedBody[] bodies = RestrictedNBodyOracle.CreateBodies(centralMu);
            Double3 r0 = new Double3(radius, 0d, 0d);
            Double3 v0 = new Double3(0d, speed, 0d);
            const double duration = 3600.0d;

            var scenario = new Scenario();
            scenario.Id = "restricted-3body";
            scenario.Title = "Restricted 3-body test particle | 1 h";
            scenario.Family = "NBody";
            scenario.Class = ScenarioClass.RestrictedNBody;
            scenario.Tier = ScenarioTier.Fast;
            scenario.Mu = centralMu;
            scenario.DurationSeconds = duration;
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
    }
}


