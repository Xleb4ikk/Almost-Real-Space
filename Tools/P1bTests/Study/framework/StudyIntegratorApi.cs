using System;

namespace P1bStudy.Framework
{
    internal enum StudyKind
    {
        Fixed,
        Adaptive
    }

    /// <summary>
    /// Everything the harness needs to know about a method. To add an integrator:
    /// derive from FixedStepIntegrator or AdaptiveIntegrator (a stepper + five
    /// one-line members), then add one line to StudyRegistry.All().
    /// </summary>
    internal interface IStudyIntegrator
    {
        string Name { get; }

        /// <summary>Free-text group used only for reports ("Runge-Kutta", "Symplectic", ...).</summary>
        string Family { get; }

        StudyKind Kind { get; }

        /// <summary>Order used by the observed-order check of fixed-step methods. NaN = not checked.</summary>
        double ExpectedOrder { get; }

        bool Supports(StudyProblem p, out string reason);

        /// <summary>
        /// Sweep range of the control value: coarse (start), fine (do not go below) and the
        /// factor between successive points. Fixed: control = h in seconds. Adaptive: control = rtol.
        /// </summary>
        void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio);

        StudyConfig MakeConfig(StudyProblem p, double control, double maxStep);

        StudyRun Run(StudyProblem p, StudyConfig cfg, StudyContext ctx);
    }

    /// <summary>One fixed-size step. May keep a cached force between calls (valid because the end state of one step is the start of the next).</summary>
    internal interface IFixedStepper
    {
        void Step(double t, double h, double[] y);

        /// <summary>Called at every stop; fold compensated sums into y here.</summary>
        void Flush(double[] y);
    }

    /// <summary>One trial step of an adaptive method.</summary>
    internal interface IAdaptiveStepper
    {
        /// <summary>Controller exponent denominator q: factor = 0.9 * err^(-1/q).</summary>
        int ErrorOrderExponent { get; }

        /// <summary>Called once with the initial state (FSAL methods evaluate k1 here).</summary>
        void Begin(double t, double[] y);

        /// <summary>Tries (t, y) -> (t + h, yNew). Returns the scaled error norm; the step is accepted when &lt;= 1.</summary>
        double TryStep(double t, double h, double[] y, double[] yNew);

        /// <summary>Commits the last TryStep (FSAL bookkeeping). Not called for rejected steps.</summary>
        void Accept(double tNew, double[] yNew);
    }

    internal abstract class FixedStepIntegrator : IStudyIntegrator
    {
        public abstract string Name { get; }

        public abstract string Family { get; }

        public abstract double ExpectedOrder { get; }

        public StudyKind Kind
        {
            get { return StudyKind.Fixed; }
        }

        public abstract bool Supports(StudyProblem p, out string reason);

        protected abstract IFixedStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx);

        public virtual void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            double scale = p.CharTime > 0d ? p.CharTime : p.Duration / 50d;
            coarse = 0.25d * scale;
            fine = coarse / 30000d;
            ratio = 1.5d;
        }

        public StudyConfig MakeConfig(StudyProblem p, double control, double maxStep)
        {
            var cfg = new StudyConfig();
            cfg.Control = control;
            cfg.H = control;
            cfg.Label = "h=" + control.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
            return cfg;
        }

        public StudyRun Run(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            IFixedStepper stepper = CreateStepper(p, cfg, ctx);
            StudyStops stops = StudyStops.Build(p);
            double[] y = (double[])p.Y0.Clone();
            var run = new StudyRun();
            run.Samples = new double[p.CheckpointTimes.Length][];
            double t = p.T0;
            for (int k = 0; k < stops.Times.Length; k++)
            {
                double tStop = stops.Times[k];
                double span = tStop - t;
                if (span > 0d)
                {
                    // Equal sub-steps inside each interval: h_eff <= h, the stop is hit
                    // exactly and no tiny remainder step distorts the cost or the error.
                    long n = (long)Math.Ceiling((span / cfg.H) - 1e-9d);
                    if (n < 1L)
                    {
                        n = 1L;
                    }

                    double hh = span / n;
                    for (long i = 0; i < n; i++)
                    {
                        stepper.Step(t + (i * hh), hh, y);
                        if (ctx.Abort != null)
                        {
                            run.Aborted = true;
                            run.AbortTime = t + (i * hh);
                            run.Abort = ctx.Abort;
                            run.Final = y;
                            return run;
                        }
                    }

                    run.Steps += n;
                }

                stepper.Flush(y);
                t = tStop;
                if (stops.CheckpointIndex[k] >= 0)
                {
                    run.Samples[stops.CheckpointIndex[k]] = (double[])y.Clone();
                }

                if (p.DomainViolation != null)
                {
                    string why = p.DomainViolation(y);
                    if (why != null)
                    {
                        run.Aborted = true;
                        run.AbortTime = t;
                        run.Abort = why;
                        run.Final = y;
                        return run;
                    }
                }
            }

            run.Final = y;
            return run;
        }
    }

    internal abstract class AdaptiveIntegrator : IStudyIntegrator
    {
        public abstract string Name { get; }

        public abstract string Family { get; }

        public double ExpectedOrder
        {
            get { return double.NaN; }
        }

        public StudyKind Kind
        {
            get { return StudyKind.Adaptive; }
        }

        public abstract bool Supports(StudyProblem p, out string reason);

        protected abstract IAdaptiveStepper CreateStepper(StudyProblem p, StudyConfig cfg, StudyContext ctx);

        public virtual void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            coarse = 1e-3d;
            fine = 1e-16d;
            ratio = Math.Sqrt(10d);
        }

        public StudyConfig MakeConfig(StudyProblem p, double control, double maxStep)
        {
            var cfg = new StudyConfig();
            cfg.Control = control;
            cfg.Rtol = control;
            cfg.Atol = 0d;
            cfg.MaxStep = maxStep;
            if (p.MaxStepSeconds > 0d
                && (double.IsPositiveInfinity(cfg.MaxStep) || cfg.MaxStep > p.MaxStepSeconds))
            {
                cfg.MaxStep = p.MaxStepSeconds;
            }

            cfg.Label = "rtol=" + control.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);
            return cfg;
        }

        /// <summary>
        /// Step-size driver shared by every adaptive method. The controller state (h) survives
        /// checkpoints: a stop only clips the step, it never restarts the controller or the FSAL stage.
        /// </summary>
        public StudyRun Run(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            IAdaptiveStepper stepper = CreateStepper(p, cfg, ctx);
            StudyStops stops = StudyStops.Build(p);
            int dim = p.Dim;
            double[] y = (double[])p.Y0.Clone();
            double[] yNew = new double[dim];
            var run = new StudyRun();
            run.Samples = new double[p.CheckpointTimes.Length][];
            double t = p.T0;
            double maxStep = cfg.MaxStep > 0d ? cfg.MaxStep : double.PositiveInfinity;
            double h = cfg.H0 > 0d ? cfg.H0 : (p.CharTime > 0d ? 1e-3d * p.CharTime : 1e-3d * p.Duration);
            h = Math.Min(h, maxStep);
            double minStep = 1e-14d * Math.Max(p.Duration, 1d);
            int q = stepper.ErrorOrderExponent;
            bool lastTrialNonFinite = false;
            stepper.Begin(t, y);
            for (int k = 0; k < stops.Times.Length; k++)
            {
                double tStop = stops.Times[k];
                while (tStop - t > 1e-12d * Math.Max(1d, Math.Abs(tStop)))
                {
                    double remaining = tStop - t;
                    double hTry = Math.Min(h, maxStep);
                    bool clipped = false;
                    if (hTry * 1.01d >= remaining)
                    {
                        hTry = remaining;
                        clipped = true;
                    }

                    double err = stepper.TryStep(t, hTry, y, yNew);
                    if (ctx.Abort != null)
                    {
                        run.Aborted = true;
                        run.AbortTime = t;
                        run.Abort = ctx.Abort;
                        run.Final = y;
                        return run;
                    }

                    lastTrialNonFinite = double.IsNaN(err) || double.IsInfinity(err);
                    if (lastTrialNonFinite)
                    {
                        err = 1e10d;
                    }

                    if (err <= 1d)
                    {
                        double tNew = clipped ? tStop : t + hTry;
                        stepper.Accept(tNew, yNew);
                        Array.Copy(yNew, y, dim);
                        t = tNew;
                        run.Steps++;
                        if (p.DomainViolation != null && (run.Steps & 255L) == 0L)
                        {
                            string why = p.DomainViolation(y);
                            if (why != null)
                            {
                                run.Aborted = true;
                                run.AbortTime = t;
                                run.Abort = why;
                                run.Final = y;
                                return run;
                            }
                        }

                        double fac = 0.9d * Math.Pow(Math.Max(err, 1e-10d), -1d / q);
                        fac = Math.Min(5d, Math.Max(0.2d, fac));
                        double hNew = hTry * fac;
                        if (hNew > maxStep)
                        {
                            run.CapLimited++;
                            hNew = maxStep;
                        }

                        if (clipped)
                        {
                            hNew = Math.Max(hNew, Math.Min(h, maxStep));
                        }

                        h = hNew;
                    }
                    else
                    {
                        run.Rejected++;
                        double fac = 0.9d * Math.Pow(err, -1d / q);
                        fac = Math.Min(0.9d, Math.Max(0.1d, fac));
                        h = hTry * fac;
                        if (h < minStep)
                        {
                            // The step controller reached its floor. This is a property of
                            // the trajectory (the state entered a singularity or left the
                            // physical domain), not a harness failure; report the reason so
                            // the sweep can tell it apart from a controller bug.
                            run.Aborted = true;
                            run.AbortTime = t;
                            run.Abort = (lastTrialNonFinite
                                    ? "non-finite trial state; "
                                    : "scaled error stayed above 1 at the smallest step; ")
                                + "step floor " + minStep.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                                + " s reached at t=" + t.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                                + " s (last scaled error " + err.ToString("E3", System.Globalization.CultureInfo.InvariantCulture) + ")";
                            run.Final = y;
                            return run;
                        }
                    }
                }

                t = tStop;
                if (stops.CheckpointIndex[k] >= 0)
                {
                    run.Samples[stops.CheckpointIndex[k]] = (double[])y.Clone();
                }

                if (p.DomainViolation != null)
                {
                    string why = p.DomainViolation(y);
                    if (why != null)
                    {
                        run.Aborted = true;
                        run.AbortTime = t;
                        run.Abort = why;
                        run.Final = y;
                        return run;
                    }
                }
            }

            run.Final = y;
            return run;
        }
    }
}
