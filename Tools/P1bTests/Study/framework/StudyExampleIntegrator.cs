using System;

namespace P1bStudy.Framework
{
    /// <summary>
    /// Worked example of "add an integrator": velocity Verlet (2nd order). The stepper is in
    /// StudySteppers.cs (LeapfrogStepper, ~40 lines); this class is the whole registration.
    /// Copy it, change the stepper, add one line to StudyRegistry.All().
    ///
    /// For an adaptive method derive from AdaptiveIntegrator instead and implement IAdaptiveStepper
    /// (TryStep returns the scaled error norm, accepted when &lt;= 1); the driver handles step control,
    /// checkpoints, breakpoints, the max-step cap and cost counting.
    ///
    /// Rules the harness relies on:
    ///   - Evaluate the force only through ctx.Rhs(t, y, dy); never call the problem's Rhs directly.
    ///   - Report non-RHS work (Kepler solves, Newton iterations on implicit stages, ...) with
    ///     ctx.CountAux(n); StudyCalibration prices it against an RHS call by measurement.
    ///   - Return false from Supports(...) for problems the method is not valid for (velocity-dependent
    ///     force, no central body, ...). The row is then reported as n/a with the reason instead of
    ///     being ranked on a wrong answer.
    /// </summary>
    internal sealed class LeapfrogIntegrator : FixedStepIntegrator
    {
        public override string Name { get { return "Leapfrog"; } }

        public override string Family { get { return "Symplectic"; } }

        public override double ExpectedOrder { get { return 2d; } }

        public override bool Supports(StudyProblem p, out string reason)
        {
            return StudySupport.Conservative(p, out reason);
        }

        protected override IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new LeapfrogStepper(p, ctx);
        }
    }
}
