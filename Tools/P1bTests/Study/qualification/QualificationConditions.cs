using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Galilego.Core;
using P1bStudy.Framework;

// TASK.md Appendix A, A8: conditions.csv — one row per catalogue problem.
//
// Every number is computed: extrema are taken from the oracle checkpoints and the initial
// state (the reference trajectory the run actually grades against), and for a central-field
// problem the osculating conic of the initial state adds the periapsis/apoapsis extremes the
// sparse checkpoint grid could miss. No value is typed by hand (section 14 rule 7).
internal static partial class P1bTests
{
    private static void QualificationWriteConditions(
        string dir, List<QualificationProblem> catalog, TextWriter log)
    {
        List<QualificationMatrixEntry> matrix = QualificationReadMatrix(dir);
        string path = Path.Combine(dir, "conditions.csv");
        bool recondition = string.Equals(
            Environment.GetEnvironmentVariable("P1B564_RECONDITION"), "1", StringComparison.Ordinal);
        if (!recondition && File.Exists(path) && QualificationConditionsComplete(path, catalog))
        {
            log.WriteLine("conditions: conditions.csv already covers the catalogue, reused"
                + " (set P1B564_RECONDITION=1 to recompute)");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("problem_id,class_tags,central_body,r_min_m,r_max_m,v_min_ms,v_max_ms,"
            + "horizon_s,forces,velocity_dependent,oracle_id,oracle_floor_m,targets_evaluated,"
            + "epoch_window,n_bodies,model");

        int rows = 0;
        foreach (QualificationProblem q in catalog)
        {
            string centralBody = q.CentralBody;
            double rMin = double.NaN;
            double rMax = double.NaN;
            double vMin = double.NaN;
            double vMax = double.NaN;
            double horizon = double.NaN;
            bool velocityDependent = false;

            if (q.Build != null)
            {
                try
                {
                    StudyProblem p = q.Problem;
                    horizon = p.Duration;
                    if (string.IsNullOrEmpty(centralBody))
                    {
                        centralBody = p.CentralMu > 0d
                            ? "central body (mu=" + p.CentralMu.ToString("E6", QualificationCatalogInv) + ")"
                            : "frame origin (no central body)";
                    }

                    QualificationConditionsExtrema(p, out rMin, out rMax, out vMin, out vMax);
                    bool massDependent;
                    velocityDependent = QualificationForceDependsOnVelocity(p, out massDependent)
                        || massDependent;
                }
                catch (Exception ex)
                {
                    log.WriteLine("conditions: " + q.Id + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            var fields = new List<string>();
            fields.Add(q.Id);
            fields.Add(string.Join(";", q.Tags));
            fields.Add(centralBody);
            fields.Add(QualificationConditionsNumber(rMin));
            fields.Add(QualificationConditionsNumber(rMax));
            fields.Add(QualificationConditionsNumber(vMin));
            fields.Add(QualificationConditionsNumber(vMax));
            fields.Add(QualificationConditionsNumber(horizon));
            fields.Add(q.Forces.Length == 0 ? "none" : string.Join(";", q.Forces));
            fields.Add(velocityDependent ? "1" : "0");            fields.Add(q.OracleId);
            fields.Add(QualificationConditionsNumber(q.Floor));
            fields.Add(QualificationConditionsTargets(matrix, q));
            // Appendix B, B7: H-tier columns. Empty for the other tiers.
            fields.Add(q.EpochWindow);
            fields.Add(q.NBodies > 0 ? q.NBodies.ToString(QualificationCatalogInv) : "");
            fields.Add(q.ModelName);

            foreach (string field in fields)
            {
                sb.Append(QualificationCsvField(field)).Append(',');
            }

            sb.Length -= 1;
            sb.AppendLine();
            rows++;
        }

        File.WriteAllText(Path.Combine(dir, "conditions.csv"), sb.ToString(), new UTF8Encoding(false));
        log.WriteLine("conditions: conditions.csv (" + rows.ToString(QualificationCatalogInv)
            + " problem row(s))");
    }

    /// <summary>True when the existing conditions.csv already has a complete row for every catalogue id.</summary>
    private static bool QualificationConditionsComplete(string path, List<QualificationProblem> catalog)
    {
        List<string[]> rows = QualificationReadCsv(path);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < rows.Count; i++)
        {
            string[] f = rows[i];
            // A truncated/empty row is not a complete one: require the 16 columns (13 from A8
            // plus the three H columns of B7) and a finite r_min / v_max. Otherwise a broken
            // file would be reused forever.
            if (f.Length >= 16
                && double.IsFinite(QualificationParseDouble(f[3]))
                && double.IsFinite(QualificationParseDouble(f[6])))
            {
                ids.Add(f[0]);
            }
        }

        foreach (QualificationProblem q in catalog)
        {
            if (!ids.Contains(q.Id))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Goals of a problem that are actually evaluated (PASS/FAIL/ERROR) in this run.</summary>
    private static string QualificationConditionsTargets(
        List<QualificationMatrixEntry> matrix, QualificationProblem q)
    {
        var names = new List<string>();
        if (q.AnalyticLevels)
        {
            foreach (string level in QualificationLevelNames)
            {
                names.Add(level);
            }
        }

        if (q.MeterTargets != null)
        {
            foreach (double target in q.MeterTargets)
            {
                if (target > 0d && double.IsFinite(target))
                {
                    names.Add(QualificationMeterGoalName(target));
                }
            }
        }

        // Appendix B, B6: Q goals are goals too; report the ones that were graded.
        if (q.QTargets != null)
        {
            foreach (double factor in q.QTargets)
            {
                if (factor > 0d && double.IsFinite(factor))
                {
                    names.Add("Q" + (factor >= 0.05d ? "10" : "1"));
                }
            }
        }

        var evaluated = new List<string>();
        foreach (string name in names)
        {
            if (QualificationLevelEvaluated(matrix, q.Id, name))
            {
                evaluated.Add(name);
            }
        }

        return string.Join(";", evaluated);
    }

    /// <summary>
    /// Min/max distance and speed over the oracle checkpoints plus the initial state. For one
    /// particle in a central field the osculating conic of the initial state adds the periapsis
    /// and (if bound) apoapsis extremes. Multi-body problems use each body's distance from the
    /// frame origin (the barycentre by construction) and its speed.
    /// </summary>
    private static void QualificationConditionsExtrema(
        StudyProblem p, out double rMin, out double rMax, out double vMin, out double vMax)
    {
        rMin = double.NaN;
        rMax = double.NaN;
        vMin = double.NaN;
        vMax = double.NaN;
        if (p.Y0 == null || p.PosDim < 3)
        {
            return;
        }
        var states = new List<double[]>();
        states.Add(p.Y0);
        if (p.CheckpointRef != null)
        {
            foreach (double[] state in p.CheckpointRef)
            {
                if (state != null)
                {
                    states.Add(state);
                }
            }
        }

        bool central = p.CentralMu > 0d;
        int bodies = p.PosDim / 3;
        bool hasVelocity = 2 * p.PosDim <= p.Dim;
        foreach (double[] state in states)
        {
            if (state.Length < p.PosDim)
            {
                continue;
            }

            if (central)
            {
                QualificationConditionsSample(state, 0, hasVelocity, p.PosDim,
                    ref rMin, ref rMax, ref vMin, ref vMax);
            }
            else
            {
                for (int b = 0; b < bodies; b++)
                {
                    QualificationConditionsSample(state, 3 * b, hasVelocity, p.PosDim,
                        ref rMin, ref rMax, ref vMin, ref vMax);
                }
            }
        }

        // A checkpoint grid is 8-256 points; on S/P/F that is not enough for a periapsis or
        // for the peaks of an oscillator. A dense fixed-step DOP853 pass over the model gives
        // real extrema of the reference trajectory (the same RHS the methods integrate).
        QualificationConditionsDense(p, ref rMin, ref rMax, ref vMin, ref vMax);

        if (central && hasVelocity && bodies == 1)
        {
            var position = new Vector3d(p.Y0[0], p.Y0[1], p.Y0[2]);
            var velocity = new Vector3d(p.Y0[3], p.Y0[4], p.Y0[5]);
            OrbitalElements elements = OrbitalElements.FromState(position, velocity, p.CentralMu);
            if (elements.IsValid && double.IsFinite(elements.SemiMajorAxis))
            {
                double a = elements.SemiMajorAxis;
                double rp = elements.PeriapsisDistance;
                if (rp > 0d && double.IsFinite(rp))
                {
                    rMin = QualificationConditionsMin(rMin, rp);
                    vMax = QualificationConditionsMax(vMax,
                        QualificationConditionsVisViva(p.CentralMu, rp, a));
                }

                if (elements.IsBound && double.IsFinite(elements.ApoapsisDistance))
                {
                    double ra = elements.ApoapsisDistance;
                    rMax = QualificationConditionsMax(rMax, ra);
                    vMin = QualificationConditionsMin(vMin,
                        QualificationConditionsVisViva(p.CentralMu, ra, a));
                }
            }
        }
    }

    /// <summary>
    /// Dense trajectory sample: fixed-step DOP853 with ~400 points per CharTime (clamped to
    /// [2000, 2e6] steps). This is the model the methods integrate, so the extrema are the
    /// real ones, not the values at the 8–256 oracle checkpoints. A non-physical state
    /// (DomainViolation or non-finite component) aborts the dense pass and leaves the
    /// checkpoint/osculating values in place.
    /// </summary>
    private static void QualificationConditionsDense(
        StudyProblem p, ref double rMin, ref double rMax, ref double vMin, ref double vMax)
    {
        double duration = p.Duration;
        if (!(duration > 0d) || !double.IsFinite(duration) || p.Rhs == null || p.Y0 == null)
        {
            return;
        }

        double charTime = p.CharTime > 0d && double.IsFinite(p.CharTime)
            ? p.CharTime
            : duration / 50d;
        double wanted = 400d * (duration / Math.Max(charTime, 1e-12d));
        int steps = (int)Math.Ceiling(Math.Min(Math.Max(wanted, 2000d), 2000000d));
        bool central = p.CentralMu > 0d;
        int bodies = p.PosDim / 3;
        bool hasVelocity = 2 * p.PosDim <= p.Dim;
        try
        {
            var ctx = new StudyContext(p, long.MaxValue / 4L);
            var stepper = new FixedRkStepper(RkTableau.Dop853, p, ctx);
            var y = (double[])p.Y0.Clone();
            double h = duration / steps;
            double t = p.T0;
            for (int i = 0; i < steps; i++)
            {
                stepper.Step(t, h, y);
                t += h;
                if (!QualificationConditionsFinite(y))
                {
                    return;
                }

                if (p.DomainViolation != null && p.DomainViolation(y) != null)
                {
                    return;
                }

                if (central)
                {
                    QualificationConditionsSample(y, 0, hasVelocity, p.PosDim,
                        ref rMin, ref rMax, ref vMin, ref vMax);
                }
                else
                {
                    for (int b = 0; b < bodies; b++)
                    {
                        QualificationConditionsSample(y, 3 * b, hasVelocity, p.PosDim,
                            ref rMin, ref rMax, ref vMin, ref vMax);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Leave the checkpoint/osculating extrema in place.
        }
    }

    private static bool QualificationConditionsFinite(double[] state)
    {
        for (int i = 0; i < state.Length; i++)
        {
            if (!double.IsFinite(state[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void QualificationConditionsSample(
        double[] state, int offset, bool hasVelocity, int posDim,
        ref double rMin, ref double rMax, ref double vMin, ref double vMax)
    {
        double x = state[offset];
        double y = state[offset + 1];
        double z = state[offset + 2];
        double r = Math.Sqrt((x * x) + (y * y) + (z * z));
        if (double.IsFinite(r) && r > 0d)
        {
            rMin = QualificationConditionsMin(rMin, r);
            rMax = QualificationConditionsMax(rMax, r);
        }

        if (!hasVelocity)
        {
            return;
        }

        int v = posDim + offset;
        if (v + 2 >= state.Length)
        {
            return;
        }

        double vx = state[v];
        double vy = state[v + 1];
        double vz = state[v + 2];
        double speed = Math.Sqrt((vx * vx) + (vy * vy) + (vz * vz));
        if (double.IsFinite(speed))
        {
            vMin = QualificationConditionsMin(vMin, speed);
            vMax = QualificationConditionsMax(vMax, speed);
        }
    }

    private static double QualificationConditionsVisViva(double mu, double r, double a)
    {
        if (!(r > 0d) || !(mu > 0d) || !double.IsFinite(a))
        {
            return double.NaN;
        }

        double v2 = mu * ((2d / r) - (1d / a));
        return v2 >= 0d ? Math.Sqrt(v2) : double.NaN;
    }

    private static double QualificationConditionsMin(double current, double value)
    {
        return double.IsNaN(current) || value < current ? value : current;
    }

    private static double QualificationConditionsMax(double current, double value)
    {
        return double.IsNaN(current) || value > current ? value : current;
    }

    private static string QualificationConditionsNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            return "";
        }

        return value.ToString("E10", QualificationCatalogInv);
    }
}
