using System;
using System.Collections.Generic;
using System.IO;
using Galilego.Core;
using Galilego.Spacecraft;
using P1bStudy.Framework;

// TASK.md Appendix C, C4-C5: the `production` profile and the two adapters that run the GAME
// steppers (SpacecraftOrbitIntegrator.StepForward / StepForwardDop853) inside the stand.
//
// Nothing here changes the game: the adapter calls the public game API with the game's own
// default tolerances (read from SpacecraftPhysics) and the game's own step cap
// (OrbitIntegrator.DefaultMaxStepSize = 600 s). It is the honest "what the game does now"
// baseline of C5, not a study re-implementation.
internal static partial class P1bTests
{
    /// <summary>
    /// Method list of a profile. Only `production` adds the two game adapters; smoke/full keep
    /// exactly StudyRegistry.All(), so their baseline cannot move.
    /// </summary>
    private static List<IStudyIntegrator> QualificationMethodsFor(QualificationProfile profile)
    {
        List<IStudyIntegrator> methods = StudyRegistry.All();
        if (profile == QualificationProfile.Production)
        {
            methods.Add(new QualificationProdDop853());
            methods.Add(new QualificationProdDopri5());
        }

        return methods;
    }

    /// <summary>Game tolerance defaults, read once from SpacecraftPhysics (C4: not re-invented).</summary>
    private sealed class QualificationGameTolerances
    {
        public readonly double Position;
        public readonly double Velocity;
        public readonly double Mass;
        public readonly double Relative;

        public QualificationGameTolerances(SpacecraftPhysics physics)
        {
            Position = physics.PositionAbsoluteTolerance;
            Velocity = physics.VelocityAbsoluteTolerance;
            Mass = physics.MassAbsoluteTolerance;
            Relative = physics.RelativeTolerance;
        }
    }

    private static readonly QualificationGameTolerances QualificationGameDefaults =
        new QualificationGameTolerances(new SpacecraftPhysics());

    /// <summary>
    /// Shared adapter body: maps the StudyProblem's [pos(3), vel(3)] or [pos(3), vel(3), mass(1)]
    /// state onto SpacecraftIntegrationState, runs the game stepper chunk by chunk (the game
    /// calls it once per chunk) and records accepted/rejected steps.
    /// </summary>
    private abstract class QualificationProductionIntegrator : IStudyIntegrator
    {
        private readonly bool dop853;

        protected QualificationProductionIntegrator(bool dop853)
        {
            this.dop853 = dop853;
        }

        public abstract string Name { get; }

        public string Family
        {
            get { return "Production (game stepper)"; }
        }

        public StudyKind Kind
        {
            get { return StudyKind.Adaptive; }
        }

        public double ExpectedOrder
        {
            get { return double.NaN; }
        }

        public bool Supports(StudyProblem p, out string reason)
        {
            // The game steppers integrate one 3-D particle; mass is optional (Dim 6 = no mass
            // flow, Dim 7 = [pos, vel, mass] exactly as the ship problems are laid out).
            if (p.PosDim != 3 || (p.Dim != 6 && p.Dim != 7))
            {
                reason = "the game stepper integrates one 3-D particle with optional mass "
                    + "(state [pos(3), vel(3)] or [pos(3), vel(3), mass(1)])";
                return false;
            }

            reason = "";
            return true;
        }

        public void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            // One point only: the production tolerances are the game's defaults, not a swept
            // control. The sweep driver cannot ask for a tighter game tolerance.
            coarse = 1d;
            fine = 1d;
            ratio = 2d;
        }

        public StudyConfig MakeConfig(StudyProblem p, double control, double maxStep)
        {
            var cfg = new StudyConfig();
            cfg.Control = control;
            cfg.H = OrbitIntegrator.DefaultMaxStepSize;
            cfg.MaxStep = OrbitIntegrator.DefaultMaxStepSize;
            cfg.Label = "game tolerances";
            return cfg;
        }

