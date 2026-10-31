using System;
using Galilego.Core;
using GalilegoPhysicsTests.Support.Oracles;

namespace GalilegoPhysicsTests.Support.Scenarios
{
    internal enum IntegratorKind
    {
        Dopri5,
        Dop853
    }

    internal enum ScenarioClass
    {
        KeplerCircular,
        KeplerEccentric,
        KeplerHighEccentric,
        KeplerHyperbolic,
        KeplerNearParabolic,
        RestrictedNBody,
        Thrust,
        Drift,
        KeplerCircularLong,
        KeplerEccentricLong,
        KeplerHighEccentricLong,
        KeplerHyperbolicLong,
        GoldenEccentric1y,
        RestrictedFlyby
    }

    internal enum ScenarioTier
    {
        Fast,
        Slow
    }

    internal sealed class Scenario
    {
        public string Id;
        public string Title;
        public string Family;
        public ScenarioClass Class;
        public ScenarioTier Tier;
        public SpacecraftIntegrationState InitialState;
        public double DurationSeconds;
        public Func<SpacecraftIntegrationState, double, SpacecraftIntegrationState> Derivative;
        public Func<double, OracleState> Oracle;
        public bool CheckEnergy;
        public bool CheckAngularMomentum;
        public bool CheckLrl;
        public double[] Tolerances;
        public double Mu;

        public string IntegratorLabel(IntegratorKind kind) => kind == IntegratorKind.Dopri5 ? "Dopri5" : "Dop853";

        public string DisplayName(IntegratorKind kind, double tolerance)
        {
            return IntegratorLabel(kind) + " | " + Title + " | tol " + tolerance.ToString("0.0E+0");
        }

        public string FilterCommand(IntegratorKind kind)
        {
            return "dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter " +
                   "\"Integrator=" + IntegratorLabel(kind) + "&Scenario~" + Family + "\"";
        }

        public string Snippet(IntegratorKind kind, double tolerance)
        {
            return "Run.Scenario(\"" + Id + "\").With(Integrator." + IntegratorLabel(kind) +
                   ", " + tolerance.ToString("R") + ").Execute()";
        }
    }
}

