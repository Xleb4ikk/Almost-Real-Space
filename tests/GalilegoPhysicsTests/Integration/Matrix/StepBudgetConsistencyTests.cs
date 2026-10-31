using System;
using System.Collections.Generic;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    [Trait("Tier", "Fast")]
    public sealed class StepBudgetConsistencyTests
    {
        [Fact]
        public void EveryMatrixRowHasExactlyOneBudgetEntry()
        {
            var expected = new HashSet<(string Scenario, string Integrator, double Tolerance)>();
            foreach (Scenario scenario in ScenarioCatalog.All)
            {
                foreach (IntegratorKind integrator in new[] { IntegratorKind.Dopri5, IntegratorKind.Dop853 })
                {
                    foreach (double tolerance in scenario.Tolerances)
                    {
                        expected.Add((scenario.Id, scenario.IntegratorLabel(integrator), tolerance));
                    }
                }
            }

            var actual = new HashSet<(string Scenario, string Integrator, double Tolerance)>(StepBudget.Keys);
            var missing = new List<string>();
            foreach (var key in expected)
            {
                if (!actual.Contains(key))
                {
                    missing.Add(key.Scenario + " | " + key.Integrator + " | " + key.Tolerance.ToString("R"));
                }
            }

            var stale = new List<string>();
            foreach (var key in actual)
            {
                if (!expected.Contains(key))
                {
                    stale.Add(key.Scenario + " | " + key.Integrator + " | " + key.Tolerance.ToString("R"));
                }
            }

            var message = "StepBudget does not match the matrix.";
            if (missing.Count > 0)
            {
                message += Environment.NewLine + "Missing (" + missing.Count + "): " + string.Join("; ", missing);
            }

            if (stale.Count > 0)
            {
                message += Environment.NewLine + "Stale (" + stale.Count + "): " + string.Join("; ", stale);
            }

            Asserts.True(missing.Count == 0 && stale.Count == 0, message);
        }

        [Fact]
        public void EveryDop853RowHasExactlyOneProductionBudgetEntry()
        {
            var expected = new HashSet<(string Scenario, string Integrator, double Tolerance)>();
            foreach (Scenario scenario in ScenarioCatalog.All)
            {
                foreach (double tolerance in scenario.Tolerances)
                {
                    expected.Add((scenario.Id, "Dop853", tolerance));
                }
            }

            var actual = new HashSet<(string Scenario, string Integrator, double Tolerance)>(StepBudget.ProductionKeys);
            var missing = new List<string>();
            foreach (var key in expected)
            {
                if (!actual.Contains(key))
                {
                    missing.Add(key.Scenario + " | " + key.Integrator + " | " + key.Tolerance.ToString("R"));
                }
            }

            var stale = new List<string>();
            foreach (var key in actual)
            {
                if (!expected.Contains(key))
                {
                    stale.Add(key.Scenario + " | " + key.Integrator + " | " + key.Tolerance.ToString("R"));
                }
            }

            var message = "Production StepBudget does not match the DOP853 matrix rows.";
            if (missing.Count > 0)
            {
                message += Environment.NewLine + "Missing (" + missing.Count + "): " + string.Join("; ", missing);
            }

            if (stale.Count > 0)
            {
                message += Environment.NewLine + "Stale (" + stale.Count + "): " + string.Join("; ", stale);
            }

            Asserts.True(missing.Count == 0 && stale.Count == 0, message);
        }
    }
}
