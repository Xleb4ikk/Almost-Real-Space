using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Galilego.Core;
using Galilego.Universe;
using P1bStudy.Framework;
using Rkn12BakeStepper = Galilego.Universe.Rkn1210Stepper;

internal static partial class P1bTests
{
    // ────────────────────────────────────────────────────────────────────────
    // Test566 — RKN12(10) certification. R1 scope: coefficients and stepper.
    // R2 (baker wiring) and R3–R5 (bake certification) extend this case.
    //
    // Gates (TASK_RKN12 §8.1):
    //   * coefficient identities (owner decision A: sum(B)=1/2);
    //   * observed fixed-step order 12 (ns={4,8,16}, errors >1e-14 and <0.1,
    //     overall order in (11.0, 13.5));
    //   * Kepler 2-body one period error < 1e-6 m;
    //   * N-body |dP|/P_scale <= 1e-11, |dCOM|/COM_scale <= 1e-11 over 10 years;
    //   * node landing t == nextT bit-for-bit;
    //   * determinism: two runs bitwise equal;
    //   * allocations: <= 1 KB per 2000 steps after warm-up;
    //   * error norm is translation invariant (relative per body);
    //   * one step matches the StudyAdvancedSteppers reference to round-off.
    // ────────────────────────────────────────────────────────────────────────

    private const int Rkn12TestStages = 17;
    private const int Rkn12TestRhsPerAttempt = 16;

    private static int Test566_Rkn12BakeCertification()
    {
        Console.WriteLine("--- Test566: RKN12(10) stepper certification (R1) ---");
        Rkn12ConfigDefaults();
        Rkn12UnknownIntegrator();
        Rkn12Dop853Bitwise();
        Rkn12BakePaths();
        Rkn12CoefficientsIdentities();
        Rkn12ReferenceCoefficients();
        Rkn12CallCount();
        Rkn12ObservedOrder();
        Rkn12KeplerOnePeriod();
        Rkn12InvariantsTenYears();
        Rkn12NodeLanding();
        Rkn12Determinism();
        Rkn12Allocations();
        Rkn12RelativeNormTranslation();
        Rkn12ReferenceStepAgreement();
        Rkn12CertificationSynthetic();
        return 0;
    }

    // ------------------------------------------------------------------------
    // R2: configuration defaults and baker branch
    // ------------------------------------------------------------------------

    private static void Rkn12ConfigDefaults()
    {
        Check((int)BakeIntegrator.Dop853Fixed == 0,
            "R2 config: Dop853Fixed = 0", ((int)BakeIntegrator.Dop853Fixed).ToString(CultureInfo.InvariantCulture));
        Check((int)BakeIntegrator.Rkn1210Adaptive == 1,
            "R2 config: Rkn1210Adaptive = 1", ((int)BakeIntegrator.Rkn1210Adaptive).ToString(CultureInfo.InvariantCulture));
        Check(new BakeConfig().Integrator == BakeIntegrator.Dop853Fixed,
            "R2 config: BakeConfig initialiser is Dop853Fixed", "");
        Check(new StarSystemAuthoring().Integrator == BakeIntegrator.Dop853Fixed,
            "R2 config: StarSystemAuthoring initialiser is Dop853Fixed", "");
        new BakeConfig().Validate();
    }

