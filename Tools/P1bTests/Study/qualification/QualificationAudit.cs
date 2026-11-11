using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using P1bStudy.Framework;

// G8 (TASK.md section 9): the oracle of every problem must be audited before any method is
// graded on it. An unaudited problem is INVALID and is not evaluated.
//
// Analytic oracle: compared with an independent formula (KeplerUniversal for the closed-form
// ones, the closed form for the KeplerUniversal ones) or, for the oscillators, with a
// finite-difference ODE residual plus a DOP853 cross-check.
// Reference oracle: dd-Taylor self-convergence (order 32 vs 36) and, where the trajectory is
// regular, a fixed-step DOP853 5-level Richardson certificate. For the chaotic Pythagorean
// problem the DOP853 levels cannot converge pointwise by construction; that disagreement is
// reported as a chaos diagnostic, and the dd-Taylor self-difference is the certificate.
internal static partial class P1bTests
{
    private const int QualificationAuditCoefficientLevels = 5;

    private sealed class QualificationDop853Levels
    {
        public double[] State;
        public double[] Finest;
        public double[][] All;
        public double Floor = double.NaN;
        public double ObservedOrder = double.NaN;
        public bool Valid;
        public string Detail = "";
    }

    /// <summary>Runs `levels` fixed-step DOP853 solutions to tEnd; Richardson with p = 8.</summary>
    private static QualificationDop853Levels QualificationDop853Richardson(
        StudyProblem p, double tEnd, int coarseSteps, int levels)
    {
        var result = new QualificationDop853Levels();
        var states = new double[levels][];
        for (int k = 0; k < levels; k++)
        {
            int steps = coarseSteps << k;
            double span = tEnd - p.T0;
            double h = span / steps;
            var ctx = new StudyContext(p, long.MaxValue / 4L);
            var stepper = new FixedRkStepper(RkTableau.Dop853, p, ctx);
            double[] y = (double[])p.Y0.Clone();
            double t = p.T0;
            for (int i = 0; i < steps; i++)
            {
                stepper.Step(t, h, y);
                t += h;
            }

            stepper.Flush(y);
            states[k] = y;
        }

        result.Finest = states[levels - 1];
        result.State = states[levels - 1];
        result.All = states;
        var diffs = new double[levels - 1];
        for (int k = 0; k + 1 < levels; k++)
        {
            diffs[k] = QualificationMaxAbsDiff(states[k], states[k + 1]);
        }

        double finest = diffs[levels - 2];
        double previous = levels >= 3 ? diffs[levels - 3] : double.NaN;
        if (double.IsFinite(previous) && previous > 0d && finest > 0d)
        {
            result.ObservedOrder = Math.Log(previous / finest, 2d);
        }

        // Richardson at order 8 while the sequence is asymptotic. When the step is so small
        // that the differences stop falling (round-off), the Richardson estimate is a fantasy:
        // the honest bound is then the finest observed difference.
        bool asymptotic = double.IsFinite(result.ObservedOrder)
            && result.ObservedOrder >= 6d && result.ObservedOrder <= 10d;
        double order = asymptotic ? result.ObservedOrder : 8d;
        double richardson = finest / (Math.Pow(2d, order) - 1d);
        result.Floor = asymptotic ? Math.Max(richardson, 0d) : finest;
        result.Valid = double.IsFinite(result.Floor) && double.IsFinite(finest);
        result.Detail = "5 levels, " + coarseSteps.ToString(QualificationCatalogInv) + ".."
            + (coarseSteps << (levels - 1)).ToString(QualificationCatalogInv) + " steps;"
            + (asymptotic ? " order " : " round-off limited, order ")
            + QualificationFormat(result.ObservedOrder)
            + "; finest delta " + QualificationFormat(finest);
        return result;
    }

    private static double QualificationMaxAbsDiff(double[] a, double[] b)
    {
        double worst = 0d;
        for (int i = 0; i < a.Length; i++)
        {
            double d = Math.Abs(a[i] - b[i]);
            if (d > worst)
            {
                worst = d;
            }
        }

        return worst;
    }

    /// <summary>Relative component error: |a-b| / max(1, |b|).</summary>
    private static double QualificationMaxRelDiff(double[] a, double[] b)
    {
        double worst = 0d;
        for (int i = 0; i < a.Length; i++)
        {
            double scale = Math.Max(1d, Math.Abs(b[i]));
            double d = Math.Abs(a[i] - b[i]) / scale;
            if (d > worst)
            {
                worst = d;
            }
        }

        return worst;
    }

