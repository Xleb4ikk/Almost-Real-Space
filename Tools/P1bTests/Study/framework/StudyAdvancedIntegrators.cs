using System;

namespace P1bStudy.Framework
{
    /// <summary>
    /// Registrations for the advanced methods ported from the standalone
    /// AdvancedIntegrators prototype: RKN12(10), SRKN14A, WH-BM14A, IAS15,
    /// Gauss-Jackson 8 and Encke-IAS15.
    /// </summary>
    internal sealed class Rkn1210Integrator : AdaptiveIntegrator
    {
        public override string Name
        {
            get { return "RKN12(10)"; }
        }

        public override string Family
        {
            get { return "Runge-Kutta"; }
        }

        public override bool Supports(StudyProblem p, out string reason)
        {
            return StudySupport.Conservative(p, out reason);
        }

        protected override IAdaptiveStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new Rkn1210Stepper(p, cfg, ctx);
        }

        public override void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            coarse = 1e-3d;
            fine = 1e-13d;
            ratio = Math.Sqrt(10d);
        }
    }

    internal sealed class Ias15Integrator : AdaptiveIntegrator
    {
        public override string Name
        {
            get { return "IAS15"; }
        }

        public override string Family
        {
            get { return "Gauss-Radau"; }
        }

        public override bool Supports(StudyProblem p, out string reason)
        {
            if (p.PosDim <= 0 || 2 * p.PosDim > p.Dim)
            {
                reason = "no second-order [x, v] structure";
                return false;
            }

            reason = "";
            return true;
        }

        protected override IAdaptiveStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new Ias15Stepper(p, cfg, ctx);
        }

        public override void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            coarse = 1e-3d;
            fine = 1e-13d;
            ratio = Math.Sqrt(10d);
        }
    }

    internal sealed class Srkn14AIntegrator : FixedStepIntegrator
    {
        public override string Name
        {
            get { return "SRKN14A"; }
        }

        public override string Family
        {
            get { return "Symplectic"; }
        }

        public override double ExpectedOrder
        {
            get { return 6d; }
        }

        public override bool Supports(StudyProblem p, out string reason)
        {
            return StudySupport.Conservative(p, out reason);
        }

        protected override IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new Srkn14AStepper(p, ctx);
        }
    }

    internal sealed class KeplerSplit14AIntegrator : FixedStepIntegrator
    {
        public override string Name
        {
            get { return "WH-BM14A"; }
        }

        public override string Family
        {
            get { return "Kepler-split"; }
        }

        public override double ExpectedOrder
        {
            get { return double.NaN; }
        }

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
            return new KeplerSplit14AStepper(p, ctx);
        }

        public override void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            double scale = p.CharTime > 0d ? p.CharTime : p.Duration / 50d;
            coarse = 0.5d * scale;
            fine = coarse / 30000d;
            ratio = 1.5d;
        }
    }

    internal sealed class GaussJacksonIntegrator : FixedStepIntegrator
    {
        public override string Name
        {
            get { return "GJ8"; }
        }

        public override string Family
        {
            get { return "Multistep"; }
        }

        public override double ExpectedOrder
        {
            // Summed Stormer-Cowell of difference order 8: the position error converges
            // as h^10, which is what the self-test measures.
            get { return 10d; }
        }

        public override bool Supports(StudyProblem p, out string reason)
        {
            return StudySupport.Conservative(p, out reason);
        }

        protected override IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new GaussJacksonStepper(p, ctx, 8);
        }

        public override void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            // Keep the coarsest point above the q+1 start-up intervals: a run that never
            // reaches a multistep step is IAS15 in disguise and must not win the sweep.
            double scale = p.CharTime > 0d ? p.CharTime : p.Duration / 50d;
            coarse = 0.02d * scale;
            fine = coarse / 3000d;
            ratio = 1.5d;
        }
    }

    internal sealed class EnckeIntegrator : FixedStepIntegrator
    {
        public override string Name
        {
            get { return "Encke-IAS15"; }
        }

        public override string Family
        {
            get { return "Encke"; }
        }

        public override double ExpectedOrder
        {
            get { return double.NaN; }
        }

        public override bool Supports(StudyProblem p, out string reason)
        {
            if (!(p.CentralMu > 0d) || p.PosDim != 3)
            {
                reason = "needs one central body at the origin (CentralMu > 0, PosDim == 3)";
                return false;
            }

            if (p.Dim < 6)
            {
                reason = "state has no [r, v] block";
                return false;
            }

            reason = "";
            return true;
        }

        protected override IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            return new EnckeStepper(p, ctx);
        }

        public override void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            double scale = p.CharTime > 0d ? p.CharTime : p.Duration / 50d;
            coarse = 0.25d * scale;
            fine = coarse / 64d;
            ratio = 2d;
        }
    }
}
