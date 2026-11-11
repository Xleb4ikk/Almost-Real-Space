using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using P1bStudy.Framework;

// Test564_MethodQualification: engine-independent qualification of every method in
// StudyRegistry.All() against the catalogue of TASK.md (K/T/R/L tiers).
//
// M1 (this file): registration, smoke/full profile, run directory, environment.txt,
// header-only runs.csv / matrix.csv, run.log. No measurement yet.
//
// Everything lives in this folder. The class is a partial of P1bTests (like the rest of
// Study/) so later stages can reuse the private builders (StudyBridgeShipProblem,
// StudyShipMatrixBuildCases, MoonFlybyProblem, ...) without editing existing files.
internal static partial class P1bTests
{
    private const string QualificationTaskVersion = "1";

    private static readonly CultureInfo QualificationInv = CultureInfo.InvariantCulture;

    private const string QualificationRunsHeader =
        "problem_id,method,family,kind,control,status,reason,err_max,err_end,order_obs,order_exp,"
        + "dE_rel,dL_rel,dCOM_rel,rev_err,steps,rejected,rhs_calls,price,wall_ms_median,alloc_bytes,"
        + "deterministic,oracle_id,oracle_floor";

    private const string QualificationMatrixHeader =
        "problem_id,method,tier,status,reason,best_control,price_at_tier";

    /// <summary>smoke = minutes; full = hours (TASK.md 13).</summary>
    private enum QualificationProfile
    {
        Smoke,
        Full
    }

    private static int Test564_MethodQualification()
    {
        var watch = Stopwatch.StartNew();
        QualificationProfile profile = QualificationResolveProfile();
        string resultsRoot = QualificationResultsRoot();
        string dir = QualificationCreateRunDir(resultsRoot, profile);
        Directory.CreateDirectory(dir);
        string logPath = Path.Combine(dir, "run.log");

        using (var fileWriter = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true })
        {
            var log = new QualificationTeeWriter(Console.Out, fileWriter);
            log.WriteLine("Test564_MethodQualification");
            log.WriteLine("profile: " + profile.ToString().ToLowerInvariant());
            log.WriteLine("run dir: " + dir);

            List<IStudyIntegrator> methods = StudyRegistry.All();
            log.WriteLine("methods: " + methods.Count.ToString(QualificationInv));
            foreach (IStudyIntegrator method in methods)
            {
                log.WriteLine("  " + method.Name + " | " + method.Family + " | " + method.Kind
                    + " | ExpectedOrder=" + QualificationNumber(method.ExpectedOrder));
            }

            QualificationWriteEnvironment(Path.Combine(dir, "environment.txt"), profile, methods);
            QualificationWriteHeaderOnly(Path.Combine(dir, "runs.csv"), QualificationRunsHeader);
            QualificationWriteHeaderOnly(Path.Combine(dir, "matrix.csv"), QualificationMatrixHeader);

            List<QualificationProblem> catalog = QualificationCatalogFor(profile);
            log.WriteLine("catalog: " + catalog.Count.ToString(QualificationInv) + " problem(s)");
            QualificationAuditOracles(catalog, log, dir);

            int invalid = 0;
            foreach (QualificationProblem problem in catalog)
            {
                if (problem.Status != "VALID")
                {
                    invalid++;
                }
            }

            log.WriteLine("oracle audit: " + (catalog.Count - invalid).ToString(QualificationInv)
                + " VALID, " + invalid.ToString(QualificationInv) + " not VALID");
            log.WriteLine("wrote environment.txt, runs.csv (header only), matrix.csv (header only), oracle_audit.md");

            QualificationRunCatalogue(catalog, profile, log, dir);

            log.WriteLine("elapsed: " + watch.ElapsedMilliseconds.ToString(QualificationInv) + " ms");

            if (invalid > 0)
            {
                Console.WriteLine("FAIL: " + invalid.ToString(QualificationInv)
                    + " problem(s) with an unaudited/invalid oracle (see oracle_audit.md)");
                return 1;
            }
        }

