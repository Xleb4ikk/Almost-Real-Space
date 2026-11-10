using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

// Test560 measurement engine. One DecisionMethod is either the real production
// SpacecraftPhysics path or a StudyRegistry method driven through a chunk-restart
// wrapper. Both see the same StarSystem, the same initial state, the same force
// sources and the same chunk boundaries.
internal static partial class P1bTests
{
    private const long DecisionRunBudgetRhs = 60000000L;

    private sealed class DecisionMethod
    {
        public string Name;
        public string Class;      // production | stand-only
        public string Kernel;     // dop853 | dopri5 | wh-bm14a | rkn12 | ias15
        public bool Production;
        public IStudyIntegrator Stand;
    }

    private sealed class DecisionRun
    {
        public double Control;
        public string ControlKind; // rtol | h
        public string Status = "ok"; // ok | error | censored | unsupported
        public string Note = "";
        public double ErrMaxM = double.NaN;
        public double ErrFinalM = double.NaN;
        public double ErrMaxV = double.NaN;
        public long Calls;
        public long Aux;
        public long Steps;
        public long Accepted;
        public long Rejected;
        public long Chunks;
        public long RawCalls;
        public double CostRhs;
        public double CostLocalFrame;
        public double Ms;
        public long GcBytes;
        public int Reps = 1;
        public double MsMedian;
        public double MsSpreadPct;
        public bool Finite;
        public Vector3d[] SamplesP;
        public Vector3d[] SamplesV;
    }

    private sealed class DecisionMethodResult
    {
        public DecisionScenario Scenario;
        public DecisionMethod Method;
        public string UnsupportedReason = "";
        public List<DecisionRun> Runs = new List<DecisionRun>();
        public DecisionRun BestHit; // cheapest executed hit for the tightest target
        public double TightestTarget;
        public string Status = "ok";
        public string Note = "";
    }

    private static List<DecisionMethod> DecisionMethods()
    {
        return new List<DecisionMethod>
        {
            new DecisionMethod { Name = "DOP853", Class = "production", Kernel = "dop853", Production = true },
            new DecisionMethod { Name = "DOPRI5", Class = "production", Kernel = "dopri5", Production = true },
            new DecisionMethod { Name = "WH-BM14A", Class = "stand-only", Kernel = "wh-bm14a", Stand = DecisionStand("WH-BM14A") },
            new DecisionMethod { Name = "RKN12(10)", Class = "stand-only", Kernel = "rkn12", Stand = DecisionStand("RKN12(10)") },
            new DecisionMethod { Name = "IAS15", Class = "stand-only", Kernel = "ias15", Stand = DecisionStand("IAS15") }
        };
    }

    private static IStudyIntegrator DecisionStand(string name)
    {
        List<IStudyIntegrator> all = StudyRegistry.Select(name);
        return all.Count == 1 ? all[0] : null;
    }

    private static string DecisionEnv(string name)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static bool DecisionQuick()
    {
        return string.Equals(DecisionEnv("P1B560_QUICK"), "1", StringComparison.Ordinal);
    }

    private static double[] DecisionGrid(DecisionScenario s, DecisionMethod m)
    {
        if (m.Production)
        {
            double[] full = { 1e-6d, 1e-8d, 1e-10d, 1e-11d, 1e-12d, 1e-13d };
            return DecisionQuick() ? new[] { 1e-6d, 1e-9d, 1e-12d } : full;
        }

        if (m.Stand.Kind == StudyKind.Fixed)
        {
            double coarse;
            double fine;
            double ratio;
            m.Stand.ControlRange(s.Problem, out coarse, out fine, out ratio);
            if (coarse > s.ChunkSeconds)
            {
                coarse = s.ChunkSeconds;
            }

            double floor = Math.Max(fine, s.ChunkSeconds / 1024d);
            if (!(floor < coarse))
            {
                floor = coarse / 1024d;
            }

            int count = DecisionQuick() ? 4 : 7;
            var grid = new double[count];
            double factor = Math.Pow(coarse / floor, 1d / (count - 1));
            for (int i = 0; i < count; i++)
            {
                grid[i] = coarse / Math.Pow(factor, i);
            }

            return grid;
        }

        double[] adaptive = { 1e-4d, 1e-6d, 1e-8d, 1e-10d, 1e-12d };
        return DecisionQuick() ? new[] { 1e-4d, 1e-8d, 1e-12d } : adaptive;
    }

    private static string DecisionControlKind(DecisionMethod m)
    {
        if (m.Production)
        {
            return "rtol";
        }

        return m.Stand.Kind == StudyKind.Fixed ? "h" : "rtol";
    }

    private static bool DecisionSupports(DecisionScenario s, DecisionMethod m, out string reason)
    {
        if (m.Production)
        {
            reason = "";
            return true;
        }

        if (m.Stand == null)
        {
            reason = "method not present in StudyRegistry";
            return false;
        }

        return m.Stand.Supports(s.Problem, out reason);
    }

    // ------------------------------------------------------------------------------------
    // Production path
    // ------------------------------------------------------------------------------------