    private static void Rkn12UnknownIntegrator()
    {
        var config = BaselineConfig();
        config.Integrator = (BakeIntegrator)99;
        bool threw = false;
        try
        {
            EphemerisBaker.Bake(BaselineOutdoorsSystem(), 0.01d, config);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        Check(threw, "R2 config: unknown integrator fails loudly (no fallback)", "");
    }

    private static void Rkn12Dop853Bitwise()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rkn12_r2_dop_" + Guid.NewGuid().ToString("N"));
        try
        {
            StarSystem sys = BaselineOutdoorsSystem();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            EphemerisBaker.BakeToFiles(sys, dir, 1.2d, BaselineConfig(), null, null, null, true);
            watch.Stop();
            string[] files = Directory.GetFiles(dir, "*.bin");
            Array.Sort(files, StringComparer.Ordinal);
            string hash = BaselineHashDirectory(dir, files);
            const string r0Hash = "2AECA30F587EDF18C814C0A0C708C8C1007445BAA9F95CA0C8E9198D23C1DEC3";
            Check(string.Equals(hash, r0Hash, StringComparison.Ordinal),
                "R2 DOP853: bake is bitwise equal to the R0 baseline",
                hash + (string.Equals(hash, r0Hash, StringComparison.Ordinal) ? "" : " != " + r0Hash));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  R2 DOP853 Outdoors 1.2 y: {0:F1} ms", watch.Elapsed.TotalMilliseconds));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    private static void Rkn12BakePaths()
    {
        Rkn12BakePath("Outdoors", BaselineOutdoorsSystem());
        Rkn12BakePath("Example4", ExampleSystemPresets.CreateExampleSystem());
    }

    private static void Rkn12BakePath(string label, StarSystem sys)
    {
        string dir = Path.Combine(Path.GetTempPath(), "rkn12_r2_" + label + "_" + Guid.NewGuid().ToString("N"));
        try
        {
            BakeConfig config = BaselineConfig();
            config.Integrator = BakeIntegrator.Rkn1210Adaptive;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var baked = EphemerisBaker.BakeToFiles(sys, dir, 1.2d, config, null, null, null, true);
            watch.Stop();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  R2 RKN12 {0} 1.2 y: {1:F1} ms", label, watch.Elapsed.TotalMilliseconds));
            int expected = sys.AllBodies.Count - 1;
            Check(baked.Count == expected, "R2 RKN12 bake " + label + ": every body baked",
                baked.Count + "/" + expected);

            bool filesPresent = true;
            foreach (var kv in baked)
            {
                filesPresent &= !string.IsNullOrEmpty(kv.Value.FilePath) && File.Exists(kv.Value.FilePath);
            }

            Check(filesPresent, "R2 RKN12 bake " + label + ": BG3 files written", "");
            string[] files = Directory.GetFiles(dir, "*.bin");
            Array.Sort(files, StringComparer.Ordinal);
            string hash = BaselineHashDirectory(dir, files);
            string dopHash = label == "Outdoors"
                ? "2AECA30F587EDF18C814C0A0C708C8C1007445BAA9F95CA0C8E9198D23C1DEC3"
                : "2E7DDE62E7DE7884424518D7AB34794EE4721D6FFE70E70F9ABA9E0F32217BF0";
            Check(!string.Equals(hash, dopHash, StringComparison.Ordinal),
                "R2 RKN12 bake " + label + ": output differs from DOP853 (no silent fallback)", hash);

            EphemerisBaker.AttachFiles(sys, dir);
            double end = sys.BakedEndSeconds();
            double span = 1.2d * 365d * 86400d;
            Check(double.IsFinite(end) && end >= span * 0.999d,
                "R2 RKN12 bake " + label + ": manifest attaches and covers span",
                end.ToString("R", CultureInfo.InvariantCulture));

            bool finite = true;
            foreach (OrbitingBody body in sys.AllBodies)
            {
                if (body.Parent == null)
                {
                    continue;
                }

                sys.EnsureCachedPositions(end * 0.5d);
                sys.EvaluateBodyState(body, end * 0.5d, out Vector3d p, out Vector3d v);
                finite &= p.IsFinite && v.IsFinite;
            }

            Check(finite, "R2 RKN12 bake " + label + ": reader returns finite states", "");

            foreach (OrbitingBody body in sys.AllBodies)
            {
                if (body.Baked != null)
                {
                    body.Baked.Dispose();
                    body.Baked = null;
                }
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    // ------------------------------------------------------------------------
    // Acceleration callbacks (no per-call allocations)
    // ------------------------------------------------------------------------

    /// <summary>Pairwise Newtonian gravity in mu units: a_i = sum_j mu_j (r_j-r_i)/r^3.</summary>
    private static Rkn1210Acceleration Rkn12Gravity(double[] mu)
    {
        int n = mu.Length;
        return delegate (double t, double[] p, double[] a)
        {
            for (int i = 0; i < 3 * n; i++)
            {
                a[i] = 0d;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dx = p[3 * j] - p[3 * i];
                    double dy = p[(3 * j) + 1] - p[(3 * i) + 1];
                    double dz = p[(3 * j) + 2] - p[(3 * i) + 2];
                    double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                    double s = 1d / (r2 * Math.Sqrt(r2));
                    double fi = s * mu[j];
                    double fj = s * mu[i];
                    a[3 * i] += fi * dx;
                    a[(3 * i) + 1] += fi * dy;
                    a[(3 * i) + 2] += fi * dz;
                    a[3 * j] -= fj * dx;
                    a[(3 * j) + 1] -= fj * dy;
                    a[(3 * j) + 2] -= fj * dz;
                }
            }
        };
    }

    // ------------------------------------------------------------------------
    // 1. Coefficient identities (§6.2, owner decision A)
    // ------------------------------------------------------------------------

    private static void Rkn12CoefficientsIdentities()
    {
        double sumB = 0d;
        double sumBp = 0d;
        double sumBt = 0d;
        double sumBpt = 0d;
        for (int i = 0; i < Rkn12TestStages; i++)
        {
            sumB += Rkn1210Coefficients.B[i];
            sumBp += Rkn1210Coefficients.BP[i];
            sumBt += Rkn1210Coefficients.BT[i];
            sumBpt += Rkn1210Coefficients.BPT[i];
        }

        // Owner decision 2026-11-15 (option A): the position weights sum to 1/2.
        Check(Math.Abs(sumB - 0.5d) <= 1e-15d, "R1 coefficients: sum(B)=1/2",
            sumB.ToString("R", CultureInfo.InvariantCulture));
        Check(Math.Abs(sumBp - 1d) <= 1e-15d, "R1 coefficients: sum(BP)=1",
            sumBp.ToString("R", CultureInfo.InvariantCulture));
        Check(Math.Abs(sumBt) <= 1e-15d, "R1 coefficients: sum(BT)=0",
            sumBt.ToString("E3", CultureInfo.InvariantCulture));
        Check(Math.Abs(sumBpt) <= 1e-15d, "R1 coefficients: sum(BPT)=0",
            sumBpt.ToString("E3", CultureInfo.InvariantCulture));
        Check(Rkn1210Coefficients.C[0] == 0d, "R1 coefficients: C[0]=0", "");
        Check(Rkn1210Coefficients.C[16] == 1d, "R1 coefficients: C[16]=1", "");

        double worstA = 0d;
        int worstStage = -1;
        for (int i = 1; i < Rkn12TestStages; i++)
        {
            double sum = 0d;
            double[] row = Rkn1210Coefficients.A[i];
            for (int j = 0; j < row.Length; j++)
            {
                sum += row[j];
            }

            double target = 0.5d * Rkn1210Coefficients.C[i] * Rkn1210Coefficients.C[i];
            double diff = Math.Abs(sum - target);
            if (diff > worstA)
            {
                worstA = diff;
                worstStage = i;
            }
        }

        Check(worstA <= 1e-13d, "R1 coefficients: sum_j A[i][j] = C[i]^2/2",
            "worst |diff|=" + worstA.ToString("E3", CultureInfo.InvariantCulture) +
            " at stage " + worstStage.ToString(CultureInfo.InvariantCulture));

        bool rowsValid = Rkn1210Coefficients.A.Length == Rkn12TestStages;
        for (int i = 0; i < Rkn1210Coefficients.A.Length; i++)
        {
            rowsValid &= Rkn1210Coefficients.A[i].Length == i;
        }

        Check(rowsValid, "R1 coefficients: A is a 17-row triangular table", "");
    }

    // ------------------------------------------------------------------------
    // 2. Coefficients are byte-for-byte the StudyAdvancedSteppers reference
    // ------------------------------------------------------------------------

    private static void Rkn12ReferenceCoefficients()
    {
        Type reference = typeof(Rkn12BakeStepper).Assembly.GetType("P1bStudy.Framework.Rkn1210Stepper");
        Check(reference != null, "R1 reference: StudyAdvancedSteppers.Rkn1210Stepper found", "");
        if (reference == null)
        {
            return;
        }

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        Check(Rkn12CompareArray(reference, "C", Rkn1210Coefficients.C), "R1 reference: C identical", "");
        Check(Rkn12CompareArray(reference, "B", Rkn1210Coefficients.B), "R1 reference: B identical", "");
        Check(Rkn12CompareArray(reference, "BP", Rkn1210Coefficients.BP), "R1 reference: BP identical", "");
        Check(Rkn12CompareArray(reference, "BT", Rkn1210Coefficients.BT), "R1 reference: BT identical", "");
        Check(Rkn12CompareArray(reference, "BPT", Rkn1210Coefficients.BPT), "R1 reference: BPT identical", "");

        FieldInfo aField = reference.GetField("A", flags);
        double[][] refA = aField != null ? aField.GetValue(null) as double[][] : null;
        bool same = refA != null && refA.Length == Rkn1210Coefficients.A.Length;
        int rows = Math.Min(refA != null ? refA.Length : 0, Rkn1210Coefficients.A.Length);
        for (int i = 0; i < rows; i++)
        {
            same &= refA[i].Length == Rkn1210Coefficients.A[i].Length;
            int cols = Math.Min(refA[i].Length, Rkn1210Coefficients.A[i].Length);
            for (int j = 0; j < cols; j++)
            {
                same &= BitConverter.DoubleToInt64Bits(refA[i][j])
                    == BitConverter.DoubleToInt64Bits(Rkn1210Coefficients.A[i][j]);
            }
        }

        Check(same, "R1 reference: A identical", "");
    }

    private static bool Rkn12CompareArray(Type type, string name, double[] mine)
    {
        FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        double[] reference = field != null ? field.GetValue(null) as double[] : null;
        if (reference == null || reference.Length != mine.Length)
        {
            return false;
        }

        for (int i = 0; i < mine.Length; i++)
        {
            if (BitConverter.DoubleToInt64Bits(reference[i]) != BitConverter.DoubleToInt64Bits(mine[i]))
            {
                return false;
            }
        }

        return true;
    }

    // ------------------------------------------------------------------------
    // 3. Stage accounting: Begin=1, TryStep=16, Accept=1
    // ------------------------------------------------------------------------

    private static void Rkn12CallCount()
    {
        int calls = 0;
        Rkn1210Acceleration accel = delegate (double t, double[] p, double[] a)
        {
            calls++;
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = 0d;
            }
        };

        var stepper = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-12, 1e-3, 1e-6, accel);
        double[] p = { 0d, 0d, 0d, 1e6, 0d, 0d };
        double[] v = { 0d, 0d, 0d, 0d, 100d, 0d };
        double[] pn = new double[6];
        double[] vn = new double[6];

        stepper.Begin(0d, p, v);
        Check(calls == 1, "R1 stages: Begin evaluates k[0] once", "calls=" + calls);
        stepper.TryStep(0d, 10d, p, v, pn, vn);
        Check(calls == 1 + Rkn12TestRhsPerAttempt,
            "R1 stages: TryStep evaluates 16 RHS (not 17)",
            "calls=" + calls);
        stepper.Accept(10d, pn, vn);
        Check(calls == 1 + Rkn12TestRhsPerAttempt + 1,
            "R1 stages: Accept refreshes k[0]",
            "calls=" + calls);
        Check(Rkn12BakeStepper.StageCount == 17 && Rkn12BakeStepper.RhsPerAttempt == 16,
            "R1 stages: published stage counts", "17 / 16");
    }

    // ------------------------------------------------------------------------
    // 4. Observed order (template: StudySelfTest.Rkn1210FixedOrder)
    // ------------------------------------------------------------------------

    private static void Rkn12ObservedOrder()
    {
        const double e = 0.2d;
        const double period = 2d * Math.PI;
        int[] ns = { 4, 8, 16 };
        var errors = new double[ns.Length];
        for (int k = 0; k < ns.Length; k++)
        {
            double h = period / ns[k];
            double[] p = { 0d, 0d, 0d, 1d - e, 0d, 0d };
            double[] v = { 0d, 0d, 0d, 0d, Math.Sqrt((1d + e) / (1d - e)), 0d };
            double[] pn = new double[6];
            double[] vn = new double[6];
            var stepper = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-13, 0d, 0d,
                Rkn12Gravity(new[] { 1d, 0d }));
            stepper.Begin(0d, p, v);
            double t = 0d;
            for (int s = 0; s < ns[k]; s++)
            {
                stepper.TryStep(t, h, p, v, pn, vn);
                stepper.Accept(t + h, pn, vn);
                Array.Copy(pn, p, 6);
                Array.Copy(vn, v, 6);
                t += h;
            }

            Rkn12KeplerAnalytic(e, t, out double px, out double py, out _, out _);
            double dx = p[3] - px;
            double dy = p[4] - py;
            errors[k] = Math.Sqrt((dx * dx) + (dy * dy));
        }

        bool decreasing = errors[0] > errors[1] && errors[1] > errors[2];
        bool aboveFloor = errors[0] > 1e-14d && errors[1] > 1e-14d && errors[2] > 1e-14d;
        bool belowGate = errors[0] < 0.1d && errors[1] < 0.1d && errors[2] < 0.1d;
        double overall = Math.Log(errors[0] / errors[2])
            / Math.Log((double)ns[2] / ns[0]);
        bool orderOk = overall > 11.0d && overall < 13.5d;

        Check(decreasing && aboveFloor && belowGate,
            "R1 order: errors decrease, >1e-14 and <0.1",
            errors[0].ToString("E3", CultureInfo.InvariantCulture) + ", " +
            errors[1].ToString("E3", CultureInfo.InvariantCulture) + ", " +
            errors[2].ToString("E3", CultureInfo.InvariantCulture));
        Check(orderOk, "R1 order: overall in (11.0, 13.5)",
            overall.ToString("F2", CultureInfo.InvariantCulture) + " (expected 12)");
    }

    /// <summary>Analytic Kepler state for a=1, mu=1, eccentricity e; mean anomaly = t.</summary>
    private static void Rkn12KeplerAnalytic(
        double e, double t, out double px, out double py, out double vx, out double vy)
    {
        double mean = t;
        double eccentric = mean;
        for (int i = 0; i < 80; i++)
        {
            double d = (eccentric - (e * Math.Sin(eccentric)) - mean)
                / (1d - (e * Math.Cos(eccentric)));
            eccentric -= d;
            if (Math.Abs(d) < 1e-16d)
            {
                break;
            }
        }

        double r = 1d - (e * Math.Cos(eccentric));
        double sq = Math.Sqrt(1d - (e * e));
        px = Math.Cos(eccentric) - e;
        py = sq * Math.Sin(eccentric);
        vx = -Math.Sin(eccentric) / r;
        vy = sq * Math.Cos(eccentric) / r;
    }

    // ------------------------------------------------------------------------
    // 5. Kepler 2-body, one period, < 1e-6 m
    // ------------------------------------------------------------------------

    private static void Rkn12KeplerOnePeriod()
    {
        const double a = 1e7d;
        const double muCentral = 3.986004418e14d;
        double n = Math.Sqrt(muCentral / (a * a * a));
        double period = 2d * Math.PI / n;

        double[] p = { 0d, 0d, 0d, a, 0d, 0d };
        double[] v = { 0d, 0d, 0d, 0d, a * n, 0d };
        double[] pn = new double[6];
        double[] vn = new double[6];
        var stepper = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-13, 1e-9, 1e-12,
            Rkn12Gravity(new[] { muCentral, 0d }));
        stepper.Begin(0d, p, v);

        const int steps = 128;
        double h = period / steps;
        double t = 0d;
        for (int s = 0; s < steps; s++)
        {
            stepper.TryStep(t, h, p, v, pn, vn);
            stepper.Accept(t + h, pn, vn);
            Array.Copy(pn, p, 6);
            Array.Copy(vn, v, 6);
            t += h;
        }

        double dx = p[3] - a;
        double dy = p[4];
        double dz = p[5];
        double error = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        Check(error < 1e-6d, "R1 Kepler: one period < 1e-6 m",
            error.ToString("E3", CultureInfo.InvariantCulture) + " m, period " +
            period.ToString("F1", CultureInfo.InvariantCulture) + " s");
    }

