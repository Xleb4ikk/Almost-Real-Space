using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{
    /// <summary>
    /// ARCHIVED: production chunking is no longer developed and is excluded from
    /// the mode map and win counts. The code and CSV are kept as historical data.
    /// </summary>
    private sealed class ProductionChunkRow
    {
        public string Scenario;
        public double ChunkSeconds;
        public long RawCalls;
        public long RhsCost;
        public double MaxErrorM;
        public double Ms;
        public string Note;
    }

    /// <summary>
    /// Production-path chunking check on a combined LEO + J2 + drag + burn case.
    /// Uses the real SpacecraftPhysics / DOP853 path and the same 600 s coast /
    /// 0.05 s thrust split as VesselStep. The chunk-invariance rows are a separate
    /// experiment: the same trajectory with one fixed chunk size.
    /// </summary>
    private static int Test557_ProductionChunking()
    {
        Console.WriteLine("--- Production chunking: combined LEO + J2 + drag + burn ---");
        StarSystem sys = StudyShipMatrixSystem(true, true);
        OrbitingBody earth = sys.AllBodies[1];
        StudyShipMatrixOrbit(sys, 1, earth.Radius + 400000d, earth.Radius + 400000d,
            0d, 0d, 0d, out Vector3d p0, out Vector3d v0);
        double mu = earth.ResolveStandardGravitationalParameter();
        var c = new StudyShipMatrixCase
        {
            Label = "Production LEO + J2 + drag + 600 s burn",
            Sys = sys,
            Central = 1,
            P0 = p0,
            V0 = v0,
            Mass0 = 1000d,
            Seconds = 1200d,
            Thrust = true,
            ThrustDirection = v0.Normalized,
            ThrustStart = StudyShipT0 + 100d,
            ThrustSeconds = 600d,
            ThrustNewtons = 2000d,
            ThrustIsp = 300d,
            ThrustRampSeconds = 20d,
            AllowTaylor = false,
            WorldChunkSeconds = 600d,
            Tau = StudyShipMatrixTau(earth.Radius + 400000d, mu),
            StepScale = 0.02d,
            ScaleLength = earth.Radius + 400000d,
            Drag = new DragSource(earth),
            AllowUnratioedResolution = true,
            Mode = "production-combined",
            ModeTags = new[] { "earth", "leo", "j2", "drag", "thrust", "variable-mass", "production-chunking" },
            AltitudeKm = 400d,
            SpeedMps = v0.Magnitude,
            Eccentricity = 0d,
            HasBreakpoints = true
        };

        StudyShipMatrixSetTargets(c);
        StudyShipMatrixRefs refs = StudyShipMatrixBuildReferences(c);
        Console.WriteLine("reference floor: " + refs.ReferenceFloor.ToString("E3", CultureInfo.InvariantCulture)
            + " m; " + refs.LocalKind);
        if (!double.IsFinite(refs.ReferenceFloor))
        {
            Console.WriteLine("REFERENCE NOT CERTIFIED; production chunking table is diagnostic only");
        }

        var rows = new List<ProductionChunkRow>();
        // Main production pattern: 600 s coast, 0.05 s while thrust is on.
        ProductionChunkRow main = RunProductionChunk(
            c, refs, 600d, 0.05d, "600 s coast / 0.05 s thrust (game pattern)");
        rows.Add(main);

        // Chunk-invariance experiment: one fixed chunk size for the whole run.
        double[] chunks = { 0.05d, 1d, 600d, 86400d };
        double[] reference = null;
        foreach (double chunk in chunks)
        {
            ProductionChunkRow row = RunProductionChunk(
                c, refs, chunk, 0d, "fixed chunk " + chunk.ToString("G", CultureInfo.InvariantCulture) + " s");
            rows.Add(row);
            if (Math.Abs(chunk - 0.05d) < 1e-12d)
            {
                reference = null; // state comparison handled below by re-running
            }
        }

        // The row carries only the error vs the certified reference; for the
        // invariance statement compare the actual checkpoint states at 0.05/1/600/86400.
        var states = new Dictionary<double, Vector3d[]>();
        foreach (double chunk in chunks)
        {
            StudyShipRun run = StudyShipMatrixWorld(
                c, true, chunkSeconds: chunk, splitThrust: true,
                checkpoints: refs.CheckpointTimes);
            var local = new Vector3d[Math.Min(run.SamplesRecorded, refs.CheckpointP.Length)];
            for (int i = 0; i < local.Length; i++)
            {
                local[i] = run.SamplesP[i];
            }

            states[chunk] = local;
        }

        Vector3d[] baseline = states[0.05d];
        var invariance = new StringBuilder();
        invariance.AppendLine("scenario,chunk_seconds,max_delta_vs_0.05_m");
        foreach (double chunk in chunks)
        {
            double worst = 0d;
            Vector3d[] other = states[chunk];
            int n = Math.Min(baseline.Length, other.Length);
            for (int i = 0; i < n; i++)
            {
                worst = Math.Max(worst, (other[i] - baseline[i]).Magnitude);
            }

            invariance.Append(c.Label.Replace(',', ';')).Append(',')
                .Append(chunk.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(worst.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        string dir = StudyShipMatrixResultsDir();
        Directory.CreateDirectory(dir);
        string csv = Path.Combine(dir, "Test557_ProductionChunking.csv");
        var sb = new StringBuilder();
        sb.AppendLine("scenario,chunk_seconds,raw_calls,rhs_eq,max_err_m,ms,note");
        foreach (ProductionChunkRow row in rows)
        {
            sb.Append(row.Scenario.Replace(',', ';')).Append(',')
                .Append(row.ChunkSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.RawCalls.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.RhsCost.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.MaxErrorM.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Ms.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Note.Replace(',', ';')).AppendLine();
        }

        File.WriteAllText(csv, sb.ToString());
        File.WriteAllText(Path.Combine(dir, "Test557_ChunkInvariance.csv"), invariance.ToString());

        foreach (ProductionChunkRow row in rows)
        {
            Console.WriteLine("chunk " + row.ChunkSeconds.ToString("G", CultureInfo.InvariantCulture)
                + " s: raw=" + row.RawCalls.ToString("N0", CultureInfo.InvariantCulture)
                + " rhs_eq=" + row.RhsCost.ToString("N0", CultureInfo.InvariantCulture)
                + " max_err=" + row.MaxErrorM.ToString("E3", CultureInfo.InvariantCulture) + " m"
                + " ms=" + row.Ms.ToString("F1", CultureInfo.InvariantCulture));
        }

        Console.WriteLine(invariance.ToString());
        Check(rows.Count > 0, "production chunking ran", rows.Count.ToString(CultureInfo.InvariantCulture));
        Check(main.RawCalls > 0, "production chunking counted RHS", main.RawCalls.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("report: " + csv);
        Console.WriteLine("invariance: " + Path.Combine(dir, "Test557_ChunkInvariance.csv"));
        return 0;
    }

    private static ProductionChunkRow RunProductionChunk(
        StudyShipMatrixCase c, StudyShipMatrixRefs refs,
        double chunkSeconds, double thrustChunkSeconds, string note)
    {
        StudyShipRun run = StudyShipMatrixWorld(
            c, true, chunkSeconds: chunkSeconds, splitThrust: true,
            checkpoints: refs.CheckpointTimes, thrustChunkSeconds: thrustChunkSeconds);
        double worst = 0d;
        int n = run.SamplesRecorded;
        for (int i = 0; i < n; i++)
        {
            worst = Math.Max(worst, (run.SamplesP[i] - refs.CheckpointP[i]).Magnitude);
        }

        return new ProductionChunkRow
        {
            Scenario = c.Label,
            ChunkSeconds = chunkSeconds,
            RawCalls = run.RawCalls,
            RhsCost = run.Calls,
            MaxErrorM = worst,
            Ms = run.Ms,
            Note = note
        };
    }
}
