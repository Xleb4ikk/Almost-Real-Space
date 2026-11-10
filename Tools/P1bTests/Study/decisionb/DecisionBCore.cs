using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

// Test560b measurement engine: every StudyRegistry method plus the existing
// cap variants, the production path, explicit configs and full-cost accounting.
internal static partial class P1bTests
{
    private const long DecisionBRunBudgetRhs = 80000000L;

    private sealed class DecisionBMethod
    {
        public string Name;
        public string Class;      // production | stand-only
        public string Kernel;     // production: dop853/dopri5; stand: registry name
        public bool Production;
        public IStudyIntegrator Stand;
        public double FixedMaxStep; // 0 = scenario max step; >0 = explicit cap
        public bool IsCap;
        public string MaxStepLabel = "";
    }

    private sealed class DecisionBRun
    {
        public double Control;
        public string ControlKind;
        public string Status = "ok";
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
        /// <summary>Effective average fixed step: duration / accepted steps. NaN when steps = 0.</summary>
        public double HAvg = double.NaN;
        /// <summary>Production absolute tolerances (pos/vel/mass) for this run; empty on stand rows.</summary>
        public string ProductionAtol = "";
        /// <summary>Stand absolute tolerances (pos/vel/mass) for this run; empty on production rows.</summary>
        public string StandAtol = "";
        public double CostRhs;
        public double CostLocalFrame;
        public double CostAux;
        public double CostTotal;
        /// <summary>
        /// Comparable total if the method ran through the production local-frame
        /// wrapper: production already pays it; a stand method is charged the
        /// measured production convention 2*(raw RHS - chunks). This is an
        /// estimate, not a re-run, and is marked as requiring a port.
        /// </summary>
        public double CostIfPorted;
        public double Ms;
        public long GcBytes;
        public int Reps = 1;
        public double MsMedian;
        public double MsSpreadPct;
        public bool Finite;
        public Vector3d[] SamplesP;
        public Vector3d[] SamplesV;
        public bool Sensitive; // chunk-sweep / max-step sensitivity run
    }

    private sealed class DecisionBMethodResult
    {
        public DecisionBScenario Scenario;
        public DecisionBMethod Method;
        public string Status = "not run"; // ok | n/a | unresolved | not run
        public string Reason = "";
        public double EffectiveMaxStep;
        public List<DecisionBRun> Runs = new List<DecisionBRun>();
        /// <summary>
        /// Executed runs excluded from the selection list because they reproduce
        /// an existing fixed-step h_avg (same executed trajectory). They are
        /// still written to Test560b_Runs.csv so no executed point is hidden.
        /// </summary>
        public List<DecisionBRun> ExtraRuns = new List<DecisionBRun>();
        public DecisionBRun BestHit;
    }

    private static List<DecisionBMethod> DecisionBMethods()
    {
        var methods = new List<DecisionBMethod>();
        methods.Add(new DecisionBMethod
        {
            Name = "DOP853 [production]",
            Class = "production",
            Kernel = "dop853",
            Production = true
        });
        methods.Add(new DecisionBMethod
        {
            Name = "DOPRI5 [production]",
            Class = "production",
            Kernel = "dopri5",
            Production = true
        });

        List<IStudyIntegrator> all = StudyRegistry.All();
        foreach (IStudyIntegrator integ in all)
        {
            methods.Add(new DecisionBMethod
            {
                Name = integ.Name,
                Class = "stand-only",
                Kernel = integ.Name,
                Stand = integ
            });
        }

        // Cap variants that already exist in the stand (Test552/554/558/559 use
        // 600 s for ships; Test553 uses 86 400 s for planets). They are explicit
        // configurations, not new integrators.
        string[] capNames = { "DOPRI5-adaptive", "DOP853-adaptive", "RKN12(10)", "IAS15" };
        foreach (string name in capNames)
        {
            IStudyIntegrator integ = all.Find(x => string.Equals(x.Name, name, StringComparison.Ordinal));
            if (integ == null)
            {
                continue;
            }

            foreach (double cap in new[] { 600d, 86400d })
            {
                methods.Add(new DecisionBMethod
                {
                    Name = name + " [cap=" + cap.ToString("G", CultureInfo.InvariantCulture) + " s]",
                    Class = "stand-only",
                    Kernel = name,
                    Stand = integ,
                    FixedMaxStep = cap,
                    IsCap = true
                });
            }
        }

        return methods;
    }