    private static DecisionRun DecisionRunProduction(
        DecisionScenario s, string kernel, double rtol, bool dense,
        double posTol = 1e-4d, double velTol = 1e-8d, double massTol = 1e-4d,
        bool splitAtCheckpoints = false)
    {
        var run = new DecisionRun
        {
            Control = rtol,
            ControlKind = "rtol",
            SamplesP = new Vector3d[s.Refs.CheckpointTimes.Length],
            SamplesV = new Vector3d[s.Refs.CheckpointTimes.Length],
            Chunks = s.ChunkCount
        };

        try
        {
            StudyShipMatrixCase c = s.Case;
            StarSystem sys = c.Sys;
            OrbitingBody central = sys.AllBodies[c.Central];
            sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);

            var counting = new CountingSource(new CachedGravitySource(sys));
            var phys = new SpacecraftPhysics
            {
                Integrator = kernel == "dopri5" ? SpacecraftIntegrator.Dopri5 : SpacecraftIntegrator.Dop853,
                ToleranceFrameSystem = sys,
                PositionAbsoluteTolerance = posTol,
                VelocityAbsoluteTolerance = velTol,
                MassAbsoluteTolerance = massTol,
                RelativeTolerance = rtol
            };
            phys.Sources.Add(counting);
            if (c.Drag != null)
            {
                phys.Sources.Add(c.Drag);
            }

            if (c.Thrust)
            {
                phys.Sources.Add(new ThrustSource(c.ThrustNewtons, 0d, new ConstantIsp(c.ThrustIsp))
                {
                    ThrottleAt = t => StudyShipMatrixThrottle(c, t),
                    ThrustDirection = c.ThrustDirection
                });
            }

            var ship = new Spacecraft(bp + c.P0, bv + c.V0, c.Mass0);
            var segments = dense ? new List<DenseSegment>() : null;
            long gc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            double t = StudyShipT0;
            double end = t + c.Seconds;
            int sample = 0;
            long chunksDone = 0L;
            long accepted = 0L;
            long rejected = 0L;
            long attempts = 0L;

            while (t < end - 1e-12d * Math.Max(1d, Math.Abs(end)))
            {
                double chunk = Math.Min(s.ChunkSeconds, end - t);
                chunk = DecisionClampToBreakpoint(s, t, chunk);
                if (splitAtCheckpoints && sample < s.Refs.CheckpointTimes.Length)
                {
                    double ck = s.Refs.CheckpointTimes[sample];
                    if (ck > t + 1e-9d && ck < t + chunk - 1e-9d)
                    {
                        chunk = ck - t;
                    }
                }

                phys.StepWithSegments(ship, t, chunk, segments);
                accepted += phys.StepDiagnostics.Accepted;
                rejected += phys.StepDiagnostics.Rejected;
                attempts += phys.StepDiagnostics.Attempts;
                chunksDone++;
                t += chunk;

                if (counting.Calls > DecisionRunBudgetRhs)
                {
                    run.Status = "censored";
                    run.Note = "RHS budget " + DecisionRunBudgetRhs.ToString(CultureInfo.InvariantCulture) + " exceeded";
                    break;
                }

                while (sample < s.Refs.CheckpointTimes.Length
                    && s.Refs.CheckpointTimes[sample] <= t + 1e-6d)
                {
                    DecisionRecordProductionSample(s, sys, central, ship, s.Refs.CheckpointTimes[sample], run, sample);
                    sample++;
                }
            }

            sw.Stop();
            run.GcBytes = GC.GetAllocatedBytesForCurrentThread() - gc0;
            run.Ms = sw.Elapsed.TotalMilliseconds;
            run.RawCalls = counting.Calls;
            run.Calls = counting.Calls;
            run.Steps = accepted;
            run.Accepted = accepted;
            run.Rejected = rejected;
            run.Chunks = chunksDone;
            // One derivative call per chunk is the torque snapshot; it is not a
            // local-frame RHS (it evaluates the absolute gravity once). The rest of
            // the calls go through EvaluateLocalDerivative, which costs three
            // StarSystem acceleration sums per call.
            long localCalls = Math.Max(0L, counting.Calls - chunksDone);
            run.CostRhs = counting.Calls;
            run.CostLocalFrame = (3d * localCalls) + chunksDone;
            run.Finite = sample == s.Refs.CheckpointTimes.Length && DecisionAllFinite(run.SamplesP, run.SamplesV);
            DecisionEvaluateErrors(s, run);
            if (run.Status == "ok" && !run.Finite)
            {
                run.Status = "error";
                run.Note = "non-finite or missing checkpoint sample";
            }
        }
        catch (Exception ex)
        {
            run.Status = "error";
            run.Note = ex.GetType().Name + ": " + ex.Message;
            run.Finite = false;
        }

