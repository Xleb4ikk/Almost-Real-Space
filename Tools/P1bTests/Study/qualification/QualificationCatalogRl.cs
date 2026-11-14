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
        List<QualificationProblem> list, QualificationProfile profile, bool productionOnly = false)
    {
        // The production profile (C4) needs only the ship tasks R3/R4/L1; R1/R2 are planet
        // and DE441 models the game does not integrate.
        if (!productionOnly)
        {
            list.Add(QualificationRealSolarProblem(profile));
            list.Add(QualificationDe441Problem("R2", true, profile, useHGoals: true, modelName: "1PN"));
        }

        list.Add(QualificationShipProblem(
            "R3", "Earth LEO 400 km, 30 d", new[] { "ship", "perturbed" },
            "ship near Earth, Moon and Sun perturbations, 30 d"));
        list.Add(QualificationMoonFlybyProblem());
        list.Add(QualificationLongLeoProblem());
        if (!productionOnly)
        {
            list.Add(QualificationKeplerLongProblem());
        }
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
        q.MeterTargets = QualificationMeterGoals;
        q.CentralBody = "barycentre (7 bodies, no central body)";
        q.Forces = new[] { "gravity" };

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
        string id, bool pn, QualificationProfile profile,
        string epochFile = "horizons_real_2026_10y.json",
        string window = "2026-2036",
        string title = null,
        string[] tags = null,
        string modelName = null,
        bool useHGoals = false,
        bool tenYearInSmoke = false)
    {
        var q = new QualificationProblem();
        q.Id = id;
        q.Title = title != null
            ? title
            : (pn ? "DE441 10 bodies, 1PN" : "DE441 10 bodies, Newtonian");
        q.Tags = tags != null
            ? tags
            : (pn
                ? new[] { "1pn", "velocity-dependent", "long-horizon", "many-body" }
                : new[] { "nbody-regular", "long-horizon", "many-body", "central-dominant" });
        q.OracleKind = "reference";
        q.OracleId = "dop853-5level-v1";
        q.OracleNote = "fixed DOP853, 5 step levels, Richardson per checkpoint";
        q.CentralBody = "barycentre (10 bodies, no central body)";
        q.Forces = pn ? new[] { "gravity", "1pn" } : new[] { "gravity" };
        if (useHGoals)
        {
            // B7 conditions.csv columns for the H tier (R2 included).
            q.EpochWindow = window;
            q.ModelName = pn ? "1pn" : "newton";
        }

        // B6: the H tier and R2 carry M100/M10/M1 and the Q goals; A1-A3 stay as they were.
        bool tenYearSmoke = tenYearInSmoke;
        if (useHGoals)
        {
            q.MeterTargets = QualificationHydroGoals;
            if (profile == QualificationProfile.Full || tenYearSmoke)
            {
                // Q compares against a 10-year E_model, so it is meaningless on the 30-day
                // smoke horizon of R2; H1 runs its full horizon in smoke and keeps both goals.
                q.QTargets = new[] { 0.1d, 0.01d };
                q.QModelRms8 = QualificationNasaModelErrorMeters(window, modelName, false);
                q.QModelMoon = QualificationNasaModelErrorMeters(window, modelName, true);
            }
        }

        double horizonDays = profile == QualificationProfile.Full || tenYearSmoke ? 3650d : 30d;
        double horizon = horizonDays * 86400d;
        double[] times = QualificationSplitHorizon(horizon, 4);

        string path = Path.Combine("Study", "jpl", epochFile);
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
        if (useHGoals)
        {
            q.NBodies = n;
        }

        q.OracleTimes = times;
        q.ComFn = QualificationComNBody(mu);

        int[] planetBlocks = { 1, 2, 3, 5, 6, 7, 8, 9 };
        int moonBlock = 4;
        int earthBlock = 3;
        double[][] horizonsPlanets = horizonDays >= 365d
            ? QualificationHorizonsPlanets(jpl, names, planetBlocks, horizonDays)
            : null;

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
            p.QErrorFn = QualificationQNBodyMetric(planetBlocks, moonBlock, earthBlock, horizonsPlanets);
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
        int coarse = profile == QualificationProfile.Full || tenYearSmoke ? 2048 : 512;
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
        q.MeterTargets = QualificationMeterGoals;

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
            q.CentralBody = found.Sys.AllBodies[found.Central].Name;
            var forces = new List<string> { "gravity", "third-body" };
            if (found.Sys.AllBodies[found.Central].J2 != 0d)
            {
                forces.Add("j2");
            }

            if (found.Drag != null)
            {
                forces.Add("drag");
            }

            if (found.Thrust)
            {
                forces.Add("thrust");
            }

            q.Forces = forces.ToArray();
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
        q.MeterTargets = QualificationMeterGoals;
        q.CentralBody = "Moon";
        q.Forces = new[] { "gravity", "third-body" };

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
        q.OracleId = "ship-certified-dop853-local-v1";
        q.OracleNote = "local-frame certified fixed DOP853 reference (the model the methods integrate); "
            + "the world-frame dd golden is a cross-check only (owner decision 2026-11-11)";
        q.MeterTargets = QualificationMeterGoals;
        q.CentralBody = "Earth";
        q.Forces = new[] { "gravity", "third-body" };

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

            // Same model as the methods: the certified local DOP853 reference of the Test549
            // case (not the world-frame dd golden). This keeps the oracle and the integrated
            // force in the same frame (owner decision: the 8.5 m golden difference is a frame
            // difference, not an integrator error). LocalReferenceOnly leaves the dd
            // disagreement in the reference note and does not fold it into the floor.
            found.LocalReferenceOnly = true;
            StudyShipMatrixSetTargets(found);
            StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(found);
            StudyProblem p = StudyBridgeShipProblem(found, refs);
            p.Label = QualificationLabel("L1", q.Title);
            // refs.ReferenceFloor includes the world-frame dd endpoint difference (8.522 m) —
            // a frame difference, not the local reference error. The owner's decision is to use
            // the local certified error (1.82e-4 m) as the floor.
            double localFloor = refs.LocalError;
            if (!double.IsFinite(localFloor) || localFloor <= 0d)
            {
                localFloor = refs.ReferenceFloor;
            }

            p.Floor = localFloor;
            p.ReferenceNote = refs.LocalKind;
            p.ReferenceMethod = "fixed DOP853, local frame";
            p.Certified = double.IsFinite(p.Floor);

            q.Build = delegate { return p; };
            q.Floor = p.Floor;
            q.OracleTimes = (double[])p.CheckpointTimes.Clone();
            q.Oracle = QualificationCloneStates(p.CheckpointRef);

            // Cross-check only (world-frame dd golden); never the grading reference.
            string cross = "golden missing";
            double goldenDiff = double.NaN;
            string path = StudyShipDdGoldenPath();
            if (File.Exists(path))
            {
                StudyShipDdGoldenFile golden =
                    JsonSerializer.Deserialize<StudyShipDdGoldenFile>(File.ReadAllText(path));
                double[] end = p.CheckpointRef[p.CheckpointRef.Length - 1];
                double dp = 0d;
                double dv = 0d;
                for (int i = 0; i < 3; i++)
                {
                    dp = Math.Max(dp, Math.Abs(end[i] - golden.PRel[i]));
                    dv = Math.Max(dv, Math.Abs(end[p.PosDim + i] - golden.VRel[i]));
                }

                goldenDiff = Math.Max(dp, dv * 1000d);
                cross = "golden vs local: " + QualificationFormat(dp) + " m, "
                    + QualificationFormat(dv) + " m/s (frame difference, FOUND.md F8)";
            }

            q.Residual = goldenDiff;
            q.AuditNote = "reference " + p.ReferenceNote
                + "; floor " + QualificationFormat(p.Floor) + "; " + cross;
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
        q.CentralBody = "central field (mu=1, origin)";
        q.Forces = new[] { "gravity" };

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
            // Owner decision 2026-11-11: the oracle is the local certified DOP853 reference of
            // the same case (the model the methods integrate). The world-frame dd golden is a
            // cross-check; its ~8.5 m disagreement is a frame difference (FOUND.md F8), not an
            // oracle failure. A3 remains SKIP because the local floor is ~1.8e-4 m.
            bool localCertified = double.IsFinite(p.Floor) && p.Floor > 0d;
            q.Status = localCertified ? "VALID" : "INVALID";
            q.AuditNote = q.AuditNote
                + (localCertified ? "" : "; local certified reference has no finite floor");
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