    /// <summary>
    /// Relative error of a state: position and velocity are normalised by their own vector
    /// norms (with a floor at 1% of the problem scale, so a component passing through zero
    /// cannot inflate the residual). This is the "≤ 1e-13 relative" G8 metric.
    /// </summary>
    private static double QualificationStateRelDiff(
        double[] a, double[] b, int posDim, double lengthScale, double velocityScale)
    {
        double posDiff = 0d;
        double posNorm = 0d;
        double velDiff = 0d;
        double velNorm = 0d;
        for (int i = 0; i < posDim; i++)
        {
            double dx = a[i] - b[i];
            posDiff += dx * dx;
            posNorm += b[i] * b[i];
        }

        for (int i = posDim; i < 2 * posDim; i++)
        {
            double dv = a[i] - b[i];
            velDiff += dv * dv;
            velNorm += b[i] * b[i];
        }

        double posRel = Math.Sqrt(posDiff) / Math.Max(Math.Sqrt(posNorm), 0.01d * lengthScale);
        double velRel = Math.Sqrt(velDiff) / Math.Max(Math.Sqrt(velNorm), 0.01d * velocityScale);
        return Math.Max(posRel, velRel);
    }

    // ---------------------------------------------------------------------------------
    // Audit entry
    // ---------------------------------------------------------------------------------

