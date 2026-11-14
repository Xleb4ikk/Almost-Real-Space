using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Galilego.Core;
using Galilego.Spacecraft;
using P1bStudy.Framework;

// TASK.md Appendix C, C4: the chunk axis. The arc is walked in sequential chunks of the given
// length, exactly as VesselStep calls the integrator once per chunk. For every method and every
// chunk length the stand records: error at the end of the arc against a tight reference, RHS
// calls, steps, stages, wall time per chunk (median of 5 sequential repetitions) and allocations.
//
// The chunk lengths are the game's own values (INVENTORY.md C-Limits):
//   0.05 s flight grain, 1/60 s live frame, 0.5 s surface/control tick,
//   30 s live-warp chunk, 600 s stress/long-warp chunk.
// The arc is 10 s for the frame-bound lengths (0.05 and 1/60) and 600 s for the rest: enough
// dynamics for a per-chunk measurement, bounded so the full 15-method catalogue stays feasible.
internal static partial class P1bTests
{
    private const string QualificationChunksHeader =
        "problem_id,method,control,chunk_s,arc_s,status,reason,err_m,rhs_calls,steps,stages,"
        + "wall_ms_per_chunk,alloc_bytes_per_chunk";

    private const string QualificationGameControlHeader =
        "problem_id,method,control,status,reason,err_max,rhs_calls,steps,rejected";

    private static readonly double[] QualificationChunkLengths =
        { 0.05d, 1d / 60d, 0.5d, 30d, 600d };

    private static readonly double[] QualificationChunkArcSeconds =
        { 10d, 10d, 600d, 600d, 600d };

    private static void QualificationRunChunkTest(
        List<QualificationProblem> catalog, List<IStudyIntegrator> methods, string dir, TextWriter log)
    {
        QualificationRunGameControlPass(catalog, methods, dir, log);
        string path = Path.Combine(dir, "chunks.csv");
        bool force = Environment.GetEnvironmentVariable("P1B564_RECHUNK") == "1";
        if (File.Exists(path) && !force)
        {
            log.WriteLine("chunks: reused " + path);
            return;
        }

        var rows = new List<string>();
        int ok = 0;
        int error = 0;
        log.WriteLine("chunk test: " + methods.Count.ToString(QualificationInv) + " method(s) × "
            + catalog.Count.ToString(QualificationInv) + " problem(s) × "
            + QualificationChunkLengths.Length.ToString(QualificationInv) + " chunk length(s)");
        foreach (QualificationProblem q in catalog)
        {
            if (q.Status != "VALID")
            {
                continue;
            }

            StudyProblem p = q.Problem;
            var references = new Dictionary<double, double[]>();
            var referenceErrors = new Dictionary<double, string>();
            foreach (IStudyIntegrator method in methods)
            {
                double control = QualificationChunkGameControl(method);
                for (int i = 0; i < QualificationChunkLengths.Length; i++)
                {
                    double chunk = QualificationChunkLengths[i];
                    double arc = Math.Min(QualificationChunkArcSeconds[i], p.Duration);
                    double[] reference = null;
                    string referenceError = "";
                    if (referenceErrors.TryGetValue(arc, out referenceError))
                    {
                        reference = null;
                    }
                    else if (!references.TryGetValue(arc, out reference))
                    {
                        try
                        {
                            reference = QualificationChunkReference(p, arc);
                            references[arc] = reference;
                        }
                        catch (Exception ex)
                        {
                            reference = null;
                            referenceError = "reference: " + ex.GetType().Name + ": " + ex.Message;
                            referenceErrors[arc] = referenceError;
                        }
                    }

                    QualificationChunkRow row;
                    if (reference == null)
                    {
                        row = new QualificationChunkRow();
                        row.Status = "error";
                        row.Reason = referenceError;
                    }
                    else
                    {
                        row = QualificationChunkCell(p, method, control, chunk, arc, reference);
                    }
                    if (row.Status == "ok")
                    {
                        ok++;
                    }
                    else
                    {
                        error++;
                    }

                    rows.Add(string.Join(",", new[]
                    {
                        QualificationCsvField(q.Id),
                        QualificationCsvField(method.Name),
                        QualificationCsvNumber(control),
                        QualificationCsvNumber(chunk),
                        QualificationCsvNumber(arc),
                        QualificationCsvField(row.Status),
                        QualificationCsvField(row.Reason),
                        QualificationCsvNumber(row.Err),
                        row.Calls.ToString(QualificationInv),
                        row.Steps.ToString(QualificationInv),
                        row.Stages.ToString(QualificationInv),
                        QualificationCsvNumber(row.WallMsPerChunk),
                        row.AllocPerChunk.ToString(QualificationInv)
                    }));
                }
            }

            QualificationWriteCsv(path, QualificationChunksHeader, rows);
        }

        log.WriteLine("chunks: " + ok.ToString(QualificationInv) + " ok, "
            + error.ToString(QualificationInv) + " error/aborted -> chunks.csv");
    }

