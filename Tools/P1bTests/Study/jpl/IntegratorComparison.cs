using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

internal static partial class P1bTests
{
    private sealed class IntegratorRow
    {
        public string Name;
        public string Method;
        public double H;
        public bool Pn;
        public double[] P;
        public double[] V;
        public double Ms;
        /// <summary>Integrator error against a same-force reference.</summary>
        public double Err;
        public long Calls;
    }

    private static double IntegratorBodyError(double[] a, double[] b, int index)
    {
        int i = 3 * index;
        double dx = a[i] - b[i];
        double dy = a[i + 1] - b[i + 1];
        double dz = a[i + 2] - b[i + 2];
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static double IntegratorError(double[] a, double[] b, int n, out int worst)
    {
        worst = -1;
        double best = 0d;
        for (int i = 0; i < n; i++)
        {
            double e = IntegratorBodyError(a, b, i);
            if (e > best)
            {
                best = e;
                worst = i;
            }
        }

        return best;
    }

    private static int Test545_JplIntegratorComparison()
    {
        Console.WriteLine("--- Integrator comparison: 10 bodies, DE441, Newtonian, 10 years ---");
        string path = Path.Combine("Study", "jpl", "horizons_real_2026_10y.json");
        Check(File.Exists(path), "JPL vectors file", path);
        if (!File.Exists(path))
        {
            return 0;
        }

        JplFile jpl = JsonSerializer.Deserialize<JplFile>(File.ReadAllText(path));
        string[] ten =
        {
            "Mercury", "Venus", "Earth", "Moon", "Mars",
            "Jupiter", "Saturn", "Uranus", "Neptune"
        };
        StudyNBody nb = JplNBody(jpl, ten, true, true);
        int n = nb.Count;
        int length = 3 * n;
        const double days = 3650d;
        const double year = 365d;

        var referenceWatch = Stopwatch.StartNew();
        var reference = JplDop853(nb, days, 1e-9d, 1e-12d, 1e-14d, false);
        referenceWatch.Stop();
        var referenceFine = JplDop853(nb, days, 1e-10d, 1e-13d, 1e-15d, false);
        var pnReference = JplDop853(nb, days, 1e-9d, 1e-12d, 1e-14d, true);
        // 1PN is a different force model, so it needs its own fine run and its
        // own convergence floor; comparing it with the Newtonian reference is a
        // force-model difference, not an integrator error.
        var pnReferenceFine = JplDop853(nb, days, 1e-10d, 1e-13d, 1e-15d, true);
        double referenceMs = referenceWatch.Elapsed.TotalMilliseconds;
        int referenceWorst;
        int pnReferenceWorst;
        double referenceSelf = IntegratorError(referenceFine.p, reference.p, n, out referenceWorst);
        double pnReferenceSelf = IntegratorError(
            pnReferenceFine.p, pnReference.p, n, out pnReferenceWorst);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "reference: DOP853 rtol=1e-14, atol=1e-9/1e-12, maxStep=1 d, {0:F0} ms; self-convergence (1e-13 vs 1e-14, worst {2}) = {1:E2} m",
            referenceMs, referenceSelf, nb.Names[referenceWorst]));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "1PN reference: DOP853 rtol=1e-14; self-convergence (1e-13 vs 1e-14, worst {0}) = {1:E2} m",
            nb.Names[pnReferenceWorst], pnReferenceSelf));
        Check(referenceSelf < 10d, "JPL reference self-convergence",
            referenceSelf.ToString("E2", CultureInfo.InvariantCulture) + " m < 10 m");
        Check(pnReferenceSelf < 10d, "JPL 1PN reference self-convergence",
            pnReferenceSelf.ToString("E2", CultureInfo.InvariantCulture) + " m < 10 m");

        var rows = new[]
        {
            new IntegratorRow { Name = "DOP853 adaptive", Method = "DOP853", H = 86400d },
            new IntegratorRow { Name = "DOPRI5 1 d", Method = "DOPRI5", H = 86400d },
            new IntegratorRow { Name = "RK4 1 d", Method = "RK4", H = 86400d },
            new IntegratorRow { Name = "PEFRL 1 d", Method = "PEFRL", H = 86400d },
            new IntegratorRow { Name = "Y6 1 d", Method = "Y6", H = 86400d },
            new IntegratorRow { Name = "DOPRI5 0.5 d", Method = "DOPRI5", H = 43200d },
            new IntegratorRow { Name = "RK4 0.5 d", Method = "RK4", H = 43200d },
            new IntegratorRow { Name = "PEFRL 0.5 d", Method = "PEFRL", H = 43200d },
            new IntegratorRow { Name = "Y6 0.5 d", Method = "Y6", H = 43200d },
            new IntegratorRow { Name = "DOP853 1PN", Method = "DOP853", H = 86400d, Pn = true },
            new IntegratorRow { Name = "Y6 1PN 112.5 s", Method = "Y6C", H = 112.5d, Pn = true }
        };

        Parallel.For(
            0,
            rows.Length,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            index =>
            {
                IntegratorRow row = rows[index];
                var watch = Stopwatch.StartNew();
                long calls = 0L;
                if (row.Method == "DOP853")
                {
                    var run = JplDop853(nb, days, 1e-6d, 1e-9d, 1e-13d, row.Pn);
                    row.P = run.p;
                    row.V = run.v;
                    calls = run.calls;
                }
                else if (row.Method == "Y6C")
                {
                    var run = JplY6C(nb, days, row.H, row.Pn, out calls);
                    row.P = run.p;
                    row.V = run.v;
                }
                else
                {
                    row.P = (double[])nb.P0.Clone();
                    row.V = (double[])nb.V0.Clone();
                    long steps = (long)Math.Round(days * 86400d / row.H);
                    StudyStep(row.Method, row.P, row.V, row.H, nb.Mu, steps, ref calls);
                }

                row.Ms = watch.Elapsed.TotalMilliseconds;
                row.Calls = calls;
                row.Err = IntegratorError(row.P, row.Pn ? pnReference.p : reference.p, n, out _);
            });

        Console.WriteLine("method                 force  h        calls  ms(wall,par)  max err(km)   (ms is not a cost metric)");

        double bestNewton = double.MaxValue;
        string bestNewtonName = "";
        double bestPn = double.MaxValue;
        string bestPnName = "";
        double bestFixed = double.MaxValue;
        string bestFixedName = "";
        for (int i = 0; i < rows.Length; i++)
        {
            IntegratorRow row = rows[i];
            string h = row.Method == "DOP853" || row.Method == "Y6C"
                ? (row.Method == "Y6C" ? "112.5 s" : "adaptive")
                : (row.H / 86400d).ToString("F1", CultureInfo.InvariantCulture) + " d";
            // Ranking cannot go below the reference's own uncertainty for the SAME
            // force model: an error smaller than the self-convergence is not
            // evidence of a better method. 1PN and Newtonian rows never share a
            // ranking, because their difference is the force model.
            double floor = row.Pn ? pnReferenceSelf : referenceSelf;
            double ranked = Math.Max(row.Err, floor);
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-22} {1,-6} {2,-8} {3,10} {4,8:F0} {5,14:F3}{6}",
                row.Name, row.Pn ? "1PN" : "newton", h, row.Calls, row.Ms, row.Err / 1000d,
                ranked > row.Err ? " (at reference floor)" : ""));
            Check(double.IsFinite(row.Err), "integrator " + row.Name + " finite", "");
            if (row.Pn)
            {
                if (ranked < bestPn)
                {
                    bestPn = ranked;
                    bestPnName = row.Name;
                }
            }
            else
            {
                if (ranked < bestNewton)
                {
                    bestNewton = ranked;
                    bestNewtonName = row.Name;
                }

                // DOP853 is also the reference method; the fixed-step comparison is
                // the independent-method statement.
                if (row.Method != "DOP853" && ranked < bestFixed)
                {
                    bestFixed = ranked;
                    bestFixedName = row.Name;
                }
            }
        }

        Console.WriteLine("best Newtonian (reference floor "
            + (referenceSelf / 1000d).ToString("E2", CultureInfo.InvariantCulture) + " km): "
            + bestNewtonName);
        Console.WriteLine("best Newtonian fixed-step: " + bestFixedName);
        Console.WriteLine("best 1PN (reference floor "
            + (pnReferenceSelf / 1000d).ToString("E2", CultureInfo.InvariantCulture) + " km): "
            + bestPnName);
        Console.WriteLine("note: the adaptive DOP853 row uses looser tolerances than the "
            + "DOP853 reference; it is not an independent method check.");
        Console.WriteLine("--- Integrator comparison complete ---");
        return 0;
    }
}
