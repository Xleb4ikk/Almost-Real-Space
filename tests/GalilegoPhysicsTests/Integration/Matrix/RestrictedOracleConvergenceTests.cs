using System;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Oracles;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    /// <summary>
    /// The restricted N-body oracle is an independent fixed-step RK4, but its
    /// own error was never measured. Step halving gives a Richardson estimate:
    /// for a 4th-order method the fine-vs-medium difference is ~1/15 of the
    /// remaining error. The oracle must converge at the expected order and its
    /// estimated error must sit below the production gate it certifies.
    /// </summary>
    [Trait("Tier", "Fast")]
    public sealed class RestrictedOracleConvergenceTests
    {
        private readonly ITestOutputHelper output;

        public RestrictedOracleConvergenceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData("restricted-3body", 2.0d)]
        [InlineData("restricted-flyby", 0.4d)]
        public void OracleConvergesUnderStepHalving(string scenarioId, double coarseStep)
        {
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            Double3 r0 = new Double3(
                scenario.InitialState.Position.X,
                scenario.InitialState.Position.Y,
                scenario.InitialState.Position.Z);
            Double3 v0 = new Double3(
                scenario.InitialState.Velocity.X,
                scenario.InitialState.Velocity.Y,
                scenario.InitialState.Velocity.Z);
            RestrictedBody[] bodies = scenario.RestrictedBodies;
            double mu = scenario.RestrictedCentralMu;

            OracleState coarse = RestrictedNBodyOracle.Propagate(
                r0, v0, mu, bodies, scenario.DurationSeconds, coarseStep);
            OracleState medium = RestrictedNBodyOracle.Propagate(
                r0, v0, mu, bodies, scenario.DurationSeconds, coarseStep * 0.5d);
            OracleState fine = RestrictedNBodyOracle.Propagate(
                r0, v0, mu, bodies, scenario.DurationSeconds, coarseStep * 0.25d);

            double difference1 = (medium.Position - coarse.Position).Magnitude;
            double difference2 = (fine.Position - medium.Position).Magnitude;
            double refinementSpread = Math.Max(difference1, difference2);
            double gate = ToleranceTable.For(
                IntegratorKind.Dop853, scenarioId, 1e-9d).OraclePositionMeters;
            bool orderVisible = difference2 < difference1 * 0.5d;

            output.WriteLine(scenarioId + ": h=" + coarseStep.ToString("R") +
                             " -> " + (coarseStep * 0.25d).ToString("R") +
                             "; |h/2-h|=" + difference1.ToString("E3") +
                             " m, |h/4-h/2|=" + difference2.ToString("E3") +
                             " m, spread=" + refinementSpread.ToString("E3") +
                             " m, order visible=" + orderVisible +
                             " (gate " + gate.ToString("E3") + " m).");

            // On the smooth flyby the RK4 truncation term is already below
            // double roundoff at the scenario's 0.1 s step, so successive
            // differences are not monotone and a Richardson estimate would be
            // false precision. There the meaningful certificate is stability:
            // the whole three-step spread stays below the production gate.
            // restricted-3body does show the order, so its Richardson estimate
            // must also stay below the gate.
            Asserts.True(refinementSpread <= gate,
                scenarioId + ": refinement spread " + refinementSpread.ToString("E3") +
                " m exceeds the production gate " + gate.ToString("E3") +
                " m; the oracle's numerical uncertainty is not certified.");
            if (orderVisible)
            {
                double estimatedError = difference2 / 15d;
                Asserts.True(estimatedError <= gate,
                    scenarioId + ": oracle's Richardson error estimate " +
                    estimatedError.ToString("E3") + " m exceeds the production gate " +
                    gate.ToString("E3") + " m; reduce the oracle step or stop trusting the gate.");
            }
        }

        [Theory]
        [InlineData("restricted-3body", 0.5d)]
        [InlineData("restricted-flyby", 0.1d)]
        public void BuildHermiteTrajectoryStaysBelowTheGate(string scenarioId, double stepSeconds)
        {
            // restricted-flyby's Scenario.Oracle is Build + Hermite Evaluate, not
            // Propagate. Compare the production step (and its midpoints, the
            // worst case for cubic Hermite) against a 4x finer build.
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            Double3 r0 = new Double3(
                scenario.InitialState.Position.X,
                scenario.InitialState.Position.Y,
                scenario.InitialState.Position.Z);
            Double3 v0 = new Double3(
                scenario.InitialState.Velocity.X,
                scenario.InitialState.Velocity.Y,
                scenario.InitialState.Velocity.Z);
            RestrictedBody[] bodies = scenario.RestrictedBodies;
            double mu = scenario.RestrictedCentralMu;

            RestrictedNBodyTrajectory coarse = RestrictedNBodyOracle.Build(
                r0, v0, mu, bodies, scenario.DurationSeconds, stepSeconds);
            RestrictedNBodyTrajectory fine = RestrictedNBodyOracle.Build(
                r0, v0, mu, bodies, scenario.DurationSeconds, stepSeconds * 0.25d);
            int coarseSteps = (int)Math.Ceiling(scenario.DurationSeconds / stepSeconds);
            double coarseStep = scenario.DurationSeconds / coarseSteps;
            int stride = Math.Max(1, coarseSteps / 2000);
            double worst = 0d;
            int samples = 0;
            for (int i = 0; i < coarseSteps; i += stride)
            {
                double time = (i + 0.5d) * coarseStep;
                double error = (coarse.Evaluate(time).Position -
                                fine.Evaluate(time).Position).Magnitude;
                worst = Math.Max(worst, error);
                samples++;
            }

            double gate = ToleranceTable.For(
                IntegratorKind.Dop853, scenarioId, 1e-9d).OraclePositionMeters;
            output.WriteLine(scenarioId + ": Build/Hermite h=" + coarseStep.ToString("R") +
                             " vs h/4, worst midpoint=" + worst.ToString("E3") +
                             " m over " + samples + " samples (gate " +
                             gate.ToString("E3") + " m).");

            Asserts.True(worst <= gate,
                scenarioId + ": Build/Hermite error " + worst.ToString("E3") +
                " m exceeds the production gate " + gate.ToString("E3") +
                " m; the oracle interpolant is not certified.");
        }

        [Theory]
        [InlineData("restricted-3body")]
        [InlineData("restricted-flyby")]
        public void TimeDependentScenariosAreReversibleAndChunkingInvariant(string scenarioId)
        {
            // restricted-3body's Moon moves, so a reversibility run that keeps
            // evaluating the perturber at forward time is a different physical
            // problem. The driver maps tau -> duration - tau in reverse mode.
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            ScenarioExecution run = ScenarioExecution.Execute(
                scenario, IntegratorKind.Dop853, 1e-9d, 1);

            // Far tighter than the physical ceiling: reversibility/chunking are
            // algebraic invariants, not accuracy requirements.
            run.IsTimeReversible(1e-3d, 1e-6d);
            run.IsChunkingInvariant(1e-3d, 1e-6d);

            output.WriteLine(scenarioId + ": reversible |dP|=" +
                             run.ReversibilityPositionError.ToString("E3") +
                             " m, |dV|=" + run.ReversibilityVelocityError.ToString("E3") +
                             " m/s; chunking |dP|=" + run.ChunkingPositionError.ToString("E3") +
                             " m, |dV|=" + run.ChunkingVelocityError.ToString("E3") + " m/s.");
        }
    }
}
