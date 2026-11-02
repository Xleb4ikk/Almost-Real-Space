using System;
using System.Collections.Generic;
using System.Globalization;
using Galilego.Core;

internal static partial class P1bTests
{
    // ------------------------------------------------------------------
    // Certified reference for the spacecraft matrix.
    //
    // The reference is a FIXED-STEP DOP853 run in the central-body frame. Fixed
    // steps make Richardson extrapolation meaningful (the adaptive production
    // stepper changes its own step sequence when the tolerance changes, so
    // "tighter tolerance - looser tolerance" is not an error estimator).
    //
    // Valid=true only when a pair of step levels is asymptotic and the answer level
    // is self-consistent: then PositionError is a bound, either the Richardson
    // estimate or (when the finest level stalls) the observed disagreement with the
    // next finer level, whichever is larger. A run with no converging pair at all is
    // UNRESOLVED and may only be used for consistency checks; accuracy gates must
    // not treat FinestDelta as an error bound.
    // ------------------------------------------------------------------

    private sealed class StudyShipCertifiedRef
    {
        public Vector3d P;
        public Vector3d V;
        public double M;
        public double PositionError;
        public double VelocityError;
        public double FinestDelta;
        public double Ratio;
        public double Order;
        public int Level;
        /// <summary>
        /// True when a converging pair exists and the answer level is self-consistent.
        /// PositionError is then a bound: Richardson extrapolation raised to the
        /// observed disagreement with the next finer level when that is larger.
        /// </summary>
        public bool Valid;
        /// <summary>
        /// True when the answer level is certified (same as Valid). Floored says
        /// whether PositionError was raised above the Richardson estimate.
        /// </summary>
        public bool Asymptotic;
        /// <summary>True when PositionError was raised by the finest observed disagreement.</summary>
        public bool Floored;
        /// <summary>
        /// A converging pair exists and the observed floor is below 10% of the
        /// accuracy target. This is the flag the case gate uses to accept the
        /// reference for ranking; it is not an 8th-order certificate.
        /// </summary>
        public bool Resolved;
        public string Detail;
    }

    private static double[] StudyShipRefMergeEdges(double[] edges, double[] extra)
    {
        if (extra == null || extra.Length == 0)
        {
            return edges;
        }

        var all = new List<double>(edges.Length + extra.Length);
        all.AddRange(edges);
        all.AddRange(extra);
        all.Sort();
        var merged = new double[all.Count];
        int n = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (n == 0 || all[i] - merged[n - 1] > 1e-9d)
            {
                merged[n++] = all[i];
            }
        }

