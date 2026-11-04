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

    /// <summary>
    /// Adaptive DOP853/DOPRI5 for the mutual-gravity planet table. Fixed-step
    /// rows alone cannot answer "when is an adaptive method better": the table
    /// needs the same kind of row for production DOP853. Samples land exactly on
    /// the shared 30-day checkpoints, so the ranking stays comparable.
    /// </summary>
    private static StudyRefResult StudyRefRunAdaptive(
        StudyNBody nb, bool dop853, double posAtol, double velAtol, double rtol,
        double years, double sampleEvery)
    {
        int n = nb.Count;
        int length = 3 * n;
        var p = (double[])nb.P0.Clone();
        var v = (double[])nb.V0.Clone();
        var a = new double[length];
        var asStage = new double[length];
        var pStage = new double[length];
        var vStage = new double[length];
        var pNew = new double[length];
        var vNew = new double[length];

        double[][] A;
        double[] B;
        double[] E;
        int stages;
        int order;
        if (dop853)
        {
            A = StudyDop853A;
            B = StudyDop853B;
            E = StudyDop853E5;
            stages = 12;
            order = 8;
        }
        else
        {
            // DOPRI5 rows are indexed by target stage 1..6; prepend the empty
            // stage 0 row so the loop below can index A[s] like DOP853.
            A = new double[7][];
            A[0] = Array.Empty<double>();
            A[1] = new[] { 1d / 5d };
            A[2] = new[] { 3d / 40d, 9d / 40d };
            A[3] = new[] { 44d / 45d, -56d / 15d, 32d / 9d };
            A[4] = new[] { 19372d / 6561d, -25360d / 2187d, 64448d / 6561d, -212d / 729d };
            A[5] = new[] { 9017d / 3168d, -355d / 33d, 46732d / 5247d, 49d / 176d, -5103d / 18656d };
            A[6] = new[] { 35d / 384d, 0d, 500d / 1113d, 125d / 192d, -2187d / 6784d, 11d / 84d };
            B = new[] { 35d / 384d, 0d, 500d / 1113d, 125d / 192d, -2187d / 6784d, 11d / 84d, 0d };
            E = new[]
            {
                71d / 57600d, 0d, -71d / 16695d, 71d / 1920d,
                -17253d / 339200d, 22d / 525d, -1d / 40d
            };
            stages = 7;
            order = 5;
        }

        var kp = new double[stages][];
        var kv = new double[stages][];
        for (int s = 0; s < stages; s++)
        {
            kp[s] = new double[length];
            kv[s] = new double[length];
        }

        double T = years * StudyYear;
        double maxStep = 86400d;
        const double minStep = 1e-6d;
        double t = 0d;
        double h = Math.Min(maxStep, T);
        double nextSample = sampleEvery > 0d ? sampleEvery : double.PositiveInfinity;
        var samples = sampleEvery > 0d ? new List<StudySample>() : null;
        long calls = 0L;
        long steps = 0L;
        long guard = 0L;
        var sw = Stopwatch.StartNew();
        StudyAcc(p, a, nb.Mu);
        calls++;

        while (t < T - 1e-9d)
        {
            if (++guard > 5000000L)
            {
                throw new InvalidOperationException(
                    "StudyRefRunAdaptive step-loop guard at t="
                    + t.ToString("R", CultureInfo.InvariantCulture));
            }

            double hStep = Math.Min(h, T - t);
            if (samples != null && nextSample > t && nextSample - t < hStep)
            {
                hStep = nextSample - t;
            }

            for (int s = 0; s < stages; s++)
            {
                if (s == 0)
                {
                    for (int i = 0; i < length; i++)
                    {
                        kp[0][i] = v[i];
                        kv[0][i] = a[i];
                    }

                    continue;
                }

                for (int i = 0; i < length; i++)
                {
                    double dp = 0d;
                    double dv = 0d;
                    for (int j = 0; j < s; j++)
                    {
                        double aa = A[s][j];
                        if (aa == 0d)
                        {
                            continue;
                        }

                        dp += aa * kp[j][i];
                        dv += aa * kv[j][i];
                    }

                    pStage[i] = p[i] + (hStep * dp);
                    vStage[i] = v[i] + (hStep * dv);
                    kp[s][i] = vStage[i];
                }

                StudyAcc(pStage, asStage, nb.Mu);
                calls++;
                for (int i = 0; i < length; i++)
                {
                    kv[s][i] = asStage[i];
                }
            }

            for (int i = 0; i < length; i++)
            {
                double dp = 0d;
                double dv = 0d;
                for (int j = 0; j < stages; j++)
                {
                    double b = B[j];
                    if (b == 0d)
                    {
                        continue;
                    }

                    dp += b * kp[j][i];
                    dv += b * kv[j][i];
                }

                pNew[i] = p[i] + (hStep * dp);
                vNew[i] = v[i] + (hStep * dv);
            }

            double err = 0d;
            for (int body = 0; body < n; body++)
            {
                double normP = 0d;
                double normV = 0d;
                double errP = 0d;
                double errV = 0d;
                for (int c = 0; c < 3; c++)
                {
                    int i = (3 * body) + c;
                    normP += pNew[i] * pNew[i];
                    normV += vNew[i] * vNew[i];
                    double ep = 0d;
                    double ev = 0d;
                    for (int j = 0; j < stages; j++)
                    {
                        double e = E[j];
                        if (e == 0d)
                        {
                            continue;
                        }

                        ep += e * kp[j][i];
                        ev += e * kv[j][i];
                    }

                    errP += (hStep * ep) * (hStep * ep);
                    errV += (hStep * ev) * (hStep * ev);
                }

                double scaleP = posAtol + (rtol * Math.Sqrt(normP));
                double scaleV = velAtol + (rtol * Math.Sqrt(normV));
                err = Math.Max(err, Math.Max(Math.Sqrt(errP) / scaleP, Math.Sqrt(errV) / scaleV));
            }

            if (!double.IsFinite(err))
            {
                throw new InvalidOperationException(
                    "Planet adaptive non-finite error at t=" + t.ToString("R", CultureInfo.InvariantCulture));
            }

            if (err <= 1d)
            {
                Array.Copy(pNew, p, length);
                Array.Copy(vNew, v, length);
                StudyAcc(p, a, nb.Mu);
                calls++;
                t += hStep;
                steps++;
                while (samples != null && t >= nextSample - 1e-9d)
                {
                    samples.Add(new StudySample(nextSample, (double[])p.Clone(), (double[])v.Clone()));
                    nextSample += sampleEvery;
                }

                double factor = Math.Min(5d, Math.Max(0.2d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-12d), 1d / order)));
                h = Math.Min(maxStep, Math.Max(minStep, hStep * factor));
            }
            else
            {
                double factor = Math.Min(0.5d, Math.Max(0.1d,
                    0.9d * Math.Pow(1d / Math.Max(err, 1e-12d), 1d / order)));
                h = Math.Max(minStep, hStep * factor);
            }
        }

        sw.Stop();
        return new StudyRefResult
        {
            P = p,
            V = v,
            Samples = samples,
            Calls = calls,
            Ms = sw.Elapsed.TotalMilliseconds
        };
    }

    private static bool StudyPlanetSamplesAligned(
        List<StudySample> candidate, List<StudySample> reference)
    {
        if (candidate == null || reference == null || candidate.Count != reference.Count)
        {
            return false;
        }

        for (int s = 0; s < candidate.Count; s++)
        {
            double scale = Math.Max(1d, Math.Abs(reference[s].T));
            if (Math.Abs(candidate[s].T - reference[s].T) > 1e-9d * scale)
            {
                return false;
            }
        }

        return true;
    }

    private static double StudyPlanetMaxRel(
        List<StudySample> candidate, List<StudySample> reference, int i, int j)
    {
        // Compare sample-by-sample only when the time grids are identical. The
        // old Math.Min silently paired a candidate's final partial sample with
        // the reference's last 30-day checkpoint, which is a different time.
        if (!StudyPlanetSamplesAligned(candidate, reference))
        {
            return double.NaN;
        }

        double worst = 0d;
        for (int s = 0; s < candidate.Count; s++)
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

    private sealed class StudyPlanetExpectation
    {
        public string BestMaxInner;
        public string CheapestAt1m;
        public string CheapestAt10m;
        /// <summary>Recorded cheapest call count; 0 means no budget is recorded.</summary>
        public long CheapestAt1mCalls;
        public long CheapestAt10mCalls;
    }

    // Explicit per-horizon, per-system expectations. The old code switched on
    // `years >= 10` and applied the same 10-year winner to the 100-year table,
    // which is not true: the two horizons have different reference floors and
    // different cheap rows. A new table call without an entry fails instead of
    // silently skipping its claim.
    private static readonly Dictionary<string, StudyPlanetExpectation> StudyPlanetExpectations =
        new Dictionary<string, StudyPlanetExpectation>(StringComparer.Ordinal)
        {
            ["Real solar 7-body 10 y"] = new StudyPlanetExpectation
            {
                BestMaxInner = "Y6C/1800",
                CheapestAt1m = "DOP853 study-adaptive",
                CheapestAt10m = "DOP853 study-adaptive",
                CheapestAt1mCalls = 114648L,
                CheapestAt10mCalls = 114648L
            },
            ["Custom 10 y"] = new StudyPlanetExpectation
            {
                BestMaxInner = "Y6C/1800",
                CheapestAt1m = "Y6C/3600",
                CheapestAt10m = "DOP853 study-adaptive",
                CheapestAt1mCalls = 613742L,
                CheapestAt10mCalls = 162059L
            },
            ["Real solar 7-body 100 y"] = new StudyPlanetExpectation
            {
                BestMaxInner = "DOP853 study-adaptive",
                CheapestAt1m = null,
                CheapestAt10m = "DOP853 study-adaptive"
            },
            ["Custom 100 y"] = new StudyPlanetExpectation
            {
                BestMaxInner = "Y6C/1800",
                CheapestAt1m = null,
                CheapestAt10m = "Y6C/3600"
            }
        };

    private static void StudyPlanetTable(
        string label, StudyNBody nb, double years)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + label + ": " + years + " y, independent "
            + "dd-Taylor K12 reference ===");
        double sampleEvery = 30d * 86400d;
        // The two dd-Taylor references are independent and dominate the runtime
        // of the 10/100-year tables. Run them together instead of serially.
        StudyRefResult reference = null;
        StudyRefResult alternative = null;
        Parallel.Invoke(
            () => reference = StudyPlanetIndependentReference(nb, years, sampleEvery),
            () => alternative = StudyPlanetIndependentReferenceAlt(nb, years, sampleEvery));
        bool referenceAligned = StudyPlanetSamplesAligned(reference.Samples, alternative.Samples);
        Check(referenceAligned, label + " reference/alternative sample alignment",
            referenceAligned
                ? "aligned"
                : "reference samples "
                  + (reference.Samples == null ? "missing" : reference.Samples.Count.ToString(
                      System.Globalization.CultureInfo.InvariantCulture))
                  + " vs alternative "
                  + (alternative.Samples == null ? "missing" : alternative.Samples.Count.ToString(
                      System.Globalization.CultureInfo.InvariantCulture)));
        double referenceSelf = 0d;
        for (int i = 0; i < nb.Count; i++)
        {
            for (int j = 0; j < i; j++)
            {
                referenceSelf = Math.Max(referenceSelf, StudyRel(reference.P, i, j, alternative.P));
                if (referenceAligned)
                {
                    double sampleSelf = StudyPlanetMaxRel(
                        reference.Samples, alternative.Samples, i, j);
                    if (double.IsFinite(sampleSelf))
                    {
                        referenceSelf = Math.Max(referenceSelf, sampleSelf);
                    }
                }
            }
        }

        // A bit-identical pair is not zero uncertainty: the reference still has
        // the double roundoff floor of its barycentric state.
        double referenceScale = 0d;
        for (int i = 0; i < reference.P.Length; i++)
        {
            referenceScale = Math.Max(referenceScale, Math.Abs(reference.P[i]));
        }

        double referenceRoundoff = ToleranceReference.FloatingPointFloor
            * Math.Max(referenceScale, 1d);
        referenceSelf = Math.Max(referenceSelf, referenceRoundoff);

        Console.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "reference calls={0:N0}, {1:F0} ms; self-convergence={2:E3} m",
            reference.Calls, reference.Ms, referenceSelf));
        Console.WriteLine("ranking floor: " + referenceSelf.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
            + " m (independent reference self-convergence)");
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
        Console.WriteLine("{" + "system,-24} {" + "method,-16} {" + "h, s,6} {" + "calls,12} {" +
            "inner, m,11} {" + "mid, m,11} {" + "outer, m,11} {" + "maxInner, m,11} {" +
            "maxOuter, m,11} {" + "maxAll, m,11} {" + "dE/E,9} {" + "maxdE,9} {" +
            "inv,9} {" + "ms(par),8}");
        // One symmetric grid for every fixed-step family: an asymmetric grid
        // cannot support a Pareto claim (a method "wins" because it alone was
        // given a fine step). The adaptive rows use the study controller, not
        // SpacecraftOrbitIntegrator's production controller; they are named
        // accordingly and are not presented as production rows.
        var rows = new[]
        {
            ("RK4", 1800d),
            ("RK4", 3600d),
            ("RK4", 7200d),
            ("DOPRI5", 1800d),
            ("DOPRI5", 3600d),
            ("DOPRI5", 7200d),
            ("PEFRL", 1800d),
            ("PEFRL", 3600d),
            ("PEFRL", 7200d),
            ("Y6", 1800d),
            ("Y6", 3600d),
            ("Y6", 7200d),
            ("Y6C", 1800d),
            ("Y6C", 3600d),
            ("Y6C", 7200d),
            ("DOP853 study-adaptive", 0d),
            ("DOPRI5 study-adaptive", 0d)
        };
        // WH is deliberately absent: its drift assumes a prescribed central body
        // (the ship matrix covers it there). In this pure mutual-gravity table the
        // Sun moves, so a heliocentric WH would need a different splitting and
        // would not be the same model as the other rows.
        Console.WriteLine("WH-Y6: not applicable to the mutual-gravity planet table; see Test549 for its domain");
        // Sequential on purpose: the old Parallel.For wall-clock column was
        // thread-contention time, not method cost. Calls remain the reproducible
        // cost; ms is now a valid sequential measurement.
        var rowResults = new (double inner, double mid, double outer, double maxInner,
            double maxOuter, double maxAll, double de, double maxDe, double inv, long calls,
            double ms)[rows.Length];
        var rowNames = new string[rows.Length];
        var rowAligned = new bool[rows.Length];
        var rowAlignDetail = new string[rows.Length];
        // Rows are independent; each one is a full 10/100-year integration.
        // Calls stay deterministic; ms is now a wall-clock under contention and
        // is labeled as such in the output.
        Parallel.For(0, rows.Length, idx =>
        {
            (string method, double h) = rows[idx];
            StudyRefResult r = h > 0d
                ? StudyRefRunSampled(nb, method, h, years, sampleEvery)
                : StudyRefRunAdaptive(
                    nb, method.StartsWith("DOP853", StringComparison.Ordinal),
                    1e-6d, 1e-9d, 1e-12d, years, sampleEvery);
            double e = StudyEnergy(r.P, r.V, nb.Mu);
            double de = Math.Abs((e - e0) / e0);
            double inner = StudyRel(r.P, 2, 1, reference.P);
            double mid = StudyRel(r.P, 1, 0, reference.P);
            double outer = StudyRel(r.P, outerIndex, 0, reference.P);
            double maxInner = StudyPlanetMaxRel(r.Samples, reference.Samples, 2, 1);
            double maxOuter = StudyPlanetMaxRel(r.Samples, reference.Samples, outerIndex, 0);
            double maxDe = StudyPlanetMaxEnergyError(r.Samples, nb.Mu, e0);
            double inv = StudyPlanetMaxInvariantError(r.Samples, nb.Mu, mom0, ang0, sumMuV);
            bool aligned = StudyPlanetSamplesAligned(r.Samples, reference.Samples);
            double maxAll = aligned ? 0d : double.NaN;
            if (aligned)
            {
                for (int i = 0; i < nb.Count; i++)
                {
                    for (int j = 0; j < i; j++)
                    {
                        maxAll = Math.Max(maxAll,
                            StudyRel(r.P, i, j, reference.P));
                        maxAll = Math.Max(maxAll,
                            StudyPlanetMaxRel(r.Samples, reference.Samples, i, j));
                    }
                }
            }

            rowNames[idx] = method
                + (h > 0d ? "/" + h.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "");
            rowAligned[idx] = aligned;
            rowAlignDetail[idx] = aligned ? "aligned" : "candidate samples "
                + (r.Samples == null ? "missing" : r.Samples.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                + " vs reference "
                + (reference.Samples == null ? "missing" : reference.Samples.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            rowResults[idx] = (inner, mid, outer, maxInner, maxOuter, maxAll, de, maxDe, inv, r.Calls, r.Ms);
        });

        for (int idx = 0; idx < rows.Length; idx++)
        {
            Check(rowAligned[idx], label + " " + rowNames[idx] + " sample count/time alignment",
                rowAlignDetail[idx]);
        }
        // Convergence curve per fixed-step method: the old table printed one or
        // two steps per method and no observed order, so "best" had no slope
        // behind it. Skip a pair where either error is at the reference floor.
        Console.Write("observed order (maxInner):");
        foreach (string method in new[] { "RK4", "DOPRI5", "PEFRL", "Y6", "Y6C" })
        {
            for (int idx = 0; idx < rows.Length; idx++)
            {
                if (rows[idx].Item1 != method || rows[idx].Item2 <= 0d)
                {
                    continue;
                }

                for (int other = 0; other < rows.Length; other++)
                {
                    if (rows[other].Item1 != method || rows[other].Item2 != 2d * rows[idx].Item2)
                    {
                        continue;
                    }

                    // idx is the fine step, other is 2*h (coarse). Error must fall
                    // from coarse to fine for a positive observed order; Y6 can go
                    // the other way because roundoff accumulates with more steps.
                    double coarse = rowResults[other].maxInner;
                    double fine = rowResults[idx].maxInner;
                    if (double.IsFinite(coarse) && double.IsFinite(fine)
                        && coarse > referenceSelf && fine > referenceSelf && fine > 0d)
                    {
                        double order = Math.Log(coarse / fine, 2d);
                        Console.Write(" " + method + "/"
                            + rows[other].Item2.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                            + "->"
                            + rows[idx].Item2.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                            + "=" + order.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        Console.WriteLine();
        StudyPlanetExpectations.TryGetValue(label, out StudyPlanetExpectation expectation);
        Check(expectation != null, label + " planet expectation entry",
            expectation == null ? "missing per-horizon/per-system entry" : "present");

        double bestScore = double.PositiveInfinity;
        double bestOuterScore = double.PositiveInfinity;
        long bestCalls = long.MaxValue;
        long bestOuterCalls = long.MaxValue;
        int bestIndex = -1;
        int bestOuterIndex = -1;
        for (int idx = 0; idx < rows.Length; idx++)
        {
            (string method, double h) = rows[idx];
            var row = rowResults[idx];
            // Ranking cannot go below the reference's own convergence: a row whose
            // max error is smaller than the self-convergence is measuring noise.
            double score = double.IsFinite(row.maxInner)
                ? Math.Max(row.maxInner, referenceSelf) : double.PositiveInfinity;
            if (score < bestScore || (score == bestScore && row.calls < bestCalls))
            {
                bestScore = score;
                bestCalls = row.calls;
                bestIndex = idx;
            }

            double outerScore = double.IsFinite(row.maxOuter)
                ? Math.Max(row.maxOuter, referenceSelf) : double.PositiveInfinity;
            if (outerScore < bestOuterScore
                || (outerScore == bestOuterScore && row.calls < bestOuterCalls))
            {
                bestOuterScore = outerScore;
                bestOuterCalls = row.calls;
                bestOuterIndex = idx;
            }

            string hText = h > 0d ? h.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "adapt";
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,-24} {1,-16} {2,6} {3,12:N0} {4,11:E3} {5,11:E3} {6,11:E3} {7,11:E3} {8,11:E3} {9,11:E3} {10,9:E2} {11,9:E2} {12,9:E2} {13,8:F0}",
                label, method, hText, row.calls, row.inner, row.mid, row.outer,
                row.maxInner, row.maxOuter, row.maxAll, row.de, row.maxDe, row.inv, row.ms));
            Check(double.IsFinite(row.maxInner) && double.IsFinite(row.maxOuter)
                    && double.IsFinite(row.maxAll)
                    && double.IsFinite(row.maxDe) && double.IsFinite(row.inv),
                label + " " + method
                + (h > 0d
                    ? " h=" + h.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                    : " adaptive")
                + " metrics finite",
                "maxInner=" + row.maxInner.ToString("E2", System.Globalization.CultureInfo.InvariantCulture)
                + " maxOuter=" + row.maxOuter.ToString("E2", System.Globalization.CultureInfo.InvariantCulture)
                + " maxdE=" + row.maxDe.ToString("E2", System.Globalization.CultureInfo.InvariantCulture)
                + " inv=" + row.inv.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        }

        string NameOf(int idx)
        {
            return idx < 0 ? "none"
                : rows[idx].Item1
                  + (rows[idx].Item2 > 0d
                      ? "/" + rows[idx].Item2.ToString("F0", CultureInfo.InvariantCulture)
                      : "");
        }

        int IndexOf(string name)
        {
            for (int idx = 0; idx < rows.Length; idx++)
            {
                if (NameOf(idx) == name)
                {
                    return idx;
                }
            }

            return -1;
        }

        int y6Index = Array.FindIndex(rows, r => r.Item1 == "Y6" && Math.Abs(r.Item2 - 3600d) < 1e-9d);
        int y6cIndex = Array.FindIndex(rows, r => r.Item1 == "Y6C" && Math.Abs(r.Item2 - 3600d) < 1e-9d);
        if (y6Index >= 0 && y6cIndex >= 0)
        {
            double y6Err = rowResults[y6Index].maxInner;
            double y6cErr = rowResults[y6cIndex].maxInner;
            double ratio = Math.Max(y6Err, y6cErr)
                / Math.Max(Math.Min(y6Err, y6cErr), 1e-30d);
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Y6C vs Y6 at 3600 s: max inner {0:E3} vs {1:E3} m ({2:F1}x {3})",
                y6cErr, y6Err, ratio, y6cErr <= y6Err ? "better" : "worse"));
            // Compensated Y6 must not be worse than plain Y6 on the same system;
            // in practice it is tens of times better (measured 50-75x over 10 y).
            Check(y6cErr <= 1.5d * y6Err, label + " Y6C vs Y6 cross-check",
                y6cErr.ToString("E3", System.Globalization.CultureInfo.InvariantCulture) + " <= 1.5 x "
                + y6Err.ToString("E3", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (bestIndex >= 0)
        {
            var best = rowResults[bestIndex];
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "best maxInner: {0} maxInner={1:E3} m maxOuter={2:E3} m maxdE={3:E2} inv={4:E2}{5}",
                NameOf(bestIndex), best.maxInner, best.maxOuter, best.maxDe, best.inv,
                best.maxInner < referenceSelf ? " (at reference floor)" : ""));
            if (expectation != null && expectation.BestMaxInner != null)
            {
                // A new method may legitimately win; the lock is that the winner
                // is no worse than the recorded winner, not that its name is the
                // same. Requiring the name made every improvement a test failure.
                int expectedIndex = IndexOf(expectation.BestMaxInner);
                Check(expectedIndex >= 0, label + " recorded best row present",
                    expectedIndex >= 0 ? expectation.BestMaxInner : "missing " + expectation.BestMaxInner);
                if (expectedIndex >= 0)
                {
                    double expectedValue = rowResults[expectedIndex].maxInner;
                    Check(best.maxInner <= expectedValue * 1.05d + referenceSelf,
                        label + " best maxInner vs recorded winner",
                        NameOf(bestIndex) + " maxInner="
                        + best.maxInner.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                        + " m <= " + expectation.BestMaxInner + " maxInner="
                        + expectedValue.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                        + " m x1.05 + floor="
                        + referenceSelf.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                        + " m");
                }
            }
        }

        if (bestOuterIndex >= 0)
        {
            var bestOuter = rowResults[bestOuterIndex];
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "best maxOuter: {0} maxOuter={1:E3} m maxInner={2:E3} m{3}",
                NameOf(bestOuterIndex), bestOuter.maxOuter, bestOuter.maxInner,
                bestOuter.maxOuter < referenceSelf ? " (at reference floor)" : ""));
        }

        // The fixed table only names one "best" row. Rank every method by both
        // max-over-checkpoints metrics separately, with the reference's own
        // convergence as the floor, so a method that wins on the inner pair is
        // not silently presented as the outer-pair winner.
        void PrintPlanetPareto(string metricName, Func<int, double> metric)
        {
            Console.Write("planet Pareto (calls, " + metricName + "):");
            bool any = false;
            for (int idx = 0; idx < rows.Length; idx++)
            {
                double value = metric(idx);
                if (!double.IsFinite(value))
                {
                    continue;
                }

                any = true;
                double score = Math.Max(value, referenceSelf);
                bool dominated = false;
                for (int other = 0; other < rows.Length; other++)
                {
                    double otherValue = metric(other);
                    if (other == idx || !double.IsFinite(otherValue))
                    {
                        continue;
                    }

                    double qs = Math.Max(otherValue, referenceSelf);
                    if (rowResults[other].calls <= rowResults[idx].calls && qs <= score
                        && (rowResults[other].calls < rowResults[idx].calls || qs < score))
                    {
                        dominated = true;
                        break;
                    }
                }

                if (!dominated)
                {
                    Console.Write(" " + rows[idx].Item1
                        + (rows[idx].Item2 > 0d
                            ? "/" + rows[idx].Item2.ToString("F0", CultureInfo.InvariantCulture)
                            : "")
                        + "=" + score.ToString("E2", CultureInfo.InvariantCulture)
                        + "@" + rowResults[idx].calls.ToString("N0", CultureInfo.InvariantCulture));
                }
            }

            if (!any)
            {
                Console.Write(" none");
            }

            Console.WriteLine();
        }

        PrintPlanetPareto("max inner", idx => rowResults[idx].maxInner);
        PrintPlanetPareto("max outer", idx => rowResults[idx].maxOuter);
        PrintPlanetPareto("max all pairs", idx => rowResults[idx].maxAll);

        foreach (double target in new[] { 1d, 10d, 100d, 1000d })
        {
            if (referenceSelf > target)
            {
                Console.WriteLine("  <= " + target.ToString("G", CultureInfo.InvariantCulture)
                    + " m: not claimed (below reference floor "
                    + referenceSelf.ToString("E3", CultureInfo.InvariantCulture) + " m)");
                continue;
            }

            int cheapCalls = -1;
            int cheapMs = -1;
            double cheapCallsScore = double.PositiveInfinity;
            double cheapMsScore = double.PositiveInfinity;
            double effectiveTarget = target;
            for (int idx = 0; idx < rows.Length; idx++)
            {
                var row = rowResults[idx];
                // A row is only at the requested accuracy when EVERY relative
                // pair is. The old selection used maxInner alone and could claim
                // an outer/inner-planet failure as a "<= target" win.
                double trajectory = row.maxAll;
                if (!double.IsFinite(trajectory))
                {
                    continue;
                }

                double score = Math.Max(trajectory, referenceSelf);
                if (score > effectiveTarget)
                {
                    continue;
                }

                if (row.calls < (cheapCalls < 0 ? long.MaxValue : rowResults[cheapCalls].calls)
                    || (cheapCalls >= 0 && row.calls == rowResults[cheapCalls].calls
                        && score < cheapCallsScore))
                {
                    cheapCalls = idx;
                    cheapCallsScore = score;
                }

                if (row.ms < (cheapMs < 0 ? double.MaxValue : rowResults[cheapMs].ms)
                    || (cheapMs >= 0 && row.ms == rowResults[cheapMs].ms
                        && score < cheapMsScore))
                {
                    cheapMs = idx;
                    cheapMsScore = score;
                }
            }

            Console.WriteLine("  <= " + target.ToString("G", CultureInfo.InvariantCulture) + " m: calls "
                + NameOf(cheapCalls) + (cheapCalls >= 0
                    ? " @" + rowResults[cheapCalls].calls.ToString("N0", CultureInfo.InvariantCulture)
                    : "")
                + "; ms " + NameOf(cheapMs) + (cheapMs >= 0
                    ? " " + rowResults[cheapMs].ms.ToString("F0", CultureInfo.InvariantCulture) + " ms"
                    : ""));
            string expectedRow = target == 1d
                ? expectation?.CheapestAt1m
                : target == 10d ? expectation?.CheapestAt10m : null;
            if (expectedRow != null)
            {
                // A new method may be cheaper; the lock is that the cheapest row
                // is no more expensive than the recorded winner. An exact-name
                // check would fail every time a better integrator is added.
                int expectedIndex = IndexOf(expectedRow);
                Check(expectedIndex >= 0, label + " recorded cheapest row present",
                    expectedIndex >= 0 ? expectedRow : "missing " + expectedRow);
                if (expectedIndex >= 0)
                {
                    long expectedCalls = rowResults[expectedIndex].calls;
                    Check(cheapCalls >= 0
                            && rowResults[cheapCalls].calls <= expectedCalls * 1.1d,
                        label + " cheapest <= " + target.ToString("G", CultureInfo.InvariantCulture)
                        + " m cost vs recorded winner",
                        NameOf(cheapCalls) + (cheapCalls >= 0
                            ? " calls=" + rowResults[cheapCalls].calls.ToString("N0", CultureInfo.InvariantCulture)
                            : "") + " <= " + expectedRow + " calls="
                          + expectedCalls.ToString("N0", CultureInfo.InvariantCulture) + " x1.1"
                          + " (floor=" + referenceSelf.ToString("E3", CultureInfo.InvariantCulture) + " m)");
                }

                // A fixed recorded budget catches a cost regression even when the
                // expected row itself gets more expensive (the current-row check
                // above would then move its own goalpost).
                long recordedCap = target == 1d
                    ? expectation.CheapestAt1mCalls
                    : target == 10d ? expectation.CheapestAt10mCalls : 0L;
                if (recordedCap > 0L)
                {
                    Check(cheapCalls >= 0
                            && rowResults[cheapCalls].calls <= recordedCap * 1.1d,
                        label + " cheapest <= " + target.ToString("G", CultureInfo.InvariantCulture)
                        + " m recorded cost budget",
                        NameOf(cheapCalls) + (cheapCalls >= 0
                            ? " calls=" + rowResults[cheapCalls].calls.ToString("N0", CultureInfo.InvariantCulture)
                            : "") + " <= recorded " + recordedCap.ToString("N0", CultureInfo.InvariantCulture)
                          + " x1.1");
                }
            }
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
            // The reference's own uncertainty is part of the budget: a 2.2 m best
            // against a 2.3 m floor is not a 2.2 m result.
            Check(best.maxInner + referenceSelf < innerGate, label + " best max inner",
                best.maxInner.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                + " + " + referenceSelf.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                + " m < " + innerGate.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) + " m");
            Check(best.maxOuter + referenceSelf < outerGate, label + " best max outer",
                best.maxOuter.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                + " + " + referenceSelf.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                + " m < " + outerGate.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) + " m");
            Check(best.maxDe < energyGate, label + " best max dE/E",
                best.maxDe.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
            Check(best.inv < invariantGate, label + " best max invariant",
                best.inv.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            Check(false, label + " best row", "none");
        }

        if (bestOuterIndex >= 0)
        {
            // The outer-pair winner is a separate ranking; gating only the
            // inner-pair winner's maxOuter let an outer-pair regression pass.
            var bestOuter = rowResults[bestOuterIndex];
            Check(bestOuter.maxOuter + referenceSelf < outerGate,
                label + " best max outer row",
                NameOf(bestOuterIndex) + ": "
                + bestOuter.maxOuter.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                + " + " + referenceSelf.ToString("E3", System.Globalization.CultureInfo.InvariantCulture)
                + " m < " + outerGate.ToString("G3", System.Globalization.CultureInfo.InvariantCulture) + " m");
        }
        else
        {
            Check(false, label + " best max outer row", "none");
        }
    }

    private static void StudyBake(
        string label, StarSystem sys, StudyNBody nb, double years, double worstGateMeters = 0d)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + label + ": bake " + years + " y, DOP853 substeps (900 s cap), degree 12, quant 2 m ===");
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

