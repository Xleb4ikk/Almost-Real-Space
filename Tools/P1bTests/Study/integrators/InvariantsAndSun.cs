using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using P1bStudy.Framework;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// J2/inclination boundary for the Kepler-split methods, a normalized Sun
    /// scenario (so absolute-coordinate round-off cannot hide WH-BM14A/Encke),
    /// and invariant-drift tables for the pure-gravity rows.
    /// </summary>
    private static int Test558_J2InclinationAndNormalizedSun()
    {
        Console.WriteLine("--- Integrator framework: J2/inclination boundary + normalized Sun ---");
        var problems = new List<StudyProblem>();

        double[] factors = { 0d, 0.1d, 1d, 10d };
        double[] inclinations = { 0d, 45d, 90d, 98d };
        foreach (double factor in factors)
        {
            StarSystem sys = StudyShipMatrixJ2FactorSystem(factor);
            foreach (double inc in inclinations)
            {
                StudyShipMatrixCase c = StudyShipMatrixCaseOrbit(
                    "J2 x" + factor.ToString("G", CultureInfo.InvariantCulture)
                        + " inc " + inc.ToString("G", CultureInfo.InvariantCulture),
                    sys, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d,
                    inc, 0d, 0d, 86400d, true, false, 1d);
                c.Mode = "j2-sensitivity";
                c.ModeTags = new[]
                {
                    "earth", "leo", "400km", "circular", "j2",
                    "j2x" + factor.ToString("G", CultureInfo.InvariantCulture),
                    "inc" + inc.ToString("G", CultureInfo.InvariantCulture)
                };
                StudyShipMatrixSetTargets(c);
                StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(c);
                StudyProblem sp = StudyBridgeShipProblem(c, refs);
                StudyBridgeAttachSecondReference(sp);
                problems.Add(sp);
            }
        }

        problems.Add(NormalizedSunProblem());

        var options = new StudyOptions();
        options.RelTargets = new double[0];
        options.AbsTargets = new[] { 100d, 10d, 1d, 0.1d, 0.01d };
        options.AdaptiveMaxSteps = new[] { double.PositiveInfinity, 600d };
        options.MaxCostRhs = 2000000L;
        options.MaxPoints = 50;
        if (StudyBridgeEnv("P1B_NO_REFINE") == "1")
        {
            options.RefineTies = false;
        }

        string filter = StudyBridgeEnv("P1B_SCENARIOS");
        List<IStudyIntegrator> integrators = StudyRegistry.Select(StudyBridgeEnv("P1B_INTEGRATORS"));
        var results = new List<StudyScenarioResult>();
        var endResults = new List<StudyScenarioResult>();
        foreach (StudyProblem p in problems)
        {
            if (!StudyBridgeScenarioSelected(p.Label, filter))
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine("sweeping: " + p.Label);
            StudyScenarioResult r = StudySweepRunner.Run(p, integrators, options, Console.Out);
            results.Add(r);
            StudyReport.Print(Console.Out, r, options);
            StudyScenarioResult rEnd = StudySweepRunner.RunEndOnly(p, integrators, options, null);
            endResults.Add(rEnd);
            Console.WriteLine("  end-only: " + rEnd.Rows.Count.ToString(CultureInfo.InvariantCulture) + " points");
        }

        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        StudyReport.WriteCsv(Path.Combine(dir, "Test558_J2AndSun.csv"), results);
        StudyReport.WriteCsv(Path.Combine(dir, "Test558_J2AndSun_endonly.csv"), endResults);
        StudyReport.WriteCellsCsv(Path.Combine(dir, "Test558_J2AndSun_cells.csv"),
            results, endResults, options);
        if (!StudyBridgeSkipSummaries())
        {
            StudyReport.WriteMarkdown(Path.Combine(dir, "Test558_J2AndSun.md"), results, options);
            StudyModeSummary.Write(Path.Combine(dir, "Test558_ModeSummary.md"), "Test558", results, options);
        }

        var drift = new StringBuilder();
        drift.AppendLine("scenario,integrator,variant,config,err_m,"
            + "energy_drift_from_initial,angular_drift_from_initial,"
            + "energy_drift_vs_ref,energy_drift_ref,angular_drift_vs_ref,angular_drift_ref");
        foreach (StudyScenarioResult r in results)
        {
            if (r.Problem.EnergyFn == null && r.Problem.AngularMomentumFn == null)
            {
                continue;
            }

            foreach (StudyRow row in r.Rows)
            {
                if (row.Status != "ok" || !row.Finite)
                {
                    continue;
                }

                drift.Append(r.Problem.Label.Replace(',', ';')).Append(',')
                    .Append(row.Integrator.Replace(',', ';')).Append(',')
                    .Append(row.Variant.Replace(',', ';')).Append(',')
                    .Append(row.Config.Replace(',', ';')).Append(',')
                    .Append(row.Err.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.EnergyDriftFromInitial.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.AngularDriftFromInitial.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.EnergyDriftVsRef.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.EnergyDriftRef.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.AngularDriftVsRef.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.AngularDriftRef.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            }
        }

        File.WriteAllText(Path.Combine(dir, "Test558_Invariants.csv"), drift.ToString());

        int failed = 0;
        int uncertified = 0;
        foreach (StudyScenarioResult r in results)
        {
            foreach (StudyRow row in r.Rows)
            {
                if (row.Status == "error")
                {
                    failed++;
                    Console.WriteLine("FAILED RUN " + row.Scenario + " / " + row.Key + ": " + row.Note);
                }
            }

            if (!r.Problem.Certified)
            {
                uncertified++;
                Console.WriteLine("UNCERTIFIED " + r.Problem.Label);
            }
        }

        Check(results.Count > 0, "J2/Sun sweep ran",
            results.Count.ToString(CultureInfo.InvariantCulture));
        Check(failed == 0, "J2/Sun sweep: no failed runs",
            failed.ToString(CultureInfo.InvariantCulture));
        Check(uncertified == 0, "J2/Sun sweep: every scenario certified",
            uncertified.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("report: " + Path.Combine(dir, "Test558_J2AndSun.md"));
        return 0;
    }

    private static StarSystem StudyShipMatrixJ2FactorSystem(double factor)
    {
        StarSystem sys = StudyRealSolar7();
        OrbitingBody earth = sys.AllBodies[1];
        earth.RotationPeriodSeconds = 86164.0905d;
        earth.J2 = 1.08263e-3d * factor;
        StudyNBody nb = StudyMakeNBody(sys);
        StudyBake("ship matrix J2 x" + factor.ToString("G", CultureInfo.InvariantCulture),
            sys, nb, 0.25d, 0d);
        return sys;
    }

    private static StudyProblem NormalizedSunProblem()
    {
        const double period = 2d * Math.PI;
        var p = new StudyProblem
        {
            Label = "Sun 1 AU normalized (dimensionless)",
            Mode = "sun-normalized",
            ModeTags = new[] { "sun", "1au", "circular", "normalized", "pure-gravity", "central", "kepler-exact" },
            KeplerExact = true,
            Dim = 6,
            PosDim = 3,
            T0 = 0d,
            Y0 = new[] { 1d, 0d, 0d, 0d, 1d, 0d },
            ConservativeSecondOrder = true,
            CentralMu = 1d,
            ScaleLength = 1d,
            CharTime = period,
            Floor = 1e-9d,
            Certified = true,
            ReferenceNote = "dimensionless circular Kepler; error reported in meters",
            ReferenceMethod = "analytic circular Kepler",
            ReferenceFamily = "Analytic",
            ReferenceIndependent = true,
            ReferenceResolution = "ratio_ok",
            ReferenceCrossDiffM = 1e-9d,
            ReferenceSecondMethod = "closed-form circle",
            AltitudeKm = double.NaN,
            SpeedMps = 29780d,
            Eccentricity = 0d
        };
        p.Rhs = delegate (double t, double[] y, double[] dy)
        {
            double r2 = (y[0] * y[0]) + (y[1] * y[1]) + (y[2] * y[2]);
            double r3 = r2 * Math.Sqrt(r2);
            dy[0] = y[3];
            dy[1] = y[4];
            dy[2] = y[5];
            dy[3] = -y[0] / r3;
            dy[4] = -y[1] / r3;
            dy[5] = -y[2] / r3;
        };
        p.ErrorFn = delegate (double[] s, double[] r)
        {
            double dx = s[0] - r[0];
            double dy = s[1] - r[1];
            double dz = s[2] - r[2];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) * StudyAU;
        };
        p.EnergyFn = delegate (double[] s)
        {
            double r2 = (s[0] * s[0]) + (s[1] * s[1]) + (s[2] * s[2]);
            double v2 = (s[3] * s[3]) + (s[4] * s[4]) + (s[5] * s[5]);
            return (0.5d * v2) - (1d / Math.Sqrt(r2));
        };
        p.AngularMomentumFn = delegate (double[] s)
        {
            double lx = (s[1] * s[5]) - (s[2] * s[4]);
            double ly = (s[2] * s[3]) - (s[0] * s[5]);
            double lz = (s[0] * s[4]) - (s[1] * s[3]);
            return Math.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
        };

        int count = 16;
        var times = new double[count];
        var refs = new double[count][];
        for (int i = 0; i < count; i++)
        {
            double t = period * (i + 1) / count;
            times[i] = t;
            refs[i] = new[]
            {
                Math.Cos(t), Math.Sin(t), 0d,
                -Math.Sin(t), Math.Cos(t), 0d
            };
        }

        p.CheckpointTimes = times;
        p.CheckpointRef = refs;
        return p;
    }
}
