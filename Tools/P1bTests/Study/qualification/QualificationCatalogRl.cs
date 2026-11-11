using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using P1bStudy.Framework;
using Galilego.Universe;

// Test564 catalogue, tier R (real system) and L (long horizons). Every problem is built from
// an existing builder; this file only adapts it to StudyProblem + an audited oracle.
//
// R1/R2: DE441 10 bodies (Sun + 8 planets + Moon) from horizons_real_2026_10y.json.
//        Newtonian uses the plain N-body RHS; 1PN uses JplAcc1Pn (Sun-only 1PN, the
//        project's headline model). Oracle: fixed-step DOP853, 5 step levels, Richardson.
// R3:    "Earth LEO 400 km, 30 d" from the Test549 case set (ship + Moon/Sun perturbations).
// R4:    Moon flyby (hyperbolic) from Test559's MoonFlybyProblem.
// L1:    "Earth LEO 400 km, 1000 orbits" case; oracle = the committed dd golden
//        ship_1000_orbits_dd.json, cross-checked against the certified local DOP853 reference.
// L2:    Kepler e=0.5 over N=100 periods, closed-form oracle.
internal static partial class P1bTests
{
    /// <summary>L2 horizon; frozen at M5 (TASK.md leaves N to this stage).</summary>
    private const int QualificationL2Orbits = 100;

    private static List<StudyShipMatrixCase> qualificationShipCases;

    private static List<StudyShipMatrixCase> QualificationShipCases()
    {
        if (qualificationShipCases == null)
        {
            StarSystem gravity = StudyShipMatrixSystem(false, false);
            StarSystem j2Only = StudyShipMatrixSystem(true, false);
            StarSystem dragOnly = StudyShipMatrixSystem(false, true);
            qualificationShipCases = StudyShipMatrixBuildCases(gravity, j2Only, dragOnly);
        }

        return qualificationShipCases;
    }

    private static void QualificationAppendRl(
        List<QualificationProblem> list, QualificationProfile profile)
    {
        list.Add(QualificationRealSolarProblem(profile));
        list.Add(QualificationDe441Problem("R2", true, profile));
        list.Add(QualificationShipProblem(
            "R3", "Earth LEO 400 km, 30 d", new[] { "ship", "perturbed" },
            "ship near Earth, Moon and Sun perturbations, 30 d"));
        list.Add(QualificationMoonFlybyProblem());
        list.Add(QualificationLongLeoProblem());
        list.Add(QualificationKeplerLongProblem());
    }

    // ---------------------------------------------------------------------------------
    // R1: the proven real-solar test fixture (Test553 path)
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// R1 uses the test-side real-solar fixture StudyRealSolar7 with the same machinery as
    /// Test553: StudyMakeNBody, two independent dd-Taylor references (43200 s and 21600 s),
    /// StudyBridgePlanetProblem (worst pair error, adaptive scales). No game production path,
    /// no baked ephemerides: the fixture is built from BodyBlueprint in Study/fixtures.
    /// </summary>
    private static QualificationProblem QualificationRealSolarProblem(QualificationProfile profile)
    {
        var q = new QualificationProblem();
        q.Id = "R1";
        q.Title = "Real solar 7-body (test fixture), Newtonian";
        q.Tags = new[] { "nbody-regular", "long-horizon", "many-body", "central-dominant" };
        q.OracleKind = "reference";
        q.OracleId = "dd-taylor-pair-v1";
        q.OracleNote = "Test553 independent dd-Taylor references (43200 s / 21600 s); floor = worst pair difference";

        double years = profile == QualificationProfile.Full ? 10d : 1d;
        double sampleEvery = 30d * 86400d;

        try
        {
            StudyNBody nb = StudyMakeNBody(StudyRealSolar7());
            StudyRefResult reference = StudyPlanetIndependentReference(nb, years, sampleEvery);
            StudyRefResult alternative = StudyPlanetIndependentReferenceAlt(nb, years, sampleEvery);
            StudyProblem p = StudyBridgePlanetProblem(
                QualificationLabel("R1", q.Title),
                nb, years, reference, alternative);

            q.Build = delegate { return p; };
            q.Floor = p.Floor;
            q.OracleTimes = (double[])p.CheckpointTimes.Clone();
            q.Oracle = QualificationCloneStates(p.CheckpointRef);
            q.Residual = p.ReferenceCrossDiffM;
            q.AuditNote = "reference " + p.ReferenceNote
                + "; floor " + QualificationFormat(p.Floor)
                + "; horizon " + years.ToString("G", QualificationCatalogInv) + " y";
        }
        catch (Exception ex)
        {
            q.Status = "INVALID";
            q.AuditNote = ex.GetType().Name + ": " + ex.Message;
        }

        return q;
    }