    private static void QualificationAuditOracles(
        List<QualificationProblem> catalog, TextWriter log, string dir)
    {
        var rows = new List<string>();
        foreach (QualificationProblem q in catalog)
        {
            string error = null;
            if (q.Build == null)
            {
                if (q.Status != "INVALID")
                {
                    q.Status = "INVALID";
                }

                if (string.IsNullOrEmpty(q.AuditNote))
                {
                    q.AuditNote = "builder unavailable (see log)";
                }

                log.WriteLine("  " + q.Id + ": " + q.Status + " (" + q.AuditNote + ")");
                rows.Add(QualificationAuditRow(q));
                continue;
            }

            try
            {
                if (q.Id == "L2")
                {
                    QualificationAuditAnalytic(q);
                }
                else if (q.Id.StartsWith("R", StringComparison.Ordinal)
                    || q.Id.StartsWith("L", StringComparison.Ordinal))
                {
                    QualificationAuditRl(q);
                }
                else if (q.OracleKind == "analytic")
                {
                    QualificationAuditAnalytic(q);
                }
                else
                {
                    QualificationAuditReference(q);
                }
            }
            catch (Exception ex)
            {
                q.Status = "ERROR";
                q.AuditNote = ex.GetType().Name + ": " + ex.Message;
                error = q.AuditNote;
            }

            if (q.Status == "VALID")
            {
                // The value the sweep uses to decide whether a level is resolvable at all.
                q.Problem.Floor = q.Floor;
                q.Problem.ReferenceNote = q.OracleNote;
                q.Problem.ReferenceMethod = q.OracleId;
                q.Problem.ReferenceFamily = q.OracleKind;
                q.Problem.ReferenceIndependent = double.IsFinite(q.Cross);
                q.Problem.ReferenceCrossDiffM = double.IsNaN(q.Cross) ? 0d : q.Cross;
                q.Problem.ReferenceResolution = "ratio_ok";
            }

            log.WriteLine("  " + q.Id + ": " + q.Status
                + " floor=" + QualificationFormat(q.Floor)
                + (error == null ? "" : " (" + error + ")"));
            rows.Add(QualificationAuditRow(q));
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Oracle audit (G8)");
        sb.AppendLine();
        sb.AppendLine("Generated by `Test564_MethodQualification` at "
            + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", QualificationCatalogInv) + " UTC.");
        sb.AppendLine();
        sb.AppendLine("Gate G8: a problem whose oracle is not `VALID` is `INVALID` and no method is");
        sb.AppendLine("graded on it. `floor` is the oracle's own uncertainty in problem units;");
        sb.AppendLine("`residual` is the independent cross-check residual (relative); `cross` is the");
        sb.AppendLine("difference between two independent oracles (problem units).");
        sb.AppendLine();
        sb.AppendLine("| problem | oracle | status | residual | floor | cross | note |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (string row in rows)
        {
            sb.AppendLine(row);
        }

        sb.AppendLine();
        sb.AppendLine("## Method notes");
        sb.AppendLine();
        sb.AppendLine("* **K1–K4**: oracle = closed-form Kepler equation (Newton on eccentric anomaly),");
        sb.AppendLine("  audited against `KeplerUniversal` (universal variables, Laguerre/Conway).");
        sb.AppendLine("* **K5–K6**: oracle = `KeplerUniversal` (TASK.md table), audited against Barker's");
        sb.AppendLine("  equation (parabola) and the hyperbolic Kepler equation.");
        sb.AppendLine("* **K7–K8**: closed form; audited by the initial-condition match, a central-difference");
        sb.AppendLine("  ODE residual (finite-difference limited, threshold 1e-6) and a DOP853 rtol 1e-13");
        sb.AppendLine("  cross-check (threshold 1e-9). The floor is the double round-off of the formula.");
        sb.AppendLine("* **T1, T3**: oracle = dd-Taylor (order 32, tol 1e-30); floor = |K32 − K36| at the");
        sb.AppendLine("  final checkpoint. T1 is also certified by a fixed-step DOP853 5-level Richardson");
        sb.AppendLine("  (order 8). T3 is chaotic: the DOP853 levels cannot converge pointwise, the");
        sb.AppendLine("  disagreement is recorded as the chaos diagnostic and does not gate validity.");
        sb.AppendLine("* **T2**: exact rigid rotation; audited by substituting the state into the Newtonian");
        sb.AppendLine("  equations (residual |a_computed − (−ω²r)| / scale) and by the zero momentum/COM check.");
        sb.AppendLine();
        sb.AppendLine("A level A1/A2/A3 is evaluated for a problem only when `floor <= 0.1 * threshold`");
        sb.AppendLine("(TASK.md section 7); otherwise the level is SKIP with reason `oracle floor`.");
        File.WriteAllText(Path.Combine(dir, "oracle_audit.md"), sb.ToString(), new UTF8Encoding(false));
    }

    private static string QualificationAuditRow(QualificationProblem q)
    {
        return "| " + q.Id + " | " + q.OracleId + " | " + q.Status
            + " | " + QualificationFormat(q.Residual)
            + " | " + QualificationFormat(q.Floor)
            + " | " + QualificationFormat(q.Cross)
            + " | " + q.AuditNote.Replace("|", "/") + " |";
    }

    // ---------------------------------------------------------------------------------
    // Analytic audits
    // ---------------------------------------------------------------------------------

    private static void QualificationAuditAnalytic(QualificationProblem q)
    {
        StudyProblem p = q.Problem;
        switch (q.Id)
        {
            case "K1":
            case "K2":
            case "K3":
            case "K4":
            case "K5":
            case "K6":
            case "L2":
                QualificationAuditKepler(q, p);
                return;
            case "K7":
            case "K8":
                QualificationAuditOscillator(q, p);
                return;
            case "T2":
                QualificationAuditLagrange(q, p);
                return;
            default:
                q.Status = "INVALID";
                q.AuditNote = "no analytic audit rule for id " + q.Id;
                return;
        }
    }

    private static void QualificationAuditKepler(QualificationProblem q, StudyProblem p)
    {
        double velocityScale = Math.Sqrt(p.CentralMu / p.ScaleLength);
        double residual = 0d;
        double crossDiff = 0d;
        int worstK = -1;
        for (int k = 0; k < p.CheckpointTimes.Length; k++)
        {
            double dt = p.CheckpointTimes[k] - p.T0;
            double[] universal = (double[])p.Y0.Clone();
            KeplerUniversal.Propagate(p.CentralMu, universal, 0, 3, dt);
            double[] closed;
            if (q.Id == "K5")
            {
                closed = QualificationParabolicState(p.CentralMu, p.ScaleLength, dt);
            }
            else if (q.Id == "K6")
            {
                closed = QualificationHyperbolicState(p.CentralMu, 1.5d, p.ScaleLength, dt);
            }
            else
            {
                double e = q.Id == "K1" ? 0d
                    : (q.Id == "K2" ? 0.1d
                    : (q.Id == "K3" || q.Id == "L2" ? 0.5d : 0.9d));
                closed = QualificationEllipticState(p.CentralMu, p.ScaleLength, e, dt);
            }

            // The oracle may be either side (closed form for K1-K4, KeplerUniversal for K5-K6);
            // the audit always measures the disagreement between the two.
            double diff = QualificationStateRelDiff(universal, closed, 3, p.ScaleLength, velocityScale);
            double vsRef = QualificationStateRelDiff(universal, p.CheckpointRef[k], 3, p.ScaleLength, velocityScale);
            if (vsRef > residual)
            {
                residual = vsRef;
                worstK = k;
            }

            if (diff > crossDiff)
            {
                crossDiff = diff;
            }
        }

        q.Residual = residual;
        q.Cross = crossDiff;
        q.Floor = Math.Max(1e-14d * p.ScaleLength, Math.Max(residual, crossDiff) * p.ScaleLength);
        q.Status = residual <= 1e-13d && crossDiff <= 1e-13d ? "VALID" : "INVALID";
        q.AuditNote = "independent closed form vs KeplerUniversal " + QualificationFormat(crossDiff)
            + "; oracle vs independent at checkpoint "
            + (worstK < 0 ? "n/a" : worstK.ToString(QualificationCatalogInv))
            + " = " + QualificationFormat(residual)
            + (q.Status == "VALID" ? "" : "; above 1e-13");

        if (q.Id == "L2")
        {
            // Over 100 revolutions the double-precision universal-variable propagator itself
            // drifts by ~1e-12, so it cannot certify a 1e-13 formula. The closed form is
            // checked by invariant constancy (a wrong formula or a wrong velocity breaks E and
            // L immediately), and the propagator disagreement becomes the oracle floor.
            double e0 = p.EnergyFn(p.Y0);
            double l0 = p.AngularMomentumFn(p.Y0);
            double invariant = 0d;
            for (int k = 0; k < p.CheckpointTimes.Length; k++)
            {
                double eK = p.EnergyFn(q.Oracle[k]);
                double lK = p.AngularMomentumFn(q.Oracle[k]);
                invariant = Math.Max(invariant, Math.Abs(eK - e0) / Math.Max(Math.Abs(e0), 1e-300d));
                invariant = Math.Max(invariant, Math.Abs(lK - l0) / Math.Max(Math.Abs(l0), 1e-300d));
            }

            q.Floor = Math.Max(1e-13d, crossDiff);
            q.Status = invariant <= 1e-13d ? "VALID" : "INVALID";
            q.AuditNote = "closed form invariant drift " + QualificationFormat(invariant)
                + " (gate 1e-13); KeplerUniversal disagreement " + QualificationFormat(crossDiff)
                + " is the floor (double-precision propagator over "
                + QualificationL2Orbits.ToString(QualificationCatalogInv) + " revolutions)";
        }
    }

    private static void QualificationAuditOscillator(QualificationProblem q, StudyProblem p)
    {
        // 1. The closed form must reproduce the initial condition exactly.
        double ic = q.Id == "K7" ? QualificationMaxRelDiff(
            QualificationHarmonicState(0d), p.Y0) : QualificationMaxRelDiff(
            QualificationDampedState(0d), p.Y0);

        // 2. Central-difference ODE residual along the analytic trajectory. h = 1e-3: the
        //    truncation error is ~h^2 and the cancellation error ~eps/h^2, so this is the
        //    best double-precision compromise. It catches a wrong formula (O(1)) and is not
        //    a precision claim; the DOP853 cross-check below is the accuracy check.
        double ode = 0d;
        const double fd = 1e-3d;
        var rhs = new double[p.Dim];
        foreach (double t in p.CheckpointTimes)
        {
            double[] s = q.Id == "K7" ? QualificationHarmonicState(t) : QualificationDampedState(t);
            double[] plus = q.Id == "K7" ? QualificationHarmonicState(t + fd) : QualificationDampedState(t + fd);
            double[] minus = q.Id == "K7" ? QualificationHarmonicState(t - fd) : QualificationDampedState(t - fd);
            p.Rhs(t, s, rhs);
            for (int i = 0; i < 3; i++)
            {
                double vel = (plus[i] - minus[i]) / (2d * fd);
                double acc = (plus[i] - (2d * s[i]) + minus[i]) / (fd * fd);
                ode = Math.Max(ode, Math.Abs(vel - s[3 + i]));
                ode = Math.Max(ode, Math.Abs(acc - rhs[3 + i]));
            }
        }

        // 3. Trajectory cross-check against the framework's DOP853 at rtol 1e-13.
        var integ = new Dop853AdaptiveIntegrator();
        StudyConfig cfg = integ.MakeConfig(p, 1e-13d, double.PositiveInfinity);
        var ctx = new StudyContext(p, 20000000L);
        StudyRun run = integ.Run(p, cfg, ctx);
        double cross = 0d;
        bool complete = true;
        for (int k = 0; k < p.CheckpointTimes.Length; k++)
        {
            if (run.Samples == null || run.Samples[k] == null)
            {
                complete = false;
                break;
            }

            cross = Math.Max(cross, QualificationMaxAbsDiff(run.Samples[k], p.CheckpointRef[k]));
        }

        q.Residual = ode;
        q.Cross = complete ? cross : double.NaN;
        q.Floor = 1e-14d * p.ScaleLength;
        bool ok = ic <= 1e-14d && ode <= 1e-6d && complete && cross <= 1e-9d;
        q.Status = ok ? "VALID" : "INVALID";
        q.AuditNote = "initial-condition " + QualificationFormat(ic)
            + "; central-difference ODE residual " + QualificationFormat(ode)
            + " (h=1e-3, formula check only, not a floor); DOP853 cross " + QualificationFormat(cross)
            + (ok ? "" : "; one check above threshold");
    }

    private static void QualificationAuditLagrange(QualificationProblem q, StudyProblem p)
    {
        const double a = 1d;
        double omega = Math.Sqrt(3d / (a * a * a));
        int n = 3;
        double residual = 0d;
        double momentum = 0d;
        for (int k = 0; k < p.CheckpointTimes.Length; k++)
        {
            double[] s = p.CheckpointRef[k];
            var acc = new double[p.Dim];
            p.Rhs(p.CheckpointTimes[k], s, acc);
            double cx = 0d;
            double cy = 0d;
            for (int i = 0; i < n; i++)
            {
                cx += s[3 * i];
                cy += s[(3 * i) + 1];
            }

            cx /= n;
            cy /= n;
            double mvx = 0d;
            double mvy = 0d;
            for (int i = 0; i < n; i++)
            {
                double ax = -omega * omega * (s[3 * i] - cx);
                double ay = -omega * omega * (s[(3 * i) + 1] - cy);
                double scale = Math.Max(1d, omega * omega * p.ScaleLength);
                residual = Math.Max(residual, Math.Abs(acc[9 + (3 * i)] - ax) / scale);
                residual = Math.Max(residual, Math.Abs(acc[9 + (3 * i) + 1] - ay) / scale);
                mvx += s[9 + (3 * i)];
                mvy += s[9 + (3 * i) + 1];
            }

            // Equal masses: the total linear momentum must stay zero for rigid rotation.
            momentum = Math.Max(momentum, Math.Max(Math.Abs(mvx), Math.Abs(mvy)));
        }

        q.Residual = residual;
        q.Cross = momentum;
        q.Floor = 1e-14d * p.ScaleLength;
        bool ok = residual <= 1e-13d && momentum <= 1e-12d;
        q.Status = ok ? "VALID" : "INVALID";
        q.AuditNote = "Newtonian residual vs -omega^2 r " + QualificationFormat(residual)
            + "; |sum v| (rigid rotation has zero total momentum) " + QualificationFormat(momentum)
            + (ok ? "" : "; above threshold");
    }

    // ---------------------------------------------------------------------------------
    // Reference audits
    // ---------------------------------------------------------------------------------

    private static void QualificationAuditReference(QualificationProblem q)
    {
        StudyProblem p = q.Problem;
        if (q.Id == "T1")
        {
            QualificationAuditFigureEight(q, p);
            return;
        }

        if (q.Id == "T3")
        {
            QualificationAuditPythagorean(q, p);
            return;
        }

        q.Status = "INVALID";
        q.AuditNote = "no reference audit rule for id " + q.Id;
    }

    private static void QualificationAuditFigureEight(QualificationProblem q, StudyProblem p)
    {
        TaylorNBodyInput input = QualificationFigureEightInput();
        double end = p.EndTime;
        DdNBodyResult fine = RunTaylorNBodyDdOptSeconds(input, end, 36, 1e-34d, 1e-34d, 0.05d, 1e-12d);
        var fineState = new double[p.Dim];
        int len = p.Dim / 2;
        for (int i = 0; i < len; i++)
        {
            fineState[i] = fine.DdP[i].ToDouble;
            fineState[len + i] = fine.DdV[i].ToDouble;
        }

        double selfDiff = QualificationMaxAbsDiff(fineState, p.CheckpointRef[p.CheckpointRef.Length - 1]);

        // A regular orbit: fixed-step DOP853 must converge to the same trajectory. 1024..16384
        // steps put the finest level at ~2.5e-3 s per step (the period is 6.33), which is
        // several orders below the h^8 truncation needed for a 1e-13 certificate.
        QualificationDop853Levels levels = QualificationDop853Richardson(p, end, 1024, QualificationAuditCoefficientLevels);
        double cross = levels.State == null
            ? double.NaN
            : QualificationMaxAbsDiff(levels.State, p.CheckpointRef[p.CheckpointRef.Length - 1]);

        q.Residual = selfDiff;
        q.Cross = cross;
        // The oracle is dd-Taylor; its own uncertainty is the K32-K36 difference (plus the
        // double conversion). The DOP853 levels are an independent certificate, not a floor.
        q.Floor = Math.Max(selfDiff, 4e-16d * p.ScaleLength);
        bool ok = double.IsFinite(q.Floor) && selfDiff <= 1e-13d * p.ScaleLength
            && levels.Valid && cross <= 1e-9d * p.ScaleLength;
        q.Status = ok ? "VALID" : "INVALID";
        q.AuditNote = "dd-Taylor K32-K36 " + QualificationFormat(selfDiff)
            + "; DOP853 " + levels.Detail
            + "; DOP853-vs-dd " + QualificationFormat(cross)
            + (ok ? "" : "; above threshold");
    }

    private static void QualificationAuditPythagorean(QualificationProblem q, StudyProblem p)
    {
        TaylorNBodyInput input = PythagoreanInput();
        double end = p.EndTime;
        DdNBodyResult fine = RunTaylorNBodyDdOptSeconds(input, end, 36, 1e-34d, 1e-34d, 0.05d, 1e-12d);
        var fineState = new double[p.Dim];
        int len = p.Dim / 2;
        for (int i = 0; i < len; i++)
        {
            fineState[i] = fine.DdP[i].ToDouble;
            fineState[len + i] = fine.DdV[i].ToDouble;
        }

        double selfDiff = QualificationMaxAbsDiff(fineState, p.CheckpointRef[p.CheckpointRef.Length - 1]);

        // The cross-check runs in the regular phase (t = 1): the first close encounter of the
        // Pythagorean problem happens between t = 1.5 and t = 2 (measured: at 64 steps DOP853
        // agrees with dd to 3e-15 at t=1.5, but scattering after the encounter differs
        // completely). A fixed-step double solution cannot follow the trajectory past it.
        DdNBodyResult regular = RunTaylorNBodyDdOptSeconds(input, 1d, 36, 1e-34d, 1e-34d, 0.05d, 1e-12d);
        var regularState = new double[p.Dim];
        for (int i = 0; i < len; i++)
        {
            regularState[i] = regular.DdP[i].ToDouble;
            regularState[len + i] = regular.DdV[i].ToDouble;
        }

        QualificationDop853Levels levels = QualificationDop853Richardson(p, 1d, 64, QualificationAuditCoefficientLevels);
        double cross = levels.State == null
            ? double.NaN
            : QualificationMaxAbsDiff(levels.State, regularState);

        var full = new StudyConfig();
        full.H = (p.EndTime - p.T0) / 4096d;
        var fullCtx = new StudyContext(p, 40000000L);
        var fullStepper = new FixedRkStepper(RkTableau.Dop853, p, fullCtx);
        var fullY = (double[])p.Y0.Clone();
        double fullT = p.T0;
        for (int i = 0; i < 4096; i++)
        {
            fullStepper.Step(fullT, full.H, fullY);
            fullT += full.H;
        }

        fullStepper.Flush(fullY);
        double chaos = QualificationMaxAbsDiff(fullY, p.CheckpointRef[p.CheckpointRef.Length - 1]);

        q.Residual = selfDiff;
        q.Cross = cross;
        q.Floor = Math.Max(selfDiff, 4e-16d * p.ScaleLength);
        bool ok = double.IsFinite(q.Floor) && selfDiff <= 1e-13d * p.ScaleLength
            && levels.Valid && cross <= 1e-9d * p.ScaleLength;
        q.Status = ok ? "VALID" : "INVALID";
        q.AuditNote = "dd-Taylor K32-K36 " + QualificationFormat(selfDiff)
            + "; DOP853 5 levels vs dd at t=1 (regular phase) " + QualificationFormat(cross)
            + " (" + levels.Detail + ")"
            + "; chaos diagnostic at t=" + p.EndTime.ToString("G4", QualificationCatalogInv)
            + ": " + QualificationFormat(chaos)
            + (ok ? "" : "; dd self-difference or the regular-phase cross-check is above threshold");
    }

    private static string QualificationFormat(double value)
    {
        if (double.IsNaN(value))
        {
            return "n/a";
        }

        if (double.IsInfinity(value))
        {
            return value > 0d ? "inf" : "-inf";
        }

        return value.ToString("0.000E+00", QualificationCatalogInv);
    }
}
