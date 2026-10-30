using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Galilego.Core;
using GalilegoPhysicsTests.Support.Oracles;
using GalilegoPhysicsTests.Support.Scenarios;

namespace GalilegoPhysicsTests.Support.Golden
{
    internal sealed class GoldenCase
    {
        public string Id;
        public double Mu;
        public double Duration;
        public Vector3d R0;
        public Vector3d V0;
        public Vector3d P;
        public Vector3d V;
    }

    internal sealed class GoldenDocument
    {
        public string Generator;
        public string Model;
        public List<GoldenCase> Cases = new List<GoldenCase>();

        public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "GoldenData");

        public static GoldenDocument Load(string fileName)
        {
            string path = Path.Combine(DirectoryPath, fileName);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            var result = new GoldenDocument
            {
                Generator = root.TryGetProperty("generator", out JsonElement generator) ? generator.GetString() : null,
                Model = root.TryGetProperty("model", out JsonElement model) ? model.GetString() : null
            };

            foreach (JsonElement item in root.GetProperty("cases").EnumerateArray())
            {
                var golden = new GoldenCase
                {
                    Id = item.GetProperty("id").GetString(),
                    Mu = item.GetProperty("mu").GetDouble(),
                    Duration = item.GetProperty("duration").GetDouble(),
                    R0 = ReadVector(item.GetProperty("r0")),
                    V0 = ReadVector(item.GetProperty("v0")),
                    P = ReadVector(item.GetProperty("P")),
                    V = ReadVector(item.GetProperty("V"))
                };
                result.Cases.Add(golden);
            }

            return result;
        }

        private static Vector3d ReadVector(JsonElement element)
        {
            double[] values = new double[3];
            int index = 0;
            foreach (JsonElement component in element.EnumerateArray())
            {
                string text = component.ValueKind == JsonValueKind.String
                    ? component.GetString()
                    : component.GetRawText();
                values[index++] = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            if (index != 3)
            {
                throw new FormatException("Golden vector must have exactly three components.");
            }

            return new Vector3d(values[0], values[1], values[2]);
        }
    }

    internal static class GoldenScenarios
    {
        public static Scenario Create(GoldenCase golden)
        {
            double mu = golden.Mu;
            var scenario = new Scenario
            {
                Id = golden.Id,
                Title = "Golden " + golden.Id,
                Family = "Golden",
                Class = ClassOf(golden.Id),
                Tier = ScenarioTier.Fast,
                Mu = mu,
                DurationSeconds = golden.Duration,
                InitialState = new SpacecraftIntegrationState
                {
                    Position = golden.R0,
                    Velocity = golden.V0,
                    Mass = 1000d
                },
                Derivative = (state, time) => new SpacecraftIntegrationState
                {
                    Position = state.Velocity,
                    Velocity = PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                        state.Position, Vector3d.Zero, mu),
                    Mass = 0d
                },
                Oracle = null,
                CheckEnergy = false,
                CheckAngularMomentum = false,
                CheckLrl = false,
                Tolerances = new[] { 1e-12d }
            };
            return scenario;
        }

        private static ScenarioClass ClassOf(string id)
        {
            if (id.Contains("10y"))
            {
                return ScenarioClass.GoldenCircular10y;
            }

            if (id.Contains("1y"))
            {
                return ScenarioClass.GoldenEccentric1y;
            }

            if (id.Contains("hyperbolic"))
            {
                return ScenarioClass.KeplerHyperbolic;
            }

            if (id.Contains("e09"))
            {
                return ScenarioClass.KeplerHighEccentric;
            }

            if (id.Contains("e05") || id.Contains("inclined"))
            {
                return ScenarioClass.KeplerEccentric;
            }

            return ScenarioClass.KeplerCircular;
        }
    }
}