    // ------------------------------------------------------------------------
    // 6. N-body momentum/COM over 10 years
    // ------------------------------------------------------------------------

    private static StarSystem Rkn12ThreeBodySystem()
    {
        var bp = new SystemBlueprint();
        bp.Bodies.Add(StudyBody("Sun", -1, 1.32712440018e20d, 6.96e8d, 0d, 0d, 0d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Earth", 0, 3.986004418e14d, 6.371e6d, 1.495978707e11d, 0d, 0d, 0d, 0d, 0d));
        bp.Bodies.Add(StudyBody("Moon", 1, 4.9028e12d, 1.737e6d, 3.844e8d, 0d, 0d, 0d, 0d, 0d));
        return bp.Build();
    }

    private static void Rkn12InvariantsTenYears()
    {
        StarSystem sys = Rkn12ThreeBodySystem();
        int n = sys.AllBodies.Count;
        var mu = new double[n];
        var p = new double[3 * n];
        var v = new double[3 * n];
        for (int i = 0; i < n; i++)
        {
            mu[i] = sys.AllBodies[i].ResolveStandardGravitationalParameter();
            sys.EvaluateBodyState(sys.AllBodies[i], 0d, out Vector3d bp, out Vector3d bv);
            p[3 * i] = bp.X;
            p[(3 * i) + 1] = bp.Y;
            p[(3 * i) + 2] = bp.Z;
            v[3 * i] = bv.X;
            v[(3 * i) + 1] = bv.Y;
            v[(3 * i) + 2] = bv.Z;
        }

        // Zero the total momentum and the COM so the invariant drift is numerical,
        // not the physical drift of a non-barycentric initial state.
        double totalMu = 0d;
        double cx = 0d;
        double cy = 0d;
        double cz = 0d;
        double cvx = 0d;
        double cvy = 0d;
        double cvz = 0d;
        for (int i = 0; i < n; i++)
        {
            totalMu += mu[i];
            cx += mu[i] * p[3 * i];
            cy += mu[i] * p[(3 * i) + 1];
            cz += mu[i] * p[(3 * i) + 2];
            cvx += mu[i] * v[3 * i];
            cvy += mu[i] * v[(3 * i) + 1];
            cvz += mu[i] * v[(3 * i) + 2];
        }

        cx /= totalMu;
        cy /= totalMu;
        cz /= totalMu;
        cvx /= totalMu;
        cvy /= totalMu;
        cvz /= totalMu;
        for (int i = 0; i < n; i++)
        {
            p[3 * i] -= cx;
            p[(3 * i) + 1] -= cy;
            p[(3 * i) + 2] -= cz;
            v[3 * i] -= cvx;
            v[(3 * i) + 1] -= cvy;
            v[(3 * i) + 2] -= cvz;
        }

        double pScale = 0d;
        double comScale = 0d;
        for (int i = 0; i < n; i++)
        {
            pScale += mu[i] * Magnitude(v, 3 * i);
            comScale += mu[i] * Magnitude(p, 3 * i);
        }

        comScale /= totalMu;

        int[] parent = new int[n];
        for (int i = 0; i < n; i++)
        {
            parent[i] = sys.AllBodies[i].Parent == null
                ? -1
                : Rkn12IndexOf(sys.AllBodies, sys.AllBodies[i].Parent);
        }

        var stepper = new Rkn12BakeStepper(n, parent, 1e-12, 1e-3, 1e-6, Rkn12Gravity(mu));
        var pn = new double[3 * n];
        var vn = new double[3 * n];
        stepper.Begin(0d, p, v);
        double e0 = Rkn12Energy(mu, p, v);

        const double step = 3600d;
        const double years = 10d;
        long steps = (long)Math.Round(years * 365d * 86400d / step);
        double t = 0d;
        for (long s = 0; s < steps; s++)
        {
            stepper.TryStep(t, step, p, v, pn, vn);
            stepper.Accept(t + step, pn, vn);
            Array.Copy(pn, p, p.Length);
            Array.Copy(vn, v, v.Length);
            t += step;
        }

        double px = 0d;
        double py = 0d;
        double pz = 0d;
        double mx = 0d;
        double my = 0d;
        double mz = 0d;
        for (int i = 0; i < n; i++)
        {
            px += mu[i] * v[3 * i];
            py += mu[i] * v[(3 * i) + 1];
            pz += mu[i] * v[(3 * i) + 2];
            mx += mu[i] * p[3 * i];
            my += mu[i] * p[(3 * i) + 1];
            mz += mu[i] * p[(3 * i) + 2];
        }

        mx /= totalMu;
        my /= totalMu;
        mz /= totalMu;
        double dp = Math.Sqrt((px * px) + (py * py) + (pz * pz)) / pScale;
        double dcom = Math.Sqrt((mx * mx) + (my * my) + (mz * mz)) / comScale;

        Check(dp <= 1e-11d, "R1 N-body: |dP|/P_scale <= 1e-11",
            dp.ToString("E3", CultureInfo.InvariantCulture) + " over " + years + " y");
        Check(dcom <= 1e-11d, "R1 N-body: |dCOM|/COM_scale <= 1e-11",
            dcom.ToString("E3", CultureInfo.InvariantCulture) + " over " + years + " y");

        // Energy is a report-only quantity at R1 (§8.1).
        double energy = Rkn12Energy(mu, p, v);
        double energyDrift = Math.Abs((energy - e0) / e0);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  R1 N-body 10 y: steps={0}, |dP|/P={1:E3}, |dCOM|/COM={2:E3}, E0={3:E6}, E={4:E6}, |dE/E|={5:E3}",
            steps, dp, dcom, e0, energy, energyDrift));
        Check(double.IsFinite(energyDrift), "R1 N-body: energy drift finite",
            energyDrift.ToString("E3", CultureInfo.InvariantCulture));
    }

