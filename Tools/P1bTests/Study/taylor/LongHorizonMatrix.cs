using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Universe;

internal static partial class P1bTests
{
    private sealed class MatrixRun
    {
        public StudyDd[] P;
        public StudyDd[] V;
        public long Steps;
    }

    private static int Test521_LongHorizonMatrix()
    {
        Console.WriteLine("--- Long-horizon matrix: 16 scenarios x default/tight dd, 90 d ---");
        StarSystem sys = StudyRealSolar7();
        StudyNBody nb = StudyMakeNBody(sys);
        StudyBake("Long matrix", sys, nb, 1.1d, 1d);

        int[] central = { 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 6, 6, 6, 6 };
        double[] rp =
        {
            6.771e6d, 7e6d, 1e7d, 2e7d,
            1.9e6d, 2.5e6d, 5e6d, 1e7d,
            1e8d, 2e8d, 5e8d, 1e9d,
            4e6d, 5e6d, 1e7d, 5e7d
        };
        double[] ra =
        {
            4.6371e7d, 2e7d, 5e7d, 1e8d,
            1e7d, 3e7d, 5e7d, 5e7d,
            1e9d, 5e9d, 1e10d, 2e10d,
            1e8d, 5e8d, 5e8d, 8e8d
        };

        double t0 = StudyShipT0;
        double total = 91.3125d * 86400d;
        var runs = new MatrixRun[central.Length * 2];
        var watch = Stopwatch.StartNew();
        Parallel.For(
            0,
            runs.Length,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            job =>
            {
                int index = job / 2;
                OrbitingBody body = sys.AllBodies[central[index]];
                double mu = body.ResolveStandardGravitationalParameter();
                sys.EvaluateBodyState(body, t0, out Vector3d bp, out Vector3d bv);
                StudyShipOrbit(rp[index], ra[index], mu, out Vector3d relP, out Vector3d relV);
                var p0 = bp + relP;
                var v0 = bv + relV;
                bool tight = (job & 1) != 0;
                TaylorShipDdResult result = tight
                    ? TaylorShip.RunDdSeconds(
                        sys, p0, v0, t0, total, 24, 1e-12d, 1e-15d, 1800d, 1e-3d)
                    : TaylorShip.RunDdSeconds(
                        sys, p0, v0, t0, total, 24, 1e-9d, 1e-12d, 3600d, 1e-3d);
                runs[job] = new MatrixRun
                {
                    P = new[] { result.DdPx, result.DdPy, result.DdPz },
                    V = new[] { result.DdVx, result.DdVy, result.DdVz },
                    Steps = result.Steps
                };
            });
        watch.Stop();

        Console.WriteLine(
            "note: this matrix is a stress/self-convergence test, not an absolute golden; " +
            "rows marked hmax-limited are not accuracy evidence.");
        long defaultHmaxSteps = (long)Math.Ceiling(total / 3600d);
        long tightHmaxSteps = (long)Math.Ceiling(total / 1800d);
        int failuresBefore = failures;
        var rows = new List<string>();
        for (int i = 0; i < central.Length; i++)
        {
            MatrixRun def = runs[2 * i];
            MatrixRun tight = runs[(2 * i) + 1];
            double dp = DdNorm(def.P, tight.P);
            double dv = DdNorm(def.V, tight.V);
            double vcirc = Math.Sqrt(
                sys.AllBodies[central[i]].ResolveStandardGravitationalParameter() / ra[i]);
            double dpGate = (1e-5d * ra[i]) + 1d;
            double dvGate = (1e-5d * vcirc) + 1e-6d;
            bool hmaxLimited = def.Steps <= defaultHmaxSteps + 2L
                || tight.Steps <= tightHmaxSteps + 2L;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "scenario {0,2}: body={1,-7} rp={2:E2} ra={3:E2} steps={4}/{5} dp={6:E2} m dv={7:E2} m/s{8}",
                i, sys.AllBodies[central[i]].Name, rp[i], ra[i],
                def.Steps, tight.Steps, dp, dv, hmaxLimited ? " hmax-limited" : ""));
            rows.Add(StudyCsvRow(
                "scenario " + i.ToString(CultureInfo.InvariantCulture) + " " + sys.AllBodies[central[i]].Name,
                dp, dv, def.Steps, tight.Steps, hmaxLimited));
            Check(double.IsFinite(dp) && double.IsFinite(dv),
                "matrix " + i + " finite", "dp/dv");
            if (!hmaxLimited)
            {
                Check(dp < dpGate, "matrix " + i + " dp", dp.ToString("E2", CultureInfo.InvariantCulture));
                Check(dv < dvGate, "matrix " + i + " dv", dv.ToString("E2", CultureInfo.InvariantCulture));
            }
        }

        StudyWriteCsv("Test521_LongHorizonMatrix.csv",
            "case,dp_m,dv_mps,default_steps,tight_steps,hmax_limited", rows);

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "long matrix wall={0} ms, failed checks={1}",
            watch.ElapsedMilliseconds, failures - failuresBefore));
        Console.WriteLine("--- Long-horizon matrix complete ---");
        return 0;
    }
}