    /// <summary>
    /// C6 "same tolerances" pass: one run per (method, problem) at the GAME control - fixed 600 s,
    /// adaptive rtol = SpacecraftPhysics.RelativeTolerance, Prod-* their single game point. This is
    /// separate from the goal sweep (which stops at the cheapest goal-reaching control): the error
    /// gate must compare the same tolerances, not the cheapest controls.
    /// </summary>
    private static void QualificationRunGameControlPass(
        List<QualificationProblem> catalog, List<IStudyIntegrator> methods, string dir, TextWriter log)
    {
        string path = Path.Combine(dir, "game_control.csv");
        bool force = Environment.GetEnvironmentVariable("P1B564_RECHECK") == "1";
        if (File.Exists(path) && !force)
        {
            log.WriteLine("game control: reused " + path);
            return;
        }

        var rows = new List<string>();
        int ok = 0;
        int bad = 0;
        foreach (QualificationProblem q in catalog)
        {
            if (q.Status != "VALID")
            {
                continue;
            }

            StudyProblem p = q.Problem;
            foreach (IStudyIntegrator method in methods)
            {
                double control = QualificationChunkGameControl(method);
                double maxStep = p.MaxStepSeconds > 0d ? p.MaxStepSeconds : double.PositiveInfinity;
                StudyConfig cfg = method.MakeConfig(p, control, maxStep);
                var ctx = new StudyContext(p, 10000000L);
                string status;
                string reason = "";
                double err = double.NaN;
                long steps = 0L;
                long rejected = 0L;
                try
                {
                    StudyRun run = method.Run(p, cfg, ctx);
                    steps = run.Steps;
                    rejected = run.Rejected;
                    if (run.Aborted || run.Final == null)
                    {
                        status = "aborted";
                        reason = run.Abort;
                    }
                    else
                    {
                        status = "ok";
                        err = QualificationGameControlError(p, run);
                    }
                }
                catch (StudyBudgetExceeded)
                {
                    status = "censored";
                    reason = "RHS budget 1e7";
                }
                catch (Exception ex)
                {
                    status = "error";
                    reason = ex.GetType().Name + ": " + ex.Message;
                }

                if (status == "ok" && double.IsFinite(err))
                {
                    ok++;
                }
                else
                {
                    bad++;
                }

                rows.Add(string.Join(",", new[]
                {
                    QualificationCsvField(q.Id),
                    QualificationCsvField(method.Name),
                    QualificationCsvNumber(control),
                    QualificationCsvField(status),
                    QualificationCsvField(reason),
                    QualificationCsvNumber(err),
                    ctx.Calls.ToString(QualificationInv),
                    steps.ToString(QualificationInv),
                    rejected.ToString(QualificationInv)
                }));
            }

            QualificationWriteCsv(path, QualificationGameControlHeader, rows);
        }

        log.WriteLine("game control: " + ok.ToString(QualificationInv) + " ok, "
            + bad.ToString(QualificationInv) + " aborted/error -> game_control.csv");
    }

    private static double QualificationGameControlError(StudyProblem p, StudyRun run)
    {
        double worst = 0d;
        bool any = false;
        for (int k = 0; k < run.Samples.Length; k++)
        {
            if (run.Samples[k] == null)
            {
                continue;
            }

            any = true;
            double e = p.Error(run.Samples[k], p.CheckpointRef[k]);
            if (!(e <= worst))
            {
                worst = e;
            }
        }

        return any ? worst : double.NaN;
    }

    /// <summary>
    /// The game's own control for this method (C6: compare at the same tolerances):
    ///   * Prod-*: one point, the game tolerances;
    ///   * fixed-step study methods: the 600 s step cap;
    ///   * adaptive study methods: the game relative tolerance (SpacecraftPhysics default).
    /// No goal-dependent "best control" is used here: that would compare different tolerances.
    /// </summary>
    private static double QualificationChunkGameControl(IStudyIntegrator method)
    {
        if (method.Name.StartsWith("Prod-", StringComparison.Ordinal))
        {
            return 1d;
        }

        if (method.Kind == StudyKind.Fixed)
        {
            return OrbitIntegrator.DefaultMaxStepSize;
        }

        return QualificationGameDefaults.Relative;
    }

    private sealed class QualificationChunkRow
    {
        public string Status = "ok";
        public string Reason = "";
        public double Err = double.NaN;
        public long Calls;
        public long Steps;
        public int Stages;
        public double WallMsPerChunk = double.NaN;
        public long AllocPerChunk;
    }

