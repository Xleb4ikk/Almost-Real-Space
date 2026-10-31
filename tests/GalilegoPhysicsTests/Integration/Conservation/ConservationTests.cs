using System;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests.Integration.Conservation
{
    [Trait("Tier", "Fast")]
    public sealed class ConservationTests
    {
        private readonly ITestOutputHelper output;

        public ConservationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData("kepler-circular", "Dopri5")]
        [InlineData("kepler-circular", "Dop853")]
        [InlineData("kepler-e05", "Dopri5")]
        [InlineData("kepler-e05", "Dop853")]
        [InlineData("kepler-e09", "Dopri5")]
        [InlineData("kepler-e09", "Dop853")]
        [InlineData("kepler-hyperbolic", "Dopri5")]
        [InlineData("kepler-hyperbolic", "Dop853")]
        [InlineData("kepler-retrograde", "Dopri5")]
        [InlineData("kepler-retrograde", "Dop853")]
        [InlineData("kepler-inclined", "Dopri5")]
        [InlineData("kepler-inclined", "Dop853")]
        public void ConstantsOfMotionAreConserved(string scenarioId, string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            IntegratorKind integrator = Parse(integratorName);
            ToleranceProfile table = ToleranceTable.For(integrator, scenario.Id, 1e-9d);

            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-9d, 1);
            run.ConservesEnergy(table.EnergyRelative)
               .ConservesAngularMomentum(table.AngularMomentumRelative)
               .ConservesLrl(table.LaplaceRungeLenzAbsolute);

            output.WriteLine(scenarioId + "/" + integratorName +
                             ": E=" + run.MaxEnergyDrift.ToString("E2") +
                             ", L=" + run.MaxAngularMomentumDrift.ToString("E2") +
                             ", LRL=" + run.MaxLrlDrift.ToString("E2") +
                             ", steps=" + run.Steps + ".");
        }

        [Theory]
        [InlineData("kepler-circular", "Dopri5")]
        [InlineData("kepler-circular", "Dop853")]
        [InlineData("kepler-e05", "Dopri5")]
        [InlineData("kepler-e05", "Dop853")]
        [InlineData("kepler-e09", "Dopri5")]
        [InlineData("kepler-e09", "Dop853")]
        [InlineData("kepler-hyperbolic", "Dopri5")]
        [InlineData("kepler-hyperbolic", "Dop853")]
        public void TimeReversalReturnsToTheInitialState(string scenarioId, string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            IntegratorKind integrator = Parse(integratorName);
            ToleranceProfile table = ToleranceTable.For(integrator, scenario.Id, 1e-9d);

            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-9d, 1);
            run.IsTimeReversible(table.ReversibilityPositionMeters, table.ReversibilityVelocityMetersPerSecond);
            output.WriteLine(scenarioId + "/" + integratorName + ": reverse OK, steps=" + run.Steps + ".");
        }

        [Theory]
        [InlineData("kepler-circular", "Dopri5")]
        [InlineData("kepler-circular", "Dop853")]
        [InlineData("kepler-e05", "Dopri5")]
        [InlineData("kepler-e05", "Dop853")]
        [InlineData("kepler-hyperbolic", "Dopri5")]
        [InlineData("kepler-hyperbolic", "Dop853")]
        public void ChunkingTheIntervalDoesNotChangeTheAnswer(string scenarioId, string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            IntegratorKind integrator = Parse(integratorName);
            ToleranceProfile table = ToleranceTable.For(integrator, scenario.Id, 1e-9d);

            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, 1e-9d, 1);
            run.IsChunkingInvariant(table.ChunkingPositionMeters, table.ChunkingVelocityMetersPerSecond);
        }

        [Theory]
        [InlineData("kepler-circular", "Dopri5")]
        [InlineData("kepler-circular", "Dop853")]
        [InlineData("kepler-e05", "Dopri5")]
        [InlineData("kepler-e05", "Dop853")]
        [InlineData("restricted-3body", "Dopri5")]
        [InlineData("restricted-3body", "Dop853")]
        [InlineData("thrust-tsiolkovsky", "Dopri5")]
        [InlineData("thrust-tsiolkovsky", "Dop853")]
        public void RepeatedRunsAreBitwiseIdentical(string scenarioId, string integratorName)
        {
            Scenario scenario = ScenarioCatalog.Get(scenarioId);
            ScenarioExecution run = ScenarioExecution.Execute(scenario, Parse(integratorName), 1e-9d, 1);
            run.IsDeterministic();
        }

        private static IntegratorKind Parse(string name)
        {
            return name == "Dop853" ? IntegratorKind.Dop853 : IntegratorKind.Dopri5;
        }
    }
}

