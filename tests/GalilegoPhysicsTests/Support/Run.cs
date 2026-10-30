using GalilegoPhysicsTests.Support.Scenarios;

namespace GalilegoPhysicsTests.Support
{
    internal static class Integrator
    {
        public static IntegratorKind Dopri5 => IntegratorKind.Dopri5;
        public static IntegratorKind Dop853 => IntegratorKind.Dop853;
    }

    internal static class Run
    {
        public static ScenarioRunBuilder Scenario(string id)
        {
            return new ScenarioRunBuilder(ScenarioCatalog.Get(id));
        }
    }

    internal sealed class ScenarioRunBuilder
    {
        private readonly Scenario scenario;
        private IntegratorKind integrator = IntegratorKind.Dop853;
        private double tolerance = 1e-9d;

        public ScenarioRunBuilder(Scenario scenario)
        {
            this.scenario = scenario;
        }

        public ScenarioRunBuilder With(IntegratorKind kind, double tolerance)
        {
            integrator = kind;
            this.tolerance = tolerance;
            return this;
        }

        public ScenarioExecution Execute()
        {
            return ScenarioExecution.Execute(scenario, integrator, tolerance, 1);
        }

        public ScenarioExecution ExecuteChunks(int chunks)
        {
            return ScenarioExecution.Execute(scenario, integrator, tolerance, chunks);
        }
    }
}
