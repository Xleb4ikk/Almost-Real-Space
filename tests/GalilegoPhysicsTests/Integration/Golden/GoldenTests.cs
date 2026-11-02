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
                [("golden-kepler-circular-1p", IntegratorKind.Dop853)] = (3.0e-7d, 3.5e-10d),
                [("golden-kepler-e05-1p", IntegratorKind.Dop853)] = (4.5e-7d, 6.0e-10d),
                [("golden-kepler-e09-1p", IntegratorKind.Dop853)] = (1.4e-5d, 2.0e-7d),
                [("golden-kepler-e099-1p", IntegratorKind.Dop853)] = (4.8e-4d, 2.1e-4d),
                [("golden-kepler-near-parabolic-100s", IntegratorKind.Dop853)] = (7.5e-7d, 1.0e-8d),
                [("golden-kepler-hyperbolic-1h", IntegratorKind.Dop853)] = (2.3e-8d, 2.0e-11d),
                [("golden-kepler-inclined-1p", IntegratorKind.Dop853)] = (9.0e-7d, 1.0e-9d),
                [("golden-kepler-e05-1y", IntegratorKind.Dop853)] = (1.0d, 1.5e-3d),

                [("golden-kepler-circular-1p", IntegratorKind.Dopri5)] = (1.9e-4d, 2.0e-7d),
                [("golden-kepler-e05-1p", IntegratorKind.Dopri5)] = (5.8e-4d, 8.0e-7d),
                [("golden-kepler-e09-1p", IntegratorKind.Dopri5)] = (7.8e-3d, 1.15e-4d),
                [("golden-kepler-e099-1p", IntegratorKind.Dopri5)] = (2.25e-1d, 1.0e-1d),
                [("golden-kepler-near-parabolic-100s", IntegratorKind.Dopri5)] = (1.2e-4d, 1.6e-6d),
                [("golden-kepler-hyperbolic-1h", IntegratorKind.Dopri5)] = (2.3e-5d, 7.5e-9d),
                [("golden-kepler-inclined-1p", IntegratorKind.Dopri5)] = (3.0e-4d, 3.2e-7d)

                // DOPRI5 is deliberately absent from the year-long case: it
                // drifts ~9,000 km after one year, which is not a certified
                // result for the production default's reference golden. DOP853 has to
                // stay under 1.0 m / 1.5e-3 m/s (measured 0.389 m / 5.64e-4 m/s).
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
            Asserts.True(document.Cases.Count == 7,
                "fast Core golden must contain exactly seven cases, got " +
                document.Cases.Count + ".");
            foreach (GoldenCase golden in document.Cases)
            {
                Scenario scenario = GoldenScenarios.Create(golden);
                foreach (IntegratorKind integrator in new[] { IntegratorKind.Dop853, IntegratorKind.Dopri5 })
                {
                    if (!GoldenGates.ContainsKey((golden.Id, integrator)))
                    {
                        continue;
                    }

                    (double positionGate, double velocityGate) = GoldenGateFor(golden.Id, integrator);
                    ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-12d, 1);

                    double positionError = (run.FinalState.Position - golden.P).Magnitude;
                    double velocityError = (run.FinalState.Velocity - golden.V).Magnitude;
                    string row = golden.Id + " / " + run.Integrator;
                    output.WriteLine(row + ": |dP|=" + positionError.ToString("E3") +
                                     " m, |dV|=" + velocityError.ToString("E3") +
                                     " m/s, steps=" + run.Steps + ".");
                    Asserts.True(positionError <= positionGate,
                        row + ": position error " + positionError.ToString("E3") +
                        " m > " + positionGate.ToString("E3") + " m.");
                    Asserts.True(velocityError <= velocityGate,
                        row + ": velocity error " + velocityError.ToString("E3") +
                        " m/s > " + velocityGate.ToString("E3") + " m/s.");
                    AssertGateTight(row, "position", positionError, positionGate, 1e-12d);
                    AssertGateTight(row, "velocity", velocityError, velocityGate, 1e-15d);
                    run.MatchesPhysicalAccuracy();
                    AssertGoldenCheckpoints(golden, scenario, run, row, positionGate, velocityGate);
                }
            }
        }

        [Fact]
        [Trait("Tier", "Slow")]
        public void SlowCoreGoldenCaseMatches()
        {
            GoldenDocument document = GoldenDocument.Load("golden_core_kepler_1y.json");
            AssertCoreGoldenDocument(document);
            Asserts.True(document.Cases.Count == 1,
                "slow Core golden must contain exactly one case, got " +
                document.Cases.Count + ".");
            foreach (GoldenCase golden in document.Cases)
            {
                Scenario scenario = GoldenScenarios.Create(golden);
                foreach (IntegratorKind integrator in new[] { IntegratorKind.Dop853, IntegratorKind.Dopri5 })
                {
                    if (!GoldenGates.ContainsKey((golden.Id, integrator)))
                    {
                        continue;
                    }

                    (double positionGate, double velocityGate) = GoldenGateFor(golden.Id, integrator);
                    ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-12d, 1);

                    double positionError = (run.FinalState.Position - golden.P).Magnitude;
                    double velocityError = (run.FinalState.Velocity - golden.V).Magnitude;
                    string row = golden.Id + " / " + run.Integrator;
                    output.WriteLine(row + ": |dP|=" + positionError.ToString("E3") +
                                     " m, |dV|=" + velocityError.ToString("E3") +
                                     " m/s, steps=" + run.Steps + ".");
                    Asserts.True(positionError <= positionGate,
                        row + ": position error " + positionError.ToString("E3") +
                        " m > " + positionGate.ToString("E3") + " m.");
                    Asserts.True(velocityError <= velocityGate,
                        row + ": velocity error " + velocityError.ToString("E3") +
                        " m/s > " + velocityGate.ToString("E3") + " m/s.");

                    // The 1-year golden is a final state only; the analytic oracle
                    // checks 65 interior samples so a correct endpoint cannot hide a
                    // wrong trajectory in between.
                    Asserts.True(run.MaxPositionError <= positionGate,
                        row + ": interior max position " + run.MaxPositionError.ToString("E3") +
                        " m > " + positionGate.ToString("E3") + " m.");
                    Asserts.True(run.MaxVelocityError <= velocityGate,
                        row + ": interior max velocity " + run.MaxVelocityError.ToString("E3") +
                        " m/s > " + velocityGate.ToString("E3") + " m/s.");
                    AssertGateTight(row, "position", positionError, positionGate, 1e-12d);
                    AssertGateTight(row, "velocity", velocityError, velocityGate, 1e-15d);
                    run.MatchesPhysicalAccuracy();
                    AssertGoldenCheckpoints(golden, scenario, run, row, positionGate, velocityGate);
                }
            }
        }

        [Fact]
        [Trait("Tier", "Fast")]
        public void EveryGoldenCaseHasAnExplicitGate()
        {
            // Without this, a newly added golden with no GoldenGates entry is
            // silently skipped by the loops above. Fast cases must certify both
            // integrators; the year-long case certifies DOP853 only (DOPRI5 is
            // covered by a finite smoke test, not by an accuracy gate).
            GoldenDocument fast = GoldenDocument.Load("golden_core_kepler.json");
            GoldenDocument slow = GoldenDocument.Load("golden_core_kepler_1y.json");
            var caseIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (GoldenCase golden in fast.Cases)
            {
                caseIds.Add(golden.Id);
                Asserts.True(GoldenGates.ContainsKey((golden.Id, IntegratorKind.Dop853)),
                    golden.Id + " has no DOP853 gate.");
                Asserts.True(GoldenGates.ContainsKey((golden.Id, IntegratorKind.Dopri5)),
                    golden.Id + " has no DOPRI5 gate.");
            }

            foreach (GoldenCase golden in slow.Cases)
            {
                caseIds.Add(golden.Id);
                Asserts.True(GoldenGates.ContainsKey((golden.Id, IntegratorKind.Dop853)),
                    golden.Id + " has no DOP853 gate.");
            }

            foreach ((string Id, IntegratorKind Integrator) in GoldenGates.Keys)
            {
                Asserts.True(caseIds.Contains(Id),
                    "GoldenGates has a stale entry for \"" + Id + "\" / " + Integrator + ".");
            }
        }

        [Fact]
        [Trait("Tier", "Fast")]
        public void Dopri5YearLongGoldenStaysFinite()
        {
            // DOPRI5 is not certified on the year-long golden (it drifts
            // ~9,000 km where DOP853 measures 0.389 m), but it must not produce
            // NaN/Inf, stall on the same one-year arc at a coarse tolerance, or
            // leave the orbit entirely.
            GoldenDocument document = GoldenDocument.Load("golden_core_kepler_1y.json");
            AssertCoreGoldenDocument(document);
            foreach (GoldenCase golden in document.Cases)
            {
                Scenario scenario = GoldenScenarios.Create(golden);
                ScenarioExecution run = ScenarioExecution.Execute(
                    scenario, IntegratorKind.Dopri5, 1e-6d, 1);

                SpacecraftIntegrationState final = run.FinalState;
                bool finite =
                    double.IsFinite(final.Position.X) && double.IsFinite(final.Position.Y) &&
                    double.IsFinite(final.Position.Z) && double.IsFinite(final.Velocity.X) &&
                    double.IsFinite(final.Velocity.Y) && double.IsFinite(final.Velocity.Z) &&
                    double.IsFinite(final.Mass);
                double positionError = (final.Position - golden.P).Magnitude;
                double velocityError = (final.Velocity - golden.V).Magnitude;
                output.WriteLine(golden.Id + " / Dopri5 smoke: steps=" + run.Steps +
                                 ", finite=" + finite +
                                 ", |dP|=" + positionError.ToString("E3") +
                                 " m, |dV|=" + velocityError.ToString("E3") + " m/s.");

                Asserts.True(run.Steps > 0, golden.Id + " / Dopri5: no steps taken.");
                Asserts.True(finite, golden.Id + " / Dopri5: final state is not finite.");
                // DOPRI5 is not certified on this arc (it drifts by ~9,000 km
                // over the year), but a finite state alone would also accept an
                // escape or a collapse. These are divergence bounds, not
                // accuracy gates: the state must stay within twice the orbit
                // size and twice the periapsis speed. The certified DOP853 gate
                // stays 1.0 m / 1.5e-3 m/s.
                double positionLimit = 2d * Math.Abs(scenario.SemiMajorAxisMeters);
                double velocityLimit = 2d * scenario.PeriapsisSpeedMetersPerSecond;
                Asserts.True(positionError <= positionLimit,
                    golden.Id + " / Dopri5: final position drift " +
                    positionError.ToString("E3") + " m > " + positionLimit.ToString("E3") + " m.");
                Asserts.True(velocityError <= velocityLimit,
                    golden.Id + " / Dopri5: final velocity drift " +
                    velocityError.ToString("E3") + " m/s > " + velocityLimit.ToString("E3") + " m/s.");
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
            AssertHash("golden_core_kepler.json", "F14DE29B0345113EF540E610A564E5073BF0BE429E6D2920A5815540A2E94B48");
            AssertHash("golden_core_kepler_1y.json", "74B41DB183755063261616CB32310BF45FB50E98D620AF29331909EE61E3D60A");
        }

        [Fact]
        [Trait("Tier", "Fast")]
        [Trait("Integrity", "LinkedStandGolden")]
        public void LinkedGoldenHashesMatchTheStandReadme()
        {
            // Integrity pin for the linked stand data, not a Core physics gate.
            // A deliberate golden regeneration in P1bTests updates these hashes.
            AssertHash("golden_real.json", "6FA820F7C492500C2E7A8FDF3DBF93A2CB72323914104041FDEBFFFB50544A67");
            AssertHash("golden_real_1y_hi.json", "F5EB80C44B75AE98314098E6FD742EC16B21F16044521B47CCB39CBC06E94BFC");
            AssertHash("golden_real_10y_k28.json", "2B8730BB137CF8CE1F15021C6B807433C51490BF37295726DB29E43228D429E9");
        }

        /// <summary>
        /// Fast goldens are final-state vectors; the mpmath generator also
        /// writes interior checkpoints. Without these the matrix could calibrate
        /// against a KeplerOracle that is wrong in the middle of the arc.
        /// </summary>
        private void AssertGoldenCheckpoints(
            GoldenCase golden, Scenario scenario, ScenarioExecution run, string row,
            double positionGate, double velocityGate)
        {
            Asserts.True(golden.Checkpoints.Count == 3,
                row + ": golden must have exactly three interior checkpoints, got " +
                golden.Checkpoints.Count + ".");
            double[] expectedFractions = { 0.25d, 0.5d, 0.75d };
            for (int i = 0; i < expectedFractions.Length; i++)
            {
                Asserts.Near(golden.Checkpoints[i].Fraction, expectedFractions[i], 1e-12d,
                    row + ": checkpoint " + i + " fraction");
            }

            foreach (GoldenCheckpoint checkpoint in golden.Checkpoints)
            {
                double time = scenario.DurationSeconds * checkpoint.Fraction;
                SpacecraftIntegrationState dense = DenseSegment.EvaluateTrack(run.Segments, time);
                double positionError = (dense.Position - checkpoint.P).Magnitude;
                double velocityError = (dense.Velocity - checkpoint.V).Magnitude;
                output.WriteLine(row + " @" + checkpoint.Fraction.ToString("F2") +
                                 ": |dP|=" + positionError.ToString("E3") +
                                 " m, |dV|=" + velocityError.ToString("E3") + " m/s.");
                // The final-state gate is the requirement. Interior-dense points
                // get 2x because the interpolation polynomial is not the endpoint
                // state itself; the factor is still far tighter than any physical
                // ceiling and catches a wrong mid-arc trajectory.
                Asserts.True(positionError <= positionGate * 2d,
                    row + " @" + checkpoint.Fraction.ToString("F2") + ": position " +
                    positionError.ToString("E3") + " m exceeds 2x the golden gate " +
                    positionGate.ToString("E3") + " m.");
                Asserts.True(velocityError <= velocityGate * 2d,
                    row + " @" + checkpoint.Fraction.ToString("F2") + ": velocity " +
                    velocityError.ToString("E3") + " m/s exceeds 2x the golden gate " +
                    velocityGate.ToString("E3") + " m/s.");
            }
        }

        /// <summary>
        /// A golden gate is a requirement, but a gate that is many times the
        /// measured error is not testing anything. This guard keeps the
        /// requirement honest: gates stay within 3x of the current measurement
        /// (or a tiny floor so exact-zero cases remain expressible).
        /// </summary>
        private const double MaxGateHeadroom = 3.0d;

        private static void AssertGateTight(string id, string field, double measured, double gate, double floor)
        {
            Asserts.True(gate <= Math.Max(measured * MaxGateHeadroom, floor),
                id + ": " + field + " gate " + gate.ToString("E3") +
                " is more than " + MaxGateHeadroom.ToString("F1") + "x the measured " +
                measured.ToString("E3") + "; tighten GoldenGates.");
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
            byte[] hash = SHA256.HashData(NormalizeLf(File.ReadAllBytes(path)));
            string actual = Convert.ToHexString(hash);
            Asserts.True(actual == expected, fileName + ": SHA-256 is " + actual + ", expected " + expected + ".");
            output.WriteLine(fileName + ": SHA-256 " + actual + ".");
        }

        private static byte[] NormalizeLf(byte[] bytes)
        {
            var normalized = new List<byte>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')
                {
                    continue;
                }

                normalized.Add(bytes[i]);
            }

            return normalized.ToArray();
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


