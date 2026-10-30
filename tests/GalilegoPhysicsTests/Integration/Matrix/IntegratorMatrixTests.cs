using System;
using GalilegoPhysicsTests.Support;
using GalilegoPhysicsTests.Support.Matrix;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;

namespace GalilegoPhysicsTests.Integration.Matrix
{
    public sealed class IntegratorMatrixTests
    {
        [MatrixTheory]
        [MatrixData]
        public void Matrix(MatrixCase matrix)
        {
            Scenario scenario = ScenarioCatalog.Get(matrix.ScenarioId);
            IntegratorKind integrator = matrix.IntegratorName == "Dop853"
                ? IntegratorKind.Dop853
                : IntegratorKind.Dopri5;

            ToleranceProfile tolerance = ToleranceTable.For(integrator, scenario.Class, matrix.Tolerance);
            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, matrix.Tolerance, 1);

            run.MatchesOracle(tolerance.OraclePositionMeters, tolerance.OracleVelocityMetersPerSecond);

            if (scenario.CheckEnergy)
            {
                run.ConservesEnergy(tolerance.EnergyRelative);
            }

            if (scenario.CheckAngularMomentum)
            {
                run.ConservesAngularMomentum(tolerance.AngularMomentumRelative);
            }

            if (scenario.CheckLrl)
            {
                run.ConservesLrl(tolerance.LaplaceRungeLenzAbsolute);
            }

            if (scenario.Class == ScenarioClass.Thrust)
            {
                run.MatchesMass(tolerance.OracleMassRelative);
            }
        }
    }
}
