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
    // Valid=true only when a pair of step levels is asymptotic: then PositionError
    // is a Richardson error estimate. A non-asymptotic run may still return
    // Resolved=true with FinestDelta as a RESOLUTION (the difference between the two
    // finest steps), never as an 8th-order error bound; such a reference is usable
    // only for consistency checks, and accuracy gates must not treat it as proof.
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
        /// <summary>True only when the finest pair is asymptotic: the error is an 8th-order estimate.</summary>
        public bool Valid;
        /// <summary>True when that estimate came from a converging pair (same as Valid).</summary>
        public bool Asymptotic;
        /// <summary>Non-asymptotic reference usable only as a resolution/consistency check, never as an error bound.</summary>
        public bool Resolved;
        public string Detail;
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
        StudyShipMatrixCase c, double h, out long calls)
    {
        var acc = new StudyShipMatrixAcc(c);
        var state = new SpacecraftIntegrationState
        {
            Position = c.P0,
            Velocity = c.V0,
            Mass = c.Mass0
        };
        double[] edges = StudyShipRefBreakpoints(c);
        double t = edges[0];
        double edge = Math.Max(1e-9d, h * 1e-9d);
        for (int segment = 0; segment < edges.Length - 1; segment++)
        {
            double tEnd = edges[segment + 1];
            double span = tEnd - t;
            if (span <= 0d)
            {
                continue;
            }

            // Do not put a DOP853 stage exactly on a throttle breakpoint: the
            // one-sided value of the RHS there is not the value the quadrature
            // needs, and that first-order boundary error never decreases with h.
            // Midpoints of a C0 force are integrated at the expected order; the
            // tiny edge steps are below the target accuracy.
            if (span > 3d * edge)
            {
                state = StudyShipRefDop853Step(acc, state, t, edge);
                t += edge;
                double mainEnd = tEnd - edge;
                double mainSpan = mainEnd - t;
                long full = (long)Math.Floor(mainSpan / h);
                double rem = mainSpan - (full * h);
                for (long n = 0; n < full; n++)
                {
                    state = StudyShipRefDop853Step(acc, state, t, h);
                    t += h;
                }

                if (rem > 1e-15d)
                {
                    state = StudyShipRefDop853Step(acc, state, t, rem);
                    t += rem;
                }

                state = StudyShipRefDop853Step(acc, state, t, edge);
                t = tEnd;
            }
            else
            {
                state = StudyShipRefDop853Step(acc, state, t, span);
                t = tEnd;
            }
        }

        calls = acc.Calls;
        return state;
    }

    private static StudyShipCertifiedRef StudyShipCertifiedReference(
        StudyShipMatrixCase c, double stepsPerTau = 24d, int levels = 4)
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
            for (int i = 0; i < levels; i++)
            {
                hs[i] = h0 / (1 << i);
                states[i] = StudyShipRefDop853Fixed(c, hs[i], out calls[i]);
            }

            var d = new double[levels - 1];
            var dv = new double[levels - 1];
            for (int i = 0; i < levels - 1; i++)
            {
                d[i] = (states[i].Position - states[i + 1].Position).Magnitude;
                dv[i] = (states[i].Velocity - states[i + 1].Velocity).Magnitude;
            }

            // The answer is the finest state. The finest pair may be roundoff-
            // limited (its ratio is not asymptotic); in that case the reference is
            // still usable only if its resolution (the finest delta) is far below
            // the production target. It is then reported as "resolved", not as an
            // 8th-order-certified error estimate.
            int chosen = -1;
            for (int i = levels - 3; i >= 0; i--)
            {
                double candidate = d[i] / Math.Max(d[i + 1], 1e-30d);
                if (candidate > 64d && candidate < 1024d)
                {
                    chosen = i;
                    break;
                }
            }

            bool asymptotic = chosen >= 0;
            double ratioFine = d[levels - 3] / Math.Max(d[levels - 2], 1e-30d);
            // Ratio <= 1 means the two finest solutions are diverging. FinestDelta
            // then measures disagreement, not resolution, and cannot certify anything.
            bool resolved = ratioFine > 1d
                && d[levels - 2] <= 0.05d * Math.Max(c.AccuracyTarget, 1d);
            bool valid = asymptotic || resolved;
            double ratio = ratioFine;
            double errPos = double.NaN;
            double errVel = double.NaN;
            if (asymptotic)
            {
                ratio = d[chosen] / Math.Max(d[chosen + 1], 1e-30d);
                double order = Math.Log(Math.Max(ratio, 1e-30d), 2d);
                double scale = Math.Pow(
                    hs[levels - 1] / Math.Max(hs[chosen + 1], 1e-30d), order);
                double richardson = Math.Max(ratio - 1d, 1d);
                // Richardson only. d_finest is a separate resolution, not the error
                // of the finest solution (it is ~2^p times larger when asymptotic).
                errPos = d[chosen] * scale / richardson;
                errVel = dv[chosen] * scale / richardson;
            }

            double orderUsed = Math.Log(Math.Max(ratio, 1e-30d), 2d);
            double roundoffPos = ToleranceReference.FloatingPointFloor
                * Math.Max(states[levels - 1].Position.Magnitude, 1d);
            double roundoffVel = ToleranceReference.FloatingPointFloor
                * Math.Max(states[levels - 1].Velocity.Magnitude, 1d);
            var r = new StudyShipCertifiedRef
            {
                P = states[levels - 1].Position,
                V = states[levels - 1].Velocity,
                M = states[levels - 1].Mass,
                PositionError = asymptotic ? Math.Max(errPos, roundoffPos) : double.NaN,
                VelocityError = asymptotic ? Math.Max(errVel, roundoffVel) : double.NaN,
                FinestDelta = d[levels - 2],
                Ratio = ratio,
                Order = orderUsed,
                Level = levels - 1,
                Valid = asymptotic,
                Asymptotic = asymptotic,
                Resolved = resolved,
                Detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "fixed DOP853 h={0:G6} s, ratio={1:F2} order={2:F2}{3}, est {4:E2} m / {5:E1} m/s, finest d={6:E2} m{7}{8}",
                    hs[levels - 1], ratio, orderUsed,
                    asymptotic ? "" : " resolved (non-asymptotic; resolution only)",
                    asymptotic ? Math.Max(errPos, roundoffPos) : double.NaN,
                    asymptotic ? Math.Max(errVel, roundoffVel) : double.NaN,
                    d[levels - 2],
                    attempt > 0 ? " retry " + attempt.ToString(CultureInfo.InvariantCulture) : "",
                    valid ? "" : " [INVALID]")
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
