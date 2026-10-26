using System;
using System.Diagnostics;
using Galilego.Universe;

internal static partial class P1bTests
{
    // Taylor jet whose coefficients are double-double (StudyDd, ~106-bit significand).
    // Same recurrences as Reference/TaylorJet.cs, but every scalar operation is dd.
    private sealed class DdJet
    {
        public readonly int Order;
        public readonly StudyDd[] C;

        public DdJet(int order)
        {
            Order = order;
            C = new StudyDd[order + 1];
        }

        public static DdJet Constant(int order, StudyDd value)
        {
            var j = new DdJet(order);
            j.C[0] = value;
            return j;
        }

        public static DdJet FromSeries(int order, double[] values)
        {
            var j = new DdJet(order);
            int n = Math.Min(order, values.Length);
            for (int i = 0; i < n; i++)
            {
                j.C[i] = StudyDd.From(values[i]);
            }

            return j;
        }

        public static DdJet Add(DdJet a, DdJet b)
        {
            var r = new DdJet(a.Order);
            for (int i = 0; i <= a.Order; i++)
            {
                r.C[i] = StudyDd.Add(a.C[i], b.C[i]);
            }

            return r;
        }

        public static DdJet Sub(DdJet a, DdJet b)
        {
            var r = new DdJet(a.Order);
            for (int i = 0; i <= a.Order; i++)
            {
                r.C[i] = StudyDd.Sub(a.C[i], b.C[i]);
            }

            return r;
        }

        public static DdJet Mul(DdJet a, DdJet b)
        {
            int order = a.Order;
            var r = new DdJet(order);
            for (int n = 0; n <= order; n++)
            {
                StudyDd sum = StudyDd.Zero;
                for (int k = 0; k <= n; k++)
                {
                    sum = StudyDd.Add(sum, StudyDd.Mul(a.C[k], b.C[n - k]));
                }

                r.C[n] = sum;
            }

            return r;
        }

        public static DdJet Div(DdJet a, DdJet b)
        {
            int order = a.Order;
            var r = new DdJet(order);
            r.C[0] = StudyDd.Div(a.C[0], b.C[0]);
            for (int n = 1; n <= order; n++)
            {
                StudyDd sum = a.C[n];
                for (int k = 1; k <= n; k++)
                {
                    sum = StudyDd.Sub(sum, StudyDd.Mul(b.C[k], r.C[n - k]));
                }

                r.C[n] = StudyDd.Div(sum, b.C[0]);
            }

            return r;
        }

        public static DdJet Sqrt(DdJet a)
        {
            int order = a.Order;
            var r = new DdJet(order);
            r.C[0] = StudyDd.Sqrt(a.C[0]);
            StudyDd denom = StudyDd.Add(r.C[0], r.C[0]);
            for (int n = 1; n <= order; n++)
            {
                StudyDd sum = a.C[n];
                for (int k = 1; k < n; k++)
                {
                    sum = StudyDd.Sub(sum, StudyDd.Mul(r.C[k], r.C[n - k]));
                }

                r.C[n] = StudyDd.Div(sum, denom);
            }

            return r;
        }

        public static DdJet Scale(DdJet a, StudyDd s)
        {
            var r = new DdJet(a.Order);
            for (int i = 0; i <= a.Order; i++)
            {
                r.C[i] = StudyDd.Mul(a.C[i], s);
            }

            return r;
        }

        public StudyDd Eval(double tau)
        {
            StudyDd x = StudyDd.From(tau);
            StudyDd value = StudyDd.Zero;
            for (int n = Order; n >= 0; n--)
            {
                value = StudyDd.Add(StudyDd.Mul(value, x), C[n]);
            }

            return value;
        }

        public static DdJet operator +(DdJet a, DdJet b) => Add(a, b);
        public static DdJet operator -(DdJet a, DdJet b) => Sub(a, b);
        public static DdJet operator *(DdJet a, DdJet b) => Mul(a, b);
        public static DdJet operator *(DdJet a, StudyDd s) => Scale(a, s);
        public static DdJet operator *(StudyDd s, DdJet a) => Scale(a, s);
        public static DdJet operator /(DdJet a, DdJet b) => Div(a, b);
    }