    // ---------------------------------------------------------------------------------
    // R1/R2: DE441 10-body
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationDe441Problem(
        string id, bool pn, QualificationProfile profile)
    {
        var q = new QualificationProblem();
        q.Id = id;
        q.Title = pn ? "DE441 10 bodies, 1PN" : "DE441 10 bodies, Newtonian";
        q.Tags = pn
            ? new[] { "1pn", "velocity-dependent", "long-horizon", "many-body" }
            : new[] { "nbody-regular", "long-horizon", "many-body", "central-dominant" };
        q.OracleKind = "reference";
        q.OracleId = "dop853-5level-v1";
        q.OracleNote = "fixed DOP853, 5 step levels, Richardson per checkpoint";

        double horizonDays = profile == QualificationProfile.Full ? 3650d : 30d;
        double horizon = horizonDays * 86400d;
        double[] times = QualificationSplitHorizon(horizon, 4);

        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        if (!File.Exists(path))
        {
            q.Status = "INVALID";
            q.AuditNote = "missing data file " + path;
            return q;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] names =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        StudyNBody nb = JplNBody(jpl, names, true, true);
        int n = nb.Count;
        int len = 3 * n;
        double[] mu = nb.Mu;
        double[] y0 = QualificationConcat(nb.P0, nb.V0);

        q.OracleTimes = times;
        q.ComFn = QualificationComNBody(mu);

        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel(id, q.Title);
            p.Dim = 2 * len;
            p.PosDim = len;
            p.ConservativeSecondOrder = !pn;
            p.CentralMu = 0d;
            p.T0 = 0d;
            p.Y0 = (double[])y0.Clone();
            p.Rhs = pn
                ? QualificationDe4411PnRhs(len, mu, n)
                : QualificationNBodyRhsNoCopy(len, mu, n);
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = new double[times.Length][];
            p.ErrorFn = QualificationPairError;
            p.CharTime = horizon / 50d;
            p.ScaleLength = StudyAU;
            p.Certified = true;
            p.Floor = double.NaN;
            p.EnergyFn = delegate (double[] s) { return QualificationNBodyEnergy(s, mu); };
            return p;
        };

