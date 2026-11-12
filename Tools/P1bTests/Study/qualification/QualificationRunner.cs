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
    private const string QualificationTaskVersion = "2";

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
        string resumeDir = QualificationResumeDir();
        bool resuming = !string.IsNullOrWhiteSpace(resumeDir);
        string dir;
        if (resuming)
        {
            dir = QualificationResolveFolder(resumeDir);
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("resume: folder not found: " + dir);
                return 1;
            }

            string profileFile = Path.Combine(dir, "environment.txt");
            if (File.Exists(profileFile))
            {
                string stored = QualificationEnvironmentField(dir, "profile");
                if (!string.Equals(stored, profile.ToString().ToLowerInvariant(), StringComparison.Ordinal))
                {
                    Console.WriteLine("resume: profile mismatch: folder is '" + stored
                        + "', current is '" + profile.ToString().ToLowerInvariant() + "'");
                    return 1;
                }
            }
        }
        else
        {
            dir = QualificationCreateRunDir(resultsRoot, profile);
            Directory.CreateDirectory(dir);
        }

        string logPath = Path.Combine(dir, "run.log");

        using (var fileWriter = new StreamWriter(logPath, resuming, new UTF8Encoding(false)) { AutoFlush = true })
        {
            var log = new QualificationTeeWriter(Console.Out, fileWriter);
            log.WriteLine("Test564_MethodQualification");
            log.WriteLine("profile: " + profile.ToString().ToLowerInvariant());
            log.WriteLine("run dir: " + dir + (resuming ? " (resume)" : ""));

            List<IStudyIntegrator> methods = StudyRegistry.All();
            log.WriteLine("methods: " + methods.Count.ToString(QualificationInv));
            foreach (IStudyIntegrator method in methods)
            {
                log.WriteLine("  " + method.Name + " | " + method.Family + " | " + method.Kind
                    + " | ExpectedOrder=" + QualificationNumber(method.ExpectedOrder));
            }

            List<QualificationProblem> catalog = QualificationCatalogFor(profile);
            log.WriteLine("catalog: " + catalog.Count.ToString(QualificationInv) + " problem(s)");

            QualificationResumeContext resume = null;
            if (resuming)
            {
                if (!QualificationResumeMatchesEnvironment(dir, methods, log))
                {
                    return 1;
                }

                var goalsPerProblem = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (QualificationProblem q in catalog)
                {
                    goalsPerProblem[q.Id] = QualificationGoalCount(q, profile);
                }

                resume = QualificationLoadResume(dir, methods.Count, goalsPerProblem);
                log.WriteLine("resume: " + resume.ReusedProblems.ToString(QualificationInv)
                    + " finished problem(s), " + resume.ReusedCells.ToString(QualificationInv)
                    + " reused cell(s)");
                QualificationRestoreAuditStatuses(dir, catalog, log);
            }
            else
            {
                QualificationWriteEnvironment(Path.Combine(dir, "environment.txt"), profile, methods);
                QualificationWriteHeaderOnly(Path.Combine(dir, "runs.csv"), QualificationRunsHeader);
                QualificationWriteHeaderOnly(Path.Combine(dir, "matrix.csv"), QualificationMatrixHeader);
                var auditWatch = Stopwatch.StartNew();
                QualificationAuditOracles(catalog, log, dir);
                log.WriteLine("audit: " + auditWatch.ElapsedMilliseconds.ToString(QualificationInv) + " ms");
            }

            int invalid = 0;
            int unaccounted = 0;
            foreach (QualificationProblem problem in catalog)
            {
                if (problem.Status != "VALID")
                {
                    invalid++;
                    if (string.IsNullOrEmpty(problem.AuditNote))
                    {
                        unaccounted++;
                    }
                }
            }

            log.WriteLine("oracle audit: " + (catalog.Count - invalid).ToString(QualificationInv)
                + " VALID, " + invalid.ToString(QualificationInv) + " not VALID"
                + " (each non-VALID must carry a documented reason)");
            if (!resuming)
            {
                log.WriteLine("wrote environment.txt, runs.csv (header only), matrix.csv (header only), oracle_audit.md");
            }

            int remaining = 0;
            foreach (QualificationProblem problem in catalog)
            {
                if (problem.Status == "VALID" && (resume == null || !resume.Completed.Contains(problem.Id)))
                {
                    remaining++;
                }
            }

            if (resuming && remaining == 0)
            {
                log.WriteLine("nothing to resume: all evaluated problems are complete");
                QualificationWriteReports(dir, catalog, log);
                if (QualificationFailOnErrors(dir, catalog, log))
                {
                    return 1;
                }

                QualificationRunCompareIfRequested(dir, log);
                log.WriteLine("elapsed: " + watch.ElapsedMilliseconds.ToString(QualificationInv) + " ms");
                return 0;
            }

            QualificationRunCatalogue(catalog, profile, log, dir, resume);
            QualificationWriteReports(dir, catalog, log);

            if (QualificationFailOnErrors(dir, catalog, log))
            {
                return 1;
            }

            QualificationRunCompareIfRequested(dir, log);

            log.WriteLine("elapsed: " + watch.ElapsedMilliseconds.ToString(QualificationInv) + " ms");

            if (unaccounted > 0)
            {
                Console.WriteLine("FAIL: " + unaccounted.ToString(QualificationInv)
                    + " problem(s) with an unaudited oracle and no documented reason (see oracle_audit.md)");
                return 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// A report-only resume must not hide a broken folder: an ERROR cell still fails the
    /// run, exactly as it does after a fresh sweep.
    /// </summary>
    private static bool QualificationFailOnErrors(
        string dir, List<QualificationProblem> catalog, TextWriter log)
    {
        int errorCells = 0;
        foreach (QualificationMatrixEntry entry in QualificationReadMatrix(dir))
        {
            if (entry.Status == "ERROR")
            {
                errorCells++;
            }
        }

        int errorProblems = 0;
        foreach (QualificationProblem problem in catalog)
        {
            if (problem.Status == "ERROR")
            {
                errorProblems++;
            }
        }

        if (errorCells + errorProblems == 0)
        {
            return false;
        }

        log.WriteLine("ERROR: " + errorCells.ToString(QualificationInv) + " cell(s), "
            + errorProblems.ToString(QualificationInv) + " problem(s)");
        Console.WriteLine("FAIL: " + errorCells.ToString(QualificationInv)
            + " ERROR cell(s), " + errorProblems.ToString(QualificationInv)
            + " ERROR problem(s); see matrix.csv / oracle_audit.md");
        return true;
    }

    /// <summary>--compare works in both the normal path and a report-only resume.</summary>
    private static void QualificationRunCompareIfRequested(string dir, TextWriter log)
    {
        string compareDir = QualificationResolveFolder(QualificationCompareDir());
        if (string.IsNullOrWhiteSpace(compareDir))
        {
            return;
        }

        string other = Path.GetFullPath(compareDir);
        if (Directory.Exists(other))
        {
            QualificationCompareRuns(dir, other, log);
        }
        else
        {
            log.WriteLine("compare: folder not found: " + other);
        }
    }

    /// <summary>
    /// A resumed folder must belong to the same TASK.md revision and the same method list:
    /// mixing a run of an older catalogue with a newer code is what silently corrupts a
    /// baseline. The profile check is done by the caller.
    /// </summary>
    private static bool QualificationResumeMatchesEnvironment(
        string dir, List<IStudyIntegrator> methods, TextWriter log)
    {
        string storedTask = QualificationEnvironmentField(dir, "task_sha256");
        string currentTask = QualificationFileSha256(Path.Combine(QualificationRoot(), "TASK.md"));
        if (storedTask != "unknown" && currentTask != "missing"
            && !string.Equals(storedTask, currentTask, StringComparison.Ordinal))
        {
            Console.WriteLine("resume: TASK.md changed since the run: stored " + storedTask
                + ", current " + currentTask + "; refusing to mix revisions");
            return false;
        }

        var names = new List<string>(methods.Count);
        foreach (IStudyIntegrator method in methods)
        {
            names.Add(method.Name);
        }

        string storedMethods = QualificationEnvironmentField(dir, "methods");
        string currentMethods = string.Join(", ", names);
        if (storedMethods != "unknown"
            && !string.Equals(storedMethods, currentMethods, StringComparison.Ordinal))
        {
            Console.WriteLine("resume: method list changed since the run; refusing to mix revisions");
            Console.WriteLine("  stored:  " + storedMethods);
            Console.WriteLine("  current: " + currentMethods);
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------------------------
    // Resume
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a user folder path. Program.cs pins the working directory to Tools/P1bTests,
    /// so a path typed from the repository root must also be accepted.
    /// </summary>
    private static string QualificationResolveFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string direct = Path.GetFullPath(path);
        if (Directory.Exists(direct))
        {
            return direct;
        }

        string repoRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", ".."));
        string fromRoot = Path.GetFullPath(Path.Combine(repoRoot, path));
        return Directory.Exists(fromRoot) ? fromRoot : direct;
    }

    /// <summary>--resume &lt;folder&gt; (command line) or P1B564_RESUME (environment).</summary>
    private static string QualificationResumeDir()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], "--resume", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return Environment.GetEnvironmentVariable("P1B564_RESUME");
    }

    /// <summary>--compare &lt;folder&gt; (command line) or P1B564_COMPARE (environment fallback).</summary>
    private static string QualificationCompareDir()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], "--compare", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return Environment.GetEnvironmentVariable("P1B564_COMPARE");
    }

    /// <summary>
    /// Restores oracle statuses from the previous run's oracle_audit.md. On resume the audit is
    /// not re-executed (it would rebuild every problem, including the expensive R1/R2
    /// references), so the statuses and notes come from the file.
    /// </summary>
    private static void QualificationRestoreAuditStatuses(
        string dir, List<QualificationProblem> catalog, TextWriter log)
    {
        string path = Path.Combine(dir, "oracle_audit.md");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("resume: oracle_audit.md is missing in " + dir);
        }

        var status = new Dictionary<string, string>(StringComparer.Ordinal);
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] cells = line.Split('|');
            if (cells.Length < 4)
            {
                continue;
            }

            string id = cells[1].Trim();
            string state = cells[3].Trim();
            string note = cells.Length > 7 ? cells[7].Trim() : "";
            if (id.Length > 0 && state.Length > 0 && id != "problem" && !id.StartsWith("---", StringComparison.Ordinal))
            {
                status[id] = state;
                notes[id] = note;
            }
        }

        int restored = 0;
        foreach (QualificationProblem q in catalog)
        {
            string state;
            if (status.TryGetValue(q.Id, out state))
            {
                q.Status = state;
                string note;
                notes.TryGetValue(q.Id, out note);
                if (state == "VALID")
                {
                    q.AuditNote = string.IsNullOrEmpty(note)
                        ? "reused from oracle_audit.md (VALID)"
                        : "reused from oracle_audit.md: " + note;
                }
                else
                {
                    // An INVALID problem must keep its documented reason: a generic "reused"
                    // text would mask a lost reason and trip the unaccounted check.
                    q.AuditNote = note;
                }

                restored++;
            }
        }

        // Floors are restored too: resumed problems are not re-audited, but a problem that is
        // still to be computed needs its floor for the level gate and the G2 window.
        foreach (string line in File.ReadAllLines(path))
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] cells = line.Split('|');
            if (cells.Length < 6)
            {
                continue;
            }

            string id = cells[1].Trim();
            double floor = QualificationParseDouble(cells[5].Trim());
            foreach (QualificationProblem q in catalog)
            {
                if (q.Id == id && double.IsFinite(floor))
                {
                    q.Floor = floor;
                    break;
                }
            }
        }

        log.WriteLine("resume: restored " + restored.ToString(QualificationInv)
            + " oracle status(es) and floors from oracle_audit.md");
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