        Array.Resize(ref merged, n);
        return merged;
    }

    private static double[] StudyShipRefBreakpoints(StudyShipMatrixCase c)
    {
        double t0 = StudyShipT0;
        double t1 = t0 + c.Seconds;
        var edges = new List<double> { t0 };
        if (c.Thrust)
        {
            double start = c.ThrustStart;
            double end = start + c.ThrustSeconds;
            edges.Add(start);
            if (c.ThrustRampSeconds > 0d)
            {
                edges.Add(start + c.ThrustRampSeconds);
                edges.Add(end - c.ThrustRampSeconds);
            }

            edges.Add(end);
        }

        edges.Add(t1);
        edges.Sort();
        var result = new List<double>();
        foreach (double t in edges)
        {
            if (t < t0 - 1e-9d || t > t1 + 1e-9d)
            {
                continue;
            }

            if (result.Count == 0 || Math.Abs(t - result[result.Count - 1]) > 1e-9d)
            {
                result.Add(t);
            }
        }

        if (result.Count == 0 || Math.Abs(result[0] - t0) > 1e-9d)
        {
            result.Insert(0, t0);
        }

        if (Math.Abs(result[result.Count - 1] - t1) > 1e-9d)
        {
            result.Add(t1);
        }

        return result.ToArray();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static SpacecraftIntegrationState StudyShipRefDop853Step(
        StudyShipMatrixAcc acc, SpacecraftIntegrationState state, double t, double h)
    {
        var k = new SpacecraftIntegrationState[12];
        k[0] = acc.Evaluate(state, t);
        for (int i = 1; i < 12; i++)
        {
            SpacecraftIntegrationState stage = state;
            double[] row = DOP853Coefficients.A[i];
            for (int j = 0; j < i; j++)
            {
                double a = row[j];
                if (a != 0d)
                {
                    stage += k[j] * (h * a);
                }
            }

            k[i] = acc.Evaluate(stage, t + (DOP853Coefficients.C[i] * h));
        }

        SpacecraftIntegrationState next = state;
        for (int i = 0; i < 12; i++)
        {
            next += k[i] * (h * DOP853Coefficients.B[i]);
        }

        return next;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static SpacecraftIntegrationState StudyShipRefDop853Fixed(
        StudyShipMatrixCase c, double h, out long calls,
        double[] sampleTimes = null,
        Vector3d[] samplePositions = null,
        Vector3d[] sampleVelocities = null)
    {
        var acc = new StudyShipMatrixAcc(c);
        var state = new SpacecraftIntegrationState
        {
            Position = c.P0,
            Velocity = c.V0,
            Mass = c.Mass0
        };
        double[] breakpoints = StudyShipRefBreakpoints(c);
        double[] edges = StudyShipRefMergeEdges(breakpoints, sampleTimes);
        double edge = Math.Max(1e-9d, h * 1e-9d);
        bool IsThrustBoundary(double time)
        {
            if (!c.Thrust
                || !Array.Exists(breakpoints, b => Math.Abs(b - time) <= 1e-9d))
            {
                return false;
            }

            double thrustStart = c.ThrustStart;
            double thrustEnd = thrustStart + c.ThrustSeconds;
            if (Math.Abs(time - thrustStart) <= 1e-9d
                || Math.Abs(time - thrustEnd) <= 1e-9d)
            {
                return true;
            }

            return c.ThrustRampSeconds > 0d
                && (Math.Abs(time - (thrustStart + c.ThrustRampSeconds)) <= 1e-9d
                    || Math.Abs(time - (thrustEnd - c.ThrustRampSeconds)) <= 1e-9d);
        }

        double t = edges[0];
        int sample = 0;
        for (int segment = 0; segment < edges.Length - 1; segment++)
        {
            double tEnd = edges[segment + 1];
            double span = tEnd - t;
            if (span <= 0d)
            {
                continue;
            }

            bool startThrust = IsThrustBoundary(t);
            bool endThrust = IsThrustBoundary(tEnd);
            if (span <= 3d * edge)
            {
                state = StudyShipRefDop853Step(acc, state, t, span);
                t = tEnd;
            }
            else
            {
                double mainStart = t;
                if (startThrust)
                {
                    double edgeEnd = t + edge;
                    double edgeStep = edgeEnd - t;
                    if (edgeStep > 0d)
                    {
                        state = StudyShipRefDop853Step(acc, state, t, edgeStep);
                    }

                    mainStart = edgeEnd;
                }

                double mainEnd = endThrust ? tEnd - edge : tEnd;
                double mainSpan = mainEnd - mainStart;
                long n = Math.Max(1L, (long)Math.Round(mainSpan / h));
                double step = mainSpan / n;
                for (long i = 0; i < n; i++)
                {
                    double tn = mainStart + (i * step);
                    state = StudyShipRefDop853Step(acc, state, tn, step);
                }

                t = mainEnd;
                if (endThrust)
                {
                    double tail = tEnd - mainEnd;
                    if (tail > 0d)
                    {
                        state = StudyShipRefDop853Step(acc, state, mainEnd, tail);
                    }

                    t = tEnd;
                }
            }

            // Every forced end is either a throttle edge, a checkpoint or the
            // horizon; record the checkpoints at their exact times.
            while (sampleTimes != null && sample < sampleTimes.Length
                && Math.Abs(sampleTimes[sample] - tEnd) <= 1e-6d)
            {
                if (samplePositions != null)
                {
                    samplePositions[sample] = state.Position;
                }

                if (sampleVelocities != null)
                {
                    sampleVelocities[sample] = state.Velocity;
                }

                sample++;
            }
        }

        calls = acc.Calls;
        return state;
    }

    private static StudyShipCertifiedRef StudyShipCertifiedReference(
        StudyShipMatrixCase c, double stepsPerTau = 24d, int levels = 5,
        double[] sampleTimes = null,
        Vector3d[] samplePositions = null,
        Vector3d[] sampleVelocities = null)
    {
        if (levels < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(levels), "at least 3 step levels are required");
        }

        if (DOP853Coefficients.A.Length < 12 || DOP853Coefficients.B.Length < 12
            || DOP853Coefficients.C.Length < 12)
        {
            throw new InvalidOperationException(
                "DOP853 arrays: A=" + DOP853Coefficients.A.Length
                + " B=" + DOP853Coefficients.B.Length
                + " C=" + DOP853Coefficients.C.Length);
        }

        for (int i = 0; i < 12; i++)
        {
            if (DOP853Coefficients.A[i].Length < i)
            {
                throw new InvalidOperationException(
                    "DOP853 A row " + i + " length " + DOP853Coefficients.A[i].Length);
            }
        }

        StudyShipCertifiedRef last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            // If the step is not in the asymptotic regime, halve it. Never turn a
            // non-asymptotic finest delta into a fake error estimate: it is the
            // difference between two solutions, not the error of either.
            double h0 = Math.Max(1d, c.Tau / (stepsPerTau * (1 << attempt)));
            var hs = new double[levels];
            var states = new SpacecraftIntegrationState[levels];
            var calls = new long[levels];
            var samplePos = sampleTimes != null ? new Vector3d[levels][] : null;
            var sampleVel = sampleTimes != null ? new Vector3d[levels][] : null;
            for (int i = 0; i < levels; i++)
            {
                hs[i] = h0 / (1 << i);
                if (sampleTimes != null)
                {
                    samplePos[i] = new Vector3d[sampleTimes.Length];
                    sampleVel[i] = new Vector3d[sampleTimes.Length];
                    states[i] = StudyShipRefDop853Fixed(c, hs[i], out calls[i],
                        sampleTimes, samplePos[i], sampleVel[i]);
                }
                else
                {
                    states[i] = StudyShipRefDop853Fixed(c, hs[i], out calls[i]);
                }
            }

            var d = new double[levels - 1];
            var dv = new double[levels - 1];
            for (int i = 0; i < levels - 1; i++)
            {
                if (sampleTimes != null)
                {
                    double worstPos = 0d;
                    double worstVel = 0d;
                    for (int s = 0; s < sampleTimes.Length; s++)
                    {
                        worstPos = Math.Max(worstPos,
                            (samplePos[i][s] - samplePos[i + 1][s]).Magnitude);
                        worstVel = Math.Max(worstVel,
                            (sampleVel[i][s] - sampleVel[i + 1][s]).Magnitude);
                    }

                    d[i] = worstPos;
                    dv[i] = worstVel;
                }
                else
                {
                    d[i] = (states[i].Position - states[i + 1].Position).Magnitude;
                    dv[i] = (states[i].Velocity - states[i + 1].Velocity).Magnitude;
                }
            }

            if (string.Equals(Environment.GetEnvironmentVariable("P1B_CERT_TRACE"), "1",
                StringComparison.Ordinal))
            {
                Console.Write("cert trace " + c.Label + " attempt " + attempt + " h="
                    + hs[levels - 1].ToString("G6", CultureInfo.InvariantCulture));
                for (int i = 0; i < d.Length; i++)
                {
                    Console.Write(" d" + i + "=" + d[i].ToString("E2", CultureInfo.InvariantCulture));
                    if (i > 0)
                    {
                        Console.Write(" r" + i + "=" + (d[i - 1] / Math.Max(d[i], 1e-30d))
                            .ToString("F2", CultureInfo.InvariantCulture));
                    }
                }

                Console.WriteLine();
            }

            // The answer need not be the very finest state: when the extra finest
            // level stalls or disagrees, return the previous consistent level and
            // raise the bound to that observed disagreement (see observedPos below).
            // Inspect every pair, including the finest one. A pair whose smaller
            // delta is at the double floor is not a convergence measurement: a zero
            // delta means the two runs agreed bit-for-bit, not that the answer is
            // exact. Such a pair can neither certify 8th order nor be called a
            // resolution; the extrapolated coarse pair plus the roundoff floor is
            // the honest bound in that case.
            double roundoffPos = ToleranceReference.FloatingPointFloor
                * Math.Max(states[levels - 1].Position.Magnitude, 1d);
            double roundoffVel = ToleranceReference.FloatingPointFloor
                * Math.Max(states[levels - 1].Velocity.Magnitude, 1d);
            int finestPair = levels - 3;
            int chosen = -1;
            double bestRatio = double.NaN;
            for (int i = finestPair; i >= 0; i--)
            {
                if (!(d[i] > roundoffPos) || !(d[i + 1] > roundoffPos))
                {
                    continue;
                }

                double candidate = d[i] / Math.Max(d[i + 1], 1e-30d);
                if (!double.IsFinite(bestRatio) || candidate > bestRatio)
                {
                    bestRatio = candidate;
                }

                // An 8th-order pair gives ~256. Accept anything from 32 up: the
                // coarse end can overshoot (a 1,284 ratio is still a clean
                // convergence measurement), and the observed finest-level
                // disagreement below caps the extrapolation in any case.
                if (candidate >= 32d)
                {
                    chosen = i;
                    break;
                }
            }

            bool asymptotic = chosen >= 0;
            double finestDelta = d[finestPair + 1];
            // A coarse asymptotic pair certifies the sequence down to that pair.
            // If the extra finest level is inconsistent (d_finest stops shrinking
            // or is roundoff-dominated), return the previous, consistent level
            // instead of the noisy extra one. This is the checked fallback that
            // keeps the answer and its error estimate at the same h.
            int answerLevel = levels - 1;
            if (!(finestDelta <= Math.Max(0.5d * d[finestPair], roundoffPos)))
            {
                answerLevel = levels - 2;
            }

            while (answerLevel > 1)
            {
                double answerDelta = d[answerLevel - 1];
                double coarserDelta = answerLevel > 1 ? d[answerLevel - 2] : double.PositiveInfinity;
                if (answerDelta <= Math.Max(0.5d * coarserDelta, roundoffPos))
                {
                    break;
                }

                answerLevel--;
            }

            SpacecraftIntegrationState answer = states[answerLevel];
            if (sampleTimes != null)
            {
                if (samplePositions != null)
                {
                    Array.Copy(samplePos[answerLevel], samplePositions, sampleTimes.Length);
                }

                if (sampleVelocities != null)
                {
                    Array.Copy(sampleVel[answerLevel], sampleVelocities, sampleTimes.Length);
                }
            }

            double answerRoundoffPos = ToleranceReference.FloatingPointFloor
                * Math.Max(answer.Position.Magnitude, 1d);
            double answerRoundoffVel = ToleranceReference.FloatingPointFloor
                * Math.Max(answer.Velocity.Magnitude, 1d);
            double ratio = asymptotic
                ? d[chosen] / Math.Max(d[chosen + 1], 1e-30d)
                : (double.IsFinite(bestRatio) ? bestRatio : double.NaN);
            double errPos = double.NaN;
            double errVel = double.NaN;
            if (asymptotic)
            {
                double order = Math.Log(Math.Max(ratio, 1e-30d), 2d);
                double scale = Math.Pow(
                    hs[answerLevel] / Math.Max(hs[chosen + 1], 1e-30d), order);
                double richardson = Math.Max(ratio - 1d, 1d);
                // Richardson only. d_finest is a separate resolution, not the error
                // of the finest solution (it is ~2^p times larger when asymptotic).
                errPos = d[chosen] * scale / richardson;
                errVel = dv[chosen] * scale / richardson;
            }

            // The answer level must still be self-consistent. The check below only
            // looks at coarser levels; a finest pair that stalls says nothing about
            // the accuracy of the returned state. The observed disagreement with the
            // next finer level is therefore a lower bound on the answer's error:
            //   - answer below the finest level: it is d[answerLevel] (S_A vs S_A+1);
            //   - answer at the finest level: it is d[A-1], divided by the observed
            //     convergence ratio only when that pair actually converged.
            double observedPos;
            double observedVel;
            if (answerLevel < levels - 1)
            {
                observedPos = d[answerLevel];
                observedVel = dv[answerLevel];
            }
            else
            {
                double finestRatio = answerLevel >= 2 && d[answerLevel - 1] > roundoffPos
                    ? d[answerLevel - 2] / Math.Max(d[answerLevel - 1], 1e-30d)
                    : double.NaN;
                observedPos = finestRatio >= 32d
                    ? d[answerLevel - 1] / Math.Max(finestRatio, 2d)
                    : d[answerLevel - 1];
                observedVel = finestRatio >= 32d
                    ? dv[answerLevel - 1] / Math.Max(finestRatio, 2d)
                    : dv[answerLevel - 1];
            }

            observedPos = Math.Max(observedPos, answerRoundoffPos);
            observedVel = Math.Max(observedVel, answerRoundoffVel);
            bool answerConsistent = answerLevel == 0
                || d[answerLevel - 1] <= Math.Max(
                    0.5d * (answerLevel > 1 ? d[answerLevel - 2] : double.PositiveInfinity),
                    answerRoundoffPos);
            bool valid = asymptotic && answerConsistent;
            bool floored = valid && observedPos > errPos * 1.5d;
            // A usable resolution needs a genuine converging pair (ratio >= 32) and
            // an observed floor well below the target. This is a ranking floor, not
            // an 8th-order certificate; the case-level gate still requires Valid.
            bool resolved = valid
                && observedPos <= 0.1d * Math.Max(c.AccuracyTarget, 1d);
            double orderUsed = Math.Log(Math.Max(ratio, 1e-30d), 2d);
            var r = new StudyShipCertifiedRef
            {
                P = answer.Position,
                V = answer.Velocity,
                M = answer.Mass,
                PositionError = valid ? Math.Max(errPos, observedPos) : double.NaN,
                VelocityError = valid ? Math.Max(errVel, observedVel) : double.NaN,
                // The actual difference between the two finest runs. It is a
                // resolution (how far apart the levels are), not the error of the
                // returned answer when the sequence converged.
                FinestDelta = levels > 1
                    ? Math.Max(d[levels - 2], answerRoundoffPos)
                    : double.NaN,
                Ratio = ratio,
                Order = orderUsed,
                Level = answerLevel,
                Valid = valid,
                Asymptotic = valid,
                Resolved = resolved,
                Floored = floored,
                Detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "fixed DOP853 h={0:G6} s, ratio={1:F2} order={2:F2}{3}, est {4:E2} m / {5:E1} m/s, finest d={6:E2} m{7}",
                    hs[answerLevel], ratio, orderUsed,
                    valid
                        ? floored
                            ? " (floor from finest disagreement)"
                            : ""
                        : " UNRESOLVED (no converging pair)",
                    valid ? Math.Max(errPos, observedPos) : double.NaN,
                    valid ? Math.Max(errVel, observedVel) : double.NaN,
                    levels > 1 ? Math.Max(d[levels - 2], answerRoundoffPos) : double.NaN,
                    attempt > 0 ? " retry " + attempt.ToString(CultureInfo.InvariantCulture) : "")
            };
            if (valid)
            {
                return r;
            }

            last = r;
        }

        return last;
    }

    private static string StudyShipRefTrace(StudyShipMatrixCase c, double stepsPerTau, int levels)
    {
        double h0 = Math.Max(1d, c.Tau / stepsPerTau);
        var hs = new double[levels];
        var states = new SpacecraftIntegrationState[levels];
        for (int i = 0; i < levels; i++)
        {
            hs[i] = h0 / (1 << i);
            states[i] = StudyShipRefDop853Fixed(c, hs[i], out _);
        }

        var text = new System.Text.StringBuilder();
        var dp = new double[levels - 1];
        for (int i = 0; i < levels - 1; i++)
        {
            dp[i] = (states[i].Position - states[i + 1].Position).Magnitude;
            text.Append(string.Format(
                CultureInfo.InvariantCulture,
                " h={0:G6} d={1:E2}",
                hs[i], dp[i]));
            if (i > 0)
            {
                text.Append(string.Format(
                    CultureInfo.InvariantCulture,
                    " r={0:F2}",
                    dp[i - 1] / Math.Max(dp[i], 1e-30d)));
            }
        }

        return text.ToString();
    }
}