        StudyProblem built = q.Problem;
        double floor;
        double order;
        string detail;
        int coarse = profile == QualificationProfile.Full ? 2048 : 512;
        q.Oracle = QualificationCertifiedDop853(built, times, coarse, out floor, out order, out detail);
        built.CheckpointRef = QualificationCloneStates(q.Oracle);
        q.Floor = floor;
        q.Residual = order;
        q.AuditNote = detail;
        return q;
    }

    /// <summary>N-body RHS with the [x, v] layout; no allocation per call.</summary>
    private static OdeRhs QualificationNBodyRhsNoCopy(int len, double[] mu, int n)
    {
        return delegate (double t, double[] y, double[] dy)
        {
            for (int i = 0; i < len; i++)
            {
                dy[i] = y[len + i];
                dy[len + i] = 0d;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dx = y[3 * j] - y[3 * i];
                    double dyy = y[(3 * j) + 1] - y[(3 * i) + 1];
                    double dz = y[(3 * j) + 2] - y[(3 * i) + 2];
                    double r2 = (dx * dx) + (dyy * dyy) + (dz * dz);
                    double inv = 1d / (r2 * Math.Sqrt(r2));
                    double fi = mu[j] * inv;
                    double fj = mu[i] * inv;
                    dy[len + (3 * i)] += dx * fi;
                    dy[len + (3 * i) + 1] += dyy * fi;
                    dy[len + (3 * i) + 2] += dz * fi;
                    dy[len + (3 * j)] -= dx * fj;
                    dy[len + (3 * j) + 1] -= dyy * fj;
                    dy[len + (3 * j) + 2] -= dz * fj;
                }
            }
        };
    }

    /// <summary>Sun-only 1PN (the project's headline model), [x, v] layout.</summary>
    private static OdeRhs QualificationDe4411PnRhs(int len, double[] mu, int n)
    {
        var pos = new double[len];
        var vel = new double[len];
        var acc = new double[len];
        return delegate (double t, double[] y, double[] dy)
        {
            for (int i = 0; i < len; i++)
            {
                pos[i] = y[i];
                vel[i] = y[len + i];
                dy[i] = y[len + i];
            }

            JplAcc1Pn(pos, vel, acc, mu, n, true);
            for (int i = 0; i < len; i++)
            {
                dy[len + i] = acc[i];
            }
        };
    }

    private static Func<double[], double[]> QualificationComNBody(double[] mu)
    {
        int n = mu.Length;
        return delegate (double[] s)
        {
            double total = 0d;
            double x = 0d;
            double y = 0d;
            double z = 0d;
            for (int i = 0; i < n; i++)
            {
                total += mu[i];
                x += mu[i] * s[3 * i];
                y += mu[i] * s[(3 * i) + 1];
                z += mu[i] * s[(3 * i) + 2];
            }

            return new[] { x / total, y / total, z / total };
        };
    }

    // ---------------------------------------------------------------------------------
    // Certified fixed-step DOP853 reference with Richardson per checkpoint
    // ---------------------------------------------------------------------------------

    private static double[][] QualificationCertifiedDop853(
        StudyProblem p, double[] times, int coarseSteps, out double floor, out double order,
        out string detail)
    {
        var states = new double[times.Length][];
        floor = 0d;
        double worstOrder = double.NaN;
        double finestDelta = 0d;
        for (int k = 0; k < times.Length; k++)
        {
            QualificationDop853Levels levels = QualificationDop853Richardson(
                p, times[k], coarseSteps, QualificationAuditCoefficientLevels);
            states[k] = levels.Finest;
            double f = double.IsFinite(levels.Floor) ? levels.Floor : double.PositiveInfinity;
            if (f > floor)
            {
                floor = f;
            }

            if (double.IsFinite(levels.ObservedOrder))
            {
                worstOrder = double.IsNaN(worstOrder)
                    ? levels.ObservedOrder
                    : Math.Min(worstOrder, levels.ObservedOrder);
            }

            finestDelta = Math.Max(finestDelta, Math.Abs(levels.Floor));
        }

        order = worstOrder;
        detail = "certified fixed DOP853, " + coarseSteps.ToString(QualificationCatalogInv)
            + ".." + (coarseSteps << (QualificationAuditCoefficientLevels - 1)).ToString(QualificationCatalogInv)
            + " steps, floor " + QualificationFormat(floor)
            + ", worst observed order " + QualificationFormat(order)
            + ", finest delta " + QualificationFormat(finestDelta);
        return states;
    }

    // ---------------------------------------------------------------------------------
    // R3/R4: ship cases
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationShipProblem(
        string id, string caseLabel, string[] tags, string title)
    {
        var q = new QualificationProblem();
        q.Id = id;
        q.Title = title;
        q.Tags = tags;
        q.OracleKind = "reference";
        q.OracleId = "ship-certified-dop853-local-v1";
        q.OracleNote = "Test549 certified fixed DOP853 local reference + independent IAS15 cross-check";

        StudyShipMatrixCase found = null;
        foreach (StudyShipMatrixCase c in QualificationShipCases())
        {
            if (string.Equals(c.Label, caseLabel, StringComparison.Ordinal))
            {
                found = c;
                break;
            }
        }

        if (found == null)
        {
            q.Status = "INVALID";
            q.AuditNote = "case not found in StudyShipMatrixBuildCases: " + caseLabel;
            return q;
        }

        try
        {
            StudyProblem p = BuildPureProblem(found);
            p.Label = QualificationLabel(id, title);
            q.Build = delegate { return p; };
            q.Floor = p.Floor;
            q.OracleTimes = (double[])p.CheckpointTimes.Clone();
            q.Oracle = QualificationCloneStates(p.CheckpointRef);
            q.Residual = p.ReferenceCrossDiffM;
            q.AuditNote = "reference " + p.ReferenceNote
                + "; floor " + QualificationFormat(p.Floor)
                + "; independent cross-diff " + QualificationFormat(p.ReferenceCrossDiffM)
                + (p.ReferenceIndependent ? " (independent)" : " (single reference)");
        }
        catch (Exception ex)
        {
            q.Status = "INVALID";
            q.AuditNote = ex.GetType().Name + ": " + ex.Message;
        }

        return q;
    }

    private static QualificationProblem QualificationMoonFlybyProblem()
    {
        var q = new QualificationProblem();
        q.Id = "R4";
        q.Title = "Moon flyby (hyperbolic, vinf 1000 m/s, 100 km)";
        q.Tags = new[] { "ship", "close-encounter" };
        q.OracleKind = "reference";
        q.OracleId = "ship-certified-dop853-local-v1";
        q.OracleNote = "Test559 MoonFlybyProblem certified reference + independent IAS15";

        try
        {
            StarSystem solar = StudyShipMatrixSystem(false, false);
            StudyProblem p = MoonFlybyProblem(solar, 1000d, 100d);
            p.Label = QualificationLabel("R4", q.Title);
            q.Build = delegate { return p; };
            q.Floor = p.Floor;
            q.OracleTimes = (double[])p.CheckpointTimes.Clone();
            q.Oracle = QualificationCloneStates(p.CheckpointRef);
            q.Residual = p.ReferenceCrossDiffM;
            q.AuditNote = "reference " + p.ReferenceNote
                + "; floor " + QualificationFormat(p.Floor)
                + "; independent cross-diff " + QualificationFormat(p.ReferenceCrossDiffM)
                + (p.ReferenceIndependent ? " (independent)" : " (single reference)");
        }
        catch (Exception ex)
        {
            q.Status = "INVALID";
            q.AuditNote = ex.GetType().Name + ": " + ex.Message;
        }

        return q;
    }

    // ---------------------------------------------------------------------------------
    // L1: 1000 orbits, dd golden
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationLongLeoProblem()
    {
        var q = new QualificationProblem();
        q.Id = "L1";
        q.Title = "LEO 400 km, 1000 orbits (pure gravity)";
        q.Tags = new[] { "kepler", "long-horizon" };
        q.OracleKind = "reference";
        q.OracleId = "dd-golden-v1";
        q.OracleNote = "committed dd golden ship_1000_orbits_dd.json, cross-checked against the certified local DOP853 reference";

        try
        {
            string label = "Earth LEO 400 km, 1000 orbits (~64 d)";
            StudyShipMatrixCase found = null;
            foreach (StudyShipMatrixCase c in QualificationShipCases())
            {
                if (string.Equals(c.Label, label, StringComparison.Ordinal))
                {
                    found = c;
                    break;
                }
            }

            if (found == null)
            {
                q.Status = "INVALID";
                q.AuditNote = "case not found: " + label;
                return q;
            }

            StudyProblem certified = BuildPureProblem(found);
            string path = StudyShipDdGoldenPath();
            if (!File.Exists(path))
            {
                q.Status = "INVALID";
                q.AuditNote = "golden file missing: " + path;
                return q;
            }

            StudyShipDdGoldenFile golden =
                JsonSerializer.Deserialize<StudyShipDdGoldenFile>(File.ReadAllText(path));

            // The golden carries one endpoint; keep that single checkpoint.
            StudyProblem endOnly = certified.CloneEndOnly();
            double[] certifiedEnd = certified.CheckpointRef[certified.CheckpointRef.Length - 1];
            double dp = 0d;
            double dv = 0d;
            for (int i = 0; i < 3; i++)
            {
                dp = Math.Max(dp, Math.Abs(certifiedEnd[i] - golden.PRel[i]));
                dv = Math.Max(dv, Math.Abs(certifiedEnd[certified.PosDim + i] - golden.VRel[i]));
            }

            var goldenRef = new double[endOnly.Dim];
            for (int i = 0; i < 3; i++)
            {
                goldenRef[i] = golden.PRel[i];
                goldenRef[endOnly.PosDim + i] = golden.VRel[i];
            }

            for (int i = 2 * endOnly.PosDim; i < endOnly.Dim; i++)
            {
                goldenRef[i] = endOnly.Y0[i];
            }

            endOnly.CheckpointRef[0] = goldenRef;
            endOnly.Floor = Math.Max(Math.Max(golden.SelfErrorM, certified.Floor), Math.Max(dp, dv * 1000d));
            endOnly.Certified = true;
            endOnly.Label = QualificationLabel("L1", q.Title);
            endOnly.MaxControl = 0d;

            q.Build = delegate { return endOnly; };
            q.Floor = endOnly.Floor;
            q.OracleTimes = new[] { endOnly.EndTime };
            q.Oracle = new[] { goldenRef };
            q.Residual = Math.Max(dp, dv * 1000d);
            q.AuditNote = "dd golden self " + QualificationFormat(golden.SelfErrorM)
                + " m; certified local reference vs golden: "
                + QualificationFormat(dp) + " m, " + QualificationFormat(dv) + " m/s"
                + "; model/frame difference, see FOUND.md F8";
        }
        catch (Exception ex)
        {
            q.Status = "INVALID";
            q.AuditNote = ex.GetType().Name + ": " + ex.Message;
        }

        return q;
    }

    // ---------------------------------------------------------------------------------
    // L2: long Kepler
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationKeplerLongProblem()
    {
        var q = new QualificationProblem();
        q.Id = "L2";
        q.Title = "Kepler e=0.5, " + QualificationL2Orbits.ToString(QualificationCatalogInv) + " orbits";
        q.Tags = new[] { "kepler", "ecc-mid", "long-horizon" };
        q.OracleKind = "analytic";
        q.OracleId = "kepler-closed-form-v1";
        q.OracleNote = "closed-form Kepler equation over " + QualificationL2Orbits + " periods";

        const double e = 0.5d;
        const double a = 1d;
        double horizon = QualificationL2Orbits * QualificationKeplerPeriod;
        double[] times = QualificationSplitHorizon(horizon, 4);
        double[] y0 = QualificationEllipticState(QualificationKeplerMu, a, e, 0d);

        q.OracleTimes = times;
        q.Oracle = new double[times.Length][];
        for (int k = 0; k < times.Length; k++)
        {
            q.Oracle[k] = QualificationEllipticState(QualificationKeplerMu, a, e, times[k]);
        }

        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel("L2", q.Title);
            p.Dim = 6;
            p.PosDim = 3;
            p.ConservativeSecondOrder = true;
            p.CentralMu = QualificationKeplerMu;
            p.T0 = 0d;
            p.Y0 = (double[])y0.Clone();
            p.Rhs = QualificationKeplerRhs(QualificationKeplerMu);
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = QualificationCloneStates(q.Oracle);
            p.CharTime = QualificationKeplerPeriod;
            p.ScaleLength = a;
            p.Certified = true;
            double mu = QualificationKeplerMu;
            p.EnergyFn = delegate (double[] s)
            {
                double r = Math.Sqrt((s[0] * s[0]) + (s[1] * s[1]) + (s[2] * s[2]));
                double v2 = (s[3] * s[3]) + (s[4] * s[4]) + (s[5] * s[5]);
                return (0.5d * v2) - (mu / r);
            };
            p.AngularMomentumFn = QualificationAngularMomentum;
            return p;
        };
        return q;
    }

    // ---------------------------------------------------------------------------------
    // Audit for R/L
    // ---------------------------------------------------------------------------------

    private static void QualificationAuditRl(QualificationProblem q)
    {
        StudyProblem p = q.Problem;
        if (q.Id == "R2")
        {
            // Self-consistency: the same certified DOP853 with twice the resolution must agree
            // with the stored reference to within a few times its own Richardson floor.
            double floor2;
            double order2;
            string detail2;
            double[][] fine = QualificationCertifiedDop853(
                p, p.CheckpointTimes, 1024, out floor2, out order2, out detail2);
            double diff = 0d;
            for (int k = 0; k < fine.Length; k++)
            {
                diff = Math.Max(diff, QualificationMaxAbsDiff(fine[k], p.CheckpointRef[k]));
            }

            q.Residual = diff;
            q.Cross = Math.Max(q.Floor, floor2);
            double tol = Math.Max(10d * Math.Max(q.Floor, floor2), 1e-12d * p.ScaleLength);
            bool ok = double.IsFinite(q.Floor) && diff <= tol;
            q.Status = ok ? "VALID" : "INVALID";
            q.AuditNote = q.OracleNote + "; floor " + QualificationFormat(q.Floor)
                + "; refinement diff " + QualificationFormat(diff)
                + " (tol " + QualificationFormat(tol) + "); " + detail2
                + (ok ? "" : "; not converged");
            return;
        }

        if (q.Id == "L1")
        {
            // The specified golden (world-frame dd) and the local-frame force model disagree by
            // ~8.5 m over 1000 orbits. Status is computed: the golden is accepted only when it
            // agrees with the local model to 0.1 * A1 (0.1 * 1e-6 * ScaleLength). With the
            // current 9.657 m that is false, hence INVALID (FOUND.md F8), but a rebuilt golden
            // would flip it automatically.
            double a1 = QualificationLevelFactors[0] * p.ScaleLength;
            bool goldenOk = double.IsFinite(q.Residual) && q.Residual <= 0.1d * a1;
            q.Status = goldenOk ? "VALID" : "INVALID";
            q.AuditNote = q.AuditNote
                + "; gate |golden - local| = " + QualificationFormat(q.Residual)
                + " <= 0.1*A1 = " + QualificationFormat(0.1d * a1)
                + (goldenOk ? "" : " -> INVALID (model/frame difference, FOUND.md F8)");
            return;
        }

        // R2 is the DE441 adapter; R1/R3/R4 use the proven Test553/Test559 reference
        // machinery (already carrying an independent cross-check). A floor that only permits
        // A1/A2 is not an oracle failure: the level gate turns it into SKIP, exactly as
        // TASK.md prescribes.
        bool certified = double.IsFinite(p.Floor) && p.Floor > 0d;
        q.Status = certified ? "VALID" : "INVALID";
        q.AuditNote = q.AuditNote
            + (certified ? "" : "; reference is not certified (floor is not finite)");
    }
}
