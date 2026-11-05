using System;
using System.Collections.Generic;

namespace P1bStudy.Framework
{
    // ---------------------------------------------------------------------------------
    // HOW TO ADD AN INTEGRATOR (about 25 lines):
    //   1. Write a stepper: IFixedStepper (one fixed step) or IAdaptiveStepper (one trial step).
    //      Evaluate the force ONLY through ctx.Rhs(...) - that is what is counted and priced.
    //   2. Derive from FixedStepIntegrator or AdaptiveIntegrator and fill in:
    //      Name, Family, ExpectedOrder (fixed only), Supports(...), CreateStepper(...).
    //      Override ControlRange(...) only if the default sweep range does not suit the method.
    //   3. Add "new YourIntegrator()" to StudyRegistry.All() below.
    // The sweep, interpolation, cap handling, order check, report and CSV need no changes.
    // StudyExampleIntegrator.cs shows the whole procedure with a 2nd-order leapfrog.
    // ---------------------------------------------------------------------------------

    internal sealed class Rk4Integrator : FixedStepIntegrator
    {
        public override string Name { get { return "RK4"; } }

        public override string Family { get { return "Runge-Kutta"; } }

        public override double ExpectedOrder { get { return 4d; } }

        public override bool Supports(StudyProblem p, out string reason)
        {
            reason = "";
            return true;
        }

        protected override IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new Rk4Stepper(p, ctx);
        }
    }

    internal sealed class Dopri5FixedIntegrator : FixedStepIntegrator
    {
        public override string Name { get { return "DOPRI5-fixed"; } }

        public override string Family { get { return "Runge-Kutta"; } }

        public override double ExpectedOrder { get { return 5d; } }

        public override bool Supports(StudyProblem p, out string reason)
        {
            reason = "";
            return true;
        }

        protected override IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new FixedRkStepper(RkTableau.Dopri5, p, ctx);
        }
    }

    internal sealed class Dopri5AdaptiveIntegrator : AdaptiveIntegrator
    {
        public override string Name { get { return "DOPRI5-adaptive"; } }

        public override string Family { get { return "Runge-Kutta"; } }

        public override bool Supports(StudyProblem p, out string reason)
        {
            reason = "";
            return true;
        }

        protected override IAdaptiveStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new ExplicitRkStepper(RkTableau.Dopri5, p, cfg, ctx, true);
        }
    }

    internal sealed class Dop853AdaptiveIntegrator : AdaptiveIntegrator
    {
        public override string Name { get { return "DOP853-adaptive"; } }

        public override string Family { get { return "Runge-Kutta"; } }

        public override bool Supports(StudyProblem p, out string reason)
        {
            reason = "";
            return true;
        }

        protected override IAdaptiveStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new ExplicitRkStepper(RkTableau.Dop853, p, cfg, ctx, true);
        }
    }

    /// <summary>Shared support rule of every method that needs a conservative second-order force.</summary>
    internal static class StudySupport
    {
        public static bool Conservative(StudyProblem p, out string reason)
        {
            if (p.PosDim <= 0)
            {
                reason = "no second-order [x, v] structure";
                return false;
            }

            if (!p.ConservativeSecondOrder)
            {
                reason = "force depends on velocity or mass (drag/thrust): splitting order is lost";
                return false;
            }

            reason = "";
            return true;
        }
    }

    internal sealed class PefrlIntegrator : FixedStepIntegrator
    {
        public override string Name { get { return "PEFRL"; } }

        public override string Family { get { return "Symplectic"; } }

        public override double ExpectedOrder { get { return 4d; } }

        public override bool Supports(StudyProblem p, out string reason)
        {
            return StudySupport.Conservative(p, out reason);
        }

        protected override IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new PefrlStepper(p, ctx);
        }
    }

    internal sealed class Yoshida6Integrator : FixedStepIntegrator
    {
        private readonly bool compensated;

        public Yoshida6Integrator(bool compensated)
        {
            this.compensated = compensated;
        }

        public override string Name { get { return compensated ? "Y6C" : "Y6"; } }

        public override string Family { get { return "Symplectic"; } }

        public override double ExpectedOrder { get { return 6d; } }

        public override bool Supports(StudyProblem p, out string reason)
        {
            return StudySupport.Conservative(p, out reason);
        }

        protected override IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new Yoshida6Stepper(p, ctx, compensated);
        }
    }

    internal sealed class KeplerSplitIntegrator : FixedStepIntegrator
    {
        public override string Name { get { return "WH-Y6"; } }

        public override string Family { get { return "Kepler-split"; } }

        // For a pure Kepler problem the split is exact; the order only shows on perturbed problems,
        // so the observed-order check is not applied to it.
        public override double ExpectedOrder { get { return double.NaN; } }

        public override bool Supports(StudyProblem p, out string reason)
        {
            if (!StudySupport.Conservative(p, out reason))
            {
                return false;
            }

            if (!(p.CentralMu > 0d) || p.PosDim != 3)
            {
                reason = "needs one central body at the origin (CentralMu > 0, PosDim == 3)";
                return false;
            }

            return true;
        }

        protected override IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new KeplerSplitStepper(p, ctx);
        }

        public override void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            // The split tolerates steps of a large fraction of the orbit (the perturbation is small),
            // so start coarser than the generic range; the harness loosens further when needed.
            double scale = p.CharTime > 0d ? p.CharTime : p.Duration / 50d;
            coarse = 0.5d * scale;
            fine = coarse / 30000d;
            ratio = 1.5d;
        }
    }

    internal static class StudyRegistry
    {
        /// <summary>Every integrator the study knows. Add new ones here.</summary>
        public static List<IStudyIntegrator> All()
        {
            return new List<IStudyIntegrator>
            {
                new Rk4Integrator(),
                new Dopri5FixedIntegrator(),
                new Dopri5AdaptiveIntegrator(),
                new Dop853AdaptiveIntegrator(),
                new PefrlIntegrator(),
                new Yoshida6Integrator(false),
                new Yoshida6Integrator(true),
                new KeplerSplitIntegrator(),
                new LeapfrogIntegrator()
                // , new YourNewIntegrator()
            };
        }

        /// <summary>
        /// Subset by comma/semicolon separated names (case-insensitive). Null or empty = all.
        /// Unknown names throw, so a typo in P1B_INTEGRATORS cannot silently drop a method.
        /// </summary>
        public static List<IStudyIntegrator> Select(string filter)
        {
            List<IStudyIntegrator> all = All();
            if (string.IsNullOrWhiteSpace(filter))
            {
                return all;
            }

            var result = new List<IStudyIntegrator>();
            string[] names = filter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in names)
            {
                string want = raw.Trim();
                IStudyIntegrator found = null;
                foreach (IStudyIntegrator integ in all)
                {
                    if (string.Equals(integ.Name, want, StringComparison.OrdinalIgnoreCase))
                    {
                        found = integ;
                        break;
                    }
                }

                if (found == null)
                {
                    var known = new List<string>();
                    foreach (IStudyIntegrator integ in all)
                    {
                        known.Add(integ.Name);
                    }

                    throw new ArgumentException("unknown integrator '" + want + "'; known: " + string.Join(", ", known));
                }

                if (!result.Contains(found))
                {
                    result.Add(found);
                }
            }

            return result;
        }
    }
}