    private static QualificationChunkRow QualificationChunkCell(
        StudyProblem p, IStudyIntegrator method, double control, double chunk, double arc,
        double[] reference)
    {
        var row = new QualificationChunkRow();
        row.Stages = QualificationStagesOf(method.Name);
        int chunks = Math.Max(1, (int)Math.Ceiling(arc / chunk - 1e-9d));
        double dt = arc / chunks;
        double[] msReps = new double[5];
        long alloc = 0L;
        for (int rep = 0; rep < msReps.Length; rep++)
        {
            double[] y = (double[])p.Y0.Clone();
            double t = p.T0;
            long calls = 0L;
            long steps = 0L;
            long rejected = 0L;
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            try
            {
                for (int k = 0; k < chunks; k++)
                {
                    StudyProblem slice = p.ShallowClone();
                    slice.T0 = t;
                    slice.Y0 = y;
                    slice.CheckpointTimes = new[] { t + dt };
                    slice.CheckpointRef = new[] { y };
                    double maxStep = p.MaxStepSeconds > 0d
                        ? p.MaxStepSeconds
                        : double.PositiveInfinity;
                    StudyConfig cfg = method.MakeConfig(slice, control, maxStep);
                    if (method.Kind == StudyKind.Adaptive && cfg.H0 <= 0d)
                    {
                        // The game calls the stepper once per chunk with dt = chunk, not with a
                        // tiny 1e-3*CharTime guess. Start the study adaptive controller at the
                        // chunk length so the wall test measures the same per-chunk situation.
                        cfg.H0 = Math.Min(chunk, maxStep);
                    }

                    var ctx = new StudyContext(slice, 10000000L);
                    StudyRun run = method.Run(slice, cfg, ctx);
                    calls += ctx.Calls;
                    steps += run.Steps;
                    rejected += run.Rejected;
                    if (run.Aborted || run.Final == null)
                    {
                        throw new InvalidOperationException(run.Abort);
                    }

                    y = run.Final;
                    t += dt;
                }
            }
            catch (Exception ex)
            {
                row.Status = "aborted";
                row.Reason = ex.GetType().Name + ": " + ex.Message;
                return row;
            }

            sw.Stop();
            msReps[rep] = sw.Elapsed.TotalMilliseconds;
            alloc = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            if (rep == 0)
            {
                row.Calls = calls;
                row.Steps = steps;
                double dp = 0d;
                for (int i = 0; i < 3; i++)
                {
                    dp = Math.Max(dp, Math.Abs(y[i] - reference[i]));
                }

                row.Err = dp;
            }
        }

        Array.Sort(msReps);
        row.WallMsPerChunk = msReps[msReps.Length / 2] / chunks;
        row.AllocPerChunk = alloc / chunks;
        return row;
    }

    /// <summary>
    /// Tight reference at the end of a short arc: the game DOP853 at rtol 1e-14 over the same RHS,
    /// with 64 sub-chunks. It is a self-convergence reference for the chunk test only (never a G8
    /// oracle, never a PASS/FAIL gate); the production cells use the task's certified reference.
    /// </summary>
    private static double[] QualificationChunkReference(StudyProblem p, double arc)
    {
        double[] y = (double[])p.Y0.Clone();
        double t = p.T0;
        int chunks = 64;
        double dt = arc / chunks;
        for (int k = 0; k < chunks; k++)
        {
            y = QualificationChunkGameStep(y, p, t, dt);
            t += dt;
        }

        return y;
    }

    private static double[] QualificationChunkGameStep(
        double[] y, StudyProblem p, double t, double dt)
    {
        double[] yBuf = new double[p.Dim];
        double[] dyBuf = new double[p.Dim];
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
                p.Rhs(time, yBuf, dyBuf);
                return new SpacecraftIntegrationState
                {
                    Position = new Vector3d(dyBuf[0], dyBuf[1], dyBuf[2]),
                    Velocity = new Vector3d(dyBuf[3], dyBuf[4], dyBuf[5]),
                    Mass = p.Dim == 7 ? dyBuf[6] : 0d
                };
            };

        var from = new SpacecraftIntegrationState
        {
            Position = new Vector3d(y[0], y[1], y[2]),
            Velocity = new Vector3d(y[3], y[4], y[5]),
            Mass = p.Dim == 7 ? y[6] : 1d
        };
        SpacecraftIntegrationResult result = SpacecraftOrbitIntegrator.StepForwardDop853(
            from, t, dt, derivative,
            1e-9d, 1e-12d, 1e-9d, 1e-14d, null, null, null, dt);
        double[] next = (double[])y.Clone();
        next[0] = result.State.Position.X;
        next[1] = result.State.Position.Y;
        next[2] = result.State.Position.Z;
        next[3] = result.State.Velocity.X;
        next[4] = result.State.Velocity.Y;
        next[5] = result.State.Velocity.Z;
        if (p.Dim == 7)
        {
            next[6] = result.State.Mass;
        }

        return next;
    }

    /// <summary>Nominal stages of the game adapters (StudyReport.StagesOf does not know them).</summary>
    private static int QualificationStagesOf(string method)
    {
        if (method == "Prod-DOP853")
        {
            return 15;
        }

        if (method == "Prod-DOPRI5")
        {
            return 7;
        }

        return StudyReport.StagesOf(method);
    }
}
