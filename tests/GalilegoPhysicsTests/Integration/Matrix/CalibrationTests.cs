using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Artifacts;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    [Trait("Tier", "Fast")]
    public sealed class CalibrationTests
    {
        [Fact]
        public void CalibrateMatrix()
        {
            var rows = new List<string>();
            var snippet = new List<string>();
            var productionSnippet = new List<string>();
            rows.Add("| scenario | integrator | tol | steps | rhs | maxPos m | maxVel m/s | maxMass rel | E drift | L drift | LRL drift |");
            rows.Add("|---|---|---|---|---|---|---|---|---|---|---|");

            foreach (Scenario scenario in ScenarioCatalog.All)
            {
                foreach (IntegratorKind integrator in new[] { IntegratorKind.Dopri5, IntegratorKind.Dop853 })
                {
                    foreach (double tolerance in scenario.TolerancesFor(integrator))
                    {
                        ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);

                        // Calibration must work for a brand-new scenario whose gate
                        // row does not exist yet. This test is also run on every
                        // normal suite pass, where a missing or stale gate is a
                        // real failure.
                        if (Env.Calibrate)
                        {
                            if (ToleranceTable.TryFor(integrator, scenario.Id, tolerance, out ToleranceProfile existing))
                            {
                                Asserts.True(run.MaxPositionError <= existing.OraclePositionMeters,
                                    scenario.Id + " / " + scenario.IntegratorLabel(integrator) +
                                    " at tol=" + tolerance.ToString("R") + ": position " +
                                    run.MaxPositionError.ToString("E3") + " m exceeds gate " +
                                    existing.OraclePositionMeters.ToString("E3") + " m.");
                                Asserts.True(run.MaxVelocityError <= existing.OracleVelocityMetersPerSecond,
                                    scenario.Id + " / " + scenario.IntegratorLabel(integrator) +
                                    " at tol=" + tolerance.ToString("R") + ": velocity " +
                                    run.MaxVelocityError.ToString("E3") + " m/s exceeds gate " +
                                    existing.OracleVelocityMetersPerSecond.ToString("E3") + " m/s.");
                            }
                        }
                        else
                        {
                            ToleranceProfile gate = ToleranceTable.For(
                                integrator, scenario.Id, tolerance);
                            Asserts.True(run.MaxPositionError <= gate.OraclePositionMeters,
                                scenario.Id + " / " + scenario.IntegratorLabel(integrator) +
                                " at tol=" + tolerance.ToString("R") + ": position " +
                                run.MaxPositionError.ToString("E3") + " m exceeds gate " +
                                gate.OraclePositionMeters.ToString("E3") + " m.");
                            Asserts.True(run.MaxVelocityError <= gate.OracleVelocityMetersPerSecond,
                                scenario.Id + " / " + scenario.IntegratorLabel(integrator) +
                                " at tol=" + tolerance.ToString("R") + ": velocity " +
                                run.MaxVelocityError.ToString("E3") + " m/s exceeds gate " +
                                gate.OracleVelocityMetersPerSecond.ToString("E3") + " m/s.");
                        }
                        rows.Add("| " + scenario.Id +
                                 " | " + scenario.IntegratorLabel(integrator) +
                                 " | " + tolerance.ToString("0.0E+0", CultureInfo.InvariantCulture) +
                                 " | " + run.Steps +
                                 " | " + run.RhsCalls +
                                 " | " + run.MaxPositionError.ToString("0.000E+0", CultureInfo.InvariantCulture) +
                                 " | " + run.MaxVelocityError.ToString("0.000E+0", CultureInfo.InvariantCulture) +
                                 " | " + run.MaxMassError.ToString("0.000E+0", CultureInfo.InvariantCulture) +
                                 " | " + run.MaxEnergyDrift.ToString("0.000E+0", CultureInfo.InvariantCulture) +
                                 " | " + run.MaxAngularMomentumDrift.ToString("0.000E+0", CultureInfo.InvariantCulture) +
                                 " | " + run.MaxLrlDrift.ToString("0.000E+0", CultureInfo.InvariantCulture) + " |");
                        snippet.Add(StepBudget.FormatEntry(
                            scenario.Id, scenario.IntegratorLabel(integrator), tolerance,
                            run.Steps, run.RhsCalls));
                        if (integrator == IntegratorKind.Dop853)
                        {
                            long productionCalls = run.MeasureProductionRhsCalls();
                            productionSnippet.Add(StepBudget.FormatProductionEntry(
                                scenario.Id, scenario.IntegratorLabel(integrator), tolerance,
                                productionCalls));
                        }
                    }
                }
            }

            rows.Add("");
            rows.Add("### StepBudget entries");
            rows.Add("");
            rows.AddRange(snippet);

            // The verification above always runs. Artifact regeneration stays
            // opt-in so a normal test run does not rewrite the working tree.
            if (Env.Calibrate)
            {
                string path = Path.Combine(ArtifactWriter.RepositoryRoot, "TestResults", "calibration.md");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Join(Environment.NewLine, rows) + Environment.NewLine, Encoding.UTF8);

                string snippetPath = Path.Combine(ArtifactWriter.RepositoryRoot, "TestResults", "step-budget-snippet.cs");
                File.WriteAllText(snippetPath, string.Join(Environment.NewLine, snippet) + Environment.NewLine, Encoding.UTF8);
                string productionPath = Path.Combine(ArtifactWriter.RepositoryRoot, "TestResults", "production-step-budget-snippet.cs");
                File.WriteAllText(productionPath, string.Join(Environment.NewLine, productionSnippet) + Environment.NewLine, Encoding.UTF8);
                Console.WriteLine("calibration written: " + path);
                Console.WriteLine("StepBudget snippet: " + snippetPath);
                Console.WriteLine("Production StepBudget snippet: " + productionPath);
            }
        }
    }
}
