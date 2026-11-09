using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using P1bStudy.Framework;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// Edge-case sweep that the broad Test552 matrix deliberately leaves out:
    /// a Moon-centered SOI flyby (the frame production uses inside the lunar SOI),
    /// a hyperbolic Earth flyby at 1 cm, and the 30 d / 1000-orbit long-warp cases
    /// graded against the local reference with the DD endpoint kept as a diagnostic.
    ///
    /// All integrators stay in the comparison; no method is removed because it did
    /// not win the broad matrix.
    /// </summary>
    private static int Test554_IntegratorFrameworkOrbitEdgeCases()
    {
        Console.WriteLine("--- Integrator framework: orbit edge cases ---");
        StarSystem gravity = StudyShipMatrixSystem(false, false);
        StarSystem j2OnlySystem = StudyShipMatrixSystem(true, false);
        StarSystem dragOnlySystem = StudyShipMatrixSystem(false, true);
        List<StudyShipMatrixCase> all = StudyShipMatrixBuildCases(
            gravity, j2OnlySystem, dragOnlySystem);

        var selected = new List<StudyShipMatrixCase>();
        foreach (StudyShipMatrixCase c in all)
        {
            bool moon = c.Label.IndexOf("Moon-centered", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hyperbola = c.Label.IndexOf("hyperbolic", StringComparison.OrdinalIgnoreCase) >= 0;
            bool thirtyDays = c.Label.IndexOf("30 d", StringComparison.OrdinalIgnoreCase) >= 0;
            bool thousandOrbits = c.Label.IndexOf("1000 orbits", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!moon && !hyperbola && !thirtyDays && !thousandOrbits)
            {
                continue;
            }

            if (thousandOrbits)
            {
                // The local DOP853 reference's own convergence is ~1.8e-4 m.
                // The independent DD endpoint differs by ~8.5 m because it is a
                // different force formulation (world-frame DD vs the game's local
                // frame). Grade against the local reference, but keep the DD
                // cross-check visible in the reference note instead of making the
                // 1 m column unreachable.
                c.LocalReferenceOnly = true;
            }

            selected.Add(c);
        }

        if (StudyBridgeEnv("P1B_SEAM_PROBE") == "1")
        {
            StudyShipMatrixCase leo = selected.Find(c => c.Label == "Earth LEO 400 km, 30 d");
            if (leo != null)
            {
                string probeDir = StudyShipMatrixResultsDir();
                Directory.CreateDirectory(probeDir);
                StudyShipMatrixSetTargets(leo);
                StudyShipMatrixRefs leoRefs = StudyShipMatrixBuildReferences(leo);
                StudyProblem leoProblem = StudyBridgeShipProblem(leo, leoRefs);
                StudyBridgeAttachSecondReference(leoProblem);
                StudySeamProbe.Run(Console.Out, Path.Combine(probeDir, "Test554_SeamProbe.csv"),
                    leo.Label, gravity, leoProblem, 1e-11d, 0.001d, 2772.427547990396d);
                Console.WriteLine("seam probe: " + Path.Combine(probeDir, "Test554_SeamProbe.csv"));
            }

            return 0;
        }

        string scenarioFilter = StudyBridgeEnv("P1B_SCENARIOS");
        List<IStudyIntegrator> integrators = StudyRegistry.Select(StudyBridgeEnv("P1B_INTEGRATORS"));

        var options = new StudyOptions();
        options.RelTargets = new double[0];
        // 1 cm is the point of the hyperbolic test: at 100/10/1 m the fixed-step
        // methods are already below target at their cheapest grid point, so the
        // 1 m cell is an unresolved EDGE and says nothing about the cost.
        options.AbsTargets = new[] { 100d, 10d, 1d, 0.1d, 0.01d };
        options.AdaptiveMaxSteps = new[] { double.PositiveInfinity, 600d };
        // The 1000-orbit rows can exceed the broad sweep's 2M budget once the
        // local floor makes the 1 m/10 cm targets reachable.
        options.MaxCostRhs = 4000000L;
        options.MaxPoints = 60;
        if (StudyBridgeEnv("P1B_NO_REFINE") == "1")
        {
            options.RefineTies = false;
        }

        var results = new List<StudyScenarioResult>();
        var endResults = new List<StudyScenarioResult>();
        var production = new StringBuilder();
        production.AppendLine("scenario,method,chunk_seconds,raw_calls,rhs_eq,err_max_m,ms");

        foreach (StudyShipMatrixCase c in selected)
        {
            if (!StudyBridgeScenarioSelected(c.Label, scenarioFilter))
            {
                continue;
            }

            StudyShipMatrixSetTargets(c);
            StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(c);
            StudyProblem problem = StudyBridgeShipProblem(c, refs);
            StudyBridgeAttachSecondReference(problem);
            Console.WriteLine();
            Console.WriteLine("sweeping: " + c.Label);
            StudyScenarioResult r = StudySweepRunner.Run(problem, integrators, options, Console.Out);
            results.Add(r);
            StudyReport.Print(Console.Out, r, options);
            StudyScenarioResult rEnd = StudySweepRunner.RunEndOnly(problem, integrators, options, null);
            endResults.Add(rEnd);
            Console.WriteLine("  end-only: " + rEnd.Rows.Count.ToString(CultureInfo.InvariantCulture) + " points");

            // The sweep measures the method in the harness. The production row
            // measures the actual SpacecraftPhysics/DOP853 path at the same chunk
            // size the game uses on a coast (600 s), so the long-warp cost is not
            // confused with the tuned harness cost.
            if (c.Label.IndexOf("30 d", StringComparison.OrdinalIgnoreCase) >= 0
                || c.Label.IndexOf("1000 orbits", StringComparison.OrdinalIgnoreCase) >= 0
                || c.Label.IndexOf("Moon-centered", StringComparison.OrdinalIgnoreCase) >= 0
                || c.Label.IndexOf("hyperbolic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                StudyShipRun run = StudyShipMatrixWorld(
                    c, true, chunkSeconds: 600d, checkpoints: refs.CheckpointTimes);
                double worst = 0d;
                int count = run.SamplesP == null
                    ? 0
                    : Math.Min(run.SamplesRecorded, Math.Min(run.SamplesP.Length, refs.CheckpointP.Length));
                for (int i = 0; i < count; i++)
                {
                    worst = Math.Max(worst, (run.SamplesP[i] - refs.CheckpointP[i]).Magnitude);
                }

                Console.WriteLine("production-local DOP853 600 s: raw_calls="
                    + run.RawCalls.ToString("N0", CultureInfo.InvariantCulture)
                    + " rhs_eq=" + run.Calls.ToString("N0", CultureInfo.InvariantCulture)
                    + " max_err=" + worst.ToString("E3", CultureInfo.InvariantCulture) + " m"
                    + " ms=" + run.Ms.ToString("F1", CultureInfo.InvariantCulture));
                production.Append(c.Label.Replace(',', ';')).Append(',')
                    .Append("DOP853 production-local").Append(',')
                    .Append("600").Append(',')
                    .Append(run.RawCalls.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(run.Calls.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(worst.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(run.Ms.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                Check(double.IsFinite(worst),
                    "production path finite: " + c.Label, worst.ToString("E3", CultureInfo.InvariantCulture));
            }
        }

        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        StudyReport.WriteCsv(Path.Combine(dir, "Test554_FrameworkOrbitEdgeCases.csv"), results);
        StudyReport.WriteCsv(Path.Combine(dir, "Test554_FrameworkOrbitEdgeCases_endonly.csv"), endResults);
        StudyReport.WriteCellsCsv(Path.Combine(dir, "Test554_FrameworkOrbitEdgeCases_cells.csv"),
            results, endResults, options);
        if (!StudyBridgeSkipSummaries())
        {
            StudyReport.WriteMarkdown(Path.Combine(dir, "Test554_FrameworkOrbitEdgeCases.md"), results, options);
            StudyModeSummary.Write(Path.Combine(dir, "Test554_ModeSummary.md"), "Test554", results, options);
        }

        File.WriteAllText(Path.Combine(dir, "Test554_Production.csv"), production.ToString());

        int failedRuns = 0;
        int uncertified = 0;
        foreach (StudyScenarioResult r in results)
        {
            foreach (StudyRow row in r.Rows)
            {
                if (row.Status == "error")
                {
                    failedRuns++;
                    Console.WriteLine("FAILED RUN " + row.Scenario + " / " + row.Key + " / "
                        + row.Config + ": " + row.Note);
                }
            }

            if (!r.Problem.Certified)
            {
                uncertified++;
                Console.WriteLine("UNCERTIFIED " + r.Problem.Label + ": " + r.Problem.ReferenceNote);
            }
        }

        Check(results.Count > 0, "edge-case sweep ran scenarios",
            results.Count.ToString(CultureInfo.InvariantCulture) + " scenario(s)");
        Check(failedRuns == 0, "edge-case sweep: no failed runs",
            failedRuns.ToString(CultureInfo.InvariantCulture) + " failed run(s)");
        Check(uncertified == 0, "edge-case sweep: every scenario is certified",
            uncertified.ToString(CultureInfo.InvariantCulture) + " uncertified scenario(s)");
        Console.WriteLine("report: " + Path.Combine(dir, "Test554_FrameworkOrbitEdgeCases.md"));
        Console.WriteLine("production table: " + Path.Combine(dir, "Test554_Production.csv"));
        return 0;
    }
}
