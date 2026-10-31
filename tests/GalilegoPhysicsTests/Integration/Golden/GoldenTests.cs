using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Golden;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Golden
{
    public sealed class GoldenTests
    {
        private readonly ITestOutputHelper output;

        public GoldenTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        [Trait("Tier", "Fast")]
        public void FastCoreGoldenCasesMatch()
        {
            GoldenDocument document = GoldenDocument.Load("golden_core_kepler.json");
            Asserts.True(document.Cases.Count >= 5, "fast Core golden must contain at least five cases");
            foreach (GoldenCase golden in document.Cases)
            {
                Scenario scenario = GoldenScenarios.Create(golden);
                ToleranceProfile table = ToleranceTable.For(IntegratorKind.Dop853, scenario.Class, 1e-12d);
                ScenarioExecution run = ScenarioExecution.Execute(scenario, IntegratorKind.Dop853, 1e-12d, 1);

                double positionError = (run.FinalState.Position - golden.P).Magnitude;
                double velocityError = (run.FinalState.Velocity - golden.V).Magnitude;
                output.WriteLine(golden.Id + ": |ΔP|=" + positionError.ToString("E3") +
                                 " m, |ΔV|=" + velocityError.ToString("E3") +
                                 " m/s, steps=" + run.Steps + ".");
                Asserts.True(positionError <= table.OraclePositionMeters,
                    golden.Id + ": position error " + positionError.ToString("E3") +
                    " m > " + table.OraclePositionMeters.ToString("E3") + " m.");
                Asserts.True(velocityError <= table.OracleVelocityMetersPerSecond,
                    golden.Id + ": velocity error " + velocityError.ToString("E3") +
                    " m/s > " + table.OracleVelocityMetersPerSecond.ToString("E3") + " m/s.");
            }
        }

        [SlowFact]
        [Trait("Tier", "Slow")]
        public void SlowCoreGoldenCasesMatch()
        {
            GoldenDocument document = GoldenDocument.Load("golden_core_kepler_10y.json");
            Asserts.True(document.Cases.Count >= 2, "slow Core golden must contain at least two cases");
            foreach (GoldenCase golden in document.Cases)
            {
                Scenario scenario = GoldenScenarios.Create(golden);
                ToleranceProfile table = ToleranceTable.For(IntegratorKind.Dop853, scenario.Class, 1e-12d);
                ScenarioExecution run = ScenarioExecution.Execute(scenario, IntegratorKind.Dop853, 1e-12d, 1);

                double positionError = (run.FinalState.Position - golden.P).Magnitude;
                double velocityError = (run.FinalState.Velocity - golden.V).Magnitude;
                output.WriteLine(golden.Id + ": |ΔP|=" + positionError.ToString("E3") +
                                 " m, |ΔV|=" + velocityError.ToString("E3") +
                                 " m/s, steps=" + run.Steps + ".");
                Asserts.True(positionError <= table.OraclePositionMeters,
                    golden.Id + ": position error " + positionError.ToString("E3") +
                    " m > " + table.OraclePositionMeters.ToString("E3") + " m.");
                Asserts.True(velocityError <= table.OracleVelocityMetersPerSecond,
                    golden.Id + ": velocity error " + velocityError.ToString("E3") +
                    " m/s > " + table.OracleVelocityMetersPerSecond.ToString("E3") + " m/s.");
            }
        }

        [Fact]
        [Trait("Tier", "Fast")]
        public void LinkedStandGoldenFilesAreReadableAndInternallyConsistent()
        {
            foreach (string name in new[] { "golden_real.json", "golden_real_1y_hi.json", "golden_real_10y_k28.json" })
            {
                string path = Path.Combine(GoldenDocument.DirectoryPath, name);
                AssertFileExists(path);
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement checkpoints = document.RootElement.GetProperty("checkpoints");
                int count = 0;
                foreach (JsonElement checkpoint in checkpoints.EnumerateArray())
                {
                    count++;
                    double[] absolute = ReadNumbers(checkpoint.GetProperty("P"));
                    double[] central = ReadNumbers(checkpoint.GetProperty("central_P"));
                    double[] relative = ReadNumbers(checkpoint.GetProperty("rel_P"));
                    for (int i = 0; i < 3; i++)
                    {
                        double sum = central[i] + relative[i];
                        double scale = Math.Max(Math.Abs(absolute[i]), 1d);
                        Asserts.True(Math.Abs(absolute[i] - sum) / scale < 1e-10,
                            name + ": checkpoint P != central_P + rel_P at component " + i +
                            " (absolute=" + absolute[i].ToString("R") +
                            ", sum=" + sum.ToString("R") + ").");
                    }
                }

                output.WriteLine(name + ": " + count + " checkpoints, linked and internally consistent.");
                Asserts.True(count > 0, name + " has no checkpoints.");
            }
        }

        [Fact]
        [Trait("Tier", "Fast")]
        [Trait("Integrity", "LinkedStandGolden")]
        public void LinkedGoldenHashesMatchTheStandReadme()
        {
            // Integrity pin for the linked stand data, not a Core physics gate.
            // A deliberate golden regeneration in P1bTests updates these hashes.
            AssertHash("golden_real.json", "2A5206636425B04AE7B21EC9765F1FA4EF3723D98472DF347ED77BA3355402E8");
            AssertHash("golden_real_1y_hi.json", "7582663FBAF275C2076B9D411DFB91B9833FC8683AD422FF3793C39BD6ACBC4E");
        }

        private void AssertHash(string fileName, string expected)
        {
            string path = Path.Combine(GoldenDocument.DirectoryPath, fileName);
            byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
            string actual = Convert.ToHexString(hash);
            Asserts.True(actual == expected, fileName + ": SHA-256 is " + actual + ", expected " + expected + ".");
            output.WriteLine(fileName + ": SHA-256 " + actual + ".");
        }

        private static void AssertFileExists(string path)
        {
            Asserts.True(File.Exists(path), "linked golden file is missing: " + path);
        }

        private static double[] ReadNumbers(JsonElement element)
        {
            var values = new List<double>();
            foreach (JsonElement component in element.EnumerateArray())
            {
                string text = component.ValueKind == JsonValueKind.String
                    ? component.GetString()
                    : component.GetRawText();
                values.Add(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
            }

            return values.ToArray();
        }
    }
}
