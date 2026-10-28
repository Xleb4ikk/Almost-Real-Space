using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{    // ------------------------------------------------------------------
    // System table
    // ------------------------------------------------------------------

    private static void StudySystemTable(string label, StarSystem sys)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + label + " ===");
        Console.WriteLine(
            "{" + "body,-8} {" + "parent,-8} {" + "mu, m3/s2,12} {" + "R, m,10} {" + "a, m,12} {" +
            "e,6} {" + "T, d,10} {" + "Hill, m,11} {" + "seg, d,7} {" + "g, m/s2,8} {" + "rho, g/cm3,9}");
        double fastest = double.PositiveInfinity;
        foreach (OrbitingBody body in sys.AllBodies)
        {
            double mu = body.ResolveStandardGravitationalParameter();
            double g = mu / (body.Radius * body.Radius);
            double volume = (4d / 3d) * Math.PI * body.Radius * body.Radius * body.Radius;
            double density = mu / PhysicsSolver.GravitationalConstant / volume / 1000d;
            double period = 0d;
            if (body.Parent != null)
            {
                double muLocal = body.Parent.ResolveStandardGravitationalParameter()
                    + body.ResolveSubtreeStandardGravitationalParameter();
                double a3 = body.SemiMajorAxis * body.SemiMajorAxis * body.SemiMajorAxis;
                period = 2d * Math.PI * Math.Sqrt(a3 / muLocal);
                if (period < fastest)
                {
                    fastest = period;
                }
            }

            double seg = EphemerisBaker.DefaultSegmentLengthSeconds(body);
            Console.WriteLine(
                "{0,-8} {1,-8} {2,12:E3} {3,10:E3} {4,12:E3} {5,6:F3} {6,10:F2} {7,11:E3} {8,7:F2} {9,8:F2} {10,9:F2}",
                body.Name,
                body.Parent == null ? "-" : body.Parent.Name,
                mu,
                body.Radius,
                body.SemiMajorAxis,
                body.Eccentricity,
                period / 86400d,
                body.HillSphereRadius,
                seg / 86400d,
                g,
                density);
        }

        double phaseStep = fastest * 0.02d / (2d * Math.PI);
        double bakeStep = Math.Min(900d, phaseStep);
        Console.WriteLine("fastest orbit = {0:F2} d; phase budget step = {1:F0} s; bake step = {2:F0} s",
            fastest / 86400d, phaseStep, bakeStep);
    }

    // ------------------------------------------------------------------
    // Planet table
    // ------------------------------------------------------------------

    private static void StudyPlanetTable(
        string label, StudyNBody nb, double years, double refH, bool convergence)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + label + ": " + years + " y, reference Y6C h=" + refH + " s ===");
        var reference = StudyRefRun(nb, "Y6C", refH, years, 0d);
        if (convergence)
        {
            var half = StudyRefRun(nb, "Y6C", refH * 0.5d, years, 0d);
            double worst = 0d;
            for (int i = 0; i < nb.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    worst = Math.Max(worst, StudyRel(reference.P, i, j, half.P));
                }
            }

            Console.WriteLine("reference self-convergence (h vs h/2): worst pair = {0:E3} m", worst);
        }

        double e0 = StudyEnergy(nb.P0, nb.V0, nb.Mu);
        Console.WriteLine("{" + "system,-24} {" + "method,-8} {" + "h, s,6} {" + "calls,12} {" +
            "Luna/Terra, m,14} {" + "Terra/Sol, m,13} {" + "outer, m,12} {" + "dE/E,10} {" + "ms,9}");
        var rows = new[]
        {
            ("RK4", 900d),
            ("RK4", 3600d),
            ("DOPRI5", 3600d),
            ("PEFRL", 3600d),
            ("PEFRL", 7200d),
            ("Y6", 1800d),
            ("Y6", 3600d),
            ("Y6", 7200d)
        };
        var rowResults = new (double inner, double mid, double outer, double de, long calls, double ms)[rows.Length];
        Parallel.For(0, rows.Length, idx =>
        {
            (string method, double h) = rows[idx];
            var r = StudyRefRun(nb, method, h, years, 0d);
            double e = StudyEnergy(r.P, r.V, nb.Mu);
            double de = Math.Abs((e - e0) / e0);
            double inner = StudyRel(r.P, 2, 1, reference.P);
            double mid = StudyRel(r.P, 1, 0, reference.P);
            double outer = StudyRel(r.P, nb.Count - 1, 0, reference.P);
            rowResults[idx] = (inner, mid, outer, de, r.Calls, r.Ms);
        });
        for (int idx = 0; idx < rows.Length; idx++)
        {
            (string method, double h) = rows[idx];
            var row = rowResults[idx];
            Console.WriteLine(
                "{0,-24} {1,-8} {2,6:F0} {3,12:N0} {4,14:E3} {5,13:E3} {6,12:E3} {7,10:E2} {8,9:F0}",
                label, method, h, row.calls, row.inner, row.mid, row.outer, row.de, row.ms);
        }

        Console.WriteLine("reference: calls={0:N0}, {1:F0} ms", reference.Calls, reference.Ms);
    }

    private static void StudyBake(
        string label, StarSystem sys, StudyNBody nb, double years, double worstGateMeters = 0d)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + label + ": bake " + years + " y, RK4 900 s, degree 12, quant 2 m ===");
        var config = new BakeConfig { Degree = 12, MaxStepSeconds = 900d, PhasePerStep = 0.02d, QuantBudgetMeters = 2d };
        var sw = Stopwatch.StartNew();
        Dictionary<OrbitingBody, BakedEphemeris> eph = EphemerisBaker.Bake(sys, years, config);
        sw.Stop();
        double bakeMs = sw.Elapsed.TotalMilliseconds;
        foreach (var kv in eph)
        {
            kv.Key.Baked = kv.Value;
        }

        sys.InvalidatePositionCache();

        // EphemerisBaker counts a year as 365 days; StudyYear is 365.25.
        double bakeYears = years * 365d / 365.25d;
        var refSamples = StudyRefRun(nb, "Y6C", 112.5d, bakeYears, 30d * 86400d);
        double worst = 0d;
        var perBody = new double[nb.Count];
        for (int s = 0; s < refSamples.Samples.Count; s++)
        {
            StudySample sample = refSamples.Samples[s];
            double[] rp = sample.P;
            sys.EvaluateBodyState(sys.AllBodies[0], sample.T, out Vector3d rootP, out _);
            for (int i = 1; i < nb.Count; i++)
            {
                sys.EvaluateBodyState(sys.AllBodies[i], sample.T, out Vector3d p, out _);
                double dx = (p.X - rootP.X) - (rp[3 * i] - rp[0]);
                double dy = (p.Y - rootP.Y) - (rp[(3 * i) + 1] - rp[1]);
                double dz = (p.Z - rootP.Z) - (rp[(3 * i) + 2] - rp[2]);
                double err = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                if (err > perBody[i])
                {
                    perBody[i] = err;
                }

                if (err > worst)
                {
                    worst = err;
                }
            }
        }

        long segs = 0L;
        foreach (var kv in eph)
        {
            segs += kv.Value.SegmentCount;
        }

        Console.WriteLine("bake: {0:F0} ms, segments={1:N0}, worst position error vs Y6C = {2:E3} m", bakeMs, segs, worst);
        Console.Write("per body:");
        for (int i = 1; i < nb.Count; i++)
        {
            Console.Write(" {0}={1:E2} m", nb.Names[i], perBody[i]);
        }

        Console.WriteLine();

        if (worstGateMeters > 0d)
        {
            Check(worst < worstGateMeters, label + " bake worst vs Y6C",
                worst.ToString("E3", CultureInfo.InvariantCulture) + " < "
                + worstGateMeters.ToString("G3", CultureInfo.InvariantCulture) + " m");
        }

        string dir = Path.Combine(Path.GetTempPath(), "study_bake_" + Guid.NewGuid().ToString("N"));
        try
        {
            StarSystem fileSys = label.IndexOf("Custom", StringComparison.OrdinalIgnoreCase) >= 0 ? StudyCustomSystem() : StudyRealSolar7();
            var sw2 = Stopwatch.StartNew();
            EphemerisBaker.BakeToFiles(fileSys, dir, years, config, null, null, null, true);
            sw2.Stop();
            EphemerisBaker.AttachFiles(fileSys, dir);
            long bytes = 0L;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                bytes += new FileInfo(f).Length;
            }

            double worstQ = 0d;
            for (int s = 0; s < refSamples.Samples.Count; s++)
            {
                StudySample sample = refSamples.Samples[s];
                double[] rp = sample.P;
                fileSys.EvaluateBodyState(fileSys.AllBodies[0], sample.T, out Vector3d rootP, out _);
                for (int i = 1; i < nb.Count; i++)
                {
                    fileSys.EvaluateBodyState(fileSys.AllBodies[i], sample.T, out Vector3d p, out _);
                    double dx = (p.X - rootP.X) - (rp[3 * i] - rp[0]);
                    double dy = (p.Y - rootP.Y) - (rp[(3 * i) + 1] - rp[1]);
                    double dz = (p.Z - rootP.Z) - (rp[(3 * i) + 2] - rp[2]);
                    worstQ = Math.Max(worstQ, Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
                }
            }

            Console.WriteLine("compressed files: {0:F0} ms, {1:N0} bytes; worst vs Y6C = {2:E3} m",
                sw2.Elapsed.TotalMilliseconds, bytes, worstQ);
            if (worstGateMeters > 0d)
            {
                double compressedGate = Math.Max(worstGateMeters, 0.5d);
                Check(worstQ < compressedGate, label + " compressed bake vs Y6C",
                    worstQ.ToString("E3", CultureInfo.InvariantCulture) + " < "
                    + compressedGate.ToString("G3", CultureInfo.InvariantCulture) + " m");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException)
            {
            }
        }

        Console.WriteLine("bake step (actual formula): {0:F0} s", StudyBakeStep(sys));
    }

    private static double StudyBakeStep(StarSystem sys)
    {
        double fastest = double.PositiveInfinity;
        foreach (OrbitingBody body in sys.AllBodies)
        {
            if (body.Parent == null)
            {
                continue;
            }

            double muLocal = body.Parent.ResolveStandardGravitationalParameter()
                + body.ResolveSubtreeStandardGravitationalParameter();
            double a3 = body.SemiMajorAxis * body.SemiMajorAxis * body.SemiMajorAxis;
            double period = 2d * Math.PI * Math.Sqrt(a3 / muLocal);
            if (period < fastest)
            {
                fastest = period;
            }
        }

        return Math.Min(900d, fastest * 0.02d / (2d * Math.PI));
    }

}

