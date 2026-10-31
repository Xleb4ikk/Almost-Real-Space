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

    /// <summary>
    /// Physical acceptance fractions. These are requirements, not calibration
    /// values: the numerical trajectory has to stay within this fraction of the
    /// orbit's own scale, independently of what the current implementation
    /// happens to measure.
    /// </summary>
    internal static class PhysicalAccuracy
    {
        public const double StepPositionFraction = 0.002d;
        public const double DensePositionFraction = 0.004d;
        public const double StepVelocityFraction = 0.002d;
        public const double DenseVelocityFraction = 0.004d;
        public const double OrbitElementFraction = 0.01d;
        public const double EccentricityAbsolute = 0.01d;
        public const double PeriapsisFloorFraction = 0.98d;
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
        public double[] Dopri5Tolerances;
        public double Mu;

        // Physical scale used by the absolute-accuracy tests. Zero means the
        // scenario has no meaningful orbital periapsis (drift, thrust).
        public double SemiMajorAxisMeters;
        public double Eccentricity;
        public double PeriapsisMeters;
        public double ApoapsisMeters;
        public double PeriapsisSpeedMetersPerSecond;

        // Per-scenario physical acceptance. Defaults are the Kepler-orbit
        // fractions; restricted N-body scenarios tighten them to a sub-kilometre
        // absolute requirement instead of letting a 1% ceiling dominate.
        public double PhysicalStepPositionFraction = PhysicalAccuracy.StepPositionFraction;
        public double PhysicalDensePositionFraction = PhysicalAccuracy.DensePositionFraction;

        // Explicit opt-out only: drift/thrust have no periapsis/orbit. A new
        // orbital scenario with no periapsis must not silently skip the physical
        // gate; it fails loudly in MatchesPhysicalAccuracy.
        public bool CheckPhysicalAccuracy = true;

        // Oracle configuration for the restricted N-body scenarios, so the
        // oracle's own convergence can be checked independently.
        public RestrictedBody[] RestrictedBodies;
        public double RestrictedCentralMu;

        public bool IsKepler => SemiMajorAxisMeters != 0d;

        public double StepPositionLimitMeters => PhysicalStepPositionFraction * PeriapsisMeters;
        public double DensePositionLimitMeters => PhysicalDensePositionFraction * PeriapsisMeters;
        public double StepVelocityLimitMetersPerSecond => PhysicalAccuracy.StepVelocityFraction * PeriapsisSpeedMetersPerSecond;
        public double DenseVelocityLimitMetersPerSecond => PhysicalAccuracy.DenseVelocityFraction * PeriapsisSpeedMetersPerSecond;

        public double[] TolerancesFor(IntegratorKind kind)
        {
            return kind == IntegratorKind.Dopri5 && Dopri5Tolerances != null
                ? Dopri5Tolerances
                : Tolerances;
        }

        public void SetKeplerElementsFromState(Double3 r0, Double3 v0, double mu)
        {
            double radius = r0.Magnitude;
            double v2 = v0.SqrMagnitude;
            double energy = (0.5d * v2) - (mu / radius);
            double semiMajorAxis = -mu / (2d * energy);
            Double3 eVec = ((r0 * (v2 - (mu / radius))) - (v0 * Double3.Dot(r0, v0))) / mu;
            double eccentricity = eVec.Magnitude;

            SemiMajorAxisMeters = semiMajorAxis;
            Eccentricity = eccentricity;
            PeriapsisMeters = Math.Abs(semiMajorAxis * (1d - eccentricity));
            ApoapsisMeters = eccentricity < 1d
                ? semiMajorAxis * (1d + eccentricity)
                : double.PositiveInfinity;
            PeriapsisSpeedMetersPerSecond = Math.Sqrt(
                mu * (1d + eccentricity) / PeriapsisMeters);
        }

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

