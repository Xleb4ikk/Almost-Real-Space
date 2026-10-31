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
            if (!Env.Calibrate)
            {
                return;
            }

            var rows = new List<string>();
            var snippet = new List<string>();
            rows.Add("| scenario | integrator | tol | steps | rhs | maxPos m | maxVel m/s | maxMass rel | E drift | L drift | LRL drift |");
            rows.Add("|---|---|---|---|---|---|---|---|---|---|---|");

            foreach (Scenario scenario in ScenarioCatalog.All)
            {
                foreach (IntegratorKind integrator in new[] { IntegratorKind.Dopri5, IntegratorKind.Dop853 })
                {
                    foreach (double tolerance in scenario.Tolerances)
                    {
                        ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);
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
                    }
                }
            }

            rows.Add("");
            rows.Add("### StepBudget entries");
            rows.Add("");
            rows.AddRange(snippet);

            string path = Path.Combine(ArtifactWriter.RepositoryRoot, "TestResults", "calibration.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, string.Join(Environment.NewLine, rows) + Environment.NewLine, Encoding.UTF8);

            string snippetPath = Path.Combine(ArtifactWriter.RepositoryRoot, "TestResults", "step-budget-snippet.cs");
            File.WriteAllText(snippetPath, string.Join(Environment.NewLine, snippet) + Environment.NewLine, Encoding.UTF8);
            Console.WriteLine("calibration written: " + path);
            Console.WriteLine("StepBudget snippet: " + snippetPath);
        }
    }
}