        return 0;
    }

    // ---------------------------------------------------------------------------------
    // Profile
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// P1B564_PROFILE=smoke|full. Program.cs filters whole cases by --fast/--slow but does not
    /// pass that flag to a case, so the per-test profile needs its own switch. Default: smoke.
    /// </summary>
    private static QualificationProfile QualificationResolveProfile()
    {
        string raw = Environment.GetEnvironmentVariable("P1B564_PROFILE");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return QualificationProfile.Smoke;
        }

        string value = raw.Trim();
        if (value.Equals("smoke", StringComparison.OrdinalIgnoreCase))
        {
            return QualificationProfile.Smoke;
        }

        if (value.Equals("full", StringComparison.OrdinalIgnoreCase))
        {
            return QualificationProfile.Full;
        }

        throw new ArgumentException("P1B564_PROFILE must be 'smoke' or 'full', got '" + value + "'");
    }

    // ---------------------------------------------------------------------------------
    // Results directory
    // ---------------------------------------------------------------------------------

    private static string QualificationRoot()
    {
        // CWD is pinned to Tools/P1bTests by Program.cs; a published harness keeps its own
        // Study/ next to the assembly.
        string fromCwd = Path.Combine(Environment.CurrentDirectory, "Study", "qualification");
        if (Directory.Exists(fromCwd))
        {
            return fromCwd;
        }

        string fromAssembly = Path.Combine(AppContext.BaseDirectory, "Study", "qualification");
        if (Directory.Exists(fromAssembly))
        {
            return fromAssembly;
        }

        return fromCwd;
    }

    private static string QualificationResultsRoot()
    {
        return Path.Combine(QualificationRoot(), "results");
    }

    private static string QualificationCreateRunDir(string resultsRoot, QualificationProfile profile)
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", QualificationInv);
        string hash = QualificationGitHashShort();
        string baseName = stamp + "_" + hash;
        string dir = Path.Combine(resultsRoot, baseName);
        int suffix = 2;
        while (Directory.Exists(dir))
        {
            dir = Path.Combine(resultsRoot, baseName + "_" + suffix.ToString(QualificationInv));
            suffix++;
        }

        return dir;
    }

    private static string QualificationGitHashShort()
    {
        string head = TryReadGitHead();
        if (string.IsNullOrEmpty(head) || head.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "nogit";
        }

        var sb = new StringBuilder(12);
        foreach (char c in head)
        {
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
            {
                sb.Append(char.ToLowerInvariant(c));
            }

            if (sb.Length >= 7)
            {
                break;
            }
        }

        return sb.Length == 0 ? "nogit" : sb.ToString();
    }

    // ---------------------------------------------------------------------------------
    // environment.txt
    // ---------------------------------------------------------------------------------

    private static void QualificationWriteEnvironment(
        string path, QualificationProfile profile, List<IStudyIntegrator> methods)
    {
        var sb = new StringBuilder();
        sb.AppendLine("test: Test564_MethodQualification");
        sb.AppendLine("task_version: " + QualificationTaskVersion);
        sb.AppendLine("task_sha256: " + QualificationFileSha256(Path.Combine(QualificationRoot(), "TASK.md")));
        sb.AppendLine("profile: " + profile.ToString().ToLowerInvariant());
        sb.AppendLine("date_utc: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", QualificationInv));
        sb.AppendLine("date_local: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", QualificationInv));
        sb.AppendLine("commit: " + TryReadGitHead());
        sb.AppendLine("branch: " + QualificationGitBranch());
        sb.AppendLine("dirty: " + TryReadGitDirty());
        sb.AppendLine("dotnet: " + Environment.Version.ToString() + " (" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ")");
        sb.AppendLine("os: " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        sb.AppendLine("arch: " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
        sb.AppendLine("cpu: " + (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown"));
        sb.AppendLine("logical_cpus: " + Environment.ProcessorCount.ToString(QualificationInv));
        sb.AppendLine("methods_count: " + methods.Count.ToString(QualificationInv));
        sb.Append("methods: ");
        for (int i = 0; i < methods.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(methods[i].Name);
        }

        sb.AppendLine();
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string QualificationGitBranch()
    {
        string branch = TryRunGit("rev-parse --abbrev-ref HEAD").Trim();
        return branch.Length == 0 ? "unknown" : branch;
    }

    private static string QualificationFileSha256(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "missing";
            }

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(File.ReadAllBytes(path));
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }
        catch (Exception ex)
        {
            return "error: " + ex.GetType().Name;
        }
    }

    // ---------------------------------------------------------------------------------
    // CSV scaffolding
    // ---------------------------------------------------------------------------------

    private static void QualificationWriteHeaderOnly(string path, string header)
    {
        File.WriteAllText(path, header + Environment.NewLine, new UTF8Encoding(false));
    }

    private static string QualificationNumber(double value)
    {
        return double.IsNaN(value)
            ? "NaN"
            : value.ToString("G6", QualificationInv);
    }

    // ---------------------------------------------------------------------------------
    // Log
    // ---------------------------------------------------------------------------------

    /// <summary>Writes every line to the console and to run.log.</summary>
    private sealed class QualificationTeeWriter : TextWriter
    {
        private readonly TextWriter first;
        private readonly TextWriter second;

        public QualificationTeeWriter(TextWriter first, TextWriter second)
        {
            this.first = first;
            this.second = second;
        }

        public override Encoding Encoding
        {
            get { return Encoding.UTF8; }
        }

        public override void Write(char value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Write(string value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void WriteLine(string value)
        {
            first.WriteLine(value);
            second.WriteLine(value);
        }

        public override void Flush()
        {
            first.Flush();
            second.Flush();
        }
    }
}