    private static double Rkn12Energy(double[] mu, double[] p, double[] v)
    {
        int n = mu.Length;
        double kinetic = 0d;
        for (int i = 0; i < n; i++)
        {
            double vx = v[3 * i];
            double vy = v[(3 * i) + 1];
            double vz = v[(3 * i) + 2];
            kinetic += 0.5d * mu[i] * ((vx * vx) + (vy * vy) + (vz * vz));
        }

        double potential = 0d;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = p[3 * i] - p[3 * j];
                double dy = p[(3 * i) + 1] - p[(3 * j) + 1];
                double dz = p[(3 * i) + 2] - p[(3 * j) + 2];
                potential -= mu[i] * mu[j] / Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }
        }

        return kinetic + potential;
    }

    private static int Rkn12IndexOf(System.Collections.Generic.IReadOnlyList<OrbitingBody> bodies, OrbitingBody body)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            if (ReferenceEquals(bodies[i], body))
            {
                return i;
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------------
    // 7. Node landing: t == nextT bit-for-bit
    // ------------------------------------------------------------------------

    private static void Rkn12NodeLanding()
    {
        const double a = 1e7d;
        const double muCentral = 3.986004418e14d;
        double n = Math.Sqrt(muCentral / (a * a * a));

        double[] p = { 0d, 0d, 0d, a, 0d, 0d };
        double[] v = { 0d, 0d, 0d, 0d, a * n, 0d };
        double[] pn = new double[6];
        double[] vn = new double[6];
        var stepper = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-9, 1e-3, 1e-6,
            Rkn12Gravity(new[] { muCentral, 0d }));
        stepper.Begin(0d, p, v);

        const double segment = 200000d;
        const int degree = 6;
        const int segments = 2;
        int bitwise = 0;
        int checkedNodes = 0;
        int rejected = 0;
        double t = 0d;
        double h = 5000d;
        const double maxH = 20000d;
        const double minH = 1e-3d;

        for (int seg = 0; seg < segments; seg++)
        {
            for (int j = 0; j <= degree; j++)
            {
                if (seg == 0 && j == 0)
                {
                    continue; // t=0 is the accepted start, not a landing
                }

                double nextT = (seg * segment)
                    + (segment * (1d - Math.Cos(j * Math.PI / degree)) * 0.5d);
                while (t < nextT)
                {
                    double remaining = nextT - t;
                    double hTry = Math.Min(h, maxH);
                    bool clipped = false;
                    if (hTry * 1.01d >= remaining)
                    {
                        hTry = remaining;
                        clipped = true;
                    }

                    double err = stepper.TryStep(t, hTry, p, v, pn, vn);
                    if (err <= 1d)
                    {
                        double tNew = clipped ? nextT : t + hTry;
                        stepper.Accept(tNew, pn, vn);
                        Array.Copy(pn, p, 6);
                        Array.Copy(vn, v, 6);
                        t = tNew;
                        double factor = Math.Min(5d, Math.Max(0.2d,
                            0.9d * Math.Pow(Math.Max(err, 1e-10d), -1d / 11d)));
                        double hNew = Math.Min(hTry * factor, maxH);
                        if (clipped)
                        {
                            hNew = Math.Max(hNew, Math.Min(h, maxH));
                        }

                        h = hNew;
                    }
                    else
                    {
                        rejected++;
                        double factor = Math.Min(0.9d, Math.Max(0.1d,
                            0.9d * Math.Pow(err, -1d / 11d)));
                        h = hTry * factor;
                        if (h < minH)
                        {
                            throw new InvalidOperationException("R1 node landing: step floor reached.");
                        }
                    }
                }

                // Node forcing (§6.4): the accepted clipped branch above must have
                // assigned nextT itself. No post-loop snap, otherwise this check is
                // tautological.
                checkedNodes++;
                if (BitConverter.DoubleToInt64Bits(t) == BitConverter.DoubleToInt64Bits(nextT))
                {
                    bitwise++;
                }
            }
        }

        Check(bitwise == checkedNodes && checkedNodes > 0,
            "R1 node landing: t == nextT bitwise on every node",
            bitwise + "/" + checkedNodes + " nodes, rejected=" + rejected);
        Check(rejected > 0, "R1 node landing: rejection path exercised",
            "rejected=" + rejected);
    }

    // ------------------------------------------------------------------------
    // 8. Determinism: two runs bitwise equal
    // ------------------------------------------------------------------------

    private static void Rkn12Determinism()
    {
        double[] p1;
        double[] v1;
        Rkn12FixedRun(out p1, out v1);
        double[] p2;
        double[] v2;
        Rkn12FixedRun(out p2, out v2);

        bool equal = p1.Length == p2.Length && v1.Length == v2.Length;
        for (int i = 0; equal && i < p1.Length; i++)
        {
            equal &= BitConverter.DoubleToInt64Bits(p1[i]) == BitConverter.DoubleToInt64Bits(p2[i]);
            equal &= BitConverter.DoubleToInt64Bits(v1[i]) == BitConverter.DoubleToInt64Bits(v2[i]);
        }

        Check(equal, "R1 determinism: two runs bitwise equal", "");
    }

    private static void Rkn12FixedRun(out double[] p, out double[] v)
    {
        const double a = 1e7d;
        const double muCentral = 3.986004418e14d;
        double n = Math.Sqrt(muCentral / (a * a * a));
        double period = 2d * Math.PI / n;
        p = new double[] { 0d, 0d, 0d, a, 0d, 0d };
        v = new double[] { 0d, 0d, 0d, 0d, a * n, 0d };
        double[] pn = new double[6];
        double[] vn = new double[6];
        var stepper = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-13, 1e-9, 1e-12,
            Rkn12Gravity(new[] { muCentral, 0d }));
        stepper.Begin(0d, p, v);
        const int steps = 128;
        double h = period / steps;
        double t = 0d;
        for (int s = 0; s < steps; s++)
        {
            stepper.TryStep(t, h, p, v, pn, vn);
            stepper.Accept(t + h, pn, vn);
            Array.Copy(pn, p, 6);
            Array.Copy(vn, v, 6);
            t += h;
        }
    }

    // ------------------------------------------------------------------------
    // 9. Allocations after warm-up
    // ------------------------------------------------------------------------

    private static void Rkn12Allocations()
    {
        const double a = 1e7d;
        const double muCentral = 3.986004418e14d;
        double n = Math.Sqrt(muCentral / (a * a * a));
        double[] p = { 0d, 0d, 0d, a, 0d, 0d };
        double[] v = { 0d, 0d, 0d, 0d, a * n, 0d };
        double[] pn = new double[6];
        double[] vn = new double[6];
        var stepper = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-13, 1e-9, 1e-12,
            Rkn12Gravity(new[] { muCentral, 0d }));
        stepper.Begin(0d, p, v);
        double h = 0.5d;
        double t = 0d;
        for (int s = 0; s < 1000; s++)
        {
            stepper.TryStep(t, h, p, v, pn, vn);
            stepper.Accept(t + h, pn, vn);
            Array.Copy(pn, p, 6);
            Array.Copy(vn, v, 6);
            t += h;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        const int measured = 2000;
        for (int s = 0; s < measured; s++)
        {
            stepper.TryStep(t, h, p, v, pn, vn);
            stepper.Accept(t + h, pn, vn);
            Array.Copy(pn, p, 6);
            Array.Copy(vn, v, 6);
            t += h;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated <= 1024L, "R1 allocations: <= 1 KB per 2000 steps",
            allocated.ToString(CultureInfo.InvariantCulture) + " bytes");
    }

    // ------------------------------------------------------------------------
    // 10. Error norm is relative per body (translation invariant)
    // ------------------------------------------------------------------------

    private static void Rkn12RelativeNormTranslation()
    {
        const double a = 1e7d;
        const double muCentral = 3.986004418e14d;
        double n = Math.Sqrt(muCentral / (a * a * a));
        double[] p = { 0d, 0d, 0d, a, 0d, 0d };
        double[] v = { 0d, 0d, 0d, 0d, a * n, 0d };
        double[] pOffset = (double[])p.Clone();
        double[] vOffset = (double[])v.Clone();
        const double ox = 1e2d;
        const double oy = -2e2d;
        const double oz = 3e2d;
        const double ovx = 1d;
        const double ovy = -2d;
        const double ovz = 3d;
        for (int i = 0; i < 2; i++)
        {
            pOffset[3 * i] += ox;
            pOffset[(3 * i) + 1] += oy;
            pOffset[(3 * i) + 2] += oz;
            vOffset[3 * i] += ovx;
            vOffset[(3 * i) + 1] += ovy;
            vOffset[(3 * i) + 2] += ovz;
        }

        double[] pn = new double[6];
        double[] vn = new double[6];
        var stepper = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-12, 1e-3, 1e-6,
            Rkn12Gravity(new[] { muCentral, 0d }));
        stepper.Begin(0d, p, v);
        double plain = stepper.TryStep(0d, 1000d, p, v, pn, vn);
        stepper.Begin(0d, pOffset, vOffset);
        double shifted = stepper.TryStep(0d, 1000d, pOffset, vOffset, pn, vn);
        double relative = Math.Abs(plain - shifted) / Math.Max(Math.Abs(plain), 1e-300d);
        // The norm formula is exactly translation invariant; the *computed* value
        // moves at the cancellation floor of the embedded error estimate. An
        // absolute-coordinate norm would change by |offset|/|r_rel| ~ 1e-5 here,
        // i.e. far above this 1e-4 gate, so the test still separates the two.
        Check(relative <= 1e-4d, "R1 error norm: translation invariant",
            "plain=" + plain.ToString("E6", CultureInfo.InvariantCulture) +
            " shifted=" + shifted.ToString("E6", CultureInfo.InvariantCulture) +
            " rel=" + relative.ToString("E3", CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------------------------
    // 11. One step agrees with the StudyAdvancedSteppers reference
    // ------------------------------------------------------------------------

    private static void Rkn12ReferenceStepAgreement()
    {
        // Normalized Kepler e=0.2, mu=1: reference problem with a 6-component state.
        const double e = 0.2d;
        var problem = new StudyProblem
        {
            Dim = 6,
            PosDim = 3,
            T0 = 0d,
            Y0 = new[] { 1d - e, 0d, 0d, 0d, Math.Sqrt((1d + e) / (1d - e)), 0d }
        };
        problem.Rhs = delegate (double t, double[] y, double[] dy)
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

        var config = new StudyConfig { Rtol = 1e-12d, Atol = 0d };
        var context = new StudyContext(problem, long.MaxValue / 4L);
        var reference = new P1bStudy.Framework.Rkn1210Stepper(problem, config, context);
        reference.Begin(0d, problem.Y0);
        double[] referenceNew = new double[6];
        reference.TryStep(0d, 0.1d, problem.Y0, referenceNew);

        double[] p = { 0d, 0d, 0d, problem.Y0[0], problem.Y0[1], problem.Y0[2] };
        double[] v = { 0d, 0d, 0d, problem.Y0[3], problem.Y0[4], problem.Y0[5] };
        double[] pn = new double[6];
        double[] vn = new double[6];
        var stepper = new Rkn12BakeStepper(2, new[] { -1, 0 }, 1e-12, 0d, 0d,
            Rkn12Gravity(new[] { 1d, 0d }));
        stepper.Begin(0d, p, v);
        stepper.TryStep(0d, 0.1d, p, v, pn, vn);

        double worst = 0d;
        for (int i = 0; i < 3; i++)
        {
            worst = Math.Max(worst, Math.Abs(pn[3 + i] - referenceNew[i]));
            worst = Math.Max(worst, Math.Abs(vn[3 + i] - referenceNew[3 + i]));
        }

        Check(worst <= 1e-12d, "R1 reference: one step matches StudyAdvancedSteppers",
            "max |diff|=" + worst.ToString("E3", CultureInfo.InvariantCulture));
    }

    private static double Magnitude(double[] values, int offset)
    {
        double x = values[offset];
        double y = values[offset + 1];
        double z = values[offset + 2];
        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }
}
