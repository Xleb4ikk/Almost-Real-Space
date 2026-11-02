using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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
        [JsonPropertyName("model")] public string Model { get; set; }
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

    /// <summary>
    /// Stable SHA-256 of the baked Chebyshev coefficients. Metadata alone
    /// (segment count/length/degree/T0) does not change when the baker, its
    /// quantization or a coefficient changes, so a golden could silently survive
    /// a different ephemeris. Coefficients are hashed explicitly; endianness is
    /// fixed so the fingerprint is checkout/platform independent.
    /// </summary>
    private static string StudyShipBakeCoefficientHash(BakedEphemeris baked)
    {
        if (baked == null || baked.Coeffs == null)
        {
            return "none";
        }

        using (SHA256 sha = SHA256.Create())
        {
            var bytes = new byte[8];
            for (int i = 0; i < baked.Coeffs.Length; i++)
            {
                long bits = BitConverter.DoubleToInt64Bits(baked.Coeffs[i]);
                for (int b = 0; b < 8; b++)
                {
                    bytes[b] = (byte)(bits >> (8 * b));
                }

                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash);
        }
    }

    /// <summary>
    /// Everything the dd golden depends on besides its endpoint state. The old
    /// fingerprint only covered t0/seconds/P0/V0, so a force-model or ephemeris
    /// change silently reused a golden built by another model. Readable text, not
    /// a hash: a mismatch can be diagnosed from the two strings.
    /// </summary>
    private static string StudyShipDdGoldenModel(StudyShipMatrixCase c)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("dd-world force=per-body-indirect v2")
            .Append("; central=").Append(c.Central.ToString(CultureInfo.InvariantCulture))
            .Append("; mass0=").Append(c.Mass0.ToString("R", CultureInfo.InvariantCulture))
            .Append("; perturbed=").Append(c.Perturbed ? "1" : "0")
            .Append("; worldChunk=").Append(c.WorldChunkSeconds.ToString("R", CultureInfo.InvariantCulture));
        if (c.Drag != null)
        {
            sb.Append("; drag=").Append(c.Drag.DragCoefficient.ToString("R", CultureInfo.InvariantCulture))
                .Append('/').Append(c.Drag.ReferenceAreaM2.ToString("R", CultureInfo.InvariantCulture));
        }

        if (c.Thrust)
        {
            sb.Append("; thrust=")
                .Append(c.ThrustDirection.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(c.ThrustDirection.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(c.ThrustDirection.Z.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                .Append(c.ThrustStart.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                .Append(c.ThrustSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                .Append(c.ThrustNewtons.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                .Append(c.ThrustIsp.ToString("R", CultureInfo.InvariantCulture)).Append('/')
                .Append(c.ThrustRampSeconds.ToString("R", CultureInfo.InvariantCulture));
        }

        for (int i = 0; i < c.Sys.AllBodies.Count; i++)
        {
            OrbitingBody body = c.Sys.AllBodies[i];
            sb.Append("; b").Append(i.ToString(CultureInfo.InvariantCulture))
                .Append('=').Append(body.Name)
                .Append(" parent=").Append(body.Parent == null ? "-" : body.Parent.Name)
                .Append(" mu=").Append(body.ResolveStandardGravitationalParameter()
                    .ToString("R", CultureInfo.InvariantCulture))
                .Append(" R=").Append(body.Radius.ToString("R", CultureInfo.InvariantCulture))
                .Append(" J2=").Append(body.J2.ToString("R", CultureInfo.InvariantCulture))
                .Append(" J2R=").Append(body.J2ReferenceRadius.ToString("R", CultureInfo.InvariantCulture))
                .Append(" atm=").Append(body.Atmosphere == null ? "0" : "1");
            BakedEphemeris baked = body.Baked;
            if (baked != null)
            {
                sb.Append(" bake=").Append(baked.SegmentCount.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(baked.SegmentLengthSeconds.ToString("R", CultureInfo.InvariantCulture))
                    .Append('/').Append(baked.Degree.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(baked.T0Seconds.ToString("R", CultureInfo.InvariantCulture))
                    .Append(" coeff=").Append(StudyShipBakeCoefficientHash(baked));
            }
        }

        return sb.ToString();
    }

    private static bool StudyShipDdGoldenMatches(
        StudyShipDdGoldenFile g, StudyShipMatrixCase c)
    {
        return g != null && g.P0 != null && g.P0.Length == 3
            && g.V0 != null && g.V0.Length == 3
            && string.Equals(g.Label, c.Label, StringComparison.Ordinal)
            && string.Equals(g.Model, StudyShipDdGoldenModel(c), StringComparison.Ordinal)
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
        // K20 and K24 can agree bit-for-bit after projection to double, which is
        // NOT a zero error. Report the double-precision roundoff floor of the
        // relative state instead: it is the smallest error this representation
        // can resolve, and it keeps downstream budgets non-zero.
        double selfError = (t1.P - t2.P).Magnitude;
        double roundoffFloor = ToleranceReference.FloatingPointFloor
            * Math.Max(pRel.Magnitude, 1d);
        if (!(selfError > roundoffFloor))
        {
            selfError = roundoffFloor;
        }

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
            Model = StudyShipDdGoldenModel(c),
            PRel = new[] { pRel.X, pRel.Y, pRel.Z },
            VRel = new[] { vRel.X, vRel.Y, vRel.Z },
            SelfErrorM = selfError,
            Calls = t1.Calls + t2.Calls,
            Ms = sw.Elapsed.TotalMilliseconds,
            Note = "K20/K24 world-frame dd-Taylor, endpoint projected to the Earth-local frame; "
                + "self_error_m is max(|K20-K24|, double roundoff floor), not zero when they agree bitwise"
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

    private static void StudyShipRefDiagDdForCase(
        StudyShipMatrixCase c, StudyShipMatrixRefs refs)
    {
        Console.WriteLine();
        Console.WriteLine("=== dd diagnostic: " + c.Label + " ===");
        OrbitingBody central = c.Sys.AllBodies[c.Central];
        c.Sys.EvaluateBodyState(central, StudyShipT0, out Vector3d bp, out Vector3d bv);
        Vector3d certP = bp + c.P0;
        Vector3d certV = bv + c.V0;
        StudyShipDdGoldenFile golden = StudyShipDdGolden(c);
        Vector3d goldenLocalP = new Vector3d(golden.P0[0], golden.P0[1], golden.P0[2]);
        Vector3d goldenLocalV = new Vector3d(golden.V0[0], golden.V0[1], golden.V0[2]);
        Vector3d goldenP = bp + goldenLocalP;
        Vector3d goldenV = bv + goldenLocalV;

        Console.WriteLine("  central velocity source: "
            + (central.Baked != null
                ? "baked Chebyshev derivative (BakedEphemeris.TryEvaluate)"
                : "Kepler/local rail"));
        Console.WriteLine("  initial world certified: p=("
            + certP.X.ToString("R", CultureInfo.InvariantCulture) + ", "
            + certP.Y.ToString("R", CultureInfo.InvariantCulture) + ", "
            + certP.Z.ToString("R", CultureInfo.InvariantCulture) + ") m, v=("
            + certV.X.ToString("R", CultureInfo.InvariantCulture) + ", "
            + certV.Y.ToString("R", CultureInfo.InvariantCulture) + ", "
            + certV.Z.ToString("R", CultureInfo.InvariantCulture) + ") m/s");
        Console.WriteLine("  initial world production: same bp+c.P0, bv+c.V0 as StudyShipMatrixWorld");
        Console.WriteLine("  initial world golden: p=("
            + goldenP.X.ToString("R", CultureInfo.InvariantCulture) + ", "
            + goldenP.Y.ToString("R", CultureInfo.InvariantCulture) + ", "
            + goldenP.Z.ToString("R", CultureInfo.InvariantCulture) + ") m, v=("
            + goldenV.X.ToString("R", CultureInfo.InvariantCulture) + ", "
            + goldenV.Y.ToString("R", CultureInfo.InvariantCulture) + ", "
            + goldenV.Z.ToString("R", CultureInfo.InvariantCulture) + ") m/s");

        void PrintDiff(string name, Vector3d pA, Vector3d vA, Vector3d pB, Vector3d vB)
        {
            Console.WriteLine("  " + name + ": dp=("
                + (pA.X - pB.X).ToString("E3", CultureInfo.InvariantCulture) + ", "
                + (pA.Y - pB.Y).ToString("E3", CultureInfo.InvariantCulture) + ", "
                + (pA.Z - pB.Z).ToString("E3", CultureInfo.InvariantCulture) + ") m, dv=("
                + (vA.X - vB.X).ToString("E3", CultureInfo.InvariantCulture) + ", "
                + (vA.Y - vB.Y).ToString("E3", CultureInfo.InvariantCulture) + ", "
                + (vA.Z - vB.Z).ToString("E3", CultureInfo.InvariantCulture) + ") m/s");
        }

        PrintDiff("production-cert initial", certP, certV, certP, certV);
        PrintDiff("golden-cert initial", goldenP, goldenV, certP, certV);
        PrintDiff("golden-case local initial", goldenLocalP, goldenLocalV, c.P0, c.V0);

        string path = StudyShipDdGoldenPath();
        string mtime = "n/a";
        try
        {
            mtime = new FileInfo(path).LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture);
        }
        catch (IOException)
        {
        }

        Console.WriteLine("  golden: " + Path.GetFileName(path) + ", K"
            + golden.OrderA.ToString(CultureInfo.InvariantCulture) + "/K"
            + golden.OrderB.ToString(CultureInfo.InvariantCulture)
            + ", step=" + golden.StepA.ToString("G6", CultureInfo.InvariantCulture)
            + "/" + golden.StepB.ToString("G6", CultureInfo.InvariantCulture)
            + " s, tol=" + golden.TolA.ToString("E1", CultureInfo.InvariantCulture)
            + "/" + golden.TolB.ToString("E1", CultureInfo.InvariantCulture)
            + ", calls=" + golden.Calls.ToString(CultureInfo.InvariantCulture)
            + ", ms=" + golden.Ms.ToString("F0", CultureInfo.InvariantCulture)
            + ", self=" + golden.SelfErrorM.ToString("E3", CultureInfo.InvariantCulture)
            + " m, mtime=" + mtime);
        Console.WriteLine("  golden generator: StudyShipDdGoldenBuild (ShipGolden.cs), on-demand cache; "
            + "P1B_REFRESH_GOLDEN=1 rebuilds");
        Console.WriteLine("  golden model fingerprint: "
            + (string.Equals(golden.Model, StudyShipDdGoldenModel(c), StringComparison.Ordinal)
                ? "match (current bake/force)"
                : "MISMATCH"));
        Console.WriteLine("  current bake: degree 12, max step 900 s, phase 0.02 rad, quant 2 m");

        Console.WriteLine("  scaling K20 (step 600 s, tol 1E-12, final-only dd; separate runs):");
        int[] orbits = { 100, 250, 500, 1000 };
        double[] times = new double[orbits.Length];
        double[] errors = new double[orbits.Length];
        for (int k = 0; k < orbits.Length; k++)
        {
            int count = orbits[k];
            double seconds = count * c.Tau;
            StudyShipMatrixCase dc = c.Clone();
            dc.Seconds = seconds;
            dc.UseTaylorDdReference = false;
            TaylorShipDdResult dd = TaylorShip.RunDd(
                dc.Sys, bp + dc.P0, bv + dc.V0, StudyShipT0, seconds / 86400d,
                20, 1e-12d, 1e-15d, 600d, 1e-6d);
            dc.Sys.EvaluateBodyState(central, StudyShipT0 + seconds,
                out Vector3d bpT, out _);
            Vector3d ddP = dd.P - bpT;
            double stepsPerTau = dc.Central == 1 && dc.Seconds > 30d * 86400d ? 8d : 24d;
            double[] checkpoints = StudyShipMatrixCheckpointTimes(dc);
            var sampleP = new Vector3d[checkpoints.Length];
            var sampleV = new Vector3d[checkpoints.Length];
            StudyShipCertifiedRef cert = StudyShipCertifiedReference(
                dc, stepsPerTau, 5, checkpoints, sampleP, sampleV);
            double error = (ddP - cert.P).Magnitude;
            times[k] = seconds;
            errors[k] = error;
            Console.WriteLine("    " + count.ToString(CultureInfo.InvariantCulture)
                + " orbits: t=" + seconds.ToString("E6", CultureInfo.InvariantCulture)
                + " s, dd-cert=" + error.ToString("E3", CultureInfo.InvariantCulture)
                + " m, cert valid=" + cert.Valid
                + ", dd calls=" + dd.Calls.ToString(CultureInfo.InvariantCulture)
                + ", steps=" + dd.Steps.ToString(CultureInfo.InvariantCulture)
                + ", ms=" + dd.Ms.ToString("F0", CultureInfo.InvariantCulture));
        }

        bool positive = true;
        for (int i = 0; i < errors.Length; i++)
        {
            if (!(errors[i] > 0d) || !double.IsFinite(errors[i]))
            {
                positive = false;
            }
        }

        if (positive)
        {
            double sx = 0d;
            double sy = 0d;
            double sxx = 0d;
            double sxy = 0d;
            for (int i = 0; i < times.Length; i++)
            {
                double x = Math.Log(times[i]);
                double y = Math.Log(errors[i]);
                sx += x;
                sy += y;
                sxx += x * x;
                sxy += x * y;
            }

            double n = times.Length;
            double p = ((n * sxy) - (sx * sy)) / ((n * sxx) - (sx * sx));
            Console.WriteLine("  growth: dd-cert ~ t^p, p="
                + p.ToString("F2", CultureInfo.InvariantCulture)
                + " (1=initial/phase, 2=constant force)");
        }
        else
        {
            Console.WriteLine("  growth: p unavailable (non-positive/non-finite error)");
        }
    }
}
