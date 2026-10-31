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

            ToleranceProfile tolerance = ToleranceTable.For(integrator, scenario.Id, matrix.Tolerance);
            ScenarioExecution run = ScenarioExecution.Execute(scenario, integrator, matrix.Tolerance, 1);

            if (StepBudget.TryGet(scenario.Id, matrix.IntegratorName, matrix.Tolerance,
                out int expectedSteps, out long expectedCalls))
            {
                run.HasStepBudget(expectedSteps, expectedCalls, 0.1d);
            }
            else if (!Env.Calibrate)
            {
                throw new Xunit.Sdk.XunitException(
                    "РЅРµС‚ Р±СЋРґР¶РµС‚Р° С€Р°РіРѕРІ РґР»СЏ <" + scenario.Id + " | " + matrix.IntegratorName +
                    " | " + matrix.Tolerance.ToString("R") +
                    ">; Р·Р°РїСѓСЃС‚РёС‚Рµ GALILEGO_CALIBRATE=1 Рё РґРѕР±Р°РІСЊС‚Рµ СЃС‚СЂРѕРєСѓ РІ StepBudget.");
            }

            if (StepBudget.TryGetProduction(
                scenario.Id, matrix.IntegratorName, matrix.Tolerance, out long expectedProductionCalls))
            {
                long productionCalls = run.MeasureProductionRhsCalls();
                long callTolerance = Math.Max(2L, (long)Math.Ceiling(expectedProductionCalls * 0.1d));
                Asserts.True(Math.Abs(productionCalls - expectedProductionCalls) <= callTolerance,
                    "production RHS budget for " + scenario.Id + " / " + matrix.IntegratorName +
                    " at tol=" + matrix.Tolerance.ToString("R") + ": " + productionCalls +
                    ", expected " + expectedProductionCalls + "±" + callTolerance + ".");
            }

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