    private sealed class DdNBodyResult
    {
        public double[] P;
        public double[] V;
        public long Calls;
        public long Steps;
        public double Ms;
    }

    private static void DdAccel(DdJet[] p, DdJet[] a, double[] mu, int n, int order)
    {
        int dim = 3 * n;
        for (int i = 0; i < dim; i++)
        {
            a[i] = DdJet.Constant(order, StudyDd.Zero);
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                DdJet dx = p[3 * j] - p[3 * i];
                DdJet dy = p[(3 * j) + 1] - p[(3 * i) + 1];
                DdJet dz = p[(3 * j) + 2] - p[(3 * i) + 2];
                DdJet r2 = (dx * dx) + (dy * dy) + (dz * dz);
                DdJet r = DdJet.Sqrt(r2);
                DdJet inv = DdJet.Constant(order, StudyDd.From(1d)) / (r2 * r);
                DdJet fi = inv * StudyDd.From(mu[j]);
                DdJet fj = inv * StudyDd.From(mu[i]);
                a[3 * i] = a[3 * i] + (dx * fi);
                a[(3 * i) + 1] = a[(3 * i) + 1] + (dy * fi);
                a[(3 * i) + 2] = a[(3 * i) + 2] + (dz * fi);
                a[3 * j] = a[3 * j] - (dx * fj);
                a[(3 * j) + 1] = a[(3 * j) + 1] - (dy * fj);
                a[(3 * j) + 2] = a[(3 * j) + 2] - (dz * fj);
            }
        }
    }

    private static DdNBodyResult RunTaylorNBodyDd(
        TaylorNBodyInput input, double years, int order,
        double positionTolerance, double velocityTolerance, double maxStep, double minStep)
    {
        int n = input.Names.Length;
        int dim = 3 * n;
        var p = new StudyDd[dim];
        var v = new StudyDd[dim];
        for (int i = 0; i < dim; i++)
        {
            p[i] = StudyDd.From(input.P0[i]);
            v[i] = StudyDd.From(input.V0[i]);
        }

        double T = years * 365.25d * 86400d;
        double t = 0d;
        double h = Math.Min(maxStep, T);
        var pJ = new DdJet[dim];
        var vJ = new DdJet[dim];
        var aJ = new DdJet[dim];
        var invs = new StudyDd[order];
        for (int s = 0; s < order; s++)
        {
            invs[s] = StudyDd.Div(StudyDd.From(1d), StudyDd.From(s + 1d));
        }

        long calls = 0L;
        long steps = 0L;
        var sw = Stopwatch.StartNew();

        while (t < T - 1e-9d)
        {
            h = Math.Min(h, T - t);
            for (int i = 0; i < dim; i++)
            {
                pJ[i] = DdJet.Constant(order, p[i]);
                vJ[i] = DdJet.Constant(order, v[i]);
            }

            for (int s = 0; s < order; s++)
            {
                DdAccel(pJ, aJ, input.Mu, n, order);
                calls++;
                StudyDd inv = invs[s];
                for (int i = 0; i < dim; i++)
                {
                    pJ[i].C[s + 1] = StudyDd.Mul(vJ[i].C[s], inv);
                    vJ[i].C[s + 1] = StudyDd.Mul(aJ[i].C[s], inv);
                }
            }

            double hp = Math.Pow(h, order);
            double errP = 0d;
            double errV = 0d;
            for (int i = 0; i < dim; i++)
            {
                errP = Math.Max(errP, Math.Abs(pJ[i].C[order].ToDouble) * hp);
                errV = Math.Max(errV, Math.Abs(vJ[i].C[order].ToDouble) * hp);
            }

            double norm = Math.Max(errP / positionTolerance, errV / velocityTolerance);
            if (norm <= 1d)
            {
                for (int i = 0; i < dim; i++)
                {
                    p[i] = pJ[i].Eval(h);
                    v[i] = vJ[i].Eval(h);
                }

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

        sw.Stop();
        var P = new double[dim];
        var V = new double[dim];
        for (int i = 0; i < dim; i++)
        {
            P[i] = p[i].ToDouble;
            V[i] = v[i].ToDouble;
        }

        return new DdNBodyResult
        {
            P = P,
            V = V,
            Calls = calls,
            Steps = steps,
            Ms = sw.Elapsed.TotalMilliseconds
        };
    }

    private static double MaxRelDiff(DdJet dd, TaylorJet tj)
    {
        double scale = 0d;
        for (int i = 0; i <= dd.Order; i++)
        {
            scale = Math.Max(scale, Math.Abs(tj.C[i]));
        }

        if (scale <= 0d)
        {
            scale = 1d;
        }

        double worst = 0d;
        for (int i = 0; i <= dd.Order; i++)
        {
            worst = Math.Max(worst, Math.Abs(dd.C[i].ToDouble - tj.C[i]) / scale);
        }

        return worst;
    }

    private static void RunDdSelfCheck()
    {
        Console.WriteLine();
        Console.WriteLine("=== DD self-check (dd jet vs double jet, K=12) ===");
        var rnd = new Random(12345);
        const int order = 12;
        double add = 0d, sub = 0d, mul = 0d, div = 0d, sqrt = 0d, eval = 0d;
        double divId = 0d, sqrtId = 0d;

        for (int trial = 0; trial < 8; trial++)
        {
            var ca = new double[order + 1];
            var cb = new double[order + 1];
            for (int k = 0; k <= order; k++)
            {
                ca[k] = (rnd.NextDouble() * 2d) - 1d;
                cb[k] = (rnd.NextDouble() * 2d) - 1d;
            }

            ca[0] = 2d + rnd.NextDouble();
            cb[0] = 2d + rnd.NextDouble();
            TaylorJet a = TaylorJet.FromSeries(order, ca);
            TaylorJet b = TaylorJet.FromSeries(order, cb);
            DdJet da = DdJet.FromSeries(order, ca);
            DdJet db = DdJet.FromSeries(order, cb);

            add = Math.Max(add, MaxRelDiff(DdJet.Add(da, db), TaylorJet.Add(a, b)));
            sub = Math.Max(sub, MaxRelDiff(DdJet.Sub(da, db), TaylorJet.Sub(a, b)));
            mul = Math.Max(mul, MaxRelDiff(DdJet.Mul(da, db), TaylorJet.Mul(a, b)));
            div = Math.Max(div, MaxRelDiff(DdJet.Div(da, db), TaylorJet.Div(a, b)));
            sqrt = Math.Max(sqrt, MaxRelDiff(DdJet.Sqrt(da), TaylorJet.Sqrt(a)));

            double scaleEval = Math.Max(1d, Math.Abs(TaylorJet.Mul(a, b).Eval(0.37d)));
            eval = Math.Max(eval, Math.Abs(
                DdJet.Mul(da, db).Eval(0.37d).ToDouble - TaylorJet.Mul(a, b).Eval(0.37d)) / scaleEval);

            double scaleA = 0d;
            for (int k = 0; k <= order; k++)
            {
                scaleA = Math.Max(scaleA, Math.Abs(ca[k]));
            }

            DdJet q = DdJet.Div(DdJet.Mul(da, db), db);
            DdJet s = DdJet.Sqrt(DdJet.Mul(da, da));
            for (int k = 0; k <= order; k++)
            {
                divId = Math.Max(divId,
                    Math.Abs(StudyDd.Sub(q.C[k], da.C[k]).ToDouble) / scaleA);
                sqrtId = Math.Max(sqrtId,
                    Math.Abs(StudyDd.Sub(s.C[k], da.C[k]).ToDouble) / scaleA);
            }
        }

        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "add={0:E2} sub={1:E2} mul={2:E2} div={3:E2} sqrt={4:E2} eval={5:E2}",
            add, sub, mul, div, sqrt, eval));
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "identity: div(mul(a,b),b)-a={0:E2}, sqrt(a*a)-a={1:E2} (relative)",
            divId, sqrtId));
        double worst = Math.Max(Math.Max(Math.Max(add, sub), Math.Max(mul, div)),
            Math.Max(Math.Max(sqrt, eval), Math.Max(divId, sqrtId)));
        Console.WriteLine(worst < 1e-12d ? "SELF-CHECK PASS" : "SELF-CHECK FAIL");
    }

    private static bool DdSmallStep;
    private static bool DdUseOpt;

    private static void CompareY6CFine(string label, StarSystem sys, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== Y6C self-convergence: " + label + ", " + years + " y ===");
        StudyNBody nb = StudyMakeNBody(sys);
        StudyRefResult r1 = StudyRefRun(nb, "Y6C", 14.0625d, years, 0d);
        StudyRefResult r2 = StudyRefRun(nb, "Y6C", 7.03125d, years, 0d);
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "inner 14.0625 vs 7.03125 = {0:E3} m; mid={1:E3}; outer={2:E3}; ratio={3:F1}",
            StudyRel(r2.P, 2, 1, r1.P),
            StudyRel(r2.P, 1, 0, r1.P),
            StudyRel(r2.P, nb.Count - 1, 0, r1.P),
            StudyRel(r2.P, 2, 1, r1.P) / Math.Max(StudyRel(r1.P, 2, 1, StudyRefRun(nb, "Y6C", 28.125d, years, 0d).P), 1e-30d)));
        Console.WriteLine("  fine calls=" + r2.Calls + ", " + r2.Ms.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
    }

    private static void DdForceCheck(string label, StudyNBody nb)
    {
        int n = nb.Count;
        int dim = 3 * n;
        var pj = new DdJet[dim];
        for (int i = 0; i < dim; i++)
        {
            pj[i] = DdJet.Constant(0, StudyDd.From(nb.P0[i]));
        }

        var aj = new DdJet[dim];
        DdAccel(pj, aj, nb.Mu, n, 0);
        var ad = new double[dim];
        StudyAcc(nb.P0, ad, nb.Mu);
        double scale = 0d;
        double worst = 0d;
        for (int i = 0; i < dim; i++)
        {
            scale = Math.Max(scale, Math.Abs(ad[i]));
            worst = Math.Max(worst, Math.Abs(aj[i].C[0].ToDouble - ad[i]));
        }

        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "force check ({0}): max |a_dd - a_double| / max|a| = {1:E2}", label, worst / Math.Max(scale, 1e-300d)));
    }

    private static void CompareY6CFiner(string label, StarSystem sys, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== Y6C finer self-convergence: " + label + " ===");
        StudyNBody nb = StudyMakeNBody(sys);
        StudyRefResult r1 = StudyRefRun(nb, "Y6C", 7.03125d, years, 0d);
        StudyRefResult r2 = StudyRefRun(nb, "Y6C", 3.515625d, years, 0d);
        Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "inner 7.03125 vs 3.515625 = {0:E3} m; mid={1:E3}; outer={2:E3}",
            StudyRel(r2.P, 2, 1, r1.P), StudyRel(r2.P, 1, 0, r1.P), StudyRel(r2.P, nb.Count - 1, 0, r1.P)));
        Console.WriteLine("  calls=" + r2.Calls + ", " + r2.Ms.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
    }

    private static void CompareDdCase(
        string label, StarSystem sys, double years, double refH, double selfH, bool k24)
    {
        Console.WriteLine();
        Console.WriteLine("=== DD Taylor: " + label + ", " + years + " y ===");
        StudyNBody nb = StudyMakeNBody(sys);
        DdForceCheck(label, nb);
        StudyRefResult reference = StudyRefRun(nb, "Y6C", refH, years, 0d);
        StudyRefResult self = StudyRefRun(nb, "Y6C", selfH, years, 0d);
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "reference Y6C h={0} s; self-convergence vs h={1} s (inner) = {2:E3} m; calls={3}; {4:F0} ms",
            refH, selfH, StudyRel(reference.P, 2, 1, self.P), reference.Calls, reference.Ms));

        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);
        double[] mom0 = TotalMomentum(nb.V0, nb.Mu);
        double[] ang0 = TotalAngular(nb.P0, nb.V0, nb.Mu);
        double sumMuV = 0d;
        for (int i = 0; i < nb.Count; i++)
        {
            sumMuV += nb.Mu[i] * Math.Sqrt(
                (nb.V0[3 * i] * nb.V0[3 * i])
                + (nb.V0[(3 * i) + 1] * nb.V0[(3 * i) + 1])
                + (nb.V0[(3 * i) + 2] * nb.V0[(3 * i) + 2]));
        }

        Console.WriteLine("method        K        h     steps       calls        ms   allocKiB        inner          mid        outer       dE/E");
        var orders = k24 ? new[] { 12, 24 } : new[] { 12 };
        double step = DdSmallStep ? 1800d : 3600d;
        foreach (int order in orders)
        {
            Console.WriteLine("  [running DD Taylor K=" + order + " h=" + step + " ...]");
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            DdNBodyResult r = DdUseOpt
                ? RunTaylorNBodyDdOpt(ToTaylor(nb), years, order, 1e-9d, 1e-12d, step, 1e-6d)
                : RunTaylorNBodyDd(ToTaylor(nb), years, order, 1e-9d, 1e-12d, step, 1e-6d);
            double kib = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024d;
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,-10} {1,3} {2,8:F0} {3,9} {4,11} {5,9:F1} {6,10:F1} {7,12:E3} {8,12:E3} {9,12:E3} {10,9:E2}",
                "DD Taylor", order, step, r.Steps, r.Calls, r.Ms, kib,
                StudyRel(r.P, 2, 1, reference.P),
                StudyRel(r.P, 1, 0, reference.P),
                StudyRel(r.P, nb.Count - 1, 0, reference.P),
                Math.Abs((StudyEnergy(r.P, r.V, nb.Mu) - e0) / e0)));

            double[] mom = TotalMomentum(r.V, nb.Mu);
            double[] ang = TotalAngular(r.P, r.V, nb.Mu);
            double dP = Magnitude3(Sub3(mom, mom0)) / Math.Max(sumMuV, 1e-300d);
            double dL = Magnitude3(Sub3(ang, ang0)) / Math.Max(Magnitude3(ang0), 1e-300d);
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "  invariants: dP/sum(mu|v|) = {0:E2}; dL/L = {1:E2}",
                dP, dL));
        }
    }

    private static double[] TotalMomentum(double[] v, double[] mu)
    {
        int n = mu.Length;
        var m = new double[3];
        for (int i = 0; i < n; i++)
        {
            m[0] += mu[i] * v[3 * i];
            m[1] += mu[i] * v[(3 * i) + 1];
            m[2] += mu[i] * v[(3 * i) + 2];
        }

        return m;
    }

    private static double[] TotalAngular(double[] p, double[] v, double[] mu)
    {
        int n = mu.Length;
        var l = new double[3];
        for (int i = 0; i < n; i++)
        {
            double rx = p[3 * i];
            double ry = p[(3 * i) + 1];
            double rz = p[(3 * i) + 2];
            double vx = v[3 * i];
            double vy = v[(3 * i) + 1];
            double vz = v[(3 * i) + 2];
            l[0] += mu[i] * ((ry * vz) - (rz * vy));
            l[1] += mu[i] * ((rz * vx) - (rx * vz));
            l[2] += mu[i] * ((rx * vy) - (ry * vx));
        }

        return l;
    }

    private static double[] Sub3(double[] a, double[] b)
    {
        return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
    }

    private static double Magnitude3(double[] a)
    {
        return Math.Sqrt((a[0] * a[0]) + (a[1] * a[1]) + (a[2] * a[2]));
    }






    // ------------------------------------------------------------------
    // Allocation-free dd jets: views into one arena, in-place operations.
    // ------------------------------------------------------------------

    private readonly struct DdView
    {
        public readonly StudyDd[] Pool;
        public readonly int Offset;
        public readonly int Order;

        public DdView(StudyDd[] pool, int offset, int order)
        {
            Pool = pool;
            Offset = offset;
            Order = order;
        }

        public ref StudyDd C(int i) => ref Pool[Offset + i];
    }

    private sealed class DdArena
    {
        public readonly StudyDd[] Pool;
        public int Cursor;

        public DdArena(int capacity)
        {
            Pool = new StudyDd[capacity];
        }

        public DdView New(int order)
        {
            var v = new DdView(Pool, Cursor, order);
            Cursor += order + 1;
            return v;
        }

        public void ResetTo(int cursor)
        {
            Cursor = cursor;
        }
    }

    private static void JetZero(DdView r)
    {
        for (int i = 0; i <= r.Order; i++)
        {
            r.C(i) = StudyDd.Zero;
        }
    }

    private static void JetSetConstant(DdView r, StudyDd value)
    {
        r.C(0) = value;
        for (int i = 1; i <= r.Order; i++)
        {
            r.C(i) = StudyDd.Zero;
        }
    }

    private static void JetAdd(DdView a, DdView b, DdView r)
    {
        for (int i = 0; i <= r.Order; i++)
        {
            r.C(i) = StudyDd.Add(a.C(i), b.C(i));
        }
    }

    private static void JetSub(DdView a, DdView b, DdView r)
    {
        for (int i = 0; i <= r.Order; i++)
        {
            r.C(i) = StudyDd.Sub(a.C(i), b.C(i));
        }
    }

    private static void JetScale(DdView a, double s, DdView r)
    {
        StudyDd sd = StudyDd.From(s);
        for (int i = 0; i <= r.Order; i++)
        {
            r.C(i) = StudyDd.Mul(a.C(i), sd);
        }
    }

    private static void JetMul(DdView a, DdView b, DdView r)
    {
        int order = r.Order;
        for (int n = 0; n <= order; n++)
        {
            StudyDd sum = StudyDd.Zero;
            for (int k = 0; k <= n; k++)
            {
                sum = StudyDd.Add(sum, StudyDd.Mul(a.C(k), b.C(n - k)));
            }

            r.C(n) = sum;
        }
    }

    private static void JetDiv(DdView a, DdView b, DdView r)
    {
        int order = r.Order;
        r.C(0) = StudyDd.Div(a.C(0), b.C(0));
        for (int n = 1; n <= order; n++)
        {
            StudyDd sum = a.C(n);
            for (int k = 1; k <= n; k++)
            {
                sum = StudyDd.Sub(sum, StudyDd.Mul(b.C(k), r.C(n - k)));
            }

            r.C(n) = StudyDd.Div(sum, b.C(0));
        }
    }

    private static void JetSqrt(DdView a, DdView r)
    {
        int order = r.Order;
        r.C(0) = StudyDd.Sqrt(a.C(0));
        StudyDd denom = StudyDd.Add(r.C(0), r.C(0));
        for (int n = 1; n <= order; n++)
        {
            StudyDd sum = a.C(n);
            for (int k = 1; k < n; k++)
            {
                sum = StudyDd.Sub(sum, StudyDd.Mul(r.C(k), r.C(n - k)));
            }

            r.C(n) = StudyDd.Div(sum, denom);
        }
    }

    private static void JetMulAdd(DdView acc, DdView a, DdView b, bool subtract)
    {
        int order = acc.Order;
        for (int n = 0; n <= order; n++)
        {
            StudyDd sum = StudyDd.Zero;
            for (int k = 0; k <= n; k++)
            {
                sum = StudyDd.Add(sum, StudyDd.Mul(a.C(k), b.C(n - k)));
            }

            acc.C(n) = subtract ? StudyDd.Sub(acc.C(n), sum) : StudyDd.Add(acc.C(n), sum);
        }
    }

    private static StudyDd JetEval(DdView a, double tau)
    {
        StudyDd x = StudyDd.From(tau);
        StudyDd value = StudyDd.Zero;
        for (int n = a.Order; n >= 0; n--)
        {
            value = StudyDd.Add(StudyDd.Mul(value, x), a.C(n));
        }

        return value;
    }

    private static void DdAccelOpt(
        DdView[] p, DdView[] a, double[] mu, int n, int order, DdArena arena, DdView one)
    {
        int dim = 3 * n;
        for (int i = 0; i < dim; i++)
        {
            JetZero(a[i]);
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                DdView dx = arena.New(order);
                DdView dy = arena.New(order);
                DdView dz = arena.New(order);
                DdView r2 = arena.New(order);
                DdView r = arena.New(order);
                DdView tmp = arena.New(order);
                DdView inv = arena.New(order);
                DdView fi = arena.New(order);
                DdView fj = arena.New(order);

                JetSub(p[3 * j], p[3 * i], dx);
                JetSub(p[(3 * j) + 1], p[(3 * i) + 1], dy);
                JetSub(p[(3 * j) + 2], p[(3 * i) + 2], dz);
                JetMul(dx, dx, r2);
                JetMul(dy, dy, tmp);
                JetAdd(r2, tmp, r2);
                JetMul(dz, dz, tmp);
                JetAdd(r2, tmp, r2);
                JetSqrt(r2, r);
                JetMul(r2, r, tmp);
                JetDiv(one, tmp, inv);
                JetScale(inv, mu[j], fi);
                JetScale(inv, mu[i], fj);

                JetMulAdd(a[3 * i], dx, fi, false);
                JetMulAdd(a[(3 * i) + 1], dy, fi, false);
                JetMulAdd(a[(3 * i) + 2], dz, fi, false);
                JetMulAdd(a[3 * j], dx, fj, true);
                JetMulAdd(a[(3 * j) + 1], dy, fj, true);
                JetMulAdd(a[(3 * j) + 2], dz, fj, true);
            }
        }
    }

    private static DdNBodyResult RunTaylorNBodyDdOpt(
        TaylorNBodyInput input, double years, int order,
        double positionTolerance, double velocityTolerance, double maxStep, double minStep)
    {
        int n = input.Names.Length;
        int dim = 3 * n;
        var p = new StudyDd[dim];
        var v = new StudyDd[dim];
        for (int i = 0; i < dim; i++)
        {
            p[i] = StudyDd.From(input.P0[i]);
            v[i] = StudyDd.From(input.V0[i]);
        }

        double T = years * 365.25d * 86400d;
        double t = 0d;
        double h = Math.Min(maxStep, T);
        int jets = (3 * dim) + (12 * n * n) + 32;
        var arena = new DdArena(jets * (order + 1));
        var pJ = new DdView[dim];
        var vJ = new DdView[dim];
        var aJ = new DdView[dim];
        for (int i = 0; i < dim; i++)
        {
            pJ[i] = arena.New(order);
            vJ[i] = arena.New(order);
            aJ[i] = arena.New(order);
        }

        DdView one = arena.New(order);
        JetSetConstant(one, StudyDd.From(1d));
        int scratchBase = arena.Cursor;
        var invs = new StudyDd[order];
        for (int s = 0; s < order; s++)
        {
            invs[s] = StudyDd.Div(StudyDd.From(1d), StudyDd.From(s + 1d));
        }

        long calls = 0L;
        long steps = 0L;
        var sw = Stopwatch.StartNew();

        while (t < T - 1e-9d)
        {
            h = Math.Min(h, T - t);
            for (int i = 0; i < dim; i++)
            {
                JetSetConstant(pJ[i], p[i]);
                JetSetConstant(vJ[i], v[i]);
            }

            for (int s = 0; s < order; s++)
            {
                arena.ResetTo(scratchBase);
                DdAccelOpt(pJ, aJ, input.Mu, n, order, arena, one);
                calls++;
                StudyDd inv = invs[s];
                for (int i = 0; i < dim; i++)
                {
                    pJ[i].C(s + 1) = StudyDd.Mul(vJ[i].C(s), inv);
                    vJ[i].C(s + 1) = StudyDd.Mul(aJ[i].C(s), inv);
                }
            }

            double hp = Math.Pow(h, order);
            double errP = 0d;
            double errV = 0d;
            for (int i = 0; i < dim; i++)
            {
                errP = Math.Max(errP, Math.Abs(pJ[i].C(order).ToDouble) * hp);
                errV = Math.Max(errV, Math.Abs(vJ[i].C(order).ToDouble) * hp);
            }

            double norm = Math.Max(errP / positionTolerance, errV / velocityTolerance);
            if (norm <= 1d)
            {
                for (int i = 0; i < dim; i++)
                {
                    p[i] = JetEval(pJ[i], h);
                    v[i] = JetEval(vJ[i], h);
                }

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

        sw.Stop();
        var P = new double[dim];
        var V = new double[dim];
        for (int i = 0; i < dim; i++)
        {
            P[i] = p[i].ToDouble;
            V[i] = v[i].ToDouble;
        }

        return new DdNBodyResult
        {
            P = P,
            V = V,
            Calls = calls,
            Steps = steps,
            Ms = sw.Elapsed.TotalMilliseconds
        };
    }

    private static void CompareDdImplementations(string label, StarSystem sys, double years, double refH)
    {
        Console.WriteLine();
        Console.WriteLine("=== DD old vs optimized: " + label + ", " + years + " y ===");
        StudyNBody nb = StudyMakeNBody(sys);
        DdForceCheck(label, nb);
        StudyRefResult reference = StudyRefRun(nb, "Y6C", refH, years, 0d);
        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);

        long a0 = GC.GetAllocatedBytesForCurrentThread();
        DdNBodyResult old = RunTaylorNBodyDd(ToTaylor(nb), years, 12, 1e-9d, 1e-12d, 3600d, 1e-6d);
        double oldKiB = (GC.GetAllocatedBytesForCurrentThread() - a0) / 1024d;

        long b0 = GC.GetAllocatedBytesForCurrentThread();
        DdNBodyResult opt = RunTaylorNBodyDdOpt(ToTaylor(nb), years, 12, 1e-9d, 1e-12d, 3600d, 1e-6d);
        double optKiB = (GC.GetAllocatedBytesForCurrentThread() - b0) / 1024d;

        double maxDP = 0d;
        double maxDV = 0d;
        double maxP = 0d;
        for (int i = 0; i < old.P.Length; i++)
        {
            maxDP = Math.Max(maxDP, Math.Abs(old.P[i] - opt.P[i]));
            maxDV = Math.Max(maxDV, Math.Abs(old.V[i] - opt.V[i]));
            maxP = Math.Max(maxP, Math.Abs(old.P[i]));
        }

        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "max |P_old-P_opt| = {0:E3} m ({1:E2} relative); max |V| diff = {2:E3} m/s",
            maxDP, maxDP / Math.Max(maxP, 1e-300d), maxDV));
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "old: {0:F1} s, {1:N0} KiB; opt: {2:F1} s, {3:N0} KiB; speedup={4:F2}x",
            old.Ms / 1000d, oldKiB, opt.Ms / 1000d, optKiB, old.Ms / Math.Max(opt.Ms, 1e-9d)));

        PrintDdRow("old", old, reference, nb, e0);
        PrintDdRow("opt", opt, reference, nb, e0);

        double limit = Math.Max(1e-6d, StudyRel(reference.P, 2, 1, reference.P) + 1e-6d);
        Console.WriteLine(maxDP <= limit ? "METHOD SAME (within 1 mm)" : "METHOD DIFFERS (> 1 mm)");
    }

    private static void PrintDdRow(string name, DdNBodyResult r, StudyRefResult reference, StudyNBody nb, double e0)
    {
        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0,-4} inner={1:E3} mid={2:E3} outer={3:E3} dE/E={4:E2} steps={5} calls={6}",
            name,
            StudyRel(r.P, 2, 1, reference.P),
            StudyRel(r.P, 1, 0, reference.P),
            StudyRel(r.P, nb.Count - 1, 0, reference.P),
            Math.Abs((StudyEnergy(r.P, r.V, nb.Mu) - e0) / e0),
            r.Steps, r.Calls));
    }

}
