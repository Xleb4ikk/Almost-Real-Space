using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

internal static partial class P1bTests
{
    private delegate void StudyAccel(double[] p, double[] v, double[] a);

    private static void StudyRk4StepAcc(
        double[] p, double[] v, double h, StudyAccel acc, long steps, ref long calls)
    {
        int L = p.Length;
        var a1 = new double[L];
        var a2 = new double[L];
        var a3 = new double[L];
        var a4 = new double[L];
        var tp = new double[L];
        var v2 = new double[L];
        var v3 = new double[L];
        var v4 = new double[L];
        for (long s = 0; s < steps; s++)
        {
            acc(p, v, a1);
            calls++;
            for (int i = 0; i < L; i++)
            {
                tp[i] = p[i] + (0.5d * h * v[i]);
                v2[i] = v[i] + (0.5d * h * a1[i]);
            }

            acc(tp, v2, a2);
            calls++;
            for (int i = 0; i < L; i++)
            {
                tp[i] = p[i] + (0.5d * h * v2[i]);
                v3[i] = v[i] + (0.5d * h * a2[i]);
            }

            acc(tp, v3, a3);
            calls++;
            for (int i = 0; i < L; i++)
            {
                tp[i] = p[i] + (h * v3[i]);
                v4[i] = v[i] + (h * a3[i]);
            }

            acc(tp, v4, a4);
            calls++;
            for (int i = 0; i < L; i++)
            {
                p[i] += h / 6d * (v[i] + (2d * v2[i]) + (2d * v3[i]) + v4[i]);
                v[i] += h / 6d * (a1[i] + (2d * a2[i]) + (2d * a3[i]) + a4[i]);
            }
        }
    }

    private static void StudyDopri5FixedAcc(
        double[] p, double[] v, double h, StudyAccel acc, long steps, ref long calls)
    {
        int L = p.Length;
        double[][] A =
        {
            new[] { 1d / 5d },
            new[] { 3d / 40d, 9d / 40d },
            new[] { 44d / 45d, -56d / 15d, 32d / 9d },
            new[] { 19372d / 6561d, -25360d / 2187d, 64448d / 6561d, -212d / 729d },
            new[] { 9017d / 3168d, -355d / 33d, 46732d / 5247d, 49d / 176d, -5103d / 18656d }
        };
        double[] b = { 35d / 384d, 0d, 500d / 1113d, 125d / 192d, -2187d / 6784d, 11d / 84d };
        var kp = new double[6][];
        var kv = new double[6][];
        for (int s = 0; s < 6; s++)
        {
            kp[s] = new double[L];
            kv[s] = new double[L];
        }

        var tp = new double[L];
        var tv = new double[L];
        Array.Copy(v, kp[0], L);
        acc(p, v, kv[0]);
        calls++;
        for (long st = 0; st < steps; st++)
        {
            for (int s = 1; s < 6; s++)
            {
                for (int i = 0; i < L; i++)
                {
                    double sp = 0d;
                    double sv = 0d;
                    for (int j = 0; j < s; j++)
                    {
                        sp += A[s - 1][j] * kp[j][i];
                        sv += A[s - 1][j] * kv[j][i];
                    }

                    tp[i] = p[i] + (h * sp);
                    tv[i] = v[i] + (h * sv);
                }

                Array.Copy(tv, kp[s], L);
                acc(tp, tv, kv[s]);
                calls++;
            }

            for (int i = 0; i < L; i++)
            {
                double sp = 0d;
                double sv = 0d;
                for (int j = 0; j < 6; j++)
                {
                    sp += b[j] * kp[j][i];
                    sv += b[j] * kv[j][i];
                }

                p[i] += h * sp;
                v[i] += h * sv;
            }

            Array.Copy(v, kp[0], L);
            acc(p, v, kv[0]);
            calls++;
        }
    }

