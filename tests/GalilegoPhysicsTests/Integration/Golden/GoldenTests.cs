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
        // Absolute gates from the mpmath golden. They are deliberately not
        // derived from ToleranceTable: a golden is an external reference, not a
        // per-integration tolerance row.
        private static readonly Dictionary<(string Id, IntegratorKind Integrator), (double Position, double Velocity)> GoldenGates =
            new Dictionary<(string, IntegratorKind), (double Position, double Velocity)>
            {
                [("golden-kepler-circular-1p", IntegratorKind.Dop853)] = (1e-6d, 1e-9d),
                [("golden-kepler-e05-1p", IntegratorKind.Dop853)] = (1e-6d, 1e-9d),
                [("golden-kepler-e09-1p", IntegratorKind.Dop853)] = (5e-5d, 5e-7d),
                [("golden-kepler-hyperbolic-1h", IntegratorKind.Dop853)] = (5e-8d, 5e-11d),
                [("golden-kepler-inclined-1p", IntegratorKind.Dop853)] = (2e-6d, 2e-9d),
                [("golden-kepler-e05-1y", IntegratorKind.Dop853)] = (1.0d, 2e-3d),

                [("golden-kepler-circular-1p", IntegratorKind.Dopri5)] = (7.0e-4d, 7.4e-7d),
                [("golden-kepler-e05-1p", IntegratorKind.Dopri5)] = (2.15e-3d, 3.0e-6d),
                [("golden-kepler-e09-1p", IntegratorKind.Dopri5)] = (2.92e-2d, 4.23e-4d),
                [("golden-kepler-hyperbolic-1h", IntegratorKind.Dopri5)] = (8.4e-5d, 2.7e-8d),
                [("golden-kepler-inclined-1p", IntegratorKind.Dopri5)] = (1.1e-3d, 1.2e-6d),
                [("golden-kepler-e05-1y", IntegratorKind.Dopri5)] = (5.0e3d, 5.0d)
            };

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
            AssertCoreGoldenDocument(document);
            Asserts.True(document.Cases.Count >= 5, "fast Core golden must contain at least five cases");
            foreach (GoldenCase golden in document.Cases)
            {
                Scenario scenario = GoldenScenarios.Create(golden);
                foreach (IntegratorKind integrator in new[] { IntegratorKind.Dop853, IntegratorKind.Dopri5 })
                {
                    (double positionGate, double velocityGate) = GoldenGateFor(golden.Id, integrator);
                    ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-12d, 1);

                    double positionError = (run.FinalState.Position - golden.P).Magnitude;
                    double velocityError = (run.FinalState.Velocity - golden.V).Magnitude;
                    string row = golden.Id + " / " + run.Integrator;
                    output.WriteLine(row + ": |ΔP|=" + positionError.ToString("E3") +
                                     " m, |ΔV|=" + velocityError.ToString("E3") +
                                     " m/s, steps=" + run.Steps + ".");
                    Asserts.True(positionError <= positionGate,
                        row + ": position error " + positionError.ToString("E3") +
                        " m > " + positionGate.ToString("E3") + " m.");
                    Asserts.True(velocityError <= velocityGate,
                        row + ": velocity error " + velocityError.ToString("E3") +
                        " m/s > " + velocityGate.ToString("E3") + " m/s.");
                    AssertGateTight(row, "position", positionError, positionGate, 1e-9d);
                    AssertGateTight(row, "velocity", velocityError, velocityGate, 1e-12d);
                }
            }
        }

        [Fact]
        [Trait("Tier", "Slow")]
        public void SlowCoreGoldenCaseMatches()
        {
            GoldenDocument document = GoldenDocument.Load("golden_core_kepler_1y.json");
            AssertCoreGoldenDocument(document);
            Asserts.True(document.Cases.Count >= 1, "slow Core golden must contain at least one case");
            foreach (GoldenCase golden in document.Cases)
            {
                Scenario scenario = GoldenScenarios.Create(golden);
                foreach (IntegratorKind integrator in new[] { IntegratorKind.Dop853, IntegratorKind.Dopri5 })
                {
                    (double positionGate, double velocityGate) = GoldenGateFor(golden.Id, integrator);
                    ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-12d, 1);

                    double positionError = (run.FinalState.Position - golden.P).Magnitude;
                    double velocityError = (run.FinalState.Velocity - golden.V).Magnitude;
                    string row = golden.Id + " / " + run.Integrator;
                    output.WriteLine(row + ": |ΔP|=" + positionError.ToString("E3") +
                                     " m, |ΔV|=" + velocityError.ToString("E3") +
                                     " m/s, steps=" + run.Steps + ".");
                    Asserts.True(positionError <= positionGate,
                        row + ": position error " + positionError.ToString("E3") +
                        " m > " + positionGate.ToString("E3") + " m.");
                    Asserts.True(velocityError <= velocityGate,
                        row + ": velocity error " + velocityError.ToString("E3") +
                        " m/s > " + velocityGate.ToString("E3") + " m/s.");
                    AssertGateTight(row, "position", positionError, positionGate, 1e-9d);
                    AssertGateTight(row, "velocity", velocityError, velocityGate, 1e-12d);
                }
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
        [Trait("Integrity", "CoreGolden")]
        public void CoreGoldenHashesMatch()
        {
            // Integrity pin: a golden regenerated by production code must be a
            // deliberate, reviewed change, not a silent test-side update.
            AssertHash("golden_core_kepler.json", "C6755194E29A688AABC10C4EA66B69F85583F52BC3C1E71A56737C99D5B42EE2");
            AssertHash("golden_core_kepler_1y.json", "F2BEFC87A8710D335990401B467D1AB2A224F1F3DE0E7781F7D84D845A750E72");
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
            AssertHash("golden_real_10y_k28.json", "2D319000BD9A5067CC70B9B5A8D8ABF8DD11CD9924E6FB626A80062EBB37DA74");
        }

        private static void AssertGateTight(string id, string field, double measured, double gate, double floor)
        {
            Asserts.True(gate <= Math.Max(measured * 10d, floor),
                id + ": " + field + " gate " + gate.ToString("E3") +
                " is more than 10x the measured " + measured.ToString("E3") +
                "; tighten GoldenGates.");
        }

        private static void AssertCoreGoldenDocument(GoldenDocument document)
        {
            Asserts.True(document.Generator == "gen_core_kepler.py",
                "Core golden generator is \"" + document.Generator + "\", expected gen_core_kepler.py.");
            Asserts.True(document.Model == "two-body, central at origin",
                "Core golden model is \"" + document.Model + "\", expected two-body, central at origin.");
        }

        private static (double Position, double Velocity) GoldenGateFor(string id, IntegratorKind integrator)
        {
            if (GoldenGates.TryGetValue((id, integrator), out (double Position, double Velocity) gate))
            {
                return gate;
            }

            throw new InvalidOperationException(
                "Golden case \"" + id + "\" / " + integrator + " has no absolute gate; add it to GoldenGates.");
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
