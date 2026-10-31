using System;
using System.Collections.Generic;
using Galilego.Core;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Oracles;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    public sealed class ToleranceTableConsistencyTests
    {
        private const double MaxHeadroom = 4d;

        [Fact]
        [Trait("Tier", "Fast")]
        public void EveryTableKeyBelongsToTheMatrix()
        {
            var expected = new HashSet<(IntegratorKind, string, double)>();
            foreach (Scenario scenario in ScenarioCatalog.All)
            {
                foreach (IntegratorKind integrator in new[] { IntegratorKind.Dopri5, IntegratorKind.Dop853 })
                {
                    foreach (double tolerance in scenario.TolerancesFor(integrator))
                    {
                        expected.Add((integrator, scenario.Id, tolerance));
                    }
                }
            }

            var actual = new HashSet<(IntegratorKind, string, double)>(ToleranceTable.Keys);
            var missing = new List<string>();
            foreach ((IntegratorKind integrator, string scenario, double tolerance) in expected)
            {
                if (!actual.Contains((integrator, scenario, tolerance)))
                {
                    missing.Add(scenario + " / " + integrator + " / " + tolerance.ToString("R"));
                }
            }

            var stale = new List<string>();
            foreach ((IntegratorKind integrator, string scenario, double tolerance) in actual)
            {
                if (!expected.Contains((integrator, scenario, tolerance)))
                {
                    stale.Add(scenario + " / " + integrator + " / " + tolerance.ToString("R"));
                }
            }

            string message = "ToleranceTable keys do not match the matrix.";
            if (missing.Count > 0)
            {
                message += Environment.NewLine + "Missing: " + string.Join("; ", missing);
            }

            if (stale.Count > 0)
            {
                message += Environment.NewLine + "Stale: " + string.Join("; ", stale);
            }

            Asserts.True(missing.Count == 0 && stale.Count == 0, message);
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        [Trait("Tier", "Fast")]
        public void FastRowsMatchTheMeasuredCalibration(string integratorName)
        {
            CheckRows(Parse(integratorName), ScenarioCatalog.Fast());
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        [Trait("Tier", "Slow")]
        public void SlowRowsMatchTheMeasuredCalibration(string integratorName)
        {
            CheckRows(Parse(integratorName), ScenarioCatalog.Slow());
        }

        private void CheckRows(IntegratorKind integrator, IEnumerable<Scenario> scenarios)
        {
            foreach (Scenario scenario in scenarios)
            {
                foreach (double tolerance in scenario.TolerancesFor(integrator))
                {
                    ToleranceProfile gate = ToleranceTable.For(integrator, scenario.Id, tolerance);
                    ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, tolerance, 1);
                    string row = scenario.Id + " / " + scenario.IntegratorLabel(integrator) +
                                 " at tol=" + tolerance.ToString("R");

                    AssertGate(row, "position", run.MaxPositionError, gate.OraclePositionMeters);
                    AssertGate(row, "velocity", run.MaxVelocityError, gate.OracleVelocityMetersPerSecond);
                    AssertGate(row, "mass", run.MaxMassError, gate.OracleMassRelative, 1e-15d);

                    if (scenario.CheckEnergy)
                    {
                        AssertGate(row, "energy", run.MaxEnergyDrift, gate.EnergyRelative, 1e-15d);
                    }

                    if (scenario.CheckAngularMomentum)
                    {
                        AssertGate(row, "angular momentum", run.MaxAngularMomentumDrift,
                            gate.AngularMomentumRelative, 1e-15d);
                    }

                    if (scenario.CheckLrl)
                    {
                        AssertGate(row, "LRL", run.MaxLrlDrift, gate.LaplaceRungeLenzAbsolute, 1e-15d);
                    }

                    if (gate.ReversibilityPositionMeters > 0d)
                    {
                        run.IsTimeReversible(double.PositiveInfinity, double.PositiveInfinity);
                        AssertGate(row, "reversibility position",
                            run.ReversibilityPositionError, gate.ReversibilityPositionMeters);
                        AssertGate(row, "reversibility velocity",
                            run.ReversibilityVelocityError, gate.ReversibilityVelocityMetersPerSecond);
                    }

                    if (gate.ChunkingPositionMeters > 0d)
                    {
                        run.IsChunkingInvariant(double.PositiveInfinity, double.PositiveInfinity);
                        AssertGate(row, "chunking position",
                            run.ChunkingPositionError, gate.ChunkingPositionMeters);
                        AssertGate(row, "chunking velocity",
                            run.ChunkingVelocityError, gate.ChunkingVelocityMetersPerSecond);
                    }
                }
            }
        }

        [Theory]
        [InlineData("Dopri5")]
        [InlineData("Dop853")]
        [Trait("Tier", "Fast")]
        public void EveryDenseInterpolationGateMatchesTheMeasuredError(string integratorName)
        {
            IntegratorKind integrator = Parse(integratorName);

            foreach (string scenarioId in new[] { "kepler-circular", "kepler-e05", "kepler-e09" })
            {
                Scenario scenario = ScenarioCatalog.Get(scenarioId);
                ToleranceProfile gate = ToleranceTable.For(integrator, scenario.Id, 1e-9d);
                ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-9d, 1);

                double worst = 0d;
                foreach (DenseSegment segment in run.Segments)
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        double time = segment.T0 + ((segment.T1 - segment.T0) * (i / 4d));
                        SpacecraftIntegrationState dense = segment.Evaluate(time);
                        OracleState oracle = scenario.Oracle(time);
                        worst = Math.Max(worst, (dense.Position - oracle.Position.ToVector3d()).Magnitude);
                    }
                }

                AssertGate(scenario.Id + " / " + scenario.IntegratorLabel(integrator) + " dense",
                    "dense interpolation", worst, gate.DenseInterpolationMeters);
            }
        }

        private static IntegratorKind Parse(string name)
        {
            return name == "Dop853" ? IntegratorKind.Dop853 : IntegratorKind.Dopri5;
        }

        private static void AssertGate(string row, string field, double measured, double gate, double floor = 0d)
        {
            if (double.IsNaN(measured))
            {
                Asserts.True(gate <= 0d,
                    row + ": " + field + " is not measured but has a non-zero gate " +
                    gate.ToString("E3") + "; remove the dead gate.");
                return;
            }

            if (measured == 0d)
            {
                Asserts.True(gate <= 1e-15d,
                    row + ": " + field + " measures 0 but the gate is " +
                    gate.ToString("E3") + "; remove the dead gate.");
                return;
            }

            Asserts.True(measured <= gate,
                row + ": " + field + " error " + measured.ToString("E3") +
                " exceeds gate " + gate.ToString("E3") + ".");
            Asserts.True(gate <= Math.Max(measured * MaxHeadroom, floor),
                row + ": " + field + " gate " + gate.ToString("E3") +
                " is more than " + MaxHeadroom.ToString("F0") + "x the measured " +
                measured.ToString("E3") + "; recalibrate ToleranceTable.");
        }
    }
}

