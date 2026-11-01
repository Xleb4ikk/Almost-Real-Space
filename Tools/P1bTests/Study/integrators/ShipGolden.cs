using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    // ------------------------------------------------------------------
    // Golden cache for the expensive independent dd-Taylor reference of the
    // 1000-orbit (~64 d) case. Recomputing K20+K24 there takes minutes and used to
    // dominate the whole matrix; the reference is deterministic
    // and its inputs are pinned by the fingerprint below. Refresh explicitly
    // with P1B_REFRESH_GOLDEN=1 (or delete the file). A fingerprint mismatch
    // rebuilds and overwrites instead of silently grading against another case.
    // ------------------------------------------------------------------

    private sealed class StudyShipDdGoldenFile
    {
        [JsonPropertyName("label")] public string Label { get; set; }
        [JsonPropertyName("t0")] public double T0 { get; set; }
        [JsonPropertyName("seconds")] public double Seconds { get; set; }
        [JsonPropertyName("p0")] public double[] P0 { get; set; }
        [JsonPropertyName("v0")] public double[] V0 { get; set; }
        [JsonPropertyName("order_a")] public int OrderA { get; set; }
        [JsonPropertyName("order_b")] public int OrderB { get; set; }
        [JsonPropertyName("tol_a")] public double TolA { get; set; }
        [JsonPropertyName("tol_b")] public double TolB { get; set; }
        [JsonPropertyName("step_a")] public double StepA { get; set; }
        [JsonPropertyName("step_b")] public double StepB { get; set; }
        [JsonPropertyName("p_rel")] public double[] PRel { get; set; }
        [JsonPropertyName("v_rel")] public double[] VRel { get; set; }
        [JsonPropertyName("self_error_m")] public double SelfErrorM { get; set; }
        [JsonPropertyName("calls")] public long Calls { get; set; }
        [JsonPropertyName("ms")] public double Ms { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; }
    }

    private static string StudyShipDdGoldenPath()
    {
        // A checkout keeps the tracked golden under Study/ (and refreshes it
        // there); a published harness reads the copy next to the executable
        // (csproj Link -> Data/ship_1000_orbits_dd.json).
        string repo = Path.GetFullPath(Path.Combine(
            StudyShipMatrixResultsDir(), "..", "golden", "ship_1000_orbits_dd.json"));
        if (File.Exists(repo))
        {
            return repo;
        }

        string published = Path.Combine(
            AppContext.BaseDirectory, "Data", "ship_1000_orbits_dd.json");
        return File.Exists(published) ? published : repo;
    }

    private static bool StudyShipDdGoldenRefreshRequested()
    {
        string value = Environment.GetEnvironmentVariable("P1B_REFRESH_GOLDEN");
        return !string.IsNullOrEmpty(value)
            && !string.Equals(value, "0", StringComparison.Ordinal);
    }

    private static bool SameGold(double a, double b)
    {
        return Math.Abs(a - b) <= 1e-12d * Math.Max(1d, Math.Max(Math.Abs(a), Math.Abs(b)));
    }

    private static bool StudyShipDdGoldenMatches(
        StudyShipDdGoldenFile g, StudyShipMatrixCase c)
    {
        return g != null && g.P0 != null && g.P0.Length == 3
            && g.V0 != null && g.V0.Length == 3
            && string.Equals(g.Label, c.Label, StringComparison.Ordinal)
            && SameGold(g.T0, StudyShipT0)
            && SameGold(g.Seconds, c.Seconds)
            && SameGold(g.P0[0], c.P0.X)
            && SameGold(g.P0[1], c.P0.Y)
            && SameGold(g.P0[2], c.P0.Z)
            && SameGold(g.V0[0], c.V0.X)
            && SameGold(g.V0[1], c.V0.Y)
            && SameGold(g.V0[2], c.V0.Z);
    }

    private static StudyShipDdGoldenFile StudyShipDdGoldenBuild(StudyShipMatrixCase c)
    {
        OrbitingBody central = c.Sys.AllBodies[c.Central];
        c.Sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
        const int orderA = 20;
        const int orderB = 24;
        const double tolA = 1e-12d;
        const double tolB = 1e-14d;
        const double stepA = 600d;
        const double stepB = 300d;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t1 = TaylorShip.RunDd(
            c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d,
            orderA, tolA, 1e-15d, stepA, 1e-6d);
        var t2 = TaylorShip.RunDd(
            c.Sys, bp + c.P0, bv + c.V0, StudyShipT0, c.Seconds / 86400d,
            orderB, tolB, 1e-17d, stepB, 1e-6d);
        sw.Stop();
        c.Sys.EvaluateBodyState(central, StudyShipT0 + c.Seconds,
            out Vector3d bpT, out Vector3d bvT);
        Vector3d pRel = t2.P - bpT;
        Vector3d vRel = t2.V - bvT;
        return new StudyShipDdGoldenFile
        {
            Label = c.Label,
            T0 = StudyShipT0,
            Seconds = c.Seconds,
            P0 = new[] { c.P0.X, c.P0.Y, c.P0.Z },
            V0 = new[] { c.V0.X, c.V0.Y, c.V0.Z },
            OrderA = orderA,
            OrderB = orderB,
            TolA = tolA,
            TolB = tolB,
            StepA = stepA,
            StepB = stepB,
            PRel = new[] { pRel.X, pRel.Y, pRel.Z },
            VRel = new[] { vRel.X, vRel.Y, vRel.Z },
            SelfErrorM = (t1.P - t2.P).Magnitude,
            Calls = t1.Calls + t2.Calls,
            Ms = sw.Elapsed.TotalMilliseconds,
            Note = "K20/K24 world-frame dd-Taylor, endpoint projected to the Earth-local frame"
        };
    }

    private static StudyShipDdGoldenFile StudyShipDdGolden(StudyShipMatrixCase c)
    {
        string path = StudyShipDdGoldenPath();
        StudyShipDdGoldenFile golden = null;
        if (File.Exists(path) && !StudyShipDdGoldenRefreshRequested())
        {
            try
            {
                golden = JsonSerializer.Deserialize<StudyShipDdGoldenFile>(
                    File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                Console.WriteLine("golden dd: unreadable " + path + " (" + ex.Message + "), rebuilding");
            }
        }

        if (StudyShipDdGoldenMatches(golden, c))
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "golden dd reference: {0} (self {1:E2} m, built {2:F0} s); set P1B_REFRESH_GOLDEN=1 to rebuild",
                Path.GetFileName(path), golden.SelfErrorM, golden.Ms / 1000d));
            return golden;
        }

        if (golden != null)
        {
            Console.WriteLine("golden dd: fingerprint mismatch, rebuilding " + path);
        }
        else
        {
            Console.WriteLine("golden dd: building " + path + " (K20+K24; a few minutes)");
        }

        golden = StudyShipDdGoldenBuild(c);
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(
            golden, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, true);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "golden dd: wrote {0} (self {1:E2} m, calls {2:N0}, {3:F0} s)",
            path, golden.SelfErrorM, golden.Calls, golden.Ms / 1000d));
        return golden;
    }
}