    private static double DecisionBEffectiveMaxStep(DecisionBScenario s, DecisionBMethod m)
    {
        double maxStep = m.FixedMaxStep > 0d ? m.FixedMaxStep : s.MaxStep;
        if (!(maxStep > 0d))
        {
            maxStep = 600d;
        }

        return maxStep;
    }

    private static bool DecisionBQuick()
    {
        return string.Equals(DecisionBEnv("P1B560B_QUICK"), "1", StringComparison.Ordinal);
    }

    private static bool DecisionBSupports(DecisionBScenario s, DecisionBMethod m, out string reason)
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

    private static bool DecisionBCapEquivalent(DecisionBScenario s, DecisionBMethod m, double effMaxStep)
    {
        if (!m.IsCap)
        {
            return false;
        }

        // A cap is redundant when, under this scenario's chunk, it produces the
        // same effective step ceiling as the base method.
        double baseEff = Math.Min(s.MaxStep > 0d ? s.MaxStep : 600d, s.ChunkMax);
        double capEff = Math.Min(effMaxStep, s.ChunkMax);
        return Math.Abs(baseEff - capEff) <= 1e-9d;
    }

    private static double[] DecisionBGrid(DecisionBScenario s, DecisionBMethod m, double effMaxStep)
    {
        if (m.Production)
        {
            return DecisionBQuick()
                ? new[] { 1e-6d, 1e-9d, 1e-12d }
                : new[] { 1e-6d, 1e-8d, 1e-10d, 1e-11d, 1e-12d, 1e-13d };
        }

        if (m.Stand.Kind == StudyKind.Fixed)
        {
            double coarse;
            double fine;
            double ratio;
            m.Stand.ControlRange(s.Problem, out coarse, out fine, out ratio);
            double ceiling = Math.Min(s.ChunkMax, effMaxStep);
            if (coarse > ceiling)
            {
                coarse = ceiling;
            }

            double floor = Math.Max(fine, Math.Min(coarse, s.ChunkMax) / 1024d);
            if (!(floor < coarse))
            {
                floor = coarse / 1024d;
            }

            int count = DecisionBQuick() ? 3 : 6;
            var grid = new double[count];
            double factor = Math.Pow(coarse / floor, 1d / (count - 1));
            for (int i = 0; i < count; i++)
            {
                grid[i] = coarse / Math.Pow(factor, i);
            }

            return grid;
        }

        return DecisionBQuick()
            ? new[] { 1e-4d, 1e-8d, 1e-12d }
            : new[] { 1e-4d, 1e-6d, 1e-8d, 1e-10d, 1e-12d };
    }

    private static string DecisionBControlKind(DecisionBMethod m)
    {
        if (m.Production)
        {
            return "rtol";
        }

        return m.Stand.Kind == StudyKind.Fixed ? "h" : "rtol";
    }

    // ------------------------------------------------------------------------------------
    // Production path
    // ------------------------------------------------------------------------------------