    private static void StudyPefrlAcc(
        double[] p, double[] v, double h, StudyAccel acc, long steps, ref long calls)
    {
        const double xi = 0.1786178958448091d;
        const double lam = -0.2123418310626054d;
        const double chi = -0.06626458266981849d;
        int L = p.Length;
        var a = new double[L];
        for (long s = 0; s < steps; s++)
        {
            for (int i = 0; i < L; i++)
            {
                p[i] += xi * h * v[i];
            }

            acc(p, v, a);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += (1d - (2d * lam)) * h / 2d * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += chi * h * v[i];
            }

            acc(p, v, a);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += lam * h * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += (1d - (2d * (chi + xi))) * h * v[i];
            }

            acc(p, v, a);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += lam * h * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += chi * h * v[i];
            }

            acc(p, v, a);
            calls++;
            for (int i = 0; i < L; i++)
            {
                v[i] += (1d - (2d * lam)) * h / 2d * a[i];
            }

            for (int i = 0; i < L; i++)
            {
                p[i] += xi * h * v[i];
            }
        }
    }

    private static void StudyY6Acc(
        double[] p, double[] v, double h, StudyAccel acc, long steps, ref long calls)
    {
        int L = p.Length;
        var a = new double[L];
        double[] ws = StudyY6Weights();
        acc(p, v, a);
        calls++;
        for (long s = 0; s < steps; s++)
        {
            foreach (double w in ws)
            {
                double dt = w * h;
                for (int i = 0; i < L; i++)
                {
                    v[i] += 0.5d * dt * a[i];
                }

                for (int i = 0; i < L; i++)
                {
                    p[i] += dt * v[i];
                }

                acc(p, v, a);
                calls++;
                for (int i = 0; i < L; i++)
                {
                    v[i] += 0.5d * dt * a[i];
                }
            }
        }
    }

    private static double MoonRelativeError(
        double[] candidate, double[] reference, int earth, int moon)
    {
        double dx = (candidate[3 * moon] - candidate[3 * earth])
            - (reference[3 * moon] - reference[3 * earth]);
        double dy = (candidate[(3 * moon) + 1] - candidate[(3 * earth) + 1])
            - (reference[(3 * moon) + 1] - reference[(3 * earth) + 1]);
        double dz = (candidate[(3 * moon) + 2] - candidate[(3 * earth) + 2])
            - (reference[(3 * moon) + 2] - reference[(3 * earth) + 2]);
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static int Test546_JplFullSystemIntegrators()
    {
        Console.WriteLine("--- Every integrator on the full DE441 system: planets + Moon + 409 small bodies ---");
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
        var fullNames = new List<string>(ten);
        foreach (JplBody body in jpl.Bodies)
        {
            if (body.Name.StartsWith("Ast", StringComparison.Ordinal)
                || body.Name.StartsWith("Kbo", StringComparison.Ordinal)
                || body.Name.StartsWith("Kbr", StringComparison.Ordinal))
            {
                fullNames.Add(body.Name);
            }
        }

        StudyNBody nb = JplNBody(jpl, fullNames.ToArray(), true, true);
        int n = nb.Count;
        const int astStart = 10;
        var planetMu = new double[astStart];
        Array.Copy(nb.Mu, planetMu, astStart);
        int earth = JplIndex(nb, "Earth");
        int moon = JplIndex(nb, "Moon");
        const double days = 3650d;

        StudyAccel acc = (pp, vv, aa) =>
        {
            JplAccPlanetsAsteroids(pp, vv, aa, nb.Mu, n, astStart, true, true, planetMu);
            JplAccEarthJ2(pp, aa, earth, moon, nb.Mu[earth], nb.Mu[moon]);
        };

        // Same-force reference: the table used to compare every row with Horizons,
        // so its "best" was partly the force model, not the integrator. This run
        // uses the same accel as the rows; its separation from Horizons is printed
        // separately as the model error and is not attributed to any integrator.
        var refWatch = Stopwatch.StartNew();
        var fullRef = JplDop853(
            nb, days, 1e-7d, 1e-10d, 1e-14d, true, false, true,
            43200d, astStart, true);
        refWatch.Stop();
        var fineWatch = Stopwatch.StartNew();
        var fullRefFine = JplDop853(
            nb, days, 1e-8d, 1e-11d, 1e-15d, true, false, true,
            21600d, astStart, true);
        fineWatch.Stop();
        int referenceWorst;
        double referenceSelf = IntegratorError(fullRefFine.p, fullRef.p, n, out referenceWorst);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "same-force reference: DOP853 full409 rtol=1e-14, {0:F0} ms, {1:N0} calls; "
            + "self-convergence (rtol 1e-15, maxStep 0.25 d, worst {2}) = {3:E2} m",
            refWatch.Elapsed.TotalMilliseconds, fullRef.calls,
            nb.Names[referenceWorst], referenceSelf));
        Check(referenceSelf < 10d, "full409 same-force reference self-convergence",
            referenceSelf.ToString("E2", CultureInfo.InvariantCulture) + " m < 10 m");

        // Eight planets, deliberately excluding the Moon: the Moon is reported
        // separately in geocentric coordinates. Including it here would count it
        // twice and make the divisor disagree with the array.
        string[] bodies =
        {
            "Mercury", "Venus", "Earth", "Mars", "Jupiter",
            "Saturn", "Uranus", "Neptune"
        };
        var rows = new[]
        {
            new IntegratorRow { Name = "DOP853 full409", Method = "DOP853", H = 86400d },
            new IntegratorRow { Name = "DOPRI5 full409 1 d", Method = "DOPRI5", H = 86400d },
            new IntegratorRow { Name = "RK4 full409 1 d", Method = "RK4", H = 86400d },
            new IntegratorRow { Name = "PEFRL full409 1 d", Method = "PEFRL", H = 86400d },
            new IntegratorRow { Name = "Y6 full409 1 d", Method = "Y6", H = 86400d },
            new IntegratorRow { Name = "DOPRI5 full409 0.5 d", Method = "DOPRI5", H = 43200d },
            new IntegratorRow { Name = "RK4 full409 0.5 d", Method = "RK4", H = 43200d },
            new IntegratorRow { Name = "PEFRL full409 0.5 d", Method = "PEFRL", H = 43200d },
            new IntegratorRow { Name = "Y6 full409 0.5 d", Method = "Y6", H = 43200d }
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
                    var run = JplDop853(
                        nb, days, 1e-6d, 1e-9d, 1e-13d, true, false, true, 86400d, astStart, true);
                    row.P = run.p;
                    row.V = run.v;
                    calls = run.calls;
                }
                else
                {
                    row.P = (double[])nb.P0.Clone();
                    row.V = (double[])nb.V0.Clone();
                    long steps = (long)Math.Round(days * 86400d / row.H);
                    switch (row.Method)
                    {
                        case "DOPRI5":
                            StudyDopri5FixedAcc(row.P, row.V, row.H, acc, steps, ref calls);
                            break;
                        case "RK4":
                            StudyRk4StepAcc(row.P, row.V, row.H, acc, steps, ref calls);
                            break;
                        case "PEFRL":
                            StudyPefrlAcc(row.P, row.V, row.H, acc, steps, ref calls);
                            break;
                        case "Y6":
                            StudyY6Acc(row.P, row.V, row.H, acc, steps, ref calls);
                            break;
                    }
                }

                row.Ms = watch.Elapsed.TotalMilliseconds;
                row.Calls = calls;
                // Integrator error only: same force model as the row.
                row.Err = IntegratorError(row.P, fullRef.p, n, out _);
            });

        // Model error is a single property of the force model, not of a row.
        double modelSum = 0d;
        double modelJupiter = 0d;
        double modelSaturn = 0d;
        for (int b = 0; b < bodies.Length; b++)
        {
            bool geo = bodies[b] == "Moon";
            var e = JplStateError(jpl, nb, fullRef.p, fullRef.v, days, bodies[b], geo);
            modelSum += (e.dp / 1000d) * (e.dp / 1000d);
            if (bodies[b] == "Jupiter")
            {
                modelJupiter = e.dp / 1000d;
            }
            else if (bodies[b] == "Saturn")
            {
                modelSaturn = e.dp / 1000d;
            }
        }

        double modelRms = Math.Sqrt(modelSum / 8d);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "model error (same-force reference vs Horizons): RMS(8)={0:F3} km, Jupiter={1:F3} km, Saturn={2:F3} km",
            modelRms, modelJupiter, modelSaturn));
        Console.WriteLine("method                     h          calls   ms(wall,par)  int err(km) int rms8(km) total(km)  Jupiter km  Saturn km  Moon(geo) km");
        // Rank by the SAME-FORCE error, not by the Horizons residual. The
        // Horizons column (total) contains the model error; a coarse method can
        // look better there only because its error cancels part of the model
        // error (e.g. DOPRI5 1 d: 67,092 km same-force vs 2,911 km Horizons).
        // The same-force reference below uses exactly the rows' acceleration.
        int earthIndex = JplIndex(nb, "Earth");
        int moonIndex = JplIndex(nb, "Moon");
        double best = double.MaxValue;
        double bestFixed = double.MaxValue;
        double bestTotal = double.MaxValue;
        string bestName = "";
        string bestFixedName = "";
        string bestTotalName = "";
        for (int r = 0; r < rows.Length; r++)
        {
            IntegratorRow row = rows[r];
            double sum = 0d;
            double jupiter = 0d;
            double saturn = 0d;
            for (int b = 0; b < bodies.Length; b++)
            {
                int index = JplIndex(nb, bodies[b]);
                double e = IntegratorBodyError(row.P, fullRef.p, index) / 1000d;
                sum += e * e;
                if (bodies[b] == "Jupiter")
                {
                    jupiter = e;
                }
                else if (bodies[b] == "Saturn")
                {
                    saturn = e;
                }
            }

            double moonErr = MoonRelativeError(row.P, fullRef.p, earthIndex, moonIndex) / 1000d;
            double sameForceRms = Math.Sqrt(sum / 8d);
            double totalRms = 0d;
            for (int b = 0; b < bodies.Length; b++)
            {
                var e = JplStateError(jpl, nb, row.P, row.V, days, bodies[b], false);
                totalRms += (e.dp / 1000d) * (e.dp / 1000d);
            }

            totalRms = Math.Sqrt(totalRms / 8d);
            string h = row.Method == "DOP853"
                ? "adaptive"
                : (row.H / 86400d).ToString("F1", CultureInfo.InvariantCulture) + " d";
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-25} {1,-9} {2,9} {3,7:F0} {4,10:F3} {5,10:F3} {6,10:F3} {7,10:F3} {8,10:F3} {9,12:F2}",
                row.Name, h, row.Calls, row.Ms, row.Err / 1000d, sameForceRms,
                totalRms, jupiter, saturn, moonErr));
            Check(double.IsFinite(row.Err) && double.IsFinite(sameForceRms)
                    && double.IsFinite(totalRms) && double.IsFinite(moonErr),
                "full " + row.Name + " finite", "");
            if (sameForceRms < best)
            {
                best = sameForceRms;
                bestName = row.Name;
            }

            // The DOP853 row is the same method as the reference; report the
            // independent fixed-step winner separately so the headline is not
            // "DOP853 agrees with DOP853".
            if (row.Method != "DOP853" && sameForceRms < bestFixed)
            {
                bestFixed = sameForceRms;
                bestFixedName = row.Name;
            }

            if (row.Err < bestTotal)
            {
                bestTotal = row.Err;
                bestTotalName = row.Name;
            }
        }

        Console.WriteLine("best by same-force RMS(8): " + bestName
            + "; best fixed-step by same-force RMS: " + bestFixedName
            + "; best by total max over 419: " + bestTotalName
            + "; model floor vs Horizons is " + modelRms.ToString("F3", CultureInfo.InvariantCulture) + " km");
        Check(modelRms < 1000d, "full-system model error is a floor below 1000 km",
            modelRms.ToString("F3", CultureInfo.InvariantCulture) + " km");
        Console.WriteLine("--- Full-system integrator comparison complete ---");
        return 0;
    }
}
