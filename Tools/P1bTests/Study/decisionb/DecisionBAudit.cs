using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

// Test560b: events, validity, warp, heliocentric isolation, dominance audit.
internal static partial class P1bTests
{
    private sealed class DecisionBEventResult
    {
        public double Time = double.NaN;
        public double CostTotal;
        public string Status = "unresolved";
        public string Note = "";
    }

    private static double DecisionBEventFunction(DecisionBScenario s, Vector3d localP)
    {
        if (s.EventKind == "touchdown")
        {
            OrbitingBody earth = s.Case.Sys.AllBodies[s.Case.Central];
            return localP.Magnitude - earth.Radius;
        }

        return localP.Magnitude - s.EventLevelM;
    }

    private static DecisionBEventResult DecisionBFindEventProduction(
        DecisionBScenario s, string kernel, double rtol, double maxStep)
    {
        var result = new DecisionBEventResult();
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
                MaxStepSize = maxStep,
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
            long chunks = 0L;
            while (t < end - 1e-12d)
            {
                double chunk = Math.Min(s.ChunkAt(t), end - t);
                phys.StepWithSegments(ship, t, chunk, segments);
                t += chunk;
                chunks++;
            }
            for (int i = 0; i < segments.Count; i++)
            {
                DenseSegment seg = segments[i];
                double fa = DecisionBEventAbsolute(s, seg.Y0.Position, seg.T0);
                double fb = DecisionBEventAbsolute(s, seg.Y1.Position, seg.T1);
                bool touchdown = s.EventKind == "touchdown" && fa > 0d && fb <= 0d;
                bool soi = s.EventKind == "soi-out" && fa <= 0d && fb > 0d;
                if (touchdown || soi)
                {
                    result.Time = DecisionBBisectDense(s, seg);
                    result.Status = "ok";
                    result.CostTotal = counting.Calls + (2d * Math.Max(0L, counting.Calls - chunks));
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

    private static double DecisionBEventAbsolute(DecisionBScenario s, Vector3d absoluteP, double t)
    {
        OrbitingBody body = s.Case.Sys.AllBodies[s.Case.Central];
        s.Case.Sys.EvaluateBodyState(body, t, out Vector3d bp, out _);
        return DecisionBEventFunction(s, absoluteP - bp);
    }

    private static double DecisionBBisectDense(DecisionBScenario s, DenseSegment seg)
    {
        double lo = seg.T0;
        double hi = seg.T1;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5d * (lo + hi);
            double f = DecisionBEventAbsolute(s, seg.Evaluate(mid).Position, mid);
            if (s.EventKind == "touchdown")
            {
                if (f > 0d) { lo = mid; } else { hi = mid; }
            }
            else
            {
                if (f <= 0d) { lo = mid; } else { hi = mid; }
            }
        }

        return 0.5d * (lo + hi);
    }

    private static double DecisionBReferenceEventTime(DecisionBScenario s, double h, out double calls)
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
        pos[0] = s.Case.P0;
        int m = count - 1;
        var sampleTimes = new double[m];
        var samplePos = new Vector3d[m];
        var sampleVel = new Vector3d[m];
        Array.Copy(times, 1, sampleTimes, 0, m);
        long used;
        StudyShipRefDop853Fixed(s.Case, h, out used, sampleTimes, samplePos, sampleVel);
        calls = used;
        Array.Copy(samplePos, 0, pos, 1, m);
        double prevT = times[0];
        double prevF = DecisionBEventFunction(s, pos[0]);
        for (int i = 1; i < count; i++)
        {
            double f = DecisionBEventFunction(s, pos[i]);
            bool cross = s.EventKind == "touchdown" ? prevF > 0d && f <= 0d : prevF <= 0d && f > 0d;
            if (cross)
            {
                double frac = Math.Abs(prevF) / Math.Max(Math.Abs(prevF) + Math.Abs(f), 1e-30d);
                return prevT + ((times[i] - prevT) * frac);
            }

            prevT = times[i];
            prevF = f;
        }

        return double.NaN;
    }

    private static DecisionBEventResult DecisionBFindEventStand(
        DecisionBScenario s, DecisionBMethod m, double control, double maxStep)
    {
        var result = new DecisionBEventResult();
        try
        {
            var chunked = new DecisionBChunkedIntegrator(m.Stand, s.ChunkAt, s.ChunkMax, maxStep);
            StudyConfig cfg = chunked.MakeConfig(s.Problem, 1e-8d, double.PositiveInfinity);
            var ctx = new StudyContext(s.Problem, DecisionBRunBudgetRhs);
            var trajectory = new List<double[]>();
            var times = new List<double>();
            double[] y = (double[])s.Problem.Y0.Clone();
            double t = s.Problem.T0;
            double end = s.Problem.EndTime;
            while (t < end - 1e-12d)
            {
                double chunkEnd = Math.Min(t + s.ChunkAt(t), end);
                StudyProblem sub = s.Problem.ShallowClone();
                sub.T0 = t;
                sub.Y0 = y;
                sub.CheckpointTimes = new[] { chunkEnd };
                sub.CheckpointRef = new[] { s.Problem.CheckpointRef[0] };
                sub.Breakpoints = null;
                StudyConfig subCfg = m.Stand.MakeConfig(s.Problem, control, double.PositiveInfinity);
                subCfg.MaxStep = maxStep;
                subCfg.H0 = Math.Min(chunkEnd - t, maxStep);
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

            result.CostTotal = ctx.Calls;
            double prevT = s.Problem.T0;
            double prevF = DecisionBEventFunction(s,
                new Vector3d(s.Problem.Y0[0], s.Problem.Y0[1], s.Problem.Y0[2]));
            int bracket = -1;
            for (int i = 0; i < times.Count; i++)
            {
                double f = DecisionBEventFunction(s,
                    new Vector3d(trajectory[i][0], trajectory[i][1], trajectory[i][2]));
                bool cross = s.EventKind == "touchdown" ? prevF > 0d && f <= 0d : prevF <= 0d && f > 0d;
                if (cross)
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

            double startT = bracket == 0 ? s.Problem.T0 : times[bracket - 1];
            double[] startY = bracket == 0 ? (double[])s.Problem.Y0.Clone() : trajectory[bracket - 1];
            double endT = times[bracket];
            double lo = startT;
            double hi = endT;
            for (int i = 0; i < 50; i++)
            {
                double mid = 0.5d * (lo + hi);
                StudyProblem sub = s.Problem.ShallowClone();
                sub.T0 = startT;
                sub.Y0 = startY;
                sub.CheckpointTimes = new[] { mid };
                sub.CheckpointRef = new[] { s.Problem.CheckpointRef[0] };
                sub.Breakpoints = null;
                double refineControl = m.Stand.Kind == StudyKind.Fixed
                    ? control / 8d
                    : Math.Max(control / 100d, 1e-14d);
                StudyConfig subCfg = m.Stand.MakeConfig(s.Problem, refineControl, double.PositiveInfinity);
                subCfg.MaxStep = maxStep;
                subCfg.H0 = Math.Min(mid - startT, maxStep);
                var subCtx = new StudyContext(sub, DecisionBRunBudgetRhs);
                StudyRun part = m.Stand.Run(sub, subCfg, subCtx);
                result.CostTotal += subCtx.Calls;
                if (part.Aborted || part.Final == null)
                {
                    result.Status = "error";
                    result.Note = part.Abort;
                    return result;
                }

                double f = DecisionBEventFunction(s,
                    new Vector3d(part.Final[0], part.Final[1], part.Final[2]));
                bool still = s.EventKind == "touchdown" ? f > 0d : f <= 0d;
                if (still)
                {
                    lo = mid;
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

    // ------------------------------------------------------------------------------------
    // Validity
    // ------------------------------------------------------------------------------------

    private sealed class DecisionBValidityRow
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

    private static List<DecisionBValidityRow> DecisionBValidityChecks(
        List<DecisionBScenario> scenarios, List<DecisionBMethodResult> results,
        List<DecisionBMethod> methods, Dictionary<string, DecisionBRun> cache)
    {
        var rows = new List<DecisionBValidityRow>();
        var probe = new SpacecraftPhysics();
        rows.Add(new DecisionBValidityRow
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

        foreach (string marker in new[] { "Surface ascent", "LEO 200 km", "GEO" })
        {
            DecisionBScenario s = scenarios.Find(x => x.Label.IndexOf(marker, StringComparison.Ordinal) >= 0
                && x.Config == "production" && !x.Sensitivity);
            if (s == null)
            {
                continue;
            }

            IStudyIntegrator standDop853 = DecisionBStand("DOP853-adaptive");
            if (standDop853 == null)
            {
                continue;
            }

            var dm = new DecisionBMethod
            {
                Name = "DOP853-adaptive",
                Class = "stand-only",
                Kernel = "DOP853-adaptive",
                Stand = standDop853
            };
            double maxStep = s.MaxStep;
            DecisionBRun prod = DecisionBRunProduction(
                s, "dop853", 1e-12d, maxStep, null, true, false, 1e-6d, 1e-10d, 1e-8d);
            DecisionBRun stand = DecisionBRunStand(s, dm, 1e-12d, maxStep);
            double diff = double.NaN;
            if (prod.Finite && stand.Finite)
            {
                diff = 0d;
                for (int i = 0; i < s.Refs.CheckpointTimes.Length; i++)
                {
                    diff = Math.Max(diff, (prod.SamplesP[i] - stand.SamplesP[i]).Magnitude);
                }
            }

            double target = s.Targets[0].Value;
            rows.Add(new DecisionBValidityRow
            {
                Check = "production_parity_matched_atol_reference",
                Scenario = s.Label,
                Method = "DOP853",
                Metric = "prod_vs_stand_max_m",
                Value = diff,
                Threshold = target / 10d,
                Status = double.IsFinite(diff) && diff <= target / 10d ? "PASS" : "FAIL",
                Note = "reference; rtol=1e-12; max step " + maxStep.ToString("G6", CultureInfo.InvariantCulture)
                    + "; matched atol=1e-6/1e-10/1e-8"
            });
        }

        // DOPRI5 parity is split into the real game-default atol and the
        // matched-atol comparison used by the DOP853 reference. This separates
        // "the game default is loose" from "the local-frame controller is wrong".
        DecisionBScenario leo300 = scenarios.Find(x => x.Label.IndexOf("LEO 300 km", StringComparison.Ordinal) >= 0
            && x.Config == "production" && !x.Sensitivity);
        IStudyIntegrator standDopri5 = DecisionBStand("DOPRI5-adaptive");
        if (leo300 != null && standDopri5 != null)
        {
            var dm = new DecisionBMethod
            {
                Name = "DOPRI5-adaptive",
                Class = "stand-only",
                Kernel = "DOPRI5-adaptive",
                Stand = standDopri5
            };
            double target = leo300.Targets[0].Value;
            double maxStep = leo300.MaxStep;
            DecisionBRun prodGame = DecisionBRunProduction(
                leo300, "dopri5", 1e-12d, maxStep, leo300.DominantBodyOverride, true, false);
            DecisionBRun prodMatched = DecisionBRunProduction(
                leo300, "dopri5", 1e-12d, maxStep, leo300.DominantBodyOverride, true, false,
                1e-6d, 1e-10d, 1e-8d);
            DecisionBRun stand = DecisionBRunStand(leo300, dm, 1e-12d, maxStep);
            double diffGame = DecisionBParityDiff(leo300, prodGame, stand);
            double diffMatched = DecisionBParityDiff(leo300, prodMatched, stand);

            rows.Add(new DecisionBValidityRow
            {
                Check = "production_parity_game_default",
                Scenario = leo300.Label,
                Method = "DOPRI5",
                Metric = "prod_vs_stand_max_m",
                Value = diffGame,
                Threshold = target / 10d,
                Status = double.IsFinite(diffGame) && diffGame <= target / 10d ? "PASS" : "FINDING",
                Note = "game-default atol=1e-4/1e-8/1e-4; rtol=1e-12; max step "
                    + maxStep.ToString("G6", CultureInfo.InvariantCulture)
                    + "; production err "
                    + (prodGame.Finite ? prodGame.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                    + " m; stand DOPRI5 err "
                    + (stand.Finite ? stand.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN") + " m"
            });
            rows.Add(new DecisionBValidityRow
            {
                Check = "production_parity_matched_atol",
                Scenario = leo300.Label,
                Method = "DOPRI5",
                Metric = "prod_vs_stand_max_m",
                Value = diffMatched,
                Threshold = target / 10d,
                Status = double.IsFinite(diffMatched) && diffMatched <= target / 10d ? "PASS" : "FINDING",
                Note = "matched atol=1e-6/1e-10/1e-8; rtol=1e-12; max step "
                    + maxStep.ToString("G6", CultureInfo.InvariantCulture)
                    + "; production err "
                    + (prodMatched.Finite ? prodMatched.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                    + " m; stand DOPRI5 err "
                    + (stand.Finite ? stand.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN") + " m"
            });

            DecisionBRun prod853 = DecisionBRunProduction(
                leo300, "dop853", 1e-12d, maxStep, leo300.DominantBodyOverride, true, false,
                1e-6d, 1e-10d, 1e-8d);
            IStudyIntegrator stand853Integrator = DecisionBStand("DOP853-adaptive");
            DecisionBRun stand853 = stand853Integrator == null ? null : DecisionBRunStand(
                leo300, new DecisionBMethod
                {
                    Name = "DOP853-adaptive",
                    Class = "stand-only",
                    Kernel = "DOP853-adaptive",
                    Stand = stand853Integrator
                }, 1e-12d, maxStep);
            double diff853 = stand853 == null ? double.NaN : DecisionBParityDiff(leo300, prod853, stand853);

            rows.Add(new DecisionBValidityRow
            {
                Check = "local_frame_error_control",
                Scenario = leo300.Label,
                Method = "DOPRI5",
                Metric = "prod_minus_stand_at_matched_atol_m",
                Value = diffMatched,
                Threshold = target / 10d,
                Status = double.IsFinite(diffMatched) && diffMatched <= target / 10d ? "PASS/ATOL" : "FINDING",
                Note = "matched atol=1e-6/1e-10/1e-8; production err "
                    + (prodMatched.Finite ? prodMatched.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                    + " m; stand DOPRI5 err " + (stand.Finite ? stand.ErrMaxM.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                    + " m; matched diff " + (double.IsFinite(diffMatched) ? diffMatched.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                    + " m; game-default atol diff " + (double.IsFinite(diffGame) ? diffGame.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                    + " m; DOP853 matched reference " + (double.IsFinite(diff853)
                        ? diff853.ToString("E3", CultureInfo.InvariantCulture) : "NaN")
                    + " m; " + (double.IsFinite(diffMatched) && diffMatched > target / 10d
                        ? "matched-atol mismatch is a local-frame finding"
                        : "game-default difference is an atol effect, not a local-frame finding")
            });
        }

        DecisionBScenario surfaceParity = scenarios.Find(x => x.Label.IndexOf("Surface ascent", StringComparison.Ordinal) >= 0
            && x.Config == "production" && !x.Sensitivity);
        if (surfaceParity != null && standDopri5 != null)
        {
            var dm = new DecisionBMethod
            {
                Name = "DOPRI5-adaptive",
                Class = "stand-only",
                Kernel = "DOPRI5-adaptive",
                Stand = standDopri5
            };
            DecisionBRun prod = DecisionBRunProduction(
                surfaceParity, "dopri5", 1e-12d, surfaceParity.MaxStep,
                surfaceParity.DominantBodyOverride, true, false);
            DecisionBRun stand = DecisionBRunStand(surfaceParity, dm, 1e-12d, surfaceParity.MaxStep);
            double diff = DecisionBParityDiff(surfaceParity, prod, stand);
            double target = surfaceParity.Targets[0].Value;
            rows.Add(new DecisionBValidityRow
            {
                Check = "production_parity_game_default",
                Scenario = surfaceParity.Label,
                Method = "DOPRI5",
                Metric = "prod_vs_stand_max_m",
                Value = diff,
                Threshold = target / 10d,
                Status = double.IsFinite(diff) && diff <= target / 10d ? "PASS" : "FINDING",
                Note = "game-default atol=1e-4/1e-8/1e-4; rtol=1e-12; surface drag/thrust/variable mass; max step "
                    + surfaceParity.MaxStep.ToString("G6", CultureInfo.InvariantCulture)
            });
        }

        // Determinism: one production and one stand hit.
        DecisionBScenario leo = scenarios.Find(x => x.Label.IndexOf("LEO 200 km", StringComparison.Ordinal) >= 0
            && x.Config == "production" && !x.Sensitivity);
        if (leo != null)
        {
            DecisionBRun hit = null;
            DecisionBMethod prod = methods.Find(x => x.Name.StartsWith("DOP853 [production]", StringComparison.Ordinal));
            DecisionBMethodResult prodResult = results.Find(x => x.Scenario == leo && x.Method == prod);
            if (prod != null && prodResult != null)
            {
                hit = DecisionBCheapestHit(prodResult.Runs, leo.Targets[0].Value, "position");
                if (hit != null)
                {
                    DecisionBRun again = DecisionBRunProduction(
                        leo, "dop853", hit.Control, leo.MaxStep, null, true, false);
                    double dErr = Math.Abs(again.ErrMaxM - hit.ErrMaxM);
                    double dCost = Math.Abs(again.CostTotal - hit.CostTotal);
                    rows.Add(new DecisionBValidityRow
                    {
                        Check = "determinism",
                        Scenario = leo.Label,
                        Method = "DOP853 [production]",
                        Metric = "delta_err_max_m",
                        Value = dErr,
                        Threshold = 0d,
                        Status = dErr == 0d && dCost == 0d ? "PASS" : "FAIL",
                        Note = "cost total delta " + dCost.ToString(CultureInfo.InvariantCulture)
                    });
                }
            }
        }

        DecisionBScenario geo = scenarios.Find(x => x.Label.IndexOf("GEO", StringComparison.Ordinal) >= 0
            && x.Config == "production" && !x.Sensitivity);
        if (geo != null)
        {
            DecisionBMethod wh = methods.Find(x => x.Name == "WH-BM14A");
            DecisionBMethodResult whResult = results.Find(x => x.Scenario == geo && x.Method == wh);
            if (wh != null && whResult != null)
            {
                DecisionBRun hit = DecisionBCheapestHit(
                    whResult.Runs, geo.Targets[0].Value, "position");
                if (hit != null)
                {
                    DecisionBRun again = DecisionBRunStand(geo, wh, hit.Control, DecisionBEffectiveMaxStep(geo, wh));
                    double dErr = Math.Abs(again.ErrMaxM - hit.ErrMaxM);
                    double dCost = Math.Abs(again.CostTotal - hit.CostTotal);
                    rows.Add(new DecisionBValidityRow
                    {
                        Check = "determinism",
                        Scenario = geo.Label,
                        Method = "WH-BM14A",
                        Metric = "delta_err_max_m",
                        Value = dErr,
                        Threshold = 0d,
                        Status = dErr == 0d && dCost == 0d ? "PASS" : "FAIL",
                        Note = "cost total delta " + dCost.ToString(CultureInfo.InvariantCulture)
                    });
                }
            }
        }

        // Chunk invariance: production DOP853 on LEO 200, variable 600/0.05 vs fixed 60/0.05.
        if (leo != null)
        {
            DecisionBScenario fixedChunk = leo.CloneForChunk("chunk sweep 60 s", 60d, true);
            DecisionBRun a = DecisionBRunProduction(leo, "dop853", 1e-11d, leo.MaxStep, null, true, false);
            DecisionBRun b = DecisionBRunProduction(fixedChunk, "dop853", 1e-11d, leo.MaxStep, null, true, false);
            double diff = double.NaN;
            if (a.Finite && b.Finite)
            {
                diff = 0d;
                for (int i = 0; i < leo.Refs.CheckpointTimes.Length; i++)
                {
                    diff = Math.Max(diff, (a.SamplesP[i] - b.SamplesP[i]).Magnitude);
                }
            }

            rows.Add(new DecisionBValidityRow
            {
                Check = "chunk_invariance",
                Scenario = leo.Label,
                Method = "DOP853 [production]",
                Metric = "max_delta_600_005_vs_60_005_m",
                Value = diff,
                Threshold = 10d * leo.Targets[0].Value,
                Status = double.IsFinite(diff) && diff <= 10d * leo.Targets[0].Value ? "PASS" : "FAIL",
                Note = "same reference; production 600/0.05 vs fixed 60 s"
            });
        }

        // Surface 0.05 vs 0.5 sensitivity.
        DecisionBScenario surf = scenarios.Find(x => x.Label.IndexOf("Surface ascent", StringComparison.Ordinal) >= 0
            && x.Config == "production" && !x.Sensitivity);
        if (surf != null)
        {
            DecisionBScenario half = surf.CloneForChunk("surface chunk 0.5 s sensitivity", 0.5d, true);
            DecisionBRun a = DecisionBRunProduction(surf, "dop853", 1e-11d, surf.MaxStep, null, true, false);
            DecisionBRun b = DecisionBRunProduction(half, "dop853", 1e-11d, half.MaxStep, null, true, false);
            double diff = double.NaN;
            if (a.Finite && b.Finite)
            {
                diff = 0d;
                for (int i = 0; i < surf.Refs.CheckpointTimes.Length; i++)
                {
                    diff = Math.Max(diff, (a.SamplesP[i] - b.SamplesP[i]).Magnitude);
                }
            }

            rows.Add(new DecisionBValidityRow
            {
                Check = "chunk_invariance",
                Scenario = surf.Label,
                Method = "DOP853 [production]",
                Metric = "max_delta_005_vs_05_m",
                Value = diff,
                Threshold = 10d * surf.Targets[0].Value,
                Status = double.IsFinite(diff) && diff <= 10d * surf.Targets[0].Value ? "PASS" : "FAIL",
                Note = "0.05 s production vs 0.5 s sensitivity"
            });
        }

        return rows;
    }

    private static IStudyIntegrator DecisionBStand(string name)
    {
        List<IStudyIntegrator> all = StudyRegistry.Select(name);
        return all.Count == 1 ? all[0] : null;
    }

    private static double DecisionBParityDiff(
        DecisionBScenario s, DecisionBRun production, DecisionBRun stand)
    {
        if (!production.Finite || !stand.Finite
            || production.SamplesP == null || stand.SamplesP == null)
        {
            return double.NaN;
        }

        double diff = 0d;
        for (int i = 0; i < s.Refs.CheckpointTimes.Length; i++)
        {
            diff = Math.Max(diff, (production.SamplesP[i] - stand.SamplesP[i]).Magnitude);
        }

        return diff;
    }

    // ------------------------------------------------------------------------------------
    // Warp
    // ------------------------------------------------------------------------------------

    private sealed class DecisionBWarpRow
    {
        public string Scenario;
        public string Regime;
        public string Method;
        public double Factor;
        public double SimSeconds;
        public long Chunks;
        public double CostRhs;
        public double CostLocalFrame;
        public double CostAux;
        public double CostTotal;
        public double Ms;
        public long GcBytes;
        public string Status;
        public string Note;
    }

    private static List<DecisionBWarpRow> DecisionBWarpTable(
        List<DecisionBScenario> scenarios)
    {
        var rows = new List<DecisionBWarpRow>();
        foreach (DecisionBScenario s in scenarios)
        {
            if (s.Sensitivity || s.Config != "production")
            {
                continue;
            }

            foreach (string kernel in new[] { "dop853", "dopri5" })
            {
                foreach (double factor in new[] { 1d, 10d, 100d, 1000d })
                {
                    rows.Add(DecisionBWarpRun(s, kernel, factor));
                }
            }
        }

        return rows;
    }

    private static DecisionBWarpRow DecisionBWarpRun(DecisionBScenario s, string kernel, double factor)
    {
        var row = new DecisionBWarpRow
        {
            Scenario = s.Label,
            Regime = s.Regime,
            Method = kernel == "dop853" ? "DOP853 [production]" : "DOPRI5 [production]",
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
                MaxStepSize = s.MaxStep,
                RelativeTolerance = 1e-11d
            };
            phys.Sources.Add(counting);
            if (c.Drag != null) phys.Sources.Add(c.Drag);
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
            var times = new List<double>();
            var gc0 = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 60; frame++)
            {
                double frameEnd = t + frameSim;
                while (t < frameEnd - 1e-12d)
                {
                    double chunk = Math.Min(s.ChunkAt(t), frameEnd - t);
                    phys.Step(ship, t, chunk);
                    t += chunk;
                    chunks++;
                    if (counting.Calls > DecisionBRunBudgetRhs)
                    {
                        row.Status = "censored";
                        row.Note = "RHS budget exceeded";
                        break;
                    }
                }

                if (row.Status != "ok") break;
                times.Add(t);
            }

            row.GcBytes = GC.GetAllocatedBytesForCurrentThread() - gc0;
            long firstCalls = counting.Calls;
            // Time the same run once more for a median-ish wall clock.
            var sw = Stopwatch.StartNew();
            var ship2 = new Spacecraft(bp + c.P0, bv + c.V0, c.Mass0);
            double t2 = StudyShipT0;
            for (int frame = 0; frame < 60; frame++)
            {
                double frameEnd = t2 + frameSim;
                while (t2 < frameEnd - 1e-12d)
                {
                    double chunk = Math.Min(s.ChunkAt(t2), frameEnd - t2);
                    phys.Step(ship2, t2, chunk);
                    t2 += chunk;
                }
            }

            sw.Stop();
            row.Ms = sw.Elapsed.TotalMilliseconds;
            row.Chunks = chunks;
            row.CostRhs = firstCalls;
            long local = Math.Max(0L, firstCalls - chunks);
            row.CostLocalFrame = 2d * local;
            row.CostAux = 0d;
            row.CostTotal = row.CostRhs + row.CostLocalFrame + row.CostAux;
        }
        catch (Exception ex)
        {
            row.Status = "error";
            row.Note = ex.GetType().Name + ": " + ex.Message;
        }

        return row;
    }

    // ------------------------------------------------------------------------------------
    // Heliocentric isolation
    // ------------------------------------------------------------------------------------

    private sealed class DecisionBIsolationRow
    {
        public string Variant;
        public string Method;
        public string Dominant;
        public string UseFrame;
        public double MaxStep;
        public double Chunk;
        public double ErrMaxM;
        public double ErrFinalM;
        public double CostTotal;
        public string Status;
        public string Note;
    }

    private static List<DecisionBIsolationRow> DecisionBHeliocentricIsolation(
        List<DecisionBScenario> scenarios)
    {
        var rows = new List<DecisionBIsolationRow>();
        DecisionBScenario s = scenarios.Find(x => x.Label.IndexOf("5 AU", StringComparison.Ordinal) >= 0
            && x.Config == "auto-bug");
        if (s == null)
        {
            return rows;
        }

        OrbitingBody sun = s.Case.Sys.AllBodies[0];
        IStudyIntegrator standDop853 = DecisionBStand("DOP853-adaptive");
        DecisionBScenario clone86400 = s.CloneForConfig(
            "isolation86400", 86400d, t => 86400d, 86400d, "86 400 s", true);

        void Add(string variant, string method, OrbitingBody dominant, bool useFrame,
            double maxStep, double chunk, DecisionBRun run)
        {
            double err = run.Finite ? run.ErrMaxM : double.NaN;
            rows.Add(new DecisionBIsolationRow
            {
                Variant = variant,
                Method = method,
                Dominant = dominant == null ? "auto" : dominant.Name,
                UseFrame = useFrame ? "local" : "absolute",
                MaxStep = maxStep,
                Chunk = chunk,
                ErrMaxM = err,
                ErrFinalM = run.Finite ? run.ErrFinalM : double.NaN,
                CostTotal = run.CostTotal,
                Status = run.Finite ? "ok" : run.Status,
                Note = run.Note
            });
        }

        Add("auto", "DOP853 [production]", null, true, 600d, 600d,
            DecisionBRunProduction(s, "dop853", 1e-11d, 600d, null, true, false));
        Add("forced-sun", "DOP853 [production]", sun, true, 600d, 600d,
            DecisionBRunProduction(s, "dop853", 1e-11d, 600d, sun, true, false));
        Add("absolute", "DOP853 [production]", null, false, 600d, 600d,
            DecisionBRunProduction(s, "dop853", 1e-11d, 600d, null, false, false));
        Add("forced-sun-86400", "DOP853 [production]", sun, true, 86400d, 86400d,
            DecisionBRunProduction(clone86400, "dop853", 1e-11d, 86400d, sun, true, false));

        if (standDop853 != null)
        {
            var dm = new DecisionBMethod
            {
                Name = "DOP853-adaptive",
                Class = "stand-only",
                Kernel = "DOP853-adaptive",
                Stand = standDop853
            };
            Add("stand-600", "DOP853-adaptive", sun, true, 600d, 600d,
                DecisionBRunStand(s, dm, 1e-11d, 600d));
            Add("stand-86400", "DOP853-adaptive", sun, true, 86400d, 86400d,
                DecisionBRunStand(clone86400, dm, 1e-11d, 86400d));
        }

        return rows;
    }

    // ------------------------------------------------------------------------------------
    // Dominance audit
    // ------------------------------------------------------------------------------------

    private sealed class DecisionBDominanceRow
    {
        public string Check;
        public string Regime;
        public string Scenario;
        public string Target;
        public string Metric;
        public string Winner;
        public string Second;
        public double Value;
        public string Note;
    }

    private static DecisionBMatrixRow DecisionBBestRow(
        List<DecisionBMatrixRow> rows, Func<DecisionBMatrixRow, double> cost)
    {
        DecisionBMatrixRow bestOk = null;
        double bestOkCost = double.PositiveInfinity;
        DecisionBMatrixRow bestEdge = null;
        double bestEdgeCost = double.PositiveInfinity;
        foreach (DecisionBMatrixRow r in rows)
        {
            double err = DecisionBRowError(r);
            if (r.Status == "ok" && err <= 0.8d * r.Target && cost(r) < bestOkCost)
            {
                bestOkCost = cost(r);
                bestOk = r;
            }
            else if (r.Status == "edge" && err <= r.Target && cost(r) < bestEdgeCost)
            {
                bestEdgeCost = cost(r);
                bestEdge = r;
            }
        }

        return bestOk ?? bestEdge;
    }

    private static List<DecisionBDominanceRow> DecisionBDominanceAudit(
        List<DecisionBMatrixRow> matrix, List<DecisionBScenario> scenarios,
        List<DecisionBMethodResult> results, List<DecisionBMethod> methods,
        Dictionary<string, DecisionBRun> cache)
    {
        var rows = new List<DecisionBDominanceRow>();
        var cells = new Dictionary<string, List<DecisionBMatrixRow>>(StringComparer.Ordinal);
        foreach (DecisionBMatrixRow r in matrix)
        {
            if (r.Sensitivity || r.Config != "production")
            {
                continue;
            }

            if (double.IsFinite(r.ConfigMaxStep) && double.IsFinite(r.MaxStep)
                && Math.Abs(r.MaxStep - r.ConfigMaxStep) > 1e-9d)
            {
                continue;
            }

            string key = r.Scenario + "|" + r.Target.ToString("R", CultureInfo.InvariantCulture) + "|" + r.TargetKind;
            List<DecisionBMatrixRow> list;
            if (!cells.TryGetValue(key, out list))
            {
                list = new List<DecisionBMatrixRow>();
                cells[key] = list;
            }

            list.Add(r);
        }

        var winCount = new Dictionary<string, int>(StringComparer.Ordinal);
        int cellCount = 0;
        int costChanges = 0;
        int msChanges = 0;
        int rhsChanges = 0;
        int portedChanges = 0;
        int targetChanges = 0;
        int targetCells = 0;
        int chunkChanges = 0;
        foreach (var kv in cells)
        {
            List<DecisionBMatrixRow> list = kv.Value;
            DecisionBMatrixRow total = DecisionBBestRow(list, r => r.CostTotal);
            DecisionBMatrixRow rhs = DecisionBBestRow(list, r => r.CostRhs);
            DecisionBMatrixRow ms = DecisionBBestRow(list, r => double.IsFinite(r.MsMedian) ? r.MsMedian : r.CostMs);
            DecisionBMatrixRow ported = DecisionBBestRow(list, r => DecisionBComparableCost(r));
            if (total == null)
            {
                continue;
            }

            cellCount++;
            int n;
            winCount.TryGetValue(total.Method, out n);
            winCount[total.Method] = n + 1;
            if (rhs != null && rhs.Method != total.Method) rhsChanges++;
            if (ms != null && ms.Method != total.Method) msChanges++;
            if (ported != null && ported.Method != total.Method) portedChanges++;
            // cost_total vs cost_rhs is the central metric check; count changes below.
            if (rhs != null && rhs.Method != total.Method) costChanges++;

            DecisionBMatrixRow second = list.FindAll(r => r != total
                && (r.Status == "ok" || r.Status == "edge"))
                .OrderBy(r => r.CostTotal).FirstOrDefault();
            rows.Add(new DecisionBDominanceRow
            {
                Check = "cell_winner",
                Regime = total.Regime,
                Scenario = total.Scenario,
                Target = DecisionBFormatTarget(total.Target, total.TargetKind),
                Metric = "cost_total",
                Winner = total.Method,
                Second = second == null ? "-" : second.Method,
                Value = total.CostTotal,
                Note = "status " + total.Status + "; err " + DecisionBRowError(total).ToString("E3", CultureInfo.InvariantCulture)
            });

            // Target sweep from executed runs only.
            foreach (double mult in new[] { 0.1d, 1d, 10d })
            {
                double target = total.Target * mult;
                DecisionBMatrixRow best = null;
                double bestCost = double.PositiveInfinity;
                foreach (DecisionBMatrixRow r in list)
                {
                    DecisionBMethodResult mr = results.Find(x => x.Scenario.Label == r.Scenario
                        && x.Method.Name == r.Method);
                    if (mr == null)
                    {
                        continue;
                    }

                    DecisionBRun hit = DecisionBCheapestHit(mr.Runs, target, r.TargetKind);
                    if (hit != null && hit.CostTotal < bestCost)
                    {
                        bestCost = hit.CostTotal;
                        best = r;
                    }
                }

                targetCells++;
                if (best != null && best.Method != total.Method)
                {
                    targetChanges++;
                }

                rows.Add(new DecisionBDominanceRow
                {
                    Check = "target_sweep",
                    Regime = total.Regime,
                    Scenario = total.Scenario,
                    Target = DecisionBFormatTarget(target, total.TargetKind),
                    Metric = "cost_total",
                    Winner = best == null ? "unresolved" : best.Method,
                    Second = "-",
                    Value = best == null ? double.NaN : bestCost,
                    Note = mult.ToString("G3", CultureInfo.InvariantCulture) + "x budget; executed runs only"
                });
            }
        }

        string top = "-";
        int topCount = 0;
        foreach (var kv in winCount)
        {
            if (kv.Value > topCount)
            {
                top = kv.Key;
                topCount = kv.Value;
            }
        }

        rows.Add(new DecisionBDominanceRow
        {
            Check = "dominance_count",
            Regime = "all",
            Scenario = "all production cells",
            Target = "-",
            Metric = "cost_total",
            Winner = top,
            Second = "-",
            Value = cellCount > 0 ? (double)topCount / cellCount : 0d,
            Note = topCount.ToString(CultureInfo.InvariantCulture) + " / "
                + cellCount.ToString(CultureInfo.InvariantCulture) + " cells"
        });

        rows.Add(new DecisionBDominanceRow
        {
            Check = "cost_metric",
            Regime = "all",
            Scenario = "all production cells",
            Target = "-",
            Metric = "cost_rhs",
            Winner = rhsChanges > 0 ? "changes" : top,
            Second = "-",
            Value = rhsChanges,
            Note = rhsChanges.ToString(CultureInfo.InvariantCulture) + " / "
                + cellCount.ToString(CultureInfo.InvariantCulture)
                + " cells where the cost_rhs winner differs from cost_total"
        });
        rows.Add(new DecisionBDominanceRow
        {
            Check = "cost_metric",
            Regime = "all",
            Scenario = "all production cells",
            Target = "-",
            Metric = "ms",
            Winner = msChanges > 0 ? "changes" : top,
            Second = "-",
            Value = msChanges,
            Note = msChanges.ToString(CultureInfo.InvariantCulture) + " / "
                + cellCount.ToString(CultureInfo.InvariantCulture)
                + " cells where the wall-clock winner differs from cost_total"
        });
        rows.Add(new DecisionBDominanceRow
        {
            Check = "cost_metric",
            Regime = "all",
            Scenario = "all production cells",
            Target = "-",
            Metric = "cost_if_ported",
            Winner = portedChanges > 0 ? "changes" : top,
            Second = "-",
            Value = portedChanges,
            Note = portedChanges.ToString(CultureInfo.InvariantCulture) + " / "
                + cellCount.ToString(CultureInfo.InvariantCulture)
                + " cells where the cost_if_ported winner differs from cost_total"
        });

        // Chunk sweep on representative cells: the winner is re-run with explicit
        // constant chunks; this is a real run, not a re-price.
        string[] sweepScenarios = { "Surface ascent", "LEO 200 km", "Far flight" };
        foreach (string marker in sweepScenarios)
        {
            DecisionBScenario baseScenario = scenarios.Find(x => x.Label.IndexOf(marker, StringComparison.Ordinal) >= 0
                && x.Config == "production" && !x.Sensitivity);
            if (baseScenario == null)
            {
                continue;
            }

            List<string> topMethods = winCount.OrderByDescending(kv => kv.Value)
                .Take(3).Select(kv => kv.Key).ToList();
            var chunkWinners = new List<string>();
            double minChunk = baseScenario.Regime == "surface" ? 0.05d
                : (baseScenario.Regime == "leo" ? 0.05d : 600d);
            foreach (double chunk in new[] { 0.05d, 0.5d, 60d, 600d, 86400d })
            {
                if (chunk < minChunk || chunk > baseScenario.Case.Seconds / 4d)
                {
                    continue;
                }

                DecisionBScenario clone = baseScenario.CloneForChunk(
                    "dominance chunk " + chunk.ToString("G6", CultureInfo.InvariantCulture), chunk, true);
                DecisionBMatrixRow best = null;
                double bestCost = double.PositiveInfinity;
                string bestName = "unresolved";
                foreach (string methodName in topMethods)
                {
                    DecisionBMethod m = methods.Find(x => x.Name == methodName);
                    DecisionBMatrixRow cell = matrix.Find(x => x.Scenario == baseScenario.Label
                        && x.Method == methodName && x.Config == "production"
                        && x.Target == baseScenario.Targets[0].Value);
                    if (m == null || cell == null || string.IsNullOrEmpty(cell.Control))
                    {
                        continue;
                    }

                    double control = DecisionBParseDouble(cell.Control);
                    double maxStep = DecisionBEffectiveMaxStep(clone, m);
                    DecisionBRun run = DecisionBRunPoint(clone, m, control, maxStep, cache);
                    if (run.Status == "ok" && run.Finite
                        && run.ErrMaxM <= baseScenario.Targets[0].Value
                        && run.CostTotal < bestCost)
                    {
                        bestCost = run.CostTotal;
                        bestName = methodName;
                    }
                }

                rows.Add(new DecisionBDominanceRow
                {
                    Check = "chunk_sweep",
                    Regime = baseScenario.Regime,
                    Scenario = baseScenario.Label,
                    Target = DecisionBFormatTarget(baseScenario.Targets[0].Value, baseScenario.Targets[0].Kind),
                    Metric = "cost_total",
                    Winner = bestName,
                    Second = "-",
                    Value = double.IsFinite(bestCost) ? bestCost : double.NaN,
                    Note = "constant chunk " + chunk.ToString("G6", CultureInfo.InvariantCulture)
                        + " s; top methods only"
                });
                chunkWinners.Add(bestName);
            }

            if (chunkWinners.Distinct().Count() > 1)
            {
                chunkChanges++;
            }
        }

        // Production-only dominance: the first Test560 compared only production
        // methods, so this is the direct answer to "why DOPRI5 won almost everywhere".
        int prodCells = 0;
        int prodTopCount = 0;
        string prodTop = "-";
        var prodWins = new Dictionary<string, int>(StringComparer.Ordinal);
        int prodRhsChanges = 0;
        int prodMsChanges = 0;
        int prodTargetChanges = 0;
        foreach (var kv in cells)
        {
            List<DecisionBMatrixRow> prod = kv.Value.FindAll(r => r.MethodClass == "production");
            DecisionBMatrixRow pt = DecisionBBestRow(prod, r => r.CostTotal);
            if (pt == null)
            {
                continue;
            }

            prodCells++;
            int pn;
            prodWins.TryGetValue(pt.Method, out pn);
            prodWins[pt.Method] = pn + 1;
            DecisionBMatrixRow prhs = DecisionBBestRow(prod, r => r.CostRhs);
            DecisionBMatrixRow pms = DecisionBBestRow(prod, r => double.IsFinite(r.MsMedian) ? r.MsMedian : r.CostMs);
            if (prhs != null && prhs.Method != pt.Method) prodRhsChanges++;
            if (pms != null && pms.Method != pt.Method) prodMsChanges++;

            foreach (double mult in new[] { 0.1d, 1d, 10d })
            {
                double target = pt.Target * mult;
                DecisionBMatrixRow best = null;
                double bestCost = double.PositiveInfinity;
                foreach (DecisionBMatrixRow r in prod)
                {
                    DecisionBMethodResult mr = results.Find(x => x.Scenario.Label == r.Scenario
                        && x.Method.Name == r.Method);
                    if (mr == null)
                    {
                        continue;
                    }

                    DecisionBRun hit = DecisionBCheapestHit(mr.Runs, target, r.TargetKind);
                    if (hit != null && hit.CostTotal < bestCost)
                    {
                        bestCost = hit.CostTotal;
                        best = r;
                    }
                }

                if (best != null && best.Method != pt.Method) prodTargetChanges++;
                rows.Add(new DecisionBDominanceRow
                {
                    Check = "production_target_sweep",
                    Regime = pt.Regime,
                    Scenario = pt.Scenario,
                    Target = DecisionBFormatTarget(target, pt.TargetKind),
                    Metric = "cost_total",
                    Winner = best == null ? "unresolved" : best.Method,
                    Second = "-",
                    Value = best == null ? double.NaN : bestCost,
                    Note = mult.ToString("G3", CultureInfo.InvariantCulture) + "x budget; production methods only"
                });
            }
        }

        foreach (var kv in prodWins)
        {
            if (kv.Value > prodTopCount)
            {
                prodTop = kv.Key;
                prodTopCount = kv.Value;
            }
        }

        int prodChunkChanges = 0;
        foreach (string marker in new[] { "LEO 200 km", "Far flight" })
        {
            DecisionBScenario baseScenario = scenarios.Find(x => x.Label.IndexOf(marker, StringComparison.Ordinal) >= 0
                && x.Config == "production" && !x.Sensitivity);
            if (baseScenario == null)
            {
                continue;
            }

            var winners = new List<string>();
            double minChunk = baseScenario.Regime == "leo" ? 0.05d : 600d;
            foreach (double chunk in new[] { 0.05d, 0.5d, 60d, 600d, 86400d })
            {
                if (chunk < minChunk || chunk > baseScenario.Case.Seconds / 4d)
                {
                    continue;
                }

                DecisionBScenario clone = baseScenario.CloneForChunk(
                    "production chunk " + chunk.ToString("G6", CultureInfo.InvariantCulture), chunk, true);
                string bestName = "unresolved";
                double bestCost = double.PositiveInfinity;
                foreach (string methodName in new[] { "DOP853 [production]", "DOPRI5 [production]" })
                {
                    DecisionBMethod m = methods.Find(x => x.Name == methodName);
                    DecisionBMatrixRow cell = matrix.Find(x => x.Scenario == baseScenario.Label
                        && x.Config == "production" && x.Method == methodName
                        && x.Target == baseScenario.Targets[0].Value);
                    if (m == null || cell == null || string.IsNullOrEmpty(cell.Control))
                    {
                        continue;
                    }

                    DecisionBRun run = DecisionBRunPoint(
                        clone, m, DecisionBParseDouble(cell.Control),
                        DecisionBEffectiveMaxStep(clone, m), cache);
                    if (run.Status == "ok" && run.Finite
                        && run.ErrMaxM <= baseScenario.Targets[0].Value
                        && run.CostTotal < bestCost)
                    {
                        bestCost = run.CostTotal;
                        bestName = methodName;
                    }
                }

                winners.Add(bestName);
                rows.Add(new DecisionBDominanceRow
                {
                    Check = "production_chunk_sweep",
                    Regime = baseScenario.Regime,
                    Scenario = baseScenario.Label,
                    Target = DecisionBFormatTarget(baseScenario.Targets[0].Value, baseScenario.Targets[0].Kind),
                    Metric = "cost_total",
                    Winner = bestName,
                    Second = "-",
                    Value = double.IsFinite(bestCost) ? bestCost : double.NaN,
                    Note = "constant chunk " + chunk.ToString("G6", CultureInfo.InvariantCulture)
                        + " s; production DOP853/DOPRI5 only"
                });
            }

            if (winners.Distinct().Count() > 1)
            {
                prodChunkChanges++;
            }
        }

        rows.Add(new DecisionBDominanceRow
        {
            Check = "production_dominance_count",
            Regime = "all",
            Scenario = "production cells",
            Target = "-",
            Metric = "cost_total",
            Winner = prodTop,
            Second = "-",
            Value = prodCells > 0 ? (double)prodTopCount / prodCells : 0d,
            Note = prodTopCount.ToString(CultureInfo.InvariantCulture) + " / "
                + prodCells.ToString(CultureInfo.InvariantCulture) + " production cells"
        });

        rows.Add(new DecisionBDominanceRow
        {
            Check = "production_cost_metric_rhs",
            Regime = "all",
            Scenario = "production cells",
            Target = "-",
            Metric = "cost_rhs",
            Winner = prodRhsChanges > 0 ? "changes" : prodTop,
            Second = "-",
            Value = prodRhsChanges,
            Note = prodRhsChanges.ToString(CultureInfo.InvariantCulture) + " / "
                + prodCells.ToString(CultureInfo.InvariantCulture)
                + " production cells where the cost_rhs winner differs from cost_total"
        });
        rows.Add(new DecisionBDominanceRow
        {
            Check = "production_cost_metric_ms",
            Regime = "all",
            Scenario = "production cells",
            Target = "-",
            Metric = "ms",
            Winner = prodMsChanges > 0 ? "changes" : prodTop,
            Second = "-",
            Value = prodMsChanges,
            Note = prodMsChanges.ToString(CultureInfo.InvariantCulture) + " / "
                + prodCells.ToString(CultureInfo.InvariantCulture)
                + " production cells where the wall-clock winner differs from cost_total"
        });
        rows.Add(new DecisionBDominanceRow
        {
            Check = "production_cost_metric_target",
            Regime = "all",
            Scenario = "production cells",
            Target = "-",
            Metric = "target",
            Winner = prodTargetChanges > 0 ? "changes" : prodTop,
            Second = "-",
            Value = prodTargetChanges,
            Note = prodTargetChanges.ToString(CultureInfo.InvariantCulture)
                + " target cells where the production winner changes; see production_target_sweep"
        });

        rows.Add(new DecisionBDominanceRow
        {
            Check = "production_verdict",
            Regime = "all",
            Scenario = "production cells",
            Target = "-",
            Metric = "cost_total",
            Winner = prodTop,
            Second = "-",
            Value = prodCells > 0 ? (double)prodTopCount / prodCells : 0d,
            Note = prodCells > 0
                ? "production: " + prodTop + " wins " + prodTopCount + "/" + prodCells
                  + " at 1x cost_total; chunk-bound (600 s production cap; target changes "
                  + prodTargetChanges + ", chunk groups " + prodChunkChanges + "); cost_rhs changes "
                  + prodRhsChanges + ", wall-clock changes " + prodMsChanges
                : "no production method resolved a production cell"
        });

        bool dominated = cellCount > 0 && topCount > 0.6d * cellCount;
        string verdict;
        if (!dominated)
        {
            verdict = "all-method: no method wins more than 60% of cells (top "
                + top + " " + topCount + "/" + cellCount + ")";
        }
        else
        {
            verdict = "all-method: " + top + " wins " + topCount + "/" + cellCount
                + " raw cost_total; target/chunk-dependent (target changes " + targetChanges
                + ", chunk groups " + chunkChanges + "); cost_rhs changes " + rhsChanges
                + ", wall-clock changes " + msChanges
                + ", cost_if_ported changes " + portedChanges
                + ". Raw stand cost is not the production comparison; see cost_if_ported.";
        }

        rows.Add(new DecisionBDominanceRow
        {
            Check = "verdict",
            Regime = "all",
            Scenario = "all production cells",
            Target = "-",
            Metric = "cost_total",
            Winner = top,
            Second = "-",
            Value = cellCount > 0 ? (double)topCount / cellCount : 0d,
            Note = verdict
        });
        return rows;
    }

    private static void DecisionBWriteDominanceCsv(string path, List<DecisionBDominanceRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("check,regime,scenario,target,metric,winner,second,value,note");
        foreach (DecisionBDominanceRow r in rows)
        {
            sb.Append(DecisionBCsvField(r.Check)).Append(',')
                .Append(DecisionBCsvField(r.Regime)).Append(',')
                .Append(DecisionBCsvField(r.Scenario)).Append(',')
                .Append(DecisionBCsvField(r.Target)).Append(',')
                .Append(DecisionBCsvField(r.Metric)).Append(',')
                .Append(DecisionBCsvField(r.Winner)).Append(',')
                .Append(DecisionBCsvField(r.Second)).Append(',')
                .Append(DecisionBCsvNumber(r.Value)).Append(',')
                .Append(DecisionBCsvField(r.Note)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void DecisionBWriteIsolationCsv(string path, List<DecisionBIsolationRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("variant,method,dominant,use_frame,max_step,chunk,err_max_m,err_final_m,cost_total,status,note");
        foreach (DecisionBIsolationRow r in rows)
        {
            sb.Append(DecisionBCsvField(r.Variant)).Append(',')
                .Append(DecisionBCsvField(r.Method)).Append(',')
                .Append(DecisionBCsvField(r.Dominant)).Append(',')
                .Append(DecisionBCsvField(r.UseFrame)).Append(',')
                .Append(DecisionBCsvNumber(r.MaxStep)).Append(',')
                .Append(DecisionBCsvNumber(r.Chunk)).Append(',')
                .Append(DecisionBCsvNumber(r.ErrMaxM)).Append(',')
                .Append(DecisionBCsvNumber(r.ErrFinalM)).Append(',')
                .Append(DecisionBCsvNumber(r.CostTotal)).Append(',')
                .Append(DecisionBCsvField(r.Status)).Append(',')
                .Append(DecisionBCsvField(r.Note)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