    private static DecisionBRun DecisionBRunProduction(
        DecisionBScenario s, string kernel, double rtol, double maxStep,
        OrbitingBody dominantOverride, bool useFrame, bool dense,
        double posTol = 1e-4d, double velTol = 1e-8d, double massTol = 1e-4d,
        long budget = DecisionBRunBudgetRhs)
    {
        var run = new DecisionBRun
        {
            Control = rtol,
            ControlKind = "rtol",
            SamplesP = new Vector3d[s.Refs.CheckpointTimes.Length],
            SamplesV = new Vector3d[s.Refs.CheckpointTimes.Length],
            Sensitive = s.Sensitivity,
            ProductionAtol = DecisionBAtolLabel(posTol, velTol, massTol)
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
                ToleranceFrameSystem = useFrame ? sys : null,
                DominantBodyOverride = dominantOverride,
                MaxStepSize = maxStep,
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
                double chunk = Math.Min(s.ChunkAt(t), end - t);
                chunk = DecisionBClampToBreakpoint(s, t, chunk);
                if (sample < s.Refs.CheckpointTimes.Length)
                {
                    double ck = s.Refs.CheckpointTimes[sample];
                    if (ck > t + 1e-9d && ck < t + chunk - 1e-9d)
                    {
                        chunk = ck - t;
                    }
                }

                if (!(chunk > 0d))
                {
                    break;
                }

                phys.StepWithSegments(ship, t, chunk, segments);
                accepted += phys.StepDiagnostics.Accepted;
                rejected += phys.StepDiagnostics.Rejected;
                attempts += phys.StepDiagnostics.Attempts;
                chunksDone++;
                t += chunk;

                if (counting.Calls > budget)
                {
                    run.Status = "censored";
                    run.Note = "RHS budget " + budget.ToString(CultureInfo.InvariantCulture) + " exceeded";
                    break;
                }

                while (sample < s.Refs.CheckpointTimes.Length
                    && s.Refs.CheckpointTimes[sample] <= t + 1e-6d)
                {
                    DecisionBRecordProductionSample(s, sys, central, ship, s.Refs.CheckpointTimes[sample], run, sample);
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
            run.CostRhs = counting.Calls;
            long localCalls = Math.Max(0L, counting.Calls - chunksDone);
            run.CostLocalFrame = 2d * localCalls;
            run.CostAux = 0d;
            run.CostTotal = run.CostRhs + run.CostLocalFrame + run.CostAux;
            run.CostIfPorted = run.CostTotal;
            run.Finite = sample == s.Refs.CheckpointTimes.Length && DecisionBAllFinite(run.SamplesP, run.SamplesV);
            DecisionBSetHAvg(s, run);
            DecisionBEvaluateErrors(s, run);
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

    private static double DecisionBClampToBreakpoint(DecisionBScenario s, double t, double chunk)
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

    private static void DecisionBRecordProductionSample(
        DecisionBScenario s, StarSystem sys, OrbitingBody central, Spacecraft ship,
        double time, DecisionBRun run, int sample)
    {
        sys.EvaluateBodyState(central, time, out Vector3d bp, out Vector3d bv);
        bool localToCentral = ship.HasLocalState && ReferenceEquals(ship.LocalBody, central);
        run.SamplesP[sample] = localToCentral ? ship.LocalPosition : ship.Position - bp;
        run.SamplesV[sample] = localToCentral ? ship.LocalVelocity : ship.Velocity - bv;
    }

    private static bool DecisionBAllFinite(Vector3d[] p, Vector3d[] v)
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

    private static void DecisionBSetHAvg(DecisionBScenario s, DecisionBRun run)
    {
        run.HAvg = run.Steps > 0L && s.Case.Seconds > 0d
            ? s.Case.Seconds / run.Steps
            : double.NaN;
    }

    private static string DecisionBAtolLabel(double pos, double vel, double mass)
    {
        if (pos == 0d && vel == 0d && mass == 0d)
        {
            return "0/0/0";
        }

        if (pos == 1e-4d && vel == 1e-8d && mass == 1e-4d)
        {
            return "1e-4/1e-8/1e-4";
        }

        return pos.ToString("G3", CultureInfo.InvariantCulture) + "/"
            + vel.ToString("G3", CultureInfo.InvariantCulture) + "/"
            + mass.ToString("G3", CultureInfo.InvariantCulture);
    }

    private static void DecisionBEvaluateErrors(DecisionBScenario s, DecisionBRun run)
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
    }

    // ------------------------------------------------------------------------------------
    // Stand path with a fresh controller at every chunk boundary
    // ------------------------------------------------------------------------------------

    private sealed class DecisionBChunkedIntegrator : IStudyIntegrator
    {
        private readonly IStudyIntegrator inner;
        private readonly Func<double, double> chunkAt;
        private readonly double chunkMax;
        private readonly double maxStepCap;

        public long Chunks;

        public DecisionBChunkedIntegrator(
            IStudyIntegrator inner, Func<double, double> chunkAt, double chunkMax, double maxStepCap)
        {
            this.inner = inner;
            this.chunkAt = chunkAt;
            this.chunkMax = chunkMax;
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
                double ceiling = Math.Min(chunkMax, maxStepCap);
                if (coarse > ceiling)
                {
                    coarse = ceiling;
                }

                double floor = Math.Max(fine, Math.Min(coarse, chunkMax) / 1024d);
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
            Chunks = 0L;

            while (t < end - 1e-12d * Math.Max(1d, Math.Abs(end)))
            {
                while (ck < p.CheckpointTimes.Length && p.CheckpointTimes[ck] <= t + 1e-6d)
                {
                    ck++;
                }

                double chunk = Math.Min(chunkAt(t), end - t);
                double chunkEnd = t + chunk;
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
                subCfg.H0 = maxStepCap > 0d ? Math.Min(span, maxStepCap) : span;
                if (!(subCfg.H0 > 0d))
                {
                    subCfg.H0 = span;
                }

                Chunks++;
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

    private static DecisionBRun DecisionBRunStand(
        DecisionBScenario s, DecisionBMethod m, double control, double maxStep)
    {
        var run = new DecisionBRun
        {
            Control = control,
            ControlKind = DecisionBControlKind(m),
            SamplesP = new Vector3d[s.Refs.CheckpointTimes.Length],
            SamplesV = new Vector3d[s.Refs.CheckpointTimes.Length],
            Sensitive = s.Sensitivity,
            StandAtol = "0/0/0"
        };

        try
        {
            var chunked = new DecisionBChunkedIntegrator(m.Stand, s.ChunkAt, s.ChunkMax, maxStep);
            StudyConfig cfg = chunked.MakeConfig(s.Problem, control, double.PositiveInfinity);
            var ctx = new StudyContext(s.Problem, DecisionBRunBudgetRhs);
            long gc0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            StudyRun result = chunked.Run(s.Problem, cfg, ctx);
            sw.Stop();
            run.GcBytes = GC.GetAllocatedBytesForCurrentThread() - gc0;
            run.Ms = sw.Elapsed.TotalMilliseconds;
            run.RawCalls = ctx.Calls;
            run.Aux = ctx.AuxOps;
            run.Calls = ctx.Calls;
            run.Chunks = chunked.Chunks;
            run.CostRhs = ctx.Calls;
            run.CostLocalFrame = 0d; // the stand's RHS is already the local force, one gravity sum
            run.CostAux = s.KeplerWeight * ctx.AuxOps;
            run.CostTotal = run.CostRhs + run.CostLocalFrame + run.CostAux;
            // Comparability estimate, not a measurement: the production local
            // derivative pays two extra gravity sums per non-chunk RHS (the
            // torque snapshot is one call per chunk). A ported stand method would
            // pay the same wrapper, so charge it the same convention.
            long localCalls = Math.Max(0L, run.RawCalls - run.Chunks);
            run.CostIfPorted = run.CostTotal + (2d * localCalls);
            run.Steps = result.Steps;
            run.Accepted = result.Steps;
            run.Rejected = result.Rejected;
            run.Finite = true;
            DecisionBSetHAvg(s, run);
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
                DecisionBEvaluateErrors(s, run);
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

    private static DecisionBRun DecisionBRunPoint(
        DecisionBScenario s, DecisionBMethod m, double control, double maxStep,
        Dictionary<string, DecisionBRun> cache, bool forceRun = false)
    {
        string key = s.Label + "|" + m.Kernel + "|" + maxStep.ToString("R", CultureInfo.InvariantCulture)
            + "|" + control.ToString("R", CultureInfo.InvariantCulture);
        DecisionBRun cached;
        if (!forceRun && cache.TryGetValue(key, out cached))
        {
            return cached;
        }

        DecisionBRun run = m.Production
            ? DecisionBRunProduction(s, m.Kernel, control, maxStep, s.DominantBodyOverride, true, false)
            : DecisionBRunStand(s, m, control, maxStep);
        cache[key] = run;
        Console.WriteLine("    " + s.Label + " / " + m.Name + " / "
            + run.ControlKind + "=" + control.ToString("G6", CultureInfo.InvariantCulture)
            + " @max " + maxStep.ToString("G6", CultureInfo.InvariantCulture)
            + " -> " + (run.Finite ? run.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : run.Status)
            + " m, total " + run.CostTotal.ToString("N0", CultureInfo.InvariantCulture)
            + ", " + run.Ms.ToString("F1", CultureInfo.InvariantCulture) + " ms");
        return run;
    }

    // ------------------------------------------------------------------------------------
    // Sweep / coverage
    // ------------------------------------------------------------------------------------

    private static List<DecisionBMethodResult> DecisionBSweepScenario(
        DecisionBScenario s, List<DecisionBMethod> methods, Dictionary<string, DecisionBRun> cache)
    {
        var results = new List<DecisionBMethodResult>();
        if (s.Config == "auto-bug")
        {
            foreach (DecisionBMethod m in methods)
            {
                results.Add(new DecisionBMethodResult
                {
                    Scenario = s,
                    Method = m,
                    Status = "not run",
                    Reason = "isolation-only production dominant-body bug; decision uses the forced-Sun scenario"
                });
            }

            return results;
        }

        foreach (DecisionBMethod m in methods)
        {
            var mr = new DecisionBMethodResult { Scenario = s, Method = m };
            results.Add(mr);

            string reason;
            if (!DecisionBSupports(s, m, out reason))
            {
                mr.Status = "n/a";
                mr.Reason = reason;
                Console.WriteLine("  " + s.Label + " / " + m.Name + ": n/a (" + reason + ")");
                continue;
            }

            double maxStep = DecisionBEffectiveMaxStep(s, m);
            mr.EffectiveMaxStep = maxStep;
            if (DecisionBCapEquivalent(s, m, maxStep))
            {
                mr.Status = "not run";
                mr.Reason = "cap is equivalent to the base method under this chunk (max step "
                    + Math.Min(maxStep, s.ChunkMax).ToString("G6", CultureInfo.InvariantCulture) + " s)";
                Console.WriteLine("  " + s.Label + " / " + m.Name + ": not run (" + mr.Reason + ")");
                continue;
            }

            double[] grid = DecisionBGrid(s, m, maxStep);
            double loosestTarget = double.MinValue;
            double tightestTarget = double.MaxValue;
            string tightestKind = "position";
            foreach (DecisionBTarget t in s.Targets)
            {
                loosestTarget = Math.Max(loosestTarget, t.Value);
                if (t.Value < tightestTarget)
                {
                    tightestTarget = t.Value;
                    tightestKind = t.Kind;
                }
            }

            mr.Runs = new List<DecisionBRun>();
            foreach (double control in grid)
            {
                DecisionBRun run = DecisionBRunPoint(s, m, control, maxStep, cache);
                mr.Runs.Add(run);
                if (run.Status == "censored")
                {
                    break;
                }

                if (run.Status == "ok" && run.Finite
                    && DecisionBRunError(run, tightestKind) <= 0.25d * tightestTarget)
                {
                    break;
                }
            }

            // A hit on the coarsest point is not a winner; try looser controls.
            // If the first hit is already one step per chunk and far below the
            // target, looser controls cannot reduce the chunk-bound cost; skip.
            bool chunkBound = mr.Runs.Count > 0
                && mr.Runs[0].Chunks > 0
                && mr.Runs[0].Steps <= 1.2d * mr.Runs[0].Chunks
                && mr.Runs[0].Finite
                && DecisionBRunError(mr.Runs[0], "position") <= 0.01d * tightestTarget;
            double controlLoosest = grid[0];
            int extend = 0;
            while (!chunkBound
                && (m.Production || m.Stand.Kind == StudyKind.Adaptive)
                && extend++ < 3 && DecisionBCheapestHit(mr.Runs, loosestTarget, "position") != null)
            {
                double looser = controlLoosest * 10d;
                if (!(looser < 1d))
                {
                    break;
                }

                DecisionBRun run = DecisionBRunPoint(s, m, looser, maxStep, cache);
                mr.Runs.Add(run);
                controlLoosest = looser;
                if (run.Status != "ok" || !run.Finite)
                {
                    break;
                }
            }

            foreach (DecisionBTarget t in s.Targets)
            {
                DecisionBRefineTarget(s, m, maxStep, t.Value, t.Kind, cache, mr.Runs, mr.ExtraRuns);
            }

            mr.BestHit = DecisionBCheapestHit(mr.Runs, tightestTarget, tightestKind);
            bool anyFinite = false;
            bool allError = true;
            foreach (DecisionBRun r in mr.Runs)
            {
                if (r.Status == "ok" && r.Finite)
                {
                    anyFinite = true;
                    allError = false;
                }
                else if (r.Status == "error" || r.Status == "censored")
                {
                    // keep the failed run, do not silently drop it
                }
            }

            if (anyFinite)
            {
                mr.Status = "ranked";
                mr.Reason = "ranked: " + mr.Runs.Count.ToString(CultureInfo.InvariantCulture)
                    + " executed control point(s), finite";
            }
            else
            {
                mr.Status = "unresolved";
                mr.Reason = allError ? "all executed runs failed or were censored" : "no finite run";
            }
        }

        return results;
    }

    private static double DecisionBRunError(DecisionBRun r, string kind)
    {
        return kind == "velocity" ? r.ErrMaxV : r.ErrMaxM;
    }

    /// <summary>
    /// Comparable cost of one executed control point: the same metric the
    /// decision table ranks by (`cost_if_ported`). Production already pays the
    /// local frame; a stand run is charged the disclosed port estimate.
    /// </summary>
    private static double DecisionBRunComparableCost(DecisionBRun r)
    {
        if (r == null)
        {
            return double.PositiveInfinity;
        }

        return double.IsFinite(r.CostIfPorted) ? r.CostIfPorted : r.CostTotal;
    }

    private static DecisionBRun DecisionBCheapestHit(List<DecisionBRun> runs, double target, string kind)
    {
        return DecisionBCheapestHitBy(runs, target, kind, DecisionBRunComparableCost);
    }

    private static DecisionBRun DecisionBCheapestHitBy(
        List<DecisionBRun> runs, double target, string kind, Func<DecisionBRun, double> cost)
    {
        DecisionBRun bestOk = null;
        double bestOkCost = double.PositiveInfinity;
        DecisionBRun bestEdge = null;
        double bestEdgeCost = double.PositiveInfinity;
        foreach (DecisionBRun r in runs)
        {
            if (r.Status != "ok" || !r.Finite)
            {
                continue;
            }

            double err = DecisionBRunError(r, kind);
            double c = cost(r);
            if (err <= 0.8d * target && c < bestOkCost)
            {
                bestOkCost = c;
                bestOk = r;
            }
            else if (err <= target && c < bestEdgeCost)
            {
                bestEdgeCost = c;
                bestEdge = r;
            }
        }

        return bestOk ?? bestEdge;
    }

    /// <summary>
    /// Cheapest run inside the 80-100% edge band, kept separately from the
    /// selected (margin-preferred) hit. This is what makes a cheap edge visible
    /// instead of being silently replaced by the more accurate ok run.
    /// </summary>
    private static DecisionBRun DecisionBCheapestEdgeHit(
        List<DecisionBRun> runs, double target, string kind)
    {
        DecisionBRun best = null;
        double bestCost = double.PositiveInfinity;
        foreach (DecisionBRun r in runs)
        {
            if (r.Status != "ok" || !r.Finite)
            {
                continue;
            }

            double err = DecisionBRunError(r, kind);
            double c = DecisionBRunComparableCost(r);
            if (err > 0.8d * target && err <= target && c < bestCost)
            {
                bestCost = c;
                best = r;
            }
        }

        return best;
    }

    private static DecisionBRun DecisionBCheapestMiss(
        List<DecisionBRun> runs, double target, string kind, double belowCost)
    {
        DecisionBRun best = null;
        double bestCost = double.NegativeInfinity;
        foreach (DecisionBRun r in runs)
        {
            if (r.Status != "ok" || !r.Finite || !(DecisionBRunError(r, kind) > target))
            {
                continue;
            }

            double c = DecisionBRunComparableCost(r);
            if (c < belowCost && c > bestCost)
            {
                bestCost = c;
                best = r;
            }
        }

        return best;
    }

    private static void DecisionBRefineTarget(
        DecisionBScenario s, DecisionBMethod m, double maxStep, double target, string kind,
        Dictionary<string, DecisionBRun> cache, List<DecisionBRun> runs, List<DecisionBRun> extraRuns)
    {
        for (int i = 0; i < 3; i++)
        {
            DecisionBRun hit = DecisionBCheapestHit(runs, target, kind);
            if (hit == null)
            {
                return;
            }

            DecisionBRun miss = DecisionBCheapestMiss(runs, target, kind, DecisionBRunComparableCost(hit));
            if (miss == null)
            {
                return;
            }

            if (DecisionBRunComparableCost(hit) <= 1.10d * DecisionBRunComparableCost(miss))
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
            foreach (DecisionBRun r in runs)
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

            DecisionBRun added = DecisionBRunPoint(s, m, control, maxStep, cache);
            // Fixed-step controls that differ only by the grid/refinement are
            // often clipped to the same number of steps per chunk (for example
            // 424.26 s and 300 s both make two 300 s steps inside a 600 s
            // chunk). They are the same executed configuration, so do not add a
            // duplicate row for them.
            if (m.Stand != null && m.Stand.Kind == StudyKind.Fixed
                && double.IsFinite(added.HAvg))
            {
                bool sameStep = false;
                foreach (DecisionBRun r in runs)
                {
                    if (double.IsFinite(r.HAvg)
                        && Math.Abs(r.HAvg - added.HAvg)
                            <= 1e-6d * Math.Max(Math.Abs(added.HAvg), 1e-300d))
                    {
                        sameStep = true;
                        break;
                    }
                }

                if (sameStep)
                {
                    extraRuns.Add(added);
                    Console.WriteLine("    refine " + s.Label + " / " + m.Name + ": h_avg "
                        + added.HAvg.ToString("G6", CultureInfo.InvariantCulture)
                        + " s already executed; duplicate not added to the selection (kept in Runs.csv)");
                    return;
                }
            }

            runs.Add(added);
            if (added.Status != "ok" || !added.Finite)
            {
                return;
            }
        }
    }

    private static void DecisionBTimeHit(DecisionBScenario s, DecisionBMethod m, DecisionBRun hit)
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
            DecisionBRun again = m.Production
                ? DecisionBRunProduction(
                    s, m.Kernel, hit.Control, DecisionBEffectiveMaxStep(s, m),
                    s.DominantBodyOverride, true, false)
                : DecisionBRunStand(s, m, hit.Control, DecisionBEffectiveMaxStep(s, m));
            if (again.Status != "ok" || !again.Finite)
            {
                return;
            }

            times.Add(again.Ms);
            costs.Add(again.CostTotal);
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
        if (costs.Count > 1 && costs[0] != hit.CostTotal)
        {
            hit.Note = (hit.Note + "; total cost changed between repeats").Trim();
        }
    }
}
