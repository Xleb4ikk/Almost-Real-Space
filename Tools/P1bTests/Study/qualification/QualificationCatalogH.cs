using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using P1bStudy.Framework;

// TASK.md Appendix B, tier H (layer I: methods on the real force) and the B6 goals.
//
// H1/H1h/H2h are the DE441 10-body models of Test545/537/538 wrapped as StudyProblem, with the
// same certified fixed-step DOP853 reference as R2 (5 step levels, Richardson per checkpoint).
// R2 itself is not duplicated: it gains the H goals in QualificationCatalogRl.cs.
//
// Q goals (B6) are defined against E_model from layer V of the same run
// (QualificationNasaModelErrorMeters), never against a hardcoded number.
internal static partial class P1bTests
{
    /// <summary>B6: the H tier gets M100/M10/M1 (A7 rules apply).</summary>
    private static readonly double[] QualificationHydroGoals = { 100d, 10d, 1d };

    private static void QualificationAppendAppendixH(
        List<QualificationProblem> list, QualificationProfile profile)
    {
        list.Add(QualificationDe441HProblem(
            "H1", "DE441 10 bodies, Newtonian, 2026-2036", false,
            "horizons_real_2026_10y.json", "2026-2036",
            new[] { "real-system", "de441", "many-body", "long-horizon" },
            profile, false));

        QualificationProblem h1h = QualificationDe441HProblem(
            "H1h", "DE441 10 bodies, Newtonian, 2016-2026 (holdout epoch)", false,
            "horizons_real_2016_10y.json", "2016-2026",
            new[] { "real-system", "de441", "many-body", "holdout-epoch" },
            profile, true);
        h1h.FullOnly = true;
        list.Add(h1h);

        QualificationProblem h2h = QualificationDe441HProblem(
            "H2h", "DE441 10 bodies, 1PN, 2016-2026 (holdout epoch)", true,
            "horizons_real_2016_10y.json", "2016-2026",
            new[] { "real-system", "de441", "1pn", "velocity-dependent", "holdout-epoch" },
            profile, true);
        h2h.FullOnly = true;
        list.Add(h2h);

        if (profile == QualificationProfile.Full)
        {
            // B4.3: H3 and H6 carry a 1e6 RHS budget per cell; a SKIP by budget is recorded,
            // not hidden.
            QualificationProblem h3 = QualificationH3Problem();
            h3.FullOnly = true;
            list.Add(h3);
            QualificationProblem h6 = QualificationH6Problem();
            h6.FullOnly = true;
            list.Add(h6);
        }
    }

