using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private static readonly double StudyKeplerVelocityScale =
        Math.Sqrt(2d * 1.32712440018e20d / StudyAU);

    private static double[] ParseKeplerGolden(string[] values)
    {
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = double.Parse(values[i], CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static StudyNBody KeplerTwoBody(Kepler3DCase c)
    {
        return new StudyNBody
        {
            Names = new[] { "Central", "Ship" },
            Mu = new[] { c.Mu, 0d },
            P0 = new[]
            {
                0d, 0d, 0d,
                c.P0[0], c.P0[1], c.P0[2]
            },
            V0 = new[]
            {
                0d, 0d, 0d,
                c.V0[0], c.V0[1], c.V0[2]
            }
        };
    }

    private static (double[] p, double[] v, long calls, double ms, long steps) StudyRunFixedSeconds(
        StudyNBody nb, string method, double h, double seconds)
    {
        long fullSteps = (long)Math.Floor(seconds / h);
        double remainder = seconds - (fullSteps * h);
        var p = (double[])nb.P0.Clone();
        var v = (double[])nb.V0.Clone();
        long calls = 0L;
        var sw = Stopwatch.StartNew();
        if (fullSteps > 0)
        {
            StudyStep(method, p, v, h, nb.Mu, fullSteps, ref calls);
        }

        bool tail = remainder > 1e-12d;
        if (tail)
        {
            StudyStep(method, p, v, remainder, nb.Mu, 1, ref calls);
        }

        sw.Stop();
        return (p, v, calls, sw.Elapsed.TotalMilliseconds, fullSteps + (tail ? 1L : 0L));
    }

    private static double GoldStringDiff(string[] a, string[] b)
    {
        double sum = 0d;
        for (int i = 0; i < 3; i++)
        {
            double d = (double)(GoldFix(a[i]) - GoldFix(b[i])) / 1e30;
            sum += d * d;
        }

        return Math.Sqrt(sum);
    }

    private static double KeplerDp(double[] p, double[] target)
    {
        double dx = (p[3] - target[0]);
        double dy = (p[4] - target[1]);
        double dz = (p[5] - target[2]);
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static double KeplerDv(double[] v, double[] target)
    {
        double dx = (v[3] - target[0]);
        double dy = (v[4] - target[1]);
        double dz = (v[5] - target[2]);
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static int Test528_IntegratorBenchmark()
    {
        Console.WriteLine("--- Integrator benchmark: known Kepler solutions + solar 7-body ---");
        Console.WriteLine("Taylor is excluded; all runs are plain double.");
        int failuresBefore = failures;

        string[] methods = { "RK4", "DOPRI5", "PEFRL", "Y6", "Y6C" };
        var cases = new[]
        {
            Kepler3DMatrix[0],
            Kepler3DMatrix[4],
            Kepler3DMatrix[7],
            Kepler3DMatrix[10]
        };
        var divisors = new[]
        {
            new[] { 64, 256, 1024 },
            new[] { 64, 256, 1024 },
            new[] { 256, 1024, 4096 },
            new[] { 1024, 4096, 16384 }
        };
        var dopTolerances = new[]
        {
            new[] { 1e-9d, 1e-12d },
            new[] { 1e-12d, 1e-15d }
        };

        for (int ci = 0; ci < cases.Length; ci++)
        {
            Kepler3DCase c = cases[ci];
            double[] pt = ParseKeplerGolden(c.PT);
            double[] vt = ParseKeplerGolden(c.VT);
            double period = 2d * Math.PI * Math.Sqrt(1d / c.Mu);
            Console.WriteLine();
            Console.WriteLine("=== Kepler " + c.Label + " (analytic golden; a=1, mu=2) ===");
            Console.WriteLine("method      h/tol          steps      calls      ms        dp_norm     dv_norm     dp_m        dv_mps");
            Console.WriteLine("scale: 1 length unit = 1 AU, 1 velocity unit = sqrt(2*mu_sun/AU) = "
                + StudyKeplerVelocityScale.ToString("E3", CultureInfo.InvariantCulture) + " m/s");
            StudyNBody nb = KeplerTwoBody(c);
            double bestDpNorm = double.PositiveInfinity;
            double bestDvNorm = double.PositiveInfinity;

            for (int mi = 0; mi < methods.Length; mi++)
            {
                for (int di = 0; di < divisors[ci].Length; di++)
                {
                    double h = period / divisors[ci][di];
                    var r = StudyRunFixedSeconds(nb, methods[mi], h, c.TotalSeconds);
                    double dp = KeplerDp(r.p, pt);
                    double dv = KeplerDv(r.v, vt);
                    double dpM = dp * StudyAU;
                    double dvMps = dv * StudyKeplerVelocityScale;
                    bestDpNorm = Math.Min(bestDpNorm, dp);
                    bestDvNorm = Math.Min(bestDvNorm, dv);
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-11} {1,-13:F6} {2,8}  {3,10}  {4,8:F0}  {5,11:E3}  {6,11:E3}  {7,11:E3}  {8,11:E3}",
                        methods[mi], h, r.steps, r.calls, r.ms, dp, dv, dpM, dvMps));
                    Check(double.IsFinite(dp) && double.IsFinite(dv),
                        "kepler " + c.Label + " " + methods[mi] + " h=" + h.ToString("G3", CultureInfo.InvariantCulture) + " finite",
                        dp.ToString("E2", CultureInfo.InvariantCulture));
                }
            }

            var star = new OrbitingBody
            {
                Name = "Central",
                StandardGravitationalParameter = c.Mu,
                Radius = 1d
            };
            StarSystem one = new StarSystem(star);
            var p0 = new Vector3d(c.P0[0], c.P0[1], c.P0[2]);
            var v0 = new Vector3d(c.V0[0], c.V0[1], c.V0[2]);
            for (int ti = 0; ti < dopTolerances.Length; ti++)
            {
                double posTol = dopTolerances[ti][0];
                double velTol = dopTolerances[ti][1];
                var d = StudyShipDop853(
                    one, 0, p0, v0, 0d, c.TotalSeconds / 86400d, posTol, velTol, posTol);
                double dp = (d.p - new Vector3d(pt[0], pt[1], pt[2])).Magnitude;
                double dv = (d.v - new Vector3d(vt[0], vt[1], vt[2])).Magnitude;
                double dpM = dp * StudyAU;
                double dvMps = dv * StudyKeplerVelocityScale;
                bestDpNorm = Math.Min(bestDpNorm, dp);
                bestDvNorm = Math.Min(bestDvNorm, dv);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-11} {1,-13} {2,8}  {3,10}  {4,8:F0}  {5,11:E3}  {6,11:E3}  {7,11:E3}  {8,11:E3}",
                    "DOP853", "tol=" + posTol.ToString("G0", CultureInfo.InvariantCulture),
                    "-", d.calls, d.ms, dp, dv, dpM, dvMps));
                Check(double.IsFinite(dp) && double.IsFinite(dv),
                    "kepler " + c.Label + " DOP853 tol=" + posTol.ToString("G0", CultureInfo.InvariantCulture) + " finite",
                    dp.ToString("E2", CultureInfo.InvariantCulture));
            }

            Check(bestDpNorm < 1e-6d, "kepler " + c.Label + " best normalized dp",
                bestDpNorm.ToString("E3", CultureInfo.InvariantCulture));
            Check(bestDvNorm < 1e-6d, "kepler " + c.Label + " best normalized dv",
                bestDvNorm.ToString("E3", CultureInfo.InvariantCulture));
        }

        Console.WriteLine();
        Console.WriteLine("--- Solar 7-body vs independent dd-Taylor reference ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody realNb = StudyMakeNBody(real);
        double e0Energy = StudyEnergy(realNb.P0, realNb.V0, realNb.Mu);
        var stepChoices = new Dictionary<string, double[]>
        {
            { "RK4", new[] { 900d, 300d, 60d } },
            { "DOPRI5", new[] { 900d, 300d, 60d } },
            { "PEFRL", new[] { 900d, 300d, 60d } },
            { "Y6", new[] { 7200d, 3600d, 900d } },
            { "Y6C", new[] { 7200d, 3600d, 900d } }
        };

        int realOuter = StudyOuterIndex(realNb);
        double[] realMom0 = TotalMomentum(realNb.V0, realNb.Mu);
        double[] realAng0 = TotalAngular(realNb.P0, realNb.V0, realNb.Mu);
        double realSumMuV = 0d;
        for (int i = 0; i < realNb.Count; i++)
        {
            realSumMuV += realNb.Mu[i] * Math.Sqrt(
                (realNb.V0[3 * i] * realNb.V0[3 * i])
                + (realNb.V0[(3 * i) + 1] * realNb.V0[(3 * i) + 1])
                + (realNb.V0[(3 * i) + 2] * realNb.V0[(3 * i) + 2]));
        }

        foreach (double years in new[] { 1d, 10d })
        {
            double sampleEvery = 30d * 86400d;
            StudyRefResult reference = StudyPlanetIndependentReference(realNb, years, sampleEvery);
            StudyRefResult alternative = StudyPlanetIndependentReferenceAlt(realNb, years, sampleEvery);
            double referenceSelf = 0d;
            for (int i = 0; i < realNb.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    referenceSelf = Math.Max(referenceSelf, StudyRel(reference.P, i, j, alternative.P));
                    referenceSelf = Math.Max(referenceSelf, StudyPlanetMaxRel(reference.Samples, alternative.Samples, i, j));
                }
            }

            Console.WriteLine();
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "=== Solar 7-body, {0} y: independent {1} reference, self = {2:E2} m, {3:F0} ms; outer={4} ===",
                years, "dd-Taylor K12",
                referenceSelf, reference.Ms, realNb.Names[realOuter]));
            Console.WriteLine("method      h(s)      steps      calls      ms       inner(m)    mid(m)      outer(m)    maxInner    maxOuter    dE/E      maxdE     inv");
            double bestMaxInner = double.PositiveInfinity;
            double bestMaxOuter = double.PositiveInfinity;
            double bestMaxDe = double.PositiveInfinity;
            double bestInv = double.PositiveInfinity;
            string bestLabel = "none";
            for (int mi = 0; mi < methods.Length; mi++)
            {
                double[] stepsChoices = stepChoices[methods[mi]];
                for (int di = 0; di < stepsChoices.Length; di++)
                {
                    double h = stepsChoices[di];
                    StudyRefResult r = StudyRefRunSampled(realNb, methods[mi], h, years, sampleEvery);
                    double inner = StudyRel(r.P, 2, 1, reference.P);
                    double mid = StudyRel(r.P, 1, 0, reference.P);
                    double outer = StudyRel(r.P, realOuter, 0, reference.P);
                    double maxInner = StudyPlanetMaxRel(r.Samples, reference.Samples, 2, 1);
                    double maxOuter = StudyPlanetMaxRel(r.Samples, reference.Samples, realOuter, 0);
                    double dE = Math.Abs((StudyEnergy(r.P, r.V, realNb.Mu) - e0Energy) / e0Energy);
                    double maxDe = StudyPlanetMaxEnergyError(r.Samples, realNb.Mu, e0Energy);
                    double inv = StudyPlanetMaxInvariantError(
                        r.Samples, realNb.Mu, realMom0, realAng0, realSumMuV);
                    long steps = (long)Math.Floor((years * StudyYear) / h);
                    if (maxInner < bestMaxInner)
                    {
                        bestMaxInner = maxInner;
                        bestMaxOuter = maxOuter;
                        bestMaxDe = maxDe;
                        bestInv = inv;
                        bestLabel = methods[mi] + " h=" + h.ToString("F0", CultureInfo.InvariantCulture);
                    }

                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,-11} {1,7:F0}  {2,8}  {3,10}  {4,8:F0}  {5,10:E2}  {6,10:E2}  {7,10:E2}  {8,10:E2}  {9,10:E2}  {10,9:E1}  {11,9:E1}  {12,9:E1}",
                        methods[mi], h, steps, r.Calls, r.Ms, inner, mid, outer,
                        maxInner, maxOuter, dE, maxDe, inv));
                    Check(double.IsFinite(inner) && double.IsFinite(dE),
                        "nbody " + years + "y " + methods[mi] + " h=" + h.ToString("F0", CultureInfo.InvariantCulture) + " finite",
                        inner.ToString("E2", CultureInfo.InvariantCulture));
                }
            }

            Console.WriteLine("best by max inner: " + bestLabel);
            Check(referenceSelf < (years <= 20d ? 10d : 1000d),
                "nbody " + years + "y independent reference self",
                referenceSelf.ToString("E3", CultureInfo.InvariantCulture));
            Check(bestMaxInner < (years <= 20d ? 10d : 2000d),
                "nbody " + years + "y best max inner",
                bestMaxInner.ToString("E3", CultureInfo.InvariantCulture));
            Check(bestMaxOuter < (years <= 20d ? 100d : 1000d),
                "nbody " + years + "y best max outer",
                bestMaxOuter.ToString("E3", CultureInfo.InvariantCulture));
            Check(bestMaxDe < 1e-9d, "nbody " + years + "y best max dE/E",
                bestMaxDe.ToString("E2", CultureInfo.InvariantCulture));
            Check(bestInv < 1e-9d, "nbody " + years + "y best invariant",
                bestInv.ToString("E2", CultureInfo.InvariantCulture));
        }

        Console.WriteLine();
        Console.WriteLine("--- Ship 10-year reference: dd-Taylor K28 (golden_real_10y_k28.json) ---");
        string studyRoot = Path.GetFullPath(Path.Combine(StudyShipMatrixResultsDir(), "..", ".."));
        string shipGoldenPath = Path.Combine(studyRoot, "taylor", "golden", "golden_real_10y_k28.json");
        if (!File.Exists(shipGoldenPath))
        {
            Console.WriteLine("missing " + shipGoldenPath + "; run Test529_TenYearDdGolden first");
        }
        else
        {
            TenYearDdGoldenFile shipGolden = JsonSerializer.Deserialize<TenYearDdGoldenFile>(
                File.ReadAllText(shipGoldenPath));
            GoldCp final = shipGolden.Checkpoints[shipGolden.Checkpoints.Length - 1];
            Console.WriteLine("golden: " + shipGolden.Method + " K" + shipGolden.Order
                + ", last checkpoint " + final.Days.ToString("F0", CultureInfo.InvariantCulture) + " d");

            string partialPath = Path.Combine(studyRoot, "taylor", "golden", "golden_real_10y_k24.partial.json");
            if (File.Exists(partialPath))
            {
                GoldFile partial = JsonSerializer.Deserialize<GoldFile>(File.ReadAllText(partialPath));
                GoldCp dd365 = null;
                GoldCp mp365 = null;
                foreach (GoldCp cp in shipGolden.Checkpoints)
                {
                    if (Math.Abs(cp.Days - 365d) < 1e-9d)
                    {
                        dd365 = cp;
                    }
                }

                foreach (GoldCp cp in partial.Checkpoints)
                {
                    if (Math.Abs(cp.Days - 365d) < 1e-9d)
                    {
                        mp365 = cp;
                    }
                }

                if (dd365 != null && mp365 != null)
                {
                    double anchor = GoldStringDiff(dd365.P, mp365.P);
                    Console.WriteLine("chain: dd K28 @365 d vs mp-golden = "
                        + anchor.ToString("E3", CultureInfo.InvariantCulture) + " m");
                    Check(anchor < 1e-6d, "ship golden 1y vs mp",
                        anchor.ToString("E3", CultureInfo.InvariantCulture));
                }
            }

            StarSystem shipSys = StudyRealSolar7();
            StudyNBody shipNb = StudyMakeNBody(shipSys);
            StudyBake("Integrator ship", shipSys, shipNb, 10.1d, 20d);
            OrbitingBody shipBody = shipSys.AllBodies[1];
            double shipMu = shipBody.ResolveStandardGravitationalParameter();
            double shipT0 = shipGolden.T0;
            StudyShipOrbit(6.771e6d, 4.6371e7d, shipMu, out Vector3d srelP, out Vector3d srelV);
            double shipDays = final.Days;

            Console.WriteLine("method       step/tol    dp(rel)     dv(rel)     calls      ms");
            double bestShipDp = double.PositiveInfinity;
            double bestShipDv = double.PositiveInfinity;
            void ShipRow(string method, double step, Vector3d p, Vector3d v, long calls, double ms)
            {
                double dp = LocalGoldErr(p, final.RelP);
                double dv = LocalGoldErr(v, final.RelV);
                bestShipDp = Math.Min(bestShipDp, dp);
                bestShipDv = Math.Min(bestShipDv, dv);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-12} {1,-10:G4} {2,12:E3} {3,12:E3} {4,10} {5,9:F0}",
                    method, step, dp, dv,
                    calls < 0L ? "-" : calls.ToString("N0", CultureInfo.InvariantCulture), ms));
                Check(double.IsFinite(dp) && double.IsFinite(dv),
                    "ship " + method + " finite", dp.ToString("E2", CultureInfo.InvariantCulture));
            }

            StarSystem MakeClone()
            {
                StarSystem clone = StudyRealSolar7();
                for (int i = 0; i < clone.AllBodies.Count; i++)
                {
                    clone.AllBodies[i].Baked = shipSys.AllBodies[i].Baked;
                }

                clone.InvalidatePositionCache();
                return clone;
            }

            var shipRuns = new (string Method, double Step, Vector3d P, Vector3d V, long Calls, double Ms)[7];
            Parallel.Invoke(
                () =>
                {
                    var sw = Stopwatch.StartNew();
                    var r = StudyShipRefLocal(MakeClone(), 1, srelP, srelV, shipT0, shipDays, 112.5d);
                    sw.Stop();
                    shipRuns[0] = ("Y6C local", 112.5d, r.p, r.v, -1L, sw.Elapsed.TotalMilliseconds);
                },
                () =>
                {
                    var sw = Stopwatch.StartNew();
                    var r = StudyShipRefLocal(MakeClone(), 1, srelP, srelV, shipT0, shipDays, 56.25d);
                    sw.Stop();
                    shipRuns[1] = ("Y6C local", 56.25d, r.p, r.v, -1L, sw.Elapsed.TotalMilliseconds);
                },
                () =>
                {
                    var r = StudyShipFixedLocal(MakeClone(), 1, srelP, srelV, shipT0, shipDays, 300d, "Y6");
                    shipRuns[2] = ("Y6 local", 300d, r.p, r.v, r.calls, r.ms);
                },
                () =>
                {
                    var r = StudyShipFixedLocal(MakeClone(), 1, srelP, srelV, shipT0, shipDays, 60d, "Y6");
                    shipRuns[3] = ("Y6 local", 60d, r.p, r.v, r.calls, r.ms);
                },
                () =>
                {
                    var r = StudyShipFixedLocal(MakeClone(), 1, srelP, srelV, shipT0, shipDays, 300d, "PEFRL");
                    shipRuns[4] = ("PEFRL local", 300d, r.p, r.v, r.calls, r.ms);
                },
                () =>
                {
                    var r = StudyShipDop853Local(
                        MakeClone(), 1, srelP, srelV, shipT0, shipDays, 1e-9d, 1e-12d, 1e-12d);
                    shipRuns[5] = ("DOP853 local", 1e-9d, r.p, r.v, r.calls, r.ms);
                },
                () =>
                {
                    var r = StudyShipDop853Local(
                        MakeClone(), 1, srelP, srelV, shipT0, shipDays, 1e-12d, 1e-15d, 1e-15d);
                    shipRuns[6] = ("DOP853 local", 1e-12d, r.p, r.v, r.calls, r.ms);
                });

            foreach (var run in shipRuns)
            {
                ShipRow(run.Method, run.Step, run.P, run.V, run.Calls, run.Ms);
            }

            Check(bestShipDp < 1000d, "ship best dp vs dd-Taylor golden",
                bestShipDp.ToString("E3", CultureInfo.InvariantCulture));
            Check(bestShipDv < 1d, "ship best dv vs dd-Taylor golden",
                bestShipDv.ToString("E3", CultureInfo.InvariantCulture));
        }

        Console.WriteLine();
        Console.WriteLine("failed checks: " + (failures - failuresBefore));
        Console.WriteLine("--- Integrator benchmark complete ---");
        return 0;
    }
}