        public StudyRun Run(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            StudyStops stops = StudyStops.Build(p);
            double[] y = (double[])p.Y0.Clone();
            var run = new StudyRun();
            run.Samples = new double[p.CheckpointTimes.Length][];
            var diag = new SpacecraftStepDiagnostics();
            double[] yBuf = new double[p.Dim];
            double[] dyBuf = new double[p.Dim];
            double t = p.T0;

            SpacecraftOrbitIntegrator.DerivativeFunction derivative =
                delegate (SpacecraftIntegrationState state, double time)
                {
                    if (p.Dim == 7)
                    {
                        yBuf[6] = state.Mass;
                    }

                    yBuf[0] = state.Position.X;
                    yBuf[1] = state.Position.Y;
                    yBuf[2] = state.Position.Z;
                    yBuf[3] = state.Velocity.X;
                    yBuf[4] = state.Velocity.Y;
                    yBuf[5] = state.Velocity.Z;
                    ctx.Rhs(time, yBuf, dyBuf);
                    return new SpacecraftIntegrationState
                    {
                        Position = new Vector3d(dyBuf[0], dyBuf[1], dyBuf[2]),
                        Velocity = new Vector3d(dyBuf[3], dyBuf[4], dyBuf[5]),
                        Mass = p.Dim == 7 ? dyBuf[6] : 0d
                    };
                };

            for (int k = 0; k < stops.Times.Length; k++)
            {
                double tStop = stops.Times[k];
                while (tStop - t > 1e-12d * Math.Max(1d, Math.Abs(tStop)))
                {
                    double dt = Math.Min(OrbitIntegrator.DefaultMaxStepSize, tStop - t);
                    SpacecraftIntegrationState from = QualificationProductionState(y, p.Dim);
                    SpacecraftIntegrationResult result;
                    try
                    {
                        result = dop853
                            ? SpacecraftOrbitIntegrator.StepForwardDop853(
                                from, t, dt, derivative,
                                QualificationGameDefaults.Position, QualificationGameDefaults.Velocity,
                                QualificationGameDefaults.Mass, QualificationGameDefaults.Relative,
                                null, null, diag, OrbitIntegrator.DefaultMaxStepSize)
                            : SpacecraftOrbitIntegrator.StepForward(
                                from, t, dt, derivative,
                                QualificationGameDefaults.Position, QualificationGameDefaults.Velocity,
                                QualificationGameDefaults.Mass, QualificationGameDefaults.Relative,
                                null, null, diag, OrbitIntegrator.DefaultMaxStepSize);
                    }
                    catch (StudyBudgetExceeded)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        run.Aborted = true;
                        run.AbortTime = t;
                        run.Abort = ex.GetType().Name + ": " + ex.Message;
                        run.Final = y;
                        return run;
                    }

                    SpacecraftIntegrationState next = result.State;
                    y[0] = next.Position.X;
                    y[1] = next.Position.Y;
                    y[2] = next.Position.Z;
                    y[3] = next.Velocity.X;
                    y[4] = next.Velocity.Y;
                    y[5] = next.Velocity.Z;
                    if (p.Dim == 7)
                    {
                        y[6] = next.Mass;
                    }

                    t += dt;
                    if (!next.IsFinite)
                    {
                        run.Aborted = true;
                        run.AbortTime = t;
                        run.Abort = "the game stepper produced a non-finite state";
                        run.Final = y;
                        return run;
                    }

                    if (ctx.Abort != null)
                    {
                        run.Aborted = true;
                        run.AbortTime = t;
                        run.Abort = ctx.Abort;
                        run.Final = y;
                        return run;
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

            run.Steps = diag.Accepted;
            run.Rejected = diag.Rejected;
            run.Final = y;
            return run;
        }

        private static SpacecraftIntegrationState QualificationProductionState(double[] y, int dim)
        {
            return new SpacecraftIntegrationState
            {
                Position = new Vector3d(y[0], y[1], y[2]),
                Velocity = new Vector3d(y[3], y[4], y[5]),
                Mass = dim == 7 ? y[6] : 1d
            };
        }
    }

    private sealed class QualificationProdDop853 : QualificationProductionIntegrator
    {
        public QualificationProdDop853()
            : base(true)
        {
        }

        public override string Name
        {
            get { return "Prod-DOP853"; }
        }
    }

    private sealed class QualificationProdDopri5 : QualificationProductionIntegrator
    {
        public QualificationProdDopri5()
            : base(false)
        {
        }

        public override string Name
        {
            get { return "Prod-DOPRI5"; }
        }
    }

    /// <summary>
    /// C2 acceptance: both adapters must be wired to the GAME path, not to a private copy.
    /// For every K problem the adapter's final state is compared BITWISE with the result of the
    /// real game wrapper `SpacecraftPhysics.Step` (same tolerances, same 600 s cap, same
    /// SpacecraftOrbitIntegrator call). The A1 accuracy of each run is logged separately: it is
    /// a property of the game method at the game's tolerances, not of the adapter's wiring.
    /// </summary>
    private static bool QualificationProductionAdapterSelfCheck(TextWriter log)
    {
        // Keep the measured Kepler pins inside the qualification folder: the self-check runs
        // before QualificationRunCatalogue, and without this it would write the global
        // Study/integrators/kepler_weights.csv (outside qualification).
        QualificationPrepareKeplerPins();
        List<QualificationProblem> kepler = QualificationCatalogKepler();
        var adapters = new List<IStudyIntegrator>
        {
            new QualificationProdDop853(),
            new QualificationProdDopri5()
        };
        bool ok = true;
        log.WriteLine("C2 adapter self-check on K1-K8 (game tolerances, 600 s cap):");
        foreach (QualificationProblem q in kepler)
        {
            q.Status = "VALID";
            StudyProblem p = q.Problem;
            if (double.IsNaN(p.Floor))
            {
                p.Floor = 0d;
            }

            var options = new StudyOptions();
            options.RelTargets = new double[0];
            options.AbsTargets = new[] { 1e-6d * p.ScaleLength };
            p.MaxStepSeconds = OrbitIntegrator.DefaultMaxStepSize;
            options.AdaptiveMaxSteps = new[] { double.PositiveInfinity };
            options.AdaptiveControlMax = 1d;
            options.MaxCostRhs = 2000000L;
            options.RefineTies = false;
            options.TimingReps = 1;
            StudyScenarioResult result = StudySweepRunner.Run(p, adapters, options, log);
            foreach (IStudyIntegrator adapter in adapters)
            {
                StudyRow best = null;
                foreach (StudyRow row in result.Rows)
                {
                    if (row.Integrator != adapter.Name || row.Status != "ok" || !row.Finite)
                    {
                        continue;
                    }

                    if (best == null || row.Err < best.Err)
                    {
                        best = row;
                    }
                }

                bool finite = best != null;
                bool a1 = best != null && best.Err <= 1e-6d * p.ScaleLength;
                bool parity = finite && QualificationProductionParity(p, adapter, log);
                if (!finite || !parity)
                {
                    ok = false;
                }

                log.WriteLine("  " + q.Id + " " + adapter.Name + ": "
                    + (best == null ? "no finite run"
                        : "err=" + QualificationFormat(best.Err) + " m (" + (a1 ? "A1 PASS" : "A1 above")
                            + "), calls=" + best.Calls.ToString(QualificationInv) + ", steps="
                            + best.Steps.ToString(QualificationInv))
                    + ", game-wrapper parity=" + (parity ? "PASS" : "FAIL"));
            }
        }

        if (!ok)
        {
            log.WriteLine("C2 adapter self-check FAILED: an adapter did not match the game wrapper or produced no finite run");
        }

        return ok;
    }

    /// <summary>
    /// Runs the same K problem through the real game wrapper (SpacecraftPhysics.Step, which calls
    /// SpacecraftOrbitIntegrator) and through the adapter, then compares the final state bitwise.
    /// The source wrapper only translates StudyProblem.Rhs into a DynamicsContribution, so a
    /// mismatch means the adapter changed the numerics.
    /// </summary>
    private static bool QualificationProductionParity(
        StudyProblem p, IStudyIntegrator adapter, TextWriter log)
    {
        bool dop853 = adapter.Name == "Prod-DOP853";
        var physics = new SpacecraftPhysics();
        physics.Integrator = dop853 ? SpacecraftIntegrator.Dop853 : SpacecraftIntegrator.Dopri5;
        physics.Sources.Add(new QualificationProductionParitySource(p));
        var ship = new Spacecraft(
            new Vector3d(p.Y0[0], p.Y0[1], p.Y0[2]),
            new Vector3d(p.Y0[3], p.Y0[4], p.Y0[5]),
            p.Dim == 7 ? p.Y0[6] : 1d);

        StudyStops stops = StudyStops.Build(p);
        double t = p.T0;
        try
        {
            for (int k = 0; k < stops.Times.Length; k++)
            {
                double tStop = stops.Times[k];
                while (tStop - t > 1e-12d * Math.Max(1d, Math.Abs(tStop)))
                {
                    double dt = Math.Min(OrbitIntegrator.DefaultMaxStepSize, tStop - t);
                    physics.Step(ship, t, dt);
                    t += dt;
                }

                t = tStop;
            }
        }
        catch (Exception ex)
        {
            log.WriteLine("    parity: game wrapper threw " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }

        var ctx = new StudyContext(p, 2000000L);
        StudyRun run = adapter.Run(p, adapter.MakeConfig(p, 1d, OrbitIntegrator.DefaultMaxStepSize), ctx);
        if (run.Aborted || run.Final == null)
        {
            log.WriteLine("    parity: adapter aborted: " + run.Abort);
            return false;
        }

        bool same = run.Final[0] == ship.Position.X && run.Final[1] == ship.Position.Y
            && run.Final[2] == ship.Position.Z
            && run.Final[3] == ship.Velocity.X && run.Final[4] == ship.Velocity.Y
            && run.Final[5] == ship.Velocity.Z
            && (p.Dim != 7 || run.Final[6] == ship.Mass);
        if (!same)
        {
            double dp = 0d;
            double dv = 0d;
            for (int i = 0; i < 3; i++)
            {
                dp = Math.Max(dp, Math.Abs(run.Final[i] - (i == 0 ? ship.Position.X
                    : i == 1 ? ship.Position.Y : ship.Position.Z)));
                dv = Math.Max(dv, Math.Abs(run.Final[3 + i] - (i == 0 ? ship.Velocity.X
                    : i == 1 ? ship.Velocity.Y : ship.Velocity.Z)));
            }

            log.WriteLine("    parity: adapter vs SpacecraftPhysics dp=" + dp.ToString("E3", QualificationInv)
                + " m dv=" + dv.ToString("E3", QualificationInv) + " m/s");
        }

        return same;
    }

    /// <summary>Translates a StudyProblem RHS into the game's IDynamicsSource contract.</summary>
    private sealed class QualificationProductionParitySource : Galilego.Spacecraft.IDynamicsSource
    {
        private readonly StudyProblem problem;
        private readonly double[] y;
        private readonly double[] dy;

        public QualificationProductionParitySource(StudyProblem p)
        {
            problem = p;
            y = new double[p.Dim];
            dy = new double[p.Dim];
        }

        public Galilego.Spacecraft.DynamicsContribution Evaluate(
            Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            y[0] = position.X;
            y[1] = position.Y;
            y[2] = position.Z;
            y[3] = velocity.X;
            y[4] = velocity.Y;
            y[5] = velocity.Z;
            if (problem.Dim == 7)
            {
                y[6] = mass;
            }

            problem.Rhs(timeSeconds, y, dy);
            return new Galilego.Spacecraft.DynamicsContribution(
                new Vector3d(dy[3], dy[4], dy[5]),
                Vector3d.Zero,
                problem.Dim == 7 ? -dy[6] : 0d,
                Vector3d.Zero);
        }
    }
}