        return run;
    }

    /// <summary>Stop exactly on a thrust/ramp force breakpoint inside the chunk.</summary>
    private static double DecisionClampToBreakpoint(DecisionScenario s, double t, double chunk)
    {
        if (!s.Case.Thrust)
        {
            return chunk;
        }

        double end = t + chunk;
        double best = end;
        foreach (double b in StudyShipRefBreakpoints(s.Case))
        {
            if (b > t + 1e-9d && b < best - 1e-9d)
            {
                best = b;
            }
        }

        return best - t;
    }

    private static void DecisionRecordProductionSample(
        DecisionScenario s, StarSystem sys, OrbitingBody central, Spacecraft ship,
        double time, DecisionRun run, int sample)
    {
        sys.EvaluateBodyState(central, time, out Vector3d bp, out Vector3d bv);
        bool localToCentral = ship.HasLocalState && ReferenceEquals(ship.LocalBody, central);
        run.SamplesP[sample] = localToCentral ? ship.LocalPosition : ship.Position - bp;
        run.SamplesV[sample] = localToCentral ? ship.LocalVelocity : ship.Velocity - bv;
    }

    private static bool DecisionAllFinite(Vector3d[] p, Vector3d[] v)
    {
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i].IsFinite == false || v[i].IsFinite == false)
            {
                return false;
            }
        }

        return true;
    }

    private static void DecisionEvaluateErrors(DecisionScenario s, DecisionRun run)
    {
        if (!run.Finite || run.SamplesP == null)
        {
            return;
        }

        double worstP = 0d;
        double worstV = 0d;
        double lastP = 0d;
        for (int i = 0; i < s.Refs.CheckpointTimes.Length; i++)
        {
            double dp = (run.SamplesP[i] - s.Refs.CheckpointP[i]).Magnitude;
            double dv = (run.SamplesV[i] - s.Refs.CheckpointV[i]).Magnitude;
            worstP = Math.Max(worstP, dp);
            worstV = Math.Max(worstV, dv);
            lastP = dp;
        }

        run.ErrMaxM = worstP;
        run.ErrFinalM = lastP;
        run.ErrMaxV = worstV;

        if (string.Equals(DecisionEnv("P1B560_DIAG"), "1", StringComparison.Ordinal)
            && s.Label.IndexOf("LEO 300", StringComparison.Ordinal) >= 0)
        {
            for (int i = 0; i < s.Refs.CheckpointTimes.Length; i++)
            {
                Console.WriteLine("      diag " + s.Label + " ck " + i.ToString(CultureInfo.InvariantCulture)
                    + " e=" + (run.SamplesP[i] - s.Refs.CheckpointP[i]).Magnitude.ToString("E3", CultureInfo.InvariantCulture));
            }
        }
    }

    // ------------------------------------------------------------------------------------
    // Stand path with a fresh controller at every chunk boundary
    // ------------------------------------------------------------------------------------

    private sealed class DecisionChunkedIntegrator : IStudyIntegrator
    {
        private readonly IStudyIntegrator inner;
        private readonly double chunkSeconds;
        private readonly double maxStepCap;

        public DecisionChunkedIntegrator(IStudyIntegrator inner, double chunkSeconds, double maxStepCap)
        {
            this.inner = inner;
            this.chunkSeconds = chunkSeconds;
            this.maxStepCap = maxStepCap;
        }

        public string Name { get { return inner.Name; } }
        public string Family { get { return inner.Family; } }
        public StudyKind Kind { get { return inner.Kind; } }
        public double ExpectedOrder { get { return inner.ExpectedOrder; } }

        public bool Supports(StudyProblem p, out string reason)
        {
            return inner.Supports(p, out reason);
        }

        public void ControlRange(StudyProblem p, out double coarse, out double fine, out double ratio)
        {
            inner.ControlRange(p, out coarse, out fine, out ratio);
            if (Kind == StudyKind.Fixed)
            {
                if (coarse > chunkSeconds)
                {
                    coarse = chunkSeconds;
                }

                double floor = Math.Max(fine, chunkSeconds / 1024d);
                fine = floor < coarse ? floor : coarse / 1024d;
            }
        }

        public StudyConfig MakeConfig(StudyProblem p, double control, double maxStep)
        {
            return inner.MakeConfig(p, control, maxStep);
        }

        public StudyRun Run(StudyProblem p, StudyConfig cfg, StudyContext ctx)
        {
            var result = new StudyRun();
            result.Samples = new double[p.CheckpointTimes.Length][];
            double[] y = (double[])p.Y0.Clone();
            double t = p.T0;
            double end = p.EndTime;
            int ck = 0;

            while (t < end - 1e-12d * Math.Max(1d, Math.Abs(end)))
            {
                while (ck < p.CheckpointTimes.Length && p.CheckpointTimes[ck] <= t + 1e-6d)
                {
                    ck++;
                }

                double chunkEnd = Math.Min(t + chunkSeconds, end);
                if (ck < p.CheckpointTimes.Length && p.CheckpointTimes[ck] < chunkEnd - 1e-6d)
                {
                    chunkEnd = p.CheckpointTimes[ck];
                }

                StudyProblem sub = p.ShallowClone();
                sub.T0 = t;
                sub.Y0 = y;
                sub.CheckpointTimes = new[] { chunkEnd };
                sub.CheckpointRef = new[] { p.CheckpointRef[Math.Min(ck, p.CheckpointRef.Length - 1)] };
                if (p.Breakpoints != null)
                {
                    var kept = new List<double>();
                    foreach (double b in p.Breakpoints)
                    {
                        if (b > t + 1e-9d && b < chunkEnd - 1e-9d)
                        {
                            kept.Add(b);
                        }
                    }

                    sub.Breakpoints = kept.Count == 0 ? null : kept.ToArray();
                }

                StudyConfig subCfg = inner.MakeConfig(p, cfg.Control, double.PositiveInfinity);
                subCfg.MaxStep = maxStepCap;
                double span = chunkEnd - t;
                subCfg.H0 = double.IsPositiveInfinity(maxStepCap) ? span : Math.Min(span, maxStepCap);
                if (!(subCfg.H0 > 0d))
                {
                    subCfg.H0 = span;
                }

                StudyRun part = inner.Run(sub, subCfg, ctx);
                result.Steps += part.Steps;
                result.Rejected += part.Rejected;
                result.CapLimited += part.CapLimited;
                if (part.Aborted)
                {
                    result.Aborted = true;
                    result.AbortTime = part.AbortTime;
                    result.Abort = part.Abort;
                    result.Final = part.Final;
                    return result;
                }

                y = part.Final;
                t = chunkEnd;
                if (ck < p.CheckpointTimes.Length && Math.Abs(p.CheckpointTimes[ck] - t) <= 1e-6d)
                {
                    result.Samples[ck] = (double[])y.Clone();
                    ck++;
                }
            }

            result.Final = y;
            return result;
        }
    }

    private static DecisionRun DecisionRunStand(
        DecisionScenario s, DecisionMethod m, double control, bool parityMax600)
    {
        var run = new DecisionRun
        {
            Control = control,
            ControlKind = DecisionControlKind(m),
            SamplesP = new Vector3d[s.Refs.CheckpointTimes.Length],
            SamplesV = new Vector3d[s.Refs.CheckpointTimes.Length],
            Chunks = s.ChunkCount
        };

        try
        {
            var chunked = new DecisionChunkedIntegrator(
                m.Stand, s.ChunkSeconds, parityMax600 ? 600d : double.PositiveInfinity);
            StudyConfig cfg = chunked.MakeConfig(s.Problem, control, double.PositiveInfinity);
            var ctx = new StudyContext(s.Problem, DecisionRunBudgetRhs);
            long gc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            StudyRun result = chunked.Run(s.Problem, cfg, ctx);
            sw.Stop();
            run.GcBytes = GC.GetAllocatedBytesForCurrentThread() - gc0;
            run.Ms = sw.Elapsed.TotalMilliseconds;
            run.RawCalls = ctx.Calls;
            run.Aux = ctx.AuxOps;
            run.Calls = ctx.Calls;
            run.CostRhs = ctx.Calls + (s.KeplerWeight * ctx.AuxOps);
            run.CostLocalFrame = run.CostRhs; // stand formulation: one gravity sum per RHS
            run.Steps = result.Steps;
            run.Accepted = result.Steps;
            run.Rejected = result.Rejected;
            run.Finite = true;
            for (int i = 0; i < s.Refs.CheckpointTimes.Length; i++)
            {
                double[] sample = result.Samples[i];
                if (sample == null || sample.Length < 6)
                {
                    run.Finite = false;
                    break;
                }

                run.SamplesP[i] = new Vector3d(sample[0], sample[1], sample[2]);
                run.SamplesV[i] = new Vector3d(sample[3], sample[4], sample[5]);
                for (int k = 0; k < sample.Length; k++)
                {
                    if (double.IsNaN(sample[k]) || double.IsInfinity(sample[k]))
                    {
                        run.Finite = false;
                        break;
                    }
                }

                if (!run.Finite)
                {
                    break;
                }
            }

            if (result.Aborted)
            {
                run.Status = "error";
                run.Note = "aborted at t=" + result.AbortTime.ToString("E3", CultureInfo.InvariantCulture)
                    + " s: " + result.Abort;
                run.Finite = false;
            }
            else if (!run.Finite)
            {
                run.Status = "error";
                run.Note = "non-finite or missing checkpoint sample";
            }
            else
            {
                DecisionEvaluateErrors(s, run);
            }
        }
        catch (StudyBudgetExceeded ex)
        {
            run.Status = "censored";
            run.Note = ex.Message;
            run.Finite = false;
        }
        catch (Exception ex)
        {
            run.Status = "error";
            run.Note = ex.GetType().Name + ": " + ex.Message;
            run.Finite = false;
        }

        return run;
    }

    private static DecisionRun DecisionRunPoint(
        DecisionScenario s, DecisionMethod m, double control,
        Dictionary<string, DecisionRun> cache)
    {
        string key = s.Label + "|" + m.Name + "|" + control.ToString("R", CultureInfo.InvariantCulture);
        DecisionRun cached;
        if (cache.TryGetValue(key, out cached))
        {
            return cached;
        }

        DecisionRun run = m.Production
            ? DecisionRunProduction(s, m.Kernel, control, false, 1e-4d, 1e-8d, 1e-4d, true)
            : DecisionRunStand(s, m, control, false);
        cache[key] = run;
        Console.WriteLine("    " + s.Label + " / " + m.Name + " / "
            + run.ControlKind + "=" + control.ToString("G6", CultureInfo.InvariantCulture)
            + " -> " + (run.Finite ? run.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : run.Status)
            + " m, cost " + run.CostRhs.ToString("N0", CultureInfo.InvariantCulture)
            + ", " + run.Ms.ToString("F1", CultureInfo.InvariantCulture) + " ms");
        return run;
    }

    // ------------------------------------------------------------------------------------
    // Sweep: real executed points only; the cheapest executed hit wins.
    // ------------------------------------------------------------------------------------

    private static List<DecisionMethodResult> DecisionSweepScenario(
        DecisionScenario s, List<DecisionMethod> methods)
    {
        var results = new List<DecisionMethodResult>();
        var cache = new Dictionary<string, DecisionRun>(StringComparer.Ordinal);

        foreach (DecisionMethod m in methods)
        {
            var mr = new DecisionMethodResult { Scenario = s, Method = m };
            results.Add(mr);
            string reason;
            if (!DecisionSupports(s, m, out reason))
            {
                mr.Status = "unsupported";
                mr.UnsupportedReason = reason;
                Console.WriteLine("  " + s.Label + " / " + m.Name + ": n/a (" + reason + ")");
                continue;
            }

            double[] grid = DecisionGrid(s, m);
            double loosestTarget = double.MinValue;
            double tightestTarget = double.MaxValue;
            foreach (DecisionTarget t in s.Targets)
            {
                loosestTarget = Math.Max(loosestTarget, t.Value);
                tightestTarget = Math.Min(tightestTarget, t.Value);
            }

            var runs = new List<DecisionRun>();
            mr.Runs = runs;
            foreach (double control in grid)
            {
                DecisionRun run = DecisionRunPoint(s, m, control, cache);
                runs.Add(run);
                if (run.Status == "censored")
                {
                    break;
                }

                // No need to go finer once the tightest target is comfortably met:
                // a tighter control can only cost more. The coarse end is the real
                // floor for fixed-step methods; adaptive methods are loosened below.
                if (run.Status == "ok" && run.Finite && run.ErrMaxM <= 0.25d * tightestTarget)
                {
                    break;
                }
            }

            // A hit on the coarsest grid point is not a winner: try looser controls
            // until the error exceeds the loosest target. Only executed runs are added.
            double controlLoosest = grid[0];
            int extend = 0;
            while ((m.Production || m.Stand.Kind == StudyKind.Adaptive)
                && extend++ < 3 && DecisionCheapestHit(runs, loosestTarget) != null)
            {
                double looser = controlLoosest * 10d;
                if (!(looser < 1d))
                {
                    break;
                }

                DecisionRun run = DecisionRunPoint(s, m, looser, cache);
                runs.Add(run);
                controlLoosest = looser;
                if (run.Status != "ok" || !run.Finite)
                {
                    break;
                }
            }

            // Bisect the winning bracket for each target; at most 3 extra runs per target.
            double tightest = double.MaxValue;
            string tightestKind = "position";
            foreach (DecisionTarget t in s.Targets)
            {
                if (t.Value < tightest)
                {
                    tightest = t.Value;
                    tightestKind = t.Kind;
                }

                DecisionRefineTarget(s, m, t.Value, t.Kind, cache, runs);
            }

            mr.TightestTarget = tightest;
            mr.BestHit = DecisionCheapestHit(runs, tightest, tightestKind);
            if (mr.BestHit == null)
            {
                mr.Status = "no_hit";
                mr.Note = "no executed run reached " + tightest.ToString("G6", CultureInfo.InvariantCulture)
                    + " at the tested controls";
            }
        }

        return results;
    }

    private static double DecisionRunError(DecisionRun r, string kind)
    {
        return kind == "velocity" ? r.ErrMaxV : r.ErrMaxM;
    }

    private static DecisionRun DecisionCheapestHit(List<DecisionRun> runs, double target)
    {
        return DecisionCheapestHit(runs, target, "position");
    }

    private static DecisionRun DecisionCheapestHit(List<DecisionRun> runs, double target, string kind)
    {
        DecisionRun best = null;
        double bestCost = double.PositiveInfinity;
        foreach (DecisionRun r in runs)
        {
            if (r.Status != "ok" || !r.Finite || !(DecisionRunError(r, kind) <= target))
            {
                continue;
            }

            if (r.CostRhs < bestCost)
            {
                bestCost = r.CostRhs;
                best = r;
            }
        }

        return best;
    }

    private static DecisionRun DecisionCheapestMiss(
        List<DecisionRun> runs, double target, string kind, double belowCost)
    {
        DecisionRun best = null;
        double bestCost = double.NegativeInfinity;
        foreach (DecisionRun r in runs)
        {
            if (r.Status != "ok" || !r.Finite || !(DecisionRunError(r, kind) > target))
            {
                continue;
            }

            if (r.CostRhs < belowCost && r.CostRhs > bestCost)
            {
                bestCost = r.CostRhs;
                best = r;
            }
        }

        return best;
    }

    private static void DecisionRefineTarget(
        DecisionScenario s, DecisionMethod m, double target, string kind,
        Dictionary<string, DecisionRun> cache, List<DecisionRun> runs)
    {
        for (int i = 0; i < 3; i++)
        {
            DecisionRun hit = DecisionCheapestHit(runs, target, kind);
            if (hit == null)
            {
                return;
            }

            DecisionRun miss = DecisionCheapestMiss(runs, target, kind, hit.CostRhs);
            if (miss == null)
            {
                return;
            }

            if (hit.CostRhs <= 1.10d * miss.CostRhs)
            {
                return;
            }

            double control = Math.Sqrt(hit.Control * miss.Control);
            double lo = Math.Min(hit.Control, miss.Control);
            double hi = Math.Max(hit.Control, miss.Control);
            if (!(control > lo) || !(control < hi))
            {
                return;
            }

            bool exists = false;
            foreach (DecisionRun r in runs)
            {
                if (Math.Abs(r.Control - control) <= 1e-9d * Math.Max(Math.Abs(control), 1e-300d))
                {
                    exists = true;
                    break;
                }
            }

            if (exists)
            {
                return;
            }

            DecisionRun added = DecisionRunPoint(s, m, control, cache);
            runs.Add(added);
            if (added.Status != "ok" || !added.Finite)
            {
                return;
            }
        }
    }

    // ------------------------------------------------------------------------------------
    // Timing repeats for the winning executed run
    // ------------------------------------------------------------------------------------

    private static void DecisionTimeHit(DecisionScenario s, DecisionMethod m, DecisionRun hit)
    {
        if (hit == null || hit.Status != "ok")
        {
            return;
        }

        var times = new List<double>();
        var costs = new List<double>();
        int reps = hit.Ms < 2000d ? 2 : 1;
        for (int i = 0; i < reps; i++)
        {
            DecisionRun again = m.Production
                ? DecisionRunProduction(s, m.Kernel, hit.Control, false, 1e-4d, 1e-8d, 1e-4d, true)
                : DecisionRunStand(s, m, hit.Control, false);
            if (again.Status != "ok" || !again.Finite)
            {
                return;
            }

            times.Add(again.Ms);
            costs.Add(again.CostRhs);
            if (Math.Abs(again.ErrMaxM - hit.ErrMaxM) > 1e-9d * Math.Max(1d, hit.ErrMaxM))
            {
                hit.Note = (hit.Note + "; non-deterministic error").Trim();
            }
        }

        times.Sort();
        double median = times[times.Count / 2];
        hit.MsMedian = median;
        hit.MsSpreadPct = times.Count > 1
            ? 100d * (times[times.Count - 1] - times[0]) / Math.Max(median, 1e-9d)
            : 0d;
        hit.Reps = 1 + reps;
        if (costs.Count > 1 && costs[0] != hit.CostRhs)
        {
            hit.Note = (hit.Note + "; cost changed between repeats").Trim();
        }
    }

    // ------------------------------------------------------------------------------------
    // Determinism, parity, chunk invariance
    // ------------------------------------------------------------------------------------

    private sealed class DecisionValidityRow
    {
        public string Check;
        public string Scenario;
        public string Method;
        public string Metric;
        public double Value;
        public double Threshold;
        public string Status;
        public string Note;
    }

    private static List<DecisionValidityRow> DecisionValidityChecks(
        List<DecisionScenario> scenarios, List<DecisionMethodResult> results,
        List<DecisionMethod> methods)
    {
        var rows = new List<DecisionValidityRow>();

        // Production default must be untouched.
        var probe = new SpacecraftPhysics();
        rows.Add(new DecisionValidityRow
        {
            Check = "production_default",
            Scenario = "-",
            Method = "-",
            Metric = "default_integrator_is_dop853",
            Value = probe.Integrator == SpacecraftIntegrator.Dop853 ? 1d : 0d,
            Threshold = 1d,
            Status = probe.Integrator == SpacecraftIntegrator.Dop853 ? "PASS" : "FAIL",
            Note = probe.Integrator.ToString()
        });

        // Determinism: rerun the cheapest hit of two scenarios exactly.
        DecisionScenario determinismScenario = scenarios.Find(x => x.Label.IndexOf("LEO 200 km", StringComparison.Ordinal) >= 0);
        if (determinismScenario != null)
        {
            DecisionMethodResult mr = results.Find(x => x.Scenario == determinismScenario && x.Method.Name == "DOP853");
            DecisionRun hit = mr != null ? mr.BestHit : null;
            if (hit != null)
            {
                DecisionRun again = DecisionRunProduction(determinismScenario, "dop853", hit.Control, false, 1e-4d, 1e-8d, 1e-4d, true);
                double dErr = Math.Abs(again.ErrMaxM - hit.ErrMaxM);
                double dCost = Math.Abs(again.CostRhs - hit.CostRhs);
                rows.Add(new DecisionValidityRow
                {
                    Check = "determinism",
                    Scenario = determinismScenario.Label,
                    Method = "DOP853",
                    Metric = "delta_err_max_m",
                    Value = dErr,
                    Threshold = 0d,
                    Status = dErr == 0d && dCost == 0d ? "PASS" : "FAIL",
                    Note = "cost delta " + dCost.ToString(CultureInfo.InvariantCulture)
                });
            }

        }

        // A stand-only fixed-step method must be deterministic too. LEO is
        // drag/thrust, where WH is n/a, so use the high-orbit pure-gravity case.
        DecisionScenario high = scenarios.Find(x => x.Label.IndexOf("GEO", StringComparison.Ordinal) >= 0);
        if (high != null)
        {
            DecisionMethodResult wr = results.Find(x => x.Scenario == high && x.Method.Name == "WH-BM14A");
            if (wr != null && wr.BestHit != null)
            {
                DecisionRun again = DecisionRunStand(high, wr.Method, wr.BestHit.Control, false);
                double dErr = Math.Abs(again.ErrMaxM - wr.BestHit.ErrMaxM);
                double dCost = Math.Abs(again.CostRhs - wr.BestHit.CostRhs);
                rows.Add(new DecisionValidityRow
                {
                    Check = "determinism",
                    Scenario = high.Label,
                    Method = "WH-BM14A",
                    Metric = "delta_err_max_m",
                    Value = dErr,
                    Threshold = 0d,
                    Status = dErr == 0d && dCost == 0d ? "PASS" : "FAIL",
                    Note = "cost delta " + dCost.ToString(CultureInfo.InvariantCulture)
                });
            }
        }

        // Production parity: one surface and one LEO scenario, production DOP853
        // vs the stand DOP853 at the same tight tolerance.
        foreach (string marker in new[] { "Surface ascent", "LEO 200 km" })
        {
            DecisionScenario s = scenarios.Find(x => x.Label.IndexOf(marker, StringComparison.Ordinal) >= 0);
            if (s == null)
            {
                continue;
            }

            DecisionMethod stand = methods.Find(x => x.Name == "DOP853" && !x.Production);
            // DOP853 production/stand parity uses the Registry method, not a candidate row.
            IStudyIntegrator dop853 = DecisionStand("DOP853-adaptive");
            double rtol = 1e-12d;
            DecisionRun prod = DecisionRunProduction(s, "dop853", rtol, false, 1e-6d, 1e-10d, 1e-8d, true);
            var parityRun = new DecisionMethod
            {
                Name = "DOP853-stand-parity",
                Class = "stand-only",
                Kernel = "dop853",
                Stand = dop853
            };
            DecisionRun standRun = DecisionRunStand(s, parityRun, rtol, true);
            double diff = double.NaN;
            if (prod.Finite && standRun.Finite)
            {
                diff = 0d;
                for (int i = 0; i < s.Refs.CheckpointTimes.Length; i++)
                {
                    diff = Math.Max(diff, (prod.SamplesP[i] - standRun.SamplesP[i]).Magnitude);
                }
            }

            Console.WriteLine("  parity " + s.Label + ": prod " + prod.Status
                + " err=" + (prod.Finite ? prod.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                + ", stand " + standRun.Status
                + " err=" + (standRun.Finite ? standRun.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                + ", diff=" + (double.IsFinite(diff) ? diff.ToString("E3", CultureInfo.InvariantCulture) : "NaN"));

            double threshold = s.Targets[0].Value / 10d;
            rows.Add(new DecisionValidityRow
            {
                Check = "production_parity",
                Scenario = s.Label,
                Method = "DOP853",
                Metric = "prod_vs_stand_max_m",
                Value = diff,
                Threshold = threshold,
                Status = double.IsFinite(diff) && diff <= threshold ? "PASS" : "FAIL",
                Note = "rtol=" + rtol.ToString("G3", CultureInfo.InvariantCulture)
                    + ", production atol=1e-6/1e-10/1e-8"
            });
        }

        // Local-frame error-control check: production DOPRI5 vs the stand DOPRI5
        // on the 30-day LEO scenario. This is a finding, not a harness pass/fail.
        DecisionScenario leo30 = scenarios.Find(x => x.Label.IndexOf("LEO 300", StringComparison.Ordinal) >= 0);
        if (leo30 != null)
        {
            DecisionMethodResult prodDopri = results.Find(x => x.Scenario == leo30 && x.Method.Name == "DOPRI5");
            double bestProd = double.PositiveInfinity;
            if (prodDopri != null)
            {
                foreach (DecisionRun r in prodDopri.Runs)
                {
                    if (r.Status == "ok" && r.Finite)
                    {
                        bestProd = Math.Min(bestProd, r.ErrMaxM);
                    }
                }
            }

            IStudyIntegrator standDopri = DecisionStand("DOPRI5-adaptive");
            if (standDopri != null)
            {
                var dm = new DecisionMethod
                {
                    Name = "DOPRI5-stand",
                    Class = "stand-only",
                    Kernel = "dopri5",
                    Stand = standDopri
                };
                DecisionRun standRun = DecisionRunStand(leo30, dm, 1e-12d, true);
                rows.Add(new DecisionValidityRow
                {
                    Check = "local_frame_error_control",
                    Scenario = leo30.Label,
                    Method = "DOPRI5",
                    Metric = "stand_err_at_rtol_1e-12_m",
                    Value = standRun.Finite ? standRun.ErrMaxM : double.NaN,
                    Threshold = leo30.Targets[0].Value,
                    Status = "FINDING",
                    Note = "production best " + (double.IsFinite(bestProd)
                        ? bestProd.ToString("E3", CultureInfo.InvariantCulture)
                        : "NaN") + " m; stand DOPRI5 "
                        + (standRun.Finite ? standRun.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                        + " m; the production local-frame controller can under-estimate the error"
                });
            }
        }

        // Production local-frame finding: the 5 AU / 10 y arc crosses Jupiter's
        // orbit and the production dominant-body switch misses the target, while
        // the Sun-centred stand methods resolve it.
        DecisionScenario helio10 = scenarios.Find(x => x.Label.IndexOf("5 AU", StringComparison.Ordinal) >= 0);
        if (helio10 != null)
        {
            double prodBest = double.PositiveInfinity;
            double standBest = double.PositiveInfinity;
            foreach (DecisionMethodResult mr in results)
            {
                if (mr.Scenario != helio10)
                {
                    continue;
                }

                foreach (DecisionRun r in mr.Runs)
                {
                    if (r.Status != "ok" || !r.Finite)
                    {
                        continue;
                    }

                    if (mr.Method.Production)
                    {
                        prodBest = Math.Min(prodBest, r.ErrMaxM);
                    }
                    else
                    {
                        standBest = Math.Min(standBest, r.ErrMaxM);
                    }
                }
            }

            rows.Add(new DecisionValidityRow
            {
                Check = "production_dominant_body",
                Scenario = helio10.Label,
                Method = "DOP853/DOPRI5",
                Metric = "best_production_err_m",
                Value = prodBest,
                Threshold = helio10.Targets[0].Value,
                Status = "FINDING",
                Note = "best stand error " + (double.IsFinite(standBest)
                    ? standBest.ToString("E3", CultureInfo.InvariantCulture)
                    : "NaN") + " m; Jupiter-crossing arc, production local-frame dominant-body switch"
            });
        }

        // Chunk invariance: LEO 1 d, DOP853, 60 s vs 600 s, final state only.
        DecisionScenario leo = scenarios.Find(x => x.Label.IndexOf("LEO 200 km", StringComparison.Ordinal) >= 0);
        if (leo != null)
        {
            double rtol = 1e-11d;
            DecisionRun a = DecisionRunProduction(leo, "dop853", rtol, false, 1e-4d, 1e-8d, 1e-4d, true);
            DecisionScenario b = DecisionScenarioWithChunk(leo, 600d);
            DecisionRun c = DecisionRunProduction(b, "dop853", rtol, false, 1e-4d, 1e-8d, 1e-4d, true);
            double diff = double.NaN;
            if (a.Finite && c.Finite)
            {
                diff = 0d;
                int n = Math.Min(a.SamplesP.Length, c.SamplesP.Length);
                for (int i = 0; i < n; i++)
                {
                    diff = Math.Max(diff, (a.SamplesP[i] - c.SamplesP[i]).Magnitude);
                }
            }

            Console.WriteLine("  chunk invariance " + leo.Label + ": 60 s err="
                + (a.Finite ? a.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                + ", 600 s err=" + (c.Finite ? c.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                + ", diff=" + (double.IsFinite(diff) ? diff.ToString("E3", CultureInfo.InvariantCulture) : "NaN"));
            if (DecisionEnv("P1B560_DIAG") == "1" && a.Finite && c.Finite)
            {
                for (int i = 0; i < leo.Refs.CheckpointTimes.Length; i++)
                {
                    Console.WriteLine("    ck " + i.ToString(CultureInfo.InvariantCulture)
                        + " t=" + leo.Refs.CheckpointTimes[i].ToString("G6", CultureInfo.InvariantCulture)
                        + " e60=" + (a.SamplesP[i] - leo.Refs.CheckpointP[i]).Magnitude.ToString("E3", CultureInfo.InvariantCulture)
                        + " e600=" + (c.SamplesP[i] - leo.Refs.CheckpointP[i]).Magnitude.ToString("E3", CultureInfo.InvariantCulture));
                }
            }

            rows.Add(new DecisionValidityRow
            {
                Check = "chunk_invariance",
                Scenario = leo.Label,
                Method = "DOP853",
                Metric = "max_delta_600_vs_60_m",
                Value = diff,
                Threshold = 10d * leo.Targets[0].Value,
                Status = double.IsFinite(diff) && diff <= 10d * leo.Targets[0].Value ? "PASS" : "FAIL",
                Note = "same reference, chunk 60 s vs 600 s"
            });
        }

        return rows;
    }

    private static DecisionScenario DecisionScenarioWithChunk(DecisionScenario s, double chunk)
    {
        // Shallow clone is enough: the case/refs/problem are read-only after build.
        var clone = new DecisionScenario
        {
            Label = s.Label + " [chunk " + chunk.ToString("G", CultureInfo.InvariantCulture) + "]",
            Regime = s.Regime,
            Case = s.Case,
            Refs = s.Refs,
            Problem = s.Problem,
            ChunkSeconds = chunk,
            ChunkCount = (int)Math.Round(s.Case.Seconds / chunk, MidpointRounding.AwayFromZero),
            Targets = s.Targets,
            FloorM = s.FloorM,
            FloorV = s.FloorV,
            EventKind = s.EventKind,
            EventLevelM = s.EventLevelM,
            EventTargetSeconds = s.EventTargetSeconds,
            Note = s.Note
        };
        return clone;
    }

    // ------------------------------------------------------------------------------------
    // Event time: dense output for production, local re-integration for the stand.
    // ------------------------------------------------------------------------------------

    private sealed class DecisionEventResult
    {
        public double Time = double.NaN;
        public double CostRhs;
        public string Status = "unresolved";
        public string Note = "";
    }

    private static double DecisionEventFunction(DecisionScenario s, Vector3d localP, double t)
    {
        if (s.EventKind == "touchdown")
        {
            OrbitingBody earth = s.Case.Sys.AllBodies[s.Case.Central];
            return localP.Magnitude - earth.Radius;
        }

        // soi-out: local position is already relative to the Moon (central body).
        return localP.Magnitude - s.EventLevelM;
    }

    private static DecisionEventResult DecisionFindEventProduction(
        DecisionScenario s, string kernel, double rtol)
    {
        var result = new DecisionEventResult();
        try
        {
            StudyShipMatrixCase c = s.Case;
            StarSystem sys = c.Sys;
            OrbitingBody central = sys.AllBodies[c.Central];
            sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
            var counting = new CountingSource(new CachedGravitySource(sys));
            var phys = new SpacecraftPhysics
            {
                Integrator = kernel == "dopri5" ? SpacecraftIntegrator.Dopri5 : SpacecraftIntegrator.Dop853,
                ToleranceFrameSystem = sys,
                RelativeTolerance = rtol
            };
            phys.Sources.Add(counting);
            if (c.Drag != null)
            {
                phys.Sources.Add(c.Drag);
            }

            if (c.Thrust)
            {
                phys.Sources.Add(new ThrustSource(c.ThrustNewtons, 0d, new ConstantIsp(c.ThrustIsp))
                {
                    ThrottleAt = t => StudyShipMatrixThrottle(c, t),
                    ThrustDirection = c.ThrustDirection
                });
            }

            var ship = new Spacecraft(bp + c.P0, bv + c.V0, c.Mass0);
            var segments = new List<DenseSegment>();
            double t = StudyShipT0;
            double end = t + c.Seconds;
            while (t < end - 1e-12d)
            {
                double chunk = Math.Min(s.ChunkSeconds, end - t);
                phys.StepWithSegments(ship, t, chunk, segments);
                t += chunk;
            }

            result.CostRhs = counting.Calls;
            for (int i = 0; i < segments.Count; i++)
            {
                DenseSegment seg = segments[i];
                double fa = DecisionEventAbsolute(s, seg.Y0.Position, seg.T0);
                double fb = DecisionEventAbsolute(s, seg.Y1.Position, seg.T1);
                if (fa > 0d && fb <= 0d && s.EventKind == "touchdown")
                {
                    result.Time = DecisionBisectDense(s, seg, fa, fb);
                    result.Status = "ok";
                    return result;
                }

                if (s.EventKind == "soi-out" && fa <= 0d && fb > 0d)
                {
                    result.Time = DecisionBisectDense(s, seg, fa, fb);
                    result.Status = "ok";
                    if (string.Equals(DecisionEnv("P1B560_DIAG"), "1", StringComparison.Ordinal))
                    {
                        Console.WriteLine("    event prod bracket t0="
                            + seg.T0.ToString("R", CultureInfo.InvariantCulture)
                            + " t1=" + seg.T1.ToString("R", CultureInfo.InvariantCulture)
                            + " fa=" + fa.ToString("E3", CultureInfo.InvariantCulture)
                            + " fb=" + fb.ToString("E3", CultureInfo.InvariantCulture)
                            + " found=" + result.Time.ToString("R", CultureInfo.InvariantCulture)
                            + " f(found)=" + DecisionEventAbsolute(s, seg.Evaluate(result.Time).Position, result.Time)
                                .ToString("E3", CultureInfo.InvariantCulture));
                    }

                    return result;
                }
            }

            result.Note = "event not crossed inside the horizon";
        }
        catch (Exception ex)
        {
            result.Status = "error";
            result.Note = ex.GetType().Name + ": " + ex.Message;
        }

        return result;
    }

    private static double DecisionEventAbsolute(DecisionScenario s, Vector3d absoluteP, double t)
    {
        OrbitingBody body = s.Case.Sys.AllBodies[s.Case.Central];
        s.Case.Sys.EvaluateBodyState(body, t, out Vector3d bp, out _);
        return DecisionEventFunction(s, absoluteP - bp, t);
    }

    private static double DecisionBisectDense(DecisionScenario s, DenseSegment seg, double fa, double fb)
    {
        double lo = seg.T0;
        double hi = seg.T1;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5d * (lo + hi);
            double f = DecisionEventAbsolute(s, seg.Evaluate(mid).Position, mid);
            if (s.EventKind == "touchdown")
            {
                // positive altitude -> negative: crossing is to the right
                if (f > 0d)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            else
            {
                // SOI distance negative -> positive: crossing is to the left
                if (f <= 0d)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
        }

        return 0.5d * (lo + hi);
    }

    private static double DecisionReferenceEventTime(DecisionScenario s, double h, out double calls)
    {
        double t0 = StudyShipT0;
        double end = t0 + s.Case.Seconds;
        var allTimes = new List<double>();
        allTimes.Add(t0);
        for (int i = 1; ; i++)
        {
            double t = t0 + (i * h);
            if (t >= end)
            {
                allTimes.Add(end);
                break;
            }

            allTimes.Add(t);
        }

        int count = allTimes.Count;
        var times = allTimes.ToArray();
        var pos = new Vector3d[count];
        var vel = new Vector3d[count];
        pos[0] = s.Case.P0;
        vel[0] = s.Case.V0;

        // StudyShipRefDop853Fixed records samples at segment ENDS. Passing t0 as
        // the first sample would stall its sample pointer (t0 is an edge, not an
        // end), so the initial state is prepended here and only t0+h..end go in.
        int m = count - 1;
        var sampleTimes = new double[m];
        var samplePos = new Vector3d[m];
        var sampleVel = new Vector3d[m];
        Array.Copy(times, 1, sampleTimes, 0, m);
        long used;
        StudyShipRefDop853Fixed(s.Case, h, out used, sampleTimes, samplePos, sampleVel);
        calls = used;
        Array.Copy(samplePos, 0, pos, 1, m);
        Array.Copy(sampleVel, 0, vel, 1, m);

        double prevT = times[0];
        double prevF = DecisionEventFunction(s, pos[0], prevT);
        if (string.Equals(DecisionEnv("P1B560_DIAG"), "1", StringComparison.Ordinal))
        {
            double minF = double.PositiveInfinity;
            double maxF = double.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                double f = DecisionEventFunction(s, pos[i], times[i]);
                minF = Math.Min(minF, f);
                maxF = Math.Max(maxF, f);
            }

            Console.WriteLine("    event diag " + s.Label + " h=" + h.ToString("G6", CultureInfo.InvariantCulture)
                + " samples=" + count.ToString(CultureInfo.InvariantCulture)
                + " f0=" + prevF.ToString("E3", CultureInfo.InvariantCulture)
                + " fmin=" + minF.ToString("E3", CultureInfo.InvariantCulture)
                + " fmax=" + maxF.ToString("E3", CultureInfo.InvariantCulture));
        }

        for (int i = 1; i < count; i++)
        {
            double f = DecisionEventFunction(s, pos[i], times[i]);
            bool crossing = s.EventKind == "touchdown"
                ? prevF > 0d && f <= 0d
                : prevF <= 0d && f > 0d;
            if (crossing)
            {
                double frac = Math.Abs(prevF) / Math.Max(Math.Abs(prevF) + Math.Abs(f), 1e-30d);
                return prevT + ((times[i] - prevT) * frac);
            }

            prevT = times[i];
            prevF = f;
        }

        return double.NaN;
    }

    private static DecisionEventResult DecisionFindEventStand(
        DecisionScenario s, DecisionMethod m, double control)
    {
        var result = new DecisionEventResult();
        try
        {
            // First pass: one state per chunk, same chunk restarts as the main run.
            var chunked = new DecisionChunkedIntegrator(m.Stand, s.ChunkSeconds, double.PositiveInfinity);
            StudyConfig cfg = chunked.MakeConfig(s.Problem, control, double.PositiveInfinity);
            var ctx = new StudyContext(s.Problem, DecisionRunBudgetRhs);
            var trajectory = new List<double[]>();
            var times = new List<double>();
            double[] y = (double[])s.Problem.Y0.Clone();
            double t = s.Problem.T0;
            double end = s.Problem.EndTime;
            while (t < end - 1e-12d)
            {
                double chunkEnd = Math.Min(t + s.ChunkSeconds, end);
                StudyProblem sub = s.Problem.ShallowClone();
                sub.T0 = t;
                sub.Y0 = y;
                sub.CheckpointTimes = new[] { chunkEnd };
                sub.CheckpointRef = new[] { s.Problem.CheckpointRef[0] };
                sub.Breakpoints = null;
                StudyConfig subCfg = m.Stand.MakeConfig(s.Problem, control, double.PositiveInfinity);
                subCfg.H0 = chunkEnd - t;
                StudyRun part = m.Stand.Run(sub, subCfg, ctx);
                if (part.Aborted)
                {
                    result.Status = "error";
                    result.Note = part.Abort;
                    return result;
                }

                y = part.Final;
                t = chunkEnd;
                times.Add(t);
                trajectory.Add((double[])y.Clone());
            }

            result.CostRhs = ctx.Calls;
            double prevT = s.Problem.T0;
            double prevF = DecisionEventFunction(s,
                new Vector3d(s.Problem.Y0[0], s.Problem.Y0[1], s.Problem.Y0[2]), prevT);
            int bracket = -1;
            for (int i = 0; i < times.Count; i++)
            {
                double f = DecisionEventFunction(s,
                    new Vector3d(trajectory[i][0], trajectory[i][1], trajectory[i][2]), times[i]);
                bool crossing = s.EventKind == "touchdown"
                    ? prevF > 0d && f <= 0d
                    : prevF <= 0d && f > 0d;
                if (crossing)
                {
                    bracket = i;
                    break;
                }

                prevT = times[i];
                prevF = f;
            }

            if (bracket < 0)
            {
                result.Note = "event not crossed inside the horizon";
                return result;
            }

            // Local refinement: re-integrate only the bracketing chunk with the same
            // method at a control four times tighter. This is root finding, not an
            // artificial stop in the measured run.
            double startT = bracket == 0 ? s.Problem.T0 : times[bracket - 1];
            double[] startY = bracket == 0 ? (double[])s.Problem.Y0.Clone() : trajectory[bracket - 1];
            double endT = times[bracket];
            double lo = startT;
            double hi = endT;
            double fLo = DecisionEventFunction(s,
                new Vector3d(startY[0], startY[1], startY[2]), lo);
            for (int i = 0; i < 50; i++)
            {
                double mid = 0.5d * (lo + hi);
                StudyProblem sub = s.Problem.ShallowClone();
                sub.T0 = startT;
                sub.Y0 = startY;
                sub.CheckpointTimes = new[] { mid };
                sub.CheckpointRef = new[] { s.Problem.CheckpointRef[0] };
                sub.Breakpoints = null;
                StudyConfig subCfg = m.Stand.MakeConfig(s.Problem, DecisionTighterControl(m, control), double.PositiveInfinity);
                subCfg.H0 = Math.Min(mid - startT, 600d);
                var subCtx = new StudyContext(sub, DecisionRunBudgetRhs);
                StudyRun part = m.Stand.Run(sub, subCfg, subCtx);
                result.CostRhs += subCtx.Calls;
                if (part.Aborted || part.Final == null)
                {
                    result.Status = "error";
                    result.Note = part.Abort;
                    return result;
                }

                double f = DecisionEventFunction(s,
                    new Vector3d(part.Final[0], part.Final[1], part.Final[2]), mid);
                bool still = s.EventKind == "touchdown" ? f > 0d : f <= 0d;
                if (still)
                {
                    lo = mid;
                    fLo = f;
                }
                else
                {
                    hi = mid;
                }

                if (hi - lo < 1e-7d)
                {
                    break;
                }
            }

            result.Time = 0.5d * (lo + hi);
            result.Status = "ok";
        }
        catch (Exception ex)
        {
            result.Status = "error";
            result.Note = ex.GetType().Name + ": " + ex.Message;
        }

        return result;
    }

    private static double DecisionTighterControl(DecisionMethod m, double control)
    {
        if (m.Stand.Kind == StudyKind.Fixed)
        {
            return control / 8d;
        }

        return Math.Max(control / 100d, 1e-14d);
    }

    // ------------------------------------------------------------------------------------
    // Warp cost: a real run of 60 frames, each frame advances factor/60 s.
    // ------------------------------------------------------------------------------------

    private sealed class DecisionWarpRow
    {
        public string Scenario;
        public string Regime;
        public string Method;
        public double Factor;
        public double SimSeconds;
        public long Chunks;
        public long Calls;
        public double CostRhs;
        public double CostLocalFrame;
        public double Ms;
        public long GcBytes;
        public string Status;
        public string Note;
    }

    private static List<DecisionWarpRow> DecisionWarpTable(
        List<DecisionScenario> scenarios, List<DecisionMethodResult> results)
    {
        var rows = new List<DecisionWarpRow>();
        double[] factors = { 1d, 10d, 100d, 1000d };
        foreach (DecisionScenario s in scenarios)
        {
            foreach (string kernel in new[] { "dop853", "dopri5" })
            {
                DecisionMethodResult mr = results.Find(x => x.Scenario == s && x.Method.Name == (kernel == "dop853" ? "DOP853" : "DOPRI5"));
                double rtol = mr != null && mr.BestHit != null ? mr.BestHit.Control : 1e-11d;
                foreach (double factor in factors)
                {
                    rows.Add(DecisionWarpRun(s, kernel, rtol, factor));
                }
            }
        }

        return rows;
    }

    private static DecisionWarpRow DecisionWarpRun(
        DecisionScenario s, string kernel, double rtol, double factor)
    {
        var row = new DecisionWarpRow
        {
            Scenario = s.Label,
            Regime = s.Regime,
            Method = kernel == "dop853" ? "DOP853" : "DOPRI5",
            Factor = factor,
            SimSeconds = factor,
            Status = "ok"
        };

        try
        {
            StudyShipMatrixCase c = s.Case;
            StarSystem sys = c.Sys;
            OrbitingBody central = sys.AllBodies[c.Central];
            sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
            var counting = new CountingSource(new CachedGravitySource(sys));
            var phys = new SpacecraftPhysics
            {
                Integrator = kernel == "dopri5" ? SpacecraftIntegrator.Dopri5 : SpacecraftIntegrator.Dop853,
                ToleranceFrameSystem = sys,
                RelativeTolerance = rtol
            };
            phys.Sources.Add(counting);
            if (c.Drag != null)
            {
                phys.Sources.Add(c.Drag);
            }

            if (c.Thrust)
            {
                phys.Sources.Add(new ThrustSource(c.ThrustNewtons, 0d, new ConstantIsp(c.ThrustIsp))
                {
                    ThrottleAt = t => StudyShipMatrixThrottle(c, t),
                    ThrustDirection = c.ThrustDirection
                });
            }

            var ship = new Spacecraft(bp + c.P0, bv + c.V0, c.Mass0);
            double frameSim = factor / 60d;
            double t = StudyShipT0;
            long chunks = 0L;
            long gc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int frame = 0; frame < 60; frame++)
            {
                double frameEnd = t + frameSim;
                while (t < frameEnd - 1e-12d)
                {
                    double chunk = Math.Min(s.ChunkSeconds, frameEnd - t);
                    phys.Step(ship, t, chunk);
                    t += chunk;
                    chunks++;
                    if (counting.Calls > DecisionRunBudgetRhs)
                    {
                        row.Status = "censored";
                        row.Note = "RHS budget exceeded";
                        break;
                    }
                }

                if (row.Status != "ok")
                {
                    break;
                }
            }

            sw.Stop();
            row.GcBytes = GC.GetAllocatedBytesForCurrentThread() - gc0;
            row.Ms = sw.Elapsed.TotalMilliseconds;
            row.Chunks = chunks;
            row.Calls = counting.Calls;
            row.CostRhs = counting.Calls;
            long localCalls = Math.Max(0L, counting.Calls - chunks);
            row.CostLocalFrame = (3d * localCalls) + chunks;
        }
        catch (Exception ex)
        {
            row.Status = "error";
            row.Note = ex.GetType().Name + ": " + ex.Message;
        }

        return row;
    }
}