    // ---------------------------------------------------------------------------------
    // H3: 10 bodies + 409 small bodies (full409, the Test546 force)
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationH3Problem()
    {
        var q = new QualificationProblem();
        q.Id = "H3";
        q.Title = "DE441 10 bodies + 409 small bodies (full409), 2026-2036";
        q.Tags = new[] { "real-system", "small-bodies", "1pn", "velocity-dependent", "many-body" };
        q.OracleKind = "reference";
        q.OracleId = "dop853-5level-v1";
        q.OracleNote = "fixed DOP853, 5 step levels, Richardson per checkpoint; "
            + "force identical to Test546 (Sun-only 1PN + solar J2 + Earth J2 + 409 test particles)";
        q.CentralBody = "barycentre (419 bodies, 409 are test particles)";
        q.Forces = new[] { "gravity", "1pn", "j2" };
        q.MeterTargets = QualificationHydroGoals;
        q.QTargets = new[] { 0.1d, 0.01d };
        q.QModelRms8 = QualificationNasaModelErrorMeters("2026-2036", "409small+J2+EJ2", false);
        q.QModelMoon = QualificationNasaModelErrorMeters("2026-2036", "409small+J2+EJ2", true);
        q.EpochWindow = "2026-2036";
        q.ModelName = "full409";
        q.MaxCostRhs = 1000000L;
        q.AuditKind = "h";
        q.AuditCoarseSteps = 512;
        q.FullOnly = true;

        double horizon = 3650d * 86400d;
        double[] times = QualificationSplitHorizon(horizon, 4);
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        if (!File.Exists(path))
        {
            q.Status = "INVALID";
            q.AuditNote = "missing data file " + path;
            return q;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        var names = new List<string>(QualificationNasaNames());
        foreach (JplBody body in jpl.Bodies)
        {
            if (body.Name.StartsWith("Ast", StringComparison.Ordinal)
                || body.Name.StartsWith("Kbo", StringComparison.Ordinal)
                || body.Name.StartsWith("Kbr", StringComparison.Ordinal))
            {
                names.Add(body.Name);
            }
        }

        StudyNBody nb = JplNBody(jpl, names.ToArray(), true, true);
        int n = nb.Count;
        int len = 3 * n;
        double[] mu = nb.Mu;
        double[] y0 = QualificationConcat(nb.P0, nb.V0);
        int earth = JplIndex(nb, "Earth");
        int moon = JplIndex(nb, "Moon");
        var planetMu = new double[10];
        Array.Copy(mu, planetMu, 10);
        q.NBodies = n;

        q.OracleTimes = times;
        q.ComFn = QualificationComNBody(mu);
        int[] planetBlocks = { 1, 2, 3, 5, 6, 7, 8, 9 };
        int moonBlock = 4;
        int earthBlock = 3;
        double[][] horizonsPlanets = QualificationHorizonsPlanets(jpl, names.ToArray(), planetBlocks, 3650d);

        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel(q.Id, q.Title);
            p.Dim = 2 * len;
            p.PosDim = len;
            p.ConservativeSecondOrder = false;
            p.CentralMu = 0d;
            p.T0 = 0d;
            p.Y0 = (double[])y0.Clone();
            p.Rhs = QualificationFull409Rhs(len, mu, n, planetMu, earth, moon);
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = new double[times.Length][];
            p.ErrorFn = QualificationPairError;
            p.QErrorFn = QualificationQNBodyMetric(planetBlocks, moonBlock, earthBlock, horizonsPlanets);
            p.CharTime = horizon / 50d;
            p.ScaleLength = StudyAU;
            p.Certified = true;
            p.Floor = double.NaN;
            // No EnergyFn: 1PN plus test particles is not the Newtonian invariant, and G3 is
            // reported only where the invariant exists.
            if (q.Oracle == null)
            {
                double floor;
                double order;
                string detail;
                // ~6 min in full: lazy, so a report-only resume does not pay for it.
                q.Oracle = QualificationCertifiedDop853(p, times, 2048, out floor, out order, out detail);
                q.Floor = floor;
                q.Residual = order;
                q.AuditNote = detail;
            }

            p.CheckpointRef = QualificationCloneStates(q.Oracle);
            return p;
        };
        return q;
    }

    /// <summary>Sun-only 1PN + solar J2 + Earth J2 + 409 test particles, exactly Test546's acc.</summary>
    private static OdeRhs QualificationFull409Rhs(
        int len, double[] mu, int n, double[] planetMu, int earth, int moon)
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

