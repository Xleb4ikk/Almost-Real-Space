using System.Collections.Generic;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    /// <summary>
    /// Cross-integrator claims, not per-row regression gates. At the same
    /// tolerance DOP853 must be dramatically more accurate than DOPRI5 without
    /// becoming an order of magnitude more expensive. This is the statement the
    /// per-integrator gates cannot make: a suite where both integrators regress
    /// together can stay green row by row.
    /// </summary>
    public sealed class IntegratorComparisonTests
    {
        private const double ComparisonTolerance = 1e-9d;
        private const double MinimumAccuracyAdvantage = 10d;
        private const double MaximumCostRatio = 2.5d;

        public static IEnumerable<object[]> FastSharedRows => SharedRows(ScenarioCatalog.Fast());

        public static IEnumerable<object[]> SlowSharedRows => SharedRows(ScenarioCatalog.Slow());

        private static IEnumerable<object[]> SharedRows(IEnumerable<Scenario> scenarios)
        {
            foreach (Scenario scenario in scenarios)
            {
                if (System.Array.IndexOf(
                        scenario.TolerancesFor(IntegratorKind.Dop853), ComparisonTolerance) >= 0 &&
                    System.Array.IndexOf(
                        scenario.TolerancesFor(IntegratorKind.Dopri5), ComparisonTolerance) >= 0)
                {
                    yield return new object[] { scenario.Id };
                }
            }
        }

        [Theory]
        [MemberData(nameof(FastSharedRows))]
        [Trait("Tier", "Fast")]
        public void Dop853BeatsDopri5OnAccuracyAtComparableCost(string scenarioId)
        {
            AssertComparison(scenarioId);
        }

        [Theory]
        [MemberData(nameof(SlowSharedRows))]
        [Trait("Tier", "Slow")]
        public void Dop853BeatsDopri5OnAccuracyAtComparableCostSlow(string scenarioId)
        {
            AssertComparison(scenarioId);
        }

        private static void AssertComparison(string scenarioId)
        {
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            ScenarioExecution dop853 = ScenarioExecution.Execute(
                scenario, IntegratorKind.Dop853, ComparisonTolerance, 1);
            ScenarioExecution dopri5 = ScenarioExecution.Execute(
                scenario, IntegratorKind.Dopri5, ComparisonTolerance, 1);
            string row = scenarioId + " at tol=" + ComparisonTolerance.ToString("R");

            Asserts.True(dop853.MaxPositionError > 0d && dopri5.MaxPositionError > 0d,
                row + ": a zero position error cannot rank the integrators (DOP853=" +
                dop853.MaxPositionError.ToString("E3") + " m, DOPRI5=" +
                dopri5.MaxPositionError.ToString("E3") + " m).");

            // Both errors can be at the double-roundoff floor (drift-harmonic,
            // thrust). A 10x ratio between two roundoff values is not an accuracy
            // advantage, so the claim is skipped instead of being decided by noise.
            double roundoffFloor = 1e-12d *
                System.Math.Max(1d, scenario.InitialState.Position.Magnitude);
            if (dop853.MaxPositionError <= roundoffFloor && dopri5.MaxPositionError <= roundoffFloor)
            {
                return;
            }

            double advantage = dopri5.MaxPositionError / dop853.MaxPositionError;
            Asserts.True(advantage >= MinimumAccuracyAdvantage,
                row + ": DOP853 error " + dop853.MaxPositionError.ToString("E3") +
                " m is not at least " + MinimumAccuracyAdvantage.ToString("F0") +
                "x better than DOPRI5 " + dopri5.MaxPositionError.ToString("E3") +
                " m (ratio " + advantage.ToString("E3") + ").");

            double costRatio = (double)dop853.RhsCalls / System.Math.Max(1L, dopri5.RhsCalls);
            Asserts.True(costRatio <= MaximumCostRatio,
                row + ": DOP853 uses " + dop853.RhsCalls +
                " RHS calls vs DOPRI5 " + dopri5.RhsCalls +
                " (" + costRatio.ToString("F3") + "x, limit " +
                MaximumCostRatio.ToString("F1") + "x). The accuracy advantage must be affordable.");
        }
    }
}
