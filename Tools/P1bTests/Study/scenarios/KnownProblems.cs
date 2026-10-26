using System;

internal static partial class P1bTests
{
    // ------------------------------------------------------------------
    // Known-answer problems for Taylor:
    //   1) Lagrange equilateral triangle with unequal masses (exact rotation)
    //   2) Chenciner-Montgomery figure-eight (known period, many periods)
    //   3) Arenstorf orbit in the Earth-Moon CR3BP (known period)
    // Units: G=1, distances and masses are dimensionless.
    // ------------------------------------------------------------------

    private const double KnownYearSeconds = 365.25d * 86400d;

    private static TaylorNBodyInput KnownInput(double[] mu, double[] p, double[] v)
    {
        var names = new string[mu.Length];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = "B" + (i + 1);
        }

        return new TaylorNBodyInput { Names = names, Mu = mu, P0 = p, V0 = v };
    }

    private static double MaxDiff(double[] a, double[] b)
    {
        double worst = 0d;
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        }

        return worst;
    }

    private static double MaxAbs(double[] a)
    {
        double worst = 0d;
        for (int i = 0; i < a.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(a[i]));
        }

        return worst;
    }

    private static void RunKnownTaylor(
        string label, TaylorNBodyInput input, double totalTime, double[] exactP, double[] exactV,
        bool gate, double doubleLimit, double ddLimit)
    {
        double years = totalTime / KnownYearSeconds;

        long a0 = GC.GetAllocatedBytesForCurrentThread();
        TaylorNBodyResult td = TaylorNBody.Run(input, years, 12, 1e-12d, 1e-12d, 0.05d, 1e-10d, 0d);
        double dKiB = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024d;

        long b0 = GC.GetAllocatedBytesForCurrentThread();
        DdNBodyResult dd = RunTaylorNBodyDdOpt(input, years, 12, 1e-12d, 1e-12d, 0.05d, 1e-10d);
        double ddKiB = (GC.GetAllocatedBytesForCurrentThread() - b0) / 1024d;

        double dPos = MaxDiff(td.P, exactP);
        double dVel = MaxDiff(td.V, exactV);
        double ddPos = MaxDiff(dd.P, exactP);
        double ddVel = MaxDiff(dd.V, exactV);

        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0,-34} double: dp={1:E2} dv={2:E2} ({3:F1} СЃ, {4:N0} KiB) | dd: dp={5:E2} dv={6:E2} ({7:F1} СЃ, {8:N0} KiB)",
            label, dPos, dVel, td.Ms / 1000d, dKiB,
            ddPos, ddVel, dd.Ms / 1000d, ddKiB));

        Check(double.IsFinite(dPos) && double.IsFinite(ddPos), label + " finite", "dp/dd-dp finite");
        if (gate)
        {
            Check(dPos < doubleLimit, label + " double dp", dPos.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
            Check(ddPos < ddLimit, label + " dd dp", ddPos.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void KnownLagrangeTriangle(double m1, double m2, double m3, int periods, bool gate, double doubleLimit, double ddLimit)
    {
        double a = 1d;
        double totalMass = m1 + m2 + m3;
        double omega = Math.Sqrt(totalMass / (a * a * a));
        var mu = new[] { m1, m2, m3 };

        double root3 = Math.Sqrt(3d);
        var p = new double[]
        {
            0d, 0d, 0d,
            a, 0d, 0d,
            a * 0.5d, a * root3 * 0.5d, 0d
        };

        double cx = 0d;
        double cy = 0d;
        for (int i = 0; i < 3; i++)
        {
            cx += mu[i] * p[3 * i];
            cy += mu[i] * p[(3 * i) + 1];
        }

        cx /= totalMass;
        cy /= totalMass;

        var v = new double[9];
        for (int i = 0; i < 3; i++)
        {
            p[3 * i] -= cx;
            p[(3 * i) + 1] -= cy;
            v[3 * i] = -omega * p[(3 * i) + 1];
            v[(3 * i) + 1] = omega * p[3 * i];
        }

        double totalTime = periods * 2d * Math.PI / omega;
        double theta = omega * totalTime;
        double c = Math.Cos(theta);
        double s = Math.Sin(theta);
        var exactP = new double[9];
        var exactV = new double[9];
        for (int i = 0; i < 3; i++)
        {
            double x = p[3 * i];
            double y = p[(3 * i) + 1];
            double vx = v[3 * i];
            double vy = v[(3 * i) + 1];
            exactP[3 * i] = (x * c) - (y * s);
            exactP[(3 * i) + 1] = (x * s) + (y * c);
            exactV[3 * i] = (vx * c) - (vy * s);
            exactV[(3 * i) + 1] = (vx * s) + (vy * c);
        }

        string label = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Lagrange {0}:{1}:{2}, {3} periods",
            m1, m2, m3, periods);
        RunKnownTaylor(label, KnownInput(mu, (double[])p.Clone(), (double[])v.Clone()), totalTime, exactP, exactV, gate, doubleLimit, ddLimit);
    }

    private static void KnownFigureEight(int periods)
    {
        var mu = new[] { 1d, 1d, 1d };
        var p = new double[]
        {
            0.97000436d, -0.24308753d, 0d,
            -0.97000436d, 0.24308753d, 0d,
            0d, 0d, 0d
        };
        var v = new double[]
        {
            0.466203685d, 0.43236573d, 0d,
            0.466203685d, 0.43236573d, 0d,
            -0.93240737d, -0.86473146d, 0d
        };

        const double period = 6.32591398d;
        double totalTime = periods * period;
        string label = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Figure-eight, {0} periods", periods);
        RunKnownTaylor(label, KnownInput(mu, (double[])p.Clone(), (double[])v.Clone()), totalTime, p, v, true, 1e-5d, 1e-5d);
    }

    private static (double x, double y, double vx, double vy, long steps) RunCr3bpTaylor(
        double mu, double x0, double y0, double vx0, double vy0, double totalTime,
        int order, double tolerance, double maxStep, double minStep)
    {
        double x = x0;
        double y = y0;
        double vx = vx0;
        double vy = vy0;
        double t = 0d;
        double h = Math.Min(maxStep, totalTime);
        long steps = 0L;

        while (t < totalTime - 1e-12d)
        {
            h = Math.Min(h, totalTime - t);
            TaylorJet xj = TaylorJet.Constant(order, x);
            TaylorJet yj = TaylorJet.Constant(order, y);
            TaylorJet vxj = TaylorJet.Constant(order, vx);
            TaylorJet vyj = TaylorJet.Constant(order, vy);

            for (int s = 0; s < order; s++)
            {
                Cr3bpAccel(xj, yj, vxj, vyj, mu, order, out TaylorJet ax, out TaylorJet ay);
                xj.C[s + 1] = vxj.C[s] / (s + 1d);
                yj.C[s + 1] = vyj.C[s] / (s + 1d);
                vxj.C[s + 1] = ax.C[s] / (s + 1d);
                vyj.C[s + 1] = ay.C[s] / (s + 1d);
            }

            double hp = Math.Pow(h, order);
            double errP = Math.Max(Math.Abs(xj.C[order]), Math.Abs(yj.C[order])) * hp;
            double errV = Math.Max(Math.Abs(vxj.C[order]), Math.Abs(vyj.C[order])) * hp;
            double norm = Math.Max(errP, errV) / tolerance;

            if (norm <= 1d)
            {
                x = xj.Eval(h);
                y = yj.Eval(h);
                vx = vxj.Eval(h);
                vy = vyj.Eval(h);
                t += h;
                steps++;
                double factor = Math.Min(4d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-14d), 1d / order)));
                h = Math.Min(maxStep, Math.Max(minStep, h * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-14d), 1d / order)));
                h = Math.Max(minStep, h * factor);
            }
        }

        return (x, y, vx, vy, steps);
    }

    private static void Cr3bpAccel(
        TaylorJet x, TaylorJet y, TaylorJet vx, TaylorJet vy, double mu, int order,
        out TaylorJet ax, out TaylorJet ay)
    {
        TaylorJet one = TaylorJet.Constant(order, 1d);
        TaylorJet muJ = TaylorJet.Constant(order, mu);
        TaylorJet oneMinusMu = TaylorJet.Constant(order, 1d - mu);

        TaylorJet dx1 = x + muJ;
        TaylorJet dy1 = y;
        TaylorJet r1sq = (dx1 * dx1) + (dy1 * dy1);
        TaylorJet r1 = TaylorJet.Sqrt(r1sq);
        TaylorJet inv1 = one / (r1sq * r1);

        TaylorJet dx2 = x - oneMinusMu;
        TaylorJet dy2 = y;
        TaylorJet r2sq = (dx2 * dx2) + (dy2 * dy2);
        TaylorJet r2 = TaylorJet.Sqrt(r2sq);
        TaylorJet inv2 = one / (r2sq * r2);

        ax = (vy * 2d) + x - (dx1 * (inv1 * (1d - mu))) - (dx2 * (inv2 * mu));
        ay = -(vx * 2d) + y - (dy1 * (inv1 * (1d - mu))) - (dy2 * (inv2 * mu));
    }

    private static void KnownArenstorf(int periods, int order, bool gate)
    {
        const double mu = 0.012277471d;
        const double x0 = 0.994d;
        const double y0 = 0d;
        const double vx0 = 0d;
        const double vy0 = -2.0015851063790825224d;
        const double period = 17.065216560157962d;

        double totalTime = periods * period;
        var r = RunCr3bpTaylor(
            mu, x0, y0, vx0, vy0, totalTime, order, 1e-13d, 0.05d, 1e-10d);
        double dp = Math.Sqrt(
            ((r.x - x0) * (r.x - x0))
            + ((r.y - y0) * (r.y - y0)));
        double dv = Math.Sqrt(
            ((r.vx - vx0) * (r.vx - vx0))
            + ((r.vy - vy0) * (r.vy - vy0)));

        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Arenstorf, {0,3} periods, K={1}: dp={2:E2}, dv={3:E2}, steps={4}",
            periods, order, dp, dv, r.steps));
        Check(double.IsFinite(dp) && double.IsFinite(dv), "Arenstorf finite", "dp/dv finite");
        if (gate)
        {
            Check(dp < 1e-10d, "Arenstorf periodic", dp.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
            Check(r.steps < 5000L, "Arenstorf steps", r.steps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static int Test505_KnownProblems()
    {
        Console.WriteLine("--- Known problems: Taylor vs exact/known periodic orbits ---");
        Console.WriteLine("Lagrange 1:2:3 and 1:2:5 are linearly unstable; 1 period tests the method, 10 periods show physical growth. 1:1:100 is stable.");
        KnownLagrangeTriangle(1d, 2d, 3d, 1, true, 1e-5d, 1e-7d);
        KnownLagrangeTriangle(1d, 2d, 3d, 10, false, 0d, 0d);
        KnownLagrangeTriangle(1d, 2d, 5d, 1, true, 1e-5d, 1e-7d);
        KnownLagrangeTriangle(1d, 1d, 100d, 10, true, 1e-4d, 1e-6d);
        KnownLagrangeTriangle(1d, 1d, 100d, 100, true, 1e-4d, 1e-6d);
        KnownFigureEight(1);
        KnownFigureEight(10);
        KnownFigureEight(100);
        Console.WriteLine("Figure-eight IC/period are 8-digit; the ~3e-8 per-period return error is the known-data floor (double == dd).");
        KnownArenstorf(1, 12, true);
        KnownArenstorf(1, 16, true);
        KnownArenstorf(2, 12, false);
        KnownArenstorf(5, 12, false);
        KnownArenstorf(5, 16, false);
        KnownArenstorf(10, 12, false);
        KnownArenstorfDd(1, 12, 1e-14d);
        KnownArenstorfDd(1, 16, 1e-16d);
        KnownArenstorfDd(1, 20, 1e-18d);
        KnownArenstorfDd(1, 24, 1e-20d);
        KnownArenstorfDd(1, 28, 1e-22d);
        KnownArenstorfDd(1, 32, 1e-24d);
        Console.WriteLine("Arenstorf is unstable/sensitive: multi-period rows are informational; the one-period row is the method gate.");
        Console.WriteLine("--- Known problems complete ---");
        return 0;
    }








    // ------------------------------------------------------------------
    // Convergence sweeps: separate method order/tolerance from IC/period
    // and from the double-precision floor.
    // ------------------------------------------------------------------

    private static void LagrangeState(
        double m1, double m2, double m3, out double[] mu, out double[] p, out double[] v, out double omega)
    {
        double a = 1d;
        double totalMass = m1 + m2 + m3;
        omega = Math.Sqrt(totalMass / (a * a * a));
        mu = new[] { m1, m2, m3 };
        double root3 = Math.Sqrt(3d);
        p = new double[]
        {
            0d, 0d, 0d,
            a, 0d, 0d,
            a * 0.5d, a * root3 * 0.5d, 0d
        };

        double cx = 0d;
        double cy = 0d;
        for (int i = 0; i < 3; i++)
        {
            cx += mu[i] * p[3 * i];
            cy += mu[i] * p[(3 * i) + 1];
        }

        cx /= totalMass;
        cy /= totalMass;
        v = new double[9];
        for (int i = 0; i < 3; i++)
        {
            p[3 * i] -= cx;
            p[(3 * i) + 1] -= cy;
            v[3 * i] = -omega * p[(3 * i) + 1];
            v[(3 * i) + 1] = omega * p[3 * i];
        }
    }

    private static void KnownLagrangeSweep()
    {
        Console.WriteLine("Lagrange 1:1:100, 100 periods: return error vs K/tolerance");
        Console.WriteLine("K    tol        dp          dv          steps");
        LagrangeState(1d, 1d, 100d, out double[] mu, out double[] p, out double[] v, out double omega);
        double totalTime = 100d * 2d * Math.PI / omega;
        double years = totalTime / KnownYearSeconds;
        var input = KnownInput(mu, (double[])p.Clone(), (double[])v.Clone());
        var orders = new[] { 8, 12, 16 };
        var tols = new[] { 1e-8d, 1e-10d, 1e-12d, 1e-14d };
        foreach (int order in orders)
        {
            foreach (double tol in tols)
            {
                TaylorNBodyResult r = TaylorNBody.Run(input, years, order, tol, tol, 0.05d, 1e-10d, 0d);
                Console.WriteLine(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0,-4} {1,8:E0} {2,11:E2} {3,11:E2} {4,8}",
                    order, tol, MaxDiff(r.P, p), MaxDiff(r.V, v), r.Steps));
            }
        }

        TaylorNBodyResult h16 = TaylorNBody.Run(input, years, 16, 1e-14d, 1e-14d, 0.01d, 1e-10d, 0d);
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "K16, maxStep=0.01: dp={0:E2}, steps={1}",
            MaxDiff(h16.P, p), h16.Steps));
    }

    private static void KnownFigureEightSweep()
    {
        Console.WriteLine("Figure-eight, 100 periods: return error vs K/tolerance");
        Console.WriteLine("K    tol        dp          dv");
        var mu = new[] { 1d, 1d, 1d };
        var p = new double[]
        {
            0.97000436d, -0.24308753d, 0d,
            -0.97000436d, 0.24308753d, 0d,
            0d, 0d, 0d
        };
        var v = new double[]
        {
            0.466203685d, 0.43236573d, 0d,
            0.466203685d, 0.43236573d, 0d,
            -0.93240737d, -0.86473146d, 0d
        };
        const double period = 6.32591398d;
        double years = 100d * period / KnownYearSeconds;
        var input = KnownInput(mu, (double[])p.Clone(), (double[])v.Clone());
        var orders = new[] { 8, 12, 16 };
        var tols = new[] { 1e-8d, 1e-10d, 1e-12d, 1e-14d };
        foreach (int order in orders)
        {
            foreach (double tol in tols)
            {
                TaylorNBodyResult r = TaylorNBody.Run(input, years, order, tol, tol, 0.02d, 1e-10d, 0d);
                Console.WriteLine(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0,-4} {1,8:E0} {2,11:E2} {3,11:E2}",
                    order, tol, MaxDiff(r.P, p), MaxDiff(r.V, v)));
            }
        }
    }

    private static void KnownArenstorfSweep()
    {
        Console.WriteLine("Arenstorf, 1 period: dp vs K/tolerance");
        Console.WriteLine("K    tol        dp          steps");
        const double mu = 0.012277471d;
        const double x0 = 0.994d;
        const double y0 = 0d;
        const double vx0 = 0d;
        const double vy0 = -2.0015851063790825224d;
        const double period = 17.065216560157962d;
        var orders = new[] { 8, 12, 16, 20 };
        var tols = new[] { 1e-4d, 1e-6d, 1e-8d, 1e-10d, 1e-12d, 1e-14d };
        foreach (int order in orders)
        {
            foreach (double tol in tols)
            {
                var r = RunCr3bpTaylor(mu, x0, y0, vx0, vy0, period, order, tol, 0.05d, 1e-10d);
                double dp = Math.Sqrt(
                    ((r.x - x0) * (r.x - x0)) + ((r.y - y0) * (r.y - y0)));
                Console.WriteLine(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0,-4} {1,8:E0} {2,11:E2} {3,10}",
                    order, tol, dp, r.steps));
            }
        }
    }

    private static int Test506_KnownConvergence()
    {
        Console.WriteLine("--- Known problems: convergence sweeps ---");
        KnownLagrangeSweep();
        KnownFigureEightSweep();
        KnownArenstorfSweep();
        Console.WriteLine("--- Known convergence complete ---");
        return 0;
    }

    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // Arenstorf in double-double: IC, mu, period and step are parsed/kept
    // in dd. The residual is computed in dd and only then converted.
    // ------------------------------------------------------------------

    private static StudyDd DdPow10(int n)
    {
        StudyDd value = StudyDd.From(1d);
        StudyDd ten = StudyDd.From(10d);
        for (int i = 0; i < n; i++)
        {
            value = StudyDd.Mul(value, ten);
        }

        return value;
    }

    private static StudyDd DdFromDecimal(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return StudyDd.Zero;
        }

        int i = 0;
        bool negative = false;
        if (text[0] == '+')
        {
            i++;
        }
        else if (text[0] == '-')
        {
            negative = true;
            i++;
        }

        int ePos = text.IndexOfAny(new[] { 'e', 'E' });
        string mantissa = ePos >= 0 ? text.Substring(i, ePos - i) : text.Substring(i);
        int exponent = 0;
        if (ePos >= 0)
        {
            exponent = int.Parse(text.Substring(ePos + 1), System.Globalization.CultureInfo.InvariantCulture);
        }

        int dot = mantissa.IndexOf('.');
        string digits = dot >= 0 ? mantissa.Remove(dot, 1) : mantissa;
        int fractionDigits = dot >= 0 ? mantissa.Length - dot - 1 : 0;

        StudyDd value = StudyDd.Zero;
        for (int pos = 0; pos < digits.Length;)
        {
            int len = Math.Min(15, digits.Length - pos);
            string chunk = digits.Substring(pos, len);
            double chunkValue = double.Parse(
                chunk, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture);
            value = StudyDd.Add(
                StudyDd.Mul(value, DdPow10(len)),
                StudyDd.From(chunkValue));
            pos += len;
        }

        value = StudyDd.Div(value, DdPow10(fractionDigits));
        if (exponent > 0)
        {
            value = StudyDd.Mul(value, DdPow10(exponent));
        }
        else if (exponent < 0)
        {
            value = StudyDd.Div(value, DdPow10(-exponent));
        }

        return negative ? StudyDd.Sub(StudyDd.Zero, value) : value;
    }

    private static bool DdLess(StudyDd a, StudyDd b)
    {
        return a.Hi < b.Hi || (a.Hi == b.Hi && a.Lo < b.Lo);
    }

    private static StudyDd DdMin(StudyDd a, StudyDd b)
    {
        return DdLess(a, b) ? a : b;
    }

    private static void Cr3bpAccelDd(
        DdJet x, DdJet y, DdJet vx, DdJet vy,
        StudyDd mu, StudyDd oneMinusMu, int order,
        out DdJet ax, out DdJet ay)
    {
        DdJet one = DdJet.Constant(order, StudyDd.From(1d));
        DdJet two = DdJet.Constant(order, StudyDd.From(2d));
        DdJet minusTwo = DdJet.Constant(order, StudyDd.From(-2d));
        DdJet muJ = DdJet.Constant(order, mu);
        DdJet oneMinusJ = DdJet.Constant(order, oneMinusMu);

        DdJet dx1 = x + muJ;
        DdJet dy1 = y;
        DdJet r1sq = (dx1 * dx1) + (dy1 * dy1);
        DdJet r1 = DdJet.Sqrt(r1sq);
        DdJet inv1 = one / (r1sq * r1);

        DdJet dx2 = x - oneMinusJ;
        DdJet dy2 = y;
        DdJet r2sq = (dx2 * dx2) + (dy2 * dy2);
        DdJet r2 = DdJet.Sqrt(r2sq);
        DdJet inv2 = one / (r2sq * r2);

        ax = (vy * two) + x - (dx1 * (inv1 * oneMinusMu)) - (dx2 * (inv2 * mu));
        ay = (vx * minusTwo) + y - (dy1 * (inv1 * oneMinusMu)) - (dy2 * (inv2 * mu));
    }

    private static (StudyDd x, StudyDd y, StudyDd vx, StudyDd vy, long steps) RunCr3bpTaylorDd(
        StudyDd mu, StudyDd x0, StudyDd y0, StudyDd vx0, StudyDd vy0,
        StudyDd totalTime, int order, double tolerance, double maxStep, double minStep)
    {
        StudyDd oneMinusMu = StudyDd.Sub(StudyDd.From(1d), mu);
        StudyDd x = x0;
        StudyDd y = y0;
        StudyDd vx = vx0;
        StudyDd vy = vy0;
        StudyDd t = StudyDd.Zero;
        StudyDd maxH = StudyDd.From(maxStep);
        StudyDd minH = StudyDd.From(minStep);
        StudyDd h = DdMin(maxH, totalTime);
        long steps = 0L;

        while (DdLess(t, totalTime))
        {
            StudyDd remaining = StudyDd.Sub(totalTime, t);
            if (DdLess(remaining, h))
            {
                h = remaining;
            }

            DdJet xj = DdJet.Constant(order, x);
            DdJet yj = DdJet.Constant(order, y);
            DdJet vxj = DdJet.Constant(order, vx);
            DdJet vyj = DdJet.Constant(order, vy);

            for (int s = 0; s < order; s++)
            {
                Cr3bpAccelDd(xj, yj, vxj, vyj, mu, oneMinusMu, order, out DdJet ax, out DdJet ay);
                StudyDd inv = StudyDd.Div(StudyDd.From(1d), StudyDd.From(s + 1d));
                xj.C[s + 1] = StudyDd.Mul(vxj.C[s], inv);
                yj.C[s + 1] = StudyDd.Mul(vyj.C[s], inv);
                vxj.C[s + 1] = StudyDd.Mul(ax.C[s], inv);
                vyj.C[s + 1] = StudyDd.Mul(ay.C[s], inv);
            }

            double hp = Math.Pow(h.ToDouble, order);
            double errP = Math.Max(Math.Abs(xj.C[order].ToDouble), Math.Abs(yj.C[order].ToDouble)) * hp;
            double errV = Math.Max(Math.Abs(vxj.C[order].ToDouble), Math.Abs(vyj.C[order].ToDouble)) * hp;
            double norm = Math.Max(errP, errV) / tolerance;

            if (norm <= 1d)
            {
                x = xj.EvalDd(h);
                y = yj.EvalDd(h);
                vx = vxj.EvalDd(h);
                vy = vyj.EvalDd(h);
                t = StudyDd.Add(t, h);
                steps++;
                double factor = Math.Min(4d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-30d), 1d / order)));
                h = StudyDd.Mul(h, StudyDd.From(factor));
                h = DdMin(h, maxH);
                if (DdLess(h, minH))
                {
                    h = minH;
                }
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(norm, 1e-30d), 1d / order)));
                h = StudyDd.Mul(h, StudyDd.From(factor));
                if (DdLess(h, minH))
                {
                    h = minH;
                }
            }
        }

        return (x, y, vx, vy, steps);
    }

    private static void KnownArenstorfDd(int periods, int order, double tolerance)
    {
        StudyDd mu = DdFromDecimal("0.012277471");
        StudyDd x0 = DdFromDecimal("0.994");
        StudyDd y0 = StudyDd.Zero;
        StudyDd vx0 = StudyDd.Zero;
        StudyDd vy0 = DdFromDecimal("-2.00158510637908252240537862224");
        StudyDd period = DdFromDecimal("17.0652165601579625588917206249");
        StudyDd totalTime = StudyDd.Mul(period, StudyDd.From(periods));

        var r = RunCr3bpTaylorDd(
            mu, x0, y0, vx0, vy0, totalTime, order, tolerance, 0.05d, 1e-12d);
        StudyDd dx = StudyDd.Sub(r.x, x0);
        StudyDd dy = StudyDd.Sub(r.y, y0);
        StudyDd dvx = StudyDd.Sub(r.vx, vx0);
        StudyDd dvy = StudyDd.Sub(r.vy, vy0);
        double dp = StudyDd.Sqrt(StudyDd.Add(StudyDd.Mul(dx, dx), StudyDd.Mul(dy, dy))).ToDouble;
        double dv = StudyDd.Sqrt(StudyDd.Add(StudyDd.Mul(dvx, dvx), StudyDd.Mul(dvy, dvy))).ToDouble;

        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Arenstorf dd(dd IC/T), {0,2} periods, K={1}, tol={2:E0}: dp={3:E2}, dv={4:E2}, steps={5}",
            periods, order, tolerance, dp, dv, r.steps));
        Check(double.IsFinite(dp) && double.IsFinite(dv), "Arenstorf dd finite", "dp/dv finite");
        Check(dp < 1e-12d, "Arenstorf dd below double", dp.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
    }
}