            JplAccPlanetsAsteroids(pos, vel, acc, mu, n, 10, true, true, planetMu);
            JplAccEarthJ2(pos, acc, earth, moon, mu[earth], mu[moon]);
            for (int i = 0; i < len; i++)
            {
                dy[len + i] = acc[i];
            }
        };
    }

    // ---------------------------------------------------------------------------------
    // H6: the satellite system of Test544
    // ---------------------------------------------------------------------------------

    private static QualificationProblem QualificationH6Problem()
    {
        var q = new QualificationProblem();
        q.Id = "H6";
        q.Title = "DE441 satellites: Moon, Galilean moons, Saturn moons (Test544 model)";
        q.Tags = new[] { "real-system", "satellites", "many-body" };
        q.OracleKind = "reference";
        q.OracleId = "dop853-5level-v1";
        q.OracleNote = "fixed DOP853, 5 step levels, Richardson per checkpoint; "
            + "Test544 force (1PN, Earth J2, Jupiter/Saturn J2/J4/J6, moon J2)";
        q.CentralBody = "each moon relative to its planet (max over the 13 moons)";
        q.Forces = new[] { "gravity", "1pn", "j2" };
        q.MeterTargets = QualificationHydroGoals;
        q.EpochWindow = "2026-2036";
        q.ModelName = "satellites";
        q.MaxCostRhs = 1000000L;
        q.AuditKind = "h";
        q.AuditCoarseSteps = 14600;
        q.FullOnly = true;

        double horizon = 3650d * 86400d;
        double[] times = QualificationSplitHorizon(horizon, 4);
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        if (!File.Exists(path))
        {
            q.Status = "INVALID";
            q.AuditNote = "missing data file " + path;
            return q;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        StudyNBody nb = JplNBody(jpl, QualificationNasaSatelliteNames(), true, true);
        int n = nb.Count;
        int len = 3 * n;
        double[] mu = nb.Mu;
        double[] y0 = QualificationConcat(nb.P0, nb.V0);
        q.NBodies = n;

        var moons = new[]
        {
            "Moon", "Io", "Europa", "Ganymede", "Callisto",
            "Mimas", "Enceladus", "Tethys", "Dione", "Rhea",
            "Titan", "Hyperion", "Iapetus"
        };
        var centers = new[]
        {
            "Earth", "JupiterCenter", "JupiterCenter", "JupiterCenter", "JupiterCenter",
            "SaturnCenter", "SaturnCenter", "SaturnCenter", "SaturnCenter", "SaturnCenter",
            "SaturnCenter", "SaturnCenter", "SaturnCenter"
        };
        var moonBlocks = new int[moons.Length];
        var centerBlocks = new int[moons.Length];
        for (int i = 0; i < moons.Length; i++)
        {
            moonBlocks[i] = JplIndex(nb, moons[i]);
            centerBlocks[i] = JplIndex(nb, centers[i]);
        }

        int earth = JplIndex(nb, "Earth");
        int moon = JplIndex(nb, "Moon");
        int jupiterCenter = JplIndex(nb, "JupiterCenter");
        int saturnCenter = JplIndex(nb, "SaturnCenter");
        var jupiterMoons = new[]
        {
            JplIndex(nb, "Io"), JplIndex(nb, "Europa"),
            JplIndex(nb, "Ganymede"), JplIndex(nb, "Callisto")
        };
        var saturnMoons = new[]
        {
            JplIndex(nb, "Mimas"), JplIndex(nb, "Enceladus"),
            JplIndex(nb, "Tethys"), JplIndex(nb, "Dione"),
            JplIndex(nb, "Rhea"), JplIndex(nb, "Titan"),
            JplIndex(nb, "Hyperion"), JplIndex(nb, "Iapetus")
        };

        q.OracleTimes = times;
        q.Build = delegate
        {
            var p = new StudyProblem();
            p.Label = QualificationLabel(q.Id, q.Title);
            p.Dim = 2 * len;
            p.PosDim = len;
            p.ConservativeSecondOrder = false;
            p.CentralMu = 0d;
            p.T0 = 0d;
            p.Y0 = (double[])y0.Clone();
            p.Rhs = QualificationSatelliteRhs(
                len, mu, n, earth, moon, jupiterCenter, saturnCenter, jupiterMoons, saturnMoons);
            p.CheckpointTimes = (double[])times.Clone();
            p.CheckpointRef = new double[times.Length][];
            p.ErrorFn = QualificationSatelliteError(moonBlocks, centerBlocks);
            p.CharTime = horizon / 50d;
            p.ScaleLength = StudyAU;
            p.Certified = true;
            p.Floor = double.NaN;
            if (q.Oracle == null)
            {
                double floor;
                double order;
                string detail;
                // Lazy: a report-only resume must not rebuild a 10-year 21-body reference.
                // 0.125 d at the coarsest level: Mimas (period 0.94 d) is not resolved by a
                // 0.5 d fixed step, and a non-asymptotic coarse pair inflates the floor.
                q.Oracle = QualificationCertifiedDop853(p, times, 29200, out floor, out order, out detail);
                q.Floor = floor;
                q.Residual = order;
                q.AuditNote = detail;
            }

            p.CheckpointRef = QualificationCloneStates(q.Oracle);
            return p;
        };
        return q;
    }

    /// <summary>
    /// Test544 force: Sun-only 1PN for every body, Earth J2 for the Moon, J2/J4/J6 of Jupiter
    /// and Saturn, J2 of the moons. It mirrors the inline acceleration of JplDop853 exactly;
    /// the assembly is repeated because JplDop853's closure is not callable, and the
    /// cross-check in the H6 audit keeps the two from drifting apart.
    /// </summary>
    private static OdeRhs QualificationSatelliteRhs(
        int len, double[] mu, int n, int earth, int moon,
        int jupiterCenter, int saturnCenter, int[] jupiterMoons, int[] saturnMoons)
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
            if (earth >= 0 && moon >= 0)
            {
                JplAccEarthJ2(pos, acc, earth, moon, mu[earth], mu[moon]);
            }

            if (jupiterCenter >= 0)
            {
                const double jupiterEps = 3.13d * Math.PI / 180d;
                double sx = 0d;
                double sy = -Math.Sin(jupiterEps);
                double sz = Math.Cos(jupiterEps);
                double[] moonJ2 = { 1.795e-3d, 4.35e-4d, 3.12e-4d, 3.0e-4d };
                double[] moonR = { 1.8216e6d, 1.5608e6d, 2.6312e6d, 2.4103e6d };
                for (int k = 0; k < jupiterMoons.Length; k++)
                {
                    if (jupiterMoons[k] < 0)
                    {
                        continue;
                    }

                    JplAccZonal(pos, acc, jupiterCenter, jupiterMoons[k],
                        mu[jupiterCenter], 1.4736e-2d, 7.1492e7d, 2, sx, sy, sz);
                    JplAccZonal(pos, acc, jupiterCenter, jupiterMoons[k],
                        mu[jupiterCenter], -5.87e-4d, 7.1492e7d, 4, sx, sy, sz);
                    JplAccZonal(pos, acc, jupiterCenter, jupiterMoons[k],
                        mu[jupiterCenter], 3.4e-5d, 7.1492e7d, 6, sx, sy, sz);
                    JplAccZonal(pos, acc, jupiterMoons[k], jupiterCenter,
                        mu[jupiterMoons[k]], moonJ2[k], moonR[k], 2, sx, sy, sz);
                }
            }

            if (saturnCenter >= 0)
            {
                const double saturnEps = 26.73d * Math.PI / 180d;
                double sx = 0d;
                double sy = -Math.Sin(saturnEps);
                double sz = Math.Cos(saturnEps);
                double[] moonJ2 =
                {
                    4.5e-3d, 5.4e-3d, 1.0e-3d, 1.0e-3d,
                    1.0e-3d, 3.3e-5d, 0d, 1.0e-3d
                };
                double[] moonR =
                {
                    1.986e5d, 2.521e5d, 5.31e5d, 5.61e5d,
                    7.64e5d, 2.5747e6d, 1.35e5d, 7.35e5d
                };
                for (int k = 0; k < saturnMoons.Length; k++)
                {
                    if (saturnMoons[k] < 0)
                    {
                        continue;
                    }

                    JplAccZonal(pos, acc, saturnCenter, saturnMoons[k],
                        mu[saturnCenter], 1.6298e-2d, 6.0268e7d, 2, sx, sy, sz);
                    JplAccZonal(pos, acc, saturnCenter, saturnMoons[k],
                        mu[saturnCenter], -9.15e-4d, 6.0268e7d, 4, sx, sy, sz);
                    JplAccZonal(pos, acc, saturnCenter, saturnMoons[k],
                        mu[saturnCenter], 1.0e-4d, 6.0268e7d, 6, sx, sy, sz);
                    if (moonJ2[k] != 0d)
                    {
                        JplAccZonal(pos, acc, saturnMoons[k], saturnCenter,
                            mu[saturnMoons[k]], moonJ2[k], moonR[k], 2, sx, sy, sz);
                    }
                }
            }

            for (int i = 0; i < len; i++)
            {
                dy[len + i] = acc[i];
            }
        };
    }

    /// <summary>Max relative position error over the 13 moons (Test544's metric).</summary>
    private static Func<double[], double[], double> QualificationSatelliteError(
        int[] moons, int[] centers)
    {
        return delegate (double[] s, double[] reference)
        {
            double worst = 0d;
            for (int i = 0; i < moons.Length; i++)
            {
                int m = moons[i];
                int c = centers[i];
                double dx = (s[3 * m] - s[3 * c]) - (reference[3 * m] - reference[3 * c]);
                double dy = (s[(3 * m) + 1] - s[(3 * c) + 1])
                    - (reference[(3 * m) + 1] - reference[(3 * c) + 1]);
                double dz = (s[(3 * m) + 2] - s[(3 * c) + 2])
                    - (reference[(3 * m) + 2] - reference[(3 * c) + 2]);
                worst = Math.Max(worst, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
            }

            return worst;
        };
    }

    /// <summary>
    /// One H task. The force model and reference path are exactly R2's (QualificationDe441Problem),
    /// so the H tier never invents a new model; only the epoch/window and the goals differ.
    /// </summary>
    private static QualificationProblem QualificationDe441HProblem(
        string id, string title, bool pn, string epochFile, string window, string[] tags,
        QualificationProfile profile, bool fullOnly)
    {
        QualificationProblem q = QualificationDe441Problem(
            id, pn, profile,
            epochFile: epochFile,
            window: window,
            title: title,
            tags: tags,
            modelName: pn ? "1PN" : "Newton",
            useHGoals: true,
            tenYearInSmoke: !fullOnly);
        q.AuditKind = "h";
        q.AuditCoarseSteps = 1024;
        return q;
    }

    /// <summary>
    /// B6 metric: (state, reference) -> { rms8, geocentric Moon }. The formula is the one of
    /// JplStateError: planets relative to the Sun, the Moon relative to the Earth. Both are in
    /// the problem's units (metres).
    /// </summary>
    private static Func<double[], double[], double[]> QualificationQNBodyMetric(
        int[] planets, int moon, int earth, double[][] horizonsPlanets = null)
    {
        return delegate (double[] s, double[] reference)
        {
            double sum = 0d;
            double horizonsSum = 0d;
            for (int i = 0; i < planets.Length; i++)
            {
                int b = planets[i];
                double dx = (s[3 * b] - s[0]) - (reference[3 * b] - reference[0]);
                double dy = (s[(3 * b) + 1] - s[1]) - (reference[(3 * b) + 1] - reference[1]);
                double dz = (s[(3 * b) + 2] - s[2]) - (reference[(3 * b) + 2] - reference[2]);
                sum += (dx * dx) + (dy * dy) + (dz * dz);
                if (horizonsPlanets != null)
                {
                    double hx = (s[3 * b] - s[0]) - horizonsPlanets[i][0];
                    double hy = (s[(3 * b) + 1] - s[1]) - horizonsPlanets[i][1];
                    double hz = (s[(3 * b) + 2] - s[2]) - horizonsPlanets[i][2];
                    horizonsSum += (hx * hx) + (hy * hy) + (hz * hz);
                }
            }

            double mx = (s[3 * moon] - s[3 * earth])
                - (reference[3 * moon] - reference[3 * earth]);
            double my = (s[(3 * moon) + 1] - s[(3 * earth) + 1])
                - (reference[(3 * moon) + 1] - reference[(3 * earth) + 1]);
            double mz = (s[(3 * moon) + 2] - s[(3 * earth) + 2])
                - (reference[(3 * moon) + 2] - reference[(3 * earth) + 2]);
            if (horizonsPlanets == null)
            {
                return new[]
                {
                    Math.Sqrt(sum / planets.Length),
                    Math.Sqrt((mx * mx) + (my * my) + (mz * mz))
                };
            }

            return new[]
            {
                Math.Sqrt(sum / planets.Length),
                Math.Sqrt((mx * mx) + (my * my) + (mz * mz)),
                Math.Sqrt(horizonsSum / planets.Length)
            };
        };
    }

    /// <summary>
    /// Horizons positions of the eight planets at the task's final time, in the same Sun-relative
    /// frame the Q metric uses. Null when the file has no slice at that time (for example a
    /// 30-day smoke horizon), and then the reference-only column is simply absent.
    /// </summary>
    private static double[][] QualificationHorizonsPlanets(
        JplFile jpl, string[] names, int[] planets, double days)
    {
        var result = new double[planets.Length][];
        for (int i = 0; i < planets.Length; i++)
        {
            string name = names[planets[i] - 1];
            JplBody body = null;
            foreach (JplBody candidate in jpl.Bodies)
            {
                if (candidate.Name == name)
                {
                    body = candidate;
                    break;
                }
            }

            if (body == null)
            {
                return null;
            }

            JplState state = null;
            foreach (JplState candidate in body.States)
            {
                if (Math.Abs(candidate.Days - days) < 1e-9d)
                {
                    state = candidate;
                    break;
                }
            }

            if (state == null)
            {
                return null;
            }

            result[i] = state.P;
        }

        return result;
    }

    /// <summary>
    /// G8 for the H tier: an independent refinement of the same certified fixed-step DOP853
    /// must agree with the stored reference within 10 x its own Richardson floor. This is the
    /// same audit as R2's, so a failure here means the reference is not converged.
    /// </summary>
    private static void QualificationAuditH(QualificationProblem q)
    {
        StudyProblem p = q.Problem;
        int coarse = q.AuditCoarseSteps > 0 ? q.AuditCoarseSteps : 1024;
        double floor2;
        double order2;
        string detail2;
        double[][] fine = QualificationCertifiedDop853(
            p, p.CheckpointTimes, coarse, out floor2, out order2, out detail2);
        double diff = 0d;
        for (int k = 0; k < fine.Length; k++)
        {
            diff = Math.Max(diff, QualificationMaxAbsDiff(fine[k], p.CheckpointRef[k]));
        }

        q.Residual = diff;
        q.Cross = Math.Max(q.Floor, floor2);
        double tol = Math.Max(10d * Math.Max(q.Floor, floor2), 1e-12d * p.ScaleLength);
        bool ok = double.IsFinite(q.Floor) && diff <= tol;
        string extra = "";

        if (q.Id == "H3")
        {
            // Cross-check against Test546's same-force reference (rtol 1e-14, maxStep 0.5 d).
            string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
            JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
            StudyNBody nb = JplNBody(jpl, QualificationNasaSmallBodyNames(jpl), true, true);
            var run = JplDop853(
                nb, 3650d, 1e-7d, 1e-10d, 1e-14d, true, false, true, 43200d, 10, true);
            double worst = 0d;
            for (int i = 0; i < p.Y0.Length / 2; i++)
            {
                worst = Math.Max(worst, Math.Abs(run.p[i] - p.CheckpointRef[p.CheckpointRef.Length - 1][i]));
            }

            double crossTol = Math.Max(10d * Math.Max(q.Floor, 1d), 10d);
            ok &= worst <= crossTol;
            q.Cross = Math.Max(q.Cross, worst);
            extra = "; Test546 cross-check " + QualificationFormat(worst)
                + " m (tol " + QualificationFormat(crossTol) + ")";
        }

        if (q.Id == "H6")
        {
            // Cross-check the force assembly against Test544's own JplDop853 run: the two
            // must agree at the level of their convergence, otherwise H6 is not the Test544
            // model (a missing J2 term would show up as thousands of km).
            string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
            JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
            StudyNBody nb = JplNBody(jpl, QualificationNasaSatelliteNames(), true, true);
            var run = JplDop853(
                nb, 3650d, 1e-6d, 1e-9d, 1e-13d, true, false, false, 21600d, -1, true, true);
            double worst = 0d;
            for (int i = 0; i < p.Y0.Length / 2; i++)
            {
                worst = Math.Max(worst, Math.Abs(run.p[i] - p.CheckpointRef[p.CheckpointRef.Length - 1][i]));
            }

            double crossTol = Math.Max(10d * Math.Max(q.Floor, 1d), 10d);
            ok &= worst <= crossTol;
            q.Cross = Math.Max(q.Cross, worst);
            extra = "; Test544 cross-check " + QualificationFormat(worst)
                + " m (tol " + QualificationFormat(crossTol) + ")";
        }

        q.Status = ok ? "VALID" : "INVALID";
        q.AuditNote = q.OracleNote + "; floor " + QualificationFormat(q.Floor)
            + "; refinement diff " + QualificationFormat(diff)
            + " (tol " + QualificationFormat(tol) + "); " + detail2 + extra
            + (ok ? "" : "; not converged")
            + "; Q E_model rms8 " + QualificationFormat(q.QModelRms8 / 1000d) + " km"
            + ", Moon " + QualificationFormat(q.QModelMoon / 1000d) + " km";
    }
}
