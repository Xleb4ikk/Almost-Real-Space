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

    private static List<StudySample> StudyPlanetTaylorSamples(List<TaylorSample> samples)
    {
        if (samples == null)
        {
            return null;
        }

        var converted = new List<StudySample>(samples.Count);
        for (int i = 0; i < samples.Count; i++)
        {
            converted.Add(new StudySample(samples[i].T, samples[i].P, samples[i].V));
        }

        return converted;
    }

    private static StudyRefResult StudyPlanetIndependentReference(
        StudyNBody nb, double years, double sampleEvery)
    {
        var samples = sampleEvery > 0d ? new List<StudySample>() : null;
        var dd = RunTaylorNBodyDd(
            ToTaylor(nb), years, 12, 1e-9d, 1e-12d, 43200d, 1e-6d,
            sampleEvery, samples);
        return new StudyRefResult
        {
            P = dd.P,
            V = dd.V,
            Samples = samples,
            Calls = dd.Calls,
            Ms = dd.Ms
        };
    }

    private static StudyRefResult StudyPlanetIndependentReferenceAlt(
        StudyNBody nb, double years, double sampleEvery)
    {
        var samples = sampleEvery > 0d ? new List<StudySample>() : null;
        var dd = RunTaylorNBodyDd(
            ToTaylor(nb), years, 12, 1e-9d, 1e-12d, 21600d, 1e-6d,
            sampleEvery, samples);
        return new StudyRefResult
        {
            P = dd.P,
            V = dd.V,
            Samples = samples,
            Calls = dd.Calls,
            Ms = dd.Ms
        };
    }

    private static double StudyPlanetMaxRel(
        List<StudySample> candidate, List<StudySample> reference, int i, int j)
    {
        if (candidate == null || reference == null)
        {
            return double.NaN;
        }

        int count = Math.Min(candidate.Count, reference.Count);
        double worst = 0d;
        for (int s = 0; s < count; s++)
        {
            worst = Math.Max(worst, StudyRel(candidate[s].P, i, j, reference[s].P));
        }

        return worst;
    }

    private static double StudyPlanetMaxEnergyError(
        List<StudySample> samples, double[] mu, double e0)
    {
        if (samples == null || samples.Count == 0)
        {
            return double.NaN;
        }

        double worst = 0d;
        for (int s = 0; s < samples.Count; s++)
        {
            if (samples[s].V == null)
            {
                continue;
            }

            double e = StudyEnergy(samples[s].P, samples[s].V, mu);
            worst = Math.Max(worst, Math.Abs((e - e0) / e0));
        }

        return worst;
    }

    private static double StudyPlanetMaxInvariantError(
        List<StudySample> samples, double[] mu, double[] mom0, double[] ang0, double sumMuV)
    {
        if (samples == null || samples.Count == 0)
        {
            return double.NaN;
        }

        double worstP = 0d;
        double worstL = 0d;
        for (int s = 0; s < samples.Count; s++)
        {
            if (samples[s].V == null)
            {
                continue;
            }

            double[] mom = TotalMomentum(samples[s].V, mu);
            double[] ang = TotalAngular(samples[s].P, samples[s].V, mu);
            worstP = Math.Max(worstP,
                Magnitude3(Sub3(mom, mom0)) / Math.Max(sumMuV, 1e-300d));
            worstL = Math.Max(worstL,
                Magnitude3(Sub3(ang, ang0)) / Math.Max(Magnitude3(ang0), 1e-300d));
        }

        return Math.Max(worstP, worstL);
    }

    private static void StudyPlanetTable(
        string label, StudyNBody nb, double years, double refH, bool convergence)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + label + ": " + years + " y, independent "
            + "dd-Taylor K12 reference ===");
        double sampleEvery = 30d * 86400d;
        StudyRefResult reference = StudyPlanetIndependentReference(nb, years, sampleEvery);
        StudyRefResult alternative = StudyPlanetIndependentReferenceAlt(nb, years, sampleEvery);
        double referenceSelf = 0d;
        for (int i = 0; i < nb.Count; i++)
        {
            for (int j = 0; j < i; j++)
            {
                referenceSelf = Math.Max(referenceSelf, StudyRel(reference.P, i, j, alternative.P));
                referenceSelf = Math.Max(referenceSelf, StudyPlanetMaxRel(reference.Samples, alternative.Samples, i, j));
            }
        }

        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "reference calls={0:N0}, {1:F0} ms; self-convergence={2:E3} m",
            reference.Calls, reference.Ms, referenceSelf));
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

        int outerIndex = StudyOuterIndex(nb);
        Console.WriteLine("outer body: " + nb.Names[outerIndex]);
        Console.WriteLine("{" + "system,-24} {" + "method,-8} {" + "h, s,6} {" + "calls,12} {" +
            "inner, m,11} {" + "mid, m,11} {" + "outer, m,11} {" + "maxInner, m,11} {" +
            "maxOuter, m,11} {" + "dE/E,9} {" + "maxdE,9} {" + "inv,9} {" + "ms,8}");
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
        var rowResults = new (double inner, double mid, double outer, double maxInner,
            double maxOuter, double de, double maxDe, double inv, long calls, double ms)[rows.Length];
        Parallel.For(0, rows.Length, idx =>
        {
            (string method, double h) = rows[idx];
            StudyRefResult r = StudyRefRunSampled(nb, method, h, years, sampleEvery);
            double e = StudyEnergy(r.P, r.V, nb.Mu);
            double de = Math.Abs((e - e0) / e0);
            double inner = StudyRel(r.P, 2, 1, reference.P);
            double mid = StudyRel(r.P, 1, 0, reference.P);
            double outer = StudyRel(r.P, outerIndex, 0, reference.P);
            double maxInner = StudyPlanetMaxRel(r.Samples, reference.Samples, 2, 1);
            double maxOuter = StudyPlanetMaxRel(r.Samples, reference.Samples, outerIndex, 0);
            double maxDe = StudyPlanetMaxEnergyError(r.Samples, nb.Mu, e0);
            double inv = StudyPlanetMaxInvariantError(r.Samples, nb.Mu, mom0, ang0, sumMuV);
            rowResults[idx] = (inner, mid, outer, maxInner, maxOuter, de, maxDe, inv, r.Calls, r.Ms);
        });

        double bestScore = double.PositiveInfinity;
        int bestIndex = -1;
        for (int idx = 0; idx < rows.Length; idx++)
        {
            (string method, double h) = rows[idx];
            var row = rowResults[idx];
            double score = double.IsFinite(row.maxInner) ? row.maxInner : row.inner;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = idx;
            }

            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,-24} {1,-8} {2,6:F0} {3,12:N0} {4,11:E3} {5,11:E3} {6,11:E3} {7,11:E3} {8,11:E3} {9,9:E2} {10,9:E2} {11,9:E2} {12,8:F0}",
                label, method, h, row.calls, row.inner, row.mid, row.outer,
                row.maxInner, row.maxOuter, row.de, row.maxDe, row.inv, row.ms));
        }

        if (bestIndex >= 0)
        {
            (string method, double h) = rows[bestIndex];
            var best = rowResults[bestIndex];
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "best: {0} h={1:F0} maxInner={2:E3} m maxOuter={3:E3} m maxdE={4:E2} inv={5:E2}",
                method, h, best.maxInner, best.maxOuter, best.maxDe, best.inv));
        }

        double innerGate = years <= 20d ? 10d : 5000d;
        double outerGate = years <= 20d ? 100d : 2000d;
        double energyGate = years <= 20d ? 1e-9d : 1e-7d;
        double invariantGate = 1e-9d;
        Check(referenceSelf < innerGate, label + " independent reference self-convergence",
            referenceSelf.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
        if (bestIndex >= 0)
        {
            var best = rowResults[bestIndex];
            Check(best.maxInner < innerGate, label + " best max inner",
                best.maxInner.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
            Check(best.maxOuter < outerGate, label + " best max outer",
                best.maxOuter.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
            Check(best.maxDe < energyGate, label + " best max dE/E",
                best.maxDe.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
            Check(best.inv < invariantGate, label + " best max invariant",
                best.inv.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            Check(false, label + " best row", "none");
        }
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

